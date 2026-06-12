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
    }
}
