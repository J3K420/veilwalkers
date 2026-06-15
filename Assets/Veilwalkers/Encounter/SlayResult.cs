using System;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// Why a Slay attempt did not RUN (Story 4.5, FR-9). <see cref="None"/> is the success sentinel — note that
    /// "the attempt ran" is distinct from "the Monster was slain" (a miss is a successful RUN whose roll failed;
    /// see <see cref="SlayResult.Slain"/>). APPEND-ONLY (never reorder/renumber — telemetry + serialization
    /// stability, the <see cref="CaptureFailureReason"/>/<c>SpendFailureReason</c> convention).
    /// </summary>
    public enum SlayFailureReason
    {
        /// <summary>The attempt ran cleanly (it may have slain or missed — read <see cref="SlayResult.Slain"/>).</summary>
        None = 0,

        /// <summary>
        /// The Slay was attempted while there was no settled Monster — the encounter was not in a live,
        /// mid-action state (the machine could not enter <see cref="EncounterState.Acting"/>). Nothing was
        /// persisted. A sequencing/precondition failure, distinct from a persist fault — a UI shows "lure a
        /// Monster first," NOT an error. (Story 4.5 gives this a distinct reason rather than the inherited
        /// misleading <c>PersistenceFailed</c>+0 — deferred-work.md, the 4.1 out-of-sequence deferral, settled
        /// for the Slay path here as Capture/Scan settled it for theirs.)
        /// </summary>
        NotSettled = 1,

        /// <summary>
        /// A Slay whose roll WOULD have won could not be afforded — the balance was below
        /// <see cref="EconomyConfig.SlayCost"/> (3) at the moment the spend would commit. Blocked BEFORE any
        /// deduction (nothing persisted; the count never goes negative). The service raises
        /// <see cref="EncounterService.OnInsufficientCredits"/> (AR-11 — never opens the Shop) and leaves the
        /// encounter live for a Retry once the player tops up.
        /// </summary>
        InsufficientCredits = 2,

        /// <summary>The atomic Slay write faulted (persist failure or a recovery swap). Every in-memory mutation
        /// (the Credit deduction, codex, XP) was rolled back (NFR-3 — a typed failure, never a crash).</summary>
        PersistenceFailed = 3,
    }

    /// <summary>
    /// The typed outcome of <see cref="EncounterService.TrySlayAsync"/> (Story 4.5, FR-9). A SUCCESSFUL Slay
    /// spends exactly <see cref="Cost"/> Credits (canon 3), records the <c>Slain</c> Codex discovery, and grants
    /// <see cref="EconomyConfig.XpPerSlay"/> XP (strictly more than Capture, FR-9) — all in ONE atomic write. A
    /// MISS spends NOTHING and leaves the encounter live for a free Retry (AC-3). Never thrown for an expected
    /// outcome (a miss, an insufficient-credits block, an out-of-sequence call, a persist fault are all reported
    /// here — AR-7); only a null/empty/invalid monster id throws (programmer error).
    /// <para>
    /// <b><see cref="Success"/> vs <see cref="Slain"/>.</b> <see cref="Success"/> means the attempt RAN and
    /// persisted cleanly — it is true for BOTH a slay and a miss. <see cref="Slain"/> carries the ROLL outcome:
    /// true = the Monster was slain (Codex discovery recorded, 3 Credits spent, XP granted); false = a miss (no
    /// Credits lost, a free Retry is offered — AC-3). An insufficient-credits block or an out-of-sequence Slay
    /// is <see cref="Success"/> = false with a reason. So the HUD distinguishes "you missed — try again, it cost
    /// nothing" (Success=true, Slain=false) from "you can't — not enough Credits / not settled" (Success=false).
    /// </para>
    /// <para>
    /// <b><see cref="Cost"/> is carried on EVERY result</b> (success, miss, AND failure) so the HUD can always
    /// render the "Slay − 3" affordance (AC-1 "cost shown before spend"). <see cref="NewBalance"/> is the
    /// post-spend balance on a slay; the UNCHANGED balance on a miss / block (no Credits lost).
    /// </para>
    /// </summary>
    public readonly struct SlayResult
    {
        /// <summary>Whether the attempt RAN and persisted cleanly (true for a slay AND a miss). False only for a
        /// typed failure that prevented the attempt (insufficient Credits, not settled, persist fault).</summary>
        public bool Success { get; }

        /// <summary>Why it failed (<see cref="SlayFailureReason.None"/> on success — a slay OR a miss).</summary>
        public SlayFailureReason FailureReason { get; }

        /// <summary>The Monster's id (the deferred Epic-6 slay/loot UI animates this). Empty on a typed failure.</summary>
        public string MonsterId { get; }

        /// <summary>The ROLL outcome: true = the Monster was slain (Codex discovery recorded + Credits spent + XP
        /// granted); false = a miss (no Credits lost; the encounter stays live for a free Retry, AC-3). Only
        /// meaningful when <see cref="Success"/> is true.</summary>
        public bool Slain { get; }

        /// <summary>The Slay Credit cost (canon 3, <see cref="EconomyConfig.SlayCost"/>) — carried on EVERY result
        /// so the HUD can show "Slay − 3" before the spend (AC-1 "cost shown before spend").</summary>
        public int Cost { get; }

        /// <summary>The Credit balance AFTER the attempt: the post-spend balance on a slay (prior − <see cref="Cost"/>);
        /// the UNCHANGED balance on a miss or an insufficient-credits block (no Credits lost).</summary>
        public int NewBalance { get; }

        private SlayResult(bool success, SlayFailureReason reason, string monsterId, bool slain, int cost, int newBalance)
        {
            Success = success;
            FailureReason = reason;
            MonsterId = monsterId ?? string.Empty;
            Slain = slain;
            Cost = cost;
            NewBalance = newBalance;
        }

        /// <summary>A Slay attempt that RAN (a slay or a miss). <paramref name="slain"/> carries the roll outcome;
        /// <paramref name="newBalance"/> is the post-spend balance on a slay or the unchanged balance on a miss.</summary>
        public static SlayResult Succeeded(string monsterId, bool slain, int cost, int newBalance) =>
            new SlayResult(true, SlayFailureReason.None, monsterId, slain, cost, newBalance);

        /// <summary>A typed failure that prevented the attempt (insufficient Credits, not settled, persist fault).
        /// <paramref name="newBalance"/> is the unchanged balance; <paramref name="cost"/> is still carried so the
        /// HUD can render the cost.</summary>
        public static SlayResult Failed(SlayFailureReason reason, int cost, int newBalance)
        {
            if (reason == SlayFailureReason.None)
            {
                throw new ArgumentException("A failed SlayResult needs a non-None reason.", nameof(reason));
            }

            return new SlayResult(false, reason, string.Empty, false, cost, newBalance);
        }
    }
}
