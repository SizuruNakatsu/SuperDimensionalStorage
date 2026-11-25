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

    // ============================================================================
    // 来源：ProjectE 等价交换
    // ============================================================================

    /// <summary>
    /// ProjectE 等价交换（packageId = Natsuki.ProjectE）适配器。
    ///
    /// 为什么需要：它的「装载清单瞬时转化」**不是原版装载管线**——
    /// `Dialog_LoadTransporters.TryAccept` 被它自己的前缀接管，直接对清单里的对象
    /// `SplitOff` + `Destroy`。我们的装载清单放的是**展示物**（不是镜像），
    /// 本体的「Destroy = 外部消费确认」覆盖不到，会表现为「看得到超维库存、转化后账本不减少」。
    ///
    /// 本适配器只做边界翻译（《设计原则》准则三）：
    ///  - 转化前，把展示物代表的数量走**统一取货事务**换成真身，再交给它按原版语义销毁；
    ///  - 保真通道（尸体 / 缩装 / 基因载体）不参与瞬时转化：从清单里摘掉（它的 `CanTransmute` 只认 Item，语义也对不齐）；
    ///  - 转化没真正发生（它自己失败 / 抛异常）→ Finalizer 把真身回充账本，绝不丢物；
    ///  - 区域转化（按格扫描）会把金库格的镜像当「地面物品」→ 从候选里剔除（容器内容物本来就不在格上）。
    ///
    /// 未安装时整块跳过；结构变化时只记挂点缺失，不影响本体。
    /// </summary>
    internal static class SDS_ModCompat_EquivalentExchange
    {
        private const string TableTypeName = "EquivalentExchange.Building_TransmutationTable";
        private const string InterfaceTypeName = "EquivalentExchange.Building_EmcInterface";
        private const string UtilityTypeName = "EquivalentExchange.TransmutationUtility";
        private const string TableDefName = "EE_TransmutationTable";
        private const string InterfaceDefName = "EE_EmcInterface";

        /// <summary>运行期探测结果：未安装时所有挂点都不存在，入口保持原样。</summary>
        private static bool detected;

        /// <summary>瞬时转化挂点是否已生效；未生效时 EE 对话框不再注入展示物（fail-closed）。</summary>
        private static bool hookReady;

        private static bool fieldsProbed;
        private static FieldInfo transferablesField;
        private static FieldInfo transportersField;
        private static FieldInfo mapField;

        public static void TryApply(Harmony harmony)
        {
            bool found = false;
            try
            {
                if (harmony == null)
                {
                    return;
                }
                found = AccessTools.TypeByName(TableTypeName) != null
                    || AccessTools.TypeByName(InterfaceTypeName) != null;
                detected = found;
                ProbeFields();
                if (!found)
                {
                    SDS_CompatReportHub.Register("ProjectE 等价交换", "Natsuki.ProjectE", false, null);
                    return;
                }

                SDS_CompatPatchReport report = new SDS_CompatPatchReport("ProjectE 等价交换");
                if (transferablesField == null || transportersField == null)
                {
                    report.Missing("Dialog_LoadTransporters 字段（transferables / transporters）",
                        "字段改名或结构变化");
                }

                // 优先级必须 High：EE 自己的 prefix 会接管 TryAccept 并跳过原版，
                // 更低优先级的 prefix 在它返回 false 后不会再执行（见 SDS_CompatTools.TryPatch 注释）。
                bool acceptPatched = SDS_CompatTools.TryPatch(harmony, report,
                    typeof(SDS_ModCompat_EquivalentExchange),
                    "Dialog_LoadTransporters.TryAccept（瞬时转化：展示物换真身）",
                    AccessTools.Method(typeof(Dialog_LoadTransporters), "TryAccept"),
                    nameof(TryAcceptPrefix), null, "找不到方法 TryAccept()",
                    nameof(TryAcceptFinalizer), Priority.High);

                Type utilityType = AccessTools.TypeByName(UtilityTypeName);
                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_EquivalentExchange),
                    "TransmutationUtility.GetTransmutableThingsInArea（金库投影过滤）",
                    utilityType != null
                        ? AccessTools.Method(utilityType, "GetTransmutableThingsInArea",
                            new[] { typeof(Map), typeof(IntVec3), typeof(int) })
                        : null,
                    null, nameof(AreaCandidatesPostfix),
                    utilityType == null
                        ? "找不到类型 " + UtilityTypeName
                        : "找不到方法 GetTransmutableThingsInArea(Map,IntVec3,int)");

                hookReady = acceptPatched && transferablesField != null && transportersField != null;
                SDS_CompatReportHub.Register("ProjectE 等价交换", "Natsuki.ProjectE", true, report,
                    hookReady ? null : "瞬时转化未接管：EE 对话框不注入展示物");
            }
            catch (Exception e)
            {
                SDS_CompatReportHub.Register("ProjectE 等价交换", "Natsuki.ProjectE", found, null,
                    null, "应用异常 " + e.GetType().Name);
                SDS_Diag.ErrorOnce("ProjectE 等价交换兼容补丁应用失败（已跳过，本体行为不受影响）", e);
            }
        }

        private static void ProbeFields()
        {
            if (fieldsProbed)
            {
                return;
            }
            fieldsProbed = true;
            transferablesField = AccessTools.Field(typeof(Dialog_LoadTransporters), "transferables");
            transportersField = AccessTools.Field(typeof(Dialog_LoadTransporters), "transporters");
            mapField = AccessTools.Field(typeof(Dialog_LoadTransporters), "map");
        }

        /// <summary>
        /// 装载清单注入闸门（由 `Patch_SDS_TransporterAddVirtual` 调用）：
        /// 装了 EE 但瞬时转化挂点没生效时，EE 的对话框不注入展示物，避免「展示物被它销毁、账本不动」。
        /// </summary>
        public static bool AllowInjection(Dialog_LoadTransporters dialog)
        {
            if (!detected)
            {
                return true;
            }
            if (hookReady)
            {
                return true;
            }
            return !IsTransmutationDialog(GetTransporters(dialog));
        }

        private static List<CompTransporter> GetTransporters(Dialog_LoadTransporters dialog)
        {
            if (dialog == null || !fieldsProbed || transportersField == null)
            {
                return null;
            }
            try
            {
                return transportersField.GetValue(dialog) as List<CompTransporter>;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>与 EE 自己的判定同口径：清单里**每一个**运输器都属于转化台 / EMC 接口。</summary>
        private static bool IsTransmutationDialog(List<CompTransporter> transporters)
        {
            if (transporters == null || transporters.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < transporters.Count; i++)
            {
                CompTransporter transporter = transporters[i];
                ThingWithComps parent = transporter != null ? transporter.parent : null;
                string defName = parent != null && parent.def != null ? parent.def.defName : null;
                if (defName != TableDefName && defName != InterfaceDefName)
                {
                    return false;
                }
            }
            return true;
        }

        // ============================ 瞬时转化：展示物换真身 ============================

        /// <summary>
        /// 在 EE 的前缀之前把清单改写成「它可以直接销毁的真身」。
        /// 只在 EE 的转化对话框上生效；其它情况完全不碰。
        /// </summary>
        public static bool TryAcceptPrefix(Dialog_LoadTransporters __instance, out SDS_EETransmuteState __state)
        {
            __state = null;
            try
            {
                if (__instance == null || !fieldsProbed
                    || transferablesField == null || transportersField == null)
                {
                    return true;
                }
                if (!IsTransmutationDialog(GetTransporters(__instance)))
                {
                    return true;
                }
                List<TransferableOneWay> transferables =
                    transferablesField.GetValue(__instance) as List<TransferableOneWay>;
                if (transferables == null || transferables.Count == 0)
                {
                    return true;
                }
                __state = ConvertRows(transferables);
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("ProjectE 等价交换 兼容（瞬时转化前置）失败（已忽略，它的原流程不受影响）", e);
                return true;
            }
        }

        /// <summary>
        /// EE 没真正消耗掉的真身必须回充账本（失败才落地），并把这些引用从清单里摘掉。
        /// 正常情况下 EE 会当场 Destroy，这里什么都不做。
        /// </summary>
        public static Exception TryAcceptFinalizer(Dialog_LoadTransporters __instance,
            SDS_EETransmuteState __state, Exception __exception)
        {
            if (__state == null)
            {
                return __exception;
            }
            try
            {
                Map map = null;
                if (__instance != null && mapField != null)
                {
                    map = mapField.GetValue(__instance) as Map;
                }
                IntVec3 cell = IntVec3.Invalid;
                List<CompTransporter> transporters = GetTransporters(__instance);
                if (transporters != null && transporters.Count > 0
                    && transporters[0] != null && transporters[0].parent != null)
                {
                    cell = transporters[0].parent.PositionHeld;
                }
                for (int i = 0; i < __state.materialized.Count; i++)
                {
                    Thing thing = __state.materialized[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }
                    if (thing.Spawned || thing.holdingOwner != null)
                    {
                        continue;
                    }
                    if (SDS_API.TryGetStockKey(thing, out SDS_StockKey key) && key != null)
                    {
                        // 先回充账本（成功即销毁实物），失败才落地；绝不吞物。
                        SDS_API.TryReturnVirtual(thing, map, cell);
                    }
                }
                for (int i = 0; i < __state.rows.Count; i++)
                {
                    TransferableOneWay row = __state.rows[i];
                    if (row == null || row.things == null)
                    {
                        continue;
                    }
                    for (int j = row.things.Count - 1; j >= 0; j--)
                    {
                        Thing thing = row.things[j];
                        if (thing == null || thing.Destroyed || __state.materialized.Contains(thing))
                        {
                            row.things.RemoveAt(j);
                        }
                    }
                    if (IsRowEmpty(row))
                    {
                        // 这一行本轮没有真正成交：清掉数量，避免对话框停留在一个空行上。
                        row.AdjustTo(0);
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("ProjectE 等价交换 兼容（瞬时转化回充）失败（实物已在世界，未重复记账）", e);
            }
            return __exception;
        }

        /// <summary>
        /// 把每一行里属于我们的展示物换成真身：
        ///  - 真货已经覆盖的数量不再重复取；
        ///  - 保真展示物直接摘掉（它的瞬时转化不支持保真通道）；
        ///  - 取不出来（禁用 / 库存不足）就摘掉，绝不留下能被它白烧的展示物。
        /// </summary>
        private static SDS_EETransmuteState ConvertRows(List<TransferableOneWay> transferables)
        {
            SDS_EETransmuteState state = null;
            for (int i = transferables.Count - 1; i >= 0; i--)
            {
                TransferableOneWay row = transferables[i];
                if (row == null || row.things == null || row.CountToTransfer <= 0)
                {
                    continue;
                }

                int realUnits = 0;
                bool hasVirtualFake = false;
                bool hasPreservedFake = false;
                int fakeStack = 0;
                SDS_StockKey virtualKey = null;
                for (int j = 0; j < row.things.Count; j++)
                {
                    Thing thing = row.things[j];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }
                    if (!SDS_API.IsDisplayThing(thing))
                    {
                        realUnits += Math.Max(0, thing.stackCount);
                        continue;
                    }
                    SDS_StockKey key;
                    if (!SDS_API.TryGetDisplayKey(thing, out key) || key == null)
                    {
                        hasPreservedFake = true;
                        continue;
                    }
                    if (key.preserved)
                    {
                        hasPreservedFake = true;
                        continue;
                    }
                    hasVirtualFake = true;
                    fakeStack += Math.Max(0, thing.stackCount);
                    if (virtualKey == null)
                    {
                        virtualKey = key;
                    }
                }
                if (!hasVirtualFake && !hasPreservedFake)
                {
                    continue;
                }

                if (!hasVirtualFake)
                {
                    // 整行只有保真展示物：摘掉；行里若还有真货则保留真货。
                    RemoveFakes(row);
                    if (IsRowEmpty(row))
                    {
                        transferables.RemoveAt(i);
                    }
                    continue;
                }

                int need = row.CountToTransfer - realUnits;
                int amount = need > 0 ? Math.Min(need, fakeStack) : 0;
                long available = SDS_API.GetAvailableCount(virtualKey);
                amount = SDS_API.ClampTakeToAvailable(amount, available);
                if (amount <= 0)
                {
                    RemoveFakes(row);
                    if (IsRowEmpty(row))
                    {
                        transferables.RemoveAt(i);
                    }
                    continue;
                }

                List<Thing> made;
                if (!SDS_API.TryWithdraw(virtualKey, amount, SDS_WithdrawMode.Hand,
                        SDS_ApiCaller.FromFaction(Faction.OfPlayer), out made, out _)
                    || made == null || made.Count == 0)
                {
                    // fail-closed：取不出来就不留展示物，绝不让它凭空给 EMC。
                    RemoveFakes(row);
                    if (IsRowEmpty(row))
                    {
                        transferables.RemoveAt(i);
                    }
                    continue;
                }

                RemoveFakes(row);
                for (int m = 0; m < made.Count; m++)
                {
                    Thing real = made[m];
                    if (real == null || real.Destroyed || real.stackCount <= 0)
                    {
                        continue;
                    }
                    row.things.Add(real);
                    if (state == null)
                    {
                        state = new SDS_EETransmuteState();
                    }
                    state.materialized.Add(real);
                    if (!state.rows.Contains(row))
                    {
                        state.rows.Add(row);
                    }
                }
                if (IsRowEmpty(row))
                {
                    transferables.RemoveAt(i);
                }
            }
            return state;
        }

        private static void RemoveFakes(TransferableOneWay row)
        {
            if (row == null || row.things == null)
            {
                return;
            }
            for (int i = row.things.Count - 1; i >= 0; i--)
            {
                Thing thing = row.things[i];
                if (thing != null && SDS_API.IsDisplayThing(thing))
                {
                    row.things.RemoveAt(i);
                }
            }
        }

        private static bool IsRowEmpty(TransferableOneWay row)
        {
            if (row == null || row.things == null || row.things.Count == 0)
            {
                return true;
            }
            for (int i = 0; i < row.things.Count; i++)
            {
                Thing thing = row.things[i];
                if (thing != null && !thing.Destroyed && thing.stackCount > 0)
                {
                    return false;
                }
            }
            return true;
        }

        // ============================ 区域转化：不把金库投影当地面物品 ============================

        /// <summary>
        /// `GetTransmutableThingsInArea` 是按格 `GetThingList` 扫描 —— 会扫到金库格的镜像。
        /// 镜像代表的是容器内容物，本来就不在“格上”，因此从候选里剔除。
        /// </summary>
        public static void AreaCandidatesPostfix(ref List<Thing> __result)
        {
            try
            {
                if (__result == null || __result.Count == 0)
                {
                    return;
                }
                for (int i = __result.Count - 1; i >= 0; i--)
                {
                    if (__result[i] == null || SDS_API.IsMirror(__result[i]))
                    {
                        __result.RemoveAt(i);
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("ProjectE 等价交换 兼容（区域转化过滤）失败（已忽略）", e);
            }
        }
    }

    /// <summary>一次瞬时转化的现场：换了哪些真身、动过哪些清单行（供 Finalizer 回充 / 摘引用）。</summary>
    internal sealed class SDS_EETransmuteState
    {
        public readonly List<Thing> materialized = new List<Thing>();
        public readonly List<TransferableOneWay> rows = new List<TransferableOneWay>();
    }
}
