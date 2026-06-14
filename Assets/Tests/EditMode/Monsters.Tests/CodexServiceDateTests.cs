using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.Persistence;

namespace Veilwalkers.Monsters.Tests
{
    /// <summary>
    /// The Story 2.5 first-discovered date contract on <see cref="CodexService"/>: the date is
    /// stamped (ISO <c>yyyy-MM-dd</c> from the injected <see cref="FakeClock"/>) ONLY on the
    /// first-discovery create path, is NEVER rewritten by a later flag flip (it is the
    /// <em>first</em>-discovered date), survives a reload, and is reverted with the whole entry
    /// when a persist fault rolls back (no leaked date). Every test exercises the production
    /// write path over a real <see cref="SaveService"/> + the deep-cloning
    /// <see cref="FakeCodexProgressStore"/>; the asserted date string is mutation-testable
    /// (change the stamp format/source/path → a named test goes red), the 2.1–2.4 anti-tautology
    /// bar.
    /// </summary>
    public sealed class CodexServiceDateTests
    {
        private static readonly DateTime Day1 =
            new DateTime(2026, 6, 14, 9, 30, 0, DateTimeKind.Utc); // → "2026-06-14"
        private static readonly DateTime Day2 =
            new DateTime(2026, 7, 1, 0, 5, 0, DateTimeKind.Utc);   // → "2026-07-01"

        private static (FakeCodexProgressStore store, SaveService save, CodexService codex)
            Create(FakeClock clock)
        {
            var store = new FakeCodexProgressStore { Stored = new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            var codex = new CodexService(save, db, clock);
            return (store, save, codex);
        }

        [Test]
        public void First_discovery_stamps_the_clock_date_as_iso_yyyy_MM_dd()
        {
            var (_, save, codex) = Create(new FakeClock(Day1));

            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture).GetAwaiter().GetResult();

            // Read through the defensive view AND the raw model — both must carry the exact date.
            Assert.That(codex.GetEntry("mon01").Discovered, Is.EqualTo("2026-06-14"));
            Assert.That(save.Current.Codex["mon01"].Discovered, Is.EqualTo("2026-06-14"));
        }

        [Test]
        public void Later_flag_flip_does_not_rewrite_the_first_discovered_date()
        {
            // Capture on Day1 stamps the date; advance the clock; Slay on Day2 must NOT change it.
            var clock = new FakeClock(Day1);
            var (_, _, codex) = Create(clock);

            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture).GetAwaiter().GetResult();
            clock.UtcNow = Day2;
            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Slay).GetAwaiter().GetResult();

            // Still the FIRST-discovered date, not the flag-flip day.
            Assert.That(codex.GetEntry("mon01").Discovered, Is.EqualTo("2026-06-14"));
            // And the flag flip really did happen (so we know it took the flip path, not a no-op).
            Assert.That(codex.GetEntry("mon01").Slain, Is.True);
            Assert.That(codex.GetEntry("mon01").Captured, Is.True);
        }

        [Test]
        public void Idempotent_rediscovery_leaves_the_date_unchanged()
        {
            var clock = new FakeClock(Day1);
            var (_, _, codex) = Create(clock);

            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture).GetAwaiter().GetResult();
            clock.UtcNow = Day2;
            // Same flag again → pure no-op (no persist) — certainly no date rewrite.
            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture).GetAwaiter().GetResult();

            Assert.That(codex.GetEntry("mon01").Discovered, Is.EqualTo("2026-06-14"));
        }

        [Test]
        public void Date_survives_a_reload()
        {
            var (store, _, codex) = Create(new FakeClock(Day1));
            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture).GetAwaiter().GetResult();

            // Restart: new SaveService over the same persisted store (LoadAsync deep-copies).
            var reloadedSave = new SaveService(store);
            reloadedSave.InitializeAsync().GetAwaiter().GetResult();
            var reloadedDb = ScriptableObject.CreateInstance<MonsterDatabase>();
            var reloaded = new CodexService(reloadedSave, reloadedDb, new FakeClock(Day2));

            Assert.That(reloaded.GetEntry("mon01").Discovered, Is.EqualTo("2026-06-14"),
                "the persisted first-discovered date must round-trip a reload unchanged");
        }

        [Test]
        public void Persist_failure_on_first_discovery_leaves_no_entry_and_no_date()
        {
            var (store, save, codex) = Create(new FakeClock(Day1));
            store.FailNextSave = true;

            // Both the SaveService fault log and the CodexService rollback log are EXPECTED on
            // this path (the test runner fails on any unexpected error log) — mirrors the
            // existing CodexServiceTests rollback test.
            LogAssert.Expect(LogType.Error, "SaveService: save failed. FakeCodexProgressStore: simulated save failure.");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "CodexService: discovery of 'mon01' rolled back — persist failed\\..*"));

            CodexResult result = codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture)
                .GetAwaiter().GetResult();

            Assert.That(result.Outcome, Is.EqualTo(CodexOutcome.PersistenceFailed));
            // The rollback removed the whole entry — so no leaked date and the slot reads
            // not-discovered (and the view's Discovered is null).
            Assert.That(codex.IsDiscovered("mon01"), Is.False);
            Assert.That(save.Current.Codex.ContainsKey("mon01"), Is.False);
            Assert.That(codex.GetEntry("mon01").Discovered, Is.Null);
        }

        [Test]
        public void Pre_2_5_entry_without_a_date_reads_as_null_not_a_crash()
        {
            // A save created before this field existed: an entry present (discovered) with a null
            // Discovered. The read must surface null (the view renders "—"), never throw.
            var seed = new SaveModel();
            seed.Codex["mon01"] = new CodexEntryData { Captured = true, Discovered = null };
            var store = new FakeCodexProgressStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            var codex = new CodexService(save, db, new FakeClock(Day1));

            Assert.That(codex.IsDiscovered("mon01"), Is.True);
            Assert.That(codex.GetEntry("mon01").Discovered, Is.Null);
        }
    }
}
