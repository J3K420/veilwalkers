# Story 6.4: Onboard a new player and disclose the camera

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

baseline_commit: cc7252f587a10576a2dadba1ccfdf89e02acd7ab

## Story

As a new player,
I want a short, on-brand onboarding that explains the premise and the camera before granting my Credits,
so that I understand the game and the camera use before hunting.

## Acceptance Criteria

Sourced verbatim-in-intent from `docs/epics.md#Story-6.4` (lines 897–913) and `docs/architecture.md` (lines 25, 89, 101, 221, 223, 283, 502, 575–584). The 4th Epic-6 story (Visual Identity, Navigation & Onboarding Shell); it CONSUMES the 6.1 tokens + 6.2 chunky kit + 6.3 `AppStateMachine` gate and SUPPLIES the Onboarding-surface CONTENT the 6.3 gate deliberately deferred.

**AC-1 — Premise cards convey the world**
**Given** a first launch
**When** onboarding runs
**Then** **2–3 chunky premise cards** convey the premise (the Veil is real / monsters are real / you're a Veilwalker), composed from the Story-6.2 chunky kit + 6.1 tokens (no re-declared hex/copy-baked layout).

**AC-2 — Camera-disclosure card precedes the OS dialog**
**Given** onboarding is running
**When** the player advances past the premise cards
**Then** a **camera-disclosure card appears BEFORE the OS dialog** (FR-5, architecture.md:223) — i.e. the disclosure step is modeled as a distinct pre-prompt state, NOT a side effect of the OS prompt. This reuses the already-built Story-3.1 `CameraPermissionFlow` (`CameraPermissionState.Disclosure` precedes `Requesting`); 6.4 does NOT re-implement permission logic.

**AC-3 — The 20-Credit grant is presented with a coin-burst, then routes to ENTER AR HUNT**
**Given** the premise + disclosure have been seen
**When** onboarding completes
**Then** the **20-Credit grant (Story 1.7 `FirstLaunchGrant`) is PRESENTED with a chunky coin-burst** (the grant itself is already performed by `FirstLaunchGrant.RunAsync()` at boot — 6.4 surfaces the granted balance + the burst affordance, it does NOT re-grant), **then onboarding routes to ENTER AR HUNT** by calling `AppStateMachine.CompleteOnboarding()` (Onboarding → Home) and surfacing the **ENTER AR HUNT** intent (cold AR entry from Home is 6.3's `EnterArHuntAsync(ArEntryKind.ColdEntry)`).

**AC-4 — AR Mode is gated until onboarding completes**
**Given** onboarding is incomplete
**When** the player tries to reach AR Mode
**Then** **AR Mode is not reachable until onboarding completes** (architecture.md:221 — the AR-unreachable-until-onboarding gate). This gate is ALREADY ENFORCED by 6.3's `AppStateMachine` (`EnterArHuntAsync(ColdEntry)` is rejected while `!OnboardingComplete`, and `CompleteOnboarding()` is the only exit from `Onboarding`). 6.4's job is to ensure onboarding's terminal step is the call to `CompleteOnboarding()` — i.e. there is NO path that lands the player in AR without first driving the onboarding flow to completion. 6.4 ADDS the regression pin proving the gate holds for an incomplete onboarding flow; it does NOT re-implement the gate.

### Derived / load-bearing requirements (not separately numbered but required for the feature to work end-to-end)

- **AR session prewarm ownership (architecture.md:575–584, the "Warmup ownership rule (critical)"):** onboarding/Home do NOT warm the AR session themselves — they call **`ArSessionService.PrewarmAsync()`** so that by the time the player taps "Enter AR Hunt" the session + ≥1 Monster are warm (the cold-start budget is launch → first plane anchor, not launch → camera). 6.4 owns the onboarding-side PREWARM CALL SITE (the presenter exposes the prewarm intent / the onboarding flow kicks it during the premise cards). **If `ArSessionService` is still a deferred Bootstrap seam (likely — AR-rig scene unbuilt), 6.4 wires the call through a narrow port that degrades to a no-op (the 6.3 `IEncounterSnapshotPort.NoEncounter` precedent)** — do NOT block 6.4 on the AR-rig scene; record the deferral.
- **Onboarding completion is idempotent + once-per-install in spirit, but the GATE state is per-process (6.3).** `AppStateMachine.CompleteOnboarding()` is already idempotent (a second call past onboarding is a no-op + warn). The "should onboarding RE-RUN on a later launch?" decision is a SEPARATE persistence question: the architecture's first-launch marker is `SaveModel.StartingCreditsGranted` (1.7) — onboarding content (premise/disclosure) re-showing is a UX/persistence decision. **6.4 scope: drive the onboarding FLOW + wire `CompleteOnboarding()`. Whether the onboarding SCREENS re-show on a returning install (a persisted "onboarding seen" flag distinct from the credit-grant marker) is an explicit decision — see Decision A in Dev Notes.**
- **The disclosure card precedes the grant AND the AR entry (ordering).** The flow order is: premise cards → camera-disclosure card (pre-OS-prompt) → [OS prompt fires on continue] → coin-burst grant presentation → `CompleteOnboarding()` → ENTER AR HUNT. The disclosure must come BEFORE the coin-burst routes to AR (you disclose the camera before you send the player toward camera use). Pin this ordering as a falsifiable state-progression test.

## Tasks / Subtasks

> **Altitude split (the load-bearing decision — identical posture to 6.3).** `Veilwalkers.UI` references `Veilwalkers.App` (App is BELOW UI in the acyclic graph — see `AcyclicDependencyTests`). The onboarding PRESENTER composes Story-6.2 chunky descriptors (which live in `Veilwalkers.UI`) AND reads the 6.3 `AppStateMachine` / 1.7-granted balance, so it lives in **`Veilwalkers.UI`**. The split is:
> - **`OnboardingFlow` (the pure-logic step state machine: PremiseCard[i] → Disclosure → Grant → Complete) → `Veilwalkers.UI`** — OR a thin `OnboardingState` enum + flow if the camera-disclosure step is delegated to the existing `CameraPermissionFlow`. Tested in `Veilwalkers.UI.Tests`.
> - **`OnboardingPresenter` + `OnboardingViewModel` → `Veilwalkers.UI`** (composes premise-card descriptors + the disclosure-card descriptor + the coin-burst descriptor + the ENTER-AR-HUNT CTA; reads `AppStateMachine` for `CompleteOnboarding`, `ICreditService` for the granted balance, the AR-prewarm port). Pure logic; returns a readonly view-model. Tested in `Veilwalkers.UI.Tests`.
> - **The thin `OnboardingView : MonoBehaviour` + the `Onboarding.unity` scene + the actual coin-burst ANIMATION / card tween render → deferred to Story 6.5** (logic-free views; visual/scene/motion wiring). 6.4 is **headless logic only**, like every prior UI story ([[ui-presenter-pattern]]).

- [x] **Task 1 — `OnboardingStep` model (AC-1/AC-2/AC-3)** `Veilwalkers.UI`
  - [x] Add a standalone `public enum OnboardingStep { Premise, Disclosure, Grant, Complete }` (PascalCase; standalone, not nested — mirror `AppSurface`/`CameraPermissionState` so the UI.Tests can assert members/order without flow accessibility). The `Premise` step covers the 2–3 premise cards (a card index inside the flow, not separate enum members — keep the enum about phase, not card count).
  - [x] (If a premise-card kind is needed) add a small `PremiseCard` value type or an indexed array of premise-card descriptors inside the view-model — see Task 3.

- [x] **Task 2 — `OnboardingFlow` step state machine (AC-1→AC-3, AC ordering)** `Veilwalkers.UI`
  - [x] `public sealed class OnboardingFlow` — pure logic, ctor-injected. It owns the STEP progression only (Premise → Disclosure → Grant → Complete); it does NOT own camera-permission logic (delegate to the 3.1 `CameraPermissionFlow`) and does NOT own the credit grant (1.7 `FirstLaunchGrant`).
  - [x] `public OnboardingStep Step { get; private set; }` starting at `Premise`.
  - [x] `public int PremiseCardIndex { get; private set; }` + `AdvancePremise()` to walk the 2–3 premise cards; when the last card is advanced, the step moves to `Disclosure`. The premise card COUNT (2 or 3) is a content constant on the flow/presenter (provisional — see Decision B); pin "all premise cards are walked before Disclosure," not the exact count, so a content tweak stays green.
  - [x] `AdvanceFromDisclosure()` → moves to `Grant` (the camera disclosure has been acknowledged; the OS prompt fire is delegated — see Task 4). `AdvanceFromGrant()` → moves to `Complete` and is the step that triggers `AppStateMachine.CompleteOnboarding()` (Task 5).
  - [x] Illegal/out-of-order advances are **rejected** (no state change; `GameLog.Warn`; return a typed `bool`) — NFR-3 graceful, never throw on a flow no-op (the `AppStateMachine` transition-rejection precedent).
  - [x] **Ordering invariant (the derived AC):** Disclosure MUST be reached before Grant, and Grant before Complete. Encode it in the step progression (you cannot skip Disclosure to Grant). Pin a non-tautological state-progression test that asserts the only legal order.

- [x] **Task 3 — `OnboardingPresenter` + `OnboardingViewModel` (AC-1/AC-2/AC-3 composition)** `Veilwalkers.UI`
  - [x] `public sealed class OnboardingPresenter` — ctor-injected with `AppStateMachine` (for `CompleteOnboarding`), `ICreditService` (for the granted balance the coin-burst presents), and the AR-prewarm port (Task 6). Pure logic; returns an `OnboardingViewModel`. Null-safe / degrade-gracefully if a service is unresolved (the `HomePresenter` "render even while a service is an unregistered Bootstrap seam" precedent).
  - [x] `OnboardingViewModel` (readonly struct, the `HomeViewModel` precedent) composes the Story-6.2 descriptors per current `OnboardingStep`:
    - **Premise cards (AC-1):** 2–3 card descriptors. Reuse `ChunkyStyle.Elevated(PumpkinPatchTokens.Surface, PumpkinPatchTokens.RadiusLg)` for the card chrome (the `PackCardStyle` lg-sticker precedent) + the premise copy + an advance CTA (`ChunkyButtonStyle.For(ChunkyButtonKind.Primary, <label>)`). If 6.2 has no premise-card descriptor (it does not — only `PackCardStyle` exists), add a minimal `PremiseCardStyle` (Surface fill, RadiusLg, hard-shadow via `ChunkyStyle.Elevated`) following the 6.2 descriptor pattern — body-text via `PumpkinPatchTokens.TextPrimary`, NOT a tier color (the 6.1 AC-4 chrome-never-tier rule).
    - **Camera-disclosure card (AC-2):** surfaced ONLY when `Step == Disclosure`. Plain, legible, high-contrast disclosure copy (the safety/disclosure-copy exception — architecture.md:223; UX safety copy is plain, never buried in flavor) + a Continue CTA whose tap routes to `CameraPermissionFlow.ConfirmDisclosureAndRequest()` (3.1) THEN `OnboardingFlow.AdvanceFromDisclosure()`. The presenter EXPOSES the intent; the View wires the tap.
    - **Coin-burst grant presentation (AC-3):** surfaced when `Step == Grant`. Reads the granted balance via `ICreditService` (the +20 already landed by `FirstLaunchGrant` at boot) and composes a `CreditPillStyle.For(balance)` (gold) + a coin-burst affordance. If 6.2/6.1 has no coin-burst descriptor (it does not), add a minimal `CoinBurstStyle` value (the burst is a VIEW animation; the descriptor models the credit-gold token + the granted amount to present) — the actual burst tween is the 6.5 view-render defer.
    - **ENTER AR HUNT routing (AC-3):** when `Step == Complete`, the view-model exposes the ENTER-AR-HUNT CTA intent (routes to `AppStateMachine.EnterArHuntAsync(ArEntryKind.ColdEntry)` from Home after `CompleteOnboarding()` lands the player on Home). The presenter exposes the intent; the View wires the tap.
  - [x] `Build()` is a pure recompute that reads live state once and returns a fresh model (the `HomePresenter.Build()` precedent), never throws (before-load `Balance` read guarded with try/catch → calm 0).

- [x] **Task 4 — Camera-disclosure delegation (AC-2)** `Veilwalkers.UI`
  - [x] Do NOT re-implement permission logic. The `OnboardingFlow` Disclosure step DELEGATES to the existing `CameraPermissionFlow` (Story 3.1, `Veilwalkers.AR`): the disclosure CARD corresponds to `CameraPermissionState.Disclosure`; the Continue tap calls `CameraPermissionFlow.ConfirmDisclosureAndRequest()` (which fires the OS prompt — AC-2's "BEFORE the OS dialog" is the `Disclosure` state existing as a distinct pre-prompt phase). **VERIFIED: `Veilwalkers.UI → Veilwalkers.AR` is ALREADY a sanctioned edge** (`Veilwalkers.UI.asmdef` line 10 + `AcyclicDependencyTests` matrix line 80) — the production presenter/flow may reference `CameraPermissionFlow`/`CameraPermissionState` directly, **NO new production asmdef ref or matrix row is needed**. The ONLY asmdef consideration is the TEST asmdef: `Veilwalkers.UI.Tests` does NOT yet reference `Veilwalkers.AR` — add that **test-only** ref ONLY IF a test constructs a real `CameraPermissionFlow` (the `.Tests` suffix excludes it from the acyclic matrix — the 5.4/6.3 precedent). Prefer faking the disclosure via a narrow `IDisclosureGate { void ConfirmAndRequest(); }` port in the flow so the tests need NO AR ref at all (Decision C — the narrow port is now about test-fakeability, NOT about avoiding a production edge that already exists).
  - [x] The denied-permission re-grant path (3.1 `CameraPermissionState.Denied` → Settings) is ALREADY built (3.1) — 6.4 does NOT re-build it; if the player denies during onboarding, the existing 3.1 re-grant flow applies. Record this as inherited, not re-implemented.

- [x] **Task 5 — Onboarding completion wires the 6.3 gate (AC-3/AC-4)** `Veilwalkers.UI`
  - [x] The `Grant` → `Complete` advance (`AdvanceFromGrant()`) calls `AppStateMachine.CompleteOnboarding()` exactly once (it is idempotent, but the flow should call it once — pin "called exactly once on completion"). After it returns `true`, `AppStateMachine.Current == Home`.
  - [x] AC-4 regression pin: with onboarding INCOMPLETE (flow not driven to `Complete`, so `CompleteOnboarding()` never called), `AppStateMachine.EnterArHuntAsync(ArEntryKind.ColdEntry)` is rejected (`Current` stays `Onboarding`, returns false). This proves "AR Mode not reachable until onboarding completes" end-to-end through the 6.4 flow + the 6.3 gate (composition pin, not a re-test of 6.3's internal gate). Use a real `AppStateMachine` constructed with fakes (the 6.3 `IEncounterSnapshotPort.NoEncounter` + a fake `ICreditService`).

- [x] **Task 6 — AR session prewarm call site (derived: architecture.md:575–584)** `Veilwalkers.UI`
  - [x] The onboarding flow KICKS `ArSessionService.PrewarmAsync()` during the premise cards (the warmup-ownership rule: Onboarding/Home call prewarm; they do not warm the session themselves). Define a narrow `IArPrewarmPort { Task PrewarmAsync(); }` (the 6.3 narrow-port precedent) adapted onto `ArSessionService` in Bootstrap — OR, if `ArSessionService` is unregistered (AR-rig scene deferred), wire a `NoOp` prewarm port that degrades gracefully (the `IEncounterSnapshotPort.NoEncounter` precedent). Pin: the prewarm port is called once during onboarding (fire-and-forget is fine — prewarm is best-effort; observe/guard the task like the 6.3 snapshot fire-and-forget so a prewarm fault never crashes onboarding). **Do NOT block 6.4 on the AR-rig scene; record the prewarm-wiring deferral if the real `ArSessionService` is still seam-blocked.**

- [x] **Task 7 — Bootstrap wiring (AC-3/derived)** `Veilwalkers.App`
  - [x] If the `OnboardingPresenter` needs live registration (so the future `OnboardingView` resolves it), register it OR — preferably — let the deferred `OnboardingView` construct the presenter directly from resolved services (the `MaterializationView` constructs-its-presenter precedent), since the presenter is ctor-injected and the View is deferred to 6.5. **Decide and document (Decision D): no Bootstrap change if the deferred View owns construction.**
  - [x] Confirm `FirstLaunchGrant.RunAsync()` is already invoked at boot (Story 1.7) — 6.4 does NOT change the grant timing; it only PRESENTS the result. If the boot ordering means the grant runs before the player sees onboarding (it does — `FirstLaunchGrant` runs after save load), the coin-burst presents an already-granted balance, which is correct (the burst is celebratory presentation, not the grant moment). Document this ordering so the dev does not "move" the grant into onboarding.
  - [x] Do NOT change the acyclic matrix. `Veilwalkers.UI → {App, Economy, AR, Monsters, Persistence, Billing, Encounter, Core}` are ALL already present (asmdef + matrix line 76–82, VERIFIED) — the camera-disclosure delegation (Task 4) and the AR-prewarm port (Task 6) reuse the EXISTING `UI → AR` edge, so NO production asmdef ref or matrix row is needed. The only possible asmdef change is a TEST-only `UI.Tests → AR` ref IF a test wires the real `CameraPermissionFlow` (excluded from the matrix by the `.Tests` suffix). ([[unity-asmdef-nontransitive]] applies only to that test-only case.)

- [x] **Task 8 — Tests (all ACs)** `Veilwalkers.UI.Tests`
  - [x] **`OnboardingFlowTests`:**
    - AC ordering: the only legal step order is Premise → Disclosure → Grant → Complete; an out-of-order advance is rejected (no state change, returns false) — **non-tautological**: assert the rejected advance left `Step` unchanged.
    - AC-1: all premise cards are walked (`AdvancePremise()` N times) before `Step` becomes `Disclosure` — assert the transition happens after the LAST card, not the first (pin the walk, not a hard-coded count).
    - AC-2: Disclosure is reached BEFORE Grant (cannot skip).
    - AC-3: `AdvanceFromGrant()` calls `AppStateMachine.CompleteOnboarding()` exactly once and `Current → Home` after.
    - Enum pins: `OnboardingStep` members + order (the `LoadPhaseContractTests`/`AppSurface` precedent).
  - [x] **`OnboardingPresenterTests`:**
    - AC-1: `OnboardingViewModel` at `Step == Premise` carries 2–3 premise-card descriptors sourced from `PumpkinPatchTokens` (Surface fill, RadiusLg, hard-shadow blur==0 — the 6.1 elevation invariant) + the premise copy; the advance CTA is `ChunkyButtonKind.Primary`.
    - AC-2: at `Step == Disclosure`, the view-model surfaces the disclosure card with PLAIN high-contrast copy (`TextPrimary`, not muted, not a tier color — the safety-copy-is-plain rule) + the Continue intent.
    - AC-3: at `Step == Grant`, the view-model surfaces a `CreditPillStyle.For(20)` (or the live granted balance from a fake `ICreditService` returning 20) + the coin-burst affordance; at `Step == Complete`, the ENTER-AR-HUNT CTA intent is exposed. Non-tautological: the presented amount ticks with the fake balance (20 → assert 20; a fake at 50 → assert 50), not a hard-coded literal.
    - Graceful degrade: a null `ICreditService` (before-load) yields a calm 0 in the coin-burst presentation, no throw (the `HomePresenter` null-service precedent).
  - [x] **AC-4 composition pin (in `OnboardingFlowTests` or a small `OnboardingGateTests`):** a real `AppStateMachine` (fakes for ports) + the onboarding flow NOT driven to `Complete` ⇒ `EnterArHuntAsync(ColdEntry)` rejected, `Current` stays `Onboarding`; THEN drive the flow to `Complete` ⇒ `Current == Home` and a subsequent `EnterArHuntAsync(ColdEntry)` is accepted. The end-to-end gate proof.
  - [x] **Headless gate:** the EditMode suite must pass; current baseline is **564/564** (post-6.3). Report the new count.

## Dev Notes

### Decisions to make (call them explicitly in the Dev Agent Record)

| # | Decision | Default / recommendation | Why |
|---|---|---|---|
| A | Does onboarding re-show on a later launch (a persisted "onboarding seen" flag distinct from `SaveModel.StartingCreditsGranted`)? | **Default: NO new persisted flag in 6.4.** Drive the onboarding FLOW + the `CompleteOnboarding()` gate; the per-launch "should the screens re-show" persistence is a UX/persistence decision that can ride `StartingCreditsGranted` (already false→true once-per-install) OR a future explicit `OnboardingSeen` flag. If you add a flag, it is a `SaveModel` additive bool (the `StartingCreditsGranted`/`GuaranteedRareLures` additive-int precedent — NO schema bump, deserializes to false on old saves). Recommend deferring the persisted-reshow flag unless the AC demands it (it does not — AC-1 says "a first launch"). | AC-1 scopes to "first launch"; the grant marker already gates once-per-install; adding a second persisted flag is scope the AC doesn't require. Keep 6.4 logic-only + flow-only. |
| B | The premise card COUNT (2 or 3). | **Default: 3** (the Veil is real / monsters are real / you're a Veilwalker — the epics.md AC names three beats). Make it a content constant; pin the walk, not the count. | The AC says "2–3"; three beats are named. A content tweak must not break tests, so test the walk. |
| C | Camera disclosure: whole `CameraPermissionFlow` reference vs a narrow `IDisclosureGate` port. | **Default: narrow `IDisclosureGate` port for test-fakeability (NOT for asmdef reasons).** `UI → AR` is ALREADY a sanctioned production edge (asmdef line 10 + matrix line 80, VERIFIED), so the production flow MAY reference `CameraPermissionFlow` directly. The narrow port's only remaining benefit is letting `UI.Tests` fake disclosure without adding a test-only `UI.Tests → AR` ref. Either is legal; the narrow port keeps the test graph minimal. | Decision is now ergonomics, not architecture — the production edge exists. ([[unity-asmdef-nontransitive]] still applies only if a test wires the real flow ⇒ add the test-only AR ref.) |
| D | `OnboardingPresenter` Bootstrap registration vs deferred-View-constructs-it. | **Default: deferred View constructs it (no Bootstrap change), the `MaterializationView` precedent.** | The View is deferred to 6.5; the presenter is ctor-injected; no live registration is needed until the View exists. Avoid a Bootstrap edit for a logic-only story. |

### Sanctioned-deviation matrix (the CR noise filter — keep this exhaustive)

| # | Deviation | Why it is sanctioned | Source |
|---|---|---|---|
| D1 | The `OnboardingPresenter`/`OnboardingFlow` live in `Veilwalkers.UI`, NOT `Veilwalkers.App`. | They compose Story-6.2 chunky descriptors (UI-tier) + read the granted balance. App is below UI in the acyclic graph; App can't reference UI. The [[ui-presenter-pattern]] split (presenter + EditMode tests; thin view deferred). | 6.3 D2; ui-presenter-pattern; AcyclicDependencyTests |
| D2 | No scene assets, no MonoBehaviour view logic, no coin-burst ANIMATION, no card tween render. Logic-only. | Every prior UI story (2.4, 2.5, 4.7, 6.3) deferred the MonoBehaviour view + scene + motion to keep the headless EditMode gate honest. The `Onboarding.unity` scene + the coin-burst tween + the card-swipe animation are Story 6.5 (state treatments / diegetic motion) + the device build. | ui-presenter-pattern; 6.3 D3; deferred-work.md (Epic-6 view defers) |
| D3 | 6.4 does NOT re-grant the 20 Credits; it PRESENTS the balance `FirstLaunchGrant` (1.7) already granted at boot. | The grant policy + once-per-install marker live in `FirstLaunchGrant` (App, Story 1.7). Moving the grant into onboarding would duplicate the battle-tested grant pipeline + the `StartingCreditsGranted` gate. The coin-burst is celebratory PRESENTATION of an already-granted balance. | FirstLaunchGrant.cs:62 (RunAsync); architecture.md:502 |
| D4 | 6.4 does NOT re-implement camera permission; it DELEGATES to the 3.1 `CameraPermissionFlow` (`CameraPermissionState.Disclosure` IS the pre-OS-prompt card). | Story 3.1 built the disclosure-before-prompt flow + the denied re-grant path. AC-2's "disclosure BEFORE the OS dialog" is satisfied by the existing `Disclosure` state preceding `Requesting`. Re-implementing would duplicate FR-5 logic. | CameraPermissionFlow.cs; CameraPermissionState.cs; architecture.md:223 |
| D5 | AC-4's gate ("AR unreachable until onboarding completes") is NOT re-implemented; 6.4 only ensures the flow's terminal step CALLS `CompleteOnboarding()` + adds a composition regression pin. | The gate is 6.3's `AppStateMachine` (`EnterArHuntAsync(ColdEntry)` rejected while `!OnboardingComplete`; `CompleteOnboarding()` is the only exit). 6.4 wires the content side + proves the composition holds. | AppStateMachine.cs:107–123, 159–189; 6.3 Task 2 |
| D6 | The AR-session PREWARM is wired through a narrow port that may be a graceful no-op (AR-rig scene deferred). | `ArSessionService` is likely still a deferred Bootstrap seam (the AR-rig scene is unbuilt — same family as the `EncounterService` MonsterDatabase seam). 6.4 wires the call site + degrades if unregistered (the `IEncounterSnapshotPort.NoEncounter` precedent), without un-blocking the AR-rig scene. | architecture.md:575–584; 6.3 D4 |
| D7 | New minimal descriptors (`PremiseCardStyle`, `CoinBurstStyle`) may be ADDED to `Veilwalkers.UI/Components` if 6.2 has no equivalent. | 6.2 delivered `PackCardStyle` (Shop card) + `CreditPillStyle` but NO premise-card / coin-burst concept (confirmed). Adding minimal descriptors sourcing ONLY 6.1 tokens (Surface/RadiusLg/CreditGold + hard-shadow) follows the 6.2 descriptor pattern; the actual burst/card RENDER is the 6.5 view defer. | 6.2 component list; deferred-work.md:10 (6.4 consumes the kit) |

### Seams this story inherits (from deferred-work.md + prior stories)

- **Onboarding premise/disclosure/coin-burst cards are explicitly 6.4 (deferred-work.md:10).** 6.2 honored UX-DR17 at the SLAY-button descriptor level but deferred the onboarding content to 6.4. 6.4 CONSUMES the 6.2 kit (it does not rebuild components) + adds the two minimal missing descriptors (D7).
- **The 6.3 `AppStateMachine.CompleteOnboarding()` gate + the `Onboarding` start surface (6.3 Task 2, deferred-work line for 6.3).** 6.3 built the gate; 6.4 supplies the content that drives it to completion. The gate's idempotency + the "only exit" rule are 6.3's; 6.4 calls it once.
- **The 1.7 `FirstLaunchGrant.RunAsync()` grant (App-tier).** Already invoked at boot; 6.4 presents the result (D3).
- **The 3.1 `CameraPermissionFlow` disclosure-before-prompt + denied re-grant (Veilwalkers.AR).** Already built; 6.4 delegates (D4).
- **The architecture warmup-ownership rule (architecture.md:575–584).** Onboarding calls `ArSessionService.PrewarmAsync()`; 6.4 wires the call site, degrading if the AR session service is still a seam (D6).

### Architecture compliance (must-follow guardrails)

- **Service-locator rule (architecture.md:456–459):** the `OnboardingPresenter` is UI-tier and ctor-injected (the future `OnboardingView` reads `GameServices.Get`, the presenter does not — the `HomePresenter`/`CodexGridPresenter` precedent).
- **Camera disclosure BEFORE the OS prompt (architecture.md:223, FR-5):** the disclosure card is a distinct pre-prompt state (`CameraPermissionState.Disclosure`), never a side effect of firing the prompt. This is AC-2.
- **Safety/disclosure copy is plain (epics.md:926, the 6.5 voice exception applies retroactively to disclosure):** the camera-disclosure card copy is plain, legible, high-contrast — NOT buried in diegetic flavor. Use `TextPrimary` on `Surface`, never muted text for the disclosure (the contrast rule, anticipates 6.6).
- **Warmup ownership (architecture.md:575–584):** Onboarding calls `PrewarmAsync()`; it does NOT construct/warm the AR session itself. Getting this wrong re-introduces the cold-start-at-AR-entry stall the architecture explicitly designs out.
- **NFR-3 graceful (architecture.md:474, 509):** an out-of-order onboarding advance is a no-op + log, never a throw. A null/unresolved service (credit/prewarm) degrades, never crashes. Onboarding must always render.
- **Chrome never painted with a tier color (6.1 AC-4):** premise/disclosure cards use the frame palette (Surface/TextPrimary/PrimaryAccent), never `DreadScaleTokens`. The credit-gold for the coin-burst is the currency role token (`PumpkinPatchTokens.CreditGold`), correct for a credit presentation.
- **`GameLog` for all diagnostics** (`Veilwalkers.Core`) — never `Debug.Log` (the recurring first-compile-fix: `using Veilwalkers.Core;`).
- **Events: symmetric subscribe/unsubscribe (architecture.md:514–515)** — IF the onboarding presenter subscribes to anything (it likely does not — it reads on `Build()`), it must expose symmetric teardown ([[failure-path-cleanup-parity]]). The flow is poll/recompute, not event-driven, so prefer no subscription.

### Source tree components to touch

**NEW (`Veilwalkers.UI`):**
- `Assets/Veilwalkers/UI/Onboarding/OnboardingStep.cs` (enum) — new `Onboarding/` folder
- `Assets/Veilwalkers/UI/Onboarding/OnboardingFlow.cs`
- `Assets/Veilwalkers/UI/Onboarding/OnboardingPresenter.cs`
- `Assets/Veilwalkers/UI/Onboarding/OnboardingViewModel.cs`
- `Assets/Veilwalkers/UI/Components/PremiseCardStyle.cs` (IF 6.2 has no equivalent — D7)
- `Assets/Veilwalkers/UI/Components/CoinBurstStyle.cs` (IF 6.2 has no equivalent — D7)
- `Assets/Veilwalkers/UI/Onboarding/IArPrewarmPort.cs` (narrow prewarm seam — D6; co-locate or `Veilwalkers.App` if it adapts an App/AR service)
- (Optional) `Assets/Veilwalkers/UI/Onboarding/IDisclosureGate.cs` (narrow disclosure seam — Decision C, only if avoiding a `UI → AR` edge)

**UPDATE (production) — ONLY IF a decision requires it:**
- `Assets/Veilwalkers/App/Bootstrap.cs` — ONLY if Decision D chooses live registration (default: no change).
- `Assets/Veilwalkers/UI/Veilwalkers.UI.asmdef` — ONLY if Decision C references `Veilwalkers.AR` directly (add ref + matrix row).

**NEW TESTS:**
- `Assets/Tests/EditMode/UI.Tests/OnboardingFlowTests.cs`
- `Assets/Tests/EditMode/UI.Tests/OnboardingPresenterTests.cs`
- (AC-4 composition pin co-located in `OnboardingFlowTests` or a small `OnboardingGateTests.cs`)

**UPDATE (tests) — ONLY IF needed:**
- `Assets/Tests/EditMode/UI.Tests/Veilwalkers.UI.Tests.asmdef` — already references `Veilwalkers.App`/`Economy`/`UI`/`Core` (6.3 added App+Economy). Add `Veilwalkers.AR` ONLY if the disclosure test constructs a real `CameraPermissionFlow` (the `.Tests` suffix excludes it from the acyclic matrix — the 5.4/6.3 precedent).

**DO NOT TOUCH (sanctioned no-change):** `FirstLaunchGrant` (1.7 grant — present, don't re-grant); `CameraPermissionFlow`/`CameraPermissionState` (3.1 — delegate, don't re-implement); the 6.3 `AppStateMachine` gate (call `CompleteOnboarding()`, don't re-gate); the `EconomyConfig`/schema; the deferred `EncounterService`/`ArSessionService` Bootstrap seams (stay deferred — D6).

### Testing standards summary

- **Headless EditMode is the hard gate** ([[unity-headless-testing]]): Editor must be closed; never trust exit 0 — read `test-results.xml` for `result="Passed"` AND grep the log for `error CS`. Delete `test-results.xml` + `editor-test.log` before commit.
- **Fakes:** a fake `ICreditService` returning a configurable balance (20, then 50) for the coin-burst non-tautological tick; a fake `IArPrewarmPort` recording the call count; the real `AppStateMachine` constructed with `IEncounterSnapshotPort.NoEncounter` + a fake `ICreditService` (the 6.3 `AppStateMachineTests` precedent) for the AC-4 composition pin.
- **`GameServices.ResetForTests()`** between tests if any test resolves via the locator (most won't — ctor-inject the fakes directly, the `HomePresenterTests` precedent).
- **Non-tautological pins:** the premise walk reaches Disclosure after the LAST card (not the first); the rejected out-of-order advance leaves `Step` unchanged; the coin-burst amount ticks with the fake balance; `CompleteOnboarding()` is called exactly once; the AC-4 gate flips from rejected→accepted only after `Complete`. These are the falsifiable assertions the CR edge-case hunter will check for.

### Project Structure Notes

- `Veilwalkers.UI` already references `Veilwalkers.App`, `Veilwalkers.Economy`, `Veilwalkers.Monsters`, `Veilwalkers.Billing`, `Veilwalkers.Persistence`, `Veilwalkers.AR`, `Veilwalkers.Encounter`, `Veilwalkers.Core` (VERIFIED: asmdef lines 4–13 + `AcyclicDependencyTests` matrix lines 76–82). The `Veilwalkers.UI → Veilwalkers.AR` edge for the camera-disclosure delegation ALREADY EXISTS — NO new production asmdef ref or matrix row is needed. Only a test-only `UI.Tests → AR` ref may be required (Decision C), and only if a test wires the real `CameraPermissionFlow`.
- The `AppSurface`/`CameraPermissionState`/`LoadPhase` enums are the precedent for the standalone-enum + member/order-pin pattern this story reuses for `OnboardingStep`.
- `FirstLaunchGrant.RunAsync()` (Story 1.7, `Veilwalkers.App`) and `CameraPermissionFlow` (Story 3.1, `Veilwalkers.AR`) are already built + tested — 6.4 only adds the presentation/flow + the `CompleteOnboarding()` call site; do not re-implement either.
- `HomePresenter`/`HomeViewModel` (6.3, `Veilwalkers.UI/Home`) is the exact presenter+readonly-struct-viewmodel pattern to mirror.

### References

- [Source: docs/epics.md#Story-6.4] — the four AC blocks (lines 897–913).
- [Source: docs/architecture.md] — line 25 (onboarding & first-run economy FR-1/FR-2), 89 (camera disclosure before), 101 (scene/nav flow compliance gates), 221 (Onboarding → Home → AR Hunt flow), 223 (disclosure BEFORE the OS prompt, FR-5), 283 (PascalCase scenes incl. `Onboarding`), 502 (FR-1/FR-2 → App/UI/Onboarding + CreditService), 575–584 (cold-start staging + the critical warmup-ownership rule: Onboarding calls `ArSessionService.PrewarmAsync()`).
- [Source: Assets/Veilwalkers/AR/ArSessionService.cs:105] — `public Task PrewarmAsync()` (VERIFIED: exists, double-start-safe, refuses gracefully if unsupported/ungranted — the prewarm call site 6.4 wires; NFR-3 never-throws).
- [Source: Assets/Veilwalkers/UI/Veilwalkers.UI.asmdef:10 + Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs:76–82] — VERIFIED `UI → AR` (and App/Economy/etc.) already sanctioned; no new production edge for the disclosure delegation.
- [Source: Assets/Veilwalkers/App/AppStateMachine.cs] — lines 48 (`Current = Onboarding`), 50–52 (`OnboardingComplete`), 107–123 (`CompleteOnboarding()`), 159–189 (`EnterArHuntAsync` + the onboarding-incomplete rejection).
- [Source: Assets/Veilwalkers/App/FirstLaunchGrant.cs] — lines 22–140 (`RunAsync()`, `StartingCredits = 20`, the once-per-install marker) — the grant 6.4 PRESENTS, does not re-run.
- [Source: Assets/Veilwalkers/AR/CameraPermissionFlow.cs] — the disclosure-before-prompt flow 6.4 delegates to.
- [Source: Assets/Veilwalkers/AR/CameraPermissionState.cs] — `Disclosure`/`Requesting`/`Granted`/`Denied` (the pre-prompt disclosure state).
- [Source: Assets/Veilwalkers/UI/Home/HomePresenter.cs + HomeViewModel.cs] — the presenter+readonly-struct pattern to mirror.
- [Source: Assets/Veilwalkers/UI/Components/*] — the 6.2 descriptors (`ChunkyStyle.Elevated`, `ChunkyButtonStyle.For`, `CreditPillStyle.For`, `PackCardStyle`, `WordmarkStyle`) the presenter composes; NO premise-card/coin-burst exists (add minimal — D7).
- [Source: Assets/Veilwalkers/UI/Design/PumpkinPatchTokens.cs] — `Surface`/`TextPrimary`/`CreditGold`/`RadiusLg` + the hard-shadow blur==0 invariant the cards source.
- [Source: _bmad-output/implementation-artifacts/6-3-navigate-between-surfaces-via-the-app-state-machine.md] — the parent story (the gate + the altitude split + the narrow-port precedent).
- [Memory: ui-presenter-pattern] — presenter + EditMode tests; thin view deferred to Epic 6.
- [Memory: unity-asmdef-nontransitive] — new cross-tier type ⇒ direct ref + matrix edge (the `UI → AR` check).
- [Memory: failure-path-cleanup-parity] — symmetric teardown if the presenter ever subscribes (prefer no subscription).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Debug Log References

- Headless EditMode gate (Unity 2022.3.62f3, Editor closed): one compile-fix iteration — the two new test files referenced `SpendResult`/`Result` (in `Veilwalkers.Core`) without a `using Veilwalkers.Core;` (the recurring first-compile-fix; the `ICreditService` fake's `TrySpendCreditsAsync`/`GrantCreditsAsync` return types failed CS0246+CS0738). Added the using to both test files. Re-run clean: **587/587 passed, 0 failed, 0 `error CS`** (+23 over the 564 6.3 baseline — OnboardingFlowTests 12 + OnboardingPresenterTests 11). `test-results.xml` `result="Passed"` confirmed + log grepped for `error CS` (0). Artifacts deleted before commit.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- **Altitude split realized exactly as specified (the 6.3 posture).** `OnboardingStep` enum + `OnboardingFlow` (step state machine) + `OnboardingPresenter`/`OnboardingViewModel` + the two narrow ports (`IDisclosureGate`, `IArPrewarmPort`) all live in `Veilwalkers.UI` (pure logic, ctor-injected, headless-tested). NO scene assets, NO MonoBehaviour view, NO coin-burst/card animation — those move with the `Onboarding.unity` scene to Story 6.5 (D2). NO production asmdef edge (UI→App/Economy/AR already present); NO acyclic-matrix edit; NO Bootstrap change (D4 — the deferred View constructs the presenter, the `MaterializationView` precedent; `FirstLaunchGrant.RunAsync()` already runs at boot — Bootstrap.cs:427).
- **AC-1:** `OnboardingPresenter` composes 3 premise cards (Decision B) from a new minimal `PremiseCardStyle` (Surface fill + lg radius + the hard-shadow blur==0 device + TextPrimary body — 6.1 tokens only, chrome never a tier color). The flow walks all cards before Disclosure (pinned by the walk, not the count).
- **AC-2:** the camera disclosure is a distinct `OnboardingStep.Disclosure` phase BEFORE the OS prompt; `OnboardingFlow.AdvanceFromDisclosure()` fires the prompt via the narrow `IDisclosureGate` (Decision C — a 1-method port so the UI.Tests need no `Veilwalkers.AR` ref even though `UI→AR` is a sanctioned edge; the real adapter onto `CameraPermissionFlow.ConfirmDisclosureAndRequest()` is the deferred View's). Disclosure copy is plain high-contrast `TextPrimary` (the safety-copy exception). The OS prompt cannot fire before the disclosure card is acknowledged (pinned).
- **AC-3:** a new minimal `CoinBurstStyle` presents the canon 20-Credit grant (`FirstLaunchGrant.StartingCredits`) settling into the live balance via `CreditPillStyle` (credit-gold currency token). The grant is NOT re-performed (D3) — the presenter only READS the balance. `AdvanceFromGrant()` calls `AppStateMachine.CompleteOnboarding()` exactly once (Onboarding → Home) and surfaces the ENTER AR HUNT primary ALL-CAPS CTA.
- **AC-4:** the end-to-end composition pin (`AR_is_unreachable_until_the_onboarding_flow_completes`) drives a REAL `AppStateMachine` (fakes for ports): `EnterArHuntAsync(ColdEntry)` is rejected while onboarding is incomplete (stays `Onboarding`), then accepted only after the flow reaches `Complete` (lands `ArHunt`). The gate itself is 6.3's; 6.4 proves the content drives it (D5).
- **Derived prewarm (architecture.md:575–584):** `OnboardingFlow.BeginPrewarm()` kicks `IArPrewarmPort.PrewarmAsync()` once, observed fire-and-forget on `TaskScheduler.Default` with a guarded continuation (a prewarm fault logs + is swallowed, never crashes onboarding — the 6.3 fire-and-forget discipline). Degrades to `IArPrewarmPort.NoPrewarm` when `ArSessionService` is the unregistered AR-rig seam (D6). The real `ArSessionService.PrewarmAsync()` (AR/ArSessionService.cs:105) is double-start-safe + refuses gracefully — verified in VS.
- **Decision A settled: NO new persisted "onboarding seen" flag.** Scoped to driving the FLOW + the gate; the per-launch re-show persistence is deferred (the AC says "first launch"; the 1.7 `StartingCreditsGranted` marker already gates once-per-install). Recorded as a deferral.
- **Graceful degradation throughout:** null `IDisclosureGate`/`IArPrewarmPort` default to the `No*` graceful seams (ctor); a null/before-load `ICreditService.Balance` degrades the coin-burst to a calm 0; onboarding always renders.
- One compile-fix iteration (missing `using Veilwalkers.Core;` in the two test files), caught by the headless gate's exit-1 first run.

### File List

**NEW — `Veilwalkers.UI` (production):**
- `Assets/Veilwalkers/UI/Onboarding/OnboardingStep.cs` (+ `.meta`) — new `Onboarding/` folder (+ `Onboarding.meta`)
- `Assets/Veilwalkers/UI/Onboarding/IDisclosureGate.cs` (+ `.meta`)
- `Assets/Veilwalkers/UI/Onboarding/IArPrewarmPort.cs` (+ `.meta`)
- `Assets/Veilwalkers/UI/Onboarding/OnboardingFlow.cs` (+ `.meta`)
- `Assets/Veilwalkers/UI/Onboarding/OnboardingViewModel.cs` (+ `.meta`)
- `Assets/Veilwalkers/UI/Onboarding/OnboardingPresenter.cs` (+ `.meta`)
- `Assets/Veilwalkers/UI/Components/PremiseCardStyle.cs` (+ `.meta`)
- `Assets/Veilwalkers/UI/Components/CoinBurstStyle.cs` (+ `.meta`)

**NEW — tests:**
- `Assets/Tests/EditMode/UI.Tests/OnboardingFlowTests.cs` (+ `.meta`) — 12 tests
- `Assets/Tests/EditMode/UI.Tests/OnboardingPresenterTests.cs` (+ `.meta`) — 11 tests

**NO production asmdef edit, NO test asmdef edit** (`UI.Tests` already references App/Economy/Core/UI — the disclosure is faked via the narrow `IDisclosureGate`, so no `UI.Tests → AR` ref was needed). **NO Bootstrap change, NO scene, NO schema bump, NO acyclic-matrix edit.**

### Change Log

- 2026-06-16 — Story 6.4 implemented: onboarding shell (premise cards + camera disclosure + 20-Credit coin-burst presentation), wiring the 6.3 `CompleteOnboarding()` gate. Logic-only (presenter + flow + 2 minimal descriptors + 2 narrow ports); scene/view/animation deferred to 6.5. 587/587 EditMode green (+23). Status → review.

### Review Findings

Adversarial 3-layer CR (Blind Hunter / Edge Case Hunter / Acceptance Auditor → verify-disputed-first triage), 2026-06-16: **36 raw → 36 deduped (0 cross-layer convergence) → 6 confirmed (all PATCHED), 0 deferred, 26 spec-sanctioned, 4 false-positive, 1 AC violation (PATCHED).** All 6 confirmed findings were graceful-degradation / contract-consistency tightenings on the new logic — none touched an AC behavior beyond the one AC-violation fix. 589/589 EditMode green re-verified after patching (+2 CR coverage pins over the 587 dev baseline).

- [x] [Review][Patch] **AC violation (major): premise-count desync.** `OnboardingPresenter.BuildPremiseCards()` sized cards from `PremiseBeats.Count` while `OnboardingFlow.PremiseCardCount` was a hardcoded `const 3` — a content pass changing the beat list to 2 (AC allows 2–3) would have the flow walk an index the presenter never built. → `OnboardingFlow.PremiseCardCount` is now a derived `=> OnboardingPresenter.PremiseBeats.Count` (one source of truth; they can't diverge). New pin `Premise_card_count_is_the_single_source_shared_with_the_presenter` asserts equality AND that the walk visits exactly that many cards. [Assets/Veilwalkers/UI/Onboarding/OnboardingFlow.cs (PremiseCardCount); OnboardingPresenter.cs]
- [x] [Review][Patch] **Unguarded `CompleteOnboarding()` call (major).** `AdvanceFromGrant()` called the external gate with no try/catch, contradicting the flow's "never throws" class doc — a throw (incl. from an `OnSurfaceChanged` subscriber) wedged the flow at `Grant`. → Wrapped in try/catch + `GameLog.Warn`; the counter increments only on a non-throwing call (a throw correctly leaves it 0 so a test catches it), but the flow ALWAYS advances to `Complete` (the gate is idempotent → no wedge, no double-grant risk). [Assets/Veilwalkers/UI/Onboarding/OnboardingFlow.cs (AdvanceFromGrant)]
- [x] [Review][Patch] **Unguarded disclosure-gate call (minor).** `AdvanceFromDisclosure()` called `_disclosure.ConfirmDisclosureAndRequest()` unguarded; a misbehaving adapter throw would wedge the flow + violate the "never throws" claim. → Wrapped in try/catch + warn; the flow still advances to `Grant` (the prompt is best-effort). New pin `A_throwing_disclosure_gate_does_not_wedge_the_flow` (a `ThrowingDisclosureGate` → no throw escapes, Step reaches Grant). [Assets/Veilwalkers/UI/Onboarding/OnboardingFlow.cs (AdvanceFromDisclosure)]
- [x] [Review][Patch] **`BeginPrewarm` flag-latched-before-schedule (nit).** `_prewarmKicked=true` was set BEFORE `KickPrewarm()` scheduled the task; a synchronous `StartNew` schedule fault would latch the flag true and silently lose prewarm for the whole session. → `KickPrewarm()` is now wrapped; a synchronous schedule fault RESETS `_prewarmKicked=false` + warns, so a later `BeginPrewarm` retries. [Assets/Veilwalkers/UI/Onboarding/OnboardingFlow.cs (BeginPrewarm)]
- [x] [Review][Patch] **`CoinBurstStyle` asymmetric clamp (nit + minor — same root, 2 findings).** `For()` clamped `amount` to ≥0 but forwarded `resultingBalance` unclamped (relying implicitly on `CreditPillStyle.For` to clamp). → Both values now clamp through a centralized `ClampToFloor` (symmetric, self-evident contract; no silent dependency on the pill helper continuing to clamp). [Assets/Veilwalkers/UI/Components/CoinBurstStyle.cs (For)]

**Spec-sanctioned (26, no change):** the full acceptance-auditor confirmation sweep (every AC + the 8-task File List + the D1–D7 sanctioned deviations + the 587/587 gate — all verified as compliant, no defect); the narrow-port decisions (`IDisclosureGate`/`IArPrewarmPort` for test-fakeability — Decisions C/D6); no Bootstrap/asmdef/matrix edit (D4 + the already-present `UI→AR` edge); the graceful-degradation tests (null/before-load credit service → calm 0; null ports → No* seams); the enum-order + exactly-once + ordering-rejection pins.

**False-positive (4, verified):** `BeginPrewarm` concurrent-call atomicity (OnboardingFlow is single-threaded UI-tier, no thread-safety contract — adding a lock would violate the pure-logic presenter pattern); `ChunkyButtonStyle.For()` "could return default" (it's a readonly struct, never null, with a graceful default arm + null-safe `DisplayLabel` — the pinned precedent); `CompleteOnboardingCalls++` placement "loses the count on throw" (the count tracks SUCCESSFUL drives by design — a throw leaving it 0 is the correct regression signal, now also guarded); the prewarm-idempotency test "doesn't assert the second call was rejected" (the `==1` count assertion IS that check — a second kick would make it 2).

No `deferred-work.md` entries — 0 deferrals this CR.
