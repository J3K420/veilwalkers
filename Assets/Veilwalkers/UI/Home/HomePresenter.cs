using System;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// Pure-logic read model for the Home surface (Story 6.3, AC-1). Composes the Story-6.2 chunky
    /// descriptors (wordmark, credit pill, ENTER AR HUNT, Codex / Shop entries) with the live service
    /// reads (the <c>CodexService</c> X/67, the <c>ICreditService</c> balance, the
    /// <c>IDailyRewardService</c> claimable flag) into a <see cref="HomeViewModel"/>. NO Unity types, NO
    /// <c>MonoBehaviour</c>, NO service-locator reads — ctor-injected (AR-4), headless-testable (the
    /// <see cref="CodexGridPresenter"/> precedent). The thin <c>HomeView</c> binds the model and wires
    /// the CTA/entry taps to the <c>AppStateMachine</c>; that view + the scene are the deferred Epic-6
    /// view layer.
    /// <para>
    /// <b>Degrade gracefully (the CodexGridView "inert while unregistered" precedent).</b> Any of the
    /// services may be unresolved (a deferred Bootstrap seam — e.g. CodexService is blocked on
    /// <c>MonsterDatabase.asset</c>) and the balance/claim reads THROW before the save model is loaded
    /// (the <see cref="ICreditService.Balance"/> / <see cref="IDailyRewardService.CanClaimToday"/>
    /// before-load contract). <see cref="Build"/> therefore never lets a missing service or a
    /// before-load read crash Home: a null service or a thrown read degrades to its calm default (0
    /// credits, <c>0 / 67</c>, no reward) so Home always renders.
    /// </para>
    /// </summary>
    public sealed class HomePresenter
    {
        /// <summary>The label the ENTER AR HUNT CTA carries before the descriptor ALL-CAPS-es it.</summary>
        public const string EnterArHuntLabel = "Enter AR Hunt";

        /// <summary>The Codex entry label prefix; the X/67 count is appended at build time.</summary>
        public const string CodexLabel = "Codex";

        /// <summary>The Shop entry label.</summary>
        public const string ShopLabel = "Shop";

        private readonly CodexService _codex;            // may be null (deferred seam)
        private readonly ICreditService _credits;        // may be null (defensive)
        private readonly IDailyRewardService _dailyReward; // may be null (defensive)

        public HomePresenter(CodexService codex, ICreditService credits, IDailyRewardService dailyReward)
        {
            // All optional: Home must render even while a service is an unregistered Bootstrap seam.
            _codex = codex;
            _credits = credits;
            _dailyReward = dailyReward;
        }

        /// <summary>
        /// Build the immutable Home view-model from current state. A PURE RECOMPUTE — reads live values
        /// and returns a fresh model every call; the view rebuilds on <c>OnCreditsChanged</c> /
        /// <c>OnMonsterDiscovered</c> / <c>OnDailyRewardClaimed</c>. Never throws: a missing service or a
        /// before-load read degrades to its calm default.
        /// </summary>
        public HomeViewModel Build()
        {
            int balance = ReadBalance();
            int discovered = ReadDiscoveredCount();
            int universe = ReadUniverseCount();
            bool rewardAvailable = ReadDailyRewardAvailable();

            string codexProgress = $"{discovered} / {universe}";

            return new HomeViewModel(
                wordmark: WordmarkStyle.Create(),
                creditPill: CreditPillStyle.For(balance),
                enterArHunt: ChunkyButtonStyle.For(ChunkyButtonKind.Primary, EnterArHuntLabel),
                codexEntry: ChunkyButtonStyle.For(ChunkyButtonKind.Secondary, $"{CodexLabel} {codexProgress}"),
                codexProgressLabel: codexProgress,
                shopEntry: ChunkyButtonStyle.For(ChunkyButtonKind.Secondary, ShopLabel),
                dailyRewardAvailable: rewardAvailable);
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
                // Before-load contract: balance is unreadable until the save model loads. Home renders
                // a calm "0" (the credit pill never locks — UX-DR4) until the first OnCreditsChanged.
                return 0;
            }
        }

        private int ReadDiscoveredCount()
        {
            if (_codex == null)
            {
                return 0;
            }

            try
            {
                return _codex.DiscoveredCount;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }

        private int ReadUniverseCount()
        {
            // UniverseCount is the static 67 (no save dependency), but guard the null seam regardless.
            if (_codex == null)
            {
                return MonsterDatabase.UniverseCount;
            }

            return _codex.UniverseCount;
        }

        private bool ReadDailyRewardAvailable()
        {
            if (_dailyReward == null)
            {
                return false;
            }

            try
            {
                return _dailyReward.CanClaimToday;
            }
            catch (InvalidOperationException)
            {
                // Before-load: not yet claimable-known → present as unavailable until the model loads.
                return false;
            }
        }
    }
}
