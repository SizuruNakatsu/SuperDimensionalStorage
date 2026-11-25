# SDS.ThirdModCompat

> 定位：第三方 Mod 兼容单程序集。一个 dll 内包含 5 个 Sdk 共享工具类与 9 个适配器；目标 Mod 只反射探测，不静态引用。约束本体见《ModuleRules.zh-CN.md》6.6 / 七 / 八与《ModuleResponsibilities.zh-CN.md》二。

## 一、职责

### 1.1 Sdk 工具类（`SDS_CompatServices.cs`）

| 类 | 职责 |
|---|---|
| `SDS_CompatTools` | 反射封装（`Method` / `Property` / `PropertyGetter` / `Field` / `GetBool`）与 `TryPatch` 挂点唯一入口 |
| `SDS_CompatEndpoint` | 端点存在性：`HasAnyEndpoint` / `HasUsableBuildingEndpointWithin` |
| `SDS_CompatStock` | 按 def 的可用量（虚拟 + 保真合计）、取出与回充 |
| `SDS_CompatBillSupply` | 保真配料补料服务：候选 + 原版选料算法 + 保真取出 |
| `SDS_CompatMirrorSeam` | 第三方 Job 预约前的端点物化唯一实现：取端点格 / 取货 / 复制 comp 字段 / 落点 / 改写 target / 失败回账；供同形态适配器共用 |

### 1.2 适配器（`SDS_CompatAdapter_*.cs` + `SDS_ModCompat_*.cs`）

| 适配器 | 目标 Mod | 职责 |
|---|---|---|
| `SDS_CompatAdapter_PUAH` | Pick Up And Haul | 搬运 Job 判定与卸载派发接管 |
| `SDS_CompatAdapter_CE` | Combat Extended | 换弹 / 炮塔补弹 / 弹药镜像翻译 |
| `SDS_CompatAdapter_UFRecipes` | 联合轻工家具拓展 | 打印机拉料失败后的保真配料 |
| `SDS_CompatAdapter_ManipulatorBeam` | 牵引光束搬运器 | 特殊容器类型 / 扫描表 / 取源 |
| `SDS_CompatAdapter_MiliraExpanded` | 米莉拉扩展-未来科技 | 虚拟生产设备 / 订单的账单补料 |
| `SDS_CompatAdapter_CeleTechShuttle` | 天工模块化穿梭机 | 装载清单只读注入 |
| `SDS_CompatAdapter_EquivalentExchange` | ProjectE 等价交换 | 转化对话框展示物 ↔ 真身翻译 |
| `SDS_CompatAdapter_Versatran` | VC载具框架 | 车辆货舱装载的镜像行摘出 / 真身入舱 |
| `SDS_CompatAdapter_MiliraGift` | 边缘米莉拉养成：与你相连之人 | 送礼 Job 预约前的端点物化与 targetA 改写 |

挂点数、packageId、Harmony ID 见《Interfaces.zh-CN.md》§F.1；逐个挂点与接管方式见 §F.2。

本模块铁律即《Rules.zh-CN.md》R1–R11（禁止硬引用目标程序集、只经 `SDS_API`、`Apply` 幂等、动态挂点不计入 112 等），本文不复述。

## 二、依赖与产物

| 项 | 值 |
|---|---|
| 程序集 | `SDS.ThirdModCompat.dll` |
| ProjectReference | `SDS.Contracts`、`SDS.Diag`、`SDS.Api` |
| 另需 | Harmony（`SdsHarmonyRef`，`0Harmony`） |
| 不允许 | `SDS.Core`、`SDS.Vanilla`、任何第三方 dll |

## 三、public 面

公开面为 0：5 个 Sdk 工具类与 9 个适配器全部 `internal`（《ModuleResponsibilities.zh-CN.md》二）。Bootstrap 不静态引用本程序集，靠反射枚举 `ISDSCompatAdapter` 发现并调用；测试经登记的 `InternalsVisibleTo("SDS.Tests")` 直引 internal 类型。白名单由 `Tools/CheckAssemblyBoundaries.ps1` 做类型级检查。

## 四、文档地图

| 文件 | 内容 |
|---|---|
| [README.zh-CN.md](README.zh-CN.md) | 本文件：定位、职责、依赖与产物、public 面、地图 |
| [Interfaces.zh-CN.md](Interfaces.zh-CN.md) | Sdk 签名、9 个适配器身份与 `SDS_API` 调用清单、Contracts 类型、调用关系、「原版支持」「第三方支持」两节适配事实 |
| [Rules.zh-CN.md](Rules.zh-CN.md) | 本模块可检查规则 R1–R11 与对应校验方式 |
| [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) | 历史决策与坑、旧实现 → 新实现的差异与坑、设计取舍与边界 |

## 五、来源

- 规则：`Framework/docs/ModuleRules.zh-CN.md` 6.6 / 七 / 八；`ModuleResponsibilities.zh-CN.md` 二；`ModuleInterfaces.zh-CN.md` 六。
- 代码：`超维存储/Source/SDS.ThirdModCompat/` 全部源码；`SDS.Bootstrap/SDS_CompatLoader.cs`。
- 测试：`超维存储/Source/SDS.Tests/Compat/CompatAdapterTests.cs`；跑测试经 `Tools/TestModule.ps1`（先构建目标模块，再只编译测试工程并比对生产 DLL，禁止测试构建重建生产 DLL）。

读完本目录即可开工；仅需另读《ModuleRules.zh-CN.md》。
