namespace Veilwalkers.App
{
    /// <summary>
    /// The top-level navigable surfaces of the app (Story 6.3; architecture.md:221, 283). The
    /// <see cref="AppStateMachine"/> moves between these in the legal flow Onboarding → Home →
    /// AR Hunt / Codex / Shop.
    /// <para>
    /// A standalone <c>public enum</c> (not nested in <see cref="AppStateMachine"/>) — the
    /// <see cref="LoadPhase"/> precedent — so the <c>Veilwalkers.App.Tests</c> contract test can
    /// assert its members from outside without needing the state machine's accessibility.
    /// </para>
    /// </summary>
    public enum AppSurface
    {
        /// <summary>The first-launch onboarding shell (premise + camera disclosure + grant). The
        /// START surface; <see cref="AppStateMachine.CompleteOnboarding"/> is the ONLY exit (→
        /// <see cref="Home"/>). The onboarding CONTENT is Story 6.4; 6.3 owns only the gate.</summary>
        Onboarding,

        /// <summary>The home hub — wordmark, credit pill, ENTER AR HUNT, Codex (X/67), Shop, daily
        /// reward. The pivot the other surfaces return to.</summary>
        Home,

        /// <summary>The full-bleed AR hunting surface (floating chunky chrome islands). Reached from
        /// Home only after onboarding completes; cold-entry fires the FR-3 safety warning (Story 3.2),
        /// a Shop-resume re-entry does not (architecture.md:482-486).</summary>
        ArHunt,

        /// <summary>The 67-monster Codex grid (Story 2.4). Reached from Home; returns to Home.</summary>
        Codex,

        /// <summary>The credit-pack Shop (Story 5.1). Reached from Home, from the AR Shop entry, OR
        /// auto-navigated on an insufficient-credits shortfall (AC-2). Returns to the surface that was
        /// current when it was opened.</summary>
        Shop,
    }
}
