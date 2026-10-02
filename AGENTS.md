# Veilwalkers project instructions

## Mission

Veilwalkers is an existing Unity Android AR monster-hunting and collection game. Players lure, scan, capture, and slay exactly 67 monsters in real-world AR, collect them in the Codex, and eventually use captured monsters in The Pit. The current brownfield priority is the ordered editor, render, device, accessibility, billing, and Play-release gate—not a redesign of the logic-complete core.

## Ground truth and workflow

Inspect the current Unity source, scenes, assets, tests, configuration, and dirty worktree. Read `docs/build-guide.md` and `docs/epic-8-device-release-gate.md`; use `docs/epic-8-editor-walkthrough.md` for the current ordered Gate 1–5 procedure. The design doc is `docs/game-design-doc.md`; the canonical economy and assembly boundaries are in `docs/architecture.md`. Treat older PRD, architecture, epics, stories, and superseded runbooks as useful requirements and historical decisions, not proof of current state.

Use OpenSpec under `openspec/` for a non-trivial behavior change, architectural fix, scene or device contract, billing change, refactor, or production-readiness change. A trivial local fix does not require a proposal. OpenSpec now replaces BMAD as the default change layer. Preserve valuable BMAD-created product and architecture documents, but do not require analyst, PM, architect, scrum-master, and developer handoffs, duplicate planning artifacts, or sprint ceremony.

Use an installed skill when its workflow fits (`gate-review`, `tdd`, `codebase-design`). Trace the actual path before simplifying; minimalism never weakens AR safety, purchases, data integrity, accessibility, release gates, or device verification.

Use this precedence: James's current request, this file, the active OpenSpec change, relevant skills, then general defaults. Use subagents only for independent work.

## Product and safety contracts

- Show the mandatory AR safety warning immediately on entering AR mode.
- Camera permission is required and clearly disclosed; location remains optional for world spawns.
- Use official Google Play Billing for every purchase. Never use a third-party payment path.
- Keep Teen-rated stylized-spooky content; no realistic gore or extreme horror.
- Preserve exactly 67 monsters.
- Preserve the canonical economy in `docs/architecture.md`: credits buy Basic Lure, Premium Lure, Multi-Lure, and Slay only; Scan, base Capture, and Retry are free; Strong Capture, Stability Boost, and Nightveil Filter are XP-earned, non-purchasable charges.
- Treat purchase mutation, acknowledgement, and reconciliation as atomic integrity work. Never infer a successful purchase from a timeout or ambiguous callback.
- Keep secrets, keystores, `google-services.json`, `keystore.properties`, and Play service-account keys out of Git and logs.

## Architecture and release constraints

- Unity project files are initialized through Unity Hub. Do not hand-author generated `Library/`, `.csproj`, or `.sln` content.
- Preserve feature-oriented assembly boundaries and the acyclic dependency direction documented in `docs/architecture.md`.
- The permanent bundle identifier is `com.veilwalkers.app` after publication. Minimum Android SDK stays 24; re-check the Play target-SDK floor before every submission and update the guarded source of truth rather than only prose.
- **Bundle id (PERMANENT once published):** `com.veilwalkers.app`. Do not change for a live Play listing.
- **Android target-SDK floor: API 35** (Play floor *as of 2026-06* — Google raises it ~yearly, **re-check before each submission**). Single source of truth = `MinPlayTargetSdk` in `BuildConfigGuardTests.cs`; the build guide cites it. Min SDK stays **24** (ARCore floor).
- **Public GitHub repo** (`J3K420/veilwalkers`), same as GraveStory.
- **Never commit secrets** — keystores (`*.jks`/`*.keystore`), `google-services.json`, `keystore.properties`, Play Billing service-account keys. All are gitignored; keep it that way.
- `docs/build-guide.md` owns the Play-submission config; `BuildConfigGuardTests` regression-guards it. Gradle templates and resolved `Assets/Plugins/Android/*.aar` are gitignored.
- Run EDM4U Force Resolve after a fresh clone. Resolved Android templates and AARs remain ignored.
- Device-only `#if UNITY_ANDROID && !UNITY_EDITOR` paths have zero EditMode coverage. Do not claim them verified without the required ARCore device or PlayMode check.
- Follow Gate 1 through Gate 5 in order: AR rig and device bodies; views, navigation, and backgrounding; render and billing glue; accessibility; signed AAB and on-device release smoke.

## Production readiness

A request to finish, polish, prepare, or ship includes applicable logic and scene correctness, AR lifecycle and recovery, safety and camera disclosure, billing and reconciliation, save integrity, accessibility, privacy and security, UX, rendering, performance, diagnostics, tests, Unity build configuration, signing, device compatibility, Play metadata, rollback, and demonstrated debt. Fix findings without speculative rewrites.

## Verification and commands

Close the Unity Editor before batch tests. Never combine `-runTests` with `-quit`; that can report a false success. The current documented headless floor is 691 total: 690 passed and one intentionally ignored AR rig guard until Gate 1 is authored.

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" `
  -batchmode -nographics -projectPath "C:\Users\james\Desktop\Veilwalkers" `
  -runTests -testPlatform EditMode `
  -testResults "$env:TEMP\vw-editmode.xml" `
  -logFile "$env:TEMP\vw-editmode.log"
```

For release builds, use the Veilwalkers Editor menu or `Veilwalkers.EditorTools.VeilwalkersBuilder.BuildAndroidAppBundle` exactly as documented in `docs/build-guide.md`. Signing values come from `VEILWALKERS_KEYSTORE_*` environment variables.

After headless checks, perform the applicable PlayMode and physical ARCore-device gates. Verify safety copy, permission flow, plane and anchor lifecycle, recovery, real render and touch targets, TalkBack, dynamic type, reduced motion, billing purchase and acknowledgement, signed AAB installation, and cold-start behavior where the change reaches those surfaces.

Before a non-trivial change is handed off, merged into the default branch, opened as a PR, or released, run the `gate-review` skill on that change. For a multi-agent build, run it once on the integrated result. Resolve every finding as fixed, dismissed with evidence, or deferred with a note. Docs-only and typo-level edits are exempt. The Gate 1–5 release procedure and the authorization below are additional gates.

Commit, push, deployment, Play submission, purchase, credential change, and other external actions require James's explicit authorization for the exact action. Public repository status is not authorization.
