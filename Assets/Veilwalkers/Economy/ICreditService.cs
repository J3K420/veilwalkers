using System;
using System.Threading.Tasks;
using Veilwalkers.Core;

namespace Veilwalkers.Economy
{
    /// <summary>
    /// The Credit ledger seam consumed by UI, Encounter, and Billing. Balances are
    /// integers only; spends return a typed <see cref="SpendResult"/> and NEVER
    /// throw for an expected failure ("can't afford" → <see
    /// cref="SpendFailureReason.InsufficientCredits"/>, persist failure →
    /// <see cref="SpendFailureReason.PersistenceFailed"/>). Throws are reserved for
    /// programmer errors: non-positive amounts and use before the save model is
    /// loaded.
    /// <para>
    /// Naming deviation (documented): the epic writes <c>TrySpendCredits(cost)</c>,
    /// but every spend persists before it is committed (AR-8), so the method is
    /// async and the architecture's naming rule mandates the <c>Async</c> suffix —
    /// the intent (typed result, never throws for can't-afford) is unchanged.
    /// </para>
    /// <para>
    /// Threading: both events are raised outside the internal mutation lock, only
    /// after a committed persist, and possibly on a background thread — UI
    /// consumers (Epic 6) must marshal to the main thread themselves, identical to
    /// the <c>SaveService</c> event contract.
    /// </para>
    /// </summary>
    public interface ICreditService
    {
        /// <summary>
        /// The current credit balance, read from the loaded save model. Throws
        /// <see cref="InvalidOperationException"/> before the model is loaded
        /// (call <c>SaveService.InitializeAsync</c> / recover first).
        /// </summary>
        int Balance { get; }

        /// <summary>
        /// Attempt to spend <paramref name="cost"/> credits atomically:
        /// validate → deduct → persist; a persist failure rolls the deduction back
        /// (AR-8). Exact balance (<c>Balance == cost</c>) succeeds to zero. Never
        /// throws for an expected failure; <c>cost &lt;= 0</c> throws
        /// <see cref="ArgumentOutOfRangeException"/>.
        /// </summary>
        Task<SpendResult> TrySpendCreditsAsync(int cost);

        /// <summary>
        /// Grant <paramref name="amount"/> credits through the same atomic
        /// pipeline (persist failure rolls back and returns a failed
        /// <see cref="Result"/>). Deliberately dumb — first-launch/pack semantics
        /// live with the callers (Stories 1.7, 5.1). <c>amount &lt;= 0</c> throws
        /// <see cref="ArgumentOutOfRangeException"/>.
        /// </summary>
        Task<Result> GrantCreditsAsync(int amount);

        /// <summary>
        /// Raised after a credit mutation has been COMMITTED (persisted); payload
        /// is the new balance. Raised outside the mutation lock, possibly on a
        /// background thread.
        /// </summary>
        event Action<int> OnCreditsChanged;

        /// <summary>
        /// Raised when a spend fails because balance &lt; cost. The service only
        /// raises this event — it never opens the Shop or calls Billing/UI itself
        /// (AR-11); App/UI decides how to react. Raised outside the mutation lock,
        /// possibly on a background thread.
        /// </summary>
        event Action<InsufficientCreditsEvent> OnInsufficientCredits;
    }
}
