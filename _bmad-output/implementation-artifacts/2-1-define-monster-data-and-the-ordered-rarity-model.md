---
baseline_commit: "0d3af9be137f23876d0b912d376976992493ac87"
---

# Story 2.1: Define Monster data and the ordered Rarity model

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want Monster definitions as ScriptableObjects with an ordered rarity tier,
so that static creature data is separate from player state and the rarity floor for Guaranteed-Rare is well-defined.

## Acceptance Criteria

**AC-1 — `MonsterDefinition` is a static-data ScriptableObject** [Source: docs/epics.md#Story-2.1; docs/architecture.md#Data-boundaries (488-491, 305)]
**Given** the `Monsters` area
**When** `MonsterDefinition` is created
**Then** `MonsterDefinition` is a ScriptableObject holding art refs, base stats, rarity, and lore — and **never** holds runtime/player state.

**AC-2 — `Rarity` is an ordered enum with a named `Rare` threshold** [Source: docs/epics.md#Story-2.1; docs/architecture.md#Minimum-rarity-contract (687-695)]
**Given** the `Monsters` area
**When** `Rarity` is created
**Then** `Rarity` is an **ordered enum** `Common < Uncommon < Rare < Epic < Nightmare`
**And** a named `Rare` threshold exists (the Guaranteed-Rare Lure floor), expressed so that `rarity >= Rarity.Rare` is a meaningful, correct comparison.

**AC-3 — stable `monNN` string IDs** [Source: docs/epics.md#Story-2.1; docs/architecture.md#Naming (282)]
**Given** Monster ID conventions
**When** assets are authored
**Then** each Monster has a stable string ID `monNN` (mon01..mon67) used everywhere (never array index or display name).

**AC-4 — asset file naming convention** [Source: docs/epics.md#Story-2.1; docs/architecture.md#Naming (280-281)]
**Given** Monster ID conventions
**When** assets are authored
**Then** asset files follow `<Id>_<Name>.asset` (e.g., `Mon01_AntleredShade.asset`).

## Tasks / Subtasks

- [x] **Task 1 — Author `Rarity` ordered enum** (AC: 2)
  - [x] Create `Assets/Veilwalkers/Monsters/Rarity.cs`, namespace `Veilwalkers.Monsters`.
  - [x] Declare `public enum Rarity { Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Nightmare = 4 }` — explicit ascending integer values so the ordering is durable against reorder and `>=` comparisons are correct.
  - [x] Expose the named threshold via canonical `public static class RarityThresholds { public const Rarity GuaranteedRareFloor = Rarity.Rare; }`, XML-doc'd as the Guaranteed-Rare Lure floor (FR-13). No `IsRareOrBetter` extension added (kept the single `const` symbol).
  - [x] XML-doc the enum (ordered dread ladder, append-only / never-reorder contract).

- [x] **Task 2 — Author `MonsterDefinition` ScriptableObject** (AC: 1, 2)
  - [x] Create `Assets/Veilwalkers/Monsters/MonsterDefinition.cs`, namespace `Veilwalkers.Monsters`.
  - [x] `[CreateAssetMenu(fileName = "MonNN_Name", menuName = "Veilwalkers/Monster Definition")]`, `public sealed class MonsterDefinition : ScriptableObject`.
  - [x] Private `[SerializeField]` fields + public read-only accessors: `_id`, `_displayName`, `_rarity`, `_captureDifficulty`, `_slayDifficulty`, `_lore`, `_art` (`Sprite`). Stats are minimal + flagged OQ-9-provisional per Dev Notes.
  - [x] No runtime/player-state fields. Enforced by `No_public_mutable_state` (declared-members-only read-only invariant).
  - [x] `internal SetForTests(...)` seam (params 1:1 with serialized fields, declaration order) + `[assembly: InternalsVisibleTo("Veilwalkers.Monsters.Tests")]`.
  - [x] Art ref is `Sprite` (lightest UnityEngine-core type, no asmdef edge).
  - [x] XML-doc the class (static design data only; player state lives in SaveModel/CodexService; read-only accessors are the guard — stated as convention, not an overclaimed "enforced" guarantee).

- [x] **Task 3 — Create the `Monsters.Tests` EditMode assembly** (AC: 1, 2, 3, 4)
  - [x] Create `Assets/Tests/EditMode/Monsters.Tests/Veilwalkers.Monsters.Tests.asmdef` (mirrors Economy.Tests; references Core + Monsters only).
  - [x] `.Tests` suffix → auto-excluded by the acyclic guard. Verified empirically: headless run shows 0 compile errors and the `Architecture.Tests` allowlist guard stayed green with the new test assembly present — no matrix edit needed.

- [x] **Task 4 — Write tests** (AC: 1, 2, 3, 4)
  - [x] `RarityTests.Rarity_is_ordered_ascending` (via `Is.Ordered.Ascending`).
  - [x] `RarityTests.Rarity_rare_floor_is_inclusive` — three+ cases incl. the on-the-floor `Rare >= floor == true`, plus `GuaranteedRareFloor_is_Rare`.
  - [x] `RarityTests.Rarity_explicit_values_are_stable` (Common==0 .. Nightmare==4).
  - [x] `MonsterDefinitionTests.Accessors_round_trip_set_values` + `No_public_mutable_state` (falsifiable read-only invariant, `DeclaredOnly`-scoped so Unity base `name`/`hideFlags` are correctly out of scope).
  - [x] `MonsterDefinitionTests.Id_follows_monNN_format` (`^mon\d{2}$`, format-level only — content auditing is 2.2).
  - [x] `MonsterDefinitionTests.Asset_file_name_convention_is_Id_underscore_Name` (derives `<Id>_<Name>` ⇒ `Mon01_AntleredShade`).

- [x] **Task 5 — Verify headless green** (all AC)
  - [x] Ran EditMode suite headless: `test-results.xml` → `result="Passed"`, total 153, passed 153, failed 0; `grep "error CS" editor-test.log` → 0. (143-case prior baseline + 8 new `[Test]` methods in Monsters.Tests, which NUnit reports as 10 test-case nodes; no regressions.)
  - [x] `test-results.xml` + `editor-test.log` deleted before commit (handled in CR/commit phase).

## Dev Notes

### What this story IS and IS NOT
- **IS:** the *types* — `Rarity` enum + `MonsterDefinition` SO + the named `Rare` threshold + a `Monsters.Tests` assembly. It locks the **shape** of static creature data and the rarity model.
- **IS NOT:** the `MonsterDatabase` registry, the 67-count lore assertion, authoring the 3–5 MVP `.asset` files, or the "≥1 Rare+ populated" content criterion — **all of that is Story 2.2.** Do not build the registry here. Do not author monster `.asset` content here (a single throwaway asset to prove `[CreateAssetMenu]` works is fine but not required; tests use `CreateInstance` + `SetForTests`). Codex read-model + persistence is Story 2.3.

### Architecture constraints (hard rules — violating these is a review failure)
- **ScriptableObjects hold static data only; never write runtime/player state into an SO.** [docs/architecture.md:305, 345] This is the central correctness contract of AC-1. Player state (discovered/captured/scan flags) lives in `SaveModel` via `IProgressStore` (already built, Story 1.3) and will be read by `CodexService` (Story 2.3).
- **`Rarity` ordered enum `Common < Uncommon < Rare < Epic < Nightmare`, named `Rare` threshold = Guaranteed-Rare Lure floor.** [docs/architecture.md:687-695; AR-3/AR-13 in docs/epics.md:60,70] `GuaranteedRareLure` (Story 5.3) resolves via `Rarity >= Rarity.Rare`; your `RarityThresholds.GuaranteedRareFloor` constant is what that future story consumes. Get the `>=` semantics right and tested now (note: the "AR-NN" shorthand used throughout this story refers to the Architecture Requirements list in **epics.md:58-77**, not architecture.md).
- **Naming canon** [docs/architecture.md:280-282]: SO type suffix `Definition`; asset files `<Id>_<Name>.asset` (e.g. `Mon01_AntleredShade.asset`); IDs are stable strings `monNN` (mon01..mon67), **never** array index or display name.
- **Acyclic boundary** [docs/architecture.md#AR-5]: `Monsters` sits on the same tier as Persistence/Economy, **below** AR. Its asmdef may reference **only `Veilwalkers.Core`** (it already does — see Project Structure Notes). `MonsterDefinition` must NOT pull in AR-Foundation types or anything above its tier. Art references must use base UnityEngine types (`Sprite`, `GameObject`, `Texture2D`) that are available without an upward asmdef edge.

### Minimal MVP stat shape (guidance, not over-spec)
AR-3 says "base stats" without enumerating them, and full balancing is OQ-2/OQ-9. Keep the stat surface **minimal and provisional** — enough to be real static data, flagged as tunable. A reasonable minimum: a small `int`/`float` for a capture-difficulty or "stability" stat and a slay/combat stat. Mark them OQ-9-provisional in XML-doc (the 1.6 precedent for provisional economy numbers). Do not invent an elaborate stat block the later epics haven't asked for; the AC only requires that "base stats" exist as static fields.

### Test seam pattern (copy from 1.6 EconomyConfig)
The `EconomyConfig` SO established the lean pattern for SO test access — do exactly this for `MonsterDefinition`:
```csharp
// top of MonsterDefinition.cs
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Veilwalkers.Monsters.Tests")]
```
```csharp
// inside the class — internal, never public
internal void SetForTests(string id, string displayName, Rarity rarity, /* stats, lore, art */) { ... }
```
[Source: Assets/Veilwalkers/Economy/EconomyConfig.cs:4-7,182] — `InternalsVisibleTo` + an `internal SetForTests` is the sanctioned test-only mutation path; private `[SerializeField]` + read-only accessors keep the runtime contract intact.

### `Monsters.Tests` asmdef — exact JSON (mirror Economy.Tests)
```json
{
    "name": "Veilwalkers.Monsters.Tests",
    "rootNamespace": "Veilwalkers.Monsters.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "Veilwalkers.Core",
        "Veilwalkers.Monsters"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```
[Source: Assets/Tests/EditMode/Economy.Tests/Veilwalkers.Economy.Tests.asmdef]

### Files being touched (all NEW — no UPDATE files this story)
- `Assets/Veilwalkers/Monsters/Rarity.cs` (NEW)
- `Assets/Veilwalkers/Monsters/MonsterDefinition.cs` (NEW)
- `Assets/Tests/EditMode/Monsters.Tests/Veilwalkers.Monsters.Tests.asmdef` (NEW)
- `Assets/Tests/EditMode/Monsters.Tests/*Tests.cs` (NEW)
- `.meta` files for each new `.cs`/`.asmdef`/folder — Unity generates them; **commit them** (the 1.2 lesson: a missing `.meta` orphans on fresh clone). Verify a `.meta` exists for every new file before committing.
- **No edits** to `AcyclicDependencyTests` matrix (`.Tests` suffix is auto-excluded — verify, don't assume). No edits to `Veilwalkers.Monsters.asmdef` (already references only Core, which is correct).

### Project Structure Notes
- The `Monsters` asmdef already exists from Story 1.2 and references **only `Veilwalkers.Core`** — this is exactly right for 2.1; do not add references. [Verified: Assets/Veilwalkers/Monsters/Veilwalkers.Monsters.asmdef]
- A `Monsters/Codex/` subfolder stub exists (`.gitkeep`) reserved for `CodexService` (Story 2.3). **Do not put `Rarity`/`MonsterDefinition` inside `Codex/`** — they go directly in `Monsters/` per the architecture source tree (docs/architecture.md:398-402).
- There is currently **no `Monsters.Tests` assembly** (only App/Architecture/Economy/Persistence under `Assets/Tests/EditMode/`). Creating it is Task 3.
- Architecture source tree places `MonsterDefinition.cs`, `Rarity.cs`, `MonsterDatabase.cs` (2.2), and `Codex/CodexService.cs` (2.3) all under `Monsters/`. [docs/architecture.md:398-402]

### Previous story intelligence (Epic 1 patterns that apply)
- **SO test-seam pattern (Story 1.6):** `InternalsVisibleTo` + `internal SetForTests` is proven and lean — reuse verbatim. Do NOT reach for `SerializedObject`/reflection mutation.
- **`.meta` discipline (Story 1.2):** every committed file needs its `.meta`; a missing folder/file `.meta` orphans on fresh clone (1.2 fixed exactly this with `Codex/.gitkeep`).
- **Doc-comments are contract surface (Stories 1.4/1.5/1.6):** the recurring review-finding class across Epic 1 was XML-doc overstating what code does. Write the `Rarity`/`MonsterDefinition` doc-comments to say *exactly* what is true (e.g. don't claim runtime immutability is "enforced" if it's only a convention + private setters — say "convention; private `[SerializeField]` + read-only accessors are the guard").
- **Test-quality bar (Epic 1 retro):** no tautological tests. The static-data-only test must actually be able to fail — assert the *absence* of a settable player-state member via reflection, not just that the happy-path accessors work.
- **Headless gate discipline ([[unity-headless-testing]]):** never trust exit 0; read `test-results.xml` for `Passed` + grep log for `error CS`; the Unity Editor must be fully closed (`Temp/UnityLockfile` present = locked) before a headless run.

### References
- [Source: docs/epics.md#Story-2.1 (379-396)] — the four ACs verbatim.
- [Source: docs/architecture.md#Minimum-rarity-contract (687-704)] — ordered `Rarity`, named `Rare` threshold, `Rarity >= Rarity.Rare` floor for Guaranteed-Rare Lure.
- [Source: docs/architecture.md#Naming (280-282)] — `<Name>Definition` type, `<Id>_<Name>.asset`, `monNN` IDs.
- [Source: docs/architecture.md#Data-boundaries (305, 345, 488-491)] — SOs hold static data only; never player state.
- [Source: docs/architecture.md#Source-tree (398-402)] — `Monsters/` folder layout.
- [Source: Assets/Veilwalkers/Economy/EconomyConfig.cs:4-7,182] — `InternalsVisibleTo` + `internal SetForTests` precedent.
- [Source: Assets/Tests/EditMode/Economy.Tests/Veilwalkers.Economy.Tests.asmdef] — test-asmdef template.

## Technical Requirements

- **Language/engine:** C# for Unity 2022.3 LTS; `MonsterDefinition` derives `UnityEngine.ScriptableObject`. `.NET Standard 2.1` / IL2CPP-safe (no reflection-heavy runtime code; reflection is test-only).
- **Namespace:** `Veilwalkers.Monsters` for both new types.
- **No new package dependencies.** Everything needed (UnityEngine, the `Monsters` and `Core` asmdefs, NUnit via TestRunner) is already pinned/present.
- **No `EconomyConfig` / `SaveModel` schema changes** — this story adds static-data types only; it touches neither the save schema nor the economy config.

## Testing Requirements

- **Framework:** Unity Test Framework (UTF) 1.1.33, NUnit, EditMode. UTF 1.1.33 has **no `async Task` test support** — but nothing here is async, so plain `[Test]` methods suffice.
- **New assembly:** `Veilwalkers.Monsters.Tests` (Task 3).
- **Headless run command** ([[unity-headless-testing]]):
  ```
  "/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode \
    -projectPath "C:/Users/james/Desktop/Veilwalkers" -runTests -testPlatform EditMode \
    -testResults "<abs>/test-results.xml" -logFile "<abs>/editor-test.log" -nographics
  ```
  - **Editor must be fully closed** before running (`Temp/UnityLockfile` present ⇒ locked; ask James to close Unity).
  - **Never trust exit 0:** confirm `test-results.xml` has the new tests with `result="Passed"` AND grep `editor-test.log` for `error CS` (a compile error still exits 0 via the crash handler).
  - **Delete `test-results.xml` + `editor-test.log` before committing** (not gitignored).
- **Expected post-story count:** the 145 from Story 1.9 remain green + the new Monsters.Tests cases, 0 failures, 0 compile errors.
- **Test-quality bar:** the AC-1 static-data test must be falsifiable (mutation-testable) via the **read-only invariant** (no public mutable field, no public property setter) — not a name denylist. Adding *any* public setter/mutable field to `MonsterDefinition` must turn it red; mutation-test once to confirm.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Opus 4.8)

### Debug Log References

- Headless EditMode run 1: 152/153, **1 failure** — `MonsterDefinitionTests.No_public_mutable_state` reported `Found: name, hideFlags`. True positive against the wrong scope: the reflection check walked the `ScriptableObject`/`UnityEngine.Object` base class, whose `name`/`hideFlags` are framework infrastructure, not game/player state this story introduces. Fixed by scoping the reflection to `BindingFlags.DeclaredOnly` (members `MonsterDefinition` itself declares) — the AC-1 contract is about state this type adds, and the invariant stays falsifiable for any public mutable member added to *this* type.
- Headless EditMode run 2 (after fix): **153/153 Passed, 0 failed, 0 compile errors** (`grep "error CS" editor-test.log` → 0). The 8 new `[Test]` methods (4 `RarityTests` + 4 `MonsterDefinitionTests`) are reported by NUnit as 10 test-case nodes.

### Completion Notes List

- Implemented the static-data shape for Epic 2: `Rarity` ordered enum + named `RarityThresholds.GuaranteedRareFloor` (= `Rarity.Rare`, inclusive), and `MonsterDefinition` SO (private `[SerializeField]` + read-only accessors + `InternalsVisibleTo`/`SetForTests`, the Story 1.6 precedent).
- Created the `Veilwalkers.Monsters.Tests` EditMode assembly (first tests in the Monsters area); references `Core` + `Monsters` only. The `.Tests` suffix is auto-excluded from the acyclic-graph allowlist guard — confirmed empirically (Architecture.Tests stayed green, no matrix edit).
- **Scope held:** no `MonsterDatabase` registry, no `Count == 67` assertion, no MVP `.asset` content authoring, no "≥1 Rare+" content criterion (all Story 2.2); no Codex persistence (Story 2.3). No `EconomyConfig`/`SaveModel` schema changes. `Veilwalkers.Monsters.asmdef` unchanged (already references only Core).
- **AC mapping:** AC-1 → `MonsterDefinition` static-data SO + `No_public_mutable_state`; AC-2 → `Rarity` enum + threshold + `Rarity_is_ordered_ascending`/`_rare_floor_is_inclusive`/`_explicit_values_are_stable`; AC-3 → `Id` accessor + `Id_follows_monNN_format`; AC-4 → `Asset_file_name_convention_is_Id_underscore_Name`.
- Every new `.cs`/`.asmdef`/folder has its Unity-generated `.meta` (the Story 1.2 orphaned-meta lesson honored).
- Doc-contract honesty (Epic-1 recurring-finding class): the `MonsterDefinition` XML-doc states the read-only accessors ARE the guard (a convention via access shape), not an overclaimed runtime-immutability "enforcement."

### File List

- `Assets/Veilwalkers/Monsters/Rarity.cs` (NEW) + `.meta`
- `Assets/Veilwalkers/Monsters/MonsterDefinition.cs` (NEW) + `.meta`
- `Assets/Tests/EditMode/Monsters.Tests/Veilwalkers.Monsters.Tests.asmdef` (NEW) + `.meta`
- `Assets/Tests/EditMode/Monsters.Tests/RarityTests.cs` (NEW) + `.meta`
- `Assets/Tests/EditMode/Monsters.Tests/MonsterDefinitionTests.cs` (NEW) + `.meta`
- `Assets/Tests/EditMode/Monsters.Tests.meta` (NEW folder meta)
- `_bmad-output/implementation-artifacts/2-1-define-monster-data-and-the-ordered-rarity-model.md` (story file — Dev Agent Record, tasks, status)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (status transitions)

### Change Log

- 2026-06-13 — Implemented Story 2.1 (Monster data + ordered Rarity model). Added `Rarity` enum + `RarityThresholds`, `MonsterDefinition` SO, and the `Monsters.Tests` assembly (8 `[Test]` methods). Headless EditMode 153/153 green, 0 compile errors. Status → review.
- 2026-06-13 — Code review (3-layer adversarial fan-out): 15 raw findings → 3 patched, 7 deferred, 2 spec-sanctioned, 3 false-positive, 0 AC violations. Applied 3 patches (Art round-trip assertion, brittle line-number citation, test-count wording). Re-ran headless: 153/153 green. Status → done.

## Review Findings

Code review 2026-06-13 — adversarial 3-layer fan-out (Blind Hunter / Edge Case Hunter / Acceptance Auditor), verify-disputed-first triage. 15 raw findings, 0 cross-layer convergence. `acViolations: []` — all 4 ACs satisfied.

**Patched (3):**
- [x] [Review][Patch] Round-trip test omitted the `Art` accessor (`_art → Art` unexercised — a wire-bug could pass silently). Added a real `Sprite` via `Sprite.Create` and `Assert.That(def.Art, Is.SameAs(sprite))`. [Assets/Tests/EditMode/Monsters.Tests/MonsterDefinitionTests.cs]
- [x] [Review][Patch] `Rarity.cs` doc-comment cited a brittle `epics.md:70` line number that would silently rot. Changed to cite the requirement (FR-13 / AR-13) + architecture "Minimum rarity contract" section. [Assets/Veilwalkers/Monsters/Rarity.cs]
- [x] [Review][Patch] Story Dev Notes/Debug Log said "10 new Monsters.Tests cases"; there are 8 `[Test]` methods (NUnit reports 10 test-case nodes). Reworded to the verifiable method count. [this story file]

**Deferred (7) — see `deferred-work.md` under "code review of 2-1-… (2026-06-13)":**
- [x] [Review][Defer] AC-4 asset-name test is tautological (no production code derives the name yet) → Story 2.2/2.3 (registry + asset persistence).
- [x] [Review][Defer] `DisplayName` null guard → Story 2.2 (authored-asset content validation).
- [x] [Review][Defer] Capture/Slay difficulty negative-value bound → Story 4 (Encounter, where consumed) / OQ-9.
- [x] [Review][Defer] `Lore` null guard → Story 2.2 content validation.
- [x] [Review][Defer] `Art` Sprite null guard for authored assets → Story 2.2 (authoring) / 2.3 (UI defensive render).
- [x] [Review][Defer] Difficulty upper-bound/overflow → Story 4 / OQ-9 balancing.
- [x] [Review][Defer] Deserialized out-of-range `Rarity` (e.g. `(Rarity)99` from a corrupt/future asset) → Story 2.2 `MonsterDatabase` load validation.

**Spec-sanctioned (2) — no action, by design:**
- `monNN` regex `^mon\d{2}$` permits `mon00`/`mon68..99`: format-level only; range/content audit is Story 2.2 (Dev Notes sanction this).
- `SetForTests` implicit parameter-order contract: the inherited 1.6 `EconomyConfig` test-seam pattern reused verbatim per Dev Notes.

**False-positive (3) — dropped:**
- "Null/empty ID bypasses format contract" — `Regex.IsMatch` returns false for null/empty, so `Id_follows_monNN_format` IS the guard for the test seam.
- "Append-only enum not guarded" — inserting/reordering a member forces a renumber that breaks `Rarity_explicit_values_are_stable`; the explicit-values test IS the guard.
- "Unsafe `Substring` on short IDs" — the `^mon\d{2}$` regex locks IDs to ≥5 chars, so `Substring(1)` is safe.
