using MyAiWorld.Presentation.Atmosphere;
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
            BuildHelp(root);
            BuildInspector(root);
            BuildResources(root);
            BuildMarket(root);
            BuildEvents(root);
            BuildSaveMenu(root);
            BuildDebug(root);
            BuildToast(root);
            BuildCampaignModal(root);
            SelectInspectorTab(0);
            RefreshNavigation();
        }

        private void BuildTopBar(Transform root)
        {
            var bar = HudLayout.Surface(root, "TopBar", Vector2.up, Vector2.one, Vector2.up,
                new Vector2(16, -16), new Vector2(-32, 102));
            HudTheme.Icon(bar, "CalendarIcon", "clock", new Vector2(18, -22), new Vector2(28, 28), HudTheme.Gold);
            _clockText = HudLayout.FixedLabel(bar, "Date", "", new Vector2(56, -10), new Vector2(220, 18), 12);
            _clockText.color = HudTheme.Gold;
            _timeText = HudLayout.FixedLabel(bar, "Clock", "", new Vector2(56, -28), new Vector2(104, 34), 26);
            _timeText.fontStyle = FontStyle.Bold;
            _runDot = UiFactory.CreatePanel(bar, "RunDot", Vector2.up, Vector2.up, Vector2.up,
                new Vector2(158, -43), new Vector2(6, 6), HudTheme.Positive).GetComponent<Image>();
            _runDot.raycastTarget = false;
            _runText = HudLayout.FixedLabel(bar, "RunState", "", new Vector2(170, -34), new Vector2(108, 20), 11, true);

            var rule = UiFactory.CreatePanel(bar, "TopRule", Vector2.up, Vector2.up, Vector2.up,
                new Vector2(292, -14), new Vector2(1, 44), HudTheme.LineSoft);
            rule.GetComponent<Image>().raycastTarget = false;

            var stock = HudLayout.Rect(bar, "VillageStock");
            HudLayout.Place(stock, new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(480, 52));
            string[] names = { "食物", "饮水", "种子", "村民" };
            for (int i = 0; i < names.Length; i++)
            {
                float x = i * 120;
                UiFactory.CreateIcon(stock, "StockIcon" + i, _presenter.Art.Get("icon_" + i),
                    new Vector2(x + 14, -6), new Vector2(28, 28));
                HudLayout.FixedLabel(stock, "StockLabel" + i, names[i], new Vector2(x + 48, -3), new Vector2(62, 16), 11, true);
                _stockTexts[i] = HudLayout.FixedLabel(stock, "StockValue" + i, "", new Vector2(x + 48, -18), new Vector2(70, 30), 21);
                _stockTexts[i].fontStyle = FontStyle.Bold;
            }

            var rightRule = UiFactory.CreatePanel(bar, "ControlsRule", Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-360, -14), new Vector2(1, 44), HudTheme.LineSoft);
            rightRule.GetComponent<Image>().raycastTarget = false;

            var controls = HudLayout.Rect(bar, "TimeControls");
            HudLayout.Place(controls, Vector2.one, new Vector2(-16, -10), new Vector2(350, 52));
            _pauseButton = HudLayout.IconButton(controls, "PauseButton", "pause", "暂停", new Vector2(0, -6),
                new Vector2(78, 40), delegate { _runner.TogglePause(); RefreshTopBar(); }, out _pauseIcon);
            _pauseLabel = _pauseButton.transform.Find("Label").GetComponent<Text>();
            for (int i = 0; i < 4; i++)
            {
                int speed = 1 << i;
                _speedButtons[i] = HudLayout.FixedButton(controls, "Speedx" + speed, speed + "×",
                    new Vector2(86 + i * 46, -6), new Vector2(42, 40),
                    delegate { _runner.SetSpeed(speed); RefreshTopBar(); });
                _speedButtons[i].GetComponentInChildren<Text>().fontSize = 13;
            }
            _stepButton = HudLayout.IconButton(controls, "StepButton", "step", "单步", new Vector2(272, -6),
                new Vector2(70, 40), delegate { _runner.StepOnce(); RefreshAll(); }, out _);
            BuildCampaignTopBar(bar);
        }

        private void BuildNavigation(Transform root)
        {
            var dock = HudLayout.Surface(root, "Navigation", new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(520, 60));
            string[] names = { "ResourcesButton", "EconomyButton", "EventsButton", "SaveMenuButton" };
            string[] labels = { "村庄物资", "生态与市场", "村庄记事", "存档" };
            string[] icons = { "bag", "coin", "scroll", "save" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                _navigation[i] = HudLayout.IconButton(dock, names[i], icons[i], labels[i],
                    new Vector2(12 + i * 124, -10), new Vector2(116, 40),
                    delegate { if (index == 3) { ToggleSaveMenu(); } else { ToggleGlobalPanel(index); } }, out _);
                _navigation[i].GetComponentInChildren<Text>().fontSize = 13;
            }

            var help = HudLayout.IconButton(root, "HelpButton", "help", "", new Vector2(16, 16), new Vector2(44, 44),
                ToggleHelp, out _);
            HudLayout.Place(help.GetComponent<RectTransform>(), Vector2.zero, new Vector2(16, 16), new Vector2(44, 44));

            var home = HudLayout.IconButton(root, "ReturnToVillage", "home", "回村  H", new Vector2(0, 0),
                new Vector2(132, 44), ReturnToVillage, out _);
            HudLayout.Place(home.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(-16, 16), new Vector2(132, 44));
        }

        private void BuildHelp(Transform root)
        {
            var panel = HudLayout.Surface(root, "ControlGuide", Vector2.zero, new Vector2(16, 76), new Vector2(330, 356));
            HudTheme.Icon(panel, "HelpIcon", "help", new Vector2(16, -14), new Vector2(20, 20), HudTheme.Gold);
            var title = HudLayout.FixedLabel(panel, "HelpTitle", "操作指南", new Vector2(44, -10), new Vector2(200, 28), 16);
            title.fontStyle = FontStyle.Bold;
            HudLayout.CloseButton(panel, "CloseHelp", new Vector2(286, -10), ToggleHelp);
            var line = UiFactory.CreatePanel(panel, "HelpRule", Vector2.up, Vector2.up, Vector2.up,
                new Vector2(16, -48), new Vector2(44, 2), HudTheme.Gold);
            line.GetComponent<Image>().raycastTarget = false;
            var rows = HudLayout.Column(panel, "HelpRows", 3f, 0);
            HudLayout.Place(rows, Vector2.up, new Vector2(16, -58), new Vector2(298, 286));
            HudLayout.KeyRow(rows, "鼠标右键", "拖拽平移视野");
            HudLayout.KeyRow(rows, "滚轮", "缩放世界");
            HudLayout.KeyRow(rows, "鼠标左键", "选择村民");
            HudLayout.KeyRow(rows, "空格", "暂停 / 继续");
            HudLayout.KeyRow(rows, "1 - 4", "切换流速");
            HudLayout.KeyRow(rows, "F", "定位选中村民");
            HudLayout.KeyRow(rows, "H", "回到村庄");
            HudLayout.KeyRow(rows, "Esc", "关闭面板");
            HudLayout.KeyRow(rows, "F1", "调试观察");
            _helpPanel = Transition(panel);
        }

        private RectTransform Detail(Transform root, string name, string title, string icon, int index, int top = 64)
        {
            var panel = HudLayout.Surface(root, name, Vector2.up, new Vector2(16, -130), new Vector2(400, -222));
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.up;
            panel.pivot = Vector2.up;
            Header(panel, title, icon);
            HudLayout.CloseButton(panel, "CloseDetails", new Vector2(352, -14), delegate { ToggleGlobalPanel(index); });
            _globalPanels[index] = Transition(panel);
            return HudLayout.Scroll(panel, "Viewport", top, 16, out _);
        }

        private static void Header(RectTransform panel, string title, string icon)
        {
            HudTheme.Icon(panel, "TitleIcon", icon, new Vector2(18, -16), new Vector2(22, 22), HudTheme.Gold);
            var label = HudLayout.FixedLabel(panel, "Title", title, new Vector2(48, -12), new Vector2(292, 30), 21);
            label.fontStyle = FontStyle.Bold;
            var line = UiFactory.CreatePanel(panel, "HeaderRule", Vector2.up, Vector2.up, Vector2.up,
                new Vector2(18, -49), new Vector2(44, 2), HudTheme.Gold);
            line.GetComponent<Image>().raycastTarget = false;
        }

        private void BuildInspector(Transform root)
        {
            var panel = HudLayout.Surface(root, "NpcInspector", Vector2.one, new Vector2(-16, -130), new Vector2(360, -222));
            panel.anchorMin = Vector2.right;
            panel.anchorMax = Vector2.one;
            panel.pivot = Vector2.one;
            var eyebrow = HudLayout.FixedLabel(panel, "Eyebrow", "村 民 档 案", new Vector2(18, -12), new Vector2(200, 20), 11);
            eyebrow.color = HudTheme.Gold;
            eyebrow.fontStyle = FontStyle.Bold;
            HudLayout.CloseButton(panel, "CloseInspector", new Vector2(316, -10), CloseInspector);
            var portraitFrame = HudLayout.Inset(panel, "PortraitFrame", new Vector2(18, -42), new Vector2(72, 80), HudTheme.Inset);
            var portraitOutline = portraitFrame.gameObject.AddComponent<Outline>();
            portraitOutline.effectColor = HudTheme.GoldSoft;
            portraitOutline.effectDistance = new Vector2(1, -1);
            _portrait = UiFactory.CreateIcon(portraitFrame, "Portrait", null, new Vector2(4, 4), new Vector2(64, 72));

            var nameArea = HudLayout.Rect(panel, "IdentityScrollArea");
            HudLayout.Place(nameArea, Vector2.up, new Vector2(102, -42), new Vector2(240, 46));
            var nameScroll = HudLayout.Scroll(nameArea, "IdentityScroll", 0, 0, 0, 0);
            _identity = HudLayout.Label(nameScroll.content, "Name", "", 22);
            _identity.fontStyle = FontStyle.Bold;
            _identityMeta = HudLayout.FixedLabel(panel, "IdentityMeta", "", new Vector2(102, -88), new Vector2(240, 20), 11, true);

            var actionChip = HudLayout.Inset(panel, "ActionChip", new Vector2(18, -113), new Vector2(324, 28), HudTheme.Inset);
            HudTheme.Icon(actionChip, "ActionIcon", "leaf", new Vector2(9, -5), new Vector2(18, 18), HudTheme.Gold);
            _currentAction = HudLayout.FixedLabel(actionChip, "CurrentAction", "", new Vector2(33, -3), new Vector2(282, 22), 12);
            _currentAction.horizontalOverflow = HorizontalWrapMode.Overflow;

            HudLayout.IconButton(panel, "FocusSelected", "target", "定位", new Vector2(18, -146), new Vector2(100, 30),
                FocusSelected, out _);
            _followButton = HudLayout.IconButton(panel, "FollowSelected", "eye", "跟随", new Vector2(126, -146),
                new Vector2(100, 30), ToggleFollowSelected, out _);

            string[] tabs = { "概览", "关系", "计划" };
            for (int i = 0; i < 3; i++)
            {
                int tab = i;
                _tabButtons[i] = HudLayout.FixedButton(panel, new[] { "OverviewTab", "RelationshipsTab", "PlanTab" }[i],
                    tabs[i], new Vector2(18 + i * 108, -184), new Vector2(104, 30), delegate { SelectInspectorTab(tab); });
                _tabButtons[i].GetComponentInChildren<Text>().fontSize = 13;
                var content = HudLayout.Scroll(panel, new[] { "概览", "关系记忆", "行动计划" }[i], 224, 16, out _tabScrolls[i]);
                _tabs[i] = _tabScrolls[i].gameObject;
                if (i == 0) { BuildOverview(content); }
                else if (i == 1)
                {
                    HudLayout.Section(content, "主要关系");
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
            string[] iconKeys = { "", "", "leaf", "health", "shield" };
            for (int i = 0; i < names.Length; i++)
            {
                var need = HudLayout.Rect(content, "Need_" + i);
                HudLayout.Height(need, 30);
                if (i < 2)
                {
                    _needIcons[i] = UiFactory.CreateIcon(need, "Icon", _presenter.Art.Get("icon_" + i),
                        new Vector2(0, -7), new Vector2(16, 16));
                }
                else
                {
                    _needIcons[i] = HudTheme.Icon(need, "Icon", iconKeys[i], new Vector2(0, -7), new Vector2(16, 16),
                        HudTheme.Needs[i]);
                }
                _needLabels[i] = HudLayout.FixedLabel(need, "Label", names[i], new Vector2(24, -4), new Vector2(180, 18), 12, true);
                _needValues[i] = HudLayout.FixedLabel(need, "Value", "", new Vector2(220, -4), new Vector2(100, 18), 12);
                _needValues[i].alignment = TextAnchor.MiddleRight;
                _needFills[i] = HudLayout.Meter(need, "Track", 5, HudTheme.LineSoft, HudTheme.Needs[i]);
            }

            HudLayout.Section(content, "随身物品");
            var bag = HudLayout.Row(content, "Inventory", 56, 8);
            for (int i = 0; i < 3; i++)
            {
                var slot = HudLayout.Rect(bag, "Item_" + i);
                HudLayout.Flexible(slot);
                var image = slot.gameObject.AddComponent<Image>();
                image.color = HudTheme.Inset;
                image.raycastTarget = false;
                HudTheme.StyleSurface(image);
                UiFactory.CreateIcon(slot, "Icon", _presenter.Art.Get("icon_" + i), new Vector2(10, -16), new Vector2(24, 24));
                _bagLabels[i] = HudLayout.FixedLabel(slot, "Count", "", new Vector2(42, -15), new Vector2(50, 26), 15);
                _bagLabels[i].fontStyle = FontStyle.Bold;
            }

            HudLayout.Section(content, "档案");
            AddFactRow(content, "bag", "背包", "bag", "coins", "钱包", "coin");
            AddFactRow(content, "skill", "狩猎技能", "shield", "tool", "狩猎工具", "target");
            AddFactRow(content, "cell", "位置", "pin", "home", "住所", "home");
            AddFactRow(content, "trade", "买卖", "coin", "output", "产出", "leaf");
            AddFactRow(content, "condition", "状况", "health", null, null, null);

            HudLayout.Section(content, "需求趋势");
            _historyBody = HudLayout.Label(content, "NeedsHistory", "", 11, true);
            _historyBody.alignment = TextAnchor.MiddleLeft;
            HudLayout.Height(_historyBody.rectTransform, 18);
            var graph = HudLayout.Rect(content, "NeedsHistoryGraph");
            HudLayout.Height(graph, 92);
            _historyGraph = graph.gameObject.AddComponent<NeedsHistoryGraph>();
            _historyGraph.raycastTarget = false;
            var legend = HudLayout.Row(content, "Legend", 22, 4);
            for (int i = 0; i < names.Length; i++)
            {
                var text = HudLayout.Label(legend, "Series" + i, names[i], 10);
                text.color = HudTheme.Needs[i];
                text.alignment = TextAnchor.MiddleCenter;
                HudLayout.Flexible(text.rectTransform);
            }
        }

        private void AddFactRow(Transform parent, string keyA, string captionA, string iconA,
            string keyB, string captionB, string iconB)
        {
            var row = HudLayout.Row(parent, "Facts_" + keyA, 27, 14);
            _facts[keyA] = HudLayout.FactCell(row, keyA, captionA, iconA, HudTheme.Gold, out _);
            if (keyB != null)
            {
                _facts[keyB] = HudLayout.FactCell(row, keyB, captionB, iconB, HudTheme.Gold, out _);
            }
            else
            {
                var spacer = HudLayout.Rect(row, "Spacer");
                HudLayout.Flexible(spacer);
            }
        }

        private void AddMetric(Transform parent, string key, string label)
        {
            _metrics.Add(key, HudLayout.Metric(parent, label));
        }

        private void BuildResources(Transform root)
        {
            var content = Detail(root, "ResourceOverview", "村庄物资", "bag", 0);
            _resourceScroll = content.GetComponentInParent<ScrollRect>();
            HudLayout.Section(content, "公共仓库");
            _resourceCells = HudLayout.Grid(content, "Stockpile", new[] { "物品", "库存", "野外" }, 3);
            AddMetric(content, "stockCell", "仓库位置");

            HudLayout.Section(content, "田野与生长");
            var stats = HudLayout.Row(content, "FieldStats", 66, 6);
            _metrics["growing"] = HudLayout.StatCard(stats, "GrowingStat", "生长", "leaf", HudTheme.Positive, out _);
            _metrics["mature"] = HudLayout.StatCard(stats, "MatureStat", "可收获", "bag", HudTheme.Gold, out _);
            _metrics["empty"] = HudLayout.StatCard(stats, "EmptyStat", "空闲", "leaf", HudTheme.Muted, out _);
            _metrics["dry"] = HudLayout.StatCard(stats, "DryStat", "缺水", "alert", HudTheme.Danger, out _);

            HudLayout.Section(content, "需要留意");
            AddMetric(content, "reservations", "资源预留");
            AddMetric(content, "critical", "需求告急村民");

            HudLayout.Section(content, "设施与产出");
            _production = new HudEntries(content, "Production");
            _inspectionSection = HudLayout.Section(content, "选中对象").gameObject;
            _inspection = new HudEntries(content, "WorldInspection");
            _inspectionSection.SetActive(false);
            HudLayout.Section(content, "环境与供给估算");
            _supplyGuide = HudLayout.Label(content, "SupplyEstimateGuide", "", 12);
        }

        private void BuildMarket(Transform root)
        {
            var content = Detail(root, "EcologyEconomy", "生态与市场", "coin", 1);
            HudLayout.Section(content, "山野生灵");
            _rabbitValue = AddPopulationRow(content, "RabbitRow", "野兔", out _rabbitFill);
            _boarValue = AddPopulationRow(content, "BoarRow", "野猪", out _boarFill);
            var births = HudLayout.Row(content, "AnimalEvents", 66, 6);
            _birthValue = HudLayout.StatCard(births, "BirthStat", "出生", "health", HudTheme.Positive, out _);
            _deathValue = HudLayout.StatCard(births, "DeathStat", "自然死亡", "arrow-down", HudTheme.Muted, out _);
            _huntValue = HudLayout.StatCard(births, "HuntStat", "猎获", "target", HudTheme.Gold, out _);
            HudLayout.Section(content, "种群、栖地与迁徙");
            _ecology = new HudEntries(content, "EcologyObservation");

            HudLayout.Section(content, "今日行情");
            _marketCells = HudLayout.Grid(content, "MarketTable", new[] { "物品", "单价", "库存", "买入", "卖出" }, 3);

            HudLayout.Section(content, "交易汇总");
            var trades = HudLayout.Row(content, "TradeStats", 66, 6);
            _tradeValue = HudLayout.StatCard(trades, "TradeStat", "成交 / 拒绝", "coin", HudTheme.Positive, out _);
            _treasuryValue = HudLayout.StatCard(trades, "TreasuryStat", "国库 / 成交额", "coin", HudTheme.Gold, out _);

            HudLayout.Section(content, "最近交易");
            _ledger = new HudEntries(content, "TradeLedger");
        }

        private Text AddPopulationRow(Transform parent, string name, string label, out Image fill)
        {
            var row = HudLayout.Rect(parent, name);
            HudLayout.Height(row, 26);
            HudTheme.Icon(row, "Icon", "paw", new Vector2(0, -2), new Vector2(16, 16), HudTheme.Gold);
            var caption = HudLayout.FixedLabel(row, "Label", label, new Vector2(22, -1), new Vector2(100, 18), 12);
            caption.fontStyle = FontStyle.Bold;
            var value = HudLayout.FixedLabel(row, "Value", "", new Vector2(180, -1), new Vector2(140, 18), 12);
            value.alignment = TextAnchor.MiddleRight;
            fill = null;
            return value;
        }

        private void BuildEvents(Transform root)
        {
            var content = Detail(root, "EventLog", "村庄记事", "scroll", 2, 148);
            var panel = (RectTransform)_globalPanels[2].transform;
            var filters = HudLayout.Row(panel, "Filters", 34);
            HudLayout.Place(filters, Vector2.up, new Vector2(16, -62), new Vector2(368, 34));
            _timelineAll = HudLayout.Button(filters, "TimelineAll", "全部", delegate { SetTimelineFilter(false); });
            _timelineSelected = HudLayout.Button(filters, "TimelineSelected", "当前村民", delegate { SetTimelineFilter(true); });

            var paging = HudLayout.Row(panel, "Paging", 30);
            HudLayout.Place(paging, Vector2.up, new Vector2(16, -104), new Vector2(368, 30));
            var pagingLayout = paging.GetComponent<HorizontalLayoutGroup>();
            pagingLayout.childForceExpandWidth = false;
            _timelineNewer = HudLayout.IconButton(paging, "TimelineNewer", "chevron-left", "", Vector2.zero,
                new Vector2(34, 30), delegate { _timelinePage = System.Math.Max(0, _timelinePage - 1); RefreshEventLog(); }, out _);
            SetFixedWidth(_timelineNewer, 34);
            _timelinePageLabel = HudLayout.Label(paging, "PageNumber", "", 11, true);
            _timelinePageLabel.alignment = TextAnchor.MiddleCenter;
            HudLayout.Flexible(_timelinePageLabel.rectTransform);
            _timelineOlder = HudLayout.IconButton(paging, "TimelineOlder", "chevron-right", "", Vector2.zero,
                new Vector2(34, 30), delegate { _timelinePage++; RefreshEventLog(); }, out _);
            SetFixedWidth(_timelineOlder, 34);
            _events = new HudEntries(content, "Timeline");
        }

        private static void SetFixedWidth(Button button, float width)
        {
            var element = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            element.flexibleWidth = 0;
        }

        private void BuildSaveMenu(Transform root)
        {
            var panel = HudLayout.Surface(root, "SaveMenu", new Vector2(0.5f, 0.5f), new Vector2(0, 12), new Vector2(430, 540));
            Header(panel, "存档", "save");
            HudLayout.CloseButton(panel, "SaveClose", new Vector2(386, -12), ToggleSaveMenu);
            var content = HudLayout.Scroll(panel, "SaveViewport", 66, 16, out _);

            _saveButtons[0] = HudLayout.IconButton(content, "SaveButton", "save", "保存当前世界", Vector2.zero,
                new Vector2(390, 48), SaveWorld, out _);
            HudLayout.Flexible(_saveButtons[0].GetComponent<RectTransform>());
            HudLayout.Height(_saveButtons[0].GetComponent<RectTransform>(), 48);
            _saveButtons[0].GetComponentInChildren<Text>().fontSize = 14;
            HudTheme.Select(_saveButtons[0], true);

            HudLayout.Section(content, "存档栏位");
            _saveButtons[1] = SaveSlot(content, "LoadButton", "手动存档", "save", 0,
                delegate { LoadSavedWorld(_session.ManualSavePath); });
            _saveButtons[2] = SaveSlot(content, "LoadAutoButton", "自动存档", "save", 1,
                delegate { LoadSavedWorld(_session.AutoSavePath); });

            HudLayout.Section(content, "备份");
            _saveButtons[3] = SaveSlot(content, "LoadBackupButton", "手动备份", "scroll", 2,
                delegate { LoadSavedWorld(_session.ManualSavePath + ".bak"); });
            _saveButtons[4] = SaveSlot(content, "LoadAutoBackupButton", "自动备份", "scroll", 3,
                delegate { LoadSavedWorld(_session.AutoSavePath + ".bak"); });

            HudLayout.Section(content, "最近状态");
            _saveStatus = HudLayout.Label(content, "SaveStatus", "", 12);
            _savePanel = Transition(panel);
        }

        private Button SaveSlot(Transform content, string name, string label, string icon, int statusIndex, UnityEngine.Events.UnityAction action)
        {
            var button = HudLayout.IconButton(content, name, icon, label, Vector2.zero, new Vector2(390, 46), action, out _);
            var rect = button.GetComponent<RectTransform>();
            HudLayout.Flexible(rect);
            HudLayout.Height(rect, 46);
            button.transform.Find("Label").GetComponent<Text>().rectTransform.offsetMax = new Vector2(-80, 0);

            _saveSlotDots[statusIndex] = UiFactory.CreatePanel(button.transform, "StatusDot", new Vector2(1, 0.5f),
                new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-72, 0), new Vector2(6, 6), HudTheme.Line)
                .GetComponent<Image>();
            _saveSlotDots[statusIndex].raycastTarget = false;
            _saveSlotStatuses[statusIndex] = UiFactory.CreateText(button.transform, "Status", "", 11,
                TextAnchor.MiddleRight, HudTheme.Muted);
            HudLayout.Place(_saveSlotStatuses[statusIndex].rectTransform, new Vector2(1, 0.5f),
                new Vector2(-12, 0), new Vector2(54, 20));
            return button;
        }

        private void BuildDebug(Transform root)
        {
            var panel = HudLayout.Surface(root, "DebugPanel", new Vector2(0.5f, 1), new Vector2(0, -130), new Vector2(560, -214));
            panel.anchorMin = new Vector2(0.5f, 0);
            panel.anchorMax = new Vector2(0.5f, 1);
            Header(panel, "世界观察", "eye");
            var content = HudLayout.Scroll(panel, "DebugViewport", 64, 62, out _);
            _debugBody = HudLayout.Label(content, "Body", "", 12, true);
            var spawn = HudLayout.IconButton(panel, "SpawnButton", "bag", "生成村民", Vector2.zero,
                new Vector2(120, 36), delegate { _session.SpawnDebugNpc(); }, out _);
            HudLayout.Place(spawn.GetComponent<RectTransform>(), Vector2.zero, new Vector2(16, 16), new Vector2(120, 36));
            string[] labels = { "雾", "光束", "粒子", "树冠", "夜色" };
            for (int i = 0; i < labels.Length; i++)
            {
                AtmosphereLayer layer = (AtmosphereLayer)i;
                int index = i;
                _atmosphereButtons[i] = HudLayout.FixedButton(panel, "AtmosphereLayer" + i, labels[i],
                    new Vector2(148 + i * 76, 16), new Vector2(72, 36), delegate { ToggleAtmosphere(layer); });
                _atmosphereButtons[i].GetComponentInChildren<Text>().fontSize = 12;
                _atmosphereButtons[i].name = "AtmosphereLayer" + index;
            }
            _debugPanel = panel.gameObject;
            _debugPanel.SetActive(false);
        }

        private void BuildToast(Transform root)
        {
            var panel = HudLayout.Surface(root, "SaveStatusPanel", new Vector2(1, 0), new Vector2(-16, 16), new Vector2(360, 54));
            _toastIcon = HudTheme.Icon(panel, "ToastIcon", "save", new Vector2(14, -16), new Vector2(22, 22), HudTheme.Positive);
            var toastScroll = HudLayout.Scroll(panel, "ToastViewport", 44, 6, 44, 6);
            _toastText = HudLayout.Label(toastScroll.content, "Feedback", "", 12);
            HudLayout.CloseButton(panel, "DismissFeedback", new Vector2(324, -12), DismissToast);
            _toastPanel = Transition(panel);
        }
    }
}
