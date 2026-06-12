using System;
using System.IO;
using NUnit.Framework;

namespace Veilwalkers.Persistence.Tests
{
    /// <summary>
    /// The AR-20 tamper test (AC-3, load-bearing): a corrupt or tampered save makes
    /// load throw <see cref="SaveCorruptException"/>, the original file is NEVER
    /// silently wiped, <see cref="SaveService"/> lands in
    /// <see cref="SaveStatus.Corrupt"/> with the failure event raised, and both
    /// explicit recovery choices (retry / start fresh) work.
    /// </summary>
    public sealed class SaveTamperRecoveryTests
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

        private byte[] SaveAndTamper(int credits)
        {
            _store.SaveAsync(new SaveModel { Credits = credits }).GetAwaiter().GetResult();

            string path = TestSaveFiles.SavePath(_dir);
            byte[] good = File.ReadAllBytes(path);
            byte[] tampered = (byte[])good.Clone();
            tampered[tampered.Length / 2] ^= 0xFF; // flip a ciphertext byte
            File.WriteAllBytes(path, tampered);
            return good;
        }

        [Test]
        public void Flipped_ciphertext_byte_makes_load_throw_SaveCorrupt()
        {
            SaveAndTamper(7);

            Assert.Throws<SaveCorruptException>(
                () => _store.LoadAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Truncated_file_makes_load_throw_SaveCorrupt()
        {
            _store.SaveAsync(new SaveModel { Credits = 7 }).GetAwaiter().GetResult();
            string path = TestSaveFiles.SavePath(_dir);
            byte[] good = File.ReadAllBytes(path);

            // Truncate to a stub.
            byte[] stub = new byte[10];
            Array.Copy(good, stub, stub.Length);
            File.WriteAllBytes(path, stub);
            Assert.Throws<SaveCorruptException>(
                () => _store.LoadAsync().GetAwaiter().GetResult());

            // Truncate the tail (clips the MAC) — must also throw.
            byte[] clipped = new byte[good.Length - 5];
            Array.Copy(good, clipped, clipped.Length);
            File.WriteAllBytes(path, clipped);
            Assert.Throws<SaveCorruptException>(
                () => _store.LoadAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Corrupt_load_reaches_Corrupt_status_without_touching_the_file_then_StartFresh_recovers()
        {
            SaveAndTamper(7);
            string path = TestSaveFiles.SavePath(_dir);
            byte[] tamperedBytes = File.ReadAllBytes(path);

            var service = new SaveService(_store);
            Exception reported = null;
            service.OnLoadFailed += ex => reported = ex;

            // InitializeAsync does not fault on corruption — it parks in Corrupt and
            // waits for the explicit recovery choice.
            service.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(SaveStatus.Corrupt, service.Status);
            Assert.IsNull(service.Current);
            Assert.IsInstanceOf<SaveCorruptException>(reported);
            CollectionAssert.AreEqual(tamperedBytes, File.ReadAllBytes(path),
                "A corrupt save must NEVER be modified or wiped by the failed load.");

            // Recovery choice 2: start fresh — the only path that wipes.
            service.StartFreshAsync().GetAwaiter().GetResult();

            Assert.AreEqual(SaveStatus.Idle, service.Status);
            Assert.IsNotNull(service.Current);
            Assert.AreEqual(0, service.Current.Credits, "Fresh save has 0 credits (the grant is Story 1.7).");
            Assert.AreEqual(0, _store.LoadAsync().GetAwaiter().GetResult().Credits,
                "The fresh default save must actually be persisted and loadable.");
        }

        [Test]
        public void Non_numeric_anchor_component_throws_SaveCorrupt()
        {
            // Converter conversions throw FormatException (not JsonException) for
            // non-numeric components; that must still classify as corruption so the
            // recovery flow is offered instead of a Failed rethrow.
            TestSaveFiles.WriteCraftedSave(_dir,
                "{\"schemaVersion\":1,\"encounterSnapshot\":{\"anchors\":[{\"trackableId\":\"t\"," +
                "\"position\":{\"x\":\"abc\",\"y\":0,\"z\":0}," +
                "\"rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}],\"monsters\":[]}}");

            Assert.Throws<SaveCorruptException>(
                () => _store.LoadAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Corruption_after_a_successful_load_clears_Current()
        {
            _store.SaveAsync(new SaveModel { Credits = 7 }).GetAwaiter().GetResult();

            var service = new SaveService(_store);
            service.InitializeAsync().GetAwaiter().GetResult();
            Assert.IsNotNull(service.Current);

            // The file is tampered AFTER a successful session load; a re-load must
            // not leave consumers running on the stale prior model (the doc promises
            // Current is null while corrupt and unrecovered).
            string path = TestSaveFiles.SavePath(_dir);
            byte[] tampered = File.ReadAllBytes(path);
            tampered[tampered.Length / 2] ^= 0xFF;
            File.WriteAllBytes(path, tampered);

            service.RetryLoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(SaveStatus.Corrupt, service.Status);
            Assert.IsNull(service.Current,
                "A corrupt re-load must clear the stale in-memory model.");
        }

        [Test]
        public void RetryLoad_recovers_once_the_file_is_intact_again()
        {
            byte[] good = SaveAndTamper(7);
            string path = TestSaveFiles.SavePath(_dir);

            var service = new SaveService(_store);
            service.InitializeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(SaveStatus.Corrupt, service.Status);

            // The file becomes intact again (e.g. the user restored a backup), and
            // recovery choice 1 — retry — succeeds without any data loss.
            File.WriteAllBytes(path, good);
            service.RetryLoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(SaveStatus.Idle, service.Status);
            Assert.AreEqual(7, service.Current.Credits);
        }
    }
}
