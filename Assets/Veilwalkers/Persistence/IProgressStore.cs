using System.Threading.Tasks;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// The swappable persistence backend seam (AR-2): <see cref="LocalProgressStore"/>
    /// now, a Firebase-backed store in Phase 2. The interface deliberately exposes no
    /// file paths and no crypto — those are implementation details of the local store.
    /// All members are safe to call from any thread.
    /// </summary>
    public interface IProgressStore
    {
        /// <summary>
        /// Load the persisted <see cref="SaveModel"/>. Throws
        /// <see cref="SaveCorruptException"/> when the data is corrupt or tampered
        /// (never returns null, never silently wipes), and
        /// <see cref="System.IO.FileNotFoundException"/> when no save exists —
        /// callers decide between load and create via <see cref="Exists"/> first.
        /// </summary>
        Task<SaveModel> LoadAsync();

        /// <summary>
        /// Persist the model atomically and off the main thread (AC-2). Concurrent
        /// calls are serialized internally; failures surface as a faulted task so
        /// callers can roll back in-memory state (AR-8).
        /// </summary>
        Task SaveAsync(SaveModel model);

        /// <summary>Whether a persisted save currently exists.</summary>
        bool Exists();

        /// <summary>
        /// Delete the persisted save. This is what makes the explicit "start fresh"
        /// recovery choice possible — it is only ever called deliberately.
        /// </summary>
        Task DeleteAsync();
    }
}
