# SDS.ThirdModCompat 规则

> 定位：本模块可执行、可检查的规则清单。全局约束见《ModuleRules.zh-CN.md》6.6 / 七 / 八；职责见《ModuleResponsibilities.zh-CN.md》八；签名见《Interfaces.zh-CN.md》。每条只写「要求 + 检查方式」，不重复解释原因。

## 一、规则清单

### R1 禁止硬引用目标程序集

- 要求：`SDS.ThirdModCompat.csproj` 的 `ProjectReference` 只有 `SDS.Contracts` / `SDS.Diag` / `SDS.Api`；Harmony 由 `SdsHarmonyRef` 引入；不得出现任何目标 Mod 的 dll 或 `Reference`（RimWorld / Unity 基础程序集除外）。
- 检查：`Tools/CheckAssemblyBoundaries.ps1`（引用图 + Harmony 引用白名单）；`dotnet build SDS.ThirdModCompat.csproj` 在未安装目标 Mod 的机器上必须成功；人工查看 csproj。
- 失败表现：脚本报 `[引用越界]` / 构建需要第三方 dll。

### R2 只经 `SDS_API` 读写

- 要求：适配器的所有库存 / 端点 / 取货 / 存入 / 回滚都只调 `SDS_API`（或经 `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply` 转发）；不得引用 `SDS.Core` / `SDS.Vanilla` 实现或 internal 类型，不得直接碰账本、事务、镜像 / 展示物注册表。`SDS_CompatTools` 只做反射与 Harmony，不读写游戏状态。
- 检查：`Tools/CheckCompatSurface.ps1`（入口归属 + 调用链 + 兼容层调用图）；`Tools/CheckPatchInventory.ps1` 的兼容层 allowlist（只允许 `SDS_API` / `SDS_Compat*` / `SDS_ModCompat*`）；`CompatAdapterTests.ApiBridge_ForwardsDisplayMirrorAndProducedFacts`。
- 失败表现：脚本报 `[调用链门面未知]` / `[兼容直调]`；测试引用失败。

### R3 不得调用 `SDS_Diag.Fail`

- 要求：失败路径只允许 `SDS_Diag.ErrorOnce` / `SDS_Diag.Warn` / `SDS_CompatReportHub`；正常拒绝静默。
- 检查：`CompatAdapterTests.CompatSources_DoNotCallDiagFail`（扫描 `SDS_ModCompat_*.cs`）；补充 `grep -n "SDS_Diag.Fail" SDS.ThirdModCompat` 必须为 0 行（Sdk 工具类同样适用）。
- 说明：`CheckCompatSurface.ps1` 当前不扫描该字符串，以测试 + grep 为准。

### R4 名称与 Harmony ID 唯一

- 要求：9 个适配器的 `Name`、`TargetPackageId`、`HarmonyId` 互不重复，且 `HarmonyId` 与《ModuleRules.zh-CN.md》八一致；新增适配器必须同时满足。
- 检查：`CompatAdapterTests.AdapterIdentity_MatchesContract` + `HarmonyIds_AreUniqueAndMatchContract`；运行时 `SDS_CompatLoader` 对同名不同类型记 `duplicates++` + `ErrorOnce` 并跳过后注册者。
- 失败表现：测试断言失败；启动日志出现「适配器名称（Harmony ID）重复」。

### R5 `Apply` 只执行一次

- 要求：每个适配器用私有静态 `applied` 做幂等；`Apply` 内只创建一次 `new Harmony(HarmonyId)`；Bootstrap 复扫不得重复应用。
- 检查：人工审阅 9 个 `SDS_CompatAdapter_*.cs` 的 `applied` 分支；`SDS_CompatLoader.Process` 的 `attempted` 集合（先登记再执行，异常不重试）；启动日志 `AppliedCount` 不得大于 9。
- 待补：当前没有专门测试 `Apply` 幂等，可在 Tests 中反射调用两次并断言挂点数不变。

### R6 适配器互不调用

- 要求：`SDS_ModCompat_*` 之间不得相互引用 / 调用；共享逻辑只允许放进 `SDS_CompatTools` / `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply`。
- 检查：`grep -n "SDS_ModCompat_[A-Za-z]*\." SDS.ThirdModCompat/SDS_ModCompat_*.cs`，命中只允许是自身类型名；`Tools/CheckCompatSurface.ps1` 的基础设施类白名单（`SDS_CompatServices.cs` 只允许 4 个类）。
- 失败表现：出现跨适配器调用；脚本报 `[基础设施多类]`。

### R7 静态清理只登记 CE 与 Milira

- 要求：只有 `SDS_CompatAdapter_CE` 与 `SDS_CompatAdapter_MiliraExpanded` 实现 `ISDSResettable`，且只在 `Apply` 成功后 `SDS_StaticState.Register(this)`；只清跨档缓存，进程级反射常量不登记。
- 检查：`CompatAdapterTests.CeAndMilira_DeclareStaticCleanup`；人工确认其它 6 个适配器没有 `ISDSResettable` / `SDS_StaticState.Register`；启动日志「静态清理登记 N」。
- 失败表现：测试断言失败；`SDS_StaticState` 多出第三方项。

### R8 动态挂点不计入 112

- 要求：本模块不得出现 `[HarmonyPatch]` 特性类；9 个适配器的挂点全部走运行期 `harmony.Patch`，只登记在《PatchInventory.zh-CN.md》「动态补丁」节。
- 检查：`Tools/CheckPatchInventory.ps1`（Vanilla 特性类固定 112；每个启用 `TryPatch` 的 `SDS_ModCompat_*.cs` 必须有动态登记行）；`grep -n "\[HarmonyPatch" SDS.ThirdModCompat` 必须为 0 行。
- 失败表现：脚本报 `[动态缺行]` / 特性类计数偏移。

### R9 异常隔离口径

- 要求：单个适配器的实例化 / 身份读取 / 目标检测 / `Apply` / 挂点应用失败都只影响自己：`SDS_CompatLoader` 与 `SDS_CompatTools.TryPatch` 各自 try/catch，失败记 `ErrorOnce` 并继续；不得抛出到 Bootstrap，不得因此阻止 Mod 启动。
- 检查：人工审阅 `SDS_CompatLoader.Process`、`SDS_CompatTools.TryPatch` 与 9 个 `TryApply` 的 try/catch 边界；启动报告应出现「已检测到，挂点 0/N（原因）」而不是启动异常。
- 待补：当前无自动化「单适配器异常不影响其它适配器」测试；可用伪造适配器补充。

### R10 新增适配器步骤

1. 新建 `SDS_CompatAdapter_<Name>.cs`（`public sealed`，实现 `ISDSCompatAdapter`）与 `SDS_ModCompat_<Name>.cs`（`internal static`，只放挂点与翻译）。
2. `Name` 与 `HarmonyId` 取唯一值，`HarmonyId` 形如 `sizuru.sds.compat.<name>`；`TargetPackageId` 用目标 Mod 的真实 packageId；`IsTargetLoaded` 只按 `AccessTools.TypeByName` 探测。
3. `Apply` 用 `applied` 幂等，`new Harmony(HarmonyId)`，挂点统一走 `SDS_CompatTools.TryPatch`；有跨档缓存才实现 `ISDSResettable` 并登记。
4. 只经 `SDS_API`；能复用 `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply` 时不新写助手；不得在适配器里算缺口 / 选端点 / 扣账。
5. 在 `SDS_CompatReportHub.Register` 登记 `name` / `packageId` / `detected` / `report` / `note` / `reason`；挂点逐条 `Applied` / `Missing`。
6. 跑校验：`CompatAdapterTests`（适配器总数、身份、唯一性、清理声明、不调 `SDS_Diag.Fail`）、`Tools/CheckCompatSurface.ps1`、`Tools/CheckPatchInventory.ps1`、`Tools/CheckAssemblyBoundaries.ps1`；同步更新《Interfaces.zh-CN.md》B 节与「第三方支持」节、《PatchInventory.zh-CN.md》动态节。

### R11 适配器只做转译（W17）

- 规范句：适配器只做 Intent → Query 转译和只读委托查询；`SDS_WithdrawPlan` 由 `Decide(query)` 生成；不得自行选端点、决定可用数量、直读 `preservedOwner`、创建事务或预留。
- 检查：人工审阅各 `SDS_ModCompat_*.cs` 的取货调用链；`CheckCompatSurface.ps1` 的调用链 / 入口表保证每个适配器只经 `SDS_API` 门面。
- 登记：`RuleIndex.zh-CN.md` W17 行。

## 二、校验命令

| 脚本 / 测试 | 覆盖规则 |
|---|---|
| `Tools/CheckAssemblyBoundaries.ps1` | R1、R10（public 白名单另行约束入口类型） |
| `Tools/CheckCompatSurface.ps1` | R2、R6（基础设施类白名单）、R10 |
| `Tools/CheckPatchInventory.ps1` | R2、R8、R10 |
| `SDS.Tests/Compat/CompatAdapterTests.cs` | R3、R4、R7、R10 |

## 三、来源

- 规则本体：`Framework/docs/ModuleRules.zh-CN.md` 二.3 / 四 / 六.6 / 七 / 八 / 九；`ModuleResponsibilities.zh-CN.md` 八。
- 代码事实：`SDS.ThirdModCompat.csproj`、`SDS_CompatServices.cs`、9 个 `SDS_CompatAdapter_*.cs`、`SDS_CompatLoader.cs`。
- 脚本事实：`超维存储/Source/Tools/Check*.ps1`；测试：`SDS.Tests/Compat/CompatAdapterTests.cs`。

## 四、规则正文（权威）

> 以下为《DesignDecisions.zh-CN.md》原「相关规则 ID」表中本文件尚未覆盖的规范句，原样迁入，与 R1–R11 同等效力。E6 / E1 / W17 的规范句已分别由 R1–R2 / R7 / R11 覆盖，不再重复。

| ID | 规范句 | 本模块落点 |
|---|---|---|
| E7 | 每个第三方入口只有一条写接管路径 | 每个适配器只保留一个写出口；只读投影可与翻译共存 |
| W50 | 第三方自建装载清单的库存可见性只能由清单构建处的展示物注入提供 | CeleTech 只调 `InjectTransportLoadCandidates` |
| S16 | 第三方补料只补缺口；选 def / required / shortfall / 预留只在 `SDS_BillSupplyPlanner` | UFRecipes、Milira 只提供事实并执行取货 / 存入 |
| W30 / W58 | 第三方整备 Job 不再注册取量；数量缺失由本 Mod 统一按 Job 对镜像的预留意图补齐 | 适配器不得再提供数量；`SDS_JobFuelCount` / `RegisterJobFuelCountProvider` 已删除 |
| E5 | Version 变更即兼容性声明 | `SDS_API.Version = 4`（程序集改名，第三方须重新 soft-depend） |
| W33 | 武器 / 装备强制虚拟化开关会改变 `RequiresPreservedStorage` 结果 | Milira 的 `canStoreCache` 依赖该结果 |

> CE 补弹口径（实现事实，不新增规则 ID）：`TryFindSupplyAmmo` 供数 = `MissingToFullMagazine ∩ stackLimit`（优先 `SelectedAmmo`，未征召允许 `CurAmmoSet` 回退）；本趟实际携带由原版 `TakeInventory` / CE 自身换弹链夹取，适配器不夹携带量、不判可用量（R11）。

## 五、兼容边界（冻结，2026-11-16）

> 本节是「本 Mod 与第三方通用兼容」的最终边界；新增需求必须先按 E3 重新裁定，不得在适配器里临时开口子。

1. 显式要求 1 件的第三方（`count = 1` / `SplitOff(1)` / 逐 `Thing` 迭代）**不做数量干预**；本 Mod 只保证每次取 1 件与真实仓库一致。
2. 只覆盖走原版接缝的行为：`SplitOff` / `TryStartCarry` / `TakeToInventory` / `ThingOwner` / 原版 driver `Notify_Starting` / `Toils_Haul.ErrorCheckForCarry` / `ReservationManager` 预留。
3. 不走上述接缝、直接对镜像 `Destroy` 的第三方：**fail-closed（拒绝 + 告警）**，不支持；`ThingOwner.Remove`（无返回值）同样拒绝，不支持。
4. 不为单个 Mod 新增适配器；除非另有一次 L2 裁定并登记影响面。
5. 第三方若在取货时 `job.count ≤ 0` 且已对镜像预留（`Reservation.StackCount`，-1 = 整堆），由 W58 统一补齐；第三方不需要、也不得再注册数量。
