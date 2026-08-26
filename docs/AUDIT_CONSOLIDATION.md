# Audit A-J 汇总结论

> 审计对象：初始 `docs/DEVELOPMENT_PLAN.md`，基线提交 `d2411d9`  
> 汇总状态：2026-08-26 已转化为规范基线

## 总体结论

产品方向保留，但初始计划不能直接冻结。主要原因不是功能想法错误，而是产品边界、组合状态、Windows 宿主、持久化、安全和工程门禁尚未形成唯一可验证契约。

## 审计处置表

| Audit | 原结论 | 统一处置 | 当前状态 |
|---|---|---|---|
| A 产品边界 | 有条件通过 | 增加 Primary Job、Preview direct launch，缩减 V0.1 | 规范已解决 |
| B 交互状态 | 不通过 | 拆正交状态、焦点模式、优先级、token 与不变量 | 规范已解决，待实现验证 |
| C Desktop | Phase 0 blocker | Enhanced/Fallback host、per-monitor、恢复和 Z-order 契约 | 仍阻断，待 Spike |
| D UI/Glass/Motion | 不通过 | 三级材质、模拟玻璃一等方案、Reduced Motion | 规范已解决，待视觉 Spike |
| E 性能 | 不通过 | 建立测量协议、不变量、虚拟化和 soak | 协议已解决，数值待校准 |
| F 数据 | 不通过 | 根级 ItemStore、revision、迁移、未知类型保留 | 规范已解决，待原型 |
| G 安全 | 不通过 | Application/Command 安全域分离、URL allowlist | 规范已解决，真实 Item 前阻断 |
| H 测试 | 不通过 | Minimum CI 前置、确定性组合测试、故障注入 | 基线已建立，待测试工程 |
| I AI 治理 | 不通过 | AGENTS、PR、ADR、规范/实现分离审查 | 文件已建立，Ruleset 待人工配置 |
| J Roadmap | 修订后通过 | CI/Packaging/DPI 前置，拆分 V0.2/V0.3 | 已解决 |

## 跨审计统一决策

### 1. 焦点

Visual Focus 不等于 Keyboard Focus。Hover 永不抢键盘；直接键入搜索从 V0.1 移除，待 Focus Spike 后再决定。

### 2. Desktop Hosting

“贴附桌面”是产品目标，不等于承诺 WorkerW。WorkerW 只能是 EnhancedDesktopHost；FallbackDesktopHost 是正式产品模式，不是残废版。

### 3. 材质

真实 Blur 不是必要条件。Idle 默认模拟玻璃；Focused 才可使用单一增强 Backdrop；Solid/accessibility 必须完整可用。

### 4. Item 与命令

用户界面统一把应用、文件、文件夹和 URL 视为 Item，但安全实现不统一为任意 command。`ApplicationItem` 不接受自由命令行；高级命令未来另立安全域。

### 5. 数据

根级 ItemStore 是当前选择。运行状态不持久化；保存带 revision；迁移失败回退；未知类型保留原始数据。

### 6. 路线

CI、Packaging、多显示器/DPI、诊断和恢复属于 Phase 0。完整视频产品后移，避免与核心交互同时集成。

## 仍然开放的阻断问题

- Enhanced Desktop Host 是否可长期维护。
- Fallback Host 的产品可接受程度。
- 前台分类的准确性与 Windows 版本差异。
- Packaging/依赖组合。
- 增强 Backdrop 的真实成本。
- Provisional 性能阈值的实机校准。
- 虚拟桌面最终产品语义。

这些问题只能通过 Spike、原始证据和 ADR 关闭，不能通过继续扩写计划书关闭。

## 不再采用的初始表述

- 单一 `Passive/Armed/Active/Suspended` 内部状态机。
- Focused 后无条件直接键入搜索。
- V0.3 才开始建设 CI、Packaging 或诊断。
- 所有 Space 默认长期真实 Acrylic。
- `executable + arguments` 作为通用 ApplicationItem。
- Space 只保存 `itemIds[]` 却没有 Item 实体容器。
