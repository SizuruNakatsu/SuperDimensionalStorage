using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Vanilla 宿主服务：存入筛选 / 优先级 / 实物交付。
    /// 进程级单例，每次现读 SDS_GameComponent_Grid.Instance?.State，不缓存跨档值（§6.2）。
    /// </summary>
    internal sealed partial class SDS_VanillaServices : ISDS_DepositFilter, ISDS_PrioritySource, ISDS_DeliverySink
    {
        private static SDS_VanillaState State
        {
            get
            {
                SDS_GameComponent_Grid grid = SDS_GameComponent_Grid.Instance;
                return grid != null ? grid.State : null;
            }
        }

        // ---------- ISDS_DepositFilter ----------

        /// <summary>存档未就绪（主菜单 / 单测）时恒允许，避免把 Core 的存入路径整体判死。</summary>
        public bool Allows(Thing thing)
        {
            if (thing == null)
            {
                return false;
            }
            SDS_VanillaState state = State;
            if (state == null)
            {
                return true;
            }
            return state.DepositFilter.Allows(thing);
        }

        // ---------- ISDS_PrioritySource ----------

        public int Priority
        {
            get { return (int)SDS_VanillaState.CurrentStoragePriority; }
        }

        public bool IgnorePriority
        {
            get { return SDS_Config.IgnorePriorityMode; }
        }

        // ---------- ISDS_DeliverySink ----------

        /// <summary>落地：指定格 → 邻近可站立格 → GenSpawn 兜底，保证数量守恒。</summary>
        public bool TryPlace(Thing thing, Map map, IntVec3 cell)
        {
            if (thing == null || thing.Destroyed || map == null)
            {
                return false;
            }
            return SDS_PlacementUtility.TryPlaceOrSpawn(thing, cell, map);
        }

        /// <summary>交给小人：优先手推车（preferCarry）或背包，失败再落地。</summary>
        public bool TryGiveToPawn(Thing thing, Pawn pawn, bool preferCarry)
        {
            if (thing == null || thing.Destroyed || pawn == null)
            {
                return false;
            }
            if (preferCarry && pawn.carryTracker != null
                && pawn.carryTracker.innerContainer.TryAdd(thing))
            {
                return true;
            }
            if (pawn.inventory != null && pawn.inventory.innerContainer != null
                && pawn.inventory.innerContainer.TryAdd(thing, true))
            {
                return true;
            }
            if (!preferCarry && pawn.carryTracker != null
                && pawn.carryTracker.innerContainer.TryAdd(thing))
            {
                return true;
            }
            if (!pawn.Spawned || pawn.Map == null)
            {
                return false;
            }
            return SDS_PlacementUtility.TryPlaceOrSpawn(thing, pawn.Position, pawn.Map);
        }
    }
}

