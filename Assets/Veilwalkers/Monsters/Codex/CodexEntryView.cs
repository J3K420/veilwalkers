using System.Collections.Generic;
using System.Collections.ObjectModel;
using Veilwalkers.Persistence;

namespace Veilwalkers.Monsters
{
    /// <summary>
    /// A read-only snapshot of a Monster's Codex entry, handed to callers (the 2.4/2.5
    /// detail views) instead of the live <see cref="CodexEntryData"/>. Defensive-copy
    /// discipline (the 2.2 <c>MonsterDatabase.Populated</c> CR lesson): a caller must
    /// never be able to mutate persisted state through a read.
    /// <para>
    /// <b>Scalars are value-copied</b> (so already safe) and <see cref="VariantFlags"/>
    /// is a FRESH copy wrapped read-only — NOT the live list typed as
    /// <see cref="IReadOnlyList{T}"/>. Exposing the live list would let a caller cast it
    /// back to <c>List&lt;string&gt;</c> and mutate the persisted entry (exactly the hole
    /// <c>MonsterDatabase.Populated</c> closed with <c>.Clone()</c>); a fresh copy closes
    /// it for real.
    /// </para>
    /// </summary>
    public readonly struct CodexEntryView
    {
        /// <summary>True when this view describes a discovered Monster; false for the
        /// <see cref="NotDiscovered"/> sentinel (the id has no Codex entry).</summary>
        public bool IsDiscovered { get; }

        /// <summary>The Monster id this view describes.</summary>
        public string Id { get; }

        public bool Scanned { get; }
        public bool Captured { get; }
        public bool Slain { get; }

        /// <summary>A defensive, read-only snapshot of the entry's variant flags. Never
        /// null (empty for a not-discovered sentinel or an entry with no variants), and
        /// never aliases the persisted list.</summary>
        public IReadOnlyList<string> VariantFlags { get; }

        private CodexEntryView(
            bool isDiscovered, string id, bool scanned, bool captured, bool slain,
            IReadOnlyList<string> variantFlags)
        {
            IsDiscovered = isDiscovered;
            Id = id;
            Scanned = scanned;
            Captured = captured;
            Slain = slain;
            VariantFlags = variantFlags;
        }

        /// <summary>
        /// Build a view over a stored entry, COPYING <paramref name="data"/>'s variant
        /// flags into a fresh read-only list so the view can never mutate the persisted
        /// entry. The scalar flags are value-copied.
        /// </summary>
        internal static CodexEntryView From(string id, CodexEntryData data)
        {
            // A fresh List copy (defensive against null elements too), wrapped read-only:
            // even casting VariantFlags back to its concrete type reaches only this copy,
            // never the persisted list.
            var flagsCopy = new List<string>();
            if (data.VariantFlags != null)
            {
                foreach (string flag in data.VariantFlags)
                {
                    flagsCopy.Add(flag);
                }
            }

            return new CodexEntryView(
                true, id, data.Scanned, data.Captured, data.Slain,
                new ReadOnlyCollection<string>(flagsCopy));
        }

        /// <summary>
        /// The sentinel for an id that has not been discovered: all flags false, empty
        /// variant list. Distinguished from a discovered entry by
        /// <see cref="IsDiscovered"/> == false.
        /// </summary>
        internal static CodexEntryView NotDiscovered(string id) =>
            new CodexEntryView(
                false, id, false, false, false,
                new ReadOnlyCollection<string>(new List<string>()));
    }
}
