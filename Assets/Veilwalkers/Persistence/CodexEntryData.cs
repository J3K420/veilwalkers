using System.Collections.Generic;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// Per-monster codex state stored in <see cref="SaveModel.Codex"/> under the
    /// monster's stable string id. Presence of the entry means "discovered"; the
    /// flags record how the player has interacted with the monster since.
    /// </summary>
    public sealed class CodexEntryData
    {
        /// <summary>The player has completed a scan of this monster.</summary>
        public bool Scanned { get; set; }

        /// <summary>The player has captured this monster at least once.</summary>
        public bool Captured { get; set; }

        /// <summary>The player has slain this monster at least once.</summary>
        public bool Slain { get; set; }

        /// <summary>
        /// The UTC calendar day this monster was FIRST discovered, as an ISO-8601
        /// <c>yyyy-MM-dd</c> string (the same shape as <see cref="SaveModel.DailyClaim"/> —
        /// never a serialized <c>DateTime</c>; all persisted time is an ISO string read
        /// through <c>IClock</c>, AR-18). Stamped ONCE by <c>CodexService</c> on the
        /// first-discovery create path (Story 2.5) and never rewritten — a later flag flip
        /// (e.g. Capture then Slay) leaves it untouched, because it is the <em>first</em>-
        /// discovered date. The Codex detail view (Story 2.5 AC-1) renders it.
        /// <para>
        /// <b>Nullable by design.</b> <c>null</c> is the legitimate "discovered before this
        /// field existed" state for pre-2.5 saves (the field is purely additive — a v2 save
        /// with no <c>discovered</c> key deserializes to null, no migration needed). The
        /// detail view renders a null date as empty/"—", never crashing.
        /// </para>
        /// </summary>
        public string Discovered { get; set; }

        /// <summary>
        /// Variant flags unlocked for this monster (stable string tags; Epic 2 defines
        /// the vocabulary). Never null — empty when no variants are unlocked.
        /// </summary>
        public List<string> VariantFlags { get; set; } = new List<string>();

        /// <summary>
        /// Repair a null <see cref="VariantFlags"/> back to empty and drop null
        /// elements (load validation). The <see cref="Discovered"/> scalar needs NO
        /// coercion: a null date string is its valid "unknown / pre-2.5" state, not a
        /// defect — only collections are coerced.
        /// </summary>
        public void CoerceNullCollections()
        {
            if (VariantFlags == null)
            {
                VariantFlags = new List<string>();
            }
            else
            {
                VariantFlags.RemoveAll(v => v == null);
            }
        }
    }
}
