using System;
using System.Numerics;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>Anim-pilot griffin (issue #68): a minimal damped spring-chain simulator for runtime
    /// secondary motion (tail tip, wing feathers) layered on top of the baked clip pose -- exactly the
    /// methodology doc's recommendation (section 6: "implement as a lightweight spring-damper per bone
    /// chain... layered on top of the baked skeletal pose, evaluated every frame in C#") and section 3b's
    /// ("secondary/overlapping motion... should not be keyframed by hand or by Blender script at all").
    /// Not a general physics system: one damped point mass per tracked joint, chasing that joint's
    /// clip-driven world position every frame. No collision, no multi-bone chain coupling -- a single
    /// spring per joint is enough to give a tail tip or wing feather tip a believable one-frame-of-lag
    /// "follow" without the complexity (or cost) of a real chain solver, which this pilot's two spring
    /// points (tail_04, one representative wing-tip bone per side) don't need.</summary>
    public struct SpringJointState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public bool Initialized;

        /// <summary>Advances the spring one frame toward `targetWorldPosition` (the joint's normal,
        /// clip-driven world position this frame). `stiffness` (per second^2-ish) pulls the simulated
        /// point toward the target; `damping` (0..1-ish, applied as an exponential decay per second)
        /// bleeds velocity so the spring settles rather than oscillating forever. First call snaps to the
        /// target with zero velocity (avoids a one-frame "fling" from an uninitialised (0,0,0) position).</summary>
        public void Update(Vector3 targetWorldPosition, float stiffness, float damping, float dt)
        {
            if (!Initialized)
            {
                Position = targetWorldPosition;
                Velocity = Vector3.Zero;
                Initialized = true;
                return;
            }
            if (dt <= 0f)
                return;

            Vector3 accel = (targetWorldPosition - Position) * stiffness;
            Velocity += accel * dt;
            Velocity *= MathF.Max(0f, 1f - damping * dt);
            Position += Velocity * dt;
        }

        /// <summary>The offset to apply to the baked joint's world matrix translation so the mesh deforms
        /// toward the spring-lagged position instead of snapping straight to the clip's own value.</summary>
        public readonly Vector3 Offset(Vector3 bakedWorldPosition) => Position - bakedWorldPosition;
    }

    /// <summary>One spring-driven joint: which skeleton node it tracks, and its tuning. Stiffness/damping
    /// are deliberately different for tail vs. wing (a heavier, longer tail settles slower than a light
    /// feather tip) -- tuned by eye against the review GIF, not derived from any physical measurement.</summary>
    public readonly struct SpringJointConfig
    {
        public readonly int NodeIndex;
        public readonly float Stiffness;
        public readonly float Damping;

        public SpringJointConfig(int nodeIndex, float stiffness, float damping)
        {
            NodeIndex = nodeIndex;
            Stiffness = stiffness;
            Damping = damping;
        }

        public readonly bool IsValid => NodeIndex >= 0;
    }
}
