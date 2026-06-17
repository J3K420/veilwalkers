using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Veilwalkers.Architecture.Tests
{
    /// <summary>
    /// Story 8.1 (AR-21) — the Android build config is pinned as committed DATA, and this guard
    /// makes the five Play-submission invariants falsifiable regression tests (the same
    /// single-purpose invariant-as-test shape as <see cref="PitSeamReservedTests"/>).
    /// <para>
    /// Every pin is a TEXT read of a file ON DISK (<c>ProjectSettings/ProjectSettings.asset</c>,
    /// <c>.gitignore</c>, the committed build script) — the Epic-8 verifiability invariant: a
    /// headless pin never runs a real build and never loads ProjectSettings via the running
    /// player. The actual <c>.aab</c> assembly + keystore generation + EDM4U resolve are the
    /// device/secret release-gate checklist (Story 8.7), NOT exercised here.
    /// </para>
    /// <para>
    /// The scans are NON-VACUOUS (mutation-verified in dev — revert applicationId→template and
    /// TargetSdk→0, confirm RED, restore). YAML maps are read BLOCK-ANCHORED: the
    /// <c>applicationIdentifier</c> and <c>scriptingBackend</c> values live under map headers
    /// whose <c>Android:</c> child must be distinguished from the <c>iPhone:</c>/<c>Standalone:</c>
    /// siblings (which still carry the URP template id) and from the many unrelated
    /// <c>Android*</c> scalar lines (<c>AndroidMinSdkVersion</c>, …).
    /// </para>
    /// <para>
    /// [Source: docs/epics.md#Story-8.1; Epic-6 retro Action Item #4; docs/architecture.md:137–148,234–238,528–532]
    /// </para>
    /// </summary>
    public sealed class BuildConfigGuardTests
    {
        // --- Single source of truth for the Play target-SDK floor (retro AI#3) ---
        // The doc (docs/build-guide.md + CLAUDE.md) cites THIS value + date; do not hand-copy a
        // second integer that can drift. Google requires new apps to target an API level within
        // ~one year of the latest Android release; API 35 (Android 15) is the floor as of 2026-06.
        // Re-check against the live Play policy yearly: https://support.google.com/googleplay/android-developer/answer/11926878
        private const int MinPlayTargetSdk = 35; // Play target-SDK floor as of 2026-06.
        private const int ArcoreMinSdk = 24;      // ARCore floor — do not lower.

        private const string ChosenApplicationId = "com.veilwalkers.app";

        // ---- path resolution (project-root files are siblings of Assets/) ----

        private static string ResolveExistingProjectRootFile(string relativePath, string humanName)
        {
            // Application.dataPath is "<project>/Assets"; project-root files sit one level up.
            string path = Path.GetFullPath(
                Path.Combine(UnityEngine.Application.dataPath, "..", relativePath));

            Assert.That(
                File.Exists(path),
                Is.True,
                $"{humanName} is missing ('{path}'). The reproducible build config (Story 8.1, " +
                "AR-21) depends on this committed file — restore it rather than deleting it.");

            return path;
        }

        private static string ReadProjectSettings()
        {
            string path = ResolveExistingProjectRootFile(
                Path.Combine("ProjectSettings", "ProjectSettings.asset"), "ProjectSettings.asset");
            return File.ReadAllText(path);
        }

        // Reads the FIRST indented `Android:` child under a named YAML map header (e.g.
        // `applicationIdentifier:` or `scriptingBackend:`), so a whole-file scan can't false-match
        // a sibling platform key or an unrelated `Android*` scalar. Returns null if not found.
        private static string ReadAndroidChildOfMap(string yaml, string mapHeader)
        {
            // Match: a line `  <mapHeader>:` then the first deeper-indented `Android: <value>`.
            var pattern = new Regex(
                @"^\s*" + Regex.Escape(mapHeader) + @":\s*$\r?\n(?:^\s.*$\r?\n)*?^\s+Android:\s*(\S+)\s*$",
                RegexOptions.Multiline);
            Match m = pattern.Match(yaml);
            return m.Success ? m.Groups[1].Value : null;
        }

        // Reads a top-level scalar `<key>: <value>` (e.g. `AndroidTargetSdkVersion: 35`).
        private static string ReadTopLevelScalar(string yaml, string key)
        {
            var pattern = new Regex(@"^\s*" + Regex.Escape(key) + @":\s*(\S+)\s*$", RegexOptions.Multiline);
            Match m = pattern.Match(yaml);
            return m.Success ? m.Groups[1].Value : null;
        }

        // ---- AC-1: applicationId is a real bundle id, not the template default ----

        [Test]
        public void Android_applicationId_is_not_the_unity_template_default()
        {
            string yaml = ReadProjectSettings();
            string androidId = ReadAndroidChildOfMap(yaml, "applicationIdentifier");

            Assert.That(androidId, Is.Not.Null,
                "Could not locate applicationIdentifier → Android in ProjectSettings.asset.");
            Assert.That(androidId.StartsWith("com.UnityTechnologies."), Is.False,
                $"applicationIdentifier.Android is the URP template default ('{androidId}') — a Play " +
                "hard blocker. Set a real reverse-DNS bundle id (Story 8.1).");
            Assert.That(androidId.Contains("template"), Is.False,
                $"applicationIdentifier.Android still contains 'template' ('{androidId}').");
            Assert.That(androidId, Is.EqualTo(ChosenApplicationId),
                $"applicationIdentifier.Android is '{androidId}', expected the committed id " +
                $"'{ChosenApplicationId}'. The bundle id is PERMANENT once published — change it " +
                "here and in the story record together if intentionally re-pointed.");
        }

        // ---- AC-2: target SDK is an explicit pinned floor; min SDK stays 24 ----

        [Test]
        public void Android_target_sdk_is_pinned_at_or_above_the_play_floor()
        {
            string yaml = ReadProjectSettings();
            string targetRaw = ReadTopLevelScalar(yaml, "AndroidTargetSdkVersion");

            Assert.That(targetRaw, Is.Not.Null,
                "Could not locate AndroidTargetSdkVersion in ProjectSettings.asset.");
            Assert.That(int.TryParse(targetRaw, out int target), Is.True,
                $"AndroidTargetSdkVersion is not an integer ('{targetRaw}').");
            Assert.That(target, Is.Not.Zero,
                "AndroidTargetSdkVersion is 0 (Automatic) — Play rejects an implicit target. Pin an " +
                $"explicit integer >= {MinPlayTargetSdk} (the Play floor as of 2026-06; see docs/build-guide.md).");
            Assert.That(target, Is.GreaterThanOrEqualTo(MinPlayTargetSdk),
                $"AndroidTargetSdkVersion {target} is below the Play floor {MinPlayTargetSdk} " +
                "(as of 2026-06). Raise it (and update MinPlayTargetSdk + docs/build-guide.md together).");
        }

        [Test]
        public void Android_min_sdk_stays_at_the_arcore_floor()
        {
            string yaml = ReadProjectSettings();
            string minRaw = ReadTopLevelScalar(yaml, "AndroidMinSdkVersion");

            Assert.That(minRaw, Is.Not.Null,
                "Could not locate AndroidMinSdkVersion in ProjectSettings.asset.");
            Assert.That(int.TryParse(minRaw, out int min), Is.True,
                $"AndroidMinSdkVersion is not an integer ('{minRaw}').");
            Assert.That(min, Is.EqualTo(ArcoreMinSdk),
                $"AndroidMinSdkVersion is {min}, expected {ArcoreMinSdk} (the ARCore floor). Do not " +
                "lower it (ARCore needs API 24) or raise it without a recorded reason.");
        }

        // ---- AC-6: IL2CPP + ARM64 stay set (regression guard against template drift) ----

        [Test]
        public void Android_scripting_backend_is_il2cpp()
        {
            string yaml = ReadProjectSettings();
            string backend = ReadAndroidChildOfMap(yaml, "scriptingBackend");

            Assert.That(backend, Is.Not.Null,
                "Could not locate scriptingBackend → Android in ProjectSettings.asset.");
            // 1 = IL2CPP, 0 = Mono. Play submission + ARM64 require IL2CPP.
            Assert.That(backend, Is.EqualTo("1"),
                $"scriptingBackend.Android is '{backend}', expected '1' (IL2CPP). A flip to Mono (0) " +
                "breaks the ARM64 / Play requirement (Story 1.1 / AR-1).");
        }

        [Test]
        public void Android_target_architectures_include_arm64()
        {
            string yaml = ReadProjectSettings();
            string archRaw = ReadTopLevelScalar(yaml, "AndroidTargetArchitectures");

            Assert.That(archRaw, Is.Not.Null,
                "Could not locate AndroidTargetArchitectures in ProjectSettings.asset.");
            Assert.That(int.TryParse(archRaw, out int arch), Is.True,
                $"AndroidTargetArchitectures is not an integer ('{archRaw}').");
            // Bit-field: 1 = ARMv7, 2 = ARM64. Play 64-bit + ARCore require the ARM64 bit.
            Assert.That(arch & 2, Is.Not.Zero,
                $"AndroidTargetArchitectures ({arch}) does not include the ARM64 bit (2). Play " +
                "requires a 64-bit build (Story 1.1 / AR-1).");
        }

        // ---- AC-5: the keystore/secret exclusions stay in .gitignore ----

        [Test]
        public void Gitignore_still_excludes_signing_secrets()
        {
            string path = ResolveExistingProjectRootFile(".gitignore", ".gitignore");

            // Strip trailing same-line comments + whitespace, drop full-line comments, so a benign
            // reformat (`*.keystore # release key`) or a more-specific glob (`**/*.keystore`) still
            // counts — we assert the SECURITY PROPERTY (each secret token is excluded), not an exact
            // literal line. Still non-vacuous: deleting an exclusion entirely fails.
            string[] patterns = File.ReadAllLines(path)
                .Select(StripGitignoreComment)
                .Where(l => l.Length > 0)
                .ToArray();

            foreach (string secret in new[] { "*.keystore", "*.jks", "keystore.properties" })
            {
                bool excluded = patterns.Any(p => GitignorePatternExcludes(p, secret));
                Assert.That(
                    excluded,
                    Is.True,
                    $".gitignore no longer excludes '{secret}'. Signing secrets must NEVER be " +
                    "committed (CLAUDE.md). Restore the exclusion (Story 8.1 / AR-21 / AC-5).");
            }
        }

        // Drops a full-line `#` comment and any trailing ` # …` comment, then trims.
        private static string StripGitignoreComment(string line)
        {
            string s = line.Trim();
            if (s.StartsWith("#"))
            {
                return string.Empty;
            }

            int hash = s.IndexOf(" #", System.StringComparison.Ordinal);
            if (hash >= 0)
            {
                s = s.Substring(0, hash).Trim();
            }

            return s;
        }

        // True if a (comment-stripped) .gitignore pattern excludes the given secret token, allowing
        // benign equivalents: the exact token, a leading-slash or `**/`-anchored variant, or a
        // broader glob that ends with the token (e.g. `**/*.keystore`). Not a full gitignore engine
        // — just tolerant enough that a harmless reformat doesn't read as a security regression.
        private static bool GitignorePatternExcludes(string pattern, string secret)
        {
            string p = pattern.TrimStart('/');
            return p == secret
                || p == "**/" + secret
                || p.EndsWith("/" + secret, System.StringComparison.Ordinal);
        }

        // ---- AC-3: the .aab output is reproducible via the committed build script ----

        [Test]
        public void Build_script_pins_the_app_bundle_output_reproducibly()
        {
            string path = ResolveExistingProjectRootFile(
                Path.Combine("Assets", "Editor", "VeilwalkersBuilder.cs"), "VeilwalkersBuilder.cs");
            string source = File.ReadAllText(path);

            // Match the exact assignment, not a bare mention — a flip to `= false` must fail.
            var pattern = new Regex(@"buildAppBundle\s*=\s*true");
            Assert.That(
                pattern.IsMatch(source),
                Is.True,
                "VeilwalkersBuilder.cs must set `EditorUserBuildSettings.buildAppBundle = true` so the " +
                ".aab (Android App Bundle) output is reproducible from a fresh clone (the toggle " +
                "otherwise lives only in gitignored EditorUserBuildSettings). Story 8.1 / AR-21 / AC-3.");
        }
    }
}
