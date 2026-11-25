using Verse;

namespace SuperDimensionalStorage
{
    internal sealed class SDS_FakeDeliverySink : ISDS_DeliverySink
    {
        public bool Result = true;
        public int PlaceCalls;
        public int PawnCalls;
        public Thing LastThing;
        public Pawn LastPawn;
        public bool LastPreferCarry;

        public bool TryPlace(Thing thing, Map map, IntVec3 cell)
        {
            PlaceCalls++;
            LastThing = thing;
            return Result;
        }

        public bool TryGiveToPawn(Thing thing, Pawn pawn, bool preferCarry)
        {
            PawnCalls++;
            LastThing = thing;
            LastPawn = pawn;
            LastPreferCarry = preferCarry;
            return Result;
        }
    }
}
