# Zhuomian 正式开发计划书

> 文档状态：Draft v0.1  
> 项目：`Niceskys/zhuomian`  
> 目标平台：Windows 11 为主，兼容策略在技术验证后确定  
> 文档用途：产品、交互、UI、架构、性能、测试、AI 长期维护与审计的统一基线  
> 原则：任何实现不得以“做出来了”为完成标准，必须满足对应验收条件并通过审计。

---

## 0. 执行摘要

Zhuomian 不是传统桌面整理器、Dock、启动器或 Rainmeter 皮肤，而是一个**原生贴附于 Windows 桌面的空间化应用与文件管理系统**。

核心体验由四层组成：

1. **Desktop Layer**：桌面背景、图片/视频播放、前台窗口感知、全局交互状态。
2. **Space Layer**：桌面上的多个可编辑玻璃质感分类框（Space）。
3. **Item Layer**：统一承载应用、文件、文件夹、URL 等可打开对象。
4. **Interaction Layer**：Hover、Intent、居中展开、滚动、搜索、图标邻域放大、离开自动缩回。

产品最重要的差异化不是“能分类”，而是：

> **Space 平时安静地存在于桌面；只有在用户明确表现出交互意图时，才以流畅、克制、空间化的动画进入中心焦点。**

首要工程目标不是一次性做完整桌面软件，而是先证明以下四个体验是否成立：

- Space 是否能自然融入桌面；
- Hover / Intent 是否不会误触；
- Space 居中展开动画是否舒适；
- Item 图标邻域放大是否有高质量的选择反馈。

只有这四项通过体验验收，才进入大规模功能开发。

---

# 第一部分：产品定义

## 1. 产品愿景

### 1.1 一句话定义

> Zhuomian 是一个以“空间”为核心单位的现代 Windows 桌面工作层，用统一的玻璃视觉、空间动画和低干扰交互管理应用、文件、文件夹与链接。

### 1.2 设计关键词

整个项目统一采用以下六个关键词作为视觉和交互判断标准：

- **Quiet**：安静，不持续抢夺注意力；
- **Spatial**：具有明确空间位置与层级；
- **Fluid**：动画连续、自然；
- **Soft**：视觉与运动不过硬；
- **Minimal**：减少常驻控件与装饰；
- **Native**：优先使用 Windows 原生能力与行为习惯。

### 1.3 产品价值

Zhuomian 解决的不是“桌面图标太乱”这一个问题，而是同时解决：

- 软件安装后难以按个人逻辑长期分类；
- 桌面快捷方式、文件、文件夹、网址被不同系统割裂；
- 传统桌面整理器视觉陈旧、动画僵硬；
- 启动器通常需要额外呼出，与桌面空间脱节；
- 动态壁纸与桌面管理通常是两个互不感知的软件；
- 普通桌面分组在用户使用其他窗口时容易形成视觉或交互干扰。

---

## 2. 非目标（Non-goals）

在早期版本明确禁止范围膨胀。

以下内容不属于 V0.1/V0.2 核心目标：

- 内置 AI 助手；
- AI 自动分类；
- 插件商城；
- 云同步；
- 用户账户；
- 社区皮肤市场；
- 新闻、天气、日历信息流；
- 音乐播放器；
- 通用桌面 Widget 平台；
- 任意脚本执行平台；
- macOS 视觉克隆；
- Windows Shell 全替换。

AI 在早期阶段的主要角色是**参与开发、测试、审计、文档维护**，而不是作为产品卖点进入运行时。

---

## 3. 核心概念

### 3.1 Space

Space 是 Zhuomian 的核心交互与视觉单元。

一个 Space 可以表示：

- 开发；
- 学习；
- 游戏；
- AI；
- 工具；
- 临时文件；
- 某个具体项目；
- 用户自定义任意主题。

Space 具有：

- 名称；
- 可选图标；
- 位置；
- 默认尺寸；
- 展开尺寸；
- 外观；
- 背景；
- 布局模式；
- 激活模式；
- 收起模式；
- Item 集合；
- 搜索状态；
- 滚动状态；
- 动画状态。

### 3.2 Item

用户视角下，软件与文件都属于“可打开的东西”，因此 UI 和领域模型统一为 `Item`。

初始类型：

- `ApplicationItem`
- `FileItem`
- `FolderItem`
- `UrlItem`

未来预留但首版不实现：

- `ActionItem`
- `ScriptItem`
- `WidgetItem`

### 3.3 Wallpaper

Wallpaper 是 Desktop Layer 的背景来源。

首批支持：

- Windows 当前壁纸；
- 本地图片；
- 本地视频。

后续候选：

- Shader；
- Web 场景；
- 多层动态场景。

---

# 第二部分：用户体验与功能需求

## 4. 正常桌面状态

### 4.1 默认表现

Space 应直接存在于桌面背景层上，并保持低视觉侵入。

默认状态不得出现：

- 调整大小手柄；
- 删除按钮；
- 明显编辑按钮；
- 大面积高亮边框；
- 永久滚动条；
- 永久搜索输入框；
- 类传统窗口标题栏。

### 4.2 默认 Space 内容容量

未展开 Space 不需要显示其中所有 Item。

根据 Space 尺寸动态计算 `PreviewCapacity`，例如可展示 6、8、12 个 Item。

展示优先级默认建议：

1. 用户固定（Pinned）；
2. 最近使用（Recently Used）；
3. 高频使用（Frequently Used）；
4. 其余对象。

V0.1 原型可以先使用固定顺序，V0.2 再加入动态排序。

---

## 5. 全局前台窗口感知

这是 Zhuomian 与普通桌面整理器的关键交互安全规则。

### 5.1 核心规则

当存在正常前台应用窗口时：

- Space 的 Hover 自动居中展开失效；
- Hover 最多允许轻微视觉反馈；
- Space 不得抢键盘焦点；
- 视频背景默认暂停或停止；
- 用户必须先在露出的桌面区域进行明确点击，才能重新允许桌面 Hover 展开。

### 5.2 DesktopInteractionState

定义全局交互状态：

```text
Passive
Armed
Active
Suspended
```

#### Passive

存在其他正在使用的前台窗口。

行为：

- Space Hover 不展开；
- 可保留极轻微视觉反馈；
- 不抢焦点；
- 视频按策略暂停。

#### Armed

用户在其他应用仍存在的情况下，明确点击了可见桌面区域。

行为：

- Zhuomian 获得桌面交互意图；
- 允许支持 Hover 激活的 Space 进入 Intent；
- 仍不得无理由夺取键盘焦点。

#### Active

桌面本身是当前主要交互对象。

行为：

- Space 可正常 Hover / Click 激活；
- Wallpaper 可正常播放。

#### Suspended

例如：

- 锁屏；
- 全屏游戏；
- 用户会话切换；
- 系统进入需要暂停渲染的状态。

行为：

- Space 动画停止；
- 视频停止解码或释放资源；
- 不响应普通 Hover。

### 5.3 前台状态矩阵

| Windows 状态 | Zhuomian 状态 | Hover 自动展开 | 视频默认策略 |
|---|---|---:|---|
| 纯桌面 | Active | 允许 | 播放 |
| 普通窗口 | Passive | 禁止 | 暂停 |
| 普通窗口 + 用户点击可见桌面 | Armed | 允许 | 可恢复 |
| 最大化应用 | Passive | 禁止 | 停止或暂停 |
| 全屏应用/游戏 | Suspended | 禁止 | 停止并释放高成本资源 |
| 锁屏/切换会话 | Suspended | 禁止 | 停止 |

> 注：具体“窗口是否算桌面遮挡”的判断方式必须经过 Win32/Windows App SDK 技术验证，不允许在产品层直接依赖单一 API 假设。

---

## 6. Space 激活模式

每个 Space 独立配置：

```text
ActivationMode
- Hover
- Click
```

架构预留：

```text
- DoubleClick
- Hotkey
- Disabled
```

### 6.1 Hover 模式

流程：

```text
Idle
→ Hover
→ Intent
→ Focusing
→ Focused
```

### 6.2 Click 模式

Hover 只提供轻微反馈，点击后：

```text
Idle
→ Hover
→ Click
→ Focusing
→ Focused
```

### 6.3 Intent 防误触

Hover 模式不得“鼠标一碰立即飞走”。

初始建议：

- 进入 Hover 后先进行 120–180ms 微反馈；
- 持续停留约 300–450ms 后才成立 Intent；
- 参数最终由原型体验测试决定。

---

## 7. Space 状态机

正式状态建议：

```text
Idle
Hover
Intent
Focusing
Focused
Interacting
Returning
```

### 7.1 Idle

Space 融入桌面，仅展示预览 Item。

### 7.2 Hover

轻微 Scale、亮度、边缘或阴影反馈。

### 7.3 Intent

确认用户确实准备进入 Space。

### 7.4 Focusing

Space 沿空间轨迹移动到桌面中央附近并适度放大。

### 7.5 Focused

Space 进入完整内容模式：

- 更多 Item；
- 搜索；
- 滚动；
- Item hover 邻域放大；
- 点击打开。

### 7.6 Interacting

当用户正在：

- 拖动 Item；
- 右键；
- 搜索；
- 滚动；
- 进行未来编辑操作；

自动离开收起机制应暂停，防止误关闭。

### 7.7 Returning

Space 反向动画回到原位置与默认尺寸。

---

## 8. Space 居中展开

### 8.1 展开尺寸

不得默认全屏。

初始视觉目标：

- 宽度约屏幕工作区 55%–65%；
- 高度约 50%–70%；
- 具体尺寸受内容量、分辨率、DPI、多显示器工作区影响。

不得硬编码固定像素作为唯一规则。

### 8.2 运动轨迹

优先采用平滑空间位移，而不是简单线性 Tween。

候选：

- Bezier；
- Composition spring / easing；
- 轻微弧形运动。

目标是产生“从桌面空间浮出”的感受，而不是“窗口被移动”。

### 8.3 空间背景处理

Space 展开时允许：

- 桌面背景轻微降亮度；
- 局部或整体视觉层级变化；
- 必要时增加轻度 Blur；

禁止默认使用粗暴全屏黑色遮罩。

---

## 9. Space 收起

默认：

```text
CollapseMode = AutoOnLeave
```

### 9.1 默认行为

鼠标离开展开 Space 后延迟一小段时间自动缩回。

初始候选延迟：约 500–700ms，最终以体验测试为准。

### 9.2 其他关闭方式

同时支持：

- `Esc`；
- 点击有效桌面空白区域；
- 后续可增加显式 Pin / Keep Open。

### 9.3 防误收起

`Interacting` 状态不得因为鼠标瞬间越界自动关闭。

---

## 10. Item 浏览

### 10.1 默认预览

未展开 Space 只展示部分 Item。

### 10.2 展开后滚动

Focused 状态支持鼠标滚轮纵向浏览。

规则：

- Space 外框保持稳定；
- 仅内容层滚动；
- 默认隐藏传统滚动条；
- 滚动时可显示 2–3px 的低存在感位置提示；
- 滚动停止后自动淡出。

### 10.3 搜索

每个 Space 自带局部搜索。

建议交互：

- 默认只显示低存在感搜索入口；
- Focused 后直接键盘输入可自动进入搜索模式；
- 输入即时过滤当前 Space Item；
- `Esc` 先退出搜索，再根据状态决定是否收起 Space。

V0.1 可以先实现显式搜索输入；“直接键盘即搜”可在交互稳定后加入。

---

## 11. Item 选择与 macOS 式邻域放大

不直接复制 macOS Dock，而吸收其“邻域连续响应”思想。

### 11.1 Hover 只代表选择，不代表执行

状态：

```text
Normal → Hovered / Focused → Click → Launch
```

严禁 Hover 自动启动应用或文件。

### 11.2 邻域缩放

当前 Item 最大，邻近 Item 按距离连续衰减。

可使用类似高斯衰减：

```text
scale(d) = 1 + A * exp(-(d²)/(2σ²))
```

实际实现可以使用近似函数或 Composition 表达式，前提是视觉连续、性能稳定。

### 11.3 Item Hover 视觉建议

候选变化：

- Scale 约 1.20–1.35；
- Y 轻微上移；
- 标签透明度增加；
- 极轻环境阴影或高光；
- 相邻 Item 中等程度联动。

所有参数必须由交互原型统一调校，不能由各组件自行定义。

---

## 12. 文件、文件夹、应用统一行为

用户不应被迫理解内部类型差异。

统一基本动作：

```text
Click Item → Open
```

内部由 `IItemLauncher` 或同等抽象路由：

- 应用：通过安全、可追踪的 Shell/Process 启动；
- 文件：交给系统默认关联程序；
- 文件夹：交给 Explorer 或用户配置行为；
- URL：仅允许受控协议，默认 `http/https`。

---

# 第三部分：视觉设计系统

## 13. 视觉方向

不是 macOS 克隆，也不是 Windows 11 控件堆砌。

目标：

> Windows 原生空间感 + macOS 的动效克制 + 低侵入玻璃拟态。

### 13.1 禁止风格

- 2000/2010 年代桌面美化皮肤感；
- 高饱和霓虹描边滥用；
- 厚重阴影；
- 过度半透明导致可读性差；
- 所有元素都做玻璃；
- 视觉元素为了“炫”而持续运动；
- 模仿 macOS Dock/Launchpad 到失去自身产品语言。

---

## 14. Space 玻璃材质

初始设计范围而非硬编码标准：

### Idle

- Background opacity：约 0.10–0.18；
- Blur：约 12–24px 视觉目标；
- Border：1px 低透明度；
- Corner radius：约 20–28px；
- Shadow：极弱。

### Focused

- Background opacity：约 0.18–0.30；
- Blur：约 24–40px 视觉目标；
- Corner radius：约 28–36px；
- 层级更明确但不得成为厚重窗口。

> 重要：上述 Blur 数字是视觉目标，不等于 WinUI 某个 API 的参数。实际背景模糊受 Windows compositor、窗口层级与实现方式限制，必须技术验证。

---

## 15. 动画语言

统一三档：

### Micro

约 100–180ms：

- Item hover；
- 边缘亮度；
- 轻微 scale。

### Normal

约 220–320ms：

- 内容重排；
- 搜索进入；
- 小型界面出现/消失。

### Spatial

约 350–500ms：

- Space 居中；
- Space 返回。

所有时长均为初始设计区间，最终必须通过体验审计。

---

## 16. UI 设计来源与审计方法

外部参考只提取：

- 层级；
- 留白；
- 圆角；
- 玻璃密度；
- 图标反馈；
- 动画路径；
- 搜索形态；
- 空间布局。

不得直接复制第三方产品视觉资产或完整界面。

推荐参考来源：

- Dribbble：概念视觉与动效灵感；
- Behance：完整设计系统；
- Mobbin：真实商业产品交互；
- Refero：真实产品组件；
- Awwwards：空间运动与过渡动画。

对任何概念图必须额外审计：

- 是否真实可用；
- 对比度是否达标；
- 是否只适合截图；
- 动效是否会长期疲劳；
- 是否需要不可接受的 GPU 成本。

---

# 第四部分：编辑与自定义

## 17. 编辑模式

正常模式不显示编辑控件。

通过统一入口进入，例如：

- 桌面右键“编辑 Zhuomian”；
- 后续可增加全局快捷键。

### 17.1 编辑能力规划

首批：

- 移动 Space；
- 调整大小；
- 修改名称；
- 调整基础 Blur / Opacity；
- 选择背景；
- 设置 `ActivationMode`。

后续：

- 圆角；
- 色调；
- 边框；
- 阴影；
- Padding；
- Item 大小；
- 展开尺寸；
- 动画参数；
- 多布局模式。

### 17.2 必须提前保留的数据入口

即使 V0.1 UI 不暴露，也必须在模型层避免把外观写死。

建议 `SpaceAppearance`：

```text
Width
Height
Position
ExpandedWidth
ExpandedHeight
CornerRadius
Blur
Opacity
Tint
Border
Shadow
Background
Padding
IconSize
```

---

## 18. 布局模式

架构预留：

```text
Grid
List
Free
```

V0.1/V0.2 优先只实现 `Grid`。

Grid 必须支持 Reflow，不允许把每个 Item 的位置完全硬编码为绝对 X/Y。

---

# 第五部分：壁纸与媒体系统

## 19. Wallpaper Source

统一抽象：

```text
IWallpaperSource
- SystemWallpaperSource
- ImageWallpaperSource
- VideoWallpaperSource
```

后续扩展时不改动 Desktop Layer 主逻辑。

---

## 20. 视频播放策略

参考 Wallpaper Engine 的“按其他应用状态暂停或停止/释放内存”思想，但实现保持独立。

官方 Wallpaper Engine 文档明确支持：

- 特定应用运行时 Pause；
- 全屏应用时 Stop；
- `Stop (free memory)` 释放壁纸资源。

Zhuomian 初始策略：

```text
Desktop Active       → Play
Windowed App         → Pause
Maximized App        → Stop 或 Pause（最终由性能验证决定默认值）
Fullscreen/Game      → Stop + Release expensive resources
Lock/Session switch  → Stop
Battery Mode         → 可配置 Pause/Stop
```

用户后续可配置：

```text
PlaybackPolicy
- Performance
- Balanced
- Visual
```

### 20.1 Space 自定义视频背景

原则：

- Idle Space 不持续解码视频；
- 可保留最后一帧或静态封面；
- Hover 可选预热；
- Focused 时开始播放；
- 同时高成本视频解码数量必须受到全局限制。

---

# 第六部分：技术架构

## 21. 技术基线

初始首选：

- **C#**
- **WinUI 3**
- **Windows App SDK**
- **Microsoft.UI.Composition**
- 必要的 Win32 interop

选择理由：

- 微软当前将 Windows App SDK + WinUI 3 作为新 Windows 原生桌面应用推荐路线；
- Composition Visual Layer 为保留模式高性能图形、效果和动画提供基础；
- C# 有利于 AI 长期维护、测试和工程可读性；
- 可以在保持现代 UI 的同时调用必要 Win32 能力。

官方参考：

- https://learn.microsoft.com/windows/apps/windows-app-sdk/
- https://learn.microsoft.com/windows/apps/winui/winui3/
- https://learn.microsoft.com/windows/apps/develop/composition/composition-animation
- https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops

### 21.1 重要技术限制

WinUI 3 的 `AcrylicBrush` 是**应用内 Acrylic**，它模糊的是应用自身 XAML 内容，不等同于对真实桌面背景做任意区域的 HostBackdrop Blur。

因此：

- 不允许把普通 `AcrylicBrush` 直接定义为 Space 毛玻璃最终方案；
- `DesktopAcrylicBackdrop`、`SystemBackdropElement`、Composition Effects 与桌面宿主层的组合需要单独 Spike；
- 真正“贴在桌面上的多个独立玻璃区域”必须先验证可行性、性能与 Explorer 兼容性。

---

## 22. 建议解决方案结构

```text
Zhuomian.sln

src/
  Zhuomian.App/
  Zhuomian.Core/
  Zhuomian.Desktop/
  Zhuomian.Interaction/
  Zhuomian.Rendering/
  Zhuomian.Media/
  Zhuomian.Items/
  Zhuomian.Persistence/
  Zhuomian.Platform.Windows/

tests/
  Zhuomian.Core.Tests/
  Zhuomian.Interaction.Tests/
  Zhuomian.Persistence.Tests/
  Zhuomian.Platform.Tests/

spikes/
  DesktopHosting/
  BackdropBlur/
  ForegroundDetection/
  WallpaperVideo/
  MultiMonitor/

docs/
  DEVELOPMENT_PLAN.md
  PRODUCT_SPEC.md
  INTERACTION_SPEC.md
  ARCHITECTURE.md
  PERFORMANCE_BUDGET.md
  TEST_PLAN.md
  ADR/
```

> `spikes/` 用于一次性技术验证，不得默认直接复制进正式生产架构。

---

## 23. 模块职责

### Zhuomian.Core

纯领域模型，不依赖具体 UI：

- Space；
- Item；
- Appearance；
- Layout；
- Settings；
- Playback policy；
- Schema version。

### Zhuomian.Desktop

桌面语义层：

- Desktop host；
- Desktop interaction state；
- Monitor work area；
- Explorer 生命周期协调。

### Zhuomian.Interaction

- Space 状态机；
- Intent 判定；
- Hover/Click 激活；
- Auto collapse；
- Search interaction；
- Scroll interaction。

### Zhuomian.Rendering

- Space visuals；
- Glass material；
- Composition animation；
- Item magnification；
- visual transitions。

### Zhuomian.Media

- 图片；
- 视频；
- 播放策略；
- 解码资源生命周期。

### Zhuomian.Items

- Item discovery；
- Icon extraction；
- Item launching；
- Start Menu / shortcut 等来源适配。

### Zhuomian.Persistence

- JSON 或其他最终选定本地格式；
- 原子写入；
- 备份；
- Schema migration；
- 恢复。

### Zhuomian.Platform.Windows

所有 Win32/系统 API 细节集中在这里，避免平台调用散落在 UI code-behind 中。

---

## 24. 强制架构规则

### 24.1 状态与 UI 分离

禁止：

```text
SpaceState == SpaceControl
```

UI 控件只能反映状态，不能成为唯一状态来源。

### 24.2 动画控制独立

推荐抽象：

```text
ISpaceAnimationController
IItemMagnificationController
```

禁止将大量逻辑散落在 `PointerEntered` / `PointerExited` code-behind。

### 24.3 平台能力隔离

Win32 API 不得随机出现在业务项目。

所有窗口枚举、前台窗口、Shell、Explorer、桌面宿主、DPI、显示器相关调用进入 `Platform.Windows`。

### 24.4 配置必须版本化

根配置：

```json
{
  "schemaVersion": 1
}
```

所有破坏性配置变更必须：

- 写 migration；
- 写测试；
- 保留用户数据恢复路径。

### 24.5 不接受万能 Manager

禁止逐渐形成：

- `AppManager`；
- `DesktopManager`；
- `EverythingService`；
- 数千行 `MainWindow.xaml.cs`。

每个类型必须职责单一且边界可审计。

---

# 第七部分：关键技术验证（Spikes）

正式功能开发前必须完成以下验证。

## 25. Spike A：真正桌面宿主

验证：

- Zhuomian 窗口是否能稳定位于桌面图标合理层级；
- 是否能在 Explorer 重启后恢复；
- 是否影响桌面图标原始交互；
- 是否与虚拟桌面兼容；
- 是否与多显示器兼容；
- 是否会出现在 Alt+Tab；
- 是否会错误抢焦点；
- Windows 更新后风险如何。

必须比较至少两种实现路径，不允许直接锁定未公开/脆弱技巧。

输出：`docs/ADR/ADR-xxxx-desktop-hosting.md`。

---

## 26. Spike B：真实玻璃与 Blur

验证：

- `DesktopAcrylicBackdrop` 能否满足窗口级需求；
- `SystemBackdropElement` 的适用边界；
- Composition Effects 能否实现可控局部效果；
- 多 Space 的 Blur 成本；
- 视频背景下 Blur 成本；
- 低端 GPU fallback。

需要明确区分：

- 真正背景采样模糊；
- 对应用内部内容的 Blur；
- 视觉模拟玻璃。

如果真实 Blur 成本/兼容性不可接受，必须允许退化到“高质量模拟玻璃”，而不是牺牲稳定性。

---

## 27. Spike C：前台窗口与桌面 Intent

验证：

- foreground window 检测；
- 最大化/全屏判定；
- 可见桌面区域点击；
- Zhuomian 不抢焦点；
- 点击桌面后从 Passive → Armed 的可靠转换；
- 窗口移动、最小化、切换时状态同步。

---

## 28. Spike D：Space 动画

只做 3 个模拟 Space 和假 Item。

必须验证：

- Hover delay；
- Intent；
- 位移轨迹；
- Scale；
- Background transition；
- Returning；
- 中途取消；
- 连续快速切换 Space；
- 60Hz/120Hz/144Hz 感受。

此 Spike 的体验结果决定项目是否继续当前交互方向。

---

## 29. Spike E：Item 邻域放大

验证：

- 高斯/近似衰减；
- Grid 内二维邻域还是仅行内邻域；
- 放大是否造成重排抖动；
- 是否应该使用 overlay/transform 而非 layout reflow；
- 触控板、鼠标、高 DPI 下体验。

验收核心：

> 视觉上连续，Item 不发生廉价的逐格跳动或布局抖动。

---

## 30. Spike F：视频壁纸生命周期

验证：

- Play/Pause/Stop/Release；
- Stop 后重新恢复的延迟；
- 视频硬件解码；
- GPU/VRAM；
- 多显示器；
- 应用窗口覆盖状态；
- 全屏游戏；
- 锁屏；
- 睡眠/唤醒。

---

# 第八部分：数据模型

## 31. Workspace 根模型

建议概念：

```text
Workspace
- schemaVersion
- globalSettings
- wallpaper
- spaces[]
```

## 32. Space 模型

建议字段：

```text
id
name
icon
position
size
expandedSize
appearance
activationMode
collapseMode
collapseDelay
layout
background
itemIds[]
searchSettings
sortSettings
```

## 33. Item 模型

公共字段：

```text
id
kind
name
iconSource
customIcon
pinned
lastOpenedAt
openCount
```

类型扩展：

```text
ApplicationItem → executable/arguments/workingDirectory
FileItem        → path
FolderItem      → path
UrlItem         → uri
```

### 33.1 路径策略

必须考虑：

- 文件被移动；
- 软件卸载；
- 快捷方式失效；
- 驱动器盘符变化；
- 网络路径不可用。

失效 Item 不应让整个 Space 加载失败。

---

# 第九部分：应用与文件发现

## 34. App Discovery

V0.2 后考虑扫描：

- Start Menu；
- 常见 `.lnk`；
- 注册安装信息；
- Microsoft Store App；
- Steam 等平台作为独立 Provider。

必须采用 Provider 架构：

```text
IItemDiscoveryProvider
```

禁止在一个超大扫描函数里硬编码全部来源。

## 35. 去重

同一个软件可能同时来自：

- Start Menu；
- Desktop shortcut；
- registry；
- package metadata。

必须设计稳定去重键或归并策略。

不能仅依靠显示名称。

---

# 第十部分：性能预算

## 36. 性能原则

性能是产品功能，不是发布前优化项。

所有持续渲染、Blur、视频、动画都必须有生命周期。

### 36.1 初始目标（需通过基准测试校正）

非视频 Idle：

- CPU 平均目标：< 1%；
- GPU 应接近空闲；
- 不允许因为 Space 动画框架导致持续高帧渲染；
- 内存工作集目标先控制在约 150MB 以内，后续根据 WinUI/Windows App SDK 实测建立正式门槛。

动画：

- 目标 60 FPS 基线；
- 高刷新率显示器应避免人为锁死导致明显不连续；
- Frame pacing 比单纯平均 FPS 更重要。

视频：

- 优先硬件解码；
- 被其他应用覆盖时默认暂停；
- 全屏/高负载场景能够 Stop 并释放高成本资源。

> 上述数值是初始工程预算，不是未经测量的承诺。Phase 0 必须建立可重复性能基准。

---

## 37. 性能监控项

持续记录：

- cold start；
- warm start；
- idle CPU；
- idle working set；
- idle GPU；
- focus animation frame time；
- video CPU/GPU；
- VRAM；
- Explorer 重启后的恢复时间；
- 100/500/1000 Item 搜索响应；
- 多显示器资源增长。

---

# 第十一部分：可靠性与恢复

## 38. Explorer 重启

必须视为正常系统事件，而不是异常边界。

Zhuomian 应：

- 检测桌面宿主失效；
- 重建绑定；
- 恢复 Space；
- 不丢配置；
- 不留下幽灵窗口。

## 39. 崩溃恢复

配置写入：

- 原子写；
- 临时文件；
- 成功后替换；
- 保留上一个可恢复版本。

不得在拖动 Space 时每个像素都同步写磁盘。

## 40. 无效 Item

应用卸载、文件删除时：

- 标记失效；
- 提供修复/移除；
- 不阻塞 Space；
- 不产生无限错误弹窗。

---

# 第十二部分：安全与隐私

## 41. 默认隐私

首版默认：

- 不上传用户应用列表；
- 不上传文件名；
- 不上传路径；
- 不上传壁纸；
- 不需要账户。

若未来加入遥测，必须显式文档化采集字段和用途。

## 42. Item 启动安全

- URL 默认限制到安全协议白名单；
- 不允许配置文件静默执行任意 PowerShell/CMD 作为首版能力；
- 处理参数时避免命令拼接；
- 日志不得泄露不必要的私人绝对路径；
- 外部文件图标/缩略图解析要考虑恶意文件与异常输入。

## 43. 更新机制

后续若加入自动更新：

- 必须签名；
- 校验来源；
- 不静默执行未知下载；
- 更新失败可恢复。

---

# 第十三部分：可访问性与输入

## 44. 不允许只依赖 Hover

虽然 Hover 是特色交互，但核心功能必须存在非 Hover 路径：

- 点击激活；
- 键盘访问；
- `Esc` 返回；
- 可见焦点状态。

## 45. Reduced Motion

应尊重系统或产品级减少动画选项。

Reduced Motion 下：

- 缩短/去除弧线运动；
- 降低 Scale；
- 保留状态变化但减少大范围运动。

## 46. 高对比度与可读性

玻璃效果不能凌驾于可读性。

需要测试：

- 浅色壁纸；
- 深色壁纸；
- 高细节壁纸；
- 视频快速变化背景；
- Windows 高对比度设置。

---

# 第十四部分：测试策略

## 47. 单元测试

必须覆盖：

- Space 状态机；
- DesktopInteractionState；
- Intent 判定；
- Collapse 逻辑；
- 排序；
- 搜索；
- Schema migration；
- 无效 Item；
- Playback policy。

## 48. 集成测试

覆盖：

- Item launch；
- App discovery provider；
- Persistence；
- Explorer 生命周期；
- Foreground detection；
- 多显示器坐标映射。

## 49. UI/交互测试

重点不是截图相同，而是状态正确：

- Hover 不误展开；
- 前台窗口存在时禁止自动展开；
- 点击露出桌面后恢复；
- Focus 后滚轮有效；
- Interacting 不误收起；
- Escape 行为一致；
- 两个 Space 快速切换不会卡死。

## 50. 性能测试

建立固定测试场景：

- 3 Space / 30 Item；
- 8 Space / 200 Item；
- 20 Space / 1000 Item；
- 静态壁纸；
- 1080p 视频；
- 4K 视频；
- 单显示器；
- 双显示器；
- 60Hz 与高刷新率。

## 51. 兼容性矩阵

至少覆盖：

- Windows 11 当前受支持版本；
- 100% / 125% / 150% / 200% DPI；
- 多显示器不同 DPI；
- 横屏/竖屏；
- Explorer 重启；
- 睡眠/唤醒；
- 锁屏；
- 虚拟桌面；
- 游戏全屏；
- UAC 安全桌面切换后的恢复。

Windows 10 是否进入正式支持范围，在 Desktop/Backdrop Spike 后再决定。

---

# 第十五部分：日志、诊断与可维护性

## 52. 结构化日志

建议日志类别：

```text
DesktopHost
Interaction
Rendering
Media
Persistence
Discovery
Launch
Performance
```

日志必须支持级别：

- Debug；
- Info；
- Warning；
- Error。

## 53. 诊断模式

开发版预留诊断 overlay：

- 当前 DesktopInteractionState；
- Space state；
- FPS；
- frame time；
- CPU/GPU 粗略指标；
- 当前 foreground classification；
- video playback state。

Release 默认关闭。

---

# 第十六部分：AI 长期开发治理

## 54. AI 的角色

AI 可以：

- 写实现；
- 写测试；
- 写文档；
- 分析日志；
- 审查 PR；
- 做重构建议；
- 生成迁移；
- 建立 Spike。

AI 不得自行：

- 扩大产品范围；
- 更换核心框架；
- 删除测试以“修复 CI”；
- 绕过架构边界；
- 修改配置 schema 而不写 migration；
- 将 Spike 代码未经审查直接并入生产路径；
- 因实现困难而悄悄改变交互需求。

## 55. PR 粒度

原则：

> 一个 PR 解决一个可描述、可测试、可回滚的问题。

禁止大型“AI 一次性重写整个项目”PR。

推荐每个 PR 描述：

- Problem；
- Scope；
- Out of scope；
- Design；
- Tests；
- Performance impact；
- Risks；
- Screenshots/video（UI 改动）；
- Rollback。

## 56. ADR

以下内容改变时必须新增 Architecture Decision Record：

- Desktop hosting；
- UI framework；
- rendering/backend；
- persistence；
- update system；
- application discovery model；
- plugin model；
- background media pipeline。

不得通过“顺手重构”偷偷改变架构。

## 57. AI 防漂移规则

每次大型任务前 AI 必须先读取：

- `DEVELOPMENT_PLAN.md`
- `PRODUCT_SPEC.md`
- `INTERACTION_SPEC.md`
- `ARCHITECTURE.md`
- 相关 ADR

如果任务与规范冲突：

1. 停止实现；
2. 指出冲突；
3. 先修改规范或 ADR；
4. 再实现。

---

# 第十七部分：Git 与分支治理

## 58. 分支模型

建议：

- `main`：始终可构建；
- `feature/...`：功能；
- `fix/...`：修复；
- `spike/...`：技术验证；
- `docs/...`：规范。

## 59. Commit

推荐 Conventional Commit 风格：

```text
feat:
fix:
docs:
refactor:
test:
perf:
build:
ci:
```

## 60. 合并标准

进入 main 前至少满足：

- build 通过；
- 自动测试通过；
- 无新增已知高严重度警告；
- UI 变化有视觉证据；
- 状态机变化有测试；
- 性能敏感改动给出前后数据；
- 文档与代码一致。

---

# 第十八部分：版本路线

## 61. Phase 0 — Specification & Feasibility

目标：证明底层路线可行，而不是堆业务功能。

交付：

- DEVELOPMENT_PLAN；
- PRODUCT_SPEC；
- INTERACTION_SPEC；
- ARCHITECTURE；
- DesktopHosting Spike；
- BackdropBlur Spike；
- ForegroundDetection Spike；
- Animation Spike；
- ItemMagnification Spike；
- VideoLifecycle Spike；
- 初版性能基线。

退出条件：

- 桌面宿主路径可接受；
- 不抢焦点；
- Hover gating 可实现；
- 动画流畅；
- Blur 有明确 production/fallback 路线；
- 视频能够按应用状态暂停/停止；
- 没有发现需要推翻产品核心交互的阻断问题。

---

## 62. V0.1 — Interaction Prototype

目标：验证“Zhuomian 为什么值得存在”。

范围：

- 3 个 Space；
- 假 Item 或固定 Item；
- Idle/Hover/Intent/Focusing/Focused/Returning；
- 居中展开；
- Auto collapse；
- Item 邻域放大；
- 前台窗口存在时禁用 Hover 自动展开；
- 点击桌面重新 Armed；
- 基础图片背景。

不要求：

- 完整应用扫描；
- 完整编辑器；
- 视频背景；
- 多种布局；
- 商业发布。

退出条件：

- 日常反复 Hover 不产生明显误触；
- Space 动画主观体验通过审计；
- 没有焦点抢夺；
- 60FPS 基线场景稳定；
- 快速重复操作不产生状态错乱。

---

## 63. V0.2 — Usable Desktop

目标：可以真实管理自己的日常桌面。

范围：

- 真 Application/File/Folder/URL Item；
- 点击打开；
- Space 持久化；
- 基础编辑模式；
- 移动/Resize；
- Grid reflow；
- 展开后滚动；
- Space 搜索；
- 图片背景；
- 基础 App Discovery；
- 失效 Item 处理；
- 配置 migration 框架。

退出条件：

- 能连续使用而不依赖手改配置；
- 配置异常不会整体丢失；
- 主要状态有自动测试；
- Explorer 重启可恢复。

---

## 64. V0.3 — Daily Driver

目标：达到长期常驻使用标准。

范围：

- 视频 Wallpaper；
- Wallpaper playback policy；
- Space 图片/视频背景；
- 多显示器；
- 更完整应用扫描；
- 高 DPI；
- 崩溃恢复；
- 性能优化；
- 完整日志与诊断；
- CI；
- 安装/升级路径。

退出条件：

- 稳定性、性能、兼容性达到明确门槛；
- 无已知数据损坏问题；
- 全屏游戏/普通应用不会被 Zhuomian 明显干扰；
- 视频资源生命周期符合性能策略。

---

## 65. V0.4 — Customization Foundation

候选：

- 高级 Space 外观；
- 更多布局模式；
- 自定义主题；
- 动画参数；
- Item 自定义封面；
- 用户导入/导出配置。

是否进入该版本，必须以 V0.3 实际使用反馈为依据。

---

# 第十九部分：风险登记表

## 66. R1 — Explorer/Desktop Hosting 依赖脆弱

严重度：高。

风险：某些桌面嵌入方法依赖未文档化 Shell 行为，Windows 更新后可能失效。

缓解：

- Phase 0 比较多个方案；
- 封装 `IDesktopHost`；
- 建立 fallback hosting mode；
- Explorer restart 自动恢复；
- 不让业务层依赖具体 HWND 层级。

放弃触发：

> 如果唯一可行路径需要高度不稳定的 Shell hack 且没有合理 fallback，则重新评估“真正嵌入桌面”的产品约束。

---

## 67. R2 — Blur 性能或能力不足

严重度：高。

风险：理想玻璃效果无法在多个 Space + 视频背景下稳定实现。

缓解：

- Backdrop Spike；
- 静态近似；
- 自动质量等级；
- Focused 才提高 Blur；
- 低端设备 fallback。

放弃触发：

> Blur 明显损害帧率、输入延迟或稳定性时，优先牺牲“真实模糊”，不牺牲核心交互。

---

## 68. R3 — Hover 动画变成干扰

严重度：高。

风险：视觉漂亮但日常使用烦躁。

缓解：

- Intent delay；
- 前台窗口 gating；
- 每 Space 可改 Click；
- Reduce Motion；
- 可调 delay；
- 长期实用测试。

放弃触发：

> 若用户在日常使用中频繁误触，默认 ActivationMode 应从 Hover 改为 Click，Hover 变为可选增强。

---

## 69. R4 — AI 长期维护导致架构漂移

严重度：高。

缓解：

- 规范先行；
- ADR；
- 小 PR；
- 测试；
- 模块边界；
- 审计窗口并行检查；
- 禁止无理由重写。

---

## 70. R5 — 功能膨胀

严重度：中高。

典型诱因：

- 天气；
- AI；
- 音乐；
- 插件；
- 社区；
- 手机联动。

规则：

> 任何新功能先回答“是否强化 Space 核心体验”。若否，默认延后。

---

## 71. R6 — 视频背景吞噬资源

严重度：中高。

缓解：

- 覆盖即 Pause；
- 全屏 Stop；
- 解码器生命周期；
- 同时活跃视频上限；
- 电池策略；
- GPU 监控。

---

# 第二十部分：正式审计分组

本计划书建议拆成以下独立审计窗口。每个窗口只审自己的主题，最后再做交叉审计。

## Audit A — 产品边界

检查：

- 产品定义是否清晰；
- V0.1 是否仍过大；
- Non-goals 是否充分；
- 是否存在“为了好看但日常不可用”的需求。

## Audit B — 交互状态机

检查：

- Passive/Armed/Active/Suspended 是否完备；
- Idle→Returning 是否存在竞态；
- Hover 与 Click 模式是否冲突；
- Interacting 是否覆盖所有误关闭情况；
- Esc、桌面点击、窗口切换优先级。

## Audit C — Windows 桌面集成

检查：

- Desktop Host 是否技术可行；
- Explorer restart；
- 虚拟桌面；
- Z-order；
- focus；
- multi-monitor；
- DPI；
- fallback。

## Audit D — UI / Glass / Motion

检查：

- 是否现代；
- 是否存在廉价 glassmorphism；
- Blur 是否过度；
- Item 放大是否产生 layout shift；
- Motion 是否会长期疲劳；
- reduced motion。

## Audit E — 性能

检查：

- Idle 是否真的低占用；
- Composition 生命周期；
- 视频解码；
- Blur；
- 资源释放；
- frame pacing；
- 多显示器增长。

## Audit F — 数据模型与持久化

检查：

- Space/Item 是否可扩展；
- Schema migration；
- 路径失效；
- 原子写；
- 恢复；
- 配置兼容。

## Audit G — 安全与隐私

检查：

- Item launch；
- URL protocol；
- 参数注入；
- 日志隐私；
- 恶意文件；
- 自动更新。

## Audit H — 测试与可靠性

检查：

- 状态机测试；
- Explorer 生命周期；
- 兼容矩阵；
- 性能基准；
- crash recovery；
- CI 门槛。

## Audit I — AI 开发治理

检查：

- PR 粒度；
- ADR；
- AI 是否容易绕过规范；
- 测试是否能阻止回归；
- 文档与实现同步机制。

## Audit J — Roadmap

检查：

- Phase 0 是否足以降低技术风险；
- V0.1 是否只验证差异化体验；
- V0.2/V0.3 分界是否合理；
- 是否存在提前优化或提前泛化。

---

# 第二十一部分：关键开放问题

以下问题在对应 Spike/审计完成前不得伪装成已经决定：

1. Desktop hosting 最稳定的实现是什么？
2. 是否正式支持 Windows 10？
3. Space 的真实背景 Blur 能做到什么程度？
4. 是否需要单一全屏透明窗口，还是每 Space/monitor 独立宿主？
5. 前台窗口“有页面打开”的精确定义是什么？
6. 点击露出桌面区域后，Armed 状态何时自动失效？
7. Space 在多显示器之间移动时展开中心属于哪块屏幕？
8. Item 邻域放大采用二维还是行/列局部算法？
9. 默认 Hover activation 是否最终适合大多数用户？
10. 视频 Stop 后是否需要立即释放 decoder/VRAM？
11. App Discovery 的最小可靠来源集合是什么？
12. 配置存储 JSON 是否足够，何时才需要数据库？
13. 安装方式采用 packaged 还是 unpackaged/self-contained？
14. 是否需要系统托盘；若需要，其最小职责是什么？
15. 是否需要安全模式关闭视频、Blur 与动画以便故障恢复？

---

# 第二十二部分：Definition of Done

任何 Feature 只有同时满足以下条件才算完成：

1. 对应需求有明确验收标准；
2. 实现符合模块边界；
3. 错误路径被处理；
4. 测试覆盖关键逻辑；
5. UI 变化通过视觉审计；
6. 性能敏感功能有数据；
7. 文档同步；
8. 不引入未记录架构决策；
9. 不破坏 Reduced Motion / 基础键盘路径；
10. PR 可独立回滚。

---

# 第二十三部分：下一步执行顺序

正式开发顺序固定为：

```text
1. 审计本计划书
2. 根据审计结果修订 DEVELOPMENT_PLAN
3. 拆 PRODUCT_SPEC
4. 拆 INTERACTION_SPEC
5. 拆 ARCHITECTURE
6. 建立 ADR 模板
7. 建立 Spike 项目
8. 先验证 Desktop Hosting / Blur / Foreground State
9. 再制作 Interaction Prototype
10. 交互通过后进入 V0.2 业务能力
```

不建议在第 8 步之前开始：

- 应用扫描大系统；
- 完整设置页；
- 自动更新；
- 大量主题；
- 插件；
- AI 功能。

---

# 附录 A：当前核心验收语句

任何开发者或 AI 都应能用以下语句判断 Zhuomian 是否仍在正确方向：

> 当用户只看桌面时，Space 应像壁纸的一部分一样安静存在；当用户明确靠近一个允许 Hover 激活的 Space 时，它先给予微反馈，在确认 Intent 后平滑进入屏幕中心并适度放大；进入后，用户可通过滚轮和搜索访问全部应用与文件，Item 对鼠标产生连续邻域放大；鼠标离开后 Space 自动回到原位。若用户正在使用其他应用，则 Hover 自动展开默认失效，只有用户明确点击露出的桌面区域后才重新获得桌面交互权。视频壁纸在其他应用占用桌面时默认暂停，在全屏或高负载场景能够停止并释放资源。

如果某个实现让这段行为变得更难、更脆弱、更耗资源或更容易误触，则它必须被质疑，而不是因为“功能更多”而接受。

---

# 附录 B：外部技术依据

本计划当前参考的公开技术资料：

- Microsoft — Windows App SDK：<https://learn.microsoft.com/windows/apps/windows-app-sdk/>
- Microsoft — WinUI 3：<https://learn.microsoft.com/windows/apps/winui/winui3/>
- Microsoft — Composition animations：<https://learn.microsoft.com/windows/apps/develop/composition/composition-animation>
- Microsoft — System backdrops / Mica / Acrylic：<https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops>
- Microsoft — In-app Acrylic：<https://learn.microsoft.com/windows/apps/develop/ui/in-app-acrylic>
- Wallpaper Engine — Application Rules：<https://help.wallpaperengine.io/en/functionality/applicationrules.html>
- Wallpaper Engine — Game / performance behavior：<https://help.wallpaperengine.io/en/performance/game.html>

技术文档只用于确认平台能力与限制；Zhuomian 的产品交互与实现不会复制第三方产品代码或受版权保护的视觉资产。
