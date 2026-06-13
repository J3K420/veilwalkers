using System;
using System.Threading.Tasks;
using Veilwalkers.Core;

namespace Veilwalkers.Economy
{
    /// <summary>
    /// The daily free-credit reward seam (Story 1.8, FR-2). A returning player may
    /// claim a small Credit top-up at most once per UTC calendar day; the amount is
    /// data-driven from <see cref="EconomyConfig.DailyRewardCredits"/> (AR-16). The
    /// "what day is it" decision routes through <see cref="IClock"/> (AR-18) so the
    /// rule is deterministically fakeable — no logic reads <c>DateTime.Now</c>.
    /// <para>
    /// Typed-result contract (AR-7): <see cref="TryClaimAsync"/> NEVER throws for an
    /// expected outcome ("already claimed today" → a refused
    /// <see cref="DailyRewardResult"/>); it throws only for programmer errors (claim
    /// before the save model is loaded). The service never opens UI or navigates
    /// (AR-11) — App/UI reads <see cref="CanClaimToday"/> to enable the affordance and
    /// reacts to the returned result.
    /// </para>
    /// <para>
    /// The claim is ONE atomic save write (AR-8): the credit delta and the
    /// claim-date stamp commit together or not at all, so a persist failure can never
    /// leave credits granted without the day recorded (which would allow a same-day
    /// double-claim). See <see cref="DailyRewardService"/>.
    /// </para>
    /// </summary>
    public interface IDailyRewardService
    {
        /// <summary>
        /// Whether the reward is claimable on the current UTC calendar day — true when
        /// it has never been claimed or was last claimed on an earlier UTC day. An
        /// ADVISORY read for UI to enable/disable the claim affordance; the
        /// authoritative gate is the re-check inside <see cref="TryClaimAsync"/> under
        /// the mutation lock (a stale <c>true</c> here is harmless — a concurrent claim
        /// is refused at the gate). Throws <see cref="InvalidOperationException"/>
        /// before the save model is loaded, the same before-load contract as
        /// <see cref="ICreditService.Balance"/>.
        /// </summary>
        bool CanClaimToday { get; }

        /// <summary>
        /// Claim the daily reward atomically: under the shared mutation lock, re-check
        /// the UTC day, then (if claimable) add <see cref="EconomyConfig.DailyRewardCredits"/>
        /// to the balance AND stamp <c>SaveModel.DailyClaim</c> with today's UTC date in
        /// a SINGLE persist; a persist failure rolls BOTH back (AR-8). Returns a refused
        /// result (never throws) when already claimed today. Throws
        /// <see cref="InvalidOperationException"/> if called before the save model is
        /// loaded.
        /// </summary>
        Task<DailyRewardResult> TryClaimAsync();

        /// <summary>
        /// Raised after a daily-reward claim has been COMMITTED (persisted); payload is
        /// the new balance. Raised OUTSIDE the mutation lock, possibly on a background
        /// thread; the payload may be stale by arrival — treat it as a change signal
        /// and re-read the authoritative balance (identical to the
        /// <see cref="ICreditService.OnCreditsChanged"/> contract). UI consumers
        /// (Epic 6) must marshal to the main thread themselves.
        /// </summary>
        event Action<int> OnDailyRewardClaimed;
    }
}
