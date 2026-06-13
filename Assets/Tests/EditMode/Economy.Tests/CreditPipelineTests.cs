using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.Core;
using Veilwalkers.Persistence;

namespace Veilwalkers.Economy.Tests
{
    /// <summary>
    /// The AR-8 pipeline guarantees: persist-failure rollback (spend AND grant),
    /// the <c>SemaphoreSlim</c> serialization of in-flight mutations, and the
    /// locator wiring happy path (constructed and registered like Bootstrap does,
    /// NOT via Veilwalkers.App, which EditMode tests must not reference).
    /// </summary>
    public sealed class CreditPipelineTests
    {
        [SetUp]
        public void SetUp()
        {
            GameServices.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.ResetForTests();
        }

        private static (FakeProgressStore store, SaveService save, CreditService credits)
            CreateInitialized(int credits)
        {
            var store = new FakeProgressStore { Stored = new SaveModel { Credits = credits } };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            return (store, save, new CreditService(save, new SaveMutationLock()));
        }

        [Test]
        public void Spend_persist_failure_rolls_back_and_reports_PersistenceFailed()
        {
            var (store, save, credits) = CreateInitialized(5);
            store.FailNextSave = true;

            int changedCount = 0;
            credits.OnCreditsChanged += _ => changedCount++;

            // The faulting save logs in SaveService's catch, then the rollback logs
            // in CreditService's — both on this thread (the fake's faulted task
            // continues synchronously), so LogAssert can expect them in order.
            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Error, new Regex("CreditService: spend of 3 rolled back"));

            SpendResult result = credits.TrySpendCreditsAsync(3).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SpendFailureReason.PersistenceFailed, result.FailureReason);
            Assert.AreEqual(5, result.NewBalance, "The result reports the restored prior balance.");
            Assert.AreEqual(5, save.Current.Credits, "The deduction must be rolled back.");
            Assert.AreEqual(1, store.SaveCalls, "The persist was attempted exactly once.");
            Assert.AreEqual(0, changedCount, "No OnCreditsChanged for an uncommitted spend.");
            Assert.AreEqual(5, store.Stored.Credits, "The persisted snapshot is untouched by the failed save.");
        }

        [Test]
        public void Grant_persist_failure_rolls_back_and_returns_failed_Result()
        {
            var (store, save, credits) = CreateInitialized(7);
            store.FailNextSave = true;

            int changedCount = 0;
            credits.OnCreditsChanged += _ => changedCount++;

            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Error, new Regex("CreditService: grant of 20 rolled back"));

            Result result = credits.GrantCreditsAsync(20).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.Message);
            Assert.AreEqual(7, save.Current.Credits, "The grant must be rolled back.");
            Assert.AreEqual(7, credits.Balance, "Balance reads the restored value after rollback.");
            Assert.AreEqual(1, store.SaveCalls, "The persist was attempted exactly once — no retry.");
            Assert.AreEqual(0, changedCount, "No OnCreditsChanged for an uncommitted grant.");
            Assert.AreEqual(7, store.Stored.Credits, "The persisted snapshot is untouched by the failed save.");
        }

        [Test]
        public void Concurrent_spends_are_serialized_never_interleaved()
        {
            var (store, save, credits) = CreateInitialized(10);

            // Hold every save open: continuations run asynchronously on release so
            // the test thread can't be hijacked by the resuming pipeline.
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.SaveGate = gate;

            Task<SpendResult> spendA = credits.TrySpendCreditsAsync(3);
            Task<SpendResult> spendB = credits.TrySpendCreditsAsync(2);

            // A deducted and is held open at its persist; B is parked at the
            // mutation lock and must NOT have deducted while A's save is in flight.
            Assert.IsFalse(spendA.IsCompleted);
            Assert.IsFalse(spendB.IsCompleted);
            Assert.AreEqual(7, save.Current.Credits,
                "Only A's deduction may be visible while A's save is in flight.");
            Assert.AreEqual(1, store.SaveCalls,
                "B must not reach its persist while A holds the mutation lock.");

            gate.SetResult(true);
            Task.WhenAll(spendA, spendB).GetAwaiter().GetResult();

            Assert.IsTrue(spendA.Result.Success);
            Assert.IsTrue(spendB.Result.Success);
            Assert.AreEqual(7, spendA.Result.NewBalance);
            Assert.AreEqual(5, spendB.Result.NewBalance);
            Assert.AreEqual(5, save.Current.Credits, "Both spends commit once the gate opens.");
            Assert.AreEqual(2, store.SaveCalls, "Exactly one persist per spend.");
        }

        [Test]
        public void Recovery_swap_mid_spend_reports_PersistenceFailed_not_phantom_success()
        {
            var (store, save, credits) = CreateInitialized(10);

            int changedCount = 0;
            credits.OnCreditsChanged += _ => changedCount++;

            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.SaveGate = gate;

            // The spend deducts on its captured model and is held open at the persist.
            Task<SpendResult> spend = credits.TrySpendCreditsAsync(3);
            Assert.IsFalse(spend.IsCompleted);

            // Recovery races the in-flight mutation: RetryLoadAsync publishes a
            // FRESH model object (the fake clones on load, like the real store
            // deserializing), so Current no longer is the spend's captured model.
            store.SaveGate = null;
            save.RetryLoadAsync().GetAwaiter().GetResult();

            LogAssert.Expect(LogType.Error, new Regex("CreditService: spend of 3 rolled back — the save model was swapped"));

            gate.SetResult(true);
            SpendResult result = spend.GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "A mid-operation model swap must never report a phantom success.");
            Assert.AreEqual(SpendFailureReason.PersistenceFailed, result.FailureReason);
            Assert.AreEqual(10, result.NewBalance, "The result reports the restored prior balance.");
            Assert.AreEqual(10, credits.Balance, "Balance reads the swapped-in (recovered) model.");
            Assert.AreEqual(0, changedCount, "No OnCreditsChanged for a spend that did not commit.");
        }

        [Test]
        public void Locator_wiring_happy_path_grant_and_spend_round_trip()
        {
            // Mirrors Bootstrap's wiring (construct → register → seal) without
            // referencing Veilwalkers.App.
            var store = new FakeProgressStore();
            var save = new SaveService(store);
            var credits = new CreditService(save, new SaveMutationLock());

            GameServices.Register<ICreditService>(credits);
            GameServices.MarkReady();

            ICreditService resolved = GameServices.Get<ICreditService>();
            Assert.AreSame(credits, resolved);

            save.InitializeAsync().GetAwaiter().GetResult();

            Result grant = resolved.GrantCreditsAsync(20).GetAwaiter().GetResult();
            Assert.IsTrue(grant.Success);

            SpendResult spend = resolved.TrySpendCreditsAsync(5).GetAwaiter().GetResult();
            Assert.IsTrue(spend.Success);
            Assert.AreEqual(15, spend.NewBalance);
            Assert.AreEqual(15, resolved.Balance);
            Assert.AreEqual(15, store.Stored.Credits, "The round-trip balance is persisted.");
        }
    }
}
