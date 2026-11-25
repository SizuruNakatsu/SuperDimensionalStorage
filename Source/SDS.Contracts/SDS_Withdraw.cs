using RimWorld;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>取货交付模式。物理交付形态由原版契约决定，不由调用方随意指定。</summary>
    public enum SDS_WithdrawMode
    {
        Hand,
        Container,
        SpawnedTarget,

        /// <summary>历史兼容值：所有事务入口一律拒绝；当前交付只用 Hand / Container / SpawnedTarget。</summary>
        VirtualHandoff
    }

    /// <summary>供给失败的唯一分类；UI / 使用 / 日志共用这一份判定。</summary>
    public enum SDS_SupplyFailure
    {
        None,
        UnresolvedDef,
        Forbidden,
        Insufficient,

        /// <summary>预留值：GetFailure 当前不产出；供后续物化 / 重建失败分类，保留映射。</summary>
        RebuildFailed
    }

    /// <summary>事务终态：只描述事务自己的动作，只由事务写入。</summary>
    public enum SDS_WithdrawEndState
    {
        None,
        Reserved,
        Materialized,
        Delivered,
        RolledBack
    }

    /// <summary>回收结果：描述"账已扣之后，实物去哪了"，不得反向驱动事务状态迁移。</summary>
    public enum SDS_ReclaimOutcome
    {
        None,
        Returned,
        Consumed,
        Abandoned
    }

    /// <summary>
    /// 统一回收账本的存档 DTO：策略 + owner + 精确 Thing 引用 + 数量。
    /// </summary>
    public sealed class SDS_ReclaimSave : IExposable
    {
        public SDS_ReclaimPolicy policy;
        public int jobLoadID;
        public int ownerLoadID;
        public int tick;
        public bool hand;
        public List<Thing> things = new List<Thing>();
        public List<int> counts = new List<int>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref policy, "policy", SDS_ReclaimPolicy.JobTarget);
            Scribe_Values.Look(ref jobLoadID, "jobLoadID", 0);
            Scribe_Values.Look(ref ownerLoadID, "ownerLoadID", 0);
            Scribe_Values.Look(ref tick, "tick", 0);
            Scribe_Values.Look(ref hand, "hand", false);
            Scribe_Collections.Look(ref things, "things", LookMode.Reference);
            Scribe_Collections.Look(ref counts, "counts", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (things == null)
                {
                    things = new List<Thing>();
                }
                if (counts == null)
                {
                    counts = new List<int>();
                }
                while (counts.Count < things.Count)
                {
                    counts.Add(1);
                }
            }
        }
    }

    /// <summary>交付策略 Def：按 jobDefName 主键、driverType 兜底，决定交付模式。</summary>
    public class SDS_WithdrawDeliveryPolicyDef : Def
    {
        public string jobDefName;
        public string driverType;
        public SDS_WithdrawMode mode = SDS_WithdrawMode.Hand;
    }

    /// <summary>只读查询：只描述"要什么、多少、是否保真"，不含端点与模式决定。</summary>
    public sealed class SDS_WithdrawQuery
    {
        public SDS_StockKey key;
        public int quantity;
        public bool preserved;
    }

    /// <summary>
    /// 已提交的取货计划：endpoint 与 mode 在决定时固定。
    /// endpoint 由 Vanilla 装载选择读取并用于建 Job 目标，Rules 不读取；mode 用于 VirtualHandoff 守卫。
    /// </summary>
    public sealed class SDS_WithdrawPlan
    {
        public SDS_WithdrawQuery query;
        public ISDS_Port endpoint;
        public SDS_WithdrawMode mode;

        /// <summary>镜像取出的物理转换器；由调用方注入，Rules 不读宿主槽位。</summary>
        public ISDS_MirrorPhysical physical;
    }

    /// <summary>只读版本快照：镜像 / 端点 / 账本 / 预留 / 筛选 / tick 一次取齐。</summary>
    public struct SDS_ReadVersions
    {
        public int tick;
        public int mirror;
        public int endpoint;
        public int stock;
        public int reservation;
        public int filter;
    }
}
