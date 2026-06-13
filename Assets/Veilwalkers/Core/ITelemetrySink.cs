namespace Veilwalkers.Core
{
    /// <summary>
    /// The local telemetry seam (AR-15, Story 1.9). A LOCAL DIAGNOSTIC counter sink —
    /// NOT analytics: the data never leaves the device pre-backend; its value is the
    /// internal/tester balancing pass (loop-completions-before-zero, first-zero-credit,
    /// session-after-first-zero). Call sites simply <see cref="Count"/> a stable event
    /// key; where the counter lands (a capped-ring <c>telemetry.json</c> now, Firebase
    /// later) is the impl's concern.
    /// <para>
    /// Swappable by contract: a Phase-2 Firebase sink replaces the <c>LocalTelemetrySink</c>
    /// impl WITHOUT moving any call site, because this interface exposes no storage,
    /// path, or transport detail. Keep event keys STABLE strings (the same append-only
    /// discipline as <see cref="SpendFailureReason"/> / <c>ChargeType</c>): a renamed
    /// key silently breaks historical comparison.
    /// </para>
    /// </summary>
    public interface ITelemetrySink
    {
        /// <summary>
        /// Record <paramref name="amount"/> occurrences of the diagnostic event
        /// <paramref name="evt"/> (a stable string key). Fire-and-forget from the
        /// caller's view — the sink owns persistence and must never throw into a
        /// gameplay call site for an expected I/O hiccup.
        /// </summary>
        void Count(string evt, long amount = 1);
    }
}
