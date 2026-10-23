using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.App;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation.Atmosphere;
using MyAiWorld.Presentation.CameraControl;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Presentation.Selection;
using MyAiWorld.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    public sealed partial class GodHud : MonoBehaviour
    {
        private GameSession _session;
        private SimulationRunner _runner;
        private WorldPresenter _presenter;
        private WorldSelectionController _selection;
        private PanelTransition _inspectorPanel;
        private PanelTransition _savePanel;
        private PanelTransition _toastPanel;
        private PanelTransition _helpPanel;
        private readonly PanelTransition[] _globalPanels = new PanelTransition[3];
        private readonly Button[] _navigation = new Button[4];
        private readonly Button[] _speedButtons = new Button[4];
        private readonly Button[] _tabButtons = new Button[3];
        private readonly Button[] _saveButtons = new Button[5];
        private readonly Button[] _atmosphereButtons = new Button[5];
        private readonly GameObject[] _tabs = new GameObject[3];
        private readonly ScrollRect[] _tabScrolls = new ScrollRect[3];
        private readonly Text[] _stockTexts = new Text[4];
        private readonly Text[] _needLabels = new Text[5];
        private readonly Image[] _needFills = new Image[5];
        private readonly Text[] _bagLabels = new Text[3];
        private readonly Dictionary<string, Text> _metrics = new Dictionary<string, Text>();
        private Text _clockText, _timeText, _runText, _pauseLabel;
        private Text _identity, _identityMeta, _currentAction, _historyBody;
        private Text _saveStatus, _toastText, _debugBody, _timelinePageLabel;
        private Button _pauseButton, _timelineAll, _timelineSelected, _timelineOlder, _timelineNewer;
        private Image _portrait, _pauseIcon, _runDot, _toastIcon;
        private readonly Text[] _needValues = new Text[5];
        private readonly Image[] _needIcons = new Image[5];
        private readonly Dictionary<string, Text> _facts = new Dictionary<string, Text>();
        private readonly Text[] _saveSlotStatuses = new Text[4];
        private readonly Image[] _saveSlotDots = new Image[4];
        private Text _rabbitValue, _boarValue, _birthValue, _deathValue, _huntValue, _tradeValue, _treasuryValue;
        private Image _rabbitFill, _boarFill;
        private GameObject _inspectionSection;
        private Button _followButton;
        private GodCameraController _cameraController;
        private NeedsHistoryGraph _historyGraph;
        private HudEntries _relations, _memories, _plan, _events, _ledger, _production, _inspection;
        private HudEntries _ecology;
        private ScrollRect _resourceScroll;
        private GameObject _debugPanel;
        private EntityId? _selected;
        private bool _initialized, _debugVisible, _filterTimeline;
        private int _timelinePage, _worldRevision;
        private float _nextRefresh, _toastUntil, _fps = 60f;
        private string _lastSaveStatus;
        private string _inspectorText = "", _resourceText = "", _economyText = "", _eventText = "";
        public int OpenGlobalPanel { get; private set; } = -1;
        public int InspectorTab { get; private set; }
        public bool InspectorOpen => _inspectorPanel != null && _inspectorPanel.IsOpen;
        public bool SaveMenuOpen => _savePanel != null && _savePanel.IsOpen;
        public bool HelpOpen => _helpPanel != null && _helpPanel.IsOpen;
        public bool ToastVisible => _toastPanel != null && _toastPanel.IsOpen;
        public bool DebugVisible => _debugVisible;
        public string InspectorText => _inspectorText;
        public string ResourceText => _resourceText;
        public string EconomyText => _economyText;
        public string EventText => _eventText;
        public string HistoryText => _historyBody != null ? _historyBody.text : "";
        public string SaveStatusText => _saveStatus != null ? _saveStatus.text : "";

        public void Initialize(GameSession session, SimulationRunner runner, WorldPresenter presenter, WorldSelectionController selection)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _cameraController = Camera.main.GetComponent<GodCameraController>();
            _worldRevision = session.WorldRevision;
            _lastSaveStatus = session.SaveStatus;
            BuildUi();
            _selection.SelectionChanged += OnSelectionChanged;
            _selection.CellSelected += OnCellSelected;
            _initialized = true;
            RefreshAll();
        }

        private void OnDestroy()
        {
            if (_selection != null) { _selection.SelectionChanged -= OnSelectionChanged; _selection.CellSelected -= OnCellSelected; }
        }

        private void Update()
        {
            if (!_initialized) { return; }
            if (_worldRevision != _session.WorldRevision)
            {
                _worldRevision = _session.WorldRevision;
                _selection.Select(null);
                _timelinePage = 0;
            }
            RefreshCampaign();
            HandleShortcuts();
            HudTheme.Select(_followButton, _cameraController.IsFollowing);
            if (Time.unscaledDeltaTime > 0.0001f) { _fps = Mathf.Lerp(_fps, 1f / Time.unscaledDeltaTime, 0.08f); }
            if (_toastUntil > 0f && Time.unscaledTime >= _toastUntil) { DismissToast(); }
            if (Time.unscaledTime < _nextRefresh) { return; }
            _nextRefresh = Time.unscaledTime + 0.2f;
            RefreshAll();
        }

        private void HandleShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseTopmostPanel(); }
            if (CampaignModalOpen || CampaignLocked) { return; }
            if (Input.GetKeyDown(KeyCode.Space)) { _runner.TogglePause(); RefreshTopBar(); }
            for (int i = 0; i < 4; i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) { _runner.SetSpeed(1 << i); RefreshTopBar(); }
            }
            if (Input.GetKeyDown(KeyCode.Period)) { _runner.StepOnce(); RefreshAll(); }
            if (Input.GetKeyDown(KeyCode.F5)) { SaveWorld(); }
            if (Input.GetKeyDown(KeyCode.F9)) { LoadSavedWorld(_session.ManualSavePath); }
            if (Input.GetKeyDown(KeyCode.F)) { FocusSelected(); }
            if (Input.GetKeyDown(KeyCode.H)) { ReturnToVillage(); }
            if (Input.GetKeyDown(KeyCode.F1)) { ToggleDebug(); }
        }

        public void CloseTopmostPanel()
        {
            if (CampaignModalOpen || CampaignLocked)
            {
                if (ContinueObservationConfirmationOpen) { CancelContinueObservation(); }
                else { CloseCampaignResult(); }
                return;
            }
            if (HelpOpen) { ToggleHelp(); }
            else if (SaveMenuOpen) { ToggleSaveMenu(); }
            else if (OpenGlobalPanel >= 0) { CloseGlobalPanels(); }
            else { CloseInspector(); }
        }

        public void ToggleGlobalPanel(int index)
        {
            if (index < 0 || index >= _globalPanels.Length) { return; }
            OpenGlobalPanel = OpenGlobalPanel == index ? -1 : index;
            for (int i = 0; i < _globalPanels.Length; i++) { _globalPanels[i].SetOpen(i == OpenGlobalPanel); }
            RefreshNavigation();
        }

        public void CloseGlobalPanels()
        {
            OpenGlobalPanel = -1;
            foreach (var panel in _globalPanels) { panel.SetOpen(false); }
            RefreshNavigation();
        }

        public void CloseInspector()
        {
            _selection.Select(null);
            _inspectorPanel.SetOpen(false);
        }

        public void HideInspectorPanel()
        {
            if (_inspectorPanel != null) { _inspectorPanel.SetOpen(false); }
        }

        public void ToggleSaveMenu()
        {
            _savePanel.SetOpen(!SaveMenuOpen);
            RefreshSaveStatus();
            RefreshNavigation();
        }

        public void ToggleHelp()
        {
            _helpPanel.SetOpen(!HelpOpen);
            if (HelpOpen) { _helpPanel.transform.SetAsLastSibling(); }
        }

        public void SelectInspectorTab(int index)
        {
            if (index < 0 || index >= _tabs.Length) { return; }
            InspectorTab = index;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].SetActive(i == index);
                HudTheme.Select(_tabButtons[i], i == index);
            }
            Canvas.ForceUpdateCanvases();
            _tabScrolls[index].verticalNormalizedPosition = 1f;
        }

        public void SetTimelineFilter(bool selectedOnly)
        {
            _filterTimeline = selectedOnly;
            _timelinePage = 0;
            RefreshEventLog();
        }

        public void ToggleDebug()
        {
            _debugVisible = !_debugVisible;
            _debugPanel.SetActive(_debugVisible);
            if (_debugVisible) { _debugPanel.transform.SetAsLastSibling(); }
            RefreshDebug();
        }

        private void ToggleAtmosphere(AtmosphereLayer layer)
        {
            AtmospherePresenter atmosphere = _presenter != null ? _presenter.Atmosphere : null;
            if (atmosphere == null) { return; }
            bool enabled = !atmosphere.IsLayerEnabled(layer);
            atmosphere.SetLayerEnabled(layer, enabled);
            if (layer == AtmosphereLayer.Canopy && _presenter.Canopy != null) { _presenter.Canopy.SetEnabled(enabled); }
            RefreshDebug();
        }

        public void DismissToast()
        {
            _toastPanel.SetOpen(false);
            _toastUntil = 0f;
        }

        private void SaveWorld()
        {
            bool success = _session.Save();
            ShowSaveFeedback(success);
            RefreshAll();
        }

        private void LoadSavedWorld(string path)
        {
            bool success = _runner.Load(path);
            if (success) { _selection.Select(null); _timelinePage = 0; }
            ShowSaveFeedback(success);
            RefreshAll();
        }

        private void ShowSaveFeedback(bool success)
        {
            _lastSaveStatus = _session.SaveStatus;
            _toastText.text = _lastSaveStatus;
            _toastText.color = success ? HudTheme.Ink : HudTheme.Danger;
            _toastIcon.sprite = HudIcons.Get(success ? "save" : "alert");
            _toastIcon.color = success ? HudTheme.Positive : HudTheme.Danger;
            _toastUntil = success ? Time.unscaledTime + 4f : 0f;
            _toastPanel.SetOpen(true);
            _toastPanel.transform.SetAsLastSibling();
        }

        private void OnCellSelected(GridPos cell)
        {
            if (OpenGlobalPanel != 0) { return; }
            RefreshResources();
            Canvas.ForceUpdateCanvases(); _resourceScroll.verticalNormalizedPosition = 1f;
        }

        private void OnSelectionChanged(EntityId? selected)
        {
            _cameraController.StopFollowing();
            _selected = selected;
            _timelinePage = 0;
            _inspectorPanel.SetOpen(selected.HasValue);
            RefreshInspector();
            SelectInspectorTab(0);
            RefreshEventLog();
        }

        public void FocusSelected()
        {
            if (!_selected.HasValue) { return; }
            NpcView view = _presenter.FindNpcView(_selected.Value);
            if (view != null) { _cameraController.Focus(view.transform.position); }
        }

        public void ToggleFollowSelected()
        {
            if (_cameraController.IsFollowing) { _cameraController.StopFollowing(); return; }
            if (!_selected.HasValue) { return; }
            NpcView view = _presenter.FindNpcView(_selected.Value);
            if (view != null) { _cameraController.Follow(view.transform); }
        }

        public void ReturnToVillage()
        {
            _cameraController.Focus(_presenter.CellCenter(_session.ReadModel.StockpileCell));
        }

        private void RefreshAll()
        {
            RefreshTopBar();
            RefreshInspector();
            RefreshResources();
            RefreshEventLog();
            RefreshSaveStatus();
            RefreshDebug();
            RefreshCampaign();
        }

        private void RefreshSaveStatus()
        {
            _saveStatus.text = _session.SaveStatus;
            _saveStatus.color = _session.SaveStatus.IndexOf("失败", StringComparison.Ordinal) >= 0 ? HudTheme.Danger : HudTheme.Ink;
            if (_lastSaveStatus != _session.SaveStatus)
            {
                ShowSaveFeedback(_session.SaveStatus.IndexOf("失败", StringComparison.Ordinal) < 0);
            }
            if (!SaveMenuOpen) { return; }
            string[] paths = { _session.ManualSavePath, _session.AutoSavePath, _session.ManualSavePath + ".bak", _session.AutoSavePath + ".bak" };
            for (int i = 1; i < _saveButtons.Length; i++)
            {
                bool exists = File.Exists(paths[i - 1]);
                _saveButtons[i].interactable = exists;
                _saveSlotStatuses[i - 1].text = exists ? "可读取" : "空";
                _saveSlotStatuses[i - 1].color = exists ? HudTheme.Positive : HudTheme.Faint;
                _saveSlotDots[i - 1].color = exists ? HudTheme.Positive : HudTheme.Line;
            }
        }

        private void RefreshNavigation()
        {
            for (int i = 0; i < 3; i++) { HudTheme.Select(_navigation[i], OpenGlobalPanel == i); }
            HudTheme.Select(_navigation[3], SaveMenuOpen);
        }

        private static PanelTransition Transition(RectTransform rect)
        {
            var transition = rect.gameObject.AddComponent<PanelTransition>();
            transition.SetOpen(false, true);
            return transition;
        }
    }
}
