using System;
using Veilwalkers.Core;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The pure-logic camera-permission flow (Story 3.1, FR-5): the ONE place that decides which
    /// screen the player sees — the disclosure card (AC-1), the OS prompt, the granted hand-off, or
    /// the denied re-grant screen (AC-2). Plain C#, ctor-injected with an
    /// <see cref="ICameraPermission"/> seam; NO <c>MonoBehaviour</c>, no Unity types, so it is
    /// headless-tested against a fake.
    /// <para>
    /// <b>This branching does NOT violate AR's "thin adapter, zero branching logic" mandate
    /// (decision #2).</b> That rule (architecture.md, AR boundary) targets the OS-TOUCHING adapter
    /// — <see cref="AndroidCameraPermission"/> stays a thin <c>#if</c> wrapper with no decisions.
    /// The disclose→prompt→denied→re-grant state machine IS branching worth testing, and the
    /// architecture's intent ("keep branching out of the adapter") is satisfied by isolating it
    /// here, behind the seam, fully covered by EditMode tests.
    /// </para>
    /// <para>
    /// <b>Synchronous, polled (decision #3):</b> Unity's <c>RequestUserPermission</c> returns void.
    /// <see cref="ConfirmDisclosureAndRequest"/> fires the prompt and moves to
    /// <see cref="CameraPermissionState.Requesting"/>; the view pumps <see cref="OnRequestResult"/>
    /// on focus-regain to re-query and resolve. No async/await.
    /// </para>
    /// <para>
    /// <b>Legal transitions</b> (illegal calls are a logged no-op returning the current state — a
    /// UI-driven flow must never throw, NFR-3):
    /// <list type="bullet">
    /// <item><see cref="Evaluate"/>: (any) → <see cref="CameraPermissionState.Granted"/> if already
    /// granted, else <see cref="CameraPermissionState.Disclosure"/>.</item>
    /// <item><see cref="ConfirmDisclosureAndRequest"/>: only from
    /// <see cref="CameraPermissionState.Disclosure"/> → <see cref="CameraPermissionState.Requesting"/>
    /// (fires the ONE prompt).</item>
    /// <item><see cref="OnRequestResult"/>: from <see cref="CameraPermissionState.Requesting"/> (or a
    /// Settings round-trip) → <see cref="CameraPermissionState.Granted"/> /
    /// <see cref="CameraPermissionState.Denied"/> by re-query.</item>
    /// <item><see cref="RetryFromSettings"/>: only from <see cref="CameraPermissionState.Denied"/> →
    /// opens settings, stays <see cref="CameraPermissionState.Denied"/> until a later re-query.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class CameraPermissionFlow
    {
        private readonly ICameraPermission _permission;

        /// <summary>The current state. Starts <see cref="CameraPermissionState.Disclosure"/> until
        /// the first <see cref="Evaluate"/> resolves it.</summary>
        public CameraPermissionState State { get; private set; } = CameraPermissionState.Disclosure;

        public CameraPermissionFlow(ICameraPermission permission)
        {
            _permission = permission ?? throw new ArgumentNullException(nameof(permission));
        }

        /// <summary>
        /// The entry decision, called when the player heads toward AR Mode (AC-1). If the OS has
        /// already granted camera permission → <see cref="CameraPermissionState.Granted"/>;
        /// otherwise → <see cref="CameraPermissionState.Disclosure"/> (the disclosure card, NOT the
        /// prompt — disclosure must precede the prompt). This NEVER fires the OS prompt.
        /// </summary>
        public CameraPermissionState Evaluate()
        {
            State = _permission.HasCameraPermission
                ? CameraPermissionState.Granted
                : CameraPermissionState.Disclosure;
            return State;
        }

        /// <summary>
        /// The player acknowledged the disclosure card → fire the OS prompt and move to
        /// <see cref="CameraPermissionState.Requesting"/>. This is the ONLY method that calls
        /// <see cref="ICameraPermission.RequestCameraPermission"/>, and only reachable from
        /// <see cref="CameraPermissionState.Disclosure"/> (the AC-1 disclose-before-prompt rule).
        /// An out-of-order call (already granted/requesting/denied) is a logged no-op.
        /// </summary>
        public CameraPermissionState ConfirmDisclosureAndRequest()
        {
            if (State != CameraPermissionState.Disclosure)
            {
                GameLog.Warn(
                    $"CameraPermissionFlow.ConfirmDisclosureAndRequest ignored — only valid from " +
                    $"Disclosure (current: {State}). No prompt fired.");
                return State;
            }

            _permission.RequestCameraPermission();
            State = CameraPermissionState.Requesting;
            return State;
        }

        /// <summary>
        /// Re-query permission after the OS dialog or a Settings round-trip closes (pumped by the
        /// view on focus-regain). Resolves to <see cref="CameraPermissionState.Granted"/> or
        /// <see cref="CameraPermissionState.Denied"/>. Safe to call from
        /// <see cref="CameraPermissionState.Requesting"/> OR <see cref="CameraPermissionState.Denied"/>
        /// (the recovery case); a stray call before any request resolves on current OS state too.
        /// </summary>
        public CameraPermissionState OnRequestResult()
        {
            State = _permission.HasCameraPermission
                ? CameraPermissionState.Granted
                : CameraPermissionState.Denied;
            return State;
        }

        /// <summary>
        /// From <see cref="CameraPermissionState.Denied"/>, the player tapped "Open Settings" →
        /// open the OS app-settings page (AC-2's path to system settings) and stay
        /// <see cref="CameraPermissionState.Denied"/> until they return and <see cref="OnRequestResult"/>
        /// re-queries (which may flip to <see cref="CameraPermissionState.Granted"/>). This is the
        /// SOLE recovery on a "Don't ask again" device, so it is load-bearing, not a nicety. An
        /// out-of-state call is a logged no-op.
        /// </summary>
        public CameraPermissionState RetryFromSettings()
        {
            if (State != CameraPermissionState.Denied)
            {
                GameLog.Warn(
                    $"CameraPermissionFlow.RetryFromSettings ignored — only valid from Denied " +
                    $"(current: {State}). Settings not opened.");
                return State;
            }

            _permission.OpenAppSettings();
            // Stay Denied: the re-grant happens in the OS Settings UI; the result is observed by
            // OnRequestResult() when the player returns and the app regains focus.
            return State;
        }
    }
}
