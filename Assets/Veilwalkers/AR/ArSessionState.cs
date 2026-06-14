namespace Veilwalkers.AR
{
    /// <summary>
    /// The AR-session lifecycle state (Story 3.3). The single source of truth the consumers read,
    /// owned + transitioned by <see cref="ArSessionService"/> (the sole owner of the AR lifecycle —
    /// architecture.md:405, :466-468, :584). The canonical lifecycle is
    /// cold → prewarming → ready → running → paused.
    /// </summary>
    public enum ArSessionState
    {
        /// <summary>
        /// Initial / after teardown; no AR subsystem running — the cheap resting state. A back-out
        /// from a warm session tears cheaply back here (AC-3).
        /// </summary>
        Cold,

        /// <summary>
        /// <see cref="ArSessionService.PrewarmAsync"/> is in flight — the async warmup the trigger
        /// kicked (AC-3). The subsystem is starting (<see cref="IArSession.StartAsync"/> awaited) but
        /// the player has not committed to AR yet.
        /// </summary>
        Prewarming,

        /// <summary>
        /// Prewarm completed; the session is warm and idle, waiting for the player to actually enter
        /// AR. From here a back-out tears cheaply to <see cref="Cold"/> (AC-3).
        /// </summary>
        Ready,

        /// <summary>
        /// The session is active and rendering the camera / AR — the player is in AR Mode.
        /// </summary>
        Running,

        /// <summary>
        /// The running session is paused (backgrounding / focus-loss). Resumes to <see cref="Running"/>
        /// on focus-regain — but only after the camera-permission re-check passes (a permission revoked
        /// while backgrounded tears down to <see cref="Cold"/> + raises the interruption instead).
        /// </summary>
        Paused,
    }
}
