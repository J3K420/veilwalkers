using UnityEngine;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The onboarding premise-card component descriptor (Story 6.4; AC-1). A pure-logic
    /// <c>readonly struct</c> — an lg sticker card (the <c>PackCardStyle</c> precedent) carrying ONE
    /// premise beat (the Veil is real / monsters are real / you're a Veilwalker) + a primary advance
    /// CTA. Sources ONLY Story-6.1 tokens via <see cref="ChunkyStyle.Elevated"/> (Surface fill, lg
    /// radius, the hard-shadow elevation device — AC-2, never a re-declared hex). The render (the UGUI
    /// widget, the card-swipe tween, the actual art) is the deferred Story-6.5 view layer.
    /// <para>
    /// <b>Chrome never painted with a tier color (6.1 AC-4).</b> The card uses the frame palette
    /// (<see cref="PumpkinPatchTokens.Surface"/> fill + <see cref="PumpkinPatchTokens.TextPrimary"/>
    /// body) — never a <c>DreadScaleTokens</c> tier color. The body text is the warm-cream
    /// <see cref="PumpkinPatchTokens.TextPrimary"/> on the Surface fill (legible, NOT muted).
    /// </para>
    /// </summary>
    public readonly struct PremiseCardStyle
    {
        /// <summary>The chunky elevation style — Surface fill, lg sticker radius, hard shadow (6.1 tokens).</summary>
        public ChunkyStyle Style { get; }

        /// <summary>The body text color — warm cream <see cref="PumpkinPatchTokens.TextPrimary"/> (legible
        /// on the Surface fill; never muted).</summary>
        public Color32 BodyColor { get; }

        /// <summary>The premise copy this card conveys (null-safe → empty).</summary>
        public string Body => _body ?? string.Empty;

        private readonly string _body;

        /// <summary>The primary advance CTA (e.g. "Next" / "I'm a Veilwalker" on the last card). A
        /// pumpkin-orange primary chunky button (ALL-CAPS via the descriptor).</summary>
        public ChunkyButtonStyle Advance { get; }

        private PremiseCardStyle(ChunkyStyle style, Color32 bodyColor, string body, ChunkyButtonStyle advance)
        {
            Style = style;
            BodyColor = bodyColor;
            _body = body;
            Advance = advance;
        }

        /// <summary>
        /// Build a premise card with the given <paramref name="body"/> copy + <paramref name="advanceLabel"/>
        /// CTA label. Surface fill at lg radius with the hard shadow; warm-cream body text — all from
        /// Story-6.1 tokens (AC-2). The advance CTA is a Primary chunky button.
        /// </summary>
        public static PremiseCardStyle For(string body, string advanceLabel)
        {
            return new PremiseCardStyle(
                ChunkyStyle.Elevated(PumpkinPatchTokens.Surface, PumpkinPatchTokens.RadiusLg),
                PumpkinPatchTokens.TextPrimary,
                body,
                ChunkyButtonStyle.For(ChunkyButtonKind.Primary, advanceLabel));
        }
    }
}
