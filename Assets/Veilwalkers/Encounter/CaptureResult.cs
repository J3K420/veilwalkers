using System;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// Why a Capture attempt did not RUN (Story 4.4, FR-8). <see cref="None"/> is the success sentinel — note
    /// that "the attempt ran" is distinct from "the Monster was captured" (a miss is a successful RUN whose
    /// roll failed; see <see cref="CaptureResult.Captured"/>). APPEND-ONLY (never reorder/renumber — telemetry
    /// + serialization stability, the <see cref="ScanFailureReason"/>/<c>SpendFailureReason</c> convention).
    /// </summary>
    public enum CaptureFailureReason
    {
        /// <summary>The attempt ran cleanly (it may have captured or missed — read <see cref="CaptureResult.Captured"/>).</summary>
        None = 0,

        /// <summary>
        /// The Capture was attempted while there was no settled Monster — the encounter was not in a live,
        /// mid-action state (the machine could not enter <see cref="EncounterState.Acting"/>). Nothing was
        /// persisted. A sequencing/precondition failure, distinct from a persist fault — a UI shows "lure a
        /// Monster first," NOT an error. (Story 4.4 gives this a distinct reason rather than the inherited
        /// misleading <c>PersistenceFailed</c>+0 — deferred-work.md, the 4.1 out-of-sequence deferral.)
        /// </summary>
        NotSettled = 1,

        /// <summary>
        /// A STRONG Capture was attempted with zero <see cref="ChargeType.StrongCapture"/> charges — blocked
        /// before any spend (the "earn via XP" hint; the charge count never goes negative). Nothing persisted.
        /// (A BASE Capture is FREE and never has this failure.)
        /// </summary>
        InsufficientCharges = 2,

        /// <summary>The atomic Capture write faulted (persist failure or a recovery swap). Every in-memory
        /// mutation (charge, codex, XP) was rolled back (NFR-3 — a typed failure, never a crash).</summary>
        PersistenceFailed = 3,
    }

    /// <summary>
    /// The typed outcome of <see cref="EncounterService.TryCaptureAsync"/> (Story 4.4, FR-8). A Capture never
    /// changes the Credit balance (base is free; Strong consumes an earned charge, never Credits). Carries
    /// enough for the HUD to react (the captured Monster id, whether it was actually captured vs missed,
    /// whether the Strong variant ran, the charge consumed, the remaining charge count for the charge pill).
    /// Never thrown for an expected outcome (a miss, a zero-charge block, an out-of-sequence call, a persist
    /// fault are all reported here — AR-7); only a null/empty/invalid monster id throws (programmer error).
    /// <para>
    /// <b><see cref="Success"/> vs <see cref="Captured"/>.</b> <see cref="Success"/> means the attempt RAN and
    /// persisted cleanly — it is true for BOTH a capture and a miss. <see cref="Captured"/> carries the ROLL
    /// outcome: true = the Monster was captured (Codex discovery recorded); false = a miss (a free Retry is
    /// offered, AC-3). A blocked Strong (zero charges) or an out-of-sequence Capture is <see cref="Success"/> =
    /// false with a reason. So the HUD distinguishes "you missed — try again" (Success=true, Captured=false)
    /// from "you can't — no charge / not settled" (Success=false).
    /// </para>
    /// </summary>
    public readonly struct CaptureResult
    {
        /// <summary>Whether the attempt RAN and persisted cleanly (true for a capture AND a miss). False only
        /// for a typed failure that prevented the attempt (zero charge, not settled, persist fault).</summary>
        public bool Success { get; }

        /// <summary>Why it failed (<see cref="CaptureFailureReason.None"/> on success — a capture OR a miss).</summary>
        public CaptureFailureReason FailureReason { get; }

        /// <summary>The Monster's id (the deferred Epic-6 capture/reveal UI animates this). Empty on a typed failure.</summary>
        public string MonsterId { get; }

        /// <summary>The ROLL outcome: true = the Monster was captured (Codex discovery recorded + XP granted);
        /// false = a miss (the encounter stays live for a free Retry, AC-3). Only meaningful when
        /// <see cref="Success"/> is true.</summary>
        public bool Captured { get; }

        /// <summary>Whether this was a STRONG Capture (the earned-charge variant) vs the free base attempt.</summary>
        public bool WasStrong { get; }

        /// <summary>Whether a <see cref="ChargeType.StrongCapture"/> charge was consumed by this attempt — true
        /// for a Strong attempt that RAN (win OR miss — the charge buys the odds, Decision A), false for a base
        /// attempt or a blocked Strong (zero charges).</summary>
        public bool ChargeConsumed { get; }

        /// <summary>The remaining <see cref="ChargeType.StrongCapture"/> charge count after the attempt (for the
        /// HUD charge pill). On a typed failure it carries the unchanged count.</summary>
        public int RemainingCharges { get; }

        private CaptureResult(
            bool success, CaptureFailureReason reason, string monsterId, bool captured, bool wasStrong,
            bool chargeConsumed, int remainingCharges)
        {
            Success = success;
            FailureReason = reason;
            MonsterId = monsterId ?? string.Empty;
            Captured = captured;
            WasStrong = wasStrong;
            ChargeConsumed = chargeConsumed;
            RemainingCharges = remainingCharges;
        }

        /// <summary>A Capture attempt that RAN (a capture or a miss). <paramref name="captured"/> carries the
        /// roll outcome.</summary>
        public static CaptureResult Succeeded(
            string monsterId, bool captured, bool wasStrong, bool chargeConsumed, int remainingCharges) =>
            new CaptureResult(true, CaptureFailureReason.None, monsterId, captured, wasStrong, chargeConsumed, remainingCharges);

        /// <summary>A typed failure that prevented the attempt (zero charge, not settled, persist fault).
        /// <paramref name="remainingCharges"/> is the unchanged charge count.</summary>
        public static CaptureResult Failed(CaptureFailureReason reason, bool wasStrong, int remainingCharges)
        {
            if (reason == CaptureFailureReason.None)
            {
                throw new ArgumentException("A failed CaptureResult needs a non-None reason.", nameof(reason));
            }

            return new CaptureResult(false, reason, string.Empty, false, wasStrong, false, remainingCharges);
        }
    }
}
