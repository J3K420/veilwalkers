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
    /// <b>5.1 is the SIMPLE grant-once-on-success path; 5.2 inserts <c>PurchaseReconciler</c>.</b> The
    /// canonical flow (architecture.md:522) routes <c>BillingService → Unity IAP → PurchaseReconciler
    /// (pending→ack→credit once→persist) → CreditService</c>. 5.1 grants directly via
    /// <c>GrantCreditsAsync</c> in ONE place (the <c>Purchased</c> branch) so 5.2 can interpose the
    /// reconciler without rewriting this public surface. 5.1 surfaces <see cref="PurchaseFailureReason.GrantFailed"/>
    /// (paid-but-not-durably-saved) + <see cref="PurchaseFailureReason.AlreadyOwned"/> as the 5.2 seams; it
    /// does NOT survive interruption or dedup by order id (NFR-4 reconciliation is Story 5.2).
    /// </para>
    /// </summary>
    public sealed class BillingService : IBillingService
    {
        private readonly CreditPackCatalog _catalog;
        private readonly IStoreAdapter _store;
        private readonly ICreditService _credits;

        public BillingService(CreditPackCatalog catalog, IStoreAdapter store, ICreditService credits)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _credits = credits ?? throw new ArgumentNullException(nameof(credits));
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

            // (3) A completed purchase grants the pack TOTAL (base + bonus, AC-3) ONE-WAY into Economy.
            // This is the single grant site 5.2's PurchaseReconciler will interpose on (architecture.md:522).
            Result grant = await _credits.GrantCreditsAsync(pack.TotalCredits).ConfigureAwait(false);

            // (6) The store charged the player but the local grant did not durably save: surface a distinct
            // GrantFailed (the 5.2 reconciliation seam) + log loudly. No event — nothing was durably granted.
            if (!grant.Success)
            {
                GameLog.Error(
                    $"BillingService: PAID purchase of '{pack.PackId}' (order {store.PlayOrderId}) granted no Credits — " +
                    $"the grant did not save ({grant.Message}). Story 5.2's PurchaseReconciler must recover this exactly-once.");
                return PurchaseResult.Failed(PurchaseFailureReason.GrantFailed, pack.PackId, _credits.Balance);
            }

            // (4) Success: the new balance is the durable, post-grant balance. Raise OnPurchaseCompleted once.
            int newBalance = _credits.Balance;
            GameLog.Info($"BillingService: purchase of '{pack.PackId}' granted {pack.TotalCredits} Credits → balance {newBalance}.");
            RaisePurchaseCompleted(new PurchaseCompletedEvent(pack.PackId, pack.TotalCredits, newBalance));
            return PurchaseResult.Succeeded(pack.PackId, pack.TotalCredits, newBalance);
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
