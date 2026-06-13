using System;

namespace Veilwalkers.App
{
    /// <summary>
    /// The single UTC day-bucket-long helper (Story 1.9), shared by the ad-cap counter
    /// (<see cref="AdHook"/>) and the write-once first-zero-credit recorder
    /// (<see cref="FirstZeroCreditRecorder"/>) so both agree on "what day is it." A
    /// compact integer day index, distinct from <c>DailyRewardService</c>'s
    /// human-readable <c>yyyy-MM-dd</c> string (Story 1.8) — the two day representations
    /// coexist by design: a readable claim date vs a compact counter bucket.
    /// </summary>
    internal static class DayBucket
    {
        /// <summary>
        /// The UTC calendar day of <paramref name="utc"/> as a <c>long</c> — the count
        /// of whole days since <see cref="DateTime.MinValue"/>. <c>.Date</c> zeroes the
        /// time-of-day so every instant in one UTC day maps to one bucket; <c>.Ticks</c>
        /// is <see cref="DateTimeKind"/>-independent, so a faked clock works identically
        /// to the real one. Stable, monotonic across the day boundary, no epoch constant.
        /// </summary>
        public static long For(DateTime utc) => utc.Date.Ticks / TimeSpan.TicksPerDay;
    }
}
