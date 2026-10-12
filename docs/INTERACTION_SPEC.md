# Zhuomian 交互规范

> 状态：开发前规范基线 2026-10-12；本文件定义目标行为，不表示完整控制器已实现。

## 1. 正交状态

内部不得使用 `Passive/Armed/Active/Suspended` 单枚举作为唯一真相。

### DesktopAvailability

- `DesktopAvailable`
- `ExternalForeground`
- `Suspended`

### DesktopIntentGate

- `Disarmed`
- `Armed`

`Armed` 只表达用户近期点击过可见桌面区域，不表达 Windows 前台所有权。

### KeyboardMode

- `NoKeyboardCapture`
- `ZhuomianKeyboardActive`

Visual Focus 与 Keyboard Focus 必须分离。Hover、Intent 和纯视觉 Focus 不得隐式改变 `KeyboardMode`。

## 2. Space 状态

```text
Idle → Hover → Intent → Focusing → Focused → Returning → Idle
```

`Interacting` 改为 Focused 上的正交标志集合：`Searching`、`Scrolling`、`ContextMenuOpen`、`Dragging`、`PointerCaptured`。任一标志存在时暂停模式 A 的 Auto Collapse；模式 B 采用显式关闭。

## 3. Armed 生命周期

初始超时为 **2000ms provisional**。发生以下任一事件立即 `Disarmed`：

- 点击或激活外部应用；
- foreground identity 改变为另一外部窗口；
- 指针在外部应用客户区发生按键或点击；
- 进入 Suspended；
- 锁屏、会话切换、Explorer host 丢失；
- 超时；
- 一个 Space 成功进入 Focusing，意图已消费。

恢复或重启后始终为 `Disarmed`，不得持久化。

## 4. 激活判定

Hover 可进入 Intent 必须满足：

```text
Space.ActivationMode == Hover
AND DesktopAvailability != Suspended
AND (
  DesktopAvailability == DesktopAvailable
  OR (DesktopAvailability == ExternalForeground AND DesktopIntentGate == Armed)
)
AND no higher-priority event is pending
```

`ActivationMode.Hover` 对应模式 A；`Click` 对应模式 B。逐 Space 独立配置，互不触发另一模式。B 点击标题或空白进入 Focus；两种模式点击 Preview Item 始终优先直接 Launch。键盘显式展开可用于无悬停/辅助操作路径，同样服从安全门，不能隐式捕获文本。

### 4.1 A：原位置附近展开

- `hoverDelayMs` 默认 20，用户可逐 Space 设 0–120；0 也必须先经安全门和事件优先级处理，不能绕过点击竞态。
- 放大以原框附近为目标，受当前显示器工作区 Clamp，不因靠边而移到其他屏幕。尺寸、锚点和动画参数待实测。
- 有效区域必须包含原框、展开框及必要过渡路径；移动过程中不能因框离开指针而立即误收回。区域形状/安全余量待验证，不能把整屏作为永久有效区域。
- 指针离开有效区域后，经待校准的收回延迟 Returning；重新进入取消旧收回 timer。Searching/Scrolling/ContextMenuOpen/Dragging/PointerCaptured 和 IME 期间暂停 Auto Collapse。

### 4.2 B：移位放大与关闭

- 当前显示器取激活时原 Space 所属显示器；跨屏归属规则、跨屏编辑后的选择须实测。候选目标为该显示器工作区中央（避开任务栏），不能使用整个虚拟桌面中心。
- 同一 Space 视觉身份从当前 bounds 连续变换到中央目标，再以反向变换返回保存的原位；只移动视觉副本时也必须保持单一视觉身份、单一命中/Focus token，不可同时出现两个可操作框。
- 内容可在 Space 尺寸变化时响应式重排；动画中的命中坐标必须同步。返回不能把展开尺寸/位置保存为编辑布局。
- B 不因鼠标离开而自动收回。显式关闭按钮和 Esc 可返回；点击框外可见桌面空白关闭为候选规则，待实机验收；框外事件不得重复穿透或吞掉对外部应用的点击。外部前台/Host lost/Suspended 的安全抢占仍优先。
- 点击关闭、重复激活、切换 Space 时从当前视觉值中断/返回，不跳回动画起点。正常关闭后是否恢复搜索等内容状态待验证，运行态不得持久化。

### 4.3 B 渐进式降级（候选，启用前需证据与实机验收）

| 等级 | 实际效果 | 启用条件 |
|---|---|---|
| B0 优先目标 | 原框连续移位/放大，关闭反向返回 | 原生轨迹、命中、安全中断和性能通过 |
| B1 降级 | 保持单一视觉身份的简化移动/缩放，减少材质或内容过渡 | B0 失败原因、限制和回退证据明确 |
| B2 降级 | 中央浏览 surface 淡入/即时切换，关闭恢复原框 | 仍满足核心浏览与安全；明确标识“中央展开（简化动画）” |

每次降级记录 Windows/build/DPI、原因、实际等级和可恢复条件。B1/B2 不等于完整动画通过；未获验收只可工程试验。Reduced Motion 是用户主动的替代动效路径，单独记录，不能用来掩盖 B0 失败。

### 4.4 Preview 点击与 Hover 竞态

- pointer-down 命中可见 Item 时固定 Item ID/操作目标并取消未提交 Hover timer；激活 on-click/pointer-up 验证操作成立后恰好一次交给 launcher。事件消费，不再触发 Space 激活。
- 在有效点击序列结束/取消前，Hover 延时或布局变化不得把目标移走并改判成空白点击。pointer-down 已开始时禁止提交 Hover Focusing；若 Focusing 先发生，仍按同步命中结果支持一次 Item 点击，不能要求补点。
- timer、命中快照、Focus token 均带 generation；外部前台、删除目标、Host lost、Suspended 或拖拽接管使过期操作无效。失效时反馈失败，不改为打开另一个 Item。
- Hover 不产生 launch；双击的 OS 后续消息不能使同一已消费点击被再次执行。独立的重复单击是否去抖待验证，不能用长延迟破坏直接打开。

### 4.5 日常与编辑模式、拖入

- 日常模式不允许手势移动/Resize Space；提供明确编辑入口。编辑模式负责创建、命名、移动、缩放、删除、布局保存与取消；布局手势不得同时 launch 或展开。
- 两种模式接收到外部 drag-enter 时取消 Hover/Intent，冻结自动展开和相关命中布局；已有展开保持稳定直到安全抢占或拖入结束，拖动不改变激活模式。
- drop 只创建入口/引用，经类型、安全校验与保存事务；不自动打开、不移动/复制/删除用户源文件。取消或拒绝不能留下半完成入口；drop 完成后新的 pointer-enter 才能重新计时 Hover。
- 添加入口的具体视觉/目标区待原生验收，不能在本任务中指定成用户已确认 UI。自定义拖拽载荷未经安全审查不启用。

### 4.6 Grid 与图标反馈

Space 展开、编辑 Resize 或容量变化可重算 Grid，内容滚动保持外框稳定。单 Item Hover 放大采用 transform/overlay，不改布局尺寸、不触发全 Grid reflow；重叠命中、裁剪、滚动和虚拟化需验证。

## 5. 输入命中优先级

从高到低：

1. 系统安全状态、Suspended、Host lost。
2. 已捕获指针的 Drag/Resize。
3. 上下文菜单或模态确认。
4. 搜索输入和 IME composition。
5. 可见 Item 点击。
6. Focused Space 内容滚动。
7. Space 空白区域激活。
8. 可见桌面区域的 Armed 点击。
9. Hover 微反馈。

事件被高优先级处理后不得继续冒泡成另一语义。

## 6. Esc 优先级

连续按 Esc 每次只执行一层：

1. 关闭模态确认或上下文菜单。
2. 取消 Drag/Resize 并恢复事务前状态。
3. 清空搜索文本；若已为空则退出键盘模式。
4. Focused/Focusing Space 进入 Returning。
5. 无可关闭层时不处理，让系统或外部应用接收。

## 7. 抢占与竞态

- Focusing A 时激活 B：A 从当前视觉值平滑 Returning；B 在 A 释放主 Focus token 后才 Focusing。
- ExternalForeground 新出现或外部身份改变：取消未提交的 Hover/Intent；Focusing/Focused 安全 Returning；若失去安全渲染条件则直接安全 Idle。已 Armed 的同一外部前台下仅允许明确意图路径，不能借延迟旧事件放行。
- Suspended 或 Host lost：清空延时事件、取消动画、释放输入捕获和键盘模式，逻辑回到安全 Idle。
- Resume/Host restored：从 Idle/Disarmed/NoKeyboardCapture 开始。
- 延时事件必须携带 generation/token；过期事件丢弃。

## 8. 键盘与搜索

- Hover Focus 不捕获键盘。
- 用户点击搜索框、通过明确快捷键进入或在 Zhuomian 已显式激活后，才进入 `ZhuomianKeyboardActive`。
- 核心内测版不支持“纯视觉 Focus 后任意字母自动搜索”。
- IME composition 期间不得 Auto Collapse。

## 9. 组合不变量

- INV-01：`Suspended` 时 Space 不得进入 Intent/Focusing/Focused。
- INV-02：`ExternalForeground + Disarmed` 时 Hover 不得触发 Intent。
- INV-03：主 Focus token 持有者不超过 1。
- INV-04：`NoKeyboardCapture` 时 Zhuomian 不接收文本输入。
- INV-05：Host lost 后不存在输入捕获、活动动画或媒体播放。
- INV-06：Preview Item 点击不会同时触发 Space Focus。
- INV-07：Returning 完成后视觉和逻辑状态均为 Idle。
- INV-08：过期 timer/event 不改变当前状态。
- INV-09：外部 drag 活动期间不自动展开、不发生布局手势或 launch。
- INV-10：Space 返回不覆盖已保存原位；Item Hover 不触发全 Grid reflow。
- INV-11：同一已消费 Preview 点击最多一次 launch；模式 A/B 不互相隐式触发。

这些不变量必须由确定性测试覆盖，参见 [RELIABILITY_TEST_PLAN.md](RELIABILITY_TEST_PLAN.md)。
