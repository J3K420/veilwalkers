using System;
using System.Collections.Generic;
using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// Pure-logic read model for the Codex 67-grid (Story 2.4). Computes each of the 67 slots'
    /// reveal state (<see cref="CodexSlotState.Discovered"/> / <c>???</c>
    /// <see cref="CodexSlotState.Silhouette"/> / <c>?</c> <see cref="CodexSlotState.Blank"/>)
    /// from <see cref="CodexService"/>'s discovery data plus the Dread-Ladder begun-tier rule,
    /// plus the X/67 numbers. NO Unity types, NO <c>MonoBehaviour</c>, NO service-locator reads —
    /// ctor-injected (AR-4), headless-testable. The thin <see cref="CodexGridView"/> binds this
    /// output to widgets and rebuilds on <c>OnMonsterDiscovered</c>.
    /// <para>
    /// Visual rendering (real art, tier color, the tier-tinted caught stamp, the silhouette→art
    /// flip + count-tick animation, the chunky 3-state slot component) is Epic 6 (UX-DR2/DR5/
    /// DR15) — this presenter only decides <em>which state</em> each slot is in and exposes
    /// everything Epic 6 needs (per-slot state + tier + the before/after counts).
    /// </para>
    /// </summary>
    public sealed class CodexGridPresenter
    {
        private readonly CodexService _codex;

        public CodexGridPresenter(CodexService codex)
        {
            _codex = codex ?? throw new ArgumentNullException(nameof(codex));
        }

        /// <summary>The discovered count — the "X" in "X / 67". Passed through from
        /// <see cref="CodexService.DiscoveredCount"/>; the view renders/animates it.</summary>
        public int DiscoveredCount => _codex.DiscoveredCount;

        /// <summary>The universe size — the "/ 67". Passed through from
        /// <see cref="CodexService.UniverseCount"/>.</summary>
        public int UniverseCount => _codex.UniverseCount;

        /// <summary>The "X / 67" label, unlocalized — keeps the view dumber. Epic 6 owns any
        /// richer copy/voice; this is the minimal numeric form.</summary>
        public string ProgressLabel => $"{DiscoveredCount} / {UniverseCount}";

        /// <summary>
        /// Build the 67 slots, one per universe id <c>mon01</c>..<c>mon67</c>, each classified
        /// into its reveal state. A PURE RECOMPUTE: it reads current state and returns a fresh
        /// list every call; there is no internal mutable slot cache. The view calls this on first
        /// bind and again whenever <see cref="CodexService.OnMonsterDiscovered"/> fires — that is
        /// how AC-3's flip (the discovered slot → full art + the count tick) and AC-2's
        /// silhouette-upgrade (a discovery raising the high-water mark flips lower blanks to
        /// <c>???</c>) both happen from a single rebuild. The presenter does NOT subscribe to the
        /// event (it has no lifecycle); the view owns the OnEnable/OnDisable subscription.
        /// <para>
        /// Classification per id (discovered always wins):
        /// <list type="bullet">
        /// <item><b>discovered</b> → <see cref="CodexSlotState.Discovered"/>; <c>Tier</c> may be
        ///   <c>null</c> if the discovered id is unauthored (the view falls back).</item>
        /// <item><b>unauthored</b> (no known tier) → <see cref="CodexSlotState.Blank"/>,
        ///   <c>Tier = null</c> — can't silhouette without a tier.</item>
        /// <item><b>authored + tier begun</b> → <see cref="CodexSlotState.Silhouette"/>.</item>
        /// <item>otherwise (authored, tier not begun) → <see cref="CodexSlotState.Blank"/>.</item>
        /// </list>
        /// </para>
        /// </summary>
        public IReadOnlyList<CodexSlot> BuildSlots()
        {
            // One snapshot of the discovered ids into a HashSet for O(1) membership — read ONCE,
            // not IsDiscovered 67×. GetDiscoveredIds() is the settled-2.3 snapshot method.
            var discovered = new HashSet<string>(_codex.GetDiscoveredIds());

            int universe = _codex.UniverseCount;
            var slots = new List<CodexSlot>(universe);

            for (int i = 1; i <= universe; i++)
            {
                string id = "mon" + i.ToString("00");
                Rarity? tier = _codex.GetTier(id);

                CodexSlotState state;
                if (discovered.Contains(id))
                {
                    // Discovery is the strongest signal — full art even if the id is unauthored
                    // (Tier stays null then; the view falls back). Discovered always wins.
                    state = CodexSlotState.Discovered;
                }
                else if (!tier.HasValue)
                {
                    // No authored definition → no tier to silhouette → blank (the locked
                    // reserved-slot rule). Grows to ??? organically as content is authored.
                    state = CodexSlotState.Blank;
                }
                else if (_codex.IsTierBegun(tier.Value))
                {
                    // Authored and in a begun tier (at or one tier above the high-water).
                    state = CodexSlotState.Silhouette;
                }
                else
                {
                    // Authored but the tier is not yet reached — preserve the reveal.
                    state = CodexSlotState.Blank;
                }

                slots.Add(new CodexSlot(id, state, tier, i));
            }

            return slots;
        }
    }
}
