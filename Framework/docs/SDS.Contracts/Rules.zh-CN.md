# 规则

> 定位：只收录 `SDS.Contracts` 自身适用、可执行可检查的规则。全局规则见《ModuleRules.zh-CN.md》，本文不重复（历史迁入的权威正文见文末「规则正文（权威）」）；每条都给出检查方式。

| # | 规则 | 可执行口径 | 检查方式 |
|---|---|---|---|
| C1 | 零行为、禁止实现引用 | 只允许声明类型、字段、属性、方法签名与 DefOf / 注册表；不得调用 Harmony、UI、寻路、行为层 API，不得出现 `SDS_Core` / `SDS_Diag` / `SDS_API` / `SDS_Ledger` / `SDS_Vanilla` / `SDS_Mod` 符号。 | `Tools/CheckAssemblyBoundaries.ps1` 第 7 节逐文件匹配禁止符号；禁止符号含 `Harmony`、`Window`、`ITab`、`Widgets.`、`GUI.`、`PathFinder`、`Reachability`、`GenClosest`、`JobDriver`、`Toil`、`ThinkTree`、`WorkGiver`、`DutyDef`、`ListerThings`、`SlotGroup` 及上述实现类名。 |
| C2 | 枚举边界 | 允许数据枚举（`Danger` / `PathEndMode` / `StoragePriority` / `ThingCategory` / `RotStage` 等）；禁止行为性枚举（`WorkGiverDef` / `ThinkTreeDef` / `DutyDef` 等）。自有枚举只能是状态、通道或结果码。 | 同 C1 的脚本第 7 节已覆盖行为类型名；新增枚举时对照《ModuleRules.zh-CN.md》6.1 人工复核，`Interfaces.zh-CN.md` 原版支持 三 记录当前枚举清单。 |
| C3 | DefOf 写法 | `SDS_JobDefOf` / `SDS_HediffDefOf` 一律用属性 `public static JobDef X { get { return DefDatabase<X>.GetNamedSilentFail(name); } }`；每次访问现查，禁止 `static readonly` 字段、禁止 `lookedUp` 之类一次性缓存；缺失返回 null，并按 Def 名各 `Log.ErrorOnce` 一次。 | 在 `SDS.CaravanTransport.cs` / `SDS_Implants.cs` 检索 `static readonly` 与 `lookedUp` 应为 0 命中；编译 `SDS.Contracts` 通过；Def 缺失场景按 `Interfaces.zh-CN.md` 原版支持 五 手工删 Def 验证。 |
| C4 | 纯注册表写法 | `SDS_PreserveHooks.Register` 空 / 重复返回 false；`RequiresPreserved` 任一探针为真即真，单探针异常 `Log.ErrorOnce` 后跳过。注册表为进程级常量，不登记 `SDS_StaticState`（`SDS_JobFuelCount` 已于 2026-11-16 删除，数量缺失由 W58 处理）。 | `SDS.Tests`：`LedgerTests.PreserveHook_RoutesToPreservedChannel`；消费点 `SDS.Core/SDS_StockRules.cs`；另检索 `SDS_StaticState.Register` 不得出现该类型。 |
| C5 | `SDS_Host.Install` 调用限制 | 生产代码只有 `SDS.Bootstrap`（`SDS_HostInstaller`）可调用；测试装配例外仅 `SDS.Tests` / `SDS.Core.Tests` 的 `CoreTestEnv.Create`。任一槽为 null 时 `Install` 记一次 `Log.ErrorOnce` 并继续，不抛异常、不阻止启动。 | `Tools/CheckAssemblyBoundaries.ps1` 第 6 节（字面量匹配 `SDS_Host.Install`，命中后按所属模块放行 `SDS.Bootstrap` / `SDS.Tests` / `SDS.Core.Tests`）；生产代码命中只允许在 `SDS.Bootstrap/SDS_HostInstaller.cs`。 |
| C6 | 序列化节点锁定 | `SDS_StockKey` 写 `defName` / `stuffName` / `qualityInt` / `preserved`，旧节点 `thingDef` / `stuff` 只读不写；`SDS_StockEntry` 写 `key`(Deep) / `count` / `avgHitPoints` / `avgRotProgress`；`SDS_Reservation` 写 `jobLoadID` / `key`(Deep) / `count` / `pawn`(Reference)；`SDS_ReclaimSave` 写 `policy` / `jobLoadID` / `ownerLoadID` / `tick` / `hand` / `things`(Reference) / `counts`(Value)；`SDS_TransportLoadSave`、`SDS_SidebarUIState`、`SDS_Settings` 同理按《ModuleInterfaces.zh-CN.md》八。节点名与 LookMode 不得改名或改模式。 | 无自动往返测试；`SDS.Tests/Contracts/ContractTests.StockKeyAndEntry_UseLockedSaveNodes` 断言键的保存侧不再出现旧节点、读取侧仍保留（2026-11-16 起）；其余逐行比对《ModuleInterfaces.zh-CN.md》八与本文件，再做「新档保存 → 读回 → 再保存」节点名与字段名逐字一致；改结构按 L3 走《DesignPrinciples.zh-CN.md》三。 |
| C7 | `SDS_Config.Current` 写入限制 | 运行期只由 Bootstrap 启动时写一次；Core / Vanilla / Api / ThirdModCompat 只读 `SDS_Config` 的纯读取口，不写 `Current`，不缓存跨档设置。测试可临时赋值，但必须还原。 | `Tools/CheckAssemblyBoundaries.ps1` 第 9 节（跳过 `SDS.Tests` / `SDS.Core.Tests`，只允许 `SDS.Bootstrap`）；测试侧另看 `SDS.Core.Tests/Support/CoreTestKit.cs` / `VanillaContractTests.cs`（`VanillaServices_Defaults_WithoutSave` 结束时置回 null）。 |
| C8 | 失败处理口径 | Def 缺失：返回 null + 按名 `Log.ErrorOnce` 一次，功能不启用但不阻断游戏；未解析品类：`SDS_StockKey` 保留 `defName` / `stuffName`，`Resolved == false` 是合法状态，不删账、不刷屏；宿主缺失：对应功能不可用，其余正常；探针异常：跳过该探针，其余继续。 | 代码锚点 `SDS_Host.Install`、`SDS_StockKey.TryResolve`、`SDS_PreserveHooks.RequiresPreserved`；测试 `LedgerTests.PreserveHook_RoutesToPreservedChannel`、`ApiTests.ReadStock_GridUnavailable_ZeroesAvailableKeepsTotal`（未解析优先分支）与 `MigrationTests`；删 Def 手工验证一次。 |

## 规则正文（权威）

> 下列规范句由全局规则索引迁入，`SDS.Contracts` 必须遵守，且 C1–C8 未覆盖；原样收录为权威正文。检查方式沿用对应 C 条与《ModuleInterfaces.zh-CN.md》《PatchInventory.zh-CN.md》。

| ID | 规范正文 | 与本模块的关系 |
|---|---|---|
| W26 | 旧档 → 新版必须可读；数据不丢优先于格式不变；不承诺新版 → 旧版。 | `SDS_StockKey` 自 2026-11-16 起只写 `defName` / `stuffName`；旧节点 `thingDef` / `stuff` 只读不写（2026-11-13 前写出的档仍可读）；所有 DTO 默认值必须能读旧档。 |
| W27 | 回滚按通道走专用回退（保真 `ReturnPreserved`、虚拟 `ReturnVirtual`），禁止通用 `Return`。 | `SDS_StockChannel` 为统一回充门面提供显式通道参数。 |
| W30 | 直接消费 Job 的镜像目标不在派活期物化；默认交付形态 = Hand，数量缺失由 W58 按预留意图补齐。 | `SDS_WithdrawDeliveryPolicyDef` 的 mode 语义；`SDS_JobFuelCount` 与 `RegisterJobFuelCountProvider` 已于 2026-11-16 删除。 |
| W33 | 「武器 / 装备强制虚拟化」开启时，普通武器 / 衣物不再因 `CompArt` 等状态保真。 | `SDS_Settings.forceWeaponApparelVirtual` 与 `SDS_Config.ForceWeaponApparelVirtual` 的语义。 |
| W34 | 该设置只在玩家确认后转换一次存量；关闭不回溯；读档 / 启动不得静默转换。 | `SDS_Config.ForceWeaponApparelVirtual` 只读；转换入口在 Core / Bootstrap 流程。 |
| W35 | `ThingDef.IsStudiable` 一律保真，且先于四类默认虚拟判定。 | 保真事实探针 `SDS_PreserveHooks` 与 `SDS_StockKey.preserved` 的消费口径。 |
| R17 | G 关闭时所有读侧可用量归零；账本数据保留，不属于品类禁用。 | `SDS_ApiStockInfo` 的 `result` / `available` / `failure` 组合口径。 |
| E4（应用） | 变更分级：L1 实现、L2 规则语义、L3 准则 / 存档 / 公共 API。 | 改 DTO 字段、节点名、`SDS_Host` 槽位属 L3。 |
| E5（应用） | Version 变更即兼容性声明；无外部采用可直接 break，有采用需薄转发至少一个大版本。 | 拆分后 `SDS_API.Version = 4`，程序集改名，第三方须重新 soft-depend。 |
| E7（应用） | 每个第三方入口只有一条写接管路径；只读投影可共存，不得有第二条扣账 / 派活 / 回滚。 | `SDS_PreserveHooks` 只登记事实，不建写路径。 |

## 补充口径（版本族）

- 键相等与哈希同口径：`SDS_StockKey.SameAs` 在任一侧材质未解析（`stuff == null`）时按 `EffectiveStuffName` 比较；`GetHashCode` 同样只用名字，二者不得出现「相等但哈希不同」。聚合展示键（`CategoryKeyOf`）只作展示 / 分组，不得写进账本或禁用集合。检查：`SDS.Tests` 的 `ContractTests` 键相等与哈希一致用例。
- `SDS_ReadVersions` 一次给齐六个版本：`tick` / `mirror` / `endpoint` / `stock` / `reservation` / `filter`；其中快照只按 `tick` / `mirror` / `endpoint` / `stock` 判失效。
- `CanReach`、事务预留与筛选**不**进快照：预留只属于可用量判定，筛选只属于只读查询缓存，可达性每次按需重算。
- 快照缓存以 60 tick 兜底刷新；版本未推进时也不会长期陈旧。
- `tick` 与各版本必须同批取齐（一次 `CaptureReadVersions`）；不得把先前捕获的 `tick` 与后取的版本拼在一起。
