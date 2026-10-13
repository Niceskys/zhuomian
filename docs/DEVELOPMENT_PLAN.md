# Zhuomian 开发计划

> 状态：开发前规范基线 2026-10-12
> 历史审计基线：2026-08-26；本次基线 main：`6882cef42b4c0c564825a051993b7e97b7c9e817`

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

本文件只负责总体方向和执行顺序，不重复专项规则。安全/数据不变量不因排序较后而被产品行为豁免。新旧差异及第一项可执行任务见 [SPEC_REFACTOR_2026-10-12.md](SPEC_REFACTOR_2026-10-12.md)。

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

P0 编号保留；本次没有关闭任何未通过项。允许不依赖平台的领域实现先行，以及受控工程验证包完成集成证据；不允许在门禁未通过时将验证包作为可日常使用的 V0.1 内测版。能力按基础/增强和实测环境记录，不能以删去支持声明冒充已通过。详见 [ADR-0004](ADR/0004-incremental-delivery-gates.md)。

| ID | 阻断项 | 必需证据 |
|---|---|---|
| P0-01 | Desktop Host | 基础 Fallback 的演示、限制、恢复和 ADR；增强明确接受/拒绝/延后（非必需） |
| P0-02 | 焦点与输入 | NoActivate、显式键盘模式和外部输入不被劫持的自动测试 |
| P0-03 | 前台分类 | Desktop/ExternalForeground/Suspended 的实机矩阵和误判记录 |
| P0-04 | 多显示器/DPI | per-monitor host、坐标转换、热插拔和混合 DPI 证据 |
| P0-05 | 材质 | 模拟玻璃基线、A/B 原生动效、降级与截图；Backdrop 候选非必需 |
| P0-06 | Packaging | packaged/unpackaged 与依赖方式的兼容结论和 ADR |
| P0-07 | 性能协议 | 可重复的 Release 基准、采样脚本和 provisional 阈值 |
| P0-08 | 工程门禁 | Minimum CI、PR 模板、规范检查和测试工程骨架 |

## 6. 增量执行与责任

1. 独立审查并合并本规范 PR，保留既有 CI、Ruleset、测试、ADR 和风险记录。
2. E1 先交付不依赖 Windows 的可执行领域规则与行为测试；第一项控制器任务见差异矩阵。避免继续扩写整套规划。
3. E2 以小型可回滚平台 PR 验证真实安全输入、宿主、恢复、A/B 动效；没有证据不接受候选架构。
4. E3 分别实现受控真实资源和安全持久化；按启动、数据专项门禁审查后接 UI。
5. E4 完成编辑、浏览、拖入和基础外观，E5 集成实机验证，达标后交付 V0.1 核心内测。
6. V0.2 稳定使用、V0.3 公开发布；Dock/视频及高级定制 V0.4 可选。完整顺序与退出条件见 [ROADMAP.md](ROADMAP.md)。

AI 负责开发、文档、自动测试、诊断和交接；用户负责需求取舍与实机验收。每项任务注明依赖、允许修改模块、验收命令、Windows 证据和未决项。环境/设备缺失必须记录实际阻断，不能要求用户写代码或编造通过。未经用户确认的默认模式、动画参数、容量算法等保持待验证。

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

## 8. 当前状态（历史证据保留）

- Audit A-J：已汇总，参见 [AUDIT_CONSOLIDATION.md](AUDIT_CONSOLIDATION.md)。
- Phase 0A：已完成；规范基线、`main` Ruleset、Minimum CI、测试工程骨架和诊断约定已经建立。
- Phase 0B：进行中；Desktop Host、NoActivate/显式焦点、Explorer 恢复、前台分类、前台事件防抖参考模型、单显示器 DPI、Fallback 可用性和真实外部全屏分类均已有相应证据，详见 [PHASE_0_STATUS.md](PHASE_0_STATUS.md)。
- 仍未满足 Phase 0B 退出条件：真实双显示器混合 DPI/热插拔、用户参与的锁屏/UAC/睡眠/远程会话、真实前台事件投递与调度集成（防抖策略已有确定性参考模型）、真实生命周期资源释放、增强宿主剩余恢复路径与 Fallback 图标冲突/Z-order 生产策略仍待验证。
- P0-05（材质）和 P0-06（Packaging）仍未完成验证；P0-07 已建立机器可校验的性能证据契约、通用进程资源采样器、确定性的 per-run Average/P95/P99/max 统计器、显式 cross-run median/worst 选择器、最终 evidence metadata 装配器及对应 CI tooling tests，但 Release x64 Zhuomian 场景编排、Baseline/Enhanced 实机结果和 provisional 阈值校准尚未完成，因此 P0-07 仍为未完成；P0-08 已完成。
- 产品正式实现仍未开始；现有 `spikes/`、测试参考模型和性能协议工具是可丢弃/可替换验证资产，不得直接视为 V0.1 核心内测实现。
- WorkerW、真实背景 Blur 与 Packaging 均未成为已接受的唯一生产方案。

本次重排仅改变依赖顺序与交付范围；P0-01 至 P0-07 未因此关闭，ADR-0001/0002 保持 Proposed。当前没有生产控制器、launcher、持久化或渲染；已有 Core/参考模型测试并不覆盖全部目标 INV。
