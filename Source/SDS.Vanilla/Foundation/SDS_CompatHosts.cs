using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 兼容宿主实现（§6.3）：只把兼容层需要的宿主事实 / 派活出口转发到唯一实现。
    /// public 无参构造，构造时不读 Instance / State / Find / Def；每次方法调用现读。
    /// 三种实现都不新增规则：展示物身份、存入派活、装载注入各只有一份实现。
    /// </summary>
    internal sealed partial class SDS_DisplayAdapter : ISDS_DisplayQuery
    {
        public bool IsDisplay(Thing thing)
        {
            try
            {
                return SDS_OutputUtility.IsDisplay(thing);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("兼容宿主/展示物判定异常", e);
                return false;
            }
        }

        public bool TryGetDisplayKey(Thing thing, out SDS_StockKey key)
        {
            key = null;
            try
            {
                return thing != null && SDS_OutputUtility.TryGetDisplayKey(thing, out key) && key != null;
            }
            catch (Exception e)
            {
                key = null;
                SDS_Diag.ErrorOnce("兼容宿主/展示键解析异常", e);
                return false;
            }
        }
    }

    /// <summary>第三方 Job 型适配器的存入事实：只转发 SDS_DepositService / SDS_ChipManualDeposit。</summary>
    internal sealed partial class SDS_DepositJobsAdapter : ISDS_DepositJobs
    {
        public bool CanTakeCandidate(Pawn pawn, Thing thing, bool forced)
        {
            try
            {
                return SDS_DepositService.CanTakeCandidate(pawn, thing, forced, out ISDS_Port _);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("兼容宿主/存入候选判定异常", e);
                return false;
            }
        }

        public Job TryMakeJob(Pawn pawn, Thing thing, bool forced, Job vanillaJob, bool multi)
        {
            try
            {
                return SDS_DepositService.TryMake(pawn, thing, forced, vanillaJob, multi);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("兼容宿主/存入派活异常", e);
                return null;
            }
        }

        public Job TryMakeFromInventory(Pawn pawn, IList<Thing> items)
        {
            try
            {
                return SDS_DepositService.TryMakeFromInventory(pawn, items);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("兼容宿主/背包存入派活异常", e);
                return null;
            }
        }

        public bool IsManualDepositAvailable(Pawn pawn)
        {
            try
            {
                return SDS_ChipManualDeposit.PawnCanDeposit(pawn);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("兼容宿主/手动存入查询异常", e);
                return false;
            }
        }
    }

    /// <summary>第三方自建装载清单注入（W50）：只转发 SDS_TransportVirtual 的唯一注入实现。</summary>
    internal sealed partial class SDS_TransportLoadAdapter : ISDS_TransportLoadSink
    {
        public bool InjectTransportLoadCandidates(ThingWithComps host,
            List<TransferableOneWay> transferables)
        {
            try
            {
                if (host == null || host.Map == null || transferables == null)
                {
                    return false;
                }
                CompTransporter transporter = host.TryGetComp<CompTransporter>();
                if (transporter == null)
                {
                    return false;
                }
                List<CompTransporter> transporters = transporter.TransportersInGroup(host.Map);
                if (transporters == null || transporters.Count == 0)
                {
                    // 原版 `TransportersInGroup` 在「尚未开始装载」（leftToLoad 还没建立）时返回 null，
                    // 这不是故障：只要宿主自身有 CompTransporter，就足以注入只读投影行。
                    // 与对方 `VanillaTransporterAdapter.ResolveTransporters` 的「组为空则退回首台」口径一致。
                    transporters = new List<CompTransporter> { transporter };
                }
                SDS_TransportVirtual.InjectIntoTransporters(transferables, transporters, host.Map);
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("兼容宿主/装载清单注入异常", e);
                return false;
            }
        }
    }
}



