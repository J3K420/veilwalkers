using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using Veilwalkers.UI;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// <see cref="VeilVoice"/> as the single canonical diegetic-copy home (Story 6.5, AC-1) + the plain
    /// safety EXCEPTION (AC-2). The assertions are falsifiable: each diegetic line equals its EXACT
    /// canonical wording (incl. the U+2026 ellipsis + the period on the locked slot), the
    /// <see cref="VeilVoice.For"/> map is total + degrades gracefully, and the safety group is DISJOINT
    /// from the diegetic group (a safety string is never a Veil-voice line).
    /// </summary>
    public sealed class VeilVoiceTests
    {
        // ---- AC-1: the exact canonical wording ----

        [Test]
        public void EnterAr_is_the_canonical_line()
        {
            Assert.AreEqual("Part the Veil", VeilVoice.EnterAr);
        }

        [Test]
        public void RewardedAction_is_the_canonical_line()
        {
            Assert.AreEqual("Consult the Veil", VeilVoice.RewardedAction);
        }

        [Test]
        public void NewTier_uses_the_typographic_ellipsis_not_three_dots()
        {
            Assert.AreEqual("The Veil shows you more…", VeilVoice.NewTier);
            // Non-tautological: the real ellipsis is U+2026, NOT three ASCII dots — guard the regression.
            Assert.IsTrue(VeilVoice.NewTier.Contains("…"), "Must use the typographic ellipsis U+2026.");
            Assert.IsFalse(VeilVoice.NewTier.Contains("..."), "Must NOT use three ASCII dots.");
        }

        [Test]
        public void AnchorRestored_is_the_canonical_line()
        {
            Assert.AreEqual("Pulled back through the Veil", VeilVoice.AnchorRestored);
        }

        [Test]
        public void LockedSlot_is_the_canonical_line_with_the_trailing_period()
        {
            Assert.AreEqual("Not yet discovered.", VeilVoice.LockedSlot);
            // The period form is authoritative (AC + the existing CodexDetailViewModel value) — the
            // period-less UX-DR14 table entry is not. Guard against silently dropping it.
            Assert.IsTrue(VeilVoice.LockedSlot.EndsWith("."), "The locked-slot copy must keep its period.");
        }

        // ---- AC-1: the For(VeilMoment) total map ----

        [Test]
        public void For_returns_a_nonempty_line_for_every_defined_moment()
        {
            foreach (VeilMoment moment in System.Enum.GetValues(typeof(VeilMoment)))
            {
                Assert.IsFalse(string.IsNullOrEmpty(VeilVoice.For(moment)),
                    $"VeilVoice.For({moment}) must return a non-empty line.");
            }
        }

        [Test]
        public void For_maps_each_moment_to_its_matching_constant()
        {
            Assert.AreEqual(VeilVoice.EnterAr, VeilVoice.For(VeilMoment.EnterAr));
            Assert.AreEqual(VeilVoice.RewardedAction, VeilVoice.For(VeilMoment.RewardedAction));
            Assert.AreEqual(VeilVoice.NewTier, VeilVoice.For(VeilMoment.NewTier));
            Assert.AreEqual(VeilVoice.AnchorRestored, VeilVoice.For(VeilMoment.AnchorRestored));
            Assert.AreEqual(VeilVoice.LockedSlot, VeilVoice.For(VeilMoment.LockedSlot));
        }

        [Test]
        public void For_degrades_an_out_of_range_moment_to_EnterAr_with_a_warning()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("unmapped VeilMoment"));
            string copy = VeilVoice.For((VeilMoment)99);
            Assert.AreEqual(VeilVoice.EnterAr, copy, "An out-of-range moment degrades to the calm default.");
        }

        [Test]
        public void VeilMoment_members_and_order_are_pinned()
        {
            VeilMoment[] expected =
            {
                VeilMoment.EnterAr,
                VeilMoment.RewardedAction,
                VeilMoment.NewTier,
                VeilMoment.AnchorRestored,
                VeilMoment.LockedSlot,
            };
            CollectionAssert.AreEqual(expected, System.Enum.GetValues(typeof(VeilMoment)));
        }

        // ---- AC-2: the safety group is plain AND disjoint from the diegetic voice ----

        [Test]
        public void Safety_copy_is_disjoint_from_every_diegetic_line()
        {
            var diegetic = new HashSet<string>
            {
                VeilVoice.EnterAr,
                VeilVoice.RewardedAction,
                VeilVoice.NewTier,
                VeilVoice.AnchorRestored,
                VeilVoice.LockedSlot,
            };

            // No safety string may equal any diegetic line — the plain exception is structurally separate
            // (the 6.1 AC-4 disjointness-pin precedent: tier set ∩ frame palette = ∅).
            Assert.IsFalse(diegetic.Contains(VeilVoice.Safety.ArWarningFull),
                "The full safety warning must not be a Veil-voice line.");
            Assert.IsFalse(diegetic.Contains(VeilVoice.Safety.ArWarningFast),
                "The fast safety card must not be a Veil-voice line.");
        }

        [Test]
        public void Safety_copy_is_present_and_nonempty()
        {
            Assert.IsFalse(string.IsNullOrEmpty(VeilVoice.Safety.ArWarningFull));
            Assert.IsFalse(string.IsNullOrEmpty(VeilVoice.Safety.ArWarningFast));
        }
    }
}
