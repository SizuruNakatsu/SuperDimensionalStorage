# SDS.Vanilla · 设计决策（为什么这么做 / 踩过的坑）

> 定位：本文件保存本模块「为什么这么做」的完整原因——历史决策、踩过的坑、旧实现到新实现的差异、设计取舍。只写原因，不复述可检查规则。
> 规则正文（含规则 ID 摘要与镜像可见性结论）见 [Rules.zh-CN.md](Rules.zh-CN.md) §十 / §十一；补丁数量与文件分布见《PatchInventory.zh-CN.md》。

## 一、历史决策与坑

| # | 决策 / 坑 | 现状 |
|---|---|---|
| 1 | 旧文件名是 `Patch_SDS_Core.cs`，但它装的全是原版接缝，不是 Core；新框架改名 `Patch_VanillaSeams.cs`，Core 规则同名类会误导 | 已改名；按 def 找键收编为 Core `SDS_Rules.TryFindWithdrawableKey` 唯一选择规则 |
| 2 | 旧 `SDS_GameComponent_Grid` 直接实现 `IThingHolder` 并暴露保真 owner | 新壳不再实现 `IThingHolder`；保真真身由 `SDS_Ledger` 私有 owner 持有，测试断言 `IsAssignableFrom == false` |
| 3 | 镜像物理层若调 `mirror.SplitOff` / `TryAbsorbStack`，会被 `Patch_SDS_MirrorTakeOnSplitOff` / `Patch_SDS_MirrorAbsorb` 翻译回取货事务并递归 | 物理层只改 `stackCount`、用 `MakeDisplayThing`、整堆取只 `ForgetMirror` + `Unmark` + `DeSpawnOrDeselect` |
| 4 | 裸 `ThingMaker.MakeThing` 造展示物会丢品质 / 材质 / vitals / 展示键 | 统一 `SDS_OutputUtility.MakeDisplayThing` / `MakePreservedDisplayThingFrom` |
| 5 | 旧 `SDS_Mirrors.cs` 同时承担镜像、投影、暴露三件事，单文件过大且易循环引用 | 拆出 `SDS_Projection.cs`、`SDS_Exposure.cs` |
| 6 | 旧落点格退让用 `EmptyWasteContainer` / `EmptyEggBox` 类型特化挂点，覆盖不全；该退让规则已由 W63 折叠 / 退让替代 | 改为运行期枚举全部 `WorkGiverDef.Worker` 的 `JobOnThing` / `HasJobOnThing` 去重挂 Postfix；`IsProbing` 防止探针递归与计划登记；W63 先折叠落点，无法解析才退让 |
| 7 | 旧 `SDS_EndpointResolver` 在 Vanilla；Core 需要端点决议却受禁止项约束 | 决议先移入 Core，P2 再移入 `SDS.Rules`；Vanilla 只保留 `IsLocalUsable` / `HasUsableBEndpointOn` / `CanReach` 等事实 |
| 8 | 29 条注册表条目在特性补丁之后安装，任一失败不能拖垮其余 | 两张注册表各自 try/catch、各自 A/B 报告；`SDS_CompatPatchReport` 记录 Missing 原因 |
| 9 | `SDS_MirrorService.Reset` 内部又调 `SDS_Exposure.Reset` / `SDS_OutputUtility.ResetDisplayCaches`，与 `RegisterAll` 的独立登记重复 | 重复清理幂等，不重复计数；真正要求是「只清引用、不销毁实物」 |
| 10 | `Building_SDS_Core` 若不是 `Building_WorkTable`，`ITab_Bills` 会硬转型失败 | 基类固定 `Building_WorkTable`；`SpawnSetup` 前补 `billStack`，防读档空引用 |
| 11 | `Building_SDS_Vault.DeSpawn` 若先调基类，原版搬迁循环会把镜像 `SplitOff` 当成取货 | 必须在 `base.DeSpawn` 之前 `EvacuateForStructureChange` |
| 12 | `SDS_EndpointBuffer` 若只在 `TryAdd` 挡、不在 `GetCountCanAccept` 挡，JobDriver 预检会通过而投递静默失败 | `TryAdd` 与 `GetCountCanAccept` 共用 `CanAcceptNow` |
| 13 | 旧 `SDS_AccessPoints` 的 public 类型被外部误用 | 新实现改为 internal，只服务本程序集 |
| 14 | 周期性注册表安全网（旧每 250 tick 按 `ListerThings` 重扫三类建筑）曾被遗漏 | 已恢复：`SDS_GameComponent_Grid.SweepRegistries` 每 250 tick 提交活体事实，`SDS_Rules.ReconcileStructures` 统一补齐 / 剔除 / 修正；地图集合变化时立即重扫，静默不打日志 |
| 15 | 首档操作指南信（旧 `MaybeSendIntroNotice`）曾被遗漏，存档字段与语言键残留 | 已恢复：`SDS_VanillaState.TrySendIntroNotice` 每档发一次 `PositiveEvent`，随 `introNoticeShown` 保存；地图未生成时延后到首个非空 tick |
| 16 | 服装优化计时提醒（旧 `apparelNudgePending` + 600 tick 去抖）曾被遗漏 | 已恢复：`SDS_GameComponent_Grid.TickStockNudges` 在账本 `StockVersion` 变化后把玩家小人 `nextApparelOptimizeTick` 拉到当前 tick；只触发原版重评，不决定穿不穿 |
| 17 | 财富重算提醒（旧 `wealthRecountPending` + 300 tick 去抖）曾被遗漏 | 已恢复：`TickStockNudges` 在库存变化后对玩家主地图 `wealthWatcher.ForceRecount()`；只提前重算，不改财富口径 |
| 18 | 金库跟随锚点（旧 `CanonicalEndpointOn` 取 `thingIDNumber` 最小的可用建筑端点） | 已恢复：`SDS_Exposure.TryGetExposureAnchor` 不再按候选顺序取第一个，改为最小 `thingIDNumber`，避免多端点时金库锚点漂移、核心搬家后不跟随 |
| 19 | 保真桶分组调用原版 `TransferAsOne` 时的 GeneSet 空引用 | 已修：`SDS_OutputUtility.HasCompleteTransferIdentity` 在交易 / 编成 / 装载的保真桶构建与展示物创建前剔除 `GeneSet == null` 的基因包 / 异种胚，避免 `CanStackTogether` NRE 和整段注入中断 |

## 二、旧实现 → 新实现的差异与坑

| 旧容器 | 旧节点 / 字段 | 现归属 | 处理 |
|---|---|---|---|
| `SDS_GameComponent_Grid` | `dataVersion`、`stockEntries`、`forbiddenKeys`、`reservations`、`preservedThings` | Core `SDS_Ledger` | 节点名不变，由 Core 读写；Vanilla 不碰 |
| `SDS_GameComponent_Grid` | `sdsReclaim`（Deep） | 本模块 `SDS_ReclaimRegistry` | C8 起读写都只在 `SDS_GameComponent_Grid`；节点与本账本同级，旧档同名节点走同一条读路径，Core 不读不写 |
| `SDS_GameComponent_Grid` | `depositFilter`（Deep）、`coreStoragePriority`（Value）、`introNoticeShown`（Value）、`sidebarUI`（Deep） | `SDS_VanillaState` | 节点名与 LookMode 不变；筛选回调读档后重建 |
| `SDS_GameComponent_Grid` | `sdsMirrors`（Reference） | `SDS_VanillaState.mirrorScratch` | 旧镜像只作投影，读档后销毁并按账本重建；跳过 null |
| `SDS_GameComponent_Grid` | `transportLoadRequests`（Deep） | `SDS_VanillaState.transportLoadScratch` | `SDS_TransportLoadSave` 字段：`containerThingID` / `rowIndex` / `defName` / `stuffName` / `qualityInt` / `preserved` / `units` |
| `Building_SDS_Core` / `Building_SDS_Port` | `inputBuffer`（Deep，owner=this） | 同名建筑 | 读档空则重建 `SDS_EndpointBuffer` |
| ~~`MapComponent_SDS_ProtectedOutputs`~~ | ~~`sdsProtectedOutputs`（Deep）~~ | **已退役（2026-11-16）** | 只服务 2.4（2026-10-26 21:54）之前的档；已按 [CompatibilityRetirement.zh-CN.md](CompatibilityRetirement.zh-CN.md) 删除，旧节点不再读取 |
| `SDS_StockKey` | 旧 `thingDef` / `stuff` / `qualityInt` / `preserved` | Contracts | 旧字段名**只读不写**（2026-11-16 起停写），新字段 `defName` / `stuffName` 兜底 |
| ~~`SDS_ProtectedOutput`~~ | ~~`thing`（Reference）、`lordLoadID`、`createdTick`~~ | **已退役（2026-11-16）** | 旧档迁移 DTO；随 `sdsProtectedOutputs` 一起删除，回收账本只认 `SDS_ReclaimSave` |

## 三、设计取舍

> 原《实现原因.md》全文并入本节：原 §一 → 3.1、原 §二 → 3.2、原 §三 → 3.3（条目编号 1–6 保留，[Rules.zh-CN.md](Rules.zh-CN.md) V5「依据」列写的「《实现原因.md》三.1」即本节第 1 条）；原 §一.4（旧文件落点表，与 §一重复）、原 §四（维护约束）、原 §六（验收关注点）与 [Rules.zh-CN.md](Rules.zh-CN.md)、[Interfaces.zh-CN.md](Interfaces.zh-CN.md) 重复，已删。

### 3.1 为什么原版适配必须单独成程序集

- 补丁归属由《ModuleRules.zh-CN.md》七固定：特性补丁类全部在 Vanilla，29 个注册表条目（19 镜像守卫 + 10 投影 owner）也在 Vanilla；9 个第三方适配器的补丁是 `SDS.ThirdModCompat` 内的动态 `harmony.Patch`，不计入。
- `SDS_PatchInstaller` 只扫描 `SDS.Vanilla` 程序集，逐类 `CreateClassProcessor(type).Patch()`，不扫其它程序集；其它层在规则上被禁止承担这件事：Contracts 禁 Harmony（6.1）、Core 禁 Harmony 与全部原版行为入口（6.3）、Api 禁 Harmony（6.5）、Bootstrap 只负责安装与启动顺序（6.7、九），ThirdModCompat 的 Harmony 只针对具体第三方目标。
- 安装顺序也固定了这一点：启动第 3 步先装 `SDS_PatchInstaller`（特性类 + 两张注册表），第 4 步才扫描第三方适配器；即使没有任何第三方 Mod，原版接缝也必须完整生效（《ModuleRules.zh-CN.md》九）。
- 逐类 try/catch，失败只记 `Warn` 并继续；启动报告输出「A/B = 已应用 / 声明」与失败名单，A/B 口径可直接对上 `CheckPatchInventory.ps1`（`SDS_PatchInstaller.cs`；《ModuleRules.zh-CN.md》十一）。补丁数、注册表数与补丁类名因此可以当验收项。
- 事实与规则分离，Core 才能脱离游戏启动流程测试：Core 禁调 `ListerThings`、`GenClosest`、`ResourceCounter`、`JobDriver`、`SlotGroup`、`Reachability`、`IHaulDestination`、`IThingHolder`、`Find.*`、`mapPawns`、`GenPlace`、`GenSpawn` 等原版行为面（《ModuleRules.zh-CN.md》6.3），这些事实只能经 `SDS_Host` 注入；11 个槽位中只有 Ports / DepositFilter / Delivery 仍由 Core 直接消费，Priority / MirrorPhysical / MedicalCare 改由调用方以查询字段 / 计划字段 / 委托注入，MirrorQuery 归 Api 适配器桥，ReclaimWorld 随 C1 只在本模块内部使用，Display / DepositJobs / TransportLoad 只服务 Api 适配器桥（《ModuleResponsibilities.zh-CN.md》三、五、六）。宿主实现 public 无参构造、每次调用现读，`SDS_Host.Install` 可以早于 `SDS_GameComponent_Grid`，测试侧也能用假实现填满槽位。「脱离」只指运行期不启动游戏、不建 `Map`、不进 Mod 启动顺序；编译仍以 RimWorld / Verse 数据类型为引用（《ModuleRules.zh-CN.md》四）。
- 第三方兼容不污染本层：Vanilla 只引用 Contracts / Diag / Core / Harmony，ThirdModCompat 只引用 Contracts / Diag / Api / Harmony，两者互不引用（《ModuleRules.zh-CN.md》2.3、四），Vanilla 不出现任何目标 Mod 的 packageId、类型名或适配器分支；新增或删除一个第三方适配器不需要改 Vanilla，也不改变原版接缝补丁的 A/B 口径。

### 3.2 特性补丁与注册表为什么必须留在这一层

- 数量与归属见 [Rules.zh-CN.md](Rules.zh-CN.md) V4 与《PatchInventory.zh-CN.md》统计节，本文不复述数字。
- 两张注册表不是 C# 特性类，而是由 `SDS_MirrorGuardRegistry` / `SDS_ProjectionOwnerBridge` 在 Vanilla 安装阶段用 `TargetMethod()` 动态挂载的入口；它们同样依赖原版类型与方法，只能与特性补丁一起留在 Vanilla（`SDS_PatchInstaller.cs`；《ModuleResponsibilities.zh-CN.md》六）。
- 补丁与注册表只提供「原版事实与执行」：识别镜像、改写可见性、折算距离、转发派活、在端点缓冲上写穿；不定义扣账、预留、容量、排序或回滚规则。
- 每个文件各有多少补丁类见《PatchInventory.zh-CN.md》统计节；本层不重复《ModuleResponsibilities.zh-CN.md》六的原版行为入口表。
- 验收关注点：补丁 A/B、19 个镜像守卫 + 10 个投影 owner、静态清理 16 项、存档节点名与 `LookMode`（改名属 L3）——检查方式见 [Rules.zh-CN.md](Rules.zh-CN.md) V4 / V8 / V10 与 §六。

### 3.3 六条关键取舍

1. **原版接缝以 Postfix + 显式低优先级为主。** 资源条、财富、Map 级 holder、搬运派活、落点格守卫等入口统一标 `[HarmonyPriority(Priority.Low)]`，让其它 Mod 先看到原版结果，再决定是否补镜像、补派活或退让（`Patch_VanillaSeams.cs`）。唯一例外是 `ResourceCounterTick` 的 Prefix：它只按节流触发一次 `UpdateResourceCounts()`，不改原版返回值（同文件 `Patch_SDS_ResourceCounterFastRefresh`）。
2. **尽量只在原版结果缺失或不优时接管。** `JobGiver_Haul` 只在原版 `__result == null` 时补派；`HaulToStorageJob` 仅在端点决议胜出时替换，否则保留原版（含原版 null）；`WorkGiver_DoBill` 的镜像兜底只在原版找不到原料后执行；保真消费桥只在原版决策返回「没有」时物化 1 件（`PatchInventory.zh-CN.md` 四、六·五）。不是「所有入口一律改写」，而是把原版行为保留为基线。
3. **镜像物理操作禁止 `SplitOff` / `TryAbsorbStack`。** 原因与检查见 §一.3 与 [Rules.zh-CN.md](Rules.zh-CN.md) §四；部分取只改 `stackCount` 并用 `SDS_OutputUtility.MakeDisplayThing` 产物，整堆取只做 `ForgetMirror` / `Unmark` / 恢复 `CompRottable.disabled` / `DeSpawnOrDeselect`（《ModuleInterfaces.zh-CN.md》4.3；`SDS_Mirrors.cs`）。
4. **`SDS_GameComponent_Grid` 不再实现 `IThingHolder`。** 保真真身由 `SDS_Ledger` 私有 owner 持有；grid 只负责创建账本、调 `ExposeData` / `Tick`、设置 `Instance` 并 `SDS_Core.Attach`。`Verse.Map.GetChildHolders` 遍历的是 `map.components`，`GameComponent` 不在该集合中，因此移除 `IThingHolder` 对 Map 级枚举无影响；若发现其它调用方，回退到空 owner / 只读视图，不暴露可变 owner（《ModuleInterfaces.zh-CN.md》4.1；`SDS_GameComponent_Grid.cs`）。
5. **宿主实现不缓存跨档状态。** `SDS_VanillaServices` / `SDS_EndpointProvider` / `SDS_DisplayAdapter` 等都是进程级实例，每次调用现读 `Instance?.State`，构造时不读 `Instance` / `State` / `Find` / `Def`；这样注入顺序（启动第 2 步）与存档壳构造（第 7 步）可以解耦（《ModuleInterfaces.zh-CN.md》4.2；`SDS_VanillaServices.cs`、`SDS_EndpointProvider.cs`、`SDS_CompatHosts.cs`）。
6. **存档壳只序列化内容，不兜底重建账本。** `ExposeData` 调用 `Ledger.ExposeData()`、`State.ExposeData()` 与组件级 `sdsReclaim`（回收账本）；静默重建会丢档且不重挂 `SDS_Core`（《ModuleRules.zh-CN.md》五；《ModuleInterfaces.zh-CN.md》4.1）。

> 本章是现行设计取舍；维护本模块无需再读任何历史存档。

## 四、补充决策与坑

> 以下十五条是复核旧设计后确认「仍然有效、但前三节未覆盖原因」的取舍；只写为什么，不复述规则正文。

| # | 决策 / 坑 | 现状 / 理由 |
|---|---|---|
| 1 | 建镜口径只认「非保真 + 已解析 + 数量 > 0」；不能用「可重建」判断去过滤旧档虚拟条目，否则升级后账本里保留的条目会从原版搜索、取用、财富里整体消失 | `SyncAll` 跳过 `key.preserved`、`!key.Resolved`、`count <= 0`。未解析条目不建镜，也不进 `ListerThings` / `ThingGrid` / `Region` / `GenClosest`，只留在存储窗口的未解析区；账本条目保留，def 回来后下一轮同步自动恢复。生成时先把镜像堆量设为 `Math.Min(count, stackLimit)` 再 spawn，成功后才把镜像 `stackCount` 写回完整数量——原版 `Thing.SpawnSetup` 会截断超限堆，先写真实值会被静默截断；账本本身不由建镜路径写入 |
| 2 | 结构离场（金库 `DeSpawn` / 地图卸载）不是取货入口，但必须先撤投影：原版搬迁循环会对格内超上限堆调 `SplitOff`，镜像的 `SplitOff` 会被翻译成取货扣账 | 两个入口都在 `base.DeSpawn` / `Deinit` 之前调 `EvacuateForStructureChange`。撤离只摘标记并 `Destroy(Vanish)` 镜像，零账本写入、不产出实物、不改数量；用局部收集列表而非静态表（`Destroy` 可能触发事件重入），空表时是空操作，可重复执行。撤离到下一轮重建之间，读侧（可用量 / 财富 / 交易 / 资源条）回落到账本，数量不会因为窗口内没有镜像而下降 |
| 3 | 单格自愈与结构离场不是一回事：`EdificeGrid.DeRegister` 不校验「解登记的是不是自己」，任何同格建筑离场都会清掉金库槽，金库还在但 `GetMaxItemsAllowedInCell` 掉回 1 | `RepairEdificeRegistration` 在本格没有其它建筑时重新 `Register` 金库，不重建建筑。金库搬迁只能走 `DeSpawn` + `GenSpawn.Spawn` 的完整路径；不能靠直接 `DeSpawn` 清格——游玩期镜像的 `DeSpawn` 被守卫拦截（只给撤离 / 地图卸载放行），原版 `GenSpawn` 挪动同格物品时会与残留投影的 DeSpawn 保护冲突，报 `already spawned` 红字。镜像落子前先查 `GetMaxItemsAllowedInCell <= GetItemCount`，不满足就不 spawn 并 `ScheduleRetry()` |
| 4 | 全局存储优先级是存档级，不是 Mod 跨档设置；`-1` 是「本存档从未设置」哨兵，不是某个实际优先级 | 唯一读写口是 `SDS_VanillaState.CoreStoragePriority` / `SetCoreStoragePriority`：读侧见到 `-1` 才回退 Mod 设置，UI 不直接写 `SDS_Config`。放进存档的原因：核心界面改优先级发生在游戏内，不会触发 Mod 设置的 `WriteSettings`，只写内存会在重启后丢成「普通」。读档 `FinalizeInit` 时已生效（端点 `SpawnSetup` 同步 `StorageSettings.Priority`），250 tick 全量扫描只做兜底校正 |
| 5 | 交易 / 编成 / 装载清单不建立跨对话框预留，只在成交 / 出发 / 装车时刻结算 | 预留会计入 `SDS_Availability.GetAvailableCount`（Total − Reserved）；清单若提前扣预留，还有货的行会被算成 0 而置灰，玩家看到「有货却点不了」。打开清单只读快照，结算时开一次瞬时事务；另一笔 Job 预留仍会挡住可用量，这是有意保守 |
| 6 | `Pawn_JobTracker.CleanupCurrentJob` 是 private，只能按私有名挂 Harmony；且 `releaseReservations && !carryThingAfterJob` 时原版会把 `carryTracker` 的物品丢到脚下 | 「挂起 / 结束不吐货」只对 `inventory` 成立（存入 Job 走背包）。`Patch_SDS_ReclaimJobTargets` 的 Prefix 在原版 cleanup 掉落之前先 `SDS_ReclaimRegistry.ReclaimHand(pawn, loadID)` 回收手上的料；`SDS_DepositJobCleanup` 只看 `pawn.inventory.innerContainer`，管不到 `carryTracker`，手上物品必须单独处理 |
| 7 | 保真消费桥的时序常量不是随手值：6000 / 2500 tick 是 offer 的超时上限，600 tick 是同一 Pawn + 同一 def 的提供冷却 | 6000 tick 约 1.7 游戏小时，够从访问点走到并完成一次取用；食物用 2500 是因为吃饭通常就近。600 tick 防止高频 think 反复物化 / 回收。落点被并入地图已有同类堆时实例已销毁、数量守恒，不再跟踪 / 打标 / 解禁；候选品类只要已有任何预留（`GetReserved > 0`）就整类跳过，因为预留是 key 级，无法保证拿到的不是别人预定的那件 |
| 8 | 旧版（2.5.2）端点输入缓冲入账成功后没有把物品移出 `inputBuffer`，导致同一真身同时写进 `preservedThings`（Deep）与 `inputBuffer`（Deep），读档时 Scribe 报 `Id already used` | 写侧已改为成功入账后 `RemoveIfContained`；读侧再加 `PruneLoadedBuffer`，按 `thingID` 剔除重复副本。**不能只靠 `holdingOwner` 判断**：旧档里的缓冲副本与保真真身是两个对象、ID 相同，只有 ID 级判定能识别 |
| 9 | 读档镜像清理不能放在 `SDS_VanillaState.ExposeData/PostLoadInit` | 该时机早于地图 spawn；先 `Destroy` 会让地图加载阶段再按存档 spawn 一次，产生 `Spawning destroyed thing / 0 stackCount`。旧版在 `SDS_GameComponent_Grid.FinalizeInit` 清理，地图已就绪，所以无此问题 |
| 10 | 不恢复旧的同 tick 派活决议缓存（`SDS_HaulJobDecisionCache`） | 新架构的搬运派发只在 `JobGiver_Haul.TryGiveJob` 与 `HaulAIUtility.HaulToStorageJob` 两个单点调用 `SDS_DepositService.TryMake`，不存在旧版 `HasJobOnThing ↔ JobOnThing` 成对重复决议；恢复旧缓存会变成第二份跨调用状态，反而增加不一致风险 |
| 11 | 地图签名 / 库存提醒改为 30 tick，安全网只在 changed 时通知镜像 | 地图集合和库存版本都不需要逐 tick 感知；30 tick 的延迟远小于财富 300 / 服装 600 的去抖。`Reconcile` 已比较 `Operational`，250 tick 无条件重评镜像属于空转；按 3 个 SDS Def 取建筑则把全基地扫描降为 3 个短列表 |
| 12 | 通道分部用「文件归属 + 两向禁引」落地，不用程序集两拆（2026-11-16 评审否决拆 `SDS.Vanilla` 为虚拟 / 保真两程序集） | 端口建筑 / 超维空间 UI / 存档壳 / 回收队列 / 原版统一挂点按准则一、推论三必须共用——程序集两拆会制造账本双写路径与同一原版方法的双补丁，且通道按「件」在运行期判定，无法按物品先天分组（判定唯一在 `SDS.Rules.RequiresPreservedStorage`，抄两份违反准则三）。可固化的只有 `Mirrors/` 与 `Preserve/` 的直接依赖：`CheckAssemblyBoundaries.ps1` §10 两向禁引（Rules.zh-CN.md §十四）。为此把虚拟镜像配料兜底从 `SDS_PreservedBill.cs` 物理搬到共享层 `Jobs/SDS_VirtualBillFallback.cs`（含两条通道共用的原版配料反射探针 `findInSet` / `findInSetNoMix` 与 `RootCell`，保真侧只调用不复写）；双通道挂点 `Patch_SDS_PreservedBillIngredients` 留在 `Preserve/`，虚拟腿只调共享层。成员体逐字搬移，公开面 / 补丁点 / 行为零变化（`SDS.Tests` 全绿）。同日按方案 B 把 `SDS_PreservedOffer` 的 offer 表、落点、腐烂冻结、超时回库与虚拟腿物理搬到 `Foundation/SDS_OfferBridge.cs`，`Preserve/` 只留保真选件与五类挂点；仍是一份 offer 生命周期，不新增第二套缓存 |
| 13 | W47 的读档重建不新增存档节点，也不把运行期计划落盘 | 预留账本只存「唯一键 + 合计件数」，容器行由 W48 恢复；驱动器在物化前按预留键从容器内**唯一**分组桶用 `TakePlan` 重建计划，多桶同键 / 找不到桶 / 不足额一律 fail-closed。理由：运行期计划不跨存档（准则三），账本预留是唯一权威；把计划或实例 ID 落盘会形成第二份权威，且修改 `SDS_Reservation` / `SDS_TransportLoadSave` 属 L3 存档结构变更，收益不抵风险。`FinalizeInit` 立即尝试一次 W48 行恢复，缩短驱动器等待窗口 |
| 14 | 展示物身份只在工厂封存；读档补封存，不在热路径保留结构兜底 | `displayKeys` 不落盘。`FinalizeInit` 在 W48 恢复后调用 `SDS_TransportVirtual.RebindLoadedDisplayKeys`：对容器行内未 spawn、无 holder 的持久化展示物，优先按唯一保真桶解析，其次按虚拟键补封存；同键多桶无法唯一解析时保持未绑定，按非展示物 fail-closed。理由：结构兜底会把任意未 spawn / 无 holder 的 Thing 当展示物，违反「身份只在封存时确定」；库存不动，故不回退到猜测 |
| 15 | 桶身份 / 键解析收口，登记表按流程分离 | `SDS_PreservedBuckets.SameBucket` 是 W45 的唯一判定；`SDS_OutputUtility.KeyOfThing` 是实例 → 品类键的唯一 helper；`SDS_DisplayBucketRegistry` 提供唯一注册 / 查询 / 移除 / 清理实现，交易、车运与装载各持一个实例。理由：判定与键解析必须唯一；登记表的生命周期和跨容器隔离不同，合并成全局单例会让一个流程的刷新误清另一个流程的登记 |
| 16 | 镜像 Job 目标不能靠第三方适配兜底：VEF PipeSystem 的 `BringToProcessor` 在 Job 级 endCondition / finalizer 里裸读 `Ingredient.def`，镜像读档失效后会无限抛 NRE | 修自己的通用接缝，而不是加适配器：W60 键失效时先结束当前 Job / 排队出队 / 移除队列槽，再丢弃目标；运行期镜像移除前走同一收口；读档后立即同步 + 立即重映射。理由：镜像等价真实仓库，真实物品不会在 Job 期间凭空变 null；这是所有「先带走目标、后使用目标引用」驱动的共同前提。按《SDS.ThirdModCompat/Rules.zh-CN.md》§五.4 不为单个 Mod 新增适配器，且适配器无法覆盖「镜像在存档前被移除」这条路径；取货后原版 `Toils_Haul.StartCarryThing` 本就把 Job 槽改成真身，不需要再改取货原语。边界：旧档中已经变成 null 的槽无法在第三方 finalizer 前无副作用修复；本修复只保证新存档不再产生该状态，已坏档需手动取消一次该 Job。实现收口：槽位枚举 / 队列增删 / 结束-出队动作 / 镜像有效性各只有一份（`SDS_MirrorJobTargets`、`SDS_MirrorService.IsLiveOn`）；读档条目改写/收口前先校验槽仍指向旧镜像（已 `Destroyed`）或已失效为 null，Job 已推进（原版 `StartCarryThing` 已把槽换成手上真身）时跳过，避免覆盖真实目标或误结束 Job；存档条目未记旧 `thingID`，`Destroyed` 是近似判据（精确化需改存档结构，属 L3，不做）；无效镜像替换已改为“先建新镜像 → 精确引用改指 → 再移除旧镜像”，成功路径不再取消 Job；新建 / 落点 / 换新失败仍回退关闭旧目标并移除；改指时释放旧镜像预留（新目标按 W61 非排他接管），Job 遍历统一走 `ForEachJobOnMap`；金库搬迁仍走“先结束”，按 key 重建后重映射列为后续 P1 行为优化 |
