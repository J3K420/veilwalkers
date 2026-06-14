using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.AR;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="ArSessionService"/> (Story 3.3) — the sole-owner AR-session
    /// lifecycle (cold→prewarming→ready→running→paused, AC-1), the double-start-safe prewarm + cheap
    /// tear-back + backgrounding (AC-3), and the inherited Story 3.1 camera-permission re-check
    /// (re-validate before start + on backgrounded-resume). The lifecycle's one async seam
    /// (<see cref="IArSession.StartAsync"/>) is awaited via the project's <c>.GetAwaiter().GetResult()</c>
    /// idiom (the pattern in <c>Economy.Tests/ChargePipelineTests</c>); a controllable
    /// <see cref="FakeArSession"/> + a <see cref="FakeCameraPermission"/>-backed
    /// <see cref="CameraPermissionFlow"/> drive all state. NO <c>[UnityTest]</c> (none exist in this
    /// project).
    /// <para>
    /// Anti-tautology: every assertion checks the PRODUCTION service's transitions + the fake's SEAM
    /// CALL COUNTS (StartCount/StopCount/PauseCount/ResumeCount), never a literal recomputed from the
    /// same input. The in-flight double-start (StartCount stays 1), the permission-revoked-on-resume
    /// (Resume NOT called + event fired), and the unsupported/ungranted refusals (StartCount == 0) are
    /// the falsifiable, mutation-testable pins.
    /// </para>
    /// </summary>
    public sealed class ArSessionServiceTests
    {
        private static CameraPermissionFlow GrantedFlow()
            => new CameraPermissionFlow(new FakeCameraPermission { HasCameraPermission = true });

        private static ArSessionService NewService(FakeArSession session, CameraPermissionFlow flow)
            => new ArSessionService(session, flow);

        // ---- ctor guards ----

        [Test]
        public void Ctor_rejects_a_null_session_seam()
        {
            Assert.Throws<ArgumentNullException>(() => new ArSessionService(null, GrantedFlow()));
        }

        [Test]
        public void Ctor_rejects_a_null_camera_permission_flow()
        {
            Assert.Throws<ArgumentNullException>(() => new ArSessionService(new FakeArSession(), null));
        }

        // ---- AC-2 (staging contract): construction does NOT warm ----

        [Test]
        public void Construction_does_NOT_warm_the_session_warmup_is_not_awaited_at_wire_time()
        {
            var session = new FakeArSession();

            var service = NewService(session, GrantedFlow());

            Assert.AreEqual(ArSessionState.Cold, service.State, "A freshly constructed service is Cold.");
            Assert.AreEqual(0, session.StartCount,
                "Construction must NOT fire StartAsync — AR warmup is on the async path, NOT awaited at " +
                "wire-time (AC-CS-2). A ctor that eagerly warms breaks the staging contract.");
        }

        // ---- AC-1: the lifecycle path cold → prewarming → ready → running → paused → running → cold ----

        [Test]
        public void Full_lifecycle_cold_prewarm_ready_enter_run_pause_resume_teardown()
        {
            var session = new FakeArSession();
            var service = NewService(session, GrantedFlow());

            Assert.AreEqual(ArSessionState.Cold, service.State);

            service.PrewarmAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ArSessionState.Ready, service.State, "Prewarm (supported+granted) → Ready.");
            Assert.AreEqual(1, session.StartCount, "Exactly one subsystem start.");

            service.EnterAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ArSessionState.Running, service.State, "Enter from Ready → Running (no second start).");
            Assert.AreEqual(1, session.StartCount);

            service.OnApplicationPause(true);
            Assert.AreEqual(ArSessionState.Paused, service.State, "Backgrounding while Running → Paused.");
            Assert.AreEqual(1, session.PauseCount);

            service.OnApplicationPause(false);
            Assert.AreEqual(ArSessionState.Running, service.State, "Focus-regain (still granted) → Running.");
            Assert.AreEqual(1, session.ResumeCount);

            service.TearDown();
            Assert.AreEqual(ArSessionState.Cold, service.State, "TearDown → Cold.");
            Assert.AreEqual(1, session.StopCount, "Exactly one Stop.");
        }

        // ---- AC-3: double-start safe (the in-flight guard) ----

        [Test]
        public void A_second_prewarm_while_a_start_is_in_flight_does_NOT_fire_a_second_StartAsync()
        {
            var session = new FakeArSession { AutoCompleteStart = false }; // hold StartAsync in flight
            var service = NewService(session, GrantedFlow());

            System.Threading.Tasks.Task first = service.PrewarmAsync();
            Assert.AreEqual(ArSessionState.Prewarming, service.State, "While StartAsync is in flight → Prewarming.");
            Assert.AreEqual(1, session.StartCount);

            // A second prewarm races the in-flight start — it must NOT fire a second StartAsync.
            System.Threading.Tasks.Task second = service.PrewarmAsync();
            Assert.AreEqual(1, session.StartCount,
                "Double-start guard: a second PrewarmAsync while in flight must NOT fire a second StartAsync.");
            Assert.AreSame(first, second, "The second call returns the same in-flight task.");

            // Complete the start → both resolve to Ready.
            session.CompleteStart();
            first.GetAwaiter().GetResult();
            Assert.AreEqual(ArSessionState.Ready, service.State);
            Assert.AreEqual(1, session.StartCount);
        }

        [Test]
        public void Enter_while_a_prewarm_is_in_flight_awaits_it_then_runs()
        {
            var session = new FakeArSession { AutoCompleteStart = false }; // hold StartAsync in flight
            var service = NewService(session, GrantedFlow());

            System.Threading.Tasks.Task prewarm = service.PrewarmAsync();
            Assert.AreEqual(ArSessionState.Prewarming, service.State);

            // Enter while still Prewarming — it must await the in-flight prewarm, not fire a second start.
            System.Threading.Tasks.Task enter = service.EnterAsync();
            Assert.AreEqual(1, session.StartCount, "Enter during prewarm must NOT fire a second StartAsync.");
            Assert.IsFalse(enter.IsCompleted, "Enter awaits the in-flight prewarm before running.");

            // Complete the prewarm → enter resolves to Running.
            session.CompleteStart();
            enter.GetAwaiter().GetResult();
            prewarm.GetAwaiter().GetResult();

            Assert.AreEqual(ArSessionState.Running, service.State, "Once the in-flight prewarm reaches Ready, Enter runs.");
            Assert.AreEqual(1, session.StartCount);
        }

        // ---- AC-3: TearDown WHILE a prewarm is in flight (the CR-found double-start race) ----

        [Test]
        public void TearDown_while_a_prewarm_is_in_flight_returns_to_cold_and_does_not_clobber_back_to_ready()
        {
            var session = new FakeArSession { AutoCompleteStart = false }; // hold StartAsync in flight
            var service = NewService(session, GrantedFlow());

            System.Threading.Tasks.Task prewarm = service.PrewarmAsync();
            Assert.AreEqual(ArSessionState.Prewarming, service.State);
            Assert.AreEqual(1, session.StartCount);

            // Back out DURING the in-flight start.
            service.TearDown();
            Assert.AreEqual(ArSessionState.Cold, service.State, "TearDown during prewarm → Cold.");
            Assert.AreEqual(1, session.StopCount, "Stop is called once (aborts the in-flight start per the IArSession contract).");

            // The in-flight start completing must NOT clobber Cold back to Ready.
            session.CompleteStart();
            prewarm.GetAwaiter().GetResult();
            Assert.AreEqual(ArSessionState.Cold, service.State,
                "The in-flight StartAsync completing after a tear-down must NOT resurrect the session to Ready.");
        }

        [Test]
        public void Prewarm_after_a_torn_down_in_flight_prewarm_starts_clean_no_concurrent_double_start()
        {
            var session = new FakeArSession { AutoCompleteStart = false };
            var service = NewService(session, GrantedFlow());

            System.Threading.Tasks.Task first = service.PrewarmAsync(); // in flight
            service.TearDown(); // back out during the start
            Assert.AreEqual(ArSessionState.Cold, service.State);
            Assert.AreEqual(1, session.StartCount);

            // A fresh prewarm from Cold must NOT be tricked by a stale in-flight latch into either
            // returning a stale task OR firing a second concurrent StartAsync while the first is still
            // pending. It starts a clean new prewarm (StartCount rises to 2 — sequential, not concurrent,
            // because the first was torn down). The DOUBLE-START hazard is the two firing concurrently on
            // a LIVE session; here the first was aborted by Stop, so the second is a legitimate restart.
            session.AutoCompleteStart = true; // let the fresh prewarm complete
            service.PrewarmAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ArSessionState.Ready, service.State, "A fresh prewarm after tear-back reaches Ready cleanly.");
            Assert.AreEqual(2, session.StartCount, "The restart is sequential (post-tear-down), not a concurrent double-start.");

            // Complete the original aborted start's task so it does not dangle; it must not change state.
            session.CompleteStart();
            first.GetAwaiter().GetResult();
            Assert.AreEqual(ArSessionState.Ready, service.State,
                "The torn-down original start completing late must not perturb the fresh session.");
        }

        // ---- patch #2: a downstream OnArSessionInterrupted subscriber throwing cannot crash the pump (NFR-3) ----

        [Test]
        public void A_throwing_interrupted_subscriber_is_swallowed_and_does_not_escape_the_pause_pump()
        {
            var session = new FakeArSession();
            var permission = new FakeCameraPermission { HasCameraPermission = true };
            var service = NewService(session, new CameraPermissionFlow(permission));

            service.EnterAsync().GetAwaiter().GetResult();
            service.OnApplicationPause(true);
            permission.HasCameraPermission = false; // revoked while backgrounded

            // A misbehaving downstream subscriber (Story 3.5/Epic 4) throws — it must NOT escape the
            // lifecycle pump (NFR-3). The service swallows + logs an Error and stays consistent.
            service.OnArSessionInterrupted += () => throw new InvalidOperationException("subscriber boom");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("revoked while"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("a subscriber threw"));

            Assert.DoesNotThrow(() => service.OnApplicationPause(false),
                "A throwing OnArSessionInterrupted subscriber must not propagate out of the lifecycle pump (NFR-3).");
            Assert.AreEqual(ArSessionState.Cold, service.State, "The session is still torn down despite the subscriber throw.");
        }

        [Test]
        public void Prewarm_when_already_ready_is_a_warned_no_op_no_second_start()
        {
            var session = new FakeArSession();
            var service = NewService(session, GrantedFlow());

            service.PrewarmAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ArSessionState.Ready, service.State);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("PrewarmAsync ignored .* already warm"));
            service.PrewarmAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ArSessionState.Ready, service.State);
            Assert.AreEqual(1, session.StartCount, "An already-warm prewarm fires no second start.");
        }

        // ---- AC-3: cheap tear-back from Ready ----

        [Test]
        public void Tear_back_from_ready_returns_to_cold_cheaply()
        {
            var session = new FakeArSession();
            var service = NewService(session, GrantedFlow());

            service.PrewarmAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ArSessionState.Ready, service.State);

            service.TearDown();

            Assert.AreEqual(ArSessionState.Cold, service.State, "Back out before committing → Cold.");
            Assert.AreEqual(1, session.StopCount);
        }

        // ---- AC-3: backgrounding pause/resume ----

        [Test]
        public void Backgrounding_while_running_pauses_and_focus_regain_resumes()
        {
            var session = new FakeArSession();
            var service = NewService(session, GrantedFlow());

            service.EnterAsync().GetAwaiter().GetResult(); // cold → prewarm → running
            Assert.AreEqual(ArSessionState.Running, service.State);

            service.OnApplicationPause(true);
            Assert.AreEqual(ArSessionState.Paused, service.State);
            Assert.AreEqual(1, session.PauseCount);
            Assert.AreEqual(0, session.ResumeCount);

            service.OnApplicationPause(false);
            Assert.AreEqual(ArSessionState.Running, service.State);
            Assert.AreEqual(1, session.ResumeCount);
        }

        // ---- AC-3: permission revoked while backgrounded (the inherited 3.1 deferral) ----

        [Test]
        public void Permission_revoked_while_backgrounded_does_NOT_resume_tears_down_and_interrupts()
        {
            var session = new FakeArSession();
            var permission = new FakeCameraPermission { HasCameraPermission = true };
            var flow = new CameraPermissionFlow(permission);
            var service = NewService(session, flow);

            service.EnterAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ArSessionState.Running, service.State);

            service.OnApplicationPause(true);
            Assert.AreEqual(ArSessionState.Paused, service.State);

            // Revoke camera permission WHILE backgrounded (Android can do this).
            permission.HasCameraPermission = false;

            int interrupted = 0;
            service.OnArSessionInterrupted += () => interrupted++;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("revoked while"));
            service.OnApplicationPause(false);

            Assert.AreEqual(ArSessionState.Cold, service.State,
                "A permission revoked while backgrounded must tear down, NOT silently resume.");
            Assert.AreEqual(0, session.ResumeCount, "Resume must NOT be called when permission was revoked.");
            Assert.AreEqual(1, session.StopCount, "The session is torn down on the revoked-resume path.");
            Assert.AreEqual(1, interrupted, "OnArSessionInterrupted fires exactly once so the re-grant flow takes over.");
        }

        // ---- AC-3: unsupported device refuses to prewarm (NFR-3 degrade) ----

        [Test]
        public void Unsupported_device_refuses_to_prewarm_and_stays_cold()
        {
            var session = new FakeArSession { IsSupported = false };
            var service = NewService(session, GrantedFlow());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("does not support AR"));
            service.PrewarmAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ArSessionState.Cold, service.State, "Unsupported → stays Cold (degrade, never crash).");
            Assert.AreEqual(0, session.StartCount, "No subsystem start on an unsupported device.");
        }

        // ---- AC-3: permission not granted refuses to prewarm (NFR-3 degrade) ----

        [Test]
        public void Ungranted_camera_permission_refuses_to_prewarm_and_stays_cold()
        {
            var session = new FakeArSession(); // supported
            var permission = new FakeCameraPermission { HasCameraPermission = false };
            var service = NewService(session, new CameraPermissionFlow(permission));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("permission is not granted"));
            service.PrewarmAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ArSessionState.Cold, service.State, "Ungranted → stays Cold.");
            Assert.AreEqual(0, session.StartCount, "No subsystem start without camera permission.");
        }

        // ---- decision #3: cold Enter prewarms (prewarm is an optimization, not a precondition) ----

        [Test]
        public void Cold_enter_without_a_prior_prewarm_still_runs_prewarm_is_optional()
        {
            var session = new FakeArSession();
            var service = NewService(session, GrantedFlow());

            service.EnterAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ArSessionState.Running, service.State,
                "A cold Enter (no prior prewarm) still reaches Running — prewarm is an optimization, not a precondition.");
            Assert.AreEqual(1, session.StartCount, "It prewarms once on the way.");
        }

        [Test]
        public void Cold_enter_on_an_unsupported_device_refuses_to_run()
        {
            var session = new FakeArSession { IsSupported = false };
            var service = NewService(session, GrantedFlow());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("does not support AR"));
            service.EnterAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ArSessionState.Cold, service.State, "Cold enter refused on an unsupported device → stays Cold.");
            Assert.AreEqual(0, session.StartCount);
        }

        // ---- illegal transitions (NFR-3): warned no-ops, never throw ----

        [Test]
        public void TearDown_from_cold_is_a_warned_no_op()
        {
            var session = new FakeArSession();
            var service = NewService(session, GrantedFlow());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("TearDown ignored — already Cold"));
            service.TearDown();

            Assert.AreEqual(ArSessionState.Cold, service.State);
            Assert.AreEqual(0, session.StopCount, "No Stop on a cold tear-down.");
        }

        [Test]
        public void Background_pause_from_cold_is_a_warned_no_op()
        {
            var session = new FakeArSession();
            var service = NewService(session, GrantedFlow());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("OnApplicationPause\\(true\\) ignored — not running"));
            service.OnApplicationPause(true);

            Assert.AreEqual(ArSessionState.Cold, service.State);
            Assert.AreEqual(0, session.PauseCount);
        }

        [Test]
        public void Focus_regain_when_not_paused_is_a_warned_no_op()
        {
            var session = new FakeArSession();
            var service = NewService(session, GrantedFlow());

            service.PrewarmAsync().GetAwaiter().GetResult(); // Ready, not Paused

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("OnApplicationPause\\(false\\) ignored — not paused"));
            service.OnApplicationPause(false);

            Assert.AreEqual(ArSessionState.Ready, service.State);
            Assert.AreEqual(0, session.ResumeCount);
        }

        [Test]
        public void Enter_when_already_running_is_a_warned_no_op_no_second_start()
        {
            var session = new FakeArSession();
            var service = NewService(session, GrantedFlow());

            service.EnterAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ArSessionState.Running, service.State);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("EnterAsync ignored — already entered"));
            service.EnterAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ArSessionState.Running, service.State);
            Assert.AreEqual(1, session.StartCount, "Re-entering fires no second start.");
        }
    }
}
