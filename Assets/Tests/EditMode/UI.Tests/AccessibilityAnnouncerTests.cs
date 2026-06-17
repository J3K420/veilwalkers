using NUnit.Framework;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// Story 6.6 — the screen-reader announce-string decisions (AC-4; UX-DR17): the credit-pill balance
    /// announce + the Codex count tick-up announce. Exact canonical wording (non-tautological — the 6.5
    /// exact-wording precedent), plain functional copy (Decision E), with the clamp + the singular/plural
    /// + the completion variant pinned.
    /// </summary>
    public sealed class AccessibilityAnnouncerTests
    {
        // ---- AC-4: credit-pill balance announce ("N credits") ----

        [Test]
        public void Balance_announcement_is_n_credits()
        {
            Assert.That(AccessibilityAnnouncer.AnnounceBalance(20), Is.EqualTo("20 credits"));
            Assert.That(AccessibilityAnnouncer.AnnounceBalance(50), Is.EqualTo("50 credits"));
        }

        [Test]
        public void Zero_balance_announces_zero_credits_world_never_locks()
        {
            Assert.That(AccessibilityAnnouncer.AnnounceBalance(0), Is.EqualTo("0 credits"));
        }

        [Test]
        public void One_credit_is_singular()
        {
            Assert.That(AccessibilityAnnouncer.AnnounceBalance(1), Is.EqualTo("1 credit"));
        }

        [Test]
        public void Negative_balance_clamps_to_zero_credits()
        {
            // A balance is never negative; the announce string clamps to the floor (the ClampToFloor
            // precedent) rather than announcing "-5 credits".
            Assert.That(AccessibilityAnnouncer.AnnounceBalance(-5), Is.EqualTo("0 credits"));
        }

        // ---- AC-4: Codex count tick-up announce ("X of Y discovered") ----

        [Test]
        public void Discovery_announcement_is_x_of_y_discovered()
        {
            Assert.That(AccessibilityAnnouncer.AnnounceDiscovery(12, 67), Is.EqualTo("12 of 67 discovered"));
            Assert.That(AccessibilityAnnouncer.AnnounceDiscovery(1, 67), Is.EqualTo("1 of 67 discovered"));
        }

        [Test]
        public void Completion_announces_the_codex_complete_variant()
        {
            // At 67/67 the announcement is the completion variant (tied to OnCodexCompleted), not just
            // "67 of 67 discovered".
            Assert.That(AccessibilityAnnouncer.AnnounceDiscovery(67, 67),
                Is.EqualTo("67 of 67 — Codex complete"));
        }

        [Test]
        public void Discovered_at_or_past_universe_uses_the_completion_variant_not_an_overflow_count()
        {
            // The completion guard is `discovered >= universe`, NOT `== universe` — so a count that
            // somehow exceeds the universe (a stale/over-counted load) still reads as "complete", never
            // "68 of 67 discovered". Mutation-testable: flipping `>=` to `==` makes 68/67 announce the
            // raw count, which this pin catches.
            Assert.That(AccessibilityAnnouncer.AnnounceDiscovery(68, 67),
                Is.EqualTo("67 of 67 — Codex complete"));
            Assert.That(AccessibilityAnnouncer.AnnounceDiscovery(100, 67),
                Is.EqualTo("67 of 67 — Codex complete"));
        }

        [Test]
        public void Discovery_counts_clamp_non_negative()
        {
            Assert.That(AccessibilityAnnouncer.AnnounceDiscovery(-3, 67), Is.EqualTo("0 of 67 discovered"));
        }

        [Test]
        public void Credits_helper_matches_announce_balance()
        {
            // The pill label (AccessibilityLabels.ForCreditPill) reads Credits(); pin it agrees with the
            // announce string so a balance change and the pill label never diverge.
            Assert.That(AccessibilityAnnouncer.Credits(20), Is.EqualTo(AccessibilityAnnouncer.AnnounceBalance(20)));
            Assert.That(AccessibilityAnnouncer.Credits(1), Is.EqualTo("1 credit"));
            Assert.That(AccessibilityAnnouncer.Credits(0), Is.EqualTo("0 credits"));
        }
    }
}
