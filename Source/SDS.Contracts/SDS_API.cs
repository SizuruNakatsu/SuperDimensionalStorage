using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>公共 API 的调用方上下文；非 Pawn 调用方把 Pawn 留空。</summary>
    public struct SDS_ApiCaller
    {
        public Faction faction;
        public Pawn pawn;
        public Thing claimOwner;

        public bool IsNonPawn
        {
            get { return pawn == null; }
        }

        public static SDS_ApiCaller FromPawn(Pawn pawn)
        {
            return new SDS_ApiCaller
            {
                faction = pawn != null ? pawn.Faction : null,
                pawn = pawn,
                claimOwner = pawn
            };
        }

        public static SDS_ApiCaller FromFaction(Faction faction, Thing claimOwner = null)
        {
            return new SDS_ApiCaller
            {
                faction = faction,
                pawn = null,
                claimOwner = claimOwner
            };
        }
    }

    /// <summary>公共 API 的结构化结果码；正常拒绝不写日志，异常统一 InternalError。</summary>
    public enum SDS_ApiResultCode
    {
        Ok,
        InvalidArgument,
        GridUnavailable,
        NoEndpoint,
        EndpointUnavailable,
        NotDepositable,
        Forbidden,
        NotOwnedByCaller,
        NotPlayerFaction,
        NoStock,
        UnresolvedDef,
        RebuildFailed,
        NotWithdrawable,
        DepositFailed,
        TransactionFailed,
        InternalError
    }

    /// <summary>只读库存快照；不建事务、不预留、不扣账。</summary>
    public struct SDS_ApiStockInfo
    {
        public SDS_StockKey key;
        public long total;
        public long available;
        public bool forbidden;
        public bool resolved;
        public SDS_SupplyFailure failure;
        public SDS_ReadVersions versions;
        public SDS_ApiResultCode result;
    }
}
