---
title: Veilwalkers
status: final
created: 2026-06-10
updated: 2026-06-10
---

# PRD: Veilwalkers
*Working title — confirmed against design doc v1.0.*

> **Tagline:** "Monsters are closer than you think."

## 0. Document Purpose

This PRD is the authoritative product specification for **Veilwalkers**, an Android AR monster-hunting and collection game. It is written for the PM, the development team (Unity/AR, billing, backend), the UX designer, and downstream BMad workflows (architecture, epics/stories). It builds on the existing game design doc (`docs/game-design-doc.md`, v1.0) and the project conventions in `CLAUDE.md` — it does not duplicate them; where they already state a fact (credit economy, Google Play hard requirements, 67-monster lore), this PRD references and operationalizes it.

Structure: vocabulary is anchored in a **Glossary (§3)** that all other sections use verbatim; features are grouped with functional requirements (`FR-N`) nested and globally numbered; inferences not explicit in the source are tagged inline `[ASSUMPTION: ...]` and indexed in §9. The PRD is scoped to the **MVP (Phase 1)**; Phase 2/3 capabilities are captured as explicit non-goals and roadmap notes so they survive to later planning without bloating the MVP.

## 1. Vision

Veilwalkers turns the player's real surroundings into a hunting ground for monsters. Using the phone camera and AR, players **lure** creatures into their actual living room, backyard, or street, then **scan**, **capture**, or **slay** them — each monster anchored convincingly in real-world space with plane detection, occlusion, and lighting. It is *Pokédex-meets-cryptid-hunt* with a stylized-spooky tone in the register of *Gravity Falls* and *Stranger Things*: unsettling and mysterious, never gory, comfortably Teen-rated.

The hook is twofold. First, the **collection drive**: there are exactly **67** monsters in the universe, and completing the **Codex** is a clear, finite, prestige goal. Second, the **hunter/collector split**: every encounter forces a meaningful choice — capture it gently for the Codex, or slay it for richer loot. These two playstyles give the game replay tension beyond "catch 'em all."

Veilwalkers is free-to-play, funded by **Monster Credits** purchased through Google Play Billing. The free experience is deliberately generous (20 starting credits) so the camera-magic moment lands before any paywall; spending is driven by desire — rare monsters, slaying for loot, advanced lures — not by friction. *Monster Hunter Now* (Niantic) is the proof point that AR monster-hunting plus IAP is both approvable on Play and commercially viable.

## 2. Target User

### 2.1 Jobs To Be Done

- **Collect and complete** — "Help me find and own all 67 monsters; give me a finite, prestige collection I can show off." (functional + emotional)
- **Experience camera magic** — "Make something genuinely cool appear in *my* real space, on *my* coffee table, so I want to show whoever's next to me." (emotional + social)
- **Hunt for loot and progression** — "Give me a reason to fight monsters, not just photograph them — materials, XP, rare variants." (functional)
- **Spend small, idle moments** — "Let me pull out my phone and have a satisfying 2–5 minute encounter wherever I am." (contextual)
- **Spend money only when it feels worth it** — "When I run out mid-session or want a rare monster, let me top up cleanly without feeling cheated." (functional + emotional)

### 2.2 Non-Users (v1)

- Players on **non-ARCore devices** — the AR core loop is the product; a non-AR fallback is explicitly out of scope for MVP. `[ASSUMPTION: AR is "AR Required," not "AR Optional," for MVP — see Open Question OQ-1.]`
- **Under-13 players** — content and store rating target Teen; no child-directed design, no COPPA-scoped features in v1.
- **iOS players** — Android-only at launch.
- **Competitive / PvP-seeking players** — v1 has no real-time multiplayer or direct PvP.

### 2.3 Key User Journeys

- **UJ-1. Maya meets her first monster in her living room.**
  - **Persona + context:** Maya, 19, installed Veilwalkers after seeing a friend's clip; curious, no prior commitment.
  - **Entry state:** First launch, unauthenticated/local, onboarding not yet seen.
  - **Path:** Onboarding shows the premise and grants **20 free credits** → she taps **Enter AR Hunt** → the **AR Safety Warning** appears and she acknowledges it → camera opens, plane detection finds her floor → she spends **1 credit** to **Lure** a Basic Monster → a tall antlered shadow figure resolves on her rug, occluded correctly by her coffee table.
  - **Climax:** She **Captures** it; a Codex entry unlocks with a satisfying reveal. She knows it landed because the monster animates into the Codex and her count reads "1 / 67."
  - **Resolution:** Back on the Home screen, 19 credits left, one monster owned, a clear "find the next one" pull.
  - **Edge case:** If plane detection fails (poor lighting / featureless floor), the app shows a guided "move your phone slowly across a textured surface" coach instead of spawning into empty space.

- **UJ-2. Dev burns through a hot session and tops up.**
  - **Persona + context:** Dev, 24, a hunter type, mid-session in a good spot with lots of surface area.
  - **Entry state:** Authenticated/local session, ~5 credits left, on the AR encounter screen.
  - **Path:** He keeps choosing **Slay (3 credits)** for loot over Capture → drops to 2 credits → a rare monster appears and he wants a **Premium Lure (4 credits)** but can't afford it → a non-blocking prompt offers the **Shop**.
  - **Climax:** He buys the **Hunter Pack (150 + 20 bonus, $4.99)**; credits update immediately and he returns to the *same* encounter without losing it.
  - **Resolution:** He Premium-Lures the rare, slays it, banks rare materials. Spending felt like buying *more of a good time*, not unblocking a wall.
  - **Edge case:** If the purchase fails or is cancelled, he returns to the encounter with credits unchanged and a clear, non-alarming message; no monster or progress is lost.

- **UJ-3. Priya completes a Codex streak before bed.**
  - **Persona + context:** Priya, 31, a completionist; checks in nightly to chip at "67."
  - **Entry state:** Returning player, Home screen, has a daily free-credit reward waiting.
  - **Path:** Claims daily reward → enters AR → uses **Stability Boost (1 credit)** to make a tricky monster easier to **Capture** → fills a Codex gap → opens the **Codex** to admire progress and read the new entry's lore.
  - **Climax:** Her count ticks to "12 / 67"; the detail view shows the monster's art, stats, and flavor text.
  - **Resolution:** She closes the app satisfied, with a visible next gap to chase tomorrow.

## 3. Glossary

*Downstream workflows and readers must use these terms exactly. FRs, UJs, and SMs use these terms verbatim; no synonyms anywhere else in the PRD.*

- **Monster** — One of exactly **67** distinct creatures in the Veilwalkers universe. Has art, stats, rarity, and a Codex entry. MVP ships a subset (3–5) of the 67.
- **Monster Credit (Credit)** — The single soft/hard currency. Granted free (onboarding, daily/ad rewards) or purchased via a Credit Pack. Spent on player actions. No other in-game currency exists in v1.
- **Credit Pack** — A real-money bundle of Credits sold through Google Play Billing (see §Monetization).
- **AR Mode** — The camera-active state where Monsters are rendered into real-world space. Entering it always triggers the AR Safety Warning first.
- **AR Safety Warning** — The mandatory full-screen advisory shown immediately on entering AR Mode, before the camera feed is interactive. A Google Play hard requirement.
- **Lure** — The action of spending Credits to summon a Monster into the current AR view. Variants: **Basic Lure** (1 Credit), **Premium Lure** (4 Credits, higher rare chance), **Multi-Lure** (5 Credits, two Monsters at once).
- **Encounter** — A single active session with one or more lured Monsters, during which the player may Scan, Capture, or Slay.
- **Scan** — A free action that reveals information about a Monster in an Encounter and contributes to its Codex entry.
- **Capture** — The action of adding a Monster to the Codex non-violently. Base attempt is free; **Strong Capture** (2 Credits) raises success rate.
- **Slay** — The action (3 Credits) of defeating a Monster in combat for superior rewards (rare materials, higher XP, exclusive variants/trophies) versus Capture.
- **Stability Boost** — A 1-Credit consumable that makes the current Monster easier to Scan/Capture/Slay for the rest of the Encounter.
- **Nightveil Filter** — A 2-Credit consumable applying an atmospheric visual filter plus a small rarity boost.
- **Retry** — A 1-Credit action to re-attempt a failed Capture or Slay within the same Encounter.
- **Codex** — The player's persistent collection of discovered Monsters, showing progress toward 67 and per-Monster detail entries.
- **The Pit** — A placeable AR miniature Roman colosseum in which **Miniaturized** captured Monsters fight. Signature feature; **post-MVP** (see §4.6 and §6.2).
- **Miniaturize** — The action (1–2 Credits) of shrinking a captured Monster into a fightable token for The Pit. Post-MVP.
- **Rarity** — A Monster's scarcity tier, affecting Lure odds and reward value. `[ASSUMPTION: discrete tiers exist (e.g., Common/Rare/…); exact tier names/count are a design detail — see OQ-2.]`

## 4. Features

*FRs are numbered globally so downstream artifacts have stable references even if features get reorganized. MVP features are §4.1–§4.5. §4.6 (The Pit) is specified but `[NON-GOAL for MVP]`.*

### 4.1 Onboarding & First-Run Economy

**Description:** On first launch the player sees a short onboarding that conveys the premise ("Monsters are closer than you think"), discloses Camera permission usage, and grants the **20 free Credits**. Onboarding ends by routing the player toward their first **Lure**. Realizes UJ-1. The free-credit grant happens exactly once per install. Camera permission is requested with clear disclosure of why it is needed, satisfying Google Play disclosure rules.

**Functional Requirements:**

#### FR-1: First-launch onboarding and free-credit grant
A new player completing first launch is granted exactly **20 Credits** and shown the premise and Camera-usage disclosure. Realizes UJ-1.

**Consequences (testable):**
- A fresh install shows the onboarding flow before any AR Mode entry is possible.
- Credit balance reads exactly 20 immediately after onboarding completes.
- Reinstalling or clearing data and relaunching re-grants 20 Credits (local-only MVP). `[ASSUMPTION: no server-side grant-once enforcement in MVP — see OQ-3.]`
- Camera permission disclosure text is shown before the OS permission dialog.

#### FR-2: Daily / ad-based free credit top-up
A returning player can receive small free Credit rewards via a daily login reward and/or an optional rewarded action. `[ASSUMPTION: design doc lists "daily login rewards or ad rewards" — MVP ships daily login reward; rewarded ads flagged for PM — see OQ-4.]`

**Consequences (testable):**
- The daily reward can be claimed at most once per calendar day per player.
- Claiming credits the player's balance by the configured amount.

**Notes:** `[NOTE FOR PM]` Confirm whether rewarded video ads ship in MVP; ads change the Play data-safety disclosure and SDK footprint.

### 4.2 AR Mode & Safety

**Description:** Entering AR Mode **always** shows the **AR Safety Warning** first (awareness of surroundings, parental guidance), and only after acknowledgement does the camera feed become interactive. AR Mode runs plane detection so Monsters anchor to real surfaces with occlusion and lighting. Realizes UJ-1. This feature carries the Google Play hard requirements for AR safety and camera disclosure.

**Functional Requirements:**

#### FR-3: Mandatory AR Safety Warning gate
The player must acknowledge the AR Safety Warning every time they enter AR Mode before the camera view becomes interactive. Realizes UJ-1.

**Consequences (testable):**
- No Lure or Encounter action is reachable until the warning is acknowledged.
- The warning appears on *every* AR Mode entry, not only the first. `[ASSUMPTION: "immediately on entering AR mode" is read as every entry, per CLAUDE.md hard requirement — see OQ-5.]`

#### FR-4: AR surface anchoring with occlusion and lighting
The system anchors lured Monsters to detected real-world planes with depth occlusion and environmental lighting.

**Consequences (testable):**
- A lured Monster rests on a detected plane (floor/table/ground), not floating in undefined space.
- Real-world foreground objects occlude the Monster where depth data supports it.
- When plane detection has not yet found a surface, the app shows guidance and does not spawn a Monster into empty space (UJ-1 edge case).

#### FR-5: Camera permission handling
The system requests Camera permission with disclosure and degrades gracefully if denied.

**Consequences (testable):**
- Denying Camera permission blocks AR Mode and shows an explanatory screen with a path to re-grant via settings, not a crash or dead end.
- Location permission, if requested, is clearly optional and its denial does not block the core loop. `[ASSUMPTION: location is post-MVP (world spawns are Phase 3); MVP does not request Location — see §6.2.]`

### 4.3 Encounter: Lure, Scan, Capture, Slay

**Description:** The heart of the loop. From AR Mode the player spends Credits to **Lure** a Monster, then chooses among **Scan** (free), **Capture** (free base / **Strong Capture** 2 Credits), and **Slay** (3 Credits), with consumables (**Stability Boost**, **Nightveil Filter**) and **Retry** available to shape odds and outcomes. Captures fill the Codex; Slays yield superior loot. Realizes UJ-1, UJ-2, UJ-3. All Credit costs are taken from the design doc economy and are `[ASSUMPTION: subject to balancing]` per the design doc's own note.

**Functional Requirements:**

#### FR-6: Lure a Monster (Basic / Premium / Multi)
A player with sufficient Credits can Lure a Monster into the current Encounter: Basic (1), Premium (4, higher rare chance), or Multi-Lure (5, two Monsters). Realizes UJ-1, UJ-2.

**Consequences (testable):**
- A Lure deducts exactly its Credit cost only on successful spawn; a failed spawn refunds or does not deduct.
- Premium Lure's rare-tier probability is strictly higher than Basic Lure's.
- Multi-Lure spawns two Monsters in one Encounter.
- A Lure is blocked with a non-blocking top-up prompt when the player lacks Credits (UJ-2).

#### FR-7: Scan a Monster (free)
A player can Scan a Monster in an Encounter to reveal information and progress its Codex entry at no Credit cost. Realizes UJ-3.

**Consequences (testable):**
- Scan never changes Credit balance.
- Scanning reveals at least partial Codex data even before Capture/Slay.

#### FR-8: Capture a Monster (free base / Strong Capture)
A player can attempt to Capture a Monster; base attempt is free, **Strong Capture** (2 Credits) raises success rate. A successful Capture adds the Monster to the Codex. Realizes UJ-1, UJ-3.

**Consequences (testable):**
- A successful Capture creates/updates the Monster's Codex entry and increments the "X / 67" count if newly discovered.
- Strong Capture deducts 2 Credits and applies a higher success probability than the free attempt.
- A failed Capture leaves the Encounter active and offers **Retry**.

#### FR-9: Slay a Monster (combat for superior loot)
A player can Slay a Monster (3 Credits) to defeat it for rewards strictly better than Capture (rare materials, higher XP, exclusive variants/trophies). Realizes UJ-2.

**Consequences (testable):**
- Slay deducts 3 Credits.
- Slay rewards are strictly superior to Capture rewards for the same Monster (materials and/or XP and/or variant).
- `[ASSUMPTION: MVP Slay is "tap-to-slay" / light combat, not a deep combat system — design doc Open Question; see OQ-6.]`

#### FR-10: Encounter consumables and Retry
A player can apply **Stability Boost** (1), **Nightveil Filter** (2), and **Retry** (1) within an Encounter.

**Consequences (testable):**
- Stability Boost raises Scan/Capture/Slay ease for the remainder of the current Encounter and deducts 1 Credit.
- Nightveil Filter applies the visual filter and a small rarity boost and deducts 2 Credits.
- Retry re-enables a failed Capture or Slay attempt and deducts 1 Credit.

**Feature-specific NFRs:**
- Every Credit-spending action must show its cost *before* the spend is committed (no surprise deductions).
- An Encounter survives a Shop round-trip without loss (UJ-2): returning from a purchase resumes the same Encounter.

### 4.4 Codex (Collection)

**Description:** The persistent collection surface. Shows progress toward **67**, a grid/list of discovered Monsters, and a per-Monster **detail view** with art, stats, rarity, and lore/flavor text. Captures and Slays both populate it; Slays may unlock exclusive variants or richer entries. Realizes UJ-1, UJ-3.

**Functional Requirements:**

#### FR-11: Codex collection and progress
A player can view their collection with a visible "X / 67" progress indicator and open any discovered Monster's detail view. Realizes UJ-1, UJ-3.

**Consequences (testable):**
- Discovering a new Monster increments the progress count by exactly one.
- Undiscovered Monsters appear as locked/silhouette slots up to 67. `[ASSUMPTION: locked slots are shown to convey the finite 67 goal — see OQ-7.]`
- The detail view shows art, stats, rarity, and lore for discovered Monsters.

#### FR-12: Local progression persistence
The player's Codex and Credit balance persist across app restarts on-device.

**Consequences (testable):**
- Closing and reopening the app preserves Codex entries and Credit balance.
- `[ASSUMPTION: MVP persistence is local; cloud sync is post-MVP — see §6.2 and OQ-3.]`

### 4.5 Shop & Monetization (Google Play Billing)

**Description:** When the player wants more Credits — typically mid-session (UJ-2) — the **Shop** offers **Credit Packs** through official Google Play Billing only. Purchases credit the balance immediately and return the player to context without losing their Encounter. Realizes UJ-2. No third-party payment path exists.

**Functional Requirements:**

#### FR-13: Credit Pack catalog and purchase
A player can view and purchase Credit Packs via Google Play Billing; a completed purchase credits the balance.

**Consequences (testable):**
- The Shop lists at least the three launch packs (see §Monetization) with prices localized by Play.
- A successful purchase increments the Credit balance by the pack's total (base + bonus) and grants any bundled Rare Lure.
- All purchases route through Google Play Billing; no alternative payment UI exists anywhere in the app.

#### FR-14: Purchase reliability and recovery
Purchases are acknowledged and resilient to interruption; failed/cancelled purchases never charge without crediting and never lose the player's place.

**Consequences (testable):**
- A cancelled or failed purchase leaves the Credit balance unchanged and returns the player to their prior context (UJ-2 edge case).
- A purchase interrupted (network drop, app kill) is reconciled on next launch so a paid-for pack is granted exactly once.
- Purchases are acknowledged per Play Billing requirements within the allowed window (un-acknowledged purchases auto-refund).

**Feature-specific NFRs:**
- `[ASSUMPTION: Server-side purchase verification is strongly recommended but not a hard MVP gate per CLAUDE.md ("Firebase optional for MVP"); MVP may client-acknowledge with verification as fast-follow — see OQ-3.]`

### 4.6 The Pit (Miniature Colosseum) — `[NON-GOAL for MVP]`

**Description:** Veilwalkers' signature differentiator. A placeable AR **miniature Roman colosseum** (dinner-plate scale) into which the player drops **Miniaturized** captured Monsters to fight — automatically (AI) or with light tap-to-attack control. Winners earn XP, rare materials, or exclusive trophies. Credit sinks include Miniaturize (1–2), hosting a fight (1, or free with limits), temporary stat boosts, reviving a defeated miniature, and unlocking arena effects (fire, lightning). Gives captured Monsters a second purpose and creates strong shareable/social moments.

**Status:** Fully specified here for continuity, but **deferred to post-MVP** (Phase 2/3). MVP ships without The Pit. `[NOTE FOR PM]` This is the emotionally load-bearing differentiator — revisit for the *first* post-launch update; it is what distinguishes Veilwalkers from generic AR catch-games.

**Functional Requirements (deferred — not built in MVP):**

#### FR-15 (deferred): Place The Pit and Miniaturize Monsters
A player can place The Pit on a detected plane and Miniaturize captured Monsters (1–2 Credits each) into fight tokens.

#### FR-16 (deferred): Host and resolve Pit fights
A player can host a fight (1 Credit / free with limits) between Miniaturized Monsters, resolved automatically or via light player control, granting rewards to the winner.

#### FR-17 (deferred): Pit credit sinks
A player can spend Credits to boost a fighter's stats, revive a defeated miniature, or unlock arena effects.

## 5. Non-Goals (Explicit)

- **Not a PvP / real-time multiplayer game** in v1 — no live player-vs-player battles, no shared world sessions.
- **Not iOS** — Android only at launch.
- **Not a non-AR game** — there is no 2D/non-camera fallback mode; ARCore is required.
- **Not a gacha/loot-box product** — monetization is transparent Credit Packs, not randomized paid boxes. (Keeps Teen rating and Play compliance clean.)
- **Not a social network** — no friends graph, chat, or in-app feeds in v1 (clip-sharing via OS share sheet is the only social surface considered, and only with The Pit, post-MVP).
- **Not using third-party billing** — all real-money flows are Google Play Billing.
- **Not realistic horror** — no gore, no jump-scare horror; stylized-spooky only (Teen).

## 6. MVP Scope

### 6.1 In Scope
- Onboarding + 20 free Credits + Camera disclosure (FR-1).
- Daily free-credit reward (FR-2). *(Rewarded ads: PM decision, OQ-4.)*
- AR Mode with mandatory AR Safety Warning, plane detection, occlusion, lighting (FR-3, FR-4, FR-5).
- Core Encounter loop: Lure (Basic/Premium/Multi), Scan, Capture (+Strong Capture), Slay, Stability Boost, Nightveil Filter, Retry (FR-6–FR-10).
- **3–5 Monster types** with basic animations (subset of the 67).
- Codex with "X / 67" progress and detail views (FR-11), local persistence (FR-12).
- Shop with Google Play Billing and the three launch Credit Packs (FR-13, FR-14).

### 6.2 Out of Scope for MVP
- **The Pit / Miniaturize** (FR-15–FR-17) — the signature feature; first major post-launch addition. `[NOTE FOR PM]` highest-value deferral; protect it.
- **Slaying as deep combat** — MVP keeps Slay light (tap-to-slay); a richer combat system is Phase 2 (OQ-6).
- **Location-based / GPS world spawns** — Phase 3; MVP does not request Location.
- **Cloud progression sync & server-side purchase verification** — Phase 2 (Firebase); MVP is local-first (OQ-3).
- **Rarity tiers beyond the minimum**, daily events, limited-time Monsters, social/clip-sharing — Phase 2/3.
- **Full 67-Monster roster** — MVP ships 3–5; the rest roll out over post-launch content updates.
- **iOS / non-ARCore support.**

## 7. Success Metrics

*Each SM cross-references the FR(s) it validates. Targets are launch hypotheses to be calibrated.* `[ASSUMPTION: all numeric targets below are proposed defaults for a soft launch and must be confirmed — see OQ-8.]`

**Primary**
- **SM-1 — First-encounter completion:** % of new installs that complete their first Capture or Slay in the first session. Target ≥ 60%. Validates FR-1, FR-3, FR-4, FR-6, FR-8/FR-9. *(The camera-magic moment landing.)*
- **SM-2 — D1 / D7 retention:** Target D1 ≥ 35%, D7 ≥ 15%. Validates the core loop (FR-6–FR-12).
- **SM-3 — Conversion to first purchase:** % of players who buy any Credit Pack within 7 days. Target ≥ 3%. Validates FR-13, FR-14, and the generosity-then-desire economy (FR-1, FR-2, FR-6, FR-9).

**Secondary**
- **SM-4 — Codex engagement:** median Monsters discovered per retained player by D7. Target ≥ 4. Validates FR-7, FR-8, FR-11.
- **SM-5 — Slay/Capture mix:** share of Encounters ending in Slay vs Capture. No single target — used to confirm both playstyles are live (healthy spread, neither < 10%). Validates FR-8, FR-9.
- **SM-6 — AR session quality:** % of AR sessions with successful plane anchoring within N seconds and no AR-related crash. Target ≥ 90%. Validates FR-4.

**Counter-metrics (do not optimize)**
- **SM-C1 — Spend-per-session pressure:** average Credits a player is *forced* to spend to complete a basic Capture loop. Should stay **low**; counterbalances SM-3. If conversion rises because basic play got expensive, that is a failure, not a win.
- **SM-C2 — Refund / billing-complaint rate:** counterbalances SM-3 and SM-6. Rising purchases must not come with rising "charged but no credits" reports (FR-14).
- **SM-C3 — AR safety dismissal speed:** if the AR Safety Warning (FR-3) is being instantly dismissed at ~100% with near-zero dwell, do **not** "optimize" it away — it is a compliance requirement, not a funnel step.

## 8. Open Questions

1. **OQ-1 — AR Required vs AR Optional?** Design doc allows either; CLAUDE.md implies AR-required. Confirm the Play listing flag and whether any non-AR onboarding is needed for unsupported devices.
2. **OQ-2 — Rarity model.** How many rarity tiers, their names, and their Lure-odds curve?
3. **OQ-3 — Backend for MVP.** Ship local-only, or include Firebase for cloud sync + server-side purchase verification at launch? (Security vs. timeline trade-off — recommend at least server-side purchase verification.)
4. **OQ-4 — Rewarded ads in MVP?** Affects Play data-safety disclosure and SDK footprint.
5. **OQ-5 — AR Safety Warning frequency.** Every AR entry, once per session, or once per day? Confirm against Google Play's current AR policy wording.
6. **OQ-6 — Slay combat depth.** Tap-to-slay (MVP) vs a light real combat system. Design doc Open Question.
7. **OQ-7 — Codex presentation.** Bestiary-style (all 67 silhouettes shown) vs personal-journal (only discovered shown). Affects how strongly the "67" goal is felt.
8. **OQ-8 — Metric targets.** Confirm/replace all SM targets against comparable AR-game benchmarks.
9. **OQ-9 — Credit economy balancing.** All costs/pack values are flagged "subject to balancing"; needs an economy simulation pass before launch.
10. **OQ-10 — Privacy policy + camera/data disclosure copy.** Required for Play; who owns drafting and hosting?

## 9. Assumptions Index

- §2.1 / §2.2 / FR-3 — AR is "AR Required" for MVP; no non-AR fallback. (OQ-1)
- §3 / FR-9 — Discrete Rarity tiers exist; exact model TBD. (OQ-2)
- §4.1 FR-1 — No server-side grant-once enforcement in MVP (local re-grant on reinstall). (OQ-3)
- §4.1 FR-2 — MVP ships daily login reward; rewarded ads are a PM decision. (OQ-4)
- §4.2 FR-3 — AR Safety Warning shows on *every* AR Mode entry. (OQ-5)
- §4.2 FR-5 — MVP does not request Location permission (world spawns are Phase 3).
- §4.3 — All Credit costs are "subject to balancing" (design doc's own note). (OQ-9)
- §4.3 FR-9 — MVP Slay is light/tap-to-slay, not deep combat. (OQ-6)
- §4.4 FR-11 — Locked silhouette slots up to 67 are shown to convey the finite goal. (OQ-7)
- §4.4 FR-12 / §4.5 — MVP progression and purchase handling are local-first; cloud sync and server-side verification are post-MVP, with server-side purchase verification recommended as a fast-follow. (OQ-3)
- §7 — All numeric Success Metric targets are proposed soft-launch defaults pending benchmark confirmation. (OQ-8)

---

## Aesthetic & Tone

- **Art style:** Stylized spooky / atmospheric — *not* hyper-realistic gore. Creepy-but-cute-adjacent; Teen-safe.
- **Color palette:** Dark backgrounds with **cyan/teal** accents (signature look).
- **Monster design language:** Tall antlered shadow figures, multi-eyed furry creatures, spider-like beasts — uncanny silhouettes over body horror.
- **Tone / voice:** Mysterious, slightly unsettling — *Gravity Falls* meets *Stranger Things*. Any product-generated copy (tooltips, Codex lore) leans wry-eerie, never gory or genuinely frightening.
- **Anti-references:** Realistic blood/gore, jump-scare horror, photoreal monsters, clinical/flat UI.

## Information Architecture

Top-level surfaces:
- **Home** — entry hub; primary CTA **Enter AR Hunt**; Credit balance; access to Codex and Shop.
- **AR Hunt (AR Mode)** — gated by the AR Safety Warning; hosts the Encounter loop.
- **Codex** — collection grid + Monster detail views; "X / 67" progress.
- **Shop** — Credit Packs via Play Billing.
- **(Post-MVP) The Pit** — placeable colosseum surface.

Cross-cutting overlays: AR Safety Warning (on AR entry), "How to Use Credits" help popup, onboarding (first run), non-blocking top-up prompt (on insufficient Credits).

## Monetization

- **Model:** Free-to-play; single currency (Monster Credits); real money only via **Google Play Billing**.
- **Free grant:** 20 Credits on first launch; small daily/ad top-ups.
- **Launch Credit Packs** *(prices from design doc; `[ASSUMPTION: subject to balancing]`)*:
  - **Starter Pack** — 50 Credits — **$1.99**
  - **Hunter Pack** — 150 Credits + 20 bonus — **$4.99**
  - **Veil Pack** — 400 Credits + 100 bonus + **Rare Lure** — **$9.99**
- **Spend design goal:** basic luring stays cheap; **Premium Lure** and **Slay** are the intended primary money drivers. Avoid aggressive paywalls — the free game must remain enjoyable (guarded by SM-C1).
- **No** gacha/loot boxes, **no** third-party payment, **no** pay-to-win PvP (no PvP at all in v1).

## Platform

- **OS:** Android only (Google Play). iOS out of scope.
- **AR:** Google ARCore via Unity **AR Foundation**; target ARCore-supported devices. AR Required for MVP (pending OQ-1).
- **Engine:** Unity 2022 LTS+.
- **Billing:** Google Play Billing Library.
- **Backend:** Firebase optional for MVP (progression sync, purchase verification) — see OQ-3.
- **Target hardware:** mid-range ARCore Android devices; good plane detection and occlusion are make-or-break (NFR-1).

## Cross-Cutting NFRs

- **NFR-1 — AR performance on mid-range devices:** AR Mode must hold a stable, immersive frame rate on mid-range ARCore devices with plane detection, occlusion, and lighting active. `[ASSUMPTION: target ≥ 30 FPS sustained in AR Mode on the reference mid-range device — confirm device + target with eng.]`
- **NFR-2 — Encounter responsiveness:** Lure-to-spawn and action feedback (Scan/Capture/Slay) must feel immediate (sub-second UI acknowledgement of a spend).
- **NFR-3 — Crash-free AR sessions:** AR Mode must be crash-resilient (SM-6); camera/AR failures degrade to guidance, never to a crash or charged-but-stuck state.
- **NFR-4 — Billing integrity:** exactly-once credit grant per purchase; resilient reconciliation across interruptions (FR-14).
- **NFR-5 — Cold-start to first hunt:** time from app launch to an interactive AR Mode (post-warning) should be short enough not to deter the first-session magic moment. `[ASSUMPTION: target ≤ ~10s on reference device — confirm.]`
- **NFR-6 — Offline tolerance:** the core single-player loop should function without connectivity except where Billing requires it (MVP is local-first).

## Constraints & Guardrails

### Safety
- **AR Safety Warning is mandatory and non-skippable on entering AR Mode** (FR-3) — a Google Play hard requirement. Must convey surroundings-awareness and parental guidance. Cannot be A/B-tested away (SM-C3).
- Encourage breaks / awareness consistent with AR safety norms. `[ASSUMPTION: a one-time stronger advisory at onboarding plus the per-entry warning — confirm with OQ-5.]`

### Privacy
- **Camera permission** is required and must be **clearly disclosed** before the OS prompt (FR-5); a **privacy policy** is required for the Play listing (OQ-10).
- **Location** is optional and post-MVP; if/when added it must be disclosed and non-blocking.
- Camera frames are processed on-device for AR; `[ASSUMPTION: no camera imagery is uploaded or stored server-side in MVP — confirm and reflect in the Play Data Safety form.]`
- Content rating: **Teen** — stylized scary only, no realistic gore/extreme horror (enforced by Aesthetic & Tone).

### Cost
- All real-money flows via Google Play Billing only (FR-13); Play takes its standard cut — pack economics (OQ-9) must account for it.
- Any rewarded-ads decision (OQ-4) adds an ad-SDK and a data-safety disclosure cost; weigh against revenue.
- Optional Firebase backend (OQ-3) adds operational cost; MVP can defer to control burn.

---

## Roadmap (context — not MVP scope)

- **Phase 1 (MVP):** this document's In-Scope set.
- **Phase 2 (Core Polish):** Slay combat system, richer AR (occlusion/lighting/shadows), more Monsters + rarity tiers, daily/free-credit systems, fuller Codex, **The Pit (signature feature)**, cloud sync + server-side purchase verification.
- **Phase 3 (Expansion):** GPS world spawns, social/clip-sharing, events & limited-time Monsters, more credit sinks, progress toward the full 67-Monster roster.
