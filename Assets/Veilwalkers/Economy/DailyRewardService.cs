using System;
using System.Globalization;
using System.Threading.Tasks;
using Veilwalkers.Core;
using Veilwalkers.Persistence;

namespace Veilwalkers.Economy
{
    /// <summary>
    /// The daily free-credit reward (Story 1.8, FR-2): grant a small Credit top-up at
    /// most once per UTC calendar day. A sibling Economy mutator of the save model — it
    /// uses the SAME shared <see cref="SaveMutationLock"/> as <see cref="CreditService"/>
    /// and <see cref="ProgressionService"/> so its write can never interleave with
    /// theirs (AR-8). The reward amount is data-driven from
    /// <see cref="EconomyConfig.DailyRewardCredits"/> (AR-16); the calendar day comes
    /// only through <see cref="IClock.UtcNow"/> (AR-18), never <c>DateTime.Now</c>.
    /// <para>
    /// <b>One atomic write, by design.</b> A claim mutates TWO fields —
    /// <c>SaveModel.Credits</c> (+ the reward) and <c>SaveModel.DailyClaim</c> (today's
    /// UTC date) — in a SINGLE lock-held persist, rolling BOTH back together on failure.
    /// It deliberately does NOT call <see cref="ICreditService.GrantCreditsAsync"/> and
    /// then stamp the date in a second write: a crash between two persists could leave
    /// credits granted but the day unrecorded, and the retry would grant AGAIN the same
    /// day — a double-claim. (This is the inverse of Story 1.7's two-write
    /// <c>FirstLaunchGrant</c>, where an inter-write crash is benign because a re-grant
    /// equals the accepted reinstall behavior; here it is not benign.)
    /// </para>
    /// <para>
    /// A persist failure here is recoverable and retryable (the player simply re-taps;
    /// nothing is lost), so rollbacks log <see cref="GameLog.Warn"/> — NOT
    /// <see cref="GameLog.Error"/> as <see cref="CreditService"/> does — matching the
    /// <c>FirstLaunchGrant</c> rationale.
    /// </para>
    /// </summary>
    public sealed class DailyRewardService : IDailyRewardService
    {
        private readonly SaveService _saveService;
        private readonly SaveMutationLock _mutationLock;
        private readonly IClock _clock;
        private readonly int _rewardCredits;

        /// <inheritdoc />
        public event Action<int> OnDailyRewardClaimed;

        public DailyRewardService(
            SaveService saveService,
            SaveMutationLock mutationLock,
            IClock clock,
            EconomyConfig economyConfig)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _mutationLock = mutationLock ?? throw new ArgumentNullException(nameof(mutationLock));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (economyConfig == null)
            {
                throw new ArgumentNullException(nameof(economyConfig));
            }

            // Validate the data-driven amount ONCE, at composition: a non-positive daily
            // reward is a config error (a designer typo in EconomyConfig), surfaced at
            // boot rather than silently granting zero or letting a per-claim guard throw
            // mid-session. Same programmer/config-error family as CreditService's
            // argument guards.
            _rewardCredits = economyConfig.DailyRewardCredits;
            if (_rewardCredits <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(economyConfig),
                    _rewardCredits,
                    "EconomyConfig.DailyRewardCredits must be a positive integer; the daily reward cannot grant a non-positive amount.");
            }
        }

        /// <inheritdoc />
        public bool CanClaimToday => IsClaimable(RequireModel().DailyClaim, TodayKey());

        /// <inheritdoc />
        public async Task<DailyRewardResult> TryClaimAsync()
        {
            DailyRewardResult result;
            bool committed = false;
            int newBalance = 0;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                string today = TodayKey();

                if (!IsClaimable(model.DailyClaim, today))
                {
                    // Already claimed on this UTC day: refuse with a typed result, no
                    // mutation, no persist. This is the authoritative gate (the lock-free
                    // CanClaimToday is only an advisory UI hint).
                    return DailyRewardResult.Failed(
                        DailyRewardFailureReason.AlreadyClaimedToday, model.Credits);
                }

                int priorBalance = model.Credits;
                string priorClaim = model.DailyClaim;

                try
                {
                    // Mutate BOTH fields together, in memory, then persist ONCE. checked:
                    // a pathological balance near int.MaxValue must not silently overflow
                    // into the save. The arithmetic is INSIDE this try so an
                    // OverflowException is caught and converted to a PersistenceFailed
                    // result (and both fields rolled back) rather than escaping as a third,
                    // undocumented throw path — TryClaimAsync's contract is: throw only the
                    // before-load InvalidOperationException, report every other failure as a
                    // typed result.
                    checked
                    {
                        model.Credits = priorBalance + _rewardCredits;
                    }
                    model.DailyClaim = today;

                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (ReferenceEquals(_saveService.Current, model))
                    {
                        committed = true;
                        newBalance = model.Credits;
                        result = DailyRewardResult.Succeeded(newBalance);
                    }
                    else
                    {
                        // A recovery swap replaced the model mid-persist: what was written
                        // is not this claim. Roll BOTH fields back on our (now-detached)
                        // instance so memory matches disk; the next attempt re-claims.
                        model.Credits = priorBalance;
                        model.DailyClaim = priorClaim;
                        GameLog.Warn(
                            "DailyRewardService: claim rolled back — the save model was swapped " +
                            "mid-operation (recovery raced a mutation); will be claimable again.");
                        // Report the AUTHORITATIVE balance from the swapped-in model, not this
                        // detached one's priorBalance — DailyRewardResult.NewBalance is documented
                        // as "the unchanged current balance," and after a swap the current model is
                        // _saveService.Current. Fall back to priorBalance only if recovery also
                        // nulled Current (corrupt-and-unrecovered), which RequireModel guards on the
                        // next call anyway.
                        result = DailyRewardResult.Failed(
                            DailyRewardFailureReason.PersistenceFailed,
                            _saveService.Current?.Credits ?? priorBalance);
                    }
                }
                catch (Exception ex)
                {
                    // The mutate-or-persist step faulted (a persist throw, or the checked
                    // overflow). Roll BOTH fields back together so the credit delta and the
                    // claim date revert as a unit (the single-write guarantee); whichever
                    // field had been assigned reverts, the other is already prior.
                    // Recoverable/retryable — Warn, not Error.
                    model.Credits = priorBalance;
                    model.DailyClaim = priorClaim;
                    GameLog.Warn(
                        $"DailyRewardService: claim did not complete ({ex.Message}); " +
                        "balance and claim date unchanged, will retry.");
                    result = DailyRewardResult.Failed(
                        DailyRewardFailureReason.PersistenceFailed, priorBalance);
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            if (committed)
            {
                RaiseDailyRewardClaimed(newBalance);
            }

            return result;
        }

        /// <summary>
        /// Today's UTC calendar day as an ISO-8601 <c>yyyy-MM-dd</c> string — the same
        /// representation stored in <see cref="SaveModel.DailyClaim"/>. Culture-invariant
        /// so the key is stable regardless of the device locale.
        /// </summary>
        private string TodayKey() =>
            _clock.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>
        /// Claimable when the stored claim is absent (never claimed) or belongs to a
        /// STRICTLY EARLIER UTC day. The comparison is <c>&lt; today</c>, not
        /// <c>!= today</c>: both sides are the canonical <c>yyyy-MM-dd</c> key, so
        /// lexicographic order equals date order, and a stored date that is equal to OR
        /// AFTER today must NOT be claimable. Rejecting a future-dated stamp matters
        /// because a tampered/corrupt save (or a device clock that jumped backward) could
        /// otherwise carry a date ahead of "today" that <c>!= today</c> would wrongly
        /// treat as claimable — re-opening the same-day double-claim AC-2 forbids.
        /// </summary>
        private static bool IsClaimable(string storedClaim, string today) =>
            string.IsNullOrEmpty(storedClaim) ||
            string.CompareOrdinal(storedClaim, today) < 0;

        /// <summary>
        /// Invoke <see cref="OnDailyRewardClaimed"/> with subscriber isolation: the claim
        /// is already committed, so a throwing subscriber must be logged, not propagated
        /// (a faulted task here would tell the caller a persisted claim failed). Mirrors
        /// <see cref="CreditService"/>'s event-raising contract.
        /// </summary>
        private void RaiseDailyRewardClaimed(int newBalance)
        {
            try
            {
                OnDailyRewardClaimed?.Invoke(newBalance);
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    $"DailyRewardService: an OnDailyRewardClaimed subscriber threw — the claim is already committed. {ex.Message}");
            }
        }

        /// <summary>
        /// The loaded model, or an <see cref="InvalidOperationException"/> when it is not
        /// loaded yet (Bootstrap fire-and-forgets the initial load, so this service is
        /// resolvable before the model exists) or the save is corrupt and unrecovered —
        /// a bare read there would be a null-ref, not a typed failure. Mirrors
        /// <see cref="CreditService"/>'s before-load contract.
        /// </summary>
        private SaveModel RequireModel()
        {
            SaveModel model = _saveService.Current;
            if (model == null)
            {
                throw new InvalidOperationException(
                    "DailyRewardService has no loaded save model — await SaveService.InitializeAsync " +
                    "(or recover the corrupt save) before reading or claiming the daily reward.");
            }

            return model;
        }
    }
}
