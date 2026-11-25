# SDS.Bootstrap · 设计决策（为什么这么做 / 踩过的坑）

> 定位：本文件保存 SDS.Bootstrap 的历史决策、旧实现差异、设计取舍与待验证项，回答「为什么这么做」。可检查规则见[Rules.zh-CN.md](Rules.zh-CN.md)，public 面见[Interfaces.zh-CN.md](Interfaces.zh-CN.md)。文中「旧」指重构前的单文件实现（`SDS_Mod.cs` / `SDS_ModCompat.cs`），仅作历史溯源，不再可访问。

## 一、历史决策与坑

| 主题 | 旧做法 / 原因 | 新做法与维护提示 |
|---|---|---|
| 延迟到 `LongEventHandler.ExecuteWhenFinished` | `Mod` 构造时 Def 未加载，写 `ThingDef` 耗电只能推迟（旧 `SDS_Mod.cs` 36–37） | 拆成同步段 + `RunDeferred`；`started` / `deferredRan` 双标记 |
| 逐类装补丁 | 不用 `PatchAll`：一个坏补丁会让整个 Mod 构造失败（旧 49–52） | 由 Vanilla `SDS_PatchInstaller` 承担；Bootstrap 只调用 |
| 单 Harmony 实例 | 旧 `cagier.superdimensionalstorage` 一个实例传给全部适配器 | Vanilla 固定 `sizuru.sds.vanilla`；适配器各自建实例、ID 与名称一致 |
| 复扫幂等 | 旧兼容层只在启动时一次性执行，无复扫 | `attempted` 先登记再 `Apply`；目标未装不写 `attempted`，第 6 步可补接管 |
| 报告改从 Diag 读 | 旧入口直接调 9 个适配器类并 `Hub.Reset()` / `LogAll()` | Bootstrap 只调 `LogAll()`；`Reset()` 不在启动路径调用（`attempted` + `Has(name)` 双闸已防重，属有意取舍） |
| 设置页副作用自吞异常 | `WriteSettings` 抛异常会让设置窗口关不掉，只能杀进程（旧 400–402） | 保留 try/catch + `Log.Error`（`SDS_Mod.cs` 324–332） |
| 主菜单打开设置 | `Instance` 退出存档后不自动置空，可能指向旧组件（旧 450–452） | 统一判 `Current.ProgramState` / `Current.Game` / `Find.Maps` |
| 耗电写入 | Def 级共享；`CompProperties_Power` 实例被其它 Def 共用时必须跳过 | 保留 `Log.ErrorOnce` 跳过 + `SetUpPowerVars()` 重算场上实例 |
| 设置应用差异 | 旧 `ApplySettings` 还调 `SDS_GameComponent_Grid.NotifyStructureChanged()` 与米莉拉 `ClearStoreCache()` | 新入口改为 `SDS_Rules.NotifyEndpointChanged()` + `SDS_Vanilla.ApplyStoragePriorityToEndpoints()`；米莉拉缓存已由适配器按 W33 开关自失效；结构变化由 250-tick 对账 + 建筑 Tick 兜底（有意不立即重评） |
| 「使用」命令报数 | 旧入口 `SDS_UseCommand.ReportProvidersOnce()` | 已恢复：`SDS_Vanilla.ReportUseProvidersOnce()` 由第 7 步调用，输出纳入 / 排除计数与排除表未命中名单 |
| 游戏内验证 | 旧设计只到「补丁 A/B 日志」层 | 新启动报告末句固定输出「游戏内 A/B 验证待跑」；G3 实机验证未完成 |

## 二、旧实现 → 新实现的差异与坑

### 2.1 旧流程中与本模块相关的结论

| 旧节点 | 结论（转述） | 新落点 |
|---|---|---|
| §零·0.1 | `SDS_Mod.ApplyHarmonyPatches` 逐类加载 103 个特性补丁，再应用 29 个注册表条目（19 守卫 + 10 投影 owner），合计 132 补丁点；单点失败不影响其余 | 第 3 步 `SDS_PatchInstaller.Install()`；新计数为 112 + 29（《ModuleRules.zh-CN.md》七） |
| §零·0.2–0.5 | G / L 开关、端点注册、`GameComponentTick` 分频属运行期 | 不在 Bootstrap；归 Vanilla `SDS_GameComponent_Grid` |
| §五·5.11 | W63 运行期枚举全部 WorkGiver 并挂落点守卫，在 Def 加载后执行一次，失败只记日志 | 第 6 步 `SDS_PatchInstaller.ApplyRuntimeHooks()` |
| §五·5.3 / 5.4 | 派活出口与 Job 工厂 | Vanilla，不在 Bootstrap |
| §五·五·5·5.5 | 9 个第三方适配器阶段 4 已迁到只调 `SDS_API` 的翻译层 | ThirdModCompat；Bootstrap 只负责发现与 `Apply` |
| §五·五·5·5.7 | 天工穿梭机装载清单注入（W50） | 适配器 + Api，Bootstrap 不接触 |

## 三、设计取舍

原《实现原因.md》并入本节（其 §四·4 对应本节 3.3 第 4 条）；规则本体见《ModuleRules.zh-CN.md》六·6.7、八、九，签名见《ModuleInterfaces.zh-CN.md》七，不再复述。

### 3.1 为什么必须只有一个装配点

启动七步的顺序由《ModuleRules.zh-CN.md》九定义，这里只解释为什么每一步只能由入口做。

| 装配动作 | 只能由 Bootstrap 做的原因 | 源码证据 |
|---|---|---|
| 设置读取与钳制 | `SDS_Mod` 构造中取 `GetSettings<SDS_Settings>()` 并 `ClampAll`；Mod 实例可能重复构造，装配由 `SDS_Startup` 的 `started` 跳过 | `SDS.Bootstrap/SDS_Mod.cs` 24–34；`SDS_Startup.cs` 18–25；旧 `SDS_Mod.cs` 55–56 |
| 宿主注入 | 11 个宿主对象必须在 Core / Vanilla 的任何调用之前就位；`SDS_Host.Install` 只允许 Bootstrap 调用 | `SDS.Bootstrap/SDS_HostInstaller.cs` 15–50；《ModuleRules.zh-CN.md》五 |
| Harmony 安装 | 补丁是进程级全局的，重复安装会让同一 Postfix 执行两次；安装器用静态 `harmony` 字段保证进程内只装一次 | `SDS.Vanilla/SDS_PatchInstaller.cs` 54–60；旧 `SDS_Mod.cs` 42–61 |
| 适配器扫描 | 扫描枚举 `AppDomain.CurrentDomain.GetAssemblies()`，只能在各 Mod 程序集已进入 AppDomain 之后执行；目标 Mod 可能晚于首次扫描才可探测，因此还要复扫 | `SDS.Bootstrap/SDS_CompatLoader.cs` 45–71；《ModuleRules.zh-CN.md》九·4 |
| 模块注册 | 各模块的 `RegisterAll()` 只在启动阶段调用一次；没有入口就没有统一的调用时机 | `SDS.Bootstrap/SDS_Startup.cs` 58–69 |
| 启动报告 | 报告要汇总补丁、宿主、适配器、静态清理与自检的计数，只有入口同时持有这些数字 | `SDS.Bootstrap/SDS_Startup.cs` 101–114 |

顺序上的三个硬约束：

1. 宿主先于一切消费者。宿主实现类都是 public 无参构造，构造时不读 `Instance` / `State` / `Find` / `Def`，因此可以在 `SDS_GameComponent_Grid` 之前注入且不会空引用（《ModuleInterfaces.zh-CN.md》四·4.2）。
2. 设置先于宿主与扫描。`Settings.ClampAll()` 与 `SDS_Config.Current` 写入发生在构造最早处，后续步骤只读（`SDS.Bootstrap/SDS_Mod.cs` 26–33）。
3. 补丁先于适配器扫描。第 3 步安装的是 Vanilla 特性补丁；第 4 步的第三方动态补丁由适配器 `Apply` 自己创建，二者互不扫描对方程序集（《ModuleRules.zh-CN.md》七、九）。

### 3.2 为什么 Bootstrap 不被其它 SDS 程序集引用

| 理由 | 说明 |
|---|---|
| 依赖方向会成环 | Bootstrap 已引用 Vanilla / Api / Core；若 Vanilla 再引用 Bootstrap，就形成 `Vanilla → Bootstrap → Vanilla`，违反《ModuleRules.zh-CN.md》四的单向图 |
| 插件式反向依赖 | ThirdModCompat 只允许经 Contracts 的 `ISDSCompatAdapter` 被反射发现；适配器不应知道启动器是否存在，否则目标未装时整块兼容层也会被拉进依赖链 |
| 防止绕过 Api | 拿到 Bootstrap 就能直接触碰启动顺序、设置实例与宿主槽，容易形成第二条写路径，违反 E6 / E7（《ModuleRules.zh-CN.md》三） |
| 不是兼容契约 | 适配器的唯一发现面是 Contracts 的 `ISDSCompatAdapter`；Bootstrap 没有版本常量与适配器桥，引用它拿不到任何对外承诺，版本判断应读 `SDS_API.Version`（《ModuleResponsibilities.zh-CN.md》七） |
| 公开面唯一 | Bootstrap 只对 RimWorld Mod 发现暴露 `SDS_Mod`，其余类型 `internal`（《ModuleResponsibilities.zh-CN.md》二、《ModuleInterfaces.zh-CN.md》七） |
| 机器校验 | `CheckAssemblyBoundaries.ps1` 检查程序集引用图与 `SDS_Host.Install` 调用方；越界直接失败（《ModuleRules.zh-CN.md》十） |

`SDS.Bootstrap.csproj` 的项目引用只有 Contracts / Diag / Core / Api / Vanilla，没有 ThirdModCompat；启动路径中也没有任何 `SDS_API` 静态调用。Api 引用是矩阵允许项，不代表入口转发五轴。

### 3.3 设计取舍

1. 延迟到 `LongEventHandler.ExecuteWhenFinished`：把第 6 步整体推迟，可避免启动早期反复判空。代价是启动被拆成同步段与延迟段，需要 `started` / `deferredRan` 两个幂等标记（`SDS_Startup.cs` 14–16、72–81）。
2. 适配器扫描幂等且允许复扫：`ownerByName` 负责名称唯一，`attempted` 保证同一适配器只 `Apply` 一次；目标未装时不写入 `attempted`，因此目标在后续扫描中可见时仍能接管。代价是两次扫描都要遍历全部程序集与类型（`SDS_CompatLoader.cs` 28–31、125–160）。
3. 报告从 Diag 读，不引用 ThirdModCompat：编译期没有 `Bootstrap → ThirdModCompat` 边，适配器全未装时启动路径也不变（`SDS_Startup.cs` 101–114；《ModuleResponsibilities.zh-CN.md》九）。代价是适配器必须自觉登记，漏登记只能由扫描器补一条 0/0 记录（`SDS_CompatLoader.cs` 163–171）。
4. 设置钳制集中在 `ClampAll`：构造、每帧绘制结束、`WriteSettings` 与 Def 应用前都调用，使越界值不会流入 Core / Vanilla；运行期读取统一走 `SDS_Config` 的纯读取口（《ModuleInterfaces.zh-CN.md》一·1.3）。代价是同一方法被多处调用，需要保证它是幂等的（Contracts `SDS_Mod.cs` 53–63）。
5. 失败只记 `ErrorOnce`，不阻止启动：同步段与延迟段各自 try/catch；第 5 步注册有独立兜底，前置步骤失败也照样登记（`SDS_Startup.cs` 53–56、58–69、96–98）。代价是启动不完整时只能从日志与启动报告判断，入口不会弹出阻断性提示。

## 四、待验证

- 实机启动报告的「Vanilla 补丁 A/B = 112/112、宿主 11/11」核对（待补）。

旧实现自此仅作历史存档，维护本模块无需再读。
