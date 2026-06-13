namespace Veilwalkers.Economy
{
    /// <summary>
    /// Payload for <see cref="ICreditService.OnInsufficientCredits"/>: what spend
    /// was attempted (<see cref="Cost"/>) and what the player actually has
    /// (<see cref="Balance"/>), so consumers (the Epic 5.4 top-up sheet, the Story
    /// 6.3 AppStateMachine) get the full context without querying back.
    /// </summary>
    public readonly struct InsufficientCreditsEvent
    {
        /// <summary>The credit cost the failed spend attempted.</summary>
        public int Cost { get; }

        /// <summary>The (unchanged) balance at the time of the attempt.</summary>
        public int Balance { get; }

        public InsufficientCreditsEvent(int cost, int balance)
        {
            Cost = cost;
            Balance = balance;
        }
    }
}
