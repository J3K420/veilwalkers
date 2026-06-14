using System;

namespace Veilwalkers.Core
{
    /// <summary>
    /// Production <see cref="IRandom"/> backed by <see cref="System.Random"/>. Tests substitute a scripted
    /// fake to make the Lure rarity roll deterministic. An optional seed makes a production run reproducible
    /// (e.g. for a balancing replay); the default ctor uses a time-seeded <see cref="System.Random"/>.
    /// <para>
    /// Single-threaded by contract (gameplay is main-thread — the same premise as the rest of the
    /// architecture); <see cref="System.Random"/> is not thread-safe and this wrapper adds no locking.
    /// </para>
    /// </summary>
    public sealed class SystemRandom : IRandom
    {
        private readonly Random _random;

        /// <summary>A time-seeded instance (the normal production path).</summary>
        public SystemRandom()
        {
            _random = new Random();
        }

        /// <summary>A fixed-seed instance — reproducible draws (balancing replay / a deterministic build).</summary>
        public SystemRandom(int seed)
        {
            _random = new Random(seed);
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExclusive), maxExclusive, "Next requires a positive exclusive upper bound.");
            }

            return _random.Next(maxExclusive);
        }

        public double NextDouble() => _random.NextDouble();
    }
}
