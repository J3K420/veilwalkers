using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Veilwalkers.Persistence;

namespace Veilwalkers.Monsters.Tests
{
    /// <summary>
    /// <see cref="CodexService"/>'s Story 2.4 reveal read model: the Dread-Ladder high-water mark
    /// (<see cref="CodexService.HighestDiscoveredRarity"/>), the begun-tier rule
    /// (<see cref="CodexService.IsTierBegun"/>), the per-id tier lookup
    /// (<see cref="CodexService.GetTier"/>), and the settled-2.3 <see cref="CodexService.GetDiscoveredIds"/>
    /// snapshot-method reshape. Built on a real <see cref="SaveService"/> over the deep-cloning
    /// <see cref="FakeCodexProgressStore"/> + an in-memory <see cref="MonsterDatabase"/> authored
    /// at chosen rarities. Every test exercises the production service; none recompute the
    /// begun-tier rule (anti-tautology bar). The <c>+1</c> rule, the authored-only high-water
    /// qualifier, and the snapshot contract are each mutation-testable.
    /// </summary>
    public sealed class CodexServiceRevealTests
    {
        private static MonsterDefinition Def(string id, Rarity rarity)
        {
            // Only Id + Rarity matter for the reveal model; the rest are placeholders.
            var d = ScriptableObject.CreateInstance<MonsterDefinition>();
            d.SetForTests(id, "x", rarity, 0, 0, "x", null);
            return d;
        }

        private static MonsterDatabase Db(params MonsterDefinition[] defs)
        {
            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            db.SetForTests(defs);
            return db;
        }

        // A real SaveService over the deep-cloning fake + the supplied in-memory DB + a real
        // CodexService (mirrors CodexServiceTests.CreateInitialized, parameterized by the DB).
        private static (FakeCodexProgressStore store, SaveService save, CodexService codex)
            Create(MonsterDatabase db)
        {
            var store = new FakeCodexProgressStore { Stored = new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var codex = new CodexService(save, db, new FakeClock(
                new System.DateTime(2026, 6, 14, 0, 0, 0, System.DateTimeKind.Utc)));
            return (store, save, codex);
        }

        private static void Discover(CodexService codex, string id, DiscoverySource via = DiscoverySource.Capture)
        {
            codex.RecordDiscoveryAsync(id, via).GetAwaiter().GetResult();
        }

        // ---- HighestDiscoveredRarity ----

        [Test]
        public void HighestDiscoveredRarity_is_null_when_no_authored_id_discovered()
        {
            // DB populated with only mon01; fresh model → null.
            var (_, _, codex) = Create(Db(Def("mon01", Rarity.Common)));
            Assert.That(codex.HighestDiscoveredRarity(), Is.Null, "fresh model");

            // Discover ONLY an unauthored reserved id (mon40 not in the DB). It has no Rarity to
            // contribute, so the high-water is still null — pins the "authored only" qualifier.
            Discover(codex, "mon40");
            Assert.That(codex.HighestDiscoveredRarity(), Is.Null,
                "an unauthored discovery contributes no tier");
        }

        [Test]
        public void HighestDiscoveredRarity_tracks_the_max_authored_rarity()
        {
            var db = Db(
                Def("mon01", Rarity.Common),
                Def("mon02", Rarity.Rare),
                Def("mon03", Rarity.Nightmare));
            var (_, _, codex) = Create(db);

            Discover(codex, "mon01");
            Assert.That(codex.HighestDiscoveredRarity(), Is.EqualTo(Rarity.Common));

            Discover(codex, "mon02");
            Assert.That(codex.HighestDiscoveredRarity(), Is.EqualTo(Rarity.Rare));

            Discover(codex, "mon03");
            Assert.That(codex.HighestDiscoveredRarity(), Is.EqualTo(Rarity.Nightmare));

            // Re-discovering a lower tier never lowers the high-water.
            Discover(codex, "mon01");
            Assert.That(codex.HighestDiscoveredRarity(), Is.EqualTo(Rarity.Nightmare));
        }

        // ---- IsTierBegun ----

        [Test]
        public void IsTierBegun_brand_new_player_only_Common_is_begun()
        {
            // 0 discovered → floor = -1 → begun ⇔ tier <= 0 → only Common.
            var (_, _, codex) = Create(Db(Def("mon01", Rarity.Common)));

            Assert.That(codex.IsTierBegun(Rarity.Common), Is.True);
            Assert.That(codex.IsTierBegun(Rarity.Uncommon), Is.False);
            Assert.That(codex.IsTierBegun(Rarity.Rare), Is.False);
            Assert.That(codex.IsTierBegun(Rarity.Epic), Is.False);
            Assert.That(codex.IsTierBegun(Rarity.Nightmare), Is.False);
        }

        [Test]
        public void IsTierBegun_one_tier_ahead_of_high_water()
        {
            // Discover an authored Rare → high-water Rare → begun = Common..Epic (Epic = Rare+1),
            // Nightmare not begun. Mutation-test: break the +1 and Epic flips false.
            var db = Db(Def("mon02", Rarity.Rare));
            var (_, _, codex) = Create(db);
            Discover(codex, "mon02");

            Assert.That(codex.IsTierBegun(Rarity.Common), Is.True);
            Assert.That(codex.IsTierBegun(Rarity.Uncommon), Is.True);
            Assert.That(codex.IsTierBegun(Rarity.Rare), Is.True);
            Assert.That(codex.IsTierBegun(Rarity.Epic), Is.True, "Epic = Rare+1 is begun (the one-ahead rule)");
            Assert.That(codex.IsTierBegun(Rarity.Nightmare), Is.False, "Nightmare = Rare+2 is unreached");
        }

        [Test]
        public void IsTierBegun_all_true_once_Nightmare_discovered()
        {
            // High-water Nightmare → floor+1 = 5 exceeds the enum → every real tier (0..4) begun.
            // Pins the "no special-case at the top needed" claim.
            var db = Db(Def("mon03", Rarity.Nightmare));
            var (_, _, codex) = Create(db);
            Discover(codex, "mon03");

            foreach (Rarity tier in System.Enum.GetValues(typeof(Rarity)).Cast<Rarity>())
            {
                Assert.That(codex.IsTierBegun(tier), Is.True, $"{tier} should be begun once Nightmare is discovered");
            }
        }

        // ---- GetTier ----

        [Test]
        public void GetTier_returns_rarity_for_authored_null_for_reserved()
        {
            var db = Db(Def("mon02", Rarity.Rare));
            var (_, _, codex) = Create(db);

            Assert.That(codex.GetTier("mon02"), Is.EqualTo(Rarity.Rare));
            Assert.That(codex.GetTier("mon40"), Is.Null, "valid reserved-but-unauthored id → null");
        }

        // ---- GetDiscoveredIds (settled 2.3 deferral) ----

        [Test]
        public void GetDiscoveredIds_is_a_snapshot_method()
        {
            var db = Db(Def("mon01", Rarity.Common), Def("mon02", Rarity.Rare));
            var (_, _, codex) = Create(db);
            Discover(codex, "mon01");
            Discover(codex, "mon02");

            var first = codex.GetDiscoveredIds();
            Assert.That(first, Is.EquivalentTo(new[] { "mon01", "mon02" }), "contains exactly the discovered ids");

            // A fresh instance per call — the method shape's contract (single allocation per call,
            // never the live Dictionary.Keys). Distinct reference each time.
            var second = codex.GetDiscoveredIds();
            Assert.That(second, Is.Not.SameAs(first), "each call returns a fresh snapshot");

            // Discovering more is reflected by a subsequent call, but the earlier snapshot is
            // frozen (it never aliased internal state).
            Discover(codex, "mon03");
            Assert.That(first.Count, Is.EqualTo(2), "the earlier snapshot is unaffected by later discoveries");
            Assert.That(codex.GetDiscoveredIds().Count, Is.EqualTo(3));
        }
    }
}
