using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Vanilla 存档状态（节点名与 LookMode 见《模块接口》四.1 / 八.3）：
    /// 存入筛选、核心优先级、首档提示、侧边栏 UI、镜像与装载行中转。
    /// 端点候选缓存是运行期派生数据，不落盘。
    /// </summary>
    internal sealed class SDS_VanillaState : IExposable
    {
        /// <summary>每图的端点建筑缓存：只在端点集合变化（SDS_Rules.EndpointVersion 推进）时重建。</summary>
        private sealed class EndpointCache
        {
            internal readonly List<Thing> structures = new List<Thing>();
            internal int version = int.MinValue;
            internal bool built;
        }

        private static readonly List<Thing> noStructures = new List<Thing>();
        private static readonly List<Map> staleMaps = new List<Map>();

        private readonly Dictionary<Map, EndpointCache> endpointCaches =
            new Dictionary<Map, EndpointCache>();

        internal ThingFilter depositFilter;
        internal int coreStoragePriority = -1;
        internal bool introNoticeShown;
        internal SDS_SidebarUIState sidebarUI = new SDS_SidebarUIState();
        internal List<Thing> mirrorScratch = new List<Thing>();
        internal List<SDS_TransportLoadSave> transportLoadScratch = new List<SDS_TransportLoadSave>();

        // W60：读档镜像 Job 目标重映射。镜像本体读档后会被销毁重建，
        // 存档里的 Job 目标必须先按「Job + 槽位 + 品类键」落盘，等镜像重建后再换到新镜像。
        internal List<SDS_MirrorJobTargetSave> mirrorJobTargets = new List<SDS_MirrorJobTargetSave>();
        private bool mirrorJobTargetRemapPending;
        private int mirrorJobTargetRemapDeadline;
        private int mirrorJobTargetRemapNextTick;

        // W48：运输舱 / 传送门装载行的纯展示物行跨存档重建。
        private bool transportLoadRestorePending;
        private int transportLoadRestoreDeadline;
        private int transportLoadRestoreNextTick;

        private int filterVersion;
        private int tickCounter;

        internal int FilterVersion
        {
            get { return filterVersion; }
        }

        private static SDS_VanillaState Current
        {
            get
            {
                SDS_GameComponent_Grid grid = SDS_GameComponent_Grid.Instance;
                return grid != null ? grid.State : null;
            }
        }

        // ---------- 存入筛选 ----------

        /// <summary>全局唯一一套存入标准；读档缺失时按默认口径重建。</summary>
        internal ThingFilter DepositFilter
        {
            get
            {
                if (depositFilter == null)
                {
                    depositFilter = BuildDefaultDepositFilter();
                }
                return depositFilter;
            }
        }

        /// <summary>
        /// 默认只勾选可重建的普通物品与基因载体；尸体 / 缩装家具默认不勾选，由玩家手动开启。
        /// 与冻结版 SDS_DepositRules.AllowedByDefault 同口径（Core 的规则是 internal，Vanilla 不能直接引用）。
        /// </summary>
        private ThingFilter BuildDefaultDepositFilter()
        {
            ThingFilter filter = new ThingFilter(NotifyFilterChanged);
            filter.SetDisallowAll();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (AllowedByDefault(def))
                {
                    filter.SetAllow(def, true);
                }
            }
            return filter;
        }

        private static bool AllowedByDefault(ThingDef def)
        {
            if (def == null || def.category != ThingCategory.Item)
            {
                return false;
            }
            Type cls = def.thingClass;
            if (SDS_Rules.IsLegacyUnrebuildableDef(def))
            {
                return cls != null && typeof(GeneSetHolderBase).IsAssignableFrom(cls);
            }
            // 普通物品（无论虚拟还是命中黑名单后保真）默认勾选。
            return def.EverHaulable;
        }

        private void NotifyFilterChanged()
        {
            filterVersion++;
            SDS_Core.NotifyDepositFilterChanged();
        }

        // ---------- 核心优先级（存档级） ----------

        /// <summary>本存档的核心优先级；-1 = 未设置过，回退到 Mod 设置。</summary>
        internal StoragePriority CoreStoragePriority
        {
            get
            {
                if (coreStoragePriority < 0)
                {
                    return SDS_Config.CoreStoragePriority;
                }
                return (StoragePriority)SDS_Config.Clamp(coreStoragePriority,
                    (int)StoragePriority.Low, (int)StoragePriority.Critical);
            }
        }

        internal static StoragePriority CurrentStoragePriority
        {
            get
            {
                SDS_VanillaState state = Current;
                return state != null ? state.CoreStoragePriority : SDS_Config.CoreStoragePriority;
            }
        }

        /// <summary>核心 UI 修改全局优先级：写存档字段 → 通知 Core → 同步所有端点。</summary>
        internal void SetCoreStoragePriority(StoragePriority value)
        {
            int clamped = SDS_Config.Clamp((int)value,
                (int)StoragePriority.Low, (int)StoragePriority.Critical);
            if (coreStoragePriority == clamped)
            {
                return;
            }
            coreStoragePriority = clamped;
            SDS_Rules.NotifyEndpointChanged();
            SDS_BuildingUtility.ApplyStoragePriorityToEndpoints();
        }

        // ---------- 端点候选缓存 ----------

        /// <summary>本图的端点建筑（核心 / 锚点），只在端点集合版本变化时重建。</summary>
        internal List<Thing> StructuresOn(Map map)
        {
            if (map == null || map.Disposed)
            {
                return noStructures;
            }
            if (!endpointCaches.TryGetValue(map, out EndpointCache cache) || cache == null)
            {
                cache = new EndpointCache();
                endpointCaches[map] = cache;
            }
            int version = SDS_Rules.EndpointVersion;
            if (!cache.built || cache.version != version)
            {
                Rebuild(map, cache);
                cache.version = version;
                cache.built = true;
            }
            return cache.structures;
        }

        /// <summary>本图是否存在可用核心（L 的真值源；ChipB 端口另行判定）。</summary>
        internal bool HasOperationalCore(Map map)
        {
            List<Thing> structures = StructuresOn(map);
            for (int i = 0; i < structures.Count; i++)
            {
                Building_SDS_Core core = structures[i] as Building_SDS_Core;
                if (core != null && core.Spawned && core.Map == map && core.Operational)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Rebuild(Map map, EndpointCache cache)
        {
            cache.structures.Clear();
            if (map == null || map.Disposed || map.listerThings == null)
            {
                return;
            }
            List<Thing> all = map.listerThings.AllThings;
            if (all == null)
            {
                return;
            }
            for (int i = 0; i < all.Count; i++)
            {
                Thing thing = all[i];
                if (thing != null && !thing.Destroyed && thing.Spawned && thing.Map == map
                    && thing is ISDS_StorageEndpoint)
                {
                    cache.structures.Add(thing);
                }
            }
        }

        // ---------- 生命周期 ----------

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                mirrorScratch.Clear();
                SDS_MirrorService.CollectMirrors(mirrorScratch);
                CollectMirrorJobTargets();
                CollectTransportLoadRows();
            }
            Scribe_Deep.Look(ref depositFilter, "depositFilter");
            Scribe_Values.Look(ref coreStoragePriority, "coreStoragePriority", -1);
            Scribe_Values.Look(ref introNoticeShown, "introNoticeShown", false);
            Scribe_Deep.Look(ref sidebarUI, "sidebarUI");
            Scribe_Collections.Look(ref mirrorScratch, "sdsMirrors", LookMode.Reference);
            Scribe_Collections.Look(ref transportLoadScratch, "transportLoadRequests", LookMode.Deep);
            Scribe_Collections.Look(ref mirrorJobTargets, "sdsMirrorJobTargets", LookMode.Deep);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }
            if (sidebarUI == null)
            {
                sidebarUI = new SDS_SidebarUIState();
            }
            if (mirrorScratch == null)
            {
                mirrorScratch = new List<Thing>();
            }
            if (transportLoadScratch == null)
            {
                transportLoadScratch = new List<SDS_TransportLoadSave>();
            }
            if (mirrorJobTargets == null)
            {
                mirrorJobTargets = new List<SDS_MirrorJobTargetSave>();
            }
            if (depositFilter != null)
            {
                // ThingFilter 的回调不落盘：读档后用同一份允许项重建，恢复"改筛选 → 推进版本"的唯一链路。
                ThingFilter loaded = depositFilter;
                depositFilter = new ThingFilter(NotifyFilterChanged);
                depositFilter.CopyAllowancesFrom(loaded);
            }
            // 旧档镜像只是投影：延后到 SDS_GameComponent_Grid.FinalizeInit 清理（地图 spawn 之后），
            // 避免在地图加载前 Destroy 造成 "Spawning destroyed thing"。
            SDS_MirrorService.NotifyStockChanged();
            endpointCaches.Clear();
        }

        /// <summary>读档镜像清理：必须在地图 spawn 之后由 SDS_GameComponent_Grid.FinalizeInit 调用。</summary>
        internal void CleanupLoadedMirrors()
        {
            if (mirrorScratch != null && mirrorScratch.Count > 0)
            {
                SDS_MirrorService.CleanupLoadedMirrors(mirrorScratch);
            }
            if (mirrorScratch != null)
            {
                mirrorScratch.Clear();
            }
        }

        // ---------- W60：读档镜像 Job 目标重映射 ----------

        /// <summary>
        /// 读档后调用：镜像清理 → 同步重建完成前，Job 里的旧镜像引用先视为待重映射；
        /// 最多等 250 tick，仍取不到镜像时由 SDS_MirrorJobTargets 先结束 / 出队再清槽。
        /// </summary>
        internal void BeginMirrorJobTargetRemap()
        {
            mirrorJobTargetRemapPending = mirrorJobTargets != null && mirrorJobTargets.Count > 0;
            int now = GenTicks.TicksGame;
            mirrorJobTargetRemapDeadline = now + 250;
            // 首个 Tick 立即尝试，不再空等 30 tick；读档时另有一把立即重映射。
            mirrorJobTargetRemapNextTick = now;
        }

        /// <summary>
        /// 读档收口：旧镜像已销毁时立即同步重建一次并重映射，缩短目标失效窗口。
        /// 同步未就绪（非 Playing / 无端点）时不强行清槽，保留到 Tick 重试。
        /// </summary>
        internal void TryRemapMirrorJobTargetsImmediately()
        {
            if (!mirrorJobTargetRemapPending || Verse.Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            try
            {
                SDS_MirrorService.NotifyStockChanged();
                SDS_MirrorService.SyncIfDirty();
                mirrorJobTargetRemapNextTick = 0;
                TryRemapMirrorJobTargets();
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("读档镜像 Job 目标立即重映射失败（已保留到下次 tick 重试）", e);
            }
        }

        /// <summary>
        /// 保存时收集：所有 Pawn 的当前 / 排队 Job 里指向镜像的目标槽，按「Job + 槽位 + 品类键」落盘。
        /// 不收集队列里的数量（countQueue 原样保留，只换目标引用）。
        /// </summary>
        // RULE:W60
        private void CollectMirrorJobTargets()
        {
            if (mirrorJobTargets == null)
            {
                mirrorJobTargets = new List<SDS_MirrorJobTargetSave>();
            }
            mirrorJobTargets.Clear();
            try
            {
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return;
            }
            for (int m = 0; m < maps.Count; m++)
            {
                SDS_MirrorJobTargets.ForEachJobOnMap(maps[m],
                    delegate (Pawn pawn, Job job, bool isCurrent)
                    {
                        CollectMirrorJobTargets(job);
                    });
            }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("存档/镜像 Job 目标收集失败（本次不记录）", e);
                mirrorJobTargets.Clear();
            }
        }

        private void CollectMirrorJobTargets(Job job)
        {
            if (job == null)
            {
                return;
            }
            SDS_MirrorJobTargets.ForEachSlot(job,
                delegate (LocalTargetInfo target, int slot, int subIndex)
                {
                    Thing thing = target.Thing;
                    if (!SDS_MirrorRegistry.IsMirror(thing)
                        || !SDS_Rules.TryGetStockKey(thing, out SDS_StockKey key) || key == null)
                    {
                        return;
                    }
                    mirrorJobTargets.Add(new SDS_MirrorJobTargetSave
                    {
                        loadID = job.loadID,
                        slot = slot,
                        subIndex = subIndex,
                        key = key
                    });
                });
        }

        /// <summary>
        /// 按「Job 槽位 → 品类键 → 新镜像」重映射；键已无镜像且已到期限时先结束/出队，再丢弃该槽。
        /// 队列清空时同步移除 countQueue 对应项，避免目标与数量错位；不写 LocalTargetInfo.Invalid。
        /// </summary>
        private void TryRemapMirrorJobTargets()
        {
            if (!mirrorJobTargetRemapPending)
            {
                return;
            }
            int now = GenTicks.TicksGame;
            if (now < mirrorJobTargetRemapNextTick)
            {
                return;
            }
            mirrorJobTargetRemapNextTick = now + 30;
            bool deadline = now >= mirrorJobTargetRemapDeadline;
            if (!SDS_Rules.Available && !deadline)
            {
                return;
            }
            Dictionary<int, Job> jobs = new Dictionary<int, Job>();
            Dictionary<int, Map> jobMaps = new Dictionary<int, Map>();
            Dictionary<int, Pawn> jobOwners = new Dictionary<int, Pawn>();
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return;
            }
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null || map.Disposed || map.mapPawns == null)
                {
                    continue;
                }
                SDS_MirrorJobTargets.ForEachJobOnMap(map,
                    delegate (Pawn pawn, Job job, bool isCurrent)
                    {
                        if (job == null || jobs.ContainsKey(job.loadID))
                        {
                            return;
                        }
                        jobs[job.loadID] = job;
                        jobMaps[job.loadID] = map;
                        jobOwners[job.loadID] = pawn;
                    });
            }
            if (jobs.Count == 0)
            {
                mirrorJobTargets.Clear();
                mirrorJobTargetRemapPending = false;
                return;
            }
            // 队列清空会移动后续下标：同 Job 同队列按 subIndex 从大到小应用。
            List<SDS_MirrorJobTargetSave> ordered = new List<SDS_MirrorJobTargetSave>(mirrorJobTargets);
            ordered.Sort((a, b) =>
            {
                int c = a.loadID.CompareTo(b.loadID);
                if (c != 0)
                {
                    return c;
                }
                c = a.slot.CompareTo(b.slot);
                return c != 0 ? c : b.subIndex.CompareTo(a.subIndex);
            });
            List<SDS_MirrorJobTargetSave> pending = new List<SDS_MirrorJobTargetSave>();
            for (int i = 0; i < ordered.Count; i++)
            {
                SDS_MirrorJobTargetSave entry = ordered[i];
                if (entry == null)
                {
                    continue;
                }
                if (!jobs.TryGetValue(entry.loadID, out Job job) || job == null)
                {
                    continue;
                }
                // 槽已推进（例如 StartCarryThing 已换成手上真身）时跳过，避免覆盖真实目标或误结束 Job。
                if (!SDS_MirrorJobTargets.SlotAwaitingRemap(job, entry.slot, entry.subIndex))
                {
                    continue;
                }
                Thing mirror = null;
                if (entry.key != null && entry.key.Resolved
                    && jobMaps.TryGetValue(entry.loadID, out Map map)
                    && SDS_MirrorService.TryGetMirror(map, entry.key, out Thing found))
                {
                    mirror = found;
                }
                if (mirror == null && !deadline)
                {
                    pending.Add(entry);
                    continue;
                }
                if (mirror == null)
                {
                    jobOwners.TryGetValue(entry.loadID, out Pawn owner);
                    CloseMirrorJobSlot(owner, job, entry.slot, entry.subIndex);
                    continue;
                }
                SDS_MirrorJobTargets.SetSlot(job, entry.slot, entry.subIndex, mirror);
            }
            mirrorJobTargets.Clear();
            mirrorJobTargets.AddRange(pending);
            if (mirrorJobTargets.Count == 0)
            {
                mirrorJobTargetRemapPending = false;
            }
        }

        /// <summary>
        /// W60 失败分支：键已无镜像时不得写 Invalid；交给唯一收口先结束 / 出队，再丢弃槽。
        /// </summary>
        private static void CloseMirrorJobSlot(Pawn pawn, Job job, int slot, int subIndex)
        {
            try
            {
                SDS_MirrorJobCloseKind kind =
                    SDS_MirrorJobTargets.CloseSlot(pawn, job, slot, subIndex);
                if (kind != SDS_MirrorJobCloseKind.None)
                {
                    // 固定 reason 去重；具体 Job / 槽位 / 动作放上下文，读档只记一次。
                    SDS_Diag.Warn("镜像 Job 目标失效收口（读档键已无镜像，先结束/出队）",
                        SDS_Diag.Ctx(null, pawn, null, job,
                            "slot=" + slot + " sub=" + subIndex + " kind=" + kind));
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("读档镜像 Job 目标收口失败（已跳过该槽）", e);
            }
        }

        // ---------- W48：运输舱 / 传送门装载行跨存档重建 ----------

        /// <summary>读档后调用：装载行的展示物引用不跨存档，按品类键 + 欠量重建（最多等 250 tick）。</summary>
        internal void BeginTransportLoadRestore()
        {
            transportLoadRestorePending = transportLoadScratch != null && transportLoadScratch.Count > 0;
            int now = GenTicks.TicksGame;
            transportLoadRestoreDeadline = now + 250;
            transportLoadRestoreNextTick = now + 30;
        }

        /// <summary>
        /// 读档时立即尝试一次 W48 行恢复（跳过 30 tick 节流），让 W47 驱动器能尽早解析到容器桶。
        /// 失败时保持 pending，后续仍由 Tick 按 30 tick 重试。
        /// </summary>
        internal void TryRestoreTransportLoadRowsImmediately()
        {
            if (!transportLoadRestorePending)
            {
                return;
            }
            transportLoadRestoreNextTick = 0;
            TryRestoreTransportLoadRows();
        }

        /// <summary>
        /// 保存时收集：运输舱 / 传送门里「纯展示物行」按「容器 thingID + 行号 + 品类键 + 欠量」落盘。
        /// 真实物行、虚拟与保真混行、多桶混行不收集（交回原版自行持久化）。
        /// </summary>
        // RULE:W48
        private void CollectTransportLoadRows()
        {
            if (transportLoadScratch == null)
            {
                transportLoadScratch = new List<SDS_TransportLoadSave>();
            }
            transportLoadScratch.Clear();
            try
            {
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return;
            }
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null || map.Disposed || map.listerThings == null)
                {
                    continue;
                }
                List<Thing> all = map.listerThings.AllThings;
                for (int i = 0; i < all.Count; i++)
                {
                    Thing thing = all[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }
                    List<TransferableOneWay> rows = null;
                    CompTransporter transporter = thing.TryGetComp<CompTransporter>();
                    if (transporter != null)
                    {
                        rows = transporter.leftToLoad;
                    }
                    else if (thing is MapPortal portal)
                    {
                        rows = portal.leftToLoad;
                    }
                    if (rows == null || rows.Count == 0)
                    {
                        continue;
                    }
                    for (int r = 0; r < rows.Count; r++)
                    {
                        if (!SDS_TransportVirtual.TryGetRestorableRowKey(rows[r],
                                out SDS_StockKey key, out bool preserved))
                        {
                            continue;
                        }
                        transportLoadScratch.Add(new SDS_TransportLoadSave
                        {
                            containerThingID = thing.thingIDNumber,
                            rowIndex = r,
                            defName = key.EffectiveDefName,
                            stuffName = key.EffectiveStuffName,
                            qualityInt = key.qualityInt,
                            preserved = preserved,
                            units = rows[r].CountToTransfer
                        });
                    }
                }
            }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("存档/装载行收集失败（本次不记录）", e);
                transportLoadScratch.Clear();
            }
        }

        /// <summary>
        /// 读档后把纯展示物行按品类键重建到原容器的 leftToLoad。
        /// 行号只作为“空行”提示，不硬套；找不到空行时按欠量新建行。
        /// </summary>
        private void TryRestoreTransportLoadRows()
        {
            if (!transportLoadRestorePending)
            {
                return;
            }
            int now = GenTicks.TicksGame;
            if (now < transportLoadRestoreNextTick)
            {
                return;
            }
            transportLoadRestoreNextTick = now + 30;
            bool deadline = now >= transportLoadRestoreDeadline;
            if (!SDS_Rules.Available && !deadline)
            {
                return;
            }
            Dictionary<int, Thing> containers = new Dictionary<int, Thing>();
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return;
            }
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null || map.Disposed || map.listerThings == null)
                {
                    continue;
                }
                List<Thing> all = map.listerThings.AllThings;
                for (int i = 0; i < all.Count; i++)
                {
                    Thing thing = all[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }
                    if (thing.TryGetComp<CompTransporter>() != null || thing is MapPortal)
                    {
                        containers[thing.thingIDNumber] = thing;
                    }
                }
            }
            if (containers.Count == 0)
            {
                if (deadline)
                {
                    transportLoadScratch.Clear();
                    transportLoadRestorePending = false;
                }
                return;
            }
            List<SDS_TransportLoadSave> pending = new List<SDS_TransportLoadSave>();
            for (int i = 0; i < transportLoadScratch.Count; i++)
            {
                SDS_TransportLoadSave save = transportLoadScratch[i];
                if (save == null)
                {
                    continue;
                }
                if (!containers.TryGetValue(save.containerThingID, out Thing container)
                    || container == null)
                {
                    if (!deadline)
                    {
                        pending.Add(save);
                    }
                    continue;
                }
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(save.defName);
                if (def == null)
                {
                    continue;
                }
                ThingDef stuff = string.IsNullOrEmpty(save.stuffName)
                    ? null
                    : DefDatabase<ThingDef>.GetNamedSilentFail(save.stuffName);
                SDS_StockKey key = new SDS_StockKey(def, stuff, save.qualityInt, save.preserved);
                Thing display = SDS_TransportVirtual.TryGetRestoreDisplay(key);
                if (display == null)
                {
                    if (!deadline)
                    {
                        pending.Add(save);
                    }
                    continue;
                }
                List<TransferableOneWay> rows = null;
                CompTransporter comp = container.TryGetComp<CompTransporter>();
                if (comp != null)
                {
                    if (comp.leftToLoad == null)
                    {
                        comp.leftToLoad = new List<TransferableOneWay>();
                    }
                    rows = comp.leftToLoad;
                }
                else if (container is MapPortal portal)
                {
                    if (portal.leftToLoad == null)
                    {
                        portal.leftToLoad = new List<TransferableOneWay>();
                    }
                    rows = portal.leftToLoad;
                }
                if (rows == null)
                {
                    continue;
                }
                // 行号只用于复用“空行”（读档后被原版清掉 things 的纯超维行），不硬套到真实行。
                TransferableOneWay row = null;
                if (save.rowIndex >= 0 && save.rowIndex < rows.Count)
                {
                    TransferableOneWay candidate = rows[save.rowIndex];
                    if (candidate != null && !candidate.HasAnyThing)
                    {
                        row = candidate;
                    }
                }
                if (row == null)
                {
                    for (int r = 0; r < rows.Count; r++)
                    {
                        if (rows[r] != null && !rows[r].HasAnyThing)
                        {
                            row = rows[r];
                            break;
                        }
                    }
                }
                if (row == null)
                {
                    row = new TransferableOneWay();
                    rows.Add(row);
                }
                if (row.things == null)
                {
                    row.things = new List<Thing>();
                }
                if (!row.things.Contains(display))
                {
                    row.things.Add(display);
                }
                IntVec3 cell = container.Position;
                if (container is Building building && building.def != null
                    && building.def.hasInteractionCell)
                {
                    cell = building.InteractionCell;
                }
                if (cell.IsValid && container.Map != null && cell.InBounds(container.Map))
                {
                    display.Position = cell;
                }
                if (display.stackCount < save.units)
                {
                    display.stackCount = save.units;
                }
                row.ForceTo(save.units);
            }
            transportLoadScratch.Clear();
            transportLoadScratch.AddRange(pending);
            if (transportLoadScratch.Count == 0)
            {
                transportLoadRestorePending = false;
            }
        }

        // ---------- 首次进档通知 ----------

        /// <summary>
        /// 首次进入存档时发一封基础操作通知信；每档只发一次，随 <see cref="introNoticeShown"/> 保存。
        /// 地图尚未生成时不发，等地图就绪后的首个 tick；PositiveEvent 不暂停游戏。
        /// </summary>
        internal void TrySendIntroNotice()
        {
            if (introNoticeShown || Find.Maps == null || Find.Maps.Count == 0
                || Find.LetterStack == null)
            {
                return;
            }
            introNoticeShown = true;
            Find.LetterStack.ReceiveLetter(
                "SDS.IntroNotice.Label".Translate(),
                "SDS.IntroNotice.Text".Translate(),
                LetterDefOf.PositiveEvent);
        }

        /// <summary>每 tick 驱动镜像同步（30 tick 节流）、保真供给清理（60 tick 节流）；每 250 tick 修剪带地图引用的缓存。</summary>
        public void Tick()
        {
            SDS_MirrorService.SyncIfDirty();
            try
            {
                TryRemapMirrorJobTargets();
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("读档镜像 Job 目标重映射异常（本 tick 跳过）", e);
                mirrorJobTargets.Clear();
                mirrorJobTargetRemapPending = false;
            }
            try
            {
                TryRestoreTransportLoadRows();
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("读档装载行重建异常（本 tick 跳过）", e);
                transportLoadScratch.Clear();
                transportLoadRestorePending = false;
            }
            SDS_OfferBridge.TickIfDue();
            tickCounter++;
            if (tickCounter < 250)
            {
                return;
            }
            tickCounter = 0;
            if (endpointCaches.Count == 0)
            {
                return;
            }
            staleMaps.Clear();
            foreach (KeyValuePair<Map, EndpointCache> pair in endpointCaches)
            {
                if (pair.Key == null || pair.Key.Disposed)
                {
                    staleMaps.Add(pair.Key);
                }
            }
            for (int i = 0; i < staleMaps.Count; i++)
            {
                endpointCaches.Remove(staleMaps[i]);
            }
            staleMaps.Clear();
        }
    }

    /// <summary>
    /// W60：读档镜像 Job 目标映射项。slot：0=A / 1=B / 2=C / 3=queueA / 4=queueB；
    /// subIndex 只对队列有效；key = 该目标原本所属品类。
    /// </summary>
    internal sealed class SDS_MirrorJobTargetSave : IExposable
    {
        internal int loadID;
        internal int slot;
        internal int subIndex;
        internal SDS_StockKey key;

        // Scribe_Deep 用 Activator.CreateInstance(type) 造实例，必须保留公开无参构造。
        public SDS_MirrorJobTargetSave()
        {
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref loadID, "loadID", 0);
            Scribe_Values.Look(ref slot, "slot", 0);
            Scribe_Values.Look(ref subIndex, "subIndex", 0);
            Scribe_Deep.Look(ref key, "key");
        }
    }

    /// <summary>W60 单槽收口的实际动作；供计数 / 日志区分，不参与判定。</summary>
    internal enum SDS_MirrorJobCloseKind
    {
        None,
        JobEnded,
        JobDequeued,
        QueueSlotRemoved
    }

    /// <summary>
    /// W60 槽位编号的唯一常量表。落盘 `SDS_MirrorJobTargetSave.slot` 仍用 int，
    /// 枚举 / 改写 / 收口 / 读取四处必须共用这里的值，不得再写裸 0–4。
    /// </summary>
    internal static class SDS_MirrorJobSlots
    {
        internal const int A = 0;
        internal const int B = 1;
        internal const int C = 2;
        internal const int QueueA = 3;
        internal const int QueueB = 4;
    }

    /// <summary>
    /// W60：镜像 Job 目标的唯一收口实现（保存 / 读档 / 运行期镜像移除共用）。
    /// 能按品类键找回新镜像时改写槽；找不回时先结束当前 Job / 将排队 Job 出队 /
    /// 移除当前 Job 的队列槽，再丢弃目标，**绝不写入 LocalTargetInfo.Invalid**。
    /// 槽位枚举 / 队列增删 / 结束-出队动作只在这里实现；A/B/C 的 SetTarget 由调用方执行。
    /// </summary>
    internal static class SDS_MirrorJobTargets
    {
        internal delegate void SlotVisitor(LocalTargetInfo target, int slot, int subIndex);

        /// <summary>
        /// Job 槽位（A/B/C + targetQueueA/B）的唯一枚举实现。
        /// 队列倒序遍历：访问者可以原地删除当前条目而不跳项。
        /// </summary>
        internal static void ForEachSlot(Job job, SlotVisitor visit)
        {
            if (job == null || visit == null)
            {
                return;
            }
            visit(job.targetA, SDS_MirrorJobSlots.A, 0);
            visit(job.targetB, SDS_MirrorJobSlots.B, 0);
            visit(job.targetC, SDS_MirrorJobSlots.C, 0);
            VisitQueue(job.targetQueueA, SDS_MirrorJobSlots.QueueA, visit);
            VisitQueue(job.targetQueueB, SDS_MirrorJobSlots.QueueB, visit);
        }

        private static void VisitQueue(List<LocalTargetInfo> queue, int slot, SlotVisitor visit)
        {
            if (queue == null)
            {
                return;
            }
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                visit(queue[i], slot, i);
            }
        }

        internal delegate void JobVisitor(Pawn pawn, Job job, bool isCurrent);

        /// <summary>
        /// 遍历一张图上所有 Pawn 的当前 Job 与排队 Job 的唯一实现。
        /// 排队 Job 先快照再访问，允许访问者出队 / 改槽而不跳项。
        /// </summary>
        internal static void ForEachJobOnMap(Map map, JobVisitor visit)
        {
            if (map == null || map.Disposed || map.mapPawns == null || visit == null)
            {
                return;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                ForEachJobOnPawn(pawns[i], visit);
            }
        }

        private static void ForEachJobOnPawn(Pawn pawn, JobVisitor visit)
        {
            Pawn_JobTracker tracker = pawn != null ? pawn.jobs : null;
            if (tracker == null)
            {
                return;
            }
            if (tracker.curJob != null)
            {
                visit(pawn, tracker.curJob, true);
            }
            JobQueue queue = tracker.jobQueue;
            if (queue == null || queue.Count == 0)
            {
                return;
            }
            List<Job> queuedJobs = new List<Job>(queue.Count);
            for (int i = 0; i < queue.Count; i++)
            {
                QueuedJob queued = queue[i];
                if (queued != null && queued.job != null)
                {
                    queuedJobs.Add(queued.job);
                }
            }
            for (int i = 0; i < queuedJobs.Count; i++)
            {
                visit(pawn, queuedJobs[i], false);
            }
        }

        /// <summary>收集该 Job 全部槽中的已登记镜像，供自检 / 测试共用。</summary>
        internal static void CollectReferencedMirrors(Job job, List<Thing> outList)
        {
            if (outList == null)
            {
                return;
            }
            ForEachSlot(job, delegate (LocalTargetInfo target, int slot, int subIndex)
            {
                Thing thing = target.Thing;
                if (thing != null && SDS_MirrorRegistry.IsMirror(thing))
                {
                    outList.Add(thing);
                }
            });
        }

        /// <summary>
        /// W60 读档条目只处理“仍停在读档清理留下的旧镜像（已 Destroyed）”或已失效为 null 的槽。
        /// Job 已推进（例如原版 StartCarryThing 已把槽换成手上真身）时返回 false：
        /// 不覆盖真实目标，也不误结束已经正常执行的 Job。
        /// </summary>
        internal static bool SlotAwaitingRemap(Job job, int slot, int subIndex)
        {
            if (job == null)
            {
                return false;
            }
            LocalTargetInfo target;
            switch (slot)
            {
                case SDS_MirrorJobSlots.A:
                    target = job.targetA;
                    break;
                case SDS_MirrorJobSlots.B:
                    target = job.targetB;
                    break;
                case SDS_MirrorJobSlots.C:
                    target = job.targetC;
                    break;
                case SDS_MirrorJobSlots.QueueA:
                    if (!TryQueueSlot(job.targetQueueA, subIndex, out target))
                    {
                        return false;
                    }
                    break;
                case SDS_MirrorJobSlots.QueueB:
                    if (!TryQueueSlot(job.targetQueueB, subIndex, out target))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
            Thing thing = target.Thing;
            return thing == null || thing.Destroyed;
        }

        private static bool TryQueueSlot(List<LocalTargetInfo> queue, int subIndex,
            out LocalTargetInfo target)
        {
            target = LocalTargetInfo.Invalid;
            if (queue == null || subIndex < 0 || subIndex >= queue.Count)
            {
                return false;
            }
            target = queue[subIndex];
            return true;
        }

        /// <summary>队列槽替换 / 删除的唯一实现；mirror == null 表示删除该槽并同步 countQueue。</summary>
        internal static bool ApplyQueueSlot(Job job, List<LocalTargetInfo> queue, int subIndex,
            Thing mirror)
        {
            if (job == null || queue == null || subIndex < 0 || subIndex >= queue.Count)
            {
                return false;
            }
            if (mirror != null)
            {
                queue[subIndex] = new LocalTargetInfo(mirror);
                return true;
            }
            queue.RemoveAt(subIndex);
            if (job.countQueue != null && subIndex < job.countQueue.Count)
            {
                job.countQueue.RemoveAt(subIndex);
            }
            return true;
        }

        /// <summary>槽位写入的唯一实现：核心槽 SetTarget，队列槽走 ApplyQueueSlot。</summary>
        internal static bool SetSlot(Job job, int slot, int subIndex, Thing thing)
        {
            if (job == null || thing == null)
            {
                return false;
            }
            LocalTargetInfo target = new LocalTargetInfo(thing);
            switch (slot)
            {
                case SDS_MirrorJobSlots.A:
                    job.SetTarget(TargetIndex.A, target);
                    return true;
                case SDS_MirrorJobSlots.B:
                    job.SetTarget(TargetIndex.B, target);
                    return true;
                case SDS_MirrorJobSlots.C:
                    job.SetTarget(TargetIndex.C, target);
                    return true;
                case SDS_MirrorJobSlots.QueueA:
                    return ApplyQueueSlot(job, job.targetQueueA, subIndex, thing);
                case SDS_MirrorJobSlots.QueueB:
                    return ApplyQueueSlot(job, job.targetQueueB, subIndex, thing);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 读档重映射失败时的单槽收口：当前 Job 的核心槽（A/B/C）结束 Job；
        /// 队列槽只移除该条；排队 Job 的核心槽出队，队列槽只移除该条。
        /// </summary>
        internal static SDS_MirrorJobCloseKind CloseSlot(Pawn pawn, Job job, int slot, int subIndex)
        {
            if (pawn == null || job == null || pawn.jobs == null)
            {
                return SDS_MirrorJobCloseKind.None;
            }
            Pawn_JobTracker tracker = pawn.jobs;
            bool current = ReferenceEquals(tracker.curJob, job);
            JobQueue pending = tracker.jobQueue;
            bool queued = !current && ContainsQueued(pending, job);
            if (!current && !queued)
            {
                return SDS_MirrorJobCloseKind.None;
            }
            if (slot >= SDS_MirrorJobSlots.QueueA)
            {
                List<LocalTargetInfo> queue = slot == SDS_MirrorJobSlots.QueueA
                    ? job.targetQueueA
                    : job.targetQueueB;
                return ApplyQueueSlot(job, queue, subIndex, null)
                    ? SDS_MirrorJobCloseKind.QueueSlotRemoved
                    : SDS_MirrorJobCloseKind.None;
            }
            if (current)
            {
                tracker.EndCurrentJob(JobCondition.Incompletable, startNewJob: false);
                return SDS_MirrorJobCloseKind.JobEnded;
            }
            return pending.Extract(job) != null
                ? SDS_MirrorJobCloseKind.JobDequeued
                : SDS_MirrorJobCloseKind.None;
        }

        private static bool ContainsQueued(JobQueue queue, Job job)
        {
            if (queue == null || job == null)
            {
                return false;
            }
            for (int i = 0; i < queue.Count; i++)
            {
                QueuedJob queued = queue[i];
                if (queued != null && ReferenceEquals(queued.job, job))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 运行期同一 key 的镜像替换：把 Job 中精确指向 oldThing 的槽改到 newThing。
        /// 只做引用替换，不取消 Job、不改数量；Job 已推进（目标已换成其它物）时自然跳过。
        /// </summary>
        internal static bool RemapReferencing(Map map, Thing oldThing, Thing newThing)
        {
            if (map == null || map.Disposed || map.mapPawns == null
                || oldThing == null || newThing == null || ReferenceEquals(oldThing, newThing))
            {
                return true;
            }
            try
            {
                ForEachJobOnMap(map, delegate (Pawn pawn, Job job, bool isCurrent)
                {
                    RemapJob(pawn, map, job, oldThing, newThing);
                });
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("镜像 Job 目标换新失败（已回退关闭旧目标）", e);
                return false;
            }
        }

        private static void RemapJob(Pawn pawn, Map map, Job job, Thing oldThing, Thing newThing)
        {
            if (job == null)
            {
                return;
            }
            int remapped = 0;
            ForEachSlot(job, delegate (LocalTargetInfo target, int slot, int subIndex)
            {
                if (ReferenceEquals(target.Thing, oldThing) && SetSlot(job, slot, subIndex, newThing))
                {
                    remapped++;
                }
            });
            // 旧镜像的预留不跨引用：改指后立即释放；新目标由 W61 非排他预留规则接管。
            if (remapped > 0 && pawn != null && map.reservationManager != null
                && map.reservationManager.ReservedBy(oldThing, pawn, job))
            {
                map.reservationManager.Release(oldThing, pawn, job);
            }
        }

        /// <summary>
        /// 镜像即将离开地图（移除 / 结构撤离 / 地图不再暴露）时，先收口引用它的 Job。
        /// 必须在 Unmark / DeSpawn / Destroy 之前调用；只做 Job 收口，不写账本、不碰镜像状态。
        /// </summary>
        internal static void CloseReferencing(Map map, Thing thing, string reason)
        {
            if (thing == null)
            {
                return;
            }
            CloseReferencing(map, new[] { thing }, reason);
        }

        private struct CloseStats
        {
            internal int ended;
            internal int removedJobs;
            internal int removedSlots;
        }

        /// <summary>批量版本：同一批镜像只扫描一次全图 Pawn。</summary>
        internal static void CloseReferencing(Map map, ICollection<Thing> things, string reason)
        {
            if (map == null || map.Disposed || map.mapPawns == null
                || things == null || things.Count == 0)
            {
                return;
            }
            HashSet<Thing> set = things as HashSet<Thing>;
            if (set == null)
            {
                set = new HashSet<Thing>(things);
            }
            int ended = 0;
            int removedJobs = 0;
            int removedSlots = 0;
            ForEachJobOnMap(map, delegate (Pawn pawn, Job job, bool isCurrent)
            {
                try
                {
                    CloseStats stats = CloseJob(pawn, job, set);
                    ended += stats.ended;
                    removedJobs += stats.removedJobs;
                    removedSlots += stats.removedSlots;
                }
                catch (Exception e)
                {
                    SDS_Diag.ErrorOnce("镜像 Job 目标收口失败（已忽略，继续移除镜像）", e);
                }
            });
            if (ended + removedJobs + removedSlots > 0)
            {
                // 固定 reason 去重；计数放上下文，避免同一操作重复刷屏。
                SDS_Diag.Warn("镜像 Job 目标失效收口（先结束/出队，再移除镜像）",
                    SDS_Diag.Ctx(map, null, null, null,
                        reason + " ended=" + ended + " queued=" + removedJobs
                        + " slots=" + removedSlots));
            }
        }

        /// <summary>
        /// 单个 Job 的槽位遍历与收口：动作只调 CloseSlot，这里不重复判断
        /// “核心槽结束 / 队列槽删除 / 排队 Job 出队”。
        /// </summary>
        private static CloseStats CloseJob(Pawn pawn, Job job, HashSet<Thing> things)
        {
            CloseStats stats = default;
            ForEachSlot(job, delegate (LocalTargetInfo target, int slot, int subIndex)
            {
                Thing thing = target.Thing;
                if (thing == null || !things.Contains(thing))
                {
                    return;
                }
                switch (CloseSlot(pawn, job, slot, subIndex))
                {
                    case SDS_MirrorJobCloseKind.JobEnded:
                        stats.ended++;
                        break;
                    case SDS_MirrorJobCloseKind.JobDequeued:
                        stats.removedJobs++;
                        break;
                    case SDS_MirrorJobCloseKind.QueueSlotRemoved:
                        stats.removedSlots++;
                        break;
                }
            });
            return stats;
        }
    }
}
