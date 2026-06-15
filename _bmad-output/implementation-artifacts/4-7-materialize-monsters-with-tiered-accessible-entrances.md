---
baseline_commit: 332f1cbfa4e18e172a66a6775143f1ef702062d1
---

# Story 4.7: Materialize Monsters with tiered, accessible entrances

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want Monsters to arrive with a tiered cinematic entrance,
so that rare Monsters feel like events and the camera-magic moment lands.

## Acceptance Criteria

**AC-1 — Tiered entrance + action-bar gating + ambiance shift**
**Given** a Lure resolves to a Monster of a given tier
**When** it materializes
**Then** the action bar is HIDDEN during materialization and pops in only when the Monster settles
**And** the entrance variant + duration scales with tier: T1 Pop-in ~0.5s, T2 Unfurl ~1s, T3 Seep ~1.5s, T4 Tear ~2s, T5 Breach ~2.5–3s
**And** AR ambiance (tint/lighting/vignette/SLAY-glow) shifts toward the active tier — never shown as a literal rarity bar.
[Source: docs/epics.md:733-737; docs/epics.md:103 (UX-DR10); docs/epics.md:105 (UX-DR12 — action bar hidden during materialization, pops in on settle)]

**AC-2 — T5 Breach reads as branded FX, never a crash, never blocks input recovery**
**Given** a T5 Nightmare Breach
**When** it plays
**Then** the camera feed/cartoon UI glitch reads as a designed branded FX (brief, input recovery never blocked), never a crash.
[Source: docs/epics.md:739-741; docs/epics.md:104 (UX-DR11)]

**AC-3 — Reduced-motion / photosensitivity taming + sub-second spend-ack (NFR-2)**
**Given** the reduced-motion / photosensitivity setting is ON
**When** any materialization plays
**Then** glitch/screen-shake/strobe is tamed to a non-flashing low-motion entrance, and the dread still reads without strobe/shake
**And** the credit spend-ack still confirms sub-second regardless of materialization duration (NFR-2).
[Source: docs/epics.md:743-746; docs/epics.md:104 (UX-DR11 respects reduced-motion); docs/epics.md:110 (UX-DR17 accessibility floor — tames T5 glitch + per-tier ambiance + screen-shake, glitch→static branded transition); docs/architecture.md NFR-2 spend-ack]

## The scope contract (READ FIRST — this is the load-bearing decision)

This story's ACs are written in presentation/VFX language (entrance animations, camera glitch, ambiance shaders). **This story builds NONE of that pixel/shader/scene work.** Per the repo's settled UI-presenter pattern (Story 2.4/2.5; the `ui-presenter-pattern` memory) and the fact that **there is no AR rig scene yet** (deferred to Story 6.3), Epic 4 is the logic tier. 4.7 builds the **materialization *decision* logic** as a pure-logic, headless-EditMode-tested presenter, and **defers the actual VFX/shaders/scene render to Epic 6** (where the AR HUD + chunky component kit + design tokens land).

**What 4.7 BUILDS (the testable kernel):**
1. A `MaterializationPlan` value type + a tier→plan map: each `Rarity` tier → its entrance `MaterializationVariant` (Pop-in/Unfurl/Seep/Tear/Breach) and its nominal `DurationSeconds` (~0.5s … ~2.5–3s). (AC-1)
2. The **action-bar-hidden-during / pops-in-on-settle** gating expressed as a decision the presenter owns (a plan flag / a settle signal), so the view never decides when the bar shows. (AC-1)
3. The **ambiance shift toward the active tier** expressed as data (a tier-keyed ambiance descriptor on a "diffused dread scale" — UX-DR10's phrase), explicitly NOT a literal 0..1 rarity-bar value — "shifts toward the active tier," never a numeric rarity meter in chrome. (AC-1)
4. The **T5 Breach** plan carries a `BrandedGlitch` flag + a bounded duration + an `InputRecoveryBlocked = false` invariant, so the FX is brief and never blocks input recovery (the logic-side guarantee against "reads as a crash"). (AC-2)
5. A **reduced-motion** input that maps each plan to a **tamed** variant: no strobe/shake/glitch, low-motion, glitch→static branded transition, dread still reads (a non-empty tamed descriptor — never "no entrance"). (AC-3)
6. The **NFR-2 sub-second spend-ack decoupling**: the spend-ack is the synchronous `LureResult` already returned by Story 4.2 BEFORE the materialization plays; 4.7 pins that the materialization `DurationSeconds` is independent of (never gates) the spend-ack. Modeled as a presenter contract/test, not a timer. (AC-3)

**What 4.7 DEFERS to Epic 6 (record in deferred-work.md):** the actual entrance animations/tweens, the camera-feed glitch shader, the ambiance tint/lighting/vignette/SLAY-glow render, the screen-shake, the action-bar widget show/hide animation, the reduced-motion *setting UI* + its persistence, and the AR-rig scene wiring that drives all of it. 4.7 returns the **plan**; Epic 6 renders it.

## Tasks / Subtasks

- [x] **Task 1 — Define the materialization value types in `Veilwalkers.UI`** (AC: 1, 2, 3) — *plain C#, NO MonoBehaviour, NO Unity types in the logic; headless-testable. The `Veilwalkers.UI` assembly already references `Veilwalkers.Monsters` (for `Rarity`) — NO asmdef edge change needed (verified: Veilwalkers.UI.asmdef references Monsters; UI.Tests.asmdef references UI + Monsters). [Source: Assets/Veilwalkers/UI/Veilwalkers.UI.asmdef; Assets/Tests/EditMode/UI.Tests/Veilwalkers.UI.Tests.asmdef]*
  - [x] Create `Assets/Veilwalkers/UI/Encounter/MaterializationVariant.cs`, namespace `Veilwalkers.UI`: `public enum MaterializationVariant { PopIn, Unfurl, Seep, Tear, Breach }` — XML-doc each as the per-tier entrance (T1 Pop-in … T5 Breach, UX-DR10). One member per tier, ordered to mirror `Rarity` Common→Nightmare. The view maps each to a tween/FX in Epic 6; this enum is the decision.
  - [x] Create `Assets/Veilwalkers/UI/Encounter/MaterializationPlan.cs`, namespace `Veilwalkers.UI`: a `public readonly struct MaterializationPlan` carrying: `Rarity Tier`, `MaterializationVariant Variant`, `float DurationSeconds`, `bool BrandedGlitch` (true only for Breach/T5 — UX-DR11), `bool ReducedMotion` (whether this is the tamed plan), `bool HideActionBarDuringMaterialization` (always true per UX-DR12, but expose it so the view never hard-codes the gate), and the ambiance descriptor (see Task 2). Carry a `MonsterId` string (the spawned id from `LureResult` — the thing being materialized; nullable/empty-coalesced like `ScanResult`/`LureResult` precedent). XML-doc that `DurationSeconds` is the materialization render duration and is INDEPENDENT of the spend-ack (NFR-2). **DONE — also carries `InputRecoveryBlocked` (always false, AC-2); `HideActionBarDuringMaterialization` + `InputRecoveryBlocked` are constructor-fixed invariants, not inputs.**
  - [x] Create `Assets/Veilwalkers/UI/Encounter/MaterializationAmbiance.cs`, namespace `Veilwalkers.UI`: a small value type describing the tier-keyed ambiance SHIFT (e.g. an enum `AmbianceIntensity { Calm, Unsettled, Tense, Dreadful, Nightmarish }` keyed to tier, plus a `bool` for the SLAY-glow on the highest tier). **CRITICAL (AC-1):** XML-doc that this is a discrete *toward-the-tier* descriptor, NOT a literal 0..1 rarity-bar value — the AC bans "shown as a literal rarity bar." Epic 6 maps it to tint/lighting/vignette/SLAY-glow tokens.

- [x] **Task 2 — Build the pure-logic `MaterializationPresenter` in `Veilwalkers.UI`** (AC: 1, 2, 3) — *plain C#, NO MonoBehaviour; the entire tier→plan + reduced-motion-taming decision lives here so it is headless-testable. Mirror `CodexGridPresenter` (ctor-injected dependency, no service-locator read, pure recompute, fresh value every call). [Source: Assets/Veilwalkers/UI/Codex/CodexGridPresenter.cs]*
  - [x] Create `Assets/Veilwalkers/UI/Encounter/MaterializationPresenter.cs`, namespace `Veilwalkers.UI`.
  - [x] **`public MaterializationPlan PlanFor(Rarity tier, bool reducedMotion)`** — the core decision. Map each `Rarity` to its `(MaterializationVariant, DurationSeconds, AmbianceIntensity)`:
    - `Common` → `PopIn`, ~0.5s, `Calm`
    - `Uncommon` → `Unfurl`, ~1.0s, `Unsettled`
    - `Rare` → `Seep`, ~1.5s, `Tense`
    - `Epic` → `Tear`, ~2.0s, `Dreadful`
    - `Nightmare` → `Breach`, ~2.75s (within the ~2.5–3s band), `Nightmarish`, `BrandedGlitch = true`, SLAY-glow on.
    Durations are the nominal mid-band values (the AC gives ranges/approximate values; pin the BEHAVIOR — monotonic non-decreasing with tier + each within its AC band — NOT an exact magic number; the precise timing is OQ-9 / Epic-6 polish, mirror the 4.2/4.4/4.5 "pin the inequality, defer the magnitude" precedent). Use named `const` durations on the presenter (the `LureSystem.BasicRareChance` precedent), NOT `EconomyConfig` fields (these are UX timings, not economy values).
  - [x] **`HideActionBarDuringMaterialization` is always `true`** on the plan (UX-DR12 — bar hidden during materialization, pops in on settle). Expose a companion notion of "settle" — the presenter need not run a timer; model "settled" as the moment the view calls back after `DurationSeconds` (document that the view owns the clock; the presenter owns the decision THAT the bar is hidden until settle). Keep the presenter clock-free (mirror the `CodexGridPresenter` no-lifecycle rule); if a settle helper is useful, make it a pure `bool ShouldShowActionBar(bool materializationSettled) => materializationSettled;` so the gate is testable without a timer.
  - [x] **Reduced-motion taming (AC-3):** when `reducedMotion` is true, the returned plan must be TAMED — `BrandedGlitch = false` for EVERY tier (including Breach: glitch→static branded transition, UX-DR17), no strobe/shake implied, low-motion. **The dread must still read:** the ambiance intensity is PRESERVED (still `Nightmarish` for T5) and the variant is NOT flattened to "no entrance" — pin that a tamed plan still has a non-trivial entrance + the tier's ambiance (the AC: "dread still reads without strobe/shake"). Decide + document whether tamed durations are shortened or preserved (recommend: preserve the variant + ambiance, drop only the glitch/shake/strobe — taming is about MOTION, not removing the entrance).
  - [x] **`PlanFor(string monsterId, Rarity tier, bool reducedMotion)`** overload (or carry `MonsterId` on the plan) so the plan names what is materializing — the `LureResult`'s spawned id(s) feed this (the deferred 4.2 handoff: "the materialization plays afterward against the returned ids"). Null/empty id coalesces to empty (the `ScanResult`/`LureResult` factory precedent), it does not throw — the tier drives the plan, the id is a label.
  - [x] Ctor: if the presenter needs a `CodexService` (to resolve a tier from a monster id via `GetTier`), ctor-inject it and guard null (`ArgumentNullException`, the `CodexGridPresenter` precedent). **Prefer taking `Rarity` directly** (the caller already has the spawned monster + its tier) to keep the presenter dependency-free and the UI.Tests asmdef unchanged (UI.Tests does NOT reference Encounter). Only inject `CodexService` if a `monsterId`→tier resolve is genuinely needed at this layer; if so, UI.Tests already references Monsters so `CodexService` is reachable.

- [x] **Task 3 — Thin, logic-free `MaterializationView : MonoBehaviour`** (AC: 1, 2, 3) — *binds the presenter; holds NO decisions; untestable-by-design. Mirror `CodexDetailView`/`CodexGridView`: the ONLY service-locator reader (AR-4), graceful-degrade guard, render is a stub that logs + TODO(Epic 6). [Source: Assets/Veilwalkers/UI/Codex/CodexDetailView.cs]*
  - [x] Create `Assets/Veilwalkers/UI/Encounter/MaterializationView.cs`, namespace `Veilwalkers.UI`. Resolve any needed service in `Awake` inside a `try/catch (ServicesNotReadyException | KeyNotFoundException)` → stay inert + `GameLog.Warn` (the `CodexDetailView` precedent — never throw at Awake; the AR rig + live registration are deferred). If the presenter takes only `Rarity` (no service), the view can construct it directly with no locator read — then there is NO Awake guard needed; document that.
  - [x] Public entry `Play(string monsterId, Rarity tier)` (the Epic-6 wire point from the encounter, fed by `LureResult`): reads the reduced-motion setting (a stubbed `bool` for now — the real setting + persistence is Epic 6; document the TODO), calls `presenter.PlanFor(...)`, and pushes the plan to a stub `Render(MaterializationPlan)` that `GameLog.Info`s the variant/duration/ambiance and a `TODO(Epic 6)` for the real tween/glitch/ambiance + action-bar hide/show + the AR-rig scene wiring. **No decisions in the view** beyond which stub log to emit.
  - [x] Document on the view: the spend-ack is already delivered by Story 4.2's synchronous `LureResult` BEFORE `Play` is called — `Play` (the materialization) never gates the ack (NFR-2). The view does NOT touch credits.

- [x] **Task 4 — Tests: `MaterializationPresenter` (UI.Tests)** (AC: 1, 2, 3) — *exercise the production presenter; never recompute the tier→variant rule in the test (the 2.1–2.4 anti-tautology bar). Each test mutation-testable: removing a rule turns a test red. [Source: Assets/Tests/EditMode/UI.Tests/CodexGridPresenterTests.cs]*
  - [x] Create `Assets/Tests/EditMode/UI.Tests/MaterializationPresenterTests.cs`, namespace `Veilwalkers.UI.Tests`. NO asmdef change (UI.Tests already references UI + Monsters).
  - [x] **AC-1 variant mapping (one assertion per tier, table-driven or explicit):** `PlanFor(Common,false).Variant == PopIn`, `Uncommon→Unfurl`, `Rare→Seep`, `Epic→Tear`, `Nightmare→Breach`. (Asserts the named mapping, not a recomputation.)
  - [x] **AC-1 duration scales with tier (the BEHAVIOR pin):** `Plan durations are strictly monotonic non-decreasing Common→Nightmare` (a loop over ordered tiers asserting `dur[i] <= dur[i+1]` with at least one strict increase), AND each tier's duration is within its AC band (T1 in [~0.4,0.6], … T5 in [2.5,3.0]) — pin the band, not a magic number. This is the 4.2/4.4 "pin the inequality + the band, defer the exact value" precedent.
  - [x] **AC-1 action-bar gate:** `PlanFor(anyTier,false).HideActionBarDuringMaterialization == true`; `ShouldShowActionBar(false) == false` and `ShouldShowActionBar(true) == true` (the bar pops in only on settle).
  - [x] **AC-1 ambiance is per-tier, NOT a literal bar:** `PlanFor(Common,..).Ambiance.Intensity == Calm` … `Nightmare → Nightmarish`; assert the ambiance type exposes a discrete intensity (and SLAY-glow on the top tier), NOT a 0..1 float field (a compile-time/shape assertion + a doc-pinned design note — the AC bans a literal rarity bar).
  - [x] **AC-2 Breach is branded + bounded + never blocks input:** `PlanFor(Nightmare,false).BrandedGlitch == true`; `DurationSeconds <= 3.0f` (brief/bounded); a plan never carries an "input blocked" state (assert there is no blocking flag / it is false) — the logic-side guarantee the FX reads as designed, not a crash.
  - [x] **AC-2 branded glitch is ONLY the top tier:** for every tier `< Nightmare`, `BrandedGlitch == false` (a non-Breach entrance is not a glitch).
  - [x] **AC-3 reduced-motion tames every tier:** for EVERY tier, `PlanFor(tier,true).BrandedGlitch == false` (including Nightmare — glitch→static, UX-DR17); the tamed plan is `ReducedMotion == true`.
  - [x] **AC-3 dread still reads when tamed:** `PlanFor(Nightmare,true).Ambiance.Intensity == Nightmarish` (ambiance PRESERVED — tier dread survives taming) AND the tamed plan still has a real entrance variant (NOT flattened to a no-op / a "None" — pin that `Variant` is still the tier's variant or an explicit tamed-but-present variant). This is the AC's "the dread still reads without strobe/shake."
  - [x] **AC-3 NFR-2 spend-ack decoupling:** a presenter-level pin that the materialization plan's `DurationSeconds` is metadata the RENDER consumes and is independent of any spend value — document + assert that `PlanFor` performs NO credit/economy work and returns a plan whose duration does not gate anything the caller must await before acking (the spend-ack is the synchronous `LureResult` from 4.2). A focused unit test: `PlanFor` is a pure synchronous call returning immediately (no async, no I/O) — the materialization duration lives in DATA, never in a blocking call (so the ack, delivered earlier by 4.2, is unaffected). Pair with a doc note citing `LureResult` as the ack contract.
  - [x] **Ctor guard (if a service is injected):** `new MaterializationPresenter(null)` → `ArgumentNullException` (the `CodexGridPresenter` precedent). Omit if the presenter is dependency-free.
  - [x] **Monster-id label (if carried):** `PlanFor("mon07", Rare, false).MonsterId == "mon07"`; a null/empty id coalesces to empty, does not throw (the `LureResult`/`ScanResult` factory precedent).
  - [x] Run the EditMode suite headless after authoring (Task 5).

- [x] **Task 5 — Headless EditMode gate + bookkeeping** (AC: all) —
  - [x] Run the EditMode suite headless (the `unity-headless-testing` memory command). **Unity Editor must be fully closed** (`Temp/UnityLockfile` present = locked — ask James to close it). **Never trust exit 0** — confirm `test-results.xml` has `result="Passed"` AND grep `editor-test.log` for `error CS` (a compile error still exits 0 via the crash handler). Report the green count (expect the prior 381 + the new MaterializationPresenter tests). Delete `test-results.xml` + `editor-test.log` before any commit.
  - [x] **Bootstrap seam:** there is NO live registration to add — the presenter is constructed by the view (Epic 6 wires the view into the AR-rig scene). If the presenter takes a service, do NOT add live wiring (the inherited `MonsterDatabase.asset` blocker, the 4.1–4.6 posture); if a seam comment is warranted, add it. Most likely: no Bootstrap change at all (a `Rarity`-only presenter has no service dependency).
  - [x] **Record the deferrals** (Epic-6 render items + the reduced-motion setting/persistence + the AR-rig scene wiring + the OQ-9 exact-timing band) in `deferred-work.md` under a dev heading, and **settle the 4.2 deferral** (deferred-work.md:60 — "the tiered materialization VFX / entrance render … is Story 4.7": mark the DECISION layer landed here, the render deferred to Epic 6).

## Dev Notes

### The scope decision (sanctioned deviation from a literal reading of the ACs)

The ACs read as pixel/VFX work. The **sanctioned deviation** (approved at the create-story gate) is: 4.7 builds the materialization DECISION as a pure-logic presenter + value types + a thin inert view, and DEFERS all actual VFX/shader/scene/animation render to Epic 6. This is NOT scope-cutting — it is the repo's settled altitude split:
- **The `ui-presenter-pattern` memory / Story 2.4 & 2.5:** every UI story = pure-logic presenter (EditMode-tested) + thin logic-free MonoBehaviour view; "visual polish (tokens, chunky components) defers to Epic 6."
- **There is no AR rig scene yet** — the AR-rig scene wiring is Story 6.3 (deferred-work.md, 3.3/3.4/3.5/4.1 entries all re-point scene render to 6.3/Epic 6). A VFX-render story is impossible before the rig exists.
- **Story 4.2 explicitly named 4.7 as the owner of the materialization render** (deferred-work.md:60) and pinned the handoff: `LureResult` carries the spawned id(s) + IS the sub-second spend-ack; "the materialization plays afterward against the returned ids."

### Sanctioned-deviation / placement matrix

| Item | Where it goes | Why |
|---|---|---|
| `MaterializationPresenter` + value types | `Veilwalkers.UI` (new `UI/Encounter/` folder) | The UI-presenter pattern: pure logic in the UI tier, EditMode-tested. `Veilwalkers.UI` already references `Monsters` (`Rarity`) + `Encounter` (`LureResult`, if needed) — top of the acyclic allow-matrix. [Source: Veilwalkers.UI.asmdef] |
| Presenter input = `Rarity` (NOT `LureResult`) | Take the tier directly | Keeps the presenter dependency-free + UI.Tests asmdef UNCHANGED (UI.Tests references Monsters but NOT Encounter). The caller (the view / Epic 6) reads the tier off the spawned monster and passes it in. Avoids widening the test asmdef. |
| Durations as presenter `const`s, NOT `EconomyConfig` | `MaterializationPresenter` consts | These are UX timings, not economy values; mirrors `LureSystem.BasicRareChance` being a local const (deferred-work.md 4.2/4.4 — "the AC is the behavior, not the number"). Adding SO fields would widen `EconomyConfig.SetForTests`. |
| Reduced-motion as a presenter INPUT `bool` | `PlanFor(tier, reducedMotion)` | The taming is a pure decision (testable). The SETTING UI + its persistence (a `SaveModel` flag or a player-pref) is Epic 6 — the view stubs the read. |
| Action-bar hide/show, ambiance render, glitch shader, screen-shake, tweens | Epic 6 (deferred) | No AR rig / chunky component kit / design tokens exist yet (Epic 6: 6.1 tokens, 6.2 components, 6.3 surfaces). 4.7 returns the PLAN; Epic 6 renders it. |

### Files to touch

- **NEW** `Assets/Veilwalkers/UI/Encounter/MaterializationVariant.cs` (enum)
- **NEW** `Assets/Veilwalkers/UI/Encounter/MaterializationPlan.cs` (readonly struct)
- **NEW** `Assets/Veilwalkers/UI/Encounter/MaterializationAmbiance.cs` (value type + intensity enum)
- **NEW** `Assets/Veilwalkers/UI/Encounter/MaterializationPresenter.cs` (pure logic)
- **NEW** `Assets/Veilwalkers/UI/Encounter/MaterializationView.cs` (thin MonoBehaviour, inert/stub)
- **NEW** `Assets/Tests/EditMode/UI.Tests/MaterializationPresenterTests.cs`
- **NO asmdef changes** — `Veilwalkers.UI.asmdef` already references `Monsters` (+ `Encounter`); `Veilwalkers.UI.Tests.asmdef` already references `UI` + `Monsters`. (Verify the `.cs.meta` files are generated by Unity on the next domain reload — author the `.cs`, let Unity create metas; do NOT hand-author GUIDs.)
- **POSSIBLY** `_bmad-output/implementation-artifacts/deferred-work.md` (record Epic-6 render deferrals + settle the 4.2 materialization-render deferral) — done in Task 5.

### Current state of the seams this story consumes

- **`LureResult` (Assets/Veilwalkers/Encounter/LureResult.cs):** already carries `Spend` (the sub-second spend-ack, NFR-2) + the spawned Monster id(s), and its XML-doc already names "the (deferred) materialization render (Story 4.7) will animate" the ids. 4.7 does NOT modify `LureResult` — it consumes the contract. The spend-ack is delivered synchronously by `TryLureAsync` BEFORE any materialization plays.
- **`Rarity` (Assets/Veilwalkers/Monsters/Rarity.cs):** the ordered tier enum Common(0)…Nightmare(4) = T1…T5 (the UX Dread Ladder, OQ-2). Append-only, never reorder — the tier→variant map keys off it. 4.7 does NOT modify it.
- **`CodexService.GetTier(id)` (Monsters):** resolves a monster id → its `Rarity?` (used by `CodexGridPresenter`). Available if the presenter ever needs a monsterId→tier resolve; prefer passing `Rarity` directly to avoid the dependency.

### What must be preserved

- The `LureResult` spend-ack contract: nothing 4.7 adds may make the spend-ack await the materialization (NFR-2). The materialization duration lives in plan DATA the render consumes — never in a blocking call on the spend path.
- The acyclic assembly graph + the allowed-edge matrix (the `unity-asmdef-architecture-test` memory): 4.7 adds NO new asmdef edge, so the `AcyclicDependencyTests` matrix needs NO edit. Confirm the suite stays green.
- The AR-4 locator discipline: pure-logic presenters are ctor-injected and read NO service locator; only the MonoBehaviour view may read `GameServices` (and only inside the graceful-degrade guard).

### Anti-tautology bar (the 2.1–2.4 / 4.x test discipline)

- Tests exercise the PRODUCTION presenter; never re-implement the tier→variant/duration map in the test. Assert the named outputs + the behavioral invariants (monotonic durations, branded-glitch-only-on-Breach, reduced-motion tames every tier, ambiance preserved when tamed).
- Each test is mutation-testable: deleting a mapping rule or the taming branch turns a specific test red.
- Pin the BEHAVIOR + the AC band, not a magic duration number (the exact ms is Epic-6/OQ-9 polish).

### Testing standards

- EditMode, NUnit, in `Veilwalkers.UI.Tests`. Headless run via the `unity-headless-testing` memory command (Unity 2022.3.62f3, `-runTests -testPlatform EditMode -nographics`). Verify `test-results.xml` `result="Passed"` AND grep the log for `error CS`. Delete the result/log files before committing.
- No new fakes needed if the presenter takes `Rarity` (pure value input). If it injects `CodexService`, reuse the `CodexGridPresenterTests` setup (REAL `CodexService` over a fake store + in-memory DB) — UI.Tests already has `FakeUiCodexProgressStore`.

### References

- [Source: docs/epics.md:725-746 — Story 4.7 ACs]
- [Source: docs/epics.md:103 (UX-DR10 materialization hero primitive: tiered entrance per rarity, action bar HIDDEN during materialization, duration T1~0.5s→T5~2.5–3s, spend-ack sub-second NFR-2, variants Pop-in/Unfurl/Seep/Tear/Breach, ambiance shifts toward active tier — diffused dread, never a literal rarity bar)]
- [Source: docs/epics.md:104 (UX-DR11 T5 Breach FX: camera/UI glitch, branded designed FX never a crash, brief, input recovery never blocked, respects reduced-motion)]
- [Source: docs/epics.md:105 (UX-DR12 action bar hidden during materialization, pops in on settle)]
- [Source: docs/epics.md:110 (UX-DR17 accessibility floor: reduced-motion tames T5 glitch + per-tier ambiance + screen-shake, no flashing, glitch→static branded transition)]
- [Source: docs/epics.md:109 (UX-DR16 interaction primitives: tap-to-act on the action bar, COST SHOWN BEFORE SPEND — reinforces the spend-ack-before-materialization ordering)]
- [Source: docs/epics.md:156 — Story 4.7 traceability line: **NFR-2 · Arch AR-8 (per-action atomic) / AR-9 (state machine) · UX-DR10, UX-DR11, UX-DR12, UX-DR16**]
- [Source: docs/epics.md:48 (NFR-2 verbatim: "spend-ack ≠ materialization duration; the ack confirms instantly even during a multi-second Breach")]
- [Source: docs/architecture.md:215 — "UI subscribes… keeps sub-second feedback (NFR-2) decoupled from logic" — the canonical decoupling citation for AC-3]
- [Source: docs/ux-designs/ux-Veilwalkers-2026-06-10/.decision-log.md:126,145 — entrance length is itself part of the reward (T1 ~0.5s pop → T5 ~2-3s breach); spend-ack ≠ materialization duration; action bar HIDDEN during all 5, pops in on settle]
- [Source: Assets/Veilwalkers/UI/Codex/CodexGridPresenter.cs — the pure-logic-presenter precedent to mirror]
- [Source: Assets/Veilwalkers/UI/Codex/CodexDetailView.cs — the thin inert-view + graceful-degrade precedent to mirror]
- [Source: Assets/Veilwalkers/Monsters/Rarity.cs — the ordered tier enum]
- [Source: Assets/Veilwalkers/Encounter/LureResult.cs — the spend-ack + spawned-id handoff (already names Story 4.7)]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md:60 — the 4.2 deferral 4.7 settles (decision layer) / re-defers (render layer to Epic 6)]
- [Source: memory ui-presenter-pattern; memory unity-asmdef-architecture-test; memory unity-headless-testing]

### Project Structure Notes

- New `UI/Encounter/` folder under `Assets/Veilwalkers/UI/` mirrors the existing `UI/Codex/` grouping — presenter + view + value types co-located, tests in `UI.Tests`. No conflict with the unified structure.
- No asmdef variance: the edges already exist; the acyclic matrix is unchanged.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Opus 4.8) — via the story-automator runbook (gated CS → VS → DS → CR).

### Debug Log References

- Headless EditMode run (Unity 2022.3.62f3, `-runTests -testPlatform EditMode -nographics`): dev baseline **399/399** (381 prior + 18 new `MaterializationPresenterTests`); after the CR patches **401/401 passed, 0 failed, 0 skipped, 0 compile errors** (`error CS` grep clean; +2 CR-patch tests). `test-results.xml` `result="Passed"`; `test-results.xml` + `editor-test.log` deleted after verification (not gitignored).

### Completion Notes List

- **Scope as approved at the create-story gate:** built the materialization DECISION as a pure-logic presenter + value types + a thin inert view; deferred ALL actual VFX/shader/scene/animation render to Epic 6 (recorded in deferred-work.md; the 4.2 "materialization render is 4.7" deferral is SETTLED at the decision layer + re-deferred at the render layer).
- **`MaterializationPresenter` is dependency-free** — it takes a `Rarity` (+ a reduced-motion `bool`) directly, NOT a `CodexService` or `LureResult`. This keeps the presenter pure AND leaves the `UI.Tests` asmdef unchanged (UI.Tests references Monsters but not Encounter). No ctor → no ctor-null guard test (none needed).
- **AC-1:** `PlanFor` maps each tier → its named `MaterializationVariant` (Pop-in/Unfurl/Seep/Tear/Breach) + a duration (mid-band consts 0.5/1.0/1.5/2.0/2.75s) + a discrete `AmbianceIntensity` (Calm…Nightmarish, SLAY-glow on T5 only). Durations are `internal const` (UX timings, not `EconomyConfig`); tests pin the BEHAVIOR (monotonic non-decreasing + each within its AC band), not the magic number. `HideActionBarDuringMaterialization` is a fixed `true` invariant on the plan; `ShouldShowActionBar(settled)` is the clock-free gate (the view owns the clock). Ambiance is a discrete enum, deliberately NOT a 0..1 float — the AC bans a literal rarity bar.
- **AC-2:** `BrandedGlitch` is true for the T5 Breach only; `DurationSeconds <= 3.0` keeps it brief/bounded; `InputRecoveryBlocked` is a fixed `false` invariant on EVERY plan (the logic-side guarantee the FX reads as designed FX, never a crash/hang).
- **AC-3:** reduced-motion ON disables `BrandedGlitch` on every tier (including Breach — glitch→static) while PRESERVING the variant + ambiance (a tamed Nightmare is still a Breach with Nightmarish ambiance — the dread still reads). NFR-2 decoupling proven at the presenter level: `PlanFor` is synchronous (not a `Task`), does no economy/I/O work, and returns the duration as plan DATA — so the spend-ack (delivered earlier by Story 4.2's synchronous `LureResult`) is never gated by the entrance length.
- **`MaterializationView`** is the thin logic-free MonoBehaviour wire point (`Play(monsterId, tier)`), constructs the presenter directly (no locator read → no Awake guard needed, unlike `CodexDetailView`), and stubs `Render` to a `GameLog.Info` + Epic-6 TODOs. `_reducedMotion` is a stubbed field (the real setting/persistence is Epic 6).
- **No Bootstrap change, no asmdef change** — the dependency-free presenter has nothing to register; the `Veilwalkers.UI` ↔ `Monsters` edge already exists so the acyclic matrix is unchanged.

### File List

- **NEW** `Assets/Veilwalkers/UI/Encounter/MaterializationVariant.cs`
- **NEW** `Assets/Veilwalkers/UI/Encounter/MaterializationAmbiance.cs`
- **NEW** `Assets/Veilwalkers/UI/Encounter/MaterializationPlan.cs`
- **NEW** `Assets/Veilwalkers/UI/Encounter/MaterializationPresenter.cs`
- **NEW** `Assets/Veilwalkers/UI/Encounter/MaterializationView.cs`
- **NEW** `Assets/Tests/EditMode/UI.Tests/MaterializationPresenterTests.cs`
- **MODIFIED** `_bmad-output/implementation-artifacts/deferred-work.md` (4.7 dev deferrals + settled the 4.2 materialization-render deferral)
- **MODIFIED** `_bmad-output/implementation-artifacts/sprint-status.yaml` (4.7 → in-progress → review)
- (Unity auto-generates the `.cs.meta` files on the next domain reload — not hand-authored.)

### Change Log

- 2026-06-15 — Story 4.7 implemented (DS phase, story-automator). Materialization DECISION layer: `MaterializationPresenter` (tier→variant+duration map, action-bar gate, T5 branded-glitch, reduced-motion taming, NFR-2 decoupling) + `MaterializationPlan`/`MaterializationVariant`/`MaterializationAmbiance` value types + thin inert `MaterializationView`. 18 new EditMode tests, 399/399 green. VFX/scene render deferred to Epic 6.
- 2026-06-15 — Code review (3-layer adversarial fan-out, 11 agents): 8 raw → 8 deduped → **2 confirmed (both PATCHED in-story), 1 deferred, 4 spec-sanctioned, 1 false-positive, 0 AC violations.** Both confirmed findings shared one root cause (`MaterializationPlan` public ctor permitting inconsistent / default-bypassed plans); patched via the `LureResult` private-ctor + `internal Create` factory precedent + null-safe `MonsterId` getter + constant-property invariants (resolving the deferred finding for free). +2 CR-patch tests. Re-verified green: **401/401 EditMode, 0 compile errors** (was 399 at the dev baseline).

### Review Findings

Adversarial 3-layer CR (Blind Hunter / Edge Case Hunter / Acceptance Auditor) + per-finding verifier. 8 raw → **2 confirmed (both PATCHED), 1 deferred, 4 spec-sanctioned, 1 false-positive, 0 AC violations.** Re-verified green (401/401 EditMode, 0 compile errors).

- [x] [Review][Patch] **`MaterializationPlan` public ctor permitted internally-inconsistent plans** (blind-hunter, nit→confirmed) — a future external caller could build Tier=Common + Variant=Breach. Fixed: ctor made `private`, added `internal static MaterializationPlan.Create(...)` so only `MaterializationPresenter` (the sole tier→outputs map authority) constructs plans (the `LureResult` factory precedent). [Assets/Veilwalkers/UI/Encounter/MaterializationPlan.cs; Assets/Veilwalkers/UI/Encounter/MaterializationPresenter.cs]
- [x] [Review][Patch] **`default(MaterializationPlan).MonsterId` was null, not empty** (blind-hunter, minor→confirmed) — contradicted the documented "empty when not supplied" contract; an Epic-6 render doing `MonsterId.Length` would NRE on a default plan. Fixed: `MonsterId` is now a null-safe getter coalescing the backing field → empty. Pinned by `Default_plan_monster_id_is_empty_not_null`. [Assets/Veilwalkers/UI/Encounter/MaterializationPlan.cs]
- [x] [Review][Defer→folded into the patch] **`default(MaterializationPlan)` violated the "Always true" HideActionBar invariant** (blind-hunter, minor→out-of-scope) — triaged out-of-scope (the story never produces a default plan), but the same root-cause fix RESOLVES it: `HideActionBarDuringMaterialization` + `InputRecoveryBlocked` are now constant PROPERTIES (not ctor-set fields), so a default plan honors them. Pinned by `Default_plan_still_hides_the_action_bar_and_never_blocks_input`. No separate work remains. [Assets/Veilwalkers/UI/Encounter/MaterializationPlan.cs]
- [x] [Review][Sanctioned] **No test for out-of-bounds `Rarity`; future-tier silent degradation** (edge-case-hunter, ×2) — the `default:` switch cases degrade an unmapped tier to the calmest entrance (NFR-3 graceful boundary, documented); the anti-tautology bar mandates mutation tests on the 5 existing append-only members, not completeness for hypothetical tiers. A 6th tier must manually extend the switches + `AscendingTiers`. Owner: a future Rarity-tier addition.
- [x] [Review][Sanctioned] **`MaterializationView._reducedMotion` stubbed false until Epic 6** (edge-case-hunter, minor) — the approved Epic-6 setting-read deferral; the presenter taming LOGIC is proven by the `reducedMotion=true` tests. Owner: Epic 6.
- [x] [Review][Sanctioned] **`Render` stub logs `{plan.Tier}`** (edge-case-hunter, minor) — `Render` is the sanctioned Epic-6 stub; enum-log robustness belongs to the Epic-6 render layer. Owner: Epic 6.
- [x] [Review][False-positive] **`GameLog.Info` dependency unverified** (blind-hunter, nit) — verified real (`Veilwalkers.Core/GameLog.cs:21`; UI asmdef refs Core; `using` present). No action.
