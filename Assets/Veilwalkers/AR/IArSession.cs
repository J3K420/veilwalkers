using System.Threading.Tasks;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The platform seam over the ARCore / AR Foundation <c>ARSession</c> surface (Story 3.3, FR-4
    /// staging) — the <b>OS-touching edge</b>. It exists so the AR-session LIFECYCLE
    /// (<see cref="ArSessionService"/>'s cold→prewarming→ready→running→paused state machine) is
    /// headless-testable: the service is ctor-injected with this interface and a fake drives it in
    /// EditMode, while the production <see cref="ArcoreSession"/> is the thin, untestable adapter over
    /// the real subsystem (the same shape as <see cref="ICameraPermission"/>/<c>AndroidCameraPermission</c>
    /// in this area, and <c>IClock</c>/<c>SystemClock</c> in Core).
    /// <para>
    /// <b>There is no <c>ARSession</c> in EditMode (architecture.md:591-592):</b> this seam is exactly
    /// why the lifecycle state machine can be CI-tested without one. CI asserts the staging CONTRACT,
    /// not a millisecond budget.
    /// </para>
    /// <para>
    /// <b>Lives in AR, not Core:</b> this is an Android-platform behavior — like
    /// <see cref="IArAnchorProvider"/> it belongs in the AR area, and the service that consumes it lives
    /// in AR too. Core stays platform-free.
    /// </para>
    /// <para>
    /// <b>Thin glue — ZERO branching (architecture.md:466):</b> this seam holds NO lifecycle decisions.
    /// ALL of them (when to start, the double-start guard, the permission re-check, the cheap tear-back)
    /// live in <see cref="ArSessionService"/>. This interface is the adapter the "AR contains no
    /// branching logic worth testing — thin adapter" mandate targets.
    /// </para>
    /// <para>
    /// <b>Async at exactly one member:</b> <see cref="StartAsync"/> is async because the ARCore
    /// subsystem start (subsystem enable + tracking acquisition) is not instantaneous — this is the
    /// expensive warmup. <see cref="Pause"/>/<see cref="Resume"/>/<see cref="Stop"/> are cheap
    /// synchronous toggles.
    /// </para>
    /// </summary>
    public interface IArSession
    {
        /// <summary>
        /// Whether this device supports AR (ARCore availability). When <c>false</c>,
        /// <see cref="ArSessionService"/> refuses to prewarm and degrades gracefully (no-op + warn,
        /// never a crash — NFR-3). Off-device this reports a documented editor default so the lifecycle
        /// is reachable in-editor.
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// Initialize / enable the underlying <c>ARSession</c> subsystem — the expensive warmup
        /// (subsystem start + tracking acquisition). Async because ARCore session start is not
        /// instantaneous. Awaited by <see cref="ArSessionService.PrewarmAsync"/> (the
        /// <see cref="ArSessionState.Prewarming"/> → <see cref="ArSessionState.Ready"/> transition).
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// Pause the running session (backgrounding / app-focus-loss). Cheap and synchronous. Driven
        /// by <see cref="ArSessionService.OnApplicationPause"/> when the app is backgrounded while
        /// running.
        /// </summary>
        void Pause();

        /// <summary>
        /// Resume a paused session (focus-regain). Cheap and synchronous. Driven by
        /// <see cref="ArSessionService.OnApplicationPause"/> on focus-regain, AFTER the service has
        /// re-validated camera permission (a permission revoked while backgrounded must NOT silently
        /// resume — see <see cref="ArSessionService"/>).
        /// </summary>
        void Resume();

        /// <summary>
        /// Disable / tear the session back toward cold (the cheap tear-back on back-out, AC-3).
        /// Synchronous. Driven by <see cref="ArSessionService.TearDown"/> and the interruption path.
        /// <para>
        /// <b>Concurrency contract (load-bearing):</b> <see cref="Stop"/> MUST be safe to call while a
        /// <see cref="StartAsync"/> is still in flight, and MUST abort / cancel that pending start — a
        /// player can back out (<see cref="ArSessionService.TearDown"/>) during the prewarm window. The
        /// implementation must leave the subsystem in a clean, restartable state (a later
        /// <see cref="StartAsync"/> must succeed), and the in-flight <see cref="StartAsync"/> task may
        /// complete or fault but must not leave a half-started subsystem. The editor stub satisfies this
        /// trivially (its <see cref="StartAsync"/> is already complete); the device adapter (Story
        /// 3.4/6.3) must honor it when it drives the real <c>ARSession</c> subsystem
        /// (e.g. <c>ARSession.Reset()</c> / disable cancels an in-progress session init).
        /// </para>
        /// </summary>
        void Stop();
    }
}
