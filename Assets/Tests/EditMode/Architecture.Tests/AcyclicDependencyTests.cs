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
    /// It asserts the negative ("no upward edge"), which is robust to legitimate
    /// same-tier/downward edges such as the future Economy → Persistence interface
    /// dependency documented in the Story 1.2 notes.
    /// </para>
    /// </summary>
    public sealed class AcyclicDependencyTests
    {
        private const string Prefix = "Veilwalkers.";

        // Tier rank per the canonical graph. Lower rank = closer to Core (a
        // dependency target). A reference is illegal only if it points to a
        // STRICTLY higher rank than the referencing assembly.
        private static readonly Dictionary<string, int> TierRank = new Dictionary<string, int>
        {
            { "Veilwalkers.Core", 0 },
            { "Veilwalkers.Persistence", 1 },
            { "Veilwalkers.Economy", 1 },
            { "Veilwalkers.Monsters", 1 },
            { "Veilwalkers.AR", 2 },
            { "Veilwalkers.Encounter", 2 },
            { "Veilwalkers.Billing", 2 },
            { "Veilwalkers.App", 3 },
            { "Veilwalkers.UI", 4 },
        };

        /// <summary>
        /// Every ranked Veilwalkers asmdef that declares a reference to another ranked
        /// Veilwalkers asmdef must reference a tier rank &lt;= its own.
        /// </summary>
        [Test]
        public void No_Veilwalkers_assembly_references_a_higher_tier()
        {
            var graph = LoadVeilwalkersAsmdefGraph();
            var violations = new List<string>();

            foreach (KeyValuePair<string, List<string>> entry in graph)
            {
                if (!TierRank.TryGetValue(entry.Key, out int ownRank))
                {
                    continue;
                }

                foreach (string referenced in entry.Value)
                {
                    if (!TierRank.TryGetValue(referenced, out int referencedRank))
                    {
                        continue;
                    }

                    if (referencedRank > ownRank)
                    {
                        violations.Add(
                            $"{entry.Key} (tier {ownRank}) references " +
                            $"{referenced} (tier {referencedRank}) — upward dependency is forbidden.");
                    }
                }
            }

            Assert.IsEmpty(
                violations,
                "Assembly dependency graph must stay one-way (Core ← services ← App ← UI). " +
                "Upward references found:\n" + string.Join("\n", violations));
        }

        /// <summary>
        /// Guards the test itself: every ranked Veilwalkers assembly is expected to
        /// exist on disk as an asmdef. If the graph is renamed/removed this fails
        /// loudly rather than silently passing on a graph it never inspected.
        /// </summary>
        [Test]
        public void All_expected_Veilwalkers_assemblies_are_present()
        {
            var present = LoadVeilwalkersAsmdefGraph().Keys.ToHashSet();
            var missing = TierRank.Keys.Where(expected => !present.Contains(expected)).ToList();

            Assert.IsEmpty(
                missing,
                "Expected Veilwalkers asmdefs are missing on disk: " + string.Join(", ", missing));
        }

        /// <summary>
        /// No service assembly (Core/Persistence/Economy/Monsters/AR/Encounter/Billing)
        /// may reference App or UI. This is a stronger, explicit restatement of AC-1's
        /// "no service assembly references UI or App".
        /// </summary>
        [Test]
        public void No_service_assembly_references_App_or_UI()
        {
            var graph = LoadVeilwalkersAsmdefGraph();
            var topTier = new HashSet<string> { "Veilwalkers.App", "Veilwalkers.UI" };
            var violations = new List<string>();

            foreach (KeyValuePair<string, List<string>> entry in graph)
            {
                // Services are tiers 0–2; App (3) and UI (4) are not "services".
                if (!TierRank.TryGetValue(entry.Key, out int ownRank) || ownRank >= 3)
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
        /// Loads every <c>Veilwalkers.*</c> asmdef on disk and returns a map of
        /// assembly name → the list of Veilwalkers assembly names it references.
        /// Test asmdefs (<c>*.Tests</c>) are excluded. Handles both name-based and
        /// GUID-based references.
        /// </summary>
        private static Dictionary<string, List<string>> LoadVeilwalkersAsmdefGraph()
        {
            var graph = new Dictionary<string, List<string>>();

            foreach (string guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string json = File.ReadAllText(path);
                var asmdef = JsonUtility.FromJson<AsmdefShape>(json);

                if (asmdef == null || string.IsNullOrEmpty(asmdef.name))
                {
                    continue;
                }

                if (!asmdef.name.StartsWith(Prefix) || asmdef.name.Contains(".Tests"))
                {
                    continue;
                }

                var refs = (asmdef.references ?? Array.Empty<string>())
                    .Select(ResolveReferenceName)
                    .Where(n => !string.IsNullOrEmpty(n) && n.StartsWith(Prefix) && !n.Contains(".Tests"))
                    .ToList();

                graph[asmdef.name] = refs;
            }

            return graph;
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

            if (!reference.StartsWith("GUID:"))
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
