namespace Veilwalkers.AR
{
    /// <summary>
    /// The state a player is in with respect to camera permission (Story 3.1). The single source
    /// of truth the <see cref="CameraPermissionView"/> renders; computed by
    /// <see cref="CameraPermissionFlow"/>.
    /// </summary>
    public enum CameraPermissionState
    {
        /// <summary>
        /// Not yet granted — show the in-app camera-usage disclosure card BEFORE the OS prompt
        /// (AC-1). The disclosure must precede the prompt; the flow never jumps straight to
        /// <see cref="Requesting"/>.
        /// </summary>
        Disclosure,

        /// <summary>
        /// The OS permission dialog is in flight (the player acknowledged the disclosure and the
        /// prompt was fired). Resolves to <see cref="Granted"/> or <see cref="Denied"/> when focus
        /// returns and the flow re-queries.
        /// </summary>
        Requesting,

        /// <summary>
        /// Camera permission is granted — AR Mode may proceed. (This story only signals "may
        /// proceed"; the actual AR-entry trigger is the Story 3.2 safety gate / Story 3.3 session.)
        /// </summary>
        Granted,

        /// <summary>
        /// The player denied camera permission — show the explanatory re-grant screen with a path
        /// to system settings (AC-2). Never a dead end: a Settings round-trip can recover to
        /// <see cref="Granted"/>.
        /// </summary>
        Denied,
    }
}
