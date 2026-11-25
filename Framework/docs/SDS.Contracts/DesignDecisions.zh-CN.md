# SDS.Contracts · 设计决策（为什么这么做 / 踩过的坑）

> 定位：把旧框架中仍然有效、且维护 `SDS.Contracts` 必须知道的「为什么」固化下来：历史决策与坑、迁移时删掉 / 新增了什么（含理由）、为什么必须是纯数据程序集、旧存档节点与字段的历史说明。旧源码与设计方案自此只作历史证据，不再作为日常依据；可执行规则见 `Rules.zh-CN.md`，签名见 `Interfaces.zh-CN.md`。

## 一、历史决策与坑

- `SDS_StockKey` 必须同时重写 `Equals` / `GetHashCode`：旧注释记录过只写 `SameAs` 导致同品类每次 `new` 出独立字典项、钢铁 200 与 300 分成两栏的事故，删除即回归。
- 保真键的 `thingDef` 是「真身」def：缩装家具取 `MinifiedThing.InnerThing` 的 def，尸体 / 基因载体取自身；不能用外壳 def 当键，否则会把所有缩装家具并成一个品类。
- `defName` / `stuffName` 无条件落盘是 W26 的核心。旧节点 `thingDef` / `stuff` 自 2026-11-16 起**只读不写**：W26 只承诺「旧档 → 新版」、不承诺降级，所以新档无需再回写旧节点；读取兜底必须保留到 2026-11-13 之前写出的档退出支持窗口。
- DefOf 不能退回静态缓存：静态构造若发生在 Def 加载前，会把 null 永久固化，之后所有调用都拿 null。
- `BackCompatibility.BackCompatibleDefName` 可能抛异常；旧实现捕获后按原名 `GetNamedSilentFail`，不要删这层兜底。
- `SDS_Host` 选可空静态字段而非构造注入：宿主注入必须早于 `SDS_GameComponent_Grid` 构造，且 Core 不能引用 Vanilla；null 槽位只让对应功能不可用。
- `SDS_PreserveHooks` 是进程级常量，不随存档变化，不登记 `ResetAll`；`Reset` 只给测试与重载用。
- 旧 `SDS_Settings` 的两个「分开计算」开关只影响品类计数口径，取出仍按品质 / 材质还原；不要把开关写进键本身。
- ~~`SDS_ProtectedOutput` 与 `MapComponent_SDS_ProtectedOutputs`~~ 已于 2026-11-16 按 [兼容退役清单](../SDS.Vanilla/CompatibilityRetirement.zh-CN.md) 退役（只服务 2.4 / 2026-10-26 21:54 之前的档）。新档只写 Vanilla 的统一回收账本（C1/C8），不要恢复第二套登记。

## 二、迁移时删掉 / 新增了什么（含理由）

删掉或移走：

- `SDS_StockEntry.preserved`、`displayKey` 与三参构造：只服务 UI，本就不落盘（旧 `SDS_Stock.cs` 372–405 行）；显示派生归 Vanilla。
- `SDS_StockVitals.CaptureHitPoints` / `CaptureRotProgress`：加权平均归 Core 的 `SDS_StockRules`，Contracts 只留字段。
- `SDS_WithdrawPlan.Key` / `Quantity` / `Preserved` 计算属性与 `SDS_ReadVersions.Capture()`：由使用方从字段读取，读档 / 版本捕获在 Core / Api。
- `SDS_WithdrawDeliveryPolicy` 解析器、`SDS_WithdrawSave` 审计类、`SDS_EndpointPriorityState`：分别归 Vanilla / Core / Vanilla，Contracts 只留 DTO。
- `SDS_SidebarUIState.NormalizeAgainst(grid)`：删除；旧聚合展示键（`CategoryKeyOf` 归零过 stuff / quality）的读档归一改由 Vanilla 的 `Window_SuperSpace.RebuildSelectedMembers` 在重建时完成（同 def + 同通道取身份序最小的行代表键，找不到才清空）。
- `SDS_Config.Settings` 属性（旧实现直读 `SDS_Mod.Settings`）：改为 `SDS_Config.Current`，由 Bootstrap 注入。

新增或改写：

- 新增 `SDS_StockChannel`（虚拟 / 保真）、`SDS_StructureKind` / `SDS_StructureFact`、`SDS_PreserveHooks`。
- 新增 `SDS_Host` 与 11 个宿主槽对应的接口、`ISDSCompatAdapter`；旧 `SDS_EndpointDecision.handle` 与 `SDS_WithdrawPlan.endpoint` 的具体类型 `SDS_EndpointHandle` 改为接口 `ISDS_Port`。
- ~~`SDS_JobFuelCount`~~ 已于 2026-11-16 删除（方案乙：数量缺失由 W58 按预留意图统一补齐，第三方不再注册数量）。
- 可见性收窄的同类先例：Core 的 `SDS_Ledger.CurrentDataVersion` 由旧 `public const` 改为 `internal const`；本程序集保留的 `SDS_JobDefOf.*Name` / `SDS_HediffDefOf.*Name` 仍是 `public const`，因为它们是跨程序集共享的稳定 Def 名。
- `SDS_JobDefOf` / `SDS_HediffDefOf` 由一次性 `lookedUp` 缓存改为每次访问 `DefDatabase` 现查；缺失从 `Log.Error` 改为按名 `Log.ErrorOnce`。
- 旧 `SDS_Settings.ResetToDefaults` 末尾的 `SDS_EndpointPriorityState.NotifyChanged()` 删除；通知归 Vanilla / Bootstrap。
- `SDS_API.Version` 升到 4；旧 `SuperDimensionalStorage.dll` 不再保留。

## 三、设计取舍

为什么必须是独立的纯数据程序集：

- **解除模块间编译依赖**：依赖图以本程序集为唯一公共上游，Diag / Core / Api / Vanilla / ThirdModCompat / Bootstrap 全部只向下引用，没有反向引用（《ModuleRules.zh-CN.md》四）；模块之间只允许经 Contracts 的 DTO 与接口通信，不得引用其它模块的 `internal` 类、实现类或静态状态（《ModuleRules.zh-CN.md》二.2）。
- **拆分前的代价**：单程序集 `SuperDimensionalStorage.dll`（48 个 `.cs`，见《ModuleRules.zh-CN.md》冻结基线）里数据类型与实现类同处一室，任何实现改动都会牵动全部调用方；契约独立后，改 Core 规则不再需要重编 Vanilla。
- **可独立验收**：每个模块的完成不以其它模块已完成为前提，验收标准是契约 + 独立编译 + 独立测试（《ModuleRules.zh-CN.md》二.1、二.5）。
- **可脱离游戏启动流程测试**：Core 不引用 Vanilla / Api / Bootstrap，只依赖 Contracts 与 Diag；所有宿主事实都经 `SDS_Host` 槽位或调用方入参取得（Core 直接消费 `Ports` / `DepositFilter` / `Delivery`；优先级 / 镜像物理 / 医疗门以查询字段 / 计划字段 / 委托注入），实现类都是 public 无参构造、构造时不读 `Instance` / `State` / `Find` / `Def`（《ModuleInterfaces.zh-CN.md》4.2）。`SDS.Tests` 的 `CoreTestKit` 即用假实现填满槽位并注入上述参数，再直接 `new SDS_Ledger()` + `SDS_Core.Attach`（`超维存储/Source/SDS.Tests/Core/CoreTestKit.cs`、`SDS.Tests/Fakes/`）。「脱离」仅指不启动游戏、不建 `Map`、不进 Mod 启动顺序；编译与运行仍需 `Assembly-CSharp` 与 `UnityStubs`。
- **契约稳定后各模块可独立优化**：只要 DTO 字段名与接口成员不变，Core 可以改事务实现、Vanilla 可以重写补丁、ThirdModCompat 可以替换适配器，互不重编（《ModuleRules.zh-CN.md》二.1、二.5）；稳定性由存档锁定清单与 `CheckAssemblyBoundaries.ps1` 的类型级白名单背书（《ModuleInterfaces.zh-CN.md》八、《ModuleRules.zh-CN.md》五、十）；改存档结构或对外 API 属 L3，必须真人决策（《DesignPrinciples.zh-CN.md》三 E4）。

本程序集拆为 13 个文件：`SDS_Stock.cs` / `SDS_Withdraw.cs` / `SDS_EndpointContract.cs` / `SDS_API.cs` / `SDS_Mod.cs` / `SDS_CaravanTransport.cs` / `SDS_Implants.cs` / `SDS_UI.cs` / `SDS_Search.cs` / `SDS_Trade.cs` / `SDS_JobTargets.cs` / `SDS_Host.cs` / `SDS_PreserveHooks.cs`，各文件承载的类型见 `Interfaces.zh-CN.md` A 节。

旧框架没有宿主接口层：Core 与 Vanilla 在同一程序集里直接调 `SDS_GameComponent_Grid` / `SDS_EndpointHandle`，因此 `SDS_Host`、11 个宿主接口、`ISDSCompatAdapter` 都是拆分时新加的契约（`SDS_Host.cs`）。

拆分时新抽出的纯数据类型（旧框架无同名类型）：

| 类型 | 现文件 | 说明 |
|---|---|---|
| `SDS_StockChannel` | `SDS_Stock.cs` | 旧框架用 `SDS_ReturnUtility.ReturnPreserved` / `ReturnVirtual` 两个方法表达通道区分（旧 `SDS_ReturnAndReclaim.cs`），统一回充门面需要显式通道参数 |
| `SDS_StructureKind` / `SDS_StructureFact` | `SDS_EndpointContract.cs` | 旧框架的结构登记是 `SDS_GameComponent_Grid.RegisterCore` 等具体方法（旧 `SDS_GameComponent_Grid.cs` 378 行），拆分后改为值类型事实 |
| `SDS_PreserveHooks` | `SDS_PreserveHooks.cs` | 第三方「必须保真」事实的纯注册表，旧框架无对应物 |

关键取舍：

1. **DefOf 用属性而非 `static readonly` 字段。** 旧框架用 `lookedUp` 懒加载缓存（旧 `SDS_CaravanTransport.cs` 3714 行起、`SDS_Implants.cs` 16 行起）；拆分后 Def 引用要跨程序集使用，静态构造时机不可控，缓存一旦在 Def 加载前跑过就会把 null 固化。现实现改为每次访问 `DefDatabase<T>.GetNamedSilentFail`，缺失返回 null 并按 Def 名 `Log.ErrorOnce` 一次（本目录 `Interfaces.zh-CN.md`；`SDS_CaravanTransport.cs`、`SDS_Implants.cs` 现实现）。
2. **`SDS_Host` 用可空静态字段而非构造函数注入。** 构造函数注入会把宿主创建顺序绑进 Core（Core 不引用 Vanilla），而宿主注入必须早于 `SDS_GameComponent_Grid` 构造完成（《ModuleRules.zh-CN.md》九 第 2 步与第 7 步的顺序）；null 槽位的降级口径见 §一 与 `Rules.zh-CN.md` C5。
3. **禁止行为性枚举。** 本程序集目前只出现 `Danger` / `PathEndMode` / `StoragePriority` 这类纯数据枚举，未出现 `WorkGiverDef` / `ThinkTreeDef` / `DutyDef`（判定边界见《ModuleRules.zh-CN.md》6.1 与 `Rules.zh-CN.md` C2）。理由：契约若引用行为性枚举，等于把「谁派活、怎么走」的知识写进所有模块共同依赖的上游，原版行为表一变就全量重编，且违反「不改原版表现出的工作逻辑」（《DesignPrinciples.zh-CN.md》推论二）。
4. **端点用接口 `ISDS_Port`，不用旧的具体句柄。** 旧 `SDS_EndpointDecision.handle` 类型是 `SDS_EndpointHandle`（旧 `SDS_EndpointContract.cs` 108 行），那是 Vanilla 的实现类型；Contracts 不能引用实现（《ModuleRules.zh-CN.md》6.1），故改为接口，只保留判定与排序需要的成员（本目录 `Interfaces.zh-CN.md`）。同一取舍也体现在 `SDS_WithdrawPlan.endpoint`。
5. **DTO 减负：只留数据，不留 UI 派生与计算。** 删除项见 §二（旧 `SDS_StockEntry.preserved` / `displayKey`、旧 `SDS_StockVitals.Capture*` 等）；加权平均归 Core 的 `SDS_StockRules`（《ModuleResponsibilities.zh-CN.md》三）。理由：计算属规则，规则不进 Contracts（《ModuleRules.zh-CN.md》6.1）；UI 派生字段也不该成为存档契约。

旧框架自此仅作历史存档，维护本模块无需再读。

## 四、旧存档节点与字段的历史说明

> 下列节点、默认值与读档兜底是存档兼容的历史口径，用于解释这些字段为什么保留；签名与锁定口径以《ModuleInterfaces.zh-CN.md》八、`Interfaces.zh-CN.md` A.8 与 `Rules.zh-CN.md` C6 为准。

| 类型 | 节点（LookMode） | 备注 |
|---|---|---|
| `SDS_StockKey` | `defName`、`stuffName`（Value）；旧节点 `thingDef`、`stuff`（Value，默认 `"null"`，**只读**）；`qualityInt`（Value，默认 -1）；`preserved`（Value，默认 false） | 保存只写新字段（2026-11-16 起停写旧节点）；读档时新字段为空才回退旧节点；读后 `TryResolve`。 |
| `SDS_StockEntry` | `key`（Deep）、`count`（Value，0）、`avgHitPoints`（Value，-1）、`avgRotProgress`（Value，-1） | -1 表示旧档无记录，按满血 / 未腐烂处理。 |
| `SDS_Reservation` | `jobLoadID`（0）、`key`（Deep）、`count`（0）、`pawn`（Reference） | 读档后负 id / 空 key / 失效 pawn 由 Core 审计清理。 |
| `SDS_ReclaimSave` | `policy`（默认 `JobTarget`）、`jobLoadID`、`ownerLoadID`、`tick`、`hand`、`things`（Reference）、`counts`（Value） | `PostLoadInit` 补齐 `counts` 长度。 |
| `SDS_TransportLoadSave` | `containerThingID`、`rowIndex`（-1）、`defName`、`stuffName`、`qualityInt`（-1）、`preserved`、`units` | 不保存任何 `Thing` 引用。 |
| `SDS_SidebarUIState` | `collapsed`（Value）、`scroll`、`hasSelection`、`selectedKey`（Deep）、`selectedPreserved` | `PostLoadInit` 调 `Normalize`：丢空项、负滚动归零、重解析选中键。 |
| ~~`SDS_ProtectedOutput`~~ | ~~`thing`（Reference）、`lordLoadID`、`createdTick`~~ | 已退役（2026-11-16）；新档由统一回收账本持有。 |
| `SDS_Settings` | `gridCapacity`、`capacityPerArray`、`corePower`、`arrayPower`、`portPower`、`excludeGridFromWealth`、`highestStoragePriority`、`forceWeaponApparelVirtual`、`endpointStoragePriority`、`separateByQuality`、`separateByStuff` | 全部 `Scribe_Values`，默认值来自 `SDS_Config`。 |
| `SDS_WithdrawDeliveryPolicyDef` | 无 `ExposeData` | 从 XML 读取 `jobDefName` / `driverType` / `mode`。 |

## 五、UI 状态边界

- 搜索词只活在窗口的 `QuickSearchWidget` 里，不写入 `SDS_SidebarUIState`、不落盘；读档后搜索为空。
- `collapsed` 在分组折叠 / 展开的点击当场写回；`scroll` 在库存滚动区每次绘制结束后写回，搜索词变化时先归零再写回。
- `hasSelection` / `selectedKey` / `selectedPreserved` 在点击行时立即写回；行重建时若选中键被归一或已不存在，也当场写回（后者清空三者）。
- 读档只恢复引用：`PostLoadInit` 的 `Normalize` 只丢空项、负滚动归零、`selectedKey.TryResolve()`；窗口与行 / 成员表等 UI 对象在打开时重建，不从存档反序列化。
