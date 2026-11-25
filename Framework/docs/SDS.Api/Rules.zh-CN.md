> 定位：把 SDS.Api 必须遵守的约束写成可检查条目；规则本体以《ModuleRules.zh-CN.md》与冻结基线为准，本文件不复述玩法语义。

# SDS.Api 规则

## 一、可检查条目

| 编号 | 规则 | 具体要求 | 检查方式 | 依据 |
|---|---|---|---|---|
| API-1 | 依赖边界 | 只引用 `SDS.Contracts`、`SDS.Diag`、`SDS.Core`；禁止 `SDS.Vanilla`、`0Harmony`、`SDS.ThirdModCompat` | `CheckAssemblyBoundaries.ps1` 引用图；`SDS.Api.csproj` 只应有 3 个 `ProjectReference`；对 `SDS.Api/*.cs` 搜 `SDS.Vanilla` / `Harmony` / `SDS.ThirdModCompat` 应为 0 | 《ModuleRules.zh-CN.md》四、6.5 |
| API-2 | 不写规则 | 不实现库存、容量、筛选、端点排序、事务、扣账、预留；只组装查询 DTO 并转发 | 人工审阅 + 搜索 `SDS.Api` 是否出现 `Capacity` / `Filter` / `Reserved` / `TryWithdrawTransaction` 等实现符号；`SDS.Api` 只应调用 `SDS_Core` 公开成员 | 《ModuleRules.zh-CN.md》6.5；《ModuleResponsibilities.zh-CN.md》七 |
| API-3 | 调用门与规则门分工 | 玩家阵营、所有权、禁止、同地图只在 Api 判定；库存 / 容量 / 筛选 / 排序由 Core 判定 | 测试 `ApiTests.DepositAt_NonPlayerFaction_IsRejected`、`DepositNow_EndToEnd_WritesLedger`；检查 `ValidateDepositCall` / `ValidateWithdrawCall` 只含这四类判定 | 《ModuleResponsibilities.zh-CN.md》七；《PublicAPI.zh-CN.md》§3.4 |
| API-4 | `ReadStockFor` 不复制分流 | 通道判定只调 `SDS_Rules.TryGetStockKey`；不得出现 `RequiresPreservedStorage` 或 `SDS_StockKey.From...` 的分流 | 测试 `ApiTests.ReadStockFor_RoutesByCoreChannel`；grep `SDS_API.cs` 中 `RequiresPreservedStorage` 应为 0 | 《ModuleResponsibilities.zh-CN.md》七；《ModuleInterfaces.zh-CN.md》3.2 |
| API-5 | `TryWithdraw` 只虚拟 | `key == null`、`quantity <= 0`、`VirtualHandoff`、`key.preserved` 一律 `InvalidArgument`；保真只走 `TryWithdrawPreservedExact` | 测试 `ApiTests.Withdraw_PreservedKey_IsInvalidArgument`；grep `key.preserved` 分支存在且返回 `InvalidArgument` | 《ModuleResponsibilities.zh-CN.md》七；《PublicAPI.zh-CN.md》§二·五 |
| API-6 | 桥不新增规则 | 桥方法只转发 Core / Host；按 def 批量取只能是同一事务原语的逐键组合与整批回充；不得新增排序、容量、扣账、派活 | 人工审阅 + `CheckCompatSurface.ps1 -Report` 调用图；桥内不得出现新的候选排序或数量口径；`ApiTests` / `CompatSdkTests` 覆盖转发行为 | 《ModuleInterfaces.zh-CN.md》五；《ModuleRules.zh-CN.md》6.6 |
| API-7 | Version 语义 | `Version = 4` 只声明五轴兼容性；五轴签名或语义变化才改版本；桥方法增删不改版本 | 测试 `ApiTests.Version_IsFour`；变更时按 E5 在《ModuleInterfaces.zh-CN.md》五与版本记录中标注影响面 | 《PublicAPI.zh-CN.md》§3.8；《RuleIndex.zh-CN.md》E5 |
| API-8 | 日志门 | Api 只允许 `SDS_Diag.ErrorOnce`；不得调用 `SDS_Diag.Fail`（《ModuleInterfaces.zh-CN.md》二把 `Fail` 限定为 Core / Rules / Vanilla，Api 未列入调用方，ThirdModCompat 亦禁止） | grep `SDS_Diag.Fail` 在 `SDS.Api` 应为 0；对照《ModuleInterfaces.zh-CN.md》二的调用方白名单 | 《ModuleInterfaces.zh-CN.md》二；《ModuleRules.zh-CN.md》6.2 |
| API-9 | `Find.*` 唯一例外 | 只允许 `Find.TickManager.TicksGame`；不得引入 `Find.CurrentMap`、`Find.Maps`、`GenClosest`、`ListerThings`、`GenPlace`、`GenSpawn` | grep `Find\.` 在 `SDS.Api` 应只命中 `CurrentTick()`；其余符号应为 0 | 《ModuleResponsibilities.zh-CN.md》七；《ModuleRules.zh-CN.md》6.5 |
| API-10 | 公共面白名单 | 只暴露 `SDS_API`；新增 public 桥方法必须同步登记到《ModuleInterfaces.zh-CN.md》五，否则视为泄漏 | `CheckAssemblyBoundaries.ps1` 类型级白名单；公共方法清单与《ModuleInterfaces.zh-CN.md》五逐项对账 | 《ModuleRules.zh-CN.md》五；《ModuleResponsibilities.zh-CN.md》二 |
| API-11 | 零补丁 | Api 不安装任何 Harmony 补丁；补丁只在 Vanilla / ThirdModCompat | `CheckPatchInventory.ps1`；grep `[HarmonyPatch]` / `Harmony` 在 `SDS.Api` 应为 0 | 《ModuleRules.zh-CN.md》七、八 |

## 二、调用门顺序（检查用）

存入（`ValidateDepositCall`，`TryFindDepositEndpoint` / `TryDepositAt` 共用，`SDS_API.cs`）：

1. 实物非空、未销毁、`stackCount > 0`；
2. 调用方阵营为玩家阵营（`IsPlayerFaction`）；
3. `SDS_Rules.Available`；
4. 实物与根持有者归调用方（`IsOwnedByCaller`）；
5. 实物未被禁止（`ForbidUtility.IsForbidden`）；
6. `SDS_Rules.CanDeposit`；
7. 实物地图与传入地图一致。

取出（`ValidateWithdrawCall`）只含第 2、3 步；Pawn 有效性由各取货方法追加（`TryWithdraw` / `TryWithdrawFromMirror`）。

## 三、审查口径

- 正常拒绝不写日志；异常统一 `InternalError`（或空集 / `default`）并记一次 `公共API/*异常`（《PublicAPI.zh-CN.md》§3.6）。
- 任何「先算一下可用量 / 缺口 / 优先级」的改动必须回到 Core；Api 只做结果码映射（《DesignDecisions.zh-CN.md》四）。
- 新增桥方法必须同时更新《ModuleInterfaces.zh-CN.md》五与白名单，否则 `CheckAssemblyBoundaries.ps1` 记为泄漏（《DesignDecisions.zh-CN.md》四）。
- 无法由脚本证明的条目（API-2、API-6）按人工审阅记录，不得写成「已校验」（《RuleIndex.zh-CN.md》约定）。

## 四、规则正文（权威）

> 规范句原样承接自原「相关规则 ID」表；E5 与 API-7 同源。§三 中两处「5.6」对应《DesignDecisions.zh-CN.md》四、待补。

| ID | 规范句（权威） | 本模块落点 |
|---|---|---|
| E5 | Version 变更即兼容性声明；无外部采用可直接 break；有采用需保留薄转发至少一个大版本 | `Version = 4` 的唯一变更依据 |
| E6 | 兼容层只允许 `SDS_API` / `SDS_Compat*` / `SDS_Diag` 与公共 API 类型 | 桥公开的直接原因；Api 是兼容层唯一写入口 |
| E7 | 每个第三方入口只有一条写接管路径；只读投影可与适配器翻译共存 | 桥不得新增第二条扣账 / 派活 / 回滚路径 |
| R17 | G 关闭时读侧可用量归零，账本数据保留 | `ReadStock` 的 `GridUnavailable` 分支 |
| S16 | 补料缺口只在 `SDS_BillSupplyPlanner` 实现，适配器只提供事实并执行 | `TryPlanBillSupplySlot` / `TryPlanAllowedDefSupply` 只转发 |
| W30 / W58 | 直接消费 Job 默认 Hand 不物化；数量缺失按 Job 对镜像的预留意图补齐，不再由 Api 提供整备数量注册 | `RegisterJobFuelCountProvider` 已于 2026-11-16 删除；`SDS_API` 不再承载该桥 |
| W33 / W35 | 武器衣物强制虚拟化、`IsStudiable` 一律保真；调用方必须按查询结果走通道 | `ReadStockFor` / `RequiresPreservedStorage` 只转发 Core |
| W50 | 第三方装载清单只能经 `SDS_API` 内部桥注入 | `InjectTransportLoadCandidates` 桥 |

（来源：`RuleIndex.zh-CN.md` 对应行。）

## 五、补充口径

- 适配器 / API 不做跨对话框预留：预留会让清单可见量下降，出现「有货却变灰」；只在交货或装车时刻结算。
- 规划阶段只提供「可用量」事实，跨对话框期间不占量、不回写预留；结算仍由唯一执行器完成。
