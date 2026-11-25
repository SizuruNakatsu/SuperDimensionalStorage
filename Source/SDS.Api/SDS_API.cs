using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 对外五轴门面（看 / 读 / 找 / 取 / 存）。
    /// 只翻译调用方上下文并转发唯一实现：库存、容量、筛选、端点排序与事务全部由 Core 决定；
    /// 本类只保留玩家阵营 / 所有权 / 禁止 / 同地图这层调用门，不定义第二套规则。
    /// </summary>
    public static partial class SDS_API
    {
        /// <summary>
        /// 公共 API 契约版本。程序集改名（SDS.Api）后第三方须重新 soft-depend。
        /// E5：Version 变更即兼容性声明；五轴签名不变，保真取货统一走精确计划。
        /// </summary>
        public const int Version = 4;

        // ---------- 看 ----------

        /// <summary>「看」：该地图上原版枚举可见的该 def 实物（含镜像 / 代理）；只读。</summary>
        public static IReadOnlyList<Thing> VisibleThingsOfDef(Map map, ThingDef def)
        {
            try
            {
                ISDS_MirrorQuery mirrors = SDS_Host.MirrorQuery;
                if (mirrors == null || map == null || def == null)
                {
                    return new List<Thing>();
                }
                return mirrors.VisibleThingsOfDef(map, def) ?? (IReadOnlyList<Thing>)new List<Thing>();
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/看库存异常", e);
                return new List<Thing>();
            }
        }

        // ---------- 读 ----------

        /// <summary>「读」：某品类的总数 / 可用量 / 是否禁用 / 供给失败原因；只读，不建事务。</summary>
        public static SDS_ApiStockInfo ReadStock(SDS_StockKey key)
        {
            SDS_ApiStockInfo info = default;
            info.key = key;
            info.result = SDS_ApiResultCode.Ok;
            try
            {
                info.versions = CaptureReadVersions();
                info.resolved = key != null && key.Resolved;
                ISDS_LedgerRead ledger = SDS_Core.Ledger;
                if (key != null && ledger != null)
                {
                    info.forbidden = ledger.IsForbidden(key);
                    info.total = key.preserved ? ledger.GetPreservedCount(key) : ledger.GetCount(key);
                }
                if (!SDS_Rules.Available)
                {
                    // R17：G 关闭时账本保留（total 照给），读侧归零由 SDS_Availability 统一给出。
                    info.result = SDS_ApiResultCode.GridUnavailable;
                }
                info.available = SDS_Availability.GetAvailableCount(key);
                info.failure = key != null ? SDS_Availability.GetFailure(key) : SDS_SupplyFailure.UnresolvedDef;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/读库存异常", e);
                info.result = SDS_ApiResultCode.InternalError;
                info.failure = SDS_SupplyFailure.Insufficient;
            }
            return info;
        }

        /// <summary>按实物读取其所属通道（虚拟 / 保真）的库存信息；通道判定只来自 Core。</summary>
        public static SDS_ApiStockInfo ReadStockFor(Thing thing)
        {
            try
            {
                if (!SDS_Rules.TryGetStockKey(thing, out SDS_StockKey key))
                {
                    return ReadStock(null);
                }
                return ReadStock(key);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/读实物库存异常", e);
                SDS_ApiStockInfo info = default;
                info.result = SDS_ApiResultCode.InternalError;
                info.failure = SDS_SupplyFailure.Insufficient;
                return info;
            }
        }

        /// <summary>
        /// 「取」的数量夹取：min(需求, 可用量)；任一非正即 0（本次不取）。
        /// W56 的唯一实现在 Core 的 SDS_Availability，本方法只做门面转发。
        /// </summary>
        public static int ClampTakeToAvailable(int wanted, long available)
            => SDS_Availability.ClampTakeToAvailable(wanted, available);

        /// <summary>「读」：捕获当前只读版本快照，供调用方判定缓存是否失效。</summary>
        public static SDS_ReadVersions CaptureReadVersions()
        {
            SDS_ReadVersions versions = default;
            try
            {
                versions.tick = CurrentTick();
                ISDS_MirrorQuery mirrors = SDS_Host.MirrorQuery;
                versions.mirror = mirrors != null ? mirrors.Version : 0;
                versions.endpoint = SDS_Rules.EndpointVersion;
                ISDS_LedgerRead ledger = SDS_Core.Ledger;
                if (ledger != null)
                {
                    versions.stock = ledger.StockVersion;
                    versions.reservation = ledger.ReservationVersion;
                    versions.filter = ledger.FilterVersion;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/读版本异常", e);
                return default;
            }
            return versions;
        }

        // ---------- 找 ----------

        /// <summary>「找」：为非 Pawn 调用方找可用的存入端点；选择顺序由 Core 决定。</summary>
        public static bool TryFindDepositEndpoint(Thing thing, Map map, IntVec3 reference, SDS_ApiCaller caller,
            out Thing endpoint, out SDS_ApiResultCode code)
        {
            endpoint = null;
            try
            {
                if (!ValidateDepositCall(thing, map, caller, out code))
                {
                    return false;
                }
                Map actualMap = map ?? thing.MapHeld;
                IntVec3 actualReference = reference.IsValid
                    ? reference
                    : (thing.SpawnedOrAnyParentSpawned
                        ? thing.PositionHeld
                        : (actualMap != null ? actualMap.Center : IntVec3.Invalid));
                SDS_EndpointDecision decision =
                    SDS_Rules.SelectEndpoint(SDS_EndpointQuery.DepositForNonPawn(thing, actualMap, actualReference));
                if (!decision.allowed || decision.port == null || decision.port.Thing == null)
                {
                    code = SDS_ApiResultCode.NoEndpoint;
                    return false;
                }
                if (!IsEndpointOwnedBy(decision.port.Thing, caller))
                {
                    code = SDS_ApiResultCode.NotOwnedByCaller;
                    return false;
                }
                endpoint = decision.port.Thing;
                code = SDS_ApiResultCode.Ok;
                return true;
            }
            catch (Exception e)
            {
                endpoint = null;
                return Fail("存/找端点异常", e, out code);
            }
        }

        /// <summary>「找」：为 Pawn 调用方找取物点（自身端口或最近建筑端点）；只读，不预留。</summary>
        public static bool TryFindWithdrawPoint(Pawn actor, out Thing endpoint, out bool self,
            out SDS_ApiResultCode code)
        {
            endpoint = null;
            self = false;
            try
            {
                if (!IsPlayerPawn(actor))
                {
                    code = SDS_ApiResultCode.InvalidArgument;
                    return false;
                }
                if (!TrySelectWithdrawPoint(actor, out SDS_EndpointDecision decision, out code))
                {
                    return false;
                }
                if (decision.port.Thing == null)
                {
                    code = SDS_ApiResultCode.NoEndpoint;
                    return false;
                }
                endpoint = decision.port.Thing;
                self = decision.port.IsSelf;
                code = SDS_ApiResultCode.Ok;
                return true;
            }
            catch (Exception e)
            {
                endpoint = null;
                self = false;
                return Fail("取/找取物点异常", e, out code);
            }
        }

        /// <summary>取物点决议的唯一入口：只做 SelectEndpoint + 硬门；调用方各自保留调用门。</summary>
        private static bool TrySelectWithdrawPoint(Pawn actor, out SDS_EndpointDecision decision,
            out SDS_ApiResultCode code)
        {
            decision = SDS_Rules.SelectEndpoint(SDS_EndpointQuery.WithdrawPoint(actor));
            if (!decision.allowed || decision.port == null)
            {
                code = SDS_ApiResultCode.NoEndpoint;
                return false;
            }
            code = SDS_ApiResultCode.Ok;
            return true;
        }

        // ---------- 存 ----------

        /// <summary>「存」：选端点并立即入账（单件、原子）；失败时实物原样留在调用方。</summary>
        public static bool TryDepositNow(Thing thing, Map map, IntVec3 reference, SDS_ApiCaller caller,
            out int accepted, out SDS_ApiResultCode code)
        {
            accepted = 0;
            try
            {
                if (!TryFindDepositEndpoint(thing, map, reference, caller, out Thing endpoint, out code))
                {
                    return false;
                }
                return TryDepositAt(thing, endpoint, caller, out accepted, out code);
            }
            catch (Exception e)
            {
                accepted = 0;
                return Fail("存/立即入账异常", e, out code);
            }
        }

        /// <summary>「存」：向已知端点入账（单件、原子）；端点必须是本 Mod 的可用端口。</summary>
        public static bool TryDepositAt(Thing thing, Thing endpoint, SDS_ApiCaller caller,
            out int accepted, out SDS_ApiResultCode code)
        {
            accepted = 0;
            try
            {
                if (!ValidateDepositCall(thing, endpoint != null ? endpoint.MapHeld : null, caller, out code))
                {
                    return false;
                }
                if (endpoint == null || endpoint.Destroyed)
                {
                    code = SDS_ApiResultCode.EndpointUnavailable;
                    return false;
                }
                if (!IsEndpointOwnedBy(endpoint, caller))
                {
                    code = SDS_ApiResultCode.NotOwnedByCaller;
                    return false;
                }
                if (!TryGetOperationalPort(endpoint, out ISDS_Port port))
                {
                    code = SDS_ApiResultCode.EndpointUnavailable;
                    return false;
                }
                if (!SDS_Rules.TryDeposit(thing, port, out accepted) || accepted <= 0)
                {
                    accepted = 0;
                    code = SDS_ApiResultCode.DepositFailed;
                    return false;
                }
                code = SDS_ApiResultCode.Ok;
                return true;
            }
            catch (Exception e)
            {
                accepted = 0;
                return Fail("存/端点入账异常", e, out code);
            }
        }

        // ---------- 取 ----------

        /// <summary>「取」：事务级取出虚拟库存，返回已物化的真实实物；交付由调用方完成。</summary>
        public static bool TryWithdraw(SDS_StockKey key, int quantity, Pawn actor, SDS_WithdrawMode mode,
            out List<Thing> made, out SDS_ApiResultCode code)
        {
            return TryWithdraw(key, quantity, mode, SDS_ApiCaller.FromPawn(actor), out made, out code);
        }

        /// <summary>「取」的调用方上下文重载：支持非 Pawn 调用方。</summary>
        public static bool TryWithdraw(SDS_StockKey key, int quantity, SDS_WithdrawMode mode,
            SDS_ApiCaller caller, out List<Thing> made, out SDS_ApiResultCode code)
        {
            made = null;
            try
            {
                if (!ValidateWithdrawCall(caller, out code))
                {
                    return false;
                }
                if (key == null || quantity <= 0 || mode == SDS_WithdrawMode.VirtualHandoff)
                {
                    code = SDS_ApiResultCode.InvalidArgument;
                    return false;
                }
                if (key.preserved)
                {
                    // 保真通道只经精确真身计划；本入口只处理虚拟通道。
                    code = SDS_ApiResultCode.InvalidArgument;
                    return false;
                }
                if (caller.pawn != null
                    && (!caller.pawn.Spawned || caller.pawn.Dead || caller.pawn.Map == null))
                {
                    code = SDS_ApiResultCode.InvalidArgument;
                    return false;
                }
                SDS_SupplyFailure failure = SDS_Availability.GetFailure(key);
                if (failure != SDS_SupplyFailure.None)
                {
                    code = MapSupplyFailure(failure);
                    return false;
                }
                SDS_WithdrawPlan plan = new SDS_WithdrawPlan
                {
                    query = new SDS_WithdrawQuery
                    {
                        key = key,
                        quantity = quantity,
                        preserved = false
                    },
                    endpoint = null,
                    mode = mode
                };
                if (!SDS_Rules.TryWithdraw(plan, caller.pawn, out made) || made == null || made.Count == 0)
                {
                    made = null;
                    code = SDS_ApiResultCode.TransactionFailed;
                    return false;
                }
                code = SDS_ApiResultCode.Ok;
                return true;
            }
            catch (Exception e)
            {
                made = null;
                return Fail("取/事务异常", e, out code);
            }
        }

        /// <summary>「取」：从镜像取出；返回真实物化结果，交付由调用方完成。</summary>
        public static bool TryWithdrawFromMirror(Thing mirror, int quantity, SDS_WithdrawMode mode,
            SDS_ApiCaller caller, out Thing taken, out SDS_ApiResultCode code)
        {
            taken = null;
            try
            {
                if (!ValidateWithdrawCall(caller, out code))
                {
                    return false;
                }
                if (mirror == null || quantity <= 0 || mode == SDS_WithdrawMode.VirtualHandoff)
                {
                    code = SDS_ApiResultCode.NotWithdrawable;
                    return false;
                }
                if (caller.pawn != null
                    && (!caller.pawn.Spawned || caller.pawn.Dead || caller.pawn.Map != mirror.Map))
                {
                    code = SDS_ApiResultCode.InvalidArgument;
                    return false;
                }
                if (!SDS_Rules.TryTakeMirror(mirror, quantity, SDS_Host.MirrorPhysical, out taken) || taken == null)
                {
                    taken = null;
                    code = SDS_ApiResultCode.TransactionFailed;
                    return false;
                }
                code = SDS_ApiResultCode.Ok;
                return true;
            }
            catch (Exception e)
            {
                taken = null;
                return Fail("取/镜像取出异常", e, out code);
            }
        }

        /// <summary>「取」：按精确真身计划取出保真库存；返回真实物化结果。</summary>
        public static bool TryWithdrawPreservedExact(List<ThingCount> plan, SDS_WithdrawMode mode,
            SDS_ApiCaller caller, out List<Thing> made, out SDS_ApiResultCode code)
        {
            made = null;
            try
            {
                if (!ValidateWithdrawCall(caller, out code))
                {
                    return false;
                }
                if (plan == null || plan.Count == 0 || mode == SDS_WithdrawMode.VirtualHandoff)
                {
                    code = SDS_ApiResultCode.InvalidArgument;
                    return false;
                }
                // 取用口径：禁用品类不得经精确计划绕过（可用量 / 预留仍由 Core 的 Begin 复核）。
                for (int i = 0; i < plan.Count; i++)
                {
                    Thing thing = plan[i].Thing;
                    if (thing == null)
                    {
                        continue;
                    }
                    if (SDS_Rules.TryGetStockKey(thing, out SDS_StockKey key)
                        && key != null && SDS_Core.Ledger != null
                        && SDS_Core.Ledger.IsForbidden(key))
                    {
                        code = SDS_ApiResultCode.Forbidden;
                        return false;
                    }
                }
                if (!SDS_Rules.TryWithdrawPreservedExact(plan, caller.pawn, out made)
                    || made == null || made.Count == 0)
                {
                    made = null;
                    code = SDS_ApiResultCode.TransactionFailed;
                    return false;
                }
                code = SDS_ApiResultCode.Ok;
                return true;
            }
            catch (Exception e)
            {
                made = null;
                return Fail("取/保真精确取出异常", e, out code);
            }
        }

        // ---------- 内部：统一调用门与映射 ----------

        private static bool ValidateDepositCall(Thing thing, Map map, SDS_ApiCaller caller,
            out SDS_ApiResultCode code)
        {
            code = SDS_ApiResultCode.Ok;
            if (thing == null || thing.Destroyed || thing.stackCount <= 0)
            {
                code = SDS_ApiResultCode.InvalidArgument;
                return false;
            }
            if (!IsPlayerFaction(caller.faction))
            {
                code = SDS_ApiResultCode.NotPlayerFaction;
                return false;
            }
            if (!SDS_Rules.Available)
            {
                code = SDS_ApiResultCode.GridUnavailable;
                return false;
            }
            if (!IsOwnedByCaller(thing, caller.faction))
            {
                code = SDS_ApiResultCode.NotOwnedByCaller;
                return false;
            }
            if (ForbidUtility.IsForbidden(thing, caller.faction))
            {
                code = SDS_ApiResultCode.Forbidden;
                return false;
            }
            if (!SDS_Rules.CanDeposit(thing))
            {
                code = SDS_ApiResultCode.NotDepositable;
                return false;
            }
            Map thingMap = thing.MapHeld;
            if (thingMap != null && map != null && map != thingMap)
            {
                code = SDS_ApiResultCode.InvalidArgument;
                return false;
            }
            return true;
        }

        private static bool ValidateWithdrawCall(SDS_ApiCaller caller, out SDS_ApiResultCode code)
        {
            code = SDS_ApiResultCode.Ok;
            if (!IsPlayerFaction(caller.faction))
            {
                code = SDS_ApiResultCode.NotPlayerFaction;
                return false;
            }
            if (!SDS_Rules.Available)
            {
                code = SDS_ApiResultCode.GridUnavailable;
                return false;
            }
            return true;
        }

        private static bool IsPlayerPawn(Pawn actor)
        {
            return actor != null && actor.Spawned && actor.Map != null && !actor.Dead
                && IsPlayerFaction(actor.Faction);
        }

        private static bool IsPlayerFaction(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }
            try
            {
                return faction == Faction.OfPlayerSilentFail;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsEndpointOwnedBy(Thing endpoint, SDS_ApiCaller caller)
        {
            return endpoint != null && (endpoint.Faction == null || endpoint.Faction == caller.faction);
        }

        /// <summary>端点身份与可用性的唯一判定：五轴与适配器桥共用，不重复 TryGetPort。</summary>
        private static bool TryGetOperationalPort(Thing thing, out ISDS_Port port)
        {
            port = null;
            if (thing == null)
            {
                return false;
            }
            ISDS_PortProvider ports = SDS_Host.Ports;
            return ports != null && ports.TryGetPort(thing, out port)
                && port != null && port.Operational;
        }

        /// <summary>调用方是否拥有这件实物：阵营 + 根持有者；无主物品放行。</summary>
        private static bool IsOwnedByCaller(Thing thing, Faction faction)
        {
            if (thing == null || faction == null)
            {
                return false;
            }
            if (thing.Faction != null && thing.Faction != faction)
            {
                return false;
            }
            Thing root = thing.SpawnedOrAnyParentSpawned ? thing.SpawnedParentOrMe : thing;
            if (root is Pawn rootPawn && rootPawn.Faction != faction)
            {
                return false;
            }
            if (root != null && root.Faction != null && root.Faction != faction)
            {
                return false;
            }
            return true;
        }

        private static SDS_ApiResultCode MapSupplyFailure(SDS_SupplyFailure failure)
        {
            switch (failure)
            {
                case SDS_SupplyFailure.UnresolvedDef:
                    return SDS_ApiResultCode.UnresolvedDef;
                case SDS_SupplyFailure.Forbidden:
                    return SDS_ApiResultCode.Forbidden;
                case SDS_SupplyFailure.Insufficient:
                    return SDS_ApiResultCode.NoStock;
                case SDS_SupplyFailure.RebuildFailed:
                    return SDS_ApiResultCode.RebuildFailed;
                default:
                    return SDS_ApiResultCode.TransactionFailed;
            }
        }

        private static bool Fail(string where, Exception e, out SDS_ApiResultCode code)
        {
            SDS_Diag.ErrorOnce("公共API/" + where, e);
            code = SDS_ApiResultCode.InternalError;
            return false;
        }

        /// <summary>Find.TickManager 是 Api 唯一允许接触 Find.* 的地方。</summary>
        private static int CurrentTick()
        {
            try
            {
                return Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
