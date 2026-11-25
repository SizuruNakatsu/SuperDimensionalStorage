using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 交付策略解析（Defs/SDS_JobDefs.xml 的 SDS_WithdrawDeliveryPolicyDef）：
    /// 按 jobDefName 主键、driverType 兜底；未登记一律 Hand（W30）。
    /// VirtualHandoff 不适用于物理取出（W3），误登记也会被跳过并回落默认值。
    /// Def 是进程级常量，不随存档变化，不登记 ResetAll。
    /// </summary>
    internal static class SDS_WithdrawDeliveryPolicy
    {
        private static bool built;
        private static readonly Dictionary<string, SDS_WithdrawMode> byJobName =
            new Dictionary<string, SDS_WithdrawMode>(StringComparer.Ordinal);
        private static readonly Dictionary<string, SDS_WithdrawMode> byDriver =
            new Dictionary<string, SDS_WithdrawMode>(StringComparer.Ordinal);

        internal static SDS_WithdrawMode ModeFor(Job job)
        {
            if (job == null || job.def == null)
            {
                return SDS_WithdrawMode.SpawnedTarget;
            }
            EnsureBuilt();
            if (!string.IsNullOrEmpty(job.def.defName)
                && byJobName.TryGetValue(job.def.defName, out SDS_WithdrawMode mode))
            {
                return mode;
            }
            Type driver = job.def.driverClass;
            if (driver != null && byDriver.TryGetValue(driver.FullName ?? driver.Name, out mode))
            {
                return mode;
            }
            // W30：默认交付形态 = Hand。镜像目标不在派活期物化，取真身发生在消费接缝
            // （W58 数量补齐 / W59 消费点物化 / 取货原语）。只有显式登记为 SpawnedTarget
            // 的 Job 才走逃生门（当前仅瓶喂），逃生门内 count<=0 仍按 1 件。
            return SDS_WithdrawMode.Hand;
        }

        private static void EnsureBuilt()
        {
            if (built)
            {
                return;
            }
            built = true;
            List<SDS_WithdrawDeliveryPolicyDef> defs =
                DefDatabase<SDS_WithdrawDeliveryPolicyDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                SDS_WithdrawDeliveryPolicyDef def = defs[i];
                if (def == null || def.mode == SDS_WithdrawMode.VirtualHandoff)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(def.jobDefName))
                {
                    byJobName[def.jobDefName] = def.mode;
                }
                if (!string.IsNullOrEmpty(def.driverType))
                {
                    Type type = GenTypes.GetTypeInAnyAssembly(def.driverType);
                    if (type != null)
                    {
                        byDriver[type.FullName ?? type.Name] = def.mode;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Job 执行层适配（C-PA）：只在必要时物化镜像目标，不改 JobDef / Toil 序列。
    ///
    ///  - 搬运 / 取料类（队列目标、HaulToCell / HaulToContainer、Hand / Container 交付策略）
    ///    保留镜像目标，由取货原语接管（GotoThing / TryStartCarry / ThingOwner）。
    ///  - 其余「直接消费目标」在 StartJob 时物化到最近端点访问点：
    ///    1. SDS_Rules.SelectEndpoint 选端点；
    ///    2. SDS_Rules.TryTakeMirror 扣账并取出真身；
    ///    3. SDS_PlacementUtility 落地；
    ///    4. Job 目标改指这件真实物。
    /// 目标不是镜像时零操作；物化失败保持原样（可达性补丁会让 Job 像"目标不存在"一样结束）。
    /// </summary>
    internal static class SDS_JobTargetAdapter
    {
        public static void Rewrite(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || !pawn.Spawned || pawn.Map == null)
            {
                return;
            }
            // S18：非玩家派系不把隐藏库存物化成真身。
            if (SDS_AccessRules.BlocksNonPlayer(pawn))
            {
                return;
            }
            if (job.def == JobDefOf.HaulToCell || job.def == JobDefOf.HaulToContainer)
            {
                return;
            }
            // 交付策略：Hand / Container 交给取货原语；只有 SpawnedTarget 在这里落地。
            if (SDS_WithdrawDeliveryPolicy.ModeFor(job) != SDS_WithdrawMode.SpawnedTarget)
            {
                return;
            }
            Dictionary<Thing, Thing> replaced = null;

            bool replacedA = RewriteSlot(pawn, ref job.targetA,
                TakeCountForSlot(pawn, job, TargetIndex.A), job, ref replaced);
            // B / C 的数量已由 TakeCountForSlot 按槽位算准；job.count 只描述 targetA。
            RewriteSlot(pawn, ref job.targetB,
                TakeCountForSlot(pawn, job, TargetIndex.B), job, ref replaced);
            RewriteSlot(pawn, ref job.targetC,
                TakeCountForSlot(pawn, job, TargetIndex.C), job, ref replaced);

            if (replacedA && job.count > 0 && job.targetA.HasThing
                && job.targetA.Thing != null && job.targetA.Thing.stackCount < job.count)
            {
                job.count = job.targetA.Thing.stackCount;
            }
        }

        /// <summary>
        /// Job 启动后释放该 Job 对镜像的预留：镜像代表整个品类，
        /// 预留到 Job 结束会把同品类其它 Job 全部锁死；真正扣账在 TryTakeMirror 按数量原子完成。
        /// W58/W59 会在 Notify_Starting 里把目标换成真身，因此这里按“本 Job 的全部预留”扫，
        /// 不能只看当前 targetA/B/C（否则旧镜像引用被换掉后预留会残留到 Job 结束）。
        /// </summary>
        public static void ReleaseMirrorReservations(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || pawn.Map == null)
            {
                return;
            }
            ReservationManager manager = pawn.Map.reservationManager;
            if (manager == null)
            {
                return;
            }
            List<ReservationManager.Reservation> reservations = manager.ReservationsReadOnly;
            for (int i = reservations.Count - 1; i >= 0; i--)
            {
                ReservationManager.Reservation reservation = reservations[i];
                if (reservation == null || !ReferenceEquals(reservation.Claimant, pawn)
                    || !ReferenceEquals(reservation.Job, job))
                {
                    continue;
                }
                Thing mirror = reservation.Target.Thing;
                if (mirror == null || !SDS_MirrorRegistry.IsMirror(mirror))
                {
                    continue;
                }
                manager.Release(mirror, pawn, job);
            }
        }

        /// <summary>
        /// 逃生门（显式登记为 SpawnedTarget）的取量口径（《规则索引.md》W30）。
        /// 通用 = max(1, job.count)；唯一保留的缺口取分支是瓶喂（营养缺口、库存交付不夹携带量）。
        /// 非逃生门 Job 不在派活期物化，数量由 W58（预留意图）/ 消费接缝自行决定。
        /// </summary>
        // RULE:W30
        private static int TakeCountForSlot(Pawn pawn, Job job, TargetIndex ind)
        {
            int baseCount = job != null && job.count > 0 ? job.count : 1;
            if (job == null)
            {
                return baseCount;
            }
            if (job.def == JobDefOf.BottleFeedBaby && ind == TargetIndex.B)
            {
                Pawn baby = job.GetTarget(TargetIndex.A).Thing as Pawn;
                Thing food = job.GetTarget(TargetIndex.B).Thing;
                if (baby != null && food != null && baby.needs != null && baby.needs.food != null)
                {
                    float nutrition = FoodUtility.NutritionForEater(baby, food);
                    if (nutrition > 0f)
                    {
                        return Math.Max(1, FoodUtility.WillIngestStackCountOf(baby, food.def, nutrition));
                    }
                }
                return baseCount;
            }
            return baseCount;
        }

        private static bool RewriteSlot(Pawn pawn, ref LocalTargetInfo target, int count, Job job,
            ref Dictionary<Thing, Thing> replaced)
        {
            Thing mirror = target.Thing;
            if (mirror == null || !SDS_MirrorRegistry.IsMirror(mirror))
            {
                return false;
            }
            // W30：本趟无可携带量 / 无缺口时不接管，绝不回落成 1 件。
            if (count <= 0)
            {
                return false;
            }
            if (replaced != null && replaced.TryGetValue(mirror, out Thing existing)
                && existing != null && !existing.Destroyed)
            {
                target = new LocalTargetInfo(existing);
                return true;
            }
            if (!SDS_Rules.TryGetStockKey(mirror, out SDS_StockKey key)
                || key == null || key.preserved || !SDS_Rules.Available)
            {
                return false;
            }
            // W30：取量 = min(需求, 镜像总量) 后再按 W56 夹到可用量；
            // 可用量只认 SDS_Availability（W22），拿物理总量当可用量会让 TryReserve 整笔失败。
            int want = SDS_Availability.ClampTakeToAvailable(Math.Min(count, mirror.stackCount),
                SDS_Availability.GetAvailableCount(key));
            if (want <= 0)
            {
                return false;
            }
            // 端点 / 交付模式来自统一决议入口，不在这里遍历端点。
            SDS_EndpointQuery query = default;
            query.ApplyHostPriority();
            query.op = SDS_EndpointOp.Withdraw;
            query.map = pawn.Map;
            query.actor = pawn;
            query.reference = pawn.Position;
            query.key = key;
            query.danger = pawn.NormalMaxDanger();
            query.requireReach = true;
            query.allowSelf = true;
            query.requireStock = true;
            SDS_EndpointDecision decision = SDS_Rules.SelectEndpoint(query);
            if (!decision.allowed || decision.port == null)
            {
                return false;
            }
            if (!SDS_Rules.TryTakeMirror(mirror, want, SDS_Host.MirrorPhysical, out Thing real)
                || real == null || real.Destroyed)
            {
                return false;
            }
            IntVec3 cell = decision.port.AccessCell;
            if (!SDS_PlacementUtility.TryPlaceOrSpawn(real, cell, pawn.Map, out Thing landed)
                || landed == null)
            {
                // 落不下去也必须回账本（数量守恒），失败才交给落地兜底。
                SDS_Rules.ReturnThing(real, SDS_StockChannel.Virtual, pawn.Map, pawn,
                    SDS_ReturnFallback.Ground);
                return false;
            }
            // 只释放本 Job 自己对镜像的预留，不动其它小人 / Mod 的预留。
            if (mirror.Spawned && job != null
                && pawn.Map.reservationManager.ReservedBy(mirror, pawn, job))
            {
                pawn.Map.reservationManager.Release(mirror, pawn, job);
            }
            // 登记 Job 目标：失败 / 打断时由 Patch_SDS_ReclaimJobTargets 回充。
            SDS_ReclaimRegistry.TrackJobTarget(job, landed, landed.stackCount);
            if (replaced == null)
            {
                replaced = new Dictionary<Thing, Thing>();
            }
            replaced[mirror] = landed;
            target = new LocalTargetInfo(landed);
            return true;
        }
    }

    /// <summary>Job 真正开始时改写镜像目标（不改 JobDef / Toil）。</summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_JobMirrorTargets
    {
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            try
            {
                if (newJob == null)
                {
                    return;
                }
                if (Current.ProgramState != ProgramState.Playing || !SDS_MirrorService.AnyMirrorInWorld)
                {
                    return;
                }
                SDS_JobTargetAdapter.Rewrite(___pawn, newJob);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("Job 镜像目标改写失败（已忽略，原版行为不受影响）", e);
            }
        }

        /// <summary>
        /// StartJob 内部已跑完 TryMakePreToilReservations：此时释放对镜像的预留，只保留实物预留。
        /// 这样同品类的多个制作 / 建造 Job 不会互相把整批品类锁死。
        /// </summary>
        public static void Postfix(Job newJob, Pawn ___pawn)
        {
            try
            {
                if (newJob == null)
                {
                    return;
                }
                if (Current.ProgramState != ProgramState.Playing || !SDS_MirrorService.AnyMirrorInWorld)
                {
                    return;
                }
                SDS_JobTargetAdapter.ReleaseMirrorReservations(___pawn, newJob);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("释放镜像预留失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>
    /// 最后一道闸：绝不让镜像被「直接穿」进 wornApparel。
    /// 正常路径由 SDS_JobTargetAdapter 在 StartJob 时物化成真身；物化失败时拒绝穿戴，
    /// 避免"镜像被 DeSpawn 后加入 wornApparel、账本却没扣"的复制。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_NoWearMirror
    {
        public static bool Prefix(Apparel newApparel)
        {
            try
            {
                return newApparel == null || !SDS_MirrorRegistry.Any
                    || !SDS_MirrorRegistry.IsMirror(newApparel);
            }
            catch (Exception e)
            {
                // 守卫异常时放行原版（不改变原版行为），绝不把异常漏回 Wear 调用链。
                SDS_Diag.ErrorOnce("镜像穿戴守卫失败（已放行原版行为）", e);
                return true;
            }
        }
    }

    /// <summary>
    /// 通用规则（W54）：任何 Job 的 Toils_Goto.GotoThing 若目标是镜像，寻路改到最近访问点；
    /// 解析不到任何可用端点时按“目标不可达”结束 Job——镜像真身位置（隐藏金库）永远不是取货点。
    /// 只包一层 initAction，其余情况原样调用原 initAction；没有镜像时零行为变化。
    /// </summary>
    [HarmonyPatch(typeof(Toils_Goto), nameof(Toils_Goto.GotoThing),
        new[] { typeof(TargetIndex), typeof(PathEndMode), typeof(bool) })]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorTargetGoto
    {
        public static void Postfix(TargetIndex ind, PathEndMode peMode, ref Toil __result)
        {
            if (__result == null || __result.initAction == null || !SDS_MirrorRegistry.Any)
            {
                return;
            }
            Toil toil = __result;
            Action original = toil.initAction;
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job job = actor != null && actor.jobs != null ? actor.jobs.curJob : null;
                Thing target = job != null ? job.GetTarget(ind).Thing : null;
                if (target == null || !SDS_MirrorRegistry.IsMirror(target))
                {
                    original();
                    return;
                }
                try
                {
                    if (SDS_AccessPointResolver.TryGetNearestReachable(actor, out SDS_AccessPoint point)
                        && point != null)
                    {
                        actor.pather.StartPath(new LocalTargetInfo(point.cell), peMode);
                        return;
                    }
                }
                catch (Exception e)
                {
                    SDS_Diag.ErrorOnce("Toils_Goto.GotoThing 镜像寻路改写失败（已按目标不可达结束）", e);
                }
                // W54：没有可用端点时不得回落到真身坐标（隐藏金库），按目标不存在结束 Job。
                if (actor != null && actor.jobs != null)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                }
            };
        }
    }

    /// <summary>
    /// Job 结束 / 被替换 / 被挂起时的统一收口（Pawn_JobTracker.CleanupCurrentJob）：
    /// 存入 Job 清理 + 手上料 / 地面目标的自动回充（W27）。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), "CleanupCurrentJob")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_ReclaimJobTargets
    {
        public static void Prefix(Pawn ___pawn, Job ___curJob, JobCondition condition,
            bool releaseReservations, out int __state)
        {
            __state = 0;
            try
            {
                SDS_DepositJobCleanup.OnCleanup(___pawn, ___curJob, condition, releaseReservations);
                if (___curJob == null || !releaseReservations)
                {
                    return;
                }
                // W63：Job 真正结束 / 被替换时清掉落点登记；挂起不清理。
                SDS_CellOnlyDestinationFold.Clear(___curJob);
                // 先回收仍在手上的材料：必须在原版 cleanup 掉落之前。
                SDS_ReclaimRegistry.ReclaimHand(___pawn, ___curJob.loadID);
                if (condition == JobCondition.Succeeded)
                {
                    SDS_ReclaimRegistry.ForgetJobTarget(___curJob.loadID);
                    return;
                }
                __state = ___curJob.loadID;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("Job 清理失败（已忽略，原版行为不受影响）", e);
            }
        }

        public static void Postfix(int __state)
        {
            if (__state == 0)
            {
                return;
            }
            try
            {
                SDS_ReclaimRegistry.ReclaimJobTarget(__state);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("Job 目标回充失败（已忽略，实物留在地图）", e);
            }
        }
    }
}
