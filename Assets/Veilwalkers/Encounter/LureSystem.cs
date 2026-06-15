using System;
using System.Collections.Generic;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Monsters;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// The Lure decision logic (Story 4.2, FR-6; architecture.md:416 names this file, :701 — "consumed
    /// through the existing Lure path in <c>Encounter/LureSystem</c>"). Pure C# (NO MonoBehaviour, NO Unity
    /// types), constructor-injected (AR-4 — NEVER <c>GameServices.Get&lt;T&gt;()</c>). It makes ONLY the
    /// decision — kind → Credit cost (<see cref="CostOf"/>) and kind → the chosen Monster id(s)
    /// (<see cref="RollMonster"/>). It does NOT spend, persist, place, or spawn — those are the
    /// <see cref="EncounterService"/> composed action (AC-1). Free of side effects, so the rarity roll is
    /// trivially headless-testable against a scripted <see cref="IRandom"/>.
    /// <para>
    /// <b>The rarity roll (AC-2).</b> A draw on <see cref="IRandom.NextDouble"/> decides whether the spawn
    /// is rare-tier-or-better (<c>Rarity &gt;= RarityThresholds.GuaranteedRareFloor</c>): below the kind's
    /// rare chance → pick uniformly from the rare-or-better populated subset; otherwise → from the
    /// below-rare subset. The HARD invariant (AC-2): <see cref="PremiumRareChance"/> &gt;
    /// <see cref="BasicRareChance"/> STRICTLY, so Premium's rare probability strictly exceeds Basic's. The
    /// exact percentages are PROVISIONAL (OQ-9 / OQ-2 balancing) — only the strict inequality is the AC.
    /// </para>
    /// <para>
    /// <b>Fallback (MVP 3–5 roster).</b> If the rolled side of the split has no populated Monster (e.g. the
    /// roster has no below-rare entry), the pick falls back to the whole populated set — never returns null
    /// for a valid roster. A genuinely empty database (or one of only null slots) is a programmer/content
    /// error and THROWS (<see cref="InvalidOperationException"/> for an empty set; an all-null roster surfaces
    /// as <see cref="ArgumentOutOfRangeException"/> from the empty pick) — there is nothing to Lure, and
    /// roster validity is the Story 2.2 content-authoring concern, not a runtime LureResult failure.
    /// </para>
    /// <para>
    /// <b>Multi-Lure</b> rolls each of its two Monsters INDEPENDENTLY (two separate draws) — see
    /// <see cref="RollMultiMonsters"/>. The placement/spend/spawn (and the require-two-or-block rule, AC-3)
    /// are <see cref="EncounterService"/>'s; <see cref="LureSystem"/> only chooses the ids.
    /// </para>
    /// </summary>
    public sealed class LureSystem
    {
        // Rare-tier roll probabilities — PROVISIONAL (OQ-9 / OQ-2 balancing). Kept as local consts rather
        // than EconomyConfig fields for now: the AC pins the STRICT inequality, not the values, and the
        // numbers are pure balancing knobs the OQ-9 pass will move (and may promote into EconomyConfig
        // then). The ONE invariant that must hold: Premium > Basic, strictly. Multi rolls each monster at
        // the Basic chance (a Multi is a volume play, not a rarity play — recorded choice, revisitable by
        // balancing).
        /// <summary>Basic Lure rare-tier roll probability (provisional, OQ-9).</summary>
        public const double BasicRareChance = 0.15;

        /// <summary>Premium Lure rare-tier roll probability — STRICTLY greater than <see cref="BasicRareChance"/> (AC-2, provisional OQ-9).</summary>
        public const double PremiumRareChance = 0.50;

        // Story 4.6 (FR-10) — the Nightveil Filter "small rarity boost" added to the rare-tier gate for the
        // remainder of an encounter in which a NightveilFilter charge was applied (AC-2). The MAGNITUDE lives on
        // ExtrasSystem (the single extras-tunables home, alongside StabilityBoostEase — CR patch). Clamped < 1.0
        // so a rare is never guaranteed (that affordance is the $9.99 "Guaranteed-Rare Lure" pack, not a Nightveil
        // charge). The AC pins the BEHAVIOR — a boosted rare chance STRICTLY greater than un-boosted — NOT the magnitude.

        private readonly EconomyConfig _config;
        private readonly MonsterDatabase _database;
        private readonly IRandom _random;

        public LureSystem(EconomyConfig config, MonsterDatabase database, IRandom random)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>The injected <see cref="EconomyConfig"/> (the tunable economy values, AR-16). Exposed so the
        /// <see cref="EncounterService"/> can read action XP/cost values without a duplicate config dependency —
        /// the Lure system already holds the one injected instance.</summary>
        public EconomyConfig Config => _config;

        /// <summary>The Credit cost of <paramref name="kind"/> — read from <see cref="EconomyConfig"/>
        /// (AR-16; never hard-coded).</summary>
        public int CostOf(LureKind kind)
        {
            switch (kind)
            {
                case LureKind.Basic: return _config.BasicLureCost;
                case LureKind.Premium: return _config.PremiumLureCost;
                case LureKind.Multi: return _config.MultiLureCost;
                // Story 5.3 — the Guaranteed-Rare Lure costs ZERO Credits: the Veil pack already paid, and it
                // is consumed from the one-shot SaveModel.GuaranteedRareLures inventory, never Credits. NOT an
                // EconomyConfig tunable (the 4-action credit table is Basic/Premium/Multi/Slay only — canon).
                case LureKind.GuaranteedRare: return 0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown LureKind.");
            }
        }

        /// <summary>How many Monsters <paramref name="kind"/> spawns: 2 for Multi, 1 otherwise (Basic/Premium
        /// and the Story 5.3 GuaranteedRare each spawn ONE).</summary>
        public int MonsterCountOf(LureKind kind) => kind == LureKind.Multi ? 2 : 1;

        /// <summary>The rare-tier roll probability for <paramref name="kind"/> (AC-2). Multi rolls each
        /// monster at the Basic chance.</summary>
        public double RareChanceOf(LureKind kind) => RareChanceOf(kind, nightveilActive: false);

        /// <summary>
        /// The rare-tier roll probability for <paramref name="kind"/>, optionally BOOSTED by an active Nightveil
        /// Filter (Story 4.6, AC-2). <paramref name="nightveilActive"/> = true adds <see cref="ExtrasSystem.NightveilRarityBoost"/>
        /// (clamped just below 1.0 so a rare is never guaranteed), so the boosted chance is STRICTLY greater than
        /// the un-boosted one (the mutation-testable AC). The un-boosted values are byte-identical to pre-4.6.
        /// </summary>
        public double RareChanceOf(LureKind kind, bool nightveilActive)
        {
            double chance = kind == LureKind.Premium ? PremiumRareChance : BasicRareChance;
            if (!nightveilActive)
            {
                return chance;
            }

            // Strictly greater than the un-boosted chance, but capped just under certainty (never 1.0).
            return Math.Min(chance + ExtrasSystem.NightveilRarityBoost, 0.999);
        }

        /// <summary>
        /// Roll ONE Monster for <paramref name="kind"/> (Basic/Premium): a rare-tier gate at
        /// <see cref="RareChanceOf(LureKind)"/>, then a uniform pick from the matching populated subset. Returns
        /// the chosen Monster id. Throws if the database is empty (nothing to Lure — content/programmer error).
        /// </summary>
        public string RollMonster(LureKind kind) => RollMonster(kind, nightveilActive: false);

        /// <summary>
        /// Roll ONE Monster for <paramref name="kind"/>, optionally BOOSTED by an active Nightveil Filter (Story
        /// 4.6, AC-2): the rare-tier gate uses the strictly-higher boosted chance, so the SAME draw that yields a
        /// common pick un-boosted may yield a rare pick boosted. The un-boosted path is byte-identical to pre-4.6.
        /// </summary>
        public string RollMonster(LureKind kind, bool nightveilActive) =>
            RollOne(RareChanceOf(kind, nightveilActive));

        /// <summary>
        /// Roll the TWO Monsters of a Multi-Lure (AC-3) — each INDEPENDENTLY (two separate draws), so the
        /// pair is not a single duplicated roll. Returns both ids (may be the same Monster by chance — a
        /// Multi can legitimately surface two of a kind from the small MVP roster).
        /// </summary>
        public (string first, string second) RollMultiMonsters() => RollMultiMonsters(nightveilActive: false);

        /// <summary>
        /// Roll the two Multi-Lure Monsters, optionally BOOSTED by an active Nightveil Filter (Story 4.6, AC-2):
        /// each independent draw uses the strictly-higher boosted rare chance. The un-boosted path is
        /// byte-identical to pre-4.6.
        /// </summary>
        public (string first, string second) RollMultiMonsters(bool nightveilActive)
        {
            double chance = RareChanceOf(LureKind.Multi, nightveilActive);
            return (RollOne(chance), RollOne(chance));
        }

        /// <summary>
        /// Roll the ONE Monster of a Guaranteed-Rare Lure (Story 5.3, FR-13, AC-2): a uniform pick from the
        /// <c>Rarity &gt;= RarityThresholds.GuaranteedRareFloor</c> (Rare-or-better) populated subset ONLY.
        /// <para>
        /// Unlike <see cref="RollMonster"/> this is NOT a probabilistic roll and has NO common fallback: the
        /// guarantee is a CONTRACT, not a probability. If the roster has no Rare+ Monster (a content/roster
        /// error — the MVP roster MUST contain at least one Rare+), this returns <c>false</c> (a TYPED failure,
        /// never a throw — NFR-3, and never a silent Common spawn that would violate the guarantee). The caller
        /// (<see cref="EncounterService"/>) then blocks the Lure WITHOUT consuming the player's one-shot item.
        /// An empty/all-null database is likewise a typed <c>false</c> (nothing to Lure).
        /// </para>
        /// </summary>
        public bool TryRollGuaranteedRare(out string monsterId)
        {
            monsterId = null;

            IReadOnlyList<MonsterDefinition> populated = _database.Populated;
            if (populated == null || populated.Count == 0)
            {
                return false;
            }

            // Build the rare-or-better subset ONLY — no fallback to the whole set (the guarantee cannot degrade
            // to a Common). Skip null slots defensively (the Validate() pass owns content correctness).
            var rareOrBetter = new List<MonsterDefinition>(populated.Count);
            foreach (MonsterDefinition def in populated)
            {
                if (def != null && def.Rarity >= RarityThresholds.GuaranteedRareFloor)
                {
                    rareOrBetter.Add(def);
                }
            }

            if (rareOrBetter.Count == 0)
            {
                // A content error: the roster cannot honor the guarantee. Surface it as a typed failure so the
                // consume can refuse and KEEP the player's paid lure (EncounterService logs loudly).
                return false;
            }

            int index = _random.Next(rareOrBetter.Count);
            monsterId = rareOrBetter[index].Id;
            return true;
        }

        private string RollOne(double rareChance)
        {
            IReadOnlyList<MonsterDefinition> populated = _database.Populated;
            if (populated == null || populated.Count == 0)
            {
                throw new InvalidOperationException(
                    "LureSystem cannot roll a Monster — the MonsterDatabase has no populated definitions. " +
                    "Author the MVP roster (Story 2.2) before Luring.");
            }

            bool wantRare = _random.NextDouble() < rareChance;

            // Partition the populated set by the rare-or-better floor. Build the matching side; if it is
            // empty (the small MVP roster lacks that side), fall back to the whole populated set — never
            // null, never throw.
            var matching = new List<MonsterDefinition>(populated.Count);
            foreach (MonsterDefinition def in populated)
            {
                if (def == null)
                {
                    continue; // a null slot is content the Validate() pass reports; skip defensively.
                }

                bool isRareOrBetter = def.Rarity >= RarityThresholds.GuaranteedRareFloor;
                if (isRareOrBetter == wantRare)
                {
                    matching.Add(def);
                }
            }

            IReadOnlyList<MonsterDefinition> pool = matching.Count > 0 ? matching : NonNull(populated);
            int index = _random.Next(pool.Count);
            return pool[index].Id;
        }

        // The whole populated set with null slots removed — the fallback pool. Kept separate so a roster
        // entirely of null slots still surfaces the empty-database error (pool.Count == 0 → Next throws via
        // ArgumentOutOfRange, but that is a corrupt-content case the Validate() pass owns).
        private static IReadOnlyList<MonsterDefinition> NonNull(IReadOnlyList<MonsterDefinition> populated)
        {
            var nonNull = new List<MonsterDefinition>(populated.Count);
            foreach (MonsterDefinition def in populated)
            {
                if (def != null)
                {
                    nonNull.Add(def);
                }
            }

            return nonNull;
        }
    }
}
