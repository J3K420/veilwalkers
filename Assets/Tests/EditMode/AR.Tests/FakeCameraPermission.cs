using Veilwalkers.AR;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// Test double for <see cref="ICameraPermission"/> (Story 3.1). Drives REAL state — a settable
    /// <see cref="HasCameraPermission"/> the test flips to simulate "granted after the dialog" or
    /// "granted after returning from Settings", plus call counters so a test can prove the flow
    /// fired the OS prompt exactly once (and only after the disclosure) and opened settings exactly
    /// once. No Unity types; the flow logic is platform-independent and proven entirely here.
    /// </summary>
    internal sealed class FakeCameraPermission : ICameraPermission
    {
        /// <summary>Settable OS-grant state the flow re-queries. The test flips it to model the
        /// dialog/settings outcome.</summary>
        public bool HasCameraPermission { get; set; }

        /// <summary>How many times the flow fired the OS prompt. The AC-1 disclose-before-prompt
        /// rule asserts this is 0 until the disclosure is acknowledged, then exactly 1.</summary>
        public int RequestCount { get; private set; }

        /// <summary>How many times the flow opened the OS app-settings page (the AC-2 recovery
        /// path).</summary>
        public int OpenSettingsCount { get; private set; }

        public void RequestCameraPermission() => RequestCount++;

        public void OpenAppSettings() => OpenSettingsCount++;
    }
}
