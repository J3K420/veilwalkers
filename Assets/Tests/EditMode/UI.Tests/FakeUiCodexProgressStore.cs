using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Veilwalkers.Persistence;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// A minimal in-memory <see cref="IProgressStore"/> for the Codex grid presenter tests.
    /// <para>
    /// <b>Why a UI.Tests-local fake?</b> <c>FakeCodexProgressStore</c> is <c>internal sealed</c>
    /// to <c>Veilwalkers.Monsters.Tests</c>, so this assembly cannot see it. The presenter tests
    /// drive discoveries through a REAL <see cref="CodexService"/> + <see cref="SaveService"/>,
    /// so they need an <see cref="IProgressStore"/> only to back the <c>SaveService</c> ctor.
    /// </para>
    /// <para>
    /// <b>The point is a real deep copy</b> ([[tautological-test-trap]]). Like the production
    /// store (serialize-on-save, fresh-object-on-load) and like <c>FakeCodexProgressStore</c>,
    /// <see cref="SaveAsync"/> stores a SNAPSHOT and <see cref="LoadAsync"/> returns a fresh
    /// copy, so <c>Stored</c> never aliases the live model and a "did it persist / survive a
    /// reload" assertion is honest, not an aliasing artifact. <see cref="Clone"/> also COERCES a
    /// null entry value to a fresh empty entry, exactly as production's <c>LocalProgressStore</c>
    /// does via <c>SaveModel.CoerceNullCollections()</c>.
    /// </para>
    /// <para>
    /// Deliberately omits <c>FailNextSave</c>/<c>SaveGate</c>/<c>SaveCalls</c>: those exercise
    /// rollback/concurrency, which is CodexService's own Monsters.Tests concern — the presenter
    /// only needs store-and-reload.
    /// </para>
    /// </summary>
    internal sealed class FakeUiCodexProgressStore : IProgressStore
    {
        /// <summary>The currently "persisted" model, or null when none exists.</summary>
        public SaveModel Stored;

        public Task<SaveModel> LoadAsync()
        {
            if (Stored == null)
            {
                throw new FileNotFoundException("FakeUiCodexProgressStore: no save exists.");
            }

            return Task.FromResult(Clone(Stored));
        }

        public Task SaveAsync(SaveModel model)
        {
            Stored = Clone(model);
            return Task.CompletedTask;
        }

        public bool Exists() => Stored != null;

        public Task DeleteAsync()
        {
            Stored = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Deep copy: scalars value-copied AND <c>Codex</c> fully cloned (new dict, fresh entry
        /// per key, fresh variant-flags list). Never shares a reference with the live model.
        /// Mirrors <c>FakeCodexProgressStore.Clone</c> (and production's null-entry coercion).
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
