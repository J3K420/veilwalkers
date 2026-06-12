using System.Collections.Generic;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// One monster's state inside <see cref="EncounterSnapshotData"/>: enough to
    /// rehydrate the encounter after an interruption. Minimal on purpose — Epic 4
    /// (encounter state machine) and Epic 5 (mid-encounter top-up) extend it.
    /// </summary>
    public sealed class EncounterMonsterStateData
    {
        /// <summary>Stable monster id (<c>"monNN"</c>).</summary>
        public string MonsterId { get; set; }

        /// <summary>Scan completion in [0, 1] at the moment of interruption.</summary>
        public float ScanProgress { get; set; }

        /// <summary>
        /// Charges/boosts applied to this monster before the interruption (stable
        /// string tags). Never null — empty when nothing was applied.
        /// </summary>
        public List<string> AppliedBoosts { get; set; } = new List<string>();

        /// <summary>Repair a null <see cref="AppliedBoosts"/> back to empty (load validation).</summary>
        public void CoerceNullCollections()
        {
            if (AppliedBoosts == null)
            {
                AppliedBoosts = new List<string>();
            }
        }
    }
}
