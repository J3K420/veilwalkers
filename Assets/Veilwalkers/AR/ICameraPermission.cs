namespace Veilwalkers.AR
{
    /// <summary>
    /// The platform-permission seam over the OS camera-permission surface (Story 3.1, FR-5).
    /// It exists so the disclose→prompt→denied→re-grant decision (<see cref="CameraPermissionFlow"/>)
    /// is headless-testable: the flow is ctor-injected with this interface and a fake drives it,
    /// while the production <see cref="AndroidCameraPermission"/> is the thin, untestable adapter
    /// over <c>UnityEngine.Android.Permission</c> (the same shape as
    /// <c>IClock</c>/<c>SystemClock</c> in Core).
    /// <para>
    /// <b>Lives in AR, not Core (decision #1):</b> this is an Android-platform behavior — like
    /// <c>IArAnchorProvider</c> it belongs in the AR area, and the flow that consumes it lives in
    /// AR too (architecture.md maps <c>FR-5 → AR/CameraPermissionFlow</c>). Core stays platform-free.
    /// </para>
    /// <para>
    /// <b>Camera ONLY — Location is deliberately absent (AC-3, FR-5):</b> there is intentionally NO
    /// location member here, so no flow path can request location permission. World spawns are a
    /// Phase-3 feature; Location is never requested in the MVP. The AC-3 test pins that the AR
    /// assembly references no location permission API.
    /// </para>
    /// <para>
    /// <b>Synchronous by design (decision #3):</b> <c>Permission.RequestUserPermission</c> returns
    /// <c>void</c> in Unity — the dialog result is observed by re-querying
    /// <see cref="HasCameraPermission"/> after the app regains focus, NOT awaited. So this seam is
    /// plain synchronous shape; the flow stays deterministically testable.
    /// </para>
    /// </summary>
    public interface ICameraPermission
    {
        /// <summary>
        /// True when the OS has granted camera permission. Wraps
        /// <c>Permission.HasUserAuthorizedPermission(Permission.Camera)</c>. Re-queried after the
        /// permission dialog or a Settings round-trip closes (focus-regain) to resolve the result.
        /// </summary>
        bool HasCameraPermission { get; }

        /// <summary>
        /// Fire the OS camera-permission dialog. Wraps
        /// <c>Permission.RequestUserPermission(Permission.Camera)</c> — the result is NOT returned
        /// here (void); it arrives by a later <see cref="HasCameraPermission"/> re-query. On a
        /// "Don't ask again" / permanently-denied device this is a silent OS no-op, which is why
        /// <see cref="OpenAppSettings"/> is the mandatory recovery path (AC-2).
        /// </summary>
        void RequestCameraPermission();

        /// <summary>
        /// Open the OS app-settings page (the AC-2 "path to system settings") so a player who
        /// denied — including permanently — can re-grant. The production impl launches the Android
        /// <c>ACTION_APPLICATION_DETAILS_SETTINGS</c> Intent; off-device it is a logged no-op. The
        /// flow never depends on the side effect itself, only on the subsequent
        /// <see cref="HasCameraPermission"/> re-query when focus returns.
        /// </summary>
        void OpenAppSettings();
    }
}
