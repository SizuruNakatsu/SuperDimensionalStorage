using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 公共 API 的适配器桥（§7.5）：public 以跨程序集给 SDS.ThirdModCompat 使用，
    /// 不属于五轴 Version 契约；每个方法只转发 Core / Host 的唯一实现。
    /// </summary>
    public static partial class SDS_API
    {
        // ---------- 端点 ----------

        /// <summary>本图是否有任一可用端口（建筑 / 带芯片 Pawn / 机械体）；走同一端点决议。</summary>
        public static bool HasAnyEndpoint(Map map)
        {
            try
            {
                if (map == null)
                {
                    return false;
                }
                SDS_EndpointQuery query = default;
                query.ApplyHostPriority();
                query.op = SDS_EndpointOp.Withdraw;
                query.map = map;
                query.reference = IntVec3.Invalid;
                query.requireReach = false;
                query.allowSelf = true;
                SDS_EndpointDecision decision = SDS_Rules.SelectEndpoint(query);
                return decision.allowed && decision.port != null;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/端点存在性查询异常", e);
                return false;
            }
        }

        /// <summary>原版 DoBill 半径语义：建筑端点在半径内且从 root 可达。</summary>
        public static bool HasUsableBuildingEndpointWithin(Map map, IntVec3 root, float radius)
        {
            try
            {
                if (map == null)
                {
                    return false;
                }
                ISDS_PortProvider ports = SDS_Host.Ports;
                if (ports == null || !ports.IsLocalUsable(map))
                {
                    return false;
                }
                List<ISDS_Port> candidates = new List<ISDS_Port>();
                SDS_EndpointQuery query = default;
                query.op = SDS_EndpointOp.Withdraw;
                query.map = map;
                query.reference = root;
                query.requireReach = false;
                query.allowSelf = false;
                ports.AppendCandidates(query, candidates);
                float radiusSquared = Math.Max(0f, radius) * Math.Max(0f, radius);
                TraverseParms parms = TraverseParms.For(TraverseMode.PassDoors);
                for (int i = 0; i < candidates.Count; i++)
                {
                    ISDS_Port port = candidates[i];
                    if (port == null || port.Pawn != null || port.Thing == null
                        || port.IsSelf || !port.Operational)
                    {
                        continue;
                    }
                    if (port.RankDistanceSquared(root) > radiusSquared)
                    {
                        continue;
                    }
                    if (!root.IsValid || !root.InBounds(map))
                    {
                        return false;
                    }
                    try
                    {
                        if (map.reachability.CanReach(root, (LocalTargetInfo)port.Thing,
                                port.AccessMode, parms))
                        {
                            return true;
                        }
                    }
                    catch (Exception)
                    {
                        // 单个端口不可达不影响其它候选。
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/端点半径可达查询异常", e);
                return false;
            }
        }

        // ---------- 账本只读 ----------

        /// <summary>全局账本是否可用（不检查本图是否暴露端点）。</summary>
        public static bool IsGridAvailable()
        {
            return SDS_Rules.Available && SDS_Core.Ledger != null;
        }

        /// <summary>把当前保真真身追加到列表（不改变账本）。</summary>
        public static void AppendPreservedThings(List<Thing> into)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger != null)
            {
                ledger.AppendPreservedThings(into);
            }
        }

        /// <summary>该实物是否仍是账本中的存活保真项。</summary>
        public static bool IsLivePreservedThing(Thing thing)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            return ledger != null && thing != null && !thing.Destroyed && ledger.IsPreserved(thing);
        }

        /// <summary>该品类是否被禁用。</summary>
        public static bool IsStockForbidden(SDS_StockKey key)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            return ledger != null && key != null && ledger.IsForbidden(key);
        }

        /// <summary>实例 → 通道 / 键（转发 Core 唯一规则）。</summary>
        public static bool TryGetStockKey(Thing thing, out SDS_StockKey key)
        {
            return SDS_Rules.TryGetStockKey(thing, out key);
        }

        /// <summary>def 级总可用量：虚拟 + 保真，统一转发 Core 的唯一读口。</summary>
        public static long GetAvailableCountForDef(ThingDef def)
        {
            try
            {
                return SDS_Rules.GetAvailableCountForDef(def);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/可用数量异常", e);
                return 0L;
            }
        }

        /// <summary>某 def 的保真可用量：按品类键去重后累加。</summary>
        public static long GetPreservedAvailableCount(ThingDef def)
        {
            try
            {
                if (def == null)
                {
                    return 0L;
                }
                List<Thing> all = new List<Thing>();
                AppendPreservedThings(all);
                HashSet<SDS_StockKey> seen = new HashSet<SDS_StockKey>();
                long total = 0L;
                for (int i = 0; i < all.Count; i++)
                {
                    Thing thing = all[i];
                    if (thing == null || thing.Destroyed || thing.def != def)
                    {
                        continue;
                    }
                    if (!SDS_Rules.TryGetStockKey(thing, out SDS_StockKey key)
                        || key == null || !key.preserved || !seen.Add(key))
                    {
                        continue;
                    }
                    total += SDS_Availability.GetAvailableCount(key);
                }
                return total;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/保真可用量查询异常", e);
                return 0L;
            }
        }

        /// <summary>配方槽位是否接受某 def / 实物；只转发 Core 补料规划。</summary>
        public static bool IsBillIngredientAllowed(IngredientCount slot, Thing thing, ThingDef def,
            Bill bill, RecipeDef recipe, List<ThingDef> allowedIngredients, ThingFilter corpseFilter)
        {
            try
            {
                Thing giver = bill?.billStack?.billGiver as Thing;
                return SDS_Rules.AllowsDef(slot, thing, def, bill, recipe, allowedIngredients,
                    corpseFilter, candidate => SDS_MedicalGate.Allows(giver, candidate));
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/补料槽位判定异常", e);
                return false;
            }
        }

        // ---------- 按 def 批量取出 ----------

        /// <summary>按 def 取出虚拟库存；不足则整批回充，不留下半截。</summary>
        public static bool TryWithdrawVirtualByDef(ThingDef def, int count, Map map,
            SDS_ApiCaller caller, out List<Thing> made, out SDS_ApiResultCode code)
        {
            made = null;
            try
            {
                if (!ValidateWithdrawCall(caller, out code))
                {
                    return false;
                }
                if (def == null || count <= 0)
                {
                    code = SDS_ApiResultCode.InvalidArgument;
                    return false;
                }
                ISDS_LedgerRead ledger = SDS_Core.Ledger;
                if (ledger == null || !SDS_Rules.Available)
                {
                    code = SDS_ApiResultCode.GridUnavailable;
                    return false;
                }
                List<SDS_StockKey> keys = new List<SDS_StockKey>();
                ledger.CopyKeys(keys);
                List<Thing> all = new List<Thing>();
                int remaining = count;
                for (int i = 0; i < keys.Count && remaining > 0; i++)
                {
                    SDS_StockKey key = keys[i];
                    if (key == null || key.preserved || key.thingDef != def || ledger.IsForbidden(key))
                    {
                        continue;
                    }
                    long available = SDS_Availability.GetAvailableCount(key);
                    if (available <= 0L)
                    {
                        continue;
                    }
                    int take = SDS_Availability.ClampTakeToAvailable(remaining, available);
                    if (!TryWithdrawVirtualExact(key, take, caller.pawn, out List<Thing> part))
                    {
                        continue;
                    }
                    all.AddRange(part);
                    remaining -= SumUnits(part);
                }
                if (all.Count == 0 || remaining > 0)
                {
                    ReturnVirtualList(all, map);
                    code = SDS_ApiResultCode.TransactionFailed;
                    return false;
                }
                made = all;
                code = SDS_ApiResultCode.Ok;
                return true;
            }
            catch (Exception e)
            {
                made = null;
                return Fail("取/按def虚拟取出异常", e, out code);
            }
        }

        /// <summary>按 def 取出保真库存；只接受精确真身计划，不足则整批回充。</summary>
        public static bool TryWithdrawPreservedByDef(ThingDef def, int count, Map map,
            SDS_ApiCaller caller, out List<Thing> made, out SDS_ApiResultCode code)
        {
            made = null;
            try
            {
                if (!ValidateWithdrawCall(caller, out code))
                {
                    return false;
                }
                if (def == null || count <= 0)
                {
                    code = SDS_ApiResultCode.InvalidArgument;
                    return false;
                }
                List<Thing> all = new List<Thing>();
                AppendPreservedThings(all);
                List<ThingCount> plan = new List<ThingCount>();
                int remaining = count;
                for (int i = 0; i < all.Count && remaining > 0; i++)
                {
                    Thing thing = all[i];
                    if (thing == null || thing.Destroyed || thing.def != def || thing.stackCount <= 0
                        || !IsLivePreservedThing(thing))
                    {
                        continue;
                    }
                    if (!SDS_Rules.TryGetStockKey(thing, out SDS_StockKey key)
                        || key == null || !key.preserved || IsStockForbidden(key))
                    {
                        continue;
                    }
                    int take = Math.Min(remaining, thing.stackCount);
                    if (take <= 0)
                    {
                        continue;
                    }
                    plan.Add(new ThingCount(thing, take));
                    remaining -= take;
                }
                if (remaining > 0 || plan.Count == 0)
                {
                    code = SDS_ApiResultCode.NoStock;
                    return false;
                }
                if (!SDS_Rules.TryWithdrawPreservedExact(plan, caller.pawn, out made)
                    || made == null || made.Count == 0)
                {
                    made = null;
                    code = SDS_ApiResultCode.TransactionFailed;
                    return false;
                }
                if (SumUnits(made) < count)
                {
                    ReturnPreservedList(made, map);
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
                return Fail("取/按def保真取出异常", e, out code);
            }
        }

        // ---------- Job 派活 / 来源标记 ----------

        /// <summary>只读：是否为本 Mod 刚物化、等待原版消费的实物（来源排除）。</summary>
        public static bool IsProducedPendingConsumption(Thing thing)
        {
            try
            {
                return thing != null && SDS_Rules.IsProducedMarked(thing);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/来源排除查询异常", e);
                return false;
            }
        }

        /// <summary>给刚物化的实物打来源排除标记；只转发标记表。</summary>
        public static void MarkProducedPendingConsumption(IList<Thing> things)
        {
            if (things == null)
            {
                return;
            }
            try
            {
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] != null)
                    {
                        SDS_Rules.MarkProduced(things[i]);
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/来源排除标记异常", e);
            }
        }

        /// <summary>只读：小人的芯片端口是否可用。</summary>
        public static bool IsPawnPortOperational(Pawn pawn)
        {
            try
            {
                return pawn != null && SDS_ImplantUtility.IsPortOperational(pawn);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/端口可用性查询异常", e);
                return false;
            }
        }

        /// <summary>Job 型第三方候选闸门：端点能接这件货时返回 true（只做决议，不派活）。</summary>
        public static bool TryTakeDepositCandidate(Pawn pawn, Thing thing, bool forced,
            out SDS_ApiResultCode code)
        {
            code = SDS_ApiResultCode.Ok;
            ISDS_DepositJobs jobs = SDS_Host.DepositJobs;
            if (jobs == null)
            {
                code = SDS_ApiResultCode.GridUnavailable;
                return false;
            }
            if (!jobs.CanTakeCandidate(pawn, thing, forced))
            {
                code = SDS_ApiResultCode.NotDepositable;
                return false;
            }
            return true;
        }

        /// <summary>Job 型第三方派活出口：统一存入服务建 Job，不新增第三条派活路径。</summary>
        public static bool TryMakeDepositJob(Pawn pawn, Thing thing, bool forced, Job vanillaJob,
            bool multi, out Job job, out SDS_ApiResultCode code)
        {
            job = null;
            code = SDS_ApiResultCode.Ok;
            if (pawn == null || thing == null)
            {
                code = SDS_ApiResultCode.InvalidArgument;
                return false;
            }
            ISDS_DepositJobs jobs = SDS_Host.DepositJobs;
            if (jobs == null)
            {
                code = SDS_ApiResultCode.GridUnavailable;
                return false;
            }
            try
            {
                job = jobs.TryMakeJob(pawn, thing, forced, vanillaJob, multi);
            }
            catch (Exception e)
            {
                job = null;
                return Fail("存/派活异常", e, out code);
            }
            if (job == null)
            {
                code = SDS_ApiResultCode.NotDepositable;
                return false;
            }
            return true;
        }

        /// <summary>背包卸载派活出口：能就地存时返回本 Mod 的多件 Job。</summary>
        public static bool TryMakeDepositJobFromInventory(Pawn pawn, IList<Thing> items,
            out Job job, out SDS_ApiResultCode code)
        {
            job = null;
            code = SDS_ApiResultCode.Ok;
            if (pawn == null || items == null || items.Count == 0)
            {
                code = SDS_ApiResultCode.InvalidArgument;
                return false;
            }
            if (!IsPawnPortOperational(pawn))
            {
                code = SDS_ApiResultCode.NoEndpoint;
                return false;
            }
            ISDS_DepositJobs jobs = SDS_Host.DepositJobs;
            if (jobs == null)
            {
                code = SDS_ApiResultCode.GridUnavailable;
                return false;
            }
            try
            {
                job = jobs.TryMakeFromInventory(pawn, items);
            }
            catch (Exception e)
            {
                job = null;
                return Fail("存/背包派活异常", e, out code);
            }
            if (job == null)
            {
                code = SDS_ApiResultCode.NotDepositable;
                return false;
            }
            return true;
        }

        public static bool IsDepositJob(Job job)
        {
            return job != null && SDS_JobDefOf.DepositToGrid != null && job.def == SDS_JobDefOf.DepositToGrid;
        }

        public static bool IsManualDepositAvailable(Pawn pawn)
        {
            ISDS_DepositJobs jobs = SDS_Host.DepositJobs;
            if (jobs == null)
            {
                return false;
            }
            try
            {
                return jobs.IsManualDepositAvailable(pawn);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/手动存入查询异常", e);
                return false;
            }
        }

        // ---------- 只读辅助 ----------

        /// <summary>只读：W33「武器 / 装备强制虚拟化」开关当前值（兼容层缓存失效判定用）。</summary>
        public static bool IsForceWeaponApparelVirtual()
        {
            return SDS_Config.ForceWeaponApparelVirtual;
        }

        /// <summary>只读：某品类的可用量（统一 Availability 口径）。</summary>
        public static long GetAvailableCount(SDS_StockKey key)
        {
            try
            {
                return SDS_Availability.GetAvailableCount(key);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/可用量查询异常", e);
                return 0L;
            }
        }

        /// <summary>只读：def 级可取出键（只转发 Core 的唯一选择规则）。</summary>
        public static bool TryFindWithdrawableKey(ThingDef def, out SDS_StockKey key, out int available)
        {
            key = null;
            available = 0;
            if (def == null || !SDS_Rules.Available)
            {
                return false;
            }
            try
            {
                return SDS_Rules.TryFindWithdrawableKey(def, out key, out available);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/找可取出键异常", e);
                key = null;
                available = 0;
                return false;
            }
        }

        public static bool IsDisplayThing(Thing thing)
        {
            ISDS_DisplayQuery display = SDS_Host.Display;
            return display != null && thing != null && display.IsDisplay(thing);
        }

        public static bool TryGetDisplayKey(Thing thing, out SDS_StockKey key)
        {
            key = null;
            ISDS_DisplayQuery display = SDS_Host.Display;
            if (display == null || thing == null)
            {
                return false;
            }
            try
            {
                return display.TryGetDisplayKey(thing, out key) && key != null;
            }
            catch (Exception e)
            {
                key = null;
                SDS_Diag.ErrorOnce("公共API/展示键解析异常", e);
                return false;
            }
        }

        public static bool IsMirror(Thing thing)
        {
            ISDS_MirrorQuery mirrors = SDS_Host.MirrorQuery;
            return mirrors != null && thing != null && mirrors.IsMirror(thing);
        }

        /// <summary>只读：Pawn 取物点所在格（自身端口＝脚下；建筑端点＝交互格）。</summary>
        public static bool TryGetWithdrawCell(Pawn actor, out IntVec3 cell, out SDS_ApiResultCode code)
        {
            cell = IntVec3.Invalid;
            code = SDS_ApiResultCode.Ok;
            try
            {
                if (actor == null || !actor.Spawned || actor.Map == null || actor.Dead
                    || actor.Faction != Faction.OfPlayer)
                {
                    code = SDS_ApiResultCode.InvalidArgument;
                    return false;
                }
                if (!TrySelectWithdrawPoint(actor, out SDS_EndpointDecision decision, out code))
                {
                    return false;
                }
                if (!decision.port.AccessCell.IsValid)
                {
                    code = SDS_ApiResultCode.NoEndpoint;
                    return false;
                }
                cell = decision.port.AccessCell;
                return true;
            }
            catch (Exception e)
            {
                cell = IntVec3.Invalid;
                return Fail("取/取物格解析异常", e, out code);
            }
        }

        /// <summary>只读：该实物是否是本 Mod 的可用端点（统一端口句柄的唯一事实）。</summary>
        public static bool IsDepositEndpoint(Thing thing)
        {
            try
            {
                return TryGetOperationalPort(thing, out _);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/端点身份查询异常", e);
                return false;
            }
        }

        /// <summary>只读：本图超维空间是否可用（全局可用 + 本图暴露端点）。</summary>
        public static bool IsGridUsable(Map map)
        {
            try
            {
                ISDS_PortProvider ports = SDS_Host.Ports;
                return SDS_Rules.Available && map != null && ports != null && ports.IsLocalUsable(map);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/网格可用性查询异常", e);
                return false;
            }
        }

        /// <summary>def 级通道判定：只转发 Core 的唯一规则。</summary>
        public static bool RequiresPreservedStorage(ThingDef def)
        {
            return def != null && SDS_Rules.RequiresPreservedStorage(def);
        }

        // ---------- 第三方补料规划 ----------

        public static bool TryPlanBillSupplySlot(IngredientCount slot, Bill bill, RecipeDef recipe,
            List<ThingDef> allowedIngredients, ThingFilter corpseFilter, int need,
            Func<ThingDef, int> ownCount, Func<ThingDef, long> externalCount,
            Func<ThingDef, bool> extraAllows,
            Func<ThingDef, int> reserveProvider, HashSet<ThingDef> reserveApplied,
            Dictionary<ThingDef, int> pool,
            out ThingDef def, out int required, out int fromOwn, out int shortfall)
        {
            def = null;
            required = 0;
            fromOwn = 0;
            shortfall = 0;
            try
            {
                Thing giver = bill?.billStack?.billGiver as Thing;
                Func<ThingDef, bool> combined = candidate =>
                    (extraAllows == null || extraAllows(candidate))
                    && SDS_MedicalGate.Allows(giver, candidate);
                return SDS_Rules.TryPlanSlot(slot, bill, recipe, allowedIngredients, corpseFilter, need,
                    ownCount, externalCount, combined, reserveProvider, reserveApplied, pool,
                    out def, out required, out fromOwn, out shortfall);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/补料规划异常", e);
                return false;
            }
        }

        public static bool TryPlanAllowedDefSupply(
            List<ThingDef> allowedDefs, ThingFilter corpseFilter,
            Func<ThingDef, int> needForDef,
            Func<ThingDef, int> ownCount, Func<ThingDef, long> externalCount,
            Func<ThingDef, bool> extraAllows,
            Func<ThingDef, int> reserveProvider, HashSet<ThingDef> reserveApplied,
            Dictionary<ThingDef, int> pool,
            out ThingDef def, out int required, out int fromOwn, out int shortfall)
        {
            def = null;
            required = 0;
            fromOwn = 0;
            shortfall = 0;
            try
            {
                Func<ThingDef, bool> combined = candidate =>
                    (extraAllows == null || extraAllows(candidate))
                    && SDS_MedicalGate.Allows(null, candidate);
                return SDS_Rules.TryPlanAllowedDefSupply(allowedDefs, corpseFilter, needForDef,
                    ownCount, externalCount, combined, reserveProvider, reserveApplied, pool,
                    out def, out required, out fromOwn, out shortfall);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/def列表补料规划异常", e);
                return false;
            }
        }

        public static void NotifyBillSupplyDeposited(Dictionary<ThingDef, int> pool, ThingDef def, int amount)
        {
            try
            {
                SDS_Rules.NotifyDeposited(pool, def, amount);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/补料存入反馈异常", e);
            }
        }

        // ---------- 第三方自建装载清单注入（W50） ----------

        public static bool InjectTransportLoadCandidates(ThingWithComps host,
            List<TransferableOneWay> transferables)
        {
            ISDS_TransportLoadSink sink = SDS_Host.TransportLoad;
            if (sink == null || host == null || transferables == null)
            {
                return false;
            }
            try
            {
                return sink.InjectTransportLoadCandidates(host, transferables);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/装载清单注入异常", e);
                return false;
            }
        }

        // ---------- 回充 ----------

        /// <summary>保真回充：先回账本，失败按 Ground 退路交给宿主；cell 为失败落地首选格。</summary>
        public static bool TryReturnPreserved(Thing thing, Map map, IntVec3 cell)
        {
            if (thing == null || thing.Destroyed)
            {
                return false;
            }
            try
            {
                return SDS_Rules.ReturnThing(thing, SDS_StockChannel.Preserved, map, null,
                    SDS_ReturnFallback.Ground, cell);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/保真回充异常", e);
                return false;
            }
        }

        /// <summary>
        /// 虚拟回充：先回账本；landOnFailure=true 时失败按 Ground 退路交给宿主，
        /// false 时保持原状。cell 为失败落地首选格。
        /// </summary>
        public static bool TryReturnVirtual(Thing thing, Map map, IntVec3 cell,
            bool landOnFailure = true)
        {
            if (thing == null || thing.Destroyed)
            {
                return false;
            }
            try
            {
                return SDS_Rules.ReturnThing(thing, SDS_StockChannel.Virtual, map, null,
                    landOnFailure ? SDS_ReturnFallback.Ground : SDS_ReturnFallback.None, cell);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("公共API/虚拟回充异常", e);
                return false;
            }
        }

        // ---------- 内部：批量取出的单键 / 回滚 ----------

        private static bool TryWithdrawVirtualExact(SDS_StockKey key, int count, Pawn actor,
            out List<Thing> made)
        {
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery
                {
                    key = key,
                    quantity = count,
                    preserved = false
                },
                endpoint = null,
                mode = SDS_WithdrawMode.Container
            };
            if (!SDS_Rules.TryWithdraw(plan, actor, out made) || made == null
                || SumUnits(made) < count)
            {
                made = null;
                return false;
            }
            return true;
        }

        private static void ReturnVirtualList(List<Thing> things, Map map)
        {
            if (things == null)
            {
                return;
            }
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && !thing.Destroyed)
                {
                    SDS_Rules.ReturnThing(thing, SDS_StockChannel.Virtual, map, null,
                        SDS_ReturnFallback.Ground);
                }
            }
        }

        private static void ReturnPreservedList(List<Thing> things, Map map)
        {
            if (things == null)
            {
                return;
            }
            for (int i = 0; i < things.Count; i++)
            {
                TryReturnPreserved(things[i], map, IntVec3.Invalid);
            }
        }

        private static int SumUnits(List<Thing> things)
        {
            int total = 0;
            if (things == null)
            {
                return total;
            }
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && !thing.Destroyed)
                {
                    total += thing.stackCount;
                }
            }
            return total;
        }
    }
}
