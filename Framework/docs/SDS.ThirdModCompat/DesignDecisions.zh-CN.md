# SDS.ThirdModCompat · 设计决策（为什么这么做 / 踩过的坑）

> 定位：记录本模块从旧 9 个项目收编为单程序集以来的历史决策、踩过的坑与设计取舍。只作存档与追溯；维护本模块不需要回溯旧代码，逐挂点事实以《Interfaces.zh-CN.md》§F.2 为准。数字均按源码可核实口径，无法核实的写「（待补）」。

## 一、历史决策与坑

| 日期 / 项 | 决策或坑 | 现状 |
|---|---|---|
| 2026-11-12 收编 | 9 个 dll 合并为 `SDS.ThirdModCompat.dll`；部署只多一个文件，任一适配器改动都会让整 dll 版本前进 | 已落地 |
| 每程序集一实现 → 名称唯一 | 失去程序集边界后，只能靠 `Name` / Harmony ID 发现重复；同名不同类型记 Error 并跳过后注册者 | `SDS_CompatLoader` + `CompatAdapterTests` |
| 共享 Harmony → 各自 Harmony | 旧 `SDS_ModCompat.cs` 用一个 Harmony 无条件 `TryApplyAll`；新 `Apply` 按目标是否加载逐适配器创建 | 已落地 |
| Sdk 工具类可见性 | 旧 `SDS_CompatTools` 等 4 类为 `internal`（Sdk 程序集内）；收编后提升为 `public`，由 public 白名单约束 | `CheckAssemblyBoundaries.ps1` |
| `SDS_StockKey.FromThing` | 旧 EE / Milira 直接由实物反推键；新 API 不再暴露该入口，统一改 `SDS_API.TryGetStockKey` | 已替换 |
| CE 保真探针 | 旧 `SDS_Stock.HasInstanceNonKeyState` 内联调 `HasLoadedAmmoState`；新由 `SDS_CompatAdapter_CE.Apply` 调 `SDS_PreserveHooks.Register`，Core 统一消费 | 已落地 |
| `TryReturnPreserved` 签名 | 旧签名带 `IntVec3`；上一版曾收窄为 `(Thing, Map)`；2026-11-12 已恢复 `(Thing, Map, IntVec3)`，Milira 统一传 `IntVec3.Invalid` | 已恢复 |
| `AllowInjection` 调用链 | 旧文档称由 `Patch_SDS_TransporterAddVirtual` 调用；新实现由适配器 `Apply` 登记到 `SDS_CompatGates`，Vanilla 注入补丁统一消费（未登记放行、异常 fail-closed） | 已修复 |
| `ClearStoreCache` 调用点 | 旧 `SDS_Mod.ApplySettings` 与确认回调两处调用；新实现改为 `CanStoreDef` 按开关自失效，并在 `Reset()` 清缓存 | 已修复 |
| EE 动态清单 3 行 | 旧《补丁清单》为 EE 登记 3 行（含 `AddItemsToTransferables` 注入闸门）；前两行是 `TryPatch`，第 3 行是闸门消费，现由 `SDS_CompatGates` 支撑 | 已对齐 |
| 报告中心迁移 | 旧 `SDS_CompatReports.cs` 在兼容层；现归 `SDS.Diag`，只允许主线程写、Bootstrap 读快照 | 已落地 |
| 优先级 | 挂点统一 `Priority.Low`，让第三方原体先执行、适配器兜底；EE 的 `TryAccept` Prefix 例外为 `High` | 已落地 |
| 动态补丁统计 | 第三方挂点不计入 Vanilla 的 112 个特性补丁类；只在《PatchInventory.zh-CN.md》动态节登记 | `CheckPatchInventory.ps1` |

## 二、旧实现 → 新实现的差异与坑

> 本节保留旧实现事实与新旧差异；逐挂点、packageId 与当前注意事项见《Interfaces.zh-CN.md》§F.1 / §F.2，不再重复。

### 2.1 PUAH

旧实现要点：端点胜出时整单替换为本 Mod 多件存入 Job；`HasJobOnThing` 写同 tick 决议缓存，`JobOnThing` 只消费同一决议，扣账仍只发生一次。

### 2.2 CE

旧实现要点：静态缓存 `checkCooldownUntil` / `priorityCacheUntil` / `priorityCacheValue` 按 `thingIDNumber` 存绝对 tick，旧 `SDS_StaticState` 登记 1 项；`pendingAmmo` 系列是前后腿之间的一次性传值槽。

### 2.3 UFRecipes

旧实现要点：选择复用原版 `WorkGiver_DoBill.TryFindBestBillIngredientsInSet`，适配器只提供候选与取出；不足整批回充并返回 false，不留半截。

### 2.4 GlitterworldDestroyer（2026-11-16 已移除）

原适配器只注册剩余需求量；方案乙后数量缺失统一由 W58 处理，适配器与 `SDS_JobFuelCount` 一并删除。

### 2.5 ManipulatorBeam

旧实现要点：旧版三处判定直接检查 `ISDS_StorageEndpoint`（旧本体的端点接口）；新版统一改问 `SDS_API.IsDepositEndpoint`，去掉对 Core / Vanilla 实现的跨层引用（E6）。

### 2.6 MiliraExpanded

旧实现要点：`nextTickByDevice` 登记 `ISDSResettable`；`canStoreCache` 的键是 `ThingDef`、值依赖 W33 开关。旧源码在 W33 确认回调与 `ApplySettings` 两处调 `ClearStoreCache()`；新实现在 `CanStoreDef` 内按 `ForceWeaponApparelVirtual` 自失效，且 `Reset()` 一并清缓存（E1），不再依赖设置回调。

### 2.7 CeleTechShuttle

旧实现要点：无静态缓存；准入 / 冷藏 / 装载 Job / 入舱仍由对方与原版负责。

### 2.8 EquivalentExchange

旧实现要点：旧版回充用 `SDS_StockKey.FromThing(thing)` 直接由实物反推键；新版统一改 `SDS_API.TryGetStockKey(thing, out key)`，避免兼容层依赖旧内部类型。

## 三、设计取舍

> 原《实现原因.md》并入本节：第三方适配为何必须独立于原版（`SDS.Vanilla`）、为何收编为单程序集、Sdk 工具类为何必须存在，以及由此付出的取舍。历史结构见 3.2，已落地决策见第一章，本节不复述；约束本体见《ModuleRules.zh-CN.md》6.6 / 七 / 八。

### 3.1 为什么第三方适配必须独立于原版

| 原因 | 做法 | 源码证据 |
|---|---|---|
| 不能改原版行为 | 9 个适配器的挂点全部落在第三方类型上；仅两处原版方法挂点，且都以「目标 Mod 已加载」为安装前提（见《Interfaces.zh-CN.md》「原版支持」E.1） | `SDS_ModCompat_CE.TryApply` 的 `Resolve()`；`SDS_ModCompat_EquivalentExchange.TryApply` 的 `found` |
| 不能污染 Core 规则 | 适配器只提供事实（自有量 / 外部可用量 / 一个周期用量），不算缺口、不选端点、不扣账 | `SDS_ModCompat_MiliraExpanded` 只调 `SDS_API.TryPlan*`；`SDS_CompatBillSupply` 复用原版选料算法 |
| 目标 Mod 未装时零影响 | `IsTargetLoaded` 只做类型名探测；Bootstrap 不满足时不调用 `Apply`，不创建 Harmony 实例、不产生任何挂点 | `SDS_CompatLoader.Process`（`!targetLoaded` 直接返回） |

- 112 个特性补丁类全部在 `SDS.Vanilla`，本模块没有任何 `[HarmonyPatch]` 类；补丁归属见《ModuleRules.zh-CN.md》七。
- 若把第三方翻译写进 Core：S16 会出现第二份实现、端点排序会出现第三方特例、事务会出现第二条写路径，直接违反 E7。
- 独立程序集的第二个作用是「未被安装的 Mod 不进编译」：`SDS.ThirdModCompat.csproj` 只引用 `SDS.Contracts` / `SDS.Diag` / `SDS.Api` 与 Harmony，没有任何第三方 dll。

### 3.2 为什么收编为单程序集

历史结构是 1 个 `SDS.Compat.Sdk` + 8 个 `SDS.Compat.*` 独立项目，每个程序集恰好一个 `ISDSCompatAdapter`，靠程序集边界天然隔离。收编后的收益与代价：

| 取舍项 | 收编后的收益 | 代价与兜底 |
|---|---|---|
| 部署 | `Assemblies` 只多 `SDS.ThirdModCompat.dll` 一个文件，不再出现 9 个 dll 碎片 | 无 |
| 版本一致 | 9 个适配器同处一个 dll，不会出现「部分适配器新版、部分旧版」 | 任一适配器改动都会让整个 dll 版本前进 |
| 类型唯一 | 不保留旧的 `SuperDimensionalStorage.dll`，避免类型重复与 `GenTypes` 歧义（《ModuleRules.zh-CN.md》四 命名规则） | 可见性校验改由 `CheckAssemblyBoundaries.ps1` 做类型级检查（《ModuleRules.zh-CN.md》五） |
| 适配器隔离 | 适配器之间仍互不调用，只共享 Sdk 工具类（《ModuleRules.zh-CN.md》二.3） | 失去「每程序集一实现」的天然边界，改由名称唯一担保 |

隔离在单程序集内如何维持：

- 每个 `Apply` 内部独立 try/catch，单个适配器失败不影响其余适配器，也不影响 `SDS.Vanilla`。
- 适配器身份从「程序集」变成「`Name` / Harmony ID」：`SDS_CompatLoader` 用 `Name` 建索引，同名不同类型即 `duplicates++` + `ErrorOnce` + 跳过后注册者（对应 3.4 第 3 条与第一章的「每程序集一实现 → 名称唯一」）。
- 每个适配器自己 `new Harmony(HarmonyId)`、按需 `Apply`（旧实现的「一个 Harmony + 无条件全量 `TryApplyAll`」对照见第一章）。

### 3.3 Sdk 工具类为什么必须存在

| 工具类 | 只调用 | 主要使用方 |
|---|---|---|
| `SDS_CompatTools` | Harmony + `SDS_Diag.ErrorOnce` + 报告 DTO | 全部 9 个适配器 |
| `SDS_CompatEndpoint` | `SDS_API` | UFRecipes、Milira |
| `SDS_CompatStock` | `SDS_API` | Milira、UFRecipes（间接） |
| `SDS_CompatBillSupply` | `SDS_API` | UFRecipes |
| `SDS_CompatMirrorSeam` | `SDS_API` | MiliraGift（同形态适配器共用） |

职责与签名见《Interfaces.zh-CN.md》A 节与《README.zh-CN.md》职责表。这些类都必须存在的原因：

- 反射探测要处理「类型不存在 / 成员改名 / 调用抛异常」三种情况，9 个适配器各写一份必然出现不一致的 null 判断与日志口径。
- `TryPatch` 统一了「应用 / 报告 / 异常 / 优先级」四件事（target 为 null → `Missing`；异常 → `Missing` + `ErrorOnce`；成功 → `Applied`），报告中心的挂点计数才有唯一来源。
- 端点、库存、补料都是写路径入口；如果每个适配器自己实现，等于给同一件事造第二套排序或扣账（E6 / E7）。
- 第三方预约前的端点物化只有 `SDS_CompatMirrorSeam` 一份实现；同形态适配器只传描述符，避免复制取货 / 落点 / 回账（2026-11-19 增补）。
- 工具类自身不含规则：不算缺口、不派活、不建事务、不缓存跨档业务状态（《ModuleRules.zh-CN.md》6.6）。

### 3.4 取舍

1. 不硬引用目标程序集
   - 做法：`SDS.ThirdModCompat.csproj` 不含任何第三方 `ProjectReference`。
   - 原因：目标 Mod 是可选的；硬引用会让「未装目标 Mod」变成加载失败，而不是功能缺席。
   - 代价：一切类型与成员都只能按字符串名字访问，对方改名即失效。
2. 用 `AccessTools.TypeByName` 反射探测
   - 做法：每个适配器先 `IsTargetLoaded`，再在 `TryApply` 里 `Resolve()` 一次并缓存 `Type` / `MethodInfo`。
   - 原因：未装、改名、结构变化都退化为「找不到」，可以按挂点上报缺失，而不是启动崩溃。
   - 代价：编译期无校验，只能靠适配器测试与实机 A/B。
3. 名称唯一是唯一防线
   - 做法：单程序集后不再「每程序集一实现」，`SDS_CompatLoader` 用 `adapter.Name` 去重；名称为空回退 `type.FullName`。
   - 原因：没有程序集边界后，重复实现只能被名称发现。
   - 代价：新增适配器必须保证 `Name` 与 Harmony ID 不与既有 9 个冲突；测试 `HarmonyIds_AreUniqueAndMatchContract` 覆盖。
4. 失败只记 `ErrorOnce`，不阻止启动
   - 做法：`TryPatch` 失败只上报缺失；扫描阶段的实例化 / 身份读取 / 目标检测 / `Apply` 异常都只记一次并继续。
   - 原因：兼容层是可选增强，不能因为某个第三方版本变化导致整个 Mod 起不来；`SDS_Diag.Fail` 在本模块被禁止（《ModuleRules.zh-CN.md》6.6）。
   - 代价：功能会静默退化，只能从启动报告与 `SDS_CompatReportHub` 看出「已检测到，挂点 0/N」。
5. 动态补丁不计入 112
   - 做法：9 个适配器全部用运行期 `harmony.Patch`，不写 `[HarmonyPatch]` 特性类。
   - 原因：`SDS_PatchInstaller` 只扫 `SDS.Vanilla`；第三方挂点随目标 Mod 是否存在而变化，不能进固定口径（《ModuleRules.zh-CN.md》七）。
   - 代价：补丁清单看不到这些挂点，只能读各 `TryApply` 的挂点名与 `SDS_CompatPatchReport`。

## 四、边界（本模块有意不做）

- 不引用 Core / Vanilla，不调用其它适配器；需要跨适配器共享时只允许放进 3.3 的 Sdk 工具类。
- 不在工具类或适配器里算缺口、选端点、复制数量口径；这些规则只在 Core / Api。
- 不新增第二条扣账 / 派活 / 回滚路径；所有写操作只经 `SDS_API`。
- 不调用 `SDS_Diag.Fail`；正常拒绝不写日志，异常只 `ErrorOnce`。
- 不接管第三方的 Job / Toil / 表现：CE 只补「物品来源」，CeleTech 只注入装载清单，Manipulator Beam 只补「目的地发现」，ProjectE 只做转化前后的边界翻译。

## 五、来源

- 规则：`Framework/docs/ModuleRules.zh-CN.md` 6.6 / 七 / 八；`ModuleResponsibilities.zh-CN.md` 二；`ModuleInterfaces.zh-CN.md` 一.1.2 / 五 / 六。
- 代码：`超维存储/Source/SDS.ThirdModCompat/SDS_CompatServices.cs`、9 个 `SDS_CompatAdapter_*.cs`、`SDS.ThirdModCompat.csproj`；`SDS.Bootstrap/SDS_CompatLoader.cs`。
- 旧实现对照：见第一章「历史决策与坑」与第二章「旧实现 → 新实现的差异与坑」。
- 测试：`超维存储/Source/SDS.Tests/Compat/CompatAdapterTests.cs`。

本文只作历史存档，维护本模块无需回溯旧实现。
