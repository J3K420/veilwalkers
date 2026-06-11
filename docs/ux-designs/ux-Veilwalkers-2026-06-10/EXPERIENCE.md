---
name: Veilwalkers
status: final
sources:
  - docs/prds/prd-Veilwalkers-2026-06-10/prd.md
  - docs/architecture.md
  - docs/game-design-doc.md
updated: 2026-06-10
design_ref: ./DESIGN.md
---

# Veilwalkers — Experience Spine

> Single-surface Android AR game. The camera is the core surface; the chrome is a Saturday-morning cartoon (see `DESIGN.md`). The signature primitive is the tiered **Materialization** — a monster entering the player's real space. Spine wins on conflict with mocks.

## Foundation

Single-surface Android mobile, AR-Required. Unity 2022 LTS + AR Foundation 5 / ARCore 5; local-first MVP (see `architecture.md` — inherited, not duplicated). `DESIGN.md` is the visual identity reference; this spine is the experience. **The AR camera is the core surface** — Home/Codex/Shop are the bright frame around it. The Pit is post-MVP.

## Information Architecture

| Surface | Reached from | Purpose |
|---|---|---|
| Onboarding | First launch (cold, once) | Premise cards, camera disclosure, grant 20 Credits |
| Home | App open / hub | Wordmark, credit pill, **ENTER AR HUNT** CTA, Codex (X/67), Shop, daily reward |
| AR Hunt (Encounter) | Home → ENTER AR HUNT | The core loop: lure → materialize → Scan/Capture/Slay |
| Codex | Home | The 67-grid, X/67 progress, per-monster detail |
| Shop | Home credit pill / mid-encounter top-up | Credit Packs via Google Play Billing |
| **The Pit** | — | **Post-MVP** (placeable colosseum). Noted, not built. |

| Overlay | Trigger | Notes |
|---|---|---|
| AR Safety Warning | Cold entry into AR Mode | Mandatory, non-skippable. Full read on first/session-first entry; fast on-brand card on later cold entries. Shop round-trip resume does NOT re-fire (per architecture). |
| Insufficient-credits top-up sheet | Lure/Slay with too few Credits | Non-blocking sheet over the live encounter; returns to the exact same encounter. |
| How to Use Credits | Help affordance | Explains cost-before-spend economy. |

→ Composition references: [`mockups/encounter.html`](../ux-Veilwalkers-2026-06-10/mockups/encounter.html), [`mockups/codex.html`](mockups/codex.html), [`mockups/surfaces-home-shop-onboarding-safety.html`](mockups/surfaces-home-shop-onboarding-safety.html), [`mockups/palette-pumpkin-patch.html`](mockups/palette-pumpkin-patch.html). Placeholder geometry in mocks conveys layout/states only, **not art direction** (the CSS "monsters" are stand-ins; real creature art is a separate production track guided by the Dread Ladder). Spine wins on conflict.

## Dread Ladder / Rarity Tiers

**Single axis: rarity = scariness = materialization tier.** One ladder drives everything (resolves PRD **OQ-2**; supersedes architecture's 4-tier placeholder — feed upstream). Tier color tokens are `{colors.tier1-common}` … `{colors.tier5-nightmare}` in `DESIGN.md`. **T3 Rare = the Guaranteed-Rare Lure threshold** (Veil Pack guarantees Rare-or-better).

| Tier | Rarity | Tone | Monster character |
|---|---|---|---|
| T1 | Common | Playful / cartoon-kid-scary | Round, goofy, big-eyed, lunchbox-friendly |
| T2 | Uncommon | Lightly eerie | Odd proportions, one-too-many eyes, mischievous |
| T3 | Rare | Genuinely unsettling | Uncanny silhouettes, wrong angles, watching |
| T4 | Epic | Dread | Large, distorted, predatory |
| T5 | Nightmare | Terrifying (stylized, Teen-ceiling) | Pus-covered grotesque-cool monstrosity — never realistic gore |

The dread ladder is also a **Codex instrument**: as the player climbs tiers, more silhouettes unlock into view, so the collection's scope escalates with the arc (see Codex slot states).

## Materialization System

**The hero primitive.** Monsters do not pop into existence — they *arrive*, and the arrival is tiered to the monster's rarity. This is the single most important interaction in the product. All five reuse the architecture's "veil" metaphor (lure VFX = veil-parting; lost-anchor restore reuses these same VFX).

**Universal rules:**
- **Action bar is HIDDEN during materialization** — plays clean and cinematic, no buttons; the bar animates/pops in when the monster settles.
- **Entrance duration scales with tier:** T1 ~0.5s → T5 ~2.5–3s. The wait is part of the reward; rare monsters feel like events.
- **Credit spend-ack stays sub-second regardless** (NFR-2). The Lure deduction confirms instantly on the credit pill even while a 3-second Breach animation plays out. **Spend-ack ≠ materialization duration.**
- Ambiance is the **diffused dread scale** (`DESIGN.md` Colors) — AR tint/lighting/vignette/SLAY-glow shift toward the active tier; never a visible rarity bar.

| Tier | Variant | Motion | Ambiance (lighting/tint) | Audio | Camera | Duration |
|---|---|---|---|---|---|---|
| T1 Common | **Pop-in** | Candy-teal mist poof, squash-stretch overshoot, wobble-settle | None — full bright friendly | Cartoon *bamf* + boing | Static | ~0.5s |
| T2 Uncommon | **Unfurl** | Veil-ribbons peel back from center, fades up through shimmer, curious twitch | Faint teal→periwinkle vignette breathes in, holds soft `{colors.tier2-uncommon}` | Soft chime-shimmer + one low note | 1–2% breathing zoom, no shake | ~1s |
| T3 Rare | **Seep** | Dark pool spreads on surface, rises head-first, eyes catch light before body resolves | Lights visibly DIM, overlay cools toward `{colors.tier3-rare}`, real vignette closes, cartoon brightness drains | Rising drone + wet shadow-rustle → silence as it locks eyes | Slow push-in + one soft pulse | ~1.5s |
| T4 Epic | **Tear** | Jagged rip opens above plane, world warps/refracts, monster forces through (too big), drops with weight | Heavy `{colors.tier4-epic}` tint, strong vignette, chromatic aberration at tear edges | Sub-bass swell + tearing crunch + landing thud | Real shake on tear + landing, brief push-in | ~2s |
| T5 Nightmare | **Breach** | The **camera feed itself "corrupts"** — screen edges crawl with glitching veil-rot, picture distorts; monstrosity erupts; final lunge-toward-camera before settle | Full bruise-rot grade (`{colors.tier5-nightmare}`), oppressive vignette, **the cartoon UI itself briefly flickers/glitches** | Silence → hard audio sting + guttural roar + chest-felt low end | Strong shake, snap push-in on lunge, settle | ~2.5–3s |

**T5 Breach is the thesis made literal** — the Nightmare briefly *breaks* the friendly cartoon wrapper. Rules around it:
- Must read as a **designed FX, never a crash** — branded glitch styling, kept brief, input recovery never blocked.
- **Must respect the reduced-motion / photosensitivity setting** (see Accessibility Floor): with it on, the corruption/glitch/screen-shake is tamed to a non-flashing, low-motion entrance.

## Voice and Tone

Microcopy. Visual identity lives in `DESIGN.md`. Register: **wry-eerie, *Gravity Falls* × *Stranger Things*** — diegetic system framing ("the systems wear a costume"). **Exception: safety messaging is plain, legible, and never buried in flavor.**

| Do | Don't |
|---|---|
| "Part the Veil." | "Start AR" / "Loading…" |
| "Consult the Veil" (rewarded action) | "Watch an ad" |
| "The Veil shows you more…" (new tier silhouettes appear) | "New items unlocked!" |
| "Pulled back through the Veil." (anchor restored) | "Reconnecting…" |
| "Not yet discovered." (locked Codex slot) | "???ERROR" |
| Wry-eerie lore in Codex entries | Gory, genuinely frightening, or jokey-random copy |
| **Safety: "The Veil pulls your attention. Keep one eye on the real world."** — in-brand but unmistakably a real safety message | Burying the safety warning in flavor or making it skippable |

## Component Patterns

Behavioral. Visual specs live in `DESIGN.md.Components`.

| Component | Use | Behavioral rules |
|---|---|---|
| Encounter action bar | AR Hunt, monster settled | **Scan** (free), **Capture** (free base / Strong Capture consumes an earned charge), **Slay** (3 Credits, SLAY-red + icon + label), plus earned charge-items (Stability Boost, Nightveil Filter). HIDDEN during materialization, pops in on settle. Charge-items show charge count, not a credit cost — they are earned via XP, never purchasable (per architecture economy revision). |
| Credit pill | All surfaces | Cost shown before every spend (no surprise deductions). Tap → Shop. Near-empty "felt descent": pulse cyan `{colors.primary-accent}` → dim teal. **World never locks at zero** — player still enters AR, sees monsters, aims; only Lure is dimmed with its real cost shown. |
| Codex slot | Codex grid | Three states (Discovered / `???` silhouette / `?` blank), per Dread Ladder gating. Tap discovered → detail (art, tier badge, wry lore, stats, first-discovered date). First-capture fires the reveal animation. |
| Shop pack cards | Shop / top-up sheet | Three packs (Starter 50/$1.99, Hunter 150+20/$4.99 "POPULAR", Veil 400+100+Guaranteed-Rare-Lure/$9.99 "BEST VALUE"). Google Play Billing only. Transparent contents, soft nudges, no gacha. |
| Non-blocking top-up sheet | Insufficient credits mid-encounter | Appears as a sheet over the live encounter; on purchase, credits update immediately and return to the **exact same encounter, state-faithful** (per UJ-2 / architecture snapshot-rehydrate). The encounter is not lost behind it. |

## State Patterns

| State | Surface | Treatment |
|---|---|---|
| Cold-start | App / AR warmup | **"Veil-parting wipe" ritual, never a spinner/progress bar.** AR prewarm hides under interaction (choosing first Lure, tilt-up). Slow-device fallback = *more ritual* ("The Veil is thick here, hold steady…"), never a percentage. |
| Plane not found | AR Hunt | Friendly coaching: "Move your phone slowly across a textured surface." Never spawn a monster into empty space. |
| Lost / interrupted anchor | AR Hunt | Encounter suspends with guidance (not a crash). On recovery, **"Pulled back through the Veil"** restore — reuses materialization VFX; re-places on nearest in-frustum plane, state-faithful (scan progress, boosts intact). Never vanishes. |
| Camera denied | Onboarding / AR entry | Explanatory re-grant screen with a path to settings. **Never a dead-end or crash.** Disclosure shown before the OS dialog. |
| Insufficient credits | AR Hunt | Non-blocking top-up sheet (see Component Patterns). World never locks. |
| Purchase cancel/fail | Shop / top-up sheet | Balance unchanged, non-alarming message, no monster or progress lost. Returns to prior context. |
| Codex reveal | Codex / encounter resolve | On first discovery: slot flips silhouette→full art with a "caught" stamp in the tier color; count ticks "11/67 → 12/67". New-tier unlock adds a beat: silhouettes fade into the grid ("The Veil shows you more…"). |

## Interaction Primitives

- **Tilt-up — "Raise your phone to part the Veil."** Doubles as plane-detection priming. The gesture that enters the live encounter.
- **Tap-to-act** on the action bar (Scan / Capture / Slay / charge-items). Cost shown before spend.
- **Tap-to-slay** — MVP keeps Slay light/tap-based, not deep combat (per OQ-6).
- **Lure selection** — Basic (1) / Premium (4, higher rare chance) / Multi-Lure (5, two monsters). Cost shown before spend.
- **Banned:** any flow that strands the player (dead-end permission screens, locked-world at zero credits, un-acknowledgeable safety gate).

## Accessibility Floor

Consumer stakes; none specified upstream, so we set the floor here.

- **Reduced-motion / photosensitivity setting** — tames the T5 Breach glitch + camera-corruption, the per-tier ambiance intensity, and screen-shake. With it on: no flashing, low-motion entrances, glitch replaced by a static branded transition. Materialization dread must still read without strobe/shake.
- **SLAY-red is never the sole signifier** — the dangerous action always pairs red with a skull/slash icon + "SLAY" label.
- **Audio is never the sole carrier of dread** — materialization audio stings have a visual/caption cue, so players with sound off or hearing loss still get the entrance beat.
- **Legibility** — chunky-outline UI and `{colors.text-primary}` must hold contrast on `{colors.background}`; muted text never used for safety copy.
- **AR safety message legibility is non-negotiable** — plain language, high contrast, never dismissed by flavor or A/B'd away (SM-C3).
- **Tap targets ≥ 48dp** (Android).
- **TalkBack** — every interactive element labeled with role + state; the credit pill announces balance changes; the Codex count announces on tick-up.

## Key Flows

### Flow 1 — Maya meets her first monster in her living room

1. Maya installs and launches; **Onboarding** shows 2–3 chunky premise cards (the Veil is real, monsters are real, you're a Veilwalker).
2. **Camera disclosure** card appears *before* the OS dialog; she allows. Onboarding grants **20 Credits** (chunky coin-burst).
3. Home → she taps **ENTER AR HUNT**.
4. **AR Safety Warning** (full read, first session) → she taps "I'M AWARE — PART THE VEIL."
5. Camera live; plane coaching → she raises her phone ("part the Veil") → planes detected on her rug.
6. She spends **1 Credit** on a Basic Lure (cost shown first; spend-ack instant).
7. A **Common "Pop-in"** materialization — candy-teal mist poof, bouncy settle — on her actual rug, occluded by her coffee table. Action bar pops in on settle.
8. She taps **Capture** (free base); success.
9. **Codex reveal:** slot flips silhouette→full art, count reads **"1 / 67."**

**Climax:** the Pop-in materialization — a monster arriving in her real living room — is the moment she wants to show whoever's next to her.

**Edge case (plane fail):** poor lighting / featureless floor → friendly coach "Move your phone slowly across a textured surface"; no monster spawns into empty space until a plane is found.

### Flow 2 — Dev burns a hot session and tops up

1. Dev (hunter type) is mid-session, good surface area, ~5 Credits.
2. He repeatedly chooses **Slay (3 Credits)** for loot over Capture; drops to ~2 Credits. Credit pill begins the cyan→dim-teal "felt descent."
3. A **Rare/Epic** monster appears via **Seep/Tear** — ambiance cools/warps toward its tier. He wants a **Premium Lure (4)** but can't afford it.
4. A **non-blocking top-up sheet** slides over the live encounter (world doesn't lock; the waiting monster stays behind the sheet).
5. He buys the **Hunter Pack (150 + 20, $4.99)** via Google Play Billing; credits update immediately.
6. The sheet dismisses → he returns to the **exact same encounter**, state-faithful — the monster is still there, mid-materialization mood intact.

**Climax:** returning to the waiting monster mid-breach — spending felt like buying *more of a good time*, not unblocking a wall.

**Edge case (purchase cancel):** he cancels → balance unchanged, non-alarming message, no monster or progress lost; he's back in the same encounter.

### Flow 3 — Priya completes a Codex streak before bed

1. Priya (completionist) returns to Home; a **daily reward** is waiting ("claim your daily offering") — she claims free Credits.
2. She enters AR (fast on-brand safety card, later cold entry) → lures a tricky monster.
3. She spends a **Stability Boost** charge (earned, not bought) to raise her odds.
4. She attempts a tricky **Capture** — succeeds, filling a Codex gap.
5. She opens the **Codex** to admire progress and read the new entry's wry-eerie lore.

**Climax:** the Codex slot flips from silhouette to full art with a tier-colored "caught" stamp; the count ticks up (e.g. "11 / 67 → 12 / 67").

**Edge case (failed capture):** a failed Capture leaves the encounter active and offers a free **Retry** (re-attempt; no Retry credit cost per the economy revision) — never a dead-end.
