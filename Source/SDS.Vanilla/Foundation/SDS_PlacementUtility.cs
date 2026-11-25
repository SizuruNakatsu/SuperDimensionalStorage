using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 落点兜底：GenPlace 失败时再退一级到 GenSpawn，保证"取出来 / 退回地图"的实物不凭空消失。
    /// 所有落地路径共用这一处，不各写一套 TryPlace 回落。
    /// </summary>
    internal static class SDS_PlacementUtility
    {
        internal static bool TryPlaceOrSpawn(Thing thing, IntVec3 cell, Map map)
        {
            return TryPlaceOrSpawn(thing, cell, map, out _);
        }

        /// <summary>
        /// 落点唯一实现：返回实际落点实例。Near 模式可能并入已有同类堆并销毁临时实例，
        /// 此时 <paramref name="placed"/> 是那个已存在的堆（不是传入的 thing）。
        /// </summary>
        internal static bool TryPlaceOrSpawn(Thing thing, IntVec3 cell, Map map, out Thing placed)
        {
            placed = null;
            if (thing == null || thing.Destroyed || thing.Spawned || map == null)
            {
                return false;
            }
            IntVec3 at = cell.IsValid && cell.InBounds(map) ? cell : map.Center;
            if (GenPlace.TryPlaceThing(thing, at, map, ThingPlaceMode.Near, out Thing spot)
                && spot != null && !spot.Destroyed)
            {
                placed = spot;
                return true;
            }
            if (!thing.Destroyed)
            {
                GenSpawn.Spawn(thing, at, map);
                placed = thing;
                return true;
            }
            return false;
        }
    }
}
