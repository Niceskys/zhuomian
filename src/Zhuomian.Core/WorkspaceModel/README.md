# E1 Workspace / Space / ItemStore 领域模型

实现 [架构数据与身份契约](../../../docs/ARCHITECTURE.md)、[产品规范](../../../docs/PRODUCT_SPEC.md) 和 [安全边界](../../../SECURITY.md)。本模块仅包含不可变内存模型和纯操作；没有 Windows UI、磁盘访问、JSON、数据库、真实 Launcher、应用扫描、布局算法或外观继承。

## ID、引用与事务

- Workspace、Space 和 Item 使用调用方提供的非空 Guid；ItemStore 以稳定 Item ID 建立根级索引。调用方可生成新 ID，也可保留恢复的 ID；同类 ID 冲突拒绝。删除后保留的 ID 不可被新对象复用，避免旧点击命中新对象。
- 所有集合和对象不可变。操作返回新 Workspace，每次有效修改 revision 增加一次；无变化的 rename/configure/reorder/move/status 返回同一快照。失败（含枚举中途异常和 revision 溢出）不修改旧快照；跨 Space 移动、Item 添加并绑定都是单个原子领域操作。
- `Space.ItemIds` 保留用户顺序。正常添加不允许同 Space 重复引用，抛出 `ReferenceAlreadyExists`；不会自动移动或合并。多个 Space 可引用同一个 Item。默认追加，显式 index 插入；移动 index 表示移除后的最终位置。
- `ReorderReferences` 必须是原 ID 集合的完整无重复排列；禁止借排序添加/丢弃引用。存在重复原始引用时先显式修复，排序/移动不自动清洗。
- `RemoveReference` 只解除指定 Space 的引用；也可显式修复隔离 ID（包括空 ID、缺失 ID 和重复 ID），移除该 ID 的全部原始出现。源 Item 与其他 Space 保留。
- `DeleteSpace` 从活动集合删除 Space，保存 `RemovedSpaces` 恢复记录，不删除根级 Item。普通解绑/删除使 Item 无引用时仍保留；没有自动回收或 TTL。
- `RemoveItemGlobally` 是独立命令，必须传入 `GlobalRemovalConfirmation.Confirmed`；未来应用层必须先获取用户确认，枚举本身不是安全授权。它只从所有活动 Space 解绑并把 Item 转入 `RemovedItems`，保留完整元数据、身份和资源引用，保留旧快照及已删除 Space 的恢复记录。
- `RestoreItem` 把相同 ID/身份恢复到活动目录；不重新创建已被全局解绑的入口。若加载快照已保留隔离引用，恢复其 Item 后该原始引用可再次解析。删除 Space 的完整恢复可由后续恢复模块使用保留记录构造新快照，当前没有自动恢复 UI。
- 移除后的 Item 身份继续保留；重复添加同一规范身份拒绝，调用方须明确选择已有 ID/恢复 ID。没有静默去重丢元数据。

## 四类资源身份

| 类型 | 身份与元数据 | 约束 |
|---|---|---|
| ApplicationItem | provider-scoped `ApplicationIdentity` + `ApplicationProvenance` | provider/application token 去外围空白、NFC、大小写敏感；没有任意 command line、参数、脚本或执行 API |
| FileItem | 绝对 WindowsPath + 可选 opaque stableFileIdentity | 路径词法归一化，与 FolderItem 分开去重；稳定文件标识保留为辅助证据，不擅自以它合并移动/硬链接 |
| FolderItem | 绝对 WindowsPath + 可选 opaque stableFileIdentity | 与 FileItem 同样保持失效可恢复，不检查文件系统 |
| UrlItem | 规范 HTTP(S) AbsoluteUri | 拒绝其他 scheme、credentials 和控制字符；保留 path/query/fragment 的实际目的地 |

WindowsPath 不使用宿主平台路径函数：统一斜杠、drive letter、`.`/`..` 和 UNC，拒绝相对路径、越过根、device namespace、ADS、明显非法字符和尾部点/空白。CanonicalKey 为 ordinal 大小写折叠后的路径；这只是默认 Windows 词法去重，不证明存在/可打开，也不能识别 case-sensitive volume、reparse、硬链接或替换攻击。路径、网络/可移动目标和 provider token 始终是不可信数据；真实 Launcher 仍须独立验证身份、文件/关联、来源、链接、权限和 URL allowlist。Application identity 不等于 provider 信任或执行能力；具体 provider/启动描述由后续 ADR 与安全适配器确定，不能把 opaque ID 当命令执行。

`ItemMetadata` 包含显示名称和可选 icon 资源引用。`ExternalResourceReference` 保留外部路径；`ManagedAssetReference` 保留自有资产 ID，二者无删除/解码能力。`GetRetainedManagedAssetIds` 包含活动、未引用及全局移除 Item 的资产保留集合；它不是可回收清单。`GetReferenceCount` 仅为当前活动 Space 中原始出现次数，不含旧快照/删除 Space/备份，不可单独用于 GC。未来回收必须再核对所有权、备份和恢复快照；当前没有任何资源回收。

`ItemAvailability` 初始 Unverified；适配器可反馈 Available/Missing/Unsupported，改变状态不删除引用。Available 不是安全许可，Missing 不是反序列化失败。ToString 与异常消息不输出资源目的地；公开日志仍应只使用 ID/错误码，禁止直接记录字段。

## 损坏引用与配置

- `FromSnapshot` 是纯 typed rehydration（没有序列化或迁移）。Space 构造会复制原始 Item ID 顺序，允许保留待诊断的损坏引用；正常编辑 API 不产生新的悬挂引用。
- `DiagnoseReferences(spaceId)` 返回 Space/Item ID、原 index 与 EmptyIdentity/MissingItem/GloballyRemovedItem/DuplicateReference；`GetUsableItemIds` 只返回结构可解析的按序引用，隔离项仍原样保存在 `Space.ItemIds`。Missing/Unsupported 资源状态仍可显示为失效入口，此视图不代表 launch 可用。
- 添加对应 Item 或显式 RestoreItem 可修复悬挂引用；仅显式 RemoveReference 才清除原始记录。重复引用的第一份有效记录保留在可解析视图；后续出现诊断为 DuplicateReference。
- root schema 不支持、同类重复对象 ID 或重复规范身份时拒绝 typed snapshot；调用方仍拥有全部原始输入。未来 Persistence 必须在构造这些对象前隔离无效/未知 Item 并保留原始载荷，按规范回退 last-known-good。当前没有未知 kind 的 serializer，也不以此宣称恶意配置导入通过。
- 复用现有 `SpaceActivationPolicy`，创建 Space 必须显式选 Hover/Click；A 默认 20ms、范围 0–120ms，未设新 Space 默认 A/B。
- `PreviewCapacityPolicy` 的 null 表示自动/重置，非负显式整数表示覆盖（包括 0）；它只是配置表示能力，不冻结 UI 可选容量、最小/最大容量或自动算法。尺寸、布局和主题未加入本 PR，容量操作不写任何几何配置。
- `schemaVersion = 1` 为本阶段 typed domain 契约，revision 为非负单调 long；磁盘 schema、迁移和保存协议仍须 Persistence ADR/fixture 冻结。不持久化 Focus、Armed、搜索、动画或键盘运行态。

## 集成方向与限制

依赖单向：WorkspaceModel 复用 Core.Interaction 的策略，Interaction 不读取 WorkspaceModel。`GetInteractionPolicies` 只投影 canonical Guid `D` 字符串和现有策略，供控制器构造；稳定 Item ID 通过同样的 `D` 字符串跨 `IInteractionLauncher`。

未来应用层在单一 dispatcher 提交成功的新快照，并协调命中/控制器/Launcher 的当前版本。全局 Item 移除发送 ItemInvalidated；Space 删除发送 SpaceRemoved；单 Space 解绑不能全局 ItemInvalidated（会误伤其他 Space 共享项）。若解绑了 pointer-down 固定目标，应在该点击的应用层上下文投递 PointerCancel，不回退为空白点击；Launcher 再查最新活动目录与独立安全许可。新建 Space/配置更新尚无现有 controller 热更新 API，未来独立集成需要定义安全注册/更新或显式重建，不能保留/复活旧运行态。本 PR 不实现同步桥或扩展控制器。

Persistence 只消费不可变快照和 revision，自身负责保存竞争、事务提交、备份、恢复、迁移与原始损坏载荷。未来真实 Launcher 只解析最新的有效 Item，执行各安全域，不被领域对象授权。各适配器反向依赖 Core，不构成循环。

## 验证与后续

`WorkspaceTests`、`ResourceIdentityTests`、`InteractionCompatibilityTests` 覆盖 CRUD/ID 冲突、共享/解绑/显式全局移除、排序/跨 Space 移动、损坏引用保留、边界/原子失败/overflow、逐 Space 策略、资源保留、规范身份去重和既有控制器的 0/20/120ms、Preview 稳定 ID、删除失效事件兼容。既有 110 项测试文件原样保留。

E1-3 [纯布局引擎](../Layout/README.md) 通过只读 Workspace 投影计算 Grid/容量，并用独立 typed placement 候选计算恢复边界；没有修改本模块 schema。后续独立 PR：E1-4 外观继承；E2 原生门禁集成；E3 Launcher/安全持久化与恢复。没有真实 Windows 产品、启动/保存、动画/布局性能或实机验收证据；P0、启动和数据门禁均未被关闭。

E3-2 现由独立 [Persistence 模块](../../Zhuomian.Persistence/README.md) 消费本模型，领域代码/schema 保持不变。未知/损坏数据以私有隔离证据保留并拒绝有损保存；[磁盘与原生验证证据](../../../docs/E3_2_PERSISTENCE_EVIDENCE.md) 不等于完整 UI 布局或启动许可。
