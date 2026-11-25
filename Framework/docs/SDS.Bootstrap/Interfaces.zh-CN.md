> 定位：列出 SDS.Bootstrap 的 public 面、启动 7 步的实际调用点、`SDS_HostInstaller` 注入的 11 个对象、本模块消费的跨模块成员，以及与 RimWorld / 第三方的交互面；签名以源码为准。

# SDS.Bootstrap 接口

源码路径均相对 `超维存储/Source/SDS.Bootstrap/`。启动顺序的规则本体见《ModuleRules.zh-CN.md》九，本文只登记落点。

## 一、A 节：`SDS_Mod` 的 public 面

```csharp
public class SDS_Mod : Mod
{
    public static SDS_Settings Settings;              // SDS_Mod.cs 16
    public SDS_Mod(ModContentPack content);            // 24–34
    public override string SettingsCategory();         // 36–39
    public override void DoSettingsWindowContents(Rect inRect); // 41–138
    public override void WriteSettings();              // 315–334
}
```

| 成员 | 行为 |
|---|---|
| 构造 | `GetSettings<SDS_Settings>()` → 非空 `ClampAll()` → `SDS_Config.Current = Settings` → `SDS_Startup.Run(Settings)`（26–33） |
| `SettingsCategory` | 返回 `"SDS.Settings.Category".Translate()` |
| `DoSettingsWindowContents` | 先通知 Vanilla 设置页已绘制；快捷键捕获中则只画遮罩；否则画滚动区，结束前 `ClampAll()` |
| `WriteSettings` | `SDS_Vanilla.CancelHotkeyCapture()` → `ClampAll()` → `SDS_SettingsApplier.ApplyPowerAndPriority` + `RefreshWealth`（try/catch 记 `Log.Error`）→ `base.WriteSettings()` |

设置页条目（字段定义与默认值 / 范围在 Contracts 的 `SDS_Settings` / `SDS_Config`）：

| 控件 | 字段 | 副作用 |
|---|---|---|
| 滑块 | `gridCapacity` | 无（Core 经 `SDS_Config` 只读） |
| 滑块 | `capacityPerArray` | 无（Vanilla 经 `SDS_Config` 只读） |
| 滑块 | `corePower` / `arrayPower` / `portPower` | 关窗时写入建筑 Def 耗电 |
| 复选框 | `separateByQuality` / `separateByStuff` | 无（Core 只读） |
| 复选框 | `excludeGridFromWealth` | 关窗时 `RefreshWealth()` |
| 复选框 | `playerFactionOnlyAccess` | 首次勾选先弹确认框；确认后置真；无其它副作用（Vanilla 每次现读 `SDS_Config`，门在访问点缓存之前判断） |
| 复选框 | `highestStoragePriority` | 变化即 `SDS_Rules.NotifyEndpointChanged()` + `SDS_Vanilla.ApplyStoragePriorityToEndpoints()`；开启时同时关闭 `allowPriorityOutflow` |
| 复选框 | `allowPriorityOutflow` | 首次勾选先弹确认框；确认后置真并关闭 `highestStoragePriority`（若原为开则同步刷新优先级）；取消保持关闭 |
| 复选框 | `forceWeaponApparelVirtual` | 首次勾选先退回 `false` 并弹确认框；确认后置真，游戏中调 `SDS_Rules.ConvertPreservedGearToVirtual()` |
| 快捷键区 | 原版 `KeyPrefs`（Open / Output / Use） | 设置 / 清除 / 恢复默认均转 `SDS_Vanilla` |
| 按钮 | `ResetToDefaults()` | 立即 `SDS_SettingsApplier.ApplyPowerAndPriority` + `RefreshWealth` |

ModSettings 读写：

- 读：`GetSettings<SDS_Settings>()`，与存档节点的读写实现同在 Contracts（《ModuleInterfaces.zh-CN.md》八·8.3），本模块不新增节点。
- 写：仅 `WriteSettings` 的钳制与应用副作用；字段落盘由 `base.WriteSettings()` 触发。
- 是否暴露其它 public 类型：否。全程序集只有 `SDS_Mod` 一个顶层 public 类型，由 `SDS.Tests/Bootstrap/BootstrapContractTests.cs` 21–34 与 `CheckAssemblyBoundaries.ps1` 白名单双重约束。

## 二、B 节：启动 7 步的实际调用点

| 步 | 动作 | 调用点 | 位置 |
|---|---|---|---|
| 1 | 设置读取与钳制 | `SDS_Mod` 构造：`GetSettings` / `ClampAll` / `SDS_Config.Current =` / `SDS_Startup.Run` | `SDS_Mod.cs` 26–33 |
| 2 | 宿主注入 | `SDS_Startup.Run` → `SDS_HostInstaller.Install()` → `SDS_Host.Install(SDS_HostSlots)` | `SDS_Startup.cs` 37；`SDS_HostInstaller.cs` 15–50 |
| 3 | Harmony 安装 | `SDS_PatchInstaller.Install()` | `SDS_Startup.cs` 43 |
| 4 | 适配器首扫 | `SDS_CompatLoader.Scan()` | `SDS_Startup.cs` 48 |
| 5 | 模块注册 | `SDS_Core.RegisterAll()`、`SDS_Rules.RegisterAll()`、`SDS_Vanilla.RegisterAll()` | `SDS_Startup.cs` 61–63 |
| 6 | 延迟应用 | `LongEventHandler.ExecuteWhenFinished(RunDeferred)` → `SDS_SettingsApplier.ApplyPowerAndPriority`、`SDS_PatchInstaller.ApplyRuntimeHooks`、`SDS_CompatLoader.Scan` | `SDS_Startup.cs` 72、85–87 |
| 7 | 启动报告 | `SDS_Vanilla.ReportUseProvidersOnce()`、`SDS_CompatReportHub.LogAll()`、`SDS_SelfCheck.Run(false)`、综合 `Log.Message` | `SDS_Startup.cs` 92–114 |

补充约定：

- 幂等标记：`started` 防重复初始化（18–25），`deferredRan` 防延迟段重入（76–81）；`SDS_HostInstaller.installed` 防宿主重复注入。
- 第 5 步有独立 try/catch：第 1–4 步失败也照常登记（58–69）。
- 规则里的「第 7 步：进入游戏 / 读档」由 Vanilla 的 `SDS_GameComponent_Grid` 构造接管，Bootstrap 不重复实现（`SDS_Startup.cs` 10 的注释、113 的日志）。

`SDS_HostInstaller` 注入的 11 个对象（填入 `SDS_HostSlots` 字段，`SDS_HostInstaller.cs` 23–35）：

| 槽 | 槽位字段类型 | 接口 |
|---|---|---|
| `Ports` | `SDS_EndpointProvider` | `ISDS_PortProvider` |
| `DepositFilter` | `SDS_VanillaServices` | `ISDS_DepositFilter` |
| `Priority` | 同上实例 | `ISDS_PrioritySource` |
| `Delivery` | 同上实例 | `ISDS_DeliverySink` |
| `MirrorQuery` | `SDS_MirrorService` | `ISDS_MirrorQuery` |
| `MirrorPhysical` | `SDS_MirrorPhysicalOps` | `ISDS_MirrorPhysical` |
| `MedicalCare` | `SDS_MedicalCareAdapter` | `ISDS_MedicalCarePolicy` |
| `ReclaimWorld` | `SDS_ReclaimWorldAdapter` | `ISDS_ReclaimWorld` |
| `Display` | `SDS_DisplayAdapter` | `ISDS_DisplayQuery` |
| `DepositJobs` | `SDS_DepositJobsAdapter` | `ISDS_DepositJobs` |
| `TransportLoad` | `SDS_TransportLoadAdapter` | `ISDS_TransportLoadSink` |

- 同一个 `SDS_VanillaServices` 实例占 3 个槽；其余每槽一个新实例。
- 注入后逐槽判非空计数并返回，启动日志按 `N/11` 输出；不因某槽为 null 抛异常（《ModuleInterfaces.zh-CN.md》一·1.2）。

## 三、C 节：消费的跨模块成员

| 来源 | 成员 | 用途 |
|---|---|---|
| Contracts | `SDS_Settings`（字段、`ClampAll`、`ResetToDefaults`） | 设置页读写与钳制 |
| Contracts | `SDS_Config`（`Current`、`*Min` / `*Max`、`*DefName`） | 首写实例、滑块范围、建筑 Def 名 |
| Contracts | `SDS_Host`（11 槽 + `Install`） | 宿主注入 |
| Contracts | `SDS_PreserveHooks`（`RequiresPreserved`） | 作为外部保真分类委托接入 Core |
| Contracts | `ISDSCompatAdapter`（`Name` / `TargetPackageId` / `IsTargetLoaded` / `Apply`） | 反射扫描与执行 |
| Diag | `SDS_Diag.ErrorOnce` | 所有失败分支 |
| Diag | `SDS_StaticState.RegisteredCount` | 第 5 步与报告计数 |
| Diag | `SDS_SelfCheck.Count` / `SDS_SelfCheck.Run(false)` | provider 计数与自检输出 |
| Diag | `SDS_CompatReportHub.LogAll()` / `Has` / `Register` | 报告输出与补登记 |
| Core | `SDS_Core.RegisterAll()` | Core 自检 provider 登记 |
| Rules | `SDS_Rules.RegisterAll()`、`SDS_Rules.NotifyEndpointChanged()`、`SDS_Rules.SetExternalPreserveProvider(...)`；W34 确认后的 `SDS_Rules.ConvertPreservedGearToVirtual()` | 静态清理 / 自检登记、端点契约通知、外部保真接线、存量迁移 |
| Vanilla | `SDS_PatchInstaller.Install` / `ApplyRuntimeHooks` / `AppliedCount` / `DeclaredCount` | 补丁安装与计数 |
| Vanilla | `SDS_Vanilla.RegisterAll` / `NotifySettingsPageDrawn` / `HotkeyCapturing` / `HotkeyCaptureAction` / `HotkeyCaptureNote` / `GetHotkeyLabel` / `BeginHotkeyCapture` / `CancelHotkeyCapture` / `ClearHotkey` / `ResetHotkeys` / `ApplyStoragePriorityToEndpoints` | 设置页与快捷键 |
| Vanilla | `SDS_GameComponent_Grid.Instance` | 存档壳存在性判断（`SDS_Vanilla.HasGrid`）；W33 转换已改调 Rules |
| Vanilla | 9 个宿主实现类（见 B 节表） | 宿主注入 |
| Api | 无 | 项目引用存在（矩阵允许），源码无 `SDS_API` 调用 |

## 四、调用关系

```text
RimWorld → SDS_Mod(ModContentPack)
  └→ SDS_Startup.Run(settings)
       ├→ SDS_HostInstaller.Install() ─→ SDS_Host.Install(SDS_HostSlots)
       ├→ SDS_PatchInstaller.Install()        [SDS.Vanilla]
       ├→ SDS_CompatLoader.Scan() ─→ ISDSCompatAdapter.Apply()
       ├→ SDS_Core.RegisterAll() / SDS_Rules.RegisterAll() / SDS_Vanilla.RegisterAll()
       └→ LongEventHandler.ExecuteWhenFinished
            └→ SDS_SettingsApplier.ApplyPowerAndPriority() → SDS_Rules.NotifyEndpointChanged / SDS_Vanilla.ApplyStoragePriorityToEndpoints
               SDS_PatchInstaller.ApplyRuntimeHooks() / SDS_CompatLoader.Scan()
               LogReport() → SDS_CompatReportHub.LogAll() / SDS_SelfCheck.Run(false)

设置页：SDS_Mod.DoSettingsWindowContents / WriteSettings → SDS_Settings 字段 + SDS_SettingsApplier
```

## 五、原版支持

原版生命周期的顺序规则见《ModuleRules.zh-CN.md》九，本节只登记 Bootstrap 侧接触到的原版类型、方法与时机。

### 5.1 交互面总表

| 原版 API / 机制 | Bootstrap 中的用途 | 位置 | 来源 |
|---|---|---|---|
| `Mod` 基类与 `ModContentPack` 构造 | RimWorld 发现并构造唯一入口 | `SDS.Bootstrap/SDS_Mod.cs` 24 | 旧 `SDS_Mod.cs` 31 |
| `Mod.GetSettings<T>()` | 取 `SDS_Settings` 实例 | 同文件 26 | 旧 `SDS_Mod.cs` 33 |
| `ModSettings` / `Scribe_Values.Look` | 设置落盘与读回（实现在 Contracts） | `SDS.Contracts/SDS_Mod.cs` 21–34 | 旧 `SDS_Mod.cs` 606–619；《ModuleInterfaces.zh-CN.md》八·8.3 |
| `Mod.SettingsCategory` | 设置页分类名 | `SDS.Bootstrap/SDS_Mod.cs` 36–39 | 旧 `SDS_Mod.cs` 124–127 |
| `Mod.DoSettingsWindowContents` | 设置页绘制 | 同文件 41–138 | 旧 `SDS_Mod.cs` 129–225 |
| `Mod.WriteSettings` | 关窗前钳制与副作用 | 同文件 315–334 | 旧 `SDS_Mod.cs` 396–413 |
| `Listing_Standard` / `Widgets` / `Mathf` | 滑块、复选框、滚动区 | 同文件 54–135 | 旧 `SDS_Mod.cs` 138–222 |
| `Dialog_MessageBox.CreateConfirmation` / `Messages.Message` / `Find.WindowStack` | W33 强制虚拟化确认与阻止窗口判定 | 同文件 144–210 | 旧 `SDS_Mod.cs` 231–298；《DiagnosticsCoverage.zh-CN.md》§H |
| `Current.ProgramState` | 判断是否在游戏中 | 同文件 146、163、168 | 旧 `SDS_Mod.cs` 233、239 |
| `HarmonyLib.Harmony` | 创建 Vanilla 补丁实例 | Vanilla `SDS_PatchInstaller.cs` 60 | 旧 `SDS_Mod.cs` 62 |
| `AccessTools.GetTypesFromAssembly` | 枚举本程序集补丁类 | 同文件 61 | 旧 `SDS_Mod.cs` 64 |
| `LongEventHandler.ExecuteWhenFinished` | 把第 6 步排到加载完成之后 | `SDS.Bootstrap/SDS_Startup.cs` 72 | 旧 `SDS_Mod.cs` 37–39 |
| `DefDatabase<ThingDef>.GetNamedSilentFail` | 取建筑 Def | `SDS.Bootstrap/SDS_SettingsApplier.cs` 68 | 旧 `SDS_Mod.cs` 467 |
| `Def.GetCompProperties<CompProperties_Power>()` | 取耗电属性 | 同文件 73 | 旧 `SDS_Mod.cs` 472 |
| `CompProperties_Power` 两个字段（反射） | 写耗电与「需要电力」提示 | 同文件 16–21、86–92 | 旧 `SDS_Mod.cs` 24–29、484–490 |
| `CompPowerTrader.SetUpPowerVars` | 让场上建筑立即重算耗电 | 同文件 101–124 | 旧 `SDS_Mod.cs` 498–530 |
| `Find.Maps` / `map.listerThings` | 枚举场上建筑 | 同文件 103–123 | 旧 `SDS_Mod.cs` 503–529 |
| `map.wealthWatcher.ForceRecount` | 设置切换后立即重算财富 | 同文件 46–60 | 旧 `SDS_Mod.cs` 420–435 |

### 5.2 启动时机

- Harmony 实例在 Vanilla `SDS_PatchInstaller.Install()` 内创建（`SDS_PatchInstaller.cs` 54–60），调用点是第 3 步（`SDS_Startup.cs` 43）；静态 `harmony` 非空即返回，同一进程只安装一次。
- 安装方式是逐类 `harmony.CreateClassProcessor(type).Patch()`，不使用 `Harmony.PatchAll`；单个补丁类失败只记录并跳过，其余补丁继续。两张动态注册表（`SDS_MirrorGuardRegistry` / `SDS_ProjectionOwnerBridge`）与 W63 落点挂点也在 `Install()` 内安装（`SDS_PatchInstaller.cs` 72–99），第 6 步 `ApplyRuntimeHooks()` 再跑一次幂等兜底（`SDS_Startup.cs` 86）。ID 为 `sizuru.sds.vanilla`（《ModuleRules.zh-CN.md》八）；第三方动态补丁不在此安装，由适配器在 `Apply` 内自建实例。
- `SDS_Startup.Run` 末尾调用 `LongEventHandler.ExecuteWhenFinished(RunDeferred)`（`SDS_Startup.cs` 72）：同步段只做设置、宿主、Vanilla 补丁、适配器首扫与模块注册，第 6 步放到回调里；原因是 `Mod` 构造时 Def 尚未加载完成，写 `ThingDef` 的 `CompProperties_Power` 只能等加载结束。
- 回调执行时可能没有游戏（主菜单 / 加载阶段）：`RefreshWealth` 与 `RefreshSpawnedPowerComps` 开头都判断 `Current.ProgramState` / `Current.Game` / `Find.Maps`，避免空引用（`SDS_SettingsApplier.cs` 48、103）；Def 级写入在判空之前执行，只要有 Def 就会写。
- `DebugAction` 不在 Bootstrap：唯一入口是 Diag 的 `SDS_SelfCheck.RunLedgerChecks`（`SDS.Diag/SDS_SelfCheck.cs` 44），归开发者菜单「超维存储 → 自检：账本 / 预留 / 镜像」。`Prefs.DevMode` 由 Diag 断言与 Vanilla 的 `SDS_GameComponent_Grid` 读取，Bootstrap 不读 `Prefs`，第 7 步只调 `SDS_SelfCheck.Run(false)` 输出自检（`SDS_Startup.cs` 106）。设置页的 W33 确认框属于玩家可见 UI，用 `Current.ProgramState` 与窗口类型名判断，不是开发者入口（`SDS_Mod.cs` 144–210）。

### 5.3 待补

- `ExecuteWhenFinished` 在「主菜单直接打开 Mod 设置」与「读档」两条路径下的实际触发次数（待补，需实机日志）。
- `CompProperties_Power` 的 `basePowerConsumption` 字段名是否随 RimWorld 版本变化（待补，需按目标版本核对）。
- `DebugAction` 在非开发者模式下的菜单可见性（待补，需实机确认；与《SDS.Diag/Interfaces.zh-CN.md》原版支持 待补项同源）。

## 六、第三方支持

适配器自身的翻译职责见《ModuleRules.zh-CN.md》六·6.6、《ModuleResponsibilities.zh-CN.md》八、《ModuleInterfaces.zh-CN.md》六；本节只写 Bootstrap 侧。

### 6.1 Bootstrap 对第三方暴露的契约面

| 对象 | 角色 | Bootstrap 如何接触 |
|---|---|---|
| `ISDSCompatAdapter`（Contracts） | 适配器身份与目标探测接口 | 反射枚举实现类型，实例化后按接口调用（签名见《ModuleInterfaces.zh-CN.md》一·1.2） |
| `SDS_CompatReportHub`（Diag） | 适配器报告登记与输出 | 只调 `LogAll()`；读侧另有 `Snapshot()` 深拷贝 |
| `SDS_Diag.ErrorOnce` / `Warn`（Diag） | 扫描期异常记录 | 扫描器自身失败时调用 |
| `SDS.ThirdModCompat` 程序集 | 9 个适配器与 Sdk 工具类 | 不静态引用，只按接口发现 |

Bootstrap 不向第三方提供任何 `public` 入口：`SDS_Mod` 只服务 RimWorld Mod 发现（《ModuleResponsibilities.zh-CN.md》二）。第三方接入点是 `SDS_API`（`Version = 4`，soft-depend 反射探测，见《ModuleResponsibilities.zh-CN.md》七与《PublicAPI.zh-CN.md》§3.8），不是 Bootstrap；适配器只实现 `ISDSCompatAdapter` 由 Bootstrap 扫描发现，不引用 Bootstrap（理由见[DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) §三·3.2）。

### 6.2 适配器扫描机制

扫描发生在启动第 4 步，并在第 6 步 `ExecuteWhenFinished` 复扫一次（顺序见《ModuleRules.zh-CN.md》九·4）。实现位于 `SDS.Bootstrap/SDS_CompatLoader.cs`。

| 阶段 | 做法 | 源码 |
|---|---|---|
| 枚举程序集 | `AppDomain.CurrentDomain.GetAssemblies()`；跳过 null 与动态程序集 | 47–57 |
| 取类型 | `assembly.GetTypes()`；`ReflectionTypeLoadException` 取 `e.Types`，其它异常返回空数组 | 173–187 |
| 过滤 | 跳过抽象类、接口、非 `ISDSCompatAdapter` 实现 | 59–68 |
| 实例化 | `Activator.CreateInstance(type)`；失败记 `ErrorOnce` 并跳过 | 75–85 |
| 读取身份 | `Name` / `TargetPackageId` 读取失败记 `ErrorOnce`；空名回退 `FullName` | 91–107 |
| 名称去重 | `ownerByName` 以 `Name` 为键；同名但不同类型 → 记 `ErrorOnce`，跳过后注册者 | 109–123 |
| 目标检测 | `IsTargetLoaded` 为 false → 只计入 `notLoaded`，不调用 `Apply`，不写入 `attempted` | 125–142 |
| 幂等 | `attempted` 已含该名则直接返回；**先登记再执行**，`Apply` 抛异常也不会在复扫中重试 | 143–160 |
| 执行 | `adapter.Apply()` 成功才累加 `appliedCount` | 150–154 |
| 异常隔离 | `Apply` 异常记 `ErrorOnce`，并由 `RegisterBrokenReport` 补一条 0/0 记录 | 155–171 |

- 目标未装时完全静默：不 `Apply`、不登记报告；目标已装但关键成员探测失败时，由适配器自己登记 `detectedButBroken`（《DiagnosticsCoverage.zh-CN.md》§三）。
- 扫描器不持有适配器实例：每次 `Scan` 通过 `Activator.CreateInstance` 重新创建，因此适配器必须有无参构造（`SDS_CompatLoader.cs` 75–85）。
- 扫描器不调用 `SDS_Diag.Fail`：Compat 层只允许 `ErrorOnce` / `Warn` / 报告中心（《ModuleRules.zh-CN.md》六·6.6）。

### 6.3 启动报告与第三方可见面

- `SDS_CompatReportHub.LogAll()` 的输出口径（`detected` 过滤、0/0 的两种分支、失败明细格式）以《SDS.Diag/Interfaces.zh-CN.md》五·5.2 为唯一权威；Bootstrap 只在启动第 7 步调用，不复述规则。读侧 `Snapshot()` 返回逐项深拷贝；启动报告使用 `LogAll()` 就地输出，不持有 Hub 内部列表。
- 报告只在主线程写：`Apply` 由 Bootstrap 主线程调用，Hub 内部 `List` 无锁（《ModuleResponsibilities.zh-CN.md》四）。
- 设置页不绘制 `endpointStoragePriority`（由核心界面调节）；第三方经 `ISDS_PrioritySource` 读到的是 `SDS_Config.CoreStoragePriority`（`SDS.Vanilla/SDS_VanillaState.cs` 125–137）。

### 6.4 待补

- 9 个适配器在真实 Mod 组合下第 4 步首扫与第 6 步复扫的命中差异（待补，需实机日志）。
- `SDS_CompatReportHub.Reset()` 是否需要随读档或复扫清空（当前 Bootstrap 未调用，待补）。
- 设置页开关变化后，第三方缓存是否需要额外失效通知；目前只有 `SDS_Rules.NotifyEndpointChanged()` 与 `SDS_Vanilla.ApplyStoragePriorityToEndpoints()` 两条直接通知（待补）。
