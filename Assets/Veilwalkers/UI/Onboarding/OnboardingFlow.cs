using System;
using System.Threading.Tasks;
using Veilwalkers.App;
using Veilwalkers.Core;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The pure-logic first-launch onboarding STEP state machine (Story 6.4). It owns the phase
    /// progression ONLY — premise cards → camera disclosure → coin-burst grant → complete — and at the
    /// terminal step drives the Story-6.3 <see cref="AppStateMachine.CompleteOnboarding"/> gate (the
    /// AR-entry precondition). It deliberately owns NEITHER camera-permission logic (delegated to the
    /// 3.1 <c>CameraPermissionFlow</c> via the narrow <see cref="IDisclosureGate"/>) NOR the credit grant
    /// (the 1.7 <c>FirstLaunchGrant</c> already ran at boot; the coin-burst only PRESENTS the balance).
    /// <para>
    /// Plain C#, ctor-injected, NO <c>MonoBehaviour</c> / Unity types / service-locator reads — the
    /// <c>HomePresenter</c> / <c>CameraPermissionFlow</c> headless-testable precedent. The thin
    /// <c>OnboardingView</c> + the <c>Onboarding.unity</c> scene + the card/coin animations are the
    /// deferred Story-6.5 view layer.
    /// </para>
    /// <para>
    /// <b>The order IS the contract (the derived ordering AC).</b> Disclosure is reachable only after the
    /// last premise card; Grant only after Disclosure; Complete only after Grant. Out-of-order advances
    /// are a logged no-op returning false (NFR-3 graceful — a UI-driven flow never throws). This
    /// guarantees the camera is disclosed BEFORE the player is routed toward camera use (AR Hunt).
    /// </para>
    /// </summary>
    public sealed class OnboardingFlow
    {
        /// <summary>
        /// The premise-card count the flow walks (Story 6.4, AC-1; Decision B). DERIVED from the SINGLE
        /// source of truth — <see cref="OnboardingPresenter.PremiseBeats"/> — so the flow's walk length
        /// and the presenter's rendered cards can NEVER diverge (the AC allows 2–3; a content pass that
        /// changes the beat list moves both at once). NOT a hardcoded literal: a desync here would have
        /// the flow walk an index the presenter never built (the CR-confirmed coupling bug). Tests pin
        /// the WALK + this equality, not a magic number.
        /// </summary>
        public static int PremiseCardCount => OnboardingPresenter.PremiseBeats.Count;

        private readonly AppStateMachine _appState;        // the 6.3 gate (CompleteOnboarding)
        private readonly IDisclosureGate _disclosure;      // fires the OS prompt after the disclosure card
        private readonly IArPrewarmPort _prewarm;          // best-effort AR session warmup

        private bool _prewarmKicked;

        /// <summary>The current onboarding phase. Starts at <see cref="OnboardingStep.Premise"/>.</summary>
        public OnboardingStep Step { get; private set; } = OnboardingStep.Premise;

        /// <summary>The index of the premise card currently shown (0-based), while <see cref="Step"/> is
        /// <see cref="OnboardingStep.Premise"/>. Walks 0..<see cref="PremiseCardCount"/>-1; the last
        /// card's advance moves to <see cref="OnboardingStep.Disclosure"/>.</summary>
        public int PremiseCardIndex { get; private set; }

        /// <summary>How many times <see cref="AppStateMachine.CompleteOnboarding"/> was invoked by this
        /// flow (must be exactly 1 across a normal completion — pinned by a test).</summary>
        public int CompleteOnboardingCalls { get; private set; }

        public OnboardingFlow(AppStateMachine appState, IDisclosureGate disclosure, IArPrewarmPort prewarm)
        {
            // appState is required (the gate it drives); the ports degrade to no-ops when their backing
            // service is an unregistered Bootstrap seam.
            _appState = appState ?? throw new ArgumentNullException(nameof(appState));
            _disclosure = disclosure ?? IDisclosureGate.NoDisclosure;
            _prewarm = prewarm ?? IArPrewarmPort.NoPrewarm;
        }

        /// <summary>
        /// Kick the best-effort AR-session prewarm (architecture.md:575-584 — onboarding calls prewarm,
        /// it does not warm the session itself). Idempotent: only the FIRST call fires the port. Safe to
        /// call as the premise cards begin. Fire-and-forget with an observed/guarded continuation so a
        /// prewarm fault never crashes onboarding (the 6.3 fire-and-forget-snapshot discipline).
        /// </summary>
        public void BeginPrewarm()
        {
            if (_prewarmKicked)
            {
                return;
            }

            // Latch the flag ONLY after a successful schedule: if KickPrewarm throws synchronously (a rare
            // scheduler fault), reset the flag so a later BeginPrewarm can retry rather than silently
            // losing prewarm for the whole session.
            _prewarmKicked = true;
            try
            {
                KickPrewarm();
            }
            catch (Exception ex)
            {
                _prewarmKicked = false;
                GameLog.Warn(
                    "OnboardingFlow: failed to schedule AR prewarm (will retry on the next BeginPrewarm; " +
                    $"the cold AR entry prewarms on demand regardless). {ex.Message}");
            }
        }

        private void KickPrewarm()
        {
            // Observed fire-and-forget on the default scheduler: prewarm is best-effort, must never throw
            // up into the onboarding flow. Any fault is logged + swallowed (NFR-3).
            Task.Factory.StartNew(
                    () => _prewarm.PrewarmAsync(),
                    System.Threading.CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default)
                .Unwrap()
                .ContinueWith(
                    t =>
                    {
                        if (t.IsFaulted)
                        {
                            GameLog.Warn(
                                "OnboardingFlow: AR prewarm faulted (best-effort; the cold AR entry will " +
                                $"prewarm on demand). {t.Exception?.GetBaseException().Message}");
                        }
                    },
                    TaskScheduler.Default);
        }

        /// <summary>
        /// Advance the premise cards (AC-1). While not on the last card, moves to the next card; on the
        /// last card, transitions to <see cref="OnboardingStep.Disclosure"/>. Rejected (no-op + warn,
        /// returns false) if not currently on the <see cref="OnboardingStep.Premise"/> step.
        /// </summary>
        public bool AdvancePremise()
        {
            if (Step != OnboardingStep.Premise)
            {
                return Reject(nameof(AdvancePremise), OnboardingStep.Premise);
            }

            if (PremiseCardIndex < PremiseCardCount - 1)
            {
                PremiseCardIndex++;
                return true;
            }

            // The last premise card was advanced → the camera-disclosure card (AC-2: BEFORE the OS prompt).
            Step = OnboardingStep.Disclosure;
            return true;
        }

        /// <summary>
        /// The player acknowledged the camera-disclosure card (AC-2) → fire the OS permission prompt via
        /// the disclosure gate, then move to the coin-burst grant presentation
        /// (<see cref="OnboardingStep.Grant"/>). Rejected (no-op + warn) if not on
        /// <see cref="OnboardingStep.Disclosure"/> — you cannot reach Grant without disclosing first.
        /// </summary>
        public bool AdvanceFromDisclosure()
        {
            if (Step != OnboardingStep.Disclosure)
            {
                return Reject(nameof(AdvanceFromDisclosure), OnboardingStep.Disclosure);
            }

            // Disclosure acknowledged → fire the OS prompt (delegated; the gate is a no-op if the camera
            // flow is unwired or already past Disclosure). This is what makes "disclosure BEFORE the OS
            // dialog" true: the prompt fires only here, after the disclosure card. Guarded: the gate
            // contract is "never throws" but defense-in-depth at the seam upholds THIS flow's own
            // "never throws" claim even if a misbehaving adapter faults — log + still advance so the flow
            // is never wedged on Disclosure (the prompt is best-effort; the player can still proceed).
            try
            {
                _disclosure.ConfirmDisclosureAndRequest();
            }
            catch (Exception ex)
            {
                GameLog.Warn(
                    "OnboardingFlow: the disclosure gate faulted firing the OS prompt (best-effort; " +
                    $"advancing onboarding anyway). {ex.Message}");
            }

            Step = OnboardingStep.Grant;
            return true;
        }

        /// <summary>
        /// The 20-Credit coin-burst has been presented (AC-3) → complete onboarding. Calls the Story-6.3
        /// <see cref="AppStateMachine.CompleteOnboarding"/> gate EXACTLY ONCE (Onboarding → Home) and
        /// moves to <see cref="OnboardingStep.Complete"/>. Rejected (no-op + warn) if not on
        /// <see cref="OnboardingStep.Grant"/>.
        /// </summary>
        public bool AdvanceFromGrant()
        {
            if (Step != OnboardingStep.Grant)
            {
                return Reject(nameof(AdvanceFromGrant), OnboardingStep.Grant);
            }

            // Drive the 6.3 gate: this is the ONLY exit from AppSurface.Onboarding (→ Home), and the
            // precondition that makes a cold AR entry reachable (AC-4). Call it exactly once. Guarded:
            // CompleteOnboarding() can raise OnSurfaceChanged (a subscriber could throw), so wrap it to
            // uphold THIS flow's "never throws" contract. The counter increments only on a NON-throwing
            // call (it tracks successful gate drives — a throw correctly leaves the count at 0 so a test
            // catches the regression), but the flow ALWAYS advances to Complete: the gate itself is
            // idempotent (a re-call past onboarding is a no-op + warn), so a partial fault never wedges
            // the player mid-onboarding nor risks a double-grant.
            try
            {
                _appState.CompleteOnboarding();
                CompleteOnboardingCalls++;
            }
            catch (Exception ex)
            {
                GameLog.Warn(
                    "OnboardingFlow: completing the onboarding gate faulted (advancing anyway; the gate " +
                    $"is idempotent and onboarding must not wedge). {ex.Message}");
            }

            Step = OnboardingStep.Complete;
            return true;
        }

        private bool Reject(string method, OnboardingStep expected)
        {
            GameLog.Warn(
                $"OnboardingFlow.{method} ignored — only valid from {expected} (current: {Step}). " +
                "No state change.");
            return false;
        }
    }
}
