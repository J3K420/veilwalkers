using UnityEngine;
using Veilwalkers.AR;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// Test double for <see cref="IArAnchorProvider"/> (Story 3.4 forward + Story 3.5 restore). Mirrors
    /// <see cref="FakeArSession"/>/<see cref="FakeCameraPermission"/>: settable state the test flips to
    /// model the forward cases ("no plane" / "plane but no pose" / "anchor fails") AND the restore cases
    /// ("re-acquire succeeds/fails" / the relocation candidate set), plus call counters so a test can
    /// prove <see cref="PlaneAnchorService"/> / <see cref="AnchorRestoreService"/> called the seam exactly
    /// when expected (the AC-2 "no spawn without a plane" pin; the AC-2/3 "no re-acquire on an invalid
    /// token" + "no relocation query when Restored" pins). No AR-Foundation types — only <see cref="Pose"/>
    /// and <see cref="PlaneCandidate"/> — so the logic is platform-independent and proven entirely here.
    /// </summary>
    internal sealed class FakeArAnchorProvider : IArAnchorProvider
    {
        /// <summary>Settable plane-tracking state the service reads to decide coach-vs-place.</summary>
        public bool HasTrackablePlane { get; set; }

        /// <summary>When true, <see cref="TryGetPlacementPose"/> hands back <see cref="PoseToReturn"/> +
        /// <see cref="PlaneIdToReturn"/>; when false it returns false (a plane is tracked but no placeable
        /// pose in front of the player).</summary>
        public bool HasPlacementPose { get; set; }

        /// <summary>When true, <see cref="TryCreateAnchor"/> succeeds and hands back
        /// <see cref="TokenToReturn"/>; when false it fails (default token).</summary>
        public bool AnchorCreationSucceeds { get; set; } = true;

        /// <summary>The pose <see cref="TryGetPlacementPose"/> returns when <see cref="HasPlacementPose"/>.</summary>
        public Pose PoseToReturn { get; set; } = new Pose(new Vector3(1f, 2f, 3f), Quaternion.identity);

        /// <summary>The plane id <see cref="TryGetPlacementPose"/> returns.</summary>
        public string PlaneIdToReturn { get; set; } = "plane-1";

        /// <summary>The anchor token <see cref="TryCreateAnchor"/> hands back on success. Defaults to a
        /// token built from <see cref="PoseToReturn"/> + <see cref="PlaneIdToReturn"/> at call time when
        /// left unset (see <see cref="TryCreateAnchor"/>).</summary>
        public AnchorToken? TokenToReturn { get; set; }

        /// <summary>How many times the service asked for a placement pose. The AC-2 pin asserts this stays
        /// 0 when there is no plane.</summary>
        public int GetPlacementPoseCalls { get; private set; }

        /// <summary>How many times the service tried to create an anchor. The AC-2 pin asserts this stays
        /// 0 when there is no plane.</summary>
        public int CreateAnchorCalls { get; private set; }

        public bool TryGetPlacementPose(out Pose pose, out string planeId)
        {
            GetPlacementPoseCalls++;

            if (!HasPlacementPose)
            {
                pose = Pose.identity;
                planeId = null;
                return false;
            }

            pose = PoseToReturn;
            planeId = PlaneIdToReturn;
            return true;
        }

        public bool TryCreateAnchor(in Pose pose, string planeId, out AnchorToken token)
        {
            CreateAnchorCalls++;

            if (!AnchorCreationSucceeds)
            {
                token = default;
                return false;
            }

            // Default success behavior: build a token from the pose+planeId the service handed in (so the
            // test can assert the service round-tripped the provider's pose into the token), unless the
            // test pinned an explicit TokenToReturn.
            token = TokenToReturn ?? new AnchorToken(planeId, pose.position, pose.rotation);
            return true;
        }

        // ---- Restore path (Story 3.5) ----

        /// <summary>When true, <see cref="TryReacquireAnchor"/> succeeds and hands back
        /// <see cref="ReacquiredPose"/>; when false it fails (anchor lost → the service relocates).</summary>
        public bool ReacquireSucceeds { get; set; }

        /// <summary>The pose <see cref="TryReacquireAnchor"/> returns on success.</summary>
        public Pose ReacquiredPose { get; set; } = new Pose(new Vector3(9f, 9f, 9f), Quaternion.identity);

        /// <summary>The relocation candidates <see cref="TryGetRelocationCandidates"/> hands back. Empty
        /// (default) models "no plane available" → the service returns Failed.</summary>
        public PlaneCandidate[] RelocationCandidates { get; set; } = System.Array.Empty<PlaneCandidate>();

        /// <summary>How many times the service tried to re-acquire the anchor. The invalid-token pin
        /// asserts this stays 0 (no native re-acquire on a junk/None token).</summary>
        public int ReacquireCalls { get; private set; }

        /// <summary>How many times the service queried relocation candidates. The Restored pin asserts
        /// this stays 0 (no relocation query when the re-acquire succeeded).</summary>
        public int GetRelocationCandidatesCalls { get; private set; }

        public bool TryReacquireAnchor(in AnchorToken token, out Pose pose)
        {
            ReacquireCalls++;

            if (!ReacquireSucceeds)
            {
                pose = Pose.identity;
                return false;
            }

            pose = ReacquiredPose;
            return true;
        }

        public bool TryGetRelocationCandidates(out PlaneCandidate[] candidates)
        {
            GetRelocationCandidatesCalls++;
            candidates = RelocationCandidates;
            return RelocationCandidates != null && RelocationCandidates.Length > 0;
        }
    }
}
