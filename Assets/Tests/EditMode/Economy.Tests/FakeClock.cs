using System;
using Veilwalkers.Core;

namespace Veilwalkers.Economy.Tests
{
    /// <summary>
    /// The first <see cref="IClock"/> test double in the repo (Story 1.8): a settable
    /// UTC clock so time-dependent rules — the daily-reward calendar day here, ad caps
    /// in 1.9 — are deterministic. Production code reads time ONLY through
    /// <see cref="IClock"/> (AR-18), so substituting this lets a test pin "now," cross a
    /// midnight boundary, or sit just before/after it without sleeping or touching the
    /// system clock. Lives in <c>Economy.Tests</c> where <see cref="DailyRewardService"/>
    /// is exercised; promote it to a shared test assembly if a later story needs it
    /// elsewhere.
    /// </summary>
    internal sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        /// <summary>The clock's current instant. Always treated as UTC.</summary>
        public DateTime UtcNow { get; set; }

        /// <summary>Move the clock forward (or back, with a negative span).</summary>
        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
