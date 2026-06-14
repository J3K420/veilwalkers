using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// A data-only view-model for one Codex grid slot (Story 2.4). The
    /// <see cref="CodexGridPresenter"/> computes these; the view binds them and computes
    /// nothing. A <c>readonly struct</c> (value type) so a slot handed to the view can never
    /// alias or corrupt the presenter's or service's state — the 2.2/2.3 defensive-read lesson.
    /// <para>
    /// Deliberately carries NO <c>Sprite</c>/<c>Color</c>: art and tier color are Epic 6
    /// (UX-DR2/DR5). The view resolves those from <c>MonsterDatabase</c>/Epic-6 tokens at bind
    /// time, given <see cref="Id"/> and <see cref="Tier"/>.
    /// </para>
    /// </summary>
    public readonly struct CodexSlot
    {
        /// <summary>The stable <c>monNN</c> id of this slot (mon01..mon67). The KEY for every
        /// lookup — art, discovery, tier — never the display index.</summary>
        public readonly string Id;

        /// <summary>The reveal state the view must render.</summary>
        public readonly CodexSlotState State;

        /// <summary>
        /// The slot's authored <see cref="Rarity"/>, or <c>null</c> for a reserved-but-unauthored
        /// id (a <c>Blank</c> slot with no known tier, or a discovered-yet-unauthored id whose
        /// art the view falls back on). Drives the Epic-6 tier color.
        /// </summary>
        public readonly Rarity? Tier;

        /// <summary>
        /// The 1-based slot position (1..67), derived from the <c>monNN</c> id for stable
        /// ordering/display ONLY. NEVER used as a lookup key (the id is the key).
        /// </summary>
        public readonly int Index;

        public CodexSlot(string id, CodexSlotState state, Rarity? tier, int index)
        {
            Id = id;
            State = state;
            Tier = tier;
            Index = index;
        }
    }
}
