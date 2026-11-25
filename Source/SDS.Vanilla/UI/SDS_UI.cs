using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>输出目标类型：建筑（核心 / 锚点）与植入体小人共用同一套输出流程。</summary>
    internal enum SDS_OutputTargetKind
    {
        Core,
        Port,
        ImplantedPawn
    }

    internal sealed class SDS_OutputTarget
    {
        public SDS_OutputTargetKind kind;
        public Thing building;
        public Pawn pawn;
    }

    /// <summary>
    /// 「输出」执行逻辑：把超维空间里的品类物化到所选目标处（落点 = 目标格，占用回退四周）。
    /// 取货一律走 SDS_Core 的事务 / 账本；本类只做目标校验、数量夹取与落地。
    /// </summary>
    internal static class SDS_OutputCommand
    {
        /// <summary>植入体小人端口总开关（已启用，见 SDS_ImplantUtility 的芯片判定）。</summary>
        internal const bool PawnPortEnabled = true;

        /// <summary>地图点选判定：在线的核心 / 锚点，或带可用芯片的玩家小人 / 机械体。</summary>
        internal static bool IsValidOutputTarget(Thing thing)
        {
            if (thing == null || !thing.Spawned || thing.Map == null || thing.Destroyed)
            {
                return false;
            }
            if (!SDS_Rules.Available)
            {
                return false;
            }
            return SDS_EndpointHandle.TryFromThing(thing, out SDS_EndpointHandle handle)
                && handle != null && handle.Operational;
        }

        internal static bool TryResolveTargetKind(Thing thing, out SDS_OutputTargetKind kind)
        {
            if (thing is Building_SDS_Core)
            {
                kind = SDS_OutputTargetKind.Core;
                return true;
            }
            if (thing is Building_SDS_Port)
            {
                kind = SDS_OutputTargetKind.Port;
                return true;
            }
            if (PawnPortEnabled && thing is Pawn)
            {
                kind = SDS_OutputTargetKind.ImplantedPawn;
                return true;
            }
            kind = SDS_OutputTargetKind.Core;
            return false;
        }

        /// <summary>地图点选后按 Thing 直接输出（虚拟通道）。</summary>
        internal static bool TryOutputToThing(Thing thing, SDS_StockKey key, int count,
            out string message)
        {
            message = null;
            if (thing == null || !TryResolveTargetKind(thing, out SDS_OutputTargetKind kind))
            {
                message = "SDS.Output.TargetGone".Translate().ToString();
                return false;
            }
            return TryOutput(MakeTarget(kind, thing), key, count, out message);
        }

        /// <summary>地图点选后按 Thing 直接输出（保真通道：按真身归属键逐件摘真身）。</summary>
        internal static bool TryOutputPreservedToThing(Thing thing, SDS_StockKey key, int count,
            out string message)
        {
            message = null;
            if (thing == null || !TryResolveTargetKind(thing, out SDS_OutputTargetKind kind))
            {
                message = "SDS.Output.TargetGone".Translate().ToString();
                return false;
            }
            return TryOutputPreserved(MakeTarget(kind, thing), key, count, out message);
        }

        private static SDS_OutputTarget MakeTarget(SDS_OutputTargetKind kind, Thing thing)
        {
            SDS_OutputTarget target = new SDS_OutputTarget { kind = kind };
            if (kind == SDS_OutputTargetKind.ImplantedPawn)
            {
                target.pawn = thing as Pawn;
            }
            else
            {
                target.building = thing;
            }
            return target;
        }

        /// <summary>解析输出落点（地图 + 中心格）；失败时给出提示。</summary>
        private static bool TryResolvePlacement(SDS_OutputTarget target, out Map map,
            out IntVec3 center, out string message)
        {
            map = null;
            center = IntVec3.Invalid;
            message = null;
            if (target == null)
            {
                message = "SDS.Output.NoTarget".Translate().ToString();
                return false;
            }
            if (target.kind == SDS_OutputTargetKind.ImplantedPawn)
            {
                SpawnedPawn(target.pawn, out map, out center, out message);
                return map != null;
            }
            if (target.building == null || !target.building.Spawned || target.building.Map == null)
            {
                message = "SDS.Output.TargetGone".Translate().ToString();
                return false;
            }
            if (!IsValidOutputTarget(target.building))
            {
                message = "SDS.Output.TargetGone".Translate().ToString();
                return false;
            }
            map = target.building.Map;
            center = target.building.Position;
            return true;
        }

        /// <summary>把一批实物落到目标处；放不下就强制生成（保证数量守恒）。返回落地总量。</summary>
        internal static int PlaceAll(List<Thing> made, Map map, IntVec3 center)
        {
            // 这里【不】打来源排除标记：手动输出是玩家显式要求"把东西摆在目标处"，
            // 之后被带芯片的小人搬进储存区 / 容器时按定稿正常数字化即可。
            int placed = 0;
            if (made == null)
            {
                return 0;
            }
            for (int i = 0; i < made.Count; i++)
            {
                Thing thing = made[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                int amount = thing.stackCount;
                if (!SDS_PlacementUtility.TryPlaceOrSpawn(thing, center, map, out Thing landed)
                    || landed == null)
                {
                    continue;
                }
                placed += amount;
            }
            return placed;
        }

        /// <summary>手动输出上限必须扣掉 Job 预留（预留只影响可用量，不动物理量）；0 表示「全部」。</summary>
        internal static int ClampToAvailable(SDS_StockKey key, int count)
        {
            if (key == null)
            {
                return 0;
            }
            return SDS_Availability.ClampTakeToAvailable(count > 0 ? count : int.MaxValue,
                SDS_Availability.GetAvailableCount(key));
        }

        /// <summary>虚拟通道输出：按品类键物化出实物并落到目标处。</summary>
        internal static bool TryOutput(SDS_OutputTarget target, SDS_StockKey key, int count,
            out string message)
        {
            message = null;
            if (key == null || key.preserved)
            {
                message = "SDS.Output.NoKey".Translate().ToString();
                return false;
            }
            if (SDS_Core.Ledger == null || !SDS_Rules.Available)
            {
                message = "SDS.Output.NoGrid".Translate().ToString();
                return false;
            }
            SDS_SupplyFailure failure = SDS_Availability.GetFailure(key);
            if (failure == SDS_SupplyFailure.UnresolvedDef)
            {
                message = "SDS.Output.Unresolved".Translate(key.EffectiveDefName ?? "?").ToString();
                return false;
            }
            if (!TryResolvePlacement(target, out Map map, out IntVec3 center, out message))
            {
                return false;
            }
            count = ClampToAvailable(key, count);
            if (count <= 0)
            {
                failure = SDS_Availability.GetFailure(key);
                message = failure == SDS_SupplyFailure.Forbidden
                    ? "SDS.Output.Forbidden".Translate().ToString()
                    : "SDS.Output.Insufficient".Translate().ToString();
                return false;
            }
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery
                {
                    key = key,
                    quantity = count
                },
                endpoint = PortOf(target),
                mode = SDS_WithdrawMode.Hand
            };
            if (!SDS_Rules.TryWithdraw(plan, null, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                message = "SDS.Output.RebuildFailed".Translate(key.EffectiveDefName ?? "?").ToString();
                return false;
            }
            int placed = PlaceAll(made, map, center);
            message = "SDS.Output.Done".Translate(KeyLabel(key), placed).ToString();
            return placed > 0;
        }

        /// <summary>保真通道输出：把该真身归属组的实体原样落地（不重建）。</summary>
        internal static bool TryOutputPreserved(SDS_OutputTarget target, SDS_StockKey key,
            int count, out string message)
        {
            message = null;
            if (key == null || !key.preserved)
            {
                message = "SDS.Output.NoKey".Translate().ToString();
                return false;
            }
            if (SDS_Core.Ledger == null || !SDS_Rules.Available)
            {
                message = "SDS.Output.NoGrid".Translate().ToString();
                return false;
            }
            if (!TryResolvePlacement(target, out Map map, out IntVec3 center, out message))
            {
                return false;
            }
            count = ClampToAvailable(key, count);
            if (count <= 0)
            {
                message = IsForbidden(key)
                    ? "SDS.Output.Forbidden".Translate().ToString()
                    : "SDS.Output.Insufficient".Translate().ToString();
                return false;
            }
            List<ThingCount> plan = BuildPreservedPlan(key, count, out int planned);
            if (planned <= 0 || plan.Count == 0
                || !SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                message = "SDS.Output.Insufficient".Translate().ToString();
                return false;
            }
            int placed = PlaceAll(made, map, center);
            message = "SDS.Output.Done".Translate(KeyLabel(key), placed).ToString();
            return placed > 0;
        }

        /// <summary>点选目标是否是棺材 / 坟墓（只对保真尸体开放这条输出路径）。</summary>
        internal static bool IsValidCasketTarget(Thing thing)
        {
            return thing != null && thing.Spawned && !thing.Destroyed && thing.Map != null
                && thing is Building_Casket;
        }

        /// <summary>保真尸体入棺 / 入墓：摘真身交给原版容器；容器满则退回地面（数量守恒）。</summary>
        internal static bool TryOutputPreservedToCasket(Thing casketThing, SDS_StockKey key,
            int count, out string message)
        {
            message = null;
            if (!(casketThing is Building_Casket casket) || key == null || !key.preserved)
            {
                message = "SDS.Output.NoKey".Translate().ToString();
                return false;
            }
            if (SDS_Core.Ledger == null || !SDS_Rules.Available)
            {
                message = "SDS.Output.NoGrid".Translate().ToString();
                return false;
            }
            count = ClampToAvailable(key, count);
            if (count <= 0)
            {
                message = IsForbidden(key)
                    ? "SDS.Output.Forbidden".Translate().ToString()
                    : "SDS.Output.Insufficient".Translate().ToString();
                return false;
            }
            List<ThingCount> plan = BuildPreservedPlan(key, count, out int planned);
            if (planned <= 0 || plan.Count == 0
                || !SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                message = "SDS.Output.Insufficient".Translate().ToString();
                return false;
            }
            Map map = casket.Map;
            IntVec3 center = casket.Position;
            int placed = 0;
            for (int i = 0; i < made.Count; i++)
            {
                Thing thing = made[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                int amount = thing.stackCount;
                if (casket.TryAcceptThing(thing, true))
                {
                    placed += amount;
                    continue;
                }
                if (!SDS_PlacementUtility.TryPlaceOrSpawn(thing, center, map)
                    || thing.Destroyed)
                {
                    continue;
                }
                placed += amount;
            }
            message = "SDS.Output.Done".Translate(KeyLabel(key), placed).ToString();
            return placed > 0;
        }

        /// <summary>按「真身归属键」取出最多 wanted 件的精确计划（复用交易桶的唯一计划实现）。</summary>
        internal static List<ThingCount> BuildPreservedPlan(SDS_StockKey key, int wanted,
            out int planned)
        {
            planned = 0;
            List<Thing> all = new List<Thing>();
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || key == null)
            {
                return new List<ThingCount>();
            }
            ledger.AppendPreservedThings(all);
            List<Thing> bucket = new List<Thing>();
            for (int i = 0; i < all.Count; i++)
            {
                Thing thing = all[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                if (SDS_Rules.TryGetStockKey(thing, out SDS_StockKey thingKey)
                    && thingKey != null && thingKey.SameAs(key))
                {
                    bucket.Add(thing);
                }
            }
            return SDS_PreservedBuckets.TakePlan(bucket, wanted, out planned);
        }

        /// <summary>可安装缩装（Minifiable 且有 installBlueprintDef）判定（W31）。</summary>
        internal static bool IsInstallablePreservedKey(SDS_StockKey key)
        {
            return key != null && key.preserved && key.thingDef != null
                && key.thingDef.Minifiable && key.thingDef.installBlueprintDef != null;
        }

        /// <summary>安装放置合法性判定（唯一实现，W31）：可用量 + 地图 + 原版 CanPlaceBlueprintAt。</summary>
        internal static AcceptanceReport CanPlaceInstallAt(SDS_StockKey key, Map map, IntVec3 cell,
            Rot4 rot)
        {
            if (!IsInstallablePreservedKey(key))
            {
                return "SDS.Output.NoKey".Translate().ToString();
            }
            if (SDS_Core.Ledger == null || !SDS_Rules.Available)
            {
                return "SDS.Output.NoGrid".Translate().ToString();
            }
            if (map == null || !cell.IsValid || !cell.InBounds(map))
            {
                return "SDS.Output.TargetGone".Translate().ToString();
            }
            if (SDS_Availability.GetAvailableCount(key) <= 0L)
            {
                return "SDS.Output.Insufficient".Translate().ToString();
            }
            return GenConstruct.CanPlaceBlueprintAt(key.thingDef, cell, rot, map,
                false, null, null, key.stuff);
        }

        /// <summary>
        /// 可安装缩装的唯一落点实现（W31 / W32）：校验 → 摘 1 件真身 → 物化到端点 → 建安装蓝图。
        /// 只在「确认落点」时调用；取消放置不取货。
        /// </summary>
        internal static bool TryPlaceInstallFromStock(SDS_StockKey key, Map map, IntVec3 cell,
            Rot4 rot, out string message)
        {
            message = null;
            AcceptanceReport report = CanPlaceInstallAt(key, map, cell, rot);
            if (!report.Accepted)
            {
                message = report.Reason.NullOrEmpty()
                    ? "SDS.Output.CannotPlace".Translate().ToString()
                    : report.Reason;
                return false;
            }
            List<ThingCount> plan = BuildPreservedPlan(key, 1, out int planned);
            if (planned <= 0 || plan.Count == 0
                || !SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                message = "SDS.Output.Insufficient".Translate().ToString();
                return false;
            }
            Thing thing = made[0];
            for (int i = 1; i < made.Count; i++)
            {
                SDS_Rules.ReturnThing(made[i], SDS_StockChannel.Preserved, map, null,
                    SDS_ReturnFallback.None);
            }
            if (!(thing is MinifiedThing))
            {
                SDS_Rules.ReturnThing(thing, SDS_StockChannel.Preserved, map, null,
                    SDS_ReturnFallback.None);
                message = "SDS.Output.NoKey".Translate().ToString();
                return false;
            }
            if (!SDS_PreservedUsage.TryPlaceAtAccessPoint(thing, map, cell, out Thing placed)
                || !(placed is MinifiedThing real))
            {
                message = "SDS.Output.NoTarget".Translate().ToString();
                return false;
            }
            if (!SDS_InstallBlueprintGuard.TryReplaceBlueprintWithRollback(real, cell, map, rot))
            {
                message = "SDS.Output.InstallFailed".Translate().ToString();
                return false;
            }
            message = "SDS.Output.InstallStarted".Translate(KeyLabel(key)).ToString();
            return true;
        }

        internal static bool IsForbidden(SDS_StockKey key)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            return ledger != null && key != null && ledger.IsForbidden(key);
        }

        /// <summary>读档后把账本禁用态同步到保真真身（CompForbiddable）；虚拟通道无需物理标记。</summary>
        internal static void SyncForbiddenToPreserved()
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null)
            {
                return;
            }
            List<Thing> preserved = new List<Thing>();
            ledger.AppendPreservedThings(preserved);
            for (int i = 0; i < preserved.Count; i++)
            {
                Thing thing = preserved[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                if (SDS_Rules.TryGetStockKey(thing, out SDS_StockKey key) && ledger.IsForbidden(key))
                {
                    thing.SetForbidden(true, false);
                }
            }
        }

        internal static ISDS_Port PortOf(SDS_OutputTarget target)
        {
            if (target == null)
            {
                return null;
            }
            if (target.kind == SDS_OutputTargetKind.ImplantedPawn)
            {
                return target.pawn != null ? SDS_EndpointHandle.ForPawn(target.pawn, false) : null;
            }
            ISDS_StorageEndpoint endpoint = target.building as ISDS_StorageEndpoint;
            return endpoint != null ? SDS_EndpointHandle.ForBuilding(endpoint) : null;
        }

        private static void SpawnedPawn(Pawn pawn, out Map map, out IntVec3 center,
            out string message)
        {
            map = null;
            center = IntVec3.Invalid;
            message = null;
            if (pawn == null || !pawn.Spawned || pawn.Map == null || pawn.Dead
                || !SDS_ImplantUtility.IsPortOperational(pawn))
            {
                message = "SDS.Output.TargetGone".Translate().ToString();
                return;
            }
            map = pawn.Map;
            center = pawn.Position;
        }

        /// <summary>品类键的可读名称（材质 + 名称 + 品质括号），与账本 / 列表同口径。</summary>
        internal static string KeyLabel(SDS_StockKey key)
        {
            if (key == null)
            {
                return "?";
            }
            ThingDef def = key.thingDef;
            if (def == null)
            {
                return key.EffectiveDefName ?? "?";
            }
            string label = GenLabel.ThingLabel(def, key.stuff, 1).CapitalizeFirst();
            if (key.qualityInt >= 0)
            {
                label += " (" + ((QualityCategory)key.qualityInt).GetLabel() + ")";
            }
            return label;
        }
    }

    /// <summary>
    /// 「输出」目标选择：不在面板里列名单，而是关掉面板后用原版 Targeter 让玩家直接点选
    /// 核心 / 锚点 / 带可用芯片的植入体小人（与未来科技一致）。
    /// 虚拟 / 保真两条通道共用同一套点选流程；可安装缩装改走原版安装放置器（W31）。
    /// </summary>
    internal static class SDS_OutputTargeting
    {
        private enum Mode
        {
            Endpoint,
            Casket
        }

        private static SDS_StockKey pendingKey;
        private static SDS_StockKey pendingPreservedKey;
        private static int pendingCount;
        private static Mode pendingMode;

        public static void Begin(SDS_StockKey key, int count)
        {
            if (key == null)
            {
                return;
            }
            pendingKey = key;
            pendingPreservedKey = null;
            pendingCount = count <= 0 ? int.MaxValue : count;
            pendingMode = Mode.Endpoint;
            BeginTargeting();
        }

        public static void BeginPreserved(SDS_StockKey key, int count)
        {
            if (key == null || !key.preserved)
            {
                return;
            }
            if (SDS_OutputCommand.IsInstallablePreservedKey(key))
            {
                BeginInstallPlacement(key);
                return;
            }
            pendingKey = null;
            pendingPreservedKey = key;
            pendingCount = count <= 0 ? int.MaxValue : count;
            ThingDef def = key.thingDef;
            pendingMode = def != null && typeof(Corpse).IsAssignableFrom(def.thingClass)
                ? Mode.Casket
                : Mode.Endpoint;
            BeginTargeting();
        }

        /// <summary>可安装缩装的唯一入口（W31）：关掉面板后选中原版安装放置器。</summary>
        public static void BeginInstallPlacement(SDS_StockKey key)
        {
            if (!SDS_OutputCommand.IsInstallablePreservedKey(key))
            {
                return;
            }
            pendingKey = null;
            pendingPreservedKey = null;
            pendingCount = 0;
            pendingMode = Mode.Endpoint;
            Find.WindowStack.TryRemove(typeof(Window_SDS_OutputDialog), doCloseSound: false);
            Find.WindowStack.TryRemove(typeof(Window_SuperSpace), doCloseSound: false);
            Find.DesignatorManager.Select(new Designator_SDS_Install(key));
        }

        private static void BeginTargeting()
        {
            Find.WindowStack.TryRemove(typeof(Window_SDS_OutputDialog), doCloseSound: false);
            Find.WindowStack.TryRemove(typeof(Window_SuperSpace), doCloseSound: false);

            bool casketMode = pendingMode == Mode.Casket;
            TargetingParameters targetParams = new TargetingParameters
            {
                canTargetLocations = false,
                // 只有「带芯片的小人」这一种端点时，玩家自己就是唯一端点：必须允许选自己。
                canTargetSelf = true,
                canTargetPawns = true,
                canTargetItems = false,
                canTargetFires = false,
                canTargetPlants = false,
                canTargetBuildings = true,
                mustBeSelectable = false,
                validator = delegate(TargetInfo t)
                {
                    if (SDS_OutputCommand.IsValidOutputTarget(t.Thing))
                    {
                        return true;
                    }
                    return casketMode && SDS_OutputCommand.IsValidCasketTarget(t.Thing);
                }
            };
            Find.Targeter.BeginTargeting(targetParams, OnTargetChosen);
        }

        private static void OnTargetChosen(LocalTargetInfo target)
        {
            SDS_StockKey key = pendingKey;
            SDS_StockKey preservedKey = pendingPreservedKey;
            int count = pendingCount;
            Mode mode = pendingMode;
            pendingKey = null;
            pendingPreservedKey = null;
            pendingCount = 0;
            pendingMode = Mode.Endpoint;

            Thing thing = target.Thing;
            if (thing == null)
            {
                return;
            }
            bool ok;
            string message;
            if (preservedKey != null)
            {
                ok = mode == Mode.Casket && SDS_OutputCommand.IsValidCasketTarget(thing)
                    ? SDS_OutputCommand.TryOutputPreservedToCasket(thing, preservedKey, count,
                        out message)
                    : SDS_OutputCommand.TryOutputPreservedToThing(thing, preservedKey, count,
                        out message);
            }
            else if (key != null)
            {
                ok = SDS_OutputCommand.TryOutputToThing(thing, key, count, out message);
            }
            else
            {
                return;
            }
            Messages.Message(message, ok ? MessageTypeDefOf.TaskCompletion
                : MessageTypeDefOf.RejectInput, false);
        }
    }

    /// <summary>
    /// 「使用」的地图点选（方案 B）：与输出一致的交互 —— 关掉本 Mod 窗口，进入原版瞄准，点谁就谁。
    /// targeting 阶段不做任何过滤，真正的判定只在 SDS_UseCommand.TryUse 一处。
    /// </summary>
    internal static class SDS_UseTargeting
    {
        private static SDS_StockKey pendingKey;
        private static bool pendingPreserved;

        public static void Begin(SDS_StockKey key, bool preserved)
        {
            if (key == null)
            {
                return;
            }
            pendingKey = key;
            pendingPreserved = preserved;
            Find.WindowStack.TryRemove(typeof(Window_SDS_OutputDialog), doCloseSound: false);
            Find.WindowStack.TryRemove(typeof(Window_SuperSpace), doCloseSound: false);

            TargetingParameters parameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetSelf = false,
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetItems = true,
                canTargetFires = false,
                canTargetPlants = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                mustBeSelectable = false,
                validator = delegate(TargetInfo t) { return true; }
            };
            Find.Targeter.BeginTargeting(parameters, OnTargetChosen);
        }

        private static void OnTargetChosen(LocalTargetInfo target)
        {
            SDS_StockKey key = pendingKey;
            bool preserved = pendingPreserved;
            pendingKey = null;
            pendingPreserved = false;
            if (key == null)
            {
                return;
            }
            SDS_UseCommand.HandleTarget(target, key, preserved);
        }
    }

    /// <summary>
    /// 可安装缩装的输出放置器（W31 / W32）：复用原版 Designator_Place 的预览 / 旋转 / 合法性着色。
    /// 确认落点时才摘 1 件真身；右键 / ESC 取消不取货。拖拽只取第一个合法格（防蓝图重复注册）。
    /// </summary>
    internal sealed class Designator_SDS_Install : Designator_Place
    {
        private readonly SDS_StockKey key;

        public Designator_SDS_Install(SDS_StockKey key)
        {
            this.key = key;
            icon = TexCommand.Install;
        }

        public override BuildableDef PlacingDef
        {
            get { return key != null ? key.thingDef : null; }
        }

        public override ThingStyleDef ThingStyleDefForPreview
        {
            get { return null; }
        }

        public override ThingDef StuffDef
        {
            get { return key != null ? key.stuff : null; }
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            return SDS_OutputCommand.CanPlaceInstallAt(key, Map, c, placingRot);
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            bool ok = SDS_OutputCommand.TryPlaceInstallFromStock(key, Map, c, placingRot,
                out string message);
            Messages.Message(message, ok ? MessageTypeDefOf.TaskCompletion
                : MessageTypeDefOf.RejectInput, false);
            if (ok)
            {
                Find.DesignatorManager.Deselect();
            }
        }

        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            if (cells == null)
            {
                return;
            }
            foreach (IntVec3 c in cells)
            {
                if (CanDesignateCell(c).Accepted)
                {
                    DesignateSingleCell(c);
                    Finalize(true);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 「只读 UI / 计数」口径补全：让清单产物计数与寒冷预警把超维库存算进去。
    /// 两个都是**只加不减**，可复用的原版判定全部复用（CountValidThing / 原版保暖分组算法）。
    /// </summary>
    internal static class SDS_ReadOnlyUI
    {
        private const int VersionTickThreshold = 60;

        private static bool cachedValid;
        private static SDS_ReadVersions cachedVersions;
        private static int cachedTick;
        private static List<SDS_StockEntry> cachedSnapshot;
        private static readonly List<Thing> cachedPreserved = new List<Thing>();

        /// <summary>新档 / 读档清空缓存（只解除引用，不持有旧账本与真身）。</summary>
        internal static void Reset()
        {
            cachedValid = false;
            cachedVersions = default(SDS_ReadVersions);
            cachedTick = 0;
            cachedSnapshot = null;
            cachedPreserved.Clear();
        }

        private static SDS_ReadVersions CaptureVersions()
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            SDS_ReadVersions versions = default(SDS_ReadVersions);
            versions.tick = GenTicks.TicksGame;
            versions.mirror = SDS_Host.MirrorQuery != null ? SDS_Host.MirrorQuery.Version : 0;
            versions.endpoint = SDS_Rules.EndpointVersion;
            versions.stock = ledger != null ? ledger.StockVersion : 0;
            versions.reservation = ledger != null ? ledger.ReservationVersion : 0;
            versions.filter = ledger != null ? ledger.FilterVersion : 0;
            return versions;
        }

        private static bool SameExposure(SDS_ReadVersions a, SDS_ReadVersions b)
        {
            return a.mirror == b.mirror && a.endpoint == b.endpoint && a.stock == b.stock
                && a.reservation == b.reservation && a.filter == b.filter;
        }

        /// <summary>清单产物里是否走原版 CountAsResource → ResourceCounter 早退分支（该分支已被注入，不能再加）。</summary>
        internal static bool UsesResourceCounter(Bill_Production bill, ThingDef def)
        {
            return def.CountAsResource
                && !bill.includeEquipped
                && (bill.includeTainted || !def.IsApparel || !def.apparel.careIfWornByCorpse)
                && bill.GetIncludeSlotGroup() == null
                && bill.hpRange.min == 0f && bill.hpRange.max == 1f
                && bill.qualityRange.min == QualityCategory.Awful
                && bill.qualityRange.max == QualityCategory.Legendary
                && !bill.limitToAllowedStuff;
        }

        /// <summary>超维空间里「符合该清单产物口径」的数量（复用原版 CountValidThing）。</summary>
        internal static long CountGridProducts(RecipeWorkerCounter worker, Bill_Production bill,
            ThingDef def)
        {
            if (worker == null || bill == null || def == null)
            {
                return 0L;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || !SDS_Rules.Available || !SDS_Exposure.MapExposesGrid(bill.Map))
            {
                return 0L;
            }
            EnsureSnapshot(ledger);
            long total = 0L;
            List<SDS_StockEntry> snapshot = cachedSnapshot;
            for (int i = 0; i < snapshot.Count; i++)
            {
                SDS_StockEntry entry = snapshot[i];
                if (entry == null || entry.key == null || entry.key.thingDef != def
                    || entry.count <= 0)
                {
                    continue;
                }
                bool valid;
                if (SDS_MirrorService.TryGetMirror(bill.Map, entry.key, out Thing mirror))
                {
                    valid = worker.CountValidThing(mirror, bill, def);
                }
                else
                {
                    // 复用缓存展示物：计数只读，绝不 Destroy。
                    Thing display = SDS_OutputUtility.CachedDisplayThing(entry.key);
                    valid = display != null && worker.CountValidThing(display, bill, def);
                }
                if (!valid)
                {
                    continue;
                }
                long units = entry.count + SDS_Exposure.PhysicalMirrorUnitOffset(bill.Map, entry.key, true);
                if (units > 0L)
                {
                    total += units;
                }
            }
            // 保真通道：真身也是地图上真摆着的实物，按原版 CountValidThing 逐件判定。
            for (int i = 0; i < cachedPreserved.Count; i++)
            {
                Thing thing = cachedPreserved[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                if (thing is MinifiedThing minified && minified.InnerThing != null
                    && minified.InnerThing.def == def)
                {
                    if (worker.CountValidThing(minified.InnerThing, bill, def))
                    {
                        total += (long)thing.stackCount * minified.InnerThing.stackCount;
                    }
                    continue;
                }
                if (thing.def != def)
                {
                    continue;
                }
                if (worker.CountValidThing(thing, bill, def))
                {
                    total += thing.stackCount;
                }
            }
            return total;
        }

        /// <summary>超维空间能提供的「温暖衣物套数」——复刻原版 FreeWarmClothesSetsCount 的分组算法。</summary>
        internal static int GridWarmClothesSetsCount(Alert_NeedWarmClothes alert, Map map)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || !SDS_Rules.Available || !SDS_Exposure.MapExposesGrid(map))
            {
                return 0;
            }
            EnsureSnapshot(ledger);
            List<float> jackets = new List<float>();
            List<float> shirts = new List<float>();
            List<float> pants = new List<float>();
            List<SDS_StockEntry> snapshot = cachedSnapshot;
            for (int i = 0; i < snapshot.Count; i++)
            {
                SDS_StockEntry entry = snapshot[i];
                ThingDef def = entry != null && entry.key != null ? entry.key.thingDef : null;
                if (def == null || !def.IsApparel || entry.count <= 0)
                {
                    continue;
                }
                bool mirrorInStorage = SDS_MirrorService.TryGetMirror(map, entry.key, out Thing mirror)
                    && mirror.IsInAnyStorage();
                Thing display = SDS_OutputUtility.CachedDisplayThing(entry.key);
                if (display == null)
                {
                    continue;
                }
                float cold = display.GetStatValue(StatDefOf.Insulation_Cold, true, 120);
                bool torso = def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Torso);
                bool legs = def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Legs);
                bool onSkin = def.apparel.layers.Contains(ApparelLayerDefOf.OnSkin);
                if (cold <= 0f || (!torso && !legs))
                {
                    continue;
                }
                long n = entry.count + SDS_Exposure.PhysicalMirrorUnitOffset(map, entry.key,
                    mirrorInStorage);
                if (n <= 0L)
                {
                    continue;
                }
                for (long k = 0; k < n; k++)
                {
                    AddWarmPiece(jackets, shirts, pants, torso, legs, onSkin, cold);
                }
            }
            // 保真通道：受保护衣物的真身同样是地图上的实物，按真身自己的保暖值计入。
            for (int i = 0; i < cachedPreserved.Count; i++)
            {
                if (!(cachedPreserved[i] is Apparel apparel) || apparel.Destroyed
                    || apparel.def == null)
                {
                    continue;
                }
                float cold = apparel.GetStatValue(StatDefOf.Insulation_Cold, true, 120);
                if (cold <= 0f)
                {
                    continue;
                }
                bool torso = apparel.def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Torso);
                bool legs = apparel.def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Legs);
                if (!torso && !legs)
                {
                    continue;
                }
                bool onSkin = apparel.def.apparel.layers.Contains(ApparelLayerDefOf.OnSkin);
                long n = apparel.stackCount;
                for (long k = 0; k < n; k++)
                {
                    AddWarmPiece(jackets, shirts, pants, torso, legs, onSkin, cold);
                }
            }
            return WarmSets(jackets, shirts, pants, alert);
        }

        private static void AddWarmPiece(List<float> jackets, List<float> shirts, List<float> pants,
            bool torso, bool legs, bool onSkin, float cold)
        {
            if (torso)
            {
                (onSkin ? shirts : jackets).Add(cold);
            }
            if (legs)
            {
                pants.Add(cold);
            }
        }

        private static int WarmSets(List<float> jackets, List<float> shirts, List<float> pants,
            Alert_NeedWarmClothes alert)
        {
            if (jackets.Count == 0 && shirts.Count == 0 && pants.Count == 0)
            {
                return 0;
            }
            // 原版按 PriorityQueue 出队 = 保暖值从高到低；这里手动降序排序等效。
            jackets.Sort((a, b) => b.CompareTo(a));
            shirts.Sort((a, b) => b.CompareTo(a));
            pants.Sort((a, b) => b.CompareTo(a));

            float lowest = 0f;
            if (alert != null)
            {
                lowest = Traverse.Create(alert).Field("lowestTemperatureComing").GetValue<float>();
            }
            float need = ThingDefOf.Human.GetStatValueAbstract(StatDefOf.ComfyTemperatureMin) - lowest;
            if (need <= 0f)
            {
                return GenMath.Max(jackets.Count, shirts.Count, pants.Count);
            }
            int sets = 0;
            int ji = 0, si = 0, pi = 0;
            while (ji < jackets.Count || si < shirts.Count || pi < pants.Count)
            {
                float sum = 0f;
                if (ji < jackets.Count)
                {
                    sum += jackets[ji++];
                }
                if (si < shirts.Count && sum < need)
                {
                    sum += shirts[si++];
                }
                if (pi < pants.Count && sum < need)
                {
                    sum += pants[pi++];
                }
                if (sum < need)
                {
                    break;
                }
                sets++;
            }
            return sets;
        }

        /// <summary>按版本缓存账本快照（60 tick 兜底刷新，覆盖版本未推进的极小概率）。</summary>
        private static void EnsureSnapshot(ISDS_LedgerRead ledger)
        {
            SDS_ReadVersions versions = CaptureVersions();
            int now = GenTicks.TicksGame;
            if (cachedValid && cachedSnapshot != null
                && SameExposure(versions, cachedVersions)
                && now - cachedTick < VersionTickThreshold)
            {
                return;
            }
            cachedValid = true;
            cachedVersions = versions;
            cachedTick = now;
            cachedSnapshot = ledger.Snapshot(0);
            cachedPreserved.Clear();
            ledger.AppendPreservedThings(cachedPreserved);
        }
    }

    /// <summary>
    /// RecipeWorkerCounter.CountProducts：把超维空间里符合该清单产物口径的数量加进去。
    /// 该值同时用于 UI 显示与 "做到 X 为止"，因此是口径补全而非纯 UI 修正。
    /// </summary>
    [HarmonyPatch(typeof(RecipeWorkerCounter), "CountProducts")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_RecipeCounterCountProducts
    {
        public static void Postfix(RecipeWorkerCounter __instance, Bill_Production bill,
            ref int __result)
        {
            try
            {
                if (bill == null || bill.Map == null || bill.recipe == null)
                {
                    return;
                }
                List<ThingDefCountClass> products = bill.recipe.products;
                if (products.NullOrEmpty() || products[0] == null
                    || products[0].thingDef == null)
                {
                    return;
                }
                ThingDef def = products[0].thingDef;
                if (SDS_ReadOnlyUI.UsesResourceCounter(bill, def))
                {
                    return;
                }
                if (bill.GetIncludeSlotGroup() != null)
                {
                    return;
                }
                long add = SDS_ReadOnlyUI.CountGridProducts(__instance, bill, def);
                if (add > 0)
                {
                    __result = (int)Math.Min((long)__result + add, int.MaxValue);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("清单产物计数注入超维空间失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>Alert_NeedWarmClothes.FreeWarmClothesSetsCount：把超维空间里的温暖衣物套数加进寒冷预警。</summary>
    [HarmonyPatch(typeof(Alert_NeedWarmClothes), "FreeWarmClothesSetsCount")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_AlertNeedWarmClothes
    {
        public static void Postfix(Alert_NeedWarmClothes __instance, Map map, ref int __result)
        {
            try
            {
                if (map == null)
                {
                    return;
                }
                int add = SDS_ReadOnlyUI.GridWarmClothesSetsCount(__instance, map);
                if (add > 0)
                {
                    __result += add;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("寒冷预警注入超维空间失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>
    /// 「当前工作」文案的唯一改写实现：这趟 Job 的实际去向是超维空间时，
    /// 统一显示「正在把 X 存入超维空间。」纯显示层，异常一律静默。
    /// </summary>
    internal static class SDS_JobReportText
    {
        public static void Rewrite(JobDriver driver, ref string result)
        {
            try
            {
                Job job = driver != null ? driver.job : null;
                if (job == null || job.def == null)
                {
                    return;
                }
                if (job.def != SDS_JobDefOf.DepositToGrid
                    && !(job.targetB.Thing is ISDS_StorageEndpoint))
                {
                    return;
                }
                // 驱动可能已写动态文案（例如"背包已满，剩余留在地面"），不要覆盖。
                if (!string.IsNullOrEmpty(job.reportStringOverride))
                {
                    return;
                }
                Thing target = job.GetTarget(TargetIndex.A).Thing;
                if (target == null && job.targetQueueA != null)
                {
                    target = job.targetQueueA.FirstValid().Thing;
                }
                string label = target != null && !target.Destroyed ? target.LabelShort : string.Empty;
                result = "SDS.JobReport.Deposit".Translate(label).ToString();
            }
            catch (Exception)
            {
                // 纯显示层：失败保留原版文案。
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.GetReport))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_JobReport
    {
        public static void Postfix(JobDriver __instance, ref string __result)
        {
            SDS_JobReportText.Rewrite(__instance, ref __result);
        }
    }

    /// <summary>JobDriver_HaulToContainer.GetReport 是覆写（不调基类），单独补一次。</summary>
    [HarmonyPatch(typeof(JobDriver_HaulToContainer), nameof(JobDriver_HaulToContainer.GetReport))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_JobReportHaulToContainer
    {
        public static void Postfix(JobDriver_HaulToContainer __instance, ref string __result)
        {
            SDS_JobReportText.Rewrite(__instance, ref __result);
        }
    }

    /// <summary>右下角入口：在 PlaySettings 那一排里加一个带文字的「超维」按钮。</summary>
    [HarmonyPatch(typeof(PlaySettings), "DoPlaySettingsGlobalControls")]
    internal static class Patch_SDS_PlaySettingsButton
    {
        private static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || row == null)
            {
                return;
            }
            GameFont prevFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                string text = "SDS.Button.Label".Translate().ToString();
                Rect rect = row.ButtonRect(text, 24f);
                rect.height = 22f;
                TooltipHandler.TipRegion(rect,
                    "SDS.Window.TitleTip".Translate().ToString() + "\n"
                    + SDS_Hotkeys.HotkeyHintText());
                if (Widgets.ButtonText(rect, text.Truncate(rect.width - 4f)))
                {
                    SDS_Hotkeys.ToggleSidebar();
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("PlaySettings 超维按钮绘制失败（已忽略）", e);
            }
            finally
            {
                Text.Font = prevFont;
            }
        }
    }
}
