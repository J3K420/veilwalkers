using System.Threading.Tasks;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The narrow AR-session prewarm seam the onboarding flow kicks during the premise cards (Story 6.4,
    /// derived from architecture.md:575-584 — the "warmup ownership rule"). Onboarding/Home do NOT warm
    /// the AR session themselves; they CALL prewarm so that by the time the player taps "Enter AR Hunt"
    /// the session + ≥1 Monster are warm (the cold-start budget is launch → first plane anchor, not
    /// launch → camera).
    /// <para>
    /// A 1-method port (the <c>IEncounterSnapshotPort</c> / <see cref="IDisclosureGate"/> precedent) so
    /// the onboarding flow is fakeable in EditMode and degrades when <c>ArSessionService</c> is an
    /// unregistered Bootstrap seam (the AR-rig scene is unbuilt). The adapter onto the real
    /// <c>ArSessionService.PrewarmAsync()</c> (which is itself double-start-safe and refuses gracefully
    /// if the device is unsupported / permission ungranted) is wired with the deferred onboarding View;
    /// <see cref="NoPrewarm"/> is the graceful no-op until then.
    /// </para>
    /// </summary>
    public interface IArPrewarmPort
    {
        /// <summary>
        /// Best-effort warm the AR session. Maps onto <c>ArSessionService.PrewarmAsync()</c>. Prewarm is
        /// best-effort: a fault must never crash onboarding (the caller observes/guards the task — the
        /// 6.3 fire-and-forget-snapshot discipline). Idempotent / double-start-safe at the service.
        /// </summary>
        Task PrewarmAsync();

        /// <summary>
        /// The graceful no-op used when <c>ArSessionService</c> is not registered (the deferred Bootstrap
        /// seam blocked on the AR-rig scene): onboarding still completes; no session warms (the cold
        /// entry will prewarm on demand — <c>ArSessionService</c> prewarms from Cold inside
        /// <c>RunAsync</c>). NFR-3 graceful.
        /// </summary>
        public static readonly IArPrewarmPort NoPrewarm = new NoOpPrewarmPort();

        private sealed class NoOpPrewarmPort : IArPrewarmPort
        {
            public Task PrewarmAsync() => Task.CompletedTask;
        }
    }
}
