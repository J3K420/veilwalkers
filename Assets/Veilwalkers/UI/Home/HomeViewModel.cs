namespace Veilwalkers.UI
{
    /// <summary>
    /// The pure-logic view-model the Home surface renders (Story 6.3, AC-1). It composes the Story-6.2
    /// chunky component descriptors (wordmark, credit pill, the ENTER AR HUNT CTA) with the live
    /// service reads (Codex X/67, the daily-reward affordance) into one immutable snapshot the thin
    /// <c>HomeView</c> (deferred) binds to widgets. Built fresh by <see cref="HomePresenter.Build"/> on
    /// each bind / change — no internal mutable state.
    /// <para>
    /// The CTA / Codex / Shop / pill TAP ACTIONS are not on this model — the view wires those to the
    /// <c>AppStateMachine</c> navigation methods (the presenter exposes the intents; this model is the
    /// display data). The actual scene layout + the "floating islands, never docked" chrome is the
    /// deferred Epic-6 view layer.
    /// </para>
    /// </summary>
    public readonly struct HomeViewModel
    {
        /// <summary>The brand wordmark lockup (Story 6.2 <see cref="WordmarkStyle"/>).</summary>
        public WordmarkStyle Wordmark { get; }

        /// <summary>The credit pill showing the live balance (Story 6.2 <see cref="CreditPillStyle"/>),
        /// tappable → Shop.</summary>
        public CreditPillStyle CreditPill { get; }

        /// <summary>The primary ENTER AR HUNT CTA (Story 6.2 <see cref="ChunkyButtonStyle"/>, Primary,
        /// ALL-CAPS). Its tap routes to a cold AR entry.</summary>
        public ChunkyButtonStyle EnterArHunt { get; }

        /// <summary>The Codex entry button (secondary) — its label carries the X/67 progress.</summary>
        public ChunkyButtonStyle CodexEntry { get; }

        /// <summary>The "X / 67" Codex progress label (from <c>CodexService</c>), surfaced separately so
        /// the view can render the count distinctly from the button chrome.</summary>
        public string CodexProgressLabel { get; }

        /// <summary>The Shop entry button (secondary).</summary>
        public ChunkyButtonStyle ShopEntry { get; }

        /// <summary>Whether the daily-reward affordance is presented as claimable today (from
        /// <c>IDailyRewardService.CanClaimToday</c>). The view shows/enables the "Consult the Veil"
        /// reward control accordingly (the diegetic copy is Story 6.5).</summary>
        public bool DailyRewardAvailable { get; }

        public HomeViewModel(
            WordmarkStyle wordmark,
            CreditPillStyle creditPill,
            ChunkyButtonStyle enterArHunt,
            ChunkyButtonStyle codexEntry,
            string codexProgressLabel,
            ChunkyButtonStyle shopEntry,
            bool dailyRewardAvailable)
        {
            Wordmark = wordmark;
            CreditPill = creditPill;
            EnterArHunt = enterArHunt;
            CodexEntry = codexEntry;
            CodexProgressLabel = codexProgressLabel;
            ShopEntry = shopEntry;
            DailyRewardAvailable = dailyRewardAvailable;
        }
    }
}
