using System;
using System.Threading.Tasks;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Persistence;

namespace Veilwalkers.App
{
    /// <summary>
    /// The OQ-4 ad-reward seam (Story 1.9, AR-17), reserved now though ads are not built.
    /// <see cref="GrantRewardAsync"/> grants exactly ONE low-tier charge via
    /// <see cref="IProgressionService.AddChargeAsync"/> — NEVER Credits or XP — capped at
    /// <c>EconomyConfig.AdDailyCap</c> grants per UTC day. App→Economy direction (no
    /// cycle); the charge is Progression's existing atomic write.
    /// <para>
    /// <b>Which charge:</b> <see cref="ChargeType.StabilityBoost"/>, a low-tier encounter
    /// aid. Deliberately NOT <see cref="ChargeType.StrongCapture"/>: the architecture
    /// (AR-17) names Strong Capture as the charge an ad must NOT grant — "an on-demand
    /// Strong Capture charge defeats the earned-progression curve." The <c>ChargeType</c>
    /// enum order is serialization-stability order, not a power order. The exact low tier
    /// (and any randomization) is an OQ-4 tuning decision; the stub pins one deterministic
    /// low-tier charge.
    /// </para>
    /// <para>
    /// <b>Lock discipline (the correctness crux):</b> <see cref="SaveMutationLock"/> is a
    /// NON-reentrant semaphore and <see cref="IProgressionService.AddChargeAsync"/>
    /// acquires it internally, so this hook must NOT hold the lock across the charge call.
    /// The flow is: (lock) read+rollover+cap-check (unlock) → AddChargeAsync (it locks
    /// itself) → (lock) re-check+increment+persist (unlock). The cap counter and the
    /// charge are therefore two separate persists, ordered CHARGE-FIRST so the failure
    /// direction is benign: a crash between them leaves the charge granted but the counter
    /// un-incremented → at most ONE extra grant that day, never a consumed-cap-without-
    /// charge nor a permanent cap bypass. (Folding the counter into the charge's atomic
    /// write would require editing ProgressionService — out of scope.)
    /// </para>
    /// </summary>
    public sealed class AdHook
    {
        /// <summary>The deterministic low-tier charge an ad grants (see class doc).</summary>
        public const ChargeType RewardCharge = ChargeType.StabilityBoost;

        private readonly IProgressionService _progression;
        private readonly SaveService _saveService;
        private readonly SaveMutationLock _mutationLock;
        private readonly IClock _clock;
        private readonly int _adDailyCap;

        public AdHook(
            IProgressionService progression,
            SaveService saveService,
            SaveMutationLock mutationLock,
            IClock clock,
            int adDailyCap)
        {
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _mutationLock = mutationLock ?? throw new ArgumentNullException(nameof(mutationLock));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (adDailyCap <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(adDailyCap), adDailyCap,
                    "EconomyConfig.AdDailyCap must be positive; the ad reward cannot have a non-positive daily cap.");
            }

            _adDailyCap = adDailyCap;
        }

        /// <summary>
        /// Grant one ad reward if the day's cap is not yet reached. Returns a typed
        /// <see cref="AdRewardResult"/>; throws only for the before-load programmer error
        /// (no save model loaded), like the other Economy mutators.
        /// </summary>
        public async Task<AdRewardResult> GrantRewardAsync()
        {
            long todayBucket = DayBucket.For(_clock.UtcNow);

            // --- Phase 1: cap check under the lock (released before the charge) ---
            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                AdRewardData ad = RequireModel().AdReward;
                RollOverIfNewDay(ad, todayBucket);
                if (ad.GrantsToday >= _adDailyCap)
                {
                    return AdRewardResult.Failed(AdRewardFailureReason.CapReached, ad.GrantsToday);
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            // --- Phase 2: grant the charge (AddChargeAsync takes the lock itself) ---
            Result charge = await _progression.AddChargeAsync(RewardCharge).ConfigureAwait(false);
            if (!charge.Success)
            {
                // The charge did not land — do NOT consume the cap. The count for the
                // (informational) result is a lock-free read: the AdReward fields are only
                // mutated under the lock by this hook, and a stale value on a failure
                // result is harmless (the caller re-reads authoritative state if it cares).
                AdRewardData ad = RequireModel().AdReward;
                int grantsNow = ad.DayBucket == todayBucket ? ad.GrantsToday : 0;
                GameLog.Warn(
                    $"AdHook: ad-reward charge did not persist ({charge.Message}); cap not consumed, will retry.");
                return AdRewardResult.Failed(AdRewardFailureReason.ChargeGrantFailed, grantsNow);
            }

            // --- Phase 3: consume the cap (re-acquire; re-check the day; persist) ---
            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                AdRewardData ad = model.AdReward;
                // Re-read the clock: the lock was dropped across Phase 2's I/O, so the UTC
                // day may have crossed midnight since entry. Using the entry bucket here
                // would credit the grant to the wrong (stale) day.
                long bucketNow = DayBucket.For(_clock.UtcNow);
                RollOverIfNewDay(ad, bucketNow);
                ad.GrantsToday += 1;

                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (!ReferenceEquals(_saveService.Current, model))
                    {
                        // A recovery swap replaced the model mid-persist: our increment
                        // landed on the now-detached instance, not on what was written. The
                        // charge stands and the in-memory cap (on the detached model) is
                        // consumed; the on-disk counter on the new Current was not advanced.
                        // Treated like the persist-fail case: Granted is true (the player
                        // got the charge), the on-disk counter self-heals on a later save.
                        GameLog.Warn(
                            "AdHook: save model swapped mid-persist (recovery raced a mutation); " +
                            "charge stands, on-disk cap counter will catch up.");
                    }

                    return AdRewardResult.Succeeded(ad.GrantsToday);
                }
                catch (Exception ex)
                {
                    // Counter persist failed AFTER the charge already landed. Do NOT roll
                    // the in-memory counter back: keeping it incremented means subsequent
                    // calls THIS SESSION read the consumed cap from the live model (Phase 1
                    // reads SaveService.Current, the same in-memory instance), so a
                    // repeatedly-failing disk can NEVER mint more than the cap's worth of
                    // charges in-process — closing the unbounded-bypass hole. The on-disk
                    // counter lags until a later save succeeds; the charge stands, so the
                    // result is still Granted (the player did get the charge).
                    GameLog.Warn(
                        $"AdHook: cap counter did not persist after a granted charge ({ex.Message}); " +
                        "in-memory cap holds for the session, on-disk counter will catch up.");
                    return AdRewardResult.Succeeded(ad.GrantsToday);
                }
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        /// <summary>
        /// Reset the day counter when the stored bucket is not today's (a new UTC day
        /// re-opens the cap). In-memory only; the caller's persist (or the next call)
        /// commits it.
        /// </summary>
        private static void RollOverIfNewDay(AdRewardData ad, long todayBucket)
        {
            if (ad.DayBucket != todayBucket)
            {
                ad.GrantsToday = 0;
                ad.DayBucket = todayBucket;
            }
        }

        private SaveModel RequireModel()
        {
            SaveModel model = _saveService.Current;
            if (model == null)
            {
                throw new InvalidOperationException(
                    "AdHook has no loaded save model — await SaveService.InitializeAsync " +
                    "(or recover the corrupt save) before granting an ad reward.");
            }

            return model;
        }
    }
}
