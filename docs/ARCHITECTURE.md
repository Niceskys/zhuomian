# Zhuomian 架构规范

> 状态：Phase 0 candidate。未经 Spike 与 ADR 接受的内容均不是生产承诺。

## 1. 架构原则

- 业务层不依赖 WorkerW、具体 HWND 层级或单一前台检测 API。
- 平台、渲染、交互、数据和启动安全域分离。
- 每个高风险能力必须有降级路径和可观测状态。
- Spike 是一次性证据，不得未经审查复制进正式模块。
- 默认 per-monitor host，避免单个跨屏窗口承担混合 DPI。

## 2. 候选模块

```text
src/
  Zhuomian.App
  Zhuomian.Core
  Zhuomian.Interaction
  Zhuomian.Desktop
  Zhuomian.Rendering
  Zhuomian.Items
  Zhuomian.Persistence
  Zhuomian.Media
  Zhuomian.Platform.Windows

tests/
  Zhuomian.Core.Tests
  Zhuomian.Interaction.Tests
  Zhuomian.Persistence.Tests
  Zhuomian.Platform.Tests

spikes/
  DesktopHosting
  ForegroundClassification
  BackdropMaterial
  FocusAndInput
  MultiMonitorDpi
  PackagingDeployment
  MediaLifecycle
```

## 3. Desktop Host

```text
IDesktopHost
  EnhancedDesktopHost   // WorkerW/Shell 私有层级，仅可选增强
  FallbackDesktopHost   // 公开窗口能力下的正式降级模式
```

业务层只接收规范化事件：HostAvailable、HostLost、MonitorChanged、WorkAreaChanged、ExplorerRestarted。不得读取具体 WorkerW 或 Progman 句柄。

### 不变量

- 普通应用始终可遮盖 Zhuomian。
- Zhuomian 不进入 TopMost。
- Host 丢失后停止输入、动画和媒体。
- Explorer 重启后在 provisional 恢复预算内重建，失败时进入可诊断降级模式。
- 每台显示器独立 host、DPI context 与 work-area mapping。

EnhancedDesktopHost 只有在多版本 Windows 实测通过、Explorer 恢复可靠且 fallback 可用时才能被接受。

## 4. 焦点和窗口模式

视觉宿主默认 NoActivate。需要文本输入时，进入显式 Keyboard Interaction Mode，由独立可聚焦 surface 或经验证的窗口模式承载。退出后恢复 NoActivate。

不得尝试用 Hover、透明命中窗口或全局键盘钩子绕过显式激活。全局快捷键若引入，必须有独立安全与可访问性审查。

## 5. 坐标模型

持久化位置使用 monitor stable key、工作区归一化坐标和 logical size，不保存原始跨屏物理像素作为唯一事实。

恢复顺序：

1. 匹配原显示器稳定标识。
2. 映射到当前 work area 与 DPI。
3. Clamp 到可见区域。
4. 原显示器缺失时迁移到 primary，并记录可恢复诊断。

虚拟桌面语义在 Phase 0 决定；决定前默认只承诺当前 Windows 会话中的一致显示，不承诺每虚拟桌面独立布局。

## 6. 数据模型

采用根级 `ItemStore`，Space 通过稳定 ID 引用 Item。理由是允许同一 Item 被多个 Space 引用、集中去重、独立标记失效并保持迁移可控。

```text
Workspace
  schemaVersion
  revision
  globalSettings
  wallpaper
  spaces[]
  itemsById{}

Space
  id
  name
  itemIds[]
  placement
  appearance
  activationPolicy
  layoutPolicy
```

删除 Space 默认不删除仍被其他 Space 引用的 Item。删除 Item 必须从所有 Space 引用中解绑。加载时隔离 dangling reference 并记录诊断，不阻断整个 Workspace。

### 持久态与运行态

允许持久化：用户配置、Space 布局、Item identity、策略和 schema/revision。

禁止持久化：Hover、Focused、动画进度、Armed、KeyboardMode、临时搜索文本、指针捕获和媒体 decoder 状态。

## 7. 保存与迁移

- 每次内存快照携带单调 `revision`。
- 保存使用 temp → flush → atomic replace，并保留 last-known-good。
- 写入完成前比较 revision，旧快照不得覆盖新状态。
- migration 只能逐版本执行，并具有输入/输出 fixture、幂等性测试和失败回滚。
- 未知 Item kind 保留原始载荷并以 unsupported placeholder 展示，不得静默丢字段。
- 单对象损坏优先隔离；根结构或校验失败时回退 last-known-good。
- `openCount` 等热数据使用独立批量 journal/telemetry store，不触发主配置频繁保存。

## 8. Item identity 与资源

Application identity 优先来自可靠 provider identity，文件/文件夹保留路径及可选稳定文件标识。路径失效是可恢复状态，不是反序列化失败。

用户复制进 Zhuomian 的图标、图片和视频由 managed asset store 所有；外部引用保持 external 状态，不因清理缓存删除用户源文件。所有资源引用必须定义创建、替换和垃圾回收语义。

## 9. 材质与媒体

材质等级：

1. Solid/accessibility
2. Simulated glass（Idle 默认）
3. Enhanced backdrop（Focused 可选）

材质策略同时考虑 Tint、Luminosity、Noise、对比保护和 fallback color。Enhanced 成本超预算或平台能力不足时自动降级。

媒体状态机固定为 `Play → Pause → Stop → Release`，逆向恢复必须重新建立所需资源。Phase 0 只验证资源生命周期，不交付完整动态壁纸产品。

## 10. Packaging

Phase 0 必须比较：

- packaged 与 unpackaged；
- framework-dependent 与 self-contained；
- Desktop Host、启动、恢复、更新、日志路径和卸载残留的影响。

结论以 ADR 记录。Packaging 不是 V0.3 才处理的末端问题。
