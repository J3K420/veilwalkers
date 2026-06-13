namespace Veilwalkers.Monsters
{
    /// <summary>
    /// The outcome of a <see cref="CodexService.RecordDiscoveryAsync"/> call. Every
    /// outcome maps to exactly one <see cref="CodexOutcome"/> value — no value is dead
    /// and no path is ambiguous.
    /// </summary>
    public enum CodexOutcome
    {
        /// <summary>First discovery: the entry was created, persisted, the count
        /// incremented, and <c>OnMonsterDiscovered</c> raised.</summary>
        Discovered,

        /// <summary>The Monster was already discovered, but a NEW flag flipped (e.g. a
        /// previously-captured Monster is now slain): the entry changed and was
        /// persisted, but the count did NOT increment and no discovery event fired.</summary>
        FlagUpdated,

        /// <summary>The entry already existed AND the requested flag was already set:
        /// a pure no-op — nothing changed, nothing persisted, no event.</summary>
        AlreadyRecorded,

        /// <summary>The mutation could not be persisted (a save fault, or a recovery
        /// swap raced the write): the in-memory change was rolled back.</summary>
        PersistenceFailed,
    }

    /// <summary>
    /// Result of recording a Codex discovery. Mirrors the Economy
    /// <c>SpendResult</c>/<c>DailyRewardResult</c> readonly-struct + static-factory
    /// house style, with one sanctioned divergence: success and failure are folded
    /// into the single <see cref="Outcome"/> enum rather than a separate
    /// success-sentinel reason enum — three of the four outcomes are successes, so a
    /// dedicated failure-reason type would carry a single member. Callers check
    /// <see cref="Success"/>; this type NEVER throws for an expected outcome (an
    /// already-recorded re-discovery or a persist failure is a typed result, not a
    /// throw — AR-7). <see cref="DiscoveredCount"/> is the post-operation "X" in the
    /// "X / 67" progress, for UI binding.
    /// </summary>
    public readonly struct CodexResult
    {
        /// <summary>True for every non-fault outcome (<see cref="CodexOutcome.Discovered"/>,
        /// <see cref="CodexOutcome.FlagUpdated"/>, <see cref="CodexOutcome.AlreadyRecorded"/>);
        /// false only for <see cref="CodexOutcome.PersistenceFailed"/>.</summary>
        public bool Success { get; }

        /// <summary>The discovered-Monster count after the operation (the "X" in "X / 67").
        /// On a no-op or a rolled-back failure this is the unchanged count.</summary>
        public int DiscoveredCount { get; }

        public CodexOutcome Outcome { get; }

        private CodexResult(bool success, CodexOutcome outcome, int discoveredCount)
        {
            Success = success;
            Outcome = outcome;
            DiscoveredCount = discoveredCount;
        }

        /// <summary>First discovery: entry created, persisted, count incremented,
        /// <c>OnMonsterDiscovered</c> raised. <paramref name="discoveredCount"/> is the
        /// post-increment count.</summary>
        public static CodexResult Discovered(int discoveredCount) =>
            new CodexResult(true, CodexOutcome.Discovered, discoveredCount);

        /// <summary>Already discovered, but a new flag flipped and was persisted; the
        /// count is unchanged and no discovery event fired.</summary>
        public static CodexResult FlagUpdated(int discoveredCount) =>
            new CodexResult(true, CodexOutcome.FlagUpdated, discoveredCount);

        /// <summary>Pure no-op: entry present and the requested flag already set; nothing
        /// persisted, no event.</summary>
        public static CodexResult AlreadyRecorded(int discoveredCount) =>
            new CodexResult(true, CodexOutcome.AlreadyRecorded, discoveredCount);

        /// <summary>The persist faulted (or a recovery swap raced the write); the
        /// in-memory mutation was rolled back. <paramref name="discoveredCount"/> is the
        /// unchanged count.</summary>
        public static CodexResult PersistenceFailed(int discoveredCount) =>
            new CodexResult(false, CodexOutcome.PersistenceFailed, discoveredCount);
    }
}
