# ADR-0004: Incremental delivery without closing unresolved gates

- Status: Proposed
- Date: 2026-10-12
- Owners: Niceskys (decision), AI implementation (evidence)
- Supersedes: none
- Superseded by: none

## Context

旧路线要求所有 Phase 0 退出后再做固定假资源原型，基础编辑/持久化/外观分散到后期。真实内测需要一个可运行闭环；当前存在有效 CI/Core/Spike/性能工具，但没有生产 App，P0-01 至 P0-07 尚未全部关闭。

## Decision drivers

- 推进可复核实现，避免全局等待及无限规划。
- 不能靠降低安全/兼容门槛实现加速。
- 假资源工程包、可用内测版与公开版有明确差别。
- Optional WorkerW/Backdrop/视频不能成为静态核心成立条件。

## Options considered

### All Phase 0 before any implementation

保持简单阶段排序，但让无平台依赖领域/数据工作等待硬件，不能尽早验证闭环。

### Ungated application implementation

直接复制 Spike 或先做 UI 再处理安全，无法证明平台和数据底线；拒绝该方向。

### Independent domain work and gated integration

候选方向：先实现不依赖 Windows 的领域契约和确定性测试；在受控验证包中取得真实集成证据；每项能力按门禁启用，最终收敛至内测闭环。

## Decision

本 PR 提议第三种路线，阶段契约见 ROADMAP。此 ADR 为 Proposed，规范 PR 合并不自动接受宿主、框架、打包、持久化或发现方案。

不修改旧证据、不关闭未通过门槛。P0-01/02/03/05/06/07 的基础路径和相关生产集成须通过后交付内测；P0-04 仍需实机双屏/DPI等覆盖，单屏包只用于工程验证，不冒充完整内测验收。Enhanced 可以明确拒绝/延后，但启用时仍需独立证据。所有支持声明必须有实际环境矩阵。E2 只完成当期原生 fixture 的证据，完整静态性能在 E4/E5 收敛；远程使用可不支持，但真实会话边界安全暂停/恢复仍须验证，不能据“不支持”关闭 ADR-0002 风险。

## Consequences

后续 PR 分为领域测试、平台集成、启动安全、持久化、编辑浏览、外观、实机验收等可回滚关注点。并行工作不能被理解成放行未验证 UI；软件内部也应禁止或安全关闭未验证能力。

## Validation and rollback

规范接受以本 PR 的差异矩阵和一致性检查为依据；生产架构仍须各自 ADR/测试/实机证据。每阶段退出记录含环境、命令、结果、限制、降级与风险；任何安全/数据/焦点失败阻断。可独立撤回本路线规范变更而不改现有 Core、Spike 和 CI。

## References

- [Development plan](../DEVELOPMENT_PLAN.md)
- [Roadmap](../ROADMAP.md)
- [Phase 0 evidence status](../PHASE_0_STATUS.md)
- [Reliability gates](../RELIABILITY_TEST_PLAN.md)
