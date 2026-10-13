# E1-4 Appearance / Theme 纯领域契约

本模块执行 [架构规范 §9](../../../docs/ARCHITECTURE.md) 的逐属性继承和最终安全降级。没有 WinUI/Win32/WorkerW、真实 Acrylic/DWM/Composition、图片解码、持久化、动画或资源生命周期实现，不是 Windows 视觉验收证据。

## 公开模型与继承

- `RgbColor`：0–255 的不透明 sRGB。透明度单独使用 0–1 `Opacity`，不接受隐含 alpha 或颜色字符串。
- `AppearanceValues`：完整不可变描述；`Material`（Solid/SimulatedGlass）、`Tint`、`Opacity`、`CornerRadius`、`Shadow`、`Image`、`FallbackColor`。
- `AppearanceOverrides`：七个独立属性的覆盖。`ShadowSpec` 和 `ImageBackground` 各为一个原子属性；圆角与阴影长度为逻辑 DIP，±1000/1000 为工程保护范围，不是产品默认或验收结果。
- `PropertyOverride.Set(value)` 显式设置；`PropertyOverride.Inherit<T>()`、`Reset<T>()` 和 default 均表示清除当前层。可用 `with { Opacity = PropertyOverride.Reset<double>() }` 单独清除，立即恢复上层。透明度 0、圆角 0、`ShadowSpec.None`、`ImageBackground.None` 和 Solid 都是显式值。Reset 不是“回到内置默认”的屏蔽标记，不擦除其他层。
- `AppearanceLayers`：公共值、Idle、Focused；`AppearanceConfiguration`：全局主题及稳定 Space Guid → 独立层的 immutable dictionary。`WithGlobalTheme`、`WithSpace`、`ResetSpace` 返回新快照，调用者输入集合在构造时复制；旧快照及其他 Space 不改变。
- `AppearanceResolver.Resolve`：一个 Space/一个 `AppearanceState` 的 O(1) 同步解析；无 timer、逐帧循环、布局重排或图像资源创建。

每个属性严格执行：

1. `AppearanceValues.BuiltInSafe(safety.Palette)`：实体、不透明、无圆角/阴影/图片；颜色由可信安全输入提供。
2. 全局主题公共值。
3. 全局主题当前 Idle/Focused 值。
4. Space 公共覆盖。
5. Space 当前 Idle/Focused 覆盖。
6. 全部继承结束后执行最终环境、平台、资源和性能安全策略。

`AppearanceResolution.Inherited` 是前五级的配置事实；`Effective` 与 `ActualMaterial` 是领域要求的最终描述，不能将 Inherited 直接交给 renderer 绕过安全。结果含 `SpaceId`、`State`、`ContentPalette`、`RequiresOpaqueContentBacking`、`AllowMotion` 和可组合的 `AppearanceReason` 原因代码，无资源路径诊断。

## 可信环境、能力与安全限制

用户配置不能控制 `AppearanceSafetyPolicy`、`AppearanceEnvironment`、`AppearanceCapabilities`、`AppearanceBudget` 或 `ImageAssessment`。这些由受门禁约束的应用/平台 adapter 提供，不得从不可信主题导入时复制为“能力已支持”。

| 输入/限制 | 最终规则与代码 |
|---|---|
| High Contrast | 必须传入系统可读 palette；实体、opacity 1、使用 palette 背景/回退、无图片/阴影/圆角；`HighContrast` |
| Reduced Transparency | 实体、不透明、使用注入回退颜色、移除图片；`ReducedTransparency` |
| Reduced Motion | `AllowMotion=false`；保留选定 Idle/Focused 外观，不实现动画；`ReducedMotion` |
| 模拟玻璃不支持或预算禁止 | 实体、opacity 1、回退颜色；`PlatformMaterialUnsupported` / `MaterialBudgetExceeded` |
| 透明度低于可信下限 | clamp 至显式注入的正数下限；`OpacitySafetyFloor`；原配置中的零值保留 |
| 透明/模拟玻璃/图片/低对比 Tint | 内容必须使用独立不透明 backing，文字及图标不能跟随装饰层 opacity；`ContentContrastProtection` |
| 阴影不支持、禁止或 blur 超预算 | `ShadowSpec.None`；`PlatformShadowUnsupported` / `ShadowBudgetExceeded` |
| 圆角超预算 | clamp 至注入上限；`CornerRadiusBudgetExceeded`，不改变 Space bounds |
| 图片不支持、禁止或资源超限 | 去掉图片并使用不透明实体回退；`PlatformImageUnsupported` / `ImageBudgetExceeded` |
| 图片未知、缺失、无效、不支持、身份不匹配 | 同上；`ImageUnverified` / `ImageMissing` / `ImageInvalid` / `ImageUnsupported` / `ImageAssessmentMismatch` |

`ReadablePalette` 验证不透明 sRGB 相对亮度对比不低于 4.5，作为普通文本的保守领域安全下限。所有颜色、透明度下限、可用能力和预算都显式注入；没有宣称新默认色值、模糊半径、视觉风格或资源阈值已获验收。模拟玻璃只是装饰材质描述，未提供真实背景采样/模糊。

内容安全契约是 renderer 必须执行的要求，不是“计算颜色即已证明可读”。`RequiresOpaqueContentBacking=true` 时，对实际文字、图标及其可交互内容区域应用 `ContentPalette.Background` 的不透明背景和 `ContentPalette.Foreground`；装饰 Tint/Image/Opacity 不得作用到该层。false 时实体 Tint 已满足该 foreground 的域内对比下限。图标本身、文字抗锯齿、命中区域、布局裁剪与辅助技术仍须原生验证。

原因按同时适用的限制累计。平台/预算先处理，Reduced Transparency 和 High Contrast 最后强制执行，因此 Space 当前状态覆盖不能重新启用被禁止的材质、图片或透明度。配置与环境均不可变；环境恢复后重新解析即可恢复配置，无保存副作用。

## 图片引用与评估

`ImageBackground.FromReference` 只接受 WorkspaceModel 的 `ExternalResourceReference` / `ManagedAssetReference`；None 显式禁用图片。模块不读文件、不解码、不下载、不删除，也不授予 decoder/GC 权限。

`ImageAssessment` 是 adapter 对**同一引用**的观测，Available 必须带正数 encoded bytes、宽/高及 decoded bytes。解析器验证编码大小、总像素与解码内存上限，最少 RGBA8（像素 ×4）内存一致性；使用 long 乘法和除法避免 hostile dimensions 溢出。实际 codec、文件类型、malformed input、reparse/网络/取消、并发解码总预算和 TOCTOU 仍由未来安全资源 adapter 负责；不能只凭领域 Available 绕过这些检查。缺少或旧资源的 assessment 安全回退，不丢用户引用。

adapter 不得为最终已禁止的图片启动 decoder；可以先用缺少 assessment 的解析结果查看限制，再对允许候选执行受控评估并重新解析。评估与渲染须绑定资源内容 generation，引用相同不能证明文件内容未被替换。当前无图片解码/缓存/真实资产证据。

## Workspace / Layout / Interaction 集成

- `AppearanceConfiguration` 是独立 typed 候选，不挂入 `Workspace`，没有 serializer/schemaVersion；未经 Persistence ADR 不冻结 disk schema。配置中的 Guid 用 `Space.Id` 对齐。调用层负责限制活跃 Space，旧候选保留策略须另定。
- 外观修改不会调用 Workspace 编辑操作、增加 revision、改变 Item 顺序、Preview 容量或 `UserPlacement`；不接收 `SpaceLayoutPlan` 的可写引用。
- renderer 对照同一 Space ID 使用 Layout 的 bounds 与 Appearance 的 Effective；装饰不得触发布局计算、命中迁移或写回已保存几何。圆角渲染应按实际 surface 裁剪，不修改逻辑 bounds。
- A/B 的 `ActivationMode` 不是主题状态。两种模式均显式选择 Idle/Focused 样式。Hover/Intent/Focusing/Returning 的具体目标样式与插值由未来 adapter 基于控制器状态/token 决定；本模块不将过渡状态强制映射为产品已验收行为，不修改 Focus/Keyboard/IME。
- `Workspace.GetRetainedManagedAssetIds()` 当前只知道 Item 图标，不包含本候选的背景；任何后续资产回收都必须同时审计全局/Space/所有状态/旧快照/备份引用，不能把现有 inventory 当作这些背景的 GC 授权。

## 验证与后续

新增 xUnit 覆盖五级全属性与混合继承、Idle/Focused、全部属性 reset/显式零值、Space 隔离、环境恢复、组合限制、图片失效/identity mismatch/资源边界/溢出、不可变性、非法数值/颜色/枚举/null、A/B 集成及 geometry/order/capacity/Focus/Keyboard 不写回；三个固定种子各 500 组组合与重复状态切换，以及 512 种环境/能力/预算组合。既有 245 项测试文件不修改。

E2 仍需真实平台安全输入、实体/模拟玻璃的 renderer 与独立内容层、系统 High Contrast/Reduced Transparency/Reduced Motion 事件、A/B/token/命中同步及取消；通过 Windows 截图/录屏与实机可访问性验收。图片安全解码/缓存/预算/释放在资源与外观集成阶段完成；Persistence ADR 后才设计 schema/migration/备份。真实性能和 P0 门禁保持开放。领域代码与 Windows CI 不等于 Windows 视觉、解码或性能验收。
