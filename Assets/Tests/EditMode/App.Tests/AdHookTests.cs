using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Persistence;

namespace Veilwalkers.App.Tests
{
    /// <summary>
    /// <see cref="AdHook"/> ad-reward seam (Story 1.9, AR-17): grants exactly one
    /// <see cref="ChargeType.StabilityBoost"/> charge per call, never Credits/XP
    /// (the load-bearing AC-AD-4 spy), capped per UTC day via <c>AdReward</c>, with the
    /// cap re-opening on a day rollover and the charge-first failure ordering not
    /// consuming the cap. Wires a real <see cref="ProgressionService"/> +
    /// <see cref="CreditService"/> + <see cref="SaveService"/> over an in-memory store
    /// sharing ONE <see cref="SaveMutationLock"/>, exactly like Bootstrap.
    /// </summary>
    public sealed class AdHookTests
    {
        private static readonly DateTime RefUtc =
            new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// In-memory store cloning the fields these tests assert on — including the
        /// <c>AdReward</c> counter (deep-copied), charge counts, Credits, and Xp — with a
        /// one-shot save-failure hook for the charge-fail test.
        /// </summary>
        private sealed class InMemoryStore : IProgressStore
        {
            public SaveModel Stored;
            public bool FailNextSave;

            /// <summary>1-based save number to fail (0 = none). Lets a test fail the
            /// COUNTER persist (the 2nd save) while the charge persist (1st) succeeds.</summary>
            public int FailOnSaveNumber;

            /// <summary>
            /// When true, fail every EVEN-numbered save (2nd, 4th, …). Within one
            /// <c>GrantRewardAsync</c> the charge is save #1 (odd, succeeds) and the counter
            /// is save #2 (even, fails) — so this models a disk on which the COUNTER persist
            /// always fails while the charge persist always lands, repeatedly.
            /// </summary>
            public bool FailNextCounterSave;

            private int _saveCalls;

            public int SaveCalls => _saveCalls;

            public bool Exists() => Stored != null;

            public Task<SaveModel> LoadAsync()
            {
                if (Stored == null)
                {
                    throw new FileNotFoundException("InMemoryStore: no save exists.");
                }

                return Task.FromResult(Clone(Stored));
            }

            public Task SaveAsync(SaveModel model)
            {
                _saveCalls++;
                bool failEvenCounter = FailNextCounterSave && (_saveCalls % 2 == 0);
                if (FailNextSave || _saveCalls == FailOnSaveNumber || failEvenCounter)
                {
                    FailNextSave = false;
                    throw new IOException("InMemoryStore: simulated save failure.");
                }

                Stored = Clone(model);
                return Task.CompletedTask;
            }

            public Task DeleteAsync()
            {
                Stored = null;
                return Task.CompletedTask;
            }

            private static SaveModel Clone(SaveModel model) => new SaveModel
            {
                SchemaVersion = model.SchemaVersion,
                Credits = model.Credits,
                StartingCreditsGranted = model.StartingCreditsGranted,
                Xp = model.Xp,
                Level = model.Level,
                StrongCaptureCharges = model.StrongCaptureCharges,
                StabilityBoostCharges = model.StabilityBoostCharges,
                NightveilFilterCharges = model.NightveilFilterCharges,
                DailyClaim = model.DailyClaim,
                FirstZeroCreditDay = model.FirstZeroCreditDay,
                AdReward = new AdRewardData
                {
                    GrantsToday = model.AdReward?.GrantsToday ?? 0,
                    DayBucket = model.AdReward?.DayBucket ?? 0,
                },
            };
        }

        private sealed class Fixture
        {
            public InMemoryStore Store;
            public SaveService Save;
            public CreditService Credit;
            public ProgressionService Progression;
            public FakeClock Clock;
            public AdHook Hook;
        }

        private static Fixture CreateInitialized(SaveModel seed, DateTime utcNow, int adDailyCap = 3)
        {
            var store = new InMemoryStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var mutationLock = new SaveMutationLock();
            var credit = new CreditService(save, mutationLock);
            // Provisional rules: thresholds high enough that no AddCharge here triggers a
            // level-up grant — the ad charge is the only charge mutation under test.
            var rules = new ProgressionRules(new[] { 1000, 2000, 3000 }, 1, 1, 1);
            var progression = new ProgressionService(save, rules, mutationLock);
            var clock = new FakeClock(utcNow);
            var hook = new AdHook(progression, save, mutationLock, clock, adDailyCap);
            return new Fixture
            {
                Store = store, Save = save, Credit = credit,
                Progression = progression, Clock = clock, Hook = hook,
            };
        }

        // ---- AC-AD-1 / AC-AD-4 (load-bearing): one StabilityBoost charge, never Credits/XP ----

        [Test]
        public void GrantReward_grants_exactly_one_StabilityBoost_charge()
        {
            var f = CreateInitialized(new SaveModel(), RefUtc);

            AdRewardResult result = f.Hook.GrantRewardAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result.Granted);
            Assert.AreEqual(1, f.Save.Current.StabilityBoostCharges, "Exactly one StabilityBoost charge.");
            Assert.AreEqual(0, f.Save.Current.StrongCaptureCharges, "NOT StrongCapture (AR-17 forbids it).");
            Assert.AreEqual(0, f.Save.Current.NightveilFilterCharges);
            Assert.AreEqual(ChargeType.StabilityBoost, AdHook.RewardCharge, "The seam pins StabilityBoost.");
        }

        [Test]
        public void GrantReward_never_touches_Credits_or_Xp()
        {
            // The load-bearing AC-AD-4 spy: the ONLY economy mutation is the charge.
            var seed = new SaveModel { Credits = 42, Xp = 7 };
            var f = CreateInitialized(seed, RefUtc);

            f.Hook.GrantRewardAsync().GetAwaiter().GetResult();

            Assert.AreEqual(42, f.Credit.Balance, "Credits never change on an ad reward.");
            Assert.AreEqual(42, f.Store.Stored.Credits, "Persisted Credits unchanged.");
            Assert.AreEqual(7, f.Save.Current.Xp, "XP never changes on an ad reward.");
            Assert.AreEqual(7, f.Store.Stored.Xp, "Persisted XP unchanged.");
        }

        // ---- AC-AD-2: daily cap, rollover, day-bucket boundary ----

        [Test]
        public void Grants_succeed_up_to_the_cap_then_are_refused()
        {
            var f = CreateInitialized(new SaveModel(), RefUtc, adDailyCap: 3);

            for (int i = 1; i <= 3; i++)
            {
                AdRewardResult ok = f.Hook.GrantRewardAsync().GetAwaiter().GetResult();
                Assert.IsTrue(ok.Granted, $"Grant {i} within cap.");
                Assert.AreEqual(i, ok.GrantsToday);
            }

            AdRewardResult capped = f.Hook.GrantRewardAsync().GetAwaiter().GetResult();
            Assert.IsFalse(capped.Granted);
            Assert.AreEqual(AdRewardFailureReason.CapReached, capped.FailureReason);
            Assert.AreEqual(3, capped.GrantsToday, "Cap counter not exceeded.");
            Assert.AreEqual(3, f.Save.Current.StabilityBoostCharges, "No 4th charge granted.");
        }

        [Test]
        public void Cap_reopens_after_the_utc_day_rolls_over()
        {
            var f = CreateInitialized(new SaveModel(), RefUtc, adDailyCap: 2);
            f.Hook.GrantRewardAsync().GetAwaiter().GetResult();
            f.Hook.GrantRewardAsync().GetAwaiter().GetResult();
            Assert.IsFalse(f.Hook.GrantRewardAsync().GetAwaiter().GetResult().Granted, "Capped on day 1.");

            f.Clock.Advance(TimeSpan.FromDays(1));

            AdRewardResult nextDay = f.Hook.GrantRewardAsync().GetAwaiter().GetResult();
            Assert.IsTrue(nextDay.Granted, "Cap re-opens on the new UTC day.");
            Assert.AreEqual(1, nextDay.GrantsToday, "Counter reset for the new day.");
        }

        [Test]
        public void Same_utc_day_different_time_shares_one_bucket_and_one_cap()
        {
            var f = CreateInitialized(new SaveModel(), new DateTime(2026, 6, 13, 0, 1, 0, DateTimeKind.Utc), adDailyCap: 1);
            Assert.IsTrue(f.Hook.GrantRewardAsync().GetAwaiter().GetResult().Granted, "First grant at 00:01 UTC.");

            // Same UTC day, 23:59 — same bucket, so the cap is still consumed.
            f.Clock.UtcNow = new DateTime(2026, 6, 13, 23, 59, 0, DateTimeKind.Utc);
            AdRewardResult later = f.Hook.GrantRewardAsync().GetAwaiter().GetResult();
            Assert.IsFalse(later.Granted, "Still capped later the same UTC day.");
            Assert.AreEqual(AdRewardFailureReason.CapReached, later.FailureReason);
        }

        // ---- AC-AD-3: charge-fail does not consume the cap ----

        [Test]
        public void A_failed_charge_does_not_consume_the_cap_and_retries()
        {
            var f = CreateInitialized(new SaveModel(), RefUtc, adDailyCap: 3);
            f.Store.FailNextSave = true; // the charge's persist throws

            // The forced persist fault logs three EXPECTED messages on this path: the
            // SaveService error, ProgressionService's charge-rollback error, and AdHook's
            // own warning. All three must be declared or the UTF run fails on them.
            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Error, new Regex(@"ProgressionService: charge grant \(StabilityBoost\) rolled back"));
            LogAssert.Expect(LogType.Warning, new Regex("AdHook: ad-reward charge did not persist"));

            AdRewardResult failed = f.Hook.GrantRewardAsync().GetAwaiter().GetResult();

            Assert.IsFalse(failed.Granted);
            Assert.AreEqual(AdRewardFailureReason.ChargeGrantFailed, failed.FailureReason);
            Assert.AreEqual(0, failed.GrantsToday, "Cap NOT consumed for a charge that didn't land.");
            Assert.AreEqual(0, f.Save.Current.StabilityBoostCharges, "No charge granted.");

            // Fault cleared → a retry succeeds and consumes exactly one.
            AdRewardResult retry = f.Hook.GrantRewardAsync().GetAwaiter().GetResult();
            Assert.IsTrue(retry.Granted);
            Assert.AreEqual(1, retry.GrantsToday);
            Assert.AreEqual(1, f.Save.Current.StabilityBoostCharges);
        }

        [Test]
        public void A_failed_counter_persist_keeps_the_cap_consumed_in_memory()
        {
            // The charge persist (save #1, inside AddChargeAsync) succeeds; the COUNTER
            // persist (save #2, Phase 3) fails. The charge stands and the in-memory cap is
            // consumed, so the result is still Granted and the next call sees the cap.
            var f = CreateInitialized(new SaveModel(), RefUtc, adDailyCap: 1);
            f.Store.FailOnSaveNumber = 2;

            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Warning, new Regex("AdHook: cap counter did not persist"));

            AdRewardResult r = f.Hook.GrantRewardAsync().GetAwaiter().GetResult();

            Assert.IsTrue(r.Granted, "Charge landed → Granted is true even though the counter persist failed.");
            Assert.AreEqual(1, r.GrantsToday, "In-memory cap consumed.");
            Assert.AreEqual(1, f.Save.Current.StabilityBoostCharges, "The charge stands.");

            // The cap holds for the session despite the failed on-disk write.
            AdRewardResult capped = f.Hook.GrantRewardAsync().GetAwaiter().GetResult();
            Assert.IsFalse(capped.Granted);
            Assert.AreEqual(AdRewardFailureReason.CapReached, capped.FailureReason);
        }

        [Test]
        public void Repeated_counter_persist_failures_stay_bounded_by_the_cap()
        {
            // The unbounded-bypass guard. The charge persists (save #1) but the counter
            // persist (Phase 3) fails on EVERY granting call. Because the in-memory counter
            // is NOT rolled back, the in-process cap holds: only AdDailyCap grants ever
            // succeed, no matter how many times a retry loop calls in. (Pre-fix, the rolled-
            // back counter let every call re-grant → unlimited charges.)
            var f = CreateInitialized(new SaveModel(), RefUtc, adDailyCap: 2);

            f.Store.FailNextCounterSave = true; // every counter persist (even save) fails
            LogAssert.ignoreFailingMessages = true; // a storm of expected Error/Warn on the failing saves
            int granted = 0;
            for (int i = 0; i < 10; i++)
            {
                if (f.Hook.GrantRewardAsync().GetAwaiter().GetResult().Granted)
                {
                    granted++;
                }
            }

            LogAssert.ignoreFailingMessages = false;

            // Only the cap's worth of charges were minted, despite 10 attempts on a disk that
            // never lets the counter persist — the in-memory cap bounded it.
            Assert.AreEqual(2, granted, "Bounded to AdDailyCap, never unbounded.");
            Assert.AreEqual(2, f.Save.Current.AdReward.GrantsToday, "In-memory cap fully consumed.");
        }

        [Test]
        public void Phase3_re_reads_the_clock_so_a_midnight_crossing_credits_the_new_day()
        {
            // An auto-advancing clock that ticks forward one full day on its SECOND read —
            // i.e. Phase 1 sees day 1, then the "charge I/O" crosses midnight, so Phase 3's
            // fresh read sees day 2. Because Phase 3 re-reads (not reusing the entry bucket),
            // the grant is credited to day 2's bucket — the cap-reset invariant across a
            // boundary that a stale read would have mis-credited to day 1.
            var store = new InMemoryStore { Stored = new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var mutationLock = new SaveMutationLock();
            var rules = new ProgressionRules(new[] { 1000, 2000, 3000 }, 1, 1, 1);
            var progression = new ProgressionService(save, rules, mutationLock);
            var clock = new AdvanceOnReadClock(
                new DateTime(2026, 6, 13, 23, 59, 0, DateTimeKind.Utc),
                advanceAfterReadNumber: 1, advanceBy: TimeSpan.FromDays(1));
            var hook = new AdHook(progression, save, mutationLock, clock, adDailyCap: 5);
            long day2 = new DateTime(2026, 6, 14, 23, 59, 0, DateTimeKind.Utc).Date.Ticks / TimeSpan.TicksPerDay;

            AdRewardResult r = hook.GrantRewardAsync().GetAwaiter().GetResult();

            Assert.IsTrue(r.Granted);
            Assert.AreEqual(day2, save.Current.AdReward.DayBucket, "Grant credited to the NEW day (Phase 3 re-read the clock).");
            Assert.AreEqual(1, save.Current.AdReward.GrantsToday);
        }

        /// <summary>A clock that jumps forward once, after a chosen read, to model a UTC
        /// day boundary crossing between AdHook's Phase 1 and Phase 3.</summary>
        private sealed class AdvanceOnReadClock : IClock
        {
            private readonly int _advanceAfter;
            private readonly TimeSpan _advanceBy;
            private DateTime _now;
            private int _reads;

            public AdvanceOnReadClock(DateTime start, int advanceAfterReadNumber, TimeSpan advanceBy)
            {
                _now = start;
                _advanceAfter = advanceAfterReadNumber;
                _advanceBy = advanceBy;
            }

            public DateTime UtcNow
            {
                get
                {
                    _reads++;
                    DateTime value = _now;
                    if (_reads == _advanceAfter)
                    {
                        _now += _advanceBy;
                    }

                    return value;
                }
            }
        }

        // ---- Guards ----

        [Test]
        public void A_non_positive_cap_is_rejected_at_construction()
        {
            var store = new InMemoryStore { Stored = new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var mutationLock = new SaveMutationLock();
            var rules = new ProgressionRules(new[] { 1000 }, 1, 1, 1);
            var progression = new ProgressionService(save, rules, mutationLock);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AdHook(progression, save, mutationLock, new FakeClock(RefUtc), 0));
        }

        [Test]
        public void Grant_before_the_model_is_loaded_throws()
        {
            var store = new InMemoryStore { Stored = null };
            var save = new SaveService(store);
            var mutationLock = new SaveMutationLock();
            var rules = new ProgressionRules(new[] { 1000 }, 1, 1, 1);
            var progression = new ProgressionService(save, rules, mutationLock);
            var hook = new AdHook(progression, save, mutationLock, new FakeClock(RefUtc), 3);

            Assert.Throws<InvalidOperationException>(
                () => hook.GrantRewardAsync().GetAwaiter().GetResult());
        }
    }
}
