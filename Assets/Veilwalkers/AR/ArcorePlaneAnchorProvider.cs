using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.AR
{
    /// <summary>
    /// Production <see cref="IArPlaneAnchorProvider"/> over AR Foundation's plane-detection +
    /// anchor-creation surface (Story 3.4) — the untestable adapter edge, the <see cref="ArcoreSession"/>
    /// equivalent. It holds NO branching beyond the platform <c>#if</c> (the AR "thin adapter" mandate,
    /// architecture.md:466); all placement/coaching DECISIONS live in <see cref="PlaneAnchorService"/>.
    /// <para>
    /// <b>Logic-complete-but-subsystem-stub (decision #2).</b> AR Foundation's <c>ARPlaneManager</c> /
    /// <c>ARRaycastManager</c> / <c>ARAnchorManager</c> are scene <c>MonoBehaviour</c>s, and Story 3.4
    /// builds no scene (consistent with 3.1/3.2/3.3). So the <c>#else</c> editor path is real-enough that
    /// the placement/coaching logic is reachable in-editor and proven against
    /// <c>FakeArPlaneAnchorProvider</c>, platform-independent: it reports NO trackable plane (so the
    /// editor exercises the AC-2 coaching path by default) and returns false/default from the
    /// pose/anchor methods. The <c>#if UNITY_ANDROID</c> device path that drives the real subsystem is a
    /// documented <c>TODO</c> wired when the AR rig scene lands — Story 6.3 (scene placement). The
    /// genuinely device-only, CI-untestable subsystem glue (architecture.md:591) is the only thing
    /// deferred; the placement decision itself ships complete + tested.
    /// </para>
    /// <para>
    /// <b>Never throws (NFR-3):</b> any AR Foundation call that can throw is wrapped + logged via
    /// <see cref="GameLog"/> and degrades, mirroring <see cref="ArcoreSession"/>.
    /// </para>
    /// </summary>
    public sealed class ArcorePlaneAnchorProvider : IArPlaneAnchorProvider
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
        // FakeArPlaneAnchorProvider; this device glue is the deferred, device-only, CI-untestable edge
        // (architecture.md:591). Occlusion (AROcclusionManager + the URP occlusion shader) and
        // environmental lighting (light-estimation → scene light) are AR-rig-scene render features and
        // also land in Story 6.3 / Epic 6.
        public bool HasTrackablePlane => false;

        public bool TryGetPlacementPose(out Pose pose, out string planeId)
        {
            GameLog.Info("ArcorePlaneAnchorProvider.TryGetPlacementPose: device-path stub — the AR rig is not placed yet (TODO Story 6.3).");
            pose = Pose.identity;
            planeId = null;
            return false;
        }

        public bool TryCreateAnchor(in Pose pose, string planeId, out AnchorToken token)
        {
            GameLog.Info("ArcorePlaneAnchorProvider.TryCreateAnchor: device-path stub (TODO Story 6.3).");
            token = default;
            return false;
        }
#else
        // Editor / non-Android: the AR Foundation plane subsystem does not exist. Report NO trackable
        // plane so the placement decision is reachable in-editor on its AC-2 coaching path, and make the
        // pose/anchor methods return false/default. The PlaneAnchorService LOGIC never depends on these
        // side effects — it is proven entirely against FakeArPlaneAnchorProvider in AR.Tests,
        // platform-independent — so editor behavior matches the seam contract.
        public bool HasTrackablePlane => false;

        public bool TryGetPlacementPose(out Pose pose, out string planeId)
        {
            GameLog.Info("ArcorePlaneAnchorProvider.TryGetPlacementPose: no-op off-device (plane subsystem unavailable).");
            pose = Pose.identity;
            planeId = null;
            return false;
        }

        public bool TryCreateAnchor(in Pose pose, string planeId, out AnchorToken token)
        {
            GameLog.Info("ArcorePlaneAnchorProvider.TryCreateAnchor: no-op off-device.");
            token = default;
            return false;
        }
#endif
    }
}
