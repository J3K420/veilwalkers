using UnityEngine;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The Pumpkin Patch design tokens (Story 6.1; UX-DR1 frame palette, UX-DR9 hard-shadow
    /// elevation). The single source of truth for the app's frame colors, spacing scale, corner
    /// radii, and the hard-shadow elevation device — every chunky surface, button, pill, and card
    /// (Story 6.2 onward) reads these named constants instead of re-declaring a hex.
    /// <para>
    /// Each color is stored TWICE: a <c>const string</c> canonical <c>#RRGGBB</c> hex (the
    /// copy-pasteable, designer-auditable source of truth — checked line-by-line against UX-DR1)
    /// and a <see cref="Color32"/> accessor (the runtime form). The two are locked together by
    /// tests, so they can never drift.
    /// </para>
    /// <para>
    /// <b>Frame palette ONLY.</b> The five dread-scale TIER tokens live in
    /// <see cref="DreadScaleTokens"/>, a deliberately separate set (UX-DR2) — a tier color is never
    /// also a frame-palette role, so chrome can never accidentally be painted with a tier color.
    /// <see cref="CreditGold"/> is used ONLY for currency and <see cref="SlayRed"/> ONLY for
    /// Slay/destructive (UX-DR1); they are distinct named tokens so that discipline is checkable.
    /// </para>
    /// All spacing/radius values are in dp (Android density-independent pixels; UX-DR18).
    /// </summary>
    public static class PumpkinPatchTokens
    {
        // ---- Frame palette (UX-DR1) — canonical hex ----

        /// <summary>App background — deep veil purple.</summary>
        public const string BackgroundHex = "#2E1A47";

        /// <summary>Raised surface / card fill.</summary>
        public const string SurfaceHex = "#3D2461";

        /// <summary>Primary accent — candy teal.</summary>
        public const string PrimaryAccentHex = "#2BD9C4";

        /// <summary>Secondary accent — pumpkin orange (primary chunky-button fill, bonus credits).</summary>
        public const string SecondaryAccentHex = "#FF7A1A";

        /// <summary>Primary text — warm cream.</summary>
        public const string TextPrimaryHex = "#FFF6E9";

        /// <summary>Muted text. NEVER used for safety copy (UX-DR17).</summary>
        public const string TextMutedHex = "#B9A6C9";

        /// <summary>Danger / SLAY-red. Used ONLY for Slay/destructive (UX-DR1) — never as a general accent.</summary>
        public const string SlayRedHex = "#FF2E4D";

        /// <summary>Credit gold. Used ONLY for currency (UX-DR1, UX-DR4) — never as a general accent.</summary>
        public const string CreditGoldHex = "#FFC73A";

        // ---- Frame palette — typed accessors ----

        public static Color32 Background => Hex.ToColor32(BackgroundHex);
        public static Color32 Surface => Hex.ToColor32(SurfaceHex);
        public static Color32 PrimaryAccent => Hex.ToColor32(PrimaryAccentHex);
        public static Color32 SecondaryAccent => Hex.ToColor32(SecondaryAccentHex);
        public static Color32 TextPrimary => Hex.ToColor32(TextPrimaryHex);
        public static Color32 TextMuted => Hex.ToColor32(TextMutedHex);

        /// <summary>SLAY-red — ONLY for Slay/destructive (UX-DR1).</summary>
        public static Color32 SlayRed => Hex.ToColor32(SlayRedHex);

        /// <summary>Credit-gold — ONLY for currency (UX-DR1).</summary>
        public static Color32 CreditGold => Hex.ToColor32(CreditGoldHex);

        // ---- Spacing scale (UX-DR1 / UX-DR18) — dp ----

        public const int Space4 = 4;
        public const int Space8 = 8;
        public const int Space12 = 12;
        public const int Space16 = 16;
        public const int Space24 = 24;
        public const int Space32 = 32;

        /// <summary>
        /// The full ascending spacing scale (dp). Provided as an immutable ordered set for callers
        /// that iterate (e.g. a spacing picker); individual call sites use the named
        /// <c>SpaceNN</c> constants.
        /// </summary>
        public static readonly int[] Spacing = { Space4, Space8, Space12, Space16, Space24, Space32 };

        // ---- Corner radii (UX-DR1) — dp ----

        /// <summary>Small radius — sm badges/chips.</summary>
        public const int RadiusSm = 10;

        /// <summary>Medium radius — md buttons/surfaces.</summary>
        public const int RadiusMd = 18;

        /// <summary>Large radius — lg sticker cards.</summary>
        public const int RadiusLg = 28;

        /// <summary>
        /// Pill radius — the "fully rounded" sentinel (UX-DR4 credit pill). A large value the
        /// renderer clamps to half the control height; not a literal dp dimension.
        /// </summary>
        public const int RadiusPill = 999;

        // ---- Hard-shadow elevation (UX-DR9) ----

        /// <summary>Hard-shadow X offset (dp). THE elevation device — UX-DR9.</summary>
        public const int ShadowOffsetX = 6;

        /// <summary>Hard-shadow Y offset (dp).</summary>
        public const int ShadowOffsetY = 6;

        /// <summary>
        /// Hard-shadow blur — ALWAYS 0. UX-DR9: the hard offset shadow with NO blur is THE elevation
        /// device on every chunky card/button/badge/pill; no blurred/soft material shadows on chrome.
        /// This zero is a load-bearing invariant, pinned by a test.
        /// </summary>
        public const int ShadowBlur = 0;

        /// <summary>Hard-shadow color — pure black.</summary>
        public const string ShadowColorHex = "#000000";

        /// <summary>Hard-shadow color (typed).</summary>
        public static Color32 ShadowColor => Hex.ToColor32(ShadowColorHex);
    }
}
