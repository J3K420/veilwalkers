using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.AR;
using Veilwalkers.Billing;
using Veilwalkers.Core;
using Veilwalkers.Core.Contracts;
using Veilwalkers.Economy;
using Veilwalkers.Monsters;
using Veilwalkers.Persistence;

namespace Veilwalkers.Encounter.Tests
{
    /// <summary>
    /// Story 5.4 — the mid-encounter top-up / Shop-round-trip snapshot engine. Drives the REAL
    /// <see cref="EncounterService"/> snapshot/rehydrate over a real <see cref="SaveService"/> + the
    /// deep-cloning <see cref="FakeEncounterProgressStore"/> (clones <c>EncounterSnapshot</c>, so a
    /// "persisted / restored" assertion is honest, never aliased — [[tautological-test-trap]]). Every pin
    /// asserts on the STORED (post-clone) snapshot or on a re-snapshot-after-teardown-and-rehydrate, never a
    /// literal recomputed from the live model.
    /// <para>
    /// AC-1 (the world does not lock) is proven via a real insufficient-credit SLAY (the mid-encounter
    /// credit-spend trigger — a Lure is Idle-only). AC-4 (relaunch recovers BOTH the encounter AND the pending
    /// purchase) drives the real 5.2 <see cref="PurchaseReconciler.ReconcilePendingOnLaunchAsync"/> + the new
    /// rehydrate over ONE shared stored model.
    /// </para>
    /// </summary>
    public sealed class EncounterSnapshotTests
    {
        private const string MonsterId = "mon01";

        private static readonly DateTime FixedNow =
            new DateTime(2026, 6, 14, 9, 30, 0, DateTimeKind.Utc);

        // A minimal IStoreAdapter for the AC-4 relaunch pin — the reconciler acknowledges a granted record.
        private sealed class FakeStore : IStoreAdapter
        {
            public Task<StorePurchaseResult> PurchaseAsync(string packId) =>
                Task.FromResult(StorePurchaseResult.Succeeded(packId, "order-stub"));

            public Task<bool> AcknowledgeAsync(string playOrderId) => Task.FromResult(true);

            public Task<IReadOnlyDictionary<string, string>> FetchLocalizedPricesAsync(IEnumerable<string> packIds) =>
                Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        }

        private sealed class Harness
        {
            public FakeEncounterProgressStore Store;
            public SaveService Save;
            public CreditService Credit;
            public SaveMutationLock Lock;
            public FakeArAnchorProvider AnchorProvider;
            public MonsterDatabase Db;
            public FakeRandom Random;
            public EncounterService Encounter;
        }

        // A roster spanning the rare floor: mon01 Common, mon02 Rare, mon03 Epic.
        private static MonsterDatabase SeededDb()
        {
            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            db.SetForTests(new[]
            {
                MakeDef("mon01", Rarity.Common),
                MakeDef("mon02", Rarity.Rare),
                MakeDef("mon03", Rarity.Epic),
            });
            return db;
        }

        private static MonsterDefinition MakeDef(string id, Rarity rarity)
        {
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.SetForTests(id, id, rarity, 0, 0, "lore", null);
            return def;
        }

        // Build a harness over the GIVEN store (so the AC-4 relaunch pin can construct a SECOND harness over
        // the same Stored model — the genuine "services recreated on relaunch" path, the 5.2 precedent).
        private static Harness HarnessOver(FakeEncounterProgressStore store)
        {
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();

            var mutationLock = new SaveMutationLock();
            var credit = new CreditService(save, mutationLock);
            var rules = new ProgressionRules(new[] { 100, 200, 300 }, 1, 1, 1);
            var progression = new ProgressionService(save, rules, mutationLock);

            var db = SeededDb();
            var codex = new CodexService(save, db, new FakeClock(FixedNow));

            var anchorProvider = new FakeArAnchorProvider();
            var anchorRestore = new AnchorRestoreService(anchorProvider);

            var arSession = new FakeArSession();
            var permission = new FakeCameraPermission { HasCameraPermission = true };
            var permissionFlow = new CameraPermissionFlow(permission);
            var arSessionService = new ArSessionService(arSession, permissionFlow);

            var config = ScriptableObject.CreateInstance<EconomyConfig>(); // canon costs 1/4/5, Slay 3
            var random = new FakeRandom();
            var lureSystem = new LureSystem(config, db, random);
            var planeAnchor = new PlaneAnchorService(anchorProvider);
            var spawnSink = new FakeSpawnSink();
            var spawner = new MonsterSpawner(spawnSink);
            var captureSystem = new CaptureSystem(random);
            var slaySystem = new SlaySystem(config, random);

            var encounter = new EncounterService(
                save, credit, progression, codex, mutationLock, anchorRestore, arSessionService,
                lureSystem, planeAnchor, spawner, captureSystem, slaySystem);

            return new Harness
            {
                Store = store,
                Save = save,
                Credit = credit,
                Lock = mutationLock,
                AnchorProvider = anchorProvider,
                Db = db,
                Random = random,
                Encounter = encounter,
            };
        }

        private static Harness CreateHarness(SaveModel seed) =>
            HarnessOver(new FakeEncounterProgressStore { Stored = seed });

        // Drive a Basic Lure to a live (Lured) encounter — mon01 Common (the LuredHarness shape).
        private static Harness LuredHarness(SaveModel seed)
        {
            var h = CreateHarness(seed);
            h.AnchorProvider.AvailablePlacements = 1;
            h.Random.EnqueueDouble(0.99).EnqueueNext(0); // common roll → mon01
            LureResult lure = h.Encounter.TryLureAsync(LureKind.Basic).GetAwaiter().GetResult();
            Assert.IsTrue(lure.Success, "Precondition: the Lure settled a Monster (encounter is Lured).");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State);
            return h;
        }

        // ---- AC-2 / AC-3: snapshot captures the live encounter ----

        [Test]
        public void Snapshot_captures_the_live_Lured_encounter_state()
        {
            // A Lured encounter with a scanned Monster + an applied extra → the snapshot carries the id, the
            // scan flag (ScanProgress == 1), the applied-extra tag, the Lured State, and the anchor.
            var seed = new SaveModel { Credits = 10, StabilityBoostCharges = 1 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = LuredHarness(seed);
            h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult(); // records the scan flag
            h.Encounter.TryApplyExtraAsync(ExtraKind.StabilityBoost).GetAwaiter().GetResult(); // records the extra

            int savesBefore = h.Store.SaveCalls;
            bool ok = h.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult();

            Assert.IsTrue(ok, "Snapshotting a Lured encounter succeeds.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "AR-8: exactly ONE SaveAsync for the snapshot.");

            EncounterSnapshotData snap = h.Store.Stored.EncounterSnapshot;
            Assert.IsNotNull(snap, "The snapshot persisted to the stored model.");
            Assert.AreEqual("Lured", snap.State, "The Lured state tag persisted.");
            Assert.AreEqual(1, snap.Monsters.Count, "One lured Monster captured.");
            Assert.AreEqual(MonsterId, snap.Monsters[0].MonsterId, "...with its id.");
            Assert.Greater(snap.Monsters[0].ScanProgress, 0f, "...and its scan flag (scanned → ScanProgress > 0).");
            Assert.AreEqual(1, snap.Anchors.Length, "The encounter's anchor captured.");
            CollectionAssert.Contains(snap.AppliedExtras, "StabilityBoost", "The applied extra captured as a stable tag.");
        }

        [Test]
        public void Snapshot_then_rehydrate_over_a_fresh_service_restores_state_faithful()
        {
            // The headline pin (AC-2/AC-3) — simulate the Shop navigation the GENUINE way: snapshot, then
            // construct a FRESH EncounterService over the SAME Stored model (the encounter/services were torn
            // down by leaving to Shop — NOT EndEncounter, which is the normal end). Rehydrate on the fresh
            // service, then re-snapshot: the two snapshot DTOs are field-equal (anti-tautology).
            var seed = new SaveModel { Credits = 10, NightveilFilterCharges = 1 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h1 = LuredHarness(seed);
            h1.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();
            h1.Encounter.TryApplyExtraAsync(ExtraKind.NightveilFilter).GetAwaiter().GetResult();
            Assert.IsTrue(h1.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult());
            EncounterSnapshotData before = h1.Store.Stored.EncounterSnapshot;

            // Relaunch: a fresh service over the same stored model (services recreated, encounter torn down).
            var h2 = HarnessOver(h1.Store);
            Assert.AreEqual(EncounterState.Idle, h2.Encounter.State, "A fresh service starts Idle.");

            bool restored = h2.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult();
            Assert.IsTrue(restored, "Rehydrate restores the encounter from the persisted snapshot.");
            Assert.AreEqual(EncounterState.Lured, h2.Encounter.State, "...to the Lured state it left.");

            // Re-snapshot the rehydrated encounter and compare DTOs field-by-field (the consumed snapshot was
            // cleared on rehydrate, so re-snapshotting writes a fresh one).
            Assert.IsTrue(h2.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult());
            EncounterSnapshotData after = h2.Store.Stored.EncounterSnapshot;

            AssertSnapshotsFieldEqual(before, after);
        }

        [Test]
        public void Multi_lure_snapshot_round_trips_both_monsters_with_correct_per_id_scan_flags()
        {
            // The index-parallel pairing case (AC-2/AC-3): a Multi-Lure (2 ids, 2 anchors), one scanned + one
            // not, must round-trip both Monsters with their CORRECT per-id scan flags and paired anchors.
            var seed = new SaveModel { Credits = 10 };
            // Both spawned Monsters discovered so a Scan flips the persistent flag deterministically; mon01
            // common, mon02 rare. The Multi rolls two independent monsters; seed them as discovered.
            seed.Codex["mon01"] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            seed.Codex["mon02"] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = CreateHarness(seed);
            h.AnchorProvider.AvailablePlacements = 2;
            // Multi rolls two monsters: force mon01 (common) then mon02 (rare) deterministically.
            h.Random.EnqueueDouble(0.99, 0.0).EnqueueNext(0, 0);
            LureResult lure = h.Encounter.TryLureAsync(LureKind.Multi).GetAwaiter().GetResult();
            Assert.IsTrue(lure.Success, "Precondition: the Multi-Lure spawned two Monsters.");
            Assert.AreEqual(2, lure.SpawnedMonsterIds.Count, "Two lured ids.");

            // Scan exactly ONE of the two spawned Monsters. The two ids must differ for the per-id pairing
            // assertion to be meaningful (the scripted roll forces mon01 + mon02).
            string scanned = lure.SpawnedMonsterIds[0];
            string notScanned = lure.SpawnedMonsterIds[1];
            Assert.AreNotEqual(scanned, notScanned, "Precondition: the Multi-Lure rolled two DISTINCT monsters.");
            h.Encounter.TryScanAsync(scanned).GetAwaiter().GetResult();

            Assert.IsTrue(h.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult());
            EncounterSnapshotData snap = h.Store.Stored.EncounterSnapshot;

            Assert.AreEqual(2, snap.Monsters.Count, "Both Monsters captured.");
            Assert.AreEqual(2, snap.Anchors.Length, "Both anchors captured (index-parallel).");
            float scannedProgress = ProgressFor(snap, scanned);
            float notScannedProgress = ProgressFor(snap, notScanned);
            Assert.Greater(scannedProgress, 0f, "The scanned Monster's flag landed on the RIGHT id.");
            Assert.AreEqual(0f, notScannedProgress, "The un-scanned Monster's flag is 0 (not mis-paired).");
        }

        [Test]
        public void Snapshot_survives_a_serialized_disk_hop_and_rehydrates_losslessly()
        {
            // AC-3: snapshot, take the cloned Stored (the "disk"), build a fresh service over it, rehydrate,
            // re-snapshot → field-equal to the original. Proves the DTO survives the store round-trip.
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h1 = LuredHarness(seed);
            Assert.IsTrue(h1.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult());
            EncounterSnapshotData before = h1.Store.Stored.EncounterSnapshot;

            var h2 = HarnessOver(h1.Store);
            Assert.IsTrue(h2.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult());
            Assert.IsTrue(h2.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult());

            AssertSnapshotsFieldEqual(before, h2.Store.Stored.EncounterSnapshot);
        }

        // ---- snapshot lifecycle: consumed once, one persist, guards ----

        [Test]
        public void Rehydrate_consumes_the_snapshot_so_a_second_rehydrate_is_a_noop()
        {
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h1 = LuredHarness(seed);
            Assert.IsTrue(h1.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult());

            var h2 = HarnessOver(h1.Store);
            Assert.IsTrue(h2.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult(), "First rehydrate restores.");
            Assert.IsNull(h2.Store.Stored.EncounterSnapshot, "The snapshot is consumed (cleared + persisted).");

            // A fresh service over the now-cleared store: a second rehydrate finds nothing.
            var h3 = HarnessOver(h2.Store);
            Assert.IsFalse(h3.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult(), "A second rehydrate is a no-op (snapshot consumed).");
        }

        [Test]
        public void Snapshot_from_Idle_is_a_noop_with_no_write()
        {
            var h = CreateHarness(new SaveModel { Credits = 10 });
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State);
            int savesBefore = h.Store.SaveCalls;

            LogAssert.ignoreFailingMessages = true; // the guard warns
            bool ok = h.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(ok, "Nothing to snapshot from Idle.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "No write for an Idle snapshot.");
            Assert.IsNull(h.Store.Stored.EncounterSnapshot, "No snapshot persisted.");
        }

        [Test]
        public void Rehydrate_with_no_snapshot_is_a_silent_noop()
        {
            var h = CreateHarness(new SaveModel { Credits = 10 }); // EncounterSnapshot == null
            int savesBefore = h.Store.SaveCalls;

            bool ok = h.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult();

            Assert.IsFalse(ok, "No snapshot → no rehydrate.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "No write when there is nothing to rehydrate.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "Stays Idle.");
        }

        [Test]
        public void Rehydrate_over_an_active_encounter_does_not_clobber_it()
        {
            // Seed a stored snapshot, then drive a LIVE encounter on the same store, then rehydrate → refused.
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h1 = LuredHarness(seed);
            Assert.IsTrue(h1.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult());

            // Same harness is STILL Lured (snapshot does not end the encounter).
            Assert.AreEqual(EncounterState.Lured, h1.Encounter.State);
            LogAssert.ignoreFailingMessages = true; // the guard warns
            bool ok = h1.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(ok, "Rehydrate refuses to clobber an active encounter.");
            Assert.IsNotNull(h1.Store.Stored.EncounterSnapshot, "The snapshot is left intact for a later from-Idle rehydrate.");
        }

        [Test]
        public void Rehydrate_a_corrupt_snapshot_clears_it_without_crashing()
        {
            // A snapshot with an unrecognized State (corrupt) → cleared, no rehydrate, no throw (NFR-3). One
            // anchor paired with one Monster so ONLY the State tag is corrupt (isolates the State guard).
            var seed = new SaveModel { Credits = 10 };
            seed.EncounterSnapshot = new EncounterSnapshotData
            {
                State = "GarbageState",
                Monsters = new List<EncounterMonsterStateData>
                {
                    new EncounterMonsterStateData { MonsterId = MonsterId, ScanProgress = 0f },
                },
                Anchors = new[] { new AnchorToken("a1", Vector3.zero, Quaternion.identity) },
                AppliedExtras = new List<string>(),
            };
            var h = CreateHarness(seed);

            LogAssert.ignoreFailingMessages = true; // the corrupt path logs an Error
            bool ok = h.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(ok, "A corrupt snapshot does not rehydrate.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "Stays Idle (no half-encounter).");
            Assert.IsNull(h.Store.Stored.EncounterSnapshot, "The corrupt snapshot is cleared (never loops).");
        }

        [Test]
        public void Rehydrate_a_snapshot_with_a_null_monster_id_clears_it_without_crashing()
        {
            // CR patch (Decision H semantic guard): a Monster with a null/empty id is corrupt — CoerceNullCollections
            // repairs null COLLECTIONS but not null id STRINGS. Cleared, no rehydrate, no throw (NFR-3).
            var seed = new SaveModel { Credits = 10 };
            seed.EncounterSnapshot = new EncounterSnapshotData
            {
                State = "Lured",
                Monsters = new List<EncounterMonsterStateData>
                {
                    new EncounterMonsterStateData { MonsterId = null, ScanProgress = 0f },
                },
                Anchors = new[] { new AnchorToken("a1", Vector3.zero, Quaternion.identity) },
                AppliedExtras = new List<string>(),
            };
            var h = CreateHarness(seed);

            LogAssert.ignoreFailingMessages = true; // the corrupt path logs an Error
            bool ok = h.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(ok, "A null-id Monster is a corrupt snapshot — no rehydrate.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "Stays Idle (no phantom monster in flight).");
            Assert.IsNull(h.Store.Stored.EncounterSnapshot, "The corrupt snapshot is cleared.");
        }

        [Test]
        public void Rehydrate_a_snapshot_with_an_anchor_monster_count_mismatch_clears_it()
        {
            // CR patch (Decision H semantic guard): the index-parallel invariant is load-bearing (Story 6.3's
            // re-spawn pairs _activeMonsterIds[i] with _activeAnchors[i]). A length mismatch (2 anchors, 1
            // Monster) is corrupt — cleared, no rehydrate, no out-of-bounds pairing downstream.
            var seed = new SaveModel { Credits = 10 };
            seed.EncounterSnapshot = new EncounterSnapshotData
            {
                State = "Lured",
                Monsters = new List<EncounterMonsterStateData>
                {
                    new EncounterMonsterStateData { MonsterId = MonsterId, ScanProgress = 0f },
                },
                Anchors = new[]
                {
                    new AnchorToken("a1", Vector3.zero, Quaternion.identity),
                    new AnchorToken("a2", Vector3.one, Quaternion.identity),
                },
                AppliedExtras = new List<string>(),
            };
            var h = CreateHarness(seed);

            LogAssert.ignoreFailingMessages = true; // the corrupt path logs an Error
            bool ok = h.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(ok, "An anchors-vs-monsters length mismatch is corrupt — no rehydrate.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "Stays Idle.");
            Assert.IsNull(h.Store.Stored.EncounterSnapshot, "The corrupt snapshot is cleared.");
        }

        [Test]
        public void Rehydrate_rolls_back_to_Idle_when_the_consume_clear_persist_fails()
        {
            // CR patch (finding #4): the consume-clear is AWAITED, not fire-and-forget. If the clear's persist
            // faults, the in-memory restore is rolled back to Idle (disk + memory stay consistent — no relaunch
            // zombie encounter) and the call reports false.
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h1 = LuredHarness(seed);
            Assert.IsTrue(h1.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult());

            var h2 = HarnessOver(h1.Store);
            h2.Store.FailNextSave = true; // the consume-clear persist will fault

            LogAssert.ignoreFailingMessages = true; // the rollback logs an Error
            bool ok = h2.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(ok, "A consume-clear fault fails the rehydrate (not a phantom success).");
            Assert.AreEqual(EncounterState.Idle, h2.Encounter.State, "The in-memory restore rolled back to Idle.");
            Assert.IsNotNull(h2.Store.Stored.EncounterSnapshot,
                "The snapshot survives on disk (the rollback kept disk + memory consistent — the caller may retry).");
        }

        [Test]
        public void ClearEncounterSnapshot_nulls_the_persisted_field_in_one_write()
        {
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = LuredHarness(seed);
            Assert.IsTrue(h.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult());
            Assert.IsNotNull(h.Store.Stored.EncounterSnapshot);
            int savesBefore = h.Store.SaveCalls;

            bool ok = h.Encounter.ClearEncounterSnapshot().GetAwaiter().GetResult();

            Assert.IsTrue(ok, "Clearing an existing snapshot succeeds.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "AR-8: exactly ONE SaveAsync for the clear.");
            Assert.IsNull(h.Store.Stored.EncounterSnapshot, "The persisted snapshot is nulled.");
        }

        [Test]
        public void EndEncounter_clears_the_in_memory_lured_ids()
        {
            // The Task 1 addition: _activeMonsterIds is cleared on EndEncounter (parity with _activeAnchors).
            // After a teardown, a snapshot from the (now Idle) machine is a no-op — proving the live state cleared.
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = LuredHarness(seed);
            Assert.IsTrue(h.Encounter.EndEncounter(), "The encounter ends → Idle.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State);

            LogAssert.ignoreFailingMessages = true;
            bool ok = h.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;
            Assert.IsFalse(ok, "Nothing to snapshot after EndEncounter (the live ids/anchors cleared).");
        }

        // ---- AC-1: the world does not lock on an insufficient-credit SLAY ----

        [Test]
        public void Insufficient_credit_slay_leaves_the_encounter_live_and_snapshottable()
        {
            // The mid-encounter top-up trigger is SLAY (3 credits). Seed just enough for the Basic Lure (cost 1)
            // but NOT the Slay: Credits = 2 → after the Lure, 1 left < the Slay's 3. The Slay is rejected, the
            // encounter resolves back to Lured (the world does not lock), OnInsufficientCredits fires, and the
            // encounter is STILL snapshottable.
            var seed = new SaveModel { Credits = 2 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = LuredHarness(seed);
            Assert.AreEqual(1, h.Save.Current.Credits, "Precondition: 1 credit left after the Basic Lure (cost 1) — below the Slay cost (3).");
            int insufficientEvents = 0;
            h.Encounter.OnInsufficientCredits += _ => insufficientEvents++;
            h.Random.EnqueueDouble(0.0); // a winning roll, so the block is purely economic

            SlayResult result = h.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "The Slay is blocked — insufficient Credits.");
            Assert.AreEqual(1, insufficientEvents, "OnInsufficientCredits fired (AR-11 — App/UI opens the top-up sheet).");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "The world does not lock — the encounter is STILL Lured.");
            // The encounter survives: it is still snapshottable for the Shop round-trip.
            Assert.IsTrue(h.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult(),
                "The live encounter can be snapshotted for the top-up Shop round-trip.");
        }

        // ---- AC-4: a relaunch mid-purchase recovers BOTH the encounter AND the pending purchase ----

        [Test]
        public void Relaunch_recovers_both_the_encounter_snapshot_and_the_pending_purchase_exactly_once()
        {
            // Build a Lured encounter + snapshot it; ALSO record a pending Starter purchase (the 5.2 ledger),
            // on the SAME stored model. Simulate a relaunch: a FRESH reconciler grants the purchase exactly
            // once AND a FRESH encounter service rehydrates the encounter — independently.
            var seed = new SaveModel { Credits = 5 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h1 = LuredHarness(seed);
            Assert.IsTrue(h1.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult());

            // Record a pending purchase on the SAME store (its own reconciler over the same SaveService).
            var reconciler1 = new PurchaseReconciler(
                h1.Save, h1.Lock, h1.Credit, new CreditPackCatalog(), new FakeStore(), new FakeClock(FixedNow));
            bool recorded = reconciler1.RecordPendingAsync("order-relaunch", CreditPackCatalog.StarterPackId)
                .GetAwaiter().GetResult();
            Assert.IsTrue(recorded, "Precondition: the pending purchase recorded.");
            Assert.IsNotNull(h1.Store.Stored.EncounterSnapshot, "Precondition: the snapshot persisted.");
            Assert.AreEqual(1, h1.Store.Stored.PendingPurchases.Count, "Precondition: a pending purchase ledger row.");
            int creditsBeforeGrant = h1.Store.Stored.Credits;

            // --- Relaunch: fresh services over the SAME stored model. ---
            var h2 = HarnessOver(h1.Store);
            var reconciler2 = new PurchaseReconciler(
                h2.Save, h2.Lock, h2.Credit, new CreditPackCatalog(), new FakeStore(), new FakeClock(FixedNow));

            // (a) The purchase reconciles exactly once (Credits += 50, the pending row settled).
            reconciler2.ReconcilePendingOnLaunchAsync().GetAwaiter().GetResult();
            Assert.AreEqual(creditsBeforeGrant + 50, h2.Credit.Balance,
                "The pending Starter purchase granted its 50 credits exactly once on relaunch (5.2).");

            // (b) The encounter rehydrates state-faithful.
            Assert.IsTrue(h2.Encounter.RehydrateFromSnapshot().GetAwaiter().GetResult(), "The encounter rehydrated from the snapshot.");
            Assert.AreEqual(EncounterState.Lured, h2.Encounter.State, "...to the Lured state.");
            Assert.IsTrue(h2.Encounter.SnapshotActiveEncounter().GetAwaiter().GetResult(),
                "...and it is a live, re-snapshottable encounter.");
            Assert.AreEqual(MonsterId, h2.Store.Stored.EncounterSnapshot.Monsters[0].MonsterId,
                "...with the original lured Monster restored.");
        }

        // ---- helpers ----

        private static float ProgressFor(EncounterSnapshotData snap, string monsterId)
        {
            foreach (EncounterMonsterStateData m in snap.Monsters)
            {
                if (m.MonsterId == monsterId)
                {
                    return m.ScanProgress;
                }
            }

            Assert.Fail($"Monster '{monsterId}' not found in the snapshot.");
            return 0f;
        }

        private static void AssertSnapshotsFieldEqual(EncounterSnapshotData a, EncounterSnapshotData b)
        {
            Assert.IsNotNull(a, "snapshot A is non-null");
            Assert.IsNotNull(b, "snapshot B is non-null");
            Assert.AreEqual(a.State, b.State, "State tag round-trips.");
            Assert.AreEqual(a.Anchors.Length, b.Anchors.Length, "Anchor count round-trips.");
            Assert.AreEqual(a.Monsters.Count, b.Monsters.Count, "Monster count round-trips.");
            for (int i = 0; i < a.Monsters.Count; i++)
            {
                Assert.AreEqual(a.Monsters[i].MonsterId, b.Monsters[i].MonsterId, $"Monster[{i}] id round-trips.");
                Assert.AreEqual(a.Monsters[i].ScanProgress, b.Monsters[i].ScanProgress, $"Monster[{i}] scan flag round-trips.");
            }

            CollectionAssert.AreEquivalent(a.AppliedExtras, b.AppliedExtras, "Applied extras round-trip.");
        }
    }
}
