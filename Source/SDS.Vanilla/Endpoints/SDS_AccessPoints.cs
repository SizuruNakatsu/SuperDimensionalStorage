using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 访问点：镜像物理上在金库，玩家 / 小人真正去取的位置。
    /// 建筑端点 = 交互格（不可站退回建筑格）；Pawn 端点 = 所在格。
    /// </summary>
    internal sealed class SDS_AccessPoint
    {
        internal Thing thing;
        internal IntVec3 cell;
        internal PathEndMode endMode;
    }

    /// <summary>
    /// 访问点解析器：镜像的"有效距离 / 可达"唯一口径。
    /// 只按距离比较，无迟滞系数；端点版本或 60 tick 失效。
    /// </summary>
    internal static class SDS_AccessPointResolver
    {
        private const int CacheTicks = 60;
        private const int ReachCacheTicks = 30;
        private const int ReachCacheMaxEntries = 128;

        private sealed class MapEntry
        {
            internal readonly List<SDS_AccessPoint> points = new List<SDS_AccessPoint>();
            internal int builtTick = -1;
            internal int builtVersion = -1;
        }

        private struct ReachKey : IEquatable<ReachKey>
        {
            public Map map;
            public IntVec3 root;
            public TraverseParms parms;

            public bool Equals(ReachKey other)
            {
                return map == other.map && root == other.root && parms.Equals(other.parms);
            }

            public override bool Equals(object obj)
            {
                return obj is ReachKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                int h = map != null ? map.GetHashCode() : 0;
                h = h * 397 ^ root.GetHashCode();
                h = h * 397 ^ parms.GetHashCode();
                return h;
            }
        }

        private sealed class ReachEntry
        {
            internal int tick;
            internal int version;
            internal SDS_AccessPoint point;
        }

        private static readonly Dictionary<Map, MapEntry> cache = new Dictionary<Map, MapEntry>();
        private static readonly Dictionary<ReachKey, ReachEntry> reachCache =
            new Dictionary<ReachKey, ReachEntry>();
        private static readonly List<ReachKey> staleKeys = new List<ReachKey>();

        internal static void Invalidate()
        {
            cache.Clear();
            reachCache.Clear();
            staleKeys.Clear();
        }

        /// <summary>该图当前的访问点列表（缓存，勿修改返回的 List）。</summary>
        internal static List<SDS_AccessPoint> GetPoints(Map map)
        {
            if (map == null || map.Disposed)
            {
                return null;
            }
            if (!cache.TryGetValue(map, out MapEntry entry) || entry == null)
            {
                entry = new MapEntry();
                cache[map] = entry;
            }
            int now = GenTicks.TicksGame;
            int version = SDS_Rules.EndpointVersion;
            if (entry.builtTick < 0 || entry.builtVersion != version || now - entry.builtTick >= CacheTicks)
            {
                BuildPoints(map, entry.points);
                entry.builtTick = now;
                entry.builtVersion = version;
            }
            return entry.points;
        }

        private static void BuildPoints(Map map, List<SDS_AccessPoint> points)
        {
            points.Clear();
            ISDS_PortProvider provider = SDS_Host.Ports;
            if (provider == null || !SDS_Rules.Available)
            {
                return;
            }
            SDS_EndpointQuery query = default;
            query.op = SDS_EndpointOp.Withdraw;
            query.map = map;
            query.requireReach = false;
            query.allowSelf = false;
            query.requireStock = false;
            List<ISDS_Port> ports = new List<ISDS_Port>();
            provider.AppendCandidates(query, ports);
            for (int i = 0; i < ports.Count; i++)
            {
                ISDS_Port port = ports[i];
                if (port == null || port.Thing == null || !port.Operational)
                {
                    continue;
                }
                IntVec3 cell = port.AccessCell;
                if (!cell.IsValid || !cell.InBounds(map))
                {
                    continue;
                }
                points.Add(new SDS_AccessPoint
                {
                    thing = port.Thing,
                    cell = cell,
                    endMode = port.AccessMode
                });
            }
        }

        /// <summary>镜像的有效距离平方：到最近访问点；非镜像 / 无访问点时退回自身位置。</summary>
        internal static float EffectiveDistanceSquared(IntVec3 from, Thing thing)
        {
            if (thing == null)
            {
                return float.MaxValue;
            }
            if (!SDS_MirrorRegistry.IsMirror(thing))
            {
                return (from - thing.PositionHeld).LengthHorizontalSquared;
            }
            if (!TryGetNearestAccess(from, thing.Map, out SDS_AccessPoint nearest))
            {
                return (from - thing.PositionHeld).LengthHorizontalSquared;
            }
            return (from - nearest.cell).LengthHorizontalSquared;
        }

        /// <summary>纯几何的最近访问点：只取点，不判可达（排序 / 距离用）。</summary>
        internal static bool TryGetNearestAccess(IntVec3 from, Map map, out SDS_AccessPoint point)
        {
            point = FindNearestAccess(from, map, false, default);
            return point != null;
        }

        /// <summary>最近点的唯一循环：距离公式与"严格更近"平局规则只在这里出现一次。</summary>
        private static SDS_AccessPoint FindNearestAccess(IntVec3 from, Map map, bool requireReach,
            TraverseParms traverseParams)
        {
            List<SDS_AccessPoint> points = GetPoints(map);
            if (points == null || points.Count == 0)
            {
                return null;
            }
            SDS_AccessPoint best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                SDS_AccessPoint candidate = points[i];
                if (candidate == null)
                {
                    continue;
                }
                float dist = (from - candidate.cell).LengthHorizontalSquared;
                if (dist >= bestDist)
                {
                    continue;
                }
                if (requireReach && (map.reachability == null
                    || !map.reachability.CanReach(from, new LocalTargetInfo(candidate.thing),
                        candidate.endMode, traverseParams)))
                {
                    continue;
                }
                best = candidate;
                bestDist = dist;
            }
            return best;
        }

        /// <summary>
        /// 唯一取货点规则（准则一 / 推论三）：自身端口可用且 from 就是本人所在格时，
        /// 它即距离 0 的访问点（含可达）；否则取最近可达的建筑访问点（30 tick 缓存）。
        /// 保真桥、镜像走位、Job 物化与可达性判定全部只经这里，不各自另写一套。
        /// </summary>
        internal static bool TryGetNearestReachableAccess(IntVec3 from, Map map,
            TraverseParms traverseParams, out SDS_AccessPoint point)
        {
            point = null;
            if (map == null)
            {
                return false;
            }
            // S18：非玩家派系不解析隐藏库存的访问点；先于缓存判断，保证设置热切换立即生效。
            if (SDS_AccessRules.BlocksNonPlayer(traverseParams.pawn))
            {
                return false;
            }
            // 自身端口不做缓存：访问格随本人移动，缓存一个旧格会让“原地取货”失效。
            if (traverseParams.pawn != null && from == traverseParams.pawn.Position
                && SDS_ImplantUtility.IsPortOperational(traverseParams.pawn))
            {
                SDS_EndpointHandle self = SDS_EndpointHandle.ForPawn(traverseParams.pawn, true);
                point = new SDS_AccessPoint
                {
                    thing = traverseParams.pawn,
                    cell = self.AccessCell,
                    endMode = self.AccessMode
                };
                return true;
            }
            try
            {
                int now = GenTicks.TicksGame;
                int version = SDS_Rules.EndpointVersion;
                ReachKey key = new ReachKey { map = map, root = from, parms = traverseParams };
                if (reachCache.TryGetValue(key, out ReachEntry entry) && entry != null
                    && entry.version == version && entry.tick <= now
                    && now - entry.tick <= ReachCacheTicks)
                {
                    point = entry.point;
                    return point != null;
                }
                if (entry == null)
                {
                    if (reachCache.Count >= ReachCacheMaxEntries)
                    {
                        PruneReachCache(now, version);
                    }
                    entry = new ReachEntry();
                    reachCache[key] = entry;
                }
                entry.tick = now;
                entry.version = version;
                entry.point = FindNearestAccess(from, map, true, traverseParams);
                point = entry.point;
                if (reachCache.Count > ReachCacheMaxEntries)
                {
                    PruneReachCache(now, version);
                }
                return point != null;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("最近可达访问点查询失败（本次按不可达处理）", e);
                point = null;
                return false;
            }
        }

        private static void PruneReachCache(int now, int version)
        {
            staleKeys.Clear();
            foreach (KeyValuePair<ReachKey, ReachEntry> pair in reachCache)
            {
                ReachEntry entry = pair.Value;
                if (entry == null || entry.version != version || now - entry.tick > ReachCacheTicks * 4)
                {
                    staleKeys.Add(pair.Key);
                }
            }
            for (int i = 0; i < staleKeys.Count; i++)
            {
                reachCache.Remove(staleKeys[i]);
            }
            staleKeys.Clear();
        }

        /// <summary>镜像是否可经任一访问点到达（不依赖金库格是否可达）。</summary>
        internal static bool CanReachViaAccess(IntVec3 from, Map map, TraverseParms traverseParams)
        {
            return TryGetNearestReachableAccess(from, map, traverseParams, out _);
        }

        /// <summary>给 Job 适配器 / 保真桥用：该小人此刻的取货点（唯一规则见 TryGetNearestReachableAccess）。</summary>
        internal static bool TryGetNearestReachable(Pawn pawn, out SDS_AccessPoint point)
        {
            point = null;
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return false;
            }
            return TryGetNearestReachableAccess(pawn.Position, pawn.Map, TraverseParms.For(pawn), out point);
        }
    }
}
