using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 物理镜像标记：只有这里登记过的 Thing 才享受"不搬 / 不合并 / 不 tick / 不腐烂 / 不被销毁"的保护。
    /// 用引用集合而不是 def / defName，避免误伤玩家自己的同类物品。
    /// </summary>
    internal static class SDS_MirrorRegistry
    {
        private static readonly HashSet<Thing> mirrors = new HashSet<Thing>();

        /// <summary>地图卸载 / 读档期间允许引擎 DeSpawn 镜像；游玩期仍被拦截。</summary>
        internal static bool AllowDeSpawn;

        internal static bool Any
        {
            get { return mirrors.Count > 0; }
        }

        internal static bool IsMirror(Thing thing)
        {
            return thing != null && mirrors.Contains(thing);
        }

        internal static void Mark(Thing thing)
        {
            if (thing != null)
            {
                mirrors.Add(thing);
            }
        }

        internal static void Unmark(Thing thing)
        {
            if (thing != null)
            {
                mirrors.Remove(thing);
            }
        }

        internal static void CollectOn(Map map, List<Thing> outList)
        {
            if (map == null || outList == null)
            {
                return;
            }
            foreach (Thing thing in mirrors)
            {
                if (SDS_MirrorService.IsLiveOn(thing, map))
                {
                    outList.Add(thing);
                }
            }
        }

        /// <summary>自检专用：不过滤失效项，原样返回登记表里的全部镜像引用。</summary>
        internal static void CollectAll(List<Thing> outList)
        {
            if (outList == null)
            {
                return;
            }
            foreach (Thing thing in mirrors)
            {
                if (thing != null)
                {
                    outList.Add(thing);
                }
            }
        }

        internal static void Clear()
        {
            mirrors.Clear();
        }
    }

    /// <summary>
    /// 每图每虚拟品类一份物理镜像，统一 spawn 在隐藏金库格：
    /// 让原版 Spawned / listerThings / thingGrid / 按组 / 格查询真正看得到库存。
    /// 数量只是投影，账本永远以 SDS_Core.Ledger 为准。
    /// </summary>
    internal sealed partial class SDS_MirrorService : ISDS_MirrorQuery
    {
        private static readonly Dictionary<Map, Dictionary<SDS_StockKey, Thing>> mirrors =
            new Dictionary<Map, Dictionary<SDS_StockKey, Thing>>();

        private static readonly List<Map> tmpMaps = new List<Map>();
        private static readonly List<SDS_StockKey> tmpKeys = new List<SDS_StockKey>();
        private static readonly HashSet<SDS_StockKey> tmpKeySet = new HashSet<SDS_StockKey>();
        private static readonly List<Thing> tmpHaulMirrors = new List<Thing>();

        private static bool dirty = true;
        private static bool initialized;
        private static bool lastOutflowMode;

        private const int RetryIntervalTicks = 250;
        private const int SyncIntervalTicks = 30;
        private static int retryAtTick = -1;
        private static int nextSyncTick = -1;
        private static int lastStockVersion = int.MinValue;
        private static int lastEndpointVersion = int.MinValue;

        /// <summary>镜像表版本：投影 owner 与外部读取据此判断是否需要重建。</summary>
        public static int Version { get; private set; }

        internal static void MarkChanged()
        {
            Version++;
        }

        /// <summary>新档 / 读档 / 换 Game：清空运行期镜像表与派生缓存（旧对象属于旧游戏）。</summary>
        public static void Reset()
        {
            mirrors.Clear();
            tmpMaps.Clear();
            tmpKeys.Clear();
            tmpKeySet.Clear();
            tmpHaulMirrors.Clear();
            dirty = true;
            initialized = false;
            lastOutflowMode = false;
            SDS_MirrorRegistry.Clear();
            SDS_MirrorRegistry.AllowDeSpawn = false;
            SDS_AccessPointResolver.Invalidate();
            Building_SDS_Vault.ResetStatics();
            SDS_Exposure.Reset();
            SDS_OutputUtility.ResetDisplayCaches();
            retryAtTick = -1;
            nextSyncTick = -1;
            lastStockVersion = int.MinValue;
            lastEndpointVersion = int.MinValue;
            MarkChanged();
        }

        /// <summary>账本整体变化（读档）：下一轮同步按版本 + dirty 重新对齐。</summary>
        public static void NotifyStockChanged()
        {
            retryAtTick = -1;
            nextSyncTick = -1;
            dirty = true;
        }

        // ---------- ISDS_MirrorQuery ----------

        public IReadOnlyList<Thing> VisibleThingsOfDef(Map map, ThingDef def)
        {
            List<Thing> result = new List<Thing>();
            CopyMirrorsForDef(map, def, result);
            return result;
        }

        /// <summary>该实物是否为本 Mod 的镜像 / 代理（只读事实）。</summary>
        public bool IsMirror(Thing thing)
        {
            return thing != null && SDS_MirrorRegistry.IsMirror(thing);
        }

        int ISDS_MirrorQuery.Version
        {
            get { return Version; }
        }

        // ---------- 快照 / 只读 ----------

        public static void CollectMirrors(List<Thing> outList)
        {
            if (outList == null)
            {
                return;
            }
            outList.Clear();
            foreach (Dictionary<SDS_StockKey, Thing> dict in mirrors.Values)
            {
                if (dict == null)
                {
                    continue;
                }
                foreach (Thing thing in dict.Values)
                {
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }
                    // 存档里的镜像只是投影：先把超过 stackLimit 的显示数量夹回上限。
                    int limit = thing.def != null ? Math.Max(1, thing.def.stackLimit) : 1;
                    if (thing.stackCount > limit)
                    {
                        thing.stackCount = limit;
                        if (thing.Map != null)
                        {
                            thing.DirtyMapMesh(thing.Map);
                        }
                        dirty = true;
                        MarkChanged();
                    }
                    outList.Add(thing);
                }
            }
        }

        public static void CleanupLoadedMirrors(List<Thing> loaded)
        {
            if (loaded != null)
            {
                for (int i = 0; i < loaded.Count; i++)
                {
                    Thing thing = loaded[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }
                    SDS_MirrorRegistry.Unmark(thing);
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
            dirty = true;
            MarkChanged();
        }

        internal static void ForgetMirror(Thing mirror)
        {
            if (mirror == null)
            {
                return;
            }
            Map map = mirror.Map;
            if (map == null || !mirrors.TryGetValue(map, out Dictionary<SDS_StockKey, Thing> dict)
                || dict == null)
            {
                return;
            }
            SDS_StockKey found = null;
            foreach (KeyValuePair<SDS_StockKey, Thing> pair in dict)
            {
                if (ReferenceEquals(pair.Value, mirror))
                {
                    found = pair.Key;
                    break;
                }
            }
            if (found != null)
            {
                dict.Remove(found);
                MarkChanged();
            }
        }

        public static bool HasAnyMirrorOn(Map map)
        {
            return map != null && mirrors.TryGetValue(map, out Dictionary<SDS_StockKey, Thing> dict)
                && dict != null && dict.Count > 0;
        }

        public static bool AnyMirrorInWorld
        {
            get
            {
                foreach (KeyValuePair<Map, Dictionary<SDS_StockKey, Thing>> pair in mirrors)
                {
                    if (pair.Value != null && pair.Value.Count > 0)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public static void CopyMirrorsOn(Map map, List<Thing> outList)
        {
            if (outList == null)
            {
                return;
            }
            outList.Clear();
            if (map == null || !mirrors.TryGetValue(map, out Dictionary<SDS_StockKey, Thing> dict)
                || dict == null)
            {
                return;
            }
            foreach (Thing thing in dict.Values)
            {
                if (IsLiveOn(thing, map))
                {
                    outList.Add(thing);
                }
            }
        }

        public static void CopyMirrorKeys(Map map, List<SDS_StockKey> outList)
        {
            if (outList == null)
            {
                return;
            }
            outList.Clear();
            if (map == null || !mirrors.TryGetValue(map, out Dictionary<SDS_StockKey, Thing> dict)
                || dict == null)
            {
                return;
            }
            foreach (KeyValuePair<SDS_StockKey, Thing> pair in dict)
            {
                Thing thing = pair.Value;
                if (pair.Key != null && thing != null && !thing.Destroyed && thing.Spawned
                    && thing.Map == map)
                {
                    outList.Add(pair.Key);
                }
            }
        }

        /// <summary>单 def 候选：该图全部同 def 品类的镜像（材质 / 品质会拆成多条 key）。</summary>
        public static void CopyMirrorsForDef(Map map, ThingDef def, List<Thing> outList)
        {
            if (outList == null)
            {
                return;
            }
            outList.Clear();
            if (map == null || def == null
                || !mirrors.TryGetValue(map, out Dictionary<SDS_StockKey, Thing> dict) || dict == null)
            {
                return;
            }
            foreach (KeyValuePair<SDS_StockKey, Thing> pair in dict)
            {
                Thing thing = pair.Value;
                if (thing != null && !thing.Destroyed && thing.Spawned && thing.Map == map
                    && pair.Key != null && pair.Key.thingDef == def)
                {
                    outList.Add(thing);
                }
            }
        }

        /// <summary>镜像有效性的唯一判定：存活、已 spawn、属于指定且未卸载的地图。</summary>
        internal static bool IsLiveOn(Thing thing, Map map)
        {
            return thing != null && !thing.Destroyed && thing.Spawned
                && map != null && !map.Disposed && thing.Map == map;
        }

        public static bool TryGetMirror(Map map, SDS_StockKey key, out Thing mirror)
        {
            mirror = null;
            if (map == null || key == null
                || !mirrors.TryGetValue(map, out Dictionary<SDS_StockKey, Thing> dict) || dict == null)
            {
                return false;
            }
            if (!dict.TryGetValue(key, out mirror) || !IsLiveOn(mirror, map))
            {
                mirror = null;
                return false;
            }
            return true;
        }

        /// <summary>原版 ResourceCounter 已数到的镜像量（含同格属于多个 SlotGroup 的重复计数）。</summary>
        public static long MirrorExposureOnResourceCounter(Map map, SDS_StockKey key)
        {
            if (map == null || key == null || !TryGetMirror(map, key, out Thing mirror))
            {
                return 0L;
            }
            if (mirror.def == null || !mirror.def.EverStorable(false)
                || mirror.IsNotFresh() || mirror.PositionHeld.Fogged(map))
            {
                return 0L;
            }
            HaulDestinationManager manager = map.haulDestinationManager;
            if (manager == null)
            {
                return 0L;
            }
            IntVec3 cell = mirror.PositionHeld;
            List<SlotGroup> groups = manager.AllGroupsListForReading;
            int containing = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                SlotGroup group = groups[i];
                if (group != null && group.CellsList.Contains(cell))
                {
                    containing++;
                }
            }
            return containing <= 0 ? 0L : (long)containing * mirror.stackCount;
        }

        public static void NotifyVaultRelocated(Map map)
        {
            if (map == null)
            {
                return;
            }
            RemoveMap(map);
            MarkChanged();
            dirty = true;
        }

        /// <summary>该图镜像中会被原版 WealthWatcher 算到的市值。</summary>
        public static float MirrorWealthOn(Map map)
        {
            if (map == null || !mirrors.TryGetValue(map, out Dictionary<SDS_StockKey, Thing> dict)
                || dict == null)
            {
                return 0f;
            }
            float sum = 0f;
            foreach (Thing thing in dict.Values)
            {
                if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != map)
                {
                    continue;
                }
                if (thing.def == null || !thing.def.EverHaulable || thing.PositionHeld.Fogged(map))
                {
                    continue;
                }
                sum += thing.MarketValue * thing.stackCount;
            }
            return sum;
        }

        private static void ScheduleRetry()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            int due = now + RetryIntervalTicks;
            if (retryAtTick < 0 || due < retryAtTick)
            {
                retryAtTick = due;
            }
        }

        /// <summary>由 SDS_VanillaState.Tick 每 30 tick 调用；无变化时 O(1) 返回。</summary>
        public static void SyncIfDirty()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (nextSyncTick >= 0 && now < nextSyncTick)
            {
                return;
            }
            nextSyncTick = now + SyncIntervalTicks;

            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null)
            {
                return;
            }
            // W52：设置切换 / 开启期间刷新镜像 haulable 状态；30 tick 上限，允许延迟。
            bool outflow = SDS_Config.AllowPriorityOutflow;
            bool outflowChanged = outflow != lastOutflowMode;
            lastOutflowMode = outflow;
            bool need = dirty || !initialized || outflowChanged;
            int stockVersion = ledger.StockVersion;
            int endpointVersion = SDS_Rules.EndpointVersion;
            if (stockVersion != lastStockVersion || endpointVersion != lastEndpointVersion)
            {
                lastStockVersion = stockVersion;
                lastEndpointVersion = endpointVersion;
                need = true;
            }
            if (retryAtTick >= 0 && now >= retryAtTick)
            {
                retryAtTick = -1;
                need = true;
            }
            // outflowChanged 必然置 need，因此这里只需看 need 与 outflow。
            if (!need && !outflow)
            {
                return;
            }
            dirty = false;
            initialized = true;
            try
            {
                if (need)
                {
                    SyncAll();
                }
                if (outflow || outflowChanged)
                {
                    RefreshHaulableMirrors();
                }
            }
            catch (Exception e)
            {
                dirty = true;
                SDS_Diag.ErrorOnce("物理镜像同步失败（已忽略，账本不受影响）", e);
            }
        }

        /// <summary>
        /// W52：把镜像重新送入原版 ListerHaulables 判定。
        /// 关闭流出时移除候选；开启时只由原版 IsInValidBestStorage 决定是否保留。
        /// </summary>
        public static void RefreshHaulableMirrors()
        {
            if (Current.ProgramState != ProgramState.Playing || !SDS_MirrorRegistry.Any)
            {
                return;
            }
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return;
            }
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null || map.Disposed || map.listerHaulables == null)
                {
                    continue;
                }
                tmpHaulMirrors.Clear();
                SDS_MirrorRegistry.CollectOn(map, tmpHaulMirrors);
                for (int j = 0; j < tmpHaulMirrors.Count; j++)
                {
                    Thing mirror = tmpHaulMirrors[j];
                    if (mirror == null)
                    {
                        continue;
                    }
                    map.listerHaulables.Notify_DeSpawned(mirror);
                    map.listerHaulables.Notify_AddedThing(mirror);
                }
                tmpHaulMirrors.Clear();
            }
        }

        /// <summary>按当前账本重建所有图的镜像；只增删 / 改数量，不读取镜像当账本。</summary>
        public static void SyncAll()
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            List<Map> maps = Find.Maps;
            if (ledger == null || maps == null)
            {
                return;
            }

            tmpMaps.Clear();
            foreach (KeyValuePair<Map, Dictionary<SDS_StockKey, Thing>> pair in mirrors)
            {
                if (pair.Key == null || !maps.Contains(pair.Key))
                {
                    tmpMaps.Add(pair.Key);
                }
            }
            for (int i = 0; i < tmpMaps.Count; i++)
            {
                RemoveMap(tmpMaps[i]);
            }

            if (!SDS_Rules.Available)
            {
                RemoveAll();
                return;
            }

            List<SDS_StockEntry> snapshot = ledger.Snapshot(0);
            Dictionary<SDS_StockKey, SDS_StockEntry> entryLookup =
                new Dictionary<SDS_StockKey, SDS_StockEntry>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                SDS_StockEntry entry = snapshot[i];
                if (entry != null && entry.key != null)
                {
                    entryLookup[entry.key] = entry;
                }
            }
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null || map.Disposed || map.listerThings == null)
                {
                    continue;
                }
                if (!SDS_Exposure.MapExposesGrid(map))
                {
                    RemoveMap(map);
                    Building_SDS_Vault.HandleNoAnchor(map);
                    continue;
                }
                Building_SDS_Vault.ClearNoAnchorTimer(map);

                Thing anchorThing;
                if (!SDS_Exposure.TryGetExposureAnchor(map, out anchorThing) || anchorThing == null)
                {
                    RemoveMap(map);
                    Building_SDS_Vault.HandleNoAnchor(map);
                    continue;
                }

                // W37：锚点优先沿用金库记录的端点，避免多核心 / 多人端点间来回跳。
                Building_SDS_Vault currentVault = Building_SDS_Vault.GetOn(map);
                if (currentVault != null
                    && Building_SDS_Vault.IsAnchorUsable(currentVault.AnchorThing, map))
                {
                    Thing stored = currentVault.AnchorThing;
                    if (!(stored is Pawn && !(anchorThing is Pawn)))
                    {
                        anchorThing = stored;
                    }
                }

                IntVec3 preferred = PreferredMirrorCell(map, anchorThing);
                Building_SDS_Vault vault = Building_SDS_Vault.EnsureOn(map, preferred, anchorThing,
                    out IntVec3 cell);
                if (vault == null || !cell.IsValid)
                {
                    SDS_Diag.Fail("镜像/金库落点不可用", SDS_Diag.Ctx(map, null, null, null,
                        "cell=" + (cell.IsValid ? cell.ToString() : "无效")));
                    RemoveMap(map);
                    ScheduleRetry();
                    continue;
                }

                Dictionary<SDS_StockKey, Thing> dict = GetOrCreateMap(map);

                tmpKeys.Clear();
                tmpKeySet.Clear();
                for (int j = 0; j < snapshot.Count; j++)
                {
                    SDS_StockEntry entry = snapshot[j];
                    if (entry == null || entry.key == null || entry.key.thingDef == null
                        || entry.key.preserved || entry.count <= 0)
                    {
                        continue;
                    }
                    // 禁用品类不投影：不进 tmpKeySet，让下面的 stale 批量移除统一收口。
                    if (ledger != null && ledger.IsForbidden(entry.key))
                    {
                        continue;
                    }
                    tmpKeys.Add(entry.key);
                    tmpKeySet.Add(entry.key);
                }

                List<SDS_StockKey> stale = null;
                foreach (KeyValuePair<SDS_StockKey, Thing> pair in dict)
                {
                    if (!tmpKeySet.Contains(pair.Key))
                    {
                        if (stale == null)
                        {
                            stale = new List<SDS_StockKey>();
                        }
                        stale.Add(pair.Key);
                    }
                }
                if (stale != null)
                {
                    RemoveMirrorsBatch(map, dict, stale, "mirror-stale");
                }

                for (int j = 0; j < tmpKeys.Count; j++)
                {
                    SDS_StockKey key = tmpKeys[j];
                    SDS_StockEntry entry;
                    entryLookup.TryGetValue(key, out entry);
                    EnsureMirrorFor(map, dict, key, entry, cell);
                }
            }
        }

        /// <summary>
        /// 批量移除镜像：同一批 key 只扫描一次 Pawn 收口 Job，再逐个 Unmark / 移除。
        /// 禁用 / 超期 / 结构撤离等“多 key 同时失效”路径必须走这里，避免逐 key 全量扫描。
        /// </summary>
        private static void RemoveMirrorsBatch(Map map, Dictionary<SDS_StockKey, Thing> dict,
            List<SDS_StockKey> keys, string reason)
        {
            if (dict == null || keys == null || keys.Count == 0)
            {
                return;
            }
            List<SDS_StockKey> presentKeys = new List<SDS_StockKey>(keys.Count);
            List<Thing> things = new List<Thing>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                if (dict.TryGetValue(keys[i], out Thing thing) && thing != null)
                {
                    presentKeys.Add(keys[i]);
                    things.Add(thing);
                }
            }
            if (things.Count == 0)
            {
                return;
            }
            SDS_MirrorJobTargets.CloseReferencing(map, things, reason);
            for (int i = 0; i < presentKeys.Count; i++)
            {
                dict.Remove(presentKeys[i]);
                RemoveMirror(things[i]);
            }
        }

        /// <summary>调用方 SyncAll 已过滤 forbidden / count≤0；这里只负责“存活则更新，否则重建”。</summary>
        private static void EnsureMirrorFor(Map map, Dictionary<SDS_StockKey, Thing> dict,
            SDS_StockKey key, SDS_StockEntry entry, IntVec3 cell)
        {
            if (key == null || key.thingDef == null || entry == null)
            {
                return;
            }
            int count = (int)Math.Min(entry.count, int.MaxValue);
            if (dict.TryGetValue(key, out Thing existing) && IsLiveOn(existing, map))
            {
                UpdateMirror(existing, key, count, map, entry);
                return;
            }
            dict.Remove(key);
            if (!cell.IsValid)
            {
                Building_SDS_Vault vault = Building_SDS_Vault.GetOn(map);
                cell = vault != null ? vault.Position : IntVec3.Invalid;
            }
            if (!cell.IsValid)
            {
                // 无法重建：旧镜像已无效，按原行为先收口再移除，等待下一次重试。
                if (existing != null)
                {
                    SDS_MirrorJobTargets.CloseReferencing(map, existing, "mirror-replace-no-cell");
                    RemoveMirror(existing);
                }
                ScheduleRetry();
                return;
            }
            Thing spawned = SpawnMirror(map, key, count, cell);
            if (spawned == null)
            {
                if (existing != null)
                {
                    SDS_MirrorJobTargets.CloseReferencing(map, existing, "mirror-replace-failed");
                    RemoveMirror(existing);
                }
                ScheduleRetry();
                return;
            }
            dict[key] = spawned;
            if (existing == null)
            {
                return;
            }
            // 同 key 替换：先让 Job 精确改指新镜像，再移除旧镜像；失败才回退关闭。
            if (!SDS_MirrorJobTargets.RemapReferencing(map, existing, spawned))
            {
                SDS_MirrorJobTargets.CloseReferencing(map, existing, "mirror-replace-fallback");
            }
            RemoveMirror(existing);
        }

        private static Dictionary<SDS_StockKey, Thing> GetOrCreateMap(Map map)
        {
            if (!mirrors.TryGetValue(map, out Dictionary<SDS_StockKey, Thing> dict) || dict == null)
            {
                dict = new Dictionary<SDS_StockKey, Thing>();
                mirrors[map] = dict;
            }
            return dict;
        }

        /// <summary>镜像 spawn 后：注销 tick、冻结腐烂（不清零，腐烂进度是账本堆平均值）。</summary>
        public static void OnMirrorSpawned(Thing thing)
        {
            if (thing == null || !SDS_MirrorRegistry.IsMirror(thing))
            {
                return;
            }
            if (Find.TickManager != null)
            {
                Find.TickManager.DeRegisterAllTickabilityFor(thing);
            }
            CompRottable rottable = thing.TryGetComp<CompRottable>();
            if (rottable != null)
            {
                rottable.disabled = true;
            }
        }

        private static Thing SpawnMirror(Map map, SDS_StockKey key, int count, IntVec3 cell)
        {
            if (cell.GetMaxItemsAllowedInCell(map) <= cell.GetItemCount(map))
            {
                SDS_Diag.Fail("镜像/落点已满", SDS_Diag.Ctx(map, null, null, null,
                    "cell=" + cell + " def=" + (key.thingDef != null ? key.thingDef.defName : "?")));
                return null;
            }
            Thing thing = SDS_ProxyFactory.CreateMirrorThing(key);
            if (thing == null)
            {
                SDS_Diag.Fail("镜像/品类不可重建", SDS_Diag.Ctx(map, null, null, null,
                    "def=" + (key.thingDef != null ? key.thingDef.defName : "?")));
                return null;
            }
            int stackLimit = thing.def != null ? thing.def.stackLimit : 0;
            if (stackLimit <= 0)
            {
                SDS_Diag.Fail("镜像/堆上限非法", SDS_Diag.Ctx(map, null, thing, null,
                    "stackLimit=" + stackLimit));
                SDS_MirrorRegistry.Unmark(thing);
                thing.Destroy(DestroyMode.Vanish);
                return null;
            }
            thing.stackCount = Math.Min(count, stackLimit);
            SDS_MirrorRegistry.Mark(thing);
            try
            {
                GenSpawn.Spawn(thing, cell, map);
                if (thing.Destroyed || !thing.Spawned)
                {
                    SDS_MirrorRegistry.Unmark(thing);
                    return null;
                }
                if (thing.stackCount != count)
                {
                    thing.stackCount = count;
                    thing.DirtyMapMesh(map);
                    MarkChanged();
                }
                return thing;
            }
            catch (Exception e)
            {
                SDS_MirrorRegistry.Unmark(thing);
                if (!thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
                SDS_Diag.ErrorOnce("物理镜像 spawn 失败（已忽略，账本不受影响）", e);
                return null;
            }
        }

        private static void UpdateMirror(Thing thing, SDS_StockKey key, int count, Map map,
            SDS_StockEntry entry)
        {
            if (thing == null || thing.Destroyed)
            {
                return;
            }
            SDS_MirrorRegistry.Mark(thing);
            SDS_Core.ApplyVitals(thing, entry.avgHitPoints, entry.avgRotProgress);
            if (thing.stackCount != count)
            {
                thing.stackCount = count;
                if (thing.def != null)
                {
                    thing.DirtyMapMesh(map);
                }
                MarkChanged();
            }
        }

        /// <summary>
        /// 移除镜像：调用方必须先对引用它的 Job 收口（CloseReferencing / RemapReferencing）；
        /// 本方法只负责 Unmark / DeSpawn / Destroy，避免在移除内部隐式扫描 Pawn。
        /// </summary>
        private static void RemoveMirror(Thing thing)
        {
            if (thing == null)
            {
                return;
            }
            // 先摘标记，再销毁：Destroy guard 只拦"仍被标记"的镜像。
            SDS_MirrorRegistry.Unmark(thing);
            if (thing.Destroyed)
            {
                return;
            }
            if (thing.Spawned)
            {
                thing.DeSpawnOrDeselect(DestroyMode.Vanish);
            }
            else
            {
                thing.Destroy(DestroyMode.Vanish);
            }
            MarkChanged();
        }

        private static void RemoveMap(Map map)
        {
            if (map == null)
            {
                return;
            }
            if (!mirrors.TryGetValue(map, out Dictionary<SDS_StockKey, Thing> dict) || dict == null)
            {
                return;
            }
            mirrors.Remove(map);
            // W60：同一批镜像只扫描一次 Pawn，先结束 / 出队引用它们的 Job。
            SDS_MirrorJobTargets.CloseReferencing(map, new List<Thing>(dict.Values), "mirror-map-remove");
            foreach (Thing thing in dict.Values)
            {
                RemoveMirror(thing);
            }
            dict.Clear();
        }

        private static void RemoveAll()
        {
            tmpMaps.Clear();
            foreach (Map map in mirrors.Keys)
            {
                tmpMaps.Add(map);
            }
            for (int i = 0; i < tmpMaps.Count; i++)
            {
                RemoveMap(tmpMaps[i]);
            }
        }

        /// <summary>
        /// 结构离场前撤离本图全部物理镜像（账本零写入）：
        /// 金库 DeSpawn / 地图卸载必须先撤投影，否则原版搬迁循环会把镜像 SplitOff 当成取货扣账。
        /// </summary>
        public static void EvacuateForStructureChange(Map map, string reason)
        {
            if (map == null || map.Disposed)
            {
                return;
            }
            List<Thing> evac = new List<Thing>();
            SDS_MirrorRegistry.CollectOn(map, evac);
            mirrors.TryGetValue(map, out Dictionary<SDS_StockKey, Thing> dict);
            if (dict != null)
            {
                foreach (Thing thing in dict.Values)
                {
                    if (thing != null && !evac.Contains(thing))
                    {
                        evac.Add(thing);
                    }
                }
            }
            mirrors.Remove(map);
            // W60：结构撤离会销毁本图全部镜像，先结束 / 出队引用它们的 Job。
            // 地图卸载本身会丢弃全部 Pawn / Job，不在 Deinit 中反向操作 Job。
            if (reason != "map-deinit")
            {
                SDS_MirrorJobTargets.CloseReferencing(map, evac, "mirror-evacuate:" + reason);
            }
            int count = 0;
            for (int i = 0; i < evac.Count; i++)
            {
                Thing thing = evac[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                try
                {
                    SDS_MirrorRegistry.Unmark(thing);
                    thing.Destroy(DestroyMode.Vanish);
                    count++;
                }
                catch (Exception e)
                {
                    SDS_Diag.Fail("镜像/撤离失败", SDS_Diag.Ctx(map, null, thing, null,
                        "reason=" + reason), e);
                }
            }
            if (dict != null)
            {
                dict.Clear();
            }
            if (count > 0)
            {
                SDS_Diag.Warn("镜像/结构撤离", SDS_Diag.Ctx(map, null, null, null,
                    "reason=" + reason + " count=" + count));
            }
            retryAtTick = -1;
            dirty = true;
            MarkChanged();
        }

        private static IntVec3 PreferredMirrorCell(Map map, Thing anchorThing)
        {
            IntVec3 baseCell = anchorThing != null ? anchorThing.Position : map.Center;
            Building building = anchorThing as Building;
            if (building != null && building.def != null && building.def.hasInteractionCell)
            {
                baseCell = building.InteractionCell;
            }
            return baseCell.IsValid && baseCell.InBounds(map) ? baseCell : map.Center;
        }
    }

    /// <summary>
    /// 镜像物理原语（ISDS_MirrorPhysical）：只做形态转换，不碰账本；扣账 / 回滚由 Core 事务负责。
    /// </summary>
    internal sealed partial class SDS_MirrorPhysicalOps : ISDS_MirrorPhysical
    {
        public bool TryTakePhysical(SDS_StockKey key, Thing mirror, int count, out Thing made)
        {
            made = null;
            if (!SDS_Rules.Available || key == null || mirror == null || count <= 0
                || !SDS_MirrorRegistry.IsMirror(mirror))
            {
                return false;
            }
            int mirrorCount = mirror.stackCount;
            if (count > mirrorCount)
            {
                return false;
            }
            Thing piece = SDS_OutputUtility.MakeDisplayThing(key);
            if (piece == null)
            {
                return false;
            }
            piece.stackCount = count;
            mirror.stackCount = mirrorCount - count;
            if (mirror.Map != null)
            {
                mirror.DirtyMapMesh(mirror.Map);
            }
            // 扣账前捕获的 vitals 就是镜像当前状态（镜像由同步写成账本堆平均值）。
            if (piece.def != null && piece.def.useHitPoints && mirror.HitPoints > 0)
            {
                piece.HitPoints = mirror.HitPoints;
            }
            CompRottable sourceRot = mirror.TryGetComp<CompRottable>();
            CompRottable pieceRot = piece.TryGetComp<CompRottable>();
            if (sourceRot != null && pieceRot != null)
            {
                pieceRot.RotProgress = sourceRot.RotProgress;
            }
            SDS_MirrorService.MarkChanged();
            made = piece;
            return true;
        }

        public bool TryTakeWholePhysical(SDS_StockKey key, Thing mirror, out Thing made)
        {
            made = null;
            if (!SDS_Rules.Available || mirror == null || !SDS_MirrorRegistry.IsMirror(mirror))
            {
                return false;
            }
            SDS_MirrorService.ForgetMirror(mirror);
            SDS_MirrorRegistry.Unmark(mirror);
            CompRottable rottable = mirror.TryGetComp<CompRottable>();
            if (rottable != null)
            {
                rottable.disabled = false;
            }
            made = mirror;
            if (mirror.Spawned)
            {
                mirror.DeSpawnOrDeselect(DestroyMode.Vanish);
            }
            return true;
        }
    }

    /// <summary>镜像取用的唯一外层入口：只转发 Core 的镜像事务。</summary>
    internal static class SDS_TakeService
    {
        internal static bool TryTake(Thing mirror, int count, out Thing taken)
        {
            taken = null;
            if (mirror == null || count <= 0 || !SDS_Rules.Available)
            {
                return false;
            }
            return SDS_Rules.TryTakeMirror(mirror, count, SDS_Host.MirrorPhysical, out taken)
                && taken != null;
        }

        /// <summary>取出的实物最终没交出去：先回账本；回不去才按调用方给定格落地（不吞物品）。</summary>
        internal static void ReturnToGridOrDrop(Thing thing, Map map, IntVec3 cell)
        {
            if (thing == null || thing.Destroyed)
            {
                return;
            }
            if (SDS_Rules.ReturnThing(thing, SDS_StockChannel.Virtual, map, null, SDS_ReturnFallback.None))
            {
                return;
            }
            if (thing.Destroyed || map == null)
            {
                return;
            }
            SDS_PlacementUtility.TryPlaceOrSpawn(thing, cell, map);
        }
    }

    /// <summary>
    /// 镜像参与 TryAbsorbStack 的唯一翻译：
    /// 镜像作为被吸收方 = 先取货再交原版吸收；镜像作为吸收方 = 拒绝（存入不走合并语义）。
    /// </summary>
    internal static class SDS_MirrorAbsorb
    {
        private const int MaxRounds = 256;

        internal static bool TryHandle(Thing receiver, Thing other, bool respectStackLimit, out bool result)
        {
            result = false;
            bool receiverMirror = SDS_MirrorRegistry.IsMirror(receiver);
            bool otherMirror = SDS_MirrorRegistry.IsMirror(other);
            if (!receiverMirror && !otherMirror)
            {
                return false;
            }
            if (receiverMirror)
            {
                return true;
            }
            try
            {
                if (receiver == null || receiver.Destroyed || other == null || other.Destroyed
                    || other.stackCount <= 0 || !receiver.CanStackWith(other))
                {
                    return true;
                }
                int want = ThingUtility.TryAbsorbStackNumToTake(receiver, other, respectStackLimit);
                int stackLimit = other.def != null ? Math.Max(1, other.def.stackLimit) : want;
                for (int round = 0; round < MaxRounds && want > 0; round++)
                {
                    if (receiver.Destroyed || other.Destroyed || other.stackCount <= 0)
                    {
                        break;
                    }
                    int count = Math.Min(want, stackLimit);
                    if (!SDS_TakeService.TryTake(other, count, out Thing real)
                        || real == null || real.Destroyed)
                    {
                        break;
                    }
                    int taken = real.stackCount;
                    if (!receiver.TryAbsorbStack(real, respectStackLimit))
                    {
                        SDS_TakeService.ReturnToGridOrDrop(real, receiver.MapHeld, receiver.PositionHeld);
                        break;
                    }
                    want -= Math.Max(0, taken);
                }
                result = other.Destroyed || !SDS_MirrorRegistry.IsMirror(other);
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.Fail("取货/合并取出异常", SDS_Diag.Ctx(
                    receiver != null ? receiver.Map : null, null, other, null), e);
                result = false;
                return true;
            }
        }
    }

    /// <summary>镜像入手：任何 Job 对镜像 TryStartCarry 时，从账本扣数并把实物直接放进手上。</summary>
    internal static class SDS_MirrorCarry
    {
        internal static int TakeIntoCarry(Pawn_CarryTracker tracker, Thing mirror, int count, bool reserve)
        {
            if (tracker == null || mirror == null)
            {
                return 0;
            }
            Pawn pawn = tracker.pawn;
            if (pawn == null || pawn.Map == null || pawn.Dead || pawn.Downed)
            {
                return 0;
            }
            // S18：非玩家派系不允许从隐藏库存直接入手。
            if (SDS_AccessRules.BlocksNonPlayer(pawn))
            {
                return 0;
            }
            // W54：取货必须发生在端口（建筑端点 / 自身端口）。带芯片小人自身就是端口；
            // 其余小人必须站在已解析访问点的命中范围内，否则说明有路径按镜像真身坐标
            // （隐藏金库）来取——直接拒绝，绝不从金库隔空取货。
            if (!SDS_ImplantUtility.IsPortOperational(pawn))
            {
                if (!SDS_AccessPointResolver.TryGetNearestReachable(pawn, out SDS_AccessPoint point)
                    || point == null
                    || (pawn.Position - point.cell).LengthHorizontalSquared > 2)
                {
                    SDS_Diag.Warn("镜像取货点不在端点范围内（已拒绝取货）",
                        SDS_Diag.Ctx(pawn.Map, pawn, mirror));
                    return 0;
                }
            }
            int handSpace = tracker.AvailableStackSpace(mirror.def);
            int want = Math.Min(count, Math.Min(handSpace, mirror.stackCount));
            if (want <= 0)
            {
                return 0;
            }
            // W52：镜像整品类代表账本量；只按 total − reserved 取，绝不抢交易 / 编成 / 账单预留。
            if (!SDS_Rules.TryGetStockKey(mirror, out SDS_StockKey key)
                || key == null || key.preserved)
            {
                return 0;
            }
            long available = SDS_Availability.GetAvailableCount(key);
            if (available <= 0L)
            {
                return 0;
            }
            want = SDS_Availability.ClampTakeToAvailable(want, available);
            if (want <= 0)
            {
                return 0;
            }
            if (!SDS_TakeService.TryTake(mirror, want, out Thing real) || real == null || real.Destroyed)
            {
                return 0;
            }
            int taken = tracker.TryStartCarry(real, real.stackCount, reserve);
            Thing carried = tracker.CarriedThing;
            Job job = pawn.jobs != null ? pawn.jobs.curJob : null;
            if (taken > 0 && carried != null && job != null)
            {
                // 把 Job 对镜像的整批预留换成对手上实物的预留，防止落地后被 PUAH / 搬运工拿走。
                SDS_ReclaimRegistry.TrackHand(job, carried);
                if (pawn.CanReserve(carried, 1, -1, null, false))
                {
                    pawn.Reserve(carried, job);
                }
                if (mirror.Spawned && !mirror.Destroyed
                    && pawn.Map.reservationManager.ReservedBy(mirror, pawn, job))
                {
                    pawn.Map.reservationManager.Release(mirror, pawn, job);
                }
            }
            // 手上只拿得下一部分：余量回充账本，绝不落地。
            if (real != null && !real.Destroyed && real.stackCount > 0 && real.holdingOwner == null)
            {
                SDS_Rules.ReturnThing(real, SDS_StockChannel.Virtual, pawn.Map, pawn,
                    SDS_ReturnFallback.PawnCarry);
            }
            return taken;
        }
    }

    /// <summary>
    /// W19：镜像被外部 Destroy 时 fail-closed —— 拒绝销毁并一次性告警。
    /// 内部清理（RemoveMirror / EvacuateForStructureChange / 地图卸载）必须先 Unmark，
    /// 因此不会进入本判定；账本不再按“镜像当前数量”整品类扣账。
    /// </summary>
    internal static class SDS_MirrorConsume
    {
        // RULE:W19
        internal static bool Allow(Thing thing)
        {
            if (thing == null || !SDS_MirrorRegistry.IsMirror(thing))
            {
                return true;
            }
            if (SDS_MirrorRegistry.AllowDeSpawn || Current.ProgramState != ProgramState.Playing)
            {
                return true;
            }
            if (thing.Destroyed || thing.def == null || !thing.def.destroyable)
            {
                return true;
            }
            if (!thing.Spawned)
            {
                // 未 spawn 的镜像销毁：只摘标记，不涉及账本（与 3.2 行为一致）。
                SDS_MirrorRegistry.Unmark(thing);
                return true;
            }
            if (thing.stackCount <= 0)
            {
                SDS_MirrorRegistry.Unmark(thing);
                return true;
            }
            Log.WarningOnce("[超维存储] 外部销毁镜像被拒绝（W19 fail-closed）：" + thing.ToStringSafe(),
                thing.thingIDNumber ^ 0x5D1901);
            return false;
        }
    }
}
