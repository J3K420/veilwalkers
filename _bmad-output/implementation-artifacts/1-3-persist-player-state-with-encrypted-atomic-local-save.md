---
baseline_commit: 6409621
---

# Story 1.3: Persist player state with encrypted, atomic local save

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want my progress saved reliably on my device,
So that closing the app never loses my Credits or collection.

## Acceptance Criteria

**AC-1 — `SaveModel` schema, serialization, and the `IProgressStore` seam**
**Given** the persistence layer
**When** `SaveModel` is defined and serialized
**Then** it is JSON (Newtonsoft, **camelCase**) **AES-encrypted**, with a top-level integer `schemaVersion`
**And** it holds: credit balance (integer), codex (discovered IDs + per-monster scan/capture/slay/variant flags), XP + level, per-extra charge counts, encounterSnapshot, pending-purchase records, dailyClaim date, adReward {grantsToday, dayBucket}, firstZeroCreditDay
**And** all reads/writes go through the **`IProgressStore`** interface (`LocalProgressStore` implementation).
[Source: docs/epics.md#Story 1.3; docs/epics.md#AR-2; docs/architecture.md#Data Architecture]

**AC-2 — Atomic, async write that survives a mid-write kill**
**Given** a save operation
**When** `SaveAsync` writes
**Then** it writes **atomically (temp file + swap)** to `Application.persistentDataPath` **off the main thread**
**And** a process kill mid-write leaves the prior valid save intact.
[Source: docs/epics.md#Story 1.3; docs/epics.md#AR-2; docs/architecture.md#Data Architecture]

**AC-3 — Corrupt/tampered save throws and surfaces recovery; null collections never persisted**
**Given** a corrupt or tampered save file
**When** the app loads it
**Then** load **throws (never silently wipes)** and surfaces a player-facing recovery choice (retry / start fresh)
**And** null collections are never persisted (default to empty arrays, validated on load).
[Source: docs/epics.md#Story 1.3; docs/epics.md#AR-20 (save tamper test); docs/architecture.md#Edge Cases & Failure Handling]

## Tasks / Subtasks

- [ ] **Task 1 — Define `SaveModel` + the snapshot/pending-purchase data shapes (AC: #1, #3)**
  - [ ] `Persistence/SaveModel.cs` — plain POCO (NOT a ScriptableObject, NOT a MonoBehaviour) with a top-level `int schemaVersion` (start at `1`) and exactly the AR-2 contents: `int credits`; codex state (discovered monster IDs + per-monster flags `scanned/captured/slain` + variant flags, keyed by stable string id `monNN`); `int xp` + `int level`; per-extra charge counts (Strong Capture / Stability Boost / Nightveil Filter — three named `int` fields or a serializable map); `encounterSnapshot`; pending-purchase records; `dailyClaim` (ISO-8601 UTC date string); `adReward { int grantsToday; long dayBucket; }`; `firstZeroCreditDay` (write-once scalar — use a `long` day-bucket, `-1`/sentinel = unset). [Source: docs/epics.md#AR-2; docs/architecture.md#Data & Serialization Formats]
  - [ ] **Tier rule for the snapshot:** `EncounterSnapshot.cs` (the behavior-bearing type) lives in **Encounter** and arrives in Epic 4 — Persistence must NEVER reference Encounter (forbidden upward edge). Define a serializable **data-only** shape Persistence can own now: e.g. `Persistence/EncounterSnapshotData.cs` holding `AnchorToken[]` (from `Veilwalkers.Core.Contracts` — this is exactly why AnchorToken lives below AR) + a list of per-monster state entries (monster id, scan progress, applied boosts — minimal fields now; Epic 4/5 extend it). Default = null/absent meaning "no active encounter". [Source: docs/architecture.md#Architectural Boundaries ("Shared contracts below AR"); docs/architecture.md#Complete Project Directory Structure]
  - [ ] `Persistence/PendingPurchaseRecord.cs` — minimal serializable record `{ string orderId; string packId; string state; string isoTimestampUtc }`; the reconciler logic that uses it is Story 5.2 — only the persisted shape lands here. [Source: docs/epics.md#AR-12; docs/epics.md#Story 5.2]
  - [ ] **Null-collection rule:** every collection field initializes to an empty collection, and load-validation coerces any null collection to empty (never persist null; never return a model with null collections). One public type per file. [Source: docs/architecture.md#Data & Serialization Formats; docs/architecture.md#File & Project Structure Patterns]

- [ ] **Task 2 — Implement `AesCrypto` with integrity (tamper → throw) (AC: #1, #3)**
  - [ ] `Persistence/AesCrypto.cs` — AES (CBC) encrypt/decrypt of the JSON payload with a **random IV per write, prepended to the file**, plus an integrity check so tampering reliably throws: **encrypt-then-MAC** with `HMACSHA256` appended (or prepended) to the ciphertext; on decrypt, verify the MAC FIRST and throw a typed exception on mismatch. (CBC padding errors alone do NOT reliably detect tampering — the MAC is what makes AC-3 testable and deterministic.) Use `System.Security.Cryptography` (`Aes.Create()`, `HMACSHA256`) — supported on Unity 2022.3 IL2CPP/Android. [Source: docs/epics.md#AR-2; docs/architecture.md#Authentication & Security]
  - [ ] Key material: derive the AES + HMAC keys deterministically from an embedded app secret via `Rfc2898DeriveBytes` (fixed salt, e.g. 32+32 bytes from one derivation). This **raises the bar against casual save-editing only** — the architecture explicitly accepts it is not cheat-proof (client-side, single-player MVP). Do NOT use `SystemInfo.deviceUniqueIdentifier` (main-thread-only API, and it would brick saves across device transfers). [Source: docs/architecture.md#Authentication & Security ("acknowledged as not cheat-proof")]
  - [ ] Define the typed failure: `Persistence/SaveCorruptException.cs` (thrown on MAC mismatch, decrypt failure, JSON parse failure, or schema validation failure). Load must throw THIS, never return null and never silently wipe. [Source: docs/epics.md#Story 1.3; docs/architecture.md#Edge Cases & Failure Handling]

- [ ] **Task 3 — Author `IProgressStore` + `LocalProgressStore` with atomic temp+swap write (AC: #1, #2)**
  - [ ] `Persistence/IProgressStore.cs` — the backend seam (Local now → Firebase later; do not leak file paths or crypto into the interface): `Task<SaveModel> LoadAsync()`, `Task SaveAsync(SaveModel model)`, `bool Exists()`, `Task DeleteAsync()` (Delete enables the "start fresh" recovery choice). [Source: docs/epics.md#AR-2; docs/architecture.md#Data Architecture ("Backend seam")]
  - [ ] `Persistence/LocalProgressStore.cs` — implements `IProgressStore` over a single save file. **Take the storage directory as a constructor parameter** (Bootstrap passes `Application.persistentDataPath`); never call `Application.persistentDataPath` inside the store — it is a main-thread-only Unity API and the ctor param is also what makes the store testable against a temp directory. [Source: docs/architecture.md#Data Architecture]
  - [ ] **Atomic write pipeline (the heart of AC-2):** serialize (Newtonsoft, camelCase via `CamelCasePropertyNamesContractResolver`) → encrypt+MAC → write to a **temp file in the same directory** (`save.dat.tmp`) → flush to disk (`FileStream.Flush(flushToDisk: true)`) → swap into place over `save.dat` (`File.Replace`, falling back to delete+`File.Move` only if no destination exists yet). Same-directory temp guarantees same volume, so the swap is an atomic rename. A kill before the swap leaves the prior `save.dat` untouched; a kill after is the new valid save. Clean up any stale `.tmp` on startup/load. [Source: docs/epics.md#AR-2; docs/architecture.md#Data Architecture ("atomic write (temp file + swap)")]
  - [ ] **Off the main thread:** run serialize+encrypt+IO inside `Task.Run` (JsonConvert and the crypto APIs are thread-safe; no UnityEngine API may be touched inside). Serialize concurrent `SaveAsync` calls (e.g. a `SemaphoreSlim(1,1)`) so two writers can never interleave on the temp file. [Source: docs/epics.md#AR-2; docs/architecture.md#Process Patterns ("No silent main-thread blocking")]
  - [ ] **Validate on load:** after decrypt+parse, check `schemaVersion` is a known version (route through `SaveMigrations`), coerce null collections to empty, and reject structurally-invalid models with `SaveCorruptException`. [Source: docs/epics.md#AR-2; docs/architecture.md#Data & Serialization Formats]

- [ ] **Task 4 — Author `SaveService` (app-facing owner of the loaded model + AR-19 status surface) (AC: #2, #3)**
  - [ ] `Persistence/SaveService.cs` — owns the single in-memory `SaveModel` for the session and is the app-facing API; it delegates raw persistence to the injected `IProgressStore` (constructor injection — Persistence is a pure-logic area; it must NOT call `GameServices.Get<T>()`). Responsibilities: `InitializeAsync()` (load-or-create: no file → fresh `SaveModel` with defaults — the 20-credit FIRST-LAUNCH GRANT is Story 1.7, NOT here; fresh model has `credits = 0`), `Current` (the loaded model), `SaveAsync()` (persist `Current`). [Source: docs/architecture.md#Complete Project Directory Structure; docs/architecture.md#Architectural Boundaries ("locator rule"); docs/epics.md#Story 1.7]
  - [ ] **AR-19 status surface:** expose an explicit status enum (e.g. `SaveStatus { Idle, Loading, Saving, Corrupt, Failed }`) + start/finish C# events (e.g. `OnSaveStarted`, `OnSaveCompleted`, `OnLoadFailed`) so UI can reflect long ops; log via `GameLog` (never raw `Debug.Log`). [Source: docs/epics.md#AR-19; docs/architecture.md#Process Patterns]
  - [ ] **Corrupt-load recovery (AC-3):** when `IProgressStore.LoadAsync` throws `SaveCorruptException`, `SaveService` enters `Corrupt` status, raises the failure event carrying the detail, and exposes the recovery API: `RetryLoadAsync()` (re-attempt the load) and `StartFreshAsync()` (delete via `IProgressStore.DeleteAsync`, create defaults, persist). It must NEVER auto-wipe — the wipe happens only through the explicit `StartFreshAsync` call. The actual recovery dialog UI is Epic 6; this story delivers the complete mechanism + events the UI will bind to, proven by tests. [Source: docs/epics.md#Story 1.3; docs/architecture.md#Edge Cases & Failure Handling ("recovery path is explicit, not a dead end")]
  - [ ] Note for later stories (do NOT build now): the one-atomic-write-per-player-action rule (AR-8) is consumed by 1.4/1.5 — they mutate `Current` then call `SaveAsync()`, rolling back the in-memory mutation on persist failure. Make sure `SaveAsync` surfaces failure as an awaitable fault (exception or failed `Result`) so callers CAN roll back. [Source: docs/epics.md#AR-8]

- [ ] **Task 5 — `SaveMigrations` v1 baseline (AC: #1)**
  - [ ] `Persistence/SaveMigrations.cs` — the migration path stub: current version constant (`1`), `Migrate(jObjectOrModel, fromVersion)` that is a no-op for v1, throws `SaveCorruptException` for unknown/newer versions, and is the single place future versions chain (v1→v2→…). Keep it boring; its existence + routing is the AC, not speculative migrations. [Source: docs/epics.md#AR-2 ("migration path (schemaVersion)"); docs/architecture.md#Data Architecture]

- [ ] **Task 6 — Wire Bootstrap for real + the deferred runtime-lifecycle items (AC: #1)**
  - [ ] Register the first real services in `Bootstrap.WireServices()`: `GameServices.Register<IClock>(new SystemClock())`, `Register<IProgressStore>(new LocalProgressStore(Application.persistentDataPath))`, `Register<SaveService>(...)` (inject the store via constructor), then `MarkReady()`. Kick `SaveService.InitializeAsync()` after sealing. [Source: deferred-work.md#Bootstrap runtime lifecycle; docs/epics.md#AR-4]
  - [ ] **Scene placement (deferred from 1.2 review, owner = this story):** create `Assets/Veilwalkers/Scenes/Bootstrap.unity` (or place under `Assets/Scenes/` beside the template scene) containing a `Bootstrap` GameObject, and add it as **scene 0** in Build Settings (`EditorBuildSettings.scenes` is currently EMPTY — nothing is in the build today). The architecture's rule: a single Bootstrap entry scene wires GameServices first, then loads Home (Home itself is Epic 6 — for now Bootstrap scene may simply stay, or load `SampleScene`). [Source: deferred-work.md; docs/architecture.md#Edge Cases & Failure Handling ("Services-not-ready race")]
  - [ ] **Execution-order guarantee:** add `[DefaultExecutionOrder(-1000)]` (or wire via `RuntimeInitializeOnLoadMethod`) so no other `Awake` can race the composition root. [Source: deferred-work.md#Bootstrap runtime lifecycle]
  - [ ] **Domain-reload-disabled reset:** `EditorSettings.asset` already has `m_EnterPlayModeOptions: 3` (domain reload disabled) — add a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` static reset so `GameServices` statics return to unready on each Editor play. (`ResetForTests()` is `#if UNITY_INCLUDE_TESTS` — the reset method lives in App or uses a guarded path; do NOT make `ResetForTests` public-in-player again.) [Source: deferred-work.md#Bootstrap runtime lifecycle; Assets/Veilwalkers/Core/GameServices.cs]
  - [ ] **Mid-wiring failure recovery:** today a `Register` throw mid-wiring leaves the locator poisoned (not ready, but re-running dies on duplicate registration). Make `WireServices` failure-safe: catch, log via `GameLog.Error`, and leave a recoverable state (e.g. reset-and-rethrow, or an idempotent wiring guard) — document the chosen approach in code. [Source: deferred-work.md#Bootstrap runtime lifecycle]

- [ ] **Task 7 — Tests: `Persistence.Tests` (EditMode) + the AR-20 tamper test (AC: #1, #2, #3)**
  - [ ] Create `Assets/Tests/EditMode/Persistence.Tests/` with `Veilwalkers.Persistence.Tests.asmdef` (Editor-only, references `Veilwalkers.Persistence` + `Veilwalkers.Core`, UTF + `nunit.framework.dll` per the Architecture.Tests asmdef pattern). The name MUST end in `.Tests` — the architecture-guard loader excludes test asmdefs by that suffix, so no allowed-edge-matrix edit is needed (verify all Architecture.Tests still pass). [Source: docs/architecture.md#File & Project Structure Patterns; Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs]
  - [ ] **Test against a temp directory** (e.g. `Path.Combine(Path.GetTempPath(), "veilwalkers-tests", TestContext id)`) via the `LocalProgressStore` ctor param — never `Application.persistentDataPath`. Clean up in teardown. UTF **1.1.33 does not support `async Task` test methods** — block with `task.GetAwaiter().GetResult()` inside `[Test]` methods (acceptable in EditMode tests).
  - [ ] Round-trip: save a fully-populated `SaveModel` → load → all fields equal (proves camelCase JSON + encrypt/decrypt + schemaVersion). Also: file on disk is NOT plaintext JSON (assert the raw bytes don't contain a known field name).
  - [ ] Atomicity: (a) with a valid `save.dat` present, simulate a mid-write kill by making the NEXT write fail before swap (e.g. injectable/abortable stream or pre-created locked temp) OR by directly verifying the pipeline order (temp written + flushed before any touch of `save.dat`) — then assert the original loads intact; (b) a stale `.tmp` left on disk is cleaned and never loaded as the save.
  - [ ] **AR-20 tamper test (load-bearing):** corrupt the file (flip ciphertext bytes; also truncate) → `LoadAsync` throws `SaveCorruptException` → `SaveService` reaches `Corrupt` status and the original file is UNCHANGED (never silently wiped) → `StartFreshAsync()` recovers to a working default save; `RetryLoadAsync()` on an uncorrupted file recovers too. [Source: docs/epics.md#AR-20]
  - [ ] Null-collection validation: hand-craft a save whose JSON has `null` for a collection → load returns empty collections, never null; saving a model with a null collection persists `[]`.
  - [ ] Migration routing: `schemaVersion: 1` loads; unknown version (e.g. `999`) throws `SaveCorruptException`.
  - [ ] Run the FULL EditMode suite headless and confirm green (Architecture.Tests 13 + new Persistence.Tests). Headless command + the two traps (open-Editor lock, crash-handler fake exit 0) are in Dev Notes → Testing Requirements.

- [ ] **Task 8 — Verify, update story/sprint records, and commit**
  - [ ] Zero compile errors on clean reopen; full EditMode suite green; `test-results.xml`/`editor-test.log` deleted before commit (they are NOT gitignored).
  - [ ] All new `.cs`, `.asmdef`, `.unity`, and `.meta` files tracked; no secrets.
  - [ ] Commit + push (convention: `Story 1.3: <what>`), update sprint-status. [Source: CLAUDE.md#Working conventions]

## Dev Notes

### What this story IS (and is NOT)

This story builds the **entire persistence layer**: `SaveModel` (the versioned, encrypted schema), `AesCrypto`, the `IProgressStore` seam + `LocalProgressStore` (atomic temp+swap, off-main-thread), `SaveService` (app-facing owner + AR-19 status/events + corrupt-recovery mechanism), `SaveMigrations`, and the first REAL Bootstrap wiring (including the runtime-lifecycle items deferred from the 1.2 review). It is the foundation every later story persists through.

**DO NOT build here (later stories own these):**
- `CreditService` / spend pipeline / `OnCreditsChanged` → **Story 1.4** (it adds the `Economy → Persistence` asmdef edge AND the matching allowed-edge-matrix entry in `AcyclicDependencyTests` — do not pre-add either).
- `ProgressionService` / `ChargeInventory` / charge-atomicity test → **Story 1.5**. `EconomyConfig` → **Story 1.6**.
- The 20-credit first-launch grant → **Story 1.7** (a fresh `SaveModel` here has `credits = 0`; 1.7 adds the grant + "granted" marker logic).
- Daily-reward claim logic → **Story 1.8** (the `dailyClaim` FIELD lands now; the rule does not).
- `LocalTelemetryStore` / `ITelemetryStore` / `telemetry.json` → **Story 1.9** (telemetry is a SEPARATE file by design — do not add telemetry counters to `SaveModel`).
- `EncounterSnapshot` behavior / snapshot-rehydrate → **Epic 4/5** (only the serializable DATA shape lands now).
- Recovery dialog UI → **Epic 6** (this story delivers the mechanism: status, events, `RetryLoadAsync`/`StartFreshAsync`).

### Design decisions a dev agent must NOT re-litigate

1. **`SaveService` vs `IProgressStore` split:** `IProgressStore`/`LocalProgressStore` = the raw, swappable persistence seam (Load/Save/Exists/Delete of a `SaveModel`; file/crypto details hidden behind it; Firebase replaces it in Phase 2). `SaveService` = the app-facing layer that OWNS the in-memory `SaveModel`, exposes AR-19 status/events, and is what Bootstrap registers + later services receive by constructor injection. Both files are explicitly listed in the architecture's directory tree. [Source: docs/architecture.md#Complete Project Directory Structure]
2. **The snapshot tier problem:** the architecture stores `encounterSnapshot (AnchorTokens)` in `SaveModel`, but `EncounterSnapshot.cs` lives in Encounter (tier above Persistence). Persistence must NOT reference Encounter. Resolution: a Persistence-owned serializable DTO (`EncounterSnapshotData`) holding `AnchorToken[]` (Core/Contracts — placed below AR for exactly this reason) + minimal per-monster state entries; Epic 4's `EncounterSnapshot` maps to/from it. Adding a `Persistence → Encounter` reference would fail `Every_Veilwalkers_reference_is_on_the_allowed_edge_list`. [Source: docs/architecture.md#Architectural Boundaries; Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs]
3. **Encrypt-then-MAC, not bare AES-CBC:** AC-3 requires tampering to deterministically THROW. Bare CBC only throws on (some) padding corruption; an HMAC over the ciphertext makes every byte-flip detectable. This is still only a casual-tampering bar (key ships in the client) — the architecture accepts that. [Source: docs/architecture.md#Authentication & Security]
4. **No new production asmdef edges:** `Veilwalkers.Persistence` already references `Veilwalkers.Core` (verified on disk) — Newtonsoft (`com.unity.nuget.newtonsoft-json` 3.2.1, already pinned) is a precompiled auto-referenced package needing NO asmdef change. The allowed-edge matrix in `AcyclicDependencyTests` needs NO edit this story. [Source: Assets/Veilwalkers/Persistence/Veilwalkers.Persistence.asmdef; Packages/manifest.json]
5. **Dates/IDs format:** dates = ISO-8601 UTC strings; day-buckets = `long` integers derived via `IClock` (never a serialized `DateTime` for bucket fields); IDs = strings; currency = `int`. [Source: docs/architecture.md#Data & Serialization Formats; docs/epics.md#AR-17]

### Current repo state (verified on disk at baseline 6409621)

- Story 1.2 is done: all 9 area asmdefs exist with the one-way graph; `Veilwalkers.Persistence` compiles as an EMPTY assembly ("no scripts" Editor notice is expected and disappears when this story adds .cs files).
- **Core types ready to consume:** `GameServices` (thread-safe, wire-once, `ResetForTests` gated behind `UNITY_INCLUDE_TESTS`), `ServicesNotReadyException`, `IClock`/`SystemClock` (`DateTime UtcNow`), `GameLog` (Debug/Info/Warn/Error), `Result` (Ok/Fail + never-null Message), `SpendResult`/`SpendFailureReason`, `Core/Contracts/AnchorToken` (`[Serializable]`, string trackableId + UnityEngine pose math), `AnchorRestoreResult`.
- **`Bootstrap.cs` is a near-empty shell in NO scene** — it already guards re-entry via `GameServices.IsReady` and has the commented example `Register<IClock>(new SystemClock())`. This story puts it in a scene and registers the first real services. [Source: Assets/Veilwalkers/App/Bootstrap.cs]
- `Assets/Scenes/` contains only the template `SampleScene.unity`; **`EditorBuildSettings.scenes` is empty** (no scene is in the build at all).
- `EditorSettings.asset` has `m_EnterPlayModeOptions: 3` (domain reload disabled) — currently inert because nothing runs at runtime; becomes live the moment Bootstrap enters a scene (hence the static-reset task).
- Packages pinned: `com.unity.nuget.newtonsoft-json` **3.2.1**, `com.unity.test-framework` **1.1.33**. Unity Editor **2022.3.62f3**.
- `Assets/Tests/EditMode/Architecture.Tests/` exists with 13 green tests; its loader excludes any asmdef whose name ENDS with `.Tests`, so the new `Veilwalkers.Persistence.Tests` is automatically exempt from the graph matrix.

### Deferred work this story MUST absorb (from 1.2 code review — owner: Story 1.3)

> From `_bmad-output/implementation-artifacts/deferred-work.md`:

- **Bootstrap runtime lifecycle** — scene placement, execution-order guarantee (`[DefaultExecutionOrder]` / `RuntimeInitializeOnLoadMethod`), domain-reload-disabled static reset (`SubsystemRegistration`), and mid-wiring-failure recovery (today a failed `Register` poisons the locator). All four are Task 6 subtasks.

NOT this story's deferrals (do not touch): `AnchorToken` validity helper (→ 3.5); EDM4U template/artifact commit-vs-ignore policy, target SDK pin, bundle ID, .aab toggle (→ AR-21).

### Threading & Unity API constraints (trap list)

- `Application.persistentDataPath` is **main-thread-only** → read it in Bootstrap (main thread) and pass into `LocalProgressStore`'s constructor. No UnityEngine API inside `Task.Run`.
- `JsonConvert` + `System.Security.Cryptography` are thread-safe for per-call use; create crypto primitives per operation (or pool carefully), never share a single `Aes` instance across threads.
- `File.Replace` requires the destination to exist — first-ever save must branch to `File.Move`. Both are same-volume atomic renames when temp lives in the same directory.
- `FileStream.Flush(true)` (flushToDisk) before the swap — without it the rename can land before the data hits disk and a power-loss truncates the "new" file.
- Newtonsoft on IL2CPP: `SaveModel` is a concrete, directly-referenced POCO so reflection serialization survives stripping in practice; if a device build ever strips members, the fix is a `link.xml`/`[Preserve]` — that verification belongs to the AR-21 device build gate, not this story's EditMode scope.
- Async in UTF 1.1.33: no `async Task` `[Test]` support — call `.GetAwaiter().GetResult()`.

### Testing Requirements

- New surface: `Assets/Tests/EditMode/Persistence.Tests/` (own Editor-only asmdef, mirrors the Architecture.Tests asmdef shape: `includePlatforms: ["Editor"]`, UTF references + `"precompiledReferences": ["nunit.framework.dll"]`, `"overrideReferences": true`). In scope: round-trip, not-plaintext, atomic-swap survival, stale-tmp cleanup, **AR-20 tamper test** (corrupt → `SaveCorruptException` → never-wipe → explicit recovery works), null-collection coercion, migration routing, and the Bootstrap/SaveService wiring happy path (use `GameServices.ResetForTests()` for isolation — available under `UNITY_INCLUDE_TESTS`).
- Explicitly NOT in scope: charge-atomicity persist-failure test (Story 1.5 — though designing `IProgressStore` as an interface makes the throwing-fake trivial then), `MonsterDatabase.Count == 67` (Epic 2), device/IL2CPP build verification (AR-21 release gate).
- **Headless run (how 1.1/1.2 validated):**
  `"/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode -projectPath "C:/Users/james/Desktop/Veilwalkers" -runTests -testPlatform EditMode -testResults "<abs>/test-results.xml" -logFile "<abs>/editor-test.log" -nographics`
  **Two traps, both fake an exit 0:** (1) project open in the Editor → `HandleProjectAlreadyOpenInAnotherInstance`, no XML written — `Temp/UnityLockfile` existing = locked; ask James to close the Editor. (2) compile error → no XML, crash handler exits 0 — never trust exit 0; confirm `test-results.xml` exists AND grep the log for `error CS`. Delete `test-results.xml`/`editor-test.log` before committing (not gitignored).

### Previous Story Intelligence (from Story 1.2)

- **Review-proven patterns to repeat:** guard `default(struct)` invariants (the `Result.Message` getter-coercion pattern); doc-comments must match actual thrown exception types (a 1.2 review finding); add the negative-path tests up front (null/duplicate/double-call guards were flagged as untested in review — write `SaveService`/`LocalProgressStore` failure-path tests in the first pass, not after review).
- **Thread-safety precedent:** `GameServices` got a lock because Play Billing callbacks arrive on a binder thread (Epic 5). `SaveService` will be called from async contexts — make its status/`Current` access thread-safe or document the main-thread contract explicitly.
- **Asmdefs authored as plain JSON files with name-based references work** — Unity resolves on reopen; no GUID churn. Repeat for `Veilwalkers.Persistence.Tests.asmdef`.
- **Headless EditMode batch run is the verification gate** (8/8 then 13/13 in 1.2). Same gate here, full suite.
- **`.gitkeep` discipline:** git can't track empty dirs and Unity orphans the `.meta` on fresh clones — if any new empty folder is created, add `.gitkeep`.
- **Editor notices "assembly will not be compiled (no scripts)"** for still-empty areas are benign and expected; Persistence's notice disappears this story.

### Git Intelligence

- Baseline: `6409621` ("Story 1.2: code review passed (14 patches applied) → done"). Working tree clean.
- Commit convention: `Story 1.3: <what>` (mirror `Story 1.2: establish assembly boundaries and composition root`); story-record commits like `Story 1.3: create story → ready-for-dev`.
- 1.2's pattern of committing implementation, then review patches as a separate commit, kept history reviewable — repeat it.

### Project Structure Notes

- All production code in `Assets/Veilwalkers/Persistence/` (namespace `Veilwalkers.Persistence`), one public type per file: `SaveModel.cs`, `EncounterSnapshotData.cs`, `PendingPurchaseRecord.cs`, `AesCrypto.cs`, `SaveCorruptException.cs`, `IProgressStore.cs`, `LocalProgressStore.cs`, `SaveService.cs`, `SaveMigrations.cs`. Bootstrap edits in `Assets/Veilwalkers/App/Bootstrap.cs`; new scene under `Assets/Veilwalkers/Scenes/` (or `Assets/Scenes/`) + Build Settings entry.
- Tests in `Assets/Tests/EditMode/Persistence.Tests/` (parallel Tests tree per architecture).
- **Variance note:** the architecture tree also lists `LocalTelemetryStore.cs` under Persistence — that is Story 1.9, not this story. The directory tree is the map, not this story's scope.

### References

- [Source: docs/epics.md#Story 1.3: Persist player state with encrypted, atomic local save] — story + ACs.
- [Source: docs/epics.md#AR-2 (Persistence foundation)] — SaveModel contents, Newtonsoft/camelCase/AES, atomic async write, validate-on-load, schemaVersion migration, IProgressStore seam.
- [Source: docs/epics.md#AR-19 / docs/architecture.md#Process Patterns] — status enum + start/finish events for long ops; GameLog only.
- [Source: docs/epics.md#AR-20] — the save-tamper test (corrupt save → throw, player-facing recovery, never silent wipe) is THIS story's slice of the architecture-test bundle.
- [Source: docs/architecture.md#Data Architecture / #Data & Serialization Formats] — single versioned save, camelCase, ISO-8601 UTC, int currency, never-null collections, PlayerPrefs only for trivial settings.
- [Source: docs/architecture.md#Authentication & Security] — AES raises the bar, acknowledged not cheat-proof; secrets stay gitignored.
- [Source: docs/architecture.md#Architectural Boundaries] — contracts below AR (AnchorToken in SaveModel without upward edges), locator confinement (constructor injection for Persistence), services-not-ready race.
- [Source: docs/architecture.md#Edge Cases & Failure Handling] — corrupt/tampered save: throw → explicit recovery choice; one-transactional-write rule (consumed by 1.4/1.5).
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — the four Bootstrap runtime-lifecycle items owned by this story; the NOT-this-story deferrals.
- [Source: _bmad-output/implementation-artifacts/1-2-establish-assembly-boundaries-and-composition-root.md] — current Core/locator state, asmdef authoring pattern, headless validation gate, review-finding patterns.
- [Source: Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs] — allowed-edge matrix (no edit needed; `.Tests` suffix exemption; 1.4 owns the Economy→Persistence row change).
- [Source: CLAUDE.md#Working conventions] — commit + push every session; never commit secrets.

### Latest Tech Information

- **Newtonsoft via `com.unity.nuget.newtonsoft-json` 3.2.1** (pinned) = Json.NET 13.0.x, the Unity-vetted build (IL2CPP/AOT-aware). Use `JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }` (or `[JsonProperty]` names) for camelCase; `NullValueHandling` defaults are fine since collections are always non-null by construction.
- **Crypto on Unity 2022.3 (.NET Standard 2.1 / IL2CPP Android):** `Aes.Create()`, `HMACSHA256`, `Rfc2898DeriveBytes`, `RandomNumberGenerator` are all supported. `AesGcm` is NOT reliably available on this profile — use AES-CBC + HMAC (encrypt-then-MAC), which is the portable equivalent.
- **`File.Replace(source, dest, backup)`** and **`File.Move(src, dst, overwrite: true)`** (.NET Standard 2.1) both perform an atomic rename on the same volume on Android/Linux; keep temp + save in the same directory.
- **UTF 1.1.33:** EditMode `[Test]` only (+ `[UnityTest]` coroutines); no `async Task` tests — block on `GetAwaiter().GetResult()`.

### Project Context Reference

No `project-context.md` exists (the `persistent_facts` glob `**/project-context.md` matched nothing). Authoritative context: `docs/architecture.md`, `docs/epics.md`, `CLAUDE.md`, the 1.1/1.2 story records, and `deferred-work.md`.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
