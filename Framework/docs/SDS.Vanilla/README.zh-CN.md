# SDS.Vanilla（原版适配）

> 定位：本目录是 `SDS.Vanilla.dll` 的唯一维护入口，**自包含**：接口、规则与历史对照都在本目录内联；唯一另读的是《ModuleRules.zh-CN.md》（全局一页）。
> 事实来源：`超维存储/Source/SDS.Vanilla/`、`SDS.Tests/Vanilla/`；历史决策与踩过的坑见 [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md)。本模块九条铁律已全部落为 [Rules.zh-CN.md](Rules.zh-CN.md) 的 V1–V13 可检查条目（一一对应），本文不复述。

## 一、职责

| 类别 | 内容 | 源码落点 |
|---|---|---|
| 建筑 / 端口 | 核心、锚点、稳固装置、隐藏金库；`ISDS_StorageEndpoint` 原版容器契约 | `SDS_Buildings.cs` |
| 端点句柄 / 缓冲 | 建筑与 Pawn 共用句柄；`SDS_EndpointBuffer` 输入缓冲与即时数字化 | `SDS_EndpointHandle.cs`、`SDS_EndpointBuffer.cs`、`SDS_EndpointUtility.cs` |
| 镜像 / 投影 / 暴露 / 访问点 | 每图每键镜像、投影 owner、财富与资源条口径、访问点距离、非玩家取用门（S18）、优先级流出（W52） | `SDS_Mirrors.cs`、`SDS_Projection.cs`、`SDS_Exposure.cs`、`SDS_AccessPoints.cs`、`SDS_AccessRules.cs` |
| 112 个特性补丁 | 原版接缝、搜索、可及性、交易、编成 / 装载、保真桥、UI、热键 | 见《PatchInventory.zh-CN.md》统计节 |
| 29 个注册表条目 | 19 镜像守卫 + 10 投影 owner，由 `SDS_PatchInstaller` 动态安装 | `SDS_MirrorGuardRegistry.cs`、`SDS_ProjectionOwnerBridge.cs` |
| JobDriver / Thing / Comp | 存入 / 取货 / 保真取料驱动、代理物、芯片组件、附加光效 | `SDS_Destinations.cs`、`SDS_CaravanTransport.cs`、`SDS_PreservedBill.cs`、`SDS_Proxies.cs`、`SDS_Implants.cs`、`SDS_Comp_DrawExtraOverlay.cs` |
| UI / 热键 | 侧边栏、输出对话框、存入筛选 ITab、放置器、播放设置、工作报表 | `SDS_UI.cs`、`Window_SuperSpace.cs`、`Window_SDS_OutputDialog.cs`、`SDS_ITab_DepositFilter.cs`、`SDS_Hotkeys.cs`、`SDS_Use.cs` |
| 交易 / 编成 / 装载 | 展示物注入、成交校验、出发 / 入舱结算、读档恢复 | `SDS_Trade.cs`、`SDS_CaravanTransport.cs`、`SDS_TransportVirtual.cs`、`Patch_TransportLoad.cs` |
| 存档壳 | 账本挂载、Vanilla 状态、旧档节点迁移；每 250 tick 结构事实对账、首档操作指南信、库存变化后的服装 / 财富去抖提醒 | `SDS_GameComponent_Grid.cs`、`SDS_VanillaState.cs` |
| 回收账本 | 回收条目（Job 目标 / 手上料 / 受保护输出）、`sdsReclaim` 节点读写、读档审计、250 tick 巡检 | `SDS_ReclaimRegistry.cs`（`Foundation/`）、`SDS_GameComponent_Grid.cs` |
| 通道分部 | `Mirrors/`（虚拟专属）与 `Preserve/`（保真专属）两向禁引；共享目录为唯一合法桥；虚拟配料兜底与双通道供给桥在共享层 | `Mirrors/`、`Preserve/`、`SDS_VirtualBillFallback.cs`（`Jobs/`）、`SDS_OfferBridge.cs`（`Foundation/`）；检查见 [Rules.zh-CN.md](Rules.zh-CN.md) §十四 |

不负责：不发明 CRUD 规则；不计算容量 / 缺口 / 排序；不写账本内部；不引用 `SDS.ThirdModCompat`；不定义存档格式以外的玩法语义；不提供对外 API（五轴门面在 `SDS.Api`）；不承载第三方目标适配（在 `SDS.ThirdModCompat`）；不决定启动顺序（`SDS_Host.Install` 只允许 Bootstrap 调用）；不判断目标 Mod 是否存在。

## 二、依赖与产物

| 项 | 内容 |
|---|---|
| 允许引用 | `SDS.Contracts`、`SDS.Diag`、`SDS.Core`、Harmony（《ModuleRules.zh-CN.md》四） |
| 禁止引用 | `SDS.ThirdModCompat`、`SDS.Api`、`SDS.Bootstrap`、Core 的 internal 类型 |
| 产物 | `SDS.Vanilla.dll`，输出到 `超维存储/Assemblies` |
| 命名空间 | 全部 `SuperDimensionalStorage`（不新增子命名空间） |
| Harmony ID | `sizuru.sds.vanilla` |

## 三、public 面

| 面 | 类型 | 约束 |
|---|---|---|
| 模块间面 | `SDS_Vanilla`（`SDS.Vanilla_API.cs`，E8 登记文件） | 唯一对其它模块公开的入口：`CreateHostSlots` / `InstallPatches` / `ApplyRuntimeHooks` / `RegisterAll` 等按启动步骤由 Bootstrap 调用 |
| 引擎面 | `Building_SDS_*`、`JobDriver_SDS_*`、`SDS_Proxy_*`、`SDS_Comp*`；存档类型 `SDS_GameComponent_Grid`、`SDS_EndpointBuffer` | 按 Defs XML 名实例化或 Scribe 存档；禁止被其它模块引用 |
| internal 实现 | 9 个宿主实现（`SDS_EndpointProvider` … `SDS_TransportLoadAdapter`）、`SDS_PatchInstallerImpl`、`SDS_VanillaState` | 不构成对外面，仅作维护索引 |

签名、字段与消费面见 [Interfaces.zh-CN.md](Interfaces.zh-CN.md) 一；完整 public 清单以 `DumpPublicSurface.ps1 -Check` 基线为准（新增 public 类型即失败）。

## 四、文档地图

| 文档 | 用途 |
|---|---|
| [Interfaces.zh-CN.md](Interfaces.zh-CN.md) | public 签名、消费的 Contracts / Host / Core 面、调用关系、原版流程接入总览（§4.0）与原版支持（§4.1–4.15）、第三方支持 |
| [Rules.zh-CN.md](Rules.zh-CN.md) | V1–V13 可检查规则、补丁与存档约束、S 系搜索规则、静态清理 16 项、规则 ID 正文（§十）、镜像可见性结论（§十一） |
| [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) | 为什么这么做：历史决策与坑、旧实现到新实现的差异、设计取舍 |
| [CompatibilityRetirement.zh-CN.md](CompatibilityRetirement.zh-CN.md) | 旧档兼容项台账：支持窗口（2.4 起）、每项只对哪些档有意义、判定与退役前置 |
| [SplitPlan.zh-CN.md](SplitPlan.zh-CN.md) | 目录重组（9 职责簇）与可选大文件拆小的方案与依据；评估 Vanilla 体积时读 |
| 《ModuleRules.zh-CN.md》 | 全局硬约束、依赖方向、补丁归属、启动顺序 |

读完本目录即可开工；仅需另读《ModuleRules.zh-CN.md》。
