using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    // ════════════════════════════════════════════════════════════════════════════════════════
    // 保真原料派活（尸体 / 缩装家具 / 基因载体）——通道分部 · 保真侧
    //
    // 保真品类没有物理镜像（真身只能存在于超维空间里），所以原版
    // `WorkGiver_DoBill.TryFindBestIngredientsHelper` 在地图上永远搜不到它们 —— 屠宰尸体这类
    // 清单会被判成"缺料"。
    //
    // 现在的最小接管（两处，且都只在原版失败时生效）：
    //  1) `TryFindBestBillIngredients` 返回 false 时，用**原版自己的配料算法**
    //     （`TryFindBestBillIngredientsInSet`）在保真通道的真身里挑一份完整配料，填进 chosen；
    //  2) `TryStartNewDoBillJob` 造出 DoBill Job 后，若队列里全是保真真身，
    //     把 Job 换成"先去端点把真身取到手上"的取货 Job。
    // 取货 Job 完成后把**已经拿在手上**的真身交给原版 DoBill：
    // 原版自己带着它走到工作台、放到台边、扣料开工 —— 与地图上真有一具尸体时逐字节一致。
    // 虚拟通道的镜像配料兜底与共享反射探针在 Jobs/SDS_VirtualBillFallback.cs（唯一实现）；
    // 通道分部约束：Preserve/ 与 Mirrors/ 互不引用（CheckAssemblyBoundaries.ps1 §10）。
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 账单 / 固定配料的保真通道兜底："挑选 → 取到手上 / 交回原版"，真身 + 专用取货 Job。
    /// 原版配料算法反射探针与 RootCell 复用 SDS_VirtualBillFallback（共享层唯一实现）。
    /// </summary>
    internal static class SDS_PreservedBillDispatch
    {
        /// <summary>
        /// 原版说"找不到原料"之后调用：在保真通道里找一份**完整**配料。
        /// 成功则写好 chosen（原版随后会用同一份 chosen 造 DoBill Job）。
        /// 只在"小人确实能走到一个端口、且这份料能整叠拿在手上"时才接管。
        /// </summary>
        public static bool TrySupplyPreservedIngredients(Bill bill, Pawn pawn, Thing billGiver,
            List<ThingCount> chosen, List<IngredientCount> missingIngredients)
        {
            if (bill == null || pawn == null || billGiver == null || chosen == null
                || bill.recipe == null || bill.recipe.ingredients == null
                || bill.recipe.ingredients.Count == 0)
            {
                return false;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || !SDS_Rules.Available)
            {
                return false;
            }
            // 必须有一个走得到的端口（自身端口优先）：没有端口就不是"能取到的库存"。
            if (!SDS_PreservedUsage.HasReachableEndpoint(pawn))
            {
                return false;
            }
            if (!SDS_VirtualBillFallback.TryGetFinder())
            {
                return false;
            }

            List<Thing> candidates = new List<Thing>();
            ledger.AppendPreservedThings(candidates);
            // 同一次扫描内按 key 缓存可用量：同一品类可能对应多件真身，避免重复查询。
            Dictionary<SDS_StockKey, long> availabilityByKey = new Dictionary<SDS_StockKey, long>();
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                Thing thing = candidates[i];
                if (thing == null || thing.Destroyed || !bill.IsFixedOrAllowedIngredient(thing)
                    || !SDS_MedicalGate.Allows(billGiver, thing.def))
                {
                    candidates.RemoveAt(i);
                    continue;
                }
                // 已被别的 Job 整组预留完的真身不参与本次选料，避免两个清单抢同一批；
                // 被玩家禁用的品类同样不参与（禁用 = 原版 Forbidden，不参与生产）。
                if (!SDS_Rules.TryGetStockKey(thing, out SDS_StockKey preservedKey)
                    || preservedKey == null || ledger.IsForbidden(preservedKey))
                {
                    candidates.RemoveAt(i);
                    continue;
                }
                if (!availabilityByKey.TryGetValue(preservedKey, out long available))
                {
                    available = SDS_Availability.GetAvailableCount(preservedKey);
                    availabilityByKey[preservedKey] = available;
                }
                if (available <= 0L)
                {
                    candidates.RemoveAt(i);
                }
            }
            if (candidates.Count == 0)
            {
                return false;
            }

            chosen.Clear();
            // S17：失败时不得改写真实 missing 列表——原版要靠它给出 NoMedicineMatchingCategory。
            List<IngredientCount> tempMissing = missingIngredients != null
                ? new List<IngredientCount>()
                : null;
            bool found;
            try
            {
                found = (bool)SDS_VirtualBillFallback.findInSet.Invoke(null,
                    new object[]
                    {
                        candidates, bill, chosen, SDS_VirtualBillFallback.RootCell(billGiver, pawn),
                        false, tempMissing
                    });
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真原料挑选调用原版算法失败（已忽略，原版行为不受影响）", e);
                chosen.Clear();
                return false;
            }
            if (!found || chosen.Count == 0)
            {
                chosen.Clear();
                return false;
            }
            // 1-A：整份计划都要拿得动。
            //  - 单件：维持原行为（不额外限重，原版手上本来就能抱一件较重的实体）；
            //  - 多件：小人会"手上 + 背包"一起带，总重量必须以原版携带能力为上限，
            //    否则会出现"背包无限大"的观感（超重计划交回原版，不做超能力搬运）。
            float totalMass = 0f;
            for (int i = 0; i < chosen.Count; i++)
            {
                Thing thing = chosen[i].Thing;
                int count = chosen[i].Count;
                if (thing == null || thing.Destroyed || count <= 0)
                {
                    chosen.Clear();
                    return false;
                }
                totalMass += (float)count * thing.GetStatValue(StatDefOf.Mass);
            }
            if (chosen.Count > 1)
            {
                float capacity = pawn.GetStatValue(StatDefOf.CarryingCapacity);
                if (totalMass > capacity + 0.01f)
                {
                    chosen.Clear();
                    return false;
                }
            }
            // S17：只有真正接管成功才清空原版缺料列表；任何失败路径都保持原样。
            missingIngredients?.Clear();
            return true;
        }

        /// <summary>
        /// W51：多槽配方的保真缺口唯一物化腿。
        /// 原版搜索失败后，按 `missingIngredients` 点名的槽位把保真真身取到地图的
        /// 「工作台交互格」（= 原版 IngredientPlaceCellsInOrder[0]），随后由原版重新搜料 →
        /// 原版搬运 → 原版 DepositHauledThingInContainer 入容器。只对 Building 类工作台生效；
        /// 不写容器、不派活、不另算数量口径（件数直接问原版 `CountRequiredOfFor`）。
        /// 返回 true 表示本批至少落地一件，调用方应立刻重跑原版搜索。
        /// </summary>
        // RULE:W51
        public static bool TryMaterializePreservedGaps(Bill bill, Pawn pawn, Thing billGiver,
            List<IngredientCount> missingIngredients, out List<Thing> placed)
        {
            placed = null;
            if (bill == null || pawn == null || !(billGiver is Building) || missingIngredients == null
                || missingIngredients.Count == 0 || bill.recipe == null || bill.recipe.ingredients == null)
            {
                return false;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || !SDS_Rules.Available)
            {
                return false;
            }
            Map map = billGiver.MapHeld ?? pawn.Map;
            if (map == null || !SDS_PreservedUsage.HasReachableEndpoint(pawn))
            {
                return false;
            }

            // 候选过滤与 TrySupplyPreservedIngredients 完全同源（唯一口径）。
            List<Thing> candidates = new List<Thing>();
            ledger.AppendPreservedThings(candidates);
            Dictionary<SDS_StockKey, long> availabilityByKey = new Dictionary<SDS_StockKey, long>();
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                Thing thing = candidates[i];
                if (thing == null || thing.Destroyed || !bill.IsFixedOrAllowedIngredient(thing)
                    || !SDS_MedicalGate.Allows(billGiver, thing.def))
                {
                    candidates.RemoveAt(i);
                    continue;
                }
                if (!SDS_Rules.TryGetStockKey(thing, out SDS_StockKey key) || key == null
                    || ledger.IsForbidden(key))
                {
                    candidates.RemoveAt(i);
                    continue;
                }
                if (!availabilityByKey.TryGetValue(key, out long available))
                {
                    available = SDS_Availability.GetAvailableCount(key);
                    availabilityByKey[key] = available;
                }
                if (available <= 0L)
                {
                    candidates.RemoveAt(i);
                }
            }
            if (candidates.Count == 0)
            {
                return false;
            }

            // 逐缺料槽挑一件真身；件数只问原版 CountRequiredOfFor（不复制数量规则）。
            List<ThingCount> plan = new List<ThingCount>();
            HashSet<Thing> used = new HashSet<Thing>();
            for (int s = 0; s < missingIngredients.Count; s++)
            {
                IngredientCount slot = missingIngredients[s];
                if (slot == null || slot.filter == null)
                {
                    continue;
                }
                for (int i = 0; i < candidates.Count; i++)
                {
                    Thing thing = candidates[i];
                    if (thing == null || thing.Destroyed || used.Contains(thing)
                        || !slot.filter.Allows(thing))
                    {
                        continue;
                    }
                    int need = Math.Max(1, slot.CountRequiredOfFor(thing.def, bill.recipe, bill));
                    need = Math.Min(need, thing.stackCount);
                    if (need <= 0)
                    {
                        continue;
                    }
                    plan.Add(new ThingCount(thing, need));
                    used.Add(thing);
                    break;
                }
            }
            if (plan.Count == 0)
            {
                return false;
            }

            // 先整批摘出，再逐个落地；任一件落不下就把本批已落下的也收回账本（数量守恒）。
            if (!SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                return false;
            }
            IntVec3 cell = SDS_VirtualBillFallback.RootCell(billGiver, pawn);
            List<Thing> placedThings = new List<Thing>();
            bool allPlaced = true;
            for (int i = 0; i < made.Count; i++)
            {
                Thing thing = made[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                if (SDS_PreservedUsage.TryPlaceAtCell(thing, map, cell, out Thing dropped,
                        markProduced: false)
                    && dropped != null && !dropped.Destroyed)
                {
                    if (ReferenceEquals(dropped, thing))
                    {
                        placedThings.Add(thing);
                    }
                    continue;
                }
                allPlaced = false;
            }
            if (!allPlaced)
            {
                RollbackMaterializedGaps(placedThings, map);
                return false;
            }
            placed = placedThings;
            return true;
        }

        /// <summary>W51：重跑原版搜索仍失败时，把本批可回滚的物化真身收回账本（数量守恒）。</summary>
        public static void RollbackMaterializedGaps(List<Thing> placedThings, Map map)
        {
            if (placedThings == null || placedThings.Count == 0 || map == null)
            {
                return;
            }
            for (int i = 0; i < placedThings.Count; i++)
            {
                Thing thing = placedThings[i];
                if (thing != null && !thing.Destroyed)
                {
                    SDS_Rules.ReturnThing(thing, SDS_StockChannel.Preserved, map, null,
                        SDS_ReturnFallback.None);
                }
            }
        }

        /// <summary>
        /// 原版 DoBill Job 已造好、且队列里全是保真真身时，换成"先取到手上"的 Job。
        /// 返回 true 表示已处理（result 可能是 null：此刻没有可用端口）。
        /// </summary>
        public static bool TryWrapDoBillJob(Pawn pawn, Bill bill, IBillGiver giver, ref Job result)
        {
            if (result == null || result.def != JobDefOf.DoBill || result.targetQueueB == null
                || result.targetQueueB.Count == 0 || pawn == null || bill == null
                || !(giver is Thing giverThing))
            {
                return false;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null)
            {
                return false;
            }
            for (int i = 0; i < result.targetQueueB.Count; i++)
            {
                Thing thing = result.targetQueueB[i].Thing;
                // 只要有一件不是保真真身，就不是本补丁的 Job（普通原料继续走镜像链路）。
                if (thing == null || thing.Spawned || !ledger.IsPreserved(thing))
                {
                    return false;
                }
            }
            JobDef jobDef = SDS_JobDefOf.WithdrawPreservedForBill;
            if (jobDef == null)
            {
                return false;
            }
            if (!SDS_AccessPointResolver.TryGetNearestReachable(pawn, out SDS_AccessPoint point)
                || point == null || point.thing == null)
            {
                // 端口在挑选之后失效了：不要留下一个指向未 spawn 真身的坏 Job。
                result = null;
                return true;
            }
            Job job = JobMaker.MakeJob(jobDef, point.thing, giverThing);
            job.bill = bill;
            job.count = 1;
            job.haulMode = HaulMode.ToCellNonStorage;
            job.targetQueueB = new List<LocalTargetInfo>(result.targetQueueB);
            job.countQueue = result.countQueue != null
                ? new List<int>(result.countQueue)
                : new List<int>();
            job.workGiverDef = result.workGiverDef;
            result = job;
            return true;
        }
    }

    /// <summary>
    /// 原版找原料失败后的**唯一**补漏点：先试虚拟通道镜像，再试保真通道；
    /// 多槽配方（W51）最后把保真缺口物化到工作台交互格，并在同一 tick 重跑原版搜索。
    /// 原版找到了原料（__result == true）时本补丁一个字都不改。
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PreservedBillIngredients
    {
        /// <summary>W51 重跑守卫：物化后反射调用本方法时，内层只让原版跑，绝不再进本补丁。</summary>
        private static bool rerunning;

        private static MethodInfo findBillIngredients;

        private static bool TryGetBillFinder()
        {
            if (findBillIngredients != null)
            {
                return true;
            }
            findBillIngredients = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients",
                new[]
                {
                    typeof(Bill), typeof(Pawn), typeof(Thing), typeof(List<ThingCount>),
                    typeof(List<IngredientCount>)
                });
            return findBillIngredients != null;
        }

        public static void Postfix(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen,
            List<IngredientCount> missingIngredients, ref bool __result)
        {
            if (rerunning)
            {
                return;
            }
            // 整个 Postfix 执行期间置位：W51 物化后反射重跑原版时，内层只让原版跑。
            rerunning = true;
            try
            {
                if (__result)
                {
                    return;
                }
                // 虚拟通道优先：镜像代表可重建品类，直接用原版算法从镜像里挑
                // （虚拟腿唯一实现在 Jobs/SDS_VirtualBillFallback）。
                if (SDS_VirtualBillFallback.TrySupplyVirtualIngredients(bill, pawn, billGiver,
                    chosen, missingIngredients))
                {
                    __result = true;
                    return;
                }
                // 虚拟通道找不到时，再看保真通道。
                if (SDS_PreservedBillDispatch.TrySupplyPreservedIngredients(bill, pawn, billGiver,
                    chosen, missingIngredients))
                {
                    __result = true;
                    return;
                }
                // W51：多槽配方（复活机械体 = 1 尸体 + N 钢）单通道候选补不齐所有槽。
                // 把保真缺口物化到工作台交互格，再在同一 tick 重跑原版搜索：
                // 原版随后自己选中 / 搬运 / 入容器，不触发失败锁窗。
                if (TryGetBillFinder()
                    && SDS_PreservedBillDispatch.TryMaterializePreservedGaps(bill, pawn, billGiver,
                        missingIngredients, out List<Thing> placedGaps))
                {
                    __result = (bool)findBillIngredients.Invoke(null,
                        new object[] { bill, pawn, billGiver, chosen, missingIngredients });
                    if (!__result)
                    {
                        // 物化后原版仍判缺料（极端过滤器 / 可达组合）：把本批真身收回账本。
                        SDS_PreservedBillDispatch.RollbackMaterializedGaps(placedGaps,
                            billGiver != null ? billGiver.MapHeld : pawn.Map);
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真原料补漏失败（已忽略，原版行为不受影响）", e);
            }
            finally
            {
                rerunning = false;
            }
        }
    }

    /// <summary>
    /// S17：医疗账单（手术 / 安装 / 麻醉等）的药品候选统一在此关口校验医药等级。
    ///
    /// 原版 `TryFindBestIngredientsHelper` 只有「区域分支」排除了 Pawn 账单的药品，
    /// 而 `IHaulSource` 递归分支只走 `IsUsableIngredient`、**没有医药等级判据**；
    /// 本 Mod 的金库是 `IHaulSource` 且通过子 holder 暴露镜像投影，恰好会走进这条分支。
    /// 这里复用 `SDS_MedicalGate.Allows`（与 `AddEveryMedicineToRelevantThings`
    /// 同一判据），覆盖全部来源；非 `Bill_Medical` / 非 Pawn 患者一律放行（原版行为不变）。
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), "IsUsableIngredient")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MedicalIngredientGate
    {
        public static bool Prefix(Thing t, Bill bill, ref bool __result)
        {
            try
            {
                if (t == null || t.def == null || !t.def.IsMedicine
                    || !(bill is Bill_Medical medical)
                    || medical.billStack == null
                    || !(medical.billStack.billGiver is Pawn patient))
                {
                    return true;
                }
                if (!SDS_MedicalGate.Allows(patient, t.def))
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("医疗账单选料（S17）判定失败（已忽略，按原版继续）", e);
            }
            return true;
        }
    }

    /// <summary>
    /// 把"队列里全是保真真身"的原版 DoBill Job 换成"先去端点取到手上"的 Job。
    /// 普通原料（有镜像 / 地图实物）的 DoBill 完全不受影响。
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.TryStartNewDoBillJob))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PreservedBillJob
    {
        public static void Postfix(Pawn pawn, Bill bill, IBillGiver giver, ref Job __result)
        {
            try
            {
                SDS_PreservedBillDispatch.TryWrapDoBillJob(pawn, bill, giver, ref __result);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真原料派活改写失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>
    /// 保真原料取货 Job：预留保真库存 → 走到端口 → 把真身取到**手上** → 交回原版 DoBill。
    /// 全程不在端点与工作台之间落地捡取；原版随后按正常流程把料带到工作台旁并开工。
    /// </summary>
    public class JobDriver_SDS_WithdrawPreservedForBill : JobDriver
    {
        private const TargetIndex WithdrawPointInd = TargetIndex.A;
        private const TargetIndex GiverInd = TargetIndex.B;

        /// <summary>
        /// 失败退避：同一个清单在取料 Job 失败后短时间内不要再派同一个 Job。
        /// 不设它的话，WorkGiver_DoBill 会在同一 tick 反复 "失败 → EndJob → 重新派活"，
        /// 触发原版 "started 10 jobs in one tick" 错误恢复。
        /// </summary>
        private const int FailureBackoffTicks = 60;

        private void BackoffCurrentBill()
        {
            if (job != null && job.bill != null)
            {
                job.bill.nextTickToSearchForIngredients = GenTicks.TicksGame + FailureBackoffTicks;
            }
        }

        private bool IsSelfWithdrawPoint()
        {
            Thing point = job != null ? job.GetTarget(WithdrawPointInd).Thing : null;
            return point != null && ReferenceEquals(point, pawn);
        }

        /// <summary>把 Job 队列还原成"真身 + 需要件数"的计划。</summary>
        private List<ThingCount> CurrentPlan()
        {
            List<ThingCount> plan = new List<ThingCount>();
            if (job == null || job.targetQueueB == null)
            {
                return plan;
            }
            for (int i = 0; i < job.targetQueueB.Count; i++)
            {
                Thing thing = job.targetQueueB[i].Thing;
                int count = job.countQueue != null && i < job.countQueue.Count ? job.countQueue[i] : 1;
                if (thing != null)
                {
                    plan.Add(new ThingCount(thing, Math.Max(1, count)));
                }
            }
            return plan;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 与原版换活一致：手上若还拿着别的东西，先放下。
            if (pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null)
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
            }
            Thing point = job.targetA.Thing;
            if (!IsSelfWithdrawPoint() && !(point is Pawn))
            {
                if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
                {
                    SDS_Diag.Fail("取货/清单取物点不可预留", SDS_Diag.Ctx(pawn.Map, pawn, point, job));
                    BackoffCurrentBill();
                    return false;
                }
            }
            // ★ 与原版 JobDriver_DoBill 同一口径：占住工作台本体 + 交互格。
            //   如果不占，WorkGiver_DoBill 下一 tick 仍看到这台工作台"未预留"，会重复派同一个取料 Job；
            //   第二个 Job 在首个 toil 预留保真库存时失败 → started 10 jobs in one tick 死循环。
            Thing giver = job.targetB.Thing;
            if (giver == null || giver.Destroyed || !giver.Spawned || giver.Map != pawn.Map)
            {
                SDS_Diag.Fail("取货/清单工作台失效", SDS_Diag.Ctx(pawn.Map, pawn, giver, job));
                BackoffCurrentBill();
                return false;
            }
            if (!pawn.Reserve(giver, job, 1, -1, null, errorOnFailed))
            {
                SDS_Diag.Fail("取货/清单工作台不可预留", SDS_Diag.Ctx(pawn.Map, pawn, giver, job));
                BackoffCurrentBill();
                return false;
            }
            if (giver is Building building && building.def != null && building.def.hasInteractionCell
                && !pawn.ReserveSittableOrSpot(building.InteractionCell, job, errorOnFailed))
            {
                SDS_Diag.Fail("取货/清单交互格不可预留", SDS_Diag.Ctx(pawn.Map, pawn, giver, job,
                    "cell=" + building.InteractionCell));
                BackoffCurrentBill();
                return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(delegate
            {
                SDS_Rules.ReleaseJobReservation(job.loadID);
            });
            this.FailOnIncapable(PawnCapacityDefOf.Manipulation);
            if (!IsSelfWithdrawPoint())
            {
                if (job.targetA.Thing is Pawn)
                {
                    this.FailOn(delegate
                    {
                        Thing point = job.GetTarget(WithdrawPointInd).Thing;
                        return point == null || point.Destroyed || !point.Spawned;
                    });
                }
                else
                {
                    this.FailOnDespawnedNullOrForbidden(WithdrawPointInd);
                }
            }

            // 1. 预留保真库存（按真身归属组），防止两个小人拿同一批。
            yield return Toils_General.Do(delegate
            {
                List<ThingCount> plan = CurrentPlan();
                if (plan.Count == 0 || !SDS_Rules.TryReservePreservedForJob(job.loadID, plan, pawn))
                {
                    SDS_Diag.Fail("取货/清单保真预留失败", SDS_Diag.Ctx(pawn.Map, pawn, null, job,
                        "plan=" + plan.Count));
                    BackoffCurrentBill();
                    EndJobWith(JobCondition.Incompletable);
                }
            });

            // 2. 走位（自身端口无需寻路）。
            if (!IsSelfWithdrawPoint())
            {
                Thing point = job.targetA.Thing;
                PathEndMode mode = PathEndMode.Touch;
                if (!(point is Pawn)
                    && SDS_EndpointHandle.TryFromThing(point, out SDS_EndpointHandle handle))
                {
                    mode = handle.AccessMode;
                }
                yield return Toils_Goto.GotoThing(WithdrawPointInd, mode);
            }

            // 3. 取到手上 → 交回原版 DoBill。
            Toil materialize = ToilMaker.MakeToil("SDS_WithdrawPreservedForBill");
            materialize.initAction = MaterializeAndHandoff;
            materialize.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return materialize;
        }

        private void MaterializeAndHandoff()
        {
            List<ThingCount> plan = CurrentPlan();
            if (plan.Count == 0)
            {
                SDS_Diag.Fail("取货/清单保真取料失败", SDS_Diag.Ctx(pawn.Map, pawn, null, job,
                    "plan=0 reservation=" + job.loadID));
                BackoffCurrentBill();
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            // Core 公开门面没有「接管 Job 预留」的精确保真入口：先释放本 Job 的预留，
            // 再按精确计划取（计划本身钉死了具体真身，同 tick 内不会跨 Job 抢料）。
            SDS_Rules.ReleaseJobReservation(job.loadID);
            if (!SDS_Rules.TryWithdrawPreservedExact(plan, pawn, out List<Thing> real)
                || real == null || real.Count == 0)
            {
                SDS_Diag.Fail("取货/清单保真取料失败", SDS_Diag.Ctx(pawn.Map, pawn, null, job,
                    "plan=" + plan.Count + " reservation=" + job.loadID));
                BackoffCurrentBill();
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            for (int i = 0; i < real.Count; i++)
            {
                if (real[i] != null && !real[i].Destroyed)
                {
                    SDS_Rules.MarkProduced(real[i]);
                }
            }

            // 1-A：第一件尽量进手（"从端口取到手上"）；其余进背包。
            // 原版取料腿本身支持 `canTakeFromInventory`，会一趟一趟地：
            // 取一件 → 送到台边 → 再取下一件，直到队列清空。
            Thing carried = null;
            List<Thing> queued = new List<Thing>();
            for (int i = 0; i < real.Count; i++)
            {
                Thing thing = real[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                if (carried == null && pawn.carryTracker != null && pawn.carryTracker.TryStartCarry(thing))
                {
                    carried = pawn.carryTracker.CarriedThing;
                    if (carried != null)
                    {
                        queued.Add(carried);
                        continue;
                    }
                }
                // canMergeWithExistingStacks: false —— 保真真身保持独立实例，回滚时可逐件取回。
                if (pawn.inventory != null && pawn.inventory.innerContainer != null
                    && pawn.inventory.innerContainer.TryAdd(thing, false))
                {
                    queued.Add(thing);
                    continue;
                }
                // 手上和背包都放不下：整单回滚，绝不吞物品。
                ReturnAllToGrid(carried, queued);
                BackoffCurrentBill();
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            Thing giver = job.GetTarget(GiverInd).Thing;
            if (queued.Count == 0 || giver == null || giver.Destroyed || !giver.Spawned)
            {
                ReturnAllToGrid(carried, queued);
                BackoffCurrentBill();
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            // 成功交接：清掉失败退避，让原版 DoBill 立即接手。
            if (job.bill != null)
            {
                job.bill.nextTickToSearchForIngredients = 0;
            }

            // 交回原版：队列 = 手上 + 背包里的全部真身（手上那件排第一）。
            // 原版 JobDriver_DoBill 会把它当成"已经拿到的原料"继续：
            // 手上那件直接走到台边；背包里的下一件再被取到手上，继续下一趟。
            Job billJob = JobMaker.MakeJob(JobDefOf.DoBill, giver);
            billJob.targetQueueB = new List<LocalTargetInfo>();
            billJob.countQueue = new List<int>();
            if (carried != null)
            {
                billJob.targetQueueB.Add(carried);
                billJob.countQueue.Add(carried.stackCount);
            }
            for (int i = 0; i < queued.Count; i++)
            {
                Thing thing = queued[i];
                if (thing == null || thing.Destroyed || ReferenceEquals(thing, carried))
                {
                    continue;
                }
                billJob.targetQueueB.Add(thing);
                billJob.countQueue.Add(thing.stackCount);
            }
            billJob.haulMode = HaulMode.ToCellNonStorage;
            billJob.bill = job.bill;
            billJob.playerForced = job.playerForced;
            billJob.workGiverDef = job.workGiverDef;
            pawn.jobs.StartJob(billJob, JobCondition.Succeeded, null, false, true, null, null,
                false, false, true, false, true, true);
        }

        /// <summary>
        /// 交接失败时把手上 + 背包里的真身全部放回账本（回充不了才落地），绝不吞物品。
        /// </summary>
        private void ReturnAllToGrid(Thing carried, List<Thing> queued)
        {
            if (pawn == null)
            {
                return;
            }
            if (carried != null && !carried.Destroyed && pawn.carryTracker != null
                && ReferenceEquals(pawn.carryTracker.CarriedThing, carried))
            {
                if (pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing dropped)
                    && dropped != null)
                {
                    SDS_Rules.ReturnThing(dropped, SDS_StockChannel.Preserved, pawn.Map, pawn,
                        SDS_ReturnFallback.None);
                }
            }
            if (queued == null || pawn.inventory == null || pawn.inventory.innerContainer == null)
            {
                return;
            }
            for (int i = 0; i < queued.Count; i++)
            {
                Thing thing = queued[i];
                if (thing == null || thing.Destroyed || ReferenceEquals(thing, carried))
                {
                    continue;
                }
                if (!pawn.inventory.innerContainer.Contains(thing))
                {
                    continue;
                }
                pawn.inventory.innerContainer.Remove(thing);
                SDS_Rules.ReturnThing(thing, SDS_StockChannel.Preserved, pawn.Map, pawn,
                    SDS_ReturnFallback.Ground);
            }
        }
    }
}
