using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Persistence;

namespace Veilwalkers.Billing
{
    /// <summary>
    /// The exactly-once purchase-reconciliation engine (Story 5.2, NFR-4) — the architecture-named
    /// <c>PurchaseReconciler</c> (architecture.md:419-420, :496-497, :522). It turns Story 5.1's "grant once
    /// per successful <c>PurchaseAsync</c>, lost on interruption" into "granted exactly once even across a
    /// network drop / app-kill, never twice." It sits between the store and the credit grant in the
    /// canonical flow (architecture.md:522): <c>BillingService → Unity IAP → PurchaseReconciler
    /// (pending→ack→credit once→persist) → CreditService</c>.
    /// <para>
    /// <b>The sequence (AC-1):</b> write a pending-ledger record (BEFORE the grant, so an app-kill at any
    /// point after the player paid leaves a recoverable row) → grant the Credits exactly once keyed by Play
    /// order id → acknowledge with Play (AC-4 — un-acknowledged purchases auto-refund) → clear the record.
    /// </para>
    /// <para>
    /// <b>Exactly-once (AC-2) — the persisted <see cref="StateGranted"/> is the PRIMARY guard, the order-id
    /// dedup the SECONDARY.</b> A record advances <see cref="StatePendingGrant"/> → <see cref="StateGranted"/>
    /// → cleared. The advance to <see cref="StateGranted"/> is PERSISTED before the acknowledge, so a kill
    /// between grant and ack re-runs only the acknowledge and NEVER re-credits; the order id keys the record
    /// so a duplicate pending write is refused. The launch recovery pass
    /// (<see cref="ReconcilePendingOnLaunchAsync"/>) walks the ledger and grants only the not-yet-credited
    /// records. This is the inverse of Story 1.7's benign-re-grant <c>FirstLaunchGrant</c> and matches Story
    /// 1.8's daily-claim "a crash between two persists must not double-grant."
    /// </para>
    /// <para>
    /// <b>A sibling Economy-tier mutator over the shared <see cref="SaveMutationLock"/>.</b> It mutates
    /// <c>SaveModel.PendingPurchases</c> directly AND <c>SaveModel.Credits</c> (via
    /// <see cref="ICreditService.GrantCreditsAsync"/>), so it serializes against
    /// <c>CreditService</c>/<c>ProgressionService</c>/<c>DailyRewardService</c> on the ONE shared lock — a
    /// per-reconciler lock would let a reconcile write and a credit spend interleave and persist each other's
    /// uncommitted deltas (the <c>DailyRewardService</c> precedent). The lock is held for each LEDGER
    /// mutate→persist span ONLY; it is NEVER held across <see cref="ICreditService.GrantCreditsAsync"/> (which
    /// acquires the same non-reentrant lock itself — holding it would deadlock) or the external
    /// <see cref="IStoreAdapter.AcknowledgeAsync"/> store call.
    /// </para>
    /// <para>
    /// Billing → Economy stays STRICTLY one-way (architecture.md:478-480): the reconciler CALLS
    /// <c>GrantCreditsAsync</c>; Economy never references Billing. NO service locator (AR-4) — every
    /// dependency is constructor-injected.
    /// </para>
    /// </summary>
    public sealed class PurchaseReconciler
    {
        /// <summary>A pending record whose Credits have NOT yet been granted. Written before the grant.</summary>
        public const string StatePendingGrant = "pending-grant";

        /// <summary>The Credits were granted (the order-id dedup state); the record awaits acknowledge.</summary>
        public const string StateGranted = "granted";

        private readonly SaveService _saveService;
        private readonly SaveMutationLock _mutationLock;
        private readonly ICreditService _credits;
        private readonly CreditPackCatalog _catalog;
        private readonly IStoreAdapter _store;
        private readonly IClock _clock;

        public PurchaseReconciler(
            SaveService saveService,
            SaveMutationLock mutationLock,
            ICreditService credits,
            CreditPackCatalog catalog,
            IStoreAdapter store,
            IClock clock)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _mutationLock = mutationLock ?? throw new ArgumentNullException(nameof(mutationLock));
            _credits = credits ?? throw new ArgumentNullException(nameof(credits));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// The outcome of reconciling ONE just-completed purchase through <see cref="ReconcilePurchaseAsync"/>,
        /// so <see cref="BillingService"/> can build the exact 5.1 <see cref="PurchaseResult"/> + event without
        /// inferring it from a balance delta (which a concurrently-recovered prior purchase could confound).
        /// </summary>
        public enum PurchaseReconcileOutcome
        {
            /// <summary>The pack was granted exactly once and the balance reflects it. Raise the event.</summary>
            Granted = 0,

            /// <summary>The pending record could not be durably written, or the grant did not save — nothing
            /// was credited; the launch pass will recover it (the 5.1 <c>GrantFailed</c> seam, now
            /// recoverable). No event.</summary>
            GrantPending = 1,
        }

        /// <summary>
        /// The result of <see cref="ReconcilePurchaseAsync"/>: the outcome + the credited amount + the new
        /// balance (for the <c>PurchaseResult</c>/event <see cref="BillingService"/> builds).
        /// </summary>
        public readonly struct PurchaseReconcileResult
        {
            public PurchaseReconcileOutcome Outcome { get; }
            public int CreditsGranted { get; }
            public int NewBalance { get; }

            public PurchaseReconcileResult(PurchaseReconcileOutcome outcome, int creditsGranted, int newBalance)
            {
                Outcome = outcome;
                CreditsGranted = creditsGranted;
                NewBalance = newBalance;
            }
        }

        /// <summary>
        /// Reconcile a SINGLE just-completed purchase (the in-line path <see cref="BillingService"/> calls on a
        /// <c>Purchased</c> store result): write the pending record (AC-1 step 1), then run the full
        /// reconcile (grant-once → acknowledge → clear). Returns a typed result so the caller builds the exact
        /// 5.1 <see cref="PurchaseResult"/>/event. If the pending write fails, NOTHING is granted and the
        /// outcome is <see cref="PurchaseReconcileOutcome.GrantPending"/> (the launch pass recovers it next
        /// time). After the reconcile, the order id's state is consulted to decide Granted vs GrantPending —
        /// the balance is read AFTER for the event payload.
        /// </summary>
        public async Task<PurchaseReconcileResult> ReconcilePurchaseAsync(string playOrderId, string packId)
        {
            if (!_catalog.TryGet(packId, out CreditPack pack))
            {
                // BillingService only calls this for a catalog pack it already resolved, so an unknown id here
                // is a programmer error upstream — but never crash the purchase flow (NFR-3): treat it as
                // pending (uncredited) and log loudly.
                GameLog.Error(
                    $"PurchaseReconciler.ReconcilePurchaseAsync: unknown pack '{packId}' (order {playOrderId}); nothing granted.");
                return new PurchaseReconcileResult(PurchaseReconcileOutcome.GrantPending, 0, _credits.Balance);
            }

            bool recorded = await RecordPendingAsync(playOrderId, packId).ConfigureAwait(false);
            if (!recorded)
            {
                // The pending ledger did not durably save — do NOT grant (the grant must never outrun a
                // recoverable record). The next launch pass has nothing to recover (no record), so the player
                // re-attempts; this is the same "paid-but-not-recorded" exposure 5.1 surfaced as GrantFailed.
                return new PurchaseReconcileResult(PurchaseReconcileOutcome.GrantPending, 0, _credits.Balance);
            }

            // ReconcileOneAsync reports AUTHORITATIVELY whether THIS call newly credited the pack — never
            // inferred from a re-read state or a balance delta (which a concurrently-recovered prior purchase,
            // or a grant-then-state-advance-fault, could confound — the CR self-contradictory-result finding).
            RecordOutcome outcome = await ReconcileOneAsync(playOrderId).ConfigureAwait(false);
            int newBalance = _credits.Balance;

            // NewlyGranted → the grant committed in THIS call (Credits landed; ack may still be pending, which
            // the launch pass retries). GrantPending → nothing was credited in this call (grant did not save,
            // or the Granted-state advance faulted so the credits are NOT yet safely owned by a Granted record);
            // the launch recovery pass settles it. AlreadyGranted → the record was granted on a PRIOR pass
            // (a double-tap / relaunch racing the in-line path): this call credited NOTHING, so report
            // GrantPending with 0 (the event must not claim a grant this call did not make).
            if (outcome == RecordOutcome.NewlyGranted)
            {
                return new PurchaseReconcileResult(PurchaseReconcileOutcome.Granted, pack.TotalCredits, newBalance);
            }

            return new PurchaseReconcileResult(PurchaseReconcileOutcome.GrantPending, 0, newBalance);
        }

        /// <summary>
        /// AC-1 step 1 — write a <see cref="StatePendingGrant"/> record for a just-completed purchase, BEFORE
        /// the grant, so the purchase survives an app-kill. Idempotent on order id: a record with this order
        /// id already in the ledger is NOT duplicated (a double-tap / retry must not create two pending rows
        /// for one purchase). Persists once (lock-held, rollback on fault — the AR-8 atomic-write shape).
        /// Returns true when the record is present (written now or already there), false on a persist fault.
        /// </summary>
        public async Task<bool> RecordPendingAsync(string playOrderId, string packId)
        {
            if (string.IsNullOrEmpty(playOrderId))
            {
                // A completed purchase ALWAYS carries an order id (StorePurchaseResult.Succeeded enforces it);
                // an empty one here is a programmer error upstream, surfaced loudly rather than persisting an
                // un-dedupable record.
                throw new ArgumentException(
                    "A pending purchase requires a Play order id (exactly-once dedup keys on it).",
                    nameof(playOrderId));
            }

            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();

                if (FindByOrderId(model, playOrderId) != null)
                {
                    // Already recorded (a retried purchase / a relaunch racing a fresh attempt): do not add a
                    // second row. The existing record drives reconciliation.
                    return true;
                }

                var record = new PendingPurchaseRecord
                {
                    OrderId = playOrderId,
                    PackId = packId,
                    State = StatePendingGrant,
                    IsoTimestampUtc = _clock.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                };
                model.PendingPurchases.Add(record);

                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (ReferenceEquals(_saveService.Current, model))
                    {
                        return true;
                    }

                    // A recovery swap replaced the model mid-persist: what was written is not this ledger.
                    model.PendingPurchases.Remove(record);
                    GameLog.Warn(
                        "PurchaseReconciler: pending-record write rolled back — the save model was swapped " +
                        "mid-operation (recovery raced a mutation); reconcile will retry.");
                    return false;
                }
                catch (Exception ex)
                {
                    model.PendingPurchases.Remove(record);
                    GameLog.Error(
                        $"PurchaseReconciler: failed to record pending purchase (order {playOrderId}) — {ex.Message}. " +
                        "The grant will not proceed until the pending record is durable.");
                    return false;
                }
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        /// <summary>
        /// AC-1 steps 2-5 + AC-2 + AC-4 — walk the pending ledger and, for each record, grant the Credits
        /// exactly once (keyed by order id), acknowledge with Play, then clear it. A <see cref="StatePendingGrant"/>
        /// record is granted then advanced to <see cref="StateGranted"/> (persisted, so a kill before the
        /// acknowledge never re-grants); a <see cref="StateGranted"/> record grants NOTHING and only
        /// acknowledges + clears (the exactly-once dedup). A grant persist-fault LEAVES the record at
        /// <see cref="StatePendingGrant"/> for the next pass; a failed acknowledge LEAVES the record at
        /// <see cref="StateGranted"/> (Credits already safe) for the next pass. A record whose pack id is not
        /// in the catalog is corrupt/stale: logged + cleared (terminal), never granted, never looped on.
        /// </summary>
        public async Task ReconcileAsync()
        {
            // Snapshot the order ids to process so the walk is stable even as records are cleared. Reading the
            // list is a cheap lock-held copy; the per-record work re-resolves the record by order id under its
            // own lock span, so a concurrent mutation never corrupts the iteration.
            string[] orderIds = await SnapshotOrderIdsAsync().ConfigureAwait(false);

            foreach (string orderId in orderIds)
            {
                await ReconcileOneAsync(orderId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// AC-2 — the launch recovery pass. Called after <c>SaveService.InitializeAsync</c> completes (the
        /// model must be loaded — <see cref="RequireModel"/> throws otherwise, the CreditService before-load
        /// contract). Any <see cref="StatePendingGrant"/> record left by an interrupted prior run is granted
        /// exactly once; any <see cref="StateGranted"/>-but-unacknowledged record is acknowledged (AC-4 on the
        /// next launch). A no-op when the ledger is empty (the common launch). Simply delegates to
        /// <see cref="ReconcileAsync"/>.
        /// </summary>
        public Task ReconcilePendingOnLaunchAsync() => ReconcileAsync();

        /// <summary>What a single <see cref="ReconcileOneAsync"/> call did to the Credits — reported
        /// AUTHORITATIVELY (never inferred from a re-read state or a balance delta), so
        /// <see cref="ReconcilePurchaseAsync"/> builds a self-consistent result + event.</summary>
        private enum RecordOutcome
        {
            /// <summary>Nothing credited in this call (the record is gone/missing, the grant did not save, or
            /// the Granted-state advance faulted). The launch recovery pass settles it.</summary>
            GrantPending = 0,

            /// <summary>The grant committed in THIS call (Credits landed; the ack may still be pending).</summary>
            NewlyGranted = 1,

            /// <summary>The record was already Granted on a PRIOR pass; this call credited NOTHING (it only
            /// acknowledged/cleared). A double-tap / relaunch racing the in-line path.</summary>
            AlreadyGranted = 2,
        }

        private async Task<RecordOutcome> ReconcileOneAsync(string orderId)
        {
            // (1) GRANT (if not yet granted). The grant runs through CreditService.GrantCreditsAsync, which
            // acquires the shared lock ITSELF — so this MUST NOT hold the lock here (non-reentrant; would
            // deadlock). We read the record's state under a short lock, then grant outside it.
            ReadStateResult read = await ReadStateAsync(orderId).ConfigureAwait(false);
            if (read.State == PendingState.Missing)
            {
                return RecordOutcome.GrantPending;
            }

            if (read.State == PendingState.Invalid)
            {
                // A persisted state string that is neither pending-grant nor granted (a corrupt/forward-version
                // record). It cannot be safely granted or acknowledged — clear it (terminal) + log loudly,
                // mirroring the unknown-pack corrupt-record guard, so the ledger never loops on it.
                GameLog.Error(
                    $"PurchaseReconciler: pending purchase (order {orderId}) has an unrecognized state — " +
                    "clearing the corrupt record without granting.");
                await ClearRecordAsync(orderId).ConfigureAwait(false);
                return RecordOutcome.GrantPending;
            }

            bool newlyGranted = false;
            if (read.State == PendingState.PendingGrant)
            {
                if (!_catalog.TryGet(read.PackId, out CreditPack pack))
                {
                    // Corrupt/stale record: a pack id no longer in the catalog can never be granted. Clear it
                    // (terminal) so the ledger does not loop on it forever, and log loudly.
                    GameLog.Error(
                        $"PurchaseReconciler: pending purchase (order {orderId}) references unknown pack '{read.PackId}' — " +
                        "clearing the stale record without granting.");
                    await ClearRecordAsync(orderId).ConfigureAwait(false);
                    return RecordOutcome.GrantPending;
                }

                if (pack.TotalCredits <= 0)
                {
                    // A factory-guaranteed-positive CreditPack should make this unreachable, but a defense-in-
                    // depth guard keeps NFR-3 (never throw for an expected outcome): a non-positive total would
                    // throw out of GrantCreditsAsync. Treat it as a corrupt record — clear + log, never throw.
                    GameLog.Error(
                        $"PurchaseReconciler: pending purchase (order {orderId}, pack '{read.PackId}') has a non-positive " +
                        $"total ({pack.TotalCredits}) — clearing the corrupt record without granting.");
                    await ClearRecordAsync(orderId).ConfigureAwait(false);
                    return RecordOutcome.GrantPending;
                }

                Result grant = await _credits.GrantCreditsAsync(pack.TotalCredits).ConfigureAwait(false);
                if (!grant.Success)
                {
                    // The grant did not durably save (CreditService rolled it back). Leave the record at
                    // PendingGrant; the next pass retries. Nothing was credited — balance net-unchanged.
                    GameLog.Warn(
                        $"PurchaseReconciler: grant for pending purchase (order {orderId}, pack '{read.PackId}') did not " +
                        $"save ({grant.Message}); leaving it pending for the next reconcile pass.");
                    return RecordOutcome.GrantPending;
                }

                // Advance to Granted and PERSIST before acknowledging — the persisted state is what prevents a
                // re-grant after an app-kill between the grant and the acknowledge. Story 5.3: a Veil pack also
                // grants ONE one-shot Guaranteed-Rare Lure, folded into THIS same write so the persisted Granted
                // state is the exactly-once envelope for the lure too (a Granted record has, by definition,
                // already granted both the Credits AND the lure — the order-id dedup covers it for free).
                if (!await AdvanceToGrantedAsync(orderId, pack.IncludesGuaranteedRareLure).ConfigureAwait(false))
                {
                    // The state-advance persist faulted. The Credits ARE granted (the grant above committed),
                    // but the record is still PendingGrant on disk. We MUST NOT re-grant on the next pass, yet
                    // the order-id record looks ungranted. Log loudly: the order-id dedup (the record exists)
                    // plus this loud signal are the recovery seam; do not acknowledge/clear with an
                    // inconsistent ledger. The narrow grant↔state-advance two-write window is the documented
                    // client-trusted MVP exposure (deferred-work.md → Phase-2 verifier). Report GrantPending —
                    // this call did NOT leave a clean Granted record, so the caller must NOT claim a grant.
                    GameLog.Error(
                        $"PurchaseReconciler: granted pending purchase (order {orderId}) but could not persist the " +
                        "Granted state — the Credits are committed; the next pass must NOT re-grant. Manual/verifier " +
                        "reconciliation may be required (client-trusted MVP).");
                    return RecordOutcome.GrantPending;
                }

                newlyGranted = true;
            }

            // (2) ACKNOWLEDGE (AC-4) — outside the lock (external store call). Idempotent on Play's side.
            bool acknowledged = await _store.AcknowledgeAsync(orderId).ConfigureAwait(false);
            if (!acknowledged)
            {
                // Transient acknowledge failure: the Credits are safely granted; only the ack is outstanding.
                // Leave the record at Granted so the next pass retries the acknowledge (NO re-grant).
                GameLog.Warn(
                    $"PurchaseReconciler: acknowledge for purchase (order {orderId}) failed; the Credits are granted, " +
                    "the next reconcile pass will retry the acknowledge.");
                return newlyGranted ? RecordOutcome.NewlyGranted : RecordOutcome.AlreadyGranted;
            }

            // (3) CLEAR — the purchase is granted AND acknowledged; remove it from the ledger.
            await ClearRecordAsync(orderId).ConfigureAwait(false);
            GameLog.Info($"PurchaseReconciler: purchase (order {orderId}) reconciled exactly once and cleared.");
            return newlyGranted ? RecordOutcome.NewlyGranted : RecordOutcome.AlreadyGranted;
        }

        private enum PendingState
        {
            Missing,
            PendingGrant,
            Granted,

            /// <summary>The record exists but its persisted state is neither pending-grant nor granted (corrupt
            /// / forward-version) — treated as a corrupt record (cleared, never granted).</summary>
            Invalid,
        }

        private readonly struct ReadStateResult
        {
            public PendingState State { get; }
            public string PackId { get; }

            public ReadStateResult(PendingState state, string packId)
            {
                State = state;
                PackId = packId;
            }
        }

        private async Task<ReadStateResult> ReadStateAsync(string orderId)
        {
            // The model can be swapped by recovery, so take the lock for a consistent read with the writers.
            // Use the async wait (the DailyRewardService/CreditService discipline) — never a synchronous
            // .GetAwaiter().GetResult() block on the non-reentrant SaveMutationLock (a sync-over-async deadlock
            // risk when a continuation thread holds the lock).
            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                PendingPurchaseRecord record = FindByOrderId(model, orderId);
                if (record == null)
                {
                    return new ReadStateResult(PendingState.Missing, null);
                }

                PendingState state;
                if (record.State == StateGranted)
                {
                    state = PendingState.Granted;
                }
                else if (record.State == StatePendingGrant)
                {
                    state = PendingState.PendingGrant;
                }
                else
                {
                    // An unrecognized persisted state (corrupt / forward-version) — never silently treat it as
                    // PendingGrant (which could re-credit). The caller clears it as a corrupt record.
                    state = PendingState.Invalid;
                }

                return new ReadStateResult(state, record.PackId);
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        /// <summary>
        /// Advance the record to <see cref="StateGranted"/> and persist (the exactly-once envelope). Story 5.3:
        /// when <paramref name="grantGuaranteedRareLure"/> is true (a Veil pack), ALSO increment
        /// <c>SaveModel.GuaranteedRareLures</c> in this SAME write — so the persisted <c>Granted</c> state guards
        /// the one-shot lure exactly as it guards the Credits (a kill after this commit never re-grants either;
        /// a kill BEFORE it re-runs the whole grant, granting the lure exactly once per <c>Granted</c>
        /// transition). Both deltas roll back together on a swap/persist fault.
        /// </summary>
        private async Task<bool> AdvanceToGrantedAsync(string orderId, bool grantGuaranteedRareLure)
        {
            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                PendingPurchaseRecord record = FindByOrderId(model, orderId);
                if (record == null)
                {
                    return false;
                }

                string priorState = record.State;
                int priorLures = model.GuaranteedRareLures;
                record.State = StateGranted;
                if (grantGuaranteedRareLure)
                {
                    model.GuaranteedRareLures = priorLures + 1;
                }

                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);
                    if (ReferenceEquals(_saveService.Current, model))
                    {
                        if (grantGuaranteedRareLure)
                        {
                            GameLog.Info(
                                $"PurchaseReconciler: granted 1 Guaranteed-Rare Lure for order {orderId} " +
                                $"(now {model.GuaranteedRareLures}).");
                        }

                        return true;
                    }

                    // Roll BOTH deltas back together onto the captured ref (the swap-branch contract).
                    record.State = priorState;
                    model.GuaranteedRareLures = priorLures;
                    GameLog.Warn(
                        "PurchaseReconciler: Granted-state advance rolled back — the save model was swapped " +
                        "mid-operation (recovery raced a mutation).");
                    return false;
                }
                catch (Exception ex)
                {
                    record.State = priorState;
                    model.GuaranteedRareLures = priorLures;
                    GameLog.Error(
                        $"PurchaseReconciler: failed to persist the Granted state for order {orderId} — {ex.Message}.");
                    return false;
                }
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        private async Task ClearRecordAsync(string orderId)
        {
            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                PendingPurchaseRecord record = FindByOrderId(model, orderId);
                if (record == null)
                {
                    return;
                }

                model.PendingPurchases.Remove(record);
                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);
                    if (!ReferenceEquals(_saveService.Current, model))
                    {
                        // Recovery swapped the model: the record we removed was on a detached instance. Restore
                        // it so the (now-detached) memory matches disk; the next pass re-clears against Current.
                        model.PendingPurchases.Add(record);
                        GameLog.Warn(
                            "PurchaseReconciler: clear-pending rolled back — the save model was swapped " +
                            "mid-operation; the next pass will re-clear.");
                    }
                }
                catch (Exception ex)
                {
                    model.PendingPurchases.Add(record);
                    GameLog.Warn(
                        $"PurchaseReconciler: failed to clear reconciled purchase (order {orderId}) — {ex.Message}; " +
                        "the next pass will retry. The Credits are granted and acknowledged, so this is benign.");
                }
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        private async Task<string[]> SnapshotOrderIdsAsync()
        {
            // Async wait (not a synchronous .GetAwaiter().GetResult() block on the non-reentrant lock — the
            // sync-over-async deadlock risk). SaveModel.CoerceNullCollections null-scrubs PendingPurchases, but
            // skip any null/empty-order-id element defensively rather than indexing .OrderId blind.
            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel model = RequireModel();
                var ids = new List<string>(model.PendingPurchases.Count);
                foreach (PendingPurchaseRecord record in model.PendingPurchases)
                {
                    if (record != null && !string.IsNullOrEmpty(record.OrderId))
                    {
                        ids.Add(record.OrderId);
                    }
                }

                return ids.ToArray();
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        private static PendingPurchaseRecord FindByOrderId(SaveModel model, string orderId)
        {
            // A null/empty order id is never a valid key (RecordPendingAsync rejects empty ids; the dedup keys
            // on a real Play order id) — refuse to match so a corrupt empty-id record is never resolved by it.
            if (string.IsNullOrEmpty(orderId))
            {
                return null;
            }

            foreach (PendingPurchaseRecord record in model.PendingPurchases)
            {
                if (record != null && string.Equals(record.OrderId, orderId, StringComparison.Ordinal))
                {
                    return record;
                }
            }

            return null;
        }

        /// <summary>
        /// The loaded model, or an <see cref="InvalidOperationException"/> when it is not loaded yet (the
        /// launch recovery pass MUST run after <c>SaveService.InitializeAsync</c>) — the CreditService /
        /// DailyRewardService before-load contract.
        /// </summary>
        private SaveModel RequireModel()
        {
            SaveModel model = _saveService.Current;
            if (model == null)
            {
                throw new InvalidOperationException(
                    "PurchaseReconciler has no loaded save model — await SaveService.InitializeAsync " +
                    "(or recover the corrupt save) before reconciling purchases.");
            }

            return model;
        }
    }
}
