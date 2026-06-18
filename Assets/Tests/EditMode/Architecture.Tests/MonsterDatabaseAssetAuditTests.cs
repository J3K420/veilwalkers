using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Veilwalkers.Monsters;

namespace Veilwalkers.Architecture.Tests
{
    /// <summary>
    /// Story 2.2 Task-3 / Epic 8 Gate 0 — the ON-DISK audit of the authored
    /// <see cref="MonsterDatabase"/> asset. Where <see cref="MonsterDatabaseLoreCountTests"/>
    /// pins the model relationships against the public type surface (universe == 67, the
    /// monNN range logic) and <c>Veilwalkers.Monsters.Tests.MonsterDatabaseTests</c> pins the
    /// in-memory registry behaviour, THIS test loads the REAL serialized
    /// <c>MonsterDatabase.asset</c> + its definition assets via <see cref="AssetDatabase"/>
    /// and asserts they are authored correctly — the falsifiable replacement for Story 2.1's
    /// old tautological <c>Asset_file_name_convention</c> test (which rebuilt a string from
    /// constants it set itself and never touched disk).
    /// <para>
    /// Non-vacuous: an absent asset, an unpopulated registry, a count outside [3..5], a
    /// content-validation failure, a missing Rare+ floor monster, or a definition whose
    /// <c>.asset</c> filename does not follow <c>&lt;Id&gt;_&lt;Name&gt;</c> each turns a
    /// distinct assertion RED. Editor-only (the asmdef is Editor-platform; <c>AssetDatabase</c>
    /// is the Pit/Acyclic precedent in this assembly).
    /// </para>
    /// <para>
    /// [Source: docs/epics.md#Story-2.2 (AC-1/AC-3); docs/epic-8-device-release-gate.md Gate 0.1;
    /// the Story 2.2 Editor-authoring guide.]
    /// </para>
    /// </summary>
    public sealed class MonsterDatabaseAssetAuditTests
    {
        private const string DatabaseAssetPath =
            "Assets/Veilwalkers/ScriptableObjects/MonsterDatabase.asset";

        private static MonsterDatabase LoadDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<MonsterDatabase>(DatabaseAssetPath);
            Assert.That(db, Is.Not.Null,
                $"MonsterDatabase.asset must exist at '{DatabaseAssetPath}' and load as a " +
                "MonsterDatabase (Story 2.2 / Epic 8 Gate 0.1 authoring). Author it in the Editor " +
                "and assign the MVP MonsterDefinition assets to it.");
            return db;
        }

        [Test]
        public void Database_asset_exists_and_loads()
        {
            // The guard itself; LoadDatabase asserts non-null with an authoring hint.
            LoadDatabase();
        }

        [Test]
        public void Populated_count_is_in_the_MVP_range()
        {
            var db = LoadDatabase();
            Assert.That(db.PopulatedCount, Is.InRange(3, 5),
                "MVP populates 3–5 Monsters (AR-3 / Story 2.2 AC-1). PopulatedCount is " +
                db.PopulatedCount + ".");
        }

        [Test]
        public void Content_validation_reports_no_problems()
        {
            var db = LoadDatabase();
            var problems = db.Validate();
            Assert.That(problems, Is.Empty,
                "The authored MonsterDatabase content must pass Validate() (non-empty monNN id " +
                "in-universe, unique; non-empty DisplayName + Lore; non-null Art; in-range Rarity). " +
                "Problems: " + string.Join(" | ", problems));
        }

        [Test]
        public void At_least_one_populated_monster_is_Rare_or_better()
        {
            var db = LoadDatabase();
            bool hasRarePlus = db.Populated.Any(
                m => m != null && m.Rarity >= RarityThresholds.GuaranteedRareFloor);
            Assert.That(hasRarePlus, Is.True,
                "At least one populated Monster must be Rarity >= " +
                RarityThresholds.GuaranteedRareFloor + " (Veil Pack / Guaranteed-Rare Lure " +
                "validity, Story 2.2 AC-3).");
        }

        [Test]
        public void Each_populated_definition_asset_follows_Id_underscore_Name()
        {
            var db = LoadDatabase();

            foreach (var m in db.Populated)
            {
                Assert.That(m, Is.Not.Null,
                    "MonsterDatabase has an unassigned (null) populated slot — fill every slot.");

                string assetPath = AssetDatabase.GetAssetPath(m);
                Assert.That(assetPath, Is.Not.Empty,
                    $"Populated definition '{m.Id}' has no on-disk asset path — it must be a " +
                    "serialized .asset, not an in-memory instance.");

                string fileStem = Path.GetFileNameWithoutExtension(assetPath);

                // The convention: <Id>_<Name> where Id is monNN with the leading 'm'
                // capitalised (Mon01) and Name is the DisplayName with spaces removed.
                string expectedStem =
                    char.ToUpperInvariant(m.Id[0]) + m.Id.Substring(1)
                    + "_" + m.DisplayName.Replace(" ", string.Empty);

                Assert.That(fileStem, Is.EqualTo(expectedStem),
                    $"Definition asset filename '{fileStem}' must follow <Id>_<Name> " +
                    $"('{expectedStem}' for id '{m.Id}' / name '{m.DisplayName}').");
            }
        }
    }
}
