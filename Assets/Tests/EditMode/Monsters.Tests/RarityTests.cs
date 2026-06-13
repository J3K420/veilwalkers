using NUnit.Framework;

namespace Veilwalkers.Monsters.Tests
{
    /// <summary>
    /// <see cref="Rarity"/> as the ordered tier model (Story 2.1, AC-2): the members
    /// ascend Common &lt; Uncommon &lt; Rare &lt; Epic &lt; Nightmare, the explicit
    /// underlying values are stable (a reorder must fail loudly — serialized-asset
    /// contract), and the named Guaranteed-Rare floor
    /// (<see cref="RarityThresholds.GuaranteedRareFloor"/>) is INCLUSIVE — the
    /// FR-13-critical <c>&gt;=</c> behavior Story 5.3 will consume.
    /// </summary>
    public sealed class RarityTests
    {
        [Test]
        public void Rarity_is_ordered_ascending()
        {
            var values = new[]
            {
                (int)Rarity.Common,
                (int)Rarity.Uncommon,
                (int)Rarity.Rare,
                (int)Rarity.Epic,
                (int)Rarity.Nightmare,
            };

            Assert.That(values, Is.Ordered.Ascending,
                "Rarity members must ascend Common < Uncommon < Rare < Epic < Nightmare.");
        }

        [Test]
        public void Rarity_explicit_values_are_stable()
        {
            // These explicit ints are a contract: they are serialized into authored
            // Monster .asset files and back the >= floor. A reorder/renumber must break here.
            Assert.That((int)Rarity.Common, Is.EqualTo(0));
            Assert.That((int)Rarity.Uncommon, Is.EqualTo(1));
            Assert.That((int)Rarity.Rare, Is.EqualTo(2));
            Assert.That((int)Rarity.Epic, Is.EqualTo(3));
            Assert.That((int)Rarity.Nightmare, Is.EqualTo(4));
        }

        [Test]
        public void GuaranteedRareFloor_is_Rare()
        {
            Assert.That(RarityThresholds.GuaranteedRareFloor, Is.EqualTo(Rarity.Rare));
        }

        [Test]
        public void Rarity_rare_floor_is_inclusive()
        {
            // The on-the-floor case is the single most important value: Rare itself qualifies.
            Assert.That(Rarity.Rare >= RarityThresholds.GuaranteedRareFloor, Is.True,
                "The Guaranteed-Rare floor is inclusive: Rare itself qualifies.");

            // Above the floor qualifies.
            Assert.That(Rarity.Epic >= RarityThresholds.GuaranteedRareFloor, Is.True);
            Assert.That(Rarity.Nightmare >= RarityThresholds.GuaranteedRareFloor, Is.True);

            // Below the floor does not.
            Assert.That(Rarity.Uncommon >= RarityThresholds.GuaranteedRareFloor, Is.False);
            Assert.That(Rarity.Common >= RarityThresholds.GuaranteedRareFloor, Is.False);
        }
    }
}
