using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// ★ 方案 A：保真普通物品的「消费桥」——保真腿（选件）。
    ///
    /// 背景：带键外状态的药品 / 食物 / 武器 / 衣物会进入保真通道（真身不 spawn），
    /// 而原版的 <c>HealthAIUtility.FindBestMedicine</c> / <c>FoodUtility.TryFindBestFoodSourceFor</c> /
    /// <c>JobGiver_*</c> 只枚举地图上的实物 → 会出现「库里有、原版看不见、只能手动输出」。
    ///
    /// 本文件只负责「从保真真身里选 1 件」；落点、解禁、腐烂冻结、超时回库与虚拟腿
    /// 都在共享层 <see cref="SDS_OfferBridge"/>（单一 offer 生命周期，禁止拆成两份）。
    ///
    /// 边界：
    ///  - 只覆盖**原版调用者**；第三方自带 JobGiver 可能绕开；
    ///  - 每次只提供 1 件，数量由原版 Job 自己决定；
    ///  - 与保真预留共用 Core 的精确保真取出，不新增第二套扣账。
    /// </summary>
    internal static class SDS_PreservedOffer
    {
        /// <summary>
        /// 从保真通道提供 1 件符合条件的实物。成功时 <paramref name="offered"/> 已 spawn 在访问点。
        /// 失败返回 false 且不改账本（可安全地交回原版逻辑）。
        /// </summary>
        public static bool TryOffer(Pawn pawn, Map map, Predicate<Thing> filter,
            Func<Thing, float> score, int lifetimeTicks, out Thing offered)
        {
            offered = null;
            if (pawn == null || map == null || filter == null || !pawn.Spawned)
            {
                return false;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || !SDS_Rules.Available || !SDS_Exposure.MapExposesGrid(map))
            {
                return false;
            }
            if (!SDS_OfferBridge.TryGetAccess(pawn, map, out SDS_AccessPoint access))
            {
                return false;
            }
            SDS_OfferBridge.TickIfDue();

            Thing chosen = null;
            float bestScore = float.MinValue;
            List<Thing> candidates = new List<Thing>();
            ledger.AppendPreservedThings(candidates);
            // 同一次提供内按 key 缓存可用量：多件真身同属一个品类时不重复查询。
            Dictionary<SDS_StockKey, long> availabilityByKey = new Dictionary<SDS_StockKey, long>();
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing thing = candidates[i];
                if (thing == null || thing.Destroyed || thing.stackCount <= 0 || !filter(thing))
                {
                    continue;
                }
                if (!SDS_Rules.TryGetStockKey(thing, out SDS_StockKey key)
                    || key == null || key.thingDef == null || ledger.IsForbidden(key))
                {
                    continue;
                }
                if (!availabilityByKey.TryGetValue(key, out long available))
                {
                    available = SDS_Availability.GetAvailableCount(key);
                    availabilityByKey[key] = available;
                }
                if (available <= 0L)
                {
                    continue;
                }
                // 预留是 key 级的：只要该品类还有任何预留，就无法保证"拿到的不是别人预定的那件"，
                // 因此整类跳过，宁可让原版这一轮继续失败，也不抢别人的料。
                if (ledger.GetReserved(key) > 0L)
                {
                    continue;
                }
                float value = score != null ? score(thing) : 0f;
                if (chosen == null || value > bestScore)
                {
                    chosen = thing;
                    bestScore = value;
                }
            }
            if (chosen == null)
            {
                return false;
            }
            if (SDS_OfferBridge.InCooldown(pawn, chosen.def))
            {
                return false;
            }

            // 精确取 1 件（走统一事务；失败不改账本）。
            List<ThingCount> plan = new List<ThingCount> { new ThingCount(chosen, 1) };
            if (!SDS_Rules.TryWithdrawPreservedExact(plan, pawn, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                return false;
            }
            if (SDS_OfferBridge.TrackPlaced(pawn, map, made[0], access, lifetimeTicks, true, out offered))
            {
                return true;
            }
            // 已并入地图上的同类堆（实物已销毁）时数量已守恒，无需回库。
            if (!made[0].Destroyed)
            {
                SDS_OfferBridge.RollbackOrWarn(made[0], true, map, pawn, IntVec3.Invalid,
                    SDS_ReturnFallback.Ground, "保真桥/落点失败（实物已落地，未回库）");
            }
            return false;
        }
    }

    /// <summary>
    /// 保真药品桥：原版找不到药时，从保真通道提供 1 件并返回给它。
    /// 只处理「地图上确实没有可用药」的情形；原版已找到时完全不干预。
    /// </summary>
    [HarmonyPatch(typeof(HealthAIUtility), nameof(HealthAIUtility.FindBestMedicine))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PreservedMedicineOffer
    {
        public static void Postfix(Pawn healer, Pawn patient, bool onlyUseInventory, ref Thing __result)
        {
            try
            {
                if (__result != null || onlyUseInventory || healer == null || patient == null
                    || !healer.Spawned || healer.Map == null || healer.Map != patient.MapHeld
                    || healer.Faction != Faction.OfPlayer)
                {
                    return;
                }
                if (patient.playerSettings != null && (int)patient.playerSettings.medCare <= 1)
                {
                    return;
                }
                if (Medicine.GetMedicineCountToFullyHeal(patient) <= 0)
                {
                    return;
                }
                if (!SDS_PreservedOffer.TryOffer(healer, healer.Map,
                        t => t.def != null && t.def.IsMedicine
                            && SDS_MedicalGate.Allows(patient, t.def),
                        t => t.GetStatValue(StatDefOf.MedicalPotency),
                        6000, out Thing offered))
                {
                    return;
                }
                __result = offered;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真桥/药品提供失败（已忽略，原版按无药处理）", e);
            }
        }
    }

    /// <summary>
    /// 保真食物桥：原版找不到食物时，从保真通道提供 1 件并写回 out 参数。
    /// 只覆盖原版 <c>TryFindBestFoodSourceFor</c> 的调用者；营养 / 饮食禁忌由 <c>WillEat</c> 判定。
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.TryFindBestFoodSourceFor))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PreservedFoodOffer
    {
        public static void Postfix(Pawn getter, Pawn eater, bool desperate, bool allowVenerated,
            ref bool __result, ref Thing foodSource, ref ThingDef foodDef)
        {
            try
            {
                if (__result || getter == null || eater == null || !getter.Spawned || getter.Map == null
                    || eater.Map != getter.Map || eater.needs == null || eater.needs.food == null
                    || eater.Dead)
                {
                    return;
                }
                if (getter.Faction != Faction.OfPlayer && !getter.IsColonist)
                {
                    return;
                }
                if (!SDS_PreservedOffer.TryOffer(getter, getter.Map,
                        t => t.def != null && t.def.IsNutritionGivingIngestible && !t.def.IsCorpse
                            && eater.WillEat(t, getter, careIfNotAcceptableForTitle: true,
                                allowVenerated: allowVenerated),
                        t => t.GetStatValue(StatDefOf.Nutrition),
                        2500, out Thing offered))
                {
                    return;
                }
                foodSource = offered;
                foodDef = FoodUtility.GetFinalIngestibleDef(offered);
                __result = true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真桥/食物提供失败（已忽略，原版按无食物处理）", e);
            }
        }
    }

    /// <summary>
    /// 保真武器桥：原版「机会拾取武器」找不到武器时，提供 1 件并直接返回 Equip Job。
    /// 先试保真真身，再回退虚拟通道（旧档虚拟条目 / Core+DLC 物品）；只在「当前没有武器」时补位。
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_PickUpOpportunisticWeapon), "TryGiveJob")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PreservedWeaponOffer
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (__result != null || pawn == null || !pawn.Spawned || pawn.Map == null
                    || pawn.Faction != Faction.OfPlayer || pawn.Downed
                    || pawn.equipment == null || pawn.equipment.Primary != null
                    || pawn.WorkTagIsDisabled(WorkTags.Violent)
                    || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                    || pawn.GetRegion() == null)
                {
                    return;
                }
                if (!SDS_PreservedOffer.TryOffer(pawn, pawn.Map,
                        t => t.def != null && t.def.HasAssignableCompFrom(typeof(CompEquippable))
                            && EquipmentUtility.CanEquip(t, pawn)
                            && (!t.def.IsRangedWeapon || !pawn.WorkTagIsDisabled(WorkTags.Shooting)),
                        t => t.MarketValue,
                        6000, out Thing offered)
                    && !SDS_OfferBridge.TryOfferVirtual(pawn, pawn.Map,
                        def => def.HasAssignableCompFrom(typeof(CompEquippable))
                            && (!def.IsRangedWeapon || !pawn.WorkTagIsDisabled(WorkTags.Shooting)),
                        def => def.GetStatValueAbstract(StatDefOf.MarketValue),
                        6000, out offered))
                {
                    return;
                }
                Job job = JobMaker.MakeJob(JobDefOf.Equip, offered);
                job.expiryInterval = 60;
                __result = job;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真桥/武器提供失败（已忽略，原版按无武器处理）", e);
            }
        }
    }

    /// <summary>
    /// 保真衣物桥：原版「优化服装」扫描不到衣物时，提供 1 件并把优化计时清零，
    /// 让原版下一次 think 用**它自己的**评分 / 政策 / 冲突规则决定要不要穿。
    /// 这里不复制服装评分逻辑，只负责"让真身出现在可见范围里"。
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_OptimizeApparel), "TryGiveJob")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PreservedApparelOffer
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (__result != null || pawn == null || !pawn.Spawned || pawn.Map == null
                    || pawn.Faction != Faction.OfPlayer || pawn.apparel == null || pawn.outfits == null
                    || pawn.Downed || pawn.IsQuestLodger()
                    || (pawn.IsMutant && pawn.mutant.Def.disableApparel))
                {
                    return;
                }
                // 原版还在优化冷却中：说明它这次根本没扫描，不能提供（否则会每 tick 刷物品）。
                if (Find.TickManager.TicksGame < pawn.mindState.nextApparelOptimizeTick)
                {
                    return;
                }
                ApparelPolicy policy = pawn.outfits.CurrentApparelPolicy;
                if (policy == null || policy.filter == null)
                {
                    return;
                }
                if (!SDS_PreservedOffer.TryOffer(pawn, pawn.Map,
                        t => t is Apparel apparel && apparel.def != null
                            && policy.filter.Allows(apparel)
                            && ApparelUtility.HasPartsToWear(pawn, apparel.def)
                            && (apparel.def.apparel.gender == Gender.None
                                || apparel.def.apparel.gender == pawn.gender)
                            && apparel.def.apparel.developmentalStageFilter.Has(pawn.DevelopmentalStage)
                            && !pawn.apparel.WouldReplaceLockedApparel(apparel)
                            && !CompBiocodable.IsBiocoded(apparel),
                        null,
                        6000, out _))
                {
                    return;
                }
                // 让原版下一次 think 立即重新评分（它会把 offer 当成"在库里的衣物"）。
                pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真桥/衣物提供失败（已忽略，原版按无衣物处理）", e);
            }
        }
    }

    /// <summary>
    /// 让「本子系统提供的待消费实物」对原版表现为「在存储中」。
    /// 只影响 <c>StoreUtility.IsInAnyStorage</c>；用于让 <c>JobGiver_OptimizeApparel</c>
    /// 的 <c>apparel2.IsInAnyStorage()</c> 条件通过。实物确实就在访问点上，随时可取。
    /// </summary>
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsInAnyStorage))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PreservedOfferInStorage
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            try
            {
                if (!__result && SDS_OfferBridge.IsOffer(t))
                {
                    __result = true;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真供给存储判定失败（已忽略，原版行为不受影响）", e);
            }
        }
    }
}
