using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Veilwalkers.App;
using Veilwalkers.Core;
using Veilwalkers.Economy;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// <see cref="OnboardingFlow"/> as the pure-logic first-launch step state machine (Story 6.4). The
    /// assertions are falsifiable: the only legal order is Premise → Disclosure → Grant → Complete
    /// (out-of-order advances are rejected with NO state change); all premise cards are WALKED before
    /// Disclosure (not a hard-coded count); the disclosure gate fires exactly when the disclosure card is
    /// acknowledged; and the terminal step drives the Story-6.3 <see cref="AppStateMachine.CompleteOnboarding"/>
    /// gate exactly once. The AC-4 composition pin proves AR is unreachable until the flow completes.
    /// </summary>
    public sealed class OnboardingFlowTests
    {
        // ---- fakes ----

        // Records whether the OS-prompt request was fired (and how many times).
        private sealed class FakeDisclosureGate : IDisclosureGate
        {
            public int ConfirmCalls { get; private set; }
            public void ConfirmDisclosureAndRequest() => ConfirmCalls++;
        }

        private sealed class FakePrewarm : IArPrewarmPort
        {
            public int PrewarmCalls;
            public Task PrewarmAsync()
            {
                PrewarmCalls++;
                return Task.CompletedTask;
            }
        }

        // A misbehaving disclosure gate that violates its "never throws" contract — the flow must still
        // be graceful (CR defense-in-depth at the seam).
        private sealed class ThrowingDisclosureGate : IDisclosureGate
        {
            public void ConfirmDisclosureAndRequest() => throw new InvalidOperationException("adapter fault");
        }

        // Minimal ICreditService: AppStateMachine only subscribes to OnInsufficientCredits in its ctor.
        private sealed class FakeCreditService : ICreditService
        {
            public int Balance => 0;
            public Task<SpendResult> TrySpendCreditsAsync(int cost) => throw new NotSupportedException();
            public Task<Result> GrantCreditsAsync(int amount) => throw new NotSupportedException();
            public event Action<int> OnCreditsChanged;
            public event Action<InsufficientCreditsEvent> OnInsufficientCredits;
            public void Touch() { OnCreditsChanged?.Invoke(0); OnInsufficientCredits?.Invoke(default); }
        }

        private static AppStateMachine NewAppState() =>
            new AppStateMachine(new FakeCreditService(), IEncounterSnapshotPort.NoEncounter);

        private static OnboardingFlow NewFlow(
            AppStateMachine app = null, FakeDisclosureGate gate = null, FakePrewarm prewarm = null) =>
            new OnboardingFlow(app ?? NewAppState(), gate, prewarm);

        // Drive the flow through every premise card to the Disclosure step.
        private static void WalkPremise(OnboardingFlow flow)
        {
            for (int i = 0; i < OnboardingFlow.PremiseCardCount; i++)
            {
                Assert.IsTrue(flow.AdvancePremise(), $"premise advance {i} should succeed");
            }
        }

        // ---- enum contract ----

        [Test]
        public void OnboardingStep_has_exactly_the_four_phases_in_order()
        {
            var values = (OnboardingStep[])Enum.GetValues(typeof(OnboardingStep));
            Assert.AreEqual(4, values.Length, "Adding/removing a phase must update this pin + the flow.");
            Assert.AreEqual(OnboardingStep.Premise, values[0]);
            Assert.AreEqual(OnboardingStep.Disclosure, values[1]);
            Assert.AreEqual(OnboardingStep.Grant, values[2]);
            Assert.AreEqual(OnboardingStep.Complete, values[3]);
        }

        // ---- AC-1: the premise walk ----

        [Test]
        public void Flow_starts_on_the_first_premise_card()
        {
            var flow = NewFlow();
            Assert.AreEqual(OnboardingStep.Premise, flow.Step);
            Assert.AreEqual(0, flow.PremiseCardIndex);
        }

        [Test]
        public void All_premise_cards_are_walked_before_reaching_disclosure()
        {
            var flow = NewFlow();

            // Every advance EXCEPT the last stays on Premise and increments the card index.
            for (int i = 0; i < OnboardingFlow.PremiseCardCount - 1; i++)
            {
                Assert.IsTrue(flow.AdvancePremise());
                Assert.AreEqual(OnboardingStep.Premise, flow.Step,
                    "Disclosure must be reached after the LAST card, not the first.");
                Assert.AreEqual(i + 1, flow.PremiseCardIndex);
            }

            // The last advance crosses into Disclosure.
            Assert.IsTrue(flow.AdvancePremise());
            Assert.AreEqual(OnboardingStep.Disclosure, flow.Step);
        }

        // ---- AC-2 / ordering: disclosure precedes grant ----

        [Test]
        public void Disclosure_acknowledgement_fires_the_os_prompt_then_advances_to_grant()
        {
            var gate = new FakeDisclosureGate();
            var flow = NewFlow(gate: gate);
            WalkPremise(flow);
            Assert.AreEqual(OnboardingStep.Disclosure, flow.Step);
            Assert.AreEqual(0, gate.ConfirmCalls, "The OS prompt must NOT fire before the disclosure card is acknowledged.");

            Assert.IsTrue(flow.AdvanceFromDisclosure());
            Assert.AreEqual(1, gate.ConfirmCalls, "Acknowledging the disclosure card fires the OS prompt exactly once.");
            Assert.AreEqual(OnboardingStep.Grant, flow.Step);
        }

        [Test]
        public void Grant_cannot_be_reached_without_disclosing_first()
        {
            var flow = NewFlow();
            WalkPremise(flow); // now on Disclosure

            // Attempting the grant advance while still on Disclosure is rejected — you must disclose first.
            // (AdvanceFromGrant only works from Grant; here Step is Disclosure.)
            Assert.IsFalse(flow.AdvanceFromGrant(), "Cannot complete the grant step before disclosure is acknowledged.");
            Assert.AreEqual(OnboardingStep.Disclosure, flow.Step, "Rejected advance leaves the step unchanged.");
        }

        // ---- AC-3: completion drives the 6.3 gate exactly once ----

        [Test]
        public void Completing_the_grant_step_calls_CompleteOnboarding_exactly_once_and_lands_on_Home()
        {
            var app = NewAppState();
            var flow = NewFlow(app: app);
            WalkPremise(flow);
            Assert.IsTrue(flow.AdvanceFromDisclosure()); // → Grant

            Assert.AreEqual(AppSurface.Onboarding, app.Current, "Still onboarding before the grant step completes.");

            Assert.IsTrue(flow.AdvanceFromGrant()); // → Complete
            Assert.AreEqual(OnboardingStep.Complete, flow.Step);
            Assert.AreEqual(1, flow.CompleteOnboardingCalls, "CompleteOnboarding must be called exactly once.");
            Assert.IsTrue(app.OnboardingComplete);
            Assert.AreEqual(AppSurface.Home, app.Current, "Completing onboarding lands the player on Home.");
        }

        // ---- ordering: out-of-order advances are rejected, no state change ----

        [Test]
        public void Premise_advance_is_rejected_once_past_the_premise_step()
        {
            var flow = NewFlow();
            WalkPremise(flow); // → Disclosure
            Assert.IsFalse(flow.AdvancePremise(), "Premise advance is invalid once past the Premise step.");
            Assert.AreEqual(OnboardingStep.Disclosure, flow.Step);
        }

        [Test]
        public void Disclosure_advance_is_rejected_from_the_premise_step()
        {
            var gate = new FakeDisclosureGate();
            var flow = NewFlow(gate: gate);
            Assert.IsFalse(flow.AdvanceFromDisclosure(), "Cannot acknowledge disclosure while still on a premise card.");
            Assert.AreEqual(OnboardingStep.Premise, flow.Step);
            Assert.AreEqual(0, gate.ConfirmCalls, "No OS prompt fires from an out-of-order disclosure advance.");
        }

        [Test]
        public void Grant_advance_is_rejected_from_the_complete_step()
        {
            var app = NewAppState();
            var flow = NewFlow(app: app);
            WalkPremise(flow);
            flow.AdvanceFromDisclosure();
            flow.AdvanceFromGrant(); // → Complete (CompleteOnboarding called once)

            Assert.IsFalse(flow.AdvanceFromGrant(), "Cannot re-complete from the terminal step.");
            Assert.AreEqual(1, flow.CompleteOnboardingCalls, "CompleteOnboarding is not called a second time.");
        }

        // ---- prewarm ----

        [Test]
        public void BeginPrewarm_kicks_the_prewarm_port_at_most_once()
        {
            var prewarm = new FakePrewarm();
            var flow = NewFlow(prewarm: prewarm);

            flow.BeginPrewarm();
            flow.BeginPrewarm(); // idempotent

            // Fire-and-forget on the default scheduler — give the kicked task a moment to run.
            SpinUntil(() => prewarm.PrewarmCalls >= 1);
            Assert.AreEqual(1, prewarm.PrewarmCalls, "Prewarm is kicked exactly once even if BeginPrewarm is called twice.");
        }

        [Test]
        public void Null_ports_degrade_to_no_ops_and_the_flow_still_completes()
        {
            var app = NewAppState();
            var flow = new OnboardingFlow(app, null, null); // ports default to the No* graceful seams
            flow.BeginPrewarm(); // must not throw with a null prewarm
            WalkPremise(flow);
            Assert.IsTrue(flow.AdvanceFromDisclosure()); // null disclosure gate → no-op, still advances
            Assert.IsTrue(flow.AdvanceFromGrant());
            Assert.AreEqual(AppSurface.Home, app.Current);
        }

        // ---- AC-4 composition pin: AR unreachable until onboarding completes ----

        [Test]
        public void AR_is_unreachable_until_the_onboarding_flow_completes()
        {
            var app = NewAppState();
            var flow = NewFlow(app: app);

            // Onboarding incomplete: a cold AR entry is rejected and the player stays on Onboarding.
            Assert.IsFalse(app.EnterArHuntAsync(ArEntryKind.ColdEntry).GetAwaiter().GetResult(),
                "AR Mode must not be reachable while onboarding is incomplete.");
            Assert.AreEqual(AppSurface.Onboarding, app.Current);

            // Drive the flow to completion.
            WalkPremise(flow);
            flow.AdvanceFromDisclosure();
            flow.AdvanceFromGrant();
            Assert.AreEqual(AppSurface.Home, app.Current);

            // Now a cold AR entry from Home is accepted (the gate flipped only after Complete).
            Assert.IsTrue(app.EnterArHuntAsync(ArEntryKind.ColdEntry).GetAwaiter().GetResult(),
                "After onboarding completes, the cold AR entry from Home is accepted.");
            Assert.AreEqual(AppSurface.ArHunt, app.Current);
        }

        // ---- CR coverage pins ----

        // Patch 6 (the AC-violation root fix): the flow's walk length is the SAME source as the
        // presenter's rendered cards, so they can never desync (the flow can't walk an index the
        // presenter never built). This pin fails the instant the two sources diverge.
        [Test]
        public void Premise_card_count_is_the_single_source_shared_with_the_presenter()
        {
            Assert.AreEqual(OnboardingPresenter.PremiseBeats.Count, OnboardingFlow.PremiseCardCount,
                "The flow's premise walk length MUST equal the presenter's beat count (one source of truth).");
            // And the walk actually visits exactly that many cards before Disclosure.
            var flow = NewFlow();
            int advances = 0;
            while (flow.Step == OnboardingStep.Premise && advances < 99)
            {
                flow.AdvancePremise();
                advances++;
            }

            Assert.AreEqual(OnboardingPresenter.PremiseBeats.Count, advances,
                "Walking the premise cards takes exactly PremiseBeats.Count advances to reach Disclosure.");
        }

        // Patches 1+2 (defense-in-depth): a misbehaving disclosure gate that throws must NOT wedge the
        // flow — it logs + still advances to Grant (the flow's own "never throws" contract holds).
        [Test]
        public void A_throwing_disclosure_gate_does_not_wedge_the_flow()
        {
            var flow = new OnboardingFlow(NewAppState(), new ThrowingDisclosureGate(), null);
            WalkPremise(flow); // → Disclosure
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("disclosure gate faulted"));
            Assert.DoesNotThrow(() => flow.AdvanceFromDisclosure(),
                "A faulting disclosure gate must not propagate out of the onboarding flow.");
            Assert.AreEqual(OnboardingStep.Grant, flow.Step, "The flow advances to Grant despite the gate fault.");
        }

        // A bounded spin for the fire-and-forget prewarm continuation (no Thread.Sleep loop abuse).
        private static void SpinUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                System.Threading.Thread.Yield();
            }
        }
    }
}
