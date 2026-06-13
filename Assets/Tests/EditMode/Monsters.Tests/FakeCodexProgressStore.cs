using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Veilwalkers.Persistence;

namespace Veilwalkers.Monsters.Tests
{
    /// <summary>
    /// An in-memory <see cref="IProgressStore"/> fake for the Codex tests. Mirrors the
    /// Economy.Tests <c>FakeProgressStore</c> API (<see cref="Stored"/>,
    /// <see cref="FailNextSave"/>, <see cref="SaveGate"/>, <see cref="SaveCalls"/>) but is
    /// authored fresh here for two reasons the 2.3 notes call out: the Economy fake is
    /// <c>internal</c> to <c>Economy.Tests</c> (invisible here), AND its <c>Clone()</c> copies
    /// scalars only and DROPS <c>Codex</c> — reused unmodified, every persistence/reload/
    /// rollback assertion would be tautologically green.
    /// <para>
    /// <b>The point of this fake is a real deep copy.</b> Like the production store (which
    /// serializes on save and deserializes a fresh object on load), <see cref="SaveAsync"/>
    /// stores a SNAPSHOT and <see cref="LoadAsync"/> returns a fresh copy, so
    /// <see cref="Stored"/> never aliases the live model. <see cref="Clone"/> DEEP-copies
    /// <c>Codex</c>: a new dictionary, a fresh <see cref="CodexEntryData"/> per entry, and a
    /// fresh <c>VariantFlags</c> list — so "was it persisted?" / "did it survive a reload?" /
    /// "was it rolled back?" assertions are honest, not aliasing artifacts. <see cref="Clone"/>
    /// also COERCES null entry values to fresh empty entries, exactly as production's
    /// <c>LocalProgressStore</c> does via <c>SaveModel.CoerceNullCollections()</c> — so the fake
    /// can never persist a null-entry state the real store cannot.
    /// </para>
    /// </summary>
    internal sealed class FakeCodexProgressStore : IProgressStore
    {
        private int _saveCalls;

        /// <summary>The currently "persisted" model, or null when none exists.</summary>
        public SaveModel Stored;

        /// <summary>When true, the next <see cref="SaveAsync"/> throws (then resets).</summary>
        public bool FailNextSave;

        /// <summary>When non-null, every <see cref="SaveAsync"/> awaits this gate before
        /// completing, so a test can hold a save open mid-flight.</summary>
        public TaskCompletionSource<bool> SaveGate;

        /// <summary>How many times <see cref="SaveAsync"/> has been entered.</summary>
        public int SaveCalls => Volatile.Read(ref _saveCalls);

        public Task<SaveModel> LoadAsync()
        {
            if (Stored == null)
            {
                throw new FileNotFoundException("FakeCodexProgressStore: no save exists.");
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
                throw new IOException("FakeCodexProgressStore: simulated save failure.");
            }

            Stored = Clone(model);
        }

        public bool Exists() => Stored != null;

        public Task DeleteAsync()
        {
            Stored = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Deep copy: scalars value-copied AND <c>Codex</c> fully cloned (new dict, fresh
        /// entry per key, fresh variant-flags list). Never shares a reference with the live
        /// model — that is what keeps the codex persistence/reload/rollback tests honest.
        /// </summary>
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
                Codex = new Dictionary<string, CodexEntryData>(),
            };

            if (model.Codex != null)
            {
                foreach (KeyValuePair<string, CodexEntryData> pair in model.Codex)
                {
                    CodexEntryData src = pair.Value;
                    // Mirror production: LocalProgressStore runs SaveModel.CoerceNullCollections()
                    // on save/load, which replaces a null entry value with a fresh empty entry. So
                    // a persisted-then-reloaded model NEVER carries a null entry value. The fake
                    // must reproduce that, or tests could persist impossible (null-entry) states
                    // the real store cannot.
                    copy.Codex[pair.Key] = new CodexEntryData
                    {
                        Scanned = src?.Scanned ?? false,
                        Captured = src?.Captured ?? false,
                        Slain = src?.Slain ?? false,
                        VariantFlags = src?.VariantFlags == null
                            ? new List<string>()
                            : new List<string>(src.VariantFlags),
                    };
                }
            }

            return copy;
        }
    }
}
