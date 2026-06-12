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
        /// Repair null inner collections back to empty (load validation). The snapshot
        /// OBJECT itself may legally be null on <see cref="SaveModel"/> — that means
        /// "no active encounter" and is never coerced to an empty snapshot.
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
                foreach (EncounterMonsterStateData monster in Monsters)
                {
                    monster?.CoerceNullCollections();
                }
            }
        }
    }
}
