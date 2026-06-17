# Story 8.1: Own AR-21's Play-submission blockers and pin the build config as data

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

baseline_commit: d4f5b73a0b826ecfdafe1fccf89d86b109cc5402

## Story

As a developer preparing the first Play-submittable build,
I want the five AR-21 Play-submission blockers explicitly owned and the reproducible build settings pinned as committed data, with the per-machine/secret pieces captured as a documented checklist,
so that a fresh clone (and CI) builds a signed, correctly-identified `.aab` against Play's moving floor — closing the AR-21 debt the retro flagged as untouched for three epics (Action Item #4).

## Acceptance Criteria

Sourced from `docs/epics.md#Story-8.1` + the Epic-8 charter (`docs/epics.md#Epic-8`) + the Epic-6 retrospective Action Item #4 (`_bmad-output/implementation-artifacts/epic-6-retro-2026-06-16.md` §8 + §6 "AR-21's fate… untouched for 3 epics") + `docs/architecture.md` build anchors (`:137–148` package/Player-Settings init; `:234–238` Infrastructure & Deployment; `:528–532` build/deploy). The FIRST Epic-8 story (Render, Scene & Device-Build Pass) — and the one that is **data-first / cheapest-blocker-first**, so it can run with zero asset dependency.

**This is a BUILD-CONFIG-AS-DATA + DOCUMENTATION story. It writes NO gameplay C# and NO new assembly.** Its product is: (1) the reproducible Android build settings pinned as committed `ProjectSettings` data (applicationId, target SDK, IL2CPP/ARM64 regression-guarded, the `.aab` toggle captured reproducibly); (2) a documented build guide + recorded decisions (target-SDK floor + its source date, EDM4U commit-vs-regenerate policy, keystore/signing storage) in `CLAUDE.md` or a `docs/build-guide.md`; and (3) a non-vacuous regression guard in `Veilwalkers.Architecture.Tests` that reads the `ProjectSettings`/`.gitignore` files **as text off disk** and fails if a Play-submission blocker regresses.

**AC-1 — applicationId is a real bundle id, not the template default (regression-guarded by a disk text-read)**
**Given** `ProjectSettings/ProjectSettings.asset`
**When** `applicationIdentifier.Android` is read
**Then** it is a real bundle id (e.g. `com.veilwalkers.app`), NOT the template default `com.UnityTechnologies.*` — asserted by an EditMode test that parses `ProjectSettings.asset` as TEXT off disk (the Pit/`EditorBuildSettings` disk-read precedent; the asset is never loaded by running the player).
- **Current state (CONFIRMED, this is the blocker):** `ProjectSettings.asset` has `applicationIdentifier:` → `Android: com.UnityTechnologies.com.unity.template.urpblank` (the literal URP-template default) and `companyName: DefaultCompany`. A real device build would ship under the template id — a Play hard blocker. 8.1 sets a real bundle id (**Decision A** picks the exact string; default `com.veilwalkers.app`) + a real `companyName`.
- The guard scans the `applicationIdentifier` block for the `Android:` value and asserts it neither equals nor starts with `com.UnityTechnologies.` / contains `template`. NON-VACUOUS: reverting to the template id makes it RED.

**AC-2 — Android target SDK is an explicit pinned floor, not Automatic; min SDK stays 24 (with the floor + its date documented)**
**Given** `ProjectSettings/ProjectSettings.asset`
**When** `AndroidTargetSdkVersion` is read
**Then** it is an explicit pinned integer at or above Play's 2026-06 minimum floor (not 0/Automatic) and `AndroidMinSdkVersion` remains 24 (ARCore floor); the pinned floor value AND its source date are recorded in `CLAUDE.md` or a build guide.
- **Current state (CONFIRMED):** `AndroidTargetSdkVersion: 0` (0 = Automatic/"highest installed") and `AndroidMinSdkVersion: 24`. Play rejects an Auto/implicit target — it must be an explicit recent integer. 8.1 pins `AndroidTargetSdkVersion` to the documented floor (**Decision B** picks the value; record it + the source date so a future reader knows when it was current — Play's floor moves yearly).
- The guard asserts `AndroidTargetSdkVersion` is a non-zero integer `>=` a constant the test reads from the SAME documented source (so the test and the doc cannot silently disagree — **the single-source rule, retro AI#3**), AND `AndroidMinSdkVersion == 24`.

**AC-3 — the `.aab` (Android App Bundle) output is enforced reproducibly, not only in gitignored EditorUserBuildSettings**
**Given** the build-output format
**When** the project is built
**Then** Android App Bundle (`.aab`) output is enforced reproducibly — committed to `ProjectSettings` OR via a committed build script that sets `buildAppBundle:true` — so a fresh clone does not rely on the gitignored `EditorUserBuildSettings`; the headless check asserts the committed setting or the build-script presence as text, NOT a real build.
- **Current state:** the `.aab` toggle (`EditorUserBuildSettings.buildAppBundle`) lives in `Library/`/user build settings, which is **gitignored** — a fresh clone defaults to APK. 8.1 makes it reproducible. **Decision C** picks the mechanism: (a) a committed `Editor/` build script (`VeilwalkersBuilder.cs`) that sets `EditorUserBuildSettings.buildAppBundle = true` before `BuildPipeline.BuildPlayer` and is the documented build entry point (PREFERRED — also gives CI a headless build command); or (b) rely on a committed `ProjectSettings` field if one exists. The headless check asserts the chosen committed artifact as TEXT (the build-script file exists + contains `buildAppBundle = true`, OR the ProjectSettings field is set), NEVER runs a real build.

**AC-4 — the EDM4U Android Gradle template + resolved-artifact policy is recorded**
**Given** the EDM4U Android Gradle template + resolved-artifact policy
**When** the decision is recorded
**Then** `CLAUDE.md` states whether `mainTemplate.gradle` and `Assets/Plugins/Android/*.aar|*.jar|*.srcaar` are committed or regenerated post-clone, with the rationale and the post-clone EDM4U-run requirement.
- **Current state:** EDM4U (External Dependency Manager for Unity, Google's Play-Services Resolver) is present (`ProjectSettings/GvhProjectSettings.xml` exists — "Gvh" = Google Versioned Handler). Whether its resolved Android artifacts are committed or regenerated is undecided. **Decision D** records the policy (default: **regenerate post-clone** — keep `Assets/Plugins/Android/*.aar` gitignored, document the "run EDM4U → Force Resolve after clone" step — so binary artifacts don't bloat git and don't drift from the resolver config). This AC is **documentation-only** (no headless test for the prose, but the `.gitignore`/policy consistency is checked under AC-5 if the policy says "ignore").

**AC-5 — Android signing is documented; the keystore exclusions stay in `.gitignore` (regression-guarded)**
**Given** Android signing
**When** the build guide is written
**Then** it documents keystore/`keystore.properties` generation + env-var/vault storage (files stay gitignored, never committed) and the build script references them — the headless half verifies the keystore exclusions remain in `.gitignore` (text read); the actual keystore generation is a device/secret checklist item.
- **Current state (CONFIRMED):** `.gitignore` already excludes `*.keystore`, `*.jks`, `keystore.properties`, `google-services.json`. 8.1 does NOT remove them — it adds a regression guard that they STAY excluded (a future careless edit that drops the exclusion would let a keystore be committed — a security incident). The build guide documents how signing is wired (the build script reads the keystore path/passwords from env vars / a vault, never from a committed file).
- The guard reads `.gitignore` as text and asserts each of `*.keystore`, `*.jks`, `keystore.properties` is present. NON-VACUOUS: removing an exclusion makes it RED.

**AC-6 — IL2CPP + ARM64 stay set (regression guard against template drift)**
**Given** the scripting + arch config from Story 1.1
**When** `ProjectSettings.asset` is asserted as text
**Then** IL2CPP backend and ARM64 architecture remain set (regression guard against template drift).
- **Current state (CONFIRMED — already correct):** `scriptingBackend: Android: 1` (1 = IL2CPP) and `AndroidTargetArchitectures: 2` (bit-flag 2 = ARM64). These were set in Story 1.1; 8.1 does NOT change them — it pins them with a regression guard so a template re-import or a careless Player-Settings edit that flips to Mono/ARMv7 fails the suite. NON-VACUOUS: flipping `scriptingBackend.Android` to 0 (Mono) or clearing the ARM64 bit makes it RED.

### Derived / load-bearing requirements (not separately numbered but required end-to-end)

- **NO gameplay C#, NO new assembly, NO scene/SO/schema/Bootstrap change.** The only code is an EditMode regression-guard test (on-disk text reads) in the EXISTING `Veilwalkers.Architecture.Tests` assembly, plus — for AC-3 Decision C(a) — one `Editor/` build script (`VeilwalkersBuilder.cs`), which is build tooling, not gameplay (it would live in a tiny `Editor`-platform asmdef or the existing `Veilwalkers.App` editor scope — **Decision C** covers placement; if it needs an asmdef, that asmdef must be `Editor`-only and is recorded in the AcyclicDependencyTests `.Tests`/editor exclusion, NOT the production matrix). The other artifacts are DATA (`ProjectSettings.asset` edits) + DOCS (`CLAUDE.md` / `docs/build-guide.md`).
- **The headless gate NEVER runs a real build and NEVER loads `ProjectSettings` via the running player.** Every 8.1 pin is a TEXT read of a file on disk (`ProjectSettings.asset`, `.gitignore`, the build-script file) — the Pit/`EditorBuildSettings` disk-read precedent. The actual `.aab` assembly, keystore generation, and EDM4U Force-Resolve are the **device/secret checklist** (this story's device-checklist half, owned but not auto-claimed done — they recur in Story 8.7's release gate).
- **The pinned target-SDK floor is single-sourced (retro AI#3).** The integer the guard compares against and the integer documented in `CLAUDE.md`/the build guide MUST be the same source — either the test reads the documented constant, or both cite one named constant. Two hand-copied integers that drift is the 6.4 premise-count desync class. (Practical form: a `const int MinPlayTargetSdk = NN; // Play floor as of 2026-06` in the test, with the doc citing that same value + date.)
- **The guard must be NON-VACUOUS ([[tautological-test-trap]] / retro AI#6).** Each pin reads the real file and would go RED on a real regression (template id restored; target SDK back to 0; a `.gitignore` exclusion dropped; scripting backend flipped to Mono). Mutation-test the load-bearing pins in dev (temporarily revert `applicationIdentifier` to the template string → confirm AC-1 RED → restore), and record it.
- **ProjectSettings YAML is edited as text, carefully.** `ProjectSettings.asset` is a Unity YAML asset; editing `applicationIdentifier`/`AndroidTargetSdkVersion`/`companyName` by hand is safe (they are scalar fields) but the edit must preserve the surrounding YAML structure exactly (the `applicationIdentifier` is a nested map with an `Android:` key). Verify the file still parses (the headless gate will catch a corrupt asset — Unity fails to open the project). Prefer editing the minimal scalar; do not reformat the file.

## Tasks / Subtasks

> **Altitude split (the load-bearing decision).** 8.1 is **build-config DATA + DOCS + a regression guard ONLY** — the same shape as Story 7.1 (config/docs + a non-vacuous on-disk guard), NOT a render/scene story. The deliverables:
> - **The pinned `ProjectSettings.asset` data** (AC-1 applicationId + companyName; AC-2 target SDK; AC-3 `.aab` reproducibility if via ProjectSettings; AC-6 IL2CPP/ARM64 unchanged-but-now-guarded).
> - **The build guide + recorded decisions** → `CLAUDE.md` and/or `docs/build-guide.md` (AC-2 SDK floor + date; AC-3 build mechanism; AC-4 EDM4U policy; AC-5 signing storage).
> - **The regression guard** → `Veilwalkers.Architecture.Tests` (`BuildConfigGuardTests.cs`): on-disk text reads of `ProjectSettings.asset` + `.gitignore` (+ the build-script presence for AC-3). Non-vacuous, mutation-tested.
> - **(AC-3 Decision C(a) only)** one `Editor/` build script `VeilwalkersBuilder.cs`.
> - **The device/secret checklist** (keystore generation, EDM4U Force-Resolve, a real `.aab` build) is RECORDED in the story's device-checklist + the Phase-3 release-gate checklist, NOT executed headlessly.

- [ ] **Task 1 — Recon the current build config (read-only baseline) (all ACs)**
  - [ ] Read `ProjectSettings/ProjectSettings.asset` and record the exact current values: `applicationIdentifier` (the `Android:` line), `companyName`, `productName`, `AndroidTargetSdkVersion`, `AndroidMinSdkVersion`, `AndroidTargetArchitectures`, `scriptingBackend` (the `Android:` line). (Baseline already observed: applicationId = `com.UnityTechnologies.com.unity.template.urpblank`; companyName = `DefaultCompany`; TargetSdk = 0; MinSdk = 24; Architectures = 2 (ARM64); scriptingBackend.Android = 1 (IL2CPP).) Confirm these against the live file.
  - [ ] Read `.gitignore` and confirm the keystore/secret exclusions present (`*.keystore`, `*.jks`, `keystore.properties`, `google-services.json`). Record.
  - [ ] Confirm `ProjectSettings/GvhProjectSettings.xml` exists (EDM4U present). Check whether `Assets/Plugins/Android/` exists and whether any `*.aar`/`*.jar`/`*.srcaar` are currently tracked by git (`git ls-files Assets/Plugins/Android/`). Record (informs Decision D).
  - [ ] Confirm NO build script exists (`find Assets -iname "*Builder*.cs"` non-test — confirmed none at baseline). Record (informs Decision C).
  - [ ] Record the baseline in the Dev Agent Record so the CR auditor can see which fields 8.1 changed.

- [ ] **Task 2 — Pin applicationId + companyName (AC-1)** `ProjectSettings/ProjectSettings.asset`
  - [ ] **Decision A — the bundle id:** set `applicationIdentifier.Android` to a real reverse-DNS id. Default: **`com.veilwalkers.app`** (the repo is `J3K420/veilwalkers`; James's call if a different id is wanted — e.g. `com.j3k420.veilwalkers`). Set `companyName` to a real value (default: `Veilwalkers`) and confirm `productName` (`VeilwalkersUnity` → optionally `Veilwalkers`, cosmetic — Decision A note).
  - [ ] Edit the minimal scalar(s); preserve the YAML structure. Do NOT reformat the file.
  - [ ] Record the chosen id + rationale in the Dev Agent Record (a bundle id is effectively permanent once published — flag it for James's explicit confirmation in the CS/DS gate).

- [ ] **Task 3 — Pin the Android target SDK floor + document it (AC-2)** `ProjectSettings.asset` + `CLAUDE.md`/`docs/build-guide.md`
  - [ ] **Decision B — the target SDK:** set `AndroidTargetSdkVersion` to an explicit integer at/above Play's current minimum floor. Default: pin to a recent value and **document the exact value + "Play floor as of 2026-06" + the source URL** so a future reader knows it will need re-checking (Play raises the floor ~yearly). Keep `AndroidMinSdkVersion: 24` (ARCore floor — do NOT lower).
  - [ ] Record the floor as a single source: a named `const int` in the guard test whose value + date the doc cites (the AI#3 single-source rule). Do NOT hand-copy two integers that can drift.

- [ ] **Task 4 — Make the `.aab` output reproducible (AC-3)** (Decision C)
  - [ ] **Decision C — the mechanism:** Default **C(a): author `Assets/Editor/VeilwalkersBuilder.cs`** — a small `Editor`-only static class with a `[MenuItem]`/CI entry that sets `EditorUserBuildSettings.buildAppBundle = true`, `EditorUserBuildSettings.SwitchActiveBuildTarget(Android)`, and calls `BuildPipeline.BuildPlayer(...)` with the scene list from `EditorBuildSettings`, reading the keystore path/passwords from env vars (AC-5). It is the documented build entry point + the CI hook. If it needs its own asmdef, make it `Editor`-platform only and note it is excluded from the production acyclicity matrix (the `.Tests`/editor-exclusion precedent); prefer placing it where no new asmdef is required if feasible.
  - [ ] The headless check (Task 6) asserts the build-script file exists AND contains `buildAppBundle` set true (text presence) — it does NOT run the build.
  - [ ] If James prefers C(b) (a committed ProjectSettings field), record that instead and guard that field.

- [ ] **Task 5 — Document EDM4U policy + signing; guard the keystore exclusions (AC-4, AC-5)** `CLAUDE.md`/`docs/build-guide.md` + `.gitignore` (verify-only)
  - [ ] **Decision D — EDM4U policy:** record in `CLAUDE.md` whether `mainTemplate.gradle` + `Assets/Plugins/Android/*.aar|*.jar|*.srcaar` are committed or regenerated. **NOTE — the policy is largely already the de-facto state:** `.gitignore` lines ~81–96 ALREADY have an EDM4U/gradle-template section (gitignoring `mainTemplate.gradle`, `gradleTemplate.properties`, `settingsTemplate.gradle`, …) with a comment explicitly deferring "Android build config … to the release-gate work (AR-21)" — i.e. THIS story. The `.gradle` templates exist on disk but are NOT git-tracked; only `Assets/Plugins/Android/.gitkeep` is tracked (recon confirms no `.aar`/`.gradle` tracked). So Decision D's default — **regenerate post-clone, keep gitignored** — CODIFIES the existing `.gitignore` reality; point the doc at the existing block (don't author a duplicate). Document the "open Unity → Assets → External Dependency Manager → Android Resolver → Force Resolve" step + the post-clone EDM4U-run requirement explicitly. Decision F resolves to NO file-tracking action (nothing tracked to untrack).
  - [ ] **Signing (AC-5):** document keystore + `keystore.properties` generation (the `keytool` command), env-var/vault storage, and that the build script references them — never a committed file. Confirm (do NOT remove) the `.gitignore` exclusions.
  - [ ] These are documentation tasks; the only code is the AC-5 `.gitignore` regression guard (Task 6).

- [ ] **Task 6 — Add the non-vacuous build-config regression guard (AC-1, AC-2, AC-3, AC-5, AC-6)** `Veilwalkers.Architecture.Tests`
  - [ ] Add `Assets/Tests/EditMode/Architecture.Tests/BuildConfigGuardTests.cs` in the EXISTING `Veilwalkers.Architecture.Tests` assembly (it already uses `System.IO` + `Editor` platform — **NO asmdef edit**, the 7.1 `PitSeamReservedTests` precedent). Resolve `ProjectSettings.asset` + `.gitignore` paths off `Application.dataPath` (the `..` → project root; `ProjectSettings/ProjectSettings.asset` and `.gitignore` are siblings of `Assets/`) — the proven `Path.Combine(Application.dataPath, "..", ...)` idiom; guard missing-file with a clear message (NFR-3), never an opaque throw.
  - [ ] **Pin 1 (AC-1):** read `ProjectSettings.asset` text and parse the value **block-anchored** (CRITICAL — `applicationIdentifier` is a nested map with `Android:`, `iPhone:`, `Standalone:` siblings, and the literal word `Android` appears on MANY unrelated scalar lines like `AndroidMinSdkVersion`/`AndroidTargetSdkVersion`; a whole-file scan for `Android:`/`template`/`com.UnityTechnologies` WILL false-match — the `iPhone:`/`Standalone:` siblings still carry `com.unity.template`/`com.Unity-Technologies` even AFTER the fix). The recipe: find the `applicationIdentifier:` block header line, then read the FIRST indented child matching `^\s+Android:\s*(\S+)`. Assert that captured value does NOT start with `com.UnityTechnologies.`, does NOT contain `template`, AND equals the chosen id (or at least is a non-empty reverse-DNS id with ≥2 dots). NON-VACUOUS (restore template id → RED). Mutation note: confirm the test still goes RED when ONLY the `Android:` child is the template id while `iPhone:`/`Standalone:` are untouched — proving the test reads the right line, not a sibling.
  - [ ] **Pin 2 (AC-2):** parse `AndroidTargetSdkVersion`, assert it is a non-zero integer `>= MinPlayTargetSdk` (the single-source const), AND `AndroidMinSdkVersion == 24`. NON-VACUOUS (set TargetSdk back to 0 → RED).
  - [ ] **Pin 3 (AC-6):** parse `scriptingBackend` **block-anchored** (same nested-map trap as Pin 1 — `scriptingBackend:` is a map whose child is `Android: 1`; a whole-file scan for `Android: 1` is unsafe). Recipe: find the `scriptingBackend:` header, read its FIRST indented `^\s+Android:\s*(\d+)` child, assert `== 1` (IL2CPP). AND parse `AndroidTargetArchitectures:` (a top-level scalar, value `2`) and assert the ARM64 bit is set (`value & 2 != 0`). NON-VACUOUS (flip scriptingBackend.Android to 0/Mono or clear the ARM64 bit → RED).
  - [ ] **Pin 4 (AC-5):** read `.gitignore` text, assert `*.keystore`, `*.jks`, `keystore.properties` each present. NON-VACUOUS (drop an exclusion → RED).
  - [ ] **Pin 5 (AC-3, if Decision C(a)):** assert the build-script file exists AND contains the exact assignment `buildAppBundle = true` (whitespace-tolerant regex `buildAppBundle\s*=\s*true`) — NOT a bare `contains("buildAppBundle")`, which would stay green if a future edit flipped it to `= false`. NON-VACUOUS against the realistic regression (flag flipped, not removed): rename the script OR flip to `= false` → RED. (If C(b), guard the ProjectSettings field's committed value instead.)
  - [ ] **Mutation-test the load-bearing pins in dev (retro AI#6):** temporarily revert `applicationIdentifier` to `com.UnityTechnologies...` and `AndroidTargetSdkVersion` to 0, run the suite, CONFIRM Pin 1 + Pin 2 go RED, then RESTORE and confirm green. Record. (Do NOT let the reverted values reach the commit — `git diff ProjectSettings/ProjectSettings.asset` before commit must show only the intended pins.)

- [ ] **Task 7 — asmdef / matrix / Bootstrap / schema: confirm NO change (derived)** (verification)
  - [ ] **asmdef:** the guard lands in the existing `Veilwalkers.Architecture.Tests` — NO edit. The AC-3 build script, IF it needs an asmdef, is `Editor`-only and outside the production matrix — record it; otherwise no asmdef change. Confirm `AcyclicDependencyTests` stays green.
  - [ ] **Bootstrap / GameServices:** NO change (no service). **schema / SaveModel / EconomyConfig:** NO change. **scenes:** NO change (8.2 owns scenes). Confirm untouched.
  - [ ] If anything else appears to need a change, STOP — it signals scope creep beyond "own the build config."

- [ ] **Task 8 — Tests + headless gate (all ACs)** `Veilwalkers.Architecture.Tests`
  - [ ] `BuildConfigGuardTests` (Task 6): Pins 1–5 + the missing-file graceful messages. All on-disk text reads, non-vacuous (mutation-tested in dev).
  - [ ] Re-confirm the EXISTING suite still passes unchanged (8.1 adds NO type to production, only a test + data + docs). **Baseline 674/674** (post-7.1, per `sprint-status.yaml`). Target green at 674 + the new build-config pins (~5), 0 failed, 0 `error CS`.
  - [ ] **Headless gate:** Editor must be CLOSED; never trust exit 0 — read `test-results.xml` for `result="Passed"` AND grep the log for `error CS`. Delete `test-results.xml` + `editor-test.log` before commit ([[unity-headless-testing]]). CONFIRM `ProjectSettings.asset` still parses (a corrupt YAML edit would make Unity fail to open the project — the gate would catch it as a load failure, not a test failure).

## Dev Notes

### Decisions to make (call them explicitly in the Dev Agent Record)

| # | Decision | Default / recommendation | Why |
|---|---|---|---|
| A | The applicationId / bundle id (and companyName/productName). | **`com.veilwalkers.app`** + companyName `Veilwalkers`. **FLAG for James** — a published bundle id is permanent. | The repo is `J3K420/veilwalkers`; `com.veilwalkers.app` is clean reverse-DNS. James may prefer `com.j3k420.veilwalkers` or a domain he owns. This is the one decision with a permanent external consequence — confirm it. |
| B | The Android target SDK value. | Pin to Play's current floor (a recent integer), documented WITH "Play floor as of 2026-06" + source URL; keep MinSdk 24. | Play rejects Automatic (0) and raises the floor ~yearly. An explicit pinned value + a date tells the next reader when to re-check. Single-sourced with the guard's const (AI#3). |
| C | The `.aab` reproducibility mechanism: a committed `Editor/` build script vs a committed ProjectSettings field. | **C(a): a committed `Assets/Editor/VeilwalkersBuilder.cs`** that sets `buildAppBundle=true` + is the CI build entry point. | The `.aab` toggle lives in gitignored `EditorUserBuildSettings`; a committed build script makes it reproducible AND gives CI/8.7 a real headless build command (reused by the device gate). A bare ProjectSettings field doesn't give CI an entry point. |
| D | EDM4U resolved-artifact policy: commit vs regenerate. | **Regenerate post-clone** — keep `Assets/Plugins/Android/*.aar` gitignored, document the Force-Resolve step. | Resolved `.aar`s are large binaries that drift from the resolver config; regenerating keeps git clean and authoritative. (If a reproducible-offline-build need arises, committing is the alternative — recorded, not chosen.) |
| E | Where the docs live: `CLAUDE.md` vs a new `docs/build-guide.md`. | **A new `docs/build-guide.md`** for the full build/sign/EDM4U guide, with a one-line pointer + the SDK-floor value/date in `CLAUDE.md`. | The build guide is multi-section (SDK floor, `.aab`, EDM4U, signing, the build-script command); `CLAUDE.md` is the always-loaded index and should carry the pointer + the single-sourced SDK floor, not the whole guide. |
| F | Whether to also un-track any currently-committed EDM4U `.aar` artifacts. | **Only if Task 1 finds them tracked** — then decide per Decision D (default regenerate → untrack + gitignore, recorded). If none tracked, no action. | Don't churn files that aren't there. The recon (Task 1) gates this. |

### Sanctioned-deviation matrix (the CR noise filter — keep this exhaustive)

| # | Deviation | Why it is sanctioned | Source |
|---|---|---|---|
| D1 | 8.1 ships NO gameplay C#, NO new production assembly, NO scene/SO/schema/Bootstrap change. The only code is an EditMode guard test (+ optionally one `Editor/` build script). | The AC scopes 8.1 to build-config DATA + DOCS + a regression guard — the data-first / cheapest-blocker-first Epic-8 story (the 7.1 docs+guard altitude split, applied to build config). | epics.md#Story-8.1; Epic-8 charter; the 7.1 precedent |
| D2 | Most ACs assert/guard config that is set as DATA (`ProjectSettings.asset` edits), not produced by code. | "Pin the build config as committed data" — the reproducibility comes from the data being committed + guarded, not from runtime logic. | epics.md#Story-8.1 ("pinned as committed data") |
| D3 | The headless tests are TEXT reads of `ProjectSettings.asset` / `.gitignore` / the build-script file off disk — they never load ProjectSettings via the player and never run a build. | The Epic-8 verifiability invariant: headless pins are (1) file/YAML text reads, (2) pure-logic, or (3) enum/contract shape. AC-1/2/3/5/6 are all (1). The `.aab` build itself is device-only. | Epic-8 charter (verifiability invariant); the Pit/EditorBuildSettings disk-read precedent |
| D4 | AC-5 (keystore) + AC-6 (IL2CPP/ARM64) GUARD already-correct config rather than changing it. | `.gitignore` already excludes keystores; IL2CPP+ARM64 were set in Story 1.1. 8.1's value is the REGRESSION GUARD (a future careless edit can't silently un-set them) — the failure-path-parity / structural-invariant discipline. | .gitignore (current); ProjectSettings (current); Story 1.1; [[failure-path-cleanup-parity]] |
| D5 | AC-4 (EDM4U) is documentation-only with no headless test for the prose. | A policy decision recorded in `CLAUDE.md` has no falsifiable code assertion (it's a human policy); the only adjacent guard is the `.gitignore` consistency under AC-5 if the policy is "ignore." | epics.md#Story-8.1 (AC-4 "the decision is recorded"); retro AI#4 (recorded decision, not code) |
| D6 | The device/secret half (real keystore generation, EDM4U Force-Resolve, a real `.aab` build) is RECORDED + deferred to the device checklist / Story 8.7, NOT executed in this headless session. | These need a build machine + secrets + a device; the Epic-8 split puts them on the owned release-gate checklist, never auto-claimed done. They recur in 8.7's release gate. | Epic-8 charter (device-checklist split, never auto-claimed); epics.md#Story-8.7 |
| D7 | The AC-3 build script (if Decision C(a)) is `Editor`-only build tooling, not a gameplay service, and is exempt from the production acyclicity matrix. | An `Editor`-platform build script never ships in the player and never participates in the runtime dependency graph (the `.Tests`/editor-exclusion precedent). | the `.Tests`/editor asmdef-exclusion precedent (5.4/6.x); AcyclicDependencyTests `.Tests` exclusion |

### Seams this story inherits (from deferred-work.md + prior stories + the Epic-8 scope)

- **AR-21 (Play-submission readiness) — the retro Action Item #4 debt.** The Epic-6 retro (`epic-6-retro-2026-06-16.md` §6, §8) records AR-21 as "untouched for 3 epics" with five open blockers: template applicationId, target SDK, the `.aab` toggle, EDM4U, signing. 8.1 is the named story AI#4 demanded ("AR-21 is either a scheduled story or an explicitly-owned checklist with a target milestone"). 8.1 SETTLES the data/docs half; the device build half is 8.7.
- **Story 1.1 (project init — AR-1).** Set IL2CPP + ARM64 + MinSdk 24 + AR Required at init. 8.1 does NOT redo this — it GUARDS it (AC-6) so a template re-import can't silently revert it. (`epics.md` Story 1.1 / architecture.md:137–148.)
- **The `.gitignore` keystore exclusions (Story 1.1 repo hygiene).** Already present; 8.1 adds the regression guard (AC-5). (`CLAUDE.md` "Never commit secrets"; `.gitignore`.)
- **The deferred-work render/scene/device re-point header (this run, 2026-06-16).** 8.1 is the owner of the "AR-21 / Play-submission blockers" row in that mapping. 8.1 introduces NO new deferral of its own beyond the device-build half it explicitly hands to 8.7.
- **The Epic-8 verifiability invariant.** 8.1 is one of only two Epic-8 stories (with 8.2) whose headless half is substantial; it sets the pattern (config-as-text guards) the automator can take to a green gate. 8.3–8.7 are device-checklist.

### Architecture compliance (must-follow guardrails)

- **The Epic-8 verifiability invariant (the central guardrail):** every headless pin is a file/YAML/`.gitignore`/build-script TEXT read off disk — never a real build, never a player-loaded ProjectSettings. (epics.md#Epic-8.)
- **AR-1 / build target (architecture.md:137–148, :234–238, :528–532):** Android, AR Required, ARM64, IL2CPP, `.aab`, min API per ARCore (24). 8.1 pins the target SDK + makes `.aab` reproducible + guards ARM64/IL2CPP — all within this canon.
- **The single-purpose architecture-guard precedent (`MonsterDatabaseLoreCountTests.cs`, `PitSeamReservedTests.cs`):** turn an invariant into a falsifiable on-disk EditMode test. `BuildConfigGuardTests.cs` follows that shape.
- **[[tautological-test-trap]] / retro AI#6:** every pin reads the real file + is mutation-tested (revert a value → confirm RED → restore). A guard that can't go red asserts nothing.
- **Single-source rule (retro AI#3):** the target-SDK floor integer is one source (the guard's const) that the doc cites — never two hand-copied integers (the 6.4 desync class).
- **NEVER commit secrets (`CLAUDE.md`):** the keystore/`keystore.properties`/`google-services.json` stay gitignored; AC-5 guards this. The build script reads secrets from env/vault.
- **NFR-3 graceful:** the guard fails with a clear message if `ProjectSettings.asset`/`.gitignore` is missing — never an opaque `FileNotFoundException`.

### Source tree components to touch

**NEW:**
- `Assets/Tests/EditMode/Architecture.Tests/BuildConfigGuardTests.cs` (+ `.cs.meta`) — the non-vacuous build-config regression guard (Pins 1–5; on-disk text reads of `ProjectSettings.asset` + `.gitignore` + the build-script file; graceful missing-file messages). In the EXISTING `Veilwalkers.Architecture.Tests` (no asmdef edit).
- `docs/build-guide.md` (Decision E) — the full build/sign/EDM4U/SDK-floor guide.
- (Decision C(a)) `Assets/Editor/VeilwalkersBuilder.cs` (+ `.cs.meta`) — the `Editor`-only `.aab` build entry point / CI hook.

**UPDATE (DATA + DOCS):**
- `ProjectSettings/ProjectSettings.asset` — `applicationIdentifier.Android` (real id), `companyName`, `AndroidTargetSdkVersion` (pinned floor); (verify-unchanged: `AndroidMinSdkVersion 24`, `scriptingBackend.Android 1`, `AndroidTargetArchitectures 2`).
- `CLAUDE.md` — a pointer to `docs/build-guide.md` + the single-sourced target-SDK floor value + date (+ the EDM4U policy one-liner).
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — 8.1 backlog → ready-for-dev → (review) → done; epic-8 backlog → in-progress (first story).
- `.gitignore` — verify-only (Decision F may add an `Assets/Plugins/Android/*.aar` exclusion IF Task 1 found resolved artifacts; otherwise unchanged).

**DO NOT TOUCH (sanctioned no-change):**
- Any production gameplay `.cs`, any scene (`.unity`), any ScriptableObject, `SaveModel`/`SaveMigrations`/`EconomyConfig`, `Bootstrap.cs`, the `AcyclicDependencyTests` production matrix (an `Editor` build-script asmdef, if any, is editor-excluded — not a production-matrix row).
- `EditorBuildSettings.asset` — the scene list + SampleScene removal is **Story 8.2**, not 8.1.

**NO asmdef edit expected** (the guard uses the existing `Veilwalkers.Architecture.Tests`). If the AC-3 build script needs an asmdef, it is `Editor`-only and recorded as matrix-exempt; surface it in the DS gate if so.

### Testing standards summary

- **Headless EditMode is the hard gate** ([[unity-headless-testing]]): Editor closed; never trust exit 0 — read `test-results.xml` for `result="Passed"` AND grep the log for `error CS`; delete `test-results.xml` + `editor-test.log` before commit. ALSO confirm `ProjectSettings.asset` still parses (a corrupt YAML edit makes Unity fail to open the project — a load failure, distinct from a test failure).
- **Non-vacuous + mutation-tested** (the load-bearing test-quality requirement): each pin reads the real file; in dev, revert `applicationIdentifier` → template + `AndroidTargetSdkVersion` → 0, confirm Pins 1+2 RED, restore, confirm green. Record in the Dev Agent Record.
- **Single-source the SDK floor** (AI#3): the guard's `const int MinPlayTargetSdk` is the one source the doc cites.
- **Baseline:** 674/674 (post-7.1). Target: 674 + the ~5 build-config pins, 0 failed, 0 `error CS`.
- **No service fakes, no `GameServices.ResetForTests()`** — pure on-disk file scans, no service dependencies (the 7.1 `PitSeamReservedTests` model).
- **The CR auditor will check:** the FR/AC values cite their source; the guard pins read real files (mutation-proven); the device-only items (real build, keystore gen, EDM4U resolve) are recorded as deferred/checklist, not silently claimed; the bundle id was flagged for James (permanent consequence).

### Project Structure Notes

- `Veilwalkers.Architecture.Tests` references `{UnityEngine.TestRunner, UnityEditor.TestRunner, Veilwalkers.Core, Veilwalkers.Monsters}`, `Editor`-only, `UNITY_INCLUDE_TESTS`, `nunit.framework.dll` (VERIFIED). `System.IO` + `Application.dataPath` are already the idiom in `AcyclicDependencyTests` + `PitSeamReservedTests` — `BuildConfigGuardTests` needs NO new reference. Project-root files resolve via `Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset")` and `Path.Combine(Application.dataPath, "..", ".gitignore")`.
- `ProjectSettings.asset` is Unity YAML; the fields 8.1 edits are scalars (`applicationIdentifier` is a nested map with `Android:`, `iPhone:`, `Standalone:` keys — edit the `Android:` line). `AndroidTargetArchitectures` is a bit-field (2 = ARM64; 1 = ARMv7; the guard checks `& 2`).
- The single-purpose guard precedents: `PitSeamReservedTests.cs` (7.1, on-disk seam guard) + `MonsterDatabaseLoreCountTests.cs` (AR-20 lore count). `BuildConfigGuardTests.cs` is the same shape for build config.
- No previous Epic-8 story exists (8.1 is the first). The nearest precedent is 7.1 (the docs+guard altitude split) and Story 1.1 (which set the build config 8.1 now guards).
- **Permanent-consequence flag:** the bundle id (Decision A) is the one 8.1 choice with an irreversible external effect once published — it must be James's explicit call, surfaced at the gate.

### References

- [Source: docs/epics.md#Story-8.1] — the six AC blocks: applicationId real not template (AC-1); explicit target SDK floor + MinSdk 24 (AC-2); reproducible `.aab` output (AC-3); EDM4U policy recorded (AC-4); signing documented + keystore exclusions guarded (AC-5); IL2CPP/ARM64 regression guard (AC-6); + the device-checklist (keystore gen, EDM4U resolve, real build).
- [Source: docs/epics.md#Epic-8] — the Epic-8 charter + the verifiability invariant (headless pins are file/YAML/asmdef text, pure-logic, or enum-shape; device-only parts are an owned release-gate checklist, never auto-claimed done) + the sequencing (8.1 data-first).
- [Source: _bmad-output/implementation-artifacts/epic-6-retro-2026-06-16.md §6, §8 (Action Item #4)] — AR-21 untouched 3 epics; the five Play-submission blockers; AI#4 "AR-21 is either a scheduled story or an explicitly-owned checklist with a milestone."
- [Source: docs/architecture.md:137–148] — package init + Player Settings: Android, Min API per ARCore, AR Required, ARM64, IL2CPP.
- [Source: docs/architecture.md:234–238, :528–532] — Infrastructure & Deployment: Unity → `.aab`, IL2CPP, ARM64, AR Required, min API per ARCore; signing via gitignored keystore + keystore.properties; Google Play tracks.
- [Source: ProjectSettings/ProjectSettings.asset] — current values (the blockers): `applicationIdentifier.Android = com.UnityTechnologies.com.unity.template.urpblank`; `companyName = DefaultCompany`; `AndroidTargetSdkVersion = 0`; `AndroidMinSdkVersion = 24`; `AndroidTargetArchitectures = 2` (ARM64); `scriptingBackend.Android = 1` (IL2CPP).
- [Source: .gitignore] — the secret exclusions to guard: `*.keystore`, `*.jks`, `keystore.properties`, `google-services.json`.
- [Source: ProjectSettings/GvhProjectSettings.xml] — EDM4U (Google Versioned Handler / Play-Services Resolver) is present.
- [Source: Assets/Tests/EditMode/Architecture.Tests/PitSeamReservedTests.cs] — the on-disk non-vacuous guard precedent (7.1); the `Path.Combine(Application.dataPath, …)` idiom + the graceful-missing-file pattern + dev mutation-testing.
- [Source: CLAUDE.md] — "Never commit secrets — keystores… keep it that way."; the build-target + economy canon context.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Opus 4.8) — story-automator ungated cycle (Epic 8).

### Debug Log References

- **First dev run hit the "compile-fix on first run" trap (Epic-6 retro Theme A — a missing namespace import):** the gate exited 0 with NO `test-results.xml` + 6 `error CS` + "Scripts have compiler errors" (caught by the never-trust-exit-0 discipline). `VeilwalkersBuilder.cs` used `BuildFailedException` (namespace `UnityEditor.Build`) with only `using UnityEditor.Build.Reporting;` — added `using UnityEditor.Build;`. Re-ran clean.
- Headless EditMode gate (after the compile fix): **681/681 passed, 0 failed, 0 `error CS`** (`test-results.xml` `result="Passed"`; log grep clean). Baseline was 674 (post-7.1); the 7 new `BuildConfigGuardTests` pins bring it to 681.
- **Mutation test of the guard (the non-vacuous proof — retro AI#6):** reverted `applicationIdentifier.Android`→`com.UnityTechnologies...template.urpblank` + `AndroidTargetSdkVersion`→0, re-ran the gate → **exactly 2 failures, the right ones:** `Android_applicationId_is_not_the_unity_template_default` (caught the template id) + `Android_target_sdk_is_pinned_at_or_above_the_play_floor` (caught the 0/Automatic) — 679 passed, 2 failed. Restored ProjectSettings from backup (verified `com.veilwalkers.app` / target 35 back); `git diff` shows ONLY the 3 intended pins (companyName, Android applicationId, target SDK). The guard bites — not tautological. Deleted `test-results.xml`/`editor-test.log`/the backup.

### Completion Notes List

- **This story shipped build-config DATA + DOCS + a regression guard + one editor build script** — NO gameplay C#, NO new production assembly, NO scene/SO/schema/Bootstrap change (the 7.1 docs+guard altitude split applied to build config). The recon baseline (Task 1) confirmed every claimed current value against the live `ProjectSettings.asset`.
- **Decision A — RESOLVED (James, at the CS/DS gate): `com.veilwalkers.app`** + companyName `Veilwalkers`. The bundle id is permanent once published; James chose it explicitly (the one genuine ungated blocker, surfaced and answered). The `iPhone:`/`Standalone:` siblings stay the template id (Android-only Play target — not edited).
- **Decision B — RESOLVED: `AndroidTargetSdkVersion = 35`** (Play floor as of 2026-06, Android 15 / API 35). Single-sourced: `BuildConfigGuardTests.MinPlayTargetSdk = 35`; `docs/build-guide.md` + `CLAUDE.md` cite that value + the 2026-06 date + the Play policy URL (the AI#3 single-source rule). MinSdk stays 24.
- **Decision C — RESOLVED: C(a), `Assets/Editor/VeilwalkersBuilder.cs`** — an editor-only build entry point (menu + `-executeMethod` CI hook) that sets `EditorUserBuildSettings.buildAppBundle = true`, switches to Android, reads signing from `VEILWALKERS_KEYSTORE_*` env vars, and builds from the enabled `EditorBuildSettings` scenes. Under `Assets/Editor/` (predefined `Assembly-CSharp-Editor` — NO asmdef, editor-only, outside the production acyclic graph). Guarded by `Build_script_pins_the_app_bundle_output_reproducibly` (matches the exact `buildAppBundle = true` assignment).
- **Decision D — RESOLVED: regenerate EDM4U post-clone** — codifies the existing `.gitignore` reality (the Gradle templates + `Assets/Plugins/Android/*.aar` are already excluded; recon found nothing `.aar`/`.gradle` tracked). Documented the Force-Resolve step in `docs/build-guide.md`. Decision F: no tracking action (nothing to untrack).
- **Decision E — RESOLVED: `docs/build-guide.md`** (full guide) + a `CLAUDE.md` "Build & release (AR-21)" pointer carrying the bundle id + the single-sourced SDK floor + the build command.
- **AC-5 (signing) + AC-6 (IL2CPP/ARM64):** GUARD already-correct config (the `.gitignore` exclusions + IL2CPP/ARM64 from Story 1.1) — 8.1's value is the regression guard, not a change. The build script documents env-var signing (never a committed keystore).
- **asmdef / matrix / Bootstrap / schema / scenes:** all untouched (Task 7). The build script needs NO asmdef (`Assembly-CSharp-Editor`); `EditorBuildSettings.asset`/SampleScene stays Story 8.2.
- **Device/secret half (recorded, NOT done here):** the real signed `.aab` build, keystore generation, EDM4U Force-Resolve, and on-device smoke are the Story 8.7 release-gate checklist (also captured in the Phase-3 device checklist of this automator run).

### File List

**NEW:**
- `Assets/Tests/EditMode/Architecture.Tests/BuildConfigGuardTests.cs` (+ `.cs.meta`, Unity-generated) — the 8-pin non-vacuous build-config guard (applicationId not template; target SDK ≥ floor; min SDK 24; IL2CPP; ARM64; `.gitignore` secret exclusions; `.aab` build-script assignment), all on-disk text reads, block-anchored YAML.
- `Assets/Editor/VeilwalkersBuilder.cs` (+ `.cs.meta`, Unity-generated) — the editor-only reproducible `.aab` build entry point (CI `-executeMethod` hook + menu item), env-var signing.
- `docs/build-guide.md` — the full Android build/sign/EDM4U/SDK-floor guide.

**MODIFIED (DATA + DOCS):**
- `ProjectSettings/ProjectSettings.asset` — `applicationIdentifier.Android` `com.UnityTechnologies.com.unity.template.urpblank` → `com.veilwalkers.app`; `companyName` `DefaultCompany` → `Veilwalkers`; `AndroidTargetSdkVersion` `0` → `35`. (Unchanged + now guarded: `AndroidMinSdkVersion 24`, `scriptingBackend.Android 1`, `AndroidTargetArchitectures 2`.)
- `CLAUDE.md` — added the "Build & release (AR-21 / Story 8.1)" subsection (build-guide pointer + bundle id + single-sourced SDK floor + build command + EDM4U note).
- `docs/epics.md` — Epic 8 section + Epic List entry (scoped this run).
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — epic-8 → in-progress; 8.1 → ready-for-dev → (review).
- `_bmad-output/implementation-artifacts/deferred-work.md` — the render/scene/device re-point header + the line-347 SampleScene owner re-point.

**UNCHANGED (verified — sanctioned no-change):** any gameplay `.cs`, any `.unity` scene, `EditorBuildSettings.asset` (8.2 owns the scene list), `Bootstrap.cs`, `SaveModel`/`EconomyConfig`, the `AcyclicDependencyTests` production matrix, all production + test asmdefs.

### Change Log

| Date | Change |
|---|---|
| 2026-06-16 | Story created (CS) via the story-automator ungated cycle. Epic 8 was scoped this same day (retro AI#2); 8.1 is the data-first / cheapest-blocker-first story. |
| 2026-06-16 | VS (fresh-context adversarial validation): baseline ProjectSettings values CONFIRMED 100% accurate against the live file (the guard will assert the right things). 2 critical fixes applied (C1: AC-1 Pin 1 now block-anchored to the `applicationIdentifier:`→first-`Android:`-child to avoid false-matching the `iPhone:`/`Standalone:` siblings that still carry `template`, + the many `Android*` scalar lines; C2: AC-6 Pin 3 same block-anchor for `scriptingBackend:`→`Android:`). 2 enhancements applied (E1: pointed Decision D at the EXISTING `.gitignore` EDM4U block ~81–96 — the policy codifies existing reality, nothing tracked to untrack; E2: Pin 5 now matches the exact `buildAppBundle = true` assignment, not a bare contains). E3 (the permanent bundle-id) escalated to James as the one genuine ungated blocker. Verdict: ready-with-fixes → fixes applied → ready-for-dev pending the bundle-id decision. |
| 2026-06-16 | James chose the bundle id: `com.veilwalkers.app` (companyName `Veilwalkers`). DS: edited ProjectSettings.asset (applicationId, companyName, target SDK 35) + authored `VeilwalkersBuilder.cs` + `BuildConfigGuardTests.cs` + `docs/build-guide.md` + CLAUDE.md pointer. First gate hit the compile-fix trap (missing `using UnityEditor.Build;` for `BuildFailedException`) — fixed. Headless gate **681/681 green, 0 `error CS`**. Mutation-tested the guard (revert id→template + SDK→0 → exactly 2 pins RED → restored). Status → review. |
| 2026-06-16 | CR: adversarial 3-layer fan-out (36 agents) → **33 raw → 33 deduped → 5 confirmed (4 code patches; the 5th was the acceptance-auditor's positive AC-verification, not a defect) + 10 defer + 13 sanctioned + 5 false-positive, 0 AC violations.** 4 patches applied to `VeilwalkersBuilder.cs` + `BuildConfigGuardTests.cs` (see Review Findings). The headline confirmed finding was a REAL security bug: the build script wrote keystore passwords into `PlayerSettings` → serialized into the tracked `ProjectSettings.asset`, violating the "never commit secrets" invariant — fixed to apply signing transiently + scrub in a `finally`. Re-ran gate after patching. 8 of 10 defers are `VeilwalkersBuilder` device-build-robustness items correctly routed to Story 8.7; 2 (D9/D10) were diff-scope artifacts (sprint-status/deferred-work WERE updated, just outside the code CR diff). Status → done. |

### Validation Findings

- [x] [VS][Fix] **AC-1 Pin 1 nested-map false-match (critical).** `applicationIdentifier` is a YAML map (`Android:`/`iPhone:`/`Standalone:`); the `iPhone:`/`Standalone:` siblings still contain `com.unity.template`/`com.Unity-Technologies` after the fix, and `Android` appears on many unrelated scalar lines. A whole-file scan would be brittle/falsely-green. PATCHED: Pin 1 is now block-anchored (find `applicationIdentifier:`, read first `^\s+Android:\s*(\S+)` child) + a mutation note proving it reads the right line.
- [x] [VS][Fix] **AC-6 Pin 3 same nested-map trap for `scriptingBackend:` (critical).** PATCHED: block-anchored to `scriptingBackend:`→`Android:` child == 1; `AndroidTargetArchitectures` confirmed a top-level scalar (`& 2` ARM64 bit check is correct).
- [x] [VS][Fix] **Pin 5 build-script match too loose (enhancement).** PATCHED: matches `buildAppBundle\s*=\s*true` (non-vacuous against a flip-to-false), not a bare `contains`.
- [x] [VS][Fix] **EDM4U already gitignored (enhancement).** PATCHED: Decision D points at the existing `.gitignore` block (~81–96) — the policy codifies existing reality; Decision F resolves to no tracking action (recon: no `.aar`/`.gradle` tracked).
- [ ] [VS][Escalate] **The applicationId (Decision A) is a permanent external identifier — James's call.** Surfaced as the one genuine ungated blocker; DS will not default it silently.

### Review Findings

Adversarial 3-layer code review (Blind Hunter / Edge Case Hunter / Acceptance Auditor, 36 agents) of `8-1-cr.diff`. **33 raw → 33 deduped → 5 confirmed (4 code patches + 1 positive AC-verification) / 10 defer / 13 spec-sanctioned / 5 false-positive, 0 AC violations.** Post-patch headless gate: **681/681 green, 0 `error CS`.** The 8 genuine defers (D1 folded into a patch; D2–D8 below) are `VeilwalkersBuilder` device-build-robustness items correctly routed to Story 8.7's release gate → `deferred-work.md`.

- [x] [Review][Patch] **Signing passwords persisted into the committed `ProjectSettings.asset` (major, SECURITY).** `ApplySigningFromEnvironment` wrote `keystorePass`/`keyaliasPass` into `PlayerSettings`, which Unity serializes into the tracked asset — directly violating "never commit secrets" (CLAUDE.md / AC-5). FIXED: signing is now applied TRANSIENTLY just before the build and SCRUBBED in a `finally` (`ScrubSigningFromPlayerSettings`), so no keystore name/password ever persists to tracked state. (blind-hunter)
- [x] [Review][Patch] **Unsigned path left stale keystore fields (minor).** The unsigned branch flipped `useCustomKeystore = false` but didn't clear the four keystore fields. FIXED: the unsigned branch + the build `finally` both call `ScrubSigningFromPlayerSettings` (clears all five fields). (blind-hunter)
- [x] [Review][Patch] **`report.summary` NRE if `BuildPlayer` returns null (major).** A crashed/interrupted build can return a null `BuildReport`; the code dereferenced `report.summary` directly. FIXED: null-guarded → clean `BuildFailedException`. (edge-case-hunter)
- [x] [Review][Patch] **`.gitignore` guard brittle to benign formatting (nit→real, AC-5 security property).** Exact trimmed-line equality would false-fail on a valid `*.keystore # comment` or `**/*.keystore`. FIXED: the guard now strips comments + matches the security PROPERTY (each secret token is excluded, allowing leading-`/`, `**/`, and `…/secret` globs) while staying non-vacuous (deleting an exclusion still fails). (blind-hunter)
- [x] [Review][Patch][folded] **Build-target switch result ignored (D1, major).** `SwitchActiveBuildTarget` return was unchecked — a failed switch (Android module absent) would build for the wrong platform. FIXED alongside the build-script patches: the switch result + the resulting active target are checked → `BuildFailedException` on failure. (verifier-routed defer, folded into the same method's patch since it was cheap + clearly correct.)
- [x] [Review][Verify] **All six ACs satisfied with non-vacuous guards (acceptance-auditor positive verdict, NOT a defect).** AC-1..AC-6 each confirmed: applicationId not template (guarded), target SDK 35 ≥ floor + min 24 (guarded, single-sourced), `.aab` reproducible via `buildAppBundle = true` (exact-regex guarded), EDM4U policy documented, keystore exclusions guarded, IL2CPP+ARM64 guarded. No change — records the green-for-the-right-reason verdict (retro AI#6 gate).
- [x] [Review][Defer] **D2–D8: `VeilwalkersBuilder` device-build robustness** (output-dir not pre-created; stale-scene-path guard; partial/whitespace-only env-var handling; keystore-file-existence validation; empty-scenes-unsigned edge) — all real but OUT of 8.1's headless "the script exists + pins the toggle as text" scope; they exercise on a real build machine. → `deferred-work.md`, owner Story 8.7 (the device release gate that actually invokes the script). (edge-case-hunter / blind-hunter, out-of-scope)
- [x] [Review][Note] **D9/D10 (sprint-status.yaml / deferred-work.md "missing from diff") are diff-scope artifacts, not defects.** Both files WERE updated as part of this story; they were simply outside the CODE/DOCS CR diff (`8-1-cr.diff`), which scoped to ProjectSettings + CLAUDE.md + the new code + build-guide + epics.md. No action. (acceptance-auditor, false-alarm-from-diff-scope)
- 13 spec-sanctioned + 5 false-positive (the deviation matrix knocked them down): e.g. "ARM64 guard allows ARMv7+ARM64" (the `& 2` bit-check is intentionally a containment test, not equality — FP); "regex lacks word boundaries" / "key-name collisions" (the block-anchored + top-level-scalar patterns are sufficient for these specific keys — sanctioned/FP); "no negative-SDK rejection" (the `>= MinPlayTargetSdk` floor already rejects negatives — sanctioned). Full list in the CR output.
