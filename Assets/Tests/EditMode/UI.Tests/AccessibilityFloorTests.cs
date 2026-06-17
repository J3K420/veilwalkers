using NUnit.Framework;
using UnityEngine;
using Veilwalkers.AR;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// Story 6.6 — the accessibility-floor tokens + the contrast firewall (AC-2, AC-3; UX-DR17): the
    /// ≥48dp tap-target token, the WCAG contrast helper, the muted-text-never-for-safety firewall, and
    /// the SLAY-red-never-the-sole-signifier pin. Each is mutation-testable: drop the icon, mute the
    /// safety text, or shrink the tap floor and a named test goes red. Color comparisons use the packed
    /// int (the 6.1 per-channel/packed precedent) to sidestep the <see cref="Color32"/> struct-equality
    /// trap.
    /// </summary>
    public sealed class AccessibilityFloorTests
    {
        // ---- AC-3: the ≥48dp tap-target floor token ----

        [Test]
        public void Min_tap_target_token_is_48_dp()
        {
            Assert.That(PumpkinPatchTokens.MinTapTargetDp, Is.EqualTo(48));
        }

        [Test]
        public void Min_tap_target_is_at_least_the_largest_spacing_step()
        {
            // Sanity ordering: a tap target must be at least as large as the biggest spacing unit
            // (so the floor reads as a real minimum size, not below the layout grid).
            Assert.That(PumpkinPatchTokens.MinTapTargetDp, Is.GreaterThanOrEqualTo(PumpkinPatchTokens.Space32));
        }

        // ---- AC-3: the WCAG contrast helper ----

        [Test]
        public void Contrast_ratio_of_identical_colors_is_one_and_black_on_white_is_max()
        {
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 black = new Color32(0, 0, 0, 255);
            Assert.That(AccessibilityContrast.Ratio(white, white), Is.EqualTo(1f).Within(0.001f),
                "identical colors → 1:1");
            // Black on white is the WCAG maximum, 21:1.
            Assert.That(AccessibilityContrast.Ratio(black, white), Is.EqualTo(21f).Within(0.05f),
                "black on white → ~21:1");
        }

        [Test]
        public void Contrast_ratio_is_symmetric()
        {
            Color32 fg = PumpkinPatchTokens.TextPrimary;
            Color32 bg = PumpkinPatchTokens.Background;
            Assert.That(AccessibilityContrast.Ratio(fg, bg),
                Is.EqualTo(AccessibilityContrast.Ratio(bg, fg)).Within(0.0001f));
        }

        [Test]
        public void Text_primary_clears_the_body_contrast_floor_on_the_dark_surfaces()
        {
            // The warm-cream body/safety text must clear WCAG AA (4.5:1) against both dark surfaces —
            // legibility holds (UX-DR17). This is the falsifiable contrast pin.
            Assert.That(AccessibilityContrast.MeetsBodyContrast(
                PumpkinPatchTokens.TextPrimary, PumpkinPatchTokens.Background), Is.True,
                "TextPrimary on Background must clear WCAG AA body contrast");
            Assert.That(AccessibilityContrast.MeetsBodyContrast(
                PumpkinPatchTokens.TextPrimary, PumpkinPatchTokens.Surface), Is.True,
                "TextPrimary on Surface must clear WCAG AA body contrast");
        }

        // ---- AC-3: muted text is NEVER used for safety copy (the firewall) ----

        [Test]
        public void Safety_treatment_text_color_is_text_primary_never_muted()
        {
            // A real safety treatment via the only public producer (the firewall). Its text color must
            // be the high-contrast TextPrimary, NEVER the muted token (safety legibility is
            // non-negotiable, UX-DR17). Per-channel compare sidesteps the Color32 struct-equality trap
            // (the 6.1 PumpkinPatchTokensTests precedent).
            var presenter = new StateTreatmentPresenter();
            StateTreatment safety = presenter.ForSafetyGate(ArSafetyGateState.BlockingFull);
            Assert.That(safety.IsSafetyException, Is.True, "precondition: this IS a safety treatment");

            Color32 textColor = AccessibilityContrast.SafetyTextColor(safety);
            AssertSameColor(textColor, PumpkinPatchTokens.TextPrimary, "safety text must be TextPrimary");
            Assert.That(SameColor(textColor, PumpkinPatchTokens.TextMuted), Is.False,
                "safety text must NEVER be the muted token (the firewall)");
        }

        [Test]
        public void Safety_text_clears_the_body_contrast_floor()
        {
            var presenter = new StateTreatmentPresenter();
            StateTreatment safety = presenter.ForSafetyGate(ArSafetyGateState.BlockingFull);
            Color32 textColor = AccessibilityContrast.SafetyTextColor(safety);
            // The safety text color must itself clear the contrast floor on the dark surfaces.
            Assert.That(AccessibilityContrast.MeetsBodyContrast(textColor, PumpkinPatchTokens.Background), Is.True);
            Assert.That(AccessibilityContrast.MeetsBodyContrast(textColor, PumpkinPatchTokens.Surface), Is.True);
        }

        // ---- AC-2: SLAY-red is never the sole signifier (always + icon + label) ----

        [Test]
        public void Slay_button_carries_both_an_icon_and_the_slay_label_never_red_alone()
        {
            // The UX-DR17 accessibility guarantee, pinned from the a11y angle over the 6.2 descriptor.
            // SLAY-red ALWAYS carries BOTH the icon AND the "SLAY" label — never color alone. A passed
            // label is overridden to the fixed "SLAY". Mutation-testable: flip requiresIcon false or let
            // the passed label win and this goes red.
            var slay = ChunkyButtonStyle.For(ChunkyButtonKind.Slay, "ignore me");
            Assert.That(slay.RequiresIcon, Is.True, "SLAY must render an icon (never red alone)");
            Assert.That(slay.DisplayLabel, Is.EqualTo("SLAY"), "SLAY must render the fixed label (never red alone)");
        }

        [Test]
        public void Non_slay_buttons_do_not_force_the_slay_affordances()
        {
            // The icon requirement is the SLAY guarantee only — Primary/Secondary do not force it.
            Assert.That(ChunkyButtonStyle.For(ChunkyButtonKind.Primary, "PLAY").RequiresIcon, Is.False);
            Assert.That(ChunkyButtonStyle.For(ChunkyButtonKind.Secondary, "BACK").RequiresIcon, Is.False);
        }

        // Per-channel Color32 comparison (the 6.1 PumpkinPatchTokensTests precedent — sidesteps the
        // Color32 struct-equality trap without needing the internal Hex helper).
        private static bool SameColor(Color32 a, Color32 b) =>
            a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        private static void AssertSameColor(Color32 actual, Color32 expected, string because)
        {
            Assert.That(SameColor(actual, expected), Is.True,
                $"{because} (got {actual.r},{actual.g},{actual.b} expected {expected.r},{expected.g},{expected.b})");
        }

        // ---- AC-1: the reduced-motion setting seam — graceful motion-ON default when unwired ----

        [Test]
        public void No_reduced_motion_setting_reports_motion_on_and_ignores_writes()
        {
            // The graceful no-op for an unwired seam: motion-ON (the safe default), writes are silently
            // ignored — an un-wired view degrades to full motion rather than null-ref'ing.
            IReducedMotionSetting setting = NoReducedMotionSetting.Instance;
            Assert.That(setting.ReducedMotionEnabled, Is.False, "unwired → motion-on");
            Assert.DoesNotThrow(() => setting.SetReducedMotion(true), "a write on the no-op never throws");
            Assert.That(setting.ReducedMotionEnabled, Is.False, "the no-op never latches a write");
        }
    }
}
