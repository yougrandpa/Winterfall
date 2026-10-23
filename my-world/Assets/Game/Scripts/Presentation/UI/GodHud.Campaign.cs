using System.Globalization;
using System.Text;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.World;
using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    public sealed partial class GodHud
    {
        private GameObject _campaignModal, _campaignActions, _campaignConfirmation;
        private Text _campaignSummary, _environmentSummary, _campaignBody, _campaignWarning, _supplyGuide;
        private readonly Text[] _supplySummaries = new Text[3];
        private Button _campaignSummaryButton, _campaignCloseButton, _stepButton;
        private bool _confirmObservation;
        private int _campaignRevision = -1;

        public Button ContinueObservationButton { get; private set; }
        public Button ConfirmContinueObservationButton { get; private set; }
        public bool CampaignModalOpen => _campaignModal != null && _campaignModal.activeSelf;
        public bool ContinueObservationConfirmationOpen => CampaignModalOpen && _confirmObservation;
        public string CampaignText => _campaignBody != null ? _campaignBody.text : "";
        public string CampaignSummaryText => _campaignSummary != null ? _campaignSummary.text : "";
        public string EnvironmentSummaryText => _environmentSummary != null ? _environmentSummary.text : "";
        private bool CampaignLocked => _session != null && _session.ReadModel.Campaign.IsLocked;

        private void BuildCampaignTopBar(RectTransform bar)
        {
            _campaignSummaryButton = HudLayout.FixedButton(bar, "CampaignResultButton", "",
                new Vector2(18, -72), new Vector2(270, 22), ShowCampaignResult);
            _campaignSummary = _campaignSummaryButton.GetComponentInChildren<Text>();
            _campaignSummary.fontSize = 11;
            _environmentSummary = HudLayout.FixedLabel(bar, "EnvironmentSummary", "",
                new Vector2(302, -74), new Vector2(270, 20), 11);
            for (int i = 0; i < _supplySummaries.Length; i++)
            {
                _supplySummaries[i] = HudLayout.FixedLabel(bar, "SupplySummary" + i, "",
                    new Vector2(584 + i * 210, -74), new Vector2(204, 20), 10);
            }
        }

        private void BuildCampaignModal(Transform root)
        {
            // Full-screen raycast blocker; no backdrop/close action can unlock a settlement.
            var overlay = UiFactory.CreatePanel(root, "CampaignModal", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.04f, 0.07f, 0.06f, 0.76f));
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            _campaignModal = overlay.gameObject;
            var panel = HudLayout.Surface(overlay, "CampaignResult", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 480));
            Header(panel, "寒冬挑战 · 原始成绩", "scroll");
            var content = HudLayout.Scroll(panel, "CampaignViewport", 66, 138, out _);
            _campaignBody = HudLayout.Label(content, "CampaignResultBody", "", 14);
            _campaignWarning = HudLayout.FixedLabel(panel, "CampaignWarning", "",
                new Vector2(22, -350), new Vector2(516, 56), 12);
            _campaignWarning.color = HudTheme.Gold;
            var actions = HudLayout.Rect(panel, "CampaignActions");
            HudLayout.Place(actions, Vector2.up, new Vector2(22, -414), new Vector2(516, 42));
            _campaignActions = actions.gameObject;
            ContinueObservationButton = HudLayout.FixedButton(actions, "ContinueObservationButton", "继续观察…",
                Vector2.zero, new Vector2(300, 42), RequestContinueObservation);
            _campaignCloseButton = HudLayout.FixedButton(actions, "CloseCampaignResultButton", "关闭成绩",
                new Vector2(316, 0), new Vector2(200, 42), CloseCampaignResult);
            var confirmation = HudLayout.Rect(panel, "ContinueObservationConfirmation");
            HudLayout.Place(confirmation, Vector2.up, new Vector2(22, -414), new Vector2(516, 42));
            _campaignConfirmation = confirmation.gameObject;
            ConfirmContinueObservationButton = HudLayout.FixedButton(confirmation, "ConfirmContinueObservationButton", "确认继续 · 成绩不再变化",
                Vector2.zero, new Vector2(320, 42), ConfirmContinueObservation);
            HudLayout.FixedButton(confirmation, "CancelContinueObservationButton", "返回结算",
                new Vector2(336, 0), new Vector2(180, 42), CancelContinueObservation);
            _campaignModal.SetActive(false);
        }

        private void RefreshCampaignTopBar()
        {
            CampaignSnapshot campaign = _session.ReadModel.Campaign;
            EnvironmentSnapshot environment = _session.ReadModel.Environment;
            _campaignSummary.text = campaign.IsUnlimited ? "自由观察 · 无期限" :
                "挑战 " + campaign.ChallengeDay + "/" + campaign.ChallengeDays +
                (campaign.Result == null ? " · 剩余 " + Duration(campaign.RemainingMinutes) :
                    " · 原成绩 " + campaign.Result.Grade + " · 查看");
            _campaignSummaryButton.interactable = campaign.Result != null;
            _environmentSummary.text = (environment.Temperature / 100m).ToString("0.0", CultureInfo.InvariantCulture) + "°C · " +
                ChineseText.Weather(environment.Weather) + " · " + (environment.IsDaytime ? "白昼" : "夜晚") +
                (environment.WaterFrozen ? " · 水源结冰" : "");
            SupplyEstimateSnapshot[] supplies = { environment.Food, environment.Water, environment.Fuel };
            string[] names = { "食", "饮", "柴" };
            for (int i = 0; i < supplies.Length; i++)
            {
                SupplyEstimateSnapshot supply = supplies[i];
                _supplySummaries[i].text = names[i] + " " + supply.Reserve + " · 估 " +
                    (supply.Status == SupplyStatus.NoDemand ? "无需供给" : Duration(supply.Minutes)) + " " + ChineseText.Supply(supply.Status);
                _supplySummaries[i].color = supply.Status == SupplyStatus.Exhausted ? HudTheme.Danger :
                    supply.Status == SupplyStatus.Tight ? HudTheme.Gold : HudTheme.Ink;
            }
            _pauseButton.interactable = !campaign.IsLocked;
            _stepButton.interactable = !campaign.IsLocked;
            _pauseLabel.text = campaign.IsLocked ? "已结算" : _runner.IsPaused ? "继续" : "暂停";
            if (campaign.IsLocked) { _runText.text = "结算锁定"; }
            foreach (Button speed in _speedButtons) { speed.interactable = !campaign.IsLocked; }
        }

        private static string Duration(long minutes) => minutes / 60 + "时" + (minutes % 60).ToString("00") + "分";

        private void RefreshSupplyGuide()
        {
            EnvironmentSnapshot environment = _session.ReadModel.Environment;
            _supplyGuide.text = _environmentSummary.text + "\n风力 " + environment.Wind + "/100 · 日照 " +
                environment.Daylight + "/10000 · 积雪 " + environment.SnowDepth + "\n" +
                "已储存：食物 " + environment.FoodReserve + " · 饮用水 " + environment.WaterReserve +
                " · 木材 " + environment.FuelReserve + "（含住所库存，不含生水、生肉、动物、节点、未收获作物）\n" +
                "按当前存活 " + environment.Living + " 人：食物估 " + Duration(environment.Food.Minutes) +
                " · 饮用水估 " + Duration(environment.Water.Minutes) + "；木材估 " + Duration(environment.Fuel.Minutes) +
                "（连续供暖平摊至 " + environment.OccupiedShelters + " 座有人可用住所）\n" +
                "游戏估算：每人每分钟食物需求 6，每份食物 +4500；饮水需求 9，每份饮用水 +5500。\n" +
                "每木材供暖 " + SurvivalRules.FuelTicksPerWood + " 分钟，每分钟热量 " + SurvivalRules.HeatPerTick +
                "；已燃烧剩余热量计入时长，木材实际也供净水/融雪等用途。\n" +
                "估算 ≥24 小时为充足，低于24小时为紧张，少于1分钟为耗尽；无人需求为无需供给。\n" +
                "这是汇总参考而非存活保证，未计运输可达性、未来产出、湿冷/疾病额外需求及加工耗柴。";
        }

        private void RefreshCampaign()
        {
            if (_campaignRevision != _session.WorldRevision)
            {
                _campaignRevision = _session.WorldRevision;
                _confirmObservation = false;
                _campaignModal.SetActive(false);
            }
            CampaignSnapshot campaign = _session.ReadModel.Campaign;
            if (campaign.IsLocked && !CampaignModalOpen)
            {
                _confirmObservation = false;
                _campaignModal.SetActive(true);
            }
            if (!campaign.IsLocked) { _confirmObservation = false; }
            if (!CampaignModalOpen) { return; }
            if (campaign.Result == null) { _campaignModal.SetActive(false); return; }
            _campaignModal.transform.SetAsLastSibling();
            CampaignResultSnapshot result = campaign.Result;
            _campaignBody.text = "等级 " + result.Grade + " · 第 " + campaign.ChallengeDay + "/" + campaign.ChallengeDays + " 日\n" +
                "原始名单：初始 " + result.InitialCount + " · 存活 " + result.Survivors + "\n" +
                "健康活动 " + result.Active + " · 倒地 " + result.Downed + " · 恢复 " + result.Recovering + "\n" +
                "实际食物 " + result.FoodReserve + " · 饮用水 " + result.WaterReserve + " · 木材 " + result.FuelReserve + "\n" +
                "结算冻结时长：食物 " + FrozenSupply(result.FoodMinutes, result.FoodStatus) +
                " · 饮水 " + FrozenSupply(result.WaterMinutes, result.WaterStatus) +
                " · 供暖 " + FrozenSupply(result.FuelMinutes, result.FuelStatus) + "\n" +
                "结算需求：全世界存活 " + result.ReferenceConsumers + " 人 · 有人可用住所 " + result.OccupiedShelters +
                " 座 · 已燃烧剩余供暖 " + FrozenHours(result.RemainingHeatingMinutes) + "（各住所合计）\n" +
                "原始名单死亡 " + (result.InitialCount - result.Survivors) + "：" + FrozenDeaths(result) + "\n" +
                "结束原因：" + ChineseText.Reason(result.Reason) + "\n" +
                "干预标记：" + (result.Assisted ? "有干预 · 辅助成绩" : "无干预") + "\n" +
                "结束 tick：" + result.EndedTick + " · 成绩已永久冻结\n\n" +
                "储备仅计已入库/背包/地面物资，不计生水、生肉、动物、节点或未收获作物。\n" +
                "以上为结算时冻结的游戏估算，不是实时供给：食物按每位存活者每分钟 6 满足度、每份 +4500；饮用水每分钟 9、每份 +5500。\n" +
                "木材按每份 " + SurvivalRules.FuelTicksPerWood + " 分钟、每分钟 " + SurvivalRules.HeatPerTick +
                " 热量，平摊至有人且可用的住所；剩余时长取燃烧时间与剩余热量可供时间的较小值，不重复计已耗木材。\n" +
                "估算 ≥24 小时为充足，低于24小时为紧张，少于1分钟为耗尽；无人需求为无需供给。\n" +
                "汇总估算不保证运输、可达性或存活；未计未来产出、疾病、湿冷额外消耗及净水/融雪用柴。";
            ContinueObservationButton.gameObject.SetActive(campaign.IsLocked);
            ContinueObservationButton.interactable = campaign.IsLocked;
            _campaignCloseButton.gameObject.SetActive(!campaign.IsLocked);
            _campaignActions.SetActive(!_confirmObservation);
            _campaignConfirmation.SetActive(_confirmObservation && campaign.IsLocked);
            _campaignWarning.text = _confirmObservation ?
                "确认结束挑战锁定并恢复世界流动？继续后的生死与储备不会改变原始成绩；成绩不再变化。" :
                campaign.IsLocked ? "挑战已结算，普通暂停、倍速、单步和 Esc 无法解除锁定。继续观察须二次确认。" :
                "当前处于自由观察；以上为挑战结束时的原始成绩，不随之后的世界变化。";
        }

        private static string FrozenHours(long minutes) =>
            (minutes / 60m).ToString("0.00", CultureInfo.InvariantCulture) + "小时";

        private static string FrozenSupply(long minutes, ReserveStatus status) =>
            FrozenHours(minutes) + "（" + ReserveLabel(status) + "）";

        private static string ReserveLabel(ReserveStatus status)
        {
            switch (status)
            {
                case ReserveStatus.NoDemand: return "无需供给";
                case ReserveStatus.Exhausted: return "耗尽";
                case ReserveStatus.Tight: return "紧张";
                case ReserveStatus.Sufficient: return "充足";
                default: return "未知";
            }
        }

        private static string FrozenDeaths(CampaignResultSnapshot result)
        {
            var text = new StringBuilder();
            for (int i = (int)NpcDeathCause.Cold; i <= (int)NpcDeathCause.Unknown; i++)
            {
                if (text.Length > 0) { text.Append(" · "); }
                text.Append(DeathLabel((NpcDeathCause)i)).Append(' ').Append(result.DeathCounts[i]);
            }
            return text.ToString();
        }

        private static string DeathLabel(NpcDeathCause cause)
        {
            switch (cause)
            {
                case NpcDeathCause.Cold: return "冻死";
                case NpcDeathCause.Starvation: return "饿死";
                case NpcDeathCause.Thirst: return "渴死";
                case NpcDeathCause.Exhaustion: return "耗竭";
                case NpcDeathCause.Disease: return "疾病";
                case NpcDeathCause.Combat: return "战斗";
                default: return "未知";
            }
        }

        public void ShowCampaignResult()
        {
            if (_session.ReadModel.Campaign.Result == null) { return; }
            _campaignRevision = _session.WorldRevision;
            _confirmObservation = false;
            _campaignModal.SetActive(true);
            RefreshCampaign();
        }

        public void CloseCampaignResult()
        {
            if (CampaignLocked) { return; }
            _confirmObservation = false;
            _campaignModal.SetActive(false);
        }

        public void RequestContinueObservation()
        {
            if (!CampaignLocked || !CampaignModalOpen) { return; }
            _confirmObservation = true;
            RefreshCampaign();
        }

        public void CancelContinueObservation()
        {
            _confirmObservation = false;
            RefreshCampaign();
        }

        public void ConfirmContinueObservation()
        {
            if (!_confirmObservation || !CampaignLocked || !CampaignModalOpen) { return; }
            if (!_runner.ContinueObservation()) { return; }
            _confirmObservation = false;
            _campaignModal.SetActive(false);
            RefreshAll();
        }
    }
}
