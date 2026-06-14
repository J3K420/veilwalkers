using System;
using Veilwalkers.Core;
using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// Pure-logic read model for the Codex detail panel (Story 2.5). Given a tapped slot's
    /// <c>monNN</c> id, it produces ONE <see cref="CodexDetailViewModel"/>: the full content
    /// for a discovered Monster (AC-1: name, rarity tier, stats, lore, first-discovered date)
    /// or the bare "Not yet discovered." shape for an undiscovered slot (AC-2: copy only, no
    /// content leak). NO Unity types, NO <c>MonoBehaviour</c>, NO service-locator reads —
    /// ctor-injected (AR-4), headless-testable. The thin <see cref="CodexDetailView"/> binds
    /// the output to widgets and resolves the art/badge-color (Epic 6) from the id + tier.
    /// <para>
    /// <b>One collaborator (the 2.4 "one home" rule).</b> It takes ONLY <see cref="CodexService"/>
    /// and routes both the discovery state (<c>GetEntry</c>) AND the catalog content
    /// (<c>GetDefinition</c>) through it — it never touches <c>MonsterDatabase</c> directly, so
    /// the catalog read has a single home.
    /// </para>
    /// <para>
    /// <b>Null-safety (settles the 2.1/2.2 deferrals at the render layer).</b>
    /// <c>MonsterDefinition.DisplayName</c>/<c>Lore</c>/<c>Art</c> are null-unguarded; a
    /// discovered-but-unauthored id has NO definition at all. This presenter renders all of
    /// those safely (placeholder name, empty lore, null tier/art) and NEVER throws on null
    /// content — a UI read must not crash the panel.
    /// </para>
    /// </summary>
    public sealed class CodexDetailPresenter
    {
        /// <summary>The safe placeholder shown when an authored asset (or an unauthored
        /// discovered id) has no display name. Kept here so a future Epic-6 polish pass has one
        /// home to change it.</summary>
        public const string UnnamedPlaceholder = "(unnamed)";

        private readonly CodexService _codex;

        public CodexDetailPresenter(CodexService codex)
        {
            _codex = codex ?? throw new ArgumentNullException(nameof(codex));
        }

        /// <summary>
        /// Build the detail view-model for <paramref name="id"/>.
        /// <list type="bullet">
        /// <item>Invalid / null id → the NOT-discovered shape + a <c>GameLog.Warn</c> (a UI read
        ///   must not throw on a bad id, UNLIKE <c>RecordDiscoveryAsync</c> whose throw guards an
        ///   Epic-4 caller bug — the asymmetry is intentional: a read degrades, a write asserts).</item>
        /// <item>Not discovered (no Codex entry — covers BOTH <c>?</c> blank and <c>???</c>
        ///   silhouette grid states) → the NOT-discovered shape: <see cref="CodexDetailViewModel.Id"/>
        ///   + the copy ONLY, every content field null/default (the AC-2 anti-leak guarantee).</item>
        /// <item>Discovered → the full content shape. Content is null-coalesced: a null name →
        ///   <see cref="UnnamedPlaceholder"/>, a null lore → empty string, no definition (an
        ///   unauthored discovered id) → null tier/stats-zero/empty lore but a real id + date.</item>
        /// </list>
        /// Pure read — no lock (single-threaded gameplay, the 2.2/2.3/2.4 rationale).
        /// </summary>
        public CodexDetailViewModel BuildDetail(string id)
        {
            if (string.IsNullOrEmpty(id) || !MonsterDatabase.IsValidMonsterId(id))
            {
                // A read must not crash the panel on a malformed id; degrade to "not discovered"
                // (the reveal-preserving safe default) and warn for diagnosis.
                GameLog.Warn(
                    $"CodexDetailPresenter: '{id}' is not a valid Monster id (expected " +
                    $"mon01..mon{MonsterDatabase.UniverseCount:00}); showing the not-discovered panel.");
                return CodexDetailViewModel.ForNotDiscovered(id);
            }

            CodexEntryView entry = _codex.GetEntry(id);
            if (!entry.IsDiscovered)
            {
                // AC-2: copy only, NO content — preserves the silhouette/blank reveal.
                return CodexDetailViewModel.ForNotDiscovered(id);
            }

            // Discovered. Pull the static content from the catalog (through CodexService — one
            // home). A discovered-but-unauthored id has no definition: render a content-light
            // discovered panel (valid id + date, null tier/name/lore) rather than throwing —
            // mirrors the grid's "discovered unauthored id still shows Discovered" rule.
            MonsterDefinition def = _codex.GetDefinition(id);
            if (def == null)
            {
                return CodexDetailViewModel.ForDiscovered(
                    id, displayName: null, tier: null,
                    captureDifficulty: 0, slayDifficulty: 0, lore: null,
                    discovered: entry.Discovered);
            }

            // Authored. Null-coalesce the null-unguarded content fields (the 2.1/2.2 deferral,
            // settled here): a null name becomes the placeholder; null lore becomes empty. The
            // tier/stats come from the definition as-is (stats are PROVISIONAL OQ-9 — displayed,
            // not validated). Art is NOT carried (Unity-free view-model); the view resolves it
            // from the id at bind time.
            return CodexDetailViewModel.ForDiscovered(
                id,
                displayName: def.DisplayName ?? UnnamedPlaceholder,
                tier: def.Rarity,
                captureDifficulty: def.CaptureDifficulty,
                slayDifficulty: def.SlayDifficulty,
                lore: def.Lore ?? string.Empty,
                discovered: entry.Discovered);
        }
    }
}
