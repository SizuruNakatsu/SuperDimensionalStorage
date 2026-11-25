using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    // ============================================================================
    // 本文件只保留「全局判定 / 统计」收敛点：
    //  1. ResourceCounter：资源汇总条要看到镜像；
    //  2. RecipeDef.PotentiallyMissingIngredients：配方 UI 不能把镜像里的原料算成缺料；
    //  3. Map.GetChildHolders：端点 / 金库 / 带投影小人在 Map 级只暴露一份；
    //  4. WealthWatcher：保真通道补差 + 「不计入殖民地财富」开关的一次性减法；
    //  5. 搬运派活：HaulToStorageJob / HaulToCellStorageJob / HaulToContainerJob 唯一函数级出口；
    //  6. W63：只消费落点格的 Job 先折叠落点，失败才退让。
    // ============================================================================

    /// <summary>资源汇总条要看到超维空间库存。</summary>
    [HarmonyPatch(typeof(ResourceCounter), "UpdateResourceCounts")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_ResourceCounterWithGrid
    {
        private static FieldInfo countedAmountsField;
        private static FieldInfo resourceMapField;
        private static readonly List<Thing> preservedBuffer = new List<Thing>();
        private static readonly List<SDS_StockKey> mirrorKeyBuffer = new List<SDS_StockKey>();
        private static readonly HashSet<ThingDef> defBuffer = new HashSet<ThingDef>();

        public static void Postfix(ResourceCounter __instance)
        {
            try
            {
                ISDS_LedgerRead ledger = SDS_Core.Ledger;
                if (ledger == null || __instance == null)
                {
                    return;
                }
                if (countedAmountsField == null)
                {
                    countedAmountsField = AccessTools.Field(typeof(ResourceCounter), "countedAmounts");
                }
                if (resourceMapField == null)
                {
                    resourceMapField = AccessTools.Field(typeof(ResourceCounter), "map");
                }
                Map map = resourceMapField != null ? resourceMapField.GetValue(__instance) as Map : null;
                if (map == null)
                {
                    return;
                }
                bool exposed = SDS_Exposure.MapExposesGrid(map);
                Dictionary<ThingDef, int> counts = countedAmountsField != null
                    ? countedAmountsField.GetValue(__instance) as Dictionary<ThingDef, int>
                    : null;
                if (counts == null)
                {
                    return;
                }
                List<SDS_StockEntry> snapshot = ledger.Snapshot(0);
                defBuffer.Clear();
                for (int i = 0; i < snapshot.Count; i++)
                {
                    SDS_StockEntry entry = snapshot[i];
                    if (entry == null || entry.key == null || entry.key.thingDef == null || entry.count <= 0)
                    {
                        continue;
                    }
                    if (entry.key.thingDef.CountAsResource)
                    {
                        defBuffer.Add(entry.key.thingDef);
                    }
                }
                mirrorKeyBuffer.Clear();
                SDS_MirrorService.CopyMirrorKeys(map, mirrorKeyBuffer);
                for (int i = 0; i < mirrorKeyBuffer.Count; i++)
                {
                    ThingDef def = mirrorKeyBuffer[i] != null ? mirrorKeyBuffer[i].thingDef : null;
                    if (def != null && def.CountAsResource)
                    {
                        defBuffer.Add(def);
                    }
                }
                if (exposed)
                {
                    preservedBuffer.Clear();
                    ledger.AppendPreservedThings(preservedBuffer);
                    for (int i = 0; i < preservedBuffer.Count; i++)
                    {
                        Thing thing = preservedBuffer[i];
                        if (thing != null && !thing.Destroyed && thing.def != null && thing.def.CountAsResource)
                        {
                            defBuffer.Add(thing.def);
                        }
                    }
                }
                foreach (ThingDef def in defBuffer)
                {
                    long add = SDS_Exposure.InjectedOnCounter(map, def, exposed);
                    if (add == 0L)
                    {
                        continue;
                    }
                    int cur = counts.TryGetValue(def, out int existing) ? existing : 0;
                    long next = (long)cur + add;
                    counts[def] = next <= 0L ? 0 : (int)System.Math.Min(next, int.MaxValue);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("资源汇总注入超维空间库存失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>资源汇总条的快速刷新：具体规则全部在 SDS_Exposure 一处定义。</summary>
    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.ResourceCounterTick))]
    internal static class Patch_SDS_ResourceCounterFastRefresh
    {
        private static FieldInfo mapField;

        public static void Prefix(ResourceCounter __instance)
        {
            try
            {
                if (__instance == null || SDS_Core.Ledger == null)
                {
                    return;
                }
                Map map = MapOf(__instance);
                if (map == null || !SDS_Exposure.TryBeginCounterRefresh(map))
                {
                    return;
                }
                __instance.UpdateResourceCounts();
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("资源汇总快速刷新失败（已忽略，原版 204 tick 周期不受影响）", e);
            }
        }

        private static Map MapOf(ResourceCounter counter)
        {
            if (mapField == null)
            {
                mapField = AccessTools.Field(typeof(ResourceCounter), "map");
            }
            return mapField != null ? mapField.GetValue(counter) as Map : null;
        }
    }

    // ============================ 配方缺料判定 ============================

    [HarmonyPatch(typeof(RecipeDef), "PotentiallyMissingIngredients")]
    internal static class Patch_SDS_PotentiallyMissingIngredients
    {
        public static void Postfix(RecipeDef __instance, Pawn billDoer, Map map, ref IEnumerable<ThingDef> __result)
        {
            try
            {
                if (__result == null || __instance == null || !SDS_Rules.Available
                    || !SDS_Exposure.MapExposesGrid(map))
                {
                    return;
                }
                List<ThingDef> missing = null;
                bool removedAny = false;
                foreach (ThingDef def in __result)
                {
                    if (def == null)
                    {
                        removedAny = true;
                        continue;
                    }
                    (missing ?? (missing = new List<ThingDef>())).Add(def);
                }
                HashSet<ThingDef> satisfied = missing != null
                    ? SDS_BillIngredientQuery.CollectSatisfied(__instance, missing, billDoer, map)
                    : null;
                List<ThingDef> kept = null;
                if (missing != null)
                {
                    for (int i = 0; i < missing.Count; i++)
                    {
                        ThingDef def = missing[i];
                        if (satisfied != null && satisfied.Contains(def))
                        {
                            removedAny = true;
                            continue;
                        }
                        (kept ?? (kept = new List<ThingDef>())).Add(def);
                    }
                }
                if (removedAny)
                {
                    __result = kept ?? new List<ThingDef>();
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("配方缺料判定注入超维空间失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    // ============================ Map 级只暴露一份 ============================

    [HarmonyPatch(typeof(Map), "GetChildHolders")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MapChildHoldersExposure
    {
        public static void Postfix(Map __instance, List<IThingHolder> outChildren)
        {
            try
            {
                if (__instance == null || outChildren == null || SDS_Core.Ledger == null)
                {
                    return;
                }
                // 1) 摘掉本 Mod 的端点与隐藏金库：它们各自挂着投影，Map 级枚举会把同一批镜像算 N 遍。
                for (int i = outChildren.Count - 1; i >= 0; i--)
                {
                    if (outChildren[i] is ISDS_StorageEndpoint || outChildren[i] is Building_SDS_Vault)
                    {
                        outChildren.RemoveAt(i);
                    }
                }
                // 2) 带投影的小人 / 机械体：换成包装 holder，镜像只由 spawned 列表计一次。
                if (!SDS_MirrorService.HasAnyMirrorOn(__instance))
                {
                    return;
                }
                for (int i = 0; i < outChildren.Count; i++)
                {
                    if (outChildren[i] is Pawn pawn && SDS_ImplantUtility.IsPortOperational(pawn))
                    {
                        outChildren[i] = SDS_ProjectionService.MapLevelHolderFor(pawn);
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("共享库存向 Map 级枚举暴露失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    // ============================ 财富归一化 ============================

    [HarmonyPatch(typeof(WealthWatcher), "CalculateWealthItems")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_WealthNormalize
    {
        public static void Postfix(WealthWatcher __instance, Map ___map, ref float __result)
        {
            try
            {
                if (__instance == null || ___map == null || SDS_Core.Ledger == null)
                {
                    return;
                }
                float adjust = SDS_Exposure.WealthAdjust(___map);
                if (adjust == 0f)
                {
                    return;
                }
                __result = __result + adjust > 0f ? __result + adjust : 0f;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("殖民地财富归一化失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    // ============================ 搬运派活出口 ============================

    [HarmonyPatch(typeof(WorkGiver_Haul), "JobOnThing")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_HaulGeneralToGrid
    {
        public static void Postfix(WorkGiver_Haul __instance, Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            try
            {
                if (pawn == null || t == null)
                {
                    return;
                }
                if (__result != null && __result.def == SDS_JobDefOf.DepositToGrid
                    && __result.workGiverDef == null && __instance != null)
                {
                    __result.workGiverDef = __instance.def;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("超维空间「搬运即存入」派活失败（已忽略，原版搬运行为不受影响）", e);
            }
        }
    }

    /// <summary>JobGiver_Haul.TryGiveJob 的补派适配器。</summary>
    [HarmonyPatch(typeof(JobGiver_Haul), "TryGiveJob")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_JobGiverHaul
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (__result != null || pawn == null || !pawn.Spawned || pawn.Map == null
                    || pawn.Faction != Faction.OfPlayer)
                {
                    return;
                }
                if (!SDS_Rules.Available)
                {
                    return;
                }
                Thing thing = GenClosest.ClosestThing_Global_Reachable(
                    pawn.Position, pawn.Map, pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling(),
                    PathEndMode.OnCell, TraverseParms.For(pawn), 9999f, Validator);
                if (thing == null)
                {
                    return;
                }
                Job job = SDS_DepositService.TryMake(pawn, thing, false, null, true);
                if (job != null)
                {
                    __result = job;
                }

                bool Validator(Thing t)
                {
                    return t != null && !t.IsForbidden(pawn)
                        && !SDS_MirrorRegistry.IsMirror(t)
                        && SDS_DepositService.CanCarryCandidate(pawn, t, false);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("超维空间「JobGiver 搬运」补派失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>原版「存储投递 Job」的唯一函数级出口。</summary>
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToStorageJob))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_HaulToStorageJob
    {
        public static void Postfix(Pawn p, Thing t, bool forced, ref Job __result)
        {
            try
            {
                if (p == null || t == null)
                {
                    return;
                }
                if (__result != null && __result.def == SDS_JobDefOf.DepositToGrid)
                {
                    return;
                }
                Job job = SDS_DepositService.TryMake(p, t, forced, __result, true);
                if (job != null)
                {
                    __result = job;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("超维空间「存储投递」派活失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>「某件物品 → 某个仓储格」Job 工厂的共同出口。</summary>
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToCellStorageJob))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_CarriedProductToGrid
    {
        public static void Postfix(Pawn p, Thing t, ref Job __result)
        {
            try
            {
                if (p == null || t == null || __result == null || __result.def == null)
                {
                    return;
                }
                if (__result.def == SDS_JobDefOf.DepositToGrid)
                {
                    return;
                }
                Job job = SDS_DepositService.TryMakeCarried(p, t, __result);
                if (job == null)
                {
                    job = SDS_DepositService.TryMake(p, t, false, __result, true);
                }
                if (job != null)
                {
                    __result = job;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("超维空间「格搬运」派活失败（已忽略，原版搬运行为不受影响）", e);
            }
        }
    }

    /// <summary>原版「容器搬运」Job 工厂——机会搬运的唯一容器出口。</summary>
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToContainerJob))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_HaulToContainerJob
    {
        public static void Postfix(Pawn p, Thing t, Thing container, ref Job __result)
        {
            try
            {
                if (p == null || t == null || container == null || __result == null || __result.def == null)
                {
                    return;
                }
                if (__result.def == SDS_JobDefOf.DepositToGrid)
                {
                    return;
                }
                Job job = SDS_DepositService.TryMake(p, t, false, __result, true);
                if (job != null)
                {
                    __result = job;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("超维空间「容器搬运」派活失败（已忽略，原版搬运行为不受影响）", e);
            }
        }
    }

    // ============================ W63：只消费落点格的 Job 折叠 / 退让 ============================

    internal static class SDS_CellOnlyJobShape
    {
        /// <summary>
        /// cell-only 搜索基线：内容物正在被取出，当前容器不计入候选；
        /// 与 vanilla cell-only WorkGiver（EmptyWasteContainer / EmptyEggBox 等）同口径。
        /// </summary>
        private const StoragePriority CellOnlySearchPriority = StoragePriority.Unstored;

        /// <summary>
        /// 取“内容物的最佳目的地是超维建筑端点、且原版没有有效落点格”的结果。
        /// W63 落点改写与失败退让共用这一份判定。
        /// </summary>
        private static bool TryGetEndpointDestination(Pawn pawn, Thing contents,
            out ISDS_StorageEndpoint endpoint)
        {
            endpoint = null;
            if (pawn == null || contents == null || contents.Destroyed || pawn.Map == null)
            {
                return false;
            }
            if (!StoreUtility.TryFindBestBetterStorageFor(contents, pawn, pawn.Map,
                    CellOnlySearchPriority, pawn.Faction, out IntVec3 cell,
                    out IHaulDestination destination, needAccurateResult: false))
            {
                return false;
            }
            if (cell.IsValid)
            {
                return false;
            }
            endpoint = destination as ISDS_StorageEndpoint;
            return endpoint != null;
        }

        private static bool IsCellOnlyJobShape(Job job)
        {
            if (job == null || job.def == null || job.def == SDS_JobDefOf.DepositToGrid)
            {
                return false;
            }
            Thing container = job.targetA.Thing;
            Thing contents = job.targetB.Thing;
            if (container == null || contents == null || container.Destroyed || contents.Destroyed
                || container is Pawn)
            {
                return false;
            }
            if (job.targetC.HasThing || job.targetC.IsValid)
            {
                return false;
            }
            ThingOwner owner = container.TryGetInnerInteractableThingOwner();
            return owner != null && owner.Contains(contents);
        }

        /// <summary>
        /// 一次形状 + 存储搜索：同时回答“是否 cell-only 且最佳目的地是超维端点”，
        /// 供 W63 改写与失败退让共用，避免失败路径重复 search。
        /// </summary>
        public static bool TryGetEndpointDestinationJob(Pawn pawn, Job job,
            out ISDS_StorageEndpoint endpoint)
        {
            endpoint = null;
            try
            {
                if (pawn == null || !pawn.Spawned || pawn.Map == null)
                {
                    return false;
                }
                if (!IsCellOnlyJobShape(job))
                {
                    return false;
                }
                return TryGetEndpointDestination(pawn, job.targetB.Thing, out endpoint);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("落点格 Job 判定失败（已忽略，原版行为不受影响）", e);
                endpoint = null;
                return false;
            }
        }

        public static bool MayHoldContents(Thing t)
        {
            if (t == null || t.Destroyed || t is Pawn)
            {
                return false;
            }
            ThingOwner owner = t.TryGetInnerInteractableThingOwner();
            return owner != null && owner.Count > 0;
        }
    }

    /// <summary>
    /// W63：玩家直接下单的 cell-only Job（如提取圣物）先折叠落点，再交原版预留 / 执行。
    /// 不再按入口置灰；折叠失败才由原版自己失败。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_CellOnlyOrderedJob
    {
        public static void Prefix(Pawn ___pawn, Job __0)
        {
            if (__0 == null)
            {
                return;
            }
            try
            {
                SDS_CellOnlyDestinationFold.TryRewrite(___pawn, __0);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("cell-only 直接下单折叠失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>
    /// W63 统一版：运行期枚举全部 WorkGiver 实现，把「落点格 Job」的折叠 / 退让挂到契约层。
    /// 形状判定仍只在 SDS_CellOnlyJobShape 一处。
    /// </summary>
    internal static class SDS_CellOnlyWorkGiverPatches
    {
        private static readonly HashSet<MethodInfo> patchedJobOnThing = new HashSet<MethodInfo>();
        private static readonly HashSet<MethodInfo> patchedHasJobOnThing = new HashSet<MethodInfo>();

        [ThreadStatic]
        private static bool probing;

        internal static bool IsProbing
        {
            get { return probing; }
        }

        internal static bool Applied { get; private set; }
        internal static bool PlacementHookApplied { get; private set; }
        internal static int JobOnThingCount { get; private set; }
        internal static int HasJobOnThingCount { get; private set; }
        internal static int W63RewriteCount { get; private set; }
        internal static int W63ProbeCount { get; private set; }
        internal static int W63FallbackJobCount { get; private set; }
        internal static int W63FallbackProbeCount { get; private set; }

        private static bool placementPatched;

        /// <summary>E1：换档 / 重载时清空 W63 诊断计数，不跨档累加。</summary>
        internal static void ResetDiagnostics()
        {
            W63RewriteCount = 0;
            W63ProbeCount = 0;
            W63FallbackJobCount = 0;
            W63FallbackProbeCount = 0;
        }

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }
            TryPatchPlacementHook(harmony);
            if (Applied)
            {
                return;
            }
            try
            {
                List<WorkGiverDef> defs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
                if (defs == null || defs.Count == 0)
                {
                    return;
                }
                Applied = true;
                MethodInfo jobPostfix = AccessTools.Method(typeof(SDS_CellOnlyWorkGiverPatches),
                    nameof(JobOnThingPostfix));
                MethodInfo hasPostfix = AccessTools.Method(typeof(SDS_CellOnlyWorkGiverPatches),
                    nameof(HasJobOnThingPostfix));
                List<string> failures = null;
                for (int i = 0; i < defs.Count; i++)
                {
                    try
                    {
                        WorkGiver worker = defs[i] != null ? defs[i].Worker : null;
                        Type workerType = worker != null ? worker.GetType() : null;
                        if (workerType == null)
                        {
                            continue;
                        }
                        MethodInfo job = FindDeclaredMethod(workerType, "JobOnThing",
                            new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                        if (job != null && job.DeclaringType != typeof(WorkGiver_Scanner)
                            && patchedJobOnThing.Add(job)
                            && TryPatch(harmony, job, jobPostfix, ref failures))
                        {
                            JobOnThingCount++;
                        }
                        MethodInfo has = FindDeclaredMethod(workerType, "HasJobOnThing",
                            new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                        if (has != null && has.DeclaringType != typeof(WorkGiver_Scanner)
                            && patchedHasJobOnThing.Add(has)
                            && TryPatch(harmony, has, hasPostfix, ref failures))
                        {
                            HasJobOnThingCount++;
                        }
                    }
                    catch (Exception e)
                    {
                        SDS_Diag.ErrorOnce("落点格 WorkGiver 定义枚举失败（已跳过该定义）", e);
                    }
                }
                Log.Message("[超维存储] 落点格 WorkGiver 统一守卫：JobOnThing " + JobOnThingCount
                    + " / HasJobOnThing " + HasJobOnThingCount
                    + "（枚举 " + defs.Count + " 个 WorkGiverDef）。");
                if (failures != null)
                {
                    Log.Warning("[超维存储][落点格守卫] " + failures.Count + " 个挂点失败，已跳过："
                        + string.Join("，", failures.ToArray()));
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("落点格 WorkGiver 守卫应用失败（已跳过，原版行为不受影响）", e);
            }
        }

        /// <summary>W63：落点改写的唯一入账钩子；运行期挂点，不计入特性补丁类。</summary>
        private static void TryPatchPlacementHook(Harmony harmony)
        {
            if (placementPatched)
            {
                return;
            }
            try
            {
                MethodInfo target = AccessTools.Method(typeof(Toils_Haul),
                    nameof(Toils_Haul.PlaceHauledThingInCell),
                    new[] { typeof(TargetIndex), typeof(Toil), typeof(bool), typeof(bool) });
                if (target == null)
                {
                    return;
                }
                MethodInfo postfix = AccessTools.Method(typeof(SDS_CellOnlyWorkGiverPatches),
                    nameof(PlaceHauledThingInCellPostfix));
                harmony.Patch(target, postfix: new HarmonyMethod(postfix)
                {
                    priority = (int)Priority.Low
                });
                placementPatched = true;
                PlacementHookApplied = true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("W63 落格入账挂点失败（已跳过，退让路径仍可用）", e);
            }
        }

        [HarmonyPriority(Priority.Low)]
        public static void PlaceHauledThingInCellPostfix(TargetIndex cellInd, ref Toil __result)
        {
            if (__result == null || __result.initAction == null)
            {
                return;
            }
            Toil toil = __result;
            Action original = toil.initAction;
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job job = actor != null && actor.jobs != null ? actor.jobs.curJob : null;
                if (job != null && SDS_CellOnlyDestinationFold.TryGetEndpoint(job, out ISDS_Port port))
                {
                    int accepted;
                    bool deposited = SDS_CellOnlyDestinationFold.TryDepositCarried(actor, port, out accepted);
                    SDS_CellOnlyDestinationFold.Clear(job);
                    if (deposited)
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Succeeded);
                        return;
                    }
                }
                original();
            };
        }

        private static MethodInfo FindDeclaredMethod(Type type, string name, Type[] parameters)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                MethodInfo method = t.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, parameters, null);
                if (method != null)
                {
                    return method;
                }
            }
            return null;
        }

        private static bool TryPatch(Harmony harmony, MethodInfo target, MethodInfo postfix,
            ref List<string> failures)
        {
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                return true;
            }
            catch (Exception e)
            {
                (failures ?? (failures = new List<string>())).Add(target.DeclaringType != null
                    ? target.DeclaringType.Name + "." + target.Name
                    : target.Name);
                SDS_Diag.ErrorOnce("落点格 WorkGiver 守卫挂点失败（已跳过）："
                    + (target.DeclaringType != null ? target.DeclaringType.FullName : "?")
                    + "." + target.Name, e);
                return false;
            }
        }

        [HarmonyPriority(Priority.Low)]
        public static void JobOnThingPostfix(Pawn __0, Thing __1, ref Job __result)
        {
            if (probing || __result == null)
            {
                return;
            }
            try
            {
                // W62/W63：优先改写目的地；无法解析落点才按 W63 退让。
                if (SDS_CellOnlyDestinationFold.TryRewrite(__0, __result, out bool endpointDestination))
                {
                    W63RewriteCount++;
                    return;
                }
                if (!endpointDestination)
                {
                    return;
                }
                W63FallbackJobCount++;
                JobFailReason.Is(HaulAIUtility.NoEmptyPlaceLowerTrans);
                __result = null;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("落点格 Job 退让（JobOnThing）失败（已忽略，原版行为不受影响）", e);
            }
        }

        [HarmonyPriority(Priority.Low)]
        public static void HasJobOnThingPostfix(WorkGiver __instance, Pawn __0, Thing __1, bool __2,
            ref bool __result)
        {
            if (!__result || probing || __instance == null)
            {
                return;
            }
            try
            {
                if (!SDS_CellOnlyJobShape.MayHoldContents(__1))
                {
                    return;
                }
                WorkGiver_Scanner scanner = __instance as WorkGiver_Scanner;
                if (scanner == null)
                {
                    return;
                }
                Job probe;
                probing = true;
                try
                {
                    probe = scanner.JobOnThing(__0, __1, __2);
                }
                finally
                {
                    probing = false;
                }
                if (probe == null)
                {
                    return;
                }
                if (SDS_CellOnlyDestinationFold.CanRewrite(__0, probe, out bool endpointDestination))
                {
                    W63ProbeCount++;
                    return;
                }
                if (endpointDestination)
                {
                    W63FallbackProbeCount++;
                    JobFailReason.Is(HaulAIUtility.NoEmptyPlaceLowerTrans);
                    __result = false;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("落点格 Job 退让（HasJobOnThing 探针）失败（已回落到原版）", e);
            }
        }
    }

    /// <summary>
    /// 静态构造兜底：Def 加载完成后再尝试一次运行期枚举挂点。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class SDS_CellOnlyWorkGiverBootstrap
    {
        static SDS_CellOnlyWorkGiverBootstrap()
        {
            SDS_CellOnlyWorkGiverPatches.Apply(SDS_PatchInstallerImpl.HarmonyInstance);
        }
    }
}
