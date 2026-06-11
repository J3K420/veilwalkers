---
title: Sprint Change Proposal — Pre-Epics Artifact Sync
project: Veilwalkers
date: 2026-06-10
author: James (via BMAD Correct Course)
status: approved
scope_classification: Minor–Moderate (documentation-only; no code, no backlog)
---

# Sprint Change Proposal: Pre-Epics Artifact Sync

## Section 1 — Issue Summary

**Problem statement:** Three product decisions had been ratified in downstream
planning artifacts (architecture, UX) but never propagated back to the upstream
documents (CLAUDE.md, PRD). With no epics/stories authored yet, the project was
about to generate epics from contradictory source documents.

**Discovery context:** Surfaced from the project's memory queue
(`economy-canon`, `ux-feed-upstream-queue`) during a Correct Course pass run
*before* epic creation. This is preventive reconciliation, not a mid-sprint
correction — there are no stories to roll back.

**Evidence — the three drifts:**

| # | Decision (canon) | Source of truth | Stale docs (now fixed) |
|---|---|---|---|
| **A. Economy** | 4 paid actions (Basic/Premium/Multi-Lure, Slay); Strong Capture / Stability Boost / Nightveil = **XP-earned charges**; Retry **free** | `architecture.md` §"Economy Model Revision" | CLAUDE.md 8-cost table; PRD §3 Glossary, FR-8, FR-10, §4.3, §Monetization |
| **B. Rarity (OQ-2)** | **5 ordered tiers**: Common < Uncommon < Rare < Epic < Nightmare; Rare = Guaranteed-Rare threshold | UX `EXPERIENCE.md` Dread Ladder + `DESIGN.md` | PRD OQ-2 (was open), §3 Rarity; architecture L688 4-tier placeholder (`…Legendary`) |
| **C. Codex reveal (OQ-7)** | **Hybrid gradual reveal**: Discovered / `???` silhouette / `?` blank, tier-gated | UX `EXPERIENCE.md` + `DESIGN.md` Codex slot | PRD OQ-7 (was open), FR-11 binary-silhouette assumption |

A secondary inconsistency (FR-13 "Rare Lure" vs architecture's resolved
"Guaranteed-Rare Lure") was cleaned up in the same pass.

## Section 2 — Impact Analysis

- **Epic Impact:** None to *modify* — no epics exist. The impact is that epics
  **must not be authored until this sync lands** (now complete), so they inherit
  consistent canon.
- **Story Impact:** None (no stories).
- **Artifact Conflicts (resolved):**
  - **PRD** — §3 Glossary (Capture, Stability Boost, Nightveil Filter, Retry,
    Rarity + new XP/Charges entry), FR-8, FR-10, §4.3 intro + new Economy Model
    subsection, §Monetization, FR-13, OQ-2, OQ-7, §9 Assumptions Index.
  - **architecture.md** — Rarity enum placeholder → real 5-tier set (L688).
  - **CLAUDE.md** — §Credit economy rewritten to the 4-action + XP-charge model.
  - **UX (DESIGN.md / EXPERIENCE.md)** — **no change**; they are the source of
    truth for B and C and were already correct.
- **Technical Impact:** None — documentation only; architecture's structural
  decisions (EconomyConfig, ordered Rarity enum, IProgressStore) already
  anticipated this canon.

## Section 3 — Recommended Approach

**Selected: Option 1 — Direct Adjustment.** Effort **Low**, Risk **Low**.

Rationale: the decisions were already made and ratified downstream; this is pure
reconciliation. Rollback (Option 2) is N/A — nothing built. MVP Review (Option 3)
is N/A — scope is unaffected (the 4-action model still serves SM-C1/SM-3; tier
count and Codex presentation don't change the MVP feature set). Direct Adjustment
is lowest-effort, lowest-risk, and unblocks clean epic authoring.

## Section 4 — Detailed Change Proposals (all applied)

**Drift A — Economy (CLAUDE.md + PRD):**
- CLAUDE.md §Credit economy → 4 paid actions; extras = XP charges; Retry free;
  Veil Pack = Guaranteed-Rare Lure; pointer to architecture as authoritative.
- PRD §3 Glossary: Capture/Stability Boost/Nightveil/Retry reworded to charges &
  free Retry; new **XP / Charges** entry added.
- PRD FR-8, FR-10: consequences rewritten to charge-consumption + zero-charge
  block + free Retry.
- PRD §4.3 intro reworded; new **Economy Model** subsection added (resolves the
  glossary's "§Economy Model" cross-reference).
- PRD §Monetization spend-design line: canon pointer + charge framing.

**Drift B — Rarity / OQ-2 (PRD + architecture):**
- PRD §3 Rarity entry → 5 ordered tiers; OQ-2 marked RESOLVED (odds curve folds
  into OQ-9 balancing); §9 Assumptions Index updated.
- architecture L688 enum → `Common < Uncommon < Rare < Epic < Nightmare`.

**Drift C — Codex / OQ-7 (PRD):**
- PRD FR-11 consequence → hybrid gradual reveal (3 slot states, tier-gated);
  OQ-7 marked RESOLVED; §9 Assumptions Index updated.

**Cleanup:** PRD FR-13 consequence + Veil Pack line: "Rare Lure" →
"Guaranteed-Rare Lure (forces Rare-or-better)".

## Section 5 — Implementation Handoff

- **Scope classification:** Minor–Moderate (docs-only; no backlog to reorganize).
- **Routed to:** PM / Architect doc owners (James) — **complete**.
- **Next step (unblocked):** author epics & stories
  (`bmad-create-epics-and-stories`) from the now-consistent PRD + architecture + UX.
- **Success criteria:** epics reference the 4-action/XP-charge economy, the 5-tier
  Dread Ladder, and the hybrid Codex reveal with **zero** contradictions against
  any source artifact.

## Residual open items (genuinely open — not drifts)

- **OQ-9** — economy balancing (per-tier Lure-odds curve now folds in here).
- **OQ-4** — rewarded ads in MVP (shape pinned in architecture; build deferred).
- Other PRD open questions (OQ-1, OQ-3, OQ-5, OQ-6, OQ-8, OQ-10) remain as-is.
