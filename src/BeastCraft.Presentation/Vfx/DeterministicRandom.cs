namespace BeastCraft.Presentation.Vfx
{
    /// <summary>
    /// A tiny seeded generator for presentation noise (particles, shake): SplitMix32-style mixing
    /// of a counter, so a given seed yields the same sequence on every platform and runtime —
    /// unlike <see cref="System.Random"/>, whose algorithm is an implementation detail. Never used
    /// by the battle, whose one <see cref="System.Random"/> it leaves untouched.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(int seed)
        {
            _state = (uint)seed ^ 0x9E3779B9u;
        }

        /// <summary>The next 32 random bits.</summary>
        public uint NextUInt()
        {
            _state += 0x9E3779B9u;
            return Mix(_state);
        }

        /// <summary>A float in [0, 1).</summary>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>An int in [0, <paramref name="maxExclusive"/>) (0 when the bound is below 1).</summary>
        public int Next(int maxExclusive)
        {
            return maxExclusive < 1 ? 0 : (int)(NextUInt() % (uint)maxExclusive);
        }

        /// <summary>A stateless hash of two values: the same inputs give the same bits.</summary>
        public static uint Hash(int a, int b)
        {
            return Mix(Mix((uint)a ^ 0x85EBCA6Bu) + (uint)b * 0x9E3779B9u);
        }

        private static uint Mix(uint z)
        {
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            return z ^ (z >> 16);
        }
    }
}
