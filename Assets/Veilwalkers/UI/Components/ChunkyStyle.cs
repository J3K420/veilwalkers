using UnityEngine;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The shared chunky-chrome elevation contract (Story 6.2; UX-DR3 / UX-DR9). Every chunky
    /// component in the kit — button, credit pill, pack card, Codex slot, rarity badge, wordmark —
    /// EMBEDS or BUILDS a <see cref="ChunkyStyle"/> from the Story-6.1 tokens, so the "black outline +
    /// hard offset shadow, NO blur" elevation device (AC-2) is sourced ONCE and structurally
    /// guaranteed across the whole kit.
    /// <para>
    /// A <c>readonly struct</c> (a pure value-type descriptor — the <c>MaterializationPlan</c> /
    /// <c>CodexSlot</c> precedent): a style handed to a view can never alias or corrupt anything, and
    /// it is fully EditMode-testable headless (no scene, no <see cref="MonoBehaviour"/>). The actual
    /// widget render (UGUI Image/Text, the 9-slice outline sprite, the drop shadow) is Story 6.3.
    /// </para>
    /// All offset/width/radius values are dp (Android density-independent pixels; UX-DR18). Colors are
    /// sourced from <see cref="PumpkinPatchTokens"/> — a component NEVER re-declares a hex (AC-2).
    /// </summary>
    public readonly struct ChunkyStyle
    {
        /// <summary>The canonical outline width (dp). UX-DR3 specifies a 4–5px band; 4 is the kit
        /// default. A 6.3 feel-pass may pick 5 within the band — the AC is "4–5px", pinned as the band.</summary>
        public const int OutlineWidthDefault = 4;

        /// <summary>The minimum outline width of the UX-DR3 4–5px band (inclusive).</summary>
        public const int OutlineWidthMin = 4;

        /// <summary>The maximum outline width of the UX-DR3 4–5px band (inclusive).</summary>
        public const int OutlineWidthMax = 5;

        /// <summary>
        /// The pressed-state shadow offset (dp). UX-DR3: pressed "shrinks the shadow offset (e.g.
        /// 2px 2px 0)". Strictly less than the resting offset (<see cref="PumpkinPatchTokens.ShadowOffsetX"/>
        /// = 6), so the control reads as "pushed in" on press.
        /// </summary>
        public const int PressedShadowOffset = 2;

        /// <summary>The fill color — sourced from a <see cref="PumpkinPatchTokens"/> / tier token.</summary>
        public readonly Color32 Fill;

        /// <summary>The outline color — black (the chunky outline, UX-DR3).</summary>
        public readonly Color32 OutlineColor;

        /// <summary>The outline width (dp), in the 4–5 band.</summary>
        public readonly int OutlineWidth;

        /// <summary>The hard-shadow X offset (dp) — resting 6, pressed 2.</summary>
        public readonly int ShadowOffsetX;

        /// <summary>The hard-shadow Y offset (dp) — resting 6, pressed 2.</summary>
        public readonly int ShadowOffsetY;

        /// <summary>
        /// The hard-shadow blur — ALWAYS 0 (UX-DR9: a hard offset shadow with NO blur is THE elevation
        /// device; no soft/blurred material shadow on chrome). Sourced from
        /// <see cref="PumpkinPatchTokens.ShadowBlur"/>. The load-bearing AC-2 invariant.
        /// </summary>
        public readonly int ShadowBlur;

        /// <summary>The hard-shadow color — pure black (from tokens).</summary>
        public readonly Color32 ShadowColor;

        /// <summary>The corner radius (dp) — sm/md/lg/pill from <see cref="PumpkinPatchTokens"/>.</summary>
        public readonly int CornerRadius;

        private ChunkyStyle(
            Color32 fill,
            Color32 outlineColor,
            int outlineWidth,
            int shadowOffsetX,
            int shadowOffsetY,
            int shadowBlur,
            Color32 shadowColor,
            int cornerRadius)
        {
            Fill = fill;
            OutlineColor = outlineColor;
            OutlineWidth = outlineWidth;
            ShadowOffsetX = shadowOffsetX;
            ShadowOffsetY = shadowOffsetY;
            ShadowBlur = shadowBlur;
            ShadowColor = shadowColor;
            CornerRadius = cornerRadius;
        }

        /// <summary>
        /// Build the elevation style for a component — black outline + the hard offset shadow from the
        /// 6.1 tokens, NO blur. The ONLY way to construct a <see cref="ChunkyStyle"/>, so every
        /// component in the kit gets the AC-2 device by construction (the resting shadow is the token
        /// offset 6,6; <see cref="ShadowBlur"/> is forced to the token zero).
        /// </summary>
        /// <param name="fill">The fill color (a <see cref="PumpkinPatchTokens"/> / tier token).</param>
        /// <param name="cornerRadius">The corner radius (a <see cref="PumpkinPatchTokens"/> radius).</param>
        /// <param name="outlineWidth">The outline width (dp); defaults to <see cref="OutlineWidthDefault"/>.</param>
        public static ChunkyStyle Elevated(Color32 fill, int cornerRadius, int outlineWidth = OutlineWidthDefault)
        {
            return new ChunkyStyle(
                fill,
                Hex.ToColor32(PumpkinPatchTokens.ShadowColorHex),
                outlineWidth,
                PumpkinPatchTokens.ShadowOffsetX,
                PumpkinPatchTokens.ShadowOffsetY,
                PumpkinPatchTokens.ShadowBlur,
                PumpkinPatchTokens.ShadowColor,
                cornerRadius);
        }

        /// <summary>
        /// True iff this style uses the hard (un-blurred) shadow elevation device (UX-DR9). Lets a
        /// component / test assert AC-2 compliance in one line. Always true for any
        /// <see cref="Elevated"/>-built style.
        /// </summary>
        public bool HasHardShadow => ShadowBlur == 0 && (ShadowOffsetX != 0 || ShadowOffsetY != 0);

        /// <summary>
        /// The pressed-state style (UX-DR3): the SAME fill/outline/radius with the shadow offset shrunk
        /// to <see cref="PressedShadowOffset"/> (2,2), strictly smaller than the resting 6,6 — so the
        /// control reads as pushed-in. Blur stays 0 (still the hard-shadow device).
        /// </summary>
        public ChunkyStyle Pressed()
        {
            return new ChunkyStyle(
                Fill,
                OutlineColor,
                OutlineWidth,
                PressedShadowOffset,
                PressedShadowOffset,
                ShadowBlur,
                ShadowColor,
                CornerRadius);
        }
    }
}
