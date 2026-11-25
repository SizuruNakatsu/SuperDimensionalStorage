namespace SuperDimensionalStorage
{
    /// <summary>
    /// 端点距离缓存时钟：地图 / 优先级 / 端点集合变化时推进，SDS_RulesImpl.EndpointVersion 读取它。
    /// 入 / 出：无业务输入输出，只有版本号。
    /// 区别：与 StockVersion / ReservationVersion 不同，它只服务端点相关缓存（距离、候选）。
    /// </summary>
    internal static class SDS_EndpointContractCache
    {
        /// <summary>
        /// 当前端点契约版本。
        /// 入：无。
        /// 出：版本号；纯读。
        /// 区别：StockVersion 管库存，本版本管端点集合与优先级。
        /// </summary>
        public static int Version { get; private set; }

        /// <summary>
        /// 推进端点版本（端点集合 / 优先级 / 地图集合变化时调用）。
        /// 入：无。
        /// 出：无。
        /// 区别：Reset 也推进版本，但语义是「进程级清场」，两者对读侧等价。
        /// </summary>
        public static void NotifyEndpointsChanged()
        {
            Version++;
        }

        /// <summary>
        /// 清进程级端点缓存时钟（新档 / 退出时由 SDS_StaticState 调用）。
        /// 入：无。
        /// 出：无（只推进版本，让读侧缓存失效）。
        /// 区别：NotifyEndpointsChanged 是业务变化；本方法是进程级重置。
        /// </summary>
        public static void Reset()
        {
            Version++;
        }
    }
}
