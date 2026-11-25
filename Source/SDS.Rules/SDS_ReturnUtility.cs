using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 回充 / 退路的唯一实现：先回账本，失败再按 SDS_ReturnFallback 交给宿主交付。
    /// 任何分支都不主动销毁地图上的实物（虚拟回充成功时才消化已物化的临时实例）。
    /// 入 / 出：入实物 + 落点信息，出「是否已回到账本」；退路成功不算回账本。
    /// 区别：SDS_Ledger.TryConvertThingToVirtual 只管「入账 + 销毁」；本类负责失败后的交付退路。
    /// </summary>
    internal static class SDS_ReturnUtility
    {
        /// <summary>
        /// 虚拟通道回充（无指定落点）。
        /// 入：thing —— 待回充实物；map / pawn —— 退路上下文；fallback —— 退路策略。
        /// 出：是否已回账本；落到退路或无处可去返回 false。
        /// 区别：带 cell 的重载只是把落点传给 Ground 退路，账本优先逻辑完全相同。
        /// </summary>
        public static bool ReturnVirtual(Thing thing, Map map, Pawn pawn, SDS_ReturnFallback fallback)
        {
            return ReturnVirtual(thing, map, pawn, fallback, IntVec3.Invalid);
        }

        /// <summary>
        /// 虚拟通道回充（带调用方落点）。
        /// 入：cell —— 只作用于 Ground 退路；无效 / 越界时回退 pawn 脚下 / 地图中心。
        /// 出：是否已回账本；回账本走账本唯一「实转虚」原子实现（失败会回挂原容器）。
        /// 区别：ReturnPreserved 保留真身；本方法会销毁实物只留账面数量（回账本成功且未在账本时）。
        ///       账本已持有同一实物时按幂等成功处理，不销毁、不落地；同 thingID 冲突拒绝且不落地。
        /// </summary>
        public static bool ReturnVirtual(Thing thing, Map map, Pawn pawn, SDS_ReturnFallback fallback,
            IntVec3 cell)
        {
            if (thing == null || thing.Destroyed)
            {
                return false;
            }
            SDS_ProducedThingMarker.Unmark(thing);
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            SDS_PreservedIdentityState ownership = ClassifyLedgerOwnership(ledger, thing, "virtual");
            if (ownership == SDS_PreservedIdentityState.SameReference)
            {
                // 账本已持有该实物：幂等成功，绝不重复入账或落地复制。
                return true;
            }
            if (ownership == SDS_PreservedIdentityState.SameThingId)
            {
                // 同 thingID 的副本已在账本：不转通道、不落地，实物保持原状交给调用方。
                return false;
            }
            if (ledger != null && ledger.TryConvertThingToVirtual(thing, out _))
            {
                return true;
            }
            DeliverFallback(thing, map, pawn, fallback, cell, "virtual");
            return false;
        }

        /// <summary>
        /// 保真通道回充（无指定落点）。
        /// 入：thing —— 待回充实物；map / pawn —— 退路上下文；fallback —— 退路策略；cell 取无效值。
        /// 出：是否已回账本；落到退路或无处可去返回 false。
        /// 区别：与虚拟回充不同，本通道不销毁实物，只是把真身交回账本容器。
        /// </summary>
        public static bool ReturnPreserved(Thing thing, Map map, Pawn pawn, SDS_ReturnFallback fallback)
        {
            return ReturnPreserved(thing, map, pawn, fallback, IntVec3.Invalid);
        }

        /// <summary>
        /// 保真通道回充（带调用方落点）。
        /// 入：cell —— 只作用于 Ground 退路。
        /// 出：是否已回账本；已在账本里的真身按幂等成功处理；同 thingID 冲突拒绝且不落地。
        /// 区别：ReturnVirtual 走「实转虚」；本方法走 TryDepositPreserved，不重建、不销毁。
        /// </summary>
        public static bool ReturnPreserved(Thing thing, Map map, Pawn pawn, SDS_ReturnFallback fallback,
            IntVec3 cell)
        {
            if (thing == null || thing.Destroyed)
            {
                return false;
            }
            SDS_ProducedThingMarker.Unmark(thing);
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            SDS_PreservedIdentityState ownership = ClassifyLedgerOwnership(ledger, thing, "preserved");
            if (ownership == SDS_PreservedIdentityState.SameReference)
            {
                // 已在账本里的真身视为回充成功（幂等），绝不重复入列或落地复制。
                return true;
            }
            if (ownership == SDS_PreservedIdentityState.SameThingId)
            {
                // 同 thingID 的副本已在账本：拒绝，避免复制 / 落地形成双所有者。
                return false;
            }
            if (ledger != null && ledger.TryDepositPreserved(thing))
            {
                return true;
            }
            DeliverFallback(thing, map, pawn, fallback, cell, "preserved");
            return false;
        }

        /// <summary>
        /// 回充身份门：转发账本唯一判定，并在同 ID 冲突时记一次 Warn。
        /// 入：ledger —— 账本，可 null；thing —— 待回充实物；channel —— 日志标记。
        /// 出：账本的 SDS_PreservedIdentityState（None / SameReference / SameThingId）。
        /// 区别：判定本身只在 SDS_Ledger.ClassifyPreservedIdentity 定义一次；本方法只做日志映射，
        ///       不重复比较引用 / thingID。
        /// </summary>
        private static SDS_PreservedIdentityState ClassifyLedgerOwnership(SDS_Ledger ledger, Thing thing,
            string channel)
        {
            if (ledger == null)
            {
                return SDS_PreservedIdentityState.None;
            }
            SDS_PreservedIdentityState ownership = ledger.ClassifyPreservedIdentity(thing);
            if (ownership == SDS_PreservedIdentityState.SameThingId)
            {
                SDS_Diag.Warn("回充/同一实物已在账本（已拒绝，不落地复制）",
                    "channel=" + channel + " def="
                    + (thing.def != null ? thing.def.defName : "?")
                    + "#" + thing.thingIDNumber);
            }
            return ownership;
        }

        /// <summary>
        /// 回充失败后的退路（内部唯一实现）。
        /// 入：thing —— 待交付实物；map / pawn / cell —— 落点上下文；fallback —— Ground / PawnInventory / PawnCarry / None；channel —— 日志标记。
        /// 出：无；失败只记 Warn / ErrorOnce，绝不丢失实物（map 为空时保持原状）。
        /// null 规则：map 为 null 不落地并记 Error；pawn 为 null 时背包 / 手持按 None 处理。
        /// 区别：本方法只做「交给宿主」，不碰账本；回账本由两个 Return* 入口先尝试。
        /// </summary>
        private static void DeliverFallback(Thing thing, Map map, Pawn pawn,
            SDS_ReturnFallback fallback, IntVec3 cell, string channel)
        {
            if (map == null)
            {
                SDS_Diag.ErrorOnce("回充失败且没有地图，实物保持原状（通道：" + channel + "）", null);
                return;
            }
            if (pawn == null && (fallback == SDS_ReturnFallback.PawnInventory
                || fallback == SDS_ReturnFallback.PawnCarry))
            {
                SDS_Diag.ErrorOnce("回充退路需要小人但 pawn 为空，按 None 处理（通道：" + channel + "）", null);
                fallback = SDS_ReturnFallback.None;
            }
            if (fallback == SDS_ReturnFallback.None)
            {
                return;
            }
            ISDS_DeliverySink delivery = SDS_Host.Delivery;
            if (delivery == null)
            {
                SDS_Diag.ErrorOnce("SDS_Host.Delivery 未注入，回充退路不可用（通道：" + channel + "）", null);
                return;
            }
            IntVec3 landing = cell.IsValid && cell.InBounds(map)
                ? cell
                : (pawn != null && pawn.Spawned ? pawn.Position : map.Center);
            bool ok = false;
            if (fallback == SDS_ReturnFallback.Ground)
            {
                ok = delivery.TryPlace(thing, map, landing);
            }
            else if (fallback == SDS_ReturnFallback.PawnInventory)
            {
                ok = delivery.TryGiveToPawn(thing, pawn, false);
            }
            else if (fallback == SDS_ReturnFallback.PawnCarry)
            {
                ok = delivery.TryGiveToPawn(thing, pawn, true);
            }
            if (!ok)
            {
                SDS_Diag.Warn("回充退路交付失败", "channel=" + channel + " fallback=" + fallback);
            }
        }
    }
}
