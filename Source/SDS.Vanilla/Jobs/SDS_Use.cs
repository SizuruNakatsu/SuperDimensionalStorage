using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 「使用」命令（《用得上.md》定稿）：
    ///  - 从不自己构造"使用 Job"：只调用原版 / 第三方 provider 返回的 FloatMenuOption.action；
    ///  - 判定逻辑不复制：能不能用、为什么不能用完全由原版 provider 回答；
    ///  - 取物点固定为目标小人自身端口（建筑 / 机械体端口不参与）；
    ///  - 回充只发生在判定阶段；控制权交给 action 之后按"输出到地上"处理，不再回收。
    /// </summary>
    internal static class SDS_UseCommand
    {
        /// <summary>排除表：任务类 provider。未列出的 provider 一律纳入（含第三方）。</summary>
        private static readonly HashSet<string> ExcludedProviderNames = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "FloatMenuOptionProvider_Arrest",
            "FloatMenuOptionProvider_CapturePawn",
            "FloatMenuOptionProvider_CaptureEntity",
            "FloatMenuOptionProvider_DraftedAttack",
            "FloatMenuOptionProvider_DraftedMove",
            "FloatMenuOptionProvider_DraftedRepair",
            "FloatMenuOptionProvider_DraftedTend",
            "FloatMenuOptionProvider_CarryPawn",
            "FloatMenuOptionProvider_CarryingPawn",
            "FloatMenuOptionProvider_CarryPawnToExit",
            "FloatMenuOptionProvider_CarryToShuttle",
            "FloatMenuOptionProvider_CarryToCryptosleepCasket",
            "FloatMenuOptionProvider_CarryToBiosculpterPod",
            "FloatMenuOptionProvider_CarryDeathrestingToCasket",
            "FloatMenuOptionProvider_CarryMechToCharger",
            "FloatMenuOptionProvider_LoadCaravan",
            "FloatMenuOptionProvider_LoadOntoPackAnimal",
            "FloatMenuOptionProvider_FromZone",
            "FloatMenuOptionProvider_FromLord",
            "FloatMenuOptionProvider_PickUpItem",
            "FloatMenuOptionProvider_HandleCorpse",
            "FloatMenuOptionProvider_Deathrest",
            "FloatMenuOptionProvider_Childcare",
            "FloatMenuOptionProvider_OfferHelp",
            "FloatMenuOptionProvider_Mechanitor",
            "FloatMenuOptionProvider_BringBabyToSafety",
            "FloatMenuOptionProvider_CleanRoom",
            "FloatMenuOptionProvider_ExtinguishFires",
            "FloatMenuOptionProvider_PutOutFireOnPawn",
            "FloatMenuOptionProvider_PrisonerBloodfeed",
            "FloatMenuOptionProvider_DressOtherPawn",
            "FloatMenuOptionProvider_DropEquipment",
            // 任务类：会把「搬运」等任务项塞进候选；本 Mod 搬运补丁又会改写成 SDS_DepositToGrid。
            "FloatMenuOptionProvider_WorkGivers"
        };

        /// <summary>任务类 JobDef 前缀：出现这些就说明点错了项，取消并回充。</summary>
        private static readonly string[] TaskJobPrefixes =
        {
            "Haul", "Goto", "Attack", "Arrest", "Capture", "Carry", "Wait", "Prioritize", "LayDown"
        };

        private static List<FloatMenuOptionProvider> providers;
        private static bool providersReported;

        /// <summary>
        /// 启动时输出一次「右键菜单项来源」的纳入 / 排除计数，并校验排除表是否全部命中。
        /// 原版 provider 改名 / 移除时必须一眼可见，不静默。
        /// </summary>
        internal static void ReportProvidersOnce()
        {
            if (providersReported)
            {
                return;
            }
            providersReported = true;
            try
            {
                List<FloatMenuOptionProvider> list = EnsureProviders();
                List<string> missing = MissingExclusionNames();
                Log.Message("[超维存储] 【使用命令】右键菜单项来源：纳入 " + list.Count
                    + " 个 provider / 排除表 " + ExcludedProviderNames.Count + " 类（未列出的第三方 provider 默认纳入）"
                    + (missing.Count > 0
                        ? "；排除表未命中 " + missing.Count + " 类：" + string.Join("、", missing)
                        : "；排除表全部命中"));
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("「使用命令」provider 枚举失败（已跳过，使用命令不可用）", e);
            }
        }

        /// <summary>排除表里在运行时并不存在的类型名（原版改名 / 移除后必须一眼可见）。</summary>
        private static List<string> MissingExclusionNames()
        {
            HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<Type> types = typeof(FloatMenuOptionProvider).AllSubclassesNonAbstract();
            if (types != null)
            {
                foreach (Type type in types)
                {
                    if (type != null)
                    {
                        existing.Add(type.Name);
                    }
                }
            }
            List<string> missing = new List<string>();
            foreach (string name in ExcludedProviderNames)
            {
                if (!existing.Contains(name))
                {
                    missing.Add(name);
                }
            }
            return missing;
        }

        /// <summary>地图点选回调：点谁就谁；不是小人只给提示，不做任何扣账。</summary>
        public static void HandleTarget(LocalTargetInfo target, SDS_StockKey key, bool preserved)
        {
            try
            {
                Pawn pawn = target.Pawn;
                if (pawn == null)
                {
                    MsgKey("SDS.Use.NotPawn");
                    return;
                }
                TryUse(pawn, key, preserved);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("「使用」命令处理点选目标失败（已忽略）", e);
            }
        }

        /// <summary>门控 0 → 门控 1 → 取出 1 件到脚下 → 门控 2 → 任务项兜底。</summary>
        public static bool TryUse(Pawn pawn, SDS_StockKey key, bool preserved)
        {
            Thing landed = null;
            try
            {
                if (!IsValidTarget(pawn))
                {
                    MsgKey("SDS.Use.NotEligible");
                    return false;
                }
                if (!SDS_ImplantUtility.IsPortOperational(pawn))
                {
                    MsgKey("SDS.Use.NoPort");
                    return false;
                }
                if (SDS_Core.Ledger == null || key == null)
                {
                    MsgKey("SDS.Use.NoStock");
                    return false;
                }
                if (!key.Resolved)
                {
                    MsgRaw("SDS.Use.Unresolved".Translate(key.EffectiveDefName ?? "?").ToString());
                    return false;
                }
                if (SDS_Core.Ledger.IsForbidden(key))
                {
                    MsgKey("SDS.Forbidden.CannotUse");
                    return false;
                }
                if (SDS_Availability.GetAvailableCount(key) <= 0)
                {
                    MsgKey("SDS.Use.NoStock");
                    return false;
                }
                if (!IsUsableCategory(key.thingDef))
                {
                    MsgKey("SDS.Use.NotUsable");
                    return false;
                }

                Thing item = TakeOne(pawn, key, preserved);
                if (item == null)
                {
                    SDS_SupplyFailure failure = SDS_Availability.GetFailure(key);
                    MsgKey(failure == SDS_SupplyFailure.Forbidden
                        ? "SDS.Forbidden.CannotUse"
                        : "SDS.Use.NoStock");
                    return false;
                }
                Map map = pawn.Map;
                IntVec3 cell = pawn.PositionHeld;
                if (!SDS_PlacementUtility.TryPlaceOrSpawn(item, cell, map, out landed)
                    || landed == null)
                {
                    Refund(item, map, cell, preserved);
                    MsgKey("SDS.Use.NoPlace");
                    return false;
                }
                // 交给原版使用前：清 Forbidden（原版自动穿衣 / 拾取会跳过）；打来源标记防抢。
                // 落点被并入玩家已有堆时两者都不做（不能连带保护 / 解禁玩家原有的那一堆）。
                if (ReferenceEquals(landed, item))
                {
                    landed.SetForbidden(false);
                    SDS_Rules.MarkProduced(landed);
                }
                return DecideAndRun(pawn, landed, map, preserved);
            }
            catch (Exception e)
            {
                // W49：物品已取出并落地，失败退路必须撤销在途标记，不得让它以「在途」身份滞留。
                // 不 Refund：实物已在地图上，回充会造成复制。
                if (landed != null)
                {
                    SDS_Rules.UnmarkProduced(landed);
                }
                SDS_Diag.ErrorOnce("「使用」命令执行失败（物品="
                    + (landed != null ? landed.LabelShort : "?") + "；已忽略，原版行为不受影响）", e);
                return false;
            }
        }

        private static Thing TakeOne(Pawn pawn, SDS_StockKey key, bool preserved)
        {
            if (preserved)
            {
                return SDS_PreservedUsage.TryWithdrawOne(key, null, out Thing thing) ? thing : null;
            }
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery
                {
                    key = key,
                    quantity = 1
                },
                endpoint = SDS_EndpointHandle.ForPawn(pawn, true),
                mode = SDS_WithdrawMode.SpawnedTarget
            };
            return SDS_Rules.TryWithdraw(plan, pawn, out List<Thing> made)
                && made != null && made.Count > 0
                ? made[0]
                : null;
        }

        // ---------------- 门控 2 / 任务项兜底 ----------------

        private static bool DecideAndRun(Pawn pawn, Thing landed, Map map, bool preserved)
        {
            FloatMenuContext context = new FloatMenuContext(new List<Pawn> { pawn },
                landed.DrawPos, map);
            FloatMenuOption chosen = null;
            string reason = null;

            foreach (FloatMenuOptionProvider provider in EnsureProviders())
            {
                List<FloatMenuOption> options = CollectOptions(provider, landed, context);
                if (options == null || options.Count == 0)
                {
                    continue;
                }
                for (int i = 0; i < options.Count; i++)
                {
                    FloatMenuOption option = options[i];
                    if (option == null)
                    {
                        continue;
                    }
                    if (option.action != null)
                    {
                        if (chosen == null)
                        {
                            chosen = option;
                        }
                    }
                    else if (reason == null && !option.Label.NullOrEmpty())
                    {
                        // 灰项 = 原版给出的"为什么不能用"，原文照搬（门控 2）。
                        reason = option.Label;
                    }
                }
                if (chosen != null)
                {
                    break;
                }
            }

            if (chosen == null)
            {
                Refund(landed, map, pawn.PositionHeld, preserved);
                if (!reason.NullOrEmpty())
                {
                    MsgRaw("SDS.Use.Blocked".Translate(landed.LabelShort, reason).ToString());
                }
                else
                {
                    MsgKey("SDS.Use.NotUsable");
                }
                return false;
            }

            Job prevCur = pawn.CurJob;
            int prevQueue = pawn.jobs != null && pawn.jobs.jobQueue != null
                ? pawn.jobs.jobQueue.Count
                : 0;
            try
            {
                chosen.action();
            }
            catch (Exception e)
            {
                // 已交给原版：按"输出到地上"处理，不回收。
                SDS_Diag.ErrorOnce("「使用」命令调用原版菜单项失败（物品已在地上）", e);
                return false;
            }

            Job created = ResolveCreatedJob(pawn, prevCur, prevQueue);
            if (created == null)
            {
                // 纯 UI 动作（只弹窗 / 打开终端）：不回充。
                return true;
            }
            if (created.def == null)
            {
                // 第三方 / 异常 Job 可能没有 JobDef：按「未知使用类」处理，保留落地并撤销在途标记。
                SDS_Rules.UnmarkProduced(landed);
                Log.WarningOnce("[超维存储] 【使用命令】创建了无 JobDef 的任务，已按未知使用类处理："
                    + (landed != null ? landed.LabelShort : "?"), 0x5D5D0011);
                return true;
            }
            if (IsTaskJob(created.def))
            {
                if (ReferenceEquals(created, pawn.CurJob))
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                Refund(landed, map, pawn.PositionHeld, preserved);
                MsgRaw("SDS.Use.TaskJob".Translate(landed.LabelShort, created.def.label).ToString());
                return false;
            }
            if (!IsKnownUseJob(created.def))
            {
                Log.WarningOnce("[超维存储] 【使用命令】未知使用类 JobDef：" + created.def.defName
                    + "（已按使用处理；如需排除请更新《用得上.md》§五）", created.def.shortHash);
            }
            return true;
        }

        /// <summary>镜像 FloatMenuMakerMap.GetProviderOptions 的单目标分支。</summary>
        private static List<FloatMenuOption> CollectOptions(FloatMenuOptionProvider provider,
            Thing thing, FloatMenuContext context)
        {
            FloatMenuMakerMap.currentProvider = provider;
            try
            {
                bool anyValid = false;
                foreach (Pawn _ in context.ValidSelectedPawns)
                {
                    anyValid = true;
                    break;
                }
                if (!anyValid || !provider.Applies(context))
                {
                    return null;
                }
                if (!provider.TargetThingValid(thing, context))
                {
                    return null;
                }
                return new List<FloatMenuOption>(provider.GetOptionsFor(thing, context));
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("「使用命令」provider 求值失败（已跳过）："
                    + provider.GetType().Name, e);
                return null;
            }
            finally
            {
                FloatMenuMakerMap.currentProvider = null;
            }
        }

        private static Job ResolveCreatedJob(Pawn pawn, Job prevCur, int prevQueue)
        {
            if (pawn == null || pawn.jobs == null)
            {
                return null;
            }
            if (pawn.CurJob != null && !ReferenceEquals(pawn.CurJob, prevCur))
            {
                return pawn.CurJob;
            }
            if (pawn.jobs.jobQueue != null && pawn.jobs.jobQueue.Count > prevQueue)
            {
                QueuedJob queued = pawn.jobs.jobQueue.Peek();
                return queued != null ? queued.job : null;
            }
            return null;
        }

        // ---------------- 判定辅助 ----------------

        private static bool IsValidTarget(Pawn pawn)
        {
            return pawn != null && pawn.Spawned && pawn.Map != null && !pawn.Dead
                && pawn.Faction == Faction.OfPlayer && pawn.RaceProps != null
                && pawn.RaceProps.Humanlike && !pawn.RaceProps.IsMechanoid
                && !pawn.InMentalState;
        }

        /// <summary>门控 1：def 级类别白名单（不 spawn 就能拒绝）。</summary>
        private static bool IsUsableCategory(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }
            if (def.ingestible != null && def.ingestible.showIngestFloatOption)
            {
                return true;
            }
            if (def.IsApparel)
            {
                return true;
            }
            if (def.HasAssignableCompFrom(typeof(CompEquippable)))
            {
                return true;
            }
            if (def.HasAssignableCompFrom(typeof(CompUsable)))
            {
                return true;
            }
            return def.thingClass != null && typeof(IOpenable).IsAssignableFrom(def.thingClass);
        }

        private static bool IsTaskJob(JobDef def)
        {
            if (def == null || def.defName == null)
            {
                return false;
            }
            // 本 Mod 的存入 Job 也是任务类：把"使用"变成"收回去"，必须取消并回充。
            if (SDS_JobDefOf.DepositToGrid != null && def == SDS_JobDefOf.DepositToGrid)
            {
                return true;
            }
            for (int i = 0; i < TaskJobPrefixes.Length; i++)
            {
                if (def.defName.StartsWith(TaskJobPrefixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsKnownUseJob(JobDef def)
        {
            if (def == null)
            {
                return false;
            }
            return def == JobDefOf.Ingest || def == JobDefOf.Wear
                || def == JobDefOf.Equip || def == JobDefOf.Open
                || string.Equals(def.defName, "UseItem", StringComparison.Ordinal);
        }

        private static void Refund(Thing thing, Map map, IntVec3 cell, bool preserved)
        {
            if (thing == null || thing.Destroyed)
            {
                return;
            }
            bool returned = SDS_Rules.ReturnThing(thing,
                preserved ? SDS_StockChannel.Preserved : SDS_StockChannel.Virtual,
                map, null, SDS_ReturnFallback.None);
            if (!returned)
            {
                SDS_PlacementUtility.TryPlaceOrSpawn(thing, cell, map);
                if (thing.Spawned)
                {
                    MsgRaw("SDS.Use.RefundFailed".Translate(thing.LabelShort).ToString());
                }
            }
        }

        private static void MsgKey(string key)
        {
            Messages.Message(key.Translate(), MessageTypeDefOf.RejectInput, false);
        }

        private static void MsgRaw(string text)
        {
            Messages.Message(text, MessageTypeDefOf.RejectInput, false);
        }

        private static List<FloatMenuOptionProvider> EnsureProviders()
        {
            if (providers != null)
            {
                return providers;
            }
            providers = new List<FloatMenuOptionProvider>();
            IEnumerable<Type> types = typeof(FloatMenuOptionProvider).AllSubclassesNonAbstract();
            if (types == null)
            {
                return providers;
            }
            foreach (Type type in types)
            {
                if (type == null || ExcludedProviderNames.Contains(type.Name))
                {
                    continue;
                }
                try
                {
                    FloatMenuOptionProvider provider = Activator.CreateInstance(type)
                        as FloatMenuOptionProvider;
                    if (provider != null)
                    {
                        providers.Add(provider);
                    }
                }
                catch (Exception e)
                {
                    SDS_Diag.ErrorOnce("「使用命令」provider 实例化失败（已跳过）：" + type.FullName, e);
                }
            }
            // 使用类 provider 优先：保证穿衣 / 装备 / 进食 / 使用物品 / 打开先被尝试。
            providers.Sort((a, b) => ProviderRank(a).CompareTo(ProviderRank(b)));
            return providers;
        }

        private static int ProviderRank(FloatMenuOptionProvider provider)
        {
            string name = provider != null ? provider.GetType().Name : string.Empty;
            switch (name)
            {
                case "FloatMenuOptionProvider_Wear": return 0;
                case "FloatMenuOptionProvider_Equip": return 1;
                case "FloatMenuOptionProvider_Ingest": return 2;
                case "FloatMenuOptionProvider_UseItem": return 3;
                case "FloatMenuOptionProvider_Open": return 4;
                case "FloatMenuOptionProvider_FromThing": return 5;
                default: return 10;
            }
        }
    }
}
