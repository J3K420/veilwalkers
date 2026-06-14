using System.Threading.Tasks;
using Veilwalkers.AR;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// Test double for <see cref="IArSession"/> (Story 3.3). Mirrors <see cref="FakeCameraPermission"/>:
    /// a settable <see cref="IsSupported"/> the test flips to simulate an unsupported device, plus call
    /// counters so a test can prove the service started/stopped/paused/resumed the session exactly when
    /// expected (the "sole owner / no second StartAsync" proofs). A controllable
    /// <see cref="TaskCompletionSource{T}"/> lets a test hold <see cref="StartAsync"/> in flight to
    /// assert the <see cref="ArSessionState.Prewarming"/> state + the double-start guard deterministically
    /// (no real async timing). No Unity types — the lifecycle logic is platform-independent and proven
    /// entirely here.
    /// </summary>
    internal sealed class FakeArSession : IArSession
    {
        // The gate the test completes to drive StartAsync → done (and thus Prewarming → Ready). When
        // AutoCompleteStart is true (the default), StartAsync returns an already-completed task so
        // synchronous lifecycle tests need not manage the gate; set AutoCompleteStart=false to hold the
        // start in flight and drive it via CompleteStart().
        private TaskCompletionSource<bool> _startGate;

        /// <summary>Settable AR-support state the service reads before prewarming. The test flips it to
        /// model an unsupported device.</summary>
        public bool IsSupported { get; set; } = true;

        /// <summary>When true (default), <see cref="StartAsync"/> completes immediately. Set false to
        /// hold the start in flight (the in-flight double-start guard test).</summary>
        public bool AutoCompleteStart { get; set; } = true;

        /// <summary>How many times the service fired the (expensive) subsystem start. The "no second
        /// StartAsync" double-start guard asserts this stays 1 while a start is in flight.</summary>
        public int StartCount { get; private set; }

        /// <summary>How many times the service paused the running session (backgrounding).</summary>
        public int PauseCount { get; private set; }

        /// <summary>How many times the service resumed a paused session (focus-regain).</summary>
        public int ResumeCount { get; private set; }

        /// <summary>How many times the service tore the session back toward cold.</summary>
        public int StopCount { get; private set; }

        public Task StartAsync()
        {
            StartCount++;

            if (AutoCompleteStart)
            {
                return Task.CompletedTask;
            }

            _startGate = new TaskCompletionSource<bool>();
            return _startGate.Task;
        }

        /// <summary>Complete an in-flight <see cref="StartAsync"/> (only meaningful when
        /// <see cref="AutoCompleteStart"/> is false). Drives <see cref="ArSessionState.Prewarming"/> →
        /// <see cref="ArSessionState.Ready"/>.</summary>
        public void CompleteStart()
        {
            _startGate?.SetResult(true);
        }

        public void Pause() => PauseCount++;

        public void Resume() => ResumeCount++;

        public void Stop() => StopCount++;
    }
}
