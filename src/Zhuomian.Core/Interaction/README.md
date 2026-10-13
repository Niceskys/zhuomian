# E1 Space 领域交互控制器

本模块实现 [交互规范](../../../docs/INTERACTION_SPEC.md) 在本阶段的领域状态与安全规则。它不是 Windows 应用，不创建窗口、不移动视觉框、不启动真实文件，也不接受任何生产宿主 ADR。

## 调用契约

- `SpaceActivationPolicy` 要求显式指定 Hover/Click（A/B），A 延迟默认 20ms，允许 0–120ms；不替用户选择新 Space 默认模式。
- 构造控制器时注入单调 `IInteractionClock`、`IInteractionScheduler`、`IInteractionLauncher` 和非负收回延迟。收回延迟没有产品默认值；测试中的 30ms 只用于边界断言。
- 控制器、scheduler 与 launcher 在同一 dispatcher 上调用，不是跨线程对象。平台回调必须先归一化并汇聚到该 dispatcher。在同一 tick 内先 `Post` 所有输入，再 `Drain`；安全抢占、拖拽/菜单、搜索/IME、点击先于 due timer。已经提交到较早 tick 的 Focusing 不会阻断 Item 的首次点击。
- 内置 `DeterministicInteractionScheduler` 为一次性、拉取式调度。dispatcher 可按 `NextDeadline` 唤醒后 `Drain`，无 deadline 时不轮询；本模块不安装 OS timer。timer 被取消后即使仍被投递，也由 token 与 epoch 拒绝。
- pointer-down 的 `SpaceId/ItemId` 来自当时可见的同步命中。`GestureId` 是整个控制器内单调增长的物理点击序列 ID；后续重复 OS 消息沿用该 ID，双击第二序列使用 `ClickCount=2`。独立重复单击使用新 ID，不引入长时间去抖。
- pointer-up 的 `ValidClick` 由 adapter 的点击识别产生，并携带固定目标；布局变化不得将命中重新解释为空白或另一个 Item。若目标已删除，投递 `ItemInvalidated`；若 Space 删除，投递 `SpaceRemoved`。失效反馈为 `ClickInvalidated`，不得回落为空白激活。
- `LayoutChanged` 不改变固定点击目标。控制器不拥有 bounds、持久化原位、Grid、外部资源或拖入保存事务，因此不会写回动画坐标、重排图标或操作源文件；这些属于后续独立模块。
- `IInteractionLauncher.TryLaunch` 只接收稳定 Item ID，返回成功/失败；adapter 必须将预期启动失败转换成 false，不抛出异常，并单独通过未来启动安全域审查。消费点击发生在调用 launcher 之前，失败和重入投递均不会重复 launch。本 PR 仅使用 fake launcher。

## Focus、返回与键盘

`FocusOwner` 在 Focusing、Focused 和 Returning 期间最多一个。切换时旧 Space 进入 Returning；adapter 从当前视觉值开始返回，完成后回传 `TransitionCompleted` 和该 Space 的 `TransitionToken`，才允许新 Space Focusing。重复关闭保留返回 token，快速切换保留最新请求；旧回调不能结束新转换。没有 adapter 完成事件时控制器安全等待，不伪造动画完成或 B0/B1/B2 证据。

Hover/视觉 Focus 不捕获键盘。显式 `SearchActivate` 成功后发放 `KeyboardToken`；后续 TextInput/ImeStarted/ImeEnded 必须携带同一 token 和 Space ID。返回、抢占、退出键盘或新搜索会话会使旧 token 失效。IME 中 Esc 返回 `EscapeUnhandled` 交给 IME；组成结束后 Esc 依次关闭菜单/模态、取消捕获/拖拽、清搜索/退出键盘、返回 Space，每次一层。模态确认以 ModalOpened/ModalClosed 表达，不实现确认 UI。

ExternalForeground 下已存在且未改变的前台不阻断 Preview 单击；未经 Armed 的 A Hover/显式辅助展开仍被阻止，B 显式点击可用。VisibleDesktopClick 的 Armed 为 2000ms provisional，在成功 Focusing、外部输入/身份变更、暂停和 HostLost 时取消。暂停和丢失宿主直接清理为 Idle/Disarmed/NoKeyboardCapture；恢复不重播旧状态。外部前台变更优先安全返回。

DragEnter 取消 Hover、点击和待切换意图，冻结自动展开和收回；已展开逻辑保持稳定，安全抢占仍优先。DragEnd（drop/reject/cancel）以及 Esc 取消都要求新的 PointerEnter 才能重新计时 Hover，不复活旧 timer。本模块不接收拖入载荷，不创建入口或执行保存。ActivityChanged 的 Dragging/PointerCaptured 表达未来布局事务的安全抑制，仅清除领域活动标志，事务回滚由未来编辑模块负责。

## 验证与边界

`SpaceInteractionControllerTests` 使用 fake clock、fake scheduler、fake launcher；包含 0/20/120ms、过期/提前 timer、Hover 临界点击、目标取消/删除、布局改变、双击/重复消息、launch 失败/重入、Armed、前台身份、Suspended/HostLost、500 次切换、活动标志、搜索/IME token、Esc 分层和三个固定种子的 9000 步组合序列。旧安全契约和防抖参考模型测试保留不改。

| 不变量 | 本阶段证据/适用范围 |
|---|---|
| INV-01/02 | 暂停和外部 Disarmed 负面路径、100 次 Hover 尝试及 A 辅助展开安全门 |
| INV-03/07/08 | 主 Focus、返回完成、500 次切换、generation/epoch、提前/取消 timer、旧 transition/keyboard token；每步组合断言 |
| INV-04 | 显式键盘激活、NoKeyboardCapture 丢弃文本、退出/抢占、旧搜索会话消息 |
| INV-05 | HostLost 立即清理逻辑转换/捕获/键盘；真实窗口、动画资源、媒体释放待 E2/后续模块验证 |
| INV-06/11 | 固定 Item 点击优先、最多一次 launch、双击/重复消息不重启、A/B 独立路由 |
| INV-09 | DragEnter/End/Esc、点击/展开抑制、已有展开稳定及安全抢占；真实添加/布局事务待后续模块 |
| INV-10 | 控制器无持久化/几何/Grid 写入能力；LayoutChanged 不改目标。真实返回轨迹/原位保存和 Item transform 待 E2/E4 |

真实有效区域、显示器中心/Clamp、命中坐标随动画同步、Reduced Motion 轨迹、WinUI/IME、NoActivate、Windows Hook、WorkerW、视频和 Dock 均未实现/未验收。E1 其余数据模型、Space/Item 引用、纯布局和主题继承需独立 PR；E2 再接带门禁的原生 adapter。本 PR 不关闭任何 P0 平台/性能风险。
