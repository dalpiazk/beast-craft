using System;
using System.Linq;
using System.Numerics;
using SharpGLTF.Schema2;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>One skin joint: the SharpGLTF Node it corresponds to (kept live, not copied out -- see
    /// GltfSkinnedModel's doc comment for why) plus its inverse bind matrix.</summary>
    public struct GltfJoint
    {
        public Node Node;
        public Matrix4x4 InverseBind;
    }

    /// <summary>A loaded, GPU-skinnable glTF mesh (see GpuMesh/Toon.fx for the GPU skin itself): static
    /// per-vertex data (position/normal/uv/joint indices/weights), plus the live SharpGLTF node graph
    /// and named animations AnimatedPose needs to evaluate a pose every frame. Deliberately keeps the
    /// SharpGLTF Schema2 objects (Node, Animation) alive and queries them directly (Node.
    /// GetLocalTransform(animation, time) -- AnimatedPose walks the parent hierarchy itself using this
    /// class's precomputed ParentIndex array, rather than calling SharpGLTF's own Node.GetWorldMatrix,
    /// which turned out to allocate measurably when called once per node per instance per frame -- see
    /// AnimatedPose.ComputeWorldMatrices's doc comment for the before/after GC numbers). No
    /// SharpGLTF.Runtime dependency either -- this is a small, purpose-built reader for exactly what
    /// griffin_live.glb and crest_alt.glb carry (one mesh primitive, <= 4 joint influences per vertex, at
    /// most two named animations), not a general-purpose glTF scene loader.</summary>
    public sealed class GltfSkinnedModel
    {
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] TexCoords;
        public Vector4[] JointIndices; // stored as float4, cast to int on use (small integers, exact in float)
        public Vector4[] JointWeights;
        public int[] Indices;
        public byte[] BaseColorImageBytes; // PNG/JPEG bytes as embedded in the GLB, or null if untextured

        public Node[] Nodes;    // every logical node, indexed by LogicalIndex
        public int[] ParentIndex; // ParentIndex[i] = Nodes[i].VisualParent's LogicalIndex, or -1 for a root.
                                  // Precomputed once here so AnimatedPose can walk the hierarchy itself
                                  // with Node.GetLocalTransform(animation, time) (one node, no ancestor
                                  // walk) instead of Node.GetWorldMatrix(animation, time) -- see
                                  // AnimatedPose's doc comment for why that swap mattered.
        public GltfJoint[] Joints; // empty if this model has no skin (e.g. crest_alt.glb)
        public Animation IdleAnimation;
        public Animation MoveAnimation;
        public Animation AttackAnimation; // anim-pilot griffin: griffin_anim.glb carries a third named
                                          // clip (Tooling/Animation/anim/keyed.py); null for any model
                                          // that doesn't export one (e.g. griffin_live.glb).
                                          // Round 15: four new battle clips (Tooling/Animation/anim/keyed.py), same pattern as
                                          // Attack above -- null for any model that doesn't export one.
        public Animation CastAnimation;
        public Animation HitAnimation;
        public Animation KOAnimation;
        public Animation VictoryAnimation;

        public static GltfSkinnedModel Load(string path)
        {
            var root = ModelRoot.Load(path);
            var model = new GltfSkinnedModel();

            var logicalNodes = root.LogicalNodes;
            model.Nodes = logicalNodes.OrderBy(n => n.LogicalIndex).ToArray();
            model.ParentIndex = new int[model.Nodes.Length];
            for (int i = 0; i < model.Nodes.Length; i++)
                model.ParentIndex[i] = model.Nodes[i].VisualParent?.LogicalIndex ?? -1;

            Node meshNode = logicalNodes.FirstOrDefault(n => n.Mesh != null);
            if (meshNode == null)
                throw new InvalidOperationException($"No mesh node found in {path}");
            var primitive = meshNode.Mesh.Primitives[0];

            var posAccessor = primitive.GetVertexAccessor("POSITION");
            var normAccessor = primitive.GetVertexAccessor("NORMAL");
            var uvAccessor = primitive.GetVertexAccessor("TEXCOORD_0");
            model.Positions = posAccessor.AsVector3Array().ToArray();
            model.Normals = normAccessor != null ? normAccessor.AsVector3Array().ToArray() : new Vector3[model.Positions.Length];
            model.TexCoords = uvAccessor != null ? uvAccessor.AsVector2Array().ToArray() : new Vector2[model.Positions.Length];

            var jointsAccessor = primitive.GetVertexAccessor("JOINTS_0");
            var weightsAccessor = primitive.GetVertexAccessor("WEIGHTS_0");
            if (jointsAccessor != null && weightsAccessor != null)
            {
                model.JointIndices = jointsAccessor.AsVector4Array().ToArray();
                model.JointWeights = weightsAccessor.AsVector4Array().ToArray();
            }
            else
            {
                model.JointIndices = new Vector4[model.Positions.Length];
                model.JointWeights = new Vector4[model.Positions.Length];
            }

            model.Indices = primitive.GetIndices().Select(u => (int)u).ToArray();

            var skin = meshNode.Skin;
            if (skin != null)
            {
                model.Joints = new GltfJoint[skin.JointsCount];
                for (int j = 0; j < skin.JointsCount; j++)
                {
                    var (joint, inverseBind) = skin.GetJoint(j);
                    model.Joints[j] = new GltfJoint { Node = joint, InverseBind = inverseBind };
                }
            }
            else
            {
                model.Joints = Array.Empty<GltfJoint>();
            }

            // Base-colour texture (if any): the primitive's material's BaseColor channel's first image.
            var material = primitive.Material;
            var channel = material?.FindChannel("BaseColor");
            if (channel.HasValue && channel.Value.Texture != null)
            {
                model.BaseColorImageBytes = channel.Value.Texture.PrimaryImage.Content.Content.ToArray();
            }

            model.IdleAnimation = root.LogicalAnimations.FirstOrDefault(a => string.Equals(a.Name, "Idle", StringComparison.OrdinalIgnoreCase));
            model.MoveAnimation = root.LogicalAnimations.FirstOrDefault(a => string.Equals(a.Name, "Move", StringComparison.OrdinalIgnoreCase));
            model.AttackAnimation = root.LogicalAnimations.FirstOrDefault(a => string.Equals(a.Name, "Attack", StringComparison.OrdinalIgnoreCase));
            model.CastAnimation = root.LogicalAnimations.FirstOrDefault(a => string.Equals(a.Name, "Cast", StringComparison.OrdinalIgnoreCase));
            model.HitAnimation = root.LogicalAnimations.FirstOrDefault(a => string.Equals(a.Name, "Hit", StringComparison.OrdinalIgnoreCase));
            model.KOAnimation = root.LogicalAnimations.FirstOrDefault(a => string.Equals(a.Name, "KO", StringComparison.OrdinalIgnoreCase));
            model.VictoryAnimation = root.LogicalAnimations.FirstOrDefault(a => string.Equals(a.Name, "Victory", StringComparison.OrdinalIgnoreCase));

            return model;
        }

        public int TriangleCount => Indices.Length / 3;
    }
}
