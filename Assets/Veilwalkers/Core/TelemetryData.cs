using System.Collections.Generic;

namespace Veilwalkers.Core
{
    /// <summary>
    /// The serializable local-telemetry payload (AR-15, Story 1.9), persisted to a
    /// SEPARATE <c>telemetry.json</c> via <see cref="ITelemetryStore"/> — NEVER in
    /// <c>SaveModel</c> (per-action/per-session counters are unbounded and would bloat
    /// the save + migration surface). A plain POCO (Newtonsoft-serializable, no Unity
    /// types) so it round-trips on any thread, exactly like <c>SaveModel</c>.
    /// <para>
    /// Two parts: <see cref="Counters"/> — cumulative per-event totals over a small,
    /// fixed key set; and <see cref="RecentEvents"/> — an ordered ring of recent dated
    /// records that the STORE trims (by count and by retention age) on every save, so
    /// the file stays bounded.
    /// </para>
    /// </summary>
    public sealed class TelemetryData
    {
        /// <summary>Cumulative count per stable event key.</summary>
        public Dictionary<string, long> Counters { get; set; } = new Dictionary<string, long>();

        /// <summary>
        /// Ordered, bounded log of recent telemetry events (oldest first). The
        /// <see cref="ITelemetryStore"/> impl trims this ring on save.
        /// </summary>
        public List<TelemetryEvent> RecentEvents { get; set; } = new List<TelemetryEvent>();

        /// <summary>
        /// Repair nulls that arrive via deserialization so callers never see a null
        /// collection and a save never persists one — mirrors <c>SaveModel</c>'s
        /// null-collection rule. Null elements in <see cref="RecentEvents"/> are
        /// dropped so downstream iteration cannot null-ref.
        /// </summary>
        public void CoerceNullCollections()
        {
            if (Counters == null)
            {
                Counters = new Dictionary<string, long>();
            }

            if (RecentEvents == null)
            {
                RecentEvents = new List<TelemetryEvent>();
            }
            else
            {
                RecentEvents.RemoveAll(e => e == null);
            }
        }
    }

    /// <summary>
    /// One recorded telemetry event: a stable <see cref="Evt"/> key, the
    /// <see cref="Amount"/> counted, and the UTC <see cref="DayBucket"/> it occurred in
    /// (the same day-bucket-long the ad-cap and first-zero recorder use). Dated so the
    /// store can trim by retention age.
    /// </summary>
    public sealed class TelemetryEvent
    {
        public string Evt { get; set; }
        public long Amount { get; set; }
        public long DayBucket { get; set; }
    }
}
