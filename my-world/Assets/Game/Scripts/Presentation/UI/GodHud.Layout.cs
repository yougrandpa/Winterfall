using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    public sealed partial class GodHud
    {
        private Text[] _resourceCells, _marketCells;
        private void BuildUi()
        {
            var canvasObject = new GameObject("GodHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            Transform root = canvasObject.transform;
            BuildTopBar(root);
            BuildNavigation(root);
            BuildInspector(root);
            BuildResources(root);
            BuildMarket(root);
            BuildEvents(root);
            BuildSaveMenu(root);
            BuildDebug(root);
            BuildToast(root);
            SelectInspectorTab(0);
            RefreshNavigation();
        }

        private void BuildTopBar(Transform root)
        {
            var calendar = HudLayout.Surface(root, "Calendar", Vector2.up, new Vector2(16, -16), new Vector2(272, 68));
            _clockText = HudLayout.FixedLabel(calendar, "Date", "", new Vector2(16, -10), new Vector2(170, 22), 14);
            _timeText = HudLayout.FixedLabel(calendar, "Clock", "", new Vector2(16, -32), new Vector2(92, 32), 20);
            _runText = HudLayout.FixedLabel(calendar, "RunState", "", new Vector2(114, -39), new Vector2(144, 22), 12, true);
            var stock = HudLayout.Surface(root, "VillageStock", new Vector2(0.5f, 1), new Vector2(-12, -16), new Vector2(410, 68));
            string[] names = { "食物", "饮水", "种子", "村民" };
            for (int i = 0; i < names.Length; i++)
            {
                float x = 12 + i * 100;
                UiFactory.CreateIcon(stock, "StockIcon" + i, _presenter.Art.Get("icon_" + i), new Vector2(x, -24), new Vector2(24, 24));
                HudLayout.FixedLabel(stock, "StockLabel" + i, names[i], new Vector2(x + 31, -9), new Vector2(62, 20), 12, true);
                _stockTexts[i] = HudLayout.FixedLabel(stock, "StockValue" + i, "", new Vector2(x + 31, -29), new Vector2(64, 30), 20);
            }
            var controls = HudLayout.Surface(root, "TimeControls", Vector2.one, new Vector2(-16, -16), new Vector2(350, 68));
            _pauseButton = HudLayout.FixedButton(controls, "PauseButton", "暂停", new Vector2(10, -16), new Vector2(64, 36),
                delegate { _runner.TogglePause(); RefreshTopBar(); });
            _pauseLabel = _pauseButton.GetComponentInChildren<Text>();
            for (int i = 0; i < 4; i++)
            {
                int speed = 1 << i;
                _speedButtons[i] = HudLayout.FixedButton(controls, "Speedx" + speed, speed + "×", new Vector2(80 + i * 47, -16),
                    new Vector2(42, 36), delegate { _runner.SetSpeed(speed); RefreshTopBar(); });
            }
            HudLayout.FixedButton(controls, "StepButton", "单步", new Vector2(272, -16), new Vector2(68, 36),
                delegate { _runner.StepOnce(); RefreshAll(); });
        }

        private void BuildNavigation(Transform root)
        {
            var dock = HudLayout.Surface(root, "Navigation", new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(468, 52));
            string[] names = { "ResourcesButton", "EconomyButton", "EventsButton", "SaveMenuButton" };
            string[] labels = { "村庄物资", "生态与市场", "村庄记事", "存档" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                _navigation[i] = HudLayout.FixedButton(dock, names[i], labels[i], new Vector2(8 + i * 115, -8), new Vector2(107, 36),
                    delegate { if (index == 3) { ToggleSaveMenu(); } else { ToggleGlobalPanel(index); } });
            }
            var hint = HudLayout.FixedLabel(root, "ControlsHint", "右键拖拽 · 滚轮缩放 · 点击村民\n空格暂停 · F 定位 · H 回村 · F1 调试", Vector2.zero, new Vector2(340, 44), 12);
            HudLayout.Place(hint.rectTransform, Vector2.zero, new Vector2(16, 10), new Vector2(340, 44));
            var cameraDock = HudLayout.Surface(root, "CameraNavigation", Vector2.right, new Vector2(-16, 16), new Vector2(140, 52));
            HudLayout.FixedButton(cameraDock, "ReturnToVillage", "回到村庄  H", new Vector2(8, -8), new Vector2(124, 36), ReturnToVillage);
            hint.color = HudTheme.Paper;
            var shadow = hint.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.08f, 0.15f, 0.1f, 0.5f); shadow.effectDistance = new Vector2(0, -1);
        }

        private RectTransform Detail(Transform root, string name, string title, string subtitle, int index)
        {
            var panel = HudLayout.Surface(root, name, Vector2.up, new Vector2(16, -100), new Vector2(400, -192));
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.up;
            panel.pivot = Vector2.up;
            Header(panel, title, subtitle);
            HudLayout.FixedButton(panel, "CloseDetails", "关闭", new Vector2(332, -16), new Vector2(52, 30), delegate { ToggleGlobalPanel(index); });
            _globalPanels[index] = Transition(panel);
            return HudLayout.Scroll(panel, "Viewport", 82, 16, out _);
        }

        private static void Header(RectTransform panel, string title, string subtitle)
        {
            HudLayout.FixedLabel(panel, "Title", title, new Vector2(18, -16), new Vector2(300, 30), 20);
            HudLayout.FixedLabel(panel, "Subtitle", subtitle, new Vector2(18, -51), new Vector2(364, 22), 12, true);
            var line = UiFactory.CreatePanel(panel, "HeaderRule", Vector2.up, Vector2.up, Vector2.up,
                new Vector2(18, -77), new Vector2(42, 2), HudTheme.Gold);
            line.GetComponent<Image>().raycastTarget = false;
        }

        private void BuildInspector(Transform root)
        {
            var panel = HudLayout.Surface(root, "NpcInspector", Vector2.one, new Vector2(-16, -100), new Vector2(336, -192));
            panel.anchorMin = Vector2.right;
            panel.anchorMax = Vector2.one;
            panel.pivot = Vector2.one;
            HudLayout.FixedLabel(panel, "Eyebrow", "村 民 档 案", new Vector2(16, -12), new Vector2(120, 22), 12, true);
            HudLayout.FixedButton(panel, "FocusSelected", "定位", new Vector2(148, -10), new Vector2(54, 28), FocusSelected);
            _followButton = HudLayout.FixedButton(panel, "FollowSelected", "跟随", new Vector2(208, -10), new Vector2(54, 28), ToggleFollowSelected);
            HudLayout.FixedButton(panel, "CloseInspector", "关闭", new Vector2(268, -10), new Vector2(52, 28), CloseInspector);
            var portraitFrame = HudLayout.Surface(panel, "PortraitFrame", Vector2.up, new Vector2(16, -44), new Vector2(64, 72));
            portraitFrame.GetComponent<Image>().color = HudTheme.Inset;
            portraitFrame.GetComponent<Image>().raycastTarget = false;
            _portrait = UiFactory.CreateIcon(portraitFrame, "Portrait", null, new Vector2(8, -4), new Vector2(48, 64));
            var nameArea = HudLayout.Rect(panel, "IdentityScrollArea");
            HudLayout.Place(nameArea, Vector2.up, new Vector2(92, -44), new Vector2(226, 48));
            var nameScroll = HudLayout.Scroll(nameArea, "IdentityScroll", 0, 0, 0, 0);
            _identity = HudLayout.Label(nameScroll.content, "Name", "", 20);
            _identityMeta = HudLayout.FixedLabel(panel, "IdentityMeta", "", new Vector2(92, -95), new Vector2(226, 22), 12, true);
            _currentAction = HudLayout.FixedLabel(panel, "CurrentAction", "", new Vector2(16, -126), new Vector2(304, 24), 14);
            string[] tabs = { "概览", "关系记忆", "行动计划" };
            for (int i = 0; i < 3; i++)
            {
                int tab = i;
                _tabButtons[i] = HudLayout.FixedButton(panel, new[] { "OverviewTab", "RelationshipsTab", "PlanTab" }[i], tabs[i], new Vector2(16 + i * 103, -162),
                    new Vector2(98, 34), delegate { SelectInspectorTab(tab); });
                var content = HudLayout.Scroll(panel, new[] { "概览", "关系记忆", "行动计划" }[i], 210, 16, out _tabScrolls[i]);
                _tabs[i] = _tabScrolls[i].gameObject;
                if (i == 0) { BuildOverview(content); }
                else if (i == 1)
                {
                    HudLayout.Section(content, "主要关系 · 我对他人的信任");
                    _relations = new HudEntries(content, "Relations");
                    HudLayout.Section(content, "近期记忆");
                    _memories = new HudEntries(content, "Memories");
                }
                else { _plan = new HudEntries(content, "PlanSteps"); }
            }
            _inspectorPanel = Transition(panel);
        }

        private void BuildOverview(RectTransform content)
        {
            HudLayout.Section(content, "生活状态");
            string[] names = { "饱食", "水分", "精力", "健康", "安全" };
            for (int i = 0; i < names.Length; i++)
            {
                var need = HudLayout.Rect(content, "Need_" + i);
                HudLayout.Height(need, 36);
                _needLabels[i] = HudLayout.FixedLabel(need, "Label", "", Vector2.zero, new Vector2(304, 24), 14);
                var track = UiFactory.CreatePanel(need, "Track", Vector2.up, Vector2.one, Vector2.up,
                    new Vector2(0, -29), new Vector2(0, 5), HudTheme.Line);
                track.GetComponent<Image>().raycastTarget = false;
                var fill = UiFactory.CreatePanel(track, "Fill", Vector2.zero, Vector2.one, Vector2.zero,
                    Vector2.zero, Vector2.zero, HudTheme.Needs[i]);
                _needFills[i] = fill.GetComponent<Image>();
                _needFills[i].raycastTarget = false;
            }
            HudLayout.Section(content, "随身物品");
            var bag = HudLayout.Row(content, "Inventory", 48, 6);
            for (int i = 0; i < 3; i++)
            {
                var slot = HudLayout.Rect(bag, "Item_" + i);
                HudLayout.Flexible(slot);
                var image = slot.gameObject.AddComponent<Image>();
                image.color = HudTheme.Inset;
                image.raycastTarget = false;
                HudTheme.StyleSurface(image);
                UiFactory.CreateIcon(slot, "Icon", _presenter.Art.Get("icon_" + i), new Vector2(8, -12), new Vector2(24, 24));
                _bagLabels[i] = HudLayout.FixedLabel(slot, "Count", "", new Vector2(38, -11), new Vector2(52, 28), 14);
            }
            _overview = HudLayout.Label(content, "Overview", "", 14);
            HudLayout.Section(content, "需求变化");
            _historyBody = HudLayout.Label(content, "NeedsHistory", "", 12, true);
            var graph = HudLayout.Rect(content, "NeedsHistoryGraph");
            HudLayout.Height(graph, 90);
            _historyGraph = graph.gameObject.AddComponent<NeedsHistoryGraph>();
            _historyGraph.raycastTarget = false;
            var legend = HudLayout.Row(content, "Legend", 24, 3);
            for (int i = 0; i < names.Length; i++)
            {
                var text = HudLayout.Label(legend, "Series" + i, "● " + names[i], 12);
                text.color = HudTheme.Needs[i];
                HudLayout.Flexible(text.rectTransform);
            }
        }

        private void AddMetric(Transform parent, string key, string label)
        {
            _metrics.Add(key, HudLayout.Metric(parent, label));
        }

        private void BuildResources(Transform root)
        {
            var content = Detail(root, "ResourceOverview", "村庄物资", "一餐一饮，皆是村庄的日常。", 0);
            _resourceScroll = content.GetComponentInParent<ScrollRect>();
            _inspection = new HudEntries(content, "WorldInspection");
            HudLayout.Section(content, "公共仓库");
            _resourceCells = HudLayout.Grid(content, "Stockpile", new[] { "物品", "库存", "野外" }, 3);
            AddMetric(content, "stockCell", "仓库位置");
            HudLayout.Section(content, "田野与生长");
            AddMetric(content, "growing", "生长中的作物");
            AddMetric(content, "mature", "可收获作物");
            AddMetric(content, "empty", "空闲农田");
            AddMetric(content, "dry", "缺水农田");
            HudLayout.Section(content, "需要留意");
            AddMetric(content, "reservations", "资源预留");
            AddMetric(content, "critical", "需求告急村民");
            _production = new HudEntries(content, "Production");
        }

        private void BuildMarket(Transform root)
        {
            var content = Detail(root, "EcologyEconomy", "生态与市场", "山野有生息，村庄有往来。", 1);
            HudLayout.Section(content, "山野生灵");
            AddMetric(content, "rabbits", "野兔");
            AddMetric(content, "boars", "野猪");
            AddMetric(content, "births", "出生 / 自然死亡 / 猎获");
            HudLayout.Label(content, "Protection", "每类保护 4 只 · 种群上限 24 只", 12, true);
            HudLayout.Section(content, "今日行情");
            _marketCells = HudLayout.Grid(content, "MarketTable", new[] { "物品", "单价", "库存", "买入", "卖出" }, 3);
            AddMetric(content, "trades", "成交 / 拒绝");
            AddMetric(content, "treasury", "国库 / 成交额");
            HudLayout.Section(content, "最近交易");
            _ledger = new HudEntries(content, "TradeLedger");
        }

        private void BuildEvents(Transform root)
        {
            var content = Detail(root, "EventLog", "村庄记事", "把每一次相遇，记在这里。", 2);
            var viewport = content.parent.parent.GetComponent<RectTransform>();
            viewport.offsetMax = new Vector2(-16, -172);
            var filters = HudLayout.Row(_globalPanels[2].transform, "Filters", 36);
            _timelineAll = HudLayout.Button(filters, "TimelineAll", "全部", delegate { SetTimelineFilter(false); });
            _timelineSelected = HudLayout.Button(filters, "TimelineSelected", "当前村民", delegate { SetTimelineFilter(true); });
            HudLayout.Place(filters, Vector2.up, new Vector2(16, -88), new Vector2(368, 36));
            var paging = HudLayout.Row(_globalPanels[2].transform, "Paging", 32);
            HudLayout.Place(paging, Vector2.up, new Vector2(16, -132), new Vector2(368, 32));
            _timelineNewer = HudLayout.Button(paging, "TimelineNewer", "较新", delegate { _timelinePage = System.Math.Max(0, _timelinePage - 1); RefreshEventLog(); });
            _timelinePageLabel = HudLayout.Label(paging, "PageNumber", "", 12, true);
            _timelinePageLabel.alignment = TextAnchor.MiddleCenter;
            HudLayout.Flexible(_timelinePageLabel.rectTransform);
            _timelineOlder = HudLayout.Button(paging, "TimelineOlder", "较早", delegate { _timelinePage++; RefreshEventLog(); });
            _events = new HudEntries(content, "Timeline");
        }

        private void BuildSaveMenu(Transform root)
        {
            var panel = HudLayout.Surface(root, "SaveMenu", new Vector2(0.5f, 0.5f), new Vector2(0, 12), new Vector2(400, 480));
            Header(panel, "留住这一刻", "每日午夜自动存档 · F5 保存 / F9 读取");
            HudLayout.FixedButton(panel, "SaveClose", "关闭", new Vector2(332, -16), new Vector2(52, 30), ToggleSaveMenu);
            var content = HudLayout.Scroll(panel, "SaveViewport", 90, 16, out _);
            string[] names = { "SaveButton", "LoadButton", "LoadAutoButton", "LoadBackupButton", "LoadAutoBackupButton" };
            string[] labels = { "保存当前世界", "读取手动存档", "读取自动存档", "读取手动备份", "读取自动备份" };
            for (int i = 0; i < names.Length; i++)
            {
                int action = i;
                _saveButtons[i] = HudLayout.Button(content, names[i], labels[i], delegate
                {
                    if (action == 0) { SaveWorld(); }
                    else
                    {
                        string path = action == 1 ? _session.ManualSavePath : action == 2 ? _session.AutoSavePath :
                            action == 3 ? _session.ManualSavePath + ".bak" : _session.AutoSavePath + ".bak";
                        LoadSavedWorld(path);
                    }
                });
                HudLayout.Height(_saveButtons[i].GetComponent<RectTransform>(), 40);
                HudTheme.Select(_saveButtons[i], i == 0);
            }
            HudLayout.Section(content, "最近状态");
            _saveStatus = HudLayout.Label(content, "SaveStatus", "", 14);
            HudLayout.Label(content, "SaveHelp", "读取后世界会暂停。备份保留上一次存档，自动存档与手动存档分别保存。", 12, true);
            _savePanel = Transition(panel);
        }

        private void BuildDebug(Transform root)
        {
            var panel = HudLayout.Surface(root, "DebugPanel", new Vector2(0.5f, 1), new Vector2(0, -100), new Vector2(560, -184));
            panel.anchorMin = new Vector2(0.5f, 0);
            panel.anchorMax = new Vector2(0.5f, 1);
            Header(panel, "世界观察 · 调试", "F1 收起 · 仅用于开发观察");
            var content = HudLayout.Scroll(panel, "DebugViewport", 84, 62, out _);
            _debugBody = HudLayout.Label(content, "Body", "", 12, true);
            HudLayout.FixedButton(panel, "SpawnButton", "生成村民", new Vector2(16, -16), new Vector2(110, 36), delegate { _session.SpawnDebugNpc(); });
            var spawnRect = panel.Find("SpawnButton").GetComponent<RectTransform>();
            HudLayout.Place(spawnRect, Vector2.zero, new Vector2(16, 16), new Vector2(110, 36));
            _debugPanel = panel.gameObject;
            _debugPanel.SetActive(false);
        }

        private void BuildToast(Transform root)
        {
            var panel = HudLayout.Surface(root, "SaveStatusPanel", new Vector2(1, 0), new Vector2(-16, 16), new Vector2(302, 52));
            var toastScroll = HudLayout.Scroll(panel, "ToastViewport", 12, 8, 50, 8);
            var content = toastScroll.content;
            _toastText = HudLayout.Label(content, "Feedback", "", 12);
            HudLayout.FixedButton(panel, "DismissFeedback", "关闭", new Vector2(254, -10), new Vector2(40, 30), DismissToast);
            _toastPanel = Transition(panel);
        }
    }
}
