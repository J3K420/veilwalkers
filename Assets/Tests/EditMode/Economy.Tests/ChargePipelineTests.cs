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
    /// The AR-8 progression-pipeline guarantees: the AR-20 charge-atomicity test
    /// (forced persist throw), full-state rollback for the XP/level/charge path, the
    /// recovery-swap guard, the cross-service serialization that justifies the shared
    /// <see cref="SaveMutationLock"/>, the mid-encounter usable-immediately edge, and
    /// the locator wiring happy path (constructed and registered like Bootstrap does,
    /// NOT via Veilwalkers.App, which EditMode tests must not reference).
    /// </summary>
    public sealed class ChargePipelineTests
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

        private static ProgressionRules Rules(
            IReadOnlyList<int> thresholds,
            int strong = 1, int stability = 1, int nightveil = 1) =>
            new ProgressionRules(thresholds, strong, stability, nightveil);

        private static (FakeProgressStore store, SaveService save, ProgressionService progression)
            CreateInitialized(ProgressionRules rules, SaveModel seed)
        {
            var store = new FakeProgressStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            return (store, save, new ProgressionService(save, rules, new SaveMutationLock()));
        }

        [Test]
        public void Consume_persist_failure_rolls_back_charge_and_reports_PersistenceFailed()
        {
            // AC-3: the AR-20 charge-atomicity guarantee test.
            var seed = new SaveModel { StrongCaptureCharges = 2 };
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100 }), seed);
            store.FailNextSave = true;

            int chargeEvents = 0;
            progression.OnChargesChanged += (_, __) => chargeEvents++;

            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Error, new Regex("ProgressionService: charge consume .* rolled back"));

            SpendResult result =
                progression.TryConsumeChargeAsync(ChargeType.StrongCapture).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SpendFailureReason.PersistenceFailed, result.FailureReason);
            Assert.AreEqual(2, result.NewBalance, "The result reports the restored prior count.");
            Assert.AreEqual(2, progression.GetChargeCount(ChargeType.StrongCapture),
                "The charge count is INTACT after the failed persist.");
            Assert.AreEqual(1, store.SaveCalls, "The persist was attempted exactly once.");
            Assert.AreEqual(2, store.Stored.StrongCaptureCharges,
                "No new persisted snapshot — the stored count is untouched.");
            Assert.AreEqual(0, chargeEvents, "No event for an uncommitted consume.");
        }

        [Test]
        public void AddCharge_persist_failure_rolls_back_and_returns_failed_Result()
        {
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100 }), new SaveModel());
            store.FailNextSave = true;

            int chargeEvents = 0;
            progression.OnChargesChanged += (_, __) => chargeEvents++;

            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Error, new Regex("ProgressionService: charge grant .* rolled back"));

            Result result = progression.AddChargeAsync(ChargeType.StabilityBoost).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.Message);
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.StabilityBoost),
                "The grant must be rolled back.");
            Assert.AreEqual(1, store.SaveCalls, "No retry.");
            Assert.AreEqual(0, chargeEvents);
        }

        [Test]
        public void AddXp_persist_failure_rolls_back_xp_level_and_all_charges()
        {
            // Seed JUST below a threshold so the add would cross it and grant charges;
            // a fault must restore XP, level, AND every charge count.
            var seed = new SaveModel { Xp = 90, Level = 0 };
            var (store, save, progression) = CreateInitialized(
                Rules(new[] { 100, 250 }, strong: 1, stability: 1, nightveil: 1), seed);
            store.FailNextSave = true;

            int eventCount = 0;
            progression.OnXpChanged += _ => eventCount++;
            progression.OnLevelChanged += _ => eventCount++;
            progression.OnChargesChanged += (_, __) => eventCount++;

            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Error, new Regex("ProgressionService: XP grant of 50 rolled back"));

            Result result = progression.AddXpAsync(50).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(90, progression.Xp, "XP restored.");
            Assert.AreEqual(0, progression.Level, "Level restored.");
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.StrongCapture), "Charges restored.");
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.StabilityBoost));
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.NightveilFilter));
            Assert.AreEqual(1, store.SaveCalls, "Persist attempted exactly once — no retry.");
            Assert.AreEqual(90, store.Stored.Xp, "The persisted snapshot is untouched by the failed save.");
            Assert.AreEqual(0, eventCount, "No events for an uncommitted XP add.");
        }

        [Test]
        public void Recovery_swap_mid_consume_reports_PersistenceFailed_not_phantom_success()
        {
            var seed = new SaveModel { StrongCaptureCharges = 2 };
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100 }), seed);

            int chargeEvents = 0;
            progression.OnChargesChanged += (_, __) => chargeEvents++;

            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.SaveGate = gate;

            // The consume decrements its captured model and is held open at the persist.
            Task<SpendResult> consume = progression.TryConsumeChargeAsync(ChargeType.StrongCapture);
            Assert.IsFalse(consume.IsCompleted);

            // Recovery races the in-flight mutation: RetryLoadAsync publishes a FRESH
            // model object (the fake clones on load), so Current is no longer the
            // consume's captured model.
            store.SaveGate = null;
            save.RetryLoadAsync().GetAwaiter().GetResult();

            LogAssert.Expect(LogType.Error,
                new Regex("ProgressionService: charge consume .* rolled back — the save model was swapped"));

            gate.SetResult(true);
            SpendResult result = consume.GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "A mid-operation model swap must never report a phantom success.");
            Assert.AreEqual(SpendFailureReason.PersistenceFailed, result.FailureReason);
            Assert.AreEqual(2, result.NewBalance, "The result reports the restored prior count.");
            Assert.AreEqual(2, progression.GetChargeCount(ChargeType.StrongCapture),
                "Reads the swapped-in (recovered) model, which still has 2.");
            Assert.AreEqual(0, chargeEvents, "No event for a consume that did not commit.");
        }

        [Test]
        public void Recovery_swap_mid_addxp_rolls_back_xp_level_and_charges_with_no_phantom_event()
        {
            // The widest-rollback path: a recovery swap mid-persist of AddXpAsync must
            // restore XP, level, AND all three charge counts on the captured model, fail
            // the result, and raise NO event (mirrors the consume-swap guard).
            var seed = new SaveModel { Xp = 90, Level = 0 };
            var (store, save, progression) = CreateInitialized(
                Rules(new[] { 100, 250 }, strong: 1, stability: 1, nightveil: 1), seed);

            int eventCount = 0;
            progression.OnXpChanged += _ => eventCount++;
            progression.OnLevelChanged += _ => eventCount++;
            progression.OnChargesChanged += (_, __) => eventCount++;

            // Capture the model AddXpAsync will operate on BEFORE the swap. The service
            // captures _saveService.Current too, so this is the SAME object it rolls back.
            // Asserting on `captured` (not progression.Xp, which reads the swapped-in
            // recovered model) is what actually pins Rollback — delete the swap-branch
            // Rollback call and these assertions go red.
            SaveModel captured = save.Current;

            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.SaveGate = gate;

            // The add crosses the threshold (grants charges) and is held at its persist.
            Task<Result> add = progression.AddXpAsync(50);
            Assert.IsFalse(add.IsCompleted);

            // Recovery publishes a FRESH model object: Current is no longer the captured
            // model, so the post-persist ReferenceEquals check fails.
            store.SaveGate = null;
            save.RetryLoadAsync().GetAwaiter().GetResult();
            Assert.AreNotSame(captured, save.Current, "Recovery must have swapped in a fresh model.");

            LogAssert.Expect(LogType.Error,
                new Regex("ProgressionService: XP grant of 50 rolled back — the save model was swapped"));

            gate.SetResult(true);
            Result result = add.GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "A mid-operation model swap must never report a phantom success.");

            // The load-bearing assertions: the CAPTURED model must have every mutated
            // field rolled back to its prior value (XP 90, level 0, all charges 0),
            // independent of the recovered model's state.
            Assert.AreEqual(90, captured.Xp, "Captured model's XP rolled back.");
            Assert.AreEqual(0, captured.Level, "Captured model's level rolled back.");
            Assert.AreEqual(0, captured.StrongCaptureCharges, "Captured model's charges rolled back.");
            Assert.AreEqual(0, captured.StabilityBoostCharges);
            Assert.AreEqual(0, captured.NightveilFilterCharges);
            Assert.AreEqual(0, eventCount, "No event for an XP add that did not commit.");
        }

        [Test]
        public void Recovery_swap_mid_addcharge_reports_failure_with_no_phantom_event()
        {
            var seed = new SaveModel { StabilityBoostCharges = 3 };
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100 }), seed);

            int chargeEvents = 0;
            progression.OnChargesChanged += (_, __) => chargeEvents++;

            // Same object AddChargeAsync captures; assert rollback on it, not on the
            // recovered model that progression.GetChargeCount would read post-swap.
            SaveModel captured = save.Current;

            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.SaveGate = gate;

            Task<Result> grant = progression.AddChargeAsync(ChargeType.StabilityBoost);
            Assert.IsFalse(grant.IsCompleted);

            store.SaveGate = null;
            save.RetryLoadAsync().GetAwaiter().GetResult();
            Assert.AreNotSame(captured, save.Current, "Recovery must have swapped in a fresh model.");

            LogAssert.Expect(LogType.Error,
                new Regex("ProgressionService: charge grant .* rolled back — the save model was swapped"));

            gate.SetResult(true);
            Result result = grant.GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "A mid-operation model swap must never report a phantom success.");
            Assert.AreEqual(3, captured.StabilityBoostCharges,
                "The captured model's grant is rolled back to the prior count.");
            Assert.AreEqual(0, chargeEvents, "No event for a grant that did not commit.");
        }

        [Test]
        public void Charge_consume_does_not_mutate_while_a_credit_spend_holds_the_shared_lock()
        {
            // The test that justifies the shared SaveMutationLock: a credit spend and a
            // charge consume on the SAME model must serialize, not interleave.
            var seed = new SaveModel { Credits = 10, StrongCaptureCharges = 1 };
            var store = new FakeProgressStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();

            var sharedLock = new SaveMutationLock();
            var credits = new CreditService(save, sharedLock);
            var progression = new ProgressionService(save, Rules(new[] { 100 }), sharedLock);

            // Hold every save open; continuations resume asynchronously on release so
            // the test thread is not hijacked by the resuming pipeline.
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.SaveGate = gate;

            Task<SpendResult> spend = credits.TrySpendCreditsAsync(3);
            Task<SpendResult> consume = progression.TryConsumeChargeAsync(ChargeType.StrongCapture);

            // The spend deducted and is held at its persist; the consume is parked at
            // the SHARED lock and must NOT have decremented the charge yet.
            Assert.IsFalse(spend.IsCompleted);
            Assert.IsFalse(consume.IsCompleted);
            Assert.AreEqual(7, save.Current.Credits, "Only the spend's deduction is visible.");
            Assert.AreEqual(1, save.Current.StrongCaptureCharges,
                "The charge consume must not mutate while the spend holds the shared lock.");
            Assert.AreEqual(1, store.SaveCalls, "The consume must not reach its persist yet.");

            gate.SetResult(true);
            Task.WhenAll(spend, consume).GetAwaiter().GetResult();

            Assert.IsTrue(spend.Result.Success);
            Assert.IsTrue(consume.Result.Success);
            Assert.AreEqual(7, save.Current.Credits, "Both commit once the gate opens.");
            Assert.AreEqual(0, save.Current.StrongCaptureCharges);
            Assert.AreEqual(2, store.SaveCalls, "Exactly one persist per mutation.");
        }

        [Test]
        public void Charge_granted_by_level_up_is_consumable_immediately_in_the_same_session()
        {
            // Architecture boundary rule: a charge granted mid-encounter is usable now.
            var seed = new SaveModel { Xp = 0, StrongCaptureCharges = 0 };
            var (store, save, progression) = CreateInitialized(
                Rules(new[] { 100 }, strong: 1, stability: 0, nightveil: 0), seed);

            // Level up: grants one StrongCapture charge.
            progression.AddXpAsync(100).GetAwaiter().GetResult();
            Assert.AreEqual(1, progression.Level);
            Assert.AreEqual(1, progression.GetChargeCount(ChargeType.StrongCapture));

            // Immediately consume it — must succeed in the same session.
            SpendResult result =
                progression.TryConsumeChargeAsync(ChargeType.StrongCapture).GetAwaiter().GetResult();
            Assert.IsTrue(result.Success, "A charge granted by level-up is consumable immediately.");
            Assert.AreEqual(0, result.NewBalance);
            Assert.AreEqual(2, store.SaveCalls, "One persist for the level-up, one for the consume.");
        }

        [Test]
        public void Locator_wiring_happy_path_addxp_and_consume_round_trip()
        {
            // Mirrors Bootstrap's wiring (construct → register → seal) without
            // referencing Veilwalkers.App: both services share ONE mutation lock.
            var store = new FakeProgressStore();
            var save = new SaveService(store);
            var sharedLock = new SaveMutationLock();
            var credits = new CreditService(save, sharedLock);
            var progression = new ProgressionService(
                save, Rules(new[] { 100 }, strong: 1, stability: 1, nightveil: 1), sharedLock);

            GameServices.Register<ICreditService>(credits);
            GameServices.Register<IProgressionService>(progression);
            GameServices.MarkReady();

            IProgressionService resolved = GameServices.Get<IProgressionService>();
            Assert.AreSame(progression, resolved);

            save.InitializeAsync().GetAwaiter().GetResult();

            // AddXp to level up, then consume a granted charge round-trip.
            Result add = resolved.AddXpAsync(100).GetAwaiter().GetResult();
            Assert.IsTrue(add.Success);
            Assert.AreEqual(1, resolved.Level);
            Assert.AreEqual(1, resolved.GetChargeCount(ChargeType.StrongCapture));

            SpendResult consume =
                resolved.TryConsumeChargeAsync(ChargeType.StrongCapture).GetAwaiter().GetResult();
            Assert.IsTrue(consume.Success);
            Assert.AreEqual(0, consume.NewBalance);
            Assert.AreEqual(0, store.Stored.StrongCaptureCharges, "The round-trip state is persisted.");
            Assert.AreEqual(1, store.Stored.Level);
        }
    }
}
