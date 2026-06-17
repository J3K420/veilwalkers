# Story 7.1: Reserve the Pit seam without building it

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

baseline_commit: ea647bbd20650a772aff5152ae5e2819eff45435

## Story

As a developer,
I want the Pit's place in the architecture reserved and its specs carried forward,
so that the post-MVP signature feature can be added later without retrofitting the MVP.

## Acceptance Criteria

Sourced verbatim-in-intent from `docs/epics.md#Story-7.1` (lines 960–979) + the Epic-7 charter (`docs/epics.md:168–171, 956–958`) + `docs/architecture.md` Pit anchors (the reuse note `:260`; the directory stub `:430`; the "Pit has no asmdef until it has code" rule `:441`; the FR mapping `:508, :627`; the post-MVP subsystem note `:33, :177, :757`) + `AR-5` (`docs/epics.md:62` — "Pit = folder stub, no asmdef"). The FIRST and ONLY Epic-7 story (Post-MVP Seam — The Pit, architecture only, NOT built). It is the half-day, no-asmdef, **zero-gameplay** seam-reservation story the Epic-6 retrospective confirmed ready and independent of the render/device-build defer ([[epic-6-complete]] — "Epic 7 (7.1) confirmed ready as a half-day no-asmdef stub, independent of the render defer").

**This is a DOCUMENTATION + REGRESSION-GUARD story. It implements NO C# gameplay and NO new assembly.** Its entire product is: (1) the carried-forward FR-15/16/17 specs + the "reuse AR session + EncounterService patterns" note, captured as a durable Pit-folder note; and (2) a falsifiable regression guard that the Pit folder stays an empty boundary (no `.asmdef`, no gameplay code) until real post-MVP work begins.

**AC-1 — The Pit seam is a folder stub with NO asmdef (and the rule is regression-guarded)**
**Given** the project structure
**When** the Pit seam is reserved
**Then** `Assets/Veilwalkers/Pit/` exists as a folder stub with **NO `.asmdef`** (an empty boundary is not created until real code exists)
**And** no FR-15/FR-16/FR-17 gameplay is implemented in MVP.
- The folder + its `.gitkeep` already EXIST (created in Story 1.2; `Assets/Veilwalkers/Pit/.gitkeep` carries the terse "NO asmdef until code" note + `Pit.meta` guid). 7.1 does NOT re-create them — it makes the AC's two claims **falsifiable**: a new `Architecture.Tests` guard asserts (a) NO `*.asmdef` exists anywhere under `Assets/Veilwalkers/Pit/`, and (b) NO `*.cs` gameplay file exists there (the "not built" half — the folder stays code-free until the post-MVP feature lands). This is the house discipline of turning an AC sentence into a regression test (the `MonsterDatabase.Count == 67` lore-guard precedent, `MonsterDatabaseLoreCountTests.cs`).
- The existing `AcyclicDependencyTests` enumerates all `Veilwalkers.*` asmdefs on disk and would mechanically pick up a stray Pit asmdef the moment one appeared (it would land in `LoadVeilwalkersAsmdefGraph()` and fail `Every_Veilwalkers_asmdef_on_disk_has_an_allowlist_entry` because Pit is deliberately NOT in the matrix). 7.1 ADDS an EXPLICIT, named guard so the intent ("Pit has no asmdef — on purpose") is self-documenting rather than an incidental side effect of a different test's failure mode. **Decision A** governs whether the guard is a new test file or an added `[Test]` on the existing class.

**AC-2 — MVP AR + Encounter code stays reusable for the Pit; the reuse note is documented (no MVP dependency on the Pit)**
**Given** the MVP AR + Encounter code
**When** it is written
**Then** `ArSessionService` and `EncounterService` patterns are kept reusable so the Pit can later reuse AR session + Encounter patterns (**documented note**), with **no MVP dependency on the Pit**.
- The MVP AR + Encounter code is ALREADY written (Epics 3 + 4, all `done`). 7.1 does NOT modify it. AC-2 has two halves: (a) the **documented note** that the Pit will reuse `ArSessionService` (the sole AR lifecycle owner — Epic 3.3) + `EncounterService` (the encounter state machine + actions — Epic 4.1) patterns; and (b) the **no-MVP-dependency** invariant — nothing in the MVP references the Pit. The architecture already records the reuse intent (`architecture.md:260` "The Pit (post-MVP) will reuse AR session + EncounterService patterns — keep them reusable"; `:757` "The Pit subsystem (reuses AR + Encounter patterns)"). 7.1 CARRIES this forward into the Pit-folder note so a future implementer reads it at the seam, AND pins the no-dependency direction.
- The **no-MVP-dependency** half is naturally true (the Pit has no code, so nothing can depend on it) and is implicitly guarded by the AcyclicDependencyTests (no asmdef references a Pit assembly because none exists). 7.1 records this as a documented invariant; **Decision C** governs whether to add an explicit "no production asmdef references a Pit assembly" assertion (default: NO — it is vacuously true and the no-asmdef guard already covers the failure mode; adding a forward-looking assertion against a non-existent assembly is ceremony).

**AC-3 — The deferred FR-15/16/17 specs remain documented as post-MVP scope**
**Given** the deferred specs
**When** planning continues
**Then** FR-15 (place Pit + Miniaturize captured Monsters, 1–2 Credits each), FR-16 (host/resolve fights, 1 Credit / free with limits, rewards to the winner), and FR-17 (Pit credit sinks: boost a fighter / revive a defeated miniature / unlock arena effects) remain documented as post-MVP scope.
- These specs ALREADY live in `docs/epics.md` (FR-15–17 at `:41–43`; the Epic-7 charter `:168–171`; FR mapping `:130–132`) and `docs/architecture.md` (`:33, :177, :508, :627`). They are NOT being deleted or descoped — AC-3 is satisfied by their continued presence in the planning docs PLUS being carried into the Pit-folder note so the spec travels WITH the seam (a future implementer opening `Assets/Veilwalkers/Pit/` finds the what-goes-here without spelunking the planning docs). 7.1's job is to make the spec **discoverable at the seam**, not to re-author it.
- **Decision B** governs the carried-note format (default: a `README.md` in the Pit folder — richer than the `.gitkeep` comment, the natural home for a multi-paragraph spec + reuse note + the canonical credit costs from the Economy revision). The note must cite its sources (epics.md FR lines + architecture.md anchors) so it stays a POINTER to canon, never a fork of it (a forked spec drifts; a cited pointer does not).

### Derived / load-bearing requirements (not separately numbered but required end-to-end)

- **ZERO gameplay, ZERO assembly, ZERO MVP code change.** No `MonoBehaviour`, no service, no `.asmdef`, no scene, no ScriptableObject, no schema change, no Bootstrap touch, no production-`.cs` anywhere. The ONLY new artifacts are (1) the Pit-folder documentation note (a `README.md` / updated `.gitkeep`) and (2) the regression-guard test(s) in `Veilwalkers.Architecture.Tests`. This is the strictest altitude split in the project — even tighter than the logic-only UI stories ([[ui-presenter-pattern]]), which at least shipped pure-logic C#. 7.1 ships docs + a guard.
- **The carried note is a CITED POINTER to canon, never a fork.** FR-15–17 and the reuse note already live in `docs/`. The Pit note must reference them (epics.md FR lines + architecture.md anchors) and summarize, NOT re-author an authoritative second copy that can silently drift from the planning docs. If the canon later changes, the note's citations lead a reader to the live source. (This mirrors how every story's `### References` cites `[Source: …]` rather than restating.)
- **The guard must be NON-VACUOUS (the [[tautological-test-trap]] applies even here).** A test that asserts "no asmdef under Pit" must actually scan the Pit folder ON DISK (not a hard-coded empty list) so that a future stray `Pit/Veilwalkers.Pit.asmdef` genuinely fails it. Likewise the "no gameplay `.cs`" guard must enumerate real files under the Pit path. Mutation test the guard in dev: a throwaway dummy `.asmdef`/`.cs` dropped under `Pit/` must turn the guard RED (then removed) — proving the assertion bites. (The retro's AI#6 "does this test pass for the right reason?" gate.)
- **The `.gitkeep` stays (the folder must remain git-tracked while empty).** Git does not track empty directories; the `.gitkeep` is what keeps `Pit/` in the repo. If 7.1 adds a `README.md`, the folder is tracked by the README and the `.gitkeep` is technically redundant — **Decision B** covers whether to keep both (default: KEEP the `.gitkeep` OR fold its note into the README and drop it; either is fine as long as the folder stays tracked. Do NOT leave the folder untracked.).

## Tasks / Subtasks

> **Altitude split (the load-bearing decision — the strictest in the project).** 7.1 is **documentation + a regression guard ONLY**. There is NO production assembly, NO gameplay, NO MVP code change. The two deliverables:
> - **The carried Pit-folder note → `Assets/Veilwalkers/Pit/`** (a `README.md`, or an enriched `.gitkeep`; Decision B). Carries FR-15/16/17 (cited to epics.md) + the "reuse `ArSessionService` + `EncounterService` patterns" note (cited to architecture.md:260) + the canonical Miniaturize/fight credit costs (from the Economy revision). A CITED POINTER, never a fork (D-note above).
> - **The no-asmdef / no-gameplay regression guard → `Veilwalkers.Architecture.Tests`** (`Assets/Tests/EditMode/Architecture.Tests/`). Asserts the Pit folder has NO `.asmdef` and NO gameplay `.cs`, by scanning the folder on disk (non-vacuous). Decision A governs new file vs added `[Test]`.
> - **NO other artifact.** No scene, no service, no SO, no schema, no Bootstrap, no asmdef, no matrix edit.

- [x] **Task 1 — Verify the Pit seam's current state is already conformant (AC-1, AC-2, AC-3)** (read-only)
  - [x] Confirm `Assets/Veilwalkers/Pit/` EXISTS with NO `.asmdef` and NO `.cs` (it does — `.gitkeep` + `Pit.meta` only; created Story 1.2). Record the current state in the Dev Agent Record (the recon baseline — so the CR auditor can see 7.1 did not silently change MVP code).
  - [x] Confirm NO MVP production asmdef references a Pit assembly (none exists; vacuously true). Confirm the AcyclicDependencyTests matrix does NOT list a Pit assembly (it must not — Pit is deliberately ungated). Record.
  - [x] Confirm FR-15/16/17 + the reuse note are present in `docs/epics.md` (`:41–43, :130–132, :168–171`) and `docs/architecture.md` (`:33, :177, :260, :508, :627, :757`). Record the exact anchors (they become the carried note's citations).
  - [x] **NO change in this task — it is the verification baseline.** If ANY of the above is already violated (a stray Pit asmdef, a Pit `.cs`, a matrix entry), STOP and surface it (it would mean a prior story leaked Pit code — unexpected).

- [x] **Task 2 — Carry the FR-15/16/17 specs + the reuse note into a durable Pit-folder note (AC-2, AC-3)** `Assets/Veilwalkers/Pit/`
  - [x] Author `Assets/Veilwalkers/Pit/README.md` (Decision B default) — the seam's at-the-folder documentation. It MUST contain:
    - A one-line statement: "**The Pit — POST-MVP signature feature. This folder is a reserved seam: NO `.asmdef`, NO gameplay code until the post-MVP Pit is built.**"
    - **The carried FR specs (cited pointers, summarized not forked):** FR-15 (place The Pit on a detected plane + Miniaturize captured Monsters into fight tokens, **1–2 Credits each**), FR-16 (host + resolve Pit fights between miniaturized Monsters, **1 Credit / free with limits**, rewards to the winner), FR-17 (Pit credit sinks: **boost a fighter's stats / revive a defeated miniature / unlock arena effects**). Cite `[Source: docs/epics.md FR-15–17 (lines 41–43); Epic 7 charter (168–171); FR map (130–132)]`.
    - **The reuse note:** "When built, the Pit reuses the AR session lifecycle (`ArSessionService` — the sole AR lifecycle owner, Epic 3.3) and the Encounter patterns (`EncounterService` + the encounter state machine, Epic 4.1) rather than hand-rolling its own. The MVP keeps these reusable; the Pit places on a detected plane (Epic 3.4 `PlaneAnchorService`) like a lure does." Cite `[Source: docs/architecture.md:260, :757]`.
    - **The no-dependency direction:** "The MVP does NOT depend on the Pit (the dependency points Pit → MVP, never the reverse). The Pit gets its `.asmdef` only when it has code; until then this folder stays an empty boundary." Cite `[Source: docs/epics.md AR-5 (line 62); docs/architecture.md:441]`.
    - **Canon caveat:** "Credit costs here reflect the design-doc Pit costs; the live economy is the 4-action model in `docs/architecture.md` §Economy Model Revision — reconcile Pit costs against the live `EconomyConfig` when the Pit is built." (So a future implementer does not hard-code a stale cost — the [[economy-canon]] discipline.)
  - [x] **Decision B — the `.gitkeep`:** EITHER keep the existing `.gitkeep` alongside the README (belt-and-suspenders; both tracked) OR fold its note into the README and remove the `.gitkeep` (the README now keeps the folder tracked). Default: **KEEP both** (lowest-risk — no file deletion; the `.gitkeep` already carries the terse rule and removing it is a needless diff). Document the choice.
  - [x] **DO NOT** create a fork of the FR specs that can drift — summarize + cite. **DO NOT** add any `.cs` or `.asmdef` to this folder. **DO NOT** touch any MVP code.

- [x] **Task 3 — Add the non-vacuous no-asmdef / no-gameplay regression guard (AC-1, AC-2)** `Veilwalkers.Architecture.Tests`
  - [x] **Decision A — home:** add the guard EITHER as a new test file `Assets/Tests/EditMode/Architecture.Tests/PitSeamReservedTests.cs` OR as new `[Test]` methods on the existing `AcyclicDependencyTests` class. Default: **a new file `PitSeamReservedTests.cs`** (the seam's intent is distinct from the acyclicity matrix — a named file documents "the Pit is a reserved, code-free seam" as a first-class guarded fact, the `MonsterDatabaseLoreCountTests.cs` single-purpose-guard precedent). It lives in the SAME `Veilwalkers.Architecture.Tests` assembly (no asmdef edit — the assembly already references `UnityEditor.TestRunner` for `AssetDatabase` and uses `System.IO` in the sibling `AcyclicDependencyTests`).
  - [x] **Pin 1 — NO asmdef under Pit (AC-1):** scan `Assets/Veilwalkers/Pit/` ON DISK (e.g. `Directory.GetFiles(pitDir, "*.asmdef", SearchOption.AllDirectories)`) and assert the result is EMPTY. **Resolve the folder path via `AssetDatabase.GUIDToAssetPath("10b58bdaa4e04d99beb45fcf38f2b19b")`** (the Pit folder's stable guid from `Pit.meta`) — PREFERRED over a hard-coded `Application.dataPath + "/Veilwalkers/Pit"` string because the guid survives a folder move/rename and is the pattern the sibling `AcyclicDependencyTests` already uses (`AssetDatabase.GUIDToAssetPath`). Fall back to the literal `Assets/Veilwalkers/Pit` path only if the guid lookup returns empty (and FAIL with a clear message in that case — see NFR-3 below). The folder MUST exist (assert it does — a missing Pit folder is itself a regression: the seam was deleted). NON-VACUOUS: a stray `Pit/Anything.asmdef` makes this RED. (The scan reads files off disk, NOT via compilation, so a stray Pit asmdef cannot defeat the guard by compilation order — the test never references the Pit assembly.)
  - [x] **Pin 2 — NO gameplay `.cs` under Pit (AC-1 "not built", AC-2 reusability):** scan `Assets/Veilwalkers/Pit/` for `*.cs` and assert EMPTY (the seam carries docs only until the post-MVP feature lands). NON-VACUOUS: a stray `Pit/PitController.cs` makes this RED. (This is the regression guard for "no FR-15/16/17 gameplay implemented in MVP" — the moment someone starts building the Pit inside MVP scope, this fires and forces a deliberate decision: that work belongs to the post-MVP epic, with its own asmdef + matrix entry.)
  - [x] **Pin 3 — the Pit is NOT in the acyclicity matrix (AC-2 no-dependency, self-documenting):** assert that `AcyclicDependencyTests`'s allowed-edge matrix does NOT contain a `Veilwalkers.Pit` key AND that no on-disk asmdef named `Veilwalkers.Pit*` exists (re-uses the same on-disk scan as `AcyclicDependencyTests.LoadVeilwalkersAsmdefGraph` — Pin 1 already covers the file absence; this pin documents the INTENT "Pit is deliberately ungated, not forgotten"). Decision C governs whether this is worth a separate assertion (default: a light assertion that no `Veilwalkers.Pit` asmdef name appears among the loaded graph keys — cheap, self-documenting, reuses Pin 1's scan).
  - [x] **Mutation-test the guard in dev (the [[tautological-test-trap]] / retro AI#6 gate):** temporarily drop a dummy `Pit/__mutation_probe.asmdef` and a dummy `Pit/__mutation_probe.cs`, run the suite, CONFIRM Pin 1 + Pin 2 go RED, then DELETE the probes and confirm green. Record the mutation-test in the Dev Agent Record (proof the guard bites). **CRITICAL — do NOT let the probes reach the commit:** Unity auto-generates `.meta` files for the probes; after deleting the probe `.cs`/`.asmdef`, also delete their `.meta` files, and run `git status` to CONFIRM nothing under `Assets/Veilwalkers/Pit/` except the README (+ its `.meta`) and the unchanged `.gitkeep`/`Pit.meta` is staged. A committed probe asmdef would CREATE the very Pit assembly the guard forbids (self-defeating). The `__mutation_probe` prefix makes a stray probe obvious in `git status`. This is the difference between a guard that passes because it's correct and one that passes because it asserts nothing.
  - [x] **NFR-3 graceful / robustness:** the scan must handle the folder-exists case cleanly; if the Pit folder is genuinely absent, FAIL with a clear message ("the Pit seam folder is missing — the reserved boundary was deleted"), never throw an opaque `DirectoryNotFoundException`. Filter out `.meta` files (a `Pit.meta` is fine and expected; the scan targets `*.asmdef` / `*.cs`, so `.meta` is naturally excluded — but if the README gets a `README.md.meta`, that is fine too; the scan must not flag docs/meta).

- [x] **Task 4 — asmdef / matrix / Bootstrap: confirm NO change (AC / derived)** (verification)
  - [x] **asmdef:** NO new production asmdef. NO new test asmdef. The guard lands in the EXISTING `Veilwalkers.Architecture.Tests` (already references `UnityEditor.TestRunner` + uses `System.IO` — VERIFIED in `AcyclicDependencyTests.cs`). **NO asmdef edit expected.** Confirm in dev.
  - [x] **matrix:** **DO NOT** add a `Veilwalkers.Pit` row to `AcyclicDependencyTests`'s `AllowedReferences` — the Pit must stay UNGATED (no asmdef ⇒ no graph entry ⇒ no matrix row; adding one would CREATE the ceremony AR-5 forbids). The new guard ASSERTS the absence; it does not register the Pit. Confirm the matrix is untouched.
  - [x] **Bootstrap / GameServices:** NO change (no service to wire — the Pit has no code). Confirm `Bootstrap.cs` is untouched.
  - [x] **schema / SaveModel / EconomyConfig:** NO change (no persisted Pit state, no Pit economy values in MVP). Confirm untouched.
  - [x] If ANY of the above appears to need a change, STOP and reconsider — it would signal scope creep beyond "reserve the seam" (the AC is explicit: NO gameplay, NO asmdef).

- [x] **Task 5 — Tests + headless gate (all ACs)** `Veilwalkers.Architecture.Tests`
  - [x] `PitSeamReservedTests` (Task 3): Pin 1 (no asmdef under Pit), Pin 2 (no gameplay `.cs` under Pit), Pin 3 (Pit absent from the acyclicity graph/matrix), the folder-exists assertion, the graceful-missing-folder message. All scans are on-disk + non-vacuous (mutation-tested in dev).
  - [x] Re-confirm the EXISTING `AcyclicDependencyTests` (4 tests) + `MonsterDatabaseLoreCountTests` + `GameServicesTests` still pass unchanged (7.1 adds NO asmdef and NO type, so they cannot regress — but the headless gate proves it).
  - [x] **Headless gate:** the EditMode suite must pass; current baseline is **670/670** (post-6.6, per the Epic-6 retro). Target green at 670 + the new Pit-seam pins (3–4 new assertions), 0 failed, 0 `error CS`. Editor must be CLOSED; never trust exit 0 — read `test-results.xml` for `result="Passed"` AND grep the log for `error CS`; delete `test-results.xml` + `editor-test.log` before commit ([[unity-headless-testing]]).

## Dev Notes

### Decisions to make (call them explicitly in the Dev Agent Record)

| # | Decision | Default / recommendation | Why |
|---|---|---|---|
| A | The no-asmdef guard's home: a new `PitSeamReservedTests.cs` vs added `[Test]` methods on `AcyclicDependencyTests`. | **Default: a new file `PitSeamReservedTests.cs`** in `Veilwalkers.Architecture.Tests`. | The Pit-seam intent ("a reserved, code-free boundary") is a distinct guarded fact from the acyclicity matrix — a named single-purpose file self-documents it (the `MonsterDatabaseLoreCountTests.cs` single-purpose-guard precedent). Same assembly ⇒ no asmdef edit. |
| B | The carried-note format: a `README.md` vs an enriched `.gitkeep`; and whether to keep the `.gitkeep`. | **Default: a `README.md` + KEEP the existing `.gitkeep`.** The README carries the multi-paragraph FR specs + reuse note + citations; the `.gitkeep` already carries the terse rule and removing it is a needless diff. | The FR-15–17 spec + reuse note + canon caveat is too rich for a one-line `.gitkeep` comment. A `README.md` is the conventional at-the-folder doc. Keeping the `.gitkeep` is zero-risk (no deletion); the folder stays tracked either way. |
| C | Whether to add an explicit "no production asmdef references a Pit assembly" forward-looking assertion (AC-2 no-dependency). | **Default: NO separate assertion** — it is vacuously true (no Pit asmdef exists to reference), and Pin 1 (no Pit asmdef) + the existing AcyclicDependencyTests already cover the failure mode. Pin 3's light "no `Veilwalkers.Pit` in the graph" assertion documents the intent cheaply. | Asserting a non-existent assembly is unreferenced is ceremony against nothing. The real, falsifiable guard is "no Pit asmdef / no Pit `.cs` exists" (Pins 1–2); the no-dependency direction follows from the Pit having no code. Keep the guard minimal + non-vacuous. |
| D | Scope: does 7.1 touch ANY MVP AR/Encounter code to "keep it reusable" (AC-2)? | **Default: NO.** The MVP AR + Encounter code is already written (Epics 3+4 done) and already reusable (`ArSessionService` is the sole AR owner; `EncounterService` is the state-machine owner). AC-2's "keep them reusable" was a constraint ON those epics as they were built — it is already satisfied. 7.1 DOCUMENTS the reuse intent; it does not refactor. | 7.1 is "reserve the seam," not "refactor the MVP." Re-opening Epic-3/4 code to "improve reusability" would be unscoped, risky, and regression-prone for zero AC benefit. If a genuine reusability gap existed it would be a NEW story against Epic 3/4, not smuggled into 7.1. |
| E | Where the carried FR specs source their canonical credit costs (the design-doc Pit costs vs the live economy). | **Default: cite the design-doc Pit costs (1–2 Credits Miniaturize, 1 Credit/free fights) WITH an explicit "reconcile against the live `EconomyConfig` when built" caveat.** | The Pit costs are post-MVP and not in the live `EconomyConfig` (which is the 4-action MVP model). Hard-coding them as settled canon would mislead a future implementer; a cited cost + a reconcile-caveat is honest about their provisional status ([[economy-canon]] — the live economy is the architecture revision, not the design doc). |

### Sanctioned-deviation matrix (the CR noise filter — keep this exhaustive)

| # | Deviation | Why it is sanctioned | Source |
|---|---|---|---|
| D1 | 7.1 ships NO production C#, NO assembly, NO scene, NO ScriptableObject, NO schema/Bootstrap change. The only new artifacts are a Pit-folder `README.md` + a test in `Veilwalkers.Architecture.Tests`. | The AC is explicit: "a folder stub with NO asmdef… no FR-15/16/17 gameplay implemented in MVP." This is the strictest altitude split in the project — a docs + guard story (even tighter than the logic-only UI stories). | epics.md#Story-7.1 (AC-1); AR-5; architecture.md:441; [[ui-presenter-pattern]] |
| D2 | The Pit folder + `.gitkeep` + `Pit.meta` already EXIST (Story 1.2). 7.1 does not re-create them; it ADDS the carried note + the guard. | Story 1.2 created the empty boundary as part of establishing assembly boundaries; 7.1 reserves the SEAM (the carried spec + the regression guard), which 1.2 did not do. No duplication — 7.1 builds on 1.2's stub. | architecture.md:430; Story 1.2; the existing `.gitkeep` |
| D3 | The carried FR-15/16/17 note in the Pit README is a SUMMARY + CITATION of `docs/epics.md` / `docs/architecture.md`, not an authoritative re-authoring. | A forked spec drifts from canon silently; a cited pointer leads a reader to the live source. Every story's `### References` cites `[Source: …]` rather than restating — the same discipline at the folder level. | epics.md:41–43, 130–132, 168–171; architecture.md:33,177,260,508,627,757; the `### References` convention |
| D4 | 7.1 does NOT modify any MVP AR or Encounter code, despite AC-2 naming `ArSessionService` + `EncounterService`. | AC-2's "keep them reusable" was a constraint on Epics 3+4 as they were built (both `done`); it is already satisfied. 7.1 documents the reuse intent; it does not refactor working MVP code (Decision D). | epics.md#Story-7.1 (AC-2); Epics 3+4 (done); Decision D |
| D5 | NO `AcyclicDependencyTests` matrix edit; the Pit gets NO matrix row. | A matrix row implies an asmdef (a graph node); the Pit deliberately has none. Adding a row would CREATE the ceremony AR-5 forbids ("an empty boundary is ceremony, not enforcement"). The new guard asserts the ABSENCE; it does not register the Pit. | AR-5; architecture.md:441–442; AcyclicDependencyTests structure |
| D6 | The new guard scans the Pit folder ON DISK (real `Directory.GetFiles`), not a hard-coded list, and is mutation-tested in dev. | A hard-coded "Pit is empty" assertion is tautological — it would pass even if a real Pit asmdef appeared. The on-disk scan + the dev mutation-probe (drop a dummy `.asmdef`/`.cs`, confirm RED, remove) prove the guard bites (the [[tautological-test-trap]] / retro AI#6 "right reason" gate). | [[tautological-test-trap]]; epic-6 retro AI#6; MonsterDatabaseLoreCountTests precedent |
| D7 | The Pit README cites the design-doc Pit credit costs (1–2 / 1-or-free) WITH a "reconcile against live `EconomyConfig`" caveat, rather than treating them as settled canon. | The Pit costs are post-MVP and absent from the live `EconomyConfig` (the 4-action MVP model). A cited-but-caveated cost is honest about their provisional status without losing the design intent ([[economy-canon]]). | [[economy-canon]]; architecture.md §Economy Model Revision; design-doc Pit costs |

### Seams this story inherits (from deferred-work.md + prior stories)

- **The Story 1.2 Pit stub (`Assets/Veilwalkers/Pit/` + `.gitkeep` + `Pit.meta`, guid `10b58bdaa4e04d99beb45fcf38f2b19b`).** Created when assembly boundaries were established (AR-5: "Pit = folder stub, no asmdef"). 7.1 SETTLES the seam-reservation half that 1.2 left open: the carried FR spec + the regression guard. The `.gitkeep` note ("NO asmdef until code… Keep this folder tracked but empty") is the seed the README expands.
- **The architecture's reuse intent (`architecture.md:260` "The Pit will reuse AR session + EncounterService patterns — keep them reusable"; `:757`).** Already honored by Epics 3+4 (`ArSessionService` sole AR owner; `EncounterService` state-machine owner). 7.1 carries this note to the seam so a future implementer finds it at `Pit/README.md`.
- **The FR-15/16/17 specs (`epics.md:41–43, :130–132, :168–171`; `architecture.md:33, :177, :508, :627`).** Already documented as deferred post-MVP scope. 7.1 makes them discoverable AT the seam (the README) without forking them.
- **NO deferred-work.md item is owned by Epic 7 / the Pit** (verified: the only `pit`/`7.1` grep hit in deferred-work.md is an unrelated Epic-6-owned coaching-banner entry). 7.1 introduces NO new deferral of its own beyond the inherent "the actual Pit feature is post-MVP" (which is the epic's whole premise, already recorded in epics.md, not a new defer).
- **[[epic-6-complete]] note:** the Epic-6 retrospective (2026-06-16) created a NEW **Epic 8 (Render, Scene & Device-Build Pass)** sequenced BEFORE Epic 7 for the MVP-blocking render defer. 7.1 is explicitly independent of that defer ("a half-day no-asmdef stub… cannot be blocked by the render defer") — it is architecture-only and ships regardless of Epic 8's state. (This story is being run ahead of Epic 8 at the user's explicit direction; that sequencing choice is the user's, and 7.1's independence makes it safe.)

### Architecture compliance (must-follow guardrails)

- **AR-5 (Acyclic boundaries / Pit = folder stub, no asmdef) — `epics.md:62`, `architecture.md:441`:** "Pit = folder stub, no asmdef. An empty boundary is ceremony, not enforcement." The new guard ENFORCES this (no asmdef under Pit) without CREATING a boundary (no matrix row). This is the central guardrail.
- **The lore/seam regression-guard precedent (`MonsterDatabaseLoreCountTests.cs` — AR-20 `Count == 67`):** the house pattern of turning an architectural invariant into a falsifiable EditMode test. The Pit no-asmdef/no-gameplay guard is the same shape (a structural invariant pinned as a test).
- **NFR-3 graceful (architecture.md):** the guard handles the folder-exists case cleanly and fails with a clear message if the Pit folder is missing (the boundary was deleted) — never an opaque `DirectoryNotFoundException`.
- **The [[tautological-test-trap]] / retro AI#6 "right reason" gate:** the guard MUST scan on disk + be mutation-tested in dev (drop a dummy `.asmdef`/`.cs`, confirm RED, remove). A guard that asserts an empty hard-coded list is worse than no guard (false confidence).
- **The carried note is a CITED POINTER, never a fork (the `### References` discipline):** the README summarizes + cites epics.md/architecture.md; it does not re-author an authoritative copy that drifts.
- **Economy canon ([[economy-canon]]):** Pit credit costs are post-MVP and NOT in the live `EconomyConfig`; the README cites them WITH a reconcile-caveat, never as settled `EconomyConfig` values.
- **NO MVP code change (Decision D):** 7.1 does not refactor Epic-3/4 code; AC-2's "keep reusable" is already satisfied by those epics.
- **`GameLog` for diagnostics IF any code logs** — but 7.1's only code is a test (NUnit `Assert` messages), so this does not arise. No `Debug.Log`.

### Source tree components to touch

**NEW:**
- `Assets/Veilwalkers/Pit/README.md` (Decision B) — the carried FR-15/16/17 specs (cited) + the `ArSessionService`/`EncounterService` reuse note (cited) + the no-dependency rule + the economy-canon caveat. The seam's at-the-folder documentation.
- `Assets/Tests/EditMode/Architecture.Tests/PitSeamReservedTests.cs` (Decision A) — the non-vacuous regression guard: no `.asmdef` under Pit (AC-1), no gameplay `.cs` under Pit (AC-1/AC-2), Pit absent from the acyclicity graph/matrix (AC-2, self-documenting). On-disk scan, folder-exists assertion, graceful-missing message.
- (`Assets/Tests/EditMode/Architecture.Tests/PitSeamReservedTests.cs.meta` — Unity generates it; commit it.)
- (`Assets/Veilwalkers/Pit/README.md.meta` — Unity generates it; commit it.)

**UPDATE (only if Decision B chooses to fold the `.gitkeep`):**
- `Assets/Veilwalkers/Pit/.gitkeep` — default is KEEP UNCHANGED (Decision B keeps both). Only edit/remove if folding its note into the README (not the default).

**DO NOT TOUCH (sanctioned no-change — the entire MVP):**
- `Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs` — its matrix gets NO Pit row (D5). (The new guard is a SEPARATE file; it does not edit this one.)
- `Assets/Veilwalkers/App/Bootstrap.cs` — no service to wire (no Pit code).
- Any `Veilwalkers.AR` / `Veilwalkers.Encounter` production code — AC-2's "keep reusable" is already satisfied (D4/Decision D); no refactor.
- `SaveModel` / `SaveMigrations` / `EconomyConfig` — no persisted Pit state, no Pit economy values in MVP.
- Any `.asmdef` (production or test) — the guard lands in the existing `Veilwalkers.Architecture.Tests`; no new/edited asmdef.
- `Pit.meta` — keep the existing folder guid.

**NO asmdef edit expected:** the guard uses the existing `Veilwalkers.Architecture.Tests` (already references `UnityEditor.TestRunner` for `AssetDatabase` + uses `System.IO`). **DO NOT touch the `AcyclicDependencyTests` matrix.** If a genuinely-new edge or assembly appears in dev, STOP — it signals scope creep beyond "reserve the seam."

### Testing standards summary

- **Headless EditMode is the hard gate** ([[unity-headless-testing]]): Editor must be closed; never trust exit 0 — read `test-results.xml` for `result="Passed"` AND grep the log for `error CS`. Delete `test-results.xml` + `editor-test.log` before commit.
- **The guard must be NON-VACUOUS + mutation-tested** (the load-bearing test-quality requirement for THIS story): scan the Pit folder on disk; in dev, drop a dummy `Pit/__probe.asmdef` + `Pit/__probe.cs`, confirm the guard goes RED, then DELETE the probes and confirm green. Record the mutation-test in the Dev Agent Record. A guard that can't go red asserts nothing ([[tautological-test-trap]] / retro AI#6).
- **No service fakes, no `GameServices.ResetForTests()`** — the guard is a pure on-disk file scan with no service dependencies.
- **Baseline:** 670/670 (post-6.6). Target: 670 + the new Pit-seam pins, 0 failed, 0 `error CS`.
- **Non-tautological pins (the falsifiable assertions the CR edge-case hunter will check):** the no-asmdef scan reads the real folder (a stray `.asmdef` makes it red — mutation-proven); the no-gameplay scan reads the real folder (a stray `.cs` makes it red — mutation-proven); the folder-exists assertion (a deleted Pit folder makes it red); the README is a cited pointer not a fork (the CR auditor checks the FR specs cite epics.md, not re-author it); NO MVP code changed (the CR auditor diffs the range and confirms only the README + the test + their `.meta` files appear).

### Project Structure Notes

- `Veilwalkers.Architecture.Tests` references `{UnityEngine.TestRunner, UnityEditor.TestRunner, Veilwalkers.Core, Veilwalkers.Monsters}` + `nunit.framework.dll`, `Editor`-only, `UNITY_INCLUDE_TESTS` constraint (VERIFIED: the asmdef). `AssetDatabase` (via `UnityEditor.TestRunner`) + `System.IO.Directory`/`File` are already used by `AcyclicDependencyTests.cs` — the new guard needs NO new reference.
- The Pit folder guid is `10b58bdaa4e04d99beb45fcf38f2b19b` (from `Pit.meta`) — usable for an `AssetDatabase.GUIDToAssetPath`-based folder lookup if a hard-coded relative path is undesirable. A `Application.dataPath + "/Veilwalkers/Pit"` path also resolves the folder for the on-disk scan.
- The single-purpose architecture-guard precedent is `MonsterDatabaseLoreCountTests.cs` (AR-20 `Count == 67`) — a named test file pinning one structural invariant. `PitSeamReservedTests.cs` follows that shape.
- No previous Epic-7 story exists (7.1 is the first + only). The nearest precedents are Story 1.2 (which created the Pit stub) and the Epic-6 retro ([[epic-6-complete]], which confirmed 7.1's readiness + independence from the render defer).

### References

- [Source: docs/epics.md#Story-7.1 (lines 960–979)] — the three AC blocks: folder stub no asmdef + no gameplay (AC-1); MVP AR/Encounter kept reusable + documented reuse note + no MVP dependency (AC-2); FR-15/16/17 remain documented post-MVP scope (AC-3).
- [Source: docs/epics.md:168–171, 956–958] — the Epic-7 charter: "Reserve the seam only… No MVP stories are implemented in this epic."
- [Source: docs/epics.md:41–43] — FR-15 (place Pit + Miniaturize 1–2 Credits), FR-16 (host/resolve fights 1 Credit/free with limits), FR-17 (Pit credit sinks: boost/revive/arena effects).
- [Source: docs/epics.md:62] — AR-5: "Pit = folder stub, no asmdef."
- [Source: docs/epics.md:130–132] — FR map: FR-15/16/17 → Epic 7 (deferred), NOT built in MVP.
- [Source: docs/architecture.md:260, :757] — "The Pit (post-MVP) will reuse AR session + EncounterService patterns — keep them reusable" / "The Pit subsystem (reuses AR + Encounter patterns)."
- [Source: docs/architecture.md:430, :441–442] — the directory stub ("Pit/ — POST-MVP — folder only, NO asmdef until real code exists") + "Pit has no asmdef until it has code (an empty boundary is ceremony, not enforcement)."
- [Source: docs/architecture.md:33, :177, :508, :627] — the Pit as post-MVP seam-only across the architecture (FR mapping + subsystem notes).
- [Source: Assets/Veilwalkers/Pit/.gitkeep] — the existing terse seam note (Story 1.2): "Pit/ — placeable miniature Roman colosseum… Post-MVP seam (Epic 7)… intentionally has NO .asmdef… Keep this folder tracked but empty."
- [Source: Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs] — the on-disk asmdef-graph scan + the allowed-edge matrix (the Pit must NOT appear in either); the `LoadVeilwalkersAsmdefGraph` pattern the guard re-uses.
- [Source: Assets/Tests/EditMode/Architecture.Tests/MonsterDatabaseLoreCountTests.cs] — the single-purpose structural-invariant-as-test precedent (AR-20 `Count == 67`).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Opus 4.8) — story-automator ungated cycle.

### Debug Log References

- Headless EditMode gate (baseline + new Pit tests): **674/674 passed, 0 failed, 0 `error CS`** (`test-results.xml` `result="Passed"`; log greps clean). Baseline was 670 (post-6.6); the 4 new `PitSeamReservedTests` bring it to 674.
- **Mutation test of the guard (the load-bearing non-vacuous proof):** dropped `Pit/__mutation_probe.asmdef` (declaring `Veilwalkers.Pit.__MutationProbe`) + `Pit/__mutation_probe.cs`, re-ran the gate → **5 failures, exactly the right ones:** `Pit_folder_has_no_asmdef` (caught the probe asmdef), `Pit_folder_has_no_gameplay_code` (caught the probe .cs), `No_Pit_assembly_is_registered_in_the_dependency_graph` (caught the probe assembly name), PLUS the existing `AcyclicDependencyTests.Every_Veilwalkers_asmdef_on_disk_has_an_allowlist_entry` + `Every_Veilwalkers_reference_is_on_the_allowed_edge_list` (defense-in-depth — the existing acyclicity guard ALSO catches a stray Pit asmdef, confirming the story's claim). `Pit_folder_exists_as_a_reserved_boundary` correctly stayed GREEN (folder still present). Probes + their generated `.meta` files then deleted; final clean run back to **674/674 green, 0 `error CS`**. The guard bites — it is not tautological ([[tautological-test-trap]] / retro AI#6 gate satisfied).

### Completion Notes List

- **This story shipped NO production C#, NO assembly, NO scene/SO/schema/Bootstrap change** — the strictest altitude split in the project (docs + a guard test). The recon baseline confirmed the Pit folder already existed (`.gitkeep` + `Pit.meta` from Story 1.2) with zero `.cs` and zero `.asmdef`, and the Pit is absent from the `AcyclicDependencyTests` matrix — all as required. 7.1 added the seam-reservation half 1.2 left open.
- **Decision A — RESOLVED: a new file `PitSeamReservedTests.cs`** in the existing `Veilwalkers.Architecture.Tests` assembly (single-purpose guard, the `MonsterDatabaseLoreCountTests` precedent). No asmdef edit (the assembly already references `UnityEditor.TestRunner` + uses `System.IO`).
- **Decision B — RESOLVED: a `README.md` + KEPT the existing `.gitkeep`** (both tracked; zero deletion). The README carries the FR-15/16/17 specs (cited to epics.md), the `ArSessionService`/`EncounterService`/`PlaneAnchorService` reuse note (cited to architecture.md:260,757), the one-way Pit→MVP dependency rule (cited to AR-5/architecture.md:441), the economy-canon caveat, and a "when you build the Pit" un-reservation checklist.
- **Decision C — RESOLVED: a light Pin 3** (`No_Pit_assembly_is_registered_in_the_dependency_graph`) that scans all production asmdefs for a `Veilwalkers.Pit*` name — cheap, self-documenting, and (as the mutation test proved) catches a Pit assembly added ANYWHERE, not just under `Pit/`. No separate "no-reference" assertion (vacuous — there is no Pit assembly to reference).
- **Decision D — RESOLVED: NO MVP code touched.** AC-2's "keep `ArSessionService`/`EncounterService` reusable" was a constraint on Epics 3+4 (both `done`) and is already satisfied; 7.1 documents the reuse intent, it does not refactor. No Epic-3/4 file changed.
- **Decision E — RESOLVED: the README cites the design-doc Pit costs (1–2 / 1-or-free) WITH an explicit "reconcile against the live `EconomyConfig` when built" caveat** — honest about their provisional, post-MVP status ([[economy-canon]]).
- **Path idiom:** the guard resolves the Pit folder via `Path.Combine(Application.dataPath, "Veilwalkers", "Pit")` — the proven idiom from `AR.Tests.CameraPermissionLocationGuardTests.ResolveArSourceDirectory` — rather than the `AssetDatabase.GUIDToAssetPath` + relative→absolute translation the story first proposed (simpler, matches the repo, no null-forgiving operator). The stable folder guid is documented in the resolver's doc-comment as the move-resilient alternative.
- **asmdef / matrix / Bootstrap / schema:** all untouched (Task 4 verified). The `AcyclicDependencyTests` matrix gets NO `Veilwalkers.Pit` row — the Pit stays deliberately ungated (AR-5).

### File List

**NEW:**
- `Assets/Veilwalkers/Pit/README.md` (+ `README.md.meta`, Unity-generated) — the carried FR-15/16/17 specs + reuse note + dependency rule + economy caveat + un-reservation checklist (all cited).
- `Assets/Tests/EditMode/Architecture.Tests/PitSeamReservedTests.cs` (+ `.cs.meta`, Unity-generated) — the 4-pin non-vacuous regression guard (folder exists; no asmdef under Pit; no gameplay .cs under Pit; no `Veilwalkers.Pit*` assembly in the graph).

**MODIFIED:**
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — epic-7 → in-progress; 7.1 → ready-for-dev→(review); dated note.
- `_bmad-output/implementation-artifacts/7-1-reserve-the-pit-seam-without-building-it.md` — this story file (VS fixes + Dev Agent Record).

**UNCHANGED (verified — the entire MVP):** no `.asmdef` (prod or test), no `AcyclicDependencyTests.cs`, no `Bootstrap.cs`, no `Veilwalkers.AR`/`Veilwalkers.Encounter` code, no `SaveModel`/`EconomyConfig`, the existing `Pit/.gitkeep` + `Pit.meta`.

### Change Log

| Date | Change |
|---|---|
| 2026-06-16 | Story created (CS) + validated (VS, 2 fixes: guid-path-resolution clarification + mutation-probe commit safeguard). |
| 2026-06-16 | DS: authored `Pit/README.md` (carried specs + reuse note, cited) + `PitSeamReservedTests.cs` (4-pin on-disk guard). Headless gate 674/674 green (670→674). Mutation-tested the guard (probe → 5 RED including 3 Pit pins + 2 existing acyclicity guards → probes removed → 674/674 green). Status → review. |
| 2026-06-16 | CR: adversarial 3-layer fan-out (16 agents) → 13 raw → 13 deduped → 5 confirmed (3 code patches) + 5 defer + 2 sanctioned + 1 false-positive, 0 AC violations. 3 patches applied to `PitSeamReservedTests.cs` (dot-boundary Pit-name match; robust `ReadAsmdefName` helper guarding empty-path/IO/JSON-parse; `.gitkeep` message wording). Re-ran gate: 674/674 green, 0 `error CS`. 5 defers (all pre-existing `AcyclicDependencyTests` robustness, code 7.1 didn't author) → deferred-work.md. Status → done. |

### Review Findings

Adversarial 3-layer code review (Blind Hunter / Edge Case Hunter / Acceptance Auditor, 16 agents) of `7-1-…-cr.diff` (the README + the guard test). **13 raw → 13 deduped (0 cross-layer convergence) → 5 confirmed / 5 defer / 2 spec-sanctioned / 1 false-positive, 0 AC violations.** The 5 confirmed findings consolidated to **3 code patches** (three of them targeted the same `No_Pit_assembly_is_registered_in_the_dependency_graph` LINQ line). All defers are pre-existing `AcyclicDependencyTests` robustness issues in code 7.1 did NOT author → recorded in `deferred-work.md`.

- [x] [Review][Patch] **Over-broad `StartsWith("Veilwalkers.Pit")`** (PitSeamReservedTests.cs Pin 3) matched unrelated future names (`Veilwalkers.Pitfall`/`Pitch`/`Pitstop`). FIXED: extracted `IsPitAssemblyName` → `name == "Veilwalkers.Pit" || name.StartsWith("Veilwalkers.Pit.", Ordinal)` (exact-or-dot-bounded). (blind-hunter, minor)
- [x] [Review][Patch] **Unguarded `File.ReadAllText` on a GUID-resolved path** (No_Pit_assembly_… LINQ) threw opaque `ArgumentException` on an empty/stale path — violated the story's own NFR-3 graceful mandate. FIXED: the new `ReadAsmdefName` helper guards `string.IsNullOrEmpty`/`File.Exists` before reading (mirrors `AcyclicDependencyTests.ResolveReferenceName`). (blind-hunter, minor)
- [x] [Review][Patch] **Unhandled `FileNotFoundException`/`UnauthorizedAccessException` race** in the same `File.ReadAllText` (file deleted between `FindAssets` and read — a CI race). FIXED: same `ReadAsmdefName` helper wraps the read in try-catch → null on any IO fault, never a test crash. (edge-case-hunter, major) — consolidated with the patch above (same line).
- [x] [Review][Patch] **Unhandled `JsonUtility.FromJson` `ArgumentException` on malformed asmdef JSON** (same line; the verifier noted the "line 265" ref was mislabeled — the in-scope new code is at the No_Pit_assembly_… pipeline). FIXED: `ReadAsmdefName`'s try-catch covers the parse → null. (edge-case-hunter, major) — consolidated (same helper).
- [x] [Review][Patch] **`.gitkeep` referenced in the resolver error message + docstring** could mislead a reader into thinking 7.1 created it (it pre-exists from Story 1.2 per D2). FIXED: reworded the message to "the README + the pre-existing .gitkeep." (blind-hunter, nit)
- [x] [Review][Defer] **`JsonUtility.FromJson` no exception-handling in `AcyclicDependencyTests`** (lines 206) — pre-existing, not authored by 7.1. → deferred-work.md. (edge-case-hunter, major, out-of-scope)
- [x] [Review][Defer] **`File.ReadAllText` race in `AcyclicDependencyTests.LoadVeilwalkersAsmdefGraph`** (line 205) — pre-existing. → deferred-work.md. (edge-case-hunter, major, out-of-scope)
- [x] [Review][Defer] **`File.ReadAllText` on a referenced asmdef in `AcyclicDependencyTests.ResolveReferenceName`** (line 265) — pre-existing TOCTOU. → deferred-work.md. (edge-case-hunter, major, out-of-scope)
- [x] [Review][Defer] **`JsonUtility.FromJson` malformed-JSON crash in `AcyclicDependencyTests`** (the broader deserialization-robustness class across both tests) — pre-existing. → deferred-work.md. (edge-case-hunter, major, out-of-scope)
- [x] [Review][Defer] **Empty `"GUID:"` reference edge case in `AcyclicDependencyTests.ResolveReferenceName`** (line 258) — pre-existing, already handled safely by the `isEmpty` check; a minor input-validation nicety. → deferred-work.md. (edge-case-hunter, minor, out-of-scope)
- [x] [Review][Sanctioned] **README "Dependency direction" documents (not enforces) the Pit→MVP one-way rule.** Spec-sanctioned: AC-2 + Decision C + D5 explicitly scope the no-dependency invariant as DOCUMENTED (vacuously true — no Pit code exists to point anywhere), not guard-enforced. No change. (blind-hunter, nit)
- [x] [Review][Sanctioned] **Fixed-path folder resolution instead of guid-based.** Spec-sanctioned: the Dev Agent Record (+ the resolver doc-comment) explicitly chose the proven `Application.dataPath` idiom and documented that a Pit-folder move SHOULD surface as a regression (intentional, not a defect). No change. (edge-case-hunter, minor)
- [x] [Review][False-positive] **"Two tests redundantly cover an asmdef under Pit/."** Rejected: the overlap is intentional defense-in-depth — `Pit_folder_has_no_asmdef` is the site-specific non-vacuous guard; `No_Pit_assembly_…` is the whole-graph self-documenting companion (catches a Pit assembly added ANYWHERE). The mutation test confirmed both fire. No change. (blind-hunter, nit)
