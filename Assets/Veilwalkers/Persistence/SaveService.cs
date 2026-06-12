using System;
using System.Threading.Tasks;
using Veilwalkers.Core;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// The app-facing owner of the single in-memory <see cref="SaveModel"/> for the
    /// session. Raw persistence is delegated to the injected
    /// <see cref="IProgressStore"/> (constructor injection — Persistence is a
    /// pure-logic area and must not call <c>GameServices.Get&lt;T&gt;()</c>).
    /// <para>
    /// Documented AR-4 deviation: there is deliberately NO <c>ISaveService</c>. The
    /// swappable seam is <see cref="IProgressStore"/> (Local now → Firebase later);
    /// SaveService is app-facing composition glue with no second implementation
    /// foreseeable, so Bootstrap registers the concrete class.
    /// </para>
    /// <para>
    /// Threading: <see cref="Status"/> and <see cref="Current"/> are safe to read
    /// from any thread (published under a lock). Events may fire on background
    /// threads — UI consumers (Epic 6) must marshal to the main thread themselves.
    /// <see cref="SaveAsync"/> surfaces failure as a faulted task so callers can roll
    /// back in-memory mutations (the AR-8 one-write-per-action rule consumed by
    /// Stories 1.4/1.5).
    /// </para>
    /// </summary>
    public sealed class SaveService
    {
        private readonly IProgressStore _store;
        private readonly object _gate = new object();

        private SaveModel _current;
        private SaveStatus _status = SaveStatus.Idle;

        /// <summary>Raised when a save begins (AR-19 long-op surface).</summary>
        public event Action OnSaveStarted;

        /// <summary>Raised when a save completes successfully.</summary>
        public event Action OnSaveCompleted;

        /// <summary>
        /// Raised when a load fails, carrying the failure detail. A
        /// <see cref="SaveCorruptException"/> here means the recovery choice
        /// (<see cref="RetryLoadAsync"/> / <see cref="StartFreshAsync"/>) must be
        /// surfaced to the player.
        /// </summary>
        public event Action<Exception> OnLoadFailed;

        public SaveService(IProgressStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Current persistence status (AR-19).</summary>
        public SaveStatus Status
        {
            get
            {
                lock (_gate)
                {
                    return _status;
                }
            }
        }

        /// <summary>
        /// The loaded session model, or null before <see cref="InitializeAsync"/>
        /// completes (or while the save is corrupt and unrecovered).
        /// </summary>
        public SaveModel Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        /// <summary>
        /// Load-or-create: loads the existing save, or — when none exists — creates a
        /// fresh default model and persists it. A fresh model has <c>Credits == 0</c>;
        /// the 20-credit first-launch grant is Story 1.7, not here.
        /// <para>
        /// A corrupt save does NOT fault this task: status becomes
        /// <see cref="SaveStatus.Corrupt"/> and <see cref="OnLoadFailed"/> fires —
        /// recovery happens only through the explicit
        /// <see cref="RetryLoadAsync"/>/<see cref="StartFreshAsync"/> calls (AC-3:
        /// never auto-wipe). Non-corruption failures set
        /// <see cref="SaveStatus.Failed"/> and rethrow.
        /// </para>
        /// </summary>
        public async Task InitializeAsync()
        {
            SetStatus(SaveStatus.Loading);
            try
            {
                if (!_store.Exists())
                {
                    var fresh = new SaveModel();
                    await _store.SaveAsync(fresh).ConfigureAwait(false);
                    PublishModel(fresh);
                    GameLog.Info("SaveService: no save found — created and persisted a fresh default save.");
                    return;
                }

                SaveModel loaded = await _store.LoadAsync().ConfigureAwait(false);
                PublishModel(loaded);
                GameLog.Info("SaveService: save loaded.");
            }
            catch (SaveCorruptException ex)
            {
                SetStatus(SaveStatus.Corrupt);

                // Warn, not Error: a corrupt save is an EXPECTED, handled state with a
                // designed recovery flow (retry / start fresh) — GameLog.Warn's
                // "recoverable problem worth attention" contract, exactly.
                GameLog.Warn($"SaveService: save is corrupt — awaiting explicit recovery. {ex.Message}");
                OnLoadFailed?.Invoke(ex);
            }
            catch (Exception ex)
            {
                SetStatus(SaveStatus.Failed);
                GameLog.Error($"SaveService: load failed. {ex.Message}");
                OnLoadFailed?.Invoke(ex);
                throw;
            }
        }

        /// <summary>
        /// Re-attempt the load after a failure (recovery choice 1: "retry").
        /// Identical semantics to <see cref="InitializeAsync"/>.
        /// </summary>
        public Task RetryLoadAsync() => InitializeAsync();

        /// <summary>
        /// Recovery choice 2: "start fresh". Deletes the persisted save, creates a
        /// default model, and persists it. This is the ONLY path that wipes a save —
        /// it never happens implicitly.
        /// </summary>
        public async Task StartFreshAsync()
        {
            SetStatus(SaveStatus.Saving);
            try
            {
                await _store.DeleteAsync().ConfigureAwait(false);
                var fresh = new SaveModel();
                await _store.SaveAsync(fresh).ConfigureAwait(false);
                PublishModel(fresh);
                GameLog.Info("SaveService: started fresh — previous save deleted, defaults persisted.");
            }
            catch (Exception ex)
            {
                SetStatus(SaveStatus.Failed);
                GameLog.Error($"SaveService: start-fresh failed. {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Persist <see cref="Current"/>. Faults (with status
        /// <see cref="SaveStatus.Failed"/>) when persistence fails so callers can
        /// roll back the in-memory mutation they just made (AR-8).
        /// </summary>
        public async Task SaveAsync()
        {
            SaveModel model = Current;
            if (model == null)
            {
                throw new InvalidOperationException(
                    "SaveService has no loaded model to save — call InitializeAsync first.");
            }

            SetStatus(SaveStatus.Saving);
            OnSaveStarted?.Invoke();
            try
            {
                await _store.SaveAsync(model).ConfigureAwait(false);
                SetStatus(SaveStatus.Idle);
                OnSaveCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                SetStatus(SaveStatus.Failed);
                GameLog.Error($"SaveService: save failed. {ex.Message}");
                throw;
            }
        }

        private void PublishModel(SaveModel model)
        {
            lock (_gate)
            {
                _current = model;
                _status = SaveStatus.Idle;
            }
        }

        private void SetStatus(SaveStatus status)
        {
            lock (_gate)
            {
                _status = status;
            }
        }
    }
}
