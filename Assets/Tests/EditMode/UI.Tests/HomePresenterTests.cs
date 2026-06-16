using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Monsters;
using Veilwalkers.Persistence;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// <see cref="HomePresenter"/> as the pure-logic Home read model (Story 6.3, AC-1). It composes the
    /// Story-6.2 chunky descriptors (wordmark, credit pill, ENTER AR HUNT, Codex / Shop entries) with the
    /// live service reads (a REAL <see cref="CodexService"/> for the X/67, fakes for the credit balance +
    /// the daily-reward flag). The assertions are falsifiable: the X/67 ticks with REAL discoveries (not a
    /// hard-coded string), the credit pill shows the live balance, the daily-reward flag follows the
    /// service, and a before-load read / missing service degrades to a calm default instead of crashing.
    /// </summary>
    public sealed class HomePresenterTests
    {
        // ---- fakes (the presenter reads only Balance / CanClaimToday) ----

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

        private sealed class FakeDailyReward : IDailyRewardService
        {
            public bool Claimable { get; set; }
            public bool ThrowBeforeLoad { get; set; }

            public bool CanClaimToday => ThrowBeforeLoad
                ? throw new InvalidOperationException("save model not loaded")
                : Claimable;

            public Task<DailyRewardResult> TryClaimAsync() => throw new NotSupportedException();
            public event Action<int> OnDailyRewardClaimed;
            public void Touch() => OnDailyRewardClaimed?.Invoke(0);
        }

        // ---- CodexService construction (the CodexGridPresenterTests precedent) ----

        private static MonsterDefinition Def(string id, Rarity rarity)
        {
            var d = ScriptableObject.CreateInstance<MonsterDefinition>();
            d.SetForTests(id, "x", rarity, 0, 0, "x", null);
            return d;
        }

        private static MonsterDatabase Db(params MonsterDefinition[] defs)
        {
            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            db.SetForTests(defs);
            return db;
        }

        private static CodexService NewCodex(MonsterDatabase db)
        {
            var store = new FakeUiCodexProgressStore { Stored = new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            return new CodexService(save, db, new FakeClock(
                new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc)));
        }

        private static void Discover(CodexService codex, string id)
        {
            codex.RecordDiscoveryAsync(id, DiscoverySource.Capture).GetAwaiter().GetResult();
        }

        // ---- AC-1: the Home model carries every required affordance ----

        [Test]
        public void Build_carries_the_wordmark_lockup()
        {
            var p = new HomePresenter(NewCodex(Db()), new FakeCreditService(), new FakeDailyReward());
            HomeViewModel vm = p.Build();
            Assert.AreEqual(WordmarkStyle.Text, vm.Wordmark.DisplayText);
            Assert.IsTrue(vm.Wordmark.BodyTextForbidden);
        }

        [Test]
        public void Build_credit_pill_shows_the_live_balance()
        {
            var credits = new FakeCreditService { Balance = 17 };
            var p = new HomePresenter(NewCodex(Db()), credits, new FakeDailyReward());
            Assert.AreEqual(17, p.Build().CreditPill.Count);
        }

        [Test]
        public void Build_ENTER_AR_HUNT_cta_is_a_primary_all_caps_button()
        {
            var p = new HomePresenter(NewCodex(Db()), new FakeCreditService(), new FakeDailyReward());
            ChunkyButtonStyle cta = p.Build().EnterArHunt;
            Assert.AreEqual(ChunkyButtonKind.Primary, cta.Kind);
            Assert.AreEqual("ENTER AR HUNT", cta.DisplayLabel, "The CTA is ALL-CAPS (UX-DR3).");
            Assert.IsFalse(cta.RequiresIcon);
        }

        [Test]
        public void Build_Codex_progress_label_ticks_with_real_discoveries()
        {
            var codex = NewCodex(Db(Def("mon01", Rarity.Common), Def("mon02", Rarity.Common), Def("mon03", Rarity.Common)));
            var p = new HomePresenter(codex, new FakeCreditService(), new FakeDailyReward());

            Assert.AreEqual("0 / 67", p.Build().CodexProgressLabel);

            Discover(codex, "mon01");
            Discover(codex, "mon02");
            Discover(codex, "mon03");

            HomeViewModel vm = p.Build();
            Assert.AreEqual("3 / 67", vm.CodexProgressLabel, "X/67 must tick with real discoveries, not be hard-coded.");
            // The Codex entry button label embeds the count (ALL-CAPS via the descriptor): "CODEX 3 / 67".
            StringAssert.Contains("3 / 67", vm.CodexEntry.DisplayLabel);
        }

        [Test]
        public void Build_Codex_and_Shop_entries_are_secondary_buttons()
        {
            var p = new HomePresenter(NewCodex(Db()), new FakeCreditService(), new FakeDailyReward());
            HomeViewModel vm = p.Build();
            Assert.AreEqual(ChunkyButtonKind.Secondary, vm.CodexEntry.Kind);
            Assert.AreEqual(ChunkyButtonKind.Secondary, vm.ShopEntry.Kind);
            StringAssert.Contains("SHOP", vm.ShopEntry.DisplayLabel);
        }

        [Test]
        public void Build_daily_reward_affordance_follows_the_service()
        {
            var reward = new FakeDailyReward { Claimable = true };
            var p = new HomePresenter(NewCodex(Db()), new FakeCreditService(), reward);
            Assert.IsTrue(p.Build().DailyRewardAvailable);

            reward.Claimable = false;
            Assert.IsFalse(p.Build().DailyRewardAvailable, "The affordance must reflect CanClaimToday, not be constant.");
        }

        [Test]
        public void Build_daily_reward_label_is_the_single_VeilVoice_source()
        {
            // Story 6.5 single-source pin: the daily-reward control's diegetic label is VeilVoice.RewardedAction
            // ("Consult the Veil") — one source, closing the prior "diegetic copy is Story 6.5" placeholder.
            var p = new HomePresenter(NewCodex(Db()), new FakeCreditService(), new FakeDailyReward());
            // Single-source pin: the label sources VeilVoice.RewardedAction (the constant's exact wording
            // is pinned independently in VeilVoiceTests — asserting it again here would be tautological).
            Assert.AreEqual(VeilVoice.RewardedAction, p.Build().RewardControlLabel);
        }

        // ---- graceful degradation ----

        [Test]
        public void Build_degrades_when_credit_read_throws_before_load()
        {
            var credits = new FakeCreditService { Balance = 99, ThrowBeforeLoad = true };
            var p = new HomePresenter(NewCodex(Db()), credits, new FakeDailyReward());
            Assert.AreEqual(0, p.Build().CreditPill.Count, "A before-load balance read degrades to a calm 0 (the pill never locks).");
        }

        [Test]
        public void Build_degrades_when_daily_reward_read_throws_before_load()
        {
            var reward = new FakeDailyReward { Claimable = true, ThrowBeforeLoad = true };
            var p = new HomePresenter(NewCodex(Db()), new FakeCreditService(), reward);
            Assert.IsFalse(p.Build().DailyRewardAvailable, "A before-load claim read degrades to unavailable.");
        }

        [Test]
        public void Build_degrades_when_services_are_null()
        {
            var p = new HomePresenter(null, null, null);
            HomeViewModel vm = p.Build();
            Assert.AreEqual(0, vm.CreditPill.Count);
            Assert.AreEqual("0 / 67", vm.CodexProgressLabel, "A null CodexService still reports the static universe size.");
            Assert.IsFalse(vm.DailyRewardAvailable);
            Assert.AreEqual(WordmarkStyle.Text, vm.Wordmark.DisplayText, "Home always renders the wordmark.");
        }
    }
}
