using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 端点候选与可用性事实（§6.3）：枚举建筑 / Pawn / 机械体，本图可用性。
    /// 每次现读 Instance.State；建筑列表按 SDS_Rules.EndpointVersion 缓存，Pawn 实时扫描。
    /// </summary>
    internal sealed partial class SDS_EndpointProvider : ISDS_PortProvider
    {
        public void AppendCandidates(in SDS_EndpointQuery query, List<ISDS_Port> into)
        {
            if (into == null)
            {
                return;
            }
            Map map = query.map;
            if (map == null && query.actor != null)
            {
                map = query.actor.Map;
            }
            if (map == null || map.Disposed)
            {
                return;
            }
            SDS_VanillaState state = State();
            if (state == null)
            {
                return;
            }

            // 建筑端点：是否此刻可用（通电 / 本图有核心）留给 Core 的硬门实时判定。
            List<Thing> structures = state.StructuresOn(map);
            for (int i = 0; i < structures.Count; i++)
            {
                Thing thing = structures[i];
                if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != map
                    || ReferenceEquals(thing, query.actor))
                {
                    continue;
                }
                ISDS_StorageEndpoint endpoint = thing as ISDS_StorageEndpoint;
                if (endpoint == null)
                {
                    continue;
                }
                SDS_EndpointHandle handle = SDS_EndpointHandle.ForBuilding(endpoint);
                if (handle != null)
                {
                    into.Add(handle);
                }
            }

            // 人端口：带芯片（含继承主人芯片的机械体）一律入候选，可用性实时判定。
            if (map.mapPawns != null)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn == null || !pawn.Spawned || pawn.Map != map || pawn.Dead
                        || ReferenceEquals(pawn, query.actor))
                    {
                        continue;
                    }
                    if (SDS_ImplantUtility.EffectiveChipDef(pawn) == null)
                    {
                        continue;
                    }
                    into.Add(SDS_EndpointHandle.ForPawn(pawn, false));
                }
            }

            // 自身端口最后加入：距离恒 0，同距离时 Core 的排序会让它胜出。
            if (query.allowSelf && query.actor != null)
            {
                ISDS_Port self;
                if (TryGetSelfPort(query.actor, out self))
                {
                    into.Add(self);
                }
            }
        }

        public bool TryGetPort(Thing thing, out ISDS_Port port)
        {
            port = null;
            if (thing == null)
            {
                return false;
            }
            ISDS_StorageEndpoint endpoint = thing as ISDS_StorageEndpoint;
            if (endpoint != null)
            {
                port = SDS_EndpointHandle.ForBuilding(endpoint);
                return port != null;
            }
            Pawn pawn = thing as Pawn;
            if (pawn != null && SDS_ImplantUtility.IsPortOperational(pawn))
            {
                port = SDS_EndpointHandle.ForPawn(pawn, false);
                return true;
            }
            return false;
        }

        public bool TryGetSelfPort(Pawn pawn, out ISDS_Port port)
        {
            port = null;
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null)
            {
                return false;
            }
            if (!SDS_ImplantUtility.IsPortOperational(pawn))
            {
                return false;
            }
            port = SDS_EndpointHandle.ForPawn(pawn, true);
            return true;
        }

        /// <summary>本图可用：G &&（本图有可用核心 || 本图有可用 B 端口）。</summary>
        public bool IsLocalUsable(Map map)
        {
            if (map == null || map.Disposed || !SDS_Rules.Available)
            {
                return false;
            }
            SDS_VanillaState state = State();
            if (state == null)
            {
                return false;
            }
            return state.HasOperationalCore(map) || HasUsableBEndpointOn(map);
        }

        private bool HasUsableBEndpointOn(Map map)
        {
            if (map == null || map.mapPawns == null || !SDS_Rules.Available)
            {
                return false;
            }
            HediffDef chipB = SDS_HediffDefOf.ChipB;
            if (chipB == null)
            {
                return false;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || !pawn.Spawned || pawn.Map != map || pawn.Dead)
                {
                    continue;
                }
                if (SDS_ImplantUtility.EffectiveChipDef(pawn) == chipB
                    && SDS_ImplantUtility.IsPortOperational(pawn))
                {
                    return true;
                }
            }
            return false;
        }

        private static SDS_VanillaState State()
        {
            SDS_GameComponent_Grid grid = SDS_GameComponent_Grid.Instance;
            return grid != null ? grid.State : null;
        }
    }
}

