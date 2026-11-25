# SDS.Vanilla 规则

> 定位：本模块可逐条检查的实现约束。W / R / E 的规则正文见本文 §十（权威摘要），S 系的正文以本文 §九 为权威，镜像可见性结论见 §十一；本文其余章节只登记落点与检查方式。
> 签名见 [Interfaces.zh-CN.md](Interfaces.zh-CN.md)；全局硬约束见《ModuleRules.zh-CN.md》；为什么这么做见 [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md)；补丁数量与文件分布见《PatchInventory.zh-CN.md》。

## 一、规则总表

| ID | 规则 | 依据 | 检查方式 |
|---|---|---|---|
| V1 | 禁止引用 `SDS.ThirdModCompat`；不得出现目标 Mod 的 packageId / 类型名 / 适配器分支 | 《ModuleRules.zh-CN.md》2.3、6.4 | `CheckAssemblyBoundaries.ps1` 引用图；`Grep "ThirdModCompat\|Mehni.PickUpAndHaul\|CETeam.CombatExtended"` 应为 0 |
| V2 | 不发明 CRUD 规则：容量、缺口、排序、预留、扣账、回滚只调 `SDS_Core` / `SDS_Rules` / Contracts；Vanilla 只提供事实与执行 | 《ModuleRules.zh-CN.md》6.4 | 人工审查；`Grep "SDS_Core\.\|SDS_Rules\."` 对照 [Interfaces.zh-CN.md](Interfaces.zh-CN.md) 2.3 白名单；新增 `total − reserved` 类算式即失败 |
| V3 | 禁止直接引用 Core / Rules 的 internal 类型：`SDS_WithdrawExecutor`、`SDS_WithdrawTransaction`、`SDS_ReturnUtility`、`SDS_StockRules`、`SDS_Ledger` 的私有 owner（`SDS_ReclaimRegistry` 自 C1 起是本模块 internal，不在此列） | 《ModuleRules.zh-CN.md》6.4；《ModuleInterfaces.zh-CN.md》3.1 | `CheckAssemblyBoundaries.ps1` 类型白名单；编译期 CS0122 即失败 |
| V4 | 112 个特性补丁类 + 29 个注册表条目数量守恒；补丁类名不改；全部在本程序集 | 《ModuleRules.zh-CN.md》七 | `VanillaContractTests.PatchInventory_V2ToV5_HasOneHundredTwelvePatchClasses`；`MirrorContractTests.GuardRegistry_HasNineteenEntries` / `ProjectionOwnerBridge_HasTenEntries`；`CheckPatchInventory.ps1` |
| V5 | 新增原版接缝默认 Postfix + 显式 `Priority.Low`；新增 Prefix 必须显式标优先级、在 §二新增一行登记并说明理由，且不得改变账本语义。存量 Prefix（含动态注册表处理器）与未标 Low 的类按现状保留，不逐一追认，也不得作为新增类的先例；`Patch_SDS_ResourceCounterFastRefresh` 是节流 Prefix，`Patch_SDS_PotentiallyMissingIngredients` 等冻结版行为保留未标优先级 | [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) 三·3 第 1 条；`Patch_VanillaSeams.cs` | `Grep "\[HarmonyPriority\(Priority\.Low\)\]"` + 新增类逐条对照 §二；存量例外见 §二附注 |
| V6 | `SDS_PatchInstaller` 只扫描 `Assembly.GetExecutingAssembly()`，逐类 try/catch；Harmony ID 固定 `sizuru.sds.vanilla` | 《ModuleRules.zh-CN.md》七、九 | `VanillaContractTests.PatchInstaller_HarmonyId_IsStable`；`Grep "GetTypesFromAssembly"` 只允许 1 处 |
| V7 | 镜像物理操作禁止 `mirror.SplitOff`、`mirror.TryAbsorbStack`、直接写账本、未 `Unmark` 的 `DeSpawn`；部分取用 `SDS_OutputUtility.MakeDisplayThing` | 《ModuleInterfaces.zh-CN.md》4.3；W20 | 代码审查 `SDS_MirrorPhysicalOps` 两个方法体：不得出现 `SplitOff` / `TryAbsorbStack` / 写账本 |
| V8 | 存档节点名与 LookMode 锁定：`inputBuffer`（Deep）、`sdsReclaim`（Deep，C8 起归本模块）、`sdsMirrors`（Reference）、`depositFilter`（Deep）、`coreStoragePriority`（Value）、`introNoticeShown`（Value）、`sidebarUI`（Deep）、`transportLoadRequests`（Deep）、`sdsMirrorJobTargets`（Deep，W60） | 《ModuleInterfaces.zh-CN.md》4.1、4.4；《ModuleRules.zh-CN.md》三 | `Grep "Scribe_.*Look"` 对照节点名；存档保存 → 读回 → 再保存逐字比对（待补自动化） |
| V9 | `SDS_GameComponent_Grid` 不实现 `IThingHolder`；`ExposeData` 只序列化内容，不重建账本、不重挂 `SDS_Core` | 《ModuleInterfaces.zh-CN.md》4.1 | `VanillaContractTests.Grid_Shell_CreatesLedgerAndState_WithoutThingHolder` |
| V10 | 运行期静态缓存只经 `SDS_Vanilla.RegisterAll` 登记，共 16 项；展示物缓存只清引用、不销毁实物 | E1；《DiagnosticsCoverage.zh-CN.md》M | `Grep "SDS_StaticState.Register" SDS_Vanilla.cs` 计数 = 16；`VanillaContractTests.Vanilla_RegisterAll_IsIdempotent`；启动日志「登记 N / 清理 M」 |
| V11 | 9 个宿主实现类均为 `internal sealed`，带无参构造；构造时不读 `Instance` / `State` / `Find` / `Def`；每次方法调用现读 | 《ModuleInterfaces.zh-CN.md》4.2 | `VanillaContractTests.HostImplementations_ConstructWithoutGameState`、`VanillaServices_Defaults_WithoutSave`、`EndpointProvider_EmptyWithoutSave` |
| V12 | 失败处理：补丁单点失败 `Warn` 后继续；运行期异常 `ErrorOnce` 后返回安全值；`SDS_Diag.Fail` 只用于数据一致性失败 | 《ModuleInterfaces.zh-CN.md》2.1；《DiagnosticsCoverage.zh-CN.md》四 | 代码审查所有 `catch` 分支；`MirrorContractTests` 空世界用例必须不抛异常 |
| V13 | public 面只允许《ModuleResponsibilities.zh-CN.md》二白名单；其余类型 / 成员保持 internal | 《ModuleRules.zh-CN.md》五 | `CheckAssemblyBoundaries.ps1` 类型级白名单 |

## 二、补丁规则与 Prefix 白名单

> 本表是**类别白名单**，不是逐类清单：存量 Prefix 分布在多个业务文件，完整清单以源码 `Grep "public static bool Prefix"` 为准；新增 Prefix 必须在本表新增一行并说明理由。

| 类别 | 允许的 Prefix / Postfix / Finalizer | 位置 | 约束 |
|---|---|---|---|
| 节流刷新 | `Patch_SDS_ResourceCounterFastRefresh.Prefix` | `Patch_VanillaSeams.cs` | 只按 30 tick 节流触发一次 `UpdateResourceCounts()`，不改返回值；无 `HarmonyPriority`（保留冻结版行为） |
| 拦截型守卫 | 19 条 `SDS_MirrorGuardRegistry` 的 Prefix / Postfix | `SDS_MirrorGuardRegistry.cs` | 每条显式 `Priority`；只返回 false / 过滤，不写账本 |
| 投影 owner 翻译 | 10 条 `SDS_ProjectionOwnerBridge` 的 Prefix / Postfix / Finalizer | `SDS_ProjectionOwnerBridge.cs` | 只翻译取用原语；失败走 `SDS_TakeService.ReturnToGridOrDrop` |
| 写接管 / 校验 | `TradeDealValidate`、`TradeableResolveTrade`、`BeaconLaunchTake`、`SettlementGiveBought`、`CaravanGiveBought`、`CaravanValidate`、`CaravanBlockInstant`、`OwnerTryAdd`、`OwnerTryAddOrTransfer`、`JobMirrorTargets`、`NoWearMirror`、`ReclaimJobTargets`、`TransporterDepositIdentity`、`PortalDepositIdentity` | 各业务文件 | Prefix 只做单次判定 / 单次事务；新增条目必须显式标优先级（默认 Low） |
| 搜索 / 可达 | `ClosestGlobal`、`ClosestGlobalReachable`、`ReachabilityMirror`（Prefix）；`ClosestReachable`、`ClosestRegionwisePrioritized`、`StealValueIgnoresHiddenGrid`（Postfix） | `SDS_Search.cs` | 只重算 / 补候选 / 折算可达，不改账本；区域优先只由 Prefix 型强制执行（S9） |
| 仓库存储判定 | `MirrorIsInValidStorage`、`MirrorStoragePriority`（Prefix）；`MirrorIsInAnyStorage`、`MirrorHaulDestination`（Postfix） | `SDS_MirrorBehavior.cs` | 只回答存储事实，不写账本 |
| 镜像取用 / 吸收 / 携带 | `MirrorAbsorb` / `MirrorAbsorbWithComps`、`MirrorTakeOnSplitOff`、`MirrorCarry` / `MirrorCarryCounted`（Prefix）；`StartCarryAlreadyCarried`（Postfix） | `Patch_Mirrors.cs`、`SDS_StartCarryDepositFold.cs` | 先取真身再交原版；`StartCarryAlreadyCarried` 另承载 W62 自存入账 |
| 非排他预留 | `MirrorReserveNonExclusiveCanReserve` / `...CanReserveStack`（Prefix） | `SDS_MirrorReservations.cs` | 只放行镜像预留；G 关闭时拒绝 / 返回 0（W61） |
| 消费点物化 | `WearConsumeSeam`、`EquipConsumeSeam`、`UseItemConsumeSeam`、`IngestConsumeSeam`（Prefix） | `SDS_MirrorConsumeSeams.cs` | 消费点换真身；失败不接管（W59） |
| 保真专用物化 | `XenogermMaterialize`、`GrowthVatSelectEmbryo`、`CasketAcceptPreservedCorpse`、`PlaceBlueprintForInstall`（Prefix / Finalizer） | `SDS_PreservedUsage.cs` | 先物化真身再交原版；失败阻断或回充 |
| 医疗门 | `MedicalIngredientGate`（Prefix） | `SDS_PreservedBill.cs` | 只按医药等级过滤；判定唯一在 `SDS_MedicalGate`（S17） |
| 玩家持有量 / 热键 | `PlayerHasDef`（Prefix）、`Hotkeys`（Prefix） | `SDS_Selection.cs`、`SDS_Hotkeys.cs` | 只读重算 / UI 输入；不改账本 |
| 财富过滤 | `WealthItemsFilterProjection`（Prefix） | `Patch_Mirrors.cs` | 只按原版 Pawn 口径过滤投影 |
| 离场放行 | `MapDeinitAllowMirrorDespawn` 的 Prefix + Finalizer | `Patch_Mirrors.cs` | 卸载期间临时放行 `AllowDeSpawn` 并在结束后恢复，不做扣账 |
| 销毁守卫 | `MirrorDestroyConsume` / `MirrorDestroyConsumeWithComps` 的 Prefix | `Patch_Mirrors.cs` | W19 fail-closed：只拒绝 / 放行销毁，不做扣账 |

**存量例外（2026-11-17）**：全模块 60 个 Prefix 方法（含动态注册表处理器；112 个特性类中 48 个未标 `HarmonyPriority`），按现状保留、不得作为新增类先例。『写接管 / 校验』行中当前未标 Low 的 8 个：`TradeDealValidate`、`BeaconLaunchTake`、`SettlementGiveBought`、`CaravanGiveBought`、`CaravanValidate`、`CaravanBlockInstant`、`TransporterDepositIdentity`、`PortalDepositIdentity`。其余未标类含 `SDS_PreservedUsage` 9 个、`Patch_Trade` 9 个、`Patch_CaravanTransport` 6 个、`Patch_TransportLoad` 5 个、`SDS_MirrorConsumeSeams` 4 个、`SDS_Buildings` 4 个等；完整清单以源码对账。

新增任何 Prefix / Finalizer 必须同时满足：作用单一、可解释为「原版契约缺口」、不产生第二条扣账 / 派活 / 回滚路径。检查：`CheckPatchInventory.ps1` + 人工 diff。

## 三、Harmony 安装范围

- 只装 `SDS.Vanilla`：`SDS_PatchInstaller.Install` 用 `AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly())`；重复调用幂等。
- 顺序：112 个特性类逐类 `CreateClassProcessor(type).Patch()` → `SDS_MirrorGuardRegistry.Apply` → `SDS_ProjectionOwnerBridge.Apply` → `SDS_CellOnlyWorkGiverPatches.Apply`。特性类逐类 try/catch；两张注册表内部逐条 try/catch；三个 `Apply` 调用本身没有外层 try/catch（若在逐条循环之外抛异常，会中断后续段）。
- 运行期补充：`SDS_PatchInstaller.ApplyRuntimeHooks()` 与 `SDS_CellOnlyWorkGiverBootstrap` 在 Def 加载后重跑 W63 落点守卫，`Applied` 保证幂等。
- 报告：`Log.Message` 输出「A/B = applied/declared」；失败名单 `Log.Warning`；注册表各自输出 A/B。
- 检查：`CheckPatchInventory.ps1`（按程序集对账）、`VanillaContractTests.V4_PatchClasses_ArePresent` / `V5_PatchClasses_ArePresent`、启动日志。

## 四、镜像与投影禁止项

| 禁止 | 原因 | 检查 |
|---|---|---|
| `mirror.SplitOff(count)` | `Patch_SDS_MirrorTakeOnSplitOff` 会把它翻译回取货事务，形成递归 | `Grep "SplitOff" SDS_Mirrors.cs`，物理类内 0 |
| `mirror.TryAbsorbStack(...)` | `Patch_SDS_MirrorAbsorb` 会把镜像吸收翻译成先取货再合并 | 同上 |
| 直接 `Ledger.TryDebitVirtual` / 写账本 | 扣账、回滚只允许 Core 事务 | `Grep "Ledger\.(Try|Get)"` 只允许只读 |
| 未 `SDS_MirrorRegistry.Unmark` 的 `DeSpawn` | `Patch_SDS_MirrorNoDeSpawn` 会拦截，且镜像表残留 | 审查 `TryTakeWholePhysical` / `EvacuateForStructureChange` |
| `ThingMaker.MakeThing` 直接造展示物 | 会丢失键 / vitals / 品质材质 | 统一走 `SDS_OutputUtility.MakeDisplayThing` / `MakePreservedDisplayThingFrom` |
| 镜像参与 `ResourceCounter` 重复计数 | 同一批镜像被端点投影与 spawned 列表各算一次 | `Patch_SDS_MapChildHoldersExposure` 摘端点 / 金库；`MirrorContractTests.Exposure_NoWorld_ReturnsZero` |

## 五、存档规则

| 对象 | 节点 / 字段 | 规则 | 检查 |
|---|---|---|---|
| `Building_SDS_Core` / `Building_SDS_Port` | `inputBuffer`（`Scribe_Deep`，owner=this） | 读档 `PostLoadInit` 为空时补建；缓冲内容由 Tick 兜底数字化 | `Grep "inputBuffer" SDS_Buildings.cs` |
| `SDS_GameComponent_Grid` | `sdsReclaim`（`Deep`） | 读写都只在本组件（与本账本节点同级，新旧档同一读路径）：`PostLoadInit` → `ImportFrom`；`FinalizeInit` 兜底再导入 → `AuditRestored`（Job 目标审计 + 受保护输出巡检）。**账本不读该节点**，否则重复 | `Grep "sdsReclaim" SDS_GameComponent_Grid.cs`；`LedgerStructureTests` |
| `SDS_VanillaState` | `sdsMirrors`（`Reference`） | 读档可能含 null；只销毁旧投影，不写账本；随后 `NotifyStockChanged` | [Interfaces.zh-CN.md](Interfaces.zh-CN.md) 1.8；`MirrorContractTests` 空世界 |
| `SDS_VanillaState` | `depositFilter`（`Deep`） | `ThingFilter` 回调不落盘；读档后用 `NotifyFilterChanged` 重建并 `CopyAllowancesFrom` | `Grep "CopyAllowancesFrom" SDS_VanillaState.cs` |
| `SDS_VanillaState` | `coreStoragePriority`（`Value`，默认 -1） | -1 回退 Mod 设置；写入前 `SDS_Config.Clamp`；变更后 `NotifyEndpointChanged` | `Grep "coreStoragePriority"` |
| `SDS_VanillaState` | `sidebarUI` / `transportLoadScratch` | `Deep`；读档为空时补默认对象 | `Grep "sidebarUI\|transportLoadRequests"` |
| 旧档迁移 | 未知 def / 缺失字段 | 保留未解析项，不丢数据；一次 `Warn` 后继续 | 代码审查 + 读档手工验证 |

禁止：在 `ExposeData` 里 `new SDS_Ledger()`、`SDS_Core.Attach`、清空账本或重排节点顺序。

## 六、静态清理 16 项（`SDS_Vanilla.RegisterAll`）

| # | 清理入口 | 清什么 |
|---|---|---|
| 1 | `SDS_MirrorService.Reset` | 镜像表、脏标记、版本、金库锚点缓存、访问点、暴露缓存、展示物缓存 |
| 2 | `SDS_ReclaimRegistry.Reset` | 回收账本条目（随 C1 迁入本模块） |
| 3 | `SDS_ProjectionService.Reset` | 投影 owner / holder 缓存 |
| 4 | `SDS_OutputUtility.ResetDisplayCaches` | 展示物键表与缓存引用（不销毁实物） |
| 5 | `SDS_MirrorHaulDestination.Reset` | 每图仓库代理 |
| 6 | `SDS_Exposure.Reset` | 资源条刷新缓存与复用缓冲 |
| 7 | `SDS_DepositDispatchLedger.Reset` | 在途 / 失败退避（pawnID / thingID 键） |
| 8 | `SDS_Selection.Reset` | 可及性计数缓存 |
| 9 | `SDS_CaravanVirtual.Reset` | 编成展示物与桶绑定 |
| 10 | `SDS_TransportVirtual.Reset` | 装载计划、同 tick 快照、桶绑定 |
| 11 | `SDS_OfferBridge.Reset` | 双通道供给 offer 表（保真 / 虚拟共用） |
| 12 | `SDS_PreservedUsage.Reset` | 保真使用缓存 |
| 13 | `SDS_TradeVirtual.Reset` | 交易展示物与桶 |
| 14 | `SDS_ReadOnlyUI.Reset` | 只读清单计数缓存 |
| 15 | `SDS_CellOnlyDestinationFold.Reset` | W63 cell-only 落点登记（Job 实例 → 端口 / 自身） |
| 16 | `SDS_CellOnlyWorkGiverPatches.ResetDiagnostics` | W63 改写 / 探针 / 退让诊断计数（不跨档累加） |

另外 `SDS_SelfCheck.Register(new SDS_VanillaDiagnostics())` 只登记诊断 provider，不计入 16 项。检查：`Grep "SDS_StaticState.Register"` 计数、`VanillaContractTests.Vanilla_RegisterAll_IsIdempotent`、启动日志。

## 七、宿主与失败口径

- 9 个宿主实现类均为 `internal sealed`，带无参构造；`SDS_Host.Install` 可以早于 `SDS_GameComponent_Grid`，不得出现 NRE。
- 宿主方法应尽量不向上抛异常；异常密集或会访问原版对象的实现使用 `try/catch` + `SDS_Diag.ErrorOnce` + 返回安全值。当前简单取值转发（`SDS_MedicalCareAdapter.Allows`、`SDS_VanillaServices.Allows/Priority/TryPlace/TryGiveToPawn`）未额外捕获，依赖被调方不抛；新增宿主方法必须显式处理异常。
- 补丁安装失败：`SDS_Diag.Warn` + 继续；运行期补丁异常：`ErrorOnce` + 保留原版结果。
- `SDS_Diag.Fail` 使用范围：金库 def 缺失 / 无空格 / 创建失败、投影取用或落地失败、取货点不可预留等数据一致性问题；正常拒绝（无库存、不可达、筛选拒绝）保持静默。
- 检查：`VanillaContractTests.HostImplementations_ConstructWithoutGameState`、`MirrorContractTests.*_NullInputs_AreSafe`、代码审查全部 `catch` 块。

## 八、医疗等级判据的唯一实现（S17）

- 判据只在 `SDS_MedicalCareAdapter.Allows`：`WorkGiver_DoBill.GetMedicalCareCategory(billGiver).AllowsMedicine(def)`；非药品 / 非 Pawn billGiver 恒放行（原版口径 = Best）。
- 唯一入口是 Contracts 的 `SDS_MedicalGate.Allows(billGiver, def)`：账单派活（`IsUsableIngredient` 补丁、保真 / 虚拟原料筛选）与保真药品供给（`HealthAIUtility.FindBestMedicine` 兜底）全部走它；不得再写 `GetMedicalCareCategory` / `AllowsMedicine` / `playerSettings.medCare.AllowsMedicine`。
- 原「患者自选等级」分支已删除：`patient.playerSettings == null` 时按原版 `GetMedicalCareCategory` 的 Best 处理，不再按 `NoMeds` 拒绝。
- 补料侧的医药门由 Vanilla / Api 经 `SDS_MedicalGate.Allows(billGiver, def)` 取值，并作为 `extraAllows` 注入 `SDS_Rules`（P2 起；此前为 Core）；`SDS_BillSupplyPlanner` 不读 `SDS_Host.MedicalCare`，也不复制判定。
- `Patch_SDS_MedicalIngredientGate` 继续只拦 `Bill_Medical` + Pawn 患者；其他形态一律放行，保持原版行为。
- 宿主未注入时放行（功能按不可用处理），不得抛异常或阻断派活。
- 检查：`grep "GetMedicalCareCategory\|AllowsMedicine"` 只应命中 `SDS_MedicalCareAdapter.cs`（注释除外）；`BillMedicalGateTests.VanillaMedicineHelper_DelegatesToHostPolicy`、`VanillaContractTests.MedicalCare_NonMedicineAndNonPawnBillGiver_Allow`。

## 九、S 系：搜索适配规则

> 本节是 S2 / S4 / S9 / S10 / S12a / S12b 的权威正文（原载《原版支持.md》§六，该文件已并入 [Interfaces.zh-CN.md](Interfaces.zh-CN.md) 四·6）；顶层《RuleIndex.zh-CN.md》的 S 系「依据」列按本节核对。适配器的原版接触面见 [Interfaces.zh-CN.md](Interfaces.zh-CN.md) 4.6。

| ID | 规范句 | 落点 | 检查方式 |
|---|---|---|---|
| S2 | 同一候选 + 同一投影 = 所有搜索适配器得到相同距离 / 可达 / 仓储状态。 | `Patch_SDS_ClosestGlobal`、`Patch_SDS_ClosestGlobalReachable`（`SDS_Search.cs`） | 代码审阅 |
| S4 | 不允许任何搜索适配器重复实现距离公式；访问点折算只经唯一实现。 | `Patch_SDS_ClosestGlobal`、`Patch_SDS_ClosestGlobalReachable`；距离折算唯一实现在 `SDS_AccessPoints.cs` | 代码审阅 |
| S9 | 区域优先只约束能控制阶段与候选集的适配器（Prefix 型）；`ClosestThingReachable` 与 `ClosestThing_Regionwise_ReachablePrioritized` 是 Postfix 型，无法区分 Region / Global 阶段，统一按 Global 语义与原版 best 二选一（比较键 = `priorityGetter → effectiveDistance → tieBreaker`），允许在镜像严格更优时替换原版非空区域结果；这是记录在案的偏差。若将来需要严格区域优先，只允许新增 Prefix 型适配器，不得在 Postfix 内复制区域 BFS。 | `Patch_SDS_ClosestReachable`、`Patch_SDS_ClosestRegionwisePrioritized`（`SDS_Search.cs`） | 代码审阅 |
| S10 | Source / Destination 的 EffectiveCell 分开；source 的镜像 / 保真依赖 root。 | `Patch_SDS_ReachabilityMirror`、`Patch_SDS_MirrorTargetGoto` | 代码审阅 |
| S12a | 无 `priorityGetter`（`ClosestThingReachable`）：Postfix 型适配器只做「补候选 vs 原版 best」的二选一比较，不重排区域候选、不跨 Region / Global。 | `Patch_SDS_ClosestReachable`（`SDS_Search.cs`） | 代码审阅 |
| S12b | 有 `priorityGetter`（`ClosestThing_Regionwise_ReachablePrioritized`）：同样只做二选一比较；比较键 = `priorityGetter → effectiveDistance → tieBreaker`。 | `Patch_SDS_ClosestRegionwisePrioritized`（`SDS_Search.cs`） | 代码审阅 |

## 十、规则正文（权威）

> 本节是 R / W / E 及 S15 / S17 / S18 的规则正文摘要。顶层《RuleIndex.zh-CN.md》按本节核对。

| ID | 规范句摘要 | 本模块落点 |
|---|---|---|
| R15 | 交易 / 捐献 / 发射读「物理存量 − 事务预留」，不因品类禁用归零 | `SDS_Trade.cs`、`SDS_Availability.GetTradableCount` |
| R16 | 读路径不写交易登记表 / 保真桶映射 | `SDS_Trade.cs`（只读注入） |
| R17 | G 关闭时所有读侧可用量归零，账本保留 | `SDS_Exposure.cs`、`SDS_EndpointProvider.IsLocalUsable` |
| S15 | 镜像 / 金库不进入 NPC 偷窃目标与偷窃价值 | `SDS_Search.cs` |
| S17 | 本 Mod 医疗候选必须过原版医药等级判据 | `SDS_BillIngredientQuery.cs`、`SDS_PreservedBill.cs`、`SDS_VirtualBillFallback.cs` |
| S18 | 设置开启时，非玩家派系不得取用超维库存：镜像补候选、镜像可达性、Job 目标物化、携带、装备 / 衣物 / 背包接收与端点取出统一经 `SDS_AccessRules.BlocksNonPlayer` 拒绝；玩家派系与设置关闭时行为不变 | `SDS_AccessRules.cs`、`SDS_Search.cs`、`SDS_AccessPoints.cs`、`SDS_JobTargets.cs`、`SDS_Mirrors.cs`、`SDS_Transmission.cs`、`SDS_EndpointHandle.cs` |
| W2 | 投影 owner 取用 / 物化走统一取货 | `SDS_ProjectionOwnerBridge.cs`、`SDS_Mirrors.cs` |
| W5 | 镜像作为 Job / 携带 / 拆分目标时先取真身：取真身发生在消费接缝（W58 / W59）或取货原语，不在 `StartJob` 派活期物化 | `SDS_Mirrors.cs`、`SDS_JobTargets.cs` |
| W18 | 未 `Unmark` 的镜像拒绝 `DeSpawn`；地图卸载临时放行 | `SDS_MirrorGuardRegistry.cs`、`SDS_Mirrors.cs` |
| W19 | 外部销毁镜像 **fail-closed**：拒绝销毁 + 一次性告警；不得按镜像当前数量整品类扣账；内部清理必须先 `Unmark` | `SDS_MirrorConsume`、`Patch_SDS_MirrorDestroyConsume*` |
| W20 | 镜像不参与合并语义；吸收先取货 | `SDS_MirrorAbsorb`、`Patch_SDS_MirrorNotMergeable` |
| W23 / W24 | 交易成交按行校验；保真只从同一分组桶摘真身 | `SDS_Trade.cs`、`SDS_PreservedBuckets` |
| W25 | 编成差额与装载差额是两个不同口径：编成（出发校验）= `CountToTransfer − 行内未被任何 holder 持有的实物量`（不含展示物）；装载（派活）= `CountToTransfer − 该容器原版在途 Job 已认领量`（`HaulToTransporter` / `HaulToPortal` 的 `initialCount`，不含本 Mod 待派活）。两者不得互换：装载不以地图实物量扣减，因为原版只在找不到可装实物时才让超维空间接管；编成阶段没有该容器的原版在途 Job，故第二项为 0 | `SDS_CaravanTransport.cs`（编成）；`SDS_TransportVirtual.cs`（装载） |
| W28 | 编成 / 装载差额校验只处理「物品 + 带本 Mod 展示物」的行；pawn 行与纯原版行交原版 `CheckForErrors` | `SDS_CaravanVirtual.ValidateQuantities` |
| W26 | 旧档可读；数据不丢优先于格式不变 | `SDS_VanillaState.cs` |
| W27 | 回滚按通道走专用回退 | 本模块只调 `SDS_Rules.ReturnThing` / `SDS_TakeService.ReturnToGridOrDrop` |
| W30 | 直接消费 Job 的镜像目标不在派活期物化；默认交付形态 = Hand。唯一例外：显式登记为 `SpawnedTarget` 的逃生门 Job（当前仅 `BottleFeedBaby`）按缺口 `WillIngestStackCountOf` 物化到端点、不夹携带量；逃生门内 `job.count ≤ 0` 按 1 件；未登记 Job 的数量缺失由 W58 处理，不得回落成 1；显式 `count = 1` 由调用方决定 | `SDS_JobTargets.cs` |
| W31 / W32 | 可安装缩装输出固定 1 件；安装蓝图唯一 | `SDS_UI.cs`、`SDS_PreservedUsage.cs` |
| W34 | 武器 / 衣物强制虚拟化只一次性迁移，不静默转换 | `SDS_Rules.ConvertPreservedGearToVirtual`（Bootstrap 直接调用）；Vanilla 的 `SDS_GameComponent_Grid` 壳不再承担模块面入口 |
| W35 | `IsStudiable` 一律保真且先于默认虚拟判定 | 本模块只经 `SDS_Rules.TryGetStockKey` / 展示物键消费 |
| W36 | 镜像不登记可研究缓存 | `Patch_SDS_MirrorNotStudiable` |
| W37 | 金库锚定本图可用端点；无锚点宽限 250 tick | `Building_SDS_Vault` |
| W38 | 装载行只从该行展示物所属的原版分组桶内取真身；未登记桶的 autoload 行按「展示物 + `TransferAsOne(PodsOrCaravanPacking)`」解析出它自己的那一个桶，不得按键聚合多桶；`HasJob` 与 `JobOn` 共用同一「可计划单位 > 0 且键可用量 > 0 且能决议取物点」判定，不可服务跳过该行，不得据此放行整台运输舱 | `SDS_TransportVirtual.cs`（`TrySelectLoadRow` / `ResolveRowBucket`）、`Patch_TransportLoad.cs`（`HasJobOnTransporter` / `JobOnTransporter`） |
| W39 | 读档恢复装载行欠量 = `CountToTransfer − 未装实物`；展示物绑定同键且未被其它行占用的分组桶，不得按键取第一个桶，也不得多行共享同一展示物 | `SDS_CaravanTransport.cs` |
| W40 | 装舱记账按「真身 → 原版装载行」身份定位行：优先展示物登记桶包含该真身，其次封存品类键等于真身键；命中后真身放回该行 `things`，扣数 / 移除行 / 完成消息仍由原版执行；运输舱 / 传送门 / 穿梭机共用同一身份规则，不得依赖 `TransferAsOne` 同 def 回退定位 | `SDS_TransportVirtual.cs`（`NormalizeDepositedThing`）、`Patch_TransportLoad.cs`（`SubtractFromToLoadList` 前缀） |
| W42 | 装载派活必须使用同一 tick 的判定快照：`HasJob` 生成并登记选择结果，`JobOn` 只消费该快照；不得在 `JobOn` 重新求值可变运行期状态（含 `FindThingToLoad`） | `SDS_TransportVirtual.cs`（`RememberSelection` / `TryTakeSelection`）、`Patch_TransportLoad.cs` |
| W43 | 装载计划只在「本 tick 新建」或「仍是某个小人的当前 Job（含待执行队列）」时视为占用，否则选行时回收；`TryMakePreToilReservations` 失败必须显式释放；落点格守卫的 `JobOnThing` 探针期间不得登记计划（`IsProbing` 闸门） | `SDS_TransportVirtual.cs`（`BuildPendingBusy` / `MakeLoadJob`）、`JobDriver_SDS_CaravanJob.cs`（`TryMakePreToilReservations`）、`Patch_VanillaSeams.cs`（`SDS_CellOnlyWorkGiverPatches.IsProbing`） |
| W44 | 装载对话框刷新只清理展示物身份注册，不得清空其它容器的派活计划 / 同 tick 快照 / 保真桶绑定；全局清理只允许由 `SDS_StaticState.ResetAll` 触发 | `SDS_TransportVirtual.cs`（`ResetForDialog` / `Reset`） |
| W45 | 保真桶身份按「桶内任一真身重叠即同桶」判定；对话框注入去重、装载中重开复用、跨容器展示物隔离共用这一判定，不得用桶首元素代表桶身份 | `SDS_Trade.cs`（`SDS_PreservedBuckets.SameBucket`）、`SDS_TransportVirtual.cs`（`ResolveBucketDisplay`）、`SDS_CaravanTransport.cs`（`TryScanRowIdentities`） |
| W46 | 装载选件数量口径统一为「预留后可用量」（`SDS_Availability`：Total − Reserved，禁用归零）；选行门槛 / 单趟夹取 / 对话框注入 / 穿梭机 autoload 共用同一入口，不得另算物理存量；单趟夹取 = `min(欠量, 携带上限 MaxStackSpaceEver) ∩ 可用量（唯一实现 W56）`：携带部分唯一实现 `ClampCarry`（`ClampLoadTake` 与驱动器共用），可用量只经 `SDS_Availability`，不得内联同式 | `SDS_TransportVirtual.cs`（`ClampCarry` / `ClampLoadTake` / `LoadAvailable`）、`JobDriver_SDS_CaravanJob.cs` |
| W47 | 装载途中读档后，派活身份以预留账本为唯一权威：驱动器必须在物化前按 `job.loadID` 的预留键、从容器内该键所属的那一个原版分组桶重建保真计划；运行期计划 / 待用键不跨存档，不得退化成按 def 任取一件，也不得按键聚合多个桶。同键对应多个不同桶、找不到桶或桶内可计划单位不足额时必须 fail-closed（库存与预留不动） | `JobDriver_SDS_CaravanJob.cs`、`SDS_TransportVirtual.cs` |
| W48 | 装载行读档恢复对运输舱与传送门同一口径：展示物引用不跨存档，按身份 + 欠量重建；读档会丢弃纯超维行的容器必须按品类定位或按欠量重建行，不得按行号硬套 | `SDS_TransportVirtual.cs`（`TryGetRestorableRowKey` / `TryGetRestoreDisplay`）、`SDS_VanillaState.cs`（`CollectTransportLoadRows` / `TryRestoreTransportLoadRows`） |
| W49 | 在途物化标记只在交付期间有效，回充 / 落地 / 失败退路必须撤销 | `SDS_Implants.cs`（经 Core 统一撤销）、`SDS_Use.cs` |
| W50 | 第三方自建清单只经唯一注入实现 | `SDS_CompatHosts.cs`、`SDS_TransportVirtual` |
| W52 | 设置「允许优先级流出」开启且「最高优先级」关闭时，镜像进入原版搬运候选；只有存在严格更高优先级且允许进入的储存目的地时才流出，账本只消耗 total − reserved，目标格 / 容器与镜像 Job 预留遵循原版；设置关闭或「最高优先级」开启时不流出 | `Patch_Mirrors.cs`、`SDS_Mirrors.cs` |
| W53 | 镜像代表账本本身，不是可再入库的实物：`SDS_EndpointUtility.IsDepositable` 是「可存入实物」的唯一判据，端点接收（`IHaulDestination.Accepts`）、存入竞争与手动存入一律拒绝镜像；镜像的存储事实由每图仓库代理唯一提供（`CurrentHaulDestinationOf` 返回代理，`CurrentStoragePriorityOf` 取存档级优先级，`IsInAnyStorage` / `IsInValidStorage` 为真），W52 的「严格更高优先级才流出」据此成立 | `SDS_EndpointUtility.cs`、`SDS_Destinations.cs`、`SDS_MirrorBehavior.cs` |
| W54 | 镜像真身位置（隐藏金库）永远不是取货点：镜像寻路、携带与存入执行器在任何情况下都不得按真身坐标走位；无可用端点时按目标不可达结束 Job（fail-closed）；不在端口范围内的取货一律拒绝；镜像不得经 `ThingOwner` 进入小人所属 owner（背包 / 装备 / 衣物），第三方搬运（含 PUAH）的候选与派活一律排除镜像，卸货清单中的镜像引用按品类键映射回背包真身 | `SDS_JobTargets.cs`、`SDS_Mirrors.cs`、`SDS_Destinations.cs`、`SDS_Transmission.cs`、`SDS_ModCompat_PUAH.cs` |
| W55 | 设置「最高优先级」开启时，超维空间在所有可存实物的搬运判定中视为最高优先级储存：`ListerHaulables.ShouldBeHaulable` 对「已 spawn、非镜像、未禁止、无在途产出标记、可存入且本图有可用端点」的实物返回 true；`SDS_DepositService` 的存入竞争同时忽略「已在最优储存」拒绝；`SDS_DestinationResolver.TryDecideCore` 的存入决议不再与原版目的地比较优先级，直接采用超维端点。功能性目的地与 owner 型容器内容物不在覆盖内；镜像仍按 W52/W53/W54 处理，设置关闭时零行为变化 | `Patch_Mirrors.cs`、`SDS_Destinations.cs` |
| W57 | 所有玩家发起的手动存入入口（右键菜单、存入背包、第三方玩家 UI 的强制派活）必须共用同一手动决议 `SDS_DepositService.TryResolveManualDeposit`：手动 = forced，允许通过在途物化标记闸（`SDS_EndpointUtility.AcceptsForManual`）；自动搬运 / 回充 / 交付路径只允许 `AcceptsForAuto`，新增手动入口不得另写判定 | `SDS_Destinations.cs`、`SDS_EndpointUtility.cs` |
| W58 | 数量缺失的唯一补齐点 = `Toils_Haul.ErrorCheckForCarry`：仅镜像 + `job.count ≤ 0` 时按该 Job 对镜像的 `Reservation.StackCount`（-1 = 整堆）恢复；无预留保持 1；只读、不写账、不预留；恢复后仍由原版夹取与 W56 收口 | `SDS_HaulCountIntent.cs` |
| W59 | 消费点物化的唯一实现 = 直接消费型 driver 的 `Notify_Starting` 前置：Wear / Equip / UseItem 取 1 件；非工具使用者 Ingest 取 `max(1, job.count)`；落点 = 最近可达端点访问点；无端点 / 失败不接管，交回原版失败条件 | `SDS_MirrorConsumeSeams.cs` |
| W60 | 镜像 Job 目标重映射与失效收口（保存 / 读档 / 运行期移除共用）：按「Job loadID + 槽位 + 品类键」落盘；镜像重建后换新镜像；同 key 无效镜像替换先建新镜像、按精确引用改指、再移除旧镜像（新建 / 落点 / 换新失败时回退关闭旧目标并移除）；仅处理仍指向旧镜像（读档清理后已 `Destroyed`）或已失效为 null 的槽，Job 已推进（槽已换成真身等存活物）时跳过、不覆盖、不结束；键失效时当前 Job 核心槽先结束 Job、队列槽只移除该条、排队 Job 出队后再丢弃目标（队列同步移除 countQueue）；任何情况下不写 `LocalTargetInfo.Invalid`；旧档无节点视为空 | `SDS_VanillaState.cs` |
| W61 | 镜像预留不排他：`CanReserve` 对镜像一律放行（忽略 `maxPawns` / 其它预留），原版 `Reserve` 随之登记；`CanReserveStack` 返回镜像堆量；**G 关闭时两处一律拒绝 / 返回 0**（防 Job 死循环）；互斥由账本事务保证；非镜像路径零变化 | `SDS_MirrorReservations.cs` |
| W62 | 任何「存入超维空间」的决议，若执行者是可用端口且可接收该实物，目的地必须折叠为执行者自身端口；物品到手后立刻经唯一存入入口入账；显式指定端口与取货 / 内部回充不适用 | `SDS_SelfDepositFold.cs`、`SDS_Destinations.cs` |
| W63 | cell-only Job 的最佳目的地是超维建筑端点且执行者自身端口不可用时，落点必须改写为该端点 `AccessCellOf`（基础站位口径，不可站时兜底建筑格，不保证可走），由唯一落格入口入账；落点不可达、无法解析或入账失败时按原版「无空余储存空间」退让或落地；最佳目的地基线 = `StoragePriority.Unstored`（与 cell-only WorkGiver 同口径） | `SDS_CellOnlyDestinationFold.cs`、`Patch_VanillaSeams.cs` |
| E1 / E2 | 静态缓存注册制；开发期断言只记 Error | `SDS_Vanilla.RegisterAll`、`SDS_Diag` |

## 十一、镜像可见性结论（权威）

> 本节即镜像可见性规则的权威定义。搜索与可达的距离、排序、过滤边界以本文 §九、§十二.6 为权威，不在本表重复。

### 6.1 计数 / 可及性口径

**口径区分**：本表 API 问的是「是否拥有 / 总量」，用物理总量（不扣预留）；`SDS_Availability`（Total − Reserved）问「现在能取多少」。两个口径不得互相替换。

| 原版 API | 虚拟镜像 | 保真真身 | 本 Mod 处理 |
|---|---|---|---|
| `ResourceCounter.UpdateResourceCounts` / `GetCount` | 1 次/品类（`stackCount` 为账本量） | 仅 `CountAsResource` 另加 | 金库组 + `MirrorExposure` 校正（[Interfaces.zh-CN.md](Interfaces.zh-CN.md) 4.3） |
| `WealthWatcher.CalculateWealthItems` | 默认仅规范图保留；`excludeGridFromWealth` 时全图扣镜像 | 仅规范图补 1 次 | 规范图 `+PreservedWealth`；非规范端点图 `−MirrorWealthOn`；结果夹 0（[Interfaces.zh-CN.md](Interfaces.zh-CN.md) 4.4） |
| `RecipeWorkerCounter.CountProducts` | 资源分支靠 `ResourceCounter`；非资源 1 镜像 = 1 件 | 按真身属性 | `UsesResourceCounter` 跳过；`CountGridProducts` 补 N−1 / 全量 |
| `CountValidThings(List<Thing>)` / `(ThingOwner)` | 1/镜像；金库直接内容为空 | 真身逐件 | 差额补丁 |
| `Alert_NeedWarmClothes.FreeWarmClothesSetsCount` | 1/镜像（补 N−1） | 真身逐件 | 有镜像补 `entry.count − 1`，无镜像补 `entry.count` |
| `PlayerItemAccessibilityUtility.Accessible` / `PossiblyAccessible` / `PlayerCanMake` | 镜像可见 | 真身派生 | 补丁加账本 + 保真：`Patch_SDS_PossiblyAccessible`（拥有量 + 派生量 × `entry.count`）、`Patch_SDS_PlayerCanMake` |
| `PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas(def,count)` / `(filter)` | 镜像（每品类 1 件） | `LedgerTotalForDef` / `HasPreservedMatching` 补 | 补丁整体重算 / 补保真匹配 |
| `RecipeDef.PotentiallyMissingIngredients` | — | — | 逐槽位精确筛选（`Patch_VanillaSeams.cs`） |
| `Bill_Production.GetRepeatInfo` / `ShouldDoNow` / `CanUnpause` | 同 `CountProducts` | 同 | 间接覆盖 |

### 6.2 AI / 房间 / 建筑视角（副作用）

| 原版 API | 影响 | 本 Mod 处理 |
|---|---|---|
| `Room.ContainedAndAdjacentThings` / `GetStat` / `ContainsThing` / `ContainedThings` / `ThingCount` | 镜像影响房间美观 / 清洁 / 空间 / 角色 | `Patch_SDS_RoomIgnoresHiddenGrid` + `RoomContainsThing` / `RoomContainedThings` / `RoomThingCount` 三个过滤补丁 |
| `ListerBuildings` / `ListerArtificialBuildingsForMeditation` / `ListerBuldingOfDefInProximity` | 金库是真实建筑，可能进建筑统计 / 冥想 / 邻近查询 | **无实际影响，不打补丁**：金库 `MarketValue=0` / `Beauty=0`、无 comp、无 `buildingTags` |
| `TantrumMentalStateUtility` | 疯狂小可能把镜像当打砸目标 | `CanSmash` 对镜像 / 金库返回 false |
| `JobGiver_FireStartingSpree` | 可能尝试点燃镜像 | `TryFindRandomIgniteTarget` 过滤镜像 / 金库 |
| `PawnObserver` | 观察 / 恐怖思绪可能锁定镜像 / 金库 | `Patch_SDS_ObserverIgnoresHiddenGrid` 过滤 `PossibleToObserve` |
| `StealAIUtility` / `Trigger_HighValueThingsAround` | 镜像 `stackCount` 代表整品类 → 误触发「袭击转偷窃」 | S15：偷窃组不补镜像候选；`GetValue` 对镜像 / 金库读 0 |
| `Find.StudyManager.UpdateStudiableCache` | 隐藏金库代理被当研究对象 | W36：`Patch_SDS_MirrorNotStudiable` 不登记镜像 |
| `RoomRequirement_ForbiddenBuildings` | 金库可能影响房间需求 | **无实际影响**：金库 def 无 `buildingTags` |

### 6.3 按 API 的「可见次数」结论

| 口径 | 虚拟库存 | 保真库存 |
|---|---|---|
| `ListerThings`（def / group） | 1 次/品类（`stackCount` 为账本量） | 0 |
| `ListerThings.HaulableEver` 区域扫描 | 1 次/品类（金库区域） | 0 |
| `Region.ListerThings` / `ThingGrid` | 1 次/品类（金库区域 / 金库格） | 0 |
| `Map.GetChildHolders` → Map 级枚举 | 0（端点 / 金库被摘掉；由 `spawnedThings` 计 1 次） | 0 |
| `Map.spawnedThings` / `GetAllThingsRecursively(map)` | 1 次/品类 | 0 |
| `ThingOwnerUtility.GetAllThingsRecursively(vault/endpoint/pawn)` | 1 次/品类（投影） | 0 |
| `SlotGroup.HeldThings` | 1 次/品类（金库组） | 0 |
| `AllHaulSourcesListForReading` 直接内容 | 0（金库直接内容为空） | 0 |
| `WealthWatcher` / `ResourceCounter` | 1 次/品类（补丁校正） | 仅规范图补 / `CountAsResource` 另加 |
| `RecipeWorkerCounter` | 慢路径 1 次/品类 + 差额补丁 | 按真身补 |
| 交易 / 编成 / 装载清单 | 0（镜像被过滤） | 展示物各 1 行 |
| `Find.StudyManager` 可研究缓存 | 0（W36） | 0（真身不 spawn） |
| 展示物 | 0（三套基础口径；无 spawn / holder） | 0（仅存在于清单 UI） |

**保真消费桥例外（方案 A）**：`SDS_OfferBridge` 物化出的实物在 offer 生命周期内就是普通 spawned 实物，按 1 次计；超时回库后重新计 0。物化时已从对应通道扣账，因此不是第二份库存。

## 十二、补充口径（仍有效、无独立规则 ID）

> 本节只登记判据与事实，不引入新规则编号；与 §一–§十一 冲突时以本文前文为准。

### 12.1 品类禁用与容量

- 禁用 ≠ 删除：禁用品类仍计入容量 / 财富 / 可交易量，R15 只管读侧归零，账本与物理存量不随之消失。
- 新品类默认可用；禁用粒度 = 品类键（不是 def，也不是单件）。
- `ThingDef.EverHaulable` 只决定存入筛选的默认勾选：尸体 / 缩装默认不勾；基因包 / 异种胚等基因载体默认勾选。不得把它当成「可存判定」。
- 禁用品类不建镜像、不参与搜索枚举；搜索侧需二次过滤，兜住 30 tick 同步窗口。

### 12.2 建镜与同步

- 建镜条件：非保真 + 已解析 + 数量 > 0；不得用「可重建」过滤旧档虚拟条目；未解析不建镜、不参与枚举。
- 生成顺序：先按 `Math.Min(count, stackLimit)` 设镜像堆量再 spawn；spawn 成功后若堆量不等于账本量，立即把镜像 `stackCount` 写回完整数量并置脏（账本不由该路径写入）；读档时 `CollectMirrors` 把超过 `stackLimit` 的镜像夹回上限。数量增量触发同步，结构变化触发全量重建；脏标记为空时 O(1) 返回。
- 同步周期 30 tick；G 关闭撤全部镜像；本图 L 关闭撤该图镜像并转 W37 宽限；地图离开 `Find.Maps` 时清表。
- 读档镜像清理必须在 `SDS_GameComponent_Grid.FinalizeInit`（地图 spawn 之后）执行；不得在 `SDS_VanillaState.ExposeData/PostLoadInit` 提前 `Destroy`，否则会触发 `Spawning destroyed thing` 与零栈报错。
- G / L 判据：G = 世界上存在玩家阵营、已 spawn、未损坏的核心建筑；耗电 > 0 时还要求通电，耗电 = 0 视为可用。L = G 且本图有可用核心或可用芯片 Pawn。
- 可及性与迷雾完全跟随原版，本 Mod 只补「总量差」，不自行推导可达范围。
- `ThingsInGroup(HaulableEver)` 包含镜像，而 `ListerHaulables` 排除镜像——两个独立 registry，不得混用。
- 口袋图（玩家所在及玩家所在地图）同样参与镜像 / 曝光。
- 保真不可见组桥：尸体（复活机械体需要地图尸体）、缩装、Apparel / Medicine / Weapon / Food 有桥；Art / Book 无桥——无桥意味着原版看不到，不得假设可见。
- W53 仓库代理口径：镜像的存储事实由每图 `SDS_MirrorHaulDestination` 唯一提供——`Patch_SDS_MirrorHaulDestination` 让 `CurrentHaulDestinationOf(镜像)` 返回代理（`Accepts` 恒真），从而 `IsInAnyStorage` / `IsInValidStorage` 为真、`CurrentStoragePriorityOf` 取存档级优先级；W52 的「严格更高优先级才流出」据此成立。金库自身的 `StorageSettings` 不参与该判定，不得再写一份「金库筛选 / 优先级」。`HaulDestinationEnabled=false` 保证金库不作为搬运目的地。
- W53 镜像不可存入：镜像代表账本本身，不是可再入库的新货。`SDS_EndpointUtility.IsDepositable` 是唯一判据，端点接收（`IHaulDestination.Accepts`）、存入竞争（`SDS_DepositService`）与手动存入统一拒绝镜像，不得在各入口另写镜像判断；否则流出 Job 会被改写成存入 Job、端点也会被当成流出目的地。
- W55 最高优先级：`SDS_Config.IgnorePriorityMode` 开启时，`Patch_SDS_MirrorNotHaulable` 把「已 spawn、非镜像、未禁止、无在途产出标记、可存入且本图有可用端点」的实物重新放回搬运候选；`SDS_DepositService.AutoOnlyGates` 同时忽略 `IsInValidBestStorage` 拒绝；`SDS_DestinationResolver.TryDecideCore` 的存入决议跳过与原版目的地的优先级比较，直接采用超维端点（随后按 W62 折叠自身）。owner 型容器（棺材 / 料斗 / 运输舱内部）内容物与功能性目的地不在覆盖内；设置关闭时零行为变化。

### 12.3 结构离场与单格自愈

- 结构离场撤离：零账本写入、不产出实物、可重复执行；局部缓冲防重入；窗口内计数回落账本；与单格自愈互不替代。
- W19 的放行分支（不算「外部销毁」）：def 不可销毁、未 spawn、`stackCount <= 0`（后两者先 `Unmark` 再放行）、`AllowDeSpawn`（地图卸载）、非 `Playing`（读档）；其余游玩期外部 `Destroy` 一律拒绝并 `Log.WarningOnce`。
- 单格上限自愈：`EdificeGrid.DeRegister` 不校验归属 → 上限可能掉回 1；重建走 `GenSpawn` 搬迁，`DeSpawn` 会触发保护红字；落子前必须查 `GetMaxItemsAllowedInCell`。

### 12.4 存入 / 派活原子性

- 存入提交原子写 `targetB` / `targetQueueB` / 文案，多件全有或全无；执行期不得重选目的地。
- 多件只投建筑端点；自身端口例外；非自身小人 / 机械体端口只接单件。
- 账本登记的是 Job 精确 `Thing` 引用；失效即删，禁止按 (def, stuff, quality, count) 找回；不碰账本外的背包内容。
- 清理只碰「仍在背包里的账本物品」；挂起不吐货；终止且未成功才落地。
- 默认交付形态 = Hand：镜像目标不在 `StartJob` 物化，`targetA/B/C` 与 `targetQueueA/B` 原样保留，由 W58 / W59 / 取货原语在消费点换真身；只有显式登记为 `SpawnedTarget` 的逃生门 Job 才改写 `targetA/B/C` 与 `job.count` 并打标记（当前仅瓶喂）；失败不得改投。
- 单件 owner 只取 1 件真身；整堆塞入会把「装备一件」变成「装走整堆」。
- 读档时端点 `inputBuffer` 必须清理三类失效残留：与存活保真真身同 `thingID` 的重复副本、`Destroyed`、零栈；清理只在 `SDS_EndpointUtility.PruneLoadedBuffer` 实现，丢弃副本不 Split、不 Destroy、不改数量。
- `FlushInputBufferItem` 遇到 `Destroyed` / 零栈 / 已被其它 owner 持有的条目时，只从缓冲移除，不得再入账。
- 取量口径见《RuleIndex.zh-CN.md》W30 / W58 / W59；唯一实现分别是 `SDS_JobTargetAdapter.TakeCountForSlot`（逃生门）、`SDS_HaulCountIntent`（数量缺失）、`SDS_MirrorConsumeSeam`（消费点物化），本节不复述。
- 翻译判据：同一动作作用于真实容器内容物时，是否会让它离场；`DeSpawn` / 读档 / 卸载豁免。
- `Wear`：先 `DeSpawnOrDeselect`，再校验部位 / biocode / `PawnCanWear`；装备走 `AddEquipment → TryAdd`，属 Container 通道。
- 读档 / 非 Playing 状态才 flush 残留；逐件处理，绝不整批。

### 12.5 使用命令

- 使用门控 0 / 0.5 / 1 / 2 四档：仅自身端口、人形且玩家阵营、六类可用类别、同一回调内同步 spawn → 判定 → 回充。
- JobDef 白 / 黑名单；未知 def 默认接受；未产生 Job 不回充；任务类 Job 取消并回充。

### 12.6 搜索口径（补充 §九）

- 距离排序不判可达；走位 / 交付用「最近可达点」，两者不可互换。
- 取货点 / 可达的唯一规则：自身端口可用且起点就是本人所在格时，自身端口即距离 0 的访问点（含可达）；否则取最近可用建筑访问点。`SDS_AccessPointResolver.TryGetNearestReachableAccess` 是唯一实现，镜像走位、Job 物化、保真桥与可达性判定共用，不得各自另写。
- W54：镜像真身（隐藏金库）永远不是取货点。`Patch_SDS_MirrorTargetGoto` 解析不到端点时按目标不可达结束 Job，不得回落到真身坐标；`SDS_MirrorCarry` 对非芯片小人要求本人位于已解析访问点命中范围内，否则拒绝取货；存入执行器不得按真身坐标走位。镜像不得经 `ThingOwner` 进入小人所属 owner（背包 / 装备 / 衣物）——上手只能走 `SDS_MirrorCarry`；第三方搬运（含 PUAH）的候选 / 派活一律排除镜像，卸货清单里的镜像引用按品类键映射回背包真身，不得让第三方自己选目的地。
- 平局口径：端点 vs 原版目的地取端点；端点之间按 `thingIDNumber` 稳定键（自身端口不再优先）。
- 距离相等取镜像；超过 `maxDistance²` 放弃；比较键顺序按 S12a / S12b。
- 有意绕过 `searchRegionsMax`，理由与「按 Global 语义补候选」同源（Postfix 无法区分阶段）。
- Region / Global 两段过滤顺序：区域候选 → 全局回退 → `CanReach` 最后；源可达 ≠ 目的地可达。
- 候选与取货分层：搜索 / 账单候选按镜像总量（`total`）参与原版选料，实际取货才夹到 `available`。这是有意分层（保留原版选料 / 失败锁窗行为），不得把候选直接改成 `available`。
- 编成的行身份：一行**最多**包含**一个虚拟键 + 一个保真桶**。原版 `TransferAsOne` 按 def / 材质 / 品质 / HP 等分组建行，**不区分通道**，因此同 def / 材质 / 品质的虚拟展示物与保真桶展示物可能落在同一行。`TryScanRowIdentities` 与 `MaterializeForCaravan` 只处理这两个身份；出现多个虚拟键或多个保真桶时必须 fail-closed（记 `Fail`、剥掉虚拟展示物、本行归零），不得静默只取第一个，也不得按 key 聚合多个保真桶（W45 / W47）。
- 装载的行身份：同一行有多个虚拟展示物键时，`TrySelectLoadRow` 取第一个「可用量 > 0」的键；有多个保真桶时逐个尝试，任选一个「本趟能整桶取到真身」的桶，**不 fail-closed、不归零**。桶解析本身仍 fail-closed（命中 0 或 >1 桶返回 null，不得按键聚合）；保真单趟还受展示物可代表单位上限约束。

### 12.7 周期安全网与去抖
- 地图签名与库存提醒按 **30 tick** 检查一次：`CheckMapSignature` + `TickStockNudges`；地图变化最迟 30 tick 内收敛，财富 / 服装提醒本身有 300 / 600 tick 去抖。
- 结构安全网仍为 **250 tick**，但只按 `SDS_BuildingCore` / `SDS_BuildingPort` / `SDS_BuildingCapacityArray` 三个 Def 精确取建筑，不再扫描全部 `BuildingArtificial`。
- 安全网只在 `ReconcileStructures` 返回 `changed` 时调用 `SDS_MirrorService.NotifyStockChanged()` 与 `SDS_AccessPointResolver.Invalidate()`；`Reconcile` 已比较 `Operational`，核心断电 / 恢复会走 `changed` 分支，不需要无条件重评。
- `excludeGridFromWealth = true` 时，库存变化不触发 300 tick 的 `WealthWatcher.ForceRecount`；开关切换本身由 `SDS_SettingsApplier.RefreshWealth` 兜底。
- `separateByQuality` / `separateByStuff` 切换必须调用 `SDS_Core.NotifyCapacitySettingsChanged()`，丢弃账本派生索引；否则容量分组会沿用到下一次库存变化。
- 预留终老由本模块驱动：`SDS_GameComponent_Grid.FinalizeInit` 立刻调一次 `SDS_Core.AuditReservations(ownerAlive)`，此后每 **250 tick** 一次；账本只提供快照与按归属 id 释放，判活事实（`ISDS_ReclaimWorld.JobExists` + `Pawn.Destroyed`）由本模块以委托传入，账本不得自行读 Job / Pawn 存活语义。

## 十三、已知边界与稳定事实

> 本节登记已核实仍适用的旧设计结论，只补 §十二 未覆盖的边界；不引入新规则编号，与 §一–§十二 冲突时以前文为准。

### 13.1 可见性 / 镜像边界

0. 三套访问口径：① `ListerThings`（`ThingsInGroup` / `ThingsOfDef` / `ThingsMatching`，只含 spawn 顶层实体）；② holder 树（`Map.GetChildHolders` → `IThingHolder` → `ThingOwner` → `GetAllThingsRecursively`，含容器内部）；③ `SlotGroup`（`AllGroupsListForReading` / `HeldThings`，`ResourceCounter` 主口径）。另有 `IHaulSource` / `IHaulDestination` 注册表与 `ListerHaulables`，对镜像可见性各不相同。
1. 保真真身不 spawn、不在 holder 树；三套口径均不可见。
2. 端点与金库从 Map 级 holder 摘掉；但直接读端点 / 金库容器的第三方仍可见。
3. 镜像对玩家渲染 / 点选 / 搬运 / 合并 / 伤害 / 拆除不可见，对查询与 AI 可见；设置开启时非玩家仍可被查询看到，但常规取用入口（搜索 / 可达 / Job / 携带 / 容器 / 端点）被 S18 拒绝。
4. 虚拟入库 ⇒ `CanRebuildDef = true`；`CanRebuildDef` 不参与建镜过滤。
5. G 关闭时 `SyncAll` 走 `RemoveAll`，数据保留，恢复后重建。
6. 交易 / 编成 / 装载清单只放展示物，不放镜像。
7. `ListerBuildings` 等仍看到金库建筑，但其市值 / 美观为 0，无 comp、无 `buildingTags`。
8. `GenRadial.RadialDistinctThingsAround` / `ThingGrid.ThingsAt` 只按金库格，无访问点适配。
9. 多殖民地财富只在「规范图」计 `PreservedWealth`，非规范图扣本图镜像。
10. 兼容范围 = 原版实现路径；第三方自造产物 / 直改 `stackCount` 不兼容，不做轮询对账。
11. 投影不是真实容器：`TryAdd` / `GetCountCanAccept` 恒 false / 0；`Remove` / `RemoveAt` 超过 `stackLimit` 返回 false；`Contains` 不保证 false（首次读取前内部列表为空，任何触发 `EnsureFresh` 的读取之后会命中镜像引用）。
12. ProjectE 瞬时转化绕过原版管线：先换真身、保真不参与、区域转化剔除镜像；挂点未生效时 fail-closed。
13. 建镜失败窗口：`MapExposesGrid` 不因落点失败判 L 关闭；窗口内计数 / 可及性按账本读，枚举 / 搜索看不到镜像；`ScheduleRetry` 250 tick 退避全量重试。
14. 投影不落盘：`SDS_ProjectionOwner.ExposeData` 为空，读档按账本重建；`TryAdd` / `GetCountCanAccept` 恒 0 / false；`SDS_MirrorService.Version` 变化使投影快照重建。
15. W52 优先级流出开启且「最高优先级」关闭时，镜像才进入 `ListerHaulables`；是否保留由原版 `IsInValidBestStorage` 判定（严格更高优先级且允许进入），`SDS_MirrorService` 每 30 tick 刷新；关闭或最高优先级开启时刷新移除候选。该判定的「当前优先级」来自仓库代理（W53）；镜像不可存入任何端点（W53），因此端点不能成为流出目的地。
16. 展示物身份：创建时由工厂封存；读档后容器行内未封存的持久化展示物由 `SDS_TransportVirtual.RebindLoadedDisplayKeys` 补封存（唯一保真桶优先，其次虚拟键；无保真候选时按虚拟键兜底，陈旧保真展示物可能被归为虚拟，库存不动）；无法唯一解析时保持未绑定，按非展示物 fail-closed。唯一判据是 `SDS_OutputUtility.IsDisplay`，唯一键解析入口是 `KeyOfDisplay`。

### 13.2 表示形态 × 口径矩阵

| 表示形态 | 枚举 | 搜索 | 计数 | 财富 | 可达 |
|---|---|---|---|---|---|
| 镜像 | 1 次/品类（金库格 / 区域 / 金库组），Map 级 0、由 `spawnedThings` 计 1 | 1 次候选，距离折算访问点 | 1，补丁校正后 = 账本量 | 规范图保留、非规范图扣本图镜像 | 任一访问点可达 |
| 投影 | 直接子树 1，Map 级 0 | 0 | 0 | 0 | — |
| 保真真身 | 0 | 0 | 仅 `CountAsResource` 另加 | 仅规范图补 1 次 | 0 |
| 展示物 | 0 | 0 | 0（只读清单另补） | 0 | 0 |
| 数字增量 | 0 | 0 | 目标量 − 原版已数到的曝光 | 账本虚拟 + 保真 − 本图实际镜像，夹 0 | 不产生候选 |

### 13.3 搜索已知边界（补充 §九 / §十二.6）

- `thingReq` 为 Undefined / Nothing / Everything 时不补候选。
- 不支持的 `thingClass` 回退原版类镜像；强转子类可能失配。
- 取货 / 访问点：带芯片小人 / 机械体自身端口距离 0，优先于建筑端点；可达性判定同口径（起点为本人所在格时，自身端口即距离 0 的访问点）。存入排序不再沿用该特例（见 §12.6 平局口径）。
- 镜像真身坐标（隐藏金库）只用于计数 / 暴露，永远不用于寻路与取货；没有可用端点就按目标不可达结束（W54）。
- 保真品类不进 `GenClosest`，由专用桥接管。
- `RegionwiseBFSWorker` 未单独打补丁，第三方直调只见金库区域。
- 原子加油走区域 BFS，没有访问点兜底（不接管）。
- DoBill 原料按金库格算距离 / 半径；失败后由虚拟 / 保真兜底，并以 `HasReachableEndpointWithin` 遵守半径。
- 顺路合并可能选中镜像，于是改为去端点取货。
- `ThingGrid` / `GenRadial` 不折算访问点距离。
- Postfix 型适配器按 Global 语义与原版 best 二选一；只有 Prefix 型能强制执行区域优先（S9）。
- 仓库代理 `Accepts` 恒真、`HaulDestinationEnabled = false`、优先级跟随全局。
- 端点只在非 `SlotGroup` 容器链路可见。
- 同一镜像可能被 `Region.ListerThings`、全局集合、搜索期投影同时产出，必须按 `Thing` 引用去重，只参与一次比较。

### 13.4 工作类型 × 接入矩阵

> 本表记录「原版工作类型 → 是否可经本 Mod 端点取物」的结论与原因；『判据』列是原版条件，不一定对应本模块代码。实际接入点以《PatchInventory.zh-CN.md》与 [Interfaces.zh-CN.md](Interfaces.zh-CN.md) §4.5 / §4.6 为准。

| 工作类型 | 接入 | 判据或原因 |
|---|---|---|
| 建造材料 | 接 | 不走 `IsInValidStorage` |
| 维修 | 接 | `IsForbidden` + `CanReserve` |
| 装填发酵桶 | 接 | — |
| 生化舱 | 接 | `CanAcceptNutrition` |
| 生长舱 | 接 | 阈值 2.5 / 0 分两段 |
| 次核扫描仪 | 接 | — |
| 基因库装包 | 不接 | 找目的基因库；基因包走保真桥 |
| 囚犯血原包 | 接 | — |
| 造异种胚芽 | 接 | — |
| 染发 / 造型 | 接 | `Global_Reachable` |
| 找药 | 接 | 带 `priorityGetter` |
| 找食物 | 接 | 人类 -1、动物 100 / 畜栏，内部预算 30；S18 开启时非玩家派系不接 |
| 交付囚犯 / 访客 | 接 | — |
| 通用可预留物 | 接 | — |
| 炮塔弹药 | 接 | — |
| 单件加油 | 接 | — |
| 原子加油 | 不接 | `RegionTraverser` 无兜底 |
| 料斗装料 | 接 | 已补 `IsInValidStorage` |
| 染色 | 接 | `CanReach` 补丁生效 |
| 自动穿衣 | 接 | 已补 `IsInAnyStorage` |
| DoBill | 受限 | `ingredientSearchRadius` 内才可见 |
| 顺路合并 | 受限 | 可能去端点取货 |
| 装容器 / 装载 | 接 | 自身不调 `GenClosest`；装载走 `LoadTransportersJobUtility` |

## 十四、通道分部（虚拟 / 保真两向禁引）

> 依据《DesignPrinciples.zh-CN.md》：通道规则已在 Core / Rules 唯一收敛（通道判定 / 账本 / 可用量）；端口 / UI / 存档 / 回收 / 原版挂点按准则一 / 推论三必须共用。可在文件层面固化的只有两个**通道专属目录**之间的直接依赖，其余交织点为结构性共用，禁止拆分（程序集级两拆方案已于 2026-11-16 评审否决）。

| 目录 | 通道归属 | 约束 |
|---|---|---|
| `Mirrors/` | 虚拟专属 | 不得引用 `Preserve/` 声明的顶层类型 |
| `Preserve/` | 保真专属 | 不得引用 `Mirrors/` 声明的顶层类型 |
| `Endpoints/` `Foundation/` `Jobs/` `Query/` `TradeTransport/` `UI/` `Install/` | 共享（唯一合法桥） | 可引用两侧；通道专属成员只允许落在专属目录或本层，禁止把对侧专属类型搬进共享层绕过检查 |

- 通道专属成员的归属以「目录 + 类型」为准：`Preserve/SDS_PreservedBill.cs` 只承载保真兜底与保真取货 JobDriver；虚拟镜像配料兜底唯一实现在 `Jobs/SDS_VirtualBillFallback.cs`（含两条通道共用的原版配料算法反射探针与 `RootCell`，保真侧只调用、不复写）。
- `Patch_SDS_PreservedBillIngredients` 是**双通道挂点**（先虚拟镜像、再保真真身、W51 多槽物化）：允许住在 `Preserve/`，但虚拟腿必须调用共享层 `SDS_VirtualBillFallback`，不得把镜像事实抄进保真文件。
- `SDS_PreservedOffer` 只承载保真选件与保真挂点；双通道供给的 offer 表、落点、腐烂冻结、超时按来源回库与虚拟腿唯一实现在共享层 `Foundation/SDS_OfferBridge.cs`（保真腿只调 `TryGetAccess` / `TrackPlaced` / `RollbackOrWarn` / `InCooldown`，不得自建第二份 offer 表；拆成两份即第二个生命周期，违反准则三）。来源：2026-11-16 方案 B。
- 通道判定唯一在 `SDS.Rules`（`RequiresPreservedStorage` 实例重载 / `key.preserved` 键位）；本节只约束接触面文件归属，不新增第二份通道判据。

检查方式：`CheckAssemblyBoundaries.ps1` §10（提取两目录顶层类型名，逐行剥注释后做两向符号扫描，命中即失败）。来源：2026-11-16 方案评审（程序集级两拆否决，落地方案 A）。
