using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Veilwalkers.AR;
using Veilwalkers.Core;
using Veilwalkers.Core.Contracts;
using Veilwalkers.Economy;
using Veilwalkers.Monsters;
using Veilwalkers.Persistence;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// The encounter spine (Story 4.1, FR-6–10) — the service that drives one <see cref="EncounterStateMachine"/>
    /// and owns the ONE atomic multi-delta write (AR-8) every player action commits through. Pure C# (NO
    /// MonoBehaviour); receives ALL dependencies via the constructor (AR-4 / architecture.md:456-459 — Encounter
    /// is pure-logic and must NOT call <c>GameServices.Get&lt;T&gt;()</c>).
    /// <para>
    /// <b>Scope (Story 4.1 — the spine, not the gameplay).</b> 4.1 builds: (a) the state machine it drives;
    /// (b) the AC-2 atomic-write PRIMITIVE (<see cref="CommitActionAsync"/>) — one <c>SaveModel</c> mutation
    /// (balance/charge AND codex), ONE <c>SaveService.SaveAsync</c>, under the SHARED Economy
    /// <see cref="SaveMutationLock"/>, whole-mutation rollback on fault — proven by a representative composed
    /// action (<see cref="TryCaptureAsync"/>, charge + codex); (c) the AC-3 <c>OnArSessionInterrupted</c> →
    /// <see cref="EncounterState.Suspended"/> → recovery <c>TryRestore</c> wiring; (d) the
    /// <see cref="OnInsufficientCredits"/> surfacing seam (AR-11). The FR-6–10 per-action BUSINESS LOGIC
    /// (Lure cost+rarity 4.2, Scan 4.3, Capture math 4.4, Slay loot 4.5, extras/Retry 4.6) is downstream —
    /// those stories add public methods calling <see cref="CommitActionAsync"/>.
    /// </para>
    /// <para>
    /// <b>The atomic multi-delta write (AC-2 — the architectural heart).</b> Encounter is the only assembly
    /// that sees BOTH the Economy <see cref="SaveMutationLock"/> AND <see cref="CodexService"/>, so the
    /// composed write lives here. It clones the <see cref="ProgressionService.AddXpAsync"/> pipeline: acquire
    /// the SHARED lock once → capture the model ref once → mutate every slice in memory (economy via the
    /// caller's delegate; codex via <see cref="CodexService.StageDiscovery"/>, lock-free + persist-free) →
    /// ONE <c>SaveAsync</c> → post-persist recovery-swap guard (<c>ReferenceEquals(Current, model)</c>) →
    /// roll the WHOLE mutation back on any fault → release the lock → raise events. The shared lock means a
    /// composed write can never interleave with a plain credit spend / charge consume (the whole reason the
    /// lock is shared — see <see cref="SaveMutationLock"/>). NEVER two persists per action (the AR-8 rule).
    /// </para>
    /// <para>
    /// <b>Mid-session AR loss (AC-3).</b> Subscribes to <see cref="ArSessionService.OnArSessionInterrupted"/>
    /// (raised by Story 3.3): an active encounter enters <see cref="EncounterState.Suspended"/> + surfaces
    /// guidance (the COPY/UI is Epic 6). On <see cref="Recover"/> it calls
    /// <see cref="AnchorRestoreService.TryRestore"/> (the Story 3.5 decision): <c>Restored</c>/<c>RelocatedToPlane</c>
    /// resume the encounter; <c>Failed</c> stays suspended with guidance (never vanish, NFR-3). An interruption
    /// that arrives DURING <see cref="EncounterState.Resolving"/> is deferred until the atomic write settles
    /// (decision H2 — see <see cref="EncounterStateMachine.Suspend"/>).
    /// </para>
    /// </summary>
    public sealed class EncounterService
    {
        private readonly SaveService _saveService;
        private readonly ICreditService _creditService;
        // The CONCRETE ProgressionService (not the IProgressionService interface) because the composed Capture
        // write (Story 4.4) needs the StageXpGrant seam — the lock-free, persist-free XP/level/charge-grant
        // delta the one Capture SaveAsync applies (AR-8). The same posture as the concrete CodexService below
        // (whose StageDiscovery/StageScan seams the composed writes use). It still IS an IProgressionService.
        private readonly ProgressionService _progressionService;
        private readonly CodexService _codexService;
        private readonly SaveMutationLock _mutationLock;
        private readonly AnchorRestoreService _anchorRestoreService;
        private readonly ArSessionService _arSessionService;

        // Story 4.2 — the Lure collaborators: the decision logic (cost + rarity roll), the placement
        // decision (AC-1 "deduct only on successful spawn" — placement is secured BEFORE the spend), and the
        // pooled spawner (NFR-1). All ctor-injected (AR-4).
        private readonly LureSystem _lureSystem;
        private readonly PlaneAnchorService _planeAnchorService;
        private readonly MonsterSpawner _monsterSpawner;

        // Story 4.4 — the Capture success-roll decision (base vs Strong chance, the LureSystem precedent). The
        // service composes its roll outcome into the atomic Capture write (the system rolls, the service
        // persists). Ctor-injected (AR-4).
        private readonly CaptureSystem _captureSystem;

        // Story 4.5 — the Slay success-roll decision + cost accessor (the CaptureSystem precedent). The service
        // composes its roll outcome into the atomic Slay write (credits + codex + XP, roll-gated). Ctor-injected (AR-4).
        private readonly SlaySystem _slaySystem;

        private readonly EncounterStateMachine _stateMachine = new EncounterStateMachine();

        // The active encounter's anchors (in-memory only this story — the persisted Shop round-trip snapshot
        // is Story 5.4). Set when a Lure spawns (4.2); read on recovery to re-anchor. Minimal for 4.1.
        private AnchorToken[] _activeAnchors = Array.Empty<AnchorToken>();

        // The active encounter's pooled-spawn handles (Story 4.2). Held so EndEncounter can Release them back
        // to the MonsterSpawner pool — otherwise a Lured encounter's spawns would stay active forever (a pool
        // leak). Set on a successful Lure; cleared + released on EndEncounter.
        private int[] _activeSpawnHandles = Array.Empty<int>();

        // The set of Monster ids scanned THIS encounter (Story 4.3, FR-7 — Decision B1). The in-memory scan
        // PROGRESS record: a Scan records partial progress here EVEN for a not-yet-discovered Monster (so AC-2
        // "partial Codex data even before Capture/Slay" holds WITHOUT creating a Codex key / inflating X/67),
        // and additionally flips the persistent CodexEntryData.Scanned flag iff the Monster is already
        // discovered (CodexService.StageScan). Cleared on EndEncounter. The persisted disk round-trip of this
        // per-monster scan state (across a Shop navigation / relaunch) is Story 5.4 — 4.3 holds it in memory
        // only (the 4.1 `_activeAnchors` precedent).
        private readonly HashSet<string> _scannedThisEncounter = new HashSet<string>();

        // The extras applied THIS encounter (Story 4.6, FR-10 — Decision C'). The in-memory per-encounter
        // MODIFIER record: an applied Stability Boost / Nightveil Filter records its ExtraKind here (the
        // `_scannedThisEncounter` precedent — set BEFORE the persist, reverted on a fault, cleared on
        // EndEncounter). The existing rolls consult it for the ease/rarity bonus: Stability Boost eases the
        // Capture and Slay success ROLLS (AC-1) — Scan has NO success roll to ease (it is a free, deterministic
        // flag-record, CommitScanAsync), so "Scan/Capture/Slay ease" in AC-1 means Capture+Slay in practice;
        // Nightveil raises the Lure rarity roll (AC-2) + flags the atmospheric visual filter (the render-layer
        // flag the Epic-6 VFX reads). Per-encounter: an extra's effect does NOT carry to the next encounter (AC-1/AC-2 "for the
        // remainder of the current encounter"). The persisted disk round-trip (across a Shop navigation) is
        // Story 5.4 — the same deferral as the scan-progress snapshot.
        private readonly HashSet<ExtraKind> _activeExtras = new HashSet<ExtraKind>();

        /// <summary>
        /// Raised when a composed action cannot afford its credit spend (AR-11): the service NEVER opens the
        /// Shop or calls Billing/UI — App/UI decides the top-up prompt. Surfaced at the encounter altitude so
        /// App/UI subscribes here; sourced from the rejected spend's typed <see cref="SpendResult"/>. The
        /// actual Lure/Slay spends are Stories 4.2/4.5 — 4.1 establishes the seam. Subscriber-isolated.
        /// </summary>
        public event Action<InsufficientCreditsEvent> OnInsufficientCredits;

        /// <summary>The encounter state machine this service drives (read-only access for UI/tests).</summary>
        public EncounterStateMachine StateMachine => _stateMachine;

        /// <summary>The current encounter state (convenience pass-through to <see cref="StateMachine"/>).</summary>
        public EncounterState State => _stateMachine.State;

        public EncounterService(
            SaveService saveService,
            ICreditService creditService,
            ProgressionService progressionService,
            CodexService codexService,
            SaveMutationLock mutationLock,
            AnchorRestoreService anchorRestoreService,
            ArSessionService arSessionService,
            LureSystem lureSystem,
            PlaneAnchorService planeAnchorService,
            MonsterSpawner monsterSpawner,
            CaptureSystem captureSystem,
            SlaySystem slaySystem)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _creditService = creditService ?? throw new ArgumentNullException(nameof(creditService));
            _progressionService = progressionService ?? throw new ArgumentNullException(nameof(progressionService));
            _codexService = codexService ?? throw new ArgumentNullException(nameof(codexService));
            _mutationLock = mutationLock ?? throw new ArgumentNullException(nameof(mutationLock));
            _anchorRestoreService = anchorRestoreService ?? throw new ArgumentNullException(nameof(anchorRestoreService));
            _arSessionService = arSessionService ?? throw new ArgumentNullException(nameof(arSessionService));
            _lureSystem = lureSystem ?? throw new ArgumentNullException(nameof(lureSystem));
            _planeAnchorService = planeAnchorService ?? throw new ArgumentNullException(nameof(planeAnchorService));
            _monsterSpawner = monsterSpawner ?? throw new ArgumentNullException(nameof(monsterSpawner));
            _captureSystem = captureSystem ?? throw new ArgumentNullException(nameof(captureSystem));
            _slaySystem = slaySystem ?? throw new ArgumentNullException(nameof(slaySystem));

            // AC-3: the encounter consumer of the AR-loss event 3.3 already raises. Subscribed for the
            // service's lifetime; symmetric unsubscribe lands with the Epic-6 disposal/teardown contract.
            _arSessionService.OnArSessionInterrupted += HandleArSessionInterrupted;
        }

        // ---- AC-1: state-machine entry points (the Lure/begin-action transitions; gameplay is 4.2–4.6) ----

        /// <summary>
        /// Idle → Lured. The Lure action's cost/rarity logic is Story 4.2; 4.1 owns the transition + records
        /// the encounter's anchors so recovery (AC-3) can re-anchor them. <paramref name="anchors"/> may be
        /// empty (the anchor wiring fills in with 4.2's spawn).
        /// </summary>
        public bool BeginLure(AnchorToken[] anchors)
        {
            if (!_stateMachine.BeginLure())
            {
                return false;
            }

            _activeAnchors = anchors ?? Array.Empty<AnchorToken>();
            return true;
        }

        /// <summary>Lured → Acting: the player began an action (the action logic is 4.3–4.6).</summary>
        public bool BeginAction() => _stateMachine.BeginAction();

        /// <summary>Reset the encounter to Idle (ends/aborts it); releases the active pooled spawns back to the
        /// <see cref="MonsterSpawner"/> pool and clears the active anchors. (Without the release the Lured
        /// encounter's spawns would stay active across encounters — a pool leak.)</summary>
        public bool EndEncounter()
        {
            foreach (int handle in _activeSpawnHandles)
            {
                _monsterSpawner.Release(handle);
            }

            _activeSpawnHandles = Array.Empty<int>();
            _activeAnchors = Array.Empty<AnchorToken>();
            _scannedThisEncounter.Clear(); // per-encounter scan progress does not carry to the next encounter
            _activeExtras.Clear(); // per-encounter extra modifiers (Stability Boost / Nightveil) do not carry over (4.6)
            return _stateMachine.Reset();
        }

        // ---- AC-1/3/4 (Story 4.2): the Lure action — cost + rarity + placement + atomic credit-spend ----

        /// <summary>
        /// Lure a Monster (Story 4.2, FR-6): Basic (1) / Premium (4) / Multi-Lure (5). Composes the
        /// <see cref="LureSystem"/> decision (cost + rarity roll) with the Epic-3 placement
        /// (<see cref="PlaneAnchorService"/>) and pooled spawn (<see cref="MonsterSpawner"/>), committing the
        /// Credit deduction in ONE atomic save write (AR-8). Returns a typed <see cref="LureResult"/> — never
        /// throws for an expected failure (AR-7).
        /// <para>
        /// <b>The ordering (Decision E — "deduct only on successful spawn", AC-1).</b> Placement is secured
        /// FIRST (a placement that cannot be obtained costs nothing — <see cref="LureFailureReason.NoPlacement"/>);
        /// THEN credits are checked + deducted in the single composed write (insufficient → nothing persisted,
        /// <see cref="OnInsufficientCredits"/> raised, <see cref="LureFailureReason.InsufficientCredits"/>); THEN
        /// the secured placements are activated as pooled spawns and the state machine moves Idle → Lured. So
        /// there is never a charge without a spawn, nor a spawn without a charge.
        /// </para>
        /// <para>
        /// <b>Multi-Lure (AC-3 — require two or block).</b> A Multi-Lure needs TWO valid placements. If the
        /// second cannot be secured, the first is released and the whole Lure is blocked with coaching,
        /// deducting nothing — never a partial spawn, never a double-charge. (The "spawn-what-fits + queue"
        /// option was rejected by the PM; see the story's resolved FLAG.)
        /// </para>
        /// <para>
        /// The tiered materialization VFX/entrance (T1 Pop-in … T5 Breach) is Story 4.7 — 4.2 performs the
        /// LOGICAL spawn (pool activate + anchor) + the state transition; the <see cref="LureResult"/> is the
        /// sub-second spend-ack contract (returned synchronously after the persist commits, NFR-2), and the
        /// cinematic plays afterward against the returned ids.
        /// </para>
        /// </summary>
        public async Task<LureResult> TryLureAsync(LureKind kind)
        {
            // Must start from Idle (one Lure per encounter; a re-Lure aborts the prior encounter first via
            // EndEncounter). A non-Idle machine is a warned no-op — surface a typed failure, persist nothing.
            if (_stateMachine.State != EncounterState.Idle)
            {
                GameLog.Warn(
                    $"EncounterService.TryLureAsync ignored — an encounter is already active (state: {_stateMachine.State}).");
                // Re-entrancy refusal — NOT a placement or affordability failure. No spend was attempted.
                return LureResult.Failed(
                    LureFailureReason.AlreadyActive, SpendResult.Failed(SpendFailureReason.NotAttempted, CurrentBalance()));
            }

            int cost = _lureSystem.CostOf(kind);
            int wanted = _lureSystem.MonsterCountOf(kind);

            // (1) Roll the monster id(s) — pure decision, no side effects (LureSystem). Multi rolls two
            // INDEPENDENT monsters; Basic/Premium roll one. Story 4.6 (AC-2): an active Nightveil Filter raises
            // the rare-tier chance — pass the in-encounter modifier state INTO the roll (the system stays pure;
            // it does not back-reference the service). NOTE: a Lure starts from Idle (one Lure per encounter), so
            // `_activeExtras` is empty here unless a prior Lure's encounter applied Nightveil AND was not ended —
            // but a re-Lure requires Idle, and EndEncounter clears `_activeExtras`. The boost therefore applies to
            // Lures rolled within the SAME live encounter once the deferred re-Lure / queued-spawn flows land;
            // the seam is wired now so the rarity boost reads from the same source as Capture/Slay ease.
            bool nightveilActive = _activeExtras.Contains(ExtraKind.NightveilFilter);
            var monsterIds = new List<string>(wanted);
            if (kind == LureKind.Multi)
            {
                (string first, string second) = _lureSystem.RollMultiMonsters(nightveilActive);
                monsterIds.Add(first);
                monsterIds.Add(second);
            }
            else
            {
                monsterIds.Add(_lureSystem.RollMonster(kind, nightveilActive));
            }

            // (2) Secure the required placements BEFORE the spend (Decision E). For Multi this is two; if the
            // second cannot be secured, release the first and block — deduct nothing (AC-3). No anchor here
            // creates a persisted side effect, so abandoning a secured placement on a partial-fit block is
            // free of economy consequences (the spawn is only ACTIVATED after the deduction commits).
            var anchors = new List<AnchorToken>(wanted);
            if (!TrySecurePlacements(wanted, anchors))
            {
                // Not enough planes — coaching is already surfaced by PlaneAnchorService. Nothing deducted,
                // machine stays Idle (the world does not lock).
                GameLog.Info(
                    $"EncounterService.TryLureAsync ({kind}) blocked — only secured {anchors.Count} of {wanted} " +
                    "placements. Nothing deducted (AC-1/AC-3); showing plane coaching.");
                // Placement failed BEFORE any spend — the credits were never checked, so this is NotAttempted,
                // NOT InsufficientCredits (a consumer must not read this as "can't afford").
                return LureResult.Failed(
                    LureFailureReason.NoPlacement, SpendResult.Failed(SpendFailureReason.NotAttempted, CurrentBalance()));
            }

            // (3) The atomic credit-spend write (AR-8): ONE persist, whole-mutation rollback. Insufficient
            // credits is reported WITHOUT persisting; a persist fault rolls back.
            SpendResult spend = await CommitLureSpendAsync(cost).ConfigureAwait(false);

            if (!spend.Success)
            {
                if (spend.FailureReason == SpendFailureReason.InsufficientCredits)
                {
                    // AC-4: raise the rejected-spend event (AR-11 — never open the Shop). The world does not
                    // lock; the secured placements are simply abandoned (no spawn activated, no anchor cost).
                    RaiseInsufficientCredits(new InsufficientCreditsEvent(cost, spend.NewBalance));
                    return LureResult.Failed(LureFailureReason.InsufficientCredits, spend);
                }

                // Persist fault: the deduction (if any) already rolled back inside the composed write.
                return LureResult.Failed(LureFailureReason.PersistenceFailed, spend);
            }

            // (4) The spend committed — NOW activate the secured placements as pooled spawns and move
            // Idle → Lured (recording the anchors for AC-3 recovery). The spawn is the "perform" step; given
            // the pre-check it should not fail, but a cap refusal here is the NFR-3 path → refund + abort.
            if (!ActivateSpawns(anchors, out List<int> handles))
            {
                SpendResult refund = await RefundLureSpendAsync(cost).ConfigureAwait(false);
                GameLog.Warn(
                    $"EncounterService.TryLureAsync ({kind}): a spawn was refused after the deduction — refunded " +
                    "and aborted (NFR-3). No charge without a spawn.");
                return LureResult.Failed(LureFailureReason.PersistenceFailed, refund);
            }

            // Idle → Lured. The Idle pre-check above + single-threaded gameplay guarantee this succeeds; if it
            // somehow does not, reconcile (release the just-activated spawns + refund) rather than returning a
            // false success with charged-but-not-Lured state (NFR-3 / failure-path-cleanup-parity).
            if (!_stateMachine.BeginLure())
            {
                foreach (int handle in handles)
                {
                    _monsterSpawner.Release(handle);
                }

                SpendResult refund = await RefundLureSpendAsync(cost).ConfigureAwait(false);
                GameLog.Error(
                    $"EncounterService.TryLureAsync ({kind}): Idle → Lured transition was refused after a committed " +
                    "spend — released the spawns + refunded (should be unreachable given the Idle pre-check).");
                return LureResult.Failed(LureFailureReason.PersistenceFailed, refund);
            }

            _activeAnchors = anchors.ToArray();
            _activeSpawnHandles = handles.ToArray();

            return LureResult.Succeeded(spend, monsterIds);
        }

        /// <summary>
        /// Secure <paramref name="count"/> placements via <see cref="PlaneAnchorService.TryPlace"/>,
        /// appending each <c>Placed</c> token to <paramref name="anchors"/>. Returns true only if ALL
        /// <paramref name="count"/> were secured; on a shortfall it returns false WITHOUT mutating
        /// <paramref name="anchors"/> beyond what it secured (the caller treats a partial as a block). No
        /// persisted side effect — a secured-but-unused anchor is abandoned for free (the spawn is activated
        /// later, only after the deduction commits).
        /// </summary>
        private bool TrySecurePlacements(int count, List<AnchorToken> anchors)
        {
            for (int i = 0; i < count; i++)
            {
                PlacementResult placement = _planeAnchorService.TryPlace();
                if (placement.Outcome != PlacementOutcome.Placed)
                {
                    // A shortfall: AC-3 requires ALL-or-block. Stop; the caller blocks + deducts nothing.
                    return false;
                }

                anchors.Add(placement.Token);
            }

            return true;
        }

        /// <summary>
        /// Activate one pooled spawn per secured anchor (<see cref="MonsterSpawner.TrySpawn"/>), deriving the
        /// <see cref="Pose"/> from the token (<c>new Pose(token.position, token.rotation)</c> — the
        /// <see cref="AnchorRestoreService"/> precedent). Returns true only if EVERY spawn activated; on a cap
        /// refusal it releases any spawns it already activated (so a partial spawn never lingers) and returns
        /// false. Never throws (NFR-3).
        /// </summary>
        private bool ActivateSpawns(List<AnchorToken> anchors, out List<int> handles)
        {
            handles = new List<int>(anchors.Count);
            foreach (AnchorToken token in anchors)
            {
                var pose = new Pose(token.position, token.rotation);
                if (!_monsterSpawner.TrySpawn(in pose, out int handle))
                {
                    // Reconcile: release the spawns already activated for this Lure so a refused Multi does
                    // not leave one monster active (the [[failure-path-cleanup-parity]] discipline).
                    foreach (int activated in handles)
                    {
                        _monsterSpawner.Release(activated);
                    }

                    handles.Clear();
                    return false;
                }

                handles.Add(handle);
            }

            return true;
        }

        /// <summary>
        /// The atomic credit-spend composed write (AR-8) for a Lure — a SIBLING of
        /// <see cref="CommitActionAsync"/>: it spends CREDITS (not a charge) and records NO codex discovery (a
        /// Lure does not discover — that is Capture/Slay, Stories 4.4/4.5). Same pipeline shape as
        /// <see cref="ProgressionService.AddXpAsync"/> / <see cref="CommitActionAsync"/>: acquire the SHARED
        /// <see cref="SaveMutationLock"/> once → capture the model ref once → validate <c>Credits &gt;= cost</c>
        /// (the exact-balance <c>&gt;=</c> rule) → deduct in memory → ONE <c>SaveAsync</c> → recovery-swap
        /// guard → roll back on any fault → release → (events are the caller's: <see cref="OnInsufficientCredits"/>
        /// on the insufficient path). Does NOT call <see cref="ICreditService"/> — that takes its OWN lock, so
        /// calling it here would deadlock; the composed write mutates <c>model.Credits</c> directly under the
        /// shared lock (the [[codex-service-lock-tier]] rule — exactly as <see cref="CommitActionAsync"/>
        /// mutates charges directly).
        /// </summary>
        private async Task<SpendResult> CommitLureSpendAsync(int cost)
        {
            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                int priorCredits = model.Credits;

                if (priorCredits < cost)
                {
                    // Expected "can't afford" failure — no mutation, no persist (AC-4). The unchanged balance
                    // is reported so the OnInsufficientCredits payload is accurate.
                    return SpendResult.Failed(SpendFailureReason.InsufficientCredits, priorCredits);
                }

                // Deduct in memory (exact-balance OK: priorCredits == cost → 0, the >= rule).
                model.Credits = priorCredits - cost;

                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (ReferenceEquals(_saveService.Current, model))
                    {
                        return SpendResult.Succeeded(model.Credits);
                    }

                    // Recovery swap mid-persist: SaveAsync durably wrote whatever Current pointed at, NOT this
                    // deduction. Revert onto the captured ref (the swap-branch contract) + report failure.
                    model.Credits = priorCredits;
                    GameLog.Error(
                        $"EncounterService: a Lure spend ({cost}) rolled back — the save model was swapped " +
                        "mid-operation (recovery raced a mutation).");
                    return SpendResult.Failed(SpendFailureReason.PersistenceFailed, priorCredits);
                }
                catch (Exception ex)
                {
                    // Persist fault: revert the deduction onto the captured ref — never recompute.
                    model.Credits = priorCredits;
                    GameLog.Error(
                        $"EncounterService: a Lure spend ({cost}) rolled back — persist failed. {ex.Message}");
                    return SpendResult.Failed(SpendFailureReason.PersistenceFailed, priorCredits);
                }
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        /// <summary>
        /// Refund a Lure's cost (the NFR-3 path when a spawn is refused AFTER the deduction committed):
        /// re-credit <paramref name="cost"/> in one atomic write under the shared lock, so a refused spawn
        /// never leaves the player charged. Mirrors the spend pipeline (add instead of subtract). A refund
        /// persist fault is logged; the in-memory balance is restored regardless so gameplay is not charged.
        /// </summary>
        private async Task<SpendResult> RefundLureSpendAsync(int cost)
        {
            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                model.Credits += cost; // re-credit in memory so gameplay is never left charged

                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);
                    if (!ReferenceEquals(_saveService.Current, model))
                    {
                        // Swapped mid-persist: the durable save did NOT capture the refund. Report a failure
                        // (the refund did not durably commit) — the caller already returns a failed LureResult;
                        // do NOT claim Succeeded, or a consumer would believe the credit state is committed.
                        GameLog.Error(
                            "EncounterService: a Lure refund could not be persisted (model swapped mid-persist) — " +
                            "the in-memory balance is restored, but the durable save may lag until the next write.");
                        return SpendResult.Failed(SpendFailureReason.PersistenceFailed, model.Credits);
                    }
                }
                catch (Exception ex)
                {
                    GameLog.Error(
                        $"EncounterService: a Lure refund persist failed — in-memory balance restored, durable save lags. {ex.Message}");
                    return SpendResult.Failed(SpendFailureReason.PersistenceFailed, model.Credits);
                }

                return SpendResult.Succeeded(model.Credits);
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        /// <summary>The current credit balance, or 0 if the model is not loaded (defensive — used only to
        /// populate a typed failure's balance field on an early-out path).</summary>
        private int CurrentBalance() => _saveService.Current?.Credits ?? 0;

        // ---- FR-8 (Story 4.4): Capture — free base / Strong Capture (earned charge), with a success roll ----

        /// <summary>
        /// Capture the settled Monster <paramref name="monsterId"/> (Story 4.4, FR-8). A BASE Capture is FREE
        /// (no Credits, no charge); a STRONG Capture (<paramref name="strong"/> = true) consumes exactly one
        /// <see cref="ChargeType.StrongCapture"/> charge to apply a STRICTLY HIGHER success probability (never
        /// Credits; blocked at zero charges; never negative). The outcome is a ROLL (<see cref="CaptureSystem"/>):
        /// a SUCCESS records the Codex Capture discovery (X/67 +1 if newly discovered) AND grants XP (AC-4),
        /// committed in ONE atomic save (AR-8); a MISS records nothing and leaves the encounter live for a FREE
        /// Retry (just call this again — AC-3). The Credit balance NEVER changes (AC-1/AC-2).
        /// <para>
        /// Returns a typed <see cref="CaptureResult"/> — never throws for an expected outcome (a miss, a
        /// zero-charge block, an out-of-sequence call, a persist fault are all reported on the result, AR-7);
        /// throws only for a null/empty/invalid monster id (programmer error). <see cref="CaptureResult.Success"/>
        /// = "the attempt ran"; <see cref="CaptureResult.Captured"/> = the roll outcome (captured vs missed).
        /// </para>
        /// <para>
        /// Drives the in-encounter loop (Lured → Acting → Resolving → Lured) — modelled on
        /// <see cref="TryScanAsync"/> (it calls <c>BeginAction</c> itself then <c>BeginResolve</c>), NOT the
        /// inherited <see cref="RunActionAsync"/> (which assumes the caller already drove Lured → Acting and
        /// returns the misleading <c>PersistenceFailed</c>+0 on its out-of-sequence branch). A MISS is a
        /// successfully RESOLVED action (it <c>CompleteResolve</c>s back to Lured), not a state-machine failure.
        /// </para>
        /// </summary>
        public async Task<CaptureResult> TryCaptureAsync(string monsterId, bool strong)
        {
            // Validate the FULL id up front (programmer error — same contract as StageDiscovery/TryScanAsync)
            // BEFORE touching the state machine, so a bad id never leaves the machine stranded mid-resolve and
            // an invalid id throws regardless of the roll outcome (not only on a winning roll).
            if (string.IsNullOrEmpty(monsterId) || !MonsterDatabase.IsValidMonsterId(monsterId))
            {
                throw new ArgumentException(
                    $"'{monsterId}' is not a valid Monster id (expected mon01..mon{MonsterDatabase.UniverseCount:00}).",
                    nameof(monsterId));
            }

            // The state machine must be able to enter Acting — a live encounter with a settled Monster (AC-1).
            // A Capture out of sequence (no Lured encounter) is a typed NotSettled failure, NOT the misleading
            // inherited PersistenceFailed+0 (settles the 4.1 out-of-sequence deferral for the Capture path).
            if (!_stateMachine.BeginAction())
            {
                GameLog.Warn(
                    $"EncounterService.TryCaptureAsync ignored — no settled Monster to capture (state: {_stateMachine.State}).");
                return CaptureResult.Failed(CaptureFailureReason.NotSettled, strong, StrongChargeCount());
            }

            // Lured → Acting succeeded; open the commit window (Acting → Resolving), run the rolled write, and
            // settle the resolve. A MISS still CompleteResolves (a resolved "miss" returns to Lured for Retry,
            // AC-3); only a true persist fault FailResolves. Mirrors TryScanAsync's sequencing.
            if (!_stateMachine.BeginResolve())
            {
                // Should be unreachable (we just entered Acting). Reconcile rather than stranding the machine in
                // Acting (a stuck-in-Acting encounter is bricked) — EndEncounter resets to Idle + releases the
                // Lure's pooled spawns (the [[failure-path-cleanup-parity]] discipline, the TryScanAsync precedent).
                GameLog.Error(
                    "EncounterService.TryCaptureAsync: could not begin resolve after entering Acting (unexpected) — " +
                    "ending the encounter to avoid stranding it in Acting.");
                EndEncounter();
                return CaptureResult.Failed(CaptureFailureReason.NotSettled, strong, StrongChargeCount());
            }

            // Roll the success OUTSIDE the locked write (the LureSystem precedent: the system rolls, the service
            // composes the persist with the known outcome). One draw on IRandom against the variant's chance.
            // Story 4.6 (AC-1): an active Stability Boost EASES the roll (a strictly-higher success chance) for
            // the remainder of THIS encounter — pass the in-encounter modifier state INTO the roll (the system
            // stays pure; it does not back-reference the service).
            bool eased = _activeExtras.Contains(ExtraKind.StabilityBoost);
            bool rollSucceeded = _captureSystem.RollCapture(strong, eased);

            CaptureResult result;
            try
            {
                result = await CommitCaptureAsync(monsterId, strong, rollSucceeded).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The composed write throws only for a programmer/system error (an unloaded/corrupt model —
                // RequireModel). We are mid-resolve (BeginResolve succeeded above); rethrowing without settling
                // would strand the machine in Resolving (a bricked encounter — every later BeginAction needs
                // Lured). Reconcile via EndEncounter (Resets to Idle + releases the Lure's pooled spawns) BEFORE
                // propagating — the [[failure-path-cleanup-parity]] discipline, the BeginResolve-failed precedent
                // above. The caller still sees the exception (it IS a programmer error), just not a stranded machine.
                EndEncounter();
                throw;
            }

            // A persist fault (the ONLY false-Success outcome that reached the write) FailResolves; everything
            // else — a capture OR a miss OR a zero-charge block — is a clean resolution back to Lured.
            if (result.Success || result.FailureReason != CaptureFailureReason.PersistenceFailed)
            {
                _stateMachine.CompleteResolve();
            }
            else
            {
                _stateMachine.FailResolve();
            }

            return result;
        }

        /// <summary>The current <see cref="ChargeType.StrongCapture"/> count, or 0 if the model is not loaded
        /// (defensive — used only to populate a typed failure's RemainingCharges on an early-out path).</summary>
        private int StrongChargeCount()
        {
            SaveModel model = _saveService.Current;
            return model == null ? 0 : ChargeInventory.GetCount(model, ChargeType.StrongCapture);
        }

        /// <summary>
        /// The atomic CAPTURE composed write (AR-8) — the FOURTH composed-write sibling (after the 4.1 charge
        /// <see cref="CommitActionAsync"/>, the 4.2 credit <see cref="CommitLureSpendAsync"/>, the 4.3 free
        /// <see cref="CommitScanAsync"/>). Unlike its siblings the delta is ROLL-dependent: it may consume a
        /// Strong charge (Decision A — on the ATTEMPT, win OR miss), and stage a discovery + XP grant ONLY on a
        /// winning roll. Same pipeline shape: acquire the SHARED <see cref="SaveMutationLock"/> once → capture
        /// the model ref once → mutate the staged slices in memory → ONE <c>SaveAsync</c> → recovery-swap guard
        /// → whole-mutation rollback on any fault → release → events after release. NEVER mutates
        /// <c>model.Credits</c> (AC-1/AC-2 — structurally absent here).
        /// <para>
        /// The four outcome shapes:
        /// <list type="bullet">
        /// <item><b>Strong, zero charges</b> → typed <see cref="CaptureFailureReason.InsufficientCharges"/>, no
        ///   mutation, no persist (save-count 0). The "earn via XP" block (AC-2); the count never goes negative.</item>
        /// <item><b>base MISS</b> → nothing staged (no charge, no discovery, no XP) → no persist (save-count 0),
        ///   a <see cref="CaptureResult"/> with <c>Captured=false</c> (free Retry, AC-3).</item>
        /// <item><b>Strong MISS</b> → the charge IS consumed (Decision A) but NO discovery/XP → ONE persist
        ///   (save-count 1) of the charge decrement, <c>Captured=false</c> (free Retry).</item>
        /// <item><b>capture (base or Strong)</b> → (Strong: charge consumed) + Codex discovery + XP grant →
        ///   ONE persist (save-count 1), <c>Captured=true</c>.</item>
        /// </list>
        /// Decision F': a Strong capture both CONSUMES one Strong charge AND may GRANT level-up charges (XP) on
        /// the SAME field — consume FIRST, then stage the XP grant (which reads the post-consume count as its
        /// prior and adds the level-up grants on top). Both roll back together on a fault.
        /// </para>
        /// </summary>
        private async Task<CaptureResult> CommitCaptureAsync(string monsterId, bool strong, bool rollSucceeded)
        {
            CaptureResult result;
            bool committed = false;
            bool chargeConsumed = false;
            int newChargeCount = 0;
            CodexService.CodexStage codexStage = default;
            ProgressionService.ProgressionStage xpStage = default;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Capture the model reference ONCE — a recovery swap mid-persist must never make the rollback
                // write onto a NEW model (the inherited rollback-fidelity discipline).
                SaveModel model = RequireModel();

                int priorChargeCount = ChargeInventory.GetCount(model, ChargeType.StrongCapture);

                if (strong && priorChargeCount == 0)
                {
                    // AC-2 zero-charge block: the "earn via XP" failure. No mutation, no persist, no codex/XP
                    // stage, no event. The only decrement below is guarded by this check, so the count can
                    // never go negative.
                    result = CaptureResult.Failed(CaptureFailureReason.InsufficientCharges, wasStrong: strong, priorChargeCount);
                }
                else if (!strong && !rollSucceeded)
                {
                    // base MISS: nothing changed (free attempt, failed roll → no charge, no discovery, no XP).
                    // Skip the SaveAsync entirely (AR-8 — no write when nothing changed). A free Retry (AC-3).
                    result = CaptureResult.Succeeded(
                        monsterId, captured: false, wasStrong: false, chargeConsumed: false, priorChargeCount);
                }
                else
                {
                    // Something will change → stage the slices, then ONE persist.
                    // (a) Strong always consumes its charge on the ATTEMPT (win OR miss — Decision A). The charge
                    // buys the improved odds, not a guaranteed capture. Set chargeConsumed BEFORE the SetCount so
                    // that even if SetCount itself threw (it should not — the count is guarded >= 0), the catch
                    // block still reverts the charge: the flag that controls rollback must never lag the mutation
                    // it guards (the rollback-fidelity discipline; CR patch).
                    if (strong)
                    {
                        chargeConsumed = true;
                        ChargeInventory.SetCount(model, ChargeType.StrongCapture, priorChargeCount - 1);
                    }

                    // (b) on a WINNING roll: stage the Codex discovery + the XP grant (AC-1/AC-4). On a Strong
                    // MISS neither is staged (only the charge decrement persists). Decision F': the XP stage
                    // reads the POST-consume charge count as its prior, so a level-up grant nets correctly on
                    // top of the consume.
                    if (rollSucceeded)
                    {
                        codexStage = _codexService.StageDiscovery(model, monsterId, DiscoverySource.Capture);
                        xpStage = _progressionService.StageXpGrant(model, _lureSystem.Config.XpPerCapture);
                    }

                    try
                    {
                        // ONE persist for every staged slice (AR-8: never two persists per action).
                        await _saveService.SaveAsync().ConfigureAwait(false);

                        if (ReferenceEquals(_saveService.Current, model))
                        {
                            committed = true;
                            newChargeCount = ChargeInventory.GetCount(model, ChargeType.StrongCapture);
                            result = CaptureResult.Succeeded(
                                monsterId, captured: rollSucceeded, wasStrong: strong, chargeConsumed, newChargeCount);
                        }
                        else
                        {
                            // Recovery swap mid-persist: SaveAsync durably wrote whatever Current pointed at,
                            // NOT this capture. Revert EVERY staged slice onto the captured ref. ORDER IS
                            // LOAD-BEARING for the charge slice (Decision F'): xpStage captured the StrongCapture
                            // count POST-consume (priorChargeCount-1), so xpStage.Revert() writes priorChargeCount-1
                            // back; the explicit restore to priorChargeCount MUST run AFTER it (else the Revert
                            // would overwrite the true pre-action value and leak one charge). So: xpStage.Revert()
                            // FIRST, then the explicit charge restore.
                            xpStage.Revert();
                            codexStage.Revert();
                            if (chargeConsumed)
                            {
                                ChargeInventory.SetCount(model, ChargeType.StrongCapture, priorChargeCount);
                            }

                            GameLog.Error(
                                $"EncounterService: a Capture ({(strong ? "Strong" : "base")} on '{monsterId}') rolled " +
                                "back — the save model was swapped mid-operation (recovery raced a mutation).");
                            result = CaptureResult.Failed(CaptureFailureReason.PersistenceFailed, strong, priorChargeCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Persist fault: revert EVERY staged slice onto the captured ref — never recompute. ORDER
                        // IS LOAD-BEARING (Decision F', see the swap branch above): xpStage.Revert() FIRST
                        // (restores the StrongCapture count to its post-consume snapshot priorChargeCount-1), THEN
                        // the explicit restore to the true pre-action priorChargeCount.
                        xpStage.Revert();
                        codexStage.Revert();
                        if (chargeConsumed)
                        {
                            ChargeInventory.SetCount(model, ChargeType.StrongCapture, priorChargeCount);
                        }

                        GameLog.Error(
                            $"EncounterService: a Capture ({(strong ? "Strong" : "base")} on '{monsterId}') rolled " +
                            $"back — persist failed. {ex.Message}");
                        result = CaptureResult.Failed(CaptureFailureReason.PersistenceFailed, strong, priorChargeCount);
                    }
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            // Events AFTER the lock releases, ONLY on a committed capture (a miss/block commits no events). The
            // discovery + XP/level/charge events live with their owning services (raised via the staged handles).
            if (committed)
            {
                // Raise the StrongCapture charges-changed event for the consume — BUT NOT if the XP stage already
                // changed (and will raise) the StrongCapture count via a level-up grant: on a Strong WIN that
                // crosses a threshold, the level-up grant nets against the consume on the SAME field, so
                // xpStage.RaiseCommittedEvents() below already reports the final StrongCapture value. Without this
                // guard a HUD would see the SAME value twice (CR patch — the duplicate-event fix). When the XP
                // stage did NOT touch StrongCapture (a Strong miss, or a win with no level-up), this explicit
                // raise is the only one and is required.
                if (chargeConsumed && !xpStage.ChargesChangedFor(ChargeType.StrongCapture))
                {
                    RaiseChargesChanged(ChargeType.StrongCapture, newChargeCount);
                }

                codexStage.RaiseCommittedEvents();
                xpStage.RaiseCommittedEvents();
            }

            return result;
        }

        // ---- FR-9 (Story 4.5): Slay — a 3-Credit action for superior loot (more XP than Capture) ----

        /// <summary>
        /// Slay the settled Monster <paramref name="monsterId"/> (Story 4.5, FR-9). A Slay costs
        /// <see cref="EconomyConfig.SlayCost"/> Credits (canon 3, surfaced on <see cref="SlayResult.Cost"/> so
        /// the HUD shows it before the spend — AC-1). The outcome is a ROLL (<see cref="SlaySystem"/>): a
        /// SUCCESS deducts exactly 3 Credits, records the <c>Slain</c> Codex discovery (X/67 +1 if newly
        /// discovered), AND grants <see cref="EconomyConfig.XpPerSlay"/> XP — STRICTLY more than Capture
        /// (FR-9) — all committed in ONE atomic save (AR-8). A MISS deducts NOTHING (no Credits lost), records
        /// nothing, and leaves the encounter live for a FREE Retry (just call this again — AC-3).
        /// <para>
        /// <b>The spend is ROLL-GATED (Decision C).</b> The 3 Credits are deducted ONLY on a winning roll —
        /// the deduction + discovery + XP are ONE write that runs only on a success (a miss is save-count 0).
        /// This is the only reading consistent with BOTH "no Credits lost on a failed Slay" AND "Retry is free"
        /// (a per-attempt charge would make Retry cost Credits). architecture.md:648 — the spend + resolution
        /// commit as ONE write ("prevents free-Slay via two separate persists").
        /// </para>
        /// <para>
        /// Returns a typed <see cref="SlayResult"/> — never throws for an expected outcome (a miss, an
        /// insufficient-credits block, an out-of-sequence call, a persist fault are all reported on the result,
        /// AR-7); throws only for a null/empty/invalid monster id (programmer error). <see cref="SlayResult.Success"/>
        /// = "the attempt ran"; <see cref="SlayResult.Slain"/> = the roll outcome (slain vs missed).
        /// </para>
        /// <para>
        /// Drives the in-encounter loop (Lured → Acting → Resolving → Lured) — modelled on
        /// <see cref="TryCaptureAsync"/> (it calls <c>BeginAction</c> itself then <c>BeginResolve</c>), NOT the
        /// inherited <see cref="RunActionAsync"/> (which assumes the caller already drove Lured → Acting and
        /// returns the misleading <c>PersistenceFailed</c>+0 on its out-of-sequence branch). A MISS is a
        /// successfully RESOLVED action (it <c>CompleteResolve</c>s back to Lured), not a state-machine failure.
        /// </para>
        /// </summary>
        public async Task<SlayResult> TrySlayAsync(string monsterId)
        {
            int cost = _slaySystem.Cost;

            // Validate the FULL id up front (programmer error — same contract as TryCaptureAsync/TryScanAsync)
            // BEFORE touching the state machine, so a bad id never leaves the machine stranded mid-resolve and
            // an invalid id throws regardless of the roll outcome.
            if (string.IsNullOrEmpty(monsterId) || !MonsterDatabase.IsValidMonsterId(monsterId))
            {
                throw new ArgumentException(
                    $"'{monsterId}' is not a valid Monster id (expected mon01..mon{MonsterDatabase.UniverseCount:00}).",
                    nameof(monsterId));
            }

            // The state machine must be able to enter Acting — a live encounter with a settled Monster (AC-1).
            // A Slay out of sequence (no Lured encounter) is a typed NotSettled failure, NOT the misleading
            // inherited PersistenceFailed+0 (settles the 4.1 out-of-sequence deferral for the Slay path).
            if (!_stateMachine.BeginAction())
            {
                GameLog.Warn(
                    $"EncounterService.TrySlayAsync ignored — no settled Monster to slay (state: {_stateMachine.State}).");
                return SlayResult.Failed(SlayFailureReason.NotSettled, cost, CurrentBalance());
            }

            // Lured → Acting succeeded; open the commit window (Acting → Resolving), run the rolled write, and
            // settle the resolve. A MISS still CompleteResolves (a resolved "miss" returns to Lured for Retry,
            // AC-3); only a true persist fault FailResolves. Mirrors TryCaptureAsync's sequencing.
            if (!_stateMachine.BeginResolve())
            {
                // Should be unreachable (we just entered Acting). Reconcile rather than stranding the machine in
                // Acting (a stuck-in-Acting encounter is bricked) — EndEncounter resets to Idle + releases the
                // Lure's pooled spawns (the [[failure-path-cleanup-parity]] discipline, the TryCaptureAsync precedent).
                GameLog.Error(
                    "EncounterService.TrySlayAsync: could not begin resolve after entering Acting (unexpected) — " +
                    "ending the encounter to avoid stranding it in Acting.");
                EndEncounter();
                return SlayResult.Failed(SlayFailureReason.NotSettled, cost, CurrentBalance());
            }

            // Roll the success OUTSIDE the locked write (the CaptureSystem/LureSystem precedent: the system
            // rolls, the service composes the persist with the known outcome). One draw on IRandom. Story 4.6
            // (AC-1): an active Stability Boost EASES the roll (a strictly-higher success chance) for the
            // remainder of THIS encounter — the in-encounter modifier state is passed INTO the roll.
            bool eased = _activeExtras.Contains(ExtraKind.StabilityBoost);
            bool rollSucceeded = _slaySystem.RollSlay(eased);

            SlayResult result;
            try
            {
                result = await CommitSlayAsync(monsterId, cost, rollSucceeded).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The composed write throws only for a programmer/system error (an unloaded/corrupt model —
                // RequireModel). We are mid-resolve (BeginResolve succeeded above); rethrowing without settling
                // would strand the machine in Resolving (a bricked encounter). Reconcile via EndEncounter (Resets
                // to Idle + releases the Lure's pooled spawns) BEFORE propagating — the [[failure-path-cleanup-parity]]
                // discipline, the TryCaptureAsync precedent. The caller still sees the exception (it IS a programmer
                // error), just not a stranded machine.
                EndEncounter();
                throw;
            }

            // A persist fault (the ONLY false-Success outcome that reached the write) FailResolves; everything
            // else — a slay OR a miss OR an insufficient-credits block — is a clean resolution back to Lured.
            if (result.Success || result.FailureReason != SlayFailureReason.PersistenceFailed)
            {
                _stateMachine.CompleteResolve();
            }
            else
            {
                _stateMachine.FailResolve();
            }

            // AC-1: surface the rejected spend at the encounter altitude (AR-11 — the service NEVER opens the
            // Shop). Raised AFTER the resolve settles so the machine is in a consistent state for the subscriber.
            // Slay is the SECOND credit-spending caller of this seam (after the 4.2 Lure).
            if (!result.Success && result.FailureReason == SlayFailureReason.InsufficientCredits)
            {
                RaiseInsufficientCredits(new InsufficientCreditsEvent(cost, result.NewBalance));
            }

            return result;
        }

        /// <summary>
        /// The atomic SLAY composed write (AR-8) — the FIFTH composed-write sibling (after the 4.1 charge
        /// <see cref="CommitActionAsync"/>, the 4.2 credit <see cref="CommitLureSpendAsync"/>, the 4.3 free
        /// <see cref="CommitScanAsync"/>, the 4.4 roll-aware <see cref="CommitCaptureAsync"/>). Its delta —
        /// CREDITS + codex (<c>Slain</c>) + XP (<see cref="EconomyConfig.XpPerSlay"/>) — matches no other
        /// sibling (Lure spends credits but does not discover; Capture consumes a CHARGE, not Credits), so Slay
        /// gets its own write (this settles the 4.4 "Slay decides whether to fold into RunActionAsync"
        /// deferral: NO — <see cref="CommitActionAsync"/> consumes a charge + grants no XP). Same pipeline shape:
        /// acquire the SHARED <see cref="SaveMutationLock"/> once → capture the model ref once → mutate the
        /// staged slices in memory → ONE <c>SaveAsync</c> → recovery-swap guard → whole-mutation rollback on any
        /// fault → release → events after release.
        /// <para>
        /// The roll-gated outcome shapes (Decision C):
        /// <list type="bullet">
        /// <item><b>MISS</b> → nothing staged (no deduction, no discovery, no XP) → no persist (save-count 0), a
        ///   <see cref="SlayResult"/> with <c>Slain=false</c> and the UNCHANGED balance (free Retry, AC-3 — no
        ///   Credits lost).</item>
        /// <item><b>HIT, insufficient Credits</b> → typed <see cref="SlayFailureReason.InsufficientCredits"/>,
        ///   no mutation, no persist (save-count 0); the public method raises <see cref="OnInsufficientCredits"/>.</item>
        /// <item><b>HIT, affordable</b> → deduct 3 Credits + Codex <c>Slain</c> discovery + XP grant → ONE
        ///   persist (save-count 1), <c>Slain=true</c>.</item>
        /// </list>
        /// On a fault, every staged slice is reverted onto the captured ref. The Credit slice and the XP-grant
        /// slice touch DISJOINT fields (<c>Credits</c> vs <c>Xp</c>/charges) — <see cref="ProgressionService.StageXpGrant"/>
        /// only ever GRANTS charges, never touches Credits — so there is NO load-bearing revert ordering here
        /// (unlike Capture's Decision F', where the XP stage and an explicit charge consume shared the same
        /// field). All three are still reverted.
        /// </para>
        /// </summary>
        private async Task<SlayResult> CommitSlayAsync(string monsterId, int cost, bool rollSucceeded)
        {
            SlayResult result;
            bool committed = false;
            int priorCredits = 0;
            bool creditsDeducted = false;
            CodexService.CodexStage codexStage = default;
            ProgressionService.ProgressionStage xpStage = default;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Capture the model reference ONCE — a recovery swap mid-persist must never make the rollback
                // write onto a NEW model (the inherited rollback-fidelity discipline).
                SaveModel model = RequireModel();
                priorCredits = model.Credits;

                if (!rollSucceeded)
                {
                    // MISS: nothing changes (the spend is roll-gated — no deduction, no discovery, no XP). Skip
                    // the SaveAsync entirely (AR-8 — no write when nothing changed). A free Retry, no Credits
                    // lost (AC-3). The base-Capture-miss precedent (CommitCaptureAsync).
                    result = SlayResult.Succeeded(monsterId, slain: false, cost, priorCredits);
                }
                else if (priorCredits < cost)
                {
                    // HIT but can't afford: the expected "not enough Credits" block (the >= rule, the
                    // CommitLureSpendAsync precedent). No mutation, no persist; the public method raises
                    // OnInsufficientCredits (AR-11). The unchanged balance is reported so the event payload is
                    // accurate, and the encounter stays live for a Retry once the player tops up.
                    result = SlayResult.Failed(SlayFailureReason.InsufficientCredits, cost, priorCredits);
                }
                else
                {
                    // HIT and affordable → stage all three slices, then ONE persist.
                    // (a) credit slice: deduct in memory (exact-balance OK: priorCredits == cost → 0, the >= rule).
                    // Set the flag BEFORE the mutation so the catch/swap revert never lags the mutation it guards
                    // (the rollback-fidelity discipline, the CommitCaptureAsync charge-flag precedent).
                    creditsDeducted = true;
                    model.Credits = priorCredits - cost;

                    // (b) codex slice: stage the Slain discovery (lock-free, persist-free — the composed write
                    // owns the persist). DiscoverySource.Slay sets the Slain flag (NOT Captured).
                    codexStage = _codexService.StageDiscovery(model, monsterId, DiscoverySource.Slay);

                    // (c) XP slice: stage the Slay XP grant (XpPerSlay > XpPerCapture — FR-9). Read XpPerSlay from
                    // the shared EconomyConfig (the CommitCaptureAsync precedent reads XpPerCapture via
                    // _lureSystem.Config). Folded into THIS write — never a second AddXpAsync save (AR-8).
                    xpStage = _progressionService.StageXpGrant(model, _lureSystem.Config.XpPerSlay);

                    try
                    {
                        // ONE persist for every staged slice (AR-8: never two persists per action — the
                        // load-bearing "no free-Slay via two separate persists", architecture.md:648).
                        await _saveService.SaveAsync().ConfigureAwait(false);

                        if (ReferenceEquals(_saveService.Current, model))
                        {
                            committed = true;
                            result = SlayResult.Succeeded(monsterId, slain: true, cost, model.Credits);
                        }
                        else
                        {
                            // Recovery swap mid-persist: SaveAsync durably wrote whatever Current pointed at,
                            // NOT this slay. Revert EVERY staged slice onto the captured ref. The slices touch
                            // DISJOINT fields (Credits vs Xp/charges), so there is no load-bearing ordering —
                            // but revert all three.
                            xpStage.Revert();
                            codexStage.Revert();
                            if (creditsDeducted)
                            {
                                model.Credits = priorCredits;
                            }

                            GameLog.Error(
                                $"EncounterService: a Slay of '{monsterId}' ({cost} Credits) rolled back — the save " +
                                "model was swapped mid-operation (recovery raced a mutation).");
                            result = SlayResult.Failed(SlayFailureReason.PersistenceFailed, cost, priorCredits);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Persist fault: revert EVERY staged slice onto the captured ref — never recompute.
                        xpStage.Revert();
                        codexStage.Revert();
                        if (creditsDeducted)
                        {
                            model.Credits = priorCredits;
                        }

                        GameLog.Error(
                            $"EncounterService: a Slay of '{monsterId}' ({cost} Credits) rolled back — persist failed. {ex.Message}");
                        result = SlayResult.Failed(SlayFailureReason.PersistenceFailed, cost, priorCredits);
                    }
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            // Events AFTER the lock releases, ONLY on a committed slay (a miss/block commits no events). The
            // discovery + XP/level/charge events live with their owning services (raised via the staged handles).
            // Slay consumes NO charge (it spends Credits), so there is no explicit RaiseChargesChanged and no
            // duplicate-event guard (unlike Capture's Decision F') — any charge change is a pure level-up grant
            // carried by xpStage.RaiseCommittedEvents().
            if (committed)
            {
                codexStage.RaiseCommittedEvents();
                xpStage.RaiseCommittedEvents();
            }

            return result;
        }

        // ---- FR-10 (Story 4.6): apply an encounter extra — Stability Boost / Nightveil Filter ----

        /// <summary>
        /// Apply an encounter extra (Story 4.6, FR-10): Stability Boost (raises Scan/Capture/Slay ease — AC-1) or
        /// Nightveil Filter (flags the atmospheric visual filter + raises Lure rarity — AC-2) for the REMAINDER
        /// of the current encounter. Consumes EXACTLY one charge of the extra's type (never Credits; blocked at
        /// zero charges; never negative) and records the in-encounter modifier, committing the charge decrement
        /// in ONE atomic save (AR-8). Returns a typed <see cref="ApplyExtraResult"/> — never throws for an
        /// expected outcome (a zero-charge block, an out-of-sequence call, a persist fault are all reported on
        /// the result, AR-7); throws only for an UNDEFINED <paramref name="kind"/> (programmer error — note that
        /// <c>StrongCapture</c> has no <see cref="ExtraKind"/> member, so it cannot be passed here at all).
        /// <para>
        /// <b>The SIXTH atomic-write sibling (charge-only, no discovery).</b> Unlike Capture (charge + codex + XP)
        /// the extras consume a charge and discover NOTHING — so they do NOT compose <see cref="CommitActionAsync"/>
        /// (which always stages a discovery). The composed write (<see cref="CommitApplyExtraAsync"/>) reuses the
        /// <see cref="CommitCaptureAsync"/> charge-consume slice + the <see cref="CommitScanAsync"/> in-encounter
        /// record. Extras spend a CHARGE, never Credits — so there is NO <see cref="OnInsufficientCredits"/> raise
        /// (unlike Slay/Lure); the zero-charge block is surfaced via the typed result ONLY (Epic 6 binds the
        /// "earn via XP" hint to <see cref="ApplyExtraFailureReason.InsufficientCharges"/>).
        /// </para>
        /// <para>
        /// <b>AC-4 (mid-encounter grant usable immediately).</b> The charge count is read LIVE from the model
        /// inside the locked write (the <see cref="CommitCaptureAsync"/> posture) — it is NEVER snapshotted at
        /// encounter start, so a charge granted by a mid-encounter level-up (a Capture/Slay XP grant that crosses
        /// a threshold) is immediately spendable without ending/re-entering the encounter.
        /// </para>
        /// <para>
        /// Drives the in-encounter loop (Lured → Acting → Resolving → Lured) — modelled on
        /// <see cref="TryScanAsync"/>/<see cref="TrySlayAsync"/> (it calls <c>BeginAction</c> itself then
        /// <c>BeginResolve</c>), NOT the inherited <see cref="RunActionAsync"/> (which returns the misleading
        /// <c>PersistenceFailed</c>+0 out of sequence). A zero-charge block is a CLEAN resolution (it
        /// <c>CompleteResolve</c>s back to Lured); only a true persist fault <c>FailResolve</c>s.
        /// </para>
        /// </summary>
        public async Task<ApplyExtraResult> TryApplyExtraAsync(ExtraKind kind)
        {
            // Map the extra to its charge type UP FRONT (programmer-error contract — an undefined ExtraKind
            // throws ArgumentOutOfRangeException via ExtrasSystem) BEFORE touching the state machine, so a bad
            // enum never leaves the machine stranded mid-resolve (the TryCaptureAsync/TrySlayAsync up-front
            // validation precedent). StrongCapture cannot reach here — it has no ExtraKind member.
            ChargeType chargeType = ExtrasSystem.ChargeTypeOf(kind);

            // The state machine must be able to enter Acting — a live encounter with a settled Monster (AC-1).
            // An apply out of sequence (no Lured encounter) is a typed NotSettled failure, NOT the misleading
            // inherited PersistenceFailed+0 (settles the 4.1 out-of-sequence deferral for the extras path).
            if (!_stateMachine.BeginAction())
            {
                GameLog.Warn(
                    $"EncounterService.TryApplyExtraAsync ignored — no live encounter to apply {kind} to (state: {_stateMachine.State}).");
                return ApplyExtraResult.Failed(ApplyExtraFailureReason.NotSettled, kind, ChargeCount(chargeType));
            }

            // Lured → Acting succeeded; open the commit window (Acting → Resolving), run the charge-only write,
            // and settle the resolve. A zero-charge BLOCK still CompleteResolves (a resolved block returns to
            // Lured); only a true persist fault FailResolves. Mirrors TryScanAsync/TrySlayAsync sequencing.
            if (!_stateMachine.BeginResolve())
            {
                // Should be unreachable (we just entered Acting). Reconcile rather than stranding the machine in
                // Acting (a stuck-in-Acting encounter is bricked) — EndEncounter resets to Idle + releases the
                // Lure's pooled spawns (the [[failure-path-cleanup-parity]] discipline, the TrySlayAsync precedent).
                GameLog.Error(
                    "EncounterService.TryApplyExtraAsync: could not begin resolve after entering Acting (unexpected) — " +
                    "ending the encounter to avoid stranding it in Acting.");
                EndEncounter();
                return ApplyExtraResult.Failed(ApplyExtraFailureReason.NotSettled, kind, ChargeCount(chargeType));
            }

            ApplyExtraResult result;
            try
            {
                result = await CommitApplyExtraAsync(kind, chargeType).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The composed write throws only for a programmer/system error (an unloaded/corrupt model —
                // RequireModel). We are mid-resolve (BeginResolve succeeded above); rethrowing without settling
                // would strand the machine in Resolving (a bricked encounter). Reconcile via EndEncounter (Resets
                // to Idle + releases the Lure's pooled spawns) BEFORE propagating — the [[failure-path-cleanup-parity]]
                // discipline, the TrySlayAsync precedent. The caller still sees the exception (a programmer error).
                EndEncounter();
                throw;
            }

            // A persist fault (the ONLY false-Success outcome that reached the write) FailResolves; everything
            // else — a clean apply OR a zero-charge block — is a clean resolution back to Lured.
            if (result.Success || result.FailureReason != ApplyExtraFailureReason.PersistenceFailed)
            {
                _stateMachine.CompleteResolve();
            }
            else
            {
                _stateMachine.FailResolve();
            }

            return result;
        }

        /// <summary>The current charge count of <paramref name="type"/>, or 0 if the model is not loaded
        /// (defensive — used only to populate a typed failure's RemainingCharges on an early-out path).</summary>
        private int ChargeCount(ChargeType type)
        {
            SaveModel model = _saveService.Current;
            return model == null ? 0 : ChargeInventory.GetCount(model, type);
        }

        /// <summary>
        /// The atomic APPLY-EXTRA composed write (AR-8) — the SIXTH composed-write sibling (after the 4.1 charge
        /// <see cref="CommitActionAsync"/>, the 4.2 credit <see cref="CommitLureSpendAsync"/>, the 4.3 free
        /// <see cref="CommitScanAsync"/>, the 4.4 roll-aware <see cref="CommitCaptureAsync"/>, the 4.5 roll-gated
        /// <see cref="CommitSlayAsync"/>). Its delta — ONE charge consumed (no Credits, no codex, no XP) PLUS an
        /// in-memory per-encounter modifier (<see cref="_activeExtras"/>) — matches no other sibling, so it gets
        /// its own write. Same pipeline shape: acquire the SHARED <see cref="SaveMutationLock"/> once → capture
        /// the model ref once → mutate the slices in memory → ONE <c>SaveAsync</c> → recovery-swap guard →
        /// whole-mutation rollback on any fault → release → the charges-changed event after release.
        /// <para>
        /// The outcome shapes:
        /// <list type="bullet">
        /// <item><b>zero charges</b> → typed <see cref="ApplyExtraFailureReason.InsufficientCharges"/>, no charge
        ///   decrement, no modifier record, no persist (save-count 0). The "earn via XP" block (AC-3); the count
        ///   never goes negative (the only decrement is gated by this check).</item>
        /// <item><b>≥ 1 charge</b> → consume one charge + record the in-encounter modifier → ONE persist
        ///   (save-count 1) of the charge decrement, the modifier active for the rest of the encounter.</item>
        /// </list>
        /// On a fault BOTH slices are reverted onto the captured ref: the in-encounter modifier
        /// (<c>_activeExtras.Remove(kind)</c> if THIS call added it — a single <c>addedModifier</c> local for both
        /// fault branches, the <see cref="CommitScanAsync"/> <c>addedToEncounter</c> precedent) AND the charge
        /// (<c>ChargeInventory.SetCount(..., priorChargeCount)</c> if consumed). The charge slice (persisted
        /// <c>SaveModel</c> field) and the modifier slice (in-memory <see cref="_activeExtras"/>) touch DISJOINT
        /// state, so there is NO load-bearing revert ordering (unlike Capture's Decision F') — but revert BOTH.
        /// The charges-changed event fires AFTER release ONLY on a committed success; an extra stages NO XP, so
        /// there is no <c>ChargesChangedFor</c> duplicate-event guard (unlike Capture's Decision F').
        /// </para>
        /// </summary>
        private async Task<ApplyExtraResult> CommitApplyExtraAsync(ExtraKind kind, ChargeType chargeType)
        {
            ApplyExtraResult result;
            bool committed = false;
            bool chargeConsumed = false;
            int newChargeCount = 0;
            // Whether THIS call added the modifier to the encounter set — the single source of truth for the
            // rollback (both the swap branch AND the catch revert exactly what this call mutated). Declared out
            // here so BOTH fault branches use the SAME variable, not two equivalent expressions (the CommitScanAsync
            // `addedToEncounter` precedent — [[failure-path-cleanup-parity]]: revert provably mirrors the add).
            bool addedModifier = false;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Capture the model reference ONCE — a recovery swap mid-persist must never make the rollback
                // write onto a NEW model (the inherited rollback-fidelity discipline).
                SaveModel model = RequireModel();

                // AC-4: read the charge count LIVE inside the locked write (NEVER a snapshot cached at encounter
                // start) — this is what makes a mid-encounter level-up grant immediately spendable.
                int priorChargeCount = ChargeInventory.GetCount(model, chargeType);

                if (priorChargeCount == 0)
                {
                    // AC-3 zero-charge block: the "earn via XP" failure. No charge decrement, no modifier record,
                    // no persist (save-count 0). The only decrement below is guarded by this check, so the count
                    // can never go negative. Surfaced via the typed result only (no Credits → no OnInsufficientCredits).
                    result = ApplyExtraResult.Failed(ApplyExtraFailureReason.InsufficientCharges, kind, priorChargeCount);
                }
                else
                {
                    // Both slices change → stage them, then ONE persist.
                    // (a) charge slice: consume one charge. Set chargeConsumed BEFORE the SetCount so even if
                    // SetCount itself threw (it should not — the count is guarded >= 0), the catch still reverts
                    // the charge: the flag controlling rollback must never lag the mutation it guards (the
                    // rollback-fidelity discipline, the CommitCaptureAsync charge-flag precedent).
                    chargeConsumed = true;
                    ChargeInventory.SetCount(model, chargeType, priorChargeCount - 1);

                    // (b) modifier slice: record the in-encounter modifier BEFORE the persist so a swap/throw
                    // rolls it back alongside the charge (the CommitScanAsync `_scannedThisEncounter.Add` precedent).
                    // Add returns false if the modifier was already active (a re-apply) — addedModifier then stays
                    // false so the rollback does not remove a modifier a PRIOR apply established.
                    addedModifier = _activeExtras.Add(kind);

                    try
                    {
                        // ONE persist for the charge slice (AR-8: never two persists per action). The modifier
                        // slice is in-memory only this story (the disk snapshot is Story 5.4).
                        await _saveService.SaveAsync().ConfigureAwait(false);

                        if (ReferenceEquals(_saveService.Current, model))
                        {
                            committed = true;
                            // Use the KNOWN computed post-consume value (priorChargeCount - 1), not a re-read via
                            // GetCount — there is no XP stage here to touch the charge after SetCount (Decision C',
                            // disjoint slices), so the value is deterministic, and using it matches the failure
                            // branches' "never recompute" discipline (they restore priorChargeCount directly). CR patch.
                            newChargeCount = priorChargeCount - 1;
                            result = ApplyExtraResult.Succeeded(kind, newChargeCount);
                        }
                        else
                        {
                            // Recovery swap mid-persist: SaveAsync durably wrote whatever Current pointed at, NOT
                            // this apply. Revert BOTH slices onto the captured ref (disjoint state — no ordering).
                            if (addedModifier)
                            {
                                _activeExtras.Remove(kind);
                            }

                            if (chargeConsumed)
                            {
                                ChargeInventory.SetCount(model, chargeType, priorChargeCount);
                            }

                            GameLog.Error(
                                $"EncounterService: applying {kind} rolled back — the save model was swapped " +
                                "mid-operation (recovery raced a mutation).");
                            result = ApplyExtraResult.Failed(ApplyExtraFailureReason.PersistenceFailed, kind, priorChargeCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Persist fault: revert BOTH slices onto the captured ref — never recompute. Use the SAME
                        // addedModifier/chargeConsumed locals as the swap branch so the revert provably mirrors the
                        // mutation (the charge and the modifier commit together or not at all — the extras-specific
                        // [[failure-path-cleanup-parity]] bug class: never a modifier-active-but-charge-restored,
                        // never a charge-spent-but-modifier-not-recorded).
                        if (addedModifier)
                        {
                            _activeExtras.Remove(kind);
                        }

                        if (chargeConsumed)
                        {
                            ChargeInventory.SetCount(model, chargeType, priorChargeCount);
                        }

                        GameLog.Error(
                            $"EncounterService: applying {kind} rolled back — persist failed. {ex.Message}");
                        result = ApplyExtraResult.Failed(ApplyExtraFailureReason.PersistenceFailed, kind, priorChargeCount);
                    }
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            // Events AFTER the lock releases, ONLY on a committed apply (a block commits no events). An extra
            // consumes a charge directly and stages NO XP, so there is no ChargesChangedFor duplicate-event guard
            // (unlike Capture's Decision F') — this explicit raise is the only charges-changed signal.
            if (committed)
            {
                RaiseChargesChanged(chargeType, newChargeCount);
            }

            return result;
        }

        // ---- AC-2: the atomic multi-delta write primitive (the generic charge+discovery action) ----
        // NOTE (settled by Story 4.6): the two NON-capture extras (Stability Boost, Nightveil Filter) do NOT
        // compose this primitive. CommitActionAsync ALWAYS stages a Codex StageDiscovery, but an extra consumes a
        // charge and discovers NOTHING (there is no "no-discovery" DiscoverySource, and an extra must never
        // discover a Monster as a side effect of applying a Boost). So the extras get their OWN composed write
        // (CommitApplyExtraAsync — charge-only, no codex/XP, plus an in-encounter modifier). Strong Capture's
        // charge already flows through the 4.4 Capture path, so no 4.6 caller composes RunActionAsync; it remains
        // the generic charge+discovery primitive (Slay 4.5 retired its use too — see CommitSlayAsync).

        /// <summary>
        /// Run a composed action as the encounter state machine expects (Acting → Resolving → Acting): begin
        /// resolve (opens the AR-8 commit window), run the atomic write, then complete-or-fail the resolve so
        /// the machine returns to a live state — and an AR interruption that arrived DURING the write is
        /// applied now (decision H2, honored by <see cref="EncounterStateMachine.CompleteResolve"/>/
        /// <see cref="EncounterStateMachine.FailResolve"/>). The caller must be in <see cref="EncounterState.Acting"/>
        /// (i.e. inside a live encounter mid-action). The remaining FR-9–10 action flows (Slay 4.5, extras 4.6)
        /// compose this generic charge+discovery primitive.
        /// </summary>
        public async Task<SpendResult> RunActionAsync(string monsterId, ChargeType chargeType, DiscoverySource via)
        {
            if (!_stateMachine.BeginResolve())
            {
                // Not in Acting — the caller must drive Lured → Acting first. A warned no-op (the machine
                // already logged); return a typed failure rather than running a write out of sequence.
                return SpendResult.Failed(SpendFailureReason.PersistenceFailed, 0);
            }

            SpendResult result = await CommitActionAsync(monsterId, chargeType, via).ConfigureAwait(false);

            // Settle the resolve: the machine returns to Lured (or applies a deferred suspend — decision H2).
            if (result.Success)
            {
                _stateMachine.CompleteResolve();
            }
            else
            {
                _stateMachine.FailResolve();
            }

            return result;
        }

        // ---- FR-7 (Story 4.3): Scan — a FREE in-encounter action (no Credits, no charges) ----

        /// <summary>
        /// Scan the settled Monster <paramref name="monsterId"/> (Story 4.3, FR-7) — a FREE action: it records
        /// partial Codex progress and NEVER changes the Credit balance or any charge (AC-3). Drives the
        /// in-encounter loop (Lured → Acting → Resolving → Lured) around ONE atomic save write (AR-8), and
        /// returns a typed <see cref="ScanResult"/> — never throws for an expected failure (AR-7); throws only
        /// for a null/empty id (programmer error).
        /// <para>
        /// <b>Scan ≠ discover (Decision B1).</b> A Scan records the Monster's scan progress in the encounter
        /// state (<see cref="_scannedThisEncounter"/>) — so partial data is recorded EVEN before a Capture/Slay
        /// discovery (AC-2) WITHOUT creating a Codex key / inflating the X/67 count — and additionally flips the
        /// persistent <see cref="CodexEntryData.Scanned"/> flag iff the Monster is ALREADY discovered
        /// (<see cref="CodexService.StageScan"/>). It raises NO discovery event.
        /// </para>
        /// <para>
        /// <b>Idempotent re-scan.</b> Scanning a Monster already scanned THIS encounter is a pure no-op (no
        /// persist) returning success with <see cref="ScanResult.AlreadyScanned"/> = true — the player may
        /// re-scan freely; it records nothing new and never touches Credits.
        /// </para>
        /// </summary>
        public async Task<ScanResult> TryScanAsync(string monsterId)
        {
            // Validate the FULL id up front (programmer error — same contract as TryCaptureAsync/StageScan)
            // BEFORE touching the state machine, so a bad id never leaves the machine stranded mid-resolve.
            if (string.IsNullOrEmpty(monsterId) || !MonsterDatabase.IsValidMonsterId(monsterId))
            {
                throw new ArgumentException(
                    $"'{monsterId}' is not a valid Monster id (expected mon01..mon{MonsterDatabase.UniverseCount:00}).",
                    nameof(monsterId));
            }

            // The state machine must be able to enter Acting — i.e. a live encounter with a settled Monster
            // (AC-1). A Scan out of sequence (no Lured encounter) is a typed NotSettled failure, NOT the
            // misleading inherited PersistenceFailed+0 (deferred-work.md:21 — 4.3 gives Scan a distinct reason).
            if (!_stateMachine.BeginAction())
            {
                GameLog.Warn(
                    $"EncounterService.TryScanAsync ignored — no settled Monster to scan (state: {_stateMachine.State}).");
                return ScanResult.Failed(ScanFailureReason.NotSettled);
            }

            // Lured → Acting succeeded above; now open the commit window (Acting → Resolving), run the free
            // write, and settle the resolve (Resolving → Lured, or a deferred suspend — decision H2). Mirrors
            // RunActionAsync, but for a charge-free / discovery-free action.
            if (!_stateMachine.BeginResolve())
            {
                // Should be unreachable (we just entered Acting → BeginResolve from Acting cannot fail in
                // single-threaded gameplay). But RECONCILE rather than stranding the machine in Acting — a
                // stuck-in-Acting encounter is bricked (every later BeginAction needs Lured). FailResolve only
                // works from Resolving (we are in Acting), so the safe abort is the full teardown EndEncounter():
                // it Resets the machine to Idle AND releases the active Lure's pooled spawns + clears the
                // encounter scan-state (no leak), the [[failure-path-cleanup-parity]] discipline.
                GameLog.Error(
                    "EncounterService.TryScanAsync: could not begin resolve after entering Acting (unexpected) — " +
                    "ending the encounter to avoid stranding it in Acting.");
                EndEncounter();
                return ScanResult.Failed(ScanFailureReason.NotSettled);
            }

            ScanResult result = await CommitScanAsync(monsterId).ConfigureAwait(false);

            if (result.Success)
            {
                _stateMachine.CompleteResolve();
            }
            else
            {
                _stateMachine.FailResolve();
            }

            return result;
        }

        /// <summary>
        /// The atomic SCAN composed write (AR-8) — the FREE sibling of <see cref="CommitActionAsync"/>
        /// (charge) / <see cref="CommitLureSpendAsync"/> (credits): it spends NEITHER. Same pipeline shape:
        /// acquire the SHARED <see cref="SaveMutationLock"/> once (a Scan still mutates the shared
        /// <see cref="SaveModel"/>, so it MUST serialize) → capture the model ref once → stage the scan delta
        /// (the in-encounter progress record + <see cref="CodexService.StageScan"/> for the discovered-only
        /// persistent flag) → ONE <c>SaveAsync</c> → recovery-swap guard → whole-mutation rollback on any
        /// fault → release. NEVER mutates <c>model.Credits</c> or any charge (AC-3 — structurally absent here).
        /// <para>
        /// A pure no-op (already scanned this encounter AND no new persistent flag to set) skips the persist
        /// entirely (save-count 0) and returns success with <see cref="ScanResult.AlreadyScanned"/> = true.
        /// </para>
        /// </summary>
        private async Task<ScanResult> CommitScanAsync(string monsterId)
        {
            CodexService.ScanStage stage = default;
            bool alreadyScannedThisEncounter = _scannedThisEncounter.Contains(monsterId);
            // Whether THIS call added the monster to the encounter scan-set — the single source of truth for the
            // rollback (both the swap branch AND the catch must revert exactly what this call mutated). Declared
            // out here so BOTH fault branches use the SAME variable, not two equivalent expressions (CR patch —
            // [[failure-path-cleanup-parity]]: revert provably mirrors the add).
            bool addedToEncounter = false;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();

                // Stage the persistent flag (discovered-only; a no-op for an undiscovered or already-scanned
                // entry — it never creates a key, so X/67 is never inflated). Validates the id (throws on a
                // bad id, like RecordDiscoveryAsync — a programmer error).
                stage = _codexService.StageScan(model, monsterId);

                // The pure no-op: already recorded in the encounter AND nothing new to persist (no flag flip).
                // Skip the SaveAsync entirely (AR-8 — no write when nothing changed; the idempotent re-scan).
                if (alreadyScannedThisEncounter && !stage.Applied)
                {
                    return ScanResult.Succeeded(monsterId, alreadyScanned: true);
                }

                try
                {
                    // ONE persist for the scan delta (AR-8). The in-encounter progress is recorded BEFORE the
                    // persist so a swap/throw rolls it back alongside the codex flag.
                    addedToEncounter = _scannedThisEncounter.Add(monsterId);

                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (ReferenceEquals(_saveService.Current, model))
                    {
                        // AlreadyScanned is true ONLY when this Scan recorded NOTHING new — neither a persistent
                        // flag flip (stage.Applied) NOR a new encounter-state entry (addedToEncounter). A re-scan
                        // that flips the persistent flag for the first time (undiscovered → scanned, then
                        // discovered mid-encounter, then re-scanned) DID persist new data, so it is NOT
                        // "already scanned" (CR patch — the contract is "recorded nothing new").
                        bool recordedNothingNew = !stage.Applied && !addedToEncounter;
                        return ScanResult.Succeeded(monsterId, alreadyScanned: recordedNothingNew);
                    }

                    // Recovery swap mid-persist: SaveAsync durably wrote whatever Current pointed at, NOT this
                    // scan. Revert BOTH the encounter progress AND the codex flag onto the captured ref.
                    if (addedToEncounter)
                    {
                        _scannedThisEncounter.Remove(monsterId);
                    }

                    stage.Revert();
                    GameLog.Error(
                        $"EncounterService: a Scan of '{monsterId}' rolled back — the save model was swapped " +
                        "mid-operation (recovery raced a mutation).");
                    return ScanResult.Failed(ScanFailureReason.PersistenceFailed);
                }
                catch (Exception ex)
                {
                    // Persist fault: revert BOTH slices onto the captured ref — never recompute. Use the SAME
                    // addedToEncounter local as the swap branch so the revert provably mirrors the add.
                    if (addedToEncounter)
                    {
                        _scannedThisEncounter.Remove(monsterId);
                    }

                    stage.Revert();
                    GameLog.Error(
                        $"EncounterService: a Scan of '{monsterId}' rolled back — persist failed. {ex.Message}");
                    return ScanResult.Failed(ScanFailureReason.PersistenceFailed);
                }
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        /// <summary>
        /// THE atomic multi-delta write (AC-2). Consumes one charge of <paramref name="chargeType"/> AND
        /// stages the Codex discovery <paramref name="via"/> for <paramref name="monsterId"/>, committing BOTH
        /// in ONE <c>SaveService.SaveAsync</c> under the SHARED Economy lock, with whole-mutation rollback on
        /// any fault. The pipeline mirrors <see cref="ProgressionService.AddXpAsync"/> exactly. This is the
        /// primitive Stories 4.4–4.6 compose their real actions from; 4.1 proves it via
        /// <see cref="TryCaptureAsync"/>.
        /// <para>
        /// Sequence: acquire the shared lock → capture the model ref once → (a) check the charge is available
        /// (a zero-charge block is the expected "earn via XP" failure: no mutation, no persist) → (b) stage
        /// the economy delta (decrement the charge in memory) → (c) stage the codex delta (lock-free
        /// <see cref="CodexService.StageDiscovery"/>) → ONE <c>SaveAsync</c> → recovery-swap guard → on
        /// success commit; on ANY fault revert BOTH slices (charge AND codex) onto the captured ref → release
        /// the lock → raise the committed events (charges-changed + the staged discovery events).
        /// </para>
        /// </summary>
        private async Task<SpendResult> CommitActionAsync(string monsterId, ChargeType chargeType, DiscoverySource via)
        {
            SpendResult result;
            bool committed = false;
            int newChargeCount = 0;
            CodexService.CodexStage stage = default;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Capture the model reference ONCE — a recovery swap mid-persist must never make the rollback
                // write onto a NEW model (the CreditService/ProgressionService rollback-fidelity lesson).
                SaveModel model = RequireModel();

                int priorChargeCount = ChargeInventory.GetCount(model, chargeType);
                if (priorChargeCount == 0)
                {
                    // Expected "earn via XP" failure — the only decrement is guarded by this check, so the
                    // count can never go negative. No mutation, no persist, no codex stage, no event.
                    result = SpendResult.Failed(SpendFailureReason.InsufficientCharges, 0);
                }
                else
                {
                    // (b) economy slice: decrement the charge in memory.
                    ChargeInventory.SetCount(model, chargeType, priorChargeCount - 1);

                    // (c) codex slice: stage the discovery (lock-free, persist-free — the composed write owns
                    // the persist). The codex-write RULES stay in CodexService (one home); the stage carries a
                    // Revert mirroring the three RollBack cases.
                    stage = _codexService.StageDiscovery(model, monsterId, via);

                    try
                    {
                        // ONE persist for BOTH slices (AR-8: never two persists per action).
                        await _saveService.SaveAsync().ConfigureAwait(false);

                        if (ReferenceEquals(_saveService.Current, model))
                        {
                            committed = true;
                            newChargeCount = ChargeInventory.GetCount(model, chargeType);
                            result = SpendResult.Succeeded(newChargeCount);
                        }
                        else
                        {
                            // Recovery swap replaced the model mid-persist: SaveAsync durably wrote whatever
                            // Current pointed at, which is NOT this action. Revert BOTH slices onto the
                            // captured ref (the ProgressionService/CodexService swap-branch contract).
                            stage.Revert();
                            ChargeInventory.SetCount(model, chargeType, priorChargeCount);
                            GameLog.Error(
                                $"EncounterService: action ({chargeType}/{via} on '{monsterId}') rolled back — the save " +
                                "model was swapped mid-operation (recovery raced a mutation).");
                            result = SpendResult.Failed(SpendFailureReason.PersistenceFailed, priorChargeCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Persist fault: revert BOTH slices onto the captured ref — never recompute. The codex
                        // revert mirrors the three RollBack cases (a naive Codex.Remove would corrupt a
                        // flag-flip rollback — see CodexStage.Revert).
                        stage.Revert();
                        ChargeInventory.SetCount(model, chargeType, priorChargeCount);
                        GameLog.Error(
                            $"EncounterService: action ({chargeType}/{via} on '{monsterId}') rolled back — persist failed. {ex.Message}");
                        result = SpendResult.Failed(SpendFailureReason.PersistenceFailed, priorChargeCount);
                    }
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            // Events AFTER the lock releases, ONLY on a committed action (possibly background-thread; UI
            // marshals — Epic 6). The discovery events live with CodexService (raised via the staged handle).
            if (committed)
            {
                RaiseChargesChanged(chargeType, newChargeCount);
                stage.RaiseCommittedEvents();
            }

            return result;
        }

        // ---- AC-3: mid-session AR loss → Suspended + guidance → recovery TryRestore ----

        /// <summary>
        /// The <see cref="ArSessionService.OnArSessionInterrupted"/> handler (AC-3). An ACTIVE encounter
        /// enters <see cref="EncounterState.Suspended"/> (an interruption during Resolving is deferred until
        /// the atomic write settles — decision H2, handled inside <see cref="EncounterStateMachine.Suspend"/>).
        /// An idle/concluded encounter is a safe no-op (the state machine warns + ignores). Never throws
        /// (NFR-3) — defensive even though <c>ArSessionService</c> already isolates subscriber throws.
        /// </summary>
        private void HandleArSessionInterrupted()
        {
            try
            {
                _stateMachine.Suspend();
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    $"EncounterService: HandleArSessionInterrupted threw — swallowed to keep the lifecycle crash-free (NFR-3). {ex.Message}");
            }
        }

        /// <summary>
        /// Attempt recovery after an AR interruption (AC-3): for each anchor of the active encounter, call
        /// <see cref="AnchorRestoreService.TryRestore"/> (the Story 3.5 decision). If EVERY anchor restores
        /// (<c>Restored</c>/<c>RelocatedToPlane</c> — never vanish), resume the encounter to the state it left;
        /// if ANY anchor <c>Failed</c>, STAY suspended with guidance (the object never vanishes; the encounter
        /// waits for a later recovery). The actual scene re-anchor to the returned pose is Story 6.3 — 4.1
        /// makes the DECISION + drives the state machine. Returns the overall outcome for the caller/tests.
        /// Never throws (NFR-3).
        /// </summary>
        public AnchorRestoreResult Recover()
        {
            if (_stateMachine.State != EncounterState.Suspended)
            {
                GameLog.Warn(
                    $"EncounterService.Recover ignored — the encounter is not suspended (state: {_stateMachine.State}).");
                // Not suspended: report Restored (nothing to recover) without touching the machine.
                return AnchorRestoreResult.Restored;
            }

            // Zero anchors to restore: do NOT silently fall through to Resume()+Restored (that would claim a
            // restore that never happened — no TryRestore was called). A suspended encounter with no anchors
            // has nothing to re-acquire, so recovery cannot genuinely succeed — stay Suspended + guidance
            // (NFR-3, never a false "Restored"). In 4.1 `BeginLure` permits an empty anchor array (4.2's spawn
            // fills it); a suspended-with-no-anchors encounter is an edge that must not auto-resume. (CR patch.)
            if (_activeAnchors.Length == 0)
            {
                GameLog.Warn(
                    "EncounterService.Recover: the suspended encounter has no anchors to restore — staying " +
                    "Suspended (nothing to re-acquire; not a false 'Restored').");
                return AnchorRestoreResult.Failed;
            }

            bool anyFailed = false;
            bool anyRelocated = false;

            foreach (AnchorToken token in _activeAnchors)
            {
                AnchorRestoreResult outcome = _anchorRestoreService.TryRestore(in token, out _);
                if (outcome == AnchorRestoreResult.Failed)
                {
                    anyFailed = true;
                }
                else if (outcome == AnchorRestoreResult.RelocatedToPlane)
                {
                    anyRelocated = true;
                }
            }

            if (anyFailed)
            {
                // A lost anchor with no plane to relocate onto: stay Suspended + guidance (NFR-3, never vanish).
                GameLog.Warn(
                    "EncounterService.Recover: at least one anchor could not be restored — staying Suspended " +
                    "pending a later recovery (guidance, never vanish).");
                return AnchorRestoreResult.Failed;
            }

            // Every anchor restored or relocated → resume the encounter to the state it left.
            _stateMachine.Resume();
            return anyRelocated ? AnchorRestoreResult.RelocatedToPlane : AnchorRestoreResult.Restored;
        }

        /// <summary>
        /// Surface a rejected-spend event at the encounter altitude (AR-11). Used by the composed Lure/Slay
        /// actions (Stories 4.2/4.5) when a credit spend can't be afforded — the service raises this, never
        /// opening the Shop. Subscriber-isolated (a throwing subscriber is logged, not propagated).
        /// </summary>
        internal void RaiseInsufficientCredits(InsufficientCreditsEvent insufficientEvent)
        {
            try
            {
                OnInsufficientCredits?.Invoke(insufficientEvent);
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    $"EncounterService: an OnInsufficientCredits subscriber threw. {ex.Message}");
            }
        }

        private void RaiseChargesChanged(ChargeType type, int newCount)
        {
            // The authoritative charge-count change event lives on IProgressionService; the composed write
            // mutated charges WITHOUT going through ProgressionService (it owns the single persist), so it must
            // surface the change itself. UI re-reads GetChargeCount for the authoritative value (events are
            // change signals). Nothing to raise here beyond logging in 4.1 — the IProgressionService event is
            // not re-raisable from outside; Epic 6's HUD binds the encounter result directly. Kept as the seam.
            GameLog.Info($"EncounterService: charge {type} changed to {newCount} via a composed action.");
        }

        /// <summary>
        /// The loaded model, or an <see cref="InvalidOperationException"/> when it is not loaded yet (Bootstrap
        /// fire-and-forgets the initial load) or the save is corrupt and unrecovered — mirrors every other
        /// service's before-load contract.
        /// </summary>
        private SaveModel RequireModel()
        {
            SaveModel model = _saveService.Current;
            if (model == null)
            {
                throw new InvalidOperationException(
                    "EncounterService has no loaded save model — await SaveService.InitializeAsync " +
                    "(or recover the corrupt save) before running an encounter action.");
            }

            return model;
        }
    }
}
