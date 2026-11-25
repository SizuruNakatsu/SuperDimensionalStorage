> 定位：SDS.Api 是「超维存储」唯一对外门面：把调用方上下文翻译成 Core 能理解的查询，再把结果翻译成结构化结果码；不定义第二套规则。

# SDS.Api（对外五轴门面）

## 一、定位

- 程序集 `SDS.Api.dll`，命名空间统一 `SuperDimensionalStorage`；唯一 public 类型 `SDS_API`（《ModuleResponsibilities.zh-CN.md》二）。
- 面向外部第三方与仓库内 `SDS.ThirdModCompat`；原版行为、补丁、UI 均不在本模块（《ModuleResponsibilities.zh-CN.md》一）。
- 跨程序集没有 `InternalsVisibleTo`，兼容层需要的工具以 public 方法挂在同一 `SDS_API` 上，单独称为「适配器桥」（《ModuleInterfaces.zh-CN.md》五）。

## 二、职责

| 轴 / 面 | 方法 | 转发到 |
|---|---|---|
| 看 | `VisibleThingsOfDef` | `SDS_Host.MirrorQuery` |
| 读 | `ReadStock` / `ReadStockFor` / `CaptureReadVersions` | `SDS_Core.Ledger`（`ISDS_LedgerRead`）、`SDS_Availability`、`SDS_Rules.TryGetStockKey`、`SDS_Host` 版本 |
| 找 | `TryFindDepositEndpoint` / `TryFindWithdrawPoint` | `SDS_Rules.SelectEndpoint` |
| 取 | `TryWithdraw`（两个重载）/ `TryWithdrawFromMirror` / `TryWithdrawPreservedExact` | `SDS_Rules.TryWithdraw` / `TryTakeMirror` / `TryWithdrawPreservedExact` |
| 存 | `TryDepositNow` / `TryDepositAt` | `SDS_Rules.TryDeposit` |
| 适配器桥 | 端点、账本只读、展示镜像、来源通道、Job 派活、按 def 批量取、补料规划、装载、回充 | `SDS_Core` / `SDS_Rules` / `SDS_Host` 的唯一实现 |

不做 UI、不寻路、不交付、不落地、不进容器、不建 JobDriver；不维护缓存或影子数量，每次调用现读（《PublicAPI.zh-CN.md》§3.1）。

本模块铁律见 [Rules.zh-CN.md](Rules.zh-CN.md)。

## 三、public 入口

- 唯一 public 类型 `SDS_API`：五轴在 `SDS_API.cs`，适配器桥在 `SDS_API_Adapters.cs`（同一 `SDS_API` 的 partial）；常量 `Version = 4`。
- Contracts 中对外可见的公共类型：`SDS_ApiCaller`、`SDS_ApiResultCode`、`SDS_ApiStockInfo`、`SDS_ReadVersions`、`SDS_StockKey`、`SDS_WithdrawMode`。
- 五轴签名、桥面分类、转发目标与消费的 Contracts / `SDS_Host` 槽见 [Interfaces.zh-CN.md](Interfaces.zh-CN.md)；桥的 public 方法清单唯一登记在《ModuleInterfaces.zh-CN.md》五。
- 适配器桥虽为 public，但不进入 `Version` 承诺，运行时消费者只有 `SDS.ThirdModCompat`。

## 四、依赖与产物

| 项 | 内容 |
|---|---|
| 项目引用 | `SDS.Contracts`、`SDS.Diag`、`SDS.Core`（`SDS.Api.csproj`） |
| 禁止引用 | `SDS.Vanilla`、`0Harmony`、`SDS.ThirdModCompat`（《ModuleRules.zh-CN.md》6.5） |
| 产物 | `SDS.Api.dll` |
| 补丁数 | 0（《ModuleRules.zh-CN.md》七） |
| 静态状态注册 | 无；Api / Sdk 工具类 / Bootstrap 合计 0 项（《ModuleResponsibilities.zh-CN.md》四） |

## 五、文档地图

| 文件 | 内容 |
|---|---|
| [Interfaces.zh-CN.md](Interfaces.zh-CN.md) | public 签名索引、消费的 Contracts / `SDS_Host` / Core 成员、调用关系、原版支持、第三方支持 |
| [Rules.zh-CN.md](Rules.zh-CN.md) | 本模块可检查的规则、调用门顺序与检查方式 |
| [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) | 历史决策与坑、旧实现与新实现的差异、设计取舍、待补（「为什么这么做」的完整原因） |
| [../ModuleRules.zh-CN.md](../ModuleRules.zh-CN.md) | 全局硬约束、依赖矩阵、各模块禁止项 |

读完本目录即可开工；仅需另读《ModuleRules.zh-CN.md》。
