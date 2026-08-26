# Zhuomian 性能预算

> 状态：测量协议已定义；数值门槛在 Phase 0 校准前均为 provisional。

## 1. 强制不变量

- P1：无动画、指针运动或媒体更新时，不得维持 CPU 驱动的逐帧循环。
- P2：Idle/ExternalForeground/Suspended 不得用高频 timer 轮询指针。
- P3：大型 Space 必须虚拟化；1000 Item 场景不得实例化全部视觉元素。
- P4：动画取消后必须释放 callback、timer、composition batch 和 pointer capture。
- P5：视频进入 Release 后 decoder、surface 与可归属 VRAM 必须可回收。
- P6：Backdrop 超预算时自动降级，不得以丢帧换取材质。

## 2. 标准测量协议

每份性能结果必须包含：

- commit SHA、Windows build、Windows App SDK 与 GPU driver；
- CPU、内存、GPU、显示器数量/DPI/刷新率；
- Release x64 构建和 packaging 模式；
- 冷启动或 warm-up 条件；
- 测量持续时间和重复次数；
- Average、P95、P99、max 与 dropped-frame ratio；
- 原始结果文件和采集命令。

默认协议：warm-up 60s、测量 300s、独立重复 3 次，报告中位运行与最差运行。Spike 可调整，但必须说明理由。

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
