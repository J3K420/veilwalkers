using System;

namespace Veilwalkers.Core
{
    /// <summary>
    /// The time seam. Logic must read the current time through this interface
    /// rather than calling <c>DateTime.Now</c> or <c>Time.time</c> directly, so that
    /// time-dependent rules (e.g. the daily reward in Story 1.8, ad caps in 1.9) are
    /// deterministically fakeable in tests. All times are UTC (ISO-8601 when
    /// serialized). <see cref="SystemClock"/> is the production implementation.
    /// </summary>
    public interface IClock
    {
        /// <summary>The current Coordinated Universal Time.</summary>
        DateTime UtcNow { get; }
    }
}
