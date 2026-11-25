using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    // ════════════════════════════════════════════════════════════════════════════════════════
    // W58：数量缺失的唯一补齐点
    //
    // 原版 Toils_Haul.StartCarryThing / TakeToInventory 在取货前都会调用
    // Toils_Haul.ErrorCheckForCarry；当 job.count <= 0 时它会把 count 夹成 1 并打日志。
    // 对镜像目标来说，1 件是“原版兜底”而不是调用方意图。本补丁在夹取之后按该 Job
    // 对镜像的 Reservation.StackCount（-1 = 整堆）恢复意图数量；无预留时保持 1。
    //
    // 只读预留、不写账、不预留；恢复后的数量仍由原版 min(count, 携带量, 镜像量) 与
    // W56（SDS_Availability.ClampTakeToAvailable）夹取。非镜像目标零行为变化。
    // ════════════════════════════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.ErrorCheckForCarry))]
    internal static class Patch_SDS_HaulCountIntent
    {
        /// <summary>记录调用前的 job.count；int.MinValue = 未缺失（不需要补齐）。</summary>
        public static void Prefix(Pawn pawn, out int __state)
        {
            __state = int.MinValue;
            try
            {
                Job job = pawn != null && pawn.jobs != null ? pawn.jobs.curJob : null;
                if (job != null && job.count <= 0)
                {
                    __state = job.count;
                }
            }
            catch (Exception e)
            {
                __state = int.MinValue;
                SDS_Diag.ErrorOnce("取量意图记录失败（已交回原版 1 件兜底）", e);
            }
        }

        public static void Postfix(Pawn pawn, Thing haulThing, int __state, ref bool __result)
        {
            try
            {
                if (__result || __state == int.MinValue)
                {
                    return;
                }
                if (pawn == null || pawn.jobs == null || haulThing == null
                    || !SDS_MirrorRegistry.IsMirror(haulThing))
                {
                    return;
                }
                Job job = pawn.jobs.curJob;
                if (job == null || !ReferenceEquals(job.GetTarget(TargetIndex.A).Thing, haulThing)
                    && !ContainsThing(job, haulThing))
                {
                    return;
                }
                int intent = ResolveIntent(pawn, job, haulThing);
                if (intent <= 0)
                {
                    return;
                }
                if (!SDS_Rules.TryGetStockKey(haulThing, out SDS_StockKey key) || key == null)
                {
                    return;
                }
                long available = SDS_Availability.GetAvailableCount(key);
                int want = SDS_Availability.ClampTakeToAvailable(
                    Math.Min(intent, haulThing.stackCount),
                    available > int.MaxValue ? int.MaxValue : available);
                if (want > 0)
                {
                    job.count = want;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("取量意图补齐失败（已保留原版 1 件兜底）", e);
            }
        }

        /// <summary>
        /// 该 Job 对镜像的预留意图：StackCount == -1 = 整堆；>0 = 明示意图；无预留 = 0（不补齐）。
        /// </summary>
        // RULE:W58
        private static int ResolveIntent(Pawn pawn, Job job, Thing mirror)
        {
            Map map = pawn.MapHeld;
            if (map == null)
            {
                return 0;
            }
            ReservationManager manager = map.reservationManager;
            if (manager == null)
            {
                return 0;
            }
            List<ReservationManager.Reservation> reservations = manager.ReservationsReadOnly;
            for (int i = 0; i < reservations.Count; i++)
            {
                ReservationManager.Reservation reservation = reservations[i];
                if (reservation == null || !ReferenceEquals(reservation.Claimant, pawn)
                    || !ReferenceEquals(reservation.Job, job)
                    || !ReferenceEquals(reservation.Target.Thing, mirror))
                {
                    continue;
                }
                return reservation.StackCount == -1 ? mirror.stackCount : reservation.StackCount;
            }
            return 0;
        }

        private static bool ContainsThing(Job job, Thing thing)
        {
            if (job == null || thing == null)
            {
                return false;
            }
            if (ReferenceEquals(job.targetA.Thing, thing) || ReferenceEquals(job.targetB.Thing, thing)
                || ReferenceEquals(job.targetC.Thing, thing))
            {
                return true;
            }
            return ContainsInQueue(job.targetQueueA, thing) || ContainsInQueue(job.targetQueueB, thing);
        }

        private static bool ContainsInQueue(List<LocalTargetInfo> queue, Thing thing)
        {
            if (queue == null)
            {
                return false;
            }
            for (int i = 0; i < queue.Count; i++)
            {
                if (ReferenceEquals(queue[i].Thing, thing))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
