using System;
using Veilwalkers.Core;

namespace Veilwalkers.App.Tests
{
    /// <summary>
    /// A settable <see cref="IClock"/> for App.Tests (Story 1.9). Mirrors the
    /// Economy.Tests <c>FakeClock</c>, which is <c>internal</c> to that assembly and so
    /// invisible here; a tiny local copy is cheaper than promoting a shared test-support
    /// assembly. Lets the ad-cap day rollover and the first-zero day-bucket be driven
    /// deterministically across midnight without touching the system clock.
    /// </summary>
    internal sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; set; }

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
