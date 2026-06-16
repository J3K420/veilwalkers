namespace Veilwalkers.UI
{
    /// <summary>
    /// The kind of chunky button (Story 6.2; UX-DR3). Determines the fill token + whether the SLAY
    /// affordances (icon + fixed label) apply.
    /// </summary>
    public enum ChunkyButtonKind
    {
        /// <summary>Primary CTA — pumpkin-orange fill (e.g. ENTER AR HUNT, BUY).</summary>
        Primary,

        /// <summary>Secondary action — surface fill + outline.</summary>
        Secondary,

        /// <summary>Destructive Slay — SLAY-red fill + a skull/slash icon + the literal "SLAY" label.</summary>
        Slay,
    }

    /// <summary>
    /// The chunky-button component descriptor (Story 6.2; UX-DR3 / UX-DR17). A pure-logic
    /// <c>readonly struct</c> that captures the button's chunky-style DECISION — its
    /// <see cref="ChunkyStyle"/> (fill/outline/hard-shadow/radius, all from Story-6.1 tokens), its
    /// ALL-CAPS label, and the SLAY composite affordances — so a feature epic consumes one consistent
    /// button instead of re-deriving a fill + outline + shadow. The render (the UGUI widget, the press
    /// animation) is Story 6.3.
    /// <para>
    /// <b>UX-DR3:</b> <c>md</c>/<c>lg</c> rounded, 4–5px black outline, hard offset shadow, ALL-CAPS;
    /// primary = pumpkin-orange, secondary = surface + outline; pressed shrinks the shadow offset.
    /// <b>UX-DR17 (red never alone):</b> the <see cref="ChunkyButtonKind.Slay"/> variant carries
    /// <see cref="RequiresIcon"/> == true AND the fixed label "SLAY" — SLAY-red is NEVER the sole
    /// signifier.
    /// </para>
    /// </summary>
    public readonly struct ChunkyButtonStyle
    {
        /// <summary>The fixed label the SLAY variant always shows (UX-DR3/DR17). ALL-CAPS already.</summary>
        public const string SlayLabel = "SLAY";

        private readonly string _label;

        /// <summary>The button kind (drives the fill token + the SLAY affordances).</summary>
        public ChunkyButtonKind Kind { get; }

        /// <summary>The chunky elevation style — fill/outline/hard-shadow/radius from Story-6.1 tokens.</summary>
        public ChunkyStyle Style { get; }

        /// <summary>
        /// Whether the button MUST render an icon alongside the label. True ONLY for
        /// <see cref="ChunkyButtonKind.Slay"/> (the skull/slash) — the UX-DR17 guarantee that SLAY-red
        /// is never the sole signifier. False for Primary/Secondary.
        /// </summary>
        public bool RequiresIcon { get; }

        /// <summary>
        /// The ALL-CAPS display label (UX-DR3). Null-safe (coerces null → empty), so
        /// <c>default(ChunkyButtonStyle)</c> honors the contract — the <c>CreditPack.PackId</c> /
        /// <c>MaterializationPlan.MonsterId</c> null-safe-getter precedent.
        /// </summary>
        public string DisplayLabel => (_label ?? string.Empty).ToUpperInvariant();

        private ChunkyButtonStyle(ChunkyButtonKind kind, ChunkyStyle style, bool requiresIcon, string label)
        {
            Kind = kind;
            Style = style;
            RequiresIcon = requiresIcon;
            _label = label;
        }

        /// <summary>
        /// Build a chunky button of the given kind. The fill + radius are resolved from Story-6.1
        /// tokens (never a re-declared hex, AC-2): Primary → pumpkin-orange (<c>SecondaryAccent</c>) at
        /// <c>RadiusMd</c>; Secondary → surface fill + outline at <c>RadiusMd</c>; Slay → SLAY-red at
        /// <c>RadiusMd</c>, forced icon, fixed "SLAY" label (the <paramref name="label"/> is ignored
        /// for Slay — UX-DR3 mandates the literal "SLAY").
        /// </summary>
        public static ChunkyButtonStyle For(ChunkyButtonKind kind, string label)
        {
            switch (kind)
            {
                case ChunkyButtonKind.Slay:
                    // SLAY-red fill + the skull/slash icon + the fixed "SLAY" label (UX-DR17 — never red alone).
                    return new ChunkyButtonStyle(
                        kind,
                        ChunkyStyle.Elevated(PumpkinPatchTokens.SlayRed, PumpkinPatchTokens.RadiusMd),
                        requiresIcon: true,
                        label: SlayLabel);

                case ChunkyButtonKind.Secondary:
                    // Surface fill + outline (the "secondary = surface + outline" rule).
                    return new ChunkyButtonStyle(
                        kind,
                        ChunkyStyle.Elevated(PumpkinPatchTokens.Surface, PumpkinPatchTokens.RadiusMd),
                        requiresIcon: false,
                        label: label);

                case ChunkyButtonKind.Primary:
                    // Pumpkin-orange primary CTA. The default arm (any unmapped/future kind) also
                    // degrades to Primary — the calmest, non-destructive button (never a stray Slay).
                    return new ChunkyButtonStyle(
                        kind,
                        ChunkyStyle.Elevated(PumpkinPatchTokens.SecondaryAccent, PumpkinPatchTokens.RadiusMd),
                        requiresIcon: false,
                        label: label);

                default:
                    // Pumpkin-orange primary CTA (graceful default — see the Primary arm).
                    return new ChunkyButtonStyle(
                        kind,
                        ChunkyStyle.Elevated(PumpkinPatchTokens.SecondaryAccent, PumpkinPatchTokens.RadiusMd),
                        requiresIcon: false,
                        label: label);
            }
        }
    }
}
