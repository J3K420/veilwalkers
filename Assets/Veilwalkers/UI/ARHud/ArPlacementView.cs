using System;
using System.Collections.Generic;
using UnityEngine;
using Veilwalkers.AR;
using Veilwalkers.Core;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The thin, logic-free <see cref="MonoBehaviour"/> that binds <see cref="PlaneAnchorService"/> to the
    /// AR-HUD placement surface (Story 3.4): it forwards a placement request to the service and routes the
    /// typed <see cref="PlacementResult"/>. It holds NO decisions — the AC-1 place-on-detected-plane / AC-2
    /// coach-otherwise / NFR-3 typed-result logic all live in the headless-tested service.
    /// <para>
    /// <b>The ONLY locator reader here (AR-4).</b> Pure-logic classes are ctor-injected; only
    /// MonoBehaviours/UI read <see cref="GameServices"/>.
    /// </para>
    /// <para>
    /// <b>Graceful degrade (required)</b> — mirrors <c>ArSessionView</c>/<c>CameraPermissionView</c>/
    /// <c>ArSafetyView</c>. <see cref="GameServices.Get{T}"/> throws <see cref="ServicesNotReadyException"/>
    /// before wiring and <see cref="KeyNotFoundException"/> if <see cref="PlaneAnchorService"/> is
    /// unregistered. This view catches BOTH and stays inert (<see cref="RequestPlacement"/> no-ops + warns),
    /// never throwing at <c>Awake</c>.
    /// </para>
    /// <para>
    /// <b>Wiring is Epic 6.</b> The ACTUAL caller — the AR-HUD "tap/auto place" affordance — and the real
    /// coaching-banner pixels (the COPY lives in <see cref="PlaneAnchorService.CoachingMessage"/>; the
    /// styling/animation is Epic 6 UX), plus handing a <see cref="PlacementOutcome.Placed"/>
    /// <c>AnchorToken</c> to the Encounter (Epic 4), are downstream. This view has NO <c>Render</c> pixels;
    /// placing it + routing the trigger is Story 6.3.
    /// </para>
    /// </summary>
    public sealed class ArPlacementView : MonoBehaviour
    {
        private PlaneAnchorService _service;

        private void Awake()
        {
            // The locator read — guarded against BOTH failure modes. Never throw at Awake: an inert view
            // is the correct seam if the service is not yet registered.
            try
            {
                _service = GameServices.Get<PlaneAnchorService>();
            }
            catch (Exception ex) when (ex is ServicesNotReadyException || ex is KeyNotFoundException)
            {
                _service = null;
                GameLog.Warn(
                    "ArPlacementView: PlaneAnchorService is not available yet (services not ready, or the " +
                    "service is unregistered). The view is inert. " + ex.Message);
            }
        }

        /// <summary>
        /// Request a placement (the AR-HUD "tap/auto place" affordance, Epic 6) → ask the service to place
        /// on a detected plane, then route the typed result. <see cref="PlacementOutcome.NeedsCoaching"/> →
        /// show the coaching banner (Epic 6 pixels); <see cref="PlacementOutcome.Placed"/> → hand the
        /// <c>AnchorToken</c> to the Encounter (Epic 4); <see cref="PlacementOutcome.Failed"/> → guidance.
        /// No-ops + warns when inert. Returns the <see cref="PlacementResult"/> so a (future) caller/test
        /// can observe it; an inert view returns a coaching result by default (the safe "no spawn" outcome).
        /// </summary>
        public PlacementResult RequestPlacement()
        {
            if (_service == null)
            {
                GameLog.Warn("ArPlacementView.RequestPlacement ignored — the view is inert (service unregistered).");
                return PlacementResult.NeedsCoaching(PlaneAnchorService.CoachingMessage);
            }

            PlacementResult result = _service.TryPlace();

            // Epic-6 routing seam: the real coaching-banner draw / Encounter hand-off / guidance bind here.
            // 3.4 logs the outcome breakdown (the CodexGridView/ArSafetyView Render-stub precedent) and
            // returns the result; the pixels + Encounter wiring are downstream.
            GameLog.Info($"ArPlacementView.RequestPlacement → {result.Outcome} (TODO Epic 6: bind coaching banner / Encounter hand-off).");
            return result;
        }
    }
}
