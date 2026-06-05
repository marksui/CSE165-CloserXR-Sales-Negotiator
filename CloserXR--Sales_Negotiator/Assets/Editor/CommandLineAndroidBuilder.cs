using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace CloserXR.SalesNegotiator.Editor
{
    public static class CommandLineAndroidBuilder
    {
        [MenuItem("CloserXR/Build Android APK")]
        public static void BuildApkFromMenu()
        {
            BuildApkInternal(Path.Combine("build", "1.apk"), false);
        }

        public static void BuildApk()
        {
            string outputPath = GetArgument("-outputPath") ?? Path.Combine("build", "1.apk");
            BuildApkInternal(outputPath, true);
        }

        private static void BuildApkInternal(string outputPath, bool exitEditor)
        {
            string fullOutputPath = Path.GetFullPath(outputPath);
            string outputDirectory = Path.GetDirectoryName(fullOutputPath);

            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Console.Error.WriteLine("No enabled scenes found in EditorBuildSettings.");
                if (exitEditor)
                {
                    EditorApplication.Exit(1);
                }
                return;
            }

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.forceInternetPermission = true;

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = fullOutputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Console.WriteLine($"Android build result: {summary.result} ({summary.totalSize} bytes)");

            if (exitEditor)
            {
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
        }

        private static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
