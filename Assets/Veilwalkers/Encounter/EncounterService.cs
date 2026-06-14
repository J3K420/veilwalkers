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
        private readonly IProgressionService _progressionService;
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

        private readonly EncounterStateMachine _stateMachine = new EncounterStateMachine();

        // The active encounter's anchors (in-memory only this story — the persisted Shop round-trip snapshot
        // is Story 5.4). Set when a Lure spawns (4.2); read on recovery to re-anchor. Minimal for 4.1.
        private AnchorToken[] _activeAnchors = Array.Empty<AnchorToken>();

        // The active encounter's pooled-spawn handles (Story 4.2). Held so EndEncounter can Release them back
        // to the MonsterSpawner pool — otherwise a Lured encounter's spawns would stay active forever (a pool
        // leak). Set on a successful Lure; cleared + released on EndEncounter.
        private int[] _activeSpawnHandles = Array.Empty<int>();

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
            IProgressionService progressionService,
            CodexService codexService,
            SaveMutationLock mutationLock,
            AnchorRestoreService anchorRestoreService,
            ArSessionService arSessionService,
            LureSystem lureSystem,
            PlaneAnchorService planeAnchorService,
            MonsterSpawner monsterSpawner)
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
            // INDEPENDENT monsters; Basic/Premium roll one.
            var monsterIds = new List<string>(wanted);
            if (kind == LureKind.Multi)
            {
                (string first, string second) = _lureSystem.RollMultiMonsters();
                monsterIds.Add(first);
                monsterIds.Add(second);
            }
            else
            {
                monsterIds.Add(_lureSystem.RollMonster(kind));
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

        // ---- AC-2: the atomic multi-delta write primitive + a representative composed action ----

        /// <summary>
        /// Capture a monster (the REPRESENTATIVE composed action proving AC-2): consume one
        /// <see cref="ChargeType.StrongCapture"/> charge AND record the Codex Capture discovery in ONE atomic
        /// save write. The full Capture success math + the free-base-vs-Strong choice is Story 4.4 — 4.1 wires
        /// the charge+codex composed write so the AC-2 mechanism is real + tested. Returns a typed
        /// <see cref="SpendResult"/> (never throws for an expected failure: a zero-charge block, a persist
        /// fault). Requires <paramref name="monsterId"/> to be a valid universe id (programmer error otherwise).
        /// </summary>
        public Task<SpendResult> TryCaptureAsync(string monsterId)
        {
            if (string.IsNullOrEmpty(monsterId))
            {
                throw new ArgumentException("A monster id is required.", nameof(monsterId));
            }

            return CommitActionAsync(monsterId, ChargeType.StrongCapture, DiscoverySource.Capture);
        }

        /// <summary>
        /// Run a composed action as the encounter state machine expects (Acting → Resolving → Acting): begin
        /// resolve (opens the AR-8 commit window), run the atomic write, then complete-or-fail the resolve so
        /// the machine returns to a live state — and an AR interruption that arrived DURING the write is
        /// applied now (decision H2, honored by <see cref="EncounterStateMachine.CompleteResolve"/>/
        /// <see cref="EncounterStateMachine.FailResolve"/>). The caller must be in <see cref="EncounterState.Acting"/>
        /// (i.e. inside a live encounter mid-action). The full FR-6–10 action flows (4.2–4.6) call this; 4.1
        /// proves it via <see cref="TryCaptureInEncounterAsync"/>.
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

        /// <summary>The representative IN-ENCOUNTER composed action (proves AC-2 + the state-machine loop):
        /// a Strong-Capture charge + a Codex Capture, committed atomically, driven through Acting → Resolving →
        /// Lured. Capture math is Story 4.4.</summary>
        public Task<SpendResult> TryCaptureInEncounterAsync(string monsterId)
        {
            if (string.IsNullOrEmpty(monsterId))
            {
                throw new ArgumentException("A monster id is required.", nameof(monsterId));
            }

            return RunActionAsync(monsterId, ChargeType.StrongCapture, DiscoverySource.Capture);
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
