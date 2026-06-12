---
baseline_commit: 6409621
---

# Story 1.3: Persist player state with encrypted, atomic local save

Status: done

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

- [x] **Task 1 — Define `SaveModel` + the snapshot/pending-purchase data shapes (AC: #1, #3)**
  - [x] `Persistence/SaveModel.cs` — plain POCO (NOT a ScriptableObject, NOT a MonoBehaviour) with a top-level `int schemaVersion` (start at `1`) and exactly the AR-2 contents: `int credits`; codex state (discovered monster IDs + per-monster flags `scanned/captured/slain` + variant flags, keyed by stable string id `monNN`); `int xp` + `int level`; per-extra charge counts (Strong Capture / Stability Boost / Nightveil Filter — three named `int` fields or a serializable map); `encounterSnapshot`; pending-purchase records; `dailyClaim` (ISO-8601 UTC date string); `adReward { int grantsToday; long dayBucket; }`; `firstZeroCreditDay` (write-once scalar — use a `long` day-bucket, `-1`/sentinel = unset). [Source: docs/epics.md#AR-2; docs/architecture.md#Data & Serialization Formats]
  - [x] **Tier rule for the snapshot:** `EncounterSnapshot.cs` (the behavior-bearing type) lives in **Encounter** and arrives in Epic 4 — Persistence must NEVER reference Encounter (forbidden upward edge). Define a serializable **data-only** shape Persistence can own now: e.g. `Persistence/EncounterSnapshotData.cs` holding `AnchorToken[]` (from `Veilwalkers.Core.Contracts` — this is exactly why AnchorToken lives below AR) + a list of per-monster state entries (monster id, scan progress, applied boosts — minimal fields now; Epic 4/5 extend it). Default = null/absent meaning "no active encounter". [Source: docs/architecture.md#Architectural Boundaries ("Shared contracts below AR"); docs/architecture.md#Complete Project Directory Structure]
  - [x] `Persistence/PendingPurchaseRecord.cs` — minimal serializable record `{ string orderId; string packId; string state; string isoTimestampUtc }`; the reconciler logic that uses it is Story 5.2 — only the persisted shape lands here. [Source: docs/epics.md#AR-12; docs/epics.md#Story 5.2]
  - [x] **Null-collection rule:** every collection field initializes to an empty collection, and load-validation coerces any null collection to empty (never persist null; never return a model with null collections). This applies to **nested collections too**: `EncounterSnapshotData`'s `AnchorToken[]` + per-monster entry list, and the pending-purchase list. A null `encounterSnapshot` OBJECT is legal ("no active encounter"), but when the snapshot is present its inner collections must be coerced non-null the same way. One public type per file. [Source: docs/architecture.md#Data & Serialization Formats; docs/architecture.md#File & Project Structure Patterns]

- [x] **Task 2 — Implement `AesCrypto` with integrity (tamper → throw) (AC: #1, #3)**
  - [x] `Persistence/AesCrypto.cs` — AES (CBC) encrypt/decrypt of the JSON payload with a **random IV per write, prepended to the file**, plus an integrity check so tampering reliably throws: **encrypt-then-MAC** with `HMACSHA256` computed over **(IV ‖ ciphertext)** — the MAC MUST cover the IV, not just the ciphertext (an IV-only flip otherwise corrupts only the first plaintext block and detection would hinge on JSON-parse luck) — appended after the ciphertext; on decrypt, verify the MAC FIRST and throw a typed exception on mismatch. (CBC padding errors alone do NOT reliably detect tampering — the MAC is what makes AC-3 testable and deterministic.) Use `System.Security.Cryptography` (`Aes.Create()`, `HMACSHA256`) — supported on Unity 2022.3 IL2CPP/Android. [Source: docs/epics.md#AR-2; docs/architecture.md#Authentication & Security]
  - [x] Key material: derive the AES + HMAC keys deterministically from an embedded app secret via `Rfc2898DeriveBytes` (fixed salt, e.g. 32+32 bytes from one derivation). **Derive ONCE and cache** (e.g. `static readonly`/`Lazy<byte[]>`) — never re-derive per save/load call; keep the iteration count modest, since the secret ships in the client and iterations buy no real security here. This **raises the bar against casual save-editing only** — the architecture explicitly accepts it is not cheat-proof (client-side, single-player MVP). Do NOT use `SystemInfo.deviceUniqueIdentifier` (main-thread-only API, and it would brick saves across device transfers). [Source: docs/architecture.md#Authentication & Security ("acknowledged as not cheat-proof")]
  - [x] Define the typed failure: `Persistence/SaveCorruptException.cs` (thrown on MAC mismatch, decrypt failure, JSON parse failure, or schema validation failure). Load must throw THIS, never return null and never silently wipe. [Source: docs/epics.md#Story 1.3; docs/architecture.md#Edge Cases & Failure Handling]

- [x] **Task 3 — Author `IProgressStore` + `LocalProgressStore` with atomic temp+swap write (AC: #1, #2)**
  - [x] `Persistence/IProgressStore.cs` — the backend seam (Local now → Firebase later; do not leak file paths or crypto into the interface): `Task<SaveModel> LoadAsync()`, `Task SaveAsync(SaveModel model)`, `bool Exists()`, `Task DeleteAsync()` (Delete enables the "start fresh" recovery choice). [Source: docs/epics.md#AR-2; docs/architecture.md#Data Architecture ("Backend seam")]
  - [x] `Persistence/LocalProgressStore.cs` — implements `IProgressStore` over a single save file. **Take the storage directory as a constructor parameter** (Bootstrap passes `Application.persistentDataPath`); never call `Application.persistentDataPath` inside the store — it is a main-thread-only Unity API and the ctor param is also what makes the store testable against a temp directory. [Source: docs/architecture.md#Data Architecture]
  - [x] **Atomic write pipeline (the heart of AC-2):** serialize (Newtonsoft, camelCase via `CamelCasePropertyNamesContractResolver`) → encrypt+MAC → write to a **temp file in the same directory** (`save.dat.tmp`) → flush to disk (`FileStream.Flush(flushToDisk: true)`) → swap into place over `save.dat` (`File.Replace`, falling back to delete+`File.Move` only if no destination exists yet). Same-directory temp guarantees same volume, so the swap is an atomic rename. A kill before the swap leaves the prior `save.dat` untouched; a kill after is the new valid save. Clean up any stale `.tmp` on startup/load. [Source: docs/epics.md#AR-2; docs/architecture.md#Data Architecture ("atomic write (temp file + swap)")]
  - [x] **Off the main thread:** run serialize+encrypt+IO inside `Task.Run` (JsonConvert and the crypto APIs are thread-safe; no UnityEngine API may be touched inside). Serialize concurrent `SaveAsync` calls (e.g. a `SemaphoreSlim(1,1)`) so two writers can never interleave on the temp file. [Source: docs/epics.md#AR-2; docs/architecture.md#Process Patterns ("No silent main-thread blocking")]
  - [x] **Validate on load:** after decrypt+parse, check `schemaVersion` is a known version (route through `SaveMigrations`), coerce null collections to empty, and reject structurally-invalid models with `SaveCorruptException`. [Source: docs/epics.md#AR-2; docs/architecture.md#Data & Serialization Formats]

- [x] **Task 4 — Author `SaveService` (app-facing owner of the loaded model + AR-19 status surface) (AC: #2, #3)**
  - [x] `Persistence/SaveService.cs` — owns the single in-memory `SaveModel` for the session and is the app-facing API; it delegates raw persistence to the injected `IProgressStore` (constructor injection — Persistence is a pure-logic area; it must NOT call `GameServices.Get<T>()`). Responsibilities: `InitializeAsync()` (load-or-create: no file → fresh `SaveModel` with defaults — the 20-credit FIRST-LAUNCH GRANT is Story 1.7, NOT here; fresh model has `credits = 0`), `Current` (the loaded model), `SaveAsync()` (persist `Current`). [Source: docs/architecture.md#Complete Project Directory Structure; docs/architecture.md#Architectural Boundaries ("locator rule"); docs/epics.md#Story 1.7]
  - [x] **AR-19 status surface:** expose an explicit status enum (e.g. `SaveStatus { Idle, Loading, Saving, Corrupt, Failed }`) + start/finish C# events (e.g. `OnSaveStarted`, `OnSaveCompleted`, `OnLoadFailed`) so UI can reflect long ops; log via `GameLog` (never raw `Debug.Log`). [Source: docs/epics.md#AR-19; docs/architecture.md#Process Patterns]
  - [x] **Corrupt-load recovery (AC-3):** when `IProgressStore.LoadAsync` throws `SaveCorruptException`, `SaveService` enters `Corrupt` status, raises the failure event carrying the detail, and exposes the recovery API: `RetryLoadAsync()` (re-attempt the load) and `StartFreshAsync()` (delete via `IProgressStore.DeleteAsync`, create defaults, persist). It must NEVER auto-wipe — the wipe happens only through the explicit `StartFreshAsync` call. The actual recovery dialog UI is Epic 6; this story delivers the complete mechanism + events the UI will bind to, proven by tests. [Source: docs/epics.md#Story 1.3; docs/architecture.md#Edge Cases & Failure Handling ("recovery path is explicit, not a dead end")]
  - [x] Note for later stories (do NOT build now): the one-atomic-write-per-player-action rule (AR-8) is consumed by 1.4/1.5 — they mutate `Current` then call `SaveAsync()`, rolling back the in-memory mutation on persist failure. Make sure `SaveAsync` surfaces failure as an awaitable fault (exception or failed `Result`) so callers CAN roll back. [Source: docs/epics.md#AR-8]

- [x] **Task 5 — `SaveMigrations` v1 baseline (AC: #1)**
  - [x] `Persistence/SaveMigrations.cs` — the migration path stub: current version constant (`1`), `Migrate(jObjectOrModel, fromVersion)` that is a no-op for v1, throws `SaveCorruptException` for unknown/newer versions, and is the single place future versions chain (v1→v2→…). Keep it boring; its existence + routing is the AC, not speculative migrations. [Source: docs/epics.md#AR-2 ("migration path (schemaVersion)"); docs/architecture.md#Data Architecture]

- [x] **Task 6 — Wire Bootstrap for real + the deferred runtime-lifecycle items (AC: #1)**
  - [x] Register the first real services in `Bootstrap.WireServices()`: `GameServices.Register<IClock>(new SystemClock())`, `Register<IProgressStore>(new LocalProgressStore(Application.persistentDataPath))`, `Register<SaveService>(...)` (inject the store via constructor), then `MarkReady()`. Kick `SaveService.InitializeAsync()` after sealing — fire-and-forget is fine for now (awaited staging is AR-14, later), but OBSERVE the task's fault (e.g. continuation on faulted → `GameLog.Error`) so a load failure never dies as an unobserved exception. [Source: deferred-work.md#Bootstrap runtime lifecycle; docs/epics.md#AR-4]
  - [x] **Scene placement (deferred from 1.2 review, owner = this story):** create `Assets/Veilwalkers/Scenes/Bootstrap.unity` (or place under `Assets/Scenes/` beside the template scene) containing a `Bootstrap` GameObject, and insert it as **scene 0** in Build Settings — **ahead of (or replacing) `SampleScene.unity`, which is currently enabled at index 0** in `EditorBuildSettings.scenes`. If SampleScene stays in the list, Bootstrap MUST come first or the composition root never runs at startup. The architecture's rule: a single Bootstrap entry scene wires GameServices first, then loads Home (Home itself is Epic 6 — for now Bootstrap scene may simply stay, or load `SampleScene`). [Source: deferred-work.md; docs/architecture.md#Edge Cases & Failure Handling ("Services-not-ready race")]
  - [x] **Execution-order guarantee:** add `[DefaultExecutionOrder(-1000)]` (or wire via `RuntimeInitializeOnLoadMethod`) so no other `Awake` can race the composition root. [Source: deferred-work.md#Bootstrap runtime lifecycle]
  - [x] **Play-mode-options static reset:** `EditorSettings.asset` stores `m_EnterPlayModeOptions: 3` but **`m_EnterPlayModeOptionsEnabled: 0` — Enter Play Mode Options are OFF, so full domain reload still happens on every Editor play today**. Add the `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` static reset anyway, so `GameServices` statics return to unready if anyone later enables the stored fast-enter option. (`ResetForTests()` is `#if UNITY_INCLUDE_TESTS` — the reset method lives in App or uses a guarded path; do NOT make `ResetForTests` public-in-player again.) [Source: deferred-work.md#Bootstrap runtime lifecycle; Assets/Veilwalkers/Core/GameServices.cs; ProjectSettings/EditorSettings.asset]
  - [x] **Mid-wiring failure recovery:** today a `Register` throw mid-wiring leaves the locator poisoned (not ready, but re-running dies on duplicate registration). Make `WireServices` failure-safe: catch, log via `GameLog.Error`, and leave a recoverable state (e.g. reset-and-rethrow, or an idempotent wiring guard) — document the chosen approach in code. [Source: deferred-work.md#Bootstrap runtime lifecycle]

- [x] **Task 7 — Tests: `Persistence.Tests` (EditMode) + the AR-20 tamper test (AC: #1, #2, #3)**
  - [x] Create `Assets/Tests/EditMode/Persistence.Tests/` with `Veilwalkers.Persistence.Tests.asmdef` (Editor-only, references `Veilwalkers.Persistence` + `Veilwalkers.Core`, UTF + `nunit.framework.dll` per the Architecture.Tests asmdef pattern). The name MUST end in `.Tests` — the architecture-guard loader excludes test asmdefs by that suffix, so no allowed-edge-matrix edit is needed (verify all Architecture.Tests still pass). [Source: docs/architecture.md#File & Project Structure Patterns; Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs]
  - [x] **Test against a temp directory** (e.g. `Path.Combine(Path.GetTempPath(), "veilwalkers-tests", TestContext id)`) via the `LocalProgressStore` ctor param — never `Application.persistentDataPath`. Clean up in teardown. UTF **1.1.33 does not support `async Task` test methods** — block with `task.GetAwaiter().GetResult()` inside `[Test]` methods (acceptable in EditMode tests).
  - [x] Round-trip: save a fully-populated `SaveModel` → load → all fields equal (proves camelCase JSON + encrypt/decrypt + schemaVersion). Also: file on disk is NOT plaintext JSON (assert the raw bytes don't contain a known field name).
  - [x] Atomicity: (a) with a valid `save.dat` present, simulate a mid-write kill by making the NEXT write fail before swap (e.g. injectable/abortable stream or pre-created locked temp) OR by directly verifying the pipeline order (temp written + flushed before any touch of `save.dat`) — then assert the original loads intact; (b) a stale `.tmp` left on disk is cleaned and never loaded as the save.
  - [x] **AR-20 tamper test (load-bearing):** corrupt the file (flip ciphertext bytes; also truncate) → `LoadAsync` throws `SaveCorruptException` → `SaveService` reaches `Corrupt` status and the original file is UNCHANGED (never silently wiped) → `StartFreshAsync()` recovers to a working default save; `RetryLoadAsync()` on an uncorrupted file recovers too. [Source: docs/epics.md#AR-20]
  - [x] Null-collection validation: hand-craft a save whose JSON has `null` for a collection → load returns empty collections, never null; saving a model with a null collection persists `[]`. Cover both a top-level collection AND a nested one (a present `encounterSnapshot` with a null inner list); also confirm a fully-null `encounterSnapshot` loads as "no active encounter" without coercion to an empty object.
  - [x] Migration routing: `schemaVersion: 1` loads; unknown version (e.g. `999`) throws `SaveCorruptException`.
  - [x] Run the FULL EditMode suite headless and confirm green (Architecture.Tests 13 + new Persistence.Tests). Headless command + the two traps (open-Editor lock, crash-handler fake exit 0) are in Dev Notes → Testing Requirements.

- [x] **Task 8 — Verify, update story/sprint records, and commit**
  - [x] Zero compile errors on clean reopen; full EditMode suite green; `test-results.xml`/`editor-test.log` deleted before commit (they are NOT gitignored).
  - [x] **Manual Bootstrap play check (the one path with no EditMode coverage):** enter Play Mode in the Bootstrap scene once and confirm via the Console that GameServices wires + seals and a save file appears under `Application.persistentDataPath`; ask James to run this if the Editor is needed interactively. *(Done 2026-06-12 by James: Console showed "Bootstrap: GameServices wired and sealed." + "SaveService: no save found — created and persisted a fresh default save."; `save.dat` (320 bytes, encrypted binary, no stale .tmp) verified under `AppData/LocalLow/DefaultCompany/VeilwalkersUnity/`.)*
  - [x] All new `.cs`, `.asmdef`, `.unity`, and `.meta` files tracked; no secrets.
  - [x] Commit + push (convention: `Story 1.3: <what>`), update sprint-status. [Source: CLAUDE.md#Working conventions]

### Review Findings (code review 2026-06-12: Blind Hunter + Edge Case Hunter + Acceptance Auditor)

- [x] [Review][Decision] **RESOLVED (a): serialize on the calling thread** — Live `SaveModel` is serialized and mutated on a thread-pool thread while the main thread owns it — `SaveService.SaveAsync` hands the shared `Current` model to `LocalProgressStore.SaveCore`, which runs `CoerceNullCollections()` (swaps collection instances), sets `SchemaVersion`, and enumerates every collection during `JsonConvert.SerializeObject` inside `Task.Run`. A main-thread mutation landing mid-serialization throws `InvalidOperationException` or — worse — writes a torn half-old/half-new save with a valid MAC. The store semaphore serializes store calls, not model access. Fix choices: (a) snapshot on the calling thread (serialize to JSON before `Task.Run`; encrypt+IO stay off-thread — deviates from the task's "serialize inside Task.Run" letter), or (b) keep as-is and harden the documented contract: mutations main-thread-only and every `SaveAsync` awaited before the next mutation (the AR-8 pattern), accepting the latent fire-and-forget hazard. [Assets/Veilwalkers/Persistence/SaveService.cs:168-179, Assets/Veilwalkers/Persistence/LocalProgressStore.cs:174-180]
- [x] [Review][Decision] **RESOLVED (a): fail-fast + honest doc** (`IsRegistered` + its 2 Architecture tests removed — sole stated purpose was the rejected re-run design) — Mid-wiring recovery narrative is undeliverable as coded — nothing ever re-invokes `WireServices` after the rethrow from `Awake` (no retry mechanism exists until Story 6.3), so "idempotent re-run will skip already-registered services" has no invoker and a wiring failure bricks the locator; AND a hypothetical re-run would construct fresh instances while `IsRegistered` guards skip per-key, so the registered `IProgressStore` and the store captured inside a newly-registered `SaveService` could be different instances (two semaphores → the "two writers never interleave" guarantee voided). Fix choices: (a) simplify to fail-fast with honest doc (drop the per-key guards — construct-all-before-register already makes partial registration unreachable), (b) keep guards but rewrite the doc/log honestly + make a re-run instance-consistent, (c) add a real retry hook now. [Assets/Veilwalkers/App/Bootstrap.cs:20-29,79-103]
- [x] [Review][Patch] Non-`JsonException` corruption escapes the `SaveCorruptException` contract → recovery UI never offered — `versionToken.Value<int>()` throws `OverflowException` on a huge `schemaVersion` (outside any try), and the `Vector3`/`Quaternion` converters' `Value<float?>()` throws `FormatException`/`OverflowException`/`InvalidCastException` on non-numeric components (incl. `NaN`/`Infinity` written as strings by Json.NET's default `FloatFormatHandling`), escaping the `catch (JsonException)` around `ToObject` — `SaveService` lands in `Failed` (rethrow) instead of `Corrupt`. [Assets/Veilwalkers/Persistence/LocalProgressStore.cs:152-162, Assets/Veilwalkers/Persistence/SaveJson.cs:63-96]
- [x] [Review][Patch] `CleanUpStaleTemp` catches `IOException` but not `UnauthorizedAccessException` — a read-only/ACL-denied stale `save.dat.tmp` makes `File.Delete` throw past the catch, blocking the load of a perfectly valid `save.dat`, contradicting the inline comment. [Assets/Veilwalkers/Persistence/LocalProgressStore.cs:208-220]
- [x] [Review][Patch] Corrupt-path leaves stale non-null `Current`, violating the class's own doc — the doc promises `Current` is null "while the save is corrupt and unrecovered", but the `SaveCorruptException` catch never clears `_current`: corrupt-on-reload leaves consumers gating on `Current != null` running on dead data. [Assets/Veilwalkers/Persistence/SaveService.cs:67-68,112-121]
- [x] [Review][Patch] Overlapping `SaveAsync` calls interleave AR-19 status — first completion sets `Idle`/fires `OnSaveCompleted` while the second writer is still queued behind the store semaphore; status surface can lie to UI. Needs an in-flight counter (e.g. `Interlocked`) before reporting `Idle`. [Assets/Veilwalkers/Persistence/SaveService.cs:166-189]
- [x] [Review][Patch] Null list ELEMENTS survive `CoerceNullCollections` — `"pendingPurchases":[null]`, `"monsters":[null]`, `"variantFlags":[null]`, `"appliedBoosts":[null]` load with null entries (only null collections and null Codex dict values are repaired); downstream iteration NREs. Add element-level `RemoveAll(x => x == null)`. [Assets/Veilwalkers/Persistence/SaveModel.cs:100-138, Assets/Veilwalkers/Persistence/EncounterSnapshotData.cs:45]
- [x] [Review][Patch] `OnSaveStarted` subscriber exception strands status at `Saving` forever — the invoke sits after `SetStatus(Saving)` but before the try block, so a throwing handler aborts the save with the catch never running. Move the invoke inside the try (or wrap it). [Assets/Veilwalkers/Persistence/SaveService.cs:175-177]
- [x] [Review][Patch] `Exists()`/`LoadAsync` TOCTOU turns a missing file into `Failed` instead of fresh-create — file vanishing between the unguarded `Exists()` check and the load surfaces `FileNotFoundException` → generic catch → `Failed`, rather than falling through to create-defaults. [Assets/Veilwalkers/Persistence/SaveService.cs:99-110]
- [x] [Review][Patch] `Failed_write_before_swap_leaves_prior_save_intact` relies on Windows sharing semantics — on POSIX, `File.Delete` unlinks the held-open temp and the save succeeds, failing the assert. Document the Windows-host assumption in the test. [Assets/Tests/EditMode/Persistence.Tests/AtomicWriteTests.cs:40-48]
- [x] [Review][Patch] Two undocumented deviations missing from the Dev Agent Record — the test asmdef adds a `Newtonsoft.Json.dll` precompiled reference (necessary, test-only, but absent from the completion notes), and `CoerceNullCollections` coerces a null `adReward` OBJECT to a default instance (the only object-level rule the story states is "null snapshot stays null"). Document both. [Assets/Tests/EditMode/Persistence.Tests/Veilwalkers.Persistence.Tests.asmdef, Assets/Veilwalkers/Persistence/SaveModel.cs:132-135]
- [x] [Review][Defer] `SampleScene` left enabled at build index 1 — ships dead template content; a second loadable scene that never runs the composition root. Spec-sanctioned this story ("SampleScene may stay if Bootstrap comes first"); Epic 6 (Story 6.3 scene flow) owns the restructure. [ProjectSettings/EditorBuildSettings.asset] — deferred, spec-sanctioned
- [x] [Review][Defer] Event surface incomplete for Epic 6 UI binding — `OnLoadFailed` fires during scene-0 boot before any subscriber can exist (no replay-on-subscribe; consumers must check `Status` first), and AR-19 events don't cover load-started/completed or `StartFreshAsync` saves. Epic 6 (recovery dialog + UI binding) is the natural owner. [Assets/Veilwalkers/Persistence/SaveService.cs:36-47, Assets/Veilwalkers/App/Bootstrap.cs:109-113] — deferred to Epic 6

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
6. **No `ISaveService` — register the concrete class (documented AR-4 deviation):** AR-4's list nominally puts SaveService "behind an interface", but the architecture's directory tree ships no `ISaveService` — the swappable seam is `IProgressStore` (Local → Firebase), and `SaveService` is app-facing composition glue with no second implementation foreseeable. Register `SaveService` concretely; do NOT invent an interface for it. State the deviation in the class doc-comment (doc-comment accuracy was a 1.2 review finding). 1.4's `CreditService` still gets `ICreditService` per its own AC. [Source: docs/epics.md#AR-4; docs/architecture.md#Complete Project Directory Structure]

### Current repo state (verified on disk at baseline 6409621)

- Story 1.2 is done: all 9 area asmdefs exist with the one-way graph; `Veilwalkers.Persistence` compiles as an EMPTY assembly ("no scripts" Editor notice is expected and disappears when this story adds .cs files).
- **Core types ready to consume:** `GameServices` (thread-safe, wire-once, `ResetForTests` gated behind `UNITY_INCLUDE_TESTS`), `ServicesNotReadyException`, `IClock`/`SystemClock` (`DateTime UtcNow`), `GameLog` (Debug/Info/Warn/Error), `Result` (Ok/Fail + never-null Message), `SpendResult`/`SpendFailureReason`, `Core/Contracts/AnchorToken` (`[Serializable]`, string trackableId + UnityEngine pose math), `AnchorRestoreResult`.
- **`Bootstrap.cs` is a near-empty shell in NO scene** — it already guards re-entry via `GameServices.IsReady` and has the commented example `Register<IClock>(new SystemClock())`. This story puts it in a scene and registers the first real services. [Source: Assets/Veilwalkers/App/Bootstrap.cs]
- `Assets/Scenes/` contains only the template `SampleScene.unity`; **`EditorBuildSettings.scenes` lists SampleScene ENABLED at index 0** — the new Bootstrap scene must be inserted ahead of it (or replace it), never appended after.
- `EditorSettings.asset` stores `m_EnterPlayModeOptions: 3` with **`m_EnterPlayModeOptionsEnabled: 0`** — domain reload is currently still ON (the stored value only takes effect if the toggle is enabled); the static-reset task future-proofs that flip rather than reflecting live behavior.
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

- New surface: `Assets/Tests/EditMode/Persistence.Tests/` (own Editor-only asmdef, mirrors the Architecture.Tests asmdef shape: `includePlatforms: ["Editor"]`, UTF references + `"precompiledReferences": ["nunit.framework.dll"]`, `"overrideReferences": true`). In scope: round-trip, not-plaintext, atomic-swap survival, stale-tmp cleanup, **AR-20 tamper test** (corrupt → `SaveCorruptException` → never-wipe → explicit recovery works), null-collection coercion, migration routing, and the **SaveService wiring happy path**: after `GameServices.ResetForTests()` (available under `UNITY_INCLUDE_TESTS`), construct `LocalProgressStore(tempDir)` + `SaveService` directly, `Register` them, `MarkReady()`, resolve via `Get<>`, and assert `InitializeAsync()` creates defaults. Do NOT reference `Veilwalkers.App` or try to test `Bootstrap` from EditMode — `WireServices` is private and Awake-driven, and the 1.2 review removed exactly such a dead App reference from a test asmdef; Bootstrap itself is verified by the manual play check in Task 8.
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
- **`File.Replace(source, dest, backup)`** performs an atomic rename on the same volume on Android/Linux; keep temp + save in the same directory. First-ever save (no destination yet) uses plain two-arg `File.Move` — the pipeline never needs an overwrite-capable move, which sidesteps any doubt about the 3-arg `File.Move` overload on Unity's API profile.
- **UTF 1.1.33:** EditMode `[Test]` only (+ `[UnityTest]` coroutines); no `async Task` tests — block on `GetAwaiter().GetResult()`.

### Project Context Reference

No `project-context.md` exists (the `persistent_facts` glob `**/project-context.md` matched nothing). Authoritative context: `docs/architecture.md`, `docs/epics.md`, `CLAUDE.md`, the 1.1/1.2 story records, and `deferred-work.md`.

## Dev Agent Record

### Agent Model Used

claude-fable-5 (Claude Code) — 2026-06-12

### Debug Log References

- Headless EditMode runs (command per Testing Requirements): run 1 → 38/41 (3 failures), run 2 → 39/41, run 3 → **41/41 green** (15 Architecture.Tests + 26 Persistence.Tests), 0 × `error CS` in editor-test.log, `test-results.xml` confirmed written each run. Artifacts deleted before commit.
- Run-1 failure: Json.NET's default `DateParseHandling` silently converted the ISO-8601 string `isoTimestampUtc` to a culture-formatted DateTime on load. Fix: `DateParseHandling.None` in `SaveJson.Settings` AND on the `JsonTextReader` used for the `JObject` parse in `LocalProgressStore.LoadCore` (the parse site does not inherit serializer settings).
- Run-2 failures: UTF fails any test that observes an error log, and `LogAssert.ignoreFailingMessages` did not suppress logs raised from thread-pool continuations (`ConfigureAwait(false)` paths). Resolved semantically, not with test plumbing — see Completion Notes on Warn-vs-Error.

### Completion Notes List

- **All three ACs implemented and proven by tests.** AC-1: round-trip of a fully-populated model (camelCase JSON, AES, schemaVersion, all AR-2 fields), `IProgressStore` seam. AC-2: temp+flush+swap atomic write off the main thread, mid-write-failure survival test, stale-tmp cleanup tests. AC-3: AR-20 tamper tests (flip + truncate → `SaveCorruptException`; file bytes verified UNCHANGED after failed load; `StartFreshAsync`/`RetryLoadAsync` recovery both proven), null-collection coercion (top-level + nested + null-snapshot-object stays null), migration routing (v1 loads, 999/missing/non-integer throws).
- **Corrupt-path log severity is Warn, not Error (deliberate):** a corrupt save is an expected, HANDLED state with a designed recovery flow — `GameLog.Warn`'s documented "recoverable problem worth attention" contract. `GameLog.Error` remains on the unexpected-failure paths (load/save IO failures, start-fresh failure, wiring failure). This also matters practically: UTF fails tests on error logs even from background threads, and the AR-20 tests exercise exactly this path.
- **Extra public types beyond the story's named file list** (one-public-type-per-file rule): `CodexEntryData`, `EncounterMonsterStateData`, `AdRewardData`, `SaveStatus`; plus internal `SaveJson` (serializer settings + private `Vector3`/`Quaternion` converters — plain Json.NET cannot serialize Unity math types: `Vector3.normalized` self-references, so `AnchorToken` needs converters).
- **camelCase resolver deviation (letter, not intent):** `DefaultContractResolver` + `CamelCaseNamingStrategy(processDictionaryKeys: false)` instead of the stock `CamelCasePropertyNamesContractResolver`, which would re-case codex dictionary KEYS (monster ids must persist verbatim — covered by a dedicated test). `ObjectCreationHandling.Replace` prevents double-population of pre-initialized collections.
- **Core additions for Task 6:** `GameServices.IsRegistered<T>()` (pre-ready on purpose — enables idempotent wiring recovery; 2 new Architecture tests) and `GameServices.ResetForFastEnterPlayMode()` (`#if UNITY_EDITOR`, shares private `ResetCore()` with `ResetForTests` — `ResetForTests` stays test-gated, nothing reset-shaped ships in player builds).
- **Mid-wiring failure recovery (chosen approach, documented in Bootstrap):** construct-all-before-first-Register (ctor failures leave the locator untouched) + `IsRegistered`-guarded registrations (re-run skips what landed) + catch/log/rethrow. Reset-and-rethrow rejected: no player-build reset exists, by design.
- **Threading:** `ConfigureAwait(false)` on every await in Persistence — no Unity sync-context capture, which is also what lets EditMode tests block with `GetAwaiter().GetResult()` without deadlock. `SaveService.Status`/`Current` published under a lock; events may fire on background threads (UI must marshal — Epic 6). Store serializes ALL file ops (load/save/delete) through one `SemaphoreSlim` so a load's stale-tmp cleanup can never race a concurrent save's live temp file.
- **Bootstrap scene authored as YAML** (`Assets/Veilwalkers/Scenes/Bootstrap.unity`, minimal: settings blocks + Bootstrap GameObject) with a controlled GUID referenced from `EditorBuildSettings.asset` at **index 0**, SampleScene kept enabled at index 1. Unity imported it cleanly in the headless runs.
- **`SaveModel` fresh default has `credits = 0`** (asserted in tests) — the 20-credit grant is Story 1.7. `SaveAsync` faults on failure (asserted) so 1.4/1.5 can roll back per AR-8.
- **Manual Bootstrap play check PASSED (James, 2026-06-12):** Play Mode in the Bootstrap scene logged "Bootstrap: GameServices wired and sealed." then "SaveService: no save found — created and persisted a fresh default save." (an EDM4U analytics info line in between is benign); `save.dat` verified on disk — 320 bytes, encrypted binary, no stale `.tmp`. All story verification is now complete: headless suite 41/41 + the manual runtime path.
- **Code-review patches (2026-06-12, 11 applied — see Review Findings):** serialization moved to the CALLING thread in `LocalProgressStore.SaveAsync` (snapshot-before-thread-hop; encrypt+IO stay in `Task.Run` — documented deviation from the task's "serialize inside Task.Run" letter, review-driven); corruption classification hardened (schemaVersion overflow + converter `FormatException`/`OverflowException`/`InvalidCastException` now → `SaveCorruptException`, reaching the recovery flow); `CleanUpStaleTemp` also catches `UnauthorizedAccessException`; `SaveService` clears `Current` on a corrupt load (doc now true), uses an in-flight counter so overlapping saves can't report `Idle` early, fires `OnSaveStarted` inside the try, and falls back to fresh-create when the file vanishes between `Exists()` and the load; null list ELEMENTS are removed by coercion (all four data shapes); Bootstrap simplified to fail-fast wiring with an honest doc (per-key `IsRegistered` guards removed; `GameServices.IsRegistered<T>()` + its 2 Architecture tests deleted — its sole purpose was the rejected re-run design); Windows-host assumption documented in `Failed_write_before_swap_leaves_prior_save_intact`. 6 new regression tests added (overflow version, non-numeric anchor component, corrupt-after-load clears Current, null list elements, throwing subscriber, vanishing-file fallback).
- **Previously-undocumented deviations, now recorded (review finding):** the test asmdef references `Newtonsoft.Json.dll` in `precompiledReferences` alongside `nunit.framework.dll` (tests parse `JObject`; test-only, no production edge); `CoerceNullCollections` coerces a null `adReward` OBJECT to a default instance (object-level coercion beyond the stated collection rule — deliberate, matches the never-null-defaults spirit; the null `encounterSnapshot` object remains the one legal null).

### File List

New — production (`Assets/Veilwalkers/Persistence/`, each with `.meta`):
- SaveModel.cs, CodexEntryData.cs, EncounterSnapshotData.cs, EncounterMonsterStateData.cs, PendingPurchaseRecord.cs, AdRewardData.cs, AesCrypto.cs, SaveCorruptException.cs, IProgressStore.cs, LocalProgressStore.cs, SaveService.cs, SaveStatus.cs, SaveMigrations.cs, SaveJson.cs

New — scene:
- Assets/Veilwalkers/Scenes.meta, Assets/Veilwalkers/Scenes/Bootstrap.unity (+ .meta)

New — tests (`Assets/Tests/EditMode/Persistence.Tests/`, each with `.meta`, plus folder `.meta`):
- Veilwalkers.Persistence.Tests.asmdef, TestSaveFiles.cs, SaveRoundTripTests.cs, AtomicWriteTests.cs, SaveTamperRecoveryTests.cs, NullCollectionTests.cs, MigrationRoutingTests.cs, SaveServiceTests.cs

Modified:
- Assets/Veilwalkers/App/Bootstrap.cs (real wiring, execution order, editor static reset, init kick with observed fault)
- Assets/Veilwalkers/Core/GameServices.cs (IsRegistered, ResetForFastEnterPlayMode, ResetCore refactor)
- Assets/Tests/EditMode/Architecture.Tests/GameServicesTests.cs (2 IsRegistered tests)
- ProjectSettings/EditorBuildSettings.asset (Bootstrap scene at index 0)
- _bmad-output/implementation-artifacts/sprint-status.yaml (status transitions)
- _bmad-output/implementation-artifacts/1-3-persist-player-state-with-encrypted-atomic-local-save.md (this record)

## Change Log

- 2026-06-12 — Story 1.3 implemented: full persistence layer (SaveModel/AesCrypto/IProgressStore/LocalProgressStore/SaveService/SaveMigrations), Bootstrap wired for real with entry scene at build index 0 + deferred 1.2 runtime-lifecycle items, 26 new EditMode tests + 2 Architecture tests; full suite 41/41 green headless. Status → review.
- 2026-06-12 — Code review (3 parallel layers: Blind Hunter / Edge Case Hunter / Acceptance Auditor): all 3 ACs verified satisfied; 2 decisions resolved (serialize-on-calling-thread; fail-fast wiring) + 9 patches applied + 6 regression tests added; 2 items deferred (SampleScene at index 1 → Story 6.3; SaveService event-surface completeness → Epic 6) and recorded in deferred-work.md; 1 finding dismissed as noise.
