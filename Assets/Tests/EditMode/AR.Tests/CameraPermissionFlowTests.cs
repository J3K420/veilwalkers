using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.AR;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="CameraPermissionFlow"/> (Story 3.1) — the disclose-before-prompt
    /// rule (AC-1) and the denied → settings → recover, never-a-dead-end rule (AC-2). The flow is
    /// synchronous, so plain <c>[Test]</c>s; <see cref="FakeCameraPermission"/> drives all state.
    /// <para>
    /// Anti-tautology: every assertion checks the PRODUCTION flow's output (its returned/observed
    /// <see cref="CameraPermissionState"/> + the fake's call counters), never the fake's own inputs.
    /// The AC-1 tests pin <c>RequestCount</c> so a regression that prompts before the disclosure
    /// turns the suite red.
    /// </para>
    /// </summary>
    public sealed class CameraPermissionFlowTests
    {
        private static CameraPermissionFlow NewFlow(FakeCameraPermission permission)
            => new CameraPermissionFlow(permission);

        // ---- ctor guard ----

        [Test]
        public void Ctor_rejects_a_null_permission_seam()
        {
            Assert.Throws<ArgumentNullException>(() => new CameraPermissionFlow(null));
        }

        // ---- AC-1: disclosure precedes the prompt ----

        [Test]
        public void Evaluate_without_permission_shows_disclosure_and_does_NOT_prompt()
        {
            var permission = new FakeCameraPermission { HasCameraPermission = false };
            var flow = NewFlow(permission);

            CameraPermissionState state = flow.Evaluate();

            Assert.AreEqual(CameraPermissionState.Disclosure, state, "No permission → must show the disclosure card, not the prompt.");
            Assert.AreEqual(CameraPermissionState.Disclosure, flow.State);
            Assert.AreEqual(0, permission.RequestCount, "Evaluate() must NEVER fire the OS prompt (AC-1 disclose-before-prompt).");
        }

        [Test]
        public void Evaluate_when_already_granted_is_Granted_with_no_disclosure_and_no_prompt()
        {
            var permission = new FakeCameraPermission { HasCameraPermission = true };
            var flow = NewFlow(permission);

            CameraPermissionState state = flow.Evaluate();

            Assert.AreEqual(CameraPermissionState.Granted, state);
            Assert.AreEqual(0, permission.RequestCount, "An already-granted player is never prompted.");
        }

        [Test]
        public void ConfirmDisclosure_fires_exactly_one_prompt_and_enters_Requesting()
        {
            var permission = new FakeCameraPermission { HasCameraPermission = false };
            var flow = NewFlow(permission);
            flow.Evaluate(); // → Disclosure

            CameraPermissionState state = flow.ConfirmDisclosureAndRequest();

            Assert.AreEqual(CameraPermissionState.Requesting, state);
            Assert.AreEqual(1, permission.RequestCount, "The ONE prompt fires only after the disclosure is acknowledged.");
        }

        // ---- AC-1/AC-2: dialog resolves ----

        [Test]
        public void OnRequestResult_when_dialog_granted_becomes_Granted()
        {
            var permission = new FakeCameraPermission { HasCameraPermission = false };
            var flow = NewFlow(permission);
            flow.Evaluate();
            flow.ConfirmDisclosureAndRequest(); // → Requesting

            // The OS dialog resolved positively: the grant is observed by the next re-query.
            permission.HasCameraPermission = true;
            CameraPermissionState state = flow.OnRequestResult();

            Assert.AreEqual(CameraPermissionState.Granted, state);
        }

        [Test]
        public void OnRequestResult_when_dialog_denied_becomes_Denied()
        {
            var permission = new FakeCameraPermission { HasCameraPermission = false };
            var flow = NewFlow(permission);
            flow.Evaluate();
            flow.ConfirmDisclosureAndRequest(); // → Requesting

            // The player denied: re-query still sees no permission.
            CameraPermissionState state = flow.OnRequestResult();

            Assert.AreEqual(CameraPermissionState.Denied, state, "A denied dialog must route to the re-grant screen (AC-2).");
        }

        // ---- AC-2: denied → open settings → recover, never a dead end ----

        [Test]
        public void RetryFromSettings_opens_settings_and_stays_Denied_until_recheck()
        {
            var permission = new FakeCameraPermission { HasCameraPermission = false };
            var flow = NewFlow(permission);
            flow.Evaluate();
            flow.ConfirmDisclosureAndRequest();
            flow.OnRequestResult(); // → Denied

            CameraPermissionState state = flow.RetryFromSettings();

            Assert.AreEqual(1, permission.OpenSettingsCount, "Open Settings is the AC-2 path to system settings.");
            Assert.AreEqual(CameraPermissionState.Denied, state, "Stays Denied until the player returns and the flow re-queries — no dead end, but no premature flip.");
        }

        [Test]
        public void Denied_player_who_grants_in_settings_recovers_to_Granted()
        {
            var permission = new FakeCameraPermission { HasCameraPermission = false };
            var flow = NewFlow(permission);
            flow.Evaluate();
            flow.ConfirmDisclosureAndRequest();
            flow.OnRequestResult();   // → Denied
            flow.RetryFromSettings(); // opens settings, still Denied

            // Player grants in the OS Settings UI and returns; focus-regain re-queries.
            permission.HasCameraPermission = true;
            CameraPermissionState state = flow.OnRequestResult();

            Assert.AreEqual(CameraPermissionState.Granted, state, "The Settings round-trip recovers — never a dead end (AC-2).");
        }

        [Test]
        public void Denied_player_who_returns_still_denied_stays_Denied_and_can_retry()
        {
            var permission = new FakeCameraPermission { HasCameraPermission = false };
            var flow = NewFlow(permission);
            flow.Evaluate();
            flow.ConfirmDisclosureAndRequest();
            flow.OnRequestResult();   // → Denied
            flow.RetryFromSettings(); // opens settings (count 1), still Denied

            // Player returns without granting; re-query still denied.
            CameraPermissionState afterReturn = flow.OnRequestResult();
            Assert.AreEqual(CameraPermissionState.Denied, afterReturn);

            // The re-grant screen persists and the player can open settings again — no dead end.
            CameraPermissionState afterRetry = flow.RetryFromSettings();
            Assert.AreEqual(CameraPermissionState.Denied, afterRetry);
            Assert.AreEqual(2, permission.OpenSettingsCount, "The player can retry the Settings path as many times as needed.");
        }

        // ---- illegal-transition safety (NFR-3: a UI-driven flow never throws) ----

        [Test]
        public void ConfirmDisclosure_when_already_Granted_is_a_warned_no_op()
        {
            var permission = new FakeCameraPermission { HasCameraPermission = true };
            var flow = NewFlow(permission);
            flow.Evaluate(); // → Granted

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ConfirmDisclosureAndRequest ignored"));
            CameraPermissionState state = flow.ConfirmDisclosureAndRequest();

            Assert.AreEqual(CameraPermissionState.Granted, state, "An out-of-order confirm must not change state.");
            Assert.AreEqual(0, permission.RequestCount, "No prompt fires from an illegal transition.");
        }

        [Test]
        public void RetryFromSettings_when_not_Denied_is_a_warned_no_op()
        {
            var permission = new FakeCameraPermission { HasCameraPermission = false };
            var flow = NewFlow(permission);
            flow.Evaluate(); // → Disclosure

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("RetryFromSettings ignored"));
            CameraPermissionState state = flow.RetryFromSettings();

            Assert.AreEqual(CameraPermissionState.Disclosure, state);
            Assert.AreEqual(0, permission.OpenSettingsCount, "Settings is not opened from an illegal transition.");
        }
    }
}
