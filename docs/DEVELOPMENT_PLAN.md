# Zhuomian 开发计划

> 状态：Post-audit baseline v0.2
> 基线日期：2026-08-26
> 当前状态更新：2026-08-30

## 1. 执行结论

Zhuomian 的产品方向成立，但 Desktop Hosting、前台窗口感知、键盘焦点、真实背景模糊和部署方式仍包含未经完整验证的工程假设。项目当前仍处于 **Phase 0：Specification & Feasibility**，不得把 WorkerW、真实 Acrylic 或任何单一安装方式描述为既定生产架构。

产品的首要任务是：

> 让用户按照个人逻辑组织并快速访问应用、文件、文件夹和链接，同时保持 Windows 桌面安静、低干扰且长期可用。

壁纸和视频属于辅助体验。真实模糊失败时，模拟玻璃或实体材质必须仍然提供完整体验。

## 2. 规范层级

发生冲突时，按下列顺序解释；仍无法消解时停止实现并发起规范变更：

1. [PRODUCT_SPEC.md](PRODUCT_SPEC.md)：产品边界和用户可见行为。
2. [INTERACTION_SPEC.md](INTERACTION_SPEC.md)：输入、状态、事件与优先级。
3. [ARCHITECTURE.md](ARCHITECTURE.md)：技术边界、数据和降级路径。
4. [SECURITY.md](../SECURITY.md)：安全与隐私底线。
5. [PERFORMANCE_BUDGET.md](PERFORMANCE_BUDGET.md)：资源与测量契约。
6. [RELIABILITY_TEST_PLAN.md](RELIABILITY_TEST_PLAN.md)：测试证据和质量门槛。
7. [DIAGNOSTICS.md](DIAGNOSTICS.md)：诊断字段与隐私约束。
8. [ROADMAP.md](ROADMAP.md)：阶段顺序和退出条件。
9. [ADR/](ADR/README.md)：已接受的架构决策。

本文件只负责总体方向和执行顺序，不重复专项规则。

## 3. 不可违反的产品规则

- 未展开 Space 中已经可见的 Item，单击应直接打开；Focus Space 不是启动 Item 的必经步骤。
- 存在外部前台应用时，Hover 不得自动展开 Space；用户必须先在可见桌面区域表达明确意图。
- Hover 和纯视觉展开不得抢键盘焦点。搜索必须由显式键盘激活或点击取得输入焦点。
- 同一时刻至多有一个主要 Focused Space。
- Idle/Passive/Suspended 且没有动画、指针或媒体更新时，不得运行 CPU 驱动的逐帧循环。
- WorkerW 等未文档化 Shell 路径只能作为可替换增强宿主，必须存在正式降级路径。
- `ApplicationItem` 只能启动已识别应用；任意命令或脚本属于未来独立安全域。
- 运行时 Hover、动画进度、搜索输入焦点等状态不得写入主配置。
- 数据迁移失败必须保留可恢复的旧数据，不得静默重置配置。
- V0.1/V0.2 不以真实 Blur、动态壁纸或自动应用扫描作为核心体验成立条件。

## 4. 技术基线

候选基线为 C#、WinUI 3、Windows App SDK、Composition 与必要的 Win32 interop。它是 Phase 0 的验证对象，不是免审计的永久决定。

正式实现必须隔离以下高风险能力：

- `IDesktopHost`：桌面宿主、Z-order 和 Explorer 生命周期。
- `IForegroundClassifier`：桌面可用性和外部前台分类。
- `IItemLauncher`：受控的应用、文件、文件夹和 URL 打开行为。
- `IWorkspaceStore`：带 revision 的原子保存、迁移和恢复。
- `IMediaLifecycle`：Play、Pause、Stop、Release。
- `IGlassMaterialPolicy`：材质等级和自动降级。

## 5. Phase 0 阻断项

在全部关闭前，不得进入 V0.1 正式原型：

| ID | 阻断项 | 必需证据 |
|---|---|---|
| P0-01 | Desktop Host | 增强与降级两条路径的演示、限制、恢复测试和 ADR |
| P0-02 | 焦点与输入 | NoActivate、显式键盘模式和外部输入不被劫持的自动测试 |
| P0-03 | 前台分类 | Desktop/ExternalForeground/Suspended 的实机矩阵和误判记录 |
| P0-04 | 多显示器/DPI | per-monitor host、坐标转换、热插拔和混合 DPI 证据 |
| P0-05 | 材质 | 模拟玻璃基线、Backdrop 候选、降级阈值与截图 |
| P0-06 | Packaging | packaged/unpackaged 与依赖方式的兼容结论和 ADR |
| P0-07 | 性能协议 | 可重复的 Release 基准、采样脚本和 provisional 阈值 |
| P0-08 | 工程门禁 | Minimum CI、PR 模板、规范检查和测试工程骨架 |

## 6. 固定执行顺序

1. 合并本次审计后规范基线。
2. 配置 `main` Ruleset 和必要检查。
3. 建立最小解决方案、测试项目和诊断日志。
4. 逐个完成 Phase 0 Spike；Spike 不得直接复制为生产代码。
5. 每项 Spike 以 ADR 记录接受、拒绝或延后。
6. 仅在 Phase 0 全部退出条件满足后进入 V0.1。
7. V0.1 只验证 Space 核心交互。
8. 真实 Item 在安全、持久化和恢复门槛通过后分批进入 V0.2。

详细阶段参见 [ROADMAP.md](ROADMAP.md)。

## 7. Definition of Done

- 需求和非目标明确，PR 可独立回滚。
- 行为符合规范；规范变更与实现变更可以分别审查。
- 关键状态和错误路径有外部行为测试。
- UI 变更有真实截图或录屏证据。
- 性能敏感变更有相同协议下的前后数据。
- 数据变化包含迁移、失败恢复和兼容性测试。
- 安全边界未被扩大；扩大时先完成威胁审查。
- 文档、ADR、测试和实现同步。
- CI 通过且没有通过删除或弱化测试规避门槛。

## 8. 当前状态

- Audit A-J：已汇总，参见 [AUDIT_CONSOLIDATION.md](AUDIT_CONSOLIDATION.md)。
- Phase 0A：已完成；规范基线、`main` Ruleset、Minimum CI、测试工程骨架和诊断约定已经建立。
- Phase 0B：进行中；Desktop Host、NoActivate/显式焦点、Explorer 恢复、前台分类、前台事件防抖参考模型、单显示器 DPI、Fallback 可用性和真实外部全屏分类均已有相应证据，详见 [PHASE_0_STATUS.md](PHASE_0_STATUS.md)。
- 仍未满足 Phase 0B 退出条件：真实双显示器混合 DPI/热插拔、用户参与的锁屏/UAC/睡眠/远程会话、真实前台事件投递与调度集成（防抖策略已有确定性参考模型）、真实生命周期资源释放、增强宿主剩余恢复路径与 Fallback 图标冲突/Z-order 生产策略仍待验证。
- P0-05（材质）和 P0-06（Packaging）尚未开始完成性验证；P0-07 已建立机器可校验的性能证据契约、通用进程资源采样器、确定性的 per-run Average/P95/P99/max 统计器、显式 cross-run median/worst 选择器及对应 CI tooling tests，但 Release x64 Zhuomian 场景编排、完整 final evidence metadata 组装、Baseline/Enhanced 实机结果和 provisional 阈值校准尚未完成，因此 P0-07 仍为未完成；P0-08 已完成。
- 产品正式实现仍未开始；现有 `spikes/`、测试参考模型和性能协议工具是可丢弃/可替换验证资产，不得直接视为 V0.1 生产实现。
- WorkerW、真实背景 Blur 与 Packaging 均未成为已接受的唯一生产方案。
