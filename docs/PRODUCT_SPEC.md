# Zhuomian 产品规范

> 状态：Post-audit baseline v0.2

## 1. 产品定义

Zhuomian 是面向 Windows 的空间化桌面工作层。用户用多个 `Space` 按个人逻辑组织 `Item`，并从桌面快速打开它们。

### Primary user

需要长期管理大量应用、文件、文件夹和链接，同时重视桌面秩序、低干扰和现代视觉的 Windows 用户。

### Primary job

在不进入另一个复杂管理工具的前提下，从桌面按个人分类快速找到并打开常用对象。

### Secondary jobs

- 提供安静、现代、可调的桌面视觉。
- 在资源允许时提供图片或视频背景。
- 通过可选 Hover 和空间动画改善浏览体验。

## 2. 核心对象

`Space` 是可命名、可定位、可调整尺寸的分类容器。每个 Space 独立设置 `ActivationMode`，初期只支持 `Hover` 与 `Click`。

初期正式 Item 类型为 `ApplicationItem`、`FileItem`、`FolderItem` 和 `UrlItem`。`CommandItem`、`ScriptItem`、Widget 和插件不属于 V0.1/V0.2。

Wallpaper 是辅助背景能力。静态图片优先；视频必须服从前台状态和资源生命周期。

## 3. 核心用户行为

### Preview direct launch

未展开 Space 只显示 `PreviewCapacity` 范围内的 Item。用户单击其中可见 Item，应立即打开该 Item；不得先强制展开 Space 再点击一次。点击或 Hover Space 的空白区域才用于进入完整浏览。

### Focused browsing

Focused Space 提供虚拟化浏览、内容层纵向滚动、Space 内搜索、Item 邻域反馈和点击打开。Space 外框在内容滚动时保持稳定。

### Foreground safety

当外部应用处于前台，Hover 只能显示轻微反馈，不能自动展开，也不得抢键盘焦点。用户点击露出的桌面区域后，才可短暂开启 Hover 意图门。全屏、锁屏或会话切换时进入 Suspended 行为。

### Collapse

默认离开自动收起，延时为 provisional 参数。搜索、拖动、上下文菜单等交互期间不得误收起。`Esc` 优先级由交互规范定义。

## 4. 预览排序

最终目标顺序为 Pinned、Recently used、Frequently used、Remaining。V0.1 使用固定顺序，避免过早引入热数据写入和排序复杂度。

## 5. 材质和动效规则

- Idle Space 默认使用高质量模拟玻璃或实体材质。
- 真实 Acrylic/SystemBackdrop 只允许作为 Focused 增强，且必须可降级。
- 同时至多一个高成本 Focused surface。
- Item 放大只改变视觉 transform，不能造成 Grid reflow。
- Reduced Motion 使用淡入、轻度缩放或即时状态替代大范围移动。
- 可读性优先于透明度、模糊和视觉一致性。

所有数值参数均为 provisional，必须由 Spike 和可用性测试校准。

## 6. V0.1 范围

- 三个固定 Space 和固定假 Item。
- Click 与可选 Hover 激活。
- Preview direct launch 的模拟行为。
- 展开、返回和离开收起。
- 外部前台存在时的 Hover gating。
- 显式搜索入口的交互原型。
- Item transform 邻域反馈。
- 静态图片或纯色背景。

V0.1 不包含真实应用扫描、通用持久化编辑器、视频壁纸、主题市场、任意命令、自动更新或正式发布。

## 7. Non-goals

- Windows Shell 全替换或 macOS 克隆。
- AI 助手、AI 自动分类或通用脚本平台。
- 云同步、账户、社区市场或信息面板。
- 以 WorkerW 或真实 Blur 为产品成立条件。

## 8. V0.1 量化验收

以下阈值在 Phase 0 校准后冻结；冻结前为 provisional：

- 100 次外部前台路径测试中，Hover 自动展开次数为 0。
- 100 次 Preview Item 点击中，要求二次点击的次数为 0。
- 自动化组合状态测试中，不允许出现两个 Focused Space。
- 快速切换 500 次后，无卡死、幽灵窗口或无法返回状态。
- 键盘输入仍归属外部应用，除非用户显式进入 Zhuomian 键盘模式。
- Reduced Motion、键盘路径和 Click-only 模式均可完成核心任务。
- 性能满足 [PERFORMANCE_BUDGET.md](PERFORMANCE_BUDGET.md) 的当期门槛。
