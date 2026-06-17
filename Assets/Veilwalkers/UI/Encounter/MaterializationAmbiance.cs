namespace Veilwalkers.UI
{
    /// <summary>
    /// How "active" the AR ambiance reads during a materialization (Story 4.7, AC-1; UX-DR10's
    /// "diffused dread scale"). One discrete step per <see cref="Veilwalkers.Monsters.Rarity"/>
    /// tier — Epic 6 maps each to tint / lighting / vignette / SLAY-glow tokens that shift TOWARD
    /// the active tier.
    /// <para>
    /// <b>CRITICAL (AC-1):</b> this is a DISCRETE, toward-the-tier descriptor — deliberately NOT a
    /// literal 0..1 rarity-bar value. The AC bans showing rarity "as a literal rarity bar"; the
    /// ambiance is felt (tint/lighting), never a numeric meter in chrome. That is why this is an
    /// enum, not a float.
    /// </para>
    /// </summary>
    public enum AmbianceIntensity
    {
        /// <summary>T1 (Common) — calm; the world barely stirs.</summary>
        Calm,

        /// <summary>T2 (Uncommon) — unsettled.</summary>
        Unsettled,

        /// <summary>T3 (Rare) — tense; the dread is noticeable.</summary>
        Tense,

        /// <summary>T4 (Epic) — dreadful.</summary>
        Dreadful,

        /// <summary>T5 (Nightmare) — nightmarish; the ambiance is at its most active (SLAY-glow on).</summary>
        Nightmarish,
    }

    /// <summary>
    /// The ambiance SHIFT for one materialization (Story 4.7, AC-1). A data-only
    /// <c>readonly struct</c> the (Epic-6) view consumes to tint/light/vignette the AR scene
    /// toward the active tier. Carries NO Unity <c>Color</c>/<c>Light</c> — those are Epic-6
    /// tokens resolved from <see cref="Intensity"/> at render time.
    /// <para>
    /// <b>Reduced-motion taming (Story 6.6, AC-1).</b> When <see cref="Tamed"/> is true the
    /// <see cref="EffectiveIntensity"/> is clamped one step toward calm (floored at
    /// <see cref="AmbianceIntensity.Calm"/>) so the per-tier ambiance plays at lower amplitude — but
    /// the <em>per-tier ordering is preserved</em> (a tamed Nightmare's effective intensity is still
    /// strictly higher than a tamed Common's), so the dread STILL reads (the 4.7 AC-3 invariant). The
    /// raw <see cref="Intensity"/> is unchanged (the variant + the "toward the tier" reading survive);
    /// only the rendered amplitude drops. Taming lowers MOTION/amplitude, it does NOT flatten the
    /// scale to a single step.
    /// </para>
    /// </summary>
    public readonly struct MaterializationAmbiance
    {
        /// <summary>The discrete dread-scale step for this tier (NOT a literal rarity bar — AC-1).
        /// This is the RAW per-tier step; reduced-motion taming lowers <see cref="EffectiveIntensity"/>,
        /// never this.</summary>
        public readonly AmbianceIntensity Intensity;

        /// <summary>
        /// Whether the SLAY-red tier-glow is on (the highest tier only — UX-DR9 reserves the glow
        /// for the AR encounter + the SLAY button tier-glow). Epic 6 renders the glow token.
        /// </summary>
        public readonly bool SlayGlow;

        /// <summary>
        /// Whether this ambiance is reduced-motion tamed (Story 6.6, AC-1): the rendered amplitude
        /// (<see cref="EffectiveIntensity"/>) is lowered while the per-tier ordering is preserved.
        /// </summary>
        public readonly bool Tamed;

        public MaterializationAmbiance(AmbianceIntensity intensity, bool slayGlow)
            : this(intensity, slayGlow, false)
        {
        }

        public MaterializationAmbiance(AmbianceIntensity intensity, bool slayGlow, bool tamed)
        {
            Intensity = intensity;
            SlayGlow = slayGlow;
            Tamed = tamed;
        }

        /// <summary>
        /// The intensity the render should play at. When <see cref="Tamed"/>, the raw
        /// <see cref="Intensity"/> is clamped ONE discrete step toward <see cref="AmbianceIntensity.Calm"/>
        /// (floored at Calm) — lower amplitude, no strobe/shake — while the ascending per-tier ordering is
        /// preserved (a step-down applied uniformly keeps higher tiers higher). Untamed, it is the raw
        /// intensity. This is the structural "dread still reads when tamed" guarantee (AC-1 / 4.7 AC-3).
        /// </summary>
        public AmbianceIntensity EffectiveIntensity =>
            Tamed && Intensity > AmbianceIntensity.Calm ? Intensity - 1 : Intensity;

        /// <summary>
        /// Build the reduced-motion tamed twin of this ambiance — same raw <see cref="Intensity"/> +
        /// <see cref="SlayGlow"/>, but <see cref="Tamed"/> true so the effective amplitude drops a step.
        /// </summary>
        public MaterializationAmbiance AsTamed() =>
            new MaterializationAmbiance(Intensity, SlayGlow, true);
    }
}
