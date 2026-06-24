using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Core.Contracts;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#endif

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
    /// <c>TODO</c> wired when the AR rig scene lands — Story 8.3 (author the AR rig + the device bodies).
    /// The genuinely device-only, CI-untestable subsystem glue (architecture.md:591) is the only thing
    /// deferred; the placement + restore decisions themselves ship complete + tested.
    /// </para>
    /// <para>
    /// <b>Never throws (NFR-3):</b> any AR Foundation call that can throw is wrapped + logged via
    /// <see cref="GameLog"/> and degrades, mirroring <see cref="ArcoreSession"/>.
    /// </para>
    /// </summary>
    public sealed class ArcoreAnchorProvider : IArAnchorProvider
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Story 8.3 device body. Drives the scene-placed ARPlaneManager / ARRaycastManager /
        // ARAnchorManager (authored into ARHunt.unity in Gate 1). Managers are resolved LAZILY on first
        // use — the rig is not live when Bootstrap constructs this adapter. All DECISIONS (coach-vs-place;
        // Restored/RelocatedToPlane/Failed) stay in PlaneAnchorService / AnchorRestoreService; this is
        // thin subsystem glue. Never throws (NFR-3): degrades to "no plane / no re-acquire / no candidates"
        // so the services run their coaching / Failed paths.
        private ARPlaneManager _planeManager;
        private ARRaycastManager _raycastManager;
        private ARAnchorManager _anchorManager;

        private bool ResolveManagers()
        {
            if (_planeManager != null && _raycastManager != null && _anchorManager != null)
            {
                return true;
            }

            // All three live on the XR Origin GameObject; one origin lookup finds them together.
            if (_planeManager == null)
            {
                _planeManager = Object.FindObjectOfType<ARPlaneManager>(includeInactive: true);
            }

            if (_raycastManager == null)
            {
                _raycastManager = Object.FindObjectOfType<ARRaycastManager>(includeInactive: true);
            }

            if (_anchorManager == null)
            {
                _anchorManager = Object.FindObjectOfType<ARAnchorManager>(includeInactive: true);
            }

            bool ready = _planeManager != null && _raycastManager != null && _anchorManager != null;
            if (!ready)
            {
                GameLog.Warn("ArcoreAnchorProvider: AR plane/raycast/anchor managers not in scene yet; degrading (NFR-3).");
            }

            return ready;
        }

        public bool HasTrackablePlane
        {
            get
            {
                if (!ResolveManagers())
                {
                    return false;
                }

                foreach (ARPlane plane in _planeManager.trackables)
                {
                    if (plane.trackingState == TrackingState.Tracking)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private static readonly List<ARRaycastHit> RaycastHits = new List<ARRaycastHit>();

        public bool TryGetPlacementPose(out Pose pose, out string planeId)
        {
            pose = Pose.identity;
            planeId = null;

            if (!ResolveManagers())
            {
                return false;
            }

            try
            {
                Camera cam = Camera.main;
                Vector2 screenCenter = cam != null
                    ? new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f)
                    : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

                RaycastHits.Clear();
                if (!_raycastManager.Raycast(screenCenter, RaycastHits, TrackableType.PlaneWithinPolygon))
                {
                    return false;
                }

                ARRaycastHit hit = RaycastHits[0];
                pose = hit.pose;
                planeId = hit.trackableId.ToString();
                return true;
            }
            catch (System.Exception ex)
            {
                GameLog.Warn("ArcoreAnchorProvider.TryGetPlacementPose failed; degrading (NFR-3). " + ex.Message);
                return false;
            }
        }

        public bool TryCreateAnchor(in Pose pose, string planeId, out AnchorToken token)
        {
            token = AnchorToken.None;

            if (!ResolveManagers())
            {
                return false;
            }

            try
            {
                ARAnchor anchor = null;

                // Prefer attaching to the named plane (more stable); fall back to a free-standing anchor.
                ARPlane plane = FindPlane(planeId);
                if (plane != null)
                {
                    anchor = _anchorManager.AttachAnchor(plane, pose);
                }

                if (anchor == null)
                {
                    anchor = _anchorManager.AddAnchor(pose);
                }

                if (anchor == null)
                {
                    return false;
                }

                token = new AnchorToken(anchor.trackableId.ToString(), pose.position, pose.rotation);
                return true;
            }
            catch (System.Exception ex)
            {
                GameLog.Warn("ArcoreAnchorProvider.TryCreateAnchor failed; degrading (NFR-3). " + ex.Message);
                return false;
            }
        }

        private ARPlane FindPlane(string planeId)
        {
            if (string.IsNullOrEmpty(planeId) || _planeManager == null)
            {
                return null;
            }

            foreach (ARPlane plane in _planeManager.trackables)
            {
                if (plane.trackableId.ToString() == planeId)
                {
                    return plane;
                }
            }

            return null;
        }

        public bool TryReacquireAnchor(in AnchorToken token, out Pose pose)
        {
            pose = Pose.identity;

            if (!ResolveManagers())
            {
                return false;
            }

            try
            {
                // Re-find the still-tracked anchor by its saved trackable id.
                foreach (ARAnchor anchor in _anchorManager.trackables)
                {
                    if (anchor.trackableId.ToString() == token.trackableId &&
                        anchor.trackingState == TrackingState.Tracking)
                    {
                        pose = new Pose(anchor.transform.position, anchor.transform.rotation);
                        return true;
                    }
                }

                return false;
            }
            catch (System.Exception ex)
            {
                GameLog.Warn("ArcoreAnchorProvider.TryReacquireAnchor failed; degrading (NFR-3). " + ex.Message);
                return false;
            }
        }

        public bool TryGetRelocationCandidates(out PlaneCandidate[] candidates)
        {
            candidates = System.Array.Empty<PlaneCandidate>();

            if (!ResolveManagers())
            {
                return false;
            }

            try
            {
                Camera cam = Camera.main;
                var list = new List<PlaneCandidate>();

                foreach (ARPlane plane in _planeManager.trackables)
                {
                    if (plane.trackingState != TrackingState.Tracking)
                    {
                        continue;
                    }

                    Vector3 planePos = plane.transform.position;
                    var candidatePose = new Pose(planePos, plane.transform.rotation);

                    bool inFrustum = false;
                    float distance = 0f;
                    if (cam != null)
                    {
                        distance = Vector3.Distance(cam.transform.position, planePos);
                        Vector3 vp = cam.WorldToViewportPoint(planePos);
                        inFrustum = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                    }

                    list.Add(new PlaneCandidate(candidatePose, inFrustum, distance));
                }

                candidates = list.ToArray();
                return candidates.Length > 0;
            }
            catch (System.Exception ex)
            {
                GameLog.Warn("ArcoreAnchorProvider.TryGetRelocationCandidates failed; degrading (NFR-3). " + ex.Message);
                return false;
            }
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
