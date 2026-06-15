using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Veilwalkers.Billing;
using Veilwalkers.Persistence;

namespace Veilwalkers.Billing.Tests
{
    /// <summary>
    /// A scriptable <see cref="IStoreAdapter"/> test double (the <c>FakeArAnchorProvider</c> equivalent):
    /// the next <see cref="PurchaseAsync"/> returns whatever <see cref="NextResult"/> is set to, and
    /// <see cref="PurchaseCalls"/> counts the calls so the "routes through the store exactly once / never
    /// for an unknown pack" assertions (AC-2) are falsifiable. Deterministic — no real Play SDK.
    /// </summary>
    internal sealed class FakeStoreAdapter : IStoreAdapter
    {
        private int _purchaseCalls;

        /// <summary>The result the next <see cref="PurchaseAsync"/> returns. Defaults to a completed
        /// purchase so the happy-path test only sets the order id; failure tests override it.</summary>
        public StorePurchaseResult NextResult = StorePurchaseResult.Succeeded("default", "order-default");

        /// <summary>Localized prices the adapter reports (AC-1). Empty by default.</summary>
        public IReadOnlyDictionary<string, string> LocalizedPrices = new Dictionary<string, string>();

        /// <summary>The last pack id passed to <see cref="PurchaseAsync"/> (for routing assertions).</summary>
        public string LastPackId;

        public int PurchaseCalls => Volatile.Read(ref _purchaseCalls);

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
                DailyClaim = model.DailyClaim,
                FirstZeroCreditDay = model.FirstZeroCreditDay,
                Codex = model.Codex == null
                    ? new Dictionary<string, CodexEntryData>()
                    : new Dictionary<string, CodexEntryData>(model.Codex),
            };

            return copy;
        }
    }
}
