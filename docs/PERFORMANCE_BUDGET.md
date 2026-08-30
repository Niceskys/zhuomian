# Zhuomian 性能预算

> 状态：测量协议、机器可校验的证据契约与通用进程采样器已定义；Release x64 Zhuomian 场景编排、实机校准和数值冻结仍未完成，数值门槛均为 provisional。

## 1. 强制不变量

- P1：无动画、指针运动或媒体更新时，不得维持 CPU 驱动的逐帧循环。
- P2：Idle/ExternalForeground/Suspended 不得用高频 timer 轮询指针。
- P3：大型 Space 必须虚拟化；1000 Item 场景不得实例化全部视觉元素。
- P4：动画取消后必须释放 callback、timer、composition batch 和 pointer capture。
- P5：视频进入 Release 后 decoder、surface 与可归属 VRAM 必须可回收。
- P6：Backdrop 超预算时自动降级，不得以丢帧换取材质。

## 2. 标准测量协议

每份性能结果必须包含：

- commit SHA、采集 UTC 时间、Windows build、Windows App SDK 与 GPU driver；
- CPU、内存、GPU、显示器数量/DPI/刷新率；
- Release x64 构建和 packaging 模式；
- 冷启动或 warm-up 条件；
- 测量持续时间和重复次数；
- 每个已报告数值指标的 Average、P95、P99 与 max；
- 涉及 frame presentation 的场景必须报告 dropped-frame ratio；不涉及时必须给出不适用原因；
- 中位运行与最差运行索引；
- 原始结果文件和可复现的采集命令。

默认协议：warm-up 60s、测量 300s、独立重复 3 次，报告中位运行与最差运行。Spike 可调整，但必须说明理由。

### 2.1 机器可校验的证据契约

P0-07 使用 `schemaVersion: 1` 的 JSON 证据文件。校验器位于 `scripts/performance/validate-performance-evidence.ps1`，确定性自测位于 `scripts/performance/test-performance-evidence-validator.ps1`。

证据文件至少包含：

- `commitSha`、`scenarioId`（S1-S8）、`collectedAtUtc`；
- `machineTier`：`Baseline`、`Enhanced`、`Exploratory` 或 `CI`；
- `eligibleForThresholdCalibration`；
- `environment`：Windows build、Windows App SDK、GPU driver、CPU、RAM、GPU 与显示器 DPI/刷新率；
- `build`：必须为 `Release`、`x64`，并记录 packaging mode；
- `protocol`：warm-up、测量时长、重复次数、条件及必要的 deviation reason；
- `collectionCommand` 与存在的相对 `rawResultFiles`；
- `metrics[]` 的 name/unit/Average/P95/P99/max；
- `framePresentation` 的 measured/dropped-frame ratio 或不适用原因；
- `runSelection` 的 median/worst run。

约束：

- `collectedAtUtc` 必须是 JSON string，使用 `System.Text.Json` 接受的 extended ISO-8601 date/time profile，并显式以 `Z` 或 `+00:00` 表示 UTC；宽松日期文本不属于有效证据；
- 只有真实 `Baseline` 或 `Enhanced` 机器证据可将 `eligibleForThresholdCalibration` 设为 `true`；
- `CI` 或 `Exploratory` 结果可以验证工具链或做诊断，但不得冻结产品阈值；
- 偏离默认 60s/300s/3 次协议必须写明原因；
- 原始结果路径必须相对证据文件、真实存在、禁止路径穿越；
- 可持久化命令与路径不得包含用户 home/private 路径。

CI 运行证据契约的确定性自测，但不执行也不冒充真实性能基准。校验器通过只能证明“证据格式可复核”，不能证明 Zhuomian 达到任何性能预算。

### 2.2 通用进程采样器

`scripts/performance/collect-process-samples.ps1` 提供 P0-07 的原始进程资源采样基础。它直接启动并拥有一个目标子进程，每次 repetition 使用新的进程实例，默认参数与标准协议一致：60s warm-up、300s measurement、1000ms sample interval、3 repetitions。

每个 `run-XX.csv` 只记录：

- round-trip UTC timestamp；
- monotonic elapsed milliseconds；
- 按逻辑处理器数量归一化的进程 CPU percentage；
- Private Bytes、Working Set；
- handle count 与 thread count。

采样器约束：

- 参数通过 `ProcessStartInfo.ArgumentList` 逐项传递，不拼接 shell command string；
- 输出目录必须为空，避免不同采样批次混合；
- 目标在 warm-up/measurement 期间提前退出即判定该批次失败；
- 每个目标启动后立即分配到私有 Windows Job Object，并启用 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`；成功 assignment 之后由该目标创建且未显式 breakaway 的后代会继承 Job containment；
- 每次 repetition 的 finally 都关闭 Job Object；因此根进程即使已提前退出，已经归入该 Job 的存活后代仍必须被 kill-on-close 清理，根进程仍存活时还必须在 5 秒内确认退出；
- raw CSV 不持久化可执行文件完整路径、working directory 或用户 home path；
- 当前保证从**成功 Job assignment**开始。目标在 `Process.Start()` 返回到 assignment 成功之间创建的进程、显式 breakaway 进程，以及启动器退出后转移到另一个独立 packaged/activation 进程的模型，不属于本采样器的 containment/测量保证；这些情况必须由后续场景 runner 单独处理。

`scripts/performance/test-process-sampler.ps1` 在 CI 中使用临时 `pwsh` 子进程和缩短的显式参数做烟测，验证采样、UTC 格式、elapsed 单调性、资源字段、正常根进程清理、fail-closed 路径，以及“根进程提前退出但已归入 Job 的长寿命子进程仍被清理”的回归场景。该烟测是**工具链证据**，不是 Zhuomian 性能结果；它不得参与 Baseline/Enhanced 阈值校准。

## 3. 参考硬件档位

Phase 0 选择并记录至少两档真实机器：

- Baseline：集成显卡、16GB RAM、60Hz。
- Enhanced：独立显卡或现代高性能集显、高刷新率。

没有实机数据前，不对“低端设备”作营销承诺。

## 4. 初始预算

| 场景 | 指标 | Provisional 门槛 |
|---|---|---:|
| Static idle, 3 Space | CPU average | ≤ 1% |
| Static idle, 3 Space | CPU P95 | ≤ 2% |
| Static idle | Private bytes | ≤ 180 MB |
| Static idle | GPU engine average | ≤ 1% |
| 60Hz focus animation | frame time P95 | ≤ 16.67ms |
| 60Hz focus animation | frame time P99 | ≤ 25ms |
| 60Hz focus animation | dropped frames | ≤ 1% |
| Search, 1000 Item | response P95 | ≤ 50ms |
| Explorer restart | host recovery | ≤ 5s |

WinUI/packaging 基线可能要求调整内存门槛。调整只能依据可复现数据和 ADR/PR 说明。

## 5. 固定场景

- S1：3 Space / 30 Item，静态背景，完全 Idle。
- S2：8 Space / 200 Item，外部窗口前台，Hover 穿越。
- S3：20 Space / 1000 Item，Focused 滚动和搜索。
- S4：连续 Focus/Return 500 次。
- S5：Explorer 重启 20 次。
- S6：单屏与双屏混合 DPI；热插拔 20 次。
- S7：1080p 与 4K 视频分别 Play/Pause/Stop/Release。
- S8：Solid、Simulated glass、Enhanced backdrop 对比。

## 6. 多显示器增长预算

每增加一台 Idle 显示器，必须单独报告 host、surface、内存、CPU 和 GPU 增量。Phase 0 冻结具体上限；在上限确定前，任何线性以上增长均视为失败。

## 7. 长期驻留

至少运行 8 小时并执行周期性交互。Private bytes、handle、thread、GPU committed memory 和 decoder 数量不得呈持续单调增长。24 小时 soak 在 V0.3 release gate 前成为必需。

## 8. 回归规则

同一协议下任一关键指标退化 ≥10% 必须解释；越过硬门槛则阻断合并。不得用提高测量噪声、改变硬件或缩短采样规避回归。

## 9. P0-07 剩余退出条件

证据契约和通用采样器完成不等于 P0-07 完成。仍必须具备：

- 将采样器绑定到可重复执行的 **Release x64 Zhuomian S1-S8 场景编排**；
- 从原始时间序列生成完整 evidence metadata、每项 Average/P95/P99/max 与 run selection；
- 对适用固定场景生成真实原始结果；
- Baseline 与 Enhanced 至少两档真实机器数据；
- 同协议下的中位/最差运行比较；
- 基于可复现数据冻结或修订 provisional 阈值。
