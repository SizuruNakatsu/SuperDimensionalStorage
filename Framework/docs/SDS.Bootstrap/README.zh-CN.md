> 定位：SDS.Bootstrap 是「超维存储」唯一 Mod 入口与装配点：读设置、注入宿主、装 Vanilla 补丁、扫描第三方适配器、按 7 步启动并输出启动报告；不定义任何玩法规则。

# SDS.Bootstrap（Mod 入口与装配点）

## 一、定位

- 程序集 `SDS.Bootstrap.dll`，命名空间统一 `SuperDimensionalStorage`；唯一 public 类型 `SDS_Mod`（《ModuleResponsibilities.zh-CN.md》二、九）。
- RimWorld 通过 `Mod` 基类发现并构造本类型；本程序集是启动顺序的唯一实现处（《ModuleRules.zh-CN.md》九）。
- 其余全部 `internal`：`SDS_Startup`、`SDS_CompatLoader`、`SDS_CompatScanResult`、`SDS_HostInstaller`、`SDS_SettingsApplier`。
- 不负责：CRUD / 容量 / 筛选 / 扣账 / 排序 / 事务（Core）、112 个补丁与建筑 / UI（Vanilla）、账本生命周期（Vanilla `SDS_GameComponent_Grid`）、设置字段定义（Contracts）。

## 二、职责

| 职责 | 落点 | 要点 |
|---|---|---|
| 设置页 | `SDS_Mod.cs`（`SDS_Mod`） | 只写 `SDS_Settings` 字段；结束与关窗前 `ClampAll` |
| 11 宿主注入 | `SDS_HostInstaller.cs` | 全部来自 `SDS.Vanilla`；构造时不读存档 |
| Harmony 安装 | `SDS_Startup.cs` 调 Vanilla `SDS_PatchInstaller.Install()` | 只扫 `SDS.Vanilla`；本程序集 0 个 `[HarmonyPatch]` |
| 适配器扫描 | `SDS_CompatLoader.cs` | 反射发现 `ISDSCompatAdapter`；名称去重、目标未装不 `Apply` |
| 启动 7 步 | `SDS_Startup.cs` | 前 5 步同步，第 6 步延迟，第 7 步报告 |
| 启动报告 | `SDS_Startup.LogReport` | 计数来自 Vanilla 公开计数与 Diag 报告中心 |

## 三、依赖与产物

| 项 | 内容 |
|---|---|
| 项目引用 | `SDS.Contracts`、`SDS.Diag`、`SDS.Core`、`SDS.Vanilla`、`SDS.Api`（`SDS.Bootstrap.csproj`） |
| 禁止引用 | `SDS.ThirdModCompat`（只按接口反射发现）、`0Harmony` |
| 产物 | `SDS.Bootstrap.dll` |
| 静态状态注册 | 0 项（《ModuleResponsibilities.zh-CN.md》四） |

## 四、public 入口

`SDS_Mod`（唯一顶层 public 类型）。成员、设置页条目与调用点见[Interfaces.zh-CN.md](Interfaces.zh-CN.md) §一。

## 五、约束

本模块硬性约束见[Rules.zh-CN.md](Rules.zh-CN.md)（BSP-1～BSP-12 为唯一权威）。

## 六、文档地图

| 文件 | 内容 |
|---|---|
| [Interfaces.zh-CN.md](Interfaces.zh-CN.md) | public 面、启动 7 步调用点、11 个注入对象、消费成员、原版 / 第三方交互面 |
| [Rules.zh-CN.md](Rules.zh-CN.md) | 本模块可检查规则与检查方式 |
| [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) | 历史决策与坑、旧实现差异、设计取舍、待验证 |
| [../ModuleRules.zh-CN.md](../ModuleRules.zh-CN.md) | 全局硬约束、依赖矩阵、启动顺序（§九为权威） |
