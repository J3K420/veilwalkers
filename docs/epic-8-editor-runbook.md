# Epic 8 — Editor Runbook (Stories 8.3 + 8.4)

**Audience:** you, James, sitting at the PC with the Unity Editor open and (for the device-smoke steps) an ARCore phone plugged in.
**Purpose:** turn the device/Editor-gated parts of Stories 8.3 (AR rig + device glue) and 8.4 (View wiring + navigation) into a **mechanical checklist** — no exploration, no "what was that GUID again." Authored 2026-06-18 from the live codebase so every path, type, test name, and asset is verified to exist *now*.

> **Why this is a runbook and not a story:** these steps need the Editor, a GPU, and a real device — they cannot be verified headlessly, so the story-automator deliberately left them as a gate (`docs/epic-8-device-release-gate.md`). The *logic* underneath is already written and headless-green; this is placement and wiring, not authoring.

**Companion docs:** `docs/epic-8-device-release-gate.md` (the canonical gate — this runbook expands its Gate 1 + Gate 2 into clicks). Update the ☐/☑ boxes **there** as you complete each item; this runbook is the how, that file is the record.

---

## Before you start (one-time, ~5 min)

1. **Open the project in Unity Hub** (Unity 2022 LTS+). Let it finish importing — first import compiles all asmdefs and resolves `Library/PackageCache`.
2. **Force-resolve EDM4U** so the AR Foundation packages are on disk:
   *Assets → External Dependency Manager → Android Resolver → Force Resolve.*
   The rig guard (below) matches AR components **by package script GUID resolved from `Library/PackageCache`** — if the cache is missing it fails *loudly*, never green-but-false. Confirm these exist before continuing:
   - `Library/PackageCache/com.unity.xr.arfoundation@5.2.2/`
   - `Library/PackageCache/com.unity.xr.arcore@5.2.2/`
   - `Library/PackageCache/com.unity.xr.core-utils@2.5.2/` ← this is where `XROrigin` lives (not in arfoundation).
3. **Confirm the ARCore loader is already enabled** (it is, in the repo): *Project Settings → XR Plug-in Management → Android tab → ARCore ☑*. Asset on disk: `Assets/XR/Loaders/ARCoreLoader.asset`. Nothing to do unless it's unchecked.
4. **Run the headless test floor once** so you have a green baseline to compare against (PowerShell, repo root). The known-good floor is **691 passed / 690-pass / 1 ignored** (the one ignore is `ArSceneRigGuardTests`, which you'll un-ignore in Gate 1):
   ```powershell
   # Adjust the Unity path to your install. NEVER combine -runTests with -quit (headless gotcha — fakes exit 0).
   & "C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe" `
     -batchmode -projectPath "C:\Users\james\Desktop\veilwalkers" `
     -runTests -testPlatform EditMode -testResults "$env:TEMP\vw-editmode.xml"
   ```
   > **Project-lock trap:** close the Editor before a batch-mode run, or it dies on the project lock. (See the `unity-headless-testing` memory.)

---

## Gate 1 — Story 8.3: Author the AR rig into `ARHunt.unity`

**Scene:** `Assets/Veilwalkers/Scenes/ARHunt.unity` — today a **placeholder** (Directional Light + Main Camera only, zero AR components). You are replacing/augmenting it with a real AR Foundation rig.

### 1.1 Build the rig (Editor, ~15 min)

The fastest correct path is Unity's built-in menu items — they wire the component fileIDs/GUIDs that are unsafe to hand-author as YAML:

1. Open `ARHunt.unity`. **Delete the placeholder `Main Camera`** (the AR camera replaces it). Keep the Directional Light.
2. *GameObject → XR → AR Session* — creates an **`AR Session`** GameObject carrying the `ARSession` component.
3. *GameObject → XR → XR Origin (AR)* — creates the **`XR Origin`** hierarchy with its child **`Main Camera`** carrying `ARCameraManager` + `ARCameraBackground` (the camera-background AC-1 calls for). On AR Foundation 5 this is the `XROrigin` component from `com.unity.xr.core-utils`.
4. Select the **`XR Origin`** GameObject and **Add Component** for each of the four remaining managers (Inspector → Add Component → type the name):
   - `ARPlaneManager`
   - `ARAnchorManager`
   - `ARRaycastManager`
   - `AROcclusionManager`
   > These four can also be auto-added by some AR Foundation versions; if they're already present, don't duplicate them.
5. **Save the scene** (Ctrl+S).

**The seven components the guard requires** (all must end up serialized in `ARHunt.unity`):

| # | Component | Lives on | Package |
|---|---|---|---|
| 1 | `ARSession` | AR Session GO | arfoundation@5.2.2 |
| 2 | `ARCameraManager` | XR Origin → Main Camera | arfoundation@5.2.2 |
| 3 | `ARPlaneManager` | XR Origin | arfoundation@5.2.2 |
| 4 | `ARAnchorManager` | XR Origin | arfoundation@5.2.2 |
| 5 | `ARRaycastManager` | XR Origin | arfoundation@5.2.2 |
| 6 | `AROcclusionManager` | XR Origin | arfoundation@5.2.2 |
| 7 | `XROrigin` (or legacy `ARSessionOrigin`) | XR Origin GO | core-utils@2.5.2 |

### 1.2 Un-ignore the rig guard + prove non-vacuity (~5 min)

1. Open `Assets/Tests/EditMode/Architecture.Tests/ArSceneRigGuardTests.cs`.
2. **Delete the `[Ignore("…")]` attribute** on `ARHunt_scene_contains_the_AR_rig_component_types()` (lines ~140–149). Leave the `[Test]`.
3. Run EditMode tests (Test Runner window, or the batch command above). The test must go **GREEN**.
4. **Mutation discipline (do not skip — this is the non-vacuity contract):** temporarily remove **one** rig component (e.g. delete `AROcclusionManager` from the XR Origin), save, re-run → the test must turn **RED**. Re-add it, save, re-run → GREEN. This proves the guard actually pins all seven, not just compiles.
5. If the test fails with *"Could not resolve the script GUID for AR manager(s)…"* → your `Library/PackageCache` isn't resolved. Re-do Before-You-Start step 2.

> **Why GUID, not type name:** AR Foundation managers are precompiled-package MonoBehaviours; the scene serializes them as `m_Script: {guid: …}` with the type name **absent** from the YAML. The guard resolves each type's GUID from its `<Type>.cs.meta` under PackageCache at test time and asserts that GUID appears in the scene. (This is the `story-8-3-headless-slice` memory's key lesson — a type-name match would be un-greenable.)

### 1.3 Implement the `#if UNITY_ANDROID && !UNITY_EDITOR` device bodies

These three adapters have **byte-identical editor `#else` stubs today** (stub-safe no-ops) and **zero headless coverage** for the device branch — that's expected, the device branch is excluded from the EditMode compile. Fill in the real bodies against the rig you just authored. Resolve scene managers **lazily** (Bootstrap constructs these adapters at `[DefaultExecutionOrder(-1000)]` *before* the rig is live — the ctor must not touch the scene).

| Adapter (in `Assets/Veilwalkers/AR/`) | Device body to implement |
|---|---|
| `ArcoreSession` | `start`/`stop`/`query`, await tracking-ready, `IsSupported`; **`Stop` must cancel an in-flight `StartAsync`** (see `IArSession.cs` L72–82). |
| `ArcoreAnchorProvider` | `ARAnchorManager.AttachAnchor`/`AddAnchor`; saved-`TrackableId` re-acquire; plane/raycast relocation candidates with camera-to-plane `DistanceFromCamera` + `InCameraFrustum`. |
| `GameObjectSpawnSink` | pooled prefab Instantiate → pose → activate / despawn → deactivate. |

**Leave the editor `#else` stubs byte-identical** — the carried-forward headless regressions (`MonsterSpawnerTests`, `AnchorRestoreServiceTests`, `PlaneAnchorServiceTests`, `ArSessionServiceTests`) drive the `FakeSpawnSink`/fake-session through those stubs and must stay green. After editing, re-run the floor and confirm those four suites are still green.

> **asmdef note:** `Veilwalkers.AR.asmdef` references only `Veilwalkers.Core` today. The AR Foundation package refs you need for the device bodies are **Unity package** references (add them to the asmdef's `references`/`overrideReferences` as packages) — they are NOT `Veilwalkers.*` edges, so `AcyclicDependencyTests` stays green. Keep the seams (`IArSession`/`IArAnchorProvider`/`ISpawnSink` + `Pose`/`PlaneCandidate`/`AnchorToken`) AR-Foundation-free.

### 1.4 Author the monster prefab (placeholder is fine for first smoke)

A **lit placeholder primitive** is acceptable for the first device smoke (final art is Story 8.5). Create a prefab the `GameObjectSpawnSink` pool instantiates: a mesh + material; leave the Nightveil-Filter shader hook + Lure/materialization particles as TODOs for 8.5.

---

## Gate 2 — Story 8.4: Wire the Views into scenes + navigation

**Good news — the View *code* is already written.** Eight thin, logic-free MonoBehaviours exist under `Assets/Veilwalkers/UI/`, each resolving its presenter/service via `GameServices.Get<T>` in `Awake` with the graceful-degrade pattern (catch `ServicesNotReadyException`/`KeyNotFoundException` → inert). What remains is **Editor placement** (drop each on a scene GameObject) and **navigation wiring** (SceneManager round-trips). The presenters they bind are also already written and headless-tested.

### 2.1 Existing Views and what each resolves

| View (`Assets/Veilwalkers/UI/…`) | Belongs in scene | Resolves via `GameServices.Get<…>` |
|---|---|---|
| `ARHud/CameraPermissionView.cs` | ARHunt | `CameraPermissionFlow` |
| `ARHud/ArSafetyView.cs` | ARHunt | `ArSafetyGate` |
| `ARHud/ArSessionView.cs` | ARHunt | `ArSessionService` |
| `ARHud/ArPlacementView.cs` | ARHunt | `PlaneAnchorService` |
| `Codex/CodexGridView.cs` | Codex | `CodexService` |
| `Codex/CodexDetailView.cs` | Codex | `CodexService` |
| `Encounter/MaterializationView.cs` | ARHunt | (render layer — Story 8.5) |
| `Components/ChunkyComponentView.cs` | (reusable component) | — |

All four services these resolve are **registered live in `Bootstrap.cs`** (`GameServices.Register<…>` lines ~366–384), so the views will resolve them at runtime once Bootstrap boots first.

### 2.2 Views still to author (logic-free MonoBehaviours)

Per `docs/epic-8-device-release-gate.md` Gate 2, these surfaces still need a thin View wrapper following the exact same pattern as `ArSessionView.cs` (copy its shape: `Awake` locator-read with try/catch graceful-degrade, bind + forward only, **no decisions**):

- **`OnboardingView`** → resolves `OnboardingPresenter` collaborators (Onboarding scene)
- **`HomeView`** → resolves `HomePresenter` (needs `CodexService`, `ICreditService`, `IDailyRewardService` — all live) + the daily-reward affordance (Home scene)
- **`ArHudView`** → resolves `ArHudPresenter` (needs `ICreditService`, `AppStateMachine` — both live) (ARHunt scene)
- **`ShopView`** → resolves `IBillingService` (live) — pack cards, top-up sheet (Shop scene)

> Presenters already exist: `OnboardingPresenter`, `HomePresenter`, `ArHudPresenter`, `CodexGridPresenter`, `CodexDetailPresenter`, `MaterializationPresenter`, `StateTreatmentPresenter` (all under `Assets/Veilwalkers/UI/…`, headless-tested by `Veilwalkers.UI.Tests`). You're writing the **view** half only.

### 2.3 Placement + navigation (Editor + PlayMode)

1. In each scene (`Onboarding`/`Home`/`ARHunt`/`Codex`/`Shop`), create a GameObject (or reuse the Canvas root) and **Add Component → the matching `*View`**. Wire its serialized UI references (buttons, labels) in the Inspector.
2. **Wire navigation:** `AppStateMachine.OnSurfaceChanged` → actual `SceneManager` scene load/unload. **`AppStateMachine` does NOT call `SceneManager`** — a View consumer does. Add the subscription in the appropriate root View (Home/ArHud), including the **Shop round-trip return surface** (`ReturnFromShopAsync(ShopResume)` rehydrates the AR rig; the Safety Warning does **not** re-fire on return).
3. **`ArSessionView` prewarm trigger:** call `ArSessionService.PrewarmAsync()` at the **AR-entry affordance** (the "Enter AR Hunt" button on Home), **NOT** at Bootstrap — warmup ownership belongs to the entry surface, not the composition root.
4. **Backgrounding pump:** `ArSessionView.OnApplicationPause` is already implemented — just ensure the view is placed on an active GameObject in ARHunt so Unity delivers the callback. On permission-revoked-on-resume the service drives Cold → re-grant via `CameraPermissionView`.
5. **Home daily-reward affordance:** resolve `IDailyRewardService`, show/hide the claim on "not claimed today," route the claim.

### 2.4 Confirm the headless half stays green

After wiring, re-run the floor and confirm `AcyclicDependencyTests` + the `App.Tests` `AppStateMachine` event/return-surface pins are still green (you should not have touched any pure-logic class — only Views + scenes).

---

## Build-order sanity (already done — just confirm)

`EditorBuildSettings.asset` already registers the six-slot order, all enabled:
`Bootstrap(0) → Onboarding(1) → Home(2) → ARHunt(3) → Codex(4) → Shop(5)`.
`BuildSettingsGuardTests.Every_enabled_scene_resolves_to_a_real_unity_asset` passes against six real scenes. Nothing to do unless you added/renamed a scene.

---

## What this runbook does NOT cover (later gates, intentionally)

- **Gate 3 (Story 8.5):** chunky render layer, sprite/font authoring, Billing device glue, real monster art.
- **Gate 4 (Story 8.6):** rendered accessibility floor (≥48dp targets, TalkBack, dynamic-type reflow, reduced-motion) — needs physical-touch/TalkBack verification.
- **Gate 5 (Story 8.7):** generate the release keystore (set `VEILWALKERS_KEYSTORE_*` env vars — never commit), build the signed `.aab`, Firebase Test Lab matrix, FPS/launch-budget measurement, Play Console submission.

Those stay in `docs/epic-8-device-release-gate.md` Gates 3–5; do them in order after this runbook lands the rig + views.

---

## Quick session checklist (tear-off)

```
[ ] Editor open, EDM4U Force Resolve done, PackageCache has arfoundation/arcore/core-utils
[ ] Baseline EditMode run green (691, 1 ignored)
--- Gate 1 (Story 8.3) ---
[ ] ARHunt.unity: AR Session + XR Origin(AR) + 4 managers added, placeholder camera deleted, saved
[ ] ArSceneRigGuardTests un-ignored → GREEN
[ ] Mutation check: removed one component → RED, restored → GREEN
[ ] ArcoreSession / ArcoreAnchorProvider / GameObjectSpawnSink device bodies implemented (#else stubs untouched)
[ ] AR.asmdef AR-Foundation package refs added; AcyclicDependencyTests still green
[ ] Monster placeholder prefab authored
[ ] DEVICE: plane detect, anchor add/re-acquire, occlusion+lighting, re-anchor moves GO, recovery paths
--- Gate 2 (Story 8.4) ---
[ ] OnboardingView / HomeView / ArHudView / ShopView authored (copy ArSessionView pattern)
[ ] All 8 existing + 4 new Views placed on scene GameObjects, Inspector refs wired
[ ] AppStateMachine.OnSurfaceChanged → SceneManager nav wired (incl. Shop round-trip)
[ ] Prewarm triggered at Enter-AR affordance (not Bootstrap)
[ ] Daily-reward affordance on Home wired
[ ] Headless floor still green (AcyclicDependencyTests + App.Tests pins)
--- Record ---
[ ] Update the ☐/☑ boxes in docs/epic-8-device-release-gate.md (Gate 1 + Gate 2)
[ ] Commit + push (CLAUDE.md: never leave work uncommitted)
```
