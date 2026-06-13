using System.Threading.Tasks;

namespace Veilwalkers.Core
{
    /// <summary>
    /// The capped-ring storage seam for local telemetry (AR-15, Story 1.9). Mirrors
    /// <c>IProgressStore</c>'s async shape, but persists a SEPARATE <c>telemetry.json</c>
    /// (never <c>SaveModel</c>) and bounds its size: the impl trims the
    /// <see cref="TelemetryData.RecentEvents"/> ring on every <see cref="SaveAsync"/>
    /// (by a hard max count and by a retention-day age), so per-action/per-session
    /// counters cannot grow unbounded.
    /// <para>
    /// Swappable (Local now → Firebase later) by contract: no path, encryption, or
    /// transport detail leaks here. All members are safe to call from any thread.
    /// </para>
    /// </summary>
    public interface ITelemetryStore
    {
        /// <summary>
        /// Load the persisted telemetry, or a fresh empty <see cref="TelemetryData"/>
        /// when none exists yet. Unlike the save store, a missing or unreadable
        /// telemetry file is NOT fatal — telemetry is a best-effort diagnostic, so a
        /// load failure yields empty data rather than throwing into a call site.
        /// </summary>
        Task<TelemetryData> LoadAsync();

        /// <summary>
        /// Persist the telemetry atomically (temp file + swap, off the main thread),
        /// trimming the capped ring first. Concurrent calls are serialized internally.
        /// </summary>
        Task SaveAsync(TelemetryData data);

        /// <summary>Whether a persisted telemetry file currently exists.</summary>
        bool Exists();

        /// <summary>Delete the persisted telemetry file (best-effort reset).</summary>
        Task DeleteAsync();
    }
}
