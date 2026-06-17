# The Pit — POST-MVP signature feature (reserved seam)

**This folder is a reserved architectural seam: it has NO `.asmdef` and NO gameplay code,
and it stays that way until the post-MVP Pit is actually built.** An empty boundary is
ceremony, not enforcement — so the Pit gets its assembly only when it has code.

The Pit is the emotionally load-bearing differentiator: a placeable miniature Roman
colosseum where miniaturized captured Monsters fight in AR. It is **deferred to post-MVP**
(FR-15–FR-17). This note exists so the spec travels *with* the seam — a future implementer
opening this folder finds what goes here and how it plugs in, without spelunking the
planning docs.

> A regression guard (`Assets/Tests/EditMode/Architecture.Tests/PitSeamReservedTests.cs`)
> fails the build if an `.asmdef` or a gameplay `.cs` appears under this folder while the
> Pit is still post-MVP scope. When you start building the Pit for real, you will add its
> assembly + an allowed-edge matrix entry deliberately — and update that guard at the same
> time. That is the intended, visible moment the seam becomes real.

## Carried specs (post-MVP scope — cited pointers, not a fork)

These are summarized from canon; the planning docs remain authoritative. If they ever
disagree with this note, the planning docs win — update this note to match.

- **FR-15 — Place + Miniaturize.** Place The Pit on a detected plane and Miniaturize
  captured Monsters (**1–2 Credits each**) into fight tokens.
  `[Source: docs/epics.md FR-15 (line 41); FR map (line 130); Epic 7 charter (lines 168–171)]`
- **FR-16 — Host + resolve fights.** Host and resolve Pit fights (**1 Credit / free with
  limits**) between Miniaturized Monsters, granting rewards to the winner.
  `[Source: docs/epics.md FR-16 (line 42); FR map (line 131)]`
- **FR-17 — Pit credit sinks.** Boost a fighter's stats / revive a defeated miniature /
  unlock arena effects.
  `[Source: docs/epics.md FR-17 (line 43); FR map (line 132)]`

**Economy caveat:** the credit costs above reflect the design-doc Pit costs. The *live*
economy is the 4-action MVP model (Basic Lure 1 · Premium Lure 4 · Multi-Lure 5 · Slay 3),
and the Pit costs are **not** in the current `EconomyConfig`. When the Pit is built,
reconcile its costs against the live `EconomyConfig` rather than hard-coding these
provisional numbers. `[Source: docs/architecture.md §"Economy Model Revision"; CLAUDE.md credit economy]`

## How it plugs in (reuse the MVP patterns — do not hand-roll)

When built, the Pit **reuses** the MVP's AR + Encounter infrastructure rather than
reinventing it:

- **AR session lifecycle → `ArSessionService`** (the *sole* AR lifecycle owner; Epic 3.3,
  `Veilwalkers.AR`). The Pit runs inside the same cold → prewarming → ready → running →
  paused session; it does not start/tear down its own.
- **Placement on a detected plane → `PlaneAnchorService` / `IArAnchorProvider`** (Epic 3.4 /
  3.5). The Pit places on a detected plane and saves/restores an `AnchorToken` like a lured
  Monster does — never spawns into empty space.
- **Encounter patterns → `EncounterService` + the encounter state machine** (Epic 4.1,
  `Veilwalkers.Encounter`). Pit fights are a new resolution flow built on the same
  state-machine + atomic one-write-per-action discipline (AR-8), not a parallel system.

`[Source: docs/architecture.md:260 "The Pit (post-MVP) will reuse AR session + EncounterService patterns — keep them reusable"; :757 "The Pit subsystem (reuses AR + Encounter patterns)"]`

## Dependency direction (one-way — Pit → MVP, never the reverse)

The MVP does **not** depend on the Pit. The dependency points **Pit → MVP** (the Pit will
reference `Veilwalkers.AR` / `Veilwalkers.Encounter` / `Veilwalkers.Core`, never the other
way). Until the Pit has code it has no assembly, so nothing can depend on it — that is why
this folder stays an empty, ungated boundary.

`[Source: docs/epics.md AR-5 (line 62) "Pit = folder stub, no asmdef"; docs/architecture.md:441–442 "Pit has no asmdef until it has code (an empty boundary is ceremony, not enforcement)"]`

## When you build the Pit (the checklist for un-reserving the seam)

1. Add `Assets/Veilwalkers/Pit/Veilwalkers.Pit.asmdef` referencing only lower tiers
   (`Veilwalkers.Core`, `Veilwalkers.AR`, `Veilwalkers.Encounter`, …) — one-way toward Core.
2. Add a `Veilwalkers.Pit` row to the allowed-edge matrix in
   `Assets/Tests/EditMode/Architecture.Tests/AcyclicDependencyTests.cs` with its sanctioned
   references (the matrix is the deliberate, visible record of every legal edge).
3. Update `PitSeamReservedTests.cs` — the "no gameplay code / no asmdef" guard is now
   intentionally obsolete for the parts you are building; replace it with whatever invariant
   the real Pit needs (or retire the specific pins that no longer apply).
4. Reconcile Pit credit costs against the live `EconomyConfig` (economy caveat above).
