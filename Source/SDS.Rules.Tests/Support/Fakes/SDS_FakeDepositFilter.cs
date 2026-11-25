using System;
using Verse;

namespace SuperDimensionalStorage
{
    internal sealed class SDS_FakeDepositFilter : ISDS_DepositFilter
    {
        public Func<Thing, bool> AllowsFn = _ => true;

        public bool Allows(Thing thing)
        {
            return AllowsFn == null || AllowsFn(thing);
        }
    }

    internal sealed class SDS_FakePrioritySource : ISDS_PrioritySource
    {
        public int Priority { get; set; } = (int)RimWorld.StoragePriority.Normal;
        public bool IgnorePriority { get; set; }
    }

    internal sealed class SDS_FakeMedicalCarePolicy : ISDS_MedicalCarePolicy
    {
        public bool Allow = true;
        public ThingDef LastMedicine;
        public Thing LastBillGiver;

        public bool Allows(ThingDef medicine, Thing billGiver)
        {
            LastMedicine = medicine;
            LastBillGiver = billGiver;
            return Allow;
        }
    }
}
