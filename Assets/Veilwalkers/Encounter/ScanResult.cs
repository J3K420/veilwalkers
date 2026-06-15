using System;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// Why a Scan did not record (Story 4.3). <see cref="None"/> is the success sentinel. A Scan is a FREE
    /// action (no Credits, no charges) — it never has an affordability failure.
    /// </summary>
    public enum ScanFailureReason
    {
        /// <summary>The Scan succeeded (the Monster's scan progress was recorded, or it was already scanned).</summary>
        None = 0,

        /// <summary>
        /// The Scan was attempted while there was no settled Monster to scan — the encounter was not in a
        /// live, mid-action state (the machine could not enter <see cref="EncounterState.Acting"/>). Nothing
        /// was persisted. A sequencing/precondition failure, distinct from a persist fault — a UI shows
        /// "lure a Monster first," NOT an error. (Story 4.3 gives this a distinct reason rather than the
        /// inherited misleading <c>PersistenceFailed</c> — deferred-work.md:21.)
        /// </summary>
        NotSettled = 1,

        /// <summary>The atomic Scan write faulted (persist failure or a recovery swap). Any in-memory scan
        /// mutation was rolled back (NFR-3 — a typed failure, never a crash).</summary>
        PersistenceFailed = 2,
    }

    /// <summary>
    /// The typed outcome of <see cref="EncounterService.TryScanAsync"/> (Story 4.3, FR-7). A Scan is FREE —
    /// it never changes the Credit balance or any charge — so there is no spend/balance field (unlike
    /// <see cref="LureResult"/>). Carries the scanned Monster id (for the deferred Epic-6 reveal UI) and
    /// whether the Monster was already scanned (an idempotent re-scan). Never thrown — expected failures
    /// (not settled, persist fault) are reported here (AR-7); only a null/empty monster id throws (programmer
    /// error).
    /// </summary>
    public readonly struct ScanResult
    {
        /// <summary>Whether the Scan recorded (or confirmed an already-recorded) scan for the Monster.</summary>
        public bool Success { get; }

        /// <summary>Why it failed (<see cref="ScanFailureReason.None"/> on success).</summary>
        public ScanFailureReason FailureReason { get; }

        /// <summary>The scanned Monster's id (the deferred Epic-6 reveal UI animates this). Empty on failure.</summary>
        public string MonsterId { get; }

        /// <summary>True when the Scan recorded NOTHING new — neither a persistent <c>Scanned</c>-flag flip
        /// NOR a new in-encounter scan-state entry (a valid, free, idempotent re-scan). False whenever the
        /// Scan recorded something durable or new for the encounter (so a re-scan that flips the persistent
        /// flag for the first time — e.g. a Monster scanned while undiscovered, then discovered mid-encounter,
        /// then re-scanned — is NOT "already scanned": it persisted new data).</summary>
        public bool AlreadyScanned { get; }

        private ScanResult(bool success, ScanFailureReason reason, string monsterId, bool alreadyScanned)
        {
            Success = success;
            FailureReason = reason;
            MonsterId = monsterId ?? string.Empty;
            AlreadyScanned = alreadyScanned;
        }

        public static ScanResult Succeeded(string monsterId, bool alreadyScanned) =>
            new ScanResult(true, ScanFailureReason.None, monsterId, alreadyScanned);

        public static ScanResult Failed(ScanFailureReason reason)
        {
            if (reason == ScanFailureReason.None)
            {
                throw new ArgumentException("A failed ScanResult needs a non-None reason.", nameof(reason));
            }

            return new ScanResult(false, reason, string.Empty, false);
        }
    }
}
