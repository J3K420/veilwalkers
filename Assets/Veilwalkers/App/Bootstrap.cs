using System.Threading.Tasks;
using UnityEngine;
using Veilwalkers.AR;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Persistence;

namespace Veilwalkers.App
{
    /// <summary>
    /// The single composition entry point. Bootstrap is the ONLY place that wires
    /// <see cref="GameServices"/>: it registers each service instance and then calls
    /// <see cref="GameServices.MarkReady"/> to seal the table, after which the rest
    /// of the app may read services.
    /// <para>
    /// Lives in the Bootstrap entry scene (scene 0 in Build Settings) and runs at
    /// <c>[DefaultExecutionOrder(-1000)]</c> so no other <c>Awake</c> can race the
    /// composition root. Bootstrap does NOT own the AR session, and it does NOT own
    /// scene flow (the AppStateMachine lands in Story 6.3); it only assembles
    /// services and kicks the initial save load.
    /// </para>
    /// <para>
    /// Mid-wiring failure handling (chosen approach): all service instances are
    /// CONSTRUCTED before the first <c>Register</c> call — constructors are where
    /// realistic failures live, and a constructor throw therefore aborts before the
    /// locator is touched, leaving it clean. <c>Register</c>/<c>MarkReady</c> throw
    /// only on programmer error (null/duplicate/sealed), so a partially-registered
    /// locator is not a reachable runtime state; a wiring failure is treated as
    /// fatal at boot (log + rethrow). A retry surface, if ever wanted, arrives with
    /// scene flow in Story 6.3. Reset-and-rethrow was rejected because no locator
    /// reset exists in player builds (by design — wire-once).
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class Bootstrap : MonoBehaviour
    {
        /// <summary>
        /// The tuned economy values asset (Story 1.6). Assigned on the Bootstrap
        /// component in the scene-0 entry scene; its numbers drive the
        /// <see cref="ProgressionRules"/> built below. Required — wiring fails fast at
        /// boot if it is unassigned (see <see cref="WireServices"/>).
        /// </summary>
        [SerializeField] private EconomyConfig _economyConfig;

        private void Awake()
        {
            WireServices();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Returns <see cref="GameServices"/> statics to the unready state at the
        /// start of every Editor play session. Today Enter Play Mode Options are OFF
        /// (full domain reload — this is then a harmless no-op on fresh statics), but
        /// <c>EditorSettings.asset</c> already stores the fast-enter option flags, so
        /// this future-proofs anyone later enabling them. Editor-only: a player
        /// process always starts with fresh statics.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForEnterPlayMode()
        {
            GameServices.ResetForFastEnterPlayMode();
        }
#endif

        /// <summary>
        /// Register every service instance, then seal the table and kick the initial
        /// save load. Service registrations are added here as later stories introduce
        /// them.
        /// </summary>
        private void WireServices()
        {
            // Guard against re-entry (e.g. a duplicate Bootstrap in a loaded scene):
            // the wiring runs exactly once for the lifetime of the process.
            if (GameServices.IsReady)
            {
                GameLog.Warn("Bootstrap.WireServices skipped: GameServices is already wired.");
                return;
            }

            // Hoisted to method scope so the post-load continuation (below) can hand the
            // shared lock to FirstLaunchGrant. The lock is not a registered service, so it
            // is captured rather than resolved via GameServices.Get<>().
            SaveMutationLock economyMutationLock = null;

            try
            {
                // Construct first (see class doc: failures here leave the locator
                // untouched). persistentDataPath is main-thread-only, which is why it
                // is read HERE and passed into the store rather than inside it.
                var clock = new SystemClock();
                var progressStore = new LocalProgressStore(Application.persistentDataPath);
                var saveService = new SaveService(progressStore);

                // One mutation lock shared by EVERY Economy mutator of the save model:
                // independent locks would let a credit spend and a charge consume
                // interleave and durably persist each other's uncommitted deltas
                // (see SaveMutationLock). Construct once, inject into both services.
                economyMutationLock = new SaveMutationLock();
                var creditService = new CreditService(saveService, economyMutationLock);

                // Progression rules come from the tuned EconomyConfig asset (Story 1.6):
                // economy numbers live as serialized data on the asset, never as
                // constants in service logic, so AR-16 is honored by the injection seam.
                // The asset is required — a missing one is a fatal boot misconfiguration
                // (never silently fall back to a literal: that would reintroduce a
                // hard-coded number). The EconomyConfig values are OQ-9-provisional.
                if (_economyConfig == null)
                {
                    throw new System.InvalidOperationException(
                        "Bootstrap: EconomyConfig is not assigned. Assign the EconomyConfig " +
                        "asset to the Bootstrap component in the scene-0 entry scene.");
                }

                var progressionRules = _economyConfig.BuildProgressionRules();
                var progressionService = new ProgressionService(
                    saveService, progressionRules, economyMutationLock);

                // The daily-reward claim (Story 1.8): a sibling Economy mutator on the
                // SAME shared lock, and the FIRST production consumer of the IClock seam
                // by constructor injection (the calendar-day rule is fakeable in tests).
                // The reward amount is read from the tuned EconomyConfig (AR-16); a
                // non-positive value throws here, making a misconfiguration a fatal boot
                // error rather than a silent zero grant.
                var dailyRewardService = new DailyRewardService(
                    saveService, economyMutationLock, clock, _economyConfig);

                // Local telemetry seam (Story 1.9, AR-15): a SEPARATE telemetry.json
                // (capped ring, plaintext — a tester-inspectable diagnostic, distinct from
                // the encrypted save) behind the swappable ITelemetrySink/ITelemetryStore.
                // The retention cap is read from EconomyConfig HERE and injected as an int —
                // the store does not reference Economy.
                var telemetryStore = new LocalTelemetryStore(
                    Application.persistentDataPath, clock, _economyConfig.TelemetryRetentionDays);
                var telemetrySink = new LocalTelemetrySink(telemetryStore, clock);

                // OQ-4 ad-reward seam (Story 1.9, AR-17): GrantRewardAsync grants one
                // low-tier charge via Progression (never Credits/XP), capped per UTC day.
                // Constructed-but-unregistered: no caller exists yet (ads are not built),
                // but constructing it proves the wiring and fails fast on a bad AdDailyCap.
                var adHook = new AdHook(
                    progressionService, saveService, economyMutationLock, clock,
                    _economyConfig.AdDailyCap);

                // Write-once first-zero-credit recorder (Story 1.9, AR-15): the one
                // telemetry scalar that lives in SaveModel. Constructed-but-unregistered —
                // the call site (a spend to zero) is later-epic work.
                var firstZeroCreditRecorder = new FirstZeroCreditRecorder(
                    saveService, economyMutationLock, clock);

                // Camera-permission flow (Story 3.1, Veilwalkers.AR) — the FIRST AR-area service.
                // The disclose→prompt→denied→re-grant decision (FR-5) behind the ICameraPermission
                // platform seam. Registered LIVE (unlike CodexService below): it has no
                // unauthored-asset blocker — AndroidCameraPermission is the platform adapter
                // (real on Android, a logged no-op off-device), so construction never throws at
                // boot. The CONSUMER, CameraPermissionView (Veilwalkers.UI), resolves this via
                // GameServices.Get and degrades gracefully while unplaced; placing that view in a
                // scene + routing AR entry from it is Epic 6 (Story 6.3/6.4). The flow's logic is
                // fully proven headless by AR.Tests. This story stops at "permission resolved →
                // may proceed / denied → re-grant"; it does NOT own AR entry (the safety gate is
                // Story 3.2, the session lifecycle Story 3.3).
                var cameraPermissionFlow = new CameraPermissionFlow(new AndroidCameraPermission());

                // AR Safety Warning gate (Story 3.2, Veilwalkers.AR) — the FR-3 mandatory warning.
                // The full-read-first-per-session / fast-card-thereafter / skip-on-Shop-resume
                // decision + the MayInteract block predicate (AC-1) behind a pure-logic gate.
                // Registered LIVE (like CameraPermissionFlow above): it has NO constructor dependency
                // (pure session-scoped state — no clock, no save, no seam, no persistence per the
                // story's decision #4), so construction never throws at boot. The CONSUMER,
                // ArSafetyView (Veilwalkers.UI), resolves this via GameServices.Get and degrades
                // gracefully while unplaced; placing that view + supplying the ArEntryKind from
                // AR-entry routing is Epic 6 (Story 6.3). The camera/Lure/Encounter surfaces that
                // READ MayInteract (so AC-1's "not interactive until acknowledged" is enforced) live
                // in Epic 4/6. The gate's logic is fully proven headless by AR.Tests. This story
                // stops at "warning acknowledged → may proceed"; it does NOT start the AR session
                // (Story 3.3). It is intentionally INDEPENDENT of cameraPermissionFlow — the order
                // "permission → safety gate → session" is the caller's sequencing (3.3/6.3), not a
                // gate-to-flow dependency.
                var arSafetyGate = new ArSafetyGate();

                // CodexService (Story 2.3, Veilwalkers.Monsters) — registration SEAM, not
                // wired this story. It is the read model + atomic discovery-record seam over
                // SaveModel.Codex; it owns a PRIVATE SemaphoreSlim, so it takes NO lock arg
                // (it must NOT inject the Economy SaveMutationLock — that would be an illegal
                // Monsters → Economy edge). Wiring is deferred because its second collaborator,
                // a MonsterDatabase instance, has no authored .asset yet (the Story 2.2 [~]
                // deferral): adding a null [SerializeField] MonsterDatabase and constructing
                // here would throw ArgumentNullException inside this try and be fatal at boot.
                // To close the seam once MonsterDatabase.asset exists: add
                // `using Veilwalkers.Monsters;` + `[SerializeField] private MonsterDatabase
                // _monsterDatabase;` (null-checked like _economyConfig), then
                // `new CodexService(saveService, _monsterDatabase, clock)` (the IClock — already
                // in scope, registered below at GameServices.Register<IClock> — was added in
                // Story 2.5 to stamp the first-discovered date on the Codex entry) and
                // `GameServices.Register<CodexService>(...)`. CodexService's logic is fully
                // covered by Monsters.Tests, so leaving it unregistered blocks nothing.
                // Story 2.4 added the first CONSUMER: CodexGridView (Veilwalkers.UI) resolves
                // CodexService via GameServices.Get and degrades gracefully (inert grid) while
                // it stays unregistered — so the view exists-but-inert until this seam closes.
                // Story 2.5 added a SECOND consumer: CodexDetailView (Veilwalkers.UI), which
                // degrades identically (an inert Open(id) no-ops + warns) until the seam closes.
                // The grid's reveal LOGIC and the detail panel's content/reveal LOGIC are both
                // fully proven headless by Veilwalkers.UI.Tests (CodexGridPresenter +
                // CodexDetailPresenter over a real CodexService), independent of this wiring.

                GameServices.Register<IClock>(clock);
                GameServices.Register<IProgressStore>(progressStore);
                GameServices.Register<SaveService>(saveService);
                GameServices.Register<ICreditService>(creditService);
                GameServices.Register<IProgressionService>(progressionService);
                GameServices.Register<IDailyRewardService>(dailyRewardService);
                GameServices.Register<ITelemetryStore>(telemetryStore);
                GameServices.Register<ITelemetrySink>(telemetrySink);
                GameServices.Register<CameraPermissionFlow>(cameraPermissionFlow);
                GameServices.Register<ArSafetyGate>(arSafetyGate);

                // adHook + firstZeroCreditRecorder are intentionally not registered (no
                // resolver yet); keep references so the constructors run (wiring proof) and
                // the locals are not flagged unused.
                _ = adHook;
                _ = firstZeroCreditRecorder;

                GameServices.MarkReady();
                GameLog.Info("Bootstrap: GameServices wired and sealed.");
            }
            catch (System.Exception ex)
            {
                GameLog.Error(
                    "Bootstrap: wiring failed — fatal at boot (a constructor throw " +
                    $"leaves the locator untouched; see class doc). {ex}");
                throw;
            }

            // Fire-and-forget is acceptable for now (awaited boot staging is AR-14),
            // but every fault MUST be observed so a load/grant failure never dies as an
            // unobserved task exception. Resolve via Get<> so the registered instances
            // are always the ones initialized.
            //
            // Because the load is fire-and-forgotten, ICreditService is resolvable
            // BEFORE the save model is loaded; any credit call in that window throws
            // InvalidOperationException by design. Epic 4/6 own general call ordering
            // (Bootstrap.LoadPhase / AppStateMachine, AR-14); do not build staging here.
            // The ONE post-load caller wired today is the Story 1.7 first-launch grant,
            // chained onto a SUCCESSFUL load below (FirstLaunchGrant no-ops on a corrupt
            // save's null model, so a non-faulted-but-corrupt load is safe too).
            GameServices.Get<SaveService>().InitializeAsync().ContinueWith(
                loadTask =>
                {
                    if (loadTask.IsFaulted)
                    {
                        GameLog.Error(
                            "Bootstrap: SaveService.InitializeAsync faulted: " +
                            loadTask.Exception?.GetBaseException());
                        return;
                    }

                    var firstLaunchGrant = new FirstLaunchGrant(
                        GameServices.Get<SaveService>(),
                        GameServices.Get<ICreditService>(),
                        economyMutationLock);
                    firstLaunchGrant.RunAsync().ContinueWith(
                        grantTask => GameLog.Warn(
                            "Bootstrap: first-launch grant faulted (will retry next launch): " +
                            grantTask.Exception?.GetBaseException()),
                        TaskContinuationOptions.OnlyOnFaulted);
                });
        }
    }
}
