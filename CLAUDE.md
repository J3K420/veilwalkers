# Veilwalkers — CLAUDE.md

## Working conventions

- **Always commit and push at the end of every session.** Do not leave work uncommitted.
- **Public GitHub repo** (`J3K420/veilwalkers`), same as GraveStory.
- **Never commit secrets** — keystores (`*.jks`/`*.keystore`), `google-services.json`, `keystore.properties`, Play Billing service-account keys. All are gitignored; keep it that way.
- The Unity project is initialized via **Unity Hub**, not by hand-writing project files (Unity generates `Library/`, `.csproj`, `.sln` — all gitignored).

## What this is

Veilwalkers is an Android AR monster-hunting / collection game. The player uses the phone camera to **lure, scan, capture, and slay** monsters anchored in real-world space via AR. Free-to-play, funded by "Monster Credits" (Google Play Billing). Teen-rated, stylized-spooky tone (think *Gravity Falls* / *Stranger Things*).

**Core loop:** AR mode → spend credits to Lure → Scan / Capture / Slay → collect in Codex → Shop when out of credits.

**Signature feature — The Pit:** a placeable miniature Roman colosseum where miniaturized captured monsters fight in AR.

**Lore constraint:** exactly **67** monsters in the universe.

## Tech stack (planned)

| Layer | Technology |
|---|---|
| Engine | Unity 2022 LTS+ |
| AR | AR Foundation + ARCore (Android) |
| Billing | Google Play Billing Library |
| Backend (post-MVP) | Firebase (progression sync, purchase verification) — optional for MVP |
| Target | Mid-range ARCore-supported Android devices |

## Hard requirements (Google Play)

- **AR safety warning** must appear immediately on entering AR mode (mandatory).
- Camera permission required + clearly disclosed; Location optional (world spawns).
- All purchases via official Google Play Billing — no third-party payment.
- Content rating Teen: stylized scary, no realistic gore/extreme horror.

## Credit economy (canon: architecture Economy Model Revision — subject to balancing)

20 free credits on first launch. **Credits buy only 4 actions:** Basic Lure 1 · Premium Lure 4 · Multi-Lure 5 · Slay 3.
**Extras are XP-earned charges, never purchasable:** Strong Capture, Stability Boost, Nightveil Filter — granted by leveling up (Capture/Slay earn XP; Slay earns more), consumed one charge per use. **Retry is free** (just re-attempt). Scan and base Capture are free.
Credit packs: 50/$1.99 · 150+20/$4.99 · 400+100+**Guaranteed-Rare Lure**/$9.99.
(See `docs/architecture.md` §"Economy Model Revision" — it supersedes the design doc's original 8-cost table.)

## Planning method

This project is planned with **BMAD**. Artifacts (PRD, architecture, epics/stories) live in `docs/`. The game design doc (`docs/game-design-doc.md`) is the source input.

## Key references

- Design doc: `docs/game-design-doc.md`
- Precedent for the genre + IAP model on Play: *Monster Hunter Now* (Niantic).
