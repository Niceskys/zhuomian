# Zhuomian

Zhuomian 是一个面向 Windows 的空间化桌面工作区设想，用低干扰的桌面 `Space` 容器来组织并快速打开应用、文件、文件夹和链接。

当前仓库处于 **阶段 0：规格定义与可行性验证（Specification & Feasibility）**。目前还没有可用于生产环境的完整应用。在正式进入实现阶段之前，需要先验证桌面嵌入、焦点行为、背景效果、打包方式和性能等关键能力是否可行。

## 从这里开始

- [开发计划](docs/DEVELOPMENT_PLAN.md)
- [产品规格](docs/PRODUCT_SPEC.md)
- [交互规格](docs/INTERACTION_SPEC.md)
- [架构设计](docs/ARCHITECTURE.md)
- [诊断契约](docs/DIAGNOSTICS.md)
- [阶段 0 状态](docs/PHASE_0_STATUS.md)
- [路线图](docs/ROADMAP.md)
- [Audit A-J 综合整理](docs/AUDIT_CONSOLIDATION.md)
- [贡献指南](CONTRIBUTING.md)
- [安全基线](SECURITY.md)

产品最核心的交互规则很简单：当前可见的预览 `Item` 应当可以直接启动；`Space Focus` 只用于浏览完整集合。悬停操作不得抢占键盘焦点；当外部应用正处于前台时，也不得仅因悬停就展开桌面内容，除非用户已经明确将当前可见桌面置于 Armed 状态。
