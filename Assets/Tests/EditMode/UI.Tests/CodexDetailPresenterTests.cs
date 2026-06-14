using System;
using NUnit.Framework;
using UnityEngine;
using Veilwalkers.Monsters;
using Veilwalkers.Persistence;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// <see cref="CodexDetailPresenter"/> as the pure-logic Codex detail read model (Story 2.5):
    /// AC-1 (a discovered slot → name/tier/stats/lore/first-discovered date), AC-2 (an
    /// undiscovered slot — both <c>?</c> blank and <c>???</c> silhouette — → ONLY the "Not yet
    /// discovered." copy, NO content leak), the discovered-but-unauthored id case, and the
    /// null-safety that SETTLES the 2.1/2.2 <c>DisplayName</c>/<c>Lore</c>/<c>Art</c> deferrals
    /// at the render layer (a null name/lore must placeholder, never throw). Every test exercises
    /// the production presenter over a REAL <see cref="CodexService"/> (a real
    /// <see cref="SaveService"/> over the deep-cloning <see cref="FakeUiCodexProgressStore"/> + an
    /// in-memory <see cref="MonsterDatabase"/>). The AC-2 anti-leak assertion and the date string
    /// are mutation-testable (leak content / change the stamp → a named test goes red).
    /// </summary>
    public sealed class CodexDetailPresenterTests
    {
        private static readonly DateTime FixedNow =
            new DateTime(2026, 6, 14, 9, 30, 0, DateTimeKind.Utc); // → "2026-06-14"

        // A definition builder that allows NULL name/lore/art (unlike the grid tests' Def, which
        // always passes placeholders) — so the null-safety tests can author the exact null inputs
        // the 2.1/2.2 deferrals warned about.
        private static MonsterDefinition Def(
            string id, Rarity rarity, string name, string lore,
            int capture = 0, int slay = 0, Sprite art = null)
        {
            var d = ScriptableObject.CreateInstance<MonsterDefinition>();
            d.SetForTests(id, name, rarity, capture, slay, lore, art);
            return d;
        }

        private static MonsterDatabase Db(params MonsterDefinition[] defs)
        {
            var db = ScriptableObject.CreateInstance<MonsterDatabase>();
            db.SetForTests(defs);
            return db;
        }

        private static (CodexService codex, CodexDetailPresenter presenter) Create(MonsterDatabase db)
        {
            var store = new FakeUiCodexProgressStore { Stored = new SaveModel() };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var codex = new CodexService(save, db, new FakeClock(FixedNow));
            return (codex, new CodexDetailPresenter(codex));
        }

        private static void Discover(CodexService codex, string id, DiscoverySource via = DiscoverySource.Capture)
        {
            codex.RecordDiscoveryAsync(id, via).GetAwaiter().GetResult();
        }

        // ---- ctor ----

        [Test]
        public void Ctor_null_codex_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CodexDetailPresenter(null));
        }

        // ---- AC-1: a discovered, authored slot carries full content ----

        [Test]
        public void Discovered_authored_slot_carries_name_tier_stats_lore_and_date()
        {
            var (codex, presenter) = Create(Db(
                Def("mon02", Rarity.Rare, "Antlered Shade", "It watches from the treeline.", capture: 7, slay: 4)));
            Discover(codex, "mon02", DiscoverySource.Capture);

            var vm = presenter.BuildDetail("mon02");

            Assert.That(vm.IsDiscovered, Is.True);
            Assert.That(vm.Id, Is.EqualTo("mon02"));
            Assert.That(vm.DisplayName, Is.EqualTo("Antlered Shade"));
            Assert.That(vm.Tier, Is.EqualTo(Rarity.Rare));
            Assert.That(vm.CaptureDifficulty, Is.EqualTo(7));
            Assert.That(vm.SlayDifficulty, Is.EqualTo(4));
            Assert.That(vm.Lore, Is.EqualTo("It watches from the treeline."));
            Assert.That(vm.Discovered, Is.EqualTo("2026-06-14"));
        }

        // ---- AC-2: an undiscovered slot leaks NO content (both blank and silhouette) ----

        [Test]
        public void Undiscovered_silhouette_slot_shows_only_the_copy_no_content()
        {
            // mon01 authored Common → a ??? silhouette for a fresh player, but NOT discovered.
            var (_, presenter) = Create(Db(Def("mon01", Rarity.Common, "Hidden One", "secret lore")));

            var vm = presenter.BuildDetail("mon01");

            AssertNoContentLeak(vm);
        }

        [Test]
        public void Undiscovered_blank_slot_shows_only_the_copy_no_content()
        {
            // mon05 authored Nightmare → a ? blank for a fresh player (two+ tiers ahead), not discovered.
            var (_, presenter) = Create(Db(Def("mon05", Rarity.Nightmare, "Dread Thing", "scary lore")));

            var vm = presenter.BuildDetail("mon05");

            AssertNoContentLeak(vm);
        }

        [Test]
        public void Undiscovered_reserved_unauthored_slot_shows_only_the_copy()
        {
            var (_, presenter) = Create(Db(Def("mon01", Rarity.Common, "x", "x")));

            var vm = presenter.BuildDetail("mon40"); // valid reserved id, unauthored, undiscovered

            AssertNoContentLeak(vm);
        }

        private static void AssertNoContentLeak(CodexDetailViewModel vm)
        {
            Assert.That(vm.IsDiscovered, Is.False, "must be the not-discovered shape");
            // The AC-2 guarantee: every content field is null/default — no name, tier, stats,
            // lore, or date may leak for an undiscovered slot.
            Assert.That(vm.DisplayName, Is.Null, "name must not leak");
            Assert.That(vm.Tier, Is.Null, "tier must not leak");
            Assert.That(vm.CaptureDifficulty, Is.EqualTo(0), "capture stat must not leak");
            Assert.That(vm.SlayDifficulty, Is.EqualTo(0), "slay stat must not leak");
            Assert.That(vm.Lore, Is.Null, "lore must not leak");
            Assert.That(vm.Discovered, Is.Null, "date must not leak");
        }

        // ---- discovered-but-unauthored id: discovered shape, content-light, still has a date ----

        [Test]
        public void Discovered_unauthored_id_is_discovered_but_content_light_with_a_date()
        {
            var (codex, presenter) = Create(Db(Def("mon01", Rarity.Common, "x", "x")));
            Discover(codex, "mon40"); // valid reserved id, no definition

            var vm = presenter.BuildDetail("mon40");

            Assert.That(vm.IsDiscovered, Is.True, "discovery wins even without a definition");
            Assert.That(vm.Id, Is.EqualTo("mon40"));
            Assert.That(vm.Tier, Is.Null, "no definition → no tier");
            Assert.That(vm.DisplayName, Is.Null);
            Assert.That(vm.Lore, Is.Null);
            Assert.That(vm.Discovered, Is.EqualTo("2026-06-14"), "the date is recorded regardless of authoring");
        }

        // ---- null-safety: SETTLES the 2.1/2.2 DisplayName/Lore/Art deferrals ----

        [Test]
        public void Discovered_authored_slot_with_null_name_renders_placeholder_not_a_crash()
        {
            var (codex, presenter) = Create(Db(Def("mon01", Rarity.Common, name: null, lore: "lore")));
            Discover(codex, "mon01");

            CodexDetailViewModel vm = default;
            Assert.DoesNotThrow(() => vm = presenter.BuildDetail("mon01"),
                "a null authored name must never crash the detail panel (2.1 deferral, settled)");
            Assert.That(vm.DisplayName, Is.EqualTo(CodexDetailPresenter.UnnamedPlaceholder));
        }

        [Test]
        public void Discovered_authored_slot_with_null_lore_renders_empty_not_a_crash()
        {
            var (codex, presenter) = Create(Db(Def("mon01", Rarity.Common, name: "Named", lore: null)));
            Discover(codex, "mon01");

            CodexDetailViewModel vm = default;
            Assert.DoesNotThrow(() => vm = presenter.BuildDetail("mon01"),
                "a null authored lore must never crash the detail panel (2.1 deferral, settled)");
            Assert.That(vm.Lore, Is.EqualTo(string.Empty));
            Assert.That(vm.DisplayName, Is.EqualTo("Named"));
        }

        // ---- invalid id: a UI read degrades, it does NOT throw ----

        [Test]
        public void Invalid_id_returns_not_discovered_without_throwing()
        {
            var (_, presenter) = Create(Db(Def("mon01", Rarity.Common, "x", "x")));

            CodexDetailViewModel vm = default;
            Assert.DoesNotThrow(() => vm = presenter.BuildDetail("not-an-id"));
            Assert.That(vm.IsDiscovered, Is.False);
            Assert.DoesNotThrow(() => presenter.BuildDetail(null));
        }

        // ---- pre-2.5 discovered entry with a null date renders, does not crash ----

        [Test]
        public void Discovered_entry_with_null_date_renders_null_date_not_a_crash()
        {
            // Seed a discovered entry that predates the Discovered field (null date).
            var seed = new SaveModel();
            seed.Codex["mon01"] = new CodexEntryData { Captured = true, Discovered = null };
            var store = new FakeUiCodexProgressStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            var codex = new CodexService(save, Db(Def("mon01", Rarity.Common, "Named", "lore")), new FakeClock(FixedNow));
            var presenter = new CodexDetailPresenter(codex);

            var vm = presenter.BuildDetail("mon01");

            Assert.That(vm.IsDiscovered, Is.True);
            Assert.That(vm.DisplayName, Is.EqualTo("Named"));
            Assert.That(vm.Discovered, Is.Null, "a pre-2.5 entry's null date surfaces as null (view renders '—')");
        }
    }
}
