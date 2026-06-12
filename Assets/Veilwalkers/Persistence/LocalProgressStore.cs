using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Veilwalkers.Core;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// <see cref="IProgressStore"/> over a single encrypted file
    /// (<c>save.dat</c>) in the directory supplied at construction. Bootstrap passes
    /// <c>Application.persistentDataPath</c> — the store itself NEVER calls that API
    /// (it is main-thread-only), which is also what makes the store testable against
    /// a temp directory.
    /// <para>
    /// Write pipeline (the heart of AC-2): serialize (camelCase JSON) → encrypt+MAC →
    /// write to a temp file IN THE SAME DIRECTORY → flush to disk → atomically swap
    /// over <c>save.dat</c>. Same-directory temp guarantees same volume, so the swap
    /// is an atomic rename: a kill before the swap leaves the prior save untouched, a
    /// kill after leaves the new valid save. All heavy work runs inside
    /// <c>Task.Run</c>; no UnityEngine API is touched off the main thread. Concurrent
    /// operations are serialized by a semaphore so two writers can never interleave
    /// on the temp file.
    /// </para>
    /// </summary>
    public sealed class LocalProgressStore : IProgressStore
    {
        private const string SaveFileName = "save.dat";
        private const string TempFileName = "save.dat.tmp";

        private readonly string _directory;
        private readonly string _savePath;
        private readonly string _tempPath;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);

        /// <param name="directory">
        /// Absolute directory the save file lives in (production:
        /// <c>Application.persistentDataPath</c>, read on the main thread by Bootstrap;
        /// tests: a temp directory).
        /// </param>
        public LocalProgressStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException(
                    "Save directory must be a non-empty path.", nameof(directory));
            }

            _directory = directory;
            _savePath = Path.Combine(directory, SaveFileName);
            _tempPath = Path.Combine(directory, TempFileName);
        }

        /// <inheritdoc />
        public bool Exists() => File.Exists(_savePath);

        /// <inheritdoc />
        public async Task<SaveModel> LoadAsync()
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
        public async Task SaveAsync(SaveModel model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() => SaveCore(model)).ConfigureAwait(false);
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
                    DeleteIfExists(_savePath);
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private SaveModel LoadCore()
        {
            // A stale temp file is a write that never finished — it is never the
            // save and is cleaned up so it cannot linger across sessions.
            CleanUpStaleTemp();

            if (!File.Exists(_savePath))
            {
                throw new FileNotFoundException(
                    "No save file exists; check Exists() before loading.", _savePath);
            }

            byte[] blob = File.ReadAllBytes(_savePath);
            byte[] plaintext = AesCrypto.Decrypt(blob); // throws SaveCorruptException on tamper
            string json = Encoding.UTF8.GetString(plaintext);

            JObject document;
            try
            {
                // Not JObject.Parse: the reader must keep ISO-8601 strings as strings
                // (DateParseHandling.None), matching SaveJson.Settings.
                using (var stringReader = new StringReader(json))
                using (var jsonReader = new JsonTextReader(stringReader)
                {
                    DateParseHandling = DateParseHandling.None,
                })
                {
                    document = JObject.Load(jsonReader);
                }
            }
            catch (JsonException ex)
            {
                throw new SaveCorruptException("Save payload is not valid JSON.", ex);
            }

            JToken versionToken = document["schemaVersion"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
            {
                throw new SaveCorruptException("Save document has no integer schemaVersion.");
            }

            document = SaveMigrations.Migrate(document, versionToken.Value<int>());

            SaveModel model;
            try
            {
                model = document.ToObject<SaveModel>(SaveJson.CreateSerializer());
            }
            catch (JsonException ex)
            {
                throw new SaveCorruptException("Save document does not match the save schema.", ex);
            }

            if (model == null)
            {
                throw new SaveCorruptException("Save document deserialized to nothing.");
            }

            model.SchemaVersion = SaveMigrations.CurrentVersion;
            model.CoerceNullCollections();
            return model;
        }

        private void SaveCore(SaveModel model)
        {
            // Never persist a null collection, and always write the current schema.
            model.CoerceNullCollections();
            model.SchemaVersion = SaveMigrations.CurrentVersion;

            string json = JsonConvert.SerializeObject(model, SaveJson.Settings);
            byte[] blob = AesCrypto.Encrypt(Encoding.UTF8.GetBytes(json));

            Directory.CreateDirectory(_directory);
            DeleteIfExists(_tempPath);

            // Write the full payload to the temp file and force it to disk BEFORE the
            // swap — without the flush, the rename could land before the data does and
            // a power loss would truncate the "new" save.
            using (var stream = new FileStream(
                _tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(blob, 0, blob.Length);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_savePath))
            {
                File.Replace(_tempPath, _savePath, destinationBackupFileName: null);
            }
            else
            {
                // First-ever save: no destination to replace. Plain two-arg Move is
                // still a same-volume atomic rename.
                File.Move(_tempPath, _savePath);
            }
        }

        private void CleanUpStaleTemp()
        {
            try
            {
                DeleteIfExists(_tempPath);
            }
            catch (IOException ex)
            {
                // A locked/undeletable stale temp must not block loading the valid
                // save; it will be replaced by the next write anyway.
                GameLog.Warn($"Could not clean up stale save temp file: {ex.Message}");
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
