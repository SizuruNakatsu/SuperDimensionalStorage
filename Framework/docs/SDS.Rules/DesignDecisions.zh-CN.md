# SDS.Rules · 设计决策

> 定位：记录「为什么拆出 Rules」「为什么依赖方向是 Rules → Core」「迁移如何避免双写」。
> 本文件只回答为什么，不定义规则；规则正文见 [Rules.zh-CN.md](Rules.zh-CN.md)，可检查条目见各模块 `Rules.zh-CN.md`。

## 一、为什么把规则从 Core 拆出

- 目标：Core 只做账本，确保**数据可靠性**与**数量口径一致性**；规则（取谁、取多少、走哪条通道、怎么回滚）单独成模块，才能各自独立测试。
- 现状问题：`SDS_Core` 同时承载账本、端点决议、补料规划、芯片端口、在途标记与事务流程，公开面 70+ 成员；「账本出错」与「规则出错」无法在测试与日志上分开定位。
- 拆分后：
  - `SDS.Core`：账本、预留、`total − reserved`、存档、账本自检；不读 `SDS_Host`，不引用 Rules。
  - `SDS.Rules`：玩法 / 口径规则与流程；唯一规则门面 `SDS_Rules`。
  - `SDS.Vanilla`：只保留「如何接原版」的接线约束与实现。

## 二、为什么 Rules 依赖 Core，而不是反向

- 规则必须读账本、写预留，因此 Rules → Core 是自然方向。
- 反向（Core → Rules）会让 Core 持有规则引用，既破坏「纯账本」，也会在移除旧门面时形成环。
- 因此**不设 Core → Rules 的转发器**；P2–P4 每阶段把一组规则整体搬走，并同步切换消费方，靠编译器保证没有遗漏。
- 写账本的物理边界：`SDS_Ledger` 的写方法是 **internal**，Core 通过 `InternalsVisibleTo("SDS.Rules")` 只授予 Rules；Vanilla / Api 无法绕过 Rules 直接写账本（`SetForbidden` 等纯数据开关除外）。不存在独立的 `ISDS_LedgerWrite` 接口。

## 三、迁移顺序

| 阶段 | 先搬什么 | 原因 |
|---|---|---|
| P1 | 工程 / 公开面 / 文档骨架 | 先验证项目图、脚本登记、公开面基线，不碰语义 |
| P2 | 端点决议、账单补料 | 依赖账本写入最少；芯片因依赖结构事实、在途标记因 Core 回充仍调用 `Unmark`，分别顺延 P3 / P4 |
| P3 | 流程层：事务、执行器、回充、在途标记 | 流程依赖判定层，但 Rules 已引用 Core，可先搬；搬完后 Core 不再持有事务状态机与回充落点 |
| P4 | 判定层：通道、容量、可用量、结构事实、芯片 | 判定层最后搬，Core 收窄为纯账本 |

## 四、不做什么

- 不把 Vanilla 的补丁 / Job / UI 约束搬进 Rules：那会迫使 Rules 引用 Harmony / UI，破坏准则一 / 二与依赖矩阵。
- 不为兼容保留 `SDS_Core.SelectEndpoint` 之类的旧入口：规则只定义一次，旧入口在迁移阶段同步删除。
- 不在本拆分中修改存档节点与字段：保真真身仍由 Core 持有，旧档可读性不受影响；类型级「零 RimWorld」是后续独立 L3。

## 五、P2 实际迁移记录

- 已迁入：`SDS_EndpointResolver`（`Select` / `TryEvaluateStaticGate` / `IsBetter`）、`SDS_BillSupplyPlanner`（全部入口）。
- 未迁入且不复制：
  - `SDS_ImplantUtility`：`IsChipOperational` 的 ChipA 需要 Core 的 `MapHasOperationalCore`（结构事实），随 P4 结构迁入一起搬。
  - `SDS_ProducedThingMarker`：P2 时 Core 的 `SDS_ReturnUtility` 仍调用 `Unmark`（W49），随 P3 流程层一起搬。
  - `SDS_EndpointContractCache`：只是端点版本时钟，不是判定；暂留 Core，P4 结构迁入时再评估归属。
- 公开面变化：`SDS_Core` 删除 5 个规则门面，新增 `HasLiveContent` 数据判据；`SDS_Rules` 新增 5 个规则门面。`SDS_API` 签名不变，仅转发目标改为 `SDS_Rules`。
- 测试：`EndpointTests` / `BillMedicalGateTests` 迁到 `SDS.Rules.Tests`；Core 测试当时只引用 Contracts / Diag / Core。

## 六、P3 实际迁移记录

- 已迁入文件：`SDS_WithdrawTransaction`、`SDS_WithdrawExecutor`、`SDS_ReturnUtility`、`SDS_ProducedThingMarker`；新增 `SDS_RulesImpl`（19 个流程门面实现 + `RegisterAll`）。
- `SDS_Rules` 新增公开面：`CanDeposit` / `TryDeposit` / `TryWithdraw` / `TryWithdrawPreservedExact` / `TryWithdrawPreservedExactAdopted` / `TryWithdrawReservedForJob` / `TryReserveForJob` / `TryReservePreservedForJob` / `ReleaseJobReservation` / `TryTakeMirror` / `TryConsumeMirror` / `ConvertPreservedGearToVirtual` / `ReturnThing`（两重载）/ `ReturnVirtualCount` / `ReturnThings` / `IsProducedMarked` / `MarkProduced` / `UnmarkProduced` / `RegisterAll`。
- `SDS_Core` 同步删除上述 19 个门面；`FindWithdrawableKey` 由 private 收为 internal（供 Rules 选键）；`RegisterAll` 从 4 项降为 3 项，在途标记清理由 `SDS_Rules.RegisterAll` 登记。
- 新增 `SDS.Core/Properties/AssemblyInfo.cs`：`InternalsVisibleTo("SDS.Rules")`；登记在 `CheckAssemblyBoundaries.ps1` §5。
- 消费方同一阶段切换：Api / Vanilla / Bootstrap / 测试全部改调 `SDS_Rules`；测试引用在 P5 按模块拆分。
- 为什么先搬流程层：流程层的边界（事务状态机、回充落点、在途标记生命周期）最容易在 Core/Rules 之间飘移；先固定「谁编排」，P4 搬判定层时只替换被调用方，不再改调用方向。

## 七、P4 实际迁移记录

- 已迁入文件：`SDS_StockRules`、`SDS_Availability`、`SDS_StructureRegistry`、`SDS_ImplantUtility`、`SDS_EndpointContractCache`；公开类 `SDS_Availability` / `SDS_ImplantUtility` 一并迁入 `SDS.Rules_API.cs`。
- 新增 `SDS_StockData`（Core，internal）：键构造 / 品类归属键 / 可存基础判据 / 堆平均状态 / 物化 / 状态写回。账本只依赖这份数据原语，不再依赖规则判定。
- `SDS_RulesImpl` 新增判定层实现：`Available` / `TypeCapacity` / `EndpointVersion` / `TryGetStockKey` / `RequiresPreservedStorage` / `IsLegacyUnrebuildableDef` / `HasLiveContent` / `CategoryKeyOf` / `TryFind*` / `GetAvailableCountForDef` / `SetExternalPreserveProvider` / 结构登记与端点版本 / `MapHasOperationalCore` / `FindWithdrawableKey`。
- W34 机制与判据分离：`SDS_Ledger.ConvertPreservedGearToVirtual(Func<Thing,bool>)` 只做账本机制；「哪件可迁移」由 Rules 的 `SDS_StockRules.CanMigratePreservedGearToVirtual` 注入，Core 不反向引用 Rules。
- 静态清理：`SDS_Core.RegisterAll` 只登记 Core 自检；4 个清理项与 `SDS_RulesDiagnostics` 由 `SDS_Rules.RegisterAll` 登记。Core 诊断只保留虚拟可用量 / 真身失效 / 账本不变量。
- `SDS_Core` 公开面收窄为：`Ledger` / `CreateLedger` / `Attach` / `UsedCapacity` / `SetForbidden` / `ApplyQuality` / `ApplyVitals` / `NotifyDepositFilterChanged` / `NotifyCapacitySettingsChanged` / `AuditReservations` / `RegisterAll`。
- 测试（P4 记录）：`CoreTestKit` 增加 Rules 引用以维持既有用例运行；P5 已拆分为 Core / Rules 两个测试工程（见第八节）。

## 八、P5 收尾记录

- 测试工程拆分：`SDS.Core.Tests` 只引用 Contracts / Diag / Core，保留 `ConservationTests` / `InvariantTests`；规则 / 流程测试（`TransactionTests`、`ReservationTests`、`ReturnTests`、`MigrationTests`、`LedgerTests`、`IdentityDedupTests`、`ScribeRoundTripTests`、`CoreHardeningTests`、`LedgerStructureTests`）迁入 `SDS.Rules.Tests`。
- 新增 `SDS.Rules.Tests/Support/RulesTestKit.cs`：`RulesTestEnv` 在账本环境上接宿主槽位、外部保真委托、结构事实与测试端口；静态清理用反射直接清 Rules 缓存。测试假宿主（`Fakes`）归本工程所有，`SDS.Tests` 以源码链接复用；`SDS.Core.Tests` 只保留 `CoreTestKit` / `TestAssembly`。
- `CoreTestEnv` 收窄为「设置 + 创建并附加账本」，不再接触 Rules；`SDS.Tests` 通过链接 `RulesTestKit.cs` 复用集成环境。
- `SDS.Rules.Tests` 增加 `0Harmony` 测试引用（仅测试输出，用于镜像 / 圣物探针用例）；脚本 §3 同步放行。
- Core 删除 `SDS_CoreImpl.ApplyQuality` / `ApplyVitals` 两个残留转发，公开门面直接转发 `SDS_StockData`。
- 公开面基线重生成：Core 32 条 / Rules 53 条；`SDS_API` 五轴与 `Version` 不变。
- 复审清理（2026-11-16）：删除 `SDS_Rules.HasLiveContent` 公开门面（仅补料内部使用，改调 Core 的 `SDS_StockData.HasLiveContent`）；删除 `SDS_WithdrawQuery.referenceCell` / `preferredMode` 两个零读取字段；`SDS_WithdrawPlan.endpoint` 保留（Vanilla 装载选择经 `SDS_LoadSelection` 建 Job 时读取）。清理后公开面基线：Contracts 301 条 / Rules 52 条。
