using NUnit.Framework;

namespace Veilwalkers.Persistence.Tests
{
    /// <summary>
    /// AC-1's migration routing: <c>schemaVersion: 1</c> loads, unknown versions
    /// throw <see cref="SaveCorruptException"/>, and a save without an integer
    /// schemaVersion is rejected as corrupt rather than guessed at.
    /// </summary>
    public sealed class MigrationRoutingTests
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
        public void SchemaVersion_1_loads_and_missing_fields_default()
        {
            TestSaveFiles.WriteCraftedSave(_dir, "{\"schemaVersion\":1}");

            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(SaveMigrations.CurrentVersion, loaded.SchemaVersion);
            Assert.AreEqual(0, loaded.Credits);
            Assert.IsNotNull(loaded.Codex);
            Assert.IsNotNull(loaded.PendingPurchases);
            // v1→v2 (Story 1.7): an existing save migrates with the first-launch marker
            // TRUE so the player is not re-granted their starting Credits on update.
            Assert.IsTrue(loaded.StartingCreditsGranted,
                "A migrated v1 save must carry StartingCreditsGranted == true.");
        }

        [Test]
        public void Migrating_a_v1_save_with_credits_preserves_them_and_marks_granted()
        {
            // An existing player (v1 save, real balance) must keep their credits AND be
            // marked granted — defaulting the marker false would re-grant +20 on update.
            TestSaveFiles.WriteCraftedSave(_dir, "{\"schemaVersion\":1,\"credits\":7}");

            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(SaveMigrations.CurrentVersion, loaded.SchemaVersion);
            Assert.AreEqual(7, loaded.Credits, "Existing balance is preserved across the migration.");
            Assert.IsTrue(loaded.StartingCreditsGranted,
                "An existing player is marked granted so they are not re-granted.");
        }

        [Test]
        public void Unknown_newer_schemaVersion_throws_SaveCorrupt()
        {
            TestSaveFiles.WriteCraftedSave(_dir, "{\"schemaVersion\":999}");

            Assert.Throws<SaveCorruptException>(
                () => _store.LoadAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Missing_or_non_integer_schemaVersion_throws_SaveCorrupt()
        {
            TestSaveFiles.WriteCraftedSave(_dir, "{\"credits\":3}");
            Assert.Throws<SaveCorruptException>(
                () => _store.LoadAsync().GetAwaiter().GetResult());

            TestSaveFiles.WriteCraftedSave(_dir, "{\"schemaVersion\":\"one\"}");
            Assert.Throws<SaveCorruptException>(
                () => _store.LoadAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Out_of_int_range_schemaVersion_throws_SaveCorrupt()
        {
            // JTokenType.Integer admits values past int range; the conversion
            // overflow must classify as corruption (recovery flow), not Failed.
            TestSaveFiles.WriteCraftedSave(_dir, "{\"schemaVersion\":99999999999}");

            Assert.Throws<SaveCorruptException>(
                () => _store.LoadAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Valid_encryption_but_garbage_json_throws_SaveCorrupt()
        {
            TestSaveFiles.WriteCraftedSave(_dir, "this is not json {{{");

            Assert.Throws<SaveCorruptException>(
                () => _store.LoadAsync().GetAwaiter().GetResult());
        }
    }
}
