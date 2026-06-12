using System.Threading.Tasks;
using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Persistence;

namespace Veilwalkers.App
{
    /// <summary>
    /// The single composition entry point. Bootstrap is the ONLY place that wires
    /// <see cref="GameServices"/>: it registers each service instance and then calls
    /// <see cref="GameServices.MarkReady"/> to seal the table, after which the rest
    /// of the app may read services.
    /// <para>
    /// Lives in the Bootstrap entry scene (scene 0 in Build Settings) and runs at
    /// <c>[DefaultExecutionOrder(-1000)]</c> so no other <c>Awake</c> can race the
    /// composition root. Bootstrap does NOT own the AR session, and it does NOT own
    /// scene flow (the AppStateMachine lands in Story 6.3); it only assembles
    /// services and kicks the initial save load.
    /// </para>
    /// <para>
    /// Mid-wiring failure recovery (chosen approach): all service instances are
    /// CONSTRUCTED before the first <c>Register</c> call (constructors are where
    /// realistic failures live, and a constructor throw therefore aborts before the
    /// locator is touched), and every registration is guarded by
    /// <see cref="GameServices.IsRegistered{T}"/> so re-running the wiring after a
    /// partial failure skips what already landed instead of dying on the
    /// duplicate-registration guard. Reset-and-rethrow was rejected because no
    /// locator reset exists in player builds (by design — wire-once).
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class Bootstrap : MonoBehaviour
    {
        private void Awake()
        {
            WireServices();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Returns <see cref="GameServices"/> statics to the unready state at the
        /// start of every Editor play session. Today Enter Play Mode Options are OFF
        /// (full domain reload — this is then a harmless no-op on fresh statics), but
        /// <c>EditorSettings.asset</c> already stores the fast-enter option flags, so
        /// this future-proofs anyone later enabling them. Editor-only: a player
        /// process always starts with fresh statics.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForEnterPlayMode()
        {
            GameServices.ResetForFastEnterPlayMode();
        }
#endif

        /// <summary>
        /// Register every service instance, then seal the table and kick the initial
        /// save load. Service registrations are added here as later stories introduce
        /// them.
        /// </summary>
        private void WireServices()
        {
            // Guard against re-entry (e.g. a duplicate Bootstrap in a loaded scene):
            // the wiring runs exactly once for the lifetime of the process.
            if (GameServices.IsReady)
            {
                GameLog.Warn("Bootstrap.WireServices skipped: GameServices is already wired.");
                return;
            }

            try
            {
                // Construct first (see class doc: failures here leave the locator
                // untouched). persistentDataPath is main-thread-only, which is why it
                // is read HERE and passed into the store rather than inside it.
                var clock = new SystemClock();
                var progressStore = new LocalProgressStore(Application.persistentDataPath);
                var saveService = new SaveService(progressStore);

                if (!GameServices.IsRegistered<IClock>())
                {
                    GameServices.Register<IClock>(clock);
                }

                if (!GameServices.IsRegistered<IProgressStore>())
                {
                    GameServices.Register<IProgressStore>(progressStore);
                }

                if (!GameServices.IsRegistered<SaveService>())
                {
                    GameServices.Register<SaveService>(saveService);
                }

                GameServices.MarkReady();
                GameLog.Info("Bootstrap: GameServices wired and sealed.");
            }
            catch (System.Exception ex)
            {
                GameLog.Error(
                    "Bootstrap: wiring failed — locator left recoverable (idempotent " +
                    $"re-run will skip already-registered services). {ex}");
                throw;
            }

            // Fire-and-forget is acceptable for now (awaited boot staging is AR-14),
            // but the fault MUST be observed so a load failure never dies as an
            // unobserved task exception. Resolve via Get<> so the registered instance
            // is always the one initialized.
            GameServices.Get<SaveService>().InitializeAsync().ContinueWith(
                task => GameLog.Error(
                    "Bootstrap: SaveService.InitializeAsync faulted: " +
                    task.Exception?.GetBaseException()),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
