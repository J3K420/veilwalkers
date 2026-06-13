using System;
using System.Threading.Tasks;
using Veilwalkers.Core;

namespace Veilwalkers.App
{
    /// <summary>
    /// The App-layer <see cref="ITelemetrySink"/> impl (Story 1.9, AR-15): records
    /// diagnostic counters to a capped-ring <c>telemetry.json</c> via
    /// <see cref="ITelemetryStore"/>. Behind the Core <see cref="ITelemetrySink"/> seam,
    /// so a Phase-2 Firebase sink swaps in without moving any <see cref="Count"/> call
    /// site.
    /// <para>
    /// It holds an in-memory <see cref="TelemetryData"/> (lazily loaded from the store
    /// on first use) and WRITE-THROUGH persists on each <see cref="Count"/> — the
    /// simplest correct shape for a stub; the store's atomic write keeps it crash-safe
    /// and trims the ring on each save. It NEVER touches <c>SaveModel</c>/<c>SaveService</c>
    /// (it is constructed with no save dependency at all, so it structurally cannot).
    /// </para>
    /// </summary>
    public sealed class LocalTelemetrySink : ITelemetrySink
    {
        private readonly ITelemetryStore _store;
        private readonly IClock _clock;
        /// <summary>Hard cap on the live in-memory recent-event ring (matches the store's file cap).</summary>
        private const int LiveEventCap = 500;

        private readonly object _gate = new object();
        private TelemetryData _data;

        public LocalTelemetrySink(ITelemetryStore store, IClock clock)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <inheritdoc />
        public void Count(string evt, long amount = 1)
        {
            if (string.IsNullOrEmpty(evt))
            {
                // A diagnostic sink must never throw into a gameplay call site; an empty
                // key is a programmer mistake worth a Warn, not a crash.
                GameLog.Warn("LocalTelemetrySink.Count called with an empty event key; ignored.");
                return;
            }

            if (amount <= 0)
            {
                // "Count" means counting OCCURRENCES — a non-positive amount is meaningless
                // (zero) or corrupting (negative would let a counter decrease). Ignore it,
                // never throw into the call site. (A future signed-delta metric would be a
                // different method, not this one.)
                GameLog.Warn(
                    $"LocalTelemetrySink.Count('{evt}', {amount}): amount must be positive; ignored.");
                return;
            }

            // Mutate the shared in-memory data and snapshot it under a lock: Count may be
            // called from multiple threads, and the store's TrimRing mutates the same lists
            // synchronously — without this gate a concurrent Add could interleave with a
            // RemoveRange and corrupt the List<TelemetryEvent>. We persist a COPY so the
            // store trims its own instance, never our live one.
            TelemetryData snapshot;
            lock (_gate)
            {
                EnsureLoaded();

                _data.Counters.TryGetValue(evt, out long current);
                _data.Counters[evt] = current + amount;
                _data.RecentEvents.Add(new TelemetryEvent
                {
                    Evt = evt,
                    Amount = amount,
                    DayBucket = DayBucket.For(_clock.UtcNow),
                });

                // Keep the live in-memory ring bounded too (the store caps the FILE on each
                // save; this caps our long-lived copy so a long session can't grow it without
                // limit). Drop oldest-first, matching the store's hard cap.
                int overflow = _data.RecentEvents.Count - LiveEventCap;
                if (overflow > 0)
                {
                    _data.RecentEvents.RemoveRange(0, overflow);
                }

                snapshot = CloneData(_data);
            }

            // PERF: write-through means one atomic file write per Count. Fine for the
            // sparse call sites 1.9 reserves (first-zero / session markers); a future
            // high-frequency caller (e.g. per-frame) should buffer + flush instead. Kept
            // fire-and-forget with a fault observer so a diagnostic I/O hiccup never
            // surfaces in a gameplay caller.
            _store.SaveAsync(snapshot).ContinueWith(
                t => GameLog.Warn(
                    "LocalTelemetrySink: telemetry write failed (diagnostic only): " +
                    t.Exception?.GetBaseException().Message),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>Deep-enough copy so the store can trim its instance without racing ours.</summary>
        private static TelemetryData CloneData(TelemetryData data)
        {
            var copy = new TelemetryData();
            foreach (System.Collections.Generic.KeyValuePair<string, long> kv in data.Counters)
            {
                copy.Counters[kv.Key] = kv.Value;
            }

            foreach (TelemetryEvent e in data.RecentEvents)
            {
                copy.RecentEvents.Add(new TelemetryEvent { Evt = e.Evt, Amount = e.Amount, DayBucket = e.DayBucket });
            }

            return copy;
        }

        private void EnsureLoaded()
        {
            if (_data != null)
            {
                return;
            }

            try
            {
                // First-use load is synchronous on purpose: Count is a fire-and-forget
                // call site and the store's load is cheap (a small local file). A load
                // failure yields empty data (the store never throws for a missing file).
                _data = _store.LoadAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                GameLog.Warn(
                    $"LocalTelemetrySink: telemetry load failed, starting fresh. {ex.Message}");
                _data = new TelemetryData();
            }

            _data.CoerceNullCollections();
        }
    }
}
