namespace Veilwalkers.Encounter
{
    /// <summary>
    /// The two NON-capture encounter extras a player applies to shape a live encounter (Story 4.6, FR-10):
    /// Stability Boost and Nightveil Filter. Each maps 1:1 to an XP-earned
    /// <see cref="Veilwalkers.Economy.ChargeType"/> consumed one per use (never purchasable, never Credits).
    /// <para>
    /// <b>Strong Capture is deliberately NOT a member.</b> It is consumed by the 4.4 Capture path
    /// (<c>EncounterService.TryCaptureAsync(strong: true)</c>), not by the apply-extra action — so it has no
    /// member here and cannot be passed to <c>TryApplyExtraAsync</c> (the enum structurally excludes it; no
    /// runtime guard for it is needed). <see cref="ExtrasSystem"/> is the single place that maps an
    /// <see cref="ExtraKind"/> to its <see cref="Veilwalkers.Economy.ChargeType"/>.
    /// </para>
    /// <para>
    /// Explicit values, append-only (never reordered) — same telemetry-stability rule as
    /// <see cref="Veilwalkers.Economy.ChargeType"/> / <see cref="Veilwalkers.Core.SpendFailureReason"/>.
    /// </para>
    /// </summary>
    public enum ExtraKind
    {
        /// <summary>Stability Boost: consumes one <c>ChargeType.StabilityBoost</c> charge to raise the success
        /// ease of subsequent Scan/Capture/Slay attempts for the remainder of the current encounter (AC-1).</summary>
        StabilityBoost = 0,

        /// <summary>Nightveil Filter: consumes one <c>ChargeType.NightveilFilter</c> charge to flag the
        /// atmospheric visual filter (logical state; the VFX is Epic 6) AND raise the rarity of subsequent Lure
        /// rolls for the remainder of the current encounter (AC-2).</summary>
        NightveilFilter = 1
    }
}
