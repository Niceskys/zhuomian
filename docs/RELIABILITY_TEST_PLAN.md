# Zhuomian 测试与可靠性计划

## 1. Phase 0 Minimum CI

从首个解决方案出现开始，每个 PR 的持续门禁必须至少执行：

- 文档结构与本地链接检查；
- 性能证据契约校验器的确定性自测；
- 通用进程采样器的短时 tooling smoke test；
- 进程样本 per-run 统计器的确定性自测；
- 跨 run median/worst 选择器的确定性和拒绝路径自测；
- 最终 evidence 装配器的链接一致性、来源与拒绝路径自测；
- restore/build；
- 当前仓库已有的 unit、model 与 deterministic interaction tests；
- analyzer/code-style 检查，warning 不得被静默忽略；
- 测试结果 artifact。

专项能力一旦进入仓库，其对应测试必须在同一阶段加入门禁，而不是提前把“不存在的模块”视为已通过：

- persistence/migration 出现后：migration fixture、原子保存、失败回滚和兼容性测试；
- host/foreground/platform adapter 出现后：对应 integration 与故障路径测试；
- UI 行为出现后：真实 UI automation、IME、Reduced Motion 与截图/录屏证据；
- performance-sensitive 能力出现后：按性能协议生成可复核原始结果。

当前仓库已经包含 .NET solution、Core/test 项目和多个 disposable Spike。现行 PR CI 会先验证文档、性能证据契约、通用进程采样器、per-run 样本统计器、跨 run 选择器和最终 evidence 装配器，再 restore/build solution、执行 `dotnet format --verify-no-changes`、运行测试并上传 TRX。性能契约自测只证明校验工具可执行；采样器 smoke test 只使用临时 `pwsh` 子进程和缩短参数验证采样/清理/fail-closed 行为；统计器与选择器自测只使用临时合成数据验证数学计算、选择语义与格式拒绝路径。这些 tooling tests 都**不产生 Zhuomian 基准或阈值校准证据**。Persistence/migration 生产能力尚未进入仓库，因此其专项测试目前是**尚未适用**，不是“已通过”证据。

## 2. 确定性状态机测试

交互核心使用可注入 clock、scheduler 和 event queue。测试不得依赖真实 `Task.Delay` 猜测时序。

必须覆盖：

- [INTERACTION_SPEC.md](INTERACTION_SPEC.md) 中全部 INV；
- Armed timeout 与所有失效条件；
- Focusing 时 Alt+Tab、Host lost、Esc 和激活另一 Space；
- 过期 Hover/collapse timer；
- IME composition 和 Auto Collapse；
- 500 次快速 Space 切换；
- ExternalForeground 下 Preview Item 与 Space 空白命中分离。

组合状态采用模型测试或属性测试生成事件序列，并验证每一步不变量。

## 3. Explorer 与平台生命周期

把 Explorer restart 视为正常事件：

1. Host invalidated。
2. 停止输入、动画和媒体。
3. 发现新 Shell/host。
4. 每显示器重建。
5. 恢复静态 Workspace。
6. 保持 Disarmed/NoKeyboardCapture。

`TaskbarCreated` 只能作为辅助信号，不得单独证明 Explorer 已重启：它可能因 DPI 变化广播，也可能在受控重启中未被观察到。恢复权威条件至少要关联新的有效 Shell window 与不同的 Shell process generation。

测试覆盖正常退出、崩溃、重启失败、重复重启、虚拟桌面切换、睡眠唤醒、锁屏和 UAC 安全桌面返回。

## 4. 故障注入

- 配置截断、单字段损坏、未知 Item kind、migration 中断；
- temp 写入失败、磁盘满、replace 失败、旧 revision 晚到；
- 文件删除、盘符变化、网络路径不可用；
- 图标、图片和视频解码失败或资源过大；
- 进程启动失败、URL scheme 被移除；
- Explorer 重启、Host 句柄失效、显示器热插拔；
- GPU device lost 与材质降级。

每个故障必须验证用户数据、可恢复性、日志脱敏和 UI 不被错误风暴占据。

## 5. 测试层级

- Unit：领域规则、排序、策略、迁移函数。
- Model：组合状态机与事件序列。
- Integration：host adapter、foreground classifier、launch、persistence。
- UI automation：真实点击、滚轮、键盘、IME、Reduced Motion。
- Performance：按 [PERFORMANCE_BUDGET.md](PERFORMANCE_BUDGET.md)。
- Compatibility：Windows build、DPI、刷新率、显示器、GPU 和 packaging。
- Soak：长时间驻留、循环交互和资源泄漏。

## 6. Flaky test 规则

- 不允许静默重试后视为通过。
- 首次确认 flaky 时立即建立带 owner 的隔离记录。
- 隔离测试仍在 CI 中显示失败或警告，并在 7 天内修复或获得明确延期。
- 核心状态、数据完整性和安全测试不得隔离。

## 7. 阶段质量门槛

门禁对应能力和支持范围，不因进入内测而豁免安全/焦点/数据要求。现有测试只有 Core 契约/防抖模型；下列尚不存在的生产专项测试不是已通过。

### Phase 0

每项 Spike 有可复现步骤、原始证据、失败条件和 ADR；组合不变量测试全绿。

### 工程增量 E1–E4

E1 领域行为测试可先行；E2 原生工程包只用于补全门禁。真实 launch 必须先过 launch-domain 负面测试；存储上线前必须过 revision、原子保存、迁移、备份恢复和故障注入。生产宿主/框架/发现/部署等需相应 ADR，不以假资源或 Spike 替代。

### V0.1 核心内测

产品真实闭环和量化验收；两模式原生证据、8 小时驻留、可重复部署、备份恢复及适用平台/性能门槛通过。至少验证 Windows 11 支持配置，双屏混合 DPI/热插拔、Win+D、图标冲突、普通窗口遮挡、Explorer、锁屏/UAC/睡眠和 IME。缺硬件保持阻断；限定单屏工程包不是完整内测验收。远程会话等未验证能力不宣称支持并 fail safe，公开前收敛支持矩阵。

基础外观/状态切换由真实截图/录屏与用户验收决定是否成熟。B1/B2 须记录局限并获验收，不能充当 B0 通过。

### V0.2 稳定内测

修复日常问题、扩大硬件/Windows build/刷新率与会话矩阵、辅助技术覆盖、安装升级/卸载、配置兼容和诊断。核心安全/焦点/数据失败仍阻断。

### V0.3 公开发布

24 小时 soak、性能回归、完整声明支持矩阵、安装/升级/回滚、发布签名门槛通过；许可证/依赖兼容、NOTICE、完整源码/构建步骤和隐私检查完成。可不含 Dock/视频；无自动更新不需伪造更新测试，未来启用仍过专项安全门禁。

## 8. 测试证据

PR 只接受可复核证据：命令、环境、退出码、结果 artifact、截图/录屏和已知限制。不得只写“测试通过”。

## 9. 新需求专项验收矩阵

| 契约 | 必须覆盖的验证 | 阻断/证据 |
|---|---|---|
| A 延迟与有效区域 | fake clock 0/20/120ms、离开/重入、边缘 Clamp、过期 generation、IME/菜单 | 确定性测试 + 实机轨迹；动画/收回数值未冻结 |
| Preview 恰好一次 | pointer-down 在 Hover deadline 前/同一 tick/后；布局变动、pointer-up、取消/目标删除/外部前台 | fake launcher 次数断言 + 原生点击 100 次；不得用真实 Task.Delay 猜时序 |
| B 单一视觉身份 | 原位→中央→反向返回，快速关闭/切 Space、外部前台/host 丢失/Reduced Motion | 带 frame/bounds 轨迹录屏、命中测试、等级/失败原因；B0 与降级分别验收 |
| 编辑与外部拖入 | A/B drag-enter/timer/drop/cancel/reject；日常不改布局，编辑取消恢复 | drop 不启动/移动/删除源；失败无半完成保存 |
| Grid/预览 | auto/override、0/少量/1000 Item、Resize、Item Hover | 全 Grid reflow 计数/布局稳定证据；虚拟化和源配置保留 |
| 主题/状态/图片 | 继承次序、unset/reset、状态切换、损坏/超大资产、高对比度/降级 | screenshot + 继承/解码负面测试；不得持续 idle 渲染 |
| 真实资源/发现 | 四类资源/手工添加、失效、去重、恶意快捷方式/URL/拖拽、许可来源 | 安全域和 provenance 测试；人工添加不依赖扫描 |
| 数据 | 断电式截断、磁盘满、旧 revision 晚到、迁移失败、共享引用/删除、备份恢复 | 不丢源文件、不静默重置、不以重试掩盖失败 |
| 平台 | 多屏/DPI/负坐标/热插拔，Win+D，图标重叠，前台事件和会话 | 环境矩阵 + 实机截图/录屏/日志；Spike 局部证据不得升级为支持 |

## 10. 内测与公开质量差异

内测可以限制声明支持环境、保留记录清楚的视觉降级和非核心缺陷；不得保留已知数据丢失、任意执行、焦点劫持或不安全恢复。公开版还需 24 小时驻留、更完整声明兼容矩阵、分发/签名/升级回滚、开源许可与可重现构建。阈值未校准时记录 provisional，不以 CI tooling 通过冻结阈值。
