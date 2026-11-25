using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 芯片物品的「使用」组件（继承原版 CompUsable）。
    /// 唯一改动点：浮动菜单选项文案走本 Mod 的 Keyed，保证中英文都正确
    /// （CompProperties_Usable.useLabel 是 [MustTranslate]，只能靠 DefInjected）。
    ///
    /// 规则 W49：在途物化标记只在交付期间有效 —— 芯片 / 手动存入链路上的任何回充、落地、
    /// 容器拒收退路都必须撤销标记；唯一实现是 Core 的 SDS_ReturnUtility（ReturnVirtual /
    /// ReturnPreserved 开头统一 Unmark），Vanilla 不另写撤销逻辑。
    /// </summary>
    public class SDS_CompUsable : CompUsable
    {
        protected override string FloatMenuOptionLabel(Pawn pawn)
        {
            return "SDS.Chip.UseLabel".Translate(parent.LabelShort).ToString();
        }
    }

    /// <summary>
    /// 植入体芯片的安装效果属性：只比原版多一个"覆盖更低一级芯片"的字段。
    /// 可用性阶梯（定稿）：无芯片可装基础 / 高级；已有基础只能装高级；已有高级两个都不能装。
    /// </summary>
    public class SDS_CompProperties_InstallChip : CompProperties_UseEffectInstallImplant
    {
        public HediffDef replacedHediffDef;

        public SDS_CompProperties_InstallChip()
        {
            compClass = typeof(SDS_CompInstallChip);
        }
    }

    /// <summary>
    /// 右键使用芯片即植入：复用原版 CompUsable + CompUseEffect 机制。
    /// 1) CanBeUsedBy 只按本 Mod 两个芯片做层级门控（原版 canUpgrade 不支持跨芯片覆盖）；
    /// 2) DoEffect 先摘被覆盖的旧芯片，再装到脑部（人 = Brain，机械体 = ArtificialBrain）；
    /// 3) 消耗芯片（原版安装效果不销毁父物品）。
    /// </summary>
    public class SDS_CompInstallChip : CompUseEffect_InstallImplant
    {
        public SDS_CompProperties_InstallChip ChipProps
        {
            get { return (SDS_CompProperties_InstallChip)props; }
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (p == null || p.health == null || p.RaceProps == null || p.RaceProps.body == null)
            {
                return false;
            }
            // 机械体不能自己装植入体，其权限完全继承自主人。
            if (p.RaceProps.IsMechanoid)
            {
                return "SDS.Chip.MechCannotInstall".Translate();
            }
            if ((!p.IsFreeColonist || p.HasExtraHomeFaction()) && !Props.allowNonColonists)
            {
                return "InstallImplantNotAllowedForNonColonists".Translate();
            }
            if (SDS_ImplantUtility.ResolveBrainPart(p) == null)
            {
                return "InstallImplantNoBodyPart".Translate() + ": " + Props.bodyPart.LabelShort;
            }
            HediffDef mine = Props.hediffDef;
            HediffDef current = SDS_ImplantUtility.CurrentChipHediffDef(p);
            if (mine == SDS_HediffDefOf.ChipB)
            {
                return current == SDS_HediffDefOf.ChipB
                    ? (AcceptanceReport)"InstallImplantAlreadyInstalled".Translate()
                    : true;
            }
            if (current == SDS_HediffDefOf.ChipB)
            {
                return "SDS.Chip.HigherAlreadyInstalled".Translate();
            }
            return current == null
                ? true
                : (AcceptanceReport)"InstallImplantAlreadyInstalled".Translate();
        }

        public override void DoEffect(Pawn user)
        {
            if (user == null || user.health == null)
            {
                return;
            }
            if (ChipProps.replacedHediffDef != null)
            {
                Hediff oldChip = user.health.hediffSet.GetFirstHediffOfDef(ChipProps.replacedHediffDef);
                if (oldChip != null)
                {
                    user.health.RemoveHediff(oldChip);
                }
            }
            HediffDef chipDef = Props.hediffDef;
            if (chipDef != null && user.health.hediffSet.GetFirstHediffOfDef(chipDef) == null)
            {
                BodyPartRecord part = SDS_ImplantUtility.ResolveBrainPart(user);
                if (part != null)
                {
                    user.health.AddHediff(chipDef, part);
                }
                else
                {
                    Log.Error("[超维存储] 未能为 " + user.LabelShort + " 找到脑部零件，芯片未植入。");
                }
            }
            // 芯片等级已变化：清端口芯片缓存，并让端点 / 访问点缓存立即失效。
            SDS_ImplantUtility.NotifyChipStateChanged();
            SDS_Rules.NotifyEndpointChanged();
            ConsumeChip();
        }

        /// <summary>消耗一个芯片物品（沿用原版 CompUseEffect_DestroySelf 的写法）。</summary>
        private void ConsumeChip()
        {
            Thing chip = parent;
            if (chip == null || chip.Destroyed || chip.stackCount <= 0)
            {
                return;
            }
            Thing consumed = chip.SplitOff(1);
            if (consumed != null && !consumed.Destroyed)
            {
                consumed.Destroy();
            }
        }
    }

    /// <summary>
    /// 小人 gizmo 入口：把背包里符合存入筛选的物品存入超维空间。
    /// 执行与门禁全部复用 SDS_ChipManualDeposit / SDS_DepositService，UI 不另写存入规则。
    /// </summary>
    internal static class SDS_ImplantGizmos
    {
        public static Gizmo MakeInventoryDepositGizmo(Pawn pawn)
        {
            Texture2D icon = ContentFinder<Texture2D>.Get("UI/Commands/Install", false);
            return new Command_Action
            {
                defaultLabel = "SDS.Chip.DepositInventory".Translate().ToString(),
                defaultDesc = "SDS.Chip.DepositInventoryDesc".Translate().ToString(),
                icon = icon,
                action = delegate
                {
                    int count = SDS_ChipManualDeposit.DepositInventory(pawn,
                        out SDS_ManualDepositFailure failure);
                    if (count <= 0)
                    {
                        string key = failure == SDS_ManualDepositFailure.NoPort
                            ? "SDS.Chip.NoPort"
                            : "SDS.Chip.NothingToDeposit";
                        Messages.Message(key.Translate(), pawn, MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                    Messages.Message("SDS.Chip.Deposited".Translate(count),
                        pawn, MessageTypeDefOf.TaskCompletion, false);
                }
            };
        }

        public static IEnumerable<Gizmo> AppendInventoryDepositGizmo(IEnumerable<Gizmo> original,
            Pawn pawn)
        {
            if (original != null)
            {
                foreach (Gizmo gizmo in original)
                {
                    if (gizmo != null)
                    {
                        yield return gizmo;
                    }
                }
            }
            yield return MakeInventoryDepositGizmo(pawn);
        }
    }

    /// <summary>
    /// 右键地图物品「存入超维空间」。
    ///
    /// 挂点是 FloatMenuMakerMap.GetOptions 的 Postfix：只往 __result 追加一条选项，
    /// 绝不改写 / 删除任何已有选项（provider 流水线保持原版）。
    /// 目标物品由本补丁自己按光标格 + 四邻格解析，不读 FloatMenuContext
    /// （它的 FirstSelectedPawn 依赖 currentProvider，postfix 阶段不可靠）。
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuMakerMap), "GetOptions")]
    internal static class Patch_SDS_FloatMenuDeposit
    {
        public static void Postfix(Vector3 clickPos, List<Pawn> selectedPawns,
            List<FloatMenuOption> __result)
        {
            try
            {
                if (__result == null || selectedPawns == null || selectedPawns.Count != 1)
                {
                    return;
                }
                Pawn pawn = selectedPawns[0];
                if (!SDS_ChipManualDeposit.PawnCanDeposit(pawn))
                {
                    return;
                }
                Thing thing = FirstDepositableThingNear(clickPos, pawn);
                if (thing == null)
                {
                    return;
                }
                FloatMenuOption option = MakeDepositOption(pawn, thing);
                if (option != null)
                {
                    __result.Add(option);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("超维空间右键「存入超维空间」入口生成失败（已忽略，不影响原版菜单）", e);
            }
        }

        /// <summary>光标所指格（含四邻）上第一件"可存入"的地图物品；建筑 / 小人一律跳过。</summary>
        private static Thing FirstDepositableThingNear(Vector3 clickPos, Pawn pawn)
        {
            Map map = pawn != null ? pawn.MapHeld : null;
            if (map == null)
            {
                return null;
            }
            IntVec3 center = clickPos.ToIntVec3();
            if (!center.InBounds(map))
            {
                return null;
            }
            for (int i = 0; i < 5; i++)
            {
                IntVec3 cell = i == 0 ? center : center + GenAdj.AdjacentCells[i - 1];
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = map.thingGrid.ThingsListAt(cell);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing thing = things[j];
                    if (IsDepositableMapItem(thing) && SDS_ChipManualDeposit.CanDepositThing(thing))
                    {
                        return thing;
                    }
                }
            }
            return null;
        }

        private static bool IsDepositableMapItem(Thing thing)
        {
            if (thing == null || thing.def == null || thing.Destroyed || !thing.Spawned)
            {
                return false;
            }
            if (thing is Pawn || thing is Building)
            {
                return false;
            }
            return thing.def.category == ThingCategory.Item;
        }

        private static FloatMenuOption MakeDepositOption(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null || SDS_JobDefOf.DepositToGrid == null)
            {
                return null;
            }
            Action action = delegate
            {
                string label = thing != null && !thing.Destroyed ? thing.LabelShort : string.Empty;
                if (thing == null || thing.Destroyed || !thing.Spawned
                    || !pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Deadly,
                        1, Math.Max(1, thing.stackCount), null, false))
                {
                    Messages.Message("SDS.Chip.DepositItemFailed".Translate(label),
                        MessageTypeDefOf.RejectInput, false);
                    return;
                }
                Job job = SDS_ChipManualDeposit.MakeDepositJob(pawn, thing);
                if (job != null)
                {
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
                else
                {
                    Messages.Message("SDS.Chip.DepositItemFailed".Translate(label),
                        MessageTypeDefOf.RejectInput, false);
                }
            };
            return new FloatMenuOption(
                "SDS.Chip.DepositItem".Translate(thing.LabelShort).ToString(),
                action,
                MenuOptionPriority.Default,
                null,
                thing,
                0f,
                null,
                null,
                true,
                0);
        }
    }

    /// <summary>小人 gizmo 入口：把背包里符合筛选的物品存入超维空间。</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class Patch_SDS_PawnDepositGizmo
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (__instance == null || !SDS_ChipManualDeposit.PawnCanDeposit(__instance))
                {
                    return;
                }
                __result = SDS_ImplantGizmos.AppendInventoryDepositGizmo(__result, __instance);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("超维空间「存入背包」gizmo 安装失败（已忽略，原版 gizmo 不受影响）", e);
            }
        }
    }
}
