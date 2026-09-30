using System;
using System.Numerics;
using SharpGLTF.Schema2;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>Evaluates a GltfSkinnedModel's node hierarchy at a given clip/time (looped), using
    /// SharpGLTF's own Node.GetWorldMatrix(animation, time) -- which already walks the parent chain and
    /// falls back to each ancestor's bind pose where an animation has no channel for it -- and turns the
    /// result into per-joint skin matrices a CPU skinner can blend against. All math here is
    /// System.Numerics (SharpGLTF's own convention, which matches MonoGame/XNA's row-vector Matrix
    /// convention); MonoGame types only appear once a skinned vertex is written to a VertexBuffer (see
    /// Skinner).</summary>
    public static class AnimatedPose
    {
        public enum Clip { Idle, Move }

        /// <summary>World matrix for every node, at the given clip/time (seconds, looped over the
        /// clip's own duration), written into a caller-owned `destination` array (sized model.Nodes.Length
        /// -- see BeastInstance's scratch arrays) so a per-instance-per-frame evaluation doesn't allocate.
        /// An earlier version of this method allocated a fresh array every call; with up to 24 instances
        /// calling it every frame that was ~50 small-array allocations/frame and showed up as GC-driven
        /// frame-time spikes in the bench's 1%-low number -- fixed by reusing caller-owned buffers.
        /// Falls back to the bind-pose world matrix for a model with no matching animation (e.g.
        /// crest_alt.glb, which has none at all).</summary>
        public static void ComputeWorldMatrices(GltfSkinnedModel model, Clip clip, float timeSeconds, Matrix4x4[] destination)
        {
            var animation = clip == Clip.Idle ? model.IdleAnimation : model.MoveAnimation;

            if (animation == null)
            {
                for (int i = 0; i < model.Nodes.Length; i++)
                    destination[i] = model.Nodes[i].WorldMatrix;
                return;
            }

            float duration = animation.Duration;
            float t = duration > 0f ? Wrap(timeSeconds, duration) : 0f;
            for (int i = 0; i < model.Nodes.Length; i++)
                destination[i] = model.Nodes[i].GetWorldMatrix(animation, t);
        }

        private static float Wrap(float t, float duration)
        {
            float m = t % duration;
            return m < 0f ? m + duration : m;
        }

        /// <summary>Per-joint skin matrix (inverseBind * jointWorld * instanceWorld), ready for a linear
        /// blend skin against bind-pose vertex positions/normals -- see Skinner.SkinToBuffer. Writes into
        /// a caller-owned `destination` array (sized model.Joints.Length), same non-allocating reasoning
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
