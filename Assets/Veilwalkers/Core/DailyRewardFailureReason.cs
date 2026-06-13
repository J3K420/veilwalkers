namespace Veilwalkers.Core
{
    /// <summary>
    /// Why a daily-reward claim did not succeed. <see cref="None"/> is the success
    /// sentinel. Distinct from <see cref="SpendFailureReason"/> because a claim is not
    /// a spend — "already claimed today" has no spend analogue — and the daily reward
    /// reports its own vocabulary. New members must be appended (never reordered) to
    /// keep any persisted/telemetry values stable.
    /// </summary>
    public enum DailyRewardFailureReason
    {
        None = 0,

        /// <summary>
        /// The player already claimed the reward on the current UTC calendar day
        /// (<c>SaveModel.DailyClaim</c> equals today's date). The balance is
        /// unchanged. Reported as a typed result, never thrown — this is an expected
        /// outcome (the player simply comes back tomorrow), not an error. UI maps it
        /// to a "come back tomorrow" / disabled-claim affordance (services produce no
        /// player-facing copy — AR-11).
        /// </summary>
        AlreadyClaimedToday = 1,

        /// <summary>
        /// The claim was rolled back because persisting it failed (AR-8: the credit
        /// delta and the claim-date write commit together or not at all). The balance
        /// and the claim date are both unchanged, so the next attempt re-claims the
        /// same day — a recoverable, retryable state (the daily reward treats a
        /// persist failure as Warn-level, unlike a credit spend). Reported as a typed
        /// result rather than thrown — NFR-3 covers infrastructure failures.
        /// </summary>
        PersistenceFailed = 2
    }
}
