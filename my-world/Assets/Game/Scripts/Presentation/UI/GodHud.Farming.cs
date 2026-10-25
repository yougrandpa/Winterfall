using System.Collections.Generic;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using UnityEngine;

namespace MyAiWorld.Presentation.UI
{
    public sealed partial class GodHud
    {
        private RectTransform _farmPanel;
        private UnityEngine.UI.Text _farmDescription, _farmFeedback;
        private readonly List<UnityEngine.UI.Text> _farmSeedCounts = new List<UnityEngine.UI.Text>();
        private readonly List<UnityEngine.UI.Button> _farmPolicies = new List<UnityEngine.UI.Button>();
        private readonly List<UnityEngine.UI.Button> _bedHeatPolicies = new List<UnityEngine.UI.Button>();
        private int _farmRevision = -1;
        private long _farmPendingTick = -1;
        private int _farmPendingId, _farmPendingValue;
        private bool _farmPendingHeat;
        public string FarmingText => _farmDescription == null ? "" : _farmDescription.text;
        public string FarmingFeedbackText => _farmFeedback == null ? "" : _farmFeedback.text;
        public IReadOnlyList<UnityEngine.UI.Button> FarmPolicyButtons => _farmPolicies;
        public IReadOnlyList<UnityEngine.UI.Button> BedHeatPolicyButtons => _bedHeatPolicies;
        private void BuildFarming(Transform parent)
        {
            _farmPanel = HudLayout.Column(parent, "WinterFarming", 6);
            HudLayout.Section(_farmPanel, "冬季种植方针");
            _farmDescription = HudLayout.Label(_farmPanel, "FarmDescription", "点击农田查看生长条件。", 12);
            _farmFeedback = HudLayout.Label(_farmPanel, "FarmFeedback", "", 12, HudTheme.Gold);
            var seeds = HudLayout.Row(_farmPanel, "WinterSeedReserve", 26, 4);
            for (int i = 1; i <= 3; i++)
            {
                var slot = HudLayout.Rect(seeds, "SeedReserve" + i);
                HudLayout.Flexible(slot);
                UiFactory.CreateIcon(slot, "SeedIcon", _presenter.Art.Get("winter_seed_" + i), new Vector2(0, -2), new Vector2(22, 22));
                _farmSeedCounts.Add(HudLayout.FixedLabel(slot, "SeedCount", "", new Vector2(23, -3), new Vector2(70, 20), 10));
            }
            var row = HudLayout.Row(_farmPanel, "CropPolicies", 30, 3);
            for (int i = 0; i <= (int)FarmCropPolicy.Fallow; i++)
            {
                var choice = (FarmCropPolicy)i;
                var button = HudLayout.Button(row, "FarmPolicy" + i, ChineseText.FarmPolicy(choice), () => RequestFarmPolicy(choice));
                var label = button.GetComponentInChildren<UnityEngine.UI.Text>();
                label.fontSize = 12;
                label.rectTransform.offsetMin = new Vector2(3, 0);
                label.rectTransform.offsetMax = new Vector2(-3, 0);
                _farmPolicies.Add(button);
            }
            HudLayout.Label(_farmPanel, "HeatCaption", "菜床供暖 · 住所保命供暖优先", 12);
            row = HudLayout.Row(_farmPanel, "HeatPolicies", 30, 4);
            for (int i = 0; i <= (int)GrowingBedHeatPolicy.Priority; i++)
            {
                var choice = (GrowingBedHeatPolicy)i;
                _bedHeatPolicies.Add(HudLayout.Button(row, "BedHeatPolicy" + i, ChineseText.BedHeatPolicy(choice), () => RequestBedHeatPolicy(choice)));
            }
        }
        private FarmPlotSnapshot SelectedFarm()
        {
            if (!_selection.SelectedCell.HasValue) { return null; }
            foreach (var p in _session.ReadModel.FarmPlots) { if (p.Cell == _selection.SelectedCell.Value) { return p; } }
            return null;
        }
        private GrowingBedSnapshot FindBed(int id)
        {
            foreach (var bed in _session.ReadModel.Survival.GrowingBeds) { if (bed.Id == id) { return bed; } }
            return null;
        }
        public void RequestFarmPolicy(FarmCropPolicy policy)
        {
            var plot = SelectedFarm();
            if (plot == null || CampaignLocked || _farmPendingTick >= 0) { return; }
            _session.EnqueueCommand(new SetFarmPlotPolicyCommand(plot.Id, policy));
            _farmPendingTick = _session.LastTick; _farmPendingId = plot.Id; _farmPendingValue = (int)policy; _farmPendingHeat = false;
            _farmFeedback.text = "方针待执行 · 恢复或单步后生效"; RefreshFarming();
        }
        public void RequestBedHeatPolicy(GrowingBedHeatPolicy policy)
        {
            var plot = SelectedFarm();
            if (plot == null || plot.BedId == 0 || CampaignLocked || _farmPendingTick >= 0) { return; }
            _session.EnqueueCommand(new SetGrowingBedHeatPolicyCommand(plot.BedId, policy));
            _farmPendingTick = _session.LastTick; _farmPendingId = plot.BedId; _farmPendingValue = (int)policy; _farmPendingHeat = true;
            _farmFeedback.text = "供暖方针待执行 · 恢复或单步后生效"; RefreshFarming();
        }
        private void RefreshFarming()
        {
            if (_farmPanel == null) { return; }
            if (_farmRevision != _session.WorldRevision)
            { _farmRevision = _session.WorldRevision; _farmPendingTick = -1; _farmFeedback.text = ""; }
            if (_farmPendingTick < 0 && _session.PendingFarmingPolicies.Count > 0)
            {
                var pending = _session.PendingFarmingPolicies[0];
                _farmPendingTick = _session.LastTick; _farmPendingId = pending.TargetId; _farmPendingValue = pending.Policy; _farmPendingHeat = pending.Heating;
                _farmFeedback.text = "方针待执行 · 恢复或单步后生效";
            }
            if (_farmPendingTick >= 0 && _session.LastTick > _farmPendingTick)
            {
                bool applied = false;
                if (_farmPendingHeat) { var b = FindBed(_farmPendingId); applied = b != null && (int)b.HeatPolicy == _farmPendingValue; }
                else { foreach (var p in _session.ReadModel.FarmPlots) { if (p.Id == _farmPendingId) { applied = (int)p.Policy == _farmPendingValue; } } }
                _farmFeedback.text = applied ? (_farmPendingHeat ? "供暖方针已生效" : "方针已生效 · 当前作物采收后执行换种或休耕") : "方针未生效 · 请检查目标状态";
                _farmPendingTick = -1;
            }
            var plot = SelectedFarm(); var bed = plot == null ? null : FindBed(plot.BedId);
            for (int i = 0; i < _farmSeedCounts.Count; i++)
            {
                int count = _session.ReadModel.Survival.Stock[(int)CropDefinition.All[i].Seed];
                _farmSeedCounts[i].text = ChineseText.FarmPolicy((FarmCropPolicy)(i + 1)) + " " + count;
                _farmSeedCounts[i].color = count == 0 ? HudTheme.Danger : HudTheme.Ink;
            }
            _farmDescription.text = plot == null ? "点击农田设置作物、休耕与菜床供暖。\n农业已交付仓库／集市 " + _session.ReadModel.Survival.FarmFoodDelivered + " 份" :
                "农田 #" + plot.Id + " · " + ChineseText.Crop(plot.Crop) + " · 方针 " + ChineseText.FarmPolicy(plot.Policy) +
                "\n" + ChineseText.FarmStatus(plot.Status) + (plot.LostCrop ? " · 上茬已枯损" : "") +
                "\n生长 " + plot.GrowthTicks + "/" + plot.GrowthRequired + " · 已采 " + plot.HarvestCount + "/" + CropDefinition.For(plot.Crop).MaxHarvests +
                "\n土温 " + (plot.SoilTemperature / 100f).ToString("0.0") + "°C · 水分 " + plot.Moisture + " · 损伤 " + plot.Damage +
                (bed == null ? "\n露地 · 冻土禁播禁浇" : "\n菜床 #" + bed.Id + " · " + (bed.Complete ? "状态 " + bed.Condition + "%" : "等待施工") + " · 供暖 " + ChineseText.BedHeatPolicy(bed.HeatPolicy) +
                "\n" + ChineseText.FarmStatus(bed.Status) + " · 木材 " + bed.Wood + "\n燃烧余量 " + bed.FuelTicks + "分 · 覆雪 " + bed.SnowDepth) +
                (plot.FinishAfterHarvest ? "\n本次采收后结束原作物，不再复采" : "");
            for (int i = 0; i < _farmPolicies.Count; i++)
            { _farmPolicies[i].interactable = plot != null && !CampaignLocked && _farmPendingTick < 0; HudTheme.Select(_farmPolicies[i], plot != null && (int)plot.Policy == i); }
            for (int i = 0; i < _bedHeatPolicies.Count; i++)
            { _bedHeatPolicies[i].interactable = bed != null && !CampaignLocked && _farmPendingTick < 0; HudTheme.Select(_bedHeatPolicies[i], bed != null && (int)bed.HeatPolicy == i); }
        }
    }
}
