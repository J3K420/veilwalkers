using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Veilwalkers.Core;

namespace Veilwalkers.App.Tests
{
    /// <summary>
    /// <see cref="LocalTelemetrySink"/> (Story 1.9, AR-15): <c>Count</c> increments the
    /// per-event counter and write-through persists via <see cref="ITelemetryStore"/>,
    /// stamping the current UTC day-bucket; it never touches the save (it has no
    /// SaveService dependency); and the <see cref="ITelemetrySink"/> seam is swappable.
    /// </summary>
    public sealed class LocalTelemetrySinkTests
    {
        private static readonly DateTime RefUtc =
            new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>An in-memory <see cref="ITelemetryStore"/> that keeps the last saved data.</summary>
        private sealed class FakeTelemetryStore : ITelemetryStore
        {
            public TelemetryData Saved = new TelemetryData();
            public int SaveCalls;

            public bool Exists() => true;
            public Task<TelemetryData> LoadAsync() => Task.FromResult(Saved);

            public Task SaveAsync(TelemetryData data)
            {
                SaveCalls++;
                Saved = data;
                return Task.CompletedTask;
            }

            public Task DeleteAsync()
            {
                Saved = new TelemetryData();
                return Task.CompletedTask;
            }
        }

        [Test]
        public void Count_increments_the_counter_and_writes_through()
        {
            var store = new FakeTelemetryStore();
            var sink = new LocalTelemetrySink(store, new FakeClock(RefUtc));

            sink.Count("loop_completed");
            sink.Count("loop_completed", 2);

            Assert.AreEqual(3, store.Saved.Counters["loop_completed"], "Cumulative count.");
            Assert.AreEqual(2, store.SaveCalls, "Write-through: one persist per Count.");
        }

        [Test]
        public void Count_stamps_recent_events_with_the_current_day_bucket()
        {
            var store = new FakeTelemetryStore();
            var sink = new LocalTelemetrySink(store, new FakeClock(RefUtc));
            long expectedBucket = RefUtc.Date.Ticks / TimeSpan.TicksPerDay;

            sink.Count("first_zero_credit");

            Assert.AreEqual(1, store.Saved.RecentEvents.Count);
            TelemetryEvent e = store.Saved.RecentEvents[0];
            Assert.AreEqual("first_zero_credit", e.Evt);
            Assert.AreEqual(1, e.Amount);
            Assert.AreEqual(expectedBucket, e.DayBucket);
        }

        [Test]
        public void Count_with_an_empty_key_is_ignored_without_throwing()
        {
            var store = new FakeTelemetryStore();
            var sink = new LocalTelemetrySink(store, new FakeClock(RefUtc));

            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("empty event key"));

            Assert.DoesNotThrow(() => sink.Count(""));
            Assert.AreEqual(0, store.SaveCalls, "No persist for an ignored empty key.");
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        public void Count_ignores_non_positive_amounts_without_corrupting_the_counter(long amount)
        {
            var store = new FakeTelemetryStore();
            var sink = new LocalTelemetrySink(store, new FakeClock(RefUtc));
            sink.Count("evt", 5); // establish a baseline of 5
            int savesAfterBaseline = store.SaveCalls;

            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("amount must be positive"));

            sink.Count("evt", amount); // a non-positive amount must NOT change the counter

            Assert.AreEqual(5, store.Saved.Counters["evt"], "Counter unchanged by a non-positive amount.");
            Assert.AreEqual(savesAfterBaseline, store.SaveCalls, "No persist for an ignored amount.");
        }

        [Test]
        public void The_sink_seam_is_swappable_a_second_impl_satisfies_the_same_call_shape()
        {
            // A trivial alternative ITelemetrySink (the Phase-2 Firebase sink stand-in):
            // the SAME Count(evt, amount) call site works without modification — the
            // compile-level proof that the seam is swappable (AC-TEL-1).
            var recorded = new List<string>();
            ITelemetrySink alt = new RecordingSink(recorded);

            alt.Count("x");
            alt.Count("y", 5);

            Assert.AreEqual(new[] { "x:1", "y:5" }, recorded.ToArray());
        }

        private sealed class RecordingSink : ITelemetrySink
        {
            private readonly List<string> _log;
            public RecordingSink(List<string> log) => _log = log;
            public void Count(string evt, long amount = 1) => _log.Add($"{evt}:{amount}");
        }
    }
}
