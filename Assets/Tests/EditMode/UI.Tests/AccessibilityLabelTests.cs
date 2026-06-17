using NUnit.Framework;
using Veilwalkers.Monsters;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// Story 6.6 — the TalkBack role+state label model (AC-4; UX-DR17): every interactive element gets a
    /// non-empty role+label, the role is correct per component, the state reflects the descriptor, and —
    /// the load-bearing pin — an UNDISCOVERED Codex slot NEVER leaks the Monster's name (the 2.4/2.5
    /// reveal firewall, D8). Plus the NFR-3 graceful + enum-order pins.
    /// </summary>
    public sealed class AccessibilityLabelTests
    {
        // ---- AccessibilityRole enum: members + order (append-only) ----

        [Test]
        public void Accessibility_role_members_and_order_are_pinned()
        {
            var values = (AccessibilityRole[])System.Enum.GetValues(typeof(AccessibilityRole));
            Assert.That(values, Is.EqualTo(new[]
            {
                AccessibilityRole.None,
                AccessibilityRole.Button,
                AccessibilityRole.Image,
                AccessibilityRole.Tab,
                AccessibilityRole.Header,
                AccessibilityRole.Adjustable,
            }));
        }

        // ---- AccessibilityLabel.ToScreenReaderString: canonical order, null-safe ----

        [Test]
        public void Screen_reader_string_is_text_then_state_then_role()
        {
            var label = AccessibilityLabel.Of(AccessibilityRole.Image, "Antlered Shade", "12 of 67");
            Assert.That(label.ToScreenReaderString(), Is.EqualTo("Antlered Shade, 12 of 67, image"));
        }

        [Test]
        public void Screen_reader_string_drops_empty_parts()
        {
            // No state → text + role only.
            var noState = AccessibilityLabel.Of(AccessibilityRole.Button, "SLAY");
            Assert.That(noState.ToScreenReaderString(), Is.EqualTo("SLAY, button"));

            // None role → no role word.
            var noRole = AccessibilityLabel.Of(AccessibilityRole.None, "Veilwalkers");
            Assert.That(noRole.ToScreenReaderString(), Is.EqualTo("Veilwalkers"));
        }

        [Test]
        public void Default_label_and_null_inputs_are_graceful_never_throw()
        {
            AccessibilityLabel def = default;
            Assert.That(def.Text, Is.EqualTo(string.Empty));
            Assert.That(def.State, Is.EqualTo(string.Empty));
            Assert.DoesNotThrow(() => { var _ = def.ToScreenReaderString(); });
            Assert.That(def.ToScreenReaderString(), Is.EqualTo(string.Empty), "a fully-empty label → empty, not a crash");

            // Null inputs coalesce to empty.
            var nulls = AccessibilityLabel.Of(AccessibilityRole.None, null, null);
            Assert.That(nulls.ToScreenReaderString(), Is.EqualTo(string.Empty));
        }

        // ---- AC-4: per-component labels (role correct, state reflects descriptor) ----

        [Test]
        public void Chunky_button_label_is_a_button_with_its_all_caps_label()
        {
            var slay = AccessibilityLabels.ForButton(ChunkyButtonStyle.For(ChunkyButtonKind.Slay, "x"));
            Assert.That(slay.Role, Is.EqualTo(AccessibilityRole.Button));
            Assert.That(slay.Text, Is.EqualTo("SLAY"));
            Assert.That(slay.ToScreenReaderString(), Is.EqualTo("SLAY, button"));
        }

        [Test]
        public void Disabled_button_label_carries_the_disabled_state()
        {
            var disabled = AccessibilityLabels.ForButton(ChunkyButtonStyle.For(ChunkyButtonKind.Primary, "PLAY"), enabled: false);
            Assert.That(disabled.State, Is.EqualTo("disabled"));
            Assert.That(disabled.ToScreenReaderString(), Is.EqualTo("PLAY, disabled, button"));
        }

        [Test]
        public void Credit_pill_label_is_a_button_announcing_the_balance_and_never_locks_at_zero()
        {
            var pill = AccessibilityLabels.ForCreditPill(CreditPillStyle.For(20, false));
            Assert.That(pill.Role, Is.EqualTo(AccessibilityRole.Button), "the pill taps to Shop");
            Assert.That(pill.Text, Is.EqualTo("20 credits"));

            // Zero is a valid announcement, never "disabled" (the world never locks — UX-DR4).
            var zero = AccessibilityLabels.ForCreditPill(CreditPillStyle.For(0, false));
            Assert.That(zero.Text, Is.EqualTo("0 credits"));
            Assert.That(zero.State, Is.EqualTo(string.Empty), "a zero pill is never disabled");
        }

        [Test]
        public void Pack_card_label_is_a_button_announcing_the_total_credits_with_the_badge_state()
        {
            var catalog = new Veilwalkers.Billing.CreditPackCatalog();

            // The Hunter pack (150 + 20 bonus, "POPULAR") — total announced, badge as state.
            var hunter = AccessibilityLabels.ForPackCard(PackCardStyle.For(catalog.Packs[1]));
            Assert.That(hunter.Role, Is.EqualTo(AccessibilityRole.Button), "the pack card taps to Buy");
            Assert.That(hunter.Text, Is.EqualTo("170 credits"), "announces the transparent base+bonus total");
            Assert.That(hunter.State, Is.EqualTo("POPULAR"), "the soft-nudge badge is the state");

            // The Starter pack (50, no bonus, no badge) — no badge state.
            var starter = AccessibilityLabels.ForPackCard(PackCardStyle.For(catalog.Packs[0]));
            Assert.That(starter.Text, Is.EqualTo("50 credits"));
            Assert.That(starter.State, Is.EqualTo(string.Empty), "no badge → no state");
            Assert.That(starter.ToScreenReaderString(), Is.EqualTo("50 credits, button"));
        }

        // ---- AC-4 + D8: the REVEAL FIREWALL — an undiscovered slot never names the Monster ----

        [Test]
        public void Discovered_codex_slot_announces_the_monster_name_as_an_image()
        {
            var slot = CodexSlotStyle.For(CodexSlotState.Discovered, Rarity.Rare);
            var label = AccessibilityLabels.ForCodexSlot(slot, "Antlered Shade");
            Assert.That(label.Role, Is.EqualTo(AccessibilityRole.Image));
            Assert.That(label.Text, Is.EqualTo("Antlered Shade"));
            Assert.That(label.State, Is.EqualTo("discovered"));
        }

        [Test]
        public void Discovered_slot_with_null_or_empty_name_degrades_to_a_calm_discovered_label()
        {
            // NFR-3 graceful: a discovered slot whose name is null/empty (an unauthored-but-discovered
            // id) falls back to a calm "Discovered" rather than an empty or null announcement — never
            // throws. The reveal firewall is not at stake here (the slot IS discovered).
            var slot = CodexSlotStyle.For(CodexSlotState.Discovered, Rarity.Common);

            var nullName = AccessibilityLabels.ForCodexSlot(slot, null);
            Assert.That(nullName.Text, Is.EqualTo("Discovered"));
            Assert.That(nullName.Role, Is.EqualTo(AccessibilityRole.Image));
            Assert.That(nullName.ToScreenReaderString(), Is.Not.Empty);

            var emptyName = AccessibilityLabels.ForCodexSlot(slot, "");
            Assert.That(emptyName.Text, Is.EqualTo("Discovered"));
        }

        [Test]
        public void Undiscovered_silhouette_slot_never_names_the_monster()
        {
            // The load-bearing reveal pin: a silhouette announces "Undiscovered", NEVER the name —
            // even when the (would-be) name is passed in. The a11y label must not leak identity.
            var slot = CodexSlotStyle.For(CodexSlotState.Silhouette, Rarity.Nightmare);
            var label = AccessibilityLabels.ForCodexSlot(slot, "SECRET NIGHTMARE NAME");
            Assert.That(label.Text, Is.EqualTo(AccessibilityLabels.UndiscoveredText));
            Assert.That(label.Text, Does.Not.Contain("SECRET"), "the silhouette label must not leak the name");
            Assert.That(label.Role, Is.EqualTo(AccessibilityRole.Tab));
        }

        [Test]
        public void Blank_locked_slot_never_names_the_monster()
        {
            var slot = CodexSlotStyle.For(CodexSlotState.Blank, (Rarity?)null);
            var label = AccessibilityLabels.ForCodexSlot(slot, "ANYTHING");
            Assert.That(label.Text, Is.EqualTo(AccessibilityLabels.LockedText));
            Assert.That(label.Text, Does.Not.Contain("ANYTHING"), "the blank label must not leak a name");
            Assert.That(label.Role, Is.EqualTo(AccessibilityRole.Tab));
        }

        [Test]
        public void Every_interactive_descriptor_produces_a_non_empty_role_label_string()
        {
            // AC-4: every interactive element is labeled. None of the per-component factories produce an
            // empty announcement.
            Assert.That(AccessibilityLabels.ForButton(ChunkyButtonStyle.For(ChunkyButtonKind.Primary, "PLAY"))
                .ToScreenReaderString(), Is.Not.Empty);
            Assert.That(AccessibilityLabels.ForCreditPill(CreditPillStyle.For(5, true))
                .ToScreenReaderString(), Is.Not.Empty);
            Assert.That(AccessibilityLabels.ForCodexSlot(CodexSlotStyle.For(CodexSlotState.Discovered, Rarity.Common), "Mon")
                .ToScreenReaderString(), Is.Not.Empty);
            Assert.That(AccessibilityLabels.ForCodexSlot(CodexSlotStyle.For(CodexSlotState.Silhouette, Rarity.Rare), null)
                .ToScreenReaderString(), Is.Not.Empty);
        }
    }
}
