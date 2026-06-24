using System.Threading;
using System.Threading.Tasks;
using Veilwalkers.Core;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#endif

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
    /// — Story 8.3 (author the AR rig + the device bodies). The genuinely device-only,
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
        // Story 8.3 device body. Drives the scene-placed AR Foundation ARSession (authored into
        // ARHunt.unity in Gate 1). The ARSession MonoBehaviour does not exist when Bootstrap constructs
        // this adapter ([DefaultExecutionOrder(-1000)], before the rig is live), so the scene component is
        // resolved LAZILY on first use — the ctor never touches the scene. All lifecycle DECISIONS stay in
        // ArSessionService; this is thin subsystem glue. Never throws (NFR-3): every AR Foundation call
        // that can throw is wrapped + logged and degrades.
        private ARSession _session;
        private CancellationTokenSource _startCts;

        // Lazily find the scene's ARSession. Cached once resolved. Returns null (logged) if the rig is
        // not present, so every member degrades to a no-op rather than NRE'ing.
        private ARSession Session()
        {
            if (_session != null)
            {
                return _session;
            }

            _session = Object.FindObjectOfType<ARSession>(includeInactive: true);
            if (_session == null)
            {
                GameLog.Warn("ArcoreSession: no ARSession in the active scene — AR rig not loaded yet; degrading to no-op (NFR-3).");
            }

            return _session;
        }

        // ARCore availability. ARSession.state reflects the subsystem's support/checking result.
        public bool IsSupported
        {
            get
            {
                ARSessionState state = ARSession.state;
                return state != ARSessionState.Unsupported && state != ARSessionState.NeedsInstall;
            }
        }

        // Enable the session + await it reaching a tracking-ready state (the expensive warmup). Honors the
        // Stop-cancels-an-in-flight-StartAsync contract (IArSession.cs L72-82) via _startCts: Stop() cancels
        // the token, so a pending warmup completes promptly and leaves the subsystem restartable.
        public async Task StartAsync()
        {
            ARSession session = Session();
            if (session == null)
            {
                return;
            }

            _startCts?.Cancel();
            _startCts?.Dispose();
            _startCts = new CancellationTokenSource();
            CancellationToken token = _startCts.Token;

            try
            {
                session.enabled = true;

                // Await ARCore moving past init into a usable state (or a terminal unsupported result).
                // SessionInitializing → SessionTracking is the tracking-ready acquisition.
                while (!token.IsCancellationRequested)
                {
                    ARSessionState state = ARSession.state;
                    if (state == ARSessionState.SessionTracking ||
                        state == ARSessionState.Unsupported ||
                        state == ARSessionState.NeedsInstall)
                    {
                        break;
                    }

                    await Task.Yield();
                }
            }
            catch (System.Exception ex)
            {
                GameLog.Warn("ArcoreSession.StartAsync failed; degrading (NFR-3). " + ex.Message);
            }
        }

        public void Pause()
        {
            ARSession session = Session();
            if (session == null)
            {
                return;
            }

            try
            {
                session.enabled = false;
            }
            catch (System.Exception ex)
            {
                GameLog.Warn("ArcoreSession.Pause failed; degrading (NFR-3). " + ex.Message);
            }
        }

        public void Resume()
        {
            ARSession session = Session();
            if (session == null)
            {
                return;
            }

            try
            {
                session.enabled = true;
            }
            catch (System.Exception ex)
            {
                GameLog.Warn("ArcoreSession.Resume failed; degrading (NFR-3). " + ex.Message);
            }
        }

        // Tear back toward cold. MUST cancel an in-flight StartAsync (concurrency contract) and leave the
        // subsystem restartable: cancel the warmup token, then Reset() + disable.
        public void Stop()
        {
            _startCts?.Cancel();

            ARSession session = Session();
            if (session == null)
            {
                return;
            }

            try
            {
                session.Reset();
                session.enabled = false;
            }
            catch (System.Exception ex)
            {
                GameLog.Warn("ArcoreSession.Stop failed; degrading (NFR-3). " + ex.Message);
            }
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
