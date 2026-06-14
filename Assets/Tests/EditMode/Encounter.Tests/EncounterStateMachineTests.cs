using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Veilwalkers.Encounter.Tests
{
    /// <summary>
    /// <see cref="EncounterStateMachine"/> as the AC-1 explicit encounter-flow state machine: the legal
    /// happy-path transitions (Idle → Lured → Acting → Resolving → Resolved/loop), the warned-no-op on every
    /// illegal transition (never throws — NFR-3), the Suspend/Resume AR-loss path, and the decision-H2
    /// deferred-suspend-during-Resolving. Every test drives the production machine; none recompute its table.
    /// </summary>
    public sealed class EncounterStateMachineTests
    {
        // ---- AC-1: the legal happy path ----

        [Test]
        public void Happy_path_walks_Idle_to_Resolved()
        {
            var sm = new EncounterStateMachine();
            Assert.AreEqual(EncounterState.Idle, sm.State, "A new machine starts Idle.");

            Assert.IsTrue(sm.BeginLure());
            Assert.AreEqual(EncounterState.Lured, sm.State);

            Assert.IsTrue(sm.BeginAction());
            Assert.AreEqual(EncounterState.Acting, sm.State);

            Assert.IsTrue(sm.BeginResolve());
            Assert.AreEqual(EncounterState.Resolving, sm.State);

            Assert.IsTrue(sm.EndEncounter());
            Assert.AreEqual(EncounterState.Resolved, sm.State, "EndEncounter is the per-encounter terminal.");
        }

        [Test]
        public void Per_action_resolution_loops_back_to_Lured()
        {
            var sm = new EncounterStateMachine();
            sm.BeginLure();
            sm.BeginAction();
            sm.BeginResolve();

            // A committed action returns the encounter to a live, actionable state (an encounter has many
            // actions) — NOT the per-encounter terminal.
            Assert.IsTrue(sm.CompleteResolve());
            Assert.AreEqual(EncounterState.Lured, sm.State);

            // ...and the player can act again.
            Assert.IsTrue(sm.BeginAction());
            Assert.AreEqual(EncounterState.Acting, sm.State);
        }

        [Test]
        public void Failed_resolution_returns_to_Lured_for_retry()
        {
            var sm = new EncounterStateMachine();
            sm.BeginLure();
            sm.BeginAction();
            sm.BeginResolve();

            // A rolled-back action keeps the encounter live (Retry is free, FR-10).
            Assert.IsTrue(sm.FailResolve());
            Assert.AreEqual(EncounterState.Lured, sm.State);
        }

        // ---- AC-1: illegal transitions are warned no-ops, never throws ----

        [Test]
        public void Illegal_BeginAction_from_Idle_is_a_warned_no_op()
        {
            var sm = new EncounterStateMachine();

            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("illegal transition"));
            bool moved = sm.BeginAction(); // Idle → Acting skips Lured

            Assert.IsFalse(moved, "An illegal transition returns false.");
            Assert.AreEqual(EncounterState.Idle, sm.State, "State is unchanged on an illegal transition.");
        }

        [Test]
        public void Illegal_BeginResolve_from_Lured_is_a_warned_no_op()
        {
            var sm = new EncounterStateMachine();
            sm.BeginLure();

            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("illegal transition"));
            bool moved = sm.BeginResolve(); // Lured → Resolving skips Acting

            Assert.IsFalse(moved);
            Assert.AreEqual(EncounterState.Lured, sm.State);
        }

        [Test]
        public void Illegal_transitions_never_throw()
        {
            var sm = new EncounterStateMachine();
            // Fire a barrage of illegal calls from Idle; none may throw (NFR-3). Warnings are expected.
            LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(() =>
            {
                sm.BeginAction();
                sm.BeginResolve();
                sm.CompleteResolve();
                sm.EndEncounter();
                sm.Resume();
            });
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(EncounterState.Idle, sm.State);
        }

        // ---- AC-3: Suspend / Resume ----

        [Test]
        public void Suspend_from_an_active_state_then_Resume_returns_to_it()
        {
            var sm = new EncounterStateMachine();
            sm.BeginLure();
            sm.BeginAction(); // Acting

            Assert.IsTrue(sm.Suspend());
            Assert.AreEqual(EncounterState.Suspended, sm.State);

            Assert.IsTrue(sm.Resume());
            Assert.AreEqual(EncounterState.Acting, sm.State, "Resume returns to the state the encounter left.");
        }

        [Test]
        public void Suspend_from_Idle_is_a_warned_no_op()
        {
            var sm = new EncounterStateMachine();

            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("no active encounter to suspend"));
            bool suspended = sm.Suspend();

            Assert.IsFalse(suspended, "There is nothing active to suspend from Idle.");
            Assert.AreEqual(EncounterState.Idle, sm.State);
        }

        [Test]
        public void Resume_when_not_suspended_is_a_warned_no_op()
        {
            var sm = new EncounterStateMachine();
            sm.BeginLure();

            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("not suspended"));
            bool resumed = sm.Resume();

            Assert.IsFalse(resumed);
            Assert.AreEqual(EncounterState.Lured, sm.State);
        }

        // ---- decision H2: a suspend requested DURING Resolving is deferred until the write settles ----

        [Test]
        public void Suspend_during_Resolving_is_deferred_until_the_write_settles()
        {
            var sm = new EncounterStateMachine();
            sm.BeginLure();
            sm.BeginAction();
            sm.BeginResolve(); // Resolving — the atomic-write window

            // AR loss mid-write: the machine does NOT yank to Suspended immediately (it would race the
            // in-flight persist/rollback). It records the intent and stays Resolving.
            Assert.IsTrue(sm.Suspend());
            Assert.AreEqual(EncounterState.Resolving, sm.State, "Suspend during Resolving must NOT transition immediately (decision H2).");
            Assert.IsTrue(sm.SuspendRequested, "The suspend intent is recorded.");

            // When the write settles, the completion path applies the deferred suspend.
            Assert.IsTrue(sm.CompleteResolve());
            Assert.AreEqual(EncounterState.Suspended, sm.State, "The deferred suspend is honored once the write settles.");
            Assert.IsFalse(sm.SuspendRequested, "The intent is cleared once applied.");
        }

        [Test]
        public void Deferred_suspend_during_EndEncounter_resumes_to_Resolved_not_Lured()
        {
            // CR patch: the deferred-suspend resume target must derive from the resolve target, not a hardcoded
            // Lured. A suspend deferred during an EndEncounter (target Resolved) must resume to Resolved.
            var sm = new EncounterStateMachine();
            sm.BeginLure();
            sm.BeginAction();
            sm.BeginResolve(); // Resolving

            Assert.IsTrue(sm.Suspend(), "Deferred during Resolving.");
            Assert.AreEqual(EncounterState.Resolving, sm.State);

            // The write settles via EndEncounter (the per-encounter terminal, target = Resolved).
            Assert.IsTrue(sm.EndEncounter());
            Assert.AreEqual(EncounterState.Suspended, sm.State, "The deferred suspend is applied.");

            Assert.IsTrue(sm.Resume());
            Assert.AreEqual(EncounterState.Resolved, sm.State, "Resume returns to the resolve's target (Resolved), not a hardcoded Lured.");
        }

        [Test]
        public void Reset_from_any_state_returns_to_Idle_and_clears_intent()
        {
            var sm = new EncounterStateMachine();
            sm.BeginLure();
            sm.BeginAction();
            sm.BeginResolve();
            sm.Suspend(); // sets SuspendRequested while Resolving

            Assert.IsTrue(sm.Reset());
            Assert.AreEqual(EncounterState.Idle, sm.State);
            Assert.IsFalse(sm.SuspendRequested);
        }

        // ---- OnStateChanged event ----

        [Test]
        public void OnStateChanged_fires_with_the_new_state_on_each_transition()
        {
            var sm = new EncounterStateMachine();
            var seen = new List<EncounterState>();
            sm.OnStateChanged += seen.Add;

            sm.BeginLure();
            sm.BeginAction();

            CollectionAssert.AreEqual(new[] { EncounterState.Lured, EncounterState.Acting }, seen);
        }

        [Test]
        public void A_throwing_OnStateChanged_subscriber_does_not_propagate()
        {
            var sm = new EncounterStateMachine();
            sm.OnStateChanged += _ => throw new System.InvalidOperationException("subscriber boom");

            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("subscriber threw"));
            Assert.DoesNotThrow(() => sm.BeginLure(), "A throwing subscriber must not surface the committed transition as a fault.");
            Assert.AreEqual(EncounterState.Lured, sm.State);
        }
    }
}
