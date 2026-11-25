# SDS.Diag

> 定位：诊断基础设施模块。只提供日志门面、开发期断言、自检入口、静态状态注册制与适配器报告中心；不承载任何玩法规则，也不认识 Core / Vanilla / ThirdModCompat 的实现类型。

## 一、职责

| 能力 | 内容 | 源码 |
|---|---|---|
| 日志门面 | `ErrorOnce`（消息 + 调用点 + 异常类型去重，第 10 / 100 / 1000 次补记）、`Fail`（Error 级，600 tick 节流）、`Warn`（Warning 级，每 reason 进程内一次）、`Ctx`（统一上下文串） | `SDS_Diag.cs` |
| 开发期断言 | E2：可用量区间、事务阶段、事务终结；只记 Error，不抛异常 | `SDS_Invariants.cs` + `SDS_Diag.Assert*` |
| 自检入口 | 收集各模块 `ISDS_DiagnosticProvider` 的报告行，按 `[失败]` / `[统计]` / `[载入]` 分级输出 | `SDS_SelfCheck.cs` |
| 静态状态注册制 | `ISDSResettable` 登记；`ResetAll` 逐项清理并计数 | `SDS_StaticState.cs` |
| 适配器报告中心 | 挂点 A/B 报告载体与唯一输出口 | `SDS_CompatReports.cs` |

## 二、不负责

| 不负责 | 归属 |
|---|---|
| 具体检查项（账本可用量、镜像存活、金库结构、W63 落点挂点） | Core `SDS_CoreDiagnostics`、Vanilla `SDS_VanillaDiagnostics` |
| 静态缓存的实际清理动作 | 各模块的 `Reset` / `ResetStatics` / `ResetChipCache` |
| 补丁安装、补丁计数、Harmony 实例 | Vanilla、ThirdModCompat |
| 玩法规则、品类判定、扣账 / 派活 / 回滚 | Core / Vanilla（本模块只被调用） |
| 普通工作日志 | 不提供 `Message` 门面；正常回退一律静默 |

## 三、依赖与产物

> 公开面唯一文件：`SDS.Diag_API.cs`（4 个门面 + `ISDSResettable` + 报告 DTO，一行转发）。
> `SDS_Diag.cs` / `SDS_StaticState.cs` / `SDS_SelfCheck.cs` / `SDS_CompatReports.cs` / `SDS_Invariants.cs` 为对应 Impl 实现文件，全部 `internal`（脚本 4.5 节强制）。

| 项 | 值 |
|---|---|
| 项目引用 | 仅 `SDS.Contracts`（`SDS.Diag.csproj`） |
| 原版类型 | `Assembly-CSharp` / `UnityStubs`，由 `Directory.Build.props` 统一引入 |
| Harmony | 不引用；未开启 `SdsHarmonyRef` |
| 产物 | `超维存储/Assemblies/SDS.Diag.dll` |
| 下游 | Core、Api、Vanilla、ThirdModCompat、Bootstrap 单向引用本模块 |

## 四、public 入口

`SDS_Diag` / `SDS_StaticState` / `ISDSResettable` / `SDS_SelfCheck` / `SDS_CompatReportHub` / `SDS_CompatPatchReport` / `SDS_CompatAdapterReport`。

`ISDS_DiagnosticProvider` 定义在 Contracts（`SDS_Host.cs`），由本模块消费，不在本程序集内。

## 五、本模块铁律

铁律与规则不在此复述：`Fail` 调用方限制、无 `Message` 门面、DTO 类型、主线程写报告、`ResetAll` 计数、`Conditional` 剥离、依赖单向，已逐条落为《Rules.zh-CN.md》的 D1–D13（D4 = 铁律 1，D12 = 铁律 2）。

## 六、文档地图

| 文档 | 内容 |
|---|---|
| [Interfaces.zh-CN.md](Interfaces.zh-CN.md) | 公共签名、DTO 字段、各程序集实际调用点；原版支持与第三方支持 |
| [Rules.zh-CN.md](Rules.zh-CN.md) | 本模块规则 D1–D13、规则正文、检查方式与已知缺口 |
| [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) | 诊断口径的历史决策与坑、旧实现 → 新实现的差异、设计取舍、待补 |
| 《ModuleRules.zh-CN.md》 | 上级权威规则；本目录其余内容读完即可开工 |
