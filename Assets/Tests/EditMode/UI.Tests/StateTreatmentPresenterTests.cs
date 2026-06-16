using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.TestTools;
using Veilwalkers.AR;
using Veilwalkers.Core.Contracts;
using Veilwalkers.UI;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// <see cref="StateTreatmentPresenter"/> as the pure-logic state→treatment map (Story 6.5). The
    /// assertions are falsifiable: the cold-start ritual is a wipe with NO percentage member (AC-3), the
    /// slow-device fallback differs only in copy/flag (never gains a number), each AC-4 state maps to its
    /// treatment AND the alternate branches do NOT (the failure-path-cleanup-parity discipline), the
    /// safety firewall holds (a safety treatment's copy is never a diegetic line, every non-safety
    /// treatment is non-exception), and an out-of-range enum degrades to None + warns (NFR-3).
    /// </summary>
    public sealed class StateTreatmentPresenterTests
    {
        private static StateTreatmentPresenter NewPresenter() => new StateTreatmentPresenter();

        private static readonly string[] DiegeticLines =
        {
            VeilVoice.EnterAr,
            VeilVoice.RewardedAction,
            VeilVoice.NewTier,
            VeilVoice.AnchorRestored,
            VeilVoice.LockedSlot,
        };

        // ---- AC-3: the veil-parting wipe ritual, never a spinner/percentage ----

        [Test]
        public void Prewarming_is_the_veil_parting_wipe_ritual()
        {
            StateTreatment t = NewPresenter().ForArSession(ArSessionState.Prewarming, slowDevice: false);
            Assert.AreEqual(StateTreatmentKind.VeilPartingWipe, t.Kind);
            Assert.IsTrue(t.ShowsRitualNotProgress, "Cold-start is a ritual, never a progress bar.");
            Assert.IsFalse(t.IsSafetyException);
            Assert.IsFalse(t.SlowDeviceFallback);
            Assert.IsFalse(string.IsNullOrEmpty(t.Copy));
        }

        [Test]
        public void StateTreatment_has_no_numeric_progress_member()
        {
            // AC-3 structural invariant: a treatment is QUALITATIVE. The type must carry NO float/double/int
            // numeric-progress member — the absence is the "never a percentage" guarantee. (Bools like
            // ShowsRitualNotProgress/SlowDeviceFallback are flags, not progress numbers.)
            var numericMembers = typeof(StateTreatment)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(float) || f.FieldType == typeof(double) || f.FieldType == typeof(int))
                .Select(f => f.Name)
                .Concat(typeof(StateTreatment)
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.PropertyType == typeof(float) || p.PropertyType == typeof(double) || p.PropertyType == typeof(int))
                    .Select(p => p.Name))
                .ToArray();

            CollectionAssert.IsEmpty(numericMembers,
                "StateTreatment must expose NO numeric-progress member (AC-3: ritual, never a percentage). Found: "
                + string.Join(", ", numericMembers));
        }

        [Test]
        public void SlowDevice_wipe_differs_only_in_copy_and_flag_never_gains_a_number()
        {
            StateTreatmentPresenter p = NewPresenter();
            StateTreatment normal = p.ForArSession(ArSessionState.Prewarming, slowDevice: false);
            StateTreatment slow = p.ForArSession(ArSessionState.Prewarming, slowDevice: true);

            Assert.AreEqual(StateTreatmentKind.VeilPartingWipe, slow.Kind, "Still the wipe, even on a slow device.");
            Assert.IsTrue(slow.ShowsRitualNotProgress, "Still a ritual, never a percentage.");
            Assert.IsTrue(slow.SlowDeviceFallback, "The slow-device fallback flag is set.");
            Assert.IsFalse(normal.SlowDeviceFallback);
            Assert.AreNotEqual(normal.Copy, slow.Copy, "The slow-device fallback is MORE ritual copy, not the same line.");
        }

        [Test]
        public void Warm_or_running_or_paused_session_gets_no_treatment()
        {
            StateTreatmentPresenter p = NewPresenter();
            Assert.AreEqual(StateTreatmentKind.None, p.ForArSession(ArSessionState.Cold, false).Kind);
            Assert.AreEqual(StateTreatmentKind.None, p.ForArSession(ArSessionState.Ready, false).Kind);
            Assert.AreEqual(StateTreatmentKind.None, p.ForArSession(ArSessionState.Running, false).Kind);
            Assert.AreEqual(StateTreatmentKind.None, p.ForArSession(ArSessionState.Paused, false).Kind);
        }

        [Test]
        public void ForArSession_degrades_an_out_of_range_state_to_None_with_a_warning()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("unmapped ArSessionState"));
            StateTreatment t = NewPresenter().ForArSession((ArSessionState)99, false);
            Assert.AreEqual(StateTreatmentKind.None, t.Kind);
        }

        // ---- AC-4: plane-not-found ----

        [Test]
        public void NeedsCoaching_carries_the_placement_message_as_friendly_coaching()
        {
            StateTreatment t = NewPresenter().ForPlacement(PlacementResult.NeedsCoaching("Raise your phone to part the Veil."));
            Assert.AreEqual(StateTreatmentKind.PlaneCoaching, t.Kind);
            Assert.AreEqual("Raise your phone to part the Veil.", t.Copy, "Coaching passes through the placement's own message.");
        }

        [Test]
        public void Placed_or_failed_placement_gets_no_coaching()
        {
            StateTreatmentPresenter p = NewPresenter();
            Assert.AreNotEqual(StateTreatmentKind.PlaneCoaching, p.ForPlacement(PlacementResult.Placed(default)).Kind);
            Assert.AreNotEqual(StateTreatmentKind.PlaneCoaching, p.ForPlacement(PlacementResult.Failed()).Kind);
        }

        // ---- AC-4: lost-anchor "pulled back through the veil" ----

        [Test]
        public void RelocatedToPlane_is_the_pulled_back_beat()
        {
            StateTreatment t = NewPresenter().ForAnchorRestore(AnchorRestoreResult.RelocatedToPlane);
            Assert.AreEqual(StateTreatmentKind.AnchorRestoreBeat, t.Kind);
            Assert.AreEqual(VeilVoice.AnchorRestored, t.Copy);
        }

        [Test]
        public void Restored_or_failed_anchor_does_not_produce_the_pulled_back_beat()
        {
            // The alternate-branch discipline: assert the OTHER branches do NOT emit the "pulled back" copy.
            StateTreatmentPresenter p = NewPresenter();
            StateTreatment restored = p.ForAnchorRestore(AnchorRestoreResult.Restored);
            StateTreatment failed = p.ForAnchorRestore(AnchorRestoreResult.Failed);

            Assert.AreNotEqual(StateTreatmentKind.AnchorRestoreBeat, restored.Kind, "An in-place restore is silent.");
            Assert.AreNotEqual(StateTreatmentKind.AnchorRestoreBeat, failed.Kind);
            Assert.AreNotEqual(VeilVoice.AnchorRestored, restored.Copy);
            Assert.AreNotEqual(VeilVoice.AnchorRestored, failed.Copy);
        }

        // ---- AC-4: camera-denied re-grant ----

        [Test]
        public void Denied_camera_is_the_re_grant_path()
        {
            StateTreatment t = NewPresenter().ForCameraPermission(CameraPermissionState.Denied);
            Assert.AreEqual(StateTreatmentKind.CameraReGrant, t.Kind);
            Assert.IsFalse(string.IsNullOrEmpty(t.Copy));
        }

        [Test]
        public void Non_denied_camera_states_get_no_re_grant_treatment()
        {
            StateTreatmentPresenter p = NewPresenter();
            Assert.AreNotEqual(StateTreatmentKind.CameraReGrant, p.ForCameraPermission(CameraPermissionState.Disclosure).Kind);
            Assert.AreNotEqual(StateTreatmentKind.CameraReGrant, p.ForCameraPermission(CameraPermissionState.Requesting).Kind);
            Assert.AreNotEqual(StateTreatmentKind.CameraReGrant, p.ForCameraPermission(CameraPermissionState.Granted).Kind);
        }

        // ---- AC-4: Codex first-discovery ----

        [Test]
        public void First_discovery_revealing_a_new_tier_carries_the_new_tier_voice()
        {
            StateTreatment t = NewPresenter().ForCodexDiscovery(isFirstDiscovery: true, revealsNewTier: true);
            Assert.AreEqual(StateTreatmentKind.CodexFirstDiscovery, t.Kind);
            Assert.AreEqual(VeilVoice.NewTier, t.Copy, "A new tier shows 'The Veil shows you more…'.");
        }

        [Test]
        public void First_discovery_without_a_new_tier_flips_and_ticks_but_carries_no_new_tier_copy()
        {
            StateTreatment t = NewPresenter().ForCodexDiscovery(isFirstDiscovery: true, revealsNewTier: false);
            Assert.AreEqual(StateTreatmentKind.CodexFirstDiscovery, t.Kind, "The slot still flips + the count ticks.");
            Assert.AreNotEqual(VeilVoice.NewTier, t.Copy, "Without a new tier there is no silhouette-fade-in copy.");
        }

        [Test]
        public void A_non_discovery_gets_no_treatment()
        {
            StateTreatment t = NewPresenter().ForCodexDiscovery(isFirstDiscovery: false, revealsNewTier: false);
            Assert.AreEqual(StateTreatmentKind.None, t.Kind);
        }

        // ---- AC-2: the safety firewall ----

        [Test]
        public void Safety_gate_blocking_full_is_a_plain_safety_exception_never_a_diegetic_line()
        {
            StateTreatment t = NewPresenter().ForSafetyGate(ArSafetyGateState.BlockingFull);
            Assert.AreEqual(StateTreatmentKind.SafetyWarning, t.Kind);
            Assert.IsTrue(t.IsSafetyException, "A safety warning IS the plain exception.");
            Assert.AreEqual(VeilVoice.Safety.ArWarningFull, t.Copy);
            CollectionAssert.DoesNotContain(DiegeticLines, t.Copy, "Safety copy is NEVER a diegetic Veil-voice line.");
        }

        [Test]
        public void Safety_gate_fast_card_is_a_plain_safety_exception()
        {
            StateTreatment t = NewPresenter().ForSafetyGate(ArSafetyGateState.BlockingFastCard);
            Assert.AreEqual(StateTreatmentKind.SafetyWarning, t.Kind);
            Assert.IsTrue(t.IsSafetyException);
            Assert.AreEqual(VeilVoice.Safety.ArWarningFast, t.Copy);
            CollectionAssert.DoesNotContain(DiegeticLines, t.Copy);
        }

        [Test]
        public void Non_blocking_safety_states_get_no_treatment()
        {
            StateTreatmentPresenter p = NewPresenter();
            Assert.AreEqual(StateTreatmentKind.None, p.ForSafetyGate(ArSafetyGateState.Inactive).Kind);
            Assert.AreEqual(StateTreatmentKind.None, p.ForSafetyGate(ArSafetyGateState.Acknowledged).Kind);
        }

        [Test]
        public void Every_non_safety_treatment_is_not_a_safety_exception()
        {
            // The firewall's other half: ONLY the SafetyWarning kind may set IsSafetyException.
            StateTreatmentPresenter p = NewPresenter();
            StateTreatment[] nonSafety =
            {
                p.ForArSession(ArSessionState.Prewarming, false),
                p.ForArSession(ArSessionState.Prewarming, true),
                p.ForPlacement(PlacementResult.NeedsCoaching("x")),
                p.ForAnchorRestore(AnchorRestoreResult.RelocatedToPlane),
                p.ForCameraPermission(CameraPermissionState.Denied),
                p.ForCodexDiscovery(true, true),
                // The calm "None" treatment via a public path (the internal factory isn't visible to the
                // test assembly): a warm session returns None, and default(StateTreatment) is also None.
                p.ForArSession(ArSessionState.Running, false),
                default,
            };
            foreach (StateTreatment t in nonSafety)
            {
                Assert.IsFalse(t.IsSafetyException, $"A {t.Kind} treatment must NOT be a safety exception.");
            }
        }

        // ---- default(StateTreatment) safety ----

        [Test]
        public void Default_treatment_copy_is_empty_not_null()
        {
            StateTreatment def = default;
            Assert.AreEqual(string.Empty, def.Copy, "A default treatment's Copy coalesces to empty (no NRE on .Length).");
            Assert.AreEqual(StateTreatmentKind.None, def.Kind);
            Assert.IsFalse(def.IsSafetyException);
        }

        // ---- enum pins ----

        [Test]
        public void StateTreatmentKind_members_and_order_are_pinned()
        {
            StateTreatmentKind[] expected =
            {
                StateTreatmentKind.None,
                StateTreatmentKind.VeilPartingWipe,
                StateTreatmentKind.PlaneCoaching,
                StateTreatmentKind.AnchorRestoreBeat,
                StateTreatmentKind.CameraReGrant,
                StateTreatmentKind.CodexFirstDiscovery,
                StateTreatmentKind.SafetyWarning,
            };
            CollectionAssert.AreEqual(expected, Enum.GetValues(typeof(StateTreatmentKind)));
        }

        // ---- single-source pins (Task 4) ----

        [Test]
        public void Codex_locked_slot_copy_is_the_single_VeilVoice_source()
        {
            // The CodexDetailViewModel locked-slot const forwards VeilVoice.LockedSlot — one source.
            Assert.AreEqual(VeilVoice.LockedSlot, CodexDetailViewModel.NotDiscoveredCopy);
        }
    }
}
