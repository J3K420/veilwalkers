#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Veilwalkers.EditorTools
{
    /// <summary>
    /// The single, reproducible Android build entry point (Story 8.1, AR-21).
    /// <para>
    /// The <c>.aab</c> (Android App Bundle) toggle lives in <c>EditorUserBuildSettings</c>,
    /// which is stored under <c>Library/</c> and is <b>gitignored</b> — so a fresh clone (or CI)
    /// defaults to APK. This script makes the App-Bundle output reproducible by setting
    /// <see cref="EditorUserBuildSettings.buildAppBundle"/> = <c>true</c> in committed code before
    /// invoking the build, and is the documented build command for both local + CI use.
    /// </para>
    /// <para>
    /// <b>This is build TOOLING, not gameplay.</b> It lives under <c>Assets/Editor/</c> (the
    /// predefined editor-only <c>Assembly-CSharp-Editor</c> — no asmdef, never shipped in the
    /// player, never part of the runtime acyclic dependency graph). It is guarded by
    /// <c>BuildConfigGuardTests</c> (which asserts the <c>buildAppBundle = true</c> assignment
    /// stays present as committed text), but it is NOT exercised headlessly — the actual
    /// <c>.aab</c> assembly + signing is the device/secret release-gate checklist (Story 8.7).
    /// </para>
    /// <para>
    /// <b>Signing (AR-21 / AC-5).</b> The keystore + passwords are read from environment variables
    /// (never from a committed file — keystores stay gitignored). See <c>docs/build-guide.md</c>.
    /// </para>
    /// </summary>
    public static class VeilwalkersBuilder
    {
        // Env vars the signing config reads on a real build machine / CI secret store.
        private const string KeystorePathEnv = "VEILWALKERS_KEYSTORE_PATH";
        private const string KeystorePassEnv = "VEILWALKERS_KEYSTORE_PASS";
        private const string KeyAliasEnv = "VEILWALKERS_KEY_ALIAS";
        private const string KeyAliasPassEnv = "VEILWALKERS_KEY_ALIAS_PASS";

        private const string OutputDir = "Builds/Android";
        private const string OutputFile = "veilwalkers.aab";

        /// <summary>
        /// Configure + run the reproducible signed Android App Bundle build.
        /// Invoke from the Editor menu, or headlessly via:
        /// <c>Unity -batchmode -quit -projectPath . -executeMethod Veilwalkers.EditorTools.VeilwalkersBuilder.BuildAndroidAppBundle</c>.
        /// </summary>
        [MenuItem("Veilwalkers/Build Android App Bundle (.aab)")]
        public static void BuildAndroidAppBundle()
        {
            // The reproducible toggle — the whole point of committing this script (AC-3). A guard
            // test pins the literal `buildAppBundle = true` assignment so a flip to false fails CI.
            EditorUserBuildSettings.buildAppBundle = true;

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                // SwitchActiveBuildTarget returns false if the switch did not take (e.g. the Android
                // module is not installed) — proceeding would build for the wrong platform.
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android);
                if (!switched || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                {
                    throw new BuildFailedException(
                        "VeilwalkersBuilder: could not switch the active build target to Android " +
                        "(is the Android Build Support module installed?). Aborting before build.");
                }
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new BuildFailedException(
                    "VeilwalkersBuilder: no enabled scenes in EditorBuildSettings — cannot build. " +
                    "Story 8.2 owns the scene list (Bootstrap → Onboarding → Home → AR Hunt → Codex → Shop).");
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = $"{OutputDir}/{OutputFile}",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            // Signing material is applied TRANSIENTLY and scrubbed in the finally below, so keystore
            // passwords NEVER persist into the serialized (and tracked) ProjectSettings.asset — the
            // "never commit secrets" invariant (CLAUDE.md / AC-5). The apply happens just before the
            // build and the scrub always runs, even on a build throw.
            ApplySigningFromEnvironment();
            try
            {
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report == null)
                {
                    // BuildPlayer can return null if the build is interrupted/crashes — fail cleanly
                    // rather than NRE on report.summary.
                    throw new BuildFailedException(
                        "VeilwalkersBuilder: BuildPipeline.BuildPlayer returned no report " +
                        "(the build was interrupted or crashed).");
                }

                BuildSummary summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException(
                        $"VeilwalkersBuilder: Android App Bundle build {summary.result} " +
                        $"({summary.totalErrors} errors).");
                }

                Debug.Log(
                    $"VeilwalkersBuilder: built {summary.outputPath} " +
                    $"({summary.totalSize} bytes) in {summary.totalTime}.");
            }
            finally
            {
                ScrubSigningFromPlayerSettings();
            }
        }

        // Reads signing material from the environment (CI secret store / local env) and applies it
        // TRANSIENTLY for the duration of one build (scrubbed by ScrubSigningFromPlayerSettings in
        // the caller's finally). When the env vars are unset (e.g. an editor smoke run), the build
        // proceeds unsigned. Nothing here reads a committed secret, and nothing persists one.
        private static void ApplySigningFromEnvironment()
        {
            string keystorePath = Environment.GetEnvironmentVariable(KeystorePathEnv);
            string keystorePass = Environment.GetEnvironmentVariable(KeystorePassEnv);
            string keyAlias = Environment.GetEnvironmentVariable(KeyAliasEnv);
            string keyAliasPass = Environment.GetEnvironmentVariable(KeyAliasPassEnv);

            bool hasSigning =
                !string.IsNullOrEmpty(keystorePath) &&
                !string.IsNullOrEmpty(keystorePass) &&
                !string.IsNullOrEmpty(keyAlias) &&
                !string.IsNullOrEmpty(keyAliasPass);

            if (!hasSigning)
            {
                Debug.LogWarning(
                    "VeilwalkersBuilder: signing env vars unset — building UNSIGNED. " +
                    "Set " + KeystorePathEnv + " / " + KeystorePassEnv + " / " + KeyAliasEnv +
                    " / " + KeyAliasPassEnv + " for a submission build (see docs/build-guide.md).");
                ScrubSigningFromPlayerSettings();
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keyaliasPass = keyAliasPass;
        }

        // Clears every signing field so no keystore name/password lingers in the serialized
        // ProjectSettings.asset (the committed file). Called both when building unsigned and in the
        // build's finally, so a signed build never leaves plaintext passwords in tracked state.
        private static void ScrubSigningFromPlayerSettings()
        {
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.keystoreName = string.Empty;
            PlayerSettings.Android.keystorePass = string.Empty;
            PlayerSettings.Android.keyaliasName = string.Empty;
            PlayerSettings.Android.keyaliasPass = string.Empty;
        }
    }
}
#endif
