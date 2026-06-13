using System;
using System.Threading.Tasks;
using Veilwalkers.Core;

namespace Veilwalkers.Economy
{
    /// <summary>
    /// The progression seam consumed by Encounter (Epic 4), the ad-reward hook
    /// (Story 1.9), and UI (Epic 6). It owns the player-wide XP/level pool and the
    /// three XP-earned <see cref="ChargeType"/> charges. XP is added by gameplay
    /// (Capture grants some, Slay more — amounts come from the caller, never from
    /// here); crossing a level threshold grants the per-level-up charge bundle, and
    /// the whole XP+level+charges change commits as ONE persist (AR-8). Charges are
    /// NEVER purchasable — there is no Shop route for a zero-charge block (contrast
    /// <see cref="ICreditService.OnInsufficientCredits"/>); the remedy is play, so the
    /// caller acts on the typed <see cref="SpendResult"/> directly.
    /// <para>
    /// <b>Typed results vs throws.</b> Expected failures are returned, never thrown: a
    /// zero-charge consume returns <see cref="SpendResult"/> failed with
    /// <see cref="SpendFailureReason.InsufficientCharges"/>; a persist failure returns
    /// failed with <see cref="SpendFailureReason.PersistenceFailed"/> (consume) or a
    /// failed <see cref="Result"/> (add). Throws are reserved for programmer errors:
    /// a non-positive XP amount, an undefined <see cref="ChargeType"/>, use before the
    /// save model is loaded (<see cref="InvalidOperationException"/>), and XP/charge
    /// arithmetic that would overflow (<see cref="OverflowException"/> from checked
    /// arithmetic).
    /// </para>
    /// <para>
    /// <b>Async-suffix deviation (documented).</b> The epic spells <c>AddCharge(...)</c>;
    /// every mutation persists before it commits, so the methods are async and the
    /// architecture's naming rule mandates the <c>Async</c> suffix. The intent
    /// (single charge granted per call; typed results) is unchanged.
    /// </para>
    /// <para>
    /// <b>Threading / events.</b> All three events are raised AFTER the shared Economy
    /// mutation lock is released and only after a COMMITTED persist, possibly on a
    /// background thread (every await uses <c>ConfigureAwait(false)</c>) — UI consumers
    /// (Epic 6) must marshal to the main thread themselves, identical to the
    /// <see cref="ICreditService"/> contract. Delivery order across concurrent
    /// mutations is NOT guaranteed and a payload may already be stale on arrival, so
    /// treat events as change signals and re-read <see cref="Xp"/> / <see cref="Level"/>
    /// / <see cref="GetChargeCount"/> for the authoritative value. There is no
    /// replay-on-subscribe — Epic 6 owns initial binding. A throwing subscriber is
    /// caught and logged, never propagated (a committed mutation must not surface as a
    /// faulted task).
    /// </para>
    /// </summary>
    public interface IProgressionService
    {
        /// <summary>
        /// Lifetime experience points, read from the loaded save model. Throws
        /// <see cref="InvalidOperationException"/> before the model is loaded (call
        /// <c>SaveService.InitializeAsync</c> / recover first). Reads the live model
        /// without taking the mutation lock: while a mutation is in flight the value
        /// may include an added-but-not-yet-committed change that a persist failure
        /// will roll back.
        /// </summary>
        int Xp { get; }

        /// <summary>
        /// Current player level (stored, as-earned — not derived on read, so a 1.6
        /// rebalance of thresholds never silently de-levels a player). Same loaded /
        /// dirty-read caveat as <see cref="Xp"/>.
        /// </summary>
        int Level { get; }

        /// <summary>
        /// The remaining charge count for <paramref name="type"/>. Same loaded /
        /// dirty-read caveat as <see cref="Xp"/>; an undefined <paramref name="type"/>
        /// throws <see cref="ArgumentOutOfRangeException"/>.
        /// </summary>
        int GetChargeCount(ChargeType type);

        /// <summary>
        /// Add <paramref name="amount"/> XP atomically and grant the per-level-up
        /// bundle for every level threshold crossed (one call may cross several), all
        /// in ONE persist (AR-8); a persist failure rolls XP, level, and every charge
        /// count back. An exactly-met threshold counts as reached. <paramref name="amount"/>
        /// must be positive (<see cref="ArgumentOutOfRangeException"/> otherwise); an
        /// amount that would overflow lifetime XP throws <see cref="OverflowException"/>.
        /// Level never decreases here.
        /// </summary>
        Task<Result> AddXpAsync(int amount);

        /// <summary>
        /// Grant exactly ONE charge of <paramref name="type"/> through the same atomic
        /// pipeline (this is the Story 1.9 ad-reward seam — one charge per call). A
        /// persist failure rolls back and returns a failed <see cref="Result"/>. An
        /// undefined <paramref name="type"/> throws <see cref="ArgumentOutOfRangeException"/>;
        /// an overflowing count throws <see cref="OverflowException"/>.
        /// </summary>
        Task<Result> AddChargeAsync(ChargeType type);

        /// <summary>
        /// Consume exactly one charge of <paramref name="type"/>: validate (count &gt; 0)
        /// → decrement → persist; never goes below zero and never touches Credits. A
        /// zero-charge consume is the expected "earn via XP" failure — it returns a
        /// failed <see cref="SpendResult"/> with
        /// <see cref="SpendFailureReason.InsufficientCharges"/>, persists nothing, and
        /// leaves the count untouched. A persist failure rolls back and reports
        /// <see cref="SpendFailureReason.PersistenceFailed"/>. On success
        /// <see cref="SpendResult.NewBalance"/> is the remaining count of that type.
        /// An undefined <paramref name="type"/> throws <see cref="ArgumentOutOfRangeException"/>.
        /// </summary>
        Task<SpendResult> TryConsumeChargeAsync(ChargeType type);

        /// <summary>
        /// Raised after an XP change has been COMMITTED (persisted); payload is the new
        /// lifetime XP. Raised outside the mutation lock, possibly on a background
        /// thread; order across concurrent mutations is not guaranteed, so the payload
        /// may be stale — re-read <see cref="Xp"/> for the authoritative value.
        /// </summary>
        event Action<int> OnXpChanged;

        /// <summary>
        /// Raised after a COMMITTED change in which the level actually changed; payload
        /// is the new level. NOT raised when an XP add crosses no threshold. Same
        /// threading / staleness caveat as <see cref="OnXpChanged"/>.
        /// </summary>
        event Action<int> OnLevelChanged;

        /// <summary>
        /// Raised after a COMMITTED change to a charge count; payload is the
        /// <see cref="ChargeType"/> and that type's new count. Raised once per type
        /// whose count ACTUALLY changed (a level-up that grants 0 of a type raises
        /// nothing for it), so consumers never query back. Same threading / staleness
        /// caveat as <see cref="OnXpChanged"/>.
        /// </summary>
        event Action<ChargeType, int> OnChargesChanged;
    }
}
