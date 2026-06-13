namespace Veilwalkers.Core
{
    /// <summary>
    /// Why a credit spend did not succeed. <see cref="None"/> is the success
    /// sentinel. New members must be appended (never reordered) to keep any
    /// persisted/telemetry values stable.
    /// </summary>
    public enum SpendFailureReason
    {
        None = 0,
        InsufficientCredits = 1,

        /// <summary>
        /// The deduction was rolled back because persisting it failed (AR-8: a
        /// spend is committed only once it is saved). The balance is unchanged.
        /// Reported as a typed result rather than thrown — NFR-3's standard covers
        /// infrastructure failures, not just "can't afford".
        /// </summary>
        PersistenceFailed = 2
    }
}
