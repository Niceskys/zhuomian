# Zhuomian 交互规范

> 状态：Post-audit baseline v0.2

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

`Interacting` 改为 Focused 上的正交标志集合：`Searching`、`Scrolling`、`ContextMenuOpen`、`Dragging`、`PointerCaptured`。任一标志存在时暂停 Auto Collapse。

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

Click 模式点击 Space 空白区域进入 Focus；点击 Preview Item 始终优先直接 Launch。

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
- ExternalForeground 出现：取消未提交的 Hover/Intent；Focusing/Focused 立即 Returning。
- Suspended 或 Host lost：清空延时事件、取消动画、释放输入捕获和键盘模式，逻辑回到安全 Idle。
- Resume/Host restored：从 Idle/Disarmed/NoKeyboardCapture 开始。
- 延时事件必须携带 generation/token；过期事件丢弃。

## 8. 键盘与搜索

- Hover Focus 不捕获键盘。
- 用户点击搜索框、通过明确快捷键进入或在 Zhuomian 已显式激活后，才进入 `ZhuomianKeyboardActive`。
- V0.1 不支持“纯视觉 Focus 后任意字母自动搜索”。
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

这些不变量必须由确定性测试覆盖，参见 [RELIABILITY_TEST_PLAN.md](RELIABILITY_TEST_PLAN.md)。
