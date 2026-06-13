using System;
using System.Threading;
using System.Threading.Tasks;
using Veilwalkers.Core;
using Veilwalkers.Persistence;

namespace Veilwalkers.Economy
{
    /// <summary>
    /// The Credit ledger (<see cref="ICreditService"/>) over the injected
    /// <see cref="SaveService"/> (constructor injection — Economy is a pure-logic
    /// area and must not call <c>GameServices.Get&lt;T&gt;()</c>). The loaded
    /// <c>SaveModel</c> is the single source of truth: <see cref="Balance"/> reads
    /// <c>SaveService.Current.Credits</c> and this class keeps NO duplicate balance
    /// field, so recovery swaps (<c>StartFreshAsync</c>/<c>RetryLoadAsync</c>) and
    /// later stories mutating the same model can never desynchronize it.
    /// <para>
    /// Pipeline (AR-8): validate → deduct → persist; the mutation is committed only
    /// once <c>SaveService.SaveAsync</c> succeeds, and a persist fault rolls the
    /// in-memory mutation back onto the model reference captured at operation entry.
    /// Mutations are serialized by a <see cref="SemaphoreSlim"/>(1,1) so two
    /// in-flight mutations can never interleave deduct/persist/rollback. Events are
    /// raised AFTER the semaphore is released (mutate → persist → release → raise):
    /// raising inside the lock would deadlock any subscriber that synchronously
    /// blocks on another credit mutation. With <c>ConfigureAwait(false)</c> on every
    /// await, events may fire on a background thread — UI marshals (Epic 6).
    /// </para>
    /// </summary>
    public sealed class CreditService : ICreditService
    {
        private readonly SaveService _saveService;
        private readonly SemaphoreSlim _mutationLock = new SemaphoreSlim(1, 1);

        /// <inheritdoc />
        public event Action<int> OnCreditsChanged;

        /// <inheritdoc />
        public event Action<InsufficientCreditsEvent> OnInsufficientCredits;

        public CreditService(SaveService saveService)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        }

        /// <inheritdoc />
        public int Balance => RequireModel().Credits;

        /// <inheritdoc />
        public async Task<SpendResult> TrySpendCreditsAsync(int cost)
        {
            if (cost <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cost), cost, "A spend cost must be a positive integer.");
            }

            SpendResult result;
            bool committed = false;
            bool insufficient = false;
            InsufficientCreditsEvent insufficientEvent = default;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Capture the model reference ONCE and use it for the affordability
                // check, deduct, and rollback: a recovery swap mid-mutation must
                // never make the rollback write a stale balance onto a NEW model.
                SaveModel model = RequireModel();

                if (model.Credits < cost)
                {
                    insufficient = true;
                    insufficientEvent = new InsufficientCreditsEvent(cost, model.Credits);
                    result = SpendResult.Failed(SpendFailureReason.InsufficientCredits, model.Credits);
                }
                else
                {
                    int priorBalance = model.Credits;
                    model.Credits = priorBalance - cost;
                    try
                    {
                        await _saveService.SaveAsync().ConfigureAwait(false);
                        committed = true;
                        result = SpendResult.Succeeded(model.Credits);
                    }
                    catch (Exception ex)
                    {
                        // Restore, don't recompute — onto the captured reference.
                        model.Credits = priorBalance;
                        GameLog.Error(
                            $"CreditService: spend of {cost} rolled back — persist failed. {ex.Message}");
                        result = SpendResult.Failed(SpendFailureReason.PersistenceFailed, priorBalance);
                    }
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            if (insufficient)
            {
                OnInsufficientCredits?.Invoke(insufficientEvent);
            }
            else if (committed)
            {
                OnCreditsChanged?.Invoke(result.NewBalance);
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<Result> GrantCreditsAsync(int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount), amount, "A credit grant must be a positive integer.");
            }

            Result result;
            bool committed = false;
            int newBalance = 0;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                int priorBalance = model.Credits;

                // checked: a buggy caller (e.g. a 5.2 reconciliation replay) must not
                // overflow a negative balance into the save. OverflowException is a
                // programmer-error throw, same family as the argument guards.
                checked
                {
                    model.Credits = priorBalance + amount;
                }

                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);
                    committed = true;
                    newBalance = model.Credits;
                    result = Result.Ok();
                }
                catch (Exception ex)
                {
                    model.Credits = priorBalance;
                    GameLog.Error(
                        $"CreditService: grant of {amount} rolled back — persist failed. {ex.Message}");
                    result = Result.Fail("The credit grant could not be saved; the balance is unchanged.");
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            if (committed)
            {
                OnCreditsChanged?.Invoke(newBalance);
            }

            return result;
        }

        /// <summary>
        /// The loaded model, or an <see cref="InvalidOperationException"/> when it
        /// is not loaded yet (Bootstrap fire-and-forgets the initial load, so this
        /// service is resolvable before the model exists) or the save is corrupt
        /// and unrecovered — a bare read there would be a null-ref, not a typed
        /// failure.
        /// </summary>
        private SaveModel RequireModel()
        {
            SaveModel model = _saveService.Current;
            if (model == null)
            {
                throw new InvalidOperationException(
                    "CreditService has no loaded save model — await SaveService.InitializeAsync " +
                    "(or recover the corrupt save) before reading or mutating credits.");
            }

            return model;
        }
    }
}
