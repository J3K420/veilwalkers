---
stepsCompleted: [1, 2, 3, 4]
status: complete
completedAt: 2026-06-10
inputDocuments:
  - docs/prds/prd-Veilwalkers-2026-06-10/prd.md
  - docs/architecture.md
  - docs/ux-designs/ux-Veilwalkers-2026-06-10/EXPERIENCE.md
  - docs/ux-designs/ux-Veilwalkers-2026-06-10/DESIGN.md
---

# Veilwalkers - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for Veilwalkers, decomposing the requirements from the PRD, UX Design (EXPERIENCE.md + DESIGN.md), and Architecture into implementable stories.

## Requirements Inventory

### Functional Requirements

**MVP (Phase 1):**

FR-1: First-launch onboarding grants exactly 20 Credits and shows premise + Camera-usage disclosure (before the OS permission dialog); onboarding precedes any AR Mode entry; reinstall re-grants (local-only MVP).
FR-2: Daily / ad-based free credit top-up — daily login reward claimable at most once per calendar day, crediting the configured amount. (Rewarded ads: PM decision OQ-4; shape pinned — grants a charge, never Credits/XP.)
FR-3: Mandatory AR Safety Warning gate — must be acknowledged on every cold entry to AR Mode before the camera view is interactive; no Lure/Encounter action reachable until acknowledged. (Shop round-trip resume does NOT re-fire it.)
FR-4: AR surface anchoring with occlusion and lighting — lured Monsters rest on detected planes, real-world foreground occludes them; when no plane found, show guidance, never spawn into empty space.
FR-5: Camera permission handling — request with disclosure; denial blocks AR Mode and shows an explanatory re-grant screen (no crash/dead-end). Location not requested in MVP.
FR-6: Lure a Monster — Basic (1 Credit), Premium (4, strictly higher rare chance), Multi-Lure (5, two Monsters). Deduct only on successful spawn; blocked with non-blocking top-up prompt when Credits insufficient.
FR-7: Scan a Monster (free) — reveals information and progresses the Codex entry at no Credit cost; never changes Credit balance.
FR-8: Capture a Monster — base attempt free; Strong Capture consumes one XP-earned charge to raise success (never Credits, blocked at zero charges, never negative); success creates/updates Codex entry; failed Capture leaves Encounter active and offers free Retry.
FR-9: Slay a Monster (3 Credits) — combat for rewards strictly superior to Capture (materials / XP / variants). MVP is light tap-to-slay.
FR-10: Encounter consumables (XP charges) + Retry — Stability Boost and Nightveil Filter each consume one XP-earned charge (never Credits, blocked at zero, never negative); Retry is free re-attempt.
FR-11: Codex collection and progress — visible "X / 67"; new discovery increments by exactly one; hybrid gradual-reveal slot states (Discovered / `???` silhouette for begun tiers / `?` blank for unreached tiers); detail view shows art, stats, rarity, lore.
FR-12: Local progression persistence — Codex, Credit balance, XP/level, and charge counts persist across app restarts on-device.
FR-13: Credit Pack catalog and purchase — view/purchase the 3 launch packs via Google Play Billing only; success credits balance by total (base + bonus) and grants any bundled Guaranteed-Rare Lure (forces Rare-tier-or-better spawn). No alternative payment UI anywhere.
FR-14: Purchase reliability and recovery — cancelled/failed purchases never charge without crediting and never lose the player's place; interrupted purchases reconciled on next launch (granted exactly once); acknowledged per Play Billing within the allowed window.

**Deferred (Post-MVP — The Pit; architect a seam, do NOT build):**

FR-15 (deferred): Place The Pit on a detected plane and Miniaturize captured Monsters (1–2 Credits each) into fight tokens.
FR-16 (deferred): Host and resolve Pit fights (1 Credit / free with limits) between Miniaturized Monsters, granting rewards to the winner.
FR-17 (deferred): Pit credit sinks — boost a fighter's stats, revive a defeated miniature, unlock arena effects.

### NonFunctional Requirements

NFR-1: AR performance — stable, immersive frame rate (target ≥ 30 FPS sustained) on mid-range ARCore devices with plane detection, occlusion, lighting active. Make-or-break. (URP + object pooling + capped concurrent spawns + lightweight shaders/LOD.)
NFR-2: Encounter responsiveness — Lure-to-spawn and action feedback feel immediate; sub-second UI acknowledgement of any Credit spend (spend-ack ≠ materialization duration; the ack confirms instantly even during a multi-second Breach).
NFR-3: Crash-free AR sessions — AR/camera/billing failures degrade to typed results + user-facing guidance, never an uncaught exception, crash, or charged-but-stuck state.
NFR-4: Billing integrity — exactly-once credit grant per purchase (keyed by Play order id); resilient reconciliation across interruptions.
NFR-5: Cold-start to first hunt — short enough to preserve the first-session magic moment. Real budget = launch → first successful plane anchor (target ≤ ~10s on reference device); AR warmup + first-Monster preload happen async during Onboarding/Home.
NFR-6: Offline tolerance — core single-player loop functions without connectivity except where Billing requires it (MVP local-first).

### Additional Requirements

**(Extracted from Architecture — technical/infrastructure/process requirements that shape stories)**

- **AR-1 (Starter / Project Init — FIRST story):** Initialize via Unity Hub "3D (URP)" template, Unity 2022.3 LTS. Pin in manifest.json: `com.unity.xr.arfoundation` 5.0.x, `com.unity.xr.arcore` 5.0.x, `com.unity.purchasing` 5.0.0+ (Unity IAP, Play Billing 8.0.0). Import ARCore Extensions (arf5). Player Settings: Android, AR Required, ARM64, IL2CPP, min API per ARCore. (Legacy Google Play Billing Plugin REJECTED.)
- **AR-2 (Persistence foundation):** Single versioned `SaveModel` (Newtonsoft JSON, camelCase, AES-encrypted) written async + atomic (temp file + swap) to `Application.persistentDataPath`; validate on load; migration path (`schemaVersion`). Holds: credits, codex (discovered IDs + per-monster scan/capture/slay/variant flags), XP/level, per-extra charge counts, encounterSnapshot (AnchorTokens), pending-purchase records, dailyClaim, adReward {grantsToday, dayBucket}, firstZeroCreditDay (write-once scalar). All persistence via `IProgressStore` seam (Local now → Firebase later).
- **AR-3 (Static data):** 67 Monster definitions in ScriptableObjects (art refs, base stats, rarity, lore), NOT in save; MVP populates 3–5. `MonsterDatabase` SO registry. Stable string IDs `monNN`. `Rarity` ordered enum Common < Uncommon < Rare < Epic < Nightmare. At least one Rare+ Monster must be populated (Veil Pack validity).
- **AR-4 (Service layer + composition root):** Services behind C# interfaces — CreditService, ProgressionService, EncounterService, CodexService, BillingService, ArSessionService, SaveService. Resolved via `GameServices` wiring table populated ONCE at App/Bootstrap, read-only after. Pure-logic areas use constructor/Init injection (never `GameServices.Get<T>()`); only MonoBehaviours/UI read the locator. `GameServices.Get<T>()` throws `ServicesNotReadyException` if called before wiring; `ResetForTests()` for isolation.
- **AR-5 (Acyclic boundaries):** Per-area .asmdef enforcing one-way graph `Core ← {Persistence, Economy, Monsters} ← {AR, Encounter, Billing} ← App ← UI`. No service depends on UI/App. No-cycles enforced by `Architecture.Tests`. Shared contracts (`AnchorToken`, `AnchorRestoreResult`) live in `Core/Contracts` (below AR). Codex folded into Monsters. Pit = folder stub, no asmdef.
- **AR-6 (Event-driven UI):** Services raise C# events (OnCreditsChanged, OnMonsterDiscovered, OnPurchaseCompleted, OnInsufficientCredits, OnArSessionInterrupted, OnCodexCompleted); UI subscribes in OnEnable, unsubscribes in OnDisable (symmetric — acceptance criterion). No UI polling.
- **AR-7 (Typed-result spends):** Spend actions return `SpendResult { Success, NewBalance, FailureReason }`, never throw for "can't afford". Currency = integers only.
- **AR-8 (Transactional player action):** One atomic save write per player action (balance/charge delta AND encounter/codex delta committed together); roll back the whole in-memory mutation on persist failure. Never split a spend and its resolution into two persists.
- **AR-9 (Encounter state machine + Shop round-trip):** Explicit EncounterStateMachine (Idle→Lured→Acting→Resolving→Resolved, plus Suspended on AR loss). On Shop entry, AppStateMachine snapshots active Encounter (AnchorTokens + per-monster STATE: scan progress, applied boosts) to memory + disk; rehydrate on return/relaunch state-faithful; Safety Warning does NOT re-fire.
- **AR-10 (Lost-anchor restore):** On rehydrate, `TryRestoreAnchor(token, out AnchorRestoreResult)` → Restored (re-anchor in place) / RelocatedToPlane (re-place on nearest in-frustum plane with "pulled back through the veil" beat, reuse Lure VFX, never vanish) / Failed (guidance, never crash).
- **AR-11 (Insufficient-credits / Shop trigger):** Services never open Shop or call Billing/UI; they raise `OnInsufficientCredits`; App/UI decides Shop navigation (keeps Billing → Economy one-way).
- **AR-12 (Billing pipeline):** All real-money flow in `Billing`; rest of app calls `BillingService.PurchaseAsync(packId)` + listens for `OnPurchaseCompleted`. `PurchaseReconciler`: write pending-ledger → acknowledge → credit exactly once (Play order id) → persist → clear pending; reconcile pending on every launch. Client-trusted (no server verification) — named Phase-2 trigger. `IPurchaseVerifier` seam (Local now → Cloud later).
- **AR-13 (Guaranteed-Rare Lure):** One-shot grantable item from Veil Pack; granted by PurchaseReconciler alongside Credits; stored in SaveModel one-shot inventory; consumed through LureSystem filtering spawn pool to `Rarity >= Rare`.
- **AR-14 (Cold-start staging):** `Bootstrap.LoadPhase { EssentialSync, WarmupAsync, Ready }`. Essential services sync; AR warmup + first-Monster preload on async path, NOT awaited before Home interactive (AC-CS-2). Warmup via `ArSessionService.PrewarmAsync()` (ArSessionService is sole AR lifecycle owner: cold→prewarming→ready→running→paused). Trigger outside owner, lifecycle inside. Device millisecond budget is a release-gate checklist item, not a unit test.
- **AR-15 (Local telemetry — diagnostic, not analytics):** `ITelemetrySink.Count(evt, amount)` (Core); `LocalTelemetrySink` (App). Storage in a SEPARATE `telemetry.json` via `ITelemetryStore` (capped ring), NOT SaveModel. Tracks loop-completions-before-zero, first-zero-credit, session-after-first-zero. `EconomyConfig` holds tunables (adDailyCap, telemetryRetentionDays), never counters. Swappable sink for Phase-2 Firebase.
- **AR-16 (Data-driven economy):** Costs / XP curve / charge thresholds live in `EconomyConfig` ScriptableObject, tunable without rebuild (OQ-9 balancing).
- **AR-17 (Ad-reward seam — stub only):** `App/AdHook.GrantReward()` calls `ProgressionService.AddCharge(...)` (weakest/randomized low-tier charge), never CreditService/XP; daily cap via IClock day-bucket in SaveModel.AdReward; AC-AD-4 test asserts GrantReward never touches Credits or XP. Build deferred (OQ-4).
- **AR-18 (IClock seam):** No direct `Time.time`/`DateTime.Now` in logic; `IClock`/`SystemClock` for daily reward, ad cap, day buckets — enables deterministic tests.
- **AR-19 (Error/logging standard):** Single `GameLog` wrapper (Debug/Info/Warn/Error); no raw `Debug.Log` in shipping logic. Long ops (save, billing, AR init) expose status enum + start/finish events; no silent main-thread blocking.
- **AR-20 (Architecture tests):** `Architecture.Tests` asserts asmdef acyclicity and `MonsterDatabase.Count == 67` lore constraint; charge atomicity test (force IProgressStore.Save throw, assert charge intact); save tamper test (corrupt save → throw, player-facing recovery, never silent wipe).
- **AR-21 (Build/deploy):** Android App Bundle (.aab), IL2CPP, ARM64, AR Required; signing via gitignored keystore + keystore.properties. Google Play tracks (internal → closed → soft launch). Secrets (keystores, google-services.json, keystore.properties, billing keys) stay gitignored.

**Architecture edge cases (become ACs / test cases):**
- Exact-balance spend (balance == cost → 0) succeeds; assert `balance >= cost`.
- Charge at zero blocked with "earn via XP" hint, never negative.
- Codex idempotency: re-capturing an owned Monster does NOT increment "X/67"; 67/67 raises `OnCodexCompleted`.
- Mid-encounter level-up: a charge granted mid-encounter is usable immediately in that encounter.
- Multi-Lure partial fit: if both Monsters can't be placed, spawn what fits + queue/relocate the second; never silently drop or double-charge. [FLAG: confirm desired behavior.]
- Corrupt/tampered save on load: throw (never silent wipe) → player-facing recovery choice (retry / start fresh).
- Mid-session AR loss: `OnArSessionInterrupted` → EncounterStateMachine `Suspended` state + guidance; on recovery attempt TryRestoreAnchor.
- Services-not-ready race: Bootstrap entry scene wires GameServices first; Get<T>() throws ServicesNotReadyException, never null.

### UX Design Requirements

**(From EXPERIENCE.md + DESIGN.md — first-class actionable design work items)**

UX-DR1 (Design tokens — Pumpkin Patch palette): Implement the frame palette as tokens — background `#2E1A47`, surface `#3D2461`, primary-accent/candy-teal `#2BD9C4`, secondary-accent/pumpkin-orange `#FF7A1A`, text-primary `#FFF6E9`, text-muted `#B9A6C9`, danger/SLAY-red `#FF2E4D`, credit-gold `#FFC73A`. Credit-gold ONLY for currency; SLAY-red ONLY for Slay/destructive.
UX-DR2 (Dread-scale tier tokens): Five encounter-only tier color/mood tokens — tier1-common `#7DE0C4`, tier2-uncommon `#4FA8C9`, tier3-rare `#6A5AC9`, tier4-epic `#4B2E6B`, tier5-nightmare `#3A4C3A → #5C2A4A` bruise-rot gradient. Diffused into AR tint/lighting/vignette/SLAY-glow ONLY — never a visible rarity bar/strip/legend. Tier color appears in chrome ONLY on the Codex rarity-tier badge + subtle "caught" stamp.
UX-DR3 (Chunky-button component): `md`/`lg` rounded, 4–5px black outline, `6px 6px 0 #000` hard shadow (no blur), ALL-CAPS title label; primary = pumpkin-orange, secondary = surface + outline; pressed state shrinks shadow offset (e.g. `2px 2px 0`). Slay variant = SLAY-red + skull/slash icon + "SLAY" label (never red alone).
UX-DR4 (Credit pill component): Gold pill, outlined, ⬡ hex icon + integer count; top-right, tappable → Shop; cost always shown before spend; near-empty "felt descent" pulse cyan → dim teal; world never locks at zero (only Lure dimmed with real cost shown); announces balance changes (TalkBack).
UX-DR5 (Codex slot — 3 states): Chunky outlined square; Discovered (full art + tier-tinted "caught" stamp) / `???` silhouette (begun tier) / `?` blank (unreached tier). First-capture fires reveal animation (silhouette→full art + count tick). New-tier unlock beat: silhouettes fade in ("The Veil shows you more…").
UX-DR6 (Pack-card component): Sticker-style `lg` card, heavy outline + hard shadow; credit icon + base + bonus (bonus in pumpkin-orange) + pumpkin-orange Buy button with localized Play price; soft-nudge tags only ("POPULAR" Hunter, "BEST VALUE" Veil); transparent contents, no gacha/mystery-box.
UX-DR7 (Rarity-tier badge): Small `sm` outlined badge color-coded to dread-scale token (T1…T5) on Codex detail pages — the one place tier color lives in chrome.
UX-DR8 (Wordmark): Chunky outlined "Veilwalkers" display lockup with personality (slight wobble), brand anchor on Home + onboarding; never used for body.
UX-DR9 (Hard-shadow elevation system): `6px 6px 0 #000` (no blur) is THE elevation device on every chunky card/button/badge/pill; pressed = shrunk offset. No blurred/soft material shadows or gradient glows on chrome (glow only in AR encounter + SLAY button tier-glow).
UX-DR10 (Materialization system — hero primitive): Tiered entrance per rarity, action bar HIDDEN during materialization (pops in on settle); duration scales T1 ~0.5s → T5 ~2.5–3s; spend-ack stays sub-second regardless (NFR-2). Variants: T1 Pop-in, T2 Unfurl, T3 Seep, T4 Tear, T5 Breach. Ambiance shifts toward active tier (diffused dread scale).
UX-DR11 (T5 Breach FX): Camera feed "corrupts" / cartoon UI briefly glitches, lunge-toward-camera; MUST read as designed FX never a crash (branded glitch, brief, input recovery never blocked); MUST respect reduced-motion setting.
UX-DR12 (Encounter action bar): Scan (free), Capture (free base / Strong Capture consumes earned charge), Slay (3 Credits, SLAY-red + icon + label), plus charge-items (Stability Boost, Nightveil Filter) showing charge count not a credit cost; hidden during materialization, pops in on settle.
UX-DR13 (Non-blocking top-up sheet): Appears as a sheet over the LIVE encounter (one level deep, must not bury the encounter); on purchase credits update immediately + return to exact same encounter state-faithful; on cancel balance unchanged + non-alarming message + nothing lost.
UX-DR14 (Voice & tone — diegetic microcopy): Wry-eerie "Veil" framing — "Part the Veil" (enter AR), "Consult the Veil" (rewarded action), "The Veil shows you more…" (new tier), "Pulled back through the Veil" (anchor restored), "Not yet discovered" (locked slot). EXCEPTION: safety messaging plain/legible, never buried in flavor ("The Veil pulls your attention. Keep one eye on the real world.").
UX-DR15 (State treatments): Cold-start = veil-parting wipe ritual (never spinner/progress bar; slow-device = more ritual). Plane-not-found = friendly coaching, never spawn into empty space. Lost anchor = suspend + "Pulled back through the Veil" restore. Camera denied = explanatory re-grant path, never dead-end. Codex reveal = slot flip + count tick + new-tier silhouette fade-in.
UX-DR16 (Interaction primitives): Tilt-up "Raise your phone to part the Veil" (doubles as plane-detection priming, enters live encounter); tap-to-act on action bar (cost shown before spend); tap-to-slay (light, not deep combat); Lure selection Basic/Premium/Multi (cost shown before spend). Banned: any flow that strands the player.
UX-DR17 (Accessibility floor): Reduced-motion/photosensitivity setting (tames T5 Breach glitch + per-tier ambiance + screen-shake — no flashing, low-motion, glitch→static branded transition). SLAY-red never sole signifier (always + icon + label). Audio never sole carrier of dread (visual/caption cue on stings). Legibility/contrast holds; muted text never for safety. AR safety legibility non-negotiable (plain, high-contrast, never A/B'd away). Tap targets ≥ 48dp. TalkBack: every interactive element labeled role + state; credit pill announces balance; Codex count announces on tick-up.
UX-DR18 (Information architecture / surfaces): Onboarding, Home (wordmark, credit pill, ENTER AR HUNT CTA, Codex X/67 entry, Shop entry, daily reward), AR Hunt (full-bleed camera, chrome floats as chunky islands), Codex (67-grid + detail), Shop. AR Safety Warning + How-to-Use-Credits overlays. Sheets stack one level deep. Single-column mobile, 16dp margins, ≥16px internal padding.
UX-DR19 (Onboarding flow visuals): 2–3 chunky premise cards (the Veil is real / monsters are real / you're a Veilwalker); camera disclosure card BEFORE OS dialog; 20-Credit grant with chunky coin-burst; routes to ENTER AR HUNT.

### FR Coverage Map

FR-1: Epic 1 (grant/persistence logic) + Epic 6 (onboarding UI/disclosure) — first-launch 20-Credit grant + premise + camera disclosure.
FR-2: Epic 1 — daily login reward (once/calendar day) via IClock; ad-reward seam stubbed.
FR-3: Epic 3 — mandatory AR Safety Warning gate on cold AR entry.
FR-4: Epic 3 — plane anchoring with occlusion + lighting + plane-not-found guidance.
FR-5: Epic 3 — camera permission disclosure + graceful denial.
FR-6: Epic 4 — Lure (Basic/Premium/Multi).
FR-7: Epic 4 — Scan (free).
FR-8: Epic 4 — Capture (free base / Strong Capture charge).
FR-9: Epic 4 — Slay (3 Credits, tap-to-slay).
FR-10: Epic 4 — Encounter consumables (charges) + free Retry.
FR-11: Epic 2 — Codex collection, X/67, hybrid reveal, detail views.
FR-12: Epic 1 (save infra) + Epic 2 (codex persistence) — local progression persistence.
FR-13: Epic 5 — Credit Pack catalog + purchase + Guaranteed-Rare Lure grant.
FR-14: Epic 5 — purchase reliability + exactly-once reconciliation.
FR-15: Epic 7 (deferred) — place Pit + Miniaturize. NOT built in MVP.
FR-16: Epic 7 (deferred) — host/resolve Pit fights. NOT built in MVP.
FR-17: Epic 7 (deferred) — Pit credit sinks. NOT built in MVP.

**NFR coverage:** NFR-1 → Epic 3 (URP/pooling/spawn caps) + Epic 4 (spawn budget). NFR-2 → Epic 4 (sub-second spend-ack). NFR-3 → Epic 3 (AR degrade-to-guidance) + cross-cutting (typed results, GameLog). NFR-4 → Epic 5 (exactly-once billing). NFR-5 → Epic 3 (cold-start staging) + Epic 1 (Bootstrap LoadPhase). NFR-6 → Epic 1 (local-first save).

## Epic List

### Epic 1: Foundation — Project, Persistence & Economy Core
Stand up the running Unity project and the invisible spine every feature depends on: the composition root + service layer, encrypted/atomic local save behind `IProgressStore`, the integer Credit ledger, XP/charge progression, and the data-driven `EconomyConfig`. First observable value: a fresh install grants 20 Credits that survive restarts, and the daily reward credits the balance once per day. Stands alone (no AR/UI needed to verify via tests + a debug harness); enables every later epic.
**FRs covered:** FR-1 (grant + persistence), FR-2, FR-12 (save infra)
**Foundational arch:** AR-1, AR-2, AR-4, AR-5, AR-6, AR-7, AR-8, AR-15, AR-16, AR-17 (stub), AR-18, AR-19, AR-20 (acyclicity), AR-21 · **NFR:** NFR-6

### Epic 2: Monster Codex & Collection
Author the 67-Monster static data (ScriptableObjects), the `MonsterDatabase` registry, the ordered `Rarity` enum, and the Codex read-model + UI: visible "X / 67", the 3-state hybrid-reveal grid (Discovered / `???` silhouette / `?` blank, tier-gated), and per-Monster detail views (art, stats, rarity badge, wry lore). Codex idempotency + 67/67 completion hook. Stands alone on Epic 1's persistence; consumed by Epic 4 on Capture.
**FRs covered:** FR-11, FR-12 (codex persistence)
**Arch:** AR-3, AR-20 (Count==67) · **UX:** UX-DR5, UX-DR7

### Epic 3: AR Mode — Safety, Camera & World Anchoring
Enter AR safely and place objects convincingly: the mandatory non-skippable Safety Warning gate (cold-entry rule), camera-permission disclose-before-prompt + graceful denial, plane detection with occlusion + lighting, anchor save/restore (`IArAnchorProvider` + `AnchorRestoreResult`), object-pooled spawning under a frame budget, and the cold-start warmup ritual. Delivers "raise your phone → a placed object rests on your floor, occluded correctly." Verifiable with a placeholder spawn before the full Encounter exists.
**FRs covered:** FR-3, FR-4, FR-5
**NFR:** NFR-1, NFR-3, NFR-5 · **Arch:** AR-10, AR-14, AR-9 (anchor/Suspended portion)

### Epic 4: The Encounter Loop — Lure, Scan, Capture, Slay
The heart of the game: Lure (Basic/Premium/Multi), Scan, Capture (+Strong Capture charge), Slay (tap-to-slay), the charge consumables (Stability Boost, Nightveil Filter) and free Retry — driven by the `EncounterStateMachine` and rendered through the tiered **Materialization** hero primitive + action bar. Captures populate the Codex; Slays yield superior loot. Transactional one-write-per-action. Stands on Epics 1–3.
**FRs covered:** FR-6, FR-7, FR-8, FR-9, FR-10
**NFR:** NFR-2 · **Arch:** AR-8 (per-action atomic), AR-9 (state machine) · **UX:** UX-DR10, UX-DR11, UX-DR12, UX-DR16

### Epic 5: Shop & Monetization (Google Play Billing)
Credit Packs via official Google Play Billing only, exactly-once reconciliation keyed by Play order id, the Guaranteed-Rare Lure grant (Veil Pack), the non-blocking top-up sheet over a live encounter, and the **Encounter-survives-Shop-round-trip** snapshot/rehydrate (UJ-2, state-faithful, Safety Warning does not re-fire). Stands on Epics 1, 4.
**FRs covered:** FR-13, FR-14
**NFR:** NFR-4 · **Arch:** AR-11, AR-12, AR-13, AR-9 (snapshot/rehydrate) · **UX:** UX-DR6, UX-DR13

### Epic 6: Visual Identity, Navigation & Onboarding Shell
The Pumpkin Patch design system as a shared foundation other epics consume: frame + dread-scale tokens, chunky-button / credit-pill / pack-card / Codex-slot components, the hard-shadow elevation device, the wordmark; plus surface navigation (Onboarding → Home → AR/Codex/Shop via `AppStateMachine`), the onboarding flow visuals (premise cards, camera-disclosure card, 20-Credit coin-burst), diegetic Veil voice/tone, state treatments (veil-parting wipe, plane coaching, restore beat), and the accessibility floor (reduced-motion, TalkBack, contrast, ≥48dp, safety-copy legibility). Seeds tokens/components early; feature epics reference them.
**FRs covered:** FR-1 (onboarding/disclosure UI)
**UX:** UX-DR1, UX-DR2, UX-DR3, UX-DR4, UX-DR8, UX-DR9, UX-DR14, UX-DR15, UX-DR17, UX-DR18, UX-DR19

### Epic 7: Post-MVP Seam — The Pit (architecture only, NOT built)
The deferred signature feature. Reserve the seam only: `Assets/Veilwalkers/Pit/` folder stub (no asmdef until real code), a documented "reuse AR session + EncounterService patterns" note, and the carried FR-15–17 specs so the emotionally load-bearing differentiator survives planning. **No MVP stories are implemented in this epic.**
**FRs covered:** FR-15, FR-16, FR-17 (all deferred)

---

## Epic 1: Foundation — Project, Persistence & Economy Core

Stand up the running Unity project and the invisible spine every feature depends on: the composition root + service layer, encrypted/atomic local save behind `IProgressStore`, the integer Credit ledger, XP/charge progression, and the data-driven `EconomyConfig`. First observable value: a fresh install grants 20 Credits that survive restarts, and the daily reward credits the balance once per day.

### Story 1.1: Initialize Unity AR project with pinned packages

As a developer,
I want the Veilwalkers Unity project initialized with the verified URP + AR + IAP package set and Android build settings,
So that all later work builds on a compliant, reproducible foundation.

**Acceptance Criteria:**

**Given** a clean machine with Unity Hub
**When** the project is created from the "3D (URP)" template on Unity 2022.3 LTS
**Then** the project opens and compiles with no errors
**And** `Packages/manifest.json` pins `com.unity.xr.arfoundation` 5.0.x, `com.unity.xr.arcore` 5.0.x, and `com.unity.purchasing` 5.0.0+ (Unity IAP, Play Billing 8.0.0)
**And** ARCore Extensions for AR Foundation (arf5 branch) is imported.

**Given** the project is open
**When** Player Settings are configured
**Then** the platform is Android with AR Required, ARM64, IL2CPP, and Min API per ARCore
**And** the build target produces an Android App Bundle (.aab) configuration.

**Given** repo hygiene rules
**When** the project is committed
**Then** `.gitignore` excludes `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.sln`, `*.keystore`/`*.jks`, `keystore.properties`, `google-services.json`, and billing service-account keys
**And** the legacy Google Play Billing Plugin for Unity is NOT present.

### Story 1.2: Establish assembly boundaries and composition root

As a developer,
I want per-area assemblies, the shared Core contracts, and a single composition root,
So that the one-way dependency graph is enforced and services resolve from one place.

**Acceptance Criteria:**

**Given** the project structure
**When** assemblies are defined
**Then** `.asmdef` files exist for Core, Persistence, Economy, Monsters, AR, Encounter, Billing, App, UI with references forming the one-way graph `Core ← {Persistence, Economy, Monsters} ← {AR, Encounter, Billing} ← App ← UI`
**And** no service assembly references UI or App
**And** the Pit folder exists with NO asmdef.

**Given** `Core/Contracts`
**When** shared contract types are created
**Then** `AnchorToken` and `AnchorRestoreResult` (enum: Restored, RelocatedToPlane, Failed) live in Core (below AR)
**And** `Result`, `SpendResult { Success, NewBalance, FailureReason }`, `IClock`/`SystemClock`, and `GameLog` (Debug/Info/Warn/Error) exist in Core.

**Given** the composition root
**When** `GameServices` is wired at App/Bootstrap
**Then** it is populated once and read-only thereafter
**And** `GameServices.Get<T>()` throws `ServicesNotReadyException` (not null) if called before wiring
**And** `GameServices.ResetForTests()` exists for test isolation
**And** an `Architecture.Tests` test walks the compiled assemblies and fails if any Veilwalkers asmdef references a higher tier.

### Story 1.3: Persist player state with encrypted, atomic local save

As a player,
I want my progress saved reliably on my device,
So that closing the app never loses my Credits or collection.

**Acceptance Criteria:**

**Given** the persistence layer
**When** `SaveModel` is defined and serialized
**Then** it is JSON (Newtonsoft, camelCase) AES-encrypted, with a top-level integer `schemaVersion`
**And** it holds: credit balance (integer), codex (discovered IDs + per-monster scan/capture/slay/variant flags), XP + level, per-extra charge counts, encounterSnapshot, pending-purchase records, dailyClaim date, adReward {grantsToday, dayBucket}, firstZeroCreditDay
**And** all reads/writes go through the `IProgressStore` interface (`LocalProgressStore` implementation).

**Given** a save operation
**When** `SaveAsync` writes
**Then** it writes atomically (temp file + swap) to `Application.persistentDataPath` off the main thread
**And** a process kill mid-write leaves the prior valid save intact.

**Given** a corrupt or tampered save file
**When** the app loads it
**Then** load throws (never silently wipes) and surfaces a player-facing recovery choice (retry / start fresh)
**And** null collections are never persisted (default to empty arrays, validated on load).

### Story 1.4: Grant and spend Credits through the ledger

As a player,
I want a Credit balance I can spend on actions,
So that the in-game economy works with no surprise deductions.

**Acceptance Criteria:**

**Given** `CreditService` behind `ICreditService`
**When** Credits are granted or spent
**Then** balances are integers only and the service raises `OnCreditsChanged`
**And** `TrySpendCredits(cost)` returns a `SpendResult` and never throws for "can't afford".

**Given** a spend attempt where balance < cost
**When** `TrySpendCredits` is called
**Then** it returns `Success = false` with a `FailureReason`, the balance is unchanged, and `OnInsufficientCredits` is raised
**And** the service never opens the Shop or calls Billing/UI itself.

**Given** a spend attempt where balance == cost (exact balance)
**When** `TrySpendCredits` is called
**Then** it succeeds and the new balance is 0 (assert `balance >= cost`).

**Given** any successful spend
**When** the deduction occurs
**Then** the sequence is validate → deduct → persist, and a persist failure rolls the deduction back (balance unchanged).

### Story 1.5: Earn XP and award charges via progression

As a player,
I want to earn XP by playing and receive charges of the extras when I level up,
So that Strong Capture / Stability Boost / Nightveil Filter are earned, never bought.

**Acceptance Criteria:**

**Given** `ProgressionService` (hosted in Economy)
**When** XP is added (Capture grants some; Slay grants more — values from `EconomyConfig`)
**Then** a player-wide XP/level pool updates and crossing a level threshold grants charges via `ChargeInventory` (Strong Capture, Stability Boost, Nightveil Filter)
**And** `AddCharge(...)` increases the relevant charge count.

**Given** `ChargeInventory`
**When** a charge is consumed
**Then** exactly one charge of that type is removed, it never touches Credits, and it never goes below zero
**And** consuming with zero charges is blocked and returns a result indicating "earn via XP."

**Given** a charge consumption
**When** the mutate happens
**Then** it is atomic: mutate-in-memory → persist → roll back on persist failure
**And** an `Architecture.Tests` test forces `IProgressStore.Save` to throw and asserts the charge count is intact.

### Story 1.6: Configure economy values data-drivenly

As a designer,
I want all economy numbers in a tunable config asset,
So that balancing happens without a code change or rebuild.

**Acceptance Criteria:**

**Given** `EconomyConfig` as a ScriptableObject
**When** the economy reads costs/thresholds
**Then** Basic Lure (1), Premium Lure (4), Multi-Lure (5), Slay (3), XP-per-Capture, XP-per-Slay, level/charge thresholds, `adDailyCap`, and `telemetryRetentionDays` are all read from the config
**And** no economy number is hard-coded in service logic
**And** changing a value in the asset changes behavior without recompiling service code.

### Story 1.7: Grant 20 starting Credits on first launch

As a new player,
I want 20 free Credits the first time I open the app,
So that I can start hunting before any paywall.

**Acceptance Criteria:**

**Given** a fresh install with no save file
**When** first launch completes onboarding (the grant logic; onboarding UI is Epic 6)
**Then** the Credit balance reads exactly 20 and is persisted
**And** the grant happens exactly once per install (a save with a "granted" marker is not re-granted on next launch).

**Given** data is cleared / app reinstalled (local-only MVP)
**When** the app launches fresh again
**Then** 20 Credits are re-granted (no server-side grant-once enforcement in MVP).

### Story 1.8: Claim a daily free-credit reward

As a returning player,
I want a daily reward I can claim,
So that I get a small free top-up for coming back.

**Acceptance Criteria:**

**Given** a player who has not claimed today (per `IClock` calendar day)
**When** they claim the daily reward
**Then** the balance increases by the configured amount and `dailyClaim` is recorded for today.

**Given** a player who already claimed today
**When** they attempt to claim again the same calendar day
**Then** the claim is refused and the balance is unchanged.

**Given** the clock seam
**When** the day rolls over (verified by injecting `IClock`)
**Then** the reward becomes claimable again
**And** no logic reads `DateTime.Now` directly.

### Story 1.9: Reserve the ad-reward and telemetry seams (stubs)

As a developer,
I want the OQ-4 ad-reward hook and local telemetry seams reserved now,
So that a future ad grant and balancing pass do not retro-break the economy.

**Acceptance Criteria:**

**Given** the ad-reward seam
**When** `App/AdHook.GrantReward()` is invoked (stub; ads not built)
**Then** it calls `ProgressionService.AddCharge(...)` with the weakest / a randomized low-tier charge and never touches `CreditService` or raw XP
**And** a daily cap is enforced via `SaveModel.AdReward { grantsToday, dayBucket }` using the `IClock` day-bucket integer (never a `DateTime`)
**And** AC-AD-4: a test spy asserts `GrantReward()` fires only `AddCharge` and never touches Credits or XP.

**Given** the telemetry seam
**When** `ITelemetrySink.Count(evt, amount)` is called (`LocalTelemetrySink` in App)
**Then** counters write to a SEPARATE `telemetry.json` via `ITelemetryStore` (capped ring), never to `SaveModel`
**And** the bounded write-once `firstZeroCreditDay` scalar MAY live in `SaveModel`
**And** the sink interface is swappable (Phase-2 Firebase replaces the impl without moving call sites).

---

## Epic 2: Monster Codex & Collection

Author the 67-Monster static data, the registry, the ordered Rarity enum, and the Codex read-model + UI: visible "X / 67", the 3-state hybrid-reveal grid, and per-Monster detail views.

### Story 2.1: Define Monster data and the ordered Rarity model

As a developer,
I want Monster definitions as ScriptableObjects with an ordered rarity tier,
So that static creature data is separate from player state and the rarity floor for Guaranteed-Rare is well-defined.

**Acceptance Criteria:**

**Given** the Monsters area
**When** `MonsterDefinition` and `Rarity` are created
**Then** `MonsterDefinition` is a ScriptableObject holding art refs, base stats, rarity, and lore — and never holds runtime/player state
**And** `Rarity` is an ordered enum `Common < Uncommon < Rare < Epic < Nightmare` with a named `Rare` threshold.

**Given** Monster ID conventions
**When** assets are authored
**Then** each Monster has a stable string ID `monNN` (mon01..mon67) used everywhere (never array index or display name)
**And** asset files follow `<Id>_<Name>.asset` (e.g., `Mon01_AntleredShade.asset`).

### Story 2.2: Register the 67-Monster database with MVP content

As a developer,
I want a `MonsterDatabase` registry sized to the 67-Monster lore constraint with 3–5 populated for MVP,
So that the Codex has a complete catalog shape and the Veil Pack is valid.

**Acceptance Criteria:**

**Given** `MonsterDatabase` as an SO registry
**When** it is populated
**Then** it accounts for the full 67-Monster universe and 3–5 Monsters are fully populated for MVP
**And** an `Architecture.Tests` assertion verifies the catalog/lore count is exactly 67.

**Given** the MVP populated subset
**When** content is validated
**Then** at least one populated Monster has `Rarity >= Rare` (content acceptance criterion for Veil Pack / Guaranteed-Rare Lure validity).

### Story 2.3: Read and persist Codex collection state

As a player,
I want my discovered Monsters tracked persistently,
So that my collection is remembered and progress counts correctly.

**Acceptance Criteria:**

**Given** `CodexService` (folded in Monsters) as a read model over owned-monster state + the catalog
**When** a Monster is first discovered (via Capture or Slay)
**Then** its Codex entry is created/updated in `SaveModel` and `OnMonsterDiscovered` is raised
**And** discovering a new Monster increments the "X / 67" count by exactly one.

**Given** an already-owned Monster
**When** it is captured/slain again (idempotency)
**Then** the "X / 67" count does NOT increment.

**Given** the collection reaches 67/67
**When** the final Monster is discovered
**Then** `OnCodexCompleted` is raised.

**Given** persisted Codex state
**When** the app is closed and reopened
**Then** all Codex entries and the progress count are preserved.

### Story 2.4: Browse the 67-grid with hybrid gradual reveal

As a player,
I want a collection grid that shows what I've caught and hints at what's near without spoiling top tiers,
So that completing the Codex feels like a finite, escalating goal.

**Acceptance Criteria:**

**Given** the Codex grid
**When** it renders the 67 slots
**Then** each slot shows one of three states: Discovered (full art + tier-tinted "caught" stamp), `???` silhouette (undiscovered in a tier the player has *begun* encountering), or `?` blank (undiscovered in a tier not yet reached)
**And** the visible "X / 67" progress indicator is shown.

**Given** the player climbs the Dread Ladder (begins encountering a higher tier)
**When** that tier becomes "begun"
**Then** its previously-blank slots upgrade to `???` silhouettes ("The Veil shows you more…")
**And** higher unreached tiers remain `?` blank (preserving the reveal).

**Given** a first discovery resolves
**When** the Codex updates
**Then** the slot flips silhouette→full art with a tier-colored "caught" stamp and the count ticks up (e.g., "11 / 67 → 12 / 67").

### Story 2.5: View a Monster's detail entry

As a player,
I want to open a discovered Monster and read its details,
So that I can admire the art and lore I've collected.

**Acceptance Criteria:**

**Given** a discovered Monster slot
**When** the player taps it
**Then** a detail view shows the Monster's art, stats, rarity-tier badge (color-coded to its dread-scale tier token), wry-eerie lore, and first-discovered date.

**Given** an undiscovered slot (`???` or `?`)
**When** the player taps it
**Then** no detail entry is shown (only "Not yet discovered." copy), preserving the reveal.

---

## Epic 3: AR Mode — Safety, Camera & World Anchoring

Enter AR safely and place objects convincingly: the mandatory Safety Warning gate, camera-permission flow, plane detection with occlusion + lighting, anchor save/restore, pooled spawning under a frame budget, and the cold-start warmup ritual.

### Story 3.1: Disclose and request camera permission gracefully

As a player,
I want a clear explanation before the camera permission prompt and a way to recover if I deny it,
So that I understand why the camera is needed and never hit a dead end.

**Acceptance Criteria:**

**Given** a player who has not granted camera permission
**When** they head toward AR Mode
**Then** an in-app disclosure of camera usage is shown BEFORE the OS permission dialog (`CameraPermissionFlow`).

**Given** the player denies camera permission
**When** they try to enter AR Mode
**Then** AR Mode is blocked and an explanatory re-grant screen with a path to system settings is shown — never a crash or dead end.

**Given** Location permission
**When** the MVP runs
**Then** Location is never requested (world spawns are Phase 3).

### Story 3.2: Gate AR entry behind the mandatory Safety Warning

As a player,
I want a safety warning every time I enter AR,
So that I stay aware of my surroundings (a Google Play requirement).

**Acceptance Criteria:**

**Given** a cold entry into AR Mode
**When** the player enters
**Then** the AR Safety Warning (`ArSafetyGate`) appears and the camera view is not interactive until acknowledged
**And** no Lure or Encounter action is reachable until acknowledgement
**And** safety copy is plain, high-contrast, and never buried in flavor (cannot be A/B'd away).

**Given** the first entry (and first per session)
**When** the warning shows
**Then** it is a deliberate full read; later cold entries show it as a fast on-brand card (mandatory presence, minimal dwell).

**Given** a resume after a Shop round-trip
**When** AR resumes
**Then** the full warning does NOT re-fire (cold-entry rule only).

### Story 3.3: Own the AR session lifecycle with prewarm

As a player,
I want AR to be ready the moment I commit to hunting,
So that the first-session magic moment isn't lost to loading.

**Acceptance Criteria:**

**Given** `ArSessionService` as the sole owner of the AR lifecycle
**When** the session runs
**Then** it transitions cold → prewarming → ready → running → paused and is the only place AR is started/torn down (no hand-rolled session elsewhere).

**Given** the staged cold-start budget
**When** the app boots (`Bootstrap.LoadPhase { EssentialSync, WarmupAsync, Ready }`)
**Then** essential services wire synchronously while AR warmup + first-Monster preload run on the async path
**And** AC-CS-2: AR warmup + preload are NOT awaited before Home is interactive.

**Given** the AR-entry affordance is visible/likely
**When** prewarm is triggered (by Onboarding/Home calling `ArSessionService.PrewarmAsync()`, not warming the session themselves)
**Then** the session can cheaply tear back to cold if the player backs out, and backgrounding / permission-race / double-start are all handled inside `ArSessionService`.

### Story 3.4: Detect planes and anchor objects with occlusion and lighting

As a player,
I want objects to rest convincingly on my real surfaces,
So that the AR illusion holds.

**Acceptance Criteria:**

**Given** plane detection (`PlaneAnchorService`)
**When** a surface is detected and an object is placed
**Then** it rests on a detected plane (floor/table/ground), is occluded by real-world foreground objects where depth supports it, and is environmentally lit.

**Given** no plane has been found yet
**When** the player aims
**Then** friendly coaching is shown ("Move your phone slowly across a textured surface") and no object is spawned into empty space.

**Given** the performance budget (NFR-1)
**When** objects spawn
**Then** spawning uses `MonsterSpawner` object pooling with capped concurrent spawns under URP, targeting ≥30 FPS sustained on mid-range devices.

### Story 3.5: Save and restore anchors across interruption

As a player,
I want a placed object to come back if AR hiccups or I leave and return,
So that I never lose my encounter to a lost anchor.

**Acceptance Criteria:**

**Given** `IArAnchorProvider`
**When** an anchor is saved
**Then** it produces a serializable `AnchorToken` (trackable id + session-relative pose) storable in `EncounterSnapshot`/`SaveModel`.

**Given** a restore attempt `TryRestoreAnchor(token, out AnchorRestoreResult)`
**When** the result is `Restored`
**Then** the object re-anchors in place.

**Given** the result is `RelocatedToPlane`
**When** the anchor was lost
**Then** the object re-places on the nearest plane within the current camera frustum (fallback to absolute-nearest only if nothing is in view) with a brief "pulled back through the veil" beat (reusing Lure VFX) — it never vanishes.

**Given** the result is `Failed`, or the session is interrupted mid-session
**When** AR loss occurs
**Then** `OnArSessionInterrupted` is raised, the encounter shows guidance (never a crash, NFR-3), and the encounter can enter a suspended/paused state pending recovery.

---

## Epic 4: The Encounter Loop — Lure, Scan, Capture, Slay

The heart of the game: Lure variants, Scan, Capture (+Strong Capture), Slay, charge consumables and free Retry — driven by the Encounter state machine and rendered through the tiered Materialization primitive + action bar.

### Story 4.1: Drive the encounter with an explicit state machine

As a developer,
I want an explicit encounter state machine,
So that actions are well-ordered and the encounter can be suspended and resumed faithfully.

**Acceptance Criteria:**

**Given** `EncounterStateMachine`
**When** an encounter runs
**Then** it moves through Idle → Lured → Acting → Resolving → Resolved, with a `Suspended` state on AR loss
**And** `EncounterService` exposes the FR-6–10 actions and receives dependencies via constructor/Init injection (not the service locator).

**Given** any single player action (Lure/Slay/Capture/extra)
**When** it resolves
**Then** the balance/charge delta AND the encounter/codex delta are committed in ONE atomic save write
**And** a persist failure rolls back the whole in-memory mutation (never deduct-and-resolve in two separate persists).

**Given** AR interruption mid-encounter
**When** `OnArSessionInterrupted` fires
**Then** the machine enters `Suspended` and shows guidance; on recovery it attempts anchor restore per Epic 3.5.

### Story 4.2: Lure a Monster (Basic / Premium / Multi)

As a player,
I want to spend Credits to summon Monsters,
So that I have something to hunt.

**Acceptance Criteria:**

**Given** sufficient Credits and a detected plane
**When** the player chooses Basic (1), Premium (4), or Multi-Lure (5)
**Then** the cost is shown before the spend, the spend-ack confirms sub-second on the credit pill, and Credits deduct only on successful spawn (a failed spawn does not deduct / is refunded).

**Given** Premium vs Basic Lure
**When** rarity is rolled
**Then** Premium's rare-tier probability is strictly higher than Basic's.

**Given** Multi-Lure
**When** it resolves
**Then** two Monsters spawn in one encounter; if both cannot be placed on available planes, spawn what fits and queue/relocate the second — never silently drop or double-charge. [FLAG: confirm desired behavior with PM.]

**Given** insufficient Credits
**When** the player attempts a Lure
**Then** the Lure is blocked and `OnInsufficientCredits` is raised (App/UI decides the top-up prompt); the world does not lock.

### Story 4.3: Scan a Monster for free

As a player,
I want to scan a Monster at no cost,
So that I learn about it and progress its Codex entry without spending.

**Acceptance Criteria:**

**Given** a settled Monster in an encounter
**When** the player taps Scan
**Then** information is revealed, partial Codex data is recorded even before Capture/Slay, and the Credit balance never changes.

### Story 4.4: Capture a Monster (free base / Strong Capture)

As a player,
I want to capture Monsters gently, optionally boosting my odds with an earned charge,
So that I can fill the Codex.

**Acceptance Criteria:**

**Given** a settled Monster
**When** the player taps Capture (base, free)
**Then** a success adds/updates the Monster's Codex entry (via Epic 2) and increments "X / 67" if newly discovered; the Credit balance never changes.

**Given** the player chooses Strong Capture
**When** they have ≥1 Strong-Capture charge
**Then** exactly one charge is consumed (never Credits), a higher success probability than the free attempt is applied
**And** with zero charges it is blocked with an "earn via XP" hint and never goes negative.

**Given** a failed Capture
**When** the attempt resolves
**Then** the encounter stays active and a free Retry is offered (re-attempt, no Credit cost).

### Story 4.5: Slay a Monster for superior loot

As a player,
I want to slay a Monster for better rewards,
So that the hunter playstyle pays off over collecting.

**Acceptance Criteria:**

**Given** a settled Monster and ≥3 Credits
**When** the player taps Slay (tap-to-slay, light combat)
**Then** the cost (3 Credits) is shown before spend, 3 Credits are deducted, and rewards are strictly superior to Capture for the same Monster (materials and/or XP and/or variant)
**And** Slay grants more XP than Capture (per `EconomyConfig`).

**Given** a failed Slay
**When** the attempt resolves
**Then** the encounter stays active and a free Retry is offered.

### Story 4.6: Apply encounter charges (Stability Boost / Nightveil Filter)

As a player,
I want to spend earned charges to shape an encounter,
So that I can improve tough odds without paying money.

**Acceptance Criteria:**

**Given** ≥1 Stability Boost charge
**When** the player applies it
**Then** one charge is consumed (never Credits) and Scan/Capture/Slay ease is raised for the remainder of the current encounter.

**Given** ≥1 Nightveil Filter charge
**When** the player applies it
**Then** one charge is consumed (never Credits), an atmospheric visual filter is applied, and a small rarity boost is granted.

**Given** zero charges of an extra
**When** the player attempts to use it
**Then** it is blocked with an "earn via XP" hint and the count never goes negative.

**Given** a level-up that grants a charge mid-encounter
**When** the level-up happens
**Then** the new charge is usable immediately in that same encounter.

### Story 4.7: Materialize Monsters with tiered, accessible entrances

As a player,
I want Monsters to arrive with a tiered cinematic entrance,
So that rare Monsters feel like events and the camera-magic moment lands.

**Acceptance Criteria:**

**Given** a Lure resolves to a Monster of a given tier
**When** it materializes
**Then** the action bar is HIDDEN during materialization and pops in only when the Monster settles
**And** the entrance variant + duration scales with tier: T1 Pop-in ~0.5s, T2 Unfurl ~1s, T3 Seep ~1.5s, T4 Tear ~2s, T5 Breach ~2.5–3s
**And** AR ambiance (tint/lighting/vignette/SLAY-glow) shifts toward the active tier — never shown as a literal rarity bar.

**Given** a T5 Nightmare Breach
**When** it plays
**Then** the camera feed/cartoon UI glitch reads as a designed branded FX (brief, input recovery never blocked), never a crash.

**Given** the reduced-motion / photosensitivity setting is ON
**When** any materialization plays
**Then** glitch/screen-shake/strobe is tamed to a non-flashing low-motion entrance, and the dread still reads without strobe/shake
**And** the credit spend-ack still confirms sub-second regardless of materialization duration (NFR-2).

---

## Epic 5: Shop & Monetization (Google Play Billing)

Credit Packs via official Google Play Billing only, exactly-once reconciliation, the Guaranteed-Rare Lure grant, the non-blocking top-up sheet, and the Encounter-survives-Shop-round-trip snapshot/rehydrate.

### Story 5.1: Browse and purchase Credit Packs via Google Play Billing

As a player,
I want to buy Credit Packs through the official store,
So that I can top up cleanly when I want more Credits.

**Acceptance Criteria:**

**Given** the Shop (`CreditPackCatalog` + UI)
**When** it renders
**Then** it lists at least the three launch packs — Starter (50 / $1.99), Hunter (150 + 20 bonus / $4.99, "POPULAR"), Veil (400 + 100 bonus + Guaranteed-Rare Lure / $9.99, "BEST VALUE") — with prices localized by Play and transparent contents (no gacha/mystery-box).

**Given** a purchase
**When** it routes
**Then** it goes exclusively through Google Play Billing (Unity IAP 5 / Play Billing 8); no alternative payment UI exists anywhere in the app.

**Given** a successful purchase
**When** it completes
**Then** the balance increments by the pack total (base + bonus) and `OnPurchaseCompleted` is raised; the rest of the app calls only `BillingService.PurchaseAsync(packId)` and listens for the event (Billing → Economy stays one-way).

### Story 5.2: Reconcile purchases exactly once

As a player,
I want every pack I pay for granted exactly once even if something interrupts the purchase,
So that I'm never charged without Credits or granted twice.

**Acceptance Criteria:**

**Given** `PurchaseReconciler`
**When** a purchase is processed
**Then** the sequence is: write to local pending-ledger → acknowledge → credit exactly once keyed by Play order id → persist → clear pending.

**Given** an interrupted purchase (network drop / app kill before crediting)
**When** the app next launches
**Then** pending purchases are reconciled and a paid-for pack is granted exactly once (never twice).

**Given** a cancelled or failed purchase
**When** it resolves
**Then** the balance is unchanged, a non-alarming message is shown, and the player returns to their prior context with nothing lost.

**Given** Play Billing acknowledgement rules
**When** a purchase is made
**Then** it is acknowledged within the allowed window (un-acknowledged purchases auto-refund).

### Story 5.3: Grant and consume the Guaranteed-Rare Lure

As a player who bought the Veil Pack,
I want a guaranteed Rare-or-better Lure,
So that the top pack delivers a distinct, premium outcome.

**Acceptance Criteria:**

**Given** a Veil Pack purchase
**When** it is reconciled
**Then** a `GuaranteedRareLure` one-shot item is granted alongside the Credits and stored in `SaveModel`'s one-shot inventory.

**Given** the player consumes the Guaranteed-Rare Lure
**When** it resolves through the existing Lure path (`LureSystem`)
**Then** the spawn pool is filtered to `Rarity >= Rare` and a Rare-tier-or-better Monster spawns
**And** the item is consumed exactly once.

### Story 5.4: Top up mid-encounter without losing the encounter

As a player,
I want to buy Credits during an encounter and return to the exact same moment,
So that spending feels like buying more fun, not unblocking a wall (UJ-2).

**Acceptance Criteria:**

**Given** an insufficient-credit Lure/Slay attempt in a live encounter
**When** `OnInsufficientCredits` fires
**Then** App/UI presents a non-blocking top-up sheet over the live encounter (one level deep) that does not bury the encounter behind it; the world does not lock.

**Given** the player buys a pack from the top-up sheet
**When** the purchase completes
**Then** Credits update immediately and the player returns to the exact same encounter, state-faithful (lured Monsters, per-monster scan progress, applied boosts intact).

**Given** the Shop round-trip
**When** the player leaves to Shop and returns
**Then** `AppStateMachine` snapshots the active encounter (AnchorTokens + per-monster state) to memory + disk before Shop and rehydrates it on return/relaunch
**And** the AR Safety Warning does NOT re-fire on resume.

**Given** an app kill mid-purchase
**When** the app relaunches
**Then** the snapshot rehydrates the exact encounter AND the purchase reconciles exactly once (Epic 5.2).

---

## Epic 6: Visual Identity, Navigation & Onboarding Shell

The Pumpkin Patch design system as shared foundation, surface navigation, onboarding visuals, diegetic voice/tone, state treatments, and the accessibility floor.

### Story 6.1: Implement the Pumpkin Patch design tokens

As a developer,
I want the frame and dread-scale color/spacing/shape tokens defined once,
So that every surface is consistent and tier color is used correctly.

**Acceptance Criteria:**

**Given** the design token set
**When** tokens are implemented
**Then** the frame palette exists — background `#2E1A47`, surface `#3D2461`, primary-accent/candy-teal `#2BD9C4`, secondary-accent/pumpkin-orange `#FF7A1A`, text-primary `#FFF6E9`, text-muted `#B9A6C9`, danger/SLAY-red `#FF2E4D`, credit-gold `#FFC73A`
**And** spacing (4/8/12/16/24/32), corner radii (sm 10 / md 18 / lg 28 / pill 999), and the hard-shadow elevation (`6px 6px 0 #000`, no blur) are tokenized.

**Given** the five dread-scale tier tokens (T1 `#7DE0C4` … T5 bruise-rot `#3A4C3A → #5C2A4A`)
**When** they are used
**Then** they appear only in the AR encounter treatment and on the Codex rarity-tier badge / "caught" stamp — never as a visible rarity bar/strip/legend in chrome
**And** credit-gold is used only for currency, SLAY-red only for Slay/destructive.

### Story 6.2: Build the chunky shared component kit

As a developer,
I want the reusable chunky components,
So that feature epics consume one consistent component set instead of re-building UI.

**Acceptance Criteria:**

**Given** the component kit
**When** components are built
**Then** the kit includes: chunky button (4–5px outline, hard shadow, ALL-CAPS, pumpkin-orange primary / surface secondary, pressed = shrunk shadow offset; SLAY variant = SLAY-red + skull/slash icon + "SLAY" label), credit pill (gold, ⬡ + integer, tap → Shop, "felt descent" pulse, world-never-locks), pack card (sticker `lg`, bonus in pumpkin-orange, localized Buy, soft-nudge tags), Codex slot (3 states), rarity-tier badge, and the wordmark
**And** every card/button/badge/pill carries the black outline + hard offset shadow as the elevation device (no blurred/soft shadows or gradient glows on chrome).

### Story 6.3: Navigate between surfaces via the app state machine

As a player,
I want to move between Home, AR Hunt, Codex, and Shop,
So that I can reach every part of the game.

**Acceptance Criteria:**

**Given** `AppStateMachine` in the App/Bootstrap tier
**When** the app runs
**Then** navigation flows Onboarding → Home → AR Hunt / Codex / Shop, with Home showing the wordmark, credit pill, the ENTER AR HUNT CTA, Codex (X/67) entry, Shop entry, and the daily-reward affordance.

**Given** an `OnInsufficientCredits` event
**When** App/UI reacts
**Then** `AppStateMachine` (not a service) decides Shop/top-up navigation.

**Given** the AR Hunt surface
**When** it renders
**Then** it is full-bleed camera with chrome (action bar, credit pill) floating as discrete chunky islands, never a docked frame; sheets/overlays stack one level deep.

### Story 6.4: Onboard a new player and disclose the camera

As a new player,
I want a short, on-brand onboarding that explains the premise and the camera before granting my Credits,
So that I understand the game and the camera use before hunting.

**Acceptance Criteria:**

**Given** a first launch
**When** onboarding runs
**Then** 2–3 chunky premise cards convey the premise (the Veil is real / monsters are real / you're a Veilwalker)
**And** a camera-disclosure card appears BEFORE the OS dialog
**And** the 20-Credit grant (Epic 1.7) is presented with a chunky coin-burst, then onboarding routes to ENTER AR HUNT.

**Given** onboarding is incomplete
**When** the player tries to reach AR Mode
**Then** AR Mode is not reachable until onboarding completes.

### Story 6.5: Apply diegetic voice and on-brand state treatments

As a player,
I want the app's copy and loading/error states to feel like one wry-eerie world,
So that even waiting and errors stay in-brand (and safety copy stays plain).

**Acceptance Criteria:**

**Given** system moments
**When** copy is shown
**Then** it uses the diegetic Veil voice — "Part the Veil" (enter AR), "Consult the Veil" (rewarded action), "The Veil shows you more…" (new tier), "Pulled back through the Veil" (anchor restored), "Not yet discovered." (locked slot)
**And** safety messaging is the explicit exception: plain, legible, high-contrast, never buried in flavor.

**Given** state treatments
**When** the app loads or recovers
**Then** cold-start/AR-warmup shows a "veil-parting wipe" ritual (never a spinner/progress bar; slow-device fallback = more ritual, never a percentage)
**And** plane-not-found shows friendly coaching, lost-anchor shows the "Pulled back through the Veil" restore, camera-denied shows the re-grant path, and a Codex first-discovery shows the slot flip + count tick + new-tier silhouette fade-in.

### Story 6.6: Meet the accessibility floor

As a player with accessibility needs,
I want reduced-motion, screen-reader, contrast, and tap-target support,
So that the game is playable and the dread still reads safely.

**Acceptance Criteria:**

**Given** the reduced-motion / photosensitivity setting
**When** it is ON
**Then** the T5 Breach glitch + camera-corruption, per-tier ambiance intensity, and screen-shake are tamed (no flashing; glitch replaced by a static branded transition; dread still reads without strobe/shake).

**Given** non-visual / non-auditory players
**When** dread and danger are conveyed
**Then** SLAY-red is never the sole signifier (always + icon + label), and materialization audio stings always have a visual/caption cue (audio is never the sole carrier of dread).

**Given** Android accessibility
**When** the UI is used
**Then** tap targets are ≥ 48dp, text holds contrast on the dark surfaces (muted text never used for safety copy), dynamic type does not truncate chunky controls
**And** with TalkBack, every interactive element is labeled with role + state, the credit pill announces balance changes, and the Codex count announces on tick-up.

---

## Epic 7: Post-MVP Seam — The Pit (architecture only, NOT built)

The deferred signature feature. This epic reserves the seam only — no MVP gameplay is implemented. It exists so the emotionally load-bearing differentiator survives planning and so MVP code leaves a clean place for it.

### Story 7.1: Reserve the Pit seam without building it

As a developer,
I want the Pit's place in the architecture reserved and its specs carried forward,
So that the post-MVP signature feature can be added later without retrofitting the MVP.

**Acceptance Criteria:**

**Given** the project structure
**When** the Pit seam is reserved
**Then** `Assets/Veilwalkers/Pit/` exists as a folder stub with NO asmdef (an empty boundary is not created until real code exists)
**And** no FR-15/FR-16/FR-17 gameplay is implemented in MVP.

**Given** the MVP AR + Encounter code
**When** it is written
**Then** `ArSessionService` and `EncounterService` patterns are kept reusable so the Pit can later reuse AR session + Encounter patterns (documented note), with no MVP dependency on the Pit.

**Given** the deferred specs
**When** planning continues
**Then** FR-15 (place Pit + Miniaturize 1–2 Credits), FR-16 (host/resolve fights 1 Credit/free with limits), and FR-17 (Pit credit sinks: boost / revive / arena effects) remain documented as post-MVP scope.

