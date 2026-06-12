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
        /// Variant flags unlocked for this monster (stable string tags; Epic 2 defines
        /// the vocabulary). Never null — empty when no variants are unlocked.
        /// </summary>
        public List<string> VariantFlags { get; set; } = new List<string>();

        /// <summary>
        /// Repair a null <see cref="VariantFlags"/> back to empty and drop null
        /// elements (load validation).
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
