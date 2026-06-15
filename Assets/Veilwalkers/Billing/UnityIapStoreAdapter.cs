using System.Collections.Generic;
using System.Threading.Tasks;
using Veilwalkers.Core;

namespace Veilwalkers.Billing
{
    /// <summary>
    /// Production <see cref="IStoreAdapter"/> over Google Play Billing via Unity IAP 5 (Play Billing 8) —
    /// the untestable adapter edge, the <see cref="Veilwalkers.AR.ArcoreAnchorProvider"/> equivalent. It
    /// holds NO branching beyond the platform <c>#if</c> (the "thin adapter, ZERO branching logic" mandate,
    /// architecture.md:466); all DECISIONS live in <see cref="BillingService"/>.
    /// <para>
    /// <b>Logic-complete-but-subsystem-stub.</b> Unity IAP's <c>IStoreController</c> / <c>IStoreListener</c>
    /// (or the IAP 5 <c>StoreService</c>) drive the real Play purchase flow, and the
    /// <c>com.unity.purchasing</c> package is NOT yet imported (architecture.md:137 lists it as a pinned
    /// package added at IAP-import time — the same posture as the deferred ARCore Extensions import). So the
    /// <c>#else</c> editor path is real-enough that <see cref="BillingService"/>'s grant-on-success +
    /// one-way-to-Economy + failure-taxonomy logic is reachable in-editor and proven against
    /// <c>FakeStoreAdapter</c>, platform-independent: it reports a non-completed
    /// <see cref="StorePurchaseOutcome.Failed"/> (no store in-editor) and no localized prices. The
    /// <c>#if UNITY_ANDROID</c> device path that drives the real Unity IAP subsystem is a documented
    /// <c>TODO</c> wired when the IAP package is imported + the AR/Shop scene lands — Story 6.3 / device
    /// build. The genuinely device-only, CI-untestable subsystem glue is the only thing deferred; the
    /// purchase + grant DECISIONS ship complete + tested.
    /// </para>
    /// <para>
    /// <b>Never throws (NFR-3):</b> any Unity IAP call that can throw is wrapped + logged via
    /// <see cref="GameLog"/> and degrades to a typed <see cref="StorePurchaseOutcome.Failed"/>, mirroring
    /// <see cref="Veilwalkers.AR.ArcoreAnchorProvider"/>.
    /// </para>
    /// </summary>
    public sealed class UnityIapStoreAdapter : IStoreAdapter
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // TODO(Story 6.3 / device build): wire to Unity IAP 5 when com.unity.purchasing (5.0.0+, bundles
        // Play Billing 8.0.0) is imported. Initialize the IStoreController with the CreditPackCatalog product
        // ids as Consumable products; PurchaseAsync → InitiatePurchase(packId), await the
        // ProcessPurchase/OnPurchaseFailed callback, map a successful PurchaseEventArgs to
        // StorePurchaseResult.Succeeded(packId, product.transactionID) and a PurchaseFailureReason to
        // Cancelled/Failed (DuplicateTransaction/ExistingPurchase → AlreadyOwned). Story 5.2's
        // PurchaseReconciler owns the acknowledge/consume window (do NOT consume here until 5.2). FetchLocalized
        // PricesAsync → read product.metadata.localizedPriceString for each id. Until the package is imported
        // there is no store to drive, so these are conservative stubs that never crash (NFR-3): report a
        // Failed store result + no localized prices, so the device build degrades rather than fake-granting.
        public Task<StorePurchaseResult> PurchaseAsync(string packId)
        {
            GameLog.Info($"UnityIapStoreAdapter.PurchaseAsync('{packId}'): device-path stub — Unity IAP is not imported yet (TODO Story 6.3 / device build).");
            return Task.FromResult(StorePurchaseResult.NotCompleted(StorePurchaseOutcome.Failed, packId));
        }

        public Task<IReadOnlyDictionary<string, string>> FetchLocalizedPricesAsync(IEnumerable<string> packIds)
        {
            GameLog.Info("UnityIapStoreAdapter.FetchLocalizedPricesAsync: device-path stub (TODO Story 6.3 / device build).");
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        }

        // TODO(Story 6.3 / device build): wire to Unity IAP 5 / Play Billing 8. Acknowledge (Consumable
        // packs are CONSUMED) the purchase whose Play order id is playOrderId via the IStoreController
        // (ConfirmPendingPurchase(product) / the Play Billing acknowledgePurchase|consumeAsync). Play
        // auto-refunds a purchase not acknowledged within ~3 days (AC-4), and treats acknowledge as
        // idempotent — re-acknowledging is a no-op success, so a re-run reconcile pass is safe. Return true
        // on success, false on a transient failure (the reconciler retries next pass). Until the package is
        // imported there is no store to acknowledge against, so this is a conservative stub that never
        // crashes (NFR-3): report a no-op success so an in-editor/device-stub reconcile pass clears the
        // pending ledger rather than looping forever on an un-acknowledgeable order.
        public Task<bool> AcknowledgeAsync(string playOrderId)
        {
            GameLog.Info($"UnityIapStoreAdapter.AcknowledgeAsync('{playOrderId}'): device-path stub — Unity IAP is not imported yet (TODO Story 6.3 / device build).");
            return Task.FromResult(true);
        }
#else
        // Editor / non-Android: the Unity IAP / Play Billing subsystem does not exist (and the package is
        // not imported). Report a non-completed Failed store result + no localized prices so the
        // BillingService grant-on-success / one-way / failure-taxonomy decisions are reachable in-editor and
        // never fake-grant without a real purchase. The BillingService LOGIC never depends on these side
        // effects — it is proven entirely against FakeStoreAdapter in Billing.Tests, platform-independent —
        // so editor behavior matches the seam contract.
        public Task<StorePurchaseResult> PurchaseAsync(string packId)
        {
            GameLog.Info($"UnityIapStoreAdapter.PurchaseAsync('{packId}'): no-op off-device (Play Billing unavailable in the editor).");
            return Task.FromResult(StorePurchaseResult.NotCompleted(StorePurchaseOutcome.Failed, packId));
        }

        public Task<IReadOnlyDictionary<string, string>> FetchLocalizedPricesAsync(IEnumerable<string> packIds)
        {
            GameLog.Info("UnityIapStoreAdapter.FetchLocalizedPricesAsync: no-op off-device (no store prices in the editor).");
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        }

        // Editor / non-Android: there is no Play store to acknowledge against. Report a no-op SUCCESS (true)
        // so the BillingService → PurchaseReconciler reconcile pass completes and clears the pending ledger
        // in-editor (mirroring the never-throws, degrade-gracefully posture of the purchase stub). The
        // reconciler's acknowledge SEQUENCE + retry-on-false logic is proven entirely against FakeStoreAdapter
        // in Billing.Tests, platform-independent — the real Play acknowledge round-trip is the device build.
        public Task<bool> AcknowledgeAsync(string playOrderId)
        {
            GameLog.Info($"UnityIapStoreAdapter.AcknowledgeAsync('{playOrderId}'): no-op off-device (Play Billing unavailable in the editor).");
            return Task.FromResult(true);
        }
#endif
    }
}
