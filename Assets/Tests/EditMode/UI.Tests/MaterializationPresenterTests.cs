using NUnit.Framework;
using Veilwalkers.Monsters;

namespace Veilwalkers.UI.Tests
{
    /// <summary>
    /// <see cref="MaterializationPresenter"/> as the pure-logic materialization decision
    /// (Story 4.7): the per-tier entrance variant + duration (AC-1), the action-bar-hidden gate
    /// (AC-1), the discrete ambiance shift (AC-1, never a literal rarity bar), the T5 branded
    /// glitch bounded so it never blocks input (AC-2), the reduced-motion taming that preserves
    /// the dread (AC-3), and the NFR-2 spend-ack decoupling. Every test exercises the production
    /// presenter; none recompute the tier→variant/duration map (the 2.1–2.4 anti-tautology bar).
    /// Each rule is mutation-testable: break a mapping or the taming branch and a named test goes
    /// red.
    /// </summary>
    public sealed class MaterializationPresenterTests
    {
        // The five tiers in ascending order — the spine for the monotonicity + per-tier sweeps.
        private static readonly Rarity[] AscendingTiers =
        {
            Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Nightmare,
        };

        private static MaterializationPresenter Presenter() => new MaterializationPresenter();

        // ---- AC-1: per-tier entrance variant ----

        [Test]
        public void Each_tier_maps_to_its_named_entrance_variant()
        {
            var p = Presenter();
            Assert.That(p.PlanFor(Rarity.Common, false).Variant, Is.EqualTo(MaterializationVariant.PopIn));
            Assert.That(p.PlanFor(Rarity.Uncommon, false).Variant, Is.EqualTo(MaterializationVariant.Unfurl));
            Assert.That(p.PlanFor(Rarity.Rare, false).Variant, Is.EqualTo(MaterializationVariant.Seep));
            Assert.That(p.PlanFor(Rarity.Epic, false).Variant, Is.EqualTo(MaterializationVariant.Tear));
            Assert.That(p.PlanFor(Rarity.Nightmare, false).Variant, Is.EqualTo(MaterializationVariant.Breach));
        }

        // ---- AC-1: duration scales with tier (the BEHAVIOR pin, not a magic number) ----

        [Test]
        public void Duration_is_monotonic_non_decreasing_with_tier_and_strictly_increases()
        {
            var p = Presenter();
            bool sawStrictIncrease = false;
            for (int i = 0; i < AscendingTiers.Length - 1; i++)
            {
                float lower = p.PlanFor(AscendingTiers[i], false).DurationSeconds;
                float higher = p.PlanFor(AscendingTiers[i + 1], false).DurationSeconds;
                Assert.That(higher, Is.GreaterThanOrEqualTo(lower),
                    $"{AscendingTiers[i + 1]} duration must be >= {AscendingTiers[i]}");
                if (higher > lower) sawStrictIncrease = true;
            }
            Assert.That(sawStrictIncrease, Is.True, "durations must strictly increase across the ladder, not be flat");
        }

        [Test]
        public void Each_tier_duration_falls_within_its_AC_band()
        {
            // The AC gives approximate per-tier durations; pin the BAND, not an exact value
            // (the exact ms is Epic-6/OQ-9 polish). T1 ~0.5s, T2 ~1s, T3 ~1.5s, T4 ~2s, T5 ~2.5–3s.
            var p = Presenter();
            AssertInBand(p.PlanFor(Rarity.Common, false).DurationSeconds, 0.4f, 0.6f, "T1 Pop-in ~0.5s");
            AssertInBand(p.PlanFor(Rarity.Uncommon, false).DurationSeconds, 0.8f, 1.2f, "T2 Unfurl ~1s");
            AssertInBand(p.PlanFor(Rarity.Rare, false).DurationSeconds, 1.3f, 1.7f, "T3 Seep ~1.5s");
            AssertInBand(p.PlanFor(Rarity.Epic, false).DurationSeconds, 1.8f, 2.2f, "T4 Tear ~2s");
            AssertInBand(p.PlanFor(Rarity.Nightmare, false).DurationSeconds, 2.5f, 3.0f, "T5 Breach ~2.5–3s");
        }

        private static void AssertInBand(float value, float lo, float hi, string label)
        {
            Assert.That(value, Is.InRange(lo, hi), label);
        }

        // ---- AC-1: action-bar gate (hidden during, pops in on settle) ----

        [Test]
        public void Action_bar_is_hidden_during_materialization_for_every_tier()
        {
            var p = Presenter();
            foreach (var tier in AscendingTiers)
            {
                Assert.That(p.PlanFor(tier, false).HideActionBarDuringMaterialization, Is.True,
                    $"{tier} plan must hide the action bar during materialization");
            }
        }

        [Test]
        public void Action_bar_pops_in_only_on_settle()
        {
            var p = Presenter();
            Assert.That(p.ShouldShowActionBar(false), Is.False, "bar stays hidden while materializing");
            Assert.That(p.ShouldShowActionBar(true), Is.True, "bar pops in once settled");
        }

        // ---- AC-1: ambiance shifts toward the tier (a discrete scale, NOT a literal rarity bar) ----

        [Test]
        public void Ambiance_intensity_shifts_toward_the_active_tier()
        {
            var p = Presenter();
            Assert.That(p.PlanFor(Rarity.Common, false).Ambiance.Intensity, Is.EqualTo(AmbianceIntensity.Calm));
            Assert.That(p.PlanFor(Rarity.Uncommon, false).Ambiance.Intensity, Is.EqualTo(AmbianceIntensity.Unsettled));
            Assert.That(p.PlanFor(Rarity.Rare, false).Ambiance.Intensity, Is.EqualTo(AmbianceIntensity.Tense));
            Assert.That(p.PlanFor(Rarity.Epic, false).Ambiance.Intensity, Is.EqualTo(AmbianceIntensity.Dreadful));
            Assert.That(p.PlanFor(Rarity.Nightmare, false).Ambiance.Intensity, Is.EqualTo(AmbianceIntensity.Nightmarish));
        }

        [Test]
        public void Slay_glow_is_only_on_the_highest_tier()
        {
            var p = Presenter();
            Assert.That(p.PlanFor(Rarity.Nightmare, false).Ambiance.SlayGlow, Is.True, "SLAY-glow on the top tier");
            foreach (var tier in AscendingTiers)
            {
                if (tier == Rarity.Nightmare) continue;
                Assert.That(p.PlanFor(tier, false).Ambiance.SlayGlow, Is.False, $"{tier} must not carry the SLAY-glow");
            }
        }

        // ---- AC-2: T5 Breach is branded + bounded + never blocks input ----

        [Test]
        public void Nightmare_breach_carries_the_branded_glitch()
        {
            Assert.That(Presenter().PlanFor(Rarity.Nightmare, false).BrandedGlitch, Is.True);
        }

        [Test]
        public void Branded_glitch_is_only_the_top_tier()
        {
            var p = Presenter();
            foreach (var tier in AscendingTiers)
            {
                if (tier == Rarity.Nightmare) continue;
                Assert.That(p.PlanFor(tier, false).BrandedGlitch, Is.False, $"{tier} is not a glitch entrance");
            }
        }

        [Test]
        public void Breach_is_brief_and_bounded_and_never_blocks_input_recovery()
        {
            var plan = Presenter().PlanFor(Rarity.Nightmare, false);
            // Brief/bounded: the hero entrance never runs past the ~3s band (reads as designed FX,
            // not a hang/crash).
            Assert.That(plan.DurationSeconds, Is.LessThanOrEqualTo(3.0f), "Breach must stay brief/bounded");
            // The logic-side AC-2 guarantee: input recovery is NEVER blocked, on any plan.
            Assert.That(plan.InputRecoveryBlocked, Is.False, "a Breach must never block input recovery");
        }

        [Test]
        public void No_plan_ever_blocks_input_recovery()
        {
            var p = Presenter();
            foreach (var tier in AscendingTiers)
            {
                Assert.That(p.PlanFor(tier, false).InputRecoveryBlocked, Is.False, $"{tier} (motion on)");
                Assert.That(p.PlanFor(tier, true).InputRecoveryBlocked, Is.False, $"{tier} (reduced motion)");
            }
        }

        // ---- AC-3: reduced-motion tames every tier ----

        [Test]
        public void Reduced_motion_disables_the_branded_glitch_on_every_tier_including_breach()
        {
            var p = Presenter();
            foreach (var tier in AscendingTiers)
            {
                Assert.That(p.PlanFor(tier, true).BrandedGlitch, Is.False,
                    $"{tier} tamed plan must carry no branded glitch (glitch→static, UX-DR17)");
            }
        }

        [Test]
        public void Reduced_motion_plans_are_flagged_reduced_motion()
        {
            var p = Presenter();
            foreach (var tier in AscendingTiers)
            {
                Assert.That(p.PlanFor(tier, true).ReducedMotion, Is.True, $"{tier} tamed plan flagged ReducedMotion");
                Assert.That(p.PlanFor(tier, false).ReducedMotion, Is.False, $"{tier} normal plan not flagged");
            }
        }

        // ---- AC-3: the dread still reads when tamed (variant + ambiance preserved) ----

        [Test]
        public void Taming_preserves_the_entrance_variant_and_ambiance_so_dread_still_reads()
        {
            var p = Presenter();
            foreach (var tier in AscendingTiers)
            {
                var normal = p.PlanFor(tier, false);
                var tamed = p.PlanFor(tier, true);
                // Taming is about MOTION (drop the glitch/shake/strobe), NOT removing the entrance:
                // the variant is preserved (a tamed Nightmare is still a Breach, not a no-op)...
                Assert.That(tamed.Variant, Is.EqualTo(normal.Variant), $"{tier} tamed variant preserved");
                // ...and the ambiance/tier dread is preserved (still Nightmarish for T5).
                Assert.That(tamed.Ambiance.Intensity, Is.EqualTo(normal.Ambiance.Intensity),
                    $"{tier} tamed ambiance preserved — dread still reads");
            }
            // The headline AC-3 case spelled out: a tamed T5 still reads as a Nightmare.
            var tamedNightmare = p.PlanFor(Rarity.Nightmare, true);
            Assert.That(tamedNightmare.Variant, Is.EqualTo(MaterializationVariant.Breach));
            Assert.That(tamedNightmare.Ambiance.Intensity, Is.EqualTo(AmbianceIntensity.Nightmarish));
        }

        // ---- AC-3: NFR-2 — the materialization plan never gates the spend-ack ----

        [Test]
        public void PlanFor_is_a_pure_synchronous_call_that_does_no_economy_work()
        {
            // NFR-2: the spend-ack is the synchronous LureResult returned by Story 4.2 BEFORE any
            // materialization plays. PlanFor must therefore be a pure, immediate, side-effect-free
            // value computation — the entrance DURATION lives in returned DATA, never in a blocking
            // call the caller must await before acking. The presenter takes no service, touches no
            // Credits, and returns immediately; this test pins that it is a plain synchronous call
            // (no Task return) whose duration is metadata, not a wait.
            var method = typeof(MaterializationPresenter).GetMethod(
                nameof(MaterializationPresenter.PlanFor), new[] { typeof(Rarity), typeof(bool) });
            Assert.That(method, Is.Not.Null);
            Assert.That(typeof(System.Threading.Tasks.Task).IsAssignableFrom(method.ReturnType), Is.False,
                "PlanFor must be synchronous — the spend-ack must never await the materialization (NFR-2)");
            Assert.That(method.ReturnType, Is.EqualTo(typeof(MaterializationPlan)),
                "PlanFor returns the plan as data; the duration is metadata the render consumes, not a wait");

            // And the duration genuinely lives in the returned data (not consumed by the call).
            var plan = Presenter().PlanFor(Rarity.Nightmare, false);
            Assert.That(plan.DurationSeconds, Is.GreaterThan(0f), "duration is data on the plan");
        }

        // ---- the monster-id label (carried, empty-coalesced, never throws) ----

        [Test]
        public void Monster_id_is_carried_onto_the_plan()
        {
            Assert.That(Presenter().PlanFor("mon07", Rarity.Rare, false).MonsterId, Is.EqualTo("mon07"));
        }

        [Test]
        public void Null_monster_id_coalesces_to_empty_and_does_not_throw()
        {
            MaterializationPlan plan = default;
            Assert.DoesNotThrow(() => plan = Presenter().PlanFor(null, Rarity.Common, false));
            Assert.That(plan.MonsterId, Is.EqualTo(string.Empty));
            // The tier still fully drives the plan even with no id.
            Assert.That(plan.Variant, Is.EqualTo(MaterializationVariant.PopIn));
        }

        [Test]
        public void PlanFor_tier_only_overload_uses_an_empty_monster_id()
        {
            Assert.That(Presenter().PlanFor(Rarity.Epic, false).MonsterId, Is.EqualTo(string.Empty));
        }

        // ---- CR-patch: default(MaterializationPlan) honors the contract (struct default bypasses
        //      the factory, but the null-safe MonsterId getter + the constant invariant properties
        //      keep a default plan from violating the documented contract / NRE-ing the render). ----

        [Test]
        public void Default_plan_monster_id_is_empty_not_null()
        {
            MaterializationPlan def = default;
            // The getter coalesces the null backing field → empty, so render code that does
            // plan.MonsterId.Length / string interpolation never NREs on a default plan.
            Assert.That(def.MonsterId, Is.EqualTo(string.Empty));
            Assert.DoesNotThrow(() => { int _ = def.MonsterId.Length; });
        }

        [Test]
        public void Default_plan_still_hides_the_action_bar_and_never_blocks_input()
        {
            // The invariants are constant properties, not ctor-set fields — so even a default plan
            // (an array slot / uninitialized field) honors UX-DR12 + AC-2. A view reading the gate
            // off a default plan never accidentally shows the bar mid-materialization.
            MaterializationPlan def = default;
            Assert.That(def.HideActionBarDuringMaterialization, Is.True);
            Assert.That(def.InputRecoveryBlocked, Is.False);
        }
    }
}
