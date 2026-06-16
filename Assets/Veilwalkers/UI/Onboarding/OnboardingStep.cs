namespace Veilwalkers.UI
{
    /// <summary>
    /// The phases of the first-launch onboarding shell (Story 6.4). A standalone enum (the
    /// <c>AppSurface</c> / <c>CameraPermissionState</c> precedent) so the UI.Tests can pin members +
    /// order without <see cref="OnboardingFlow"/> accessibility.
    /// <para>
    /// The order IS the legal flow — premise cards → camera disclosure (BEFORE the OS prompt, FR-5) →
    /// the 20-Credit coin-burst grant presentation → complete (which calls
    /// <c>AppStateMachine.CompleteOnboarding()</c>, the AR-entry gate). The enum is about PHASE, not the
    /// premise-card count: the 2–3 premise cards are walked inside <see cref="Premise"/> via a card
    /// index, not separate members.
    /// </para>
    /// </summary>
    public enum OnboardingStep
    {
        /// <summary>Walking the 2–3 chunky premise cards (the Veil is real / monsters are real / you're
        /// a Veilwalker). The START phase.</summary>
        Premise,

        /// <summary>The camera-disclosure card — shown BEFORE the OS permission dialog (AC-2, FR-5). The
        /// Continue tap fires the OS prompt via the disclosure gate, then advances.</summary>
        Disclosure,

        /// <summary>Presenting the 20-Credit first-launch grant (already performed by Story-1.7
        /// <c>FirstLaunchGrant</c> at boot) with a chunky coin-burst (AC-3).</summary>
        Grant,

        /// <summary>Onboarding is complete — <c>AppStateMachine.CompleteOnboarding()</c> has been called
        /// (Onboarding → Home) and the ENTER AR HUNT CTA is surfaced. The TERMINAL phase.</summary>
        Complete,
    }
}
