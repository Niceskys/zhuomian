# Zhuomian Roadmap

> 原则：每阶段交付可独立验证的增量，不形成 V0.2/V0.3 集成悬崖。

## Phase 0A — Governance foundation

交付：规范基线、ADR 模板、Minimum CI、PR 模板、诊断字段约定和测试工程骨架。

退出：文档检查可执行；`main` Ruleset 已由管理员启用；首个测试在 CI 中运行。

## Phase 0B — Desktop and input feasibility

独立 Spike：

- Desktop Host：Enhanced 与 Fallback。
- Foreground classification。
- NoActivate visual focus 与显式 keyboard mode。
- Explorer restart。
- per-monitor host、混合 DPI 和热插拔。

退出：满足 [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) P0-01 至 P0-04，并有 ADR。

## Phase 0C — Rendering and performance feasibility

- Simulated glass 正式基线。
- Focused enhanced backdrop 候选。
- transform-only Item magnification。
- Idle lifecycle 与可重复性能基准。
- Play/Pause/Stop/Release 的最小媒体资源实验，不实现完整视频壁纸。

退出：模拟玻璃在所有目标环境可用；增强材质可明确接受或拒绝；性能协议冻结。

## Phase 0D — Packaging and recovery

- packaged/unpackaged 比较。
- framework-dependent/self-contained 比较。
- 配置路径、日志、启动、更新、卸载和 host 兼容性。
- 原子保存、revision、migration 与 last-known-good 原型。

退出：Packaging 与 Persistence ADR 被接受；安装运行路径可重复。

## V0.1 — Interaction prototype

只交付三个固定 Space、假 Item、Preview direct launch、Focus/Return、Hover gating、显式搜索入口、Click-only/Reduced Motion 和静态背景。

退出条件采用 [PRODUCT_SPEC.md](PRODUCT_SPEC.md) 的量化验收。任何一项核心误触、焦点或状态不变量失败均阻断。

## V0.2A — Safe real items

- 手工添加已识别应用、文件、文件夹和 HTTP(S) URL。
- 受控 launcher、安全确认和失效 Item。
- 不提供任意 arguments 编辑器或自定义 scheme 默认执行。

退出：安全测试、参数测试和失败路径通过。

## V0.2B — Durable workspace

- Workspace、Space、ItemStore 持久化。
- 基础编辑、移动、Resize 和 Grid reflow。
- migration、备份、损坏隔离和显示器拓扑恢复。

退出：故障注入无整体数据丢失；旧 snapshot 不能覆盖新 revision。

## V0.2C — Discovery and browsing

- Start Menu 等最小可靠 Provider。
- 去重与 provenance。
- 虚拟化滚动和 Space 搜索。

退出：1000 Item 性能、重复来源和卸载失效测试通过。

## V0.3A — Daily-driver platform

- 完整多显示器/DPI。
- Explorer、睡眠、锁屏和崩溃恢复。
- 诊断导出和安全模式。
- 安装、升级和卸载验证。

## V0.3B — Optional media

只有静态桌面达到 Daily Driver 后才进入：视频 Wallpaper、Space 视频背景、播放策略、电池与 GPU 降级。

## V0.3C — Release quality gate

- 24 小时 soak。
- 性能回归和兼容矩阵。
- 签名、发布 artifact、升级/回滚。
- Production CI 扩展。

## V0.4 — Customization foundation

高级外观、更多布局、导入导出和主题候选。是否进入由 V0.3 实际使用数据决定。

## 延后且不承诺

AI 功能、插件、云同步、社区、任意命令、Widget、Shader/Web 场景和 Windows 10 正式支持。
