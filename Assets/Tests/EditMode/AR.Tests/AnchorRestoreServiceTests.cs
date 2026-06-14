using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.AR;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="AnchorRestoreService"/> (Story 3.5) — the AC-2 <c>Restored</c> (re-anchor
    /// in place), the AC-3 <c>RelocatedToPlane</c> (nearest-in-frustum, fallback absolute-nearest, never
    /// vanish), and the AC-4 <c>Failed</c> (guidance, never crash) decision, plus the settled 1.2
    /// <see cref="AnchorToken.IsValid"/>/<see cref="AnchorToken.None"/> contract. A controllable
    /// <see cref="FakeArAnchorProvider"/> drives all state.
    /// <para>
    /// Anti-tautology: every assertion checks the PRODUCTION decision's typed <see cref="AnchorRestoreResult"/>
    /// + the fake's SEAM CALL COUNTS (ReacquireCalls/GetRelocationCandidatesCalls), never a literal
    /// recomputed from the same input. The nearest-in-frustum pin (an in-frustum-FARTHER plane beats an
    /// out-of-frustum-NEARER plane), the invalid-token-no-reacquire pin (ReacquireCalls == 0), and the
    /// re-acquire-preferred pin (GetRelocationCandidatesCalls == 0 when Restored) are the falsifiable,
    /// mutation-testable assertions.
    /// </para>
    /// </summary>
    public sealed class AnchorRestoreServiceTests
    {
        // Matches any non-empty warning text — asserts that A warning was logged (AC-4 guidance) without
        // coupling the test to the exact guidance copy (which is implementation detail).
        private static readonly System.Text.RegularExpressions.Regex AnyText =
            new System.Text.RegularExpressions.Regex(".");

        private static AnchorRestoreService NewService(FakeArAnchorProvider provider)
            => new AnchorRestoreService(provider);

        private static AnchorToken ValidToken()
            => new AnchorToken("anchor-1", new Vector3(1f, 0f, 1f), Quaternion.identity);

        private static PlaneCandidate Candidate(float distance, bool inFrustum)
            => new PlaneCandidate(
                new Pose(new Vector3(distance, 0f, 0f), Quaternion.identity), inFrustum, distance);

        // ---- ctor guard ----

        [Test]
        public void Ctor_rejects_a_null_provider_seam()
        {
            Assert.Throws<ArgumentNullException>(() => new AnchorRestoreService(null));
        }

        // ---- AC-2: Restored ----

        [Test]
        public void Valid_token_that_reacquires_returns_restored_in_place()
        {
            var pose = new Pose(new Vector3(2f, 3f, 4f), Quaternion.Euler(0f, 45f, 0f));
            var provider = new FakeArAnchorProvider { ReacquireSucceeds = true, ReacquiredPose = pose };
            var service = NewService(provider);

            AnchorRestoreResult result = service.TryRestore(ValidToken(), out Pose outPose);

            Assert.AreEqual(AnchorRestoreResult.Restored, result);
            Assert.AreEqual(1, provider.ReacquireCalls, "It tried to re-acquire the same trackable once.");
            Assert.AreEqual(0, provider.GetRelocationCandidatesCalls,
                "Re-acquire succeeded, so it must NOT query relocation candidates (re-acquire is preferred).");
            Assert.AreEqual(pose, outPose, "Restored re-anchors at the re-acquired pose (in place).");
        }

        // ---- AC-3: RelocatedToPlane — nearest IN-FRUSTUM wins (THE pin) ----

        [Test]
        public void Lost_anchor_relocates_to_the_nearest_in_frustum_plane_over_a_closer_out_of_frustum_one()
        {
            // re-acquire fails; candidates: in-frustum at distance 5, out-of-frustum at distance 2.
            var inFrustumFar = Candidate(5f, inFrustum: true);
            var outOfFrustumNear = Candidate(2f, inFrustum: false);
            var provider = new FakeArAnchorProvider
            {
                ReacquireSucceeds = false,
                RelocationCandidates = new[] { outOfFrustumNear, inFrustumFar },
            };
            var service = NewService(provider);

            AnchorRestoreResult result = service.TryRestore(ValidToken(), out Pose outPose);

            Assert.AreEqual(AnchorRestoreResult.RelocatedToPlane, result);
            Assert.AreEqual(inFrustumFar.Pose, outPose,
                "Nearest IN-FRUSTUM (distance 5) must beat the closer OUT-OF-FRUSTUM plane (distance 2) — " +
                "'nearest plane within the current camera frustum' (AC-3).");
            Assert.AreEqual(1, provider.GetRelocationCandidatesCalls);
        }

        // ---- AC-3: RelocatedToPlane — absolute-nearest fallback when nothing is in view ----

        [Test]
        public void Lost_anchor_with_no_in_frustum_plane_falls_back_to_the_absolute_nearest()
        {
            // re-acquire fails; candidates: out-of-frustum at 7, out-of-frustum at 3 (NONE in frustum).
            var far = Candidate(7f, inFrustum: false);
            var near = Candidate(3f, inFrustum: false);
            var provider = new FakeArAnchorProvider
            {
                ReacquireSucceeds = false,
                RelocationCandidates = new[] { far, near },
            };
            var service = NewService(provider);

            AnchorRestoreResult result = service.TryRestore(ValidToken(), out Pose outPose);

            Assert.AreEqual(AnchorRestoreResult.RelocatedToPlane, result);
            Assert.AreEqual(near.Pose, outPose,
                "With nothing in frustum, fall back to the absolute-nearest (distance 3 over 7) — AC-3.");
        }

        // ---- AC-3 robustness (CR): a non-finite distance must not win the nearest selection ----

        [Test]
        public void Lost_anchor_ignores_a_NaN_distance_candidate_and_relocates_to_a_valid_one()
        {
            // candidates[0] has a NaN distance (a degenerate projection the device adapter could produce);
            // without the guard it would become a sticky "best" that no later candidate can beat (every
            // comparison against NaN is false). The valid in-frustum candidate must still be chosen.
            var nanFirst = new PlaneCandidate(
                new Pose(new Vector3(100f, 0f, 0f), Quaternion.identity), inCameraFrustum: true, float.NaN);
            var valid = Candidate(6f, inFrustum: true);
            var provider = new FakeArAnchorProvider
            {
                ReacquireSucceeds = false,
                RelocationCandidates = new[] { nanFirst, valid },
            };
            var service = NewService(provider);

            AnchorRestoreResult result = service.TryRestore(ValidToken(), out Pose outPose);

            Assert.AreEqual(AnchorRestoreResult.RelocatedToPlane, result);
            Assert.AreEqual(valid.Pose, outPose,
                "A NaN-distance candidate must be skipped, not chosen — the valid plane is selected (no NaN pose).");
        }

        // ---- AC-4: Failed ----

        [Test]
        public void Lost_anchor_with_no_relocation_candidate_returns_failed_with_guidance_and_does_not_throw()
        {
            var provider = new FakeArAnchorProvider
            {
                ReacquireSucceeds = false,
                RelocationCandidates = Array.Empty<PlaneCandidate>(),
            };
            var service = NewService(provider);

            // Expect a warning (AC-4 mandates guidance is logged) without coupling to the exact wording —
            // the guidance copy is implementation detail, the behavioral pins are the result + pose.
            LogAssert.Expect(LogType.Warning, AnyText);

            AnchorRestoreResult result = service.TryRestore(ValidToken(), out Pose outPose);

            Assert.AreEqual(AnchorRestoreResult.Failed, result);
            Assert.AreEqual(new Pose(new Vector3(1f, 0f, 1f), Quaternion.identity), outPose,
                "On Failed the object stays at its last-known (token) pose — it never vanishes (AC-4).");
        }

        // ---- AC-4 / Task 2: invalid token → no native re-acquire attempt ----

        [Test]
        public void Invalid_token_does_NOT_attempt_a_reacquire_and_relocates_when_a_plane_exists()
        {
            var candidate = Candidate(4f, inFrustum: true);
            var provider = new FakeArAnchorProvider
            {
                ReacquireSucceeds = true, // even if it would succeed, an invalid token must skip it
                RelocationCandidates = new[] { candidate },
            };
            var service = NewService(provider);

            AnchorRestoreResult result = service.TryRestore(AnchorToken.None, out Pose outPose);

            Assert.AreEqual(AnchorRestoreResult.RelocatedToPlane, result);
            Assert.AreEqual(0, provider.ReacquireCalls,
                "A None/invalid token has no trackable to re-acquire — the service must NOT call the native " +
                "re-acquire (it would be junk). It goes straight to relocation.");
            Assert.AreEqual(candidate.Pose, outPose);
        }

        [Test]
        public void Invalid_token_with_no_plane_returns_failed_without_throwing()
        {
            var provider = new FakeArAnchorProvider
            {
                ReacquireSucceeds = true,
                RelocationCandidates = Array.Empty<PlaneCandidate>(),
            };
            var service = NewService(provider);

            LogAssert.Expect(LogType.Warning, AnyText);

            AnchorRestoreResult result = service.TryRestore(AnchorToken.None, out _);

            Assert.AreEqual(AnchorRestoreResult.Failed, result);
            Assert.AreEqual(0, provider.ReacquireCalls, "No re-acquire on a None token.");
        }

        // ---- AC-2 vs AC-3 boundary: re-acquire is preferred over relocate ----

        [Test]
        public void Reacquire_is_preferred_over_relocation_when_both_are_available()
        {
            var provider = new FakeArAnchorProvider
            {
                ReacquireSucceeds = true,
                RelocationCandidates = new[] { Candidate(1f, inFrustum: true) },
            };
            var service = NewService(provider);

            AnchorRestoreResult result = service.TryRestore(ValidToken(), out _);

            Assert.AreEqual(AnchorRestoreResult.Restored, result);
            Assert.AreEqual(0, provider.GetRelocationCandidatesCalls,
                "When re-acquire works, relocation is the fallback and must NOT be queried.");
        }

        // ---- AnchorToken.IsValid / None (settles the 1.2 deferral) ----

        [Test]
        public void AnchorToken_validity_contract()
        {
            Assert.IsFalse(default(AnchorToken).IsValid, "default(AnchorToken) is not a real anchor.");
            Assert.IsFalse(AnchorToken.None.IsValid, "None is not a real anchor.");
            Assert.IsFalse(new AnchorToken(null, Vector3.zero, Quaternion.identity).IsValid, "null id → invalid.");
            Assert.IsFalse(new AnchorToken("", Vector3.zero, Quaternion.identity).IsValid, "empty id → invalid.");
            Assert.IsTrue(new AnchorToken("id", Vector3.zero, Quaternion.identity).IsValid, "a real id → valid.");
            Assert.AreEqual(Quaternion.identity, AnchorToken.None.rotation,
                "None uses the explicit ctor so its rotation is identity (not the zero-quaternion default).");
        }
    }
}
