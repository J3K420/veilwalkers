# Story 6.3: Navigate between surfaces via the app state machine

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

baseline_commit: 5d0a9238e4d9435dca9836ddd19a61dce193e6ac

## Story

As a player,
I want to move between Home, AR Hunt, Codex, and Shop,
so that I can reach every part of the game.

## Acceptance Criteria

Sourced verbatim-in-intent from `docs/epics.md#Story-6.3` (lines 877–895) and `docs/architecture.md` (lines 221, 224–225, 373, 462–464, 478–486, 512–525).

**AC-1 — The surface-flow state machine (App/Bootstrap tier)**
**Given** `AppStateMachine` in the App/Bootstrap tier
**When** the app runs
**Then** navigation flows **Onboarding → Home → AR Hunt / Codex / Shop**, with **Home** surfacing the wordmark, credit pill, the **ENTER AR HUNT** CTA, the **Codex (X/67)** entry, the **Shop** entry, and the **daily-reward affordance**.

**AC-2 — Insufficient-credits → Shop is the state machine's decision, not a service's**
**Given** an `OnInsufficientCredits` event
**When** App/UI reacts
**Then** **`AppStateMachine` (not a service)** decides Shop/top-up navigation. Services only raise the event (`CreditService` / `EncounterService`); the state machine arbitrates the navigation (architecture.md:478–480 — keeps `Billing → Economy` strictly one-way).

**AC-3 — AR Hunt is full-bleed camera with floating chunky chrome; overlays stack one level deep**
**Given** the AR Hunt surface
**When** it renders
**Then** it is **full-bleed camera** with chrome (action bar, credit pill) floating as **discrete chunky islands, never a docked frame**; **sheets/overlays stack one level deep** (a sheet over AR Hunt is the deepest legal overlay — opening a second sheet replaces, never stacks two).

### Derived / load-bearing requirements (not separately numbered but required for the feature to work end-to-end)

- **AR-entry gating (architecture.md:482–486, Story 3.2/3.5):** "AR entry" = **cold-entry into AR mode only** fires the FR-3 safety warning + (Story 6.4) requires onboarding complete. **Resuming AR after a Shop round-trip does NOT re-fire** the full warning. The state machine must distinguish a **cold AR entry** from a **Shop-resume AR re-entry** so 6.4's gate and 3.5's re-anchor beat don't stack into two interruptions for one monster.
- **Shop round-trip snapshot (architecture.md:524–525, Story 5.4):** when navigating to Shop **while an encounter is active**, the state machine snapshots the active Encounter (via the already-built `EncounterService.SnapshotActiveEncounter()`), and on return **rehydrates** (`EncounterService.RehydrateFromSnapshot()`). The Safety Warning does NOT re-fire on that return (the `Shop-resume` entry kind, above).
- **Onboarding gate (architecture.md:221, Story 6.4 seam):** AR Hunt is unreachable until Onboarding completes. 6.3 owns the *state-machine gate* (the `Onboarding` state precedes `Home`, and a transition request to `ArHunt` while onboarding is incomplete is rejected); the *onboarding content* (premise cards, camera disclosure, coin-burst) is Story 6.4.

## Tasks / Subtasks

> **Altitude split (the load-bearing decision for this story).** `Veilwalkers.UI` references `Veilwalkers.App` (App is BELOW UI in the acyclic graph — see `AcyclicDependencyTests`). Therefore App **cannot** reference UI, and a presenter that composes the Story-6.2 chunky descriptors (which live in `Veilwalkers.UI`) **cannot** live in App. The split is:
> - **`AppStateMachine` + the surface/state enums + the navigation-intent types → `Veilwalkers.App`** (pure logic; owns flow, the insufficient-credits arbitration, the AR-entry-kind decision, and the Shop-round-trip snapshot orchestration). Tested in `Veilwalkers.App.Tests`.
> - **The surface *presenters* that assemble the 6.2 descriptors into a Home/AR-Hunt view-model → `Veilwalkers.UI`** (consume the AppStateMachine's read-model + the 6.2 styles; the [[ui-presenter-pattern]] split). Tested in `Veilwalkers.UI.Tests`.
> - **The thin `*View : MonoBehaviour` surfaces + the actual scene assets + the AR-rig re-anchor render → deferred** (logic-free views; visual/scene wiring is Story 6.4/6.5 and the device AR-rig scene). 6.3 is **headless logic only**, like every prior UI story.

- [ ] **Task 1 — `AppState` / `AppSurface` model (AC-1)** `Veilwalkers.App`
  - [ ] Add a standalone `public enum AppSurface { Onboarding, Home, ArHunt, Codex, Shop }` (PascalCase per architecture.md:283; standalone, not nested — mirror `LoadPhase` so the App.Tests can assert members/order without `AppStateMachine` accessibility).
  - [ ] Add `public enum ArEntryKind { ColdEntry, ShopResume }` (the architecture.md:482–486 distinction — cold-entry fires the full safety read/onboarding gate; Shop-resume does not).
  - [ ] (If needed for overlay depth) add `public enum OverlayLevel { None, Sheet }` to encode AC-3's "one level deep" as a type, not a magic number.

- [ ] **Task 2 — `AppStateMachine` core transitions (AC-1)** `Veilwalkers.App`
  - [ ] `public sealed class AppStateMachine` — pure logic, ctor-injected (NO `GameServices.Get<T>()`; NO MonoBehaviour). Ctor takes the collaborators it actually arbitrates over: `ICreditService` (for the `OnInsufficientCredits` subscription), and the `EncounterService` **only via a narrow interface/delegate seam** (see Task 4 — do NOT take the whole `EncounterService` if a 2-method seam suffices; App already references Encounter so either is legal, but prefer the seam for testability). It does NOT need Monsters/Billing directly — Codex(X/67) and pack rendering are the UI presenters' job (Task 6).
  - [ ] `public AppSurface Current { get; private set; }` starting at `Onboarding`.
  - [ ] `public event Action<AppSurface> OnSurfaceChanged;` raised on every committed transition (the View subscribes; symmetric subscribe/unsubscribe is the UI's job, architecture.md:514–515).
  - [ ] Transition methods that encode the **legal** flow only (Onboarding → Home; Home → ArHunt/Codex/Shop; ArHunt/Codex/Shop → Home). An illegal transition (e.g. Onboarding → ArHunt directly, or Codex → Shop) is **rejected** (no state change; `GameLog.Warn`; return a typed `bool`/result so callers/tests can assert the rejection — do NOT throw on a navigation no-op, NFR-3 graceful).
  - [ ] `CompleteOnboarding()` is the ONLY path out of `Onboarding` (→ `Home`). A request to enter `ArHunt` while `Current == Onboarding` is rejected (the AR-3 derived "unreachable until onboarding completes" gate; the onboarding *content* is 6.4).

- [ ] **Task 3 — Insufficient-credits → Shop arbitration (AC-2)** `Veilwalkers.App`
  - [ ] In the ctor, subscribe to `ICreditService.OnInsufficientCredits` (and the `EncounterService.OnInsufficientCredits` seam from Task 4 — both raise the SAME `InsufficientCreditsEvent` payload). Expose a `Dispose()`/`Unsubscribe()` that detaches both (symmetric lifecycle — the App-tier owner of the subscription; a leaked handler is the recurring [[failure-path-cleanup-parity]] class).
  - [ ] On the event, **the state machine decides** to navigate to `Shop` (it does NOT just expose a flag for the UI to interpret — AC-2 says "AppStateMachine (not a service) decides"). It records the **return surface** (the surface that was current when the shortfall fired — typically `ArHunt`) so the Shop "Buy → back" path returns there, AND (per the derived Shop-round-trip requirement) triggers the encounter snapshot if the return surface is `ArHunt` with an active encounter.
  - [ ] Expose the captured shortfall context for the UI (e.g. `public InsufficientCreditsEvent? PendingShortfall { get; }` or an `OnTopUpRequested` event carrying the `InsufficientCreditsEvent`) so the Shop surface can show the non-alarming "you need N more" top-up framing (the *pixels* are 6.5; 6.3 carries the data the calm message needs).
  - [ ] **Re-entrancy/thread note:** `OnInsufficientCredits` may be raised off the mutation lock / on a background thread (per `ICreditService` doc). The handler must NOT do save mutation; it only flips navigation state + raises `OnSurfaceChanged`/`OnTopUpRequested`. Keep the handler allocation-light and exception-safe (a throw in an event handler must not corrupt the raiser — wrap defensively + `GameLog`).

- [ ] **Task 4 — AR-entry-kind + Shop-round-trip snapshot orchestration (AC-3 derived + architecture.md:482–486, 524–525)** `Veilwalkers.App`
  - [ ] Define the narrow encounter seam the state machine needs WITHOUT pulling all of `EncounterService`: e.g. `internal interface IEncounterSnapshotPort { bool HasActiveEncounter { get; } Task<bool> SnapshotActiveEncounter(); Task<bool> RehydrateFromSnapshot(); }` adapted onto `EncounterService` in Bootstrap (a thin adapter, or have `EncounterService` implement it — but do NOT widen the seam beyond these three members). This keeps the App.Tests able to fake the port deterministically (no real Encounter needed).
    - **`HasActiveEncounter` is NOT an existing `EncounterService` member** — the adapter computes it as `State == EncounterState.Lured` (EncounterService.cs:132 exposes `EncounterState State`; only `Lured` is snapshottable — `SnapshotActiveEncounter()` itself already returns `false` for any non-`Lured` state, EncounterService.cs:1910). Use exactly that predicate so the activeness check matches the service's own snapshot gate; do NOT invent a different activeness rule.
  - [ ] `EnterArHunt(ArEntryKind kind)`:
    - `ColdEntry` → the transition that (in 6.4) fires the safety warning + requires onboarding-complete (6.3 enforces the onboarding-complete precondition; the safety-warning fire is the AR layer's, already built — 6.3 just must NOT suppress it on cold entry).
    - `ShopResume` → re-enters `ArHunt` WITHOUT the cold-entry gate, and calls `RehydrateFromSnapshot()` if a snapshot exists (architecture.md:483–484 — the warning does NOT re-fire; the re-anchor beat is 3.5's, the scene render is deferred).
  - [ ] `NavigateToShop()` (whether player-initiated via the Home/AR Shop entry OR auto-triggered by the shortfall in Task 3): if leaving `ArHunt` with `HasActiveEncounter`, `await SnapshotActiveEncounter()` BEFORE committing the Shop transition (so an app-kill in Shop survives — 5.4 built the persistence; 6.3 wires the *call site*). Record return surface = `ArHunt`. **Signature:** because the snapshot/rehydrate ports are `Task<bool>`, `NavigateToShop()` and `ReturnFromShop()` MUST be `async Task` (not `void`/sync) so the "snapshot before the Shop transition commits" ordering is actually awaitable/enforceable — the Task-9 order-sensitive test depends on this. A sync method that fire-and-forgets the snapshot would race the commit.
  - [ ] `ReturnFromShop()`: navigate back to the recorded return surface; if it is `ArHunt`, route through `EnterArHunt(ArEntryKind.ShopResume)` (so the rehydrate + no-re-warn rules hold). If the return surface is `Home`, just go Home.

- [ ] **Task 5 — Overlay depth invariant (AC-3)** `Veilwalkers.App`
  - [ ] Model "sheets/overlays stack one level deep": `public OverlayLevel Overlay { get; private set; }` + `OpenSheet()` / `CloseSheet()`. `OpenSheet()` while `Overlay == Sheet` **replaces** (stays `Sheet`, raises a "replaced" signal/log) — it never becomes a second stacked level. `CloseSheet()` → `None`. This is the testable encoding of "stack one level deep"; the actual sheet *content* (top-up sheet, etc.) is UI/6.5.
  - [ ] AR Hunt chrome being "floating chunky islands, never a docked frame" is a **layout/scene** property (6.4/6.5 view work) — 6.3 records it as a deferred view note; there is no headless logic to test for "floating vs docked." Do NOT invent a fake assertion for it. (Document this honestly in the story's deferred notes — silent-cap honesty per the CR convention.)

- [ ] **Task 6 — `HomePresenter` (AC-1 surface composition)** `Veilwalkers.UI`
  - [ ] `public sealed class HomePresenter` — ctor-injected with `CodexService` (for `DiscoveredCount`/`UniverseCount` → the "Codex (X/67)" label, reusing the `CodexGridPresenter.ProgressLabel` precedent), `ICreditService` (for the credit-pill count), and the `AppStateMachine` (or its read-model) for the navigation intents. Pure logic; returns a `HomeViewModel`.
  - [ ] `HomeViewModel` composes the Story-6.2 descriptors: `WordmarkStyle.Create()`; `CreditPillStyle.For(balance)`; the **ENTER AR HUNT** CTA as `ChunkyButtonStyle.For(ChunkyButtonKind.Primary, "Enter AR Hunt")` (ALL-CAPS handled by the descriptor); the Codex entry label `"Codex {X}/{67}"`; a Shop entry; and the **daily-reward affordance** state (read from `IDailyRewardService.CanClaimToday` — the 1.8 service's `bool` "can claim today" property; do NOT re-implement the calendar-day rule). The CTA's *tap action* routes to `AppStateMachine.EnterArHunt(ArEntryKind.ColdEntry)` (wired by the View; the presenter exposes the intent).
  - [ ] Null-safe / degrade-gracefully if a service is unresolved (the CodexGridPresenter "inert while unregistered" precedent) — Home must not hard-crash if `CodexService`/Encounter is still seam-blocked.

- [ ] **Task 7 — `ArHudPresenter` (AC-3 surface composition)** `Veilwalkers.UI`
  - [ ] `public sealed class ArHudPresenter` — composes the floating-island chrome view-model: the credit pill (`CreditPillStyle.For(balance, nearEmpty)` — the felt-descent pulse + `TapToShop` intent is 6.2's; the tap routes to `AppStateMachine.NavigateToShop()`), and the action-bar chunky buttons (Scan/Capture/Slay/Lure affordances as `ChunkyButtonStyle` descriptors; the SLAY variant carries its mandated icon + "SLAY" label from 6.2). The presenter only assembles descriptors + exposes the navigation/encounter intents; it owns NO encounter logic (that's `EncounterService`, Epic 4).
  - [ ] Expose the `OverlayLevel` from the AppStateMachine so the HUD view knows whether a sheet is open (AC-3 one-level-deep).

- [ ] **Task 8 — Bootstrap wiring (AC-1/AC-2)** `Veilwalkers.App`
  - [ ] Construct `AppStateMachine` in `Bootstrap.WireServices()` AFTER the services it subscribes to (`creditService`, and the `EncounterService` snapshot port — note `EncounterService` is currently a deferred seam in Bootstrap because `MonsterDatabase.asset` is unauthored; if Encounter is still unregistered, the AppStateMachine must be constructed with a **null/no-op snapshot port** that degrades gracefully, exactly like the CodexService consumers degrade — document this; do NOT block 6.3 on the MonsterDatabase deferral). Register it (`GameServices.Register<AppStateMachine>(...)`) so the Views resolve it.
  - [ ] Subscribe `AppStateMachine` to `creditService.OnInsufficientCredits` at wiring time; ensure a teardown path detaches it (Bootstrap has no teardown today, but the `Dispose` must exist for tests + future scene-unload).
  - [ ] Do NOT change the acyclic matrix — `Veilwalkers.App → {Economy, Encounter, ...}` and `Veilwalkers.UI → {App, ...}` are already sanctioned edges (verified against `AcyclicDependencyTests`). No new asmdef edge is required (App's asmdef already references Economy/Encounter; UI's already references App). **Confirm this holds; if any NEW cross-tier type is used that isn't already referenced, add BOTH the asmdef ref AND the matrix row** ([[unity-asmdef-nontransitive]]).
  - [ ] Settle the deferred `SampleScene` item: 6.3 introduces the App-state-flow but the **scene assets are deferred** (no Home scene built headlessly). Record in deferred-work that `SampleScene` removal/replacement moves with the Home *scene* (6.4/6.5), not 6.3 — 6.3 is logic-only. (Do NOT delete SampleScene in a logic-only story; that would break the build with no replacement.)

- [ ] **Task 9 — Tests (all ACs)** `Veilwalkers.App.Tests` + `Veilwalkers.UI.Tests`
  - [ ] **App.Tests (`AppStateMachineTests`):**
    - AC-1: the legal flow transitions commit + raise `OnSurfaceChanged`; the start state is `Onboarding`; `CompleteOnboarding()` is the only exit; illegal transitions (Onboarding→ArHunt, Codex→Shop, etc.) are rejected (no state change, returns false/typed-reject) — **non-tautological**: assert the rejected transition left `Current` unchanged AND raised no `OnSurfaceChanged`.
    - AC-2: raising `OnInsufficientCredits` on a fake `ICreditService` (and on the fake encounter port) drives `Current → Shop` and captures the `InsufficientCreditsEvent` (Cost/Balance preserved) + the return surface. Assert it was the **state machine** that navigated (the fake service did nothing but raise).
    - AC-2 cleanup: after `Dispose()`, a subsequent `OnInsufficientCredits` raise does NOT navigate (handler detached — the [[failure-path-cleanup-parity]] symmetric-unsubscribe pin).
    - AC-3 derived: `NavigateToShop()` from `ArHunt` with a fake port reporting `HasActiveEncounter == true` calls `SnapshotActiveEncounter()` exactly once BEFORE the Shop transition commits (order-sensitive — use a recording fake). `ReturnFromShop()` to `ArHunt` routes through `ShopResume` → calls `RehydrateFromSnapshot()` and does NOT re-trigger the cold-entry gate. Cold `EnterArHunt(ColdEntry)` while `Onboarding` is rejected.
    - AC-3 overlay: `OpenSheet()` twice stays one level deep (`Overlay == Sheet`, the second open "replaced" not "stacked"); `CloseSheet()` → `None`.
    - Enum pins: `AppSurface` members + order; `ArEntryKind` members (mirrors the `LoadPhaseContractTests` precedent).
  - [ ] **UI.Tests (`HomePresenterTests`, `ArHudPresenterTests`):**
    - `HomeViewModel` carries the wordmark/credit-pill/ENTER-AR-HUNT/Codex(X/67)/Shop/daily-reward affordances, each sourced from the right service (real `CodexService` over the `FakeUiCodexProgressStore`+`SaveService` precedent → `Codex 0/67` then `3/67` after discoveries; credit pill count == balance; daily-reward affordance reflects the `IDailyRewardService` can-claim read). Non-tautological: assert the X/67 ticks with real discoveries, not a hard-coded string.
    - The ENTER AR HUNT CTA descriptor is `ChunkyButtonKind.Primary` + ALL-CAPS label; the credit-pill descriptor's `TapToShop` is true and routes to the Shop intent.
    - `ArHudPresenter`: the action-bar includes the SLAY descriptor (red + icon + "SLAY" — AC-3 of 6.2's never-alone rule still holds here), and the credit pill tap exposes the `NavigateToShop` intent.
  - [ ] **Headless gate:** the EditMode suite must pass; current baseline is **518/518** (post-6.2). Report the new count.

## Dev Notes

### Sanctioned-deviation matrix (the CR noise filter — keep this exhaustive)

| # | Deviation | Why it is sanctioned | Source |
|---|---|---|---|
| D1 | `AppStateMachine` lives in `Veilwalkers.App`, NOT a Core service, NOT `Veilwalkers.UI`. | architecture.md:462–464 — "AppStateMachine lives in the App/Bootstrap tier (not a Core service)… the only thing that arbitrates UI ↔ service control flow." App is below UI in the acyclic graph; App can't reference UI. | architecture.md:462–464; AcyclicDependencyTests |
| D2 | The Home/AR-Hud **presenters** live in `Veilwalkers.UI`, separate from the state machine. | They compose the Story-6.2 chunky descriptors, which live in `Veilwalkers.UI`. App can't reference UI, so this composition MUST be UI-tier. The [[ui-presenter-pattern]] split (presenter + EditMode tests; thin view deferred). | ui-presenter-pattern memory; 2.4/2.5 precedent |
| D3 | No scene assets, no MonoBehaviour view logic, no AR-rig render. Logic-only. | Every prior UI story (2.4, 2.5, 4.7) deferred the MonoBehaviour view + scene to keep the headless EditMode gate honest. The Home/AR scenes + the "floating islands, never docked" layout + the re-anchor render are 6.4/6.5 + the device AR-rig scene. | ui-presenter-pattern; deferred-work.md (3.4/3.5/4.x Epic-6 view defers) |
| D4 | The state machine takes a narrow `IEncounterSnapshotPort` (3 members), not the whole `EncounterService`. | `EncounterService` is STILL a deferred Bootstrap seam (its `MonsterDatabase.asset` is unauthored — Bootstrap.cs:286–343). A narrow port lets 6.3 wire the snapshot CALL SITES + test them with a fake, without un-blocking the MonsterDatabase deferral. If Encounter is unregistered at boot, the port is a graceful no-op (the CodexService-consumer degrade precedent). | Bootstrap.cs:317–343; codex-service-lock-tier memory |
| D5 | "AR Hunt chrome floats as islands, never docked" (AC-3) has **no headless assertion**. | It is a pure layout/scene property with no branching logic worth testing (architecture.md:466–467 "AR contains no branching logic worth testing"). 6.3 records it as a deferred view note rather than inventing a fake test. Honest silent-cap disclosure per the CR convention. | architecture.md:466–467; bmad-code-review-pattern |
| D6 | The safety-warning fire on cold AR entry is NOT re-implemented; 6.3 only routes `ColdEntry` vs `ShopResume`. | The FR-3 gate (`ArSafetyGate`, Story 3.2) and the re-anchor beat (3.5) are already built. 6.3's job is to pass the right `ArEntryKind` so the warning fires on cold entry and does NOT re-fire on Shop-resume (architecture.md:482–486). | architecture.md:482–486; ArSafetyGate (3.2) |
| D7 | `SampleScene` is NOT removed in this story. | The deferred-work `SampleScene` item (line 309) is owned by "when Home lands" — but Home is a *scene*, deferred to 6.4/6.5. Removing it in a logic-only story leaves no loadable replacement and breaks the player build. Re-pointed to 6.4/6.5. | deferred-work.md:309 |
| D8 | The top-up "non-alarming message" + the Shop return-to-context *pixels* are NOT built. | 5.4/5.2 deferred the calm-message + top-up-sheet pixels to Epic 6 UI. 6.3 carries the DATA (the captured `InsufficientCreditsEvent`, the return surface) the calm message needs; the on-brand copy/treatment is Story 6.5 (diegetic voice). | deferred-work.md:56, 159; Story 6.5 |

### Seams this story inherits (from deferred-work.md + prior stories)

- **Wire any 6.2 component to a live service / navigation (deferred-work.md:9):** the credit-pill `TapToShop` intent, the live balance binding, the Codex-grid binding, the pack-card Play-price binding, and the `OnInsufficientCredits`→top-up navigation are ALL 6.3. 6.2 only DECLARED the affordances on the descriptors; 6.3 wires them to services via the presenters + the state machine. *(The pack-card Play-price binding specifically belongs to the Shop surface — if a Shop presenter is in scope it lives here; otherwise note it moves with the Shop surface presenter.)*
- **`ChunkyButtonStyle.DisplayLabel` per-read `ToUpperInvariant()` alloc (deferred-work.md:11):** 6.2 sanctioned compute-on-read; the per-frame caching belongs with 6.3's widget binding. The presenter may cache the computed label in the view-model (built once per state change), settling this.
- **AR-rig re-anchor render (deferred-work.md:190):** 3.5 decides WHERE (`TryRestore` out-Pose); the actual re-parenting of the scene GameObject is the AR rig scene. 6.3 owns the `RehydrateFromSnapshot()` call site (logical restore); the scene render stays deferred (D3).
- **The MaterializationView `_reducedMotion` stub + the Nightveil VFX flag (deferred-work.md:85, 106):** Epic-6 render-layer items; NOT 6.3 (those are 6.5/6.6 + the VFX layer). Do not pull them in.

### Architecture compliance (must-follow guardrails)

- **Service-locator rule (architecture.md:456–459):** `AppStateMachine` is App-tier and *may* read the locator, BUT prefer ctor-injection for testability (the App.Tests construct it directly with fakes). The **presenters** (UI tier) are ctor-injected too (the CodexGridPresenter precedent — the *View* reads `GameServices.Get`, the presenter does not).
- **Events: symmetric subscribe/unsubscribe (architecture.md:514–515):** the state machine subscribes to `OnInsufficientCredits` and MUST expose a `Dispose()` that unsubscribes (an acceptance criterion for UJ-2 — guards double-fire/leaks). Views subscribe in OnEnable / unsubscribe in OnDisable (the View layer, deferred — but the presenter/state-machine contract must make that possible).
- **Insufficient-credits one-way rule (architecture.md:478–480):** services raise `OnInsufficientCredits`; they NEVER open Shop / call Billing / call UI. The state machine (App) decides Shop. This is exactly AC-2 — do not let the presenter or a service short-circuit it.
- **Cold-entry safety rule (architecture.md:482–486):** only `ArEntryKind.ColdEntry` fires the full FR-3 warning; `ShopResume` does not. Getting this wrong stacks two interruptions onto one monster (the explicit anti-pattern the architecture calls out).
- **NFR-3 graceful (architecture.md:474, 509):** an illegal navigation request is a no-op + log, never a throw/crash. A null/unresolved snapshot port degrades, never crashes.
- **`GameLog` for all diagnostics** (`Veilwalkers.Core`) — never `Debug.Log` directly (the project convention; `using Veilwalkers.Core;` is the recurring first-compile-fix).

### Source tree components to touch

**NEW (`Veilwalkers.App`):**
- `Assets/Veilwalkers/App/AppSurface.cs` (enum)
- `Assets/Veilwalkers/App/ArEntryKind.cs` (enum) — or co-locate with AppStateMachine
- `Assets/Veilwalkers/App/AppStateMachine.cs`
- `Assets/Veilwalkers/App/IEncounterSnapshotPort.cs` (the narrow seam; adapter onto EncounterService)

**NEW (`Veilwalkers.UI`):**
- `Assets/Veilwalkers/UI/Home/HomePresenter.cs` + `HomeViewModel.cs`
- `Assets/Veilwalkers/UI/ArHud/ArHudPresenter.cs` + `ArHudViewModel.cs`

**UPDATE:**
- `Assets/Veilwalkers/App/Bootstrap.cs` — construct + register `AppStateMachine`, wire the `OnInsufficientCredits` subscription + the Encounter snapshot port (graceful no-op if Encounter unregistered). **Read it fully first (already done in story-prep — it heavily anticipates 6.3).**

**NEW TESTS:**
- `Assets/Tests/EditMode/App.Tests/AppStateMachineTests.cs`
- `Assets/Tests/EditMode/UI.Tests/HomePresenterTests.cs`
- `Assets/Tests/EditMode/UI.Tests/ArHudPresenterTests.cs`

**DO NOT TOUCH (sanctioned no-change):** `AcyclicDependencyTests` matrix (App→Economy/Encounter and UI→App already present — confirm, don't edit unless a genuinely new edge appears); the `EconomyConfig`/schema; the deferred `EncounterService` Bootstrap seam (stays deferred — D4).

### Testing standards summary

- **Headless EditMode is the hard gate** ([[unity-headless-testing]]): Editor must be closed; never trust exit 0 — read `test-results.xml` for `result="Passed"` AND grep the log for `error CS`. Delete `test-results.xml` + `editor-test.log` before commit.
- **Fakes clone-on-save** ([[tautological-test-trap]]): reuse `FakeUiCodexProgressStore` (deep-cloning) for the CodexService-backed Home tests; the recording fake for `IEncounterSnapshotPort` must record call ORDER (snapshot-before-Shop-commit is order-sensitive).
- **`GameServices.ResetForTests()`** between tests if any test resolves via the locator (most won't — ctor-inject the fakes directly, the CodexGridPresenterTests precedent).
- **Non-tautological pins:** the X/67 ticks with real discoveries; the rejected transition leaves state unchanged AND raises no event; the Dispose'd handler does not navigate; the snapshot is called before the transition commits (order). These are the falsifiable assertions the CR's edge-case hunter will check for.

### Project Structure Notes

- `Veilwalkers.App` and `Veilwalkers.UI` asmdefs already reference every tier they need (verified). No asmdef edge change is anticipated. If dev finds one is needed, add the asmdef ref AND the `AcyclicDependencyTests` matrix row in the same change ([[unity-asmdef-nontransitive]]).
- The `LoadPhase` enum + `LoadPhaseContractTests` are the precedent for the standalone-enum + member/order-pin pattern this story reuses for `AppSurface`.
- `EncounterService.SnapshotActiveEncounter()` / `RehydrateFromSnapshot()` (Story 5.4, `Veilwalkers.Encounter`) are already built and tested — 6.3 only adds the call SITES via the port; do not re-implement snapshot persistence.

### References

- [Source: docs/epics.md#Story-6.3] — the three AC blocks (lines 877–895).
- [Source: docs/architecture.md#AppStateMachine] — lines 462–464 (App-tier, not a service; arbitrates UI↔service).
- [Source: docs/architecture.md#Insufficient-credits] — lines 478–480 (services raise; App decides Shop).
- [Source: docs/architecture.md#Safety-warning-entry-rule] — lines 482–486 (cold-entry vs Shop-resume).
- [Source: docs/architecture.md#Shop-round-trip] — lines 524–525 (snapshot before Shop; rehydrate on return).
- [Source: docs/architecture.md#Scene-flow] — lines 221, 224–225, 283 (Onboarding→Home→AR Hunt→Codex/Shop; PascalCase scenes).
- [Source: Assets/Veilwalkers/App/Bootstrap.cs] — the composition root that anticipates 6.3 throughout (esp. the AR-entry-routing TODOs and the deferred EncounterService seam at 286–343).
- [Source: Assets/Veilwalkers/App/LoadPhase.cs] — the standalone-enum + warmup-ownership precedent.
- [Source: Assets/Veilwalkers/Economy/InsufficientCreditsEvent.cs] — the payload (Cost/Balance) the arbitration carries.
- [Source: Assets/Veilwalkers/UI/Components/*] — the Story-6.2 descriptors the presenters compose (WordmarkStyle, CreditPillStyle, ChunkyButtonStyle, etc.).
- [Source: Assets/Veilwalkers/Monsters/Codex/CodexService.cs] — `DiscoveredCount`/`UniverseCount` for the X/67 label.
- [Memory: ui-presenter-pattern] — presenter + EditMode tests; thin view deferred to Epic 6.
- [Memory: unity-asmdef-nontransitive] — new cross-tier type ⇒ direct ref + matrix edge.
- [Memory: failure-path-cleanup-parity] — symmetric unsubscribe; test the cleanup path.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Debug Log References

- Headless EditMode gate (Unity 2022.3.62f3, Editor closed): pre-CR **561/561** (first run clean); post-CR-patch **564/564 passed, 0 failed, 0 `error CS`** (+46 over the 518 6.2 baseline — AppStateMachineTests 32 + HomePresenterTests 9 + ArHudPresenterTests 5; the +3 over pre-CR are the race-fix pins: synchronous-commit, already-in-Shop refresh, OverlayLevel contract). One post-patch compile-fix iteration (a missing `using System.Threading;` in the test for `ManualResetEventSlim`). Artifacts deleted before commit.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- **Altitude split realized exactly as specified.** `AppStateMachine` + the flow/entry/overlay enums + the two narrow ports (`IEncounterSnapshotPort`, `IInsufficientCreditsSource`) live in `Veilwalkers.App` (pure logic, ctor-injected, `IDisposable` for symmetric unsubscribe). The descriptor-composing presenters (`HomePresenter`, `ArHudPresenter`) live in `Veilwalkers.UI` because App is below UI in the acyclic graph and the Story-6.2 descriptors are UI-tier. No production asmdef edge added (App→Economy, UI→App already present); no acyclic-matrix edit.
- **AC-2 arbitration:** the state machine subscribes to `ICreditService.OnInsufficientCredits` (and, via the `IInsufficientCreditsSource` seam, EncounterService's same event when registered) and THE STATE MACHINE decides Shop navigation + captures the shortfall payload + return surface; services only raise. Symmetric `Dispose()` detaches both (the failure-path-cleanup-parity pin: a disposed machine does not navigate).
- **AC-3 derived:** `NavigateToShopAsync` snapshots an active AR encounter (via the port) BEFORE committing the Shop transition (order-pinned with a recording fake); `ReturnFromShopAsync` routes an AR return through `ArEntryKind.ShopResume` → rehydrate + no re-warn. Overlay is one-level-deep (`OpenSheet` twice replaces, stays `Sheet`).
- **Graceful degradation:** EncounterService is still the deferred Bootstrap seam (MonsterDatabase.asset unauthored), so the state machine is wired with `IEncounterSnapshotPort.NoEncounter` + a null second source; the UI presenters tolerate null services + before-load `Balance`/`CanClaimToday` throws (calm 0 / 0-of-67 / no-reward). Home always renders.
- **Two test-only asmdef refs added** to `Veilwalkers.UI.Tests` (`Veilwalkers.Economy`, `Veilwalkers.App`) so the UI presenter tests can fake `ICreditService`/`IDailyRewardService` and construct a real `AppStateMachine`. The `.Tests` suffix excludes these from the acyclic guard (no matrix edit — the 5.4 precedent).
- **No `SampleScene` removal, no scene assets, no MonoBehaviour views** — logic-only story (D3/D7); those move with the Home/AR scenes (6.4/6.5).
- Bootstrap class-doc updated (it no longer says "AppStateMachine lands in 6.3" — it now constructs + registers it) to keep doc-vs-code honest (the recurring CR class).

### File List

**NEW — `Veilwalkers.App` (production):**
- `Assets/Veilwalkers/App/AppSurface.cs`
- `Assets/Veilwalkers/App/ArEntryKind.cs`
- `Assets/Veilwalkers/App/OverlayLevel.cs`
- `Assets/Veilwalkers/App/IEncounterSnapshotPort.cs`
- `Assets/Veilwalkers/App/IInsufficientCreditsSource.cs`
- `Assets/Veilwalkers/App/AppStateMachine.cs`

**NEW — `Veilwalkers.UI` (production):**
- `Assets/Veilwalkers/UI/Home/HomeViewModel.cs` (new `Home/` folder)
- `Assets/Veilwalkers/UI/Home/HomePresenter.cs`
- `Assets/Veilwalkers/UI/ARHud/ArHudViewModel.cs` (the PRE-EXISTING `ARHud/` folder — coexists with the 3.x AR views ArSafetyView/ArSessionView/ArPlacementView/CameraPermissionView; no new folder meta)
- `Assets/Veilwalkers/UI/ARHud/ArHudPresenter.cs`

**UPDATE (production):**
- `Assets/Veilwalkers/App/Bootstrap.cs` — construct + register `AppStateMachine`; class-doc accuracy fix.

**NEW — tests:**
- `Assets/Tests/EditMode/App.Tests/AppStateMachineTests.cs`
- `Assets/Tests/EditMode/UI.Tests/HomePresenterTests.cs`
- `Assets/Tests/EditMode/UI.Tests/ArHudPresenterTests.cs`

**UPDATE (tests):**
- `Assets/Tests/EditMode/UI.Tests/Veilwalkers.UI.Tests.asmdef` — added `Veilwalkers.Economy` + `Veilwalkers.App` test-only refs.

**NEW (.meta files):** one per new `.cs` above + a folder meta for the new `Assets/Veilwalkers/UI/Home/` (the `ARHud/` folder already existed — its meta was left untouched).

## Review Findings

Adversarial 3-layer CR (Blind Hunter / Edge Case Hunter / Acceptance Auditor → verify-disputed-first triage), 2026-06-16: **14 raw → 14 deduped → 8 confirmed (all PATCHED), 0 deferred, 3 spec-sanctioned, 3 false-positive, 0 AC violations.** All 8 confirmed findings converged on ONE root design flaw I introduced — the shortfall handler drove navigation through an `async` fire-and-forget, so the Shop commit raced behind the snapshot `await` (a flaw my own spec line warned against). The fix was a structural split: **navigation is committed SYNCHRONOUSLY** in the shortfall handler (`EnterShopSync` — `Current` is authoritative the instant the event returns, AC-2), and the encounter **snapshot runs as observed fire-and-forget AFTER** (durability; it reads the unchanged encounter state, so commit-then-snapshot is durability-equivalent). This eliminated the ordering hazard, the TOCTOU window, and the dispose-during-await window at once.

- [x] [Review][Patch] **Shortfall→Shop race (major).** `HandleInsufficientCredits` fire-and-forgot `OpenShopAsync`, leaving `Current == ArHunt` until the snapshot continuation resumed. → Refactored: `EnterShopSync` commits Shop synchronously; the snapshot is kicked after. New pin `Credit_shortfall_from_active_AR_encounter_commits_Shop_synchronously` asserts `Current == Shop` with no await + the snapshot still runs. [Assets/Veilwalkers/App/AppStateMachine.cs]
- [x] [Review][Patch] **Dispose-during-await window (major).** A snapshot await could outlive `Dispose()` and still commit navigation. → Navigation is now synchronous (no commit in the async path); the off-thread snapshot re-checks `_disposed` and only touches the encounter port, never navigation state. [Assets/Veilwalkers/App/AppStateMachine.cs]
- [x] [Review][Patch] **Ambient `TaskScheduler.Current` fire-and-forget (minor).** → The snapshot now uses `Task.Factory.StartNew(…, TaskScheduler.Default).Unwrap()` with a self-contained try/catch (every outcome observed; the raiser's context is not assumed). [Assets/Veilwalkers/App/AppStateMachine.cs]
- [x] [Review][Patch] **Stale doc cref `AppStateMachine.NavigateToShop` (nit).** → Fixed to `NavigateToShopAsync`. [Assets/Veilwalkers/App/IEncounterSnapshotPort.cs]
- [x] [Review][Patch] **`ReturnFromShopAsync` cleanup-parity (minor).** Cleared `Overlay`/`PendingShortfall` BEFORE the resume await. → Moved the clears AFTER the committed return (a future failed return can't leave half-cleared state — the failure-path-cleanup-parity rule). [Assets/Veilwalkers/App/AppStateMachine.cs]
- [x] [Review][Patch] **"Already in Shop" return semantics (minor).** A second shortfall while shopping refreshed `PendingShortfall` + raised `OnTopUpRequested` but returned `false` (read as "nothing happened"). → Returns `true` when an observable refresh occurs. New pin `Second_shortfall_while_already_in_Shop_refreshes_the_context` (refresh, no re-commit, single re-raise). [Assets/Veilwalkers/App/AppStateMachine.cs]
- [x] [Review][Patch] **TOCTOU between snapshot check and Shop commit (minor).** → Dissolved by the sync-commit refactor: navigation commits before the snapshot, and the snapshot reads encounter (not surface) state, so no check-await-commit window exists. [Assets/Veilwalkers/App/AppStateMachine.cs]
- [x] [Review][Patch] **`ArHudPresenter.MapOverlay` two-value assumption (minor).** A future `OverlayLevel` member would silently fold to `None`. → Added the `OverlayLevel_has_exactly_none_and_sheet` enum-contract pin (fails first if a member is added). [Assets/Tests/EditMode/App.Tests/AppStateMachineTests.cs]

**Spec-sanctioned (3, no change):** the fire-and-forget pattern for the sync event handler (Dev-Notes-line-63 defensive-exception guidance — now hardened by the sync-commit split); the `EnterArHuntAsync` implicit-else for the bounded `ArEntryKind` (the LoadPhase precedent + the contract pin); `ReturnFromShopAsync` passing `ShopReturnSurface` to `Reject` (NFR-3 graceful; the surface is construction-constrained to Home/ArHunt).

**False-positive (3, verified):** "`ReturnFromShopAsync` mutates before precondition" (line-number misread — the precondition returns first); "already-in-Shop double-raise" (an early `return` precludes the second raise); "`HomePresenter` should catch broad `Exception`" (`DiscoveredCount` throws only `InvalidOperationException` by the `RequireModel` contract — catching broad would mask real bugs).

No `deferred-work.md` entries — 0 deferrals this CR.
