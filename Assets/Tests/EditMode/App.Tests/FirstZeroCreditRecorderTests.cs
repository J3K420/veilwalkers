using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Persistence;

namespace Veilwalkers.App.Tests
{
    /// <summary>
    /// <see cref="FirstZeroCreditRecorder"/> (Story 1.9, AR-15): the write-once
    /// <c>firstZeroCreditDay</c> scalar — set ONCE to the current UTC day-bucket, never
    /// overwritten by a later zero-credit moment, persisted, and rolled back on a persist
    /// failure so it retries.
    /// </summary>
    public sealed class FirstZeroCreditRecorderTests
    {
        private static readonly DateTime Day1 =
            new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);

        private sealed class InMemoryStore : IProgressStore
        {
            public SaveModel Stored;
            public bool FailNextSave;

            public bool Exists() => Stored != null;

            public Task<SaveModel> LoadAsync() => Stored == null
                ? throw new FileNotFoundException("InMemoryStore: no save exists.")
                : Task.FromResult(Clone(Stored));

            public Task SaveAsync(SaveModel model)
            {
                if (FailNextSave)
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
                FirstZeroCreditDay = model.FirstZeroCreditDay,
            };
        }

        private static (InMemoryStore store, SaveService save, FakeClock clock, FirstZeroCreditRecorder recorder)
            Create(SaveModel seed, DateTime utcNow)
        {
            var store = new InMemoryStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var clock = new FakeClock(utcNow);
            var recorder = new FirstZeroCreditRecorder(save, new SaveMutationLock(), clock);
            return (store, save, clock, recorder);
        }

        private static long BucketOf(DateTime utc) => utc.Date.Ticks / TimeSpan.TicksPerDay;

        [Test]
        public void First_call_records_todays_bucket_and_persists()
        {
            var (store, save, _, recorder) = Create(new SaveModel(), Day1);
            Assert.AreEqual(FirstZeroCreditRecorder.Unset, save.Current.FirstZeroCreditDay, "Starts unset.");

            bool wrote = recorder.MarkFirstZeroCreditDayAsync().GetAwaiter().GetResult();

            Assert.IsTrue(wrote);
            Assert.AreEqual(BucketOf(Day1), save.Current.FirstZeroCreditDay);
            Assert.AreEqual(BucketOf(Day1), store.Stored.FirstZeroCreditDay, "Persisted.");
        }

        [Test]
        public void A_second_call_even_a_later_day_does_not_overwrite()
        {
            var (_, save, clock, recorder) = Create(new SaveModel(), Day1);
            recorder.MarkFirstZeroCreditDayAsync().GetAwaiter().GetResult();
            long firstValue = save.Current.FirstZeroCreditDay;

            // A later zero-credit moment, days later — must NOT overwrite.
            clock.Advance(TimeSpan.FromDays(5));
            bool wroteAgain = recorder.MarkFirstZeroCreditDayAsync().GetAwaiter().GetResult();

            Assert.IsFalse(wroteAgain, "Write-once: a second call is a no-op.");
            Assert.AreEqual(firstValue, save.Current.FirstZeroCreditDay, "Original value preserved.");
            Assert.AreEqual(BucketOf(Day1), save.Current.FirstZeroCreditDay);
        }

        [Test]
        public void A_failed_persist_leaves_it_unset_and_retryable()
        {
            var (store, save, _, recorder) = Create(new SaveModel(), Day1);
            store.FailNextSave = true;

            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("SaveService: save failed"));
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("FirstZeroCreditRecorder: write did not persist"));

            bool wrote = recorder.MarkFirstZeroCreditDayAsync().GetAwaiter().GetResult();

            Assert.IsFalse(wrote, "Persist failed → no write reported.");
            Assert.AreEqual(FirstZeroCreditRecorder.Unset, save.Current.FirstZeroCreditDay, "Rolled back to unset.");

            // Retry (fault cleared) succeeds.
            bool retry = recorder.MarkFirstZeroCreditDayAsync().GetAwaiter().GetResult();
            Assert.IsTrue(retry);
            Assert.AreEqual(BucketOf(Day1), save.Current.FirstZeroCreditDay);
        }

        [Test]
        public void Record_before_the_model_is_loaded_throws()
        {
            var store = new InMemoryStore { Stored = null };
            var save = new SaveService(store);
            var recorder = new FirstZeroCreditRecorder(save, new SaveMutationLock(), new FakeClock(Day1));

            Assert.Throws<InvalidOperationException>(
                () => recorder.MarkFirstZeroCreditDayAsync().GetAwaiter().GetResult());
        }
    }
}
