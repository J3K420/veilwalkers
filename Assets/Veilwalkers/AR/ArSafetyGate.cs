using Veilwalkers.Core;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The pure-logic AR Safety Warning gate (Story 3.2, FR-3): the ONE place that decides whether a
    /// cold entry shows the full deliberate-read warning or the fast on-brand card, whether a Shop
    /// resume skips it, and when the camera view + Encounter actions become reachable. Plain C# (NO
    /// <c>MonoBehaviour</c>, no Unity types) so it is headless-tested.
    /// <para>
    /// <b>This branching does NOT violate AR's "thin adapter, zero branching logic" mandate
    /// (decision #2).</b> That rule (architecture.md:466) targets OS-touching adapters
    /// (<see cref="AndroidCameraPermission"/>, the future ARCore session glue). The full/fast/skip/
    /// acknowledge state machine IS branching worth testing, and there is no OS-touching adapter in
    /// this gate at all — it is pure session state (mirrors <see cref="CameraPermissionFlow"/>'s
    /// decision #2 reconciliation).
    /// </para>
    /// <para>
    /// <b>Cold-entry rule (architecture.md:482-486):</b> the warning fires on cold entry only — the
    /// FULL read on the first cold entry of the session, a FAST card on later cold entries (AC-2). A
    /// Shop round-trip resume does NOT re-fire it (AC-3) and does NOT count as a "read" (it never
    /// burns the once-per-session full read). "Per session" = per app-process lifetime: the gate is a
    /// registered singleton holding an in-memory latch, so a fresh launch shows the full read again.
    /// NO persistence (decision #4) — erring toward the full read more often is the safe direction
    /// for a mandatory Play safety gate.
    /// </para>
    /// <para>
    /// <b>The interaction block (AC-1):</b> <see cref="MayInteract"/> is the SINGLE predicate the
    /// camera/Encounter consumers (Epic 4/6) read — true ONLY in
    /// <see cref="ArSafetyGateState.Acknowledged"/>. A fast card is still blocking, so the warning
    /// cannot be A/B'd away.
    /// </para>
    /// <para>
    /// <b>Legal transitions</b> (illegal calls are a logged no-op returning the current state — a
    /// UI-driven flow must never throw, NFR-3):
    /// <list type="bullet">
    /// <item><see cref="RequestEntry"/>: callable from any state. <c>ColdEntry</c> →
    /// <see cref="ArSafetyGateState.BlockingFull"/> (first this session) or
    /// <see cref="ArSafetyGateState.BlockingFastCard"/> (later); <c>ShopResume</c> →
    /// <see cref="ArSafetyGateState.Acknowledged"/> (skip, AC-3).</item>
    /// <item><see cref="Acknowledge"/>: only from <see cref="ArSafetyGateState.BlockingFull"/> or
    /// <see cref="ArSafetyGateState.BlockingFastCard"/> → <see cref="ArSafetyGateState.Acknowledged"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class ArSafetyGate
    {
        // Whether the full deliberate-read warning has been shown at least once this app session.
        // In-memory only (decision #4): a fresh launch resets it, re-showing the full read. A Shop
        // resume never sets it (a resume is not a "read"), so the first real cold entry after a
        // resume still gets the full read.
        private bool _shownThisSession;

        /// <summary>The current gate state. Starts <see cref="ArSafetyGateState.Inactive"/>.</summary>
        public ArSafetyGateState State { get; private set; } = ArSafetyGateState.Inactive;

        /// <summary>
        /// The single AC-1 interaction gate the camera view / Encounter entry reads: <c>true</c> ONLY
        /// when the warning has been acknowledged (or skipped on a Shop resume). False while the
        /// warning is up — including the fast card, which is mandatory-blocking (AC-1, "cannot be
        /// A/B'd away").
        /// </summary>
        public bool MayInteract => State == ArSafetyGateState.Acknowledged;

        /// <summary>
        /// The entry decision, called when the player enters AR Mode with the caller-supplied
        /// <paramref name="kind"/> (decision #1). For <see cref="ArEntryKind.ColdEntry"/>: the FULL
        /// warning on the first cold entry of the session, the FAST card on later cold entries (AC-2).
        /// For <see cref="ArEntryKind.ShopResume"/>: skip straight to
        /// <see cref="ArSafetyGateState.Acknowledged"/> — the warning does NOT re-fire (AC-3) and the
        /// once-per-session full read is NOT consumed. Callable from any state (a re-entry decision).
        /// </summary>
        public ArSafetyGateState RequestEntry(ArEntryKind kind)
        {
            if (kind == ArEntryKind.ShopResume)
            {
                // AC-3: a Shop round-trip resume never re-fires the warning, and is not a "read" —
                // so it must not flip _shownThisSession. AR resumes interactive.
                State = ArSafetyGateState.Acknowledged;
                return State;
            }

            // ArEntryKind.ColdEntry — the FR-3 trigger.
            if (_shownThisSession)
            {
                // Later cold entry this session: the fast on-brand card (AC-2). Still blocking.
                State = ArSafetyGateState.BlockingFastCard;
            }
            else
            {
                // First cold entry of the session: the full deliberate read (AC-2). Latch it so the
                // next cold entry downgrades to the fast card.
                _shownThisSession = true;
                State = ArSafetyGateState.BlockingFull;
            }

            return State;
        }

        /// <summary>
        /// The player acknowledged the warning (tapped the dismiss/continue action) → release the
        /// camera view + Encounter actions (AC-1). Only valid from
        /// <see cref="ArSafetyGateState.BlockingFull"/> or <see cref="ArSafetyGateState.BlockingFastCard"/>.
        /// An out-of-state call (no warning showing, or already acknowledged) is a logged no-op —
        /// a UI-driven flow must never throw (NFR-3).
        /// </summary>
        public ArSafetyGateState Acknowledge()
        {
            if (State != ArSafetyGateState.BlockingFull && State != ArSafetyGateState.BlockingFastCard)
            {
                GameLog.Warn(
                    $"ArSafetyGate.Acknowledge ignored — only valid while the warning is showing " +
                    $"(BlockingFull/BlockingFastCard; current: {State}). No state change.");
                return State;
            }

            State = ArSafetyGateState.Acknowledged;
            return State;
        }
    }
}
