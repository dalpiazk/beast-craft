using System.Numerics;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>One Griffin on the board: its hex-cell placement, its own desynced animation clock (the
    /// stress test's whole point -- proving instances don't need to share a phase), and the per-instance
    /// scratch buffers reused every frame to avoid allocating (see AnimatedPose.cs's doc comments).
    /// Geometry itself (the VertexBuffer/IndexBuffer) is *not* per-instance -- see GpuMesh -- only the
    /// bone palette uploaded to the shader before each draw is.</summary>
    public sealed class BeastInstance
    {
        public const int MaxBones = 16; // must match Toon.fx's MAX_BONES

        public Matrix4x4 World; // world placement (hex cell position + facing), row-vector convention
        public float ClockOffset; // seconds, randomised per instance so 24 beasts don't move in lockstep
        public bool CrestOn = true;

        // Reused every frame by AnimatedPose so per-instance pose evaluation doesn't allocate (see
        // AnimatedPose.ComputeWorldMatrices/ComputeSkinMatrices's doc comments for why that mattered).
        public Matrix4x4[] NodeWorldScratch;
        public Matrix4x4[] SkinScratch;

        // The same skin matrices as SkinScratch, converted to MonoGame's Matrix and padded to
        // MaxBones -- what's actually uploaded to Toon.fx's `Bones` array parameter each draw. Identity
        // padding beyond the model's real joint count is inert (no vertex ever references those slots).
        public XnaMatrix[] BonePalette = NewIdentityPalette();
        public XnaMatrix[] CrestPalette = NewIdentityPalette(); // slot 0 is the crest's one rigid transform

        public BeastInstance(GltfSkinnedModel body)
        {
            NodeWorldScratch = new Matrix4x4[body.Nodes.Length];
            SkinScratch = new Matrix4x4[body.Joints.Length];
        }

        private static XnaMatrix[] NewIdentityPalette()
        {
            var palette = new XnaMatrix[MaxBones];
            for (int i = 0; i < MaxBones; i++)
                palette[i] = XnaMatrix.Identity;
            return palette;
        }
    }
}
