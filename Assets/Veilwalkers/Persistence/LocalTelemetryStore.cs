using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Veilwalkers.Core;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// <see cref="ITelemetryStore"/> over a single PLAINTEXT <c>telemetry.json</c> in
    /// the directory supplied at construction (Story 1.9, AR-15). Distinct file from
    /// the encrypted <c>save.dat</c> — telemetry is a LOCAL DIAGNOSTIC meant for testers
    /// to read/edit during the balancing pass, so it is deliberately NOT AES-encrypted
    /// (the architecture encrypts the save, which holds tamper-sensitive currency, but
    /// not telemetry, which holds only diagnostic counters + event keys). Bootstrap
    /// passes <c>Application.persistentDataPath</c>; the store itself never calls that
    /// API, which keeps it testable against a temp directory.
    /// <para>
    /// Write pipeline mirrors <see cref="LocalProgressStore"/>: serialize on the calling
    /// thread → temp file IN THE SAME DIRECTORY → flush to disk → atomic swap; a
    /// semaphore serializes concurrent operations. The ONE difference (besides plaintext)
    /// is the CAPPED RING: every save trims <see cref="TelemetryData.RecentEvents"/> to a
    /// bounded size — by a hard <see cref="MaxRetainedEvents"/> backstop AND by dropping
    /// events older than <see cref="_retentionDays"/> UTC days — so the file cannot grow
    /// unbounded.
    /// </para>
    /// </summary>
    public sealed class LocalTelemetryStore : ITelemetryStore
    {
        private const string TelemetryFileName = "telemetry.json";
        private const string TempFileName = "telemetry.json.tmp";

        /// <summary>Absolute hard cap on retained recent events, regardless of dates.</summary>
        public const int MaxRetainedEvents = 500;

        private readonly string _directory;
        private readonly string _telemetryPath;
        private readonly string _tempPath;
        private readonly IClock _clock;
        private readonly int _retentionDays;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);

        /// <param name="directory">
        /// Absolute directory the telemetry file lives in (production:
        /// <c>Application.persistentDataPath</c>; tests: a temp directory).
        /// </param>
        /// <param name="clock">Time seam, used to age-trim the ring (AR-18).</param>
        /// <param name="retentionDays">
        /// Drop recent-event entries older than this many UTC days (from
        /// <c>EconomyConfig.TelemetryRetentionDays</c>, injected by Bootstrap — the store
        /// does not read EconomyConfig directly, Persistence does not reference Economy).
        /// </param>
        public LocalTelemetryStore(string directory, IClock clock, int retentionDays)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException(
                    "Telemetry directory must be a non-empty path.", nameof(directory));
            }

            _directory = directory;
            _telemetryPath = Path.Combine(directory, TelemetryFileName);
            _tempPath = Path.Combine(directory, TempFileName);
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _retentionDays = retentionDays < 0 ? 0 : retentionDays;
        }

        /// <inheritdoc />
        public bool Exists() => File.Exists(_telemetryPath);

        /// <inheritdoc />
        public async Task<TelemetryData> LoadAsync()
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(LoadCore).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task SaveAsync(TelemetryData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            // Snapshot on the calling thread (it owns the data): trim the ring, then
            // serialize, before any thread hop, so a concurrent mutation can never tear
            // this write. Trimming here (not in the data shape) keeps the cap a STORE
            // policy, swappable with the impl.
            data.CoerceNullCollections();
            TrimRing(data);
            string json = JsonConvert.SerializeObject(data, SaveJson.Settings);

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() => SaveCore(json)).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync()
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    DeleteIfExists(_tempPath);
                    DeleteIfExists(_telemetryPath);
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// Bound the recent-event ring: drop entries older than the retention age, then
        /// hard-cap the count (keeping the newest). Mutates <paramref name="data"/> in
        /// place. The cumulative <see cref="TelemetryData.Counters"/> map is NOT trimmed
        /// (it is a small fixed-key set, the durable diagnostic totals).
        /// </summary>
        private void TrimRing(TelemetryData data)
        {
            List<TelemetryEvent> events = data.RecentEvents;

            if (_retentionDays > 0)
            {
                long todayBucket = _clock.UtcNow.Date.Ticks / TimeSpan.TicksPerDay;
                long oldestKept = todayBucket - _retentionDays;
                events.RemoveAll(e => e.DayBucket < oldestKept);
            }

            // Hard backstop: keep only the newest MaxRetainedEvents. The store accepts any
            // externally-supplied data (SaveAsync is public) AND the plaintext file invites
            // hand-editing, so we do NOT assume the list is already chronological — sort by
            // DayBucket (stable, so same-day insertion order is preserved) before dropping
            // the front, guaranteeing the genuinely-oldest entries are the ones removed.
            int overflow = events.Count - MaxRetainedEvents;
            if (overflow > 0)
            {
                StableSortByDayBucket(events);
                events.RemoveRange(0, overflow);
            }
        }

        /// <summary>
        /// Sort <paramref name="events"/> ascending by <see cref="TelemetryEvent.DayBucket"/>,
        /// STABLY (entries within the same day keep their relative order). LINQ's
        /// <c>OrderBy</c> is documented stable; <c>List.Sort</c> is not, which would scramble
        /// same-day insertion order.
        /// </summary>
        private static void StableSortByDayBucket(List<TelemetryEvent> events)
        {
            List<TelemetryEvent> sorted = new List<TelemetryEvent>(events.Count);
            sorted.AddRange(System.Linq.Enumerable.OrderBy(events, e => e.DayBucket));
            events.Clear();
            events.AddRange(sorted);
        }

        private TelemetryData LoadCore()
        {
            if (!File.Exists(_telemetryPath))
            {
                // Telemetry is best-effort: a missing file is the normal first-run state,
                // not an error — return empty data rather than throwing.
                return new TelemetryData();
            }

            try
            {
                string json = File.ReadAllText(_telemetryPath, Encoding.UTF8);
                TelemetryData data =
                    JsonConvert.DeserializeObject<TelemetryData>(json, SaveJson.Settings)
                    ?? new TelemetryData();
                data.CoerceNullCollections();
                return data;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                // A corrupt/unreadable diagnostic file must never crash the game or wipe
                // the save — start the telemetry fresh and move on (Warn, not Error).
                GameLog.Warn(
                    $"LocalTelemetryStore: telemetry.json unreadable, starting fresh. {ex.Message}");
                return new TelemetryData();
            }
        }

        private void SaveCore(string json)
        {
            // Plaintext UTF-8 (NOT AesCrypto.Encrypt — telemetry is an inspectable local
            // diagnostic), written via the same temp-file + flush + atomic-swap pipeline
            // the save store uses so a kill mid-write never corrupts the file.
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Directory.CreateDirectory(_directory);
            DeleteIfExists(_tempPath);

            using (var stream = new FileStream(
                _tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_telemetryPath))
            {
                File.Replace(_tempPath, _telemetryPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(_tempPath, _telemetryPath);
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
