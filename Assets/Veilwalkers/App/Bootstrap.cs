using System.Threading.Tasks;
using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Economy;
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
    /// Mid-wiring failure handling (chosen approach): all service instances are
    /// CONSTRUCTED before the first <c>Register</c> call — constructors are where
    /// realistic failures live, and a constructor throw therefore aborts before the
    /// locator is touched, leaving it clean. <c>Register</c>/<c>MarkReady</c> throw
    /// only on programmer error (null/duplicate/sealed), so a partially-registered
    /// locator is not a reachable runtime state; a wiring failure is treated as
    /// fatal at boot (log + rethrow). A retry surface, if ever wanted, arrives with
    /// scene flow in Story 6.3. Reset-and-rethrow was rejected because no locator
    /// reset exists in player builds (by design — wire-once).
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
                var creditService = new CreditService(saveService);

                GameServices.Register<IClock>(clock);
                GameServices.Register<IProgressStore>(progressStore);
                GameServices.Register<SaveService>(saveService);
                GameServices.Register<ICreditService>(creditService);

                GameServices.MarkReady();
                GameLog.Info("Bootstrap: GameServices wired and sealed.");
            }
            catch (System.Exception ex)
            {
                GameLog.Error(
                    "Bootstrap: wiring failed — fatal at boot (a constructor throw " +
                    $"leaves the locator untouched; see class doc). {ex}");
                throw;
            }

            // Fire-and-forget is acceptable for now (awaited boot staging is AR-14),
            // but the fault MUST be observed so a load failure never dies as an
            // unobserved task exception. Resolve via Get<> so the registered instance
            // is always the one initialized.
            //
            // Because the load is fire-and-forgotten, ICreditService is resolvable
            // BEFORE the save model is loaded; any credit call in that window throws
            // InvalidOperationException by design. No gameplay caller exists yet —
            // Epic 4/6 own call ordering (Bootstrap.LoadPhase / AppStateMachine,
            // AR-14); do not build staging here.
            GameServices.Get<SaveService>().InitializeAsync().ContinueWith(
                task => GameLog.Error(
                    "Bootstrap: SaveService.InitializeAsync faulted: " +
                    task.Exception?.GetBaseException()),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
