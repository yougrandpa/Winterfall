using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.Presentation;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MyAiWorld.EditorTools
{
    // Runs against the open project, including its real package configuration.
    [InitializeOnLoad]
    public static class ProjectVerification
    {
        private const string PendingModeKey = "MyAiWorld.Verification.Mode";
        private static readonly TestRunnerApi Api;

        static ProjectVerification()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(new ResultRecorder());
        }

        [MenuItem("Tools/My AI World/Verification/Run EditMode Tests")]
        public static void RunEditMode()
        {
            Run(TestMode.EditMode, "MyAiWorld.Tests.EditMode");
        }

        [MenuItem("Tools/My AI World/Verification/Run PlayMode Tests")]
        public static void RunPlayMode()
        {
            Run(TestMode.PlayMode, "MyAiWorld.Tests.PlayMode");
        }

        [MenuItem("Tools/My AI World/Verification/Run PlayMode Smoke Tests")]
        public static void RunPlayModeSmoke()
        {
            Run(TestMode.PlayMode, "MyAiWorld.Tests.PlayMode", "EngineSmoke");
        }

        [MenuItem("Tools/My AI World/Verification/Inspect First NPC")]
        public static void InspectFirstNpc()
        {
            GameBootstrap bootstrap = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            if (!EditorApplication.isPlaying || bootstrap == null || bootstrap.Session == null ||
                bootstrap.Session.ReadModel.Npcs.Count == 0)
            {
                Debug.LogWarning("Run Bootstrap in Play Mode before inspecting an NPC.");
                return;
            }

            bootstrap.Runner.Pause();
            bootstrap.Selection.Select(bootstrap.Session.ReadModel.Npcs[0].Id);
        }

        [MenuItem("Tools/My AI World/Verification/Review M4 Reciprocal Help")]
        public static void ReviewM4()
        {
            GameBootstrap bootstrap = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "TestResults", "M4-ReciprocityReview.json"));
            if (!EditorApplication.isPlaying || bootstrap == null || !File.Exists(path))
            {
                Debug.LogWarning("Run the PlayMode suite, then start Bootstrap before reviewing M4.");
                return;
            }
            if (!bootstrap.Runner.Load(path)) { Debug.LogWarning(bootstrap.Session.SaveStatus); return; }
            // Defer selection until consumers have reset their views for the restored world.
            double readyAt = EditorApplication.timeSinceStartup + 0.5;
            EditorApplication.CallbackFunction select = null;
            select = () =>
            {
                if (EditorApplication.timeSinceStartup < readyAt) { return; }
                EditorApplication.update -= select;
                if (bootstrap == null || !EditorApplication.isPlaying) { return; }
                var timeline = bootstrap.Session.ReadModel.SocialTimeline;
                if (timeline.Count > 0) { bootstrap.Selection.Select(timeline[timeline.Count - 1].Actor); }
                if (bootstrap.Hud.OpenGlobalPanel != 2) { bootstrap.Hud.ToggleGlobalPanel(2); }
                bootstrap.Hud.SetTimelineFilter(true);
            };
            EditorApplication.update += select;
        }

        [MenuItem("Tools/My AI World/Verification/Capture M4 Review Shot")]
        public static void CaptureM4()
        {
            var bootstrap = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            if (!EditorApplication.isPlaying || bootstrap == null) { return; }
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Progress", "M4"));
            Directory.CreateDirectory(folder);
            bootstrap.CaptureReviewScreenshot(Path.Combine(folder, "social-save-1920.png"));
        }

        [MenuItem("Tools/My AI World/Verification/Build macOS Verification Player")]
        public static void BuildVerificationPlayer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { return; }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
            {
                Debug.LogWarning("macOS build support is not installed."); return;
            }
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Builds", "M4Review.app"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Game/Scenes/Bootstrap.unity" }, locationPathName = path,
                target = BuildTarget.StandaloneOSX, options = BuildOptions.Development
            });
            Debug.Log("M4 verification player: " + report.summary.result + ", errors=" + report.summary.totalErrors + ", " + path);
        }

        [MenuItem("Tools/My AI World/Verification/Run Hunting Field E2E Tests")]
        public static void RunHuntingField()
        {
            Run(TestMode.PlayMode, "MyAiWorld.Tests.PlayMode", "HuntingFieldE2E", "Hunting-Field");
        }

        [MenuItem("Tools/My AI World/Verification/Run Hunting Presentation Tests")]
        public static void RunHuntingPresentation()
        {
            Run(TestMode.PlayMode, "MyAiWorld.Tests.PlayMode", null, "Hunting-Presentation", new[] { "MyAiWorld.Tests.PlayMode.HuntingPresentationTests" });
        }

        [MenuItem("Tools/My AI World/Verification/Run Winter Farming Tests")]
        public static void RunWinterFarming()
        {
            Run(TestMode.PlayMode, "MyAiWorld.Tests.PlayMode", "WinterFarmingE2E", "Winter-Farming");
        }

        private static void Run(TestMode mode, string assembly, string category = null, string reportName = null, string[] testNames = null)
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Wait for compilation and stop Play Mode before running verification.");
                return;
            }

            SessionState.SetString(PendingModeKey, reportName ?? mode + (category == null ? "" : "-" + category));
            Api.Execute(new ExecutionSettings(new Filter
            {
                testMode = mode,
                assemblyNames = new[] { assembly },
                categoryNames = category == null ? null : new[] { category },
                testNames = testNames
            }));
        }

        [Serializable]
        private sealed class VerificationReport
        {
            public string projectPath;
            public string editorVersion;
            public string mode;
            public string result;
            public string completedUtc;
            public int passed;
            public int failed;
            public int skipped;
            public double durationSeconds;
            public List<CaseResult> cases = new List<CaseResult>();
        }

        [Serializable]
        private sealed class CaseResult
        {
            public string name;
            public string result;
            public string message;
            public string stackTrace;
            public double durationSeconds;
        }

        private sealed class ResultRecorder : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                string mode = SessionState.GetString(PendingModeKey, "");
                if (string.IsNullOrEmpty(mode))
                {
                    return;
                }

                SessionState.EraseString(PendingModeKey);
                var report = new VerificationReport
                {
                    projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                    editorVersion = Application.unityVersion,
                    mode = mode,
                    result = result.ResultState,
                    completedUtc = DateTime.UtcNow.ToString("O"),
                    passed = result.PassCount,
                    failed = result.FailCount,
                    skipped = result.SkipCount,
                    durationSeconds = result.Duration
                };
                Collect(result, report.cases);
                string folder = Path.Combine(report.projectPath, "Logs", "TestResults");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, mode + "-CurrentProject.json");
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log("My AI World verification: " + mode + " " + result.ResultState +
                          ", passed=" + result.PassCount + ", failed=" + result.FailCount +
                          ". Report: " + path);
            }

            private static void Collect(ITestResultAdaptor result, List<CaseResult> cases)
            {
                if (!result.HasChildren)
                {
                    cases.Add(new CaseResult
                    {
                        name = result.FullName,
                        result = result.ResultState,
                        message = result.Message,
                        stackTrace = result.StackTrace,
                        durationSeconds = result.Duration
                    });
                    return;
                }

                foreach (ITestResultAdaptor child in result.Children)
                {
                    Collect(child, cases);
                }
            }
        }
    }
}
