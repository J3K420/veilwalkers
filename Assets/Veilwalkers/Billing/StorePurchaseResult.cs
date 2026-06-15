using System;

namespace Veilwalkers.Billing
{
    /// <summary>
    /// The outcome the store adapter (<see cref="IStoreAdapter"/>) reports for a purchase attempt — the
    /// raw store-layer result, BEFORE <see cref="BillingService"/> decides whether to grant. <see cref="None"/>
    /// is the success sentinel.
    /// </summary>
    public enum StorePurchaseOutcome
    {
        /// <summary>The purchase completed at the store (the player paid). <see cref="StorePurchaseResult.PlayOrderId"/>
        /// is populated — Story 5.2 keys exactly-once reconciliation on it.</summary>
        None = 0,

        /// <summary>The player cancelled the purchase flow. Nothing was charged.</summary>
        Cancelled = 1,

        /// <summary>The store could not complete the purchase (network / billing-unavailable / item-unavailable).</summary>
        Failed = 2,

        /// <summary>
        /// The store reports the player already OWNS an un-consumed prior purchase of this product. 5.1
        /// surfaces this but does NOT grant on it — granting an already-owned-but-unreconciled purchase
        /// without the order-id dedup ledger risks the double-grant Story 5.2 exists to prevent (Decision C).
        /// </summary>
        AlreadyOwned = 3,
    }

    /// <summary>
    /// The typed result of <see cref="IStoreAdapter.PurchaseAsync"/> (Story 5.1). NEVER thrown — an
    /// expected store outcome (cancel / fail / already-owned) is reported here (NFR-3 / AR-7). Built via
    /// the private-ctor + factory + null-safe-getter pattern so <c>default</c> honors the contract.
    /// </summary>
    public readonly struct StorePurchaseResult
    {
        private readonly string _packId;
        private readonly string _playOrderId;

        /// <summary>Whether the player completed the purchase at the store.</summary>
        public bool Purchased { get; }

        /// <summary>The store-layer outcome (<see cref="StorePurchaseOutcome.None"/> on success).</summary>
        public StorePurchaseOutcome Outcome { get; }

        /// <summary>The product id the attempt targeted. Never null.</summary>
        public string PackId => _packId ?? string.Empty;

        /// <summary>
        /// The Google Play order id of a completed purchase — the key Story 5.2 dedups exactly-once
        /// reconciliation on (architecture.md:497). Empty unless <see cref="Purchased"/> is true.
        /// </summary>
        public string PlayOrderId => _playOrderId ?? string.Empty;

        private StorePurchaseResult(bool purchased, StorePurchaseOutcome outcome, string packId, string playOrderId)
        {
            Purchased = purchased;
            Outcome = outcome;
            _packId = packId;
            _playOrderId = playOrderId;
        }

        /// <summary>A completed purchase carrying the Play order id (Story 5.2 dedups on it).</summary>
        public static StorePurchaseResult Succeeded(string packId, string playOrderId)
        {
            if (string.IsNullOrEmpty(playOrderId))
            {
                throw new ArgumentException(
                    "A completed store purchase requires a Play order id (Story 5.2 keys reconciliation on it).",
                    nameof(playOrderId));
            }

            return new StorePurchaseResult(true, StorePurchaseOutcome.None, packId, playOrderId);
        }

        /// <summary>A non-completed store outcome (cancel / fail / already-owned). Nothing is granted.</summary>
        public static StorePurchaseResult NotCompleted(StorePurchaseOutcome outcome, string packId)
        {
            if (outcome == StorePurchaseOutcome.None)
            {
                throw new ArgumentException(
                    "A non-completed store result requires a concrete outcome; None is reserved for success.",
                    nameof(outcome));
            }

            return new StorePurchaseResult(false, outcome, packId, null);
        }
    }
}
