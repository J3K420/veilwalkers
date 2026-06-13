---
baseline_commit: 7a6b130
---

# Story 1.5: Earn XP and award charges via progression

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want to earn XP by playing and receive charges of the extras when I level up,
So that Strong Capture / Stability Boost / Nightveil Filter are earned, never bought.

## Acceptance Criteria

**AC-1 — `ProgressionService` (hosted in Economy): player-wide XP/level pool, threshold level-ups grant charges**
**Given** `ProgressionService` (hosted in Economy)
**When** XP is added (Capture grants some; Slay grants more — values from `EconomyConfig`)
**Then** a player-wide XP/level pool updates and crossing a level threshold grants charges via `ChargeInventory` (Strong Capture, Stability Boost, Nightveil Filter)
**And** `AddCharge(...)` increases the relevant charge count.
[Source: docs/epics.md#Story 1.5; docs/architecture.md#Economy Model Revision ("XP & Progression"); docs/epics.md#AR-16]

**AC-2 — `ChargeInventory`: consume exactly one, never Credits, never negative, zero blocked with "earn via XP"**
**Given** `ChargeInventory`
**When** a charge is consumed
**Then** exactly one charge of that type is removed, it never touches Credits, and it never goes below zero
**And** consuming with zero charges is blocked and returns a result indicating "earn via XP."
[Source: docs/epics.md#Story 1.5; docs/architecture.md#Edge Cases & Failure Handling ("Charge at zero")]

**AC-3 — Atomic charge mutation: mutate-in-memory → persist → rollback on persist failure, pinned by a forced-throw test**
**Given** a charge consumption
**When** the mutate happens
**Then** it is atomic: mutate-in-memory → persist → roll back on persist failure
**And** a test forces `IProgressStore.Save` to throw and asserts the charge count is intact.
[Source: docs/epics.md#Story 1.5; docs/epics.md#AR-8; docs/epics.md#AR-20; docs/architecture.md#Process Patterns ("Credit/charge spends are atomic")]

> **AC-3 placement deviation (sanctioned):** the epic spells "an `Architecture.Tests` test", but the established AR-20 precedent puts guarantee tests in their domain suite — 1.3's save-tamper test (also named under AR-20's `Architecture.Tests` list) lives in `Persistence.Tests/SaveTamperRecoveryTests.cs`. The charge-atomicity test therefore lives in `Economy.Tests`, exactly like 1.4's rollback tests. `Architecture.Tests` keeps only the asmdef matrix + locator tests. Do not add Economy references to `Architecture.Tests`' asmdef.

## Tasks / Subtasks

- [x] **Task 1 — Extend the Core failure vocabulary for the zero-charge path (AC: #2)**
  - [x] `Core/SpendFailureReason.cs` — APPEND `InsufficientCharges = 3` (append-only, never reorder — persisted/telemetry value stability). Doc-comment: the consume was blocked because the player has zero charges of that type; the remedy is XP (level-ups), never Credits — UI maps this reason to the "earn via XP" hint (the typed reason IS the AC's "result indicating earn-via-XP"; `SpendResult` has no message field and services produce no UI copy, AR-11 spirit). [Source: Assets/Veilwalkers/Core/SpendFailureReason.cs; docs/architecture.md#Edge Cases & Failure Handling ("Charge at zero")]
  - [x] No other Core code changes. `SpendResult { Success, NewBalance, FailureReason }` is reused behaviorally as-is for charge consumption — `NewBalance` carries the remaining charge count of that type. Do NOT invent a parallel `ChargeResult`. ONE sanctioned doc-comment touch: `SpendResult`'s class doc currently says "Result of attempting to spend credits" — append a sentence that charge consumption (Story 1.5) reuses it, with `NewBalance` then meaning the remaining charge count of the consumed type (doc-comments are contract surface; a stale credits-only claim is a review finding). [Source: Assets/Veilwalkers/Core/SpendResult.cs; docs/architecture.md#Communication Patterns; _bmad-output/implementation-artifacts/1-4-…md#Review Findings (doc-contract corrections)]

- [x] **Task 2 — Author the Economy vocabulary: `ChargeType`, `ChargeInventory`, `ProgressionRules` (AC: #1, #2)**
  - [x] `Economy/ChargeType.cs` — `enum ChargeType { StrongCapture = 0, StabilityBoost = 1, NightveilFilter = 2 }` (explicit values, append-only — same stability rule as `SpendFailureReason`; these will appear in telemetry and the 1.9 AdHook). [Source: docs/epics.md#AR-17; docs/architecture.md#Economy Model Revision]
  - [x] `Economy/ChargeInventory.cs` — the ONE place the `ChargeType` → `SaveModel` field mapping lives: `static int GetCount(SaveModel model, ChargeType type)` and `static void SetCount(SaveModel model, ChargeType type, int count)` switching over `StrongCaptureCharges` / `StabilityBoostCharges` / `NightveilFilterCharges`; unknown enum value → `ArgumentOutOfRangeException`; `SetCount` rejects negative with `ArgumentOutOfRangeException` (defense-in-depth for "never goes below zero"). No other file may touch the three charge fields directly — every later consumer (1.9 AdHook, Epic 4 extras) goes through `ProgressionService`, which goes through this mapping. [Source: docs/epics.md#Story 1.5 ("grants charges via ChargeInventory"); docs/architecture.md#Complete Project Directory Structure (ChargeInventory.cs); Assets/Veilwalkers/Persistence/SaveModel.cs]
  - [x] `Economy/ProgressionRules.cs` — immutable plain class (NOT a ScriptableObject) carrying the numbers `ProgressionService` needs: `IReadOnlyList<int> LevelXpThresholds` (element k = LIFETIME XP required to reach level k+1; Level = count of thresholds `<= Xp`; level cap = list length) and the per-level-up grant bundle `int StrongCapturePerLevelUp`, `int StabilityBoostPerLevelUp`, `int NightveilFilterPerLevelUp`. Ctor validates: thresholds non-null, non-empty, all positive, strictly increasing; grants `>= 0`. **Why this seam:** Story 1.6 owns `EconomyConfig` (the tunable SO); this story must not hard-code economy numbers in service logic (AR-16), so the rules are constructor-injected data — 1.6's `EconomyConfig` will construct/expose a `ProgressionRules` and Bootstrap will read it from the asset. Same pattern as 1.4's "cost is a parameter". [Source: docs/epics.md#AR-16; docs/epics.md#Story 1.6; docs/architecture.md#Data & Serialization Formats ("ScriptableObjects hold static data only"); _bmad-output/implementation-artifacts/1-4-…md#Dev Notes ("No costs live here")]

- [x] **Task 3 — Introduce the shared Economy mutation lock; refit `CreditService` (AC: #3)**
  - [x] **Why (the hazard THIS story creates):** `SaveService.SaveAsync()` snapshots `Current` on the calling thread; 1.4's per-service `SemaphoreSlim` serializes credit mutations against each other, but a SECOND mutator of the same model (this story) re-opens the interleave: a credit spend mid-persist concurrent with a charge consume means one service's `SaveAsync` can durably write the other service's not-yet-committed (rollback-eligible) delta — a process kill in that window persists a deduction whose spend reported failure. The story that introduces the second mutator owns the mitigation. [Source: Assets/Veilwalkers/Persistence/SaveService.cs (calling-thread snapshot doc); _bmad-output/implementation-artifacts/1-4-…md#Threading & pipeline traps]
  - [x] `Economy/SaveMutationLock.cs` — tiny sealed class wrapping a `SemaphoreSlim(1, 1)` (`Task WaitAsync()`, `void Release()`; doc-comment states the contract: ALL Economy mutate-and-persist spans of the shared `SaveModel` serialize through one instance). One public type per file.
  - [x] `Economy/CreditService.cs` — REPLACE the private `_mutationLock` field with a constructor-injected `SaveMutationLock` (`CreditService(SaveService, SaveMutationLock)`, null-guarded like the existing param). Nothing else about the credit pipeline changes — same wait/release sites, same release-before-raise order. This is a sanctioned, minimal refit; do not restructure the 1.4 pipeline.
  - [x] Update existing `Economy.Tests` fixtures for the new ctor param (one shared lock instance per fixture). The existing `CreditService` null-guard test (`new CreditService(null)`) becomes two assertions — `(null, lock)` AND `(save, null)` each throw `ArgumentNullException` (guards get tests, the 1.4 rule). The 1.4 serialization test (TCS-gated two-spend) must stay green unmodified in behavior.

- [x] **Task 4 — Author `IProgressionService` + `ProgressionService` in Economy (AC: #1, #2, #3)**
  - [x] `Economy/IProgressionService.cs` — the seam Encounter (Epic 4), AdHook (1.9), and UI consume: `int Xp { get; }`; `int Level { get; }`; `int GetChargeCount(ChargeType type)`; `Task<Result> AddXpAsync(int amount)`; `Task<Result> AddChargeAsync(ChargeType type)` (adds exactly one — the AR-17 ad grant is one charge per call; level-up grants are internal to `AddXpAsync`); `Task<SpendResult> TryConsumeChargeAsync(ChargeType type)`; events `event Action<int> OnXpChanged` (payload = new lifetime XP), `event Action<int> OnLevelChanged` (payload = new level, raised only when level actually changes), `event Action<ChargeType, int> OnChargesChanged` (payload = type + new count for that type). Doc-comment carries the SAME threading/event contract as `ICreditService` (raised outside the mutation lock, only after a committed persist, possibly on a background thread, no cross-mutation ordering guarantee — treat as change-signal / re-read properties; UI marshals in Epic 6), the throw enumeration (guards below + `OverflowException` from `checked`), and the dirty-read caveat on `Xp`/`Level`/`GetChargeCount` during a persist window (mirrors `ICreditService.Balance` — these doc items were all 1.4 review findings; write them correctly the first time). [Source: Assets/Veilwalkers/Economy/ICreditService.cs; docs/epics.md#AR-6; _bmad-output/implementation-artifacts/1-4-…md#Review Findings]
  - [x] `Economy/ProgressionService.cs` — implements `IProgressionService` over constructor-injected `SaveService`, `ProgressionRules`, `SaveMutationLock` (all null-guarded; pure-logic area — never `GameServices.Get<T>()`). State lives ONLY in `SaveModel` (`Xp`, `Level`, three charge fields) — no duplicate fields, same single-source-of-truth rule as `CreditService`. `RequireModel()` guard identical to 1.4 (`InvalidOperationException` when `Current` is null), applied to EVERY member including the getters.
  - [x] **Guards (programmer errors — THROW; typed results cover expected failures only):** `AddXpAsync(amount <= 0)` → `ArgumentOutOfRangeException`; invalid `ChargeType` (outside defined members) → `ArgumentOutOfRangeException` (via `ChargeInventory`'s switch); uninitialized save → `InvalidOperationException`; XP/charge arithmetic in `checked` blocks (`OverflowException` = programmer error, same family). [Source: Assets/Veilwalkers/Economy/CreditService.cs (guard pattern); _bmad-output/implementation-artifacts/1-4-…md#Tasks (guards)]
  - [x] **`AddXpAsync` pipeline (AR-8 — one atomic write for XP + level + charges):** wait on the shared lock → capture the model reference ONCE (`var model = RequireModel()`) → snapshot `priorXp`, `priorLevel`, and all three prior charge counts → `checked { model.Xp += amount; }` → compute `newLevel` from `Rules.LevelXpThresholds` (count of thresholds `<= model.Xp`, capped at list length) → for EACH level gained (`newLevel - priorLevel`, can be > 1 from one big grant) add one per-level-up bundle to the charge counts via `ChargeInventory` → set `model.Level = newLevel` → ONE `await _saveService.SaveAsync()` → post-persist `ReferenceEquals(_saveService.Current, model)` recovery-swap check (1.4 review decision — mismatch means what was persisted is not this mutation: roll back, `GameLog.Error`, `Result.Fail`) → on success release then raise `OnXpChanged(newXp)` always, `OnLevelChanged(newLevel)` only if a level was gained, and one `OnChargesChanged(type, newCount)` per type whose count actually CHANGED (rules permit a 0 grant for a type — a type granted nothing raises nothing); on persist FAULT roll back ALL captured fields onto the captured model reference (Xp, Level, all three charge counts — restore, don't recompute), `GameLog.Error`, `Result.Fail(...)`, no events. `Level` NEVER decreases in this method (XP only increases; threshold-table changes are a 1.6 migration concern, out of scope). [Source: docs/epics.md#AR-8; Assets/Veilwalkers/Economy/CreditService.cs (the pipeline this clones); docs/architecture.md#Edge Cases & Failure Handling ("One transactional write per player action")]
  - [x] **`AddChargeAsync(type)` pipeline:** same shape — lock → capture model → snapshot prior count → `checked` increment via `ChargeInventory` → persist → swap-check → release → raise `OnChargesChanged(type, newCount)`; rollback + `Result.Fail` + no event on fault.
  - [x] **`TryConsumeChargeAsync(type)` pipeline:** lock → capture model → read count via `ChargeInventory`; if `count == 0`: return `SpendResult.Failed(InsufficientCharges, 0)` — NO persist, NO event, count untouched (never negative — the only decrement is guarded by this check). Otherwise snapshot prior → `SetCount(model, type, count - 1)` → persist → swap-check → release → raise `OnChargesChanged(type, newCount)`, return `SpendResult.Succeeded(newCount)`; on fault roll back the count, `GameLog.Error`, return `SpendResult.Failed(PersistenceFailed, priorCount)`, no event. **Never touches Credits:** no code path reads or writes `model.Credits` — pinned by test. No `OnInsufficientCredits`-style event for zero charges: charges have no Shop (never purchasable), the caller acts on the typed result directly. [Source: docs/epics.md#Story 1.5 AC-2; docs/architecture.md#Edge Cases & Failure Handling ("Charge at zero")]
  - [x] **Event raising:** clone 1.4's subscriber isolation exactly — each raise wrapped in try/catch + `GameLog.Error` (a committed mutation must never surface as a faulted task), raised after the lock is released, `ConfigureAwait(false)` on every await. [Source: Assets/Veilwalkers/Economy/CreditService.cs (RaiseCreditsChanged pattern)]

- [x] **Task 5 — Wire `IProgressionService` in Bootstrap (AC: #1)**
  - [x] `Assets/Veilwalkers/App/Bootstrap.cs` — extend `WireServices()`'s construct-all-before-register block: construct one `SaveMutationLock`, pass it to BOTH `CreditService` and `ProgressionService`; construct a PROVISIONAL `ProgressionRules` with clearly-marked placeholder numbers (e.g. thresholds `{ 100, 250, 450, 700, 1000 }`, one charge of each type per level-up) and a comment stating Story 1.6 replaces this literal with values read from the `EconomyConfig` asset (placeholder lives in App wiring, NOT in service logic — AR-16 is honored by the injection seam). Register `GameServices.Register<IProgressionService>(progressionService)` before `MarkReady()`. The resolvable-before-loaded window note already in Bootstrap covers this service identically (calls before `InitializeAsync` completes throw `InvalidOperationException` by design). [Source: Assets/Veilwalkers/App/Bootstrap.cs; docs/epics.md#AR-4; docs/epics.md#AR-16]
  - [x] No asmdef edits and NO architecture-matrix edit this story: Economy already references Core + Persistence; the new types create no new assembly edges. (Verify `AcyclicDependencyTests` stays green untouched.) [Source: Assets/Veilwalkers/Economy/Veilwalkers.Economy.asmdef; memory: unity-asmdef-architecture-test]

- [x] **Task 6 — Tests: extend `Economy.Tests` (EditMode) (AC: all)**
  - [x] Reuse the existing `FakeProgressStore` (in-memory, `FailNextSave`, `SaveCalls` counter, `SaveGate` TCS, clone-on-save `Stored` snapshots — its `Clone` ALREADY copies `Xp`, `Level`, and all three charge counts, so no fake changes are needed; assert "persisted" against `Stored`, never the live model, the 1.4 review's de-aliasing finding). UTF 1.1.33: no `async Task` tests — block with `.GetAwaiter().GetResult()`. New files `ProgressionServiceTests.cs` + `ChargePipelineTests.cs` (mirror the 1.4 split: surface/guards vs pipeline/concurrency).
  - [x] **AddXp below threshold:** rules `{100, 250}`, add 50 → `Xp == 50`, `Level == 0`, all charges 0, ONE persist, `OnXpChanged(50)` raised after the persist, `OnLevelChanged`/`OnChargesChanged` NOT raised.
  - [x] **Single level-up:** add 100 → `Level == 1`, one bundle granted (each configured type's count incremented per the rules), ONE persist total (XP + level + charges committed together), `OnXpChanged`, `OnLevelChanged(1)`, one `OnChargesChanged` per type with the new count.
  - [x] **Multi-level crossing:** rules `{100, 250}`, add 300 in one call → `Level == 2`, TWO bundles granted, still ONE persist; exact-threshold boundary (`Xp == threshold` exactly) counts as reached (mirrors 1.4's exact-balance rule).
  - [x] **Zero-grant type raises nothing:** rules with one type's per-level-up grant = 0 → level up → that type's count stays 0 and NO `OnChargesChanged` fires for it (the other types still get theirs).
  - [x] **Level cap:** add XP far past the last threshold → `Level == thresholds.Count`, no further grants on later adds.
  - [x] **AddCharge:** +1 to the targeted type only, persisted, `OnChargesChanged(type, 1)`; other types' counts untouched.
  - [x] **Consume happy path:** count 1 → `Success`, `NewBalance == 0`, persisted, `OnChargesChanged(type, 0)`; `model.Credits` asserted UNCHANGED (the never-touches-Credits pin, run with a nonzero credit balance).
  - [x] **AC-2 zero blocked:** count 0 → `Success == false`, `FailureReason == InsufficientCharges`, `NewBalance == 0`, count still 0 (never negative), NO persist call, NO event.
  - [x] **AC-3 charge atomicity (the AR-20 test):** seed count 2, `FailNextSave` → `TryConsumeChargeAsync` returns `Failed(PersistenceFailed)` and the charge count is INTACT at 2 (in-memory AND no new persisted snapshot), no event; `LogAssert.Expect` the `GameLog.Error` (UTF fails tests observing an unexpected error log).
  - [x] **AddXp rollback restores EVERYTHING:** seed near a threshold, `FailNextSave`, add enough to level → `Result.Fail`; `Xp`, `Level`, and ALL charge counts restored to prior; no events; `SaveCalls` incremented exactly once (no retry).
  - [x] **Recovery-swap guard (regression, mirrors 1.4's):** swap `Current` mid-persist (SaveGate) → `PersistenceFailed`/`Result.Fail`, captured model rolled back, no phantom success event.
  - [x] **Cross-service serialization (pins Task 3):** gate the store, start a credit spend (held open at its persist), start `TryConsumeChargeAsync` without awaiting the spend → assert the charge consume has NOT mutated while the spend's save is in flight; release; both commit; exactly two save calls. This is the test that justifies the shared lock.
  - [x] **Mid-encounter level-up edge (architecture boundary rule):** level-up grants a charge, then `TryConsumeChargeAsync` immediately succeeds in the same session — pins "a charge granted mid-encounter is usable immediately."
  - [x] **Guards:** `AddXpAsync(0)`/`(-5)` → `ArgumentOutOfRangeException`; undefined `ChargeType` (cast `(ChargeType)99`) → `ArgumentOutOfRangeException` from all three charge methods; uninitialized save → `InvalidOperationException` from `Xp`/`Level`/`GetChargeCount` getters AND all mutators; none of these persist or raise events (subscribe + assert, the 1.4 review's strengthening).
  - [x] **`ProgressionRules` validation:** null/empty/non-increasing/non-positive thresholds throw; negative grant counts throw.
  - [x] **Wiring round-trip:** `ResetForTests()` → construct fakes + both services over ONE shared lock, register `ICreditService` + `IProgressionService`, `MarkReady()`, resolve, init, AddXp + consume round-trip.
  - [x] Run the FULL EditMode suite headless and confirm green (Architecture.Tests 13 + Persistence.Tests 32 + Economy.Tests 13 existing — adjusted for the ctor refit — + the new progression tests). Command + traps in Dev Notes → Testing Requirements.

- [x] **Task 7 — Verify, update story/sprint records, and commit**
  - [x] Zero compile errors on clean reopen; full EditMode suite green; `test-results.xml`/`editor-test.log` deleted before commit (NOT gitignored).
  - [x] No manual play check required (no new scene/runtime path; Bootstrap wiring covered by the wiring test + proven boot — same sanction as 1.4).
  - [x] All new `.cs` and `.meta` files tracked; no secrets.
  - [x] Commit + push (convention: `Story 1.5: <what>`; implementation and review patches as separate commits), update sprint-status (`ready-for-dev` → `in-progress` → `review`). [Source: CLAUDE.md#Working conventions]

## Dev Notes

### What this story IS (and is NOT)

This story builds the **progression spine**: `IProgressionService`/`ProgressionService`, `ChargeType`, `ChargeInventory` (the single field-mapping), `ProgressionRules` (the injected numbers seam), `InsufficientCharges` failure reason, the shared `SaveMutationLock` (+ the sanctioned `CreditService` ctor refit), Bootstrap wiring, and the Economy.Tests additions including the AR-20 charge-atomicity test. It is the second consumer of the persistence layer and deliberately clones 1.4's pipeline shape.

**DO NOT build here (later stories own these):**
- `EconomyConfig` ScriptableObject + real XP values / thresholds / per-level grants → **Story 1.6**. This story injects `ProgressionRules`; Bootstrap carries a clearly-marked provisional literal until 1.6 swaps in the asset.
- XP **amounts** for Capture/Slay → callers pass them (`AddXpAsync(int)`); Epic 4 reads them from `EconomyConfig`. No XP constants anywhere in this story's service logic.
- `AdHook.GrantReward()` + daily ad cap → **Story 1.9** (it will CALL `AddChargeAsync` with the weakest/randomized low-tier charge; the single-charge `AddChargeAsync(type)` signature is shaped for exactly that call).
- Encounter extras actually consuming charges (Strong Capture / Stability Boost / Nightveil Filter behavior) → **Epic 4** (it will CALL `TryConsumeChargeAsync`).
- UI for XP/level/charges, the "earn via XP" hint copy, event marshaling → **Epic 6** (AR-6/AR-11: this service only raises events and returns typed reasons).
- **Cross-service composite transactions** (e.g. Slay = credit spend + XP grant + codex delta in ONE persist per AR-8's "one transactional write per player action") → **Epic 4** owns composing multi-delta actions; do NOT build a transaction coordinator here. This story's actions (ad-style charge grant, XP add, charge consume) are each a single player action with a single persist, which is AR-8-correct for this scope. Flag for Epic 4: the per-service pipelines will need a composition seam there.
- Threshold-table migration / level recomputation on rules change → 1.6 concern if balancing changes the curve; stored `Level` is authoritative as-earned.

### Design decisions a dev agent must NOT re-litigate

1. **`ProgressionRules` is constructor-injected plain data, not a SO and not constants.** AR-16 forbids hard-coded economy numbers in service logic; 1.6 owns the tunable SO. The injection seam is how both stories stay true: tests inject tiny curves, Bootstrap injects a marked placeholder, 1.6 injects asset-derived rules. Identical reasoning to 1.4's "cost is a parameter".
2. **State lives in `SaveModel` only** (`Xp`, `Level`, `StrongCaptureCharges`, `StabilityBoostCharges`, `NightveilFilterCharges` — all already exist from 1.3). No duplicate fields in the service; getters read through `RequireModel()`. [Source: Assets/Veilwalkers/Persistence/SaveModel.cs]
3. **`Level` is stored, not derived-on-read.** The field is part of the v1 schema; `AddXpAsync` recomputes and persists it atomically with XP and charges. Deriving on every read would silently de-level players when 1.6 rebalances thresholds — as-earned levels are the product rule until a migration story says otherwise.
4. **`SpendResult` + appended `SpendFailureReason` members are the charge-consume vocabulary.** No new result type. `InsufficientCharges = 3` appended (append-only rule is documented in the enum). The "earn via XP" hint is UI copy keyed off the reason — services never produce player-facing strings.
5. **The shared `SaveMutationLock` is required, not optional.** Two services with independent locks can interleave mutate/persist/rollback on the one shared `SaveModel` (see Task 3). One lock for all Economy mutators closes it. The `CreditService` ctor refit is sanctioned and minimal — wait/release sites and release-before-raise order are unchanged.
6. **`ChargeInventory` is the only `ChargeType`→field mapping.** Static helper, not a registered service — "via ChargeInventory" in the AC names the mapping discipline, not a locator entry. A switch duplicated anywhere else is a review finding waiting to happen.
7. **Events follow the 1.4 contract verbatim:** after commit, outside the lock, subscriber-isolated (try/catch + `GameLog.Error`), possibly background-thread, no cross-mutation ordering guarantee, no replay-on-subscribe (Epic 6 owns binding). `OnLevelChanged` only when the level actually changes; `OnChargesChanged` carries `(type, newCount)`, raised only for types whose count actually changed, so UI never queries back. Write the interface doc-comments TRUE the first time — four of 1.4's nine review patches were doc-contract corrections.
8. **No event for zero-charge consume.** `OnInsufficientCredits` exists because the Shop is the remedy (AR-11 navigation trigger); charges are never purchasable, so the remedy is play — the caller reads the typed result. Adding a symmetric event would invite a Shop hook that must never exist for charges.

### Current repo state (verified on disk at baseline 7a6b130)

- Stories 1.1–1.4 done. Working tree clean.
- **Economy assembly contains:** `ICreditService`, `CreditService` (the AR-8 pipeline template — read it before writing `ProgressionService`; the recovery-swap `ReferenceEquals` check, subscriber isolation, and rollback-onto-captured-reference are all in there as the pattern to clone), `InsufficientCreditsEvent`. Asmdef references: `Veilwalkers.Core`, `Veilwalkers.Persistence` — **no asmdef or matrix edits needed this story**.
- **Core surface:** `SpendResult` (readonly struct, `Succeeded`/`Failed` factories, `Failed(None)` throws — never return `default`), `SpendFailureReason { None, InsufficientCredits, PersistenceFailed }` (append `InsufficientCharges = 3`), `Result` (Ok/Fail), `GameLog`, `GameServices` (wire-once, `ResetForTests` under `UNITY_INCLUDE_TESTS`), `IClock` (NOT needed this story — no time logic in progression).
- **Persistence surface:** `SaveService.Current` (null before init / while corrupt), `SaveAsync()` (faults so callers can roll back; serializes the live model on the CALLING thread), `IProgressStore` (the fake-store seam). `SaveModel` already carries `Xp`, `Level`, and the three charge fields with doc-comments naming this story.
- **`Bootstrap.WireServices()`** registers `IClock`, `IProgressStore`, `SaveService`, `ICreditService`; construct-all-before-register; fire-and-forget `InitializeAsync` with observed fault; resolvable-before-loaded window documented.
- **Tests:** `Architecture.Tests` 13 (matrix untouched this story), `Persistence.Tests` 32, `Economy.Tests` 13 (incl. the recovery-swap regression test) — all green at baseline. `FakeProgressStore` already has `FailNextSave`, `SaveCalls`, `SaveGate` TCS, clone-on-save snapshots whose scalar `Clone` already covers `Xp`/`Level`/all three charge fields — no fake edits needed.
- Unity Editor **2022.3.62f3**; UTF **1.1.33** (no `async Task` tests); Newtonsoft 3.2.1 (not needed in these tests).
- `deferred-work.md` contains NO items owned by Story 1.5. Two 1.4 deferrals are explicitly NOT this story's scope: mutation-lock timeout/cancellation (Epic 6 / AR-19) and load-time negative-invariant validation (5.2/1.9) — the same posture applies to the progression fields (no load-time `Xp/Level/charges >= 0` validation here; same owner).

### Threading & pipeline traps (carry-forward from the 1.3/1.4 reviews)

- **`SaveAsync` snapshots `Current` on the calling thread** — the shared lock (Task 3) is what makes "every mutation awaited before the next" mechanical across BOTH Economy services. Do not add `Task.Run` anywhere; `SaveService` owns the thread-hop.
- **Rollback restores snapshots onto the CAPTURED model reference** — never recompute (`model.Xp -= amount` after a fault is wrong if anything else mutated), never re-read `Current` mid-operation. `AddXpAsync` has the widest rollback set in the codebase so far (XP + level + three counts): snapshot all five before mutating.
- **Post-persist `ReferenceEquals(_saveService.Current, model)` check in every commit path** — the 1.4 review's decision finding; clone it, including the "narrows, not closes" comment pointing at the Epic 6 recovery-flow contract.
- **Events after release, after commit, isolated** — raising inside the lock deadlocks synchronously-blocking subscribers (the project's standard test idiom blocks with `GetAwaiter().GetResult()`).
- **`ConfigureAwait(false)` on every await** in Economy.
- **UTF fails tests that observe an error log** — every rollback test needs `LogAssert.Expect(LogType.Error, …)` (match the existing Economy.Tests handling).
- **Write negative-path tests in the first pass** (guard throws, rollback completeness, event-non-firing, no-persist-on-reject) — every review so far has converged on exactly these; the Task 6 list already enumerates them, don't trim it.

### Testing Requirements

- Extend `Assets/Tests/EditMode/Economy.Tests/` (existing asmdef — no new test assembly). In scope: the Task 6 list. The AC-3 charge-atomicity test is the AR-20 guarantee test and lives HERE (placement deviation sanctioned in the AC block above).
- Explicitly NOT in scope: `EconomyConfig` asset round-trip (1.6), ad-cap day-bucket logic (1.9), encounter consumption flows (Epic 4), `LocalProgressStore` file/crypto behavior (proven in 1.3 — fake store only), device/IL2CPP verification (AR-21 release gate).
- **Headless run (the verification gate, same as 1.1–1.4):**
  `"/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode -projectPath "C:/Users/james/Desktop/Veilwalkers" -runTests -testPlatform EditMode -testResults "<abs>/test-results.xml" -logFile "<abs>/editor-test.log" -nographics`
  **Two traps, both fake an exit 0:** (1) project open in the Editor → `HandleProjectAlreadyOpenInAnotherInstance`, no XML written — `Temp/UnityLockfile` existing = locked; ask James to close the Editor. (2) compile error → no XML, crash handler exits 0 — never trust exit 0; confirm `test-results.xml` exists AND grep the log for `error CS`. Delete `test-results.xml`/`editor-test.log` before committing (not gitignored).

### Previous Story Intelligence (from Story 1.4)

- **The pipeline is proven — clone it, don't redesign it.** `CreditService` survived a 3-layer adversarial review; its final shape (captured reference, snapshot-restore rollback, swap-check, release-then-raise, subscriber isolation, checked arithmetic, `LogAssert`-aware tests) is the template. Most of 1.4's review findings were things the pipeline now does that the first draft didn't — start from the final shape.
- **Doc-comments are contract surface.** 4 of 9 review patches were doc corrections (false "both events after persist" claim, missing `OverflowException` enumeration, undocumented dirty-read window, undocumented event-ordering caveat). The interface doc checklist for `IProgressionService` is in Task 4 — treat it as acceptance criteria.
- **Test assertions must not be tautological** — `FakeProgressStore` was de-aliased (clone-on-save) precisely so "persisted" assertions mean something; its `Clone` already snapshots the progression fields, so assert against `Stored`, never the live model.
- **Guard tests must subscribe to events and assert non-firing** — the bare "throws" assertion was a review finding.
- **Red-green collapse is sanctioned:** Unity batch-mode costs minutes per run; write tests and code in one pass, verify with one full-suite headless gate (the 1.1–1.4 pattern), but still write the negative paths up front.
- **Review rhythm:** implementation commit → review-patch commit, separate. Headless full-suite green is the gate before review.

### Git Intelligence

- Baseline: `7a6b130` ("Story 1.4: code review passed … -> done"). Working tree clean.
- Commit convention: `Story 1.5: <what>`; story-record commits like `Story 1.5: create story → ready-for-dev`.
- Established rhythm: create story → (validate) → implement → review patches → done; no new runtime path this story, so no manual-check commit.

### Project Structure Notes

- Production code in `Assets/Veilwalkers/Economy/` (namespace `Veilwalkers.Economy`), one public type per file: `ChargeType.cs`, `ChargeInventory.cs`, `ProgressionRules.cs`, `SaveMutationLock.cs`, `IProgressionService.cs`, `ProgressionService.cs`. Modified: `CreditService.cs` (ctor refit only), `Core/SpendFailureReason.cs` (append one member), `App/Bootstrap.cs` (lock + rules + construct + register).
- Tests extend `Assets/Tests/EditMode/Economy.Tests/`: new `ProgressionServiceTests.cs`, `ChargePipelineTests.cs`; modified `CreditServiceTests.cs`/`CreditPipelineTests.cs` (ctor param only). `FakeProgressStore.cs` needs NO changes — its snapshot already covers the progression fields.
- **Variance notes:** (1) the architecture tree lists `EconomyConfig.cs` under Economy — that is Story 1.6, not this story. (2) The AC's "`Architecture.Tests` test" phrase → `Economy.Tests` per the 1.3 save-tamper precedent (deviation documented in the AC block). (3) The epic's `AddCharge(...)` spelling → `AddChargeAsync(type)` per the architecture's Async-suffix naming rule (same documented deviation 1.4 made for `TrySpendCreditsAsync`). (4) `SaveMutationLock.cs` does not appear in the architecture's directory tree — it is Economy-internal infrastructure introduced by this story's shared-lock decision (Task 3), not a structural deviation to escalate.
- Every new `.cs` needs its `.meta` committed (Unity generates on first Editor open; the 1.4 review verified meta completeness — keep it).

### References

- [Source: docs/epics.md#Story 1.5: Earn XP and award charges via progression] — story + the three ACs.
- [Source: docs/architecture.md#Economy Model Revision] — XP & Progression canon: player-wide pool, Capture/Slay earn (Slay more), level-ups grant the three extras, never purchasable.
- [Source: docs/epics.md#AR-8 / docs/architecture.md#Edge Cases & Failure Handling] — one transactional write per player action; rollback on persist failure.
- [Source: docs/epics.md#AR-16 / docs/epics.md#Story 1.6] — economy numbers are data-driven; the `ProgressionRules` injection seam is this story's compliance mechanism.
- [Source: docs/epics.md#AR-17 / docs/epics.md#Story 1.9] — `AddCharge` is the ad-reward seam's target; shape the signature for it.
- [Source: docs/epics.md#AR-20] — charge-atomicity guarantee test (forced `IProgressStore.Save` throw).
- [Source: docs/epics.md#AR-6 / docs/architecture.md#Communication Patterns] — event-driven UI, typed results, `On…Changed` naming.
- [Source: docs/architecture.md#Edge Cases & Failure Handling ("Charge at zero", "Mid-encounter level-up")] — AC-2's boundary rules + the usable-immediately test.
- [Source: Assets/Veilwalkers/Economy/CreditService.cs] — THE pipeline template (read first).
- [Source: Assets/Veilwalkers/Persistence/SaveService.cs / SaveModel.cs] — persist seam contract; progression fields already in schema.
- [Source: _bmad-output/implementation-artifacts/1-4-grant-and-spend-credits-through-the-ledger.md] — pipeline decisions, review findings (the doc-contract checklist), test patterns, headless gate.
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — 1.4 deferrals that stay deferred here (lock timeout/cancellation; load-time invariants).
- [Source: CLAUDE.md#Working conventions] — commit + push every session; never commit secrets.

### Latest Tech Information

No new external technology this story — pure C# over the existing Unity 2022.3.62f3 / UTF 1.1.33 stack. All constraints carried from 1.3/1.4: no `async Task` tests (UTF 1.1.33), `ConfigureAwait(false)` in logic assemblies, no UnityEngine API off the main thread, Newtonsoft not needed in these tests.

### Project Context Reference

No `project-context.md` exists (the `persistent_facts` glob `**/project-context.md` matched nothing). Authoritative context: `docs/architecture.md`, `docs/epics.md`, `CLAUDE.md`, the 1.1–1.4 story records, and `deferred-work.md`.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Code, dev-story workflow).

### Debug Log References

- Headless EditMode gate: `Unity.exe -batchmode -projectPath … -runTests -testPlatform EditMode -testResults test-results.xml -logFile editor-test.log -nographics` (results pending in this turn; see Completion Notes for the green confirmation).

### Implementation Plan

Cloned the proven 1.4 `CreditService` AR-8 pipeline for every progression mutation (captured-reference, snapshot-restore rollback, post-persist `ReferenceEquals` recovery-swap guard, release-then-raise, subscriber isolation, checked arithmetic, `ConfigureAwait(false)`). Built in task order 1→7:

1. Appended `SpendFailureReason.InsufficientCharges = 3` + the sanctioned `SpendResult` doc addendum (charge consume reuses it; `NewBalance` = remaining count).
2. `ChargeType` (explicit append-only values), `ChargeInventory` (the single `ChargeType`→`SaveModel` field mapping; negative & unknown-enum throw), `ProgressionRules` (immutable validated curve + `LevelForXp`/`GrantPerLevelUp` helpers so the level formula lives in one place).
3. `SaveMutationLock` (shared `SemaphoreSlim(1,1)` wrapper); refit `CreditService` to inject it (`CreditService(SaveService, SaveMutationLock)`), removed now-unused `System.Threading` using; updated both existing test fixtures + split the null-guard test into two assertions.
4. `IProgressionService` (full threading/event/throw doc contract) + `ProgressionService` (AddXp atomic XP+level+charges, AddCharge single-grant, TryConsumeCharge with the zero-block; widest rollback set in the codebase — five fields snapshotted/restored).
5. Bootstrap: one shared `SaveMutationLock` into both services; provisional `ProgressionRules` literal (placeholder, 1.6 swaps in `EconomyConfig`); registered `IProgressionService` before `MarkReady`.
6. `ProgressionServiceTests` (surface/guards/rules-validation) + `ChargePipelineTests` (AR-20 atomicity, full-state rollback, recovery-swap, cross-service serialization, mid-encounter usable-immediately, wiring round-trip). `FakeProgressStore` reused unchanged (its clone already covers the progression fields).

### Completion Notes List

- **Events raised only for changed types:** `AddXpAsync` raises `OnXpChanged` always (on commit), `OnLevelChanged` only when the level actually changed, and `OnChargesChanged(type, newCount)` once per type whose count actually changed — a zero-grant type (rules permit 0) raises nothing. Verified by the zero-grant test.
- **Single-persist multi-level:** crossing several thresholds in one `AddXpAsync` grants N bundles but performs exactly ONE persist (XP + level + all charges committed together).
- **Never touches Credits:** the consume happy-path test runs with a nonzero credit balance and asserts `Credits` unchanged in both the live model and the persisted snapshot.
- **Shared lock proven:** the cross-service test starts a credit spend (held at its persist) and a charge consume on one shared lock, asserting the consume has not mutated while the spend's save is in flight — the justification for Task 3.
- **AddXp helpers on `ProgressionRules`:** `LevelForXp`/`GrantPerLevelUp` keep the level formula and grant mapping single-sourced; additive to the task's "carry the numbers" intent, no sanctioned decision re-litigated.

### File List

**Added (production):**
- `Assets/Veilwalkers/Economy/ChargeType.cs`
- `Assets/Veilwalkers/Economy/ChargeInventory.cs`
- `Assets/Veilwalkers/Economy/ProgressionRules.cs`
- `Assets/Veilwalkers/Economy/SaveMutationLock.cs`
- `Assets/Veilwalkers/Economy/IProgressionService.cs`
- `Assets/Veilwalkers/Economy/ProgressionService.cs`

**Added (tests):**
- `Assets/Tests/EditMode/Economy.Tests/ProgressionServiceTests.cs`
- `Assets/Tests/EditMode/Economy.Tests/ChargePipelineTests.cs`

**Modified:**
- `Assets/Veilwalkers/Core/SpendFailureReason.cs` (append `InsufficientCharges = 3`)
- `Assets/Veilwalkers/Core/SpendResult.cs` (doc addendum: charge-consume reuse)
- `Assets/Veilwalkers/Economy/CreditService.cs` (ctor refit to injected `SaveMutationLock`; class-doc update; drop unused `System.Threading` using)
- `Assets/Veilwalkers/App/Bootstrap.cs` (shared lock + provisional `ProgressionRules` + register `IProgressionService`)
- `Assets/Tests/EditMode/Economy.Tests/CreditServiceTests.cs` (new ctor param; null-guard split into two assertions)
- `Assets/Tests/EditMode/Economy.Tests/CreditPipelineTests.cs` (new ctor param in fixture + wiring test)

(Plus the `.meta` file Unity generates for each new `.cs`, committed alongside.)

### Change Log

- 2026-06-12 — Story 1.5 implemented: progression spine (`IProgressionService`/`ProgressionService`, `ChargeType`, `ChargeInventory`, `ProgressionRules`, shared `SaveMutationLock` + sanctioned `CreditService` ctor refit, `InsufficientCharges` reason), Bootstrap wiring, and Economy.Tests additions incl. the AR-20 charge-atomicity test.

### Review Findings

Code review 2026-06-12 (3 adversarial layers: Blind Hunter, Edge Case Hunter, Acceptance Auditor). Acceptance Auditor: no AC violations — all three ACs and every sanctioned constraint satisfied. Blind + Edge independently converged on the pre-persist rollback-window gap (Findings 1–2).

- [x] [Review][Patch] `AddXpAsync`/`AddChargeAsync` mutate-span throws could leave the model partially mutated with no rollback [Assets/Veilwalkers/Economy/ProgressionService.cs] — RESOLVED. The original draft wrote `model.Xp`, the charge counts, and `model.Level` before the persist-`try`, so a `checked` overflow or `SetCount` guard throw escaped with the captured model (== `_saveService.Current`) durably half-mutated. **Resolution (final):** restructured both methods to compute the entire post-state into LOCALS (`newXp`, `newCounts`, `newLevel`) first; all overflow-prone `checked` arithmetic now runs BEFORE any model write, so an overflow throws cleanly as the documented programmer-error (model untouched, nothing to roll back) AND the partial-mutation hazard is eliminated. The model writes happen only after all arithmetic succeeds, inside the rollback `try` that covers the persist fault + recovery-swap. (A first attempt that merely widened the `try` to catch the overflow regressed `AddXp_overflowing_lifetime_xp_throws_and_leaves_state_untouched` — it turned the contractual throw into a `Result.Fail`; the locals-first structure is the correct fix that satisfies both contracts.)

- [x] [Review][Patch] `levelsGained` could go negative; `Level NEVER decreases` was unenforced [Assets/Veilwalkers/Economy/ProgressionService.cs] — RESOLVED. `levelsGained = Math.Max(0, LevelForXp(newXp) - priorLevel)` (clamped ≥ 0) and the committed level is derived as `newLevel = priorLevel + levelsGained`, so a Story-1.6 threshold rebalance that leaves stored Level > `LevelForXp(Xp)` no longer de-levels the player or produces a negative grant (negative `newCounts[i]` is now structurally impossible). Comment corrected. Regression test added: `AddXp_never_decreases_a_stored_level_above_the_xp_derived_level` (seeds Level 2 above the XP-derived level, asserts no decrease / no grant / no event).

- [x] [Review][Patch] Recovery-swap branch of `AddXpAsync`/`AddChargeAsync` was untested [Assets/Tests/EditMode/Economy.Tests/ChargePipelineTests.cs] — RESOLVED. Added `Recovery_swap_mid_addxp_...` and `Recovery_swap_mid_addcharge_...`. Adversarial verification caught a first version as **tautological** (it asserted on `progression.Xp`, which reads the swapped-in recovered model and passes regardless of rollback); fixed to capture the pre-swap model reference (`SaveModel captured = save.Current`, the same object the service rolls back) and assert the restored state directly on it — mutation-tested: deleting the swap-branch `Rollback` now turns both tests red.

- [x] [Review][Defer] No cross-service serialization test for `AddXpAsync` under the shared lock [Assets/Tests/EditMode/Economy.Tests/ChargePipelineTests.cs] — deferred, additive coverage. The shared-lock test pairs a credit spend with a charge consume; `AddXpAsync` (5-field mutation, highest-impact) is not pinned racing another mutator. The lock itself is proven; this is extra coverage, not a defect. Logged to `deferred-work.md`.

**Resolution note (2026-06-12):** All 3 patches applied and adversarially re-verified across three rounds; the headless EditMode gate surfaced one regression from the first patch attempt (overflow contract), corrected via the locals-first restructure above.

**Final gate (2026-06-13):** Headless EditMode suite GREEN — `result="Passed"`, 86/86 (0 failed, 0 compile errors). Suite grew from 85 → 86 (the de-level regression test + the two de-tautologized recovery-swap tests, net of the locals-first restructure leaving the existing overflow test green). Story → `done`; sprint-status synced.
