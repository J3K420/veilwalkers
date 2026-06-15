using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Veilwalkers.Core;
using Veilwalkers.Economy;

namespace Veilwalkers.Billing
{
    /// <summary>
    /// The purchase orchestrator (Story 5.1) — the architecture-named home (architecture.md:419) for the
    /// Billing boundary. Pure-logic, constructor-injected with the catalog, the store seam, and the credit
    /// service; NO service locator (AR-4). It owns the DECISIONS: catalog lookup → route through the store
    /// (AC-2, the EXCLUSIVE Play path) → grant base + bonus ONE-WAY into Economy on success (AC-3) → raise
    /// <see cref="OnPurchaseCompleted"/>. The untestable Unity IAP call lives behind
    /// <see cref="IStoreAdapter"/> (the AR-adapter split).
    /// <para>
    /// <b>Billing → Economy is STRICTLY one-way (architecture.md:478-480).</b> This service CALLS
    /// <see cref="ICreditService.GrantCreditsAsync"/>; the Economy assembly never references Billing
    /// (structurally enforced by the acyclicity test). The Shop-trigger direction (insufficient credits →
    /// open Shop) is App/UI on <c>OnInsufficientCredits</c>, NOT Billing calling out.
    /// </para>
    /// <para>
    /// <b>Story 5.2 interposed <c>PurchaseReconciler</c> into the single grant site — the public surface is
    /// unchanged.</b> The canonical flow (architecture.md:522) routes <c>BillingService → Unity IAP →
    /// PurchaseReconciler (pending→ack→credit once→persist) → CreditService</c>. 5.1 deliberately grafted the
    /// grant into ONE place (the <c>Purchased</c> branch) so 5.2 could swap the bare <c>GrantCreditsAsync</c>
    /// for <c>PurchaseReconciler.ReconcilePurchaseAsync</c> (write pending-ledger → grant once keyed by Play
    /// order id → acknowledge → clear) WITHOUT touching <see cref="PurchaseAsync"/>'s signature,
    /// <see cref="OnPurchaseCompleted"/>, or the <see cref="PurchaseResult"/> taxonomy. The reconciler now
    /// OWNS the grant; this service still owns the boundary event. <see cref="PurchaseFailureReason.GrantFailed"/>
    /// flips from "lost until 5.2" to "the in-line grant did not save yet — the launch recovery pass
    /// (<c>PurchaseReconciler.ReconcilePendingOnLaunchAsync</c>) grants it exactly once next launch."
    /// </para>
    /// </summary>
    public sealed class BillingService : IBillingService
    {
        private readonly CreditPackCatalog _catalog;
        private readonly IStoreAdapter _store;
        private readonly ICreditService _credits;
        private readonly PurchaseReconciler _reconciler;

        public BillingService(
            CreditPackCatalog catalog,
            IStoreAdapter store,
            ICreditService credits,
            PurchaseReconciler reconciler)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _credits = credits ?? throw new ArgumentNullException(nameof(credits));
            _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
        }

        /// <inheritdoc />
        public IReadOnlyList<CreditPack> Packs => _catalog.Packs;

        /// <inheritdoc />
        public event Action<PurchaseCompletedEvent> OnPurchaseCompleted;

        /// <inheritdoc />
        public Task<IReadOnlyDictionary<string, string>> FetchLocalizedPricesAsync()
        {
            var ids = new List<string>(_catalog.Packs.Count);
            foreach (CreditPack pack in _catalog.Packs)
            {
                ids.Add(pack.PackId);
            }

            return _store.FetchLocalizedPricesAsync(ids);
        }

        /// <inheritdoc />
        public async Task<PurchaseResult> PurchaseAsync(string packId)
        {
            // (1) Resolve the pack — an unknown id is a typed failure, and the store is NEVER called (AC-2:
            // we only ever route real packs through Play).
            if (!_catalog.TryGet(packId, out CreditPack pack))
            {
                GameLog.Warn($"BillingService: purchase of unknown pack '{packId}' refused; the store was not called.");
                return PurchaseResult.Failed(PurchaseFailureReason.UnknownPack, packId ?? string.Empty, _credits.Balance);
            }

            // (2) Route the purchase EXCLUSIVELY through the store seam (AC-2). Never throws (NFR-3).
            StorePurchaseResult store = await _store.PurchaseAsync(pack.PackId).ConfigureAwait(false);

            // (5)/(6) A non-completed store outcome grants NOTHING and raises NO event (AC-3 is success-only).
            if (!store.Purchased)
            {
                PurchaseFailureReason reason = MapStoreOutcome(store.Outcome);
                GameLog.Info($"BillingService: purchase of '{pack.PackId}' did not complete ({store.Outcome}); nothing granted.");
                return PurchaseResult.Failed(reason, pack.PackId, _credits.Balance);
            }

            // (3) A completed purchase routes through the PurchaseReconciler (Story 5.2, architecture.md:522):
            // write pending-ledger → grant the pack TOTAL (base + bonus, AC-3) exactly once keyed by the Play
            // order id → acknowledge → clear. This is the SINGLE grant site 5.1 grafted in ONE place for 5.2
            // to interpose on — Billing → Economy stays one-way (the reconciler calls GrantCreditsAsync).
            PurchaseReconciler.PurchaseReconcileResult reconcile =
                await _reconciler.ReconcilePurchaseAsync(store.PlayOrderId, pack.PackId).ConfigureAwait(false);

            // (6) The store charged the player but the local grant did not durably save: surface a distinct
            // GrantFailed + log loudly. No event — nothing was durably granted. Unlike 5.1, this is now
            // RECOVERABLE: the pending record (or a re-attempt) lets the launch reconcile pass grant it
            // exactly-once on the next launch (NFR-4).
            if (reconcile.Outcome != PurchaseReconciler.PurchaseReconcileOutcome.Granted)
            {
                GameLog.Error(
                    $"BillingService: PAID purchase of '{pack.PackId}' (order {store.PlayOrderId}) granted no Credits yet — " +
                    "the in-line grant did not save. The PurchaseReconciler will recover it exactly-once on the next launch.");
                return PurchaseResult.Failed(PurchaseFailureReason.GrantFailed, pack.PackId, reconcile.NewBalance);
            }

            // (4) Success: the new balance is the durable, post-grant balance. Raise OnPurchaseCompleted once.
            int newBalance = reconcile.NewBalance;
            GameLog.Info($"BillingService: purchase of '{pack.PackId}' granted {reconcile.CreditsGranted} Credits → balance {newBalance}.");
            RaisePurchaseCompleted(new PurchaseCompletedEvent(pack.PackId, reconcile.CreditsGranted, newBalance));
            return PurchaseResult.Succeeded(pack.PackId, reconcile.CreditsGranted, newBalance);
        }

        private static PurchaseFailureReason MapStoreOutcome(StorePurchaseOutcome outcome)
        {
            switch (outcome)
            {
                case StorePurchaseOutcome.Cancelled:
                    return PurchaseFailureReason.Cancelled;
                case StorePurchaseOutcome.AlreadyOwned:
                    // Decision C: 5.1 surfaces AlreadyOwned but does NOT grant — the order-id dedup that
                    // makes granting an owned-but-unreconciled purchase safe is Story 5.2.
                    return PurchaseFailureReason.AlreadyOwned;
                case StorePurchaseOutcome.Failed:
                default:
                    return PurchaseFailureReason.StoreUnavailable;
            }
        }

        /// <summary>
        /// Invoke <see cref="OnPurchaseCompleted"/> with subscriber isolation: the grant is already
        /// committed, so a throwing subscriber must be logged, not propagated — a faulted task here would
        /// tell the caller a durable grant failed (and invite a double-purchase retry). Mirrors
        /// <c>CreditService.RaiseCreditsChanged</c>.
        /// </summary>
        private void RaisePurchaseCompleted(PurchaseCompletedEvent evt)
        {
            try
            {
                OnPurchaseCompleted?.Invoke(evt);
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    $"BillingService: an OnPurchaseCompleted subscriber threw — the grant is already committed. {ex.Message}");
            }
        }
    }
}
