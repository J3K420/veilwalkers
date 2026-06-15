using System;
using NUnit.Framework;
using UnityEngine;
using Veilwalkers.Economy;
using Veilwalkers.Monsters;

namespace Veilwalkers.Encounter.Tests
{
    /// <summary>
    /// <see cref="LureSystem"/> — the Story 4.2 Lure DECISION (cost + rarity roll), pure logic over a seeded
    /// <see cref="MonsterDatabase"/> and a scripted <see cref="FakeRandom"/>. Pins AC-1's data-driven cost
    /// lookup and AC-2's STRICT Premium &gt; Basic rare-tier inequality. Drives the production
    /// <see cref="LureSystem"/>; never recomputes a literal (the anti-tautology bar).
    /// </summary>
    public sealed class LureSystemTests
    {
        // A roster with BOTH sides of the rare floor: mon01 Common (below Rare), mon02 Rare (>= floor),
        // mon03 Epic (>= floor). So a rare-gate pass picks from {mon02, mon03}; a fail picks {mon01}.
        private static MonsterDatabase SeededDb()
        {
            MonsterDefinition common = Def("mon01", Rarity.Common);
            MonsterDefinition rare = Def("mon02", Rarity.Rare);
            MonsterDefinition epic = Def("mon03", Rarity.Epic);

            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            db.SetForTests(new[] { common, rare, epic });
            return db;
        }

        private static MonsterDefinition Def(string id, Rarity rarity)
        {
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            // Art may be null here — LureSystem only reads Id + Rarity (it never renders).
            def.SetForTests(id, id, rarity, 0, 0, "lore", null);
            return def;
        }

        private static EconomyConfig DefaultConfig()
        {
            // A fresh instance runs the [SerializeField] initializers — Basic 1 / Premium 4 / Multi 5 (canon).
            return ScriptableObject.CreateInstance<EconomyConfig>();
        }

        // ---- AC-1: data-driven cost lookup ----

        [Test]
        public void CostOf_reads_the_canon_costs_from_EconomyConfig()
        {
            var sut = new LureSystem(DefaultConfig(), SeededDb(), new FakeRandom());

            Assert.AreEqual(1, sut.CostOf(LureKind.Basic), "Basic Lure cost (canon 1).");
            Assert.AreEqual(4, sut.CostOf(LureKind.Premium), "Premium Lure cost (canon 4).");
            Assert.AreEqual(5, sut.CostOf(LureKind.Multi), "Multi-Lure cost (canon 5).");
        }

        [Test]
        public void MonsterCountOf_is_two_for_Multi_one_otherwise()
        {
            var sut = new LureSystem(DefaultConfig(), SeededDb(), new FakeRandom());

            Assert.AreEqual(1, sut.MonsterCountOf(LureKind.Basic));
            Assert.AreEqual(1, sut.MonsterCountOf(LureKind.Premium));
            Assert.AreEqual(2, sut.MonsterCountOf(LureKind.Multi));
        }

        // ---- AC-2: Premium rare chance STRICTLY higher than Basic ----

        [Test]
        public void Premium_rare_chance_is_strictly_higher_than_Basic()
        {
            // The HARD AC-2 invariant — independent of the (provisional OQ-9) exact values.
            Assert.Greater(
                LureSystem.PremiumRareChance, LureSystem.BasicRareChance,
                "AC-2: Premium's rare-tier probability must be STRICTLY greater than Basic's.");
        }

        [Test]
        public void At_the_boundary_roll_Basic_yields_common_but_Premium_yields_rare()
        {
            // A draw that sits ABOVE Basic's rare chance but BELOW Premium's: same roll input, different tier.
            // boundary ∈ (BasicRareChance, PremiumRareChance) exists BECAUSE Premium > Basic — if they were
            // equal there would be no such value and this test could not pass (the mutation-test on AC-2).
            double boundary = (LureSystem.BasicRareChance + LureSystem.PremiumRareChance) / 2.0;

            // Basic: NextDouble = boundary → NOT < BasicRareChance → wantRare = false → picks from {mon01}.
            var basicRng = new FakeRandom().EnqueueDouble(boundary).EnqueueNext(0);
            var basicSut = new LureSystem(DefaultConfig(), SeededDb(), basicRng);
            string basicPick = basicSut.RollMonster(LureKind.Basic);
            Assert.AreEqual("mon01", basicPick, "Basic at the boundary roll yields the below-Rare monster.");

            // Premium: same NextDouble = boundary → IS < PremiumRareChance → wantRare = true → picks from
            // {mon02, mon03}; Next(0) selects the first (mon02).
            var premiumRng = new FakeRandom().EnqueueDouble(boundary).EnqueueNext(0);
            var premiumSut = new LureSystem(DefaultConfig(), SeededDb(), premiumRng);
            string premiumPick = premiumSut.RollMonster(LureKind.Premium);
            Assert.AreEqual("mon02", premiumPick, "Premium at the SAME boundary roll yields a Rare-or-better monster.");
        }

        // ---- AC-2: uniform pick from the matching subset + the MVP-roster fallback ----

        [Test]
        public void Rare_roll_picks_uniformly_from_the_rare_or_better_subset()
        {
            // wantRare = true (draw 0.0 < any chance) → pool {mon02 Rare, mon03 Epic}; Next(1) → second = mon03.
            var rng = new FakeRandom().EnqueueDouble(0.0).EnqueueNext(1);
            var sut = new LureSystem(DefaultConfig(), SeededDb(), rng);

            Assert.AreEqual("mon03", sut.RollMonster(LureKind.Premium));
        }

        [Test]
        public void Falls_back_to_the_whole_populated_set_when_the_rolled_side_is_empty()
        {
            // A roster of ONLY rare+ monsters. A common roll (wantRare = false) finds an EMPTY below-Rare
            // subset → falls back to the whole populated set (never null, never throws). MVP 3–5 roster reality.
            var rareOnly = ScriptableObject.CreateInstance<MonsterDatabase>();
            rareOnly.SetForTests(new[] { Def("mon02", Rarity.Rare), Def("mon03", Rarity.Epic) });

            // NextDouble = 0.99 → NOT < BasicRareChance → wantRare = false → below-Rare subset empty → fallback.
            var rng = new FakeRandom().EnqueueDouble(0.99).EnqueueNext(0);
            var sut = new LureSystem(DefaultConfig(), rareOnly, rng);

            Assert.AreEqual("mon02", sut.RollMonster(LureKind.Basic), "Fallback picks from the whole set, not null.");
        }

        // ---- AC-3: Multi-Lure rolls each monster independently (two draws) ----

        [Test]
        public void Multi_rolls_two_monsters_with_two_independent_draws()
        {
            // First monster: rare roll (0.0) + Next(0) → mon02. Second: common roll (0.99) + Next(0) → mon01.
            // Distinct outcomes prove two INDEPENDENT draws (a single shared draw could not yield both tiers).
            var rng = new FakeRandom()
                .EnqueueDouble(0.0, 0.99)
                .EnqueueNext(0, 0);
            var sut = new LureSystem(DefaultConfig(), SeededDb(), rng);

            (string first, string second) = sut.RollMultiMonsters();

            Assert.AreEqual("mon02", first, "First Multi monster from the rare-gate draw.");
            Assert.AreEqual("mon01", second, "Second Multi monster from a SEPARATE common-gate draw.");
        }

        // ---- ctor guards ----

        [Test]
        public void Ctor_null_args_throw()
        {
            var db = SeededDb();
            var cfg = DefaultConfig();
            var rng = new FakeRandom();

            Assert.Throws<ArgumentNullException>(() => new LureSystem(null, db, rng));
            Assert.Throws<ArgumentNullException>(() => new LureSystem(cfg, null, rng));
            Assert.Throws<ArgumentNullException>(() => new LureSystem(cfg, db, null));
        }

        [Test]
        public void RollMonster_throws_on_an_empty_database()
        {
            var empty = ScriptableObject.CreateInstance<MonsterDatabase>();
            empty.SetForTests(Array.Empty<MonsterDefinition>());
            var sut = new LureSystem(DefaultConfig(), empty, new FakeRandom().EnqueueDouble(0.0));

            Assert.Throws<InvalidOperationException>(() => sut.RollMonster(LureKind.Basic));
        }

        // ---- Story 5.3: GuaranteedRare — zero cost, forced Rare-or-better, no common fallback ----

        [Test]
        public void GuaranteedRare_costs_zero_and_spawns_one()
        {
            var sut = new LureSystem(DefaultConfig(), SeededDb(), new FakeRandom());

            Assert.AreEqual(0, sut.CostOf(LureKind.GuaranteedRare),
                "AC-2: the Guaranteed-Rare Lure costs ZERO Credits (the Veil pack already paid).");
            Assert.AreEqual(1, sut.MonsterCountOf(LureKind.GuaranteedRare), "GuaranteedRare spawns one Monster.");
        }

        [Test]
        public void TryRollGuaranteedRare_only_ever_returns_a_rare_or_better_monster()
        {
            // Index 0 → mon02 (Rare), index 1 → mon03 (Epic) — both >= the floor. The below-Rare mon01 is NOT
            // in the rare-or-better subset, so it can NEVER be picked (no common fallback). Run both indices.
            MonsterDatabase db = SeededDb();

            var rng0 = new FakeRandom().EnqueueNext(0);
            var sut0 = new LureSystem(DefaultConfig(), db, rng0);
            Assert.IsTrue(sut0.TryRollGuaranteedRare(out string id0));
            Assert.AreEqual("mon02", id0, "Index 0 of the rare-or-better subset is mon02 (Rare).");

            var rng1 = new FakeRandom().EnqueueNext(1);
            var sut1 = new LureSystem(DefaultConfig(), db, rng1);
            Assert.IsTrue(sut1.TryRollGuaranteedRare(out string id1));
            Assert.AreEqual("mon03", id1, "Index 1 of the rare-or-better subset is mon03 (Epic) — never the Common mon01.");
        }

        [Test]
        public void TryRollGuaranteedRare_never_returns_a_common_even_under_many_draws()
        {
            // Exhaustively confirm the guarantee: across every index the FakeRandom can hand back, the result is
            // always Rarity >= Rare (never the Common mon01). A mutation that included the below-Rare side would
            // surface mon01 here.
            MonsterDatabase db = SeededDb();
            for (int i = 0; i < 12; i++)
            {
                var sut = new LureSystem(DefaultConfig(), db, new FakeRandom().EnqueueNext(i));
                Assert.IsTrue(sut.TryRollGuaranteedRare(out string id), "A mixed roster always honors the guarantee.");
                Assert.AreNotEqual("mon01", id, "AC-2: a Guaranteed-Rare Lure NEVER spawns the below-Rare Common.");
            }
        }

        [Test]
        public void TryRollGuaranteedRare_reports_failure_on_a_common_only_roster_without_throwing()
        {
            // A content error: no Rare+ Monster. The guarantee cannot be honored → a TYPED false (never a throw,
            // never a silent Common spawn). The caller keeps the player's lure.
            var commonOnly = ScriptableObject.CreateInstance<MonsterDatabase>();
            commonOnly.SetForTests(new[] { Def("mon01", Rarity.Common), Def("mon09", Rarity.Uncommon) });
            var sut = new LureSystem(DefaultConfig(), commonOnly, new FakeRandom().EnqueueNext(0));

            Assert.IsFalse(sut.TryRollGuaranteedRare(out string id),
                "AC-2: no Rare+ in the roster → a typed failure, NOT a silent Common spawn.");
            Assert.IsNull(id);
        }

        [Test]
        public void TryRollGuaranteedRare_reports_failure_on_an_empty_database()
        {
            var empty = ScriptableObject.CreateInstance<MonsterDatabase>();
            empty.SetForTests(Array.Empty<MonsterDefinition>());
            var sut = new LureSystem(DefaultConfig(), empty, new FakeRandom());

            Assert.IsFalse(sut.TryRollGuaranteedRare(out string id), "Nothing to Lure → typed false, never a throw.");
            Assert.IsNull(id);
        }
    }
}
