# Epic 8 — Unified Editor Walkthrough (Gates 1 → 5)

**Audience:** you, James, at the PC with the Unity Editor open and — for the device steps — an ARCore phone plugged in.
**Purpose:** one document to sit down and follow start-to-finish, from the bare placeholder scenes you have today to a signed `.aab` submitted to Play. Every path, test name, type, asset, GUID, version, and command below was **verified against the live codebase on 2026-06-18** (HEAD `80f7d77`) by a parallel-reader audit + manual spot-check — not copied from older docs.

> **Why this exists and what it supersedes.** Two earlier docs covered this ground: `docs/epic-8-editor-runbook.md` (click-by-click, Gates 1–2 only) and `docs/epic-8-device-release-gate.md` (a flat checklist for Gates 0–5). This file unifies them into one ordered walkthrough **and corrects the drift** found in the audit (see the "Corrections folded in" box). Treat **this file as the canonical how-to**; keep `epic-8-device-release-gate.md` only as the ☐/☑ **record** (update its boxes as you finish each item).

> **Corrections folded in (verified, so you don't repeat stale steps):**
> 1. **Gate 0 is fully done — do NOT re-do the MonsterDatabase step.** `Bootstrap.unity:154-155` already has both `_economyConfig` (guid `b02bccce…`) and `_monsterDatabase` (guid `5d2facb7…`) assigned, and **both** `BuildSettingsGuardTests` asset-pins are plain `[Test]`, un-ignored, green. The old "drag MonsterDatabase + un-ignore its pin" task from memory is **complete**. (One cosmetic leftover: the `BuildSettingsGuardTests` class docstring at `:21-25` still *describes* the EconomyConfig pin as `[Ignore]`-pending — that comment is stale; optional one-line cleanup, no behavior impact.)
> 2. **Current headless floor is 691 total / 690 pass / 1 ignored** — the 1 ignore is the rig guard you un-ignore in Gate 1. (Ignore the gate file's stale "690/0/0" and "670/670".)
> 3. **There is no `AccessibilityManager` class.** Gate 4's TalkBack seam is a live event-subscribe + a platform `AnnounceForAccessibility` call; the logic lives in pure helpers (`AccessibilityAnnouncer` et al.).
> 4. **Shop is special:** unlike Onboarding/Home/ArHud (presenters exist, only the View is missing), **Shop has neither a presenter nor a view** — author both.
> 5. **8 Views already exist; only 4 are missing.** Do not re-author the six that exist.

---

## Map of the work (so you know where you are)

```
Gate 1  Story 8.3  AR rig in ARHunt + #if device bodies + monster prefab        [device]
Gate 2  Story 8.4  4 missing Views + place all 12 + navigation + backgrounding   [editor + playmode]
Gate 3  Story 8.5  Chunky render layer + Materialization VFX + Billing glue      [device + assets]
Gate 4  Story 8.6  Accessibility render floor (≥48dp, TalkBack, reflow, motion)  [device]
Gate 5  Story 8.7  Signed .aab + on-device smoke + Test Lab + Play submission    [device + secrets]
```

Gate 0 (MonsterDatabase, scenes, Bootstrap seams) is **done** — start at Gate 1.

---

## Before you start (one-time, ~5 min)

1. **Open the project in Unity Hub** — Unity **2022.3.62f3** (the version the headless-testing memory pins). Let the first import finish (compiles all asmdefs, populates `Library/PackageCache`).
2. **Force-resolve EDM4U** so the AR Foundation + Billing packages are on disk:
   *Assets → External Dependency Manager → Android Resolver → Force Resolve.*
   Confirm these exist before continuing — the rig guard (Gate 1) resolves component GUIDs from here and **fails loudly** if the cache is missing (never green-but-false):
   - `Library/PackageCache/com.unity.xr.arfoundation@5.2.2/`
   - `Library/PackageCache/com.unity.xr.arcore@5.2.2/`
   - `Library/PackageCache/com.unity.xr.core-utils@2.5.2/` ← **`XROrigin` lives here**, not in arfoundation.
   - `Library/PackageCache/com.unity.purchasing@5.0.0/` ← needed for Gate 3 Billing (already resolved; you wire it, you don't add it).
3. **Confirm the ARCore loader is enabled** (it is, in the repo): *Project Settings → XR Plug-in Management → Android tab → ARCore ☑*. Asset on disk: `Assets/XR/Loaders/ARCoreLoader.asset`. Nothing to do unless it's unchecked.
4. **Run the headless floor once** for a green baseline. Expected: **691 tests / 690 pass / 1 ignored** (the ignore is `ArSceneRigGuardTests.ARHunt_scene_contains_the_AR_rig_component_types`). **Close the Editor first** (project-lock trap), and **NEVER combine `-runTests` with `-quit`** (that fakes exit 0):
   ```powershell
   & "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" `
     -batchmode -nographics -projectPath "C:\Users\james\Desktop\veilwalkers" `
     -runTests -testPlatform EditMode `
     -testResults "$env:TEMP\vw-editmode.xml" `
     -logFile "$env:TEMP\vw-editmode.log"
   ```
   The 9 EditMode assemblies that run: `Veilwalkers.{Persistence, Economy, App, Architecture, Monsters, AR, Billing, Encounter, UI}.Tests`.

---

# Gate 1 — Story 8.3: the AR rig + device bodies

**Scene today:** `Assets/Veilwalkers/Scenes/ARHunt.unity` is a **bare placeholder** — a Directional Light + a URP Main Camera, **zero AR components** (verified). You're turning it into a real AR Foundation rig.

## 1.1 — Build the rig (Editor, ~15 min)

Use Unity's built-in menu items — they wire the component fileIDs/GUIDs that are unsafe to hand-author as YAML.

1. Open `ARHunt.unity`. **Delete the placeholder `Main Camera`** (the AR camera replaces it; it also carries the `AudioListener` — the AR camera's will replace it). **Keep the Directional Light.**
2. *GameObject → XR → AR Session* → creates an **`AR Session`** GameObject with the `ARSession` component.
3. *GameObject → XR → XR Origin (AR)* → creates the **`XR Origin`** hierarchy with its child **`Main Camera`** carrying `ARCameraManager` + `ARCameraBackground` (the camera-background AC-1 wants). On AR Foundation 5 the origin component is `XROrigin` (from `com.unity.xr.core-utils@2.5.2`).
4. Select the **`XR Origin`** GameObject → **Add Component** for each manager not already present (some AF5 versions auto-add some; don't duplicate):
   - `ARPlaneManager`
   - `ARAnchorManager`
   - `ARRaycastManager`
   - `AROcclusionManager`
5. **Save the scene** (Ctrl+S).

**The 7 components the guard requires** (all must serialize into `ARHunt.unity`):

| # | Component | Lives on | Package |
|---|---|---|---|
| 1 | `ARSession` | AR Session GO | arfoundation@5.2.2 |
| 2 | `ARCameraManager` | XR Origin → Main Camera | arfoundation@5.2.2 |
| 3 | `ARPlaneManager` | XR Origin | arfoundation@5.2.2 |
| 4 | `ARAnchorManager` | XR Origin | arfoundation@5.2.2 |
| 5 | `ARRaycastManager` | XR Origin | arfoundation@5.2.2 |
| 6 | `AROcclusionManager` | XR Origin | arfoundation@5.2.2 |
| 7 | `XROrigin` (or legacy `ARSessionOrigin`) | XR Origin GO | core-utils@2.5.2 |

(Guard source: `ArSceneRigGuardTests.cs:71-79` managers, `:83-87` origin-either, `:47-48` total = 7.)

## 1.2 — Un-ignore the rig guard + prove non-vacuity (~5 min)

1. Open `Assets/Tests/EditMode/Architecture.Tests/ArSceneRigGuardTests.cs`.
2. **Delete the `[Ignore("…")]` attribute** on `ARHunt_scene_contains_the_AR_rig_component_types()` (the attribute is at `:141-149`; the method at `:150`). Leave the `[Test]`.
3. Run EditMode tests (Test Runner window, or the headless command above) → it must go **GREEN**.
4. **Mutation discipline (the non-vacuity contract — do not skip):** temporarily delete **one** rig component (e.g. `AROcclusionManager`), save, re-run → must turn **RED**. Re-add it, save, re-run → **GREEN**. This proves the guard pins all seven, not just compiles. (Contract: `ArSceneRigGuardTests.cs:46-52`.)
5. If it fails with *"Could not resolve the script GUID for AR manager(s)…"* — that's the **intentional loud-fail** (`ArSceneRigGuardTests.cs:156-166`): your `Library/PackageCache` isn't resolved. Re-run **EDM4U Force Resolve** (Before-You-Start §2) and retry.

> **Why GUID, not type name:** AR Foundation managers are precompiled-package MonoBehaviours; the scene serializes them as `m_Script: {guid: …}` with the **type name absent** from the YAML. The guard resolves each type's GUID from its `<Type>.cs.meta` under PackageCache at test time and asserts that GUID appears in the scene (`:110-138`). A type-name match would be un-greenable.

## 1.3 — Implement the `#if UNITY_ANDROID && !UNITY_EDITOR` device bodies

These three adapters in `Assets/Veilwalkers/AR/` are **stubs in BOTH branches today** (`TODO(Story 8.3)` tags verified). The device branch is **excluded from the EditMode compile → zero headless coverage** — you're writing untested-by-tests code, so test it on-device. Resolve scene managers **lazily** (Bootstrap constructs these at `[DefaultExecutionOrder(-1000)]`, `Bootstrap.cs:39`, **before** the rig is live — the ctor must not touch the scene).

| Adapter | Empty device branch | Device body to implement |
|---|---|---|
| `ArcoreSession.cs` | L30-64 | `start`/`stop`/`query`, await tracking-ready, `IsSupported`; **`Stop` must cancel an in-flight `StartAsync`** (`IArSession.cs:72-82`). |
| `ArcoreAnchorProvider.cs` | L32-83 | `ARAnchorManager.AttachAnchor`/`AddAnchor`; saved-`TrackableId` re-acquire; plane/raycast relocation candidates with camera-to-plane `DistanceFromCamera` + `InCameraFrustum`. |
| `GameObjectSpawnSink.cs` | L31-53 | pooled prefab Instantiate → pose → activate / despawn → deactivate. |

**Leave the editor `#else` stubs byte-identical** — the carried-forward headless regressions drive the fakes through those stubs and must stay green:

| Suite | Fake it drives |
|---|---|
| `MonsterSpawnerTests` | `FakeSpawnSink` (cap/pool accounting) |
| `AnchorRestoreServiceTests` | `FakeArAnchorProvider` (pose decisions) |
| `PlaneAnchorServiceTests` | `FakeArAnchorProvider` (coach-vs-place) |
| `ArSessionServiceTests` | `FakeArSession` (lifecycle) |

After editing, re-run the floor; confirm those four suites are still green.

> **asmdef note:** `Veilwalkers.AR.asmdef` references **only** `Veilwalkers.Core` today (`:4-6`). The AR Foundation refs the device bodies need are **Unity package** references — add them to the asmdef's `references`/`overrideReferences` as packages. They are **not** `Veilwalkers.*` edges, so `AcyclicDependencyTests` stays green. Keep the seams (`IArSession`/`IArAnchorProvider`/`ISpawnSink` + `Pose`/`PlaneCandidate`/`AnchorToken`) AR-Foundation-free.

## 1.4 — Author the monster prefab (placeholder OK for first smoke)

A **lit placeholder primitive** is acceptable for the first device smoke (final art is Gate 3 / Story 8.5). Create a prefab the `GameObjectSpawnSink` pool instantiates: a mesh + URP-lit material. Leave the Nightveil-Filter shader hook + Lure/materialization particles as TODOs for Gate 3.

## 1.5 — Device verification (phone plugged in)

- Plane detection with visual guidance.
- Anchor add → re-acquire on saved `TrackableId`.
- Occlusion (`AROcclusionManager`) + environmental lighting (`ARCameraManager`) on spawned monsters.
- Re-anchor **moves the GameObject** to the restored pose.
- Pooled spawn stays under the frame-budget cap.
- AR recovery paths: Restored / RelocatedToPlane / Failed.

**Gate 1 done when:** rig guard green + mutation-checked; three device bodies implemented; `#else` stubs untouched + four AR suites green; `AcyclicDependencyTests` green; placeholder prefab spawns on-device.

---

# Gate 2 — Story 8.4: Views, placement, navigation, backgrounding

**The View *code* is mostly written.** Eight thin, logic-free MonoBehaviours already exist, each resolving its dependency via `GameServices.Get<T>` in `Awake` with the graceful-degrade pattern. What remains: **author 4 missing Views**, **place all 12 on scene GameObjects**, and **wire navigation**.

## 2.1 — The 8 Views that already exist (do NOT re-author)

| View (`Assets/Veilwalkers/UI/…`) | Scene | Resolves via `GameServices.Get<…>` |
|---|---|---|
| `ARHud/CameraPermissionView.cs` | ARHunt | `CameraPermissionFlow` |
| `ARHud/ArSafetyView.cs` | ARHunt | `ArSafetyGate` |
| `ARHud/ArSessionView.cs` | ARHunt | `ArSessionService` |
| `ARHud/ArPlacementView.cs` | ARHunt | `PlaneAnchorService` |
| `Codex/CodexGridView.cs` | Codex | `CodexService` |
| `Codex/CodexDetailView.cs` | Codex | `CodexService` |
| `Encounter/MaterializationView.cs` | ARHunt | (render layer — Gate 3; `Render` is a stub today) |
| `Components/ChunkyComponentView.cs` | (reusable) | — (render layer — Gate 3; `Render` is a stub today) |

All the services here are **registered live in `Bootstrap.cs`** (`GameServices.Register<…>` at `:366-384`), so they resolve at runtime once Bootstrap boots first (it `MarkReady()`s at `:392`).

## 2.2 — The 4 Views to author (copy `ArSessionView.cs` exactly)

Each is a thin, **logic-free** MonoBehaviour: `Awake` reads the locator with the graceful-degrade try/catch, then **bind + forward only — no decisions**. The canonical shape to copy verbatim (`ArSessionView.cs:48-63`):

```csharp
private void Awake()
{
    try
    {
        _service = GameServices.Get<TService>();
    }
    catch (Exception ex) when (ex is ServicesNotReadyException || ex is KeyNotFoundException)
    {
        _service = null;
        GameLog.Warn("XView: service unavailable; view is inert. " + ex.Message);
    }
}
```
…and every public action guards on `if (_service == null) { GameLog.Warn(...); return; }` before forwarding (`:71-111`).

| View to author | Scene | Presenter / service | Notes |
|---|---|---|---|
| `OnboardingView` | Onboarding | `OnboardingPresenter` (exists: `UI/Onboarding/`) | View half only. |
| `HomeView` | Home | `HomePresenter` (exists: `UI/Home/`) — needs `CodexService`, `ICreditService`, `IDailyRewardService` (all live) | + daily-reward affordance (§2.4). |
| `ArHudView` | ARHunt | `ArHudPresenter` (exists: `UI/ARHud/`) — needs `ICreditService`, `AppStateMachine` (both live) | View half only. |
| **`ShopView`** | Shop | **NO presenter exists** — author **both** `ShopPresenter` and `ShopView` | Resolves `IBillingService` (live, `Bootstrap.cs:383`). Pack cards, top-up sheet. Mirror an existing presenter's shape (e.g. `HomePresenter`). |

> ⚠️ **Shop is the exception.** The other three only need the View half (presenter exists). Shop has **no UI code at all** — write the presenter (pure logic, EditMode-test it under `Veilwalkers.UI.Tests`) **and** the view.

## 2.3 — Place all 12 Views + wire navigation

1. In each scene (`Onboarding`/`Home`/`ARHunt`/`Codex`/`Shop`), create a GameObject (or reuse the Canvas root) and **Add Component → the matching `*View`**. Wire its serialized UI refs (buttons, labels) in the Inspector.
2. **Navigation:** subscribe `AppStateMachine.OnSurfaceChanged` → real `SceneManager` load/unload. **`AppStateMachine` does NOT call `SceneManager`** — a View consumer does (put it in the appropriate root View, e.g. Home/ArHud). Include the **Shop round-trip return surface**: `ReturnFromShopAsync(ShopResume)` rehydrates the AR rig (re-anchor + re-spawn the snapshot); the **Safety Warning does NOT re-fire** on return.
3. **Prewarm trigger:** call `ArSessionService.PrewarmAsync()` at the **AR-entry affordance** (the "Enter AR Hunt" button on Home) — **NOT** at Bootstrap (warmup ownership belongs to the entry surface). `ArSessionView` already exposes `Prewarm()`/`EnterAr()` for this.
4. **Backgrounding pump:** `ArSessionView.OnApplicationPause` is **already implemented** (`:116-124`) — just ensure the view sits on an **active** GameObject in ARHunt so Unity delivers the callback. On permission-revoked-on-resume the service drives Cold → re-grant via `CameraPermissionView`.
5. **Home daily-reward affordance:** resolve `IDailyRewardService`, show/hide the claim on "not claimed today," route the claim.

## 2.4 — Confirm the headless half stays green

Re-run the floor. You should have touched only Views + scenes (+ the new `ShopPresenter`), so confirm `AcyclicDependencyTests` and the `App.Tests` `AppStateMachine` event/return-surface pins are still green. The new `ShopPresenter` should arrive with its own EditMode tests.

**Gate 2 done when:** 4 Views authored (Shop incl. presenter); all 12 placed + Inspector-wired; navigation incl. Shop round-trip wired; prewarm at the entry affordance; daily-reward affordance live; floor green.

---

# Gate 3 — Story 8.5: Chunky render layer + Materialization VFX + Billing glue

This gate turns the two `Render` **stubs** into real visuals, authors sprite/font assets, and wires the live Play Billing branch. The **decision logic is already written + headless-tested** — you render flags, never re-decide.

## 3.1 — `MaterializationView.Render(plan)` — the Lure materialization VFX

Today `MaterializationView.Render` is a `GameLog.Info` stub (`MaterializationView.cs:76-86`); the reduced-motion **setting read** already landed (`:43-52`). Author the visuals against these **already-computed plan flags** (read them, never re-decide):

| Plan flag (`MaterializationPlan.cs`) | Render as |
|---|---|
| `Variant` (`:44`) + `DurationSeconds` (`:51`) | entrance tween. Tiers + durations (`MaterializationVariant.cs:13-30`): **PopIn 0.5s · Unfurl 1.0s · Seep 1.5s · Tear 2.0s · Breach 2.75s**. |
| `Rarity` (`:33`) | tier treatment via `DreadScaleTokens.ForTier` (`DreadScaleTokens.cs:89`). |
| `BrandedGlitch` (`:60`) | the Breach glitch shader beat. |
| `ScreenShake` (`:78`) | screen-shake amount. |
| `Ambiance.EffectiveIntensity` (`:116`) | ambiance tint/intensity. |
| `RequiresCaptionCue` (`:95`) | caption + visual cue — **never audio-only** (pairs with `HasAudioSting` `:85`). |
| `HideActionBarDuringMaterialization` (`:105`) | action-bar show/hide. |
| `ReducedMotion` (`:67`) | static branded transition (no shake/strobe) — see Gate 4. |

**Relocation "pulled back through the Veil" beat REUSES this same Lure VFX** — author the Lure path here first so the relocation beat never dangles.

## 3.2 — `ChunkyComponentView.Render(style)` — the 6 prefab variants

Today a `GameLog.Info` stub (`ChunkyComponentView.cs:33-39`). Author 6 UGUI prefab variants, one per descriptor (`ChunkyComponentView.cs:10-12`): **`ChunkyButtonStyle`, `CreditPillStyle`, `PackCardStyle`, `CodexSlotStyle`, `RarityBadgeStyle`, `WordmarkStyle`**. Render attributes (all from `ChunkyStyle.cs`):

- 9-slice outline width **4–5dp** (`:24-30`)
- hard-shadow **blur == 0**, offset **6,6** (`:59,104,114`; tokens `PumpkinPatchTokens.cs:123-133`)
- pressed-shrink **offset 2 from 6,6** (`:37,121-132`)
- **ALL-CAPS** labels (`ChunkyButtonStyle.cs:59`)
- palette: `PumpkinPatchTokens` frame palette (`:31-52`); `MinTapTargetDp = 48` (`:101`)

> **No typography token file exists yet** — font choice is a deferred Story-6.3 concern (`WordmarkStyle.cs:5-7`). Pick a font + author the glyphs as part of this gate's asset work.

Then wire the live bindings: credit pill `OnCreditsChanged` + felt-descent pulse + cost-before-spend; Codex 3-state grid (Discovered / `???` / `?`) + slot-flip/count-tick/tier fade-in; state-treatment renders (veil-parting wipe, plane coaching, restore beat, camera re-grant, ArSafety high-contrast cards).

## 3.3 — Billing device glue

**Most of Billing is done + headless-tested.** `PurchaseReconciler.cs:50` (exactly-once, order-id dedup, rollback-on-swap) is complete; `BillingService.cs:98-99` routes its single grant through it. `com.unity.purchasing@5.0.0` is **already resolved** in PackageCache — you wire it, you don't add it.

The **only** deferral is the `#if UNITY_ANDROID && !UNITY_EDITOR` branch of `UnityIapStoreAdapter.cs` (L34-70 — crash-proof stubs today; `#else` at `:71-99` is identical conservative outcomes):
- `PurchaseAsync` (`:45-49`) → real `IStoreController` purchase.
- `FetchLocalizedPricesAsync` (`:51-55`) → real localized prices.
- `AcknowledgeAsync` (`:66-70`) → confirm/consume by order id, atomic with `PurchaseReconciler`.

Then Shop render: pack cards with localized prices (`FetchLocalizedPricesAsync`) + soft-nudge tags; non-blocking top-up sheet (one level deep, never buries the live encounter); Guaranteed-Rare Lure use-button → confirmation → `LureKind.GuaranteedRare`; daily-reward render.

## 3.4 — Asset authoring + device check

- Sprites/fonts: wordmark, 9-slice frames, rarity badges, glyphs, dread-scale palette, typography.
- Screen-shake + post-processing + audio stings.
- **On device:** real Play Billing purchase + acknowledge round-trip with localized prices; the materialization tween + chunky components render correctly.

**Gate 3 done when:** both `Render` stubs replaced; 6 chunky prefabs authored; Materialization VFX (incl. relocation reuse) renders from plan flags; Billing device branch wired + a real purchase round-trips on device; sprite/font assets in.

---

# Gate 4 — Story 8.6: Accessibility render floor

The a11y **pure logic exists and is headless-green** (`AccessibilityAnnouncer`, `AccessibilityContrast`, `AccessibilityLabels`, `MaterializationPlan.ReducedMotion`). This gate is the **rendered + device-confirmed** enforcement.

> **No `AccessibilityManager` class exists.** The TalkBack seam is: subscribe to live events on a View + call the platform `AnnounceForAccessibility` (via `AndroidJavaObject`) + set accessibility nodes. The strings/decisions come from the pure helpers.

- **≥48dp tap targets:** rendered Canvas/CanvasScaler + device DPI honoring `MinTapTargetDp = 48` (`PumpkinPatchTokens.cs:101`) — confirm by **physical touch**.
- **Subscribe/unsubscribe symmetry:** Views `OnEnable`-subscribe / `OnDisable`-unsubscribe on active MonoBehaviours (PlayMode/device).
- **TalkBack:** wire live event subscription (`OnCreditsChanged` / `OnMonsterDiscovered` / `OnCodexCompleted`) → platform `AnnounceForAccessibility` for role+state, balance changes, Codex count-tick. Strings come from `AccessibilityAnnouncer` (`Credits`/`AnnounceBalance`/`AnnounceDiscovery` at `:28,39,48`). **Degrade to no-op when unavailable.** (Seam is the deferred VIEW concern at `AccessibilityAnnouncer.cs:11-15`.)
- **Reduced-motion render:** inject the save-model reduced-motion setting into `MaterializationView.ReducedMotionSetting` (`MaterializationView.cs:43-52`); confirm a static branded transition (no shake/strobe, tamed ambiance) **on device**.
- **Dynamic-type reflow** at large/xlarge without truncation; shadows proportional; safety copy readable (the `AccessibilityContrast.SafetyTextColor` firewall, `:69`, `WcagAaBodyRatio = 4.5f` `:22`).
- **Audio + caption pairing** perceptible.
- **Confirm headless a11y pins stay green:** contrast over the token palette; `MaterializationPlan.ReducedMotion` presenter branch; `RequiresCaptionCue ⇐ HasAudioSting`; `AccessibilityAnnouncer` strings; `HasSubscriber`-after-Dispose.

**Gate 4 done when:** ≥48dp confirmed by touch; TalkBack announces on device + degrades gracefully; reduced-motion render confirmed; dynamic-type reflow clean; a11y headless pins green.

---

# Gate 5 — Story 8.7: Signed `.aab` + on-device smoke + submission

## 5.1 — Re-confirm build config (already pinned + guarded)

`BuildConfigGuardTests` pins these — re-confirm at build time (Play raises the SDK floor ~yearly; **re-check before submission**):

| Setting | Value | Source |
|---|---|---|
| applicationId | `com.veilwalkers.app` (**permanent once published**) | `BuildConfigGuardTests.cs:41` |
| target SDK | **35** (`MinPlayTargetSdk`) | `BuildConfigGuardTests.cs:38` |
| min SDK | **24** (ARCore floor — do not lower) | `BuildConfigGuardTests.cs:39` |
| scripting backend | IL2CPP | `BuildConfigGuardTests.cs:146-174` |
| architecture | ARM64 | `BuildConfigGuardTests.cs:146-174` |

## 5.2 — Generate the release keystore (one-time; NEVER commit)

```powershell
keytool -genkeypair -v -keystore veilwalkers-release.keystore `
  -alias veilwalkers -keyalg RSA -keysize 2048 -validity 10000
```
Store the keystore + passwords in a secret manager / CI secret store (it's gitignored — keep it out of the repo). Then set the **four env vars** the builder reads (`VeilwalkersBuilder.cs:36-39`):

```powershell
$env:VEILWALKERS_KEYSTORE_PATH = "C:\secure\veilwalkers-release.keystore"
$env:VEILWALKERS_KEYSTORE_PASS = "…"
$env:VEILWALKERS_KEY_ALIAS      = "veilwalkers"
$env:VEILWALKERS_KEY_ALIAS_PASS = "…"
```
The builder applies signing **transiently** and scrubs it in a `finally` (`VeilwalkersBuilder.cs:95,122`), so passwords never persist into tracked `ProjectSettings.asset`. If the env vars are unset it builds **UNSIGNED** and warns.

## 5.3 — Resolve dependencies + build the signed `.aab`

1. **EDM4U:** *Assets → External Dependency Manager → Android Resolver → Force Resolve* (confirm a buildable Gradle project).
2. **Build** — Editor menu *Veilwalkers → Build Android App Bundle (.aab)* (`VeilwalkersBuilder.cs:49`), **or** headless (note: the **build** command **CAN** use `-quit`, unlike the test command):
   ```powershell
   & "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" `
     -batchmode -quit -projectPath "C:\Users\james\Desktop\veilwalkers" `
     -executeMethod Veilwalkers.EditorTools.VeilwalkersBuilder.BuildAndroidAppBundle
   ```
3. **Output:** `Builds/Android/veilwalkers.aab` (`VeilwalkersBuilder.cs:41-42,85`). Confirm it built IL2CPP + ARM64 + AR Required + real applicationId.

## 5.4 — On-device smoke

- Install on a reference mid-range ARCore device (API ≥24) / Firebase Test Lab; launch to Home; **no cold-start crash**; no IL2CPP-stripping / missing-reference errors. *(This is the first real exercise of the runtime Bootstrap staging contract — WireServices synchronous, warmup not awaited before Ready, `GameServices` resolves `CodexService`/`EncounterService` — the one part Gate 0 left as a device claim.)*
- AR smoke: onboarding completes; AR entry shows the **AR Safety Warning** (dismissible); camera-permission flow works; ARCore initializes; a plane is detected with visual guidance; no uncaught AR exceptions.
- AR recovery end-to-end: lose an anchor mid-encounter → re-anchor (Restored) / nearest-plane relocate (RelocatedToPlane + the Gate-3 Lure-VFX beat) / suspend (Failed) → encounter resumes.

## 5.5 — Measure + submit

- **NFR-1:** ≥30 FPS sustained under sustained spawning.
- **NFR-5:** launch → first-plane-anchor budget with prewarm ON and OFF; record p50/p95/p99 across 5–10 runs.
- **Firebase Test Lab** matrix (API 24–34) with a documented passing threshold (0 crashes, FPS floor at p50).
- **Google Play Console:** Teen content rating; mandatory AR safety warning; camera-permission disclosure before the OS prompt.

**Gate 5 done when:** signed `.aab` at `Builds/Android/veilwalkers.aab`; on-device smoke + AR recovery pass; NFR-1/NFR-5 measured; Test Lab matrix green; submitted to Play.

---

## Tear-off session checklist

```
[ ] Editor open (2022.3.62f3), EDM4U Force Resolve done
[ ] PackageCache has arfoundation/arcore/core-utils @ 5.2.2/5.2.2/2.5.2 + purchasing@5.0.0
[ ] Baseline EditMode run green (691 / 690-pass / 1-ignored)
--- Gate 1 (8.3) ---
[ ] ARHunt.unity: AR Session + XR Origin(AR) + 4 managers added, placeholder camera deleted, saved
[ ] ArSceneRigGuardTests un-ignored → GREEN; mutation-check (remove one → RED, restore → GREEN)
[ ] ArcoreSession / ArcoreAnchorProvider / GameObjectSpawnSink device bodies done (#else stubs untouched)
[ ] AR.asmdef AR-Foundation package refs added; AcyclicDependencyTests green; 4 AR suites green
[ ] Monster placeholder prefab authored
[ ] DEVICE: plane detect, anchor add/re-acquire, occlusion+lighting, re-anchor moves GO, recovery paths
--- Gate 2 (8.4) ---
[ ] OnboardingView / HomeView / ArHudView authored (copy ArSessionView)
[ ] ShopView + ShopPresenter authored (Shop has NEITHER today) + ShopPresenter tests
[ ] All 12 Views placed on scene GameObjects, Inspector refs wired
[ ] AppStateMachine.OnSurfaceChanged → SceneManager nav wired (incl. Shop round-trip, no Safety re-fire)
[ ] Prewarm at Enter-AR affordance (not Bootstrap); daily-reward affordance on Home
[ ] Headless floor green (AcyclicDependencyTests + App.Tests pins)
--- Gate 3 (8.5) ---
[ ] MaterializationView.Render authored from plan flags (PopIn/Unfurl/Seep/Tear/Breach); relocation reuses it
[ ] ChunkyComponentView 6 prefab variants (button/pill/pack/slot/badge/wordmark); 9-slice/hard-shadow/ALL-CAPS
[ ] UnityIapStoreAdapter #if device branch wired (Purchase/FetchPrices/Acknowledge); package already resolved
[ ] Sprite/font/audio assets authored
[ ] DEVICE: real Play Billing round-trip + localized prices; tween + components render
--- Gate 4 (8.6) ---
[ ] ≥48dp tap targets confirmed by touch; OnEnable/OnDisable symmetry
[ ] TalkBack via AnnounceForAccessibility (NO AccessibilityManager) + degrade no-op
[ ] Reduced-motion render confirmed; dynamic-type reflow clean; audio+caption paired
[ ] a11y headless pins green
--- Gate 5 (8.7) ---
[ ] Build config re-confirmed (target 35 — re-check Play floor — / min 24 / IL2CPP / ARM64 / com.veilwalkers.app)
[ ] Release keystore generated (NOT committed); 4 VEILWALKERS_KEYSTORE_* env vars set
[ ] EDM4U Force Resolve; signed .aab built → Builds/Android/veilwalkers.aab
[ ] DEVICE smoke (Home no-crash, GameServices resolves Codex/Encounter) + AR smoke + AR recovery
[ ] NFR-1 (≥30 FPS) + NFR-5 (launch→anchor p50/p95/p99) measured; Test Lab matrix green
[ ] Play Console: Teen rating, AR safety warning, camera disclosure → submitted
--- Record ---
[ ] Update the ☐/☑ boxes in docs/epic-8-device-release-gate.md
[ ] Commit + push (CLAUDE.md: never leave work uncommitted)
```

---

## Provenance

Authored **2026-06-18** (HEAD `80f7d77`) from a verified parallel-reader audit of the live codebase + manual spot-check, superseding the Editor-walkthrough role of `docs/epic-8-editor-runbook.md` and unifying it with `docs/epic-8-device-release-gate.md` (which remains the ☐/☑ record). All paths/tests/types/versions/GUIDs/commands cited were confirmed present at author time. Gate 0 (MonsterDatabase, scenes, Bootstrap seams) was already complete (commit `3e2d569`); the 8.3 headless slice (`2ac6155`) added the `[Ignore]`-pending rig guard you un-ignore in Gate 1.
