using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 只读投影 owner：内容物 = 该图当前镜像表的同一批引用。
    /// 不复制 Thing、不改 holdingOwner、不存档、不 tick；存取一律转 SDS_TakeService。
    /// </summary>
    internal sealed class SDS_ProjectionOwner : ThingOwner<Thing>, IEnumerable<Thing>
    {
        private static readonly List<Thing> tmpMirrors = new List<Thing>();

        private readonly SDS_ProjectionHolder holder;
        private int builtVersion = -1;
        private Map builtMap;
        private int viewOpDepth;

        internal SDS_ProjectionOwner(SDS_ProjectionHolder holder) : base(holder)
        {
            this.holder = holder;
            dontTickContents = true;
            removeContentsIfDestroyed = false;
        }

        public override int Count
        {
            get
            {
                EnsureFresh();
                return base.Count;
            }
        }

        public new List<Thing>.Enumerator GetEnumerator()
        {
            EnsureFresh();
            return InnerListForReading.GetEnumerator();
        }

        IEnumerator<Thing> IEnumerable<Thing>.GetEnumerator()
        {
            EnsureFresh();
            return InnerListForReading.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            EnsureFresh();
            return InnerListForReading.GetEnumerator();
        }

        public override int GetCountCanAccept(Thing item, bool canMergeWithExistingStacks = true)
        {
            return 0;
        }

        public override int TryAdd(Thing item, int count, bool canMergeWithExistingStacks = true)
        {
            return 0;
        }

        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            return false;
        }

        public override int IndexOf(Thing item)
        {
            EnsureFresh();
            return base.IndexOf(item);
        }

        /// <summary>Remove 在投影上等价于整堆取走；批量只读操作期间只摘引用、不扣账。</summary>
        public override bool Remove(Thing item)
        {
            EnsureFresh();
            List<Thing> list = InnerListForReading;
            int index = list.LastIndexOf(item);
            if (index < 0)
            {
                return false;
            }
            if (viewOpDepth > 0)
            {
                list.RemoveAt(index);
                return true;
            }
            // Remove 没有返回值：只有"一件就能装下"的镜像才能安全地整件交出。
            int stackLimit = item != null && item.def != null ? Math.Max(1, item.def.stackLimit) : 1;
            if (item == null || item.stackCount > stackLimit)
            {
                return false;
            }
            if (item.stackCount > 0 && !SDS_TakeService.TryTake(item, item.stackCount, out _))
            {
                return false;
            }
            index = list.LastIndexOf(item);
            if (index >= 0)
            {
                list.RemoveAt(index);
            }
            return true;
        }

        internal bool ContainsReference(Thing thing)
        {
            EnsureFresh();
            return thing != null && InnerListForReading.Contains(thing);
        }

        internal Thing TakeFromProjection(Thing thing, int count)
        {
            return TryTakeForTransfer(thing, count, out Thing taken) ? taken : null;
        }

        /// <summary>取 count 个；整堆取走后把引用从投影摘除（部分取保留减少后的同一件）。</summary>
        internal bool TryTakeForTransfer(Thing thing, int count, out Thing taken)
        {
            taken = null;
            if (thing == null || count <= 0)
            {
                return false;
            }
            EnsureFresh();
            List<Thing> list = InnerListForReading;
            if (!list.Contains(thing))
            {
                return false;
            }
            int want = Math.Min(count, thing.stackCount);
            if (want <= 0 || !SDS_TakeService.TryTake(thing, want, out taken))
            {
                return false;
            }
            if (ReferenceEquals(taken, thing) || thing.Destroyed || thing.stackCount <= 0)
            {
                list.Remove(thing);
            }
            return true;
        }

        internal void BeginViewOp()
        {
            viewOpDepth++;
        }

        internal void EndViewOp()
        {
            if (viewOpDepth > 0)
            {
                viewOpDepth--;
            }
            if (viewOpDepth == 0)
            {
                builtVersion = -1;
            }
        }

        public override void ExposeData()
        {
        }

        private void EnsureFresh()
        {
            Map map = holder.Map;
            int version = SDS_MirrorService.Version;
            if (builtVersion == version && builtMap == map)
            {
                return;
            }
            builtVersion = version;
            builtMap = map;
            List<Thing> list = InnerListForReading;
            list.Clear();
            if (map == null)
            {
                return;
            }
            SDS_MirrorService.CopyMirrorsOn(map, tmpMirrors);
            for (int i = 0; i < tmpMirrors.Count; i++)
            {
                Thing thing = tmpMirrors[i];
                if (thing != null)
                {
                    list.Add(thing);
                }
            }
        }
    }

    /// <summary>端点 / 小人身上的投影 holder：只把该图镜像表投影出去，本身不持有真实物。</summary>
    internal sealed class SDS_ProjectionHolder : IThingHolder
    {
        private readonly Pawn pawn;
        private readonly Thing endpoint;
        private readonly SDS_ProjectionOwner contents;

        internal SDS_ProjectionHolder(Pawn pawn)
        {
            this.pawn = pawn;
            contents = new SDS_ProjectionOwner(this);
        }

        internal SDS_ProjectionHolder(Thing endpoint)
        {
            this.endpoint = endpoint;
            contents = new SDS_ProjectionOwner(this);
        }

        internal Map Map
        {
            get { return pawn != null ? pawn.Map : (endpoint != null ? endpoint.Map : null); }
        }

        public IThingHolder ParentHolder
        {
            get
            {
                if (pawn != null)
                {
                    return pawn;
                }
                return endpoint as IThingHolder;
            }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return contents;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
        }

        internal bool Alive
        {
            get
            {
                return pawn != null
                    ? !pawn.Destroyed && pawn.Spawned
                    : endpoint != null && !endpoint.Destroyed && endpoint.Spawned;
            }
        }

        internal int CachedCount
        {
            get { return contents.Count; }
        }
    }

    /// <summary>
    /// Map 级枚举时替换带投影的小人：保留原 Pawn 的全部子 holder，
    /// 只把 SDS_ProjectionHolder 过滤掉，避免同一批镜像被数两次。
    /// </summary>
    internal sealed class SDS_MapLevelHolder : IThingHolder
    {
        internal readonly Pawn pawn;

        internal SDS_MapLevelHolder(Pawn pawn)
        {
            this.pawn = pawn;
        }

        public IThingHolder ParentHolder
        {
            get { return pawn; }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return pawn.GetDirectlyHeldThings();
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            int start = outChildren.Count;
            pawn.GetChildHolders(outChildren);
            for (int i = outChildren.Count - 1; i >= start; i--)
            {
                if (outChildren[i] is SDS_ProjectionHolder)
                {
                    outChildren.RemoveAt(i);
                }
            }
        }

        /// <summary>与原版 WealthItemsFilter 对 Pawn 的两条排除口径逐字一致。</summary>
        internal bool PassesWealthFilter
        {
            get { return pawn.Faction == Faction.OfPlayer && !pawn.IsQuestLodger(); }
        }
    }

    /// <summary>投影 holder / Map 级包装 holder 的缓存（运行期对象，不存档）。</summary>
    internal static class SDS_ProjectionService
    {
        private const int PruneThreshold = 256;

        private static readonly Dictionary<Pawn, SDS_ProjectionHolder> pawnProjections =
            new Dictionary<Pawn, SDS_ProjectionHolder>();
        private static readonly Dictionary<Thing, SDS_ProjectionHolder> endpointProjections =
            new Dictionary<Thing, SDS_ProjectionHolder>();
        private static readonly Dictionary<Pawn, SDS_MapLevelHolder> mapLevelHolders =
            new Dictionary<Pawn, SDS_MapLevelHolder>();

        internal static void Reset()
        {
            pawnProjections.Clear();
            endpointProjections.Clear();
            mapLevelHolders.Clear();
        }

        internal static SDS_ProjectionHolder ProjectionFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }
            PruneIfNeeded();
            if (!pawnProjections.TryGetValue(pawn, out SDS_ProjectionHolder holder) || holder == null)
            {
                holder = new SDS_ProjectionHolder(pawn);
                pawnProjections[pawn] = holder;
            }
            return holder;
        }

        internal static SDS_ProjectionHolder ProjectionFor(Thing endpoint)
        {
            if (endpoint == null)
            {
                return null;
            }
            PruneIfNeeded();
            if (!endpointProjections.TryGetValue(endpoint, out SDS_ProjectionHolder holder)
                || holder == null)
            {
                holder = new SDS_ProjectionHolder(endpoint);
                endpointProjections[endpoint] = holder;
            }
            return holder;
        }

        internal static SDS_MapLevelHolder MapLevelHolderFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }
            PruneIfNeeded();
            if (!mapLevelHolders.TryGetValue(pawn, out SDS_MapLevelHolder holder) || holder == null)
            {
                holder = new SDS_MapLevelHolder(pawn);
                mapLevelHolders[pawn] = holder;
            }
            return holder;
        }

        private static void PruneIfNeeded()
        {
            if (pawnProjections.Count + mapLevelHolders.Count > PruneThreshold)
            {
                PrunePawnDict(pawnProjections);
                PrunePawnDict(mapLevelHolders);
            }
            if (endpointProjections.Count > PruneThreshold)
            {
                List<Thing> stale = null;
                foreach (KeyValuePair<Thing, SDS_ProjectionHolder> pair in endpointProjections)
                {
                    if (pair.Key == null || pair.Key.Destroyed)
                    {
                        if (stale == null)
                        {
                            stale = new List<Thing>();
                        }
                        stale.Add(pair.Key);
                    }
                }
                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        endpointProjections.Remove(stale[i]);
                    }
                }
            }
        }

        private static void PrunePawnDict<T>(Dictionary<Pawn, T> dict)
        {
            List<Pawn> stale = null;
            foreach (KeyValuePair<Pawn, T> pair in dict)
            {
                if (pair.Key == null || pair.Key.Destroyed)
                {
                    if (stale == null)
                    {
                        stale = new List<Pawn>();
                    }
                    stale.Add(pair.Key);
                }
            }
            if (stale == null)
            {
                return;
            }
            for (int i = 0; i < stale.Count; i++)
            {
                dict.Remove(stale[i]);
            }
        }
    }

    /// <summary>投影 owner 的只读批量操作：只清引用、不扣账、不销毁镜像。</summary>
    internal static class SDS_ProjectionViewOps
    {
        internal static void Begin(ThingOwner owner)
        {
            SDS_ProjectionOwner projection = owner as SDS_ProjectionOwner;
            if (projection != null)
            {
                projection.BeginViewOp();
            }
        }

        internal static Exception End(ThingOwner owner, Exception exception)
        {
            SDS_ProjectionOwner projection = owner as SDS_ProjectionOwner;
            if (projection != null)
            {
                projection.EndViewOp();
            }
            return exception;
        }
    }
}
