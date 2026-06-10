---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
inputDocuments:
  - docs/prds/prd-Veilwalkers-2026-06-10/prd.md
  - docs/game-design-doc.md
workflowType: 'architecture'
project_name: 'Veilwalkers'
user_name: 'James'
date: '2026-06-10'
lastStep: 8
status: 'complete'
completedAt: '2026-06-10'
---

# Architecture Decision Document

_This document builds collaboratively through step-by-step discovery. Sections are appended as we work through each architectural decision together._

## Project Context Analysis

### Requirements Overview

**Functional Requirements:**
14 FRs across 5 feature groups (10 MVP, 1 daily-reward, 3 deferred "Pit").
- Onboarding & first-run economy (FR-1, FR-2): one-time 20-credit grant, daily reward.
- AR Mode & safety (FR-3, FR-4, FR-5): mandatory non-skippable safety gate, plane
  anchoring with occlusion+lighting, camera-permission disclosure & graceful denial.
- Encounter loop (FR-6–FR-10): Lure (Basic/Premium/Multi) → Scan/Capture/Slay,
  consumables + Retry; stateful session that must survive a Shop round-trip.
- Codex (FR-11, FR-12): "X/67" collection, detail views, local persistence.
- Shop & billing (FR-13, FR-14): Google Play Billing only, exactly-once grant,
  interruption-resilient reconciliation.
- The Pit (FR-15–FR-17): post-MVP — architect a seam, do not build.

**Non-Functional Requirements:**
- NFR-1 AR performance: stable ≥30 FPS on mid-range ARCore devices (make-or-break).
- NFR-2 Encounter responsiveness: sub-second feedback on every credit spend.
- NFR-3 Crash-free AR: AR/camera failures degrade to guidance, never crash.
- NFR-4 Billing integrity: exactly-once credit grant; resilient reconciliation.
- NFR-5 Cold-start to first hunt: short enough to preserve the first-session moment.
- NFR-6 Offline tolerance: core single-player loop works offline (local-first MVP).

**Scale & Complexity:**
- Primary domain: Android AR mobile game (Unity 2022 LTS+, C#).
- Complexity level: Medium–High (AR + billing-integrity + state-preservation).
- Estimated architectural components: ~8–10 (AR session, Encounter state machine,
  Credit/economy ledger, Billing/reconciliation, Codex/persistence, Monster data,
  Progression/XP + charge inventory, Scene/navigation+compliance gates, UI/HUD,
  future-backend seam).

### Economy Model Revision (supersedes PRD §Credit-economy table)

> **Deliberate design revision agreed during architecture.** This narrows what costs
> real-money Credits and introduces an XP-driven progression system. Downstream epics
> and stories follow THIS model, not the original PRD economy table.

**Credits (real-money currency) — only 4 paid actions:**

| Action | Cost | Notes |
|---|---|---|
| Basic Lure | 1 | Summon a normal Monster |
| Premium Lure | 4 | Higher rare chance |
| Multi-Lure | 5 | Two Monsters at once |
| Slay | 3 | Defeat for superior loot |

- **Retry is free** — re-attempting just means luring again (already paid for); there is
  no separate Retry credit cost.
- **Scan** and **base Capture** remain free.
- Credits now buy a single conceptual thing: *more / better Monsters* (Lure) and
  *converting a Monster to loot* (Slay). This directly serves SM-C1 (basic play must not
  get expensive) and keeps Premium Lure + Slay as the PRD's intended money drivers.

**XP & Progression (earned by playing — NOT purchasable):**
- Single **player-wide XP/level** pool. Capture and Slay both grant XP; Slay grants more.
- Leveling up grants **charges** of the "extras": **Strong Capture**, **Stability Boost**,
  **Nightveil Filter**.
- Using an extra **consumes one charge** (no money cost ever). Run out → earn more by
  playing. These items can never be bought with Credits.

**Persistence implication:** local store must now hold Credit balance, Codex, **player
XP/level, and per-extra charge counts**.

### Technical Constraints & Dependencies

- Engine: Unity 2022 LTS+; AR: AR Foundation + ARCore (Android-only, AR-Required).
- Billing: Google Play Billing Library (no third-party payment anywhere).
- Backend: Firebase OPTIONAL for MVP — MVP is local-first; cloud sync & server-side
  purchase verification are Phase-2 (OQ-3, the biggest architectural fork).
- Hard Play gates: mandatory per-entry AR Safety Warning; camera disclosure before
  OS prompt; Teen content rating; privacy policy.
- Target: mid-range ARCore Android devices; plane detection + occlusion are critical.
- Secrets: keystores, google-services.json, keystore.properties, billing keys all
  gitignored — must stay out of source.

### Cross-Cutting Concerns Identified

- AR session lifecycle & performance (touches Encounter, Codex previews, future Pit).
- Credit-economy integrity & exactly-once billing (touches the 4 paid actions + Shop).
- XP/progression & charge inventory (touches Encounter extras + persistence).
- Local persistence with a clean future-backend seam (Codex, balance, XP, charges, recon).
- Scene/navigation flow enforcing compliance gates (safety warning, permission disclosure).
- Teen-rated content & privacy disclosure (camera frames on-device only in MVP).

## Starter Template Evaluation

### Primary Technology Domain

Android AR mobile game — Unity 2022 LTS (C#). No web-style "create-app" starter
exists; the equivalent foundation is a Unity Hub project template + a verified
package manifest. Per project convention, the project is initialized via Unity Hub
(generated Library/, .csproj, .sln are gitignored — never hand-written).

### Starter Options Considered

- **Unity Hub "3D (URP)" template** — SELECTED. URP is the right baseline for AR
  occlusion/lighting and mid-range performance (NFR-1). 3D (no URP)/Built-in lacks
  the modern AR rendering support; HDRP is desktop/console-class, wrong for mobile AR.
- **Unity AR template (Hub)** — pre-wires AR Foundation but historically lags on
  package versions and pipeline choices; we prefer 3D (URP) + explicit, pinned AR
  packages for version control.
- **Legacy Google Play Billing Plugin for Unity** — REJECTED. Repo archived
  (Apr 2026), stuck on Billing Library 3, non-compliant with Google's
  ≥7.0.0 requirement (in force since 2025-08-30).

### Selected Foundation: Unity Hub "3D (URP)" + pinned AR/IAP packages

**Rationale for Selection:**
URP gives the best mobile-AR rendering/perf baseline for NFR-1; pinning AR
Foundation 5 + ARCore 5 is the verified pairing for Unity 2022.3 LTS; Unity IAP 5
is the only currently-compliant Google Play Billing path.

**Initialization (first implementation story):**
1. Unity Hub → New Project → **3D (URP)** template → Unity **2022.3 LTS**.
2. Package Manager — add/pin (manifest.json):
   - com.unity.xr.arfoundation : 5.0.x
   - com.unity.xr.arcore       : 5.0.x
   - com.unity.purchasing       : 5.0.0+   (Unity IAP, bundles Play Billing 8.0.0)
3. Import **ARCore Extensions for AR Foundation** (arf5 branch) from Google.
4. Player Settings: Android, Min API per ARCore, AR Required, ARM64, IL2CPP.

**Architectural Decisions Provided by this Foundation:**

- **Language & Runtime:** C# on Unity 2022.3 LTS; IL2CPP + ARM64 for Play.
- **Render Pipeline:** URP (mobile-AR optimized; occlusion/lighting support).
- **AR Layer:** AR Foundation 5 over ARCore 5 (verified pairing) + ARCore Extensions.
- **Billing:** Unity IAP 5 (Google Play Billing Library 8.0.0) — compliant; the legacy
  Google plugin is explicitly NOT used.
- **Build target:** Android only, AR Required, ARM64, IL2CPP.
- **Project structure & code organization:** established in the Architecture Decisions
  step (not dictated by the template).

**Versions verified June 2026.** Re-verify the AR Foundation 5 / ARCore 5 "verified"
pairing and Unity IAP version at implementation time.

**Note:** Project initialization via Unity Hub + the package pinning above should be
the FIRST implementation story.

## Core Architectural Decisions

### Decision Priority Analysis

**Critical Decisions (Block Implementation):**
- Backend posture (OQ-3): LOCAL-ONLY for MVP, with a clean backend seam for Phase-2 Firebase.
- Local persistence: single versioned, AES-encrypted JSON save (Newtonsoft).
- Encounter survives Shop/Billing round-trip via state-machine + snapshot/rehydrate.
- Billing path: Unity IAP 5 (Play Billing 8); client-acknowledge; exactly-once reconcile.

**Important Decisions (Shape Architecture):**
- Static vs dynamic data split: 67 Monster defs in ScriptableObjects; player state in save.
- App architecture: scene/state-driven with a service layer (no third-party DI for MVP).
- AR session ownership and compliance-gated scene flow.
- XP/progression + charge-inventory as a first-class service alongside the Credit ledger.

**Deferred Decisions (Post-MVP):**
- Firebase Firestore cloud sync + Auth (Phase 2).
- Server-side Play purchase verification via Cloud Function (Phase 2 fast-follow).
- The Pit subsystem (FR-15–17) — seam only, not built.
- Rewarded ads SDK (OQ-4, PM decision).

### Data Architecture

- **Persistence:** Local-only for MVP. Single `SaveModel` serialized with Newtonsoft.Json,
  AES-encrypted, written async to `Application.persistentDataPath`. Versioned with a
  migration path; loaded data is validated before use; atomic write (temp file + swap)
  to survive mid-write kills.
- **Save contents:** Credit balance, Codex (discovered monster IDs + per-monster data:
  scanned/captured/slain, variant flags), player XP + level, per-extra charge counts
  (Strong Capture / Stability Boost / Nightveil Filter), pending-purchase reconciliation
  records, daily-reward last-claim date.
- **Static game data:** the 67 Monster definitions (art refs, base stats, rarity, lore)
  live in **ScriptableObjects**, NOT in the save file. MVP ships 3–5 populated.
- **PlayerPrefs:** only for trivial settings (audio/graphics), never structured state.
- **Backend seam:** all reads/writes go through an `IProgressStore` interface
  (LocalProgressStore now; a future FirebaseProgressStore swaps in without touching
  callers). Same seam for an `IPurchaseVerifier` (LocalVerifier now → CloudVerifier later).

### Authentication & Security

- **No user accounts / auth in MVP** (single-player, local). Anonymous local profile only.
- **Save integrity:** AES encryption raises the bar against casual save-editing of premium
  currency; acknowledged as not cheat-proof (client-side), acceptable for single-player MVP.
- **Billing integrity (NFR-4):** Unity IAP acknowledge flow; every purchase recorded to a
  local pending-ledger BEFORE crediting; credited exactly once keyed by Play order id;
  un-acknowledged purchases reconciled on next launch (Play auto-refunds unacked).
- **Secrets:** keystores, google-services.json, keystore.properties, billing service-account
  keys remain gitignored — never in source.

### API & Communication Patterns

- **No backend API in MVP** (local-only). Internal communication is an in-app **service
  layer** with C# interfaces and events.
- **Services:** CreditService, ProgressionService (XP/charges), EncounterService,
  CodexService, BillingService, ArSessionService, SaveService — each behind an interface.
- **Event-driven UI:** services raise C# events (e.g., OnCreditsChanged, OnMonsterDiscovered);
  UI subscribes. Keeps sub-second feedback (NFR-2) decoupled from logic.
- **Error handling standard:** AR/camera/billing failures resolve to typed results +
  user-facing guidance, never uncaught exceptions (NFR-3).

### Frontend Architecture (Unity client)

- **Scene/flow:** scene- or state-driven navigation: Onboarding → Home → AR Hunt → Codex/Shop.
  AR Mode entry is gated by the **AR Safety Warning** (FR-3, every entry, non-skippable) and
  camera-permission disclosure shown BEFORE the OS prompt (FR-5).
- **State management:** central app/game state via a lightweight state machine + the service
  layer; **Encounter** is its own explicit state machine (Idle→Lured→Acting→Resolving→Resolved).
- **Shop round-trip (UJ-2):** on entering Shop, snapshot the active Encounter (lured monsters,
  per-monster state, active boosts) to memory + disk; Billing runs async; on return or relaunch,
  rehydrate the exact Encounter. Survives app-kill mid-purchase.
- **Rendering:** URP; AR occlusion + environment lighting via AR Foundation; budget for
  ≥30 FPS sustained on mid-range (NFR-1) — object pooling for monsters, capped concurrent
  spawns, LOD/light-weight shaders.
- **DI:** no third-party DI framework for MVP; simple service locator / manual composition.

### Infrastructure & Deployment

- **Build:** Unity → Android App Bundle (.aab), IL2CPP, ARM64, AR Required, min API per ARCore.
- **Distribution:** Google Play (internal → closed → soft launch tracks).
- **No server infra in MVP** (local-only). Phase-2 adds Firebase project (Firestore, Auth,
  Cloud Functions) behind the existing IProgressStore / IPurchaseVerifier seams.
- **Monitoring:** MVP relies on Play Console (crash/ANR, vitals). `[ASSUMPTION: add a crash/
  analytics SDK — e.g., Firebase Crashlytics or Unity Cloud Diagnostics — confirm vs. OQ-4
  data-safety footprint.]`
- **CI/CD:** `[ASSUMPTION: manual or Unity-cloud build for MVP; formalize later.]`

### Decision Impact Analysis

**Implementation Sequence:**
1. Project init (Unity Hub URP + pinned AR/IAP packages) — first story.
2. SaveService + IProgressStore (encrypted JSON) + ScriptableObject monster defs.
3. CreditService + ProgressionService (XP/charges).
4. AR session + safety/permission gating (FR-3/FR-5) + plane anchoring (FR-4).
5. EncounterService state machine (Lure/Scan/Capture/Slay + extras) (FR-6–10).
6. Codex (FR-11/12).
7. BillingService (Unity IAP) + Shop + snapshot/rehydrate + reconciliation (FR-13/14).

**Cross-Component Dependencies:**
- Every spend action depends on CreditService; every extra depends on ProgressionService.
- Encounter snapshot depends on SaveService; Billing reconciliation depends on SaveService.
- All persistence routes through IProgressStore so Phase-2 backend is a drop-in.
- The Pit (post-MVP) will reuse AR session + EncounterService patterns — keep them reusable.

## Implementation Patterns & Consistency Rules

These rules exist so any agent (or developer) writing Veilwalkers code produces compatible,
consistent results. They follow standard Microsoft C# + Unity conventions; deviations should
be deliberate and documented.

### Naming Patterns (C# / Unity)

- **Types** (classes, structs, enums, interfaces): `PascalCase`. Interfaces prefixed `I`
  (e.g., `IProgressStore`, `EncounterService`, `MonsterDefinition`).
- **Methods / properties / events**: `PascalCase` (e.g., `TrySpendCredits`, `OnCreditsChanged`).
- **Public fields**: avoid — prefer properties. **Private fields**: `_camelCase`
  (e.g., `_creditBalance`). **Locals / parameters**: `camelCase`.
- **Constants**: `PascalCase` (e.g., `StartingCredits = 20`). **Serialized inspector fields**:
  `[SerializeField] private` + `_camelCase`.
- **Async methods**: suffix `Async` (e.g., `SaveAsync`, `LoadAsync`).
- **Events**: name as `On<Subject><PastTense>` (e.g., `OnMonsterDiscovered`,
  `OnPurchaseCompleted`). Event handler methods: `Handle<Event>` (e.g., `HandleCreditsChanged`).
- **ScriptableObject assets**: `<Name>Definition` type, asset files `<Id>_<Name>.asset`
  (e.g., `Mon01_AntleredShade.asset`).
- **Monster IDs**: stable string keys `monNN` (mon01..mon67), never array index or display name.
- **Scenes**: `PascalCase` (`Onboarding`, `Home`, `ARHunt`, `Codex`, `Shop`).

### File & Project Structure Patterns

- **One public type per file**; file name == type name (`CreditService.cs`).
- **Namespaces**: rooted at `Veilwalkers.<Area>` (e.g., `Veilwalkers.Economy`,
  `Veilwalkers.AR`, `Veilwalkers.Encounter`, `Veilwalkers.Persistence`, `Veilwalkers.Codex`,
  `Veilwalkers.Billing`, `Veilwalkers.UI`, `Veilwalkers.Core`). Namespace mirrors folder path.
- **Assembly definitions (.asmdef)** per major area to enforce dependency direction and speed
  compiles (Core ← Economy/Persistence ← Encounter/AR/Codex/Billing ← UI). UI depends on
  services; services never depend on UI.
- **Tests**: EditMode/PlayMode tests in a parallel `Tests/` folder per area with its own
  `.asmdef` referencing the area under test (e.g., `Economy.Tests`).
- **Organize by feature/area, not by type** (no global `Scripts/Managers` dumping ground).

### Data & Serialization Formats

- **Save file**: JSON via Newtonsoft, **camelCase** field names, then AES-encrypted. Always
  include a top-level `schemaVersion` (int) for migrations.
- **Booleans** as JSON `true/false`; **dates** as ISO-8601 UTC strings; **IDs** as strings.
- **Money/credits**: integers only (no floats for currency). Credits are whole numbers.
- **Null handling**: never persist null collections — default to empty arrays; validate on load.
- **ScriptableObjects hold static data only**; never write runtime/player state into an SO.

### Communication Patterns (in-app service layer)

- **Services are accessed via their interface**, resolved through a single composition root /
  service locator (`GameServices`), never via `FindObjectOfType` or scattered singletons.
- **Cross-system signaling uses C# events** raised by services; **UI subscribes in OnEnable,
  unsubscribes in OnDisable** (mandatory — prevents leaks). No UI polling of service state.
- **Spend actions return a typed result**, never throw for "can't afford":
  `SpendResult { Success, NewBalance, FailureReason }`. Callers check `Success`.
- **State updates are explicit**: services own their state; callers mutate only through service
  methods (`TrySpendCredits`, `GrantCredits`), never by writing fields directly.

### Process Patterns

- **Error handling**: AR, camera, and billing operations return typed results / raise
  failure events with a user-facing message + a recovery path; **never** surface an uncaught
  exception to the player (NFR-3). Log via a single `GameLog` wrapper with levels
  (Debug/Info/Warn/Error), never raw `Debug.Log` in shipping logic paths.
- **Loading / pending states**: long ops (save, billing, AR init) expose an explicit status
  enum and raise start/finish events; UI reflects them. No silent blocking on the main thread.
- **Retry**: a failed Capture/Slay leaves the Encounter active and re-enables the action; Retry
  is free (re-lure / re-attempt), consistent with the economy revision — no Retry credit cost.
- **Credit/charge spends are atomic**: validate → deduct → perform → persist; on failure mid-way,
  roll back the deduction. Every spend persists before the action is considered committed.
- **Purchases**: write to local pending-ledger → acknowledge → credit exactly once keyed by Play
  order id → persist → clear pending. Reconcile pending on every launch.

### Enforcement Guidelines

**All implementers MUST:**
- Access other systems only through service interfaces + the composition root.
- Use C# events for cross-system notification; subscribe/unsubscribe in OnEnable/OnDisable.
- Keep static game data in ScriptableObjects and dynamic state in the save model only.
- Route all persistence through `IProgressStore`; never read/write save files directly elsewhere.
- Treat currency as integers and never let a spend bypass the validate→deduct→persist sequence.

**Anti-patterns (do NOT):**
- `FindObjectOfType` / ad-hoc singletons for service access.
- `Debug.Log` left in gameplay/economy code; raw exceptions reaching the player.
- Writing player state into ScriptableObjects, or reading the save JSON outside SaveService.
- Floats for credits; mutating a service's state fields from outside the service.
- Tearing down or hand-rolling the AR session outside `ArSessionService`.

## Project Structure & Boundaries

> Refined via an Assumption Audit + a multi-perspective (architect/dev/PM/UX) review.
> Key outcomes folded in: shared-contract types live below AR to keep the graph acyclic;
> ProgressionService has an explicit home; the service-locator is confined to the
> composition root; pure-logic areas use constructor/Init injection; IClock and an
> explicit AnchorRestoreResult seam are added; acyclicity and the "67" lore constraint
> are enforced by tests, not prose.

### Complete Project Directory Structure

```
Veilwalkers/                         # repo root (Unity project initialized via Unity Hub)
├── README.md
├── CLAUDE.md
├── .gitignore                       # ignores Library/, *.csproj, *.sln, secrets, keystores
├── docs/{game-design-doc.md, architecture.md, prds/…}
├── ProjectSettings/                 # Unity (committed)
├── Packages/{manifest.json, packages-lock.json}   # pinned arfoundation5/arcore5/purchasing5/newtonsoft
├── Assets/
│   ├── Veilwalkers/                 # ALL first-party code/content under one root
│   │   ├── Core/                    # Veilwalkers.Core — depends on nothing first-party
│   │   │   ├── GameServices.cs       # composition-root wiring table (see locator rule)
│   │   │   ├── GameLog.cs            # logging wrapper (Debug/Info/Warn/Error)
│   │   │   ├── AppStateMachine.cs    # lives in App/Bootstrap tier (see below), NOT a service
│   │   │   ├── IClock.cs / SystemClock.cs   # time seam (no direct Time.time/DateTime.Now in logic)
│   │   │   ├── ITelemetrySink.cs      # Count(evt, amount) — local diagnostic, swappable for Firebase
│   │   │   ├── ITelemetryStore.cs     # capped-ring storage seam (separate telemetry.json)
│   │   │   ├── Result.cs / SpendResult.cs
│   │   │   └── Contracts/            # shared serializable contract types (below AR)
│   │   │   │   ├── AnchorToken.cs     # serializable: trackable id + session-relative pose
│   │   │   │   └── AnchorRestoreResult.cs  # enum { Restored, RelocatedToPlane, Failed }
│   │   │   └── Veilwalkers.Core.asmdef
│   │   ├── Persistence/             # Veilwalkers.Persistence
│   │   │   ├── IProgressStore.cs     # seam (Local now → Firebase later)
│   │   │   ├── LocalProgressStore.cs
│   │   │   ├── SaveService.cs        # async, atomic write (temp+swap)
│   │   │   ├── SaveModel.cs          # versioned; credits, codex, xp+level, charges,
│   │   │   │                         #   encounterSnapshot(AnchorTokens), pending purchases, dailyClaim,
│   │   │   │                         #   adReward{grantsToday,dayBucket}, firstZeroCreditDay (bounded scalar)
│   │   │   ├── LocalTelemetryStore.cs # writes separate telemetry.json (NOT in SaveModel)
│   │   │   ├── AesCrypto.cs / SaveMigrations.cs
│   │   │   └── Veilwalkers.Persistence.asmdef
│   │   ├── Economy/                 # Veilwalkers.Economy  (also HOSTS ProgressionService)
│   │   │   ├── CreditService.cs      # ICreditService; integer credits; spend pipeline
│   │   │   ├── ProgressionService.cs # player-wide XP/level → grants extra CHARGES
│   │   │   ├── ChargeInventory.cs    # Strong Capture / Stability Boost / Nightveil counts
│   │   │   ├── EconomyConfig.cs       # data-driven costs/XP/charge thresholds (ScriptableObject)
│   │   │   └── Veilwalkers.Economy.asmdef
│   │   ├── Monsters/                # Veilwalkers.Monsters  (Codex folds in here)
│   │   │   ├── MonsterDefinition.cs  # ScriptableObject (static data)
│   │   │   ├── Rarity.cs
│   │   │   ├── MonsterDatabase.cs    # SO registry of the 67 (3–5 populated in MVP)
│   │   │   ├── Codex/CodexService.cs # read model over owned-monster + catalog (FR-11/12)
│   │   │   └── Veilwalkers.Monsters.asmdef
│   │   ├── AR/                      # Veilwalkers.AR — thin adapter, ZERO branching logic
│   │   │   ├── ArSessionService.cs   # sole owner of AR lifecycle; PrewarmAsync() (cold→prewarming→ready→running→paused)
│   │   │   ├── ArSafetyGate.cs       # FR-3 mandatory warning gate (cold-entry only — see rule)
│   │   │   ├── CameraPermissionFlow.cs # FR-5 disclose-before-prompt
│   │   │   ├── PlaneAnchorService.cs # FR-4 anchoring/occlusion/lighting + guidance
│   │   │   ├── IArAnchorProvider.cs   # SaveAnchor→AnchorToken; TryRestoreAnchor(token,out result)
│   │   │   ├── MonsterSpawner.cs     # object pooling, spawn caps
│   │   │   └── Veilwalkers.AR.asmdef
│   │   ├── Encounter/               # Veilwalkers.Encounter
│   │   │   ├── EncounterService.cs   # FR-6–10 actions (constructor/Init injection)
│   │   │   ├── EncounterStateMachine.cs # Idle→Lured→Acting→Resolving→Resolved
│   │   │   ├── EncounterSnapshot.cs  # stores AnchorTokens + per-monster STATE (not live refs)
│   │   │   ├── LureSystem.cs / CaptureSystem.cs / SlaySystem.cs / ExtrasSystem.cs
│   │   │   └── Veilwalkers.Encounter.asmdef
│   │   ├── Billing/                 # Veilwalkers.Billing
│   │   │   ├── BillingService.cs / IPurchaseVerifier.cs / LocalPurchaseVerifier.cs
│   │   │   ├── CreditPackCatalog.cs / PurchaseReconciler.cs
│   │   │   └── Veilwalkers.Billing.asmdef
│   │   ├── App/                     # Veilwalkers.App — Bootstrap/composition tier (above UI)
│   │   │   ├── Bootstrap.cs           # wires GameServices; LoadPhase{EssentialSync,WarmupAsync,Ready}; triggers PrewarmAsync (does NOT own AR)
│   │   │   ├── LocalTelemetrySink.cs  # ITelemetrySink impl (local diagnostic)
│   │   │   ├── AdHook.cs (stub)       # OQ-4 seam: GrantReward→ProgressionService.AddCharge (weakest/low-tier charge; never Credits/XP); ads/day cap via IClock
│   │   │   └── Veilwalkers.App.asmdef
│   │   ├── UI/                      # Veilwalkers.UI (views; subscribe to events)
│   │   │   ├── Onboarding/ Home/ ARHud/ Codex/ Shop/ Common/
│   │   │   └── Veilwalkers.UI.asmdef
│   │   ├── Pit/                     # POST-MVP — folder only, NO asmdef until real code exists
│   │   ├── Art/ Audio/ ScriptableObjects/ Scenes/ Settings/
│   ├── Plugins/ (Android native bits as needed)
│   └── Tests/
│       ├── EditMode/  (Economy.Tests, Persistence.Tests, Monsters.Tests, Encounter.Tests,
│       │               Billing.Tests, Architecture.Tests[asmdef-acyclicity, Count==67])
│       └── PlayMode/  (AR smoke, Encounter snapshot/rehydrate incl. lost-anchor, Billing reconcile)
└── (gitignored) Library/, Temp/, Logs/, *.csproj, *.sln, secrets, *.keystore, google-services.json
```

**Asmdef count note:** ~8 assemblies (Core, Persistence, Economy, Monsters, AR, Encounter,
Billing, App, UI). **Codex folded into Monsters**; **Pit has no asmdef** until it has code
(an empty boundary is ceremony, not enforcement).

### Architectural Boundaries

**Dependency direction (one-way, compile-enforced by .asmdef):**
`Core ← {Persistence, Economy, Monsters} ← {AR, Encounter, Billing} ← App ← UI`.
UI/App depend on services; **no service depends on UI/App**. **No-cycles rule is a TEST**
(`Architecture.Tests` walks `CompilationPipeline.GetAssemblies()` and asserts no Veilwalkers
asmdef references a higher tier), not just prose.

**Shared contracts below AR:** `AnchorToken` and `AnchorRestoreResult` live in
`Core/Contracts`, NOT in AR — so `EncounterSnapshot`/`SaveModel` can store them without
Persistence/Encounter depending upward on AR. `IArAnchorProvider` (the behavior) stays in AR.

**Service access / locator rule:** `GameServices` is a **wiring table populated ONCE at the
App/Bootstrap composition root, read-only thereafter**. Pure-logic areas (Economy, Encounter,
Monsters, Persistence, Billing) receive dependencies via **constructor/`Init(...)` injection**
and must NOT call `GameServices.Get<T>()`. Only MonoBehaviours/UI may read the locator.
`GameServices.ResetForTests()` + per-test override registration are required for test isolation.

**AppStateMachine:** lives in the **App/Bootstrap tier** (not a Core service). It owns scene
flow, reads `OnInsufficientCredits`, and decides Shop navigation. It is the only thing that
arbitrates UI ↔ service control flow.

**AR boundary:** `ArSessionService` is the sole owner of the AR session; AR contains **no
branching logic worth testing** (thin adapter). Encounter depends on AR only through
`MonsterSpawner` (spawn) and `IArAnchorProvider` (anchor). On rehydrate, Encounter calls
`TryRestoreAnchor(token, out AnchorRestoreResult)`:
- `Restored` → re-anchor in place.
- `RelocatedToPlane` → anchor lost; re-place on the nearest plane **within the current camera
  frustum** (fall back to absolute-nearest only if nothing is in view) with a brief diegetic
  "pulled back through the veil" beat (reuse Lure materialization VFX) — **never vanish**.
- `Failed` → guidance, never a crash (NFR-3).
**State-faithful restoration:** the snapshot restores per-monster STATE (scan progress, applied
boosts), not just position — a restored monster resumes exactly where it was (UJ-2).

**Insufficient-credits / Shop trigger:** services never open the Shop or call Billing/UI. When
a spend can't be afforded, `CreditService`/`EncounterService` raise `OnInsufficientCredits`;
**App/UI** decides to navigate to Shop (keeps `Billing → Economy` strictly one-way).

**Safety-warning entry rule:** "AR entry" that triggers the mandatory FR-3 warning means
**cold-entry into AR mode only**. Resuming AR after a Shop round-trip (UJ-2) does **NOT**
re-fire the full warning — otherwise the warning and the re-anchoring beat stack into two
interruptions for one monster. First entry (and first per session) gets a deliberate full read;
later cold-entries show the warning as a fast on-brand card (mandatory presence, minimal dwell).

**Data boundaries:** static monster data lives only in `Monsters` ScriptableObjects; dynamic
player state lives only in `SaveModel` via `IProgressStore`. **Economy numbers (costs, XP curve,
charge thresholds) are data-driven** in `EconomyConfig` (ScriptableObject), tunable without a
rebuild — the PRD's original economy table is superseded and treated as provisional pending
balancing (OQ-9). Charge consumption is **atomic**: mutate-in-memory → persist → rollback on
failure (tested by forcing `IProgressStore.Save` to throw and asserting the charge is intact).

**Billing boundary:** all real-money flow stays in `Billing`; the rest of the app calls
`BillingService.PurchaseAsync(packId)` and listens for `OnPurchaseCompleted`. `PurchaseReconciler`
grants credits exactly once (keyed by Play order id). **Known MVP exposure:** purchases are
client-trusted (no server verification) — documented, Phase-2 trigger is server-side verification.

### Requirements → Structure Mapping

- FR-1/FR-2 → App/UI/Onboarding + CreditService + ProgressionService + SaveModel.
- FR-3/FR-5 → AR/ArSafetyGate (cold-entry rule), AR/CameraPermissionFlow.
- FR-4 → AR/PlaneAnchorService, AR/MonsterSpawner, AR/IArAnchorProvider.
- FR-6–FR-10 → Encounter/* + Economy (Credits for Lure/Slay; charges for extras via Progression).
- FR-11/FR-12 → Monsters/Codex/CodexService + Persistence/*.
- FR-13/FR-14 → Billing/* + UI/Shop.
- FR-15–FR-17 (The Pit) → Pit/ folder stub (no asmdef), post-MVP.
- NFR-1 → MonsterSpawner pooling + URP; NFR-3 → GameLog + typed results + AnchorRestoreResult;
  NFR-4 → PurchaseReconciler exactly-once.

### Integration Points & Data Flow

- **Internal:** services raise C# events; UI subscribes in OnEnable / **unsubscribes in
  OnDisable (symmetric — an acceptance criterion, guards UJ-2 against double-fire/leaks)**.
  App flow driven by `AppStateMachine`; encounter flow by `EncounterStateMachine`.
- **External:** ARCore (via AR Foundation) and Google Play Billing (via Unity IAP). No backend
  in MVP. **OQ-4 ad-reward hook shape is reserved now** (`App/AdHook` stub) even though ads are
  not built — so a Phase-2 ad grant has a defined target (Credits/XP/charge) and doesn't
  retro-break the economy.
- **Data flow (spend):** UI → EncounterService → CreditService.TrySpend (validate→deduct→persist)
  → perform → raise event → UI. **Purchase:** UI → BillingService → Unity IAP → PurchaseReconciler
  (pending→ack→credit once→persist) → CreditService event → UI.
- **Shop round-trip:** AppStateMachine snapshots the active Encounter (AnchorTokens + state)
  before Shop; rehydrates on return; Safety Warning does NOT re-fire.

### Development / Build / Deploy

- **Dev:** Unity 2022.3 LTS via Unity Hub; Library/ + IDE project files regenerate locally (gitignored).
- **Build:** Android App Bundle (.aab), IL2CPP, ARM64, AR Required; signing via gitignored
  keystore + keystore.properties.
- **Deploy:** Google Play tracks (internal → closed → soft launch). No server deploy in MVP.
- **Build order (implementation sequence):** Persistence → Economy (incl. Progression) →
  Monsters/Codex → Encounter (faked AR via IArAnchorProvider) → AR adapter → Billing → App → UI.
  Pit stays a folder stub.

### Open Product/Architecture Flags — Pinned via Second-Order Thinking

These remain product/tuning items, but the architecture now commits to a *shape/instrument* for
each (decided via Second-Order Thinking elicitation) so they no longer threaten the build.

- **OQ-4 (ads) — ad-reward SHAPE pinned (build still deferred):** if rewarded ads ship, an
  ad grants a **CHARGE — never Credits, never raw XP** — behind a **daily cap**. Rationale
  (2nd-order): granting Credits devalues the packs and undercuts SM-3; granting XP indirectly
  short-circuits the "earned, not bought" charge progression; granting a charge keeps ads as an
  *engagement* lever fully decoupled from the *revenue* (Credits) lever.
  - **Which charge (refined):** an ad grants the **weakest / a randomized low-tier charge**, NOT
    a player-chosen one — otherwise an on-demand Strong Capture charge defeats the earned-
    progression curve. The cap is **ads-per-day** (a fixed-value grant), so "ads/day" == "charges
    /day".
  - **Seam:** `App/AdHook.GrantReward()` calls `ProgressionService.AddCharge(...)`, never
    `CreditService`. Direction is App→Economy (downstream→upstream); legal, no cycle.
  - **Cap state:** `SaveModel.AdReward { int grantsToday; long dayBucket; }` — store the
    **`IClock`-derived day-bucket integer, never a `DateTime`**. `EconomyConfig.adDailyCap` holds
    the number. **AC-AD-4 (load-bearing test): `GrantReward()` must never touch Credits or XP**
    (spy asserts only `AddCharge` fired) — the OQ-4 constraint encoded as a test, not a comment.
  Building ads stays a Phase-2 / PM decision.
- **OQ-9 (economy balancing) — TARGET + INSTRUMENT pinned (values still TBD):** the PRD economy
  table is superseded/provisional; all numbers live in `EconomyConfig` (tunable without rebuild).
  - **Primary target = events, not calendar:** a median *engaged* player completes the core loop
    at least once (a successful Capture/Slay) **before** their first zero-credit moment — i.e.
    **loop-completions-before-zero ≥ 1**. The **D3–D7 window** is kept as a *guardrail*, not the
    primary unit (calendar days conflate a D3 conversion moment with a D3 churn moment).
  - **Cohort defined up front:** "median engaged" = players with ≥ N sessions (N TBD) — locked
    *before* tuning so the instrument doesn't lie (churned installs must not drag the median).
  - **Post-zero instrument (the gap John flagged):** also record the **session AFTER first
    zero-credit** — did the player open the Shop, bounce, or churn? Without it we can hit the
    D3–D7 target and still miss SM-3 blind.
  - **Telemetry is a LOCAL DIAGNOSTIC, not analytics:** data never leaves the device pre-backend;
    value is the internal/tester balancing pass. Seam `ITelemetrySink.Count(evt, amount)` (Core);
    `LocalTelemetrySink` impl in App. **Storage is a SEPARATE `telemetry.json` via
    `ITelemetryStore` (capped ring), NOT `SaveModel`** — per-action/per-session counters are
    unbounded and would bloat the save + migration surface. **Exception:** the bounded,
    write-once `firstZeroCreditDay` scalar MAY live in `SaveModel`. `EconomyConfig` holds tunables
    (`adDailyCap`, `telemetryRetentionDays`), **never the counters**. Sink interface stays
    swappable so Phase-2 Firebase replaces the impl without moving call sites.
- **NFR-5 (cold-start) — STAGED-LOADING BUDGET pinned:** the real budget is **launch → first
  successful plane anchor** (a Monster convincingly placed), not launch → camera. Bootstrap wires
  only essential services synchronously; **first-Monster asset preload + AR session WARMUP happen
  asynchronously during Onboarding/Home**, so by the time the player taps "Enter AR Hunt" the
  session and ≥1 Monster are warm. Defer only off-path loads (Codex art, Shop catalog, Pit stub,
  non-starting Monsters).
  - **Warmup ownership rule (critical):** Onboarding/Home do **NOT** warm the session themselves —
    they call **`ArSessionService.PrewarmAsync()`**. `ArSessionService` remains the **sole owner**
    of the AR lifecycle (cold→prewarming→ready→running→paused), so backgrounding / permission-
    racing / double-start cases are all handled in one place. Trigger lives outside the owner;
    lifecycle stays inside it.
  - **Gate + measure both ways:** prewarm costs battery/GPU before the player commits to AR —
    gate it to when the AR-entry affordance is visible/likely, and `ArSessionService` can cheaply
    tear back to cold. Measure NFR-5 with prewarm **on AND off**; if it shaves little, drop it.
  - **CI vs device (testability):** millisecond budgets can't run in CI (no `ARSession` in
    EditMode). **CI asserts the staging CONTRACT, not duration** — `Bootstrap.LoadPhase
    {EssentialSync, WarmupAsync, Ready}`; **AC-CS-2: AR warmup + preload are on the async path and
    NOT awaited before Home is interactive** (the real regression guard). The launch→first-anchor
    **millisecond budget is a device / Firebase Test Lab release-gate checklist item** against
    telemetry percentiles, not a unit test.
  - This protects NFR-5 **without** trading away NFR-1 (frame rate) or SM-6 (plane-anchor success).
- **OQ-3 (local-only):** resolved for MVP; client-trusted purchases are a named fraud exposure
  with a Phase-2 server-verification trigger.

**UX framing for these moments (carry into UX/UI work — every system moment "wears a costume"):**
- **Ad reward = "consult the Veil"** — never lead with "ad." Reward is pre-revealed (charge icon
  glowing/dimmed), daily cap framed as lore ("the Veil answers thrice a day"), and the post-ad
  payoff uses the **same SFX/animation as a real hunt reward** so it reads as earned, not endured.
- **Zero-credit = felt descent, not a wall** — counter pulses/cools from cyan toward dim teal
  near empty; at zero the world does **not** lock (player still enters AR, sees Monsters, aims —
  only Lure is dimmed with its real cost shown). Shop is offered as the *answer to a question the
  player is already asking*, with rewarded-ads as the dignified free exit so the Shop is a choice.
- **Cold-start warmup = ritual, never a progress bar** — AR prewarm hides under an interaction
  (choosing the first Lure, "raise your phone to part the Veil" — the tilt primes plane
  detection); Home re-entry hides behind a ~1s veil-parting wipe. Slow-device fallback is *more
  ritual* ("the Veil is thick here, hold steady…"), never a percentage/spinner in AR.

## Architecture Validation Results

### Coherence Validation ✅
- **Decision compatibility:** Unity 2022.3 LTS, URP, AR Foundation 5, ARCore 5, Unity IAP 5
  are a verified mutually-compatible set. No contradictory decisions. The economy revision
  (extras → XP charges; 4 paid actions) is a documented intentional divergence from the PRD table.
- **Pattern consistency:** C#/Unity naming, per-area asmdefs, event-driven UI, and the
  service-locator-at-composition-root rule are coherent and mutually reinforcing.
- **Structure alignment:** the acyclic dependency graph is enforced at compile time (asmdefs)
  AND by an Architecture.Tests assertion; shared contracts sit below AR so no upward edges exist.

### Requirements Coverage Validation ✅
- **Functional:** FR-1–FR-14 all have explicit structural homes (see Requirements→Structure
  Mapping). FR-15–FR-17 (The Pit) are intentionally deferred (folder stub, no asmdef).
- **Non-functional:** NFR-1 (URP + pooling + spawn caps), NFR-2 (event-driven, sub-second spend
  ack), NFR-3 (typed results + AnchorRestoreResult + GameLog), NFR-4 (PurchaseReconciler
  exactly-once), NFR-6 (local-first) all addressed. NFR-5 (cold-start) — see gap.

### Implementation Readiness Validation ✅
- **Decision completeness:** critical decisions documented with verified versions.
- **Structure completeness:** full Assets/ tree, boundaries, and build order specified.
- **Pattern completeness:** naming, structure, format, communication, and process patterns
  defined with enforcement (tests for acyclicity, Count==67, charge atomicity, save tamper).

### Edge Cases & Failure Handling

> Surfaced via Cascading Failure Simulation (#63) + Boundary & Edge Case Sweep (#69).
> These become acceptance criteria / test cases during implementation.

**Failure cascades (must-handle):**
- **One transactional write per player action.** A spend + its action-resolution must commit
  together. Rule: a single player action (Lure/Slay/Capture/extra) mutates the in-memory
  `SaveModel` (balance/charge delta **and** encounter/codex delta) and is persisted in **one
  atomic write**; on persist failure, roll the whole in-memory mutation back. Never deduct
  credits and resolve the action in two separate persists (prevents free-Slay / resurrected-
  monster inconsistencies).
- **Mid-session AR loss.** `ArSessionService` raises `OnArSessionInterrupted`; the
  `EncounterStateMachine` gains a **`Suspended`** state that pauses the encounter and shows
  guidance (not a crash, NFR-3). On recovery, attempt `TryRestoreAnchor` per the rehydrate rules.
- **Services-not-ready race.** A single **Bootstrap entry scene** wires `GameServices` first,
  then loads Home. `GameServices.Get<T>()` throws `ServicesNotReadyException` (clear) rather
  than returning null if called before wiring.

**Boundary rules (must-specify):**
- **Exact-balance spend** (balance == cost → 0) succeeds; assert `balance >= cost`, with a test.
- **Charge at zero:** using an extra with 0 charges is blocked with an "earn via XP" hint;
  never goes negative.
- **Codex idempotency:** re-capturing an already-owned Monster does **not** increment "X/67";
  only first discovery counts. Reaching **67/67** raises a `OnCodexCompleted` acknowledgment hook.
- **Mid-encounter level-up:** a charge granted by a level-up **mid-encounter** becomes usable
  immediately in that same encounter (confirm against pacing intent).
- **Multi-Lure partial fit:** if two Monsters can't both be placed on available planes, define
  behavior explicitly (spawn what fits + queue/relocate the second; do not silently drop or
  double-charge). `[FLAG: confirm desired behavior.]`
- **Corrupt/tampered save on load:** throw (never silently wipe), then present a player-facing
  recovery choice (retry / start fresh) — recovery path is explicit, not a dead end.

### Gap Analysis Results
- **Critical:** none. (Rare Lure / FR-13 — RESOLVED above as a Guaranteed-Rare Lure with a
  minimum ordered-rarity contract.)
- **Important (now pinned, values/build TBD):** (a) OQ-9 economy values provisional — but
  EconomyConfig + a D3–D7 first-zero-credit target + local telemetry instrument are defined.
  (b) OQ-4 ad-reward **shape decided** (grant a charge/Basic Lure, never Credits/XP, daily cap);
  build deferred. (c) the D3–D7 target IS the SM-3/SM-C1 balancing acceptance criterion.
- **Minor:** NFR-5 now has a **staged-loading budget** (launch → first plane anchor; warm AR +
  first Monster during Onboarding/Home). Crash/analytics SDK choice still open (weigh vs OQ-4
  data-safety footprint).

### Resolved — Rare Lure = Guaranteed-Rare Lure (FR-13) ✅
**Decision:** the **Veil Pack ($9.99)** bundles a **Guaranteed-Rare Lure** — a distinct one-shot
item that forces a spawn of a **Rare-tier or better** Monster (stronger than Premium Lure's
"higher chance"). This gives the top pack a real identity.

**Minimum rarity contract (architecture commits to this; full model is OQ-2 balancing):**
- `Rarity` is an **ordered enum** in `Monsters` (e.g. `Common < Uncommon < Rare < Legendary`
  — exact set TBD via OQ-2). A named **`Rare` threshold** exists.
- `MonsterDefinition.Rarity` is set per Monster.
- A `GuaranteedRareLure` resolves by filtering the spawn pool to `Rarity >= Rarity.Rare` and
  picking from that subset; if the MVP roster (3–5 Monsters) has none at/above Rare, the
  populated content set MUST include at least one Rare+ Monster for the Veil Pack to be valid
  (content acceptance criterion).

**Homes:**
- `Rarity` enum + `MonsterDefinition.Rarity` → `Monsters`.
- `GuaranteedRareLure` is a grantable one-shot item: granted by `PurchaseReconciler` alongside
  the pack's Credits, stored in `SaveModel` (e.g. a small one-shot-item inventory), and consumed
  through the existing Lure path in `Encounter/LureSystem` with the `Rarity >= Rare` filter.
- Premium Lure keeps its probabilistic higher-rare-chance; only the Guaranteed-Rare Lure forces
  the tier floor.

### Validation Issues Addressed
The step-6 review resolved the highest-risk structural issues (AnchorToken home, ProgressionService
home, locator confinement, IClock, AnchorRestoreResult, acyclicity test). The elicitation sweep
above added transactional-write, AR-suspend, bootstrap-ordering, and the boundary rules. The Rare
Lure product call is now RESOLVED (Guaranteed-Rare Lure + ordered-rarity contract). No open items
block implementation; remaining flags (OQ-4 ad shape, OQ-9 balancing, NFR-5 budget) are tuning/
product items resolvable before launch.

### Architecture Completeness Checklist

**Requirements Analysis**
- [x] Project context thoroughly analyzed
- [x] Scale and complexity assessed
- [x] Technical constraints identified
- [x] Cross-cutting concerns mapped

**Architectural Decisions**
- [x] Critical decisions documented with versions
- [x] Technology stack fully specified
- [x] Integration patterns defined
- [x] Performance considerations addressed

**Implementation Patterns**
- [x] Naming conventions established
- [x] Structure patterns defined
- [x] Communication patterns specified
- [x] Process patterns documented

**Project Structure**
- [x] Complete directory structure defined
- [x] Component boundaries established
- [x] Integration points mapped
- [x] Requirements to structure mapping complete

### Architecture Readiness Assessment
**Overall Status:** READY FOR IMPLEMENTATION
**Confidence Level:** High — all 16 checklist items confirmed, no critical gaps, and the one
open product decision (Rare Lure / FR-13) is now resolved. Remaining flags (OQ-4 ad-hook shape,
OQ-9 economy balancing, NFR-5 cold-start budget) are tuning/product items that do not block the
build and are resolvable before launch.

**Key Strengths:**
- Compile-enforced acyclic boundaries make AI-agent implementation safe by construction.
- The hardest correctness problems (UJ-2 Shop round-trip, NFR-4 exactly-once billing,
  transactional player actions) have explicit, testable seams.
- Clean future-backend seam (IProgressStore / IPurchaseVerifier) for Phase-2 Firebase.
- Economy is data-driven, so balancing doesn't require code changes.

**Areas for Future Enhancement:**
- Phase-2 Firebase (cloud sync + server-side purchase verification).
- The Pit subsystem (reuses AR + Encounter patterns).
- OQ-4 ad-reward implementation behind the reserved App/AdHook seam.
- Economy balancing pass + NFR-5 cold-start budget.

### Implementation Handoff
**AI Agent Guidelines:**
- Follow all architectural decisions exactly; respect the one-way dependency graph (compile-
  and test-enforced).
- Keep static data in ScriptableObjects, dynamic state in SaveModel via IProgressStore only.
- Confine GameServices to the App/Bootstrap composition root; inject into pure-logic areas.
- One atomic save write per player action; never split a spend and its resolution.
- Treat the economy revision (this doc) as authoritative over the PRD economy table.

**First Implementation Priority:**
Project init story — Unity Hub "3D (URP)" + Unity 2022.3 LTS, pin AR Foundation 5 / ARCore 5 /
Unity IAP 5 in manifest.json, import ARCore Extensions (arf5), configure Android / AR Required /
ARM64 / IL2CPP. Then build order: Persistence → Economy → Monsters/Codex → Encounter → AR →
Billing → App → UI.
