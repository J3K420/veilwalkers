using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// A data-only view-model for the Codex detail panel (Story 2.5). The
    /// <see cref="CodexDetailPresenter"/> computes one of these per tapped slot; the thin
    /// <see cref="CodexDetailView"/> binds it and decides nothing. A <c>readonly struct</c>
    /// (value type) so a view-model handed to the view can never alias or corrupt the
    /// service's or catalog's state — the 2.2/2.3/2.4 defensive-read lesson.
    /// <para>
    /// <b>Unity-free by design</b> (the 2.4 <see cref="CodexSlot"/> decision): it carries NO
    /// <c>Sprite</c> and NO <c>Color</c>. It exposes <see cref="Id"/> and <see cref="Tier"/>;
    /// the view resolves the art and the rarity-tier badge COLOR from those at bind time
    /// (real art draw + UX-DR2 tier-color tokens + the chunky badge component are Epic 6).
    /// Keeping it Unity-free is what lets the presenter be headless-tested.
    /// </para>
    /// <para>
    /// <b>Two shapes (AC-1 vs AC-2).</b> A DISCOVERED view-model carries the full content
    /// (name, tier, stats, lore, first-discovered date). A NOT-discovered view-model
    /// (<see cref="NotDiscovered"/>) carries ONLY <see cref="Id"/> + the
    /// <see cref="NotDiscoveredCopy"/> — every content field is null/default. That is the
    /// hard AC-2 reveal-preservation rule: an undiscovered slot (both <c>?</c> blank and
    /// <c>???</c> silhouette) must leak NO name/art/lore/stats/tier.
    /// </para>
    /// </summary>
    public readonly struct CodexDetailViewModel
    {
        /// <summary>The diegetic copy shown for an undiscovered slot (UX-DR14 names this exact
        /// locked-slot voice). The view shows this INSTEAD of any content when
        /// <see cref="IsDiscovered"/> is false.</summary>
        public const string NotDiscoveredCopy = "Not yet discovered.";

        /// <summary>True when this describes a discovered Monster (show content); false for the
        /// <see cref="NotDiscovered"/> shape (show only <see cref="NotDiscoveredCopy"/>).</summary>
        public readonly bool IsDiscovered;

        /// <summary>The <c>monNN</c> id this view-model describes — present in BOTH shapes (the
        /// view may need it to resolve art for a discovered slot). NEVER an index or name.</summary>
        public readonly string Id;

        /// <summary>The display name. <c>null</c> for the not-discovered shape, OR for a
        /// discovered-but-unauthored id, OR a safe-placeholdered null on an authored asset (the
        /// presenter null-coalesces — see <see cref="CodexDetailPresenter"/>).</summary>
        public readonly string DisplayName;

        /// <summary>The rarity tier, drives the Epic-6 badge color. <c>null</c> for the
        /// not-discovered shape OR a discovered-but-unauthored id (no definition → no tier).</summary>
        public readonly Rarity? Tier;

        /// <summary>Base Capture difficulty (PROVISIONAL, OQ-9). Displayed as-is; 0 for the
        /// not-discovered shape or an unauthored id.</summary>
        public readonly int CaptureDifficulty;

        /// <summary>Base Slay difficulty (PROVISIONAL, OQ-9). Displayed as-is; 0 for the
        /// not-discovered shape or an unauthored id.</summary>
        public readonly int SlayDifficulty;

        /// <summary>Wry-eerie lore. <c>null</c>/empty for the not-discovered shape, an
        /// unauthored id, or a null-loremed authored asset (presenter renders it safely).</summary>
        public readonly string Lore;

        /// <summary>The first-discovered date (ISO <c>yyyy-MM-dd</c>), or <c>null</c> for the
        /// not-discovered shape OR a pre-2.5 save whose entry predates the field. The view
        /// renders a null date as empty/"—".</summary>
        public readonly string Discovered;

        private CodexDetailViewModel(
            bool isDiscovered, string id, string displayName, Rarity? tier,
            int captureDifficulty, int slayDifficulty, string lore, string discovered)
        {
            IsDiscovered = isDiscovered;
            Id = id;
            DisplayName = displayName;
            Tier = tier;
            CaptureDifficulty = captureDifficulty;
            SlayDifficulty = slayDifficulty;
            Lore = lore;
            Discovered = discovered;
        }

        /// <summary>Build the DISCOVERED shape — full content. Content fields may still be
        /// null (an unauthored discovered id, or a null name/lore on an authored asset); the
        /// presenter is responsible for any safe placeholdering before calling this. (Named
        /// <c>ForDiscovered</c>, not <c>Discovered</c>, to avoid colliding with the
        /// <see cref="IsDiscovered"/>-paired <see cref="Discovered"/> date field.)</summary>
        internal static CodexDetailViewModel ForDiscovered(
            string id, string displayName, Rarity? tier,
            int captureDifficulty, int slayDifficulty, string lore, string discovered) =>
            new CodexDetailViewModel(
                true, id, displayName, tier, captureDifficulty, slayDifficulty, lore, discovered);

        /// <summary>Build the NOT-DISCOVERED shape — <see cref="Id"/> + the copy ONLY, every
        /// content field null/default (the AC-2 anti-leak guarantee).</summary>
        internal static CodexDetailViewModel ForNotDiscovered(string id) =>
            new CodexDetailViewModel(false, id, null, null, 0, 0, null, null);
    }
}
