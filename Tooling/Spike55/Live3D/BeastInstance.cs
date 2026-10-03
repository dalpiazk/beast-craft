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
        // Anim-pilot griffin (griffin_anim.glb, Tooling/Animation): 33 deform bones for the confirmed
        // 4-leg quadruped rig (lead-review fix round; was 25 for an earlier, wrong 2-leg conclusion --
        // see the README). Safe for every existing model too: unused palette slots beyond a model's
        // real joint count are just inert identity padding (see NewIdentityPalette below). 34 * 4 = 136
        // vec4 vertex-uniform registers exceeds GLSL ES 2.0's 128 vec4 minimum but is comfortably inside
        // ES 3.0's 256 vec4 minimum -- this project targets ES 3.0 (Galaxy A35 minimum spec) -- see
        // Toon.fx's header comment for the full register-budget note.
        public const int MaxBones = 34; // must match Toon.fx's MAX_BONES

        // Anim-pilot griffin: runtime spring bones for tail tip / wing feathers (SpringBone.cs), layered
        // on top of the baked clip pose after AnimatedPose runs -- see SpringBone.cs's header comment.
        // Harmless, always-present fields: a model with no matching spring-joint names (e.g.
        // griffin_live.glb) just never gets its SpringJointConfig.NodeIndex resolved, and
        // SpringJointConfig.IsValid guards every use site, so these states simply never update for
        // anything but the pilot model.
        public SpringJointState TailSpring;
        public SpringJointState WingLSpring;
        public SpringJointState WingRSpring;

        public Matrix4x4 World; // world placement (hex cell position + facing), row-vector convention
        public float ClockOffset; // seconds, randomised per instance so 24 beasts don't move in lockstep
        public bool CrestOn = true;

        // Producer-feedback fix round: dynamic facing (--battle only -- see Game1.UpdateFacing). Position
        // is fixed at spawn (this spike has no movement); CurrentYaw is what World is actually built from
        // each frame, eased toward TargetYaw at a capped angular speed (shortest arc) rather than snapping,
        // so a Griffin's yaw visibly turns rather than popping when its nearest living Swarmling changes.
        public Vector3 Position;
        public float CurrentYaw;
        public float TargetYaw;

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
