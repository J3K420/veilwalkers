# Story 8.3: Author the AR rig scene contents and its device glue

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

baseline_commit: 7465c05

## Story

As a developer making the MVP renderable on a real device,
I want the AR Foundation / ARCore rig authored into `ARHunt.unity` and the three AR device adapters' `#if UNITY_ANDROID && !UNITY_EDITOR` bodies (`ArcoreSession`, `ArcoreAnchorProvider`, `GameObjectSpawnSink`) implemented against the scene rig, plus a monster prefab to spawn,
so that on a Galaxy S21+ a lured Monster rests on a detected plane, is anchored, occluded, and lit — closing the device half of Epics 3 & 4 that the green pure-logic floor was built against.

## ⚠️ Verifiability reality (the load-bearing scope decision for THIS story)

**8.3 is a DEVICE story, not a headless one.** A recon (Explore analysis, this run) confirmed the hard split — and it is more device-weighted than even 8.2:

- The three production adapters today reference **ZERO** AR Foundation / ARCore types. The real device code lives entirely inside `#if UNITY_ANDROID && !UNITY_EDITOR` blocks, which are **excluded from EditMode compilation** and **CI-untestable by design** (architecture.md:591).
- **No EditMode test touches the production adapters.** Grep `ArcoreSession|ArcoreAnchorProvider|GameObjectSpawnSink` across `Assets/Tests` = zero matches. The AR services are proven through Fakes (`FakeArSession`, `FakeArAnchorProvider`, `FakeSpawnSink`).
- Therefore **8.3's verification gate is the on-device AR smoke test** (`docs/epic-8-device-release-gate.md` Gate 1 / Gate 5), NOT new EditMode tests.

**The honest split:**

- **SAFELY HEADLESS (the ONE auto-verifiable slice):**
  1. A non-vacuous **scene-asset-as-text component-presence guard** that reads `ARHunt.unity` as YAML off disk and asserts the AR rig components are present (`ARSession`, an XR Origin / `ARSessionOrigin`, `ARPlaneManager`, `ARAnchorManager`, `ARRaycastManager`, `AROcclusionManager`, `ARCameraManager`) — the Pit/EditorBuildSettings text-read precedent (the Epic-8 verifiability invariant, option 1). This is authored to assert the rig IS present, so it is **`[Ignore]`-pending-Editor** until the rig is authored (a correct scene with valid component fileIDs/guids is Editor work, not safe hand-YAML), exactly like 8.2's `_economyConfig` pin. Never green-but-false.
  2. **Confirm the carried-forward AR regressions stay green** with the adapters' editor `#else` branches and the seam signatures **untouched**: `MonsterSpawnerTests` (cap/pool accounting via `FakeSpawnSink`), `AnchorRestoreServiceTests` (pose decisions via `FakeArAnchorProvider`), `PlaneAnchorServiceTests` (coach-vs-place via `FakeArAnchorProvider`), `ArSessionServiceTests` (lifecycle via `FakeArSession`). 8.3 must change none of them.

- **DEVICE / EDITOR CHECKLIST (NOT auto-claimed done — emitted for the operator + a Galaxy S21+):**
  3. Author the AR rig GameObjects into `ARHunt.unity`.
  4. Implement the `#if UNITY_ANDROID && !UNITY_EDITOR` device bodies in all three adapters against the scene rig.
  5. Author the monster prefab(s) + the URP pool.
  6. On-device: plane detection → place → anchor → occlusion + lighting → re-anchor moves the GameObject → pooled spawn under the frame cap; the AR recovery paths (Restored / RelocatedToPlane / Failed).

## Acceptance Criteria

Sourced from `docs/epics.md#Epic-8`, `docs/epic-8-device-release-gate.md` Gate 1, and Stories 3.3/3.4/3.5/4.7 ACs (the device half of contracts already proven headlessly). Re-scoped per the verifiability split above.

**AC-1 (DEVICE/EDITOR) — the AR rig is authored into `ARHunt.unity`**
**Given** the placeholder `ARHunt.unity` (authored empty in 8.2)
**When** the AR rig is built into it
**Then** the scene contains `ARSession`, an XR Origin (`ARSessionOrigin`/`XROrigin`) with the AR camera + camera-background, `ARPlaneManager`, `ARAnchorManager`, `ARRaycastManager`, `AROcclusionManager`, and `ARCameraManager`, with the ARCore loader enabled (the `Assets/XR/Loaders/ARCoreLoader.asset` + `Assets/XR/Settings/ARCoreSettings.asset` already present). **Editor work** — a correct rig only verifies on device.

**AC-2 (DEVICE) — the `ArcoreSession` device body drives the real `ARSession`**
**Given** `ArcoreSession`'s `#if UNITY_ANDROID && !UNITY_EDITOR` block (currently a stub — `IsSupported => true`, `StartAsync => Task.CompletedTask`, toggles no-op)
**When** it is implemented against the scene `ARSession`
**Then** `IsSupported` reads `ARSession.state` (Ready/SessionInitializing → supported; Unsupported/NeedsInstall → not), `StartAsync` enables the `ARSession` + awaits a tracking-ready state, `Pause`/`Resume` toggle `ARSession.enabled`, and `Stop` calls `ARSession.Reset()`/disable AND honors the load-bearing **`Stop`-cancels-an-in-flight-`StartAsync`** contract (`IArSession.cs` L72–82). On device, `ArSessionService` prewarms cold→prewarming→ready without crashing (NFR-3).

**AC-3 (DEVICE) — the `ArcoreAnchorProvider` forward path places + anchors on a real plane**
**Given** the provider's forward `#if` stub (`HasTrackablePlane => false`, `TryGetPlacementPose`/`TryCreateAnchor` → false/default)
**When** implemented against `ARPlaneManager` / `ARRaycastManager` / `ARAnchorManager`
**Then** `HasTrackablePlane` reflects tracked planes (`ARPlaneManager.trackables` with `trackingState == Tracking`), `TryGetPlacementPose` raycasts screen-center against `PlaneWithinPolygon` and returns the hit pose + `TrackableId`, and `TryCreateAnchor` attaches a native anchor and builds the serializable `AnchorToken` ({trackableId, session-relative pose}). On device a lured Monster rests on a detected plane and never spawns into empty space (AC-2 of Story 3.4: no plane → coach, via the unchanged `PlaneAnchorService`).

**AC-4 (DEVICE) — the `ArcoreAnchorProvider` restore path re-acquires / relocates**
**Given** the provider's restore `#if` stub (`TryReacquireAnchor` → false, `TryGetRelocationCandidates` → empty)
**When** implemented against `ARAnchorManager` re-acquire + `ARPlaneManager` frustum projection
**Then** `TryReacquireAnchor` re-resolves the saved `TrackableId` (the `Restored` case), and `TryGetRelocationCandidates` returns currently-tracked planes as `PlaneCandidate[]` with `InCameraFrustum` (via `Camera.main.WorldToViewportPoint`) + camera-to-plane `DistanceFromCamera` (the `RelocatedToPlane` case). On device, losing an anchor mid-encounter resolves to Restored / RelocatedToPlane (with the "pulled back through the Veil" Lure-VFX beat) / Failed via the unchanged `AnchorRestoreService` ranking logic — never vanishes (Story 3.5).

**AC-5 (DEVICE) — the `GameObjectSpawnSink` drives a real pooled prefab**
**Given** the sink's `#if` stub (`Instantiate => _nextId++`, `Activate`/`Deactivate` no-op)
**When** implemented against the monster prefab pool placed under the AR session origin
**Then** `Instantiate` does `Object.Instantiate(monsterPrefab, pose.position, pose.rotation)` and returns a stable instance id, `Activate` re-shows + repositions (`SetActive(true)` + `SetPositionAndRotation`), `Deactivate` hides + returns to pool (`SetActive(false)`). On device, pooled spawning honors the `MonsterSpawner` concurrent-cap (default 8, NFR-1) — re-using via `Activate`, never over-instantiating.

**AC-6 (DEVICE) — occlusion, lighting, and the monster prefab render**
**Given** `AROcclusionManager` + light estimation (`ARCameraManager`) on the rig and a URP monster prefab
**When** a Monster is spawned on device
**Then** real-world foreground occludes it (depth) and it is environmentally lit; the prefab carries its model/materials (and the Nightveil-Filter shader hook reserved for Story 4.6/8.5). Re-anchor MOVES the spawned GameObject to the restored pose.

**AC-7 (HEADLESS) — the scene-component-presence guard + the AR regression floor**
**Given** the AR rig authored into `ARHunt.unity` (AC-1)
**When** the EditMode gate runs
**Then** a scene-as-text guard asserts the rig component types are present in `ARHunt.unity` (authored `[Ignore]`-pending until the rig lands — a visible pending pin, never faked green), AND `MonsterSpawnerTests` + `AnchorRestoreServiceTests` + `PlaneAnchorServiceTests` + `ArSessionServiceTests` stay green (the editor `#else` stubs + the AR-Foundation-free seam signatures are untouched). **The ONLY headless slice.**

**AC-8 (HEADLESS) — no new illegal Veilwalkers asmdef edge; seams stay AR-Foundation-free**
**Given** the acyclic assembly graph and the seam interfaces
**When** the AR-Foundation package references are added to `Veilwalkers.AR.asmdef`
**Then** the new references are Unity package assemblies (`Unity.XR.ARFoundation`, `Unity.XR.CoreUtils`, `Unity.XR.ARSubsystems`/`Unity.XR.ARCore`, + the ARCore Extensions assembly if persistent/cloud anchors are used) — NOT `Veilwalkers.*` edges, so `AcyclicDependencyTests` (which only constrains `Veilwalkers.`-prefixed edges) stays green. CRITICAL: the seam interfaces (`IArSession`, `IArAnchorProvider`, `ISpawnSink`) and their value types (`Pose`, `PlaneCandidate`, `AnchorToken`) **stay AR-Foundation-free** — AR Foundation types live ONLY behind the `#if` in the three concrete adapters. Threading an AR Foundation type up through a seam would break every Fake and service and is forbidden.

### Derived / load-bearing requirements

- **NO green-but-false claims.** The AC-7 scene-presence guard must NOT pass against the current (rig-less) `ARHunt.unity`. Author it `[Ignore("Editor: author the AR rig into ARHunt.unity — Story 8.3 device checklist; un-ignore when the rig components are present")]` so it is a visible pending pin (the 8.2 `_economyConfig` precedent), OR have it assert the documented gap. Never faked green.
- **The editor `#else` branches stay byte-identical in behavior.** They are what keeps the AR services reachable in-editor (`IsSupported => true`, `StartAsync` completed, `HasTrackablePlane => false`, re-acquire fails, no candidates, `Instantiate => _nextId++`). Changing them would change in-editor behavior the (current + future) tests + Bootstrap construction rely on. Only the `#if UNITY_ANDROID && !UNITY_EDITOR` branch gets real code.
- **The seams stay AR-Foundation-free** (AC-8). This is the architecture's whole reason the logic is headless-provable — do not regress it.
- **Owner-tag reconciliation (must-do cleanup):** every device-body TODO and every editor-stub doc-comment is tagged `TODO(Story 6.3)` (the AR-rig scene was historically 6.3 before Epic 8 existed). When the device code is written, **retarget those `(Story 6.3)` tags to `(Story 8.3)`** (and the Bootstrap.cs L244–255 comment block) — otherwise the codebase keeps pointing at a story that no longer owns the work.
- **NO gameplay logic change.** 8.3 RENDERS/WIRES what exists. It does not re-decide a single contract (Epic 8 invariant). `PlaneAnchorService`, `AnchorRestoreService`, `MonsterSpawner`, `ArSessionService` — the pure-logic owners — are untouched.

## Tasks / Subtasks

- [x] **Task 1 (HEADLESS) — Recon (read-only baseline)**: CONFIRMED this run — the three adapters reference zero AR Foundation types; all real device code was `TODO(Story 6.3)` inside `#if UNITY_ANDROID && !UNITY_EDITOR` (now retargeted to 8.3, Task 6-8); no EditMode test touches them; `Veilwalkers.AR.asmdef` references only `Veilwalkers.Core`; `ARHunt.unity` exists (placeholder = Directional Light + Main Camera only, rig-less); the four regression-floor test files exist. Recorded.
- [x] **Task 2 (HEADLESS) — the scene-component-presence guard (AC-7)**: DONE — authored `Assets/Tests/EditMode/Architecture.Tests/ArSceneRigGuardTests.cs` (no asmdef edit — Editor-platform, the `BuildSettingsGuardTests` neighbor). It reads `ARHunt.unity` text and asserts all six AR managers + an XR Origin (`XROrigin`/`ARSessionOrigin`) are present (D1: all 7, maximally non-vacuous). Marked `[Ignore]`-pending-Editor — it FAILS against today's rig-less placeholder, so it is a VISIBLE pending pin, never green-but-false. Mutation-discipline note carried in the `[Ignore]` reason + the XML doc.
- [x] **Task 3 (HEADLESS) — confirm the AR regression floor (AC-7)**: DONE — the headless gate re-ran `MonsterSpawnerTests`, `AnchorRestoreServiceTests`, `PlaneAnchorServiceTests`, `ArSessionServiceTests` green (8.3 changed neither the seams nor the editor `#else` stubs; the only `.cs` edits were doc-comment/log-string owner-tag retargets inside the `#if` device blocks + a new ignored test).
- [ ] **Task 4 (DEVICE/EDITOR — emitted, NOT auto-claimed) — add the AR-Foundation references** to `Veilwalkers.AR.asmdef`. EMITTED to deferred-work.md + `docs/epic-8-device-release-gate.md#Gate-1`.
- [ ] **Task 5 (DEVICE/EDITOR — emitted) — author the AR rig into `ARHunt.unity`** (AC-1) + un-ignore `ArSceneRigGuardTests`. EMITTED to the checklist.
- [x] **Task 6 (owner-tag retarget DONE; device body emitted) — `ArcoreSession`**: the `(Story 6.3)` device-body TODO tag + log strings + ownership prose retargeted to `(Story 8.3)`, and the `Stop`-cancels-in-flight-`StartAsync` contract added to the device-body TODO. The device `#if` BODY implementation (AC-2) is EMITTED to the checklist (device-only).
- [x] **Task 7 (owner-tag retarget DONE; device bodies emitted) — `ArcoreAnchorProvider`**: both `(Story 6.3)` forward+restore TODO tags + log strings + ownership prose retargeted to `(Story 8.3)`; `PlaneCandidate.cs` `DistanceFromCamera` doc retargeted too. The device `#if` BODIES (AC-3 + AC-4) are EMITTED to the checklist (device-only).
- [x] **Task 8 (owner-tag retarget DONE; device body + prefab emitted) — `GameObjectSpawnSink`**: the `(Story 6.3)` device-body TODO tag + log strings + ownership prose retargeted to `(Story 8.3)`. The device `#if` BODY + the URP monster prefab (AC-5 + AC-6) are EMITTED to the checklist (device-only). Bootstrap.cs L250 device-glue comment retargeted too.
- [ ] **Task 9 (DEVICE — emitted) — on-device AR smoke (the verification gate)** on the Galaxy S21+. EMITTED to `docs/epic-8-device-release-gate.md` Gate 1 + Gate 5. NEVER auto-claimed done from a headless session.
- [x] **Task 10 (HEADLESS) — headless gate**: DONE — ran `-runTests` WITHOUT `-quit`, verified `test-results.xml` (`<test-run>` counts) + grepped `error CS`; artifacts deleted before commit. Result recorded in the Dev Agent Record below.

## Dev Notes

### Decisions to make

- **D1 — Scene-presence guard granularity.** Assert each of the 7 rig component types present in `ARHunt.unity` text, or a representative subset? RECOMMEND all 7 (ARSession, XR Origin, ARPlaneManager, ARAnchorManager, ARRaycastManager, AROcclusionManager, ARCameraManager) so removing any one fails the pin — maximally non-vacuous. **CR fix (this run):** match each component by its serialized `m_Script` **GUID**, NOT by its type name. AR Foundation managers are precompiled-package MonoBehaviours — a scene serializes them as `m_Script: {fileID: 11500000, guid: <package-script-guid>, type: 3}` with an EMPTY `m_EditorClassIdentifier`; the type-name text never appears in the YAML (so a type-name `Contains` is both un-greenable against a correct rig AND substring-collision-prone — `"ARSession"` ⊂ `"ARSessionOrigin"`). The guard resolves each component's GUID at test time from its package `.cs.meta` (tracking the installed AR Foundation version) and asserts that GUID is in the scene — exactly the GUID/serialized-field-string mechanism the `BuildSettingsGuardTests` precedent actually uses (Unity writes guids + field names verbatim). GUIDs are unique 32-hex, so no collision; removing any one component removes its guid → RED.
- **D2 — `TryReacquireAnchor` mechanism.** Plain `ARAnchorManager` trackable re-find, or ARCore Extensions persistent/cloud anchors? RECOMMEND start with trackable re-acquire within a session (the `Restored` case the restore tests model); cloud/persistent anchors are a heavier Firebase-adjacent feature — defer unless cross-session restore is required. The seam doc (`IArAnchorProvider.cs` L78–86) only promises same-trackable re-acquire.
- **D3 — Monster prefab source.** A placeholder primitive (a lit capsule/quad) for the first device smoke, or a real model? RECOMMEND a placeholder primitive prefab for AC-5/AC-6 smoke (the materialization VFX + final art are Story 8.5); 8.3 proves the spawn/anchor/occlusion pipeline, not the art.

### Current state of the files being modified (READ before implementing)

All three adapters have the SAME shape: a `#if UNITY_ANDROID && !UNITY_EDITOR` device block that is currently a non-crashing stub (logs + literal returns, tagged `TODO(Story 6.3)`), and a `#else` editor block that is the reachable-in-editor stub the services run against. **You implement the `#if` block; you leave the `#else` block alone.**

- **`Assets/Veilwalkers/AR/ArcoreSession.cs`** (`: IArSession`). Device `#if` block L30–62 (TODO L31–40). Editor `#else` L63–91 — KEEP: `IsSupported => true`, `StartAsync => Task.CompletedTask`, `Pause`/`Resume`/`Stop` no-op. Device targets: `ARSession`, `ARSession.state` {Ready, SessionInitializing, Unsupported, NeedsInstall}, `ARSession.enabled`, `ARSession.Reset()`. Load-bearing: `Stop` must cancel an in-flight `StartAsync` (`IArSession.cs` L72–82).
- **`Assets/Veilwalkers/AR/ArcoreAnchorProvider.cs`** (`: IArAnchorProvider`). Device `#if` block L32–83 (forward TODO L33–45, restore TODO L63–70). Editor `#else` L84–121 — KEEP: `HasTrackablePlane => false`, `TryGetPlacementPose`/`TryCreateAnchor`/`TryReacquireAnchor`/`TryGetRelocationCandidates` → false/default/empty (this drives the AC-2 coaching + restore-Failed paths in-editor). Device targets: `ARPlaneManager` (`.trackables`, per-plane `trackingState == Tracking`), `ARRaycastManager` (`.Raycast`, `TrackableType.PlaneWithinPolygon`), `ARAnchorManager` (`.AttachAnchor`/`.AddAnchor`), `TrackableId`, `Camera.main.WorldToViewportPoint`.
- **`Assets/Veilwalkers/AR/GameObjectSpawnSink.cs`** (`: ISpawnSink`). Device `#if` block L30–52 (TODO L31–37). Editor `#else` L53–72 — KEEP: `Instantiate => _nextId++`, `Activate`/`Deactivate` no-op. Device targets (NOT AR Foundation): `Object.Instantiate`, `GameObject.SetActive`, `Transform.SetPositionAndRotation`, the AR session origin, the URP monster prefab + pool. `MonsterSpawner` owns the pool/cap accounting — the sink only does GameObject work.

**Seam interfaces — DO NOT CHANGE (AR-Foundation-free is the whole point):**
- `IArSession.cs` — `IsSupported`, `StartAsync`, `Pause`, `Resume`, `Stop` (the `Stop`/in-flight contract L72–82).
- `IArAnchorProvider.cs` — `HasTrackablePlane`, `TryGetPlacementPose(out Pose, out string)`, `TryCreateAnchor(in Pose, string, out AnchorToken)` (this IS the "save" half), `TryReacquireAnchor(in AnchorToken, out Pose)`, `TryGetRelocationCandidates(out PlaneCandidate[])`.
- `ISpawnSink.cs` — `Instantiate(in Pose) : int`, `Activate(int, in Pose)`, `Deactivate(int)`.
- Value types: `PlaneCandidate` ({Pose, InCameraFrustum, DistanceFromCamera}), `PlacementResult`/`PlacementOutcome` (produced by `PlaneAnchorService`, not the adapter), `AnchorToken` (in `Veilwalkers.Core.Contracts`).

### What must be preserved (the system must stay working end-to-end)

- **Bootstrap construction stays no-throw.** `Bootstrap.cs` constructs all three adapters LIVE at boot (`new ArcoreSession()` L233, `new ArcoreAnchorProvider()` L260, `new GameObjectSpawnSink()` L263). Constructors must keep never throwing (NFR-3). The device bodies must tolerate the rig not yet being resolved at construction time (resolve the scene managers lazily / on first use, not in the ctor) — Bootstrap runs at `[DefaultExecutionOrder(-1000)]` before scene managers may be live.
- **The four AR.Tests files stay green** because the `#else` editor branch + the seams are untouched. The device `#if` branch is invisible to EditMode.
- **`MonsterDatabase.asset` + the monster prefab are distinct.** Gate 0 authored `MonsterDatabase.asset` (the static creature data); 8.3's prefab is the renderable GameObject the spawner instantiates. They are not the same asset.

### Previous story intelligence (Story 8.2 + Gate 0, commit 7465c05)

- **Gate 0 is closed** (commit 7465c05): `MonsterDatabase.asset` + 5 monsters authored, 5 scenes (incl. `ARHunt.unity` placeholder) + the six-slot build order, `_economyConfig` + `_monsterDatabase` assigned, the Bootstrap CodexService/EncounterService seams closed. EditMode gate **690/690 green, 0 skips**. 8.3 builds on this — `ARHunt.unity` exists as the placeholder to author the rig into.
- **The verifiability-split discipline (from 8.2):** author the headless guard `[Ignore]`-pending-Editor (never green-but-false); emit the device/Editor work as an OWNED checklist in `deferred-work.md` + `docs/epic-8-device-release-gate.md`; never auto-claim device work done. 8.3 follows this exactly.
- **Headless gate gotcha (learned this session):** `-runTests` must NOT be combined with `-quit` — `-quit` makes Unity exit before the async test runner executes (NO `test-results.xml` despite exit 0). Run without `-quit`; verify via `test-results.xml`, never the exit code.

### Target device

**Galaxy S21+** (confirmed). ARCore-certified, Android 11→14, Snapdragon 888 / Exynos 2100, ARM64 — comfortably above the project floor (min API 24, ARM64) and above the "mid-range reference device" the NFR-1 ≥30 FPS target assumes. Install "Google Play Services for AR" from the Play Store before the first smoke run (usually pre-installed). Deploy via USB (build-and-run) for the on-device gate.

### Project Structure Notes

- All adapter files live in `Assets/Veilwalkers/AR/` (the `Veilwalkers.AR` assembly). The asmdef gains Unity AR-Foundation package references only — no new `Veilwalkers.*` edge, so the acyclic matrix is untouched.
- The AR rig scene is `Assets/Veilwalkers/Scenes/ARHunt.unity` (build index 3). The XR loader/settings assets are under `Assets/XR/`.
- The new headless test joins `Assets/Tests/EditMode/Architecture.Tests/` (Editor-platform, the `BuildSettingsGuardTests` neighbor).

### References

- [Source: docs/epics.md#Epic-8] — the verifiability invariant (text-read / pure-logic / contract-shape pins only; device work never auto-claimed).
- [Source: docs/epic-8-device-release-gate.md#Gate-1] — the AR rig + device-glue checklist (the device half this story delivers).
- [Source: Assets/Veilwalkers/AR/ArcoreSession.cs (#if L30–62, #else L63–91); ArcoreAnchorProvider.cs (#if L32–83, #else L84–121); GameObjectSpawnSink.cs (#if L30–52, #else L53–72)] — the device-body TODO seams.
- [Source: Assets/Veilwalkers/AR/IArSession.cs L72–82] — the `Stop`-cancels-in-flight-`StartAsync` contract.
- [Source: Assets/Veilwalkers/AR/IArAnchorProvider.cs; ISpawnSink.cs; PlaneCandidate.cs] — the AR-Foundation-free seams the device adapters implement.
- [Source: Assets/Veilwalkers/App/Bootstrap.cs L233–264] — the live adapter construction + the `TODO(Story 6.3)` comment block to retarget.
- [Source: Packages/manifest.json] — `com.unity.xr.arfoundation` 5.2.2, `com.unity.xr.arcore` 5.2.2, ARCore Extensions (`#arf5`), URP 14.0.12.
- [Source: Assets/Tests/EditMode/AR.Tests/* — MonsterSpawnerTests, AnchorRestoreServiceTests, PlaneAnchorServiceTests, ArSessionServiceTests] — the regression floor that must stay green (the editor `#else` stubs + seams untouched).
- [Source: docs/architecture.md AR-9/AR-10/AR-14, NFR-1/NFR-3/NFR-5] — the AR session/anchor/staging contracts + device budgets.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8[1m] (story-automator, ungated mode — CS already done, entry at DS).

### Debug Log References

Headless EditMode gate (Editor 2022.3.62f3, closed; `-runTests` WITHOUT `-quit`; verified `test-results.xml` `<test-run>` + grepped `error CS`): **total 691 / passed 690 / failed 0 / skipped 1 (Ignored), 0 `error CS`** — ran TWICE (post-DS, then post-CR-patch), identical green shape both times. Baseline was 690/690/0/0 (Gate 0); the +1 is exactly the new `ArSceneRigGuardTests.ARHunt_scene_contains_the_AR_rig_component_types` pin, correctly `Ignored` (the visible pending pin — proves it is NOT silently green against the rig-less placeholder). The four AR regression-floor tests + `AcyclicDependencyTests` stayed green (0 failures), confirming the seams + editor `#else` stubs are untouched. The post-CR-patch re-run confirmed the guard rewrite (GUID-match) compiles + the pin still `[Ignore]`s. Artifacts deleted before commit.

### Completion Notes List

- **Scope reality (the load-bearing decision):** 8.3 is a DEVICE story. 6 of 8 ACs (AC-1..AC-6) are Editor/GPU/Galaxy-S21+ work — authoring the AR rig into `ARHunt.unity`, the three `#if UNITY_ANDROID && !UNITY_EDITOR` device bodies, the monster prefab, occlusion/lighting. None is auto-implementable or auto-verifiable headlessly (the `#if` device branch is excluded from EditMode compile; no EditMode test touches the production adapters — they are proven through Fakes). Per James's standing 8.2 pattern (ungated, confirmed this session): land the headless slice + emit the device work as an owned checklist; NOT auto-claimed renderable/installable.
- **Headless slice landed (AC-7 + AC-8):**
  1. `ArSceneRigGuardTests.cs` — a non-vacuous scene-asset-as-text component-presence pin asserting all six AR managers + an XR Origin are present in `ARHunt.unity`. Authored `[Ignore]`-pending-Editor because today's `ARHunt.unity` is the 8.2 placeholder (Directional Light + Main Camera only, ZERO AR rig) — the pin FAILS against it, so it is a VISIBLE pending pin, NEVER green-but-false (the 8.2 `_economyConfig` / Gate-0 `_monsterDatabase` precedent). Un-ignored by the Editor session that authors the rig; mutation-discipline (removing any one manager → RED) is documented in the `[Ignore]` reason.
  2. Owner-tag reconciliation (the must-do cleanup): every AR-device-body `TODO(Story 6.3)` retargeted to `(Story 8.3)` — `ArcoreSession` (1 TODO + 4 log strings + doc prose), `ArcoreAnchorProvider` (2 TODOs + 4 log strings + doc prose), `GameObjectSpawnSink` (1 TODO + 3 log strings + doc prose), `Bootstrap.cs` L250 device-glue comment, `PlaneCandidate.cs` `DistanceFromCamera` doc. The `Stop`-cancels-in-flight-`StartAsync` contract was added to the `ArcoreSession` device-body TODO. **Deliberately NOT retargeted:** UI/render/Billing `Story 6.3` mentions (those belong to 8.4/8.5/6.x) and historical citations (e.g. `ArEntryKind.cs` "owned by AppStateMachine (Story 6.3)" — a correct citation of where AppStateMachine was built; the Bootstrap L275 Unity-IAP glue TODO — that is Story 8.5's billing device glue).
- **AC-8 preserved:** no `Veilwalkers.*` asmdef edge added (no asmdef touched at all this session); the seams (`IArSession`/`IArAnchorProvider`/`ISpawnSink` + `Pose`/`PlaneCandidate`/`AnchorToken`) stay AR-Foundation-free. The editor `#else` stubs are byte-identical in behavior (untouched). `AcyclicDependencyTests` + the four AR.Tests regression files stay green.
- **Device work emitted (NOT done):** `docs/epic-8-device-release-gate.md#Gate-1` (Headless slice marked DONE; device steps detailed) + `deferred-work.md` ("Story 8.3 — AR rig + device-glue authoring checklist", 2026-06-17). Decisions D1 (all 7 components, either origin name), D2 (trackable re-acquire first, defer cloud anchors), D3 (placeholder primitive prefab for first smoke) recorded in the checklist.

### File List

**New:**
- `Assets/Tests/EditMode/Architecture.Tests/ArSceneRigGuardTests.cs` (+ `.meta`) — the AC-7 `[Ignore]`-pending scene-rig-presence pin.

**Modified (owner-tag retargets only — no behavior change; each edit is either inside an `#if UNITY_ANDROID` device block or in an always-compiled doc/comment — never in live editor `#else` logic):**
- `Assets/Veilwalkers/AR/ArcoreSession.cs`
- `Assets/Veilwalkers/AR/ArcoreAnchorProvider.cs`
- `Assets/Veilwalkers/AR/GameObjectSpawnSink.cs`
- `Assets/Veilwalkers/AR/PlaneCandidate.cs`
- `Assets/Veilwalkers/App/Bootstrap.cs` (L250 device-glue comment)

**Docs / tracking:**
- `docs/epic-8-device-release-gate.md` (Gate 1: headless slice DONE + device-step detail)
- `_bmad-output/implementation-artifacts/deferred-work.md` (Story 8.3 device checklist)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (8.3 → review → done)

### Review Findings

Adversarial 3-layer CR (`story-cr-fanout`: Blind Hunter / Edge Case Hunter / Acceptance Auditor + per-finding verifiers, 18 agents) over the working-tree diff vs baseline `7465c05`. **15 raw → 15 deduped → 8 confirmed (8 patched) / 1 defer (subsumed by a patch) / 4 spec-sanctioned / 2 false-positive; 1 AC violation (patched).** The CR earned its keep — it caught a load-bearing design defect in the sole headless deliverable that the story's own Dev Notes D1 shared.

- [x] [Review][Patch] **(AC violation, MAJOR — Edge Case Hunter, corroborated by Blind Hunter) The scene-rig guard was structurally un-greenable.** My `ArSceneRigGuardTests` matched AR manager presence by `sceneText.Contains(typeName)`, but AR Foundation managers are precompiled-package MonoBehaviours that serialize as `m_Script: {guid: …, type: 3}` with an EMPTY `m_EditorClassIdentifier` — the type-name text NEVER appears in the scene YAML (verified against the URP `UniversalAdditional*Data` components already in `ARHunt.unity`: a grep for their type names returns nothing). So the pin would FAIL even against a correctly-authored rig — "never-green-even-when-true," defeating AC-7. **Fix:** rewrote the guard to resolve each component's `m_Script` GUID at test time from its package `.cs.meta` (`Library/PackageCache`) and assert that GUID is in the scene — the GUID/field-string mechanism the cited `BuildSettingsGuardTests` precedent actually uses. Fails loudly (not vacuously) if the packages aren't resolved on disk. [Assets/Tests/EditMode/Architecture.Tests/ArSceneRigGuardTests.cs]
- [x] [Review][Patch] **(MAJOR ×4 — Blind Hunter ×2 + Edge Case Hunter ×2) Substring-collision vacuity: `"ARSession"` ⊂ `"ARSessionOrigin"`.** Under the old `Contains` match, a rig with only an `ARSessionOrigin` but no standalone `ARSession` component still passed the `ARSession` check — so removing the `ARSession` manager would NOT turn the pin RED (broke the mutation-discipline contract). Also the bare whole-file match would accept a GameObject merely *named* `AROcclusionManager`. **Fix:** subsumed by the GUID rewrite above — GUIDs are unique 32-hex strings, so no substring collision, and a GameObject name can't masquerade as a script reference. [ArSceneRigGuardTests.cs]
- [x] [Review][Patch] **(MINOR — Blind Hunter) Doc said "seven manager components."** The set is six AR managers + one XR Origin (the origin is explicitly NOT a manager). **Fix:** the rewritten doc says "seven rig components (the six AR managers OR the XR Origin)"; the D1 Dev Note + the deferred-work / release-gate text updated to match. [ArSceneRigGuardTests.cs; this story md; deferred-work.md; docs/epic-8-device-release-gate.md]
- [x] [Review][Patch] **(NIT — Acceptance Auditor) File List "all inside `#if` device blocks" was loose** — `Bootstrap.cs` L250 + `PlaneCandidate.cs` are always-compiled doc/comments, not inside `#if`. **Fix:** reworded the File List header to "inside an `#if UNITY_ANDROID` device block OR an always-compiled doc/comment — never in live editor `#else` logic." [this story md]
- [x] [Review][Defer→subsumed] **(MINOR — Acceptance Auditor) "ARSession-pin partially vacuous, fix before un-ignoring."** The auditor deferred the substring quirk to the Editor un-ignore session; the GUID rewrite fixes it NOW at the root, so the defer is moot — no future-facing debt remains. [ArSceneRigGuardTests.cs]
- [x] [Review][Sanctioned ×4] `Application.dataPath` with no empty-fallback; unguarded `File.Exists → ReadAllText`; camera-background (`ARCameraBackground`) not asserted; no distinct-instance/cardinality check. All four match the explicitly-cited `BuildSettingsGuardTests` text-read precedent and the D1-fixed component set (camera-background is a device/AC-1 concern, not the headless presence pin); adding novel defenses here would diverge from the sanctioned precedent. No change. [ArSceneRigGuardTests.cs]
- [x] [Review][False-positive ×2] "all 7" vs "6 managers + origin" phrasing (reconciled: 6 + 1 = 7); and an acceptance-auditor confirmation summary (device ACs emitted-not-claimed, AC-7/AC-8 met, owner-tags retargeted, editor `#else` byte-identical) — both verified non-defects. No change.
