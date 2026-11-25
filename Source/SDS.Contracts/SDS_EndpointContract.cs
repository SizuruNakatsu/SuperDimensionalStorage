using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>端点调用的两种语义：存入 / 取出。</summary>
    public enum SDS_EndpointOp
    {
        Deposit,
        Withdraw
    }

    /// <summary>结构事实的种类：核心 / 锚点 / 稳固装置。</summary>
    public enum SDS_StructureKind
    {
        Core,
        Anchor,
        CapacityArray
    }

    /// <summary>结构事实：由 Vanilla 登记、Core 只读消费。</summary>
    public struct SDS_StructureFact
    {
        public Thing Thing;
        public Map Map;
        public SDS_StructureKind Kind;
        public bool Operational;
    }

    /// <summary>
    /// 统一端点查询。所有调用方只填这个结构，再由 Rules 按统一顺序判定：
    ///  - Deposit：端口可用 → 可存入（含容量 / 筛选）→ 允许自身 → 可达；G 关闭不拦存入。
    ///  - Withdraw：可选有货门（requireStock）→ 端口可用 → 允许自身 → 可达。
    ///  - 两种操作都按 priority → distance → self → thingID 排序；只对胜出候选判可达。
    /// </summary>
    public struct SDS_EndpointQuery
    {
        public SDS_EndpointOp op;
        public Map map;
        public Pawn actor;
        public IntVec3 reference;
        public Thing thing;
        public SDS_StockKey key;

        /// <summary>旧框架遗留字段：工厂会写入，但端点判定不读取；仅为兼容旧契约保留。</summary>
        public Danger danger;
        public bool requireReach;
        public bool allowSelf;
        public bool requireStock;
        public bool allowProducedMarked;

        /// <summary>候选排序用的存储优先级；由调用方在构造查询时填入，Core 不读全局。</summary>
        public int priority;

        /// <summary>
        /// 从宿主优先级槽位取一次值（唯一读取点，供默认构造的查询使用）。
        /// 入：无。出：无；把当前优先级写入本查询。
        /// 区别：工厂方法已代填；只有 `default` 构造的查询需要显式调用。
        /// </summary>
        public void ApplyHostPriority()
        {
            ISDS_PrioritySource source = SDS_Host.Priority;
            priority = source != null ? source.Priority : 0;
        }

        public static SDS_EndpointQuery Deposit(Thing thing, Pawn actor,
            bool allowProducedMarked = false)
        {
            SDS_EndpointQuery query = default;
            query.ApplyHostPriority();
            query.op = SDS_EndpointOp.Deposit;
            query.thing = thing;
            query.actor = actor;
            query.map = actor != null ? actor.Map : (thing != null ? thing.Map : null);
            query.reference = thing != null && thing.SpawnedOrAnyParentSpawned
                ? thing.PositionHeld
                : (actor != null ? actor.Position : IntVec3.Invalid);
            query.danger = actor != null ? actor.NormalMaxDanger() : Danger.Deadly;
            query.requireReach = actor != null;
            query.allowSelf = true;
            query.allowProducedMarked = allowProducedMarked;
            return query;
        }

        /// <summary>非 Pawn 存入查询：没有自身端口、没有 Pawn 可达性。</summary>
        public static SDS_EndpointQuery DepositForNonPawn(Thing thing, Map map, IntVec3 reference)
        {
            SDS_EndpointQuery query = Deposit(thing, null);
            query.map = map;
            query.reference = reference;
            query.requireReach = false;
            query.allowSelf = false;
            return query;
        }

        /// <summary>只选"取物点"，不要求某个具体 key 当前有库存。</summary>
        public static SDS_EndpointQuery WithdrawPoint(Pawn actor)
        {
            SDS_EndpointQuery query = default;
            query.ApplyHostPriority();
            query.op = SDS_EndpointOp.Withdraw;
            query.actor = actor;
            query.map = actor != null ? actor.Map : null;
            query.reference = actor != null ? actor.Position : IntVec3.Invalid;
            query.danger = actor != null ? actor.NormalMaxDanger() : Danger.Deadly;
            query.requireReach = actor != null;
            query.allowSelf = true;
            query.requireStock = false;
            return query;
        }
    }

    /// <summary>单个端点的判定结果；priority / distance 只用于排序。</summary>
    public struct SDS_EndpointDecision
    {
        public bool allowed;
        public ISDS_Port port;
        public int priority;
        public float distanceSquared;

        public void Reset()
        {
            allowed = false;
            port = null;
            priority = 0;
            distanceSquared = float.MaxValue;
        }
    }
}
