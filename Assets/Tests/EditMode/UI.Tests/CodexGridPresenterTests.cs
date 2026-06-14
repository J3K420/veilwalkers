using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Veilwalkers.Monsters;
using Veilwalkers.Persistence;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// <see cref="CodexGridPresenter"/> as the pure-logic 67-slot reveal read model (Story 2.4):
    /// the three slot states (Discovered / <c>???</c> Silhouette / <c>?</c> Blank), the
    /// Dread-Ladder begun-tier ramp (AC-2), discovered-always-wins (AC-3), the reserved-slot
    /// rule, and the X/67 passthrough. Every test exercises the production presenter over a REAL
    /// <see cref="CodexService"/> (a real <see cref="SaveService"/> over a deep-cloning
    /// <see cref="FakeUiCodexProgressStore"/> + an in-memory <see cref="MonsterDatabase"/>); none
    /// recompute the begun-tier rule (the 2.1/2.2/2.3 anti-tautology bar). The <c>+1</c> rule,
    /// the authored-only high-water qualifier, and the reserved-slot-blank rule are each
    /// mutation-testable: break one and a named test goes red.
    /// </summary>
    public sealed class CodexGridPresenterTests
    {
        // ---- construction helpers ----

        private static MonsterDefinition Def(string id, Rarity rarity)
        {
            // The presenter reads only Id + Rarity (via GetTier); displayName/lore/difficulties/
            // art are irrelevant here — pass placeholders and null art (never read).
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

        // Mirrors CodexServiceTests.CreateInitialized, extended to also new-up the presenter:
        // a real SaveService over the deep-cloning fake + an in-memory DB + a real CodexService.
        private static (CodexService codex, CodexGridPresenter presenter) Create(MonsterDatabase db)
        {
            var store = new FakeUiCodexProgressStore { Stored = new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var codex = new CodexService(save, db, new FakeClock(
                new System.DateTime(2026, 6, 14, 0, 0, 0, System.DateTimeKind.Utc)));
            return (codex, new CodexGridPresenter(codex));
        }

        private static void Discover(CodexService codex, string id, DiscoverySource via = DiscoverySource.Capture)
        {
            codex.RecordDiscoveryAsync(id, via).GetAwaiter().GetResult();
        }

        private static CodexSlot Slot(System.Collections.Generic.IReadOnlyList<CodexSlot> slots, string id)
        {
            return slots.First(s => s.Id == id);
        }

        // ---- ctor ----

        [Test]
        public void Ctor_null_codex_throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new CodexGridPresenter(null));
        }

        // ---- AC-1: 67 slots, stable mon01..mon67 ordering ----

        [Test]
        public void BuildSlots_returns_exactly_67_slots_ordered_mon01_to_mon67()
        {
            var (_, presenter) = Create(Db(Def("mon01", Rarity.Common)));

            var slots = presenter.BuildSlots();

            Assert.That(slots.Count, Is.EqualTo(67));
            Assert.That(slots[0].Id, Is.EqualTo("mon01"));
            Assert.That(slots[66].Id, Is.EqualTo("mon67"));
            // Index is 1-based and ascending in lockstep with position.
            for (int i = 0; i < slots.Count; i++)
            {
                Assert.That(slots[i].Index, Is.EqualTo(i + 1), $"slot[{i}].Index");
                Assert.That(slots[i].Id, Is.EqualTo("mon" + (i + 1).ToString("00")), $"slot[{i}].Id");
            }
        }

        // ---- AC-1 + the new-player ramp ----

        [Test]
        public void Fresh_player_authored_Common_slots_are_silhouette_rest_blank()
        {
            // mon01 authored Common; nothing discovered. Common is begun for a brand-new player
            // (floor = -1, begun ⇔ tier <= 0), so mon01 is a ??? silhouette; everything else is
            // either unauthored (blank) or a higher authored tier (none here) → blank.
            var (_, presenter) = Create(Db(Def("mon01", Rarity.Common)));

            var slots = presenter.BuildSlots();

            Assert.That(Slot(slots, "mon01").State, Is.EqualTo(CodexSlotState.Silhouette));
            // Every other slot is unauthored → Blank.
            foreach (var s in slots.Where(s => s.Id != "mon01"))
            {
                Assert.That(s.State, Is.EqualTo(CodexSlotState.Blank), $"{s.Id} should be Blank");
                Assert.That(s.Tier, Is.Null, $"{s.Id} unauthored Tier should be null");
            }
        }

        // ---- AC-3: discovered always wins, regardless of tier ----

        [Test]
        public void Discovered_slot_is_full_art_regardless_of_tier()
        {
            var db = Db(Def("mon01", Rarity.Common), Def("mon03", Rarity.Nightmare));
            var (codex, presenter) = Create(db);

            Discover(codex, "mon01");
            Discover(codex, "mon03"); // a Nightmare — still Discovered, not gated by tier

            var slots = presenter.BuildSlots();
            Assert.That(Slot(slots, "mon01").State, Is.EqualTo(CodexSlotState.Discovered));
            Assert.That(Slot(slots, "mon03").State, Is.EqualTo(CodexSlotState.Discovered));
        }

        [Test]
        public void Discovered_unauthored_id_is_still_Discovered()
        {
            // DB authored with only mon01; discover the valid reserved id mon40 (no definition).
            // Discovery wins even with no MonsterDefinition; its Tier is null (view falls back).
            var (codex, presenter) = Create(Db(Def("mon01", Rarity.Common)));

            Discover(codex, "mon40");

            var slots = presenter.BuildSlots();
            var mon40 = Slot(slots, "mon40");
            Assert.That(mon40.State, Is.EqualTo(CodexSlotState.Discovered));
            Assert.That(mon40.Tier, Is.Null);
        }

        // ---- AC-2 headline: climbing a tier upgrades lower blanks to silhouette ----

        [Test]
        public void Climbing_a_tier_upgrades_lower_blanks_to_silhouette()
        {
            // mon01 Common, mon02 Rare, mon05 Epic. New player (0 discovered):
            //   Common begun → mon01 Silhouette; Rare/Epic not begun → mon02, mon05 Blank.
            var db = Db(
                Def("mon01", Rarity.Common),
                Def("mon02", Rarity.Rare),
                Def("mon05", Rarity.Epic));
            var (codex, presenter) = Create(db);

            var before = presenter.BuildSlots();
            Assert.That(Slot(before, "mon01").State, Is.EqualTo(CodexSlotState.Silhouette));
            Assert.That(Slot(before, "mon02").State, Is.EqualTo(CodexSlotState.Blank), "Rare not begun for a new player");
            Assert.That(Slot(before, "mon05").State, Is.EqualTo(CodexSlotState.Blank), "Epic not begun for a new player");

            // Discover the authored Rare → high-water = Rare → Epic (Rare+1) becomes begun.
            Discover(codex, "mon02");

            var after = presenter.BuildSlots();
            Assert.That(Slot(after, "mon02").State, Is.EqualTo(CodexSlotState.Discovered));
            Assert.That(Slot(after, "mon05").State, Is.EqualTo(CodexSlotState.Silhouette),
                "Epic = Rare+1 is now begun → previously-blank Epic slot upgrades to ???");
            // mon01 (Common) stays a silhouette (still undiscovered, still begun).
            Assert.That(Slot(after, "mon01").State, Is.EqualTo(CodexSlotState.Silhouette));
        }

        [Test]
        public void Higher_unreached_tier_stays_blank()
        {
            // High-water Common (discover an authored Common); an authored Epic/Nightmare is
            // two+ tiers ahead → not begun → stays Blank (AC-2 "higher unreached tiers remain ?").
            var db = Db(
                Def("mon01", Rarity.Common),
                Def("mon05", Rarity.Epic),
                Def("mon06", Rarity.Nightmare));
            var (codex, presenter) = Create(db);

            Discover(codex, "mon01"); // high-water = Common; Common+1 = Uncommon begun, no more

            var slots = presenter.BuildSlots();
            Assert.That(Slot(slots, "mon05").State, Is.EqualTo(CodexSlotState.Blank), "Epic stays blank at high-water Common");
            Assert.That(Slot(slots, "mon06").State, Is.EqualTo(CodexSlotState.Blank), "Nightmare stays blank at high-water Common");
        }

        // ---- AC-3: the count tick (presenter passes the numbers through) ----

        [Test]
        public void Count_passthrough_reflects_discoveries()
        {
            var db = Db(Def("mon01", Rarity.Common), Def("mon02", Rarity.Rare));
            var (codex, presenter) = Create(db);

            Assert.That(presenter.DiscoveredCount, Is.EqualTo(0));
            Assert.That(presenter.UniverseCount, Is.EqualTo(67));

            Discover(codex, "mon01");
            Assert.That(presenter.DiscoveredCount, Is.EqualTo(1));

            Discover(codex, "mon02");
            Assert.That(presenter.DiscoveredCount, Is.EqualTo(2));

            // UniverseCount is constant regardless of discoveries or authored count.
            Assert.That(presenter.UniverseCount, Is.EqualTo(67));
            Assert.That(presenter.ProgressLabel, Is.EqualTo("2 / 67"));
        }
    }
}
