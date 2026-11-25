using HarmonyLib;
using System;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    // ════════════════════════════════════════════════════════════════════════════════════════
    // W61：镜像预留不排他
    //
    // 镜像一个 Thing 代表整个品类；原版 ReservationManager 默认 maxPawns = 1，
    // 任何按“这个物体”预留的 Job 都会把同类别其它 Job 全部锁死（多炮塔加油 / 多工作台同料）。
    // 规则：镜像目标的 CanReserve 一律放行（忽略 maxPawns / 其它预留），原版 Reserve
    //       随后按正常路径登记；CanReserveStack 返回镜像当前堆量。IsReserved / ReservedBy
    //       仍为真，改动只影响“能不能再预留”，不影响可观察性。真正的互斥由账本事务
    //       （W22 / W56）保证；非镜像路径零行为变化。
    // G 关闭（SDS_Rules.Available 为假）时镜像不可预留：候选在 CanReserve 阶段就被过滤，
    // 不允许“派活成功、取货才失败”造成 Job 死循环（核心断电 / 卸载窗口）。
    // ════════════════════════════════════════════════════════════════════════════════════════

    internal static class SDS_MirrorReservation
    {
        // RULE:W61
        internal static bool IsMirrorTarget(LocalTargetInfo target)
        {
            Thing thing = target.Thing;
            return thing != null && !thing.Destroyed && SDS_MirrorRegistry.IsMirror(thing);
        }
    }

    [HarmonyPatch(typeof(ReservationManager), nameof(ReservationManager.CanReserve))]
    internal static class Patch_SDS_MirrorReserveNonExclusiveCanReserve
    {
        public static bool Prefix(Pawn claimant, LocalTargetInfo target, ref bool __result)
        {
            try
            {
                if (!SDS_MirrorReservation.IsMirrorTarget(target))
                {
                    return true;
                }
                if (!SDS_Rules.Available)
                {
                    __result = false;
                    return false;
                }
                __result = claimant != null && claimant.Spawned
                    && claimant.MapHeld != null
                    && target.Thing.MapHeld == claimant.MapHeld;
                return false;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("镜像预留放行（CanReserve）失败（已交回原版判定）", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(ReservationManager), nameof(ReservationManager.CanReserveStack))]
    internal static class Patch_SDS_MirrorReserveNonExclusiveCanReserveStack
    {
        public static bool Prefix(LocalTargetInfo target, ref int __result)
        {
            try
            {
                if (!SDS_MirrorReservation.IsMirrorTarget(target))
                {
                    return true;
                }
                if (!SDS_Rules.Available)
                {
                    __result = 0;
                    return false;
                }
                __result = Math.Max(0, target.Thing.stackCount);
                return false;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("镜像预留放行（CanReserveStack）失败（已交回原版判定）", e);
                return true;
            }
        }
    }
}
