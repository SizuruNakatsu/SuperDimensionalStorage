using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    internal sealed class SDS_FakeMirrorQuery : ISDS_MirrorQuery
    {
        public readonly List<Thing> Visible = new List<Thing>();
        public int Version { get; set; }

        public IReadOnlyList<Thing> VisibleThingsOfDef(Map map, ThingDef def)
        {
            List<Thing> result = new List<Thing>();
            for (int i = 0; i < Visible.Count; i++)
            {
                Thing thing = Visible[i];
                if (thing != null && thing.def == def)
                {
                    result.Add(thing);
                }
            }
            return result;
        }

        public bool IsMirror(Thing thing)
        {
            return thing != null && Visible.Contains(thing);
        }
    }

    internal sealed class SDS_FakeMirrorPhysical : ISDS_MirrorPhysical
    {
        public bool Succeed = true;

        public bool TryTakePhysical(SDS_StockKey key, Thing mirror, int count, out Thing made)
        {
            made = null;
            if (!Succeed || mirror == null || count <= 0 || mirror.stackCount < count)
            {
                return false;
            }
            made = ThingMaker.MakeThing(mirror.def, mirror.Stuff);
            if (made == null)
            {
                return false;
            }
            made.stackCount = count;
            mirror.stackCount -= count;
            return true;
        }

        public bool TryTakeWholePhysical(SDS_StockKey key, Thing mirror, out Thing made)
        {
            made = null;
            if (!Succeed || mirror == null)
            {
                return false;
            }
            made = mirror;
            return true;
        }
    }
}
