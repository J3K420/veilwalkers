namespace Veilwalkers.Economy
{
    /// <summary>
    /// The three XP-earned "extra" charges (architecture Economy Model Revision):
    /// granted by leveling up, consumed one per use, NEVER purchasable. Distinct from
    /// Credits, which buy the four base actions.
    /// <para>
    /// Explicit values, append-only (never reordered) — same stability rule as
    /// <see cref="Veilwalkers.Core.SpendFailureReason"/>: these appear in telemetry
    /// and the Story 1.9 ad-reward hook, so a reordering would silently corrupt
    /// historical/serialized meaning. <see cref="Veilwalkers.Economy.ChargeInventory"/>
    /// is the single place that maps a value here to its <c>SaveModel</c> field.
    /// </para>
    /// </summary>
    public enum ChargeType
    {
        /// <summary>Strong Capture: a higher-tier capture attempt (Epic 4 consumes it).</summary>
        StrongCapture = 0,

        /// <summary>Stability Boost: steadies an encounter (Epic 4 consumes it).</summary>
        StabilityBoost = 1,

        /// <summary>Nightveil Filter: reveals/aids under the nightveil (Epic 4 consumes it).</summary>
        NightveilFilter = 2
    }
}
