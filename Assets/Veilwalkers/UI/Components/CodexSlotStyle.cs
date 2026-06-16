using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The Codex-slot component descriptor (Story 6.2; UX-DR5). A pure-logic <c>readonly struct</c>:
    /// a chunky outlined square that renders one of the THREE Story-2.4 <see cref="CodexSlotState"/>
    /// states — Discovered (full art + a tier-tinted "caught" stamp) / <c>???</c> silhouette / <c>?</c>
    /// blank. The render (art, the caught stamp, the silhouette→art flip + count-tick animation) is
    /// Story 6.3 / 6.5.
    /// <para>
    /// <b>Renders state, does NOT re-classify (critical).</b> This component CONSUMES the
    /// <see cref="CodexSlotState"/> the <c>CodexGridPresenter</c> (Story 2.4) already computed — it does
    /// NOT re-derive the begun-tier / discovered logic (that lives in 2.4's presenter; duplicating it
    /// is the "reinvent the wheel" trap). The tier tint is resolved via
    /// <see cref="DreadScaleTokens.ForTier"/> (the single tier→token resolver) ONLY for a
    /// <see cref="CodexSlotState.Discovered"/> slot with a known tier (UX-DR2/DR7 — tier color in chrome
    /// lives only on the caught stamp + the rarity badge; never a rarity bar/strip/legend).
    /// </para>
    /// </summary>
    public readonly struct CodexSlotStyle
    {
        /// <summary>The chunky elevation style — surface fill, md radius (the outlined square), hard shadow.</summary>
        public ChunkyStyle Style { get; }

        /// <summary>The reveal state this slot renders (from the 2.4 presenter — never re-derived here).</summary>
        public CodexSlotState State { get; }

        /// <summary>
        /// The slot's authored <see cref="Rarity"/>, or <c>null</c> for a reserved-but-unauthored id
        /// (a <c>Blank</c> with no known tier). Drives the caught-stamp tier tint — the 2.4
        /// <c>CodexSlot.Tier</c> nullable contract.
        /// </summary>
        public Rarity? Tier { get; }

        /// <summary>
        /// Whether the slot shows the tier-tinted "caught" stamp — true ONLY when the slot is
        /// <see cref="CodexSlotState.Discovered"/> AND has a known tier (UX-DR5). A discovered-yet-
        /// unauthored id (null tier) shows the art but no tier stamp.
        /// </summary>
        public bool ShowsCaughtStamp => State == CodexSlotState.Discovered && Tier.HasValue;

        /// <summary>
        /// The tier tint for the caught stamp, resolved via the single <see cref="DreadScaleTokens.ForTier"/>
        /// resolver — VALID only when <see cref="ShowsCaughtStamp"/>. For a slot with no tier this
        /// returns the calmest tier (T1) per ForTier's graceful default, but
        /// <see cref="ShowsCaughtStamp"/> gates whether it is rendered at all.
        /// </summary>
        public TierGradient CaughtStampTint => DreadScaleTokens.ForTier(Tier ?? Rarity.Common);

        private CodexSlotStyle(ChunkyStyle style, CodexSlotState state, Rarity? tier)
        {
            Style = style;
            State = state;
            Tier = tier;
        }

        /// <summary>
        /// Build a Codex-slot style for a 2.4-classified state + (optional) tier. The chunky outlined
        /// square (surface fill, md radius, hard shadow) comes from Story-6.1 tokens (AC-2).
        /// </summary>
        public static CodexSlotStyle For(CodexSlotState state, Rarity? tier)
        {
            return new CodexSlotStyle(
                ChunkyStyle.Elevated(PumpkinPatchTokens.Surface, PumpkinPatchTokens.RadiusMd),
                state,
                tier);
        }

        /// <summary>
        /// Build a Codex-slot style directly from a 2.4 <see cref="CodexSlot"/> view-model (the natural
        /// consumer — the grid presenter hands these out). Renders its state + tier; never re-classifies.
        /// </summary>
        public static CodexSlotStyle For(CodexSlot slot)
        {
            return For(slot.State, slot.Tier);
        }
    }
}
