# SDS.Vanilla 拆分方案

> 定位：本目录对 `SDS.Vanilla.dll` 体积问题的拆分评估与实施方案。只记录方案与依据，不改任何规则；规则仍以 [Rules.zh-CN.md](Rules.zh-CN.md) 与《ModuleRules.zh-CN.md》为准。
> 现状（2026-11-12 复核）：53 个 `.cs` 文件、约 23,866 行；除 `Properties/AssemblyInfo.cs` 外全部平铺在根目录，单一命名空间 `SuperDimensionalStorage`。

## 一、结论

1. **程序集级拆分（新增 csproj）不可行**——经实测核实会违反本 Mod 原则与模块独立原则（依据见 §二，三条硬事实）。
2. **合规拆法：程序集不动，源码目录按职责簇分 9 个子目录**——5 个校验脚本均按「文件名递归解析」锚定；唯一例外是 `CheckAssemblyBoundaries.ps1` 的公开面登记文件存在性检查原本只查模块根目录，`SDS.Vanilla_API.cs` 移入 `Install/` 后已改为 `-Recurse`（一行工具修正，不改校验规则）；其余脚本与登记文档零改动。按 E4 属 L1（公开面 diff 为空）。
3. **可选第二步：7 个千行大文件按类拆小**——需同步PatchInventory.zh-CN.md 统计节与规则索引约 17 处锚点；建议只在文件成为维护瓶颈时按热区渐进拆，不做一次性批量拆分。

> 目录只承担导航职责，**不是依赖边界**。所有子目录仍编译进同一程序集、共享同一命名空间；跨簇 internal 直引是设计事实，不允许据此把目录解释成「9 个模块」。

## 二、为什么不能拆程序集（三条硬事实）

1. **112 个补丁类散布在 17 个文件里**，一半内嵌在功能文件内部（SDS_PreservedUsage 9 个、SDS_MirrorBehavior 8 个、SDS_Search 6 个、SDS_Selection / SDS_PreservedOffer / SDS_UI 各 5 个……显式 `Patch_*.cs` 只占 50 个）。按功能簇拆程序集必然把补丁计数拆散，直接触碰硬约束「112 个特性补丁类必须全部保留；补丁只在 Vanilla」+ `SDS_PatchInstallerImpl` 只扫本程序集 + Harmony ID 表 + CheckPatchInventory 按源码文件对账 + 启动顺序五处规则。
2. **簇间耦合是网状而非分层**：`SDS_Exposure` 被 10 个文件跨簇消费（交易 / 镜像 / 保真 / 运输 / 门面）；`SDS_OutputUtility` 16 处、`SDS_PlacementUtility` 13 处被几乎所有簇消费；`SDS_UI ↔ SDS_PreservedUsage` 双向引用。拆出的子模块之间只能走 internal 状态通信——要么公开化内部类型（违反 E8 白名单），要么批量 `InternalsVisibleTo`（违反登记制精神），形成「模块间只通过 Contracts DTO 与接口通信」无法成立的网状伪模块。
3. **E8 引擎面类型禁止被其它模块引用**：`Building_SDS_*`、`JobDriver_SDS_*`、`SDS_Proxy_*`、`SDS_Comp*` 等按名实例化 / 存档的类型与业务逻辑同文件共存，天然属于 Vanilla 单程序集。

> 《ModuleRules.zh-CN.md》二：「模块之间只允许通过 SDS.Contracts 的 DTO 与接口通信；依赖严格单向」——Vanilla 的 9 个职责簇共享同一底座（Grid.State、端点句柄、曝光口径），它们是**一个模块的子系统**，不是 9 个独立模块。按职责簇拆目录正是对这一事实的如实表达。

## 三、方案主体：53 文件 → 9 个职责子目录

命名空间保持 `SuperDimensionalStorage` 不变（《ModuleRules.zh-CN.md》四：不新增子命名空间，分层靠程序集边界——子目录只是导航分组，不产生命名空间）。目录名用英文 ASCII，与代码文件风格一致。

| # | 子目录 | 职责定位 | 文件（行数） | 小计 |
|---|---|---|---|---|
| 1 | `Foundation/` 底座 | 存档壳、状态、宿主服务、跨簇共享工具（输出 / 放置）——「事实与执行」层 | SDS_GameComponent_Grid(271)、SDS_VanillaState(316)、SDS_ProtectedOutputs(30，2026-11-16 退役)、SDS_VanillaDiagnostics(288)、SDS_VanillaServices(133)、SDS_CompatHosts(136)、SDS_ReclaimWorldAdapter(75)、SDS_MedicalCareAdapter(22)、SDS_OutputUtility(334)、SDS_PlacementUtility(43) | 10 文件 / 1,648 行 |
| 2 | `Endpoints/` 端点 | 建筑四件套、端点句柄 / 缓冲 / 提供者、访问点、建筑 Comp | SDS_Buildings(1259)、SDS_EndpointHandle(230)、SDS_EndpointUtility(201)、SDS_EndpointProvider(174)、SDS_EndpointBuffer(112)、SDS_AccessPoints(294)、SDS_Comp_DrawExtraOverlay(73) | 7 文件 / 2,343 行 |
| 3 | `Mirrors/` 镜像投影 | 镜像、守卫注册表(19)、投影 owner 桥(10)、代理 Thing、镜像接缝补丁 | SDS_Mirrors(1239)、SDS_MirrorBehavior(257)、SDS_MirrorGuardRegistry(480)、SDS_Projection(443)、SDS_ProjectionOwnerBridge(298)、SDS_Proxies(493)、Patch_Mirrors(291) | 7 文件 / 3,501 行 |
| 4 | `Jobs/` 取放交付 | 各工作类型目的地、交付策略解析、传递层、配方缺料查询、「使用」命令、芯片组件 | SDS_Destinations(1556)、SDS_JobTargets(474)、SDS_Transmission(209)、SDS_BillIngredientQuery(171)、SDS_Use(534)、SDS_Implants(331) | 6 文件 / 3,275 行 |
| 5 | `TradeTransport/` 交易运输 | 交易虚拟交付、编成 / 装载、运输舱虚拟化、对应补丁与 JobDriver | SDS_Trade(1058)、Patch_Trade(214)、SDS_CaravanTransport(999)、Patch_CaravanTransport(167)、SDS_TransportVirtual(1264)、Patch_TransportLoad(455)、JobDriver_SDS_CaravanJob(289) | 7 文件 / 4,446 行 |
| 6 | `Preserve/` 保真 | 保真账单、保真使用、保真供给 | SDS_PreservedBill(842)、SDS_PreservedUsage(799)、SDS_PreservedOffer(671) | 3 文件 / 2,312 行 |
| 7 | `Query/` 读侧可见性 | 「让原版查询口看见超维空间」三件套：曝光计数口径、通用选择层、搜索排序器 | SDS_Exposure(464)、SDS_Selection(555)、SDS_Search(546) | 3 文件 / 1,565 行 |
| 8 | `UI/` 界面 | 侧边栏、超维窗口、输出对话框、存入筛选 ITab、热键 | SDS_UI(1248)、Window_SuperSpace(1279)、Window_SDS_OutputDialog(514)、SDS_ITab_DepositFilter(112)、SDS_Hotkeys(511) | 5 文件 / 3,664 行 |
| 9 | `Install/` 装配接缝 | 公开门面、内部装配、补丁安装器、原版接缝缝合补丁 | SDS.Vanilla_API(71)、SDS_Vanilla(187)、SDS_PatchInstaller(103)、Patch_VanillaSeams(747)、Properties/AssemblyInfo.cs(4) | 4 文件 + Properties / 1,112 行 |

> 注：`SDS_Exposure` 归入 `Query/` 而非 Foundation，因「曝光 / 选择 / 搜索」同属读侧可见性口径，更符合同类职责原则；其 10 处跨簇消费不受目录影响（同程序集 internal 直引）。
> `SDS_OutputUtility` / `SDS_PlacementUtility` 因被 16 / 13 处跨簇消费，归 `Foundation/`，避免被误读为「只服务端点」。

**归簇依据**：簇边界沿用 [README.zh-CN.md](README.zh-CN.md) §一职责表的既有分类（建筑 / 端口、镜像 / 投影、112 补丁、JobDriver、UI、交易 / 编成 / 装载、存档壳），仅把「跨簇共享的查询口径」独立为 Query 簇、把「门面与安装」独立为 Install 簇——README 职责表无需重写，实施时在表后补一张目录映射即可。

### 范围外（明确不做）

- 不新增 / 删除 / 改任何类型与逻辑，纯文件移动；
- 不动命名空间（保持 `SuperDimensionalStorage`）；
- 不动 csproj、sln、Bootstrap、PatchInventory.zh-CN.md、RuleIndex.zh-CN.md（文件名全部不变）；校验脚本仅 `CheckAssemblyBoundaries.ps1` 的公开面文件存在性检查改为递归，其余不动。

## 四、可选第二步：7 个千行大文件按类拆小

当前超过 1000 行的文件（2026-11-12 实测）：

| 文件 | 行数 | 拆法（按顶层类一类一文件，补丁类跟随宿主逻辑） |
|---|---|---|
| SDS_Destinations.cs | 1556 | 无地图调用方存入端口 / 各工作类型目的地寻址，按类拆 |
| Window_SuperSpace.cs | 1279 | 窗口主体 / 子控件 / 存入筛选区 |
| SDS_TransportVirtual.cs | 1264 | 运输舱虚拟化状态机 / 装载注入，按类拆 |
| SDS_Buildings.cs | 1259 | Building_SDS_Core / _Port / _CapacityArray / _Vault + 端点契约，各一文件 |
| SDS_UI.cs | 1248 | 侧边栏 / 工作报表 / 各补丁类，按类拆 |
| SDS_Mirrors.cs | 1239 | 镜像服务 / 镜像物理操作，按类拆 |
| SDS_Trade.cs | 1058 | 展示物注入 / 成交校验 / 登记表，按类拆 |

`SDS_CaravanTransport.cs` 已降到 999 行，不再列入千行拆分清单。

**登记成本（每拆一个文件）**：

1. PatchInventory.zh-CN.md「统计」节：该文件的补丁类数量行按新文件名重排（CheckPatchInventory.ps1 逐行机械校验，多一行少一行都直接失败——改完跑脚本即验真）；
2. RuleIndex.zh-CN.md「代码锚点」列：锚到该文件名的规则（当前约 17 处，集中在 SDS_Trade.cs 3 处、SDS_CaravanTransport.cs 10+ 处、SDS_UI.cs 1 处）改为新文件名（CheckRuleIds.ps1 校验锚点存在性）；
3. 本目录文档（[README.zh-CN.md](README.zh-CN.md) 职责表、[DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md)）如提及被拆文件，补一行拆分备注；
4. 拆后立即跑 §六 的全量验收；`partial class`（当前 9 个）与嵌套类型必须保持在同一文件，不得顺手改可见性。

**建议节奏**：不做一次性批量拆分；只在某文件实际成为维护瓶颈时按热区拆，每拆一个文件跑一次全量验收。

## 五、为什么零登记成本（脚本锚定方式核实）

| 脚本 | 锚定方式 | 子目录移动影响 |
|---|---|---|
| CheckPatchInventory.ps1 | `Get-ChildItem -Recurse` 提取 [HarmonyPatch]，按**文件名**与PatchInventory.zh-CN.md 统计节对账 | 无（文件名不变） |
| CheckRuleIds.ps1 | 代码锚点**只写文件名**，按文件名递归解析（注释原文：「新布局按模块分目录，所以按文件名递归解析」） | 无 |
| CheckAssemblyBoundaries.ps1 | 类型级白名单按内容检查；公开面登记文件存在性原只查模块根目录 | 已将存在性检查改为 `-Recurse`（`SDS.Vanilla_API.cs` 移入 `Install/`）；类型规则不变 |
| CheckCompatSurface.ps1 | 扫 Api / Contracts / Diag 面，与 Vanilla 内部布局无关 | 无 |
| DumpPublicSurface.ps1 | 反射已编译程序集并与基线比对，与源码目录无关 | 无（移动后 `-Check` 仍必须一致） |

csproj 为 SDK 风格通配编译（无显式 Compile 项），子目录自动纳入；sln 引用的是 csproj 路径，不变；测试按命名空间 / 类型引用，不变。

## 六、执行与验收（批准实施时）

1. 建 9 个子目录，按 §三映射移动 53 文件（当前工作区**不是 git 仓库**：`git mv` 不可用；先决定「先初始化 git 再移动」或「普通移动 + 前后全量校验」，不要照搬 `git mv`）；
2. 构建：`dotnet build -p:GameDir=D:\RimWorld`，0 警告 0 错误；
3. 5 个校验脚本全部通过：`CheckRuleIds / CheckPatchInventory / CheckCompatSurface / CheckAssemblyBoundaries / DumpPublicSurface -Check`（在 `Source/Tools` 下运行）；
4. 135 条单测全部通过；
5. 抽查：`SDS_PatchInstallerImpl` 安装日志仍报 112/112；类型命名空间仍为 `SuperDimensionalStorage`；公开面基线 diff 为空；
6. （若批准第四步）逐个拆千行大文件，每拆一个重复 2–4 步。

## 七、相关事项状态（2026-11-12）

| 事项 | 状态 |
|---|---|
| 4 处重复实现收编 Core（按 def 找可取出键、def 级可用量聚合、CategoryKeyOf 分身、不可重建黑名单分身） | 已完成：`SDS_Rules.TryFindWithdrawableKey` / `GetAvailableCountForDef` / `CategoryKeyOf` / `IsLegacyUnrebuildableDef` 为唯一实现，Vanilla 只转发 |
| 周期性结构对账、首次进档通知信 | 已完成：`SDS_GameComponent_Grid.SweepRegistries` + `SDS_Rules.ReconcileStructures`（250 tick 静默安全网），`SDS_VanillaState.TrySendIntroNotice` |
| 库存变化后的服装优化 / 财富重算提醒 | 已完成：`SDS_GameComponent_Grid.TickStockNudges`（服装 600 tick / 财富 300 tick 去抖） |

本方案不新增规则；目录重组与文件拆小都不改变 `SDS.Vanilla` 的公开面。
