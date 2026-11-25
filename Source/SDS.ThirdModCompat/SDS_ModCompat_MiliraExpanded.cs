using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 米莉拉扩展-未来科技Milira Expanded - Future Technology 适配器：
    /// 只做 SDS → 米莉拉的「后备供料」：
    ///  - 米莉拉折叠空间网络自己的库存优先；
    ///  - 自动生产设备（CompVirtualProductionDevice）在每 30 tick 的 tick 前，
    ///    按链接工作台账单把 SDS 缺口取出来，存入米莉拉网络；
    ///  - 米莉拉自己的生产/消耗逻辑完全不变；
    ///  - 不接手动 WorkGiver_DoBill 路径（那条由 SDS 自己的 DoBill 桥负责）。
    /// </summary>
    internal static class SDS_ModCompat_MiliraExpanded
    {
        private static Type networkType;
        private static Type deviceType;
        private static PropertyInfo networkInstanceProp;
        private static PropertyInfo operationalProp;
        private static PropertyInfo recipeSourceProp;
        private static MethodInfo getAvailableCountMethod;
        private static MethodInfo getPreservedAvailableCountMethod;
        private static MethodInfo tryDepositMethod;
        private static MethodInfo canStoreAnyDefMethod;
        private static MethodInfo canDepositDefMethod;
        private static Type orderType;
        private static Type decomposerCompType;
        private static Type lifeEssenceCompType;
        private static MethodInfo ordersSnapshotMethod;
        private static FieldInfo ordersField;
        private static FieldInfo orderRecipeField;
        private static FieldInfo orderKindField;
        private static FieldInfo orderRemainingField;
        private static FieldInfo orderModeField;
        private static FieldInfo orderAllowedField;
        private static FieldInfo orderDeviceNamesField;
        private static FieldInfo orderIdField;
        private static PropertyInfo orderUsesAllowedProp;
        private static FieldInfo orderCorpseFilterField;
        private static MethodInfo orderReserveMethod;
        private static MethodInfo decomposeValueMethod;
        private static FieldInfo decConversionField;
        private static FieldInfo decEfficiencyField;
        private static FieldInfo decTargetField;
        private static FieldInfo leNutritionField;
        private static FieldInfo leOutputField;
        private static MethodInfo lifeEssenceNutritionMethod;
        private static Type matterExtensionType;
        private static MethodInfo requiredMatterMethod;
        private static PropertyInfo availableStandardMatterProp;
        private static ThingDef standardMatterUnitDef;
        private static bool resolved;
        private static readonly Dictionary<int, int> nextTickByDevice = new Dictionary<int, int>();
        private static readonly Dictionary<ThingDef, bool> canStoreCache = new Dictionary<ThingDef, bool>();
        private static bool canStoreCacheForceGear;

        /// <summary>
        /// E1：新档 / 读档清空按 <c>thingIDNumber</c> 的节流表（绝对 tick 跨档会回退失效）。
        /// <see cref="canStoreCache"/> 键是 <c>ThingDef</c>、值依赖 W33 开关，一并清空。
        /// </summary>
        internal static void Reset()
        {
            nextTickByDevice.Clear();
            ClearStoreCache();
        }

        /// <summary>W33：开关变化会让 CanStoreDef 的通道判据变化，必须清掉按 def 缓存的结果。</summary>
        internal static void ClearStoreCache()
        {
            canStoreCache.Clear();
        }

        public static void TryApply(Harmony harmony)
        {
            bool detected = false;
            try
            {
                detected = AccessTools.TypeByName("MiliraExpandedXY.Buildings.GameComponent_VirtualStorageNetwork") != null
                    || AccessTools.TypeByName("MiliraExpandedXY.Buildings.CompVirtualProductionDevice") != null;
                if (!Resolve())
                {
                    SDS_CompatReportHub.Register("米莉拉扩展-未来科技Milira Expanded - Future Technology",
                        "XiyueYM.MiliraExpandedXY", detected, null, null,
                        detected ? "关键类型 / 成员缺失（版本变化？）" : null);
                    return;
                }
                detected = true;
                SDS_CompatPatchReport report = new SDS_CompatPatchReport("米莉拉扩展-未来科技Milira Expanded - Future Technology");
                // 生产订单表是订单补料的唯一读取口：读不到时挂点仍在，但功能会静默退化，
                // 因此按「应生效挂点」记一条缺失，避免版本变化被日志掩盖。
                if (!HasOrdersAccessor())
                {
                    report.Missing("GameComponent_VirtualStorageNetwork.生产订单表（订单补料）",
                        "GetProductionOrdersSnapshot / wf / vk 均不存在（版本变化？）");
                }
                MethodInfo compTick = SDS_CompatTools.Method(deviceType, "CompTick", Type.EmptyTypes);
                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_MiliraExpanded),
                    "CompVirtualProductionDevice.CompTick（自动补料）",
                    compTick, nameof(CompTickPrefix), null,
                    "找不到 CompVirtualProductionDevice.CompTick()");

                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_MiliraExpanded),
                    "GameComponent_VirtualStorageNetwork.TryProcessAutomatedRecipeCycle（自动补料）",
                    SDS_CompatTools.Method(networkType, "TryProcessAutomatedRecipeCycle",
                        deviceType, typeof(Bill_Production), typeof(int)),
                    nameof(TryProcessAutomatedRecipeCyclePrefix), null,
                    "找不到 TryProcessAutomatedRecipeCycle(CompVirtualProductionDevice,Bill_Production,int)");

                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_MiliraExpanded),
                    "GameComponent_VirtualStorageNetwork.TryProcessManualRecomposeBill（自动补料）",
                    SDS_CompatTools.Method(networkType, "TryProcessManualRecomposeBill",
                        deviceType, typeof(Bill_Production)),
                    nameof(TryProcessManualRecomposeBillPrefix), null,
                    "找不到 TryProcessManualRecomposeBill(CompVirtualProductionDevice,Bill_Production)");

                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_MiliraExpanded),
                    "GameComponent_VirtualStorageNetwork.TryProcessRecomposeOrder（自动补料）",
                    SDS_CompatTools.Method(networkType, "TryProcessRecomposeOrder",
                        deviceType, typeof(int)),
                    nameof(TryProcessRecomposeOrderPrefix), null,
                    "找不到 TryProcessRecomposeOrder(CompVirtualProductionDevice,int)");

                // 分解订单 / 生命精华订单没有 recipeDef，输入是 allowedIngredients；
                // 这两个入口是它们唯一的起点，必须单独挂。
                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_MiliraExpanded),
                    "GameComponent_VirtualStorageNetwork.TryProcessDecomposeOrder（自动补料）",
                    decomposerCompType == null
                        ? null
                        : SDS_CompatTools.Method(networkType, "TryProcessDecomposeOrder",
                            decomposerCompType),
                    nameof(TryProcessForCompPrefix), null,
                    "找不到 TryProcessDecomposeOrder(MatterDecomposer_ThingComp)");

                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_MiliraExpanded),
                    "GameComponent_VirtualStorageNetwork.TryProcessLifeEssenceOrder（自动补料）",
                    lifeEssenceCompType == null
                        ? null
                        : SDS_CompatTools.Method(networkType, "TryProcessLifeEssenceOrder",
                            lifeEssenceCompType),
                    nameof(TryProcessForCompPrefix), null,
                    "找不到 TryProcessLifeEssenceOrder(LifeEssenceExtractor_ThingComp)");

                SDS_CompatReportHub.Register("米莉拉扩展-未来科技Milira Expanded - Future Technology",
                    "XiyueYM.MiliraExpandedXY", true, report,
                    "SDS → 米莉拉：仅补自动生产 / 链接工作台 / 分解与生命精华订单缺料");
            }
            catch (Exception e)
            {
                SDS_CompatReportHub.Register("米莉拉扩展-未来科技Milira Expanded - Future Technology",
                    "XiyueYM.MiliraExpandedXY", detected, null, null,
                    "应用异常 " + e.GetType().Name);
                SDS_Diag.ErrorOnce("米莉拉扩展-未来科技Milira Expanded - Future Technology 适配器应用失败（已跳过，双方原行为不受影响）", e);
            }
        }

        private static bool Resolve()
        {
            if (resolved)
            {
                return networkType != null && deviceType != null;
            }
            resolved = true;
            networkType = AccessTools.TypeByName("MiliraExpandedXY.Buildings.GameComponent_VirtualStorageNetwork");
            deviceType = AccessTools.TypeByName("MiliraExpandedXY.Buildings.CompVirtualProductionDevice");
            if (networkType == null || deviceType == null)
            {
                return false;
            }
            networkInstanceProp = SDS_CompatTools.Property(networkType, "Instance");
            operationalProp = SDS_CompatTools.Property(deviceType, "Operational");
            recipeSourceProp = SDS_CompatTools.Property(deviceType, "RecipeSource");
            getAvailableCountMethod = SDS_CompatTools.Method(networkType, "GetAvailableCount", typeof(ThingDef));
            getPreservedAvailableCountMethod = SDS_CompatTools.Method(networkType, "GetPreservedAvailableCount", typeof(ThingDef));
            tryDepositMethod = SDS_CompatTools.Method(networkType, "TryDeposit",
                typeof(Thing), typeof(int), typeof(int).MakeByRefType());
            Type foldedStorageType = AccessTools.TypeByName("MiliraExpandedXY.Buildings.FoldedStorage_ThingComp");
            // CanStoreAnyDef 是能力判据（含尸体/保真）；CanDepositDef 是普通玩家存入筛选（显式排除尸体）。
            canStoreAnyDefMethod = SDS_CompatTools.Method(foldedStorageType, "CanStoreAnyDef", typeof(ThingDef));
            canDepositDefMethod = SDS_CompatTools.Method(foldedStorageType, "CanDepositDef", typeof(ThingDef));
            orderType = AccessTools.TypeByName("MiliraExpandedXY.Buildings.VirtualProductionOrder");
            decomposerCompType = AccessTools.TypeByName("MiliraExpandedXY.Buildings.MatterDecomposer_ThingComp");
            lifeEssenceCompType = AccessTools.TypeByName("MiliraExpandedXY.Buildings.LifeEssenceExtractor_ThingComp");
            // 生产订单表优先走米莉拉的公共快照（源码里是可读名，不受混淆改名影响）；
            // 老版本没有这个公共入口时，再回退到私有字段（新版 wf，旧版 vk）。
            ordersSnapshotMethod = SDS_CompatTools.Method(networkType, "GetProductionOrdersSnapshot", Type.EmptyTypes);
            if (ordersSnapshotMethod == null)
            {
                ordersField = SDS_CompatTools.Field(networkType, "wf")
                    ?? SDS_CompatTools.Field(networkType, "vk");
            }
            if (orderType != null)
            {
                orderRecipeField = SDS_CompatTools.Field(orderType, "recipeDef");
                orderKindField = SDS_CompatTools.Field(orderType, "kind");
                orderRemainingField = SDS_CompatTools.Field(orderType, "remainingCount");
                orderModeField = SDS_CompatTools.Field(orderType, "mode");
                orderAllowedField = SDS_CompatTools.Field(orderType, "allowedIngredients");
                orderDeviceNamesField = SDS_CompatTools.Field(orderType, "allowedDeviceDefNames");
                orderIdField = SDS_CompatTools.Field(orderType, "orderId");
                orderUsesAllowedProp = SDS_CompatTools.Property(orderType, "UsesAllowedIngredients");
                orderCorpseFilterField = SDS_CompatTools.Field(orderType, "corpseFilter");
                orderReserveMethod = SDS_CompatTools.Method(orderType, "IngredientReserveFor", typeof(ThingDef));
            }
            decomposeValueMethod = SDS_CompatTools.Method(
                AccessTools.TypeByName("MiliraExpandedXY.Buildings.MatterDecomposeValueResolver"),
                "ResolveMarketValue", typeof(ThingDef));
            // 分解 / 生命精华的补料量按设备自己的参数算，不再用估值。
            Type decomposerPropsType =
                AccessTools.TypeByName("MiliraExpandedXY.Buildings.CompProperties_MatterDecomposer");
            decConversionField = SDS_CompatTools.Field(decomposerPropsType, "conversionFactor");
            decEfficiencyField = SDS_CompatTools.Field(decomposerPropsType, "efficiencyMultiplier");
            decTargetField = SDS_CompatTools.Field(decomposerPropsType, "targetMatterUnitsPerCycle");
            Type lifeEssencePropsType =
                AccessTools.TypeByName("MiliraExpandedXY.Buildings.CompProperties_LifeEssenceExtractor");
            leNutritionField = SDS_CompatTools.Field(lifeEssencePropsType, "nutritionPerInterval");
            leOutputField = SDS_CompatTools.Field(lifeEssencePropsType, "outputPerNutrition");
            lifeEssenceNutritionMethod = SDS_CompatTools.Method(lifeEssenceCompType, "a", typeof(ThingDef));
            matterExtensionType = AccessTools.TypeByName("MiliraExpandedXY.Buildings.MatterRecomposeRecipeExtension");
            requiredMatterMethod = SDS_CompatTools.Method(matterExtensionType, "RequiredMatterUnitsFor", typeof(Thing));
            availableStandardMatterProp = SDS_CompatTools.Property(networkType, "AvailableStandardMatterForDisplay");
            standardMatterUnitDef = DefDatabase<ThingDef>.GetNamedSilentFail("MEXY_StandardMatterUnit");
            if (networkInstanceProp == null || operationalProp == null
                || recipeSourceProp == null || getAvailableCountMethod == null
                || getPreservedAvailableCountMethod == null || tryDepositMethod == null)
            {
                networkType = null;
                deviceType = null;
                return false;
            }
            return true;
        }

        /// <summary>订单表是否可读（公共快照或私有字段任一可用）。</summary>
        private static bool HasOrdersAccessor()
        {
            return ordersSnapshotMethod != null || ordersField != null;
        }

        /// <summary>
        /// 读米莉拉网络的生产订单表：优先公共快照（返回克隆，remainingCount 已按
        /// 米莉拉 a(order) 计算，Forever=MaxValue、Maintain=目标-现有），再回退私有字段。
        /// 读不到时返回 null，由调用方跳过本次补料。
        /// </summary>
        private static IList GetOrders(object network)
        {
            if (network == null)
            {
                return null;
            }
            if (ordersSnapshotMethod != null)
            {
                try
                {
                    return ordersSnapshotMethod.Invoke(network, null) as IList;
                }
                catch
                {
                    return null;
                }
            }
            if (ordersField == null)
            {
                return null;
            }
            try
            {
                return ordersField.GetValue(network) as IList;
            }
            catch
            {
                return null;
            }
        }

        public static void TryProcessAutomatedRecipeCyclePrefix(object __instance, object[] __args)
        {
            try
            {
                ThingComp comp = __args != null && __args.Length > 0 ? __args[0] as ThingComp : null;
                Bill_Production manualBill = __args != null && __args.Length > 1
                    ? __args[1] as Bill_Production
                    : null;
                int expectedOrderId = __args != null && __args.Length > 2 && __args[2] is int id
                    ? id
                    : 0;
                if (comp?.parent == null || !comp.parent.Spawned || comp.parent.Map == null)
                {
                    return;
                }
                object network = __instance ?? networkInstanceProp?.GetValue(null, null);
                if (network == null)
                {
                    return;
                }
                if (manualBill != null)
                {
                    EnsureBillIngredients(network, manualBill, comp.parent, comp.parent.Map);
                }
                else
                {
                    EnsureOrderByIdOrScan(network, comp, comp.parent,
                        GetRecipeSource(comp, comp.parent), expectedOrderId, comp.parent.Map);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("米莉拉自动生产补料失败（已忽略，原逻辑不受影响）", e);
            }
        }

        public static void TryProcessManualRecomposeBillPrefix(object __instance, object[] __args)
        {
            try
            {
                ThingComp comp = __args != null && __args.Length > 0 ? __args[0] as ThingComp : null;
                Bill_Production bill = __args != null && __args.Length > 1
                    ? __args[1] as Bill_Production
                    : null;
                if (comp?.parent == null || !comp.parent.Spawned || comp.parent.Map == null)
                {
                    return;
                }
                object network = __instance ?? networkInstanceProp?.GetValue(null, null);
                if (network == null)
                {
                    return;
                }
                EnsureBillIngredients(network, bill, comp.parent, comp.parent.Map);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("米莉拉手动重组补料失败（已忽略，原逻辑不受影响）", e);
            }
        }

        public static void TryProcessRecomposeOrderPrefix(object __instance, object[] __args)
        {
            try
            {
                ThingComp comp = __args != null && __args.Length > 0 ? __args[0] as ThingComp : null;
                int expectedOrderId = __args != null && __args.Length > 1 && __args[1] is int id
                    ? id
                    : 0;
                if (comp?.parent == null || !comp.parent.Spawned || comp.parent.Map == null)
                {
                    return;
                }
                object network = __instance ?? networkInstanceProp?.GetValue(null, null);
                if (network == null)
                {
                    return;
                }
                EnsureOrderByIdOrScan(network, comp, comp.parent,
                    GetRecipeSource(comp, comp.parent), expectedOrderId, comp.parent.Map);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("米莉拉网络订单补料失败（已忽略，原逻辑不受影响）", e);
            }
        }

        /// <summary>
        /// 分解 / 生命精华：这两类订单没有 recipeDef，输入是 allowedIngredients，
        /// 由网络按「自家库存是否够」决定是否开工；这里在开工前把缺口从 SDS 补进网络。
        /// </summary>
        public static void TryProcessForCompPrefix(object __instance, object[] __args)
        {
            try
            {
                ThingComp comp = __args != null && __args.Length > 0 ? __args[0] as ThingComp : null;
                if (comp?.parent == null || !comp.parent.Spawned || comp.parent.Map == null)
                {
                    return;
                }
                object network = __instance ?? networkInstanceProp?.GetValue(null, null);
                if (network == null || !HasOrdersAccessor())
                {
                    return;
                }
                EnsureOrderByIdOrScan(network, comp, comp.parent, comp.parent, 0, comp.parent.Map);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("米莉拉分解 / 生命精华补料失败（已忽略，原逻辑不受影响）", e);
            }
        }

        private static Thing GetRecipeSource(object deviceComp, Thing deviceThing)
        {
            try
            {
                Thing source = recipeSourceProp?.GetValue(deviceComp, null) as Thing;
                return source ?? deviceThing;
            }
            catch
            {
                return deviceThing;
            }
        }

        public static void CompTickPrefix(object __instance)
        {
            try
            {
                if (__instance == null || deviceType == null || !deviceType.IsInstanceOfType(__instance))
                {
                    return;
                }
                ThingComp comp = __instance as ThingComp;
                if (comp == null)
                {
                    return;
                }
                object device = __instance;
                if (!SDS_CompatTools.GetBool(operationalProp, device))
                {
                    return;
                }
                Thing deviceThing = comp.parent;
                if (deviceThing == null || !deviceThing.Spawned || deviceThing.Map == null)
                {
                    return;
                }
                int id = deviceThing.thingIDNumber;
                if (nextTickByDevice.TryGetValue(id, out int next) && GenTicks.TicksGame < next)
                {
                    return;
                }
                if (nextTickByDevice.Count > 256)
                {
                    nextTickByDevice.Clear();
                }
                nextTickByDevice[id] = GenTicks.TicksGame + 30;

                object network = networkInstanceProp.GetValue(null, null);
                if (network == null)
                {
                    return;
                }
                Thing source = recipeSourceProp.GetValue(device, null) as Thing;
                if (source == null)
                {
                    source = deviceThing;
                }
                if (source == null || !source.Spawned || source.Map == null)
                {
                    return;
                }
                if (source.Map.reservationManager.OnlyReservationsForJobDef(
                        (LocalTargetInfo)source, JobDefOf.DoBill, true))
                {
                    return;
                }
                if (!SDS_CompatEndpoint.HasAnyEndpoint(source.Map))
                {
                    return;
                }
                EnsureAutoDeviceStock(network, device, deviceThing, source, source.Map);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("米莉拉扩展-未来科技Milira Expanded - Future Technology 兼容（自动补料）失败（已忽略，米莉拉行为不受影响）", e);
            }
        }

        /// <summary>
        /// 自动设备的唯一补料规则：先扫链接工作台账单，再扫网络订单；
        /// 只补缺口，不决定设备做哪一单，不碰设备状态机。
        /// </summary>
        private static bool EnsureAutoDeviceStock(object network, object deviceComp, Thing deviceThing,
            Thing source, Map map)
        {
            IBillGiver giver = source as IBillGiver;
            if (giver?.BillStack != null)
            {
                List<Bill> bills = giver.BillStack.Bills;
                for (int i = 0; i < bills.Count; i++)
                {
                    Bill_Production bill = bills[i] as Bill_Production;
                    if (bill == null || bill.suspended || !bill.ShouldDoNow())
                    {
                        continue;
                    }
                    // 米莉拉的自动配方路径会跳过网络订单账单；这里同样不碰。
                    if (bill.GetType().Name == "Bill_VirtualProductionOrder")
                    {
                        continue;
                    }
                    if (EnsureBillIngredients(network, bill, deviceThing, map))
                    {
                        return true;
                    }
                }
            }
            return EnsureOrderIngredients(network, deviceComp, deviceThing, source, map);
        }

        private static bool EnsureOrderByIdOrScan(object network, object deviceComp, Thing deviceThing,
            Thing source, int orderId, Map map)
        {
            if (orderId > 0 && HasOrdersAccessor() && orderType != null && orderIdField != null)
            {
                IList orders = GetOrders(network);
                if (orders != null)
                {
                    for (int i = 0; i < orders.Count; i++)
                    {
                        object order = orders[i];
                        if (order == null || !orderType.IsInstanceOfType(order))
                        {
                            continue;
                        }
                        int id;
                        try
                        {
                            id = orderIdField.GetValue(order) is int value ? value : -1;
                        }
                        catch
                        {
                            continue;
                        }
                        if (id == orderId)
                        {
                            return EnsureOrder(network, deviceComp, deviceThing, source, order, map);
                        }
                    }
                }
            }
            return EnsureOrderIngredients(network, deviceComp, deviceThing, source, map);
        }

        private static bool EnsureOrderIngredients(object network, object deviceComp, Thing deviceThing,
            Thing source, Map map)
        {
            if (!HasOrdersAccessor() || orderType == null || orderRecipeField == null
                || orderRemainingField == null || orderModeField == null
                || orderAllowedField == null || orderDeviceNamesField == null)
            {
                return false;
            }
            string deviceDefName = deviceThing?.def?.defName;
            string sourceDefName = source?.def?.defName;
            if (string.IsNullOrEmpty(deviceDefName) && string.IsNullOrEmpty(sourceDefName))
            {
                return false;
            }
            IList orders = GetOrders(network);
            if (orders == null || orders.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < orders.Count; i++)
            {
                object order = orders[i];
                if (order == null || !orderType.IsInstanceOfType(order))
                {
                    continue;
                }
                if (EnsureOrder(network, deviceComp, deviceThing, source, order, map))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool EnsureOrder(object network, object deviceComp, Thing deviceThing, Thing source,
            object order, Map map)
        {
            try
            {
                string mode = orderModeField.GetValue(order)?.ToString();
                int remaining = orderRemainingField.GetValue(order) is int r ? r : 0;
                if (mode != "Forever" && remaining <= 0)
                {
                    return false;
                }
                string deviceDefName = deviceThing?.def?.defName;
                string sourceDefName = source?.def?.defName;
                List<string> deviceNames = orderDeviceNamesField.GetValue(order) as List<string>;
                if (deviceNames == null || deviceNames.Count == 0
                    || (!deviceNames.Contains(deviceDefName) && !deviceNames.Contains(sourceDefName)))
                {
                    return false;
                }
                RecipeDef recipe = orderRecipeField.GetValue(order) as RecipeDef;
                if (recipe == null)
                {
                    // 分解 / 生命精华订单没有配方，输入只有 allowedIngredients。
                    return EnsureNonRecipeOrderIngredients(network, order, deviceComp, map);
                }
                if (FindMatterExtension(recipe) != null)
                {
                    return EnsureStandardMatterForRecipe(network, recipe, deviceThing, map);
                }
                List<ThingDef> allowed = orderAllowedField.GetValue(order) as List<ThingDef>;
                bool usesAllowed = allowed != null && allowed.Count > 0;
                if (orderUsesAllowedProp != null)
                {
                    try
                    {
                        usesAllowed = (bool)orderUsesAllowedProp.GetValue(order, null);
                    }
                    catch
                    {
                    }
                }
                if (usesAllowed && (allowed == null || allowed.Count == 0))
                {
                    return false;
                }
                if (!usesAllowed)
                {
                    allowed = null;
                }
                ThingFilter corpseFilter = orderCorpseFilterField?.GetValue(order) as ThingFilter;
                Func<ThingDef, int> reserveProvider = orderReserveMethod != null
                    ? (def => GetOrderReserve(order, def))
                    : null;
                return EnsureRecipeIngredients(network, recipe, allowed, null, map,
                    corpseFilter, reserveProvider);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 分解 / 生命精华订单的补料：网络只认 allowedIngredients，且必须
        /// 「可用量 > 该订单预留」才会开工。适配器只读订单与设备参数，把
        /// 「一个周期需要几件」翻译成 need；目标量 / 选 def / 缺口 / 预留由
        /// SDS_BillSupplyPlanner 决定（S16）。不预判订单总量、不改订单状态、不建第二账本。
        /// </summary>
        private static bool EnsureNonRecipeOrderIngredients(object network, object order,
            object deviceComp, Map map)
        {
            if (orderKindField == null || orderAllowedField == null || orderRemainingField == null)
            {
                return false;
            }
            string kind;
            try
            {
                kind = orderKindField.GetValue(order)?.ToString();
            }
            catch
            {
                return false;
            }
            if (kind != "Decompose" && kind != "LifeEssenceExtraction")
            {
                return false;
            }
            // 补料量要用「对应设备的 Props」算，30 tick 轮询拿到的是 CompVirtualProductionDevice，
            // 这里换成真正带参数的 comp。
            object propsComp = deviceComp;
            Thing deviceThing = (deviceComp as ThingComp)?.parent;
            if (kind == "Decompose" && decomposerCompType != null)
            {
                propsComp = FindCompOfType(deviceThing, decomposerCompType) ?? deviceComp;
            }
            else if (kind == "LifeEssenceExtraction" && lifeEssenceCompType != null)
            {
                propsComp = FindCompOfType(deviceThing, lifeEssenceCompType) ?? deviceComp;
            }
            List<ThingDef> allowed;
            try
            {
                allowed = orderAllowedField.GetValue(order) as List<ThingDef>;
            }
            catch
            {
                return false;
            }
            if (allowed == null || allowed.Count == 0)
            {
                return false;
            }
            int remaining;
            try
            {
                remaining = orderRemainingField.GetValue(order) is int value ? value : 0;
            }
            catch
            {
                remaining = 0;
            }
            ThingFilter corpseFilter = null;
            try
            {
                corpseFilter = orderCorpseFilterField?.GetValue(order) as ThingFilter;
            }
            catch
            {
            }
            object props = propsComp;
            Func<ThingDef, int> needForDef;
            if (kind == "Decompose")
            {
                needForDef = def => TranslateCycleNeedDecompose(props, def, remaining);
            }
            else
            {
                needForDef = def => TranslateCycleNeedLifeEssence(props, def, remaining);
            }

            // 存入被拒时换下一个 def，与原实现「逐个 def 尝试」一致。
            HashSet<ThingDef> rejected = null;
            while (true)
            {
                Dictionary<ThingDef, int> pool = new Dictionary<ThingDef, int>();
                HashSet<ThingDef> reserveApplied = new HashSet<ThingDef>();
                if (!SDS_API.TryPlanAllowedDefSupply(allowed, corpseFilter, needForDef,
                        def => GetOwnCount(network, def, pool),
                        def => rejected != null && rejected.Contains(def)
                            ? 0L
                            : SDS_CompatStock.CountAvailable(def),
                        CanStoreDef, def => GetOrderReserve(order, def), reserveApplied, pool,
                        out ThingDef chosen, out _, out _, out int shortfall))
                {
                    return false;
                }
                if (shortfall <= 0)
                {
                    return true;
                }
                TryWithdrawAndDeposit(network, chosen, shortfall, map, out int deposited);
                if (deposited > 0)
                {
                    return true;
                }
                if (rejected == null)
                {
                    rejected = new HashSet<ThingDef>();
                }
                rejected.Add(chosen);
            }
        }

        /// <summary>
        /// 参数翻译：分解一个周期吃几件，完全照米莉拉的公式
        /// （价值 × conversionFactor × efficiencyMultiplier / 10，再按 targetMatterUnitsPerCycle 取整）。
        /// 只产出「一个周期用量」这个输入事实；目标量 / 选 def / 缺口 / 预留由规划器决定。
        /// 读不到设备参数时只补 1 件，宁少勿多。
        /// </summary>
        private static int TranslateCycleNeedDecompose(object deviceComp, ThingDef def, int remaining)
        {
            float value = 0f;
            if (decomposeValueMethod != null)
            {
                try
                {
                    value = (float)decomposeValueMethod.Invoke(null, new object[] { def });
                }
                catch
                {
                    value = 0f;
                }
            }
            if (value <= 0f)
            {
                value = def?.BaseMarketValue ?? 0f;
            }
            if (value <= 0f)
            {
                return 1;
            }
            CompProperties props = (deviceComp as ThingComp)?.props;
            if (props == null || decConversionField == null || decEfficiencyField == null
                || decTargetField == null)
            {
                return 1;
            }
            try
            {
                float conversion = (float)decConversionField.GetValue(props);
                float efficiency = (float)decEfficiencyField.GetValue(props);
                float target = (float)decTargetField.GetValue(props);
                float perItem = value * Mathf.Max(0f, conversion) * Mathf.Max(0f, efficiency) / 10f;
                if (perItem <= 0.0001f)
                {
                    return 1;
                }
                float cycleUnits = remaining <= 0
                    ? Mathf.Max(0.01f, target)
                    : Mathf.Min(remaining, Mathf.Max(0.01f, target));
                return Mathf.Clamp(Mathf.CeilToInt(cycleUnits / perItem), 1, 200);
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// 参数翻译：生命精华一个周期吃几件，照米莉拉的公式（每周期营养 / 每件营养）；
        /// 整具尸体固定 1 件。只产出「一个周期用量」这个输入事实；目标量 / 选 def / 缺口 / 预留
        /// 由规划器决定。读不到设备参数时只补 1 件。
        /// </summary>
        private static int TranslateCycleNeedLifeEssence(object deviceComp, ThingDef def, int remaining)
        {
            if (def == null || def.IsCorpse)
            {
                return 1;
            }
            CompProperties props = (deviceComp as ThingComp)?.props;
            if (props == null || leNutritionField == null || leOutputField == null
                || lifeEssenceNutritionMethod == null)
            {
                return 1;
            }
            try
            {
                float perItemNutrition =
                    (float)lifeEssenceNutritionMethod.Invoke(null, new object[] { def });
                if (perItemNutrition <= 0.0001f)
                {
                    return 1;
                }
                float nutritionPerInterval = Mathf.Max(0.01f, (float)leNutritionField.GetValue(props));
                float outputPerNutrition = Mathf.Max(0.01f, (float)leOutputField.GetValue(props));
                float perItemUnits = perItemNutrition * outputPerNutrition;
                if (perItemUnits <= 0.0001f)
                {
                    return 1;
                }
                float cyclePerInterval = nutritionPerInterval * outputPerNutrition;
                float cycleUnits = remaining <= 0
                    ? cyclePerInterval
                    : Mathf.Min(remaining, cyclePerInterval);
                return Mathf.Clamp(Mathf.CeilToInt(cycleUnits / perItemUnits), 1, 200);
            }
            catch
            {
                return 1;
            }
        }

        private static object FindCompOfType(Thing thing, Type compType)
        {
            if (thing == null || compType == null)
            {
                return null;
            }
            List<ThingComp> comps = (thing as ThingWithComps)?.AllComps;
            if (comps == null)
            {
                return null;
            }
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] != null && compType.IsInstanceOfType(comps[i]))
                {
                    return comps[i];
                }
            }
            return null;
        }

        private static int GetOrderReserve(object order, ThingDef def)
        {
            if (orderReserveMethod == null || order == null || def == null)
            {
                return 0;
            }
            try
            {
                return (int)orderReserveMethod.Invoke(order, new object[] { def });
            }
            catch
            {
                return 0;
            }
        }

        private static object FindMatterExtension(RecipeDef recipe)
        {
            if (recipe?.modExtensions == null || matterExtensionType == null)
            {
                return null;
            }
            for (int i = 0; i < recipe.modExtensions.Count; i++)
            {
                object extension = recipe.modExtensions[i];
                if (extension != null && matterExtensionType.IsInstanceOfType(extension))
                {
                    return extension;
                }
            }
            return null;
        }

        private static float GetRequiredMatter(object extension, Thing deviceThing)
        {
            if (requiredMatterMethod == null || extension == null || deviceThing == null)
            {
                return 0f;
            }
            try
            {
                return (float)requiredMatterMethod.Invoke(extension, new object[] { deviceThing });
            }
            catch
            {
                return 0f;
            }
        }

        private static double GetAvailableStandardMatter(object network)
        {
            if (availableStandardMatterProp == null || network == null)
            {
                return 0d;
            }
            try
            {
                object value = availableStandardMatterProp.GetValue(network, null);
                return value is double d ? d : 0d;
            }
            catch
            {
                return 0d;
            }
        }

        /// <summary>
        /// 米莉拉标准物质不是普通配方原料：它由 MEXY_StandardMatterUnit 计数。
        /// 这里按缺口把 SDS 里的标准物质单位搬进米莉拉网络，让 CanConsumeStandardMatter 通过。
        /// </summary>
        private static bool EnsureStandardMatterForRecipe(object network, RecipeDef recipe,
            Thing deviceThing, Map map)
        {
            object extension = FindMatterExtension(recipe);
            if (extension == null)
            {
                return true;
            }
            if (requiredMatterMethod == null)
            {
                return false;
            }
            if (availableStandardMatterProp == null)
            {
                return false;
            }
            float required = GetRequiredMatter(extension, deviceThing);
            if (required <= 0f)
            {
                return true;
            }
            double available = GetAvailableStandardMatter(network);
            if (available + 0.0001d >= required)
            {
                return true;
            }
            int shortage = Mathf.CeilToInt((float)(required - available));
            ThingDef unitDef = standardMatterUnitDef
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("MEXY_StandardMatterUnit");
            if (unitDef == null)
            {
                return false;
            }
            if (!TryWithdrawAndDeposit(network, unitDef, shortage, map, out int deposited))
            {
                return false;
            }
            return GetAvailableStandardMatter(network) + 0.0001d >= required;
        }

        private static bool EnsureBillIngredients(object network, Bill bill, Thing deviceThing, Map map)
        {
            if (bill == null || bill.recipe == null)
            {
                return false;
            }
            if (FindMatterExtension(bill.recipe) != null)
            {
                return EnsureStandardMatterForRecipe(network, bill.recipe, deviceThing, map);
            }
            return EnsureRecipeIngredients(network, bill.recipe, null, bill, map);
        }

        private static bool EnsureRecipeIngredients(object network, RecipeDef recipe,
            List<ThingDef> allowedIngredients, Bill bill, Map map,
            ThingFilter corpseFilter = null, Func<ThingDef, int> reserveProvider = null)
        {
            if (recipe == null || recipe.ingredients == null || recipe.ingredients.Count == 0)
            {
                return false;
            }
            // 混合配料（营养 / 按价值折算）不是「按 def 计件」语义，交给米莉拉自己的路径。
            if (recipe.allowMixingIngredients)
            {
                return false;
            }
            Dictionary<ThingDef, int> pool = new Dictionary<ThingDef, int>();
            HashSet<ThingDef> reserveApplied = reserveProvider != null ? new HashSet<ThingDef>() : null;
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount slot = recipe.ingredients[i];
                if (slot == null || slot.filter == null)
                {
                    continue;
                }
                int need = Mathf.CeilToInt(slot.GetBaseCount());
                if (need <= 0)
                {
                    continue;
                }
                // S16：选 def / 算 required / 扣自有池全部由唯一规划器决定；
                // 适配器只提供「自有 / 外部 / 可存入」三个事实，并执行取货 + 存入。
                if (!SDS_API.TryPlanBillSupplySlot(slot, bill, recipe, allowedIngredients, corpseFilter,
                        need,
                        def => GetOwnCount(network, def, pool),
                        def => SDS_CompatStock.CountAvailable(def),
                        CanStoreDef, reserveProvider, reserveApplied, pool,
                        out ThingDef chosen, out _, out _, out int shortfall))
                {
                    return false;
                }
                if (shortfall <= 0)
                {
                    continue;
                }
                if (!TryWithdrawAndDeposit(network, chosen, shortfall, map, out int deposited))
                {
                    return false;
                }
                SDS_API.NotifyBillSupplyDeposited(pool, chosen, deposited);
            }
            return true;
        }

        private static bool CanStoreDef(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }
            // W33：开关变化时自失效。缓存值依赖 ForceWeaponApparelVirtual，
            // 不依赖任何设置回调，避免跨模块通知链漏接后长期使用旧结果。
            bool forceGear = SDS_API.IsForceWeaponApparelVirtual();
            if (forceGear != canStoreCacheForceGear)
            {
                canStoreCacheForceGear = forceGear;
                ClearStoreCache();
            }
            if (canStoreCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }
            // 尸体 / 保真品类走能力判据；普通品类走米莉拉玩家存入筛选（排除垃圾块等）。
            MethodInfo method = def.IsCorpse || SDS_API.RequiresPreservedStorage(def)
                ? canStoreAnyDefMethod
                : (canDepositDefMethod ?? canStoreAnyDefMethod);
            bool result = true;
            if (method != null)
            {
                try
                {
                    result = (bool)method.Invoke(null, new object[] { def });
                }
                catch
                {
                    result = true;
                }
            }
            canStoreCache[def] = result;
            return result;
        }

        private static int GetOwnCount(object network, ThingDef def, Dictionary<ThingDef, int> pool)
        {
            if (pool.TryGetValue(def, out int cached))
            {
                return cached;
            }
            long own = 0L;
            try
            {
                own += (long)getAvailableCountMethod.Invoke(network, new object[] { def });
            }
            catch
            {
            }
            try
            {
                own += (long)getPreservedAvailableCountMethod.Invoke(network, new object[] { def });
            }
            catch
            {
            }
            int value = own > int.MaxValue ? int.MaxValue : (int)Math.Max(0L, own);
            pool[def] = value;
            return value;
        }

        private static bool TryWithdrawAndDeposit(object network, ThingDef def, int count, Map map,
            out int deposited)
        {
            deposited = 0;
            int remaining = count;
            if (SDS_CompatStock.TryWithdrawVirtualByDef(def, remaining, map, out List<Thing> virtualMade))
            {
                int accepted = DepositAll(network, virtualMade, map, false);
                deposited += accepted;
                remaining -= accepted;
            }
            if (remaining > 0
                && SDS_CompatStock.TryWithdrawPreservedByDef(def, remaining, map, out List<Thing> preservedMade))
            {
                int accepted = DepositAll(network, preservedMade, map, true);
                deposited += accepted;
                remaining -= accepted;
            }
            return remaining <= 0;
        }

        private static int DepositAll(object network, List<Thing> things, Map map, bool preservedSource)
        {
            if (things == null || things.Count == 0)
            {
                return 0;
            }
            int deposited = 0;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                int requested = thing.stackCount;
                if (requested <= 0)
                {
                    continue;
                }
                int accepted = 0;
                try
                {
                    object[] args = new object[] { thing, requested, 0 };
                    tryDepositMethod.Invoke(network, args);
                    accepted = (int)args[2];
                }
                catch (Exception e)
                {
                    SDS_Diag.ErrorOnce("米莉拉扩展-未来科技Milira Expanded - Future Technology 兼容（存入网络）失败（已忽略）", e);
                }
                if (accepted > 0)
                {
                    deposited += Math.Min(accepted, requested);
                }
                int remainder = requested - Math.Max(0, accepted);
                if (remainder > 0 && thing != null && !thing.Destroyed && thing.stackCount > 0)
                {
                    ReturnOne(thing, map, preservedSource);
                }
            }
            return deposited;
        }

        private static void ReturnOne(Thing thing, Map map, bool preservedSource)
        {
            if (thing == null || thing.Destroyed)
            {
                return;
            }
            if (preservedSource)
            {
                SDS_API.TryReturnPreserved(thing, map, IntVec3.Invalid);
                return;
            }
            SDS_API.TryReturnVirtual(thing, map, IntVec3.Invalid);
        }
    }
}
