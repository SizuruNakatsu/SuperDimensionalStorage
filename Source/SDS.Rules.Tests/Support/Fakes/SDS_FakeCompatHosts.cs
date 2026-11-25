using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    internal sealed class SDS_FakeDisplayQuery : ISDS_DisplayQuery
    {
        public readonly List<Thing> Displays = new List<Thing>();
        public readonly Dictionary<Thing, SDS_StockKey> Keys = new Dictionary<Thing, SDS_StockKey>();

        public bool IsDisplay(Thing thing)
        {
            return thing != null && Displays.Contains(thing);
        }

        public bool TryGetDisplayKey(Thing thing, out SDS_StockKey key)
        {
            key = null;
            return thing != null && Keys.TryGetValue(thing, out key);
        }
    }

    internal sealed class SDS_FakeDepositJobs : ISDS_DepositJobs
    {
        public bool CandidateAllowed;
        public Job JobToMake;
        public Job InventoryJob;

        public bool CanTakeCandidate(Pawn pawn, Thing thing, bool forced)
        {
            return CandidateAllowed;
        }

        public Job TryMakeJob(Pawn pawn, Thing thing, bool forced, Job vanillaJob, bool multi)
        {
            return JobToMake;
        }

        public Job TryMakeFromInventory(Pawn pawn, IList<Thing> items)
        {
            return InventoryJob;
        }

        public bool IsManualDepositAvailable(Pawn pawn)
        {
            return CandidateAllowed;
        }
    }

    internal sealed class SDS_FakeTransportLoad : ISDS_TransportLoadSink
    {
        public int Calls;

        public bool InjectTransportLoadCandidates(ThingWithComps host,
            List<RimWorld.TransferableOneWay> transferables)
        {
            Calls++;
            return true;
        }
    }
}
