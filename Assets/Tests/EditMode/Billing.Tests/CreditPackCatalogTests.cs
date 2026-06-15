using System.Linq;
using NUnit.Framework;
using Veilwalkers.Billing;

namespace Veilwalkers.Billing.Tests
{
    /// <summary>
    /// AC-1 pins: the catalog lists at least the three launch packs with the canon base/bonus, the correct
    /// badges, the Veil-only Guaranteed-Rare flag, and TRANSPARENT contents (every grant is a declared
    /// field — no gacha). Mutation-testable: dropping a pack or flipping a bonus turns these red.
    /// </summary>
    public sealed class CreditPackCatalogTests
    {
        [Test]
        public void Catalog_lists_at_least_the_three_launch_packs()
        {
            var catalog = new CreditPackCatalog();

            Assert.That(catalog.Packs.Count, Is.GreaterThanOrEqualTo(3),
                "AC-1: the catalog must list at least the three launch packs.");

            // The three canon ids are present.
            var ids = catalog.Packs.Select(p => p.PackId).ToList();
            Assert.That(ids, Does.Contain(CreditPackCatalog.StarterPackId));
            Assert.That(ids, Does.Contain(CreditPackCatalog.HunterPackId));
            Assert.That(ids, Does.Contain(CreditPackCatalog.VeilPackId));
        }

        [Test]
        public void Starter_pack_is_50_credits_no_bonus_no_badge_no_guaranteed_rare()
        {
            var catalog = new CreditPackCatalog();
            Assert.IsTrue(catalog.TryGet(CreditPackCatalog.StarterPackId, out CreditPack pack));

            Assert.AreEqual(50, pack.BaseCredits);
            Assert.AreEqual(0, pack.BonusCredits);
            Assert.AreEqual(50, pack.TotalCredits);
            Assert.AreEqual(PackBadge.None, pack.Badge);
            Assert.IsFalse(pack.IncludesGuaranteedRareLure);
        }

        [Test]
        public void Hunter_pack_is_150_plus_20_bonus_popular_no_guaranteed_rare()
        {
            var catalog = new CreditPackCatalog();
            Assert.IsTrue(catalog.TryGet(CreditPackCatalog.HunterPackId, out CreditPack pack));

            Assert.AreEqual(150, pack.BaseCredits);
            Assert.AreEqual(20, pack.BonusCredits);
            Assert.AreEqual(170, pack.TotalCredits, "AC-3: the grant total is base + bonus.");
            Assert.AreEqual(PackBadge.Popular, pack.Badge);
            Assert.IsFalse(pack.IncludesGuaranteedRareLure);
        }

        [Test]
        public void Veil_pack_is_400_plus_100_bonus_best_value_AND_includes_the_guaranteed_rare_lure()
        {
            var catalog = new CreditPackCatalog();
            Assert.IsTrue(catalog.TryGet(CreditPackCatalog.VeilPackId, out CreditPack pack));

            Assert.AreEqual(400, pack.BaseCredits);
            Assert.AreEqual(100, pack.BonusCredits);
            Assert.AreEqual(500, pack.TotalCredits);
            Assert.AreEqual(PackBadge.BestValue, pack.Badge);
            Assert.IsTrue(pack.IncludesGuaranteedRareLure,
                "AC-1: the Veil pack transparently declares it includes the Guaranteed-Rare Lure (granted in 5.3).");
        }

        [Test]
        public void Only_the_veil_pack_includes_the_guaranteed_rare_lure()
        {
            var catalog = new CreditPackCatalog();

            int withGuaranteedRare = catalog.Packs.Count(p => p.IncludesGuaranteedRareLure);
            Assert.AreEqual(1, withGuaranteedRare, "Exactly one pack (Veil) declares the Guaranteed-Rare Lure.");
        }

        [Test]
        public void Every_pack_carries_a_positive_total_and_fallback_price_transparency()
        {
            // Transparency (AC-1): every grant is a declared, positive, non-randomized field. There is no
            // hidden/mystery reward field anywhere on CreditPack — its entire public surface is the declared
            // base/bonus/flag/badge/price. This test pins that every pack is fully specified.
            var catalog = new CreditPackCatalog();

            foreach (CreditPack pack in catalog.Packs)
            {
                Assert.That(pack.PackId, Is.Not.Empty, "Every pack has a stable Play product id.");
                Assert.That(pack.BaseCredits, Is.GreaterThan(0), "Every pack grants a positive base.");
                Assert.That(pack.BonusCredits, Is.GreaterThanOrEqualTo(0), "No negative bonus.");
                Assert.That(pack.TotalCredits, Is.EqualTo(pack.BaseCredits + pack.BonusCredits),
                    "Total is exactly base + bonus — no hidden adjustment.");
                Assert.That(pack.FallbackPriceUsd, Is.GreaterThan(0m), "Every pack has a positive fallback price.");
            }
        }

        [Test]
        public void TryGet_returns_false_for_an_unknown_id()
        {
            var catalog = new CreditPackCatalog();
            Assert.IsFalse(catalog.TryGet("credits_nonexistent", out _));
            Assert.IsFalse(catalog.TryGet(null, out _));
            Assert.IsFalse(catalog.TryGet(string.Empty, out _));
        }

        [Test]
        public void Default_CreditPack_honors_the_null_safe_contract()
        {
            // default(CreditPack) bypasses the factory; the null-safe getters must still hold (the struct-
            // invariant precedent), so a defaulted pack never NREs a consumer.
            CreditPack defaulted = default;
            Assert.AreEqual(string.Empty, defaulted.PackId);
            Assert.AreEqual(0, defaulted.TotalCredits);
        }
    }
}
