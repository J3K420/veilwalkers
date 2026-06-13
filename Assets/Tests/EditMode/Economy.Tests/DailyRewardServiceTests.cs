using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.Core;
using Veilwalkers.Persistence;

namespace Veilwalkers.Economy.Tests
{
    /// <summary>
    /// <see cref="DailyRewardService"/> claim rule (Story 1.8): once-per-UTC-day grant
    /// of the data-driven amount, same-day refusal, day-rollover re-enablement, the
    /// UTC-not-local decision, the single-atomic-write rollback, and the before-load
    /// guard. Built on the Economy.Tests fixture (FakeProgressStore + real SaveService),
    /// with a <see cref="FakeClock"/> driving the calendar day and an in-memory
    /// <see cref="EconomyConfig"/> supplying the reward amount.
    /// </summary>
    public sealed class DailyRewardServiceTests
    {
        // A fixed reference instant: 2026-06-13 10:00 UTC.
        private static readonly DateTime RefUtc =
            new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);

        private static EconomyConfig ConfigWithReward(int dailyRewardCredits)
        {
            var config = ScriptableObject.CreateInstance<EconomyConfig>();
            config.SetForTests(
                basicLureCost: 1, premiumLureCost: 4, multiLureCost: 5, slayCost: 3,
                xpPerCapture: 10, xpPerSlay: 25,
                levelXpThresholds: new[] { 100, 250, 450, 700, 1000 },
                strongCapturePerLevelUp: 1, stabilityBoostPerLevelUp: 1, nightveilFilterPerLevelUp: 1,
                dailyRewardCredits: dailyRewardCredits,
                adDailyCap: 3, telemetryRetentionDays: 30);
            return config;
        }

        private static (FakeProgressStore store, SaveService save, FakeClock clock, DailyRewardService daily)
            CreateInitialized(SaveModel seed, DateTime utcNow, int reward = 5)
        {
            var store = new FakeProgressStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var clock = new FakeClock(utcNow);
            var daily = new DailyRewardService(save, new SaveMutationLock(), clock, ConfigWithReward(reward));
            return (store, save, clock, daily);
        }

        // ---- AC-1: first claim grants the configured amount and stamps today ----

        [Test]
        public void First_claim_grants_configured_amount_stamps_today_and_persists()
        {
            var (store, _, _, daily) = CreateInitialized(
                new SaveModel { Credits = 0, DailyClaim = null }, RefUtc, reward: 5);

            DailyRewardResult result = daily.TryClaimAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result.Claimed);
            Assert.AreEqual(DailyRewardFailureReason.None, result.FailureReason);
            Assert.AreEqual(5, result.NewBalance);
            // Persisted, not just in memory: the fake store's snapshot proves the write.
            Assert.AreEqual(5, store.Stored.Credits);
            Assert.AreEqual("2026-06-13", store.Stored.DailyClaim);
        }

        [Test]
        public void First_claim_raises_OnDailyRewardClaimed_with_the_new_balance()
        {
            var (_, _, _, daily) = CreateInitialized(
                new SaveModel { Credits = 2, DailyClaim = null }, RefUtc, reward: 5);

            int? raised = null;
            daily.OnDailyRewardClaimed += b => raised = b;

            daily.TryClaimAsync().GetAwaiter().GetResult();

            Assert.AreEqual(7, raised);
        }

        // ---- AC-2: a second claim the same UTC day is refused, balance unchanged ----

        [Test]
        public void Second_claim_same_day_is_refused_balance_and_date_unchanged()
        {
            // Seed already claimed today.
            var (store, _, _, daily) = CreateInitialized(
                new SaveModel { Credits = 7, DailyClaim = "2026-06-13" }, RefUtc, reward: 5);
            int savesBefore = store.SaveCalls;

            DailyRewardResult result = daily.TryClaimAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result.Claimed);
            Assert.AreEqual(DailyRewardFailureReason.AlreadyClaimedToday, result.FailureReason);
            Assert.AreEqual(7, result.NewBalance);
            Assert.AreEqual(7, store.Stored.Credits);
            Assert.AreEqual("2026-06-13", store.Stored.DailyClaim);
            // A refused claim performs NO persist.
            Assert.AreEqual(savesBefore, store.SaveCalls);
        }

        [Test]
        public void Two_claims_in_a_row_grant_once_not_twice()
        {
            var (store, _, _, daily) = CreateInitialized(
                new SaveModel { Credits = 0, DailyClaim = null }, RefUtc, reward: 5);

            DailyRewardResult first = daily.TryClaimAsync().GetAwaiter().GetResult();
            DailyRewardResult second = daily.TryClaimAsync().GetAwaiter().GetResult();

            Assert.IsTrue(first.Claimed);
            Assert.IsFalse(second.Claimed);
            Assert.AreEqual(DailyRewardFailureReason.AlreadyClaimedToday, second.FailureReason);
            Assert.AreEqual(5, store.Stored.Credits); // 5, not 10 — the same-day guard held.
        }

        [Test]
        public void CanClaimToday_is_true_when_unclaimed_and_false_after_claiming()
        {
            var (_, _, _, daily) = CreateInitialized(
                new SaveModel { Credits = 0, DailyClaim = null }, RefUtc, reward: 5);

            Assert.IsTrue(daily.CanClaimToday);
            daily.TryClaimAsync().GetAwaiter().GetResult();
            Assert.IsFalse(daily.CanClaimToday);
        }

        // ---- AC-3: the reward re-enables after the UTC day rolls over ----

        [Test]
        public void Claim_becomes_available_again_after_utc_day_rolls_over()
        {
            var (store, _, clock, daily) = CreateInitialized(
                new SaveModel { Credits = 0, DailyClaim = null }, RefUtc, reward: 5);

            daily.TryClaimAsync().GetAwaiter().GetResult();
            Assert.IsFalse(daily.CanClaimToday);

            // Advance past midnight UTC into the next calendar day.
            clock.Advance(TimeSpan.FromDays(1));

            Assert.IsTrue(daily.CanClaimToday);
            DailyRewardResult second = daily.TryClaimAsync().GetAwaiter().GetResult();

            Assert.IsTrue(second.Claimed);
            Assert.AreEqual(10, second.NewBalance); // 5 + 5 across two days.
            Assert.AreEqual("2026-06-14", store.Stored.DailyClaim);
        }

        [Test]
        public void Same_utc_day_later_time_is_still_refused_day_granular_not_instant()
        {
            // Claim at 10:00 UTC.
            var (_, _, clock, daily) = CreateInitialized(
                new SaveModel { Credits = 0, DailyClaim = null }, RefUtc, reward: 5);
            daily.TryClaimAsync().GetAwaiter().GetResult();

            // Move to 23:59 UTC the SAME day — still the same calendar day.
            clock.UtcNow = new DateTime(2026, 6, 13, 23, 59, 0, DateTimeKind.Utc);

            Assert.IsFalse(daily.CanClaimToday);
            DailyRewardResult result = daily.TryClaimAsync().GetAwaiter().GetResult();
            Assert.IsFalse(result.Claimed);
            Assert.AreEqual(DailyRewardFailureReason.AlreadyClaimedToday, result.FailureReason);
        }

        [Test]
        public void A_future_dated_stored_claim_is_refused_not_treated_as_claimable()
        {
            // A tampered/corrupt save (or a clock that jumped backward) leaves DailyClaim
            // AHEAD of today. The guard is "claimable iff stored < today", so a future date
            // must be refused — a plain "!= today" check would wrongly re-open the claim and
            // permit a same-day double-grant once the real day catches up.
            var (store, _, _, daily) = CreateInitialized(
                new SaveModel { Credits = 3, DailyClaim = "2026-06-15" }, RefUtc, reward: 5);
            int savesBefore = store.SaveCalls;

            Assert.IsFalse(daily.CanClaimToday);
            DailyRewardResult result = daily.TryClaimAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result.Claimed);
            Assert.AreEqual(DailyRewardFailureReason.AlreadyClaimedToday, result.FailureReason);
            Assert.AreEqual(3, store.Stored.Credits);
            Assert.AreEqual("2026-06-15", store.Stored.DailyClaim);
            Assert.AreEqual(savesBefore, store.SaveCalls); // no persist on a refused claim
        }

        [Test]
        public void Day_key_is_the_utc_date_not_the_device_local_date()
        {
            // 2026-06-13 23:30 UTC. In a positive-offset local zone (e.g. UTC+2) the LOCAL
            // date would already be 2026-06-14, but the claim must key off the UTC date.
            var utc = new DateTime(2026, 6, 13, 23, 30, 0, DateTimeKind.Utc);
            var (store, _, _, daily) = CreateInitialized(
                new SaveModel { Credits = 0, DailyClaim = null }, utc, reward: 5);

            daily.TryClaimAsync().GetAwaiter().GetResult();

            // Stamped with the UTC date, regardless of the machine's local time zone.
            Assert.AreEqual("2026-06-13", store.Stored.DailyClaim);
        }

        // ---- AC-4: the amount is data-driven, not hard-coded ----

        [Test]
        public void Granted_amount_follows_the_config_value()
        {
            var smallReward = CreateInitialized(
                new SaveModel { Credits = 0, DailyClaim = null }, RefUtc, reward: 5);
            var largeReward = CreateInitialized(
                new SaveModel { Credits = 0, DailyClaim = null }, RefUtc, reward: 50);

            DailyRewardResult small = smallReward.daily.TryClaimAsync().GetAwaiter().GetResult();
            DailyRewardResult large = largeReward.daily.TryClaimAsync().GetAwaiter().GetResult();

            Assert.AreEqual(5, small.NewBalance);
            Assert.AreEqual(50, large.NewBalance);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void A_non_positive_configured_reward_is_rejected_at_construction(int reward)
        {
            var store = new FakeProgressStore { Stored = new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();

            // The ctor guard is `<= 0`, so zero AND every negative value must throw —
            // a refactor to `< 0` (wrongly admitting zero) would fail the [TestCase(0)].
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DailyRewardService(save, new SaveMutationLock(), new FakeClock(RefUtc), ConfigWithReward(reward)));
        }

        // ---- Single-atomic-write: a persist failure rolls BOTH fields back ----

        [Test]
        public void A_failed_persist_rolls_back_both_credits_and_claim_date_and_retries()
        {
            var (store, _, _, daily) = CreateInitialized(
                new SaveModel { Credits = 0, DailyClaim = null }, RefUtc, reward: 5);
            store.FailNextSave = true;

            // SaveService logs LogType.Error on the throwing save; DailyRewardService logs
            // LogType.Warning on its rollback. Both are EXPECTED on this path.
            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Warning, new Regex("DailyRewardService: claim did not complete"));

            DailyRewardResult failed = daily.TryClaimAsync().GetAwaiter().GetResult();

            Assert.IsFalse(failed.Claimed);
            Assert.AreEqual(DailyRewardFailureReason.PersistenceFailed, failed.FailureReason);
            // The result carries the unchanged balance...
            Assert.AreEqual(0, failed.NewBalance);
            // ...and BOTH fields reverted together in the live model — no
            // credits-without-date (or vice versa). The store's last good snapshot
            // (the seed) still shows zero credits and no claim date.
            Assert.AreEqual(0, store.Stored.Credits);
            Assert.IsNull(store.Stored.DailyClaim);

            // The fault is cleared (FailNextSave self-resets): a retry succeeds.
            DailyRewardResult retry = daily.TryClaimAsync().GetAwaiter().GetResult();
            Assert.IsTrue(retry.Claimed);
            Assert.AreEqual(5, retry.NewBalance);
            Assert.AreEqual("2026-06-13", store.Stored.DailyClaim);
        }

        // ---- Before-load guard ----

        [Test]
        public void Claim_before_the_model_is_loaded_throws()
        {
            // A SaveService whose Current is null (never initialized).
            var store = new FakeProgressStore { Stored = null };
            var save = new SaveService(store);
            var daily = new DailyRewardService(save, new SaveMutationLock(), new FakeClock(RefUtc), ConfigWithReward(5));

            Assert.Throws<InvalidOperationException>(() => { var _ = daily.CanClaimToday; });
            // Unwrap the async task synchronously (as the rest of the suite does); the
            // guard throws InvalidOperationException, surfaced here via GetResult().
            Assert.Throws<InvalidOperationException>(
                () => daily.TryClaimAsync().GetAwaiter().GetResult());
        }
    }
}
