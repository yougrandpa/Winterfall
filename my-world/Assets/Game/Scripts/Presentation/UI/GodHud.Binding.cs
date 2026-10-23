using System;
using System.Collections.Generic;
using System.Text;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation.Atmosphere;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    public sealed partial class GodHud
    {
        private void RefreshTopBar()
        {
            var model = _session.ReadModel;
            var date = model.Date;
            _clockText.text = "第 " + date.Year + " 年 · " + new[] { "春", "夏", "秋", "冬" }[(int)date.Season] + " " + date.DayOfSeason + " 日";
            _timeText.text = date.ClockText;
            _runText.text = _runner.IsPaused ? "已暂停" : "流动 · " + _runner.SpeedMultiplier + "×";
            _runDot.color = _runner.IsPaused ? HudTheme.Gold : HudTheme.Positive;
            _stockTexts[0].text = model.StockFood.ToString();
            _stockTexts[1].text = model.StockWater.ToString();
            _stockTexts[2].text = model.StockSeeds.ToString();
            _stockTexts[3].text = model.Survival.Living.ToString();
            _pauseLabel.text = _runner.IsPaused ? "继续" : "暂停";
            _pauseIcon.sprite = HudIcons.Get(_runner.IsPaused ? "play" : "pause");
            HudTheme.Select(_pauseButton, _runner.IsPaused);
            for (int i = 0; i < _speedButtons.Length; i++)
            {
                HudTheme.Select(_speedButtons[i], _runner.SpeedMultiplier == (1 << i));
            }
            RefreshCampaignTopBar();
        }

        private string NpcName(EntityId id)
        {
            var npc = _session.ReadModel.GetDetails(id);
            return npc != null ? npc.Name : "#" + id.Value;
        }

        private string SocialDescription(SocialEvent evt)
        {
            return NpcName(evt.Actor) + (evt.Kind == SocialKind.FoodGift ? " → 赠送食物 → " : " → 问候 → ") + NpcName(evt.Target) +
                "\n" + ChineseText.Reason(evt.Reason) + "\n" + NpcName(evt.Target) + " 对 " + NpcName(evt.Actor) + " 的信任 " + evt.TrustBefore + " → " + evt.TrustAfter +
                (evt.CauseSequence > 0 ? "\n回报帮助 #" + evt.CauseSequence : "");
        }

        private void RefreshInspector()
        {
            NpcDetails details = _selected.HasValue ? _session.ReadModel.GetDetails(_selected.Value) : null;
            if (details == null)
            {
                _inspectorText = "";
                _historyBody.text = "";
                _historyGraph.SetHistory(null);
                _inspectorPanel.SetOpen(false);
                return;
            }
            if (_identity.text != details.Name)
            { _identity.GetComponentInParent<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition = 1; }
            _identity.text = details.Name;
            _identityMeta.text = ChineseText.Role(details.Role) + " · " + details.Age + " 岁 · #" + details.Id.Value;
            _currentAction.text = ChineseText.Goal(details.GoalType) + " · " + ChineseText.Action(details.ActionType);
            if (!string.IsNullOrEmpty(ChineseText.Activity(details.ActionLabel))) { _currentAction.text += " · " + ChineseText.Activity(details.ActionLabel); }
            if (details.Life != LifeState.Active) { _currentAction.text = ChineseText.Life(details.Life); }
            _portrait.sprite = _presenter.Art.Get("npc_" + NpcView.RoleIndex(details.Role) + "_0_0_0");

            int[] values = { details.Food, details.Water, details.Energy, details.Health, details.Safety };
            for (int i = 0; i < values.Length; i++)
            {
                bool critical = values[i] < 2500;
                _needIcons[i].color = i < 2 ? Color.white : HudTheme.Needs[i];
                _needValues[i].text = values[i] / 100 + "%";
                _needValues[i].color = critical ? HudTheme.Danger : HudTheme.Ink;
                HudLayout.SetMeter(_needFills[i], values[i] / 10000f);
            }

            int[] bag = { details.BagFood, details.BagWater, details.BagSeeds };
            for (int i = 0; i < bag.Length; i++) { _bagLabels[i].text = bag[i].ToString(); }

            SetFact("bag", details.BagTotal + " / " + details.BagCapacity);
            SetFact("coins", details.Coins.ToString(), HudTheme.Gold);
            SetFact("skill", details.HuntingSkill.ToString());
            string tool = "无";
            if (details.Tools[0] > 0) { tool = "弓 " + details.Tools[0] + "/" + SurvivalRules.ToolDurability; }
            else if (details.Tools[1] > 0) { tool = "矛 " + details.Tools[1] + "/" + SurvivalRules.ToolDurability; }
            SetFact("tool", tool);
            SetFact("cell", details.Cell.ToString());
            SetFact("home", details.Home.ToString());
            SetFact("trade", details.Purchases + " / " + details.Sales);
            SetFact("output", details.HarvestedFood + " / " + details.GatheredFood + " / " + details.HuntKills);
            string condition = details.Life == LifeState.Downed ? "倒地 · " + details.RescueRemaining + " 分" :
                details.DiseaseTicks > 0 ? "患病 · " + details.DiseaseTicks + " 分" : ChineseText.Life(details.Life);
            SetFact("condition", condition, details.Life == LifeState.Active && details.DiseaseTicks == 0 ? HudTheme.Positive : HudTheme.Danger);

            _historyBody.text = details.NeedsHistory.Count < 2 ? "采样中" : details.NeedsHistory.Count + " / 24 采样";
            _historyGraph.SetHistory(details.NeedsHistory);
            RefreshRelations(details);
            RefreshPlan(details);

            var builder = new StringBuilder();
            builder.Append(_identity.text).Append('\n').Append(_identityMeta.text).Append('\n').Append(_currentAction.text).Append('\n');
            for (int i = 0; i < _needValues.Length; i++) { builder.Append(_needLabels[i].text).Append(' ').Append(_needValues[i].text).Append('\n'); }
            builder.Append("随身物品\n食物 ").Append(details.BagFood).Append(" / 饮水 ").Append(details.BagWater)
                .Append(" / 种子 ").Append(details.BagSeeds).Append("\n需求历史\n").Append(_historyBody.text).Append('\n');
            builder.Append("背包 ").Append(_facts["bag"].text).Append('\n')
                .Append("钱包 ").Append(_facts["coins"].text).Append('\n')
                .Append("狩猎技能 ").Append(_facts["skill"].text).Append('\n')
                .Append("狩猎工具 ").Append(_facts["tool"].text).Append('\n')
                .Append("位置 ").Append(_facts["cell"].text).Append('\n')
                .Append("住所 ").Append(_facts["home"].text).Append('\n')
                .Append("买卖 ").Append(_facts["trade"].text).Append('\n')
                .Append("产出 ").Append(_facts["output"].text).Append('\n')
                .Append("状况 ").Append(_facts["condition"].text).Append('\n');
            builder.Append("主要关系\n").Append(_relations.Text)
                .Append("近期记忆\n").Append(_memories.Text)
                .Append("行动计划\n").Append(_plan.Text);
            _inspectorText = builder.ToString();
        }

        private void SetFact(string key, string value, Color? color = null)
        {
            Text text;
            if (!_facts.TryGetValue(key, out text)) { return; }
            text.text = value;
            text.color = color ?? HudTheme.Ink;
        }

        private void RefreshRelations(NpcDetails details)
        {
            _relations.Begin();
            var sorted = new List<TrustSnapshot>(details.Relations);
            sorted.Sort((a, b) => a.Trust != b.Trust ? b.Trust.CompareTo(a.Trust) : a.Other.Value.CompareTo(b.Other.Value));
            foreach (var relation in sorted)
            {
                Color accent = relation.Trust >= 70 ? HudTheme.Positive : relation.Trust >= 40 ? HudTheme.Gold : HudTheme.Muted;
                _relations.Add(NpcName(relation.Other), "信任 " + relation.Trust + " / 100", accent, relation.Trust / 100f, HudTheme.Ink);
            }
            if (sorted.Count == 0) { _relations.Add("暂无关系", "尚无记录", HudTheme.Gold, 0f, HudTheme.Muted); }
            _relations.End();
            _memories.Begin();
            for (int i = details.Memories.Count - 1; i >= 0; i--)
            {
                var memory = details.Memories[i];
                _memories.Add(ChineseText.Date(GameCalendar.FromTick(memory.Tick)) + " · #" + memory.Sequence, SocialDescription(memory), HudTheme.Gold);
            }
            if (details.Memories.Count == 0) { _memories.Add("暂无记忆", "尚无记录", HudTheme.Gold, 0f, HudTheme.Muted); }
            _memories.End();
        }

        private void RefreshPlan(NpcDetails details)
        {
            _plan.Begin();
            _plan.Add("当前目标", ChineseText.Goal(details.GoalType) + "\n原因 · " + ChineseText.Reason(details.DecisionReason), HudTheme.Gold);
            if (details.Steps.Count == 0) { _plan.Add("行动计划", "等待下一次判断", HudTheme.Muted, 0f, HudTheme.Muted); }
            for (int i = 0; i < details.Steps.Count; i++)
            {
                var step = details.Steps[i];
                string state = details.IsPlanFailed ? i == details.FailedStepIndex ? "失败" : i < details.FailedStepIndex ? "已完成" : "已取消" :
                    i < details.StepIndex ? "已完成" : i == details.StepIndex ? "进行中" : "待办";
                Color accent = state == "失败" ? HudTheme.Danger : state == "进行中" ? HudTheme.Positive :
                    state == "已完成" ? HudTheme.Muted : HudTheme.Gold;
                string body = ChineseText.Step(step.Type) + " " + step.Target;
                if (step.Type == PlanStepType.Greet || step.Type == PlanStepType.GiveFood)
                {
                    body += " · " + NpcName(new EntityId(step.ResourceId));
                }
                if (step.Quantity > 0 && step.Type != PlanStepType.Greet && step.Type != PlanStepType.GiveFood)
                { body += " · " + ChineseText.Item(step.Item) + " × " + step.Quantity; }
                if (step.UnitPrice > 0) { body += " · 单价 " + step.UnitPrice; }
                _plan.Add("行动 " + (i + 1).ToString("00") + " · " + state, body, accent, -1f,
                    state == "失败" ? HudTheme.Danger : state == "进行中" ? HudTheme.Positive : HudTheme.Muted);
            }
            if (details.FailureReason != "none") { _plan.Add("最近失败", ChineseText.Reason(details.FailureReason), HudTheme.Danger); }
            _plan.Add("重新规划", details.ReplanCount + " 次", HudTheme.Muted, -1f, HudTheme.Muted);
            _plan.End();
        }

        private void SetMetric(string key, object value)
        {
            _metrics[key].text = value.ToString();
        }

        private void RefreshResources()
        {
            RefreshSupplyGuide();
            var model = _session.ReadModel;
            var economy = model.EcologyEconomy;
            int[] stock = { model.StockFood, model.StockWater, model.StockSeeds };
            int[] wild = { model.TotalNodeFood, model.TotalNodeWater, 0 };
            for (int i = 0; i < 3; i++)
            {
                _resourceCells[i * 3].text = ChineseText.Item((ItemKind)i);
                _resourceCells[i * 3 + 1].text = stock[i].ToString();
                _resourceCells[i * 3 + 2].text = i == 0 ? wild[i].ToString() : i == 1 ? "生水 " + wild[i] : "—";
                string[] values = { ChineseText.Item((ItemKind)i), economy.Prices[i].ToString(), economy.Stocks[i].ToString(), economy.Bought[i].ToString(), economy.Sold[i].ToString() };
                for (int j = 0; j < 5; j++) { _marketCells[i * 5 + j].text = values[j]; }
                _marketCells[i * 5 + 3].color = economy.Bought[i] > 0 ? HudTheme.Positive : HudTheme.Muted;
                _marketCells[i * 5 + 4].color = economy.Sold[i] > 0 ? HudTheme.Gold : HudTheme.Muted;
            }
            SetMetric("stockCell", model.StockpileCell);
            SetMetric("growing", model.GrowingCropCount);
            SetMetric("mature", model.MatureCropCount);
            SetMetric("empty", model.EmptyPlotCount);
            SetMetric("dry", model.DryCropCount);
            SetMetric("reservations", model.ReservationCount);
            SetMetric("critical", model.CriticalNpcCount);
            _metrics["critical"].color = model.CriticalNpcCount > 0 ? HudTheme.Danger : HudTheme.Ink;
            SetPopulation(_rabbitValue, economy.Rabbits);
            SetPopulation(_boarValue, economy.Boars);
            _birthValue.text = economy.Births.ToString();
            _deathValue.text = economy.NaturalDeaths.ToString();
            _huntValue.text = economy.HuntKills.ToString();
            _tradeValue.text = economy.Filled + " / " + economy.Rejected;
            _treasuryValue.text = economy.Treasury + " / " + economy.Volume;
            RefreshEcology(economy);

            _ledger.Begin();
            for (int i = economy.RecentTrades.Count - 1; i >= 0; i--)
            {
                var trade = economy.RecentTrades[i];
                bool buy = trade.Order.Side == TradeSide.Buy;
                _ledger.Add(ChineseText.Date(GameCalendar.FromTick(trade.Tick)) + " · #" + trade.Order.Id + " · " + ChineseText.Reason(trade.Reason),
                    NpcName(trade.Order.NpcId) + (buy ? "购入 " : "售出 ") + trade.Order.Quantity + " " + ChineseText.Item(trade.Order.Item) + " · 单价 " + trade.Order.UnitPrice,
                    buy ? HudTheme.Positive : HudTheme.Gold);
            }
            if (economy.RecentTrades.Count == 0) { _ledger.Add("市场刚刚开张", "暂无交易记录", HudTheme.Gold, 0f, HudTheme.Muted); }
            _ledger.End();

            _production.Begin();
            var survival = model.Survival;
            _production.Add("生存状态", "存活 " + survival.Living + " · 待援 " + survival.Downed + " · 恢复 " + survival.Recovering + " · 逝世 " + survival.Dead,
                survival.Downed > 0 ? HudTheme.Danger : HudTheme.Positive);
            _production.Add("备粮与供水", "食物目标 " + survival.FoodTarget + " · 饮用水目标 " + survival.WaterTarget + (survival.RefillWater ? " · 正在补水" : " · 储水充足") + "\n作物损失 " + survival.CropsLost + " · 鹿 " + economy.Deer);
            var inventory = new StringBuilder();
            for (int i = 3; i < survival.Stock.Count; i++) { inventory.Append(ChineseText.Item((ItemKind)i)).Append(' ').Append(survival.Stock[i]).Append("  "); }
            _production.Add("生产物资", inventory.ToString());
            foreach (var facility in survival.Facilities)
            {
                var contents = new StringBuilder();
                for (int i = 0; i < facility.Items.Count; i++) { if (facility.Items[i] > 0) { contents.Append(ChineseText.Item((ItemKind)i)).Append(' ').Append(facility.Items[i]).Append("  "); } }
                _production.Add((_selection.SelectedCell == facility.Cell ? "选中 · " : "") + ChineseText.Facility(facility.Kind) + " #" + facility.Id,
                    facility.Cell + " · " + (facility.Complete ? "已建成 · 状态 " + facility.Condition + "%" : "施工 " + facility.Progress + "/" + SurvivalRules.ConstructionTicks) + "\n" + (facility.ReservedBy.IsNone ? "空闲" : "工作者 " + NpcName(facility.ReservedBy)) + "\n" + contents,
                    facility.Complete ? HudTheme.Gold : HudTheme.Muted);
            }
            _production.End();

            _inspection.Begin();
            if (_selection.SelectedCell.HasValue)
            {
                GridPos cell = _selection.SelectedCell.Value;
                foreach (var plot in model.FarmPlots) { if (plot.Cell == cell) { _inspection.Add("选中农田 #" + plot.Id + " · " + ChineseText.Crop(plot.Crop), "生长 " + plot.GrowthTicks + "/" + plot.GrowthRequired + " · 水分 " + plot.Moisture + "\n肥力 " + plot.Fertility / 100 + "% · 损伤 " + plot.Damage + " · 前茬 " + ChineseText.Crop(plot.PreviousCrop), HudTheme.Positive); } }
                foreach (var animal in economy.Animals)
                {
                    if (animal.Cell != cell) { continue; }
                    _inspection.Add("选中动物 #" + animal.Id.Value + " · " + ChineseText.Species(animal.Species),
                        AnimalDescription(animal) + "\n猎人 " + (animal.ReservedBy.IsNone ? "无" : NpcName(animal.ReservedBy)), HudTheme.Gold);
                }
                foreach (var carcass in economy.Carcasses)
                {
                    if (carcass.Cell != cell) { continue; }
                    _inspection.Add("选中尸体 #" + carcass.Id.Value + " · " + ChineseText.Species(carcass.Species),
                        CarcassDescription(carcass) + "\n回收者 " + (carcass.ReservedBy.IsNone ? "无" : NpcName(carcass.ReservedBy)), HudTheme.Muted);
                }
                foreach (var habitat in economy.Habitats)
                {
                    if (habitat.Contains(cell)) { _inspection.Add("选中栖地 #" + habitat.Id, HabitatDescription(habitat), HudTheme.Positive); }
                }
                foreach (var node in model.ResourceNodes) { if (node.Cell == cell) { _inspection.Add("选中资源 #" + node.Id, ChineseText.Item(node.Item) + " " + node.Amount + "/" + node.Capacity, HudTheme.Gold); } }
                foreach (var ground in survival.Ground) { if (_selection.SelectedCell == ground.Cell) { _inspection.Add("待回收物资 #" + ground.Id, "物品 " + ground.Count + " · 钱币 " + ground.Coins, HudTheme.Gold); } }
            }
            _inspection.End();
            if (_inspectionSection != null) { _inspectionSection.SetActive(_selection.SelectedCell.HasValue); }

            _resourceText = "公共库存 " + model.StockpileCell + "\n食物 " + model.StockFood + " / 饮水 " + model.StockWater + " / 种子 " + model.StockSeeds +
                "\n野外资源 · 食物 " + model.TotalNodeFood + " / 生水 " + model.TotalNodeWater +
                "\n作物 · 生长 " + model.GrowingCropCount + " / 可收获 " + model.MatureCropCount +
                "\n农田 · 空闲 " + model.EmptyPlotCount + " / 缺水 " + model.DryCropCount +
                "\n资源预留 " + model.ReservationCount + " / 需求告急村民 " + model.CriticalNpcCount;
            _resourceText += "\n" + _inspection.Text + _production.Text + "\n环境与供给估算\n" + _supplyGuide.text;
            _economyText = "野兔 " + economy.Rabbits + " / 野猪 " + economy.Boars + "\n出生 " + economy.Births + " / 自然死亡 " + economy.NaturalDeaths + " / 猎获 " + economy.HuntKills +
                "\n单价 · 食物 " + economy.Prices[0] + " / 饮水 " + economy.Prices[1] + " / 种子 " + economy.Prices[2] +
                "\n市场库存 · 食物 " + economy.Stocks[0] + " / 饮水 " + economy.Stocks[1] + " / 种子 " + economy.Stocks[2] +
                "\n买入 · 食物 " + economy.Bought[0] + " / 饮水 " + economy.Bought[1] + " / 种子 " + economy.Bought[2] +
                "\n卖出 · 食物 " + economy.Sold[0] + " / 饮水 " + economy.Sold[1] + " / 种子 " + economy.Sold[2] +
                "\n成交 " + economy.Filled + " / 拒绝 " + economy.Rejected + "\n国库 " + economy.Treasury + " / 成交额 " + economy.Volume + "\n" + _ledger.Text + _ecology.Text;
        }

        private static void SetPopulation(Text value, int count)
        {
            if (value != null) { value.text = count.ToString(); }
        }

        private static string AnimalId(EntityId id) => id.IsNone ? "无" : "#" + id.Value;

        private static string AnimalDescription(AnimalSnapshot animal)
        {
            return ChineseText.AnimalActivity(animal.Behavior) + " · 生命 " + animal.Health + (animal.Fleeing ? " · 逃跑中" : "") +
                "\n能量 " + animal.Energy + "/" + animal.EnergyCapacity + " · 水分 " + animal.Hydration + " · 体温 " + (animal.CoreTemperature / 100f).ToString("0.0") + "℃" +
                "\n年龄 " + animal.AgeTicks / GameCalendar.TicksPerDay + " 天 · " + ChineseText.AnimalAge(animal.LifeStage) + " · " + ChineseText.AnimalSexName(animal.Sex) +
                "\n群体 #" + animal.GroupId + " · 领地 #" + animal.TerritoryHabitatId + " · 栖地 #" + animal.HabitatId +
                "\n母亲 " + AnimalId(animal.MotherId) + " · 父亲 " + AnimalId(animal.FatherId) + " · 配偶 " + AnimalId(animal.MateId) +
                "\n妊娠 " + animal.PregnancyTicks + " 分 · 胎数 " + animal.PregnancyLitter + " · 胎父 " + AnimalId(animal.PregnancyFatherId) +
                "\n区域 " + ChineseText.Region(animal.Region) + " · 迁徙目的地 " + ChineseText.Region(animal.MigrationDestination) +
                " · 路径 " + animal.MigrationPathIndex + "/" + animal.MigrationPath.Count + " · 上次迁徙 " + (animal.LastMigrationTick < 0 ? "无" : "tick " + animal.LastMigrationTick);
        }

        private static string CarcassDescription(CarcassSnapshot carcass)
        {
            var items = new StringBuilder();
            for (int i = 0; i < carcass.Items.Count; i++)
            {
                if (carcass.Items[i] > 0) { items.Append(ChineseText.Item((ItemKind)i)).Append(' ').Append(carcass.Items[i]).Append("  "); }
            }
            return "死因 " + ChineseText.AnimalDeath(carcass.DeathCause) + " · 来源 " + AnimalId(carcass.SourceId) +
                "\n生肉 " + carcass.MeatRemaining + " · 兽皮 " + carcass.HideRemaining + " · 新鲜度 " + carcass.Freshness / 100 + "%" +
                "\n" + carcass.Cell + " · 栖地 #" + carcass.HabitatId + (items.Length > 0 ? "\n嵌入物资 " + items : "");
        }

        private static string HabitatDescription(HabitatSnapshot habitat)
        {
            return ChineseText.Region(habitat.Region) + " · 陆地 " + habitat.LandCells + " 格" +
                "\n草本 " + habitat.Herbaceous.Biomass + "/" + habitat.Herbaceous.Capacity +
                " · 灌木 " + habitat.Shrubs.Biomass + "/" + habitat.Shrubs.Capacity +
                " · 树木 " + habitat.Trees.Biomass + "/" + habitat.Trees.Capacity +
                "\n种子库 · 草本 " + habitat.Herbaceous.SeedBank + " / 灌木 " + habitat.Shrubs.SeedBank + " / 树木 " + habitat.Trees.SeedBank +
                "\n水 " + habitat.Water + "/" + habitat.WaterCapacity + " · 雪水 " + habitat.SnowWater + " · 雪深 " + habitat.SnowDepth / 100 + "%" +
                "\n遮蔽 " + habitat.Shelter / 100 + "% · 肥力 " + habitat.SoilFertility / 100 + "% · 枯落物 " + habitat.Litter + " · 矿物 " + habitat.MineralRemaining;
        }

        private void RefreshEcology(EcologyEconomySnapshot economy)
        {
            _ecology.Begin();
            foreach (var species in economy.Species)
            {
                var ages = new StringBuilder();
                for (int i = 0; i < species.AgeCounts.Count; i++)
                {
                    if (i > 0) { ages.Append(" · "); }
                    ages.Append(ChineseText.AnimalAge((AnimalLifeStage)i)).Append(' ').Append(species.AgeCounts[i]);
                }
                _ecology.Add(ChineseText.Species(species.Species) + " · 本地 " + species.Count + " · 群体 " + species.GroupCount, ages.ToString(), HudTheme.Positive);
            }
            foreach (var group in economy.Groups)
            {
                if (group.Region != -1) { continue; }
                _ecology.Add(ChineseText.Species(group.Species) + "群 #" + group.Id,
                    "成员 " + group.Members.Count + " · 首领 " + AnimalId(group.LeaderId) + " · 领地 #" + group.TerritoryHabitatId + " · 争夺 " + group.Disputes, HudTheme.Gold);
            }
            _ecology.Add("区域往来 · " + (economy.RegionsEnabled ? "开放" : "隔离"),
                "离境 " + economy.Departures + " · 返回 " + economy.Returns + "\n外部居民不计入本地种群，不显示在地图上", HudTheme.Gold);
            foreach (var region in economy.Regions)
            {
                var counts = new StringBuilder();
                for (int i = 0; i < region.SpeciesCounts.Count; i++)
                {
                    counts.Append(ChineseText.Species((AnimalSpecies)i)).Append(' ').Append(region.SpeciesCounts[i]).Append("  ");
                }
                counts.Append("\n入口 ").Append(region.Exit).Append(" · 栖地 #").Append(region.HabitatId)
                    .Append("\n迁入 ").Append(region.Immigration).Append(" · 迁出 ").Append(region.Emigration)
                    .Append(" · 出生 ").Append(region.Births).Append(" · 死亡 ").Append(region.Deaths);
                foreach (var habitat in economy.Habitats)
                {
                    if (habitat.Id == region.HabitatId) { counts.Append('\n').Append(HabitatDescription(habitat)); break; }
                }
                _ecology.Add(ChineseText.Region(region.Id), counts.ToString(), HudTheme.Muted);
            }
            _ecology.Add("本地栖地资源", "草本 " + economy.LocalHerbaceousBiomass + " · 灌木 " + economy.LocalShrubBiomass + " · 树木 " + economy.LocalTreeBiomass +
                "\n水 " + economy.LocalWater + " · 雪水 " + economy.LocalSnowWater + " · 枯落物 " + economy.LocalLitter + " · 矿物 " + economy.LocalMineral +
                "\n全域生物量 " + economy.Biomass + " · 全域水 " + economy.Water + " · 本地尸体 " + economy.Carcasses.Count, HudTheme.Positive);
            var deaths = new StringBuilder("自然死亡 " + economy.NaturalDeaths + " · 捕食死亡 " + economy.PredationDeaths + " · 猎获 " + economy.HuntKills);
            for (int i = 1; i < economy.DeathCounts.Count; i++)
            {
                deaths.Append('\n').Append(ChineseText.AnimalDeath((AnimalDeathCause)i)).Append(' ').Append(economy.DeathCounts[i]);
            }
            _ecology.Add("死亡原因 · 全域累计", deaths.ToString(), HudTheme.Muted);
            _ecology.End();
        }

        private void RefreshEventLog()
        {
            var model = _session.ReadModel;
            var timeline = model.GetTimeline(_filterTimeline ? _selected : null);
            int count = _filterTimeline && !_selected.HasValue ? 0 : timeline.Count;
            bool showWorldEvents = !_filterTimeline && count == 0;
            if (showWorldEvents) { count = model.RecentEvents.Count; }
            int pages = Math.Max(1, (count + 2) / 3);
            _timelinePage = Math.Min(_timelinePage, pages - 1);
            _timelinePageLabel.text = (_timelinePage + 1) + " / " + pages + " · " + count + " 条";
            _timelineNewer.interactable = _timelinePage > 0;
            _timelineOlder.interactable = _timelinePage < pages - 1;
            HudTheme.Select(_timelineAll, !_filterTimeline);
            HudTheme.Select(_timelineSelected, _filterTimeline);
            _events.Begin();
            if (_filterTimeline && !_selected.HasValue)
            {
                _events.Add("当前村民", "尚未选择村民", HudTheme.Gold, 0f, HudTheme.Muted);
            }
            else if (count == 0)
            {
                _events.Add(_filterTimeline ? NpcName(_selected.Value) : "新的一天",
                    _filterTimeline ? "暂无社交记录" : "暂无世界事件", HudTheme.Gold, 0f, HudTheme.Muted);
            }
            else
            {
                int end = count - _timelinePage * 3;
                for (int i = end - 1; i >= Math.Max(0, end - 3); i--)
                {
                    if (showWorldEvents)
                    {
                        var evt = model.RecentEvents[i];
                        _events.Add(ChineseText.Date(GameCalendar.FromTick(evt.Tick)), ChineseText.Event(evt, NpcName(evt.Source)), EventAccent(evt.Kind));
                    }
                    else
                    {
                        var evt = timeline[i];
                        _events.Add(ChineseText.Date(GameCalendar.FromTick(evt.Tick)) + " · #" + evt.Sequence, SocialDescription(evt),
                            evt.CauseSequence > 0 ? HudTheme.Positive : HudTheme.Gold);
                    }
                }
            }
            _events.End();
            _eventText = (_filterTimeline && _selected.HasValue ? NpcName(_selected.Value) + "的经历" : "全村互动") + " · " + (_timelinePage + 1) + " / " + pages + " 页\n" + _events.Text;
        }

        private static Color EventAccent(SimEventKind kind)
        {
            switch (kind)
            {
                case SimEventKind.NpcDied:
                case SimEventKind.NpcDowned:
                case SimEventKind.HunterInjured:
                case SimEventKind.PlanFailed:
                    return HudTheme.Danger;
                case SimEventKind.NpcRescued:
                case SimEventKind.AnimalHunted:
                case SimEventKind.TradeFilled:
                case SimEventKind.ResourceGathered:
                case SimEventKind.CropHarvested:
                    return HudTheme.Positive;
                case SimEventKind.NpcPathFailed:
                case SimEventKind.CommandRejected:
                    return HudTheme.GoldSoft;
                default:
                    return HudTheme.Gold;
            }
        }

        private void RefreshDebug()
        {
            if (!_debugVisible) { return; }
            var model = _session.ReadModel;
            var economy = model.EcologyEconomy;
            var builder = new StringBuilder();
            builder.Append("Tick ").Append(model.Tick).Append("    Seed ").Append(model.Seed)
                .Append("\nHash ").Append(StateHasher.ToHex(model.StateHash))
                .Append("\nFPS ").Append(Mathf.RoundToInt(_fps)).Append("    Anim ").Append(_runner.AnimationTime.ToString("0.0"))
                .Append("\nNPC views ").Append(_presenter.NpcViewCount).Append("    Animal views ").Append(_presenter.AnimalViewCount)
                .Append("\nPath failures ").Append(_session.PathFailureCount).Append("    Applied commands ").Append(_session.AppliedCommandCount)
                .Append("    Queued ").Append(_session.QueuedCommandCount)
                .Append("\nWorld ").Append(model.Width).Append(" × ").Append(model.Height).Append("    Speed ").Append(_runner.SpeedText)
                .Append("\n守恒误差 · 钱包 ").Append(economy.CurrencyError).Append(" / 物资 ").Append(economy.MaterialError)
                .Append("\n生态误差 · 生物量 ").Append(economy.BiomassError).Append(" / 水 ").Append(economy.WaterError).Append(" / 矿物 ").Append(economy.MineralError)
                .Append("\n转化误差 · 生物量 ").Append(economy.BiomassConversionError).Append(" / 水 ").Append(economy.WaterConversionError).Append(" / 矿物 ").Append(economy.MineralConversionError);
            AtmospherePresenter atmosphere = _presenter.Atmosphere;
            if (atmosphere != null)
            {
                builder.Append("\n雾 · 低 ").Append(atmosphere.LowFogCount).Append(" / 高 ").Append(atmosphere.HighFogCount)
                    .Append("    粒子 ").Append(atmosphere.VisibleParticleCount).Append(" / ").Append(atmosphere.ParticlePoolCount)
                    .Append("    光束 ").Append(atmosphere.ShaftCount)
                    .Append(atmosphere.IsSnowing ? "    雪 " : "    晴 ")
                    .Append(atmosphere.SnowIntensity.ToString("0.00"));
                if (_atmosphereButtons[0] != null)
                {
                    for (int i = 0; i < _atmosphereButtons.Length; i++)
                    {
                        HudTheme.Select(_atmosphereButtons[i], atmosphere.IsLayerEnabled((AtmosphereLayer)i));
                    }
                }
            }
            builder.Append("\n\nLog: ").Append(_session.LogPath).Append("\nReplay: ").Append(_session.ReplayPath);
            if (_selected.HasValue)
            {
                var details = model.GetDetails(_selected.Value);
                if (details != null) { builder.Append("\n\n").Append(details.Decision).Append('\n').Append(details.Plan).Append('\n').Append(details.PlanConditions); }
            }
            _debugBody.text = builder.ToString();
        }
    }
}
