using System.Numerics;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>One Griffin on the board: its hex-cell placement, its own desynced animation clock (the
    /// stress test's whole point -- proving instances don't need to share a phase), and the per-instance
    /// dynamic vertex buffers the CPU skinner writes into each frame (body + optional crest).</summary>
    public sealed class BeastInstance
    {
        public Matrix4x4 World; // world placement (hex cell position + facing), row-vector convention
        public float ClockOffset; // seconds, randomised per instance so 24 beasts don't move in lockstep
        public bool CrestOn = true;

        public DynamicVertexBuffer BodyVertexBuffer;
        public VertexPositionNormalTexture[] BodyScratch;
        public DynamicVertexBuffer CrestVertexBuffer;
        public VertexPositionNormalTexture[] CrestScratch;

        // Reused every frame by AnimatedPose so per-instance pose evaluation doesn't allocate (see
        // AnimatedPose.ComputeWorldMatrices/ComputeSkinMatrices's doc comments for why that mattered).
        public Matrix4x4[] NodeWorldScratch;
        public Matrix4x4[] SkinScratch;

        public BeastInstance(GraphicsDevice device, GltfSkinnedModel body, GltfSkinnedModel crest)
        {
            BodyScratch = new VertexPositionNormalTexture[body.Positions.Length];
            BodyVertexBuffer = new DynamicVertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration, body.Positions.Length, BufferUsage.WriteOnly);

            CrestScratch = new VertexPositionNormalTexture[crest.Positions.Length];
            CrestVertexBuffer = new DynamicVertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration, crest.Positions.Length, BufferUsage.WriteOnly);

            NodeWorldScratch = new Matrix4x4[body.Nodes.Length];
            SkinScratch = new Matrix4x4[body.Joints.Length];
        }
    }
}
