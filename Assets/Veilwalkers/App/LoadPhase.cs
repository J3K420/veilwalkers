namespace Veilwalkers.App
{
    /// <summary>
    /// The staged cold-start contract (Story 3.3, AC-2; architecture.md:592-594, AR-14). Bootstrap
    /// wires essential services synchronously, while AR warmup + first-Monster preload run on the async
    /// path and are NOT awaited before Home is interactive (AC-CS-2 — the regression guard CI asserts,
    /// NOT a millisecond budget, since there is no <c>ARSession</c> in EditMode).
    /// <para>
    /// A standalone <c>public enum</c> (not nested in <see cref="Bootstrap"/>) so the AC-2 staging-
    /// contract test can assert its members + order from <c>Veilwalkers.App.Tests</c> without needing
    /// <see cref="Bootstrap"/> accessibility.
    /// </para>
    /// <para>
    /// <b>Warmup ownership rule (architecture.md:583-590):</b> Bootstrap owns this staging SHAPE
    /// (essentials sync; AR off the awaited path), but does NOT eagerly trigger the prewarm — Onboarding
    /// / Home own the <see cref="Veilwalkers.AR.ArSessionService.PrewarmAsync"/> trigger, gated to when
    /// the AR-entry affordance is visible/likely (warming AR before the player is near it burns
    /// battery/GPU). The trigger lives outside the owner; the lifecycle stays inside it.
    /// </para>
    /// </summary>
    public enum LoadPhase
    {
        /// <summary>
        /// Essential services wire synchronously (the existing Bootstrap wiring — save, economy,
        /// permission, safety gate, AR session service registration). Home cannot be interactive before
        /// this completes.
        /// </summary>
        EssentialSync,

        /// <summary>
        /// AR session warmup (<see cref="Veilwalkers.AR.ArSessionService.PrewarmAsync"/>, triggered by
        /// Onboarding/Home) + first-Monster asset preload run on the async path. <b>NOT awaited before
        /// Home is interactive (AC-CS-2).</b> The actual prewarm trigger + the Monster preload call site
        /// are downstream (Epic 6 Home/Onboarding; the Editor-authoring session that closes the 2.2
        /// <c>MonsterDatabase.asset</c> deferral) — 3.3 lands the staging contract, not the call sites.
        /// </summary>
        WarmupAsync,

        /// <summary>
        /// Home is interactive. AR warmup may still be completing on the async path — by the time the
        /// player taps "Enter AR Hunt", the session (and ≥1 Monster) is warm, but Home does not block on
        /// it.
        /// </summary>
        Ready,
    }
}
