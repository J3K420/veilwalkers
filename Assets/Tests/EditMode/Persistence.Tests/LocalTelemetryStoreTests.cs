using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Veilwalkers.Core;

namespace Veilwalkers.Persistence.Tests
{
    /// <summary>
    /// <see cref="LocalTelemetryStore"/> (Story 1.9, AR-15): a SEPARATE plaintext
    /// <c>telemetry.json</c> that round-trips, is NOT encrypted (testers can read it),
    /// and is a CAPPED RING (trimmed by hard count and by retention age on save).
    /// Mirrors the save store's atomic write; uses a real temp directory.
    /// </summary>
    public sealed class LocalTelemetryStoreTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; set; }
            public FixedClock(DateTime utc) => UtcNow = utc;
        }

        private static readonly DateTime RefUtc =
            new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);

        private string _dir;

        [SetUp]
        public void SetUp() => _dir = TestSaveFiles.CreateTempDir();

        [TearDown]
        public void TearDown() => TestSaveFiles.DeleteTempDir(_dir);

        private static long BucketOf(DateTime utc) => utc.Date.Ticks / TimeSpan.TicksPerDay;

        [Test]
        public void Round_trips_counters_and_recent_events()
        {
            var store = new LocalTelemetryStore(_dir, new FixedClock(RefUtc), retentionDays: 30);
            var data = new TelemetryData();
            data.Counters["loop_completed"] = 4;
            data.RecentEvents.Add(new TelemetryEvent { Evt = "first_zero", Amount = 1, DayBucket = BucketOf(RefUtc) });

            store.SaveAsync(data).GetAwaiter().GetResult();
            TelemetryData loaded = store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(4, loaded.Counters["loop_completed"]);
            Assert.AreEqual(1, loaded.RecentEvents.Count);
            Assert.AreEqual("first_zero", loaded.RecentEvents[0].Evt);
        }

        [Test]
        public void Writes_a_file_named_telemetry_json()
        {
            var store = new LocalTelemetryStore(_dir, new FixedClock(RefUtc), retentionDays: 30);
            store.SaveAsync(new TelemetryData()).GetAwaiter().GetResult();

            Assert.IsTrue(File.Exists(Path.Combine(_dir, "telemetry.json")), "telemetry.json exists.");
            Assert.IsTrue(store.Exists());
        }

        [Test]
        public void The_file_is_plaintext_json_not_encrypted()
        {
            var store = new LocalTelemetryStore(_dir, new FixedClock(RefUtc), retentionDays: 30);
            var data = new TelemetryData();
            data.Counters["sentinel_key"] = 9;

            store.SaveAsync(data).GetAwaiter().GetResult();

            // Read the raw bytes: a plaintext JSON file contains the literal key text and
            // parses as UTF-8 JSON. An AES blob would not contain the key in cleartext.
            byte[] bytes = File.ReadAllBytes(Path.Combine(_dir, "telemetry.json"));
            string text = Encoding.UTF8.GetString(bytes);
            StringAssert.Contains("sentinel_key", text, "Key is readable in plaintext.");
            StringAssert.StartsWith("{", text.TrimStart(), "Looks like JSON, not ciphertext.");
        }

        [Test]
        public void Missing_file_loads_empty_not_throws()
        {
            var store = new LocalTelemetryStore(_dir, new FixedClock(RefUtc), retentionDays: 30);

            Assert.IsFalse(store.Exists());
            TelemetryData loaded = store.LoadAsync().GetAwaiter().GetResult();
            Assert.IsNotNull(loaded);
            Assert.AreEqual(0, loaded.Counters.Count);
            Assert.AreEqual(0, loaded.RecentEvents.Count);
        }

        [Test]
        public void Capped_ring_drops_oldest_events_beyond_the_hard_max()
        {
            var store = new LocalTelemetryStore(_dir, new FixedClock(RefUtc), retentionDays: 0); // age-trim off
            var data = new TelemetryData();
            int over = LocalTelemetryStore.MaxRetainedEvents + 50;
            for (int i = 0; i < over; i++)
            {
                data.RecentEvents.Add(new TelemetryEvent { Evt = $"e{i}", Amount = 1, DayBucket = BucketOf(RefUtc) });
            }

            store.SaveAsync(data).GetAwaiter().GetResult();
            TelemetryData loaded = store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(LocalTelemetryStore.MaxRetainedEvents, loaded.RecentEvents.Count, "Trimmed to the hard cap.");
            // Oldest dropped, newest kept: the last appended event survives.
            Assert.AreEqual($"e{over - 1}", loaded.RecentEvents[loaded.RecentEvents.Count - 1].Evt);
            Assert.AreEqual("e50", loaded.RecentEvents[0].Evt, "First 50 (oldest) were dropped.");
        }

        [Test]
        public void Capped_ring_hard_trim_drops_the_genuinely_oldest_even_when_out_of_order()
        {
            // A hand-edited (plaintext) file could carry events out of chronological order.
            // The hard-cap trim must drop the genuinely-OLDEST by day-bucket, not just the
            // front of the list, so it sorts first.
            var store = new LocalTelemetryStore(_dir, new FixedClock(RefUtc), retentionDays: 0);
            long today = BucketOf(RefUtc);
            var data = new TelemetryData();
            // Fill past the hard cap with NEWEST entries first (reverse chronological) so a
            // naive front-trim would wrongly drop the newest.
            int total = LocalTelemetryStore.MaxRetainedEvents + 3;
            for (int i = 0; i < total; i++)
            {
                // i=0 is the newest (today), increasing i is older.
                data.RecentEvents.Add(new TelemetryEvent { Evt = $"e{i}", Amount = 1, DayBucket = today - i });
            }

            store.SaveAsync(data).GetAwaiter().GetResult();
            TelemetryData loaded = store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(LocalTelemetryStore.MaxRetainedEvents, loaded.RecentEvents.Count);
            // The 3 genuinely-oldest (largest day offset: e[total-1], e[total-2], e[total-3])
            // were dropped; the newest (smallest offset, e0 = today) survives.
            long newest = today;
            long oldestKept = today - (LocalTelemetryStore.MaxRetainedEvents - 1);
            Assert.IsTrue(loaded.RecentEvents.Exists(e => e.DayBucket == newest), "Newest kept.");
            Assert.IsFalse(loaded.RecentEvents.Exists(e => e.DayBucket < oldestKept), "Oldest dropped.");
        }

        [Test]
        public void Capped_ring_drops_events_older_than_the_retention_age()
        {
            var store = new LocalTelemetryStore(_dir, new FixedClock(RefUtc), retentionDays: 7);
            long today = BucketOf(RefUtc);
            var data = new TelemetryData();
            data.RecentEvents.Add(new TelemetryEvent { Evt = "old", Amount = 1, DayBucket = today - 10 }); // older than 7d
            data.RecentEvents.Add(new TelemetryEvent { Evt = "recent", Amount = 1, DayBucket = today - 2 }); // within 7d

            store.SaveAsync(data).GetAwaiter().GetResult();
            TelemetryData loaded = store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, loaded.RecentEvents.Count, "The 10-day-old entry was age-trimmed.");
            Assert.AreEqual("recent", loaded.RecentEvents[0].Evt);
        }

        [Test]
        public void Delete_removes_the_file()
        {
            var store = new LocalTelemetryStore(_dir, new FixedClock(RefUtc), retentionDays: 30);
            store.SaveAsync(new TelemetryData()).GetAwaiter().GetResult();
            Assert.IsTrue(store.Exists());

            store.DeleteAsync().GetAwaiter().GetResult();
            Assert.IsFalse(store.Exists());
        }
    }
}
