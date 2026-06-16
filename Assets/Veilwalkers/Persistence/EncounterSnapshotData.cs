using System;
using System.Collections.Generic;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// The serializable, data-only shape of an interrupted encounter, owned by
    /// Persistence. The behavior-bearing <c>EncounterSnapshot</c> lives in the
    /// Encounter tier (Epic 4) and Persistence must never reference it (forbidden
    /// upward edge), so Epic 4 maps its snapshot to/from this DTO instead.
    /// <see cref="AnchorToken"/> comes from <c>Core/Contracts</c> — placed below the
    /// AR tier precisely so saves can hold anchor references without upward edges.
    /// Fields are minimal for now; Epics 4/5 extend them.
    /// </summary>
    public sealed class EncounterSnapshotData
    {
        /// <summary>World anchors the encounter's monsters were attached to.</summary>
        public AnchorToken[] Anchors { get; set; } = Array.Empty<AnchorToken>();

        /// <summary>Per-monster state captured when the encounter was interrupted.</summary>
        public List<EncounterMonsterStateData> Monsters { get; set; } =
            new List<EncounterMonsterStateData>();

        /// <summary>
        /// The encounter-flow state to resume to, as a STABLE string tag (Story 5.4) — e.g. <c>"Lured"</c>.
        /// A string (not the raw <c>EncounterState</c> enum int) so the snapshot survives an enum renumber
        /// (the <c>AppliedExtras</c> / <c>ChargeType</c> telemetry-stability precedent). The Encounter tier
        /// maps it to/from <c>EncounterState</c> via an explicit switch. Only ACTIVE, snapshottable states are
        /// ever written here (a snapshot is taken only for a <c>Lured</c> encounter); an empty/unrecognized tag
        /// on load is a corrupt-snapshot path handled at rehydrate (typed no-op, never a throw). Not a
        /// collection — <see cref="CoerceNullCollections"/> does not touch it.
        /// </summary>
        public string State { get; set; }

        /// <summary>
        /// The encounter-wide extras applied before the interruption, as STABLE string tags (Story 5.4) — e.g.
        /// <c>"StabilityBoost"</c> / <c>"NightveilFilter"</c>. The encounter applies an extra to the WHOLE
        /// encounter (a <c>HashSet&lt;ExtraKind&gt;</c> on the service, "for the remainder of the current
        /// encounter" — Story 4.6), so the snapshot carries them at the ENCOUNTER level here, NOT per-monster
        /// (the per-monster <see cref="EncounterMonsterStateData.AppliedBoosts"/> stays reserved for a future
        /// per-monster boost mechanic). String tags (not the enum int) for the same telemetry-stability reason
        /// as <see cref="State"/>. Never null — empty when no extra was applied.
        /// </summary>
        public List<string> AppliedExtras { get; set; } = new List<string>();

        /// <summary>
        /// Repair null inner collections back to empty (load validation). The snapshot
        /// OBJECT itself may legally be null on <see cref="SaveModel"/> — that means
        /// "no active encounter" and is never coerced to an empty snapshot. The <see cref="State"/>
        /// string is NOT a collection (an empty/unrecognized value is handled at the rehydrate decision).
        /// </summary>
        public void CoerceNullCollections()
        {
            if (Anchors == null)
            {
                Anchors = Array.Empty<AnchorToken>();
            }

            if (Monsters == null)
            {
                Monsters = new List<EncounterMonsterStateData>();
            }
            else
            {
                // Null ELEMENTS (crafted/corrupt input) are removed so iteration
                // can never null-ref.
                Monsters.RemoveAll(m => m == null);
                foreach (EncounterMonsterStateData monster in Monsters)
                {
                    monster.CoerceNullCollections();
                }
            }

            if (AppliedExtras == null)
            {
                AppliedExtras = new List<string>();
            }
            else
            {
                // Null ELEMENTS (crafted/corrupt input) are dropped so the tag round-trip can never null-ref.
                AppliedExtras.RemoveAll(e => e == null);
            }
        }
    }
}
