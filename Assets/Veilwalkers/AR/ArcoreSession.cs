using System.Threading.Tasks;
using Veilwalkers.Core;

namespace Veilwalkers.AR
{
    /// <summary>
    /// Production <see cref="IArSession"/> over the ARCore / AR Foundation <c>ARSession</c> subsystem
    /// (Story 3.3) — the untestable adapter edge, the <c>AndroidCameraPermission : ICameraPermission</c>
    /// equivalent. It holds NO branching beyond the platform <c>#if</c> (the AR "thin adapter" mandate,
    /// architecture.md:466); all lifecycle DECISIONS live in <see cref="ArSessionService"/>.
    /// <para>
    /// <b>Logic-complete-but-subsystem-stub (decision #5).</b> The AR Foundation <c>ARSession</c> is a
    /// scene <c>MonoBehaviour</c>, and Story 3.3 builds no scene (consistent with 3.1/3.2). So the
    /// <c>#else</c> editor path is REAL — it reports support and makes the toggles logged no-ops, so the
    /// lifecycle is reachable in-editor and the <see cref="ArSessionService"/> state machine is fully
    /// proven against <c>FakeArSession</c>, platform-independent. The <c>#if UNITY_ANDROID</c> device
    /// path that drives the actual subsystem (enable/disable the scene-placed <c>ARSession</c>, query
    /// <c>ARSession.state</c> for support) is a documented <c>TODO</c> wired when the AR rig scene lands
    /// — Story 3.4 (plane detection / AR rig) / Story 6.3 (scene placement). The genuinely device-only,
    /// CI-untestable subsystem glue (architecture.md:591) is the only thing deferred; the lifecycle
    /// itself ships complete + tested.
    /// </para>
    /// <para>
    /// <b>Never throws (NFR-3):</b> any AR Foundation call that can throw is wrapped + logged via
    /// <see cref="GameLog"/> and degrades, mirroring <c>AndroidCameraPermission.OpenAppSettings</c>.
    /// </para>
    /// </summary>
    public sealed class ArcoreSession : IArSession
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // TODO(Story 3.4 / 6.3): wire to the scene-placed AR Foundation ARSession when the AR rig scene
        // lands. IsSupported → ARSession.state (Ready/SessionInitializing vs Unsupported/NeedsInstall);
        // StartAsync → enable the ARSession component + await the subsystem reaching a tracking-ready
        // state; Pause/Resume → ARSession.enabled toggle; Stop → ARSession.Reset()/disable. Until the
        // rig is placed there is no ARSession instance to drive, so these are conservative stubs that
        // never crash (NFR-3): report supported (the device IS Android) and no-op the toggles. The
        // ArSessionService lifecycle is proven against FakeArSession; this device glue is the deferred,
        // device-only, CI-untestable edge (architecture.md:591).
        public bool IsSupported => true;

        public Task StartAsync()
        {
            GameLog.Info("ArcoreSession.StartAsync: device-path stub — the AR rig ARSession is not placed yet (TODO Story 3.4/6.3).");
            return Task.CompletedTask;
        }

        public void Pause()
        {
            GameLog.Info("ArcoreSession.Pause: device-path stub (TODO Story 3.4/6.3).");
        }

        public void Resume()
        {
            GameLog.Info("ArcoreSession.Resume: device-path stub (TODO Story 3.4/6.3).");
        }

        public void Stop()
        {
            GameLog.Info("ArcoreSession.Stop: device-path stub (TODO Story 3.4/6.3).");
        }
#else
        // Editor / non-Android: the AR Foundation subsystem does not exist. Report supported so the
        // lifecycle (prewarm → ready → run → pause/resume → teardown) is reachable in-editor, and make
        // the toggles logged no-ops. The ArSessionService LOGIC never depends on these side effects —
        // it is proven entirely against FakeArSession in AR.Tests, platform-independent — so editor
        // behavior matches the seam contract.
        public bool IsSupported => true;

        public Task StartAsync()
        {
            GameLog.Info("ArcoreSession.StartAsync: no-op off-device (AR subsystem unavailable; lifecycle reachable in-editor).");
            return Task.CompletedTask;
        }

        public void Pause()
        {
            GameLog.Info("ArcoreSession.Pause: no-op off-device.");
        }

        public void Resume()
        {
            GameLog.Info("ArcoreSession.Resume: no-op off-device.");
        }

        public void Stop()
        {
            GameLog.Info("ArcoreSession.Stop: no-op off-device.");
        }
#endif
    }
}
