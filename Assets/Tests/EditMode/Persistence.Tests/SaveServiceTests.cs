using System;
using System.Collections.Generic;
using NUnit.Framework;
using Veilwalkers.Core;

namespace Veilwalkers.Persistence.Tests
{
    /// <summary>
    /// <see cref="SaveService"/> behavior: load-or-create defaults (credits 0 — the
    /// grant is Story 1.7), the AR-19 status/event surface, failure paths up front
    /// (a Story 1.2 review lesson), and the locator wiring happy path — constructed
    /// directly and registered like Bootstrap does, NOT via Veilwalkers.App (which
    /// EditMode tests must not reference).
    /// </summary>
    public sealed class SaveServiceTests
    {
        private string _dir;
        private LocalProgressStore _store;
        private SaveService _service;

        [SetUp]
        public void SetUp()
        {
            _dir = TestSaveFiles.CreateTempDir();
            _store = new LocalProgressStore(_dir);
            _service = new SaveService(_store);
            GameServices.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.ResetForTests();
            TestSaveFiles.DeleteTempDir(_dir);
        }

        [Test]
        public void Initialize_with_no_save_creates_and_persists_defaults()
        {
            Assert.IsFalse(_store.Exists());

            _service.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(SaveStatus.Idle, _service.Status);
            Assert.IsNotNull(_service.Current);
            Assert.AreEqual(0, _service.Current.Credits, "Fresh save has 0 credits (grant is Story 1.7).");
            Assert.AreEqual(-1L, _service.Current.FirstZeroCreditDay);
            Assert.IsTrue(_store.Exists(), "The fresh default save must be persisted immediately.");
        }

        [Test]
        public void Initialize_with_existing_save_loads_it()
        {
            _store.SaveAsync(new SaveModel { Credits = 33 }).GetAwaiter().GetResult();

            _service.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(33, _service.Current.Credits);
            Assert.AreEqual(SaveStatus.Idle, _service.Status);
        }

        [Test]
        public void Mutate_then_SaveAsync_persists_and_reloads_through_a_new_service()
        {
            _service.InitializeAsync().GetAwaiter().GetResult();
            _service.Current.Credits = 9;
            _service.SaveAsync().GetAwaiter().GetResult();

            var second = new SaveService(new LocalProgressStore(_dir));
            second.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(9, second.Current.Credits);
        }

        [Test]
        public void SaveAsync_raises_started_and_completed_events_and_returns_to_Idle()
        {
            _service.InitializeAsync().GetAwaiter().GetResult();

            var fired = new List<string>();
            _service.OnSaveStarted += () => fired.Add("started");
            _service.OnSaveCompleted += () => fired.Add("completed");

            _service.SaveAsync().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "started", "completed" }, fired);
            Assert.AreEqual(SaveStatus.Idle, _service.Status);
        }

        [Test]
        public void Throwing_OnSaveStarted_subscriber_cannot_strand_status_at_Saving()
        {
            _service.InitializeAsync().GetAwaiter().GetResult();
            _service.OnSaveStarted += () => throw new InvalidOperationException("bad subscriber");

            // The subscriber throws BEFORE the first await, so the Error log lands
            // on this thread and LogAssert can expect it (the run-2 UTF trap only
            // bites for logs raised from thread-pool continuations).
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("SaveService: save failed"));

            Assert.Catch<InvalidOperationException>(
                () => _service.SaveAsync().GetAwaiter().GetResult());

            Assert.AreEqual(SaveStatus.Failed, _service.Status,
                "A throwing subscriber must surface as Failed, never a stuck Saving.");
        }

        [Test]
        public void File_vanishing_between_Exists_and_Load_falls_back_to_fresh_create()
        {
            // The TOCTOU case: Exists() said yes, but the file is gone by the time
            // the load runs (external deletion / concurrent delete). That is the
            // same situation as no-save-yet and must create defaults, not Fail.
            var service = new SaveService(new VanishingStore(_store));

            service.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(SaveStatus.Idle, service.Status);
            Assert.IsNotNull(service.Current);
            Assert.AreEqual(0, service.Current.Credits);
        }

        /// <summary>Reports an existing save whose load discovers the file is gone.</summary>
        private sealed class VanishingStore : IProgressStore
        {
            private readonly IProgressStore _inner;

            public VanishingStore(IProgressStore inner) => _inner = inner;

            public bool Exists() => true;

            public System.Threading.Tasks.Task<SaveModel> LoadAsync() =>
                throw new System.IO.FileNotFoundException("vanished");

            public System.Threading.Tasks.Task SaveAsync(SaveModel model) => _inner.SaveAsync(model);

            public System.Threading.Tasks.Task DeleteAsync() => _inner.DeleteAsync();
        }

        [Test]
        public void SaveAsync_before_initialize_throws_InvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(
                () => _service.SaveAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Constructing_with_null_store_throws_ArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => new SaveService(null));
        }

        [Test]
        public void Locator_wiring_happy_path_resolves_and_initializes()
        {
            // Mirrors Bootstrap's wiring (construct → register → seal) without
            // referencing Veilwalkers.App: register the store and service, seal the
            // table, resolve via Get<>, and initialize.
            GameServices.Register<IClock>(new SystemClock());
            GameServices.Register<IProgressStore>(_store);
            GameServices.Register<SaveService>(_service);
            GameServices.MarkReady();

            SaveService resolved = GameServices.Get<SaveService>();
            Assert.AreSame(_service, resolved);

            resolved.InitializeAsync().GetAwaiter().GetResult();

            Assert.IsNotNull(resolved.Current);
            Assert.AreEqual(0, resolved.Current.Credits);
            Assert.IsTrue(GameServices.Get<IProgressStore>().Exists());
        }
    }
}
