namespace Veilwalkers.Persistence
{
    /// <summary>
    /// The AR-19 status surface for save/load operations, exposed by
    /// <see cref="SaveService.Status"/> so UI can reflect long-running persistence
    /// work and the corrupt-save recovery state.
    /// </summary>
    public enum SaveStatus
    {
        /// <summary>No persistence operation is running; state is healthy.</summary>
        Idle,

        /// <summary>A load (initialize/retry) is in flight.</summary>
        Loading,

        /// <summary>A save is in flight.</summary>
        Saving,

        /// <summary>
        /// The save file failed integrity/validation on load. The app must surface
        /// the explicit recovery choice: <see cref="SaveService.RetryLoadAsync"/> or
        /// <see cref="SaveService.StartFreshAsync"/>. Never auto-wiped.
        /// </summary>
        Corrupt,

        /// <summary>A persistence operation failed for a non-corruption reason (e.g. IO).</summary>
        Failed,
    }
}
