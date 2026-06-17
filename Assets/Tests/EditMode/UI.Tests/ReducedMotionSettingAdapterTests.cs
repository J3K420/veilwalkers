using System.Threading.Tasks;
using NUnit.Framework;
using Veilwalkers.Persistence;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// Story 6.6 (AC-1, Task 2) — the concrete <see cref="SaveModelReducedMotionSetting"/> adapter's
    /// write-path: <c>SetReducedMotion(true)</c> sets the live <see cref="SaveModel.ReducedMotion"/> flag
    /// AND persists it through the <see cref="IProgressStore"/> save pipeline (one atomic write, AR-8),
    /// then reads back true. Pins the mutation the CR flagged — an adapter whose <c>SetReducedMotion</c>
    /// ignores its argument (or never calls <c>SaveAsync</c>) goes red here. Uses a fake store (no
    /// filesystem) so the test proves the SAVE was invoked with the mutated model.
    /// </summary>
    public sealed class ReducedMotionSettingAdapterTests
    {
        // A minimal IProgressStore fake that captures the last saved model + counts SaveAsync calls.
        private sealed class FakeStore : IProgressStore
        {
            public SaveModel LastSaved;
            public int SaveCount;
            private readonly SaveModel _toLoad;

            public FakeStore(SaveModel toLoad) { _toLoad = toLoad; }

            public Task<SaveModel> LoadAsync() => Task.FromResult(_toLoad);

            public Task SaveAsync(SaveModel model)
            {
                LastSaved = model;
                SaveCount++;
                return Task.CompletedTask;
            }

            public bool Exists() => _toLoad != null;

            public Task DeleteAsync() => Task.CompletedTask;
        }

        [Test]
        public void Set_reduced_motion_true_mutates_the_model_and_persists_then_reads_back_true()
        {
            var model = new SaveModel { ReducedMotion = false };
            var store = new FakeStore(model);
            var setting = new SaveModelReducedMotionSetting(model, store);

            Assert.That(setting.ReducedMotionEnabled, Is.False, "precondition: motion-on");

            setting.SetReducedMotion(true);

            Assert.That(model.ReducedMotion, Is.True, "the live model flag is set");
            Assert.That(setting.ReducedMotionEnabled, Is.True, "reads back true");
            Assert.That(store.SaveCount, Is.EqualTo(1), "SetReducedMotion must persist (call SaveAsync once)");
            Assert.That(store.LastSaved, Is.SameAs(model), "the SAME atomic SaveModel is written (AR-8)");
            Assert.That(store.LastSaved.ReducedMotion, Is.True, "the persisted model carries the new flag");
        }

        [Test]
        public void Set_reduced_motion_false_persists_the_disabled_flag()
        {
            var model = new SaveModel { ReducedMotion = true };
            var store = new FakeStore(model);
            var setting = new SaveModelReducedMotionSetting(model, store);

            setting.SetReducedMotion(false);

            Assert.That(setting.ReducedMotionEnabled, Is.False);
            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.That(store.LastSaved.ReducedMotion, Is.False);
        }

        [Test]
        public void Reduced_motion_enabled_reads_the_live_model_value()
        {
            var model = new SaveModel { ReducedMotion = true };
            var setting = new SaveModelReducedMotionSetting(model, new FakeStore(model));
            Assert.That(setting.ReducedMotionEnabled, Is.True, "reflects the persisted opt-in");
        }

        [Test]
        public void Adapter_rejects_null_model_or_store()
        {
            var model = new SaveModel();
            Assert.Throws<System.ArgumentNullException>(() => new SaveModelReducedMotionSetting(null, new FakeStore(model)));
            Assert.Throws<System.ArgumentNullException>(() => new SaveModelReducedMotionSetting(model, null));
        }
    }
}
