using System.Collections.Generic;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The pure-logic view-model the onboarding shell renders (Story 6.4). Built fresh by
    /// <see cref="OnboardingPresenter.Build"/> for the current <see cref="OnboardingStep"/> — it composes
    /// the Story-6.2 chunky descriptors (premise cards, the disclosure card, the coin-burst, the ENTER
    /// AR HUNT CTA) into one immutable snapshot the thin deferred <c>OnboardingView</c> binds to widgets.
    /// No internal mutable state; the step-specific affordances are surfaced together so the view shows
    /// the one matching <see cref="Step"/>.
    /// <para>
    /// The CTA / Continue / advance TAP ACTIONS are not on this model — the view wires those to
    /// <see cref="OnboardingFlow"/> (advance) and <c>AppStateMachine.EnterArHuntAsync</c> (the ENTER AR
    /// HUNT CTA at <see cref="OnboardingStep.Complete"/>). This model is the display data only; the
    /// scene layout + the coin-burst/card animations are the deferred Story-6.5 view layer.
    /// </para>
    /// </summary>
    public readonly struct OnboardingViewModel
    {
        /// <summary>The current onboarding phase the view renders.</summary>
        public OnboardingStep Step { get; }

        /// <summary>The full ordered set of premise cards (Story 6.4, AC-1). The view shows the card at
        /// <see cref="ActivePremiseIndex"/> while <see cref="Step"/> is <see cref="OnboardingStep.Premise"/>.</summary>
        public IReadOnlyList<PremiseCardStyle> PremiseCards { get; }

        /// <summary>The premise card currently shown (0-based), while on the Premise step.</summary>
        public int ActivePremiseIndex { get; }

        /// <summary>The camera-disclosure card chrome (Story 6.4, AC-2). Surfaced when
        /// <see cref="Step"/> is <see cref="OnboardingStep.Disclosure"/>. Plain, high-contrast (NOT a
        /// flavor card) — the disclosure-copy exception.</summary>
        public PremiseCardStyle DisclosureCard { get; }

        /// <summary>The 20-Credit coin-burst presentation (Story 6.4, AC-3). Surfaced when
        /// <see cref="Step"/> is <see cref="OnboardingStep.Grant"/>.</summary>
        public CoinBurstStyle CoinBurst { get; }

        /// <summary>The primary ENTER AR HUNT CTA (ALL-CAPS). Surfaced when <see cref="Step"/> is
        /// <see cref="OnboardingStep.Complete"/>; its tap routes to a cold AR entry from Home.</summary>
        public ChunkyButtonStyle EnterArHunt { get; }

        public OnboardingViewModel(
            OnboardingStep step,
            IReadOnlyList<PremiseCardStyle> premiseCards,
            int activePremiseIndex,
            PremiseCardStyle disclosureCard,
            CoinBurstStyle coinBurst,
            ChunkyButtonStyle enterArHunt)
        {
            Step = step;
            PremiseCards = premiseCards;
            ActivePremiseIndex = activePremiseIndex;
            DisclosureCard = disclosureCard;
            CoinBurst = coinBurst;
            EnterArHunt = enterArHunt;
        }
    }
}
