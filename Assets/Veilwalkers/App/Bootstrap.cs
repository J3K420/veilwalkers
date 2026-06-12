using UnityEngine;
using Veilwalkers.Core;

namespace Veilwalkers.App
{
    /// <summary>
    /// The single composition entry point. Bootstrap is the ONLY place that wires
    /// <see cref="GameServices"/>: it registers each service instance and then calls
    /// <see cref="GameServices.MarkReady"/> to seal the table, after which the rest
    /// of the app may read services.
    /// <para>
    /// Right now there are no concrete services to wire — they arrive in Story 1.3+
    /// (SaveService, CreditService, …). This stub exists to be the named, single
    /// composition root and to prove the wire-once → read pattern. Bootstrap does
    /// NOT own the AR session, and it does NOT own scene flow (the AppStateMachine
    /// lands in Story 6.3); it only assembles services.
    /// </para>
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private void Awake()
        {
            WireServices();
        }

        /// <summary>
        /// Register every service instance, then seal the table. Service
        /// registrations are added here as later stories introduce them.
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

            // No services to register yet (Story 1.3+ will add them here), e.g.:
            //   GameServices.Register<IClock>(new SystemClock());

            GameServices.MarkReady();
            GameLog.Info("Bootstrap: GameServices wired and sealed.");
        }
    }
}
