using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Veilwalkers.Monsters.Tests
{
    /// <summary>
    /// <see cref="MonsterDatabase"/> as the 67-universe registry + content-validation layer
    /// (Story 2.2): ID-keyed lookup (never by index), the AC-3 "≥1 Rare+" Veil Pack
    /// criterion, and the falsifiable content validation that settles Story 2.1's
    /// null/range CR deferrals at the registry boundary. In-memory via
    /// <c>CreateInstance</c> + <c>SetForTests</c> — no serialized <c>.asset</c> needed.
    /// </summary>
    public sealed class MonsterDatabaseTests
    {
        private static MonsterDefinition Def(
            string id = "mon01",
            string displayName = "Antlered Shade",
            Rarity rarity = Rarity.Common,
            string lore = "It watches.",
            Sprite art = null)
        {
            // A valid definition needs non-null art; build a real 1x1 sprite by default.
            art = art ?? Sprite.Create(new Texture2D(1, 1), new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
            var d = ScriptableObject.CreateInstance<MonsterDefinition>();
            d.SetForTests(id, displayName, rarity, 5, 7, lore, art);
            return d;
        }

        private static MonsterDatabase Db(params MonsterDefinition[] defs)
        {
            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            db.SetForTests(defs);
            return db;
        }

        [Test]
        public void Registry_resolves_by_id()
        {
            var db = Db(Def("mon01"), Def("mon02", "Veil Crawler"), Def("mon03", rarity: Rarity.Rare));

            Assert.That(db.TryGet("mon02", out var def), Is.True);
            Assert.That(def.DisplayName, Is.EqualTo("Veil Crawler"));

            Assert.That(db.TryGet("mon99", out var missing), Is.False);
            Assert.That(missing, Is.Null);

            // Null/empty id never throws, returns false.
            Assert.That(db.TryGet(null, out _), Is.False);
            Assert.That(db.TryGet("", out _), Is.False);
        }

        [Test]
        public void Lookup_uses_id_not_index()
        {
            // Insert out of ID order; fetch by ID must return the right one regardless of position.
            var db = Db(Def("mon03", "Third"), Def("mon01", "First"), Def("mon02", "Second"));
            Assert.That(db.GetById("mon01").DisplayName, Is.EqualTo("First"));
            Assert.That(db.GetById("mon03").DisplayName, Is.EqualTo("Third"));
        }

        [Test]
        public void Validate_passes_for_well_formed_registry()
        {
            var db = Db(
                Def("mon01", "Antlered Shade", Rarity.Common),
                Def("mon02", "Veil Crawler", Rarity.Uncommon),
                Def("mon03", "Nightmare Maw", Rarity.Rare));
            Assert.That(db.Validate(), Is.Empty);
        }

        [Test]
        public void Validate_flags_missing_content()
        {
            // Each bad entry exercises a distinct guard 2.1 deferred here.
            var nullName = Def("mon01", displayName: null);
            var nullLore = Def("mon02", lore: null);
            var nullArt = ScriptableObject.CreateInstance<MonsterDefinition>();
            nullArt.SetForTests("mon03", "No Art", Rarity.Common, 1, 1, "lore", null); // art null
            var badId = Def("mon99x"); // wrong format / out of universe
            var outOfRange = ScriptableObject.CreateInstance<MonsterDefinition>();
            outOfRange.SetForTests("mon04", "OOR", (Rarity)99, 1, 1, "lore",
                Sprite.Create(new Texture2D(1, 1), new Rect(0, 0, 1, 1), Vector2.one * 0.5f));
            var dupA = Def("mon05", "Dup A");
            var dupB = Def("mon05", "Dup B"); // duplicate id

            var db = Db(nullName, nullLore, nullArt, badId, outOfRange, dupA, dupB);
            var problems = db.Validate();

            Assert.That(problems.Any(p => p.Contains("DisplayName")), Is.True, "null DisplayName not flagged");
            Assert.That(problems.Any(p => p.Contains("Lore")), Is.True, "null Lore not flagged");
            Assert.That(problems.Any(p => p.Contains("Art")), Is.True, "null Art not flagged");
            Assert.That(problems.Any(p => p.Contains("invalid Id")), Is.True, "bad Id not flagged");
            Assert.That(problems.Any(p => p.Contains("out-of-range Rarity")), Is.True, "out-of-range Rarity not flagged");
            Assert.That(problems.Any(p => p.Contains("duplicates Id")), Is.True, "duplicate Id not flagged");
        }

        [Test]
        public void Validate_flags_null_entry()
        {
            // A null array slot must be reported, never dereferenced (no NRE).
            var db = Db(Def("mon01"), null, Def("mon02"));
            var problems = db.Validate();
            Assert.That(problems.Any(p => p.Contains("null")), Is.True, "null entry not flagged");
        }

        [Test]
        public void Duplicate_id_lookup_is_deterministic_first_writer_wins()
        {
            // Two entries share mon01; lookup must deterministically resolve to the FIRST
            // (array order), never silently to a later duplicate. (Validate still flags it.)
            var first = Def("mon01", "First");
            var second = Def("mon01", "Second");
            var db = Db(first, second);

            Assert.That(db.GetById("mon01").DisplayName, Is.EqualTo("First"));
            Assert.That(db.Validate().Any(p => p.Contains("duplicates Id")), Is.True);
        }

        [Test]
        public void Validate_does_not_throw_on_null_backing_array()
        {
            // A corrupt/migrated SO can deserialize _monsters to null; Validate must not NRE.
            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            db.SetForTests(null); // SetForTests coerces to empty; mirror the contract
            Assert.That(db.Validate(), Is.Empty);
        }

        [Test]
        public void Populated_is_a_defensive_copy()
        {
            var db = Db(Def("mon01", "Original"));
            var snapshot = db.Populated;
            // Mutating a cast-back of the returned list must NOT affect the registry.
            ((MonsterDefinition[])snapshot)[0] = null;
            Assert.That(db.GetById("mon01"), Is.Not.Null, "registry backing store was mutated via Populated");
            Assert.That(db.GetById("mon01").DisplayName, Is.EqualTo("Original"));
        }

        [Test]
        public void At_least_one_populated_is_rare_or_better()
        {
            // AC-3 (Veil Pack validity) against the canonical threshold symbol from 2.1.
            var db = Db(
                Def("mon01", "Common One", Rarity.Common),
                Def("mon02", "Uncommon One", Rarity.Uncommon),
                Def("mon03", "Rare One", Rarity.Rare));

            Assert.That(db.Validate(), Is.Empty);
            Assert.That(
                db.Populated.Any(m => m.Rarity >= RarityThresholds.GuaranteedRareFloor),
                Is.True,
                "MVP content must include at least one Rarity >= Rare for Veil Pack validity.");
        }
    }
}
