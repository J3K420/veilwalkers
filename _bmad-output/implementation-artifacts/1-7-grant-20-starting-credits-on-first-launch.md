---
baseline_commit: 8678ac7
---

# Story 1.7: Grant 20 starting Credits on first launch

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a new player,
I want 20 free Credits the first time I open the app,
So that I can start hunting before any paywall.

## Acceptance Criteria

**AC-1 — Fresh install grants exactly 20, persisted, exactly once per install**
**Given** a fresh install with no save file
**When** first launch completes (the grant logic runs after the save loads; onboarding UI is Epic 6)
**Then** the Credit balance reads exactly 20 and is persisted
**And** the grant happens exactly once per install — a save whose `StartingCreditsGranted` marker is set is NOT re-granted on the next launch.
[Source: docs/epics.md#Story 1.7; CLAUDE.md#"20 free credits on first launch"; docs/architecture.md#Economy Model Revision]

**AC-2 — Reinstall / cleared data re-grants (local-only MVP)**
**Given** the app's data is cleared or the app is reinstalled (no save file present)
**When** the app launches fresh again
**Then** 20 Credits are re-granted (no server-side grant-once enforcement in MVP — the marker lives only in the local save, so a wiped save means a fresh grant).
[Source: docs/epics.md#Story 1.7; docs/architecture.md#NFR-6 (local-first); CLAUDE.md#"reinstall re-grants (local-only MVP)"]

**AC-3 — Existing players are NOT re-granted on app update (migration correctness)**
**Given** an existing player whose save predates this story (schema v1, already holding their credits)
**When** the app updates and loads that save
**Then** the save migrates to v2 with `StartingCreditsGranted` set TRUE, so the first-launch grant does NOT fire and their balance is untouched.
[Source: docs/architecture.md#AR-2 (schemaVersion migration path); Assets/Veilwalkers/Persistence/SaveMigrations.cs; derived correctness criterion — adding the marker field must not retroactively re-grant launched players]

> **AC-3 is a derived criterion, not in the epic verbatim.** The epic predates the marker-field decision. It is load-bearing: without the v1→v2 migration setting the marker true, every existing player would receive a surprise +20 on the update that ships this story. This is the single most important test of the story.

## Tasks / Subtasks

- [x] **Task 1 — Add the `StartingCreditsGranted` marker to `SaveModel` (AC: #1, #3)**
  - [x] `Assets/Veilwalkers/Persistence/SaveModel.cs` — add `public bool StartingCreditsGranted { get; set; }` (defaults to `false` for a `new SaveModel()`, i.e. a fresh install). Doc-comment: write-once first-launch marker; set true by `FirstLaunchGrant` after the 20-credit grant (Story 1.7); a v1 save migrates with this true (existing players already launched). It is NOT a collection, so `CoerceNullCollections` does not touch it. [Source: Assets/Veilwalkers/Persistence/SaveModel.cs:18-23 (the doc already names 1.7 as the grant owner); docs/architecture.md#AR-2]
  - [x] Place the property near `Credits` (the field it gates), with the camelCase JSON name `startingCreditsGranted` (Newtonsoft camelCase is already configured in `SaveJson.Settings` — do not annotate per-field).

- [x] **Task 2 — Bump the save schema v1→v2 with a real migration (AC: #3)**
  - [x] `Assets/Veilwalkers/Persistence/SaveMigrations.cs` — set `CurrentVersion = 2`. Add the v1→v2 step at the existing chain extension point (lines 35-37): `if (fromVersion < 2) { MigrateV1ToV2(save); }`. `MigrateV1ToV2(JObject save)` sets `save["startingCreditsGranted"] = true` — **a v1 save belongs to a player who already launched and holds their credits; marking the marker true prevents a surprise re-grant.** The migration runs on the raw `JObject` BEFORE deserialization (confirmed: `LocalProgressStore` calls `SaveMigrations.Migrate(document, version)` at line 173, then deserializes at line 175), so setting the JSON property is the correct mechanism. [Source: Assets/Veilwalkers/Persistence/SaveMigrations.cs:35-38; Assets/Veilwalkers/Persistence/LocalProgressStore.cs:173-175]
  - [x] Keep the unknown-version guard correct: after the bump, `fromVersion > CurrentVersion` (i.e. > 2) still throws `SaveCorruptException`; `fromVersion` 1 and 2 both load. The existing `MigrationRoutingTests` that assert `schemaVersion:999 → corrupt` stay green; the test asserting `schemaVersion:1` loads now ALSO carries `StartingCreditsGranted == true` (update that expectation — see Task 4).
  - [x] Do NOT invent speculative v2→v3 logic. One step, matching the "deliberately boring" contract of the migrations file.

- [x] **Task 3 — Author `FirstLaunchGrant` in App and invoke it after load (AC: #1, #2)**
  - [x] `Assets/Veilwalkers/App/FirstLaunchGrant.cs` — a small App-layer policy type (plain class, constructor-injected `SaveService` + `ICreditService`, NOT a MonoBehaviour, NOT a service-locator reader — App composes it). Public `async Task RunAsync()`:
    1. Read `SaveService.Current`. If null (save corrupt/unloaded), do nothing and return (the recovery flow owns that state; never grant onto a null model).
    2. If `Current.StartingCreditsGranted` is already true → return (idempotent: already granted; this is the not-re-granted-on-next-launch guarantee).
    3. Else: call `ICreditService.GrantCreditsAsync(StartingCredits)` (the existing dumb atomic primitive). On a FAILED `Result`, log `GameLog.Warn` and return WITHOUT setting the marker (so a transient persist failure lets the grant retry next launch — never set the marker for a grant that didn't land).
    4. On success: set `Current.StartingCreditsGranted = true` and persist via `SaveService.SaveAsync()`. **Atomicity note:** `GrantCreditsAsync` already persisted the +20 (it is validate→persist→rollback). The marker write is a SECOND persist. Order matters: grant first (credits land + persist), THEN marker (so a crash between them leaves marker false → at most a re-grant next launch, never credits-without-marker-lost; a re-grant is the lesser evil and is exactly the reinstall behavior). Document this ordering rationale in the method.
  - [x] `StartingCredits` constant: define `public const int StartingCredits = 20;` on `FirstLaunchGrant` (a named constant, NOT an `EconomyConfig` field). **Rationale:** the epic AC says "exactly 20" and the first-launch grant is a one-time onboarding constant, not a balancing knob in the OQ-9 tunable set (EconomyConfig holds costs/XP/curve/caps — see 1.6 — the starting grant is not in that list). A named constant with a sourced doc-comment is correct; do NOT add it to EconomyConfig. [Source: CLAUDE.md#"20 free credits on first launch"; docs/epics.md#Story 1.7 ("exactly 20"); _bmad-output/implementation-artifacts/1-6-…md#scope (EconomyConfig field list)]
  - [x] `Assets/Veilwalkers/App/Bootstrap.cs` — invoke `FirstLaunchGrant` in the post-load path. Today the load is fire-and-forget (`Bootstrap.cs:140`, `InitializeAsync().ContinueWith(onFaulted)`). Chain the grant onto SUCCESSFUL load and keep the existing faulted-only load observer. Concretely: replace the bare `InitializeAsync().ContinueWith(faultObserver)` with a continuation that, on non-faulted completion, runs the grant and observes ITS fault the same way — e.g.
    ```csharp
    GameServices.Get<SaveService>().InitializeAsync().ContinueWith(loadTask =>
    {
        if (loadTask.IsFaulted)
        {
            GameLog.Error("Bootstrap: SaveService.InitializeAsync faulted: " +
                loadTask.Exception?.GetBaseException());
            return;
        }
        // Load succeeded (or the save is Corrupt — FirstLaunchGrant no-ops on a null Current).
        new FirstLaunchGrant(saveService, creditService).RunAsync().ContinueWith(grantTask =>
                GameLog.Warn("Bootstrap: first-launch grant faulted (will retry next launch): " +
                    grantTask.Exception?.GetBaseException()),
            TaskContinuationOptions.OnlyOnFaulted);
    });
    ```
    A grant fault logs `Warn` not `Error` (it is a transient, retried-next-launch condition, not a programmer error — and a `LogError` would fail the UTF test that exercises this path). **Resolution note (CR):** `saveService`/`creditService` are NOT in scope in the continuation (it runs after the wiring `try` closes), so resolve them via `GameServices.Get<>()` — the registered instances, and the safer choice. The shared `SaveMutationLock` is NOT a registered service, so hoist it to a method-scope local (declared before the `try`, assigned inside) and capture it for `FirstLaunchGrant`. Keep construct-before-register + `MarkReady` untouched; the grant runs AFTER `MarkReady`, in this post-load continuation, never inside the wiring `try`. [Source: Assets/Veilwalkers/App/Bootstrap.cs:130-144]

- [x] **Task 4 — Tests (AC: #1, #2, #3)**
  - [x] `Assets/Tests/EditMode/Persistence.Tests/MigrationRoutingTests.cs` — UPDATE the existing `SchemaVersion_1_loads_and_missing_fields_default` test (currently at lines 28-39, asserting `SchemaVersion`/`Credits`/collections): ADD one assertion line `Assert.IsTrue(loaded.StartingCreditsGranted)` — a `{"schemaVersion":1}` save now migrates to v2 with the marker true. ADD a NEW test `Migrating_a_v1_save_with_credits_preserves_them_and_marks_granted`: a crafted v1 save `{"schemaVersion":1,"credits":7}` migrates to v2 with `Credits == 7` preserved AND `StartingCreditsGranted == true` (the existing-player-not-re-granted invariant proven at the migration layer). Note: both assert the marker comes out TRUE because the migration sets it — distinct from the grant-logic idempotency tested in `FirstLaunchGrantTests` (the migration is necessary, the grant-marker check is also necessary; both are tested).
  - [x] `Assets/Tests/EditMode/App.Tests/FirstLaunchGrantTests.cs` (NEW — see Task 5 for the asmdef) — using the `FakeProgressStore` + real `SaveService` + real `CreditService` (the Economy.Tests fixture pattern):
    - **AC-1 fresh install:** fresh model (Credits 0, marker false) → `RunAsync()` → `Balance == 20`, `StartingCreditsGranted == true`, persisted.
    - **Grant-once / next-launch:** after a grant, a SECOND `RunAsync()` (or a reload with marker true) does NOT change the balance (stays 20, not 40).
    - **AC-3 existing player:** a model with marker already true and Credits 7 → `RunAsync()` leaves Credits 7, no grant.
    - **AC-2 reinstall:** a fresh model after a "wipe" (new SaveModel, marker false) grants again — proving the marker, not file history, gates the grant.
    - **Persist-failure safety:** force the grant's persist to fail using `FakeProgressStore`'s existing "next SaveAsync throws then resets" hook (already present — `FakeProgressStore.cs:32`; same mechanism the 1.4/1.5 rollback tests use) → marker stays false, balance unchanged → next `RunAsync()` retries and succeeds. Note the failure can land on either the grant's own persist (inside `GrantCreditsAsync`) or the marker persist; test the grant-persist-fails case (cleanest: nothing granted, marker false) and document that a marker-persist-fail leaves credits granted + marker false → a benign re-grant next launch (the accepted AC-2 behavior).
    - **Null model:** `SaveService.Current == null` (corrupt/unloaded) → `RunAsync()` no-ops, no throw, no grant.
  - [x] Expected/handled failure states log `GameLog.Warn`, not `Error` (UTF fails any test observing a `LogError`, even from async continuations — [[unity-headless-testing]]).

- [x] **Task 5 — Test assembly for App (AC: all) + headless verify**
  - [x] `Assets/Tests/EditMode/App.Tests/` does not exist yet — create it with a `Veilwalkers.App.Tests.asmdef` referencing `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `Veilwalkers.Core`, `Veilwalkers.Persistence`, `Veilwalkers.Economy`, `Veilwalkers.App` (mirror the `Veilwalkers.Economy.Tests.asmdef` shape exactly: `includePlatforms: [Editor]`, `precompiledReferences: [nunit.framework.dll]`, `defineConstraints: [UNITY_INCLUDE_TESTS]`, `autoReferenced: false`, `overrideReferences: true`). **Verified — no `Architecture.Tests` matrix edit needed:** `Veilwalkers.App.asmdef` already exists (confirmed on disk), and the architecture guard's graph loader **excludes `*.Tests` asmdefs** (`AcyclicDependencyTests.LoadVeilwalkersAsmdefGraph`, line 199 `IsTestAssembly` filter; the guard-the-guard test only ranks non-test assemblies). So a new `Veilwalkers.App.Tests` asmdef is invisible to the matrix and the suite stays green without touching it. The production edge App→{Economy,Persistence,Core} the test depends on is already sanctioned in the matrix. [Source: Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs:181-208; Assets/Veilwalkers/App/Veilwalkers.App.asmdef; Assets/Tests/EditMode/Economy.Tests/Veilwalkers.Economy.Tests.asmdef]
  - [x] Run the EditMode suite headless and confirm GREEN ([[unity-headless-testing]]): Editor fully closed, read `test-results.xml` `result="Passed"` AND grep `editor-test.log` for `error CS`, never trust exit 0. Delete `test-results.xml`/`editor-test.log` before committing.

## Dev Notes

### Current state of files this story touches (read in full during story creation)

- **`Assets/Veilwalkers/Economy/ICreditService.cs` (READ, do not modify):** `GrantCreditsAsync(int amount)` already exists and its doc explicitly says "first-launch/pack semantics live with the callers (Stories 1.7, 5.1)" — it is the dumb atomic grant primitive (validate→persist→rollback; `amount <= 0` throws `ArgumentOutOfRangeException`; overflow throws `OverflowException`). 1.7 is its FIRST caller. Do not add first-launch logic to CreditService.
- **`Assets/Veilwalkers/Persistence/SaveModel.cs` (UPDATE):** plain POCO; doc lines 18-23 already name "the 20-credit first-launch grant is Story 1.7 (a fresh model has Credits == 0)". No marker field today — this story adds `StartingCreditsGranted`. `CoerceNullCollections` handles collections only; a bool needs no coercion.
- **`Assets/Veilwalkers/Persistence/SaveMigrations.cs` (UPDATE):** `CurrentVersion = 1`, a v1 no-op with the chain extension point ready at lines 35-37. This story makes it v2 with the first real migration step.
- **`Assets/Veilwalkers/Persistence/LocalProgressStore.cs` (READ, do not modify):** confirms the migration runs on the raw `JObject` at line 173 BEFORE deserialization at line 175 — so the v1→v2 step sets the JSON property and the marker arrives correct in the deserialized `SaveModel`.
- **`Assets/Veilwalkers/Persistence/SaveService.cs` (READ, do not modify):** `InitializeAsync` is load-or-create; `Current` is the loaded model; `SaveAsync()` persists `Current` and faults on failure (the AR-8 rollback hook). The grant fires after this completes.
- **`Assets/Veilwalkers/App/Bootstrap.cs` (UPDATE):** fire-and-forgets `InitializeAsync` (line 140) with a faulted-only continuation; says "no gameplay caller exists yet — do not build staging here." 1.7 adds the post-load `FirstLaunchGrant` invocation in that continuation (after success), NOT inside the wiring `try`.

### Scope boundaries

- **In scope:** the marker field, the v1→v2 migration, `FirstLaunchGrant` + its Bootstrap invocation, and the tests.
- **Explicitly NOT in scope:**
  - *Onboarding UI* (premise cards, camera-disclosure card, the coin-burst animation) — Epic 6 / Story 6.4. This story is the grant LOGIC only; the epic AC says exactly this ("onboarding UI is Epic 6").
  - *Awaited boot staging / `Bootstrap.LoadPhase`* — AR-14, Epic 3/6. The grant rides the existing fire-and-forget continuation; do not build staging here (Bootstrap's own comment says so).
  - *The daily reward (1.8) and ad/telemetry seams (1.9)* — separate stories; their SaveModel fields already exist.
  - *Sourcing 20 from EconomyConfig* — the starting grant is a fixed onboarding constant, not an OQ-9 balancing knob (deviation #3 below).

### Sanctioned-deviation matrix (check before classifying any review finding)

1. **The v1→v2 migration sets `StartingCreditsGranted = true` for existing saves.** This is REQUIRED, not a bug — defaulting it false would re-grant every launched player +20 on update (AC-3). A reviewer flagging "migration sets a 'granted' flag true without granting" has it backwards: the flag means "first-launch grant already handled," and for a pre-existing player it WAS (they launched before the feature existed and already have their credits).
2. **`FirstLaunchGrant` is App-layer policy, not in CreditService.** The `ICreditService` doc mandates this ("first-launch semantics live with the callers"). Putting the policy in App keeps the ledger dumb and honors AR-4/AR-11. A reviewer wanting the logic "consolidated into CreditService" is contradicting the sanctioned seam.
3. **`StartingCredits = 20` is a named constant on `FirstLaunchGrant`, NOT an `EconomyConfig` field.** The epic says "exactly 20"; the starting grant is a one-time onboarding constant outside the OQ-9 tunable set (costs/XP/curve/caps). 1.6's EconomyConfig field list does not include it. Not an AR-16 violation — AR-16 governs the *balancing* economy numbers, and a fixed onboarding grant is not one.
4. **Two persists on first launch (grant, then marker), BOTH lock-held and rollback-safe.** `GrantCreditsAsync` persists the +20 under the shared `SaveMutationLock`; the marker is a second `SaveAsync` ALSO taken under that same lock with the same rollback + recovery-swap guard. This is NOT a violation of AR-8: each write is one atomic lock-held mutate→persist→rollback span, and holding the same lock the credit/progression services use means no concurrent mutator can interleave between them. Ordering (grant before marker) makes any mid-sequence failure safe (worst case: one benign re-grant next launch, never credits-without-marker). **(CR-corrected:** the original implementation left the marker write outside the lock and unguarded — a real AR-8 gap that the code review caught and fixed; this deviation now describes the corrected, lock-held design.)
5. **Grant runs in Bootstrap's existing fire-and-forget post-load continuation, not awaited staging.** Matches Bootstrap's documented "no staging here" stance (AR-14 defers awaited boot phases). A reviewer wanting awaited `LoadPhase` sequencing is asking for AR-14 / Epic 3 work.
6. **`FirstLaunchGrant` no-ops on a null `Current`.** A corrupt/unloaded save must not be granted onto — the recovery flow (RetryLoad/StartFresh) owns that state. Returning silently (not throwing) is correct; the next successful load re-runs the grant path.

### Testing standards

- EditMode NUnit, headless per [[unity-headless-testing]] (Editor closed; verify `test-results.xml` + grep `error CS`; never trust exit 0). Migration tests join `Persistence.Tests`; the grant-policy tests go in a NEW `App.Tests` assembly (Task 5). Expected/handled failures log `GameLog.Warn` (warnings never fail UTF, even from async continuations). Reuse `FakeProgressStore` (Economy.Tests) / `TestSaveFiles` (Persistence.Tests) conventions.

### Project Structure Notes

- New production file: `Assets/Veilwalkers/App/FirstLaunchGrant.cs` (namespace `Veilwalkers.App`). Updated: `SaveModel.cs`, `SaveMigrations.cs`, `Bootstrap.cs`. New test file: `Assets/Tests/EditMode/App.Tests/FirstLaunchGrantTests.cs` + a new `Veilwalkers.App.Tests.asmdef` (App.Tests does not exist yet). **No `Architecture.Tests` matrix edit needed** — verified: the guard excludes `*.Tests` asmdefs (`AcyclicDependencyTests.cs:181-208`), and `Veilwalkers.App.asmdef` already exists. The migration field/version change does not alter the assembly graph at all.

### References

- [Source: docs/epics.md#Story 1.7] — the two ACs (fresh-install grant-once; reinstall re-grants).
- [Source: CLAUDE.md#"20 free credits on first launch"] — the canon amount + reinstall-regrants rule.
- [Source: Assets/Veilwalkers/Economy/ICreditService.cs:55-64] — `GrantCreditsAsync` exists; "first-launch semantics live with the callers (1.7, 5.1)".
- [Source: Assets/Veilwalkers/Persistence/SaveModel.cs:18-23] — schema doc names 1.7 as grant owner; fresh model Credits==0.
- [Source: Assets/Veilwalkers/Persistence/SaveMigrations.cs:35-38] — the v1→v2 chain extension point.
- [Source: Assets/Veilwalkers/Persistence/LocalProgressStore.cs:173-175] — migration runs on raw JObject before deserialization.
- [Source: Assets/Veilwalkers/App/Bootstrap.cs:130-144] — the fire-and-forget post-load continuation the grant rides.
- [Source: docs/architecture.md#AR-2, AR-4, AR-8, AR-11] — schema migration, service-layer composition, atomic writes, services-don't-drive-UI.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Code dev-story, via story-automator Phase DS)

### Debug Log References

- Headless EditMode gate (Editor closed, no lockfile). First run: exit 2, `result="Failed(Child)"`, 98/99 — the `A_failed_grant_persist_leaves_it_retryable` test failed on an UNEXPECTED `LogType.Error` (`SaveService: save failed` from the forced persist fault — the documented UTF trap). Fix: declared the two expected errors via `LogAssert.Expect(LogType.Error, ...)` for `SaveService: save failed` and `CreditService: grant of 20 rolled back` (the established 1.4 rollback-test pattern). Second run: `result="Passed"`, 99/99, 0 `error CS`. Suite grew 92 → 99 (+6 FirstLaunchGrant tests, +1 new migration test; the existing migration test was updated in place). `test-results.xml`/`editor-test.log` deleted before handoff.

### Completion Notes List

- **Task 1 — `SaveModel.StartingCreditsGranted` (bool, default false):** added next to `Credits` with a doc-comment covering the write-once first-launch + migrate-true-for-existing semantics. Not a collection, so `CoerceNullCollections` is untouched. Serializes camelCase (`startingCreditsGranted`) via the existing `SaveJson` settings.
- **Task 2 — schema v1→v2:** `SaveMigrations.CurrentVersion = 2`; added `MigrateV1ToV2(JObject)` at the chain extension point, setting `save["startingCreditsGranted"] = true`. Runs on the raw JObject before deserialization (`LocalProgressStore:173`). The unknown-version guard now admits 1 and 2, still rejects >2.
- **Task 3 — `FirstLaunchGrant` (App):** new plain class (ctor-injected `SaveService` + `ICreditService`, null-guarded). `RunAsync()`: no-op on null `Current` or marker-already-true; else `GrantCreditsAsync(20)`, and only on success set the marker + `SaveAsync()`. Grant-then-marker ordering with the documented crash-safety rationale; a failed grant logs `Warn` and leaves the marker false (retry next launch). `StartingCredits = 20` is a named const, not an EconomyConfig field (deviation #3). Bootstrap's post-load continuation now chains the grant onto a non-faulted load (corrupt-but-not-faulted is safe — the grant no-ops on null), with a `Warn` fault observer for the grant. Resolved `SaveService`/`ICreditService` via `GameServices.Get<>()` (the locals are out of scope after the wiring `try`).
- **Tasks 4+5 — tests + App.Tests assembly:** new `Veilwalkers.App.Tests` asmdef (mirrors Economy.Tests; no architecture-matrix edit — the guard excludes `*.Tests`). `FirstLaunchGrantTests` (6 tests via a local full-model in-memory store + real SaveService/CreditService): fresh-grant-once, run-twice-no-re-grant, already-granted-untouched, reinstall-regrants, failed-persist-retryable (with `LogAssert`), null-model no-op. `MigrationRoutingTests`: existing test now asserts the migrated marker is true + a new credits-preserved-and-marked test.
- **Note — App.Tests needed its own fake store:** the Economy.Tests `FakeProgressStore` is `internal` to that assembly AND its clone predates (so drops) the new marker; a local `InMemoryStore` that round-trips the full model was the clean fix rather than widening the Economy fake's visibility.

### File List

- `Assets/Veilwalkers/Persistence/SaveModel.cs` (MODIFIED — `StartingCreditsGranted` marker)
- `Assets/Veilwalkers/Persistence/SaveMigrations.cs` (MODIFIED — v1→v2 migration, CurrentVersion=2)
- `Assets/Veilwalkers/App/FirstLaunchGrant.cs` (NEW) + `.meta`
- `Assets/Veilwalkers/App/Bootstrap.cs` (MODIFIED — post-load grant invocation)
- `Assets/Tests/EditMode/App.Tests/Veilwalkers.App.Tests.asmdef` (NEW) + `.meta`
- `Assets/Tests/EditMode/App.Tests/FirstLaunchGrantTests.cs` (NEW) + `.meta`
- `Assets/Tests/EditMode/App.Tests.meta` (NEW — folder)
- `Assets/Tests/EditMode/Persistence.Tests/MigrationRoutingTests.cs` (MODIFIED — marker assertions)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (MODIFIED — status transitions)

### Change Log

- 2026-06-13 — Story 1.7 implemented: `StartingCreditsGranted` marker + v1→v2 migration (sets marker true for existing saves), `FirstLaunchGrant` App-policy granting 20 once via the existing `GrantCreditsAsync`, Bootstrap post-load invocation, new App.Tests assembly. Headless 99/99 green (one transient LogAssert fix on the persist-failure test).
- 2026-06-13 — Code review: 6 patches applied (the marker write is now lock-held + rollback-safe under the shared `SaveMutationLock`, closing a real AR-8 serialization gap + the unguarded-throw + TOCTOU-comment findings), 1 deferral. Suite re-verified GREEN 100/100 (+1 marker-persist-rollback test).

### Review Findings

Code review 2026-06-13 (3 adversarial layers; 14 raw findings → verify-disputed-first triage: 6 patch, 1 defer, 4 spec-sanctioned, 3 false-positive). This review found a real correctness gap the create/validate/dev passes missed — the marker write escaped the economy's `SaveMutationLock` and was not rollback-safe.

- [x] [Review][Patch] Marker write not serialized under `SaveMutationLock` (AR-8 gap) + unguarded throw + over-claimed TOCTOU comment + doc overstated symmetry [Assets/Veilwalkers/App/FirstLaunchGrant.cs] — RESOLVED as one coherent fix. `FirstLaunchGrant` now takes the shared `SaveMutationLock` (injected by Bootstrap — the same instance the credit/progression services use) and wraps the marker write in the established `WaitAsync → set → try-persist → rollback-on-fault-or-recovery-swap → release` pattern, mirroring `GrantCreditsAsync` exactly (incl. the `ReferenceEquals(Current, model)` swap check). This closes the interleave window (a concurrent mutator can no longer slip between the grant and marker persists), makes the marker persist rollback-safe (a throw rolls the in-memory marker back to false so memory matches disk, then logs `Warn` and retries next launch rather than faulting), and the null/swap re-read is now a real guard, not just a comment. Doc-comment rewritten to describe the lock-held two-write contract accurately. New test `A_failed_marker_persist_rolls_the_marker_back_and_retries` pins the rollback (and documents the one benign re-grant the AC-2 tradeoff accepts).

- [x] [Review][Patch] Test `InMemoryStore.Clone` claimed "whole model" but copies scalars only [Assets/Tests/EditMode/App.Tests/FirstLaunchGrantTests.cs] — RESOLVED. Reworded the class + `Clone` docs to the sanctioned "scalar fidelity" stance (matching the Economy.Tests `FakeProgressStore` — no test here mutates collections, and a shallow share would re-introduce aliasing). The marker IS in the scalar set, so the grant tests round-trip it faithfully.

- [x] [Review][Patch] Bootstrap used `GameServices.Get<>()` where the story said "capture locals" [Assets/Veilwalkers/App/Bootstrap.cs] — RESOLVED by correcting the STORY, not the code: the wiring locals are out of scope in the post-load continuation (it runs after the wiring `try`), and `Get<>` is the safer resolution (the reviewer agreed). The one thing that genuinely had to be captured — the un-registered `SaveMutationLock` — is now hoisted to method scope and handed to `FirstLaunchGrant`. Task 3's instruction updated to match.

- [x] [Review][Defer] No re-entrancy guard on `FirstLaunchGrant.RunAsync` [Assets/Veilwalkers/App/FirstLaunchGrant.cs] — deferred, out-of-scope. Only one caller exists (Bootstrap's once-per-boot post-load continuation); concurrent invocation is not reachable today. A re-entrancy/in-flight guard belongs with the AR-14 awaited-boot-staging work (or whenever a second caller appears). Logged to `deferred-work.md`.

**Final gate (2026-06-13):** Headless EditMode suite GREEN — `result="Passed"`, 100/100 (0 failed, 0 compile errors), stable across the post-patch re-run (one LogAssert fix on the new marker-rollback test). Story → `done`; sprint-status synced.
