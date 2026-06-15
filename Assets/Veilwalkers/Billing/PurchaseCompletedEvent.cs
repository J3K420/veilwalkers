namespace Veilwalkers.Billing
{
    /// <summary>
    /// Payload for <see cref="IBillingService.OnPurchaseCompleted"/> (Story 5.1, AC-3): which pack
    /// completed (<see cref="PackId"/>), how many Credits were granted (<see cref="CreditsGranted"/> =
    /// base + bonus), and the resulting balance (<see cref="NewBalance"/>) — so consumers (the Epic-6 Shop
    /// HUD / the Story 5.4 top-up sheet) get the full context without querying back. Mirrors the
    /// <c>InsufficientCreditsEvent</c> payload precedent. Raised ONLY on a successful, durably-granted
    /// purchase (never on cancel / fail / already-owned / grant-fault).
    /// </summary>
    public readonly struct PurchaseCompletedEvent
    {
        /// <summary>The Play product id of the completed pack.</summary>
        public string PackId { get; }

        /// <summary>The Credits granted (base + bonus, AC-3).</summary>
        public int CreditsGranted { get; }

        /// <summary>The balance after the grant.</summary>
        public int NewBalance { get; }

        public PurchaseCompletedEvent(string packId, int creditsGranted, int newBalance)
        {
            PackId = packId;
            CreditsGranted = creditsGranted;
            NewBalance = newBalance;
        }
    }
}
