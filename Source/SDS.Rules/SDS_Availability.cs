namespace SuperDimensionalStorage
{
    /// <summary>
    /// 可用量 / 供给失败的唯一口径：total − reserved，夹到 ≥ 0；
    /// 超维空间不可用或品类禁用时读侧归零（账本数据仍保留）。
    /// 入 / 出：入 key，出可用量或失败分类；不写账本、不预留。
    /// 区别：账本的 GetAvailable 只是内部算术，本类才是模块面唯一读口（叠加禁用 / 全局门）。
    /// </summary>
    internal static class SDS_AvailabilityImpl
    {
        /// <summary>
        /// 取用 / 生产口径的可用量。
        /// 入：key —— 品类键（虚拟或保真，按 key.preserved 分流）。
        /// 出：可用件数；未解析 / 账本未附加 / 全局不可用 / 品类禁用一律 0（R17）。
        /// 区别：GetTradableCount 不做禁用过滤（R15）；账本 GetAvailable 不叠加全局门。
        /// </summary>
        // RULE:R17
        public static long GetAvailableCount(SDS_StockKey key)
        {
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null || key == null || !key.Resolved || !SDS_RulesImpl.Available)
            {
                return 0L;
            }
            if (ledger.IsForbidden(key))
            {
                return 0L;
            }
            return AvailableOf(ledger, key);
        }

        /// <summary>
        /// 交易 / 捐献 / 发射口径的可用量。
        /// 入：key —— 品类键。
        /// 出：total − reserved；不做品类禁用过滤，但仍受全局可用性约束（R15 / R17）。
        /// 区别：GetAvailableCount 会因禁用归零；两者共用同一套 total / reserved 算术。
        /// </summary>
        // RULE:R15
        public static long GetTradableCount(SDS_StockKey key)
        {
            if (key == null || !key.Resolved || !SDS_RulesImpl.Available)
            {
                return 0L;
            }
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null)
            {
                return 0L;
            }
            return AvailableOf(ledger, key);
        }

        /// <summary>
        /// 供给失败原因（读侧唯一分类）。
        /// 入：key —— 品类键。
        /// 出：UnresolvedDef / Forbidden / Insufficient / None；顺序固定，UnresolvedDef 最高、Forbidden 高于 Insufficient。
        /// 区别：本方法只给分类，不给数量；数量用 GetAvailableCount / GetTradableCount。
        /// </summary>
        public static SDS_SupplyFailure GetFailure(SDS_StockKey key)
        {
            if (key == null || !key.Resolved)
            {
                return SDS_SupplyFailure.UnresolvedDef;
            }
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null)
            {
                return SDS_SupplyFailure.Insufficient;
            }
            // 顺序固定：UnresolvedDef > Forbidden > Insufficient。
            // 禁用是玩家意图，即使全局不可用也应先报 Forbidden。
            if (ledger.IsForbidden(key))
            {
                return SDS_SupplyFailure.Forbidden;
            }
            if (!SDS_RulesImpl.Available)
            {
                return SDS_SupplyFailure.Insufficient;
            }
            return AvailableOf(ledger, key) > 0L ? SDS_SupplyFailure.None : SDS_SupplyFailure.Insufficient;
        }

        /// <summary>
        /// 取用夹取：min(需求, 可用量)。
        /// 入：wanted —— 需求件数；available —— 已由本类读口取得的可用量。
        /// 出：实际取用件数；任一非正即 0（本次不取）。
        /// 区别：本方法是唯一夹取实现（W56），不得在调用点内联同式；
        ///       它不重新读账本，available 必须来自 GetAvailableCount / GetTradableCount。
        /// </summary>
        // RULE:W56
        public static int ClampTakeToAvailable(int wanted, long available)
        {
            return wanted <= 0 || available <= 0L
                ? 0
                : (int)System.Math.Min((long)wanted, available);
        }

        /// <summary>
        /// 通道口径的可用量：虚拟 = count − reserved；保真 = 真身件数 − reserved。
        /// 入：ledger —— 已附加账本；key —— 已解析品类键。
        /// 出：可用量，恒落在 [0, total]；会触发 E2 断言。
        /// 区别：私有实现，只被本类两个读口复用；不得在别处复制这段分流。
        /// </summary>
        // RULE:W22
        private static long AvailableOf(SDS_Ledger ledger, SDS_StockKey key)
        {
            if (ledger == null || key == null || !key.Resolved)
            {
                return 0L;
            }
            long total = key.preserved ? ledger.GetPreservedCount(key) : ledger.GetCount(key);
            long available = key.preserved
                ? ledger.GetPreservedAvailable(key)
                : ledger.GetAvailable(key);
            SDS_Diag.AssertAvailableWithinTotal(key, total, available);
            return available;
        }
    }
}
