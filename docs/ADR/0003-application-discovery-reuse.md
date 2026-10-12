# ADR-0003: Application discovery reuse boundary

- Status: Proposed
- Date: 2026-10-12
- Owners: Niceskys (decision), AI implementation (evidence)
- Supersedes: none
- Superseded by: none

## Context

可用内测需要添加和打开真实应用，但当前没有 discovery 或 launcher 生产模块。旧路线只写 Start Menu Provider，容易发展为复杂自研扫描器。项目未来完整开源，依赖许可尚未冻结。

## Decision drivers

- 人工选择添加可独立完成核心操作，不依赖全量扫描。
- 优先 Windows 原生能力和成熟开源复用。
- 来源可追溯；发现不扩大任意命令执行权限。
- 依赖许可证与最终开源许可证兼容；无未披露授权成本。

## Options considered

### Windows native providers

评估已注册应用与 Start Menu/受控快捷方式；保留 identity/provenance。不假定具体 API 能覆盖全部桌面/UWP/packaged 应用；每种启动语义分别验证。

### Mature open-source reuse

可复用 provider/库或经许可复用实现，需确认许可证、维护/发布状态、构建依赖、体积、NOTICE 和安全边界。未选择具体项目；不得把未经核对的依赖称为成熟兼容方案。

### Custom broad filesystem scanner

不作为初期方案；复杂全盘扫描、启发式猜测命令和长期监控不属于核心交付。

## Decision

候选方向：原生 provider 优先，必要时许可兼容开源复用；保留人工添加。此 ADR 为 Proposed，尚未批准具体 API/依赖/生产实现。

Provider 只返回规范化应用身份、展示元数据、provenance 和受控启动描述。Launcher 独立执行 SECURITY.md 的校验；快捷方式参数不默认为安全。网络/脚本/自定义协议不能经发现绕过安全域。

## Consequences

首次可用版须交付至少一个实际可用且经过验证的 provider，不能只交候选评估报告；不保证发现所有应用；明确展示失效/未支持来源，并允许手工添加。不能通过扫描遗漏阻塞整个 Workspace。去重、卸载、来源冲突和网络阻塞需测试。

## Validation and rollback

接受前提供：候选/API 一手文档核对、许可/NOTICE 清单、packaged/unpackaged 代表样本、同源/跨源去重、卸载失效、安全负面测试、异步取消/超时和性能证据。依赖拒绝或失败时停用该 provider，已保存人工 Item 和可用数据保持完整。启动策略须另行 ADR，未通过安全测试不得启用真实 launch。

## References

- [Architecture](../ARCHITECTURE.md)
- [Security baseline](../../SECURITY.md)
- [Roadmap](../ROADMAP.md)
