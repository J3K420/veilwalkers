using System.Threading.Tasks;
using Veilwalkers.Persistence;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The narrow read/write seam for the reduced-motion / photosensitivity accessibility opt-in
    /// (Story 6.6, AC-1; UX-DR17). A deliberately tiny port (the 6.4 <c>IDisclosureGate</c> /
    /// <c>IArPrewarmPort</c> narrow-port precedent) so the deferred Epic-6 view can read the player's
    /// setting and feed it to the dependency-free <see cref="MaterializationPresenter"/> as the
    /// <c>reducedMotion</c> input — WITHOUT the pure presenter taking a service dependency. The concrete
    /// adapter (over <c>SaveModel.ReducedMotion</c> + the save pipeline) is composed where the view is
    /// wired; until then <see cref="NoReducedMotionSetting"/> is the graceful default (motion-ON).
    /// <para>
    /// <b>Persist discipline (AR-8):</b> a concrete <see cref="SetReducedMotion"/> rides the existing
    /// <c>IProgressStore</c>/save pipeline (one atomic <c>SaveModel</c> write) — never a second
    /// hand-rolled persistence path.
    /// </para>
    /// </summary>
    public interface IReducedMotionSetting
    {
        /// <summary>True when the player has opted into reduced-motion / photosensitivity taming.
        /// False (motion-ON) is the default for a player who never opted in.</summary>
        bool ReducedMotionEnabled { get; }

        /// <summary>Set + persist the reduced-motion opt-in (through the atomic save pipeline).</summary>
        void SetReducedMotion(bool enabled);
    }

    /// <summary>
    /// The graceful no-op <see cref="IReducedMotionSetting"/> for when the setting seam is not yet wired
    /// (the Bootstrap seam is unregistered) — reports motion-ON and silently ignores writes, so an
    /// un-wired view degrades to the safe full-motion default rather than null-ref'ing (the 6.4
    /// <c>NoDisclosureGate</c> / <c>NoArPrewarm</c> graceful-no-op precedent).
    /// </summary>
    public sealed class NoReducedMotionSetting : IReducedMotionSetting
    {
        /// <summary>The shared instance (stateless).</summary>
        public static readonly NoReducedMotionSetting Instance = new NoReducedMotionSetting();

        /// <summary>Always false — motion-ON, the safe default for an un-wired seam.</summary>
        public bool ReducedMotionEnabled => false;

        /// <summary>No-op — there is nothing to persist when the seam is unwired.</summary>
        public void SetReducedMotion(bool enabled) { }
    }

    /// <summary>
    /// The concrete <see cref="IReducedMotionSetting"/> over the persisted <c>SaveModel.ReducedMotion</c>
    /// flag (Story 6.6, AC-1). Reads the in-memory current value the host keeps in sync, and
    /// <see cref="SetReducedMotion"/> writes the flag through the <see cref="IProgressStore"/> save
    /// pipeline (one atomic <c>SaveModel</c> write — AR-8), never a second hand-rolled persistence path.
    /// <para>
    /// The host (the deferred Epic-6 settings view / Bootstrap) supplies the live <see cref="SaveModel"/>
    /// it owns + the store; this adapter mutates that model's flag and persists it. It is the deferred
    /// view's bridge from the toggle to the save — the pure presenters/announcer never touch it
    /// (Decision C). Persists synchronously-awaited so a caller can roll back on a persist fault (AR-8).
    /// </para>
    /// </summary>
    public sealed class SaveModelReducedMotionSetting : IReducedMotionSetting
    {
        private readonly SaveModel _model;
        private readonly IProgressStore _store;

        public SaveModelReducedMotionSetting(SaveModel model, IProgressStore store)
        {
            _model = model ?? throw new System.ArgumentNullException(nameof(model));
            _store = store ?? throw new System.ArgumentNullException(nameof(store));
        }

        /// <summary>The persisted opt-in, read off the live model.</summary>
        public bool ReducedMotionEnabled => _model.ReducedMotion;

        /// <summary>
        /// Set the opt-in on the live model and persist it through the save pipeline. Synchronous
        /// wrapper over the async write (the settings toggle is a fire-and-persist; a fault propagates so
        /// the caller can surface it). The single atomic <c>SaveModel</c> write carries the whole model
        /// (AR-8) — the flag travels with the rest of the player state.
        /// </summary>
        public void SetReducedMotion(bool enabled)
        {
            _model.ReducedMotion = enabled;
            SetReducedMotionAsync(enabled).GetAwaiter().GetResult();
        }

        /// <summary>The async write the synchronous <see cref="SetReducedMotion"/> awaits — exposed so a
        /// host that is already on an async path can await it directly.</summary>
        public Task SetReducedMotionAsync(bool enabled)
        {
            _model.ReducedMotion = enabled;
            return _store.SaveAsync(_model);
        }
    }
}
