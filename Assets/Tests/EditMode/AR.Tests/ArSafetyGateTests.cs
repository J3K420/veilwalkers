using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.AR;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="ArSafetyGate"/> (Story 3.2, FR-3) — the mandatory blocking gate
    /// (AC-1), the full-read-first-per-session / fast-card-thereafter rule (AC-2), and the
    /// Shop-resume-does-NOT-re-fire rule (AC-3). The gate is synchronous with NO injected dependency,
    /// so plain <c>[Test]</c>s construct it directly — no fake/seam needed.
    /// <para>
    /// Anti-tautology: assertions check the PRODUCTION gate's returned <see cref="ArSafetyGateState"/>
    /// and its <see cref="ArSafetyGate.MayInteract"/> predicate, never a literal recomputed from the
    /// same input. The AC-2 full-then-fast test and the AC-3 resume-doesn't-burn-the-latch test are
    /// the falsifiable pins; the <c>MayInteract</c>-everywhere test is mutation-testable.
    /// </para>
    /// </summary>
    public sealed class ArSafetyGateTests
    {
        private static ArSafetyGate NewGate() => new ArSafetyGate();

        // ---- AC-1: the warning blocks until acknowledged ----

        [Test]
        public void First_cold_entry_shows_the_full_read_and_blocks_interaction()
        {
            var gate = NewGate();

            ArSafetyGateState state = gate.RequestEntry(ArEntryKind.ColdEntry);

            Assert.AreEqual(ArSafetyGateState.BlockingFull, state, "First cold entry of the session is the deliberate full read (AC-2).");
            Assert.IsFalse(gate.MayInteract, "The camera view + Encounter actions are blocked until acknowledged (AC-1).");
        }

        [Test]
        public void Acknowledging_the_full_read_releases_interaction()
        {
            var gate = NewGate();
            gate.RequestEntry(ArEntryKind.ColdEntry); // → BlockingFull

            ArSafetyGateState state = gate.Acknowledge();

            Assert.AreEqual(ArSafetyGateState.Acknowledged, state);
            Assert.IsTrue(gate.MayInteract, "Acknowledgement releases the camera view + Encounter actions (AC-1).");
        }

        [Test]
        public void The_fast_card_is_ALSO_blocking_and_cannot_be_skipped()
        {
            var gate = NewGate();
            gate.RequestEntry(ArEntryKind.ColdEntry); // full
            gate.Acknowledge();                       // → Acknowledged

            // A later cold entry this session: the fast card — but still mandatory-blocking (AC-1,
            // "cannot be A/B'd away").
            ArSafetyGateState fast = gate.RequestEntry(ArEntryKind.ColdEntry);
            Assert.AreEqual(ArSafetyGateState.BlockingFastCard, fast);
            Assert.IsFalse(gate.MayInteract, "The fast card is faster, never skippable — still blocks (AC-1).");

            ArSafetyGateState acked = gate.Acknowledge();
            Assert.AreEqual(ArSafetyGateState.Acknowledged, acked);
            Assert.IsTrue(gate.MayInteract);
        }

        // ---- AC-2: full read first-per-session, fast card thereafter ----

        [Test]
        public void First_cold_entry_is_full_and_later_cold_entries_are_fast()
        {
            var gate = NewGate();

            // First cold entry of the session → full deliberate read.
            Assert.AreEqual(ArSafetyGateState.BlockingFull, gate.RequestEntry(ArEntryKind.ColdEntry),
                "The very first cold entry must be the full read.");
            gate.Acknowledge();

            // Second (and any later) cold entry of the SAME session → fast card.
            Assert.AreEqual(ArSafetyGateState.BlockingFastCard, gate.RequestEntry(ArEntryKind.ColdEntry),
                "Later cold entries this session must downgrade to the fast card (the session-shown latch flipped).");
        }

        // ---- AC-3: Shop resume does NOT re-fire the warning, and is not a "read" ----

        [Test]
        public void Shop_resume_on_a_fresh_gate_skips_the_warning_and_does_not_block()
        {
            var gate = NewGate();

            ArSafetyGateState state = gate.RequestEntry(ArEntryKind.ShopResume);

            Assert.AreEqual(ArSafetyGateState.Acknowledged, state, "A Shop resume must NOT fire the warning (AC-3).");
            Assert.IsTrue(gate.MayInteract, "AR resumes interactive after a Shop round-trip (AC-3).");
        }

        [Test]
        public void Shop_resume_does_NOT_burn_the_once_per_session_full_read()
        {
            var gate = NewGate();

            // A resume happens BEFORE any cold entry (e.g. relaunch directly into a suspended encounter
            // path). It must not count as "the warning was read this session".
            gate.RequestEntry(ArEntryKind.ShopResume); // → Acknowledged, latch NOT set

            // The first REAL cold entry must still be the full deliberate read.
            ArSafetyGateState state = gate.RequestEntry(ArEntryKind.ColdEntry);

            Assert.AreEqual(ArSafetyGateState.BlockingFull, state,
                "A Shop resume is not a 'read' — the first cold entry after it still gets the full read (AC-3 anti-tautology guard).");
        }

        [Test]
        public void Shop_resume_after_a_full_read_still_skips_and_does_not_block()
        {
            var gate = NewGate();
            gate.RequestEntry(ArEntryKind.ColdEntry); // full
            gate.Acknowledge();                       // → Acknowledged

            ArSafetyGateState state = gate.RequestEntry(ArEntryKind.ShopResume);

            Assert.AreEqual(ArSafetyGateState.Acknowledged, state, "Resume is transparent regardless of prior state (AC-3).");
            Assert.IsTrue(gate.MayInteract);
        }

        // ---- illegal-transition safety (NFR-3: a UI-driven flow never throws) ----

        [Test]
        public void Acknowledge_from_Inactive_is_a_warned_no_op()
        {
            var gate = NewGate(); // Inactive, no warning showing

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Acknowledge ignored"));
            ArSafetyGateState state = gate.Acknowledge();

            Assert.AreEqual(ArSafetyGateState.Inactive, state, "Acknowledging with no warning up must not change state.");
            Assert.IsFalse(gate.MayInteract, "An illegal acknowledge must not release interaction.");
        }

        [Test]
        public void Acknowledge_when_already_Acknowledged_is_a_warned_no_op()
        {
            var gate = NewGate();
            gate.RequestEntry(ArEntryKind.ColdEntry); // full
            gate.Acknowledge();                       // → Acknowledged

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Acknowledge ignored"));
            ArSafetyGateState state = gate.Acknowledge();

            Assert.AreEqual(ArSafetyGateState.Acknowledged, state, "A redundant acknowledge is an idempotent-safe no-op.");
            Assert.IsTrue(gate.MayInteract);
        }

        // ---- AC-1: MayInteract is the single block — false in every non-acknowledged state ----

        [Test]
        public void MayInteract_is_false_in_Inactive()
        {
            var gate = NewGate();
            Assert.AreEqual(ArSafetyGateState.Inactive, gate.State);
            Assert.IsFalse(gate.MayInteract);
        }

        [Test]
        public void MayInteract_is_false_in_BlockingFull_and_BlockingFastCard_and_true_only_in_Acknowledged()
        {
            var gate = NewGate();

            gate.RequestEntry(ArEntryKind.ColdEntry); // BlockingFull
            Assert.IsFalse(gate.MayInteract, "BlockingFull must block.");

            gate.Acknowledge();                       // Acknowledged
            Assert.IsTrue(gate.MayInteract, "Acknowledged is the ONLY interactive state.");

            gate.RequestEntry(ArEntryKind.ColdEntry); // BlockingFastCard
            Assert.AreEqual(ArSafetyGateState.BlockingFastCard, gate.State);
            Assert.IsFalse(gate.MayInteract, "BlockingFastCard must block too (cannot be A/B'd away).");
        }
    }
}
