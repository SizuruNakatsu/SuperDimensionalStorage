using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// C 组（基因 / 尸体 / 缩装）共用桥：把保真通道里的**真身**交给原版流程。
    ///
    /// 原则：
    ///  - 不伪造、不克隆：尸体 / 缩装家具 / 基因载体的全部价值都在内部对象里，只能用真身；
    ///  - 只在原版**明确要取用**的那一刻动手（打开手术选单 / 打开基因装配 / 点生长舱 /
    ///    输出到棺材 / 创建安装蓝图），不在每 tick 的扫描里落一批东西到地图上；
    ///  - 落点统一走端点访问点；任何一步失败都整体回滚到账本（数量守恒优先）。
    /// </summary>
    internal static class SDS_PreservedUsage
    {
        /// <summary>该小人此刻能否走到一个可用的取出端点（选中手术 / 选单前的硬门）。</summary>
        public static bool HasReachableEndpoint(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return false;
            }
            if (!SDS_Rules.Available || SDS_Core.Ledger == null)
            {
                return false;
            }
            return SDS_AccessPointResolver.TryGetNearestReachable(pawn, out _);
        }

        /// <summary>超维空间里是否有该 def 的保真真身（替代旧 grid.AnyPreservedOfDef）。</summary>
        public static bool HasPreservedOfDef(ThingDef def)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || def == null)
            {
                return false;
            }
            List<Thing> all = new List<Thing>();
            ledger.AppendPreservedThings(all);
            for (int i = 0; i < all.Count; i++)
            {
                Thing thing = all[i];
                if (thing != null && !thing.Destroyed && thing.def == def)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>把保真通道里的**这一件**真身摘出账本（不落地、不改状态）。</summary>
        public static bool TryWithdrawExact(Thing thing)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || thing == null || thing.Destroyed || !ledger.IsPreserved(thing))
            {
                return false;
            }
            // 直接取用必须尊重预留：该归属组没有可用量时，不抽走别的 Job 占住的真身。
            if (!SDS_Rules.TryGetStockKey(thing, out SDS_StockKey key) || key == null || !key.preserved
                || ledger.IsForbidden(key) || SDS_Availability.GetAvailableCount(key) <= 0L)
            {
                return false;
            }
            List<ThingCount> plan = new List<ThingCount>(1)
            {
                new ThingCount(thing, thing.stackCount)
            };
            if (!SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> real)
                || real == null || real.Count == 0)
            {
                return false;
            }
            // 极端情况下多摘了：其余原样退回账本，绝不吞物品。
            for (int i = 1; i < real.Count; i++)
            {
                SDS_Rules.ReturnThing(real[i], SDS_StockChannel.Preserved, null, null,
                    SDS_ReturnFallback.None);
            }
            return true;
        }

        /// <summary>按真身归属键摘出最早的一件（可带实物级校验）。</summary>
        public static bool TryWithdrawOne(SDS_StockKey key, Predicate<Thing> validator, out Thing thing)
        {
            thing = null;
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || key == null || !key.preserved
                || ledger.IsForbidden(key)
                || SDS_Availability.GetAvailableCount(key) <= 0L)
            {
                return false;
            }
            Thing chosen = null;
            List<Thing> all = new List<Thing>();
            ledger.AppendPreservedThings(all);
            for (int i = 0; i < all.Count; i++)
            {
                Thing candidate = all[i];
                if (candidate == null || candidate.Destroyed
                    || !SDS_Rules.TryGetStockKey(candidate, out SDS_StockKey candidateKey)
                    || candidateKey == null || !candidateKey.SameAs(key))
                {
                    continue;
                }
                if (validator != null && !validator(candidate))
                {
                    continue;
                }
                chosen = candidate;
                break;
            }
            if (chosen == null)
            {
                return false;
            }
            List<ThingCount> plan = new List<ThingCount>(1) { new ThingCount(chosen, 1) };
            if (!SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                return false;
            }
            thing = made[0];
            // 极端情况下多摘了：其余原样退回账本，绝不吞物品。
            for (int i = 1; i < made.Count; i++)
            {
                SDS_Rules.ReturnThing(made[i], SDS_StockChannel.Preserved, null, null,
                    SDS_ReturnFallback.None);
            }
            return true;
        }

        /// <summary>把一件已经离开账本的实物落到最近的端点访问点（失败退回账本 / 强制落地）。</summary>
        public static bool TryPlaceAtAccessPoint(Thing thing, Map map, IntVec3 reference, out Thing placed)
        {
            IntVec3 cell = NearestAccessCell(map, reference);
            if (!cell.IsValid)
            {
                cell = reference;
            }
            return TryPlaceAtCell(thing, map, cell, out placed);
        }

        /// <summary>
        /// 把一件已经离开账本的实物落到**指定格**（无效 / 越界回退地图中心）。
        /// 落不下时先退回账本，账本也收不回才强制落地，保证数量守恒。
        /// 与 <see cref="TryPlaceAtAccessPoint"/> 共用同一实现，只是落点来源不同。
        /// `markProduced` 只在实物需要在地图上等一个**未来**派活时才打开（来源排除标记）；
        /// 同一 tick 立刻会被原版 Job 接手的物化（W51 多槽缺口）不打开，避免取消清单后标记滞留。
        /// </summary>
        public static bool TryPlaceAtCell(Thing thing, Map map, IntVec3 cell, out Thing placed,
            bool markProduced = true)
        {
            placed = null;
            if (thing == null || thing.Destroyed || map == null)
            {
                return false;
            }
            IntVec3 target = cell.IsValid && cell.InBounds(map) ? cell : map.Center;
            if (SDS_PlacementUtility.TryPlaceOrSpawn(thing, target, map, out placed)
                && placed != null && !placed.Destroyed)
            {
                // 与 PreservedOffer 同一守卫：只有实际落下的是本次物化实例才打来源标记；
                // 并入玩家已有同类堆时只返回实际落点，不标记整堆。
                if (markProduced && ReferenceEquals(placed, thing))
                {
                    SDS_Rules.MarkProduced(placed);
                }
                return true;
            }
            if (!SDS_Rules.ReturnThing(thing, SDS_StockChannel.Preserved, map, null,
                    SDS_ReturnFallback.None))
            {
                // 账本也收不回（例如容量在途中被占满）：强制落地保证数量守恒。
                GenSpawn.Spawn(thing, map.Center, map);
                placed = thing;
                if (markProduced)
                {
                    SDS_Rules.MarkProduced(thing);
                }
            }
            return placed != null;
        }

        /// <summary>取出并落地一件「指定真身」。</summary>
        public static bool TryMaterialize(Thing thing, Map map, IntVec3 reference, out Thing placed)
        {
            placed = null;
            if (!TryWithdrawExact(thing))
            {
                return false;
            }
            return TryPlaceAtAccessPoint(thing, map, reference, out placed);
        }

        /// <summary>离参考点最近的端点访问格（没有端点时返回 Invalid）。</summary>
        public static IntVec3 NearestAccessCell(Map map, IntVec3 reference)
        {
            if (map == null)
            {
                return IntVec3.Invalid;
            }
            IntVec3 from = reference.IsValid && reference.InBounds(map) ? reference : map.Center;
            return SDS_AccessPointResolver.TryGetNearestAccess(from, map, out SDS_AccessPoint point)
                && point != null
                ? point.cell
                : IntVec3.Invalid;
        }

        // ── 基因装配：每次打开 Dialog_CreateXenogerm 只把保真基因包装进基因库一次 ──────────────
        // 原版对话框会多次调用 GetGenepacks（打开时两次、右键弹出后重新加载）。
        // 若每次调用都补货，玩家"弹出基因包"后立刻又有新的从库存补上，形成打地鼠。
        private static Building_GeneAssembler activeGeneDialog;
        private static bool activeGeneDialogInjected;

        /// <summary>新档 / 读档：清空上一局对话框持有的建筑引用。</summary>
        internal static void Reset()
        {
            activeGeneDialog = null;
            activeGeneDialogInjected = false;
        }

        public static void BeginGeneDialog(Building_GeneAssembler assembler)
        {
            activeGeneDialog = assembler;
            activeGeneDialogInjected = false;
        }

        /// <summary>本次对话框是否由我们接手注入（每次打开只返回一次 true）。</summary>
        public static bool TryClaimGeneDialogInjection(Building_GeneAssembler assembler)
        {
            if (assembler == null || !ReferenceEquals(assembler, activeGeneDialog)
                || activeGeneDialogInjected)
            {
                return false;
            }
            activeGeneDialogInjected = true;
            return true;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // 一、基因
    //  1) 手术清单可见性：库存里有异种胚芽时，让「植入异种胚芽」手术可选；
    //  2) 选择窗口：把保真异种胚芽追加进原版列表；玩家确认时物化真身，原版随后照常搬运；
    //  3) 基因装配：把保真基因包**放进已连接的基因库容器**，原版 Start/Check/Finish 全部原生通过；
    //  4) 生长舱：额外给一个「从超维空间植入胚胎」按钮，选中后物化胚胎，原版搬运原语接手。
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 原版只看地图上的 Xenogerm。库存里全是保真物时手术会被判成"没有可用胚芽"。
    /// 这里只补这一条可见性；真正的取用由选择窗口 + 物化完成。
    /// </summary>
    [HarmonyPatch(typeof(Recipe_ImplantXenogerm), nameof(Recipe_ImplantXenogerm.AvailableOnNow))]
    internal static class Patch_SDS_XenogermImplantAvailable
    {
        public static void Postfix(Thing thing, ref bool __result)
        {
            try
            {
                if (__result || !ModsConfig.BiotechActive || ThingDefOf.Xenogerm == null)
                {
                    return;
                }
                if (!(thing is Pawn pawn))
                {
                    return;
                }
                if (!SDS_Rules.Available
                    || !SDS_PreservedUsage.HasPreservedOfDef(ThingDefOf.Xenogerm))
                {
                    return;
                }
                if (SDS_PreservedUsage.HasReachableEndpoint(pawn))
                {
                    __result = true;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("异种胚芽可见性补丁失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>
    /// 选择窗口里的保真异种胚芽：只追加**真身对象**（未 spawn，但 GeneSet / Label 完整可读），
    /// 不在打开窗口时落地；玩家点「接受」后由 <see cref="Patch_SDS_XenogermMaterialize"/> 物化。
    /// </summary>
    [HarmonyPatch(typeof(Dialog_SelectXenogerm), MethodType.Constructor,
        new[] { typeof(Pawn), typeof(Map), typeof(Xenogerm), typeof(Action<Xenogerm>) })]
    internal static class Patch_SDS_SelectXenogermList
    {
        public static void Postfix(Pawn pawn, Map map, Dialog_SelectXenogerm __instance)
        {
            try
            {
                if (__instance == null || map == null || !ModsConfig.BiotechActive
                    || ThingDefOf.Xenogerm == null)
                {
                    return;
                }
                if (!SDS_Rules.Available
                    || !SDS_PreservedUsage.HasPreservedOfDef(ThingDefOf.Xenogerm))
                {
                    return;
                }
                if (!SDS_PreservedUsage.HasReachableEndpoint(pawn))
                {
                    return;
                }
                List<Xenogerm> list = Traverse.Create(__instance).Field("xenogerms").GetValue<List<Xenogerm>>();
                if (list == null)
                {
                    return;
                }
                List<Thing> all = new List<Thing>();
                SDS_Core.Ledger.AppendPreservedThings(all);
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] is Xenogerm xenogerm && !xenogerm.Destroyed && !list.Contains(xenogerm))
                    {
                        list.Add(xenogerm);
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真异种胚芽列表补丁失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>
    /// 玩家确认植入后，先把保真异种胚芽落到端点访问点，再让原版建立 bill。
    /// 这样 WorkGiver_DoBill 的取料腿拿到的是一张真实的地图实物，后续流程完全原生。
    /// </summary>
    [HarmonyPatch(typeof(Xenogerm), nameof(Xenogerm.SetTargetPawn))]
    internal static class Patch_SDS_XenogermMaterialize
    {
        public static bool Prefix(Xenogerm __instance, Pawn newTarget)
        {
            try
            {
                if (__instance == null || newTarget == null)
                {
                    return true;
                }
                ISDS_LedgerRead ledger = SDS_Core.Ledger;
                if (ledger == null || !ledger.IsPreserved(__instance))
                {
                    return true;
                }
                Map map = newTarget.MapHeld;
                if (map == null
                    || !SDS_PreservedUsage.TryMaterialize(__instance, map, newTarget.Position, out _))
                {
                    Messages.Message("SDS.Preserved.NoAccess".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真异种胚芽物化失败（已忽略，原版行为不受影响）", e);
                return true;
            }
        }
    }

    /// <summary>
    /// 记录"刚刚打开的是哪台基因装配器的库列表"，让 <see cref="Patch_SDS_GeneAssemblerGenepacks"/>
    /// 只在这次打开时补一次货（原版对话框自己会多次调用 GetGenepacks）。
    /// </summary>
    [HarmonyPatch(typeof(Dialog_CreateXenogerm), MethodType.Constructor,
        new[] { typeof(Building_GeneAssembler) })]
    internal static class Patch_SDS_GeneDialogBegin
    {
        public static void Prefix(Building_GeneAssembler geneAssembler)
        {
            try
            {
                SDS_PreservedUsage.BeginGeneDialog(geneAssembler);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("基因对话框登记失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>
    /// 基因装配：原版只从已连接的基因库容器里取基因包，而装配完成后
    /// <c>CheckAllContainersValid</c> 会逐件核对"仍在容器里"，否则取消。
    /// 因此这里不是"塞进返回值"就完事，而是**先把保真基因包放进有空位的基因库容器**，
    /// 再追加到返回列表 —— Start / Check / Finish 全部走原版逻辑。
    ///
    /// 只在打开库列表的那一次调用执行（见 <see cref="Patch_SDS_GeneDialogBegin"/>）：
    /// gizmo 探测与右键弹出后的重新加载都不补货，避免"弹出后立刻又有新的补上"。
    /// </summary>
    [HarmonyPatch(typeof(Building_GeneAssembler), nameof(Building_GeneAssembler.GetGenepacks))]
    internal static class Patch_SDS_GeneAssemblerGenepacks
    {
        public static void Postfix(Building_GeneAssembler __instance, bool includePowered,
            bool includeUnpowered, ref List<Genepack> __result)
        {
            try
            {
                if (!includePowered || !includeUnpowered || __result == null
                    || __instance == null || !__instance.Spawned || ThingDefOf.Genepack == null)
                {
                    return;
                }
                if (!SDS_PreservedUsage.TryClaimGeneDialogInjection(__instance))
                {
                    return;
                }
                if (!SDS_Rules.Available
                    || !SDS_PreservedUsage.HasPreservedOfDef(ThingDefOf.Genepack))
                {
                    return;
                }
                List<Thing> facilities = __instance.ConnectedFacilities;
                if (facilities == null || facilities.Count == 0)
                {
                    return;
                }
                List<CompGenepackContainer> containers = new List<CompGenepackContainer>();
                for (int i = 0; i < facilities.Count; i++)
                {
                    CompGenepackContainer container = facilities[i] != null
                        ? facilities[i].TryGetComp<CompGenepackContainer>()
                        : null;
                    if (container != null && container.CanLoadMore)
                    {
                        containers.Add(container);
                    }
                }
                if (containers.Count == 0)
                {
                    return;
                }
                List<Thing> all = new List<Thing>();
                SDS_Core.Ledger.AppendPreservedThings(all);
                int containerIndex = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    if (!(all[i] is Genepack pack) || pack.Destroyed || pack.def != ThingDefOf.Genepack
                        || pack.GeneSet == null || pack.GeneSet.GenesListForReading.Count == 0)
                    {
                        continue;
                    }
                    while (containerIndex < containers.Count && !containers[containerIndex].CanLoadMore)
                    {
                        containerIndex++;
                    }
                    if (containerIndex >= containers.Count)
                    {
                        break;
                    }
                    if (!SDS_PreservedUsage.TryWithdrawExact(pack))
                    {
                        continue;
                    }
                    CompGenepackContainer target = containers[containerIndex];
                    if (target.innerContainer.TryAdd(pack, false))
                    {
                        __result.Add(pack);
                        continue;
                    }
                    // 容器拒收：原样退回账本（失败再落地），绝不让基因包停在半路。
                    SDS_Rules.ReturnThing(pack, SDS_StockChannel.Preserved, __instance.Map,
                        null, SDS_ReturnFallback.Ground);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真基因包装入基因库失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>
    /// 生长舱：原版只用 Map.listerThings 找地图上的胚胎，库存里的保真胚胎看不到。
    /// 这里额外给一个按钮（仅在没有已选胚胎 / 无人时），点开列出保真胚胎；
    /// 选中后 <see cref="Patch_SDS_GrowthVatSelectEmbryo"/> 负责物化，原版搬运接手。
    /// </summary>
    [HarmonyPatch(typeof(Building_GrowthVat), nameof(Building_GrowthVat.GetGizmos))]
    internal static class Patch_SDS_GrowthVatEmbryoGizmo
    {
        public static void Postfix(Building_GrowthVat __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (__result == null || __instance == null || !__instance.Spawned
                    || ThingDefOf.HumanEmbryo == null)
                {
                    return;
                }
                if (__instance.Working || __instance.selectedEmbryo != null
                    || __instance.SelectedPawn != null || !Find.Storyteller.difficulty.ChildrenAllowed)
                {
                    return;
                }
                if (!SDS_Rules.Available
                    || !SDS_PreservedUsage.HasPreservedOfDef(ThingDefOf.HumanEmbryo))
                {
                    return;
                }
                Command_Action command = new Command_Action();
                command.defaultLabel = "SDS.GrowthVat.ImplantEmbryo".Translate().ToString() + "...";
                command.defaultDesc = "SDS.GrowthVat.ImplantEmbryoDesc"
                    .Translate(540000.ToStringTicksToPeriod()).Resolve();
                command.icon = Building_GrowthVat.InsertEmbryoIcon.Texture;
                command.action = delegate { OpenMenu(__instance); };
                if (!__instance.PowerOn)
                {
                    command.Disable("NoPower".Translate().CapitalizeFirst());
                }
                __result = WithExtra(__result, command);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("生长舱保真胚胎按钮失败（已忽略，原版行为不受影响）", e);
            }
        }

        private static void OpenMenu(Building_GrowthVat vat)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || vat == null || !vat.Spawned)
            {
                return;
            }
            List<Thing> all = new List<Thing>();
            ledger.AppendPreservedThings(all);
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is HumanEmbryo embryo) || embryo.Destroyed)
                {
                    continue;
                }
                HumanEmbryo captured = embryo;
                options.Add(new FloatMenuOption(captured.LabelCap, delegate
                {
                    vat.SelectEmbryo(captured);
                }, captured, Color.white));
            }
            if (options.Count == 0)
            {
                Messages.Message("SDS.GrowthVat.NoEmbryo".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static IEnumerable<Gizmo> WithExtra(IEnumerable<Gizmo> source, Gizmo extra)
        {
            foreach (Gizmo gizmo in source)
            {
                yield return gizmo;
            }
            yield return extra;
        }
    }

    /// <summary>选中的是保真胚胎时，先物化真身再赋值，后续原版 WorkGiver_HaulToGrowthVat 直接搬运。</summary>
    [HarmonyPatch(typeof(Building_GrowthVat), nameof(Building_GrowthVat.SelectEmbryo))]
    internal static class Patch_SDS_GrowthVatSelectEmbryo
    {
        public static bool Prefix(Building_GrowthVat __instance, ref HumanEmbryo embryo)
        {
            try
            {
                if (__instance == null || embryo == null || __instance.Map == null)
                {
                    return true;
                }
                ISDS_LedgerRead ledger = SDS_Core.Ledger;
                if (ledger == null || !ledger.IsPreserved(embryo))
                {
                    return true;
                }
                if (!SDS_PreservedUsage.TryMaterialize(embryo, __instance.Map,
                        __instance.InteractionCell, out Thing placed)
                    || !(placed is HumanEmbryo real))
                {
                    Messages.Message("SDS.Preserved.NoAccess".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                embryo = real;
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真胚胎物化失败（已忽略，原版行为不受影响）", e);
                return true;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // 二、尸体
    //  - 棺材 / 坟墓：原版 TryAcceptThing 对"未 spawn 的尸体"本来就能收（容器语义），
    //    唯一的问题是保真尸体还挂在超维账本里。这里在入口先把这件真身从账本摘出，
    //    失败则整体回滚；成功则原版把尸体放进容器（不经过地面，也不会被端点吸回）。
    //  - 复活机械体 / 屠宰：走 Bill 配料链（SDS_PreservedBillDispatch）。
    //      · 单槽（屠宰）：原版搜索失败后按保真真身挑「完整配料」交回原版 DoBill；
    //      · 多槽（复活机械体 = 1 尸体 + N 钢）：单通道候选补不齐所有槽，改为
    //        **把缺口的保真真身物化到工作台交互格**，再让原版重新搜料 / 搬运 / 入容器（W51）。
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 棺材 / 坟墓接收保真尸体：先把真身从超维账本摘出，交给原版容器语义；
    /// 原版拒收（已满 / 分配了别人）时把这件原样退回账本。
    /// </summary>
    [HarmonyPatch(typeof(Building_Casket), nameof(Building_Casket.TryAcceptThing))]
    internal static class Patch_SDS_CasketAcceptPreservedCorpse
    {
        public static bool Prefix(Thing thing, out bool __state)
        {
            __state = false;
            try
            {
                ISDS_LedgerRead ledger = SDS_Core.Ledger;
                if (ledger == null || thing == null || thing.Destroyed || !ledger.IsPreserved(thing))
                {
                    return true;
                }
                __state = SDS_PreservedUsage.TryWithdrawExact(thing);
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真尸体入棺预处理失败（已忽略，原版行为不受影响）", e);
                return true;
            }
        }

        public static Exception Finalizer(Thing thing, bool __state, bool __result, Exception __exception)
        {
            if (!__state || __result)
            {
                return __exception;
            }
            try
            {
                if (thing == null || thing.Destroyed)
                {
                    return __exception;
                }
                Map map = thing.MapHeld ?? Find.AnyPlayerHomeMap;
                SDS_Rules.ReturnThing(thing, SDS_StockChannel.Preserved, map, null,
                    SDS_ReturnFallback.None);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真尸体入棺回滚失败（已忽略）", e);
            }
            return __exception;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // 三、缩装（安装）
    //  原版 Designator_Install 只认"地图上被选中的 MinifiedThing"，而保真缩装物永远不 spawn。
    //  这里提供两件事：
    //   1) GenConstruct.PlaceBlueprintForInstall 的 API 桥：传进来的若是保真真身，
    //      先物化再建蓝图（蓝图 SpawnSetup 因此能通过"实体必须在地图上"的检查）；
    //   2) 玩家入口：可安装缩装由原版放置器 Designator_SDS_Install 确认落点后调用
    //      SDS_UI.TryPlaceInstallFromStock（唯一实现）：物化到端点 → 本守卫建蓝图，
    //      之后由原版搬运 + 建造完成；取消放置不取货（W31）。
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>安装蓝图 API 桥：保真缩装物先物化到端点访问点，再把蓝图绑定到这件真身。</summary>
    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.PlaceBlueprintForInstall))]
    internal static class Patch_SDS_PlaceBlueprintForInstall
    {
        public static void Prefix(ref MinifiedThing itemToInstall, IntVec3 center, Map map)
        {
            try
            {
                if (itemToInstall == null || map == null)
                {
                    return;
                }
                ISDS_LedgerRead ledger = SDS_Core.Ledger;
                if (ledger == null || !ledger.IsPreserved(itemToInstall))
                {
                    return;
                }
                if (!SDS_PreservedUsage.TryMaterialize(itemToInstall, map, center,
                        out Thing placed)
                    || !(placed is MinifiedThing real))
                {
                    return;
                }
                // ★ 原版 Designator_Install 在放置前会先取消旧蓝图；本桥必须补齐同一步，
                //   否则同一 innerThing 第二次 Add 会抛 ArgumentException（半生成蓝图）。
                SDS_InstallBlueprintGuard.CancelExistingBlueprint(real);
                itemToInstall = real;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("保真缩装物建蓝图预处理失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>
    /// 安装蓝图守卫（《取的到》§6.4 的唯一实现）。
    ///
    /// 原版约束：`ListerBuildings.reinstallationMap` 以 `innerThing` 为键，
    /// `RegisterInstallBlueprint` 用 `Dictionary.Add` —— 同一 innerThing 第二次注册必抛
    /// `ArgumentException`；而异常发生在 `GenSpawn.Spawn` 内部，蓝图已进地图但未完成初始化，
    /// 会作为"无主蓝图"落盘并在读档后每帧抛 `Nothing to install.`。
    ///
    /// 原版 `Designator_Install` 在放置前会 `CancelBlueprintsFor`；本 Mod 的所有安装入口
    /// 必须复用同一步，不再各自写判断。
    /// </summary>
    internal static class SDS_InstallBlueprintGuard
    {
        private static readonly FieldInfo MiniToInstallField =
            AccessTools.Field(typeof(Blueprint_Install), "miniToInstall");

        /// <summary>取消该缩装物已有的在册安装蓝图（与 Designator_Install.ProcessInput 同语义）。</summary>
        public static void CancelExistingBlueprint(MinifiedThing mini)
        {
            if (mini == null)
            {
                return;
            }
            Blueprint_Install existing = InstallBlueprintUtility.ExistingBlueprintFor(mini);
            if (existing != null && !existing.Destroyed)
            {
                existing.Destroy(DestroyMode.Cancel);
            }
        }

        /// <summary>
        /// 输出面板路径（W32 唯一实现）：取消旧蓝图 → 按放置器当前朝向建新蓝图；
        /// 任一步失败都回滚物资并清理半生成蓝图。
        /// 返回 false 表示调用方应提示失败，且物品已经不在账本里。
        /// </summary>
        public static bool TryReplaceBlueprintWithRollback(MinifiedThing mini, IntVec3 cell, Map map, Rot4 rot)
        {
            if (mini == null || map == null)
            {
                return false;
            }
            try
            {
                // 与原版 Designator_Install.ProcessInput 同语义：先取消旧安装单，再建新的。
                CancelExistingBlueprint(mini);
                return GenConstruct.PlaceBlueprintForInstall(mini, cell, map, rot,
                    Faction.OfPlayer, true) != null;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("输出/安装失败（物资已回滚）", e);
                DestroyBlueprintsReferencing(mini, map);
                SDS_Rules.ReturnThing(mini, SDS_StockChannel.Preserved, map, null,
                    SDS_ReturnFallback.Ground);
                return false;
            }
        }

        /// <summary>
        /// 只清理引用本件真身的蓝图。半生成对象没有进注册表，只能按私有字段匹配；
        /// 正常蓝图（本次已成功注册但后续异常）也会被一并回滚，保证"要么完整成功，要么无残留"。
        /// </summary>
        private static void DestroyBlueprintsReferencing(MinifiedThing mini, Map map)
        {
            if (mini == null || map == null || map.listerThings == null || MiniToInstallField == null)
            {
                return;
            }
            try
            {
                List<Thing> all = map.listerThings.AllThings;
                List<Blueprint_Install> victims = null;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] is Blueprint_Install bp
                        && ReferenceEquals(MiniToInstallField.GetValue(bp), mini))
                    {
                        (victims ??= new List<Blueprint_Install>()).Add(bp);
                    }
                }
                if (victims == null)
                {
                    return;
                }
                for (int i = 0; i < victims.Count; i++)
                {
                    Blueprint_Install bp = victims[i];
                    if (bp != null && !bp.Destroyed)
                    {
                        bp.Destroy(DestroyMode.Vanish);
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("输出/安装半生成蓝图清理失败（已跳过）", e);
            }
        }
    }
}
