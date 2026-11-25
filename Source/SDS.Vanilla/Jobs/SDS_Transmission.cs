using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 传递层：把镜像在容器入口换成真身（ThingOwner 族）。
    ///
    /// 为什么必须补：镜像 spawn 在隐藏金库，一旦被原版直接装进任何 ThingOwner
    /// （运输舱 / 棺材 / 基因库 / 小人背包 / 制作台 Frame……）会立刻 despawn 成"容器里的代理"，
    /// 而镜像取用要求镜像仍 spawn 在地图上 → 取货失败，表现为"东西进了容器却拿不出来"。
    ///
    /// 覆盖口径（按原版源码核对，只补真正会"整件塞进去"的两条）：
    ///  - ThingOwner&lt;Thing&gt;.TryAdd(Thing, bool)：无需合并时直接 innerList.Add；
    ///  - ThingOwner.TryAddOrTransfer(Thing, bool)：所有泛型 owner 共用的高层入口。
    /// 带数量的两个重载不用补：原版内部一定会 SplitOff，已由镜像 SplitOff 补丁换成真身。
    ///
    /// 守卫：Map 自己的 spawnedThings 必须原样放行（镜像 spawn 流程本身）；
    /// 目标容器先判收不收，再摘账本（否则容器满时真身会停在没有 owner 的空中，数量丢失）。
    /// </summary>
    internal static class SDS_Transmission
    {
        internal enum TransferKind
        {
            /// <summary>不是活跃镜像：调用方原样走原版。</summary>
            NotMirror,
            /// <summary>已换成真身，调用方继续走原版。</summary>
            Resolved,
            /// <summary>是镜像但账本取不出来：调用方必须拒绝这次传递。</summary>
            Failed
        }

        public static TransferKind ResolveForOwner(Thing item, int count, out Thing real)
        {
            real = item;
            if (item == null || item.Destroyed || !SDS_MirrorRegistry.IsMirror(item))
            {
                return TransferKind.NotMirror;
            }
            if (count <= 0)
            {
                count = item.stackCount;
            }
            if (SDS_TakeService.TryTake(item, count, out Thing taken) && taken != null && !taken.Destroyed)
            {
                real = taken;
                return TransferKind.Resolved;
            }
            real = null;
            return TransferKind.Failed;
        }

        /// <summary>该 owner 是不是地图自己持有的容器：接收镜像属于 spawn 流程本身，必须放行。</summary>
        public static bool IsMapOwned(ThingOwner owner)
        {
            return owner != null && owner.Owner is Map;
        }

        /// <summary>
        /// W54：小人所属 owner（背包 / 装备 / 衣物 / 直接 Pawn owner）。
        /// 镜像不得经 ThingOwner 直接进入这里——上手只能经 SDS_MirrorCarry（端口命中校验）。
        /// </summary>
        public static bool IsPawnOwned(ThingOwner owner)
        {
            if (owner == null)
            {
                return false;
            }
            return owner.Owner is Pawn
                || owner.Owner is Pawn_InventoryTracker
                || owner.Owner is Pawn_EquipmentTracker
                || owner.Owner is Pawn_ApparelTracker;
        }

        /// <summary>容器归属的 Pawn；通用容器返回 null（由搜索 / 可达 / Job 门先行拦截）。</summary>
        public static Pawn OwnerPawn(ThingOwner owner)
        {
            if (owner == null)
            {
                return null;
            }
            if (owner.Owner is Pawn pawn)
            {
                return pawn;
            }
            if (owner.Owner is Pawn_EquipmentTracker equipment)
            {
                return equipment.pawn;
            }
            if (owner.Owner is Pawn_ApparelTracker apparel)
            {
                return apparel.pawn;
            }
            if (owner.Owner is Pawn_InventoryTracker inventory)
            {
                return inventory.pawn;
            }
            return null;
        }

        /// <summary>在摘账本之前判断容器此刻能不能收下这件实物。</summary>
        public static bool CanReceive(ThingOwner owner, Thing item, bool canMergeWithExistingStacks)
        {
            return owner != null && item != null
                && owner.CanAcceptAnyOf(item, canMergeWithExistingStacks);
        }

        /// <summary>容器最终没接住时把剩余实物回充账本 / 落地（绝不吞物品）。</summary>
        public static void ReturnOrDrop(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0)
            {
                return;
            }
            Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            IntVec3 cell = map != null ? map.Center : IntVec3.Invalid;
            if (!SDS_Rules.ReturnThing(thing, SDS_StockChannel.Virtual, map, null, SDS_ReturnFallback.None))
            {
                if (map == null)
                {
                    SDS_Diag.ErrorOnce("容器拒收后无处回充（已保留在世界 pawn 列表之外，请检查是否丢失）："
                        + thing.ToStringSafe(), new InvalidOperationException("no map for rollback"));
                    return;
                }
                SDS_PlacementUtility.TryPlaceOrSpawn(thing, cell, map);
            }
        }
    }

    /// <summary>
    /// ThingOwner&lt;Thing&gt;.TryAdd(item, canMerge)：容器 / 背包 / 运输舱 / 棺材 / 基因库总入口。
    /// 镜像先换真身；容器收不下或账本取不出来就拒绝本次添加。
    /// </summary>
    [HarmonyPatch(typeof(ThingOwner<Thing>), nameof(ThingOwner<Thing>.TryAdd),
        new[] { typeof(Thing), typeof(bool) })]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_OwnerTryAdd
    {
        public static bool Prefix(ThingOwner __instance, ref Thing item,
            bool canMergeWithExistingStacks, out Thing __state, ref bool __result)
        {
            __state = null;
            try
            {
                if (item == null || SDS_Transmission.IsMapOwned(__instance)
                    || !SDS_MirrorRegistry.IsMirror(item))
                {
                    return true;
                }
                // W54：镜像不得经 ThingOwner 直接进入小人背包 / 小人 owner；
                // 上手只能经 SDS_MirrorCarry（端口命中校验），否则第三方搬运会隔空从金库物化入包。
                if (SDS_Transmission.IsPawnOwned(__instance))
                {
                    __result = false;
                    return false;
                }
                // S18：非玩家派系的装备 / 衣物 / 背包不接收隐藏库存。
                if (SDS_AccessRules.BlocksNonPlayer(SDS_Transmission.OwnerPawn(__instance)))
                {
                    __result = false;
                    return false;
                }
                if (!SDS_Transmission.CanReceive(__instance, item, canMergeWithExistingStacks))
                {
                    __result = false;
                    return false;
                }
                SDS_Transmission.TransferKind kind = SDS_Transmission.ResolveForOwner(
                    item, item.stackCount, out Thing real);
                if (kind == SDS_Transmission.TransferKind.Failed)
                {
                    __result = false;
                    return false;
                }
                __state = real;
                item = real;
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("容器接收镜像时物化失败（已拒绝本次添加）", e);
                __result = false;
                return false;
            }
        }

        /// <summary>原版最终没接住（含部分合并后返回 false）时，把剩余实物回充账本 / 落地。</summary>
        public static Exception Finalizer(Thing __state, bool __result, Exception __exception)
        {
            if (__state != null && (__exception != null || !__result))
            {
                SDS_Transmission.ReturnOrDrop(__state);
            }
            return __exception;
        }
    }

    /// <summary>ThingOwner.TryAddOrTransfer(item, canMerge)：所有泛型 owner 共用的转移入口。</summary>
    [HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.TryAddOrTransfer),
        new[] { typeof(Thing), typeof(bool) })]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_OwnerTryAddOrTransfer
    {
        public static bool Prefix(ThingOwner __instance, ref Thing item,
            bool canMergeWithExistingStacks, out Thing __state, ref bool __result)
        {
            __state = null;
            try
            {
                if (item == null || SDS_Transmission.IsMapOwned(__instance)
                    || !SDS_MirrorRegistry.IsMirror(item))
                {
                    return true;
                }
                // W54：镜像不得经 ThingOwner 直接进入小人背包 / 小人 owner；
                // 上手只能经 SDS_MirrorCarry（端口命中校验），否则第三方搬运会隔空从金库物化入包。
                if (SDS_Transmission.IsPawnOwned(__instance))
                {
                    __result = false;
                    return false;
                }
                // S18：非玩家派系的装备 / 衣物 / 背包不接收隐藏库存。
                if (SDS_AccessRules.BlocksNonPlayer(SDS_Transmission.OwnerPawn(__instance)))
                {
                    __result = false;
                    return false;
                }
                if (!SDS_Transmission.CanReceive(__instance, item, canMergeWithExistingStacks))
                {
                    __result = false;
                    return false;
                }
                SDS_Transmission.TransferKind kind = SDS_Transmission.ResolveForOwner(
                    item, item.stackCount, out Thing real);
                if (kind == SDS_Transmission.TransferKind.Failed)
                {
                    __result = false;
                    return false;
                }
                __state = real;
                item = real;
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("容器转移镜像时物化失败（已拒绝本次转移）", e);
                __result = false;
                return false;
            }
        }

        public static Exception Finalizer(Thing __state, bool __result, Exception __exception)
        {
            if (__state != null && (__exception != null || !__result))
            {
                SDS_Transmission.ReturnOrDrop(__state);
            }
            return __exception;
        }
    }
}
