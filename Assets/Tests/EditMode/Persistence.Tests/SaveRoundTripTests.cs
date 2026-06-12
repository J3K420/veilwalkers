using System.IO;
using System.Text;
using NUnit.Framework;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.Persistence.Tests
{
    /// <summary>
    /// AC-1: a fully-populated <see cref="SaveModel"/> survives the full pipeline
    /// (camelCase JSON → encrypt → atomic write → read → MAC verify → decrypt →
    /// migrate → validate) with every field intact, and the bytes on disk are not
    /// readable plaintext.
    /// </summary>
    public sealed class SaveRoundTripTests
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
        public void Fully_populated_model_round_trips_with_all_fields_equal()
        {
            SaveModel original = TestSaveFiles.BuildFullModel();

            _store.SaveAsync(original).GetAwaiter().GetResult();
            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(SaveMigrations.CurrentVersion, loaded.SchemaVersion);
            Assert.AreEqual(42, loaded.Credits);
            Assert.AreEqual(120, loaded.Xp);
            Assert.AreEqual(3, loaded.Level);
            Assert.AreEqual(1, loaded.StrongCaptureCharges);
            Assert.AreEqual(2, loaded.StabilityBoostCharges);
            Assert.AreEqual(3, loaded.NightveilFilterCharges);
            Assert.AreEqual("2026-06-11", loaded.DailyClaim);
            Assert.AreEqual(2, loaded.AdReward.GrantsToday);
            Assert.AreEqual(20619L, loaded.AdReward.DayBucket);
            Assert.AreEqual(20620L, loaded.FirstZeroCreditDay);

            Assert.AreEqual(2, loaded.Codex.Count);
            CodexEntryData mon01 = loaded.Codex["mon01"];
            Assert.IsTrue(mon01.Scanned);
            Assert.IsTrue(mon01.Captured);
            Assert.IsFalse(mon01.Slain);
            CollectionAssert.AreEqual(new[] { "shadow" }, mon01.VariantFlags);
            Assert.IsTrue(loaded.Codex["mon07"].Scanned);

            Assert.IsNotNull(loaded.EncounterSnapshot);
            Assert.AreEqual(1, loaded.EncounterSnapshot.Anchors.Length);
            AnchorToken anchor = loaded.EncounterSnapshot.Anchors[0];
            Assert.AreEqual("trk-001", anchor.trackableId);
            Assert.AreEqual(1.5f, anchor.position.x);
            Assert.AreEqual(2f, anchor.position.y);
            Assert.AreEqual(-3.25f, anchor.position.z);
            Assert.AreEqual(0.7071f, anchor.rotation.y, 0.0001f);
            Assert.AreEqual(0.7071f, anchor.rotation.w, 0.0001f);

            Assert.AreEqual(1, loaded.EncounterSnapshot.Monsters.Count);
            EncounterMonsterStateData monster = loaded.EncounterSnapshot.Monsters[0];
            Assert.AreEqual("mon01", monster.MonsterId);
            Assert.AreEqual(0.5f, monster.ScanProgress);
            CollectionAssert.AreEqual(new[] { "stabilityBoost" }, monster.AppliedBoosts);

            Assert.AreEqual(1, loaded.PendingPurchases.Count);
            PendingPurchaseRecord purchase = loaded.PendingPurchases[0];
            Assert.AreEqual("GPA.0000-1111-2222", purchase.OrderId);
            Assert.AreEqual("credits50", purchase.PackId);
            Assert.AreEqual("grantedUnacked", purchase.State);
            Assert.AreEqual("2026-06-12T08:30:00Z", purchase.IsoTimestampUtc);
        }

        [Test]
        public void File_on_disk_is_not_plaintext_json()
        {
            _store.SaveAsync(TestSaveFiles.BuildFullModel()).GetAwaiter().GetResult();

            byte[] raw = File.ReadAllBytes(TestSaveFiles.SavePath(_dir));
            string asText = Encoding.UTF8.GetString(raw);

            // Known field names and ids must not appear in the raw bytes — if they
            // do, the payload was written unencrypted.
            StringAssert.DoesNotContain("schemaVersion", asText);
            StringAssert.DoesNotContain("credits", asText);
            StringAssert.DoesNotContain("mon01", asText);
        }

        [Test]
        public void Dictionary_keys_are_persisted_verbatim_not_recased()
        {
            var model = new SaveModel();
            model.Codex["Mon01_UPPER"] = new CodexEntryData { Scanned = true };

            _store.SaveAsync(model).GetAwaiter().GetResult();
            SaveModel loaded = _store.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(loaded.Codex.ContainsKey("Mon01_UPPER"),
                "Codex keys must round-trip verbatim; the camelCase resolver must not re-case dictionary keys.");
        }
    }
}
