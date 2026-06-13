using System;
using System.Collections.Generic;
using NUnit.Framework;
using Veilwalkers.Core;
using Veilwalkers.Persistence;

namespace Veilwalkers.Economy.Tests
{
    /// <summary>
    /// <see cref="ProgressionService"/> surface behavior: XP-to-level math, level-up
    /// charge grants, the AR-17 single-charge grant, charge consume (happy + the
    /// AC-2 zero block), the post-persist event contract, and the programmer-error
    /// guards — plus <see cref="ProgressionRules"/> construction validation. Negative
    /// paths written in the first pass (the 1.3/1.4 lesson). Pipeline/concurrency and
    /// rollback tests live in <see cref="ChargePipelineTests"/>.
    /// </summary>
    public sealed class ProgressionServiceTests
    {
        private static ProgressionRules Rules(
            IReadOnlyList<int> thresholds,
            int strong = 1, int stability = 1, int nightveil = 1) =>
            new ProgressionRules(thresholds, strong, stability, nightveil);

        private static (FakeProgressStore store, SaveService save, ProgressionService progression)
            CreateInitialized(ProgressionRules rules, SaveModel seed = null)
        {
            var store = new FakeProgressStore { Stored = seed ?? new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            return (store, save, new ProgressionService(save, rules, new SaveMutationLock()));
        }

        [Test]
        public void AddXp_below_threshold_updates_xp_only_no_level_or_charges()
        {
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100, 250 }));

            var xpPayloads = new List<int>();
            int levelEvents = 0;
            int chargeEvents = 0;
            progression.OnXpChanged += xp => xpPayloads.Add(xp);
            progression.OnLevelChanged += _ => levelEvents++;
            progression.OnChargesChanged += (_, __) => chargeEvents++;

            Result result = progression.AddXpAsync(50).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(50, progression.Xp);
            Assert.AreEqual(0, progression.Level);
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.StrongCapture));
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.StabilityBoost));
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.NightveilFilter));
            Assert.AreEqual(1, store.SaveCalls, "One persist for the XP add.");
            Assert.AreEqual(50, store.Stored.Xp, "The persisted snapshot holds the new XP.");
            CollectionAssert.AreEqual(new[] { 50 }, xpPayloads, "OnXpChanged fires once with the new XP.");
            Assert.AreEqual(0, levelEvents, "No level change ⇒ no OnLevelChanged.");
            Assert.AreEqual(0, chargeEvents, "No charges granted ⇒ no OnChargesChanged.");
        }

        [Test]
        public void Single_level_up_grants_one_bundle_in_one_persist_and_raises_all_events()
        {
            var (store, save, progression) = CreateInitialized(
                Rules(new[] { 100, 250 }, strong: 1, stability: 2, nightveil: 3));

            var order = new List<string>();
            int xpPayload = -1;
            int levelPayload = -1;
            var chargePayloads = new List<(ChargeType type, int count)>();
            save.OnSaveCompleted += () => order.Add("persisted");
            progression.OnXpChanged += xp => { order.Add("xp"); xpPayload = xp; };
            progression.OnLevelChanged += lvl => { order.Add("level"); levelPayload = lvl; };
            progression.OnChargesChanged += (t, c) => { order.Add("charge"); chargePayloads.Add((t, c)); };

            Result result = progression.AddXpAsync(100).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(100, progression.Xp);
            Assert.AreEqual(1, progression.Level, "Exactly-met threshold (Xp == 100) reaches level 1.");
            Assert.AreEqual(1, progression.GetChargeCount(ChargeType.StrongCapture));
            Assert.AreEqual(2, progression.GetChargeCount(ChargeType.StabilityBoost));
            Assert.AreEqual(3, progression.GetChargeCount(ChargeType.NightveilFilter));
            Assert.AreEqual(1, store.SaveCalls, "XP + level + all charges commit in ONE persist.");
            Assert.AreEqual(1, store.Stored.StrongCaptureCharges);
            Assert.AreEqual(1, store.Stored.Level);

            Assert.AreEqual("persisted", order[0], "All events fire AFTER the committed persist.");
            Assert.AreEqual(100, xpPayload, "OnXpChanged carries the new lifetime XP, not the level.");
            Assert.AreEqual(1, levelPayload, "OnLevelChanged carries the new level.");
            CollectionAssert.AreEquivalent(
                new[]
                {
                    (ChargeType.StrongCapture, 1),
                    (ChargeType.StabilityBoost, 2),
                    (ChargeType.NightveilFilter, 3)
                },
                chargePayloads,
                "One OnChargesChanged per type with its new count.");
        }

        [Test]
        public void Multi_level_crossing_in_one_call_grants_two_bundles_in_one_persist()
        {
            var (store, save, progression) = CreateInitialized(
                Rules(new[] { 100, 250 }, strong: 1, stability: 1, nightveil: 1));

            Result result = progression.AddXpAsync(300).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(300, progression.Xp);
            Assert.AreEqual(2, progression.Level, "300 crosses both thresholds ⇒ level 2.");
            Assert.AreEqual(2, progression.GetChargeCount(ChargeType.StrongCapture),
                "Two level-ups ⇒ two bundles.");
            Assert.AreEqual(2, progression.GetChargeCount(ChargeType.StabilityBoost));
            Assert.AreEqual(2, progression.GetChargeCount(ChargeType.NightveilFilter));
            Assert.AreEqual(1, store.SaveCalls, "Still ONE persist for the whole multi-level grant.");
        }

        [Test]
        public void Exact_threshold_boundary_counts_as_reached()
        {
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100, 250 }));

            // 250 exactly = the second threshold.
            progression.AddXpAsync(250).GetAwaiter().GetResult();

            Assert.AreEqual(2, progression.Level, "Xp == threshold exactly counts as reached (>=).");
        }

        [Test]
        public void Zero_grant_type_raises_no_charge_event_and_stays_zero()
        {
            // StabilityBoost grants 0 per level-up; the other two grant 1.
            var (store, save, progression) = CreateInitialized(
                Rules(new[] { 100 }, strong: 1, stability: 0, nightveil: 1));

            var chargedTypes = new List<ChargeType>();
            progression.OnChargesChanged += (t, _) => chargedTypes.Add(t);

            progression.AddXpAsync(100).GetAwaiter().GetResult();

            Assert.AreEqual(1, progression.Level);
            Assert.AreEqual(1, progression.GetChargeCount(ChargeType.StrongCapture));
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.StabilityBoost),
                "A zero-grant type stays at zero.");
            Assert.AreEqual(1, progression.GetChargeCount(ChargeType.NightveilFilter));
            CollectionAssert.AreEquivalent(
                new[] { ChargeType.StrongCapture, ChargeType.NightveilFilter },
                chargedTypes,
                "No OnChargesChanged for the zero-grant type; the other two fire.");
        }

        [Test]
        public void Level_caps_at_threshold_count_with_no_further_grants()
        {
            var (store, save, progression) = CreateInitialized(
                Rules(new[] { 100, 250 }, strong: 1, stability: 1, nightveil: 1));

            // Blow far past the last threshold in one add.
            progression.AddXpAsync(10_000).GetAwaiter().GetResult();
            Assert.AreEqual(2, progression.Level, "Level caps at the threshold count.");
            Assert.AreEqual(2, progression.GetChargeCount(ChargeType.StrongCapture),
                "Capped at 2 level-ups ⇒ 2 charges, even though XP is far past.");

            int chargeEventsAfterCap = 0;
            progression.OnChargesChanged += (_, __) => chargeEventsAfterCap++;
            int levelEventsAfterCap = 0;
            progression.OnLevelChanged += _ => levelEventsAfterCap++;

            // A further add past the cap grants nothing and changes no level.
            progression.AddXpAsync(5_000).GetAwaiter().GetResult();
            Assert.AreEqual(2, progression.Level);
            Assert.AreEqual(2, progression.GetChargeCount(ChargeType.StrongCapture),
                "No further grants once capped.");
            Assert.AreEqual(0, levelEventsAfterCap, "No level change past the cap.");
            Assert.AreEqual(0, chargeEventsAfterCap, "No charge change past the cap.");
        }

        [Test]
        public void AddCharge_increments_only_the_targeted_type_and_persists()
        {
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100 }));

            var chargePayloads = new List<(ChargeType type, int count)>();
            progression.OnChargesChanged += (t, c) => chargePayloads.Add((t, c));

            Result result = progression.AddChargeAsync(ChargeType.StabilityBoost).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, progression.GetChargeCount(ChargeType.StabilityBoost));
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.StrongCapture),
                "Other types untouched.");
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.NightveilFilter));
            Assert.AreEqual(1, store.SaveCalls);
            Assert.AreEqual(1, store.Stored.StabilityBoostCharges, "The grant is persisted.");
            CollectionAssert.AreEqual(
                new[] { (ChargeType.StabilityBoost, 1) }, chargePayloads,
                "OnChargesChanged carries (type, newCount) once.");
        }

        [Test]
        public void Consume_happy_path_decrements_persists_and_never_touches_credits()
        {
            // Nonzero credit balance so the never-touches-Credits pin is meaningful.
            var seed = new SaveModel { Credits = 42, StrongCaptureCharges = 1 };
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100 }), seed);

            var chargePayloads = new List<(ChargeType type, int count)>();
            progression.OnChargesChanged += (t, c) => chargePayloads.Add((t, c));

            SpendResult result =
                progression.TryConsumeChargeAsync(ChargeType.StrongCapture).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.NewBalance, "NewBalance carries the remaining charge count of that type.");
            Assert.AreEqual(SpendFailureReason.None, result.FailureReason);
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.StrongCapture));
            Assert.AreEqual(1, store.SaveCalls);
            Assert.AreEqual(0, store.Stored.StrongCaptureCharges, "The decrement is persisted.");
            Assert.AreEqual(42, save.Current.Credits, "A charge consume must NEVER touch Credits.");
            Assert.AreEqual(42, store.Stored.Credits, "Credits unchanged in the persisted snapshot too.");
            CollectionAssert.AreEqual(new[] { (ChargeType.StrongCapture, 0) }, chargePayloads);
        }

        [Test]
        public void Consume_with_zero_charges_is_blocked_with_InsufficientCharges_no_persist_no_event()
        {
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100 }));

            int chargeEvents = 0;
            progression.OnChargesChanged += (_, __) => chargeEvents++;

            SpendResult result =
                progression.TryConsumeChargeAsync(ChargeType.NightveilFilter).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SpendFailureReason.InsufficientCharges, result.FailureReason,
                "Zero-charge block is the typed 'earn via XP' result.");
            Assert.AreEqual(0, result.NewBalance);
            Assert.AreEqual(0, progression.GetChargeCount(ChargeType.NightveilFilter),
                "Count stays at zero — never negative.");
            Assert.AreEqual(0, store.SaveCalls, "A blocked consume must not persist.");
            Assert.AreEqual(0, chargeEvents, "A blocked consume raises no event.");
        }

        [Test]
        public void AddXp_non_positive_throws_ArgumentOutOfRange_without_persist_or_event()
        {
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100 }));

            int eventCount = 0;
            progression.OnXpChanged += _ => eventCount++;
            progression.OnLevelChanged += _ => eventCount++;
            progression.OnChargesChanged += (_, __) => eventCount++;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => progression.AddXpAsync(0).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(
                () => progression.AddXpAsync(-5).GetAwaiter().GetResult());

            Assert.AreEqual(0, progression.Xp, "Guard throws leave XP untouched.");
            Assert.AreEqual(0, store.SaveCalls);
            Assert.AreEqual(0, eventCount, "Argument guards raise no events.");
        }

        [Test]
        public void Undefined_charge_type_throws_from_all_three_charge_methods()
        {
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100 }));
            var bogus = (ChargeType)99;

            int eventCount = 0;
            progression.OnChargesChanged += (_, __) => eventCount++;

            Assert.Throws<ArgumentOutOfRangeException>(() => _ = progression.GetChargeCount(bogus));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => progression.AddChargeAsync(bogus).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(
                () => progression.TryConsumeChargeAsync(bogus).GetAwaiter().GetResult());

            Assert.AreEqual(0, store.SaveCalls, "Undefined-type guards must not persist.");
            Assert.AreEqual(0, eventCount, "Undefined-type guards raise no events.");
        }

        [Test]
        public void Use_before_initialize_throws_InvalidOperation_from_getters_and_mutators()
        {
            // SaveService never initialized: Current is null — the Bootstrap window.
            var store = new FakeProgressStore();
            var progression =
                new ProgressionService(new SaveService(store), Rules(new[] { 100 }), new SaveMutationLock());

            int eventCount = 0;
            progression.OnXpChanged += _ => eventCount++;
            progression.OnChargesChanged += (_, __) => eventCount++;

            Assert.Throws<InvalidOperationException>(() => _ = progression.Xp);
            Assert.Throws<InvalidOperationException>(() => _ = progression.Level);
            Assert.Throws<InvalidOperationException>(() => _ = progression.GetChargeCount(ChargeType.StrongCapture));
            Assert.Throws<InvalidOperationException>(
                () => progression.AddXpAsync(10).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(
                () => progression.AddChargeAsync(ChargeType.StrongCapture).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(
                () => progression.TryConsumeChargeAsync(ChargeType.StrongCapture).GetAwaiter().GetResult());

            Assert.AreEqual(0, store.SaveCalls, "An uninitialized service must never persist.");
            Assert.AreEqual(0, eventCount, "An uninitialized service must never raise events.");
        }

        [Test]
        public void AddXp_overflowing_lifetime_xp_throws_and_leaves_state_untouched()
        {
            var seed = new SaveModel { Xp = int.MaxValue };
            var (store, save, progression) = CreateInitialized(Rules(new[] { 100 }), seed);

            int eventCount = 0;
            progression.OnXpChanged += _ => eventCount++;

            Assert.Throws<OverflowException>(
                () => progression.AddXpAsync(1).GetAwaiter().GetResult());

            Assert.AreEqual(int.MaxValue, save.Current.Xp,
                "checked arithmetic rejects the overflow before any mutation lands.");
            Assert.AreEqual(0, store.SaveCalls);
            Assert.AreEqual(0, eventCount);
        }

        [Test]
        public void Constructing_with_null_dependency_throws_ArgumentNull()
        {
            var store = new FakeProgressStore();
            var save = new SaveService(store);
            ProgressionRules rules = Rules(new[] { 100 });

            Assert.Throws<ArgumentNullException>(
                () => new ProgressionService(null, rules, new SaveMutationLock()));
            Assert.Throws<ArgumentNullException>(
                () => new ProgressionService(save, null, new SaveMutationLock()));
            Assert.Throws<ArgumentNullException>(
                () => new ProgressionService(save, rules, null));
        }

        // ----- ProgressionRules construction validation -----

        [Test]
        public void ProgressionRules_rejects_null_or_empty_thresholds()
        {
            Assert.Throws<ArgumentNullException>(() => new ProgressionRules(null, 1, 1, 1));
            Assert.Throws<ArgumentException>(() => new ProgressionRules(new int[0], 1, 1, 1));
        }

        [Test]
        public void ProgressionRules_rejects_non_positive_or_non_increasing_thresholds()
        {
            Assert.Throws<ArgumentException>(() => new ProgressionRules(new[] { 0 }, 1, 1, 1),
                "Zero threshold is not positive.");
            Assert.Throws<ArgumentException>(() => new ProgressionRules(new[] { -10 }, 1, 1, 1));
            Assert.Throws<ArgumentException>(() => new ProgressionRules(new[] { 100, 100 }, 1, 1, 1),
                "Equal consecutive thresholds are not strictly increasing.");
            Assert.Throws<ArgumentException>(() => new ProgressionRules(new[] { 100, 50 }, 1, 1, 1),
                "Decreasing thresholds are rejected.");
        }

        [Test]
        public void ProgressionRules_rejects_negative_grants()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ProgressionRules(new[] { 100 }, -1, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ProgressionRules(new[] { 100 }, 1, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ProgressionRules(new[] { 100 }, 1, 1, -1));
        }

        [Test]
        public void ProgressionRules_zero_grants_are_allowed()
        {
            Assert.DoesNotThrow(() => new ProgressionRules(new[] { 100 }, 0, 0, 0),
                "A zero grant for a type is legal — that type simply earns nothing on level-up.");
        }
    }
}
