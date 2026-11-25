# SDS.Contracts

一句话定位：超维存储全部模块共用的纯数据契约程序集，只声明类型、接口、DefOf、设置与宿主注入点，不含任何行为实现。

## 职责

| # | 职责 |
|---|---|
| 1 | 跨模块数据：品类键 / 账本条目 / 预留 / 回收 / 装载行 / 侧边栏状态等 DTO 与枚举。 |
| 2 | 端点查询、判定结果与结构事实的值类型。 |
| 3 | 11 个宿主注入槽对应的接口、`ISDS_Port`、`ISDS_DiagnosticProvider`、`ISDSCompatAdapter` 与 `SDS_Host`。 |
| 4 | 设置数据 `SDS_Settings` 与只读读取口 `SDS_Config`。 |
| 5 | `SDS_JobDefOf` / `SDS_HediffDefOf` 与纯注册表 `SDS_PreserveHooks` / `SDS_CompatGates`。 |
| 6 | 冻结存档节点名、LookMode 与跨模块公共签名。 |

不负责：不实现宿主接口，不做任何运行期规则判定与计算（加权平均、排序、容量、筛选、事务、回充、回收归 Core / Vanilla）；不装 Harmony、不写 UI、不寻路、不定义行为层 Job / Toil / ThinkTree / WorkGiver / Duty；不提供设置页（设置 UI 归 Bootstrap，本模块只给数据与钳制）；不持有账本或静态状态（运行期状态归 Core / Vanilla）。

## 依赖与产物

| 项 | 内容 |
|---|---|
| 程序集引用 | 无 `ProjectReference`；仅 `Assembly-CSharp`（RimWorld / Verse）与 `UnityStubs`（UnityEngine），见 `Directory.Build.props` |
| 产物 | `SDS.Contracts.dll`，输出到 `超维存储/Assemblies` |
| 测试 | `SDS.Tests` 可引用全部模块，含本程序集 |

## public 入口（只列类型名）

- 品类与账本：`SDS_StockKey`、`SDS_StockEntry`、`SDS_StockVitals`、`SDS_Reservation`、`SDS_StockChannel`
- 事务与回收：`SDS_WithdrawMode`、`SDS_WithdrawQuery`、`SDS_WithdrawPlan`、`SDS_WithdrawEndState`、`SDS_SupplyFailure`、`SDS_ReclaimPolicy`、`SDS_ReclaimOutcome`、`SDS_ReclaimSave`、`SDS_ReturnFallback`、`SDS_WithdrawDeliveryPolicyDef`、`SDS_ReadVersions`
- 端点与结构：`SDS_EndpointOp`、`SDS_EndpointQuery`、`SDS_EndpointDecision`、`SDS_StructureKind`、`SDS_StructureFact`
- 宿主与端口：`ISDS_Port`、`ISDS_PortProvider`、`ISDS_DepositFilter`、`ISDS_PrioritySource`、`ISDS_DeliverySink`、`ISDS_MirrorQuery`、`ISDS_MirrorPhysical`、`ISDS_MedicalCarePolicy`、`ISDS_ReclaimWorld`、`ISDS_DisplayQuery`、`ISDS_DepositJobs`、`ISDS_TransportLoadSink`、`ISDS_DiagnosticProvider`、`ISDSCompatAdapter`、`SDS_Host`
- API 边界：`SDS_ApiCaller`、`SDS_ApiResultCode`、`SDS_ApiStockInfo`
- 设置：`SDS_Settings`、`SDS_Config`
- DefOf 与纯注册表：`SDS_JobDefOf`、`SDS_HediffDefOf`、`SDS_PreserveHooks`、`SDS_CompatGates`
- 存档 DTO：`SDS_TransportLoadSave`、`SDS_SidebarUIState`

字段级签名以 `Interfaces.zh-CN.md` A 节为唯一权威，本页只列类型名。

## 铁律

见 `Rules.zh-CN.md` C1–C8，本页不复述。

## 文档地图

| 文件 | 一句话 |
|---|---|
| `README.zh-CN.md` | 本页：定位、职责、依赖与产物、public 入口索引。 |
| `Interfaces.zh-CN.md` | A 节对外签名（唯一签名权威）、B 节 6 个程序集的消费面与调用关系、原版支持、第三方支持。 |
| `Rules.zh-CN.md` | 只属于本模块、逐条可执行可检查的 C1–C8 规则，及迁入的规则正文（权威）。 |
| `DesignDecisions.zh-CN.md` | 为什么这么做：历史决策与坑、迁移删增（含理由）、纯数据程序集的设计取舍、旧存档节点与字段的历史说明。 |

读完本目录即可开工；仅需另读《ModuleRules.zh-CN.md》（全局一页）
