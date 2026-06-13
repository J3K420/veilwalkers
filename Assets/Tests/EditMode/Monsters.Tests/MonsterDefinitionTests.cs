using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Veilwalkers.Monsters.Tests
{
    /// <summary>
    /// <see cref="MonsterDefinition"/> as a static-data ScriptableObject (Story 2.1):
    /// its accessors round-trip the authored values (AC-1/AC-2/AC-3), and — the
    /// load-bearing AC-1 assertion — it exposes NO public mutable state, so player/runtime
    /// state cannot leak into it. In-memory instances via
    /// <c>ScriptableObject.CreateInstance</c> + the internal <c>SetForTests</c> seam.
    /// </summary>
    public sealed class MonsterDefinitionTests
    {
        private static MonsterDefinition Definition(
            string id = "mon01",
            string displayName = "Antlered Shade",
            Rarity rarity = Rarity.Rare,
            int captureDifficulty = 5,
            int slayDifficulty = 7,
            string lore = "It watches from the treeline.",
            Sprite art = null)
        {
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.SetForTests(id, displayName, rarity, captureDifficulty, slayDifficulty, lore, art);
            return def;
        }

        [Test]
        public void Accessors_round_trip_set_values()
        {
            var sprite = Sprite.Create(
                new Texture2D(2, 2), new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            var def = Definition(
                id: "mon07",
                displayName: "Veil Crawler",
                rarity: Rarity.Epic,
                captureDifficulty: 9,
                slayDifficulty: 12,
                lore: "It unfolds where the light forgets to reach.",
                art: sprite);

            Assert.That(def.Id, Is.EqualTo("mon07"));
            Assert.That(def.DisplayName, Is.EqualTo("Veil Crawler"));
            Assert.That(def.Rarity, Is.EqualTo(Rarity.Epic));
            Assert.That(def.CaptureDifficulty, Is.EqualTo(9));
            Assert.That(def.SlayDifficulty, Is.EqualTo(12));
            Assert.That(def.Lore, Is.EqualTo("It unfolds where the light forgets to reach."));
            // AC-1: art ref is part of the static data; pin the _art -> Art wiring so a
            // future mis-wire (e.g. Art returning the wrong field) cannot pass silently.
            Assert.That(def.Art, Is.SameAs(sprite));
        }

        /// <summary>
        /// AC-1 — falsifiable static-data invariant: of the members <see cref="MonsterDefinition"/>
        /// ITSELF declares, NO public instance field is mutable (writable &amp; non-const/non-readonly)
        /// and NO public property exposes a setter. This is a read-only invariant, NOT a name
        /// denylist — adding ANY public mutable member to this type (e.g. a "Discovered" setter or
        /// a "TimesEncountered" field) turns this red, which is the point: player/runtime state must
        /// never live on the SO.
        /// <para>
        /// Scoped with <see cref="BindingFlags.DeclaredOnly"/>: Unity's base
        /// <c>UnityEngine.Object.name</c>/<c>hideFlags</c> setters are framework infrastructure on
        /// the <see cref="ScriptableObject"/> base, not game/player state this story introduces, so
        /// they are correctly out of scope for the AC-1 contract.
        /// </para>
        /// </summary>
        [Test]
        public void No_public_mutable_state()
        {
            var type = typeof(MonsterDefinition);
            const BindingFlags declared =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            var writablePublicFields = type
                .GetFields(declared)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .Select(f => f.Name)
                .ToArray();

            Assert.That(writablePublicFields, Is.Empty,
                "MonsterDefinition must declare no public mutable field (static data only). " +
                "Found: " + string.Join(", ", writablePublicFields));

            var propertiesWithPublicSetter = type
                .GetProperties(declared)
                .Where(p => p.GetSetMethod(nonPublic: false) != null)
                .Select(p => p.Name)
                .ToArray();

            Assert.That(propertiesWithPublicSetter, Is.Empty,
                "MonsterDefinition must declare no property with a public setter (static data only). " +
                "Found: " + string.Join(", ", propertiesWithPublicSetter));
        }

        [Test]
        public void Id_follows_monNN_format()
        {
            // Format-level only — content auditing of all 67 IDs belongs to Story 2.2's
            // MonsterDatabase. This pins the convention shape for a single definition.
            var def = Definition(id: "mon42");
            Assert.That(Regex.IsMatch(def.Id, "^mon\\d{2}$"), Is.True,
                "Monster IDs follow the stable monNN format (mon01..mon67).");
        }

        [Test]
        public void Asset_file_name_convention_is_Id_underscore_Name()
        {
            // AC-4 convention expressed as a derivable name: <Id>_<PascalName>.asset.
            var def = Definition(id: "mon01", displayName: "Antlered Shade");
            var expectedStem = def.Id.Substring(0, 1).ToUpperInvariant() + def.Id.Substring(1)
                + "_" + def.DisplayName.Replace(" ", string.Empty);
            Assert.That(expectedStem, Is.EqualTo("Mon01_AntleredShade"));
        }
    }
}
