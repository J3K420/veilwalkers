using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

// The EditMode test assembly builds a registry through the internal SetForTests seam
// below. The Monsters.Tests asmdef references Veilwalkers.Monsters, so this attribute is
// all that is required — no asmdef edit. (Architecture.Tests adds its own Monsters ref.)
// Veilwalkers.UI.Tests (Story 2.4) also builds in-memory registries via SetForTests to
// drive the Codex grid presenter, so it is granted the same internals access.
[assembly: InternalsVisibleTo("Veilwalkers.Monsters.Tests")]
[assembly: InternalsVisibleTo("Veilwalkers.UI.Tests")]

namespace Veilwalkers.Monsters
{
    /// <summary>
    /// The registry of the 67-Monster universe (AR-3 / AR-20). A
    /// <see cref="ScriptableObject"/> catalog: it declares the full universe as the lore
    /// constant <see cref="UniverseCount"/> (= 67) and holds the <em>populated</em>
    /// <see cref="MonsterDefinition"/> entries — 3–5 in MVP, the rest of the 67 are
    /// reserved IDs filled in later content passes.
    /// <para>
    /// <b>UniverseCount vs PopulatedCount.</b> <see cref="UniverseCount"/> is the AR-20
    /// lore constant (always 67), NOT a count of authored assets; <see cref="PopulatedCount"/>
    /// is how many <see cref="MonsterDefinition"/>s are actually populated. The architecture's
    /// "<c>MonsterDatabase.Count == 67</c>" (AR-20) refers to the universe/lore constant — this
    /// is a deliberate reading: authoring 67 stub assets would be wrong, and MVP populates 3–5
    /// (AR-3). The architecture test asserts the model relationships (universe == 67, populated
    /// in [3..5], every populated ID within the universe), not a literal count of slots.
    /// </para>
    /// <para>
    /// <b>Static data only.</b> Neither the registry nor its definitions hold player/runtime
    /// state (that is <c>SaveModel</c>/<c>CodexService</c>, Story 2.3). The ID index is a
    /// runtime read cache, never persisted.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "MonsterDatabase", menuName = "Veilwalkers/Monster Database")]
    public sealed class MonsterDatabase : ScriptableObject
    {
        /// <summary>The size of the Monster universe — the AR-20 lore constant.</summary>
        public const int UniverseCount = 67;

        [Tooltip("The populated Monster definitions (3–5 in MVP; reserved IDs fill in later).")]
        [SerializeField] private MonsterDefinition[] _monsters = new MonsterDefinition[0];

        private Dictionary<string, MonsterDefinition> _byId;

        /// <summary>How many <see cref="MonsterDefinition"/>s are actually populated (MVP: 3–5).</summary>
        public int PopulatedCount => _monsters?.Length ?? 0;

        /// <summary>
        /// The populated definitions, read-only. Returns a defensive copy so a caller
        /// cannot cast back to <c>MonsterDefinition[]</c> and mutate the registry's backing
        /// store (which would also stale the cached ID index) — the "static data only"
        /// invariant is enforced, not honor-system.
        /// </summary>
        public IReadOnlyList<MonsterDefinition> Populated =>
            (MonsterDefinition[])(_monsters ?? System.Array.Empty<MonsterDefinition>()).Clone();

        /// <summary>
        /// Look up a populated Monster by its stable <c>monNN</c> ID (never array index).
        /// Returns false (and null) for an unknown or null/empty ID — never throws, never
        /// returns the wrong entry.
        /// </summary>
        public bool TryGet(string id, out MonsterDefinition definition)
        {
            EnsureIndex();
            if (string.IsNullOrEmpty(id))
            {
                definition = null;
                return false;
            }
            return _byId.TryGetValue(id, out definition);
        }

        /// <summary>
        /// Look up a populated Monster by its <c>monNN</c> ID, or null if absent. Convenience
        /// over <see cref="TryGet"/>; never throws.
        /// </summary>
        public MonsterDefinition GetById(string id)
        {
            TryGet(id, out var definition);
            return definition;
        }

        /// <summary>
        /// Validate the populated content (settles the Story 2.1 CR deferrals at the registry
        /// boundary). Returns a list of problems — empty means valid. Does NOT throw: a
        /// garbage authored asset surfaces as a failing content test, not a runtime crash
        /// (AR-7 typed-result house style). Checks, per populated entry: a null array slot,
        /// non-empty <c>monNN</c> ID within the universe, non-empty DisplayName, non-empty Lore,
        /// non-null Art, an in-range <see cref="Rarity"/>; plus no duplicate IDs across entries.
        /// Stat bounds are deliberately NOT checked here — that is Story 4 / OQ-9.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            var seenIds = new HashSet<string>();

            // Guard the array itself: a corrupt/migrated SO can deserialize _monsters to
            // null (sibling accessors already guard this); Validate() must not NRE — it
            // promises never to throw.
            var monsters = _monsters ?? System.Array.Empty<MonsterDefinition>();
            for (int i = 0; i < monsters.Length; i++)
            {
                var m = monsters[i];
                if (m == null)
                {
                    problems.Add($"Entry [{i}] is null (unassigned slot).");
                    continue; // never dereference a null entry
                }

                // Use the array index [i] in messages, not m.Id — m.Id may itself be null.
                if (!IsValidMonsterId(m.Id))
                    problems.Add($"Entry [{i}] has invalid Id '{m.Id}' (expected mon01..mon67).");
                else if (!seenIds.Add(m.Id))
                    problems.Add($"Entry [{i}] duplicates Id '{m.Id}'.");

                if (string.IsNullOrEmpty(m.DisplayName))
                    problems.Add($"Entry [{i}] has a null/empty DisplayName.");
                if (string.IsNullOrEmpty(m.Lore))
                    problems.Add($"Entry [{i}] has a null/empty Lore.");
                if (m.Art == null)
                    problems.Add($"Entry [{i}] has a null Art reference.");
                if (!System.Enum.IsDefined(typeof(Rarity), m.Rarity))
                    problems.Add($"Entry [{i}] has an out-of-range Rarity ({(int)m.Rarity}).");
            }

            return problems;
        }

        /// <summary>
        /// True if <paramref name="id"/> is a stable Monster ID within the universe:
        /// <c>mon</c> followed by two digits naming 01..<see cref="UniverseCount"/> (67).
        /// The single production source of the <c>monNN</c> convention (replaces Story 2.1's
        /// hand-recomputed test literal).
        /// </summary>
        public static bool IsValidMonsterId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length != 5 || !id.StartsWith("mon"))
                return false;
            // Require EXACTLY two ASCII digits — int.TryParse would otherwise accept
            // whitespace/sign-padded forms ('mon 1', 'mon+1') that violate ^mon\d{2}$.
            char d1 = id[3], d2 = id[4];
            if (d1 < '0' || d1 > '9' || d2 < '0' || d2 > '9')
                return false;
            int n = (d1 - '0') * 10 + (d2 - '0');
            return n >= 1 && n <= UniverseCount;
        }

        private void EnsureIndex()
        {
            if (_byId != null)
                return;
            _byId = new Dictionary<string, MonsterDefinition>();
            if (_monsters == null)
                return;
            foreach (var m in _monsters)
            {
                if (m == null || string.IsNullOrEmpty(m.Id))
                    continue; // skip bad entries; Validate() reports them
                // First-writer-wins on a duplicate ID: lookup is deterministic by array
                // order, never silently overwritten by a later duplicate. Validate()
                // reports the duplicate as content the author must fix.
                if (!_byId.ContainsKey(m.Id))
                    _byId[m.Id] = m;
            }
        }

        /// <summary>
        /// Test-only initializer. Populates the registry from in-memory definitions
        /// (<c>CreateInstance</c>'d via <see cref="MonsterDefinition.SetForTests"/>) without a
        /// serialized <c>.asset</c>. Nulls the cached index so the next lookup rebuilds —
        /// this is why the index is lazy (an <c>OnEnable</c>-only index would be stale here,
        /// since <c>OnEnable</c> fires at <c>CreateInstance</c> before this runs). Internal:
        /// a registry is static data, never mutated by production code.
        /// </summary>
        internal void SetForTests(MonsterDefinition[] populated)
        {
            _monsters = populated ?? new MonsterDefinition[0];
            _byId = null;
        }
    }
}
