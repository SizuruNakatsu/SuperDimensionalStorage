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
    // 来源：Combat Extended（CE）
    // ============================================================================
    // ════════════════════════════════════════════════════════════════════════════════════════
    // 【第三方 Mod 兼容补丁】Combat Extended（CE）专用
    //
    // 本文件全部内容**只**用于与「Combat Extended」共存时的兼容，**不参与、不改变**本 Mod 自身的
    // 任何功能（存入筛选 / 品类容量 / 取出 / 手动存入 / 端点就近竞争 / 建造·炮塔·运输舱供给 /
    // 财富 / 交易 …… 全部照旧）。
    // 没有安装 CE 时，TryApply 里的 AccessTools.TypeByName 全部返回 null → 整块跳过，
    // 连一次反射挂点都不会产生，对未装 CE 的用户完全无感。
    //
    // ── 设计原则：把 CE 的调用链当成「原版」来改 ─────────────────────────────────────────────
    // 我们改原版从来不是"让原版查询返回虚拟物品"，而是**"原版说没有 → 我们把它变成真的
    // （物化到地面 / 塞进小人库存）→ 再交回原版自己走完流程"**（见 JobDriver_SDS_WithdrawFromGrid
    // 与 Patch_SDS_TurretSupply 的 toil 包装）。对 CE 是同一件事：
    //
    //   * 钩点选在 **CE 自己的决策出口 / 派活出口**，不碰它的 JobDef、司机、toil 与表现；
    //   * 只补"物品来源"这一层：CE 在地图上找不到弹药 → 我们从网格补上真货；
    //     交回 CE 自己的 Job 构造器（JobGiverUtils_Reload.MakeReloadJob / CompAmmoUser.TryMakeReloadJob），
    //     于是"走到弹药 → 拿在手上 → 走到炮塔 → 装填"这套表现完全由 CE 自己执行；
    //   * CE 在地图上找得到实物时**一律不插手**（原版/CE 优先，天然满足定稿就近原则）。
    //
    // ── 落地的挂点（均已反编译核实签名）────────────────────────────────────────────────────
    //  ① 炮塔换弹   WorkGiver_ReloadTurret.HasJobOnThing + JobOnThing（成对）
    //  ② 自动装弹器 WorkGiver_ReloadAutoLoader.HasJobOnThing + JobOnThing（成对）
    //  ③ 派人操作炮塔 JobGiver_ManTurretsNearSelfCE / NearPointCE 的私有静态 FindAmmoForTurret
    //  ④ 手持武器自动换弹 JobGiver_CheckReload.GetPriority + TryGiveJob
    //  ⑤ 战斗/紧急捡弹 CompAmmoUser.TryPickupAmmo
    //  ⑨ 弹药搜索收口 JobGiverUtils_Reload.FindBestAmmo(Pawn, AmmoDef)（镜像过滤，见下）
    //
    // ── 为什么必须有 ⑨（2026-10-19 倒查修正）──────────────────────────────────────────────
    // CE 的 ① ② ③ ⑦ 与两个 JobDriver 最终都经 `FindBestAmmo(Pawn, AmmoDef)` 去地图上找弹药，
    // 而它用的是 `GenClosest.ClosestThingReachable` —— 会被我们的镜像距离适配接管，把隐藏金库里的
    // 镜像当成"最近的弹药"。但 CE 的司机是 `GotoCell(ammo.Position)` + `LoadAmmo(job.targetB)`，
    // 不走本 Mod 的取货原语：镜像既走不到，也不会扣账，最后表现为"炮塔装填成功、账本却没少"。
    // 因此在这一处把镜像换成真实的最近同类弹药（找不到真货才返回 null，交回前置补货腿）。
    //
    // ── 为什么 ①② 必须成对补 ──────────────────────────────────────────────────────────────
    // CE 的 CanReload 里 `FindBestAmmo(pawn, turret) == null` 会让 HasJobOnThing 直接返回 false，
    // 于是 JobOnThing **根本不会被调用**（与 PUAH 那次的坑完全一致）。
    // 为什么**不**直接补 FindBestAmmo：CanReload（纯判定）也调它，在判定阶段物化会污染候选枚举。
    //
    // ── 为什么 ④ 必须连 GetPriority 一起补 ────────────────────────────────────────────────
    // CE 的 JobGiver_CheckReload.GetPriority 在"库存里弹药不够"时返回 0 → ThinkNode 直接跳过，
    // TryGiveJob 压根不会被调用。不补 GetPriority 的话 ④ 的 TryGiveJob 补丁永远不生效。
    //
    // ── 为什么不再补 ⑥ CompInventory.AmmoCountOfDef（2026-10-19 定稿）─────────────────────
    // 它是个**纯查询**，却被武器面板的 MagsLeft / MagsLeftReadOnly / MagsLeftSameTypeReadOnly 调用；
    // 一旦在查询里物化，就意味着"选中带芯片小人看一眼武器"也会从网格拉一匣弹进背包，违背物理等效。
    // 而换弹链本身并不需要它：征召中的换弹由 CE 自己的 Available → TryStartReload → TryOutOfAmmoAction
    // → TryPickupAmmo（⑤）与 ⑧ 覆盖；未征召由 ④ 覆盖。
    // ════════════════════════════════════════════════════════════════════════════════════════

    internal static class SDS_ModCompat_CE
    {
        // ── 运行期解析到的 CE 类型 / 成员（全部为 null 表示未装 CE）────────────────────────
        private static Type tCompAmmoUser;
        private static Type tCompInventory;
        private static Type tAmmoDef;
        private static Type tJobGiverUtilsReload;
        private static Type tBuildingAutoloaderCe;
        private static Type tTurretRegistry;
        private static MethodInfo mGetAmmo;
        private static MethodInfo mGetReloadable;
        private static MethodInfo mGetReloading;
        private static MethodInfo mFindBestAmmoTurret;
        private static MethodInfo mFindBestAmmoAutoloader;
        private static Type tCeJobDefOf;
        private static FieldInfo fReloadTurretDef;
        private static FieldInfo fReloadAutoLoaderDef;
        private static FieldInfo fReloadWeaponDef;

        /// <summary>Pawn_JobTracker.pawn 在该 repack 里是 protected，Postfix 里用反射取。</summary>
        private static readonly FieldInfo fJobTrackerPawn =
            AccessTools.Field(typeof(Pawn_JobTracker), "pawn");

        // ①② 的「前置补货」与「后置兜底」是同一次方法调用里的一对，用这两个静态槽把补出来的那件真货
        // 直接传给兜底腿（不依赖 CE 自己的 FindBestAmmo 再搜一遍地图）。
        private static Thing pendingAmmo;
        private static Thing pendingAmmoTarget;
        private static ThingDef pendingAmmoDef;

        private static readonly Dictionary<int, int> checkCooldownUntil = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> priorityCacheUntil = new Dictionary<int, int>();
        private static readonly Dictionary<int, bool> priorityCacheValue = new Dictionary<int, bool>();
        private const int CheckCooldownTicks = 250;
        private const int PriorityCacheTicks = 60;
        private const int NegativeCacheTicks = 10;

        /// <summary>
        /// E1：新档 / 读档清空按 <c>thingIDNumber</c> 的短时缓存。
        /// 这些条目存的是**绝对 tick**，同进程换档时 <c>TicksGame</c> 会回退，
        /// 旧档留下的 <c>until</c> 可能远超新档当前 tick → 同一 ID 的小人会被长期误判为"冷却中"。
        /// </summary>
        internal static void Reset()
        {
            checkCooldownUntil.Clear();
            priorityCacheUntil.Clear();
            priorityCacheValue.Clear();
        }

        /// <summary>由 SDS_Mod 在逐个补丁类应用完成后调用（没有 CE 时什么都不做）。</summary>
        public static void TryApply(Harmony harmony)
        {
            bool detected = false;
            try
            {
                if (harmony == null)
                {
                    return;
                }
                if (!Resolve())
                {
                    detected = tCompAmmoUser != null || tCompInventory != null || tAmmoDef != null
                        || tJobGiverUtilsReload != null;
                    string reason = detected
                        ? "CE 关键类型解析不完整：CompAmmoUser=" + (tCompAmmoUser != null)
                            + "，CompInventory=" + (tCompInventory != null)
                            + "，AmmoDef=" + (tAmmoDef != null)
                            + "，JobGiverUtils_Reload=" + (tJobGiverUtilsReload != null)
                        : null;
                    SDS_CompatReportHub.Register("Combat Extended", "CETeam.CombatExtended", detected, null,
                        null, reason);
                    return;
                }
                detected = true;
                SDS_CompatPatchReport report = new SDS_CompatPatchReport("Combat Extended");
                Type[] hasJobSig = { typeof(Pawn), typeof(Thing), typeof(bool) };
                Type[] findAmmoSig = { typeof(Pawn), typeof(Thing) };

                Type wgTurret = AccessTools.TypeByName("CombatExtended.WorkGiver_ReloadTurret");
                Patch(harmony, report, "WorkGiver_ReloadTurret", wgTurret, "HasJobOnThing", hasJobSig,
                    null, nameof(ReloadTurretHasJobPostfix));
                Patch(harmony, report, "WorkGiver_ReloadTurret", wgTurret, "JobOnThing", hasJobSig,
                    nameof(ReloadTurretJobPrefix), nameof(ReloadTurretJobPostfix));

                Type wgAuto = AccessTools.TypeByName("CombatExtended.WorkGiver_ReloadAutoLoader");
                Patch(harmony, report, "WorkGiver_ReloadAutoLoader", wgAuto, "HasJobOnThing", hasJobSig,
                    null, nameof(ReloadAutoLoaderHasJobPostfix));
                Patch(harmony, report, "WorkGiver_ReloadAutoLoader", wgAuto, "JobOnThing", hasJobSig,
                    nameof(ReloadAutoLoaderJobPrefix), nameof(ReloadAutoLoaderJobPostfix));

                Type manSelf = AccessTools.TypeByName("CombatExtended.JobGiver_ManTurretsNearSelfCE");
                Patch(harmony, report, "JobGiver_ManTurretsNearSelfCE", manSelf, "FindAmmoForTurret", findAmmoSig,
                    null, nameof(ManTurretsFindAmmoPostfix));
                Type manPoint = AccessTools.TypeByName("CombatExtended.JobGiver_ManTurretsNearPointCE");
                Patch(harmony, report, "JobGiver_ManTurretsNearPointCE", manPoint, "FindAmmoForTurret", findAmmoSig,
                    null, nameof(ManTurretsFindAmmoPostfix));

                Type checkReload = AccessTools.TypeByName("CombatExtended.JobGiver_CheckReload");
                Patch(harmony, report, "JobGiver_CheckReload", checkReload, "GetPriority", new[] { typeof(Pawn) },
                    null, nameof(CheckReloadPriorityPostfix));
                // 征兆空匣：Prefix 在 CE 的 DoReloadCheck 之前把弹补进背包 / 取物点，
                // 让 CE 原体自己走它自己的 ReloadWeapon Job；Postfix 继续兜住未征召路径。
                Patch(harmony, report, "JobGiver_CheckReload", checkReload, "TryGiveJob", new[] { typeof(Pawn) },
                    nameof(CheckReloadJobPrefix), nameof(CheckReloadJobPostfix));
                // 征兆"攻击中打空"：AttackStatic 是持续 Job，think tree 不会在 Job 运行期间重跑；
                // CE 自己的 TryStartReload 在攻击链里可能被战术驱动 / 姿态 / 同 tick 保护挡掉。
                // 这里在 JobTrackerTick 之后补一次：空匣 + 有弹 → 用 CE 自己的 ReloadWeapon Job 接管。
                Patch(harmony, report, "Pawn_JobTracker", typeof(Pawn_JobTracker), "JobTrackerTick", Type.EmptyTypes,
                    null, nameof(JobTrackerTickPostfix));

                // ⑦ 防御职责下的"自动给炮塔补弹"（CE 在 DutyDef "Defend" 里挂的 think giver）
                Type defenderReload = AccessTools.TypeByName("CombatExtended.JobGiver_DefenderReloadTurret");
                Patch(harmony, report, "JobGiver_DefenderReloadTurret", defenderReload, "TryGiveJob",
                    new[] { typeof(Pawn) }, nameof(DefenderReloadPrefix), null);

                Patch(harmony, report, "CompAmmoUser", tCompAmmoUser, "TryPickupAmmo", Type.EmptyTypes,
                    null, nameof(TryPickupAmmoPostfix));
                // ⑧ 战斗中"打空即换弹"：CE 每次尝试开火都会调到的上层决策点
                Patch(harmony, report, "CompAmmoUser", tCompAmmoUser, "TryPrepareShot", Type.EmptyTypes,
                    nameof(TryPrepareShotPrefix), null);

                // ⑨ 弹药搜索收口：镜像绝不能成为 CE 的 Job 目标（会复制物品）。
                Patch(harmony, report, "JobGiverUtils_Reload", tJobGiverUtilsReload, "FindBestAmmo",
                    new[] { typeof(Pawn), tAmmoDef }, null, nameof(FindBestAmmoMirrorFilterPostfix));
                // ⑩ 驱动兜底：万一 targetB 仍是镜像（其它 Mod 直构 / 版本差异），宁可不装填也不复制物品。
                Type drvTurret = AccessTools.TypeByName("CombatExtended.JobDriver_ReloadTurret");
                Patch(harmony, report, "JobDriver_ReloadTurret", drvTurret, "TryMakePreToilReservations",
                    new[] { typeof(bool) }, nameof(ReloadDriverMirrorGuardPrefix), null);
                Type drvAutoLoader = AccessTools.TypeByName("CombatExtended.JobDriver_ReloadAutoLoader");
                Patch(harmony, report, "JobDriver_ReloadAutoLoader", drvAutoLoader, "TryMakePreToilReservations",
                    new[] { typeof(bool) }, nameof(ReloadDriverMirrorGuardPrefix), null);

                SDS_CompatReportHub.Register("Combat Extended", "CETeam.CombatExtended", true, report,
                    "只影响 CE 自己的换弹 / 炮塔 / 拾取链路，不改变 CE 的任何表现");
            }
            catch (Exception e)
            {
                SDS_CompatReportHub.Register("Combat Extended", "CETeam.CombatExtended", detected, null,
                    null, "应用异常 " + e.GetType().Name);
                SDS_Diag.ErrorOnce("Combat Extended 兼容补丁应用失败（已跳过，双方原行为不受影响）", e);
            }
        }

        /// <summary>解析所有需要的 CE 类型与成员；任何一个关键项缺失都视为"没装 CE"。</summary>
        private static bool Resolve()
        {
            tCompAmmoUser = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
            tCompInventory = AccessTools.TypeByName("CombatExtended.CompInventory");
            tAmmoDef = AccessTools.TypeByName("CombatExtended.AmmoDef");
            tJobGiverUtilsReload = AccessTools.TypeByName("CombatExtended.CombatExtended.Jobs.Utils.JobGiverUtils_Reload");
            tBuildingAutoloaderCe = AccessTools.TypeByName("CombatExtended.Building_AutoloaderCE");
            tTurretRegistry = AccessTools.TypeByName("CombatExtended.Compatibility.TurretRegistry");
            if (tCompAmmoUser == null || tCompInventory == null || tAmmoDef == null || tJobGiverUtilsReload == null)
            {
                // 完全找不到 CE → 汇总行「未检测到」；能找到一部分 → 汇总行「已检测到，0/0（原因）」。
                return false;
            }
            if (tTurretRegistry != null)
            {
                mGetAmmo = AccessTools.Method(tTurretRegistry, "GetAmmo", new[] { typeof(Building_Turret) });
                // CE 自己 CanReload / HasJobOnThing 的两个闸门：
                //  GetReloadable = GetAmmo()?.HasMagazine（不是可换弹的炮塔 CE 根本不会派活）
                //  GetReloading = 该炮塔正在装填（此时 CE 明确拒绝第二个 Job）
                mGetReloadable = AccessTools.Method(tTurretRegistry, "GetReloadable", new[] { typeof(Building_Turret) });
                mGetReloading = AccessTools.Method(tTurretRegistry, "GetReloading", new[] { typeof(Building_Turret) });
            }
            if (tBuildingAutoloaderCe != null)
            {
                mFindBestAmmoAutoloader = AccessTools.Method(tJobGiverUtilsReload, "FindBestAmmo",
                    new[] { typeof(Pawn), tBuildingAutoloaderCe });
            }
            // CE 自己"去地图上找弹药"的入口（用来判断"CE 到底找不找得到"，找得到我们一律不插手）。
            mFindBestAmmoTurret = AccessTools.Method(tJobGiverUtilsReload, "FindBestAmmo",
                new[] { typeof(Pawn), typeof(Building_Turret) });
            // CE_JobDefOf 的 [DefOf] 字段要到 Def 全部加载完才有值 → 这里只解析 FieldInfo，
            // 取值延迟到运行时（补丁执行时）再读。
            tCeJobDefOf = AccessTools.TypeByName("CombatExtended.CE_JobDefOf");
            if (tCeJobDefOf != null)
            {
                fReloadTurretDef = AccessTools.Field(tCeJobDefOf, "ReloadTurret");
                fReloadAutoLoaderDef = AccessTools.Field(tCeJobDefOf, "ReloadAutoLoader");
                // 征兆空匣：用来识别"当前已经在 CE 换弹 Job 里"，避免同一轮重复补货。
                fReloadWeaponDef = AccessTools.Field(tCeJobDefOf, "ReloadWeapon");
            }
            return true;
        }

        /// <summary>
        /// 解析 CE 目标方法后交给统一 <see cref="SDS_CompatTools.TryPatch"/>
        /// （应用 / 报告 / 异常 / 优先级只有这一份实现；CE 挂点统一使用默认 <c>Priority.Low</c>，
        /// 让 CE 原体与其它适配器先执行，我们只在它们放弃后兜底）。
        /// 目标类型 / 方法找不到、应用异常一律算“失效”，绝不静默。
        /// </summary>
        private static void Patch(Harmony harmony, SDS_CompatPatchReport report, string targetName,
            Type target, string methodName, Type[] signature, string prefixName, string postfixName)
        {
            string hook = targetName + "." + methodName;
            if (target == null)
            {
                report.Missing(hook, "找不到类型 " + targetName + "（版本差异？）");
                return;
            }
            MethodInfo targetMethod = signature != null && signature.Length > 0
                ? AccessTools.Method(target, methodName, signature)
                : AccessTools.Method(target, methodName, Type.EmptyTypes);
            SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_CE), hook, targetMethod,
                prefixName, postfixName, "找不到方法（签名变化？）");
        }

        // ═════════════════════════════════════════════════════════════════════════=========
        //  ① 炮塔换弹：HasJobOnThing（放行候选）+ JobOnThing（补货后交回 CE 造 Job）
        // ═════════════════════════════════════════════════════════════════════════════════
        public static void ReloadTurretHasJobPostfix(Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            try
            {
                if (__result || !(t is Building_Turret))
                {
                    return;
                }
                if (!CanSupplyAmmoFor(pawn, t, forced) || !CanReserveTurret(pawn, t))
                {
                    return;
                }
                // CE 的非弹药部分条件（可预留 / 未禁 / 未着火 / 未在装填）已复核通过，且我们能供弹 →
                // 放行候选；真正派活时由 JobOnThing 补货并交回 CE 自己的 MakeReloadJob。
                __result = true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（炮塔 HasJobOnThing）失败（已忽略，CE 行为不受影响）", e);
            }
        }

        /// <summary>
        /// ① 炮塔：CE 原体执行**之前**把弹药补上（只在 CE 自己在图上确实找不到弹药时动手）。
        ///
        /// ★ 为什么要前置（2026-10-18 实测修正）：CE 的 JobOnThing 原体自己会调
        ///   JobGiverUtils_Reload.MakeReloadJob → 内部再调 FindBestAmmo 搜地图。若我们等原体跑完
        ///   才在 Postfix 里补货，那么原体那次一定"找不到弹"→ 记一条
        ///   "tried to create a reload job without ammo. This should have been checked earlier." 的错误，
        ///   并且我们只能经反射再补跑一遍，时序与职责都错位。
        ///   改成前置补货后：CE 原体自己那一次 FindBestAmmo 就命中我们刚放下的真货 → Job 由 **CE 自己**
        ///   原封不动地造出来（无错误日志、无反射调用、表现与 CE 原生完全一致）。
        /// </summary>
        public static bool ReloadTurretJobPrefix(Pawn pawn, Thing t, bool forced)
        {
            return ReloadJobPrefix(pawn, t, false);
        }

        /// <summary>① 炮塔：兜底 —— 前置补货后 CE 原体仍没造出 Job（极端边界）时按 CE 口径直构一个。</summary>
        public static void ReloadTurretJobPostfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            ReloadJobPostfix(pawn, t, false, ref __result);
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        //  ② 自动装弹器：同上成对
        // ═════════════════════════════════════════════════════════════════════════════════
        public static void ReloadAutoLoaderHasJobPostfix(Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            try
            {
                if (__result || tBuildingAutoloaderCe == null || !tBuildingAutoloaderCe.IsInstanceOfType(t))
                {
                    return;
                }
                if (!CanSupplyAmmoFor(pawn, t, forced) || !CanReserveTurret(pawn, t))
                {
                    return;
                }
                __result = true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（装弹器 HasJobOnThing）失败（已忽略，CE 行为不受影响）", e);
            }
        }

        /// <summary>② 自动装弹器：与 ① 完全同构（前置补货 → 放行 CE 原体）。</summary>
        public static bool ReloadAutoLoaderJobPrefix(Pawn pawn, Thing t, bool forced)
        {
            return ReloadJobPrefix(pawn, t, true);
        }

        /// <summary>② 自动装弹器：兜底直构。</summary>
        public static void ReloadAutoLoaderJobPostfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            ReloadJobPostfix(pawn, t, true, ref __result);
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        //  ③ 派人操作炮塔：CE 的两个 think giver 各自"去地图上找弹药"的私有静态方法
        // ═════════════════════════════════════════════════════════════════════════════════
        public static void ManTurretsFindAmmoPostfix(Pawn pawn, Thing turret, ref Thing __result)
        {
            try
            {
                if (__result != null || pawn == null || turret == null || pawn.Map == null)
                {
                    return;
                }
                // 只补玩家阵营：这个 think giver 的校验器会被任何拥有该职责的小人调用，
                // 敌方 / 第三方小人绝不能通过它把玩家网格里的弹药抽到地图上。
                if (pawn.Faction != Faction.OfPlayer || !GridUsable(pawn.Map))
                {
                    return;
                }
                ThingDef def;
                int need;
                if (!NeedAmmoFor(turret, out def, out need))
                {
                    return;
                }
                Thing placed = MaterializeToMap(def, need, turret.Map, turret.Position);
                if (placed != null)
                {
                    __result = placed;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（派人操作炮塔取弹）失败（已忽略，CE 行为不受影响）", e);
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        //  ⑦ 防御职责下的"自动给炮塔补弹"：CE 在 DutyDef "Defend" 里挂了 JobGiver_DefenderReloadTurret
        //
        //  它的判定链是 `ShouldReload()` + `JobGiverUtils_Reload.CanReload`，而 CanReload 最后一条要求
        //  "地图上找得到弹药"（FindBestAmmo != null）→ 网格里的弹它看不见，整条链直接被掐断
        //  （这条链**不经过**我们 ①② 补的 WorkGiver，所以必须单独补）。
        //  本 Prefix 只做一件事：把弹药来源补上，然后**放行 CE 原体**，由它自己按自己的口径造 Job
        //  —— 与 ①② 完全同思想；因为补货发生在原体之前，原体那一次 FindBestAmmo 就能命中。
        // ═════════════════════════════════════════════════════════════════════════════════
        public static bool DefenderReloadPrefix(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.Map == null || pawn.Faction != Faction.OfPlayer)
                {
                    return true;
                }
                Thing turret = FindCeTurretNeedingReload(pawn);
                if (turret == null)
                {
                    return true;   // 没有"缺弹且网格供得上"的炮塔（或 CE 自己就能处理）→ 完全不插手
                }
                ThingDef def;
                int need;
                if (!NeedAmmoFor(turret, out def, out need))
                {
                    return true;
                }
                MaterializeToMap(def, need, pawn.Map, turret.Position);
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（防御职责炮塔补弹）失败（已忽略，CE 行为不受影响）", e);
                return true;
            }
        }

        /// <summary>
        /// 与 CE 的 `JobGiver_DefenderReloadTurret` 同口径找一个"缺弹、且网格供得上"的 CE 炮塔：
        /// 100 格内、可预留、可到达、`ShouldReload()`（CE 自己的阈值：弹量低于半匣或弹种不符）、
        /// 且网格供得上（用原版 API 枚举建筑，不去碰 CE 私有的 TurretTracker 细节）。
        /// </summary>
        private static Thing FindCeTurretNeedingReload(Pawn pawn)
        {
            List<Thing> things = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            Thing best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (!(t is Building_Turret) || t.Destroyed || !t.Spawned)
                {
                    continue;
                }
                float dist = t.Position.DistanceTo(pawn.Position);
                if (dist > 100f || dist >= bestDist)
                {
                    continue;
                }
                if (!pawn.CanReserve(t) || !pawn.CanReach(t, PathEndMode.ClosestTouch, Danger.Deadly))
                {
                    continue;
                }
                // 与 CE 的 CanReload(pawn, turret, forced:false, emergency:true) 的**非弹药闸门**逐条对齐：
                // 只要有一项不满足，CE 就不会派活；此时若我们还物化弹药，就会每轮 think 抽一批到地上。
                if (ForbidUtility.IsForbidden(t, pawn) || InvokeStaticBool(mGetReloading, t))
                {
                    continue;
                }
                if (pawn.Faction != null && t.Faction != pawn.Faction)
                {
                    Faction faction = t.Faction;
                    if (faction == null || faction.RelationKindWith(pawn.Faction) != FactionRelationKind.Ally)
                    {
                        continue;
                    }
                }
                if (pawn.health == null || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                {
                    continue;
                }
                // ★ 必须与 CE 的 `turret.ShouldReload()`（threshold = 0.5，ensureAmmoType = true）同口径：
                //   否则"只缺一点弹、CE 还不想换"的炮塔会被我们每轮 think 反复物化一批弹药到地上
                //   （CE 不派活 → 没人消耗 → 下一轮又物化一批）。
                if (!ShouldReloadTarget(t))
                {
                    continue;
                }
                ThingDef def;
                int need;
                if (!NeedAmmoFor(t, out def, out need))
                {
                    continue;
                }
                best = t;
                bestDist = dist;
            }
            return best;
        }

        /// <summary>
        /// 与 `TurretRegistry.ShouldReload(this Building_Turret turret, 0.5f, true)` 逐条同口径。
        /// 反射读 CE 的公开成员，避免依赖 CE 内部实现细节。
        /// </summary>
        private static bool ShouldReloadTarget(Thing turret)
        {
            object comp = AmmoCompOf(turret);
            if (comp == null)
            {
                return false;
            }
            bool hasMagazine = BoolMember(comp, "HasMagazine");
            int cur = IntMember(comp, "CurMagCount");
            int size = IntMember(comp, "MagSize");
            ThingDef current = GetMember(comp, "CurrentAmmo") as ThingDef;
            ThingDef selected = GetMember(comp, "SelectedAmmo") as ThingDef;
            if (hasMagazine && size > 0 && cur <= size * 0.5f)
            {
                return true;
            }
            // ensureAmmoType = true：匣内弹种与选定弹种不一致时也需要换弹。
            return current != selected;
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        //  ④ 手持武器自动换弹：GetPriority（让 CE 的 think 节点不被跳过）+ TryGiveJob（补货）
        // ═════════════════════════════════════════════════════════════════════════════════
        public static void CheckReloadPriorityPostfix(Pawn pawn, ref float __result)
        {
            try
            {
                if (__result > 0f)
                {
                    return;
                }
                if (!CanConsiderCheckReload(pawn))
                {
                    return;
                }
                // CE 认为"库存里没弹"→ 优先级 0 → TryGiveJob 不会被调用；这里只把优先级抬起来，
                // 真正的取货/补货留给 TryGiveJob 那一腿（避免在高频的 GetPriority 里做重活）。
                __result = 9.1f;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（换弹优先级）失败（已忽略，CE 行为不受影响）", e);
            }
        }

        public static void CheckReloadJobPostfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (__result != null || !CanConsiderCheckReload(pawn))
                {
                    return;
                }
                ThingDef def;
                int need;
                if (!NeedAmmoForPawn(pawn, out def, out need))
                {
                    return;
                }
                if (SDS_API.IsManualDepositAvailable(pawn))
                {
                    // 自身即端点（带超维芯片 / 从属机械体）：就地取货 —— 把真货从网格塞进小人随身仓库，
                    // 随后交回 CE 自己的 TryMakeReloadJob（原地装填，CE 原表现）。
                    // 先确认造得出 Job 再取货，避免"弹药白躺背包、优先级却已被抬高"。
                    Job ceJob = InvokeTryMakeReloadJob(pawn, def);
                    if (ceJob == null)
                    {
                        return;
                    }
                    if (MaterializeIntoInventory(pawn, def, need) <= 0)
                    {
                        return;
                    }
                    NotifyChecked(pawn);
                    __result = ceJob;
                    return;
                }
                // 普通小人（身上没有可用芯片、也不是继承芯片的从属机械体）：**绝不把货凭空塞进他背包**。
                // 与「训练取食 WithdrawForTrain」同款手法（用户 2026-10-18 定稿）：
                //   ① 先把真货物化到**取物点地面**（无芯片小人 = 最近的核心/锚点交互格）；
                //   ② 再交回**原版 TakeInventory**（targetA = 那件弹药）——
                //      小人自己走过去弯腰把它拿起来放进背包（原版"手拿再搬"表现）；
                //   ③ 随后 CE 下一轮自己的 DoReloadCheck 看到背包里有弹 → 由 CE 出它自己的 ReloadWeapon Job。
                // 这样"弹药出现在身上"之前，一定是小人**自己走过去拿的**，不会再出现越过搬运的表现。
                Thing placed = MaterializeToMap(def, need, pawn.Map, WithdrawCellFor(pawn, null));
                if (placed == null)
                {
                    return;
                }
                Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, placed);
                job.count = System.Math.Min(placed.stackCount, need);
                NotifyChecked(pawn);
                __result = job;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（手持武器换弹取货）失败（已忽略，CE 行为不受影响）", e);
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        //  ④-征兆【新增】空匣装弹 Job 插入
        //
        //  问题（用户 2026-10-19 实测）：征兆小人弹匣打空后只会发呆，不会自动装弹。
        //  原因不是 CE 的 think 节点没跑到，而是：
        //   - CE 的 JobGiver_CheckReload.TryGiveJob 在征兆时会走 DoReloadCheck 的 drafted 分支，
        //     而它最后要求 `inventory.AmmoCountOfDef(ammoType) >= 1` —— 只认背包里的弹；
        //   - 本 Mod 的网格库存不在背包里；④ 原实现又把 pawn.Drafted 直接排除；
        //   - ⑧ TryPrepareShot 只在"真的尝试开火"时触发，⑨ 的 TryPickupAmmo 也要 CE 自己先走到缺弹处理；
        //     征兆闲置 / 战术 Job 早退时两条都不会执行。
        //  于是没有任何一条路径会插入换弹 Job。
        //
        //  修法（不改变 CE 的 Job/T driver/表现）：在 CE 原体执行之前补来源。
        //   - 先复刻 CE DoReloadCheck 的 drafted 非来源闸门（伺机装填开关、战斗后冷却、安全距离、阈值）；
        //   - 自身端点：把真弹塞进随身库存（并刷 CE 的 CompInventory 缓存），随后 **CE 原体自己**
        //     的 DoReloadCheck 看到背包有弹，用它自己的 TryUnload / SelectedAmmo / TryMakeReloadJob
        //     造出 CE_JobDefOf.ReloadWeapon Job —— Job 结构、司机、toil、表现全部是 CE 原样。
        //   - 非自身端点：把真弹物化到最近取物点，交回原版 TakeInventory；小人自己走过去拿，
        //     下一轮 CE 看到背包有弹后照旧由 CE 自己造换弹 Job（与④未征召路径同一手法）。
        // ═════════════════════════════════════════════════════════════════════════════════
        public static bool CheckReloadJobPrefix(Pawn pawn, ref Job __result)
        {
            try
            {
                // 别的 Prefix 已经给了 Job（或原版/其它 Mod 先接管）→ 一字不动。
                if (__result != null)
                {
                    return true;
                }
                if (pawn == null || pawn.Destroyed || pawn.Map == null || pawn.Dead || pawn.Downed
                    || pawn.InMentalState || pawn.Faction != Faction.OfPlayer || !pawn.Drafted)
                {
                    return true;
                }
                if (!GridUsable(pawn.Map) || IsOnCheckCooldown(pawn)
                    || IsCeReloadWeaponJob(pawn.CurJobDef))
                {
                    return true;
                }
                object primary = GetComp(pawn.equipment != null ? pawn.equipment.Primary : null, tCompAmmoUser);
                if (primary == null || !BoolMember(primary, "HasMagazine") || !BoolMember(primary, "UseAmmo"))
                {
                    return true;
                }
                int cur = IntMember(primary, "CurMagCount");
                int size = IntMember(primary, "MagSize");
                if (size <= 0 || cur >= size || cur > IntMember(primary, "TryReloadOn"))
                {
                    return true;
                }
                // 复刻 CE DoReloadCheck 的 drafted 非来源闸门，保证不越过 CE 自己的安全策略。
                if (!BoolMember(primary, "IsOpportunisticReloadActive")
                    || GenTicks.TicksGame - pawn.LastAttackTargetTick
                        < IntMember(primary, "MinimalTicksAfterFight")
                    || HasHostileWithin(pawn, FloatMember(primary, "SafeDistanceToReload")))
                {
                    return true;
                }
                ThingDef def;
                int need;
                int available;
                // 征兆路径必须与 CE 的 DoReloadCheck 完全同口径：它只认 SelectedAmmo，
                // 不会像"未征召 + 空匣"那样退而求其次用集合内其它弹种。
                if (!TryFindSupplyAmmo(primary, false, out def, out need, out available))
                {
                    return true;
                }
                if (SDS_API.IsManualDepositAvailable(pawn))
                {
                    // 自身端点：先补进背包，再放行 CE 原体。CE 原体会看到 HasAmmo 并自己造 Job。
                    if (MaterializeIntoInventory(pawn, def, need) > 0)
                    {
                        NotifyChecked(pawn);
                    }
                    return true;
                }
                // 非自身端点：物化到取物点，交回原版 TakeInventory；拿起来后 CE 下一轮自己换弹。
                Thing placed = MaterializeToMap(def, need, pawn.Map, WithdrawCellFor(pawn, null));
                if (placed == null)
                {
                    return true;
                }
                Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, placed);
                job.count = System.Math.Min(placed.stackCount, need);
                NotifyChecked(pawn);
                __result = job;
                return false;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（征兆空匣装弹）失败（已忽略，CE 行为不受影响）", e);
                return true;
            }
        }

        /// <summary>当前 Job 是不是 CE 自己的换弹 Job（避免同一轮重复补货）。</summary>
        private static bool IsCeReloadWeaponJob(JobDef def)
        {
            if (def == null || fReloadWeaponDef == null || tCeJobDefOf == null)
            {
                return false;
            }
            try
            {
                return ReferenceEquals(def, fReloadWeaponDef.GetValue(null) as JobDef);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>CE drafted 分支的安全距离判定：范围内有敌对小人就不装弹。</summary>
        private static bool HasHostileWithin(Pawn pawn, float distance)
        {
            if (pawn == null || pawn.Map == null || pawn.Map.mapPawns == null || distance <= 0f)
            {
                return false;
            }
            IReadOnlyList<Pawn> all = pawn.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn other = all[i];
                if (other != null && other.Position.InHorDistOf(pawn.Position, distance)
                    && !other.IsPsychologicallyInvisible() && other.HostileTo(pawn))
                {
                    return true;
                }
            }
            return false;
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        //  ④-征兆·攻击中打空【新增】JobTrackerTick 之后接管
        //
        //  为什么 think-tree 的 Prefix 不够（用户 2026-10-19 二次实测）：
        //  AttackStatic 是**持续 Job**，只要 Job 没结束，pawn 的 think tree 就不会重跑；
        //  于是即使网格里有弹，Prefix 也一直没机会执行。CE 自己的 TryStartReload 在攻击链里
        //  又可能被战术驱动 / 当前姿态 / 同 tick 保护挡掉。结果就是"当前工作仍是攻击XXXX"，
        //  只有玩家手动移动、打断 Job 后 think tree 才重跑并换弹。
        //
        //  这里在 JobTrackerTick（= 当前 Job 的 DriverTick）**之后**补一次：
        //   - 只处理"征兆 + 当前是 AttackStatic + CE 弹匣枪 + 弹匣已空"；
        //   - 自身端点：把真弹补进背包，用 CE 自己的 TryMakeReloadJob 启动原生 ReloadWeapon Job；
        //   - 非自身端点：物化到最近取物点，把 [原版 TakeInventory → CE ReloadWeapon → 原攻击 Job]
        //     排成队列；小人先走过去拿弹，再换弹，再继续攻击同一目标。
        //  全程使用 CE 自己的 JobDef / Driver / toil，只把"空匣"这个状态从攻击 Job 里接管出来。
        // ═════════════════════════════════════════════════════════════════════════════════
        public static void JobTrackerTickPostfix(Pawn_JobTracker __instance)
        {
            try
            {
                Pawn pawn = __instance != null && fJobTrackerPawn != null
                    ? fJobTrackerPawn.GetValue(__instance) as Pawn
                    : null;
                if (pawn == null || pawn.Destroyed || pawn.Map == null || pawn.Dead || pawn.Downed
                    || pawn.InMentalState || pawn.Faction != Faction.OfPlayer || !pawn.Drafted
                    || pawn.CurJobDef != JobDefOf.AttackStatic)
                {
                    return;
                }
                object primary = GetComp(pawn.equipment != null ? pawn.equipment.Primary : null, tCompAmmoUser);
                if (primary == null || !BoolMember(primary, "HasMagazine") || !BoolMember(primary, "UseAmmo")
                    || IntMember(primary, "CurMagCount") > 0)
                {
                    return;
                }
                if (!GridUsable(pawn.Map) || IsOnCheckCooldown(pawn))
                {
                    return;
                }
                ThingDef def;
                int need;
                int available;
                // 与 CE 的 DoReloadCheck 同口径：征兆只认 SelectedAmmo。
                if (!TryFindSupplyAmmo(primary, false, out def, out need, out available))
                {
                    return;
                }
                if (SDS_API.IsManualDepositAvailable(pawn))
                {
                    if (MaterializeIntoInventory(pawn, def, need) <= 0)
                    {
                        return;
                    }
                    NotifyChecked(pawn);
                    StartCeReloadJob(pawn, def);
                    return;
                }
                // 非自身端点：先确认 CE 造得出换弹 Job，再把 拿弹 → 换弹 → 继续攻击 排成队列。
                Job reloadJob = InvokeTryMakeReloadJob(pawn, def);
                if (reloadJob == null)
                {
                    return;
                }
                Thing placed = MaterializeToMap(def, need, pawn.Map, WithdrawCellFor(pawn, null));
                if (placed == null)
                {
                    return;
                }
                Job takeJob = JobMaker.MakeJob(JobDefOf.TakeInventory, placed);
                takeJob.count = System.Math.Min(placed.stackCount, need);
                NotifyChecked(pawn);
                // SuspendCurrentJob 会把当前 AttackStatic 放到队列首位；再 EnqueueFirst(reload)
                // 得到 [ReloadWeapon, AttackStatic]，最后 StartJob(take) → [TakeInventory 正在跑,
                // 队列 ReloadWeapon → AttackStatic]。换弹结束后原攻击 Job 自动恢复。
                pawn.jobs.SuspendCurrentJob(JobCondition.InterruptForced, true);
                pawn.jobs.jobQueue.EnqueueFirst(reloadJob);
                pawn.jobs.StartJob(takeJob, JobCondition.InterruptForced, null, false, true);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（攻击中空匣接管）失败（已忽略，CE 行为不受影响）", e);
            }
        }

        /// <summary>
        /// 用 CE 自己的 TryMakeReloadJob 启动换弹 Job（与 CE 的 TryStartReload 底部同一手法）。
        /// 只用于"弹匣已空 + 袋里已有弹"的场景，因此不需要 TryUnload / 改 SelectedAmmo。
        /// </summary>
        private static void StartCeReloadJob(Pawn pawn, ThingDef def)
        {
            Job job = InvokeTryMakeReloadJob(pawn, def);
            if (job == null || pawn == null || pawn.jobs == null)
            {
                return;
            }
            job.playerForced = true;
            // resumeCurJobAfterwards = true：AttackStatic 可挂起，换弹结束后自动恢复原攻击目标。
            pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, true, true);
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        //  ⑤ 战斗 / 紧急捡弹：CE 在 6 格内找不到地面弹药时，让其改从网格取（自身端点的小人就地取）
        // ═════════════════════════════════════════════════════════════════════════════════
        public static void TryPickupAmmoPostfix(object __instance, ref bool __result)
        {
            try
            {
                if (__result || __instance == null)
                {
                    return;
                }
                Pawn pawn = GetMember(__instance, "Holder") as Pawn;
                if (pawn == null || pawn.Destroyed || pawn.Map == null || pawn.Faction != Faction.OfPlayer)
                {
                    return;
                }
                // 就地取货只对"自身即端点"的小人成立；其余交回 CE（它们本来就会走原版 TakeInventory）。
                if (!SDS_API.IsManualDepositAvailable(pawn) || !GridUsable(pawn.Map))
                {
                    return;
                }
                ThingDef def;
                int need;
                int avail;
                // CE 的 TryPickupAmmo 按整个 CurAmmoSet 搜地面弹药（⑤），所以允许集合内其它弹种。
                if (!TryFindSupplyAmmo(__instance, true, out def, out need, out avail))
                {
                    return;
                }
                // 与 CE 自己的"弹药已在背包"分支（CompAmmoUser.TryPrepareShot）**逐字同一手法**：
                //   reload.playerForced = true; StartJob(InterruptForced, resumeCurJobAfterwards = 当前工作不是换弹 Job)
                // → 打断当前工作去换弹，**换完弹自动回到原来的工作**。
                // 对比：CE 的"弹药在地上"分支用的是 `EnqueueFirst + if (Drafted) EndCurrentJob(InterruptForced)`
                // —— 那条**不恢复原工作**（实测后果：征召小人被下令攻击某物体，换弹后玩家命令丢失、他就站着）。
                // 所以这里取前者：既符合"自动换弹后继续攻击目标"的预期，也完全沿用 CE 自己的既有手法。
                //
                // ★ 先确认"造得出换弹 Job"再取货：否则取出来的弹药会白躺在背包里
                //   （而且我们会把 __result 报成 true，CE 就不会走它自己的换武器兜底）。
                Job reload = InvokeTryMakeReloadJob(pawn, def);
                if (reload == null || pawn.jobs == null)
                {
                    return;
                }
                if (MaterializeIntoInventory(pawn, def, need) <= 0)
                {
                    return;
                }
                reload.playerForced = true;
                bool resumeOld = pawn.CurJob == null || pawn.CurJob.def != reload.def;
                pawn.jobs.StartJob(reload, JobCondition.InterruptForced, null, resumeOld, true);
                __result = true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（紧急捡弹）失败（已忽略，CE 行为不受影响）", e);
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        //  共用实现
        // ═════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ① ② 的前置补货：CE 自己在图上找得到弹药就完全不插手；找不到而我们有 → 把这批真货
        /// 物化到**取物点**（自身端口的小人 = 自己脚下；否则= 端点交互格，随后 CE 的司机会自己
        /// 走过去拿起来再送到炮塔），然后一律放行 CE 自己的原体去造 Job。
        /// </summary>
        private static bool ReloadJobPrefix(Pawn pawn, Thing target, bool autoLoader)
        {
            try
            {
                ClearPending();
                if (pawn == null || target == null || pawn.Map == null || target.Map != pawn.Map
                    || pawn.Faction != Faction.OfPlayer)
                {
                    return true;
                }
                // 原版/CE 优先：CE 自己能找到弹药 → 一个字都不动。
                if (CeFindsAmmo(pawn, target, autoLoader))
                {
                    return true;
                }
                ThingDef def;
                int need;
                if (!NeedAmmoFor(target, out def, out need))
                {
                    return true;
                }
                Thing placed = MaterializeToMap(def, need, pawn.Map, WithdrawCellFor(pawn, target));
                if (placed != null)
                {
                    pendingAmmo = placed;
                    pendingAmmoTarget = target;
                    pendingAmmoDef = def;
                }
                // 无论成没成都放行 CE 原体：成功时它自己那一次 FindBestAmmo 就命中这堆真货；
                // 失败时它照旧走自己的失败分支（与 CE 原生行为一致，不额外制造差异）。
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（换弹前置补货）失败（已忽略，CE 行为不受影响）", e);
                return true;
            }
        }

        /// <summary>
        /// ① ② 的兜底：CE 原体仍没造出 Job 时，按 CE 自己的构造口径**直构** ReloadTurret /
        /// ReloadAutoLoader Job（跳过它内部的 FindBestAmmo，直接用我们刚才补出来的那件真货），
        /// 保证"点了优先装填就一定有活干"，而不是留一堆弹药在地上没人管。
        /// </summary>
        private static void ReloadJobPostfix(Pawn pawn, Thing target, bool autoLoader, ref Job __result)
        {
            try
            {
                if (__result != null)
                {
                    ClearPending();
                    return;
                }
                if (pawn == null || target == null || pawn.Map == null || pawn.Faction != Faction.OfPlayer)
                {
                    ClearPending();
                    return;
                }
                // 目标类型必须与 CE 的 JobOnThing 契约一致，否则 CE 原体自己会 NRE / 报错。
                if (autoLoader
                    ? tBuildingAutoloaderCe == null || !tBuildingAutoloaderCe.IsInstanceOfType(target)
                    : !(target is Building_Turret))
                {
                    ClearPending();
                    return;
                }
                JobDef reloadDef = autoLoader ? ReloadAutoLoaderJobDef() : ReloadTurretJobDef();
                object ammoComp = AmmoCompOf(target);
                if (reloadDef == null || ammoComp == null)
                {
                    ClearPending();
                    return;
                }
                if (!BoolMember(ammoComp, "UseAmmo"))
                {
                    // 与 CE 同口径：MakeReloadJobNoAmmo 只对炮塔存在；
                    // CE 的 MakeReloadJob(Building_AutoloaderCE) 在 !UseAmmo 时会记 Error 并返回 null。
                    if (!autoLoader)
                    {
                        __result = JobMaker.MakeJob(reloadDef, new LocalTargetInfo(target), LocalTargetInfo.Invalid);
                    }
                    ClearPending();
                    return;
                }
                Thing ammo = pendingAmmoTarget == target && pendingAmmo != null
                             && !pendingAmmo.Destroyed && pendingAmmo.Spawned
                    ? pendingAmmo
                    : null;
                // 前置腿没补上（或那件货已消失）→ 再取一次；仍取不到就不造 Job（保持 CE 的原结果）。
                if (ammo == null)
                {
                    ThingDef def;
                    int need;
                    if (NeedAmmoFor(target, out def, out need))
                    {
                        ammo = MaterializeToMap(def, need, pawn.Map, WithdrawCellFor(pawn, target));
                    }
                }
                if (ammo == null)
                {
                    ammo = pendingAmmoDef != null ? FindSpawnedAmmoNear(pawn, pendingAmmoDef) : null;
                }
                if (ammo == null)
                {
                    ClearPending();
                    return;
                }
                Job job = JobMaker.MakeJob(reloadDef, new LocalTargetInfo(target), new LocalTargetInfo(ammo));
                int missing = IntMember(ammoComp, "MissingToFullMagazine");
                job.count = System.Math.Min(ammo.stackCount, missing > 0 ? missing : ammo.stackCount);
                __result = job;
                ClearPending();
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（换弹兜底直构）失败（已忽略，CE 行为不受影响）", e);
                ClearPending();
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        //  ⑧【战斗中"打空即换弹"】CompAmmoUser.TryPrepareShot 的 Prefix
        //
        //  为什么必须补这里（2026-10-18 实测）：CE 的 `Verb_ShootCE.TryCastShot` **每次尝试开火**
        //  都会先调 `compAmmoUser.TryPrepareShot()`（Verb_ShootCE.cs:456-462）。而它在
        //  "弹匣空 + 背包里没弹"时会走 `DoOutOfAmmoAction()` → 最终到 `TryPickupAmmo()`（我们 ⑤ 的挂点），
        //  但实测在"征召中攻击某物体"时 **那条链根本没走到**（Player.log 里一条 [⑤捡弹] 都没有），
        //  结果小人卡在「攻击XX物体」什么都不做。
        //
        //  这里改在**更上层的决策点**只做一件事：让 CE 自己的判断看见我们网格里的弹
        //  （真货塞进背包 + UpdateInventory），然后**放行 CE 原体**。CE 原体会走它自己的
        //  `TryMakeReloadJob() → StartJob(InterruptForced, resumeCurJobAfterwards = 当前工作不是换弹 Job)`
        //  —— 这条是 CE 自己写的路径，**天然会打断当前攻击、并在换完弹后恢复原来的工作**
        //  （即：打空 → 原地换弹 → 继续攻击同一目标）。与 ⑤ 同一思想：
        //  不改 CE 的表现，只把"有没有弹"这一层的答案改成"有"。
        // ═════════════════════════════════════════════════════════════════════════════════
        public static bool TryPrepareShotPrefix(object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return true;
                }
                Pawn pawn = GetMember(__instance, "Holder") as Pawn;
                if (pawn == null || pawn.Destroyed || pawn.Map == null || pawn.Faction != Faction.OfPlayer)
                {
                    return true;
                }
                // 只有"自身即端点"的小人（带超维芯片 / 从属机械体）才就地补；其余交回 CE 自己的路径。
                if (!SDS_API.IsManualDepositAvailable(pawn) || !GridUsable(pawn.Map))
                {
                    return true;
                }
                // 背包里已经有它能用的弹 → 一字不动（CE 原体自己就会换弹）。
                if (BoolMember(__instance, "HasAmmo"))
                {
                    return true;
                }
                // ★ 只在 CE 的 TryPrepareShot 真会失败时补货：
                //   CE 原体是 `if (HasMagazine) { if (curMagCount <= 0) return false; return true; }`。
                //   弹匣里还有弹时它会直接返回 true，不会走任何"没弹"分支；
                //   若这里也补货，就会在每次开火前把一整匣弹药从网格提前抽进小人背包（平白增加负重）。
                if (BoolMember(__instance, "HasMagazine") && IntMember(__instance, "CurMagCount") > 0)
                {
                    return true;
                }
                ThingDef def;
                int need;
                int avail;
                if (!TryFindSupplyAmmo(__instance, !BoolMember(__instance, "LoadedMagazine"), out def, out need, out avail))
                {
                    return true;
                }
                if (MaterializeIntoInventory(pawn, def, need) <= 0)
                {
                    return true;
                }
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（打空即换弹）失败（已忽略，CE 行为不受影响）", e);
                return true;
            }
        }

        /// <summary>CE 自己（JobGiverUtils_Reload 的私有/公开 FindBestAmmo）在图上能不能找到弹药。</summary>
        private static bool CeFindsAmmo(Pawn pawn, Thing target, bool autoLoader)
        {
            MethodInfo find = autoLoader ? mFindBestAmmoAutoloader : mFindBestAmmoTurret;
            if (find == null)
            {
                return false;
            }
            try
            {
                return find.Invoke(null, new object[] { pawn, target }) != null;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容：FindBestAmmo 反射调用失败（按 CE 找不到弹药处理）", e);
                return false;
            }
        }

        /// <summary>
        /// ⑨ CE 所有"去地图上找弹药"的最终收口（炮塔 / 装弹器 / CanReload / MakeReloadJob 都经这里）。
        /// 镜像若被返回，CE 的司机只会 `GotoCell(镜像位置)` + `LoadAmmo(job.targetB)`：
        /// 走不到金库、也扣不了账 → 表现为"装填成功但物品没少"。这里把它换成最近的**真货**；
        /// 找不到真货就返回 null，交回 ①② 的前置补货腿物化后再由 CE 自己重新找到。
        /// </summary>
        public static void FindBestAmmoMirrorFilterPostfix(Pawn pawn, ref Thing __result)
        {
            try
            {
                if (!SDS_API.IsMirror(__result))
                {
                    return;
                }
                __result = FindRealAmmo(pawn, __result.def);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容（弹药搜索镜像过滤）失败（已忽略，CE 行为不受影响）", e);
            }
        }

        /// <summary>
        /// 与 CE 的 `FindBestAmmo(Pawn, AmmoDef)` 同口径再搜一次，但**排除镜像**。
        /// 只走 GenClosest 的原版路径（校验器也会挡掉镜像补漏），不会递归进被补丁的方法。
        /// </summary>
        private static Thing FindRealAmmo(Pawn pawn, ThingDef def)
        {
            if (pawn == null || pawn.Map == null || def == null || def.category != ThingCategory.Item)
            {
                return null;
            }
            float radius = pawn.Faction != Faction.OfPlayer ? 40f : 9999f;
            try
            {
                return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map,
                    ThingRequest.ForDef(def), PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn, Danger.Deadly), radius,
                    t => t != null && !t.Destroyed && !SDS_API.IsMirror(t)
                         && !ForbidUtility.IsForbidden(t, pawn) && !FireUtility.IsBurning(t)
                         && pawn.CanReserve(t, 1, -1, null, false));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// ⑩ 驱动兜底：换弹 Job 的 targetA / targetB 只要还有镜像，直接让 Job 失败。
        /// 宁可这次不装填（弹还在网格里），也绝不让 `LoadAmmo` 去扣镜像的显示数量。
        /// </summary>
        public static bool ReloadDriverMirrorGuardPrefix(JobDriver __instance, ref bool __result)
        {
            Job job = __instance != null ? __instance.job : null;
            if (job == null)
            {
                return true;
            }
            if (SDS_API.IsMirror(job.GetTarget(TargetIndex.A).Thing)
                || SDS_API.IsMirror(job.GetTarget(TargetIndex.B).Thing))
            {
                __result = false;
                return false;
            }
            return true;
        }

        /// <summary>①②：补货落点 = 取物点（自身端口＝自己脚下；建筑端点＝交互格）。</summary>
        private static IntVec3 WithdrawCellFor(Pawn pawn, Thing target)
        {
            try
            {
                if (SDS_API.TryGetWithdrawCell(pawn, out IntVec3 cell, out _))
                {
                    return cell;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容：取物点解析失败（回落到目标 / 自身位置）", e);
            }
            return target != null && target.Spawned ? target.Position : pawn.Position;
        }

        private static JobDef ReloadTurretJobDef()
        {
            try
            {
                return fReloadTurretDef != null ? fReloadTurretDef.GetValue(null) as JobDef : null;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容：读取 ReloadTurret JobDef 失败", e);
                return null;
            }
        }

        private static JobDef ReloadAutoLoaderJobDef()
        {
            try
            {
                return fReloadAutoLoaderDef != null ? fReloadAutoLoaderDef.GetValue(null) as JobDef : null;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容：读取 ReloadAutoLoader JobDef 失败", e);
                return null;
            }
        }

        /// <summary>在图上就近找一件已 spawn 的该种弹药（兜底腿用；找不到就返回 null）。</summary>
        private static Thing FindSpawnedAmmoNear(Pawn pawn, ThingDef def)
        {
            try
            {
                return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(def),
                    PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                    th => th != null && !th.Destroyed && th.Spawned && !th.IsForbidden(pawn));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void ClearPending()
        {
            pendingAmmo = null;
            pendingAmmoTarget = null;
            pendingAmmoDef = null;
        }

        /// <summary>① ②③ 共用：这件目标（炮塔/装弹器）当前该从网格补哪种弹、补多少（按 CE 自己的口径）。</summary>
        private static bool NeedAmmoFor(Thing target, out ThingDef def, out int need)
        {
            int available;
            // 炮塔 / 自动装弹器：CE 的 FindBestAmmo 对玩家阵营只认 SelectedAmmo，不退到集合内其它弹种。
            return TryFindSupplyAmmo(AmmoCompOf(target), false, out def, out need, out available);
        }

        /// <summary>
        /// W33：该武器是否带 CE 装填状态（弹匣内有弹）。虚拟化会丢弹，必须走保真。
        /// 未安装 CE / 非武器 / 读取失败 → false（失败按“无装填状态”处理，避免无谓保真）。
        /// </summary>
        public static bool HasLoadedAmmoState(Thing thing)
        {
            if (tCompAmmoUser == null || thing == null || thing.def == null || !thing.def.IsWeapon)
            {
                return false;
            }
            try
            {
                object comp = GetComp(thing, tCompAmmoUser);
                return comp != null && IntMember(comp, "CurMagCount") > 0;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容：读取弹匣状态失败（按无装填状态处理）", e);
                return false;
            }
        }

        /// <summary>取目标的 CE 弹药组件：炮塔走 TurretRegistry.GetAmmo 扩展，其余走普通组件查找。</summary>
        private static object AmmoCompOf(Thing target)
        {
            try
            {
                if (mGetAmmo != null && target is Building_Turret)
                {
                    return mGetAmmo.Invoke(null, new object[] { target });
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容：TurretRegistry.GetAmmo 调用失败（回落到普通组件查找）", e);
            }
            return GetComp(target, tCompAmmoUser);
        }

        /// <summary>
        /// 核心：这台 CE 弹药组件要不要补弹、该补**哪种**、补多少（且网格确实拿得出）。
        ///
        /// 口径完全跟 CE 走：
        ///  - 缺额用 `CompAmmoUser.MissingToFullMagazine`；
        ///  - 首选**枪上选定的那一种**（`SelectedAmmo`，CE 自己的 FindBestAmmo 用的就是它）；
        ///  - 只有调用方明确允许时才退回"同一弹种集合里的其它弹种"。CE 的允许条件各不相同：
        ///      · 炮塔 / 自动装弹器：只有**非玩家**阵营才会在空匣时退而求其次（玩家的一律只认选定弹种）；
        ///      · 个人武器：背包搜索（TryFindAmmoInInventory）在**弹匣未装填**时接受集合内其它弹种；
        ///      · 战斗捡弹（TryPickupAmmo）：直接按整个集合搜地面弹药。
        ///    传错这一位会造成两类偏差：玩家炮塔被塞进"选定弹种之外"的弹（CE 的 MakeReloadJob
        ///    找不到它并报错），或弹匣半满时补进 CE 不会使用的弹种（Job 开局即失败、反复重试）。
        /// </summary>
        private static bool TryFindSupplyAmmo(object ammoComp, bool allowAmmoSetFallback,
            out ThingDef supplyDef, out int need, out int available)
        {
            supplyDef = null;
            need = 0;
            available = 0;
            if (ammoComp == null || !BoolMember(ammoComp, "UseAmmo"))
            {
                return false;
            }
            int missing = IntMember(ammoComp, "MissingToFullMagazine");
            if (missing <= 0)
            {
                return false;
            }
            ThingDef gunDef = GetMember(ammoComp, "SelectedAmmo") as ThingDef;
            if (gunDef == null)
            {
                return false;
            }
            if (HasInGrid(gunDef, out available))
            {
                supplyDef = gunDef;
                need = ClampNeed(gunDef, missing);
                return need > 0;
            }
            if (!allowAmmoSetFallback)
            {
                return false;
            }
            foreach (ThingDef alt in AmmoSetDefs(ammoComp))
            {
                if (alt == null || alt == gunDef)
                {
                    continue;
                }
                if (HasInGrid(alt, out available))
                {
                    supplyDef = alt;
                    need = ClampNeed(alt, missing);
                    return need > 0;
                }
            }
            return false;
        }

        /// <summary>网格里这类物品拿得出来多少（&gt;0 才算能供）。</summary>
        private static bool HasInGrid(ThingDef def, out int available)
        {
            available = 0;
            if (def == null)
            {
                return false;
            }
            return SDS_API.TryFindWithdrawableKey(def, out SDS_StockKey _, out available) && available > 0;
        }

        /// <summary>补货量夹到单堆上限。</summary>
        private static int ClampNeed(ThingDef def, int missing)
        {
            int limit = def != null && def.stackLimit > 0 ? def.stackLimit : 1;
            return missing > limit ? limit : missing;
        }

        /// <summary>该 CE 弹药组件所属"弹种集合"里的所有弹种 def（CurAmmoSet.ammoTypes[i].ammo）。</summary>
        private static IEnumerable<ThingDef> AmmoSetDefs(object ammoComp)
        {
            object set = GetMember(ammoComp, "CurAmmoSet");
            object types = set != null ? GetMember(set, "ammoTypes") : null;
            System.Collections.IEnumerable list = types as System.Collections.IEnumerable;
            if (list == null)
            {
                yield break;
            }
            foreach (object link in list)
            {
                ThingDef def = link != null ? GetMember(link, "ammo") as ThingDef : null;
                if (def != null)
                {
                    yield return def;
                }
            }
        }

        /// <summary>① ②：这件目标是否"我们能供弹"（含网格可用 + 本图暴露库存 + 取物点/库存里有这种弹）。</summary>
        private static bool CanSupplyAmmoFor(Pawn pawn, Thing target, bool forced)
        {
            if (pawn == null || target == null || pawn.Map == null || target.Map != pawn.Map)
            {
                return false;
            }
            if (!GridUsable(pawn.Map))
            {
                return false;
            }
            // 与 CE 的 CanReload 预留 / 可达口径逐条对齐：
            //  - 炮塔：`InteractionCell` + maxPawns 10（正在操作它的小人豁免）；
            //    CE 的 JobDriver_ReloadTurret 是 `GotoCell(turret.InteractionCell, OnCell)`，
            //    用 ClosestTouch 会放行"交互格走不到、但旁边能站"的假阳性。
            //  - 自动装弹器：CE 只做 CanReserve（司机走到建筑所在格），这里仍补一次可达判定。
            Danger danger = forced ? Danger.Deadly : pawn.NormalMaxDanger();
            if (target is Building_Turret)
            {
                CompMannable mannable = target.TryGetComp<CompMannable>();
                bool mannedByMe = mannable != null && mannable.ManningPawn == pawn;
                if (!mannedByMe && !pawn.CanReserveAndReach(target, PathEndMode.InteractionCell,
                        danger, 10, -1, null, false))
                {
                    return false;
                }
            }
            else if (!pawn.CanReserveAndReach(target, PathEndMode.ClosestTouch, danger))
            {
                return false;
            }
            ThingDef def;
            int need;
            if (!NeedAmmoFor(target, out def, out need))
            {
                return false;
            }
            return SDS_API.TryFindWithdrawableKey(def, out SDS_StockKey _, out int available) && available > 0;
        }

        /// <summary>
        /// ① ② 放行候选前的非弹药条件复核。
        /// 必须覆盖 CE 在"弹药"之外的全部拒绝原因，否则会出现 HasJobOnThing 放行、
        /// CE 自己的 JobOnThing 却造不出 Job（原版会报 "provided target ... but yielded no actual job"），
        /// 或者对正在装填 / 敌对的建筑硬派一个换弹 Job。
        /// </summary>
        private static bool CanReserveTurret(Pawn pawn, Thing target)
        {
            if (pawn == null || target == null)
            {
                return false;
            }
            if (ForbidUtility.IsForbidden(target, pawn) || FireUtility.IsBurning(target))
            {
                return false;
            }
            object ammoComp = AmmoCompOf(target);
            if (BoolMember(ammoComp, "FullMagazine"))
            {
                return false;
            }
            // 阵营口径（CE CanReload 同一条）：目标必须是自己阵营或盟友。
            if (pawn.Faction != null && target.Faction != pawn.Faction)
            {
                Faction faction = target.Faction;
                if (faction == null || faction.RelationKindWith(pawn.Faction) != FactionRelationKind.Ally)
                {
                    return false;
                }
            }
            if (target is Building_Turret)
            {
                // CE 的 HasJobOnThing 先查 `turret.GetReloadable()`（= GetAmmo()?.HasMagazine），
                // 不可换弹的炮塔 CE 根本不会派活。
                if (mGetReloadable != null && !InvokeStaticBool(mGetReloadable, target))
                {
                    return false;
                }
                // CE 的 CanReload 要求具备 Manipulation；不满足时 Job 会在 Toils 里立刻失败。
                if (pawn.health == null || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                {
                    return false;
                }
                // 正在装填的炮塔（CE 自己的状态位）：不插手。
                if (InvokeStaticBool(mGetReloading, target))
                {
                    return false;
                }
            }
            else if (tBuildingAutoloaderCe != null && tBuildingAutoloaderCe.IsInstanceOfType(target))
            {
                // CE 的 WorkGiver_ReloadAutoLoader.HasJobOnThing 在 CanReload 之前先拒绝忙碌的装弹器。
                if (BoolMember(target, "isReloading") || BoolMember(target, "isActive"))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>反射调用一个 `static bool M(Building_Turret)` 形式的 CE 方法；找不到方法时返回 false。</summary>
        private static bool InvokeStaticBool(MethodInfo method, Thing target)
        {
            if (method == null || target == null)
            {
                return false;
            }
            object value = method.Invoke(null, new object[] { target });
            return value is bool flag && flag;
        }

        /// <summary>④：这小人是否有"一把缺弹的 CE 枪"、缺多少、要哪种弹（用于 GetPriority 的廉价判定）。</summary>
        private static bool CanConsiderCheckReload(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Map == null || pawn.Downed || pawn.Dead || pawn.InMentalState
                || pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }
            // 与 CE 的 JobGiver_CheckReload.GetPriority 完全一致：它只对**未征召**小人返回 9.1。
            // 征召中的换弹由 CheckReloadJobPrefix 负责：它在 CE 原体之前补来源，
            // 并逐条复刻 CE DoReloadCheck 的 drafted 非来源闸门（伺机装填 / 战斗后冷却 / 安全距离 / 阈值）。
            // 这里保持"未征召专用"，避免 Postfix 绕过 CE 的判定。
            if (pawn.Drafted)
            {
                return false;
            }
            // GetPriority 每 tick 都会被调用，这里按小人做结果缓存：
            //  * 结果为「能供」→ 缓存 PriorityCacheTicks（60 tick），避免紧随其后的 TryGiveJob 重复做重活；
            //  * 结果为「不能供」→ 只缓存 NegativeCacheTicks（10 tick）。
            //    ★ 为什么要分开（2026-10-18 实测）：原先一律缓存 60 tick，于是"枪刚打空"那一下的判定
            //      可能命中上一次的"不需要补弹"缓存，最高要等 1 秒才被重新评估 ——
            //      玩家感受就是"不是打空一瞬间就换弹，好像有延迟 / 得等他动一下"。
            int now = GenTicks.TicksGame;
            int until;
            if (priorityCacheUntil.TryGetValue(pawn.thingIDNumber, out until) && now < until)
            {
                bool cached;
                return priorityCacheValue.TryGetValue(pawn.thingIDNumber, out cached) && cached;
            }
            bool can = CanConsiderCheckReloadUncached(pawn);
            priorityCacheUntil[pawn.thingIDNumber] = now + (can ? PriorityCacheTicks : NegativeCacheTicks);
            priorityCacheValue[pawn.thingIDNumber] = can;
            return can;
        }

        private static bool CanConsiderCheckReloadUncached(Pawn pawn)
        {
            if (!GridUsable(pawn.Map))
            {
                return false;
            }
            if (IsOnCheckCooldown(pawn))
            {
                return false;
            }
            ThingDef def;
            int need;
            if (!NeedAmmoForPawn(pawn, out def, out need))
            {
                return false;
            }
            return true;
        }

        /// <summary>④：小人身上第一把"缺弹、且网格供得上"的 CE 枪该补的弹种与缺额。</summary>
        private static bool NeedAmmoForPawn(Pawn pawn, out ThingDef def, out int need)
        {
            def = null;
            need = 0;
            if (pawn == null)
            {
                return false;
            }
            int available;
            // 个人武器：CE 的 TryFindAmmoInInventory 只在"弹匣未装填"（无弹匣或已打空）时
            // 才接受同集合里的其它弹种；弹匣半满时仍只认 SelectedAmmo。
            object primary = GetComp(pawn.equipment != null ? pawn.equipment.Primary : null, tCompAmmoUser);
            if (TryFindSupplyAmmo(primary, !BoolMember(primary, "LoadedMagazine"),
                    out def, out need, out available))
            {
                return true;
            }
            def = null;
            need = 0;
            ThingOwner inv = pawn.inventory != null ? pawn.inventory.innerContainer : null;
            if (inv != null)
            {
                foreach (Thing thing in inv)
                {
                    object comp = GetComp(thing, tCompAmmoUser);
                    if (TryFindSupplyAmmo(comp, !BoolMember(comp, "LoadedMagazine"),
                            out def, out need, out available))
                    {
                        return true;
                    }
                    def = null;
                    need = 0;
                }
            }
            return false;
        }

        /// <summary>与 CE 的 TryMakeReloadJob 同口径（原地换弹）：只在持有者身上有枪时才造得出来。</summary>
        private static Job InvokeTryMakeReloadJob(Pawn pawn, ThingDef def)
        {
            try
            {
                if (pawn == null)
                {
                    return null;
                }
                object comp = FindHoldingAmmoComp(pawn, def);
                if (comp == null)
                {
                    return null;
                }
                MethodInfo make = AccessTools.Method(tCompAmmoUser, "TryMakeReloadJob", Type.EmptyTypes);
                if (make == null)
                {
                    return null;
                }
                return make.Invoke(comp, null) as Job;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容：调用 CompAmmoUser.TryMakeReloadJob 失败（已忽略）", e);
                return null;
            }
        }

        /// <summary>找小人身上（手持或背包）用这种弹药的那把枪的 CompAmmoUser（含"同弹种集合"的其它弹种）。</summary>
        private static object FindHoldingAmmoComp(Pawn pawn, ThingDef def)
        {
            object comp = GetComp(pawn.equipment != null ? pawn.equipment.Primary : null, tCompAmmoUser);
            if (CompAcceptsAmmo(comp, def))
            {
                return comp;
            }
            ThingOwner inv = pawn.inventory != null ? pawn.inventory.innerContainer : null;
            if (inv != null)
            {
                foreach (Thing thing in inv)
                {
                    object c = GetComp(thing, tCompAmmoUser);
                    if (CompAcceptsAmmo(c, def))
                    {
                        return c;
                    }
                }
            }
            return null;
        }

        /// <summary>这把枪吃不吃这种弹：选定弹种相同，或它属于同一弹种集合。</summary>
        private static bool CompAcceptsAmmo(object comp, ThingDef def)
        {
            if (comp == null || def == null)
            {
                return false;
            }
            if ((GetMember(comp, "SelectedAmmo") as ThingDef) == def)
            {
                return true;
            }
            foreach (ThingDef d in AmmoSetDefs(comp))
            {
                if (d == def)
                {
                    return true;
                }
            }
            return false;
        }

        // ── 物化 / 取货 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// CE 取货的唯一实现：可用性判定 + 按可用量夹取 + 统一取货事务。
        /// 调用方身份必须显式传入（落地取货用 FromFaction，进背包用 FromPawn），
        /// 否则会把「小人行动」记成「派系行动」。放置与失败回充仍由调用方负责。
        /// </summary>
        private static bool TryTakeForCe(ThingDef def, int count, SDS_ApiCaller caller, out List<Thing> made)
        {
            made = null;
            if (!SDS_API.IsGridAvailable() || def == null || count <= 0)
            {
                return false;
            }
            if (!SDS_API.TryFindWithdrawableKey(def, out SDS_StockKey key, out int available) || available <= 0)
            {
                return false;
            }
            int take = SDS_API.ClampTakeToAvailable(count, available);
            if (!SDS_API.TryWithdraw(key, take, SDS_WithdrawMode.Hand, caller, out made, out _)
                || made == null || made.Count == 0)
            {
                made = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 从超维空间取出 count 件该 def，物化到地图（cell 附近）。带保真/不吞物品兜底：
        /// 落不下的一律回充网格，绝不凭空消失。本腿产物打「来源排除」标记，防止带芯片小人又吸回去。
        /// </summary>
        private static Thing MaterializeToMap(ThingDef def, int count, Map map, IntVec3 cell)
        {
            if (map == null
                || !TryTakeForCe(def, count, SDS_ApiCaller.FromFaction(Faction.OfPlayer), out List<Thing> made))
            {
                return null;
            }
            IntVec3 at = cell.IsValid ? cell : map.Center;
            Thing first = null;
            List<Thing> placed = new List<Thing>();
            for (int i = 0; i < made.Count; i++)
            {
                Thing thing = made[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                if (GenPlace.TryPlaceThing(thing, at, map, ThingPlaceMode.Near, out Thing p) && p != null)
                {
                    placed.Add(p);
                    if (first == null)
                    {
                        first = p;
                    }
                    continue;
                }
                // 地上放不下：回充超维空间，绝不吞物品（定稿 §十.4）。
                ReturnToGrid(thing);
            }
            if (placed.Count > 0)
            {
                SDS_API.MarkProducedPendingConsumption(placed);
            }
            return first;
        }

        /// <summary>
        /// 从超维空间取出 count 件该 def，真货塞进小人随身仓库（不落地），返回实际入库的数量。
        /// 入库失败的一律回充网格。**本腿不打来源排除标记**：这些货随即被 CE 的换弹链直接消耗，
        /// 不存在"取出→吸回"的通路（与 TryMaterializeReservationIntoInventory 同口径）。
        /// </summary>
        private static int MaterializeIntoInventory(Pawn pawn, ThingDef def, int count)
        {
            if (pawn == null || pawn.Destroyed || pawn.Map == null
                || pawn.inventory == null || pawn.inventory.innerContainer == null)
            {
                return 0;
            }
            if (!TryTakeForCe(def, count, SDS_ApiCaller.FromPawn(pawn), out List<Thing> made))
            {
                return 0;
            }
            ThingOwner inv = pawn.inventory.innerContainer;
            int before = CountDefInOwner(inv, def);
            for (int i = 0; i < made.Count; i++)
            {
                Thing thing = made[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                if (inv.TryAdd(thing, canMergeWithExistingStacks: true))
                {
                    continue;
                }
                ReturnToGrid(thing);
            }
            int added = CountDefInOwner(inv, def) - before;
            if (added > 0)
            {
                // ★ 必须让 CE 立刻"看见"这批真货（2026-10-18 实测修正）：
                //   CE 的 JobDriver_Reload 第一条 toil 就 FailOn(() => !compReloader.TryFindAmmoInInventory(out initAmmo))，
                //   而 TryFindAmmoInInventory 读的是 CompInventory.ammoList（= CE 自建缓存 ammoListCached），
                //   不是直接遍历 innerContainer。缓存只在 CompInventory.UpdateInventory() 里重建。
                //   虽然原版 ThingOwner.NotifyAdded 会经 CE 的 Postfix 兜到 UpdateInventory，但那依赖父链解析成功；
                //   这里显式刷一次，杜绝"弹药已经进了背包、CE 却看不见 → 派出的换弹 Job 开局即失败 → 什么动作都没有"。
                ThingComp invComp = GetComp(pawn, tCompInventory);
                if (invComp != null)
                {
                    InvokeMethod(invComp, "UpdateInventory");
                }
            }
            return added;
        }

        /// <summary>把一件真货回充超维空间（TryDeposit 内部完成扣账与销毁）；回充不了就留在原地，绝不吞物品。</summary>
        private static void ReturnToGrid(Thing thing)
        {
            SDS_API.TryReturnVirtual(thing, null, IntVec3.Invalid, false);
        }

        private static int CountDefInOwner(ThingOwner owner, ThingDef def)
        {
            if (owner == null || def == null)
            {
                return 0;
            }
            int total = 0;
            foreach (Thing thing in owner)
            {
                if (thing != null && thing.def == def)
                {
                    total += thing.stackCount;
                }
            }
            return total;
        }

        // ── 小工具 ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 小人背包发生变化后刷新 CE 的 CompInventory 缓存（CE 未安装时静默跳过）。
        /// 本 Mod 的多件存入 Job 会把实物临时放进背包，必须让 CE 立刻看见。
        /// </summary>
        public static void NotifyInventoryChanged(Pawn pawn)
        {
            if (pawn == null || tCompInventory == null)
            {
                return;
            }
            ThingComp invComp = GetComp(pawn, tCompInventory);
            if (invComp != null)
            {
                InvokeMethod(invComp, "UpdateInventory");
            }
        }

        /// <summary>
        /// CE 的背包容量判定：返回这件货最多能装多少（CE 未安装时返回整堆）。
        /// 本 Mod 的多件存入 Job 用它决定整堆拾取还是分出一部分。
        /// </summary>
        public static int CountCanFitInInventory(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null)
            {
                return 0;
            }
            if (tCompInventory == null)
            {
                return thing.stackCount;
            }
            ThingComp invComp = GetComp(pawn, tCompInventory);
            if (invComp == null)
            {
                return thing.stackCount;
            }
            try
            {
                MethodInfo mi = AccessTools.Method(tCompInventory, "CanFitInInventory",
                    new[] { typeof(Thing), typeof(int).MakeByRefType(), typeof(bool), typeof(bool) });
                if (mi == null)
                {
                    return thing.stackCount;
                }
                object[] args = { thing, 0, false, false };
                bool ok = mi.Invoke(invComp, args) is bool b && b;
                int count = args[1] is int i ? i : 0;
                if (!ok || count <= 0)
                {
                    return 0;
                }
                return Math.Min(count, thing.stackCount);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容：背包容量判定失败（按整堆放行）", e);
                return thing.stackCount;
            }
        }

        private static bool GridUsable(Map map)
        {
            return SDS_API.IsGridUsable(map);
        }

        private static ThingComp GetComp(Thing thing, Type compType)
        {
            if (thing == null || compType == null)
            {
                return null;
            }
            ThingWithComps twc = thing as ThingWithComps;
            if (twc == null || twc.AllComps == null)
            {
                return null;
            }
            for (int i = 0; i < twc.AllComps.Count; i++)
            {
                ThingComp comp = twc.AllComps[i];
                if (comp != null && compType.IsInstanceOfType(comp))
                {
                    return comp;
                }
            }
            return null;
        }

        private static object GetMember(object target, string name)
        {
            if (target == null || name == null)
            {
                return null;
            }
            try
            {
                Type t = target.GetType();
                PropertyInfo pi = AccessTools.Property(t, name);
                if (pi != null && pi.CanRead)
                {
                    return pi.GetValue(target, null);
                }
                FieldInfo fi = AccessTools.Field(t, name);
                return fi != null ? fi.GetValue(target) : null;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容：读取成员 " + name + " 失败（返回默认值）", e);
                return null;
            }
        }

        private static int IntMember(object target, string name)
        {
            object v = GetMember(target, name);
            if (v is int i)
            {
                return i;
            }
            if (v is float f)
            {
                return (int)f;
            }
            return 0;
        }

        private static bool BoolMember(object target, string name)
        {
            object v = GetMember(target, name);
            return v is bool b && b;
        }

        private static float FloatMember(object target, string name)
        {
            object v = GetMember(target, name);
            if (v is float f)
            {
                return f;
            }
            if (v is int i)
            {
                return i;
            }
            return 0f;
        }

        private static object InvokeMethod(object target, string name)
        {
            if (target == null)
            {
                return null;
            }
            try
            {
                MethodInfo mi = AccessTools.Method(target.GetType(), name, Type.EmptyTypes);
                return mi != null ? mi.Invoke(target, null) : null;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("CE 兼容：调用方法 " + name + " 失败（返回 null）", e);
                return null;
            }
        }

        private static bool IsOnCheckCooldown(Pawn pawn)
        {
            int until;
            if (pawn != null && checkCooldownUntil.TryGetValue(pawn.thingIDNumber, out until))
            {
                return GenTicks.TicksGame < until;
            }
            return false;
        }

        private static void NotifyChecked(Pawn pawn)
        {
            if (pawn != null)
            {
                int now = GenTicks.TicksGame;
                checkCooldownUntil[pawn.thingIDNumber] = now + CheckCooldownTicks;
                // 派活后立刻把优先级缓存压到"不可供"并顺延到冷却结束 ——
                // 否则缓存里还是 true 会让 GetPriority 反复抬高优先级、同一份取货 Job 被反复派出去。
                priorityCacheValue[pawn.thingIDNumber] = false;
                priorityCacheUntil[pawn.thingIDNumber] = now + CheckCooldownTicks;
            }
        }
    }
}
