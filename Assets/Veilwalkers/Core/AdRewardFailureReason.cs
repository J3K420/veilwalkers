namespace Veilwalkers.Core
{
    /// <summary>
    /// Why an ad-reward grant did not succeed (Story 1.9). <see cref="None"/> is the
    /// success sentinel. Append-only (never reorder) — same stability rule as
    /// <see cref="SpendFailureReason"/>, since these may surface in telemetry.
    /// </summary>
    public enum AdRewardFailureReason
    {
        None = 0,

        /// <summary>
        /// The per-UTC-day ad cap (<c>EconomyConfig.AdDailyCap</c>) is already reached
        /// for the current day-bucket. No charge granted, counter unchanged. An expected
        /// outcome (the player comes back tomorrow), reported as a typed result, never
        /// thrown.
        /// </summary>
        CapReached = 1,

        /// <summary>
        /// The underlying charge grant (<c>ProgressionService.AddChargeAsync</c>) failed
        /// to persist and rolled back. No charge granted; the cap counter was NOT
        /// consumed (charge-first ordering), so the next attempt retries cleanly.
        /// </summary>
        ChargeGrantFailed = 2
    }
}
