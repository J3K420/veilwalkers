using System;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// Why an apply-extra attempt did not APPLY (Story 4.6, FR-10). <see cref="None"/> is the success sentinel.
    /// APPEND-ONLY (never reorder/renumber — telemetry + serialization stability, the
    /// <see cref="SlayFailureReason"/>/<see cref="CaptureFailureReason"/>/<c>SpendFailureReason</c> convention).
    /// <para>
    /// <b>NOTE — extras spend a CHARGE, never Credits.</b> So there is no <c>InsufficientCredits</c> member and
    /// the service raises NO <see cref="EncounterService.OnInsufficientCredits"/> (unlike Slay/Lure). The
    /// zero-charge block is the <see cref="InsufficientCharges"/> "earn via XP" signal, surfaced via this typed
    /// result ONLY (Epic 6 binds the hint to that reason).
    /// </para>
    /// </summary>
    public enum ApplyExtraFailureReason
    {
        /// <summary>The extra was applied cleanly (the charge was consumed + the modifier recorded + persisted).</summary>
        None = 0,

        /// <summary>
        /// The extra was applied while there was no live encounter — the machine could not enter
        /// <see cref="EncounterState.Acting"/>. Nothing was consumed, recorded, or persisted. A
        /// sequencing/precondition failure distinct from a persist fault — a UI shows "lure a Monster first,"
        /// NOT an error. (Settles the 4.1 out-of-sequence deferral for the extras path — the distinct reason
        /// rather than the inherited misleading <c>PersistenceFailed</c>+0; the Scan/Capture/Slay precedent.)
        /// </summary>
        NotSettled = 1,

        /// <summary>
        /// The extra could not be applied because the player had ZERO charges of its type — the AC-3 "earn via
        /// XP" block. Nothing was consumed (the count stays at 0, NEVER negative — the only decrement is gated
        /// by this check), nothing was recorded, nothing persisted. The encounter stays live. Surfaced via the
        /// typed result only (no Credits involved → no <c>OnInsufficientCredits</c>).
        /// </summary>
        InsufficientCharges = 2,

        /// <summary>The atomic apply write faulted (persist failure or a recovery swap). BOTH in-memory mutations
        /// — the charge decrement AND the in-encounter modifier record — were rolled back (NFR-3 — a typed
        /// failure, never a crash).</summary>
        PersistenceFailed = 3,
    }

    /// <summary>
    /// The typed outcome of <see cref="EncounterService.TryApplyExtraAsync"/> (Story 4.6, FR-10). A SUCCESSFUL
    /// apply consumes EXACTLY one charge of the extra's type (never Credits), records the per-encounter modifier
    /// (Stability Boost ease / Nightveil Filter rarity-boost + visual-filter flag), and persists the charge
    /// decrement in ONE atomic write (AR-8). A zero-charge apply is blocked with <see cref="ApplyExtraFailureReason.InsufficientCharges"/>
    /// and consumes nothing. Never thrown for an expected outcome (a zero-charge block, an out-of-sequence call,
    /// a persist fault are all reported here — AR-7); only an undefined <see cref="Kind"/> throws
    /// <see cref="ArgumentOutOfRangeException"/> up front (programmer error).
    /// <para>
    /// <b><see cref="RemainingCharges"/></b> carries the post-consume charge count of THIS extra's type (for the
    /// HUD charge-pill — the <c>SpendResult</c> charge-count contract). On a block / out-of-sequence / fault it
    /// carries the UNCHANGED count (e.g. 0 on a zero-charge block — nothing was spent).
    /// </para>
    /// </summary>
    public readonly struct ApplyExtraResult
    {
        /// <summary>Whether the extra was applied and persisted cleanly. False for a typed failure that prevented
        /// the apply (zero charges, not settled, persist fault).</summary>
        public bool Success { get; }

        /// <summary>Why it failed (<see cref="ApplyExtraFailureReason.None"/> on success).</summary>
        public ApplyExtraFailureReason FailureReason { get; }

        /// <summary>Which extra this result is for (carried on EVERY result so the HUD can target the right pill).</summary>
        public ExtraKind Kind { get; }

        /// <summary>The post-attempt charge count of <see cref="Kind"/>'s type: the post-consume count on a
        /// success; the UNCHANGED count on a block / out-of-sequence / fault (nothing spent).</summary>
        public int RemainingCharges { get; }

        private ApplyExtraResult(bool success, ApplyExtraFailureReason reason, ExtraKind kind, int remainingCharges)
        {
            Success = success;
            FailureReason = reason;
            Kind = kind;
            RemainingCharges = remainingCharges;
        }

        /// <summary>A clean apply: one charge consumed + the modifier recorded + persisted.
        /// <paramref name="remainingCharges"/> is the post-consume count of <paramref name="kind"/>'s type.</summary>
        public static ApplyExtraResult Succeeded(ExtraKind kind, int remainingCharges) =>
            new ApplyExtraResult(true, ApplyExtraFailureReason.None, kind, remainingCharges);

        /// <summary>A typed failure that prevented the apply (zero charges, not settled, persist fault).
        /// <paramref name="remainingCharges"/> is the UNCHANGED count (nothing spent).</summary>
        public static ApplyExtraResult Failed(ApplyExtraFailureReason reason, ExtraKind kind, int remainingCharges)
        {
            if (reason == ApplyExtraFailureReason.None)
            {
                throw new ArgumentException("A failed ApplyExtraResult needs a non-None reason.", nameof(reason));
            }

            return new ApplyExtraResult(false, reason, kind, remainingCharges);
        }
    }
}
