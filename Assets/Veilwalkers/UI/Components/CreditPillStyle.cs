using UnityEngine;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The credit-pill component descriptor (Story 6.2; UX-DR4). A pure-logic <c>readonly struct</c>:
    /// a gold, outlined pill (the hard-shadow elevation device) showing a ⬡ hex glyph + an integer
    /// Credit count, top-right, tappable → Shop. The render + the live balance binding + the actual
    /// pulse tween are Story 6.3 — this descriptor only declares the chunky-style decision and the
    /// pulse STATE.
    /// <para>
    /// <b>World never locks at zero (UX-DR4 — load-bearing).</b> The pill has NO disabled / locked
    /// state: a zero count is a valid renderable state (the pill shows <c>0</c> and stays tappable →
    /// Shop). The "only Lure dimmed with real cost shown" lives in the Encounter action bar (Epic 4),
    /// NOT in this component. There is deliberately no "Locked"/"Disabled" field here.
    /// </para>
    /// <para>
    /// <b>Felt-descent pulse (UX-DR4).</b> Near-empty, the pill pulses candy-teal
    /// (<see cref="PumpkinPatchTokens.PrimaryAccent"/>) → a dim teal. "Near-empty" is a presenter
    /// INPUT (the threshold is a 6.3 / balancing decision); the component renders the pulse color it is
    /// told. <see cref="NearEmptyThresholdDefault"/> is a documented default helper.
    /// </para>
    /// </summary>
    public readonly struct CreditPillStyle
    {
        /// <summary>The ⬡ hex glyph (U+2B21 WHITE HEXAGON) — the exact glyph every UX source uses
        /// ("⬡ 20"). A const so the renderer binds it rather than re-typing a literal.</summary>
        public const string Glyph = "⬡";

        /// <summary>
        /// A documented default "near-empty" threshold (Credits). The pill runs the felt-descent pulse
        /// at or below this. Provisional — the exact threshold is a 6.3 / OQ-9 balancing decision; the
        /// component only renders the pulse state it is given via <see cref="NearEmpty"/>.
        /// </summary>
        public const int NearEmptyThresholdDefault = 5;

        /// <summary>The chunky elevation style — gold fill, pill radius, hard shadow (from Story-6.1 tokens).</summary>
        public ChunkyStyle Style { get; }

        /// <summary>The Credit count to display. A zero is valid (the world never locks — the pill shows 0).</summary>
        public int Count { get; }

        /// <summary>Whether the pill is in the near-empty felt-descent pulse state (a presenter input).</summary>
        public bool NearEmpty { get; }

        /// <summary>
        /// The pulse color: candy-teal (<see cref="PumpkinPatchTokens.PrimaryAccent"/>) normally; a dim
        /// teal when <see cref="NearEmpty"/> (the felt-descent ramp, UX-DR4). The dim teal is the candy
        /// teal at reduced value — a darkened variant, NOT a new palette token (it is the SAME accent,
        /// dimmed, per "cyan → dim teal").
        /// </summary>
        public Color32 PulseColor => NearEmpty ? DimTeal() : PumpkinPatchTokens.PrimaryAccent;

        /// <summary>
        /// The tap-to-Shop affordance is always present (UX-DR4: "tappable → Shop"). The actual
        /// navigation is <c>AppStateMachine</c> (Story 6.3); 6.2 only declares the affordance exists.
        /// </summary>
        public bool TapToShop => true;

        private CreditPillStyle(ChunkyStyle style, int count, bool nearEmpty)
        {
            Style = style;
            Count = count;
            NearEmpty = nearEmpty;
        }

        /// <summary>
        /// Build a credit pill for a Credit <paramref name="count"/>. Gold fill (<c>CreditGold</c>) at
        /// the pill radius, hard shadow — all from Story-6.1 tokens (AC-2, never a re-declared hex).
        /// A negative count is clamped to 0 (a balance is never negative; the world never locks, so the
        /// floor state is a tappable "0", never a disabled pill).
        /// </summary>
        public static CreditPillStyle For(int count, bool nearEmpty)
        {
            return new CreditPillStyle(
                ChunkyStyle.Elevated(PumpkinPatchTokens.CreditGold, PumpkinPatchTokens.RadiusPill),
                ClampToFloor(count),
                nearEmpty);
        }

        /// <summary>
        /// Convenience: build a pill that derives its near-empty state from the
        /// <see cref="NearEmptyThresholdDefault"/> (count &lt;= threshold). 6.3 may pass an explicit
        /// near-empty flag instead via <see cref="For(int, bool)"/>. The clamp is applied ONCE — the
        /// displayed count and the near-empty decision read the same clamped value, so they can never
        /// diverge if the floor rule changes.
        /// </summary>
        public static CreditPillStyle For(int count)
        {
            int display = ClampToFloor(count);
            return For(display, display <= NearEmptyThresholdDefault);
        }

        // The single clamp rule: a balance is never negative; the floor state is a tappable "0", never
        // a disabled pill (the world never locks). Centralized so the count and the near-empty decision
        // never compute from different clamped values.
        private static int ClampToFloor(int count) => count < 0 ? 0 : count;

        // The felt-descent multiplier (percent): the dim-teal target is the candy-teal accent scaled
        // toward black. NOT a new token — the SAME accent, darkened.
        private const int DimScalePercent = 55;

        // The "dim teal" felt-descent target — candy-teal scaled toward black. Each channel is reduced
        // by `Dim(channel)`, which is STRICTLY less than the source for any channel >= 2 (and the
        // candy-teal accent has every channel well above that), so the dimmed sum is strictly lower —
        // the invariant does not silently depend on the exact token magnitude. A degenerate all-black
        // accent (every channel 0) would stay black; that is the only mathematically-dimmer-impossible
        // case and is not the candy-teal token.
        private static Color32 DimTeal()
        {
            Color32 teal = PumpkinPatchTokens.PrimaryAccent;
            return new Color32(Dim(teal.r), Dim(teal.g), Dim(teal.b), teal.a);
        }

        // Scale one channel toward black, guaranteeing the result is <= the input (never brighter) and
        // strictly < the input whenever the input is >= 2 (so the felt-descent always reads as dimmer).
        private static byte Dim(byte channel)
        {
            int scaled = channel * DimScalePercent / 100;
            if (channel >= 2 && scaled >= channel)
            {
                scaled = channel - 1; // never let truncation round back up to the source
            }

            return (byte)scaled;
        }
    }
}
