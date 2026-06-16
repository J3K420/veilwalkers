using System;
using System.Collections.Generic;
using Veilwalkers.App;
using Veilwalkers.Core;
using Veilwalkers.Economy;

namespace Veilwalkers.UI
{
    /// <summary>
    /// Pure-logic read model for the first-launch onboarding shell (Story 6.4). Composes the Story-6.2
    /// chunky descriptors — the 2–3 premise cards (AC-1), the camera-disclosure card (AC-2), the
    /// 20-Credit coin-burst (AC-3), the ENTER AR HUNT CTA — into an <see cref="OnboardingViewModel"/> for
    /// the current <see cref="OnboardingFlow.Step"/>. NO Unity types, NO <c>MonoBehaviour</c>, NO
    /// service-locator reads — ctor-injected (AR-4), headless-testable (the <c>HomePresenter</c>
    /// precedent). The thin <c>OnboardingView</c> binds the model + wires the advance/CTA taps to the
    /// <see cref="OnboardingFlow"/> / <c>AppStateMachine</c>; that view + the scene + the coin/card
    /// animations are the deferred Story-6.5 view layer.
    /// <para>
    /// <b>Degrade gracefully (the <c>HomePresenter</c> "render even while a service is an unregistered
    /// seam" precedent).</b> The credit read can THROW before the save model loads (the
    /// <see cref="ICreditService.Balance"/> before-load contract) and the service may be null; the
    /// coin-burst then presents a calm 0 rather than crashing onboarding. The grant itself is performed
    /// by Story-1.7 <c>FirstLaunchGrant</c> at boot — this presenter only READS the resulting balance.
    /// </para>
    /// <para>
    /// <b>Disclosure copy is plain (AC-2 / the safety-copy exception).</b> The disclosure card uses the
    /// warm-cream <c>TextPrimary</c> body on the Surface fill — legible, high-contrast, never muted text
    /// (the contrast rule, anticipating 6.6). The premise cards likewise use the frame palette, never a
    /// tier color (6.1 AC-4).
    /// </para>
    /// </summary>
    public sealed class OnboardingPresenter
    {
        /// <summary>The premise beats (Story 6.4, AC-1; Decision B = 3). The Veil is real / monsters are
        /// real / you're a Veilwalker. Provisional copy (a 6.5 content pass may refine it).</summary>
        public static readonly IReadOnlyList<string> PremiseBeats = new[]
        {
            "The Veil is real.",
            "The monsters are real.",
            "You're a Veilwalker.",
        };

        /// <summary>The advance-CTA label on the premise cards (the last card reads as the commitment).</summary>
        public const string PremiseAdvanceLabel = "Next";
        public const string PremiseFinalAdvanceLabel = "I'm a Veilwalker";

        /// <summary>The camera-disclosure card copy — plain, legible, NOT diegetic flavor (AC-2 / FR-5).
        /// Explains the camera use before the OS dialog.</summary>
        public const string DisclosureBody =
            "Veilwalkers uses your camera to reveal and hunt monsters anchored in the world around you. " +
            "We'll ask your permission next.";

        /// <summary>The disclosure card's Continue CTA label.</summary>
        public const string DisclosureContinueLabel = "Continue";

        /// <summary>The ENTER AR HUNT CTA label (ALL-CAPS via the descriptor) — mirrors the Home CTA.</summary>
        public const string EnterArHuntLabel = "Enter AR Hunt";

        private readonly OnboardingFlow _flow;
        private readonly ICreditService _credits; // may be null / throw before load

        public OnboardingPresenter(OnboardingFlow flow, ICreditService credits)
        {
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _credits = credits; // optional: onboarding must render even before the credit service loads
        }

        /// <summary>
        /// Build the immutable onboarding view-model for the current step. A PURE RECOMPUTE — reads the
        /// flow step + the live balance and returns a fresh model every call; the view rebuilds on each
        /// advance. Never throws: a before-load credit read degrades to a calm 0.
        /// </summary>
        public OnboardingViewModel Build()
        {
            int balance = ReadBalance();

            return new OnboardingViewModel(
                step: _flow.Step,
                premiseCards: BuildPremiseCards(),
                activePremiseIndex: _flow.PremiseCardIndex,
                disclosureCard: PremiseCardStyle.For(DisclosureBody, DisclosureContinueLabel),
                // The coin-burst presents the canon 20-Credit grant settling into the live balance.
                coinBurst: CoinBurstStyle.For(FirstLaunchGrant.StartingCredits, balance),
                enterArHunt: ChunkyButtonStyle.For(ChunkyButtonKind.Primary, EnterArHuntLabel));
        }

        private static IReadOnlyList<PremiseCardStyle> BuildPremiseCards()
        {
            var cards = new PremiseCardStyle[PremiseBeats.Count];
            for (int i = 0; i < PremiseBeats.Count; i++)
            {
                bool isLast = i == PremiseBeats.Count - 1;
                cards[i] = PremiseCardStyle.For(
                    PremiseBeats[i],
                    isLast ? PremiseFinalAdvanceLabel : PremiseAdvanceLabel);
            }

            return cards;
        }

        private int ReadBalance()
        {
            if (_credits == null)
            {
                return 0;
            }

            try
            {
                return _credits.Balance;
            }
            catch (InvalidOperationException)
            {
                // Before-load contract: balance is unreadable until the save model loads. The coin-burst
                // presents a calm 0 until the first read succeeds (onboarding never crashes — NFR-3).
                return 0;
            }
        }
    }
}
