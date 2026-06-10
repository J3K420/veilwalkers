# PRD Quality Review — Veilwalkers

## Overall verdict
A coherent, build-ready MVP PRD with a clear thesis (generous-then-desire economy + finite 67-Monster collection + hunter/collector split) and disciplined scope. The strongest move is protecting The Pit as a fully-specified but explicitly deferred differentiator rather than letting it bleed into MVP. Main risks are all *open*, not hidden: the economy is unbalanced by the author's own admission, several compliance/backend decisions (AR-warning frequency, server-side purchase verification, ads) are deferred to PM, and all metric targets are unvalidated guesses. None of these block downstream architecture/story work; they are tracked in Open Questions.

## Decision-readiness — strong
Decisions are stated as decisions (AR-required, Android-only, no PvP, no gacha, The Pit deferred) and the give-up is named (e.g. "Not iOS," "no non-AR fallback," local-first trades safety for timeline). `[NOTE FOR PM]` callouts sit at real tensions — The Pit deferral, rewarded-ads decision, backend choice — not at safe checkpoints. Open Questions are genuinely open (OQ-3 backend, OQ-6 combat depth) rather than rhetorical.

### Findings
- **low** Local re-grant of 20 credits on reinstall (FR-1) is an exploit surface (OQ-3) — acknowledged but worth a PM star. *Fix:* note in OQ-3 that grant-once enforcement is the cheapest server-side win.

## Substance over theater — strong
Three personas, each driving real decisions (Maya → first-session magic / SM-1; Dev → mid-session top-up / billing resilience FR-14; Priya → Codex completion / SM-4). No persona padding. Vision is product-specific (could not swap into another PRD — the 67 constraint and hunter/collector split are load-bearing). NFRs carry product-specific thresholds (mid-range FPS, exactly-once billing) rather than "must be scalable" boilerplate, though several thresholds are tagged as needing eng confirmation.

### Findings
- **medium** NFR-1/NFR-5 thresholds (≥30 FPS, ≤10s cold start) are author assumptions, not eng-confirmed. *Fix:* convert to a confirmed device+target during architecture handoff; tracked already as inline assumptions.

## Strategic coherence — strong
Clear thesis and the feature order follows it: the loop (Lure→Scan/Capture/Slay→Codex→Shop) is exactly the CLAUDE.md core loop, MVP cut is an *experience* MVP (prove the camera-magic moment), and SMs validate the thesis (SM-1 first-encounter completion, SM-C1 guards against monetizing basic play). Counter-metrics are present and load-bearing (SM-C3 explicitly forbids optimizing away the safety warning). Not a backlog with headings.

## Done-ness clarity — adequate
Every FR has at least one testable consequence, and Credit-spend FRs specify exact deduction behavior and refund-on-failure. Billing FRs (FR-13/14) are unusually concrete (exactly-once, acknowledge-window, reconcile-on-launch). Weak spots are inherent to a design-stage game PRD: "stable, immersive frame rate," "feel immediate," and rare-tier "strictly higher" probability lack numbers — but the numeric gaps are tagged and routed to OQs, not smuggled as done.

### Findings
- **medium** "Slay rewards strictly superior to Capture" (FR-9) is testable in direction but not magnitude. *Fix:* economy pass (OQ-9) should pin reward deltas so QA can assert them.
- **low** FR-4 occlusion "where depth data supports it" is appropriately hedged for AR but will need a device-specific acceptance bar at story time.

## Scope honesty — strong
Non-Goals does real work (no PvP, no iOS, no gacha, no third-party billing, no realistic horror). `[NON-GOAL for MVP]` on The Pit with full spec retained is exemplary de-scoping — deferred loudly, not silently. 18 inline `[ASSUMPTION]` tags all roundtrip to the §9 index; 10 OQs. Open-items density is moderate-high but appropriate for a launch-stakes PRD authored from a design doc rather than live elicitation — every item is a real, named decision, not a gap.

## Downstream usability — strong
Glossary present and used verbatim (Credit, Lure, Capture, Slay, Codex, The Pit). FR/UJ/SM/OQ IDs are contiguous and unique (verified: FR-1–17, UJ-1–3, SM-1–6 + C1–C3, OQ-1–10). FRs reference UJs inline ("Realizes UJ-2"); SMs reference FRs. Each section reads standalone via Glossary terms. This PRD is chain-top (feeds architecture + epics/stories), and the traceability supports that.

## Shape fit — strong
Correct shape for a consumer AR game: named-protagonist UJs are load-bearing and present; Aesthetic & Tone, Information Architecture, Monetization, Platform, and Constraints & Guardrails (Safety/Privacy/Cost) are all pulled in because the product genuinely carries those concerns. Enterprise/regulated/developer clusters correctly omitted. Not over- or under-formalized.

## Mechanical notes
- **Glossary drift:** none found. Core nouns used consistently; "Credit" used in place of casual "credits" in normative FR text.
- **ID continuity:** clean — no gaps or duplicates across FR/UJ/SM/OQ.
- **Assumptions roundtrip:** 18 inline `[ASSUMPTION]` tags; §9 index covers each cluster and back-links to OQs. Good.
- **UJ protagonists:** all three named (Maya, Dev, Priya) with context inline; no floating UJs.
- **Required sections:** full Essential Spine + appropriate Adapt-In clusters present for launch-stakes consumer mobile.

## Gate verdict
**PASS — ready to finalize and hand to architecture/epics.** No critical or high findings. Medium findings (FPS/cold-start thresholds, Slay reward magnitude, economy balancing) are all already tracked as Open Questions/assumptions and are appropriate to resolve in the solutioning phase, not blockers to PRD finalization.
