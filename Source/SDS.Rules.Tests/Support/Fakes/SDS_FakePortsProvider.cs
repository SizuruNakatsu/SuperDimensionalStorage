using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    internal sealed class SDS_FakePort : ISDS_Port
    {
        public Thing Thing { get; set; }
        public Pawn Pawn { get; set; }
        public Map Map { get; set; }
        public bool IsSelf { get; set; }
        public bool Operational { get; set; } = true;
        public IntVec3 AccessCell { get; set; }
        public PathEndMode AccessMode { get; set; } = PathEndMode.Touch;
        public bool Reachable { get; set; } = true;
        public float Distance { get; set; }

        public bool CanReach(Pawn actor)
        {
            return Reachable;
        }

        public float RankDistanceSquared(IntVec3 reference)
        {
            return Distance;
        }
    }

    internal sealed class SDS_FakePortsProvider : ISDS_PortProvider
    {
        public readonly List<ISDS_Port> Candidates = new List<ISDS_Port>();
        public readonly List<ISDS_Port> Requested = new List<ISDS_Port>();
        public bool LocalUsable = true;
        public ISDS_Port SelfPort;

        public void AppendCandidates(in SDS_EndpointQuery query, List<ISDS_Port> into)
        {
            if (into == null)
            {
                return;
            }
            into.AddRange(Candidates);
        }

        public bool TryGetPort(Thing thing, out ISDS_Port port)
        {
            for (int i = 0; i < Candidates.Count; i++)
            {
                if (Candidates[i] != null && ReferenceEquals(Candidates[i].Thing, thing))
                {
                    port = Candidates[i];
                    return true;
                }
            }
            port = null;
            return false;
        }

        public bool TryGetSelfPort(Pawn pawn, out ISDS_Port port)
        {
            port = SelfPort;
            return port != null;
        }

        public bool IsLocalUsable(Map map)
        {
            return LocalUsable;
        }
    }
}
