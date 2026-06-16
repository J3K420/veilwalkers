using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Veilwalkers.App;
using Veilwalkers.Core;
using Veilwalkers.Economy;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// <see cref="OnboardingPresenter"/> as the pure-logic onboarding read model (Story 6.4). The
    /// assertions are falsifiable: the premise cards source the 6.1 tokens (Surface fill + the hard-shadow
    /// blur==0 elevation invariant), the disclosure card carries PLAIN high-contrast copy (TextPrimary,
    /// not muted), the coin-burst presents the canon 20-Credit grant settling into the LIVE balance (ticks
    /// with the fake, not hard-coded), the ENTER AR HUNT CTA is a primary ALL-CAPS button, and a
    /// before-load / null credit read degrades to a calm 0 rather than crashing onboarding.
    /// </summary>
    public sealed class OnboardingPresenterTests
    {
        private sealed class FakeCreditService : ICreditService
        {
            public int Balance { get; set; }
            public bool ThrowBeforeLoad { get; set; }

            int ICreditService.Balance => ThrowBeforeLoad
                ? throw new InvalidOperationException("save model not loaded")
                : Balance;

            public Task<SpendResult> TrySpendCreditsAsync(int cost) => throw new NotSupportedException();
            public Task<Result> GrantCreditsAsync(int amount) => throw new NotSupportedException();
            public event Action<int> OnCreditsChanged;
            public event Action<InsufficientCreditsEvent> OnInsufficientCredits;
            public void Touch() { OnCreditsChanged?.Invoke(Balance); OnInsufficientCredits?.Invoke(default); }
        }

        private static AppStateMachine NewAppState() =>
            new AppStateMachine(new FakeCreditService(), IEncounterSnapshotPort.NoEncounter);

        private static OnboardingFlow NewFlow(AppStateMachine app = null) =>
            new OnboardingFlow(app ?? NewAppState(), null, null);

        private static OnboardingPresenter NewPresenter(
            OnboardingFlow flow = null, ICreditService credits = null) =>
            new OnboardingPresenter(flow ?? NewFlow(), credits ?? new FakeCreditService());

        // ---- AC-1: premise cards ----

        [Test]
        public void Build_carries_two_to_three_premise_cards()
        {
            OnboardingViewModel vm = NewPresenter().Build();
            Assert.GreaterOrEqual(vm.PremiseCards.Count, 2, "AC-1 requires 2–3 premise cards.");
            Assert.LessOrEqual(vm.PremiseCards.Count, 3);
        }

        [Test]
        public void Build_premise_cards_source_the_6_1_tokens_with_the_hard_shadow_device()
        {
            OnboardingViewModel vm = NewPresenter().Build();
            foreach (PremiseCardStyle card in vm.PremiseCards)
            {
                AssertColor(PumpkinPatchTokens.Surface, card.Style.Fill, "Premise card fill is the Surface token (chrome, never a tier color).");
                Assert.AreEqual(0, card.Style.ShadowBlur, "The hard-shadow elevation device has blur==0 (UX-DR9).");
                Assert.IsTrue(card.Style.HasHardShadow, "The card uses the hard offset shadow elevation device.");
                AssertColor(PumpkinPatchTokens.TextPrimary, card.BodyColor, "Premise body is warm-cream TextPrimary, not muted.");
                Assert.AreEqual(ChunkyButtonKind.Primary, card.Advance.Kind, "The advance CTA is a primary chunky button.");
            }
        }

        [Test]
        public void Build_active_premise_index_follows_the_flow()
        {
            var flow = NewFlow();
            flow.AdvancePremise();
            var p = NewPresenter(flow);
            Assert.AreEqual(1, p.Build().ActivePremiseIndex, "The active card index reflects the flow's position.");
        }

        // ---- AC-2: the disclosure card is plain + high-contrast ----

        [Test]
        public void Build_disclosure_card_uses_plain_high_contrast_copy()
        {
            OnboardingViewModel vm = NewPresenter().Build();
            PremiseCardStyle disclosure = vm.DisclosureCard;
            AssertColor(PumpkinPatchTokens.TextPrimary, disclosure.BodyColor,
                "Disclosure copy must be plain high-contrast TextPrimary, never muted (the safety-copy exception).");
            Assert.IsFalse(string.IsNullOrEmpty(disclosure.Body), "The disclosure card explains the camera use.");
            AssertColor(PumpkinPatchTokens.Surface, disclosure.Style.Fill, "Disclosure card fill is the Surface token.");
        }

        // ---- AC-3: the coin-burst presents the grant + the live balance ----

        [Test]
        public void Build_coin_burst_presents_the_canon_20_credit_grant()
        {
            OnboardingViewModel vm = NewPresenter().Build();
            Assert.AreEqual(FirstLaunchGrant.StartingCredits, vm.CoinBurst.Amount, "The coin-burst celebrates the canon 20-Credit grant.");
            Assert.AreEqual(20, vm.CoinBurst.Amount, "The canon first-launch grant is 20 Credits.");
            AssertColor(PumpkinPatchTokens.CreditGold, vm.CoinBurst.BurstColor, "The burst uses the credit-gold currency token.");
        }

        [Test]
        public void Build_coin_burst_pill_ticks_with_the_live_balance()
        {
            var p20 = NewPresenter(credits: new FakeCreditService { Balance = 20 });
            Assert.AreEqual(20, p20.Build().CoinBurst.Pill.Count, "The settled pill shows the live granted balance (20).");

            var p50 = NewPresenter(credits: new FakeCreditService { Balance = 50 });
            Assert.AreEqual(50, p50.Build().CoinBurst.Pill.Count, "The pill ticks with the balance, not a hard-coded literal.");
        }

        // ---- AC-3: the ENTER AR HUNT CTA ----

        [Test]
        public void Build_enter_ar_hunt_cta_is_a_primary_all_caps_button()
        {
            ChunkyButtonStyle cta = NewPresenter().Build().EnterArHunt;
            Assert.AreEqual(ChunkyButtonKind.Primary, cta.Kind);
            Assert.AreEqual("ENTER AR HUNT", cta.DisplayLabel, "The CTA is ALL-CAPS (UX-DR3).");
            Assert.IsFalse(cta.RequiresIcon);
        }

        [Test]
        public void Build_step_follows_the_flow()
        {
            var flow = NewFlow();
            var p = NewPresenter(flow);
            Assert.AreEqual(OnboardingStep.Premise, p.Build().Step);

            // Walk to Disclosure.
            for (int i = 0; i < OnboardingFlow.PremiseCardCount; i++)
            {
                flow.AdvancePremise();
            }

            Assert.AreEqual(OnboardingStep.Disclosure, p.Build().Step, "The view-model step mirrors the flow.");
        }

        // ---- graceful degradation ----

        [Test]
        public void Build_degrades_when_credit_read_throws_before_load()
        {
            var p = NewPresenter(credits: new FakeCreditService { Balance = 99, ThrowBeforeLoad = true });
            Assert.AreEqual(0, p.Build().CoinBurst.Pill.Count,
                "A before-load balance read degrades to a calm 0 (onboarding never crashes).");
        }

        [Test]
        public void Build_degrades_when_credit_service_is_null()
        {
            var p = new OnboardingPresenter(NewFlow(), null);
            OnboardingViewModel vm = p.Build();
            Assert.AreEqual(0, vm.CoinBurst.Pill.Count, "A null credit service still presents a calm 0 pill.");
            Assert.GreaterOrEqual(vm.PremiseCards.Count, 2, "Onboarding always renders the premise cards.");
        }

        [Test]
        public void Constructor_requires_a_flow()
        {
            Assert.Throws<ArgumentNullException>(() => new OnboardingPresenter(null, new FakeCreditService()));
        }

        private static void AssertColor(Color32 expected, Color32 actual, string because)
        {
            Assert.AreEqual(expected.r, actual.r, because + " (r)");
            Assert.AreEqual(expected.g, actual.g, because + " (g)");
            Assert.AreEqual(expected.b, actual.b, because + " (b)");
            Assert.AreEqual(expected.a, actual.a, because + " (a)");
        }
    }
}
