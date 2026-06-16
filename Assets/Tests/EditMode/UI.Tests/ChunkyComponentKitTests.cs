using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Veilwalkers.Billing;
using Veilwalkers.Monsters;
using Veilwalkers.UI;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// Pins the Story-6.2 chunky shared component kit (UX-DR3..DR9, UX-DR17). The component
    /// DESCRIPTORS are the deliverable (a feature epic consumes one consistent set instead of
    /// re-building UI); these tests pin the chunky-style DECISION each carries, that every component
    /// sources Story-6.1 tokens (never a re-declared hex — AC-2), and the universal "no soft shadow
    /// anywhere in the kit" invariant.
    /// <para>
    /// <see cref="Color32"/> equality is asserted per-channel (the 6.1 struct-equality-trap lesson),
    /// never <c>Assert.AreEqual(color32, color32)</c>.
    /// </para>
    /// </summary>
    public class ChunkyComponentKitTests
    {
        private static void AssertSameColor(Color32 expected, Color32 actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, what + " red");
            Assert.AreEqual(expected.g, actual.g, what + " green");
            Assert.AreEqual(expected.b, actual.b, what + " blue");
            Assert.AreEqual(expected.a, actual.a, what + " alpha");
        }

        // Every component's ChunkyStyle in the kit — the AC-2 universal sweep set.
        private static IEnumerable<ChunkyStyle> EveryComponentStyle()
        {
            yield return ChunkyButtonStyle.For(ChunkyButtonKind.Primary, "go").Style;
            yield return ChunkyButtonStyle.For(ChunkyButtonKind.Secondary, "back").Style;
            yield return ChunkyButtonStyle.For(ChunkyButtonKind.Slay, "ignored").Style;
            yield return CreditPillStyle.For(20).Style;
            yield return PackCardStyle.For(new CreditPackCatalog().Packs[0]).Style;
            yield return CodexSlotStyle.For(CodexSlotState.Discovered, Rarity.Rare).Style;
            yield return RarityBadgeStyle.For(Rarity.Nightmare).Style;
            yield return WordmarkStyle.Create().Style;
        }

        // ======== AC-2: the hard-shadow elevation device is UNIVERSAL across the kit ========

        [Test]
        public void Every_component_carries_the_hard_offset_shadow_with_no_blur()
        {
            foreach (ChunkyStyle style in EveryComponentStyle())
            {
                Assert.AreEqual(0, style.ShadowBlur, "UX-DR9: no component may declare a blurred shadow on chrome");
                Assert.IsTrue(style.HasHardShadow, "every component must carry the hard offset shadow device");
                Assert.IsTrue(style.ShadowOffsetX != 0 || style.ShadowOffsetY != 0, "the offset must be non-zero (it IS the elevation)");
                AssertSameColor(PumpkinPatchTokens.ShadowColor, style.ShadowColor, "shadow color is the black token");
            }
        }

        [Test]
        public void Every_component_outline_is_in_the_4_to_5_band()
        {
            foreach (ChunkyStyle style in EveryComponentStyle())
            {
                Assert.GreaterOrEqual(style.OutlineWidth, ChunkyStyle.OutlineWidthMin, "UX-DR3 outline >= 4px");
                Assert.LessOrEqual(style.OutlineWidth, ChunkyStyle.OutlineWidthMax, "UX-DR3 outline <= 5px");
            }
        }

        // ======== AC-1: the kit includes ALL six components (constructible) ========

        [Test]
        public void The_kit_includes_all_six_components()
        {
            // A structural completeness guard — each component type exists and is constructible.
            Assert.DoesNotThrow(() => ChunkyButtonStyle.For(ChunkyButtonKind.Primary, "x"), "chunky button");
            Assert.DoesNotThrow(() => CreditPillStyle.For(0), "credit pill");
            Assert.DoesNotThrow(() => PackCardStyle.For(new CreditPackCatalog().Packs[0]), "pack card");
            Assert.DoesNotThrow(() => CodexSlotStyle.For(CodexSlotState.Blank, null), "Codex slot");
            Assert.DoesNotThrow(() => RarityBadgeStyle.For(Rarity.Common), "rarity badge");
            Assert.DoesNotThrow(() => WordmarkStyle.Create(), "wordmark");
        }

        // ======== ChunkyStyle primitive (Task 1) ========

        [Test]
        public void Elevated_style_sources_the_token_shadow_offset_and_zero_blur()
        {
            ChunkyStyle s = ChunkyStyle.Elevated(PumpkinPatchTokens.Surface, PumpkinPatchTokens.RadiusMd);
            Assert.AreEqual(PumpkinPatchTokens.ShadowOffsetX, s.ShadowOffsetX);
            Assert.AreEqual(PumpkinPatchTokens.ShadowOffsetY, s.ShadowOffsetY);
            Assert.AreEqual(PumpkinPatchTokens.ShadowBlur, s.ShadowBlur);
            Assert.AreEqual(0, s.ShadowBlur, "the token blur is 0 — load-bearing");
            Assert.AreEqual(PumpkinPatchTokens.RadiusMd, s.CornerRadius);
        }

        [Test]
        public void Pressed_shadow_offset_is_strictly_smaller_than_resting()
        {
            // The AC is the inequality, not the exact value (a 6.3 feel-pass may retune within reason).
            ChunkyStyle resting = ChunkyStyle.Elevated(PumpkinPatchTokens.SecondaryAccent, PumpkinPatchTokens.RadiusMd);
            ChunkyStyle pressed = resting.Pressed();
            Assert.Less(pressed.ShadowOffsetX, resting.ShadowOffsetX, "pressed X < resting X");
            Assert.Less(pressed.ShadowOffsetY, resting.ShadowOffsetY, "pressed Y < resting Y");
            Assert.AreEqual(0, pressed.ShadowBlur, "pressed is still the hard-shadow device (blur 0)");
            // Fill/outline/radius are preserved on press.
            AssertSameColor(resting.Fill, pressed.Fill, "press preserves fill");
            Assert.AreEqual(resting.CornerRadius, pressed.CornerRadius, "press preserves radius");
        }

        // ======== Chunky button + SLAY variant (Task 2; UX-DR3 / UX-DR17) ========

        [Test]
        public void Primary_button_fill_is_pumpkin_orange_token()
        {
            AssertSameColor(PumpkinPatchTokens.SecondaryAccent, ChunkyButtonStyle.For(ChunkyButtonKind.Primary, "go").Style.Fill,
                "primary fill is the pumpkin-orange token (not a re-declared hex)");
        }

        [Test]
        public void Secondary_button_fill_is_surface_token()
        {
            AssertSameColor(PumpkinPatchTokens.Surface, ChunkyButtonStyle.For(ChunkyButtonKind.Secondary, "back").Style.Fill,
                "secondary = surface + outline");
        }

        [Test]
        public void Button_label_is_all_caps()
        {
            // Non-tautological: input form ("primary") != output form ("PRIMARY").
            Assert.AreEqual("PRIMARY", ChunkyButtonStyle.For(ChunkyButtonKind.Primary, "primary").DisplayLabel);
            Assert.AreEqual("ENTER AR HUNT", ChunkyButtonStyle.For(ChunkyButtonKind.Primary, "Enter AR Hunt").DisplayLabel);
        }

        [Test]
        public void Default_button_label_is_empty_not_null()
        {
            Assert.AreEqual(string.Empty, default(ChunkyButtonStyle).DisplayLabel, "null-safe getter — default honors the contract");
        }

        [Test]
        public void Slay_variant_is_never_red_alone()
        {
            // UX-DR17: SLAY-red is NEVER the sole signifier — always + icon + label.
            ChunkyButtonStyle slay = ChunkyButtonStyle.For(ChunkyButtonKind.Slay, "anything");
            AssertSameColor(PumpkinPatchTokens.SlayRed, slay.Style.Fill, "slay fill is the SLAY-red token");
            Assert.IsTrue(slay.RequiresIcon, "slay MUST carry the skull/slash icon");
            Assert.AreEqual("SLAY", slay.DisplayLabel, "slay MUST carry the literal 'SLAY' label (input label ignored)");
        }

        [Test]
        public void Non_slay_buttons_do_not_force_an_icon()
        {
            Assert.IsFalse(ChunkyButtonStyle.For(ChunkyButtonKind.Primary, "x").RequiresIcon);
            Assert.IsFalse(ChunkyButtonStyle.For(ChunkyButtonKind.Secondary, "x").RequiresIcon);
        }

        // ======== Credit pill (Task 3; UX-DR4) ========

        [Test]
        public void Credit_pill_fill_is_the_gold_token()
        {
            AssertSameColor(PumpkinPatchTokens.CreditGold, CreditPillStyle.For(20).Style.Fill,
                "pill fill is the credit-gold token");
        }

        [Test]
        public void Credit_pill_uses_the_pill_radius_and_white_hexagon_glyph()
        {
            Assert.AreEqual(PumpkinPatchTokens.RadiusPill, CreditPillStyle.For(20).Style.CornerRadius);
            Assert.AreEqual("⬡", CreditPillStyle.Glyph, "the canonical glyph is U+2B21 WHITE HEXAGON");
        }

        [Test]
        public void Credit_pill_renders_zero_and_never_locks()
        {
            // World never locks at zero: a 0 count is a valid renderable state, the pill stays tap-to-Shop.
            CreditPillStyle zero = CreditPillStyle.For(0, nearEmpty: true);
            Assert.AreEqual(0, zero.Count, "zero is a valid count — no locked/disabled state exists");
            Assert.IsTrue(zero.TapToShop, "the pill is always tappable → Shop, even at zero (world never locks)");
            // A negative balance is clamped to 0 (never a negative or locked state).
            Assert.AreEqual(0, CreditPillStyle.For(-5).Count);
        }

        [Test]
        public void Credit_pill_pulse_is_candy_teal_normally_and_dimmer_when_near_empty()
        {
            Color32 normal = CreditPillStyle.For(100, nearEmpty: false).PulseColor;
            AssertSameColor(PumpkinPatchTokens.PrimaryAccent, normal, "normal pulse is candy-teal");

            Color32 dim = CreditPillStyle.For(1, nearEmpty: true).PulseColor;
            // The felt descent: dim teal is the SAME accent darkened — strictly lower value, same hue family.
            int normalSum = normal.r + normal.g + normal.b;
            int dimSum = dim.r + dim.g + dim.b;
            Assert.Less(dimSum, normalSum, "near-empty pulse is a dimmer teal (felt descent)");
        }

        [Test]
        public void Credit_pill_default_threshold_marks_low_balances_near_empty()
        {
            Assert.IsTrue(CreditPillStyle.For(CreditPillStyle.NearEmptyThresholdDefault).NearEmpty, "at the threshold is near-empty");
            Assert.IsFalse(CreditPillStyle.For(CreditPillStyle.NearEmptyThresholdDefault + 1).NearEmpty, "above is not");
        }

        [Test]
        public void Credit_pill_single_arg_clamps_negatives_then_applies_the_threshold()
        {
            // The single-arg overload must clamp THEN test the threshold against the SAME clamped value:
            // -10 → clamped 0 → 0 <= 5 → near-empty, count 0 (the world-never-locks floor).
            CreditPillStyle pill = CreditPillStyle.For(-10);
            Assert.AreEqual(0, pill.Count, "negative clamps to the 0 floor");
            Assert.IsTrue(pill.NearEmpty, "the clamped 0 is at/under the threshold → near-empty");
            Assert.IsTrue(pill.TapToShop, "still tappable at the floor (world never locks)");
        }

        // ======== Pack card (Task 4; UX-DR6) ========

        [Test]
        public void Pack_card_uses_the_lg_sticker_radius()
        {
            CreditPack starter = new CreditPackCatalog().Packs[0];
            Assert.AreEqual(PumpkinPatchTokens.RadiusLg, PackCardStyle.For(starter).Style.CornerRadius, "lg sticker card");
        }

        [Test]
        public void Pack_card_bonus_is_pumpkin_orange_and_only_shown_when_present()
        {
            CreditPackCatalog catalog = new CreditPackCatalog();
            CreditPack starter = catalog.Packs[0]; // 50, no bonus
            CreditPack hunter = catalog.Packs[1];  // 150 + 20 bonus

            PackCardStyle starterCard = PackCardStyle.For(starter);
            Assert.IsFalse(starterCard.HasBonus, "starter has no bonus");

            PackCardStyle hunterCard = PackCardStyle.For(hunter);
            Assert.IsTrue(hunterCard.HasBonus, "hunter has a bonus");
            Assert.AreEqual(20, hunterCard.BonusCredits);
            AssertSameColor(PumpkinPatchTokens.SecondaryAccent, hunterCard.BonusColor, "bonus is rendered in pumpkin-orange (UX-DR6)");
        }

        [Test]
        public void Pack_card_surfaces_the_guaranteed_rare_lure_transparently_for_the_veil_pack()
        {
            CreditPackCatalog catalog = new CreditPackCatalog();
            CreditPack veil = catalog.Packs[2]; // the Veil pack — the only one with the Guaranteed-Rare Lure
            PackCardStyle veilCard = PackCardStyle.For(veil);
            Assert.IsTrue(veilCard.IncludesGuaranteedRareLure, "the Veil pack's Guaranteed-Rare is surfaced (no hidden reward — no gacha)");
            Assert.IsFalse(PackCardStyle.For(catalog.Packs[0]).IncludesGuaranteedRareLure, "the Starter pack does not");
        }

        [Test]
        public void Pack_card_badge_text_maps_from_the_pack_badge()
        {
            Assert.AreEqual(string.Empty, PackCardStyle.BadgeTextFor(PackBadge.None));
            Assert.AreEqual("POPULAR", PackCardStyle.BadgeTextFor(PackBadge.Popular));
            Assert.AreEqual("BEST VALUE", PackCardStyle.BadgeTextFor(PackBadge.BestValue));

            // And the canon catalog wires through: Hunter = POPULAR, Veil = BEST VALUE, Starter = none.
            CreditPackCatalog catalog = new CreditPackCatalog();
            Assert.IsFalse(PackCardStyle.For(catalog.Packs[0]).HasBadge, "starter has no soft-nudge tag");
            Assert.AreEqual("POPULAR", PackCardStyle.For(catalog.Packs[1]).BadgeText);
            Assert.AreEqual("BEST VALUE", PackCardStyle.For(catalog.Packs[2]).BadgeText);
        }

        [Test]
        public void Pack_card_carries_a_localized_buy_affordance_not_a_price()
        {
            Assert.IsTrue(PackCardStyle.For(new CreditPackCatalog().Packs[0]).HasLocalizedBuyAffordance,
                "the price string is bound from Play at runtime, never stored in the component");
        }

        // ======== Codex slot (Task 5; UX-DR5) ========

        [Test]
        public void Codex_slot_renders_three_distinct_states()
        {
            Assert.AreEqual(CodexSlotState.Discovered, CodexSlotStyle.For(CodexSlotState.Discovered, Rarity.Rare).State);
            Assert.AreEqual(CodexSlotState.Silhouette, CodexSlotStyle.For(CodexSlotState.Silhouette, Rarity.Rare).State);
            Assert.AreEqual(CodexSlotState.Blank, CodexSlotStyle.For(CodexSlotState.Blank, null).State);
        }

        [Test]
        public void Codex_slot_shows_the_caught_stamp_only_when_discovered_with_a_tier()
        {
            Assert.IsTrue(CodexSlotStyle.For(CodexSlotState.Discovered, Rarity.Epic).ShowsCaughtStamp,
                "discovered + known tier → tier-tinted caught stamp");
            Assert.IsFalse(CodexSlotStyle.For(CodexSlotState.Discovered, null).ShowsCaughtStamp,
                "discovered but unauthored (null tier) → no tier stamp");
            Assert.IsFalse(CodexSlotStyle.For(CodexSlotState.Silhouette, Rarity.Rare).ShowsCaughtStamp,
                "a silhouette is not caught");
            Assert.IsFalse(CodexSlotStyle.For(CodexSlotState.Blank, null).ShowsCaughtStamp,
                "a blank is not caught");
        }

        [Test]
        public void Codex_slot_caught_stamp_tint_uses_the_for_tier_resolver()
        {
            // Delegates to 6.1's tested resolver — pin it USES ForTier (not re-deriving the map).
            TierGradient expected = DreadScaleTokens.ForTier(Rarity.Nightmare);
            TierGradient actual = CodexSlotStyle.For(CodexSlotState.Discovered, Rarity.Nightmare).CaughtStampTint;
            AssertSameColor(expected.Start, actual.Start, "caught stamp tint start");
            AssertSameColor(expected.End, actual.End, "caught stamp tint end");
        }

        [Test]
        public void Codex_slot_can_be_built_from_a_2_4_view_model()
        {
            CodexSlot vm = new CodexSlot("mon07", CodexSlotState.Discovered, Rarity.Rare, 7);
            CodexSlotStyle style = CodexSlotStyle.For(vm);
            Assert.AreEqual(CodexSlotState.Discovered, style.State, "renders the 2.4-classified state — never re-classifies");
            Assert.AreEqual(Rarity.Rare, style.Tier);
        }

        // ======== Rarity badge (Task 6; UX-DR7) ========

        [Test]
        public void Rarity_badge_uses_the_sm_radius()
        {
            Assert.AreEqual(PumpkinPatchTokens.RadiusSm, RarityBadgeStyle.For(Rarity.Common).Style.CornerRadius, "sm badge");
        }

        [Test]
        public void Rarity_badge_color_delegates_to_for_tier()
        {
            foreach (Rarity tier in System.Enum.GetValues(typeof(Rarity)))
            {
                TierGradient expected = DreadScaleTokens.ForTier(tier);
                RarityBadgeStyle badge = RarityBadgeStyle.For(tier);
                AssertSameColor(expected.Start, badge.TierColor.Start, tier + " start");
                AssertSameColor(expected.End, badge.TierColor.End, tier + " end");
            }
        }

        [Test]
        public void Rarity_badge_t5_is_the_gradient_others_are_solid()
        {
            Assert.IsTrue(RarityBadgeStyle.For(Rarity.Nightmare).IsGradient, "T5 Nightmare is the bruise-rot gradient");
            Assert.IsFalse(RarityBadgeStyle.For(Rarity.Common).IsGradient, "T1 is solid");
            Assert.IsFalse(RarityBadgeStyle.For(Rarity.Epic).IsGradient, "T4 is solid");
        }

        // ======== Wordmark (Task 7; UX-DR8) ========

        [Test]
        public void Wordmark_text_is_exactly_veilwalkers()
        {
            Assert.AreEqual("Veilwalkers", WordmarkStyle.Create().DisplayText);
            Assert.AreEqual("Veilwalkers", WordmarkStyle.Text);
        }

        [Test]
        public void Wordmark_carries_the_hard_shadow_and_a_slight_wobble()
        {
            WordmarkStyle wordmark = WordmarkStyle.Create();
            Assert.IsTrue(wordmark.Style.HasHardShadow, "the wordmark is a chunky lockup — AC-2 hard shadow");
            Assert.Greater(wordmark.Wobble, 0f, "a slight (non-zero) wobble gives the lockup personality");
            Assert.Less(wordmark.Wobble, 15f, "'slight' — not a heavy tilt");
            Assert.IsTrue(wordmark.BodyTextForbidden, "UX-DR8: never used for body text");
        }
    }
}
