using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// W62：任何“存入超维空间”的决议，若执行者是可用端口且可接收该实物，
    /// 目的地折叠为执行者自身端口；物品到手后由调用方经唯一入口 TryDeposit 入账。
    /// 显式指定端口、取货与内部回充不经过本判定。
    /// </summary>
    internal static class SDS_SelfDepositFold
    {
        // RULE:W62
        /// <summary>
        /// 自动 / 强制搬运的自身折叠：接收口径沿用调用方已定的在途标记事实。
        /// checkAccess = true；手动入口请走 TryResolveManual，cell-only 自存请走 TryResolveAuto。
        /// </summary>
        internal static bool TryResolve(Pawn pawn, Thing thing, bool allowProducedMarked,
            out ISDS_Port port)
        {
            if (!TryResolvePort(pawn, thing, checkAccess: true, out port))
            {
                return false;
            }
            if (SDS_EndpointUtility.Accepts(port, thing, allowProducedMarked))
            {
                return true;
            }
            port = null;
            return false;
        }

        /// <summary>
        /// W57 手动入口的自身折叠：只经 AcceptsForManual，不在调用点传字面量；跳过自动路径的 S18 门。
        /// hasPort 表示“可用自身端口是否存在”，供手动失败分类复用，避免二次解析。
        /// </summary>
        internal static bool TryResolveManual(Pawn pawn, Thing thing, out ISDS_Port port,
            out bool hasPort)
        {
            hasPort = TryGetSelfPort(pawn, out port);
            if (!hasPort)
            {
                return false;
            }
            if (SDS_EndpointUtility.AcceptsForManual(port, thing))
            {
                return true;
            }
            port = null;
            return false;
        }

        /// <summary>W63 cell-only 自存的自身折叠：自动口径，经 AcceptsForAuto，不通过在途物化标记闸。</summary>
        internal static bool TryResolveAuto(Pawn pawn, Thing thing, out ISDS_Port port)
        {
            if (!TryResolvePort(pawn, thing, checkAccess: true, out port))
            {
                return false;
            }
            if (SDS_EndpointUtility.AcceptsForAuto(port, thing))
            {
                return true;
            }
            port = null;
            return false;
        }

        /// <summary>
        /// 自身端口可用性的唯一解析：Provider 现读句柄，再接货前复核 Operational。
        /// 手动 / 自动 / cell-only 共用这里，不各自另写一套。
        /// </summary>
        internal static bool TryGetSelfPort(Pawn pawn, out ISDS_Port port)
        {
            port = null;
            if (pawn == null || SDS_Host.Ports == null)
            {
                return false;
            }
            if (!SDS_Host.Ports.TryGetSelfPort(pawn, out port) || port == null || !port.Operational)
            {
                port = null;
                return false;
            }
            return true;
        }

        private static bool TryResolvePort(Pawn pawn, Thing thing, bool checkAccess,
            out ISDS_Port port)
        {
            port = null;
            if (pawn == null || thing == null || thing.Destroyed || !pawn.Spawned || pawn.Map == null)
            {
                return false;
            }
            // S18 是“设置门”；自动派活的玩家阵营门在 SDS_Destinations.CommonGates，
            // 二者不同层，不互相替代。手动入口不走本方法。
            if (checkAccess && SDS_AccessRules.BlocksNonPlayer(pawn))
            {
                return false;
            }
            return TryGetSelfPort(pawn, out port);
        }
    }
}
