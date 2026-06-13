namespace Veilwalkers.Core
{
    /// <summary>
    /// Result of attempting to claim the daily free-credit reward (Story 1.8). Mirrors
    /// <see cref="SpendResult"/>'s shape — a readonly value carrying the post-operation
    /// balance and a typed reason — but is a distinct type because a claim is not a
    /// spend and its failure vocabulary (<see cref="DailyRewardFailureReason"/>)
    /// differs (notably <see cref="DailyRewardFailureReason.AlreadyClaimedToday"/>,
    /// which has no spend analogue). Callers check <see cref="Claimed"/>; this type
    /// NEVER throws for an expected outcome such as "already claimed today" — that is
    /// reported via <see cref="FailureReason"/> (AR-7).
    /// </summary>
    public readonly struct DailyRewardResult
    {
        /// <summary>True when the reward was granted and persisted this call.</summary>
        public bool Claimed { get; }

        /// <summary>
        /// The credit balance after the operation. On a refused or failed claim this
        /// is the unchanged current balance.
        /// </summary>
        public int NewBalance { get; }

        public DailyRewardFailureReason FailureReason { get; }

        private DailyRewardResult(bool claimed, int newBalance, DailyRewardFailureReason failureReason)
        {
            Claimed = claimed;
            NewBalance = newBalance;
            FailureReason = failureReason;
        }

        public static DailyRewardResult Succeeded(int newBalance) =>
            new DailyRewardResult(true, newBalance, DailyRewardFailureReason.None);

        /// <summary>
        /// Build a refused/failed result carrying a concrete <paramref name="reason"/>
        /// and the unchanged <paramref name="currentBalance"/>. Passing
        /// <see cref="DailyRewardFailureReason.None"/> (the success sentinel) is a
        /// programming error and throws — that guard is not the "expected outcome"
        /// path, which never throws.
        /// </summary>
        public static DailyRewardResult Failed(DailyRewardFailureReason reason, int currentBalance)
        {
            if (reason == DailyRewardFailureReason.None)
            {
                throw new System.ArgumentException(
                    "A failed DailyRewardResult requires a concrete failure reason; None is reserved for success.",
                    nameof(reason));
            }

            return new DailyRewardResult(false, currentBalance, reason);
        }
    }
}
