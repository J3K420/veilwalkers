---
name: Veilwalkers
description: Android AR monster-hunting game. Saturday-morning Cartoon Network frame — thick black outlines, chunky shapes, hard offset shadows — wrapping monsters that escalate from playful to terrifying. The friendly frame is what makes the scary payload land.
status: final
created: 2026-06-10
updated: 2026-06-10
project: Veilwalkers
sources:
  - docs/prds/prd-Veilwalkers-2026-06-10/prd.md
  - docs/architecture.md
  - docs/game-design-doc.md
colors:
  background: '#2E1A47'
  surface: '#3D2461'
  primary-accent: '#2BD9C4'
  secondary-accent: '#FF7A1A'
  text-primary: '#FFF6E9'
  text-muted: '#B9A6C9'
  danger-slay-red: '#FF2E4D'
  credit-gold: '#FFC73A'
  tier1-common: '#7DE0C4'
  tier2-uncommon: '#4FA8C9'
  tier3-rare: '#6A5AC9'
  tier4-epic: '#4B2E6B'
  tier5-nightmare-from: '#3A4C3A'
  tier5-nightmare-to: '#5C2A4A'
typography:
  wordmark:
    note: 'Chunky outlined display lockup — heavy weight, black outline, personality. Used only for "Veilwalkers" brand mark and tier/section titles.'
  title:
    note: 'Platform native, heavy weight — Android Headline Small. All-caps for chunky CTA buttons.'
  body:
    note: 'Platform native — Android Body Large.'
  meta:
    note: 'Platform native — Android Body Small. Counts ("12 / 67"), prices, lore captions.'
rounded:
  sm: 10px
  md: 18px
  lg: 28px
  pill: 999px
spacing:
  '1': 4px
  '2': 8px
  '3': 12px
  '4': 16px
  '5': 24px
  '6': 32px
components:
  outline:
    note: 'Thick black stroke on every card/button/badge — 4px standard, 5px on hero CTA. Non-negotiable signature.'
  hard-shadow:
    note: 'Offset-black drop shadow, NO blur — 6px 6px 0 #000 standard. This IS the elevation device.'
---

## Brand & Style

Veilwalkers is built on one load-bearing tension: **the chrome is always a bright, playful Saturday-morning cartoon — the monsters are not.** The UI frame (Home, Codex, Shop, microcopy, every button and badge) stays loud, friendly, and inviting. The monsters escalate across five rarity tiers from goofy-cute to a stylized-grotesque "Nightmare." The friendly frame makes the scary payload hit harder — this is the haunted-house-ride model, and the contrast is the brand, not an accident.

The execution register is **classic Cartoon Network**: thick black outlines (4–5px) on every shape, fat chunky rounded forms, exaggerated/bouncy/sticker-like cards, hard offset-black drop-shadows with no blur, and a chunky outlined wordmark with personality. References: *Gravity Falls* title cards, *The Amazing World of Gumball* UI energy, classic CN bumpers. The palette is **"Pumpkin Patch"** — a warm cozy-Halloween cartoon frame (purple-night, candy-teal, pumpkin-orange) chosen specifically because the coziness makes high-tier monster materializations land harder.

> **Note — this DESIGN.md deliberately inverts the mobile example's "avoid shadows / restrained palette" guidance.** Veilwalkers is loud on purpose. Hard shadows and saturated candy color are signature, not noise.

Anti-references (carried from PRD/arch): realistic gore, jump-scare horror, photoreal monsters, flat/clinical/minimal UI.

## Colors

The "Pumpkin Patch" palette. Frame colors are constant and bright; the dread-scale tokens are a separate, encounter-only treatment system (see below).

**Frame palette (constant chrome):**

- **Background (`#2E1A47`)** — purple-night canvas behind all surfaces.
- **Surface (`#3D2461`)** — cards, sheets, panels. One step up from background.
- **Primary accent / candy-teal (`#2BD9C4`)** — the signature accent; "the Veil," friendly highlights, active states, the credit-pill "felt descent" pulse.
- **Secondary accent / pumpkin-orange (`#FF7A1A`)** — the primary CTA color (Enter AR Hunt, Buy buttons), bonus call-outs in Shop. The single loudest "go" color.
- **Text-primary (`#FFF6E9`)** — warm off-white; all body and headline text on dark surfaces.
- **Text-muted (`#B9A6C9`)** — derived muted lavender for secondary/caption text, disabled labels, "Not yet discovered" copy. Legible on `#2E1A47`/`#3D2461`, never used for safety copy.
- **Danger / SLAY-red (`#FF2E4D`)** — reserved exclusively for the Slay action and genuinely destructive confirmations. Never decorative. Because it marks the dangerous action, it is **never the sole signifier** (always paired with icon + label — see EXPERIENCE Accessibility Floor).
- **Credit-gold (`#FFC73A`)** — the Monster Credits currency only: the credit pill, ⬡ icon, pack amounts. Never used for non-currency UI.

**Dread scale (encounter-only tier treatment — NOT a UI element):**

The five rarity tiers each carry a color/mood token. **CRITICAL: the dread scale is a design reference / token set, NOT something players ever see as a bar, strip, or legend.** It is diffused into the AR encounter — overlay tint, encounter lighting, vignette, and SLAY-button glow — keyed to the active monster's rarity tier during materialization and the live encounter. A Common encounter stays bright and friendly; a Nightmare encounter drains toward bruise-rot. Do **not** ship a literal rarity color-bar.

| Tier | Token | Hex | Mood it tints toward |
|---|---|---|---|
| T1 Common | `tier1-common` | `#7DE0C4` | Playful, full-bright, friendly |
| T2 Uncommon | `tier2-uncommon` | `#4FA8C9` | Lightly eerie |
| T3 Rare | `tier3-rare` | `#6A5AC9` | Genuinely unsettling |
| T4 Epic | `tier4-epic` | `#4B2E6B` | Dread |
| T5 Nightmare | `tier5-nightmare` | `#3A4C3A → #5C2A4A` (bruise-rot gradient) | Terrifying — stylized, Teen-ceiling, deliberately **no gore-red** |

The dread scale also color-codes one frame element: the **rarity-tier badge** on Codex detail pages and (subtly) the Codex "caught" stamp. Beyond the badge, tier color lives in the AR moment, not the chrome.

**Teen-rating ceiling:** T5 Nightmare is stylized grotesque-cool ("pus-covered monstrosity" as cartoon-monster, not photoreal). The bruise-rot gradient intentionally avoids realistic blood-red. This bounds the top of the ladder.

## Typography

Platform-native (Android) at heavy weights — the CN register comes from weight, outline, and all-caps, not a custom typeface. Three roles plus the wordmark:

- **Wordmark** — chunky outlined display lockup for "Veilwalkers" and major section titles. Heavy, black-outlined, with personality (slight wobble allowed). Never used for body.
- **Title** — Android Headline Small, heavy. CTA button labels are **ALL-CAPS** ("ENTER AR HUNT", "PART THE VEIL").
- **Body** — Android Body Large for premise cards, lore entries, safety copy.
- **Meta** — Android Body Small for counts ("12 / 67"), prices, dates, captions.

Dynamic type honored at every level; chunky controls must not truncate at the largest accessibility setting.

## Layout & Spacing

Scale: 4 / 8 / 12 / 16 / 24 / 32 px. Chunky components want generous internal padding (≥16px) so the thick outline + hard shadow have room to read. Single-column mobile; Android 16dp margins. The AR Hunt surface is full-bleed camera — chrome (action bar, credit pill) floats over it as discrete chunky islands, never a docked frame that competes with the encounter.

Sheets and overlays stack one level deep (the non-blocking top-up sheet over the live encounter is the key case; it must not bury the encounter behind it — see EXPERIENCE State Patterns).

## Elevation & Depth

**The hard offset-black drop-shadow IS the elevation device — `6px 6px 0 #000`, no blur.** This is the deliberate opposite of the mobile example's "avoid elevation" rule. Every chunky card, button, badge, and pill casts the same hard sticker-shadow; depth reads as cut-out stickers laid on the surface, not as soft material elevation. Pressed/active state shrinks the offset (e.g. `2px 2px 0`) so the element "presses down" into the surface — the primary tactile feedback for taps.

No blurred shadows, no gradient glows in the chrome. (Glow exists only inside the AR encounter as dread-scale lighting, never on UI chrome — except the SLAY button, whose glow shifts with the active tier.)

## Shapes

Fat, chunky, friendly. Corners: `sm` 10px (small badges, inputs), `md` 18px (cards, buttons, sheets), `lg` 28px (hero cards, pack cards), `pill` 999px (credit pill, tags). Every shape carries the 4–5px black outline. Slight hand-drawn wobble on outlines is encouraged (sticker, not vector-perfect). Imagery and monster portraits follow container corners exactly and sit inside the outline.

## Components

→ Visual reference: [`mockups/palette-pumpkin-patch.html`](mockups/palette-pumpkin-patch.html) (palette + chrome), [`mockups/surfaces-home-shop-onboarding-safety.html`](mockups/surfaces-home-shop-onboarding-safety.html), [`mockups/codex.html`](mockups/codex.html), [`mockups/encounter.html`](mockups/encounter.html). CSS "monsters" are placeholder geometry, not art direction. Spine wins on conflict.

- **Chunky button** — the workhorse CTA. `md`/`lg` rounded, 4–5px black outline, `6px 6px 0 #000` hard shadow, ALL-CAPS title label. Primary = **pumpkin-orange `#FF7A1A`** (Enter AR Hunt, Buy, primary acks). Secondary = surface `#3D2461` with outline. Pressed state shrinks the shadow offset. Slay is the one exception — **SLAY-red `#FF2E4D`**, always with a skull/slash icon + "SLAY" label (never red alone).
- **Credit pill** — gold (`#FFC73A`), pill-shaped, outlined, with the ⬡ hex icon + integer count ("⬡ 20"). Top-right, tappable → Shop. Cost is always shown before any spend. Near-empty it runs the "felt descent" pulse: cyan `#2BD9C4` → dim teal (world never locks).
- **Pack card (Shop)** — sticker-style `lg` card, heavy outline + hard shadow. Shows credit icon, base + bonus (bonus called out in pumpkin-orange), and a chunky pumpkin-orange Buy button with the localized Play price. Soft-nudge tags only ("POPULAR" on Hunter Pack, "BEST VALUE" on the Veil Pack — the most decorated card, which bundles the Guaranteed-Rare Lure). Transparent contents, never a mystery box.
- **Rarity-tier badge** — small `sm` outlined badge color-coded to the dread-scale token for its tier (T1 `#7DE0C4` … T5 bruise-rot). Appears on Codex detail pages. The one place tier color lives in the chrome.
- **Codex slot (3 states)** — a chunky outlined square in the 67-grid:
  1. **Discovered** — full monster art, tier-tinted "caught" stamp.
  2. **Locked silhouette (`???`)** — for undiscovered monsters in a tier the player has *begun* encountering: shows the shape you're missing.
  3. **Blank (`?`)** — for undiscovered monsters in top tiers the player hasn't reached yet: no silhouette, preserves the scary reveal. (Hybrid gradual-reveal per OQ-7.)
- **Wordmark** — chunky outlined "Veilwalkers" lockup with personality. The brand anchor on Home and onboarding.

## Do's and Don'ts

| Do | Don't |
|---|---|
| 4–5px black outline on every card/button/badge | Thin/no outlines, soft minimal UI |
| Hard offset-black shadow `6px 6px 0 #000` (no blur) as the elevation device | Blurred/soft material shadows, gradient glows on chrome |
| Keep the chrome bright/playful at all times (Pumpkin Patch) | Let the frame go dark/moody — dread lives in the encounter, not the menus |
| Diffuse the dread scale into AR tint/lighting/vignette/SLAY-glow | Ship a literal rarity color-bar or tier legend to the player |
| Cap T5 at stylized grotesque-cool; bruise-rot, no gore-red | Photoreal gore, realistic blood, jump-scare horror (breaks Teen rating) |
| SLAY-red + icon + label together | SLAY-red as the sole signifier of the dangerous action |
| Credit-gold only for the Credits currency | Gold on non-currency UI |
| Transparent packs, exact contents, soft nudges | Gacha/mystery-box framing, dark patterns, third-party payment UI |
| Pumpkin-orange = the one loud "go" CTA color | Multiple competing CTA colors per screen |
