using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Veilwalkers.Core;
using Veilwalkers.Persistence;

namespace Veilwalkers.Economy
{
    /// <summary>
    /// The progression spine (<see cref="IProgressionService"/>) over the injected
    /// <see cref="SaveService"/>, <see cref="ProgressionRules"/>, and shared
    /// <see cref="SaveMutationLock"/> (constructor injection — Economy is a pure-logic
    /// area and must not call <c>GameServices.Get&lt;T&gt;()</c>). The loaded
    /// <c>SaveModel</c> is the single source of truth: XP, level, and the three charge
    /// counts live ONLY there (read through <see cref="RequireModel"/>), so this
    /// service keeps no duplicate state and can never desynchronize from a recovery
    /// swap or another mutator of the same model.
    /// <para>
    /// This deliberately clones the <see cref="CreditService"/> AR-8 pipeline:
    /// capture the model reference once, mutate in memory, ONE
    /// <c>SaveService.SaveAsync</c>, post-persist
    /// <c>ReferenceEquals(Current, model)</c> recovery-swap check, roll the captured
    /// snapshot back onto the captured reference on any fault, release the shared lock,
    /// then raise events (committed, isolated, possibly background-thread). The shared
    /// lock serializes these spans against credit mutations too — see
    /// <see cref="SaveMutationLock"/>. <see cref="AddXpAsync"/> has the widest rollback
    /// set in the codebase (XP + level + three charge counts): all are snapshotted
    /// before any mutation and restored (never recomputed) on fault.
    /// </para>
    /// </summary>
    public sealed class ProgressionService : IProgressionService
    {
        private static readonly ChargeType[] AllChargeTypes =
        {
            ChargeType.StrongCapture,
            ChargeType.StabilityBoost,
            ChargeType.NightveilFilter
        };

        private readonly SaveService _saveService;
        private readonly ProgressionRules _rules;
        private readonly SaveMutationLock _mutationLock;

        /// <inheritdoc />
        public event Action<int> OnXpChanged;

        /// <inheritdoc />
        public event Action<int> OnLevelChanged;

        /// <inheritdoc />
        public event Action<ChargeType, int> OnChargesChanged;

        /// <summary>
        /// The <paramref name="mutationLock"/> MUST be the SAME instance shared with
        /// <see cref="CreditService"/> (see <see cref="SaveMutationLock"/>); Bootstrap
        /// constructs one and injects it into both.
        /// </summary>
        public ProgressionService(
            SaveService saveService, ProgressionRules rules, SaveMutationLock mutationLock)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _mutationLock = mutationLock ?? throw new ArgumentNullException(nameof(mutationLock));
        }

        /// <inheritdoc />
        public int Xp => RequireModel().Xp;

        /// <inheritdoc />
        public int Level => RequireModel().Level;

        /// <inheritdoc />
        public int GetChargeCount(ChargeType type) => ChargeInventory.GetCount(RequireModel(), type);

        /// <inheritdoc />
        public async Task<Result> AddXpAsync(int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount), amount, "An XP grant must be a positive integer.");
            }

            Result result;
            bool committed = false;

            // Captured outside the lock scope so the post-release raise can use them.
            int newXp = 0;
            int priorLevel = 0;
            int newLevel = 0;
            var newCounts = new int[AllChargeTypes.Length];
            var priorCounts = new int[AllChargeTypes.Length];

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Capture the model reference ONCE: a recovery swap mid-mutation must
                // never make the rollback write stale values onto a NEW model.
                SaveModel model = RequireModel();

                int priorXp = model.Xp;
                priorLevel = model.Level;
                for (int i = 0; i < AllChargeTypes.Length; i++)
                {
                    priorCounts[i] = ChargeInventory.GetCount(model, AllChargeTypes[i]);
                }

                // Compute the entire post-state into LOCALS first, mutating nothing on the
                // model. All checked-arithmetic throws (XP overflow, charge-grant overflow)
                // therefore fire BEFORE any model write — overflow stays a clean
                // programmer-error throw (the documented contract) with the model
                // untouched, so there is nothing to roll back. Only the persist (and the
                // recovery-swap) below needs rollback.
                checked
                {
                    newXp = priorXp + amount;
                }

                // Clamp to >= 0: XP only rises, but Level is stored as-earned (not
                // derived on read), so a future threshold rebalance (Story 1.6) can
                // leave stored Level > LevelForXp(Xp). Without the clamp the next add
                // would compute a negative gain, decrease Level (this method must
                // NEVER decrease it), and produce negative charge grants. The clamp
                // makes "no level gained ⇒ no grant" the floor; a rebalance migration
                // owns any re-leveling.
                int levelsGained = Math.Max(0, _rules.LevelForXp(newXp) - priorLevel);

                for (int i = 0; i < AllChargeTypes.Length; i++)
                {
                    int grantEach = _rules.GrantPerLevelUp(AllChargeTypes[i]);
                    checked
                    {
                        newCounts[i] = priorCounts[i] + (grantEach * levelsGained);
                    }
                }

                // Derive the committed level from the SAME clamped gain so Level is
                // monotonic: a rebalance that lowered LevelForXp(Xp) below the stored
                // level leaves Level unchanged rather than de-leveling the player.
                newLevel = priorLevel + levelsGained;

                // All arithmetic succeeded — now write the locals onto the model and
                // persist. The rollback try opens here, around the mutate-and-persist
                // span, because from this point a persist fault or a recovery swap must
                // restore the full captured snapshot.
                try
                {
                    model.Xp = newXp;
                    for (int i = 0; i < AllChargeTypes.Length; i++)
                    {
                        ChargeInventory.SetCount(model, AllChargeTypes[i], newCounts[i]);
                    }

                    model.Level = newLevel;

                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (ReferenceEquals(_saveService.Current, model))
                    {
                        // newXp / newCounts already hold the committed post-state (computed
                        // into locals above and written verbatim to the model), so the
                        // post-release event raise reads them directly.
                        committed = true;
                        result = Result.Ok();
                    }
                    else
                    {
                        // A recovery swap replaced the model mid-operation: what
                        // SaveAsync persisted is not this XP grant.
                        Rollback(model, priorXp, priorLevel, priorCounts);
                        GameLog.Error(
                            $"ProgressionService: XP grant of {amount} rolled back — the save model was swapped mid-operation (recovery raced a mutation).");
                        result = Result.Fail("The XP grant could not be saved; progression is unchanged.");
                    }
                }
                catch (Exception ex)
                {
                    // Restore every captured field onto the captured reference — never
                    // recompute (anything else may have moved) and never re-read Current.
                    // This catch now covers only the persist fault (the mutate-span
                    // overflow/guard throws happen above, before any model write).
                    Rollback(model, priorXp, priorLevel, priorCounts);
                    GameLog.Error(
                        $"ProgressionService: XP grant of {amount} rolled back — persist failed. {ex.Message}");
                    result = Result.Fail("The XP grant could not be saved; progression is unchanged.");
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            if (committed)
            {
                RaiseXpChanged(newXp);
                if (newLevel != priorLevel)
                {
                    RaiseLevelChanged(newLevel);
                }

                for (int i = 0; i < AllChargeTypes.Length; i++)
                {
                    if (newCounts[i] != priorCounts[i])
                    {
                        RaiseChargesChanged(AllChargeTypes[i], newCounts[i]);
                    }
                }
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<Result> AddChargeAsync(ChargeType type)
        {
            // Validate the type up front (throws for an undefined value) so the guard
            // is not hidden behind the lock acquisition.
            EnsureDefined(type);

            Result result;
            bool committed = false;
            int newCount = 0;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                int priorCount = ChargeInventory.GetCount(model, type);

                // Compute the increment into a local first: a checked overflow throws
                // before any model write (clean programmer-error throw, model untouched,
                // nothing to roll back), symmetric with AddXpAsync.
                int target;
                checked
                {
                    target = priorCount + 1;
                }

                newCount = target;

                // The rollback try opens around the mutate-and-persist span: from the
                // model write on, a persist fault or recovery swap must restore the count.
                try
                {
                    ChargeInventory.SetCount(model, type, target);

                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (ReferenceEquals(_saveService.Current, model))
                    {
                        committed = true;
                        result = Result.Ok();
                    }
                    else
                    {
                        ChargeInventory.SetCount(model, type, priorCount);
                        GameLog.Error(
                            $"ProgressionService: charge grant ({type}) rolled back — the save model was swapped mid-operation (recovery raced a mutation).");
                        result = Result.Fail("The charge grant could not be saved; charges are unchanged.");
                    }
                }
                catch (Exception ex)
                {
                    ChargeInventory.SetCount(model, type, priorCount);
                    GameLog.Error(
                        $"ProgressionService: charge grant ({type}) rolled back — persist failed. {ex.Message}");
                    result = Result.Fail("The charge grant could not be saved; charges are unchanged.");
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            if (committed)
            {
                RaiseChargesChanged(type, newCount);
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<SpendResult> TryConsumeChargeAsync(ChargeType type)
        {
            EnsureDefined(type);

            SpendResult result;
            bool committed = false;
            int newCount = 0;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                int priorCount = ChargeInventory.GetCount(model, type);

                if (priorCount == 0)
                {
                    // Expected "earn via XP" failure: the only decrement is guarded by
                    // this check, so the count can never go negative. No persist, no
                    // event, nothing mutated.
                    result = SpendResult.Failed(SpendFailureReason.InsufficientCharges, 0);
                }
                else
                {
                    ChargeInventory.SetCount(model, type, priorCount - 1);
                    try
                    {
                        await _saveService.SaveAsync().ConfigureAwait(false);

                        if (ReferenceEquals(_saveService.Current, model))
                        {
                            committed = true;
                            newCount = ChargeInventory.GetCount(model, type);
                            result = SpendResult.Succeeded(newCount);
                        }
                        else
                        {
                            ChargeInventory.SetCount(model, type, priorCount);
                            GameLog.Error(
                                $"ProgressionService: charge consume ({type}) rolled back — the save model was swapped mid-operation (recovery raced a mutation).");
                            result = SpendResult.Failed(SpendFailureReason.PersistenceFailed, priorCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        ChargeInventory.SetCount(model, type, priorCount);
                        GameLog.Error(
                            $"ProgressionService: charge consume ({type}) rolled back — persist failed. {ex.Message}");
                        result = SpendResult.Failed(SpendFailureReason.PersistenceFailed, priorCount);
                    }
                }
            }
            finally
            {
                _mutationLock.Release();
            }

            if (committed)
            {
                RaiseChargesChanged(type, newCount);
            }

            return result;
        }

        /// <summary>Restore the full captured snapshot onto the captured reference.</summary>
        private static void Rollback(
            SaveModel model, int priorXp, int priorLevel, IReadOnlyList<int> priorCounts)
        {
            model.Xp = priorXp;
            model.Level = priorLevel;
            for (int i = 0; i < AllChargeTypes.Length; i++)
            {
                ChargeInventory.SetCount(model, AllChargeTypes[i], priorCounts[i]);
            }
        }

        private void RaiseXpChanged(int newXp)
        {
            try
            {
                OnXpChanged?.Invoke(newXp);
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    $"ProgressionService: an OnXpChanged subscriber threw — the mutation is already committed. {ex.Message}");
            }
        }

        private void RaiseLevelChanged(int newLevel)
        {
            try
            {
                OnLevelChanged?.Invoke(newLevel);
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    $"ProgressionService: an OnLevelChanged subscriber threw — the mutation is already committed. {ex.Message}");
            }
        }

        private void RaiseChargesChanged(ChargeType type, int newCount)
        {
            try
            {
                OnChargesChanged?.Invoke(type, newCount);
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    $"ProgressionService: an OnChargesChanged subscriber threw — the mutation is already committed. {ex.Message}");
            }
        }

        /// <summary>
        /// Throw for an undefined <see cref="ChargeType"/> (programmer error). Defined
        /// members are validated via the same switch that <see cref="ChargeInventory"/>
        /// uses; this surfaces the guard before the lock is taken.
        /// </summary>
        private static void EnsureDefined(ChargeType type)
        {
            switch (type)
            {
                case ChargeType.StrongCapture:
                case ChargeType.StabilityBoost:
                case ChargeType.NightveilFilter:
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(type), type, "Unknown charge type.");
            }
        }

        /// <summary>
        /// The loaded model, or an <see cref="InvalidOperationException"/> when it is
        /// not loaded yet (Bootstrap fire-and-forgets the initial load, so this service
        /// is resolvable before the model exists) or the save is corrupt and
        /// unrecovered. Applied to every member, getters included.
        /// </summary>
        private SaveModel RequireModel()
        {
            SaveModel model = _saveService.Current;
            if (model == null)
            {
                throw new InvalidOperationException(
                    "ProgressionService has no loaded save model — await SaveService.InitializeAsync " +
                    "(or recover the corrupt save) before reading or mutating progression.");
            }

            return model;
        }
    }
}
