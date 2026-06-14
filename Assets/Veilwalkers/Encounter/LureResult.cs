using System;
using System.Collections.Generic;
using Veilwalkers.Core;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// Why a Lure did not produce a spawn (Story 4.2). <see cref="None"/> is the success sentinel.
    /// </summary>
    public enum LureFailureReason
    {
        /// <summary>The Lure succeeded (one or more Monsters spawned).</summary>
        None = 0,

        /// <summary>
        /// Not enough valid placements were available (no trackable plane / not enough planes for a
        /// Multi-Lure, AC-3). Nothing was deducted; the encounter shows plane coaching. AC-1's
        /// "deduct only on successful spawn" — a placement that cannot be secured costs the player nothing.
        /// </summary>
        NoPlacement = 1,

        /// <summary>The player could not afford the Lure (AC-4). Nothing was deducted; the service raised
        /// <see cref="EncounterService.OnInsufficientCredits"/>; the world does not lock.</summary>
        InsufficientCredits = 2,

        /// <summary>The atomic deduct/spawn write faulted (persist failure or a recovery swap). Any
        /// deduction was rolled back; the spawn was abandoned (NFR-3 — a typed failure, never a crash).</summary>
        PersistenceFailed = 3,

        /// <summary>
        /// The Lure was refused because an encounter is already active (the machine is not Idle) — a
        /// re-entrancy guard, not a placement or affordability problem. Nothing was deducted. Distinct
        /// from <see cref="NoPlacement"/> so a UI shows "busy / finish your encounter," NOT plane coaching.
        /// </summary>
        AlreadyActive = 4,
    }

    /// <summary>
    /// The typed outcome of <see cref="EncounterService.TryLureAsync"/> (Story 4.2, AC-1/3/4). Carries the
    /// economy result (<see cref="Spend"/> — the sub-second spend-ack value, NFR-2) and, on success, the
    /// spawned Monster id(s) the (deferred) materialization render (Story 4.7) will animate. Never thrown —
    /// expected failures (no plane, can't afford, persist fault) are reported here (AR-7).
    /// </summary>
    public readonly struct LureResult
    {
        /// <summary>Whether the Lure spawned at least one Monster (and deducted its cost).</summary>
        public bool Success { get; }

        /// <summary>Why it failed (<see cref="LureFailureReason.None"/> on success).</summary>
        public LureFailureReason FailureReason { get; }

        /// <summary>The underlying credit-spend result (the sub-second ack value; the new balance). On a
        /// no-placement block, this is a non-deducting failure carrying the unchanged balance.</summary>
        public SpendResult Spend { get; }

        /// <summary>The spawned Monster id(s) — one for Basic/Premium, two for Multi (AC-3). Empty on
        /// failure.</summary>
        public IReadOnlyList<string> SpawnedMonsterIds { get; }

        private LureResult(bool success, LureFailureReason reason, SpendResult spend, IReadOnlyList<string> ids)
        {
            Success = success;
            FailureReason = reason;
            Spend = spend;
            SpawnedMonsterIds = ids ?? Array.Empty<string>();
        }

        public static LureResult Succeeded(SpendResult spend, IReadOnlyList<string> spawnedMonsterIds) =>
            new LureResult(true, LureFailureReason.None, spend, spawnedMonsterIds);

        public static LureResult Failed(LureFailureReason reason, SpendResult spend)
        {
            if (reason == LureFailureReason.None)
            {
                throw new ArgumentException("A failed LureResult needs a non-None reason.", nameof(reason));
            }

            return new LureResult(false, reason, spend, Array.Empty<string>());
        }
    }
}
