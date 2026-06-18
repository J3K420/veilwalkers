# Epic 8 — Device & Editor Release-Gate Checklist

The on-device / in-Editor work that **cannot be verified headlessly** and so is NOT auto-claimed done by the story automator. The headless slices of Epic 8 (Story 8.1 build-config-as-data; Story 8.2 SampleScene removal + build-settings guard) are committed + green; everything below needs the Unity Editor, a real ARCore device, secrets, and/or a GPU. Work it top-to-bottom — the order respects the dependency chain (build config → scenes/rig → views → render → a11y → signed .aab + on-device smoke).

**Why this exists:** Epic 8 is the render/scene/device-build layer Epics 2–6 deferred. An LLM in a headless session can pin the *contracts* (config-as-text, asmdef edges, presenter/plan logic) but cannot author correct `.unity` scenes, import sprite art, run an `ARSession`, build a signed `.aab`, or measure on-device FPS. Those are listed here as an explicitly-owned gate so the "670/670 green" decision floor is never mistaken for "shippable."

Status legend: ☐ not started · ◐ in progress · ☑ done (verified on device/in Editor).

---

## Gate 0 — Editor authoring foundations (unblocks everything)

### 0.1 Author `MonsterDatabase.asset` + the MVP monster assets  (Story 8.2 / closes Story 2.2 Task-3) — ☑ DONE (2026-06-17)
- ☑ Authored 5 MVP monster `.asset` files (`Mon01_AntleredShade` … `Mon05_NightmareMaw`), `Mon03_VeilCrawler` = Rare (≥1 Rare+), each with assigned `Art` (placeholder sprites — swap for final art in a later polish pass).
- ☑ Authored `MonsterDatabase.asset` with the 5 monsters assigned (`PopulatedCount == 5`).
- ☑ Added the on-disk audit test `MonsterDatabaseAssetAuditTests` (loads via `AssetDatabase`; asserts `Validate()` empty + filenames `<Id>_<Name>` + `PopulatedCount ∈ [3..5]` + ≥1 Rare+ via `RarityThresholds.GuaranteedRareFloor`); deleted the old tautological `Asset_file_name_convention` test from `MonsterDefinitionTests`.
- **Blocks:** 0.3 (Bootstrap seam closure), all gameplay-on-device.

### 0.2 Author the 5 `.unity` game scenes  (Story 8.2) — ☑ DONE (2026-06-17)
- ☑ `Onboarding.unity`, `Home.unity`, `ARHunt.unity` (minimal placeholder — 8.3 fills the rig), `Codex.unity`, `Shop.unity` authored (near-empty placeholders).
- ☑ Registered the six-slot build order in `EditorBuildSettings.asset`: `Bootstrap(0) → Onboarding(1) → Home(2) → ARHunt(3) → Codex(4) → Shop(5)`, each enabled. `BuildSettingsGuardTests.Every_enabled_scene_resolves_to_a_real_unity_asset` passes with 6 real scenes.
- **Blocks:** 8.4 (views live in scenes), 8.7 (smoke).

### 0.3 Assign `_economyConfig` + close the Bootstrap registration seams  (Story 8.2) — ☑ DONE headlessly (2026-06-17); one device check remains
- ☑ In `Bootstrap.unity`, assigned the `EconomyConfig` asset on the Bootstrap component (inspector drag). **Un-ignored** `BuildSettingsGuardTests.Bootstrap_scene_assigns_the_EconomyConfig_reference` — active + passing.
- ☑ In `Bootstrap.cs`, closed the seams: construct + register `CodexService(saveService, _monsterDatabase, clock)`; construct + register `LureSystem/CaptureSystem/SlaySystem/EncounterService` (passing the SHARED `economyMutationLock`); wired `EncounterService` as the 2nd shortfall source + the real `IEncounterSnapshotPort` via two thin App-tier binders in `EncounterServiceAppAdapters.cs` (the `ShortfallAdapter` + `SnapshotPortAdapter` — `EncounterService` sits below App so cannot implement App-tier interfaces directly, AR-5; replaces the `NoEncounter`/null no-ops). Added `[SerializeField] MonsterDatabase _monsterDatabase` (null-checked in `WireServices`, the `_economyConfig` precedent).
- ☑ In `Bootstrap.unity`, assigned `MonsterDatabase.asset` to the `_monsterDatabase` slot. **Un-ignored** `BuildSettingsGuardTests.Bootstrap_scene_assigns_the_MonsterDatabase_reference` — active + passing.
- ☑ `AcyclicDependencyTests` stays green (headless guard of the registration code); full EditMode gate **690 passed / 0 failed / 0 ignored, 0 `error CS`.**
- ☐ On device/PlayMode (Gate 5): confirm `GameServices` resolves `CodexService` + `EncounterService` post-boot. *(The one part not headless-provable.)*
- **Blocked by:** 0.1. **Settles:** the 4.1–4.6 + 2.3 "no live EncounterService/CodexService registration" deferrals (the registration CODE; runtime resolution is the device claim).

> **Gate 0 is COMPLETE** (all headless-provable work). Next frontier: Gate 1 (AR rig + `#if UNITY_ANDROID` device bodies, Story 8.3 — device-bound).

---

## Gate 1 — AR rig + device glue  (Story 8.3, device-only)

- ☐ Author the AR rig INTO the AR Hunt scene: `ARSession`, `ARSessionOrigin`, AR camera + camera-background, `ARPlaneManager`, `ARAnchorManager`, `ARRaycastManager`, `AROcclusionManager`, `ARCameraManager`. *(A scene-asset-as-text component-presence check is the only headless slice — the rest is device.)*
- ☐ Implement the `#if UNITY_ANDROID && !UNITY_EDITOR` device bodies (excluded from EditMode compile — ZERO headless coverage): `ArcoreSession` (start/stop/query, await tracking-ready, `IsSupported`); `ArcoreAnchorProvider` (`ARAnchorManager.AddAnchor`, saved-`TrackableId` re-acquire, plane/raycast relocation candidates); `GameObjectSpawnSink` (pooled prefab Instantiate→pose→activate / despawn→deactivate).
- ☐ Author the monster prefab(s) (model, materials, Nightveil-Filter shader, materialization/Lure particles).
- ☐ On device: plane detection, anchor add/re-acquire, occlusion (AROcclusionManager) + environmental lighting (ARCameraManager) on spawned monsters; re-anchor MOVES the GameObject to the restored pose; pooled spawn under the frame-budget cap.
- ☐ Confirm the carried-forward headless regressions stay green with `GameObjectSpawnSink` as the production sink: `MonsterSpawnerTests` cap/pool accounting; `AnchorRestoreServiceTests` pose decisions.
- **Blocked by:** 0.2.

---

## Gate 2 — Thin Views + navigation + backgrounding  (Story 8.4, mixed; binding is device)

- ☐ Author the thin `*View` MonoBehaviours (Onboarding/Home/ArHud+ArSession+ArSafety+ArPlacement+CameraPermission/CodexGrid+CodexDetail/Shop), each resolving its presenter via `GameServices.Get`, logic-free (bind + forward only).
- ☐ Wire `AppStateMachine.OnSurfaceChanged` → actual `SceneManager` scene load/unload (incl. the Shop round-trip return surface). *(AppStateMachine does NOT call SceneManager — the View consumer does; device/PlayMode.)*
- ☐ `ArSessionView`: trigger `ArSessionService.PrewarmAsync()` at the AR-entry affordance (NOT Bootstrap — the warmup-ownership rule); pump `OnApplicationPause` → lifecycle loss → `Suspended`/recovery; permission-revoked-on-resume → Cold → CameraPermissionView re-grant. *(Closes the deferred-work `ArSessionView OnApplicationPause unplaced` item.)*
- ☐ Home daily-reward affordance: resolve `IDailyRewardService`, show/hide on "not claimed today," route the claim.
- ☐ `ReturnFromShopAsync(ShopResume)`: rehydrate the AR rig (re-anchor + re-spawn the snapshot) — Safety Warning does NOT re-fire.
- ☐ Confirm `AcyclicDependencyTests` + the existing `App.Tests` AppStateMachine event/return-surface pins stay green (the headless half).
- **Blocked by:** 0.2, 0.3, Gate 1.

---

## Gate 3 — Chunky render layer + Billing device glue  (Story 8.5, mixed; render is device)

- ☐ `MaterializationView.Render`: the tiered entrance tween (T1 Pop-in…T5 Breach) — **this IS the Lure materialization VFX, author it here first** — Breach glitch shader, ambiance per `plan.Ambiance.EffectiveIntensity`, action-bar show/hide, screen-shake per `plan.ScreenShake`, caption+visual cue when `plan.RequiresCaptionCue` (never audio-only). Reads plan flags; never re-decides.
- ☐ Relocation "pulled back through the Veil" beat REUSES the Lure VFX (authored above — intra-gate ordering, so the beat never dangles).
- ☐ `ChunkyComponentView`: 6 prefab variants (chunky button, credit pill, pack card, Codex slot, rarity badge, wordmark) — UGUI Image/Text, 9-slice outline, hard-shadow (blur==0, from the descriptor), pressed-shrink, ALL-CAPS labels, glyphs, corner-radius.
- ☐ Credit pill live `OnCreditsChanged` binding + felt-descent pulse + cost-before-spend. Codex 3-state grid (Discovered/`???`/`?`) + slot-flip/count-tick/tier fade-in. State-treatment renders (veil-parting wipe, plane coaching, restore beat, camera re-grant, ArSafety cards w/ high-contrast safety copy).
- ☐ Shop: pack cards w/ localized prices (`FetchLocalizedPricesAsync`) + soft-nudge tags; non-blocking top-up sheet (one level deep, never buries the live encounter); Guaranteed-Rare Lure use-button + confirmation → `LureKind.GuaranteedRare` (closes the 5.3 deferral). Daily-reward render.
- ☐ `UnityIapStoreAdapter` `#if UNITY_ANDROID && !UNITY_EDITOR` bodies: `PurchaseAsync` (real `IStoreController`) + `AcknowledgeAsync` (confirm/consume by order id, atomic w/ `PurchaseReconciler`). *(editor `#else` stubs stay green — device body excluded from EditMode compile.)*
- ☐ Sprite/font asset authoring (wordmark, 9-slice, badges, glyphs, dread-scale palette, typography); screen-shake + post-processing + audio stings.
- ☐ On device: real Play Billing purchase + acknowledge round-trip with localized prices.
- **Blocked by:** Gate 2.

---

## Gate 4 — Accessibility render floor  (Story 8.6, mixed; rendered enforcement is device)

- ☐ Rendered ≥48dp tap-target bounds (Canvas/CanvasScaler + device DPI); confirm by physical touch.
- ☐ View `OnEnable`-subscribes / `OnDisable`-unsubscribes symmetry on active MonoBehaviours (PlayMode/device).
- ☐ TalkBack: `AccessibilityManager.announce` via `AndroidJavaObject` for role+state, balance changes, Codex count-tick; degrade to no-op when unavailable.
- ☐ Dynamic-type reflow at large/xlarge without truncation; shadows proportional; safety copy readable.
- ☐ Reduced-motion VISUAL render (static branded transition, no shake/strobe, tamed ambiance) confirmed on device.
- ☐ Audio+caption pairing perceptible.
- ☐ Confirm the headless a11y pins stay green (contrast over the token palette; `MaterializationPlan.ReducedMotion` presenter branch; `RequiresCaptionCue ⇐ HasAudioSting`; `AccessibilityAnnouncer` strings; `HasSubscriber`-after-Dispose).
- **Blocked by:** Gate 3.

---

## Gate 5 — Signed `.aab` + on-device release gate  (Story 8.7, device-only)

### Build config (from Story 8.1 — already pinned + guarded; re-confirm at build time)
- ☑ applicationId `com.veilwalkers.app` · target SDK 35 (re-check Play floor before submission) · IL2CPP · ARM64 · min SDK 24 — guarded by `BuildConfigGuardTests`.
- ☐ Generate the release keystore (`keytool -genkeypair … -keystore veilwalkers-release.keystore -alias veilwalkers -keyalg RSA -keysize 2048 -validity 10000`); store it + passwords in a secret manager / CI secret store (NEVER commit). Set `VEILWALKERS_KEYSTORE_PATH/PASS`, `VEILWALKERS_KEY_ALIAS`, `VEILWALKERS_KEY_ALIAS_PASS`.
- ☐ Run EDM4U: *Assets → External Dependency Manager → Android Resolver → Force Resolve*; confirm a buildable Gradle project.

### Build + smoke
- ☐ Build the signed `.aab`: *Veilwalkers → Build Android App Bundle (.aab)*, or headless `Unity -batchmode -quit -projectPath . -executeMethod Veilwalkers.EditorTools.VeilwalkersBuilder.BuildAndroidAppBundle`. Confirm IL2CPP + ARM64 + AR Required + real applicationId.
- ☐ Install on a reference mid-range ARCore device (API ≥24) / Firebase Test Lab; launch to Home; no cold-start crash; no IL2CPP-stripping/missing-reference errors. *(This exercises the runtime Bootstrap staging contract — WireServices synchronous, warmup not awaited before Ready, GameServices resolves CodexService/EncounterService — that was NOT EditMode-runnable.)*
- ☐ AR smoke: onboarding completes; AR entry shows the AR Safety Warning (dismissible); camera-permission flow works; ARCore initializes; a plane is detected with visual guidance; no uncaught AR exceptions.
- ☐ AR recovery end-to-end: lose an anchor mid-encounter → re-anchor (Restored) / nearest-plane relocate (RelocatedToPlane + the Gate-3 Lure-VFX beat) / suspend (Failed) → encounter resumes.

### Measure + submit
- ☐ NFR-1: ≥30 FPS sustained on the reference device under sustained spawning.
- ☐ NFR-5: launch→first-plane-anchor budget with prewarm ON and OFF; record p50/p95/p99 across 5–10 runs.
- ☐ Firebase Test Lab matrix (API 24–34) with a documented passing threshold (0 crashes, FPS floor at p50).
- ☐ Google Play Console: Teen content rating; mandatory AR safety warning; camera-permission disclosure before the OS prompt.
- **Blocked by:** Gates 0–4.

---

## Provenance

Generated 2026-06-16 by the Epic-8 story-automator run. Headless slices done: Story 8.1 (`f0057a9`), Story 8.2 headless slice (`224b730`). Per-story device/checklist detail also lives in `_bmad-output/implementation-artifacts/deferred-work.md` (the 8.1 + 8.2 sections) and in each story's `### Review Findings` / device-checklist parts. Story ACs: `docs/epics.md#Epic-8`.
