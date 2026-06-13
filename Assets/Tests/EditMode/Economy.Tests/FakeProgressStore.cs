using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Veilwalkers.Persistence;

namespace Veilwalkers.Economy.Tests
{
    /// <summary>
    /// The in-memory <see cref="IProgressStore"/> fake the 1.3 notes promised the
    /// interface would make trivial: no temp directories, no crypto, no
    /// <c>LocalProgressStore</c>. Supports the credit-pipeline tests with a
    /// <see cref="FailNextSave"/> flag (rollback paths), a save-call counter, and an
    /// optional <see cref="SaveGate"/> a test can hold open to pin the
    /// serialization guarantee.
    /// <para>
    /// Like the real store (which serializes on save and deserializes a fresh
    /// object on load), <see cref="SaveAsync"/> stores a SNAPSHOT and
    /// <see cref="LoadAsync"/> returns a fresh copy — <see cref="Stored"/> never
    /// aliases the live model, so "was it persisted?" assertions are real, not
    /// tautological. The clone copies the scalar fields the economy tests use;
    /// collections reset to defaults (1.4-review-sanctioned scalar fidelity).
    /// </para>
    /// </summary>
    internal sealed class FakeProgressStore : IProgressStore
    {
        private int _saveCalls;

        /// <summary>The currently "persisted" model, or null when none exists.</summary>
        public SaveModel Stored;

        /// <summary>When true, the next <see cref="SaveAsync"/> throws (then resets).</summary>
        public bool FailNextSave;

        /// <summary>
        /// When non-null, every <see cref="SaveAsync"/> awaits this gate before
        /// completing, so a test can hold a save open mid-flight.
        /// </summary>
        public TaskCompletionSource<bool> SaveGate;

        /// <summary>How many times <see cref="SaveAsync"/> has been entered.</summary>
        public int SaveCalls => Volatile.Read(ref _saveCalls);

        public Task<SaveModel> LoadAsync()
        {
            if (Stored == null)
            {
                throw new FileNotFoundException("FakeProgressStore: no save exists.");
            }

            return Task.FromResult(Clone(Stored));
        }

        public async Task SaveAsync(SaveModel model)
        {
            Interlocked.Increment(ref _saveCalls);

            TaskCompletionSource<bool> gate = SaveGate;
            if (gate != null)
            {
                await gate.Task.ConfigureAwait(false);
            }

            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("FakeProgressStore: simulated save failure.");
            }

            Stored = Clone(model);
        }

        public bool Exists() => Stored != null;

        /// <summary>
        /// Scalar-fidelity copy: every field the economy tests assert on; the
        /// collections/objects reset to fresh defaults (no credit test touches
        /// them, and a shallow share would re-introduce aliasing).
        /// </summary>
        private static SaveModel Clone(SaveModel model)
        {
            return new SaveModel
            {
                SchemaVersion = model.SchemaVersion,
                Credits = model.Credits,
                Xp = model.Xp,
                Level = model.Level,
                StrongCaptureCharges = model.StrongCaptureCharges,
                StabilityBoostCharges = model.StabilityBoostCharges,
                NightveilFilterCharges = model.NightveilFilterCharges,
                DailyClaim = model.DailyClaim,
                FirstZeroCreditDay = model.FirstZeroCreditDay,
            };
        }

        public Task DeleteAsync()
        {
            Stored = null;
            return Task.CompletedTask;
        }
    }
}
