using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Veilwalkers.App;
using Veilwalkers.Core;
using Veilwalkers.Economy;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// <see cref="ArHudPresenter"/> as the pure-logic AR Hunt HUD read model (Story 6.3, AC-3). It
    /// composes the floating credit pill (live balance) + the action-bar chunky buttons (Lure / Scan /
    /// Capture / Slay) and mirrors the <see cref="AppStateMachine"/> overlay depth. The assertions are
    /// falsifiable: the action bar includes the SLAY descriptor with its mandated icon + label (UX-DR17
    /// red-never-alone), the pill shows the live balance + the tap-to-Shop affordance, and the overlay
    /// follows the state machine's one-level-deep rule.
    /// </summary>
    public sealed class ArHudPresenterTests
    {
        private sealed class FakeCreditService : ICreditService
        {
            public int Balance { get; set; }
            public Task<SpendResult> TrySpendCreditsAsync(int cost) => throw new NotSupportedException();
            public Task<Result> GrantCreditsAsync(int amount) => throw new NotSupportedException();
            public event Action<int> OnCreditsChanged;
            public event Action<InsufficientCreditsEvent> OnInsufficientCredits;
            public void Touch() { OnCreditsChanged?.Invoke(Balance); OnInsufficientCredits?.Invoke(default); }
        }

        private static AppStateMachine NewApp(out FakeCreditService credits)
        {
            credits = new FakeCreditService();
            return new AppStateMachine(credits, IEncounterSnapshotPort.NoEncounter);
        }

        // ---- AC-3: the floating chrome ----

        [Test]
        public void Build_credit_pill_shows_the_live_balance_and_taps_to_shop()
        {
            var credits = new FakeCreditService { Balance = 8 };
            var app = new AppStateMachine(credits, IEncounterSnapshotPort.NoEncounter);
            var p = new ArHudPresenter(credits, app);

            ArHudViewModel vm = p.Build();
            Assert.AreEqual(8, vm.CreditPill.Count);
            Assert.IsTrue(vm.CreditPill.TapToShop);
            app.Dispose();
        }

        [Test]
        public void Build_action_bar_includes_the_slay_descriptor_with_icon_and_label()
        {
            var app = NewApp(out var credits);
            var p = new ArHudPresenter(credits, app);

            ArHudViewModel vm = p.Build();
            bool foundSlay = false;
            foreach (ChunkyButtonStyle b in vm.ActionBar)
            {
                if (b.Kind == ChunkyButtonKind.Slay)
                {
                    foundSlay = true;
                    Assert.IsTrue(b.RequiresIcon, "SLAY must carry its icon (UX-DR17 — red never alone).");
                    Assert.AreEqual(ChunkyButtonStyle.SlayLabel, b.DisplayLabel, "SLAY shows the literal 'SLAY' label.");
                }
            }

            Assert.IsTrue(foundSlay, "The AR action bar must include a Slay affordance.");
            Assert.AreEqual(4, vm.ActionBar.Count, "Lure / Scan / Capture / Slay.");
            app.Dispose();
        }

        [Test]
        public void Build_action_bar_lure_is_primary_and_scan_capture_are_secondary()
        {
            var app = NewApp(out var credits);
            var p = new ArHudPresenter(credits, app);
            ArHudViewModel vm = p.Build();

            Assert.AreEqual(ChunkyButtonKind.Primary, vm.ActionBar[0].Kind, "Lure is the primary affordance.");
            Assert.AreEqual(ChunkyButtonKind.Secondary, vm.ActionBar[1].Kind, "Scan is secondary.");
            Assert.AreEqual(ChunkyButtonKind.Secondary, vm.ActionBar[2].Kind, "Capture is secondary.");
            app.Dispose();
        }

        [Test]
        public void Build_overlay_mirrors_the_state_machine_one_level_deep()
        {
            var app = NewApp(out var credits);
            var p = new ArHudPresenter(credits, app);

            Assert.AreEqual(OverlayLevelView.None, p.Build().Overlay);

            app.OpenSheet();
            Assert.AreEqual(OverlayLevelView.Sheet, p.Build().Overlay);

            app.OpenSheet(); // replaces, stays one level deep
            Assert.AreEqual(OverlayLevelView.Sheet, p.Build().Overlay);

            app.CloseSheet();
            Assert.AreEqual(OverlayLevelView.None, p.Build().Overlay);
            app.Dispose();
        }

        [Test]
        public void Build_degrades_when_services_are_null()
        {
            var p = new ArHudPresenter(null, null);
            ArHudViewModel vm = p.Build();
            Assert.AreEqual(0, vm.CreditPill.Count, "A null credit service shows a calm 0 pill.");
            Assert.AreEqual(OverlayLevelView.None, vm.Overlay, "A null state machine reports no overlay.");
            Assert.AreEqual(4, vm.ActionBar.Count, "The action bar still assembles.");
        }
    }
}
