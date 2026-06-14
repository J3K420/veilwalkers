---
baseline_commit: "398e02e20a1144debc93c2110a7513e45ae77993"
---

# Story 2.5: View a Monster's detail entry

Status: done

## Story

As a player,
I want to open a discovered Monster and read its details,
So that I can admire the art and lore I've collected.

## Acceptance Criteria

**AC-1 (discovered slot → detail).**
**Given** a discovered Monster slot
**When** the player taps it
**Then** a detail view shows the Monster's art, stats, rarity-tier badge (color-coded to its dread-scale tier token), wry-eerie lore, and first-discovered date.
[Source: docs/epics.md#Story-2.5]

**AC-2 (undiscovered slot → reveal preserved).**
**Given** an undiscovered slot (`???` or `?`)
**When** the player taps it
**Then** no detail entry is shown (only "Not yet discovered." copy), preserving the reveal.
[Source: docs/epics.md#Story-2.5]

## Tasks / Subtasks

- [x] **Task 1 — Persist a first-discovered date on the Codex entry (data layer).** (AC-1)
  - [x] Add a nullable ISO-8601 `string Discovered { get; set; }` to `CodexEntryData` (Persistence). ISO `yyyy-MM-dd`, the SAME date-key shape as `SaveModel.DailyClaim` (string, never a serialized `DateTime` — AR-18). Document: set ONCE at first discovery, never overwritten; `null` means "discovered before this field existed" (old saves) — a legitimate state the detail view renders gracefully.
  - [x] Confirm `CoerceNullCollections()` needs NO change (a null scalar string is already the valid "unknown date" state; only collections are coerced). Add a one-line doc note saying so.
  - [x] **Schema/migration call:** the new field is purely additive and back-compatible — a v2 save with no `discovered` key deserializes to `null` (= "date unknown"), which the view handles. Decide & record in Dev Notes whether this warrants a `SchemaVersion` bump (lean **NO bump** — additive nullable, no transform needed; `SaveMigrations.MigrateV1ToV2` precedent only bumps when a transform is required). Do NOT write a migration step for a field that defaults correctly.
- [x] **Task 2 — Stamp the date through the single discovery write (`CodexService`).** (AC-1)
  - [x] Inject `IClock` into `CodexService` (ctor: `(SaveService, MonsterDatabase, IClock)`), `readonly`, `ArgumentNullException` null-check — mirrors `DailyRewardService` (the established `IClock` consumer). Add `[assembly: InternalsVisibleTo]` is NOT needed (IClock is public Core).
  - [x] In `RecordDiscoveryAsync`, set `entry.Discovered = _clock.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)` ONLY on the **first-discovery create path** (the `existing == null` branch / `isFirstDiscovery`), BEFORE the `SaveAsync`. A flag-flip on an already-discovered Monster must NOT touch the date (it is the *first*-discovered date). A null-value-repair (crafted/corrupt key present as null) re-creates the entry → it gets a fresh date stamp (acceptable: the prior date was lost with the null value; document this edge).
  - [x] **Rollback fidelity:** the date is written on the same fresh `entry` instance that rollback removes (key-absent case removes the whole entry) — so the existing `RollBack` already reverts it for the create path. Verify no separate date-rollback is needed and note it. For the null-repair branch, the captured `priorEntry == null` restores null — also correct.
  - [x] Update ALL ctor call sites: the `Bootstrap.cs` seam COMMENT (it currently reads `new CodexService(saveService, _monsterDatabase)` at ~line 161 — change to `new CodexService(saveService, _monsterDatabase, clock)`; `clock` is already in scope, registered at Bootstrap.cs:170 — no new field), and EVERY test `new CodexService(...)` (CodexServiceTests.cs:31,42,43,206; CodexServiceRevealTests.cs:44; CodexGridPresenterTests.cs:47). Extend the `CreateInitialized` tuple helpers to construct + thread the `FakeClock` (see "house patterns / Test construction" for the per-assembly `FakeClock` copy).
- [x] **Task 3 — Surface the date + null-safe content on the read view (`CodexEntryView` / `CodexService.GetEntry`).** (AC-1)
  - [x] Add `string Discovered { get; }` to `CodexEntryView`, value-copied in `From(...)` (scalar — already safe), `null` on the `NotDiscovered` sentinel.
- [x] **Task 4 — Build the detail presenter (pure logic, `Veilwalkers.UI`).** (AC-1, AC-2)
  - [x] `CodexDetailPresenter` — ctor-injected `CodexService` ONLY (one collaborator; route everything through it, never touch `MonsterDatabase` directly — the 2.4 "tier rule has one home" precedent). No Unity types except it MAY expose a `Sprite` art reference resolved from the definition (decide: presenter returns the `MonsterDefinition`'s `Art` `Sprite`, or an id for the view to resolve — see Dev Notes decision #2).
  - [x] `CodexDetailViewModel` (readonly struct, data-only): `IsDiscovered`, `Id`, `DisplayName`, `Rarity? Tier`, `CaptureDifficulty`, `SlayDifficulty`, `Lore`, `Art` (or art id), `Discovered` (date string, may be null), and a `NotDiscoveredCopy` constant ("Not yet discovered.").
  - [x] `BuildDetail(string id)`: discovered → populate from `CodexService.GetEntry` + the `MonsterDefinition` (via a new `CodexService.GetDefinitionView`/`GetTier`+name accessor — keep the catalog read in CodexService); undiscovered (`!entry.IsDiscovered`) → return the `NotDiscovered` view-model (only the copy, NO content). **AC-2 reveal-preservation: the undiscovered view-model must carry NO name/art/lore/stats** (a leak would defeat the silhouette).
  - [x] **Null-safety (settles the 2.1/2.2 deferrals):** `DisplayName`/`Lore` null → render a safe placeholder (e.g. `DisplayName ?? "(unnamed)"`, `Lore ?? ""`), `Art` null → null `Art` (view falls back, Epic 6). A discovered-but-unauthored id (no `MonsterDefinition`) → discovered view-model with null name/art/lore/tier but a valid `Discovered` date + id (mirror the grid's "discovered unauthored id" rule). NEVER throw on null content.
- [x] **Task 5 — Thin detail view (`CodexDetailView : MonoBehaviour`).** (AC-1, AC-2)
  - [x] Logic-free binder: resolves `CodexService` via `GameServices.Get` with graceful degrade (catch `ServicesNotReadyException`/`KeyNotFoundException` → inert + `GameLog.Warn`, never throw — the 2.4 view precedent). Exposes an `Open(string id)` that calls `_presenter.BuildDetail(id)` and binds fields; shows the detail panel for discovered, the "Not yet discovered." copy for undiscovered. No business logic in the view.
  - [x] **Wiring to the grid is Epic 6 (Story 6.3 places surfaces).** Do NOT build a scene/prefab or the tap-routing from `CodexGridView` → `CodexDetailView`; expose the seam (the grid already carries `CodexSlot.State` + `Id`; the view's `Open(id)` is the entry point) and note it. Verify by inspection (no PlayMode test — house convention).
- [x] **Task 6 — EditMode tests (`Veilwalkers.UI.Tests` + `Monsters.Tests`).** (AC-1, AC-2)
  - [x] `CodexDetailPresenterTests` (UI.Tests): discovered → view-model carries name/stats/tier/lore/date; undiscovered (`?` blank AND `???` silhouette — both are "not discovered") → ONLY the copy, NO content (AC-2 anti-leak); discovered-unauthored id → discovered + null content + valid date; null `DisplayName`/`Lore`/`Art` → safe placeholders, no throw (the deferral-settling tests).
  - [x] `CodexService` date tests (Monsters.Tests): first discovery stamps `Discovered` from the fake clock; a later flag-flip (Capture then Slay) does NOT change the date; rollback on persist failure leaves NO entry (and thus no date); reload round-trips the date (the fake store deep-clones — copy the `Discovered` field in `Clone()`).
  - [x] Drive the fake clock to a FIXED instant; assert the exact `yyyy-MM-dd` string (mutation-testable — change the stamp format/source → red).

## Dev Notes

### SCOPE — read this first
This is the **second UI story** (2.4 was the first). Same locked altitude ([[ui-presenter-pattern]]): build the **pure-logic presenter + EditMode tests + a thin logic-free view**; DEFER pixels (real art draw, tier-color tokens, the rarity-badge component, layout/typography, the open/close animation) to **Epic 6**. Unlike 2.4, this story DOES touch the **data layer** — the AC's "first-discovered date" has no home today (`CodexEntryData` stores only `Scanned`/`Captured`/`Slain`/`VariantFlags`), so this story adds the persisted date + threads `IClock` through the discovery write. That is the one genuinely new seam; everything else mirrors 2.4.

- **BUILD now:** the persisted `CodexEntryData.Discovered` date (+ `IClock` into `CodexService` + the date on `CodexEntryView`/`GetEntry`); a pure-logic `CodexDetailPresenter` → `CodexDetailViewModel` (discovered content vs the "Not yet discovered." copy, null-safe); a thin `CodexDetailView : MonoBehaviour` (graceful-degrade, `Open(id)` seam); EditMode tests in `UI.Tests` + `Monsters.Tests`.
- **DEFER to Epic 6** (with `deferred-work.md` notes): the rarity-tier **badge color** (UX-DR2 tokens — the view-model carries `Tier`, Epic 6 maps it to a color), real art draw, the detail panel layout/typography/chunky styling (UX-DR5), the open/close transition, and the tap-routing from the grid into the detail surface (Story 6.3 places surfaces & navigation).
- **Do NOT** build a scene, a prefab, design tokens/colors, or the grid→detail tap wiring this story.

### The design decisions this story locks (call them out for the CR reviewer)
1. **First-discovered date = persisted ISO `yyyy-MM-dd` string, stamped once via `IClock` on first discovery.** Not a serialized `DateTime` (AR-18: all persisted time is ISO string via `IClock`, the `DailyClaim` precedent). Day-granular (not an instant) — matches the AC's "date" and the daily-reward house pattern; if Epic 6 later wants a richer timestamp, widen the format then. Stamped ONLY on the first-discovery create path; a later flag-flip never rewrites it (it is the *first*-discovered date). `null` = "discovered before this field existed" (pre-2.5 saves) — a legitimate state the view renders as an empty/"—" date, NOT a crash.
2. **Presenter returns the art `Sprite` reference (decide & record).** Two options: (a) the view-model carries the `MonsterDefinition.Art` `Sprite` (presenter reads the catalog via a CodexService accessor) — simplest for the view, but puts a Unity `Sprite` on a "pure logic" type; (b) the view-model carries only the `Id` and the view resolves `Art` from the catalog at bind time (keeps the view-model Unity-free, mirrors how `CodexSlot` deliberately carries NO `Sprite`). **Lean (b)** for consistency with the 2.4 `CodexSlot` decision ("Deliberately carries NO `Sprite`/`Color`… the view resolves those from `MonsterDatabase`/Epic-6 tokens at bind time") — the detail view-model stays Unity-free and headless-pure; the view resolves `Art` (and the tier color) from id+tier. Record the choice + rationale in Completion Notes. (If you pick (a), the `UI.Tests` must run in an assembly that can construct `Sprite`s — they can't headless; another reason (b) is cleaner.)
3. **AC-2 reveal-preservation is a hard anti-leak rule.** The undiscovered view-model carries the copy ONLY — no name, art, lore, stats, or tier. A leak (e.g. returning the tier for an undiscovered authored slot) would let a determined player read off unrevealed content; pin it with a test that asserts every content field is null/default on the undiscovered view-model. This applies to BOTH `?` blank AND `???` silhouette slots — both are "not discovered" (the entry has no Codex key); the grid's reveal STATE differs but the detail rule is the single "discovered?" check.

### What this story IS and IS NOT
- **IS:** (a) a persisted `CodexEntryData.Discovered` ISO date + `IClock` injected into `CodexService` + the date stamped on the first-discovery path of `RecordDiscoveryAsync` (rollback-safe) + the date surfaced on `CodexEntryView`/`GetEntry`; (b) a CodexService catalog accessor the detail presenter needs (name/stats/lore/tier for an id — keep the catalog read in CodexService, the "one home" rule); (c) `CodexDetailPresenter` (pure logic, `Veilwalkers.UI`) → `CodexDetailViewModel` struct, null-safe, AC-2-anti-leak; (d) a thin `CodexDetailView : MonoBehaviour` with `Open(id)` + graceful degrade; (e) tests in `UI.Tests` + `Monsters.Tests`.
- **IS NOT:** the tier-color/badge rendering, real art draw, panel layout/animation (Epic 6 — UX-DR2/DR5); a scene/prefab or the grid→detail tap wiring (Epic 6 Story 6.3); registering CodexService in Bootstrap (STILL blocked by the unauthored 2.2 `MonsterDatabase.asset` — the view degrades gracefully exactly as 2.4's does); the Capture/Slay actions that PRODUCE discoveries (Epic 4 — this story consumes/extends `RecordDiscoveryAsync`); a `SchemaVersion` bump or migration step (the new field is additive-nullable — see Task 1).

### Architecture constraints (hard rules)
- **UI is the top tier; services never depend on UI (AR-5).** `Veilwalkers.UI` already references every lower tier — no asmdef edge change for the presenter/view. The catalog/date read stays ON `CodexService` (Monsters), consumed by UI. [architecture.md AR-5; Veilwalkers.UI.asmdef]
- **Pure-logic classes are ctor-injected, never read the locator; only MonoBehaviours read `GameServices` (AR-4).** `CodexDetailPresenter` takes `CodexService` via ctor; only `CodexDetailView` calls `GameServices.Get`. [architecture.md AR-4]
- **All persisted time is ISO-8601 via `IClock`, never `DateTime.Now`/serialized `DateTime` (AR-18).** The `Discovered` field is a `yyyy-MM-dd` string; `CodexService` reads `_clock.UtcNow` (the `DailyRewardService` precedent). [IClock.cs; DailyRewardService.cs:177-187; SaveModel.DailyClaim]
- **One atomic write per action; rollback-safe (AR-8).** The date is written on the same `entry` the existing `RollBack` already reverts on the create path — no new rollback logic, but VERIFY and pin it with a test (a persist-fail discovery must leave no entry and no date). [CodexService.cs:185-301,331-349]
- **`GameServices.Get<T>()` throws before wiring (AR-4); CodexService is NOT wired** (the 2.3/2.4 seam — no `MonsterDatabase.asset`). The detail view MUST degrade gracefully (catch `ServicesNotReadyException`/`KeyNotFoundException` → inert + `GameLog.Warn`). [Bootstrap.cs:150-163]
- **No `Debug.Log` — use `GameLog` (AR-19).** **`monNN` is the key everywhere; validate via `MonsterDatabase.IsValidMonsterId`** (the presenter takes an id; an invalid id from a future caller → return the NotDiscovered view-model OR throw `ArgumentException` like `RecordDiscoveryAsync` — decide & document; lean: the presenter is a read, so a bad id → NotDiscovered view-model + `GameLog.Warn`, not a throw, since a UI read should not crash the panel).
- **Defensive reads (2.2/2.3/2.4 lesson):** the presenter hands out a value-type `CodexDetailViewModel`; it reads `CodexService.GetEntry` (already defensively-copied) and the catalog through CodexService; it never exposes live `SaveModel.Codex` or a live `MonsterDefinition` mutable field (`MonsterDefinition` is already read-only accessors).

### The house patterns to copy
- **`IClock` injection:** mirror `DailyRewardService(saveService, lock, clock, config)` — `readonly IClock _clock`, null-checked; date string via `_clock.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)`. [DailyRewardService.cs:39,48,177-187]
- **Ctor-injected pure-logic presenter:** `CodexDetailPresenter` mirrors `CodexGridPresenter` — `readonly CodexService`, `ArgumentNullException`, no Unity types. [CodexGridPresenter.cs:24-29]
- **Data-only view-model struct:** `CodexDetailViewModel` mirrors `CodexSlot` — `readonly struct`, value-copied scalars, carries NO live references; carries `Tier` (Rarity?) for Epic 6 to color, NOT a `Color`. [CodexSlot.cs:16-45]
- **Defensive read view:** `CodexEntryView.From` value-copies scalars + deep-copies the variant list; add the `Discovered` scalar the same way. [CodexEntryView.cs:56-73]
- **Graceful-degrade view:** `CodexDetailView` mirrors `CodexGridView` — `GameServices.Get` in a guard, catch both exception types, inert + `GameLog.Warn`, never throw at `Awake`. [CodexGridView.cs]
- **Test construction:** extend the `CreateInitialized` tuple helper (fake store + `SaveService.InitializeAsync` + in-memory `MonsterDatabase` + `CodexService`) to pass a **fake clock**. NOTE (validated): a `FakeClock : IClock` already exists in `Economy.Tests/FakeClock.cs` AND `App.Tests/FakeClock.cs`, but each is `internal sealed` to its OWN test assembly — neither is visible to `Monsters.Tests`/`UI.Tests`. So **copy that `FakeClock` verbatim into BOTH `Monsters.Tests` and `UI.Tests`** (new `FakeClock.cs` per assembly, swap the namespace to `Veilwalkers.Monsters.Tests` / `Veilwalkers.UI.Tests`), exactly as 2.4 authored a per-assembly `FakeUiCodexProgressStore`. Do NOT try to share one across assemblies (would need a new shared test asmdef — heavier than the established copy pattern; the Economy `FakeClock` doc even says "promote… if a later story needs it elsewhere," but the per-assembly copy is the lighter, precedent-matching choice). Construct with a fixed instant, e.g. `new FakeClock(new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc))`. [Economy.Tests/FakeClock.cs:16-28; CodexServiceTests.cs:24-33; CodexServiceRevealTests.cs; CodexGridPresenterTests.cs:47]
- **`.Tests` deep-clone fake:** `FakeUiCodexProgressStore`/`FakeCodexProgressStore` `Clone()` must copy the new `Discovered` field (else the reload round-trip test silently drops it — the [[tautological-test-trap]] shape). Mirror `CoerceNullCollections` and the scalar copies. [FakeCodexProgressStore.cs:90-130]

### Previous story intelligence (2.4 + 2.3 + 2.2 + 2.1)
- **2.4 shipped** `CodexGridPresenter`/`CodexSlot`/`CodexSlotState`/`CodexGridView` in `Veilwalkers.UI`, the `Veilwalkers.UI.Tests` assembly (+ `FakeUiCodexProgressStore`), and `CodexService` reveal members (`HighestDiscoveredRarity`, `IsTierBegun`, `GetTier`, `GetDiscoveredIds`). 2.5 ADDS a sibling `CodexDetailPresenter`/`CodexDetailView` in the same folder + assembly — NO new asmdef. The `CodexSlot` "carries NO Sprite/Color, view resolves at bind time" decision is the precedent for design decision #2 (lean: detail view-model also Unity-free). [CodexGridPresenter.cs; CodexSlot.cs; deferred-work.md]
- **2.3 shipped `CodexService`** incl. `GetEntry(id) → CodexEntryView` (already defensively-copied) — 2.5 EXTENDS it with the `Discovered` date and a catalog accessor for the detail content. `CodexEntryView` already documents it is "handed to … the 2.4/2.5 detail views" — this is that consumer. [CodexService.cs:83-92; CodexEntryView.cs:7-12]
- **DEFERRALS this story SETTLES** (from `deferred-work.md`, owners named "Story 2.3 UI null-safety" but 2.3 was data-only — the detail view is the real first render consumer):
  - `MonsterDefinition.DisplayName` accepts null with no guard → detail render must null-safe it (Task 4 null-safety). **Settle.**
  - `Lore` accepts null with no guard → same. **Settle.**
  - `Art` Sprite can be null on an authored asset → view falls back, view-model null-safe. **Settle (render side).** [deferred-work.md "Deferred from: code review of 2-1…"]
- **2.2 `MonsterDatabase`** `.asset` STILL unauthored (the 2.2 `[~]` deferral) → CodexService STILL unregistered → the detail view degrades gracefully, exactly as the grid view does. Not this story's job to author the asset. [MonsterDatabase.cs; Bootstrap.cs:150-163]
- **2.1 `MonsterDefinition`** stats (`CaptureDifficulty`/`SlayDifficulty`) are PROVISIONAL (OQ-9) — the detail view DISPLAYS them as-is (no validation/balancing here; the negative/overflow-bound deferrals stay owned by Epic 4/OQ-9, NOT settled here — this story only renders). [MonsterDefinition.cs:42-71; deferred-work.md]
- **Anti-tautology bar (2.1–2.4):** tests exercise the production presenter/service; the date stamp asserts an exact `yyyy-MM-dd` against a fixed fake clock (mutation-testable); the AC-2 anti-leak test asserts content fields are null/default on the undiscovered view-model (break the rule → red). The fake store deep-clones incl. the new field. [[tautological-test-trap]]

### Edge cases & boundary rules (must-handle — these become test cases)
- **Discovered, date present:** view-model carries the stamped `yyyy-MM-dd`.
- **Discovered, date null (pre-2.5 save):** view-model `IsDiscovered=true`, `Discovered=null` → view renders "—"/empty date, never crashes. (A real back-compat state — old saves predate the field.)
- **Undiscovered `?` blank AND `???` silhouette:** BOTH → NotDiscovered view-model, copy only, NO content (AC-2 anti-leak). One `entry.IsDiscovered` check covers both grid states.
- **Discovered but unauthored id (no `MonsterDefinition`):** discovered view-model with valid id + date but null name/art/lore/tier (mirror the grid's discovered-unauthored rule); never throw.
- **Null `DisplayName`/`Lore`/`Art` on an authored definition:** safe placeholders, no throw (settles the 2.1/2.2 deferrals).
- **First-discovery stamps date; later flag-flip does NOT rewrite it:** Capture (stamps date D) then Slay (date stays D). Pinned by a test.
- **Persist-fail on first discovery:** rollback removes the entry → no leaked date, count unchanged. Pinned (reuse the `FailNextSave` affordance).
- **Invalid id to the presenter:** read-path → NotDiscovered view-model + `GameLog.Warn` (do NOT throw — a UI read must not crash the panel; differs from `RecordDiscoveryAsync` which throws because a bad id there is an Epic-4 caller bug). Document the asymmetry.

### Project Structure Notes
- NEW (production, `Veilwalkers.UI`): `Assets/Veilwalkers/UI/Codex/CodexDetailPresenter.cs`, `CodexDetailViewModel.cs`, `CodexDetailView.cs` (+ `.meta`s). No new asmdef (UI + UI.Tests already exist).
- UPDATE (`Veilwalkers.Persistence`): `CodexEntryData.cs` — add `Discovered` string + doc.
- UPDATE (`Veilwalkers.Monsters`): `CodexService.cs` — inject `IClock`, stamp the date on first discovery, add the detail-content catalog accessor; `CodexEntryView.cs` — add `Discovered`.
- UPDATE: `Bootstrap.cs` — pass the existing `clock` into the (still-seam-only) `new CodexService(...)` comment; no live registration.
- UPDATE (tests): `CodexServiceTests.cs` / `CodexServiceRevealTests.cs` / `CodexGridPresenterTests.cs` — pass a fake clock to `CodexService`; `FakeUiCodexProgressStore.cs` (+ `FakeCodexProgressStore.cs`) — copy the new `Discovered` field in `Clone()`.
- NEW (tests): `Assets/Tests/EditMode/UI.Tests/CodexDetailPresenterTests.cs` (+ `.meta`); a `FakeClock`/reuse (grep first); date tests added to `CodexServiceTests.cs` (or a new `CodexServiceDateTests.cs` + `.meta`).
- NO asmdef allow-matrix edit (UI row complete; UI.Tests `.Tests`-excluded). **Possible `SchemaVersion` decision** (Task 1 — lean NO bump). NO scene, NO prefab, NO `.asset`. Bootstrap registration OPTIONAL/seam-only.

### References
- [Source: docs/epics.md#Story-2.5] — the two ACs verbatim (discovered → art/stats/badge/lore/first-discovered date; undiscovered → "Not yet discovered." only, reveal preserved).
- [Source: docs/epics.md#Epic-2] — "per-Monster detail views (art, stats, rarity badge, wry lore)", FR-11.
- [Source: docs/epics.md#UX-DR2 (95), UX-DR5 (98), UX-DR14 (105)] — tier-color tokens (Epic 6), Codex-slot/detail component (Epic 6 Story 6.2), diegetic "Not yet discovered" voice (UX-DR14 names this exact locked-slot copy).
- [Source: docs/epics.md#AR-4, AR-5, AR-6, AR-8, AR-18, AR-19] — ctor-injection vs locator, acyclic UI-on-top, event-driven, one-atomic-write/rollback, ISO-via-IClock, GameLog.
- [Source: Assets/Veilwalkers/Persistence/CodexEntryData.cs] — the entry to extend (add `Discovered`); `CoerceNullCollections` (no change).
- [Source: Assets/Veilwalkers/Monsters/Codex/CodexService.cs:60-92,185-349] — inject `IClock`, stamp on first-discovery create path, rollback fidelity, `GetEntry`; add the catalog accessor.
- [Source: Assets/Veilwalkers/Monsters/Codex/CodexEntryView.cs:21-83] — add the `Discovered` scalar; the defensive-copy discipline + the "2.4/2.5 detail views" note.
- [Source: Assets/Veilwalkers/Monsters/MonsterDefinition.cs:59-77] — name/rarity/stats/lore/art read accessors (null-unguarded — null-safe at render).
- [Source: Assets/Veilwalkers/Economy/DailyRewardService.cs:39,48,177-187] — the `IClock` injection + `yyyy-MM-dd` ISO date pattern to copy.
- [Source: Assets/Veilwalkers/Core/IClock.cs, SystemClock.cs] — the time seam (UTC, ISO when serialized).
- [Source: Assets/Veilwalkers/Persistence/SaveModel.cs:28-32, SaveMigrations.cs:13-40] — `SchemaVersion`/`CurrentVersion=2`, additive-field back-compat (no migration for a defaulting nullable).
- [Source: Assets/Veilwalkers/UI/Codex/CodexGridPresenter.cs, CodexSlot.cs, CodexGridView.cs] — the 2.4 presenter/view-model/view shapes to mirror; the "view-model carries no Sprite/Color" decision (design decision #2).
- [Source: Assets/Veilwalkers/App/Bootstrap.cs:88-170] — the `clock`/`SystemClock` wiring + the deferred-CodexService registration seam (still open; why the view degrades).
- [Source: Assets/Tests/EditMode/Monsters.Tests/CodexServiceTests.cs:24-33, CodexServiceRevealTests.cs, UI.Tests/CodexGridPresenterTests.cs:47, FakeCodexProgressStore.cs:90-130] — `CreateInitialized` helpers + the deep-clone fake to extend for the date.
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — the 2.1 `DisplayName`/`Lore`/`Art` null-guard deferrals (owner "2.3 UI null-safety" → settled HERE at the render layer).
- [Source: _bmad-output/implementation-artifacts/2-4-browse-the-67-grid-with-hybrid-gradual-reveal.md] — prior story; the UI altitude, the graceful-degrade view, the UI.Tests assembly, the anti-tautology bar.

## Technical Requirements

- C# / Unity 2022.3 LTS. `CodexDetailPresenter` + `CodexDetailViewModel` are plain C# (NO `MonoBehaviour`; lean Unity-free per decision #2 — the view resolves `Art`/color). `CodexDetailView` is the only `MonoBehaviour` + the only locator reader.
- `CodexService` gains an `IClock` ctor dependency (third param) + the date stamp (first-discovery path only, before `SaveAsync`, rollback-reverted) + a catalog accessor for detail content. Reads stay synchronous/no-lock (single-threaded gameplay); the write path's lock is unchanged.
- `CodexEntryData.Discovered` is a nullable ISO `yyyy-MM-dd` string — additive, back-compat, **no `SchemaVersion` bump** unless review finds a reason (record the decision). No new package dependencies.
- **Asmdef changes:** NONE (UI + UI.Tests already exist; `IClock` is public Core; no new InternalsVisibleTo). NO allow-matrix edit.
- Logging via `GameLog` (Core), never `UnityEngine.Debug` (AR-19). All persisted time via `IClock` ISO string (AR-18).

## Testing Requirements

- **Framework:** Unity Test Framework 1.1.33, NUnit, EditMode. Async writes via the sync `[Test]` + `.GetAwaiter().GetResult()` idiom (the house pattern); presenter reads are synchronous. No `[UnityTest]`.
- **Headless command** ([[unity-headless-testing]]):
  ```
  "/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode \
    -projectPath "C:/Users/james/Desktop/Veilwalkers" -runTests -testPlatform EditMode \
    -testResults "<abs>/test-results.xml" -logFile "<abs>/editor-test.log" -nographics
  ```
  Editor fully closed (`Temp/UnityLockfile` absent). **Never trust exit 0** — confirm `test-results.xml` `result="Passed"` AND grep the log for `error CS` (0). Confirm the new `CodexDetailPresenterTests` actually ran (names present in results — the new-test trap). Baseline at post-2.4 HEAD is **192** cases; gate is "all green, 0 failed, 0 compile errors", not a target number. Delete `test-results.xml` + `editor-test.log` before commit.
- **No PlayMode tests** — `CodexDetailView` verified by inspection (graceful degrade, `Open(id)` binds the headless-tested presenter, no logic).
- **Anti-tautology:** the date stamp asserts an exact `yyyy-MM-dd` against a FIXED fake clock (change the format/source → red); the AC-2 anti-leak test asserts every content field is null/default on the undiscovered view-model (break it → red); the first-vs-flag-flip date-immutability and the persist-fail no-leaked-date rules are each mutation-testable; the reload round-trip proves the fake store deep-clones the new field.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (story-automator, ungated run).

### Debug Log References

- Headless EditMode run (Unity 2022.3.62f3, `-batchmode -runTests -testPlatform EditMode -nographics`): **208 total, 208 passed, 0 failed, 0 skipped**, `result="Passed"`, exit 0; `grep "error CS"` → **0**. Baseline at post-2.4 HEAD was 192 → +16 new cases (10 `CodexDetailPresenterTests` + 6 `CodexServiceDateTests`).
- New-test trap checked: `CodexDetailPresenterTests` `runstate="Runnable" testcasecount="10"` 10/10; `CodexServiceDateTests` `testcasecount="6"` 6/6; `Veilwalkers.UI.Tests.dll` grew 8 → 18 cases (genuinely compiled + ran, not silently zero). `AcyclicDependencyTests` still 4/4 (the new `IClock` ctor dep + `GetDefinition` accessor added NO illegal assembly edge — `IClock`/`MonsterDefinition` are tiers UI/Monsters already reference).
- One iteration before green: (1) `CS0102` — the view-model had both a `Discovered` field and a `Discovered(...)` static factory (name collision); renamed the factories `ForDiscovered`/`ForNotDiscovered` (the field is the public-facing name). (2) the persist-fail date test logged the expected `SaveService`/`CodexService` rollback errors; added the two `LogAssert.Expect` lines (the established CodexServiceTests rollback pattern).
- Artifacts (`test-results.xml`, `editor-test.log`) deleted before commit (not gitignored). [[unity-headless-testing]]

### Completion Notes List

- **Both ACs delivered by altitude-split** (the locked UI pattern, [[ui-presenter-pattern]]): the LOGIC of AC-1 (discovered → name/tier/stats/lore/first-discovered date) and AC-2 (undiscovered → "Not yet discovered." copy only, reveal preserved) is fully built + headless-tested in `CodexDetailPresenter`; the PIXELS (real art draw, the rarity-badge tier color via UX-DR2 tokens, panel layout/typography/animation, the grid→detail tap routing) defer to Epic 6 — recorded in `deferred-work.md`. The view-model carries per-field content + `Tier` so Epic 6's render is pure binding.
- **The genuinely-new seam: a persisted first-discovered date.** `CodexEntryData` had no timestamp, so added a nullable ISO `yyyy-MM-dd` `Discovered` string (the `DailyClaim` shape — AR-18, never a serialized `DateTime`) and injected `IClock` into `CodexService` (ctor now `(SaveService, MonsterDatabase, IClock)`, mirroring `DailyRewardService`). The date is stamped ONLY on the first-discovery create path in `RecordDiscoveryAsync`, BEFORE `SaveAsync`; a later flag flip (Capture→Slay) never rewrites it (pinned), and an idempotent re-discovery is a no-op. Rollback fidelity: the date rides the same fresh `entry` the existing key-absent `RollBack` removes — no new rollback logic, verified by the persist-fail test (no leaked date, no entry).
- **Design decision #1 (date) — locked as written:** day-granular ISO string via `IClock`; `null` is the legitimate "discovered before this field existed" (pre-2.5 save) state, rendered as "—", never a crash (pinned by the two null-date tests).
- **Design decision #2 (art) — chose (b), the Unity-free view-model.** `CodexDetailViewModel` carries `Id` + `Tier` but NO `Sprite`/`Color`, exactly like 2.4's `CodexSlot`; the view resolves art + the tier-badge color from id/tier at bind time (Epic 6). Keeps the presenter + view-model headless-testable and matches the established "view-model carries no Unity types" precedent.
- **Schema/migration call — NO `SchemaVersion` bump.** The `Discovered` field is purely additive: a v2 save with no `discovered` key deserializes to `null` (= "date unknown"), which every read handles. `SaveMigrations` only chains a step for a TRANSFORM; a defaulting nullable needs none. `CoerceNullCollections()` is unchanged (a null date string is its valid state — documented in `CodexEntryData`).
- **AC-2 anti-leak is a hard rule, pinned three ways:** the undiscovered view-model carries the copy + id ONLY — every content field null/default — asserted for a `???` silhouette slot, a `?` blank slot, AND a reserved unauthored slot (all three are "not discovered" via the single `entry.IsDiscovered` check). A `Tier`/name leak → red.
- **Settled the 2.1/2.2 null-safety deferrals at the render layer:** `DisplayName ?? "(unnamed)"`, `Lore ?? ""` in the presenter; `Art` null → null (view falls back); a discovered-but-unauthored id (no `MonsterDefinition`) → a content-light discovered view-model (valid id + date, null name/tier/lore) rather than a throw. Pinned by the null-name/null-lore/unauthored tests; recorded as settled in `deferred-work.md`. (The stats negative/overflow-bound deferrals stay OPEN — Epic 4/OQ-9; this story only DISPLAYS stats.)
- **Catalog read has one home:** added `CodexService.GetDefinition(id)` (safe-null, mirrors `GetTier`); the detail presenter takes ONLY `CodexService` and never touches `MonsterDatabase` directly (the 2.4 "one home" precedent).
- **Invalid-id asymmetry (documented):** `BuildDetail(badId)` returns the NotDiscovered view-model + `GameLog.Warn` (a UI read must not crash the panel), UNLIKE `RecordDiscoveryAsync` which throws (an Epic-4 caller bug). Pinned.
- **View honest current state:** `CodexDetailView` resolves `CodexService` via `GameServices.Get` and degrades gracefully (inert `Open(id)` no-ops + `GameLog.Warn`, never throws), catching both `ServicesNotReadyException` and `KeyNotFoundException` — the 2.4 `CodexGridView` pattern. CodexService is STILL unregistered (the 2.2 `.asset` deferral), so the panel is logic-complete + view-inert until that asset is authored. The Bootstrap seam comment was extended (3-arg ctor + the new `CodexDetailView` consumer; no live registration).
- **Per-assembly test fakes (the 2.4 precedent):** `FakeClock` is `internal sealed` per assembly, so authored a copy in `Monsters.Tests` + `UI.Tests`. Both deep-clone fakes now copy the new `Discovered` scalar in `Clone()` so the reload round-trip is honest ([[tautological-test-trap]]).
- **No scene, no prefab, no `.asset`** (user-locked altitude). The new scripts' `.meta`s are committed; Epic 6 Story 6.3 places the views + wires grid→detail.

### File List

**Production — NEW (`Veilwalkers.UI`):**
- `Assets/Veilwalkers/UI/Codex/CodexDetailPresenter.cs` (+ `.meta`)
- `Assets/Veilwalkers/UI/Codex/CodexDetailViewModel.cs` (+ `.meta`)
- `Assets/Veilwalkers/UI/Codex/CodexDetailView.cs` (+ `.meta`)

**Production — UPDATED:**
- `Assets/Veilwalkers/Persistence/CodexEntryData.cs` — added the nullable ISO `Discovered` date + doc (`CoerceNullCollections` unchanged, noted).
- `Assets/Veilwalkers/Monsters/Codex/CodexService.cs` — injected `IClock` (3-arg ctor + null-check); stamp `Discovered` on the first-discovery create path; added `GetDefinition(id)`; `using System.Globalization`.
- `Assets/Veilwalkers/Monsters/Codex/CodexEntryView.cs` — added the `Discovered` scalar (value-copied in `From`, null on `NotDiscovered`).
- `Assets/Veilwalkers/App/Bootstrap.cs` — extended the deferred-CodexService seam comment (3-arg ctor with `clock`; noted the new `CodexDetailView` consumer). Seam-only; no registration.

**Tests — NEW:**
- `Assets/Tests/EditMode/UI.Tests/CodexDetailPresenterTests.cs` (+ `.meta`)
- `Assets/Tests/EditMode/UI.Tests/FakeClock.cs` (+ `.meta`)
- `Assets/Tests/EditMode/Monsters.Tests/CodexServiceDateTests.cs` (+ `.meta`)
- `Assets/Tests/EditMode/Monsters.Tests/FakeClock.cs` (+ `.meta`)

**Tests — UPDATED:**
- `Assets/Tests/EditMode/Monsters.Tests/CodexServiceTests.cs` — thread `FakeClock` through `CreateInitialized` (+ a clock overload), the ctor-null-check (3rd null-arg assertion), and the reload site.
- `Assets/Tests/EditMode/Monsters.Tests/CodexServiceRevealTests.cs` — pass a `FakeClock` to `new CodexService`.
- `Assets/Tests/EditMode/Monsters.Tests/FakeCodexProgressStore.cs` — copy the `Discovered` scalar in `Clone()`.
- `Assets/Tests/EditMode/UI.Tests/CodexGridPresenterTests.cs` — pass a `FakeClock` to `new CodexService`.
- `Assets/Tests/EditMode/UI.Tests/FakeUiCodexProgressStore.cs` — copy the `Discovered` scalar in `Clone()`.

**Docs / tracking — UPDATED:**
- `_bmad-output/implementation-artifacts/2-5-view-a-monsters-detail-entry.md` (frontmatter `baseline_commit`, task checkboxes, Dev Agent Record, File List, Change Log, Status).
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (2.5 → review).
- `_bmad-output/implementation-artifacts/deferred-work.md` (settle the 2.1 render-layer null-safety deferrals; add the Epic-6 detail-render deferral).

### Change Log

- 2026-06-14 — Story 2.5 implemented (208/208 headless green, +16). Added the persisted first-discovered date (`CodexEntryData.Discovered` ISO string + `IClock` into `CodexService`, stamped once on first discovery, rollback-safe) and the Codex detail read model: `CodexDetailPresenter` → Unity-free `CodexDetailViewModel` (discovered content vs the "Not yet discovered." copy, null-safe, AC-2 anti-leak), `CodexService.GetDefinition`, the `Discovered` field on `CodexEntryView`, and the thin graceful-degrade `CodexDetailView`. Settled the 2.1/2.2 null-safety deferrals at the render layer. No `SchemaVersion` bump (additive nullable). Detail pixels + grid→detail wiring deferred to Epic 6. Status → review.
- 2026-06-14 — Code review (story-cr-fanout, 3-layer adversarial, 13 findings → 0 patch / 0 AC violation / 10 spec-sanctioned / 3 false-positive). No code changes — every finding either restated a documented design decision or was a verified false-positive (`SetFlag` mutates in place; null-date is spec-required; `UniverseCount` locked at 67). Two Epic-6-owned render nits recorded as new deferrals (the DisplayName placeholder invariant on the unauthored-discovered path; the 0-stat sentinel ambiguity for the binder). Settled the 2.1/2.2 `DisplayName`/`Lore`/`Art` null-safety deferrals at the render layer. Headless gate unchanged at 208/208 (no patch). Status → done.

## Review Findings

Adversarial 3-layer code review (story-cr-fanout: Blind Hunter / Edge Case Hunter / Acceptance Auditor, verify-disputed-first triage), 2026-06-14. **13 raw → 13 deduped (0 cross-layer convergence) → 0 confirmed / 0 patch / 0 AC violation / 10 spec-sanctioned / 3 false-positive.** No code changes; the headless gate stays green from the dev run (208/208). Two Epic-6-owned render nits recorded as new deferrals (below); the rest restate design decisions already captured in this story's Dev Notes.

- [x] [Review][Sanctioned] **Null-value entry repair re-stamps a fresh first-discovered date** (`CodexService.cs`) — explicitly sanctioned by Task 2 ("a null-value-repair … gets a fresh date stamp (acceptable: the prior date was lost with the null value)") + the in-code comment; pinned by `Pre_2_5_entry_without_a_date_reads_as_null_not_a_crash`. Drop.
- [x] [Review][Sanctioned] **Invalid-id read-path returns NotDiscovered before GetEntry** (`CodexDetailPresenter.cs`) — the documented read-degrades / write-asserts asymmetry (Dev Notes edge cases + line on the asymmetry). Drop.
- [x] [Review][Defer] **DisplayName placeholder inconsistency on the unauthored-discovered path** (`CodexDetailPresenter.cs` / `CodexDetailView.cs`) — the presenter passes `null` name for a discovered-but-unauthored id but `UnnamedPlaceholder` for an authored-null name; the two "no name" cases render inconsistently. The `Render` is an explicit Epic-6 stub, so this is harmless today. **New deferral → deferred-work.md** (Epic 6 binding should normalize the placeholder invariant).
- [x] [Review][Defer] **Stats 0-value sentinel ambiguity for the Epic-6 binder** (`CodexDetailViewModel.cs`) — a discovered authored `CaptureDifficulty==0` is indistinguishable from the not-discovered/unauthored `0`; a consumer must gate on `IsDiscovered` first. Stats are PROVISIONAL (OQ-9) and this story only displays them. **New deferral → deferred-work.md** (Epic 6/OQ-9 — the binder must check `IsDiscovered` before interpreting stats).
- [x] [Review][Sanctioned] **`DateTime.Kind`/UTC not asserted before `ToString`** (`CodexService.cs`, ×2 findings) — the sanctioned AR-18 / `DailyRewardService` pattern; `yyyy-MM-dd` is kind-agnostic, the `Discovered` field is never used for time-math, and Kind is the `IClock` impl's contract. Drop.
- [x] [Review][Sanctioned] **No leap-year/year-boundary date test** (`CodexServiceDateTests.cs`) — calendar-edge coverage is not a 2.5 requirement (the lexicographic `yyyy-MM-dd` compare is order-correct across boundaries, the same 1.8 rationale); fixed-instant tests are the Task 6 design. Drop.
- [x] [Review][Sanctioned] **No empty-string-id test** (`CodexDetailPresenterTests.cs`) — `string.IsNullOrEmpty` handles null+empty identically (idiomatic); null + malformed are tested. Drop.
- [x] [Review][Sanctioned] **`ForNotDiscovered` does not self-validate the id** (`CodexDetailViewModel.cs`) — `internal`, both callers guard via `IsValidMonsterId` at `BuildDetail`; validation at the entry point is the intended shape. Drop.
- [x] [Review][Sanctioned] **Duplicated `FakeClock` across test assemblies** (`Monsters.Tests` + `UI.Tests`) — the explicitly-sanctioned per-assembly-fake pattern (`internal sealed` blocks sharing; the 2.4 `FakeUiCodexProgressStore` precedent). Drop.
- [x] [Review][False-positive] **Error message `:00` format if `UniverseCount` ∉ [1,99]** — `UniverseCount` is the locked lore constant (67); message reads correctly. Drop.
- [x] [Review][False-positive] **Null date on a discovered entry logs no warning** — null date is a spec-REQUIRED legitimate pre-2.5 state; a warning would contradict "never a crash / legitimate state". Drop.
- [x] [Review][False-positive] **`SetFlag` might overwrite the date** — verified: `SetFlag` mutates `Captured`/`Slain` in place on the same instance; the date stamp is never lost. Drop.
