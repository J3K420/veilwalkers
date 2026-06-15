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
    /// </summary>
    public readonly struct MaterializationAmbiance
    {
        /// <summary>The discrete dread-scale step for this tier (NOT a literal rarity bar — AC-1).</summary>
        public readonly AmbianceIntensity Intensity;

        /// <summary>
        /// Whether the SLAY-red tier-glow is on (the highest tier only — UX-DR9 reserves the glow
        /// for the AR encounter + the SLAY button tier-glow). Epic 6 renders the glow token.
        /// </summary>
        public readonly bool SlayGlow;

        public MaterializationAmbiance(AmbianceIntensity intensity, bool slayGlow)
        {
            Intensity = intensity;
            SlayGlow = slayGlow;
        }
    }
}
