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

            // The staged grant — captured outside the lock scope so the post-release raise can use it.
            ProgressionStage stage = default;

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Capture the model reference ONCE: a recovery swap mid-mutation must
                // never make the rollback write stale values onto a NEW model.
                SaveModel model = RequireModel();

                // Compute + apply the entire XP/level/charge-grant delta via the shared staging seam (Story
                // 4.4 extracted this from AddXpAsync so the standalone XP path AND the composed Capture write
                // share ONE arithmetic implementation). StageXpGrant does the checked arithmetic BEFORE any
                // model write (overflow stays a clean programmer-error throw, model untouched, nothing to roll
                // back), then writes the locals onto the model. From here a persist fault / recovery swap must
                // restore the captured snapshot — stage.Revert() does that.
                stage = StageXpGrant(model, amount);

                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (ReferenceEquals(_saveService.Current, model))
                    {
                        committed = true;
                        result = Result.Ok();
                    }
                    else
                    {
                        // A recovery swap replaced the model mid-operation: what
                        // SaveAsync persisted is not this XP grant.
                        stage.Revert();
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
                    // overflow/guard throws happen in StageXpGrant, before any model write).
                    stage.Revert();
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
                stage.RaiseCommittedEvents();
            }

            return result;
        }

        /// <summary>
        /// Compute + apply an XP grant's FULL delta (XP, level, per-level-up charge grants) onto
        /// <paramref name="model"/> WITHOUT taking this service's lock and WITHOUT persisting — the staging
        /// seam (the <see cref="CodexService"/> <c>StageDiscovery</c> precedent) the composed Encounter Capture
        /// write (Story 4.4) uses so a successful Capture grants XP IN THE SAME single atomic save as the charge
        /// consume + the Codex discovery (AR-8 — one persist per action). <see cref="AddXpAsync"/> composes this
        /// too (DRY — ONE arithmetic implementation for both the standalone XP path and the composed path).
        /// <para>
        /// All checked arithmetic (XP overflow, charge-grant overflow) runs BEFORE any model write, so an
        /// overflow throws cleanly with the model untouched (nothing to roll back). The returned
        /// <see cref="ProgressionStage"/> captures the exact pre-mutation snapshot for <see cref="ProgressionStage.Revert"/>
        /// (restore on a persist fault) and the post-mutation values for <see cref="ProgressionStage.RaiseCommittedEvents"/>
        /// (raise XP/level/charge-changed AFTER the caller's persist commits + lock releases).
        /// </para>
        /// <para>
        /// NOT lock-protected: the CALLER must hold the shared Economy <see cref="SaveMutationLock"/> around the
        /// stage + persist span. Gameplay is single-threaded, so the standalone XP path and the composed writer
        /// cannot actually interleave (the [[codex-service-lock-tier]] rationale).
        /// </para>
        /// </summary>
        public ProgressionStage StageXpGrant(SaveModel model, int amount)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount), amount, "An XP grant must be a positive integer.");
            }

            int priorXp = model.Xp;
            int priorLevel = model.Level;
            var priorCounts = new int[AllChargeTypes.Length];
            for (int i = 0; i < AllChargeTypes.Length; i++)
            {
                priorCounts[i] = ChargeInventory.GetCount(model, AllChargeTypes[i]);
            }

            // Compute the entire post-state into LOCALS first, mutating nothing on the model. All
            // checked-arithmetic throws fire BEFORE any model write (the model stays untouched on overflow —
            // a clean programmer-error throw with nothing to roll back).
            int newXp;
            checked
            {
                newXp = priorXp + amount;
            }

            // Clamp to >= 0: XP only rises, but Level is stored as-earned (not derived on read), so a future
            // threshold rebalance (Story 1.6) can leave stored Level > LevelForXp(Xp). Without the clamp the
            // next add would compute a negative gain, decrease Level (this must NEVER decrease it), and produce
            // negative charge grants. The clamp makes "no level gained ⇒ no grant" the floor.
            int levelsGained = Math.Max(0, _rules.LevelForXp(newXp) - priorLevel);

            var newCounts = new int[AllChargeTypes.Length];
            for (int i = 0; i < AllChargeTypes.Length; i++)
            {
                int grantEach = _rules.GrantPerLevelUp(AllChargeTypes[i]);
                checked
                {
                    newCounts[i] = priorCounts[i] + (grantEach * levelsGained);
                }
            }

            // Derive the committed level from the SAME clamped gain so Level is monotonic.
            int newLevel = priorLevel + levelsGained;

            // All arithmetic succeeded — write the locals onto the model. From here a persist fault / recovery
            // swap must restore the captured snapshot (stage.Revert()).
            model.Xp = newXp;
            for (int i = 0; i < AllChargeTypes.Length; i++)
            {
                ChargeInventory.SetCount(model, AllChargeTypes[i], newCounts[i]);
            }

            model.Level = newLevel;

            return ProgressionStage.Create(this, model, priorXp, priorLevel, priorCounts, newXp, newLevel, newCounts);
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

        /// <summary>
        /// A staged-but-not-persisted XP grant produced by <see cref="StageXpGrant"/>, for the standalone
        /// <see cref="AddXpAsync"/> path AND the Story 4.4 composed Encounter Capture write. The caller holds
        /// this between mutating the model and the single <c>SaveService.SaveAsync</c>: on a persist FAULT it
        /// calls <see cref="Revert"/> (restores the EXACT pre-mutation XP/level/charge snapshot); on a COMMIT it
        /// raises the XP/level/charge-changed events via <see cref="RaiseCommittedEvents"/> (after the persist
        /// commits + the shared lock releases). A nested type so it can reach the private
        /// <see cref="Rollback"/>/event raisers — the progression rules stay in ProgressionService (one home).
        /// </summary>
        public readonly struct ProgressionStage
        {
            private readonly ProgressionService _service;
            private readonly SaveModel _model;
            private readonly int _priorXp;
            private readonly int _priorLevel;
            private readonly int[] _priorCounts;
            private readonly int _newXp;
            private readonly int _newLevel;
            private readonly int[] _newCounts;

            private ProgressionStage(
                ProgressionService service, SaveModel model, int priorXp, int priorLevel, int[] priorCounts,
                int newXp, int newLevel, int[] newCounts)
            {
                _service = service;
                _model = model;
                _priorXp = priorXp;
                _priorLevel = priorLevel;
                _priorCounts = priorCounts;
                _newXp = newXp;
                _newLevel = newLevel;
                _newCounts = newCounts;
            }

            internal static ProgressionStage Create(
                ProgressionService service, SaveModel model, int priorXp, int priorLevel, int[] priorCounts,
                int newXp, int newLevel, int[] newCounts) =>
                new ProgressionStage(service, model, priorXp, priorLevel, priorCounts, newXp, newLevel, newCounts);

            /// <summary>Restore the EXACT pre-mutation XP/level/charge snapshot onto the captured reference on a
            /// persist fault (the same restore <see cref="Rollback"/> performs). The composed write calls this
            /// inside its rollback path, alongside reverting any other slice (e.g. a Capture's charge consume).</summary>
            public void Revert()
            {
                if (_service == null)
                {
                    return; // a default(ProgressionStage) — nothing was staged.
                }

                Rollback(_model, _priorXp, _priorLevel, _priorCounts);
            }

            /// <summary>True if this committed grant CHANGED the count for <paramref name="type"/> (i.e.
            /// <see cref="RaiseCommittedEvents"/> will raise <c>OnChargesChanged</c> for it) — a level-up granted
            /// charges of that type. A composed caller that ALSO mutates the same charge (e.g. a Capture consuming
            /// a StrongCapture charge) queries this to avoid raising a SECOND, duplicate charges-changed event for
            /// the same final value (the XP stage's event is authoritative for the net). False on a default stage.</summary>
            public bool ChargesChangedFor(ChargeType type)
            {
                if (_service == null)
                {
                    return false;
                }

                for (int i = 0; i < AllChargeTypes.Length; i++)
                {
                    if (AllChargeTypes[i] == type)
                    {
                        return _newCounts[i] != _priorCounts[i];
                    }
                }

                return false;
            }

            /// <summary>Raise the XP/level/charge-changed events for a COMMITTED grant — the caller invokes this
            /// AFTER its single persist commits and the shared lock is released. Only raises the events that
            /// actually changed (the standalone <see cref="AddXpAsync"/> behavior, preserved verbatim).</summary>
            public void RaiseCommittedEvents()
            {
                if (_service == null)
                {
                    return;
                }

                _service.RaiseXpChanged(_newXp);
                if (_newLevel != _priorLevel)
                {
                    _service.RaiseLevelChanged(_newLevel);
                }

                for (int i = 0; i < AllChargeTypes.Length; i++)
                {
                    if (_newCounts[i] != _priorCounts[i])
                    {
                        _service.RaiseChargesChanged(AllChargeTypes[i], _newCounts[i]);
                    }
                }
            }
        }
    }
}
