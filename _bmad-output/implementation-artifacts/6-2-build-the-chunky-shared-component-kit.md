---
baseline_commit: 643508d
---

# Story 6.2: Build the chunky shared component kit

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want the reusable chunky components,
so that feature epics consume one consistent component set instead of re-building UI.

## Acceptance Criteria

(Sourced verbatim-in-intent from `docs/epics.md#Story-6.2`, lines 864-875; UX source UX-DR3 / UX-DR4 / UX-DR5 / UX-DR6 / UX-DR7 / UX-DR8 / UX-DR9 in `docs/epics.md` lines 96-102.)

**AC-1 (The kit is complete — UX-DR3/DR4/DR5/DR6/DR7/DR8).**
**Given** the component kit
**When** components are built
**Then** the kit includes ALL of:
- **Chunky button** (UX-DR3): `md`/`lg` rounded, 4–5px black outline, hard offset shadow, ALL-CAPS label; **primary** = pumpkin-orange fill, **secondary** = surface fill + outline; **pressed** state shrinks the shadow offset (e.g. `6,6` → `2,2`). The **SLAY variant** = SLAY-red fill + a skull/slash icon + the literal label "SLAY" (red is NEVER the sole signifier — always + icon + label, UX-DR17).
- **Credit pill** (UX-DR4): gold, outlined, a ⬡ hex glyph + an integer count; positioned top-right, tappable → Shop; a near-empty "felt descent" pulse (candy-teal → dim teal); the **world never locks at zero** (the pill stays tappable, no spend-block in the component).
- **Pack card** (UX-DR6): sticker-style `lg` card, heavy outline + hard shadow; base + bonus credits (**bonus in pumpkin-orange**), a localized "Buy" affordance (the Play price string is bound at runtime, not in the component), soft-nudge tags only ("POPULAR" / "BEST VALUE"); transparent contents (no gacha/mystery-box).
- **Codex slot** (UX-DR5): chunky outlined square with the **3 states** Discovered / `???` silhouette / `?` blank (the same three `CodexSlotState` Story 2.4 defined — the component renders state, it does not re-classify).
- **Rarity-tier badge** (UX-DR7): a small `sm` outlined badge color-coded to the dread-scale token for a `Rarity` (T1…T5) — the one place tier color lives in chrome.
- **Wordmark** (UX-DR8): the chunky outlined "Veilwalkers" display lockup (with personality / a slight wobble), a brand anchor for Home + onboarding; never used for body text.

**AC-2 (The hard-shadow elevation device is universal — UX-DR9).**
**Then** every card / button / badge / pill in the kit carries the black outline + hard offset shadow as its elevation device
**And** NO component declares a blurred / soft shadow or a gradient glow on chrome (glow lives only in the AR encounter + the SLAY-button tier-glow — neither of which is built here). The blur-is-zero invariant comes from `PumpkinPatchTokens.ShadowBlur` (Story 6.1), and every component must source its outline/shadow/colors from the 6.1 tokens — never a re-declared hex.

## Tasks / Subtasks

> **Architecture decision (READ FIRST — the altitude split).** Story 6.2 is a **component kit**, not a wired scene (scene placement + live data binding is Story 6.3, the `AppStateMachine`). Following the established **UI-presenter pattern** (memory `ui-presenter-pattern`; the `MaterializationPlan` + `MaterializationView` precedent from 4.7, and `CodexSlot` + `CodexGridView` from 2.4), each component is delivered as TWO parts:
> 1. A **pure-logic, headless-testable descriptor** — a `readonly struct` (a "style/spec" view-model) that captures the chunky-style DECISION for that component: which token fill/outline/shadow/label/icon/pressed-offset it uses, sourced ENTIRELY from 6.1 tokens. This is where the AC lives and where the tests pin it. No `MonoBehaviour`, no rendering.
> 2. A **thin, logic-free `MonoBehaviour` view stub** — constructs/holds the descriptor, exposes the Epic-6.3 wire point, and stubs `Render` to a `GameLog.Info` (the `MaterializationView.Render` precedent), with a `TODO(Story 6.3)` for the real widget binding (UGUI/sprite/9-slice). The view holds NO decision.
>
> This keeps 6.2 **fully EditMode-testable headless** (no scene, no play-mode) while delivering "the reusable components" the AC asks for: a feature epic consumes the descriptor (the single source of chunky-style truth) + the view stub, not a re-built hex/layout. **Do NOT** build real UGUI prefabs, sprites, 9-slice art, fonts, or scenes — those are 6.3 / asset-authoring. **Do NOT** wire any component to a live service or scene.

- [x] **Task 1 — The shared chunky-style primitive (AC-2).** *(The elevation device + outline, sourced once.)*
  - [x] Create `Assets/Veilwalkers/UI/Components/` (new subfolder under `Veilwalkers.UI`; namespace stays flat `Veilwalkers.UI`, matching `UI/Encounter/` + `UI/Codex/` + `UI/Design/`).
  - [x] Add `ChunkyStyle.cs` — a `readonly struct ChunkyStyle` capturing the shared chrome elevation contract every component reuses: `Color32 Fill`, `Color32 OutlineColor` (black), `int OutlineWidth` (the 4–5px outline; pick `4` as the canon default const `OutlineWidthDefault`, document the 4–5 band), `int ShadowOffsetX/Y` (from `PumpkinPatchTokens.ShadowOffsetX/Y`), `int ShadowBlur` (from `PumpkinPatchTokens.ShadowBlur` — ALWAYS 0), `Color32 ShadowColor` (from tokens), `int CornerRadius`. Provide a `Pressed()` method/property that returns a copy with the shadow offset shrunk to the pressed offset (a new const `PressedShadowOffset = 2`, UX-DR3 "e.g. 2px 2px 0"). Expose `bool HasHardShadow => ShadowBlur == 0` so AC-2 is a one-line assert per component.
  - [x] Every other component's descriptor (Tasks 2–7) EMBEDS or BUILDS a `ChunkyStyle` from 6.1 tokens — so AC-2 ("every card/button/badge/pill carries the outline + hard shadow, no blur") is structurally guaranteed and pinned ONCE on the primitive plus once per component.

- [x] **Task 2 — Chunky button + SLAY variant (AC-1 button, UX-DR3/DR17).**
  - [x] Add `ChunkyButtonStyle.cs` — a `readonly struct` + an enum `ChunkyButtonKind { Primary, Secondary, Slay }`. A static factory `For(ChunkyButtonKind, string label)` (or per-kind statics) returns the descriptor with: `Primary` → `ChunkyStyle.Fill = PumpkinPatchTokens.SecondaryAccent` (pumpkin-orange), `RadiusMd`; `Secondary` → `Fill = PumpkinPatchTokens.Surface` + outline (the "surface + outline" secondary); `Slay` → `Fill = PumpkinPatchTokens.SlayRed`, plus `RequiresIcon = true` and a fixed label "SLAY".
  - [x] ALL-CAPS: the descriptor stores the label already upper-cased (`label.ToUpperInvariant()`), OR exposes `DisplayLabel => _label?.ToUpperInvariant() ?? string.Empty` (null-safe getter, the `CreditPack.PackId`/`MaterializationPlan.MonsterId` precedent). Pin that "primary"→"PRIMARY".
  - [x] **UX-DR17 (red is never alone):** the `Slay` kind MUST carry `RequiresIcon == true` (skull/slash) AND the literal label "SLAY" — a test asserts a Slay button is never red-only (icon flag true AND label == "SLAY"). The non-Slay kinds carry `RequiresIcon == false`.
  - [x] **Pressed state:** expose the pressed descriptor via `ChunkyStyle.Pressed()` — a test asserts the pressed shadow offset (`2,2`) is strictly smaller than the resting offset (`6,6`) for every kind (the inequality is the AC, the `4.x` "AC is the inequality not the value" precedent — a 6.3 feel-pass may retune the exact pressed offset, the test asserts `pressed < resting`).

- [x] **Task 3 — Credit pill (AC-1 pill, UX-DR4).**
  - [x] Add `CreditPillStyle.cs` — a `readonly struct` carrying: `ChunkyStyle` built from `PumpkinPatchTokens.CreditGold` (gold fill) at `RadiusPill`; a `Glyph` const string for the ⬡ hex marker — the canonical glyph is `"⬡"` (U+2B21 WHITE HEXAGON, the exact glyph every UX source uses: `"⬡ 20"`); keep it a const so the renderer binds it (a test pins `Glyph == "⬡"`); a `bool NearEmpty` input + a `PulseColor` that resolves candy-teal (`PrimaryAccent`) when not near-empty and a "dim teal" when near-empty (the "felt descent" pulse). Define "near-empty" as a presenter INPUT bool (the threshold is a 6.3/balancing decision — the component only renders the pulse state it is told), with a documented default helper.
  - [x] **World-never-locks invariant (UX-DR4):** the pill component carries NO spend/lock logic — it is display-only (a count + a tap-to-Shop intent flag). Document + pin that the descriptor has no "disabled"/"locked" state that would gate the world at zero credits; a zero count is a valid renderable state (the pill shows `0`, stays tappable). The actual "Lure dimmed with real cost shown" lives in the encounter action bar (Epic 4 already), NOT here.
  - [x] Tappable-→-Shop is a declared intent on the descriptor (`bool TapToShop => true` / a `CreditPillIntent` marker) — the actual navigation is `AppStateMachine` (6.3); 6.2 only declares the affordance exists.

- [x] **Task 4 — Pack card (AC-1 pack card, UX-DR6).**
  - [x] Add `PackCardStyle.cs` — a `readonly struct` that takes a `Veilwalkers.Billing.CreditPack` (the existing 5.1 catalog type — `Veilwalkers.UI` already references `Veilwalkers.Billing`, asmdef confirmed) and derives the sticker-card descriptor: `ChunkyStyle` at `RadiusLg` (the `lg` sticker), `BaseCredits`/`BonusCredits` surfaced for the view, a `BonusColor = PumpkinPatchTokens.SecondaryAccent` (bonus in pumpkin-orange — pin this), and the badge text mapped from `CreditPack.Badge` (`PackBadge.Popular`→"POPULAR", `BestValue`→"BEST VALUE", `None`→no tag). The localized **price string is NOT stored** (it is bound from Play at runtime, the 5.1 `FallbackPriceUsd`-is-reference-only contract) — the descriptor exposes a `HasLocalizedPriceSlot`/a `Buy` affordance flag, not a price value.
  - [x] **Transparency (no gacha):** the card surfaces every grant as a declared field off `CreditPack` (base + bonus + the Guaranteed-Rare flag) — pin that a `CreditPack.IncludesGuaranteedRareLure == true` pack (Veil) exposes that line, and that there is no hidden/random reward field (the no-gacha canon; the descriptor only reflects `CreditPack`'s transparent fields).
  - [x] `Veilwalkers.UI.Tests.asmdef` does NOT currently reference `Veilwalkers.Billing` (confirmed). Add the `"Veilwalkers.Billing"` reference to the TEST asmdef so the pack-card descriptor test can construct a `CreditPackCatalog`/`CreditPack`. This is a TEST-only asmdef edge (`.Tests` assemblies are excluded from the `AcyclicDependencyTests` matrix — the 5.4 `Encounter.Tests → Billing` precedent); NO production asmdef edit (UI→Billing already exists).

- [x] **Task 5 — Codex slot (AC-1 slot, UX-DR5).**
  - [x] Add `CodexSlotStyle.cs` — a `readonly struct` that takes the existing `CodexSlotState` (Story 2.4; `Discovered` / `Silhouette` / `Blank`) + an optional `Rarity?` tier and returns the chunky-square descriptor: a `ChunkyStyle` (outlined square, `RadiusMd`); and a per-state treatment — `Discovered` carries a "caught"-stamp flag + the tier tint resolved via `DreadScaleTokens.ForTier(tier)` (the ONLY tier-color-in-chrome use, alongside the badge — UX-DR2/DR7), `Silhouette` = `???` treatment, `Blank` = `?` treatment.
  - [x] **The component RENDERS state, it does not re-classify (critical):** it consumes the `CodexSlotState` the `CodexGridPresenter` (2.4) already computed — 6.2 must NOT re-derive the begun-tier / discovered logic (that is 2.4's presenter, and duplicating it is the exact "reinvent the wheel" trap). A test pins that each of the 3 states maps to a distinct treatment and that the tier tint is only resolved for a `Discovered` slot with a non-null tier (a `Blank`/unauthored slot has no tier color — `CodexSlot.Tier` can be null, the 2.4 contract).

- [x] **Task 6 — Rarity-tier badge (AC-1 badge, UX-DR7).**
  - [x] Add `RarityBadgeStyle.cs` — a `readonly struct` that takes a `Rarity` and returns the `sm` outlined badge descriptor: a `ChunkyStyle` at `RadiusSm`, with the badge color resolved via `DreadScaleTokens.ForTier(rarity)` (the tier token — the one chrome place tier color lives). For T5 (the gradient tier) the badge carries the full `TierGradient` (Start≠End); T1–T4 are solid (Start==End) — surface the `TierGradient` so the renderer can paint the T5 bruise-rot ramp.
  - [x] Pin: each `Rarity` → its `ForTier` token (delegates to 6.1's tested resolver — do NOT re-pin the exact hex, that is 6.1's test; pin that the badge USES `ForTier` and that T5 is the gradient one). Pin the badge radius is `RadiusSm` (it is the SMALL `sm` badge, AC-1).

- [x] **Task 7 — Wordmark (AC-1 wordmark, UX-DR8).**
  - [x] Add `WordmarkStyle.cs` — a `readonly struct` for the "Veilwalkers" display lockup: the literal wordmark text const (`"Veilwalkers"`), a `ChunkyStyle`-style outline + hard shadow (it is a chunky outlined lockup — AC-2 applies), a `WobbleDegrees` const for the "slight wobble" personality (a small value e.g. `3f`, documented as a feel value a 6.3 pass may tune), and a `bool BodyTextForbidden => true` marker documenting UX-DR8 "never used for body." Pin the text is exactly "Veilwalkers" and that it carries the hard shadow (AC-2).

- [x] **Task 8 — Settle the inherited 6.1 deferral: harden `PumpkinPatchTokens.Spacing` (CR-6.1 owner = "Story 6.2+").**
  - [x] The 6.1 CR deferred `PumpkinPatchTokens.Spacing` (`public static readonly int[]`) as element-mutable (`Spacing[0] = 99` corrupts the shared scale) to "Story 6.2+ (the chunky component kit) — the first consumer that iterates the scale." 6.2's components reference spacing, so harden it now: change the public surface to `IReadOnlyList<int> Spacing` (back it with a private `int[]` and expose `Array.AsReadOnly(...)` / a `ReadOnlyCollection<int>`), so a consumer cannot mutate the shared design-token scale. Keep the `SpaceNN` named consts unchanged. **Compile-breakage to fix in the SAME edit:** the existing 6.1 test `PumpkinPatchTokensTests.Spacing_scale_is_4_8_12_16_24_32_ascending_no_dupes` accesses `PumpkinPatchTokens.Spacing.Length` (×2, the ascending loop) — `IReadOnlyList<int>` has no `.Length`, so change those to `.Count` (the indexer `Spacing[i]` and `CollectionAssert.AreEqual(expected, Spacing)` both still compile against `IReadOnlyList<int>`). Any OTHER `Spacing.Length` call site across the codebase must likewise move to `.Count` — grep `Spacing.Length` before finishing. The values/order assertions stay green.
  - [x] Record this as SETTLED in the dev deferral notes (it closes the open 6.1 CR deferral).

- [x] **Task 9 — Thin view stubs (the Epic-6.3 wire points).**
  - [x] For the components that will become on-screen widgets, add a thin logic-free `MonoBehaviour` view stub mirroring `MaterializationView` — it holds the descriptor + stubs `Render` to a `GameLog.Info` + a `TODO(Story 6.3)`. To keep scope tight and avoid 7 near-identical stubs, add a SINGLE representative stub `ChunkyComponentView.cs` (or one per the two most-wired: a `CreditPillView` + a `ChunkyButtonView`) demonstrating the descriptor→render bind pattern, with a class-doc enumerating that 6.3 places each component's widget. **Decide during dev** (the lower-risk choice is ONE representative stub — the descriptors are the deliverable; the views are 6.3's binding surface). Do NOT add a view that holds logic or reads a service.
  - [x] No `GameServices`/Bootstrap registration (descriptors are pure structs; views are inert until 6.3 places them — the `MaterializationView` "wired into NO scene yet" precedent). NO Bootstrap edit.

- [x] **Task 10 — Tests (`Veilwalkers.UI.Tests`).**
  - [x] Add one test file per component under `Assets/Tests/EditMode/UI.Tests/` (e.g. `ChunkyButtonStyleTests.cs`, `CreditPillStyleTests.cs`, `PackCardStyleTests.cs`, `CodexSlotStyleTests.cs`, `RarityBadgeStyleTests.cs`, `WordmarkStyleTests.cs`, `ChunkyStyleTests.cs`). The `Veilwalkers.UI.Tests` asmdef already references UI + Monsters + Core + Persistence; ADD `Veilwalkers.Billing` (Task 4) for the pack-card test.
  - [x] **AC-2 universal pin:** a single parameterized/looping test asserts EVERY component descriptor's `ChunkyStyle` has `ShadowBlur == 0` AND a non-zero hard offset AND a black shadow color AND an outline width in the 4–5 band — the "no soft shadow anywhere in the kit" guard (the load-bearing AC-2 assertion; it must FAIL if any component is built with a blurred shadow).
  - [x] **AC-1 completeness pin:** a test that asserts all six component descriptor types exist and are constructible (the "the kit includes ALL of …" enumeration — a structural guard against a missing component).
  - [x] Per-component non-tautological pins (NOT just "the const equals the const"): button ALL-CAPS transform (`"primary"`→`"PRIMARY"`, input≠output form); the SLAY-never-alone invariant (Slay ⇒ icon flag true AND label "SLAY"); pressed offset strictly < resting offset; pack-card bonus is pumpkin-orange AND a Veil pack surfaces the Guaranteed-Rare line; credit pill has no locked/disabled state and renders a `0` count; Codex-slot 3 states map to 3 distinct treatments + tier tint only for Discovered-with-tier; rarity badge delegates to `ForTier` and T5 is the gradient; wordmark text is exactly "Veilwalkers".
  - [x] **Token-sourcing pin (AC-2 "never a re-declared hex"):** assert each component's fill EQUALS the expected `PumpkinPatchTokens`/`DreadScaleTokens` value (e.g. the SLAY button fill `==` `PumpkinPatchTokens.SlayRed`, the credit pill fill `==` `PumpkinPatchTokens.CreditGold`) — proving the component sources the token, not a copy. Use the per-channel `Color32` comparison (the 6.1 struct-equality-trap lesson — assert `r/g/b/a` or the packed `Hex.Packed` int, NOT `Assert.AreEqual(color32, color32)`).

## Dev Notes

### What this story is (and is NOT)

This is the SECOND Epic-6 story. 6.1 defined the **tokens** (colors/spacing/radii/elevation + the tier resolver); 6.2 builds the **chunky shared components** that CONSUME those tokens, so feature epics get "one consistent component set instead of re-building UI" (the AC's stated value). It delivers, per component, a **pure-logic style descriptor** (`readonly struct`, the single source of chunky-style truth, EditMode-tested headless) + a **thin logic-free view stub** (the Epic-6.3 wire point, render stubbed to a log). It builds **no real UGUI prefabs, sprites, fonts, 9-slice art, or scenes** (asset-authoring + scene placement = Story 6.3), and **wires nothing to a live service** (navigation/data binding = 6.3's `AppStateMachine`).

### Why this shape — the UI-presenter altitude split (do NOT deviate)

Memory `ui-presenter-pattern`: every Veilwalkers UI story = a pure-logic presenter/view-model (EditMode-tested) + a thin logic-free MonoBehaviour view; visual polish + scene wiring defers. The two precedents to copy EXACTLY:
- **`MaterializationPlan` (a `readonly struct` the presenter returns) + `MaterializationView` (a thin MonoBehaviour whose `Render` is a `GameLog.Info` stub with a `TODO(Epic 6)`).** 6.2's component descriptors ARE `MaterializationPlan`-shaped; the view stubs ARE `MaterializationView`-shaped. [Assets/Veilwalkers/UI/Encounter/MaterializationPlan.cs, MaterializationView.cs]
- **`CodexSlot` (a `readonly struct` view-model) + `CodexSlotState` (the 3-state enum) + `CodexGridView` (thin, logic-free).** 6.2's Codex-slot component CONSUMES `CodexSlotState` — it renders the state, it does NOT re-classify (the begun-tier logic is 2.4's `CodexGridPresenter`; re-deriving it is the "reinvent the wheel" trap the create-story role exists to prevent). [Assets/Veilwalkers/UI/Codex/CodexSlot.cs, CodexSlotState.cs, CodexGridView.cs]

Why descriptors-not-prefabs: a `.prefab`/`.unity` scene/sprite cannot be authored or asserted headless, and the AR-rig/Home scenes do not exist yet (they are 6.3). A `readonly struct` descriptor sourced from tokens IS the reusable component contract a feature epic consumes, and it is fully pinnable in EditMode (the entire MVP's "pixels deferred" chain has been logic-tested this way). The actual widget binding (UGUI Image/Text/9-slice, the sprite outline, the font) lands when 6.3 places the components in real scenes.

### Relevant architecture patterns & constraints

- **Tier placement (`Veilwalkers.UI`, top of the one-way graph).** Components live in `Veilwalkers.UI` (the highest tier, AR-5). The asmdef already references everything 6.2 needs: `Veilwalkers.Monsters` (Rarity), `Veilwalkers.Billing` (CreditPack), `Veilwalkers.Core` (GameLog). **NO production asmdef edit, NO `AcyclicDependencyTests` matrix edit.** The ONLY asmdef change is adding `Veilwalkers.Billing` to the `Veilwalkers.UI.Tests` asmdef (Task 4) — a TEST-only edge, excluded from the acyclicity matrix (the 5.4 `Encounter.Tests → Billing` precedent; memory `unity-asmdef-architecture-test` — `.Tests` suffix excludes it).
- **Source tokens, never re-declare a hex (AC-2).** Every fill/outline/shadow/radius/spacing value comes from `PumpkinPatchTokens` / `DreadScaleTokens` (6.1). A component that hard-codes `"#FF7A1A"` instead of `PumpkinPatchTokens.SecondaryAccent` is an AC-2 violation — the token-sourcing test (Task 10) guards this.
- **`readonly struct` value-type discipline (the defensive-read lesson).** Descriptors are `readonly struct` (the `CodexSlot`/`MaterializationPlan`/`CreditPack` precedent) — a descriptor handed to a view can never alias or corrupt anything. Null-safe getters for any string field (`DisplayLabel`, the wordmark text) so `default(T)` honors the contract (the `CreditPack.PackId ?? string.Empty` precedent).
- **No service-locator, no Bootstrap registration.** Descriptors are pure structs (nothing to register). View stubs are inert MonoBehaviours that read NO service (a `ChunkyButton`/`CreditPill` descriptor is dependency-free — unlike `CodexGridView` which reads `CodexService`, these components take their data as a value, so NO `GameServices` read, NO `Awake` guard — the `MaterializationView` precedent). NO Bootstrap edit.
- **`GameLog`, not `Debug.Log` (AR-19).** The view-stub `Render` logs via `GameLog.Info` (the `MaterializationView.Render` precedent).
- **Append-only `Rarity` key.** The Codex-slot + rarity-badge resolve tier via `DreadScaleTokens.ForTier(Rarity)` (6.1's tested total resolver) — do NOT re-implement the tier→color map (6.1 owns it; the badge/slot just CALL it). A future 6th tier extends 6.1's switch, not 6.2's components.

### The inherited deferral 6.2 settles (Task 8)

The 6.1 code review deferred ONE item explicitly to "Story 6.2+ (the chunky component kit) / a future design-token-hardening pass": `PumpkinPatchTokens.Spacing` is a `public static readonly int[]` whose ELEMENTS are mutable (`readonly` freezes the reference, not the contents), so `Spacing[0] = 99` would silently corrupt the shared scale process-wide. 6.2 is the first consumer that iterates the spacing scale, so it is the natural owner. Fix: expose `Spacing` as `IReadOnlyList<int>` (back it with a private array, return `Array.AsReadOnly`) so the shared token scale cannot be mutated through the public surface. The `SpaceNN` named consts are unchanged; the existing 6.1 ordered-set test stays green (assert via the `IReadOnlyList` indexer/enumeration). [deferred-work.md → "code review of 6-1" → the `Spacing` entry]

### Scope boundaries (do NOT do these — they are later stories)

- ❌ Real UGUI prefabs / sprites / 9-slice outline art / fonts / `.unity` scenes → Story 6.3 + asset-authoring.
- ❌ Wiring any component to a live service, scene, or navigation (`AppStateMachine`, tap-to-Shop navigation, the credit-pill balance binding, the Codex grid binding) → Story 6.3.
- ❌ The onboarding premise/disclosure/coin-burst cards → Story 6.4 (they consume this kit, but are not built here).
- ❌ Diegetic voice/tone microcopy + state treatments (veil-parting wipe, plane coaching) → Story 6.5.
- ❌ Reduced-motion / TalkBack / contrast / ≥48dp accessibility wiring → Story 6.6 (6.2 honors UX-DR17 "red never alone" at the SLAY-button DESCRIPTOR level — icon flag + label — because that is a component-shape decision, but the broader accessibility floor is 6.6).
- ❌ The animations themselves (the credit-pill pulse tween, the Codex flip, the wordmark wobble motion, the pressed-state transition) → 6.3 render / 6.5 / 6.6. 6.2 declares the DESCRIPTOR (pulse color, wobble degrees, pressed offset); the motion is rendered later.

### Testing standards summary

- EditMode NUnit tests in `Veilwalkers.UI.Tests` (asmdef references UI + Monsters + Core + Persistence; ADD `Veilwalkers.Billing` for the pack-card test — Task 4; `UNITY_INCLUDE_TESTS` constraint; `nunit.framework.dll`). No NEW test asmdef.
- Run headless per the `unity-headless-testing` memory:
  ```
  "/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode \
    -projectPath "C:/Users/james/Desktop/Veilwalkers" -runTests -testPlatform EditMode \
    -testResults "<abs>/test-results.xml" -logFile "<abs>/editor-test.log" -nographics
  ```
  Editor MUST be fully closed (`Temp/UnityLockfile` absent). NEVER trust exit 0 — confirm `test-results.xml` has `result="Passed"` AND grep the log for `error CS`. Delete `test-results.xml` + `editor-test.log` before committing.
- **Anti-tautology discipline** (memory `tautological-test-trap`): a test that asserts `descriptor.Fill == PumpkinPatchTokens.SecondaryAccent` is NON-tautological ONLY if the descriptor actually SOURCES the token (not a copy of the same hex) — that is the point (it locks the component to the token, so a future token change flows through). The genuinely non-tautological assertions are: the ALL-CAPS transform (input ≠ output), the SLAY-never-alone composite invariant, the pressed<resting inequality, the AC-2 universal "no blur anywhere" sweep, and the Codex-slot state→treatment distinctness. Make the AC-2 sweep test FAIL if any component declares a blurred shadow.
- **`Color32` equality — assert per-channel or via `Hex.Packed`.** `UnityEngine.Color32` default `Equals`/`==` is boxing-fragile under NUnit `Assert.AreEqual` (the 6.1 lesson). Assert the four channels, or compare `Hex.Packed(a) == Hex.Packed(b)` (the signed-int packed form 6.1 already uses for its containment checks). Do NOT `Assert.AreEqual(expectedColor32, actualColor32)`.

### Project Structure Notes

- New files (all NEW under a new `Components/` subfolder; namespace `Veilwalkers.UI`):
  - `Assets/Veilwalkers/UI/Components/ChunkyStyle.cs`
  - `Assets/Veilwalkers/UI/Components/ChunkyButtonStyle.cs`
  - `Assets/Veilwalkers/UI/Components/CreditPillStyle.cs`
  - `Assets/Veilwalkers/UI/Components/PackCardStyle.cs`
  - `Assets/Veilwalkers/UI/Components/CodexSlotStyle.cs`
  - `Assets/Veilwalkers/UI/Components/RarityBadgeStyle.cs`
  - `Assets/Veilwalkers/UI/Components/WordmarkStyle.cs`
  - `Assets/Veilwalkers/UI/Components/ChunkyComponentView.cs` (the representative thin view stub — final count decided in Task 9)
  - `Assets/Tests/EditMode/UI.Tests/ChunkyStyleTests.cs` + one per component (Task 10)
- UPDATE:
  - `Assets/Veilwalkers/UI/Design/PumpkinPatchTokens.cs` (Task 8 — `Spacing` → `IReadOnlyList<int>`)
  - `Assets/Tests/EditMode/UI.Tests/Veilwalkers.UI.Tests.asmdef` (Task 4 — add `Veilwalkers.Billing` ref; test-only)
  - `Assets/Tests/EditMode/UI.Tests/PumpkinPatchTokensTests.cs` (Task 8 — keep the Spacing test green against the `IReadOnlyList`)
- No `.meta` hand-authoring (Unity generates them; committed by the normal flow). The `UI/Components/` subfolder mirrors the established `UI/Encounter/` + `UI/Codex/` + `UI/Design/` subfolder-with-flat-namespace pattern.

### References

- [Source: docs/epics.md#Story-6.2] (lines 864-875) — the AC (the kit enumeration + the universal elevation device).
- [Source: docs/epics.md] UX-DR3 (line 96, chunky button + SLAY variant), UX-DR4 (line 97, credit pill), UX-DR5 (line 98, Codex slot 3-state), UX-DR6 (line 99, pack card), UX-DR7 (line 100, rarity-tier badge), UX-DR8 (line 101, wordmark), UX-DR9 (line 102, hard-shadow elevation), UX-DR17 (line 110, red never sole signifier).
- [Source: Assets/Veilwalkers/UI/Design/PumpkinPatchTokens.cs] — the 6.1 frame tokens every component sources (and the `Spacing` field Task 8 hardens).
- [Source: Assets/Veilwalkers/UI/Design/DreadScaleTokens.cs] — `ForTier(Rarity)`, the tier→token resolver the Codex-slot + rarity-badge call.
- [Source: Assets/Veilwalkers/UI/Encounter/MaterializationPlan.cs + MaterializationView.cs] — the `readonly struct` descriptor + thin stub-view precedent (4.7).
- [Source: Assets/Veilwalkers/UI/Codex/CodexSlot.cs + CodexSlotState.cs + CodexGridView.cs] — the 3-state Codex view-model + the "render state, don't re-classify" contract (2.4).
- [Source: Assets/Veilwalkers/Billing/CreditPack.cs + CreditPackCatalog.cs] — the transparent pack type the pack-card descriptor consumes (5.1).
- [Source: Assets/Veilwalkers/Monsters/Rarity.cs] — the append-only tier key.
- [Source: Assets/Veilwalkers/UI/Veilwalkers.UI.asmdef] — confirms UI→Monsters/Billing/Core already present (no production asmdef edit).
- [Source: Assets/Tests/EditMode/UI.Tests/Veilwalkers.UI.Tests.asmdef] — confirms the test asmdef LACKS Billing (Task 4 adds it; test-only).
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — the 6.1 CR `Spacing` deferral owned by "Story 6.2+" (Task 8 settles it).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (story-automator ungated cycle, 2026-06-16)

### Debug Log References

- Headless EditMode gate: `517/517 passed, 0 failed, 0 skipped`, `result="Passed"`, 0 `error CS` in `editor-test.log` (+30 over the 487 6.1 baseline — the new `ChunkyComponentKitTests`). Run command per `unity-headless-testing` memory (Editor closed, no `Temp/UnityLockfile`); `test-results.xml` + `editor-test.log` deleted before commit. First run clean — no compile-fix iteration needed.

### Completion Notes List

- **Task 1 (the shared chunky-style primitive, AC-2):** `ChunkyStyle.cs` — a `readonly struct` built ONLY via `ChunkyStyle.Elevated(fill, radius, outlineWidth=4)` which sources the hard-shadow offset (6,6), `ShadowBlur` (0), and black `ShadowColor` from `PumpkinPatchTokens` — so every component gets the AC-2 device by construction. `HasHardShadow` (`ShadowBlur==0 && offset!=0`) makes AC-2 a one-line assert; `Pressed()` returns a copy with the offset shrunk to `PressedShadowOffset=2` (strictly < resting 6). Outline-width band consts `OutlineWidthMin/Max=4/5`.
- **Task 2 (chunky button + SLAY variant, UX-DR3/DR17):** `ChunkyButtonStyle.cs` + `ChunkyButtonKind {Primary,Secondary,Slay}`. `For(kind,label)`: Primary→pumpkin-orange (`SecondaryAccent`)/RadiusMd, Secondary→Surface+outline, Slay→SLAY-red + `RequiresIcon=true` + the fixed "SLAY" label (the input label is IGNORED for Slay — UX-DR17 never red alone). `DisplayLabel` is the null-safe ALL-CAPS getter (`default(...)`→empty).
- **Task 3 (credit pill, UX-DR4):** `CreditPillStyle.cs` — gold (`CreditGold`)/RadiusPill; `Glyph="⬡"` (U+2B21 WHITE HEXAGON, the canonical glyph). NO locked/disabled state — a 0 count is a valid renderable state (negative clamps to 0), `TapToShop` always true (world never locks). `PulseColor` = candy-teal (`PrimaryAccent`) normally, a darkened teal when `NearEmpty` (the felt descent — the SAME accent dimmed, not a new token). `NearEmptyThresholdDefault=5` (a documented default; the threshold is a 6.3/balancing input).
- **Task 4 (pack card, UX-DR6):** `PackCardStyle.cs` — built from a `Veilwalkers.Billing.CreditPack`; Surface/RadiusLg (the lg sticker), `BonusColor=SecondaryAccent` (bonus in pumpkin-orange), `HasBonus` gates the bonus line, `IncludesGuaranteedRareLure` surfaces the Veil-pack transparency (no hidden reward — no gacha), `BadgeTextFor(PackBadge)` maps Popular→"POPULAR"/BestValue→"BEST VALUE"/None→empty, `HasLocalizedBuyAffordance` (the Play price is bound at runtime, never stored). Test-only asmdef edge `Veilwalkers.UI.Tests → Veilwalkers.Billing` added (Task 4; excluded from the acyclicity matrix by the `.Tests` suffix — the 5.4 precedent).
- **Task 5 (Codex slot, UX-DR5):** `CodexSlotStyle.cs` — Surface/RadiusMd outlined square; CONSUMES the 2.4 `CodexSlotState` (renders state, never re-classifies — `For(CodexSlot)` overload binds the 2.4 view-model directly). `ShowsCaughtStamp` is true ONLY for `Discovered` + a known tier; `CaughtStampTint` resolves via the single `DreadScaleTokens.ForTier` (the only tier-color-in-chrome use alongside the badge).
- **Task 6 (rarity badge, UX-DR7):** `RarityBadgeStyle.cs` — sm badge (RadiusSm) fill = the tier token via `ForTier(Rarity)` (delegates to 6.1's resolver, does NOT re-implement the map); carries the full `TierGradient`; `IsGradient` is true only for T5 (the bruise-rot ramp), T1–T4 solid.
- **Task 7 (wordmark, UX-DR8):** `WordmarkStyle.cs` — `Text="Veilwalkers"`, text-primary fill on the chunky hard-shadow device, `WobbleDegrees=3f` (a slight non-zero tilt — a 6.3/6.5 feel value), `BodyTextForbidden=true` (the documented "never for body" discipline).
- **Task 8 (settle the 6.1 `Spacing` deferral):** `PumpkinPatchTokens.Spacing` changed from `public static readonly int[]` to `public static readonly IReadOnlyList<int>` (`Array.AsReadOnly` over a private `_spacing` backing array) — the shared design-token scale can no longer be mutated through the public surface. The `SpaceNN` consts are unchanged; the 6.1 test's `Spacing.Length` (×1, the only call site repo-wide per grep) moved to `.Count`. CLOSES the open 6.1-CR `Spacing` deferral.
- **Task 9 (thin view stub):** ONE representative `ChunkyComponentView.cs` (the lower-risk choice — the descriptors are the deliverable; the views are 6.3's binding surface) mirroring `MaterializationView`: logic-free, no `GameServices` read, `Render(ChunkyStyle)` stubbed to `GameLog.Info` + a `TODO(Story 6.3)`. NO Bootstrap/registration (descriptors are pure structs; the view is inert until 6.3 places it).
- **Task 10 (tests):** `ChunkyComponentKitTests.cs` (30 pins). The AC-2 UNIVERSAL sweep (`Every_component_carries_the_hard_offset_shadow_with_no_blur` over all 8 component styles — fails if any declares a blur) + the outline-band sweep; AC-1 completeness (all six constructible); per-component non-tautological pins (ALL-CAPS transform input≠output, SLAY-never-alone composite, pressed<resting inequality, pill renders-0/never-locks + felt-descent dimming, pack-card bonus-pumpkin-orange + Veil Guaranteed-Rare transparency + badge mapping, Codex-slot 3-state distinctness + caught-stamp-only-when-discovered-with-tier, rarity-badge ForTier delegation + T5-gradient, wordmark text/wobble/body-forbidden); token-sourcing pins (each fill `==` its `PumpkinPatchTokens`/`ForTier` value, per-channel `Color32` compare — the 6.1 struct-equality-trap lesson).
- **NO production asmdef edit, NO Bootstrap edit, NO schema bump.** `Veilwalkers.UI` already references Monsters/Billing/Core (asmdef confirmed); descriptors are pure structs. The ONLY asmdef change is the test-only `UI.Tests → Billing` edge.
- **Scope held:** descriptors + one view stub only — no real UGUI prefabs/sprites/fonts/scenes (6.3), no live service/navigation wiring (6.3's `AppStateMachine`), no onboarding cards (6.4), no voice/state-treatments (6.5), no accessibility-floor wiring (6.6, beyond the SLAY-never-alone component-shape decision).

### File List

- `Assets/Veilwalkers/UI/Components/ChunkyStyle.cs` (NEW)
- `Assets/Veilwalkers/UI/Components/ChunkyButtonStyle.cs` (NEW)
- `Assets/Veilwalkers/UI/Components/CreditPillStyle.cs` (NEW)
- `Assets/Veilwalkers/UI/Components/PackCardStyle.cs` (NEW)
- `Assets/Veilwalkers/UI/Components/CodexSlotStyle.cs` (NEW)
- `Assets/Veilwalkers/UI/Components/RarityBadgeStyle.cs` (NEW)
- `Assets/Veilwalkers/UI/Components/WordmarkStyle.cs` (NEW)
- `Assets/Veilwalkers/UI/Components/ChunkyComponentView.cs` (NEW)
- `Assets/Tests/EditMode/UI.Tests/ChunkyComponentKitTests.cs` (NEW)
- `Assets/Veilwalkers/UI/Design/PumpkinPatchTokens.cs` (UPDATE — `Spacing` → `IReadOnlyList<int>`, Task 8)
- `Assets/Tests/EditMode/UI.Tests/Veilwalkers.UI.Tests.asmdef` (UPDATE — add `Veilwalkers.Billing` ref, test-only, Task 4)
- `Assets/Tests/EditMode/UI.Tests/PumpkinPatchTokensTests.cs` (UPDATE — `Spacing.Length` → `.Count`, Task 8)

### Review Findings

Adversarial 3-layer CR (Blind Hunter / Edge Case Hunter / Acceptance Auditor) + per-finding verifier: **17 raw → 17 deduped (0 cross-layer convergence) → 4 confirmed (all PATCHED), 2 deferred, 9 spec-sanctioned, 2 false-positive. 0 AC violations.** 518/518 EditMode green re-verified after patching (+1 over the 517 dev baseline — the negative-clamp coverage pin; no regression from the 4 patches). All 4 confirmed were minor quality/clarity/coverage nits (no correctness bug, no AC violation).

- [x] [Review][Patch] **`CreditPillStyle.For(int)` duplicated the negative-clamp rule** (`CreditPillStyle.cs`). The single-arg overload computed near-empty from `(count < 0 ? 0 : count)` then passed the UN-clamped `count` to `For(int,bool)`, which clamped again — two clamp sites that could silently diverge if the floor rule changed. Fix: a single private `ClampToFloor(count)` helper; the single-arg overload clamps ONCE and feeds the same `display` value to both the count and the near-empty test. [Blind Hunter]
- [x] [Review][Patch] **`DimTeal` "strictly dimmer" invariant silently depended on the token's channel magnitude** (`CreditPillStyle.cs`). The 55%-truncation produced a dimmer sum only because the candy-teal channels are large; a hypothetical near-black accent could truncate to an equal sum. Fix: extracted `DimScalePercent` + a per-channel `Dim(byte)` that guarantees the result is `<=` the source (never brighter) and strictly `<` for any channel `>= 2` (which the accent satisfies), so the felt-descent reads as dimmer without an implicit magnitude assumption; documented the one degenerate all-black case. [Blind Hunter]
- [x] [Review][Patch] **`ChunkyButtonStyle.For()` had a redundant `case Primary:`/`default:` pair** (`ChunkyButtonStyle.cs`). Both arms returned the identical Primary style, masking intent. Fix: split them — `Primary` is now its own explicit arm; `default` is a distinctly-commented graceful-degrade arm (any unmapped/future kind → the calmest non-destructive Primary, never a stray Slay). Clarifies that the fallback is intentional, not an accidental duplicate. [Edge Case Hunter]
- [x] [Review][Patch] **No test for the single-arg `For(negative).NearEmpty` clamp-then-threshold path** (`ChunkyComponentKitTests.cs`). Negative clamping and the threshold were tested separately but never together through the convenience overload. Fix: added `Credit_pill_single_arg_clamps_negatives_then_applies_the_threshold` (`For(-10)` → count 0, near-empty true, still tappable). [Edge Case Hunter]
- [x] [Review][Defer] **`ChunkyButtonStyle.DisplayLabel` recomputes `ToUpperInvariant()` on every read** (allocates per access). Task 2 explicitly sanctioned EITHER pre-compute-at-construction OR compute-on-read; the descriptor is pure-logic and never bound per-frame in 6.2 (the per-frame UGUI binding is 6.3). Perf-tuning the hot-path read belongs with 6.3's real widget binding. Owner: Story 6.3. → deferred-work.md. [Blind Hunter]
- [x] [Review][Defer] **`PackCardStyle.BadgeTextFor` `default`-folds a future `PackBadge` member to empty** (no exhaustiveness throw). Correct for the canonical 3-pack universe (None/Popular/BestValue all mapped + tested); a new badge lands WITH its arm in the story that adds it (the append-only `Rarity`/`ForTier` precedent). Owner: a future pack-catalog-expansion story. → deferred-work.md. [Edge Case Hunter]
- [x] [Review][Sanctioned] **`RarityBadgeStyle.Style.Fill` carries only the gradient Start; the full ramp is on `TierColor`** — the intentional architectural split (Task 6): all components share the one `ChunkyStyle` (single Fill), and T5 exposes its `TierGradient` separately for 6.3 to paint via `IsGradient`. A 6.3 mis-bind risk, not a 6.2 defect. [Blind Hunter]
- [x] [Review][Sanctioned] **`PumpkinPatchTokens.Spacing` `int[]`→`IReadOnlyList<int>` is a breaking surface change** — exactly the Task 8 mandate (settles the 6.1-CR deferral); the only `.Length` call site (the 6.1 test) was migrated to `.Count`, grep-confirmed no orphans. [Blind Hunter]
- [x] [Review][Sanctioned] **`CodexSlotStyle.CaughtStampTint` returns T1 unconditionally; the `ShowsCaughtStamp` guard is doc-only** (×2 — Blind + Edge). Task 5 design: the descriptor provides a complete value, the consumer (6.3) gates rendering via `ShowsCaughtStamp`; pinned by the tests (tint only read when discovered-with-tier). [Blind Hunter, Edge Case Hunter]
- [x] [Review][Sanctioned] **`ForTier` may `GameLog.Warn` as a side effect of the `CaughtStampTint`/badge getter for an out-of-range cast tier** — the `ShowsCaughtStamp` gate guarantees a non-null real tier when the tint is rendered; the warn is 6.1's documented NFR-3 graceful-degrade, and 6.2 delegates to 6.1's resolver by mandate (do-not-re-implement). [Blind Hunter]
- [x] [Review][Sanctioned] **`RarityBadgeStyle.For` does not validate `Rarity` range** — Dev Notes mandate delegating to 6.1's total `ForTier` resolver (which owns graceful-degrade); re-validating duplicates 6.1's guard. [Edge Case Hunter]
- [x] [Review][Sanctioned] **`ChunkyStyle.Pressed()` doesn't validate the pressed offset across radii/outlines** — the AC is the `pressed < resting` INEQUALITY (the 4.x "AC is the inequality, not the value" precedent), pinned by the test; a 6.3 feel-pass may retune within the band. [Edge Case Hunter]
- [x] [Review][Sanctioned] **`ChunkyStyle.Elevated` doesn't enforce the 4–5px outline band at construction** — Task 1 mandates "document the band," not a runtime guard; AC-2 is structural (every component calls `Elevated` with the default 4) + asserted post-construction by the band-sweep test (the `CodexSlot`/`MaterializationPlan` documented-contract precedent). [Edge Case Hunter]
- [x] [Review][Sanctioned] **`WordmarkStyle.Wobble` could be mutated via reflection** — the `readonly struct` discipline protects against normal C# corruption (the established descriptor precedent); reflection attacks are out of scope. [Edge Case Hunter]
- [x] [Review][False-positive] **`DimTeal` byte-arithmetic overflow** — misread of C# operator promotion: `byte * int` promotes to `int` (217*55=11935 fits), no overflow. (The patched `Dim` helper keeps the same correct promotion.) [Edge Case Hunter]
- [x] [Review][False-positive] **`For(negative).NearEmpty` boundary untested** — partially real (the dedicated test was thin), but the verifier deemed it covered-by-construction; nonetheless the CONFIRMED coverage patch above adds the explicit pin, fully closing it. [Edge Case Hunter]
