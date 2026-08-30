# Zhuomian 测试与可靠性计划

## 1. Phase 0 Minimum CI

从首个解决方案出现开始，每个 PR 的持续门禁必须至少执行：

- 文档结构与本地链接检查；
- 性能证据契约校验器的确定性自测；
- 通用进程采样器的短时 tooling smoke test；
- 进程样本 per-run 统计器的确定性自测；
- restore/build；
- 当前仓库已有的 unit、model 与 deterministic interaction tests；
- analyzer/code-style 检查，warning 不得被静默忽略；
- 测试结果 artifact。

专项能力一旦进入仓库，其对应测试必须在同一阶段加入门禁，而不是提前把“不存在的模块”视为已通过：

- persistence/migration 出现后：migration fixture、原子保存、失败回滚和兼容性测试；
- host/foreground/platform adapter 出现后：对应 integration 与故障路径测试；
- UI 行为出现后：真实 UI automation、IME、Reduced Motion 与截图/录屏证据；
- performance-sensitive 能力出现后：按性能协议生成可复核原始结果。

当前仓库已经包含 .NET solution、Core/test 项目和多个 disposable Spike。现行 PR CI 会先验证文档、性能证据契约、通用进程采样器和 per-run 样本统计器，再 restore/build solution、执行 `dotnet format --verify-no-changes`、运行测试并上传 TRX。性能契约自测只证明校验工具可执行；采样器 smoke test 只使用临时 `pwsh` 子进程和缩短参数验证采样/清理/fail-closed 行为；统计器自测只使用临时合成 CSV 验证数学计算与格式拒绝路径。这些 tooling tests 都**不产生 Zhuomian 基准或阈值校准证据**。Persistence/migration 生产能力尚未进入仓库，因此其专项测试目前是**尚未适用**，不是“已通过”证据。

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

### Phase 0

每项 Spike 有可复现步骤、原始证据、失败条件和 ADR；组合不变量测试全绿。

### V0.1

产品规范的量化验收通过；无焦点劫持、双 Focus、卡死或幽灵窗口。

### V0.2

真实 Item 启动安全门槛、migration、原子保存、损坏恢复和 Explorer 恢复通过。

### V0.3

24 小时 soak、安装升级、性能回归、完整兼容矩阵和发布签名门槛通过。

## 8. 测试证据

PR 只接受可复核证据：命令、环境、退出码、结果 artifact、截图/录屏和已知限制。不得只写“测试通过”。
