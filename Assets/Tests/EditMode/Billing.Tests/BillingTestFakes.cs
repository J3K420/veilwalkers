using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Veilwalkers.Billing;
using Veilwalkers.Core;
using Veilwalkers.Persistence;

namespace Veilwalkers.Billing.Tests
{
    /// <summary>
    /// A deterministic <see cref="IClock"/> for the Billing tests — the reconciler stamps
    /// <c>PendingPurchaseRecord.IsoTimestampUtc</c> from it (Story 5.2). A fixed, far-from-DST UTC instant so
    /// the timestamp is stable; tests assert ledger STATE, not the timestamp, so the exact value is inert.
    /// </summary>
    internal sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// A scriptable <see cref="IStoreAdapter"/> test double (the <c>FakeArAnchorProvider</c> equivalent):
    /// the next <see cref="PurchaseAsync"/> returns whatever <see cref="NextResult"/> is set to, and
    /// <see cref="PurchaseCalls"/> counts the calls so the "routes through the store exactly once / never
    /// for an unknown pack" assertions (AC-2) are falsifiable. Deterministic — no real Play SDK.
    /// </summary>
    internal sealed class FakeStoreAdapter : IStoreAdapter
    {
        private int _purchaseCalls;
        private int _acknowledgeCalls;

        /// <summary>The result the next <see cref="PurchaseAsync"/> returns. Defaults to a completed
        /// purchase so the happy-path test only sets the order id; failure tests override it.</summary>
        public StorePurchaseResult NextResult = StorePurchaseResult.Succeeded("default", "order-default");

        /// <summary>Localized prices the adapter reports (AC-1). Empty by default.</summary>
        public IReadOnlyDictionary<string, string> LocalizedPrices = new Dictionary<string, string>();

        /// <summary>The last pack id passed to <see cref="PurchaseAsync"/> (for routing assertions).</summary>
        public string LastPackId;

        /// <summary>What <see cref="AcknowledgeAsync"/> returns (Story 5.2). Defaults to a successful
        /// acknowledge; the ack-fails-then-retries pin sets it false then true.</summary>
        public bool NextAcknowledgeResult = true;

        /// <summary>Every order id <see cref="AcknowledgeAsync"/> was called with, in order (Story 5.2) —
        /// pins "acknowledge once per credited record, after the grant".</summary>
        public readonly List<string> AcknowledgedOrderIds = new List<string>();

        public int PurchaseCalls => Volatile.Read(ref _purchaseCalls);

        public int AcknowledgeCalls => Volatile.Read(ref _acknowledgeCalls);

        public Task<StorePurchaseResult> PurchaseAsync(string packId)
        {
            Interlocked.Increment(ref _purchaseCalls);
            LastPackId = packId;
            return Task.FromResult(NextResult);
        }

        public Task<IReadOnlyDictionary<string, string>> FetchLocalizedPricesAsync(IEnumerable<string> packIds)
        {
            return Task.FromResult(LocalizedPrices);
        }

        public Task<bool> AcknowledgeAsync(string playOrderId)
        {
            Interlocked.Increment(ref _acknowledgeCalls);
            AcknowledgedOrderIds.Add(playOrderId);
            return Task.FromResult(NextAcknowledgeResult);
        }
    }

    /// <summary>
    /// A DEEP-cloning in-memory <see cref="IProgressStore"/> for the Billing tests, so the grant runs
    /// through the REAL <c>CreditService</c> pipeline (anti-tautology, [[tautological-test-trap]]). Copied
    /// from <c>FakeEncounterProgressStore</c>: <see cref="SaveAsync"/> stores a SNAPSHOT (so
    /// <see cref="Stored"/> never aliases the live model), <see cref="FailNextSave"/> drives the persist-fault
    /// rollback path (the <c>GrantFailed</c> pin), and <see cref="SaveCalls"/> is the "exactly one persist per
    /// purchase" pin (AC-3 / NFR-4 no-double-grant). The grant touches only <c>Credits</c>, but the whole
    /// model is cloned for honesty.
    /// </summary>
    internal sealed class FakeBillingProgressStore : IProgressStore
    {
        private int _saveCalls;

        public SaveModel Stored;
        public bool FailNextSave;

        public int SaveCalls => Volatile.Read(ref _saveCalls);

        public Task<SaveModel> LoadAsync()
        {
            if (Stored == null)
            {
                throw new FileNotFoundException("FakeBillingProgressStore: no save exists.");
            }

            return Task.FromResult(Clone(Stored));
        }

        public Task SaveAsync(SaveModel model)
        {
            Interlocked.Increment(ref _saveCalls);

            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("FakeBillingProgressStore: simulated save failure.");
            }

            Stored = Clone(model);
            return Task.CompletedTask;
        }

        public bool Exists() => Stored != null;

        public Task DeleteAsync()
        {
            Stored = null;
            return Task.CompletedTask;
        }

        private static SaveModel Clone(SaveModel model)
        {
            var copy = new SaveModel
            {
                SchemaVersion = model.SchemaVersion,
                Credits = model.Credits,
                StartingCreditsGranted = model.StartingCreditsGranted,
                Xp = model.Xp,
                Level = model.Level,
                StrongCaptureCharges = model.StrongCaptureCharges,
                StabilityBoostCharges = model.StabilityBoostCharges,
                NightveilFilterCharges = model.NightveilFilterCharges,
                // Story 5.3: the one-shot Guaranteed-Rare Lure counter must be deep-copied so a grant is
                // observable on the stored copy WITHOUT aliasing the live model ([[tautological-test-trap]]).
                GuaranteedRareLures = model.GuaranteedRareLures,
                DailyClaim = model.DailyClaim,
                FirstZeroCreditDay = model.FirstZeroCreditDay,
                Codex = model.Codex == null
                    ? new Dictionary<string, CodexEntryData>()
                    : new Dictionary<string, CodexEntryData>(model.Codex),
            };

            // Story 5.2: DEEP-copy PendingPurchases (a new list + a new record per element copying all four
            // fields) so Stored.PendingPurchases never aliases the live list — otherwise the reconciler
            // advancing a record to Granted would mutate the "stored" copy too and every exactly-once /
            // ledger-state assertion would pass for the wrong reason ([[tautological-test-trap]]).
            if (model.PendingPurchases != null)
            {
                foreach (PendingPurchaseRecord record in model.PendingPurchases)
                {
                    if (record == null)
                    {
                        continue;
                    }

                    copy.PendingPurchases.Add(new PendingPurchaseRecord
                    {
                        OrderId = record.OrderId,
                        PackId = record.PackId,
                        State = record.State,
                        IsoTimestampUtc = record.IsoTimestampUtc,
                    });
                }
            }

            return copy;
        }
    }
}
