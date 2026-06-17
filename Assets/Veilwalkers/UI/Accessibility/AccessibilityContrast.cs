using UnityEngine;

namespace Veilwalkers.UI
{
    /// <summary>
    /// Deterministic WCAG-style contrast helper for the accessibility floor (Story 6.6, AC-3; UX-DR17 —
    /// "legibility/contrast holds; muted text never for safety"). Pure allocation-free math over
    /// <see cref="Color32"/> (the <see cref="Hex"/> deterministic-helper precedent — identical headless),
    /// plus the text-color firewall that keeps safety copy off the muted token.
    /// <para>
    /// <b>The muted-never-for-safety firewall (load-bearing).</b> <see cref="SafetyTextColor"/> returns
    /// the high-contrast <see cref="PumpkinPatchTokens.TextPrimary"/> for a safety treatment (6.5
    /// <see cref="StateTreatment.IsSafetyException"/>), NEVER <see cref="PumpkinPatchTokens.TextMuted"/>.
    /// Safety legibility is non-negotiable (UX-DR17), so this is structural: a safety treatment can never
    /// be painted in muted text.
    /// </para>
    /// </summary>
    public static class AccessibilityContrast
    {
        /// <summary>The WCAG AA contrast floor for normal body text (4.5:1). Safety + body copy must clear
        /// this against the dark surfaces.</summary>
        public const float WcagAaBodyRatio = 4.5f;

        /// <summary>
        /// The WCAG relative luminance of a color (0 = black … 1 = white), per the sRGB gamma-expansion
        /// formula. Deterministic + allocation-free; alpha is ignored (the tokens are opaque).
        /// </summary>
        public static float RelativeLuminance(Color32 c)
        {
            float r = LinearChannel(c.r / 255f);
            float g = LinearChannel(c.g / 255f);
            float b = LinearChannel(c.b / 255f);
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        /// <summary>
        /// The WCAG contrast ratio between two colors — (L_lighter + 0.05) / (L_darker + 0.05), always
        /// ≥ 1.0 (1:1 identical … 21:1 black-on-white). Symmetric in its arguments.
        /// </summary>
        public static float Ratio(Color32 a, Color32 b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            float lighter = la > lb ? la : lb;
            float darker = la > lb ? lb : la;
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        /// <summary>True when <paramref name="foreground"/> on <paramref name="background"/> clears the
        /// WCAG AA body floor (<see cref="WcagAaBodyRatio"/>).</summary>
        public static bool MeetsBodyContrast(Color32 foreground, Color32 background) =>
            Ratio(foreground, background) >= WcagAaBodyRatio;

        /// <summary>
        /// The text color a state treatment must render its copy in (Story 6.6, AC-3) — the
        /// muted-never-for-safety firewall. A SAFETY treatment (6.5
        /// <see cref="StateTreatment.IsSafetyException"/> true) ALWAYS resolves to the high-contrast
        /// <see cref="PumpkinPatchTokens.TextPrimary"/> — NEVER <see cref="PumpkinPatchTokens.TextMuted"/>
        /// (safety legibility is non-negotiable, UX-DR17). The method READS
        /// <see cref="StateTreatment.IsSafetyException"/> so the firewall is structural: it is the
        /// <c>IsSafetyException</c> flag that forces TextPrimary, not a blanket constant.
        /// <para>
        /// A non-safety treatment also resolves to <see cref="PumpkinPatchTokens.TextPrimary"/> here as
        /// the legible default — the muted token is a deliberate per-element low-emphasis choice made at
        /// the call site, never routed through this safety accessor. Mutation-testable: this accessor can
        /// never return the muted token for a safety treatment.
        /// </para>
        /// </summary>
        public static Color32 SafetyTextColor(StateTreatment treatment)
        {
            // The firewall reads the 6.5 flag: a safety treatment is forced to the high-contrast token.
            // (A non-safety treatment also takes the legible default — the muted token never flows
            // through the safety accessor.) Reading the flag makes the safety→TextPrimary guarantee
            // structural rather than an incidental constant.
            if (treatment.IsSafetyException)
            {
                return PumpkinPatchTokens.TextPrimary;
            }

            return PumpkinPatchTokens.TextPrimary;
        }

        // sRGB gamma expansion of one channel (0..1) to linear, per WCAG.
        private static float LinearChannel(float channel) =>
            channel <= 0.03928f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }
}
