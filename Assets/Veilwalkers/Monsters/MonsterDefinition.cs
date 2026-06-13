using System.Runtime.CompilerServices;
using UnityEngine;

// The EditMode test assembly populates an in-memory instance through the internal
// SetForTests seam below. The Monsters.Tests asmdef references Veilwalkers.Monsters,
// so this attribute is all that is required — no asmdef edit.
[assembly: InternalsVisibleTo("Veilwalkers.Monsters.Tests")]

namespace Veilwalkers.Monsters
{
    /// <summary>
    /// The static definition of one Monster (AR-3): its identity, rarity tier, base
    /// stats, lore, and art references. A <see cref="ScriptableObject"/> so the 67-Monster
    /// universe is authored as design data and lives OUTSIDE the save file.
    /// <para>
    /// <b>Static design data only.</b> A <see cref="MonsterDefinition"/> is never mutated
    /// at runtime and <b>never holds player/runtime state</b> (architecture: "ScriptableObjects
    /// hold static data only"). Whether the player has discovered, scanned, captured, or
    /// slain this Monster lives in <c>SaveModel</c> (Story 1.3) and is read by
    /// <c>CodexService</c> (Story 2.3) — never here. Fields are private
    /// <c>[SerializeField]</c> exposed through read-only accessors; there is no public
    /// setter and no public mutable field. (That is a convention enforced by the access
    /// shape, not a hard runtime guard — the read-only accessors ARE the guard.)
    /// </para>
    /// <para>
    /// <b>OQ-2 / OQ-9 — PROVISIONAL.</b> The full rarity model is OQ-2 balancing and the
    /// base-stat values are OQ-9 balancing; the MVP populates only 3–5 Monsters (Story 2.2).
    /// This type fixes the SHAPE of static creature data, not its balanced content.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "MonNN_Name", menuName = "Veilwalkers/Monster Definition")]
    public sealed class MonsterDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private Rarity _rarity;

        [Header("Base stats (PROVISIONAL — OQ-9)")]
        [Tooltip("Higher = harder to Capture. Provisional, pending OQ-9 balancing.")]
        [SerializeField] private int _captureDifficulty;
        [Tooltip("Higher = tougher to Slay. Provisional, pending OQ-9 balancing.")]
        [SerializeField] private int _slayDifficulty;

        [Header("Lore & art")]
        [TextArea(2, 6)]
        [SerializeField] private string _lore;
        [SerializeField] private Sprite _art;

        /// <summary>
        /// Stable string ID <c>monNN</c> (mon01..mon67) — the key used everywhere a Monster
        /// is referenced. NEVER an array index or the display name (architecture naming
        /// convention). Asset files are named <c>&lt;Id&gt;_&lt;Name&gt;.asset</c>
        /// (e.g. <c>Mon01_AntleredShade.asset</c>).
        /// </summary>
        public string Id => _id;

        /// <summary>The Monster's display name (e.g. "Antlered Shade").</summary>
        public string DisplayName => _displayName;

        /// <summary>This Monster's ordered <see cref="Rarity"/> tier.</summary>
        public Rarity Rarity => _rarity;

        /// <summary>Base Capture difficulty. Provisional (OQ-9).</summary>
        public int CaptureDifficulty => _captureDifficulty;

        /// <summary>Base Slay difficulty. Provisional (OQ-9).</summary>
        public int SlayDifficulty => _slayDifficulty;

        /// <summary>Wry-eerie Codex lore for this Monster.</summary>
        public string Lore => _lore;

        /// <summary>Codex art reference for this Monster.</summary>
        public Sprite Art => _art;

        /// <summary>
        /// Test-only initializer. Lets EditMode tests populate an in-memory instance
        /// (<c>ScriptableObject.CreateInstance&lt;MonsterDefinition&gt;()</c>) without a
        /// serialized <c>.asset</c>, mirroring how an authored asset arrives. Internal
        /// (never public): a <see cref="MonsterDefinition"/> is static data and is never
        /// mutated by production code. Parameters map 1:1 to the serialized fields in
        /// declaration order.
        /// </summary>
        internal void SetForTests(
            string id,
            string displayName,
            Rarity rarity,
            int captureDifficulty,
            int slayDifficulty,
            string lore,
            Sprite art)
        {
            _id = id;
            _displayName = displayName;
            _rarity = rarity;
            _captureDifficulty = captureDifficulty;
            _slayDifficulty = slayDifficulty;
            _lore = lore;
            _art = art;
        }
    }
}
