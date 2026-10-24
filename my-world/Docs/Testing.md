# 测试运行与分层

更新日期：2026-10-24。本说明取代此前“每次连接编辑器并运行两个完整程序集”的日常流程。

## 日常入口

在仓库根目录运行：

```bash
python3 tools/test.py
python3 tools/test.py --filter HuntingGameplayTests
python3 tools/test.py --filter CombatReadModelTests --filter EcologyReadModelTests
```

默认 `fast` 直接运行生产 Sim、App 源码和现有 NUnit 测试，不启动或连接 Tuanjie，不加载场景，也不等待界面帧。C# 首次仍需编译；后续按源码、依赖和输出内容哈希复用缓存，仅重新编译受影响的程序集。需要明确禁止编译时使用：

```bash
python3 tools/test.py --no-build
```

缓存缺失、源码变化、源文件增删、依赖变化或输出损坏时，`--no-build` 返回非零退出码，不能用旧程序集冒充新源码的通过结果。

## 测试分层

| 层级 | 当前用例数 | 入口 | 使用时机 |
| --- | ---: | --- | --- |
| 快速代码测试 | 392 | `python3 tools/test.py` | 日常修改，或用 `--filter` 选择相关测试 |
| 长周期代码回归 | 13 | `python3 tools/test.py slow` | 修改长期生产、生态、确定性或存读档规则时 |
| 全部可移植代码测试 | 405 | `python3 tools/test.py full` | 需要更广的模拟与 App 回归时；包含前两层 |
| 编辑器配置集成 | 1 | 编辑器完整 EditMode 或按测试名选择 | 修改 ScriptableObject 配置适配时 |
| 引擎与界面集成 | 64 | 按测试名运行 PlayMode；完整入口仍保留 | 修改渲染、输入、UI、场景或资源时 |
| 引擎冒烟检查 | 4（上述 64 项的子集） | Verification → Run PlayMode Smoke Tests | 需要检查启动、动物选择、狩猎按钮和读档视图接线时 |
| 启动器缓存单测 | 6 | `python3 -m unittest discover -s tools/tests -v` | 修改启动器、缓存或编译调用时，无需 C# 编译器 |

默认快速集明确排除标记为 `LongRunning` 的 13 项，不把它们记为“跳过”或“通过”。报告保留发现、选中和未选中的数量；实际选中的测试出现失败、跳过或不确定结果，命令均失败。未匹配任何测试返回退出码 2。

`--filter` 按完整测试名称中的子串匹配，重复参数表示“或”，同时受所选层级约束。查看范围而不执行测试：

```bash
python3 tools/test.py --list --no-build
python3 tools/test.py slow --list --no-build
python3 tools/test.py full --filter Snapshot --list --no-build
```

`--list` 可使用已有缓存；没有缓存时首次仍需构建。它不覆盖上一次执行报告。

## 本轮代码调整

- 将战斗和生态的 6 个纯数据测试方法（共 7 项用例）从 PlayMode 移到 EditMode，保留方法体与全部断言。PlayMode 从 71 项降至 64 项，EditMode 从 399 项变为 406 项；总覆盖仍为 470 项，没有删除测试。
- 将唯一使用 `WorldConfigAsset` 的测试移入 `EditorConfigurationTests.cs`，其余 405 项可独立运行。独立入口复用原 NUnit 框架的测试发现、执行、参数化用例和断言，不另写模拟规则或伪造 Unity API。
- App 的数据目录与警告输出集中到 `RuntimeHost`。引擎内仍使用 `Application.persistentDataPath` 和 `Debug.LogWarning`；独立运行使用隔离目录和标准错误。测试继续使用真实文件、保存、备份与损坏拒绝，不接触玩家存档。
- 保留十四日完整行为／守恒／固定哈希、七日生存、四种子中途恢复及逐 tick 比较等 13 项较长回归，统一标记 `LongRunning`。快速集仍包含短程确定性、损坏拒绝、命令排队、狩猎任务、冲锋和恢复专项。
- 保留引擎专属断言和实际按钮流程，仅在相关变更时运行。两项狩猎截图用例标记 `VisualReview`，不进入四项 `EngineSmoke`。

本轮不修改模拟规则、固定哈希、Bootstrap、WorldConfig 或图集。之前已删减的多年生态／全年生产长跑没有恢复，不能据此声称覆盖连续多年演化；既有范围边界见历史进度记录。

## 依赖、缓存与报告

需要 Python 3.9+、项目固定版本 Tuanjie 2022.3.62t4 的 Mono，以及工程已恢复的 `com.unity.ext.nunit`、`com.unity.nuget.newtonsoft-json` 包。不需要启动编辑器、测试桥、网络请求或安装新测试框架。默认从 macOS 标准安装路径发现运行时；自定义位置可指定：

```bash
python3 tools/test.py --editor /path/to/Tuanjie.app
```

也可设置 `TUANJIE_EDITOR`。此参数只用于读取已安装的 Mono 与 C# 编译器文件，不会启动编辑器进程。尚未导入依赖包的新检出需要先恢复工程依赖；启动器缺依赖时明确失败，不会自动下载替代版本。

缓存位于忽略目录 `my-world/Library/CodeTests/`，不依赖编辑器生成的 `.csproj` 或 `ScriptAssemblies`。结果位于 `my-world/Logs/TestResults/Code-fast.json`（或 `Code-full.json`、`Code-slow.json`）及同名 NUnit XML。JSON 记录构建缓存状态、构建耗时、测试主体耗时、命令总耗时及每项结果。失败构建或空筛选会清除该执行路径的旧成功报告；不同层级及编辑器历史报告互不覆盖。

退出码：0 表示所选用例全部通过；1 表示测试失败、意外跳过或不确定；2 表示构建、依赖、参数或测试选择错误。

## 本轮验证记录

| 执行情况 | 通过 / 失败 / 跳过 | 构建或缓存检查 | 测试主体 | 命令总耗时 |
| --- | --- | --- | --- | --- |
| 首次独立运行，编译全部三个程序集 | 392 / 0 / 0 | 1.705 秒 | 1.593 秒 | 3.424 秒 |
| 调整测试源码，仅重编测试程序集 | 392 / 0 / 0 | 0.589 秒 | 1.580 秒 | 2.280 秒 |
| `--no-build`，三个程序集均复用缓存 | 392 / 0 / 0 | 0.018 秒 | 1.635 秒 | **1.794 秒** |

实际确认源码变化时 `--no-build` 退出码为 2；`slow` 和 `full` 的只发现操作分别选出 13、405 项；空名称筛选退出码为 2，且只发现操作保留了最近一次执行报告。耗时来自当前机器的本次运行，其他负载或机器会有所不同。

启动器缓存的 6 项 Python 单测全部通过（0.021 秒），覆盖缓存命中、源码／依赖变化、源文件增删、输出损坏与编译失败保留原缓存。迁移前后静态比较确认 6 个方法的正文逐字一致。长周期代码回归和编辑器／PlayMode 本轮未执行；不把分层后未运行的测试称为通过。

历史参考：上轮完整 EditMode 399 项耗时 35.286 秒，PlayMode 71 项耗时 58.103 秒，另有连接、编译和模式切换开销。快速集的覆盖范围不同，不能把两个耗时直接当成等覆盖的加速比。
