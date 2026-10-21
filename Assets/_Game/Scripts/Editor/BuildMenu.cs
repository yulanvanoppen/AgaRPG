using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AgaRPG.EditorTools
{
    /// <summary>
    /// One-click Windows release: builds a 64-bit Windows player from the scenes in Build Settings
    /// and zips it, ready to attach to a GitHub release.
    /// </summary>
    /// <remarks>
    /// Output: <c>Builds/AgaRPG-Windows/AgaRPG.exe</c> and
    /// <c>Builds/AgaRPG-Windows-v{version}.zip</c>, where the version is
    /// <i>Project Settings → Player → Version</i>. <c>Builds/</c> is git-ignored.
    /// <see cref="BuildWindowsFromCommandLine"/> runs the same build in batch mode for CI.
    /// </remarks>
    public static class BuildMenu
    {
        private const string BuildRoot = "Builds";
        private const string PlayerFolderName = "AgaRPG-Windows";
        private const string ExecutableName = "AgaRPG.exe";

        [MenuItem("AgaRPG/Build Windows (zip)", priority = 100)]
        private static void BuildWindowsMenu()
        {
            if (!EditorUtility.DisplayDialog("Build Windows",
                    $"Build version {PlayerSettings.bundleVersion} for Windows (64-bit) and zip it?\n\n" +
                    "Bump Project Settings → Player → Version first for a new release.", "Build", "Cancel"))
                return;

            if (BuildWindows(out string zipPath)) EditorUtility.RevealInFinder(zipPath);
        }

        /// <summary>
        /// Batch mode entry point, e.g.
        /// <c>Unity.exe -batchmode -quit -projectPath . -executeMethod AgaRPG.EditorTools.BuildMenu.BuildWindowsFromCommandLine</c>.
        /// </summary>
        public static void BuildWindowsFromCommandLine()
        {
            bool ok = BuildWindows(out _);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool BuildWindows(out string zipPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string buildRoot = Path.Combine(projectRoot, BuildRoot);
            string playerFolder = Path.Combine(buildRoot, PlayerFolderName);
            zipPath = Path.Combine(buildRoot, $"{PlayerFolderName}-v{PlayerSettings.bundleVersion}.zip");

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] No scenes enabled in Build Settings.");
                return false;
            }

            // Start clean so files from older builds never end up in the zip.
            if (Directory.Exists(playerFolder)) Directory.Delete(playerFolder, true);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(playerFolder, ExecutableName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Build] Windows build {report.summary.result} with {report.summary.totalErrors} error(s).");
                return false;
            }

            // Debug symbols Unity puts next to the player; not meant to be shipped.
            foreach (string folder in Directory.GetDirectories(playerFolder, "*DoNotShip*"))
                Directory.Delete(folder, true);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(playerFolder, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);

            double sizeMb = new FileInfo(zipPath).Length / (1024.0 * 1024.0);
            Debug.Log($"[Build] Windows build succeeded: {zipPath} ({sizeMb:F0} MB, built in {report.summary.totalTime:mm\\:ss}).");
            return true;
        }
    }
}
