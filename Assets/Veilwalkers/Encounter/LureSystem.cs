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

        private readonly EconomyConfig _config;
        private readonly MonsterDatabase _database;
        private readonly IRandom _random;

        public LureSystem(EconomyConfig config, MonsterDatabase database, IRandom random)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>The Credit cost of <paramref name="kind"/> — read from <see cref="EconomyConfig"/>
        /// (AR-16; never hard-coded).</summary>
        public int CostOf(LureKind kind)
        {
            switch (kind)
            {
                case LureKind.Basic: return _config.BasicLureCost;
                case LureKind.Premium: return _config.PremiumLureCost;
                case LureKind.Multi: return _config.MultiLureCost;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown LureKind.");
            }
        }

        /// <summary>How many Monsters <paramref name="kind"/> spawns: 1 for Basic/Premium, 2 for Multi (AC-3).</summary>
        public int MonsterCountOf(LureKind kind) => kind == LureKind.Multi ? 2 : 1;

        /// <summary>The rare-tier roll probability for <paramref name="kind"/> (AC-2). Multi rolls each
        /// monster at the Basic chance.</summary>
        public double RareChanceOf(LureKind kind) =>
            kind == LureKind.Premium ? PremiumRareChance : BasicRareChance;

        /// <summary>
        /// Roll ONE Monster for <paramref name="kind"/> (Basic/Premium): a rare-tier gate at
        /// <see cref="RareChanceOf"/>, then a uniform pick from the matching populated subset. Returns the
        /// chosen Monster id. Throws if the database is empty (nothing to Lure — content/programmer error).
        /// </summary>
        public string RollMonster(LureKind kind) => RollOne(RareChanceOf(kind));

        /// <summary>
        /// Roll the TWO Monsters of a Multi-Lure (AC-3) — each INDEPENDENTLY (two separate draws), so the
        /// pair is not a single duplicated roll. Returns both ids (may be the same Monster by chance — a
        /// Multi can legitimately surface two of a kind from the small MVP roster).
        /// </summary>
        public (string first, string second) RollMultiMonsters()
        {
            double chance = RareChanceOf(LureKind.Multi);
            return (RollOne(chance), RollOne(chance));
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
