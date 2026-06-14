using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Veilwalkers.AR;
using Veilwalkers.Core;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The thin, logic-free <see cref="MonoBehaviour"/> that binds <see cref="ArSessionService"/> to the
    /// AR-session lifecycle (Story 3.3): it pumps the backgrounding signal into the service and exposes
    /// the prewarm / enter / back-out trigger entry points for Onboarding/Home. It holds NO decisions —
    /// the cold→prewarming→ready→running→paused lifecycle, the double-start guard, the permission
    /// re-check, and the cheap tear-back all live in the headless-tested service.
    /// <para>
    /// <b>The ONLY locator reader here (AR-4).</b> Pure-logic classes are ctor-injected; only
    /// MonoBehaviours/UI read <see cref="GameServices"/>.
    /// </para>
    /// <para>
    /// <b>Graceful degrade (required)</b> — mirrors <c>CameraPermissionView</c>/<c>ArSafetyView</c>.
    /// <see cref="GameServices.Get{T}"/> throws <see cref="ServicesNotReadyException"/> before wiring and
    /// <see cref="KeyNotFoundException"/> if <see cref="ArSessionService"/> is unregistered. This view
    /// catches BOTH and stays inert (its actions no-op + warn), never throwing at <c>Awake</c>.
    /// </para>
    /// <para>
    /// <b>Backgrounding pump — <c>OnApplicationPause</c>, NOT <c>OnApplicationFocus</c>.</b> The AC-3
    /// "backgrounding handled inside <see cref="ArSessionService"/>" TRIGGER is Unity's
    /// <see cref="OnApplicationPause"/> (true == backgrounded); the DECISION (pause vs. permission-revoked
    /// teardown on resume) stays in the service. This deliberately does NOT copy
    /// <c>CameraPermissionView</c>'s <c>OnApplicationFocus</c> + <c>_sawFocusLossWhileOutstanding</c>
    /// latch — that defeats the Android permission-DIALOG focus-bounce race and is irrelevant to
    /// backgrounding. We copy that view's graceful-degrade + null-guard shape only.
    /// </para>
    /// <para>
    /// <b>Wiring is Epic 6.</b> The ACTUAL callers — Onboarding/Home gating <see cref="Prewarm"/> to when
    /// the AR-entry affordance is visible/likely (architecture.md:588-589), and the "Enter AR Hunt"
    /// affordance binding <see cref="EnterAr"/> — are Story 6.3 (AppStateMachine) / 6.4 (onboarding). The
    /// "veil-parting wipe" ritual prewarm cover (architecture.md:609-612) is Epic 6.5. This view has NO
    /// <c>Render</c> pixels (the session has no UI of its own); placing it + routing the triggers is
    /// downstream.
    /// </para>
    /// </summary>
    public sealed class ArSessionView : MonoBehaviour
    {
        private ArSessionService _session;

        private void Awake()
        {
            // The locator read — guarded against BOTH failure modes. Never throw at Awake: an inert
            // view is the correct seam if the service is not yet registered.
            try
            {
                _session = GameServices.Get<ArSessionService>();
            }
            catch (Exception ex) when (ex is ServicesNotReadyException || ex is KeyNotFoundException)
            {
                _session = null;
                GameLog.Warn(
                    "ArSessionView: ArSessionService is not available yet (services not ready, or the " +
                    "service is unregistered). The view is inert. " + ex.Message);
            }
        }

        /// <summary>
        /// Trigger the AR prewarm (AC-3) — called by Onboarding/Home when the AR-entry affordance is
        /// visible/likely (Epic 6 Story 6.3/6.4). Fire-and-forget with fault observation (mirroring
        /// Bootstrap's post-load continuation): the lifecycle owner is the service; this only kicks it.
        /// No-ops + warns when inert.
        /// </summary>
        public void Prewarm()
        {
            if (_session == null)
            {
                GameLog.Warn("ArSessionView.Prewarm ignored — the view is inert (service unregistered).");
                return;
            }

            ObserveFault(_session.PrewarmAsync(), nameof(Prewarm));
        }

        /// <summary>
        /// The player committed to AR Mode (the "Enter AR Hunt" affordance, Epic 6) → run the session
        /// (prewarming first if cold — prewarm is an optimization, not a precondition). No-ops + warns
        /// when inert.
        /// </summary>
        public void EnterAr()
        {
            if (_session == null)
            {
                GameLog.Warn("ArSessionView.EnterAr ignored — the view is inert.");
                return;
            }

            ObserveFault(_session.EnterAsync(), nameof(EnterAr));
        }

        /// <summary>
        /// The player backed out before committing (AC-3 cheap tear-back) → tear the session back to
        /// cold. No-ops + warns when inert.
        /// </summary>
        public void BackOut()
        {
            if (_session == null)
            {
                GameLog.Warn("ArSessionView.BackOut ignored — the view is inert.");
                return;
            }

            _session.TearDown();
        }

        // The backgrounding pump (AC-3). OnApplicationPause(true) == backgrounded; (false) == resumed.
        // The service owns the decision (pause vs. permission-revoked teardown on resume); this view is
        // a pure trigger. NOT OnApplicationFocus — see the class doc.
        private void OnApplicationPause(bool pauseStatus)
        {
            if (_session == null)
            {
                return;
            }

            _session.OnApplicationPause(pauseStatus);
        }

        // Observe a fire-and-forget lifecycle task's faults so a prewarm/enter failure never dies as an
        // unobserved task exception (mirrors Bootstrap's ContinueWith fault-logging). The service never
        // throws on a normal lifecycle path (NFR-3); this catches the unexpected.
        private static void ObserveFault(Task task, string op)
        {
            if (task == null)
            {
                return;
            }

            task.ContinueWith(
                t => GameLog.Error($"ArSessionView.{op}: lifecycle task faulted: {t.Exception?.GetBaseException()}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
