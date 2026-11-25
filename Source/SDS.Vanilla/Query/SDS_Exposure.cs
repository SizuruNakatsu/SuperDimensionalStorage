using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// "看得见"的唯一计数口径：目标量 = 本图可用端点 ? 账本量 : 0；补丁增量 = 目标量 − 原版已数到的物理曝光。
    /// 资源条 / 财富 / 可达性的读取补丁只消费这里，不各自推导"该加多少 / 该减多少"。
    /// </summary>
    internal static class SDS_Exposure
    {
        private const int CounterMinIntervalTicks = 30;

        private struct ExposureVersions
        {
            public int tick;
            public int mirror;
            public int endpoint;
            public int stock;
            public int reservation;
            public int filter;

            public bool SameExposureAs(ExposureVersions other)
            {
                return mirror == other.mirror && endpoint == other.endpoint && stock == other.stock
                    && reservation == other.reservation && filter == other.filter;
            }
        }

        private static readonly Dictionary<Map, int> counterRefreshTick = new Dictionary<Map, int>();
        private static readonly Dictionary<Map, ExposureVersions> counterVersions =
            new Dictionary<Map, ExposureVersions>();

        private static readonly List<SDS_StockKey> injectedKeys = new List<SDS_StockKey>();
        private static readonly List<SDS_StockKey> injectedMirrorKeys = new List<SDS_StockKey>();

        internal static void Reset()
        {
            counterRefreshTick.Clear();
            counterVersions.Clear();
            injectedKeys.Clear();
            injectedMirrorKeys.Clear();
        }

        // ---------- 暴露口径 ----------

        /// <summary>该图是否把超维库存暴露给原版枚举：G + 本图存在可用端点（建筑或 B 端口）。</summary>
        internal static bool MapExposesGrid(Map map)
        {
            ISDS_PortProvider ports = SDS_Host.Ports;
            return ports != null && map != null && !map.Disposed && ports.IsLocalUsable(map);
        }

        /// <summary>本图的暴露锚点：优先建筑端点，没有则取带可用芯片的小人 / 机械体。</summary>
        internal static bool TryGetExposureAnchor(Map map, out Thing anchor)
        {
            anchor = null;
            ISDS_PortProvider ports = SDS_Host.Ports;
            if (ports == null || map == null || !SDS_Rules.Available)
            {
                return false;
            }
            SDS_EndpointQuery query = default;
            query.op = SDS_EndpointOp.Withdraw;
            query.map = map;
            query.requireReach = false;
            query.allowSelf = false;
            query.requireStock = false;
            List<ISDS_Port> candidates = new List<ISDS_Port>();
            ports.AppendCandidates(query, candidates);
            Thing bestBuilding = null;
            Pawn firstPawn = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                ISDS_Port port = candidates[i];
                if (port == null || !port.Operational || port.Thing == null)
                {
                    continue;
                }
                if (port.Pawn == null)
                {
                    // 与旧 CanonicalEndpointOn 同口径：多个建筑端点时取 thingIDNumber 最小者，
                    // 避免注册顺序变化导致金库锚点在核心 / 锚点之间漂移。
                    if (bestBuilding == null
                        || port.Thing.thingIDNumber < bestBuilding.thingIDNumber)
                    {
                        bestBuilding = port.Thing;
                    }
                    continue;
                }
                if (firstPawn == null)
                {
                    firstPawn = port.Pawn;
                }
            }
            if (bestBuilding != null)
            {
                anchor = bestBuilding;
                return true;
            }
            if (firstPawn != null)
            {
                anchor = firstPawn;
                return true;
            }
            return false;
        }

        /// <summary>规范暴露图：优先玩家主基地，否则第一张暴露图（同一份库存只在这里补一次）。</summary>
        internal static Map CanonicalExposureMap()
        {
            if (Current.Game == null)
            {
                return null;
            }
            try
            {
                Map home = Find.AnyPlayerHomeMap;
                if (home != null && MapExposesGrid(home))
                {
                    return home;
                }
                List<Map> maps = Find.Maps;
                if (maps == null)
                {
                    return null;
                }
                for (int i = 0; i < maps.Count; i++)
                {
                    if (maps[i] != null && MapExposesGrid(maps[i]))
                    {
                        return maps[i];
                    }
                }
            }
            catch (Exception e)
            {
                // 宿主未就绪（读档 / 测试壳）：本次按"无规范图"处理，不阻断任何读取路径。
                SDS_Diag.ErrorOnce("暴露/规范地图查询失败（本次按无暴露处理）", e);
            }
            return null;
        }

        // ---------- 资源条 ----------

        /// <summary>ResourceCounter 的单品类校正量。</summary>
        internal static long CounterAdjust(Map map, SDS_StockKey key, bool exposed)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (map == null || key == null || ledger == null)
            {
                return 0L;
            }
            long expected = exposed ? ledger.GetCount(key) : 0L;
            long physical = SDS_MirrorService.MirrorExposureOnResourceCounter(map, key);
            // 镜像存在但原版没有数到（迷雾 / 不新鲜）时目标为 0，不按账本兜底。
            if (physical == 0L && SDS_MirrorService.TryGetMirror(map, key, out _))
            {
                return 0L;
            }
            return expected - physical;
        }

        /// <summary>本图资源条里为某个 def 注入的总量（虚拟 + 保真，def 级唯一口径）。</summary>
        internal static long InjectedOnCounter(Map map, ThingDef def, bool exposed)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (map == null || def == null || ledger == null)
            {
                return 0L;
            }
            long total = 0L;
            ledger.CopyKeysForDef(def, injectedKeys);
            for (int i = 0; i < injectedKeys.Count; i++)
            {
                total += CounterAdjust(map, injectedKeys[i], exposed);
            }
            SDS_MirrorService.CopyMirrorKeys(map, injectedMirrorKeys);
            for (int i = 0; i < injectedMirrorKeys.Count; i++)
            {
                SDS_StockKey key = injectedMirrorKeys[i];
                if (key == null || key.thingDef != def || ContainsKey(injectedKeys, key))
                {
                    continue;
                }
                total += CounterAdjust(map, key, exposed);
            }
            if (exposed)
            {
                total += PreservedCountOfDef(def);
            }
            return total;
        }

        private static bool ContainsKey(List<SDS_StockKey> keys, SDS_StockKey key)
        {
            if (keys == null || key == null)
            {
                return false;
            }
            for (int i = 0; i < keys.Count; i++)
            {
                SDS_StockKey candidate = keys[i];
                if (ReferenceEquals(candidate, key)
                    || (candidate != null && candidate.SameAs(key)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 原版按“每个 Thing 计 1”读取时，物理镜像已贡献 1 个单位；账本计数转成原版单位应减 1。
        /// </summary>
        internal static long PhysicalMirrorUnitOffset(Map map, SDS_StockKey key, bool mirrorCounts)
        {
            return mirrorCounts && SDS_MirrorService.TryGetMirror(map, key, out _) ? -1L : 0L;
        }

        // ---------- 总量口径 ----------

        /// <summary>该 def 在超维空间里的物理总量（不扣预留）：虚拟按 def 汇总，保真按外壳 def 计件。</summary>
        internal static long LedgerTotalForDef(ThingDef def)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || def == null)
            {
                return 0L;
            }
            long total = 0L;
            List<SDS_StockKey> keys = new List<SDS_StockKey>();
            ledger.CopyKeysForDef(def, keys);
            for (int i = 0; i < keys.Count; i++)
            {
                total += ledger.GetCount(keys[i]);
            }
            return total + PreservedCountOfDef(def);
        }

        /// <summary>
        /// `PlayerItemAccessibilityUtility.Accessible` 的账本投影：
        /// 非镜像 holder 实物 + 账本 + 保真。是否接管由调用方的 <see cref="SDS_Selection.GridAccessibleFrom"/> 决定。
        /// </summary>
        internal static long AccessibleTotal(ThingDef def)
        {
            if (def == null)
            {
                return 0L;
            }
            return SDS_Selection.NonMirrorAccessibleCount(def) + LedgerTotalForDef(def);
        }

        /// <summary>保真通道按外壳 def 计件（与原版 ThingsOfDef 口径一致）。</summary>
        internal static long PreservedCountOfDef(ThingDef def)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || def == null)
            {
                return 0L;
            }
            List<Thing> preserved = new List<Thing>();
            ledger.AppendPreservedThings(preserved);
            long total = 0L;
            for (int i = 0; i < preserved.Count; i++)
            {
                Thing thing = preserved[i];
                if (thing != null && !thing.Destroyed && thing.def == def)
                {
                    total += thing.stackCount;
                }
            }
            return total;
        }

        // ---------- 财富 ----------

        /// <summary>
        /// WealthWatcher 的单图校正量：
        ///  规范图 = 账本虚拟财富 + 保真 − 本图实际镜像财富；
        ///  其它图 / 不暴露图 / 不计入财富 = − 本图实际镜像财富。
        /// </summary>
        internal static float WealthAdjust(Map map)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (map == null || ledger == null)
            {
                return 0f;
            }
            float actual = SDS_MirrorService.MirrorWealthOn(map);
            if (SDS_Config.ExcludeGridFromWealth)
            {
                return -actual;
            }
            if (!MapExposesGrid(map) || CanonicalExposureMap() != map)
            {
                return -actual;
            }
            return LedgerVirtualWealth(map) + PreservedWealth() - actual;
        }

        private static float LedgerVirtualWealth(Map map)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (map == null || ledger == null)
            {
                return 0f;
            }
            float sum = 0f;
            List<SDS_StockEntry> snapshot = ledger.Snapshot(0);
            for (int i = 0; i < snapshot.Count; i++)
            {
                SDS_StockEntry entry = snapshot[i];
                if (entry == null || entry.key == null || entry.key.preserved
                    || entry.key.thingDef == null || entry.count <= 0
                    || !entry.key.thingDef.EverHaulable)
                {
                    continue;
                }
                if (SDS_MirrorService.TryGetMirror(map, entry.key, out Thing mirror))
                {
                    if (mirror != null && mirror.Spawned && !mirror.PositionHeld.Fogged(map))
                    {
                        sum += mirror.MarketValue * mirror.stackCount;
                    }
                    continue;
                }
                // 镜像缺失（建镜失败窗口）：按账本单位市值兜底。
                Thing probe = SDS_OutputUtility.CachedDisplayThing(entry.key);
                if (probe != null)
                {
                    sum += probe.MarketValue * entry.count;
                }
            }
            return sum;
        }

        internal static float PreservedWealth()
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null)
            {
                return 0f;
            }
            float total = 0f;
            List<Thing> preserved = new List<Thing>();
            ledger.AppendPreservedThings(preserved);
            for (int i = 0; i < preserved.Count; i++)
            {
                Thing thing = preserved[i];
                if (thing != null && !thing.Destroyed && thing.def != null && thing.def.EverHaulable)
                {
                    total += thing.MarketValue * thing.stackCount;
                }
            }
            return total;
        }

        // ---------- 资源条刷新 ----------

        private static ExposureVersions Capture()
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            ISDS_MirrorQuery mirror = SDS_Host.MirrorQuery;
            ExposureVersions versions = default;
            versions.tick = GenTicks.TicksGame;
            versions.mirror = mirror != null ? mirror.Version : 0;
            versions.endpoint = SDS_Rules.EndpointVersion;
            if (ledger != null)
            {
                versions.stock = ledger.StockVersion;
                versions.reservation = ledger.ReservationVersion;
                versions.filter = ledger.FilterVersion;
            }
            return versions;
        }

        internal static bool TryBeginCounterRefresh(Map map)
        {
            if (map == null || map.resourceCounter == null)
            {
                return false;
            }
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            // 原版这一 tick 自己会整表重算，不做第二遍。
            if (now % 204 == 0)
            {
                return false;
            }
            ExposureVersions version = Capture();
            if (counterVersions.TryGetValue(map, out ExposureVersions seen)
                && version.SameExposureAs(seen))
            {
                return false;
            }
            if (counterRefreshTick.TryGetValue(map, out int last)
                && now - last < CounterMinIntervalTicks)
            {
                return false;
            }
            if (!MapExposesGrid(map))
            {
                return false;
            }
            if (counterVersions.Count > 32)
            {
                counterVersions.Clear();
                counterRefreshTick.Clear();
            }
            counterVersions[map] = version;
            counterRefreshTick[map] = now;
            return true;
        }

        internal static void ForceCounterRefresh(Map map)
        {
            if (map == null || map.resourceCounter == null)
            {
                return;
            }
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (counterRefreshTick.TryGetValue(map, out int last) && last == now)
            {
                return;
            }
            if (counterRefreshTick.Count > 32)
            {
                counterRefreshTick.Clear();
                counterVersions.Clear();
            }
            counterRefreshTick[map] = now;
            counterVersions[map] = Capture();
            map.resourceCounter.UpdateResourceCounts();
        }
    }
}
