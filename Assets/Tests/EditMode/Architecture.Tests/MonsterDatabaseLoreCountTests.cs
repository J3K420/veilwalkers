using NUnit.Framework;
using Veilwalkers.Monsters;

namespace Veilwalkers.Architecture.Tests
{
    /// <summary>
    /// AR-20 lore constraint: the Monster universe is exactly 67. This architecture-level
    /// guard asserts the universe constant and the in-universe ID range logic — the
    /// durable, public contract — using ONLY <see cref="MonsterDatabase"/>'s public surface
    /// (it does not reach the internal <c>SetForTests</c> seam, which belongs to
    /// <c>Monsters.Tests</c>). The populated-subset model relationships (PopulatedCount in
    /// [3..5], every populated ID in-universe) and content validation are covered in
    /// <c>Veilwalkers.Monsters.Tests.MonsterDatabaseTests</c>, which has internals access.
    /// The on-disk real-asset audit is deferred with Task 3's asset authoring — see the
    /// story's Review Findings.
    /// </summary>
    public sealed class MonsterDatabaseLoreCountTests
    {
        [Test]
        public void Universe_count_is_67()
        {
            Assert.That(MonsterDatabase.UniverseCount, Is.EqualTo(67));
        }

        [Test]
        public void IsValidMonsterId_accepts_only_in_universe_ids()
        {
            // Boundary cases pin the universe range [mon01..mon67] — non-tautological:
            // these exercise IsValidMonsterId's parsing + bounds, not a literal constant.
            Assert.That(MonsterDatabase.IsValidMonsterId("mon01"), Is.True);
            Assert.That(MonsterDatabase.IsValidMonsterId("mon67"), Is.True);
            Assert.That(MonsterDatabase.IsValidMonsterId("mon00"), Is.False); // below 1
            Assert.That(MonsterDatabase.IsValidMonsterId("mon68"), Is.False); // above 67 (universe edge)
            Assert.That(MonsterDatabase.IsValidMonsterId("mon99"), Is.False);
            Assert.That(MonsterDatabase.IsValidMonsterId("monAB"), Is.False); // non-numeric
            Assert.That(MonsterDatabase.IsValidMonsterId("mon1"), Is.False);  // wrong width
            Assert.That(MonsterDatabase.IsValidMonsterId(null), Is.False);
            // Reject whitespace/sign-padded forms that int.TryParse would otherwise accept.
            Assert.That(MonsterDatabase.IsValidMonsterId("mon 1"), Is.False); // leading space
            Assert.That(MonsterDatabase.IsValidMonsterId("mon1 "), Is.False); // trailing space
            Assert.That(MonsterDatabase.IsValidMonsterId("mon+1"), Is.False); // leading sign
            Assert.That(MonsterDatabase.IsValidMonsterId("mon-1"), Is.False); // negative sign
        }
    }
}
