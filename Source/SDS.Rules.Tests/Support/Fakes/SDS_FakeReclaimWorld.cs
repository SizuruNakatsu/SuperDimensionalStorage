using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    internal sealed class SDS_FakeReclaimWorld : ISDS_ReclaimWorld
    {
        public int Tick;
        public Func<int, bool> JobExistsFn = _ => true;
        public Func<int, bool> LordExistsFn = _ => true;
        public readonly List<Thing> RemovedFromCaravan = new List<Thing>();

        public int CurrentTick
        {
            get { return Tick; }
        }

        public bool JobExists(int jobLoadID)
        {
            return JobExistsFn == null || JobExistsFn(jobLoadID);
        }

        public bool LordExists(int lordLoadID)
        {
            return LordExistsFn == null || LordExistsFn(lordLoadID);
        }

        public void RemoveFromCaravan(int lordLoadID, Thing thing)
        {
            if (thing != null)
            {
                RemovedFromCaravan.Add(thing);
            }
        }
    }
}
