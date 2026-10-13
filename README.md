# Zhuomian

Zhuomian 是面向 Windows 的免费桌面资源组织工具，使用 Space 分类并快速打开应用、文件、文件夹和 HTTP(S) 链接。目标是先完成真实可用、视觉成熟的内部测试版，再正式公开发布并完整开源。当前尚无 LICENSE，具体开源许可证和依赖许可兼容性待确定。

当前仍处于 **Phase 0 / 开发前规范基线**，没有生产应用。`src/` 包含 Core 交互安全契约和 E1 Space 领域交互控制器（[调用契约与验证范围](src/Zhuomian.Core/Interaction/README.md)），E1 Workspace/Space/ItemStore 领域模型（[引用、配置与恢复契约](src/Zhuomian.Core/WorkspaceModel/README.md)），以及 E1-3 纯领域布局引擎（[几何、Grid、容量和恢复计算](src/Zhuomian.Core/Layout/README.md)），E1-4 外观逐属性继承与最终安全降级（[配置、环境与渲染集成契约](src/Zhuomian.Core/Appearance/README.md)），另有确定性测试、一次性 Windows Spike 和性能证据工具。局部实验通过不能当作已支持的平台能力。

新路线将领域规则、原生安全集成、真实资源/安全持久化、编辑/基础外观逐步收敛为 V0.1 核心 Windows 内测版；V0.2 稳定内测，V0.3 正式公开，Dock/视频等 V0.4 可选。用户负责需求决策与实机验收，AI 负责开发、文档、自动测试和排查。

## Start here

- [2026-10-12 specification difference matrix](docs/SPEC_REFACTOR_2026-10-12.md)
- [Development plan](docs/DEVELOPMENT_PLAN.md)
- [Product specification](docs/PRODUCT_SPEC.md)
- [Interaction specification](docs/INTERACTION_SPEC.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Diagnostic contract](docs/DIAGNOSTICS.md)
- [Phase 0 status](docs/PHASE_0_STATUS.md)
- [Roadmap](docs/ROADMAP.md)
- [Audit A-J consolidation](docs/AUDIT_CONSOLIDATION.md)
- [Contributing](CONTRIBUTING.md)
- [Security baseline](SECURITY.md)

The defining product rule is simple: a visible preview Item launches directly, while Space Focus exists to browse the complete collection. Hover must never steal keyboard focus or expand while an external foreground application is in use unless the user has explicitly armed the visible desktop.

每个 Space 独立选择：A 悬停在原位附近放大（默认等待 20ms，可设 0–120ms）；B 点击标题/空白后优先连续移位放大到当前显示器中央，关闭反向返回。B 动画降级须明确说明。可见预览 Item 单击直接打开，不能强制先展开。具体动画、收回和自动预览算法仍待原生实测。
