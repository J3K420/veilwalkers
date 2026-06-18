using UnityEngine;

namespace Veilwalkers.AR
{
    /// <summary>
    /// A candidate plane the relocation decision (<see cref="AnchorRestoreService"/>, Story 3.5, AC-3)
    /// ranks when a lost anchor must be re-placed. Headless-safe — it carries a plain
    /// <see cref="UnityEngine.Pose"/> (not an AR-Foundation trackable), so the relocation DECISION and
    /// its fake stay AR-Foundation-free (the same rationale as the 3.4 forward seam's <c>Pose</c> usage).
    /// <para>
    /// <b>Distance basis is distance-from-CAMERA, not from the lost anchor.</b> "Nearest plane within the
    /// current camera frustum, fall back to absolute-nearest only if nothing is in view"
    /// (architecture.md:471-473) is a camera-relative concept; both the in-frustum ranking and the
    /// absolute-nearest fallback use the SAME camera-relative distance so the two are consistent. The
    /// device adapter (<see cref="ArcoreAnchorProvider"/>, <c>TODO Story 8.3</c>) computes
    /// <see cref="DistanceFromCamera"/> as the camera-to-plane distance.
    /// </para>
    /// </summary>
    public readonly struct PlaneCandidate
    {
        /// <summary>The pose the object would relocate to on this plane.</summary>
        public Pose Pose { get; }

        /// <summary>Whether this plane is within the current camera frustum (in view). The relocation
        /// decision prefers the nearest in-frustum candidate; only if NONE are in frustum does it fall
        /// back to the absolute-nearest (AC-3).</summary>
        public bool InCameraFrustum { get; }

        /// <summary>Camera-relative distance to this plane (the ranking key). Smaller = nearer.</summary>
        public float DistanceFromCamera { get; }

        public PlaneCandidate(Pose pose, bool inCameraFrustum, float distanceFromCamera)
        {
            Pose = pose;
            InCameraFrustum = inCameraFrustum;
            DistanceFromCamera = distanceFromCamera;
        }
    }
}
