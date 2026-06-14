using System;
using UnityEngine;

namespace Veilwalkers.Core.Contracts
{
    /// <summary>
    /// A serializable handle to an AR anchor, expressed WITHOUT any AR-Foundation
    /// types so it can live in Core (below the AR tier). This lets higher-tier
    /// snapshots such as <c>EncounterSnapshot</c> (Encounter) and <c>SaveModel</c>
    /// (Persistence) store an anchor reference without depending upward on AR.
    /// The AR tier is responsible for converting between its native trackables and
    /// this token; later stories fill in that behavior.
    /// </summary>
    [Serializable]
    public struct AnchorToken
    {
        /// <summary>Stable trackable id assigned by the AR subsystem (string form).</summary>
        public string trackableId;

        /// <summary>Session-relative position of the anchor.</summary>
        public Vector3 position;

        /// <summary>Session-relative rotation of the anchor.</summary>
        public Quaternion rotation;

        public AnchorToken(string trackableId, Vector3 position, Quaternion rotation)
        {
            this.trackableId = trackableId;
            this.position = position;
            this.rotation = rotation;
        }

        /// <summary>
        /// Whether this is a real anchor handle (a non-null, non-empty <see cref="trackableId"/>) — the
        /// READ-time guard the restore path (Story 3.5) reads to tell a real token from
        /// <see cref="None"/>/<c>default</c>/a corrupt deserialized one. Settles the Story 1.2 CR
        /// deferral ("<c>default(AnchorToken)</c> is indistinguishable from a real token"). NOTE: the type
        /// stays a serializable DTO that ACCEPTS any value on load (the Persistence coercion precedent —
        /// the ctor does NOT throw); this is the read-time validity check, not load validation.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(trackableId);

        /// <summary>
        /// The explicit "no anchor" sentinel (Story 3.5) — so callers stop relying on
        /// <c>default(AnchorToken)</c>. Uses the explicit ctor (NOT <c>default</c>) so the rotation is a
        /// valid <see cref="Quaternion.identity"/> (w=1) rather than the zero-quaternion (w=0) that
        /// <c>default</c> would produce. <see cref="IsValid"/> is <c>false</c> for this value.
        /// </summary>
        public static AnchorToken None => new AnchorToken(null, Vector3.zero, Quaternion.identity);
    }
}
