using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoreSim.EditorTools
{
    /// <summary>
    /// シーンの生成とプレイヤーのビルド。メニューからも、バッチからも呼べる。
    /// </summary>
    /// <remarks>
    /// バッチ例 (Windows 向け):
    ///   Unity -batchmode -quit -projectPath . -executeMethod CoreSim.EditorTools.CoreBuild.BuildFromCommandLine
    ///         -playerTarget Windows64 -buildOutput Builds/Windows/CoRESimulator.exe
    /// Android は Android Build Support モジュールと SDK/NDK/JDK が Unity Hub から入っている必要がある。
    /// </remarks>
    public static class CoreBuild
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        /// <summary>ゲームは起動時にコードで組み立てるので、シーンは空で良い。</summary>
        [MenuItem("CoRE/Create Main Scene")]
        public static void CreateMainScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[CoreBuild] {ScenePath} を作成し、ビルド設定に登録した");
        }

        [MenuItem("CoRE/Build/Windows (x64)")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/CoRESimulator.exe");

        [MenuItem("CoRE/Build/Linux (x64)")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/CoRESimulator.x86_64");

        [MenuItem("CoRE/Build/Android (APK)")]
        public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/Android/CoRESimulator.apk");

        public static void BuildFromCommandLine()
        {
            string target = GetArg("-playerTarget") ?? "Windows64";
            switch (target)
            {
                case "Windows64":
                    Build(BuildTarget.StandaloneWindows64, GetArg("-buildOutput") ?? "Builds/Windows/CoRESimulator.exe");
                    break;
                case "Linux64":
                    Build(BuildTarget.StandaloneLinux64, GetArg("-buildOutput") ?? "Builds/Linux/CoRESimulator.x86_64");
                    break;
                case "Android":
                    Build(BuildTarget.Android, GetArg("-buildOutput") ?? "Builds/Android/CoRESimulator.apk");
                    break;
                default:
                    throw new Exception($"Unknown -playerTarget '{target}' (Windows64 | Linux64 | Android)");
            }
        }

        private static void Build(BuildTarget target, string output)
        {
            if (!File.Exists(ScenePath))
            {
                CreateMainScene();
            }
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                targetGroup = group,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[CoreBuild] result={summary.result} output={output} size={summary.totalSize} errors={summary.totalErrors}");
            if (summary.result != BuildResult.Succeeded)
            {
                // バッチモードでは例外を投げないと終了コードが 0 のままになる。
                throw new Exception($"Build failed: {summary.result} ({summary.totalErrors} errors)");
            }
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
