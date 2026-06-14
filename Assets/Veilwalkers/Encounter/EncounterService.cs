using System;
using System.Threading.Tasks;
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

        private readonly EncounterStateMachine _stateMachine = new EncounterStateMachine();

        // The active encounter's anchors (in-memory only this story — the persisted Shop round-trip snapshot
        // is Story 5.4). Set when a Lure spawns (4.2); read on recovery to re-anchor. Minimal for 4.1.
        private AnchorToken[] _activeAnchors = Array.Empty<AnchorToken>();

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
            ArSessionService arSessionService)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _creditService = creditService ?? throw new ArgumentNullException(nameof(creditService));
            _progressionService = progressionService ?? throw new ArgumentNullException(nameof(progressionService));
            _codexService = codexService ?? throw new ArgumentNullException(nameof(codexService));
            _mutationLock = mutationLock ?? throw new ArgumentNullException(nameof(mutationLock));
            _anchorRestoreService = anchorRestoreService ?? throw new ArgumentNullException(nameof(anchorRestoreService));
            _arSessionService = arSessionService ?? throw new ArgumentNullException(nameof(arSessionService));

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

        /// <summary>Reset the encounter to Idle (ends/aborts it); clears the active anchors.</summary>
        public bool EndEncounter()
        {
            _activeAnchors = Array.Empty<AnchorToken>();
            return _stateMachine.Reset();
        }

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
