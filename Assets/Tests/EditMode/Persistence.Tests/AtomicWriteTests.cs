using System;
using System.IO;
using NUnit.Framework;

namespace Veilwalkers.Persistence.Tests
{
    /// <summary>
    /// AC-2: the temp-file + swap pipeline means a write that dies before the swap
    /// leaves the prior valid save intact, and stale temp files are cleaned up and
    /// never mistaken for the save.
    /// </summary>
    public sealed class AtomicWriteTests
    {
        private string _dir;
        private LocalProgressStore _store;

        [SetUp]
        public void SetUp()
        {
            _dir = TestSaveFiles.CreateTempDir();
            _store = new LocalProgressStore(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            TestSaveFiles.DeleteTempDir(_dir);
        }

        [Test]
        public void Failed_write_before_swap_leaves_prior_save_intact()
        {
            var original = new SaveModel { Credits = 11 };
            _store.SaveAsync(original).GetAwaiter().GetResult();

            // Simulate a mid-write kill: hold the temp file exclusively locked so the
            // NEXT write fails while producing the temp file — i.e. before the swap
            // ever touches save.dat.
            //
            // Windows-host assumption: FileShare.None makes the store's delete/create
            // of the temp file throw a sharing violation. On a POSIX host the held
            // file would simply be unlinked and the save would SUCCEED, failing this
            // test — fine while the Editor/test host is Windows-only; revisit if the
            // suite ever runs on macOS/Linux CI.
            using (new FileStream(
                TestSaveFiles.TempPath(_dir), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var replacement = new SaveModel { Credits = 99 };
                Assert.Catch<Exception>(
                    () => _store.SaveAsync(replacement).GetAwaiter().GetResult(),
                    "The save attempt should fault when it cannot produce the temp file.");
            }

            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(11, loaded.Credits, "The prior valid save must survive a failed write.");
        }

        [Test]
        public void Stale_temp_file_is_cleaned_on_load_and_never_loaded_as_the_save()
        {
            var model = new SaveModel { Credits = 5 };
            _store.SaveAsync(model).GetAwaiter().GetResult();

            // A stale temp from a killed write: garbage that must never be read.
            File.WriteAllBytes(TestSaveFiles.TempPath(_dir), new byte[] { 1, 2, 3, 4 });

            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(5, loaded.Credits, "The valid save, not the stale temp, must be loaded.");
            Assert.IsFalse(File.Exists(TestSaveFiles.TempPath(_dir)),
                "The stale temp file must be cleaned up on load.");
        }

        [Test]
        public void Successful_save_leaves_no_temp_file_behind()
        {
            _store.SaveAsync(new SaveModel { Credits = 1 }).GetAwaiter().GetResult();

            Assert.IsTrue(File.Exists(TestSaveFiles.SavePath(_dir)));
            Assert.IsFalse(File.Exists(TestSaveFiles.TempPath(_dir)),
                "After the swap there must be no temp file left.");
        }

        [Test]
        public void First_ever_save_creates_the_file_and_overwrite_replaces_it()
        {
            Assert.IsFalse(_store.Exists());

            _store.SaveAsync(new SaveModel { Credits = 1 }).GetAwaiter().GetResult();
            Assert.IsTrue(_store.Exists(), "First-ever save must create save.dat (File.Move branch).");

            _store.SaveAsync(new SaveModel { Credits = 2 }).GetAwaiter().GetResult();
            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(2, loaded.Credits, "Subsequent saves must atomically replace (File.Replace branch).");
        }
    }
}
