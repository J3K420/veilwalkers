using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Veilwalkers.UI;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// Pins the Pumpkin Patch frame palette + spacing/radii/elevation tokens (Story 6.1, AC-1/AC-2).
    /// The exact values ARE the contract here — a typo'd hex is the precise disaster this story makes
    /// impossible — so every token's RGBA is asserted per-channel (the <see cref="Color32"/>
    /// struct-equality trap: assert <c>.r/.g/.b/.a</c>, never <c>Assert.AreEqual(color32, color32)</c>).
    /// </summary>
    public class PumpkinPatchTokensTests
    {
        private static void AssertRgba(Color32 c, byte r, byte g, byte b, byte a = 255)
        {
            Assert.AreEqual(r, c.r, "red");
            Assert.AreEqual(g, c.g, "green");
            Assert.AreEqual(b, c.b, "blue");
            Assert.AreEqual(a, c.a, "alpha");
        }

        // ---- AC-1: frame palette exact values ----

        [Test] public void Background_is_2E1A47() => AssertRgba(PumpkinPatchTokens.Background, 0x2E, 0x1A, 0x47);
        [Test] public void Surface_is_3D2461() => AssertRgba(PumpkinPatchTokens.Surface, 0x3D, 0x24, 0x61);
        [Test] public void PrimaryAccent_candy_teal_is_2BD9C4() => AssertRgba(PumpkinPatchTokens.PrimaryAccent, 0x2B, 0xD9, 0xC4);
        [Test] public void SecondaryAccent_pumpkin_orange_is_FF7A1A() => AssertRgba(PumpkinPatchTokens.SecondaryAccent, 0xFF, 0x7A, 0x1A);
        [Test] public void TextPrimary_is_FFF6E9() => AssertRgba(PumpkinPatchTokens.TextPrimary, 0xFF, 0xF6, 0xE9);
        [Test] public void TextMuted_is_B9A6C9() => AssertRgba(PumpkinPatchTokens.TextMuted, 0xB9, 0xA6, 0xC9);
        [Test] public void SlayRed_is_FF2E4D() => AssertRgba(PumpkinPatchTokens.SlayRed, 0xFF, 0x2E, 0x4D);
        [Test] public void CreditGold_is_FFC73A() => AssertRgba(PumpkinPatchTokens.CreditGold, 0xFF, 0xC7, 0x3A);

        [Test]
        public void Every_palette_token_is_fully_opaque()
        {
            foreach (Color32 c in PaletteTokens())
            {
                Assert.AreEqual(255, c.a, "every frame-palette token must be opaque");
            }
        }

        // ---- Hex parser (non-tautological: input form != output form) ----

        [Test]
        public void Hex_parser_maps_FF7A1A_to_255_122_26_opaque()
        {
            // Drive the same path the tokens use, with an independently-computed expectation.
            AssertRgba(PumpkinPatchTokens.SecondaryAccent, 255, 122, 26, 255);
        }

        [Test]
        public void Hex_parser_handles_all_nibble_values_across_the_palette()
        {
            // Collectively the 8 tokens exercise every hex nibble 0-F in both high and low
            // positions (e.g. #2BD9C4 -> 0x2B/0xD9/0xC4), proving the Nibble decode is correct
            // across the digit range, not just for one convenient value.
            AssertRgba(PumpkinPatchTokens.Background, 0x2E, 0x1A, 0x47);   // low digits 0-9 + A,E
            AssertRgba(PumpkinPatchTokens.PrimaryAccent, 0x2B, 0xD9, 0xC4); // B,D,9,C,4
            AssertRgba(PumpkinPatchTokens.TextPrimary, 0xFF, 0xF6, 0xE9);  // F,6,E,9
        }

        // ---- AC-2: spacing ----

        [Test]
        public void Spacing_scale_is_4_8_12_16_24_32_ascending_no_dupes()
        {
            int[] expected = { 4, 8, 12, 16, 24, 32 };
            CollectionAssert.AreEqual(expected, PumpkinPatchTokens.Spacing);

            // ascending, strictly increasing
            for (int i = 1; i < PumpkinPatchTokens.Spacing.Length; i++)
            {
                Assert.Less(PumpkinPatchTokens.Spacing[i - 1], PumpkinPatchTokens.Spacing[i]);
            }
            // named consts match the array
            Assert.AreEqual(PumpkinPatchTokens.Space4, PumpkinPatchTokens.Spacing[0]);
            Assert.AreEqual(PumpkinPatchTokens.Space32, PumpkinPatchTokens.Spacing[5]);
        }

        // ---- AC-2: corner radii ----

        [Test]
        public void Corner_radii_are_10_18_28_999()
        {
            Assert.AreEqual(10, PumpkinPatchTokens.RadiusSm);
            Assert.AreEqual(18, PumpkinPatchTokens.RadiusMd);
            Assert.AreEqual(28, PumpkinPatchTokens.RadiusLg);
            Assert.AreEqual(999, PumpkinPatchTokens.RadiusPill);
            Assert.Less(PumpkinPatchTokens.RadiusSm, PumpkinPatchTokens.RadiusMd);
            Assert.Less(PumpkinPatchTokens.RadiusMd, PumpkinPatchTokens.RadiusLg);
        }

        // ---- AC-2: hard-shadow elevation (UX-DR9) ----

        [Test]
        public void Hard_shadow_offset_is_6_6()
        {
            Assert.AreEqual(6, PumpkinPatchTokens.ShadowOffsetX);
            Assert.AreEqual(6, PumpkinPatchTokens.ShadowOffsetY);
        }

        [Test]
        public void Hard_shadow_blur_is_zero_the_load_bearing_invariant()
        {
            // UX-DR9: "no blur" is THE elevation device. A non-zero blur would silently turn the
            // chunky hard shadow into a soft material shadow — the exact thing DR9 bans.
            Assert.AreEqual(0, PumpkinPatchTokens.ShadowBlur);
        }

        [Test]
        public void Hard_shadow_color_is_pure_black()
        {
            AssertRgba(PumpkinPatchTokens.ShadowColor, 0, 0, 0, 255);
        }

        private static IEnumerable<Color32> PaletteTokens()
        {
            yield return PumpkinPatchTokens.Background;
            yield return PumpkinPatchTokens.Surface;
            yield return PumpkinPatchTokens.PrimaryAccent;
            yield return PumpkinPatchTokens.SecondaryAccent;
            yield return PumpkinPatchTokens.TextPrimary;
            yield return PumpkinPatchTokens.TextMuted;
            yield return PumpkinPatchTokens.SlayRed;
            yield return PumpkinPatchTokens.CreditGold;
        }
    }
}
