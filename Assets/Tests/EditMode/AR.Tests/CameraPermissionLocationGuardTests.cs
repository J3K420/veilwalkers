using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Veilwalkers.AR;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// AC-3 guard (Story 3.1): Location permission is NEVER requested in the MVP (world spawns are
    /// Phase-3, FR-5). Two layers:
    /// <list type="number">
    /// <item><b>Reflection:</b> the <see cref="ICameraPermission"/> seam exposes camera only — no
    /// member naming "location" — so no flow path can request location by construction.</item>
    /// <item><b>Source scan (the falsifiable one):</b> the AR production <c>.cs</c> reference no
    /// location-permission API token. A future story that adds a location request → red.</item>
    /// </list>
    /// The scan mirrors the project's disk-scan architecture-guard style (it asserts it actually
    /// found AR source files BEFORE asserting the token is absent, so a wrong/empty path cannot make
    /// the test vacuously green — the source-scan tautology trap).
    /// </summary>
    public sealed class CameraPermissionLocationGuardTests
    {
        // Tokens that would indicate a location-permission request crept into the AR area.
        private static readonly string[] BannedLocationTokens =
        {
            "FineLocation",
            "CoarseLocation",
            "LocationService",
            "ACCESS_FINE_LOCATION",
            "ACCESS_COARSE_LOCATION",
        };

        [Test]
        public void ICameraPermission_seam_exposes_no_location_member()
        {
            MemberInfo[] members = typeof(ICameraPermission).GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (MemberInfo member in members)
            {
                StringAssert.DoesNotContain(
                    "location", member.Name.ToLowerInvariant(),
                    $"ICameraPermission must expose camera only (AC-3); '{member.Name}' looks location-related.");
            }
        }

        [Test]
        public void AR_production_source_references_no_location_permission_api()
        {
            string arSourceDir = ResolveArSourceDirectory();
            string[] arSourceFiles = Directory.GetFiles(arSourceDir, "*.cs", SearchOption.AllDirectories);

            // Anti-vacuity: prove the scan actually has source to scan, else a bad path passes green.
            Assert.That(arSourceFiles, Is.Not.Empty,
                $"AC-3 scan found no AR .cs files under '{arSourceDir}' — the path is wrong and the test would be vacuously green.");

            foreach (string file in arSourceFiles)
            {
                string contents = File.ReadAllText(file);
                foreach (string banned in BannedLocationTokens)
                {
                    StringAssert.DoesNotContain(
                        banned, contents,
                        $"AR source '{Path.GetFileName(file)}' references the location-permission token " +
                        $"'{banned}' — Location is never requested in the MVP (AC-3, FR-5).");
                }
            }
        }

        // Locate Assets/Veilwalkers/AR by walking up from this test file to the project root and
        // descending — robust to where the test runner sets the working directory.
        private static string ResolveArSourceDirectory()
        {
            // Application.dataPath is "<project>/Assets"; the AR area is a fixed path beneath it.
            string assets = UnityEngine.Application.dataPath;
            string arDir = Path.Combine(assets, "Veilwalkers", "AR");
            Assert.That(Directory.Exists(arDir), Is.True,
                $"Expected the AR source directory at '{arDir}'.");
            return arDir;
        }
    }
}
