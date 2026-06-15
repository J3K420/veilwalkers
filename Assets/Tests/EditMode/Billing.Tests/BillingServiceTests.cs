using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.Billing;
using Veilwalkers.Economy;
using Veilwalkers.Persistence;

namespace Veilwalkers.Billing.Tests
{
    /// <summary>
    /// AC-2 / AC-3 pins for <see cref="BillingService"/>. The grant runs through the REAL
    /// <see cref="CreditService"/> over a deep-cloning fake store (anti-tautology) and a scriptable
    /// <see cref="FakeStoreAdapter"/>; assertions are on the durable balance, the store save-count, the
    /// store-adapter call-count, and the event fire-count — never a literal recomputed from the same input.
    /// </summary>
    public sealed class BillingServiceTests
    {
        // The shared test rig: real CreditService over a deep-cloning fake store, a scriptable store adapter,
        // and a BillingService composing them. Mirrors the Economy test wiring recipe exactly.
        private sealed class Rig
        {
            public FakeBillingProgressStore Store;
            public FakeStoreAdapter Adapter;
            public CreditService Credits;
            public BillingService Billing;
            public readonly List<PurchaseCompletedEvent> Completed = new List<PurchaseCompletedEvent>();
        }

        private static Rig BuildRig(int startingCredits)
        {
            var store = new FakeBillingProgressStore { Stored = new SaveModel { Credits = startingCredits } };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var mutationLock = new SaveMutationLock();
            var credits = new CreditService(save, mutationLock);
            var adapter = new FakeStoreAdapter();
            var catalog = new CreditPackCatalog();
            // Story 5.2: BillingService now routes its single grant site through the PurchaseReconciler. The
            // 5.1 pins below MUST stay green through the interposition — that is the proof the public surface
            // is unchanged. The reconciler shares the same lock + real CreditService + catalog + adapter.
            var reconciler = new PurchaseReconciler(save, mutationLock, credits, catalog, adapter, new FakeClock());
            var billing = new BillingService(catalog, adapter, credits, reconciler);

            var rig = new Rig { Store = store, Adapter = adapter, Credits = credits, Billing = billing };
            billing.OnPurchaseCompleted += e => rig.Completed.Add(e);
            return rig;
        }

        private static PurchaseResult Purchase(Rig rig, string packId) =>
            rig.Billing.PurchaseAsync(packId).GetAwaiter().GetResult();

        // ---------- AC-3: a successful purchase grants base+bonus in ONE grant + raises the event once ----------

        [Test]
        public void Successful_purchase_grants_base_plus_bonus_in_one_save_and_raises_the_event_once()
        {
            var rig = BuildRig(startingCredits: 10);
            rig.Adapter.NextResult = StorePurchaseResult.Succeeded(CreditPackCatalog.HunterPackId, "order-abc");

            PurchaseResult result = Purchase(rig, CreditPackCatalog.HunterPackId);

            // Granted exactly base + bonus = 150 + 20 = 170.
            Assert.IsTrue(result.Success);
            Assert.AreEqual(170, result.CreditsGranted, "AC-3: the grant is base + bonus.");
            Assert.AreEqual(180, result.NewBalance, "10 + 170.");
            Assert.AreEqual(180, rig.Credits.Balance, "The durable balance reflects the grant.");

            // Exactly ONE grant — the balance increments by the pack total exactly once (AR-8 / NFR-4 no
            // double-grant). Story 5.2: the happy path now persists FOUR times (pending-write → grant →
            // Granted-state advance → clear), so the no-double-grant invariant is the BALANCE delta (granted
            // once = +170), not the raw save-count. The exactly-once-grant pin proper lives in
            // PurchaseReconcilerTests (a Granted record's re-pass grant save-count == 0).
            Assert.AreEqual(170, rig.Credits.Balance - 10, "Granted the pack total exactly once (no double-grant).");
            Assert.AreEqual(0, rig.Store.Stored.PendingPurchases.Count,
                "The ledger is cleared after a fully-reconciled purchase (granted + acknowledged).");

            // OnPurchaseCompleted raised exactly once, with the right payload.
            Assert.AreEqual(1, rig.Completed.Count);
            Assert.AreEqual(CreditPackCatalog.HunterPackId, rig.Completed[0].PackId);
            Assert.AreEqual(170, rig.Completed[0].CreditsGranted);
            Assert.AreEqual(180, rig.Completed[0].NewBalance);
        }

        [Test]
        public void Successful_veil_purchase_grants_the_full_500_total()
        {
            var rig = BuildRig(startingCredits: 0);
            rig.Adapter.NextResult = StorePurchaseResult.Succeeded(CreditPackCatalog.VeilPackId, "order-veil");

            PurchaseResult result = Purchase(rig, CreditPackCatalog.VeilPackId);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(500, result.CreditsGranted, "Veil = 400 + 100.");
            Assert.AreEqual(500, rig.Credits.Balance);
        }

        // ---------- AC-3: cancelled / failed store result grants nothing + raises no event ----------

        [Test]
        public void Cancelled_purchase_grants_nothing_and_raises_no_event()
        {
            var rig = BuildRig(startingCredits: 25);
            rig.Adapter.NextResult =
                StorePurchaseResult.NotCompleted(StorePurchaseOutcome.Cancelled, CreditPackCatalog.HunterPackId);
            int savesBefore = rig.Store.SaveCalls;

            PurchaseResult result = Purchase(rig, CreditPackCatalog.HunterPackId);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailureReason.Cancelled, result.FailureReason);
            Assert.AreEqual(25, rig.Credits.Balance, "Balance unchanged on cancel.");
            Assert.AreEqual(0, rig.Store.SaveCalls - savesBefore, "No persist on cancel.");
            Assert.AreEqual(0, rig.Completed.Count, "No OnPurchaseCompleted on cancel.");
        }

        [Test]
        public void Failed_store_result_grants_nothing_and_maps_to_store_unavailable()
        {
            var rig = BuildRig(startingCredits: 25);
            rig.Adapter.NextResult =
                StorePurchaseResult.NotCompleted(StorePurchaseOutcome.Failed, CreditPackCatalog.HunterPackId);

            PurchaseResult result = Purchase(rig, CreditPackCatalog.HunterPackId);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailureReason.StoreUnavailable, result.FailureReason);
            Assert.AreEqual(25, rig.Credits.Balance);
            Assert.AreEqual(0, rig.Completed.Count);
        }

        [Test]
        public void Already_owned_surfaces_a_typed_result_and_does_not_grant_or_event()
        {
            // Decision C: 5.1 surfaces AlreadyOwned but does NOT grant (the order-id dedup that makes it safe
            // is Story 5.2's PurchaseReconciler).
            var rig = BuildRig(startingCredits: 25);
            rig.Adapter.NextResult =
                StorePurchaseResult.NotCompleted(StorePurchaseOutcome.AlreadyOwned, CreditPackCatalog.VeilPackId);

            PurchaseResult result = Purchase(rig, CreditPackCatalog.VeilPackId);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailureReason.AlreadyOwned, result.FailureReason);
            Assert.AreEqual(25, rig.Credits.Balance);
            Assert.AreEqual(0, rig.Completed.Count);
        }

        // ---------- AC-3: a grant-persist fault → typed GrantFailed, no event, balance net-unchanged ----------

        [Test]
        public void Persist_fault_on_the_purchase_path_returns_GrantFailed_with_no_event_and_balance_net_unchanged()
        {
            // Story 5.2: BillingService routes a Purchased result through the reconciler, whose FIRST persist
            // is the pending-ledger write. With FailNextSave armed, that write faults → RecordPendingAsync
            // returns false → NOTHING is granted → GrantFailed (recoverable: the player re-attempts; the
            // dedicated grant-fault-THEN-recovery scenario is pinned in PurchaseReconcilerTests). The contract
            // 5.1 fixed still holds: a persist fault on the purchase path grants nothing, raises no event, and
            // leaves the balance net-unchanged.
            var rig = BuildRig(startingCredits: 40);
            rig.Adapter.NextResult = StorePurchaseResult.Succeeded(CreditPackCatalog.StarterPackId, "order-x");
            rig.Store.FailNextSave = true; // the pending-ledger write faults.

            // The fault path emits three ERROR logs: the pending-write SaveService failure, the reconciler's
            // "failed to record pending purchase" (the write rolled back, so the grant will not proceed), and
            // the BillingService paid-but-not-granted-yet alarm. Expect all three so the EditMode runner does
            // not treat the (intended) errors as a failure.
            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Error, new Regex("PurchaseReconciler: failed to record pending purchase"));
            LogAssert.Expect(LogType.Error, new Regex("BillingService: PAID purchase of 'credits_starter'"));

            PurchaseResult result = Purchase(rig, CreditPackCatalog.StarterPackId);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailureReason.GrantFailed, result.FailureReason,
                "Store charged but the local grant did not save — the 5.2 reconciliation seam.");
            Assert.AreEqual(40, rig.Credits.Balance, "Nothing granted; balance is net-unchanged.");
            Assert.AreEqual(0, rig.Completed.Count, "No event when nothing was durably granted.");
        }

        // ---------- AC-2: purchase routes through the adapter ONLY ----------

        [Test]
        public void Purchase_routes_through_the_store_adapter_exactly_once()
        {
            var rig = BuildRig(startingCredits: 0);
            rig.Adapter.NextResult = StorePurchaseResult.Succeeded(CreditPackCatalog.StarterPackId, "order-1");

            Purchase(rig, CreditPackCatalog.StarterPackId);

            Assert.AreEqual(1, rig.Adapter.PurchaseCalls, "AC-2: exactly one store call per purchase.");
            Assert.AreEqual(CreditPackCatalog.StarterPackId, rig.Adapter.LastPackId);
        }

        [Test]
        public void Unknown_pack_returns_UnknownPack_without_ever_calling_the_store()
        {
            var rig = BuildRig(startingCredits: 25);

            PurchaseResult result = Purchase(rig, "credits_nonexistent");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailureReason.UnknownPack, result.FailureReason);
            Assert.AreEqual(0, rig.Adapter.PurchaseCalls, "AC-2: an unknown pack never reaches the store.");
            Assert.AreEqual(25, rig.Credits.Balance);
            Assert.AreEqual(0, rig.Completed.Count);
        }

        // ---------- AC-1: the service exposes the catalog + localized prices ----------

        [Test]
        public void Service_exposes_the_catalog_packs()
        {
            var rig = BuildRig(startingCredits: 0);
            Assert.That(rig.Billing.Packs.Count, Is.GreaterThanOrEqualTo(3),
                "AC-1: the Shop reads the packs off the service.");
        }

        [Test]
        public void FetchLocalizedPrices_delegates_to_the_store_adapter()
        {
            var rig = BuildRig(startingCredits: 0);
            rig.Adapter.LocalizedPrices = new Dictionary<string, string>
            {
                { CreditPackCatalog.StarterPackId, "£1.79" },
            };

            IReadOnlyDictionary<string, string> prices =
                rig.Billing.FetchLocalizedPricesAsync().GetAwaiter().GetResult();

            Assert.That(prices.ContainsKey(CreditPackCatalog.StarterPackId), Is.True);
            Assert.AreEqual("£1.79", prices[CreditPackCatalog.StarterPackId],
                "AC-1: prices come from Play (the store adapter), not hard-coded.");
        }

        // ---------- Ctor null-arg guards (no service locator) ----------

        [Test]
        public void Constructor_rejects_null_dependencies()
        {
            var catalog = new CreditPackCatalog();
            var adapter = new FakeStoreAdapter();
            var store = new FakeBillingProgressStore { Stored = new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var mutationLock = new SaveMutationLock();
            var credits = new CreditService(save, mutationLock);
            var reconciler = new PurchaseReconciler(save, mutationLock, credits, catalog, adapter, new FakeClock());

            Assert.Throws<ArgumentNullException>(() => new BillingService(null, adapter, credits, reconciler));
            Assert.Throws<ArgumentNullException>(() => new BillingService(catalog, null, credits, reconciler));
            Assert.Throws<ArgumentNullException>(() => new BillingService(catalog, adapter, null, reconciler));
            Assert.Throws<ArgumentNullException>(() => new BillingService(catalog, adapter, credits, null));
        }
    }
}
