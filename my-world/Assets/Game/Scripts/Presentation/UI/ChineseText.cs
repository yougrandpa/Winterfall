using System.Collections.Generic;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Presentation.UI
{
    public static class ChineseText
    {
        private static readonly string[] Roles = { "农夫", "采集者", "猎人", "商人", "建造者" };
        private static readonly string[] Actions = { "休息", "行走", "进食", "饮水", "睡眠", "采集", "工作" };
        private static readonly string[] Steps = { "前往", "领取库存", "进食", "饮水", "回家睡眠", "采集", "存放", "播种", "浇水", "收获", "等待", "狩猎", "购买", "出售", "问候", "赠送食物", "运输材料", "领取产物", "建造", "加工", "维护", "装备工具", "回收物资", "救援", "饮用生水", "服药", "回家取暖", "补充燃料", "融雪取水" };
        private static readonly string[] Goals = { "休息", "漫步", "补充食物", "补充饮水", "恢复精力", "采集食物", "采集饮水", "照料农田", "村庄生产", "救援村民", "治疗疾病", "狩猎", "交易", "问候邻居", "帮助邻居", "回家取暖" };
        private static readonly string[] Events = { "重伤倒地", "救援完成", "村民逝世", "世界已建立", "动物出生", "动物自然死亡", "猎获动物", "猎人受伤", "交易成交", "交易未成交", "居民抵达", "选择目标", "开始行动", "完成行动", "道路无法到达", "指令被拒绝", "新的一天", "季节更替", "开始计划", "计划失败", "计划中断", "采集资源", "播种作物", "农田浇水", "收获作物", "需求告急", "友好问候", "赠送食物", "寒冬挑战结束" };
        private static readonly Dictionary<string, string> Reasons = new Dictionary<string, string>
        {
            { "seek_warmth", "体温下降或衣物湿冷，回到住所取暖" },
            { "return_before_cold_night", "寒冷夜晚将至，提前返家避寒" },
            { "critical_cold_interrupt", "失温风险优先，中断工作并返家取暖" },
            { "shelter_unavailable", "住所、入口或居住容量不可用" },
            { "melt_snow", "水源结冰，利用住所余热融雪取得生水，仍需净化" },
            { "deliver_fuel", "住所燃料不足，运送木材以持续供暖" },
            { "deadline_reached", "完成全部挑战时间" },
            { "all_initial_npcs_dead", "原始名单已全部逝世" },
            { "hunt_retrieval_changed", "猎获物资状态发生变化" },
            { "hunt_retrieval_unreachable", "无法到达猎获掉落处" },
            { "hunt_retrieval_bag_full", "背包已满，猎获保留在地面" },
            { "village_production_shortage", "补充村庄物资、补燃料、融雪、建造或维护设施" },
            { "treat_waterborne_illness", "领取药品治疗水源性疾病" },
            { "urgent_rescue", "优先救助重伤村民" },
            { "recovery_needs", "恢复期补充食水并休养" },
            { "production_precondition_changed", "加工物资、容量或设施状态发生变化" },
            { "friendly_greeting", "闲暇时问候邻居，建立信任" },
            { "help_hungry_neighbor", "自己物资充足，帮助饥饿的邻居" },
            { "reciprocate_help", "记得对方曾赠送食物，优先回报帮助" },
            { "social_target_or_needs_changed", "对方状态变化或自己需要先照顾生存" },
            { "social_target_unreachable", "暂时无法接近社交对象" },
            { "none", "无" }, { "initial", "初到村庄" }, { "need_satisfaction_deficit", "优先满足当前需求" },
            { "profession_supply_strategy", "按职业补充物资" }, { "curiosity_and_diligence", "探索与勤劳倾向" },
            { "rest_bias", "倾向休息" }, { "no_valid_wander_target", "暂无可探索位置" },
            { "no_available_farm_work_or_supplies", "暂无农事或缺少物资" }, { "no_reachable_unreserved_resource", "暂无可达的空闲资源" },
            { "critical_need_interrupt", "紧急需求优先" }, { "deposit_items_missing_or_stock_full", "背包无物品或库存已满" },
            { "effect_transfer_failed", "资源转移失败" }, { "food_missing", "缺少食物" }, { "water_missing", "缺少饮水" },
            { "harvest_requires_reserved_mature_crop_and_space", "需要成熟作物预留与背包空间" }, { "home_unavailable", "住所不可用" },
            { "hunt_prey_quota_tool_energy_health_or_capacity", "猎物状态、武器、弹药、体力、健康或空间条件不足" },
            { "hunt_prey_unreachable", "无法接近猎物" }, { "hunt_pursuit_timeout", "追逐超时" }, { "hunt_strike_rejected", "攻击条件不足" },
            { "not_at_market", "尚未到达市场" }, { "not_at_work_location", "尚未到达工作位置" },
            { "path_blocked", "道路被阻挡" }, { "path_unreachable", "目标无法到达" }, { "target_unwalkable", "目标无法通行" },
            { "plant_requires_reserved_empty_fertile_plot_and_seed", "需要预留空田、肥沃土壤和种子" },
            { "preferred_work_unavailable_supply_fallback", "职业工作暂不可用，改为补充物资" },
            { "reserved_node_unavailable_or_bag_full", "预留资源不足或背包已满" }, { "reserved_stock_unavailable_or_bag_full", "库存不足或背包已满" },
            { "water_requires_reserved_crop_and_water", "需要预留作物和饮水" }, { "filled", "成交" },
            { "bag_full", "背包已满" }, { "currency_overflow", "货币数值超出上限" }, { "insufficient_coins", "货币不足" },
            { "invalid_order", "无效订单" }, { "market_full", "市场库存已满" }, { "market_stock_short", "市场库存不足" },
            { "npc_missing", "村民已不存在" }, { "price_changed", "报价已变化" }, { "seller_stock_short", "卖家库存不足" },
            { "treasury_short", "国库不足" }, { "complete", "已完成" }
        };
        public static string Role(string role) => Roles[Rendering.NpcView.RoleIndex(role)];
        public static string Action(NpcActionType action) => Actions[(int)action];
        public static string Step(PlanStepType step) => Steps[(int)step];
        public static string Goal(NpcGoalType goal) => Goals[(int)goal];
        public static string Item(ItemKind item) => new[] { "食物", "饮用水", "萝卜种子", "生水", "木材", "石料", "纤维", "草药", "药品", "小麦种子", "豆种", "弓", "矛", "锄头", "水桶", "箭矢", "生肉", "兽皮", "冬衣" }[(int)item];
        public static string Crop(CropType crop) => new[] { "休耕", "萝卜", "小麦", "豆类" }[(int)crop];
        public static string Facility(FacilityKind kind) => new[] { "工作台", "净水炉", "蓄水池", "水井", "灌溉设施" }[(int)kind];
        public static string Life(LifeState life) => new[] { "健康活动", "倒地待援", "卧床恢复", "已逝世" }[(int)life];
        public static string Weather(WeatherKind weather) => new[] { "晴", "降雪", "暴风雪", "寒潮" }[(int)weather];
        public static string Supply(SupplyStatus status) => new[] { "", "耗尽", "紧张", "充足" }[(int)status];
        public static string Species(AnimalSpecies species) => new[] { "野兔", "野猪", "鹿", "田鼠", "狐狸", "狼", "渡鸦" }[(int)species];
        public static string AnimalAge(AnimalLifeStage stage) => new[] { "哺育期", "幼年", "成年", "老年" }[(int)stage];
        public static string AnimalActivity(AnimalBehavior behavior) => new[] { "休息", "觅食", "饮水", "躲避风寒", "逃跑", "捕猎", "保卫领地", "食腐", "哺育", "迁徙", "死亡" }[(int)behavior];
        public static string AnimalDeath(AnimalDeathCause cause) => new[] { "未记录", "衰老", "饥饿", "脱水", "失温", "被捕食", "被狩猎", "受伤" }[(int)cause];
        public static string AnimalSexName(AnimalSex sex) => sex == AnimalSex.Female ? "雌性" : "雄性";
        public static string Region(int region) => region < 0 ? "本地" : "外部区域 #" + region;
        public static string Activity(string label)
        {
            if (label == null) { return ""; }
            if (label.StartsWith("Hunt: pursue")) { return "接近猎物"; }
            if (label == "Hunt: gather") { return "集结与准备装备"; }
            if (label == "Hunt: flank") { return "侧翼截击"; }
            if (label == "Hunt: dodge") { return "侧移躲避冲锋"; }
            if (label == "Hunt: wait for lane") { return "等待射路与换位"; }
            if (label == "Hunt: cover") { return "掩护队友"; }
            if (label == "Hunt: return") { return "运送猎获回村"; }
            if (label == "Hunt: retreat") { return "撤离狩猎区域"; }
            if (label == "Hunt: complete") { return "狩猎结束"; }
            if (label == "Hunt: shelter") { return "寻找安全退路"; }
            if (label == "Hunt: track") { return "追踪猎物"; }
            if (label == "Hunt: attack") { return "瞄准与攻击"; }
            if (label == "Hunt: retrieve") { return "取回猎获"; }
            if (label == "Rescue") { return "救治与转运"; }
            if (label == "Recovering") { return "卧床恢复"; }
            if (label == "Awaiting transport") { return "等待转运"; }
            if (label == "WarmUp") { return "住所取暖"; }
            if (label == "DeliverFuel") { return "补充住所木材"; }
            if (label == "MeltSnow") { return "利用热量融雪"; }
            return "";
        }
        public static string Reason(string code)
        {
            string result;
            return code != null && Reasons.TryGetValue(code, out result) ? result : "条件发生变化：" + (code ?? "未记录原因");
        }
        public static string Date(GameDateTime date) => "第 " + date.Year + " 年 · " + new[] { "春", "夏", "秋", "冬" }[(int)date.Season] + " " + date.DayOfSeason + " 日    " + date.ClockText;
        public static string Event(EventSnapshot evt, string name) => (string.IsNullOrEmpty(name) ? "" : name + " · ") + Events[(int)evt.Kind];
        public static string Conditions(PlanStepType type)
        {
            switch (type)
            {
                case PlanStepType.WarmUp: return "返家且住所可用 → 逐分钟取暖；湿冷、缺柴仍有失温风险";
                case PlanStepType.DeliverFuel: return "到达住所入口且携带木材 → 补充真实燃料库存";
                case PlanStepType.MeltSnow: return "积雪、住所燃烧余热与空间可用 → 消耗热量融雪为生水，仍需净化";
                case PlanStepType.Deliver: return "携带材料且设施有空间 → 运入设施库存";
                case PlanStepType.TakeFacility: return "设施已产出且背包有空间 → 领取产物";
                case PlanStepType.Build: return "材料齐备且工地已预留 → 建成设施";
                case PlanStepType.Craft: return "设施可用且原料齐备 → 消耗原料并产出";
                case PlanStepType.Maintain: return "携带维护材料 → 恢复设施状态";
                case PlanStepType.Equip: return "携带对应工具 → 装备并记录耐久";
                case PlanStepType.Pickup: return "物资已预留 → 回收背包可容纳的物资";
                case PlanStepType.Rescue: return "伤员待援、药品和道路可用 → 救治并转运回家";
                case PlanStepType.Treat: return "携带药品 → 治疗水源性疾病";
                case PlanStepType.DrinkRaw: return "携带生水 → 恢复水分，有患病风险";
                case PlanStepType.Greet: return "接近邻居且需求充足 → 问候并增加彼此信任";
                case PlanStepType.GiveFood: return "接近缺粮邻居且自己至少有 2 食物 → 转赠 1 食物并增加对方信任";
                case PlanStepType.Move: return "道路可达 → 到达目标";
                case PlanStepType.Eat: return "携带食物 → 消耗 1 食物，饱食 +45%";
                case PlanStepType.Drink: return "携带饮水 → 消耗 1 饮水，水分 +55%";
                case PlanStepType.Sleep: return "到达住所 → 逐分钟恢复精力";
                case PlanStepType.Gather: return "预留资源且背包有空间 → 采集入包";
                case PlanStepType.Plant: return "空田、种子与肥力 → 播种萝卜";
                case PlanStepType.Water: return "作物与饮水 → 补充土壤水分";
                case PlanStepType.Harvest: return "成熟作物且背包有空间 → 获得食物与种子";
                case PlanStepType.Hunt: return "武器、弹药、体力与猎物预留 → 追逐攻击；命中并非保证，猎获需回收和加工";
                case PlanStepType.Buy: return "报价、货币与市场库存 → 物品入包";
                case PlanStepType.Sell: return "报价、背包物品与国库 → 获得货币";
                case PlanStepType.Deposit: return "背包有物品且库存有空间 → 存入公共库存";
                case PlanStepType.TakeFromStock: return "预留库存且背包有空间 → 物品入包";
                default: return "等待结束 → 再次规划";
            }
        }
    }
}
