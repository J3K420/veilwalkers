using System;
using System.Threading.Tasks;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Persistence;

namespace Veilwalkers.App
{
    /// <summary>
    /// Records the WRITE-ONCE first-zero-credit day (Story 1.9, AR-15). The
    /// <c>firstZeroCreditDay</c> scalar is the one telemetry value that MAY live in
    /// <c>SaveModel</c> (it is bounded and write-once — the AR-15 exception to "no
    /// counters in SaveModel"); the unbounded per-action/per-session telemetry lives in
    /// the separate <c>telemetry.json</c> instead.
    /// <para>
    /// <see cref="MarkFirstZeroCreditDayAsync"/> sets <c>SaveModel.FirstZeroCreditDay</c>
    /// to the current UTC day-bucket exactly once; a later zero-credit moment never
    /// overwrites a set value. This is the RECORDER only — 1.9 reserves the mechanism;
    /// the CALL SITE (a spend that drops the balance to zero) belongs to the Economy /
    /// Epic 4 code that owns spending, exactly as 1.7/1.8 reserved grant/claim policies
    /// without their UI/triggers.
    /// </para>
    /// </summary>
    public sealed class FirstZeroCreditRecorder
    {
        /// <summary>Sentinel for "not yet recorded" (matches SaveModel's default).</summary>
        public const long Unset = -1;

        private readonly SaveService _saveService;
        private readonly SaveMutationLock _mutationLock;
        private readonly IClock _clock;

        public FirstZeroCreditRecorder(
            SaveService saveService, SaveMutationLock mutationLock, IClock clock)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _mutationLock = mutationLock ?? throw new ArgumentNullException(nameof(mutationLock));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// If <c>FirstZeroCreditDay</c> is still unset, record today's UTC day-bucket and
        /// persist it; otherwise no-op (write-once). Lock-held like every other SaveModel
        /// mutation (AR-8). Returns true iff this call performed the (first) write. A
        /// persist failure rolls the in-memory scalar back to <see cref="Unset"/> so it
        /// retries on the next zero-credit moment.
        /// </summary>
        public async Task<bool> MarkFirstZeroCreditDayAsync()
        {
            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                if (model.FirstZeroCreditDay != Unset)
                {
                    // Already recorded — the write-once guarantee. Never overwrite.
                    return false;
                }

                long todayBucket = DayBucket.For(_clock.UtcNow);
                model.FirstZeroCreditDay = todayBucket;

                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (!ReferenceEquals(_saveService.Current, model))
                    {
                        // A recovery swap replaced the model mid-persist: undo on our
                        // detached instance; the next zero-credit moment retries.
                        model.FirstZeroCreditDay = Unset;
                        GameLog.Warn(
                            "FirstZeroCreditRecorder: write rolled back — save model swapped " +
                            "mid-operation; will retry on the next zero-credit moment.");
                        return false;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    model.FirstZeroCreditDay = Unset;
                    GameLog.Warn(
                        $"FirstZeroCreditRecorder: write did not persist ({ex.Message}); " +
                        "will retry on the next zero-credit moment.");
                    return false;
                }
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        private SaveModel RequireModel()
        {
            SaveModel model = _saveService.Current;
            if (model == null)
            {
                throw new InvalidOperationException(
                    "FirstZeroCreditRecorder has no loaded save model — await " +
                    "SaveService.InitializeAsync before recording the first zero-credit day.");
            }

            return model;
        }
    }
}
