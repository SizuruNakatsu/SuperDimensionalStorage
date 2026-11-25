# SDS.Rules

> 定位：本目录是 `SDS.Rules` 程序集的全部维护说明。
> **当前状态（P5，迁移完成）**：端点决议、账单补料、判定层（通道 / 容量 / 可用量 / 结构 / 芯片）与流程层（存入 / 取出 / 回充 / 在途标记）全部在 Rules；`SDS.Core` 只保留账本与数据原语。迁移期间不双写、不转发（准则三）。

## 一、目标职责

Rules 是「本 Mod 玩法与口径规则的唯一定义处」，只产出决定与流程，不持有账本、不执行原版行为：

| 规则面 | 实现 | 状态 |
|---|---|---|
| 取谁的库存 | `SDS_EndpointResolver` | P2 |
| 补料选谁 / 缺口多少 | `SDS_BillSupplyPlanner` | P2 |
| 预留 → 物化 → 交付 / 回滚 | `SDS_WithdrawTransaction`、`SDS_WithdrawExecutor` | P3 |
| 回充与失败退路 | `SDS_ReturnUtility` | P3 |
| 在途物化标记 | `SDS_ProducedThingMarker` | P3 |
| 存入 / 取出 / Job 预留门面 | `SDS_RulesImpl` | P3 |
| 芯片端口是否可用 | `SDS_ImplantUtility` | P4 |
| 物品 → 通道 / 键、容量、可存 | `SDS_StockRules`（数据原语在 Core 的 `SDS_StockData`） | P4 |
| 可用量 / 可交易量 / 夹取 | `SDS_Availability` | P4 |
| 结构事实、总开关 G、容量上限 | `SDS_StructureRegistry` | P4 |
| 端点契约版本时钟 | `SDS_EndpointContractCache` | P4 |

## 二、明确不负责

- 不持有账本数据、不做数量算术：账本、预留、存档与 `total − reserved` 在 `SDS.Core`。
- 不打补丁、不派 Job、不写 UI、不做原版接触：这些在 `SDS.Vanilla`。
- 不做对外五轴：第三方只经 `SDS.Api`。

## 三、依赖与产物

- 编译期引用 `SDS.Contracts`、`SDS.Diag`、`SDS.Core`（`SDS.Rules.csproj`；《ModuleRules.zh-CN.md》四）。
- 产物：`SDS.Rules.dll`，输出到 `超维存储/Assemblies`；命名空间统一 `SuperDimensionalStorage`。

## 四、public 入口

`SDS_Rules`：规则 / 流程唯一门面（P2 端点与补料；P3 存入 / 取出 / Job 预留 / 镜像 / 回充 / 在途标记；P4 判定层与结构 / 端点版本）。
`SDS_Availability`：可用量 / 失败分类 / 夹取唯一口径（W22 / W56 / R15 / R17）。
`SDS_ImplantUtility`：芯片端口事实。
其余规则实现类一律 `internal`。

白名单之外的顶层 `public` 一律视为泄漏（[Rules.zh-CN.md](Rules.zh-CN.md)；`CheckAssemblyBoundaries.ps1` §4；《ModuleRules.zh-CN.md》五）。

## 五、迁移状态

| 阶段 | 内容 | Core 侧变化 |
|---|---|---|
| P1 | 工程 / 公开面 / 文档骨架 | 不变 |
| P2 | 端点决议、账单补料迁入 | 删除 5 个规则门面；新增 `HasLiveContent`（后于复审移除公开门面，内收为 `SDS_StockData`） |
| P3 | 流程层迁入（事务 / 执行器 / 回充 / 在途标记） | 删除 19 个流程门面；`FindWithdrawableKey` 转 internal；新增 `InternalsVisibleTo("SDS.Rules")` |
| P4 | 判定层迁入（通道 / 容量 / 可用量 / 结构 / 芯片） | 删除 16 个判定门面与 `SDS_Availability` / `SDS_ImplantUtility` 公开类；Core 仅保留账本 + `SDS_StockData`；`RegisterAll` 只登记自检 |

## 六、文档地图

| 文档 | 内容 |
|---|---|
| [Interfaces.zh-CN.md](Interfaces.zh-CN.md) | 签名索引、消费的契约、调用关系、原版 / 第三方接触面 |
| [Rules.zh-CN.md](Rules.zh-CN.md) | 规则正文（P2–P4 迁入；P5 收尾完成） |
| [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) | 为什么拆出 Rules、为什么 Rules 依赖 Core、迁移取舍 |
