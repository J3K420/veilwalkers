using NUnit.Framework;

namespace Veilwalkers.Persistence.Tests
{
    /// <summary>
    /// Story 6.6 (AC-1) — the persisted reduced-motion / photosensitivity opt-in. Pins the additive
    /// scalar behaves like the <c>StartingCreditsGranted</c>/<c>GuaranteedRareLures</c> precedent: a
    /// fresh model defaults <c>false</c> (motion-ON, the safe default), the flag round-trips through the
    /// full encrypt/atomic/decrypt pipeline, and an existing save that LACKS the JSON property
    /// deserializes to <c>false</c> with NO schema-version bump / migration.
    /// </summary>
    public sealed class ReducedMotionSettingTests
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
        public void Fresh_model_defaults_reduced_motion_off_motion_on()
        {
            // A new player has motion ON (the safe default — never silently forced into reduced-motion).
            Assert.IsFalse(new SaveModel().ReducedMotion);
        }

        [Test]
        public void Reduced_motion_round_trips_when_enabled()
        {
            var model = new SaveModel { ReducedMotion = true };

            _store.SaveAsync(model).GetAwaiter().GetResult();
            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(loaded.ReducedMotion, "the opt-in must survive the save pipeline");
        }

        [Test]
        public void Reduced_motion_round_trips_when_disabled()
        {
            var model = new SaveModel { ReducedMotion = false };

            _store.SaveAsync(model).GetAwaiter().GetResult();
            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.IsFalse(loaded.ReducedMotion);
        }

        [Test]
        public void Existing_save_without_the_property_deserializes_to_false_no_migration()
        {
            // A current-version (v2) save that predates the field — it has NO reducedMotion property.
            // It must deserialize to false (motion-on, never silently tamed) WITHOUT any migration:
            // CurrentVersion is unchanged, so the load is a plain pass-through, not a v-bump.
            TestSaveFiles.WriteCraftedSave(_dir, "{\"schemaVersion\":2}");

            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.IsFalse(loaded.ReducedMotion,
                "an existing save lacking the property → false (the additive-scalar default)");
            Assert.AreEqual(SaveMigrations.CurrentVersion, loaded.SchemaVersion);
        }

        [Test]
        public void Reduced_motion_field_needs_no_schema_version_bump()
        {
            // The additive field rides at the current schema version — pinning that this build still
            // reads + writes the same CurrentVersion (no MigrateVxToVy was introduced for the field).
            Assert.AreEqual(2, SaveMigrations.CurrentVersion,
                "the reduced-motion field is additive — no schema bump (the GuaranteedRareLures precedent)");
        }
    }
}
