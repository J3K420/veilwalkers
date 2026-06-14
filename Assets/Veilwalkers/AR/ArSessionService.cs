using System;
using System.Threading.Tasks;
using Veilwalkers.Core;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The sole owner of the AR-session lifecycle (Story 3.3, FR-4 staging; architecture.md:405,
    /// :466-468, :584): the ONE place AR is started, paused, resumed, or torn down. Plain C# (NO
    /// <c>MonoBehaviour</c>, no Unity types beyond the seam) so the cold→prewarming→ready→running→
    /// paused state machine is headless-tested against a fake <see cref="IArSession"/>. Mirrors the
    /// pure-logic <see cref="CameraPermissionFlow"/>/<see cref="ArSafetyGate"/> shape, but adds an
    /// async lifecycle (the seam's <see cref="IArSession.StartAsync"/>) and an injected collaborator
    /// (<see cref="CameraPermissionFlow"/> for the inherited Story 3.1 permission re-check).
    /// <para>
    /// <b>Decision #1 — pure-logic state machine + thin adapter:</b> the lifecycle DECISIONS live here;
    /// the OS-touching ARCore glue lives in <see cref="IArSession"/>/<see cref="ArcoreSession"/> (no
    /// branching beyond the platform <c>#if</c>). This reconciles "sole owner of the AR lifecycle" with
    /// the AR "thin adapter, zero branching logic" mandate (architecture.md:466) — identical to how 3.1
    /// split <see cref="CameraPermissionFlow"/> from <c>AndroidCameraPermission</c>. There is no
    /// <c>ARSession</c> in EditMode (architecture.md:591), which is exactly why the state machine is
    /// seamed away from the adapter.
    /// </para>
    /// <para>
    /// <b>Decision #2 — async at one seam, double-start-safe:</b> <see cref="PrewarmAsync"/> awaits the
    /// seam's <see cref="IArSession.StartAsync"/>; everything else is a cheap synchronous toggle. A
    /// second <see cref="PrewarmAsync"/> while a start is in flight (or already warm) does NOT fire a
    /// second <see cref="IArSession.StartAsync"/> — it returns the in-flight task / a warned no-op (the
    /// architecture's "double-start handled inside" — :585). Single-threaded UI-driven, so the guard is
    /// a state + in-flight-<see cref="Task"/> check, not a semaphore.
    /// </para>
    /// <para>
    /// <b>Decision #3 — prewarm is an OPTIMIZATION, not a precondition:</b> <see cref="EnterAsync"/>
    /// from <see cref="ArSessionState.Cold"/> (no prewarm happened) still works — it prewarms then runs,
    /// just slower. Correctness never depends on prewarm having run (architecture.md:588-590 "measure
    /// with prewarm on AND off").
    /// </para>
    /// <para>
    /// <b>Decision #4 — camera-permission re-check (the inherited 3.1 deferral):</b> before
    /// <see cref="IArSession.StartAsync"/> and on backgrounded-resume, the service re-validates camera
    /// permission via the injected <see cref="CameraPermissionFlow.Evaluate"/> (live OS truth). On
    /// not-granted it refuses + tears down + raises <see cref="OnArSessionInterrupted"/> so 3.1's
    /// re-grant flow can take over. NOTE: <see cref="CameraPermissionFlow.Evaluate"/> is NOT a pure read
    /// — it SETS the flow's <c>State</c> to <c>Granted</c>/<c>Disclosure</c> and returns it; calling it
    /// here intentionally re-routes the shared flow back to disclosure on a revocation (the desired
    /// re-grant routing, not an accidental mutation).
    /// </para>
    /// <para>
    /// <b>Legal transitions</b> (illegal/out-of-state calls are a logged no-op — a lifecycle-driven
    /// flow must never throw, NFR-3):
    /// <list type="bullet">
    /// <item><see cref="PrewarmAsync"/>: from <see cref="ArSessionState.Cold"/> (supported + granted) →
    /// <see cref="ArSessionState.Prewarming"/> → <see cref="ArSessionState.Ready"/>. Idempotent /
    /// double-start-safe from any non-cold state. Refuses (stays cold) if unsupported or ungranted.</item>
    /// <item><see cref="EnterAsync"/>: from <see cref="ArSessionState.Ready"/> →
    /// <see cref="ArSessionState.Running"/>; from <see cref="ArSessionState.Cold"/> it prewarms first
    /// (prewarm optional, decision #3). Warned no-op if already running/paused.</item>
    /// <item><see cref="TearDown"/>: from any warm state → <see cref="IArSession.Stop"/> →
    /// <see cref="ArSessionState.Cold"/>. Warned no-op from cold.</item>
    /// <item><see cref="OnApplicationPause"/>: <c>true</c> while <see cref="ArSessionState.Running"/> →
    /// <see cref="ArSessionState.Paused"/>; <c>false</c> while <see cref="ArSessionState.Paused"/> →
    /// re-check permission → <see cref="ArSessionState.Running"/> OR tear down + interrupt. Warned no-op
    /// otherwise.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class ArSessionService
    {
        private readonly IArSession _session;
        private readonly CameraPermissionFlow _cameraPermission;

        // The in-flight prewarm task. Non-null only while StartAsync is awaited (State == Prewarming).
        // The double-start guard: a second PrewarmAsync while this is non-null returns THIS task rather
        // than firing a second IArSession.StartAsync (decision #2). Cleared when the start completes.
        private Task _prewarmInFlight;

        /// <summary>The current lifecycle state. Starts <see cref="ArSessionState.Cold"/>.</summary>
        public ArSessionState State { get; private set; } = ArSessionState.Cold;

        /// <summary>
        /// Raised when the session is lost / interrupted (camera permission revoked while backgrounded,
        /// or an explicit loss teardown) — architecture.md:650, AR-6. Story 3.3 DECLARES + raises it;
        /// the <c>EncounterStateMachine</c> <c>Suspended</c> consumer + <c>TryRestoreAnchor</c> recovery
        /// are Story 3.5 / Epic 4 (the event exists + fires now; its handler is downstream).
        /// </summary>
        public event Action OnArSessionInterrupted;

        public ArSessionService(IArSession session, CameraPermissionFlow cameraPermission)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _cameraPermission = cameraPermission ?? throw new ArgumentNullException(nameof(cameraPermission));
        }

        /// <summary>
        /// The AC-3 prewarm trigger entry (called by Onboarding/Home, gated to when the AR-entry
        /// affordance is visible/likely — they do NOT warm the session themselves). From
        /// <see cref="ArSessionState.Cold"/>: re-validate camera permission + device support, then
        /// <see cref="ArSessionState.Prewarming"/> → await <see cref="IArSession.StartAsync"/> →
        /// <see cref="ArSessionState.Ready"/>. <b>Double-start-safe (decision #2):</b> a second call
        /// while a start is in flight returns the in-flight task (no second <see cref="IArSession.StartAsync"/>);
        /// a call when already <see cref="ArSessionState.Ready"/>/<see cref="ArSessionState.Running"/>/
        /// <see cref="ArSessionState.Paused"/> is a warned no-op. Refuses (stays cold, warns, never
        /// throws — NFR-3) if the device is unsupported or permission is not granted.
        /// </summary>
        public Task PrewarmAsync()
        {
            // Double-start guard: a prewarm genuinely in flight → hand back the SAME task, never a
            // second StartAsync (decision #2). This is THE "double-start handled inside" pin. The
            // AUTHORITATIVE signal is State == Prewarming (set before the StartAsync await, cleared to
            // Ready/Cold on completion) — NOT the _prewarmInFlight field alone, because a StartAsync
            // that completes synchronously settles State before this method's caller resumes, and the
            // field could otherwise be re-observed stale.
            if (State == ArSessionState.Prewarming)
            {
                GameLog.Info("ArSessionService.PrewarmAsync: a prewarm is already in flight — returning the in-flight task (no second StartAsync).");
                return _prewarmInFlight ?? Task.CompletedTask;
            }

            // Already warm/running/paused → nothing to prewarm. Warned no-op (decision #2 idempotency).
            if (State != ArSessionState.Cold)
            {
                GameLog.Warn(
                    $"ArSessionService.PrewarmAsync ignored — already warm (state: {State}). " +
                    "No second StartAsync fired.");
                return Task.CompletedTask;
            }

            // Refuse to prewarm on an unsupported device or without camera permission — degrade
            // gracefully (stay Cold + warn), never crash (NFR-3). The permission re-check is the
            // inherited 3.1 deferral (decision #4).
            if (!CanStart())
            {
                return Task.CompletedTask;
            }

            State = ArSessionState.Prewarming;
            // StartThenReadyAsync may run to completion SYNCHRONOUSLY (a StartAsync that returns an
            // already-completed task), settling State to Ready and clearing _prewarmInFlight DURING this
            // call — before the assignment below would run. So capture the task into a local first, and
            // only publish it to the field while we are still Prewarming (i.e. the start has NOT already
            // completed synchronously). This avoids the classic "assign-after-synchronous-completion"
            // race that would leave a completed task lingering in _prewarmInFlight.
            Task warmup = StartThenReadyAsync();
            if (State == ArSessionState.Prewarming)
            {
                _prewarmInFlight = warmup;
            }
            return warmup;
        }

        // The awaited warmup body. On completion → Ready (unless a TearDown raced in and reset us to
        // Cold while the start was in flight — then leave Cold). Clears the in-flight latch.
        private async Task StartThenReadyAsync()
        {
            try
            {
                await _session.StartAsync();
            }
            finally
            {
                _prewarmInFlight = null;
            }

            // A TearDown() during the in-flight start resets to Cold; respect that rather than
            // clobbering it back to Ready.
            if (State == ArSessionState.Prewarming)
            {
                State = ArSessionState.Ready;
            }
        }

        /// <summary>
        /// The player committed to AR Mode. From <see cref="ArSessionState.Ready"/> →
        /// <see cref="ArSessionState.Running"/>. From <see cref="ArSessionState.Cold"/> it prewarms
        /// first (prewarm is an OPTIMIZATION, not a precondition — decision #3), then runs. A warned
        /// no-op if already <see cref="ArSessionState.Running"/>/<see cref="ArSessionState.Paused"/>.
        /// Refuses (stays put, warns) if unsupported/ungranted on a cold entry.
        /// </summary>
        public async Task EnterAsync()
        {
            switch (State)
            {
                case ArSessionState.Cold:
                    // Cold entry without a prior prewarm: prewarm now (decision #3), then run — unless
                    // the prewarm was refused (unsupported/ungranted), in which case PrewarmAsync left
                    // us Cold and we must not run.
                    await PrewarmAsync();
                    if (State == ArSessionState.Ready)
                    {
                        State = ArSessionState.Running;
                    }
                    return;

                case ArSessionState.Prewarming:
                    // A prewarm is mid-flight — await it, then run if it reached Ready.
                    if (_prewarmInFlight != null)
                    {
                        await _prewarmInFlight;
                    }
                    if (State == ArSessionState.Ready)
                    {
                        State = ArSessionState.Running;
                    }
                    return;

                case ArSessionState.Ready:
                    State = ArSessionState.Running;
                    return;

                default:
                    GameLog.Warn(
                        $"ArSessionService.EnterAsync ignored — already entered (state: {State}).");
                    return;
            }
        }

        /// <summary>
        /// The AC-3 cheap back-out: tear the session back to <see cref="ArSessionState.Cold"/> from any
        /// warm state (<see cref="ArSessionState.Prewarming"/>/<see cref="ArSessionState.Ready"/>/
        /// <see cref="ArSessionState.Running"/>/<see cref="ArSessionState.Paused"/>) via
        /// <see cref="IArSession.Stop"/>. Idempotent — a warned no-op from <see cref="ArSessionState.Cold"/>.
        /// </summary>
        public void TearDown()
        {
            if (State == ArSessionState.Cold)
            {
                GameLog.Warn("ArSessionService.TearDown ignored — already Cold.");
                return;
            }

            _session.Stop();

            // Set Cold FIRST, then clear the in-flight latch — both are load-bearing when TearDown races
            // an in-flight prewarm (State == Prewarming, _prewarmInFlight non-null):
            //  (1) State = Cold makes the in-flight StartThenReadyAsync continuation skip its
            //      "Prewarming → Ready" set (it keys on State == Prewarming), so it does NOT clobber the
            //      tear-back back to Ready;
            //  (2) clearing _prewarmInFlight here closes the double-start hole the CR found: without it,
            //      a subsequent PrewarmAsync from Cold would see a STALE non-null latch resolved via the
            //      State==Prewarming guard — no, worse, it would pass the Cold/CanStart path and fire a
            //      SECOND _session.StartAsync while the first is still in flight (two concurrent subsystem
            //      starts, exactly what decision #2 forbids). Nulling it here means the next prewarm
            //      starts clean. The first start's StartThenReadyAsync finally also nulls it on
            //      completion; nulling here too is safe (idempotent) and closes the racy window.
            // Note: IArSession.Stop is contractually safe to call while a StartAsync is in flight (it
            // aborts/cancels the pending start on the device path) — see IArSession.Stop's doc.
            State = ArSessionState.Cold;
            _prewarmInFlight = null;
        }

        /// <summary>
        /// The backgrounding handler (AC-3 "backgrounding handled inside"), pumped by the view's
        /// <c>OnApplicationPause</c>. <paramref name="paused"/> <c>true</c> while
        /// <see cref="ArSessionState.Running"/> → <see cref="IArSession.Pause"/> →
        /// <see cref="ArSessionState.Paused"/>. <paramref name="paused"/> <c>false</c> while
        /// <see cref="ArSessionState.Paused"/> → re-validate camera permission (the inherited 3.1
        /// deferral — a permission revoked while backgrounded must NOT silently resume): if still
        /// granted → <see cref="IArSession.Resume"/> → <see cref="ArSessionState.Running"/>; if revoked
        /// → <see cref="TearDown"/> to <see cref="ArSessionState.Cold"/> + raise
        /// <see cref="OnArSessionInterrupted"/>. Warned no-op in any other state.
        /// </summary>
        public void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                if (State != ArSessionState.Running)
                {
                    GameLog.Warn(
                        $"ArSessionService.OnApplicationPause(true) ignored — not running (state: {State}).");
                    return;
                }

                _session.Pause();
                State = ArSessionState.Paused;
                return;
            }

            // paused == false (focus-regain).
            if (State != ArSessionState.Paused)
            {
                GameLog.Warn(
                    $"ArSessionService.OnApplicationPause(false) ignored — not paused (state: {State}).");
                return;
            }

            // Re-validate camera permission on resume — Android can revoke it while backgrounded
            // (the inherited 3.1 deferral, decision #4). Evaluate() reads live OS truth (and re-routes
            // the shared flow to Disclosure on a revocation).
            if (_cameraPermission.Evaluate() != CameraPermissionState.Granted)
            {
                GameLog.Warn(
                    "ArSessionService.OnApplicationPause(false): camera permission was revoked while " +
                    "backgrounded — tearing down and raising OnArSessionInterrupted (re-grant required).");
                TearDown();
                RaiseInterrupted();
                return;
            }

            _session.Resume();
            State = ArSessionState.Running;
        }

        // The shared start-precondition check: device support + camera permission (the inherited 3.1
        // re-check, decision #4). Refusal is a warned no-op that leaves State untouched (NFR-3).
        private bool CanStart()
        {
            if (!_session.IsSupported)
            {
                GameLog.Warn(
                    "ArSessionService: the device does not support AR (IArSession.IsSupported == false) " +
                    "— refusing to start the session. Staying Cold.");
                return false;
            }

            if (_cameraPermission.Evaluate() != CameraPermissionState.Granted)
            {
                GameLog.Warn(
                    "ArSessionService: camera permission is not granted — refusing to start the session. " +
                    "Staying Cold (route to the re-grant flow).");
                return false;
            }

            return true;
        }

        private void RaiseInterrupted()
        {
            // The service's own state is already settled before this fires (a subscriber throwing cannot
            // corrupt it). But RaiseInterrupted runs ON the lifecycle pump (OnApplicationPause(false)),
            // which Unity calls directly — so a downstream subscriber (the Story 3.5 / Epic 4
            // EncounterStateMachine consumer) that throws would otherwise propagate the exception OUT of
            // OnApplicationPause and up through Unity's message-pump callback, breaking the "a
            // lifecycle-driven flow must never throw" invariant (NFR-3). Catch + log so a misbehaving
            // downstream subscriber can never crash the AR lifecycle pump. The service stays correct
            // regardless of what its subscribers do.
            try
            {
                OnArSessionInterrupted?.Invoke();
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    "ArSessionService.OnArSessionInterrupted: a subscriber threw — swallowed to keep the " +
                    $"lifecycle pump crash-free (NFR-3). The session is already torn down. {ex}");
            }
        }
    }
}
