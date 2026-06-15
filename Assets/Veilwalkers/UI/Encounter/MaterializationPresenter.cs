using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// Pure-logic decision for a Monster's materialization entrance (Story 4.7). Given the
    /// spawned Monster's <see cref="Rarity"/> tier + the reduced-motion setting, it produces a
    /// <see cref="MaterializationPlan"/>: the per-tier entrance variant + duration (AC-1), the
    /// action-bar-hidden gate (AC-1), the T5 branded-glitch flag bounded so it never blocks input
    /// (AC-2), and the reduced-motion taming that keeps the dread reading (AC-3). NO Unity types,
    /// NO <c>MonoBehaviour</c>, NO service-locator reads — dependency-free + headless-testable.
    /// The thin <see cref="MaterializationView"/> binds the plan to the (Epic-6) render.
    /// <para>
    /// <b>Mirrors <see cref="CodexGridPresenter"/>:</b> a pure recompute, a fresh value every call,
    /// no internal mutable state, no lifecycle/clock — the view owns the clock (when the entrance
    /// has "settled" after <see cref="MaterializationPlan.DurationSeconds"/>).
    /// </para>
    /// <para>
    /// <b>NFR-2:</b> <see cref="PlanFor(Rarity,bool)"/> is a synchronous, side-effect-free,
    /// no-I/O, no-economy call — it touches no Credits and awaits nothing. The materialization
    /// duration lives in plan DATA the render consumes, never in a blocking call, so the
    /// sub-second credit spend-ack (delivered earlier by Story 4.2's <c>LureResult</c>) is never
    /// gated by the entrance length.
    /// </para>
    /// </summary>
    public sealed class MaterializationPresenter
    {
        // ---- per-tier nominal entrance durations (seconds) ----
        // Mid-band values within each AC band (T1 ~0.5s … T5 ~2.5–3s). They are presenter consts,
        // NOT EconomyConfig fields — these are UX timings, not economy values (the
        // LureSystem.BasicRareChance precedent). The BEHAVIOR is the contract (monotonic
        // non-decreasing with tier, each within its band); the exact value is Epic-6/OQ-9 polish.
        internal const float CommonDuration = 0.5f;     // T1 Pop-in
        internal const float UncommonDuration = 1.0f;   // T2 Unfurl
        internal const float RareDuration = 1.5f;       // T3 Seep
        internal const float EpicDuration = 2.0f;       // T4 Tear
        internal const float NightmareDuration = 2.75f; // T5 Breach (within the ~2.5–3s band)

        /// <summary>
        /// Build the materialization plan for a tier. <paramref name="reducedMotion"/> ON tames
        /// the plan (no glitch/shake/strobe) while preserving the entrance variant + ambiance so
        /// the dread still reads (AC-3). Overload of
        /// <see cref="PlanFor(string,Rarity,bool)"/> with an empty Monster-id label.
        /// </summary>
        public MaterializationPlan PlanFor(Rarity tier, bool reducedMotion)
        {
            return PlanFor(null, tier, reducedMotion);
        }

        /// <summary>
        /// Build the materialization plan for a spawned Monster. <paramref name="monsterId"/> is a
        /// label carried onto the plan (the <c>LureResult</c> spawned id the render animates); a
        /// null/empty id coalesces to empty and does NOT throw — the tier drives the plan
        /// (the <c>LureResult</c>/<c>ScanResult</c> factory precedent).
        /// </summary>
        public MaterializationPlan PlanFor(string monsterId, Rarity tier, bool reducedMotion)
        {
            MaterializationVariant variant = VariantFor(tier);
            float duration = DurationFor(tier);
            MaterializationAmbiance ambiance = AmbianceFor(tier);

            // The branded glitch (UX-DR11) is the T5 Breach only — AND it is tamed OFF entirely
            // under reduced motion (glitch→static branded transition, UX-DR17/AC-3). The variant
            // and the ambiance are PRESERVED when tamed: taming is about MOTION (strobe/shake/
            // glitch), not removing the entrance — so a tamed Nightmare is still a Breach with
            // Nightmarish ambiance, the dread still reads.
            bool brandedGlitch = !reducedMotion && tier == Rarity.Nightmare;

            return MaterializationPlan.Create(
                tier,
                monsterId,
                variant,
                duration,
                brandedGlitch,
                reducedMotion,
                ambiance);
        }

        /// <summary>
        /// The action-bar gate (UX-DR12): the bar is hidden during materialization and pops in
        /// ONLY when the entrance has settled. The view owns the clock and passes
        /// <paramref name="materializationSettled"/> (true after the plan's duration elapses);
        /// the presenter owns the decision. Clock-free, so the gate is testable without a timer.
        /// </summary>
        public bool ShouldShowActionBar(bool materializationSettled)
        {
            return materializationSettled;
        }

        // ---- the per-tier maps (the AC-1 contract) ----

        private static MaterializationVariant VariantFor(Rarity tier)
        {
            switch (tier)
            {
                case Rarity.Common: return MaterializationVariant.PopIn;
                case Rarity.Uncommon: return MaterializationVariant.Unfurl;
                case Rarity.Rare: return MaterializationVariant.Seep;
                case Rarity.Epic: return MaterializationVariant.Tear;
                case Rarity.Nightmare: return MaterializationVariant.Breach;
                // Append-only Rarity (never reorder) — an unmapped tier is a programming error, but
                // degrade to the calmest entrance rather than throw at the render boundary (NFR-3).
                default: return MaterializationVariant.PopIn;
            }
        }

        private static float DurationFor(Rarity tier)
        {
            switch (tier)
            {
                case Rarity.Common: return CommonDuration;
                case Rarity.Uncommon: return UncommonDuration;
                case Rarity.Rare: return RareDuration;
                case Rarity.Epic: return EpicDuration;
                case Rarity.Nightmare: return NightmareDuration;
                default: return CommonDuration;
            }
        }

        private static MaterializationAmbiance AmbianceFor(Rarity tier)
        {
            switch (tier)
            {
                case Rarity.Common: return new MaterializationAmbiance(AmbianceIntensity.Calm, false);
                case Rarity.Uncommon: return new MaterializationAmbiance(AmbianceIntensity.Unsettled, false);
                case Rarity.Rare: return new MaterializationAmbiance(AmbianceIntensity.Tense, false);
                case Rarity.Epic: return new MaterializationAmbiance(AmbianceIntensity.Dreadful, false);
                // SLAY-glow on the highest tier only (UX-DR9 reserves the glow for the encounter).
                case Rarity.Nightmare: return new MaterializationAmbiance(AmbianceIntensity.Nightmarish, true);
                default: return new MaterializationAmbiance(AmbianceIntensity.Calm, false);
            }
        }
    }
}
