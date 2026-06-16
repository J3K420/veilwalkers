using System;
using System.Threading;
using System.Threading.Tasks;
using Veilwalkers.Core;
using Veilwalkers.Economy;

namespace Veilwalkers.App
{
    /// <summary>
    /// The app surface-flow state machine (Story 6.3; architecture.md:462-464). It lives in the
    /// App/Bootstrap tier — NOT a Core service — and is "the only thing that arbitrates UI ↔ service
    /// control flow." It owns:
    /// <list type="bullet">
    /// <item>the legal surface flow Onboarding → Home → AR Hunt / Codex / Shop (AC-1);</item>
    /// <item>the insufficient-credits → Shop decision (AC-2): services only RAISE
    ///   <see cref="ICreditService.OnInsufficientCredits"/>; THIS class decides to navigate to Shop —
    ///   keeping <c>Billing → Economy</c> strictly one-way (architecture.md:478-480);</item>
    /// <item>the cold-entry-vs-Shop-resume AR distinction (architecture.md:482-486) so the FR-3 safety
    ///   warning and the re-anchor beat never stack into two interruptions for one monster;</item>
    /// <item>the Shop round-trip encounter snapshot/rehydrate orchestration (architecture.md:524-525)
    ///   via the narrow <see cref="IEncounterSnapshotPort"/>;</item>
    /// <item>the "overlays stack one level deep" invariant (AC-3).</item>
    /// </list>
    /// Pure logic: ctor-injected (it does NOT read <see cref="GameServices"/>), no <c>MonoBehaviour</c>,
    /// fully headless-testable. The thin surface VIEWS (Home / AR HUD) and the actual scene assets are
    /// the deferred Epic-6 view layer; the surface PRESENTERS that compose the Story-6.2 chunky
    /// descriptors live in <c>Veilwalkers.UI</c> (App is below UI in the acyclic graph, so the
    /// descriptor-composing presenters cannot live here).
    /// <para>
    /// <b>NFR-3 graceful:</b> an illegal navigation request is a no-op + <see cref="GameLog.Warn"/>,
    /// never a throw. <b>Symmetric lifecycle:</b> the ctor subscribes to the insufficient-credits
    /// event(s); <see cref="Dispose"/> detaches them (architecture.md:514-515 — symmetric
    /// subscribe/unsubscribe is an acceptance criterion; a leaked handler is a UJ-2 double-fire risk).
    /// </para>
    /// </summary>
    public sealed class AppStateMachine : IDisposable
    {
        private readonly ICreditService _credits;
        private readonly IEncounterSnapshotPort _encounter;

        // The optional second shortfall source: EncounterService raises the SAME InsufficientCreditsEvent
        // for composed actions (Lure/Slay). It is a deferred Bootstrap seam, so it may be null.
        private readonly IInsufficientCreditsSource _encounterShortfall;

        private bool _disposed;

        /// <summary>The current surface. Starts at <see cref="AppSurface.Onboarding"/>.</summary>
        public AppSurface Current { get; private set; } = AppSurface.Onboarding;

        /// <summary>Whether the onboarding shell has been completed (the AR-entry gate). Set once by
        /// <see cref="CompleteOnboarding"/>; never reverts.</summary>
        public bool OnboardingComplete { get; private set; }

        /// <summary>The current overlay depth (AC-3 — "sheets/overlays stack one level deep").</summary>
        public OverlayLevel Overlay { get; private set; } = OverlayLevel.None;

        /// <summary>
        /// The surface to return to when the Shop is dismissed. Captured when the Shop is opened
        /// (whether player-initiated or shortfall-triggered) so "Buy → back" returns where the player
        /// was — typically <see cref="AppSurface.ArHunt"/>.
        /// </summary>
        public AppSurface ShopReturnSurface { get; private set; } = AppSurface.Home;

        /// <summary>
        /// The shortfall that auto-navigated to Shop (AC-2), if any — carries the attempted
        /// <see cref="InsufficientCreditsEvent.Cost"/> and the (unchanged)
        /// <see cref="InsufficientCreditsEvent.Balance"/> so the Shop surface can frame a non-alarming
        /// "you need N more" top-up (the calm-copy PIXELS are Story 6.5; 6.3 carries the data). Null
        /// for a player-initiated Shop visit. Cleared by <see cref="ReturnFromShopAsync"/>.
        /// </summary>
        public InsufficientCreditsEvent? PendingShortfall { get; private set; }

        /// <summary>Raised on every COMMITTED surface change; payload is the new surface. Views
        /// subscribe (and unsubscribe symmetrically — the view's job, architecture.md:514-515).</summary>
        public event Action<AppSurface> OnSurfaceChanged;

        /// <summary>Raised when an insufficient-credits shortfall auto-navigates to Shop (AC-2), with
        /// the shortfall payload, so the Shop/top-up surface can show the calm "need N more" framing.
        /// Distinct from <see cref="OnSurfaceChanged"/> so a consumer can react to the SHORTFALL
        /// specifically vs a plain Shop visit.</summary>
        public event Action<InsufficientCreditsEvent> OnTopUpRequested;

        /// <summary>
        /// Construct + subscribe to the credit shortfall event(s). <paramref name="encounter"/> may be
        /// <see cref="IEncounterSnapshotPort.NoEncounter"/> when EncounterService is an unregistered
        /// Bootstrap seam (graceful). <paramref name="encounterShortfall"/> is the optional second
        /// shortfall source (EncounterService) — null when Encounter is unregistered.
        /// </summary>
        public AppStateMachine(
            ICreditService credits,
            IEncounterSnapshotPort encounter,
            IInsufficientCreditsSource encounterShortfall = null)
        {
            _credits = credits ?? throw new ArgumentNullException(nameof(credits));
            _encounter = encounter ?? throw new ArgumentNullException(nameof(encounter));
            _encounterShortfall = encounterShortfall;

            _credits.OnInsufficientCredits += HandleInsufficientCredits;
            if (_encounterShortfall != null)
            {
                _encounterShortfall.OnInsufficientCredits += HandleInsufficientCredits;
            }
        }

        // ---- AC-1: legal surface flow ----

        /// <summary>
        /// Complete onboarding (the ONLY exit from <see cref="AppSurface.Onboarding"/>) and land on
        /// <see cref="AppSurface.Home"/>. Idempotent: a second call while already past onboarding is a
        /// no-op + warn (never re-runs the grant or re-navigates). The onboarding CONTENT (premise
        /// cards, camera disclosure, coin-burst) is Story 6.4; this is the gate.
        /// </summary>
        public bool CompleteOnboarding()
        {
            if (OnboardingComplete)
            {
                GameLog.Warn("AppStateMachine.CompleteOnboarding: onboarding is already complete; ignoring.");
                return false;
            }

            OnboardingComplete = true;
            return Commit(AppSurface.Home);
        }

        /// <summary>Navigate Home from a top-level surface (AR Hunt / Codex / Shop). Rejected from
        /// Onboarding (must complete onboarding first) — no-op + warn.</summary>
        public bool NavigateHome()
        {
            if (Current == AppSurface.Onboarding)
            {
                return Reject(AppSurface.Home, "onboarding is not complete");
            }

            return Commit(AppSurface.Home);
        }

        /// <summary>Open the Codex grid. Legal only from <see cref="AppSurface.Home"/>.</summary>
        public bool NavigateToCodex()
        {
            if (Current != AppSurface.Home)
            {
                return Reject(AppSurface.Codex, "Codex is reachable only from Home");
            }

            return Commit(AppSurface.Codex);
        }

        // ---- AC-3 derived: AR entry with the cold-vs-resume distinction ----

        /// <summary>
        /// Enter AR Hunt. <see cref="ArEntryKind.ColdEntry"/> requires onboarding complete and must be
        /// initiated from Home (the FR-3 safety warning fires in the AR layer on cold entry — 6.3 does
        /// NOT suppress it). <see cref="ArEntryKind.ShopResume"/> re-enters WITHOUT the cold-entry gate
        /// and rehydrates the snapshotted encounter (architecture.md:483-484); it is the path
        /// <see cref="ReturnFromShopAsync"/> uses when the return surface is AR Hunt.
        /// <para>Returns false (no-op + warn) when a cold entry is attempted before onboarding completes
        /// (the derived "AR unreachable until onboarding completes" gate) or from a non-Home surface.</para>
        /// </summary>
        public async Task<bool> EnterArHuntAsync(ArEntryKind kind)
        {
            if (kind == ArEntryKind.ColdEntry)
            {
                if (!OnboardingComplete)
                {
                    return Reject(AppSurface.ArHunt, "onboarding is not complete (AR is unreachable until then)");
                }

                if (Current != AppSurface.Home)
                {
                    return Reject(AppSurface.ArHunt, "cold AR entry must start from Home");
                }

                return Commit(AppSurface.ArHunt);
            }

            // ShopResume: re-enter without the cold-entry gate and rehydrate the encounter. Rehydrate
            // BEFORE committing the surface so a consumer reacting to OnSurfaceChanged(ArHunt) already
            // sees the restored logical encounter. A rehydrate fault never blocks the return (graceful).
            try
            {
                await _encounter.RehydrateFromSnapshot();
            }
            catch (Exception ex)
            {
                GameLog.Warn($"AppStateMachine: encounter rehydrate on Shop-resume faulted (continuing to AR Hunt): {ex}");
            }

            return Commit(AppSurface.ArHunt);
        }

        // ---- AC-2 + AC-3 derived: Shop navigation + the round-trip snapshot ----

        /// <summary>
        /// Navigate to the Shop (player-initiated via a Home/AR Shop entry). If leaving AR Hunt with a
        /// live encounter, snapshot it BEFORE committing the Shop transition (so an app-kill in Shop
        /// survives — Story 5.4 built the persistence; 6.3 wires the call site). Records the return
        /// surface so <see cref="ReturnFromShopAsync"/> goes back where the player was.
        /// <para>This is the AWAITABLE path: the caller controls ordering, so the snapshot is awaited
        /// before the commit (architecture.md:524-525). The shortfall path (a synchronous event handler
        /// that cannot await) commits synchronously, then snapshots — see
        /// <see cref="HandleInsufficientCredits"/>; the snapshot reads the unchanged encounter state, so
        /// commit-then-snapshot is durability-equivalent there.</para>
        /// </summary>
        public async Task<bool> NavigateToShopAsync()
        {
            if (Current == AppSurface.Shop)
            {
                return false; // already in Shop, player re-tap — nothing to commit.
            }

            // Snapshot BEFORE the transition commits (the awaitable contract). A snapshot fault does not
            // block the Shop trip (the player can still buy credits); it is logged and the trip proceeds.
            if (Current == AppSurface.ArHunt && _encounter.HasActiveEncounter)
            {
                try
                {
                    await _encounter.SnapshotActiveEncounter();
                }
                catch (Exception ex)
                {
                    GameLog.Warn($"AppStateMachine: encounter snapshot before Shop faulted (continuing to Shop): {ex}");
                }
            }

            return EnterShopSync(shortfall: null);
        }

        /// <summary>
        /// Return from the Shop to the recorded <see cref="ShopReturnSurface"/>. If that is AR Hunt,
        /// route through <see cref="EnterArHuntAsync"/> with <see cref="ArEntryKind.ShopResume"/> (no
        /// re-warn, rehydrate); otherwise go Home. Clears <see cref="PendingShortfall"/> + the overlay
        /// ONLY after the return surface is committed (failure-path-cleanup-parity — never clear state
        /// before the operation that could fail confirms; today EnterArHuntAsync always commits, but the
        /// cleanup stays downstream of the await so a future failure return can't leave half-cleared state).
        /// </summary>
        public async Task<bool> ReturnFromShopAsync()
        {
            if (Current != AppSurface.Shop)
            {
                return Reject(ShopReturnSurface, "ReturnFromShop is only valid from the Shop surface");
            }

            AppSurface target = ShopReturnSurface;

            bool committed = target == AppSurface.ArHunt
                ? await EnterArHuntAsync(ArEntryKind.ShopResume)
                : Commit(AppSurface.Home);

            if (committed)
            {
                // Cleared only on a confirmed return: the sheet does not outlive its surface, and the
                // shortfall context is consumed. (Parity guard: a future failed return keeps both.)
                Overlay = OverlayLevel.None;
                PendingShortfall = null;
            }

            return committed;
        }

        // ---- AC-3: overlay depth (one level deep) ----

        /// <summary>
        /// Open a sheet/overlay over the current surface. "Overlays stack ONE level deep" (AC-3):
        /// opening a sheet while one is already open REPLACES it (stays <see cref="OverlayLevel.Sheet"/>,
        /// logs the replacement) — it never becomes a second stacked level.
        /// </summary>
        public void OpenSheet()
        {
            if (Overlay == OverlayLevel.Sheet)
            {
                GameLog.Info("AppStateMachine.OpenSheet: a sheet is already open; replacing (overlays stack one level deep).");
            }

            Overlay = OverlayLevel.Sheet;
        }

        /// <summary>Close the open sheet/overlay (→ <see cref="OverlayLevel.None"/>). A no-op when none
        /// is open.</summary>
        public void CloseSheet()
        {
            Overlay = OverlayLevel.None;
        }

        // ---- internals ----

        // The AC-2 handler: a service raised the shortfall; the STATE MACHINE decides to go to Shop.
        // Raised possibly off the mutation lock / on a background thread (the ICreditService contract).
        // The navigation DECISION is committed SYNCHRONOUSLY here (EnterShopSync) so the moment the event
        // returns to the raising service, Current == Shop and OnTopUpRequested has fired — there is NO
        // async window where a caller reading Current observes a stale ArHunt (the race the spec warned
        // about). The encounter SNAPSHOT (a durability concern) is then kicked as observed fire-and-forget:
        // the snapshot reads the unchanged encounter state (navigation does not tear the encounter down),
        // so commit-then-snapshot is durability-equivalent to snapshot-then-commit, and it removes the
        // ordering hazard + the TOCTOU/dispose-during-await windows entirely. Exception-safe: a throw here
        // must not corrupt the raiser. Does NO save mutation on the calling thread.
        private void HandleInsufficientCredits(InsufficientCreditsEvent shortfall)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                bool leavingActiveArEncounter =
                    Current == AppSurface.ArHunt && _encounter.HasActiveEncounter;

                // Synchronous navigation decision (AC-2). If already in Shop, EnterShopSync refreshes the
                // shortfall context (does not re-navigate or re-snapshot).
                EnterShopSync(shortfall);

                // Durability: persist the encounter we just left, off the event-raiser's thread. Re-checks
                // _disposed inside (the await window could outlive a Dispose), uses TaskScheduler.Default
                // (NOT the ambient TaskScheduler.Current — the raiser's context is undefined), and observes
                // EVERY outcome (fault OR completion) so nothing dies as an unobserved task exception.
                if (leavingActiveArEncounter)
                {
                    _ = Task.Factory.StartNew(
                            SnapshotAfterShopAsync,
                            CancellationToken.None,
                            TaskCreationOptions.DenyChildAttach,
                            TaskScheduler.Default)
                        .Unwrap();
                }
            }
            catch (Exception ex)
            {
                GameLog.Warn($"AppStateMachine.HandleInsufficientCredits faulted (ignored): {ex}");
            }
        }

        // The fire-and-forget snapshot kicked AFTER a shortfall-driven Shop commit. Self-contained
        // exception handling (it runs detached): a fault never escapes as an unobserved task exception.
        private async Task SnapshotAfterShopAsync()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await _encounter.SnapshotActiveEncounter();
            }
            catch (Exception ex)
            {
                GameLog.Warn($"AppStateMachine: encounter snapshot after shortfall→Shop faulted: {ex}");
            }
        }

        // The SYNCHRONOUS Shop-navigation core shared by the player path and the shortfall handler. It
        // records the return surface, commits Shop, sets the pending shortfall, and raises
        // OnTopUpRequested — all synchronously, so Current is authoritative the instant this returns. It
        // does NOT snapshot (the caller owns the snapshot ordering: the player path awaits it before
        // calling this; the shortfall path fires it after). Returns true when a navigation or an
        // observable shortfall-refresh occurred.
        private bool EnterShopSync(InsufficientCreditsEvent? shortfall)
        {
            if (Current == AppSurface.Shop)
            {
                // Already in Shop (e.g. a second shortfall while shopping) — refresh the shortfall context
                // so the top-up framing reflects the latest attempt; do not re-navigate. Returns true
                // because an observable change occurred (PendingShortfall updated + OnTopUpRequested fired)
                // — false would mislead a caller into thinking nothing happened.
                if (shortfall.HasValue)
                {
                    PendingShortfall = shortfall;
                    OnTopUpRequested?.Invoke(shortfall.Value);
                    return true;
                }

                return false;
            }

            // Record where to return BEFORE we change Current.
            ShopReturnSurface = Current == AppSurface.ArHunt ? AppSurface.ArHunt : AppSurface.Home;

            PendingShortfall = shortfall;
            bool committed = Commit(AppSurface.Shop);

            if (committed && shortfall.HasValue)
            {
                OnTopUpRequested?.Invoke(shortfall.Value);
            }

            return committed;
        }

        // Commit a transition: set Current + raise OnSurfaceChanged. The single place state changes, so
        // the event fires exactly once per committed transition.
        private bool Commit(AppSurface target)
        {
            Current = target;
            OnSurfaceChanged?.Invoke(target);
            return true;
        }

        // Reject an illegal navigation: no state change, no event, a warn, return false (NFR-3 — a
        // navigation no-op is graceful, never a throw).
        private bool Reject(AppSurface attempted, string reason)
        {
            GameLog.Warn(
                $"AppStateMachine: rejected navigation {Current} → {attempted} ({reason}); staying on {Current}.");
            return false;
        }

        /// <summary>
        /// Detach the insufficient-credits subscription(s) (symmetric with the ctor —
        /// architecture.md:514-515). After disposal, a shortfall event no longer navigates. Idempotent.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _credits.OnInsufficientCredits -= HandleInsufficientCredits;
            if (_encounterShortfall != null)
            {
                _encounterShortfall.OnInsufficientCredits -= HandleInsufficientCredits;
            }
        }
    }
}
