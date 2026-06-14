using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.AR;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="PlaneAnchorService"/> (Story 3.4) — the AC-1 place-on-a-detected-plane,
    /// the AC-2 coach-and-do-NOT-spawn-into-empty-space, and the NFR-3 never-throw-typed-result decision.
    /// A controllable <see cref="FakeArAnchorProvider"/> drives all state.
    /// <para>
    /// Anti-tautology: every assertion checks the PRODUCTION service's typed result + the fake's SEAM CALL
    /// COUNTS (GetPlacementPoseCalls/CreateAnchorCalls), never a literal recomputed from the same input.
    /// The AC-2 "no plane → GetPlacementPoseCalls == 0 && CreateAnchorCalls == 0" is the load-bearing,
    /// mutation-testable pin (drop the HasTrackablePlane guard → it asks for a pose with no plane → red).
    /// The coaching test asserts the PRODUCTION constant <see cref="PlaneAnchorService.CoachingMessage"/>.
    /// </para>
    /// </summary>
    public sealed class PlaneAnchorServiceTests
    {
        private static PlaneAnchorService NewService(FakeArAnchorProvider provider)
            => new PlaneAnchorService(provider);

        // ---- ctor guard ----

        [Test]
        public void Ctor_rejects_a_null_provider_seam()
        {
            Assert.Throws<ArgumentNullException>(() => new PlaneAnchorService(null));
        }

        // ---- AC-2: no plane → coaching, and NO placement attempt (no empty-space spawn) ----

        [Test]
        public void No_plane_returns_coaching_with_the_production_message()
        {
            var provider = new FakeArAnchorProvider { HasTrackablePlane = false };
            var service = NewService(provider);

            PlacementResult result = service.TryPlace();

            Assert.AreEqual(PlacementOutcome.NeedsCoaching, result.Outcome);
            Assert.AreEqual(PlaneAnchorService.CoachingMessage, result.Message,
                "The coaching copy must be the production constant (AC-2).");
        }

        [Test]
        public void No_plane_does_NOT_ask_for_a_pose_or_create_an_anchor()
        {
            var provider = new FakeArAnchorProvider { HasTrackablePlane = false };
            var service = NewService(provider);

            service.TryPlace();

            Assert.AreEqual(0, provider.GetPlacementPoseCalls,
                "With no trackable plane the service must NOT ask for a placement pose (AC-2 — no object " +
                "spawned into empty space).");
            Assert.AreEqual(0, provider.CreateAnchorCalls,
                "With no trackable plane the service must NOT create an anchor (AC-2).");
        }

        // ---- AC-1: plane found → placed + anchored ----

        [Test]
        public void Plane_and_pose_and_successful_anchor_returns_placed_with_the_token()
        {
            var pose = new Pose(new Vector3(4f, 5f, 6f), Quaternion.Euler(0f, 90f, 0f));
            var provider = new FakeArAnchorProvider
            {
                HasTrackablePlane = true,
                HasPlacementPose = true,
                AnchorCreationSucceeds = true,
                PoseToReturn = pose,
                PlaneIdToReturn = "plane-42",
            };
            var service = NewService(provider);

            PlacementResult result = service.TryPlace();

            Assert.AreEqual(PlacementOutcome.Placed, result.Outcome);
            Assert.AreEqual(1, provider.CreateAnchorCalls, "Exactly one anchor was created.");

            AnchorToken token = result.Token;
            Assert.AreEqual("plane-42", token.trackableId, "The token carries the plane id.");
            Assert.AreEqual(pose.position, token.position, "The token carries the placement position.");
            Assert.AreEqual(pose.rotation, token.rotation, "The token carries the placement rotation.");
        }

        // ---- AC-1 / NFR-3: plane tracked but no pose right now → coaching, no anchor ----

        [Test]
        public void Plane_but_no_placement_pose_returns_coaching_without_creating_an_anchor()
        {
            var provider = new FakeArAnchorProvider
            {
                HasTrackablePlane = true,
                HasPlacementPose = false,
            };
            var service = NewService(provider);

            PlacementResult result = service.TryPlace();

            Assert.AreEqual(PlacementOutcome.NeedsCoaching, result.Outcome);
            Assert.AreEqual(1, provider.GetPlacementPoseCalls, "It asked for a pose (a plane was tracked).");
            Assert.AreEqual(0, provider.CreateAnchorCalls,
                "No pose was available, so no anchor must be created — still no empty-space spawn.");
        }

        // ---- NFR-3: anchor creation fails → Failed + guidance, no crash ----

        [Test]
        public void Anchor_creation_failure_returns_failed_and_logs_a_warning_without_throwing()
        {
            var provider = new FakeArAnchorProvider
            {
                HasTrackablePlane = true,
                HasPlacementPose = true,
                AnchorCreationSucceeds = false,
            };
            var service = NewService(provider);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("anchor creation failed"));

            PlacementResult result = service.TryPlace();

            Assert.AreEqual(PlacementOutcome.Failed, result.Outcome);
            Assert.AreEqual(1, provider.CreateAnchorCalls, "It attempted to create the anchor once.");
        }

        // ---- Edge case (CR): TryGetPlacementPose returns true with a null planeId ----
        // The seam contract says a true return carries a valid planeId; if a provider violates that and
        // hands back a null planeId, the service must NOT crash — it round-trips whatever the seam gives
        // into the token (the AnchorToken validity contract is Story 3.5's, not 3.4's). This pins the
        // current graceful behavior: a null planeId flows into TryCreateAnchor and the token, no throw.
        [Test]
        public void Pose_with_a_null_plane_id_round_trips_without_throwing()
        {
            var pose = new Pose(new Vector3(7f, 8f, 9f), Quaternion.identity);
            var provider = new FakeArAnchorProvider
            {
                HasTrackablePlane = true,
                HasPlacementPose = true,
                AnchorCreationSucceeds = true,
                PoseToReturn = pose,
                PlaneIdToReturn = null,
            };
            var service = NewService(provider);

            PlacementResult result = service.TryPlace();

            Assert.AreEqual(PlacementOutcome.Placed, result.Outcome, "A null planeId is still placed (3.4 does not validate it — that is Story 3.5's AnchorToken.IsValid contract).");
            Assert.AreEqual(1, provider.CreateAnchorCalls, "The service forwarded the (null-planeId) pose to anchor creation.");
            Assert.IsNull(result.Token.trackableId, "The null planeId round-trips into the token (no crash, no silent substitution — 3.5 owns validity).");
        }

        // ---- OnCoachingChanged fires on enter (coaching) and clears on leave (placed/failed) ----

        [Test]
        public void Coaching_event_fires_the_message_on_enter_and_clears_on_placement()
        {
            var provider = new FakeArAnchorProvider { HasTrackablePlane = false };
            var service = NewService(provider);

            string lastMessage = "unset";
            int raises = 0;
            service.OnCoachingChanged += msg => { lastMessage = msg; raises++; };

            // Enter coaching (no plane).
            service.TryPlace();
            Assert.AreEqual(PlaneAnchorService.CoachingMessage, lastMessage, "Entering coaching publishes the message.");
            Assert.AreEqual(1, raises, "The event fired once on entering coaching.");

            // A second coaching frame must NOT re-fire (no change).
            service.TryPlace();
            Assert.AreEqual(1, raises, "Repeated coaching must not re-raise the event (no change).");

            // Now a plane + pose + success → placed → coaching clears (null).
            provider.HasTrackablePlane = true;
            provider.HasPlacementPose = true;
            provider.AnchorCreationSucceeds = true;
            service.TryPlace();
            Assert.IsNull(lastMessage, "A successful placement clears the coaching banner (null).");
            Assert.AreEqual(2, raises, "The event fired again on leaving coaching.");
        }

        // CR patch: a Failed outcome (anchor creation fails) must ALSO clear a standing coaching banner —
        // Failed is a guidance state, not a coaching state. Without the reconcile, a "Move your phone..."
        // banner left up by a prior coaching frame would linger over the Failed outcome.
        [Test]
        public void Anchor_creation_failure_clears_a_standing_coaching_banner()
        {
            var provider = new FakeArAnchorProvider { HasTrackablePlane = false };
            var service = NewService(provider);

            string lastMessage = "unset";
            int raises = 0;
            service.OnCoachingChanged += msg => { lastMessage = msg; raises++; };

            // Enter coaching (no plane) — the banner is up.
            service.TryPlace();
            Assert.AreEqual(PlaneAnchorService.CoachingMessage, lastMessage);
            Assert.AreEqual(1, raises);

            // Now a plane + pose appear but anchor creation FAILS → Failed → the standing banner must clear.
            provider.HasTrackablePlane = true;
            provider.HasPlacementPose = true;
            provider.AnchorCreationSucceeds = false;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("anchor creation failed"));
            PlacementResult result = service.TryPlace();

            Assert.AreEqual(PlacementOutcome.Failed, result.Outcome);
            Assert.IsNull(lastMessage, "Failed must clear the standing coaching banner (it is a guidance state, not coaching).");
            Assert.AreEqual(2, raises, "The event fired again on leaving coaching for the Failed outcome.");
        }
    }
}
