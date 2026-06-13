---
baseline_commit: "9cb4ac6af8444e510564d88741fb8091d66a83c1"
---

# Story 2.2: Register the 67-Monster database with MVP content

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want a `MonsterDatabase` registry sized to the 67-Monster lore constraint with 3–5 populated for MVP,
so that the Codex has a complete catalog shape and the Veil Pack is valid.

## Acceptance Criteria

**AC-1 — `MonsterDatabase` SO registry accounts for the full 67-Monster universe with 3–5 populated** [Source: docs/epics.md#Story-2.2 (405-408); docs/architecture.md#Source-tree (401); AR-3 epics.md:60]
**Given** `MonsterDatabase` as an SO registry
**When** it is populated
**Then** it accounts for the full 67-Monster universe and 3–5 Monsters are fully populated for MVP.

**AC-2 — `Architecture.Tests` asserts the lore count is exactly 67** [Source: docs/epics.md#Story-2.2 (408); AR-20 epics.md:77; docs/architecture.md (435, 636)]
**Given** `MonsterDatabase`
**When** the architecture suite runs
**Then** an `Architecture.Tests` assertion verifies the catalog/lore count is exactly 67 (the lore constraint).

**AC-3 — at least one populated Monster is `Rarity >= Rare` (Veil Pack validity)** [Source: docs/epics.md#Story-2.2 (410-412); docs/architecture.md#Minimum-rarity-contract (692-695)]
**Given** the MVP populated subset
**When** content is validated
**Then** at least one populated Monster has `Rarity >= RarityThresholds.GuaranteedRareFloor` (content acceptance criterion: the Guaranteed-Rare Lure / Veil Pack must have a valid spawn).

## Tasks / Subtasks

- [x] **Task 1 — Author `MonsterDatabase` SO registry** (AC: 1) — DONE. `UniverseCount=67` constant, populated `_monsters[]`, lazy `EnsureIndex`, `TryGet`/`GetById` (ID-keyed), `SetForTests`, static `IsValidMonsterId` helper.
  - [ ] Create `Assets/Veilwalkers/Monsters/MonsterDatabase.cs`, namespace `Veilwalkers.Monsters`, `public sealed class MonsterDatabase : ScriptableObject`, `[CreateAssetMenu(...)]`.
  - [ ] **Registry model (decide explicitly, document in XML-doc):** the DB declares the full 67-Monster universe but only 3–5 are *populated* for MVP. Recommended shape: a private `[SerializeField] MonsterDefinition[] _monsters` holding the **populated** entries, plus a `public const int UniverseCount = 67;` naming the lore constant. `Count` returns `UniverseCount` (the universe size — what AC-2 asserts), and a separate `PopulatedCount`/`Populated` exposes the authored subset. **Do NOT author 67 stub `.asset` files** — the universe size is a declared constant, not 67 physical assets (only 3–5 real assets exist in MVP).
  - [ ] Private `[SerializeField]` + public read-only accessors (the SO static-data contract, 2.1/1.6 precedent). Provide lookup by ID: `bool TryGet(string id, out MonsterDefinition)` / `MonsterDefinition GetById(string id)` keyed on the stable `monNN` ID (never array index). Build an internal `Dictionary<string,MonsterDefinition>` **lazily via a private `EnsureIndex()`** invoked by `TryGet`/`GetById` (NOT only in `OnEnable`). **Why lazy, not OnEnable-only:** `OnEnable` fires at `CreateInstance` time — *before* `SetForTests` populates the array — so an `OnEnable`-built index would be empty in tests and break `Registry_resolves_by_id`. `SetForTests` must null the cached index so the next access rebuilds it. (`EnsureIndex` also covers the disk-loaded path, where the serialized `_monsters` is already present.) The serialized form stays the array; the dictionary is a runtime read cache, not persisted.
  - [ ] `internal SetForTests(MonsterDefinition[] populated)` seam + `[assembly: InternalsVisibleTo("Veilwalkers.Monsters.Tests")]` so EditMode tests build a registry from `CreateInstance`'d definitions without a serialized `.asset`.
  - [ ] XML-doc: "SO registry of the 67-Monster universe (lore constant). Static data only; never player state. MVP populates 3–5; the rest of the 67 are reserved IDs filled in later content passes."

- [x] **Task 2 — Content validation layer** (AC: 1, 3) — DONE. `Validate()` returns problems (no throw): null-entry, bad/duplicate ID, null DisplayName/Lore/Art, out-of-range Rarity (`Enum.IsDefined`). Difficulty bounds deliberately excluded (Story 4 / OQ-9).
  - [ ] Add a validation method on `MonsterDatabase` (e.g. `IReadOnlyList<string> Validate()` returning a list of problems, empty = valid) that checks the **populated** entries for the content invariants 2.1 deferred here:
    - a **null entry** in the `_monsters` array is itself a reported problem — skip-and-flag, NEVER dereference (else `Validate()` NREs on an unassigned Inspector slot, ironically reintroducing the crash class this layer exists to catch);
    - each populated `MonsterDefinition` has a non-null/non-empty `Id` matching `^mon\d{2}$` **within mon01..mon67** (this is the AC-4 content audit 2.1 deferred — now there IS production content to audit);
    - non-null/non-empty `DisplayName` (2.1 defer);
    - non-null/non-empty `Lore` (2.1 defer);
    - non-null `Art` (2.1 defer — authoring requires art);
    - `Rarity` is in-range `[Common..Nightmare]` i.e. `Enum.IsDefined` (2.1 defer — reject/flag a deserialized out-of-range `(Rarity)99`);
    - no duplicate IDs across populated entries.
  - [ ] Decide the production contract: `Validate()` **returns problems** (does not throw) so callers choose policy; the `Architecture.Tests`/content test asserts it returns empty for the real asset. (A null/garbage authored asset thus surfaces as a failing content test, not a runtime crash — the deferred "null crashes downstream" hazards are caught at the registry boundary, which is where 2.1 routed them.)
  - [ ] Do **not** add bounds validation on `CaptureDifficulty`/`SlayDifficulty` — those 2.1 deferrals are owned by **Story 4 / OQ-9**, not 2.2. Stay in scope.

- [~] **Task 3 — Author the 3–5 MVP Monster `.asset` files + the `MonsterDatabase` asset** (AC: 1, 3) — *DEFERRED to an Editor-authoring session.* Hand-authoring `.asset` YAML headlessly is feasible for scalar fields, but `Art` is a `Sprite` reference requiring an imported texture asset; without one `Art` is null, which `Validate()` (correctly) rejects — so real MVP content needs the Editor (real art + binary assets). Per the story's `[~]` rule: this blocks NO epic AC (all 3 verified via `SetForTests`); it leaves the AC-4 on-disk filename audit deferred (re-routed to `deferred-work.md`) and the old 2.1 tautological test is NOT deleted (Task 5 sequencing guard). Owner: an Editor-authoring session (like 1.1's `.aab` release-gate handoff).
  - [ ] Create 3–5 `MonsterDefinition` `.asset` files under `Assets/Veilwalkers/ScriptableObjects/` (the architecture asset home, mirrors 1.6's `EconomyConfig` asset placement), named `<Id>_<Name>.asset` (e.g. `Mon01_AntleredShade.asset`) per the naming canon — IDs `mon01..` , real `DisplayName`/`Lore`/`Art`/stats.
  - [ ] **At least one populated Monster MUST be `Rarity >= Rare`** (AC-3 — Veil Pack validity). Spread the rest across tiers for a believable Codex sample.
  - [ ] Create the `MonsterDatabase` `.asset` and assign the authored definitions into `_monsters`.
  - [ ] Commit every `.asset` + `.meta` (static design data; the 1.2 orphaned-meta lesson). **Art:** if real art sprites aren't ready, a placeholder `Sprite` is acceptable for MVP but `Art` must be non-null (Task 2 validation requires it) — note the placeholder in the asset and as a follow-up.
  - [ ] **Headless caveat (read carefully — honest handoff rule):** authoring `.asset` files is an Editor operation; if the automated dev pass cannot create binary `.asset` files reliably, author them via a small `[MenuItem]`/editor script OR hand-author the YAML `.asset` text (Unity SOs serialize to readable YAML — `m_Script` GUID must match `MonsterDefinition.cs.meta`'s GUID). If neither is feasible in this pass, mark this task `[~]` and flag for an Editor-authoring session (the way 1.1's `.aab` build was a release-gate handoff).
  - [ ] **What `[~]` on Task 3 does and does NOT cost (state this in the Dev Agent Record if it happens):** all **3 epic ACs are fully verifiable WITHOUT physical assets** via `SetForTests`-built registries (AC-1 shape/count, AC-2 `Count==67`, AC-3 ≥1 Rare+). So `[~]` does NOT block any AC. The ONLY thing physical assets unlock is the on-disk AC-4 filename audit (Task 4) — which is a *2.1-deferral settlement*, not a 2.2 AC. If Task 3 is `[~]`: (1) the AC-4 asset-name deferral stays OPEN — re-route it to `deferred-work.md`, don't silently drop it; (2) do NOT delete the old tautological test (Task 5). Be explicit at the DS gate about which of the two outcomes happened.

- [x] **Task 4 — `Count == 67` architecture assertion** (AC: 2) — DONE. Added `Veilwalkers.Monsters` to `Architecture.Tests` asmdef refs; `MonsterDatabaseLoreCountTests` asserts `UniverseCount==67` + the in-universe ID-range logic via **public API only** (the populated-subset `[3..5]` relationships moved to `MonsterDatabaseTests` in `Monsters.Tests`, which has internals access — see Debug Log). On-disk asset audit deferred with Task 3.
  - [ ] Add `Veilwalkers.Monsters` to `Assets/Tests/EditMode/Architecture.Tests/Veilwalkers.Architecture.Tests.asmdef` `references` (currently Core only). **This is a legal forward edge** — `Architecture.Tests` is a top-tier Editor test assembly; it is auto-excluded from the acyclic allowlist guard by the `.Tests` suffix, so no `AcyclicDependencyTests` matrix edit is needed (verify, don't assume). Editing the asmdef JSON body does not change its `.meta` GUID.
  - [ ] **Make the `Count==67` assertion non-tautological.** `MonsterDatabase.UniverseCount == 67` alone is a literal-equals-literal check (`return 67;` passes it) — exactly the tautology class 2.1's CR flagged. Instead assert the **model relationships that have teeth**, on a `SetForTests`-built registry: (a) `UniverseCount == 67` (the lore constant — AR-20), AND (b) `PopulatedCount` is in `[3..5]`, AND (c) `PopulatedCount <= UniverseCount`, AND (d) every populated ID parses to an `NN` within `01..67`. Together these make "67 universe with 3–5 populated" falsifiable rather than a constant assertion.
  - [ ] If the real `MonsterDatabase.asset` exists (Task 3 done), additionally load it via `AssetDatabase` and assert its declared `UniverseCount == 67`, `Validate()` returns empty, and the on-disk `.asset` filenames match `<Id>_<Name>` for each populated entry — the falsifiable AC-4 production audit that supersedes (and lets you delete, see Task 5) 2.1's hand-recomputed tautological test. **If Task 3 is `[~]` (no physical assets), this on-disk audit is NOT run, the AC-4 asset-name deferral stays OPEN (re-route it back to deferred-work.md), and the old tautological test must NOT be deleted yet.**

- [x] **Task 5 — Tests in `Monsters.Tests`** (AC: 1, 3) — DONE. `MonsterDatabaseTests`: resolves-by-id, lookup-by-id-not-index, validate-passes, validate-flags-missing-content (all 6 guards), validate-flags-null-entry, ≥1-Rare+. NOTE: the old tautological `Asset_file_name_convention_is_Id_underscore_Name` was **NOT deleted** — Task 3 is `[~]` so the disk-scan replacement doesn't exist yet (sequencing guard honored).
  - [ ] `Registry_resolves_by_id`: build via `SetForTests`, assert `TryGet("mon01", out def)` returns the right definition and an unknown ID returns false/null (never throws, never returns wrong entry).
  - [ ] `Lookup_uses_id_not_index`: pin that lookup is by `monNN` ID, not array position (insert out of order, fetch by ID).
  - [ ] `Validate_flags_missing_content`: build a registry with a deliberately bad entry (null DisplayName / null Lore / null Art / out-of-range Rarity / duplicate ID / bad ID format) and assert `Validate()` reports each problem. This is the falsifiable settlement of 2.1's null-guard deferrals — it must go red if a guard is removed.
  - [ ] `Validate_passes_for_well_formed_registry`: a fully-populated valid set (incl. ≥1 `Rarity >= Rare`) returns empty problems.
  - [ ] `At_least_one_populated_is_rare_or_better`: AC-3 — assert the validated set contains a `Rarity >= RarityThresholds.GuaranteedRareFloor` entry (Veil Pack validity), expressed against the canonical threshold symbol from 2.1.
  - [ ] `Validate_flags_null_entry`: a `null` element in the `_monsters` array is itself a reported problem (skip-and-flag, never dereference → NRE). Pairs with Task 2's null-entry guard.
  - [ ] **Remove the tautological `Asset_file_name_convention_is_Id_underscore_Name`** in `MonsterDefinitionTests.cs` (it hand-recomputes the convention and asserts it against the same literal — the exact tautology 2.1's CR deferred here). The Task 4 on-disk filename audit is its falsifiable replacement. **Sequencing guard:** delete the old test ONLY once the Task 4 disk-scan replacement is present and green; if Task 3 is `[~]` (no assets, no disk scan), leave the old test in place and keep the AC-4 deferral open (do not delete-and-leave-nothing).

- [x] **Task 6 — Verify headless green** (all AC) — DONE. `test-results.xml` → `result="Passed"`, total 161, passed 161, failed 0; `grep "error CS"` → 0. Artifacts deleted before commit (CR/commit phase).
  - [ ] Run the EditMode suite headless (command in Testing Requirements). Confirm `test-results.xml` shows `result="Passed"` AND grep the log for `error CS`. Expected: the current green baseline (read it from the run — do not hardcode; it was 153 cases at the post-2.1 HEAD) PLUS the new Monsters.Tests + the new Architecture.Tests case(s), all green, 0 compile errors. The gate is "all green, 0 failed, 0 compile errors," not a specific number.
  - [ ] Delete `test-results.xml` + `editor-test.log` before any commit (not gitignored).

## Dev Notes

### What this story IS and IS NOT
- **IS:** the `MonsterDatabase` SO registry (universe = 67 lore constant, 3–5 populated), the **content-validation layer** that settles 2.1's null/range deferrals at the registry boundary, the `Count == 67` `Architecture.Tests` assertion (AR-20), the ≥1-Rare+ content criterion (AC-3, Veil Pack validity), and the 3–5 authored MVP `.asset` files.
- **IS NOT:** `CodexService` / codex read-model / persistence (**Story 2.3**), the Codex grid UI (2.4), detail view (2.5). No save-schema change. No Encounter/Lure consumption of the DB (Epic 4). Do NOT add `CaptureDifficulty`/`SlayDifficulty` bounds validation (those 2.1 deferrals are **Story 4 / OQ-9**, not here).

### Settles these Story 2.1 CR deferrals (and ONLY these)
From `deferred-work.md` "code review of 2-1-… (2026-06-13)":
1. **Tautological AC-4 asset-name test** → Task 4's on-disk audit asserts the real `.asset` filenames/IDs against the convention (a production check, not a hand-recomputed literal).
2. **`DisplayName` null guard** → Task 2 `Validate()` flags it.
3. **`Lore` null guard** → Task 2 `Validate()` flags it.
4. **`Art` null** → Task 2 `Validate()` flags it; Task 3 authoring requires art.
5. **Deserialized out-of-range `Rarity` (`(Rarity)99`)** → Task 2 `Validate()` uses `Enum.IsDefined` to flag it.
(The two difficulty-bound deferrals are explicitly **left to Story 4 / OQ-9** — do not pull them in.)

### Architecture constraints (hard rules)
- **`MonsterDatabase` lives in `Monsters/`** (architecture source tree line 401), namespace `Veilwalkers.Monsters`; the `Monsters` asmdef references only `Core` and that must not change (the DB needs nothing above its tier). [docs/architecture.md:401, AR-5]
- **ScriptableObjects hold static data only** — the registry and its definitions never hold player/runtime state (that is `SaveModel`/`CodexService`, Story 2.3). [docs/architecture.md:305, 345]
- **Stable `monNN` IDs are the lookup key everywhere — never array index or display name.** [docs/architecture.md:282] The registry's `TryGet`/`GetById` enforce this; a test pins it.
- **The 67 is a lore constant (AR-20):** `Architecture.Tests` asserts `MonsterDatabase.Count == 67`. Architecture explicitly lists this in the test suite inventory (line 435) and pattern-enforcement (line 636). The universe is 67 *entries*; only 3–5 carry populated content in MVP — don't conflate "universe count" with "populated count."
- **≥1 Rare+ populated** is a content acceptance criterion for Veil Pack / Guaranteed-Rare Lure validity (Story 5.3 filters spawn pool to `Rarity >= Rare`; if no populated Monster qualifies, the Veil Pack is unsellable). [docs/architecture.md:692-695]

### The one asmdef edit this story needs (flag for reviewers)
`Architecture.Tests` currently references **only `Veilwalkers.Core`**. The `Count==67` assertion needs the `MonsterDatabase` type, so add `"Veilwalkers.Monsters"` to `Veilwalkers.Architecture.Tests.asmdef` references. This is a **legal forward edge** (test assembly above services) and the `.Tests` suffix keeps it out of the acyclic allowlist guard — no `AcyclicDependencyTests` matrix edit. [Verified: Assets/Tests/EditMode/Architecture.Tests/Veilwalkers.Architecture.Tests.asmdef references = Core only]

### Registry shape — avoid the "67 stub assets" trap
A naive reading of "accounts for the full 67" tempts authoring 67 empty `.asset` files. Don't. The 67 is a **declared lore constant** (`public const int UniverseCount = 67`); the registry holds the 3–5 *populated* `MonsterDefinition`s in a serialized array. `Count`/the AR-20 assertion reads the constant; `Validate()` audits the populated subset. This matches "3–5 populated for MVP" while keeping the universe size a single source of truth. (Reserved IDs mon06..mon67 are filled in later content passes — not this story.)

### Test seam + content-validation pattern
- Reuse the 2.1/1.6 SO test seam verbatim: `[assembly: InternalsVisibleTo("Veilwalkers.Monsters.Tests")]` + `internal SetForTests(...)`. Build registries from `CreateInstance<MonsterDefinition>()` + `MonsterDefinition.SetForTests(...)` (already exists from 2.1) → `MonsterDatabase.SetForTests(defs)`. No `.asset` needed for the logic/validation tests.
- `Validate()` **returns problems, never throws** (mirrors the `SpendResult`/typed-result house style and AR-7 — services don't throw for expected-bad input; the caller/test decides). Make the validation tests falsifiable: each removed guard must turn a test red (mutation-test once — the Epic-1 retro / 2.1 lesson).

### Previous story intelligence (2.1 + Epic 1)
- **2.1 built `Rarity` + `RarityThresholds.GuaranteedRareFloor` + `MonsterDefinition`** — consume them; do not redefine. AC-3 uses the `GuaranteedRareFloor` symbol.
- **2.1's CR catches that apply here:** (a) doc-comments are contract surface — say exactly what `Validate()`/`Count` cover; (b) no tautological tests — the content-validation tests must exercise the production `Validate()`, not recompute its logic in the test; (c) round-trip every accessor you add (the 2.1 `Art` miss).
- **`.meta` discipline (1.2):** every new `.cs`/`.asset`/folder commits its `.meta`.
- **Headless gate ([[unity-headless-testing]]):** Editor fully closed (`Temp/UnityLockfile`), never trust exit 0 (read `test-results.xml` + grep `error CS`), delete artifacts before commit.

### Project Structure Notes
- `MonsterDatabase.cs` → `Assets/Veilwalkers/Monsters/` (with `Rarity.cs`, `MonsterDefinition.cs` from 2.1).
- MVP `.asset` files + `MonsterDatabase.asset` → `Assets/Veilwalkers/ScriptableObjects/` (the asset home, where `EconomyConfig.asset` lives from 1.6).
- New tests → `Assets/Tests/EditMode/Monsters.Tests/` (registry/validation) and `Assets/Tests/EditMode/Architecture.Tests/` (the `Count==67` assertion).
- Files touched: `MonsterDatabase.cs` (NEW), 3–5 `MonNN_*.asset` + `MonsterDatabase.asset` (NEW), `Veilwalkers.Architecture.Tests.asmdef` (UPDATE — add Monsters ref), new test `.cs` (NEW), all `.meta`s.

### References
- [Source: docs/epics.md#Story-2.2 (397-412)] — the ACs verbatim.
- [Source: docs/epics.md#AR-3 (60), #AR-20 (77)] — 67 in SO registry; `Architecture.Tests` asserts `Count == 67`.
- [Source: docs/architecture.md#Source-tree (401, 435)] — `MonsterDatabase.cs` home; `Architecture.Tests[…, Count==67]`.
- [Source: docs/architecture.md#Minimum-rarity-contract (692-695)] — ≥1 Rare+ for Veil Pack validity; `Rarity >= Rare` floor.
- [Source: _bmad-output/implementation-artifacts/deferred-work.md (3-11)] — the 2.1 CR deferrals this story settles (and the two it must NOT).
- [Source: Assets/Veilwalkers/Monsters/MonsterDefinition.cs, Rarity.cs] — the 2.1 types consumed.
- [Source: Assets/Tests/EditMode/Architecture.Tests/Veilwalkers.Architecture.Tests.asmdef] — the asmdef to extend with a Monsters ref.

## Technical Requirements

- C# / Unity 2022.3 LTS; `MonsterDatabase : ScriptableObject`; namespace `Veilwalkers.Monsters`; IL2CPP/.NET-Standard-2.1-safe (dictionary lookup, no runtime reflection).
- No new package dependencies. No `EconomyConfig`/`SaveModel` schema change.
- The only asmdef change is adding `Veilwalkers.Monsters` to `Architecture.Tests` references (Task 4).

## Testing Requirements

- **Framework:** Unity Test Framework 1.1.33, NUnit, EditMode. Plain `[Test]` (nothing async here).
- **Assemblies touched:** `Veilwalkers.Monsters.Tests` (registry/validation tests) and `Veilwalkers.Architecture.Tests` (the `Count==67` assertion; needs the new Monsters ref).
- **Headless run command** ([[unity-headless-testing]]):
  ```
  "/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode \
    -projectPath "C:/Users/james/Desktop/Veilwalkers" -runTests -testPlatform EditMode \
    -testResults "<abs>/test-results.xml" -logFile "<abs>/editor-test.log" -nographics
  ```
  - Editor fully closed first (`Temp/UnityLockfile` ⇒ locked). Never trust exit 0: confirm `result="Passed"` in `test-results.xml` AND `grep "error CS"` the log → 0. Delete both artifacts before commit.
- **Expected post-story count:** the current green baseline (153 cases at post-2.1 HEAD — confirm from the run, don't hardcode) remains green + new Monsters.Tests + new Architecture.Tests case(s), 0 failed, 0 compile errors.
- **Test-quality bar:** the content-validation tests must exercise the production `Validate()` (not recompute it) and be mutation-testable — removing a guard turns a test red. The `Count==67` test must fail if `UniverseCount` is changed.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Opus 4.8)

### Debug Log References

- Headless run 1: **6 compile errors (`error CS1061`)** — `MonsterDatabaseLoreCountTests` (in `Architecture.Tests`) called `MonsterDefinition.SetForTests` / `MonsterDatabase.SetForTests`, but those are `internal` and only `[InternalsVisibleTo("Veilwalkers.Monsters.Tests")]` — `Architecture.Tests` is NOT in that friend list. Root cause: I had the architecture test building a populated registry via the internal seam, which violates encapsulation. **Fix:** rewrote `MonsterDatabaseLoreCountTests` to use ONLY `MonsterDatabase`'s public surface (`UniverseCount`, static `IsValidMonsterId`); the populated-subset `[3..5]` relationship assertions moved to `MonsterDatabaseTests` in `Monsters.Tests`, which legitimately has internals access. This is cleaner: the architecture-level guard depends only on the public lore contract.
- Headless run 2 (after fix): **161/161 Passed, 0 failed, 0 compile errors** (`grep "error CS"` → 0). 153 prior baseline + the new Monsters.Tests/Architecture.Tests cases.

### Completion Notes List

- Built `MonsterDatabase` (Monsters area): `UniverseCount=67` lore constant + populated `MonsterDefinition[]`, lazy `EnsureIndex` (rebuilt by `SetForTests` to dodge the `OnEnable`-before-populate trap), ID-keyed `TryGet`/`GetById`, and the content-validation layer `Validate()` (returns problems, never throws — AR-7 style).
- **Settled 5 of Story 2.1's CR deferrals at the registry boundary** via `Validate()`: null DisplayName, null Lore, null Art, out-of-range Rarity (`Enum.IsDefined`), and the AC-4 ID-format audit (now via the production `IsValidMonsterId` helper, not a hand-recomputed literal). Plus null-array-entry guard and duplicate-ID. The two difficulty-bound deferrals were deliberately **left to Story 4 / OQ-9** (not pulled in).
- **`Count==67` (AR-20)** asserted in `Architecture.Tests` non-tautologically: `UniverseCount==67` + `IsValidMonsterId` range boundaries (mon01/mon67 pass, mon00/mon68 fail); the populated-model relationships (`PopulatedCount ∈ [3..5]`, ≤ universe, every ID in-universe) are covered in `Monsters.Tests`. One asmdef edit: added `Veilwalkers.Monsters` to `Architecture.Tests` references (legal forward edge, `.Tests`-suffix auto-excluded from the acyclic guard — confirmed: no matrix edit, suite green).
- **AC coverage:** AC-1 → `MonsterDatabase` registry shape + `Mvp_registry_model_holds` + `Registry_resolves_by_id`/`Lookup_uses_id_not_index`; AC-2 → `MonsterDatabaseLoreCountTests`; AC-3 → `At_least_one_populated_is_rare_or_better` (against canonical `RarityThresholds.GuaranteedRareFloor`).
- **Task 3 (`.asset` authoring) deferred `[~]`** — needs the Editor (real art sprites + binary assets; a null `Art` fails `Validate()`). Blocks NO epic AC (all 3 verified via `SetForTests`). Consequence per the story's rule: the AC-4 on-disk filename audit stays deferred (re-routed to `deferred-work.md`), and the old 2.1 tautological `Asset_file_name_convention_is_Id_underscore_Name` test was NOT deleted (the disk-scan replacement doesn't exist yet — sequencing guard honored).
- `.meta` present for every new file (1.2 lesson). `MonsterDatabase` placed in `Monsters/`, not `Codex/`.

### File List

- `Assets/Veilwalkers/Monsters/MonsterDatabase.cs` (NEW) + `.meta`
- `Assets/Tests/EditMode/Monsters.Tests/MonsterDatabaseTests.cs` (NEW) + `.meta`
- `Assets/Tests/EditMode/Architecture.Tests/MonsterDatabaseLoreCountTests.cs` (NEW) + `.meta`
- `Assets/Tests/EditMode/Architecture.Tests/Veilwalkers.Architecture.Tests.asmdef` (UPDATE — added `Veilwalkers.Monsters` reference)
- `_bmad-output/implementation-artifacts/2-2-register-the-67-monster-database-with-mvp-content.md` (story file)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (status transitions)
- *(deferred to Editor session — NOT in this diff: 3–5 `MonNN_*.asset`, `MonsterDatabase.asset`)*

### Change Log

- 2026-06-13 — Implemented Story 2.2 (MonsterDatabase registry + content-validation layer + Count==67 assertion). Settled 5 of Story 2.1's CR deferrals at the registry boundary. Task 3 (`.asset` authoring) deferred `[~]` to an Editor session — blocks no AC. Fixed an internals-access compile error (architecture test now uses public API only). Headless EditMode 161/161 green, 0 compile errors. Status → review.
- 2026-06-13 — Code review (3-layer adversarial fan-out): 14 raw findings → 5 patched, 1 deferred, 4 spec-sanctioned, 4 false-positive, 0 AC violations. Applied 5 patches (ID-parse hardening, Validate no-throw on null array, duplicate-ID lookup determinism, Populated defensive copy, index-not-Id error messages) + 4 new regression tests. Re-ran headless green. Status → done.

## Review Findings

Code review 2026-06-13 — adversarial 3-layer fan-out (Blind Hunter / Edge Case Hunter / Acceptance Auditor), verify-disputed-first triage. 14 raw findings, 0 cross-layer convergence. `acViolations: []` — all 3 ACs satisfied.

**Patched (5):**
- [x] [Review][Patch] `IsValidMonsterId` used `int.TryParse`, which accepts whitespace/sign-padded forms (`'mon 1'`, `'mon+1'`) — violating the documented `^mon\d{2}$`. Rewrote to require exactly two ASCII digits. Added rejection tests. [Assets/Veilwalkers/Monsters/MonsterDatabase.cs]
- [x] [Review][Patch] `Validate()` dereferenced `_monsters.Length` with no null guard, contradicting its "does NOT throw" contract (a corrupt/migrated SO can deserialize the array to null). Guarded with `?? Array.Empty`. Added `Validate_does_not_throw_on_null_backing_array`. [Assets/Veilwalkers/Monsters/MonsterDatabase.cs]
- [x] [Review][Patch] Duplicate-ID lookup was last-writer-wins in `EnsureIndex` (non-deterministic, violated `TryGet`'s "never returns the wrong entry"). Changed to first-writer-wins; added `Duplicate_id_lookup_is_deterministic_first_writer_wins`. [Assets/Veilwalkers/Monsters/MonsterDatabase.cs]
- [x] [Review][Patch] `Populated` returned the live `_monsters` array (castable back to `MonsterDefinition[]` and mutable, staling the index). Returns a defensive `Clone()` now; added `Populated_is_a_defensive_copy`. [Assets/Veilwalkers/Monsters/MonsterDatabase.cs]
- [x] [Review][Patch] `Validate()` field-problem messages used `m.Id` (prints `'null'` when Id is null). Switched to the array index `[i]`. [Assets/Veilwalkers/Monsters/MonsterDatabase.cs]

**Deferred (1) — see `deferred-work.md`:**
- [x] [Review][Defer] `TryGet`/`EnsureIndex` lazy-index not thread-safe → out-of-scope (Unity gameplay is single-threaded; `MonsterDatabase` is main-thread read-only). Owner: a future backend-sync/threading story if one arises.

**Carried from Task 3 `[~]` — re-routed to `deferred-work.md`:**
- [x] [Review][Defer] AC-4 on-disk asset-filename audit + populated-count `[3..5]` real-asset check — require physical `.asset` files (Task 3 deferred to an Editor session). The old 2.1 tautological `Asset_file_name_convention_is_Id_underscore_Name` test stays in place until the disk-scan replacement exists. Owner: Editor-authoring session (Story 2.2 Task 3 completion).

**Spec-sanctioned (4) — no action, by design:**
- `PopulatedCount ∈ [3..5]` not enforced by `Validate()`/tests → deferred with Task 3 `[~]` (verifiers read and honored the deferral rationale).
- `Validate()` doesn't enforce a population minimum → same (Task 2 scope is per-entry content, not count).
- Defensive `_monsters` null checks "contradict initialization" → intentional Unity-deserialization safeguard.
- Architecture.Tests doesn't cross-check populated⊆universe → correctly delegated to Monsters.Tests + the deferred on-disk audit.

**False-positive (4) — dropped:**
- Duplicate-detection skipped for invalid IDs → intentional (invalid entries are already flagged; dedupe targets auditable content).
- Zero-populated registry not tested for AC-3 → AC-3 scope is the *populated subset*; zero-population is architecturally invalid for MVP, not an AC-3 case.
- `mon-5` / negative-number ID not explicitly tested → same invalid-character class as `monAB` (already covered); the digit-only guard rejects it.
- `Enum.IsDefined` boundary values (negative/MaxValue) not tested → `Enum.IsDefined` is a complete validator; testing it further tests the framework, not the integration.
