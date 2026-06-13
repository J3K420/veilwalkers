using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Veilwalkers.Architecture.Tests
{
    /// <summary>
    /// Enforces the one-way, acyclic assembly graph at the heart of AC-1/AC-3:
    /// <c>Core ← {Persistence, Economy, Monsters} ← {AR, Encounter, Billing} ← App ← UI</c>.
    /// <para>
    /// The test reads the <c>.asmdef</c> files directly (the authoritative declaration
    /// of the graph) rather than the compiled output. This matters because Unity does
    /// NOT emit an assembly that currently has zero <c>.cs</c> files, so inspecting only
    /// <c>CompilationPipeline.GetAssemblies()</c> would silently skip the empty service
    /// assemblies (Persistence, Economy, …) that exist as boundaries before their code
    /// lands in later stories. Reading the asmdef JSON validates the declared edges
    /// regardless of whether each assembly has code yet.
    /// </para>
    /// <para>
    /// Enforcement is an explicit per-assembly allowlist (the Story 1.2 reference
    /// matrix), not a tier-rank comparison: a rank check would let a forbidden
    /// one-way sideways edge (e.g. <c>AR → Encounter</c>) pass, and Unity's import
    /// step only rejects true cycles, not sideways edges. Legitimate new edges (e.g.
    /// the Story 1.4 <c>Economy → Persistence</c> interface dependency) are added to
    /// the matrix deliberately, as a visible test edit.
    /// </para>
    /// </summary>
    public sealed class AcyclicDependencyTests
    {
        private const string Prefix = "Veilwalkers.";

        // The allowed-edge matrix from the Story 1.2 Dev Notes: each assembly maps to
        // the ONLY Veilwalkers assemblies it may reference. Every legal edge points
        // toward Core; anything not listed here is forbidden (upward AND sideways).
        // Story 1.4 added "Veilwalkers.Persistence" to Economy's row (the credit
        // ledger persists through SaveService).
        private static readonly Dictionary<string, string[]> AllowedReferences = new Dictionary<string, string[]>
        {
            { "Veilwalkers.Core", Array.Empty<string>() },
            { "Veilwalkers.Persistence", new[] { "Veilwalkers.Core" } },
            { "Veilwalkers.Economy", new[] { "Veilwalkers.Core", "Veilwalkers.Persistence" } },
            { "Veilwalkers.Monsters", new[] { "Veilwalkers.Core" } },
            { "Veilwalkers.AR", new[] { "Veilwalkers.Core" } },
            {
                "Veilwalkers.Encounter",
                new[] { "Veilwalkers.Core", "Veilwalkers.Economy", "Veilwalkers.Monsters", "Veilwalkers.AR" }
            },
            { "Veilwalkers.Billing", new[] { "Veilwalkers.Core", "Veilwalkers.Economy" } },
            {
                "Veilwalkers.App",
                new[]
                {
                    "Veilwalkers.Core", "Veilwalkers.Persistence", "Veilwalkers.Economy",
                    "Veilwalkers.Monsters", "Veilwalkers.AR", "Veilwalkers.Encounter", "Veilwalkers.Billing"
                }
            },
            {
                "Veilwalkers.UI",
                new[]
                {
                    "Veilwalkers.Core", "Veilwalkers.Persistence", "Veilwalkers.Economy",
                    "Veilwalkers.Monsters", "Veilwalkers.AR", "Veilwalkers.Encounter", "Veilwalkers.Billing",
                    "Veilwalkers.App"
                }
            },
        };

        /// <summary>
        /// Every Veilwalkers → Veilwalkers reference declared on disk must be on the
        /// referencing assembly's allowlist. This forbids upward edges AND sideways
        /// edges the matrix does not sanction. An assembly missing from the matrix
        /// entirely is itself a violation (see also the dedicated guard test below).
        /// </summary>
        [Test]
        public void Every_Veilwalkers_reference_is_on_the_allowed_edge_list()
        {
            var graph = LoadVeilwalkersAsmdefGraph();
            var violations = new List<string>();

            foreach (KeyValuePair<string, List<string>> entry in graph)
            {
                if (!AllowedReferences.TryGetValue(entry.Key, out string[] allowed))
                {
                    violations.Add(
                        $"{entry.Key} is not in the allowed-edge matrix — add it (with its sanctioned " +
                        "references) so the architecture guard covers it.");
                    continue;
                }

                foreach (string referenced in entry.Value)
                {
                    if (!allowed.Contains(referenced))
                    {
                        violations.Add(
                            $"{entry.Key} references {referenced}, which its allowlist does not sanction " +
                            "(edges must point toward Core and be declared in the matrix).");
                    }
                }
            }

            Assert.IsEmpty(
                violations,
                "Assembly dependency graph must stay one-way (Core ← services ← App ← UI). " +
                "Forbidden references found:\n" + string.Join("\n", violations));
        }

        /// <summary>
        /// Guards the guard: every non-test <c>Veilwalkers.*</c> asmdef on disk must
        /// have an entry in the allowed-edge matrix. Without this, a newly added
        /// assembly would be invisible to enforcement until someone remembered to
        /// extend the matrix.
        /// </summary>
        [Test]
        public void Every_Veilwalkers_asmdef_on_disk_has_an_allowlist_entry()
        {
            var present = LoadVeilwalkersAsmdefGraph().Keys.ToHashSet();
            var unranked = present.Where(name => !AllowedReferences.ContainsKey(name)).ToList();

            Assert.IsEmpty(
                unranked,
                "Veilwalkers asmdefs on disk are missing from the allowed-edge matrix " +
                "(add each with its sanctioned references): " + string.Join(", ", unranked));
        }

        /// <summary>
        /// Guards the test itself: every assembly in the matrix is expected to exist
        /// on disk as an asmdef. If the graph is renamed/removed this fails loudly
        /// rather than silently passing on a graph it never inspected.
        /// </summary>
        [Test]
        public void All_expected_Veilwalkers_assemblies_are_present()
        {
            var present = LoadVeilwalkersAsmdefGraph().Keys.ToHashSet();
            var missing = AllowedReferences.Keys.Where(expected => !present.Contains(expected)).ToList();

            Assert.IsEmpty(
                missing,
                "Expected Veilwalkers asmdefs are missing on disk: " + string.Join(", ", missing));
        }

        /// <summary>
        /// No service assembly (Core/Persistence/Economy/Monsters/AR/Encounter/Billing)
        /// may reference App or UI. Redundant with the allowlist, but kept as an
        /// explicit restatement of AC-1's "no service assembly references UI or App".
        /// </summary>
        [Test]
        public void No_service_assembly_references_App_or_UI()
        {
            var graph = LoadVeilwalkersAsmdefGraph();
            var topTier = new HashSet<string> { "Veilwalkers.App", "Veilwalkers.UI" };
            var violations = new List<string>();

            foreach (KeyValuePair<string, List<string>> entry in graph)
            {
                if (topTier.Contains(entry.Key))
                {
                    continue;
                }

                foreach (string referenced in entry.Value)
                {
                    if (topTier.Contains(referenced))
                    {
                        violations.Add($"{entry.Key} references {referenced}.");
                    }
                }
            }

            Assert.IsEmpty(
                violations,
                "No service assembly may reference App or UI:\n" + string.Join("\n", violations));
        }

        /// <summary>
        /// Loads every <c>Veilwalkers.*</c> asmdef under <c>Assets/</c> and returns a
        /// map of assembly name → the list of Veilwalkers assembly names it references.
        /// Test asmdefs (<c>*.Tests</c> suffix) are excluded. Handles both name-based
        /// and GUID-based references.
        /// </summary>
        private static Dictionary<string, List<string>> LoadVeilwalkersAsmdefGraph()
        {
            var graph = new Dictionary<string, List<string>>();

            foreach (string guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string json = File.ReadAllText(path);
                var asmdef = JsonUtility.FromJson<AsmdefShape>(json);

                if (asmdef == null || string.IsNullOrEmpty(asmdef.name))
                {
                    continue;
                }

                if (!asmdef.name.StartsWith(Prefix, StringComparison.Ordinal) || IsTestAssembly(asmdef.name))
                {
                    continue;
                }

                var refs = (asmdef.references ?? Array.Empty<string>())
                    .Select(ResolveReferenceName)
                    .Where(n => !string.IsNullOrEmpty(n)
                        && n.StartsWith(Prefix, StringComparison.Ordinal)
                        && !IsTestAssembly(n))
                    .ToList();

                graph[asmdef.name] = refs;
            }

            return graph;
        }

        /// <summary>
        /// A test assembly is identified by the ".Tests" SUFFIX only — a Contains
        /// check would wrongly exempt a production assembly with ".Tests" anywhere in
        /// its name (e.g. a hypothetical Veilwalkers.TestsSupport).
        /// </summary>
        private static bool IsTestAssembly(string assemblyName)
        {
            return assemblyName.EndsWith(".Tests", StringComparison.Ordinal);
        }

        /// <summary>
        /// A reference entry is either a plain assembly name ("Veilwalkers.Core") or a
        /// GUID reference ("GUID:abc123…"). Resolve GUID refs back to the referenced
        /// asmdef's declared name.
        /// </summary>
        private static string ResolveReferenceName(string reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }

            if (!reference.StartsWith("GUID:", StringComparison.Ordinal))
            {
                return reference;
            }

            string refGuid = reference.Substring("GUID:".Length);
            string refPath = AssetDatabase.GUIDToAssetPath(refGuid);
            if (string.IsNullOrEmpty(refPath))
            {
                return null;
            }

            var refAsmdef = JsonUtility.FromJson<AsmdefShape>(File.ReadAllText(refPath));
            return refAsmdef?.name;
        }

        // Minimal shape for JsonUtility to pull the fields we need from an asmdef.
        [Serializable]
        private sealed class AsmdefShape
        {
            public string name;
            public string[] references;
        }
    }
}
