using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.AR;
using Veilwalkers.Core;
using Veilwalkers.Core.Contracts;
using Veilwalkers.Economy;
using Veilwalkers.Monsters;
using Veilwalkers.Persistence;

namespace Veilwalkers.Encounter.Tests
{
    /// <summary>
    /// <see cref="EncounterService"/> as the Story 4.1 spine: AC-1 ctor injection (no service locator), AC-2
    /// the atomic multi-delta write (ONE persist for charge AND codex, whole-mutation rollback on fault,
    /// recovery-swap guard), and AC-3 the OnArSessionInterrupted → Suspended → recovery-TryRestore wiring.
    /// Built on a real <see cref="SaveService"/> over a deep-cloning <see cref="FakeEncounterProgressStore"/>
    /// (clones Codex, so the codex-rollback assertions are honest), real Economy services sharing one
    /// <see cref="SaveMutationLock"/>, a real <see cref="CodexService"/> over an in-memory
    /// <see cref="MonsterDatabase"/>, and a real <see cref="AnchorRestoreService"/> over a fake provider.
    /// Every test drives the production service; none recompute its logic (the anti-tautology bar).
    /// </summary>
    public sealed class EncounterServiceTests
    {
        private const string MonsterId = "mon01";

        private static readonly DateTime FixedNow =
            new DateTime(2026, 6, 14, 9, 30, 0, DateTimeKind.Utc);

        private sealed class Harness
        {
            public FakeEncounterProgressStore Store;
            public SaveService Save;
            public CreditService Credit;
            public ProgressionService Progression;
            public CodexService Codex;
            public SaveMutationLock Lock;
            public FakeArAnchorProvider AnchorProvider;
            public AnchorRestoreService AnchorRestore;
            public FakeArSession ArSession;
            public FakeCameraPermission Permission;
            public CameraPermissionFlow PermissionFlow;
            public ArSessionService ArSessionService;
            public MonsterDatabase Db;
            public EconomyConfig Config;
            public FakeRandom Random;
            public LureSystem LureSystem;
            public PlaneAnchorService PlaneAnchor;
            public FakeSpawnSink SpawnSink;
            public MonsterSpawner Spawner;
            public EncounterService Encounter;
        }

        // A roster spanning the rare floor for the LureSystem rarity roll: mon01 Common, mon02 Rare, mon03 Epic.
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

        private static Harness CreateHarness(SaveModel seed)
        {
            var store = new FakeEncounterProgressStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();

            var mutationLock = new SaveMutationLock();
            var credit = new CreditService(save, mutationLock);
            // A minimal progression rules: thresholds don't matter for the composed-write tests.
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

            // Story 4.2 Lure collaborators. The SAME anchorProvider backs BOTH AnchorRestore (recovery) and
            // PlaneAnchorService (forward placement) — set AnchorProvider.AvailablePlacements in a test to
            // grant placements. A fresh FakeRandom (script per test). The spawner over a counting sink.
            var config = ScriptableObject.CreateInstance<EconomyConfig>(); // default canon costs 1/4/5
            var random = new FakeRandom();
            var lureSystem = new LureSystem(config, db, random);
            var planeAnchor = new PlaneAnchorService(anchorProvider);
            var spawnSink = new FakeSpawnSink();
            var spawner = new MonsterSpawner(spawnSink);

            var encounter = new EncounterService(
                save, credit, progression, codex, mutationLock, anchorRestore, arSessionService,
                lureSystem, planeAnchor, spawner);

            return new Harness
            {
                Store = store,
                Save = save,
                Credit = credit,
                Progression = progression,
                Codex = codex,
                Lock = mutationLock,
                AnchorProvider = anchorProvider,
                AnchorRestore = anchorRestore,
                ArSession = arSession,
                Permission = permission,
                PermissionFlow = permissionFlow,
                ArSessionService = arSessionService,
                Db = db,
                Config = config,
                Random = random,
                LureSystem = lureSystem,
                PlaneAnchor = planeAnchor,
                SpawnSink = spawnSink,
                Spawner = spawner,
                Encounter = encounter,
            };
        }

        /// <summary>Drive the real AR session to Running, then revoke permission + resume — the genuine
        /// path that raises OnArSessionInterrupted (the AC-3 trigger 3.3 owns).</summary>
        private static void RaiseRealArInterruption(Harness h)
        {
            h.ArSessionService.EnterAsync().GetAwaiter().GetResult(); // Cold → Running (prewarm completes sync)
            Assert.AreEqual(ArSessionState.Running, h.ArSessionService.State, "Precondition: session is Running.");
            h.ArSessionService.OnApplicationPause(true); // Running → Paused
            h.Permission.HasCameraPermission = false; // revoked while backgrounded
            LogAssert.ignoreFailingMessages = true; // the resume path warns about the revocation
            h.ArSessionService.OnApplicationPause(false); // raises OnArSessionInterrupted
            LogAssert.ignoreFailingMessages = false;
        }

        // ---- AC-1: ctor injection ----

        [Test]
        public void Ctor_null_args_throw()
        {
            var h = CreateHarness(new SaveModel());
            Assert.Throws<ArgumentNullException>(() => new EncounterService(null, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, null, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, null, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, null, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, null, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, null, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, null, h.LureSystem, h.PlaneAnchor, h.Spawner));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, null, h.PlaneAnchor, h.Spawner));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, null, h.Spawner));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, null));
        }

        // ---- AC-2: atomic multi-delta commit ----

        [Test]
        public void Capture_commits_charge_and_codex_in_ONE_save_write()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 1 });
            int savesBefore = h.Store.SaveCalls;

            SpendResult result = h.Encounter.TryCaptureAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "A capture with a charge available succeeds.");
            // BOTH slices committed:
            Assert.AreEqual(0, h.Progression.GetChargeCount(ChargeType.StrongCapture), "The charge was consumed.");
            Assert.IsTrue(h.Codex.IsDiscovered(MonsterId), "The codex Capture discovery was recorded.");
            Assert.IsTrue(h.Codex.GetEntry(MonsterId).Captured, "The Captured flag is set.");
            // ONE persist for the whole action (AR-8 — never two persists per action).
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "Exactly ONE SaveAsync for the composed action.");
            // ...and it actually persisted both slices (read back the stored snapshot).
            Assert.AreEqual(0, h.Store.Stored.StrongCaptureCharges);
            Assert.IsTrue(h.Store.Stored.Codex.ContainsKey(MonsterId));
        }

        [Test]
        public void Persist_fault_rolls_back_BOTH_slices()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 1 });
            h.Store.FailNextSave = true;
            int savesBefore = h.Store.SaveCalls;

            // The persist fault logs from BOTH SaveService ("save failed") and EncounterService ("rolled
            // back") — assert the BEHAVIOR (the typed failure + the two-slice rollback), not the exact logs.
            LogAssert.ignoreFailingMessages = true;
            SpendResult result = h.Encounter.TryCaptureAsync(MonsterId).GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(result.Success, "A persist fault is a typed failure, not a faulted task.");
            Assert.AreEqual(SpendFailureReason.PersistenceFailed, result.FailureReason);
            // BOTH slices reverted to pre-action values (mutation-testable: reverting only one → red).
            Assert.AreEqual(1, h.Progression.GetChargeCount(ChargeType.StrongCapture), "The charge is restored.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "The codex entry was NOT leaked.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "Exactly one SaveAsync was attempted.");
        }

        [Test]
        public void Persist_fault_does_NOT_remove_a_pre_existing_codex_entry()
        {
            // The structural-codex-rollback pin: when the action's persist FAILS, the rollback must NOT delete
            // a codex entry that pre-existed the action (the key-PRE-EXISTED RollBack case). The composed
            // action here re-captures an already-Captured monster — the codex stage is a no-op (flag already
            // set), but the CHARGE still mutates + persists + fails, exercising the rollback path. A naive
            // revert that blindly did Codex.Remove(id) would delete the pre-existing entry → this test goes red.
            var seed = new SaveModel { StrongCaptureCharges = 1 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = CreateHarness(seed);
            h.Store.FailNextSave = true;

            LogAssert.ignoreFailingMessages = true; // SaveService + EncounterService both log on the fault
            SpendResult result = h.Encounter.TryCaptureAsync(MonsterId).GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(result.Success, "The persist fault is a typed failure.");
            Assert.IsTrue(h.Codex.IsDiscovered(MonsterId), "The pre-existing entry must survive the rollback (not removed).");
            Assert.IsTrue(h.Codex.GetEntry(MonsterId).Captured, "Its Captured flag is intact.");
            Assert.AreEqual("2026-06-01", h.Codex.GetEntry(MonsterId).Discovered, "Its first-discovered date is intact.");
            Assert.AreEqual(1, h.Progression.GetChargeCount(ChargeType.StrongCapture), "The charge is restored.");
        }

        [Test]
        public void Zero_charge_capture_is_a_typed_failure_with_no_persist_and_no_codex_write()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 0 });
            int savesBefore = h.Store.SaveCalls;

            SpendResult result = h.Encounter.TryCaptureAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SpendFailureReason.InsufficientCharges, result.FailureReason, "Zero charges is the 'earn via XP' block.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "No codex write on a blocked action.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "No persist on a blocked action.");
        }

        [Test]
        public void Capture_raises_the_discovery_event_on_a_committed_first_discovery()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 1 });
            string discovered = null;
            h.Codex.OnMonsterDiscovered += id => discovered = id;

            h.Encounter.TryCaptureAsync(MonsterId).GetAwaiter().GetResult();

            Assert.AreEqual(MonsterId, discovered, "The committed first discovery raises OnMonsterDiscovered (via the staged handle).");
        }

        [Test]
        public void In_encounter_capture_drives_Acting_to_Resolving_to_Lured_around_the_atomic_write()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 1 });
            h.Encounter.BeginLure(new[] { new AnchorToken("trk1", Vector3.zero, Quaternion.identity) });
            h.Encounter.BeginAction(); // Acting
            int savesBefore = h.Store.SaveCalls;

            SpendResult result = h.Encounter.TryCaptureInEncounterAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "A committed in-encounter action returns to Lured (the encounter continues).");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "Still exactly ONE persist for the composed action.");
            Assert.IsTrue(h.Codex.IsDiscovered(MonsterId));
            Assert.AreEqual(0, h.Progression.GetChargeCount(ChargeType.StrongCapture));
        }

        [Test]
        public void In_encounter_capture_with_no_charge_fails_and_returns_to_Lured_for_retry()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 0 });
            h.Encounter.BeginLure(Array.Empty<AnchorToken>());
            h.Encounter.BeginAction(); // Acting

            SpendResult result = h.Encounter.TryCaptureInEncounterAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SpendFailureReason.InsufficientCharges, result.FailureReason);
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "A failed action keeps the encounter live for a free Retry (FR-10).");
        }

        [Test]
        public void Invalid_monster_id_throws_a_programmer_error()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 1 });
            Assert.Throws<ArgumentException>(() => h.Encounter.TryCaptureAsync("not-a-monster").GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => h.Encounter.TryCaptureAsync(null).GetAwaiter().GetResult());
        }

        // ---- AC-3: Suspended on interruption ----

        [Test]
        public void Active_encounter_suspends_on_a_real_AR_interruption()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 1 });
            h.Encounter.BeginLure(new[] { new AnchorToken("trk1", Vector3.zero, Quaternion.identity) });
            h.Encounter.BeginAction(); // Acting (active)

            RaiseRealArInterruption(h);

            Assert.AreEqual(EncounterState.Suspended, h.Encounter.State, "An active encounter suspends on AR loss.");
        }

        [Test]
        public void Idle_encounter_ignores_an_AR_interruption()
        {
            var h = CreateHarness(new SaveModel());
            // No active encounter (Idle). The Suspend warns + no-ops; the interruption must not throw.
            LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(() => RaiseRealArInterruption(h));
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "An idle encounter is not suspended by an interruption.");
        }

        // ---- AC-3: recovery → TryRestore ----

        [Test]
        public void Recovery_reacquires_and_resumes_the_encounter()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 1 });
            h.Encounter.BeginLure(new[] { new AnchorToken("trk1", Vector3.zero, Quaternion.identity) });
            h.Encounter.BeginAction();
            RaiseRealArInterruption(h);
            Assert.AreEqual(EncounterState.Suspended, h.Encounter.State);

            h.AnchorProvider.ReacquireSucceeds = true; // the trackable comes back → Restored

            AnchorRestoreResult outcome = h.Encounter.Recover();

            Assert.AreEqual(AnchorRestoreResult.Restored, outcome);
            Assert.AreEqual(1, h.AnchorProvider.ReacquireCalls, "TryRestore was actually invoked (the seam pin).");
            Assert.AreEqual(EncounterState.Acting, h.Encounter.State, "A successful restore resumes the encounter to the state it left.");
        }

        [Test]
        public void Recovery_relocates_when_the_anchor_is_lost_but_a_plane_is_available()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 1 });
            h.Encounter.BeginLure(new[] { new AnchorToken("trk1", Vector3.zero, Quaternion.identity) });
            h.Encounter.BeginAction();
            RaiseRealArInterruption(h);

            h.AnchorProvider.ReacquireSucceeds = false; // lost
            h.AnchorProvider.Candidates = new[]
            {
                new PlaneCandidate(new Pose(new Vector3(0, 0, 3), Quaternion.identity), true, 3f),
            };

            AnchorRestoreResult outcome = h.Encounter.Recover();

            Assert.AreEqual(AnchorRestoreResult.RelocatedToPlane, outcome, "A lost anchor relocates rather than vanishing.");
            Assert.AreEqual(EncounterState.Acting, h.Encounter.State, "A relocation also resumes the encounter.");
        }

        [Test]
        public void Recovery_stays_suspended_when_no_anchor_can_be_restored()
        {
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 1 });
            h.Encounter.BeginLure(new[] { new AnchorToken("trk1", Vector3.zero, Quaternion.identity) });
            h.Encounter.BeginAction();
            RaiseRealArInterruption(h);

            h.AnchorProvider.ReacquireSucceeds = false; // lost
            h.AnchorProvider.Candidates = Array.Empty<PlaneCandidate>(); // no plane → Failed

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("staying Suspended"));
            AnchorRestoreResult outcome = h.Encounter.Recover();

            Assert.AreEqual(AnchorRestoreResult.Failed, outcome);
            Assert.AreEqual(EncounterState.Suspended, h.Encounter.State, "A failed restore stays Suspended (never vanish, NFR-3).");
        }

        [Test]
        public void Recovery_of_a_zero_anchor_encounter_stays_suspended_without_a_false_restore()
        {
            // CR patch: a suspended encounter with NO anchors must NOT silently fall through to Resume()+Restored
            // (claiming a restore that never happened — zero TryRestore calls). It stays Suspended.
            var h = CreateHarness(new SaveModel { StrongCaptureCharges = 1 });
            h.Encounter.BeginLure(Array.Empty<AnchorToken>()); // no anchors (4.2's spawn fills them later)
            h.Encounter.BeginAction();
            RaiseRealArInterruption(h);
            Assert.AreEqual(EncounterState.Suspended, h.Encounter.State);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no anchors to restore"));
            AnchorRestoreResult outcome = h.Encounter.Recover();

            Assert.AreEqual(AnchorRestoreResult.Failed, outcome, "Zero anchors cannot genuinely restore — not a false Restored.");
            Assert.AreEqual(EncounterState.Suspended, h.Encounter.State, "It stays Suspended (no auto-resume).");
            Assert.AreEqual(0, h.AnchorProvider.ReacquireCalls, "No TryRestore/reacquire was attempted (nothing to restore).");
            Assert.AreEqual(0, h.AnchorProvider.GetRelocationCandidatesCalls, "No relocation query either.");
        }

        // ---- AC-1 (Story 4.2): Lure deducts only on a successful spawn, in ONE persist ----

        [Test]
        public void Basic_lure_deducts_once_spawns_and_enters_Lured()
        {
            var h = CreateHarness(new SaveModel { Credits = 10 });
            h.AnchorProvider.AvailablePlacements = 1; // one plane → one placement
            h.Random.EnqueueDouble(0.99).EnqueueNext(0); // common roll → mon01
            int savesBefore = h.Store.SaveCalls;

            LureResult result = h.Encounter.TryLureAsync(LureKind.Basic).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "A Basic Lure with credits + a plane succeeds.");
            Assert.AreEqual(9, result.Spend.NewBalance, "Deducted exactly the Basic cost (1).");
            Assert.AreEqual(9, h.Store.Stored.Credits, "...and persisted the deduction.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "AC-1/AR-8: exactly ONE SaveAsync for the Lure.");
            Assert.AreEqual(1, h.SpawnSink.LiveCount, "One monster spawned.");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "Idle → Lured.");
            CollectionAssert.AreEqual(new[] { "mon01" }, (System.Collections.Generic.List<string>)result.SpawnedMonsterIds);
        }

        [Test]
        public void Lure_with_no_plane_deducts_nothing_and_stays_Idle()
        {
            var h = CreateHarness(new SaveModel { Credits = 10 });
            h.AnchorProvider.AvailablePlacements = 0; // NO plane
            h.Random.EnqueueDouble(0.99).EnqueueNext(0);
            int savesBefore = h.Store.SaveCalls;

            LureResult result = h.Encounter.TryLureAsync(LureKind.Basic).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(LureFailureReason.NoPlacement, result.FailureReason);
            Assert.AreEqual(10, h.Store.Stored.Credits, "Nothing deducted on a no-placement block (AC-1).");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "No persist at all (save-count unchanged).");
            Assert.AreEqual(0, h.SpawnSink.LiveCount, "No monster spawned.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "The world does not lock — stays Idle.");
        }

        [Test]
        public void Exact_balance_lure_succeeds_to_zero()
        {
            var h = CreateHarness(new SaveModel { Credits = 1 }); // balance == Basic cost
            h.AnchorProvider.AvailablePlacements = 1;
            h.Random.EnqueueDouble(0.99).EnqueueNext(0);

            LureResult result = h.Encounter.TryLureAsync(LureKind.Basic).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "Exact-balance spend succeeds (balance >= cost).");
            Assert.AreEqual(0, result.Spend.NewBalance);
            Assert.AreEqual(0, h.Store.Stored.Credits);
        }

        [Test]
        public void Spawn_refused_after_deduction_refunds_and_aborts()
        {
            // AC-1 / Decision E NFR-3 path: a spawn refused AFTER the persisted deduction rolls the deduction
            // back (refund) so the player is never charged without a spawn. Force the refusal by filling the
            // spawner to its cap before the Lure (so the Lure's TrySpawn is refused post-deduction).
            var h = CreateHarness(new SaveModel { Credits = 10 });
            h.AnchorProvider.AvailablePlacements = 1; // a plane IS available — placement pre-check passes
            h.Random.EnqueueDouble(0.99).EnqueueNext(0);

            // Saturate the pool to MaxConcurrent so the Lure's spawn activation is refused.
            var fillPose = new Pose(Vector3.zero, Quaternion.identity);
            for (int i = 0; i < h.Spawner.MaxConcurrent; i++)
            {
                Assert.IsTrue(h.Spawner.TrySpawn(in fillPose, out _), "Precondition: fill the pool to its cap.");
            }
            int liveBefore = h.SpawnSink.LiveCount;

            LogAssert.ignoreFailingMessages = true; // the refund path warns
            LureResult result = h.Encounter.TryLureAsync(LureKind.Basic).GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(result.Success, "A post-deduction spawn refusal aborts the Lure.");
            Assert.AreEqual(LureFailureReason.PersistenceFailed, result.FailureReason);
            Assert.AreEqual(10, h.Store.Stored.Credits, "The deduction was refunded — player not charged without a spawn (AC-1).");
            Assert.AreEqual(liveBefore, h.SpawnSink.LiveCount, "No extra monster leaked from the refused Lure.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "Stays Idle (the Lure aborted).");
        }

        [Test]
        public void EndEncounter_releases_the_lured_spawns_back_to_the_pool()
        {
            // Patch: spawn handles must be released on EndEncounter so a Lured encounter's spawns do not leak.
            var h = CreateHarness(new SaveModel { Credits = 10 });
            h.AnchorProvider.AvailablePlacements = 2;
            h.Random.EnqueueDouble(0.99, 0.99).EnqueueNext(0, 0);

            LureResult result = h.Encounter.TryLureAsync(LureKind.Multi).GetAwaiter().GetResult();
            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, h.SpawnSink.LiveCount, "Two monsters live during the encounter.");

            h.Encounter.EndEncounter();

            Assert.AreEqual(0, h.SpawnSink.LiveCount, "EndEncounter released both spawns back to the pool (no leak).");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State);
        }

        // ---- AC-4: insufficient credits → blocked + OnInsufficientCredits, nothing persisted ----

        [Test]
        public void Insufficient_credits_blocks_raises_event_and_persists_nothing()
        {
            var h = CreateHarness(new SaveModel { Credits = 0 });
            h.AnchorProvider.AvailablePlacements = 1; // a plane IS available — the block is purely economic
            h.Random.EnqueueDouble(0.99).EnqueueNext(0);

            int eventCount = 0;
            InsufficientCreditsEvent captured = default;
            h.Encounter.OnInsufficientCredits += e => { eventCount++; captured = e; };
            int savesBefore = h.Store.SaveCalls;

            LureResult result = h.Encounter.TryLureAsync(LureKind.Basic).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(LureFailureReason.InsufficientCredits, result.FailureReason);
            Assert.AreEqual(1, eventCount, "OnInsufficientCredits raised exactly once (AC-4).");
            Assert.AreEqual(1, captured.Cost, "Event carries the Basic cost.");
            Assert.AreEqual(0, captured.Balance, "Event carries the unchanged balance.");
            Assert.AreEqual(0, h.Store.Stored.Credits, "Balance unchanged.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "No persist on the rejected spend.");
            Assert.AreEqual(0, h.SpawnSink.LiveCount, "No spawn.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "World does not lock — stays Idle.");
        }

        // ---- AC-3: Multi-Lure spawns two; blocks (and deducts nothing) if fewer than two placements ----

        [Test]
        public void Multi_lure_with_two_planes_deducts_five_once_and_spawns_two()
        {
            var h = CreateHarness(new SaveModel { Credits = 10 });
            h.AnchorProvider.AvailablePlacements = 2; // two planes
            h.Random.EnqueueDouble(0.99, 0.99).EnqueueNext(0, 0); // two common rolls → mon01, mon01
            int savesBefore = h.Store.SaveCalls;

            LureResult result = h.Encounter.TryLureAsync(LureKind.Multi).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "Multi-Lure with two planes succeeds.");
            Assert.AreEqual(5, h.Store.Stored.Credits, "Deducted exactly the Multi cost (5).");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "ONE SaveAsync for the whole Multi-Lure (AR-8).");
            Assert.AreEqual(2, h.SpawnSink.LiveCount, "TWO monsters spawned.");
            Assert.AreEqual(2, result.SpawnedMonsterIds.Count, "Two monster ids reported.");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State);
        }

        [Test]
        public void Multi_lure_with_one_plane_blocks_deducts_nothing_and_leaks_no_spawn()
        {
            var h = CreateHarness(new SaveModel { Credits = 10 });
            h.AnchorProvider.AvailablePlacements = 1; // only ONE plane — cannot place both
            h.Random.EnqueueDouble(0.99, 0.99).EnqueueNext(0, 0);
            int savesBefore = h.Store.SaveCalls;

            LureResult result = h.Encounter.TryLureAsync(LureKind.Multi).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "Require-two-or-block: one plane is not enough (AC-3).");
            Assert.AreEqual(LureFailureReason.NoPlacement, result.FailureReason);
            Assert.AreEqual(10, h.Store.Stored.Credits, "Nothing deducted — no double-charge, no partial-charge.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "No persist at all.");
            Assert.AreEqual(0, h.SpawnSink.LiveCount, "No partial spawn — the first placement was abandoned, none activated.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State);
        }

        // ---- a Lure cannot start mid-encounter ----

        [Test]
        public void Lure_is_refused_when_an_encounter_is_already_active()
        {
            var h = CreateHarness(new SaveModel { Credits = 10 });
            h.AnchorProvider.AvailablePlacements = 3; // enough for the first Lure + an attempt
            h.Random.EnqueueDouble(0.99, 0.99, 0.99).EnqueueNext(0, 0, 0);

            LureResult first = h.Encounter.TryLureAsync(LureKind.Basic).GetAwaiter().GetResult();
            Assert.IsTrue(first.Success);
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("already active"));
            LureResult second = h.Encounter.TryLureAsync(LureKind.Basic).GetAwaiter().GetResult();

            Assert.IsFalse(second.Success, "A second Lure mid-encounter is refused.");
            Assert.AreEqual(9, h.Store.Stored.Credits, "The refused second Lure deducted nothing extra.");
        }
    }
}
