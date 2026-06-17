# Veilwalkers — Android Build Guide (AR-21 / Story 8.1)

The reproducible, Play-submittable Android build. This guide owns the AR-21 Play-submission
blockers the Epic-6 retrospective flagged as untouched for three epics. The build CONFIG is pinned
as committed data + guarded by `BuildConfigGuardTests`; the actual signed `.aab` assembly + on-device
smoke are the device/secret release gate (Story 8.7).

## Pinned build configuration (committed data, guarded)

These live in `ProjectSettings/ProjectSettings.asset` and are regression-guarded by
`Assets/Tests/EditMode/Architecture.Tests/BuildConfigGuardTests.cs` (on-disk text reads — a
regression fails the headless EditMode suite):

| Setting | Value | Why |
|---|---|---|
| `applicationIdentifier.Android` | **`com.veilwalkers.app`** | The permanent Play package id. Replaces the URP template default (`com.UnityTechnologies.*`). **Permanent once published — do not change for a live listing.** |
| `companyName` | `Veilwalkers` | Replaces the `DefaultCompany` template value. |
| `AndroidTargetSdkVersion` | **35** | Play's target-SDK floor **as of 2026-06** (Android 15 / API 35). Google requires new apps to target an API level within ~one year of the latest Android release, and raises the floor roughly yearly — **re-check before each submission.** Single source: `BuildConfigGuardTests.MinPlayTargetSdk`. Policy: <https://support.google.com/googleplay/android-developer/answer/11926878> |
| `AndroidMinSdkVersion` | 24 | The ARCore floor — do not lower. |
| `scriptingBackend.Android` | 1 (IL2CPP) | Play 64-bit + AR requirement (set in Story 1.1; guarded against template drift). |
| `AndroidTargetArchitectures` | 2 (ARM64 bit) | Play requires a 64-bit build. |

> **Target-SDK floor is single-sourced.** The integer the guard compares against
> (`MinPlayTargetSdk` in `BuildConfigGuardTests.cs`) is the one source of truth; this table cites
> it. When Play raises the floor, bump the const + this row together (the retro AI#3 rule —
> never two hand-copied integers that drift).

## Building the `.aab` (Android App Bundle)

The App-Bundle toggle (`EditorUserBuildSettings.buildAppBundle`) is stored under `Library/`, which is
**gitignored** — so a fresh clone defaults to APK. `Assets/Editor/VeilwalkersBuilder.cs` makes the
`.aab` output reproducible by setting the toggle in committed code, and is the single build entry point.

- **In the Editor:** menu **Veilwalkers → Build Android App Bundle (.aab)**.
- **Headless / CI:**
  ```
  Unity -batchmode -quit -projectPath . \
    -executeMethod Veilwalkers.EditorTools.VeilwalkersBuilder.BuildAndroidAppBundle
  ```
  Output: `Builds/Android/veilwalkers.aab`.

`VeilwalkersBuilder` is editor-only build tooling under `Assets/Editor/` (the predefined
`Assembly-CSharp-Editor` — no asmdef, never shipped in the player, never part of the runtime acyclic
graph). `BuildConfigGuardTests` pins the `buildAppBundle = true` assignment as committed text.

## Signing (keystore — NEVER committed)

Keystores and `keystore.properties` are **gitignored** (`*.keystore`, `*.jks`, `keystore.properties`)
and that exclusion is regression-guarded. Signing material is supplied via environment variables
(CI secret store / local env), read by `VeilwalkersBuilder.ApplySigningFromEnvironment`:

| Env var | Meaning |
|---|---|
| `VEILWALKERS_KEYSTORE_PATH` | absolute path to the `.keystore`/`.jks` (outside the repo, or a gitignored path) |
| `VEILWALKERS_KEYSTORE_PASS` | keystore password |
| `VEILWALKERS_KEY_ALIAS` | key alias |
| `VEILWALKERS_KEY_ALIAS_PASS` | key-alias password |

Generate a release keystore once (keep it safe + backed up — losing it means you can never update
the published app):
```
keytool -genkeypair -v -keystore veilwalkers-release.keystore \
  -alias veilwalkers -keyalg RSA -keysize 2048 -validity 10000
```
Store the keystore + passwords in a secret manager / CI secret store. If the env vars are unset the
build proceeds **unsigned** (an editor smoke build); a submission build must set them.

## EDM4U (External Dependency Manager for Unity)

EDM4U (the Google Play-Services Resolver; `ProjectSettings/GvhProjectSettings.xml`) resolves the
Android `.aar`/Gradle dependencies for AR Foundation / ARCore / Unity IAP.

- **Policy: regenerate post-clone (do NOT commit resolved artifacts).** The Gradle templates
  (`mainTemplate.gradle`, `gradleTemplate.properties`, `settingsTemplate.gradle`) and resolved
  `Assets/Plugins/Android/*.aar|*.jar|*.srcaar` are gitignored (see `.gitignore` — they were already
  excluded with a comment deferring "Android build config … to the release-gate work (AR-21)").
- **After a fresh clone**, before the first Android build, run:
  **Assets → External Dependency Manager → Android Resolver → Force Resolve** (or it resolves
  automatically on first Android build with auto-resolution on). This materializes the resolved
  artifacts locally without bloating git or letting binaries drift from the resolver config.

## Pre-submission device release gate (Story 8.7 — NOT done here)

The items below need a build machine + secrets + a real ARCore device and are owned by Story 8.7's
release-gate checklist (never auto-claimed done by the headless gate):

- Generate the release keystore + set the signing env vars.
- Run EDM4U Force-Resolve; confirm a buildable Gradle project.
- Build the signed `.aab`; install on a reference mid-range ARCore device (API ≥24) / Firebase Test Lab.
- AR smoke test + NFR-1 (≥30 FPS) / NFR-5 (launch→first-anchor) measurement.
- Google Play Console: Teen content rating, mandatory AR safety warning, camera-permission disclosure.
