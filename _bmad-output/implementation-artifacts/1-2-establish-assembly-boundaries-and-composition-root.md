---
baseline_commit: 520d352
---

# Story 1.2: Establish assembly boundaries and composition root

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want per-area assemblies, the shared Core contracts, and a single composition root,
So that the one-way dependency graph is enforced and services resolve from one place.

## Acceptance Criteria

**AC-1 — Per-area assemblies form the one-way, acyclic graph**
**Given** the project structure
**When** assemblies are defined
**Then** `.asmdef` files exist for **Core, Persistence, Economy, Monsters, AR, Encounter, Billing, App, UI** with references forming the one-way graph `Core ← {Persistence, Economy, Monsters} ← {AR, Encounter, Billing} ← App ← UI`
**And** no service assembly references **UI** or **App**
**And** the `Pit/` folder exists with **NO** asmdef.
[Source: docs/epics.md#Story 1.2; docs/epics.md#AR-5; docs/architecture.md#Architectural Boundaries]

**AC-2 — Shared contract types live in Core (below AR)**
**Given** `Core/Contracts`
**When** shared contract types are created
**Then** `AnchorToken` and `AnchorRestoreResult` (enum: `Restored`, `RelocatedToPlane`, `Failed`) live in **Core** (below AR)
**And** `Result`, `SpendResult { Success, NewBalance, FailureReason }`, `IClock`/`SystemClock`, and `GameLog` (Debug/Info/Warn/Error) exist in **Core**.
[Source: docs/epics.md#Story 1.2; docs/architecture.md#Architectural Boundaries; docs/architecture.md#Complete Project Directory Structure]

**AC-3 — Composition root is wired once and read-only**
**Given** the composition root
**When** `GameServices` is wired at App/Bootstrap
**Then** it is populated **once** and **read-only** thereafter
**And** `GameServices.Get<T>()` throws `ServicesNotReadyException` (**not null**) if called before wiring
**And** `GameServices.ResetForTests()` exists for test isolation
**And** an `Architecture.Tests` test walks the compiled assemblies and **fails** if any Veilwalkers asmdef references a higher tier.
[Source: docs/epics.md#Story 1.2; docs/epics.md#AR-4; docs/epics.md#AR-20; docs/architecture.md#Architectural Boundaries]

## Tasks / Subtasks

- [ ] **Task 1 — Create the `Assets/Veilwalkers/` area folders + asmdefs (AC: #1)**
  - [ ] Create the first-party root `Assets/Veilwalkers/` and one folder per area: `Core/`, `Persistence/`, `Economy/`, `Monsters/` (with `Monsters/Codex/`), `AR/`, `Encounter/`, `Billing/`, `App/`, `UI/`, plus the empty `Pit/` folder. (Codex folds into Monsters; Pit gets **no** asmdef.) [Source: docs/architecture.md#Complete Project Directory Structure]
  - [ ] Add one `.asmdef` per area named **`Veilwalkers.<Area>`** (e.g., `Veilwalkers.Core.asmdef`, `Veilwalkers.Economy.asmdef`, …). Root namespace for each = `Veilwalkers.<Area>` (namespace mirrors folder path). [Source: docs/architecture.md#File & Project Structure Patterns]
  - [ ] Wire each asmdef's **Assembly Definition References** to encode ONLY the allowed downward edges (see the explicit reference table in Dev Notes → "Asmdef reference matrix"). Do **not** add upward or sideways references.
  - [ ] Confirm **no** service asmdef (Core/Persistence/Economy/Monsters/AR/Encounter/Billing) references `Veilwalkers.UI` or `Veilwalkers.App`. [Source: docs/epics.md#Story 1.2]
  - [ ] Create `Assets/Veilwalkers/Pit/` as a **folder only** (a `.gitkeep` or a placeholder `.md` is fine to keep it tracked) — **NO asmdef**. [Source: docs/architecture.md#Complete Project Directory Structure ("Pit has no asmdef until it has code")]

- [ ] **Task 2 — Author the shared Core contract types (AC: #2)**
  - [ ] `Core/Contracts/AnchorToken.cs` — `[Serializable]` value type holding a trackable id (string) + session-relative pose (position/rotation). It must be serializable so `EncounterSnapshot`/`SaveModel` can store it WITHOUT depending upward on AR. (Field shape can be minimal now; later stories fill behavior.) [Source: docs/architecture.md#Architectural Boundaries ("Shared contracts below AR")]
  - [ ] `Core/Contracts/AnchorRestoreResult.cs` — `enum { Restored, RelocatedToPlane, Failed }`. [Source: docs/epics.md#Story 1.2; docs/architecture.md#Complete Project Directory Structure]
  - [ ] `Core/Result.cs` — a small typed-result type (success/failure + optional reason) used by the "typed result, never throw for expected failure" convention. [Source: docs/architecture.md#Communication Patterns]
  - [ ] `Core/SpendResult.cs` — `SpendResult { bool Success; int NewBalance; <reason> FailureReason }`. Callers check `Success`; this type NEVER throws for "can't afford". (Story 1.4 consumes it; define the shape now.) [Source: docs/epics.md#Story 1.2; docs/architecture.md#Communication Patterns]
  - [ ] `Core/IClock.cs` + `Core/SystemClock.cs` — the time seam. No direct `Time.time`/`DateTime.Now` in logic; logic reads `IClock`. `SystemClock` is the production impl. [Source: docs/architecture.md#Complete Project Directory Structure ("IClock.cs / SystemClock.cs — time seam")]
  - [ ] `Core/GameLog.cs` — logging wrapper with levels **Debug/Info/Warn/Error** (wraps `Debug.Log*` once; the rest of the codebase calls `GameLog`, never raw `Debug.Log`). [Source: docs/architecture.md#Process Patterns; docs/architecture.md#Enforcement Guidelines]
  - [ ] One **public type per file**, file name == type name. [Source: docs/architecture.md#File & Project Structure Patterns]

- [ ] **Task 3 — Build the composition root `GameServices` + `ServicesNotReadyException` (AC: #3)**
  - [ ] `Core/GameServices.cs` — a wiring table (type → instance) that is **populated once** and **read-only thereafter**. Provide a wiring API callable only at Bootstrap (e.g., `Register<T>(impl)` / a `Wire(...)`/`MarkReady()` finalizer) and a read API `Get<T>()`. [Source: docs/architecture.md#Architectural Boundaries ("populated ONCE … read-only thereafter")]
  - [ ] `Get<T>()` throws **`ServicesNotReadyException`** (a clear, named exception — define it, e.g. in Core) if called before wiring is complete — **never return null**. [Source: docs/epics.md#Story 1.2; docs/epics.md#AR-4; docs/architecture.md#Services-not-ready race]
  - [ ] Add `GameServices.ResetForTests()` to clear the table for test isolation, plus a path for per-test override registration. [Source: docs/epics.md#Story 1.2; docs/architecture.md#Architectural Boundaries ("ResetForTests() + per-test override registration")]
  - [ ] **Locator confinement:** `GameServices` lives in Core but the *rule* is enforced by convention — only **App/Bootstrap** wires it and only **MonoBehaviours/UI** read it. Pure-logic areas (Economy, Encounter, Monsters, Persistence, Billing) take dependencies via **constructor/`Init(...)` injection** and must NOT call `GameServices.Get<T>()`. Add a short XML-doc comment on `GameServices` stating this rule. [Source: docs/architecture.md#Architectural Boundaries ("Service access / locator rule")]
  - [ ] `App/Bootstrap.cs` — a minimal Bootstrap MonoBehaviour stub that is the single place that wires `GameServices`. It may be a near-empty shell now (no real services exist yet to wire — they arrive in 1.3+); its job in this story is to be the *named, single* composition entry point and to prove the wire-once → read pattern. Do NOT have Bootstrap own the AR session. [Source: docs/architecture.md#Complete Project Directory Structure; docs/architecture.md#AppStateMachine / Services-not-ready race]

- [ ] **Task 4 — Author `Architecture.Tests` acyclicity test (AC: #3)**
  - [ ] Create `Assets/Tests/EditMode/Architecture.Tests/` with its own `Veilwalkers.Architecture.Tests.asmdef` (EditMode test asmdef; references the Unity Test Framework + the Veilwalkers assemblies it needs to inspect). [Source: docs/architecture.md#Complete Project Directory Structure; docs/architecture.md#File & Project Structure Patterns ("Tests … own .asmdef")]
  - [ ] Write a test that walks the compiled assemblies via `UnityEditor.Compilation.CompilationPipeline.GetAssemblies()` and **fails** if any Veilwalkers asmdef references a **higher tier** in the graph `Core ← {Persistence, Economy, Monsters} ← {AR, Encounter, Billing} ← App ← UI`. [Source: docs/epics.md#Story 1.2; docs/epics.md#AR-5; docs/epics.md#AR-20; docs/architecture.md#Architectural Boundaries]
  - [ ] Add a focused unit test for the locator: `Get<T>()` before wiring throws `ServicesNotReadyException`; after wiring returns the registered instance; `ResetForTests()` returns it to the unready state. [Source: docs/epics.md#AR-4]
  - [ ] **Do NOT** author the `MonsterDatabase.Count == 67` test here — that lands with Epic 2 (no MonsterDatabase exists yet). Only the **acyclicity** assertion belongs to this story. [Source: docs/epics.md#AR-20 ("acyclicity" is the Epic 1 slice); docs/epics.md#Epic 2 (Count==67)]

- [ ] **Task 5 — Verify, build-gate, and commit**
  - [ ] Reopen the project clean: **zero compile errors**, all assemblies compile, and the EditMode `Architecture.Tests` **pass** (Test Runner → EditMode). [Source: docs/architecture.md#Tests]
  - [ ] Sanity-check the dependency graph in the Editor (each asmdef inspector shows only the allowed references; no service asmdef lists `Veilwalkers.UI`/`Veilwalkers.App`).
  - [ ] Confirm all new `.cs`, `.asmdef`, and `.meta` files are tracked (asmdefs and their `.meta` are committed; generated `.csproj`/`.sln` remain gitignored). [Source: CLAUDE.md#Working conventions; docs/architecture.md#Complete Project Directory Structure]
  - [ ] Commit + push at end of session (per CLAUDE.md). [Source: CLAUDE.md#Working conventions]

## Dev Notes

### What this story IS (and is NOT)

This is the **structural spine** of the whole codebase: it creates the `Assets/Veilwalkers/` tree, one `.asmdef` per area, the shared Core contract types, the `GameServices` composition root, and the `Architecture.Tests` acyclicity guard. Story 1.1 explicitly stopped short of this and handed it off: *"DO NOT create any assembly definitions, services, `Assets/Veilwalkers/` folder tree, or C# code in this story. Assembly boundaries + the composition root are Story 1.2."* [Source: _bmad-output/implementation-artifacts/1-1-initialize-unity-ar-project-with-pinned-packages.md#What this story is (and is NOT)]

**DO** (this story): folders, asmdefs (with correct reference edges), the Core contract/seam types listed in Task 2, `GameServices` + `ServicesNotReadyException`, a minimal `Bootstrap`, and the acyclicity test.

**DO NOT** (later stories own these — create the *folder/asmdef* home but NOT the service logic):
- `SaveService`/`LocalProgressStore`/`SaveModel`/`AesCrypto` → **Story 1.3**.
- `CreditService` + the real spend pipeline → **Story 1.4** (you define `SpendResult`'s *shape* here; the service that returns it is 1.4).
- `ProgressionService`/`ChargeInventory` → **Story 1.5**. `EconomyConfig` → **Story 1.6**.
- `MonsterDefinition`/`MonsterDatabase`/`Rarity`/`CodexService` and the `Count==67` test → **Epic 2**.
- AR services, Encounter services, Billing services → Epics 3/4/5.
Leaving these as empty (or near-empty) asmdef'd folders now is correct — the empty assembly is the *boundary*; the code fills in later. (Exception: an asmdef on an assembly with **zero** .cs files still compiles fine in Unity, so empty service folders + their asmdef are OK.)

### Current repo state (verified on disk at baseline 520d352)

Unity is initialized (Story 1.1 done). `Assets/` currently contains only Unity/template + XR output: `Assets/{Resources, Scenes, Settings, TutorialInfo, XR}`. **There is NO `Assets/Veilwalkers/` folder and NO `.asmdef` files anywhere yet** — this story creates them. [Verified: `find Assets` + `find Assets -name *.asmdef` → no asmdefs.]

- `Packages/manifest.json` already includes **`com.unity.test-framework` 1.1.33** — so EditMode test asmdefs (NUnit + `UnityEditor.Compilation`) work with no new package. [Verified on disk.]
- `com.unity.nuget.newtonsoft-json` 3.2.1 is present (needed in 1.3, not here).
- The template left `Assets/TutorialInfo/` (URP template sample). It's harmless; leave it unless it causes a namespace/asmdef clash (it won't — it has no asmdef). Do not fold it into `Veilwalkers/`.

### Asmdef reference matrix (the heart of AC-1 — encode EXACTLY these edges)

Graph (one-way): `Core ← {Persistence, Economy, Monsters} ← {AR, Encounter, Billing} ← App ← UI`.
`A ← B` means **B references A** (B depends on A). In each asmdef, "Assembly Definition References" lists ONLY what that assembly is allowed to depend on:

| Assembly | References (allowed) | MUST NOT reference |
|---|---|---|
| `Veilwalkers.Core` | *(none — first-party root)* | everything else |
| `Veilwalkers.Persistence` | Core | Economy, Monsters, AR, Encounter, Billing, App, UI |
| `Veilwalkers.Economy` | Core | Persistence*, Monsters, AR, Encounter, Billing, App, UI |
| `Veilwalkers.Monsters` | Core | Persistence, Economy, AR, Encounter, Billing, App, UI |
| `Veilwalkers.AR` | Core | App, UI, Encounter, Billing |
| `Veilwalkers.Encounter` | Core, Economy, Monsters, AR | App, UI, Billing |
| `Veilwalkers.Billing` | Core, Economy | App, UI |
| `Veilwalkers.App` | Core, Persistence, Economy, Monsters, AR, Encounter, Billing | UI |
| `Veilwalkers.UI` | Core, App, (+ any service tier it renders) | *(top tier — may depend down)* |

\* **Economy ↔ Persistence note:** the architecture's canonical one-line graph puts both Persistence and Economy on the **same tier under Core** (`Core ← {Persistence, Economy, Monsters}`), implying Economy does NOT reference Persistence directly. But the spend pipeline (1.4/1.5) is "validate → deduct → **persist**", and services persist via `IProgressStore`. The clean resolution the architecture intends: **services depend on the `IProgressStore` *interface* which lives in Persistence**, so Economy referencing `Veilwalkers.Persistence` for the interface is the expected edge in 1.4+. For THIS story (no service logic yet) keep Economy's references = **Core only**; when 1.3 introduces `IProgressStore` and 1.4 wires the spend pipeline, Economy will add a Persistence reference. Document this as a known forward-edge so the acyclicity test author doesn't forbid `Economy → Persistence` (it's still downward/same-tier-toward-Core, NOT upward, so it never violates the "no higher tier" rule). [Source: docs/architecture.md#Architectural Boundaries; docs/architecture.md#Process Patterns ("validate → deduct → persist"); docs/epics.md#Story 1.4]

**Acyclicity test must assert the *negative*:** no Veilwalkers asmdef references a **higher** tier. Define tier rank: Core=0; Persistence/Economy/Monsters=1; AR/Encounter/Billing=2; App=3; UI=4. For every Veilwalkers assembly, every Veilwalkers reference it declares must have a tier rank **≤** its own (i.e., never strictly greater). The test FAILS if any reference points to a strictly-higher tier. This is robust to the Economy→Persistence same-tier edge above. [Source: docs/epics.md#AR-5; docs/epics.md#AR-20; docs/architecture.md#Architectural Boundaries]

### Composition root rules (AC-3 specifics)

- **Wire once, read-only after.** `GameServices` is a wiring table populated **once** at the App/Bootstrap composition root, then read-only. After a `MarkReady()`/finalize call, further registration is rejected (throw or assert). [Source: docs/architecture.md#Architectural Boundaries]
- **`Get<T>()` before ready → `ServicesNotReadyException`, never null.** This is the "services-not-ready race" guard: Bootstrap wires first, then loads the rest; anything that reads too early gets a clear exception, not a `NullReferenceException`. [Source: docs/epics.md#AR-4; docs/architecture.md#Services-not-ready race]
- **Locator confinement (convention, called out in code comments):** only App/Bootstrap *wires*; only MonoBehaviours/UI *read* `Get<T>()`. Pure-logic services receive deps via constructor/`Init(...)`. This keeps services unit-testable without the locator and is half the reason the asmdef graph stays acyclic. [Source: docs/architecture.md#Architectural Boundaries ("Service access / locator rule")]
- **`ResetForTests()`** clears the table so each test starts unready and can register its own fakes. The acyclicity + locator tests depend on this. [Source: docs/architecture.md#Architectural Boundaries]
- **`AppStateMachine` is NOT built here.** The directory diagram lists `AppStateMachine.cs` conceptually under Core's listing but the boundaries section is explicit it "lives in the App/Bootstrap tier (not a Core service)" and it owns scene flow + Shop navigation. That's Epic 6 (Story 6.3). Do not implement it in 1.2 — just leave the App asmdef ready to host it. [Source: docs/architecture.md#Architectural Boundaries ("AppStateMachine"); docs/epics.md#Story 6.3]

### Contract type shapes (Task 2 — enough to compile + be consumed later, no behavior)

- `AnchorToken` — `[Serializable]`; minimally a `string trackableId` + a serializable pose (e.g., `Vector3 position`, `Quaternion rotation`, or a `Pose`). It lives in `Core/Contracts` precisely so `EncounterSnapshot` (Encounter) and `SaveModel` (Persistence) can store it without referencing AR. Do not add AR-Foundation types to it (that would drag an upward dependency into Core). [Source: docs/architecture.md#Architectural Boundaries]
- `AnchorRestoreResult` — `enum { Restored, RelocatedToPlane, Failed }`, exactly these three members in this order. [Source: docs/epics.md#Story 1.2]
- `SpendResult` — `{ bool Success; int NewBalance; FailureReason FailureReason }`. Credits are **integers only** (no floats) — `NewBalance` is `int`. `FailureReason` can be an enum (e.g., `None, InsufficientCredits, …`) you define now and extend in 1.4. Never throws for "can't afford". [Source: docs/architecture.md#Communication Patterns; docs/architecture.md#Data & Serialization Formats ("integers only")]
- `Result` — a lightweight success/failure (+ optional message) for ops that don't return a balance. [Source: docs/architecture.md#Communication Patterns]
- `IClock`/`SystemClock` — `IClock` exposes the current time (e.g., `DateTime UtcNow`); `SystemClock` returns `DateTime.UtcNow`. Daily-reward/ad-cap logic (1.8/1.9) will read this so it's fakeable in tests. Dates are **ISO-8601 UTC**. [Source: docs/architecture.md#Data & Serialization Formats; docs/architecture.md#Complete Project Directory Structure]
- `GameLog` — static (or injectable) wrapper exposing `Debug/Info/Warn/Error`. Internally calls `UnityEngine.Debug.Log*`. This is the ONLY place raw `Debug.Log` is allowed in shipping paths. [Source: docs/architecture.md#Process Patterns; docs/architecture.md#Enforcement Guidelines]

### Testing Requirements

- This story introduces the **first automated-test surface**: the EditMode `Architecture.Tests` assembly. Use Unity Test Framework (already pinned at 1.1.33) + NUnit. Tests live under `Assets/Tests/EditMode/` with their own `.asmdef` (test asmdefs reference the assemblies under test and the TestRunner; mark as `Editor`-platform / `includePlatforms: [Editor]` for EditMode). [Source: docs/architecture.md#File & Project Structure Patterns; docs/architecture.md#Complete Project Directory Structure]
- **In scope for 1.2:** (1) acyclicity assertion over `CompilationPipeline.GetAssemblies()`; (2) `GameServices` locator behavior (throws-before-ready, returns-after-wire, `ResetForTests` resets). [Source: docs/epics.md#AR-5, #AR-4, #AR-20]
- **Explicitly NOT in scope:** `MonsterDatabase.Count == 67` (Epic 2 — no DB yet), charge-atomicity test (Story 1.5), save-tamper test (Story 1.3). The AR-20 item bundles several tests across the epic; 1.2 owns only acyclicity. [Source: docs/epics.md#AR-20; docs/epics.md#Story 1.3 / 1.5 / Epic 2]
- Run via **Test Runner → EditMode** and confirm green before commit. There is no PlayMode surface in this story.

### Previous Story Intelligence (from Story 1.1)

- **Environment is now real:** Unity Hub + **Unity 2022.3.62f3** + Android (and iOS) Build Support are installed; the project opens clean with 0 console errors. So unlike 1.1's first pass, the Editor IS available — Task 1–5 can be executed live. [Source: 1-1…md#Completion Notes (2026-06-11 — resumed & finished)]
- **The iOS module is installed** (it was added to clear the ARCore-Extensions `UnityEditor.iOS` compile error). Don't be surprised to see iOS build support present; we never build iOS. [Source: 1-1…md#Completion Notes]
- **Unity folders were relocated into the repo root** (Hub refused to create into the non-empty repo, so a temp shell was created then moved). Everything is now at the repo root — `Assets/`, `Packages/`, `ProjectSettings/` are normal. [Source: 1-1…md]
- **`.gitignore` is curated and correct** — it tracks `Assets/**/*.meta` and ignores `*.csproj`/`*.sln`/`Library/`. New asmdefs + their `.meta` will be tracked automatically; do not edit `.gitignore`. [Source: 1-1…md#AC-3 VERIFIED]
- **Open follow-ups carried (not this story):** package name still template default `com.UnityTechnologies.*` (→ AR-21), target SDK / .aab toggle / bundle id are release-gate (→ AR-21). None block 1.2. [Source: 1-1…md#Review Findings; #NOTE (follow-up)]
- **Process lesson:** 1.1 was edited file-level (manifest.json) because the agent can't drive the Editor GUI; asmdefs are plain JSON files too, so they CAN be authored as files (Unity picks them up on focus/reopen). If you author asmdefs by hand, get the JSON shape right (`name`, `references` by name or GUID, `includePlatforms`, etc.) and let Unity resolve on reopen. Prefer creating them via the Editor (Create → Assembly Definition) if the user is at the machine, to get correct GUID refs automatically.

### Git Intelligence

- Baseline for this story: `520d352` ("Story 1.1: code review passed → done"). Recent commits are all Story 1.1 (project init). No first-party C# exists yet — this story is greenfield code on top of a clean Unity foundation. [Verified: `git log --oneline -5`.]
- Commit-message convention in this repo: `Story 1.1: <what>` / `Story 1.1: code review passed → done`. Mirror it: `Story 1.2: <what>`.

### Project Structure Notes

- All new code goes under `Assets/Veilwalkers/<Area>/` — this is the single first-party root per the architecture. Namespaces mirror folders (`Veilwalkers.Core`, `Veilwalkers.Economy`, …). One public type per file, file name == type name. [Source: docs/architecture.md#File & Project Structure Patterns]
- Tests go under `Assets/Tests/EditMode/Architecture.Tests/` with their own asmdef (parallel `Tests/` tree per the architecture). [Source: docs/architecture.md#Complete Project Directory Structure]
- **No detected conflicts.** One variance to note: the directory diagram visually lists `AppStateMachine.cs` and several `ITelemetry*`/`AdHook` seams — those are later stories (6.3 / 1.9). 1.2 creates the *asmdef homes* (App, Core) but only the types AC-2/AC-3 name. Creating an asmdef over a folder that will gain more files later is exactly the intent. [Source: docs/architecture.md#Complete Project Directory Structure; docs/epics.md#Story 6.3 / Story 1.9]

### References

- [Source: docs/epics.md#Story 1.2: Establish assembly boundaries and composition root] — user story + ACs.
- [Source: docs/epics.md#AR-4 (Service layer + composition root)] — GameServices wire-once/read-only, ServicesNotReadyException, ResetForTests, injection vs locator.
- [Source: docs/epics.md#AR-5 (Acyclic boundaries)] — per-area asmdef one-way graph, contracts in Core/Contracts, Codex folds into Monsters, Pit = folder stub.
- [Source: docs/epics.md#AR-20 (Architecture tests)] — Architecture.Tests acyclicity (Count==67 is Epic 2; charge/tamper tests are 1.5/1.3).
- [Source: docs/architecture.md#File & Project Structure Patterns] — one-type-per-file, `Veilwalkers.<Area>` namespaces, per-area asmdef, parallel Tests/ asmdef.
- [Source: docs/architecture.md#Communication Patterns] — service-via-interface + locator, typed `SpendResult`, events.
- [Source: docs/architecture.md#Process Patterns / #Enforcement Guidelines] — GameLog wrapper, no raw Debug.Log, validate→deduct→persist (informs Economy→Persistence edge).
- [Source: docs/architecture.md#Complete Project Directory Structure] — the canonical folder/asmdef layout (incl. Core/Contracts, Pit no-asmdef, Tests layout).
- [Source: docs/architecture.md#Architectural Boundaries] — dependency direction, contracts-below-AR rationale, locator confinement rule, AppStateMachine in App tier, services-not-ready guard.
- [Source: _bmad-output/implementation-artifacts/1-1-initialize-unity-ar-project-with-pinned-packages.md] — environment now real, hand-off ("assembly boundaries are 1.2"), .gitignore correct, file-level asmdef authoring note.
- [Source: CLAUDE.md#Working conventions] — Unity-Hub init, commit + push every session, never commit secrets.

### Latest Tech Information

- **Unity Test Framework 1.1.33** (pinned) is the right vintage for Unity 2022.3 LTS; EditMode test asmdefs use `"includePlatforms": ["Editor"]` and reference `UnityEngine.TestRunner` + `UnityEditor.TestRunner` plus `nunit.framework.dll` (via `"precompiledReferences": ["nunit.framework.dll"]` and `"overrideReferences": true`). The acyclicity test uses `UnityEditor.Compilation.CompilationPipeline.GetAssemblies(AssembliesType.Editor)` (or `.Player`) and inspects each `UnityEditor.Compilation.Assembly`'s `assemblyReferences`/`name`. Because it uses `UnityEditor`, the test asmdef MUST be Editor-only. [Standard Unity 2022.3 Test Framework usage; verify exact API names in-Editor.]
- **Assembly Definition file format:** plain JSON with keys `name`, `rootNamespace`, `references` (assembly names like `"Veilwalkers.Core"` or GUID refs `"GUID:xx…"`), `includePlatforms`, `excludePlatforms`, `allowUnsafeCode`, `autoReferenced`, `defineConstraints`, `versionDefines`, `noEngineReferences`. Authoring via Editor (Create ▸ Assembly Definition) auto-manages GUIDs; hand-authoring works too but get the JSON keys right and let Unity resolve on reopen. (Story 1.1 proved hand-editing Unity JSON + reopen works.)

### Project Context Reference

No `project-context.md` exists in the repo (the BMM `persistent_facts` glob `**/project-context.md` matched nothing). Authoritative context comes from `docs/architecture.md`, `docs/epics.md`, `CLAUDE.md`, and Story 1.1's record. If a `project-context.md` is generated later, re-check the asmdef graph and namespaces against it.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
