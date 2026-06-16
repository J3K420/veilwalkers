---
baseline_commit: 6e79a84
---

# Story 6.1: Implement the Pumpkin Patch design tokens

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want the frame and dread-scale color/spacing/shape tokens defined once,
so that every surface is consistent and tier color is used correctly.

## Acceptance Criteria

(Sourced verbatim-in-intent from `docs/epics.md#Story-6.1`, lines 846-862; UX source UX-DR1 / UX-DR2 / UX-DR9 in `docs/epics.md` lines 94-95, 102.)

**AC-1 (Frame palette — UX-DR1).**
**Given** the design token set
**When** tokens are implemented
**Then** the frame palette exists as named tokens with these EXACT hex values:
- background `#2E1A47`
- surface `#3D2461`
- primary-accent / candy-teal `#2BD9C4`
- secondary-accent / pumpkin-orange `#FF7A1A`
- text-primary `#FFF6E9`
- text-muted `#B9A6C9`
- danger / SLAY-red `#FF2E4D`
- credit-gold `#FFC73A`

**AC-2 (Spacing, radii, elevation — UX-DR1 / UX-DR9).**
**Then** spacing steps (4 / 8 / 12 / 16 / 24 / 32), corner radii (sm 10 / md 18 / lg 28 / pill 999), and the hard-shadow elevation device (offset `6, 6`, color `#000`, **blur = 0**) are tokenized as named constants.

**AC-3 (Dread-scale tier tokens — UX-DR2).**
**Given** the five dread-scale tier tokens
**When** they are defined
**Then** they exist keyed to the five `Rarity` tiers with these EXACT values:
- T1 common `#7DE0C4`
- T2 uncommon `#4FA8C9`
- T3 rare `#6A5AC9`
- T4 epic `#4B2E6B`
- T5 nightmare — a **two-stop bruise-rot gradient** `#3A4C3A` → `#5C2A4A`

**AC-4 (Tier-token containment / correct usage — UX-DR2).**
**Then** the dread-scale tier tokens are a SEPARATE set from the frame palette (a tier color is not also exposed as a frame-palette role), so a consumer cannot accidentally paint chrome with a tier color
**And** there is a single resolver `DreadScaleTokens.ForTier(Rarity)` so consumers map a Monster's tier → its token in ONE place (the AR-encounter treatment + the Codex rarity badge / "caught" stamp — never a visible rarity bar/strip/legend in chrome).
**And** credit-gold is a distinct named token used only for currency, and SLAY-red is a distinct named token used only for Slay/destructive (the tokens are named so the "only for X" discipline is checkable; the enforcement that *consumers* honor it is a 6.2 component concern, but the tokens must not be aliased to a general role).

## Tasks / Subtasks

- [x] **Task 1 — Frame palette tokens (AC-1).**
  - [x] Create `Assets/Veilwalkers/UI/Design/PumpkinPatchTokens.cs` (new `Design` subfolder under `Veilwalkers.UI`; namespace `Veilwalkers.UI` — matches the flat-namespace convention of `MaterializationAmbiance.cs` / `MaterializationVariant.cs`, which live in subfolders but keep the `Veilwalkers.UI` namespace).
  - [x] Define the 8 frame-palette colors. Store each as a `public const string` canonical hex (e.g. `BackgroundHex = "#2E1A47"`) — the hex string is the copy-pasteable source of truth, auditable line-by-line against UX-DR1 — AND expose a typed `Color32` accessor per token (a `static readonly Color32 Background = Hex.ToColor32(BackgroundHex);` or a `Color Background => ...` property). Rationale: a `const string` cannot hold a `Color32`, but the test must assert the exact RGBA, so provide both. `UnityEngine.Color32`/`Color` are available in headless EditMode (they are plain structs, no rendering).
  - [x] Provide a private hex→`Color32` helper (parse `#RRGGBB` → opaque `Color32(r,g,b,255)`). Do NOT call `UnityEngine.ColorUtility.TryParseHtmlString` if it proves unavailable/awkward headless — a hand-rolled 2-hex-digit parser is trivial and deterministic. Prefer the hand-rolled parser to avoid any headless surprise; pin it with a unit test (`"#FF7A1A"` → `(255,122,26,255)`).

- [x] **Task 2 — Spacing / radii / elevation tokens (AC-2).**
  - [x] Add a spacing scale: `public const int Space4 = 4; Space8 = 8; Space12 = 12; Space16 = 16; Space24 = 24; Space32 = 32;` (or an immutable ordered array `Spacing = {4,8,12,16,24,32}` + named consts — provide BOTH the named consts for call sites and assert the ordered set in a test). Units are dp (Android density-independent pixels; UX-DR18 "16dp margins"); document the unit in the XML doc.
  - [x] Add corner radii: `RadiusSm = 10`, `RadiusMd = 18`, `RadiusLg = 28`, `RadiusPill = 999` (dp; `999` is the "fully rounded pill" sentinel per UX-DR1/DR4).
  - [x] Add the hard-shadow elevation token as a small `readonly struct HardShadow` (or named consts): `ShadowOffsetX = 6`, `ShadowOffsetY = 6`, `ShadowBlur = 0`, and `ShadowColorHex = "#000000"` / a `Color32 ShadowColor` accessor. The **blur = 0** is the load-bearing invariant (UX-DR9: "no blur" is THE elevation device); pin `ShadowBlur == 0` explicitly.

- [x] **Task 3 — Dread-scale tier tokens + resolver (AC-3, AC-4).**
  - [x] Create `Assets/Veilwalkers/UI/Design/DreadScaleTokens.cs` (namespace `Veilwalkers.UI`). Define the 5 tier colors as hex consts + `Color32` accessors, named by tier (`Tier1CommonHex` … `Tier4EpicHex`), and the T5 nightmare as a TWO-stop gradient — expose `Tier5NightmareStartHex = "#3A4C3A"` and `Tier5NightmareEndHex = "#5C2A4A"` + a small `readonly struct TierGradient { Color32 Start; Color32 End; }` (T1–T4 are single-stop: `Start == End`; T5 is the only true gradient). A uniform `TierGradient ForTier(Rarity)` return type keeps the resolver total and the consumer branch-free.
  - [x] Implement `public static TierGradient ForTier(Rarity rarity)` — the SINGLE tier→token map. Use an explicit `switch` over the 5 `Rarity` members. For an out-of-range/undefined `Rarity` (defensive — callers always pass a real tier), degrade to the calmest tier (T1 common) rather than throw, matching the `MaterializationPresenter` `default:`-to-calmest precedent (deferred-work.md 4.7 "graceful boundary" entry). Document this at the `default` arm.
  - [x] Keep `Rarity` (in `Veilwalkers.Monsters`) the key — `Veilwalkers.UI` already references `Veilwalkers.Monsters` (asmdef confirmed), so NO asmdef edge / NO AcyclicDependencyTests matrix edit is needed. Do NOT reorder or re-key off anything but `Rarity` (it is append-only; the ascending order Common<Uncommon<Rare<Epic<Nightmare maps T1<T2<T3<T4<T5).

- [x] **Task 4 — Tests (`Veilwalkers.UI.Tests`).**
  - [x] Add `Assets/Tests/EditMode/UI.Tests/PumpkinPatchTokensTests.cs` and `DreadScaleTokensTests.cs` (the `Veilwalkers.UI.Tests` asmdef already references `Veilwalkers.UI` + `Veilwalkers.Monsters` — confirmed; no asmdef edit).
  - [x] Pin EVERY frame-palette token's exact `Color32` RGBA (8 assertions — the values are the contract, not an implementation detail; a typo'd hex is the exact disaster this story must make impossible).
  - [x] Pin the hex→`Color32` parser (`"#FF7A1A"` → `(255,122,26,255)`; alpha always 255).
  - [x] Pin spacing set `{4,8,12,16,24,32}` (ordered, ascending, no dupes), radii (`10/18/28/999`), elevation (`offset 6,6`, **`blur == 0`**, color black).
  - [x] Pin each tier token's exact `Color32` (T1–T4 single-stop start==end; T5 start `#3A4C3A` ≠ end `#5C2A4A`).
  - [x] Pin `ForTier`: each `Rarity` → its expected gradient; ascending-tier monotonic mapping is total over all 5 members; an out-of-range cast `(Rarity)99` → T1 (graceful, no throw).
  - [x] Pin AC-4 containment: assert credit-gold (`#FFC73A`) and SLAY-red (`#FF2E4D`) are NOT equal to any tier token and NOT equal to any other frame-palette token (they are distinct named roles); assert no tier token value appears in the frame-palette token set (the "tier tokens are a separate set" invariant — a structural guard against a consumer reaching a tier color through a frame role).

## Dev Notes

### What this story is (and is NOT)

This is the FIRST Epic-6 story — the design-system foundation that the entire MVP deferred its "pixels" to. It defines **tokens only**: named color/spacing/shape/elevation constants + the tier→token resolver. It builds **no components** (6.2), **no surfaces/navigation** (6.3), **no rendering**. There is no MonoBehaviour, no scene, no view in this story — it is a pure data/constants module, fully EditMode-testable headless (the `unity-headless-testing` memory applies).

### Why now — the deferral chain this UNBLOCKS (do not re-invent; these are the consumers)

Multiple prior stories explicitly deferred their color/tier rendering to "Epic 6 tokens." This story SUPPLIES those tokens. The most direct seam:
- **Story 4.7 `MaterializationAmbiance`** (`Assets/Veilwalkers/UI/Encounter/MaterializationAmbiance.cs`) carries an `AmbianceIntensity` enum and a `SlayGlow` bool and DELIBERATELY carries NO `Color` — its doc says *"those are Epic-6 tokens resolved from `Intensity` at render time."* 6.1's `DreadScaleTokens.ForTier(Rarity)` is exactly that resolver. (6.1 only DEFINES the tokens + resolver; the actual AR tint/lighting/vignette RENDER that consumes them stays Epic 6 / 6.3 — do not build render here.)
- **Story 2.x Codex** (`CodexSlot.cs`, `CodexDetailViewModel.cs`) carries NO `Color` — "art and tier color are Epic 6." The Codex rarity-tier badge (UX-DR7) + "caught" stamp will consume `ForTier`. (6.1 defines the token; the badge/stamp component is 6.2.)

Do NOT touch those files — they consume tokens later. 6.1 just makes the tokens exist.

### Relevant architecture patterns & constraints

- **Tier placement (`Veilwalkers.UI`, top of the graph).** Tokens live in `Veilwalkers.UI` — the highest tier in the one-way graph `Core ← {Persistence, Economy, Monsters} ← {AR, Encounter, Billing} ← App ← UI` (AR-5). No service/lower tier needs tokens (services are headless logic; only views render). `Veilwalkers.UI` already references `Veilwalkers.Monsters` (for `Rarity`) — verified in `Assets/Veilwalkers/UI/Veilwalkers.UI.asmdef`. **No new asmdef reference, no `AcyclicDependencyTests` matrix edit** (contrast 5.2's Billing→Persistence edge — not needed here).
- **No service-locator, no Bootstrap registration.** Tokens are `static` constants — there is nothing to register in `GameServices`/Bootstrap (the `MaterializationPresenter` dependency-free precedent: 4.7 added no Bootstrap wiring because the presenter took a `Rarity`, not a service; 6.1 is even simpler — pure constants).
- **Append-only `Rarity` key.** `ForTier` switches over the 5 `Rarity` members (`Veilwalkers.Monsters/Rarity.cs`): Common=0 < Uncommon=1 < Rare=2 < Epic=3 < Nightmare=4. Never reorder. A future 6th tier appends + extends the switch (the standard append-only discipline; the 4.7 `AscendingTiers` precedent).
- **`GameLog`, not `Debug.Log`** (AR-19) — though a constants module should need no logging. If the `default` arm of `ForTier` ever logs an unexpected tier, use `GameLog.Warn`.
- **Hex as source of truth.** Store the canonical `#RRGGBB` string as a `const` next to each typed accessor. Reason: the hex is what a designer/reviewer audits against UX-DR1 at a glance; the `Color32` is the runtime form. Both are pinned by tests so they can never drift apart.

### Testing standards summary

- EditMode NUnit tests in `Veilwalkers.UI.Tests` (asmdef already references `Veilwalkers.UI` + `Veilwalkers.Monsters` + `Veilwalkers.Core` + `Veilwalkers.Persistence`; `UNITY_INCLUDE_TESTS` constraint; `nunit.framework.dll`). No new test asmdef.
- Run headless per the `unity-headless-testing` memory:
  ```
  "/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode \
    -projectPath "C:/Users/james/Desktop/Veilwalkers" -runTests -testPlatform EditMode \
    -testResults "<abs>/test-results.xml" -logFile "<abs>/editor-test.log" -nographics
  ```
  Editor MUST be fully closed (`Temp/UnityLockfile` absent). NEVER trust exit 0 — confirm `test-results.xml` has `result="Passed"` AND grep the log for `error CS`. Delete `test-results.xml` + `editor-test.log` before committing.
- **Anti-tautology discipline** (the `tautological-test-trap` memory): the value-pinning tests ARE the contract here (a hardcoded hex matching a hardcoded `Color32` is the point — it locks the two representations together). The non-tautological assertions are: the parser test (input ≠ output form), the containment test (AC-4 — tier set disjoint from palette set), and the `ForTier` totality/graceful-degrade test. Make sure the containment test would FAIL if someone aliased a tier color into a frame-palette token.
- **`Color32` equality in tests — assert per-channel.** `UnityEngine.Color32` is a struct whose default `Equals`/`==` is reference/boxing-fragile under NUnit `Assert.AreEqual`. Assert the four channels explicitly (`Assert.AreEqual(255, c.r); Assert.AreEqual(122, c.g); …`) or compare a `(r,g,b,a)` tuple — do NOT `Assert.AreEqual(expectedColor32, actualColor32)` directly. Alpha is ALWAYS 255 (opaque) for every palette/tier token (the `#RRGGBB` parser hardcodes a=255). For the containment/disjointness checks (AC-4), compare the packed `int` `(r<<24|g<<16|b<<8|a)` or the canonical hex string — a clean value comparison that sidesteps the `Color32` equality trap.

### Project Structure Notes

- New files (all NEW, no UPDATE — greenfield):
  - `Assets/Veilwalkers/UI/Design/PumpkinPatchTokens.cs`
  - `Assets/Veilwalkers/UI/Design/DreadScaleTokens.cs`
  - (optional) `Assets/Veilwalkers/UI/Design/Hex.cs` (the hex→Color32 helper, if not nested privately)
  - `Assets/Tests/EditMode/UI.Tests/PumpkinPatchTokensTests.cs`
  - `Assets/Tests/EditMode/UI.Tests/DreadScaleTokensTests.cs`
- No `.meta` hand-authoring (Unity generates them; they are committed by the normal flow).
- No conflicts with existing structure — `Veilwalkers.UI/Encounter/` and `Veilwalkers.UI/Codex/` already establish the subfolder-with-flat-namespace pattern; `Design/` follows it.

### Scope boundaries (do NOT do these — they are later stories)

- ❌ Chunky button / credit pill / pack card / Codex slot / rarity badge / wordmark **components** → Story 6.2.
- ❌ Any MonoBehaviour, scene, prefab, or rendering → 6.2 / 6.3.
- ❌ Wiring tokens into `MaterializationView` / Codex views → Epic 6 render layer (6.3).
- ❌ Reduced-motion / accessibility setting → Story 6.6.
- ❌ Enforcing "credit-gold only for currency" at consumer call sites → 6.2 components (6.1 only makes the tokens distinct & named so the rule is checkable).

### References

- [Source: docs/epics.md#Story-6.1] (lines 846-862) — the AC.
- [Source: docs/epics.md] UX-DR1 (line 94, frame palette), UX-DR2 (line 95, dread-scale tier tokens), UX-DR9 (line 102, hard-shadow elevation), UX-DR7 (line 100, rarity-tier badge — the one chrome place tier color lives), UX-DR18 (line 111, 16dp margins / spacing context).
- [Source: Assets/Veilwalkers/Monsters/Rarity.cs] — the ordered, append-only `Rarity` enum the tier tokens key off.
- [Source: Assets/Veilwalkers/UI/Veilwalkers.UI.asmdef] — confirms `Veilwalkers.Monsters` reference already present (no asmdef edit).
- [Source: Assets/Veilwalkers/UI/Encounter/MaterializationAmbiance.cs] — the 4.7 seam that defers tier `Color` to "Epic-6 tokens resolved from Intensity"; `ForTier` is that resolver.
- [Source: Assets/Tests/EditMode/UI.Tests/Veilwalkers.UI.Tests.asmdef] — confirms the test assembly references UI + Monsters (no test asmdef edit).
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — the "→ Epic 6" token-consumer deferral chain (4.6 Nightveil filter VFX, 4.7 materialization render/ambiance, 2.x Codex tier color) this story's tokens unblock.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (story-automator ungated cycle, 2026-06-16)

### Debug Log References

- Headless EditMode gate: `487/487 passed, 0 failed, 0 skipped`, `result="Passed"`, 0 `error CS` in `editor-test.log` (+24 over the 463 5.4 baseline). Run command per `unity-headless-testing` memory; `test-results.xml` + `editor-test.log` deleted before commit.
- One compile-fix during dev: `DreadScaleTokens.cs` `GameLog` (`Veilwalkers.Core`) needed a `using Veilwalkers.Core;` (caught by the headless gate's first run, exit 1 + `error CS0103`; fixed; re-run green).

### Completion Notes List

- **Task 1 (frame palette, AC-1):** `PumpkinPatchTokens.cs` — 8 frame colors as `const string` canonical `#RRGGBB` hex + `Color32` accessors, parsed by a hand-rolled `Hex.ToColor32` (`Hex.cs`, internal). Alpha always 255.
- **Task 2 (spacing/radii/elevation, AC-2):** spacing `4/8/12/16/24/32` (named consts + ordered `Spacing` array), radii `sm10/md18/lg28/pill999`, hard-shadow `offset 6,6` + `ShadowBlur=0` (load-bearing) + black `ShadowColor`.
- **Task 3 (tier tokens + resolver, AC-3/AC-4):** `DreadScaleTokens.cs` — 5 tier tokens keyed off `Rarity`; T1–T4 single-stop, T5 two-stop bruise-rot gradient (`#3A4C3A → #5C2A4A`) via `TierGradient`; `ForTier(Rarity)` is the single total map, degrades an out-of-range tier to T1 (logs `GameLog.Warn`, NFR-3 graceful boundary). `TierGradient` is a separate type from the frame palette (AC-4 containment).
- **Task 4 (tests):** `PumpkinPatchTokensTests.cs` (17) + `DreadScaleTokensTests.cs` (9). Per-channel `Color32` assertions (sidesteps the struct-equality trap); non-tautological: hex-parser nibble coverage, `ForTier` totality (5 distinct opaque tiers) + graceful-degrade (`LogAssert.Expect` on the warning), AC-4 disjointness (tier set ∩ frame palette = ∅; credit-gold ≠ SLAY-red ≠ any tier).
- **No asmdef/Bootstrap/schema change.** `Veilwalkers.UI` already references `Veilwalkers.Monsters` + `Veilwalkers.Core`; tokens are `static` constants (nothing to register). No `AcyclicDependencyTests` edit. Confirms the story's "no new edge" claim.
- **Scope held:** tokens only — no components (6.2), no rendering/scenes (6.3), no consumer-file edits (4.7 `MaterializationAmbiance` / Codex untouched; they consume `ForTier` later).

### File List

- `Assets/Veilwalkers/UI/Design/Hex.cs` (NEW)
- `Assets/Veilwalkers/UI/Design/PumpkinPatchTokens.cs` (NEW)
- `Assets/Veilwalkers/UI/Design/DreadScaleTokens.cs` (NEW)
- `Assets/Tests/EditMode/UI.Tests/PumpkinPatchTokensTests.cs` (NEW)
- `Assets/Tests/EditMode/UI.Tests/DreadScaleTokensTests.cs` (NEW)

### Review Findings

Adversarial 3-layer CR (Blind Hunter / Edge Case Hunter / Acceptance Auditor) + per-finding verifier: **10 raw → 10 deduped (0 cross-layer convergence) → 1 confirmed (PATCHED), 1 deferred, 6 spec-sanctioned, 2 false-positive. 0 AC violations.** 487/487 EditMode green re-verified after patching (unchanged count — the patch was a test-comment/assertion correctness fix, no new test).

- [x] [Review][Patch] **`ForTier_is_total` test comment misstated its guard mechanism** (`DreadScaleTokensTests.cs`). The comment claimed `Assert.AreEqual(5, packed.Count)` "catches a future tier added without a ForTier arm (it would collide with T1's degrade value)" — self-defeating, because a collision KEEPS the distinct count at 5, so the assertion would still PASS. Fixed: renamed to `ForTier_maps_every_defined_rarity_to_a_distinct_opaque_token`, tied the assertion to `Enum.GetValues(Rarity).Length` (so it genuinely catches a DUPLICATE-mapping regression), and documented that the missing-ARM case is guarded by the sibling `ForTier_degrades_an_undefined_rarity_to_T1_without_throwing` (the `GameLog.Warn` via `LogAssert.Expect` — a real member never warns). [Blind Hunter]
- [x] [Review][Defer] **`Spacing` doc-comment claims immutability but `public static readonly int[]` is element-mutable** (`PumpkinPatchTokens.cs`). `readonly` freezes the reference, not contents — a consumer could write `Spacing[0] = 99`. Real quality nit but NOT an AC-2 violation (AC-2 asks for "named constants," satisfied). Deferred to a 6.2+ consumer-discipline / token-hardening pass (expose `IReadOnlyList<int>` or return a copy). Owner: Story 6.2+. → deferred-work.md.
- [x] [Review][Sanctioned] **`Hex.Packed` returns a signed `int` (high bit sets sign for channels ≥ 0x80).** Story Dev Notes line 111 explicitly mandates the `(r<<24|g<<16|b<<8|a)` signed-int formula for the AC-4 containment compares; equality/HashSet ops are sign-consistent on both sides, so correct as specified. [Blind Hunter]
- [x] [Review][Sanctioned] **Typed color accessors re-parse the hex on every access** (×2 — Blind Hunter + Edge Case Hunter). Task 1 explicitly sanctions BOTH the `static readonly` cached form AND the `=> Hex.ToColor32(...)` property form; the property form was chosen for deterministic headless behavior. Perf caching is a 6.2+ consumer-path concern. [Blind Hunter, Edge Case Hunter]
- [x] [Review][Sanctioned] **`ForTier` undefined-cast test covers `(Rarity)99` but not negative casts.** Task 4 mandates exactly the `(Rarity)99 → T1` example; the `default` arm handles all undefined values identically. [Edge Case Hunter]
- [x] [Review][Sanctioned] **`Hex.ToColor32` hardcodes alpha=255 with no runtime assert.** Dev Notes line 111 makes opaque-alpha intentional; pinned by `Every_palette_token_is_fully_opaque` + `Every_tier_token_is_fully_opaque` (tests guard the invariant, not a runtime assert — a pure-constants module). [Edge Case Hunter]
- [x] [Review][Sanctioned] **`AllTierStops` hardcodes 6 stops assuming T5 is the only gradient.** AC-3 mandates exactly that; `Tier5_nightmare_is_the_bruise_rot_gradient` (`IsSolid==false`) + `Tier1_through_Tier4_are_single_stop` (`IsSolid==true`) pin the invariant a future gradient-tier would have to update. [Edge Case Hunter]
- [x] [Review][False-positive] **`TierGradient` allows default construction with no invariant guard.** Task 3 explicitly requires a plain `readonly struct TierGradient { Color32 Start; Color32 End; }` (uniform/branch-free return); all 6.1 instantiation is internal to `DreadScaleTokens`; the no-invalid-construction discipline is a 6.2+ consumer concern. No 6.1 public surface produces an invalid instance. [Edge Case Hunter]
- [x] [Review][False-positive] **`Hex` parser doesn't distinguish non-ASCII from invalid-hex in its error.** Inputs are ONLY compile-time const literals; throwing loudly on any malformed literal is the documented intent (fail-at-test-time). Message specificity is a non-defect nit. [Edge Case Hunter]
