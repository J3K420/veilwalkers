using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Veilwalkers.Architecture.Tests
{
    /// <summary>
    /// Story 7.1 — the Pit (post-MVP signature feature, FR-15–17) is a RESERVED SEAM:
    /// <c>Assets/Veilwalkers/Pit/</c> stays a folder stub with NO <c>.asmdef</c> and NO
    /// gameplay <c>.cs</c> until the post-MVP Pit is actually built. This guard makes that
    /// AR-5 rule ("Pit = folder stub, no asmdef… an empty boundary is ceremony, not
    /// enforcement") a falsifiable regression test — the same single-purpose
    /// invariant-as-test shape as <see cref="MonsterDatabaseLoreCountTests"/> (AR-20
    /// <c>Count == 67</c>).
    /// <para>
    /// The scans are NON-VACUOUS: they read the Pit folder ON DISK, so a stray
    /// <c>Pit/Anything.asmdef</c> or <c>Pit/PitController.cs</c> genuinely turns these RED
    /// (mutation-verified in dev — drop a dummy probe, confirm red, remove). The moment
    /// someone starts building the Pit inside MVP scope this fires, forcing the deliberate
    /// decision: that work gets its own assembly + an allowed-edge matrix row (and this
    /// guard is updated at the same time). The scan reads files off disk rather than via
    /// compilation, so a stray Pit assembly cannot defeat the guard through compilation
    /// order — this test never references the Pit assembly.
    /// </para>
    /// <para>
    /// [Source: docs/epics.md#Story-7.1; AR-5 (epics.md:62); docs/architecture.md:430,441–442]
    /// </para>
    /// </summary>
    public sealed class PitSeamReservedTests
    {
        /// <summary>
        /// Resolves the absolute on-disk path of the reserved Pit folder, asserting it
        /// EXISTS. A missing Pit folder is itself a regression — the reserved boundary was
        /// deleted — and fails with a clear message rather than an opaque IO exception.
        /// <para>
        /// Uses the proven <c>Application.dataPath</c> + fixed-subpath idiom from
        /// <c>AR.Tests.CameraPermissionLocationGuardTests.ResolveArSourceDirectory</c>
        /// (<c>Application.dataPath</c> is "&lt;project&gt;/Assets"; the Pit area is a fixed
        /// path beneath it). The folder's stable guid is
        /// <c>10b58bdaa4e04d99beb45fcf38f2b19b</c> (Assets/Veilwalkers/Pit.meta) — the
        /// move-resilient alternative — but the fixed path matches the established repo
        /// idiom and a Pit-folder move would be a deliberate act this guard should surface.
        /// </para>
        /// </summary>
        private static string ResolveExistingPitFolder()
        {
            string pitFolder = Path.Combine(UnityEngine.Application.dataPath, "Veilwalkers", "Pit");

            Assert.That(
                Directory.Exists(pitFolder),
                Is.True,
                $"The reserved Pit seam folder is missing ('{pitFolder}'). The post-MVP Pit " +
                "boundary must stay tracked — restore Assets/Veilwalkers/Pit/ and its tracked " +
                "contents (the README + the pre-existing .gitkeep) rather than deleting it.");

            return pitFolder;
        }

        [Test]
        public void Pit_folder_exists_as_a_reserved_boundary()
        {
            // Asserted inside the resolver; this test names the invariant explicitly.
            ResolveExistingPitFolder();
        }

        [Test]
        public void Pit_folder_has_no_asmdef()
        {
            string pitFolder = ResolveExistingPitFolder();

            string[] asmdefs = Directory
                .GetFiles(pitFolder, "*.asmdef", SearchOption.AllDirectories);

            Assert.That(
                asmdefs,
                Is.Empty,
                "The Pit is a post-MVP reserved seam and must have NO .asmdef until it has " +
                "real code (AR-5: an empty boundary is ceremony, not enforcement). Found: " +
                string.Join(", ", asmdefs) + ". If you are building the Pit, add its assembly " +
                "AND a Veilwalkers.Pit row to the AcyclicDependencyTests allowed-edge matrix, " +
                "then update this guard.");
        }

        [Test]
        public void Pit_folder_has_no_gameplay_code()
        {
            string pitFolder = ResolveExistingPitFolder();

            string[] csFiles = Directory
                .GetFiles(pitFolder, "*.cs", SearchOption.AllDirectories);

            Assert.That(
                csFiles,
                Is.Empty,
                "The Pit is a post-MVP reserved seam and must contain NO gameplay code until " +
                "the FR-15/16/17 Pit is built (no MVP story implements Pit gameplay). Found: " +
                string.Join(", ", csFiles) + ". Pit gameplay belongs to the post-MVP Pit work " +
                "(its own assembly + matrix entry), not MVP scope.");
        }

        [Test]
        public void No_Pit_assembly_is_registered_in_the_dependency_graph()
        {
            // Self-documenting companion to the no-asmdef scan: the Pit must be ABSENT from
            // the on-disk Veilwalkers asmdef graph (it is deliberately ungated, not merely
            // forgotten). Re-uses the production-asmdef enumeration so a Pit assembly added
            // anywhere — not just under Pit/ — is still caught.
            string[] pitAssemblies = AssetDatabase
                .FindAssets("t:AssemblyDefinitionAsset", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(ReadAsmdefName)
                .Where(IsPitAssemblyName)
                .ToArray();

            Assert.That(
                pitAssemblies,
                Is.Empty,
                "No Veilwalkers.Pit assembly may exist while the Pit is a post-MVP reserved " +
                "seam (it is deliberately ungated — no asmdef, no allowed-edge matrix row). " +
                "Found: " + string.Join(", ", pitAssemblies) + ".");
        }

        /// <summary>
        /// Reads an asmdef's declared <c>name</c>, returning null on any unreadable/malformed
        /// file rather than throwing. A path that no longer resolves (an empty/stale guid, or
        /// a file deleted between <c>FindAssets</c> and the read — a real CI race) or asmdef
        /// JSON that fails to parse degrades to "no name" (which the caller treats as
        /// not-a-Pit-assembly), never an opaque IO/parse exception that would crash the test
        /// instead of failing it cleanly. Mirrors the defensive null-return posture of
        /// <c>AcyclicDependencyTests.ResolveReferenceName</c>.
        /// </summary>
        private static string ReadAsmdefName(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
            {
                return null;
            }

            try
            {
                return UnityEngine.JsonUtility
                    .FromJson<AsmdefName>(File.ReadAllText(assetPath))?.name;
            }
            catch (System.Exception)
            {
                // Unreadable file or malformed asmdef JSON — not a Pit assembly we can name.
                return null;
            }
        }

        /// <summary>
        /// True only for the reserved Pit assembly family: the exact <c>Veilwalkers.Pit</c>
        /// or a dot-bounded sub-namespace (<c>Veilwalkers.Pit.Arena</c>, …). A bare prefix
        /// match would wrongly flag unrelated future assemblies like
        /// <c>Veilwalkers.Pitfall</c> / <c>Veilwalkers.Pitch</c>.
        /// </summary>
        private static bool IsPitAssemblyName(string name)
        {
            return name == "Veilwalkers.Pit"
                || (name != null && name.StartsWith("Veilwalkers.Pit.", System.StringComparison.Ordinal));
        }

        // Minimal shape for JsonUtility to read an asmdef's declared name.
        [System.Serializable]
        private sealed class AsmdefName
        {
            public string name;
        }
    }
}
