# 模块责任划分

> 定位：全局索引——模块清单与产物、对外可见面、项目结构、旧文件归属。各模块内部如何分工见对应 `SDS.*/README.zh-CN.md` 与 `SDS.*/Interfaces.zh-CN.md`。
> 必须遵守的约束见《ModuleRules.zh-CN.md》；类型与签名见各模块 `Interfaces.zh-CN.md` 与《ModuleInterfaces.zh-CN.md》。

## 一、模块总览

| 模块 | 职责 | 产物 | 文档目录 |
|---|---|---|---|
| `SDS.Contracts` | 纯数据、枚举、DefOf、设置、跨层接口、纯注册表、宿主注入点 | `SDS.Contracts.dll` | [SDS.Contracts/](SDS.Contracts/README.zh-CN.md) |
| `SDS.Diag` | 日志、断言、自检、静态状态注册制、适配器报告中心 | `SDS.Diag.dll` | [SDS.Diag/](SDS.Diag/README.zh-CN.md) |
| `SDS.Core` | 账本与数量口径：账本、账本数据原语（键 / 物化 / vitals）、所有权转移（当前；判定层与流程层已迁 `SDS.Rules`） | `SDS.Core.dll` | [SDS.Core/](SDS.Core/README.zh-CN.md) |
| `SDS.Rules` | 规则与流程：端点决议、账单补料（P2）；流程层（P3）；通道 / 容量 / 可用量、结构事实、芯片端口、端点版本（P4） | `SDS.Rules.dll` | [SDS.Rules/](SDS.Rules/README.zh-CN.md) |
| `SDS.Vanilla` | 原版适配：建筑 / 端口、镜像 / 投影 / 访问点 / 暴露、112 补丁、JobDriver、Thing / Comp、UI、交易 / 编成 / 装载、存档壳、回收账本（`sdsReclaim`）、展示物工厂 / 身份表 | `SDS.Vanilla.dll` | [SDS.Vanilla/](SDS.Vanilla/README.zh-CN.md) |
| `SDS.Api` | 对外五轴门面 + 适配器桥 | `SDS.Api.dll` | [SDS.Api/](SDS.Api/README.zh-CN.md) |
| `SDS.ThirdModCompat` | 第三方 Mod 适配单程序集：5 个 Sdk 工具类 + 9 个适配器 | `SDS.ThirdModCompat.dll` | [SDS.ThirdModCompat/](SDS.ThirdModCompat/README.zh-CN.md) |
| `SDS.Bootstrap` | Mod 入口、设置页、Harmony 安装、宿主注入、适配器扫描、启动顺序 | `SDS.Bootstrap.dll` | [SDS.Bootstrap/](SDS.Bootstrap/README.zh-CN.md) |

依赖方向见《ModuleRules.zh-CN.md》四。

规则落点：Contracts 只放事实与契约（DTO / 接口 / DefOf / 纯注册表），不实现任何"怎么取 / 取多少"；跨模块规则在 `SDS.Rules`（P2 起逐步迁入），原版接触面规则在 Vanilla，Compat 零规则、只调 `SDS_API`。口径类规则的唯一实现地址登记在《RuleIndex.zh-CN.md》「唯一实现登记」，由 `CheckRuleSingleSource.ps1` 机器校验。

## 二、模块公共 API 白名单

| 模块 | 对谁暴露 | 允许的 public API |
|---|---|---|
| `SDS.Contracts` | 所有模块 | 全部数据类型、接口、DefOf、设置、`SDS_Host` |
| `SDS.Diag` | 所有模块 | `SDS_Diag`、`SDS_StaticState`、`ISDSResettable`、`SDS_SelfCheck`、`SDS_CompatReportHub` 与报告 DTO |
| `SDS.Core` | Vanilla / Api / Bootstrap / SDS.Rules（仅 internal 友元） | `SDS_Core`（账本生命周期 / `UsedCapacity` / `SetForbidden` / `ApplyQuality` / `ApplyVitals` / 版本通知 / `AuditReservations` / `RegisterAll`）；`ISDS_LedgerRead` / `ISDS_LedgerHost`；internal 账本写口只经 `InternalsVisibleTo("SDS.Rules")` |
| `SDS.Rules` | Vanilla / Api / Bootstrap | `SDS_Rules`（P2 端点 / 补料；P3 流程层；P4 判定层与结构 / 端点版本）；`SDS_Availability`；`SDS_ImplantUtility`；其余规则类 `internal` |
| `SDS.Vanilla` | Bootstrap（仅宿主装配）/ Defs XML / 存档 | 模块间面：`SDS_Vanilla`（`CreateHostSlots` / `InstallPatches` / `RegisterAll` / 设置页与热键 / `HasGrid`）；引擎面（XML 按名实例化 / Scribe 按类型存档，禁止其它模块引用）：`SDS_GameComponent_Grid`、`SDS_EndpointBuffer`、`Building_SDS_*`、`SDS_Proxy_*`、`JobDriver_SDS_*`、`SDS_Comp*` |
| `SDS.Api` | ThirdModCompat / 第三方 | `SDS_API`；Contracts 的公共类型 |
| `SDS.ThirdModCompat` | Bootstrap（反射扫描） | 无 public 顶层类型：5 个 Sdk 工具类 + 9 个 `ISDSCompatAdapter` 实现全部 `internal`（被依赖 0，Bootstrap 反射发现；测试经登记的 `InternalsVisibleTo("SDS.Tests")` 直引） |
| `SDS.Bootstrap` | RimWorld Mod 发现 | `SDS_Mod` |

白名单的强制规则（未列入即泄漏、Tests 例外、Diag 整体可见性）见《ModuleRules.zh-CN.md》五。

## 三、SDS.Contracts 责任边界

→ [README.zh-CN.md](SDS.Contracts/README.zh-CN.md) / [Interfaces.zh-CN.md](SDS.Contracts/Interfaces.zh-CN.md) / [Rules.zh-CN.md](SDS.Contracts/Rules.zh-CN.md)

## 四、SDS.Diag 责任边界

→ [README.zh-CN.md](SDS.Diag/README.zh-CN.md) / [Interfaces.zh-CN.md](SDS.Diag/Interfaces.zh-CN.md) / [Rules.zh-CN.md](SDS.Diag/Rules.zh-CN.md)

## 五、SDS.Core 责任边界

→ [README.zh-CN.md](SDS.Core/README.zh-CN.md) / [Interfaces.zh-CN.md](SDS.Core/Interfaces.zh-CN.md) / [Rules.zh-CN.md](SDS.Core/Rules.zh-CN.md)

## 六、SDS.Vanilla 责任边界

→ [README.zh-CN.md](SDS.Vanilla/README.zh-CN.md) / [Interfaces.zh-CN.md](SDS.Vanilla/Interfaces.zh-CN.md) / [Rules.zh-CN.md](SDS.Vanilla/Rules.zh-CN.md)

## 七、SDS.Api 责任边界

→ [README.zh-CN.md](SDS.Api/README.zh-CN.md) / [Interfaces.zh-CN.md](SDS.Api/Interfaces.zh-CN.md) / [Rules.zh-CN.md](SDS.Api/Rules.zh-CN.md)

## 八、SDS.ThirdModCompat 责任边界

→ [README.zh-CN.md](SDS.ThirdModCompat/README.zh-CN.md) / [Interfaces.zh-CN.md](SDS.ThirdModCompat/Interfaces.zh-CN.md) / [Rules.zh-CN.md](SDS.ThirdModCompat/Rules.zh-CN.md)

## 九、SDS.Bootstrap 责任边界

→ [README.zh-CN.md](SDS.Bootstrap/README.zh-CN.md) / [Interfaces.zh-CN.md](SDS.Bootstrap/Interfaces.zh-CN.md) / [Rules.zh-CN.md](SDS.Bootstrap/Rules.zh-CN.md)

## 十、项目结构

```text
超维存储/Source/                     ← 全部新代码（唯一源码树）
  SDS.sln
  Directory.Build.props
  SDS.Contracts/SDS.Contracts.csproj
  SDS.Diag/SDS.Diag.csproj
  SDS.Core/SDS.Core.csproj
  SDS.Rules/SDS.Rules.csproj
  SDS.Vanilla/SDS.Vanilla.csproj
  SDS.Api/SDS.Api.csproj
  SDS.ThirdModCompat/SDS.ThirdModCompat.csproj
  SDS.Bootstrap/SDS.Bootstrap.csproj
  SDS.Tests/SDS.Tests.csproj
  SDS.Core.Tests/SDS.Core.Tests.csproj
  SDS.Rules.Tests/SDS.Rules.Tests.csproj
  Tools/Check*.ps1
Framework/docs/                         ← 设计文档与登记数据（不放代码）
  DesignPrinciples.zh-CN.md  ModuleRules.zh-CN.md  ModuleResponsibilities.zh-CN.md  ModuleInterfaces.zh-CN.md  DataFlow.zh-CN.md
  RuleIndex.zh-CN.md  PatchInventory.zh-CN.md  PublicAPI.zh-CN.md  DiagnosticsCoverage.zh-CN.md
  SDS.Contracts/   SDS.Diag/   SDS.Core/   SDS.Rules/   SDS.Api/
  SDS.Vanilla/     SDS.ThirdModCompat/    SDS.Bootstrap/
旧框架/                              ← 纯历史存档（2026-11-12 起）：校验脚本与实现 / 评审均不读取
```

每个 `SDS.*/` 目录固定 4 份：`README.zh-CN.md` / `Interfaces.zh-CN.md` / `Rules.zh-CN.md` / `DesignDecisions.zh-CN.md`（`SDS.Vanilla` 另有 `SplitPlan.zh-CN.md`）。

- 源码与 `.csproj` 同层放在 `超维存储/Source/<模块>/`；工程目录名 = 程序集名 = `SDS.*`。
- `旧框架/` 任何文件不得修改、移动、删除，新代码不得写入。
- 每个模块独立 `.csproj`，可单独 `dotnet build`。
- `SDS.Tests` 是允许引用全部模块的集成测试项目；`SDS.Core.Tests` 只引用 Contracts / Diag / Core（只测账本），`SDS.Rules.Tests` 引用 Contracts / Diag / Core / Rules（规则 / 流程）；三者都只进测试输出。
- 各模块统一输出到 `超维存储/Assemblies`；不保留旧 `SuperDimensionalStorage.dll`。

## 十一、旧文件归属（完整性核对）

本表只做「旧文件 → 新模块」归属核对，不是迁移顺序。一个文件可拆到多个模块；段级落点随旧框架归档不再维护，本文只保留文件级归属。

| 模块 | 旧文件 |
|---|---|
| Contracts | `SDS_Stock.cs`（key / entry / vitals / reservation）、`SDS_Withdraw.cs`（枚举 / DTO）、`SDS_EndpointContract.cs`（query / decision DTO）、`SDS_API.cs`（caller / result / stock info）、`SDS_Mod.cs`（settings / config）、`SDS_CaravanTransport.cs`（`SDS_TransportLoadSave`）、`SDS_Implants.cs`（DefOf）、`SDS_UI.cs`（`SDS_SidebarUIState`）、`SDS_Search.cs`（reclaim / return 枚举） |
| Diag | `SDS_SelfCheck.cs`（provider 注册表 + 自检入口 / 出口）、`SDS_Invariants.cs`（E2 开发期断言）、`SDS_StaticState.cs`、`SDS_CompatReports.cs`、`SDS_Mod.cs`（`SDS_Diag` 片段） |
| Core | `SDS_GameComponent_Grid.cs`（账本）、`SDS_Stock.cs`（规则）、`SDS_Withdraw.cs`、`SDS_ReturnAndReclaim.cs`（回充段）、`SDS_EndpointContract.cs`（结构事实段）、`SDS_Buildings.cs`（结构注册表段）、`SDS_Implants.cs`（端口状态）、`SDS_SelfCheck.cs`（账本 / 存入规则自检片段） |
| Rules | `SDS_EndpointContract.cs`（硬门 / 排序段）、`SDS_BillIngredientQuery.cs`（补料规划段）、`SDS_Buildings.cs`（`SDS_EndpointResolver` 段） |
| Vanilla | `Patch_SDS_Core.cs`（现名 `Patch_VanillaSeams.cs`）、`SDS_AccessPoints.cs`、`SDS_BillIngredientQuery.cs`（缺料查询段）、`SDS_Buildings.cs`（建筑 / 缓冲 / 句柄）、`SDS_CaravanTransport.cs`、`SDS_Comp_DrawExtraOverlay.cs`、`SDS_Destinations.cs`、`SDS_GameComponent_Grid.cs`（壳 / 状态 / 镜像）、`SDS_ReturnAndReclaim.cs`（回收段）、`SDS_VirtualDisplay.cs`（展示物工厂 / 身份表）、`SDS_Hotkeys.cs`、`SDS_Implants.cs`（补丁 / JobDriver）、`SDS_InstallDesignator.cs`、`SDS_JobTargets.cs`、`SDS_MirrorBehavior.cs`、`SDS_MirrorGuardRegistry.cs`、`SDS_Mirrors.cs`、`SDS_PreservedBill.cs`、`SDS_PreservedOffer.cs`、`SDS_PreservedUsage.cs`、`SDS_ProjectionOwnerBridge.cs`、`SDS_Proxies.cs`、`SDS_Search.cs`、`SDS_Selection.cs`、`SDS_Transmission.cs`、`SDS_Trade.cs`、`SDS_UI.cs`、`SDS_Use.cs`、`SDS_VirtualBillFallback.cs`、`SDS_Mod.cs`（`SDS_ResearchUtility` 片段）、`SDS_SelfCheck.cs`（DebugAction / 镜像 / 金库 / 工作类型枚举检查片段）、`SDS_VanillaDiagnostics.cs` |
| Api | `SDS_API.cs`、`SDS_API_Adapters.cs` |
| ThirdModCompat | `SDS_CompatServices.cs`（Sdk 工具类）、`SDS_ModCompat_PUAH.cs`、`SDS_ModCompat_CE.cs`、`SDS_ModCompat_UFRecipes.cs`、`SDS_ModCompat_ManipulatorBeam.cs`、`SDS_ModCompat_MiliraExpanded.cs`、`SDS_ModCompat_CeleTechShuttle.cs`、`SDS_ModCompat_EquivalentExchange.cs` 及各自 `SDS_CompatAdapter_*.cs` |
| Bootstrap | `SDS_Mod.cs`（Mod / 设置页）、`SDS_ModCompat.cs`（改为扫描器） |

