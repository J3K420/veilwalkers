using UnityEngine;
using Veilwalkers.Billing;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The pack-card component descriptor (Story 6.2; UX-DR6). A pure-logic <c>readonly struct</c>
    /// derived from a <see cref="CreditPack"/> (the transparent Story-5.1 catalog type): a sticker-style
    /// <c>lg</c> card with a heavy outline + hard shadow, showing base + bonus Credits (bonus in
    /// pumpkin-orange), a soft-nudge tag, and a localized "Buy" affordance. The render + the live Play
    /// price binding are Story 6.3.
    /// <para>
    /// <b>Transparency (no gacha — Teen-rating canon).</b> Every grantable thing is a declared field
    /// off <see cref="CreditPack"/> (base, bonus, the Veil-only Guaranteed-Rare Lure) — this descriptor
    /// only REFLECTS those transparent fields; there is no hidden / random reward. The localized PRICE
    /// is NOT stored: Google Play returns the formatted price string at runtime (the 5.1
    /// <c>FallbackPriceUsd</c>-is-reference-only contract); the card exposes a Buy affordance slot, not
    /// a price value.
    /// </para>
    /// </summary>
    public readonly struct PackCardStyle
    {
        private readonly string _packId;
        private readonly string _badgeText;

        /// <summary>The chunky elevation style — surface fill, <c>lg</c> sticker radius, hard shadow.</summary>
        public ChunkyStyle Style { get; }

        /// <summary>The Play product id of the pack this card represents (the bind key). Null-safe.</summary>
        public string PackId => _packId ?? string.Empty;

        /// <summary>The base Credits to display.</summary>
        public int BaseCredits { get; }

        /// <summary>The bonus Credits to display (0 for the Starter pack).</summary>
        public int BonusCredits { get; }

        /// <summary>Whether this pack has a bonus to surface (a pumpkin-orange "+N BONUS" line).</summary>
        public bool HasBonus => BonusCredits > 0;

        /// <summary>
        /// The color the bonus Credits are rendered in — pumpkin-orange
        /// (<see cref="PumpkinPatchTokens.SecondaryAccent"/>), UX-DR6. Sourced from the token (AC-2).
        /// </summary>
        public Color32 BonusColor => PumpkinPatchTokens.SecondaryAccent;

        /// <summary>
        /// Whether this pack includes the one-shot Guaranteed-Rare Lure (true ONLY for the Veil pack).
        /// Surfaced as a transparent line on the card (no hidden reward — the no-gacha canon).
        /// </summary>
        public bool IncludesGuaranteedRareLure { get; }

        /// <summary>
        /// The soft-nudge tag text ("POPULAR" / "BEST VALUE"), or empty for the Starter pack (UX-DR6 —
        /// soft-nudge tags ONLY, no urgency/scarcity). Null-safe.
        /// </summary>
        public string BadgeText => _badgeText ?? string.Empty;

        /// <summary>Whether the card shows a soft-nudge tag.</summary>
        public bool HasBadge => !string.IsNullOrEmpty(_badgeText);

        /// <summary>
        /// The card carries a localized Buy affordance whose price string is bound from Play at runtime
        /// (UX-DR6 / the 5.1 localized-price contract) — never an in-code price. Always true.
        /// </summary>
        public bool HasLocalizedBuyAffordance => true;

        private PackCardStyle(
            ChunkyStyle style,
            string packId,
            int baseCredits,
            int bonusCredits,
            bool includesGuaranteedRareLure,
            string badgeText)
        {
            Style = style;
            _packId = packId;
            BaseCredits = baseCredits;
            BonusCredits = bonusCredits;
            IncludesGuaranteedRareLure = includesGuaranteedRareLure;
            _badgeText = badgeText;
        }

        /// <summary>
        /// Build a pack card from a <see cref="CreditPack"/>. The <c>lg</c> sticker radius + surface fill
        /// + hard shadow come from Story-6.1 tokens (AC-2); the badge text is mapped from
        /// <see cref="CreditPack.Badge"/>. Every displayed value is read off the transparent pack — no
        /// hidden reward.
        /// </summary>
        public static PackCardStyle For(CreditPack pack)
        {
            return new PackCardStyle(
                ChunkyStyle.Elevated(PumpkinPatchTokens.Surface, PumpkinPatchTokens.RadiusLg),
                pack.PackId,
                pack.BaseCredits,
                pack.BonusCredits,
                pack.IncludesGuaranteedRareLure,
                BadgeTextFor(pack.Badge));
        }

        /// <summary>Map a <see cref="PackBadge"/> to its soft-nudge tag text (UX-DR6). None → empty.</summary>
        public static string BadgeTextFor(PackBadge badge)
        {
            switch (badge)
            {
                case PackBadge.Popular: return "POPULAR";
                case PackBadge.BestValue: return "BEST VALUE";
                case PackBadge.None:
                default:
                    return string.Empty;
            }
        }
    }
}
