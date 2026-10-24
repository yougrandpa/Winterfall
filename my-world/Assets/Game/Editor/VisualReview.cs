using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.CameraControl;
using MyAiWorld.Presentation.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.EditorTools
{
    // Local, explicit requests only. No scene/config regeneration or network work.
    // Requests are written to Logs/VisualReview.request so the Game View can be
    // driven and captured without a second editor instance or a player build.
    [InitializeOnLoad]
    public static class VisualReview
    {
        private const string PlanFile = "Logs/VisualReview.plan";
        private const string CaptureFolder = "Docs/Progress/ArtUpgrade";
        private static CapturePlan _plan;
        private static int _stage;
        private static double _stageAt;
        private static bool _delayed;
        private static bool _done;
        private static double _finishAt;
        private static double _startTimeout;
        private static bool _startRequested;

        private sealed class CapturePlan
        {
            public string name;
            public string preset;
            public int width;
            public int height;
        }

        static VisualReview()
        {
            EditorApplication.update += Update;
            if (File.Exists(PlanFile))
            {
                string raw = File.ReadAllText(PlanFile).Trim();
                _plan = ParsePlan(raw);
                _stage = 0;
                _delayed = false;
                _done = false;
                _startTimeout = EditorApplication.timeSinceStartup + 25;
                Debug.Log("VisualReview: loaded plan '" + raw + "' playing=" + EditorApplication.isPlaying);
            }
        }

        private static void Update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            string request = "Logs/VisualReview.request";
            if (File.Exists(request))
            {
                string command = File.ReadAllText(request).Trim();
                File.Delete(request);
                Handle(command);
            }

            if (_plan != null)
            {
                TickPlan();
            }
        }

        private static void Handle(string command)
        {
            if (command == "refresh")
            {
                AssetDatabase.Refresh();
                return;
            }

            if (command == "bootstrap")
            {
                Finish();
                if (!EditorApplication.isPlaying)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Game/Scenes/Bootstrap.unity");
                }
                return;
            }

            if (command == "live")
            {
                EditorApplication.isPlaying = true;
                return;
            }

            if (command == "generate")
            {
                PastoralArtGenerator.Generate();
                return;
            }

            if (command == "edit")
            {
                ProjectVerification.RunEditMode();
                return;
            }

            if (command == "test-play")
            {
                ProjectVerification.RunPlayMode();
                return;
            }

            if (command == "build")
            {
                ProjectVerification.BuildVerificationPlayer();
                return;
            }

            if (command == "stop")
            {
                Finish();
                EditorApplication.isPlaying = false;
                return;
            }

            if (command.StartsWith("resize-", StringComparison.Ordinal))
            {
                int width;
                int height;
                if (TryParseSize(command.Substring("resize-".Length), out width, out height))
                {
                    ReviewGameView.TrySetResolution(width, height);
                }

                return;
            }

            if (command.StartsWith("capture-", StringComparison.Ordinal) ||
                command.StartsWith("timeline-", StringComparison.Ordinal) ||
                command.StartsWith("scene-", StringComparison.Ordinal) ||
                command.StartsWith("flee-", StringComparison.Ordinal))
            {
                CapturePlan plan = BuildPlan(command);
                if (plan == null)
                {
                    return;
                }

                _plan = plan;
                _stage = 0;
                _stageAt = 0;
                _delayed = false;
                _done = false;
                _finishAt = 0;
                _startRequested = false;
                _startTimeout = EditorApplication.timeSinceStartup + 30;
                Directory.CreateDirectory("Logs");
                File.WriteAllText(PlanFile, SerializePlan(plan));
                if (plan.width > 0 && plan.height > 0)
                {
                    ReviewGameView.TrySetResolution(plan.width, plan.height);
                }

                if (!EditorApplication.isPlaying)
                {
                    _startRequested = true;
                    EditorApplication.isPlaying = true;
                }

                return;
            }

            Debug.LogWarning("VisualReview: unknown request '" + command + "'.");
        }

        private static CapturePlan BuildPlan(string command)
        {
            string body;
            string preset;
            if (command.StartsWith("capture-", StringComparison.Ordinal))
            {
                body = command.Substring("capture-".Length);
                preset = "default";
            }
            else if (command.StartsWith("timeline-", StringComparison.Ordinal))
            {
                body = command.Substring("timeline-".Length);
                preset = "timeline";
            }
            else if (command.StartsWith("scene-", StringComparison.Ordinal))
            {
                body = command.Substring("scene-".Length);
                preset = "scene";
            }
            else
            {
                body = command.Substring("flee-".Length);
                preset = "flee";
            }

            int hash = body.IndexOf('#');
            if (hash >= 0)
            {
                preset = body.Substring(hash + 1);
                body = body.Substring(0, hash);
            }

            var plan = new CapturePlan { name = body, preset = preset };
            int at = body.IndexOf('@');
            if (at >= 0)
            {
                plan.name = body.Substring(0, at);
                int width;
                int height;
                if (!TryParseSize(body.Substring(at + 1), out width, out height))
                {
                    Debug.LogWarning("VisualReview: bad capture size in '" + command + "'.");
                    return null;
                }

                plan.width = width;
                plan.height = height;
            }

            if (string.IsNullOrEmpty(plan.name))
            {
                plan.name = "capture";
            }

            return plan;
        }

        private static bool TryParseSize(string text, out int width, out int height)
        {
            width = 0;
            height = 0;
            string[] parts = text.Split('x');
            return parts.Length == 2 &&
                   int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width) &&
                   int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height) &&
                   width > 0 && height > 0;
        }

        private static string SerializePlan(CapturePlan plan)
        {
            return plan.name + "|" + plan.preset + "|" + plan.width + "|" + plan.height;
        }

        private static CapturePlan ParsePlan(string raw)
        {
            string[] parts = raw.Split('|');
            if (parts.Length != 4)
            {
                return null;
            }

            int width;
            int height;
            int.TryParse(parts[2], out width);
            int.TryParse(parts[3], out height);
            return new CapturePlan { name = parts[0], preset = parts[1], width = width, height = height };
        }

        private static void TickPlan()
        {
            if (!EditorApplication.isPlaying)
            {
                if (!_startRequested && EditorApplication.timeSinceStartup < _startTimeout)
                {
                    _startRequested = true;
                    EditorApplication.isPlaying = true;
                }

                if (EditorApplication.timeSinceStartup > _startTimeout)
                {
                    Debug.LogWarning("VisualReview: play mode did not start.");
                    Finish();
                }

                return;
            }

            GameBootstrap bootstrap = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            if (bootstrap == null || bootstrap.Session == null || bootstrap.Runner == null)
            {
                return;
            }

            if (_done)
            {
                if (EditorApplication.timeSinceStartup >= _finishAt)
                {
                    Finish();
                    EditorApplication.isPlaying = false;
                }

                return;
            }

            if (!_delayed)
            {
                _delayed = true;
                _stageAt = EditorApplication.timeSinceStartup + 1.0;
                return;
            }

            if (EditorApplication.timeSinceStartup < _stageAt)
            {
                return;
            }

            Debug.Log("VisualReview: stage " + _stage + " preset " + _plan.preset);
            switch (_plan.preset)
            {
                case "overview-history":
                case "plan-steps":
                case "market-ledger":
                case "events-social":
                case "overview":
                case "relations":
                case "plan":
                case "save":
                case "failure":
                    TickJournal(bootstrap);
                    break;
                case "timeline":
                    TickTimeline(bootstrap);
                    break;
                case "scene":
                    TickScene(bootstrap);
                    break;
                case "flee":
                    TickFlee(bootstrap);
                    break;
                case "inspector":
                    if (_stage == 0)
                    {
                        PrepareScene(bootstrap, true, -1, true);
                        Next(0.45);
                    }
                    else
                    {
                        CaptureAndFinish(_plan.name);
                    }

                    break;
                case "panels":
                    if (_stage == 0)
                    {
                        bootstrap.Runner.SetSpeed(8);
                        Next(0);
                    }
                    else if (_stage == 1)
                    {
                        if (bootstrap.Session.ReadModel.Tick >= MyAiWorld.Sim.World.GameCalendar.StartTick + 240 || TimedOut(22))
                        {
                            bootstrap.Runner.Pause();
                            PrepareScene(bootstrap, true, 1, false);
                            Next(0.45);
                        }
                    }
                    else
                    {
                        CaptureAndFinish(_plan.name);
                    }

                    break;
                case "resources":
                    if (_stage == 0)
                    {
                        for (int i = 0; i < 800; i++) { bootstrap.Runner.StepOnce(); }
                        PrepareScene(bootstrap, false, 0);
                        Next(0.4);
                    }
                    else
                    {
                        CaptureAndFinish(_plan.name);
                    }

                    break;
                case "market":
                    if (_stage == 0)
                    {
                        for (int i = 0; i < 800; i++) { bootstrap.Runner.StepOnce(); }
                        PrepareScene(bootstrap, false, 1);
                        Next(0.4);
                    }
                    else
                    {
                        CaptureAndFinish(_plan.name);
                    }

                    break;
                case "events":
                    if (_stage == 0)
                    {
                        PrepareScene(bootstrap, false, 2);
                        Next(0.4);
                    }
                    else
                    {
                        CaptureAndFinish(_plan.name);
                    }

                    break;
                case "debug":
                    if (_stage == 0)
                    {
                        PrepareScene(bootstrap, false, -2);
                        Next(0.4);
                    }
                    else
                    {
                        CaptureAndFinish(_plan.name);
                    }

                    break;
                case "help":
                    if (_stage == 0)
                    {
                        PrepareScene(bootstrap, false, -1);
                        if (!bootstrap.Hud.HelpOpen) { bootstrap.Hud.ToggleHelp(); }
                        Next(0.4);
                    }
                    else
                    {
                        CaptureAndFinish(_plan.name);
                    }

                    break;
                case "zoom":
                    if (_stage == 0)
                    {
                        PrepareScene(bootstrap, true, -1, true);
                        SetPixelScale(bootstrap, 3);
                        Next(0.4);
                    }
                    else
                    {
                        CaptureAndFinish(_plan.name);
                    }

                    break;
                default:
                    if (_stage == 0)
                    {
                        PrepareScene(bootstrap, false, -1);
                        Next(0.35);
                    }
                    else
                    {
                        CaptureAndFinish(_plan.name);
                    }

                    break;
            }
        }

        private static void TickJournal(GameBootstrap bootstrap)
        {
            if (_stage == 0)
            {
                PrepareScene(bootstrap, false, -1);
                if (_plan.preset == "relations" || _plan.preset == "events-social")
                {
                    string review = "Logs/TestResults/M4-ReciprocityReview.json";
                    if (File.Exists(review)) { bootstrap.Runner.Load(Path.GetFullPath(review)); }
                }
                else if (_plan.preset.StartsWith("overview", StringComparison.Ordinal) || _plan.preset.StartsWith("plan", StringComparison.Ordinal) || _plan.preset == "market-ledger")
                {
                    for (int i = 0; i < 800; i++) { bootstrap.Runner.StepOnce(); }
                }
                Next(0.4);
                return;
            }
            if (_stage == 1)
            {
                if (_plan.preset == "save" || _plan.preset == "failure")
                {
                    bootstrap.Session.SaveDirectory = Path.GetFullPath("Logs/VisualReview/JournalSaves");
                    Click(bootstrap, "SaveMenuButton");
                    Click(bootstrap, "SaveButton");
                    Click(bootstrap, "SaveButton");
                    bootstrap.Session.Save(bootstrap.Session.AutoSavePath, true);
                    bootstrap.Session.Save(bootstrap.Session.AutoSavePath, true);
                    bootstrap.Hud.DismissToast();
                    if (_plan.preset == "failure")
                    {
                        File.WriteAllText(bootstrap.Session.ManualSavePath, "deliberately damaged review fixture");
                        Click(bootstrap, "LoadButton");
                    }
                }
                else
                {
                    var timeline = bootstrap.Session.ReadModel.SocialTimeline;
                    var id = (_plan.preset == "relations" || _plan.preset == "events-social") && timeline.Count > 0 ? timeline[timeline.Count - 1].Actor : bootstrap.Session.ReadModel.Npcs[0].Id;
                    bootstrap.Selection.Select(id);
                    bootstrap.Hud.ShowSelectedNpcDetails();
                    Click(bootstrap, _plan.preset == "relations" ? "RelationshipsTab" : _plan.preset.StartsWith("plan", StringComparison.Ordinal) ? "PlanTab" : "OverviewTab");
                    if (_plan.preset == "events-social")
                    {
                        Click(bootstrap, "RelationshipsTab"); Click(bootstrap, "EventsButton"); Click(bootstrap, "TimelineSelected");
                    }
                    if (_plan.preset == "market-ledger")
                    {
                        bootstrap.Hud.CloseInspector(); Click(bootstrap, "EconomyButton");
                    }
                }
                Next(0.5);
                return;
            }
            if (_stage == 2)
            {
                if (_plan.preset == "overview-history" || _plan.preset == "plan-steps" || _plan.preset == "market-ledger")
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (ScrollRect scroll in bootstrap.Hud.GetComponentsInChildren<ScrollRect>())
                    {
                        if (scroll.name == "概览" || scroll.name == "行动计划" || scroll.name == "Viewport")
                        { scroll.verticalNormalizedPosition = 0; }
                    }
                }
                if (_plan.preset != "failure") { bootstrap.Hud.DismissToast(); }
                Next(0.3); return;
            }
            CaptureAndFinish(_plan.name);
        }

        private static void Click(GameBootstrap bootstrap, string name)
        {
            foreach (Button button in bootstrap.Hud.GetComponentsInChildren<Button>(true))
            {
                if (button.name == name && button.IsInteractable()) { button.onClick.Invoke(); return; }
            }
        }

        private static void TickTimeline(GameBootstrap bootstrap)
        {
            switch (_stage)
            {
                case 0:
                    bootstrap.Hud.CloseGlobalPanels();
                    bootstrap.Hud.CloseInspector();
                    bootstrap.Runner.SetSpeed(8);
                    Next(0);
                    break;
                case 1:
                    if (AtHour(bootstrap, 12f) || TimedOut(50))
                    {
                        bootstrap.Runner.Pause();
                        Next(0.6);
                    }

                    break;
                case 2:
                    CaptureFile(_plan.name + "-day");
                    Next(0.8);
                    break;
                case 3:
                    bootstrap.Runner.SetSpeed(8);
                    Next(0);
                    break;
                case 4:
                    if (AtHour(bootstrap, 18.6f) || TimedOut(55))
                    {
                        bootstrap.Runner.Pause();
                        Next(0.6);
                    }

                    break;
                case 5:
                    CaptureFile(_plan.name + "-dusk");
                    Next(0.8);
                    break;
                case 6:
                    bootstrap.Runner.SetSpeed(8);
                    Next(0);
                    break;
                case 7:
                    if (AtHour(bootstrap, 21.5f) || TimedOut(55))
                    {
                        bootstrap.Runner.Pause();
                        Next(0.6);
                    }

                    break;
                case 8:
                    CaptureAndFinish(_plan.name + "-night");
                    break;
                default:
                    Finish();
                    break;
            }
        }

        private static void TickScene(GameBootstrap bootstrap)
        {
            if (_plan.name == "coast") { TickSceneCoast(bootstrap); return; }
            if (_plan.name == "canopy") { TickSceneCanopy(bootstrap); return; }
            switch (_stage)
            {
                case 0:
                    bootstrap.Runner.Pause();
                    bootstrap.Hud.CloseGlobalPanels();
                    bootstrap.Hud.CloseInspector();
                    SetPixelScale(bootstrap, 2);
                    Focus(bootstrap, bootstrap.Session.ReadModel.StockpileCell);
                    bootstrap.Runner.SetSpeed(8);
                    Next(0);
                    break;
                case 1:
                    if (AtHour(bootstrap, 12f) || TimedOut(50))
                    {
                        bootstrap.Runner.Pause();
                        Next(0.7);
                    }

                    break;
                case 2:
                    CaptureFile(_plan.name + "-day");
                    bootstrap.Runner.SetSpeed(8);
                    Next(0.8);
                    break;
                case 3:
                    if (AtHour(bootstrap, 18.6f) || TimedOut(55))
                    {
                        bootstrap.Runner.Pause();
                        Next(0.7);
                    }

                    break;
                case 4:
                    CaptureFile(_plan.name + "-dusk");
                    bootstrap.Runner.SetSpeed(8);
                    Next(0.8);
                    break;
                case 5:
                    if (AtHour(bootstrap, 21.5f) || TimedOut(55))
                    {
                        bootstrap.Runner.Pause();
                        Next(0.7);
                    }

                    break;
                case 6:
                    CaptureAndFinish(_plan.name + "-night");
                    break;
                default:
                    Finish();
                    break;
            }
        }

        private static void TickSceneCoast(GameBootstrap bootstrap)
        {
            if (_stage == 0)
            {
                bootstrap.Runner.Pause();
                bootstrap.Hud.CloseGlobalPanels();
                bootstrap.Hud.CloseInspector();
                SetPixelScale(bootstrap, 2);
                bootstrap.Runner.SetSpeed(8);
                Next(0);
                return;
            }

            if (_stage == 1)
            {
                if (AtHour(bootstrap, 12f) || TimedOut(50))
                {
                    bootstrap.Runner.Pause();
                    WorldReadModel model = bootstrap.Session.ReadModel;
                    MyAiWorld.Sim.Core.GridPos origin = model.StockpileCell;
                    MyAiWorld.Sim.Core.GridPos target = origin;
                    int best = int.MaxValue;
                    for (int y = 0; y < model.Height; y++)
                    for (int x = 0; x < model.Width; x++)
                    {
                        if (model.MapTiles[y * model.Width + x] != MyAiWorld.Sim.World.TileType.Water) { continue; }
                        int dx = x - origin.X, dy = y - origin.Y;
                        int distance = dx * dx + dy * dy;
                        if (distance < best) { best = distance; target = new MyAiWorld.Sim.Core.GridPos(x, y); }
                    }
                    Focus(bootstrap, target);
                    Next(0.7);
                }

                return;
            }

            CaptureAndFinish(_plan.name);
        }

        private static void TickSceneCanopy(GameBootstrap bootstrap)
        {
            if (_stage == 0)
            {
                bootstrap.Runner.Pause();
                bootstrap.Hud.CloseGlobalPanels();
                bootstrap.Hud.HideInspectorPanel();
                SetPixelScale(bootstrap, 2);
                WorldReadModel model = bootstrap.Session.ReadModel;
                NpcSnapshot target = FindCanopyNpc(model);
                if (target != null)
                {
                    bootstrap.Selection.Select(target.Id);
                    bootstrap.Hud.HideInspectorPanel();
                    Focus(bootstrap, target.Cell);
                }
                Next(0.8);
                return;
            }

            CaptureAndFinish(_plan.name);
        }

        private static NpcSnapshot FindCanopyNpc(WorldReadModel model)
        {
            NpcSnapshot best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < model.Npcs.Count; i++)
            {
                NpcSnapshot npc = model.Npcs[i];
                int score = 99;
                for (int dy = 0; dy <= 2 && score == 99; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = npc.Cell.X + dx, y = npc.Cell.Y + dy;
                    if (x < 0 || y < 0 || x >= model.Width || y >= model.Height) { continue; }
                    if (model.MapTiles[y * model.Width + x] == MyAiWorld.Sim.World.TileType.Forest)
                    {
                        score = dy * 3 + Mathf.Abs(dx);
                        break;
                    }
                }
                if (score < bestScore) { bestScore = score; best = npc; }
            }
            return best;
        }

        private static void TickFlee(GameBootstrap bootstrap)
        {
            if (_stage == 0)
            {
                PrepareScene(bootstrap, false, -1);
                bootstrap.Runner.SetSpeed(8);
                Next(0);
                return;
            }

            if (_stage == 1)
            {
                AnimalSnapshot target = FindFleeingAnimal(bootstrap);
                bool timeout = TimedOut(40);
                if (target != null || timeout)
                {
                    if (target == null)
                    {
                        target = FirstAnimal(bootstrap);
                    }

                    bootstrap.Runner.Pause();
                    if (target != null)
                    {
                        SetPixelScale(bootstrap, 3);
                        Focus(bootstrap, target.Cell);
                    }

                    Next(0.5);
                }

                return;
            }

            CaptureAndFinish(_plan.name);
        }

        private static void PrepareScene(GameBootstrap bootstrap, bool selectNpc, int panel, bool focus = false)
        {
            bootstrap.Runner.Pause();
            bootstrap.Hud.CloseGlobalPanels();
            if (selectNpc)
            {
                WorldReadModel model = bootstrap.Session.ReadModel;
                if (model.Npcs.Count > 0)
                {
                    NpcSnapshot npc = model.Npcs[0];
                    bootstrap.Selection.Select(npc.Id);
                    if (focus)
                    {
                        Focus(bootstrap, npc.Cell);
                    }
                }
            }
            else
            {
                bootstrap.Hud.CloseInspector();
            }

            if (panel == -2)
            {
                if (!bootstrap.Hud.DebugVisible)
                {
                    bootstrap.Hud.ToggleDebug();
                }
            }
            else if (panel >= 0)
            {
                bootstrap.Hud.ToggleGlobalPanel(panel);
            }

        }

        private static void Focus(GameBootstrap bootstrap, MyAiWorld.Sim.Core.GridPos cell)
        {
            GodCameraController controller = bootstrap.PlayerCamera != null
                ? bootstrap.PlayerCamera.GetComponent<GodCameraController>()
                : null;
            if (controller != null)
            {
                controller.Focus(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, -10f));
            }
        }

        private static void SetPixelScale(GameBootstrap bootstrap, int scale)
        {
            GodCameraController controller = bootstrap.PlayerCamera != null
                ? bootstrap.PlayerCamera.GetComponent<GodCameraController>()
                : null;
            if (controller != null)
            {
                controller.SetPixelScale(scale);
            }
        }

        private static bool AtHour(GameBootstrap bootstrap, float hour)
        {
            return bootstrap.Session.ReadModel.Date.MinuteOfDay / 60f >= hour;
        }

        private static bool TimedOut(double seconds)
        {
            if (_stageAt <= 0)
            {
                return false;
            }

            return EditorApplication.timeSinceStartup >= _stageAt + seconds;
        }

        private static AnimalSnapshot FindFleeingAnimal(GameBootstrap bootstrap)
        {
            IReadOnlyList<AnimalSnapshot> animals = bootstrap.Session.ReadModel.EcologyEconomy.Animals;
            for (int i = 0; i < animals.Count; i++)
            {
                if (animals[i].Fleeing)
                {
                    return animals[i];
                }
            }

            return null;
        }

        private static AnimalSnapshot FirstAnimal(GameBootstrap bootstrap)
        {
            IReadOnlyList<AnimalSnapshot> animals = bootstrap.Session.ReadModel.EcologyEconomy.Animals;
            return animals.Count > 0 ? animals[0] : null;
        }

        private static void CaptureAndFinish(string label)
        {
            CaptureFile(label);
            _done = true;
            _finishAt = EditorApplication.timeSinceStartup + 1.2;
        }

        private static void CaptureFile(string label)
        {
            string folder = label.StartsWith("journal-", StringComparison.Ordinal) ? "Docs/Progress/JournalUI" : CaptureFolder;
            Directory.CreateDirectory(folder);
            string path = folder + "/" + label + ".png";
            FocusGameView();
            GameBootstrap bootstrap = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.CaptureReviewScreenshot(path);
            }
            else
            {
                ScreenCapture.CaptureScreenshot(path);
            }

            EditorWindow focused = EditorWindow.focusedWindow;
            Debug.Log("VisualReview: requested " + path + " at " + Screen.width + "x" + Screen.height +
                      " focused=" + (focused != null ? focused.titleContent.text : "none") + ".");
        }

        private static void FocusGameView()
        {
            EditorWindow gameView = ReviewGameView.FindGameView();
            if (gameView != null)
            {
                gameView.Focus();
                gameView.Repaint();
            }
        }

        private static void Next(double delay)
        {
            _stage++;
            _stageAt = EditorApplication.timeSinceStartup + delay;
        }

        private static void Finish()
        {
            _plan = null;
            _stage = 0;
            _delayed = false;
            _done = false;
            _finishAt = 0;
            _startRequested = false;
            if (File.Exists(PlanFile))
            {
                File.Delete(PlanFile);
            }
        }

        [MenuItem("Tools/My AI World/Art/Capture Review Shot")]
        public static void CaptureManual()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            CaptureFile("manual-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture));
        }
    }
}
