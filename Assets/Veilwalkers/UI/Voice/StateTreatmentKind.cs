namespace Veilwalkers.UI
{
    /// <summary>
    /// The on-brand "treatment" families a system state maps to (Story 6.5; UX-DR15). A standalone
    /// enum (the <c>OnboardingStep</c> / <c>AppSurface</c> precedent) so tests can pin members + order
    /// without flow accessibility. The <see cref="StateTreatmentPresenter"/> maps each existing state
    /// enum (AR session / placement / anchor-restore / camera permission / Codex discovery / safety
    /// gate) to one of these — it READS the owned state, it never re-classifies it.
    /// </summary>
    public enum StateTreatmentKind
    {
        /// <summary>No on-brand treatment for this state (the calm default — e.g. a warm/running
        /// session, a successful placement). Carries no copy.</summary>
        None,

        /// <summary>Cold-start / AR-warmup: the "veil-parting wipe" ritual (AC-3) — NEVER a spinner or
        /// progress bar; the slow-device fallback is MORE ritual, never a percentage.</summary>
        VeilPartingWipe,

        /// <summary>Plane-not-found: friendly coaching (AC-4) — the placement coaching copy, framed
        /// in-voice but plainly actionable.</summary>
        PlaneCoaching,

        /// <summary>Lost-anchor relocated: the "Pulled back through the Veil" restore beat (AC-4;
        /// architecture.md:472–473) — never vanish.</summary>
        AnchorRestoreBeat,

        /// <summary>Camera-denied: the re-grant path (AC-4) — surfaces the existing 3.1
        /// <c>CameraPermissionFlow.RetryFromSettings()</c> Settings round-trip; never a dead end.</summary>
        CameraReGrant,

        /// <summary>Codex first-discovery: the slot flip + count tick (+ new-tier silhouette
        /// fade-in) treatment decision (AC-4).</summary>
        CodexFirstDiscovery,

        /// <summary>The PLAIN AR Safety Warning (AC-2; the UX-DR14 EXCEPTION) — plain, legible,
        /// high-contrast copy, NEVER a Veil-voice costume.</summary>
        SafetyWarning,
    }
}
