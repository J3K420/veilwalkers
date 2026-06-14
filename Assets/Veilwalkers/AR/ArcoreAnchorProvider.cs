using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.AR
{
    /// <summary>
    /// Production <see cref="IArAnchorProvider"/> over AR Foundation's plane-detection + anchor-create +
    /// anchor-RESTORE surface (Story 3.4 forward + Story 3.5 restore) — the untestable adapter edge, the
    /// <see cref="ArcoreSession"/> equivalent. It holds NO branching beyond the platform <c>#if</c> (the
    /// AR "thin adapter" mandate, architecture.md:466); all DECISIONS live in
    /// <see cref="PlaneAnchorService"/> (forward) and <see cref="AnchorRestoreService"/> (restore).
    /// <para>
    /// <b>Logic-complete-but-subsystem-stub.</b> AR Foundation's <c>ARPlaneManager</c> /
    /// <c>ARRaycastManager</c> / <c>ARAnchorManager</c> are scene <c>MonoBehaviour</c>s, and Stories
    /// 3.4/3.5 build no scene (consistent with 3.1/3.2/3.3). So the <c>#else</c> editor path is real-enough
    /// that the placement AND restore logic is reachable in-editor and proven against
    /// <c>FakeArAnchorProvider</c>, platform-independent: it reports NO trackable plane / NO re-acquire /
    /// NO relocation candidates (so the editor exercises the AC-2 coaching + the restore Failed path by
    /// default). The <c>#if UNITY_ANDROID</c> device path that drives the real subsystem is a documented
    /// <c>TODO</c> wired when the AR rig scene lands — Story 6.3 (scene placement). The genuinely
    /// device-only, CI-untestable subsystem glue (architecture.md:591) is the only thing deferred; the
    /// placement + restore decisions themselves ship complete + tested.
    /// </para>
    /// <para>
    /// <b>Never throws (NFR-3):</b> any AR Foundation call that can throw is wrapped + logged via
    /// <see cref="GameLog"/> and degrades, mirroring <see cref="ArcoreSession"/>.
    /// </para>
    /// </summary>
    public sealed class ArcoreAnchorProvider : IArAnchorProvider
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // TODO(Story 6.3): wire to the scene-placed AR Foundation ARPlaneManager / ARRaycastManager /
        // ARAnchorManager when the AR rig scene lands. HasTrackablePlane → ARPlaneManager.trackables.count
        // > 0 (planes whose trackingState is Tracking); TryGetPlacementPose → ARRaycastManager.Raycast
        // from screen-center against PlaneWithinPolygon, take the first hit's pose + the hit trackable's
        // TrackableId; TryCreateAnchor → ARAnchorManager.AttachAnchor(plane, pose) (or AddAnchor), then
        // build the AnchorToken from the anchor's trackableId + session-relative pose. Until the rig is
        // placed there is no plane subsystem to drive, so these are conservative stubs that never crash
        // (NFR-3): report NO plane (so the device build coaches rather than spawning into empty space)
        // and return false/default. The PlaneAnchorService decision is proven against
        // FakeArAnchorProvider; this device glue is the deferred, device-only, CI-untestable edge
        // (architecture.md:591). Occlusion (AROcclusionManager + the URP occlusion shader) and
        // environmental lighting (light-estimation → scene light) are AR-rig-scene render features and
        // also land in Story 6.3 / Epic 6.
        public bool HasTrackablePlane => false;

        public bool TryGetPlacementPose(out Pose pose, out string planeId)
        {
            GameLog.Info("ArcoreAnchorProvider.TryGetPlacementPose: device-path stub — the AR rig is not placed yet (TODO Story 6.3).");
            pose = Pose.identity;
            planeId = null;
            return false;
        }

        public bool TryCreateAnchor(in Pose pose, string planeId, out AnchorToken token)
        {
            GameLog.Info("ArcoreAnchorProvider.TryCreateAnchor: device-path stub (TODO Story 6.3).");
            token = default;
            return false;
        }

        // TODO(Story 6.3): restore path — TryReacquireAnchor → re-resolve the saved TrackableId via
        // ARAnchorManager (ARSession persistent/cloud anchor re-acquire or trackable re-find), out the
        // re-acquired pose; TryGetRelocationCandidates → ARPlaneManager.trackables projected against the
        // camera frustum (Camera.main.WorldToViewportPoint to set InCameraFrustum) with the camera-to-plane
        // distance, as PlaneCandidate[]. Until the rig is placed there is nothing to re-acquire / no planes,
        // so these are conservative stubs that never crash (NFR-3): re-acquire fails, no candidates — so the
        // device build exercises the restore Failed path. The AnchorRestoreService decision is proven
        // against FakeArAnchorProvider; this device glue is the deferred, device-only, CI-untestable edge.
        public bool TryReacquireAnchor(in AnchorToken token, out Pose pose)
        {
            GameLog.Info("ArcoreAnchorProvider.TryReacquireAnchor: device-path stub (TODO Story 6.3).");
            pose = Pose.identity;
            return false;
        }

        public bool TryGetRelocationCandidates(out PlaneCandidate[] candidates)
        {
            GameLog.Info("ArcoreAnchorProvider.TryGetRelocationCandidates: device-path stub (TODO Story 6.3).");
            candidates = System.Array.Empty<PlaneCandidate>();
            return false;
        }
#else
        // Editor / non-Android: the AR Foundation plane/anchor subsystem does not exist. Report NO
        // trackable plane / NO re-acquire / NO relocation candidates so the placement decision is
        // reachable in-editor on its AC-2 coaching path and the restore decision on its Failed path. The
        // PlaneAnchorService / AnchorRestoreService LOGIC never depends on these side effects — it is
        // proven entirely against FakeArAnchorProvider in AR.Tests, platform-independent — so editor
        // behavior matches the seam contract.
        public bool HasTrackablePlane => false;

        public bool TryGetPlacementPose(out Pose pose, out string planeId)
        {
            GameLog.Info("ArcoreAnchorProvider.TryGetPlacementPose: no-op off-device (plane subsystem unavailable).");
            pose = Pose.identity;
            planeId = null;
            return false;
        }

        public bool TryCreateAnchor(in Pose pose, string planeId, out AnchorToken token)
        {
            GameLog.Info("ArcoreAnchorProvider.TryCreateAnchor: no-op off-device.");
            token = default;
            return false;
        }

        public bool TryReacquireAnchor(in AnchorToken token, out Pose pose)
        {
            GameLog.Info("ArcoreAnchorProvider.TryReacquireAnchor: no-op off-device (anchor subsystem unavailable).");
            pose = Pose.identity;
            return false;
        }

        public bool TryGetRelocationCandidates(out PlaneCandidate[] candidates)
        {
            GameLog.Info("ArcoreAnchorProvider.TryGetRelocationCandidates: no-op off-device.");
            candidates = System.Array.Empty<PlaneCandidate>();
            return false;
        }
#endif
    }
}
