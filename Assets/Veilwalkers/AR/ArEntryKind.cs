namespace Veilwalkers.AR
{
    /// <summary>
    /// How the player is arriving at AR Mode — the caller-supplied input that the
    /// <see cref="ArSafetyGate"/> uses to decide whether the mandatory FR-3 Safety Warning fires
    /// (Story 3.2).
    /// <para>
    /// <b>The classification lives UPSTREAM, not in the gate (decision #1).</b> Whether an entry is
    /// a fresh cold entry or a resume after a Shop round-trip is navigation/session context the gate
    /// cannot know — it is owned by the AppStateMachine (Story 6.3) / the AR session lifecycle
    /// (Story 3.3), which call the gate with the right kind. The gate's contract is "given the kind,
    /// decide what to show," exactly as <see cref="CameraPermissionFlow.Evaluate"/> takes OS truth
    /// from its seam rather than guessing.
    /// </para>
    /// <para>
    /// Only the two kinds the cold-entry rule (architecture.md:482-486) needs exist today. A future
    /// "Suspended → resume on AR loss" path (AR-9) is NOT a cold entry either, but is out of scope
    /// here (Story 3.5 owns anchor-loss resume) — add kinds when their stories arrive; do NOT
    /// pre-build.
    /// </para>
    /// </summary>
    public enum ArEntryKind
    {
        /// <summary>
        /// A fresh cold entry into AR Mode — the FR-3 trigger. The Safety Warning fires: the full
        /// deliberate read on the first cold entry of the session, a fast on-brand card on later
        /// cold entries (AC-2).
        /// </summary>
        ColdEntry,

        /// <summary>
        /// AR resuming after a Shop round-trip (UJ-2). The full warning does NOT re-fire (AC-3):
        /// re-firing would stack the warning and the re-anchoring beat into two interruptions for
        /// one monster (architecture.md:482-484). The gate resolves a resume straight to
        /// <see cref="ArSafetyGateState.Acknowledged"/> — AR resumes interactive.
        /// </summary>
        ShopResume,
    }
}
