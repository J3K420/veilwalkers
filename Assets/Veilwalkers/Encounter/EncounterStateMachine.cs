using System;
using Veilwalkers.Core;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// The explicit encounter-flow state machine (Story 4.1, AR-9 / architecture.md:66, :414, :516). Pure
    /// C# — NO MonoBehaviour, NO Unity types — so the transition logic is headless-tested. Mirrors the
    /// <c>ArSessionService</c> lifecycle-machine shape: named lifecycle methods, an explicit legal-transition
    /// table, a <see cref="GameLog.Warn"/> warned no-op for any illegal transition (state unchanged, returns
    /// false), and it NEVER throws on a bad transition (NFR-3).
    /// <para>
    /// <b>The states + happy path</b> (<see cref="EncounterState"/>): Idle → Lured → Acting → Resolving →
    /// Resolved, with per-ACTION resolution looping Acting → Resolving → Acting (an encounter has many
    /// actions; <see cref="EncounterState.Resolved"/> is the per-encounter terminal). <see cref="EncounterState.Suspended"/>
    /// is reachable from any active state on AR loss; on recovery it resumes to the state it left.
    /// </para>
    /// <para>
    /// <b>Suspend during Resolving (decision H2):</b> <see cref="EncounterState.Resolving"/> is held WHILE the
    /// composed atomic write owns the shared save lock + has a persist in flight. A
    /// <see cref="Suspend"/> raised during <see cref="EncounterState.Resolving"/> must NOT yank the machine
    /// mid-persist (that could leave a half-applied mutation or race the rollback). Instead it records a
    /// <see cref="SuspendRequested"/> intent; the write's completion path (<see cref="CompleteResolve"/> /
    /// <see cref="FailResolve"/>) honors that intent by transitioning to <see cref="EncounterState.Suspended"/>
    /// once the write settles. The atomic write therefore never observes a torn model.
    /// </para>
    /// </summary>
    public sealed class EncounterStateMachine
    {
        /// <summary>The current encounter state. Starts <see cref="EncounterState.Idle"/>.</summary>
        public EncounterState State { get; private set; } = EncounterState.Idle;

        /// <summary>
        /// True when an AR interruption arrived DURING <see cref="EncounterState.Resolving"/> (the atomic-write
        /// window) and is pending application once the write settles (decision H2). Cleared when the suspend is
        /// applied (or when the machine resets / resumes). Exposed read-only so the service + tests can assert it.
        /// </summary>
        public bool SuspendRequested { get; private set; }

        /// <summary>
        /// The state the machine was in before it suspended — the resume target (<see cref="Resume"/>). Only
        /// meaningful while <see cref="State"/> is <see cref="EncounterState.Suspended"/>.
        /// </summary>
        private EncounterState _stateBeforeSuspend = EncounterState.Idle;

        /// <summary>
        /// Raised AFTER a transition commits (the new state). Subscriber-isolated (a throwing subscriber is
        /// logged, never propagated — the transition already happened), mirroring every other service event.
        /// The Epic-6 encounter HUD / Suspended-guidance UI binds this; 4.1 only raises it.
        /// </summary>
        public event Action<EncounterState> OnStateChanged;

        /// <summary>Idle → Lured: a Lure spawned a monster (the Lure action itself is Story 4.2).</summary>
        public bool BeginLure() => Transition(EncounterState.Idle, EncounterState.Lured, nameof(BeginLure));

        /// <summary>
        /// Lured/Acting → Acting: the player began an action (Scan/Capture/Slay/extra — the actions are
        /// Stories 4.3–4.6). Legal from <see cref="EncounterState.Lured"/> (first action) AND from
        /// <see cref="EncounterState.Acting"/> is NOT — an action begins from Lured or after a prior action
        /// resolved back to Lured/Acting; see <see cref="CompleteResolve"/>'s loop-back.
        /// </summary>
        public bool BeginAction() => Transition(EncounterState.Lured, EncounterState.Acting, nameof(BeginAction));

        /// <summary>
        /// Acting → Resolving: the action's atomic spend+resolution write is starting (the AR-8 commit
        /// window opens). While <see cref="EncounterState.Resolving"/>, an AR interruption is deferred
        /// (decision H2) — see <see cref="Suspend"/>.
        /// </summary>
        public bool BeginResolve() => Transition(EncounterState.Acting, EncounterState.Resolving, nameof(BeginResolve));

        /// <summary>
        /// Resolving → Lured: the action committed successfully and the encounter continues (the player can
        /// act again). If a suspend was requested mid-resolve (decision H2), this instead applies the
        /// deferred <see cref="EncounterState.Suspended"/> transition once the write has settled. Use
        /// <see cref="EndEncounter"/> for the per-encounter terminal.
        /// </summary>
        public bool CompleteResolve() => ResolveTo(EncounterState.Lured, nameof(CompleteResolve));

        /// <summary>
        /// Resolving → Lured on a FAILED (rolled-back) action: the persist failed, the mutation rolled back,
        /// and the encounter stays live so the player can retry (Retry is free, FR-10). Same deferred-suspend
        /// honoring as <see cref="CompleteResolve"/> (decision H2). The action's typed failure is the
        /// service's concern; the machine simply returns to a live, actionable state.
        /// </summary>
        public bool FailResolve() => ResolveTo(EncounterState.Lured, nameof(FailResolve));

        /// <summary>
        /// Resolving → Resolved: the encounter as a whole concludes (per-encounter terminal). Deliberately
        /// separate from <see cref="CompleteResolve"/> so the common case (keep acting) and the rare case
        /// (end the encounter) are distinct, explicit transitions.
        /// </summary>
        public bool EndEncounter() => ResolveTo(EncounterState.Resolved, nameof(EndEncounter));

        /// <summary>
        /// Enter <see cref="EncounterState.Suspended"/> on AR loss (architecture.md:650-652). Legal from any
        /// ACTIVE state (<see cref="EncounterState.Lured"/>/<see cref="EncounterState.Acting"/>). From
        /// <see cref="EncounterState.Resolving"/> it does NOT suspend immediately (decision H2): it records
        /// <see cref="SuspendRequested"/> so the in-flight atomic write settles first; the write's completion
        /// path then applies the suspend. From <see cref="EncounterState.Idle"/>/<see cref="EncounterState.Resolved"/>/
        /// already-<see cref="EncounterState.Suspended"/> it is a warned no-op (nothing active to suspend).
        /// Never throws.
        /// </summary>
        public bool Suspend()
        {
            switch (State)
            {
                case EncounterState.Lured:
                case EncounterState.Acting:
                    _stateBeforeSuspend = State;
                    return SetState(EncounterState.Suspended);

                case EncounterState.Resolving:
                    // Defer: the atomic write is in flight. Record the intent; the completion path
                    // (CompleteResolve/FailResolve) applies the suspend once the write settles (decision H2).
                    SuspendRequested = true;
                    GameLog.Info(
                        "EncounterStateMachine.Suspend: AR interruption arrived during Resolving — deferring " +
                        "the suspend until the in-flight atomic write settles (decision H2).");
                    return true;

                default:
                    GameLog.Warn(
                        $"EncounterStateMachine.Suspend ignored — no active encounter to suspend (state: {State}).");
                    return false;
            }
        }

        /// <summary>
        /// Resume from <see cref="EncounterState.Suspended"/> back to the state the encounter left
        /// (<see cref="_stateBeforeSuspend"/>) — called after a successful anchor restore on recovery
        /// (<c>Restored</c>/<c>RelocatedToPlane</c>). A warned no-op if not currently suspended. Never throws.
        /// </summary>
        public bool Resume()
        {
            if (State != EncounterState.Suspended)
            {
                GameLog.Warn(
                    $"EncounterStateMachine.Resume ignored — not suspended (state: {State}).");
                return false;
            }

            return SetState(_stateBeforeSuspend);
        }

        /// <summary>
        /// Reset to <see cref="EncounterState.Idle"/> from any state (the encounter ends / is aborted). Always
        /// legal — ending an encounter is never illegal. Clears any pending suspend intent.
        /// </summary>
        public bool Reset()
        {
            SuspendRequested = false;
            _stateBeforeSuspend = EncounterState.Idle;
            return SetState(EncounterState.Idle);
        }

        /// <summary>
        /// The shared Resolving-exit path: from <see cref="EncounterState.Resolving"/> go to
        /// <paramref name="target"/> — UNLESS a suspend was requested mid-resolve (decision H2), in which case
        /// apply the deferred <see cref="EncounterState.Suspended"/> transition instead (the write has now
        /// settled, so it is safe). A warned no-op if not currently <see cref="EncounterState.Resolving"/>.
        /// </summary>
        private bool ResolveTo(EncounterState target, string op)
        {
            if (State != EncounterState.Resolving)
            {
                GameLog.Warn(
                    $"EncounterStateMachine.{op} ignored — illegal transition (state: {State}, expected Resolving).");
                return false;
            }

            if (SuspendRequested)
            {
                // The atomic write has settled; NOW honor the deferred AR-loss suspend (decision H2). The
                // resume target is the SAME state the resolve was heading to (target) — NOT a hardcoded Lured:
                // a deferred suspend during an EndEncounter (target Resolved) must resume to Resolved, not Lured
                // (CR patch — derive the resume target from the resolve, don't bake in Lured). (architecture
                // happy-path: a per-action resolve targets Lured; EndEncounter targets Resolved.)
                SuspendRequested = false;
                _stateBeforeSuspend = target;
                return SetState(EncounterState.Suspended);
            }

            return SetState(target);
        }

        /// <summary>
        /// Guarded transition: commit <paramref name="to"/> only if the current state equals
        /// <paramref name="from"/>; otherwise a warned no-op (state unchanged, returns false, never throws).
        /// </summary>
        private bool Transition(EncounterState from, EncounterState to, string op)
        {
            if (State != from)
            {
                GameLog.Warn(
                    $"EncounterStateMachine.{op} ignored — illegal transition (state: {State}, expected {from}).");
                return false;
            }

            return SetState(to);
        }

        /// <summary>Commit a new state and raise <see cref="OnStateChanged"/> (subscriber-isolated).</summary>
        private bool SetState(EncounterState to)
        {
            State = to;
            RaiseStateChanged(to);
            return true;
        }

        private void RaiseStateChanged(EncounterState to)
        {
            try
            {
                OnStateChanged?.Invoke(to);
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    $"EncounterStateMachine: an OnStateChanged subscriber threw — the transition is already committed. {ex.Message}");
            }
        }
    }
}
