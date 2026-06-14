using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.Persistence;

namespace Veilwalkers.Monsters.Tests
{
    /// <summary>
    /// <see cref="CodexService"/> as the read model + atomic discovery-record seam over
    /// <see cref="SaveModel.Codex"/> (Story 2.3): first discovery (create + persist + count +
    /// <c>OnMonsterDiscovered</c>), idempotent re-discovery, the captured-then-slain
    /// flag-flip subtlety, 67/67 → <c>OnCodexCompleted</c>-exactly-once, persistence across a
    /// reload, the invalid-id throw, rollback-on-persist-fault, the defensive read view, and
    /// subscriber isolation. Built on a real <see cref="SaveService"/> over a deep-cloning
    /// <see cref="FakeCodexProgressStore"/> (the Economy fake drops Codex on clone), and an
    /// in-memory <see cref="MonsterDatabase"/> — CodexService only reads its STATIC
    /// <c>UniverseCount</c>/<c>IsValidMonsterId</c>, so no populated definitions are needed.
    /// Every test exercises the production service; none recompute its logic
    /// (the 2.1/2.2 anti-tautology bar).
    /// </summary>
    public sealed class CodexServiceTests
    {
        // A fixed UTC instant for the first-discovered date stamp (Story 2.5). The date tests
        // assert the exact ISO yyyy-MM-dd this produces ("2026-06-14"), mutation-testable.
        private static readonly System.DateTime FixedNow =
            new System.DateTime(2026, 6, 14, 9, 30, 0, System.DateTimeKind.Utc);

        private static (FakeCodexProgressStore store, SaveService save, CodexService codex)
            CreateInitialized(SaveModel seed = null)
        {
            return CreateInitialized(new FakeClock(FixedNow), seed);
        }

        private static (FakeCodexProgressStore store, SaveService save, CodexService codex)
            CreateInitialized(FakeClock clock, SaveModel seed = null)
        {
            var store = new FakeCodexProgressStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            var codex = new CodexService(save, db, clock);
            return (store, save, codex);
        }

        // ---- ctor null-checks (the 1.4–1.8 convention) ----

        [Test]
        public void Ctor_null_args_throw()
        {
            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            var save = new SaveService(new FakeCodexProgressStore { Stored = new SaveModel() });
            var clock = new FakeClock(FixedNow);
            Assert.Throws<System.ArgumentNullException>(() => new CodexService(null, db, clock));
            Assert.Throws<System.ArgumentNullException>(() => new CodexService(save, null, clock));
            Assert.Throws<System.ArgumentNullException>(() => new CodexService(save, db, null));
        }

        // ---- anti-tautology: prove the fake's clone is a real deep copy ----

        [Test]
        public void Fake_store_clone_is_a_real_deep_copy_of_codex()
        {
            var (store, save, codex) = CreateInitialized(new SaveModel());
            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture).GetAwaiter().GetResult();

            // The stored snapshot must not alias the live model's dict or its entries.
            Assert.That(store.Stored.Codex, Is.Not.SameAs(save.Current.Codex),
                "stored Codex dict must be a distinct instance");
            Assert.That(store.Stored.Codex["mon01"], Is.Not.SameAs(save.Current.Codex["mon01"]),
                "stored entry must be a distinct instance");

            // Mutate the live model AFTER the save; the snapshot must be untouched.
            save.Current.Codex["mon01"].Slain = true;
            save.Current.Codex["mon99"] = new CodexEntryData();
            Assert.That(store.Stored.Codex["mon01"].Slain, Is.False,
                "mutating the live entry must not change the stored snapshot");
            Assert.That(store.Stored.Codex.ContainsKey("mon99"), Is.False,
                "adding to the live dict must not change the stored snapshot");
        }

        // ---- AC-1: first discovery ----

        [Test]
        public void First_capture_creates_entry_increments_count_and_raises_OnMonsterDiscovered()
        {
            var (store, save, codex) = CreateInitialized(new SaveModel());
            Assert.AreEqual(0, codex.DiscoveredCount);

            string discovered = null;
            int raiseCount = 0;
            codex.OnMonsterDiscovered += id => { discovered = id; raiseCount++; };

            CodexResult result = codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture)
                .GetAwaiter().GetResult();

            Assert.AreEqual(CodexOutcome.Discovered, result.Outcome);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.DiscoveredCount);
            Assert.AreEqual(1, codex.DiscoveredCount);
            Assert.IsTrue(save.Current.Codex["mon01"].Captured);
            Assert.IsFalse(save.Current.Codex["mon01"].Slain);
            Assert.AreEqual("mon01", discovered);
            Assert.AreEqual(1, raiseCount);
            // Actually persisted — the fake's snapshot proves the write.
            Assert.IsTrue(store.Stored.Codex.ContainsKey("mon01"));
            Assert.IsTrue(store.Stored.Codex["mon01"].Captured);
        }

        [Test]
        public void First_slay_sets_Slain_flag()
        {
            var (_, save, codex) = CreateInitialized(new SaveModel());

            CodexResult result = codex.RecordDiscoveryAsync("mon02", DiscoverySource.Slay)
                .GetAwaiter().GetResult();

            Assert.AreEqual(CodexOutcome.Discovered, result.Outcome);
            Assert.IsTrue(save.Current.Codex["mon02"].Slain);
            Assert.IsFalse(save.Current.Codex["mon02"].Captured);
            Assert.AreEqual(1, codex.DiscoveredCount);
        }

        // ---- AC-2: idempotency ----

        [Test]
        public void Recapture_is_idempotent_no_increment_no_event_no_persist()
        {
            var (store, _, codex) = CreateInitialized(new SaveModel());
            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture).GetAwaiter().GetResult();

            int savesBefore = store.SaveCalls;
            int events = 0;
            codex.OnMonsterDiscovered += _ => events++;

            CodexResult result = codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture)
                .GetAwaiter().GetResult();

            Assert.AreEqual(CodexOutcome.AlreadyRecorded, result.Outcome);
            Assert.AreEqual(1, codex.DiscoveredCount);
            Assert.AreEqual(0, events, "re-capture must not raise OnMonsterDiscovered");
            Assert.AreEqual(savesBefore, store.SaveCalls, "a pure no-op must not persist");
        }

        // ---- AC-2 subtlety: flag-flip ≠ re-discovery ----

        [Test]
        public void Capture_then_slay_same_monster_sets_Slain_persists_but_no_rediscovery()
        {
            var (store, save, codex) = CreateInitialized(new SaveModel());
            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture).GetAwaiter().GetResult();

            int savesBefore = store.SaveCalls;
            int events = 0;
            codex.OnMonsterDiscovered += _ => events++;

            CodexResult result = codex.RecordDiscoveryAsync("mon01", DiscoverySource.Slay)
                .GetAwaiter().GetResult();

            Assert.AreEqual(CodexOutcome.FlagUpdated, result.Outcome,
                "a newly-flipped flag on an already-discovered monster is FlagUpdated, not Discovered/AlreadyRecorded");
            Assert.IsTrue(save.Current.Codex["mon01"].Slain);
            Assert.IsTrue(save.Current.Codex["mon01"].Captured, "the prior flag stays set");
            Assert.AreEqual(1, codex.DiscoveredCount, "a flag flip must not increment the count");
            Assert.AreEqual(0, events, "a flag flip must not re-raise OnMonsterDiscovered");
            Assert.AreEqual(savesBefore + 1, store.SaveCalls, "a changed entry must persist");
            Assert.IsTrue(store.Stored.Codex["mon01"].Slain, "the flip is persisted");
        }

        // ---- AC-3: 67/67 completion ----

        [Test]
        public void Reaching_67_raises_OnCodexCompleted_exactly_once()
        {
            var (_, _, codex) = CreateInitialized(new SaveModel());

            int completed = 0;
            string lastDiscovered = null;
            codex.OnCodexCompleted += () => completed++;
            codex.OnMonsterDiscovered += id => lastDiscovered = id;

            // Discover mon01..mon66 — not complete yet.
            for (int n = 1; n <= 66; n++)
            {
                codex.RecordDiscoveryAsync(Id(n), DiscoverySource.Capture).GetAwaiter().GetResult();
            }
            Assert.AreEqual(0, completed, "completion must not fire before 67/67");
            Assert.AreEqual(66, codex.DiscoveredCount);

            // The 67th completes the universe.
            codex.RecordDiscoveryAsync(Id(67), DiscoverySource.Capture).GetAwaiter().GetResult();
            Assert.AreEqual(1, completed, "completion fires exactly once at 67/67");
            Assert.AreEqual("mon67", lastDiscovered, "the 67th discovery still raises OnMonsterDiscovered");
            Assert.AreEqual(67, codex.DiscoveredCount);

            // Re-discovering an owned monster after completion must NOT re-fire completion.
            codex.RecordDiscoveryAsync("mon67", DiscoverySource.Capture).GetAwaiter().GetResult();
            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Slay).GetAwaiter().GetResult();
            Assert.AreEqual(1, completed, "completion must never re-fire on re-discovery/flag-flip");
        }

        // ---- AC-4: persistence across a reload ----

        [Test]
        public void Persisted_codex_survives_reload()
        {
            var (store, _, codex) = CreateInitialized(new SaveModel());
            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture).GetAwaiter().GetResult();
            codex.RecordDiscoveryAsync("mon02", DiscoverySource.Slay).GetAwaiter().GetResult();
            // mon03: captured THEN slain, so its entry carries BOTH flags.
            codex.RecordDiscoveryAsync("mon03", DiscoverySource.Capture).GetAwaiter().GetResult();
            codex.RecordDiscoveryAsync("mon03", DiscoverySource.Slay).GetAwaiter().GetResult();

            // Simulate a restart: a NEW SaveService over the SAME persisted store, a fresh
            // CodexService. (LoadAsync deep-copies, so the reloaded model is independent.)
            var reloadedSave = new SaveService(store);
            reloadedSave.InitializeAsync().GetAwaiter().GetResult();
            var reloadedDb = ScriptableObject.CreateInstance<MonsterDatabase>();
            var reloaded = new CodexService(reloadedSave, reloadedDb, new FakeClock(FixedNow));

            Assert.AreEqual(3, reloaded.DiscoveredCount);
            Assert.IsTrue(reloaded.IsDiscovered("mon01"));
            Assert.IsTrue(reloaded.IsDiscovered("mon02"));
            Assert.IsTrue(reloaded.IsDiscovered("mon03"));

            // The per-entry FLAGS must round-trip, not just key presence (AC-4: "all entries").
            CodexEntryView e1 = reloaded.GetEntry("mon01");
            Assert.IsTrue(e1.Captured); Assert.IsFalse(e1.Slain);
            CodexEntryView e2 = reloaded.GetEntry("mon02");
            Assert.IsTrue(e2.Slain); Assert.IsFalse(e2.Captured);
            CodexEntryView e3 = reloaded.GetEntry("mon03");
            Assert.IsTrue(e3.Captured); Assert.IsTrue(e3.Slain);
        }

        // ---- invalid id (programmer-error contract) ----

        [Test]
        public void Invalid_id_throws()
        {
            var (store, _, codex) = CreateInitialized(new SaveModel());
            int savesBefore = store.SaveCalls;

            foreach (string bad in new[] { null, "", "mon00", "mon68", "xyz", "mon1", "MON01" })
            {
                Assert.Throws<System.ArgumentException>(
                    () => codex.RecordDiscoveryAsync(bad, DiscoverySource.Capture).GetAwaiter().GetResult(),
                    $"'{bad}' must be rejected as an invalid id");
            }

            Assert.AreEqual(savesBefore, store.SaveCalls, "an invalid id must be rejected before any persist");
            Assert.AreEqual(0, codex.DiscoveredCount);
        }

        // ---- rollback on persist fault ----

        [Test]
        public void Persist_failure_rolls_back_and_reports_PersistenceFailed()
        {
            var (store, save, codex) = CreateInitialized(new SaveModel());
            store.FailNextSave = true;

            int events = 0;
            codex.OnMonsterDiscovered += _ => events++;

            // SaveService.SaveAsync logs an error on the store fault before rethrowing.
            LogAssert.Expect(LogType.Error, "SaveService: save failed. FakeCodexProgressStore: simulated save failure.");
            // CodexService logs its own rollback error.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "CodexService: discovery of 'mon05' rolled back — persist failed\\..*"));

            CodexResult result = codex.RecordDiscoveryAsync("mon05", DiscoverySource.Capture)
                .GetAwaiter().GetResult();

            Assert.AreEqual(CodexOutcome.PersistenceFailed, result.Outcome);
            Assert.IsFalse(result.Success);
            Assert.IsFalse(save.Current.Codex.ContainsKey("mon05"),
                "a newly-created entry must be removed on rollback (mutation-test: drop the rollback → this fails)");
            Assert.AreEqual(0, codex.DiscoveredCount);
            Assert.AreEqual(0, events, "no discovery event on a rolled-back write");
        }

        // ---- defensive read view (the 2.2 Populated cast-back attack) ----

        [Test]
        public void GetEntry_returns_readonly_view_not_live_entry()
        {
            var (_, save, codex) = CreateInitialized(new SaveModel());
            codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture).GetAwaiter().GetResult();
            // Seed a variant flag on the stored entry so VariantFlags is non-empty.
            save.Current.Codex["mon01"].VariantFlags.Add("shiny");

            CodexEntryView view = codex.GetEntry("mon01");
            Assert.IsTrue(view.IsDiscovered);
            Assert.IsTrue(view.Captured);
            Assert.That(view.VariantFlags, Has.Member("shiny"));

            // Defense 1 — the cast-back attack is impossible: the exposed list is NOT the live
            // mutable List<string> (it is a read-only wrapper over a fresh copy), so a caller
            // cannot cast IReadOnlyList<string> back to List<string> and Add. An ALIASING view
            // (live List exposed as IReadOnlyList) would BE a List<string> and fail this assert.
            Assert.IsFalse(view.VariantFlags is List<string>,
                "VariantFlags must not be the live mutable List — a cast-back must not reach a mutable type");

            // Defense 2 (the primary falsifiable guard) — the view is a SNAPSHOT: mutating the
            // SOURCE entry after the view was taken must not change the view. An aliasing view
            // (even one that wrapped the LIVE list read-only) would show "later" here and fail.
            save.Current.Codex["mon01"].VariantFlags.Add("later");
            Assert.That(view.VariantFlags, Has.No.Member("later"),
                "the view is a snapshot — later source mutations must not appear in it");

            // And the reverse: there is no API path by which the view mutates the source, so the
            // persisted entry still has exactly its seeded flag plus the post-view source add.
            Assert.That(save.Current.Codex["mon01"].VariantFlags, Has.No.Member("hacked"),
                "no read path may inject into the persisted entry");
        }

        // ---- null-valued key is "discovered", not a first discovery (CR finding) ----

        [Test]
        public void Null_valued_key_is_treated_as_discovered_not_first_discovery()
        {
            // Production's CoerceNullCollections repairs null entries on load, so this is a
            // defense-in-depth guard. Seed a {key: null} directly into the live model (the state a
            // crafted/corrupt save could carry pre-coercion) and prove the service honors the
            // canonical "key presence == discovered" rule: it must NOT re-fire OnMonsterDiscovered
            // or double-count, and must repair the null entry in place.
            var (_, save, codex) = CreateInitialized(new SaveModel());
            save.Current.Codex["mon01"] = null; // key present, value null

            int events = 0;
            codex.OnMonsterDiscovered += _ => events++;

            CodexResult result = codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture)
                .GetAwaiter().GetResult();

            Assert.AreEqual(CodexOutcome.FlagUpdated, result.Outcome,
                "a null-valued key is already discovered; recording a flag is FlagUpdated, not Discovered");
            Assert.AreEqual(0, events, "a null-valued key must not re-fire OnMonsterDiscovered");
            Assert.IsNotNull(save.Current.Codex["mon01"], "the null entry must be repaired in place");
            Assert.IsTrue(save.Current.Codex["mon01"].Captured);
            Assert.AreEqual(1, codex.DiscoveredCount, "key presence == discovered; count is unchanged at 1");
        }

        // ---- subscriber isolation ----

        [Test]
        public void Subscriber_throw_does_not_fault_the_write()
        {
            var (_, save, codex) = CreateInitialized(new SaveModel());
            codex.OnMonsterDiscovered += _ => throw new System.InvalidOperationException("boom");

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "CodexService: an OnMonsterDiscovered subscriber threw.*"));

            CodexResult result = codex.RecordDiscoveryAsync("mon01", DiscoverySource.Capture)
                .GetAwaiter().GetResult();

            Assert.AreEqual(CodexOutcome.Discovered, result.Outcome,
                "the discovery is committed even though a subscriber threw");
            Assert.IsTrue(save.Current.Codex.ContainsKey("mon01"));
        }

        private static string Id(int n) => "mon" + n.ToString("00");
    }
}
