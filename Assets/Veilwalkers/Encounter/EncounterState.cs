namespace Veilwalkers.Encounter
{
    /// <summary>
    /// The lifecycle of a single encounter (Story 4.1, AR-9 / architecture.md:414, :66). Distinct from
    /// <c>Veilwalkers.AR.ArSessionState</c> (the AR session lifecycle) — this is the encounter-flow state
    /// driven by <see cref="EncounterStateMachine"/>.
    /// <para>
    /// <b>The canonical happy path</b> is <see cref="Idle"/> → <see cref="Lured"/> → <see cref="Acting"/> →
    /// <see cref="Resolving"/> → <see cref="Resolved"/> (architecture.md:414). Within ONE encounter the
    /// player performs MANY actions, so a per-ACTION resolution loops back:
    /// <see cref="Acting"/> → <see cref="Resolving"/> → <see cref="Acting"/> (the next action) — the action
    /// machinery returns to <see cref="Acting"/> (or <see cref="Lured"/>) after each committed action.
    /// <see cref="Resolved"/> is the per-ENCOUNTER terminal (the encounter as a whole ends), reached
    /// deliberately from <see cref="Resolving"/>; from there the only exit is <see cref="Idle"/> (reset for
    /// a new encounter). <see cref="Suspended"/> is the AR-loss pause, reachable from any active state.
    /// </para>
    /// </summary>
    public enum EncounterState
    {
        /// <summary>No active encounter. The start and end state; a Lure moves to <see cref="Lured"/>.</summary>
        Idle,

        /// <summary>A monster has been lured/spawned; the encounter is live and awaiting player actions.</summary>
        Lured,

        /// <summary>The player has begun an action (Scan/Capture/Slay/extra); its spend+resolution is pending.</summary>
        Acting,

        /// <summary>
        /// The action's atomic spend+resolution write is in flight (the AR-8 one-write commit window). This
        /// state is held WHILE the composed atomic write owns the shared save lock — see
        /// <see cref="EncounterStateMachine"/>'s suspend-during-resolve rule (decision H2).
        /// </summary>
        Resolving,

        /// <summary>The encounter as a whole has concluded (per-encounter terminal). Exits only to <see cref="Idle"/>.</summary>
        Resolved,

        /// <summary>
        /// Paused by AR loss (architecture.md:650-652): reachable from any active state
        /// (<see cref="Lured"/>/<see cref="Acting"/>/<see cref="Resolving"/>) when
        /// <c>OnArSessionInterrupted</c> fires. On recovery the encounter attempts anchor restore and
        /// resumes to the state it left (or stays suspended on a failed restore — never vanishes).
        /// </summary>
        Suspended,
    }
}
