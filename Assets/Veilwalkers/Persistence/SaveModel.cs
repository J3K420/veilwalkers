using System.Collections.Generic;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// The single versioned save schema (AR-2): everything the game persists about a
    /// player lives in this one model, serialized as camelCase JSON and AES-encrypted
    /// on disk by <see cref="LocalProgressStore"/>. A plain POCO by design — NOT a
    /// ScriptableObject and NOT a MonoBehaviour — so it serializes with Newtonsoft on
    /// any thread and round-trips without Unity object identity.
    /// <para>
    /// Null-collection rule: every collection initializes to an empty collection and
    /// <see cref="CoerceNullCollections"/> repairs any null that arrives via
    /// deserialization, so a loaded model NEVER exposes a null collection and a saved
    /// file never contains one. A null <see cref="EncounterSnapshot"/> OBJECT is legal
    /// and means "no active encounter".
    /// </para>
    /// <para>
    /// Field ownership: the FIELDS for later stories land here so the schema is
    /// complete from v1, but their rules do not — the 20-credit first-launch grant is
    /// Story 1.7 (a fresh model has <c>Credits == 0</c>), the daily-claim rule is 1.8,
    /// the ad-reward caps are 1.9, and purchase reconciliation is 5.2.
    /// </para>
    /// </summary>
    public sealed class SaveModel
    {
        /// <summary>
        /// Schema version of this save (top-level, integer). Routed through
        /// <see cref="SaveMigrations"/> on load; <see cref="LocalProgressStore"/>
        /// always writes <see cref="SaveMigrations.CurrentVersion"/>.
        /// </summary>
        public int SchemaVersion { get; set; } = SaveMigrations.CurrentVersion;

        /// <summary>Monster Credit balance. Currency is always an integer.</summary>
        public int Credits { get; set; }

        /// <summary>
        /// Codex collection state keyed by stable monster id (<c>"monNN"</c>). A key's
        /// presence means the monster is discovered; the entry carries the per-monster
        /// scan/capture/slay/variant flags.
        /// </summary>
        public Dictionary<string, CodexEntryData> Codex { get; set; } =
            new Dictionary<string, CodexEntryData>();

        /// <summary>Lifetime experience points (Capture/Slay earn XP; Slay earns more).</summary>
        public int Xp { get; set; }

        /// <summary>Current player level derived from XP by Progression (Story 1.5).</summary>
        public int Level { get; set; }

        /// <summary>Remaining Strong Capture charges (XP-earned, never purchasable).</summary>
        public int StrongCaptureCharges { get; set; }

        /// <summary>Remaining Stability Boost charges (XP-earned, never purchasable).</summary>
        public int StabilityBoostCharges { get; set; }

        /// <summary>Remaining Nightveil Filter charges (XP-earned, never purchasable).</summary>
        public int NightveilFilterCharges { get; set; }

        /// <summary>
        /// The interrupted-encounter snapshot, or null when no encounter is active.
        /// This is the Persistence-owned data-only shape; the behavior-bearing
        /// <c>EncounterSnapshot</c> (Encounter tier, Epic 4) maps to/from it.
        /// </summary>
        public EncounterSnapshotData EncounterSnapshot { get; set; }

        /// <summary>Purchases granted locally but not yet acknowledged/reconciled (Story 5.2).</summary>
        public List<PendingPurchaseRecord> PendingPurchases { get; set; } =
            new List<PendingPurchaseRecord>();

        /// <summary>
        /// ISO-8601 UTC date of the last daily-reward claim, or null when never
        /// claimed. The claim rule itself is Story 1.8; only the field lands here.
        /// </summary>
        public string DailyClaim { get; set; }

        /// <summary>Ad-reward counters (field only; the caps land in Story 1.9).</summary>
        public AdRewardData AdReward { get; set; } = new AdRewardData();

        /// <summary>
        /// Write-once UTC day-bucket of the first time the player hit zero credits;
        /// <c>-1</c> means unset. Day-buckets are <c>long</c> integers derived via
        /// <c>IClock</c>, never serialized DateTimes.
        /// </summary>
        public long FirstZeroCreditDay { get; set; } = -1;

        /// <summary>
        /// Repair every null collection (top-level AND nested) to its empty default so
        /// callers never see null and a save never persists null. A null
        /// <see cref="EncounterSnapshot"/> is left null on purpose ("no active
        /// encounter"); when the snapshot is present its inner collections are coerced
        /// the same way. Null codex ENTRIES (crafted/corrupt input) are also replaced
        /// so codex reads cannot null-ref.
        /// </summary>
        public void CoerceNullCollections()
        {
            if (Codex == null)
            {
                Codex = new Dictionary<string, CodexEntryData>();
            }
            else
            {
                // Replace null entry values in place; collect first because mutating
                // a dictionary while enumerating it throws.
                List<string> nullKeys = null;
                foreach (KeyValuePair<string, CodexEntryData> pair in Codex)
                {
                    if (pair.Value == null)
                    {
                        (nullKeys = nullKeys ?? new List<string>()).Add(pair.Key);
                    }
                    else
                    {
                        pair.Value.CoerceNullCollections();
                    }
                }

                if (nullKeys != null)
                {
                    foreach (string key in nullKeys)
                    {
                        Codex[key] = new CodexEntryData();
                    }
                }
            }

            if (PendingPurchases == null)
            {
                PendingPurchases = new List<PendingPurchaseRecord>();
            }

            if (AdReward == null)
            {
                AdReward = new AdRewardData();
            }

            EncounterSnapshot?.CoerceNullCollections();
        }
    }
}
