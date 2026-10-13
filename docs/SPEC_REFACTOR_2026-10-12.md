# 开发前规范重构：差异矩阵与交接

> 日期：2026-10-12；基线 main：`6882cef42b4c0c564825a051993b7e97b7c9e817`。
> 本次只修改规范；任何新能力均为待实现。既有 Spike 和测试通过不代表生产能力已完成。

## 1. 旧规范与新需求差异矩阵

| 主题 | 旧规范/实际状态 | 新契约与处置 | 主要落点 |
|---|---|---|---|
| 定位 | spatial workspace；未明确免费与公开节奏 | 免费，未来完整开源；先真实可用、视觉成熟内测，再公开；许可证尚待确定 | README、PRODUCT_SPEC |
| 开发职责 | AI 治理原则，未明确分工 | AI 开发/文档/自动测试/排查；用户需求决策与实机验收 | AGENTS、DEVELOPMENT_PLAN |
| 逐 Space 模式 | Hover/Click 只有触发差别 | A 原位附近悬停放大；B 点击标题/空白移至本屏中央 | PRODUCT_SPEC、INTERACTION_SPEC |
| 延迟 | 所有数值 provisional | A 默认 20ms、可逐 Space 设 0–120ms 是确定要求；动画/收回参数待测 | INTERACTION_SPEC、PERFORMANCE_BUDGET |
| B 动画 | 泛化 Focus/Return | 原框连续移动放大及反向返回优先；降级必须记录并展示实际能力 | INTERACTION_SPEC、ARCHITECTURE |
| Preview | 已要求单击直接打开；只有模拟证据 | 保留并补充 Hover、pointer-down、布局变更竞态与恰好一次启动 | INTERACTION_SPEC、RELIABILITY_TEST_PLAN |
| 布局 | Item transform 不 reflow | 保留；Space 尺寸变化可重排，图标 Hover 不触发全 Grid 布局 | INTERACTION_SPEC |
| 可用范围 | V0.1 固定三框/假 Item，真实数据后移 | V0.1 变为真实资源闭环内测；固定假资源只做工程 fixture | ROADMAP、PRODUCT_SPEC |
| 发现应用 | 最小自研 Provider | Windows 原生优先；评估成熟开源复用、许可证与 provenance；手工添加始终可用 | ARCHITECTURE、ADR-0003 |
| 拖入/编辑 | Drag 含义未拆分 | 日常外部拖入只建入口、冻结展开；编辑负责布局；删除不删源 | INTERACTION_SPEC、SECURITY |
| 预览数量 | 固定 PreviewCapacity | 默认随数量/空间自动适配；允许覆盖；算法/上下限待验证 | PRODUCT_SPEC、ARCHITECTURE |
| 外观 | 高级自定义 V0.4；静态背景含混 | 统一主题→Space→待机/展开覆盖；模拟玻璃、透明度、颜色、圆角、阴影、图片提前 | PRODUCT_SPEC、ARCHITECTURE |
| Dock/视频 | 视频生命周期前置实验 | 均后续可选，不阻碍静态核心交付；启用前仍须安全/性能门槛 | ROADMAP、PERFORMANCE_BUDGET |
| 平台 | 候选 WinUI/WorkerW；部分单屏 Spike | Windows 11 优先；未验证跨屏/DPI/Win+D/图标共存不得宣传已支持 | PHASE_0_STATUS、RELIABILITY_TEST_PLAN |
| 交付顺序 | 全 Phase 0 完成才开始固定原型 | 无平台依赖领域工作可先行；带门禁集成；工程验证包≠可用内测版≠公开版 | DEVELOPMENT_PLAN、ROADMAP、ADR-0004 |
| 保留底线 | 正交状态、安全启动、原子保存、恢复、CI | 全部保留；不接受靠改文档关闭风险 | SECURITY、全部受影响规范 |

## 2. 实际代码审计

- `src/` 仅 `Zhuomian.Core/Interaction`：状态枚举、快照和 `InteractionSafety` 判定；没有 App、生产宿主、launcher、持久化、发现服务、布局或渲染模块。
- `tests/` 只有安全契约测试及六个前台防抖参考模型测试；未覆盖完整状态机、Hover/点击竞态、真实文件启动、数据恢复或 UI。现有测试不变，也不作为新需求已实现证据。
- `spikes/` 是一次性 Win32 实验；实机证据主要 Windows 11 build 26100、单显示器。ADR-0001/0002 仍 Proposed。完整限制保留在 PHASE_0_STATUS。
- 性能脚本包含 evidence validator、进程采样、per-run 汇总、跨 run 选择和 metadata 装配；现行 CI 已包含装配器自测。无产品基准，无阈值冻结。
- 当前无发布应用；仓库无 LICENSE 文件，免费/未来开源是产品方向，不能据此声称目前已获得某开源许可证授权。

## 3. 未决事项和验证责任

| 待验证 | AI 下一步 | 用户验收/约束 |
|---|---|---|
| A 有效区域、收回延迟、尺寸与曲线 | 确定性竞态测试、原生截图/录屏及参数对比 | 实机确认误触/操作稳定性；不得改 20ms 默认和 0–120ms 范围 |
| B 连续变换、工作区中心与降级级别 | 原生轨迹/命中/中断测试、同协议性能记录 | 确认视觉是否成熟；降级不是完整动画验收 |
| 自动预览容量、默认模式和外观值 | 提供候选及边界测试 | 尚未确认具体默认模式/算法/色值 |
| Host、Win+D、桌面图标、IME、真实前台事件 | 集成验证和 ADR，失败安全关闭相关能力 | Windows 实机、锁屏/UAC/睡眠；涉及用户会话的破坏性操作另行安排 |
| 双屏混合 DPI、热插拔、远程会话 | 保存可重跑步骤和脱敏证据 | 无设备/条件时保持未验证，不伪造覆盖 |
| 打包、持久化、发现依赖与许可证 | 独立 ADR/安全测试；许可证兼容性清单 | 选定分发方式与开源许可证后才发布 |

## 4. 后续 AI 可直接执行的第一项任务

以 ROADMAP 的 E1 为入口：实现可注入 clock/scheduler 的交互控制器与确定性外部行为测试，覆盖 A/B 路由、0/20/120ms、Preview 恰好一次启动（使用 fake launcher）、拖入抑制、generation 和安全返回。只改领域模块和相应测试，不实现 Win32 hook、文件启动、宿主、UI 或持久化。随后分别开展受控真实资源/安全持久化与 Windows 集成 PR，不重新写整套计划。

该任务须在本规范 PR 合并后开始；尚未接受的 ADR 不能充当生产平台批准。

## 5. 2026-10-13 PR #25 复核修正

| 复核问题 | 修正 | 保留门禁 |
|---|---|---|
| ADR-0001 整体接受要求 Enhanced 恢复，但新路线允许 Enhanced 延后 | 将 Fallback 基础接受与 Enhanced 启用范围分开 | 基础焦点/DPI/恢复/Z-order/图标/生命周期仍须验证；WorkerW 继续未接受 |
| E2 要求完整静态性能，但搜索/滚动/图片依赖 E4 | E2 当期原生 fixture 探索；E4/E5 收敛完整产品基准 | P0-07、两档机器和实测校准不提前关闭 |
| 发现能力可仅用评估报告替代 | V0.1/E4 要求至少一个实际可用 provider | 许可/安全/去重/失效测试；人工添加不依赖发现成功 |
| Preview 竞态把既有外部前台误写为取消条件 | 仅序列期间新出现/改变的前台使旧事件无效 | 外部前台的 Preview 单击仍可用，Hover 未 Armed 仍受限 |
| 远程会话“不支持”可能与 ADR-0002 门禁冲突 | 完整使用可后移，真实会话边界暂停/释放/恢复仍是内测门槛 | 没有实机边界证据仍保持未通过 |

复核只修正规范，不修改生产代码、Spike、测试或 CI，不合并 PR。所有未确认默认值和原生平台能力继续待验证。
