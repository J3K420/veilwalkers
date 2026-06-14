using Veilwalkers.Core;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
using UnityEngine.Android;
#endif

namespace Veilwalkers.AR
{
    /// <summary>
    /// Production <see cref="ICameraPermission"/> over the Android OS permission surface (Story 3.1)
    /// — the untestable adapter edge, the <c>SystemClock : IClock</c> equivalent. It holds NO
    /// branching logic beyond the platform <c>#if</c> (the AR "thin adapter" mandate); all flow
    /// decisions live in <see cref="CameraPermissionFlow"/>.
    /// <para>
    /// <b>Platform split:</b> on a real Android device it wraps <c>UnityEngine.Android.Permission</c>
    /// (camera-only) and launches the app-settings Intent. Off-device (Editor / standalone), where
    /// the <c>Permission</c> API and the Android Intent are unavailable, it reports the camera as
    /// authorized so the flow is reachable in-editor, and the request / open-settings calls are
    /// logged no-ops. The flow's logic never depends on these side effects — only on a later
    /// <see cref="HasCameraPermission"/> re-query — so editor behavior matches the seam contract.
    /// </para>
    /// <para>
    /// <b>Camera only (AC-3):</b> nothing here touches location permission. World spawns are
    /// Phase-3; Location is never requested in the MVP.
    /// </para>
    /// </summary>
    public sealed class AndroidCameraPermission : ICameraPermission
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        public bool HasCameraPermission => Permission.HasUserAuthorizedPermission(Permission.Camera);

        public void RequestCameraPermission()
        {
            Permission.RequestUserPermission(Permission.Camera);
        }

        public void OpenAppSettings()
        {
            // Launch ACTION_APPLICATION_DETAILS_SETTINGS for this package so a player who denied
            // — including "Don't ask again" — can re-grant. This is the sole recovery path on a
            // permanently-denied device (AC-2). The result is observed by HasCameraPermission on
            // focus-regain, not here.
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    string pkg = activity.Call<string>("getPackageName");
                    using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                    // The third arg is a TYPED null ((string)null): Uri.fromParts(String, String,
                    // String) — a bare untyped null can fail JNI overload resolution and throw,
                    // killing the sole "Don't ask again" recovery path (AC-2). Pin the overload.
                    using (var uri = uriClass.CallStatic<AndroidJavaObject>("fromParts", "package", pkg, (string)null))
                    using (var intent = new AndroidJavaObject(
                        "android.content.Intent",
                        "android.settings.APPLICATION_DETAILS_SETTINGS",
                        uri))
                    using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                    {
                        // FLAG_ACTIVITY_NEW_TASK (0x10000000) — required to start an activity from a
                        // non-activity context cleanly.
                        intent.Call<AndroidJavaObject>("addFlags", 0x10000000);

                        // startActivity() returns void and can fail SILENTLY when no activity resolves
                        // the Intent (a restricted/OEM device with the settings screen locked down) —
                        // leaving the player stranded on the re-grant screen with no exception to catch
                        // (a dead end AC-2 forbids). Pre-check resolution and surface a diagnosable
                        // error instead of a silent no-launch.
                        using (var resolved = intent.Call<AndroidJavaObject>("resolveActivity", packageManager))
                        {
                            if (resolved == null)
                            {
                                GameLog.Error(
                                    "AndroidCameraPermission.OpenAppSettings: no activity resolves " +
                                    "ACTION_APPLICATION_DETAILS_SETTINGS on this device — the app-settings " +
                                    "page could not be opened (the player stays on the re-grant screen).");
                                return;
                            }

                            activity.Call("startActivity", intent);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                // Never crash the flow on a settings-launch failure (NFR-3): the player stays on
                // the re-grant screen and can retry. Log for diagnosis.
                GameLog.Error($"AndroidCameraPermission.OpenAppSettings failed: {ex}");
            }
        }
#else
        // Editor / non-Android: the Permission API and the Android Intent do not exist. Report the
        // camera as authorized so the disclose→grant flow is reachable in the Editor, and make the
        // request / open-settings calls logged no-ops (the flow does not depend on their side
        // effects). All flow LOGIC is proven against the fake in AR.Tests, platform-independent.
        public bool HasCameraPermission => true;

        public void RequestCameraPermission()
        {
            GameLog.Info("AndroidCameraPermission.RequestCameraPermission: no-op off-device (camera reported authorized).");
        }

        public void OpenAppSettings()
        {
            GameLog.Info("AndroidCameraPermission.OpenAppSettings: no-op off-device (no Android app-settings page).");
        }
#endif
    }
}
