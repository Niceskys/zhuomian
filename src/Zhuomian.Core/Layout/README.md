# E1-3 Space 纯领域布局

实现 [产品布局规则](../../../docs/PRODUCT_SPEC.md)、[A/B 和 Grid 契约](../../../docs/INTERACTION_SPEC.md)、[坐标与持久态边界](../../../docs/ARCHITECTURE.md)。没有 Windows API、窗口、渲染、动画或磁盘操作；不修改 WorkspaceModel schema，不关闭任何 P0 门禁。

## 公共计算接口

| 接口 | 输入 / 输出 |
|---|---|
| `SpaceLayoutEngine.Calculate` | Space、用户 logical bounds、当前所属 MonitorWorkArea、ExpansionConstraints、Expand/Return、可选当前视觉 bounds → SpaceLayoutPlan |
| `SpaceLayoutEngine.Fit` | 矩形和工作区 → 安全缩小/Clamp 的矩形 |
| `PlacementRecovery.CaptureUserEdit` | 已明确接受的编辑矩形及工作区 → UserPlacement 候选契约 |
| `PlacementRecovery.Resolve` | UserPlacement、匹配 monitor 或 null、primary → PlacementResolution 和 fallback 标记 |
| `IconGridLayout.Calculate` | Space bounds、Item 数量、GridParameters → O(1) 网格指标 |
| `GetCell / GetHoverTransform` | Item index / hover scale → 内容局部坐标 cell / 纯 transform 参数 |
| `ResolvePreviewCapacity` | PreviewCapacityPolicy → 当前可显示容量 |
| `SpaceContentLayout.Calculate / GetPreviewItemId` | Workspace、Space ID、bounds、参数 → 有序可解析 ID、网格、预览容量 / 稳定 ID |

几何类型、参数和结果使用 get-only 属性；布局引擎不持有可变状态。计算不能更改用户位置、尺寸、顺序、容量配置、Workspace revision 或控制器状态。

## A/B 和中断

- `UserBounds` 始终保留传入用户矩形；`IdleBounds` 是当前工作区内的安全原位。普通有效位置两者相等；恢复/工作区变小时只产生安全运行结果，不能自动写回用户布局。
- 展开尺寸为用户尺寸乘注入 scale，各轴限制于注入 minimum/maximum，然后统一缩小到工作区。工作区不足时优先可见性，minimum 不是越界许可。极薄结果以工程最小 extent 作安全约束，极端情况下不保证原纵横比。
- A 优先围绕安全原位中心展开，靠边则向可用方向 Clamp；不寻找或迁移到其他显示器。B 围绕调用方提供的当前所属显示器工作区中心展开，不使用虚拟桌面中心。
- Return 的目标是安全原位；无 currentVisualBounds 时默认从展开矩形返回。提供 currentVisualBounds 时 Expand/Return 均逐值原样使用该起点，即使起点暂时在工作区外。没有动画进度、时长、插值、完成事件或 B0/B1/B2 能力声明。
- adapter 必须按控制器 Focusing/Returning 及 TransitionToken 调用，取消旧轨迹并从当前值计算新轨迹，完成时回传同一 token。安全 Idle/HostLost 直接清理 adapter。布局计算本身不授予 Focus、键盘或启动权限。
- A 有效区域应覆盖原框、目标框和必要过渡路径；精确形状、安全余量、移动命中、裁剪与离开判定由后续原生 adapter 实测。本引擎没有定义整屏有效区域。

## Grid、容量和虚拟化

Padding 是 Space bounds 内的内容 inset；标题/搜索等占用可通过 top 等 inset 注入。超大 padding 产生零内容区域。图标不为了挤进区域自动缩小。

列数/可见行数按 `floor((可用 extent + gap) / (icon extent + gap))` 计算，并再次严格检查整 cell 是否可容纳。不足一个图标宽度时 Columns=0，不能访问 cell；不足一行高度时 Preview=0，已有列仍可描述滚动内容。零 Item 的 Columns/TotalRows/Preview 均为 0。

`GetCell(index)` 为 row-major 内容局部、未滚动坐标，保持固定 index 顺序。ContentArea 是 adapter 坐标系里的内容 viewport；完整 cell 可位于 viewport 下方，这是滚动内容，不是 Space 外框越界。adapter 只创建可见元素、减去滚动偏移并裁剪到 viewport，不可把 1000 个 cell 全部实例化为视觉元素。完整内容高度超过工程范围时拒绝计算，不静默丢 Item。

单 Item Hover 只输出 scale 和 cell 中心 pivot；不调用 Calculate、不更改 Columns/TotalRows/cell。视觉变换后的重叠命中、裁剪和 Reduced Motion 由 adapter 处理。Resize 则显式重新 Calculate。

自动容量取 Item 数量、完整可见 slots、注入 AutomaticPreviewLimit 和 MaximumPreviewCapacity 的最小值。显式覆盖替代 AutomaticPreviewLimit（包括 0），仍受 Item 数量、几何 slots 和 MaximumPreviewCapacity 约束；int.MaxValue 等过大覆盖安全 Clamp，源 policy 不变。null/Automatic 重置恢复自动计算。没有隐式 Resize、排序或持久化写入。

Workspace 投影使用 `GetUsableItemIds`，保留用户相对顺序，结构损坏引用留在原快照及诊断中。Missing/Unsupported 的可解析 Item 仍可显示失效入口；投影不是 launch 授权。解析 ID 视图需要 O(n) 的既有 Workspace 操作；纯 Grid 指标和 cell 是 O(1)，没有批量生成全部矩形的 API。

## 恢复、DPI 和安全输入

- 所有几何输入均为逻辑 DIP；PixelDensity 为 pixels/DIP 元数据，不参与布局倍率。`FromPixelSize` 只转换尺寸，不能拿它直接转换跨屏绝对物理位置。真实 monitor 原点、任务栏工作区、stable key 和混合 DPI 映射必须由未来平台 adapter 提供。
- UserPlacement 仅为独立 typed 候选：monitor ID、工作区完整 extent 中归一化的 top-left（0–1）、逻辑尺寸。只能用已接受编辑调用 CaptureUserEdit；不能把动画帧当成编辑提交。它没有挂到 Workspace schema，没有 serializer 或磁盘格式冻结。
- Resolve 使用调用方匹配 monitor，身份不符则拒绝；缺失时使用明确提供的 primary 并报告 UsedPrimaryFallback。映射归一化位置、保留逻辑尺寸，必要时安全缩小/Clamp；不修改 placement。DPI 元数据变化不重复缩放 logical size。
- NaN/Infinity、负尺寸/spacing、零 Space/工作区、非法枚举/index、null、不匹配 monitor 和倒置约束拒绝。错误文本不包含用户路径/身份值。
- 工程数值 envelope 为坐标/extent 绝对上限 1e9 DIP、有效 Space/工作区/尺寸 extent 下限 1e-6 DIP、scale 1–1000、density 0.1–16、monitor token 最长 256 字符、Item count 上限 1,000,000。endpoint 也检查范围；容量乘积使用 long。这些是有限算术/资源保护，均不是用户已确定的产品默认或 UI 可选范围。
- 注入参数没有默认尺寸、最小尺寸、图标间距、padding、放大倍率或自动容量数值；测试数值仅是 fixture。后续可依据原生证据调整参数及工程范围，不能把本算法当成最终产品容量策略已验收。

## 验证和后续

Layout 测试覆盖 A 原位/四角 Clamp/缩小、B 所属屏幕中心/返回、当前视觉起点/旧 token、Resize、Hover 无 reflow、自动/覆盖/重置、0/1/少量/1000 Item、负坐标/DPI、恢复/缺失 monitor、严格整格容量、极端比例/溢出/无效输入及失败不修改快照。此前 176 项测试保留。

E1-4 独立实现全局主题 → Space → Idle/Focused 属性继承、unset/reset 和安全降级；复用本模块逻辑尺寸，不能把材质或视觉 transform 写回用户布局。E2 仍须真实 Windows 多屏/DPI/热插拔、原生 A/B 动画中断、命中/有效区域、虚拟化滚动、图标冲突和平台生命周期证据。没有真实性能基准或已完成多显示器适配的声明。
