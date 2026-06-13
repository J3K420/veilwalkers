using Newtonsoft.Json.Linq;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// The single place save-schema versions are routed and (in future) chained
    /// v1→v2→… . Deliberately boring at v1: its existence and the routing are the
    /// contract (AR-2), not speculative migrations.
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>The schema version this build reads and writes.</summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// Bring a raw save document from <paramref name="fromVersion"/> up to
        /// <see cref="CurrentVersion"/>. A no-op at v1. Unknown versions — older than
        /// the first shipped schema or newer than this build understands — throw
        /// <see cref="SaveCorruptException"/> so the load surfaces recovery instead of
        /// misreading the document.
        /// </summary>
        public static JObject Migrate(JObject save, int fromVersion)
        {
            if (save == null)
            {
                throw new SaveCorruptException("Save document is missing (null) — cannot migrate.");
            }

            if (fromVersion < 1 || fromVersion > CurrentVersion)
            {
                throw new SaveCorruptException(
                    $"Unknown save schemaVersion {fromVersion}; this build understands 1..{CurrentVersion}.");
            }

            // Future versions chain here, one step at a time:
            if (fromVersion < 2)
            {
                MigrateV1ToV2(save);
            }
            //   if (fromVersion < 3) { MigrateV2ToV3(save); }

            save["schemaVersion"] = CurrentVersion;
            return save;
        }

        /// <summary>
        /// v1 → v2 (Story 1.7): introduce the <c>startingCreditsGranted</c> marker and
        /// set it TRUE for every existing save. A v1 save belongs to a player who
        /// already launched before the first-launch grant existed and already holds
        /// their credits; marking it granted prevents a surprise +20 re-grant on the
        /// update that ships this schema. A genuinely fresh install has no save file at
        /// all, so it never passes through here — it starts from a default v2 model
        /// with the marker false and receives the grant. The migration operates on the
        /// raw JSON before deserialization (see <c>LocalProgressStore.LoadAsync</c>).
        /// </summary>
        private static void MigrateV1ToV2(JObject save)
        {
            save["startingCreditsGranted"] = true;
        }
    }
}
