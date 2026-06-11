---
baseline_commit: c49c8bfdb21ad941e99fa56ac138eb45d2a93182
---

# Story 1.1: Initialize Unity AR project with pinned packages

Status: in-progress (blocked — Unity Hub/Editor not installed; awaiting human editor steps)

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want the Veilwalkers Unity project initialized with the verified URP + AR + IAP package set and Android build settings,
So that all later work builds on a compliant, reproducible foundation.

## Acceptance Criteria

**AC-1 — Project created from the URP template on the pinned engine**
**Given** a clean machine with Unity Hub
**When** the project is created from the "3D (URP)" template on Unity 2022.3 LTS
**Then** the project opens and compiles with no errors
**And** `Packages/manifest.json` pins `com.unity.xr.arfoundation` 5.0.x, `com.unity.xr.arcore` 5.0.x (same version number as AR Foundation), and `com.unity.purchasing` 5.0.0+ (Unity IAP, bundles Google Play Billing 8.0.0)
**And** ARCore Extensions for AR Foundation (arf5 branch) is imported.

**AC-2 — Android player & build settings configured for Play compliance**
**Given** the project is open
**When** Player Settings are configured
**Then** the active build target platform is Android with **AR Required**, **ARM64** (scripting backend **IL2CPP**), and **Min API level per ARCore** (Android 7.0 / API 24 minimum)
**And** the build configuration produces an **Android App Bundle (.aab)**.

**AC-3 — Repo hygiene: ignore generated files & secrets, no legacy billing plugin**
**Given** repo hygiene rules
**When** the project is committed
**Then** `.gitignore` excludes `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.sln`, `*.keystore`/`*.jks`, `keystore.properties`, `google-services.json`, and billing service-account keys
**And** the legacy Google Play Billing Plugin for Unity is **NOT** present
**And** `ProjectSettings/` and `Packages/manifest.json` + `Packages/packages-lock.json` **ARE** committed (they are the reproducible foundation; do not ignore them).

## Tasks / Subtasks

- [ ] **Task 1 — Create the Unity project (AC: #1)**
  - [ ] In Unity Hub, create a new project from the **3D (URP)** template on **Unity 2022.3 LTS** (latest 2022.3.x patch).
  - [ ] Confirm the project opens and the URP render pipeline asset is assigned (Graphics + Quality settings reference the URP asset).
  - [ ] Verify a clean compile (no console errors) before adding packages.
- [ ] **Task 2 — Pin AR + IAP packages in `Packages/manifest.json` (AC: #1)**
  - [ ] Add/pin `com.unity.xr.arfoundation` to the 5.x line **matched to Unity 2022.3 LTS** (see Latest Tech Info — verify the exact 5.x patch; do not float to 6.x which requires Unity 2023.2+).
  - [ ] Add/pin `com.unity.xr.arcore` at the **same version number** as AR Foundation (required by AR Foundation 5 for Android/ARCore support).
  - [ ] Add/pin `com.unity.purchasing` (Unity IAP) `5.0.0+` — confirm it bundles **Google Play Billing Library 8.0.0** (compliant with Google's ≥7.0.0 rule in force since 2025-08-30).
  - [ ] Add `com.unity.nuget.newtonsoft-json` (needed in Epic 1.3 for the save model; pin it now so the manifest is the single source of truth).
  - [ ] Let Unity resolve and **commit `Packages/packages-lock.json`** so versions are reproducible.
- [ ] **Task 3 — Import ARCore Extensions for AR Foundation (arf5) (AC: #1)**
  - [ ] Add via Package Manager → **Add package from git URL**: `https://github.com/google-ar/arcore-unity-extensions.git#arf5` (or the `arf5`-tagged tarball from the releases page).
  - [ ] Confirm it resolves against the pinned AR Foundation 5 version with no compile errors.
- [ ] **Task 4 — Configure Android Player Settings (AC: #2)**
  - [ ] Switch active build target to **Android** (File → Build Settings → Android → Switch Platform).
  - [ ] Player Settings → Other Settings: **Scripting Backend = IL2CPP**, **Target Architectures = ARM64** only (uncheck ARMv7), **Minimum API Level = Android 7.0 (API 24)** or per current ARCore requirement.
  - [ ] XR Plug-in Management → Android → enable **ARCore**; configure the AR session as **AR Required** (ARCore "Requirement" = Required in the ARCore settings).
  - [ ] Build Settings → enable **Build App Bundle (Google Play)** so output is `.aab`.
  - [ ] (Do NOT generate or commit a keystore in this story — signing config lands at build/release time with a gitignored keystore; see AR-21.)
- [ ] **Task 5 — Verify repo hygiene & no legacy billing plugin (AC: #3)**
  - [ ] Confirm the existing `.gitignore` (already present at repo root) ignores `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.sln`, `*.keystore`/`*.jks`, `keystore.properties`, `google-services.json` — **it already does; verify, do not recreate.**
  - [ ] Confirm `ProjectSettings/`, `Packages/manifest.json`, and `Packages/packages-lock.json` are **tracked** (NOT ignored).
  - [ ] Verify NO legacy "Google Play Billing Plugin for Unity" (the archived `com.google.play.billing` / GPBL3 plugin) is present anywhere under `Assets/` or `Packages/`.
  - [ ] Do a trial `git status` to confirm `Library/` and IDE project files are untracked, and `Assets/.../*.meta` files ARE tracked.
- [ ] **Task 6 — Smoke-verify the foundation**
  - [ ] Reopen the project clean (or reimport) and confirm zero compile errors and zero console errors.
  - [ ] Produce a **debug `.aab` build** (or at minimum confirm the build pipeline runs to the point of producing an App Bundle) to prove AC-2 end-to-end. If a full device build isn't feasible in this environment, document exactly how far the build got and what's left to verify on a build machine.
  - [ ] Commit the initialized project (per CLAUDE.md: commit + push at the end of the session).

## Dev Notes

### What this story is (and is NOT)

This is the **FIRST implementation story** — the architecture explicitly names project init as story #1 [Source: docs/architecture.md#Initialization (first implementation story); docs/epics.md#AR-1]. The "starter" here is **not** a code scaffold — for a Unity AR game the equivalent of a `create-app` is a **Unity Hub project template + a verified, pinned package manifest** [Source: docs/architecture.md#Primary Technology Domain].

- **DO:** create the project via **Unity Hub** (the project is initialized via Unity Hub, never by hand-writing `.csproj`/`.sln`/`Library/` — those are generated and gitignored) [Source: CLAUDE.md#Working conventions; docs/architecture.md#Primary Technology Domain].
- **DO NOT** create any assembly definitions, services, `Assets/Veilwalkers/` folder tree, or C# code in this story. **Assembly boundaries + the composition root are Story 1.2.** This story stops at: project opens, packages pinned, Android settings set, hygiene verified. [Source: docs/epics.md#Story 1.2]

### Current repo state (verified on disk)

The repo root currently contains only planning/config: `.claude/`, `_bmad/`, `_bmad-output/`, `docs/`, `CLAUDE.md`, `README.md`, `.git/`, and a **`.gitignore` that already exists and is already correct** for AC-3. There is **no** `Assets/`, `Packages/`, or `ProjectSettings/` yet — this story creates them via Unity Hub.

- **`.gitignore` is already in place and already satisfies AC-3** (it ignores `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.sln`, `*.keystore`, `*.jks`, `keystore.properties`, `google-services.json`, `**/secrets/`, and keeps `Assets/**/*.meta` tracked). **Verify it, do not rewrite it.** One thing to double-check: it must NOT accidentally ignore `ProjectSettings/` or `Packages/` (it does not today — confirm after Unity generates them). [Source: C:\Users\james\Desktop\Veilwalkers\.gitignore]
- The repo IS a git repo (note: the harness environment reported "not a git repository" for the working dir, but `.git/` exists at the Veilwalkers root — treat it as a normal repo; commit + push at end per CLAUDE.md).

### Pinned package set & why (the compliance core of this story)

| Package | Pin | Why |
|---|---|---|
| `com.unity.xr.arfoundation` | 5.x (matched to Unity 2022.3 LTS) | URP-compatible AR rendering baseline for occlusion/lighting + mid-range perf (NFR-1). Do NOT use 6.x (needs Unity 2023.2+). |
| `com.unity.xr.arcore` | **same version as AR Foundation** | AR Foundation 5 requires the matching ARCore provider version for Android. |
| `com.unity.purchasing` (Unity IAP) | 5.0.0+ | Bundles **Google Play Billing Library 8.0.0** — the only currently-compliant Play Billing path (Google requires ≥7.0.0 since 2025-08-30). |
| ARCore Extensions for AR Foundation | `arf5` branch (git/tarball) | Google's extensions layer that officially supports AR Foundation 5. |
| `com.unity.nuget.newtonsoft-json` | latest 2022.3-compatible | Needed for the AES-encrypted JSON `SaveModel` in Story 1.3 — pin now so the manifest is authoritative. |

[Source: docs/architecture.md#Selected Foundation; docs/architecture.md#Technology Stack; docs/epics.md#AR-1]

**REJECTED — do not use:** the **legacy "Google Play Billing Plugin for Unity"** (Google's archived plugin, stuck on Billing Library 3, archived Apr 2026, non-compliant). Unity IAP 5 is the chosen path. Verifying this plugin is absent is an explicit AC-3 requirement. [Source: docs/architecture.md#Starter Options Considered; docs/epics.md#AR-1]

### Android build target settings (AC-2 specifics)

- Platform **Android**, **AR Required** (not Optional — this is an AR-first game), **ARM64** target architecture, **IL2CPP** scripting backend, **Min API per ARCore** (Android 7.0 / API 24 floor). Output = **Android App Bundle (.aab)**. [Source: docs/architecture.md#Initialization; docs/architecture.md#Infrastructure & Deployment; docs/epics.md#AR-1, AR-21]
- Signing/keystore is **out of scope here** — it's a release-time concern (gitignored keystore + `keystore.properties`), reserved by AR-21. Do not create secrets in this story. [Source: docs/architecture.md#Development / Build / Deploy; docs/epics.md#AR-21]

### Project Structure Notes

- All first-party code/content will eventually live under a single `Assets/Veilwalkers/` root, but **creating that tree is NOT this story** — it's seeded as assemblies land in Story 1.2 onward. This story only needs the bare Unity-generated `Assets/`, `Packages/`, `ProjectSettings/`. [Source: docs/architecture.md#Complete Project Directory Structure]
- `ProjectSettings/` is **committed** (Unity project config); `Packages/manifest.json` + `Packages/packages-lock.json` are **committed** (the pinned foundation). `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.sln` are **gitignored** (regenerated locally). [Source: docs/architecture.md#Complete Project Directory Structure; docs/architecture.md#Development / Build / Deploy]
- **No detected conflicts** between the story and the unified structure. The only variance to watch: ensure Unity's auto-generated `.gitignore` (if the template writes one) does not *replace* the existing curated one — merge/keep the existing curated `.gitignore`.

### Testing Requirements

- There is **no unit-test surface** in this story (no first-party C# code yet). `Architecture.Tests` (asmdef-acyclicity + `MonsterDatabase.Count == 67`) arrives with Story 1.2 / Epic 2 — do **not** try to author it here. [Source: docs/epics.md#AR-20; docs/architecture.md#Tests]
- Verification for this story is **manual / build-gate**, not automated:
  1. Project opens + compiles clean (zero console errors).
  2. `manifest.json` shows the four pinned packages at the intended versions; `packages-lock.json` committed.
  3. Player Settings reflect Android / AR Required / ARM64 / IL2CPP / Min API.
  4. A `.aab` build configuration is selected and the build runs (full device build is a release-gate checklist item, per NFR-5 / AR-14 note that device-millisecond budgets are release-gate items, not unit tests). [Source: docs/architecture.md#AR-14 equivalent / release-gate note]

### Architecture compliance call-outs (so later stories aren't retro-broken)

These don't add work to *this* story but the foundation must not preclude them:
- Persistence will use **Newtonsoft JSON + AES + atomic temp-swap** behind `IProgressStore` (why Newtonsoft is pinned now). [Source: docs/epics.md#AR-2]
- Billing must stay **one-way (Billing → Economy)** and exactly-once — Unity IAP 5 is the substrate that enables it. [Source: docs/epics.md#AR-12]
- The acyclic assembly graph `Core ← {Persistence, Economy, Monsters} ← {AR, Encounter, Billing} ← App ← UI` is enforced by `.asmdef` + a test **in Story 1.2**, not here. [Source: docs/architecture.md#Architectural Boundaries; docs/epics.md#AR-5]

### References

- [Source: docs/epics.md#Story 1.1: Initialize Unity AR project with pinned packages] — user story + ACs.
- [Source: docs/epics.md#AR-1 (Starter / Project Init — FIRST story)] — pinned package set, Player Settings, legacy plugin rejection.
- [Source: docs/epics.md#AR-21 (Build/deploy)] — .aab, IL2CPP, ARM64, AR Required, gitignored signing.
- [Source: docs/architecture.md#Selected Foundation: Unity Hub "3D (URP)" + pinned AR/IAP packages] — initialization steps 1–4, rationale, "verify at implementation time" note.
- [Source: docs/architecture.md#Starter Options Considered] — why 3D (URP), why not the AR template, why legacy billing plugin is rejected.
- [Source: docs/architecture.md#Technology Stack] — versions, build target.
- [Source: docs/architecture.md#Complete Project Directory Structure] — what's committed vs gitignored.
- [Source: CLAUDE.md#Working conventions] — Unity Hub init, never commit secrets, commit + push every session.
- [Source: C:\Users\james\Desktop\Veilwalkers\.gitignore] — already satisfies AC-3.

### Latest Tech Information (web-verified June 2026)

- **AR Foundation 5 / Unity 2022.3 LTS:** AR Foundation 5.0 reached "Released" status during 2022.2 dev and is supported through 2022.3 LTS; **package version 5.2.2 is the release published for Unity 2022.3** (use the latest stable 5.x that Package Manager offers for 2022.3 — do NOT jump to 6.x, which requires Unity 2023.2+). Keep `com.unity.xr.arcore` at the **same version number** as AR Foundation. [Verify the exact 5.x patch in Package Manager at implementation time.]
- **ARCore Extensions:** install via git URL `https://github.com/google-ar/arcore-unity-extensions.git#arf5` (or the `arf5` tarball from GitHub releases) — this is the AR-Foundation-5-compatible branch.
- **Unity IAP:** `com.unity.purchasing` **5.0.0** (released 2025-08-07) ships **Google Play Billing Library 8.0.0**; this is the version that satisfies Google's ≥7.0.0 requirement (enforced from 2025-08-31). Pin 5.0.0 or the latest 5.x.
- The architecture doc itself flags: "**Re-verify the AR Foundation 5 / ARCore 5 verified pairing and Unity IAP version at implementation time.**" The above is that verification as of this story's creation. [Source: docs/architecture.md#Note on re-verification]

Sources:
- [AR Foundation 5.0 install / Unity 2022 support](https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@5.0/manual/project-setup/install-arfoundation.html)
- [Upgrade to AR Foundation 5 (ARCore)](https://developers.google.com/ar/develop/unity-arf/upgrade-to-ar-foundation-5)
- [ARCore Unity Extensions releases (arf5)](https://github.com/google-ar/arcore-unity-extensions/releases)
- [Unity IAP / Google Play Billing 8.0.0 (Unity Support)](https://support.unity.com/hc/en-us/articles/39089405223316-Google-Play-Billing-Library-update-7-1-1)

### Project Context Reference

No `project-context.md` exists in the repo yet (the BMM `persistent_facts` glob matched nothing). Authoritative context for this story comes from `docs/architecture.md`, `docs/epics.md`, and `CLAUDE.md`. If a `project-context.md` is generated later, re-check this story's pins against it.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia, dev-story workflow)

### Debug Log References

- Environment probe (2026-06-10): Unity Hub/Editor not installed on dev machine.
  - `C:\Program Files\Unity Hub\Unity Hub.exe` → not found
  - `C:\Program Files\Unity\Hub\Editor` → not found
  - `HKCU:\Software\Unity Technologies\Unity Hub` → no key
  - `Get-Command unity` → not on PATH
  - Repo root has no `Assets/`, `Packages/`, or `ProjectSettings/` (Unity never run here).
- `git rev-parse HEAD` → c49c8bfdb21ad941e99fa56ac138eb45d2a93182 (baseline_commit).

### Completion Notes List

- Ultimate context engine analysis completed — comprehensive developer guide created.
- **BLOCKED at Task 1, subtask 1:** Story 1.1 is an interactive Unity Hub + Unity
  Editor task (CLAUDE.md mandates Unity-Hub init, forbids hand-writing project
  files). Unity Hub/Editor is not installed in this environment, so the editor
  steps (project creation, package pinning via Package Manager, Player Settings,
  `.aab` build gate) cannot be executed or verified by the agent. Faking these by
  hand-writing `manifest.json`/`ProjectSettings/`/a stub `.aab` would violate the
  story's explicit constraints and could not satisfy AC-1's "compiles with no
  errors" or AC-2's build gate — so the workflow halted instead of falsifying
  completion. Per user direction (AskUserQuestion → "Prep a setup guide + assets"),
  produced a precise human-in-the-loop setup guide + manifest reference.
- **AC-3 — VERIFIED NOW (no Unity needed):** repo-root `.gitignore` already
  satisfies AC-3 in full. Audited line-by-line:
  - Ignores: `[Ll]ibrary/`, `[Tt]emp/`, `[Ll]ogs/`, `*.csproj`, `*.sln`,
    `*.keystore`, `*.jks`, `keystore.properties`, `google-services.json`,
    `**/secrets/` + `*.env` (covers billing service-account keys).
  - Keeps tracked: `!/[Aa]ssets/**/*.meta`.
  - Does **NOT** ignore `ProjectSettings/` or `Packages/` (confirmed absent from
    ignore rules → they will be committed once Unity generates them).
  - No `.gitignore` edits required. (Note: `*.aab` is intentionally ignored on
    line 28 — build artifacts aren't committed; AC-2 is a build-gate, not a
    committed deliverable.)
  - Legacy Google Play Billing Plugin absence: trivially true today (no `Assets/`
    or `Packages/` exist yet); must be re-confirmed after Unity init (Task 5).
- **Tasks 1, 2, 3, 4, 6: NOT done** — require Unity Editor. Left unchecked
  (truthful state). See `1-1-unity-setup-guide.md` for exact steps/values and
  `1-1-manifest-reference.json` for target package pins.
- **To resume:** install Unity Hub + Unity 2022.3 LTS + Android module (per the
  setup guide), complete the editor steps, then re-run `bmad-dev-story` / tell
  Amelia "resume story 1-1". The agent will re-probe the environment, verify the
  package pins / Player Settings / hygiene, tick the task checkboxes, commit+push,
  and move the story to `review`.

### File List

- `_bmad-output/implementation-artifacts/1-1-unity-setup-guide.md` (added) — human-in-the-loop Unity setup guide for the editor-only steps.
- `_bmad-output/implementation-artifacts/1-1-manifest-reference.json` (added) — target package pins reference for `Packages/manifest.json`.
- `_bmad-output/implementation-artifacts/1-1-initialize-unity-ar-project-with-pinned-packages.md` (modified) — status, baseline_commit, Dev Agent Record, File List, Change Log.

## Change Log

- 2026-06-10 — Started dev-story; captured baseline_commit `c49c8bf`. Verified AC-3
  (`.gitignore`) satisfied with no edits. Halted at Task 1 (Unity Hub/Editor not
  installed; editor-only story). Produced `1-1-unity-setup-guide.md` and
  `1-1-manifest-reference.json` to enable the human-in-the-loop editor steps.
  Status → in-progress (blocked). (Amelia / claude-opus-4-8)
