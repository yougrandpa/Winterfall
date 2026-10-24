# 冬日十四天 · UI 重设计

## 实现

- 四角轻量仪表，默认常驻布局约占参考画布 8.0%；炭灰、霜白、炉火橙，十四道刻痕呈现挑战进度。
- 常驻储备统一使用全局食物、饮水、木材快照；紧张与耗尽同时用文字、颜色和刻度表达。估算依据默认折叠。
- 点击村民先显示状态卡，主动打开档案；右侧详情互斥，保留选择并在关闭后恢复状态卡。
- 存档、帮助、档案与全局详情使用 336 宽边缘面板；保留快捷键、全部观察数据和存读档行为。
- 结算突出成绩与存活人数，详细记录主动展开；成绩锁定与继续观察确认不变。
- 只调整表现层、相关测试与验证辅助代码，未调整模拟规则、相机视口或重建 Bootstrap。

## 验证

使用运行中的 Tuanjie 1.8.2 / 2022.3.62t4，通过项目 `ProjectVerification` 执行，未另开编辑器或重新生成场景。

| 检查 | 结果 |
| --- | --- |
| EditMode 全量 | **336/336 通过**，576.7 秒 |
| PlayMode 最终全量 | **58/58 通过**，39.4 秒 |
| 常驻布局 | 三种分辨率均小于 10%，参考布局约 8.0% |
| 中央及仪表间空白区域 | GraphicRaycaster 无命中，允许地图操作 |
| 详情互斥、状态卡恢复、Esc | 通过，隐藏面板立即释放射线 |
| 储备、进度、人物异常 | 充足／紧张／耗尽／无需供给；相对挑战起点的天数与刻痕；倒地状态通过 |
| 长文本、滚动、存读档、结算 | 通过，继续观察确认与成绩冻结不变 |
| 观察只读性 | 交互前后世界 tick / hash 验证通过 |
| 源码差异检查 | `git diff --check` 通过 |

报告位于 `Logs/TestResults/EditMode-CurrentProject.json`（2026-10-24T05:05:24Z）与 `PlayMode-CurrentProject.json`（2026-10-24T05:12:59Z）。

已逐图检查实际 Camera/Canvas 渲染的三种分辨率、冬日昼夜、状态卡、完整档案、物资和结算。修正了挑战按钮字体裁切、旧头像切片键；截图辅助方法在临时转换 ScreenSpaceCamera 时保留 Overlay 相对于世界雾效的顺序，并在完成后恢复画布状态。

按钮通过实际 GraphicRaycaster 命中与 EventSystem 指针事件执行验证；未进行物理鼠标设备的人工操作验收。

## 截图

- 默认：[1280×720](WinterHud/winter-day-1280.png)、[1920×1080](WinterHud/winter-day-1920.png)、[2560×1440](WinterHud/winter-day-2560.png)。
- 人物：[轻量状态卡](WinterHud/winter-summary-1920.png)、[完整档案与头像](WinterHud/winter-inspector-1280.png)。
- [物资详情与折叠依据](WinterHud/winter-resources-2560.png)、[冬日夜景](WinterHud/winter-night-2560.png)。
- [锁定结算](WinterHud/winter-settlement-1920.png)：受控五人灭绝测试场景，不代表默认游戏运行结果。

截图按仓库现有 `.gitignore` 规则保存在本地，不纳入 Git。其余自动捕获图保留在 `Logs/TestResults/WinterHudVisual/`。
