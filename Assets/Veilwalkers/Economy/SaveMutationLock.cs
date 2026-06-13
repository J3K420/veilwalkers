using System.Threading;
using System.Threading.Tasks;

namespace Veilwalkers.Economy
{
    /// <summary>
    /// The one mutual-exclusion primitive that serializes ALL Economy mutate-and-persist
    /// spans of the shared <c>SaveModel</c>. A tiny wrapper over a
    /// <see cref="SemaphoreSlim"/>(1,1).
    /// <para>
    /// <b>Why one shared instance (the hazard):</b> <c>SaveService.SaveAsync</c>
    /// snapshots <c>Current</c> on the calling thread. If each Economy mutator
    /// (<see cref="CreditService"/>, <see cref="ProgressionService"/>) held its OWN
    /// lock, a credit spend mid-persist could run concurrently with a charge consume,
    /// and one service's <c>SaveAsync</c> could durably write the OTHER service's
    /// not-yet-committed (rollback-eligible) delta — a process kill in that window
    /// would persist a deduction whose spend reported failure. Routing every Economy
    /// mutator through ONE instance closes that interleave: at most one mutate→persist
    /// →rollback span touches the model at a time. Bootstrap constructs exactly one and
    /// injects it into both services.
    /// </para>
    /// <para>
    /// Deliberately minimal: no timeout/cancellation (that is the deferred AR-19 / Epic 6
    /// long-op concern, the same posture as the 1.4 deferral). Callers
    /// <c>await WaitAsync()</c> then <c>Release()</c> in a finally, exactly as a raw
    /// semaphore is used.
    /// </para>
    /// </summary>
    public sealed class SaveMutationLock
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        /// <summary>Asynchronously acquire the lock (one holder at a time).</summary>
        public Task WaitAsync() => _semaphore.WaitAsync();

        /// <summary>Release the lock. Call from a finally that pairs the wait.</summary>
        public void Release() => _semaphore.Release();
    }
}
