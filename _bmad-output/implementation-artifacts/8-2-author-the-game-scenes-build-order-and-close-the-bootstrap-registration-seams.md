# Story 8.2: Author the game scenes, set the final build order, and close the Bootstrap registration seams

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

baseline_commit: f0057a9

## Story

As a developer assembling the runnable surface set,
I want Bootstrap + the four non-AR game scenes authored, a minimal AR Hunt scene reserved at its build slot, SampleScene removed, the six-slot build order pinned, the on-disk `Bootstrap.unity` EconomyConfig assigned, and the MonsterDatabase-gated CodexService/EncounterService registration seams closed in Bootstrap,
so that every later View, navigation transition, and device-glue component has a real scene to live in and a fully-wired GameServices to resolve from — and no dead template content ships.

## ⚠️ Verifiability reality (the load-bearing scope decision for THIS story — James, 2026-06-16)

8.2 was scoped as "mixed (headless content + device)" but a recon during CS surfaced that **most of 8.2 is Editor/device-bound, not headless** — materially more so than 8.1. The honest split, and James's decision (**"headless slice now, rest to checklist"**):

- **SAFELY HEADLESS (done in the automator session, gated):**
  1. **Remove `SampleScene` from the enabled build list** (`EditorBuildSettings.asset`) — dead template content that must not ship; a pure text de-registration.
  2. **A non-vacuous `BuildSettingsGuardTests` guard** that asserts: (a) `SampleScene` is NOT in the enabled build list; (b) every CURRENTLY-enabled scene path resolves to a real `.unity` asset on disk (no phantom/missing-GUID scene ships); (c) `Bootstrap.unity` is enabled at index 0. On-disk text reads (the 8.1 `BuildConfigGuardTests` / Pit precedent).
  3. **A `Bootstrap.unity` `_economyConfig`-assignment guard** (scene-YAML text read) — documents + guards the CURRENTLY-UNMET gap that the on-disk Bootstrap MonoBehaviour has no `_economyConfig` serialized reference (a real latent boot-failure: `WireServices` requires it). **NOTE:** the guard is authored to ASSERT THE GAP IS CLOSED, but closing it (assigning the EconomyConfig asset fileID/guid into the scene's MonoBehaviour block) is an **Editor action** — hand-editing scene-YAML fileID references is exactly the error-prone work this story defers. So this guard is authored **`[Ignore]`-pending-Editor** (or as a documented expected-fail) with the close-it step in the checklist, OR (preferred) it asserts the gap is DOCUMENTED + the close step is a checklist item — see Decision B. It is NOT failed-green-faked.

- **EDITOR / DEVICE CHECKLIST (NOT done headlessly, NOT auto-claimed done — emitted for James):**
  4. **Author the 5 `.unity` scenes** (Onboarding, Home, AR Hunt placeholder, Codex, Shop) — a `.unity` is YAML, but authoring a correct scene (GameObject hierarchies, Canvas, serialized component refs with valid fileIDs/guids) by hand is error-prone and effectively needs the Editor; wrong YAML only surfaces when a device opens the scene. → Editor authoring.
  5. **Register the 5 scenes into the six-slot build order** (`Bootstrap → Onboarding → Home → AR Hunt → Codex → Shop`) — only AFTER they exist on disk (else the guard's "every enabled path resolves to a real asset" correctly fails). → Editor authoring, after #4.
  6. **Assign `_economyConfig` in `Bootstrap.unity`** (the scene MonoBehaviour serialized ref) — an Editor inspector drag, not safe hand-YAML. → Editor.
  7. **Author `MonsterDatabase.asset` + the 3–5 MVP monster `.asset` files with imported sprite art** (the long-standing Story 2.2 Task-3 `[~]` deferral) — needs the Editor (a `Sprite Art` ref needs an imported texture; a null `Art` fails `Validate()`). → Editor authoring.
  8. **Close the Bootstrap CodexService/EncounterService registration seams** — blocked on #7 (both services need a constructed `MonsterDatabase`). The `Bootstrap.cs` seam comments already document the exact close steps (construct `CodexService(saveService, monsterDatabase, clock)` + `EncounterService` with the shared `economyMutationLock`, wire the 2nd `IInsufficientCreditsSource` + the real `IEncounterSnapshotPort`). → after #7; the registration CODE is headless-compilable but its EFFECT (services resolve post-boot) is a runtime/device claim.

## Acceptance Criteria

Sourced from `docs/epics.md#Story-8.2` + the Epic-8 verifiability invariant. Re-scoped per the reality split above (James's "headless slice now, rest to checklist").

**AC-1 (HEADLESS) — SampleScene is removed from the enabled build list; every enabled scene resolves to a real asset**
**Given** `ProjectSettings/EditorBuildSettings.asset`
**When** the enabled scene list is asserted
**Then** `SampleScene` (guid `99c9720ab356a0642a771bea13969a05`) is NOT in the enabled list, AND every currently-enabled scene path resolves to an EXISTING `.unity` asset on disk, AND `Bootstrap.unity` is enabled at index 0 — asserted by an EditMode test parsing `EditorBuildSettings.asset` as TEXT off disk. (Until the 5 game scenes are authored in the Editor, the enabled list is just `Bootstrap(0)`; the guard grows automatically as real scenes are registered — it never asserts a phantom path.)

**AC-2 (CHECKLIST) — the six-slot build order is the target, registered after the scenes exist**
**Given** the 5 authored game scenes
**When** they are registered (Editor, after authoring)
**Then** the enabled list becomes exactly `Bootstrap(0) → Onboarding(1) → Home(2) → AR Hunt(3) → Codex(4) → Shop(5)`, each `enabled:1`. The AC-1 guard then passes with 6 real scenes (it does not need editing — it asserts "every enabled path is real," which holds as scenes are added). **Editor/device checklist** — not headless.

**AC-3 (HEADLESS) — the `Bootstrap.unity` EconomyConfig gap is guarded/documented**
**Given** the on-disk `Bootstrap.unity`
**When** the MonoBehaviour block is asserted
**Then** a scene-YAML text assertion documents whether the Bootstrap `m_Script` block has an assigned `_economyConfig` serialized reference — CURRENTLY UNMET (the on-disk scene's Bootstrap component has no `_economyConfig`; a real latent boot-failure since `WireServices` requires it). The guard is authored to verify the closed state; closing it (the Editor inspector assignment) is a checklist item (Decision B governs whether the guard is `[Ignore]`-pending or asserts-the-documented-gap so it is never green-but-false).

**AC-4 (HEADLESS) — the LoadPhase staging contract regression stays green**
**Given** `Bootstrap.cs` / `LoadPhase.cs`
**When** the staging CONTRACT is asserted in EditMode
**Then** the EXISTING `LoadPhaseContractTests` (enum shape+order `{EssentialSync, WarmupAsync, Ready}`) + the `AR.Tests` does-NOT-warm structural guard stay green (8.2 changes neither). The RUNTIME staging behaviors (WireServices synchronous, warmup not awaited before Ready, GameServices.IsReady false until wired) are NOT EditMode-runnable (they need `Bootstrap.Awake` on a `[DefaultExecutionOrder(-1000)]` MonoBehaviour in a scene) → device/PlayMode checklist.

**AC-5 (CHECKLIST) — author MonsterDatabase.asset + MVP monster assets**
**Given** the Story 2.2 Task-3 `[~]` deferral
**When** the Editor-authoring session runs
**Then** `MonsterDatabase.asset` + 3–5 MVP monster `.asset` files (incl. ≥1 Rare+ with real imported art) are authored, each passing `Validate()`, filenames `<Id>_<Name>`, `PopulatedCount ∈ [3..5]`; the old tautological `Asset_file_name_convention` test is replaced by the on-disk audit (the deferred-work.md:327 close-out). **Editor/device checklist** — needs imported textures.

**AC-6 (CHECKLIST + partial-headless) — close the CodexService/EncounterService Bootstrap registration seams**
**Given** `MonsterDatabase.asset` is authored (AC-5)
**When** `WireServices` is updated
**Then** Bootstrap constructs + registers `CodexService(saveService, monsterDatabase, clock)` and `EncounterService` (with the SHARED `economyMutationLock`), wiring the 2nd `IInsufficientCreditsSource` + the real `IEncounterSnapshotPort` (replacing the `NoEncounter`/null seams). The registration CODE compiles headlessly + `AcyclicDependencyTests` stays green (the headless half); whether GameServices RESOLVES both post-boot is a runtime/device claim. **Blocked on AC-5 → Editor/device checklist.**

**AC-7 (HEADLESS) — no new illegal asmdef edge**
**Given** the acyclic assembly graph
**When** any 8.2 change lands
**Then** no new asmdef edge beyond the `AcyclicDependencyTests` allowed matrix (headless guard). (The headless slice adds only a test — no production edge.)

### Derived / load-bearing requirements

- **NO green-but-false claims.** The AC-3 EconomyConfig guard must NOT be written to pass against the current (unassigned) scene — that would be the exact `tautological-test-trap` / no-op-adapter class the retro flagged. Either `[Ignore("Editor must assign _economyConfig in Bootstrap.unity — Story 8.2 checklist")]` it (visible as a pending pin), or assert the gap is documented with the close-step owned. Decision B.
- **The headless slice ships only what's real:** SampleScene removal + the build-settings guard + the (pending/documented) EconomyConfig guard + the LoadPhase regression confirmation. NO phantom scene registration, NO faked asset, NO faked seam closure.
- **The Editor/device checklist is explicit + owned**, emitted to `deferred-work.md` under 8.2 + carried into the Phase-3 release-gate checklist — never auto-claimed done.
- **NO gameplay logic change.** 8.2 touches `EditorBuildSettings.asset` (data), adds a guard test, and (checklist) scenes/assets/Bootstrap registration. No service logic is re-decided.

## Tasks / Subtasks

- [ ] **Task 1 — Recon (read-only baseline)** (done in CS): confirmed `EditorBuildSettings.asset` enables `Bootstrap(0)` + `SampleScene(1)`; `Bootstrap.unity` has the Bootstrap `m_Script` (guid `8b2e1cbc78ac4df697f6bddfafbc3f80`) but NO `_economyConfig`; `MonsterDatabase.asset` is unauthored (0 found — Story 2.2 Task-3 `[~]`); the CodexService/EncounterService seam comments in `Bootstrap.cs` document the close steps. Recorded.
- [ ] **Task 2 (HEADLESS) — Remove SampleScene from the enabled build list** `ProjectSettings/EditorBuildSettings.asset`: de-register the `SampleScene` entry (guid `99c9720ab356a0642a771bea13969a05`), leaving `Bootstrap.unity` enabled at index 0. Edit the minimal YAML; preserve structure. (Physical `SampleScene.unity` asset deletion is optional cleanup — the de-registration is what matters; default KEEP the asset, just un-enable it, to minimize diff risk — Decision A.)
- [ ] **Task 3 (HEADLESS) — `BuildSettingsGuardTests.cs`** in the existing `Veilwalkers.Architecture.Tests` (no asmdef edit): Pin A — `SampleScene` guid NOT in the enabled list; Pin B — every enabled scene path resolves to an existing `.unity` on disk (the non-phantom guard; grows with the scene list); Pin C — `Bootstrap.unity` enabled at index 0. On-disk text reads, graceful missing-file message. Mutation-test in dev (re-enable SampleScene → Pin A RED; point an enabled entry at a nonexistent path → Pin B RED).
- [ ] **Task 4 (HEADLESS) — the `Bootstrap.unity` EconomyConfig guard (Decision B)**: author a `Bootstrap_scene_assigns_EconomyConfig` test that reads `Bootstrap.unity` text and asserts the Bootstrap `m_Script` MonoBehaviour block has a non-zero `_economyConfig` reference. Since the assignment is an Editor action (not safe hand-YAML), mark it `[Ignore("Editor: assign EconomyConfig on the Bootstrap component in Bootstrap.unity — Story 8.2 checklist item; un-ignore when assigned")]` so it is a VISIBLE pending pin, never a false green. Document the close-step in the checklist.
- [ ] **Task 5 (HEADLESS) — confirm the LoadPhase staging regression** (AC-4): re-run `LoadPhaseContractTests` + the AR does-not-warm guard green (8.2 changes neither). No new test needed; the headless gate confirms.
- [ ] **Task 6 (CHECKLIST — emit, do NOT execute) — the Editor/device authoring**: write the explicit checklist (scenes #4-5, EconomyConfig assign #6, MonsterDatabase.asset #7, seam closure #8) to `deferred-work.md` under 8.2 + the Phase-3 release-gate checklist. Each item names its owner (Editor-authoring session) + its blocker chain.
- [ ] **Task 7 (HEADLESS) — headless gate**: baseline 681 + the new build-settings pins (≈3 active + 1 ignored). Editor closed; never trust exit 0 — read `test-results.xml` + grep `error CS`; delete artifacts before commit. Confirm `EditorBuildSettings.asset` still parses.

## Dev Notes

### Decisions to make

| # | Decision | Default | Why |
|---|---|---|---|
| A | SampleScene: de-register only, or also delete the `.unity` asset? | **De-register only (keep the asset, un-enable it).** | The de-registration stops it shipping (the real goal); deleting the asset is a larger diff (+ its `.meta`) with no extra benefit and a small risk if anything references it. A future cleanup can delete it. |
| B | The EconomyConfig guard: `[Ignore]`-pending vs assert-documented-gap. | **`[Ignore]`-pending** with a clear message naming the Editor close-step. | An `[Ignore]`d test is a visible pending pin in the runner (shows the work isn't done) without faking green. Asserting "the gap is documented" risks reading as satisfied. The `[Ignore]` is un-ignored by the Editor session that assigns the ref. |
| C | Register the 6-scene order now (phantom paths) vs only-real-scenes. | **Only-real-scenes (just remove SampleScene now).** | Registering phantom scene paths that don't exist on disk would either break the build or force the guard to allow missing paths (defeating its purpose). The guard asserts "every enabled path is real"; scenes join the list as they're authored. |

### Sanctioned-deviation matrix

| # | Deviation | Why sanctioned | Source |
|---|---|---|---|
| D1 | 8.2's headless slice ships only a SampleScene de-registration + a guard test + an `[Ignore]`d EconomyConfig pin — NOT the scenes/assets/seam-closure. | The recon proved scenes + `MonsterDatabase.asset` + seam closure are Editor-bound (hand-authored scene YAML is error-prone + only fails on-device; the asset needs imported textures); James chose "headless slice now, rest to checklist." | epics.md#Story-8.2; Epic-8 verifiability invariant; James 2026-06-16 |
| D2 | The 6-scene build order is NOT registered now; only SampleScene is removed. | Phantom scene paths would break the build or defeat the "every enabled path is real" guard (Decision C). The order is the checklist target, reached as scenes are authored. | Decision C; the non-phantom guard |
| D3 | The EconomyConfig guard is `[Ignore]`d, not passing. | Closing the gap is an Editor inspector assignment (unsafe to hand-YAML); an `[Ignore]`d pin is visible-pending without faking green (avoids the no-op-adapter / tautological-test trap). | Decision B; retro AI#6; [[tautological-test-trap]] |
| D4 | AC-5/AC-6 (MonsterDatabase.asset + seam closure) are NOT done; emitted as checklist. | The Story 2.2 Task-3 `[~]` deferral (deferred-work.md:327) — the asset needs the Editor; every Bootstrap registration seam is blocked on it (deferred-work.md:187,199,211,225,240). | deferred-work.md:327,187..240; Bootstrap.cs seam comments |

### Architecture compliance

- **Epic-8 verifiability invariant:** headless pins are on-disk text reads (`EditorBuildSettings.asset`, `Bootstrap.unity`); the automator never authors a real scene, runs Bootstrap.Awake, or drives SceneManager in EditMode.
- **[[tautological-test-trap]] / retro AI#6:** the build-settings guard is mutation-tested (re-enable SampleScene → RED); the EconomyConfig guard is `[Ignore]`d not faked-green.
- **The `MonsterDatabase.asset` blocker chain (deferred-work.md:327, 187–240):** every CodexService/EncounterService registration seam is downstream of the unauthored asset; 8.2 cannot construct those services headlessly.
- **No green-but-false:** the honest split is the whole point of this story's re-scope — the device half is owned + visible, not silently claimed.

### Source tree components to touch

**NEW (headless):**
- `Assets/Tests/EditMode/Architecture.Tests/BuildSettingsGuardTests.cs` (+ `.meta`) — the build-settings guard (SampleScene removed; enabled paths real; Bootstrap@0) + the `[Ignore]`d EconomyConfig pin.

**MODIFIED (headless):**
- `ProjectSettings/EditorBuildSettings.asset` — de-register SampleScene (un-enable / remove the entry), leaving Bootstrap@0.
- `_bmad-output/implementation-artifacts/deferred-work.md` — the 8.2 Editor/device checklist (scenes, EconomyConfig assign, MonsterDatabase.asset, seam closure).
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — 8.2 → done (headless slice); the device half tracked in deferred-work + the release-gate checklist.

**CHECKLIST (NOT touched headlessly — emitted for the Editor session):**
- The 5 `.unity` scenes; `Bootstrap.unity` (`_economyConfig` assign); `MonsterDatabase.asset` + monster `.asset`s; `Bootstrap.cs` (seam closure — code compilable but effect is runtime).

### Testing standards summary

- Headless EditMode gate ([[unity-headless-testing]]): Editor closed; never trust exit 0; read `test-results.xml` + grep `error CS`; delete artifacts before commit; confirm `EditorBuildSettings.asset` still parses.
- Non-vacuous + mutation-tested (Pin A/B); the EconomyConfig pin is `[Ignore]`d-visible (Decision B).
- Baseline 681; target 681 + the active build-settings pins (the `[Ignore]`d one shows as skipped, not passed).

### References

- [Source: docs/epics.md#Story-8.2] — the AC blocks (scene list + build order + Bootstrap registration closure) — re-scoped here per the verifiability reality.
- [Source: _bmad-output/implementation-artifacts/deferred-work.md:327] — the Story 2.2 Task-3 `[~]` MonsterDatabase.asset authoring deferral (Editor-bound; the blocker for the seam closure).
- [Source: deferred-work.md:187,199,211,225,240] — every CodexService/EncounterService Bootstrap registration seam blocked on the unauthored `MonsterDatabase.asset`.
- [Source: Assets/Veilwalkers/App/Bootstrap.cs (CodexService/EncounterService seam comments ~310–341)] — the documented close steps.
- [Source: ProjectSettings/EditorBuildSettings.asset] — current: Bootstrap(0, guid 3f8a2b6c…) + SampleScene(1, guid 99c9720a…).
- [Source: Assets/Veilwalkers/Scenes/Bootstrap.unity] — the Bootstrap MonoBehaviour (m_Script guid 8b2e1cbc…), currently NO `_economyConfig` ref.
- [Source: Assets/Tests/EditMode/Architecture.Tests/BuildConfigGuardTests.cs] — the 8.1 on-disk-guard precedent (path resolution, graceful missing-file, non-vacuous).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Opus 4.8) — story-automator ungated cycle (Epic 8).

### Debug Log References

- Headless gate (8.2 slice): **684 passed / 0 failed / 1 skipped, 0 `error CS`** (total 685 = 681 baseline + 3 active build-settings pins + 1 `[Ignore]`d EconomyConfig pin shown as skipped). `test-results.xml` confirmed.
- **Mutation test (non-vacuous proof):** re-enabled SampleScene in `EditorBuildSettings.asset`, re-ran → exactly 1 failure, `SampleScene_is_not_in_the_enabled_build_list` (683 passed, 1 failed); Pins B/C correctly stayed green (the asset exists, just shouldn't ship; Bootstrap still index 0). Restored from backup; `git diff` shows only the clean SampleScene removal.
- Confirmation gate after the post-CR citation fix (doc-comment/message strings only): green.

### Completion Notes List

- **8.2 HEADLESS SLICE shipped** (James's "headless slice now, rest to checklist"): SampleScene de-registered from the build (closes the long-standing SampleScene-removal deferral) + `BuildSettingsGuardTests.cs` (3 active pins: SampleScene-not-enabled, every-enabled-path-real, Bootstrap@0; + 1 `[Ignore]`d EconomyConfig pin) + the LoadPhase regression confirmed. NO production logic change, NO asmdef edit (the guard uses the existing `Architecture.Tests`).
- **Decision A — RESOLVED: de-register SampleScene only** (kept the `.unity` asset, un-enabled it — minimal diff; physical deletion is an optional cleanup checklist item).
- **Decision B — RESOLVED: the EconomyConfig pin is `[Ignore]`d-pending** (visible as skipped, not faked green) — closing the gap is an Editor inspector assignment (unsafe to hand-author as scene YAML). Un-ignored by the Editor session that assigns the ref.
- **Decision C — RESOLVED: only-real-scenes** — only SampleScene removed; the 6-slot order is the checklist target reached as scenes are authored (the "every enabled path is real" guard would correctly fail on a phantom path).
- **The EDITOR/DEVICE half is emitted as an owned checklist** (deferred-work.md "Story 8.2 — Editor/device authoring checklist"): author the 5 scenes → register the 6-slot order → assign EconomyConfig → author MonsterDatabase.asset (Story 2.2 Task-3) → close the CodexService/EncounterService Bootstrap seams. Each names its blocker chain; NOT auto-claimed done. This is the honest discovery that 8.2 is mostly Editor-bound (scenes are error-prone hand-YAML; the asset needs imported textures; the seams are blocked on the asset).
- **CR (adversarial single-agent, fresh context):** clean-with-minor — every pin empirically validated non-vacuous + honest (no green-but-false; the `[Ignore]`d pin genuinely pending). 2 minors: (1) ensure `BuildSettingsGuardTests.cs.meta` is staged at commit (done — `git add -A`); (2) stale line-number citations in the guard's messages → FIXED (now name-based, drift-proof).

### File List

**NEW (headless):**
- `Assets/Tests/EditMode/Architecture.Tests/BuildSettingsGuardTests.cs` (+ `.cs.meta`) — the build-settings guard (3 active pins + 1 `[Ignore]`d EconomyConfig pin), on-disk text reads, mutation-tested.

**MODIFIED (headless):**
- `ProjectSettings/EditorBuildSettings.asset` — de-registered SampleScene (guid `99c9720a…`); Bootstrap remains the sole enabled scene at index 0.
- `_bmad-output/implementation-artifacts/deferred-work.md` — the "Story 8.2 — Editor/device authoring checklist" (5 scenes, 6-slot order, EconomyConfig assign, MonsterDatabase.asset, seam closure), each owned + blocker-chained.
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — 8.2 → done (headless slice; the device half tracked in deferred-work + the release-gate checklist).

**CHECKLIST (NOT touched headlessly — emitted for the Editor session):** the 5 `.unity` scenes; `Bootstrap.unity` (`_economyConfig` assign); `MonsterDatabase.asset` + monster `.asset`s; `Bootstrap.cs` (seam closure).

### Change Log

| Date | Change |
|---|---|
| 2026-06-16 | CS: created. Recon surfaced 8.2 is mostly Editor/device-bound (scenes + MonsterDatabase.asset + seam closure all Editor); James chose "headless slice now, rest to checklist." Story re-scoped: headless = SampleScene removal + build-settings guard + `[Ignore]`d EconomyConfig pin + LoadPhase regression; checklist = scene authoring + asset authoring + seam closure. |
| 2026-06-16 | DS: de-registered SampleScene from `EditorBuildSettings.asset` + authored `BuildSettingsGuardTests.cs`. Gate 684/0/1-skipped, 0 `error CS`. Mutation-tested (re-enable SampleScene → exactly the SampleScene pin RED → restored). Emitted the Editor/device checklist to deferred-work.md. Status → review. |
| 2026-06-16 | CR (adversarial single-agent, fresh context, proportional to the small honest slice): **clean-with-minor** — every pin empirically validated non-vacuous + honest (no green-but-false; the `[Ignore]`d pin genuinely pending-skipped). 2 minors: `.cs.meta` staged at commit (done); stale line-number citations in the guard messages → FIXED (name-based). Confirmation gate after the fix: 684/0/1-skipped. Status → done. |

### Review Findings

Adversarial single-agent code review (fresh context) of the 8.2 HEADLESS SLICE (`8-2-cr.diff` = the SampleScene de-registration + `BuildSettingsGuardTests.cs`). Proportional to the small, honest slice (the substantive 8.2 content is the Editor/device checklist — docs, not code). **Verdict: clean-with-minor; 0 critical, 2 minor (1 patched, 1 process-confirmed).**

- [x] [Review][Verify] **No green-but-false / tautology (the #1 risk for this re-scoped story).** The reviewer empirically validated: Pin A (SampleScene) flips RED on re-enable (mutation-confirmed); Pin B (every-enabled-path-real) is non-vacuous (a phantom path → RED) and correctly passes now (only Bootstrap enabled, which exists); Pin C (Bootstrap@0) has no off-by-one (Unity build index counts only enabled scenes); the `[Ignore]`d EconomyConfig pin is genuinely pending-skipped (the on-disk Bootstrap has NO `_economyConfig` — the pin would fail if run; it is not faked green). The `_economyConfig` regex correctly distinguishes assigned / fileID-0 / absent. No change — records the honest-for-the-right-reason verdict.
- [x] [Review][Verify] **Scope honesty.** The diff touches exactly 2 files (test + EditorBuildSettings); SampleScene de-registration is the ONLY build-settings change (no phantom scenes registered — Decision C); the checklist completely + correctly captures the 5 not-done items with owners + blocker chains; nothing not-done is claimed done; the LoadPhase AC-4 is an honest confirmation (unchanged). No change.
- [x] [Review][Patch] **Stale line-number citations in the guard's messages/XML doc** (cited `deferred-work.md:347`/`:327`, which drifted when the 8.2 checklist was prepended). FIXED: replaced with name-based references ("the long-standing SampleScene-removal deferral" / "the Story 2.2 Task-3 MonsterDatabase.asset blocker entry") — drift-proof. (minor)
- [x] [Review][Process] **`BuildSettingsGuardTests.cs.meta` must be staged at commit** (else Unity regenerates a new-GUID meta). Confirmed handled by `git add -A` at commit. (minor, packaging)
