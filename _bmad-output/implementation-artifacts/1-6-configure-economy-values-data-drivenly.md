---
baseline_commit: 8614f2d
---

# Story 1.6: Configure economy values data-drivenly

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a designer,
I want all economy numbers in a tunable config asset,
So that balancing happens without a code change or rebuild.

## Acceptance Criteria

**AC-1 — `EconomyConfig` ScriptableObject is the single source of every economy number**
**Given** `EconomyConfig` as a ScriptableObject
**When** the economy reads costs/thresholds
**Then** Basic Lure (1), Premium Lure (4), Multi-Lure (5), Slay (3), XP-per-Capture, XP-per-Slay, level/charge thresholds, `adDailyCap`, and `telemetryRetentionDays` are all read from the config
[Source: docs/epics.md#Story 1.6; docs/architecture.md#"Economy numbers … are data-driven in EconomyConfig" (lines 488-491); docs/architecture.md#Complete Project Directory Structure (EconomyConfig.cs, line 396); docs/architecture.md#OQ-9; CLAUDE.md#"Credit economy"]

**AC-2 — No economy number is hard-coded in service logic**
**Given** the Economy services
**When** they need a cost, an XP amount, or a threshold
**Then** no economy number is hard-coded in service logic — every number originates in `EconomyConfig` (or, for unit tests, an injected `ProgressionRules` / a value passed by the caller)
[Source: docs/epics.md#Story 1.6; docs/epics.md#AR-16; docs/architecture.md#Anti-patterns to avoid (line 344-346)]

**AC-3 — Changing a value in the asset changes behavior without recompiling service code**
**Given** a populated `EconomyConfig` asset
**When** a designer edits a value (e.g. raises a level threshold or a per-level-up grant)
**Then** the dependent behavior changes on next run with NO recompile of service code — proven by a test that builds two `EconomyConfig` instances with different values and asserts the derived `ProgressionRules` / exposed numbers differ accordingly
[Source: docs/epics.md#Story 1.6; docs/architecture.md#"tunable without a rebuild" (line 490-491)]

> **AC-3 placement deviation (sanctioned):** the epic says "without recompiling service code." A Unity `.asset` edit is genuinely no-recompile at runtime, but EditMode tests cannot edit a serialized `.asset` mid-run. The data-drivenness is therefore proven the same way 1.5 proved its rules seam: instantiate `EconomyConfig` via `ScriptableObject.CreateInstance<EconomyConfig>()`, set its serialized fields through a test-only setter or `SerializedObject`/reflection, and assert that two differently-valued instances yield different `ProgressionRules` / numbers through the SAME code path that Bootstrap uses. The "no recompile" guarantee is structural (the numbers are `[SerializeField]` data, not `const`/literals in service logic — verified by AC-2), not something a unit test re-proves at the asset-file level. Do NOT add a PlayMode/asset-import test for this; follow the 1.5 precedent (Economy.Tests, `CreateInstance`).

## Tasks / Subtasks

- [x] **Task 1 — Author `EconomyConfig` ScriptableObject (AC: #1, #2)**
  - [x] `Assets/Veilwalkers/Economy/EconomyConfig.cs` — `[CreateAssetMenu(fileName = "EconomyConfig", menuName = "Veilwalkers/Economy Config")] public sealed class EconomyConfig : ScriptableObject`. Namespace `Veilwalkers.Economy`. One public type per file (house rule). Hold ALL economy numbers as `[SerializeField] private int` fields with public read-only accessor properties (SOs serialize private fields; never expose settable properties — an SO holds static data only, never mutated at runtime, architecture line 305):
    - **Action costs:** `BasicLureCost` (provisional 1), `PremiumLureCost` (4), `MultiLureCost` (5), `SlayCost` (3). [Source: CLAUDE.md#Credit economy; docs/epics.md#Story 1.6]
    - **XP amounts:** `XpPerCapture` (provisional, e.g. 10), `XpPerSlay` (provisional, e.g. 25 — MUST be `> XpPerCapture`, the FR-9 "Slay earns more XP than Capture" invariant; validate in `OnValidate`). [Source: docs/epics.md#Story 1.5 AC-1; docs/epics.md#FR-9]
    - **Progression curve:** `LevelXpThresholds` (`[SerializeField] private int[]`, provisional `{100,250,450,700,1000}` carried verbatim from the Bootstrap placeholder), and the three per-level-up grants `StrongCapturePerLevelUp`, `StabilityBoostPerLevelUp`, `NightveilFilterPerLevelUp` (provisional 1 each).
    - **Stub tunables (fields only — logic is later stories):** `AdDailyCap` (provisional, e.g. 3 — consumed by 1.9 AdHook), `TelemetryRetentionDays` (provisional, e.g. 30 — consumed by 1.9 telemetry). Hold the numbers; do NOT add ad/telemetry logic here. [Source: docs/architecture.md#line 554, 574-575; docs/epics.md#AR-15, AR-17]
  - [x] **Mark provisional clearly:** a class-level doc-comment stating every number is OQ-9-provisional pending balancing, the PRD economy table is superseded, and only the four Lure/Slay costs are canon (the rest are placeholders carried from 1.5's Bootstrap literal). [Source: docs/architecture.md#OQ-9 (lines 558-562); _bmad-output/implementation-artifacts/1-5-…md#Dev Notes ("clearly-marked provisional")]
  - [x] `EconomyConfig.cs.meta` is generated by Unity on first asset import — do not hand-author it; the dev session running headless tests will generate it.

- [x] **Task 2 — Expose `ProgressionRules` from `EconomyConfig` (AC: #1, #2, #3)**
  - [x] Add `public ProgressionRules BuildProgressionRules()` to `EconomyConfig` — constructs and returns a `new ProgressionRules(LevelXpThresholds, StrongCapturePerLevelUp, StabilityBoostPerLevelUp, NightveilFilterPerLevelUp)`. This is THE seam 1.5 reserved: `ProgressionRules` stays the constructor-injected immutable plain data; `EconomyConfig` is the tunable SO that produces it. Do NOT make `ProgressionRules` a ScriptableObject and do NOT duplicate the level formula — `ProgressionRules` already owns `LevelForXp`/`GrantPerLevelUp`. [Source: Assets/Veilwalkers/Economy/ProgressionRules.cs (class doc lines 11-17, already names Story 1.6 as owner); _bmad-output/implementation-artifacts/1-5-…md#Dev Notes ("1.6 injects asset-derived rules")]
  - [x] `ProgressionRules`' own ctor validation (non-null/non-empty/positive/strictly-increasing thresholds, non-negative grants) is the validation gate — `BuildProgressionRules` deliberately does NOT re-validate; a bad asset throws at construction with the existing precise messages. An `OnValidate()` in `EconomyConfig` MAY surface the same problems as Editor warnings for designer ergonomics (nice-to-have, not required), but must not be the only guard.

- [x] **Task 3 — Swap the Bootstrap provisional literal for the asset (AC: #1, #2, #3)**
  - [x] `Assets/Veilwalkers/App/Bootstrap.cs` — REPLACE the inline `provisionalProgressionRules` literal (currently `Bootstrap.cs:88-99`) with rules built from a serialized `EconomyConfig` asset. Add `[SerializeField] private EconomyConfig _economyConfig;` to `Bootstrap` and build via `_economyConfig.BuildProgressionRules()`.
  - [x] **Null-asset guard:** add a null check **immediately before** the `_economyConfig.BuildProgressionRules()` call, inside the existing construct block of `WireServices()` (so it sits within the `try` whose catch already treats wiring failure as fatal-at-boot, `Bootstrap.cs:110-116`). If `_economyConfig` is null, `GameLog.Error` + throw — never silently fall back to a hidden literal (that would reintroduce a hard-coded number and violate AR-16). The message must name the missing `EconomyConfig` and state it must be assigned on the Bootstrap component in scene 0.
  - [x] Preserve EVERYTHING else in `WireServices`: construct-before-register ordering, the shared `SaveMutationLock`, the `ProgressionService(saveService, rules, economyMutationLock)` call shape, registration order, `MarkReady`, and the fire-and-forget `InitializeAsync` continuation. This is a single-line-of-reasoning swap (literal → asset-derived), not a wiring restructure. [Source: Assets/Veilwalkers/App/Bootstrap.cs (full file read; the provisional block is lines 88-99 and is self-documenting about this exact swap)]
  - [x] Update the class/inline doc-comment: the "PROVISIONAL progression rules … Story 1.6 REPLACES this literal" comment block must be rewritten to describe the asset-driven wiring now that 1.6 has landed (a stale "Story 1.6 will replace" comment becomes a review finding — same doc-contract rule as 1.4/1.5).

- [x] **Task 4 — Author the EditMode tests (AC: #1, #2, #3)**
  - [x] `Assets/Tests/EditMode/Economy.Tests/EconomyConfigTests.cs` — fixture using `ScriptableObject.CreateInstance<EconomyConfig>()`. Since serialized fields are private, expose ONE test-only seam: an `internal void SetForTests(...)` initializer on `EconomyConfig`, made visible to the test assembly via `[assembly: InternalsVisibleTo("Veilwalkers.Economy.Tests")]`. **Mechanism (verified):** the test asmdef `Veilwalkers.Economy.Tests` already references `Veilwalkers.Economy`, so NO asmdef edit is needed — only the attribute. There is no existing `InternalsVisibleTo` in the codebase; add it as a top-of-file attribute on `EconomyConfig.cs` (`using System.Runtime.CompilerServices;` + `[assembly: InternalsVisibleTo("Veilwalkers.Economy.Tests")]` above the namespace) — this is the first such attribute, so place it cleanly and keep `SetForTests` `internal` (never `public` — see deviation #2). Do NOT use `SerializedObject`/reflection (heavier, and EditMode `CreateInstance` + an internal setter is the lean path). Tests:
    - **AC-1/AC-2:** a populated config returns each cost/XP/threshold/tunable via its accessor; `BuildProgressionRules()` yields a `ProgressionRules` whose `LevelForXp`/`GrantPerLevelUp` match the configured curve.
    - **AC-3 (data-drivenness):** build TWO configs with different thresholds (and different per-level-up grants) and assert their `BuildProgressionRules()` outputs differ accordingly through the same path — proving behavior follows the data, not a literal.
    - **Invariant:** `XpPerSlay > XpPerCapture` holds for the canonical provisional values (pin the FR-9 invariant so a future edit that breaks it is caught).
    - **Bad-asset:** a config with a non-increasing threshold array makes `BuildProgressionRules()` throw `ArgumentException` (delegates to the existing `ProgressionRules` validation — assert the throw, do not re-test every ProgressionRules guard, those are already covered in `ProgressionServiceTests`/1.5).
  - [x] Do NOT add a Bootstrap PlayMode test for the asset assignment — Bootstrap runtime lifecycle verification is deferred (it's in no scene yet; see 1.2 deferral, owner Story 1.3/6.3). The null-asset guard is covered structurally by the EditMode logic + the fatal-at-boot path; a scene-level test belongs to Epic 6 scene flow.

- [x] **Task 5 — Author the asset + verify headless (AC: #1, #3)**
  - [x] Create the actual asset at **`Assets/Veilwalkers/ScriptableObjects/EconomyConfig.asset`** (decided: the SO *script* lives in `Economy/` per the architecture tree line 396; the *asset instance* lives under `Assets/Veilwalkers/ScriptableObjects/` per line 431 — create that folder if absent). Author it via the `CreateAssetMenu` entry, populated with the provisional values from Task 1. Wire it into the scene-0 Bootstrap GameObject's serialized `_economyConfig` field. Commit the `.asset` + `.meta` (static design data, not gitignored player state). The folder's `.meta` is also committed.
  - [x] Run the EditMode suite headless and confirm GREEN ([[unity-headless-testing]]): Editor fully closed, read `test-results.xml` `result="Passed"` AND grep `editor-test.log` for `error CS`, never trust exit 0. Delete `test-results.xml`/`editor-test.log` before committing.

## Dev Notes

### Current state of files this story touches (read in full during story creation)

- **`Assets/Veilwalkers/App/Bootstrap.cs` (UPDATE):** Today constructs an inline `provisionalProgressionRules = new ProgressionRules({100,250,450,700,1000}, 1,1,1)` at lines 88-99 and injects it into `ProgressionService`. The block is self-documenting: its comment literally says "Story 1.6 (EconomyConfig) REPLACES this literal with values read from the tuned EconomyConfig asset." Construct-before-register and the shared `SaveMutationLock` discipline must be preserved; wiring failure is already fatal-at-boot (lines 110-116). This story's ONLY behavioral change to Bootstrap is the literal→asset swap + a serialized `_economyConfig` field + a null-asset guard.
- **`Assets/Veilwalkers/Economy/ProgressionRules.cs` (READ, do not modify):** Already an immutable validated plain class with `LevelForXp` and `GrantPerLevelUp`. Its class doc (lines 11-17) already names Story 1.6 as the owner of the SO that constructs it. `EconomyConfig.BuildProgressionRules()` calls its existing ctor — no change to this file.
- **`Assets/Veilwalkers/Economy/` (area):** existing types — `ChargeType`, `ChargeInventory`, `SaveMutationLock`, `ICreditService`/`CreditService` (cost is a caller-passed parameter, `TrySpendCreditsAsync(int cost)` — NO hard-coded Lure/Slay constants exist to remove), `IProgressionService`/`ProgressionService`. `EconomyConfig.cs` is the only NEW production file.
- **`Assets/Tests/EditMode/Economy.Tests/` (area):** `FakeProgressStore`, `CreditServiceTests`, `CreditPipelineTests`, `ChargePipelineTests`, `ProgressionServiceTests`. New file `EconomyConfigTests.cs` joins them; reuse the fixture conventions.

### Scope boundaries

- **In scope:** the `EconomyConfig` SO (all numbers + `BuildProgressionRules`), the Bootstrap swap, the EditMode tests, the populated `.asset`.
- **Explicitly NOT in scope:**
  - *Consuming* the costs/XP at spend sites — Epic 4 (Lure/Slay/Capture) reads `EconomyConfig` costs and XP amounts when those actions exist; this story does not build encounter actions. `CreditService` keeps taking `cost` as a parameter; the *wiring* of "cost comes from `EconomyConfig`" lands where the caller is built (Epic 4). This story proves the asset is the source and is injected into the one existing consumer (`ProgressionService`, via rules).
  - *Ad-reward / telemetry logic* — Story 1.9 owns `AdHook` (`adDailyCap` consumption + day-bucket) and the telemetry sink (`telemetryRetentionDays`). 1.6 only adds the **fields** to the config asset.
  - *Daily-reward amount field* — FR-2's daily-credit amount is a Story 1.8 number; add it to `EconomyConfig` only if 1.8 hasn't; the epic's AC-1 list for 1.6 does NOT name it, so leave it to 1.8 to add its own field (avoid speculative fields).
  - *Threshold-rebalance migration* — 1.5's review already added a de-level guard (stored `Level` never decreases below the XP-derived level on a curve change). No migration is needed here; a designer raising a threshold simply means future XP levels more slowly. Stored `Level` stays authoritative as-earned. [Source: _bmad-output/implementation-artifacts/1-5-…md#Review Findings (de-level guard)]

### Sanctioned-deviation matrix (check before classifying any review finding)

1. **Test proves data-drivenness via `CreateInstance` + differing instances, NOT an asset-file edit** (AC-3 deviation above). A reviewer flagging "no test edits the real .asset" is WRONG — that's the sanctioned approach, identical to 1.5's rules-injection tests.
2. **`EconomyConfig` exposes private `[SerializeField]` numbers through read-only accessors + one `internal SetForTests`** (via `InternalsVisibleTo`). Settable public properties would be the violation (SO = static data, never runtime-mutated, architecture line 305). The test seam is `internal`, not public.
3. **`AdDailyCap` / `TelemetryRetentionDays` are fields-only here.** A reviewer expecting ad/telemetry *behavior* in 1.6 is out of scope — that's 1.9. Holding the number is the whole job (architecture line 554, 574-575).
4. **`BuildProgressionRules` does not re-validate.** Validation lives in the `ProgressionRules` ctor (single source). Re-validating in the SO would duplicate the guard. An `OnValidate` Editor warning is additive, not a second gate.
5. **Bootstrap null-asset guard throws (fatal-at-boot), never falls back to a literal.** A silent literal fallback would reintroduce a hard-coded number (AR-16 violation). The throw is correct.
6. **No PlayMode/scene test for Bootstrap's `_economyConfig` assignment.** Bootstrap runtime lifecycle is unverified-by-design until it's in a scene with flow (1.2 deferral, owner 1.3/6.3); don't flag the absence here.

### Testing standards

- EditMode NUnit suite under `Assets/Tests/EditMode/Economy.Tests/`, run headless per [[unity-headless-testing]] (Editor closed; verify `test-results.xml` + grep `error CS`; never trust exit 0). Expected/handled failure states log `GameLog.Warn` not `Error` (UTF fails any test observing a `LogError`, even from async continuations). No new asmdef — `EconomyConfigTests.cs` joins the existing `Veilwalkers.Economy.Tests` assembly.

### Project Structure Notes

- `EconomyConfig.cs` → `Assets/Veilwalkers/Economy/` (architecture tree line 396). The `.asset` instance → `Assets/Veilwalkers/ScriptableObjects/EconomyConfig.asset` (architecture line 431; create the folder if absent). Both the `.cs`/`.asset` and their `.meta`s are tracked (static design data, not player state). No asmdef changes (the test asmdef already references `Veilwalkers.Economy`); no new allowed dependency edges, so no `Architecture.Tests` matrix edit — that guard is unchanged.

### References

- [Source: docs/epics.md#Story 1.6] — the AC list (costs/XP/thresholds/adDailyCap/telemetryRetentionDays all from config; nothing hard-coded; tunable without recompile).
- [Source: docs/epics.md#AR-16] — data-driven economy; numbers in `EconomyConfig`, tunable without rebuild.
- [Source: docs/architecture.md lines 488-491, 396, 305, 554, 574-575] — EconomyConfig contract, SO directory slot, "SOs hold static data only", adDailyCap/telemetryRetentionDays placement (never counters).
- [Source: docs/architecture.md#OQ-9 (lines 558-577)] — economy values provisional; EconomyConfig is the tunable surface.
- [Source: Assets/Veilwalkers/App/Bootstrap.cs:88-99] — the provisional literal this story replaces.
- [Source: Assets/Veilwalkers/Economy/ProgressionRules.cs:11-17] — the reserved seam; class doc names 1.6 as owner.
- [Source: _bmad-output/implementation-artifacts/1-5-…md#Dev Notes & Review Findings] — the injection-seam rationale and the de-level guard that makes a rebalance safe.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Code dev-story, via story-automator Phase DS)

### Debug Log References

- Headless EditMode gate (Editor closed, no lockfile): `result="Passed"`, total 92, passed 92, failed 0, 0 `error CS`. Suite grew 86 → 92 (+6, the new `EconomyConfigTests`). Ran twice — second run imported the hand-authored `.asset` with zero import errors and stayed green. `test-results.xml`/`editor-test.log` deleted before handoff.

### Completion Notes List

- **Tasks 1+2 — `EconomyConfig` SO (`Assets/Veilwalkers/Economy/EconomyConfig.cs`):** all economy numbers as private `[SerializeField]` + read-only accessors (4 canon costs, XP-per-Capture/Slay, the level threshold curve, 3 per-level-up grants, `AdDailyCap` + `TelemetryRetentionDays` stub tunables). `BuildProgressionRules()` is the seam 1.5 reserved — constructs the immutable `ProgressionRules` from the configured curve, delegating validation to that ctor (no re-validation). Added an `internal SetForTests(...)` seam behind `[assembly: InternalsVisibleTo("Veilwalkers.Economy.Tests")]` (first such attribute in the codebase) and an Editor-only `OnValidate()` warning for the FR-9 XP ordering + empty-curve (ergonomics aid, not a guard).
- **Task 3 — Bootstrap swap (`Assets/Veilwalkers/App/Bootstrap.cs`):** replaced the provisional `ProgressionRules` literal (old lines 88-99) with `_economyConfig.BuildProgressionRules()`; added `[SerializeField] private EconomyConfig _economyConfig` + a fatal null-asset guard immediately before the build call (inside the existing fatal-at-boot `try`). Construct-before-register ordering, the shared `SaveMutationLock`, the `ProgressionService` call shape, registration order, `MarkReady`, and the fire-and-forget `InitializeAsync` continuation are all preserved. The stale "Story 1.6 REPLACES this literal" comment is rewritten to describe the asset-driven wiring.
- **Task 4 — Tests (`Assets/Tests/EditMode/Economy.Tests/EconomyConfigTests.cs`):** 6 tests via `ScriptableObject.CreateInstance` + `SetForTests` — accessor round-trip (AC-1/AC-2), built-rules match the curve, **two differing configs → differing rules (AC-3 data-drivenness)**, FR-9 XP-ordering invariant, canon-cost pin, and a bad-curve throw delegating to `ProgressionRules` validation.
- **Task 5 — Asset (`Assets/Veilwalkers/ScriptableObjects/EconomyConfig.asset`):** authored the populated asset (canon costs + provisional values), referencing the script GUID Unity generated. Imported cleanly (Unity generated `.asset.meta` + folder `.meta`).
- **Caveat (sanctioned, not a gap):** the asset is NOT yet wired into a Bootstrap GameObject's `_economyConfig` field, because **Bootstrap is in no scene yet** (1.2 deferral — runtime scene wiring is Story 1.3/6.3). The null-asset guard covers the unassigned state until Bootstrap enters a scene. No scene/PlayMode test added (deviation #6).

### File List

- `Assets/Veilwalkers/Economy/EconomyConfig.cs` (NEW) + `.meta`
- `Assets/Veilwalkers/App/Bootstrap.cs` (MODIFIED — provisional literal → asset-read + serialized field + null guard)
- `Assets/Tests/EditMode/Economy.Tests/EconomyConfigTests.cs` (NEW) + `.meta`
- `Assets/Veilwalkers/ScriptableObjects/EconomyConfig.asset` (NEW) + `.meta`
- `Assets/Veilwalkers/ScriptableObjects.meta` (NEW — folder)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (MODIFIED — status transitions)

### Change Log

- 2026-06-12 — Story 1.6 implemented: `EconomyConfig` ScriptableObject as the single data-driven source of every economy number, `BuildProgressionRules()` seam, Bootstrap literal→asset swap + null guard, populated `.asset`, 6 EditMode tests (92/92 green).
- 2026-06-13 — Code review: 1 doc-contract patch applied (OnValidate doc-comment), 2 deferrals logged. Suite re-verified green (92/92).

### Review Findings

Code review 2026-06-13 (3 adversarial layers: Blind Hunter, Edge Case Hunter, Acceptance Auditor; 34 raw findings → per-finding verify-disputed-first triage). Acceptance Auditor: all 3 ACs satisfied, no AC violations (the costs-consumed-at-spend-sites concern is the sanctioned Epic-4 scope boundary). 24 findings were spec-sanctioned against the Dev Notes deviation matrix; 7 were verified false-positives (e.g. "no `.meta` in diff" — they exist untracked with matching GUID; "negative cost → infinite credits" — `CreditService` guards `cost <= 0`; "no defensive copy" — `ProgressionRules` ctor copies the array). One real defect found and patched:

- [x] [Review][Patch] `EconomyConfig.OnValidate` doc-comment overstated its coverage [Assets/Veilwalkers/Economy/EconomyConfig.cs] — RESOLVED. The doc-comment claimed `OnValidate` surfaces "the same problems the `ProgressionRules` constructor enforces, plus FR-9," but the body only warns on FR-9 XP ordering + null/empty thresholds — NOT non-positive, non-strictly-increasing, or negative-grant cases (a designer could author `{100,100}`, pass `OnValidate` silently, then throw at boot via `BuildProgressionRules`). Doc-contradicts-code, the 1.4/1.5-class doc-contract finding. **Resolution:** rewrote the doc to state exactly the two cases `OnValidate` warns on and that the `ProgressionRules` ctor is the authoritative single gate for the rest (deviation #4: a partial ergonomic aid, never a second validation source — duplicating those checks would diverge). Comment-only change; headless suite re-verified GREEN 92/92.

- [x] [Review][Defer] No overflow/upper-bound check on `_levelXpThresholds` at asset-edit time [Assets/Veilwalkers/Economy/EconomyConfig.cs] — deferred, out-of-scope. A threshold near `int.MaxValue` is caught at runtime by the `checked` arithmetic in `ProgressionService.AddXpAsync` (logged, not silent), per the documented contract. Asset-edit-time overflow oversight belongs to a future OQ-9 balancing / economy-oversight story, not the data-driven plumbing of 1.6. Logged to `deferred-work.md`.

- [x] [Review][Defer] No runtime-immutability enforcement on `_levelXpThresholds` (reflection-mutation theoretical) [Assets/Veilwalkers/Economy/EconomyConfig.cs] — deferred, out-of-scope. `ProgressionRules` ctor makes a defensive copy, so a post-build mutation of the source array cannot affect injected rules; the "static data only" contract is a design principle, not a test-checkable runtime guard. An `ImmutableList`/`Array.AsReadOnly`-on-the-field hardening pass is a separate concern. Logged to `deferred-work.md`.

**Final gate (2026-06-13):** Headless EditMode suite GREEN — `result="Passed"`, 92/92 (0 failed, 0 compile errors), stable across the post-patch re-run. Story → `done`; sprint-status synced.
