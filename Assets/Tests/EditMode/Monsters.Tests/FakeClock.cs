using System;
using Veilwalkers.Core;

namespace Veilwalkers.Monsters.Tests
{
    /// <summary>
    /// A settable UTC <see cref="IClock"/> test double for <c>Monsters.Tests</c> (Story 2.5),
    /// so the Codex first-discovered date stamp is deterministic. Production reads time ONLY
    /// through <see cref="IClock"/> (AR-18), so substituting this lets a test pin "now" and
    /// assert the exact <c>yyyy-MM-dd</c> the discovery write persists.
    /// <para>
    /// A per-assembly copy of <c>Economy.Tests/FakeClock</c>: that one is <c>internal sealed</c>
    /// to its own assembly and so invisible here (the same reason Story 2.4 authored a
    /// <c>UI.Tests</c>-local <c>FakeUiCodexProgressStore</c> rather than sharing the Monsters
    /// fake). Identical shape, just this assembly's namespace.
    /// </para>
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
