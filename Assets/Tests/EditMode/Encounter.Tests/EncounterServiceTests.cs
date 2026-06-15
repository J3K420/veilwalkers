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
            public CaptureSystem CaptureSystem;
            public SlaySystem SlaySystem;
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
            // Story 4.4 — the Capture success-roll system shares the SAME FakeRandom as LureSystem (one
            // randomness seam per encounter, the production posture). Tests script its draws via the shared
            // `random`: a draw BELOW the chance wins, ABOVE loses. (⚠️ an empty FakeRandom queue defaults to
            // 0.0 = a guaranteed WIN, so every Capture test enqueues its draw explicitly.)
            var captureSystem = new CaptureSystem(random);
            // Story 4.5 — the Slay success-roll system shares the SAME FakeRandom and EconomyConfig (one
            // randomness seam + one config per encounter, the production posture). Tests script its draw via the
            // shared `random`; the cost reads config.SlayCost (canon 3). (⚠️ empty FakeRandom queue defaults to
            // 0.0 = a guaranteed WIN, so every Slay test enqueues its draw explicitly — the E1 trap.)
            var slaySystem = new SlaySystem(config, random);

            var encounter = new EncounterService(
                save, credit, progression, codex, mutationLock, anchorRestore, arSessionService,
                lureSystem, planeAnchor, spawner, captureSystem, slaySystem);

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
                CaptureSystem = captureSystem,
                SlaySystem = slaySystem,
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
            Assert.Throws<ArgumentNullException>(() => new EncounterService(null, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner, h.CaptureSystem, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, null, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner, h.CaptureSystem, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, null, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner, h.CaptureSystem, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, null, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner, h.CaptureSystem, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, null, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner, h.CaptureSystem, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, null, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner, h.CaptureSystem, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, null, h.LureSystem, h.PlaneAnchor, h.Spawner, h.CaptureSystem, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, null, h.PlaneAnchor, h.Spawner, h.CaptureSystem, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, null, h.Spawner, h.CaptureSystem, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, null, h.CaptureSystem, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner, null, h.SlaySystem));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService, h.LureSystem, h.PlaneAnchor, h.Spawner, h.CaptureSystem, null));
        }

        // ---- AC-2: the generic atomic multi-delta write primitive (RunActionAsync — Slay 4.5 composes it) ----
        // 4.1's representative capture proved CommitActionAsync (charge + codex in ONE save, two-slice rollback).
        // 4.4 replaced the capture representative with the REAL rolled Capture (below); these pins keep the
        // generic charge+discovery primitive (still public, composed by Slay 4.5) covered via RunActionAsync.

        // Drive a Monster into a live (Acting) encounter so RunActionAsync's precondition holds.
        private static Harness ActingHarness(SaveModel seed)
        {
            var h = LuredHarness(seed);
            Assert.IsTrue(h.Encounter.BeginAction(), "Precondition: Lured → Acting.");
            return h;
        }

        [Test]
        public void RunAction_commits_charge_and_codex_in_ONE_save_write()
        {
            var h = ActingHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 1 });
            int savesBefore = h.Store.SaveCalls;

            SpendResult result = h.Encounter
                .RunActionAsync(MonsterId, ChargeType.StrongCapture, DiscoverySource.Capture)
                .GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "A composed action with a charge available succeeds.");
            Assert.AreEqual(0, h.Progression.GetChargeCount(ChargeType.StrongCapture), "The charge was consumed.");
            Assert.IsTrue(h.Codex.IsDiscovered(MonsterId), "The codex discovery was recorded.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "Exactly ONE SaveAsync for the composed action (AR-8).");
            Assert.AreEqual(0, h.Store.Stored.StrongCaptureCharges, "...persisted the charge slice.");
            Assert.IsTrue(h.Store.Stored.Codex.ContainsKey(MonsterId), "...persisted the codex slice.");
        }

        [Test]
        public void RunAction_persist_fault_rolls_back_BOTH_slices()
        {
            var h = ActingHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 1 });
            h.Store.FailNextSave = true;

            LogAssert.ignoreFailingMessages = true;
            SpendResult result = h.Encounter
                .RunActionAsync(MonsterId, ChargeType.StrongCapture, DiscoverySource.Capture)
                .GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(result.Success, "A persist fault is a typed failure, not a faulted task.");
            Assert.AreEqual(SpendFailureReason.PersistenceFailed, result.FailureReason);
            Assert.AreEqual(1, h.Progression.GetChargeCount(ChargeType.StrongCapture), "The charge is restored.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "The codex entry was NOT leaked.");
        }

        [Test]
        public void RunAction_zero_charge_is_a_typed_failure_with_no_persist()
        {
            var h = ActingHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 0 });
            int savesBefore = h.Store.SaveCalls;

            SpendResult result = h.Encounter
                .RunActionAsync(MonsterId, ChargeType.StrongCapture, DiscoverySource.Capture)
                .GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SpendFailureReason.InsufficientCharges, result.FailureReason, "Zero charges is the 'earn via XP' block.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "No codex write on a blocked action.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "No persist on a blocked action.");
        }

        // ---- AC-1/2/3/4 (Story 4.4): Capture — free base / Strong Capture, with a success ROLL ----
        // Every Capture test enqueues its roll EXPLICITLY: FakeRandom.NextDouble() defaults to 0.0 (a WIN) on an
        // empty queue, so a forgotten enqueue would silently pass a fail-path test. Win = draw < chance; lose =
        // draw >= chance (Base 0.50, Strong 0.85 — so 0.0 always wins, 0.99 always loses both).
        private const double WinDraw = 0.0;   // below any success chance → the Monster is captured
        private const double LoseDraw = 0.99; // above both chances → a miss (free Retry)

        [Test]
        public void Base_capture_success_records_codex_and_xp_in_ONE_save_no_credits_no_charge()
        {
            // AC-1 + AC-4: a free base Capture that WINS records the Codex discovery + grants XP in exactly ONE
            // save, consuming NO Credits and NO charge.
            var h = LuredHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 2 });
            int creditsAfterLure = h.Save.Current.Credits; // 9 (Basic cost 1)
            int xpBefore = h.Save.Current.Xp;
            int savesBefore = h.Store.SaveCalls;
            h.Random.EnqueueDouble(WinDraw); // base roll wins

            CaptureResult result = h.Encounter.TryCaptureAsync(MonsterId, strong: false).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "The attempt ran cleanly.");
            Assert.IsTrue(result.Captured, "A winning base roll captures the Monster.");
            Assert.IsFalse(result.WasStrong);
            Assert.IsFalse(result.ChargeConsumed, "Base Capture consumes no charge.");
            Assert.IsTrue(h.Codex.IsDiscovered(MonsterId), "The Codex Capture discovery was recorded.");
            Assert.IsTrue(h.Codex.GetEntry(MonsterId).Captured, "The Captured flag is set.");
            Assert.AreEqual(xpBefore + h.Config.XpPerCapture, h.Save.Current.Xp, "AC-4: XP granted on a successful Capture.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "AR-8: ONE SaveAsync (codex + XP together).");
            Assert.AreEqual(creditsAfterLure, h.Save.Current.Credits, "AC-1: the Credit balance never changes.");
            Assert.AreEqual(2, h.Progression.GetChargeCount(ChargeType.StrongCapture), "No charge consumed by base Capture.");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "The encounter returns to Lured.");
        }

        [Test]
        public void Base_capture_with_zero_charges_still_succeeds_because_it_is_free()
        {
            // AC-1: base Capture is FREE — zero charges does NOT block it (the semantics flip from the 4.1
            // representative, which always consumed a charge).
            var h = LuredHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 0 });
            h.Random.EnqueueDouble(WinDraw);

            CaptureResult result = h.Encounter.TryCaptureAsync(MonsterId, strong: false).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.Captured, "A free base Capture works with zero charges.");
            Assert.IsTrue(h.Codex.IsDiscovered(MonsterId));
        }

        [Test]
        public void Base_capture_MISS_records_nothing_does_not_persist_and_keeps_the_encounter_live()
        {
            // AC-3: a base miss changes nothing (no charge, no discovery, no XP) → NO persist (save-count 0),
            // OnMonsterDiscovered fires zero times, the encounter stays live for a free Retry.
            var h = LuredHarness(new SaveModel { Credits = 10 });
            int xpBefore = h.Save.Current.Xp;
            int savesBefore = h.Store.SaveCalls;
            int discoveryEvents = 0;
            h.Codex.OnMonsterDiscovered += _ => discoveryEvents++;
            int discoveredBefore = h.Codex.DiscoveredCount;
            h.Random.EnqueueDouble(LoseDraw); // base roll misses

            CaptureResult result = h.Encounter.TryCaptureAsync(MonsterId, strong: false).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "The attempt ran (a miss is a successful RUN).");
            Assert.IsFalse(result.Captured, "A losing roll is a miss.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "No discovery on a miss.");
            Assert.AreEqual(discoveredBefore, h.Codex.DiscoveredCount, "X/67 unchanged on a miss.");
            Assert.AreEqual(0, discoveryEvents, "No discovery event on a miss.");
            Assert.AreEqual(xpBefore, h.Save.Current.Xp, "No XP on a miss.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "Nothing changed → NO persist (save-count 0).");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "AC-3: the encounter stays live for a free Retry.");
        }

        [Test]
        public void Strong_capture_success_consumes_exactly_one_charge_and_records_codex_and_xp_in_ONE_save()
        {
            // AC-2 + AC-4: a Strong Capture that WINS consumes EXACTLY one charge (never Credits), records the
            // discovery + grants XP, in ONE save.
            var h = LuredHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 1 });
            int creditsAfterLure = h.Save.Current.Credits;
            int xpBefore = h.Save.Current.Xp;
            int savesBefore = h.Store.SaveCalls;
            h.Random.EnqueueDouble(WinDraw);

            CaptureResult result = h.Encounter.TryCaptureAsync(MonsterId, strong: true).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.Captured);
            Assert.IsTrue(result.WasStrong);
            Assert.IsTrue(result.ChargeConsumed, "Strong consumes a charge.");
            Assert.AreEqual(0, result.RemainingCharges, "Exactly one charge consumed.");
            Assert.AreEqual(0, h.Store.Stored.StrongCaptureCharges, "...and it persisted.");
            Assert.IsTrue(h.Codex.IsDiscovered(MonsterId));
            Assert.AreEqual(xpBefore + h.Config.XpPerCapture, h.Save.Current.Xp, "AC-4: XP granted.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "AR-8: ONE SaveAsync for charge + codex + XP.");
            Assert.AreEqual(creditsAfterLure, h.Save.Current.Credits, "AC-2: never Credits.");
        }

        [Test]
        public void Strong_capture_uses_a_strictly_higher_chance_than_base()
        {
            // AC-2 invariant (direct, tuner-proof): the Strong success chance strictly exceeds the base chance.
            Assert.Greater(CaptureSystem.StrongCaptureChance, CaptureSystem.BaseCaptureChance,
                "Strong Capture must apply a strictly higher success probability than the free base attempt.");
        }

        [Test]
        public void The_capture_roll_is_real_consulted_from_random_not_hardcoded()
        {
            // Anti-tautology (E1): the SAME inputs with opposite draws produce opposite outcomes — proving the
            // roll is actually read from IRandom (a hard-coded success=true would capture on the LoseDraw too).
            var hWin = LuredHarness(new SaveModel { Credits = 10 });
            hWin.Random.EnqueueDouble(WinDraw);
            CaptureResult win = hWin.Encounter.TryCaptureAsync(MonsterId, strong: false).GetAwaiter().GetResult();

            var hLose = LuredHarness(new SaveModel { Credits = 10 });
            hLose.Random.EnqueueDouble(LoseDraw);
            CaptureResult lose = hLose.Encounter.TryCaptureAsync(MonsterId, strong: false).GetAwaiter().GetResult();

            Assert.IsTrue(win.Captured, "A winning draw captures.");
            Assert.IsFalse(lose.Captured, "A losing draw misses — so the outcome tracks the real roll.");
        }

        [Test]
        public void Strong_capture_with_zero_charges_is_blocked_never_negative_and_persists_nothing()
        {
            // AC-2: a Strong Capture with zero charges is blocked BEFORE any spend — typed InsufficientCharges,
            // count never goes negative, no persist, no discovery, encounter stays live.
            var h = LuredHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 0 });
            int savesBefore = h.Store.SaveCalls;
            h.Random.EnqueueDouble(WinDraw); // even a winning roll must be blocked — no charge to spend

            CaptureResult result = h.Encounter.TryCaptureAsync(MonsterId, strong: true).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "Blocked before the attempt ran.");
            Assert.AreEqual(CaptureFailureReason.InsufficientCharges, result.FailureReason, "The 'earn via XP' block.");
            Assert.AreEqual(0, h.Progression.GetChargeCount(ChargeType.StrongCapture), "Count never goes negative (stays 0).");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "No discovery on a blocked Strong Capture.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "No persist on a blocked action (save-count 0).");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "The encounter stays live.");
        }

        [Test]
        public void Strong_capture_MISS_still_consumes_the_charge_persists_once_and_records_no_discovery()
        {
            // AC-2 / Decision A (load-bearing): a Strong Capture that MISSES still consumes the charge (the
            // charge bought the odds, win or lose), persists once (the charge decrement), but records NO
            // discovery and grants NO XP, leaving the encounter live for a free Retry.
            var h = LuredHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 1 });
            int xpBefore = h.Save.Current.Xp;
            int savesBefore = h.Store.SaveCalls;
            h.Random.EnqueueDouble(LoseDraw); // Strong roll misses

            CaptureResult result = h.Encounter.TryCaptureAsync(MonsterId, strong: true).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "The attempt ran (a miss is a successful RUN).");
            Assert.IsFalse(result.Captured, "A losing Strong roll is a miss.");
            Assert.IsTrue(result.ChargeConsumed, "Decision A: the charge is consumed even on a miss.");
            Assert.AreEqual(0, h.Store.Stored.StrongCaptureCharges, "The charge decrement persisted.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "No discovery on a miss.");
            Assert.AreEqual(xpBefore, h.Save.Current.Xp, "No XP on a miss.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "ONE persist — the charge decrement (save-count 1).");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "AC-3: live for a free Retry.");
        }

        [Test]
        public void Base_capture_free_Retry_after_a_miss_succeeds_without_touching_credits_or_charges()
        {
            // AC-3: a base miss then a base retry (just call again) — the retry captures; Credits byte-identical
            // across both attempts; no charge consumed on either.
            var h = LuredHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 1 });
            int creditsAfterLure = h.Save.Current.Credits;
            h.Random.EnqueueDouble(LoseDraw, WinDraw); // miss, then win

            CaptureResult miss = h.Encounter.TryCaptureAsync(MonsterId, strong: false).GetAwaiter().GetResult();
            Assert.IsFalse(miss.Captured, "First attempt misses.");

            CaptureResult retry = h.Encounter.TryCaptureAsync(MonsterId, strong: false).GetAwaiter().GetResult();

            Assert.IsTrue(retry.Captured, "The free Retry captures.");
            Assert.AreEqual(creditsAfterLure, h.Save.Current.Credits, "Credits unchanged across both attempts (free Retry).");
            Assert.AreEqual(1, h.Progression.GetChargeCount(ChargeType.StrongCapture), "No charge consumed by base attempts.");
        }

        [Test]
        public void Capture_persist_fault_rolls_back_charge_codex_and_xp_and_raises_no_events()
        {
            // NFR-3: a Strong-win persist fault reverts EVERY slice (charge, codex, XP) and raises no discovery
            // event (events only after a committed write); a typed PersistenceFailed; encounter stays live.
            var h = LuredHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 1 });
            int xpBefore = h.Save.Current.Xp;
            int discoveryEvents = 0;
            h.Codex.OnMonsterDiscovered += _ => discoveryEvents++;
            h.Store.FailNextSave = true;
            h.Random.EnqueueDouble(WinDraw);

            LogAssert.ignoreFailingMessages = true;
            CaptureResult result = h.Encounter.TryCaptureAsync(MonsterId, strong: true).GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(result.Success, "A persist fault is a typed failure.");
            Assert.AreEqual(CaptureFailureReason.PersistenceFailed, result.FailureReason);
            Assert.AreEqual(1, h.Progression.GetChargeCount(ChargeType.StrongCapture), "The charge is restored.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "The codex entry was NOT leaked.");
            Assert.AreEqual(xpBefore, h.Save.Current.Xp, "XP rolled back.");
            Assert.AreEqual(0, discoveryEvents, "No discovery event on a faulted (uncommitted) write.");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "A failed write keeps the encounter live (free Retry).");
        }

        [Test]
        public void Capture_persist_fault_does_NOT_remove_a_pre_existing_codex_entry()
        {
            // Rollback fidelity: a Strong re-capture of an already-Captured Monster — the codex stage is a no-op
            // (flag already set), but the CHARGE + XP still mutate + persist + fault, exercising the rollback. A
            // naive Codex.Remove revert would delete the pre-existing entry → red.
            var seed = new SaveModel { Credits = 10, StrongCaptureCharges = 1 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = LuredHarness(seed);
            h.Store.FailNextSave = true;
            h.Random.EnqueueDouble(WinDraw);

            LogAssert.ignoreFailingMessages = true;
            CaptureResult result = h.Encounter.TryCaptureAsync(MonsterId, strong: true).GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(result.Success);
            Assert.IsTrue(h.Codex.IsDiscovered(MonsterId), "The pre-existing entry survives the rollback (not removed).");
            Assert.AreEqual("2026-06-01", h.Codex.GetEntry(MonsterId).Discovered, "Its first-discovered date is intact.");
            Assert.AreEqual(1, h.Progression.GetChargeCount(ChargeType.StrongCapture), "The charge is restored.");
        }

        [Test]
        public void Capture_raises_the_discovery_event_on_a_committed_first_capture()
        {
            var h = LuredHarness(new SaveModel { Credits = 10 });
            string discovered = null;
            h.Codex.OnMonsterDiscovered += id => discovered = id;
            h.Random.EnqueueDouble(WinDraw);

            h.Encounter.TryCaptureAsync(MonsterId, strong: false).GetAwaiter().GetResult();

            Assert.AreEqual(MonsterId, discovered, "A committed first capture raises OnMonsterDiscovered.");
        }

        [Test]
        public void Strong_capture_that_crosses_a_level_threshold_nets_the_consume_and_the_levelup_grant_in_ONE_save()
        {
            // Decision F': a Strong Capture both CONSUMES one Strong charge AND, by crossing a level threshold
            // via the XP grant, is GRANTED level-up charges — netted on the SAME field, in ONE save. The harness
            // rules grant 1 of each charge per level-up; thresholds are [100,200,300] and XpPerCapture is 10, so
            // seed Xp = 95 → a 10-XP capture reaches 105 → crosses level 1 → grants +1 StrongCapture. Net: a
            // seed of 1 charge, consume 1 (→0), grant 1 (→1).
            var seed = new SaveModel { Credits = 10, StrongCaptureCharges = 1, Xp = 95, Level = 0 };
            var h = LuredHarness(seed);
            int savesBefore = h.Store.SaveCalls;
            h.Random.EnqueueDouble(WinDraw);

            CaptureResult result = h.Encounter.TryCaptureAsync(MonsterId, strong: true).GetAwaiter().GetResult();

            Assert.IsTrue(result.Captured);
            Assert.AreEqual(105, h.Save.Current.Xp, "XP granted (95 + 10).");
            Assert.AreEqual(1, h.Save.Current.Level, "Crossed the first level threshold (100).");
            Assert.AreEqual(1, h.Store.Stored.StrongCaptureCharges,
                "Decision F': consumed 1 (Strong) then granted 1 (level-up) on the SAME field → net 1, persisted.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "AR-8: still ONE SaveAsync for consume + discovery + XP + grant.");
        }

        [Test]
        public void Strong_capture_crossing_a_level_threshold_raises_the_StrongCapture_event_exactly_once()
        {
            // CR patch (the duplicate-event fix): on a Strong WIN that crosses a level threshold (the consume and
            // the level-up grant both touch StrongCapture), the authoritative ProgressionService.OnChargesChanged
            // for StrongCapture must fire EXACTLY ONCE (reporting the net final value), not twice. A HUD counting
            // deltas would double-process otherwise.
            var seed = new SaveModel { Credits = 10, StrongCaptureCharges = 1, Xp = 95, Level = 0 };
            var h = LuredHarness(seed);
            int strongCaptureEvents = 0;
            int lastReported = -1;
            h.Progression.OnChargesChanged += (type, count) =>
            {
                if (type == ChargeType.StrongCapture)
                {
                    strongCaptureEvents++;
                    lastReported = count;
                }
            };
            h.Random.EnqueueDouble(WinDraw);

            h.Encounter.TryCaptureAsync(MonsterId, strong: true).GetAwaiter().GetResult();

            Assert.AreEqual(1, strongCaptureEvents, "Exactly ONE StrongCapture charges-changed event (no duplicate).");
            Assert.AreEqual(1, lastReported, "...reporting the NET final value (consumed 1, granted 1 → 1).");
        }

        [Test]
        public void Capture_out_of_a_live_encounter_is_a_typed_NotSettled_failure_that_persists_nothing()
        {
            // Settles the 4.1 out-of-sequence deferral for Capture: a Capture with no live encounter (Idle) is
            // NotSettled — NOT the misleading inherited PersistenceFailed+0 — and nothing persists.
            var h = CreateHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 1 }); // Idle (no Lure)
            int savesBefore = h.Store.SaveCalls;
            h.Random.EnqueueDouble(WinDraw);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no settled Monster"));
            CaptureResult result = h.Encounter.TryCaptureAsync(MonsterId, strong: true).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(CaptureFailureReason.NotSettled, result.FailureReason, "Out-of-sequence Capture has a distinct reason.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "Nothing persisted.");
            Assert.AreEqual(1, h.Progression.GetChargeCount(ChargeType.StrongCapture), "No charge consumed.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "State unchanged.");
        }

        [Test]
        public void Capture_invalid_or_null_monster_id_throws_a_programmer_error()
        {
            var h = LuredHarness(new SaveModel { Credits = 10, StrongCaptureCharges = 1 });
            Assert.Throws<ArgumentException>(() => h.Encounter.TryCaptureAsync("not-a-monster", strong: false).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => h.Encounter.TryCaptureAsync(null, strong: true).GetAwaiter().GetResult());
        }

        // ---- AC-1/2/3 (Story 4.5): Slay — a 3-Credit action for superior loot (more XP than Capture) ----
        // The Slay roll is enqueued EXPLICITLY per test (reusing WinDraw/LoseDraw): an empty FakeRandom queue
        // defaults to 0.0 (a WIN), so a forgotten enqueue would silently pass a miss-path test (the E1 trap).
        // Win = draw < SlaySuccessChance (0.65); lose = draw >= it — so 0.0 always wins, 0.99 always loses.
        // NOTE: LuredHarness does a Basic Lure (cost 1) first, so the post-Lure balance is (seed - 1); every
        // Slay balance assertion reads h.Save.Current.Credits AFTER LuredHarness as the baseline.

        [Test]
        public void Slay_success_deducts_three_credits_records_Slain_and_grants_xp_in_ONE_save()
        {
            // AC-1 + AC-2: a Slay that WINS deducts exactly 3 Credits, records the Slain discovery, and grants
            // XpPerSlay — all in EXACTLY ONE save (AR-8 — the load-bearing "no free-Slay via two persists").
            var h = LuredHarness(new SaveModel { Credits = 10 });
            int creditsAfterLure = h.Save.Current.Credits; // 9 (Basic cost 1)
            int xpBefore = h.Save.Current.Xp;
            int savesBefore = h.Store.SaveCalls;
            h.Random.EnqueueDouble(WinDraw); // Slay roll wins

            SlayResult result = h.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "The attempt ran cleanly.");
            Assert.IsTrue(result.Slain, "A winning roll slays the Monster.");
            Assert.AreEqual(h.Config.SlayCost, result.Cost, "AC-1: the cost (3) is carried on the result.");
            Assert.AreEqual(creditsAfterLure - h.Config.SlayCost, result.NewBalance, "NewBalance is post-spend.");
            Assert.AreEqual(creditsAfterLure - h.Config.SlayCost, h.Save.Current.Credits, "AC-1: exactly 3 Credits deducted.");
            Assert.IsTrue(h.Codex.IsDiscovered(MonsterId), "The Codex Slay discovery was recorded.");
            Assert.IsTrue(h.Codex.GetEntry(MonsterId).Slain, "The Slain flag is set (NOT Captured).");
            Assert.IsFalse(h.Codex.GetEntry(MonsterId).Captured, "Slay sets Slain, not Captured.");
            Assert.AreEqual(xpBefore + h.Config.XpPerSlay, h.Save.Current.Xp, "AC-2: XpPerSlay granted on a successful Slay.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "AR-8: ONE SaveAsync (credits + codex + XP together).");
            Assert.AreEqual(creditsAfterLure - h.Config.SlayCost, h.Store.Stored.Credits, "...the credit deduction persisted.");
            Assert.IsTrue(h.Store.Stored.Codex.ContainsKey(MonsterId), "...the codex slice persisted.");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "The encounter returns to Lured.");
        }

        [Test]
        public void Slay_reward_is_strictly_superior_to_Capture_more_xp()
        {
            // AC-2: Slay's reward is strictly superior to Capture — its XP grant (XpPerSlay 25) strictly exceeds
            // Capture's (XpPerCapture 10). The direct inequality (tuner-proof) AND a behavioral pin (the same
            // Monster slain vs captured yields a strictly larger Xp delta).
            Assert.Greater(25, 10, "Sanity: the canon values are 25 > 10 (guards a degenerate config).");

            // Direct invariant on the live config (the real enforcement — OnValidate only warns in-editor).
            var hConfig = CreateHarness(new SaveModel());
            Assert.Greater(hConfig.Config.XpPerSlay, hConfig.Config.XpPerCapture,
                "AC-2 / FR-9: XpPerSlay must strictly exceed XpPerCapture.");

            // Behavioral: capture mon01 in one encounter, slay mon01 in another; the Slay Xp delta is larger.
            var hCap = LuredHarness(new SaveModel { Credits = 10 });
            int capXpBefore = hCap.Save.Current.Xp;
            hCap.Random.EnqueueDouble(WinDraw);
            hCap.Encounter.TryCaptureAsync(MonsterId, strong: false).GetAwaiter().GetResult();
            int captureXpDelta = hCap.Save.Current.Xp - capXpBefore;

            var hSlay = LuredHarness(new SaveModel { Credits = 10 });
            int slayXpBefore = hSlay.Save.Current.Xp;
            hSlay.Random.EnqueueDouble(WinDraw);
            hSlay.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();
            int slayXpDelta = hSlay.Save.Current.Xp - slayXpBefore;

            Assert.Greater(slayXpDelta, captureXpDelta, "AC-2: slaying the same Monster grants strictly more XP than capturing it.");
        }

        [Test]
        public void Slay_cost_is_carried_on_every_result_shape()
        {
            // AC-1 "cost shown before spend": Cost (3) is on the success, the miss, AND the insufficient-credits
            // failure — the HUD must always be able to render "Slay − 3".
            int cost = SeededConfigSlayCost();

            var hWin = LuredHarness(new SaveModel { Credits = 10 });
            hWin.Random.EnqueueDouble(WinDraw);
            SlayResult win = hWin.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();
            Assert.AreEqual(cost, win.Cost, "Cost carried on a success.");

            var hMiss = LuredHarness(new SaveModel { Credits = 10 });
            hMiss.Random.EnqueueDouble(LoseDraw);
            SlayResult miss = hMiss.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();
            Assert.AreEqual(cost, miss.Cost, "Cost carried on a miss.");

            // Insufficient: seed below the cost (post-Lure must be < 3). Lure costs 1, so seed 3 → 2 after Lure.
            var hBlock = LuredHarness(new SaveModel { Credits = 3 });
            Assert.Less(hBlock.Save.Current.Credits, cost, "Precondition: post-Lure balance is below the Slay cost.");
            hBlock.Random.EnqueueDouble(WinDraw);
            LogAssert.ignoreFailingMessages = true;
            SlayResult block = hBlock.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(SlayFailureReason.InsufficientCredits, block.FailureReason);
            Assert.AreEqual(cost, block.Cost, "Cost carried on an insufficient-credits failure.");
        }

        [Test]
        public void Slay_MISS_records_nothing_loses_no_credits_does_not_persist_and_keeps_the_encounter_live()
        {
            // AC-3 (load-bearing): a missed Slay loses NO Credits, records NO discovery, grants NO XP, does NOT
            // persist (save-count 0), and leaves the encounter live for a free Retry.
            var h = LuredHarness(new SaveModel { Credits = 10 });
            int creditsAfterLure = h.Save.Current.Credits;
            int xpBefore = h.Save.Current.Xp;
            int savesBefore = h.Store.SaveCalls;
            int discoveryEvents = 0;
            h.Codex.OnMonsterDiscovered += _ => discoveryEvents++;
            int discoveredBefore = h.Codex.DiscoveredCount;
            h.Random.EnqueueDouble(LoseDraw); // Slay roll misses

            SlayResult result = h.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "The attempt ran (a miss is a successful RUN).");
            Assert.IsFalse(result.Slain, "A losing roll is a miss.");
            Assert.AreEqual(creditsAfterLure, result.NewBalance, "NewBalance is the UNCHANGED balance on a miss.");
            Assert.AreEqual(creditsAfterLure, h.Save.Current.Credits, "AC-3: NO Credits lost on a failed Slay.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "No discovery on a miss.");
            Assert.AreEqual(discoveredBefore, h.Codex.DiscoveredCount, "X/67 unchanged on a miss.");
            Assert.AreEqual(0, discoveryEvents, "No discovery event on a miss.");
            Assert.AreEqual(xpBefore, h.Save.Current.Xp, "No XP on a miss.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "Nothing changed → NO persist (save-count 0).");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "AC-3: the encounter stays live for a free Retry.");
        }

        [Test]
        public void Slay_miss_then_retry_success_charges_only_for_the_success()
        {
            // AC-3 free Retry: a miss costs 0 Credits; the retry success costs exactly 3. The spend is
            // per-SUCCESS, not per-attempt — a Retry never double-charges for the prior miss.
            var h = LuredHarness(new SaveModel { Credits = 10 });
            int creditsAfterLure = h.Save.Current.Credits; // 9
            h.Random.EnqueueDouble(LoseDraw, WinDraw); // [miss, then win] — both Slay rolls draw from NextDouble

            SlayResult miss = h.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();
            Assert.IsTrue(miss.Success);
            Assert.IsFalse(miss.Slain, "First attempt misses.");
            Assert.AreEqual(creditsAfterLure, h.Save.Current.Credits, "The miss cost 0 Credits.");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "Still live for the Retry.");

            SlayResult retry = h.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();
            Assert.IsTrue(retry.Slain, "The retry succeeds.");
            Assert.AreEqual(creditsAfterLure - h.Config.SlayCost, h.Save.Current.Credits,
                "The success cost exactly 3 — the Retry did not double-charge for the prior miss.");
        }

        [Test]
        public void Slay_winning_roll_with_insufficient_credits_is_blocked_raises_the_event_and_persists_nothing()
        {
            // AC-1: a Slay whose roll WOULD have won but the player can't afford it is blocked BEFORE any
            // deduction — typed InsufficientCredits, no persist, no discovery, OnInsufficientCredits raised once,
            // the encounter stays live for a Retry after top-up.
            var h = LuredHarness(new SaveModel { Credits = 3 }); // 2 after the Basic Lure — below the Slay cost 3
            int creditsAfterLure = h.Save.Current.Credits; // 2
            Assert.Less(creditsAfterLure, h.Config.SlayCost, "Precondition: below the Slay cost.");
            int savesBefore = h.Store.SaveCalls;
            int insufficientEvents = 0;
            InsufficientCreditsEvent captured = default;
            h.Encounter.OnInsufficientCredits += e => { insufficientEvents++; captured = e; };
            h.Random.EnqueueDouble(WinDraw); // even a winning roll is blocked — can't afford the spend

            LogAssert.ignoreFailingMessages = true;
            SlayResult result = h.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(result.Success, "Blocked before the attempt committed.");
            Assert.AreEqual(SlayFailureReason.InsufficientCredits, result.FailureReason);
            Assert.AreEqual(creditsAfterLure, h.Save.Current.Credits, "No Credits deducted on a block.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "No discovery on a block.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "No persist on a block (save-count 0).");
            Assert.AreEqual(1, insufficientEvents, "AR-11: OnInsufficientCredits raised exactly once.");
            Assert.AreEqual(h.Config.SlayCost, captured.Cost, "The event carries the Slay cost.");
            Assert.AreEqual(creditsAfterLure, captured.Balance, "The event carries the unchanged balance.");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "The encounter stays live for a Retry after top-up.");
        }

        [Test]
        public void Slay_exact_balance_succeeds_to_zero()
        {
            // AC-1 boundary: a balance exactly equal to the Slay cost succeeds to zero (the >= rule). Seed
            // SlayCost + 1 so the Basic Lure (cost 1) leaves exactly SlayCost.
            var h = LuredHarness(new SaveModel { Credits = SeededConfigSlayCost() + 1 });
            Assert.AreEqual(h.Config.SlayCost, h.Save.Current.Credits, "Precondition: post-Lure balance == Slay cost.");
            h.Random.EnqueueDouble(WinDraw);

            SlayResult result = h.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(result.Slain, "Exact-balance Slay succeeds (the >= rule).");
            Assert.AreEqual(0, h.Save.Current.Credits, "Spent to zero.");
        }

        [Test]
        public void Slay_persist_fault_rolls_back_all_three_slices()
        {
            // NFR-3: a persist fault on a successful Slay rolls back the Credit deduction AND the codex discovery
            // AND the XP grant; a typed PersistenceFailed; OnMonsterDiscovered fired zero times.
            var h = LuredHarness(new SaveModel { Credits = 10 });
            int creditsAfterLure = h.Save.Current.Credits;
            int xpBefore = h.Save.Current.Xp;
            int discoveryEvents = 0;
            h.Codex.OnMonsterDiscovered += _ => discoveryEvents++;
            h.Store.FailNextSave = true;
            h.Random.EnqueueDouble(WinDraw);

            LogAssert.ignoreFailingMessages = true;
            SlayResult result = h.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(result.Success, "A persist fault is a typed failure, not a faulted task.");
            Assert.AreEqual(SlayFailureReason.PersistenceFailed, result.FailureReason);
            Assert.AreEqual(creditsAfterLure, h.Save.Current.Credits, "The Credit deduction is rolled back.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "The codex entry was NOT leaked.");
            Assert.AreEqual(xpBefore, h.Save.Current.Xp, "The XP grant is rolled back.");
            Assert.AreEqual(0, discoveryEvents, "No discovery event on a rolled-back write.");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "A persist fault still returns to a live state.");
        }

        [Test]
        public void Slay_out_of_a_live_encounter_is_a_typed_NotSettled_failure_that_persists_nothing()
        {
            // Settles the 4.1 out-of-sequence deferral for Slay: a Slay with no live encounter (Idle) is
            // NotSettled — NOT the misleading inherited PersistenceFailed+0 — and nothing persists.
            var h = CreateHarness(new SaveModel { Credits = 10 }); // Idle (no Lure)
            int savesBefore = h.Store.SaveCalls;
            h.Random.EnqueueDouble(WinDraw);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no settled Monster"));
            SlayResult result = h.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SlayFailureReason.NotSettled, result.FailureReason, "Out-of-sequence Slay has a distinct reason.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "Nothing persisted.");
            Assert.AreEqual(10, h.Save.Current.Credits, "No Credits deducted.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "State unchanged.");
        }

        [Test]
        public void The_slay_roll_is_real_consulted_from_random_not_hardcoded()
        {
            // Anti-tautology (E1): the SAME inputs with opposite draws produce opposite outcomes — proving the
            // roll is actually read from IRandom (a hard-coded slain=true would slay on the LoseDraw too).
            var hWin = LuredHarness(new SaveModel { Credits = 10 });
            hWin.Random.EnqueueDouble(WinDraw);
            SlayResult win = hWin.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();

            var hLose = LuredHarness(new SaveModel { Credits = 10 });
            hLose.Random.EnqueueDouble(LoseDraw);
            SlayResult lose = hLose.Encounter.TrySlayAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(win.Slain, "A winning draw slays.");
            Assert.IsFalse(lose.Slain, "A losing draw misses — so the outcome tracks the real roll.");
        }

        [Test]
        public void Slay_invalid_or_null_monster_id_throws_a_programmer_error()
        {
            var h = LuredHarness(new SaveModel { Credits = 10 });
            Assert.Throws<ArgumentException>(() => h.Encounter.TrySlayAsync("not-a-monster").GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => h.Encounter.TrySlayAsync(null).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => h.Encounter.TrySlayAsync("").GetAwaiter().GetResult());
        }

        // The SlayCost on the default canon EconomyConfig the harness uses (3) — read once for the cost pins.
        private static int SeededConfigSlayCost()
        {
            var config = ScriptableObject.CreateInstance<EconomyConfig>();
            return config.SlayCost;
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

        // ---- FR-7 (Story 4.3): Scan — a FREE action; reveals + records partial codex; never spends ----

        /// <summary>Drive a real Lure so the encounter is Lured (a settled Monster to scan), granting one
        /// placement + a common roll. Returns the harness with the encounter in Lured.</summary>
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

        [Test]
        public void Scan_records_in_ONE_save_and_returns_to_Lured()
        {
            // AC-1: a Scan on a settled, ALREADY-DISCOVERED Monster records the persistent Scanned flag in
            // exactly ONE persist and returns the encounter to Lured. Seed the monster as discovered (a key
            // exists) so the persistent flag flips (the discovered-only path, Decision B1).
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = LuredHarness(seed);
            int savesBefore = h.Store.SaveCalls;

            ScanResult result = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "A Scan on a settled Monster succeeds.");
            Assert.AreEqual(MonsterId, result.MonsterId);
            Assert.IsFalse(result.AlreadyScanned, "First scan of this Monster.");
            Assert.IsTrue(h.Codex.GetEntry(MonsterId).Scanned, "The persistent Scanned flag flipped (discovered Monster).");
            Assert.IsTrue(h.Store.Stored.Codex[MonsterId].Scanned, "...and it persisted.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "AR-8: exactly ONE SaveAsync for the Scan.");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "The Scan returns the encounter to Lured.");
        }

        [Test]
        public void Scan_records_partial_progress_even_before_discovery_without_inflating_the_count()
        {
            // AC-2 (THE load-bearing pin): a Scan on a NOT-yet-discovered Monster records partial progress
            // (the encounter scan-state) WITHOUT creating a Codex key / incrementing X/67 and WITHOUT raising
            // OnMonsterDiscovered. Scan ≠ discover (Decision B1).
            var h = LuredHarness(new SaveModel { Credits = 10 }); // mon01 is NOT discovered (no codex key)
            int discoveredCountBefore = h.Codex.DiscoveredCount;
            int discoveryEvents = 0;
            h.Codex.OnMonsterDiscovered += _ => discoveryEvents++;

            ScanResult result = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "Scanning an undiscovered Monster still succeeds (partial progress).");
            Assert.AreEqual(discoveredCountBefore, h.Codex.DiscoveredCount, "X/67 is NOT inflated by a scan-only Monster.");
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "A Scan does NOT discover (no Codex key created).");
            Assert.AreEqual(0, discoveryEvents, "A Scan raises NO discovery event (scan ≠ discover).");
        }

        [Test]
        public void Scan_never_changes_the_credit_balance_or_charges()
        {
            // AC-3 (THE free-action pin): no Scan path touches Credits or any charge.
            var seed = new SaveModel { Credits = 7, StrongCaptureCharges = 2, StabilityBoostCharges = 1 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = LuredHarness(seed);
            int creditsBefore = h.Store.Stored.Credits;

            ScanResult result = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(creditsBefore, h.Store.Stored.Credits, "Scan is FREE — the persisted balance is unchanged.");
            Assert.AreEqual(creditsBefore, h.Save.Current.Credits, "...and the in-memory balance is unchanged.");
            Assert.AreEqual(2, h.Progression.GetChargeCount(ChargeType.StrongCapture), "No charge consumed.");
            Assert.AreEqual(1, h.Progression.GetChargeCount(ChargeType.StabilityBoost), "No charge consumed.");
        }

        [Test]
        public void Idempotent_rescan_records_nothing_new_and_does_not_persist_again()
        {
            // AC-2: scanning the SAME Monster again THIS encounter is a pure no-op — no second persist,
            // AlreadyScanned == true, Credits untouched.
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = LuredHarness(seed);

            ScanResult first = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();
            Assert.IsTrue(first.Success);
            Assert.IsFalse(first.AlreadyScanned);
            int savesAfterFirst = h.Store.SaveCalls;

            ScanResult second = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(second.Success, "A re-scan is still a valid (free) success.");
            Assert.IsTrue(second.AlreadyScanned, "...flagged as already scanned this encounter.");
            Assert.AreEqual(savesAfterFirst, h.Store.SaveCalls, "No second persist for an idempotent re-scan (save-count unchanged).");
        }

        [Test]
        public void Scan_persist_fault_rolls_back_both_slices_and_keeps_the_encounter_live()
        {
            // NFR-3: a persist fault reverts BOTH the encounter scan-state AND the persistent flag; a typed
            // PersistenceFailed is returned; the encounter stays live (Lured) for a free retry; Credits unchanged.
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = LuredHarness(seed);
            int creditsAfterLure = h.Save.Current.Credits; // 9 (Basic cost 1) — the Scan must not change this
            h.Store.FailNextSave = true;

            LogAssert.ignoreFailingMessages = true; // SaveService + EncounterService both log on the fault
            ScanResult result = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(result.Success, "A persist fault is a typed failure, not a faulted task.");
            Assert.AreEqual(ScanFailureReason.PersistenceFailed, result.FailureReason);
            Assert.IsFalse(h.Codex.GetEntry(MonsterId).Scanned, "The Scanned flag rolled back (not leaked).");
            Assert.IsFalse(h.Store.Stored.Codex[MonsterId].Scanned, "...and nothing persisted the flag.");
            Assert.AreEqual(creditsAfterLure, h.Save.Current.Credits, "Credits untouched by a Scan (free action) regardless of the fault.");
            Assert.AreEqual(EncounterState.Lured, h.Encounter.State, "A failed Scan keeps the encounter live (free Retry).");

            // And the encounter scan-state reverted: a subsequent successful scan is NOT 'already scanned'.
            ScanResult retry = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();
            Assert.IsTrue(retry.Success);
            Assert.IsFalse(retry.AlreadyScanned, "The rolled-back scan did not leave stale encounter scan-state.");
        }

        [Test]
        public void Scan_out_of_a_live_encounter_is_a_typed_NotSettled_failure_that_persists_nothing()
        {
            // Decision E: a Scan when NOT in a live encounter (Idle) is NotSettled — NOT the misleading
            // inherited PersistenceFailed — and nothing persists.
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Discovered = "2026-06-01" };
            var h = CreateHarness(seed); // Idle (no Lure)
            int savesBefore = h.Store.SaveCalls;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no settled Monster"));
            ScanResult result = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ScanFailureReason.NotSettled, result.FailureReason, "Out-of-sequence Scan has a distinct reason.");
            Assert.AreEqual(savesBefore, h.Store.SaveCalls, "Nothing persisted.");
            Assert.AreEqual(EncounterState.Idle, h.Encounter.State, "State unchanged.");
        }

        [Test]
        public void Scan_invalid_or_null_monster_id_throws_a_programmer_error()
        {
            var h = LuredHarness(new SaveModel { Credits = 10 });
            Assert.Throws<ArgumentException>(() => h.Encounter.TryScanAsync("not-a-monster").GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => h.Encounter.TryScanAsync(null).GetAwaiter().GetResult());
        }

        [Test]
        public void Rescan_after_a_mid_encounter_discovery_persists_the_flag_and_is_not_already_scanned()
        {
            // CR patch: a Monster scanned while UNDISCOVERED (encounter-state only, persistent flag a no-op),
            // then discovered mid-encounter (a Capture), then re-scanned — the re-scan flips the persistent
            // flag for the FIRST time, so it DID record new durable data: it must persist (one SaveAsync) and
            // report AlreadyScanned == FALSE (it is NOT a pure no-op), despite being in the encounter scan-set.
            var seed = new SaveModel { Credits = 10, StrongCaptureCharges = 1 };
            var h = LuredHarness(seed);

            // (1) Scan while undiscovered — records encounter-state progress only (no persistent flag / key).
            ScanResult firstScan = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();
            Assert.IsTrue(firstScan.Success);
            Assert.IsFalse(h.Codex.IsDiscovered(MonsterId), "Scan did not discover.");

            // (2) Discover the Monster mid-encounter via a Capture (Lured → Acting → composed write → Lured).
            // TryCaptureAsync drives BeginAction itself; enqueue a winning roll so the base Capture succeeds.
            h.Random.EnqueueDouble(WinDraw);
            CaptureResult capture = h.Encounter.TryCaptureAsync(MonsterId, strong: false).GetAwaiter().GetResult();
            Assert.IsTrue(capture.Captured, "The Capture succeeded.");
            Assert.IsTrue(h.Codex.IsDiscovered(MonsterId), "The Capture discovered the Monster.");
            Assert.IsFalse(h.Codex.GetEntry(MonsterId).Scanned, "But the persistent Scanned flag is not yet set.");
            int savesBeforeRescan = h.Store.SaveCalls;

            // (3) Re-scan — now flips the persistent Scanned flag for the first time → persists, NOT already-scanned.
            ScanResult rescan = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(rescan.Success);
            Assert.IsFalse(rescan.AlreadyScanned, "A re-scan that flips the persistent flag recorded NEW data — not 'already scanned'.");
            Assert.IsTrue(h.Codex.GetEntry(MonsterId).Scanned, "The persistent flag is now set.");
            Assert.IsTrue(h.Store.Stored.Codex[MonsterId].Scanned, "...and it persisted.");
            Assert.AreEqual(savesBeforeRescan + 1, h.Store.SaveCalls, "The flag-flipping re-scan persisted once (AR-8).");
        }

        [Test]
        public void Scan_of_a_monster_scanned_in_a_prior_encounter_persists_once_and_is_not_already_scanned()
        {
            // Spec-sanctioned (AC-2): a Monster discovered + scanned in a PRIOR encounter (persistent flag
            // already true) but NOT yet scanned THIS encounter records the encounter-state on its first scan
            // this encounter — a new encounter-state entry — so it persists once and is NOT 'already scanned'.
            var seed = new SaveModel { Credits = 10 };
            seed.Codex[MonsterId] = new CodexEntryData { Captured = true, Scanned = true, Discovered = "2026-06-01" };
            var h = LuredHarness(seed);
            int savesBefore = h.Store.SaveCalls;

            ScanResult result = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.IsFalse(result.AlreadyScanned, "First scan THIS encounter records new encounter-state — not 'already scanned'.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "It persists once (the encounter-state changed).");

            // The SECOND scan this encounter IS the pure no-op.
            ScanResult second = h.Encounter.TryScanAsync(MonsterId).GetAwaiter().GetResult();
            Assert.IsTrue(second.AlreadyScanned, "Now it is already scanned this encounter — a pure no-op.");
            Assert.AreEqual(savesBefore + 1, h.Store.SaveCalls, "No second persist.");
        }
    }
}
