using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 无地图调用方（远程交易买入 / 商队）的即时存入端口：只作为 Core.TryDeposit 的执行句柄，
    /// 不参与端点选择、不提供交付；可用性完全由 Core 的全局 G 判定。
    /// </summary>
    internal sealed class SDS_DirectDepositPort : ISDS_Port
    {
        public Thing Thing
        {
            get { return null; }
        }

        public Pawn Pawn
        {
            get { return null; }
        }

        public Map Map
        {
            get { return null; }
        }

        public bool IsSelf
        {
            get { return true; }
        }

        public bool Operational
        {
            get { return SDS_Rules.Available; }
        }

        public IntVec3 AccessCell
        {
            get { return IntVec3.Invalid; }
        }

        public PathEndMode AccessMode
        {
            get { return PathEndMode.Touch; }
        }

        public bool CanReach(Pawn actor)
        {
            return true;
        }

        public float RankDistanceSquared(IntVec3 reference)
        {
            return 0f;
        }
    }

    /// <summary>目的地的类别。只有端点与“仓储类”目的地允许参与竞争。</summary>
    internal enum SDS_DestinationKind
    {
        None,
        Endpoint,
        WarehouseSlotGroup,
        Functional,
        Unknown
    }

    /// <summary>
    /// 目的地分类：正向白名单。只有端点、ISlotGroupParent 仓储组能参与竞争；
    /// 功能性目的地与未知第三方容器一律放行给原版。
    /// </summary>
    internal static class SDS_DestinationClassifier
    {
        private static readonly HashSet<string> functionalDefNames = new HashSet<string>
        {
            "GeneBank",
            "SDS_HiddenVault"
        };

        public static SDS_DestinationKind ClassifyThing(Thing thing)
        {
            if (thing == null)
            {
                return SDS_DestinationKind.None;
            }
            if (thing is ISDS_StorageEndpoint)
            {
                return SDS_DestinationKind.Endpoint;
            }
            if (IsFunctionalContainer(thing))
            {
                return SDS_DestinationKind.Functional;
            }
            if (thing is ISlotGroupParent || thing is Building_Storage)
            {
                return SDS_DestinationKind.WarehouseSlotGroup;
            }
            if (thing is IHaulDestination && thing.def != null)
            {
                Log.WarningOnce("[SDS] 未接管未知目的地类型：" + thing.def.defName
                    + "（不参与目的地竞争）", thing.def.shortHash ^ 0x5D5D);
            }
            return SDS_DestinationKind.Unknown;
        }

        public static SDS_DestinationKind ClassifyCell(Map map, IntVec3 cell)
        {
            if (map == null || !cell.IsValid || !cell.InBounds(map))
            {
                return SDS_DestinationKind.None;
            }
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (IsFunctionalContainer(things[i]))
                {
                    return SDS_DestinationKind.Functional;
                }
            }
            if (map.haulDestinationManager != null && map.haulDestinationManager.SlotGroupAt(cell) != null)
            {
                return SDS_DestinationKind.WarehouseSlotGroup;
            }
            return SDS_DestinationKind.Unknown;
        }

        /// <summary>功能性交付：原版用它完成某项工作，不是“把物品存进仓库”。</summary>
        public static bool IsFunctionalContainer(Thing container)
        {
            if (container == null)
            {
                return false;
            }
            if (container.def != null && functionalDefNames.Contains(container.def.defName))
            {
                return true;
            }
            if (container is IConstructible || container is IBillGiver
                || container is Building_Enterable || container is Building_Casket
                || container is Building_NutrientPasteDispenser || container is Building_FermentingBarrel
                || container is Building_GeneAssembler)
            {
                return true;
            }
            if (container.def != null && container.def.defName == "Hopper")
            {
                return true;
            }
            return container.TryGetComp<CompTransporter>() != null
                || container.TryGetComp<CompRefuelable>() != null;
        }
    }

    /// <summary>
    /// 统一目的地决议器：所有派活出口只调这里。
    /// 排序键：优先级降序 → 有效距离升序 → 平局取端点。
    /// </summary>
    internal static class SDS_DestinationResolver
    {
        public static bool TryDecideDeposit(Pawn pawn, Thing thing, Job vanillaJob,
            bool allowProducedMarked, out ISDS_Port endpoint)
        {
            endpoint = null;
            if (pawn == null || thing == null || pawn.Map == null)
            {
                return false;
            }
            if (vanillaJob != null && vanillaJob.def == SDS_JobDefOf.DepositToGrid)
            {
                return false;
            }

            Thing vanillaThing;
            IntVec3 vanillaCell;
            StoragePriority vanillaPriority;
            if (!TryParseVanillaDestination(pawn, vanillaJob, out vanillaThing, out vanillaCell,
                    out vanillaPriority))
            {
                return false;
            }
            return TryDecideCore(pawn, thing, vanillaThing, vanillaCell, vanillaPriority,
                allowProducedMarked, out endpoint);
        }

        /// <summary>用“原版最佳目的地”作为基线做决议（多件候选逐件使用）。</summary>
        public static bool TryDecideAgainstVanillaBest(Pawn pawn, Thing thing,
            bool allowProducedMarked, out ISDS_Port endpoint)
        {
            endpoint = null;
            if (pawn == null || thing == null || pawn.Map == null)
            {
                return false;
            }
            Thing destThing = null;
            IntVec3 destCell = IntVec3.Invalid;
            StoragePriority priority = StoragePriority.Unstored;
            StoragePriority current = StoreUtility.CurrentStoragePriorityOf(thing, false);
            if (StoreUtility.TryFindBestBetterStorageFor(thing, pawn, pawn.Map, current, pawn.Faction,
                    out destCell, out IHaulDestination destination, false))
            {
                destThing = destination as Thing;
                if (destThing == null && !destCell.IsValid)
                {
                    return false;
                }
                if (destination is IStoreSettingsParent settingsParent)
                {
                    priority = settingsParent.GetStoreSettings().Priority;
                }
            }
            return TryDecideCore(pawn, thing, destThing, destCell, priority,
                allowProducedMarked, out endpoint);
        }

        private static bool TryDecideCore(Pawn pawn, Thing thing, Thing vanillaThing, IntVec3 vanillaCell,
            StoragePriority vanillaPriority, bool allowProducedMarked, out ISDS_Port endpoint)
        {
            endpoint = null;

            SDS_DestinationKind vanillaKind = vanillaThing != null
                ? SDS_DestinationClassifier.ClassifyThing(vanillaThing)
                : SDS_DestinationClassifier.ClassifyCell(pawn.Map, vanillaCell);
            if (vanillaKind == SDS_DestinationKind.Functional
                || vanillaKind == SDS_DestinationKind.Unknown)
            {
                return false;
            }

            SDS_EndpointQuery query = SDS_EndpointQuery.Deposit(thing, pawn, allowProducedMarked);
            SDS_EndpointDecision decision = SDS_Rules.SelectEndpoint(query);
            if (!decision.allowed || decision.port == null)
            {
                return false;
            }
            endpoint = decision.port;

            // 没有原版目的地：端点就是唯一候选。
            if (vanillaThing == null && !vanillaCell.IsValid)
            {
                FoldToSelf(pawn, thing, allowProducedMarked, ref endpoint);
                return true;
            }

            ISDS_PrioritySource prioritySource = SDS_Host.Priority;
            bool ignorePriority = prioritySource != null && prioritySource.IgnorePriority;
            if (ignorePriority)
            {
                FoldToSelf(pawn, thing, allowProducedMarked, ref endpoint);
                return true;
            }

            int endpointPriority = prioritySource != null ? prioritySource.Priority : 0;
            int otherPriority = (int)vanillaPriority;

            IntVec3 reference = thing.SpawnedOrAnyParentSpawned ? thing.PositionHeld : pawn.PositionHeld;
            IntVec3 vanillaStand = vanillaCell.IsValid
                ? vanillaCell
                : SDS_EndpointHandle.AccessCellOf(vanillaThing);
            float endpointDistance = endpoint.RankDistanceSquared(reference);
            float vanillaDistance = SDS_EndpointHandle.DistanceSquared(reference, vanillaStand);
            int endpointId = endpoint.Thing != null ? endpoint.Thing.thingIDNumber : int.MaxValue;
            int vanillaId = vanillaThing != null ? vanillaThing.thingIDNumber : int.MaxValue;

            bool endpointWins = SDS_SearchRanker.PreferDestination(
                endpointPriority, endpointDistance, endpointId, true,
                otherPriority, vanillaDistance, vanillaId, false, true);
            if (endpointWins)
            {
                FoldToSelf(pawn, thing, allowProducedMarked, ref endpoint);
            }
            return endpointWins;
        }

        /// <summary>
        /// W62：端点已经胜出后，若执行者自身是可用端口且可接收，则折叠为自身端口。
        /// 不改变优先级判定，只替换同一账本下的交付端点。
        /// </summary>
        private static void FoldToSelf(Pawn pawn, Thing thing, bool allowProducedMarked,
            ref ISDS_Port endpoint)
        {
            if (endpoint == null || endpoint.IsSelf)
            {
                return;
            }
            if (SDS_SelfDepositFold.TryResolve(pawn, thing, allowProducedMarked, out ISDS_Port self))
            {
                endpoint = self;
            }
        }

        private static bool TryParseVanillaDestination(Pawn pawn, Job job, out Thing vanillaThing,
            out IntVec3 vanillaCell, out StoragePriority priority)
        {
            vanillaThing = null;
            vanillaCell = IntVec3.Invalid;
            priority = StoragePriority.Unstored;
            if (job == null)
            {
                return true;
            }

            if (job.def == JobDefOf.HaulToCell)
            {
                if (job.haulMode != HaulMode.ToCellStorage)
                {
                    return false;
                }
                vanillaCell = job.targetB.Cell;
                if (!vanillaCell.IsValid)
                {
                    return true;
                }
                SlotGroup group = pawn.Map.haulDestinationManager != null
                    ? pawn.Map.haulDestinationManager.SlotGroupAt(vanillaCell)
                    : null;
                if (group != null)
                {
                    priority = group.Settings.Priority;
                }
                return true;
            }

            if (job.def == JobDefOf.HaulToContainer)
            {
                vanillaThing = job.targetB.Thing;
                if (vanillaThing == null)
                {
                    return true;
                }
                if (vanillaThing is IStoreSettingsParent settingsParent)
                {
                    priority = settingsParent.GetStoreSettings().Priority;
                }
                return true;
            }

            // PUAH 多件搬运：targetB 可能是容器或格子。
            if (job.def.defName == "HaulToInventory")
            {
                vanillaThing = job.targetB.Thing;
                if (vanillaThing != null)
                {
                    if (vanillaThing is IStoreSettingsParent settingsParent)
                    {
                        priority = settingsParent.GetStoreSettings().Priority;
                    }
                    return true;
                }
                vanillaCell = job.targetB.Cell;
                if (!vanillaCell.IsValid)
                {
                    return true;
                }
                SlotGroup group = pawn.Map.haulDestinationManager != null
                    ? pawn.Map.haulDestinationManager.SlotGroupAt(vanillaCell)
                    : null;
                if (group != null)
                {
                    priority = group.Settings.Priority;
                }
                return true;
            }

            return false;
        }
    }

    /// <summary>目的地提交：写 Job 目标是唯一权威。</summary>
    internal static class SDS_DestinationCommit
    {
        public static void Apply(Job job, Thing endpointThing, Thing firstItem)
        {
            if (job == null || endpointThing == null)
            {
                return;
            }
            job.targetB = new LocalTargetInfo(endpointThing);
            string label = firstItem != null && !firstItem.Destroyed ? firstItem.LabelShort : string.Empty;
            job.reportStringOverride = "SDS.JobReport.Deposit".Translate(label).ToString();
        }
    }

    /// <summary>手动存入的唯一失败原因（右键 / 存入背包共用；只用于回执，不参与判定）。</summary>
    internal enum SDS_ManualDepositFailure
    {
        None = 0,
        NoPort,
        NoMatch,
    }

    /// <summary>
    /// 存入统一服务：所有派活出口共用。只做“决议 + 提交”，不做入账 / 预留副作用。
    /// </summary>
    internal static class SDS_DepositService
    {
        private const float CandidateRadius = 12f;
        private const int MaxItems = 8;

        /// <summary>
        /// 手动存入的唯一接收判定。右键 / 存入背包 / 第三方玩家 UI 共用；
        /// 手动 = forced，允许通过在途物化标记闸（AcceptsForManual）。
        /// </summary>
        // RULE:W57
        internal static bool TryResolveManualDeposit(Pawn pawn, Thing thing, out ISDS_Port port,
            out SDS_ManualDepositFailure failure)
        {
            port = null;
            failure = SDS_ManualDepositFailure.NoPort;
            // W62：手动入口复用同一 self-port 解析与 AcceptsForManual 事实；
            // 失败只在“端口存在但不可收”时归为 NoMatch。
            if (!SDS_SelfDepositFold.TryResolveManual(pawn, thing, out port, out bool hasPort))
            {
                if (hasPort)
                {
                    failure = SDS_ManualDepositFailure.NoMatch;
                }
                return false;
            }
            failure = SDS_ManualDepositFailure.None;
            return true;
        }

        /// <summary>唯一决议：原版基线 / 端点 / 功能性白名单都在这里收口。null = 交回原版。</summary>
        public static Job Decide(Pawn pawn, Thing firstThing, bool forced, Job vanillaJob, bool multi)
        {
            if (!CommonGates(pawn, firstThing, forced))
            {
                return null;
            }
            if (!SDS_DestinationResolver.TryDecideDeposit(pawn, firstThing, vanillaJob,
                    forced, out ISDS_Port endpoint))
            {
                return null;
            }
            return endpoint != null ? BuildJob(pawn, firstThing, forced, endpoint, multi) : null;
        }

        /// <summary>
        /// 手动入口（右键「存入超维空间」/ 小人自身端口）：
        /// 与「存入背包」共用同一手动决议；只提交 targetB，不参与原版目的地竞争。
        /// </summary>
        public static Job TryMakeManual(Pawn pawn, Thing thing)
        {
            if (!TryResolveManualDeposit(pawn, thing, out ISDS_Port endpoint, out _))
            {
                return null;
            }
            return BuildJob(pawn, thing, true, endpoint, multi: false);
        }

        /// <summary>即时手动存入（gizmo / 背包）：与右键共用同一手动决议，走同一执行核心，不创建 Job。</summary>
        public static int DepositNow(Pawn pawn, IEnumerable<Thing> items, out SDS_ManualDepositFailure failure)
        {
            failure = SDS_ManualDepositFailure.NoPort;
            if (pawn == null || items == null)
            {
                failure = SDS_ManualDepositFailure.NoMatch;
                return 0;
            }
            if (!SDS_SelfDepositFold.TryGetSelfPort(pawn, out _))
            {
                return 0;
            }
            failure = SDS_ManualDepositFailure.NoMatch;
            List<Thing> snapshot = new List<Thing>(items);
            int deposited = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                // 与右键完全同一决议（W57），这里只负责“立刻执行”。
                if (!TryResolveManualDeposit(pawn, snapshot[i], out ISDS_Port endpoint, out _))
                {
                    continue;
                }
                if (SDS_Rules.TryDeposit(snapshot[i], endpoint, out int accepted) && accepted > 0)
                {
                    deposited += accepted;
                }
            }
            if (deposited > 0)
            {
                failure = SDS_ManualDepositFailure.None;
            }
            return deposited;
        }

        /// <summary>
        /// W55：最高优先级模式下，超维空间被视为所有可存实物的最优储存。
        /// 只回答“原版是否应认为这件东西还需要搬运”，不产生 Job、不扣账；设置关闭时恒为 false。
        /// 已知边界：owner 型容器（棺材 / 料斗 / 运输舱内部）内容物不 spawn，不在覆盖内。
        /// </summary>
        public static bool IsGridAlwaysBetter(Thing thing)
        {
            if (!SDS_Config.IgnorePriorityMode)
            {
                return false;
            }
            if (thing == null || thing.Destroyed || !thing.Spawned)
            {
                return false;
            }
            if (SDS_MirrorRegistry.IsMirror(thing))
            {
                return false;
            }
            // 与 ListerHaulables.ShouldBeHaulable 的「玩家禁止」口径一致（Faction.OfPlayer 的禁止标记）。
            CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();
            if (forbiddable != null && forbiddable.Forbidden)
            {
                return false;
            }
            if (SDS_Rules.IsProducedMarked(thing))
            {
                return false;
            }
            if (!SDS_Rules.Available)
            {
                return false;
            }
            if (!SDS_Rules.CanDeposit(thing))
            {
                // W55 透明化：命中“品类已满”时给一次节流提示，避免玩家把端点旁的实物误当成已入库。
                SDS_DepositDispatchLedger.NotifyCapacityFullIfNeeded(thing);
                return false;
            }
            return SDS_Exposure.MapExposesGrid(thing.Map);
        }

        /// <summary>候选闸门：只有当“端点确实赢过原版最佳目的地”时才放行。</summary>
        public static bool CanTakeCandidate(Pawn pawn, Thing thing, bool forced, out ISDS_Port endpoint)
        {
            endpoint = null;
            if (!CommonGates(pawn, thing, forced))
            {
                return false;
            }
            return SDS_DestinationResolver.TryDecideAgainstVanillaBest(pawn, thing, forced, out endpoint);
        }

        /// <summary>只做“能不能搬”的硬门，不做目的地决议。</summary>
        public static bool CanCarryCandidate(Pawn pawn, Thing thing, bool forced)
        {
            return CommonGates(pawn, thing, forced);
        }

        public static Job TryMake(Pawn pawn, Thing firstThing, bool forced, Job vanillaJob, bool multi)
        {
            return Decide(pawn, firstThing, forced, vanillaJob, multi);
        }

        /// <summary>「已经拿在手上」的产物改派（账单产出直接送仓）。</summary>
        public static Job TryMakeCarried(Pawn pawn, Thing carriedThing, Job vanillaJob)
        {
            if (pawn == null || carriedThing == null || pawn.Map == null)
            {
                return null;
            }
            if (pawn.Faction != Faction.OfPlayer || pawn.carryTracker == null
                || !ReferenceEquals(pawn.carryTracker.CarriedThing, carriedThing))
            {
                return null;
            }
            if (!SDS_DestinationResolver.TryDecideDeposit(pawn, carriedThing, vanillaJob, false,
                    out ISDS_Port endpoint) || endpoint == null)
            {
                return null;
            }
            return BuildJob(pawn, carriedThing, false, endpoint, multi: false);
        }

        /// <summary>「已经在背包里」的货改派（第三方卸载派活出口）。</summary>
        public static Job TryMakeFromInventory(Pawn pawn, IList<Thing> items)
        {
            if (pawn == null || items == null || items.Count == 0)
            {
                return null;
            }
            if (pawn.Map == null || !pawn.Spawned || pawn.inventory == null
                || pawn.inventory.innerContainer == null)
            {
                return null;
            }
            if (pawn.Faction != Faction.OfPlayer || !SDS_ImplantUtility.IsPortOperational(pawn))
            {
                return null;
            }
            Thing first = null;
            ISDS_Port endpoint = null;
            for (int i = 0; i < items.Count; i++)
            {
                Thing item = items[i];
                if (!IsInventoryDepositable(pawn, item))
                {
                    continue;
                }
                if (!SDS_DestinationResolver.TryDecideAgainstVanillaBest(pawn, item, false, out endpoint)
                    || endpoint == null)
                {
                    return null;
                }
                first = item;
                break;
            }
            if (first == null || endpoint == null)
            {
                return null;
            }
            Job job = BuildJob(pawn, first, false, endpoint, multi: false);
            if (job == null)
            {
                return null;
            }
            List<Thing> candidates = new List<Thing>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                Thing item = items[i];
                if (item != null && !ReferenceEquals(item, first) && IsInventoryDepositable(pawn, item))
                {
                    candidates.Add(item);
                }
            }
            CollectCompatible(pawn, endpoint, false, job, candidates,
                delegate (Thing _) { return CandidateVerdict.Take; });
            return job;
        }

        private static bool IsInventoryDepositable(Pawn pawn, Thing item)
        {
            return item != null && !item.Destroyed
                && ReferenceEquals(item.holdingOwner, pawn.inventory.innerContainer)
                && SDS_ChipManualDeposit.CanDepositThing(item);
        }

        private static Job BuildJob(Pawn pawn, Thing firstThing, bool forced,
            ISDS_Port endpoint, bool multi)
        {
            if (multi && endpoint.Pawn != null && !endpoint.IsSelf)
            {
                return null;
            }
            JobDef def = SDS_JobDefOf.DepositToGrid;
            if (def == null)
            {
                SDS_Diag.Fail("存入/缺JobDef", SDS_Diag.Ctx(pawn.Map, pawn, firstThing));
                return null;
            }

            Job job = JobMaker.MakeJob(def, firstThing);
            job.count = Math.Max(1, firstThing.stackCount);
            job.playerForced = forced;
            job.targetQueueA = new List<LocalTargetInfo>();
            job.countQueue = new List<int>();
            job.targetQueueA.Add(new LocalTargetInfo(firstThing));
            job.countQueue.Add(Math.Max(1, firstThing.stackCount));
            if (!QueueFitsCapacity(job))
            {
                return null;
            }
            SDS_DestinationCommit.Apply(job, endpoint.Thing, firstThing);

            if (multi)
            {
                CollectCompatibleItems(pawn, firstThing, endpoint, forced, job);
            }
            return job;
        }

        private enum CandidateVerdict
        {
            Skip,
            Stop,
            Take
        }

        private static void CollectCompatible(Pawn pawn, ISDS_Port committed, bool forced, Job job,
            IList<Thing> candidates, Func<Thing, CandidateVerdict> admit)
        {
            if (candidates == null || admit == null || job == null || job.targetQueueA == null)
            {
                return;
            }
            for (int i = 0; i < candidates.Count && job.targetQueueA.Count < MaxItems; i++)
            {
                Thing candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }
                CandidateVerdict verdict = admit(candidate);
                if (verdict == CandidateVerdict.Stop)
                {
                    break;
                }
                if (verdict != CandidateVerdict.Take)
                {
                    continue;
                }
                if (!SDS_DestinationResolver.TryDecideAgainstVanillaBest(pawn, candidate, forced,
                        out ISDS_Port candidateEndpoint)
                    || candidateEndpoint == null
                    || candidateEndpoint.Thing != committed.Thing)
                {
                    continue;
                }
                job.targetQueueA.Add(new LocalTargetInfo(candidate));
                job.countQueue.Add(Math.Max(1, candidate.stackCount));
                if (!QueueFitsCapacity(job))
                {
                    job.targetQueueA.RemoveAt(job.targetQueueA.Count - 1);
                    job.countQueue.RemoveAt(job.countQueue.Count - 1);
                }
            }
        }

        private static void CollectCompatibleItems(Pawn pawn, Thing firstThing, ISDS_Port committed,
            bool forced, Job job)
        {
            List<Thing> all = new List<Thing>(pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling());
            IntVec3 origin = firstThing.PositionHeld;
            all.RemoveAll(t => t == null || t == firstThing || t.Destroyed || !t.Spawned
                || t.Map != pawn.Map);
            all.Sort((a, b) =>
            {
                float da = (a.PositionHeld - origin).LengthHorizontalSquared;
                float db = (b.PositionHeld - origin).LengthHorizontalSquared;
                return da.CompareTo(db);
            });

            float radiusSquared = CandidateRadius * CandidateRadius;
            CollectCompatible(pawn, committed, forced, job, all, delegate (Thing candidate)
            {
                if ((candidate.PositionHeld - origin).LengthHorizontalSquared > radiusSquared)
                {
                    return CandidateVerdict.Stop;
                }
                if (!CommonGates(pawn, candidate, forced))
                {
                    return CandidateVerdict.Skip;
                }
                if (SDS_Rules.IsProducedMarked(candidate) && !forced)
                {
                    return CandidateVerdict.Skip;
                }
                if (StoreUtility.IsInValidBestStorage(candidate))
                {
                    return CandidateVerdict.Skip;
                }
                if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, candidate, candidate.stackCount))
                {
                    return CandidateVerdict.Stop;
                }
                return CandidateVerdict.Take;
            });
        }

        /// <summary>
        /// 集合级容量预检：逐件复用 Core 的唯一存入判定（容量 / 筛选 / 可存）。
        /// 执行期仍会逐件复检。
        /// </summary>
        private static bool QueueFitsCapacity(Job job)
        {
            if (job == null || job.targetQueueA == null || job.targetQueueA.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < job.targetQueueA.Count; i++)
            {
                Thing thing = job.targetQueueA[i].Thing;
                if (thing == null || thing.Destroyed || !SDS_Rules.CanDeposit(thing))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool CommonGates(Pawn pawn, Thing thing, bool forced)
        {
            return HardGates(pawn, thing, forced) && AutoOnlyGates(pawn, thing, forced);
        }

        private static bool HardGates(Pawn pawn, Thing thing, bool forced)
        {
            if (pawn == null || thing == null || pawn.Map == null || !pawn.Spawned)
            {
                return false;
            }
            if (pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }
            if (pawn.inventory == null || pawn.inventory.innerContainer == null)
            {
                return false;
            }
            if (!thing.Spawned || thing.Map != pawn.Map || thing.Destroyed)
            {
                return false;
            }
            if (!SDS_ChipManualDeposit.CanDepositThing(thing))
            {
                return false;
            }
            Danger danger = forced ? Danger.Deadly : pawn.NormalMaxDanger();
            return pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, danger,
                1, Math.Max(1, thing.stackCount), null, false);
        }

        private static bool AutoOnlyGates(Pawn pawn, Thing thing, bool forced)
        {
            if (forced)
            {
                return true;
            }
            if (thing.IsForbidden(pawn) || SDS_Rules.IsProducedMarked(thing)
                || (StoreUtility.IsInValidBestStorage(thing)
                    && !SDS_DepositService.IsGridAlwaysBetter(thing)))
            {
                return false;
            }
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, false))
            {
                return false;
            }
            return !SDS_DepositDispatchLedger.IsPending(thing)
                && !SDS_DepositDispatchLedger.IsFailureCoolingDown(pawn, thing);
        }
    }

    /// <summary>
    /// 「存入超维空间」派活的短期记账：物品级在途 + 失败退避，挡住同 tick 重复派活。
    /// </summary>
    internal static class SDS_DepositDispatchLedger
    {
        private const int PendingTicks = 300;
        private const int FailureCooldownTicks = 120;
        private const int PruneIntervalTicks = 500;
        private const int MaxEntries = 4096;
        private const int CapacityNoticeTicks = 6000;

        private static readonly Dictionary<Thing, int> pendingUntil = new Dictionary<Thing, int>();
        private static readonly Dictionary<long, int> failureUntil = new Dictionary<long, int>();
        private static int nextPruneTick;
        private static int capacityNoticeUntil;

        internal static void Reset()
        {
            pendingUntil.Clear();
            failureUntil.Clear();
            nextPruneTick = 0;
            capacityNoticeUntil = 0;
        }

        private static int Now
        {
            get { return Find.TickManager != null ? Find.TickManager.TicksGame : 0; }
        }

        public static bool IsPending(Thing thing)
        {
            if (thing == null || thing.Destroyed)
            {
                return false;
            }
            int now = Now;
            PruneIfNeeded(now);
            if (!pendingUntil.TryGetValue(thing, out int until))
            {
                return false;
            }
            if (now >= until)
            {
                pendingUntil.Remove(thing);
                return false;
            }
            return true;
        }

        public static void MarkPending(Thing thing)
        {
            if (thing == null || thing.Destroyed)
            {
                return;
            }
            int now = Now;
            PruneIfNeeded(now);
            int until = now + PendingTicks;
            if (!pendingUntil.TryGetValue(thing, out int old) || old < until)
            {
                pendingUntil[thing] = until;
            }
        }

        public static void Clear(Thing thing)
        {
            if (thing != null)
            {
                pendingUntil.Remove(thing);
            }
        }

        public static bool IsFailureCoolingDown(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null)
            {
                return false;
            }
            int now = Now;
            PruneIfNeeded(now);
            long key = MakeKey(pawn, thing);
            if (!failureUntil.TryGetValue(key, out int until))
            {
                return false;
            }
            if (now >= until)
            {
                failureUntil.Remove(key);
                return false;
            }
            return true;
        }

        public static void MarkFailure(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null)
            {
                return;
            }
            int now = Now;
            PruneIfNeeded(now);
            failureUntil[MakeKey(pawn, thing)] = now + FailureCooldownTicks;
            pendingUntil[thing] = now + FailureCooldownTicks;
            NotifyCapacityFullIfNeeded(thing);
        }

        /// <summary>
        /// W55 透明化：只在「空间可用 + 筛选允许 + 品类数已满 + 该物仍存不进」时提示一次（6000 tick 节流）。
        /// 不写日志，不改变任何派活 / 存入判定。
        /// </summary>
        public static void NotifyCapacityFullIfNeeded(Thing thing)
        {
            try
            {
                if (!SDS_Config.IgnorePriorityMode || thing == null || thing.Destroyed || thing.def == null)
                {
                    return;
                }
                if (thing.def.category != ThingCategory.Item || !thing.def.EverHaulable)
                {
                    return;
                }
                if (SDS_MirrorRegistry.IsMirror(thing)
                    || (SDS_Host.DepositFilter != null && !SDS_Host.DepositFilter.Allows(thing)))
                {
                    return;
                }
                if (!SDS_Rules.Available || SDS_Rules.CanDeposit(thing)
                    || SDS_Core.UsedCapacity < SDS_Rules.TypeCapacity)
                {
                    return;
                }
                int now = Now;
                if (now < capacityNoticeUntil)
                {
                    return;
                }
                capacityNoticeUntil = now + CapacityNoticeTicks;
                Messages.Message("SDS.Deposit.CapacityFull".Translate(
                        SDS_Core.UsedCapacity, SDS_Rules.TypeCapacity),
                    MessageTypeDefOf.RejectInput, false);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("存入容量提示失败（已忽略）", e);
            }
        }

        private static long MakeKey(Pawn pawn, Thing thing)
        {
            return ((long)(uint)pawn.thingIDNumber << 32) | (uint)thing.thingIDNumber;
        }

        private static void PruneIfNeeded(int now)
        {
            if (now < nextPruneTick && pendingUntil.Count + failureUntil.Count < MaxEntries)
            {
                return;
            }
            nextPruneTick = now + PruneIntervalTicks;

            if (pendingUntil.Count > 0)
            {
                List<Thing> expired = null;
                foreach (KeyValuePair<Thing, int> pair in pendingUntil)
                {
                    Thing t = pair.Key;
                    if (t == null || t.Destroyed || now >= pair.Value)
                    {
                        (expired ?? (expired = new List<Thing>())).Add(t);
                    }
                }
                if (expired != null)
                {
                    for (int i = 0; i < expired.Count; i++)
                    {
                        if (expired[i] != null)
                        {
                            pendingUntil.Remove(expired[i]);
                        }
                    }
                }
            }

            if (failureUntil.Count > 0)
            {
                List<long> expiredKeys = null;
                foreach (KeyValuePair<long, int> pair in failureUntil)
                {
                    if (now >= pair.Value)
                    {
                        (expiredKeys ?? (expiredKeys = new List<long>())).Add(pair.Key);
                    }
                }
                if (expiredKeys != null)
                {
                    for (int i = 0; i < expiredKeys.Count; i++)
                    {
                        failureUntil.Remove(expiredKeys[i]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 手动「存入超维空间」的判定：全部复用 Core 的唯一存入判定。
    /// </summary>
    internal static class SDS_ChipManualDeposit
    {
        public static bool PawnCanDeposit(Pawn pawn)
        {
            return pawn != null && SDS_ImplantUtility.IsPortOperational(pawn);
        }

        /// <summary>W53：可存入判定沿用端点唯一的“可存入实物”口径，镜像一律拒绝。</summary>
        public static bool CanDepositThing(Thing thing)
        {
            return SDS_EndpointUtility.IsDepositable(thing);
        }

        public static Job MakeDepositJob(Pawn pawn, Thing thing)
        {
            return SDS_DepositService.TryMakeManual(pawn, thing);
        }

        /// <summary>只有玩家手动入口（强制 + 无 workGiverDef）才向玩家弹回执。</summary>
        public static bool ShouldReportToPlayer(Job job)
        {
            return job != null && job.playerForced && job.workGiverDef == null;
        }

        public static int DepositInventory(Pawn pawn, out SDS_ManualDepositFailure failure)
        {
            failure = SDS_ManualDepositFailure.NoPort;
            if (pawn == null || pawn.inventory == null || !PawnCanDeposit(pawn))
            {
                return 0;
            }
            return SDS_DepositService.DepositNow(pawn, pawn.inventory.innerContainer, out failure);
        }
    }

    /// <summary>
    /// 存入 Job 的终止清理：只处理 Job 账本里的精确引用。
    /// 挂起不吐货；终止未成功才把账本里仍在背包的物品落地。
    /// </summary>
    internal static class SDS_DepositJobCleanup
    {
        public static void OnCleanup(Pawn pawn, Job job, JobCondition condition, bool releaseReservations)
        {
            if (pawn == null || job == null || !releaseReservations || job.def != SDS_JobDefOf.DepositToGrid)
            {
                return;
            }
            if (pawn.inventory == null || pawn.inventory.innerContainer == null || pawn.Map == null)
            {
                return;
            }

            List<Thing> ledger = new List<Thing>();
            if (job.targetQueueA != null)
            {
                for (int i = 0; i < job.targetQueueA.Count; i++)
                {
                    Thing item = job.targetQueueA[i].Thing;
                    if (item != null && !item.Destroyed)
                    {
                        ledger.Add(item);
                    }
                }
            }
            Thing targetA = job.targetA.Thing;
            if (targetA != null && !targetA.Destroyed)
            {
                ledger.Add(targetA);
            }

            for (int i = 0; i < ledger.Count; i++)
            {
                Thing item = ledger[i];
                if (!ReferenceEquals(item.holdingOwner, pawn.inventory.innerContainer))
                {
                    continue;
                }
                if (!pawn.inventory.innerContainer.Remove(item))
                {
                    continue;
                }
                SDS_PlacementUtility.TryPlaceOrSpawn(item, pawn.Position, pawn.Map);
                if (condition != JobCondition.Succeeded)
                {
                    SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                }
                else
                {
                    SDS_DepositDispatchLedger.Clear(item);
                }
            }
        }
    }

    /// <summary>
    /// 存入 Job 执行器：单件 / 多件；每件在端点处 TryDeposit，失败落地并重派。
    /// </summary>
    public class JobDriver_SDS_DepositToGrid : JobDriver
    {
        internal const TargetIndex ItemInd = TargetIndex.A;

        private int nextIndex;
        private int stuckTicks;
        private bool partialPickup;
        private bool pickupBlocked;
        private int partialRemaining;

        private List<LocalTargetInfo> Queue
        {
            get { return job != null ? job.targetQueueA : null; }
        }

        private int QueueCount
        {
            get
            {
                List<LocalTargetInfo> queue = Queue;
                if (queue != null && queue.Count > 0)
                {
                    return queue.Count;
                }
                return job != null && job.targetA.IsValid ? 1 : 0;
            }
        }

        private Thing ItemAt(int index)
        {
            List<LocalTargetInfo> queue = Queue;
            if (queue != null && queue.Count > 0)
            {
                return index >= 0 && index < queue.Count ? queue[index].Thing : null;
            }
            return index == 0 && job != null ? job.targetA.Thing : null;
        }

        private Thing EndpointThing
        {
            get { return job != null ? job.targetB.Thing : null; }
        }

        private bool SelfEndpoint
        {
            get
            {
                Thing endpoint = EndpointThing;
                return endpoint == null || endpoint == pawn;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (pawn == null || job == null || pawn.inventory == null
                || pawn.inventory.innerContainer == null)
            {
                return false;
            }
            List<LocalTargetInfo> queue = job.targetQueueA;
            if (queue == null || queue.Count == 0)
            {
                queue = new List<LocalTargetInfo>();
                if (job.targetA.IsValid)
                {
                    queue.Add(job.targetA);
                }
                job.targetQueueA = queue;
            }
            if (job.countQueue == null)
            {
                job.countQueue = new List<int>();
            }

            List<LocalTargetInfo> kept = new List<LocalTargetInfo>();
            List<int> keptCounts = new List<int>();
            for (int i = 0; i < queue.Count; i++)
            {
                Thing item = queue[i].Thing;
                if (item == null || item.Destroyed)
                {
                    continue;
                }
                if (ReferenceEquals(item.holdingOwner, pawn.inventory.innerContainer)
                    || (pawn.carryTracker != null && ReferenceEquals(pawn.carryTracker.CarriedThing, item)))
                {
                    kept.Add(queue[i]);
                    keptCounts.Add(Math.Max(1, item.stackCount));
                    continue;
                }
                if (!item.Spawned)
                {
                    continue;
                }
                int count = Math.Max(1, item.stackCount);
                if (!pawn.Reserve(new LocalTargetInfo(item), job, 1, count, null, errorOnFailed))
                {
                    continue;
                }
                kept.Add(queue[i]);
                keptCounts.Add(count);
                SDS_DepositDispatchLedger.MarkPending(item);
            }
            queue.Clear();
            queue.AddRange(kept);
            job.countQueue = keptCounts;
            if (queue.Count == 0)
            {
                ClearPending(queue);
                return false;
            }
            job.SetTarget(ItemInd, queue[0]);
            job.count = keptCounts[0];

            Thing endpoint = EndpointThing;
            if (endpoint != null && endpoint != pawn && (endpoint.Destroyed || !endpoint.Spawned))
            {
                ClearPending(queue);
                return false;
            }
            return true;
        }

        private void ClearPending(List<LocalTargetInfo> queue)
        {
            if (queue == null)
            {
                return;
            }
            for (int i = 0; i < queue.Count; i++)
            {
                Thing item = queue[i].Thing;
                if (item != null)
                {
                    SDS_DepositDispatchLedger.Clear(item);
                }
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (job == null)
            {
                yield break;
            }
            if (job.targetQueueA == null || job.targetQueueA.Count == 0)
            {
                job.targetQueueA = new List<LocalTargetInfo>();
                if (job.targetA.IsValid)
                {
                    job.targetQueueA.Add(job.targetA);
                }
            }

            Thing endpoint = EndpointThing;
            PathEndMode endpointMode = EndpointPathMode(endpoint);

            if (endpoint != null && endpoint != pawn)
            {
                yield return Toils_General.Do(delegate
                {
                    Thing ep = EndpointThing;
                    if (ep == null || ep.Destroyed || !ep.Spawned
                        || !pawn.CanReach(ep, endpointMode, pawn.NormalMaxDanger()))
                    {
                        FailWithMessage(null);
                    }
                });
            }

            nextIndex = 0;
            stuckTicks = 0;

            Toil deposit = ToilMaker.MakeToil("SDS_DepositAll");
            deposit.defaultCompleteMode = ToilCompleteMode.Instant;
            deposit.initAction = DepositAll;

            Toil gotoEndpoint = null;
            if (!SelfEndpoint)
            {
                gotoEndpoint = ToilMaker.MakeToil("SDS_GotoEndpoint");
                gotoEndpoint.initAction = delegate
                {
                    Thing ep = EndpointThing;
                    if (ep == null || ep.Destroyed || !ep.Spawned)
                    {
                        FailWithMessage(null);
                        return;
                    }
                    pawn.pather.StartPath(ep, endpointMode);
                };
                gotoEndpoint.defaultCompleteMode = ToilCompleteMode.PatherArrival;
                gotoEndpoint.AddFailCondition(delegate
                {
                    Thing ep = EndpointThing;
                    return ep == null || ep.Destroyed || !ep.Spawned
                        || !pawn.CanReach(ep, endpointMode, pawn.NormalMaxDanger());
                });
            }

            Toil next = gotoEndpoint ?? deposit;
            Toil pickup = ToilMaker.MakeToil("SDS_PickupLoop");
            pickup.defaultCompleteMode = ToilCompleteMode.Never;
            pickup.initAction = delegate { StartNextPickup(); };
            pickup.tickAction = delegate
            {
                TickPickup();
                if (nextIndex >= QueueCount && pawn != null && pawn.jobs != null)
                {
                    pawn.jobs.curDriver.JumpToToil(next);
                }
            };
            pickup.AddFailCondition(delegate
            {
                return pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned || pawn.Map == null;
            });
            yield return pickup;

            if (gotoEndpoint != null)
            {
                yield return gotoEndpoint;
            }
            yield return deposit;
        }

        private static PathEndMode EndpointPathMode(Thing endpoint)
        {
            if (endpoint == null || endpoint is Pawn)
            {
                return PathEndMode.Touch;
            }
            if (SDS_EndpointHandle.TryFromThing(endpoint, out SDS_EndpointHandle handle))
            {
                return handle.AccessMode;
            }
            return PathEndMode.Touch;
        }

        private void StartNextPickup()
        {
            stuckTicks = 0;
            int count = QueueCount;
            while (nextIndex < count)
            {
                Thing item = ItemAt(nextIndex);
                if (item == null || item.Destroyed || !item.Spawned
                    || item.Map != pawn.Map
                    || ReferenceEquals(item.holdingOwner, pawn.inventory.innerContainer))
                {
                    nextIndex++;
                    continue;
                }
                // W54：存入执行器不得按镜像真身坐标（隐藏金库）走位；正常路径已由 W53 闸门挡住，这里只做防御。
                if (SDS_MirrorRegistry.IsMirror(item))
                {
                    SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                    nextIndex++;
                    continue;
                }
                if (!pawn.CanReach(item, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
                {
                    SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                    nextIndex++;
                    continue;
                }
                if (!pawn.CanReserve(item, 1, Math.Max(1, item.stackCount), null, false))
                {
                    nextIndex++;
                    continue;
                }
                pawn.pather.StartPath(item, PathEndMode.ClosestTouch);
                return;
            }
        }

        private void TickPickup()
        {
            if (nextIndex >= QueueCount)
            {
                return;
            }
            Thing item = ItemAt(nextIndex);
            if (item == null || item.Destroyed || !item.Spawned)
            {
                nextIndex++;
                StartNextPickup();
                return;
            }
            if ((pawn.Position - item.Position).LengthHorizontalSquared <= 2)
            {
                Pickup(item, nextIndex);
                nextIndex++;
                StartNextPickup();
                return;
            }
            if (!pawn.pather.Moving)
            {
                stuckTicks++;
                if (stuckTicks > 120)
                {
                    SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                    nextIndex++;
                    StartNextPickup();
                    return;
                }
                StartNextPickup();
            }
        }

        private void Pickup(Thing item, int index)
        {
            if (item == null || item.Destroyed || !item.Spawned)
            {
                return;
            }
            if (SDS_MirrorRegistry.IsMirror(item))
            {
                Log.WarningOnce("[SDS] 存入执行器收到镜像，已拒绝；这属于逻辑错误，不应发生。",
                    0x5D5D0001);
                pickupBlocked = true;
                SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                return;
            }
            IntVec3 cell = item.PositionHeld;
            Map map = item.MapHeld;
            int canTake = pawn.inventory.innerContainer.GetCountCanAccept(item, false);
            if (canTake <= 0 || item.stackCount <= 0)
            {
                pickupBlocked = true;
                partialRemaining += Math.Max(0, item.stackCount);
                if (job != null)
                {
                    job.reportStringOverride = "SDS.JobReport.DepositBlocked"
                        .Translate(item.LabelShort).ToString();
                }
                SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                return;
            }
            Thing piece;
            if (canTake < item.stackCount)
            {
                partialPickup = true;
                partialRemaining += item.stackCount - canTake;
                if (job != null)
                {
                    job.reportStringOverride = "SDS.JobReport.DepositPartial"
                        .Translate(item.LabelShort).ToString();
                }
                piece = item.SplitOff(canTake);
                if (piece == null)
                {
                    SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                    return;
                }
            }
            else
            {
                item.DeSpawn(DestroyMode.Vanish);
                piece = item;
            }
            if (!pawn.inventory.innerContainer.TryAdd(piece, canMergeWithExistingStacks: false))
            {
                SDS_Diag.Fail("存入/入包失败", SDS_Diag.Ctx(pawn.Map, pawn, piece, job,
                    "count=" + (piece != null ? piece.stackCount : 0)));
                int pieceCount = piece != null && !piece.Destroyed ? piece.stackCount : 0;
                if (!ReferenceEquals(piece, item) && item != null && !item.Destroyed && item.Spawned
                    && piece != null && !piece.Destroyed && piece.Spawned
                    && item.def == piece.def && item.Stuff == piece.Stuff
                    && item.CanStackWith(piece)
                    && !SDS_MirrorRegistry.IsMirror(item) && !SDS_MirrorRegistry.IsMirror(piece)
                    && item.TryAbsorbStack(piece, true))
                {
                    partialRemaining += pieceCount;
                    pickupBlocked = true;
                    SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                    return;
                }
                if (piece != null && !piece.Destroyed)
                {
                    SDS_PlacementUtility.TryPlaceOrSpawn(piece, cell, map);
                }
                partialRemaining += pieceCount;
                pickupBlocked = true;
                SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                if (piece != null && !piece.Destroyed)
                {
                    SDS_DepositDispatchLedger.MarkFailure(pawn, piece);
                }
                return;
            }
            if (job.targetQueueA != null && index >= 0 && index < job.targetQueueA.Count)
            {
                job.targetQueueA[index] = new LocalTargetInfo(piece);
                if (job.countQueue != null && index < job.countQueue.Count)
                {
                    job.countQueue[index] = Math.Max(1, piece.stackCount);
                }
            }
            if (!ReferenceEquals(piece, item) && item != null && !item.Destroyed)
            {
                SDS_DepositDispatchLedger.Clear(item);
            }
        }

        private void DepositAll()
        {
            TakeCarriedIntoInventory();

            Thing endpoint = EndpointThing;
            bool self = endpoint == null || endpoint == pawn;
            ISDS_Port port = null;
            if (self)
            {
                SDS_Host.Ports.TryGetSelfPort(pawn, out port);
            }
            else
            {
                SDS_EndpointHandle.TryFromThing(endpoint, out SDS_EndpointHandle handle);
                port = handle;
            }

            bool failed = port == null;
            int deposited = 0;
            int count = QueueCount;
            for (int i = 0; i < count; i++)
            {
                Thing item = ItemAt(i);
                if (item == null || item.Destroyed)
                {
                    continue;
                }
                if (!ReferenceEquals(item.holdingOwner, pawn.inventory.innerContainer))
                {
                    continue;
                }
                if (port == null
                    || !(job.playerForced
                        ? SDS_EndpointUtility.AcceptsForManual(port, item)
                        : SDS_EndpointUtility.AcceptsForAuto(port, item)))
                {
                    failed = true;
                    SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                    continue;
                }
                // 所有权转移只由 SDS_Rules.TryDeposit 的唯一实现完成；驱动器不再预摘背包。
                int amount = item.stackCount;
                int accepted = 0;
                if (SDS_Rules.TryDeposit(item, port, out accepted) && accepted > 0)
                {
                    deposited += amount;
                    SDS_DepositDispatchLedger.Clear(item);
                }
                else
                {
                    SDS_Diag.Fail("存入/入账失败", SDS_Diag.Ctx(pawn.Map, pawn, item, job,
                        "accepted=" + accepted));
                    failed = true;
                    SDS_DepositDispatchLedger.MarkFailure(pawn, item);
                    DropFromInventory(pawn, item);
                }
            }

            if (failed)
            {
                FailWithMessage(null);
                return;
            }
            if (deposited <= 0 && pickupBlocked)
            {
                FailWithMessage(null);
                return;
            }
            if (deposited > 0)
            {
                bool report = SDS_ChipManualDeposit.ShouldReportToPlayer(job);
                bool reportPartial = report || (job != null && job.playerForced);
                Thing first = job != null ? job.targetA.Thing : null;
                string label = first != null && !first.Destroyed ? first.LabelShort : "?";
                if (reportPartial && (partialPickup || pickupBlocked))
                {
                    Messages.Message("SDS.Chip.DepositPartial".Translate(label, deposited, partialRemaining),
                        pawn, MessageTypeDefOf.TaskCompletion, false);
                }
                else if (report)
                {
                    Messages.Message("SDS.Chip.DepositItemDone".Translate(label, deposited),
                        pawn, MessageTypeDefOf.TaskCompletion, false);
                }
            }
            EndJobWith(JobCondition.Succeeded);
        }

        /// <summary>
        /// 交付清理：入账失败时把仍在小人背包里的实物按原版 Drop 放回地面。
        /// 这是交付动作，不承担所有权转移（转移只在 SDS_Rules.TryDeposit）。
        /// </summary>
        private static void DropFromInventory(Pawn pawn, Thing item)
        {
            if (pawn == null || item == null || item.Destroyed || pawn.inventory == null)
            {
                return;
            }
            ThingOwner inventory = pawn.inventory.innerContainer;
            Map map = pawn.MapHeld;
            if (inventory == null || map == null
                || !ReferenceEquals(item.holdingOwner, inventory))
            {
                return;
            }
            if (!inventory.TryDrop(item, pawn.PositionHeld, map, ThingPlaceMode.Near, out Thing _))
            {
                SDS_Diag.Fail("存入/落地清理失败", SDS_Diag.Ctx(map, pawn, item));
            }
        }

        /// <summary>把「执行中已经拿在手上」的队列货物转进背包。</summary>
        private void TakeCarriedIntoInventory()
        {
            Pawn_CarryTracker carry = pawn != null ? pawn.carryTracker : null;
            if (carry == null || pawn.inventory == null || pawn.inventory.innerContainer == null)
            {
                return;
            }
            Thing carried = carry.CarriedThing;
            if (carried == null || carried.Destroyed || !QueueContains(carried))
            {
                return;
            }
            carry.innerContainer.TryTransferToContainer(carried, pawn.inventory.innerContainer,
                carried.stackCount, out Thing _, canMergeWithExistingStacks: false);
            Thing remaining = carry.CarriedThing;
            if (remaining != null && !remaining.Destroyed && QueueContains(remaining))
            {
                SDS_Diag.Warn("存入/手上货物落地", SDS_Diag.Ctx(pawn.Map, pawn, remaining, job,
                    "count=" + remaining.stackCount));
                carry.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
            }
        }

        private bool QueueContains(Thing item)
        {
            List<LocalTargetInfo> queue = Queue;
            if (queue == null || item == null)
            {
                return false;
            }
            for (int i = 0; i < queue.Count; i++)
            {
                if (ReferenceEquals(queue[i].Thing, item))
                {
                    return true;
                }
            }
            return false;
        }

        private void FailWithMessage(Thing item)
        {
            Thing failed = item;
            if (failed == null && job != null)
            {
                failed = job.targetA.Thing;
            }
            if (failed != null && !failed.Destroyed)
            {
                SDS_DepositDispatchLedger.MarkFailure(pawn, failed);
            }
            if (SDS_ChipManualDeposit.ShouldReportToPlayer(job)
                || (job != null && job.playerForced))
            {
                Messages.Message("SDS.Chip.DepositItemFailed".Translate(
                        failed != null && !failed.Destroyed ? failed.LabelShort : "?"),
                    pawn, MessageTypeDefOf.RejectInput, false);
            }
            EndJobWith(JobCondition.Incompletable);
        }
    }
}
