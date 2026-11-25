using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SuperDimensionalStorage
{

    // ============================ 装载取货 Job ============================

    /// <summary>
    /// 运输舱 / 传送门装载的取货 Job（唯一保留的专用 Job 执行适配器）。
    ///
    /// 流程：预留超维空间库存 → 走到最近端点（核心 / 锚点 / 带芯片小人 / 机械体）
    /// → 把货取到手上（carryTracker）→ 交回原版 HaulToTransporter / HaulToPortal，
    /// 由原版 toil 带着货走到运输舱 / 传送门并投进去。全程不落地、表现与原版搬运一致。
    /// </summary>
    public class JobDriver_SDS_WithdrawFromGrid : JobDriver
    {
        internal const TargetIndex WithdrawPointInd = TargetIndex.A;
        internal const TargetIndex WorkTargetInd = TargetIndex.B;

        /// <summary>取物点是不是「小人自身端口」（植入体芯片）：自身端口无需寻路 / 预留 / 消失判定。</summary>
        private bool IsSelfWithdrawPoint()
        {
            if (job == null || pawn == null)
            {
                return false;
            }
            Thing point = job.GetTarget(WithdrawPointInd).Thing;
            return point != null && ReferenceEquals(point, pawn);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 手上若还拿着别的东西，先放下（与原版换活时的表现一致）。
            if (pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null)
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
            }
            // 自身端口（植入体）不需要预留自己；人端口（另一个带芯片的小人 / 机械体）也不做常规预留。
            Thing pointThing = job.targetA.Thing;
            if (!IsSelfWithdrawPoint() && !(pointThing is Pawn))
            {
                if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
                {
                    SDS_Diag.Fail("取货/取物点不可预留", SDS_Diag.Ctx(pawn.Map, pawn, pointThing, job));
                    // MakeNewToils（含 AddFinishAction）还没被调用，必须在这里显式释放计划。
                    SDS_TransportVirtual.ForgetPendingLoadPlan(job.loadID);
                    return false;
                }
            }
            // 运输舱 / 传送门装载：不预留容器本体，与原版 JobDriver_HaulToTransporter 完全一致。
            return true;
        }

        /// <summary>对称释放原版 enroute（「材料在路上」）账本，避免残留认领挡住后续派活。</summary>
        private void ReleaseEnrouteClaim()
        {
            if (pawn == null || pawn.Map == null)
            {
                return;
            }
            Thing destination = job.GetTarget(WorkTargetInd).Thing;
            if (destination == null || destination.Destroyed)
            {
                return;
            }
            if (destination is IHaulEnroute enroute)
            {
                pawn.Map.enrouteManager.ReleaseFor(enroute, pawn);
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Job 无论以何种方式结束都释放预留（正常物化时已释放过一次，重复释放无副作用）。
            AddFinishAction(delegate
            {
                SDS_Rules.ReleaseJobReservation(job.loadID);
                SDS_TransportVirtual.ForgetPendingLoadPlan(job.loadID);
                ReleaseEnrouteClaim();
            });

            this.FailOnIncapable(PawnCapacityDefOf.Manipulation);
            if (!IsSelfWithdrawPoint())
            {
                if (job.targetA.Thing is Pawn)
                {
                    // 人端口（另一个小人 / 机械体）：只判消失，不判 forbidden（对人不适用）。
                    this.FailOn(delegate
                    {
                        Thing p = job.GetTarget(WithdrawPointInd).Thing;
                        return p == null || p.Destroyed || !p.Spawned;
                    });
                }
                else
                {
                    this.FailOnDespawnedNullOrForbidden(WithdrawPointInd);
                }
            }

            // 1. 预留：先把超维空间里这批货占住，防止两个小人被派去取同一批库存。
            yield return Toils_General.Do(delegate
            {
                // 保真桶：派活时已生成"这一桶里具体哪几件"的精确计划，按真身预留。
                // W47：读档后运行期计划不跨存档，但账本预留仍在；按预留键从容器内唯一桶重建计划。
                List<ThingCount> plan;
                bool hadPreservedReservation;
                if (TryResolvePreservedPlan(out plan, out hadPreservedReservation))
                {
                    if (plan != null && plan.Count > 0)
                    {
                        SDS_TransportVirtual.RegisterRebuiltLoadPlan(job.loadID, plan);
                    }
                }
                else if (hadPreservedReservation)
                {
                    FailPreservedRebuild();
                    return;
                }
                if (plan != null && plan.Count > 0)
                {
                    if (SDS_Rules.TryReservePreservedForJob(job.loadID, plan, pawn))
                    {
                        return;
                    }
                    SDS_TransportVirtual.NotifyLoadFailed(pawn, job.GetTarget(WorkTargetInd).Thing);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                int carrySpace = pawn.carryTracker != null && job.thingDefToCarry != null
                    ? pawn.carryTracker.MaxStackSpaceEver(job.thingDefToCarry)
                    : 0;
                // W46：与 ClampLoadTake 共用同一夹取实现，不得内联同式。
                int space = SDS_TransportVirtual.ClampCarry(job.count, carrySpace);
                // 派活时已按玩家勾选的那一行登记了「该取哪一条品类键」（保真按真身+材质+品质分组）；
                // 取走一次即删。
                SDS_StockKey preferred = SDS_TransportVirtual.TakePendingLoadKey(job.loadID);
                if (SDS_Rules.TryReserveForJob(job.loadID, job.thingDefToCarry, space, pawn, preferred,
                        out SDS_StockKey _, out int _))
                {
                    return;
                }
                SDS_TransportVirtual.NotifyLoadFailed(pawn, job.GetTarget(WorkTargetInd).Thing);
                EndJobWith(JobCondition.Incompletable);
            });

            // 2. 走位（自身端口就在小人身上，不需要寻路）。
            if (!IsSelfWithdrawPoint())
            {
                bool pawnPoint = job.targetA.Thing is Pawn;
                PathEndMode endMode = pawnPoint ? PathEndMode.Touch : PathEndMode.InteractionCell;
                // 出发前确认一次取物点可达（原版 GotoThing 没有「不可达」失败条件）。
                yield return Toils_General.Do(delegate
                {
                    Thing point = job.GetTarget(WithdrawPointInd).Thing;
                    if (point == null || !point.Spawned || point.Destroyed
                        || !pawn.CanReach(point, endMode, pawn.NormalMaxDanger()))
                    {
                        EndJobWith(JobCondition.Incompletable);
                    }
                });
                yield return Toils_Goto.GotoThing(WithdrawPointInd, endMode);
            }

            // 3. 物化到手上 → 交回原版装载 Job。
            Toil materialize = ToilMaker.MakeToil("SDS_MaterializeTransportLoad");
            materialize.initAction = MaterializeAndHandoff;
            materialize.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return materialize;
        }

        private void MaterializeAndHandoff()
        {
            bool materialized = false;
            // 保真桶：按派活时登记的"具体哪几件真身"精确取出（不跨桶、不按品类键乱拿）。
            List<ThingCount> plan = SDS_TransportVirtual.TakePendingLoadPlan(job.loadID);
            if (plan == null || plan.Count == 0)
            {
                // W47：读档恢复到「物化前」的 Job 时，首个 toil 可能已被跳过；这里必须再兜底重建一次。
                bool hadPreservedReservation;
                if (!TryResolvePreservedPlan(out plan, out hadPreservedReservation)
                    && hadPreservedReservation)
                {
                    FailPreservedRebuild();
                    return;
                }
            }
            if (plan != null && plan.Count > 0)
            {
                // 接管本 Job 的保真预留：失败不主动释放预留，由 Job 的 FinishAction 统一清理。
                if (SDS_Rules.TryWithdrawPreservedExactAdopted(job.loadID, plan, out List<Thing> real)
                    && real != null && real.Count > 0)
                {
                    for (int i = 0; i < real.Count; i++)
                    {
                        Thing thing = real[i];
                        if (thing == null || thing.Destroyed)
                        {
                            continue;
                        }
                        SDS_Rules.MarkProduced(thing);
                        if (pawn.carryTracker != null && pawn.carryTracker.CarriedThing == null
                            && pawn.carryTracker.TryStartCarry(thing))
                        {
                            continue;
                        }
                        // 一桶一趟通常只有一件；多出来的回充保真通道 / 落地，绝不丢。
                        SDS_Rules.ReturnThing(thing, SDS_StockChannel.Preserved, pawn.Map, pawn,
                            SDS_ReturnFallback.Ground);
                    }
                    materialized = pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null;
                }
            }
            else
            {
                materialized = SDS_Rules.TryWithdrawReservedForJob(job.loadID, out List<Thing> made)
                    && made != null && made.Count > 0;
                if (materialized)
                {
                    bool carried = false;
                    for (int i = 0; i < made.Count; i++)
                    {
                        Thing thing = made[i];
                        if (thing == null || thing.Destroyed)
                        {
                            continue;
                        }
                        if (pawn.carryTracker != null && pawn.carryTracker.TryStartCarry(thing))
                        {
                            carried = true;
                            continue;
                        }
                        // 拿不动就放回地图，绝不吞物品。
                        SDS_PlacementUtility.TryPlaceOrSpawn(thing, pawn.Position, pawn.Map);
                    }
                    materialized = carried;
                }
            }
            if (!materialized)
            {
                SDS_Diag.Fail("装载/取货物化失败", SDS_Diag.Ctx(
                    pawn != null ? pawn.Map : null, pawn,
                    job != null ? job.GetTarget(WorkTargetInd).Thing : null, job,
                    "reservation=" + (job != null ? job.loadID : 0)));
                SDS_TransportVirtual.NotifyLoadFailed(pawn, job.GetTarget(WorkTargetInd).Thing);
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            Thing carriedThing = pawn.carryTracker != null ? pawn.carryTracker.CarriedThing : null;
            Thing container = job.GetTarget(WorkTargetInd).Thing;
            if (carriedThing == null || container == null || container.Destroyed || !container.Spawned)
            {
                // 取出来但交不出去：优先回充超维空间，回充不了才落地，绝不吞物品。
                SDS_Diag.Fail("装载/交付目标失效", SDS_Diag.Ctx(
                    pawn != null ? pawn.Map : null, pawn, container, job,
                    "carried=" + (carriedThing != null ? carriedThing.def.defName : "无")));
                ReturnCarriedThingToGridOrDrop();
                SDS_TransportVirtual.NotifyLoadFailed(pawn, container);
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            // 货已在手上：交回原版 HaulToTransporter / HaulToPortal，
            // 让原版 toil 自己「带着货走到运输舱 / 传送门 → 投进去」（不落地）。
            JobDef handoffDef = container is MapPortal ? JobDefOf.HaulToPortal : JobDefOf.HaulToTransporter;
            Job loadHaul = JobMaker.MakeJob(handoffDef);
            loadHaul.targetA = carriedThing;
            loadHaul.targetB = container;
            loadHaul.count = carriedThing.stackCount;
            loadHaul.ignoreForbidden = true;
            loadHaul.haulMode = HaulMode.ToContainer;
            loadHaul.playerForced = job.playerForced;
            loadHaul.workGiverDef = job.workGiverDef;
            pawn.jobs.StartJob(
                loadHaul,
                JobCondition.Succeeded,
                null,
                false,
                true,
                null,
                null,
                false,
                false,
                true,
                false,
                true,
                true);
        }

        /// <summary>
        /// W47：确保本 Job 的保真计划可用。
        /// 返回 true = plan 可用（已有运行期计划，或已按预留键重建）；
        /// 返回 false = 没有保真预留（走虚拟腿），或保真预留存在但重建失败（hadPreservedReservation = true）。
        /// </summary>
        private bool TryResolvePreservedPlan(out List<ThingCount> plan, out bool hadPreservedReservation)
        {
            plan = SDS_TransportVirtual.PeekPendingLoadPlan(job.loadID);
            hadPreservedReservation = false;
            if (plan != null && plan.Count > 0)
            {
                return true;
            }
            if (!SDS_Rules.TryGetJobReservation(job.loadID, out SDS_StockKey key, out int total)
                || key == null || !key.preserved || total <= 0)
            {
                plan = null;
                return false;
            }
            hadPreservedReservation = true;
            if (!SDS_TransportVirtual.TryRebuildPreservedLoadPlan(
                    job.GetTarget(WorkTargetInd).Thing, job.loadID, out List<ThingCount> rebuilt))
            {
                plan = null;
                return false;
            }
            plan = rebuilt;
            return true;
        }

        /// <summary>W47：保真计划重建失败的唯一收尾（诊断 + 失败熔断 + 结束 Job）。</summary>
        private void FailPreservedRebuild()
        {
            Thing container = job != null ? job.GetTarget(WorkTargetInd).Thing : null;
            SDS_Diag.Fail("装载/取货物化失败", SDS_Diag.Ctx(
                pawn != null ? pawn.Map : null, pawn, container, job,
                "reservation=" + (job != null ? job.loadID : 0) + " rebuild=preserved"));
            SDS_TransportVirtual.NotifyLoadFailed(pawn, container);
            EndJobWith(JobCondition.Incompletable);
        }

        /// <summary>交付失败时处理手上的物资：优先回充超维空间，回充不了再放回地图。</summary>
        private void ReturnCarriedThingToGridOrDrop()
        {
            if (pawn == null || pawn.carryTracker == null || pawn.carryTracker.CarriedThing == null)
            {
                return;
            }
            if (!pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing dropped)
                || dropped == null)
            {
                return;
            }
            // 已落到地图：按原通道回充，回充失败保持原地即可（不重复落地）。
            SDS_StockChannel channel = SDS_Rules.TryGetStockKey(dropped, out SDS_StockKey key)
                && key != null && key.preserved
                ? SDS_StockChannel.Preserved
                : SDS_StockChannel.Virtual;
            SDS_Rules.ReturnThing(dropped, channel, pawn.Map, pawn, SDS_ReturnFallback.None);
        }
    }
}
