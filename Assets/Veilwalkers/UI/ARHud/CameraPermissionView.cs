using System;
using System.Collections.Generic;
using UnityEngine;
using Veilwalkers.AR;
using Veilwalkers.Core;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The thin, logic-free <see cref="MonoBehaviour"/> that binds <see cref="CameraPermissionFlow"/>
    /// to the camera-permission screens (Story 3.1): the in-app disclosure card (AC-1), the OS-prompt
    /// waiting state, the granted hand-off, and the denied re-grant screen with a Settings path
    /// (AC-2). It holds NO decisions — which screen to show, the disclose-before-prompt rule, and the
    /// never-a-dead-end recovery all live in the headless-tested flow.
    /// <para>
    /// <b>The ONLY locator reader here (AR-4).</b> Pure-logic classes are ctor-injected; only
    /// MonoBehaviours/UI read <see cref="GameServices"/>.
    /// </para>
    /// <para>
    /// <b>Graceful degrade (required)</b> — mirrors <c>CodexGridView</c>/<c>CodexDetailView</c>.
    /// <see cref="GameServices.Get{T}"/> throws <see cref="ServicesNotReadyException"/> before wiring
    /// and <see cref="KeyNotFoundException"/> if <see cref="CameraPermissionFlow"/> is unregistered.
    /// This view catches BOTH and stays inert (its actions no-op + warn), never throwing at
    /// <c>Awake</c>.
    /// </para>
    /// <para>
    /// <b>Polled result (decision #3):</b> the OS dialog and the Settings round-trip both resolve on
    /// focus-regain, so <see cref="OnApplicationFocus"/> pumps <see cref="CameraPermissionFlow.OnRequestResult"/>
    /// while a request is outstanding. No callbacks, no async.
    /// </para>
    /// <para>
    /// <b>Wiring is Epic 6.</b> The disclosure-card copy/layout, the re-grant-screen styling (chunky
    /// components UX-DR3), and the plain-not-flavored permission voice (UX-DR14 exception) are
    /// UX-DR3/DR19 (Stories 6.2/6.4); placing this surface + routing AR entry from it is Story 6.3.
    /// <see cref="Render"/> is a deliberate stub. This story stops at "permission resolved → may
    /// proceed / denied → re-grant"; it does NOT start an AR session (Story 3.3) or show the Safety
    /// Warning (Story 3.2).
    /// </para>
    /// </summary>
    public sealed class CameraPermissionView : MonoBehaviour
    {
        private CameraPermissionFlow _flow;

        // Guards the focus-regain re-query against Android's permission-dialog lifecycle race:
        // presenting the OS dialog (or opening Settings) first fires OnApplicationFocus(false) then
        // (true) BEFORE the player answers. Without this latch, that spurious regain would re-query
        // while the dialog is still up, see permission still false, and flip Requesting→Denied —
        // throwing the player onto the re-grant screen mid-dialog. We only resolve on a regain that
        // was PRECEDED by a real focus-loss (the dialog/Settings genuinely closed). Set when focus is
        // lost while a request is outstanding; consumed on the next regain.
        private bool _sawFocusLossWhileOutstanding;

        private void Awake()
        {
            // The locator read — guarded against BOTH failure modes. Never throw at Awake: an inert
            // screen is the correct seam if the flow is not yet registered.
            try
            {
                _flow = GameServices.Get<CameraPermissionFlow>();
            }
            catch (Exception ex) when (ex is ServicesNotReadyException || ex is KeyNotFoundException)
            {
                _flow = null;
                GameLog.Warn(
                    "CameraPermissionView: CameraPermissionFlow is not available yet (services not " +
                    "ready, or the flow is unregistered). The screen is inert. " + ex.Message);
            }
        }

        /// <summary>
        /// Begin the permission flow as the player heads toward AR Mode (AC-1). Evaluates current OS
        /// state: already granted → may proceed; otherwise shows the disclosure card. No-ops + warns
        /// when inert. The actual caller is Epic 6's onboarding/AR-entry routing (Story 6.4/6.3).
        /// </summary>
        public void BeginTowardArMode()
        {
            if (_flow == null)
            {
                GameLog.Warn("CameraPermissionView.BeginTowardArMode ignored — the screen is inert (flow unregistered).");
                return;
            }

            Render(_flow.Evaluate());
        }

        /// <summary>The player tapped "Continue" on the disclosure card → fire the OS prompt (AC-1).</summary>
        public void OnDisclosureContinue()
        {
            if (_flow == null)
            {
                GameLog.Warn("CameraPermissionView.OnDisclosureContinue ignored — the screen is inert.");
                return;
            }

            Render(_flow.ConfirmDisclosureAndRequest());
        }

        /// <summary>The player tapped "Open Settings" on the re-grant screen (AC-2).</summary>
        public void OnOpenSettings()
        {
            if (_flow == null)
            {
                GameLog.Warn("CameraPermissionView.OnOpenSettings ignored — the screen is inert.");
                return;
            }

            Render(_flow.RetryFromSettings());
        }

        // The OS dialog and the Settings round-trip both return focus to the app rather than firing a
        // callback, so re-query on focus-regain while a request is outstanding (Requesting after the
        // prompt, or Denied while the player is in Settings). Granted/Disclosure need no re-query.
        //
        // BUT only resolve on a regain that was preceded by a genuine focus-LOSS — on Android the
        // dialog/Settings activity causes focus to drop then return, and the FIRST focus-regain often
        // arrives while the dialog is still on screen (the player hasn't answered). Re-querying then
        // would prematurely flip Requesting→Denied and bury the live dialog under the re-grant screen.
        // The _sawFocusLossWhileOutstanding latch distinguishes "regained because the dialog opened"
        // (no prior loss recorded → ignore) from "regained because the dialog closed" (loss recorded
        // → resolve).
        private void OnApplicationFocus(bool hasFocus)
        {
            if (_flow == null)
            {
                return;
            }

            bool outstanding = _flow.State == CameraPermissionState.Requesting
                || _flow.State == CameraPermissionState.Denied;

            if (!hasFocus)
            {
                // Record the focus-loss only while a request is outstanding — this is the dialog or
                // Settings activity taking the foreground. The matching regain is the real result.
                if (outstanding)
                {
                    _sawFocusLossWhileOutstanding = true;
                }
                return;
            }

            // hasFocus == true: only resolve if a real focus-loss preceded this regain.
            if (outstanding && _sawFocusLossWhileOutstanding)
            {
                _sawFocusLossWhileOutstanding = false;
                Render(_flow.OnRequestResult());
            }
        }

        // The bind site. Logic-free BY DESIGN: NO decisions beyond choosing which screen group to
        // show — all of that is the flow's State. This story stubs the render to a log; the real
        // chunky disclosure/re-grant UI lands in Epic 6.
        // TODO(Epic 6): bind the Disclosure card (plain camera-usage copy, UX-DR14 exception),
        // the Requesting waiting state, the Granted hand-off to AR entry (Story 3.2/3.3), and the
        // Denied re-grant screen with the "Open Settings" action (AC-2).
        private void Render(CameraPermissionState state)
        {
            switch (state)
            {
                case CameraPermissionState.Disclosure:
                    GameLog.Info("CameraPermissionView: showing camera-usage disclosure (before the OS prompt).");
                    break;
                case CameraPermissionState.Requesting:
                    GameLog.Info("CameraPermissionView: OS permission dialog in flight…");
                    break;
                case CameraPermissionState.Granted:
                    // Hand-off signal only — AR entry (safety gate 3.2 / session 3.3) is not this story.
                    GameLog.Info("CameraPermissionView: camera granted — AR Mode may proceed.");
                    break;
                case CameraPermissionState.Denied:
                    GameLog.Info("CameraPermissionView: camera denied — showing the re-grant screen (Open Settings to recover).");
                    break;
            }
        }
    }
}
