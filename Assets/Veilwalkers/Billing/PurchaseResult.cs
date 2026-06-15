using System;

namespace Veilwalkers.Billing
{
    /// <summary>
    /// Why a purchase did not grant Credits (Story 5.1). <see cref="None"/> is the success sentinel.
    /// </summary>
    public enum PurchaseFailureReason
    {
        /// <summary>The purchase completed and Credits were granted.</summary>
        None = 0,

        /// <summary>The requested pack id is not in the catalog. The store was never called.</summary>
        UnknownPack = 1,

        /// <summary>The player cancelled the purchase flow. Nothing was charged or granted.</summary>
        Cancelled = 2,

        /// <summary>The store could not complete the purchase (network / billing-unavailable / no store
        /// in-editor). Nothing was granted.</summary>
        StoreUnavailable = 3,

        /// <summary>
        /// The store reported the player already OWNS an un-consumed prior purchase. 5.1 does NOT grant on
        /// this (the order-id dedup that makes it safe is Story 5.2's <c>PurchaseReconciler</c>, Decision C).
        /// </summary>
        AlreadyOwned = 4,

        /// <summary>
        /// The store purchase completed (the player PAID) but the local credit grant did not durably save
        /// (a persist fault — <c>GrantCreditsAsync</c> rolled back). The paid-for Credits are NOT granted
        /// in 5.1; Story 5.2's <c>PurchaseReconciler</c> recovers this exactly-once on the next launch. This
        /// is the SEAM for 5.2, surfaced as a distinct reason + logged loudly.
        /// </summary>
        GrantFailed = 5,
    }

    /// <summary>
    /// The typed outcome of <see cref="IBillingService.PurchaseAsync"/> (Story 5.1, AC-2/AC-3). Carries the
    /// granted Credits + the new balance on success. NEVER thrown — an expected purchase outcome (cancel /
    /// fail / unknown pack / already-owned / grant-fault) is reported here (NFR-3 / AR-7). The
    /// private-ctor + factory + null-safe-getter pattern so <c>default</c> honors the contract.
    /// </summary>
    public readonly struct PurchaseResult
    {
        private readonly string _packId;

        /// <summary>Whether the purchase completed AND the Credits were durably granted.</summary>
        public bool Success { get; }

        /// <summary>Why it failed (<see cref="PurchaseFailureReason.None"/> on success).</summary>
        public PurchaseFailureReason FailureReason { get; }

        /// <summary>The pack the purchase targeted. Never null.</summary>
        public string PackId => _packId ?? string.Empty;

        /// <summary>The Credits granted on success (base + bonus, AC-3). Zero on failure.</summary>
        public int CreditsGranted { get; }

        /// <summary>The balance after the grant (unchanged on failure).</summary>
        public int NewBalance { get; }

        private PurchaseResult(
            bool success, PurchaseFailureReason reason, string packId, int creditsGranted, int newBalance)
        {
            Success = success;
            FailureReason = reason;
            _packId = packId;
            CreditsGranted = creditsGranted;
            NewBalance = newBalance;
        }

        public static PurchaseResult Succeeded(string packId, int creditsGranted, int newBalance) =>
            new PurchaseResult(true, PurchaseFailureReason.None, packId, creditsGranted, newBalance);

        /// <summary>
        /// Build a failed result. Passing <see cref="PurchaseFailureReason.None"/> is a programming error
        /// and throws — not the expected-failure path, which never throws. <paramref name="currentBalance"/>
        /// carries the unchanged balance for the (Epic-6) HUD.
        /// </summary>
        public static PurchaseResult Failed(PurchaseFailureReason reason, string packId, int currentBalance)
        {
            if (reason == PurchaseFailureReason.None)
            {
                throw new ArgumentException(
                    "A failed PurchaseResult requires a concrete failure reason; None is reserved for success.",
                    nameof(reason));
            }

            return new PurchaseResult(false, reason, packId, 0, currentBalance);
        }
    }
}
