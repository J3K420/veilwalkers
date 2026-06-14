---
baseline_commit: "1e73bceba27fa2057a1a198241158dc621f69bb2"
---

# Story 3.1: Disclose and request camera permission gracefully

Status: done

## Story

As a player,
I want a clear explanation before the camera permission prompt and a way to recover if I deny it,
So that I understand why the camera is needed and never hit a dead end.

## Acceptance Criteria

**AC-1 (disclose BEFORE the OS prompt).**
**Given** a player who has not granted camera permission
**When** they head toward AR Mode
**Then** an in-app disclosure of camera usage is shown BEFORE the OS permission dialog (`CameraPermissionFlow`).
[Source: docs/epics.md#Story-3.1]

**AC-2 (graceful denial → re-grant path, never a dead end).**
**Given** the player denies camera permission
**When** they try to enter AR Mode
**Then** AR Mode is blocked and an explanatory re-grant screen with a path to system settings is shown — never a crash or dead end.
[Source: docs/epics.md#Story-3.1]

**AC-3 (Location is NEVER requested in MVP).**
**Given** Location permission
**When** the MVP runs
**Then** Location is never requested (world spawns are Phase 3).
[Source: docs/epics.md#Story-3.1]

## Tasks / Subtasks

- [x] **Task 1 — Define the platform-permission seam (`ICameraPermission`, AR area).** (AC-1, AC-2, AC-3)
  - [x] Create `Assets/Veilwalkers/AR/ICameraPermission.cs` — a tiny behavior seam over the OS permission surface so the flow logic is headless-testable (the `IClock`/`IArAnchorProvider` precedent: public interface in the owning area + a production impl, faked in tests). Members (all synchronous-shaped; the OS dialog result is observed by re-querying state on the next pump, NOT awaited — see decision #3):
    - `bool HasCameraPermission { get; }` — wraps `Permission.HasUserAuthorizedPermission(Permission.Camera)`.
    - `void RequestCameraPermission()` — wraps `Permission.RequestUserPermission(Permission.Camera)` (fires the OS dialog; result arrives async via the permission callbacks / next-frame re-query).
    - `void OpenAppSettings()` — opens the OS app-settings page (the AC-2 "path to system settings"); production impl uses an `AndroidJavaObject` Intent (`ACTION_APPLICATION_DETAILS_SETTINGS`). Document that on non-Android editor/standalone it is a `GameLog.Info` no-op (the flow logic does not depend on the side effect, only on the subsequent `HasCameraPermission` re-query).
  - [x] **AC-3 is a seam-shape rule, not a runtime branch:** `ICameraPermission` exposes CAMERA only — NO location member exists, so no flow path can request location. Document in the interface XML-doc that location is deliberately absent (Phase-3, FR-5) and pin it with the AC-3 test (Task 6: assert via reflection that the AR assembly references no `Permission.FineLocation`/`CoarseLocation` / `LocationService`).
  - [x] Decision to record (Dev Notes #1): keep the seam in **AR** (FR-5 → `AR/CameraPermissionFlow` per architecture.md:407,503), NOT Core — it is an Android-platform behavior like `IArAnchorProvider`, and the flow that consumes it lives in AR too. Core stays platform-free.
- [x] **Task 2 — Build the pure-logic permission flow (`CameraPermissionFlow`, AR area).** (AC-1, AC-2)
  - [x] `Assets/Veilwalkers/AR/CameraPermissionFlow.cs` — plain C# (NO `MonoBehaviour`), ctor-injected `ICameraPermission` (+ optional `ITelemetrySink` later; NOT this story), `readonly`, `ArgumentNullException` null-check. This is the ONE place the disclose→prompt→denied→regrant decision lives. **AR's "thin adapter, zero branching logic" mandate (architecture.md:466) applies to the OS-touching ADAPTER (`ICameraPermission` impl), not to this flow object — the branching that decides which screen to show IS worth testing and is exactly what the architecture says to keep out of the adapter.** Call this tension out in Dev Notes (decision #2).
  - [x] An explicit state enum `CameraPermissionState { Disclosure, Requesting, Granted, Denied }` (sibling file or nested) — the single source of truth the view renders. `Disclosure` = not yet granted, must show the pre-prompt disclosure (AC-1); `Requesting` = OS dialog in flight; `Granted` = AR may proceed; `Denied` = show the re-grant screen (AC-2).
  - [x] Methods (no async/await — the OS dialog result is polled, decision #3):
    - `CameraPermissionState Evaluate()` — the entry decision: if `HasCameraPermission` → `Granted`; else → `Disclosure`. Called when the player "heads toward AR Mode" (AC-1). It NEVER calls `RequestCameraPermission` directly — disclosure must precede the prompt (AC-1 anti-pattern: prompting before disclosure).
    - `CameraPermissionState ConfirmDisclosureAndRequest()` — the player acknowledged the disclosure card → set `Requesting` and call `_permission.RequestCameraPermission()`. (This is the ONLY method that fires the OS prompt, and it can only be reached from `Disclosure`.)
    - `CameraPermissionState OnRequestResult()` — re-query `HasCameraPermission` after the dialog resolves → `Granted` or `Denied`. (Pumped by the view on focus-regain / the permission callback — decision #3.)
    - `CameraPermissionState RetryFromSettings()` — from `Denied`, the player tapped "Open Settings" → `_permission.OpenAppSettings()`, stay `Denied` until they return and a re-query (`OnRequestResult`) flips to `Granted`. (Never a dead end — AC-2.)
  - [x] Guard illegal transitions (e.g. `ConfirmDisclosureAndRequest` when already `Granted`) with `GameLog.Warn` + a no-op return of the current state, never a throw (a UI-driven flow must not crash). Document the legal transition table in the class doc.
- [x] **Task 3 — Production platform impl (`AndroidCameraPermission`, AR area).** (AC-1, AC-2, AC-3)
  - [x] `Assets/Veilwalkers/AR/AndroidCameraPermission.cs : ICameraPermission` — the untestable adapter edge (mirrors `SystemClock : IClock`). `#if UNITY_ANDROID && !UNITY_EDITOR` real impl (`UnityEngine.Android.Permission` + the settings Intent); else editor/standalone fallback (`HasCameraPermission => true` so the editor flow is reachable, `RequestCameraPermission`/`OpenAppSettings` → `GameLog.Info` no-ops). Document the conditional + WHY (no `Permission` API off-device). NO branching logic beyond the platform `#if` — keep it a thin adapter (AR mandate).
  - [x] Confirm the AR asmdef needs the `UnityEngine.AndroidJNIModule` only at build (the `Permission` API lives in `UnityEngine.CoreModule` / `UnityEngine.AndroidJNIModule`); no asmdef `references` edge changes (AR already refs only `Veilwalkers.Core` + engine). Note any `precompiledReferences`/engine-module need in Dev Notes; if none, say so explicitly.
- [x] **Task 4 — Thin view (`CameraPermissionView : MonoBehaviour`, UI/ARHud).** (AC-1, AC-2)
  - [x] `Assets/Veilwalkers/UI/ARHud/CameraPermissionView.cs` — logic-free binder, the 2.4/2.5 graceful-degrade precedent. Resolves the flow via `GameServices.Get<CameraPermissionFlow>()` (or constructs it from a resolved `ICameraPermission` — decide & record; lean: register `CameraPermissionFlow` in Bootstrap so the view just `Get`s it, mirroring how services are resolved). Catches `ServicesNotReadyException`/`KeyNotFoundException` → inert + `GameLog.Warn`, never throws at `Awake`.
  - [x] Renders the screen for the current `CameraPermissionState`: `Disclosure` → the camera-usage disclosure card + a "Continue" action that calls `ConfirmDisclosureAndRequest()`; `Requesting` → a neutral waiting state; `Granted` → hands off to AR entry (the actual AR-entry trigger is Story 3.2/3.3 — this view only signals "may proceed"); `Denied` → the re-grant screen with an "Open Settings" action calling `RetryFromSettings()` + a "Continue" that AR-blocks (AC-2). Pumps `OnRequestResult()` on `OnApplicationFocus(true)` (the dialog/settings round-trip returns focus — decision #3).
  - [x] **Pixels DEFER to Epic 6** (the locked UI altitude, [[ui-presenter-pattern]]): the disclosure-card copy/layout, the re-grant screen styling, chunky components (UX-DR3), the diegetic voice (UX-DR14 "Part the Veil" framing — EXCEPT the safety/permission copy stays plain per UX-DR14's exception). `Render` is a stubbed `GameLog.Info` breakdown with a `TODO(Epic 6)` — same as `CodexGridView`/`CodexDetailView`. Do NOT author a scene/prefab; Story 6.3/6.4 places the surface + the onboarding camera-disclosure card (UX-DR19).
- [x] **Task 5 — Bootstrap seam (register the flow; no scene).** (AC-1, AC-2)
  - [x] In `Bootstrap.cs`, construct `new AndroidCameraPermission()` + `new CameraPermissionFlow(permission)` and register the flow in `GameServices` (read-only-after-wiring, AR-4). Follow the EXISTING registration shape (the `clock`/services pattern). If CodexService-style "seam-only comment, not live" is preferred because no AR scene exists yet, decide & record — lean: this flow has NO unauthored-asset blocker (unlike CodexService), so it CAN be registered live; the view is what's unplaced (Epic 6). Record the choice.
  - [x] Do NOT wire `ArSessionService`, the safety gate, or any AR-entry control flow — those are Stories 3.2/3.3. This story stops at "permission resolved → may proceed" + "denied → re-grant".
- [x] **Task 6 — EditMode tests (`AR.Tests`, NEW assembly).** (AC-1, AC-2, AC-3)
  - [x] Create `Assets/Tests/EditMode/AR.Tests/Veilwalkers.AR.Tests.asmdef` — this is the project's FIRST AR test assembly, so there is NO sibling in the same folder to copy: author the FULL field set verbatim from `Monsters.Tests` (an incomplete asmdef silently compiles ZERO tests — the new-assembly trap). Exact shape required:
    - `"name": "Veilwalkers.AR.Tests"`, `"rootNamespace": "Veilwalkers.AR.Tests"`.
    - `"references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner", "Veilwalkers.Core", "Veilwalkers.AR"]` (the TestRunner refs are MANDATORY; AR + Core are the tiers under test — NO Persistence/UI ref needed).
    - `"includePlatforms": ["Editor"]`, `"overrideReferences": true`, `"precompiledReferences": ["nunit.framework.dll"]` (REQUIRED — without it `[Test]`/`Assert` won't resolve and the assembly compiles empty), `"autoReferenced": false`, `"defineConstraints": ["UNITY_INCLUDE_TESTS"]`, `"noEngineReferences": false`.
    - The `.Tests` suffix is what excludes it from the acyclicity guard (`AcyclicDependencyTests.IsTestAssembly`); mirror `Monsters.Tests`/`UI.Tests` exactly. [Assets/Tests/EditMode/Monsters.Tests/Veilwalkers.Monsters.Tests.asmdef:1-26]
  - [x] `FakeCameraPermission : ICameraPermission` (in `AR.Tests`) — scriptable: a settable `bool HasCameraPermission`, counters `RequestCount`/`OpenSettingsCount`, and a hook so a test can flip `HasCameraPermission = true` to simulate "granted after the dialog" / "granted after returning from settings". Deep-state, no Unity types.
  - [x] `CameraPermissionFlowTests`:
    - **AC-1:** `Evaluate()` with no permission → `Disclosure` (NOT `Requesting`) AND `RequestCount == 0` — proves disclosure precedes the prompt (the anti-pattern guard: a regression that prompts first → red).
    - **AC-1:** `Evaluate()` when already granted → `Granted`, no disclosure, `RequestCount == 0`.
    - **AC-1 path:** `Disclosure` → `ConfirmDisclosureAndRequest()` → `Requesting` AND `RequestCount == 1` (the ONE prompt fires only after acknowledgement).
    - **AC-2:** dialog resolves denied (`HasCameraPermission` stays false) → `OnRequestResult()` → `Denied`.
    - **AC-2:** from `Denied`, `RetryFromSettings()` → `OpenSettingsCount == 1`, state stays `Denied` (no dead end); then flip the fake to granted + `OnRequestResult()` → `Granted` (recovery works).
    - **AC-2 grant-after-dialog:** flip fake to granted + `OnRequestResult()` → `Granted`.
    - **Illegal transition:** `ConfirmDisclosureAndRequest()` when `Granted` → no-op + `GameLog.Warn` (use `LogAssert.Expect`), `RequestCount` unchanged, state stays `Granted`.
    - **Ctor null-guard:** `new CameraPermissionFlow(null)` → `ArgumentNullException`.
  - [x] **AC-3 (Location never referenced):** a reflection/source test asserting the seam exposes camera only and the AR assembly references no location permission API. Two-layer: (a) assert `typeof(ICameraPermission)` has no member naming "location"; (b) a source-scan test (read the AR `.cs` files) asserting no `Permission.FineLocation`/`Permission.CoarseLocation`/`LocationService`/`ACCESS_*_LOCATION` token appears. (b) is the falsifiable one — a future story adding a location request → red. **Anti-vacuity guard:** the scan MUST first assert it found a non-zero number of AR `.cs` files (glob the AR source dir, `Assert.That(files, Is.Not.Empty)`) BEFORE asserting the token is absent — otherwise a wrong/empty path makes the test vacuously green (the source-scan tautology trap). Document that this is the AC-3 guard (the [[unity-asmdef-architecture-test]] source-scan style).

## Dev Notes

### SCOPE — read this first
This is the **FIRST Epic-3 story and the FIRST AR-area code** (the AR asmdef has existed since 1.2 but holds ZERO `.cs` — references only `Veilwalkers.Core`). It is ALSO a UI-touching story, so it stacks two established patterns: the **platform-seam pattern** ([[ui-presenter-pattern]] is the closest precedent — pure-logic object + thin view, EditMode-tested) and a NEW **`ICameraPermission` platform seam** (the `IClock`/`SystemClock` shape: public interface + production impl + fake). The genuinely-new thing is the seam over Unity's Android `Permission` API; everything else mirrors the locked UI altitude.

- **BUILD now:** the `ICameraPermission` seam (AR) + `AndroidCameraPermission` production impl (the untestable adapter edge, `#if UNITY_ANDROID`); the pure-logic `CameraPermissionFlow` + `CameraPermissionState` (the disclose→prompt→denied→regrant decision, the ONE branching home); a thin graceful-degrade `CameraPermissionView : MonoBehaviour` (UI/ARHud, `Render` stubbed); Bootstrap registration of the flow; the FIRST `AR.Tests` EditMode assembly with `FakeCameraPermission` + the AC-1/AC-2/AC-3 tests.
- **DEFER to Epic 6** (with `deferred-work.md` notes): the disclosure-card + re-grant-screen PIXELS (copy/layout/chunky styling UX-DR3, the diegetic-but-plain permission voice UX-DR14, the onboarding camera-disclosure card UX-DR19), the scene/prefab placement + AR-entry routing (Story 6.3/6.4).
- **DEFER to Stories 3.2/3.3** (Epic 3): the actual AR-Mode entry the `Granted` state hands off to (the Safety Warning gate 3.2, the `ArSessionService` lifecycle 3.3). This story stops at "permission resolved → may proceed / denied → re-grant"; it does NOT start an AR session.
- **Do NOT** build a scene, a prefab, design tokens/colors, the Safety Warning, `ArSessionService`, or any location handling.

### The design decisions this story locks (call them out for the CR reviewer)
1. **`ICameraPermission` lives in AR, not Core.** It is an Android-platform behavior (mirrors `IArAnchorProvider`, which architecture.md keeps in AR), and the flow consuming it lives in AR (FR-5 → `AR/CameraPermissionFlow`, architecture.md:407,503). Core stays platform-free (only `IClock`-style pure seams live there). The production impl `AndroidCameraPermission` is the `SystemClock`-equivalent untestable edge.
2. **The flow IS branching logic — and that does NOT violate AR's "thin adapter, zero branching logic" mandate.** architecture.md:466 says "AR contains no branching logic worth testing (thin adapter)" — that rule targets the OS-TOUCHING adapter (`AndroidCameraPermission`, which stays a thin `#if` wrapper). The disclose→prompt→denied→regrant STATE MACHINE is precisely the kind of branching that IS worth testing, and the architecture's own intent ("keep branching out of the adapter") is satisfied by isolating it in the pure-logic `CameraPermissionFlow` (ctor-injected, headless-tested, no Unity types) with the adapter behind a seam. Document this so the CR doesn't flag the flow as an AR-mandate violation.
3. **No async/await — the OS dialog result is polled on focus-regain, not awaited.** Unity's `Permission.RequestUserPermission(string)` returns `void` (not a `Task`). A `RequestUserPermission(string, PermissionCallbacks)` overload exists (`PermissionGranted`/`PermissionDenied`/`PermissionDeniedAndDontAskAgain` events) — but we deliberately do NOT use it, for two reasons: (a) callbacks have historically been flaky on some OEM devices; (b) **callbacks do NOT fire for the return-from-Settings round-trip** (AC-2's recovery path), so a focus-regain re-query is required there regardless — and using it uniformly for both the dialog result AND the settings return keeps ONE resolution mechanism. So `CameraPermissionFlow` is synchronous: `ConfirmDisclosureAndRequest()` fires the prompt and moves to `Requesting`; the view pumps `OnRequestResult()` on `OnApplicationFocus(true)` to re-query `HasCameraPermission` and resolve `Granted`/`Denied`. This keeps the flow deterministically testable (the fake flips a bool; no async harness) and matches platform reality. (If a later story wants the `PermissionCallbacks` event object for finer UX, it can be threaded through the seam then — out of scope here.)
4. **`Granted` is a hand-off signal, not an AR start.** This story does NOT own AR entry. The view, on `Granted`, signals "may proceed" (a `GameLog.Info` + an exposed flag/event the Epic-6 AppStateMachine / Story 3.2 safety gate will consume); it does NOT call `ArSessionService` (3.3) or show the Safety Warning (3.2). Keeps the story scoped to permission only.

### What this story IS and IS NOT
- **IS:** (a) `ICameraPermission` (AR seam, camera-only — AC-3) + `AndroidCameraPermission` (production `#if UNITY_ANDROID` adapter); (b) `CameraPermissionFlow` + `CameraPermissionState` (pure logic, the disclose-before-prompt AC-1 rule + the denied→settings→recover AC-2 rule, illegal-transition-safe); (c) a thin `CameraPermissionView : MonoBehaviour` (UI/ARHud, graceful-degrade, `Render` stubbed, focus-pump); (d) Bootstrap registration of the flow; (e) the FIRST `AR.Tests` assembly + `FakeCameraPermission` + AC-1/2/3 tests.
- **IS NOT:** the disclosure/re-grant PIXELS (Epic 6 — UX-DR3/DR14/DR19); a scene/prefab or AR-entry routing (Epic 6 Story 6.3/6.4); the AR Safety Warning gate (Story 3.2); `ArSessionService` / the AR session lifecycle / prewarm (Story 3.3); plane detection / anchoring (3.4/3.5); ANY location permission handling (AC-3 — Phase 3, never in MVP); the onboarding flow that first routes the player toward AR (Epic 6 Story 6.4 — this story provides the flow it will call).

### Architecture constraints (hard rules)
- **AR is a thin adapter; `ArSessionService` is the sole AR-lifecycle owner (architecture.md:466-468).** This story adds NO session lifecycle code — only the permission seam + flow. The adapter (`AndroidCameraPermission`) stays branchless beyond the platform `#if`; the branching flow is pure-logic (decision #2). [architecture.md AR boundary §]
- **One-way acyclic graph `Core ← {…, AR} ← … ← App ← UI` (AR-5).** `ICameraPermission`/`CameraPermissionFlow`/`AndroidCameraPermission` live in `Veilwalkers.AR` (refs only `Veilwalkers.Core` — unchanged). `CameraPermissionView` lives in `Veilwalkers.UI` (already refs every lower tier incl. AR — no asmdef edge change). The new `AR.Tests` is `.Tests`-suffixed → excluded from the acyclic guard. NO allow-matrix edit. [architecture.md AR-5; Veilwalkers.AR.asmdef; Veilwalkers.UI.asmdef]
- **Pure-logic classes are ctor-injected, never read the locator; only MonoBehaviours read `GameServices` (AR-4).** `CameraPermissionFlow` takes `ICameraPermission` via ctor; only `CameraPermissionView` calls `GameServices.Get`. [architecture.md AR-4]
- **`GameServices` wired ONCE at Bootstrap, read-only after (AR-4).** Register the flow alongside the existing services; never mutate the table after wiring. [Bootstrap.cs; GameServices.cs]
- **`GameServices.Get<T>()` throws `ServicesNotReadyException` before wiring (AR-4).** The view degrades gracefully (catch `ServicesNotReadyException`/`KeyNotFoundException` → inert + `GameLog.Warn`) — the 2.4/2.5 view precedent. [CodexGridView.cs; CodexDetailView.cs]
- **No `Debug.Log` — use `GameLog` (AR-19).** The view `Render` stub, the adapter no-ops, and the illegal-transition guard all log via `GameLog`. [GameLog.cs]
- **Typed results / never crash to a dead end (NFR-3, AC-2).** The flow never throws on a UI-driven path (illegal transition → `GameLog.Warn` + current-state no-op); denial → `Denied` state + a settings path, never an uncaught exception or stranded player (the "Banned: any flow that strands the player" UX rule, UX-DR16). [architecture.md NFR-3; epics.md UX-DR16]
- **Location is never requested (AC-3, FR-5).** The seam exposes camera only; the AR assembly references no location API. Pinned by the AC-3 source-scan test. [epics.md FR-5, Story-3.1 AC-3]

### The house patterns to copy
- **Platform seam (interface + production impl + fake):** mirror `IClock` (Core) / `SystemClock` (Core) / `FakeClock` (per test assembly). Here: `ICameraPermission` (AR) / `AndroidCameraPermission` (AR, `#if UNITY_ANDROID`) / `FakeCameraPermission` (AR.Tests). [IClock.cs; SystemClock.cs; the per-assembly FakeClock from 2.5]
- **Ctor-injected pure-logic object:** `CameraPermissionFlow` mirrors `CodexGridPresenter`/`CodexDetailPresenter` — `readonly` dependency, `ArgumentNullException` null-check, no Unity types, an explicit state enum it returns. [CodexGridPresenter.cs:24-29; CodexDetailPresenter.cs]
- **Graceful-degrade view:** `CameraPermissionView` mirrors `CodexGridView`/`CodexDetailView` — `GameServices.Get` in a guard, catch both exception types, inert + `GameLog.Warn`, `Render` is a stubbed `GameLog.Info` with `TODO(Epic 6)`, never throws at `Awake`. [CodexGridView.cs; CodexDetailView.cs]
- **NEW `.Tests` assembly:** `Veilwalkers.AR.Tests.asmdef` mirrors `Monsters.Tests`/`UI.Tests` — references the area-under-test + Core, `.Tests` suffix excludes it from `AcyclicDependencyTests`. [Monsters.Tests asmdef; UI.Tests asmdef]
- **Bootstrap registration:** mirror the existing service-registration shape (construct impl → construct logic object → `GameServices.Register`). [Bootstrap.cs the clock/services wiring]
- **AC-3 source-scan test:** the falsifiable-disk-scan style from the asmdef architecture test ([[unity-asmdef-architecture-test]]) — read the production `.cs`, assert a banned token is absent (mutation-testable: add the token → red), NOT a hand-built tautology.

### Previous story intelligence (Epic 2 + the seam stories)
- **2.4/2.5 locked the UI altitude** ([[ui-presenter-pattern]]): pure-logic object (EditMode-tested) + thin logic-free view; pixels defer to Epic 6; views degrade gracefully via `GameServices.Get` guards. 3.1's `CameraPermissionFlow`/`CameraPermissionView` follow this verbatim — the only twist is the flow lives in AR (not UI) because FR-5 maps it there and it consumes an AR platform seam.
- **The `IClock`/`SystemClock`/per-assembly-`FakeClock` seam (1.8/2.5)** is the exact template for `ICameraPermission`/`AndroidCameraPermission`/`FakeCameraPermission`. The 2.5 CR sanctioned per-assembly fakes (`internal sealed` blocks sharing) — so `FakeCameraPermission` is authored fresh in `AR.Tests`, not shared. [[tautological-test-trap]]: the fake must drive REAL state (a settable bool + counters), and the granted/denied tests flip it and assert the production flow's response — never assert against the fake's own inputs.
- **Anti-tautology bar (Epic 1–2):** tests exercise the production flow object against the fake seam; the AC-1 "disclosure precedes prompt" test asserts `RequestCount == 0` after `Evaluate()` (a regression that prompts first → red); the AC-3 test source-scans for a banned location token (add it → red). No test asserts a literal it computed from the same input.
- **No unauthored-asset blocker here (unlike CodexService).** CodexService stayed seam-only because `MonsterDatabase.asset` was unauthored. `CameraPermissionFlow` has no such blocker — it CAN be registered live in Bootstrap (decision in Task 5). What's deferred is only the VIEW placement (no AR scene until Epic 6 Story 6.3), exactly like the Codex views are logic-complete but unplaced.

### Edge cases & boundary rules (must-handle — these become test cases)
- **No permission, heading to AR:** `Evaluate()` → `Disclosure` (NOT a prompt) — disclosure must come first (AC-1). `RequestCount == 0`.
- **Already granted:** `Evaluate()` → `Granted`, no disclosure, no prompt.
- **Acknowledge disclosure:** `ConfirmDisclosureAndRequest()` → `Requesting`, exactly ONE `RequestCameraPermission()` call.
- **Dialog → granted:** flip fake granted, `OnRequestResult()` → `Granted`.
- **Dialog → denied:** fake stays false, `OnRequestResult()` → `Denied` (the re-grant screen, AC-2).
- **Denied → open settings → return granted:** `RetryFromSettings()` opens settings (`OpenSettingsCount == 1`), stays `Denied`; on return, flip granted + `OnRequestResult()` → `Granted` (recovery, never a dead end — AC-2).
- **Denied → open settings → return still denied:** stays `Denied`, the re-grant screen persists, still no dead end (the player can retry).
- **Permanently denied / "Don't ask again" (Android `DeniedAndDontAskAgain`):** after one or two denials the OS stops showing the dialog entirely — `RequestCameraPermission()` becomes a silent no-op and the OS Settings deep-link is the SOLE recovery. The flow handles this by construction (the `Denied` state's `RetryFromSettings()` is the same Settings-Intent path — no special-casing needed), but this is WHY the Settings escape hatch is mandatory, not a nicety (AC-2 "never a dead end"). No new branch; document the rationale so the CR understands the Settings path is load-bearing.
- **Illegal transition:** `ConfirmDisclosureAndRequest()` while `Granted` → `GameLog.Warn` + no-op, state unchanged, no extra prompt.
- **Location never requested (AC-3):** the seam has no location member; the AR source references no location API. Pinned by the source-scan test.
- **Editor/standalone (`!UNITY_ANDROID`):** `AndroidCameraPermission.HasCameraPermission => true` so the flow is reachable in-editor; `Request`/`OpenSettings` are `GameLog.Info` no-ops. The flow logic is platform-agnostic (tested against the fake, not the real adapter).

### Project Structure Notes
- NEW (production, `Veilwalkers.AR`): `Assets/Veilwalkers/AR/ICameraPermission.cs`, `CameraPermissionFlow.cs`, `CameraPermissionState.cs` (or nested in the flow), `AndroidCameraPermission.cs` (+ `.meta`s). FIRST `.cs` in the AR assembly.
- NEW (production, `Veilwalkers.UI`): `Assets/Veilwalkers/UI/ARHud/CameraPermissionView.cs` (+ `.meta`). FIRST file under `UI/ARHud/`.
- UPDATE (`Veilwalkers.App`): `Bootstrap.cs` — construct `AndroidCameraPermission` + `CameraPermissionFlow`, register the flow in `GameServices` (or seam-comment per Task 5 decision).
- NEW (tests): `Assets/Tests/EditMode/AR.Tests/Veilwalkers.AR.Tests.asmdef` (+ `.meta`), `CameraPermissionFlowTests.cs` (+ `.meta`), `FakeCameraPermission.cs` (+ `.meta`), and the AC-3 source-scan test (`CameraPermissionLocationGuardTests.cs` or folded into the flow tests).
- **Asmdef changes:** NONE to existing assemblies (AR refs only Core — unchanged; UI already refs AR). ONE NEW test asmdef (`AR.Tests`, `.Tests`-excluded from the acyclic guard). Confirm the AR runtime asmdef does NOT need a new engine-module reference for `UnityEngine.Android.Permission` (it's in CoreModule/AndroidJNIModule, auto-referenced) — verify at compile, record in Dev Agent Record.
- NO scene, NO prefab, NO `.asset`, NO design tokens. View placement + AR-entry routing = Epic 6 Story 6.3/6.4.

### References
- [Source: docs/epics.md#Story-3.1] — the three ACs verbatim (disclose-before-prompt; denial → re-grant + settings path, never a dead end; Location never requested).
- [Source: docs/epics.md#Epic-3] — "camera-permission disclose-before-prompt + graceful denial"; FR-5.
- [Source: docs/epics.md#FR-5] — "request with disclosure; denial blocks AR Mode and shows an explanatory re-grant screen (no crash/dead-end). Location not requested in MVP."
- [Source: docs/epics.md#UX-DR14] — diegetic voice EXCEPTION: "safety messaging plain/legible, never buried in flavor" (the permission/safety copy stays plain — Epic 6 owns the copy, but note the constraint).
- [Source: docs/epics.md#UX-DR16] — "Banned: any flow that strands the player" (AC-2 no-dead-end rule); "Camera denied = explanatory re-grant path, never dead-end" (UX-DR15).
- [Source: docs/epics.md#UX-DR19] — onboarding "camera disclosure card BEFORE OS dialog" (Epic 6 visual; this story builds the flow it drives).
- [Source: docs/architecture.md:404-411] — the AR area layout; `CameraPermissionFlow.cs # FR-5 disclose-before-prompt` is the named home.
- [Source: docs/architecture.md:466-468 (AR boundary)] — "ArSessionService is the sole owner; AR contains no branching logic worth testing (thin adapter)" — the mandate decision #2 reconciles.
- [Source: docs/architecture.md:503 (Requirements→Structure)] — "FR-3/FR-5 → AR/ArSafetyGate (cold-entry rule), AR/CameraPermissionFlow."
- [Source: docs/architecture.md AR-4, AR-5, AR-19; NFR-3] — ctor-injection vs locator, acyclic AR-tier placement, GameLog, typed-result/never-crash.
- [Source: Assets/Veilwalkers/Core/IClock.cs, SystemClock.cs] — the seam template (public interface + production impl) to mirror for `ICameraPermission`/`AndroidCameraPermission`.
- [Source: Assets/Veilwalkers/AR/Veilwalkers.AR.asmdef] — the AR assembly (refs only `Veilwalkers.Core`; gets its first `.cs` here).
- [Source: Assets/Veilwalkers/UI/Codex/CodexGridView.cs, CodexDetailView.cs] — the graceful-degrade view pattern (`GameServices.Get` guard, `Render` stub, `TODO(Epic 6)`) to mirror for `CameraPermissionView`.
- [Source: Assets/Veilwalkers/App/Bootstrap.cs] — the wiring/registration shape to follow (construct impl → register in GameServices).
- [Source: Assets/Veilwalkers/Core/GameServices.cs, ServicesNotReadyException.cs] — the locator + the exception the view catches.
- [Source: _bmad-output/implementation-artifacts/2-5-view-a-monsters-detail-entry.md] — prior story; the per-assembly fake pattern, the graceful-degrade view, the anti-tautology bar.
- [Source: _bmad-output/implementation-artifacts/2-4-browse-the-67-grid-with-hybrid-gradual-reveal.md] — the FIRST UI story; the UI-altitude split this story re-applies.

## Technical Requirements

- C# / Unity 2022.3 LTS. `CameraPermissionFlow` + `CameraPermissionState` are plain C# (NO `MonoBehaviour`, NO Unity types — headless-testable). `AndroidCameraPermission` is the platform adapter (`#if UNITY_ANDROID && !UNITY_EDITOR` real / else no-op). `CameraPermissionView` is the only `MonoBehaviour` + the only locator reader.
- The OS-permission result is POLLED (re-query `HasCameraPermission` after focus-regain), NOT awaited — `Permission.RequestUserPermission` returns void in Unity. The flow is synchronous; no async/`Task` (decision #3). No new package dependencies (`UnityEngine.Android.Permission` is in the engine).
- **Asmdef changes:** NONE to existing assemblies. ONE NEW `AR.Tests` test asmdef (refs `Veilwalkers.AR` + `Veilwalkers.Core`, `.Tests`-excluded). NO allow-matrix edit. Verify the AR runtime asmdef compiles the `Permission`/Intent calls without a new engine-module reference (record the finding).
- Logging via `GameLog` (Core), never `UnityEngine.Debug` (AR-19). Location permission API is NEVER referenced anywhere in the AR assembly (AC-3, pinned by the source-scan test).

## Testing Requirements

- **Framework:** Unity Test Framework 1.1.33, NUnit, EditMode. The flow is synchronous → plain `[Test]`, no `[UnityTest]`, no async idiom. `FakeCameraPermission` drives all state (a settable bool + call counters); no device, no real OS dialog.
- **Headless command** ([[unity-headless-testing]]):
  ```
  "/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode \
    -projectPath "C:/Users/james/Desktop/Veilwalkers" -runTests -testPlatform EditMode \
    -testResults "<abs>/test-results.xml" -logFile "<abs>/editor-test.log" -nographics
  ```
  Editor fully closed (`Temp/UnityLockfile` absent). **Never trust exit 0** — confirm `test-results.xml` `result="Passed"` AND grep the log for `error CS` (0). Confirm the NEW `AR.Tests` assembly actually compiled + ran (the `Veilwalkers.AR.Tests.dll` appears with a non-zero `testcasecount`, names present — the new-assembly trap: a brand-new asmdef that fails to compile can silently contribute zero tests while the rest stay green). Baseline at post-2.5 HEAD is **208** cases; gate is "all green, 0 failed, 0 compile errors" + the new AR tests present, NOT a target number. Delete `test-results.xml` + `editor-test.log` before commit.
- **No PlayMode tests** — `CameraPermissionView` verified by inspection (graceful degrade, `Render` stub binds the headless-tested flow, focus-pump is a one-line re-query, no logic). The real OS dialog / settings Intent is a device release-gate check, not a unit test (the AR-mandate "no branching worth testing in the adapter" — the adapter is `#if`-guarded platform glue).
- **Anti-tautology:** the AC-1 disclosure-before-prompt test asserts `RequestCount == 0` after `Evaluate()` AND `== 1` only after `ConfirmDisclosureAndRequest()` (a prompt-first regression → red); the AC-2 recovery test flips the fake to granted and asserts the production flow returns `Granted` (not the fake's input); the AC-3 source-scan asserts a banned location token is absent from the AR `.cs` (add a location request → red — a falsifiable guard, not a hand-built equality). The illegal-transition test asserts the warn + no-op (mutation-testable).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (story-automator, ungated run).

### Debug Log References

- Headless EditMode run (Unity 2022.3.62f3, `-batchmode -runTests -testPlatform EditMode -nographics`): **221 total, 221 passed, 0 failed, 0 skipped**, `result="Passed"`, exit 0; `grep "error CS"` → **0**. Baseline at post-2.5 HEAD was 208 → **+13 new cases** (all in the new `Veilwalkers.AR.Tests.dll`).
- New-assembly trap checked: `Veilwalkers.AR.Tests.dll` appears `runstate="Runnable" testcasecount="13" result="Passed"` (13/13 — genuinely compiled + ran, NOT a silent zero). Breakdown: `CameraPermissionFlowTests` 11 cases (ctor-null, AC-1 disclose-before-prompt ×3, dialog grant/deny ×2, AC-2 settings/recover/persist ×3, illegal-transition ×2), `CameraPermissionLocationGuardTests` 2 cases (AC-3 reflection + source-scan). `AcyclicDependencyTests` still green (no asmdef edge added — AR refs only Core, App/UI already ref AR; `.Tests` suffix excludes `AR.Tests` from the guard).
- Compiled clean on the FIRST run — no iteration needed. The AR runtime asmdef compiled the `Permission`/`AndroidJavaObject` calls (in `AndroidCameraPermission`) without any new engine-module/precompiled reference: they sit behind `#if UNITY_ANDROID && !UNITY_EDITOR` (compiled OUT of the EditMode run) and the engine modules are auto-referenced (`noEngineReferences: false`).
- Artifacts (`test-results.xml`, `editor-test.log`) deleted before commit (not gitignored). [[unity-headless-testing]]

### Completion Notes List

- **All three ACs delivered, with the testable logic isolated from the platform edge** (the locked altitude, [[ui-presenter-pattern]] generalized to a platform seam): AC-1 (disclosure precedes the OS prompt), AC-2 (denied → re-grant screen + Settings path → recover, never a dead end), and AC-3 (Location never requested) live in the headless-tested `CameraPermissionFlow`; the OS-touching `AndroidCameraPermission` is a thin `#if`-guarded adapter, and the pixels defer to Epic 6.
- **First AR-area code in the project.** The `Veilwalkers.AR` assembly had existed since Story 1.2 with zero `.cs`; this story adds its first four: `ICameraPermission` (seam), `CameraPermissionState` (enum), `CameraPermissionFlow` (the pure-logic state machine), `AndroidCameraPermission` (the platform adapter). No asmdef edge change — AR still references only `Veilwalkers.Core`.
- **Decision #1 (seam placement) — AR, not Core.** `ICameraPermission` is an Android-platform behavior (mirrors `IArAnchorProvider`); placing it in AR keeps Core platform-free and matches the architecture's `FR-5 → AR/CameraPermissionFlow` mapping. The production impl `AndroidCameraPermission` is the `SystemClock : IClock` equivalent.
- **Decision #2 (the AR "thin adapter, zero branching logic" tension) — reconciled, not violated.** That mandate targets the OS-touching adapter; `AndroidCameraPermission` honors it (no decisions beyond the platform `#if`). The disclose→prompt→denied→re-grant STATE MACHINE is branching worth testing, so it lives in the pure-logic `CameraPermissionFlow` behind the seam — exactly the architecture's intent ("keep branching out of the adapter"). Called out for the CR.
- **Decision #3 (no async — polled on focus-regain) — as written.** Unity's `Permission.RequestUserPermission` returns void; a `PermissionCallbacks` overload exists but (a) is OEM-flaky and (b) does NOT fire for the return-from-Settings round-trip (AC-2's recovery). So the flow is synchronous: `ConfirmDisclosureAndRequest()` fires the prompt → `Requesting`; the view pumps `OnRequestResult()` on `OnApplicationFocus(true)` to re-query and resolve. One resolution mechanism for both the dialog result AND the Settings return; deterministically testable (the fake flips a bool).
- **AC-1 disclose-before-prompt is pinned hard:** `Evaluate()` with no permission returns `Disclosure` AND asserts `FakeCameraPermission.RequestCount == 0` — a regression that prompts first turns the suite red. The ONE prompt fires only from `ConfirmDisclosureAndRequest()` (reachable only from `Disclosure`), asserted `RequestCount == 1`.
- **AC-2 never-a-dead-end is pinned three ways:** denied dialog → `Denied`; `RetryFromSettings()` opens settings (`OpenSettingsCount == 1`) and STAYS `Denied` (no premature flip) until the player returns; grant-in-settings + `OnRequestResult()` → `Granted` (recovery); return-still-denied → stays `Denied` and can retry (`OpenSettingsCount == 2`). The "Don't ask again" / permanently-denied edge is handled by construction — the same Settings path — and documented as WHY the Settings escape hatch is load-bearing.
- **AC-3 Location-never-requested is pinned two ways, falsifiably:** (a) a reflection test asserts `ICameraPermission` exposes no location member (the seam shape forbids it by construction); (b) a source-scan over the AR `.cs` asserts no `FineLocation`/`CoarseLocation`/`LocationService`/`ACCESS_*_LOCATION` token appears — and the scan FIRST asserts it found a non-zero number of AR source files (anti-vacuity guard) so a wrong path can't pass green. A future story adding a location request → red.
- **Illegal transitions never throw (NFR-3):** `ConfirmDisclosureAndRequest()` while `Granted`, and `RetryFromSettings()` while not `Denied`, are each a `GameLog.Warn` + current-state no-op (pinned with `LogAssert.Expect` + a `RequestCount`/`OpenSettingsCount` unchanged assertion). A UI-driven flow must not crash the screen.
- **Registered LIVE in Bootstrap** (unlike CodexService, which is seam-only pending its `.asset`): `new CameraPermissionFlow(new AndroidCameraPermission())` → `GameServices.Register<CameraPermissionFlow>()`. It has no unauthored-asset blocker — the adapter is a no-op off-device, so construction never throws at boot. The CONSUMER `CameraPermissionView` resolves it via `GameServices.Get` and degrades gracefully while unplaced (the 2.4/2.5 view precedent).
- **View is logic-complete but unplaced (Epic 6).** `CameraPermissionView : MonoBehaviour` (UI/ARHud — the first file under that folder) binds the flow: `BeginTowardArMode()`/`OnDisclosureContinue()`/`OnOpenSettings()` actions + an `OnApplicationFocus` pump; `Render` is a stubbed `GameLog.Info` breakdown with a `TODO(Epic 6)`. The disclosure-card/re-grant-screen pixels (UX-DR3, the plain-not-flavored permission copy per the UX-DR14 exception, the onboarding camera-disclosure card UX-DR19) and the scene placement + AR-entry routing (Story 6.3/6.4) defer to Epic 6.
- **Scope held:** stops at "permission resolved → may proceed / denied → re-grant." Does NOT show the AR Safety Warning (Story 3.2), start `ArSessionService` / prewarm (Story 3.3), detect planes (3.4/3.5), or touch location (AC-3 — Phase 3). No scene, no prefab, no design tokens.

### File List

**Production — NEW (`Veilwalkers.AR`):**
- `Assets/Veilwalkers/AR/ICameraPermission.cs` (+ `.meta`) — the camera-only platform seam (AC-3 by shape).
- `Assets/Veilwalkers/AR/CameraPermissionState.cs` (+ `.meta`) — the `Disclosure`/`Requesting`/`Granted`/`Denied` enum.
- `Assets/Veilwalkers/AR/CameraPermissionFlow.cs` (+ `.meta`) — the pure-logic disclose→prompt→denied→regrant state machine.
- `Assets/Veilwalkers/AR/AndroidCameraPermission.cs` (+ `.meta`) — the `#if UNITY_ANDROID` production adapter (real on Android / logged no-op off-device).

**Production — NEW (`Veilwalkers.UI`):**
- `Assets/Veilwalkers/UI/ARHud/CameraPermissionView.cs` (+ `.meta`) — the thin graceful-degrade view (first file under `UI/ARHud/`; folder `.meta` also new).

**Production — UPDATED:**
- `Assets/Veilwalkers/App/Bootstrap.cs` — `using Veilwalkers.AR;`; construct `new CameraPermissionFlow(new AndroidCameraPermission())` and `GameServices.Register<CameraPermissionFlow>()` (live, with a seam-doc comment). No asmdef change (App already refs AR).

**Tests — NEW (`Veilwalkers.AR.Tests` — the project's FIRST AR test assembly):**
- `Assets/Tests/EditMode/AR.Tests/Veilwalkers.AR.Tests.asmdef` (+ `.meta`; folder `.meta` also new) — full field set mirrored from `Monsters.Tests` (TestRunner refs + `nunit.framework.dll` + `UNITY_INCLUDE_TESTS` + `Editor` platform).
- `Assets/Tests/EditMode/AR.Tests/FakeCameraPermission.cs` (+ `.meta`) — settable `HasCameraPermission` + `RequestCount`/`OpenSettingsCount` counters.
- `Assets/Tests/EditMode/AR.Tests/CameraPermissionFlowTests.cs` (+ `.meta`) — 11 cases (AC-1/AC-2 + illegal-transition).
- `Assets/Tests/EditMode/AR.Tests/CameraPermissionLocationGuardTests.cs` (+ `.meta`) — 2 cases (AC-3 reflection + anti-vacuous source-scan).

**Docs / tracking — UPDATED:**
- `_bmad-output/implementation-artifacts/3-1-disclose-and-request-camera-permission-gracefully.md` (frontmatter `baseline_commit`, task checkboxes, Dev Agent Record, File List, Change Log, Status).
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (epic-3 → in-progress at story creation; 3.1 → review).

### Change Log

- 2026-06-15 — Story 3.1 implemented (221/221 headless green, +13). Added the FIRST AR-area code: the `ICameraPermission` platform seam (camera-only — AC-3 by shape), `AndroidCameraPermission` (`#if UNITY_ANDROID` adapter / off-device no-op), the pure-logic `CameraPermissionFlow` + `CameraPermissionState` (disclose-before-prompt AC-1, denied→settings→recover AC-2, illegal-transition-safe NFR-3), and the thin graceful-degrade `CameraPermissionView` (UI/ARHud, `Render` stubbed). Registered the flow live in Bootstrap. New `Veilwalkers.AR.Tests` assembly with `FakeCameraPermission` + AC-1/2/3 tests (incl. a falsifiable anti-vacuous source-scan for AC-3). Pixels + scene placement + AR-entry routing deferred to Epic 6; the Safety Warning (3.2) and session lifecycle (3.3) are later Epic-3 stories. Status → review.
- 2026-06-15 — Code review (story-cr-fanout, 3-layer adversarial, 14 findings → **3 patch / 1 defer / 6 spec-sanctioned / 4 false-positive**). Applied 3 confirmed patches, all hardening the Android device paths the EditMode fake can't exercise: (1) the view's focus-regain re-query now requires a real focus-LOSS first (`_sawFocusLossWhileOutstanding` latch) so the spurious focus-regain when the OS dialog OPENS no longer prematurely flips `Requesting→Denied` and buries the live dialog; (2) the `Uri.fromParts` JNI call uses a TYPED `(string)null` to pin the correct overload (a bare null can throw and kill the sole "Don't ask again" recovery path); (3) `OpenAppSettings` pre-checks `Intent.resolveActivity` and logs a diagnosable error instead of letting `startActivity` fail SILENTLY (a dead end AC-2 forbids). One defer: re-validating `Granted` after a permission revoked-while-backgrounded → Story 3.3 (owns AR-entry gating). Headless gate re-run after patching: **221/221, 0 compile errors** (patches 2/3 are in the `#if UNITY_ANDROID` block compiled out of EditMode; patch 1 is in the logic-free view, verified by inspection per the house no-PlayMode convention — the fake seam can't drive Unity's `OnApplicationFocus` race, as the CR itself noted). Status → done.

## Review Findings

Adversarial 3-layer code review (story-cr-fanout: Blind Hunter / Edge Case Hunter / Acceptance Auditor, verify-disputed-first triage), 2026-06-15. **14 raw → 14 deduped (0 cross-layer convergence) → 3 confirmed/patch (1 an AC violation) / 1 defer (out-of-scope) / 6 spec-sanctioned / 4 false-positive.** All 3 patches applied; the headless gate stays **221/221** (the patches live on Android-device / view paths the EditMode suite does not compile or exercise — verified by inspection + a clean recompile). The defer is recorded in `deferred-work.md`.

- [x] [Review][Patch] **Focus-regain re-query resolved `Denied` prematurely while the OS dialog was still on screen** (`CameraPermissionView.cs`, major) — on Android, presenting the permission dialog fires `OnApplicationFocus(false)`→`(true)` BEFORE the player answers; the unguarded regain re-queried (still false) and flipped `Requesting→Denied`, burying the live dialog under the re-grant screen. **Fix:** a `_sawFocusLossWhileOutstanding` latch — only resolve on a regain PRECEDED by a real focus-loss (the dialog/Settings genuinely closed). Verified by inspection (the fake seam can't drive Unity's focus lifecycle; house no-PlayMode convention).
- [x] [Review][Patch] **`Uri.fromParts` JNI call passed an untyped `null`, risking overload-binding failure on the AC-2 recovery path** (`AndroidCameraPermission.cs`, minor) — a bare C# `null` can fail to resolve `Uri.fromParts(String,String,String)` and throw, which the catch swallows → the sole "Don't ask again" recovery silently dies. **Fix:** typed `(string)null` pins the overload.
- [x] [Review][Patch][AC-2] **`OpenAppSettings` could let `startActivity` fail SILENTLY without throwing** (`AndroidCameraPermission.cs`, major, AC violation) — `startActivity` returns void; on a device where no activity resolves the Intent it fails with no exception, stranding the player on the re-grant screen (the dead end AC-2 forbids). **Fix:** pre-check `Intent.resolveActivity(packageManager)` and `GameLog.Error` + return on null instead of a silent no-launch.
- [x] [Review][Defer] **`Granted` is never re-validated, so a permission revoked while backgrounded is missed** (`CameraPermissionView.cs`, major, out-of-scope) — Android can revoke camera permission while backgrounded (Settings toggle / unused-app auto-reset); the focus pump only re-queries `Requesting`/`Denied`, so a revoked-then-returned app still believes `Granted`. Real, but AR-entry gating (where permission validity must be re-checked before launching a session) is **Story 3.3**'s job (`ArSessionService` lifecycle); 3.1's scope ends at "permission resolved → may proceed / denied → re-grant". **New deferral → deferred-work.md** (Owner: Story 3.3).
- [x] [Review][Sanctioned] **`OnRequestResult` is guard-less / reachable from `Granted`** (`CameraPermissionFlow.cs`) — intentional and documented: it is a stateless re-query, explicitly "safe to call from Requesting OR Denied; a stray call resolves on current OS state too" (the view only pumps it from those states). Unlike the two guarded mutators, it needs no guard. Drop.
- [x] [Review][Sanctioned] **Off-device adapter reports camera authorized → only the `Granted` path is reachable in-editor** (`AndroidCameraPermission.cs`) — the sanctioned thin-adapter design (Dev Notes: flow logic proven against the fake, not the real adapter off-device). Drop.
- [x] [Review][Sanctioned] **`OnRequestResult` doc-comment vs the class transition table read as two contracts** (`CameraPermissionFlow.cs`, nit) — a doc-clarity style clash (happy-path table vs safe-contract method doc); the implementation is correct and Task 2 lists only the two mutators as requiring guards. Drop.
- [x] [Review][Sanctioned] **Focus-regain re-query could read stale `HasUserAuthorizedPermission` before subsystems resync** (`CameraPermissionView.cs`) — the sanctioned decision-#3 focus-poll design; Android permission state is stable by focus-regain. (Note: the genuine *race* — regain while the dialog is still up — IS the patched finding above; this separate "stale-read after resync" concern is the sanctioned one.) Drop.
- [x] [Review][Sanctioned] **`Render()` switch has no `default` case** (`CameraPermissionView.cs`) — `Render` is an explicit Epic-6 stub (the 2.4/2.5 `CodexGridView`/`CodexDetailView` precedent); defensive exhaustiveness belongs to the Epic-6 rewrite. Drop.
- [x] [Review][Sanctioned] **Off-device `HasCameraPermission` hardcoded `true` makes the Denied path unreachable in-editor** (`AndroidCameraPermission.cs`) — the sanctioned untestable-adapter edge (the `SystemClock` equivalent); all flow logic is proven headless against the fake. Drop.
- [x] [Review][False-positive] **`CameraPermissionView.Awake` reads `GameServices` with no `[DefaultExecutionOrder]` guard** — the graceful-degrade inert path is the spec-sanctioned design (the 2.4/2.5 view precedent); Bootstrap already runs at `-1000` and every action null-guards. Drop.
- [x] [Review][False-positive] **`OnRequestResult` is not idempotent under rapid successive calls** — it is a stateless re-query by design (documented); repeated calls re-observe the same OS state and are harmless. Drop.
- [x] [Review][False-positive] **Duplicate `CameraPermissionView` instances would each pump `OnRequestResult`** — correct Unity per-instance lifecycle, not a bug; the call is idempotent and scene/instance placement is Epic 6. Drop.
- [x] [Review][False-positive] **No test for a rapid double `ConfirmDisclosureAndRequest`** — the sole caller is a single-threaded, non-reentrant Unity button handler; no AC/NFR requires concurrent-call safety, and the "exactly one prompt" rule IS pinned for the legal path. Drop.
