using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 回收世界事实（§6.3）：当前 tick、Job / Lord 是否存在、从远征编成里摘物。
    /// Core 只经 ISDS_ReclaimWorld 读这些事实，不直接碰 Find / mapPawns。
    /// </summary>
    internal sealed partial class SDS_ReclaimWorldAdapter : ISDS_ReclaimWorld
    {
        public int CurrentTick
        {
            get { return Find.TickManager != null ? Find.TickManager.TicksGame : 0; }
        }

        /// <summary>该 loadID 的 Job 是否仍挂在某个已 spawn 小人身上（当前 Job 或队列）。</summary>
        public bool JobExists(int jobLoadID)
        {
            if (jobLoadID == 0 || Find.Maps == null)
            {
                return false;
            }
            for (int m = 0; m < Find.Maps.Count; m++)
            {
                Map map = Find.Maps[m];
                if (map == null || map.mapPawns == null)
                {
                    continue;
                }
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    Pawn_JobTracker jobs = pawn != null ? pawn.jobs : null;
                    if (jobs == null)
                    {
                        continue;
                    }
                    if (jobs.curJob != null && jobs.curJob.loadID == jobLoadID)
                    {
                        return true;
                    }
                    foreach (QueuedJob queued in jobs.jobQueue)
                    {
                        if (queued != null && queued.job != null && queued.job.loadID == jobLoadID)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public bool LordExists(int lordLoadID)
        {
            return SDS_CaravanVirtual.LordExists(lordLoadID);
        }

        /// <summary>
        /// 纯转发到 <see cref="SDS_CaravanVirtual.RemoveFromCaravan"/>：
        /// 编成摘物的唯一实现留在编成模块，适配器不保留第二份。
        /// </summary>
        public void RemoveFromCaravan(int lordLoadID, Thing thing)
        {
            SDS_CaravanVirtual.RemoveFromCaravan(lordLoadID, thing);
        }
    }
}

