using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 搜索层唯一排序器（《找得到》S3 / S4 / S12 / S14）：
    /// 排序键只写一次，所有适配器共用；稳定键用 thingIDNumber。
    /// </summary>
    internal static class SDS_SearchRanker
    {
        /// <summary>SourceThing（无优先级）：有效距离升序 → 稳定键升序。</summary>
        public static bool PreferSource(float candidateDist, int candidateId,
            float currentDist, int currentId, bool hasCurrent)
        {
            if (!hasCurrent)
            {
                return true;
            }
            if (candidateDist < currentDist)
            {
                return true;
            }
            if (candidateDist > currentDist)
            {
                return false;
            }
            return candidateId < currentId;
        }

        /// <summary>SourceThing（带 priorityGetter）：优先级降序 → 有效距离升序 → 稳定键升序。</summary>
        public static bool PreferPriority(float candidatePrio, float candidateDist, int candidateId,
            float currentPrio, float currentDist, int currentId, bool hasCurrent)
        {
            if (!hasCurrent)
            {
                return true;
            }
            if (candidatePrio > currentPrio)
            {
                return true;
            }
            if (candidatePrio < currentPrio)
            {
                return false;
            }
            return PreferSource(candidateDist, candidateId, currentDist, currentId, true);
        }

        /// <summary>
        /// SourceThing 候选池的完整排序键（S3 / S14）：
        /// priority（仅当存在 priorityGetter）→ 有效距离 → 镜像优先 → thingIDNumber。
        /// </summary>
        public static bool PreferMirrorCandidate(float candidatePrio, float candidateDist, int candidateId,
            bool candidateIsMirror, bool hasPriority,
            float currentPrio, float currentDist, int currentId, bool currentIsMirror, bool hasCurrent)
        {
            if (!hasCurrent)
            {
                return true;
            }
            if (hasPriority && candidatePrio != currentPrio)
            {
                return candidatePrio > currentPrio;
            }
            if (candidateDist != currentDist)
            {
                return candidateDist < currentDist;
            }
            if (candidateIsMirror != currentIsMirror)
            {
                return candidateIsMirror;
            }
            return candidateId < currentId;
        }

        /// <summary>镜像 vs 原版 best 的二选一（S12a / S12b）：距离相等时取镜像。</summary>
        public static bool PreferMirrorOnTie(float mirrorDist, float realDist)
        {
            return mirrorDist <= realDist;
        }

        /// <summary>
        /// Destination（端点 vs 原版目的地）：优先级降序 → 距离升序 → 平局取端点 → 稳定键。
        /// </summary>
        public static bool PreferDestination(int candidatePrio, float candidateDist, int candidateId,
            bool candidateIsEndpoint, int currentPrio, float currentDist, int currentId,
            bool currentIsEndpoint, bool hasCurrent)
        {
            if (!hasCurrent)
            {
                return true;
            }
            if (candidatePrio != currentPrio)
            {
                return candidatePrio > currentPrio;
            }
            if (candidateDist < currentDist)
            {
                return true;
            }
            if (candidateDist > currentDist)
            {
                return false;
            }
            if (candidateIsEndpoint != currentIsEndpoint)
            {
                return candidateIsEndpoint;
            }
            return candidateId < currentId;
        }
    }

    /// <summary>
    /// 搜索层唯一候选遍历 / 比较内核：两个 Global Prefix 适配器共用。
    /// 只负责「遍历 + 距离投影 + validator + 可达 + Ranker」，不创建 Job、不扣账。
    /// </summary>
    internal static class SDS_SearchCore
    {
        public static Thing FindGlobal(IntVec3 center, Map map, IEnumerable searchSet,
            PathEndMode peMode, TraverseParms traverseParams, float maxDistance,
            Predicate<Thing> validator, Func<Thing, float> priorityGetter,
            bool lookInHaulSources, bool canLookInHaulableSources, bool requireReach)
        {
            Thing chosen = null;
            float bestPrio = float.MinValue;
            float maxDistanceSquared = maxDistance * maxDistance;
            float closestDistSquared = 2.1474836E+09f;
            bool chosenIsMirror = false;
            int chosenId = int.MaxValue;
            bool careAboutHaulSourceEnabled = canLookInHaulableSources
                && traverseParams.pawn != null && traverseParams.pawn.IsColonist;
            bool readHaulSources = lookInHaulSources || canLookInHaulableSources;
            bool blockHiddenGrid = SDS_AccessRules.BlocksNonPlayer(traverseParams.pawn);

            if (searchSet is IList<Thing> list)
            {
                for (int i = 0; i < list.Count; i++) Process(list[i]);
            }
            else if (searchSet is IList<Pawn> list2)
            {
                for (int j = 0; j < list2.Count; j++) Process(list2[j]);
            }
            else if (searchSet is IList<Building> list3)
            {
                for (int k = 0; k < list3.Count; k++) Process(list3[k]);
            }
            else if (searchSet is IList<IAttackTarget> list4)
            {
                for (int l = 0; l < list4.Count; l++) Process((Thing)list4[l]);
            }
            else
            {
                foreach (Thing item in searchSet) Process(item);
            }
            return chosen;

            void Process(Thing t)
            {
                if (t == null)
                {
                    return;
                }
                if (requireReach ? !t.Spawned : !(t.Spawned || HaulAIUtility.IsInHaulableInventory(t)))
                {
                    return;
                }
                bool isMirror = SDS_MirrorRegistry.IsMirror(t);
                if (isMirror && blockHiddenGrid)
                {
                    return;
                }
                float dist = isMirror
                    ? SDS_AccessPointResolver.EffectiveDistanceSquared(center, t)
                    : (center - t.PositionHeld).LengthHorizontalSquared;
                if (dist > maxDistanceSquared || (priorityGetter == null && dist > closestDistSquared))
                {
                    return;
                }
                Validate(t, dist, isMirror);
                if (readHaulSources && !blockHiddenGrid && t is IHaulSource haulSource
                    && (!careAboutHaulSourceEnabled || haulSource.HaulSourceEnabled))
                {
                    ThingOwner held = haulSource.GetDirectlyHeldThings();
                    for (int m = 0; m < held.Count; m++)
                    {
                        Validate(held[m], dist, false);
                    }
                }
            }

            void Validate(Thing t, float dist, bool isMirror)
            {
                if (requireReach
                    && !map.reachability.CanReach(center, t.SpawnedParentOrMe, peMode, traverseParams))
                {
                    return;
                }
                if (validator != null && !validator(t))
                {
                    return;
                }
                float prio = priorityGetter != null ? priorityGetter(t) : 0f;
                if (SDS_SearchRanker.PreferMirrorCandidate(
                        prio, dist, t.thingIDNumber, isMirror, priorityGetter != null,
                        bestPrio, closestDistSquared, chosenId, chosenIsMirror, chosen != null))
                {
                    chosen = t;
                    closestDistSquared = dist;
                    bestPrio = prio;
                    chosenIsMirror = isMirror;
                    chosenId = t.thingIDNumber;
                }
            }
        }

        /// <summary>
        /// 两个 Postfix 适配器共用的候选收集：custom set / singleDef / 全图镜像。
        /// 禁用品类不进入任何搜索集合。
        /// </summary>
        public static void CollectMirrorCandidates(Map map, ThingRequest thingReq,
            IEnumerable<Thing> customGlobalSearchSet, List<Thing> outList)
        {
            CollectMirrorCandidatesRaw(map, thingReq, customGlobalSearchSet, outList);
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null)
            {
                return;
            }
            for (int i = outList.Count - 1; i >= 0; i--)
            {
                if (SDS_Rules.TryGetStockKey(outList[i], out SDS_StockKey key)
                    && key != null && ledger.IsForbidden(key))
                {
                    outList.RemoveAt(i);
                }
            }
        }

        private static void CollectMirrorCandidatesRaw(Map map, ThingRequest thingReq,
            IEnumerable<Thing> customGlobalSearchSet, List<Thing> outList)
        {
            outList.Clear();
            if (customGlobalSearchSet != null)
            {
                foreach (Thing t in customGlobalSearchSet)
                {
                    if (t != null && t.Spawned && t.Map == map && SDS_MirrorRegistry.IsMirror(t)
                        && !outList.Contains(t))
                    {
                        outList.Add(t);
                    }
                }
                return;
            }
            if (thingReq.singleDef != null)
            {
                SDS_MirrorService.CopyMirrorsForDef(map, thingReq.singleDef, outList);
                return;
            }
            SDS_MirrorService.CopyMirrorsOn(map, outList);
        }

        /// <summary>两个 Postfix 适配器共用的唯一最佳镜像选择。</summary>
        public static Thing FindBestMirror(List<Thing> candidates, IntVec3 root, ThingRequest thingReq,
            Predicate<Thing> validator, Func<Thing, float> priorityGetter,
            out float bestPrio, out float bestDist)
        {
            Thing best = null;
            bestPrio = float.MinValue;
            bestDist = float.MaxValue;
            int bestId = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing t = candidates[i];
                if (t == null || !t.Spawned || !SDS_MirrorRegistry.IsMirror(t)
                    || !thingReq.Accepts(t))
                {
                    continue;
                }
                if (validator != null && !validator(t))
                {
                    continue;
                }
                float prio = priorityGetter != null ? priorityGetter(t) : 0f;
                float dist = SDS_AccessPointResolver.EffectiveDistanceSquared(root, t);
                int id = t.thingIDNumber;
                bool better = priorityGetter != null
                    ? SDS_SearchRanker.PreferPriority(prio, dist, id, bestPrio, bestDist, bestId, best != null)
                    : SDS_SearchRanker.PreferSource(dist, id, bestDist, bestId, best != null);
                if (better)
                {
                    best = t;
                    bestPrio = prio;
                    bestDist = dist;
                    bestId = id;
                }
            }
            return best;
        }

        /// <summary>区域 / 全局搜索出口的「镜像补漏」唯一主体。返回是否替换了 result。</summary>
        public static bool TryReplaceWithMirror(IntVec3 root, Map map, ThingRequest thingReq,
            TraverseParms traverseParams, float maxDistance, Predicate<Thing> validator,
            Func<Thing, float> priorityGetter, IEnumerable<Thing> customGlobalSearchSet,
            List<Thing> candidates, ref Thing result)
        {
            if (map == null || !SDS_MirrorService.AnyMirrorInWorld
                || !SDS_MirrorService.HasAnyMirrorOn(map))
            {
                return false;
            }
            // S18：非玩家派系不补隐藏库存候选；未知 actor 保持原版口径。
            if (SDS_AccessRules.BlocksNonPlayer(traverseParams.pawn))
            {
                return false;
            }
            if (thingReq.IsUndefined || thingReq.group == ThingRequestGroup.Nothing
                || thingReq.group == ThingRequestGroup.Everything || !root.InBounds(map))
            {
                return false;
            }
            // S15：偷窃组不补镜像候选。
            if (thingReq.group == ThingRequestGroup.HaulableEverOrMinifiable)
            {
                return false;
            }
            CollectMirrorCandidates(map, thingReq, customGlobalSearchSet, candidates);
            Thing best = FindBestMirror(candidates, root, thingReq, validator, priorityGetter,
                out float bestPrio, out float bestDist);
            if (best == null)
            {
                return false;
            }
            if (!SDS_AccessPointResolver.CanReachViaAccess(root, map, traverseParams))
            {
                return false;
            }
            if (bestDist > maxDistance * maxDistance)
            {
                return false;
            }
            if (result == null)
            {
                result = best;
                return true;
            }
            float originalPrio = priorityGetter != null ? priorityGetter(result) : 0f;
            if (bestPrio > originalPrio)
            {
                result = best;
                return true;
            }
            if (bestPrio < originalPrio)
            {
                return false;
            }
            float originalDist = SDS_MirrorRegistry.IsMirror(result)
                ? SDS_AccessPointResolver.EffectiveDistanceSquared(root, result)
                : (root - result.PositionHeld).LengthHorizontalSquared;
            if (!SDS_SearchRanker.PreferMirrorOnTie(bestDist, originalDist))
            {
                return false;
            }
            result = best;
            return true;
        }
    }

    /// <summary>GenClosest.ClosestThing_Global：只按距离选最近（不检查可达）。</summary>
    [HarmonyPatch(typeof(GenClosest), nameof(GenClosest.ClosestThing_Global))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_ClosestGlobal
    {
        public static bool Prefix(IntVec3 center, IEnumerable searchSet, float maxDistance,
            Predicate<Thing> validator, Func<Thing, float> priorityGetter, bool lookInHaulSources,
            ref Thing __result)
        {
            if (searchSet == null || !SDS_MirrorService.AnyMirrorInWorld)
            {
                return true;
            }
            try
            {
                __result = SDS_SearchCore.FindGlobal(center, null, searchSet, PathEndMode.Touch, default,
                    maxDistance, validator, priorityGetter, lookInHaulSources,
                    canLookInHaulableSources: false, requireReach: false);
                return false;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("GenClosest.ClosestThing_Global 镜像距离重算失败（已放行原版搜索）", e);
                return true;
            }
        }
    }

    /// <summary>GenClosest.ClosestThing_Global_Reachable：距离按访问点；可达性交给 Reachability 补丁。</summary>
    [HarmonyPatch(typeof(GenClosest), nameof(GenClosest.ClosestThing_Global_Reachable))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_ClosestGlobalReachable
    {
        public static bool Prefix(IntVec3 center, Map map, IEnumerable<Thing> searchSet, PathEndMode peMode,
            TraverseParms traverseParams, float maxDistance, Predicate<Thing> validator,
            Func<Thing, float> priorityGetter, bool canLookInHaulableSources, ref Thing __result)
        {
            if (map == null || searchSet == null || !SDS_MirrorService.AnyMirrorInWorld)
            {
                return true;
            }
            try
            {
                __result = SDS_SearchCore.FindGlobal(center, map, searchSet, peMode, traverseParams,
                    maxDistance, validator, priorityGetter, lookInHaulSources: false,
                    canLookInHaulableSources: canLookInHaulableSources, requireReach: true);
                return false;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("GenClosest.ClosestThing_Global_Reachable 镜像距离重算失败（已放行原版搜索）", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(GenClosest), nameof(GenClosest.ClosestThingReachable))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_ClosestReachable
    {
        private static readonly List<Thing> tmpMirrors = new List<Thing>();

        public static void Postfix(IntVec3 root, Map map, ThingRequest thingReq, PathEndMode peMode,
            TraverseParms traverseParams, float maxDistance, Predicate<Thing> validator,
            IEnumerable<Thing> customGlobalSearchSet, ref Thing __result)
        {
            try
            {
                SDS_SearchCore.TryReplaceWithMirror(root, map, thingReq, traverseParams, maxDistance,
                    validator, priorityGetter: null, customGlobalSearchSet, tmpMirrors, ref __result);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("GenClosest 镜像补漏失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>带 priorityGetter 的区域优先搜索：镜像候选与原点候选按「优先级 → 有效距离」比较。</summary>
    [HarmonyPatch(typeof(GenClosest), nameof(GenClosest.ClosestThing_Regionwise_ReachablePrioritized))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_ClosestRegionwisePrioritized
    {
        private static readonly List<Thing> tmpMirrors = new List<Thing>();

        public static void Postfix(IntVec3 root, Map map, ThingRequest thingReq, PathEndMode peMode,
            TraverseParms traverseParams, float maxDistance, Predicate<Thing> validator,
            Func<Thing, float> priorityGetter, ref Thing __result)
        {
            try
            {
                SDS_SearchCore.TryReplaceWithMirror(root, map, thingReq, traverseParams, maxDistance,
                    validator, priorityGetter, customGlobalSearchSet: null, tmpMirrors, ref __result);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("GenClosest 优先搜索镜像补漏失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>规则 S15：镜像 / 金库不进入 NPC 的偷窃价值计算。</summary>
    [HarmonyPatch(typeof(StealAIUtility), nameof(StealAIUtility.GetValue))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_StealValueIgnoresHiddenGrid
    {
        public static void Postfix(Thing thing, ref float __result)
        {
            try
            {
                if (__result <= 0f)
                {
                    return;
                }
                if (SDS_MirrorRegistry.IsMirror(thing) || Building_SDS_Vault.IsVault(thing))
                {
                    __result = 0f;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("偷窃价值屏蔽镜像失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>镜像的 CanReach = 「任一访问点可达」。</summary>
    [HarmonyPatch(typeof(Reachability), nameof(Reachability.CanReach),
        new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_ReachabilityMirror
    {
        public static bool Prefix(IntVec3 start, LocalTargetInfo dest, PathEndMode peMode,
            TraverseParms traverseParams, Map ___map, ref bool __result)
        {
            if (!dest.HasThing || !SDS_MirrorRegistry.IsMirror(dest.Thing))
            {
                return true;
            }
            try
            {
                // S18：非玩家派系把隐藏库存视为不可达。
                if (SDS_AccessRules.BlocksNonPlayer(traverseParams.pawn))
                {
                    __result = false;
                    return false;
                }
                __result = SDS_AccessPointResolver.CanReachViaAccess(start, ___map, traverseParams);
                return false;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("Reachability.CanReach 镜像可达性判定失败（已放行原版寻路）", e);
                return true;
            }
        }
    }
}
