namespace Veilwalkers.UI
{
    /// <summary>
    /// The narrow camera-disclosure seam the onboarding flow drives at its <see cref="OnboardingStep.Disclosure"/>
    /// step (Story 6.4, AC-2). It exists so <see cref="OnboardingFlow"/> can fire the OS-prompt request
    /// AFTER the player acknowledges the disclosure card — WITHOUT re-implementing camera-permission
    /// logic (that is Story-3.1 <c>CameraPermissionFlow</c>, in <c>Veilwalkers.AR</c>).
    /// <para>
    /// <b>Why a narrow port, not a direct <c>CameraPermissionFlow</c> reference.</b> <c>Veilwalkers.UI</c>
    /// already references <c>Veilwalkers.AR</c> (a sanctioned acyclic edge), so a direct reference would
    /// be LEGAL — but a 1-method port keeps the onboarding flow trivially fakeable in EditMode (the
    /// UI.Tests need NO <c>Veilwalkers.AR</c> reference) and keeps the flow's dependency surface to
    /// exactly the one thing it needs: "the disclosure was acknowledged, now fire the OS prompt." The
    /// adapter onto the real <c>CameraPermissionFlow.ConfirmDisclosureAndRequest()</c> is wired where the
    /// AR rig / onboarding View lives (deferred to Story 6.5; <see cref="NoDisclosure"/> is the
    /// graceful no-op until then).
    /// </para>
    /// </summary>
    public interface IDisclosureGate
    {
        /// <summary>
        /// The player acknowledged the camera-disclosure card → fire the OS permission prompt. Maps onto
        /// <c>CameraPermissionFlow.ConfirmDisclosureAndRequest()</c> (which is itself a no-op if not in
        /// the Disclosure state — so this is safe to call once per onboarding). Must never throw (NFR-3
        /// — a UI-driven flow is graceful); a fault is the adapter's to log + swallow.
        /// </summary>
        void ConfirmDisclosureAndRequest();

        /// <summary>
        /// A graceful no-op disclosure gate for when the camera-permission flow is not yet wired (the
        /// AR rig / onboarding View is a deferred seam). Onboarding still progresses through the
        /// disclosure step; no OS prompt fires. The <c>IEncounterSnapshotPort.NoEncounter</c> precedent
        /// (a static readonly field, matching that seam's exact shape).
        /// </summary>
        public static readonly IDisclosureGate NoDisclosure = new NoOpDisclosureGate();

        private sealed class NoOpDisclosureGate : IDisclosureGate
        {
            public void ConfirmDisclosureAndRequest() { }
        }
    }
}
