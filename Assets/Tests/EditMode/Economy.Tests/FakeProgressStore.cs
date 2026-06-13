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

            return Task.FromResult(Stored);
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

            Stored = model;
        }

        public bool Exists() => Stored != null;

        public Task DeleteAsync()
        {
            Stored = null;
            return Task.CompletedTask;
        }
    }
}
