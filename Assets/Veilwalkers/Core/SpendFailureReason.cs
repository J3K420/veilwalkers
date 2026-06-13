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
        PersistenceFailed = 2,

        /// <summary>
        /// A charge consume (Story 1.5) was blocked because the player has zero
        /// charges of that type. The remedy is XP — charges are earned by leveling
        /// up (Capture/Slay grant XP) and are NEVER purchasable, so there is no Shop
        /// route here as there is for <see cref="InsufficientCredits"/>. UI maps this
        /// reason to the "earn via XP" hint; this typed reason IS the AC's "result
        /// indicating earn-via-XP" (services produce no player-facing copy — AR-11).
        /// Appended (never reordered) to keep persisted/telemetry values stable.
        /// </summary>
        InsufficientCharges = 3
    }
}
