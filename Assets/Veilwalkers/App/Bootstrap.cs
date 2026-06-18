using System.Threading.Tasks;
using UnityEngine;
using Veilwalkers.AR;
using Veilwalkers.Billing;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Encounter;
using Veilwalkers.Monsters;
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
    /// composition root. Bootstrap does NOT own the AR session, and it does NOT drive
    /// scene flow — it CONSTRUCTS the <see cref="AppStateMachine"/> (Story 6.3, the
    /// surface-flow owner) and registers it, but the flow itself is driven by that
    /// state machine + the surface Views, not by Bootstrap. Bootstrap only assembles
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

        /// <summary>
        /// The populated Monster registry (Story 2.2 / AR-3). Assigned on the Bootstrap
        /// component in the scene-0 entry scene alongside <see cref="_economyConfig"/>; it
        /// is the catalog <see cref="Veilwalkers.Monsters.Codex.CodexService"/> reads and
        /// <c>LureSystem</c> rolls rarity over. Required — wiring fails fast at boot if it is
        /// unassigned (see <see cref="WireServices"/>), the same all-or-nothing posture as
        /// <see cref="_economyConfig"/>.
        /// </summary>
        [SerializeField] private MonsterDatabase _monsterDatabase;

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

            // Hoisted likewise so the post-load continuation can run the Story 5.2 launch
            // recovery pass (reconcile any purchase interrupted before crediting on a prior
            // run — granted exactly once). It is captured, not resolved, for the same reason.
            PurchaseReconciler purchaseReconciler = null;

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

                // The Monster registry is required for the same reason (Story 2.2 seam
                // closure): CodexService + LureSystem both take it non-null, so an unassigned
                // MonsterDatabase is a fatal boot misconfiguration, never a silent fallback.
                if (_monsterDatabase == null)
                {
                    throw new System.InvalidOperationException(
                        "Bootstrap: MonsterDatabase is not assigned. Assign the MonsterDatabase " +
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

                // AR session lifecycle owner (Story 3.3, Veilwalkers.AR) — the sole owner of the AR
                // session (cold→prewarming→ready→running→paused), the ONLY place AR is started/torn
                // down (AR-1, architecture.md:405,466-468,584). The pure-logic state machine
                // (ArSessionService) is ctor-injected with the OS seam (IArSession → ArcoreSession,
                // the AndroidCameraPermission-equivalent thin adapter) AND the SAME registered
                // cameraPermissionFlow above — so the inherited Story 3.1 deferral (re-validate camera
                // permission before session start / on backgrounded-resume; Android can revoke it while
                // backgrounded) reads ONE permission-truth source, not a parallel one. Registered LIVE
                // (like CameraPermissionFlow/ArSafetyGate): ArcoreSession's editor path is a no-op and
                // its device subsystem glue is a TODO wired when the AR rig scene lands (Story 3.4/6.3,
                // decision #5), so construction never throws at boot. Construction does NOT warm the
                // session (no StartAsync) — AR warmup is on the async path, NOT awaited before Home
                // (AC-CS-2 / LoadPhase.WarmupAsync). The PREWARM TRIGGER is NOT fired here: Onboarding/
                // Home own it, gated to when the AR-entry affordance is visible/likely (architecture.md
                // :583,588-589 — warming AR before the player is near it burns battery/GPU); Bootstrap
                // owns the LoadPhase staging contract, NOT the trigger (architecture.md:423). The
                // CONSUMER, ArSessionView (Veilwalkers.UI), resolves this via GameServices.Get, pumps
                // OnApplicationPause, and exposes the prewarm/enter/back-out entry points; placing it +
                // routing the triggers is Epic 6 (Story 6.3/6.4). The OnArSessionInterrupted event is
                // raised here on lifecycle loss (permission-revoked-on-resume); its EncounterStateMachine
                // Suspended consumer is Story 3.5/Epic 4. The lifecycle is fully proven headless by
                // AR.Tests against FakeArSession. See LoadPhase.cs for the AC-2 staging contract.
                var arcoreSession = new ArcoreSession();
                var arSessionService = new ArSessionService(arcoreSession, cameraPermissionFlow);

                // AR world-anchoring + pooled spawning + anchor restore (Story 3.4 + 3.5, Veilwalkers.AR) — FR-4.
                // ONE ArcoreAnchorProvider (the architecture-named IArAnchorProvider seam, architecture.md:409 —
                // Story 3.5 absorbed 3.4's IArPlaneAnchorProvider into it; owns BOTH save/forward AND restore)
                // is shared by two pure-logic decision owners: PlaneAnchorService owns the
                // place-on-a-detected-plane (AC-1) / coach-and-do-NOT-spawn-into-empty-space (AC-2) FORWARD
                // decision; AnchorRestoreService owns the Restored / RelocatedToPlane (nearest-in-frustum, else
                // absolute-nearest, never vanish) / Failed RESTORE decision (Story 3.5 AC-2/3/4). MonsterSpawner
                // owns the NFR-1 object-pooling + concurrent-spawn-cap ACCOUNTING behind the ISpawnSink seam.
                // Registered LIVE (like CameraPermissionFlow/ArSafetyGate/ArSessionService above): the production
                // adapters' editor paths are stub-safe no-ops (ArcoreAnchorProvider reports NO plane / NO
                // re-acquire / NO relocation candidates → the AC-2 coaching path + the restore Failed path;
                // GameObjectSpawnSink returns synthetic ids), so construction never throws at boot. The adapters'
                // #if UNITY_ANDROID device/scene glue (the real ARPlaneManager/ARAnchorManager/ARRaycastManager
                // re-acquire + frustum-projected plane candidates + the URP monster-prefab pool) is a
                // TODO(Story 6.3) wired when the AR rig scene lands — the same not-yet-placed scene AR rig the
                // ArcoreSession device glue waits on. The MonsterSpawner cap default (8) is a provisional tunable
                // subject to the NFR-1 device-perf pass. AnchorRestoreService.TryRestore is the DECISION; the
                // EncounterStateMachine.Suspended consumer + the recovery CALL SITE (calling TryRestore after
                // ArSessionService.OnArSessionInterrupted, already raised by 3.3) are Epic 4 (architecture.md:650-652);
                // the "pulled back through the veil" Lure-VFX relocation beat is Epic 4/6. The CONSUMER of the
                // forward decision, ArPlacementView (Veilwalkers.UI), resolves PlaneAnchorService via
                // GameServices.Get and degrades gracefully while unplaced; placing it + the AR-HUD affordance +
                // the coaching-banner pixels are Epic 6 (Story 6.3). Both services' logic is fully proven headless
                // by AR.Tests.
                var anchorProvider = new ArcoreAnchorProvider();
                var planeAnchorService = new PlaneAnchorService(anchorProvider);
                var anchorRestoreService = new AnchorRestoreService(anchorProvider);
                var spawnSink = new GameObjectSpawnSink();
                var monsterSpawner = new MonsterSpawner(spawnSink);

                // BillingService (Story 5.1, Veilwalkers.Billing) — the FR-13 Shop boundary. Browse the
                // Credit Pack catalog + purchase exclusively through Google Play Billing (Unity IAP 5 /
                // Play Billing 8, AC-2) + grant base+bonus into Economy on success (AC-3). Registered LIVE
                // (like CameraPermissionFlow/ArSafetyGate/ArSessionService/ArcoreAnchorProvider above): it
                // has NO unauthored-asset blocker (unlike CodexService/EncounterService) — the catalog is
                // in-code canon and UnityIapStoreAdapter is the platform adapter whose editor path is a
                // stub-safe no-op (it reports a non-completed Failed store result + no localized prices, so
                // construction AND an in-editor purchase attempt never throw / never fake-grant). The
                // adapter's #if UNITY_ANDROID device glue (the real Unity IAP IStoreController purchase flow)
                // is a TODO wired when com.unity.purchasing is imported + the Shop scene lands (Story 6.3 /
                // device build) — the same not-yet-imported-package posture as the ArcoreAnchorProvider device
                // glue. Billing → Economy is STRICTLY one-way (architecture.md:478-480): BillingService is
                // ctor-injected with the ALREADY-constructed creditService and calls GrantCreditsAsync; the
                // Economy assembly never references Billing (structurally enforced by the acyclicity test). The
                // CONSUMER — the Shop UI (Veilwalkers.UI) that renders the catalog (AC-1) + calls PurchaseAsync
                // + binds OnPurchaseCompleted — is Epic 6 (the chunky Shop surface); it resolves IBillingService
                // via GameServices.Get and degrades gracefully while unplaced (the CameraPermissionView/
                // ArSafetyView precedent). 5.1 grants ONCE per successful purchase via GrantCreditsAsync; the
                // exactly-once reconciliation (PurchaseReconciler — pending-ledger, order-id dedup, acknowledge-
                // within-window, interruption survival, NFR-4) layers on top in Story 5.2 (architecture.md:522
                // routes BillingService → Unity IAP → PurchaseReconciler → CreditService). The Guaranteed-Rare
                // Lure the Veil pack DECLARES is granted/consumed in Story 5.3. The logic is fully proven
                // headless by Veilwalkers.Billing.Tests over a real CreditService + a FakeStoreAdapter.
                var creditPackCatalog = new CreditPackCatalog();
                var storeAdapter = new UnityIapStoreAdapter();

                // PurchaseReconciler (Story 5.2, Veilwalkers.Billing) — the exactly-once engine (NFR-4) the
                // 5.1 BillingService deferred. It interposes into BillingService's SINGLE grant site (the
                // Purchased branch) without a public-surface change: write pending-ledger → grant once keyed
                // by Play order id → acknowledge (within Play's window, AC-4) → clear. It is a sibling
                // Economy-tier mutator of the save model, so it REUSES the SAME shared economyMutationLock as
                // creditService/progressionService/dailyRewardService (a per-reconciler lock would let a
                // reconcile write and a credit spend interleave + persist each other's uncommitted deltas);
                // it REUSES the already-constructed creditService (one-way Billing → Economy via
                // GrantCreditsAsync), the SAME creditPackCatalog + storeAdapter the service routes through (so
                // the acknowledge targets the purchase the service made), and the SAME clock as the daily
                // reward. The real Play acknowledge/consume SDK call is the device-build TODO (com.unity.purchasing
                // not imported — the editor #else AcknowledgeAsync is a no-op success). The launch recovery
                // pass (ReconcilePendingOnLaunchAsync) is scheduled post-load below.
                purchaseReconciler = new PurchaseReconciler(
                    saveService, economyMutationLock, creditService, creditPackCatalog, storeAdapter, clock);
                var billingService = new BillingService(
                    creditPackCatalog, storeAdapter, creditService, purchaseReconciler);

                // IRandom (Story 4.2, Veilwalkers.Core) — the randomness seam for the Lure rarity roll. A
                // time-seeded SystemRandom in production; tests script a fake. Constructed + registered LIVE
                // (it has NO blocker — pure System.Random). LureSystem below consumes the same instance.
                var random = new SystemRandom();

                // CodexService (Story 2.3, Veilwalkers.Monsters) — SEAM NOW CLOSED (Epic 8 Gate 0). It is the
                // read model + atomic discovery-record seam over SaveModel.Codex; it owns a PRIVATE
                // SemaphoreSlim, so it takes NO lock arg (it must NOT inject the Economy SaveMutationLock — that
                // would be an illegal Monsters → Economy edge). It was deferred only because its second
                // collaborator, a MonsterDatabase instance, had no authored .asset; now that
                // MonsterDatabase.asset exists (assigned to _monsterDatabase above), it is constructed +
                // registered live. The IClock (registered below) stamps the first-discovered date (Story 2.5).
                // Its consumers — CodexGridView + CodexDetailView (Veilwalkers.UI) — resolve it via
                // GameServices.Get; their LOGIC is proven headless by Veilwalkers.UI.Tests.
                var codexService = new CodexService(saveService, _monsterDatabase, clock);

                // EncounterService + its systems (Stories 4.1–4.6, Veilwalkers.Encounter) — SEAM NOW CLOSED
                // (Epic 8 Gate 0). EncounterService is the encounter spine: it drives the EncounterStateMachine,
                // owns the AC-2 atomic multi-delta write (charge/credit AND codex in ONE SaveAsync under the
                // SHARED economyMutationLock), wires OnArSessionInterrupted → Suspended → recovery TryRestore
                // (AC-3), and exposes the FR-6–10 actions composing LureSystem + planeAnchorService +
                // monsterSpawner. CRITICAL: it is passed the SAME economyMutationLock constructed above (shared
                // with CreditService/ProgressionService) — the composed Encounter write MUST serialize against
                // plain Economy writes (a private lock would let a composed action interleave with a credit
                // spend and durably persist each other's uncommitted delta). LureSystem/CaptureSystem/SlaySystem
                // reuse the SAME IRandom instance; LureSystem + SlaySystem reuse the SAME EconomyConfig. The
                // logic is proven headless by Veilwalkers.Encounter.Tests; this wiring makes it live at runtime.
                var lureSystem = new LureSystem(_economyConfig, _monsterDatabase, random);
                var captureSystem = new CaptureSystem(random);
                var slaySystem = new SlaySystem(_economyConfig, random);
                var encounterService = new EncounterService(
                    saveService, creditService, progressionService, codexService,
                    economyMutationLock, anchorRestoreService, arSessionService,
                    lureSystem, planeAnchorService, monsterSpawner, captureSystem, slaySystem);

                // AppStateMachine (Story 6.3, this App tier — architecture.md:462-464) — the surface-flow
                // state machine + the AC-2 insufficient-credits → Shop arbiter. It is NOT a Core service; it
                // lives here, the "only thing that arbitrates UI ↔ service control flow." Its ctor SUBSCRIBES to
                // creditService.OnInsufficientCredits (the AC-2 source) AND — now that EncounterService is live
                // (Epic 8 Gate 0) — to the SECOND shortfall source (EncounterService's OnInsufficientCredits for
                // composed Lure/Slay spends) via the App-tier ShortfallAdapter, and it owns the encounter
                // Shop-round-trip snapshot port via the SnapshotPortAdapter (both in EncounterServiceAppAdapters
                // — thin App-tier binders, since EncounterService sits BELOW App and cannot itself implement an
                // App-tier interface, AR-5). This replaces the prior IEncounterSnapshotPort.NoEncounter no-op +
                // null second source. The CONSUMERS — HomePresenter / ArHudPresenter (Veilwalkers.UI) + their
                // thin Views — resolve this via GameServices.Get; placing those Views in scenes is the Epic-8
                // view layer. NOTE: AppStateMachine implements IDisposable (it unsubscribes symmetrically —
                // architecture.md:514-515); Bootstrap has no teardown in a player build (wire-once), but the
                // Dispose exists for tests + future scene unload.
                var encounterSnapshotPort =
                    new EncounterServiceAppAdapters.SnapshotPortAdapter(encounterService);
                var encounterShortfall =
                    new EncounterServiceAppAdapters.ShortfallAdapter(encounterService);
                var appStateMachine = new AppStateMachine(
                    creditService, encounterSnapshotPort, encounterShortfall);

                GameServices.Register<IClock>(clock);
                GameServices.Register<IRandom>(random);
                GameServices.Register<IProgressStore>(progressStore);
                GameServices.Register<SaveService>(saveService);
                GameServices.Register<ICreditService>(creditService);
                GameServices.Register<IProgressionService>(progressionService);
                GameServices.Register<IDailyRewardService>(dailyRewardService);
                GameServices.Register<ITelemetryStore>(telemetryStore);
                GameServices.Register<ITelemetrySink>(telemetrySink);
                GameServices.Register<CameraPermissionFlow>(cameraPermissionFlow);
                GameServices.Register<ArSafetyGate>(arSafetyGate);
                GameServices.Register<ArSessionService>(arSessionService);
                GameServices.Register<PlaneAnchorService>(planeAnchorService);
                GameServices.Register<AnchorRestoreService>(anchorRestoreService);
                GameServices.Register<MonsterSpawner>(monsterSpawner);
                GameServices.Register<CodexService>(codexService);
                GameServices.Register<EncounterService>(encounterService);
                GameServices.Register<IBillingService>(billingService);
                GameServices.Register<AppStateMachine>(appStateMachine);

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

                    // Story 5.2 (AC-2): reconcile any purchase interrupted before crediting on a PRIOR run —
                    // granted exactly once (the persisted Granted state + order-id dedup prevent a double
                    // grant) and acknowledged within Play's window (AC-4). A no-op when PendingPurchases is
                    // empty (the overwhelming common launch), so it never blocks the splash; it runs HERE,
                    // post-load, because the reconciler reads SaveService.Current (RequireModel throws before
                    // the model is loaded — the CreditService before-load contract). Fire-and-forget like the
                    // grant above; every fault is observed so it never dies as an unobserved task exception.
                    purchaseReconciler.ReconcilePendingOnLaunchAsync().ContinueWith(
                        reconcileTask => GameLog.Warn(
                            "Bootstrap: launch purchase-reconcile faulted (will retry next launch): " +
                            reconcileTask.Exception?.GetBaseException()),
                        TaskContinuationOptions.OnlyOnFaulted);
                });
        }
    }
}
