# SDS.Core

> 定位：本目录是 `SDS.Core` 程序集的全部维护说明，自包含：只读本目录即可维护本模块，接口签名与铁律都已在本目录内联。
> 唯一需要另读的是《ModuleRules.zh-CN.md》（全局一页）；跨模块边界存疑时再查《ModuleResponsibilities.zh-CN.md》二与《ModuleInterfaces.zh-CN.md》五、八。

## 一、职责

Core 是账本与数量口径的唯一实现，不产生原版行为（《ModuleResponsibilities.zh-CN.md》五）；玩法 / 口径判定与流程由 `SDS.Rules` 唯一定义（P2–P4 已全部迁出，P5 完成收尾）。

| 职责 | 实现类型 | 说明 |
|---|---|---|
| 账本 | `SDS_Ledger` | 虚拟数量、保真真身、预留、禁用、三个版本号、存档 / Tick |
| 账本数据原语 | `SDS_StockData` | 键构造、品类归属键、可存基础判据、堆平均状态、物化、状态写回 |
| 所有权转移 | `SDS_Ownership`（`SDS_Ledger.cs` 内） | 实物在「地图 / 容器 / 账本」之间移动的唯一实现；失败零副作用，禁止直接改 `holdingOwner` |
| 账本自检 | `SDS_CoreDiagnostics` | 虚拟可用量 / 保真真身失效 / 完整账本不变量 |

判定层（通道 / 容量 / 可用量 / 结构 / 芯片）与流程层（取货事务 / 回充 / 在途标记）自 P3–P4 起在 `SDS.Rules`。

## 二、明确不负责
- 不定义玩法 / 口径规则与流程：端点决议、账单补料、通道 / 容量 / 可用量、结构事实、芯片端口、事务 / 回充 / 在途标记均已在 `SDS.Rules`。
- 不交付：不调 `ISDS_DeliverySink` 完成正常交付；回充退路在 Rules。
- 不寻路、不枚举地图、不派 Job、不写 UI、不打补丁（《ModuleRules.zh-CN.md》6.3）。
- 不写 `Pawn.carryTracker` / `inventory.innerContainer`；不决定交付模式与端点优先级来源（模式、优先级存档值、候选枚举都在 Vanilla，《ModuleResponsibilities.zh-CN.md》五）。

## 三、依赖与产物
- 编译期只引用 `SDS.Contracts` 与 `SDS.Diag`（`SDS.Core.csproj`；《ModuleRules.zh-CN.md》四）；消费面见 [Interfaces.zh-CN.md](Interfaces.zh-CN.md) B 节。
- 产物：`SDS.Core.dll`，输出到 `超维存储/Assemblies`；命名空间统一 `SuperDimensionalStorage`，不新增子命名空间。

## 四、public 入口
`SDS_Core`、`ISDS_LedgerRead`、`ISDS_LedgerHost`；`SDS_Ledger` / `SDS_StockData` 是 internal 实现类型，不构成对外面。
P2–P4 迁出后 `SDS_Core` 只保留账本生命周期、已用品类数、禁用开关、状态写回与版本通知；端点 / 补料 / 流程 / 判定层门面全部在 `SDS_Rules`（含 `SDS_Availability`、`SDS_ImplantUtility`）。Core 的 internal 账本写接口经 `InternalsVisibleTo("SDS.Rules")` 只授予 Rules。

白名单之外的顶层 `public` 一律视为泄漏（[Rules.zh-CN.md](Rules.zh-CN.md) §四；`CheckAssemblyBoundaries.ps1` §4；《ModuleRules.zh-CN.md》五）。

## 五、铁律
见 [Rules.zh-CN.md](Rules.zh-CN.md) 的可检查条目，以及 [Rules.zh-CN.md](Rules.zh-CN.md) §十一 的规则 ID 权威表；一句话：账本与数量口径只定义一次在 Core，玩法 / 口径规则与流程只定义一次在 `SDS.Rules`（《ModuleRules.zh-CN.md》二）。

## 六、文档地图

| 文档 | 内容 |
|---|---|
| [Interfaces.zh-CN.md](Interfaces.zh-CN.md) | A 签名索引；B 消费的 Contracts / 宿主槽位；C 调用关系；D 原版支持；E 第三方支持 |
| [Rules.zh-CN.md](Rules.zh-CN.md) | 可检查的铁律：禁止符号、internal 边界、事务 / 回充 / 镜像 / 补料 / 清理 / 存档 |
| [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) | 历史决策、坑与设计取舍 |
| 《ModuleRules.zh-CN.md》（目录外，唯一另读） | 全局准则、边界与诊断要求 |

读完本目录即可开工；仅需另读《ModuleRules.zh-CN.md》
