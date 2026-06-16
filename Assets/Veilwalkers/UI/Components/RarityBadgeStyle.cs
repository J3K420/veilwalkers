using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The rarity-tier badge component descriptor (Story 6.2; UX-DR7). A pure-logic
    /// <c>readonly struct</c>: a small <c>sm</c> outlined badge color-coded to a <see cref="Rarity"/>'s
    /// dread-scale token (T1…T5) — the ONE place tier color lives in chrome (the Codex detail page).
    /// The render is Story 6.3.
    /// <para>
    /// The badge color is resolved via the single <see cref="DreadScaleTokens.ForTier"/> resolver — this
    /// component does NOT re-implement the tier→color map (Story 6.1 owns it; a future 6th tier extends
    /// 6.1's switch, not this badge). For T5 (Nightmare) the badge carries the full bruise-rot
    /// <see cref="TierGradient"/> (Start ≠ End); T1–T4 are solid (Start == End).
    /// </para>
    /// </summary>
    public readonly struct RarityBadgeStyle
    {
        /// <summary>The chunky elevation style — sm radius (the SMALL badge), the tier-token fill, hard shadow.</summary>
        public ChunkyStyle Style { get; }

        /// <summary>The tier this badge represents.</summary>
        public Rarity Tier { get; }

        /// <summary>
        /// The tier's dread-scale token, resolved via the single <see cref="DreadScaleTokens.ForTier"/>
        /// map. Carries both stops — T5 is the only true (two-stop) gradient; T1–T4 are solid.
        /// </summary>
        public TierGradient TierColor { get; }

        /// <summary>True iff this is the T5 (Nightmare) gradient badge — the renderer paints the
        /// bruise-rot ramp Start→End rather than a flat fill.</summary>
        public bool IsGradient => !TierColor.IsSolid;

        private RarityBadgeStyle(ChunkyStyle style, Rarity tier, TierGradient tierColor)
        {
            Style = style;
            Tier = tier;
            TierColor = tierColor;
        }

        /// <summary>
        /// Build the <c>sm</c> rarity badge for a <paramref name="tier"/>. The badge fill is the tier's
        /// gradient START (the flat fill for T1–T4; the ramp start for T5, the renderer paints the full
        /// ramp from <see cref="TierColor"/>). The sm radius + hard shadow come from Story-6.1 tokens.
        /// </summary>
        public static RarityBadgeStyle For(Rarity tier)
        {
            TierGradient color = DreadScaleTokens.ForTier(tier);
            return new RarityBadgeStyle(
                ChunkyStyle.Elevated(color.Start, PumpkinPatchTokens.RadiusSm),
                tier,
                color);
        }
    }
}
