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
        public const int CurrentVersion = 1;

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
            //   if (fromVersion < 2) { MigrateV1ToV2(save); }
            //   if (fromVersion < 3) { MigrateV2ToV3(save); }
            save["schemaVersion"] = CurrentVersion;
            return save;
        }
    }
}
