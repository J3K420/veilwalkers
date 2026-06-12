using System;
using System.IO;
using System.Text;
using Veilwalkers.Core.Contracts;
using Veilwalkers.Persistence;
using UnityEngine;

namespace Veilwalkers.Persistence.Tests
{
    /// <summary>
    /// Shared helpers for the Persistence fixtures: per-test temp directories (the
    /// stores are NEVER pointed at <c>Application.persistentDataPath</c> — the ctor
    /// parameter exists exactly so tests can use a temp dir), crafted save files
    /// (arbitrary JSON encrypted exactly like a real save), and a fully-populated
    /// model for round-trip assertions.
    /// </summary>
    internal static class TestSaveFiles
    {
        public static string CreateTempDir()
        {
            string dir = Path.Combine(
                Path.GetTempPath(), "veilwalkers-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void DeleteTempDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS temp dir is purged eventually anyway.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static string SavePath(string dir) => Path.Combine(dir, "save.dat");

        public static string TempPath(string dir) => Path.Combine(dir, "save.dat.tmp");

        /// <summary>Encrypt arbitrary JSON exactly like a real save and write it to disk.</summary>
        public static void WriteCraftedSave(string dir, string json)
        {
            File.WriteAllBytes(SavePath(dir), AesCrypto.Encrypt(Encoding.UTF8.GetBytes(json)));
        }

        /// <summary>Decrypt the on-disk save back to its raw JSON string.</summary>
        public static string ReadSaveJson(string dir)
        {
            return Encoding.UTF8.GetString(AesCrypto.Decrypt(File.ReadAllBytes(SavePath(dir))));
        }

        /// <summary>A model with every field populated, for round-trip equality checks.</summary>
        public static SaveModel BuildFullModel()
        {
            return new SaveModel
            {
                Credits = 42,
                Codex =
                {
                    ["mon01"] = new CodexEntryData
                    {
                        Scanned = true,
                        Captured = true,
                        Slain = false,
                        VariantFlags = { "shadow" },
                    },
                    ["mon07"] = new CodexEntryData { Scanned = true },
                },
                Xp = 120,
                Level = 3,
                StrongCaptureCharges = 1,
                StabilityBoostCharges = 2,
                NightveilFilterCharges = 3,
                EncounterSnapshot = new EncounterSnapshotData
                {
                    Anchors = new[]
                    {
                        new AnchorToken("trk-001", new Vector3(1.5f, 2f, -3.25f),
                            new Quaternion(0f, 0.7071f, 0f, 0.7071f)),
                    },
                    Monsters =
                    {
                        new EncounterMonsterStateData
                        {
                            MonsterId = "mon01",
                            ScanProgress = 0.5f,
                            AppliedBoosts = { "stabilityBoost" },
                        },
                    },
                },
                PendingPurchases =
                {
                    new PendingPurchaseRecord
                    {
                        OrderId = "GPA.0000-1111-2222",
                        PackId = "credits50",
                        State = "grantedUnacked",
                        IsoTimestampUtc = "2026-06-12T08:30:00Z",
                    },
                },
                DailyClaim = "2026-06-11",
                AdReward = new AdRewardData { GrantsToday = 2, DayBucket = 20619 },
                FirstZeroCreditDay = 20620,
            };
        }
    }
}
