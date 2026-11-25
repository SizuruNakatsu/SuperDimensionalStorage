using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// W63：cell-only Job 的目的地折叠与落点改写。
    /// 自身端口可用时走 W62 自存；否则把落点改写为被选中建筑端点的 AccessCell，
    /// 到达后由唯一落格钩子经 TryDeposit 入账。
    /// </summary>
    internal static class SDS_CellOnlyDestinationFold
    {
        private sealed class FoldTarget
        {
            internal ISDS_Port port;
            internal bool self;
        }

        // 以 Job 实例为键：Job 被丢弃后由 GC 回收，不留下无主登记；换档由 Reset 重建。
        private static ConditionalWeakTable<Job, FoldTarget> targets =
            new ConditionalWeakTable<Job, FoldTarget>();

        // RULE:W63
        internal static bool TryRewrite(Pawn pawn, Job job)
        {
            return TryRewrite(pawn, job, out _);
        }

        internal static bool TryRewrite(Pawn pawn, Job job, out bool endpointDestination)
        {
            endpointDestination = false;
            if (pawn == null || job == null)
            {
                return false;
            }
            if (!TryResolveFold(pawn, job, out ISDS_Port port, out bool self, out IntVec3 landing,
                    out endpointDestination))
            {
                return false;
            }
            job.targetC = new LocalTargetInfo(landing);
            FoldTarget target = targets.GetOrCreateValue(job);
            target.port = port;
            target.self = self;
            return true;
        }

        internal static bool CanRewrite(Pawn pawn, Job job, out bool endpointDestination)
        {
            return TryResolveFold(pawn, job, out _, out _, out _, out endpointDestination);
        }

        internal static bool TryGetSelf(Job job, out ISDS_Port port)
        {
            port = null;
            if (job == null)
            {
                return false;
            }
            if (targets.TryGetValue(job, out FoldTarget target) && target.self)
            {
                port = target.port;
                return port != null;
            }
            return false;
        }

        internal static bool TryGetEndpoint(Job job, out ISDS_Port port)
        {
            port = null;
            if (job == null)
            {
                return false;
            }
            if (targets.TryGetValue(job, out FoldTarget target) && !target.self)
            {
                port = target.port;
                return port != null;
            }
            return false;
        }

        internal static void Clear(Job job)
        {
            if (job != null)
            {
                targets.Remove(job);
            }
        }

        internal static void Reset()
        {
            targets = new ConditionalWeakTable<Job, FoldTarget>();
        }

        /// <summary>
        /// 从 carryTracker 直接走唯一写入口 TryDeposit；失败时实物保持原持有关系。
        /// 不再中转背包：HaulToCell 的后续 toil 仍需要 CarriedThing，失败必须可回退。
        /// </summary>
        internal static bool TryDepositCarried(Pawn pawn, ISDS_Port port, out int accepted)
        {
            accepted = 0;
            if (pawn == null || port == null || pawn.carryTracker == null)
            {
                return false;
            }
            Thing carried = pawn.carryTracker.CarriedThing;
            if (carried == null || carried.Destroyed)
            {
                return false;
            }
            return SDS_Rules.TryDeposit(carried, port, out accepted) && accepted > 0;
        }

        private static bool TryResolveFold(Pawn pawn, Job job, out ISDS_Port port, out bool self,
            out IntVec3 landing, out bool endpointDestination)
        {
            port = null;
            self = false;
            landing = IntVec3.Invalid;
            endpointDestination = false;
            if (pawn == null || job == null || pawn.Map == null || pawn.Map.Disposed
                || pawn.Position == IntVec3.Invalid)
            {
                return false;
            }
            if (!SDS_CellOnlyJobShape.TryGetEndpointDestinationJob(pawn, job,
                    out ISDS_StorageEndpoint endpoint))
            {
                return false;
            }
            endpointDestination = true;

            // W62：自身端口可用且可收，直接折叠到自身；取物后由 StartCarryThing 钩子入账。
            Thing contents = job.targetB.Thing;
            if (SDS_SelfDepositFold.TryResolveAuto(pawn, contents, out port))
            {
                self = true;
                landing = pawn.Position;
                return landing.IsValid;
            }

            Thing endpointThing = endpoint as Thing;
            if (endpointThing == null
                || SDS_Host.Ports == null
                || !SDS_Host.Ports.TryGetPort(endpointThing, out port)
                || port == null
                || !port.Operational)
            {
                port = null;
                return false;
            }
            if (!SDS_CellOnlyWorkGiverPatches.PlacementHookApplied)
            {
                port = null;
                return false;
            }
            landing = SDS_EndpointHandle.AccessCellOf(endpointThing);
            if (SDS_AccessRules.BlocksNonPlayer(pawn)
                || !landing.IsValid
                || !CanReachLanding(pawn, landing))
            {
                port = null;
                landing = IntVec3.Invalid;
                return false;
            }
            return true;
        }

        /// <summary>落点可达：只校验被写进 Job 的目标格，用与搬运 toil 一致的 OnCell 口径。</summary>
        private static bool CanReachLanding(Pawn pawn, IntVec3 cell)
        {
            if (pawn == null || pawn.Map == null || pawn.Map.reachability == null || !cell.IsValid)
            {
                return false;
            }
            return pawn.Map.reachability.CanReach(pawn.Position, new LocalTargetInfo(cell),
                PathEndMode.OnCell, TraverseParms.For(pawn));
        }
    }
}
