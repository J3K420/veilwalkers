using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Veilwalkers.Persistence.Tests
{
    /// <summary>
    /// AC-3's null-collection rule: null collections are never persisted (they are
    /// written as empty) and never returned from a load (they are coerced to empty),
    /// covering top-level AND nested collections. A fully-null
    /// <c>encounterSnapshot</c> OBJECT stays null — that legally means "no active
    /// encounter".
    /// </summary>
    public sealed class NullCollectionTests
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
        public void Loading_null_collections_returns_empty_collections_never_null()
        {
            // Crafted save with nulls everywhere a collection (or the adReward
            // object) could be null — including the NESTED collections of a present
            // encounterSnapshot.
            TestSaveFiles.WriteCraftedSave(_dir,
                "{\"schemaVersion\":1,\"credits\":5,\"codex\":null,\"pendingPurchases\":null," +
                "\"adReward\":null,\"encounterSnapshot\":{\"anchors\":null,\"monsters\":null}}");

            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(5, loaded.Credits);
            Assert.IsNotNull(loaded.Codex);
            Assert.IsEmpty(loaded.Codex);
            Assert.IsNotNull(loaded.PendingPurchases);
            Assert.IsEmpty(loaded.PendingPurchases);
            Assert.IsNotNull(loaded.AdReward);
            Assert.IsNotNull(loaded.EncounterSnapshot, "A PRESENT snapshot must stay present.");
            Assert.IsNotNull(loaded.EncounterSnapshot.Anchors);
            Assert.IsEmpty(loaded.EncounterSnapshot.Anchors);
            Assert.IsNotNull(loaded.EncounterSnapshot.Monsters);
            Assert.IsEmpty(loaded.EncounterSnapshot.Monsters);
        }

        [Test]
        public void Null_encounterSnapshot_object_loads_as_no_active_encounter()
        {
            TestSaveFiles.WriteCraftedSave(_dir,
                "{\"schemaVersion\":1,\"credits\":0,\"encounterSnapshot\":null}");

            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.IsNull(loaded.EncounterSnapshot,
                "A null snapshot OBJECT means 'no active encounter' and must not be coerced to an empty object.");
        }

        [Test]
        public void Null_list_ELEMENTS_are_removed_on_load_never_returned()
        {
            // Element-level repair: crafted [null] entries inside lists must not
            // survive into the loaded model (downstream iteration would null-ref).
            TestSaveFiles.WriteCraftedSave(_dir,
                "{\"schemaVersion\":1," +
                "\"codex\":{\"mon01\":{\"scanned\":true,\"variantFlags\":[null,\"shadow\"]}}," +
                "\"pendingPurchases\":[null]," +
                "\"encounterSnapshot\":{\"anchors\":[],\"monsters\":[null]}}");

            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.IsEmpty(loaded.PendingPurchases, "A [null] purchase entry must be dropped.");
            Assert.IsEmpty(loaded.EncounterSnapshot.Monsters, "A [null] monster entry must be dropped.");
            CollectionAssert.AreEqual(new[] { "shadow" }, loaded.Codex["mon01"].VariantFlags,
                "Null variant-flag elements must be dropped; real ones kept.");
        }

        [Test]
        public void Saving_a_model_with_null_collections_persists_empty_not_null()
        {
            var model = new SaveModel
            {
                Codex = null,
                PendingPurchases = null,
                EncounterSnapshot = new EncounterSnapshotData { Anchors = null, Monsters = null },
            };

            _store.SaveAsync(model).GetAwaiter().GetResult();

            JObject json = JObject.Parse(TestSaveFiles.ReadSaveJson(_dir));
            Assert.AreEqual(JTokenType.Object, json["codex"].Type,
                "A null codex must be persisted as an empty object, never null.");
            Assert.AreEqual(JTokenType.Array, json["pendingPurchases"].Type,
                "Null pendingPurchases must be persisted as an empty array, never null.");
            Assert.AreEqual(JTokenType.Array, json["encounterSnapshot"]["anchors"].Type,
                "Nested null collections of a present snapshot must persist as empty arrays.");
            Assert.AreEqual(JTokenType.Array, json["encounterSnapshot"]["monsters"].Type);
        }

        [Test]
        public void Persisted_json_uses_camelCase_property_names()
        {
            _store.SaveAsync(TestSaveFiles.BuildFullModel()).GetAwaiter().GetResult();

            JObject json = JObject.Parse(TestSaveFiles.ReadSaveJson(_dir));
            Assert.IsNotNull(json["schemaVersion"]);
            Assert.IsNotNull(json["credits"]);
            Assert.IsNotNull(json["strongCaptureCharges"]);
            Assert.IsNotNull(json["firstZeroCreditDay"]);
            Assert.IsNull(json["SchemaVersion"], "PascalCase names must not appear in the JSON.");
        }
    }
}
