namespace Veilwalkers.Core
{
    /// <summary>
    /// Result of an ad-reward grant attempt (Story 1.9, AR-17). A readonly value
    /// mirroring <see cref="SpendResult"/>/<c>DailyRewardResult</c>: callers check
    /// <see cref="Granted"/>; this type NEVER throws for an expected outcome (cap
    /// reached, or an underlying charge-persist failure) — those are reported via
    /// <see cref="FailureReason"/> (AR-7). <see cref="GrantsToday"/> carries the
    /// post-call count of grants in the current UTC day-bucket so a caller/test can
    /// see the cap state without re-reading the save.
    /// </summary>
    public readonly struct AdRewardResult
    {
        /// <summary>
        /// True when the player received one charge. Normally the cap counter also
        /// persisted; on the rare path where the charge landed but the counter's persist
        /// then failed (or a recovery swap raced it), this is still <c>true</c> — the
        /// player DID get the charge — and the in-memory cap holds for the session while
        /// the on-disk counter catches up on a later save (see <c>AdHook</c>).
        /// </summary>
        public bool Granted { get; }

        /// <summary>Grants consumed in the current UTC day-bucket after this call.</summary>
        public int GrantsToday { get; }

        public AdRewardFailureReason FailureReason { get; }

        private AdRewardResult(bool granted, int grantsToday, AdRewardFailureReason failureReason)
        {
            Granted = granted;
            GrantsToday = grantsToday;
            FailureReason = failureReason;
        }

        public static AdRewardResult Succeeded(int grantsToday) =>
            new AdRewardResult(true, grantsToday, AdRewardFailureReason.None);

        /// <summary>
        /// Build a refused/failed result with a concrete <paramref name="reason"/> and
        /// the unchanged <paramref name="grantsToday"/>. Passing
        /// <see cref="AdRewardFailureReason.None"/> (the success sentinel) is a
        /// programming error and throws.
        /// </summary>
        public static AdRewardResult Failed(AdRewardFailureReason reason, int grantsToday)
        {
            if (reason == AdRewardFailureReason.None)
            {
                throw new System.ArgumentException(
                    "A failed AdRewardResult requires a concrete failure reason; None is reserved for success.",
                    nameof(reason));
            }

            return new AdRewardResult(false, grantsToday, reason);
        }
    }
}
