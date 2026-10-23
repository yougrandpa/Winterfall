# Milestone 01 进度记录

日期：2026-10-20  
状态：**代码与自动验收完成；Game View 中的人工视觉验收待做**

## 已完成

### 工程与架构

- 建立 `Sim / App / Presentation / Editor / Tests` 程序集。
- `MyAiWorld.Sim` 设置 `noEngineReferences: true`，模拟核心不引用 Unity。
- 加入 2D Pixel Perfect 5.1.0、Newtonsoft.Json 3.2.1 依赖；自写相机像素吸附以支持连续缩放。
- 新增 `WorldConfig.asset` 和 `Bootstrap.unity`，并把场景设为构建场景。
- 默认产品名/公司名改为 `My AI World`，编辑器默认行为切到 2D。

### 模拟

- 1 tick = 1 游戏分钟，2 tick/秒，4 季 × 28 天，初始春季第 1 天 06:00。
- 确定性 RNG、稳定实体 ID、稳定迭代顺序、状态哈希。
- 64×64 种子地图：村庄、道路、水塘、房屋、农田、森林、岩石、水域边界。
- 四方向整数 A*。
- 20 个 NPC：身份、性格、需求占位、行动状态、决策记录。
- Rest/Wander 规则策略；路径失败和动作完成事件。
- 玩家 `SpawnNpcCommand` 进入 tick 边界命令队列，不直接改世界。
- `SimulationEngine`、`WorldReadModel`、日志与回放摘要。

### 表现与观察

- 正交自由相机、WASD/拖拽平移、滚轮缩放、边界限制、像素吸附。
- 运行时生成 16×16 占位像素图；Tilemap 地面、SpriteRenderer NPC、Y 排序。
- 昼夜全屏色罩。
- 点选 NPC、检查器、时间控制、单步、速度档、事件日志、F1 调试面板、`+ NPC`。
- 暂停/继续/1×/2×/4×/8×。
- 会话 JSONL 日志与回放 JSON 摘要。

## 验证证据

### 自动测试

| 平台 | 结果 | 用例数 |
|---|---|---|
| EditMode | Passed | 9 / 9 |
| PlayMode | Passed | 2 / 2 |

覆盖内容：

- 日历跨小时/天/季/年。
- RNG 同种子同序列。
- A* 绕障与不可达。
- 同种子地图、NPC 生成与连通性。
- 相同种子 400 tick 的状态哈希完全一致。
- 命令只在 tick 边界生效；非法生成被拒绝。
- 64×64、20 NPC 连续 3000 tick 仍在地图可行走格上。
- 启动组装、摄像机、HUD、字体、NPC 视图数量。
- `Bootstrap.unity` 实际加载并带有配置和 20 个视图。
- 暂停时不推进；单步 +1 tick；8× 继续推进。

证据文件：

- `Logs/TestResults/EditMode.xml`（Passed，9/9）
- `Logs/TestResults/PlayMode.xml`（Passed，2/2）
- `Logs/Bootstrap.log`（成功生成场景的日志）
- `Docs/Preview/WorldPreview.png`（摄像机渲染预览，不含屏幕叠加 UI）

### 测试命令

```bash
TUANJIE="/Applications/Tuanjie/Hub/Editor/2022.3.62t4/Tuanjie.app/Contents/MacOS/Tuanjie"
PROJECT="/Users/slg/workspace/my-ai-game/my-world"

"$TUANJIE" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testResults "$PROJECT/Logs/TestResults/EditMode.xml" \
  -logFile "$PROJECT/Logs/TestResults/EditMode.log"

"$TUANJIE" -batchmode -projectPath "$PROJECT" \
  -runTests -testPlatform PlayMode \
  -testResults "$PROJECT/Logs/TestResults/PlayMode.xml" \
  -logFile "$PROJECT/Logs/TestResults/PlayMode.log"
```

注意：**不要给 `-runTests` 加 `-quit`**，否则编辑器可能直接退出而不执行测试。本次第一次尝试就出现了这个问题，已确认并修正。

> 测试执行说明：当时原工程被一个正在运行的 Tuanjie 编辑器实例占用，CLI 无法再次打开同一路径。因此自动测试在隔离副本 `/tmp/myaiworld-m1-test.WcmH3v/my-world` 中执行；该副本使用相同 `Assets`、相同地图/场景脚本，移除了仅用于编辑器集成的 Codely 包。场景、配置、源码和 `.meta` 已回写到原工程；测试结果与日志也已回写。
>
> 另外，从最终工程再次全新复制并导入了一份副本（不包含原 Library），PlayMode **2/2 通过**，证明回写的场景/配置/`.meta` 可在新的工程实例中正确解析。结果见 `Logs/TestResults/PlayMode-FreshCopy.xml`。
>
> 原工程下一次启动/刷新后仍建议在原路径再跑一次确认。

## 当前问题

1. **原工程需要刷新/重启编辑器**  
   修改了 `Packages/manifest.json` 并加入了新场景/脚本。如果编辑器仍停留在旧导入状态，可能出现包缓存或旧程序集错误。建议关闭后重新打开工程，或至少让编辑器完成 Asset Refresh。若场景或 Build Settings 丢失，运行菜单 **Tools → My AI World → Build Bootstrap Scene**。

2. **HUD 还没有经过人工视觉检查**  
   自动测试验证了 Canvas、字体、至少 8 个 Text 元素、场景加载和视图创建，但布局是否遮挡、按钮是否易读，需要在 Game View 中确认。当前预览图只包含摄像机世界，不包含 HUD。

3. **像素完美采用自定义方案**  
   相机按 2×–6× 整数美术像素缩放，并把位置吸附到 1/16 单位；没有挂 `PixelPerfectCamera`。包已经安装，后续若采用固定缩放/低分辨率 RT，可再接入正式组件。

4. **M1 的视觉资源是运行时占位图**  
   地图元素和 NPC 都由代码生成，便于验证系统而不是验证美术。正式像素资产、Tile 资产、Prefab、Aseprite 导入流程留给后续。

5. **已知的 M1 边界**  
   - NPC 可以互相穿过；尚未做占位和局部避让。
   - 需求值是静态占位，不衰减。
   - 没有种植/狩猎/交易/关系/记忆/存档加载。
   - 局部日志路径可能因产品名包含空格而不好阅读，但可正常写入。
   - 当前安装的构建后端只有 WebGL / 微信小游戏，没有 macOS Standalone；若需要桌面包要先安装 Mac Build Support。

6. **测试验证不是原工程路径**  
   原因见上；这是环境中的编辑器占用问题，不是代码问题。原工程重新打开后应重新运行两条测试命令。

## 下一步

1. 重新打开原工程，让包与新资源完成导入。
2. 打开 `Assets/Game/Scenes/Bootstrap.unity`，Play，检查：
   - HUD 布局、字体、按钮；
   - 相机拖拽/缩放；
   - 点击 NPC 的检查器；
   - 时间暂停/单步/加速；
   - `+ NPC` 与事件日志。
3. 在原工程路径重跑 EditMode/PlayMode 测试并记录。
4. 进入 M2：需求衰减、背包/库存、采集、农田、作物生长、效用 + 多步计划。
