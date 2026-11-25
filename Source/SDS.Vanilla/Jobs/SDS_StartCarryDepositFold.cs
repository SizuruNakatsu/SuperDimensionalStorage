using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// StartCarryThing 的目标已经在搬运者手上时，原版会因 AvailableStackSpace == 0 抛异常。
    /// 本 Mod 的"从端点取到手上 → 交回原版"链路会命中；这里视为"已经拿到"，直接完成该 toil。
    /// 同时承载 W62：cell-only 自身端口折叠——拿到手后立刻入账，不再走向落点格。
    /// </summary>
    [HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.StartCarryThing),
        new[] { typeof(TargetIndex), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_StartCarryAlreadyCarried
    {
        public static void Postfix(TargetIndex haulableInd, ref Toil __result)
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
                Thing target = job != null ? job.GetTarget(haulableInd).Thing : null;
                bool alreadyCarried = target != null && actor.carryTracker != null
                    && ReferenceEquals(actor.carryTracker.CarriedThing, target);
                if (!alreadyCarried)
                {
                    original();
                }
                // W62：cell-only 自身端口折叠——拿到手后立刻入账，不再走向落点格。
                if (job != null && SDS_CellOnlyDestinationFold.TryGetSelf(job, out ISDS_Port port))
                {
                    int accepted;
                    bool deposited = SDS_CellOnlyDestinationFold.TryDepositCarried(actor, port, out accepted);
                    SDS_CellOnlyDestinationFold.Clear(job);
                    if (deposited)
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Succeeded);
                    }
                    else
                    {
                        // 自存失败：落点改为当前格，交回原版就地落地，不搬回改写时的旧格。
                        job.targetC = new LocalTargetInfo(actor.PositionHeld);
                    }
                }
            };
        }
    }
}
