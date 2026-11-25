using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>统一端口句柄：建筑、Pawn、机械体共用。</summary>
    public interface ISDS_Port
    {
        Thing Thing { get; }
        Pawn Pawn { get; }
        Map Map { get; }
        bool IsSelf { get; }
        bool Operational { get; }
        IntVec3 AccessCell { get; }
        PathEndMode AccessMode { get; }
        bool CanReach(Pawn actor);
        float RankDistanceSquared(IntVec3 reference);
    }

    /// <summary>端点枚举与可用性事实（Vanilla 实现）。</summary>
    public interface ISDS_PortProvider
    {
        void AppendCandidates(in SDS_EndpointQuery query, List<ISDS_Port> into);
        bool TryGetPort(Thing thing, out ISDS_Port port);
        bool TryGetSelfPort(Pawn pawn, out ISDS_Port port);
        bool IsLocalUsable(Map map);
    }

    /// <summary>可存过滤（Vanilla 实现，现读存档状态）。</summary>
    public interface ISDS_DepositFilter
    {
        bool Allows(Thing thing);
    }

    /// <summary>优先级来源（Vanilla 实现，每次现读，不缓存排序）。</summary>
    public interface ISDS_PrioritySource
    {
        int Priority { get; }
        bool IgnorePriority { get; }
    }

    /// <summary>实物交付原语（Vanilla 实现）。</summary>
    public interface ISDS_DeliverySink
    {
        bool TryPlace(Thing thing, Map map, IntVec3 cell);
        bool TryGiveToPawn(Thing thing, Pawn pawn, bool preferCarry);
    }

    /// <summary>只读镜像表（Vanilla 实现）。</summary>
    public interface ISDS_MirrorQuery
    {
        IReadOnlyList<Thing> VisibleThingsOfDef(Map map, ThingDef def);
        bool IsMirror(Thing thing);
        int Version { get; }
    }

    /// <summary>展示物身份（Vanilla 实现，只读）：创建时封存的键是唯一身份来源。</summary>
    public interface ISDS_DisplayQuery
    {
        bool IsDisplay(Thing thing);
        bool TryGetDisplayKey(Thing thing, out SDS_StockKey key);
    }

    /// <summary>
    /// 第三方 Job 型适配器的存入事实与派活出口（Vanilla 实现）。
    /// 只转发唯一存入服务；适配器不得自行选端点 / 算数量 / 建 Job。
    /// </summary>
    public interface ISDS_DepositJobs
    {
        bool CanTakeCandidate(Pawn pawn, Thing thing, bool forced);
        Job TryMakeJob(Pawn pawn, Thing thing, bool forced, Job vanillaJob, bool multi);
        Job TryMakeFromInventory(Pawn pawn, IList<Thing> items);
        bool IsManualDepositAvailable(Pawn pawn);
    }

    /// <summary>
    /// 第三方自建装载清单的注入出口（Vanilla 实现，W50）。
    /// 只转发唯一装载注入；不决定取货 / 预留 / 扣账 / 数量口径。
    /// </summary>
    public interface ISDS_TransportLoadSink
    {
        bool InjectTransportLoadCandidates(ThingWithComps host, List<TransferableOneWay> transferables);
    }

    /// <summary>
    /// 物理原语，不继承 ISDS_Port；只做镜像形态转换，不碰账本。
    /// 返回 false 时 made 必须为 null；Core 只按返回值判定，不重复写 vitals。
    /// key 由 ExecuteMirror 从事务计划直接传入，禁止从 mirror 反推。
    /// </summary>
    public interface ISDS_MirrorPhysical
    {
        bool TryTakePhysical(SDS_StockKey key, Thing mirror, int count, out Thing made);
        bool TryTakeWholePhysical(SDS_StockKey key, Thing mirror, out Thing made);
    }

    /// <summary>医疗等级门控（Vanilla 实现）。</summary>
    public interface ISDS_MedicalCarePolicy
    {
        bool Allows(ThingDef medicine, Thing billGiver);
    }

    /// <summary>
    /// 医药等级门控的唯一调用口径（宿主未注入时放行）。
    /// 入：billGiver = 患者 / 工作台，可空；def = 候选药材。出：true = 允许。
    /// 区别：Core 不再读取该宿主；Vanilla / Api 在调用补料判据时通过本类组合筛选。
    /// </summary>
    public static class SDS_MedicalGate
    {
        public static bool Allows(Thing billGiver, ThingDef def)
        {
            ISDS_MedicalCarePolicy policy = SDS_Host.MedicalCare;
            return policy == null || policy.Allows(def, billGiver);
        }
    }

    /// <summary>回收所需的世界事实（Vanilla 实现）。</summary>
    public interface ISDS_ReclaimWorld
    {
        int CurrentTick { get; }
        bool JobExists(int jobLoadID);
        bool LordExists(int lordLoadID);
        void RemoveFromCaravan(int lordLoadID, Thing thing);
    }

    /// <summary>自检报告提供者；由各模块在 RegisterAll 时登记。</summary>
    public interface ISDS_DiagnosticProvider
    {
        string Name { get; }
        void AppendReport(List<string> lines);
    }

    /// <summary>第三方适配器契约；每个 Compat 程序集只允许一个实现。</summary>
    public interface ISDSCompatAdapter
    {
        string Name { get; }
        string TargetPackageId { get; }
        bool IsTargetLoaded { get; }
        void Apply();
    }

    /// <summary>
    /// Vanilla 装配出的 11 个宿主槽位（纯数据）。
    /// 由 <c>SDS_Vanilla.CreateHostSlots()</c> 产出；Bootstrap 只负责把它交给宿主安装入口。
    /// 用途：让 Bootstrap 不依赖 Vanilla 的 9 个实现类型（E8）。
    /// </summary>
    public sealed class SDS_HostSlots
    {
        public ISDS_PortProvider ports;
        public ISDS_DepositFilter depositFilter;
        public ISDS_PrioritySource priority;
        public ISDS_DeliverySink delivery;
        public ISDS_MirrorQuery mirrorQuery;
        public ISDS_MirrorPhysical mirrorPhysical;
        public ISDS_MedicalCarePolicy medicalCare;
        public ISDS_ReclaimWorld reclaimWorld;
        public ISDS_DisplayQuery display;
        public ISDS_DepositJobs depositJobs;
        public ISDS_TransportLoadSink transportLoad;
    }

    /// <summary>
    /// 宿主注入点：Core / Api 只经这些接口取宿主事实。
    /// 前 8 个是 Core 的通用事实；后 3 个只服务兼容层桥面（§7.5），Core 不消费。
    /// Install 只允许 SDS.Bootstrap 调用；任一接口为 null 记一次错误并继续，
    /// 依赖该接口的功能按不可用处理，不阻止 Mod 启动。
    /// </summary>
    public static class SDS_Host
    {
        public static ISDS_PortProvider Ports;
        public static ISDS_DepositFilter DepositFilter;
        public static ISDS_PrioritySource Priority;
        public static ISDS_DeliverySink Delivery;
        public static ISDS_MirrorQuery MirrorQuery;
        public static ISDS_MirrorPhysical MirrorPhysical;
        public static ISDS_MedicalCarePolicy MedicalCare;
        public static ISDS_ReclaimWorld ReclaimWorld;
        public static ISDS_DisplayQuery Display;
        public static ISDS_DepositJobs DepositJobs;
        public static ISDS_TransportLoadSink TransportLoad;

        /// <summary>唯一注入入口：只由 Bootstrap 调用（生产）或测试夹具调用，传入 11 个槽位。</summary>
        public static void Install(SDS_HostSlots slots)
        {
            if (slots == null)
            {
                InstallCore(null, null, null, null, null, null, null, null, null, null, null);
                return;
            }
            InstallCore(slots.ports, slots.depositFilter, slots.priority, slots.delivery,
                slots.mirrorQuery, slots.mirrorPhysical, slots.medicalCare, slots.reclaimWorld,
                slots.display, slots.depositJobs, slots.transportLoad);
        }

        private static void InstallCore(
            ISDS_PortProvider ports,
            ISDS_DepositFilter depositFilter,
            ISDS_PrioritySource priority,
            ISDS_DeliverySink delivery,
            ISDS_MirrorQuery mirrorQuery,
            ISDS_MirrorPhysical mirrorPhysical,
            ISDS_MedicalCarePolicy medicalCare,
            ISDS_ReclaimWorld reclaimWorld,
            ISDS_DisplayQuery display,
            ISDS_DepositJobs depositJobs,
            ISDS_TransportLoadSink transportLoad)
        {
            Ports = ports;
            DepositFilter = depositFilter;
            Priority = priority;
            Delivery = delivery;
            MirrorQuery = mirrorQuery;
            MirrorPhysical = mirrorPhysical;
            MedicalCare = medicalCare;
            ReclaimWorld = reclaimWorld;
            Display = display;
            DepositJobs = depositJobs;
            TransportLoad = transportLoad;

            if (Ports == null) { Log.ErrorOnce("[超维存储] SDS_Host.Ports 未注入：端点相关功能不可用。", 0x5D5F11); }
            if (DepositFilter == null) { Log.ErrorOnce("[超维存储] SDS_Host.DepositFilter 未注入：存入过滤恒为允许。", 0x5D5F12); }
            if (Priority == null) { Log.ErrorOnce("[超维存储] SDS_Host.Priority 未注入：优先级按默认值处理。", 0x5D5F13); }
            if (Delivery == null) { Log.ErrorOnce("[超维存储] SDS_Host.Delivery 未注入：实物交付 / 回充退路不可用。", 0x5D5F14); }
            if (MirrorQuery == null) { Log.ErrorOnce("[超维存储] SDS_Host.MirrorQuery 未注入：镜像查询不可用。", 0x5D5F15); }
            if (MirrorPhysical == null) { Log.ErrorOnce("[超维存储] SDS_Host.MirrorPhysical 未注入：镜像取用不可用。", 0x5D5F16); }
            if (MedicalCare == null) { Log.ErrorOnce("[超维存储] SDS_Host.MedicalCare 未注入：医疗门控按拒绝处理。", 0x5D5F17); }
            if (ReclaimWorld == null) { Log.ErrorOnce("[超维存储] SDS_Host.ReclaimWorld 未注入：回收巡检不可用。", 0x5D5F18); }
            if (Display == null) { Log.ErrorOnce("[超维存储] SDS_Host.Display 未注入：第三方展示物判定不可用。", 0x5D5F19); }
            if (DepositJobs == null) { Log.ErrorOnce("[超维存储] SDS_Host.DepositJobs 未注入：第三方存入派活不可用。", 0x5D5F1A); }
            if (TransportLoad == null) { Log.ErrorOnce("[超维存储] SDS_Host.TransportLoad 未注入：第三方装载清单注入不可用。", 0x5D5F1B); }
        }
    }
}
