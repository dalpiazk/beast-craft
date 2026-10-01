using System;
using System.Numerics;
using SharpGLTF.Schema2;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>Evaluates a GltfSkinnedModel's node hierarchy at a given clip/time (looped) and turns
    /// the result into per-joint skin matrices. Walks the parent chain itself (see
    /// ComputeWorldMatrices's doc comment for why, and the GC numbers that drove the change) using each
    /// node's own Node.GetLocalTransform(animation, time), rather than SharpGLTF's
    /// Node.GetWorldMatrix(animation, time). All math here is System.Numerics (SharpGLTF's own
    /// convention, which matches MonoGame/XNA's row-vector Matrix convention); MonoGame types only
    /// appear once Game1 converts the finished skin matrices to a bone palette it uploads to Toon.fx's
    /// `Bones` parameter (GPU skinning -- see Toon.fx/GpuMesh.cs's header comments; this class only ever
    /// computes matrices, it never touches a vertex).</summary>
    public static class AnimatedPose
    {
        public enum Clip { Idle, Move }

        /// <summary>World matrix for every node, at the given clip/time (seconds, looped over the
        /// clip's own duration), written into a caller-owned `destination` array (sized model.Nodes.Length
        /// -- see BeastInstance's scratch arrays) so a per-instance-per-frame evaluation doesn't allocate.
        /// An earlier version of this method allocated a fresh array every call; with up to 24 instances
        /// calling it every frame that was ~50 small-array allocations/frame and showed up as GC-driven
        /// frame-time spikes in the bench's 1%-low number -- fixed by reusing caller-owned buffers.
        ///
        /// Composes the hierarchy itself (parent-index walk, using model.ParentIndex -- see
        /// GltfSkinnedModel) from each node's own Node.GetLocalTransform(animation, time), rather than
        /// calling SharpGLTF's Node.GetWorldMatrix(animation, time) once per node. GetWorldMatrix walks
        /// the full ancestor chain from that node up to the scene root *on every call*, so asking for
        /// it node-by-node across a whole skeleton redoes the shared upper part of the chain once per
        /// descendant (this rig is shallow -- root/body/neck/head -- so not a lot of redundant work by
        /// node count, but it also measurably allocates: a lead-review bench comparison at 24 beasts
        /// showed ~2,000 Gen0 collections over a 10-second run with GetWorldMatrix, vs. ~1,230 with this
        /// walk -- still non-zero (isolated by temporarily disabling the Bones effect-parameter upload,
        /// which barely moved the count, ruling that out as the remaining source): the residual
        /// allocation is inside SharpGLTF's own Node.GetLocalTransform(animation, time) -- its per-node
        /// keyframe-sampler evaluation, a third-party internal this spike didn't chase further into (it
        /// would mean hand-rolling keyframe interpolation directly off the accessor data, bypassing
        /// SharpGLTF's animation API entirely). Falls back to the bind-pose world matrix for a model
        /// with no matching animation (e.g. crest_alt.glb, which has none at all).</summary>
        public static void ComputeWorldMatrices(GltfSkinnedModel model, Clip clip, float timeSeconds, Matrix4x4[] destination)
        {
            var animation = clip == Clip.Idle ? model.IdleAnimation : model.MoveAnimation;
            var nodes = model.Nodes;
            var parentIndex = model.ParentIndex;

            if (animation == null)
            {
                for (int i = 0; i < nodes.Length; i++)
                    destination[i] = nodes[i].WorldMatrix;
                return;
            }

            float duration = animation.Duration;
            float t = duration > 0f ? Wrap(timeSeconds, duration) : 0f;

            // glTF's node LogicalIndex order is NOT guaranteed parent-before-child (confirmed on this
            // exact rig: griffin_live.glb's bone_body, the parent, has a *higher* LogicalIndex than its
            // child bone_head -- an export-order artefact of blender_export_live.py's armature, not
            // something to rely on) -- so a single forward pass over `nodes` can't assume a parent is
            // already computed. This does a small fixed-point sweep instead (Span<bool>, stackalloc'd:
            // zero heap allocation, and this rig is only 13 nodes/~4 levels deep, so the handful of
            // extra passes this needs are negligible): each pass computes every not-yet-done node whose
            // parent is already done (or is a root), until nothing is left.
            Span<bool> done = stackalloc bool[nodes.Length];
            int remaining = nodes.Length;
            while (remaining > 0)
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (done[i])
                        continue;
                    int p = parentIndex[i];
                    if (p >= 0 && !done[p])
                        continue;
                    var local = nodes[i].GetLocalTransform(animation, t).Matrix;
                    destination[i] = p >= 0 ? local * destination[p] : local; // row-vector: child-local first, then parent
                    done[i] = true;
                    remaining--;
                }
            }
        }

        private static float Wrap(float t, float duration)
        {
            float m = t % duration;
            return m < 0f ? m + duration : m;
        }

        /// <summary>Per-joint skin matrix (inverseBind * jointWorld * instanceWorld), ready to upload as
        /// a GPU bone palette for a linear-blend skin in Toon.fx's vertex shader. Writes into a
        /// caller-owned `destination` array (sized model.Joints.Length), same non-allocating reasoning
        /// as ComputeWorldMatrices above.</summary>
        public static void ComputeSkinMatrices(GltfSkinnedModel model, Matrix4x4[] nodeWorld, Matrix4x4 instanceWorld, Matrix4x4[] destination)
        {
            for (int j = 0; j < model.Joints.Length; j++)
            {
                var joint = model.Joints[j];
                destination[j] = joint.InverseBind * nodeWorld[joint.Node.LogicalIndex] * instanceWorld;
            }
        }

        public static int FindNodeIndexByName(GltfSkinnedModel model, string name)
        {
            for (int i = 0; i < model.Nodes.Length; i++)
                if (string.Equals(model.Nodes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
    }
}
