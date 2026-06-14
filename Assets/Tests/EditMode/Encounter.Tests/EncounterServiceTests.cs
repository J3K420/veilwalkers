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
            public EncounterService Encounter;
        }

        private static Harness CreateHarness(SaveModel seed)
        {
            var store = new FakeEncounterProgressStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();

            var mutationLock = new SaveMutationLock();
            var credit = new CreditService(save, mutationLock);
            // A minimal progression rules: thresholds don't matter for 4.1's composed-write tests.
            var rules = new ProgressionRules(new[] { 100, 200, 300 }, 1, 1, 1);
            var progression = new ProgressionService(save, rules, mutationLock);

            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            var codex = new CodexService(save, db, new FakeClock(FixedNow));

            var anchorProvider = new FakeArAnchorProvider();
            var anchorRestore = new AnchorRestoreService(anchorProvider);

            var arSession = new FakeArSession();
            var permission = new FakeCameraPermission { HasCameraPermission = true };
            var permissionFlow = new CameraPermissionFlow(permission);
            var arSessionService = new ArSessionService(arSession, permissionFlow);

            var encounter = new EncounterService(
                save, credit, progression, codex, mutationLock, anchorRestore, arSessionService);

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
            Assert.Throws<ArgumentNullException>(() => new EncounterService(null, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, null, h.Progression, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, null, h.Codex, h.Lock, h.AnchorRestore, h.ArSessionService));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, null, h.Lock, h.AnchorRestore, h.ArSessionService));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, null, h.AnchorRestore, h.ArSessionService));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, null, h.ArSessionService));
            Assert.Throws<ArgumentNullException>(() => new EncounterService(h.Save, h.Credit, h.Progression, h.Codex, h.Lock, h.AnchorRestore, null));
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
    }
}
