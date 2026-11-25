using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SuperDimensionalStorage
{

    // ============================================================================
    // 来源：Pick Up And Haul（PUAH）
    // ============================================================================

    /// <summary>
    /// 与 PickUpAndHaul（PUAH）的兼容。
    ///
    /// 架构（与《设计原则.md》准则三一致：规则定义一次 + 所有派活入口调用同一个服务）：
    ///  - `HasJobOnThing`（候选闸门）+ `JobOnThing`（派活出口）：端点胜出时整单替换为本 Mod 的多件存入 Job，
    ///    PUAH 的 `HaulToInventory` / `CompHauledToInventory` 不参与这次端点行程；不赢则原样保留 PUAH 的结果。
    ///  - **卸载派活出口**（`PawnUnloadChecker.CheckIfPawnShouldUnloadInventory`）：这是 PUAH 自己的派发点。
    ///    能就地存 → 我们入队 `SDS_DepositToGrid` 并跳过 PUAH 的入队；不能 → 原样交给 PUAH（含它自己的时机 / 自愈逻辑）。
    ///
    /// 为什么卸载必须单独接管：PUAH 的卸载 Job 创建时**没有目的地**，由它自己的驱动在执行期用
    /// `StoreUtility.TryFindBestBetterStorageFor(currentPriority = Unstored)` 现选 —— 而端点就是
    /// `IHaulDestination`，必然被选中 → 带芯片小人抱着货走到建筑端点。
    ///
    /// 边界（有意不接管）：PUAH 取货驱动内部 `EnqueueFirst(UnloadYourHauledInventory, storeCell)` 那趟没有
    /// 任何派发 API 可挂。按方案 B **不碰已构造的 Job**（`Pawn_JobTracker.StartJob` 改 job.def 属于执行边界），
    /// 该趟交回 PUAH；它只在我们没有接管取货行程的异常情形出现，此时小人已站在那个端点旁，额外位移≈0。
    /// </summary>
    internal static class SDS_ModCompat_PUAH
    {
        private const string WorkGiverTypeName = "PickUpAndHaul.WorkGiver_HaulToInventory";
        private const string HauledCompTypeName = "PickUpAndHaul.CompHauledToInventory";
        private const string UnloadCheckerTypeName = "PickUpAndHaul.PawnUnloadChecker";
        private const string UnloadCheckMethodName = "CheckIfPawnShouldUnloadInventory";

        public static void TryApply(Harmony harmony)
        {
            bool detected = false;
            try
            {
                if (harmony == null)
                {
                    return;
                }
                Type giver = AccessTools.TypeByName(WorkGiverTypeName);
                if (giver == null)
                {
                    // 未安装 → 汇总行「未检测到」；装了但结构变了 → 汇总行「已检测到，0/0」。
                    detected = AccessTools.TypeByName(HauledCompTypeName) != null
                        || AccessTools.TypeByName(UnloadCheckerTypeName) != null;
                    SDS_CompatReportHub.Register("Pick Up And Haul", "Mehni.PickUpAndHaul", detected, null,
                        null, detected ? "找不到 WorkGiver_HaulToInventory（版本 / 命名变化？）" : null);
                    return;
                }
                detected = true;
                SDS_CompatPatchReport report = new SDS_CompatPatchReport("Pick Up And Haul");
                Type[] signature = { typeof(Pawn), typeof(Thing), typeof(bool) };

                Patch(report, harmony, "WorkGiver_HaulToInventory.HasJobOnThing（候选闸门）",
                    AccessTools.Method(giver, "HasJobOnThing", signature),
                    null, nameof(HasJobOnThingPostfix),
                    "找不到方法 HasJobOnThing(Pawn,Thing,bool)");

                Patch(report, harmony, "WorkGiver_HaulToInventory.JobOnThing（派活替换）",
                    AccessTools.Method(giver, "JobOnThing", signature),
                    null, nameof(JobOnThingPostfix),
                    "找不到方法 JobOnThing(Pawn,Thing,bool)");

                Type unloadChecker = AccessTools.TypeByName(UnloadCheckerTypeName);
                Patch(report, harmony, "PawnUnloadChecker.CheckIfPawnShouldUnloadInventory（卸载派活）",
                    unloadChecker != null
                        ? AccessTools.Method(unloadChecker, UnloadCheckMethodName, new[] { typeof(Pawn), typeof(bool) })
                        : null,
                    nameof(UnloadDispatchPrefix), null,
                    unloadChecker == null
                        ? "找不到类型 " + UnloadCheckerTypeName
                        : "找不到方法 " + UnloadCheckMethodName + "(Pawn,bool)");

                SDS_CompatReportHub.Register("Pick Up And Haul", "Mehni.PickUpAndHaul", true, report);
            }
            catch (Exception e)
            {
                SDS_CompatReportHub.Register("Pick Up And Haul", "Mehni.PickUpAndHaul", detected, null,
                    null, "应用异常 " + e.GetType().Name);
                SDS_Diag.ErrorOnce("PickUpAndHaul 兼容补丁应用失败（已跳过，原版行为不受影响）", e);
            }
        }

        /// <summary>
        /// 挂一个挂点并登记到报告：目标方法找不到 / 应用异常一律算“失效”，绝不静默。
        /// 与本体 `ApplyHarmonyPatches` 的「已生效 / 应生效」口径一致。
        ///
        /// 2026-10-27：应用逻辑收口到 <see cref="SDS_CompatTools.TryPatch"/>（优先级同为 Low），
        /// 本方法只保留 PUAH 的挂点名 / 缺失原因，不再自带第二份 HarmonyMethod 构造。
        /// </summary>
        private static void Patch(SDS_CompatPatchReport report, Harmony harmony, string hook,
            MethodInfo target, string prefixName, string postfixName, string missingReason)
        {
            SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_PUAH), hook, target,
                prefixName, postfixName, missingReason);
        }

        /// <summary>
        /// PUAH 的候选闸门：本 Mod 端点能接这件货时放行候选，
        /// 否则逐字节维持 PUAH 自己的判定。
        /// </summary>
        public static void HasJobOnThingPostfix(Pawn pawn, Thing thing, bool forced, ref bool __result)
        {
            try
            {
                if (thing == null)
                {
                    return;
                }
                // W54：镜像是账本本身，不是可搬实物；PUAH 的背包链路不经过端口，
                // 一旦放行会在原地把隐藏金库的真身物化进背包（隔空取货）。
                if (SDS_API.IsMirror(thing))
                {
                    __result = false;
                    return;
                }
                // “在途待消费的料”绝不交给 PUAH / 自动搬运。
                if (SDS_API.IsProducedPendingConsumption(thing))
                {
                    __result = false;
                    return;
                }
                if (__result)
                {
                    return;
                }
                if (!SDS_API.TryTakeDepositCandidate(pawn, thing, forced, out _))
                {
                    return;
                }
                __result = true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("PickUpAndHaul 兼容（HasJobOnThing）失败（已忽略，PUAH 行为不受影响）", e);
            }
        }

        /// <summary>
        /// PUAH 的派活出口：端点胜出时整单替换为本 Mod 的多件存入 Job；
        /// 否则保留 PUAH 的原结果。
        /// </summary>
        public static void JobOnThingPostfix(Pawn pawn, Thing thing, bool forced, ref Job __result)
        {
            try
            {
                if (thing == null)
                {
                    return;
                }
                // W54：镜像不进入 PUAH 的搬运 Job（候选闸门已拒绝；这里防缓存 / 旧 Job 残留）。
                if (SDS_API.IsMirror(thing))
                {
                    __result = null;
                    return;
                }
                if (SDS_API.IsProducedPendingConsumption(thing))
                {
                    __result = null;
                    return;
                }
                // PUAH 驱动内部的“再收一件”腿：必须保持 PUAH 的返回语义，
                // 否则会让它的状态机把当前卸载腿跳过。
                if (InPuahCarryLoop(pawn))
                {
                    return;
                }
                if (SDS_API.IsDepositJob(__result))
                {
                    return;
                }
                // HasJobOnThing 已经做过一次完整决议并写入缓存；JobOnThing 直接消费该端点，
                // 不再重复 TryMake 决议。缓存缺失（原版没走 HasJob）才退回一次决议。
                if (SDS_API.TryMakeDepositJob(pawn, thing, forced, __result, true,
                        out Job job, out _))
                {
                    __result = job;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("PickUpAndHaul 兼容（JobOnThing）失败（已忽略，PUAH 行为不受影响）", e);
            }
        }

        /// <summary>
        /// PUAH 卸载派活出口：`PawnUnloadChecker.CheckIfPawnShouldUnloadInventory` 是 PUAH 自己的派发点
        /// （闲置 / `HaulToCell` 结束 / 丢弃未用物品三处会调它）。
        ///  - 统一决议能就地存 → 我们入队 `SDS_DepositToGrid`，并 `return false` 跳过 PUAH 的入队（它就不会再选建筑端点）；
        ///  - 决议让出（端点不赢 / 没有可存的货）→ `return true`，PUAH 原样执行（连它自己的自愈分支都不改）。
        /// 「什么时候卸载」仍完全由 PUAH 的调用点决定，我们只换“卸到哪”。
        /// </summary>
        public static bool UnloadDispatchPrefix(Pawn pawn, bool forced)
        {
            try
            {
                if (pawn == null || pawn.jobs == null || pawn.jobs.jobQueue == null)
                {
                    return true;
                }
                if (!SDS_API.IsPawnPortOperational(pawn))
                {
                    return true;
                }
                List<Thing> hauled;
                if (!TryGetHauledThings(pawn, out hauled))
                {
                    return true;
                }
                if (!SDS_API.TryMakeDepositJobFromInventory(pawn, hauled, out Job job, out _))
                {
                    // 统一决议让出：PUAH 自己派（它会照原样决定目的地）。
                    return true;
                }
                // 与 PUAH 自己的入队口径一致：预留成功才入队，失败就交回它自己处理。
                if (!job.TryMakePreToilReservations(pawn, false))
                {
                    return true;
                }
                pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
                return false;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("PickUpAndHaul 兼容（卸载派活）失败（已忽略，PUAH 行为不受影响）", e);
                return true;
            }
        }

        /// <summary>
        /// PUAH 记在 `CompHauledToInventory` 里的「这趟搬进背包」的实物（只读快照）。
        /// W54：镜像入包已被拒绝；旧 Job / 旧缓存里若仍登记镜像引用，按品类键映射回背包真身，
        /// 否则统一卸货决议会因 holdingOwner 不匹配永远让出，PUAH 自己把货送到核心 / 容器。
        /// </summary>
        private static bool TryGetHauledThings(Pawn pawn, out List<Thing> hauled)
        {
            hauled = null;
            if (pawn == null || pawn.AllComps == null || pawn.AllComps.Count == 0)
            {
                return false;
            }
            if (!hauledLookupDone)
            {
                hauledLookupDone = true;
                hauledCompType = AccessTools.TypeByName(HauledCompTypeName);
                hauledSetGetter = hauledCompType != null
                    ? AccessTools.Method(hauledCompType, "GetHashSet")
                    : null;
            }
            if (hauledCompType == null || hauledSetGetter == null)
            {
                return false;
            }
            for (int i = 0; i < pawn.AllComps.Count; i++)
            {
                ThingComp comp = pawn.AllComps[i];
                if (comp == null || !hauledCompType.IsInstanceOfType(comp))
                {
                    continue;
                }
                if (!(hauledSetGetter.Invoke(comp, null) is IEnumerable<Thing> set))
                {
                    return false;
                }
                hauled = new List<Thing>(set);
                return MapMirrorsToInventory(pawn, hauled);
            }
            return false;
        }

        /// <summary>W54：把登记表里的镜像引用按品类键换成背包里的真身；换不到就丢弃该条。</summary>
        private static bool MapMirrorsToInventory(Pawn pawn, List<Thing> hauled)
        {
            if (hauled == null || hauled.Count == 0)
            {
                return false;
            }
            ThingOwner inventory = pawn != null && pawn.inventory != null
                ? pawn.inventory.innerContainer
                : null;
            if (inventory == null)
            {
                return false;
            }
            List<Thing> mapped = new List<Thing>(hauled.Count);
            HashSet<Thing> used = new HashSet<Thing>();
            for (int i = 0; i < hauled.Count; i++)
            {
                Thing item = hauled[i];
                if (item == null)
                {
                    continue;
                }
                if (!SDS_API.IsMirror(item))
                {
                    mapped.Add(item);
                    continue;
                }
                Thing real = FindInventoryMatch(item, inventory, used);
                if (real != null)
                {
                    used.Add(real);
                    mapped.Add(real);
                }
            }
            hauled.Clear();
            hauled.AddRange(mapped);
            return hauled.Count > 0;
        }

        private static Thing FindInventoryMatch(Thing mirror, ThingOwner inventory, HashSet<Thing> used)
        {
            SDS_StockKey mirrorKey;
            if (!SDS_API.TryGetStockKey(mirror, out mirrorKey) || mirrorKey == null)
            {
                return null;
            }
            for (int i = 0; i < inventory.Count; i++)
            {
                Thing candidate = inventory[i];
                if (candidate == null || candidate.Destroyed || used.Contains(candidate))
                {
                    continue;
                }
                SDS_StockKey candidateKey;
                if (SDS_API.TryGetStockKey(candidate, out candidateKey) && candidateKey != null
                    && candidateKey.SameAs(mirrorKey))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static bool hauledLookupDone;
        private static Type hauledCompType;
        private static MethodInfo hauledSetGetter;

        private static bool InPuahCarryLoop(Pawn pawn)
        {
            Job job = pawn != null && pawn.jobs != null ? pawn.jobs.curJob : null;
            if (job == null || job.def == null)
            {
                return false;
            }
            return job.def.defName == "HaulToInventory";
        }
    }
}
