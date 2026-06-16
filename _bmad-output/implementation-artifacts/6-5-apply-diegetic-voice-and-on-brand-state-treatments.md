# Story 6.5: Apply diegetic voice and on-brand state treatments

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

baseline_commit: dc4a555baf5b03fc002bd59f216b7d07cc703296

## Story

As a player,
I want the app's copy and loading/error states to feel like one wry-eerie world,
so that even waiting and errors stay in-brand (and safety copy stays plain).

## Acceptance Criteria

Sourced verbatim-in-intent from `docs/epics.md#Story-6.5` (lines 915–931), `docs/epics.md:107` (UX-DR14 = the diegetic-voice table + the plain-safety EXCEPTION incl. the canonical "The Veil pulls your attention. Keep one eye on the real world." line) + `docs/epics.md:108` (UX-DR15 = the state treatments), and `docs/architecture.md` (lines 466–474 = the AnchorRestoreResult "pulled back through the veil" beat, 478–484 = the insufficient-credits / safety-warning-entry rules, 601–612 = "every system moment wears a costume" framing incl. the veil-parting wipe + "the Veil is thick here, hold steady…" slow-device fallback). **CITATION NOTE:** the voice/state-treatment requirements (UX-DR14/DR15) live in `docs/epics.md:107–108`, NOT `architecture.md:107` (which is a Unity-Hub setup paragraph). Source the diegetic + safety copy from `epics.md:107`. The 5th Epic-6 story (Visual Identity, Navigation & Onboarding Shell). It CONSUMES the 6.1 tokens + 6.2 chunky kit + 6.3 `AppStateMachine` + the existing state enums (`AnchorRestoreResult`, `CameraPermissionState`, `PlacementOutcome`, `ArSessionState`, `CodexSlotState`) and SUPPLIES the diegetic copy + the state→treatment decision layer that the whole codebase has been deferring to "Story 6.5" (e.g. `HomeViewModel.cs:41` — the daily-reward label "is Story 6.5"; `MaterializationVariant`/`ArSessionView` veil-parting references).

**AC-1 — Diegetic Veil voice for system moments**
**Given** system moments
**When** copy is shown
**Then** it uses the **diegetic Veil voice** — "Part the Veil" (enter AR), "Consult the Veil" (rewarded action), "The Veil shows you more…" (new tier), "Pulled back through the Veil" (anchor restored), "Not yet discovered." (locked slot) — sourced from ONE canonical copy home (`VeilVoice`, see Task 1), NOT re-baked per call site.

**AC-2 — Safety / disclosure copy is the explicit PLAIN exception**
**Given** safety messaging
**When** it is shown
**Then** it is **plain, legible, high-contrast, never buried in flavor** (UX-DR14 EXCEPTION — epics.md:107). The AR Safety Warning copy + the camera-disclosure copy are PLAIN strings, distinct from the diegetic-voice strings, and the state-treatment presenter NEVER applies a Veil-voice costume to a safety/disclosure moment. Pin: a safety treatment carries plain copy + `IsSafetyException == true` (or equivalent), and the Veil-voice strings are NOT used for it.

**AC-3 — Cold-start / AR-warmup is a "veil-parting wipe" ritual, never a spinner/percentage**
**Given** the app loads or AR warms up
**When** the state treatment is chosen
**Then** cold-start / AR-warmup shows a **"veil-parting wipe" ritual** — **never a spinner / progress bar**, and the **slow-device fallback is MORE ritual ("the Veil is thick here, hold steady…"), never a percentage** (architecture.md:609–612). Modeled as a state-treatment DECISION keyed off `ArSessionState.Prewarming` (+ a slow-device flag); pin that the treatment is the wipe ritual and carries NO percentage / numeric-progress field.

**AC-4 — Per-state on-brand treatments (plane / anchor / camera / Codex)**
**Given** state treatments
**When** the app loads or recovers
**Then** each system state maps to its on-brand treatment:
- **plane-not-found** (`PlacementOutcome.NeedsCoaching`) → **friendly coaching** (the `PlacementResult.Message` coaching copy, framed in-voice but plainly actionable);
- **lost-anchor** (`AnchorRestoreResult.RelocatedToPlane`) → the **"Pulled back through the Veil" restore** beat (architecture.md:472–473);
- **camera-denied** (`CameraPermissionState.Denied`) → the **re-grant path** (the existing 3.1 `CameraPermissionFlow.RetryFromSettings()` flow — surfaced, never a dead end, never re-implemented);
- **Codex first-discovery** (`CodexSlotState.Discovered` via the `CodexService.OnMonsterDiscovered` event) → the **slot flip + count tick + new-tier silhouette fade-in** treatment decision (the X/67 tick + the silhouette-reveal-on-new-tier signal).

### Derived / load-bearing requirements (not separately numbered but required for the feature to work end-to-end)

- **NO new state enums.** Every state 6.5 treats ALREADY EXISTS and is owned elsewhere (`AnchorRestoreResult` in `Veilwalkers.Core.Contracts`; `CameraPermissionState`/`PlacementOutcome`/`ArSessionState`/`ArSafetyGateState` in `Veilwalkers.AR`; `CodexSlotState` in `Veilwalkers.UI`). 6.5 READS them and maps each to a treatment — it does NOT redefine or re-own them (the `MaterializationPresenter` "render the state, never re-classify" precedent — the 6.2 `CodexSlotStyle` posture).
- **The actual RENDER stays deferred (the standing Epic-6 view-layer defer).** The veil-parting WIPE animation, the coin-burst tween, the slot-flip/silhouette-fade-in animation, the chunky safety overlay, the `.unity` scenes — all remain deferred to asset-authoring / the device build (the same defer 6.2/6.3/6.4 recorded). 6.5 delivers the DECISION + the COPY + the descriptor (which treatment, which copy, no-percentage invariant), NOT the pixels/tweens. This keeps the headless EditMode gate honest (every prior UI story's altitude split — [[ui-presenter-pattern]]).
- **The diegetic copy must be the SINGLE source consumed by the existing deferred placeholders.** `HomeViewModel.cs:41` already says the daily-reward "Consult the Veil" label "is Story 6.5"; `ArSafetyView.cs:97` defers the plain safety copy to Epic 6. 6.5 supplies the canonical strings; the existing presenters/views (Home daily-reward control, the AR safety view) point at `VeilVoice.*` so there is no second copy of any line. Where a presenter currently hard-codes a string that has a diegetic equivalent (e.g. an "Enter AR Hunt" CTA that the voice table frames as "Part the Veil"), see Decision E for the scope boundary.

## Tasks / Subtasks

> **Altitude split (the load-bearing decision — identical posture to 6.2/6.3/6.4).** `Veilwalkers.UI` is the home: the copy home + the state-treatment presenter + the readonly-struct treatment view-models all live in `Veilwalkers.UI` (pure logic, ctor-injected where it reads services, headless-tested in `Veilwalkers.UI.Tests`). The actual animation/scene RENDER (the wipe tween, the slot-flip, the chunky overlay) is the deferred view-layer concern (D2). The split:
> - **`VeilVoice` (the canonical diegetic-copy home) → `Veilwalkers.UI`** — a pure static copy class (the `PumpkinPatchTokens` static-constants precedent), `const string` fields + a thin enum-keyed accessor. Tested in `Veilwalkers.UI.Tests`.
> - **`StateTreatmentPresenter` + `StateTreatment` (the state→treatment readonly-struct view-model) → `Veilwalkers.UI`** — maps each existing state enum to a `StateTreatment` (which copy + which treatment kind + the no-percentage/safety-exception flags). Pure logic, switch-on-state (the `MaterializationPresenter.VariantFor`/`AmbianceFor` precedent). Tested in `Veilwalkers.UI.Tests`.
> - **The thin views + scenes + animations → deferred to asset-authoring / device build** (logic-free; visual/scene/motion wiring). 6.5 is **headless logic only**, like every prior UI story ([[ui-presenter-pattern]]).

- [x] **Task 1 — `VeilVoice` canonical diegetic-copy home (AC-1, AC-2)** `Veilwalkers.UI`
  - [x] Add `public static class VeilVoice` in `Assets/Veilwalkers/UI/Voice/VeilVoice.cs` (new `Voice/` folder) — the SINGLE home for diegetic system-moment copy (the `PumpkinPatchTokens` static-const precedent). Each line is a `public const string` with the canonical wording from the UX-DR14 table (epics.md:107 / epics.md:925):
    - `EnterAr = "Part the Veil"` (enter AR)
    - `RewardedAction = "Consult the Veil"` (the daily/ad reward — the label `HomeViewModel.cs:41` defers to 6.5)
    - `NewTier = "The Veil shows you more…"` (a new tier discovered) — use the real ellipsis `…` (U+2026), matching the AC.
    - `AnchorRestored = "Pulled back through the Veil"` (anchor relocated/restored)
    - `LockedSlot = "Not yet discovered."` (the locked Codex slot) — **use the period form `"Not yet discovered."` EXACTLY** (this is the AC wording at epics.md:925 AND the existing value of `CodexDetailViewModel.NotDiscoveredCopy` at `CodexDetailViewModel.cs:32`). The UX-DR14 voice table at epics.md:107 writes it period-LESS ("Not yet discovered") — that table entry is NOT authoritative for the literal; the AC + the existing const win. Dropping the period would silently change the Codex value and is a regression (see Task 4 + E-note). Task 4 re-points `CodexDetailViewModel.NotDiscoveredCopy` at `VeilVoice.LockedSlot` so there is ONE source (do NOT leave two copies of the literal).
  - [x] **AC-2 — the PLAIN safety exception is a DISTINCT, clearly-segregated group.** Add a nested `public static class Safety` (or a clearly-named region) inside `VeilVoice` for the PLAIN safety/disclosure copy — NOT diegetic, NOT flavored. At minimum the AR-safety-warning body copy (the `ArSafetyView.cs:97` `TODO(Epic 6)` plain copy) + a reuse-or-reference of the existing plain camera-disclosure body (`OnboardingPresenter.DisclosureBody` at `OnboardingPresenter.cs:49–51` is ALREADY plain — reference it / do not duplicate it; if a single home is wanted, move it to `VeilVoice.Safety` and re-point Onboarding, but prefer referencing to avoid churn — see Decision E). The class doc must state loudly: "Safety copy is the UX-DR14 EXCEPTION — plain, legible, high-contrast, never costumed in Veil voice."
  - [x] (Optional, recommended) a thin `public static string For(VeilMoment moment)` enum-keyed accessor (`enum VeilMoment { EnterAr, RewardedAction, NewTier, AnchorRestored, LockedSlot }`) so the presenter can map a moment→copy by enum (the `DreadScaleTokens.ForTier` total-map precedent) with a graceful default + `GameLog.Warn` on an unmapped moment (NFR-3). Pin totality (every `VeilMoment` member returns a non-empty string) like `DreadScaleTokens.ForTier`.

- [x] **Task 2 — `StateTreatmentKind` + `StateTreatment` model (AC-2, AC-3, AC-4)** `Veilwalkers.UI`
  - [x] Add `public enum StateTreatmentKind` in `Assets/Veilwalkers/UI/Voice/StateTreatmentKind.cs` — the on-brand treatment families the AC enumerates (standalone enum, PascalCase, the `OnboardingStep`/`AppSurface` precedent). At minimum: `VeilPartingWipe` (cold-start/AR-warmup ritual — AC-3), `PlaneCoaching` (plane-not-found — AC-4), `AnchorRestoreBeat` ("pulled back through the veil" — AC-4), `CameraReGrant` (camera-denied — AC-4), `CodexFirstDiscovery` (slot flip + count tick + silhouette fade-in — AC-4), `SafetyWarning` (the PLAIN exception — AC-2). Add the enum-member/order pin (the `LoadPhaseContractTests` precedent).
  - [x] Add `public readonly struct StateTreatment` in `Assets/Veilwalkers/UI/Voice/StateTreatment.cs` — the readonly-struct view-model (the `MaterializationPlan`/`StateTreatment` precedent), composed ONLY via a private ctor + `internal static` factory(ies) so every treatment is internally consistent (the `MaterializationPlan.Create` private-ctor precedent — a future caller cannot make `Kind=VeilPartingWipe` + a percentage). Fields:
    - `Kind` (`StateTreatmentKind`)
    - `Copy` (`string`, null-safe getter → `string.Empty`, the `MaterializationPlan.MonsterId` precedent) — the line from `VeilVoice` (or the `PlacementResult.Message` coaching copy passed through for `PlaneCoaching`)
    - `IsSafetyException` (`bool`) — `true` ONLY for `SafetyWarning`; a constant-by-construction so even `default(StateTreatment)` is safe (the `MaterializationPlan.HideActionBarDuringMaterialization` constant-property precedent). The presenter NEVER sets a Veil-voice copy when this is true.
    - `ShowsRitualNotProgress` (`bool`) — `true` for `VeilPartingWipe`; encodes AC-3's "ritual, never a spinner/percentage." There is **NO percentage / numeric-progress field on this struct at all** — the absence is the invariant (pin that the type has no numeric-progress member; a treatment is qualitative ritual/coaching, never a `float Progress`).
    - `SlowDeviceFallback` (`bool`, default false) — when true on a `VeilPartingWipe`, the copy is the "more ritual" line ("the Veil is thick here, hold steady…"), still NOT a percentage (AC-3 slow-device fallback). Add this line to `VeilVoice.Safety`-adjacent ritual copy OR a `VeilVoice.RitualThickVeil` const.
  - [x] `StateTreatment` exposes NO mutable state and NO progress number; it is a pure value the deferred view binds to.

- [x] **Task 3 — `StateTreatmentPresenter` (AC-2, AC-3, AC-4 the state→treatment map)** `Veilwalkers.UI`
  - [x] `public sealed class StateTreatmentPresenter` in `Assets/Veilwalkers/UI/Voice/StateTreatmentPresenter.cs` — pure logic, the switch-on-state map (the `MaterializationPresenter` precedent). It may be DEPENDENCY-FREE (each method takes a state value, not a service — the `MaterializationPresenter.PlanFor(Rarity)` precedent) OR ctor-inject `VeilVoice`-readers if needed (prefer dependency-free + static `VeilVoice` reads — `VeilVoice` is static). Public methods, one per state surface (each returns a `StateTreatment`):
    - `StateTreatment ForArSession(ArSessionState state, bool slowDevice)` → `Prewarming` ⇒ `VeilPartingWipe` (with `SlowDeviceFallback = slowDevice`); `Cold` (the resting/back-out state) is NOT inherently a warmup moment — map it to the wipe ONLY for the cold→prewarm entry transition and DOCUMENT that choice inline (architecture.md:609 frames the ritual around *prewarm* + Home re-entry; default: scope the wipe to `Prewarming`, and if `Cold` also gets it, write the one-line why so the CR edge-hunter doesn't flag it). `Ready`/`Running`/`Paused` ⇒ a calm no-treatment / `default`. AC-3.
    - `StateTreatment ForPlacement(PlacementResult result)` → `NeedsCoaching` ⇒ `PlaneCoaching` carrying `result.Message`; `Placed`/`Failed` ⇒ a non-coaching treatment (document). AC-4.
    - `StateTreatment ForAnchorRestore(AnchorRestoreResult result)` → `RelocatedToPlane` ⇒ `AnchorRestoreBeat` (copy = `VeilVoice.AnchorRestored`); `Restored`/`Failed` ⇒ no beat (document — `Failed` is guidance, never a crash; `Restored` is silent/in-place). AC-4.
    - `StateTreatment ForCameraPermission(CameraPermissionState state)` → `Denied` ⇒ `CameraReGrant` (surfaces the re-grant path; copy is PLAIN-but-friendly, NOT buried — this borders the safety exception, keep it plain). Other states ⇒ no treatment. AC-4.
    - `StateTreatment ForCodexDiscovery(bool isFirstDiscovery, bool revealsNewTier)` (or a `CodexSlotState`-keyed signature) → first discovery ⇒ `CodexFirstDiscovery` (the slot-flip + count-tick decision; `revealsNewTier` ⇒ the copy is `VeilVoice.NewTier` "The Veil shows you more…" + the silhouette-fade-in signal). AC-4.
    - `StateTreatment ForSafetyWarning()` (or `ForSafetyGate(ArSafetyGateState)`) → the PLAIN `SafetyWarning` treatment (`IsSafetyException = true`, plain copy from `VeilVoice.Safety`, NEVER a Veil-voice line). AC-2.
  - [x] **NFR-3 graceful:** an unmapped/out-of-range enum value degrades to a calm default treatment + `GameLog.Warn` (the `MaterializationPresenter` `default:`-to-calmest + `DreadScaleTokens.ForTier` graceful-degrade precedent). The presenter NEVER throws.
  - [x] **The AC-2 firewall (the load-bearing invariant):** assert structurally that a Veil-voice line can NEVER reach a `SafetyWarning` treatment and a safety-exception copy is NEVER a `VeilVoice` diegetic string. Encode it so it's mutation-testable (e.g. the `SafetyWarning` factory only accepts `VeilVoice.Safety.*` copy; a diegetic line on a safety treatment is unreachable by construction).

- [x] **Task 4 — Re-point the existing deferred placeholders at `VeilVoice` (AC-1, the single-source rule)** `Veilwalkers.UI`
  - [x] **`HomeViewModel.cs:41` / `HomePresenter`** — the daily-reward control's diegetic label ("Consult the Veil") the comment defers to 6.5: surface `VeilVoice.RewardedAction` as the label (add a field to `HomeViewModel` for the reward-control label OR document that the deferred view reads `VeilVoice.RewardedAction` directly — prefer adding the labeled field so the presenter owns the copy choice, the `HomePresenter` label-const precedent). Update the `HomeViewModel.cs:41` comment from "the diegetic copy is Story 6.5" to point at `VeilVoice.RewardedAction`. **REGRESSION-SAFE (verified in VS):** the existing `HomePresenterTests` assert only the `bool DailyRewardAvailable` (HomePresenterTests.cs:146/149/167/177), never a copy string — adding a label field does not break them.
  - [x] **`CodexDetailViewModel.cs:32`** — the existing `public const string NotDiscoveredCopy = "Not yet discovered.";` (a NAMED const, the SINGLE producer; consumed at `CodexDetailView.cs:89`): re-point its value at `VeilVoice.LockedSlot` (ONE source — do NOT leave the literal duplicated). The behavior is identical; this is a single-line const-value refactor (pin: the locked-slot copy equals `VeilVoice.LockedSlot`). **REGRESSION-SAFE (verified in VS):** NO test in `Assets/Tests` asserts the `"Not yet discovered."` literal or `NotDiscoveredCopy` — so the re-point is safe AS LONG AS the value stays `"Not yet discovered."` (do not drop the period). The only way this re-point regresses is changing the VALUE.
  - [x] Scan for any OTHER call site that hard-codes a string the UX-DR14 table now owns (the recon found only those two as explicit 6.5-deferred; the CTA labels like "Enter AR Hunt" are a SCOPE BOUNDARY — see Decision E, default: do NOT re-label existing chunky-button CTAs in 6.5). Record what you re-pointed vs. left, with the Decision-E rationale.

- [x] **Task 5 — Plain safety-warning copy lands (AC-2)** `Veilwalkers.UI`
  - [x] Supply the PLAIN AR-safety-warning body copy that `ArSafetyView.cs:97` (`TODO(Epic 6)`) defers, as `VeilVoice.Safety.*` const(s) — the deliberate-read full warning + (if distinct) the fast-card minimal-dwell copy. PLAIN, legible, high-contrast wording (e.g. the architecture's "The Veil pulls your attention. Keep one eye on the real world." — architecture.md UX-DR14 exception line, epics.md:926). Update the `ArSafetyView.cs:97–100` `TODO` to reference `VeilVoice.Safety` for the copy (the chunky overlay RENDER stays deferred — D2; only the COPY source lands here).
  - [x] DO NOT change the `ArSafetyGate` LOGIC (Story 3.2 — the blocking gate, the deliberate-read dwell, `MayInteract`). 6.5 supplies the COPY the gate's view will render; it does not touch the gate's state machine or the safety-entry rule (cold-entry-only, architecture.md:482–484).

- [x] **Task 6 — Bootstrap wiring (AC / derived)** `Veilwalkers.App`
  - [x] **Decide and document (Decision D):** `VeilVoice` is pure static (no registration). `StateTreatmentPresenter` is pure logic — if it's dependency-free, the deferred views construct it directly (the `MaterializationView` constructs-its-presenter precedent — NO Bootstrap change). Only register it if a live service injection is genuinely needed (default: NO Bootstrap change — prefer the deferred-view-constructs-it path).
  - [x] **DO NOT change the acyclic matrix.** `Veilwalkers.UI → {App, Economy, AR, Monsters, Persistence, Billing, Encounter, Core}` are ALL already present in PRODUCTION (asmdef + `AcyclicDependencyTests` matrix lines 76–82, VERIFIED in 6.4). `AnchorRestoreResult` lives in `Veilwalkers.Core.Contracts` (UI→Core present); `CameraPermissionState`/`PlacementOutcome`/`ArSessionState`/`ArSafetyGateState` live in `Veilwalkers.AR` (UI→AR present); `CodexService`/`CodexSlotState` are UI/Monsters (present). **NO new PRODUCTION asmdef ref or matrix row is needed.**
  - [x] **ADD the test-only `Veilwalkers.AR` ref to `Veilwalkers.UI.Tests.asmdef` — REQUIRED (resolved definitively in VS, NOT optional).** The Task-7 test plan NAMES AR types directly: `ForArSession(ArSessionState.Prewarming, …)`, `ForPlacement(PlacementResult.NeedsCoaching(…))`, `ForCameraPermission(CameraPermissionState.Denied)`, and (if used) `ForSafetyGate(ArSafetyGateState)`. `Veilwalkers.UI.Tests.asmdef` currently references {TestRunner×2, Core, UI, Monsters, Persistence, Billing, Economy, App} — it does **NOT** reference `Veilwalkers.AR`, so naming those enums in a test won't compile without the ref. This is a TEST-only edge, EXCLUDED from the acyclicity matrix by the `.Tests` suffix (`AcyclicDependencyTests.cs:238` `EndsWith(".Tests")` — the 5.4/6.3/6.4 precedent). `AnchorRestoreResult` (Core.Contracts) + `CodexSlotState` (UI) are already reachable; it is the AR enums that force the ref. You do NOT construct a real `CameraPermissionFlow`/`ArSessionService` — you pass the enum VALUE (the `MaterializationPresenterTests` enum-value precedent); naming the enum TYPE is what needs the ref.

- [x] **Task 7 — Tests (all ACs)** `Veilwalkers.UI.Tests`
  - [x] **`VeilVoiceTests`:**
    - AC-1: each diegetic line equals its canonical wording (per-string assert — `EnterAr == "Part the Veil"`, `RewardedAction == "Consult the Veil"`, `NewTier` carries the real ellipsis `…`, `AnchorRestored == "Pulled back through the Veil"`, `LockedSlot == "Not yet discovered."`). Non-tautological: assert the EXACT canonical wording, not just non-empty.
    - AC-1 totality (if the `For(VeilMoment)` accessor exists): every `VeilMoment` member returns a non-empty string; an out-of-range cast degrades + warns (`LogAssert.Expect`, the `DreadScaleTokens.ForTier` precedent).
    - AC-2: the `VeilVoice.Safety` group is DISJOINT from the diegetic group — no safety string equals any diegetic string (the 6.1 AC-4 disjointness-pin precedent: tier set ∩ frame palette = ∅).
  - [x] **`StateTreatmentPresenterTests`:**
    - AC-3: `ForArSession(ArSessionState.Prewarming, slowDevice:false)` ⇒ `Kind == VeilPartingWipe`, `ShowsRitualNotProgress == true`; the type carries NO percentage field (structural — assert via the absence / a reflection pin that no numeric-progress member exists). `slowDevice:true` ⇒ the copy is the "more ritual" line, still no percentage. **Non-tautological:** the slow-device treatment differs from the normal one only in copy/flag, never gains a number.
    - AC-4 plane: `ForPlacement(PlacementResult.NeedsCoaching("raise your phone…"))` ⇒ `PlaneCoaching` carrying that exact message; `Placed`/`Failed` ⇒ not `PlaneCoaching`.
    - AC-4 anchor: `ForAnchorRestore(RelocatedToPlane)` ⇒ `AnchorRestoreBeat` with `Copy == VeilVoice.AnchorRestored`; `Restored`/`Failed` ⇒ not the beat (assert the OTHER branches do NOT produce the "pulled back" copy — the [[failure-path-cleanup-parity]] alternate-branch discipline).
    - AC-4 camera: `ForCameraPermission(Denied)` ⇒ `CameraReGrant`; `Granted`/`Disclosure`/`Requesting` ⇒ no re-grant treatment.
    - AC-4 Codex: first discovery (with `revealsNewTier:true`) ⇒ `CodexFirstDiscovery` carrying `VeilVoice.NewTier`; a non-discovery ⇒ no treatment; `revealsNewTier:false` first discovery ⇒ the flip/tick treatment WITHOUT the new-tier copy (the silhouette-fade-in is gated on the new-tier flag).
    - **AC-2 firewall (the load-bearing pin):** `ForSafetyWarning()` ⇒ `IsSafetyException == true` and `Copy` is a `VeilVoice.Safety` string, NOT any diegetic line — assert `Copy` ∉ {the 5 diegetic strings}. AND: every NON-safety treatment has `IsSafetyException == false`. This is the mutation-testable "safety never wears a costume" guard.
    - NFR-3 graceful: an out-of-range enum cast (e.g. `(ArSessionState)99`) degrades to a calm default + `GameLog.Warn` (`LogAssert.Expect`), never throws.
    - Enum pins: `StateTreatmentKind` members + order.
  - [x] **Single-source pins (Task 4):** `VeilVoice.LockedSlot` equals the value `CodexDetailViewModel` now surfaces for an undiscovered slot (one source — assert they're the same reference/value, the 6.4 premise-count single-source precedent); the Home daily-reward label equals `VeilVoice.RewardedAction`.
  - [x] **Headless gate:** the EditMode suite must pass; current baseline is **589/589** (post-6.4). **622/622 green (+33).**

## Dev Notes

### Decisions to make (call them explicitly in the Dev Agent Record)

| # | Decision | Default / recommendation | Why |
|---|---|---|---|
| A | Centralize copy in a `VeilVoice` home vs. keep feature-local `const`s? | **Default: centralize the DIEGETIC system-moment voice in `VeilVoice`** (the 5 AC-1 lines + the safety exception). Leave feature-LOCAL functional labels (button "SLAY", "Lure"/"Scan"/"Capture"/"Slay", pack badges) where they are — those are component-shape labels, not system-moment voice. The recon confirmed no central copy home exists; AC-1 names a closed set of system-moment lines, which is exactly what a `VeilVoice` home should own. | AC-1 is about the diegetic *system-moment* voice (loading/error/reward/reveal), a small closed set. Centralizing those gives ONE source for the placeholders (`HomeViewModel.cs:41`, `CodexDetailViewModel.cs:32`) without a churny global string-extraction. |
| B | The new-tier line ellipsis: `...` (three dots) vs `…` (U+2026)? | **Default: `…` (U+2026)** — the AC + the architecture voice table both write "The Veil shows you more…" with the typographic ellipsis. Match the source exactly. | Test asserts exact canonical wording; the AC uses `…`. A `...` would fail the exact-wording pin (and be wrong per the source). |
| C | Treatment presenter: dependency-free (enum-value methods) vs. ctor-inject services? | **Default: dependency-free** — each method takes the state VALUE (`ArSessionState`, `PlacementResult`, `AnchorRestoreResult`, `CameraPermissionState`, the Codex discovery flags), reads static `VeilVoice`. The `MaterializationPresenter.PlanFor(Rarity)` precedent (no injection). This keeps `UI.Tests` needing NO new AR/Monsters ref (pass the enum value, the `MaterializationPresenterTests` precedent). | Pure value-in/value-out is the simplest testable shape, avoids a Bootstrap edit (D), and avoids any test asmdef churn. The state owners (ArSessionService/CameraPermissionFlow/CodexService) stay the source of the live value; the presenter just maps it. |
| D | `StateTreatmentPresenter` Bootstrap registration vs. deferred-view-constructs-it. | **Default: deferred View constructs it (no Bootstrap change), the `MaterializationView`/6.4 precedent.** | The views are deferred; the presenter is dependency-free; no live registration is needed until the views exist. Avoid a Bootstrap edit for a logic-only story. |
| E | Scope of re-pointing existing hard-coded strings at `VeilVoice`. | **Default: re-point ONLY the two explicit 6.5-deferred placeholders** (`HomeViewModel.cs:41` daily-reward label → `VeilVoice.RewardedAction`; `CodexDetailViewModel.cs:32` `"Not yet discovered."` → `VeilVoice.LockedSlot`) + supply the deferred safety copy (`ArSafetyView.cs:97`). **Do NOT re-label the existing chunky-button CTAs** ("Enter AR Hunt" → "Part the Veil") — those are 6.2/6.3 component labels with their own ALL-CAPS chunky-button contract, and re-labeling them is a content/UX decision beyond this story's "system moments" scope (a CTA button label ≠ a system-moment voice line). Record this boundary explicitly. | AC-1 is "system moments… copy is shown" (loading/error/reward/reveal/locked-slot), not "rename every button." The two deferred placeholders + the safety copy are the in-scope wiring; the chunky CTA labels are a separate, already-shipped component decision. Avoids scope-creep regressions on 6.2/6.3 tests. |
| F | A `For(VeilMoment)` enum accessor on `VeilVoice` (optional). | **Default: ADD it** (a total enum→copy map with graceful degrade), so the presenter maps moment→copy by enum (the `DreadScaleTokens.ForTier` total-map precedent) and gets a free totality + graceful-degrade test. Cheap, and it mirrors the established token-resolver pattern. | A total enum map is the repo's idiom for "every member resolves" + a falsifiable totality pin; it costs little and adds a strong test. If skipped, the `const` fields + per-string asserts still satisfy AC-1. |

### Sanctioned-deviation matrix (the CR noise filter — keep this exhaustive)

| # | Deviation | Why it is sanctioned | Source |
|---|---|---|---|
| D1 | `VeilVoice` + `StateTreatmentPresenter` + `StateTreatment` live in `Veilwalkers.UI`, NOT `Veilwalkers.App`. | They are UI-tier copy + presentation decisions composing UI-tier descriptors / reading UI-tier state. App is below UI in the acyclic graph; App can't reference UI. The [[ui-presenter-pattern]] split (presenter + EditMode tests; thin view deferred). | 6.2/6.3/6.4 D1; ui-presenter-pattern; AcyclicDependencyTests |
| D2 | No scene assets, no MonoBehaviour view logic, no veil-parting WIPE animation, no slot-flip / silhouette-fade-in tween, no chunky safety-overlay render. Logic-only. | Every prior UI story (2.4, 2.5, 4.7, 6.2, 6.3, 6.4) deferred the MonoBehaviour view + scene + motion to keep the headless EditMode gate honest. The wipe ritual tween, the coin-burst, the slot-flip, the chunky overlay + the `.unity` scenes are asset-authoring / the device build. 6.5 lands the DECISION + COPY + descriptor. | ui-presenter-pattern; 6.4 D2; deferred-work.md (Epic-6 view defers, the 6.4 "ALL onboarding motion are Story 6.5 + asset-authoring" entry — the MOTION-policy decision lands; the tween RENDER stays deferred) |
| D3 | 6.5 does NOT re-own / redefine any state enum. It READS `AnchorRestoreResult`, `CameraPermissionState`, `PlacementOutcome`, `ArSessionState`, `CodexSlotState`, `ArSafetyGateState` and maps each to a treatment. | The state owners are AR (session/permission/placement), Core.Contracts (anchor restore), Monsters/UI (codex), UI (safety gate). 6.5 is the consumer/renderer-of-state, the `CodexSlotStyle` "renders state, never re-classifies" precedent. Re-defining would duplicate the owned model. | 6.2 `CodexSlotStyle`; `MaterializationPresenter`; recon report |
| D4 | The camera-denied re-grant path is SURFACED, not re-implemented. | Story 3.1 built `CameraPermissionFlow.RetryFromSettings()` + the `Denied`→Settings recovery. 6.5 maps `Denied` → a `CameraReGrant` treatment that points at the existing flow; it does not re-build permission logic (the 6.4 D4 delegation precedent). | CameraPermissionFlow.cs:115 (RetryFromSettings); 6.4 D4 |
| D5 | The plane-coaching copy passes through `PlacementResult.Message` (it is already coaching copy), framed by the `PlaneCoaching` treatment. | `PlacementOutcome.NeedsCoaching` already carries the coaching `Message` (PlacementResult.cs). 6.5 wraps it in the treatment kind; it does not invent new coaching copy nor re-run placement. Plane coaching is "friendly + plainly actionable," bordering plain — keep it legible, not buried. | PlacementResult.cs (NeedsCoaching/Message); architecture.md:471–472 |
| D6 | The AR-safety / camera-disclosure copy is the PLAIN exception, structurally segregated from the diegetic group; the chunky safety overlay RENDER stays deferred. | UX-DR14's explicit exception (epics.md:107; epics.md:926). The `ArSafetyView.cs:97` `TODO(Epic 6)` is the copy-defer 6.5 closes (the COPY only — the overlay render is D2). The existing plain `OnboardingPresenter.DisclosureBody` is referenced, not duplicated (E). | epics.md:107; ArSafetyView.cs:97–100; OnboardingPresenter.cs:49–51 |
| D7 | Re-pointing scope is the two explicit deferred placeholders + the safety copy (NOT a global string-extraction; chunky CTA labels untouched). | Decision E — AC-1 scopes to "system moments," a closed voice set; the existing chunky-button CTA labels are 6.2/6.3 component decisions with their own contract + tests. Re-labeling them would risk 6.2/6.3 regressions for no AC benefit. | Decision E; HomeViewModel.cs:41; CodexDetailViewModel.cs:32 |

### Seams this story inherits (from deferred-work.md + prior stories)

- **The 6.4 "ALL onboarding motion are Story 6.5 + asset-authoring" + "the veil-parting onboarding motion lands in 6.5" defer (deferred-work.md, the 6.4 dev entry).** 6.5 lands the MOTION-POLICY decision (the veil-parting wipe IS the cold-start treatment, never a spinner — AC-3) as a headless treatment descriptor; the actual tween RENDER + the `.unity` scenes stay in the standing asset-authoring defer (D2). 6.5 is NOT the scene-build story — it is the diegetic-voice + state-treatment DECISION story.
- **`HomeViewModel.cs:41` — the daily-reward "Consult the Veil" diegetic label "is Story 6.5".** 6.5 supplies `VeilVoice.RewardedAction` and re-points the placeholder (Task 4).
- **`CodexDetailViewModel.cs:32` — the existing `"Not yet discovered."` literal.** 6.5 makes `VeilVoice.LockedSlot` the single source (Task 4).
- **`ArSafetyView.cs:97 TODO(Epic 6)` — the plain safety copy.** 6.5 supplies `VeilVoice.Safety.*` (Task 5); the chunky overlay render stays deferred (D2/D6). The 4.7 deferral "the AR safety warning copy is Epic 6" is the same family.
- **The existing state enums (3.1 camera, 3.4 placement, 3.5 anchor-restore, 3.3 session, 2.4 codex-slot, 3.2 safety-gate).** All built + owned; 6.5 maps them (D3). The architecture's "pulled back through the veil" beat (architecture.md:472–473) is the `AnchorRestoreResult.RelocatedToPlane` treatment.

### Architecture compliance (must-follow guardrails)

- **UX-DR14 voice + its EXCEPTION (epics.md:107):** the diegetic Veil voice for system moments; safety messaging is plain/legible, never buried in flavor. This is AC-1 + AC-2 and the load-bearing firewall (Task 3 — a safety treatment NEVER carries a diegetic line).
- **Cold-start = ritual, never a progress bar (architecture.md:609–612):** AR prewarm hides under the veil-parting wipe; the slow-device fallback is MORE ritual ("the Veil is thick here, hold steady…"), never a percentage/spinner. This is AC-3 — encode the no-percentage invariant structurally (the `StateTreatment` type has no numeric-progress member).
- **The "pulled back through the veil" beat (architecture.md:472–473):** `AnchorRestoreResult.RelocatedToPlane` → the diegetic restore beat; NEVER vanish. 6.5 supplies the copy + the treatment kind (the render reuses the Lure materialization VFX, deferred).
- **Service-locator rule (architecture.md:456–459):** the presenter is UI-tier; if it ever reads a service the future view does so via `GameServices.Get` (default: dependency-free, Decision C — no locator read at all).
- **NFR-3 graceful (architecture.md:474, 509):** an unmapped/out-of-range state degrades to a calm default treatment + `GameLog.Warn`, never a throw (the `MaterializationPresenter` `default:`-to-calmest + `DreadScaleTokens.ForTier` precedent).
- **Chrome never painted with a tier color (6.1 AC-4):** any treatment chrome uses the frame palette, never `DreadScaleTokens` (tier color is encounter/Codex-badge only). The diegetic copy is text, not chrome — no token risk, but if a treatment carries a descriptor it sources frame-palette tokens only.
- **Safety-entry rule (architecture.md:482–484):** the FR-3 safety warning is cold-AR-entry only; a Shop-resume does NOT re-fire it. 6.5 supplies the safety COPY; it does NOT change the gate's entry logic (Story 3.2).
- **`GameLog` for all diagnostics** (`Veilwalkers.Core`) — never `Debug.Log` (the recurring first-compile-fix: `using Veilwalkers.Core;`).
- **Events: symmetric subscribe/unsubscribe (architecture.md:514–515)** — IF a treatment presenter subscribes to `CodexService.OnMonsterDiscovered` (it likely should NOT — prefer the dependency-free value-in shape, Decision C: the deferred view subscribes and passes the discovery flags to the presenter), it must expose symmetric teardown ([[failure-path-cleanup-parity]]). Prefer NO subscription in the presenter — keep it pure value-in/value-out.

### Source tree components to touch

**NEW (`Veilwalkers.UI`):**
- `Assets/Veilwalkers/UI/Voice/VeilVoice.cs` (the canonical diegetic-copy home + the nested `Safety` plain group) — new `Voice/` folder
- `Assets/Veilwalkers/UI/Voice/VeilMoment.cs` (the enum for the optional `For(VeilMoment)` accessor — Decision F; may be nested in `VeilVoice.cs`)
- `Assets/Veilwalkers/UI/Voice/StateTreatmentKind.cs` (enum)
- `Assets/Veilwalkers/UI/Voice/StateTreatment.cs` (readonly struct, private ctor + internal factory)
- `Assets/Veilwalkers/UI/Voice/StateTreatmentPresenter.cs`

**UPDATE (production) — the single-source re-points (Task 4/5):**
- `Assets/Veilwalkers/UI/Home/HomeViewModel.cs` + `HomePresenter.cs` — surface `VeilVoice.RewardedAction` for the daily-reward label; update the line-41 comment.
- `Assets/Veilwalkers/UI/Codex/CodexDetailViewModel.cs` (+ `CodexDetailPresenter.cs` if the literal is produced there) — re-point `"Not yet discovered."` at `VeilVoice.LockedSlot`.
- `Assets/Veilwalkers/UI/ARHud/ArSafetyView.cs` — point the `TODO(Epic 6)` copy at `VeilVoice.Safety.*` (copy source only; render stays deferred).

**UPDATE (production) — ONLY IF a decision requires it:**
- `Assets/Veilwalkers/App/Bootstrap.cs` — ONLY if Decision D chooses live registration (default: no change).

**NEW TESTS:**
- `Assets/Tests/EditMode/UI.Tests/VeilVoiceTests.cs`
- `Assets/Tests/EditMode/UI.Tests/StateTreatmentPresenterTests.cs`

**UPDATE (tests) — REQUIRED:**
- `Assets/Tests/EditMode/UI.Tests/Veilwalkers.UI.Tests.asmdef` — currently references {TestRunner×2, Core, UI, Monsters, Persistence, Billing, Economy, App}; it does NOT reference `Veilwalkers.AR`. **ADD `Veilwalkers.AR` (test-only) — REQUIRED:** the Task-7 tests NAME the AR enums `ArSessionState`/`PlacementResult`(`PlacementOutcome`)/`CameraPermissionState`/`ArSafetyGateState`, which won't compile without the ref. The `.Tests` suffix EXCLUDES this edge from the acyclicity matrix (`AcyclicDependencyTests.cs:238`, the 5.4/6.3/6.4 precedent). `AnchorRestoreResult` (Core.Contracts) + `CodexSlotState` (UI) are already reachable; the AR enums force the ref.

**DO NOT TOUCH (sanctioned no-change):** the state-enum DEFINITIONS (`AnchorRestoreResult`/`CameraPermissionState`/`PlacementOutcome`/`ArSessionState`/`CodexSlotState`/`ArSafetyGateState` — read, don't redefine); `CameraPermissionFlow` (3.1 — surface its re-grant, don't re-implement); the `ArSafetyGate` LOGIC (3.2 — supply copy, don't touch the gate); the chunky-button CTA labels (6.2/6.3 — Decision E boundary); `EconomyConfig`/schema; the deferred `EncounterService`/`ArSessionService` Bootstrap seams (stay deferred).

### Testing standards summary

- **Headless EditMode is the hard gate** ([[unity-headless-testing]]): Editor must be closed; never trust exit 0 — read `test-results.xml` for `result="Passed"` AND grep the log for `error CS`. Delete `test-results.xml` + `editor-test.log` before commit.
- **Fakes:** mostly value-in (pass the enum value / `PlacementResult` / discovery flags directly — Decision C; no service fakes needed for the presenter). A `CodexService`/`ICreditService` fake only if a single-source pin needs to read the live value the placeholder surfaces (prefer asserting the `VeilVoice` constant directly).
- **`GameServices.ResetForTests()`** between tests only if a test resolves via the locator (most won't — the presenter is dependency-free).
- **Non-tautological pins:** the exact canonical wording (`…` ellipsis, exact phrases); the AC-2 firewall (a safety treatment's copy ∉ the 5 diegetic strings; every non-safety treatment `IsSafetyException == false`); the AC-3 no-percentage structural invariant (the `StateTreatment` type has no numeric-progress member; slow-device differs only in copy/flag); the single-source equalities (`VeilVoice.LockedSlot` IS the Codex locked-slot copy; the Home reward label IS `VeilVoice.RewardedAction`); the alternate-branch discipline (`Restored`/`Failed` do NOT produce the "pulled back" beat — [[failure-path-cleanup-parity]]); NFR-3 graceful (out-of-range enum → calm default + warn). These are the falsifiable assertions the CR edge-case hunter will check for.

### Project Structure Notes

- `Veilwalkers.UI` already references `Veilwalkers.App`, `Veilwalkers.Economy`, `Veilwalkers.Monsters`, `Veilwalkers.Billing`, `Veilwalkers.Persistence`, `Veilwalkers.AR`, `Veilwalkers.Encounter`, `Veilwalkers.Core` (VERIFIED in 6.4: asmdef lines 4–13 + `AcyclicDependencyTests` matrix lines 76–82). Every state type 6.5 reads is reachable — NO new production asmdef ref or matrix row.
- `AnchorRestoreResult` is in `Veilwalkers.Core.Contracts` (`Assets/Veilwalkers/Core/Contracts/AnchorRestoreResult.cs`); `CameraPermissionState`/`PlacementResult`/`PlacementOutcome`/`ArSessionState`/`ArSafetyGateState` are in `Veilwalkers.AR` (`Assets/Veilwalkers/AR/*` — note `ArSafetyGateState` is the AR-tier gate STATE; the `ArSafetyView` that RENDERS it is in `Veilwalkers.UI/ARHud`); `CodexSlotState` is in `Veilwalkers.UI`.
- `PumpkinPatchTokens` / `DreadScaleTokens` (6.1) are the pure-static-class + total-resolver precedent for `VeilVoice` + the `For(VeilMoment)` map. `MaterializationPresenter` (4.7) is the switch-on-state→readonly-struct-plan precedent for `StateTreatmentPresenter` + `StateTreatment`. `OnboardingPresenter`/`HomePresenter` (6.3/6.4) are the const-string-copy-on-the-presenter precedent.
- The existing plain copy `OnboardingPresenter.DisclosureBody` (`OnboardingPresenter.cs:49–51`) is ALREADY the plain camera-disclosure exception — reference it, don't duplicate (Decision E).

### References

- [Source: docs/epics.md#Story-6.5] — the two AC blocks (lines 915–931): the diegetic voice (AC-1) + the safety exception (AC-2) + the state treatments (AC-3/AC-4).
- [Source: docs/epics.md:107] — UX-DR14 (Voice & tone — diegetic microcopy): the canonical 5 lines + the safety exception ("The Veil pulls your attention. Keep one eye on the real world.").
- [Source: docs/architecture.md:466–474] — the `AnchorRestoreResult` mapping incl. `RelocatedToPlane` → the "pulled back through the veil" beat (reuse Lure materialization VFX) — never vanish (the AC-4 anchor treatment).
- [Source: docs/architecture.md:478–484] — services never open Shop/Billing (the re-grant is App/UI-decided); the safety-warning-entry rule (cold-AR-entry only — 6.5 supplies copy, not the gate).
- [Source: docs/architecture.md:601–612] — "every system moment wears a costume": ad reward = "consult the Veil"; zero-credit = felt descent; cold-start warmup = ritual not a progress bar, slow-device fallback = MORE ritual ("the Veil is thick here, hold steady…") (AC-1 reward voice + AC-3 wipe ritual).
- [Source: Assets/Veilwalkers/UI/Home/HomeViewModel.cs:39–42] — the daily-reward "Consult the Veil" label comment: "the diegetic copy is Story 6.5" (the placeholder 6.5 closes).
- [Source: Assets/Veilwalkers/UI/Codex/CodexDetailViewModel.cs:32] — the existing `"Not yet discovered."` literal (re-point at `VeilVoice.LockedSlot`).
- [Source: Assets/Veilwalkers/UI/ARHud/ArSafetyView.cs:94–119] — the `TODO(Epic 6)` plain safety copy + the `ArSafetyGateState` render switch (6.5 supplies the copy; the gate logic + overlay render stay).
- [Source: Assets/Veilwalkers/Core/Contracts/AnchorRestoreResult.cs] — `Restored`/`RelocatedToPlane`/`Failed` (the anchor-restore treatment input).
- [Source: Assets/Veilwalkers/AR/CameraPermissionState.cs + CameraPermissionFlow.cs:115] — `Disclosure`/`Requesting`/`Granted`/`Denied` + `RetryFromSettings()` (the camera re-grant path 6.5 surfaces).
- [Source: Assets/Veilwalkers/AR/PlacementResult.cs] — `PlacementOutcome.{Placed,NeedsCoaching,Failed}` + `PlacementResult.Message` (the plane-coaching input + copy).
- [Source: Assets/Veilwalkers/AR/ArSessionState.cs + ArSessionService.cs:105] — `Cold`/`Prewarming`/`Ready`/`Running`/`Paused` (the warmup-ritual input).
- [Source: Assets/Veilwalkers/UI/Codex/CodexSlotState.cs + Assets/Veilwalkers/Monsters/Codex/CodexService.cs:59] — `Discovered`/`Silhouette`/`Blank` + `OnMonsterDiscovered`/`DiscoveredCount`/`UniverseCount` (the first-discovery + count-tick inputs).
- [Source: Assets/Veilwalkers/UI/Encounter/MaterializationPresenter.cs + MaterializationPlan.cs] — the switch-on-state→readonly-struct-plan precedent (private-ctor + internal factory).
- [Source: Assets/Veilwalkers/UI/Design/PumpkinPatchTokens.cs + DreadScaleTokens.cs] — the pure-static-class + `ForTier` total-resolver-with-graceful-degrade precedent for `VeilVoice` + `For(VeilMoment)`.
- [Source: _bmad-output/implementation-artifacts/6-4-onboard-a-new-player-and-disclose-the-camera.md] — the prior Epic-6 story (the altitude split + the narrow-port + the "ALL onboarding motion → 6.5 + asset-authoring" defer).
- [Memory: ui-presenter-pattern] — presenter + EditMode tests; thin view deferred to Epic 6 / asset-authoring.
- [Memory: unity-asmdef-nontransitive] — new cross-tier type ⇒ direct ref + matrix edge (the `UI.Tests → AR` test-only check, if a test names an AR enum).
- [Memory: failure-path-cleanup-parity] — assert the alternate branches (`Restored`/`Failed` don't produce the beat; non-safety treatments are non-exception); symmetric teardown if the presenter ever subscribes (prefer no subscription).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Debug Log References

- Headless EditMode gate (Unity 2022.3.62f3, Editor closed): one compile-fix iteration. First run exited 0 but produced NO `test-results.xml` and 3 `error CS0117` in the log (the crash-handler fake-exit-0 trap — [[unity-headless-testing]]): `StateTreatmentPresenterTests.cs` called the `internal static StateTreatment.None()` factory directly, which is NOT visible to the separate `Veilwalkers.UI.Tests` assembly (no `InternalsVisibleTo`). Fixed by obtaining the calm `None` treatment via a public path instead (`ForArSession(Running)` + `default(StateTreatment)`), which is exactly the non-safety/None shape the firewall test needs. (A stale `Temp/UnityLockfile` left by the crash-handler exit was removed after confirming no live `Unity.exe` — then re-ran.) Re-run clean: **622/622 passed, 0 failed, 0 skipped, 0 `error CS`** (+33 over the 589 6.4 baseline — VeilVoiceTests 10 + StateTreatmentPresenterTests 22 + 1 HomePresenter single-source pin). `test-results.xml` `result="Passed"` confirmed + log grepped for `error CS` (0). Artifacts deleted before commit.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- **Altitude split realized exactly as specified (the 6.2/6.3/6.4 posture).** `VeilVoice` (the canonical diegetic-copy home + the nested plain-`Safety` group) + `StateTreatmentKind` (enum) + `StateTreatment` (readonly struct, private ctor + `internal` factories, the `MaterializationPlan` precedent) + `StateTreatmentPresenter` (the dependency-free switch-on-state map) all live in `Veilwalkers.UI/Voice/` (pure logic, headless-tested). NO scene assets, NO MonoBehaviour view logic, NO veil-parting WIPE / slot-flip / coin-burst / chunky safety-overlay RENDER — those stay in the standing Epic-6 asset-authoring defer (D2). NO production asmdef edge (UI→AR/Core already present); NO acyclic-matrix edit; NO Bootstrap change (D4 — `VeilVoice` is static, `StateTreatmentPresenter` is dependency-free, the deferred views construct it directly — the `MaterializationView` precedent).
- **AC-1 (diegetic voice):** `VeilVoice` is the single home for the 5 canonical system-moment lines (`EnterAr`="Part the Veil", `RewardedAction`="Consult the Veil", `NewTier`="The Veil shows you more…" with the U+2026 ellipsis, `AnchorRestored`="Pulled back through the Veil", `LockedSlot`="Not yet discovered." with the authoritative period) + a total `For(VeilMoment)` map with graceful degrade (the `DreadScaleTokens.ForTier` precedent).
- **AC-2 (safety exception + the firewall):** the plain safety copy lives in a structurally-separate `VeilVoice.Safety` nested class (`ArWarningFull`/`ArWarningFast`), never costumed. The firewall is by construction: `StateTreatment.SafetyWarning(...)` is the ONLY factory that sets `IsSafetyException=true`, and `StateTreatmentPresenter.ForSafetyGate` is the ONLY producer of it — sourcing copy ONLY from `VeilVoice.Safety`. Pinned: a safety treatment's copy ∉ the 5 diegetic lines; every non-safety treatment has `IsSafetyException=false`.
- **AC-3 (veil-parting wipe, never a percentage):** `ForArSession(Prewarming, slowDevice)` ⇒ `VeilPartingWipe` with `ShowsRitualNotProgress=true`; the slow-device fallback swaps to MORE ritual copy (`RitualThickVeil` "The Veil is thick here. Hold steady…") via the `SlowDeviceFallback` flag, never a number. The no-percentage invariant is STRUCTURAL: `StateTreatment` has NO float/double/int member, pinned by a reflection test (`StateTreatment_has_no_numeric_progress_member`). `Cold`/`Ready`/`Running`/`Paused` ⇒ calm `None` (the wipe is scoped to the actual warmup moment `Prewarming` — O1, documented inline).
- **AC-4 (per-state treatments):** `ForPlacement(NeedsCoaching)` ⇒ `PlaneCoaching` carrying the placement's own `Message`; `ForAnchorRestore(RelocatedToPlane)` ⇒ `AnchorRestoreBeat` with `VeilVoice.AnchorRestored` (and `Restored`/`Failed` produce NO beat — the alternate-branch discipline, pinned); `ForCameraPermission(Denied)` ⇒ `CameraReGrant` (surfaces the existing 3.1 Settings round-trip, not re-implemented — D4); `ForCodexDiscovery(first, revealsNewTier)` ⇒ `CodexFirstDiscovery` (the flip + count-tick; the `VeilVoice.NewTier` silhouette-fade-in copy gated on `revealsNewTier`).
- **Single-source re-points (Task 4, the placeholders the codebase deferred to 6.5):** `CodexDetailViewModel.NotDiscoveredCopy` now forwards `VeilVoice.LockedSlot` (a `const`-to-same-assembly-`const` alias — value unchanged "Not yet discovered.", regression-safe: no test asserts the literal); `HomeViewModel` gained a `RewardControlLabel` surfaced as `VeilVoice.RewardedAction` from `HomePresenter.Build()` (the `HomeViewModel.cs:41` "diegetic copy is Story 6.5" placeholder closed). Decision E honored: the existing chunky-button CTA labels (e.g. "Enter AR Hunt") were NOT re-labeled — those are 6.2/6.3 component decisions, out of the "system moments" scope.
- **Plain safety copy lands (Task 5):** `ArSafetyView`'s `TODO(Epic 6)` now sources `VeilVoice.Safety.ArWarningFull`/`ArWarningFast` (the copy SOURCE; the chunky overlay RENDER stays the deferred view-layer concern — D2). The `ArSafetyGate` LOGIC (3.2) is untouched.
- **NFR-3 graceful throughout:** every `StateTreatmentPresenter` method + `VeilVoice.For` degrade an out-of-range enum to the calm default (`None` / `EnterAr`) + `GameLog.Warn`, never throw (the `MaterializationPresenter` default-to-calmest precedent). `default(StateTreatment)` is safe (`Kind=None`, `Copy=""` null-coalesced, `IsSafetyException=false`).
- **Decision C settled: dependency-free presenter.** Each method takes the state VALUE; tests pass the enum value (no real `CameraPermissionFlow`/`ArSessionService` constructed). The one required asmdef change is the TEST-only `Veilwalkers.UI.Tests → Veilwalkers.AR` ref (the tests NAME the AR enums; `.Tests`-excluded from the acyclic matrix — the 5.4/6.3/6.4 precedent). NO production asmdef change.
- One compile-fix iteration (internal-factory-not-visible-to-test-assembly), caught by the headless gate's no-results-file + `error CS` first run.

### File List

**NEW — `Veilwalkers.UI` (production):**
- `Assets/Veilwalkers/UI/Voice/VeilVoice.cs` (+ `.meta`) — new `Voice/` folder (+ `Voice.meta`); the `VeilMoment` enum is co-located in this file.
- `Assets/Veilwalkers/UI/Voice/StateTreatmentKind.cs` (+ `.meta`)
- `Assets/Veilwalkers/UI/Voice/StateTreatment.cs` (+ `.meta`)
- `Assets/Veilwalkers/UI/Voice/StateTreatmentPresenter.cs` (+ `.meta`)

**MODIFIED — `Veilwalkers.UI` (production, the single-source re-points + safety copy):**
- `Assets/Veilwalkers/UI/Codex/CodexDetailViewModel.cs` — `NotDiscoveredCopy` now forwards `VeilVoice.LockedSlot` (value unchanged).
- `Assets/Veilwalkers/UI/Home/HomeViewModel.cs` — added the `RewardControlLabel` field + ctor param; updated the line-41 comment.
- `Assets/Veilwalkers/UI/Home/HomePresenter.cs` — `Build()` surfaces `VeilVoice.RewardedAction` as the reward-control label.
- `Assets/Veilwalkers/UI/ARHud/ArSafetyView.cs` — the `TODO(Epic 6)` copy now sources `VeilVoice.Safety.*` (render stays deferred).

**NEW — tests:**
- `Assets/Tests/EditMode/UI.Tests/VeilVoiceTests.cs` (+ `.meta`) — 10 tests
- `Assets/Tests/EditMode/UI.Tests/StateTreatmentPresenterTests.cs` (+ `.meta`) — 22 tests

**MODIFIED — tests:**
- `Assets/Tests/EditMode/UI.Tests/HomePresenterTests.cs` — +1 single-source pin (the daily-reward label == `VeilVoice.RewardedAction`).
- `Assets/Tests/EditMode/UI.Tests/Veilwalkers.UI.Tests.asmdef` — added the test-only `Veilwalkers.AR` ref (REQUIRED; `.Tests`-excluded from the acyclic matrix).

**NO production asmdef edit, NO Bootstrap change, NO scene, NO schema bump, NO acyclic-matrix edit.**

### Change Log

- 2026-06-16 — Story 6.5 implemented: diegetic Veil voice (`VeilVoice` copy home — 5 canonical system-moment lines + the total `For(VeilMoment)` map) + the plain safety EXCEPTION (`VeilVoice.Safety`, structurally firewalled) + the on-brand state-treatment presenter (`StateTreatmentPresenter` mapping the existing AR-session / placement / anchor-restore / camera-permission / Codex-discovery / safety-gate states to `StateTreatment` decisions, no-percentage invariant structural). Re-pointed the two 6.5-deferred placeholders (Codex locked-slot copy, Home daily-reward label) at `VeilVoice` (single source) + landed the plain safety copy in `ArSafetyView`. Logic-only (presenter + copy home + 2 value types); scene/wipe/flip/overlay render deferred to asset-authoring. 622/622 EditMode green (+33). Status → review.
- 2026-06-16 — Code review: applied 3 confirmed patches (2 doc-comment fixes on `CodexDetailViewModel.NotDiscoveredCopy`, 1 tautological-test-assertion removal in `HomePresenterTests`). No production logic changed. Re-ran the headless gate green. Status → done.

### Review Findings

Adversarial 3-layer CR (Blind Hunter / Edge Case Hunter / Acceptance Auditor → verify-disputed-first triage), 2026-06-16: **9 raw → 9 deduped (0 cross-layer convergence) → 3 confirmed (all PATCHED), 3 deferred (out-of-scope), 2 spec-sanctioned, 1 false-positive. 0 AC violations.** All 3 confirmed findings were documentation / test-quality nits on the new code — NO logic defect, NO AC behavior touched. 622/622 EditMode green re-verified after patching (no test-count change — the patches were a doc rewrite + a redundant-assertion removal).

- [x] [Review][Patch] **Contradictory doc-comment on `NotDiscoveredCopy` (minor).** The XML doc opened "(Not `const` anymore…" while the field IS still `const` — self-contradictory (the parenthetical even concluded "…stays a compile-time constant alias"). → Rewrote the comment: the field stays `const` (a `const` may initialize from another `const`), both are same-assembly so the alias is safe, with the correct cross-assembly caveat (a split would inline-and-stale, where `static readonly` would be the runtime-reference alternative). [Assets/Veilwalkers/UI/Codex/CodexDetailViewModel.cs (NotDiscoveredCopy)]
- [x] [Review][Patch] **Incorrect C# rule in the same comment (nit, same block — folded into the patch above).** The comment claimed "a `const` can only forward another same-assembly `const`" — factually wrong (cross-assembly const forwarding compiles; the real hazard is the OPPOSITE — a cross-assembly const inlines and can go stale). → The rewrite states the rule correctly. [Assets/Veilwalkers/UI/Codex/CodexDetailViewModel.cs (NotDiscoveredCopy)]
- [x] [Review][Patch] **Tautological test assertion (minor).** `Build_daily_reward_label_is_the_single_VeilVoice_source` asserted BOTH `== VeilVoice.RewardedAction` AND `== "Consult the Veil"` on the same value — the second is redundant (the constant's exact wording is already pinned independently in `VeilVoiceTests`). → Removed the redundant literal assertion, kept the single-source pin (matching the Codex single-source pin's single-assertion shape). [Assets/Tests/EditMode/UI.Tests/HomePresenterTests.cs (Build_daily_reward_label_is_the_single_VeilVoice_source)]

**Deferred (3, all out-of-scope — recorded in deferred-work.md):** (1) the `VeilVoiceTests` disjointness test's narrative is slightly broader than its two-constant assertion — fit-for-purpose for AC-2's "no safety string equals any diegetic string"; the comprehensive firewall is pinned in `StateTreatmentPresenterTests` (the per-constant wording pins also catch a manual desync); (2/3) `ForPlacement` forwards `PlacementResult.Message` which could be null/empty for a `NeedsCoaching` with no message → a blank coaching card; the struct null-coalesce prevents any crash, and the empty-message GUARD belongs to the AR-layer `PlacementResult.NeedsCoaching` factory (Story 3.4), NOT 6.5 (Decision D5 explicitly sanctions the pass-through). Owners: a future VeilVoice firewall-test-hardening pass / Story 3.4 (PlacementResult).

**Spec-sanctioned (2, no change):** the `const`-alias compile-time-inline behavior (the sanctioned single-assembly single-source pattern — same-assembly recompile is atomic; the patched comment documents the cross-assembly caveat); the switch-on-state `default:`-to-`None`+`GameLog.Warn` non-exhaustiveness (the architecture-mandated NFR-3 graceful-degrade, all 5 current enum members covered + the out-of-range degrade pinned with `LogAssert.Expect`).

**False-positive (1, verified):** the `revealsNewTier:false` Codex test "lacks an explicit `Copy == string.Empty` assert" — the `AreNotEqual(VeilVoice.NewTier, Copy)` already pins the only-other-branch (the impl passes `string.Empty`), so the boundary IS covered (the alternate-branch discipline); an explicit empty-assert is clarity-only, not a coverage gap.

No NEW debt introduced by the implementation itself — the 3 deferrals are pre-existing upstream concerns (PlacementResult validation) + a test-narrative nit, not 6.5 logic gaps.
