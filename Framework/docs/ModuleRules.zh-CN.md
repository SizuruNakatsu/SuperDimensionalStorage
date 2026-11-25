# 模块规则

> 定位：本文是**全局一页**——只写跨模块不可拆的约束与流程；各模块自己的禁止项、存档规则、补丁细节、接口签名都在 `SDS.*/` 目录内，维护单个模块时只需读该目录 + 本文。
> `旧框架/` 自 2026-11-12 起为**纯历史存档**：校验数据（规则索引 / 补丁清单 / 公共API / 诊断覆盖清单）已迁入本框架，校验脚本与实现 / 评审均不读取旧框架。唯一例外是**存档兼容取证**：核对历史 `ExposeData` 的节点名 / 字段别名 / LookMode 时可人工查阅，但不得作为设计依据，也不得修改。

## 一、设计原则

设计原则见 [DesignPrinciples.zh-CN.md](DesignPrinciples.zh-CN.md)，本文只引用、不复述。若本文与其冲突，以其为准。

准则三对文档的推论：同一规则只允许在一个文档定义，其余文档只引用。因此本文只保留跨模块规则，模块专属规则一律下沉到对应模块的 `Rules.zh-CN.md`。

## 二、模块独立规则

1. **每个模块独立项目、独立编写、独立编译**。模块的完成只以 `SDS.Contracts` 中的接口 / 数据为准。
2. 模块之间只允许通过 `SDS.Contracts` 的 DTO 与接口通信；不得引用其他模块的 `internal` 类、实现类或静态状态。
3. 依赖严格单向；`SDS.Vanilla` 与 `SDS.ThirdModCompat` 互不引用；适配器之间仅共享 Sdk 工具类，不互相调用。
4. 每个实现模块只把跨模块契约所需类型设为 `public`，其余 `internal`（白名单见《ModuleResponsibilities.zh-CN.md》二）。
5. **文档自包含**：每个 `SDS.*/` 目录必须能让维护者在不读其他模块**实现**的前提下完成本模块修改；跨模块契约以对方 `*_API.cs` 为签名与语义权威，本模块 `Interfaces.zh-CN.md` 只写**消费面**（实际消费的成员与被依赖的保证）+ **原版 / 第三方接触面**，不抄写签名。
6. 模块之间不依赖实现顺序：验收标准是「本模块文档 + 独立编译 + 独立测试」。

## 三、硬约束

- 旧档 → 新版必须可读；数据不丢优先于格式不变（W26）。
- 112 个特性补丁类 + 29 个注册表条目必须全部保留；补丁只在 Vanilla / ThirdModCompat。
- 数量守恒：虚拟 + 保真 + 地图实物 + 容器实物不丢失、不重复。
- 运行期静态缓存必须经 `SDS.Diag` 的注册制清理（E1）。
- 兼容层只经 `SDS.Api` / `SDS.Contracts` / `SDS.Diag`（E6）；每个第三方入口只有一条写接管路径（E7）。

## 四、依赖方向

下列矩阵即编译期允许的 `ProjectReference`，越界由 `CheckAssemblyBoundaries.ps1` 直接失败；产物名见《ModuleResponsibilities.zh-CN.md》一。

| 模块 | 只允许引用 |
|---|---|
| `SDS.Contracts` | RimWorld / Verse / UnityEngine 数据类型 |
| `SDS.Diag` | Contracts |
| `SDS.Core` | Contracts、Diag、数据类型 |
| `SDS.Rules` | Contracts、Diag、Core、数据类型 |
| `SDS.Api` | Contracts、Diag、Core、Rules |
| `SDS.Vanilla` | Contracts、Diag、Core、Rules、Harmony |
| `SDS.ThirdModCompat` | Contracts、Diag、Api、Harmony；目标 Mod 只反射探测 |
| `SDS.Bootstrap` | Contracts、Diag、Core、Rules、Vanilla、Api |
| `SDS.Tests` | 全部模块 + Harmony（仅测试，不进生产输出） |
| `SDS.Core.Tests` | Contracts、Diag、Core（仅测试，不进生产输出） |
| `SDS.Rules.Tests` | Contracts、Diag、Core、Rules + Harmony（仅测试，不进生产输出） |

```text
SDS.Bootstrap
  ├─→ SDS.Vanilla ──┬─→ SDS.Rules ─→ SDS.Core ─→ SDS.Contracts
  ├─→ SDS.Api ──────┤
  ├─→ SDS.Core ─────┤
  └─→ SDS.Diag ─────┴─→ SDS.Contracts

SDS.ThirdModCompat → SDS.Api + SDS.Contracts + SDS.Diag（+ Harmony）
                ↑
        目标 Mod 仅反射探测，不静态引用
```

命名规则：

- 程序集文件名 = 工程目录名 = `SDS.*`；所有类型命名空间保持 `SuperDimensionalStorage`。
- 不新增子命名空间；分层靠程序集边界，不靠命名空间。
- `超维存储/Assemblies` 不保留旧的 `SuperDimensionalStorage.dll`，避免类型重复与 `GenTypes` 歧义。

## 五、可见性与友元

- 未列入《ModuleResponsibilities.zh-CN.md》二白名单的顶层 `public` 类型一律视为泄漏，由 `CheckAssemblyBoundaries.ps1` 做类型级检查。
- **E8 公开面登记**：登记在案的模块，其顶层 `public` 类型只允许声明在该模块登记的公开面文件里。登记表只在 `CheckAssemblyBoundaries.ps1` 的 `$apiSurfaceFiles` 里维护，本文不复制模块清单；其余实现文件全部 `internal`。
- **E8 双白名单**：`SDS.Vanilla` 的公开类型分两类——**模块间面**（`SDS_Vanilla`，只在 `SDS.Vanilla_API.cs`）与**引擎面**（XML 按名实例化 / Scribe 按类型存档，清单见《ModuleResponsibilities.zh-CN.md》二）。引擎面允许散落，但**禁止被其它模块引用**（脚本 §4.7）。
- **E9 公开面文件不承载领域决策**：`*_API.cs` 只做契约声明与一行转发，不写规则；控制流（`if` / `else` / `for` / `foreach` / `while` / `switch` / `try` / `catch`）只允许出现在对外准入门 `SDS_API`——它是唯一登记的例外。
- Vanilla 不对 Core / Api 暴露实现；Core 不引用 Vanilla。
- `SDS_Ledger` 不公开 `PreservedOwner`；保真真身由 Core 私有 owner 持有，Vanilla 不得访问。
- `SDS.Tests` / `SDS.Core.Tests` / `SDS.Rules.Tests` 是测试例外：`SDS.Tests` 可引用全部模块与 `0Harmony`；`SDS.Core.Tests` 只引用 Contracts / Diag / Core；`SDS.Rules.Tests` 只引用 Contracts / Diag / Core / Rules 与 `0Harmony`（P5 起规则 / 流程测试全部在此）；三者只进测试输出。
- `SDS_Diag.Fail` 的调用方限制见《ModuleInterfaces.zh-CN.md》二与各模块 `Rules.zh-CN.md`，白名单不放开这一限制。

`InternalsVisibleTo` 白名单（登记制，新增必须在此登记，脚本对未登记项直接失败）：

| 授予方 | 被授予方 | 用途 |
|---|---|---|
| `SDS.Core` | `SDS.Rules` | 规则 / 流程模块读写账本写接口与派生索引（P3 起） |
| `SDS.ThirdModCompat` | `SDS.Tests` | 程序集公开面为 0（适配器与 Sdk 工具类全 internal），测试直引 internal 类型 |
| `SDS.Vanilla` | `SDS.Tests` | 宿主实现与补丁计数全 internal，测试直引 internal 类型 |

> P3 已落地 `SDS.Core → SDS.Rules`（`SDS.Core/Properties/AssemblyInfo.cs`）；P4–P5 沿用该友元，不新增其它授予。

唯一允许跨模块触碰 Core 状态装配的位置是 `SDS_GameComponent_Grid` 的构造（`SDS_Core.CreateLedger()` + `SDS_Core.Attach`）。原因：账本生命周期挂在 `Game` 组件上，而 Bootstrap 运行时还没有 `Game`。`ExposeData` 只序列化内容，不得兜底重建账本。

`SDS_Host.Install` 只允许 `SDS.Bootstrap` 调用；其它模块调用由脚本报错。

## 六、各模块禁止（索引）

规则正文已下沉，本节只保留编号索引，便于跨文档引用。

| 编号 | 模块 | 规则正文 |
|---|---|---|
| 6.1 | Contracts | [SDS.Contracts/Rules.zh-CN.md](SDS.Contracts/Rules.zh-CN.md) |
| 6.2 | Diag | [SDS.Diag/Rules.zh-CN.md](SDS.Diag/Rules.zh-CN.md) |
| 6.3 | Core | [SDS.Core/Rules.zh-CN.md](SDS.Core/Rules.zh-CN.md) |
| 6.4 | Vanilla | [SDS.Vanilla/Rules.zh-CN.md](SDS.Vanilla/Rules.zh-CN.md) |
| 6.5 | Api | [SDS.Api/Rules.zh-CN.md](SDS.Api/Rules.zh-CN.md) |
| 6.6 | ThirdModCompat | [SDS.ThirdModCompat/Rules.zh-CN.md](SDS.ThirdModCompat/Rules.zh-CN.md) |
| 6.7 | Bootstrap | [SDS.Bootstrap/Rules.zh-CN.md](SDS.Bootstrap/Rules.zh-CN.md) |
| 6.8 | Rules | [SDS.Rules/Rules.zh-CN.md](SDS.Rules/Rules.zh-CN.md) |

## 七、补丁归属

- 112 个特性补丁类全部在 Vanilla；29 个注册表条目（19 镜像守卫 + 10 投影 owner）也在 Vanilla。
- 9 个第三方适配器的补丁是动态 `harmony.Patch`，全部归 `SDS.ThirdModCompat`；不计入 112。
- `SDS_PatchInstallerImpl` 只扫描 `SDS.Vanilla` 程序集。
- 补丁类名保持不变；`PatchInventory.zh-CN.md` 只增加「所属程序集」列。

## 八、Harmony ID

全部动态补丁均在 `SDS.ThirdModCompat` 单程序集内，ID 与适配器名称一致；重复即记 Error 并跳过后注册者。

| 归属 | 适配器 | Harmony ID |
|---|---|---|
| Vanilla | — | `sizuru.sds.vanilla` |
| ThirdModCompat | `SDS_CompatAdapter_PUAH` | `sizuru.sds.compat.puah` |
| ThirdModCompat | `SDS_CompatAdapter_CE` | `sizuru.sds.compat.ce` |
| ThirdModCompat | `SDS_CompatAdapter_UFRecipes` | `sizuru.sds.compat.ufrecipes` |
| ThirdModCompat | `SDS_CompatAdapter_ManipulatorBeam` | `sizuru.sds.compat.manipulatorbeam` |
| ThirdModCompat | `SDS_CompatAdapter_MiliraExpanded` | `sizuru.sds.compat.miliraexpanded` |
| ThirdModCompat | `SDS_CompatAdapter_CeleTechShuttle` | `sizuru.sds.compat.celetechshuttle` |
| ThirdModCompat | `SDS_CompatAdapter_EquivalentExchange` | `sizuru.sds.compat.equivalentexchange` |
| ThirdModCompat | `SDS_CompatAdapter_Versatran` | `sizuru.sds.compat.versatran` |

## 九、启动顺序

1. 读取 `SDS_Settings`，`ClampAll`。
2. `SDS_HostInstaller.Install`：先由 `SDS_Vanilla.CreateHostSlots()` 装配 11 个槽位，再由 Bootstrap 注入 Ports / DepositFilter / Priority / Delivery / MirrorQuery / MirrorPhysical / MedicalCare / ReclaimWorld / Display / DepositJobs / TransportLoad。
3. `SDS_Vanilla.InstallPatches()`（实现在 `SDS.Vanilla`）：枚举本程序集全部 `[HarmonyPatch]` 类，逐类 try/catch 安装；再装 `SDS_MirrorGuardRegistry` / `SDS_ProjectionOwnerBridge` 两张注册表；不扫描其它程序集。
4. `SDS_CompatLoader.Scan`：扫描 `ISDSCompatAdapter`，校验名称（Harmony ID）唯一，目标已加载才 `Apply`；必要时在 `LongEventHandler.ExecuteWhenFinished` 再扫一次（幂等）。
5. 各模块 `RegisterAll()`：向 `SDS_StaticState` 注册 `ISDSResettable`，向 `SDS_SelfCheck` 注册 `ISDS_DiagnosticProvider`。
6. `LongEventHandler.ExecuteWhenFinished`：应用设置到建筑 Def；执行 W63 落点守卫；输出启动报告。
7. 进入游戏 / 读档：`SDS_GameComponent_Grid` 构造 → `SDS_StaticState.ResetAll` → 创建 `SDS_Ledger` → 设置 `Instance` → `SDS_Core.Attach(Ledger)`。

## 十、校验脚本

| 脚本 | 内容 | 运行时机 |
|---|---|---|
| `CheckRuleIds.ps1` | 规则 ID 与代码锚点 | 每次 L2 变更提交前 |
| `CheckRuleSingleSource.ps1` | 规则唯一实现：每条 `// RULE:<ID>` 标记必须恰有一处且落在登记文件；Vanilla / Compat 禁止重复 `min(…, available)` / `total − reserved` 算式；分散型规则（W1 / W5+W54 / W38–W48）的关键原语只允许登记文件调用；Compat 禁止直连 Core / Ledger / Vanilla | 每次改规则实现 / L2 变更前 |
| `CheckPatchInventory.ps1` | 按程序集对账；Vanilla 112；第三方适配器动态挂点白名单 | 每次改补丁 / 挂点后 |
| `CheckCompatSurface.ps1` | 仅允许 Api / Contracts / Diag；禁止 Core / Vanilla；适配器不得调用 `SDS_Diag.Fail` | 每次改兼容层 / 桥面后 |
| `CheckAssemblyBoundaries.ps1` | 程序集引用图 + 类型级白名单 + 独立规则 + `InternalsVisibleTo` + `SDS_Host.Install` / `SDS_Config.Current` 写入点 | 每次改程序集引用 / 公开面后 |
| `DumpPublicSurface.ps1` | 公开面清单：从 `*_API*.cs` 提取公开类型 / 成员；`-Write` 更新 `Tools/PublicSurface/` 基线，`-Check` 与基线对账 | 每次改 `*_API*.cs` 或公开面后 |
| `BuildModule.ps1` | 构建范围（E11）：用 `Tools/.build-state.json` 记录各模块源码 / 公开面 / DLL 指纹与 GameDir / props / SDK / 引用程序集指纹；源码未变且 DLL 指纹一致则跳过，源码未变但 DLL 指纹不一致则拒绝；只按 ProjectReference 拓扑构建源码变化的模块（逐个 `--no-dependencies`），源码未变的 DLL 不得被重写；引用态变化需 `-AcknowledgeReferenceChange`，公开面变化或 `InternalsVisibleTo` 友元耦合导致的依赖重编需 `-AllowChanged`，显式全量重编用 `-ForceRebuild`；`-InitializeState` 只建基线不构建，`-DryRun` 只打印计划；构建后仍按 SHA256 复核并回滚未批准变化 | 每次构建 |
| `TestModule.ps1` | 测试构建范围：可选先调 `BuildModule.ps1`（引用态变化的确认 `-AcknowledgeReferenceChange` 透传），随后只构建测试工程（`--no-dependencies`），前后比对 `超维存储/Assemblies/*.dll` SHA256；任何生产 DLL 变化退出码 1；通过后 `dotnet test --no-build --no-restore` | 每次跑测试 |

## 十一、硬验收

- 行为一致：五轴手工矩阵；112 个 Vanilla 补丁 A/B = 112/112；每个第三方适配器单独 A/B；数量守恒。
- 旧档可读：存档锁定清单（《ModuleInterfaces.zh-CN.md》八）全部恢复；未解析 def 保留；保真真身不丢；预留 / 回收 / 受保护输出审计恢复。
- 结构校验：模块引用图单向；禁止符号为 0；新档保存 → 读回 → 再保存节点名与字段名逐字一致。

## 十二、文档地图

| 位置 | 内容 | 何时读 |
|---|---|---|
| [DesignPrinciples.zh-CN.md](DesignPrinciples.zh-CN.md) | 三条准则（唯一权威） | 决策冲突时 |
| 本文 | 全局一页 | 每次开工 |
| [ModuleResponsibilities.zh-CN.md](ModuleResponsibilities.zh-CN.md) | 模块清单、产物、public 白名单、项目结构、旧文件归属 | 确认边界时 |
| [ModuleInterfaces.zh-CN.md](ModuleInterfaces.zh-CN.md) | 跨模块共享契约索引、适配器桥登记、存档锁定清单 | 跨模块改动时 |
| [DataFlow.zh-CN.md](DataFlow.zh-CN.md) | 主流程的数据形态与流向（不定义规则、不复述字段） | 理清链路 / 改跨模块数据时 |
| [RuleIndex.zh-CN.md](RuleIndex.zh-CN.md) | 规则 ID ↔ 规范句 ↔ 权威文档 ↔ 代码锚点 ↔ 机器校验（登记处，不定义规则） | 改规则 / L2 提交前 |
| [PatchInventory.zh-CN.md](PatchInventory.zh-CN.md) | 112 特性补丁 + 29 注册表条目 + 动态补丁登记（`CheckPatchInventory.ps1` 读取） | 改补丁 |
| [PublicAPI.zh-CN.md](PublicAPI.zh-CN.md) | 五轴契约、调用方上下文、桥派生表（`CheckCompatSurface.ps1` 读取） | 改 Api / 适配器 |
| [DiagnosticsCoverage.zh-CN.md](DiagnosticsCoverage.zh-CN.md) | 诊断能力对账与指标（`CheckPatchInventory.ps1` 对账） | 改日志 / 补丁 |
| `SDS.*/README.zh-CN.md` | 该模块一页纸（定位 / 依赖 / 产物 / 阅读顺序） | 维护该模块 |
| `SDS.*/Interfaces.zh-CN.md` | 消费面（依赖的外部成员与保证）+ 原版 / 第三方接触面 + 签名索引（指向 `*_API.cs`） | 维护该模块 |
| `SDS.*/Rules.zh-CN.md` | 该模块可检查规则 + 规则正文（权威） | 维护该模块 |
| `SDS.*/DesignDecisions.zh-CN.md` | 历史决策、旧实现差异与坑、设计取舍（规则正文见 `Rules.zh-CN.md`） | 追溯设计来源 |
| [SDS.Vanilla/CompatibilityRetirement.zh-CN.md](SDS.Vanilla/CompatibilityRetirement.zh-CN.md) | 旧档兼容项台账：支持窗口、每项只对哪些档有意义、判定与退役前置 | 决定删除兼容分支时 |

