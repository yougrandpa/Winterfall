# 架构设计

## 1. 设计目标

- 世界在玩家不操作时持续运行。
- 玩家是观察者和干预者，而不是某个固定角色。
- 世界逻辑不依赖渲染帧率，结果可重复、可观察、可回放。
- 规则系统是权威；LLM 只提供经过校验的高层意图。
- 先做一个垂直切片，再按需求、经济、社会、AI 的顺序扩展。

## 2. 分层

```text
Input / God UI
     │ WorldCommand
     ▼
GameSession / SimulationRunner       (Application)
     │ fixed tick
     ▼
Simulation Engine                    (纯 C#，权威状态)
├─ Core: tick / ids / rng / events / hash
├─ World: map / calendar / factory / resources / crops
├─ Agents: NPC / needs / personality / action
├─ AI: utility decision / bounded plans / decision trace
├─ Navigation: deterministic A*
└─ Systems: updates / commands / state hash
     │ TickResult + read-model DTO
     ▼
Presentation                         (Unity)
├─ Camera / input / selection
├─ Tilemap / sprites / day-night tint
└─ Runtime God View HUD / debug UI

Future LLM Gateway                    (Application，异步)
     ▲ DecisionRequest
     └─ structured IntentProposal → validation → next tick command queue
```

依赖方向：

```text
Presentation ──► App ──► Sim
Editor       ──► App / Presentation / Sim
Tests        ──► Sim / App / Presentation
Sim          ─X─► UnityEngine
```

实现约束：

- `Assets/Game/Scripts/Sim/MyAiWorld.Sim.asmdef` 使用 `noEngineReferences: true`。
- 模拟状态是普通 C# 对象；Unity 只拥有视图、输入、UI、日志和生命周期。
- 运行中的世界变更只能发生在 `SimulationEngine.Tick` 内；工厂建立初始状态，快照解码建立独立候选世界。
- 玩家操作和调试生成 NPC 都通过 `IWorldCommand`，在 tick 边界应用。
- `ScriptableObject` 只保存配置；运行时数据不放在场景组件里。

## 3. 时间与确定性

- 1 tick = 1 游戏分钟。
- 1× 默认 = 2 tick/秒；一天 1440 tick，约 12 分钟现实时间。
- 支持暂停、1×、2×、4×、8× 和单步。
- 不使用 `Time.timeScale` 驱动世界；`SimulationRunner` 用自己的累加器，并限制单帧最多追赶 8 tick。
- 日历为 4 季 × 28 天；起始为春季第 1 天 06:00。
- 模拟状态尽量整数化：坐标、tick、需求值、随机流状态、移动步数。
- 地图生成、NPC 决策和未来系统使用各自的确定性随机流。
- 实体按稳定 `EntityId` 排序迭代，避免依赖 `Dictionary` 顺序。
- `StateHasher` v5 对 tick、地图、RNG、实体位置/行动/路径/需求/性格、库存、资源再生、作物、预留、完整计划和决策元数据，以及信任、社交冷却、有界记忆/时间线做 FNV-1a 哈希。
- 重复回放依赖“种子 + 命令序列 + 记录下来的 LLM 输出”，而不是重新请求模型。

## 4. 世界数据

核心对象：

```text
WorldState
├─ Config (seed / size / tick rate)
├─ Tick
├─ WorldMap (terrain + movement costs)
├─ EntityRegistry (stable ids, sorted NPC list)
├─ Resources (stockpile / renewable nodes / farm plots / reservations)
├─ Ecology (animals / births / bounded population / hunting reservations)
├─ Market (finite stock / treasury / prices / immutable trade ledger)
├─ Materials (initial / produced / consumed per item)
├─ RuntimeRng
└─ NextEntityId

NpcState
├─ EntityId / Identity (name, role, age)
├─ Personality (diligence, caution, sociability, curiosity)
├─ Needs (hunger, thirst, fatigue, health, safety)
├─ Inventory (food / water / seeds, bounded capacity)
├─ Economy (wallet / tool / hunting skill / production counters)
├─ Plan (steps / progress / failure metadata)
├─ Home / Position
├─ RngState
├─ ActionState (idle/move, target, path, step)
├─ Social (directional trust / bounded memories / cooldown)
└─ LastDecision (goal, reason, scores)
```

地图由种子生成：中央村庄、道路、水塘、农田、房屋占位、森林、岩石和水域边界。`House` 与水域/岩石不可通行；农田运行状态保存在 `WorldState.Resources.FarmPlots`，与可通行地形分离。资源节点生成在可达位置。

`Hunger/Thirst/Fatigue` 历史字段均表示满足度（0..10000，越高越好），提供 Food/Water/Energy 别名。需求逐 tick 衰减；进食、饮水、睡眠恢复，长期匮乏影响健康。NPC 背包容量 12，共享库存容量 4000。萝卜种植消耗种子与浇水资源，生长受水分影响，收获产生食物与种子，肥力随耕作/休耕变化。

## 5. 地图与寻路

- 模拟层使用自己的网格；Unity Tilemap 仅是表现。
- 四方向 A*，移动成本：草地/泥土/道路/农田 = 1，森林 = 2，水/岩石/房屋 = 不可通行。
- 稳定 tie-break：先 F 值，再 cell index。
- 寻路在当前 tick 同步完成；NPC 每个行动只规划一次。规模扩大后再做分帧/路径预算。

## 6. 决策与行动

M2 使用 `RuleBasedNpcPolicy` + `SurvivalPlanner`：

1. 根据需求缺口、性格、库存与可执行工作计算目标效用，优先保障生存。
2. 有限领域规划器寻找可达资源，组合移动、取货/采集、食用、睡眠、存放或农事步骤。步骤包含前置条件与效果说明，不执行通用 GOAP 搜索。
3. 计划发布前预留资源数量与工作格；执行时重新校验数量、容量与位置，按稳定 NPC 顺序处理。
4. 紧急需求可中断工作；资源耗尽、不可达或条件变化会记录失败、释放预留并重规划。满包时先卸货；无可行农事时不反复空规划。
5. `SimulationEngine` 在 tick 内应用效果、推进作物/再生和需求。转移与收获先验证全部约束，再一起应用，防止部分执行复制资源。
6. `DecisionTrace` 与计划状态保留目标、原因、分数、失败步骤与恢复信息，供只读观察。

没有生存或生产任务时，NPC 用自己的确定性随机流在短暂休息与附近散步间选择；仓库两格内优先离开柜台。散步目的地校验可达性、占用与资源工作格，并在到达后短暂休息，避免交货后长期原地叠在仓库。紧急需求仍可按原规则中断散步。

预留约束的是资源与工作格；NPC 移动仍可重叠，尚无完整人群避碰。

### M3：生态与经济切片

`EcologyState` 维护独立随机流、稳定顺序动物与出生周期。兔和野猪游荡、逃跑、成对繁殖、自然死亡；每类最多 24 只，4 只保护数量限制狩猎。`Hunt` 计划预留移动猎物，执行器最多追逐 120 分钟，在相邻格检查工具、体力、生命值、容量与保护数量后攻击；受伤与掉落均由 tick 执行。猎物预留在完成、失败和需求中断时释放。

`MarketState` 是有限库存与国库，位置复用村庄库存。`Buy/Sell` 计划保留独立 `UnitPrice` 报价；`TradeCommand` 可排入 tick 边界。立即成交订单校验所有腿后才变更钱包、国库和物品；拒绝也有原因和不可变账本记录，重复 ID 不再结算。价格按库存每小时更新，界限 1..12。公共库存与采集仍提供生存保障；这是混合经济，不是完整付费生存或挂单撮合市场。

`MaterialBalance` 为再生、农业、狩猎计生产，为进食/饮水/播种/浇水计消耗。每种物品按“初始 + 生产 − 消耗 = 当前节点/公共库存/市场/背包总量”核对；国库与钱包合计核对发行总量。新增 NPC 的初始货币计入发行量。动物活体不计为库存物品。

农夫/采集者/猎人的可选工作分别偏向作物、采集与狩猎；需求紧急程度优先。商人偏好饮水供给与余货出售，建造者暂用通用生存工作。实际产出和交易次数进入 NPC 状态，可从检查器核对策略是否真的执行。

哈希 v3 覆盖生态、预留、钱包/工具/技能、计划报价、市场价格/库存/国库/发行量、生产消耗与职业计数。完整不可变账本在追加时更新滚动摘要，摘要和计数进入世界哈希。完整账本也在 `GameSession` 中逐条写入 JSONL，会话头与回放摘要标明哈希版本；回放摘要格式为 v2。

## 7. 表现层

- 2D 俯视，32×32 基础地块与 32 PPU。地形、地物、角色和逐帧动画都在编辑器像素绘制工具中生成，输出为无损 Point 过滤的 PNG 图集 `Assets/Game/Resources/Pastoral/PastoralAtlas.png`；每张切片保留独立命名、锚点与 `.meta`，配置集中在 `PastoralArt.asset`。
- 图集由 **Tools → My AI World → Art → Generate Pastoral Art** 生成。绘制源是 `Assets/Game/Editor/PastoralArtGenerator.cs` 的整数像素指令，不使用 `UnityEngine.Random` 或模拟随机流；视觉变体使用配置中的独立 `visualSeed`。生成流程只写美术资源，不重建 Bootstrap 场景，也不重置世界配置。
- 地图分为四层：`Ground`（地形变体）、`Edges`（道路/泥土/岸线过渡）、`Landmarks`（树、岩石、房屋）和 `Decorations`（花、草簇、碎石）。地物按脚底锚点与 Y 排序，装饰使用较低的固定排序，避免遮挡角色。农田的湿润/干燥状态和作物四个生长阶段使用独立切片；`CropStage.Empty` 不绘制作物。
- 相机从美术配置读取 PPU，以整数倍（1×–6×）缩放并吸附到 1/PPU 像素网格；内部保留未吸附的位置，避免小幅输入丢失。滚轮以鼠标指向处为缩放锚点，跟随模式保持人物居中；档案提供定位和跟随，F 定位、H／右下按钮回村，手动平移、切换人物和读档退出跟随；默认 1920×1080 下保持升级前的世界可视范围。相机仍支持 WASD、右键/中键拖拽和滚轮缩放；像素美术关闭 MSAA/HDR 以避免图集边缘接缝。
- 新增三种屋顶配色的瓦片／花箱小屋、遮棚物资摊位、岸边芦苇与池中睡莲；水面四帧共用模拟动画时钟，暂停冻结。图集共 755 个切片。地物和装饰归属 `WorldGrid`，读档时随地形统一销毁，避免累积旧树和房屋。
- 村民选中使用脚下光环和头顶指针，保留职业服装原色；重叠人物按实际渲染排序选择，排序相同时按实体 ID 稳定选择。
- 人物和动物共享 `CreatureMotion`：按 tick 快照做位置插值，按脚底 Y 计算排序，使用 `SimulationRunner.AnimationTime` 作为统一动画时钟。暂停时时钟冻结，倍速时按世界节奏加速；出生淡入、受伤闪烁和死亡淡出只修改视图颜色与透明度，不写入模拟状态。
- 五种职业通过服装、帽子和工具区分，具有四方向待机/行走帧，以及进食、饮水、睡眠、采集和工作表现。兔有长耳和跳跃帧，野猪有体型、背鬃和奔跑帧；四方向移动帧共用同一套动画时钟，逃跑使用更高步频。
- 人物左右方向使用侧脸、窄身、前后摆腿与摆臂的独立侧身帧。移动动画按视图的实际插值路段播放，最后一格即使模拟行动已结束也会走完；到达后才切为待机或工作。`WorldPresenter.LateUpdate` 在逻辑更新后采样 tick 与插值进度，每个新 tick 都推进插值端点，包括原地停留的 tick，避免旧路段反复回放。
- 顶部悬浮卡片分组显示日期、四类资源摘要和时间控制；倍速固定按 1×、2×、4×、8× 排列，当前速度及暂停分别高亮。底部导航提供物资、生态与市场、村庄记事、存档四个入口。左侧详情一次展开一个，宽度 400；右侧村民档案宽度 336，顶部保留身份和行动，下方为概览／关系记忆／行动计划分页。切换村民回到概览。左右详情避开顶部信息卡片和底部导航。
- UI 在运行时使用 uGUI 构建，以 1280×720 为参考分辨率，通过 CanvasScaler 适配；主要验收 1920×1080，并验证 2560×1440。主题 `HudTheme` 集中定义暖白纸色、墨绿、铜金、需求色和圆角细边框，`UiFactory` / `HudLayout` 提供按钮、自动换行、滚动区、指标行和独立单元格表格。参考字号为标题 20、正文 14、辅助 12，沿用已打包字体和 Point 像素图标。
- `GodHud` 保留公开查询与控制接口，协调选择、快捷键、导航、存档回调和反馈；`GodHud.Layout` 构建面板，`GodHud.Binding` 从不可变快照绑定内容。文本查询汇总对应面板全部分页内容，与当前可见分页无关。`HudEntries` 复用条目，滚动内容根据首选高度布局，刷新不重建 ScrollRect。长姓名在固定身份区内可独立滚动，长原因和全部 32 条记忆可完整滚动查看。
- 概览的需求名称和百分比位于细进度条外；历史曲线独立成区，图例复用需求颜色。关系按信任降序、ID 稳定次序排列；记忆和村庄记事保留日期、双方、信任变化与回报事件编号。行动计划将目标、原因、步骤状态、条件／效果和失败原因分条呈现。市场显示真实网格表格、种群和最近账本，守恒误差仅在 F1 调试区显示。
- 存档菜单集中五项操作：保存、手动读取、自动档、手动备份、自动备份；不存在的文件禁用读取。成功反馈四秒后收起，失败持续到关闭，完整最近状态保留在菜单滚动区；失败读档保留当前世界，成功读档清理旧选择。F5/F9 保留快捷入口。Esc 只由 HUD 处理，每次依次关闭存档菜单、左侧详情、村民档案，选择控制器不重复处理。
- `PanelTransition` 使用未缩放时间进行约 150 ms 淡入和 6 单位轻微位移，暂停时仍工作；关闭时立即禁用 CanvasGroup 的交互和射线，淡出期间及隐藏后不阻挡地图。操作提示位于底部辅助文字。默认中文文案集中在 `ChineseText`，原始日志、命令、状态哈希与回放格式保持英文／机器可读。
- 随项目打包 Noto Sans SC（SIL Open Font License 1.1，许可文本见 `Assets/Game/Resources/Pastoral/Fonts/OFL.txt`）。F1 面板保留 tick、哈希、日志路径、视图数量与帧率等完整调试信息。
- 昼夜通过全屏色罩表现，不增加逐单位光照。

## 8. 观察、日志与回放

- UI：时间控制、资源总览、可滚动 NPC 检查器（需求、背包、计划条件/效果/失败原因）、最近事件；M3 增加种群/市场面板，展示价格、市场库存、国库、成交/拒绝和最近四笔账本（守恒误差在 F1 调试区），NPC 检查器增加钱包/工具/技能/产出；F1 调试面板默认隐藏。
- App 发布 NPC、资源、作物和事件的不可变快照；表现层不持有可写模拟对象。需求历史每 30 游戏分钟采样，保留最近 24 点并绘制曲线。
- 会话日志：`Application.persistentDataPath/MyAiWorld/sessions/*.jsonl`，记录 session 头、命令、每个领域事件、带双边前后值的结构化交易账本和 session end。
- 回放摘要：`Application.persistentDataPath/MyAiWorld/replays/*.json`，记录种子、世界配置、命令序列、结束 tick 和状态哈希。
- 失败路径：如果日志目录不可写，游戏继续运行并把问题写到 Unity Console，不阻塞模拟。

## 9. LLM 边界（M5 预留）

不会把模型调用写进 tick：

```text
NPC decision window
  → DecisionRequest（只读上下文）
  → LLM 异步调用
  → IntentProposal（严格 JSON schema）
  → 规则校验 + 过期检查
  → 下一 tick 的命令/意图队列
  → 规则系统执行
```

规则：

- 只允许白名单目标与参数，不允许代码、脚本、文件操作或任意命令。
- 规则 AI 永远在线；LLM 超时、限流、非法 JSON、低置信度都回退规则。
- 每个 NPC 有独立人格/记忆/关系；共享一个模型网关，不创建每 NPC 一个模型实例。
- 只在关键决策窗口调用，不是每 tick 调用。
- 记录输入摘要、输出、延迟、Token/成本、回退原因；回放不重新请求模型。
- 默认 `Llm.Enabled = false`，无网络也能完整运行。

## 10. 存档与社会记忆（M4 首版）

- `WorldSnapshot` 位于纯 C# Sim，明确编码权威状态，不依赖反射或 Unity 序列化。快照 v2 支持 v1 无社会数据快照迁移；外壳 v2 支持 v1 校验与迁移。
- App `SaveStore` 保存快照、哈希版本/值、事件序号、统计及结构化待执行命令，以 SHA-256 校验完整负载；使用同目录临时文件、刷盘、原子替换和 `.bak`。手动档和每日午夜自动档分开保存。
- `GameSession.Load` 先建立独立候选世界，校验版本、容量、引用、守恒和状态哈希后发布替换。失败保留旧世界；成功递增 `WorldRevision`，Runner 暂停并重置时钟，表现层重建地图与实体，选择/检查器清理旧状态。
- NPC `Social` 保存单向信任（0–100）、下次互动 tick 和最多 32 条记忆，优先遗忘问候；世界保留最多 256 条社交时间线及计数/序号。社交更新仅在 tick 内执行。
- 规则策略先保证需求；物资充足时帮助缺粮邻居，闲暇时问候。信任影响帮助对象排序；执行时校验双方状态与位置，原子转赠食物，不计为生产或消耗。移动目标跟随有 45 tick 上限，失败释放并冷却。
- 事件包含双方 ID、种类、tick、信任前后值、原因及回报关联的受助事件编号。记忆和时间线发布为不可变只读数据；人物时间线采用该人物保留的记忆。
- 哈希 v4 覆盖完整社会状态。加载 v1 快照时用 v3 哈希检查旧数据，成功迁移后的运行采用 v4。不能直接比较 M3 与 M4 的结束哈希。
- 会话 JSONL 增加结构化 `social` 记录；读档开启新日志段并注明恢复 tick/起点哈希。回放摘要 v3 在恢复会话时附起点快照；完整回放播放器仍未实现。
- 完整交易账本仍随会话增长；文件有明确大小限制。长期历史分段、扩容及完整回放留待后续。需求曲线是 UI 短期缓存，读档后重新采样。

## 11. 测试

- `Sim` 全部可在 EditMode 无场景测试。
- EditMode 覆盖 M1/M2 回归、需求/健康、农业、资源/猎物预留、原子交易及拒绝、账本独立重建、哈希敏感性和非空命令确定性；四个种子各运行 20160 tick，逐 tick 核对物资/货币守恒与生存，每日检查成交，结束验证实际职业差异。
- PlayMode 覆盖启动/场景、检查器与曲线、按钮射线命中及指针事件；M3 覆盖动物视图、生态经济快照隔离、观察面板与结构化交易日志。原生鼠标人工验收单独跟踪。
- 编辑器 Verification 菜单可在原工程运行两套测试，结果写入 `Logs/TestResults/*-CurrentProject.json`。
- M4 新增四种子多时刻快照续跑、v1→v2 迁移、待执行命令恢复、原子写入/备份/损坏拒绝/错误提示、真实互助因果链、信任与记忆哈希、容量与需求中断，以及存读档视图和人物时间线集成检查。
- 后续每个系统都要增加不变量测试和固定种子回放测试。


## 12. 自主生存扩展（当前规则，取代上文 M2–M4 的对应历史规则）

- `SurvivalWorldState` 持有设施、工地库存、地面物资、遗物与补水状态；`SurvivalNpcState` 持有工具耐久、疾病、生命状态、救援认领与产出计数。物品目录由 `ItemKind.Count` 定义；市场明确仅支持前三类食物、饮用水和萝卜种子，其余物资不可交易。
- `ProductionSystem` 负责确定性选址、原子加工/建设/维护和工具磨损；`WaterSystem` 负责饮水储备、取水批量及蓄水灌溉；`AgricultureSystem` 负责轮作、季节、水分与损伤；`HealthSystem` 负责倒地、救援、转运、照护和死亡；`HuntingSystem` 负责武器射程和视线。执行器只协调这些系统与通用计划步骤。
- 设施只有一个占地格与相邻交互格。选址暂时封闭候选格，验证全部住所、资源和农田仍与仓库连通后发布工地；不使用道路、农田、NPC 当前格或住所邻格。工地与设施库存计入守恒，材料送达后才可施工或加工。工作格与设施认领共同释放；施工进度支持中断续建。
- 默认村庄自主建立净水炉、工作台、蓄水池、水井、灌溉设施各一座。材料来自可再生木材、石料、纤维和草药节点。净水炉每批消耗 6 生水与 1 木材，产出 6 饮用水；井的初始库存为零，每 20 分钟恢复一份，停用设施不可取水。蓄水池不生产水，灌溉每 30 分钟最多消耗一份生水照顾七格内最缺水的未预留作物。
- 饮用水库存低于目标的一半时补充，达到每位存活 NPC 五份时停止；最低目标 20。取水批量根据需求缺口、可达水源路径成本、背包及水桶计算。饮用生水有 35% 的确定性随机患病概率，疾病持续最多 720 分钟，药品可以治疗。
- 萝卜、小麦、豆类生长分别需 720、1440、1000 个有效生长分钟，基础产出分别为 4、8、5 食物和两份对应种子。豆类改善肥力；农夫最多预留并处理四块邻田，优先缺水作物，再收获、播种。夏季耗水翻倍，冬季禁止播种、生长降至四分之一并累积冻害；持续缺水同样累积损伤，达到 1800 枯死。成熟作物停止累积伤害，休耕地恢复肥力。冬季野外食物再生也降至四分之一。
- 猎物包含兔、野猪与鹿，每种上限 24、狩猎保护数 4；自然死亡仍可使种群低于保护数。猎人按路径成本、风险、技能、谨慎程度和产出排序。弓射程四格且校验视线，矛射程一格。攻击与追逐消耗体力，工具耐久归零即计消耗。击杀先生成唯一地面物资，再走到掉落处回收；背包不足或追逐中断保留可回收猎获。
- 健康归零时先进入倒地，绝不先被舒适需求的回血抬回正常状态。180 分钟内可由唯一救援者用一份药品稳定至 1500 健康并转运回家；转运中断后其他人能接手。恢复期卧床，消耗真实食水，其他 NPC 可运送补给；满包时允许直接喂食。健康达到 4000 后恢复工作。超时死亡的背包、工具耐久和钱包进入遗物，死者只保留历史身份与社会记录。
- 新存档外壳/快照为 v3、哈希 v5、回放摘要 v4。新增状态、在途工作、认领和计数进入快照与哈希。旧外壳/快照明确拒绝，新文件使用 `MyAiWorld/saves-survival-v3`，不覆盖旧目录，不迁移旧世界。
- App 发布不可变 `SurvivalReadModel`、设施/地面物资与人物生存状态快照。地图空白选择支持农田、动物、资源、设施与遗物观察；设施状态和生产库存位于物资面板，人物概览显示耐久、疾病和救援窗口。新像素切片追加至原图集，不移动已有切片槽位，不重建 Bootstrap 场景。
