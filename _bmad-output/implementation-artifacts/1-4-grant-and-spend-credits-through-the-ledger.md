---
baseline_commit: 9c2917a
---

# Story 1.4: Grant and spend Credits through the ledger

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want a Credit balance I can spend on actions,
So that the in-game economy works with no surprise deductions.

## Acceptance Criteria

**AC-1 — `CreditService` behind `ICreditService`: integer balances, typed results, events**
**Given** `CreditService` behind `ICreditService`
**When** Credits are granted or spent
**Then** balances are integers only and the service raises `OnCreditsChanged`
**And** `TrySpendCredits(cost)` returns a `SpendResult` and never throws for "can't afford".
[Source: docs/epics.md#Story 1.4; docs/epics.md#AR-7; docs/architecture.md#Communication Patterns]

**AC-2 — Insufficient balance: typed failure, unchanged balance, `OnInsufficientCredits`, no Shop/Billing/UI**
**Given** a spend attempt where balance < cost
**When** `TrySpendCredits` is called
**Then** it returns `Success = false` with a `FailureReason`, the balance is unchanged, and `OnInsufficientCredits` is raised
**And** the service never opens the Shop or calls Billing/UI itself.
[Source: docs/epics.md#Story 1.4; docs/epics.md#AR-11; docs/architecture.md#Architectural Boundaries ("Insufficient-credits / Shop trigger")]

**AC-3 — Exact-balance spend succeeds to zero**
**Given** a spend attempt where balance == cost (exact balance)
**When** `TrySpendCredits` is called
**Then** it succeeds and the new balance is 0 (the spend condition is `balance >= cost`).
[Source: docs/epics.md#Story 1.4; docs/architecture.md#Edge Cases & Failure Handling ("Exact-balance spend")]

**AC-4 — Atomic spend pipeline: validate → deduct → persist, rollback on persist failure**
**Given** any successful spend
**When** the deduction occurs
**Then** the sequence is validate → deduct → persist, and a persist failure rolls the deduction back (balance unchanged).
[Source: docs/epics.md#Story 1.4; docs/epics.md#AR-8; docs/architecture.md#Process Patterns ("Credit/charge spends are atomic")]

## Tasks / Subtasks

- [ ] **Task 1 — Extend the Core failure vocabulary for the persist-failure path (AC: #4)**
  - [ ] `Core/SpendFailureReason.cs` — APPEND `PersistenceFailed = 2` (the enum's doc-comment explicitly reserves extension for this story; append only, never reorder — persisted/telemetry value stability). A rolled-back spend reports this reason instead of throwing: NFR-3's typed-result standard covers infra failures, not just "can't afford". [Source: Assets/Veilwalkers/Core/SpendFailureReason.cs; docs/architecture.md#Communication Patterns]
  - [ ] No other Core edits. `SpendResult` already has exactly the AR-7 shape (`Success`, `NewBalance`, `FailureReason`, `Succeeded`/`Failed` factories with the `None`-guard) — do NOT touch it. [Source: Assets/Veilwalkers/Core/SpendResult.cs]

- [ ] **Task 2 — Author `ICreditService` + `CreditService` in Economy (AC: #1, #2, #3, #4)**
  - [ ] `Economy/ICreditService.cs` — the seam UI/Encounter/Billing consume (one public type per file): `int Balance { get; }`; `Task<SpendResult> TrySpendCreditsAsync(int cost)`; `Task<Result> GrantCreditsAsync(int amount)`; `event Action<int> OnCreditsChanged` (payload = new balance); `event Action<InsufficientCreditsEvent> OnInsufficientCredits`. Naming deviation, document in the interface doc-comment: the epic writes `TrySpendCredits(cost)`, but the method persists before committing (AR-8) so it is async and the architecture's naming rule mandates the `Async` suffix — intent (typed result, never throws for can't-afford) is unchanged. [Source: docs/epics.md#Story 1.4; docs/architecture.md#Naming Patterns ("Async methods: suffix Async"); docs/epics.md#AR-6]
  - [ ] `Economy/InsufficientCreditsEvent.cs` — tiny readonly struct `{ int Cost; int Balance; }` so the Epic 5.4 top-up sheet and Story 6.3 AppStateMachine get the context they need (what was attempted, what the player has) without querying back. [Source: docs/epics.md#Story 5.4; docs/epics.md#Story 6.3]
  - [ ] `Economy/CreditService.cs` — implements `ICreditService` over the injected `SaveService` (constructor injection; Economy is a pure-logic area and must NOT call `GameServices.Get<T>()`). `Balance` reads `SaveService.Current.Credits` — the loaded `SaveModel` is the single source of truth; CreditService keeps NO duplicate balance field. [Source: docs/architecture.md#Architectural Boundaries ("locator rule"); Assets/Veilwalkers/Persistence/SaveService.cs; Assets/Veilwalkers/Persistence/SaveModel.cs]
  - [ ] **Input/state guards (programmer errors — these THROW; the never-throw contract covers only expected failures):** `cost <= 0` / `amount <= 0` → `ArgumentOutOfRangeException`; `SaveService.Current == null` (not initialized / corrupt-unrecovered) → `InvalidOperationException` with a "call InitializeAsync / recover first" message. **The `Balance` getter applies the SAME `InvalidOperationException` guard** — it reads `Current.Credits`, `Current` is null in exactly the window Task 5 documents (service resolvable before the model loads), and a bare property read there is an NRE, not a typed failure. Mirrors the existing guard style in `SpendResult.Failed` and `SaveService.SaveAsync`. [Source: Assets/Veilwalkers/Core/SpendResult.cs; Assets/Veilwalkers/Persistence/SaveService.cs]
  - [ ] **No costs live here:** `TrySpendCreditsAsync` takes the cost as a parameter. Do NOT add Lure/Slay cost constants — all economy numbers arrive via `EconomyConfig` in Story 1.6 and are passed in by callers (Epic 4/5). [Source: docs/epics.md#AR-16; docs/epics.md#Story 1.6]

- [ ] **Task 3 — Implement the AR-8 atomic spend/grant pipeline (AC: #3, #4)**
  - [ ] **Spend:** validate guards → **capture the model reference ONCE after the null guard (`var model = _saveService.Current`) and use it for the affordability check, deduct, and rollback — never re-read `Current` mid-operation** (a recovery swap via `StartFreshAsync`/`RetryLoadAsync` mid-mutation would make a property-based rollback write the stale balance onto the NEW model; the captured reference makes cross-model corruption structurally impossible). If `model.Credits < cost`: raise `OnInsufficientCredits(new InsufficientCreditsEvent(cost, balance))`, return `SpendResult.Failed(InsufficientCredits, balance)` — no persist, no `OnCreditsChanged`, no exception. Otherwise: capture `priorBalance` → deduct in-memory (`model.Credits -= cost`) → `await _saveService.SaveAsync()` → on success return `SpendResult.Succeeded(newBalance)` and raise `OnCreditsChanged(newBalance)`; on a persist FAULT (`SaveAsync` throws — it is documented to fault so callers can roll back) restore `model.Credits = priorBalance`, log via `GameLog.Error`, return `SpendResult.Failed(PersistenceFailed, priorBalance)` and do NOT raise `OnCreditsChanged`. Exact-balance (`balance == cost`) follows the success path to 0. [Source: docs/epics.md#AR-8; Assets/Veilwalkers/Persistence/SaveService.cs (SaveAsync doc); docs/architecture.md#Edge Cases & Failure Handling]
  - [ ] **Grant:** same pipeline shape — guards → capture the model reference once → `checked { model.Credits += amount; }` (a buggy caller — e.g. a reconciliation replay in 5.2 — must not overflow a negative balance into the save; `OverflowException` is a programmer-error throw, same family as the guards) → `await SaveAsync()` → `Result.Ok` + `OnCreditsChanged(newBalance)`; on persist fault roll back and return `Result.Fail(...)` without the event. Grant is consumed by Story 1.7 (starting 20) and Epic 5 (pack credits) — keep it dumb: no "first launch" logic here. [Source: docs/epics.md#Story 1.7; docs/epics.md#Story 5.1]
  - [ ] **Events fire only AFTER a committed persist** (mutate → persist → event). The sub-second spend-ack (NFR-2) holds because the local save is milliseconds — do not "optimize" by raising the event before the await; an evented-but-unpersisted balance is exactly the split AR-8 forbids. [Source: docs/epics.md#AR-8; docs/epics.md (NFR-2)]
  - [ ] **Serialize mutations:** wrap spend AND grant bodies in a private `SemaphoreSlim(1,1)` so two in-flight mutations can never interleave deduct/persist/rollback (the 1.3 review's torn-write hazard: `SaveAsync` snapshots the live model on the calling thread; the documented contract is every mutation awaited before the next). **Raise events AFTER releasing the semaphore** (order: mutate → persist → release → raise): an event raised inside the lock deadlocks any subscriber that synchronously blocks on another credit mutation (`GetAwaiter().GetResult()` — this project's standard test idiom). Also `ConfigureAwait(false)` on every await in Economy — same rationale as Persistence: no Unity sync-context capture, EditMode tests can block with `GetAwaiter().GetResult()` without deadlock. Consequence to document on both interface events: raised outside the mutation lock, only after a committed persist, and possibly on a background thread; UI marshals (Epic 6) — identical to the `SaveService` contract. [Source: _bmad-output/implementation-artifacts/1-3-…md#Review Findings (RESOLVED (a)); Assets/Veilwalkers/Persistence/SaveService.cs (threading doc)]

- [ ] **Task 4 — Add the `Economy → Persistence` edge: asmdef + allowed-edge matrix, same commit (AC: #1)**
  - [ ] `Assets/Veilwalkers/Economy/Veilwalkers.Economy.asmdef` — add `"Veilwalkers.Persistence"` to `references` (name-based, like every existing reference; no GUID churn). [Source: Assets/Veilwalkers/Economy/Veilwalkers.Economy.asmdef; 1-2 story record (asmdef authoring pattern)]
  - [ ] `Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs` — add `"Veilwalkers.Persistence"` to `Veilwalkers.Economy`'s row in `AllowedReferences`. The test's own comments pre-sanction exactly this edit ("Story 1.4 is expected to add 'Veilwalkers.Persistence' to Economy's row"). New legal edges are deliberate, visible test edits — make both changes in the SAME commit or the architecture guard fails in between. [Source: Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs:39; memory: unity-asmdef-architecture-test]
  - [ ] Add NOTHING else to the matrix — this story creates no other edges.

- [ ] **Task 5 — Wire `ICreditService` in Bootstrap (AC: #1)**
  - [ ] `Assets/Veilwalkers/App/Bootstrap.cs` — extend `WireServices()` following its documented construct-all-before-register pattern: `var creditService = new CreditService(saveService);` alongside the existing constructions, then `GameServices.Register<ICreditService>(creditService);` before `MarkReady()`. Do not reorder or restructure the wiring; fail-fast doc stays true. [Source: Assets/Veilwalkers/App/Bootstrap.cs; docs/epics.md#AR-4]
  - [ ] Note: Bootstrap fire-and-forgets `SaveService.InitializeAsync()`, so `ICreditService` is resolvable BEFORE the model is loaded; any call in that window hits the `InvalidOperationException` guard by design. No gameplay caller exists yet (Epic 4/6 own ordering via `Bootstrap.LoadPhase`/AppStateMachine) — document this in the Bootstrap comment, do not build staging now (AR-14 is later). [Source: docs/epics.md#AR-14; Assets/Veilwalkers/App/Bootstrap.cs]

- [ ] **Task 6 — Tests: `Economy.Tests` (EditMode) (AC: all)**
  - [ ] Create `Assets/Tests/EditMode/Economy.Tests/` with `Veilwalkers.Economy.Tests.asmdef` — Editor-only, mirrors the Persistence.Tests asmdef shape (`includePlatforms: ["Editor"]`, UTF refs, `"precompiledReferences": ["nunit.framework.dll"]`, `"overrideReferences": true`), references `Veilwalkers.Economy`, `Veilwalkers.Persistence`, `Veilwalkers.Core`. The `.Tests` suffix exempts it from the architecture matrix automatically. [Source: Assets/Tests/EditMode/Persistence.Tests/Veilwalkers.Persistence.Tests.asmdef; memory: unity-asmdef-architecture-test]
  - [ ] **Fake store, not files:** a test `FakeProgressStore : IProgressStore` (in-memory `SaveModel`, a `FailNextSave` flag whose `SaveAsync` throws, counters for save calls, an optional `TaskCompletionSource` gate so a test can hold a save open for the serialization test) + a real `SaveService` over it — this is exactly the throwing-fake the 1.3 notes promised the interface would make trivial. No temp directories, no crypto, no `LocalProgressStore` needed. UTF 1.1.33: no `async Task` tests — block with `.GetAwaiter().GetResult()`. [Source: 1-3 story record (#Testing Requirements "designing IProgressStore as an interface makes the throwing-fake trivial")]
  - [ ] Spend happy path: balance 5, cost 3 → `Success`, `NewBalance == 2`, model persisted (store save-count incremented), `OnCreditsChanged(2)` raised exactly once, after the persist (record callback order).
  - [ ] **AC-3 exact balance:** balance 3, cost 3 → `Success`, `NewBalance == 0`, `OnInsufficientCredits` NOT raised.
  - [ ] **AC-2 insufficient:** balance 2, cost 3 → no exception, `Success == false`, `FailureReason == InsufficientCredits`, `NewBalance == 2`, balance unchanged, NO persist call, `OnInsufficientCredits` raised with `{ Cost = 3, Balance = 2 }`, `OnCreditsChanged` NOT raised.
  - [ ] **AC-4 rollback (spend AND grant):** `FailNextSave` → spend returns `Failed(PersistenceFailed)` with balance restored to prior; grant returns `Result.Fail`; in both cases `OnCreditsChanged` NOT raised and `SaveModel.Credits` equals the pre-call value. (Expect `SaveService` to log an Error here — assert via `LogAssert.Expect` or match the existing Persistence.Tests handling of UTF's fail-on-error-log.)
  - [ ] Grant happy path: +20 → `Result.Ok`, balance 20, persisted, `OnCreditsChanged(20)`.
  - [ ] Guards: `cost <= 0` / `amount <= 0` → `ArgumentOutOfRangeException`; service used before `InitializeAsync` (Current null) → `InvalidOperationException` from spend, grant, AND the `Balance` getter; neither persists nor raises events.
  - [ ] **Serialization guarantee:** gate the fake store's `SaveAsync` on a `TaskCompletionSource` — start spend A (held open at its persist), start spend B without awaiting A, assert B has NOT deducted while A's save is in flight; complete the TCS; assert both commit, final balance reflects both spends, exactly two save calls. This is the test that pins what the `SemaphoreSlim` exists for.
  - [ ] Wiring happy path (mirrors `SaveServiceTests`): `GameServices.ResetForTests()` → construct fake store + `SaveService` + `CreditService`, `Register<ICreditService>`, `MarkReady()`, resolve via `Get<ICreditService>()`, init, grant+spend round-trip.
  - [ ] Run the FULL EditMode suite headless and confirm green (Architecture.Tests 13 — incl. the edited matrix — + Persistence.Tests 26 + new Economy.Tests). Command + traps in Dev Notes → Testing Requirements.

- [ ] **Task 7 — Verify, update story/sprint records, and commit**
  - [ ] Zero compile errors on clean reopen; full EditMode suite green; `test-results.xml`/`editor-test.log` deleted before commit (NOT gitignored).
  - [ ] No manual play check required this story (no new scene/runtime path; Bootstrap wiring is covered by the wiring test + 1.3's proven boot). If Bootstrap behavior is in doubt, a single Play-Mode console check ("wired and sealed") suffices.
  - [ ] All new `.cs`, `.asmdef`, `.meta` files tracked; no secrets.
  - [ ] Commit + push (convention: `Story 1.4: <what>`; implementation and review patches as separate commits), update sprint-status. [Source: CLAUDE.md#Working conventions]

## Dev Notes

### What this story IS (and is NOT)

This story builds the **Credit ledger**: `ICreditService`/`CreditService` (Economy), the `PersistenceFailed` failure reason, the AR-8 atomic spend/grant pipeline over `SaveService`, the `Economy → Persistence` asmdef edge (+ matrix row), Bootstrap registration, and `Economy.Tests`. It is the first consumer of 1.3's persistence layer and the template for 1.5's charge pipeline.

**DO NOT build here (later stories own these):**
- `ProgressionService` / `ChargeInventory` / XP / charge-atomicity test → **Story 1.5** (same pipeline shape, different state).
- `EconomyConfig` + any cost/threshold constants → **Story 1.6**. `TrySpendCreditsAsync` takes cost as a parameter; callers will read config.
- The 20-credit first-launch grant + "granted" marker → **Story 1.7** (it will CALL `GrantCreditsAsync(20)`).
- Daily-reward claim rule → **Story 1.8**.
- `firstZeroCreditDay` write-once scalar + telemetry counters → **Story 1.9** (it needs `IClock` day-buckets and the telemetry seam; do NOT set it when a spend reaches zero here, even though the field already exists in `SaveModel`).
- Shop navigation / top-up UI reaction to `OnInsufficientCredits` → **Story 6.3 / Epic 5.4** (AR-11: this service only raises the event).
- Lure/Slay actions that actually spend → **Epic 4**.

### Design decisions a dev agent must NOT re-litigate

1. **Balance lives in `SaveModel.Credits`, period.** `CreditService` holds no duplicate field — `Balance` reads `SaveService.Current.Credits`. A cached copy would desynchronize from `StartFreshAsync`/`RetryLoadAsync` recovery swaps and from later stories mutating the same model. [Source: docs/architecture.md#Data Architecture; Assets/Veilwalkers/Persistence/SaveService.cs]
2. **Persist failure → typed result, not a throw.** `SpendFailureReason` was deliberately shipped in 1.2 with a doc-comment reserving extension for 1.4; append `PersistenceFailed = 2`. The "never throws" promise in AC-1 is scoped to *expected* failures, but NFR-3's standard (typed results + guidance, never uncaught exceptions reaching the player) argues the infra failure is also a result, not an exception — UI can show "couldn't save, try again" off `FailureReason`. Guards for programmer errors (negative cost, uninitialized service) still throw. [Source: Assets/Veilwalkers/Core/SpendFailureReason.cs; docs/architecture.md#Process Patterns]
3. **Async signature with the `Async` suffix.** AR-8 says "Every spend persists before the action is considered committed", so the method awaits `SaveAsync` and must be `Task<SpendResult> TrySpendCreditsAsync(int)`. The epic's `TrySpendCredits(cost)` spelling predates the naming rule; document the deviation in the doc-comment (doc-comment accuracy was a 1.2 review finding). [Source: docs/architecture.md#Naming Patterns; docs/epics.md#AR-8]
4. **`ICreditService` exists; no `ISaveService` precedent applies.** 1.3 deliberately registered `SaveService` concretely (documented AR-4 deviation), but 1.4's AC names `ICreditService` explicitly and Encounter/Billing will consume it from tiers that should mock it — the interface is required here. [Source: docs/epics.md#Story 1.4 AC; 1-3 story record (#Design decisions, item 6)]
5. **Events after commit, outside the lock, may fire on background threads.** Mutate → persist → release semaphore → raise. Because Economy uses `ConfigureAwait(false)` (deadlock-free blocking tests, same as Persistence), the post-await events can fire off the main thread; UI marshals in Epic 6 — the `SaveService` doc already sets this exact contract. Raising inside the semaphore would deadlock any synchronously-blocking subscriber. Do not introduce a main-thread dispatcher in this story. [Source: Assets/Veilwalkers/Persistence/SaveService.cs (threading doc)]
6. **The edge matrix edit is sanctioned, expected, and minimal.** The architecture guard is an explicit allowlist; `AcyclicDependencyTests` line 39 names this story's edit. One new string in Economy's row — nothing else. [Source: Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs; memory: unity-asmdef-architecture-test]
7. **No "perform" stage in the ledger.** Architecture §Process Patterns spells the atomic pipeline "validate → deduct → **perform** → persist"; the *perform* stage is the Epic 4 action (Lure/Slay) the CALLER runs — the ledger's own pipeline is validate → deduct → persist, exactly as the epic AC states. Do not invent a perform hook or callback parameter here. [Source: docs/architecture.md#Process Patterns; docs/epics.md#Story 1.4]

### Current repo state (verified on disk at baseline 9c2917a)

- Stories 1.1–1.3 done. Working tree clean.
- **`Veilwalkers.Economy` is an EMPTY assembly** — only the asmdef exists (references `["Veilwalkers.Core"]`); the Editor's "no scripts" notice disappears this story.
- **Core types ready to consume:** `SpendResult` (readonly struct, `Succeeded`/`Failed` factories, `Failed(None)` throws), `SpendFailureReason { None = 0, InsufficientCredits = 1 }` (doc-comment reserves 1.4 extension, append-only), `Result` (Ok/Fail + never-null Message), `GameLog`, `GameServices` (wire-once, `ResetForTests` under `UNITY_INCLUDE_TESTS`), `IClock`/`SystemClock` (not needed this story).
- **Persistence surface 1.4 builds on:** `SaveService` — `Current` (null before init / while corrupt), `InitializeAsync` (load-or-create; fresh model `Credits == 0`), `SaveAsync()` (faults on failure SO CALLERS CAN ROLL BACK — the doc literally names Stories 1.4/1.5), thread-safe `Status`/`Current`, AR-19 events; `IProgressStore` (LoadAsync/SaveAsync/Exists/DeleteAsync) — the fake-store seam for tests; `SaveModel.Credits` is `int` with doc "Currency is always an integer".
- **`Bootstrap.WireServices()`** registers `IClock`, `IProgressStore`, `SaveService`, seals, then fire-and-forgets `InitializeAsync` with an observed fault. Construct-all-before-register is the documented failure-safety pattern — extend it, don't restructure it.
- **`Assets/Tests/EditMode/`** has `Architecture.Tests` (13 green; matrix at `AcyclicDependencyTests.AllowedReferences`) and `Persistence.Tests` (26 green; its asmdef is the shape to copy). Loader excludes `*.Tests`-suffixed asmdefs from the matrix.
- Unity Editor **2022.3.62f3**; UTF **1.1.33** (no `async Task` tests); Newtonsoft `com.unity.nuget.newtonsoft-json` 3.2.1 (Economy.Tests does NOT need a Newtonsoft reference — no JSON parsing in these tests).
- `deferred-work.md` contains NO items owned by Story 1.4 (1.3 absorbed the Bootstrap lifecycle items; remaining deferrals → 3.5, 6.x, AR-21).

### Threading & pipeline traps (carry-forward from the 1.3 review)

- **`SaveService.SaveAsync` serializes the live model on the CALLING thread** (review patch): the snapshot taken is whatever `Current` holds at the moment of the call. The documented safety contract is *mutations main-thread-only and every `SaveAsync` awaited before the next mutation*. CreditService's `SemaphoreSlim(1,1)` around mutate+persist+rollback enforces the "awaited before next" half mechanically for all credit mutations.
- **Rollback must restore, not recompute — and onto the CAPTURED model reference, not via the `Current` property:** capture `priorBalance` before the deduct and assign it back on fault (`Current.Credits += cost` after a fault would be wrong if anything else mutated meanwhile), and use the model reference taken at operation entry so a recovery swap (`StartFreshAsync`/`RetryLoadAsync`) mid-mutation can never make the rollback write onto a different model than the deduct touched. Recovery concurrent with a mutation is outside the documented sequencing contract anyway — the captured reference just makes the failure mode structurally impossible.
- **`OnSaveStarted`/`OnSaveCompleted` (SaveService) will also fire during credit persists** — that's the AR-19 surface working as designed; CreditService neither subscribes to nor re-raises them.
- **UTF fails tests that observe an error log**, including from thread-pool continuations: the rollback test path triggers `GameLog.Error` in `SaveService.SaveAsync`'s catch. Handle deliberately (`LogAssert.Expect(LogType.Error, …)` before the call) — do not weaken the production log level to dodge the test runner.
- No UnityEngine API inside any `Task.Run`/background path (CreditService shouldn't need `Task.Run` at all — it awaits SaveService, which owns the thread-hop).

### Testing Requirements

- New surface: `Assets/Tests/EditMode/Economy.Tests/` (own Editor-only asmdef per Task 6). In scope: the Task 6 list — typed-result spend paths (happy/exact/insufficient), grant, persist-failure rollback both ways, guard throws, event order/non-firing, wiring happy path.
- Explicitly NOT in scope: charge atomicity (1.5), config-driven costs (1.6), first-launch grant semantics (1.7), `LocalProgressStore` file/crypto behavior (proven in 1.3 — use the fake store), device/IL2CPP verification (AR-21 release gate).
- **Headless run (the verification gate, same as 1.1–1.3):**
  `"/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode -projectPath "C:/Users/james/Desktop/Veilwalkers" -runTests -testPlatform EditMode -testResults "<abs>/test-results.xml" -logFile "<abs>/editor-test.log" -nographics`
  **Two traps, both fake an exit 0:** (1) project open in the Editor → `HandleProjectAlreadyOpenInAnotherInstance`, no XML written — `Temp/UnityLockfile` existing = locked; ask James to close the Editor. (2) compile error → no XML, crash handler exits 0 — never trust exit 0; confirm `test-results.xml` exists AND grep the log for `error CS`. Delete `test-results.xml`/`editor-test.log` before committing (not gitignored).

### Previous Story Intelligence (from Story 1.3)

- **Write the negative-path tests in the first pass** (guard throws, rollback, event-non-firing) — review keeps finding their absence; 1.3 shipped 26 tests up front and still took 9 patches, mostly on edge paths nobody had a test for.
- **Doc-comments must be true**: 1.3 review caught a doc promising `Current` null-on-corrupt that the code didn't honor, and a wiring-recovery narrative with no invoker. Every claim in CreditService's doc-comment must have a matching code path or test.
- **`ConfigureAwait(false)` everywhere in pure-logic assemblies** — proven to let EditMode tests block via `GetAwaiter().GetResult()` deadlock-free.
- **Default-struct invariants**: `SpendResult` is a readonly struct; `default(SpendResult)` is `{ Success=false, NewBalance=0, FailureReason=None }` — an incoherent value (failed-but-None). Never return `default`; always use the factories.
- **Asmdefs as plain JSON with name-based references** import cleanly; no GUID churn.
- **Review pattern that worked:** implementation commit, then review-patch commit, kept history reviewable. Headless full-suite green is the gate before review.
- Events that fire during scene-0 boot have no subscribers (deferred-work item on `SaveService`'s surface) — same reality applies to `OnCreditsChanged` during Bootstrap-time grants; Epic 6 owns replay/binding contracts. Don't add replay-on-subscribe now.

### Git Intelligence

- Baseline: `9c2917a` ("Story 1.3: code review passed (11 patches applied, 6 regression tests) → done"). Working tree clean.
- Commit convention: `Story 1.4: <what>`; story-record commits like `Story 1.4: create story → ready-for-dev`.
- Recent history shows the rhythm: create story → validate (optional) → implement → manual check if a runtime path is new → review patches → done. 1.4 has no new runtime path, so the manual-check commit step collapses away.

### Project Structure Notes

- Production code in `Assets/Veilwalkers/Economy/` (namespace `Veilwalkers.Economy`), one public type per file: `ICreditService.cs`, `CreditService.cs`, `InsufficientCreditsEvent.cs`. Core edit: `SpendFailureReason.cs` (append one member). App edit: `Bootstrap.cs` (one construction + one registration). Test edit: `AcyclicDependencyTests.cs` (one matrix row entry). Asmdef edit: `Veilwalkers.Economy.asmdef` (one reference).
- Tests in `Assets/Tests/EditMode/Economy.Tests/` (parallel Tests tree per architecture).
- **Variance note:** the architecture tree lists `ProgressionService.cs`, `ChargeInventory.cs`, `EconomyConfig.cs` under Economy — those are Stories 1.5/1.6, not this story. The directory tree is the map, not this story's scope.

### References

- [Source: docs/epics.md#Story 1.4: Grant and spend Credits through the ledger] — story + the four ACs.
- [Source: docs/epics.md#AR-7] — typed-result spends, integer currency.
- [Source: docs/epics.md#AR-8] — one atomic save write per player action; rollback on persist failure (this story's pipeline IS the pattern 1.5/4.x repeat).
- [Source: docs/epics.md#AR-11 / docs/architecture.md#Architectural Boundaries] — services never open Shop/Billing/UI; they raise `OnInsufficientCredits`; App/UI decides.
- [Source: docs/epics.md#AR-6] — event-driven UI; `OnCreditsChanged` naming.
- [Source: docs/architecture.md#Communication Patterns / #Process Patterns] — typed results, validate→deduct→persist, GameLog only.
- [Source: docs/architecture.md#Edge Cases & Failure Handling ("Exact-balance spend")] — AC-3's boundary rule.
- [Source: Assets/Veilwalkers/Persistence/SaveService.cs] — the persist seam: `SaveAsync` faults for rollback (doc names 1.4); threading contract.
- [Source: Assets/Veilwalkers/Core/SpendResult.cs / SpendFailureReason.cs] — pre-built result shape; reserved enum extension.
- [Source: Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs] — allowed-edge matrix; this story's sanctioned row edit.
- [Source: _bmad-output/implementation-artifacts/1-3-persist-player-state-with-encrypted-atomic-local-save.md] — persistence surface, threading decisions, test/asmdef patterns, headless gate.
- [Source: CLAUDE.md#Working conventions] — commit + push every session; never commit secrets.

### Latest Tech Information

No new external technology this story — pure C# over the existing Unity 2022.3.62f3 / UTF 1.1.33 / Newtonsoft 3.2.1 stack, all constraints carried from 1.3 (no `async Task` tests; `ConfigureAwait(false)` in logic assemblies; no UnityEngine API off the main thread).

### Project Context Reference

No `project-context.md` exists (the `persistent_facts` glob `**/project-context.md` matched nothing). Authoritative context: `docs/architecture.md`, `docs/epics.md`, `CLAUDE.md`, the 1.1–1.3 story records, and `deferred-work.md`.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
