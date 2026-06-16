using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Veilwalkers.App;
using Veilwalkers.Core;
using Veilwalkers.Economy;

namespace Veilwalkers.App.Tests
{
    /// <summary>
    /// <see cref="AppStateMachine"/> as the pure-logic surface-flow + insufficient-credits arbiter
    /// (Story 6.3; architecture.md:462-464, 478-486, 524-525). Every test drives the production state
    /// machine over fakes (a fake <see cref="ICreditService"/>, a recording
    /// <see cref="IEncounterSnapshotPort"/>, a fake <see cref="IInsufficientCreditsSource"/>); the
    /// assertions are falsifiable (a rejected transition leaves <c>Current</c> unchanged AND raises no
    /// event; the snapshot is recorded BEFORE the Shop commit; a disposed handler does not navigate).
    /// </summary>
    public sealed class AppStateMachineTests
    {
        // ---- fakes ----

        private sealed class FakeCreditService : ICreditService
        {
            public int Balance { get; set; }
            public Task<SpendResult> TrySpendCreditsAsync(int cost) => throw new NotSupportedException();
            public Task<Result> GrantCreditsAsync(int amount) => throw new NotSupportedException();
            public event Action<int> OnCreditsChanged;
            public event Action<InsufficientCreditsEvent> OnInsufficientCredits;

            public void RaiseShortfall(int cost, int balance)
            {
                OnInsufficientCredits?.Invoke(new InsufficientCreditsEvent(cost, balance));
            }

            public bool HasShortfallSubscriber => OnInsufficientCredits != null;
            public void TouchCreditsChanged() => OnCreditsChanged?.Invoke(Balance);
        }

        private sealed class FakeEncounterShortfall : IInsufficientCreditsSource
        {
            public event Action<InsufficientCreditsEvent> OnInsufficientCredits;
            public void RaiseShortfall(int cost, int balance)
            {
                OnInsufficientCredits?.Invoke(new InsufficientCreditsEvent(cost, balance));
            }

            public bool HasSubscriber => OnInsufficientCredits != null;
        }

        // Records the ORDER of snapshot/rehydrate vs the surface commit, so the "snapshot before the
        // Shop transition commits" ordering is a real (falsifiable) assertion. Thread-safe + signals
        // snapshot completion so the shortfall path's fire-and-forget snapshot is awaitable in tests.
        private sealed class RecordingEncounterPort : IEncounterSnapshotPort
        {
            private readonly object _gate = new object();
            private readonly ManualResetEventSlim _snapshotDone = new ManualResetEventSlim(false);

            public bool HasActiveEncounter { get; set; }
            public readonly List<string> Calls = new List<string>();
            public int SnapshotCount { get; private set; }
            public int RehydrateCount { get; private set; }

            public Task<bool> SnapshotActiveEncounter()
            {
                lock (_gate)
                {
                    SnapshotCount++;
                    Calls.Add("snapshot");
                }

                _snapshotDone.Set();
                return Task.FromResult(true);
            }

            public Task<bool> RehydrateFromSnapshot()
            {
                lock (_gate)
                {
                    RehydrateCount++;
                    Calls.Add("rehydrate");
                }

                return Task.FromResult(true);
            }

            // Wait for the (possibly off-thread) shortfall snapshot to land; returns false on timeout.
            public bool WaitForSnapshot(int ms = 2000) => _snapshotDone.Wait(ms);
            public string[] CallsSnapshot()
            {
                lock (_gate) { return Calls.ToArray(); }
            }
        }

        private static AppStateMachine Create(
            out FakeCreditService credits,
            out RecordingEncounterPort encounter,
            FakeEncounterShortfall encounterShortfall = null)
        {
            credits = new FakeCreditService();
            encounter = new RecordingEncounterPort();
            return new AppStateMachine(credits, encounter, encounterShortfall);
        }

        // Drives a machine from the Onboarding start to a target surface via the legal path, recording
        // the surface-changed events along the way.
        private static List<AppSurface> Track(AppStateMachine sm)
        {
            var seen = new List<AppSurface>();
            sm.OnSurfaceChanged += seen.Add;
            return seen;
        }

        // ---- ctor ----

        [Test]
        public void Ctor_null_credits_throws()
        {
            var enc = new RecordingEncounterPort();
            Assert.Throws<ArgumentNullException>(() => new AppStateMachine(null, enc));
        }

        [Test]
        public void Ctor_null_encounter_port_throws()
        {
            var credits = new FakeCreditService();
            Assert.Throws<ArgumentNullException>(() => new AppStateMachine(credits, null));
        }

        [Test]
        public void Ctor_subscribes_to_the_credit_shortfall_event()
        {
            var sm = Create(out var credits, out _);
            Assert.IsTrue(credits.HasShortfallSubscriber,
                "The ctor must subscribe to ICreditService.OnInsufficientCredits (AC-2 source).");
            sm.Dispose();
        }

        [Test]
        public void Ctor_subscribes_to_the_encounter_shortfall_source_when_provided()
        {
            var encShortfall = new FakeEncounterShortfall();
            var sm = Create(out _, out _, encShortfall);
            Assert.IsTrue(encShortfall.HasSubscriber,
                "The ctor must subscribe to the EncounterService shortfall source when provided (AC-2 second source).");
            sm.Dispose();
        }

        // ---- AC-1: legal surface flow ----

        [Test]
        public void Start_surface_is_Onboarding()
        {
            var sm = Create(out _, out _);
            Assert.AreEqual(AppSurface.Onboarding, sm.Current);
            Assert.IsFalse(sm.OnboardingComplete);
            sm.Dispose();
        }

        [Test]
        public void CompleteOnboarding_is_the_only_exit_to_Home()
        {
            var sm = Create(out _, out _);
            var seen = Track(sm);

            Assert.IsTrue(sm.CompleteOnboarding());
            Assert.AreEqual(AppSurface.Home, sm.Current);
            Assert.IsTrue(sm.OnboardingComplete);
            CollectionAssert.AreEqual(new[] { AppSurface.Home }, seen);
            sm.Dispose();
        }

        [Test]
        public void CompleteOnboarding_second_call_is_a_no_op()
        {
            var sm = Create(out _, out _);
            sm.CompleteOnboarding();
            var seen = Track(sm);

            Assert.IsFalse(sm.CompleteOnboarding(), "A second CompleteOnboarding must be rejected.");
            Assert.AreEqual(AppSurface.Home, sm.Current);
            CollectionAssert.IsEmpty(seen, "A rejected re-complete must raise no surface-changed event.");
            sm.Dispose();
        }

        [Test]
        public void Home_to_Codex_and_back_is_legal()
        {
            var sm = Create(out _, out _);
            sm.CompleteOnboarding();
            var seen = Track(sm);

            Assert.IsTrue(sm.NavigateToCodex());
            Assert.AreEqual(AppSurface.Codex, sm.Current);
            Assert.IsTrue(sm.NavigateHome());
            Assert.AreEqual(AppSurface.Home, sm.Current);
            CollectionAssert.AreEqual(new[] { AppSurface.Codex, AppSurface.Home }, seen);
            sm.Dispose();
        }

        [Test]
        public void Codex_is_unreachable_directly_from_Onboarding()
        {
            var sm = Create(out _, out _);
            var seen = Track(sm);

            Assert.IsFalse(sm.NavigateToCodex(), "Codex is reachable only from Home.");
            Assert.AreEqual(AppSurface.Onboarding, sm.Current, "A rejected transition leaves Current unchanged.");
            CollectionAssert.IsEmpty(seen, "A rejected transition raises no surface-changed event.");
            sm.Dispose();
        }

        [Test]
        public void NavigateHome_is_rejected_from_Onboarding()
        {
            var sm = Create(out _, out _);
            var seen = Track(sm);

            Assert.IsFalse(sm.NavigateHome());
            Assert.AreEqual(AppSurface.Onboarding, sm.Current);
            CollectionAssert.IsEmpty(seen);
            sm.Dispose();
        }

        // ---- AC-3 derived: AR entry, cold vs Shop-resume ----

        [Test]
        public void Cold_AR_entry_is_rejected_until_onboarding_completes()
        {
            var sm = Create(out _, out _);
            var seen = Track(sm);

            bool ok = sm.EnterArHuntAsync(ArEntryKind.ColdEntry).GetAwaiter().GetResult();
            Assert.IsFalse(ok, "AR is unreachable until onboarding completes (the derived gate).");
            Assert.AreEqual(AppSurface.Onboarding, sm.Current);
            CollectionAssert.IsEmpty(seen);
            sm.Dispose();
        }

        [Test]
        public void Cold_AR_entry_from_Home_after_onboarding_is_legal()
        {
            var sm = Create(out _, out var enc);
            sm.CompleteOnboarding();
            var seen = Track(sm);

            bool ok = sm.EnterArHuntAsync(ArEntryKind.ColdEntry).GetAwaiter().GetResult();
            Assert.IsTrue(ok);
            Assert.AreEqual(AppSurface.ArHunt, sm.Current);
            CollectionAssert.AreEqual(new[] { AppSurface.ArHunt }, seen);
            Assert.AreEqual(0, enc.RehydrateCount, "A COLD entry must not rehydrate an encounter.");
            sm.Dispose();
        }

        [Test]
        public void Cold_AR_entry_is_rejected_from_a_non_Home_surface()
        {
            var sm = Create(out _, out _);
            sm.CompleteOnboarding();
            sm.NavigateToCodex();
            var seen = Track(sm);

            bool ok = sm.EnterArHuntAsync(ArEntryKind.ColdEntry).GetAwaiter().GetResult();
            Assert.IsFalse(ok, "Cold AR entry must start from Home.");
            Assert.AreEqual(AppSurface.Codex, sm.Current);
            CollectionAssert.IsEmpty(seen);
            sm.Dispose();
        }

        // ---- AC-2: insufficient-credits → Shop is the state machine's decision ----

        [Test]
        public void Credit_shortfall_navigates_to_Shop_and_captures_the_payload()
        {
            var sm = Create(out var credits, out _);
            sm.CompleteOnboarding();
            // Put the player in AR Hunt so the return surface is AR Hunt.
            sm.EnterArHuntAsync(ArEntryKind.ColdEntry).GetAwaiter().GetResult();

            credits.RaiseShortfall(cost: 4, balance: 1);

            Assert.AreEqual(AppSurface.Shop, sm.Current,
                "The STATE MACHINE (not the service) decides to navigate to Shop on a shortfall (AC-2).");
            Assert.IsTrue(sm.PendingShortfall.HasValue);
            Assert.AreEqual(4, sm.PendingShortfall.Value.Cost);
            Assert.AreEqual(1, sm.PendingShortfall.Value.Balance);
            Assert.AreEqual(AppSurface.ArHunt, sm.ShopReturnSurface,
                "The return surface is where the shortfall fired (AR Hunt).");
            sm.Dispose();
        }

        [Test]
        public void Credit_shortfall_raises_OnTopUpRequested_with_the_payload()
        {
            var sm = Create(out var credits, out _);
            sm.CompleteOnboarding();
            InsufficientCreditsEvent? captured = null;
            sm.OnTopUpRequested += e => captured = e;

            credits.RaiseShortfall(cost: 5, balance: 2);

            Assert.IsTrue(captured.HasValue, "OnTopUpRequested must fire with the shortfall payload.");
            Assert.AreEqual(5, captured.Value.Cost);
            Assert.AreEqual(2, captured.Value.Balance);
            sm.Dispose();
        }

        [Test]
        public void Credit_shortfall_from_active_AR_encounter_commits_Shop_synchronously()
        {
            // The race the spec warned about: when the shortfall fires from AR Hunt WITH an active
            // encounter, the navigation must commit SYNCHRONOUSLY (Current == Shop the instant the event
            // returns), and the snapshot runs off-thread afterward (durability), not blocking the commit.
            var sm = Create(out var credits, out var enc);
            sm.CompleteOnboarding();
            sm.EnterArHuntAsync(ArEntryKind.ColdEntry).GetAwaiter().GetResult();
            enc.HasActiveEncounter = true;

            credits.RaiseShortfall(cost: 4, balance: 0);

            // SYNCHRONOUS: no await between the raise and this assert — Current is already Shop.
            Assert.AreEqual(AppSurface.Shop, sm.Current,
                "The shortfall→Shop decision must be committed synchronously even with an active encounter.");
            Assert.IsTrue(sm.PendingShortfall.HasValue);

            // The snapshot is fired-and-forgotten off-thread; it must still eventually run (durability).
            Assert.IsTrue(enc.WaitForSnapshot(), "The encounter snapshot must run after the shortfall→Shop commit.");
            Assert.AreEqual(1, enc.SnapshotCount);
            sm.Dispose();
        }

        [Test]
        public void Encounter_shortfall_source_also_navigates_to_Shop()
        {
            var encShortfall = new FakeEncounterShortfall();
            var sm = Create(out _, out _, encShortfall);
            sm.CompleteOnboarding();

            encShortfall.RaiseShortfall(cost: 3, balance: 0);

            Assert.AreEqual(AppSurface.Shop, sm.Current,
                "The EncounterService shortfall (composed Lure/Slay) must ALSO drive Shop navigation (AC-2).");
            sm.Dispose();
        }

        [Test]
        public void Second_shortfall_while_already_in_Shop_refreshes_the_context()
        {
            var sm = Create(out var credits, out _);
            sm.CompleteOnboarding();
            credits.RaiseShortfall(cost: 4, balance: 1); // → Shop
            Assert.AreEqual(AppSurface.Shop, sm.Current);

            int topUps = 0;
            InsufficientCreditsEvent? last = null;
            sm.OnTopUpRequested += e => { topUps++; last = e; };
            var surfaceChanges = new List<AppSurface>();
            sm.OnSurfaceChanged += surfaceChanges.Add;

            credits.RaiseShortfall(cost: 9, balance: 2); // a second, larger shortfall while shopping

            Assert.AreEqual(AppSurface.Shop, sm.Current, "Still in Shop — no re-navigation.");
            CollectionAssert.IsEmpty(surfaceChanges, "A refresh while already in Shop must not re-commit the surface.");
            Assert.AreEqual(1, topUps, "The refresh must re-raise OnTopUpRequested exactly once.");
            Assert.AreEqual(9, last.Value.Cost, "The pending shortfall reflects the latest attempt.");
            Assert.AreEqual(9, sm.PendingShortfall.Value.Cost);
            sm.Dispose();
        }

        // ---- AC-2 cleanup: symmetric unsubscribe ----

        [Test]
        public void After_Dispose_a_shortfall_does_not_navigate()
        {
            var encShortfall = new FakeEncounterShortfall();
            var sm = Create(out var credits, out _, encShortfall);
            sm.CompleteOnboarding();
            sm.Dispose();

            credits.RaiseShortfall(cost: 4, balance: 1);
            encShortfall.RaiseShortfall(cost: 4, balance: 1);

            Assert.AreEqual(AppSurface.Home, sm.Current,
                "A disposed state machine must NOT navigate on a shortfall (symmetric unsubscribe).");
            Assert.IsFalse(credits.HasShortfallSubscriber, "Dispose must detach the credit handler.");
            Assert.IsFalse(encShortfall.HasSubscriber, "Dispose must detach the encounter handler.");
        }

        [Test]
        public void Dispose_is_idempotent()
        {
            var sm = Create(out _, out _);
            sm.Dispose();
            Assert.DoesNotThrow(() => sm.Dispose());
        }

        // ---- AC-3 derived: Shop round-trip snapshot ordering ----

        [Test]
        public void NavigateToShop_from_AR_with_active_encounter_snapshots_before_committing()
        {
            var sm = Create(out _, out var enc);
            sm.CompleteOnboarding();
            sm.EnterArHuntAsync(ArEntryKind.ColdEntry).GetAwaiter().GetResult();
            enc.HasActiveEncounter = true;

            // Record the moment the Shop commit happens relative to the snapshot call.
            sm.OnSurfaceChanged += s =>
            {
                if (s == AppSurface.Shop)
                {
                    enc.Calls.Add("shop-commit");
                }
            };

            bool ok = sm.NavigateToShopAsync().GetAwaiter().GetResult();

            Assert.IsTrue(ok);
            Assert.AreEqual(AppSurface.Shop, sm.Current);
            Assert.AreEqual(1, enc.SnapshotCount, "Exactly one snapshot when leaving AR Hunt with an active encounter.");
            CollectionAssert.AreEqual(new[] { "snapshot", "shop-commit" }, enc.CallsSnapshot(),
                "On the awaitable player path the snapshot MUST land before the Shop transition commits (architecture.md:524-525).");
            sm.Dispose();
        }

        [Test]
        public void NavigateToShop_from_AR_without_active_encounter_does_not_snapshot()
        {
            var sm = Create(out _, out var enc);
            sm.CompleteOnboarding();
            sm.EnterArHuntAsync(ArEntryKind.ColdEntry).GetAwaiter().GetResult();
            enc.HasActiveEncounter = false;

            sm.NavigateToShopAsync().GetAwaiter().GetResult();

            Assert.AreEqual(AppSurface.Shop, sm.Current);
            Assert.AreEqual(0, enc.SnapshotCount, "No active encounter ⇒ no snapshot.");
            sm.Dispose();
        }

        [Test]
        public void NavigateToShop_from_Home_does_not_snapshot_and_returns_to_Home()
        {
            var sm = Create(out _, out var enc);
            sm.CompleteOnboarding();
            enc.HasActiveEncounter = true; // even if a stale encounter flag is set, Home is not AR Hunt

            sm.NavigateToShopAsync().GetAwaiter().GetResult();

            Assert.AreEqual(AppSurface.Shop, sm.Current);
            Assert.AreEqual(0, enc.SnapshotCount, "Leaving Home (not AR Hunt) never snapshots.");
            Assert.AreEqual(AppSurface.Home, sm.ShopReturnSurface);
            sm.Dispose();
        }

        [Test]
        public void ReturnFromShop_to_AR_rehydrates_and_does_not_re_trigger_cold_entry()
        {
            var sm = Create(out _, out var enc);
            sm.CompleteOnboarding();
            sm.EnterArHuntAsync(ArEntryKind.ColdEntry).GetAwaiter().GetResult();
            enc.HasActiveEncounter = true;
            sm.NavigateToShopAsync().GetAwaiter().GetResult();
            enc.Calls.Clear();

            bool ok = sm.ReturnFromShopAsync().GetAwaiter().GetResult();

            Assert.IsTrue(ok);
            Assert.AreEqual(AppSurface.ArHunt, sm.Current);
            Assert.AreEqual(1, enc.RehydrateCount, "Shop-resume to AR Hunt rehydrates the snapshotted encounter.");
            Assert.IsFalse(sm.PendingShortfall.HasValue, "ReturnFromShop clears the pending shortfall.");
            sm.Dispose();
        }

        [Test]
        public void ReturnFromShop_to_Home_goes_Home_without_rehydrate()
        {
            var sm = Create(out _, out var enc);
            sm.CompleteOnboarding();
            sm.NavigateToShopAsync().GetAwaiter().GetResult(); // from Home → return surface Home

            bool ok = sm.ReturnFromShopAsync().GetAwaiter().GetResult();

            Assert.IsTrue(ok);
            Assert.AreEqual(AppSurface.Home, sm.Current);
            Assert.AreEqual(0, enc.RehydrateCount, "Returning to Home does not rehydrate an encounter.");
            sm.Dispose();
        }

        [Test]
        public void ReturnFromShop_is_rejected_when_not_in_Shop()
        {
            var sm = Create(out _, out _);
            sm.CompleteOnboarding();

            bool ok = sm.ReturnFromShopAsync().GetAwaiter().GetResult();
            Assert.IsFalse(ok, "ReturnFromShop is only valid from the Shop surface.");
            Assert.AreEqual(AppSurface.Home, sm.Current);
            sm.Dispose();
        }

        // ---- AC-3: overlay depth (one level deep) ----

        [Test]
        public void Overlay_starts_None()
        {
            var sm = Create(out _, out _);
            Assert.AreEqual(OverlayLevel.None, sm.Overlay);
            sm.Dispose();
        }

        [Test]
        public void OpenSheet_twice_stays_one_level_deep()
        {
            var sm = Create(out _, out _);
            sm.OpenSheet();
            Assert.AreEqual(OverlayLevel.Sheet, sm.Overlay);
            sm.OpenSheet(); // a second open REPLACES, never stacks a second level
            Assert.AreEqual(OverlayLevel.Sheet, sm.Overlay, "Overlays stack one level deep (AC-3).");
            sm.CloseSheet();
            Assert.AreEqual(OverlayLevel.None, sm.Overlay);
            sm.Dispose();
        }

        // ---- enum contract pins (the LoadPhaseContractTests precedent) ----

        [Test]
        public void AppSurface_has_exactly_the_five_surfaces()
        {
            string[] names = Enum.GetNames(typeof(AppSurface));
            CollectionAssert.AreEquivalent(
                new[] { "Onboarding", "Home", "ArHunt", "Codex", "Shop" },
                names,
                "AppSurface must declare exactly { Onboarding, Home, ArHunt, Codex, Shop }.");
        }

        [Test]
        public void ArEntryKind_has_exactly_cold_and_shop_resume()
        {
            string[] names = Enum.GetNames(typeof(ArEntryKind));
            CollectionAssert.AreEquivalent(
                new[] { "ColdEntry", "ShopResume" },
                names,
                "ArEntryKind must declare exactly { ColdEntry, ShopResume } (architecture.md:482-486).");
        }

        [Test]
        public void OverlayLevel_has_exactly_none_and_sheet()
        {
            // Pins the one-level-deep contract (AC-3). Load-bearing because ArHudPresenter.MapOverlay
            // maps OverlayLevel → OverlayLevelView with a two-value ternary; a new OverlayLevel member
            // added without revisiting that map would silently fold to None. This pin fails first.
            string[] names = Enum.GetNames(typeof(OverlayLevel));
            CollectionAssert.AreEquivalent(
                new[] { "None", "Sheet" },
                names,
                "OverlayLevel must declare exactly { None, Sheet } — overlays stack one level deep (AC-3).");
        }

        [Test]
        public void NoEncounter_port_is_a_graceful_no_op()
        {
            var port = IEncounterSnapshotPort.NoEncounter;
            Assert.IsFalse(port.HasActiveEncounter);
            Assert.IsFalse(port.SnapshotActiveEncounter().GetAwaiter().GetResult());
            Assert.IsFalse(port.RehydrateFromSnapshot().GetAwaiter().GetResult());
        }
    }
}
