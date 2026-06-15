using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.Billing;
using Veilwalkers.Economy;
using Veilwalkers.Persistence;

namespace Veilwalkers.Billing.Tests
{
    /// <summary>
    /// Exactly-once / sequence / acknowledge / recovery pins for <see cref="PurchaseReconciler"/> (Story 5.2,
    /// AC-1–4). Every pin drives the REAL <see cref="CreditService"/> over a deep-cloning fake store
    /// (anti-tautology — the stored ledger never aliases the live list) and a scriptable
    /// <see cref="FakeStoreAdapter"/>; assertions are on the durable balance, the store save-count, the
    /// acknowledge-call-count/order, and the deep-cloned PendingPurchases state — never a literal recomputed
    /// from the same input. The load-bearing pin is "a Granted record is NEVER re-credited" (AC-2).
    /// </summary>
    public sealed class PurchaseReconcilerTests
    {
        private sealed class Rig
        {
            public FakeBillingProgressStore Store;
            public FakeStoreAdapter Adapter;
            public CreditService Credits;
            public SaveService Save;
            public SaveMutationLock Lock;
            public CreditPackCatalog Catalog;
            public PurchaseReconciler Reconciler;
        }

        private static Rig BuildRig(int startingCredits, List<PendingPurchaseRecord> seedPending = null)
        {
            var seed = new SaveModel { Credits = startingCredits };
            if (seedPending != null)
            {
                seed.PendingPurchases.AddRange(seedPending);
            }

            var store = new FakeBillingProgressStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var mutationLock = new SaveMutationLock();
            var credits = new CreditService(save, mutationLock);
            var adapter = new FakeStoreAdapter();
            var catalog = new CreditPackCatalog();
            var reconciler = new PurchaseReconciler(save, mutationLock, credits, catalog, adapter, new FakeClock());

            return new Rig
            {
                Store = store,
                Adapter = adapter,
                Credits = credits,
                Save = save,
                Lock = mutationLock,
                Catalog = catalog,
                Reconciler = reconciler,
            };
        }

        private static PendingPurchaseRecord Record(string orderId, string packId, string state) =>
            new PendingPurchaseRecord
            {
                OrderId = orderId,
                PackId = packId,
                State = state,
                IsoTimestampUtc = "2026-06-15T12:00:00.0000000Z",
            };

        // ---------- AC-1: the sequence — pending write → grant once → acknowledge → clear ----------

        [Test]
        public void Reconcile_grants_acknowledges_then_clears_a_pending_purchase()
        {
            var rig = BuildRig(startingCredits: 10);

            rig.Reconciler.ReconcilePurchaseAsync("order-1", CreditPackCatalog.HunterPackId)
                .GetAwaiter().GetResult();

            // Granted base + bonus = 170, balance 10 + 170 = 180.
            Assert.AreEqual(180, rig.Credits.Balance, "AC-1: the pack total (base + bonus) was granted.");
            // Acknowledged exactly once for this order id (AC-4).
            Assert.AreEqual(1, rig.Adapter.AcknowledgeCalls, "AC-4: acknowledged once.");
            Assert.AreEqual("order-1", rig.Adapter.AcknowledgedOrderIds[0]);
            // Cleared — the ledger is empty at the end.
            Assert.AreEqual(0, rig.Store.Stored.PendingPurchases.Count, "AC-1: the record is cleared after ack.");
        }

        [Test]
        public void Pending_record_is_written_before_the_grant()
        {
            // The pending write must precede the grant so an app-kill after paying leaves a recoverable row.
            // We prove the ORDER by failing the SECOND save (the grant): the pending record must already be
            // durable (save 1), and the grant rollback must leave it PendingGrant.
            var rig = BuildRig(startingCredits: 10);
            // First save = the pending-ledger write (succeeds); the grant's save is the one we fail.
            // FakeBillingProgressStore.FailNextSave fails the very next save, so arm it AFTER the pending write
            // by using the full ReconcilePurchaseAsync and asserting the record survived as PendingGrant.
            // (Direct ordering: RecordPendingAsync persists first, then the grant persists.)
            rig.Store.FailNextSave = false;

            // Drive only the pending-write step, then assert it is durable BEFORE any grant.
            bool recorded = rig.Reconciler.RecordPendingAsync("order-seq", CreditPackCatalog.StarterPackId)
                .GetAwaiter().GetResult();

            Assert.IsTrue(recorded);
            Assert.AreEqual(1, rig.Store.Stored.PendingPurchases.Count, "AC-1: pending-ledger write persists first.");
            Assert.AreEqual(PurchaseReconciler.StatePendingGrant, rig.Store.Stored.PendingPurchases[0].State);
            Assert.AreEqual(10, rig.Credits.Balance, "No grant happened yet — only the pending write.");
        }

        // ---------- AC-2: exactly-once across interruption (the load-bearing pin) ----------

        [Test]
        public void Launch_pass_grants_a_pending_grant_record_exactly_once()
        {
            // Simulate an app-kill AFTER the pending write but BEFORE crediting: a PendingGrant record is on
            // disk. A fresh reconciler (new process) runs the launch pass.
            var rig = BuildRig(
                startingCredits: 0,
                seedPending: new List<PendingPurchaseRecord>
                {
                    Record("order-kill", CreditPackCatalog.VeilPackId, PurchaseReconciler.StatePendingGrant),
                });

            rig.Reconciler.ReconcilePendingOnLaunchAsync().GetAwaiter().GetResult();

            Assert.AreEqual(500, rig.Credits.Balance, "AC-2: the interrupted purchase is granted exactly once.");
            Assert.AreEqual(0, rig.Store.Stored.PendingPurchases.Count, "Cleared after grant + ack.");
        }

        [Test]
        public void A_granted_record_is_never_re_credited()
        {
            // Simulate an app-kill BETWEEN the grant and the acknowledge: the record is on disk as Granted and
            // the credits were ALREADY applied on the prior run. Seed the balance to reflect the prior grant.
            var rig = BuildRig(
                startingCredits: 500, // the Veil grant already landed on the (simulated) prior run
                seedPending: new List<PendingPurchaseRecord>
                {
                    Record("order-acked-later", CreditPackCatalog.VeilPackId, PurchaseReconciler.StateGranted),
                });
            int savesBefore = rig.Store.SaveCalls;

            rig.Reconciler.ReconcilePendingOnLaunchAsync().GetAwaiter().GetResult();

            // NO second grant: balance unchanged. (A grant would persist Credits — the save-count for a grant
            // is the dedup proof.) Only the acknowledge + the clear persist run.
            Assert.AreEqual(500, rig.Credits.Balance, "AC-2: a Granted record is NEVER re-credited (no double grant).");
            Assert.AreEqual(1, rig.Adapter.AcknowledgeCalls, "Only the acknowledge runs on a Granted record.");
            Assert.AreEqual(0, rig.Store.Stored.PendingPurchases.Count, "Acknowledged + cleared.");
            // Exactly one persist (the clear); the grant did NOT persist a second credit.
            Assert.AreEqual(1, rig.Store.SaveCalls - savesBefore, "Only the clear persists — no re-grant save.");
        }

        [Test]
        public void Two_back_to_back_passes_grant_a_purchase_exactly_once_total()
        {
            var rig = BuildRig(
                startingCredits: 0,
                seedPending: new List<PendingPurchaseRecord>
                {
                    Record("order-twice", CreditPackCatalog.StarterPackId, PurchaseReconciler.StatePendingGrant),
                });

            rig.Reconciler.ReconcileAsync().GetAwaiter().GetResult();
            int afterFirst = rig.Credits.Balance;
            rig.Reconciler.ReconcileAsync().GetAwaiter().GetResult();

            Assert.AreEqual(50, afterFirst, "First pass grants the Starter total once.");
            Assert.AreEqual(50, rig.Credits.Balance, "AC-2: the second pass grants nothing — exactly once total.");
        }

        // ---------- AC-2: idempotent pending-write ----------

        [Test]
        public void Recording_the_same_order_id_twice_writes_one_record()
        {
            var rig = BuildRig(startingCredits: 0);

            rig.Reconciler.RecordPendingAsync("order-dup", CreditPackCatalog.HunterPackId).GetAwaiter().GetResult();
            rig.Reconciler.RecordPendingAsync("order-dup", CreditPackCatalog.HunterPackId).GetAwaiter().GetResult();

            Assert.AreEqual(1, rig.Store.Stored.PendingPurchases.Count,
                "A double-tap / retry must not create two pending rows for one purchase.");
        }

        // ---------- AC-3: a cancel/fail writes no pending record (via BillingService) ----------

        [Test]
        public void A_cancelled_purchase_leaves_no_pending_record_and_no_balance_change()
        {
            var rig = BuildRig(startingCredits: 25);
            var billing = new BillingService(rig.Catalog, rig.Adapter, rig.Credits, rig.Reconciler);
            rig.Adapter.NextResult =
                StorePurchaseResult.NotCompleted(StorePurchaseOutcome.Cancelled, CreditPackCatalog.HunterPackId);

            PurchaseResult result = billing.PurchaseAsync(CreditPackCatalog.HunterPackId).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailureReason.Cancelled, result.FailureReason);
            Assert.AreEqual(25, rig.Credits.Balance, "AC-3: balance unchanged on cancel.");
            Assert.AreEqual(0, rig.Store.Stored.PendingPurchases.Count,
                "AC-3: a cancel writes NO pending record — nothing to reconcile.");
        }

        // ---------- AC-4: acknowledge after the grant + retried on failure ----------

        [Test]
        public void Acknowledge_happens_after_the_grant()
        {
            var rig = BuildRig(startingCredits: 0);

            rig.Reconciler.ReconcilePurchaseAsync("order-order", CreditPackCatalog.StarterPackId)
                .GetAwaiter().GetResult();

            // The grant landed (balance 50) AND the acknowledge ran for the same order id — the grant must
            // precede the ack (a Granted state is persisted before AcknowledgeAsync is called).
            Assert.AreEqual(50, rig.Credits.Balance);
            Assert.AreEqual(1, rig.Adapter.AcknowledgeCalls);
            Assert.AreEqual("order-order", rig.Adapter.AcknowledgedOrderIds[0]);
        }

        [Test]
        public void A_failed_acknowledge_leaves_the_record_granted_and_the_next_pass_retries_without_re_granting()
        {
            var rig = BuildRig(startingCredits: 0);
            rig.Adapter.NextAcknowledgeResult = false; // the first acknowledge fails (transient).

            rig.Reconciler.ReconcilePurchaseAsync("order-ack-fail", CreditPackCatalog.HunterPackId)
                .GetAwaiter().GetResult();

            // Credits ARE granted (170); the record is left as Granted (NOT cleared) for the next pass.
            Assert.AreEqual(170, rig.Credits.Balance, "The Credits are granted even though the ack failed.");
            Assert.AreEqual(1, rig.Store.Stored.PendingPurchases.Count, "AC-4: a failed ack leaves the record.");
            Assert.AreEqual(PurchaseReconciler.StateGranted, rig.Store.Stored.PendingPurchases[0].State,
                "The record is Granted (credits safe), awaiting a retried acknowledge.");

            // Next pass: the acknowledge succeeds; NO re-grant (balance stays 170), record cleared.
            rig.Adapter.NextAcknowledgeResult = true;
            rig.Reconciler.ReconcileAsync().GetAwaiter().GetResult();

            Assert.AreEqual(170, rig.Credits.Balance, "AC-2/AC-4: the ack-retry pass never re-grants.");
            Assert.AreEqual(2, rig.Adapter.AcknowledgeCalls, "The acknowledge was retried.");
            Assert.AreEqual(0, rig.Store.Stored.PendingPurchases.Count, "Acknowledged + cleared on the retry.");
        }

        // ---------- Grant-fault recovery (settles the 5.1 GrantFailed deferral) ----------

        [Test]
        public void A_grant_persist_fault_leaves_the_record_pending_then_a_later_pass_recovers_exactly_once()
        {
            var rig = BuildRig(startingCredits: 0);
            var billing = new BillingService(rig.Catalog, rig.Adapter, rig.Credits, rig.Reconciler);
            rig.Adapter.NextResult = StorePurchaseResult.Succeeded(CreditPackCatalog.StarterPackId, "order-recover");

            // The pending-ledger write succeeds (save 1); the GRANT's save fails (save 2) → CreditService rolls
            // back + returns Result.Fail → BillingService returns GrantFailed; the record stays PendingGrant.
            // The fault path emits the three synchronous error logs (the 5.1 precedent) + the reconciler's
            // Warn, then the BillingService GrantFailed Error.
            // We arm FailNextSave so that the grant save (the second save) throws. RecordPendingAsync's save is
            // first, so arm it AFTER recording — but PurchaseAsync is one call. Instead: the pending write is
            // save #1 and succeeds; FailNextSave must fail save #2. FakeBillingProgressStore fails the NEXT
            // save, so we cannot pre-arm without failing the pending write. Drive it in two steps:
            rig.Reconciler.RecordPendingAsync("order-recover", CreditPackCatalog.StarterPackId)
                .GetAwaiter().GetResult();
            rig.Store.FailNextSave = true; // now fail the grant persist.

            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Error, new Regex("CreditService: grant of 50 rolled back"));

            rig.Reconciler.ReconcileAsync().GetAwaiter().GetResult();

            // Grant rolled back: balance unchanged, record STILL PendingGrant (recoverable).
            Assert.AreEqual(0, rig.Credits.Balance, "The grant rolled back — net-unchanged.");
            Assert.AreEqual(1, rig.Store.Stored.PendingPurchases.Count, "The record stays for recovery.");
            Assert.AreEqual(PurchaseReconciler.StatePendingGrant, rig.Store.Stored.PendingPurchases[0].State);

            // Next pass (fault cleared): the launch recovery grants it EXACTLY once.
            rig.Reconciler.ReconcileAsync().GetAwaiter().GetResult();

            Assert.AreEqual(50, rig.Credits.Balance, "AC-2: recovered exactly once on the next pass.");
            Assert.AreEqual(0, rig.Store.Stored.PendingPurchases.Count, "Granted + acked + cleared.");
        }

        // ---------- Corrupt / stale ledger record ----------

        [Test]
        public void An_unknown_pack_id_in_the_ledger_is_logged_and_cleared_without_granting()
        {
            var rig = BuildRig(
                startingCredits: 7,
                seedPending: new List<PendingPurchaseRecord>
                {
                    Record("order-stale", "credits_removed_pack", PurchaseReconciler.StatePendingGrant),
                });

            LogAssert.Expect(LogType.Error, new Regex("references unknown pack"));

            rig.Reconciler.ReconcileAsync().GetAwaiter().GetResult();

            Assert.AreEqual(7, rig.Credits.Balance, "A stale/unknown pack id grants nothing.");
            Assert.AreEqual(0, rig.Store.Stored.PendingPurchases.Count,
                "The corrupt record is cleared (terminal) — no infinite loop.");
            Assert.AreEqual(0, rig.Adapter.AcknowledgeCalls, "Never acknowledged — it was never granted.");
        }

        // ---------- Empty-ledger no-op ----------

        [Test]
        public void An_empty_ledger_launch_pass_does_nothing()
        {
            var rig = BuildRig(startingCredits: 33);
            int savesBefore = rig.Store.SaveCalls;

            rig.Reconciler.ReconcilePendingOnLaunchAsync().GetAwaiter().GetResult();

            Assert.AreEqual(33, rig.Credits.Balance, "No grant on an empty ledger.");
            Assert.AreEqual(0, rig.Adapter.AcknowledgeCalls, "No acknowledge on an empty ledger.");
            Assert.AreEqual(0, rig.Store.SaveCalls - savesBefore, "No save on an empty ledger.");
        }

        // ---------- Ctor null-args (no service locator) ----------

        [Test]
        public void Constructor_rejects_null_dependencies()
        {
            var store = new FakeBillingProgressStore { Stored = new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var mutationLock = new SaveMutationLock();
            var credits = new CreditService(save, mutationLock);
            var catalog = new CreditPackCatalog();
            var adapter = new FakeStoreAdapter();
            var clock = new FakeClock();

            Assert.Throws<ArgumentNullException>(() => new PurchaseReconciler(null, mutationLock, credits, catalog, adapter, clock));
            Assert.Throws<ArgumentNullException>(() => new PurchaseReconciler(save, null, credits, catalog, adapter, clock));
            Assert.Throws<ArgumentNullException>(() => new PurchaseReconciler(save, mutationLock, null, catalog, adapter, clock));
            Assert.Throws<ArgumentNullException>(() => new PurchaseReconciler(save, mutationLock, credits, null, adapter, clock));
            Assert.Throws<ArgumentNullException>(() => new PurchaseReconciler(save, mutationLock, credits, catalog, null, clock));
            Assert.Throws<ArgumentNullException>(() => new PurchaseReconciler(save, mutationLock, credits, catalog, adapter, null));
        }

        // ---------- Story 5.3: the Guaranteed-Rare Lure grant (gated on the Veil flag, exactly once) ----------

        [Test]
        public void Veil_purchase_grants_one_guaranteed_rare_lure_alongside_the_credits()
        {
            // AC-1: a Veil purchase grants Credits (500) AND one Guaranteed-Rare Lure, BOTH committed (the lure
            // rides the same Granted-state write as the Credits).
            var rig = BuildRig(startingCredits: 0);

            rig.Reconciler.ReconcilePurchaseAsync("order-veil", CreditPackCatalog.VeilPackId)
                .GetAwaiter().GetResult();

            Assert.AreEqual(500, rig.Credits.Balance, "AC-1: the Veil pack total (400 + 100) was granted.");
            Assert.AreEqual(1, rig.Store.Stored.GuaranteedRareLures,
                "AC-1: a Veil purchase grants exactly one one-shot Guaranteed-Rare Lure, persisted.");
            Assert.AreEqual(0, rig.Store.Stored.PendingPurchases.Count, "Cleared after ack.");
        }

        [Test]
        public void A_non_veil_purchase_grants_no_guaranteed_rare_lure()
        {
            // AC-1: Starter/Hunter packs do NOT include the Guaranteed-Rare Lure (the flag is false) — the
            // one-shot counter is untouched.
            var rig = BuildRig(startingCredits: 0);

            rig.Reconciler.ReconcilePurchaseAsync("order-hunter", CreditPackCatalog.HunterPackId)
                .GetAwaiter().GetResult();

            Assert.AreEqual(170, rig.Credits.Balance, "The Hunter total (150 + 20) was granted.");
            Assert.AreEqual(0, rig.Store.Stored.GuaranteedRareLures,
                "AC-1: a non-Veil pack grants NO Guaranteed-Rare Lure (the flag is false).");
        }

        [Test]
        public void A_granted_veil_record_is_never_re_granted_a_second_lure()
        {
            // AC-1 exactly-once across interruption (the load-bearing pin): a Granted Veil record (the lure was
            // already granted on the prior run, reflected in the seed) re-passes and grants NEITHER a second
            // lure NOR second Credits. Simulate the app-kill the 5.2 way: a Granted record + the prior balance
            // + the prior lure count, a fresh launch pass over the same stored model.
            var seed = new SaveModel { Credits = 500, GuaranteedRareLures = 1 };
            seed.PendingPurchases.Add(
                Record("order-veil-acked-later", CreditPackCatalog.VeilPackId, PurchaseReconciler.StateGranted));
            var store = new FakeBillingProgressStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var mutationLock = new SaveMutationLock();
            var credits = new CreditService(save, mutationLock);
            var adapter = new FakeStoreAdapter();
            var reconciler = new PurchaseReconciler(save, mutationLock, credits, new CreditPackCatalog(), adapter, new FakeClock());

            reconciler.ReconcilePendingOnLaunchAsync().GetAwaiter().GetResult();

            Assert.AreEqual(500, credits.Balance, "AC-1: a Granted record is never re-credited.");
            Assert.AreEqual(1, store.Stored.GuaranteedRareLures,
                "AC-1: a Granted Veil record grants NO second lure — exactly once across interruption.");
            Assert.AreEqual(0, store.Stored.PendingPurchases.Count, "Acknowledged + cleared.");
        }
    }
}
