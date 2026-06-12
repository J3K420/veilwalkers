namespace Veilwalkers.Core
{
    /// <summary>
    /// Result of attempting to spend credits. Credits are integers only (no
    /// floats), so <see cref="NewBalance"/> is an <see cref="int"/>. Callers check
    /// <see cref="Success"/>; this type NEVER throws for an expected failure such as
    /// "can't afford" — that is reported via <see cref="FailureReason"/>. The credit
    /// pipeline that produces it lands in Story 1.4; the shape is defined here.
    /// </summary>
    public readonly struct SpendResult
    {
        public bool Success { get; }

        /// <summary>The balance after the operation (unchanged on failure).</summary>
        public int NewBalance { get; }

        public SpendFailureReason FailureReason { get; }

        private SpendResult(bool success, int newBalance, SpendFailureReason failureReason)
        {
            Success = success;
            NewBalance = newBalance;
            FailureReason = failureReason;
        }

        public static SpendResult Succeeded(int newBalance) =>
            new SpendResult(true, newBalance, SpendFailureReason.None);

        /// <summary>
        /// Build a failed result carrying a concrete <paramref name="reason"/>.
        /// Passing <see cref="SpendFailureReason.None"/> (the success sentinel) is a
        /// programming error and throws — that guard is not the "expected failure"
        /// path, which never throws.
        /// </summary>
        public static SpendResult Failed(SpendFailureReason reason, int currentBalance)
        {
            if (reason == SpendFailureReason.None)
            {
                throw new System.ArgumentException(
                    "A failed SpendResult requires a concrete failure reason; None is reserved for success.",
                    nameof(reason));
            }

            return new SpendResult(false, currentBalance, reason);
        }
    }
}
