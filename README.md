# Veilwalkers

> **Monsters are closer than you think.**

An Augmented Reality (AR) monster-hunting & collection game for Android. Players use their phone's camera to lure, scan, capture, and slay monsters that appear anchored in the real world.

## At a glance

| | |
|---|---|
| **Genre** | AR Monster Hunting / Collection |
| **Platform** | Android (Google Play Store) |
| **Engine** | Unity 2022+ with AR Foundation + ARCore |
| **Monetization** | Free-to-play — "Monster Credits" via Google Play Billing |
| **Content rating** | Teen (stylized spooky, no realistic gore) |

## Core loop

Open AR mode → spend **Monster Credits** to **Lure** a monster → **Scan / Capture / Slay** it → collect it in the **Codex** → repeat. Run out of credits → **Shop**.

A standout feature is **The Pit** — a placeable miniature Roman colosseum where captured, miniaturized monsters battle each other in AR.

## Repo structure

```
Veilwalkers/
├── docs/
│   └── game-design-doc.md   — full game design document (v1.0)
├── README.md
├── CLAUDE.md                — working conventions for AI-assisted dev
└── .gitignore               — Unity-tuned
```

The Unity project itself will live at the repo root (`Assets/`, `Packages/`, `ProjectSettings/`) once initialized via Unity Hub.

## Status

Pre-development. Design doc complete; planning underway with BMAD (PRD → architecture → epics/stories).

See [`docs/game-design-doc.md`](docs/game-design-doc.md) for the full concept.
