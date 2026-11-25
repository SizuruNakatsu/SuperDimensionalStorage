using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 世界唯一存档壳：持有账本与 Vanilla 存档状态。
    /// 不再实现 IThingHolder —— 保真真身由 SDS_Ledger 的私有 owner 持有（《模块接口》四.1）。
    /// </summary>
    public class SDS_GameComponent_Grid : GameComponent
    {
        public static SDS_GameComponent_Grid Instance { get; private set; }

        public ISDS_LedgerHost Ledger { get; private set; }

        internal SDS_VanillaState State { get; private set; }

        /// <summary>进入存档后待输出的一次载入诊断（地图未生成时延后到首个非空 tick）。</summary>
        private bool loadReportPending;

        /// <summary>地图集合签名：进 / 离图、口袋图进出时推进端点与镜像失效。</summary>
        private int lastMapSignature = int.MinValue;

        // 周期性结构对账（静默安全网）：兜住换图、毁坏未走 DeSpawn、断电 / 恢复无回调等漂移。
        // 缓冲挂在存档壳实例上，随存档创建 / 丢弃，不进进程级静态缓存（E1）。
        private const int RegistrySweepIntervalTicks = 250;
        private int nextRegistrySweepTick;
        private readonly List<SDS_StructureFact> sweepFacts = new List<SDS_StructureFact>();

        // 预留终老巡检：账本只提供快照与「按归属 id 释放」，判活事实由本组件（宿主）提供。
        // 读档后 FinalizeInit 立刻审一次；运行期按同一周期兜底，避免 Job 未走 FinishAction 时长期占用可用量。
        private const int ReservationAuditIntervalTicks = 250;
        private int nextReservationAuditTick;

        // 回收账本巡检：回收数据与 Job / Lord / 编成事实都在 Vanilla，Core 不再持有。
        private const int ReclaimSweepIntervalTicks = 250;
        private int nextReclaimSweepTick;

        // 回收账本的存档中转列表（Comp 级节点 sdsReclaim）。
        private List<SDS_ReclaimSave> reclaimScratch = new List<SDS_ReclaimSave>();

        // 库存变化提醒（旧 Grid 的 apparelNudge / wealthRecount）：
        // 账本版本变化后去抖触发，避免原版长周期扫描造成的「存了没反应」观感。
        private const int NudgeIntervalTicks = 30;
        private int nextNudgeTick;
        private const int ApparelNudgeIntervalTicks = 600;
        private const int WealthRecountIntervalTicks = 300;
        private int lastStockVersion = int.MinValue;
        private bool apparelNudgePending;
        private int nextApparelNudgeTick;
        private bool wealthRecountPending;
        private int nextWealthRecountTick;

        // RimWorld 的 Game.FillComponents 用 Activator.CreateInstance(type, this) 建组件，
        // 因此必须保留 (Game) 构造函数；GameComponent 自身没有带参基构造，不写 base(...)。
        public SDS_GameComponent_Grid(Game game)
        {
            // 构造顺序固定：清进程级静态状态 → 建账本 → 建状态 → 设 Instance → 交给 Core。
            SDS_StaticState.ResetAll();
            Ledger = SDS_Core.CreateLedger();
            State = new SDS_VanillaState();
            Instance = this;
            SDS_Core.Attach(Ledger);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // Ledger / State 由构造函数创建，这里只序列化内容；不兜底重建
            // （静默重建会丢掉存档账本且不重挂 SDS_Core，等价于静默丢档）。
            Ledger.ExposeData();
            State.ExposeData();
            // 回收账本（C1/C8）：sdsReclaim 与本账本的节点同级，只由本组件读写；
            // 旧档的同名节点也在这里，因此新旧档共用这一条读路径。
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (reclaimScratch == null)
                {
                    reclaimScratch = new List<SDS_ReclaimSave>();
                }
                reclaimScratch.Clear();
                SDS_ReclaimRegistry.ExportTo(reclaimScratch);
            }
            Scribe_Collections.Look(ref reclaimScratch, "sdsReclaim", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                SDS_ReclaimRegistry.ImportFrom(reclaimScratch);
                if (reclaimScratch != null)
                {
                    reclaimScratch.Clear();
                }
                // 审计（预留 / 回收）推迟到 FinalizeInit：那时地图实物已 spawn，Sweep 的
                // 「已 spawn / 无主」判据才有意义；在 PostLoadInit 里巡检会把尚未 spawn 的实物误判为已搬走。
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // 地图已 spawn，镜像此刻才能安全销毁；下一轮同步按账本重建。
            State?.CleanupLoadedMirrors();
            // W60：镜像重建后把 Job 里的旧镜像引用换到新镜像（最多 250 tick）。
            State?.BeginMirrorJobTargetRemap();
            // 立即同步 + 立即重映射，避免目标失效窗口；同步未就绪时保留到 Tick 重试。
            State?.TryRemapMirrorJobTargetsImmediately();
            // W48：运输舱 / 传送门装载行的纯展示物行按品类键 + 欠量重建（最多 250 tick）。
            State?.BeginTransportLoadRestore();
            // W47：立即尝试一次行恢复，缩短驱动器按预留键解析容器桶的等待窗口。
            State?.TryRestoreTransportLoadRowsImmediately();
            // 读档归一化：displayKeys 不落盘，给容器行内持久化的展示物补封存（只认唯一桶 / 虚拟键）。
            SDS_TransportVirtual.RebindLoadedDisplayKeys();
            // 读档归一化：账本自身已在 PostLoadInit 按 thingID 去重；这里用同一判据清理小人容器里的重复副本。
            PruneLoadedPreservedDuplicates();
            // 兜底：PostLoadInit 未走到（老档 / 特殊载入路径）时，这里再恢复一次回收账本。
            if (reclaimScratch != null && reclaimScratch.Count > 0)
            {
                SDS_ReclaimRegistry.ImportFrom(reclaimScratch);
                reclaimScratch.Clear();
            }
            // 地图实物已 spawn，回收账本此刻才能安全审计（含 MapComponent 暂存的旧受保护输出）。
            SDS_ReclaimRegistry.AuditRestored();
            // 预留终老：读档审计的统一入口（账本不读 Job / Pawn 存活语义）。
            SDS_Core.AuditReservations(ReclaimOwnerAlive);
            // 进入存档 = 一次事件：输出一次端点报数；地图尚未生成时延后到首个非空 tick。
            loadReportPending = true;
            lastMapSignature = ComputeMapSignature();
            SDS_OutputCommand.SyncForbiddenToPreserved();
            TryLogLoadDiagnostics();
        }

        /// <summary>
        /// 读档归一化：同一真身只能有一个所有者。与端点缓冲共用
        /// <see cref="SDS_EndpointUtility.PruneLoadedOwnership"/> 的同一判据；
        /// 只移除重复引用，不 Split、不 Destroy、不改数量。
        /// </summary>
        private static void PruneLoadedPreservedDuplicates()
        {
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return;
            }
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null || map.mapPawns == null)
                {
                    continue;
                }
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawns;
                if (pawns == null)
                {
                    continue;
                }
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn == null)
                    {
                        continue;
                    }
                    if (pawn.inventory != null)
                    {
                        SDS_EndpointUtility.PruneLoadedOwnership(pawn.inventory.innerContainer,
                            "读档清理背包保真重复副本");
                    }
                    if (pawn.carryTracker != null)
                    {
                        SDS_EndpointUtility.PruneLoadedOwnership(pawn.carryTracker.innerContainer,
                            "读档清理手持保真重复副本");
                    }
                    WarnIfPreservedGearEquipped(pawn);
                }
            }
        }

        /// <summary>
        /// 装备 / 衣物槽不自动摘除（避免改变原版“穿着中”的表现），命中只告警一次。
        /// 命中说明同一真身同时被账本与装备槽持有，属于需要人工处理的数据异常。
        /// </summary>
        private static void WarnIfPreservedGearEquipped(Pawn pawn)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (pawn == null || ledger == null)
            {
                return;
            }
            Thing found = null;
            if (pawn.apparel != null)
            {
                List<Apparel> worn = pawn.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    if (worn[i] != null && ledger.IsPreservedThingId(worn[i].thingIDNumber))
                    {
                        found = worn[i];
                        break;
                    }
                }
            }
            if (found == null && pawn.equipment != null)
            {
                List<ThingWithComps> equipped = pawn.equipment.AllEquipmentListForReading;
                for (int i = 0; i < equipped.Count; i++)
                {
                    if (equipped[i] != null && ledger.IsPreservedThingId(equipped[i].thingIDNumber))
                    {
                        found = equipped[i];
                        break;
                    }
                }
            }
            if (found != null)
            {
                SDS_Diag.Warn("读档发现保真真身仍在穿戴/装备槽（未自动摘除）",
                    SDS_Diag.Ctx(pawn.Map, pawn, found));
            }
        }

        public override void GameComponentTick()
        {
            if (Ledger != null)
            {
                Ledger.Tick();
            }
            if (State != null)
            {
                State.Tick();
            }
            TryLogLoadDiagnostics();
            if (State != null)
            {
                State.TrySendIntroNotice();
            }
            int now = GenTicks.TicksGame;
            if (now >= nextReservationAuditTick)
            {
                nextReservationAuditTick = now + ReservationAuditIntervalTicks;
                SDS_Core.AuditReservations(ReclaimOwnerAlive);
            }
            if (now >= nextReclaimSweepTick)
            {
                nextReclaimSweepTick = now + ReclaimSweepIntervalTicks;
                SDS_ReclaimRegistry.Sweep(now);
            }
            if (now >= nextNudgeTick)
            {
                nextNudgeTick = now + NudgeIntervalTicks;
                CheckMapSignature();
                TickStockNudges();
            }
            SweepRegistries();
        }

        /// <summary>
        /// 预留终老判活：Job 仍存在即视为存活。
        /// 入：loadID = 预留归属 id。出：是否存活。
        /// 区别：宿主未注入时返回 true（无判活事实 = 静默跳过），不得批量释放正 id 预留。
        /// </summary>
        private static bool ReclaimOwnerAlive(int loadID)
        {
            ISDS_ReclaimWorld world = SDS_Host.ReclaimWorld;
            return world == null || world.JobExists(loadID);
        }

        /// <summary>
        /// 库存变化后的两条提醒：只提前触发原版扫描 / 重算，不改变原版判定。
        /// 账本版本变化 → 置 pending；服装 600 tick、财富 300 tick 去抖执行。
        /// </summary>
        private void TickStockNudges()
        {
            if (Ledger != null && Ledger.StockVersion != lastStockVersion)
            {
                lastStockVersion = Ledger.StockVersion;
                apparelNudgePending = true;
                wealthRecountPending = true;
            }
            int now = GenTicks.TicksGame;
            if (wealthRecountPending && now >= nextWealthRecountTick)
            {
                wealthRecountPending = false;
                nextWealthRecountTick = now + WealthRecountIntervalTicks;
                // 超维库存不计入财富时，库存变化不会改变 WealthWatcher；
                // 切换开关本身已由 RefreshWealth 兜底，这里不再触发全图重算。
                if (!SDS_Config.ExcludeGridFromWealth)
                {
                    Map home = Find.AnyPlayerHomeMap;
                    if (home != null && !home.Disposed && home.wealthWatcher != null)
                    {
                        home.wealthWatcher.ForceRecount();
                    }
                }
            }
            if (!apparelNudgePending || now < nextApparelNudgeTick)
            {
                return;
            }
            apparelNudgePending = false;
            nextApparelNudgeTick = now + ApparelNudgeIntervalTicks;
            Map map = Find.AnyPlayerHomeMap;
            if (map == null || map.Disposed || map.mapPawns == null)
            {
                return;
            }
            List<Pawn> pawns = map.mapPawns.PawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && pawn.Spawned && !pawn.Dead
                    && pawn.RaceProps != null && pawn.RaceProps.Humanlike
                    && pawn.mindState != null)
                {
                    // 只让原版下一次 think 立即重新评分；穿不穿仍由原版政策决定。
                    pawn.mindState.nextApparelOptimizeTick = now;
                }
            }
        }

        private static int ComputeMapSignature()
        {
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return 0;
            }
            int hash = 17;
            hash = hash * 31 + maps.Count;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                hash = hash * 31 + (map != null ? map.uniqueID : 0);
            }
            return hash;
        }

        private void CheckMapSignature()
        {
            int signature = ComputeMapSignature();
            if (signature == lastMapSignature)
            {
                return;
            }
            lastMapSignature = signature;
            SDS_Rules.NotifyEndpointChanged();
            SDS_MirrorService.NotifyStockChanged();
            SDS_AccessPointResolver.Invalidate();
            // 地图集合变化后立刻重扫一次结构事实，不等下一个 250 tick。
            nextRegistrySweepTick = 0;
        }

        /// <summary>
        /// 每 250 tick 按地图重扫三类建筑，作为结构注册表的静默安全网：
        /// 换图、毁坏未走 DeSpawn、断电 / 恢复无回调等漏网情况在这里收敛。
        /// 只提交活体事实，补齐 / 剔除 / 字段修正的规则全部在 Core；不打日志。
        /// </summary>
        private void SweepRegistries()
        {
            int now = GenTicks.TicksGame;
            if (now < nextRegistrySweepTick)
            {
                return;
            }
            nextRegistrySweepTick = now + RegistrySweepIntervalTicks;
            List<Map> maps = Find.Maps;
            if (maps == null || maps.Count == 0)
            {
                // 地图未就绪时没有「活体事实」可比，绝不能据此清空注册表。
                return;
            }
            sweepFacts.Clear();
            bool scanned = false;
            ThingDef coreDef = DefDatabase<ThingDef>.GetNamedSilentFail(SDS_Config.CoreDefName);
            ThingDef portDef = DefDatabase<ThingDef>.GetNamedSilentFail(SDS_Config.PortDefName);
            ThingDef arrayDef = DefDatabase<ThingDef>.GetNamedSilentFail(SDS_Config.ArrayDefName);
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null || map.Disposed || map.listerThings == null)
                {
                    continue;
                }
                scanned = true;
                AppendFactsOfDef(map, coreDef);
                AppendFactsOfDef(map, portDef);
                AppendFactsOfDef(map, arrayDef);
            }
            if (!scanned)
            {
                return;
            }
            bool changed = SDS_Rules.ReconcileStructures(sweepFacts);
            if (changed)
            {
                // 结构化事实无变化时不再每 250 tick 触发一次镜像重评；
                // Reconcile 已比较 Operational，核心断电 / 恢复会走 changed 分支。
                SDS_MirrorService.NotifyStockChanged();
                SDS_AccessPointResolver.Invalidate();
            }
            // 兜底校正端点优先级（其它 Mod / 手动改过时回到全局唯一来源；无变化时零写入）。
            SDS_BuildingUtility.ApplyStoragePriorityToEndpoints();
        }

        /// <summary>按 Def 精确取 SDS 建筑，避免每 250 tick 扫描全基地的人工建筑。</summary>
        private void AppendFactsOfDef(Map map, ThingDef def)
        {
            if (map == null || def == null)
            {
                return;
            }
            List<Thing> things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                Building_SDS_Core core = thing as Building_SDS_Core;
                if (core != null)
                {
                    sweepFacts.Add(core.BuildFact());
                    continue;
                }
                Building_SDS_Port port = thing as Building_SDS_Port;
                if (port != null)
                {
                    sweepFacts.Add(port.BuildFact());
                    continue;
                }
                Building_SDS_CapacityArray array = thing as Building_SDS_CapacityArray;
                if (array != null)
                {
                    sweepFacts.Add(array.BuildFact());
                }
            }
        }

        /// <summary>
        /// 每次进入存档：由 Vanilla 诊断 provider 产出 B 类报数，出口在 SDS.SelfCheck（Diag）。
        /// 地图未就绪时保持 pending；开发者模式随后跑一次完整只读自检。
        /// </summary>
        private void TryLogLoadDiagnostics()
        {
            if (!loadReportPending || Find.Maps == null || Find.Maps.Count == 0)
            {
                return;
            }
            loadReportPending = false;
            SDS_VanillaDiagnostics.LoadReportRequested = true;
            SDS_SelfCheck.RunLoadReport();
            if (Prefs.DevMode)
            {
                SDS_SelfCheck.Run(false);
            }
        }
    }
}
