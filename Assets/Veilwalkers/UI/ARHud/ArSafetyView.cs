using System;
using System.Collections.Generic;
using UnityEngine;
using Veilwalkers.AR;
using Veilwalkers.Core;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The thin, logic-free <see cref="MonoBehaviour"/> that binds <see cref="ArSafetyGate"/> to the
    /// mandatory AR Safety Warning screens (Story 3.2): the full deliberate-read overlay (AC-2), the
    /// fast on-brand card (AC-2), and the acknowledged hand-off. It holds NO decisions — full-vs-fast,
    /// the Shop-resume skip, and the never-skippable block all live in the headless-tested gate.
    /// <para>
    /// <b>The ONLY locator reader here (AR-4).</b> Pure-logic classes are ctor-injected; only
    /// MonoBehaviours/UI read <see cref="GameServices"/>.
    /// </para>
    /// <para>
    /// <b>Graceful degrade (required)</b> — mirrors <c>CameraPermissionView</c>/<c>CodexGridView</c>.
    /// <see cref="GameServices.Get{T}"/> throws <see cref="ServicesNotReadyException"/> before wiring
    /// and <see cref="KeyNotFoundException"/> if <see cref="ArSafetyGate"/> is unregistered. This view
    /// catches BOTH and stays inert (its actions no-op + warn), never throwing at <c>Awake</c>.
    /// </para>
    /// <para>
    /// <b>No focus-pump.</b> Unlike <c>CameraPermissionView</c>, there is no OS round-trip to poll —
    /// the warning is acknowledged by an in-app button, not an OS dialog. Do NOT add
    /// <c>OnApplicationFocus</c> handling here.
    /// </para>
    /// <para>
    /// <b>This view does NOT own "the camera view is not interactive."</b> AC-1's block is the gate's
    /// <see cref="ArSafetyGate.MayInteract"/> predicate, which the camera/Lure/Encounter affordances
    /// (the AR-HUD, Epic 6; EncounterService entry, Epic 4) read before allowing any action. This view
    /// only renders which warning screen is up.
    /// </para>
    /// <para>
    /// <b>Wiring is Epic 6.</b> The full-overlay vs fast-card layout/copy, the plain-not-flavored
    /// safety voice (UX-DR14 EXCEPTION — safety messaging is plain, legible, high-contrast, never
    /// buried in flavor, so the Epic-6 implementation cannot A/B it away), the chunky overlay
    /// (UX-DR3), and the fast-card minimal-dwell timing are all Epic 6. <see cref="Render"/> is a
    /// deliberate stub. Placing this surface + supplying the <see cref="ArEntryKind"/> from AR-entry
    /// routing is Story 6.3; the session start the acknowledged state hands off to is Story 3.3.
    /// </para>
    /// </summary>
    public sealed class ArSafetyView : MonoBehaviour
    {
        private ArSafetyGate _gate;

        private void Awake()
        {
            // The locator read — guarded against BOTH failure modes. Never throw at Awake: an inert
            // screen is the correct seam if the gate is not yet registered.
            try
            {
                _gate = GameServices.Get<ArSafetyGate>();
            }
            catch (Exception ex) when (ex is ServicesNotReadyException || ex is KeyNotFoundException)
            {
                _gate = null;
                GameLog.Warn(
                    "ArSafetyView: ArSafetyGate is not available yet (services not ready, or the " +
                    "gate is unregistered). The screen is inert. " + ex.Message);
            }
        }

        /// <summary>
        /// Enter AR Mode with the caller-supplied <paramref name="kind"/> (AC-1/AC-3). The gate
        /// decides which warning (full/fast) to show, or skips it on a Shop resume. No-ops + warns
        /// when inert. The actual caller (the AR-entry routing that supplies the kind) is Epic 6
        /// (Story 6.3) / Story 3.3.
        /// </summary>
        public void EnterArMode(ArEntryKind kind)
        {
            if (_gate == null)
            {
                GameLog.Warn("ArSafetyView.EnterArMode ignored — the screen is inert (gate unregistered).");
                return;
            }

            Render(_gate.RequestEntry(kind));
        }

        /// <summary>The player tapped the Safety Warning's dismiss/continue action (AC-1 release).</summary>
        public void OnAcknowledge()
        {
            if (_gate == null)
            {
                GameLog.Warn("ArSafetyView.OnAcknowledge ignored — the screen is inert.");
                return;
            }

            Render(_gate.Acknowledge());
        }

        // The bind site. Logic-free BY DESIGN: NO decisions beyond choosing which screen group to
        // show — all of that is the gate's State. This story stubs the render to a log; the real
        // chunky warning UI lands in Epic 6.
        // TODO(Epic 6): bind the BlockingFull deliberate-read overlay (plain safety copy, UX-DR14
        // exception), the BlockingFastCard minimal-dwell card, and the Acknowledged hand-off to AR
        // entry (Story 3.3). The block itself (camera not interactive / no Encounter action) is the
        // gate's MayInteract predicate, read by the AR-HUD/Encounter entry (Epic 4/6).
        private void Render(ArSafetyGateState state)
        {
            switch (state)
            {
                case ArSafetyGateState.Inactive:
                    GameLog.Info("ArSafetyView: inactive (no warning showing).");
                    break;
                case ArSafetyGateState.BlockingFull:
                    GameLog.Info("ArSafetyView: showing the FULL AR Safety Warning (deliberate read) — interaction blocked.");
                    break;
                case ArSafetyGateState.BlockingFastCard:
                    GameLog.Info("ArSafetyView: showing the FAST AR Safety Warning card (minimal dwell) — interaction blocked.");
                    break;
                case ArSafetyGateState.Acknowledged:
                    // Hand-off signal only — AR entry (session start, Story 3.3) is not this story.
                    GameLog.Info("ArSafetyView: Safety Warning acknowledged — AR Mode may proceed.");
                    break;
            }
        }
    }
}
