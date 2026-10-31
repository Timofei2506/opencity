using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OpenCity.EditorTools
{
    /// <summary>
    /// Entry points for headless builds. CI calls these through
    /// <c>-executeMethod OpenCity.EditorTools.BuildScript.BuildWindowsCI</c> (or Linux),
    /// locally they are also available from the OpenCity menu.
    ///
    /// Project settings, the build scene list and the always-included shader list are
    /// applied by <see cref="ProjectSetup.OnPreprocessBuild"/>, so a clean CI checkout
    /// needs no manual step before building.
    /// </summary>
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string ProductName = "OpenCity";

        [MenuItem("OpenCity/Build/Windows 64-bit")]
        public static void BuildWindowsFromMenu() => Build(BuildTarget.StandaloneWindows64, false);

        [MenuItem("OpenCity/Build/Linux 64-bit")]
        public static void BuildLinuxFromMenu() => Build(BuildTarget.StandaloneLinux64, false);

        /// <summary>CI entry point: Windows 64-bit standalone.</summary>
        public static void BuildWindowsCI() => Build(BuildTarget.StandaloneWindows64, true);

        /// <summary>CI entry point: Linux 64-bit standalone.</summary>
        public static void BuildLinuxCI() => Build(BuildTarget.StandaloneLinux64, true);

        static void Build(BuildTarget target, bool exitWhenDone)
        {
            string outputDir;
            string location;

            switch (target)
            {
                case BuildTarget.StandaloneWindows64:
                    outputDir = Path.Combine("build", "Windows64");
                    location = Path.Combine(outputDir, ProductName + ".exe");
                    break;

                case BuildTarget.StandaloneLinux64:
                    outputDir = Path.Combine("build", "Linux64");
                    location = Path.Combine(outputDir, ProductName + ".x86_64");
                    break;

                default:
                    Fail($"Unsupported build target: {target}", exitWhenDone);
                    return;
            }

            try
            {
                Directory.CreateDirectory(outputDir);
            }
            catch (Exception e)
            {
                Fail($"Cannot create output directory '{outputDir}': {e.Message}", exitWhenDone);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = location,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Debug.Log($"[BuildScript] Building {target} -> {location}");
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log($"[BuildScript] Result: {summary.result}, size: {summary.totalSize / (1024 * 1024)} MB, " +
                      $"errors: {summary.totalErrors}, warnings: {summary.totalWarnings}, " +
                      $"time: {summary.totalTime.TotalSeconds:F1} s");

            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"Build of {target} finished with {summary.result}", exitWhenDone);
                return;
            }

            if (exitWhenDone)
            {
                EditorApplication.Exit(0);
            }
        }

        static void Fail(string message, bool exitWhenDone)
        {
            Debug.LogError("[BuildScript] " + message);
            if (exitWhenDone)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
