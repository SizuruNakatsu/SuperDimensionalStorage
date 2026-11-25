> 定位：把 SDS.Bootstrap 必须遵守的约束写成可检查条目；规则本体以《ModuleRules.zh-CN.md》六·6.7、九与《ModuleResponsibilities.zh-CN.md》九为准，本文件不复述玩法语义。

# SDS.Bootstrap 规则

## 一、可检查条目

| 编号 | 规则 | 具体要求 | 检查方式 | 依据 |
|---|---|---|---|---|
| BSP-1 | 禁止被引用 | 任何 SDS 程序集都不得 `ProjectReference` 到 `SDS.Bootstrap` | `CheckAssemblyBoundaries.ps1` 第 2 节引用图；全仓搜其它 `.csproj` 中含 `SDS.Bootstrap` 的 `ProjectReference` 应为 0 | 《ModuleRules.zh-CN.md》6.7 |
| BSP-2 | 禁止静态引用 ThirdModCompat | `.csproj` 不引用 `SDS.ThirdModCompat`；源码除注释外不出现该程序集类型名（含 `SDS_CompatAdapter_*`） | 同脚本引用图；测试 `BootstrapContractTests.Bootstrap_ReferencesOnlyAllowedSdsAssemblies`；grep `SDS.ThirdModCompat` 只应命中 `SDS_CompatLoader.cs` 21 的注释，`SDS_CompatAdapter_` 为 0 | 《ModuleRules.zh-CN.md》6.7；《ModuleResponsibilities.zh-CN.md》九 |
| BSP-3 | 禁止写 CRUD / 补丁规则 | 不实现存取、容量、筛选、扣账、排序、事务；不声明补丁类、不创建 Harmony 实例 | 测试 `Bootstrap_DeclaresNoHarmonyPatchClasses`；grep `[HarmonyPatch]` / `new Harmony` / `TryDeposit` / `TryWithdraw` / `Reserved` 于本程序集应为 0；跨模块调用只允许《Interfaces.zh-CN.md》三表内成员 | 《ModuleRules.zh-CN.md》6.7 |
| BSP-4 | `SDS_Host.Install` 唯一调用点 | 只有 `SDS_HostInstaller.Install` 调用，且位于本程序集 | `CheckAssemblyBoundaries.ps1` 第 6 节（非 `SDS.Bootstrap` 文件出现即失败） | 《ModuleRules.zh-CN.md》五 |
| BSP-5 | Harmony 只装 Vanilla | 启动只调 `SDS_PatchInstaller.Install()` / `ApplyRuntimeHooks()`；不扫描其它程序集 | grep 调用点应只命中 `SDS_Startup.cs` 43、86；本程序集 `new Harmony` 为 0；测试 `Bootstrap_DeclaresNoHarmonyPatchClasses` | 《ModuleRules.zh-CN.md》七、九·3 |
| BSP-6 | 适配器去重与目标门 | 名称（Harmony ID）唯一，重复记 Error 并跳过后注册者；`IsTargetLoaded == false` 不 `Apply`、不写 `attempted` | 人工审阅 `SDS_CompatLoader.Process`（109–142）；grep `ownerByName` / `IsTargetLoaded` 分支；行为单测（待补） | 《ModuleRules.zh-CN.md》八；《ModuleInterfaces.zh-CN.md》一·1.2 |
| BSP-7 | 复扫幂等 | 同一适配器每进程最多 `Apply` 一次；先登记 `attempted` 再执行，异常不重试 | 人工审阅 143–160；两次 `Scan()` 调用点（48、87）应共用同一 `attempted`；行为单测（待补） | 《ModuleRules.zh-CN.md》九·4 |
| BSP-8 | 失败不阻断启动 | 同步段 / 注册段 / 延迟段各自 try/catch，只记 `SDS_Diag.ErrorOnce`，不向上抛 | 人工审阅 `SDS_Startup.cs` 53–56、66–69、96–98；grep `throw` 于本程序集应为 0 | 《ModuleRules.zh-CN.md》九；《ModuleInterfaces.zh-CN.md》一·1.2 |
| BSP-9 | 设置钳制 | 构造、每次绘制结束、`WriteSettings`、应用 Def 前均调 `ClampAll`；越界值不得流入 Core / Vanilla | grep `ClampAll()` 应命中 `SDS_Mod.cs` 29、137、320 与 `SDS_SettingsApplier.cs` 32；钳制幂等性测试（待补） | 《ModuleInterfaces.zh-CN.md》一·1.3；《DesignDecisions.zh-CN.md》3.3 第 4 条 |
| BSP-10 | 报告只从 Diag 读 | 启动报告只用 Vanilla 公开计数与 Diag 报告中心；不引用适配器类型、不自算 A/B | grep `LogReport` 内符号；程序集引用中无 `SDS.ThirdModCompat`；`SDS_CompatReportHub.LogAll()` 为唯一逐适配器输出 | 《ModuleResponsibilities.zh-CN.md》四、九 |
| BSP-11 | public 面唯一 | 只有 `SDS_Mod` 为顶层 public 类型 | 测试 `Bootstrap_ExposesOnlySDSMod`；`CheckAssemblyBoundaries.ps1` 第 4 节类型白名单 | 《ModuleResponsibilities.zh-CN.md》二 |
| BSP-12 | 设置实例首写唯一 | `SDS_Config.Current` 只在本程序集赋值一次 | `CheckAssemblyBoundaries.ps1` 第 9 节（非 Bootstrap 写入即失败） | 《ModuleInterfaces.zh-CN.md》一·1.3 |

## 二、审查口径

- 正常路径不写日志；异常一律 `SDS_Diag.ErrorOnce` 并继续（《诊断覆盖清单》§A 的一次性口径）。
- `CheckAssemblyBoundaries.ps1` 覆盖 BSP-1 / BSP-2 / BSP-4 / BSP-11 / BSP-12；测试覆盖 BSP-3 / BSP-5 / BSP-11 的静态事实。
- BSP-6 / BSP-7 / BSP-9 目前只有源码审阅与既有契约测试，行为级单测未落地，不得写成「已校验」（《RuleIndex.zh-CN.md》约定）。
- 新增 public 类型、新增 `SDS_Host` 槽位、把 Harmony 安装移出 Vanilla，均属越界改动，须先改《ModuleRules.zh-CN.md》与本文件。

## 三、规则正文（权威）

| ID | 规范句要点 | 与 SDS.Bootstrap 的关系 |
|---|---|---|
| E1 | 运行期静态缓存必须且只能在 `SDS_StaticState.ResetAll` 登记清理 | 第 5 步 `RegisterAll()`；报告输出登记项数 |
| E2 | 开发期断言只记 Error、不抛异常；仅 DEBUG + 开发者模式 | 第 7 步 `SDS_SelfCheck.Run(false)` 输出状态，本模块不判 `Prefs` |
| E6 | 兼容层只允许 `SDS_API` / `SDS_Compat*` / `SDS_Diag` 与公共 API 类型 | 扫描器只依赖 Contracts 的 `ISDSCompatAdapter` 与 Diag |
| E7 | 每个第三方入口只有一条写接管路径 | 扫描器只调 `Apply()` 一次，异常不重试 |
| W63 | 运行期 WorkGiver 枚举 + 落点折叠；无法解析落点才退让 | 第 6 步 `ApplyRuntimeHooks()` |
| W33 / W34 | 武器衣物强制虚拟化；只在玩家确认后转换一次存量 | 设置页确认框 + `ConvertPreservedGearToVirtual()` |

（来源：`RuleIndex.zh-CN.md` 对应行；玩法语义不回迁。）
