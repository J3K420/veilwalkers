---
name: story-automator
description: 'Drive one Veilwalkers story end-to-end through the BMAD cycle (create → validate → dev → code-review → done) with a human gate at every phase boundary. Use when the user says "automate story X.Y", "run the story automator", or "run the full cycle for the next story".'
---

# Story Automator — gated CS → VS → DS → CR runbook

**Goal:** Take one Veilwalkers story from `backlog` to `done` by orchestrating the four real BMAD skills in sequence, with a human checkpoint between each phase and the verification bookkeeping this repo requires.

**What this is NOT:** a replacement for the BMAD skills. This runbook *drives* them — it does not reimplement story creation, dev, or review. The adversarial 3-layer review is delegated to the `story-cr-fanout` Workflow. Each phase runs in **fresh context** (the code-review convention in this repo); this skill is the thin conductor that survives across those fresh contexts via the story file + `sprint-status.yaml`, which are the durable state.

## Hard rules (read first)

1. **Gate at every phase boundary.** After CS, after VS, after DS, after CR — STOP, summarize what happened, and get explicit user approval before starting the next phase. Never chain two phases without a checkpoint. (If the user said "fully autonomous" they will have invoked the autonomous variant; this skill is the gated one.)
2. **Fresh context per phase is preferred.** Recommend the user run each phase via its own skill invocation in a new context window. If running phases back-to-back in one context, explicitly note that the CR phase loses the fresh-eyes property and offer to spawn it isolated.
3. **The story file + `sprint-status.yaml` are the source of truth**, not this conversation. Re-read them at the start of every phase; do not trust in-context memory of their state.
4. **Never trust a green exit code.** The Unity headless gate must be verified by reading `test-results.xml` AND grepping the log for `error CS` (see Phase DS / CR below and the `unity-headless-testing` memory).
5. **Commit and push at the end** (project convention), and only after the temp diff file is deleted.

## Inputs

- **Target story:** an explicit `X.Y` if the user gave one; otherwise the first story whose status is `backlog` in `_bmad-output/implementation-artifacts/sprint-status.yaml`, read top-to-bottom (epics in order). As of this writing that is **Story 1.6 — Configure economy values data-drivenly**.
- All paths below are relative to `{project-root}` = `C:\Users\james\Desktop\veilwalkers`.

## Phase 0 — Orient (no gate; do this silently then report)

1. Read `sprint-status.yaml`; resolve the target story slug + status. If the user named a story, use it; else pick the first `backlog`.
2. Confirm the prior story in the epic is `done` (don't start N+1 while N is open unless the user insists).
3. Read the story's row in `docs/epics.md` (the AC block) and skim the **previous** story's file for seams it deliberately left for this one (e.g. 1.5 left `ProgressionRules` as injected data for 1.6's `EconomyConfig` to construct — that note is in `1-5-…md` Task 2).
4. Read `_bmad-output/implementation-artifacts/deferred-work.md` for items owned by this story.
5. Report: "Target = Story X.Y (status). Prior story = done/not. Seams it inherits: … Deferred items it should settle: …" Then proceed to Phase CS.

## Phase CS — Create Story  → gate

- Invoke the **`bmad-create-story`** skill (menu code CS, action `create`) for the target story. It writes `_bmad-output/implementation-artifacts/<slug>.md` and flips the story to `ready-for-dev` in `sprint-status.yaml`.
- The story file must carry: the ACs (sourced to `docs/epics.md` + `docs/architecture.md`), a Dev Notes section with any **sanctioned-deviation matrix** (placement deviations, injected-data seams), Tasks/Subtasks, and a `baseline_commit` frontmatter slot.
- **GATE:** Show the user the created story's ACs + task list. Ask: proceed to validate, edit the story first, or stop?

## Phase VS — Validate Story  → gate

- Invoke **`bmad-create-story`** with action `validate` (menu code VS) against the story file. It produces a readiness/validation report and may apply fixes (the 1.5 cycle applied 5).
- This phase is optional per BMAD but **recommended** — every prior Epic-1 story ran it and it caught real gaps.
- **GATE:** Summarize the validation verdict + any fixes. Ask: proceed to dev, or address findings first?

## Phase DS — Dev Story  → gate

- **Strongly recommend a fresh context** for this phase. Invoke **`bmad-dev-story`** (menu code DS) on the story file. It implements all tasks/subtasks, keeps the Dev Agent Record (File List, Completion Notes, Change Log) exact as it goes, sets `baseline_commit` to the pre-work commit, and flips status to `review`.
- **Verification gate inside DS (the dev skill owns running tests, but confirm):** the EditMode suite must pass headless. The command (from the `unity-headless-testing` memory):
  ```
  "/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode \
    -projectPath "C:/Users/james/Desktop/Veilwalkers" -runTests -testPlatform EditMode \
    -testResults "<abs>/test-results.xml" -logFile "<abs>/editor-test.log" -nographics
  ```
  Traps: **the Unity Editor must be fully closed** (`Temp/UnityLockfile` present = locked; ask James to close it). **Never trust exit 0** — confirm `test-results.xml` exists with `result="Passed"` AND grep the log for `error CS` (a compile error still exits 0 via the crash handler). Delete `test-results.xml` + `editor-test.log` before committing (not gitignored).
- **GATE:** Report the green suite count (e.g. "86/86, 0 failed, 0 compile errors") and the File List. Ask: proceed to code review?

## Phase CR — Code Review  → gate

Run the adversarial 3-layer review via the Workflow, then triage and patch. This is the part the `story-cr-fanout` Workflow automates.

1. **Write the range diff to a temp file** in `_bmad-output/implementation-artifacts/` — `git diff <baseline_commit>..HEAD` (the baseline is in the story's frontmatter). Diffs run large (1.2's was 84 KB); a file handoff is required, not inline. Call it `<slug>-cr.diff`.
2. **Invoke the Workflow** (only after the user has opted into multi-agent orchestration — the gate question below covers it):
   ```
   Workflow({ scriptPath: ".claude/workflows/story-cr-fanout.workflow.js",
     args: { diffPath: "<abs>/<slug>-cr.diff",
             storyPath: "<abs>/<slug>.md",
             slug: "<slug>",
             archPath: "<abs>/docs/architecture.md",
             epicsPath: "<abs>/docs/epics.md" } })
   ```
   It fans out Blind Hunter / Edge Case Hunter / Acceptance Auditor in parallel, dedups (convergence = strong real-signal), then runs a per-finding verifier (`confirmed` / `spec-sanctioned` / `false-positive` / `out-of-scope`) and returns the triage.
3. **Apply the `patches`** the Workflow confirmed. Re-run the headless gate after patching; if a patch regresses a test, fix it (the 1.5 cycle hit exactly this — a first patch turned a contractual throw into a `Result.Fail` and the suite caught it).
4. **Record findings in BOTH places** (the auditor checks this):
   - The story's `### Review Findings` section: one `- [x] [Review][Patch|Defer] …` line per finding with file + resolution + (for defers) reason + owner story.
   - `deferred-work.md` under a dated heading `## Deferred from: code review of <slug> (YYYY-MM-DD)` for every `defer`/`out-of-scope` item.
5. **Delete the temp diff file** before committing.
6. Flip the story to `done` in `sprint-status.yaml`; if it was the last story in the epic, leave the epic `in-progress` until the user runs the retrospective (don't auto-close epics).
- **GATE (before running the Workflow):** confirm with the user that they want the multi-agent CR fan-out to run (it spawns ~7+ agents). After triage, show the counts (patch / defer / sanctioned / false-positive) and the patches you intend to apply before applying them.

## Done

- Commit on the current branch with a message in the established house style (the 1.5 commit is the template: a one-line summary of what landed + patch count + deferral count + `-> done`). End the message with the Co-Authored-By trailer. Push.
- Report: story → done, suite green at N/N, deferrals logged, pushed.

## Reuse

This runbook is story-agnostic — it works for 1.7, 1.8, 1.9 and every later epic. The only per-story specifics live in the story file and `epics.md`; this skill just sequences the skills and enforces the gates + bookkeeping.
