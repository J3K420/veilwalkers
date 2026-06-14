using System;
using Veilwalkers.Core;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// A settable UTC <see cref="IClock"/> test double for <c>UI.Tests</c> (Story 2.5). The
    /// presenter tests drive discoveries through a REAL <see cref="Veilwalkers.Monsters.CodexService"/>,
    /// which now takes an <see cref="IClock"/> to stamp the first-discovered date — this fixes
    /// that clock so the date is deterministic.
    /// <para>
    /// A per-assembly copy of <c>Economy.Tests/FakeClock</c> (which is <c>internal sealed</c> to
    /// its own assembly, hence invisible here) — the same per-assembly-fake pattern Story 2.4
    /// used for <see cref="FakeUiCodexProgressStore"/>. Identical shape, this assembly's namespace.
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
