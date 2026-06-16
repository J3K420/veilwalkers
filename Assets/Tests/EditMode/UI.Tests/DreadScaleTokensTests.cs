using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.Monsters;
using Veilwalkers.UI;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// Pins the dread-scale tier tokens + the <see cref="DreadScaleTokens.ForTier"/> resolver
    /// (Story 6.1, AC-3/AC-4). Tier values are the contract (asserted per-channel); the
    /// non-tautological assertions are the resolver totality/graceful-degrade and the AC-4
    /// containment guard (tier set disjoint from the frame palette).
    /// </summary>
    public class DreadScaleTokensTests
    {
        private static int Packed(Color32 c) => (c.r << 24) | (c.g << 16) | (c.b << 8) | c.a;

        private static void AssertRgba(Color32 c, byte r, byte g, byte b, byte a = 255)
        {
            Assert.AreEqual(r, c.r, "red");
            Assert.AreEqual(g, c.g, "green");
            Assert.AreEqual(b, c.b, "blue");
            Assert.AreEqual(a, c.a, "alpha");
        }

        // ---- AC-3: tier token exact values ----

        [Test]
        public void Tier1_through_Tier4_are_single_stop_with_exact_values()
        {
            AssertRgba(DreadScaleTokens.Tier1Common.Start, 0x7D, 0xE0, 0xC4);
            AssertRgba(DreadScaleTokens.Tier2Uncommon.Start, 0x4F, 0xA8, 0xC9);
            AssertRgba(DreadScaleTokens.Tier3Rare.Start, 0x6A, 0x5A, 0xC9);
            AssertRgba(DreadScaleTokens.Tier4Epic.Start, 0x4B, 0x2E, 0x6B);

            Assert.IsTrue(DreadScaleTokens.Tier1Common.IsSolid);
            Assert.IsTrue(DreadScaleTokens.Tier2Uncommon.IsSolid);
            Assert.IsTrue(DreadScaleTokens.Tier3Rare.IsSolid);
            Assert.IsTrue(DreadScaleTokens.Tier4Epic.IsSolid);
        }

        [Test]
        public void Tier5_nightmare_is_the_bruise_rot_gradient()
        {
            TierGradient t5 = DreadScaleTokens.Tier5Nightmare;
            AssertRgba(t5.Start, 0x3A, 0x4C, 0x3A);
            AssertRgba(t5.End, 0x5C, 0x2A, 0x4A);
            Assert.AreNotEqual(Packed(t5.Start), Packed(t5.End), "T5 is a true two-stop gradient");
            Assert.IsFalse(t5.IsSolid, "T5 is the only non-solid tier");
        }

        [Test]
        public void Every_tier_token_is_fully_opaque()
        {
            foreach (Color32 c in AllTierStops())
            {
                Assert.AreEqual(255, c.a);
            }
        }

        // ---- AC-4 / AC-3: ForTier resolver totality + monotonic mapping ----

        [Test]
        public void ForTier_maps_each_rarity_to_its_tier_token()
        {
            Assert.AreEqual(Packed(DreadScaleTokens.Tier1Common.Start), Packed(DreadScaleTokens.ForTier(Rarity.Common).Start));
            Assert.AreEqual(Packed(DreadScaleTokens.Tier2Uncommon.Start), Packed(DreadScaleTokens.ForTier(Rarity.Uncommon).Start));
            Assert.AreEqual(Packed(DreadScaleTokens.Tier3Rare.Start), Packed(DreadScaleTokens.ForTier(Rarity.Rare).Start));
            Assert.AreEqual(Packed(DreadScaleTokens.Tier4Epic.Start), Packed(DreadScaleTokens.ForTier(Rarity.Epic).Start));

            TierGradient nightmare = DreadScaleTokens.ForTier(Rarity.Nightmare);
            Assert.AreEqual(Packed(DreadScaleTokens.Tier5Nightmare.Start), Packed(nightmare.Start));
            Assert.AreEqual(Packed(DreadScaleTokens.Tier5Nightmare.End), Packed(nightmare.End));
        }

        [Test]
        public void ForTier_maps_every_defined_rarity_to_a_distinct_opaque_token()
        {
            // What this test ACTUALLY guards: every real enum member resolves to an opaque token,
            // and no two tiers collide on the same color (distinct-count == member-count). It catches
            // a DUPLICATE-mapping regression — e.g. a copy-paste that points two tiers at the same
            // hex would drop the distinct count below the member count and fail here.
            //
            // It does NOT catch a future tier added to Rarity WITHOUT a ForTier arm: such a member
            // hits the `default` arm and returns Tier1Common, whose color already exists in the set,
            // so the distinct count would still equal the (now larger) member count only if it
            // collided — see the Assert below. The missing-arm case is guarded instead by
            // ForTier_degrades_an_undefined_rarity_to_T1_without_throwing (the GameLog.Warn the
            // default arm emits, asserted via LogAssert.Expect) — a real ForTier member never warns.
            int memberCount = System.Enum.GetValues(typeof(Rarity)).Length;
            var packed = new HashSet<int>();
            foreach (Rarity r in System.Enum.GetValues(typeof(Rarity)))
            {
                TierGradient g = DreadScaleTokens.ForTier(r);
                Assert.AreEqual(255, g.Start.a, "every tier token must be opaque");
                packed.Add(Packed(g.Start));
            }
            Assert.AreEqual(memberCount, packed.Count,
                "each Rarity member must map to a DISTINCT start color (no two tiers share a token)");
        }

        [Test]
        public void ForTier_degrades_an_undefined_rarity_to_T1_without_throwing()
        {
            // Defensive boundary (NFR-3): an out-of-range cast degrades to the calmest tier and
            // logs a warning rather than throwing. The warning is expected — assert it so the
            // headless gate does not fail on an "unexpected log".
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("unmapped Rarity"));
            TierGradient g = DreadScaleTokens.ForTier((Rarity)99);
            Assert.AreEqual(Packed(DreadScaleTokens.Tier1Common.Start), Packed(g.Start));
        }

        // ---- AC-4: containment — tier set disjoint from the frame palette ----

        [Test]
        public void Tier_tokens_share_no_color_with_the_frame_palette()
        {
            // AC-4: the dread-scale tier tokens are a SEPARATE set from the frame palette, so a
            // consumer cannot reach a tier color through a frame role (which would let chrome be
            // painted with a tier color). Would FAIL if anyone aliased a tier hex into a palette token.
            var palette = new HashSet<int>();
            foreach (Color32 c in FramePaletteTokens()) palette.Add(Packed(c));

            foreach (Color32 c in AllTierStops())
            {
                Assert.IsFalse(palette.Contains(Packed(c)),
                    $"tier color {Packed(c):X8} must not also be a frame-palette token");
            }
        }

        [Test]
        public void CreditGold_and_SlayRed_are_distinct_named_roles()
        {
            // AC-4: credit-gold (currency only) and SLAY-red (destructive only) are distinct named
            // tokens, not aliases of each other or of any other palette/tier token.
            int gold = Packed(PumpkinPatchTokens.CreditGold);
            int slay = Packed(PumpkinPatchTokens.SlayRed);
            Assert.AreNotEqual(gold, slay);

            foreach (Color32 c in AllTierStops())
            {
                Assert.AreNotEqual(gold, Packed(c), "credit-gold must not equal a tier token");
                Assert.AreNotEqual(slay, Packed(c), "SLAY-red must not equal a tier token");
            }
        }

        private static IEnumerable<Color32> AllTierStops()
        {
            yield return DreadScaleTokens.Tier1Common.Start;
            yield return DreadScaleTokens.Tier2Uncommon.Start;
            yield return DreadScaleTokens.Tier3Rare.Start;
            yield return DreadScaleTokens.Tier4Epic.Start;
            yield return DreadScaleTokens.Tier5Nightmare.Start;
            yield return DreadScaleTokens.Tier5Nightmare.End;
        }

        private static IEnumerable<Color32> FramePaletteTokens()
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
