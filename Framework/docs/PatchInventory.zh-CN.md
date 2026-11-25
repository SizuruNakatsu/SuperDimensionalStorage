# 超维存储 · 补丁清单

> 历史基准：旧框架五轴设计文档（2026-11-12 起为纯历史存档，不再引用）；本表「权威」列已全部指向本框架内文档。
>
> 补丁加载方式：`SDS_Mod.ApplyHarmonyPatches` 逐类 `CreateClassProcessor(type).Patch()` + try/catch；单个坏补丁不会拖垮全局。
> 「节点」= 原版类型.方法；`TargetMethod()` 类按实际解析结果标注。
>
> **列约定（2026-10-28）**
>
> - **层**：`标量读` / `看见/搜索` / `消费` / `派活` / `清单/结算` / `边界`。只描述该补丁服务的契约层，不表示代码归属。
> - **理由**：`契约`（原版契约硬成本）/ `投影`（投影表示代价）/ `规则`（本 Mod 规则代价）/ `兼容`（第三方兼容代价）。
> - **权威**：该补丁设计依据在本框架内的落点（`模块/文档 §节`）；`—` = 无域文档（纯表现 / 工程边界）。
> - **规则**：不变量 ID（R / S / W / E）或 `—`。本清单只引用，不定义规则。
> - **补丁类**：112 个 `[HarmonyPatch]` 特性类 + 29 个注册表条目 Id（§十一，`SDS_MirrorGuardRegistry` / `SDS_ProjectionOwnerBridge`）；后者不是 C# 类。
>
> **校验**：`powershell -NoProfile -ExecutionPolicy Bypass -File .\CheckPatchInventory.ps1`
> 做源码 `[HarmonyPatch(...)]` / `[HarmonyPatch]`+`TargetMethod()` / 两张注册表的 `Id = "Patch_..."` ↔ 本清单双向比对、统计对账、动态补丁登记、元数据合法性、§十一 基线（19 + 10）与 §十二 特例（3）；`-Report` 打印特性类 + 注册表条目与 TargetMethod 文本键去重数，`-Targets` 打印 TargetMethod 描述与方法体。

---

## 一、镜像生命周期与保护（SDS_Mirrors.cs + Patch_Mirrors.cs / SDS_MirrorGuardRegistry.cs）

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_PawnProjectionExposure` | `Pawn.GetChildHolders` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.2 | — | 带芯片小人的子树枚举挂上超维投影 |
| `Patch_SDS_WealthItemsFilterProjection` | `WealthWatcher.WealthItemsFilter` | 标量读 | 投影 | SDS.Vanilla/接口 §4.4 | — | 让 `SDS_MapLevelHolder` / 投影按原 Pawn 口径过滤 |
| `Patch_SDS_MirrorNotHaulable` | `ListerHaulables.ShouldBeHaulable` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十 | W52 / W55 | W52：设置关闭 / 最高优先级开启时镜像不进「可搬运」，流出开启时交原版按严格更高优先级判断；W55：最高优先级开启时，原版判定「已在最优储存」的可存实物经 `IsGridAlwaysBetter` 重新可搬 |
| `Patch_SDS_MirrorNotMergeable` | `ListerMergeables.ShouldBeMergeable` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一；SDS.Vanilla/规则 §十 | W20 | 镜像不进「可合并」列表 |
| `Patch_SDS_MirrorAbsorb` | `Thing.TryAbsorbStack` | 消费 | 投影 | SDS.Vanilla/规则 §十一；SDS.Vanilla/规则 §十 | W20 | 镜像被吸收 → 先取货再合并；镜像吸收别人 → 拒绝（存入不走合并语义） |
| `Patch_SDS_MirrorAbsorbWithComps` | `ThingWithComps.TryAbsorbStack` | 消费 | 投影 | SDS.Vanilla/规则 §十一；SDS.Vanilla/规则 §十 | W20 | 同上（带 comp 子类） |
| `Patch_SDS_MirrorSpawnSetup` | `Thing.SpawnSetup` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十 | — | 镜像 spawn 后登记到镜像索引 |
| `Patch_SDS_MirrorNotStudiable` | `StudyManager.UpdateStudiableCache` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | W36 | 镜像不登记进原版可研究缓存，避免工作分配把隐藏金库当研究对象 |
| `Patch_SDS_MirrorTakeOnSplitOff` | `Thing.SplitOff` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W5 | 镜像被 SplitOff 时从账本取真身（覆盖回退为原版类的镜像） |
| `Patch_SDS_MirrorCarry` | `Pawn_CarryTracker.TryStartCarry(Thing)` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W5 | 单参取镜像 → 真身进手 |
| `Patch_SDS_MirrorCarryCounted` | `Pawn_CarryTracker.TryStartCarry(Thing,int,bool)` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W5 | 带数量取镜像 → 真身进手并返回实际数量 |
| `Patch_SDS_MirrorNoDamage` | `Thing.TakeDamage` | 消费 | 投影 | SDS.Vanilla/规则 §十一 | — | 镜像免伤 |
| `Patch_SDS_VaultPreApplyDamage` | `Thing.PreApplyDamage` | 消费 | 投影 | SDS.Vanilla/规则 §十一 | — | 金库免伤（不拦合法 Destroy） |
| `Patch_SDS_MirrorNoFire` | `FireUtility.TryAttachFire` | 消费 | 投影 | SDS.Vanilla/规则 §十一 | — | 镜像 / 金库不着火 |
| `Patch_SDS_MirrorNoDeSpawn` | `Thing.DeSpawn` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W18 | 未 Unmark 的镜像拒绝 DeSpawn |
| `Patch_SDS_MirrorNoDeSpawnWithComps` | `ThingWithComps.DeSpawn` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W18 | 同上（带 comp 子类） |
| `Patch_SDS_MapDeinitAllowMirrorDespawn` | `MapDeiniter.Deinit` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W18 | 地图卸载期间临时放行镜像 DeSpawn；并在 `Deinit` 前调用 `EvacuateForStructureChange` 撤离本图投影 |
| `Patch_SDS_MirrorDestroyConsume` | `Thing.Destroy` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W19 | 外部销毁镜像 **fail-closed**：拒绝销毁 + 一次性告警；内部清理先 `Unmark`，不走本判定 |
| `Patch_SDS_MirrorDestroyConsumeWithComps` | `ThingWithComps.Destroy` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W19 | 同上（带 comp 子类） |
| `Patch_SDS_MirrorReserveNonExclusiveCanReserve` | `ReservationManager.CanReserve` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W61 | 镜像目标的预留冲突一律忽略；**G 关闭时返回 false**（候选阶段过滤，防 Job 死循环）；非镜像不变 |
| `Patch_SDS_MirrorReserveNonExclusiveCanReserveStack` | `ReservationManager.CanReserveStack` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W61 | 镜像目标可预留量 = 镜像当前堆量；**G 关闭时返回 0** |
| `Patch_SDS_MirrorNoRealtimeDraw` | `Thing.DynamicDrawPhaseAt` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 镜像不实时绘制 |
| `Patch_SDS_MirrorNoMeshPrint` | `Thing.Print` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 镜像不进地图网格绘制 |
| `Patch_SDS_MirrorNoMeshPrintWithComps` | `ThingWithComps.Print` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 同上（带 comp 子类） |
| `Patch_SDS_MirrorNoGuiOverlay` | `Thing.DrawGUIOverlay` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 镜像不画数量 / 品质叠加层 |
| `Patch_SDS_MirrorNoGuiOverlayWithComps` | `ThingWithComps.DrawGUIOverlay` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 同上（带 comp 子类） |
| `Patch_SDS_MirrorNotSelectable` | `ThingSelectionUtility.SelectableByMapClick` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 镜像不可鼠标点选 |
| `Patch_SDS_MirrorNotSelectableDirect` | `Selector.Select` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 第三方直接选中镜像时也拒绝 |
| `Patch_SDS_RoomIgnoresHiddenGrid` | `Room.ContainedAndAdjacentThings`（属性 getter） | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 房间统计忽略镜像 / 金库 |
| `Patch_SDS_RoomContainsThingIgnoresHiddenGrid` | `Room.ContainsThing` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 房间「包含某物」不把镜像 / 金库算作命中 |
| `Patch_SDS_RoomContainedThingsIgnoresHiddenGrid` | `Room.ContainedThings` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 房间内容枚举摘掉镜像 / 金库 |
| `Patch_SDS_RoomThingCountIgnoresHiddenGrid` | `Room.ThingCount` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 房间数量统计摘掉镜像 / 金库 |
| `Patch_SDS_ObserverIgnoresHiddenGrid` | `PawnObserver.PossibleToObserve` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 观察 / 恐怖思绪不把镜像 / 金库当目标 |
| `Patch_SDS_TantrumIgnoresHiddenGrid` | `TantrumMentalStateUtility.CanSmash` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 发脾气不把镜像 / 金库当目标 |
| `Patch_SDS_FireSpreeIgnoresHiddenGrid` | `JobGiver_FireStartingSpree.TryFindRandomIgniteTarget` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十一 | — | 纵火狂不把镜像 / 金库当目标 |

## 二、取货原语与投影 owner（SDS_ProjectionOwnerBridge.cs）

> 目标架构中的「取货原语适配器」：只负责识别镜像、调 `SDS_WithdrawExecutor` 取真身、按原版契约进手 / 进容器。
> 不负责选端点、不负责算数量、不直接扣账。
> 取货原语的镜像侧补丁（`Patch_SDS_MirrorTakeOnSplitOff` / `MirrorCarry` / `MirrorCarryCounted`）见第一节；
> `SDS_Transmission` 的 `Patch_SDS_OwnerTryAdd` / `TryAddOrTransfer` 见第七节。

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_ProjectionTake` | `ThingOwner.Take(Thing,int)` | 消费 | 投影 | SDS.Rules/规则 §一 | W2、W5 | 投影 owner 上的取用转 `SDS_TakeService` |
| `Patch_SDS_ProjectionTryDropCounted` | `ThingOwner.TryDrop(...,int,...)` | 消费 | 投影 | SDS.Rules/规则 §一 | — | 投影 owner 上带数量掉落转账本 |
| `Patch_SDS_ProjectionTryDropAll` | `ThingOwner.TryDrop(...)` | 消费 | 投影 | SDS.Rules/规则 §一 | — | 投影 owner 上整堆掉落转账本 |
| `Patch_SDS_ProjectionTryTransfer` | `ThingOwner.TryTransferToContainer` | 消费 | 投影 | SDS.Rules/规则 §一 | — | 投影 owner 上转移转账本 |
| `Patch_SDS_EndpointTransferReturn` | `ThingOwner.TryTransferToContainer`（5 参） | 消费 | 契约 | SDS.Rules/规则 §一 | — | 端点写穿成功后回填实际接收量，恢复 enroute / `INotifyHauledTo` / `placedThings` 回调 |
| `Patch_SDS_ProjectionClear` | `ThingOwner.Clear` | 消费 | 投影 | SDS.Rules/规则 §一 | — | 投影批量清空只摘引用、不扣账 |
| `Patch_SDS_ProjectionClearAndDestroy` | `ThingOwner.ClearAndDestroyContents` | 消费 | 投影 | SDS.Rules/规则 §一 | — | 同上 |
| `Patch_SDS_ProjectionClearAndDestroyOrPassToWorld` | `ThingOwner.ClearAndDestroyContentsOrPassToWorld` | 消费 | 投影 | SDS.Rules/规则 §一 | — | 同上 |
| `Patch_SDS_ProjectionRemoveAll` | `ThingOwner.RemoveAll(Predicate<Thing>)` | 消费 | 投影 | SDS.Rules/规则 §一 | — | 同上 |
| `Patch_SDS_ProjectionRemoveAllGeneric` | `ThingOwner<Thing>.RemoveAll(Predicate<Thing>)` | 消费 | 投影 | SDS.Rules/规则 §一 | — | 同上（泛型重载） |

## 三、搜索 / 可达 / Job 目标 / 存储判定 / 账单补充

> 目标架构中的「搜索适配器」：只负责镜像可见、距离折算到访问点、可达改写。
> **不物化、不扣账、不改写 Job。**

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_ClosestGlobal` | `GenClosest.ClosestThing_Global` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.6 | S2、S4 | 镜像距离折算到最近访问点，平局取镜像 |
| `Patch_SDS_ClosestGlobalReachable` | `GenClosest.ClosestThing_Global_Reachable` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.6 | S2、S4 | 同上 + 可达性交给 `CanReach` 补丁 |
| `Patch_SDS_ClosestReachable` | `GenClosest.ClosestThingReachable` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.6 | S9、S12a | 出口补镜像候选；Postfix 无法区分 Region / Global，按 Global 语义与原版 best 二选一 |
| `Patch_SDS_ClosestRegionwisePrioritized` | `GenClosest.ClosestThing_Regionwise_ReachablePrioritized` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.6 | S12b | 带优先级时补镜像候选 |
| `Patch_SDS_ReachabilityMirror` | `Reachability.CanReach(IntVec3,LocalTargetInfo,PathEndMode,TraverseParms)` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.6 | S10 | 镜像可达 = 任一访问点可达 |
| `Patch_SDS_StealValueIgnoresHiddenGrid` | `StealAIUtility.GetValue` | 看见/搜索 | 投影 | SDS.Vanilla/规则 §十 | S15 | 镜像 / 金库的偷窃价值读作 0（配套：`HaulableEverOrMinifiable` 不补镜像候选），避免袭击 AI 把投影当可偷物 |
| `Patch_SDS_MirrorTargetGoto` | `Toils_Goto.GotoThing` | 消费 | 投影 | SDS.Vanilla/接口 §4.6 | S10 | 走镜像时把寻路目标改到访问点 |
| `Patch_SDS_JobMirrorTargets` | `Pawn_JobTracker.StartJob` | 消费 | 投影 | SDS.Vanilla/规则 §十 | W5、W30 | 默认 Hand 不物化；只有显式登记为 `SpawnedTarget` 的逃生门 Job（瓶喂）在派活期物化；Postfix 释放本 Job 对镜像的预留 |
| `Patch_SDS_HaulCountIntent` | `Toils_Haul.ErrorCheckForCarry` | 消费 | 规则 | SDS.Vanilla/规则 §十 | W58 | 镜像目标 `job.count ≤ 0` 时按该 Job 的预留意图（`Reservation.StackCount`）补齐；无预留保持 1 |
| `Patch_SDS_WearConsumeSeam` | `JobDriver_Wear.Notify_Starting` | 消费 | 规则 | SDS.Vanilla/规则 §十 | W59 | 穿衣前把镜像目标换成 1 件真身 |
| `Patch_SDS_EquipConsumeSeam` | `JobDriver_Equip.Notify_Starting` | 消费 | 规则 | SDS.Vanilla/规则 §十 | W59 | 装备前把镜像目标换成 1 件真身 |
| `Patch_SDS_UseItemConsumeSeam` | `JobDriver_UseItem.Notify_Starting` | 消费 | 规则 | SDS.Vanilla/规则 §十 | W59 | 使用物品前把镜像目标换成 1 件真身（含研究台子驱动） |
| `Patch_SDS_IngestConsumeSeam` | `JobDriver_Ingest.Notify_Starting` | 消费 | 规则 | SDS.Vanilla/规则 §十 | W59 | 仅非工具使用者：进食前把镜像目标换成 `max(1, job.count)` 件真身 |
| `Patch_SDS_NoWearMirror` | `Pawn_ApparelTracker.Wear` | 消费 | 投影 | SDS.Vanilla/规则 §十一 | W5 | 镜像永远进不了 `wornApparel`（最终闸，保留） |
| `Patch_SDS_ReclaimJobTargets` | `Pawn_JobTracker.CleanupCurrentJob` | 消费 | 规则 | SDS.Rules/规则 §一 | W27 | 过渡：Job 失败 / 打断时回充 Job 目标与手上料，成功时丢弃登记（Core/设计决策 §二 第 13 条）；目标架构并入事务 Rollback |
| `Patch_SDS_MirrorIsInStorage` | `StoreUtility.IsInAnyStorage` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.1 | — | 镜像视为「在存储中」 |
| `Patch_SDS_MirrorIsInValidStorage` | `StoreUtility.IsInValidStorage` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.1 | — | 镜像视为「在有效存储中」（料斗装料） |
| `Patch_SDS_MirrorStoragePriority` | `StoreUtility.CurrentStoragePriorityOf` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.1 | — | 镜像优先级取全局单一来源 |
| `Patch_SDS_MirrorHaulDestination` | `StoreUtility.CurrentHaulDestinationOf` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.1 | — | 镜像 → 每图一个的仓库代理；异常时保留原版目的地 |
| `Patch_SDS_MirrorNotSellable` | `TradeUtility.PlayerSellableNow` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.7 | — | 镜像不可出售 |
| `Patch_SDS_MirrorNotLaunchable` | `TradeUtility.AllLaunchableThingsForTrade` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.7 | — | 镜像不进发射清单 |
| `Patch_SDS_StartCarryAlreadyCarried` | `Toils_Haul.StartCarryThing` | 消费 | 规则 | SDS.Vanilla/规则 §十 | W62 | 目标已在手上时不重复搬运；cell-only 自存折叠在取物后立刻入账，失败改为就地落地 |
| `Patch_SDS_MirrorNotCaravanItem` | `CaravanFormingUtility.AllReachableColonyItems` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.8 | — | 编成 / 装载原始清单摘掉镜像 |
| `Patch_SDS_PreservedBillIngredients` | `WorkGiver_DoBill.TryFindBestBillIngredients` | 派活 | 规则 | SDS.Vanilla/接口 §4.9 | S17 / W51 | 原版失败后先虚拟镜像、再保真真身兜底（单槽）；多槽配方最后把保真缺口物化到工作台交互格并在同一 tick 重跑原版搜索（W51，无新增挂点）；医疗候选先复用原版医药等级判据 |
| `Patch_SDS_MedicalIngredientGate` | `WorkGiver_DoBill.IsUsableIngredient` | 派活 | 规则 | SDS.Vanilla/规则 §十 | S17 | 医疗账单（`Bill_Medical`）药品候选统一过原版医药等级判据；覆盖 `IHaulSource` 递归分支 |
| `Patch_SDS_VirtualFixedIngredients` | `WorkGiver_DoBill.TryFindBestFixedIngredients` | 派活 | 规则 | SDS.Vanilla/规则 §十 | — | 固定配料失败后虚拟兜底 |
| `Patch_SDS_PreservedBillJob` | `WorkGiver_DoBill.TryStartNewDoBillJob` | 派活 | 规则 | SDS.Vanilla/规则 §十 | — | 纯保真原料 Job 换成端点取货 Job |

## 三·六、原版与第三方「只消费落点格」入口折叠 / 退让（W63）

> W63 正文见《SDS.Vanilla/Rules.zh-CN.md》§十。
> W63 先尝试折叠落点（自身端口或建筑端点 `AccessCellOf`，不可站时兜底建筑格，不保证可走；落点不可达则退让）；无法解析落点才按原版「无空余储存空间」退让。
> 覆盖方式 = 运行期枚举 `DefDatabase<WorkGiverDef>` 的全部 `Worker`，对最派生的 `JobOnThing` / `HasJobOnThing` 去重挂 Postfix（实现类 `SDS_CellOnlyWorkGiverPatches`，非 `[HarmonyPatch]` 特性类，不计入本表统计）。原版 `EmptyWasteContainer` / `EmptyEggBox` 的类型特化挂点已删除，统一由运行期枚举覆盖。
> 不 patch `TryFindBestBetterStorageFor` 本身；W63 只改写 Job 落点，不新增第二条扣账路径。

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_CellOnlyOrderedJob` | `Pawn_JobTracker.TryTakeOrderedJob` | 派活 | 契约 | SDS.Vanilla/规则 §十 | W63 | 玩家直接下单的 cell-only Job（含提取圣物）先折叠落点；失败才交原版 |

- 运行期挂点（非特性类，不计入上表）：`SDS_CellOnlyWorkGiverPatches` 在 Def 加载完成后枚举全部 `WorkGiverDef.Worker`，对最派生的 `JobOnThing` / `HasJobOnThing` 去重挂 Postfix，统一调用 `SDS_CellOnlyDestinationFold.TryRewrite` / `SDS_CellOnlyJobShape`；并挂 `Toils_Haul.PlaceHauledThingInCell` 作为唯一落格入账钩子（W63）。启动日志输出 `JobOnThing N / HasJobOnThing M`，失败逐条 `ErrorOnce` 并汇总。
- 圣物入口不再单独置灰；由 `Patch_SDS_CellOnlyOrderedJob` 在 Job 下单时折叠落点，不复制判定。

## 三·五、取货统一主干（目标架构）

> 本节为历史迁移规划存档，不是 Harmony 补丁，不改变 141 补丁点的统计；核心 `SDS_WithdrawResolver` / `SDS_WithdrawTransaction` / `SDS_WithdrawExecutor` / `SDS_WithdrawQuery` 已落地；Job 虚拟取出已收进事务，会话事务仍按 Instant 结算。
> W1–W16 正文见《SDS.Rules/Rules.zh-CN.md》§五 12.18；W21 / W22 / W27 见同一文件 §一 / §二。

| 新节点 | 角色 | 规则 |
|---|---|---|
| `SDS_WithdrawResolver` | 唯一决议 | 端点 / access point、key、qty、preserved、mode；取出只看距离；一个事务一个端点 |
| `SDS_WithdrawTransaction` | 唯一事务 | `Begin` 原子预留多 key；`Execute` 扣账物化；`Deliver` 交付；`Rollback` 回滚；挂起保留、终止释放 |
| `SDS_WithdrawExecutor` | 唯一扣账 / 物化 | 虚拟 / 保真 / 镜像三路；只有这里可以 `TryWithdraw` / 摘真身 |
| `SDS_WithdrawPlan` / `SDS_WithdrawMode` | 计划与模式 | Hand / Container / SpawnedTarget / VirtualHandoff |
| `SDS_WithdrawQuery` | 只读查询 | key / qty / preserved；可用量统一走 `SDS_Availability`；不创建事务 |
| `SDS_WithdrawDeliveryPolicyDef` | 交付策略配置 | Def 表；默认 `Hand`（不物化）；第三方可追加；mode 加载校验，jobDef / driver 首次使用惰性解析 |
| `JobDriver_SDS_WithdrawForJob` | Job 目标适配器（**目标架构，未落地**） | 直接消费 Job 默认 Hand；显式 `SpawnedTarget` 仅作逃生门（当前瓶喂）；按 mode 改写必要目标字段。当前实现仍为 `JobDriver_SDS_WithdrawFromGrid` / `JobDriver_SDS_WithdrawPreservedForBill` |
| `SDS_GameComponent_Grid` 预留池 | 逻辑预留 | `GetAvailableCount = total − reserved`；多 key 原子（先全部校验再统一写入） |
| `SDS_ReclaimRegistry` | 回收唯一 owner（Vanilla，C1） | 回充经 `SDS_Rules.ReturnThing`（失败按 Ground / PawnInventory 退路）；`SDS_ReclaimPolicy`（JobTarget / ProtectedOutput）；`sdsReclaim` 节点、巡检、读档审计都在 Vanilla |
| 读档审计 | 预留终老 + 回收恢复 | `SDS_Core.AuditReservations(ownerAlive)` 由 Vanilla 在 `FinalizeInit` 与 250 tick 调用；回收恢复走 `SDS_ReclaimRegistry.AuditRestored`（原 `SDS_WithdrawSave` 类型已不存在） |
| `SDS_ReturnUtility` | 回充 / 退路唯一实现 | `Return`（回充 → 落地策略）；`ReturnPreserved`（保真回滚）；`ReturnVirtual` / `ReturnVirtualCount`（虚拟账本直写回滚） |
| `SDS_PlacementUtility` / `SDS_PlacementPolicy` | 落地下限唯一实现 | `Exact` / `NearPawn` / `RandomNear`；硬前置 `!Spawned` |

### 迁移状态（当前 → 目标）

| 当前节点 | 目标角色 | 状态 |
|---|---|---|
| `SDS_TakeService` | 执行器薄包装 | 改造 |
| `SDS_Transmission` | 取货原语适配器（Container） | 保留 / 接入执行器 |
| `SDS_JobTargetAdapter` | Job 目标适配器 | 改造 / 由 `WithdrawForJob` 替代 |
| `SDS_JobTargetReclaim` | 已并入 `SDS_ReclaimRegistry`（`ReclaimPolicy.JobTarget`） | 已完成（2026-10-22） |
| `MapComponent_SDS_ProtectedOutputs` | 已并入 `SDS_ReclaimRegistry`（`ReclaimPolicy.ProtectedOutput`） | 已退役（2026-11-16，2.4 窗口外） |
| `SDS_TradeVirtual` / `SDS_CaravanVirtual` / `SDS_TransportVirtual` | 虚拟交付适配器 | 保留注入时序；展示物工厂 / 身份表统一在 Vanilla `SDS_OutputUtility`（`ISDS_DisplayQuery`） |
| `SDS_PreservedUsage` | 保真适配器 | 改造为统一事务 |
| `SDS_ModCompat_CE` 取货部分 | CE 适配器 | 接入执行器 |
| `JobDriver_SDS_WithdrawFromGrid` / `SDS_WithdrawPreservedForBill` | 统一 `WithdrawForJob` 或共享基类 | 合并 |

> 取货主干必须遵守 W1～W14；任何入口不得直接 `grid.TryWithdraw` 或直读 `preservedOwner`。

## 四、计数 / 可及性 / 缺料 / 财富

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_ResourceCounterWithGrid` | `ResourceCounter.UpdateResourceCounts` | 标量读 | 投影 | SDS.Vanilla/接口 §4.3 | — | 资源条注入账本 + 镜像曝光差额校正 |
| `Patch_SDS_ResourceCounterFastRefresh` | `ResourceCounter.ResourceCounterTick` | 标量读 | 投影 | SDS.Vanilla/接口 §4.3 | — | `StockVersion` 变化后 30 tick 节流强制刷新 |
| `Patch_SDS_PotentiallyMissingIngredients` | `RecipeDef.PotentiallyMissingIngredients` | 标量读 | 投影 | SDS.Vanilla/接口 §4.3 | — | 逐槽位复刻材质 / 品质 / 禁用 / 迷雾判定 |
| `Patch_SDS_WealthNormalize` | `WealthWatcher.CalculateWealthItems` | 标量读 | 投影 | SDS.Vanilla/接口 §4.4 | — | 规范图补保真财富、非规范图扣镜像 |
| `Patch_SDS_MapChildHoldersExposure` | `Map.GetChildHolders` | 看见/搜索 | 投影 | SDS.Vanilla/接口 §4.2 | — | Map 级枚举摘端点 / 金库、包装带投影 Pawn |
| `Patch_SDS_HaulGeneralToGrid` | `WorkGiver_Haul.JobOnThing` | 派活 | 契约 | SDS.Vanilla/接口 §4.5 | — | 派活出口：上游函数级出口已改派时只回填 `workGiverDef`（报告语义）；否则替换为本 Mod 多件存入 Job |
| `Patch_SDS_HaulToStorageJob` | `HaulAIUtility.HaulToStorageJob` | 派活 | 契约 | SDS.Vanilla/接口 §4.5 | — | 原版「存储投递 Job」的**唯一函数级出口**：覆盖 `WorkGiver_Haul` / `JobGiver_Haul` / KAU 紧急搬运 / `HaulFromSource` 浮窗 / `Toils_Haul.StoreThingJob` / `JobDriver_Reading` 及第三方直接调用 |
| `Patch_SDS_JobGiverHaul` | `JobGiver_Haul.TryGiveJob` | 派活 | 契约 | SDS.Vanilla/接口 §4.5 | — | 原版返回 null（无 SlotGroup / 无储存区）时的**补派口**：用同一批候选走 `SDS_DepositService.TryMake`；与 `HaulToStorageJob` 分工不同，**不是**已删除节点 |
| `Patch_SDS_CarriedProductToGrid` | `HaulAIUtility.HaulToCellStorageJob` | 派活 | 契约 | SDS.Vanilla/接口 §4.5 | — | 格搬运出口：产物已在手上（`Toils_Recipe`）走 `TryMakeCarried`；**机会搬运**（`Pawn_JobTracker.TryOpportunisticJob` 直调本函数）走普通 `TryMake`。端点胜出 → 替换为 `SDS_DepositToGrid` |
| `Patch_SDS_HaulToContainerJob` | `HaulAIUtility.HaulToContainerJob` | 派活 | 契约 | SDS.Vanilla/接口 §4.5 | — | 容器搬运出口：**机会搬运**直调本函数（`Pawn_JobTracker.cs:739`），完全不经过 `HaulToStorageJob`；端点胜出 → 替换为 `SDS_DepositToGrid`，否则原样保留 |
| `Patch_SDS_Accessible` | `PlayerItemAccessibilityUtility.Accessible` | 标量读 | 投影 | SDS.Vanilla/规则 §十一 | — | 非镜像实物 + 账本 + 保真相加 |
| `Patch_SDS_PossiblyAccessible` | `PlayerItemAccessibilityUtility.PossiblyAccessible` | 标量读 | 投影 | SDS.Vanilla/规则 §十一 | — | 补派生量（× `entry.count`）+ 保真 |
| `Patch_SDS_PlayerCanMake` | `PlayerItemAccessibilityUtility.PlayerCanMake` | 标量读 | 投影 | SDS.Vanilla/规则 §十一 | — | 补拆解 / 收获 / 屠宰 / 熔炼派生 |
| `Patch_SDS_PlayerHasDef` | `PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas(ThingDef,int)` | 标量读 | 投影 | SDS.Vanilla/规则 §十一 | — | 整体重算持有量（防多图重复 / 镜像 1 件） |
| `Patch_SDS_PlayerHasFilter` | `PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas(ThingFilter)` | 标量读 | 投影 | SDS.Vanilla/规则 §十一 | — | 补保真真身匹配 |
| `Patch_SDS_RecipeCounterCountProducts` | `RecipeWorkerCounter.CountProducts` | 标量读 | 投影 | SDS.Vanilla/规则 §十一 | — | 产物计数补 N-1 或全量 |
| `Patch_SDS_AlertNeedWarmClothes` | `Alert_NeedWarmClothes.FreeWarmClothesSetsCount` | 标量读 | 投影 | SDS.Vanilla/规则 §十一 | — | 保暖套数补 N-1 |
| `Patch_SDS_JobReport` | `JobDriver.GetReport` | 边界 | 规则 | — | — | 存入 / 取出 / 端点 Job 的当前工作文本 |
| `Patch_SDS_JobReportHaulToContainer` | `JobDriver_HaulToContainer.GetReport` | 边界 | 规则 | — | — | `HaulToContainer` 覆写了 `GetReport`（不调基类），单独补一次，覆盖核心 / 锚点 / 运输舱 / 传送门搬运 |

## 五、交易 / 编成 / 装载 / 穿梭机

> 目标架构中的「虚拟交付适配器」：展示物只做 UI；打开清单只注入展示物，**不设跨对话框预留**；
> 成交 / 出发 / 装载时开 Instant 事务（`Execute + Deliver`），关闭 / 取消只解除展示物引用。
> 不得直接 `grid.TryWithdraw`。

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_TradeDealReset` | `TradeDeal.Reset` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.7 | R16 | 清残留展示物并注入账本品类 |
| `Patch_SDS_TradeDealValidate` | `TradeDeal.TryExecute` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W23 | 成交前快照校验，不足拦下整笔 |
| `Patch_SDS_TradeableResolveTrade` | `Tradeable.ResolveTrade` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W24 | 卖出交收唯一收口：把展示物替换成真身再交回原版（本地 / 轨道 / 远程 / 赠送同一条，不按商人类型挂补丁） |
| `Patch_SDS_DialogTradeClose` | `Dialog_Trade.Close` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.7 | — | 解除展示物引用（不销毁） |
| `Patch_SDS_BeaconLaunchRead` | `TradeUtility.AllLaunchableThingsForTrade` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.7 | R15 | 信标读出口追加展示物 |
| `Patch_SDS_BeaconLaunchTake` | `TradeUtility.LaunchThingsOfType` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W23、W24 | 信标取出口先从账本扣缺口 |
| `Patch_SDS_SettlementBuyList` | `Settlement_TraderTracker.ColonyThingsWillingToBuy` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.7 | — | 远程交易清单注入 |
| `Patch_SDS_CaravanBuyList` | `Caravan_TraderTracker.ColonyThingsWillingToBuy` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.7 | — | 商队交易清单注入 |
| `Patch_SDS_SettlementGiveBought` | `Settlement_TraderTracker.GiveSoldThingToPlayer` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | — | 买入直接并入账本 |
| `Patch_SDS_CaravanGiveBought` | `Caravan_TraderTracker.GiveSoldThingToPlayer` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | — | 同上 |
| `Patch_SDS_CaravanRecacheReset` | `Dialog_FormCaravan.CalculateAndRecacheTransferables` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.8 | — | 重算清单前清残留展示物 |
| `Patch_SDS_CaravanAddVirtual` | `Dialog_FormCaravan.AddItemsToTransferables` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.8 | — | 编成清单注入 |
| `Patch_SDS_CaravanValidate` | `Dialog_FormCaravan.TryFormAndSendCaravan` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W25 | 出发前数量校验 |
| `Patch_SDS_CaravanBlockInstant` | `Dialog_FormCaravan.DebugTryFormCaravanInstantly` | 清单/结算 | 规则 | SDS.Vanilla/接口 §4.8 | — | 调试瞬间编成不支持展示物 |
| `Patch_SDS_CaravanPostCloseCleanup` | `Dialog_FormCaravan.PostClose` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.8 | — | 关闭时清理展示物 |
| `Patch_SDS_CaravanMaterialize` | `LordMaker.MakeNewLord` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W25 | 编成时把展示物换成真物 |
| `Patch_SDS_ShuttleRequiredItemsFromGrid` | `TransporterUtility.AllSendableItems` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.8 | — | 任务穿梭机必需物品追加展示物（虚拟 + 保真分组桶） |
| `Patch_SDS_TransporterAddVirtual` | `Dialog_LoadTransporters.AddItemsToTransferables` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.8 | — | 运输舱清单注入 |
| `Patch_SDS_TransporterLoadHasJob` | `LoadTransportersJobUtility.HasJobOnTransporter` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W25 | 地图无实物但账本可供时放行 |
| `Patch_SDS_TransporterLoadJob` | `LoadTransportersJobUtility.JobOnTransporter` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W25 | 改派端点取货 Job |
| `Patch_SDS_PortalAddVirtual` | `Dialog_EnterPortal.AddItemsToTransferables` | 清单/结算 | 投影 | SDS.Vanilla/接口 §4.8 | — | 传送门清单注入 |
| `Patch_SDS_PortalLoadHasJob` | `EnterPortalUtility.HasJobOnPortal` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W25 | 同上（放行判定） |
| `Patch_SDS_PortalLoadJob` | `EnterPortalUtility.JobOnPortal` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W25 | 同上（改派取货 Job） |
| `Patch_SDS_TransporterDepositIdentity` | `CompTransporter.SubtractFromToLoadList` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W40 | 入舱前把真身归一到它所属的装载行（运输舱 / 空投舱 / 运输机 / 穿梭机） |
| `Patch_SDS_PortalDepositIdentity` | `MapPortal.SubtractFromToLoadList` | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | W40 | 同上（传送门） |

## 六、保真专用桥（SDS_PreservedUsage.cs）

> 目标架构中的「保真适配器」：按真身引用预留；只调 `SDS_WithdrawExecutor` 的 preserved 模式；不直读 `preservedOwner`。

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_XenogermImplantAvailable` | `Recipe_ImplantXenogerm.AvailableOnNow` | 派活 | 规则 | SDS.Vanilla/规则 §十 | — | 保真异种胚芽让配方可用 |
| `Patch_SDS_SelectXenogermList` | `Dialog_SelectXenogerm` 构造函数 | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | — | 选择窗口追加保真胚芽 |
| `Patch_SDS_XenogermMaterialize` | `Xenogerm.SetTargetPawn` | 消费 | 规则 | SDS.Vanilla/规则 §十 | W2 | 选定目标时物化精确真身 |
| `Patch_SDS_GeneDialogBegin` | `Dialog_CreateXenogerm` 构造函数 | 清单/结算 | 规则 | SDS.Vanilla/规则 §十 | — | 造胚芽窗口把保真基因包注入可选列表 |
| `Patch_SDS_GeneAssemblerGenepacks` | `Building_GeneAssembler.GetGenepacks` | 派活 | 规则 | SDS.Vanilla/规则 §十 | — | 基因装配器追加保真基因包 |
| `Patch_SDS_GrowthVatEmbryoGizmo` | `Building_GrowthVat.GetGizmos` | 边界 | 规则 | SDS.Vanilla/规则 §十 | — | 生长舱增加「植入库存胚胎」按钮 |
| `Patch_SDS_GrowthVatSelectEmbryo` | `Building_GrowthVat.SelectEmbryo` | 消费 | 规则 | SDS.Vanilla/规则 §十 | W2 | 选择时物化真身 |
| `Patch_SDS_CasketAcceptPreservedCorpse` | `Building_Casket.TryAcceptThing` | 消费 | 规则 | SDS.Vanilla/规则 §十 | W2 | 保真尸体直接交给原版容器 |
| `Patch_SDS_PlaceBlueprintForInstall` | `GenConstruct.PlaceBlueprintForInstall` | 消费 | 规则 | SDS.Vanilla/规则 §十 | W2 | 保真缩装物先物化再建安装蓝图；物化后由 `SDS_InstallBlueprintGuard` 取消同一 innerThing 的在册旧蓝图（与 `Designator_Install` 同语义，防重复注册） |

## 六·五、保真消费桥（方案 A，SDS_PreservedOffer.cs）

> 不是搜索适配器：原版决策函数返回「没有」时，物化 1 件保真真身到访问点，再交回原版 Job。
> 规则见《RuleIndex.zh-CN.md》S 系，边界见 [SDS.Vanilla/Interfaces.zh-CN.md](SDS.Vanilla/Interfaces.zh-CN.md) 4.6；扣账/回滚复用 `SDS_WithdrawExecutor` 与 `SDS_ReturnUtility`。
> 2026-11-16：五类挂点仍在 `Preserve/SDS_PreservedOffer.cs`；offer 表、落点、超时回库与虚拟腿搬到共享层 `Foundation/SDS_OfferBridge.cs`（补丁点数量不变）。

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_PreservedMedicineOffer` | `HealthAIUtility.FindBestMedicine` | 消费 | 规则 | SDS.Vanilla/规则 §十 | — | 无药时物化 1 件保真药品并返回（尊重 `medCare` / `onlyUseInventory`） |
| `Patch_SDS_PreservedFoodOffer` | `FoodUtility.TryFindBestFoodSourceFor` | 消费 | 规则 | SDS.Vanilla/规则 §十 | — | 无食物时物化 1 件保真食物并写回 `foodSource` / `foodDef` |
| `Patch_SDS_PreservedWeaponOffer` | `JobGiver_PickUpOpportunisticWeapon.TryGiveJob` | 消费 | 规则 | SDS.Vanilla/规则 §十 | — | 当前无武器时物化 1 件并返回 `Equip` Job；**仅当原版 giver 被 DutyDef（袭击 / 越狱 / 追猎等）调用时生效**，普通殖民者空闲时原版不调用它 |
| `Patch_SDS_PreservedApparelOffer` | `JobGiver_OptimizeApparel.TryGiveJob` | 消费 | 规则 | SDS.Vanilla/规则 §十 | — | 物化 1 件并清零 `nextApparelOptimizeTick`，由原版下一次 think 重新评分 |
| `Patch_SDS_PreservedOfferInStorage` | `StoreUtility.IsInAnyStorage` | 看见/搜索 | 规则 | SDS.Vanilla/规则 §十 | — | 让 offer 对服装扫描表现为「在存储中」（仅限本子系统提供的实物） |

## 七、存入方向与容器入口（SDS_Implants.cs / SDS_Transmission.cs）

> `Patch_SDS_OwnerTryAdd` / `TryAddOrTransfer` 同时是「取货原语适配器」的 Container 模式入口；
> 目标架构中它们只调 `SDS_WithdrawExecutor`，不再自己扣账。

> 已删除三处 Toil 数字化补丁（`StartCarryDigitize` / `HaulToCellDigitize` / `HaulToContainerDigitize`）。
> 数字化只能在“目的地是端点”的本 Mod 存入 Job 内发生；芯片“手持即存”由派活时决议为自身端口实现。

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_FloatMenuDeposit` | `FloatMenuMakerMap.GetOptions` | 派活 | 契约 | SDS.Vanilla/接口 §4.7 | — | 右键「存入超维空间」入口 |
| `Patch_SDS_PawnDepositGizmo` | `Pawn.GetGizmos` | 派活 | 契约 | SDS.Vanilla/接口 §4.7 | — | 小人 gizmo「存入背包」入口 |
| `Patch_SDS_OwnerTryAdd` | `ThingOwner<Thing>.TryAdd(Thing,bool)` | 消费 | 契约 | SDS.Vanilla/接口 §4.9 | — | 容器接收镜像前先换真身；拒收回滚 |
| `Patch_SDS_OwnerTryAddOrTransfer` | `ThingOwner.TryAddOrTransfer(Thing,bool)` | 消费 | 契约 | SDS.Vanilla/接口 §4.9 | — | 同上（转移入口） |

## 八、端点 / 开关 / 事件（SDS_Buildings.cs）

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_EndpointChipAdded` | `Pawn_HealthTracker.AddHediff` | 边界 | 规则 | SDS.Vanilla/接口 §4.1 | — | 芯片增删后端点注册表置脏 |
| `Patch_SDS_EndpointPawnSpawned` | `Pawn.SpawnSetup` | 边界 | 规则 | SDS.Vanilla/接口 §4.1 | — | 玩家 / 芯片小人进图时刷新端点与地图集 |
| `Patch_SDS_PlayerPresencePawnDespawned` | `Pawn.DeSpawn` | 边界 | 规则 | SDS.Vanilla/接口 §4.1 | — | 玩家小人离图时刷新地图集 |
| `Patch_SDS_PlayerPresenceFactionChanged` | `Pawn.SetFaction` | 边界 | 规则 | SDS.Vanilla/接口 §4.1 | — | 阵营变化时刷新地图集 |

## 九、UI（SDS_UI.cs / SDS_Hotkeys.cs）

| 补丁类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `Patch_SDS_PlaySettingsButton` | `PlaySettings.DoPlaySettingsGlobalControls` | 边界 | 规则 | SDS.Vanilla/规则 §十 | — | 右下角「超维」入口按钮（开关右侧侧边栏） |
| `Patch_SDS_Hotkeys` | `WindowStack.HandleEventsHighPriority` | 边界 | 规则 | SDS.Vanilla/规则 §十 | — | 输入路由：设置页按键捕获、C 开关侧边栏、Q 在有选中行时输出、输出对话框鼠标中键确认。让位口径统一由 `SDS_Hotkeys.MustYieldHotkey` 判定：建造模式 / 屏幕上有同键 gizmo（原版把 `Designator_Cancel` 复用到取消种植 / 取消施工 / 清除优先工作）/ `Targeter` 选目标 / 文本输入聚焦 / 吸收输入窗口 / 搜索框 —— 命中任一条都不消费事件 |

## 十、动态补丁（无 `[HarmonyPatch]` 特性）

> 全部第三方 Mod 适配器集中在 `Source/SDS_ModCompat*.cs`（2026-10-27 按职责拆文件，仍同属一个目录）：`SDS_ModCompat.cs` 只做编排与分发；`SDS_CompatServices.cs` 共用基础设施与只读服务；`SDS_CompatReports.cs` 挂点 A/B 报告；每个第三方一个 `SDS_ModCompat_<名字>.cs`；配方缺料槽位匹配查询移到 `SDS_BillIngredientQuery.cs`。闪耀世界毁灭者适配器已于 2026-11-16 移除（数量缺失改由 W58 统一处理）。
> 下表按「所在类」索引，不再对应独立文件；不计入 112 的特性补丁统计。

| 所在类 | 节点 | 层 | 理由 | 权威 | 规则 | 作用 |
|---|---|---|---|---|---|---|
| `SDS_ModCompat_PUAH` | `WorkGiver_HaulToInventory.HasJobOnThing / JobOnThing` | 派活 | 兼容 | 公共API §四 | W17 | 候选闸门；端点胜出时整单替换为本 Mod 多件 Job |
| `SDS_ModCompat_PUAH` | `PawnUnloadChecker.CheckIfPawnShouldUnloadInventory`（Prefix） | 派活 | 兼容 | 公共API §四 | W17 | 卸载派活：能就地存 → 入队本 Mod 存入 Job 并跳过 PUAH 的入队；否则原样交回 PUAH（时机/自愈逻辑不改）。取货驱动内部入队那趟没有派发 API，按方案 B 不接管（不在 `StartJob` 改别人构造的 Job） |
| `SDS_ModCompat_CE` | `WorkGiver_ReloadTurret` / `ReloadAutoLoader` / `JobGiver_*Reload*` / `CompAmmoUser.*` 等 | 消费 | 兼容 | 公共API §四 | W17 | CE 弹药 / 换弹链路过滤镜像；取货部分接入统一执行器与预留池。挂点与其它适配器同走 `SDS_CompatTools.TryPatch`，优先级统一 `Low`（2026-10-27 起；此前为 Harmony 默认 `Normal`，见本节末「优先级约定」） |
| `SDS_ModCompat_UFRecipes` | `AutoSequenceUtil.TryPullIngredientsFromCandidates`（Postfix） | 派活 | 兼容 | 公共API §四 | W17 | 联合轻工家具拓展-UF Light Industries Furniture 全自动打印机：它自己的拉料失败后，**只补保真通道**（尸体 / 缩装 / 基因载体）到打印机容器；虚拟品类交给它自己的原版拉料（`SlotGroup` 里的镜像 + `SplitOff`），不在适配器里再补一套 |
| `SDS_ModCompat_ManipulatorBeam` | `BeamFeatureSettings.IsSpecialContainerTypeEnabled`（Postfix） | 派活 | 兼容 | 公共API §四 | W17 | 牵引光束搬运器Manipulator Beam Emitter 的特殊容器是封闭类型白名单：把本 Mod 端点补成它认识的一种容器类型（只放宽类型判定，不改开关 / 排他占用 / 入队） |
| `SDS_ModCompat_ManipulatorBeam` | `MB_BeamScanCacheMapComponent.get_SpecialContainers`（Postfix） | 派活 | 兼容 | 公共API §四 | W17 | 把本图端点幂等追加进它的容器扫描表（它每 30 tick 重建，追加项随重建失效后自动补回）；否则端点对它完全不可见 |
| `SDS_ModCompat_ManipulatorBeam` | `BeamSpecialTransferUtility.TryFindSpecialContainerTransfer`（Prefix） | 派活 | 兼容 | 公共API §四 | W17 | 用它的 `BeamTransfer` 形状描述「地面物 → 端点」；候选集用原版 `ListerHaulables`，收不收只问端点 `IHaulDestination.Accepts`（本 Mod 的存入硬门），**不再拿全局「找」结果比对目的地**——牵引光束已固定目的地，按全局就近比对会让所有建筑端点在附近有芯片小人时同时判为"没有活"。**投递不接管**：它自己的通用分支（`TryGetInnerInteractableThingOwner` + `ThingOwner.TryAdd`）就是原版容器契约，端点 `InputBuffer` 直接接住。1.2.0 起该方法是 5 参且含 `out BeamTransfer`（本程序集不引用对方 DLL，按 `__args[4]` 写回）；Prefix 跳过原方法时必须同时写 `__result` 与 transfer，签名不符按挂点缺失上报 |
| `SDS_ModCompat_MiliraExpanded` | `CompVirtualProductionDevice.CompTick` / `TryProcessAutomatedRecipeCycle` / `TryProcessManualRecomposeBill` / `TryProcessRecomposeOrder` / `TryProcessDecomposeOrder` / `TryProcessLifeEssenceOrder`（自动补料） | 派活 | 兼容 | 公共API §四 | W17 | 自动生产 / 链接工作台 / 重组 / 分解与生命精华订单缺料：米莉拉扩展-未来科技Milira Expanded - Future Technology 网络自有库存优先，缺口从 SDS 的**虚拟 + 保真两条通道**取出后存入网络（标准物质按 `MEXY_StandardMatterUnit` 补足；未接收余量按来源返还 SDS）；订单表读不到时按「应生效挂点」记缺失，不静默退化 |
| `SDS_ModCompat_CeleTechShuttle` | `CargoTransferFilter.BuildCargoTransferables`（Postfix） | 清单/结算 | 兼容 | 公共API §四 | W50 | 天工模块化穿梭机 CeleTech Shuttle Extension 自建装载清单注入超维展示物；准入 / 冷藏 / 装载 Job / 入舱仍交对方与原版，适配器只做只读投影 |
| `SDS_ModCompat_EquivalentExchange` | `Dialog_LoadTransporters.TryAccept`（`TryPatch`：Prefix `Priority.High` + Finalizer `Low`） | 消费 | 兼容 | 公共API §四 | W17 | ProjectE 等价交换的「装载清单瞬时转化」不是原版装载管线：转化前把展示物代表的数量走统一取货事务换成真身，再交给它销毁；取不出来 / 未真正转化 → 回充账本；保真展示物不参与 |
| `SDS_ModCompat_EquivalentExchange` | `TransmutationUtility.GetTransmutableThingsInArea`（Postfix） | 看见/搜索 | 兼容 | 公共API §四 | W17 | 区域转化是按格 `GetThingList` 扫描，会把金库格的镜像当「地面物品」→ 从候选剔除（容器内容物本来不在格上） |
| `SDS_ModCompat_EquivalentExchange` | `Dialog_LoadTransporters.AddItemsToTransferables` 注入闸门（经 `SDS_CompatGates` 由本体 Postfix 消费） | 清单/结算 | 兼容 | 公共API §四 | W17 | 装了 ProjectE 等价交换但瞬时转化挂点未生效时，它的对话框不注入展示物（fail-closed，避免展示物被直接销毁而账本不动） |
| `SDS_ModCompat_Versatran` | `Window_VersatranCargo.ExecuteInstantLoad`（Prefix / Postfix） | 消费 | 兼容 | 公共API §四 | W46、W54 | VC载具框架车辆货舱装载：先把镜像 / 保真行摘出原版循环，虚拟走 `TryWithdraw`、保真走 `TryWithdrawPreservedExact`；真身交给车辆自己的入舱方法；真实地图物品仍走车辆原逻辑 |
| `SDS_ModCompat_Versatran` | `Window_VersatranCargo.RefreshItems`（Postfix） | 清单/结算 | 兼容 | 公共API §四 | W46 | 只读投影：镜像行 `MaxCount = ReadStock.available`；追加保真真身行（`AppendPreservedThings`），不新增第二套数量 |
| `SDS_ModCompat_Versatran` | `CompVersatranTransporter.CanThingBeStoredInCargo`（Prefix） | 边界 | 兼容 | 公共API §四 | W54 | 守门：镜像 / 存活保真真身不得进入车辆容器；适配器自己的取货产物正常放行 |
| `SDS_ModCompat_MiliraGift` | `MiliraTheOneEntwinedWithYou.Gifts.JobDriver_IdealReceiveGift.TryMakePreToilReservations(bool)`（Prefix） | 消费 | 兼容 | 公共API §四 | W54、W59 | 边缘米莉拉送礼：targetA 为镜像时，先在最近可达端点访问格物化 1 件并复制 `CompIdealGift` 状态，再改写 targetA；无论是否接管都放行原版预约；不改 W54 / Core |

> 动态补丁全部先 `AccessTools.TypeByName` 探测；第三方未安装时静默跳过。
>
> **每个挂点都必须登记 A/B**（`SDS_CompatPatchReport`，与本体 `ApplyHarmonyPatches` 同口径）：
> 正常 → 一条 Message `已生效 已应用/应生效 个挂点`；任一挂点找不到类型 / 方法或应用异常 →
> Warning 逐条列「挂点名（原因）」+ 已生效名单。探测到第三方但结构变了，绝不允许静默漏挂。
>
> **第三方不需要再逐个挂点**：只要它的 WorkGiver 最终调用原版 `HaulAIUtility.HaulToStorageJob`
>（如 KAU 紧急搬运的 `JobOnThingDelegate`），就已被特性补丁 `Patch_SDS_HaulToStorageJob` 在函数级覆盖。
> 只有自带 Job 工厂 / 自带 JobDef 的第三方（Pick Up And Haul 的 `HaulToInventory`、Combat Extended 的换弹链路）才需要动态挂点。
>
> Combat Extended 的取货部分属于「Combat Extended 适配器」，必须接入统一执行器与预留池；Pick Up And Haul 只影响存入派活，不参与取货事务。
>
> 联合轻工家具拓展-UF Light Industries Furniture：全自动打印机的**虚拟品类**由它自己的原版拉料覆盖（`SlotGroup` 里的镜像 + `SplitOff` + `ThingOwner`）；
> 适配器只补它看不到的**保真通道**（尸体 / 缩装 / 基因），挂点在其拉料失败之后，查询函数里不做任何副作用。

**优先级约定（2026-10-27）**

- 第三方挂点的应用只允许走 `SDS_CompatTools.TryPatch`：解析失败 → `Missing`，应用异常 → `Missing` + `ErrorOnce`（`第三方兼容挂点应用失败（已跳过）：挂点名`），成功 → `Applied`。各适配器不得再自写 `harmony.Patch` 骨架。
- 默认优先级 `Priority.Low`：Postfix 最后读取 `__result`（只在原版 / 其它 Mod 未接管时兜底），Prefix 让原 Mod 与原体先决定。Finalizer 用独立的 `finalizerPriority` 参数，回滚 / 清理类一律保持 `Low`，不跟随 Prefix 提高。
- **唯一例外**：ProjectE 等价交换 `Dialog_LoadTransporters.TryAccept` 的 Prefix 必须 `Priority.High` —— 它自己的 Prefix 会接管并跳过原版，低于它的 Prefix 在它返回 `false` 后不会执行；同时保证其它 Mod 的 Prefix 永远看不到假展示物。
- Combat Extended 的 15 个挂点由 Harmony 默认 `Normal` 统一为 `Low`（**L2 顺序变更**）。实机验证清单：炮塔换弹两段（`HasJobOnThing` 放行 + `JobOnThing` 补货）、自动装填器换弹、征兆空匣、攻击中打空、防御职责补弹、拾取弹药、`FindBestAmmo` 镜像过滤，以及 `Pawn_JobTracker.JobTrackerTick` 与其它挂同方法的 Mod 共存。

---

## 十一、注册表化基线（TargetMethod / GuardKind，19 守卫 + 10 投影 owner）

> 本表是步骤 3（注册表化）的**唯一 diff 基线**：不定义规则，只声明「守谁 / 怎么守 / 守哪个入口 / 有无逃生门」。
> **分级**：本节是描述性说明，不新增规则 ID；E6（守卫登记强制化）留待单独 L2 拍板。
> **两个注册表**：`守卫` → `MirrorGuardRegistry`；`投影owner` → `ProjectionOwnerBridge`。不合并成一个 N。
> 主键是 `TargetKey`；`补丁类` 列是注册表条目 Id（`MirrorGuardRegistry` / `ProjectionOwnerBridge`），不是 C# 类。格式：`Type.Method` / `Type.Method[arg,...]` / `Type.Property#getter`。
> 目标 = `mirror` / `vault` / `both`。
> 动作按组隔离：守卫 = `false` / `filter` / `postfix` / `recount`；投影 owner = `translate`；特例类 = `allow` / `suppress`（见 §十二）。
> 权威仍以主表为准；本表只重复「规则」列，便于 diff。`—` 表示无逃生门或无专属规则。
> 验收：`CheckPatchInventory.ps1` 必须报 `特性类 112 + 注册表 29 = 补丁点 141 / 基线 29 行 / 28 个不同 TargetKey / 特例 3 行`；全量 TargetMethod 文本键去重实测 **138**（由 `-Report` 输出；非反射级）。

| 补丁类 | TargetKey | 组 | 目标 | 动作 | 逃生门 / 快路径 | 规则 |
|---|---|---|---|---|---|---|
| `Patch_SDS_MirrorNoDamage` | `Thing.TakeDamage` | 守卫 | both | false | — | — |
| `Patch_SDS_VaultPreApplyDamage` | `Thing.PreApplyDamage` | 守卫 | vault | false | — | — |
| `Patch_SDS_MirrorNoFire` | `FireUtility.TryAttachFire` | 守卫 | both | false | — | — |
| `Patch_SDS_MirrorNoDeSpawn` | `Thing.DeSpawn` | 守卫 | mirror | false | `AllowDeSpawn` / `ProgramState != Playing` | W18 |
| `Patch_SDS_MirrorNoDeSpawnWithComps` | `ThingWithComps.DeSpawn` | 守卫 | mirror | false | `AllowDeSpawn` / `ProgramState != Playing` | W18 |
| `Patch_SDS_MirrorNoRealtimeDraw` | `Thing.DynamicDrawPhaseAt` | 守卫 | mirror | false | `SDS_MirrorRegistry.Any` 快路径 | — |
| `Patch_SDS_MirrorNoMeshPrint` | `Thing.Print` | 守卫 | mirror | false | `Any` 快路径 | — |
| `Patch_SDS_MirrorNoMeshPrintWithComps` | `ThingWithComps.Print` | 守卫 | mirror | false | `Any` 快路径 | — |
| `Patch_SDS_MirrorNoGuiOverlay` | `Thing.DrawGUIOverlay` | 守卫 | mirror | false | `Any` 快路径 | — |
| `Patch_SDS_MirrorNoGuiOverlayWithComps` | `ThingWithComps.DrawGUIOverlay` | 守卫 | mirror | false | `Any` 快路径 | — |
| `Patch_SDS_MirrorNotSelectable` | `ThingSelectionUtility.SelectableByMapClick` | 守卫 | mirror | postfix | `Any` 快路径 / `__result` 已 false | — |
| `Patch_SDS_MirrorNotSelectableDirect` | `Selector.Select` | 守卫 | mirror | false | `Any` 快路径 | — |
| `Patch_SDS_RoomIgnoresHiddenGrid` | `Room.ContainedAndAdjacentThings#getter` | 守卫 | both | filter | — | — |
| `Patch_SDS_RoomContainsThingIgnoresHiddenGrid` | `Room.ContainsThing` | 守卫 | both | postfix | `Any` 快路径 / 全部命中项才改 false | — |
| `Patch_SDS_RoomContainedThingsIgnoresHiddenGrid` | `Room.ContainedThings[ThingDef]` | 守卫 | both | filter | `Any` 快路径 | — |
| `Patch_SDS_RoomThingCountIgnoresHiddenGrid` | `Room.ThingCount` | 守卫 | both | recount | `Any` 快路径 | — |
| `Patch_SDS_ObserverIgnoresHiddenGrid` | `PawnObserver.PossibleToObserve[Thing]` | 守卫 | both | false | — | — |
| `Patch_SDS_TantrumIgnoresHiddenGrid` | `TantrumMentalStateUtility.CanSmash` | 守卫 | both | false | — | — |
| `Patch_SDS_FireSpreeIgnoresHiddenGrid` | `JobGiver_FireStartingSpree.TryFindRandomIgniteTarget` | 守卫 | both | postfix | — | — |
| `Patch_SDS_ProjectionTake` | `ThingOwner.Take[Thing,int]` | 投影owner | mirror | translate | — | W2、W5 |
| `Patch_SDS_ProjectionTryDropCounted` | `ThingOwner.TryDrop[Thing,IntVec3,Map,ThingPlaceMode,int,ref Thing,Action,Predicate]` | 投影owner | mirror | translate | — | W5 |
| `Patch_SDS_ProjectionTryDropAll` | `ThingOwner.TryDrop[Thing,IntVec3,Map,ThingPlaceMode,ref Thing,Action,Predicate,bool]` | 投影owner | mirror | translate | — | W5 |
| `Patch_SDS_ProjectionTryTransfer` | `ThingOwner.TryTransferToContainer[Thing,ThingOwner,int,ref Thing,bool]` | 投影owner | mirror | translate | — | W5 |
| `Patch_SDS_EndpointTransferReturn` | `ThingOwner.TryTransferToContainer[Thing,ThingOwner,int,ref Thing,bool]` | 投影owner | mirror | translate | — | — |
| `Patch_SDS_ProjectionClear` | `ThingOwner.Clear` | 投影owner | mirror | translate | — | — |
| `Patch_SDS_ProjectionClearAndDestroy` | `ThingOwner.ClearAndDestroyContents` | 投影owner | mirror | translate | — | — |
| `Patch_SDS_ProjectionClearAndDestroyOrPassToWorld` | `ThingOwner.ClearAndDestroyContentsOrPassToWorld` | 投影owner | mirror | translate | — | — |
| `Patch_SDS_ProjectionRemoveAll` | `ThingOwner.RemoveAll[Predicate]` | 投影owner | mirror | translate | — | — |
| `Patch_SDS_ProjectionRemoveAllGeneric` | `ThingOwner<Thing>.RemoveAll[Predicate]` | 投影owner | mirror | translate | — | — |

## 十二、特例类（暂不注册表化，3 行）

> 这些补丁有独立语义，**不进入** `MirrorGuardRegistry` / `ProjectionOwnerBridge`；`allow` / `suppress` 是特例动作枚举，注册表化时不迁移。
> 校验：`CheckPatchInventory.ps1` 必须报 `特例 3 行`。

| 补丁类 | TargetKey | 动作 | 原因 | 规则 |
|---|---|---|---|---|
| `Patch_SDS_MapDeinitAllowMirrorDespawn` | `MapDeiniter.Deinit` | allow | 地图卸载窗口需要临时放行 DeSpawn；状态型放行，不是简单谓词 | W18 |
| `Patch_SDS_MirrorNotHaulable` | `ListerHaulables.ShouldBeHaulable` | suppress | 枚举抑制，不是返回空值；与 `NotMergeable` 同类 | — |
| `Patch_SDS_MirrorNotMergeable` | `ListerMergeables.ShouldBeMergeable` | suppress | 枚举抑制，不是返回空值；合并语义见 W20 | W20 |

### 步骤 3 分组（规划，不是基线）

| 组 | 处理 | 范围 |
|---|---|---|
| 守卫 | `MirrorGuardRegistry` | §十一 19 行 |
| 投影 owner | `ProjectionOwnerBridge` | §十一 10 行 |
| **消费原语组**（不叫“成对重载组”） | 步骤 3 单独出清单 | Absorb / Destroy / SplitOff / TryStartCarry / TryAdd；不在本基线 |
| 特例 | 不注册表化 | §十二 3 行 |

> 成对重载（Thing / ThingWithComps）是**结构性属性**，不是并列组：3 对已在守卫内，2 对（Absorb / Destroy）属于消费原语组。

---

## 统计

> 本表以源码实测为准；改动补丁后必须跑 `CheckPatchInventory.ps1` 对账，数字不一致即失败。

| 文件 | 特性补丁类数量 |
|---|---|
| `Patch_Mirrors.cs` | 14 |
| `SDS_AccessPoints.cs` | 0 |
| `SDS_Search.cs` | 6 |
| `SDS_ReturnAndReclaim.cs` | 0 |
| `SDS_JobTargets.cs` | 4 |
| `SDS_MirrorBehavior.cs` | 7 |
| `SDS_StartCarryDepositFold.cs` | 1 |
| `SDS_PreservedBill.cs` | 3 |
| `SDS_VirtualBillFallback.cs` | 1 |
| `Patch_CaravanTransport.cs` | 6 |
| `Patch_TransportLoad.cs` | 9 |
| `Patch_Trade.cs` | 10 |
| `SDS_PreservedUsage.cs` | 9 |
| `SDS_Selection.cs` | 5 |
| `Patch_VanillaSeams.cs` | 11 |
| `SDS_Buildings.cs` | 4 |
| `SDS_Implants.cs` | 2 |
| `SDS_Transmission.cs` | 2 |
| `SDS_UI.cs` | 5 |
| `SDS_Hotkeys.cs` | 1 |
| `SDS_PreservedOffer.cs` | 5 |
| `SDS_HaulCountIntent.cs` | 1 |
| `SDS_MirrorConsumeSeams.cs` | 4 |
| `SDS_MirrorReservations.cs` | 2 |
| **合计（特性补丁）** | **112** |
| 注册表条目（守卫 19 + 投影 owner 10，见 §十一） | 29 |
| **合计（补丁点）** | **141** |
| 动态补丁（Pick Up And Haul / Combat Extended / 联合轻工家具拓展-UF Light Industries Furniture / 牵引光束搬运器Manipulator Beam Emitter / 米莉拉扩展-未来科技Milira Expanded - Future Technology / 天工模块化穿梭机 CeleTech Shuttle Extension / ProjectE 等价交换，均在 `SDS_ModCompat*.cs`：同目录按职责分文件，每个第三方一个） | 7 组（多目标） |

> 公共 API（`SDS_API`）是代码门面，不是 Harmony 补丁，不计入上表；其契约见 [PublicAPI.zh-CN.md](PublicAPI.zh-CN.md)。阶段 1–4（看/读/找/存/取门面 + 适配器迁移）与阶段 5 的牵引光束搬运器Manipulator Beam Emitter / 天工模块化穿梭机 CeleTech Shuttle Extension 薄适配已实现，阶段 6 未开始。

> 以源码实测为准：特性类 **112** + 注册表条目 **29** = 补丁点 **141**（2026-11-16 校验器对账；特性计数覆盖 `[HarmonyPatch(...)]` 与 `[HarmonyPatch]`+`TargetMethod()` 两种形态）。
> 源码实测：TargetMethod 文本键去重 **138**（见 §十一 验收；差额来自多类挂同一方法）。
> `Patch_SDS_JobGiverHaul` **仍在**：它负责原版 `JobGiver_Haul` 返回 null 时的补派，与 `HaulToStorageJob` 分工不同。
> `Patch_SDS_JobReportHaulToContainer` 是第 5 个 UI 侧补丁（`HaulToContainer.GetReport` 覆写，不调基类）。
> `JobDriver_SDS_WithdrawForJob` 是**目标架构名称**，当前未落地；现有驱动为 `JobDriver_SDS_WithdrawFromGrid` / `JobDriver_SDS_WithdrawPreservedForBill`。
> 取货主干的新类（`SDS_WithdrawResolver` / `SDS_WithdrawTransaction` / `SDS_WithdrawExecutor`）不是 Harmony 补丁，不计入 141。

所有特性补丁均通过编译；`SDS_Mod.ApplyHarmonyPatches` 逐类 try/catch，单点失败不影响其余补丁。
