using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 品类规则：物品 → 键 / 通道、可存判定、容量口径、堆平均状态与物化。
    /// 只做纯判定与数据换算，不寻路、不派活、不触碰原版 UI。
    /// </summary>
    internal static class SDS_StockRules
    {
        // 进程级 Def 查询缓存：键为 ThingDef，值只依赖 def 静态数据与 ignoreArt 字典选择；
        // ForceWeaponApparelVirtual 只决定走哪本字典，因此不登记 SDS_StaticState（规则 §九）。
        private static readonly Dictionary<ThingDef, bool> rebuildCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> gearRebuildCache = new Dictionary<ThingDef, bool>();

        /// <summary>
        /// 外部保真分类输入：由组合根注入（第三方「必须保真」事实），Core 不引用任何模块。
        /// 入：thing；出：true = 额外要求保真。为空时只按 Core 自身判据。
        /// 区别：它是唯一的外部分类入口，只允许把物品「加严」为保真，不能放宽 Core 判据。
        /// </summary>
        internal static Func<Thing, bool> ExternalPreserveProvider;

        private static readonly string[] virtualDefaultCategoryDefNames =
        {
            "Manufactured", "ResourcesRaw", "Foods", "Chunks",
        };

        // ---------- 键 ----------

        /// <summary>实例分流入口：按是否需要保真选择两种取键口径之一；null 返回 null。</summary>
        /// <remarks>入：thing = 实例。出：虚拟键或保真键。
        /// 区别：与 SDS_StockData.FromThing / FromPreservedThing 的区别是它包含通道判定（RequiresPreservedStorage 实例重载）；
        ///       与 SDS_RulesImpl.TryGetStockKey 的区别是不看「是否已在保真账本中」。</remarks>
        public static SDS_StockKey FromAnyThing(Thing thing)
        {
            if (thing == null)
            {
                return null;
            }
            return FromAnyThing(thing, RequiresPreservedStorage(thing));
        }

        /// <summary>实例分流入口（通道已知）：避免调用方重复执行实例级保真判定。</summary>
        /// <remarks>入：thing = 实例；preserved = 已由本类判定的通道。出：虚拟键或保真键。
        /// 区别：与单参重载的唯一差别是复用 preserved，不再次执行 RequiresPreservedStorage(thing)。</remarks>
        public static SDS_StockKey FromAnyThing(Thing thing, bool preserved)
        {
            if (thing == null)
            {
                return null;
            }
            return preserved
                ? SDS_StockData.FromPreservedThing(thing)
                : SDS_StockData.FromThing(thing);
        }

        // ---------- 可存判定 ----------

        /// <summary>实例口径的可存判定（回填通道）：类型可存 + 通道规则 + 全局存入筛选。</summary>
        /// <remarks>入：thing = 实例，可 null。出：true = 可存入（虚拟通道还要求 EverHaulable；宿主筛选必须放行）；preserved = 本次通道判定结果。
        /// 区别：与 SDS_CoreImpl.CanDeposit 的区别是不判容量与全局可用性；回填 preserved 供调用方复用通道判定。</remarks>
        public static bool CanDeposit(Thing thing, out bool preserved)
        {
            preserved = false;
            if (!SDS_StockData.CanStore(thing))
            {
                return false;
            }
            preserved = RequiresPreservedStorage(thing);
            if (!preserved && !thing.def.EverHaulable)
            {
                return false;
            }
            ISDS_DepositFilter filter = SDS_Host.DepositFilter;
            return filter == null || filter.Allows(thing);
        }

        // ---------- 通道分流（B1 黑名单制） ----------

        /// <summary>def 级通道判定：该 def 是否必须走保真（B1 黑名单制的反向结果）。</summary>
        /// <remarks>入：def = 目标 def，可 null。出：true = 不可重建（须保真）。
        /// 区别：与实例重载的区别是不看染色 / 充能 / 内容物 / quest / 圣物；带两层缓存（按 ignoreArt 分流）。</remarks>
        public static bool RequiresPreservedStorage(ThingDef def)
        {
            if (def == null || def.category != ThingCategory.Item || SDS_StockData.IsUnfinishedDef(def))
            {
                return false;
            }
            // W33：开关开启时只忽略 CompArt 这一条 def 级外观判据；其余保真判据不放宽。
            bool ignoreArt = SDS_Config.ForceWeaponApparelVirtual && IsGearDef(def);
            return !CanRebuildDef(def, ignoreArt);
        }

        /// <summary>实例级通道判定：def 规则 + 受保护实例 + 实例非键状态 + 兼容层钩子。</summary>
        /// <remarks>入：thing = 实例，可 null。出：true = 该实例必须保真。
        /// 区别：与 def 重载的区别是可查到染色 / 充能 / 内容物 / 圣物等实例状态；两者对同一实例可能不同（本方法更严）。</remarks>
        public static bool RequiresPreservedStorage(Thing thing)
        {
            if (thing == null)
            {
                return false;
            }
            bool ignoreGearState = SDS_Config.ForceWeaponApparelVirtual && IsGearDef(thing.def);
            return RequiresPreservedStorage(thing.def)
                || IsProtectedThing(thing, ignoreGearState)
                || HasInstanceNonKeyState(thing, ignoreGearState)
                || (ExternalPreserveProvider != null && ExternalPreserveProvider(thing));
        }

        // RULE:W35
        /// <summary>
        /// 是否逐件跟踪型 def（当前 = 可研究物）：凡此类必须走保真，不允许合并成虚拟件数。
        /// 入：def = 目标 def，可 null。出：true = 逐件跟踪。
        /// 区别：是 RequiresPreservedStorage 的子判据之一；W35 自检据此校验。
        /// </summary>
        public static bool IsTrackedPerInstance(ThingDef def)
        {
            return def != null && def.IsStudiable;
        }

        /// <summary>是否武器 / 衣物（仅物品类）；决定「强制虚拟化」开关是否适用。纯读。</summary>
        /// <remarks>入：def = 目标 def，可 null。出：true = 武器或衣物。区别：只看 def 分类，不看实例状态。</remarks>
        public static bool IsGearDef(ThingDef def)
        {
            return def != null && def.category == ThingCategory.Item && (def.IsWeapon || def.IsApparel);
        }

        /// <summary>
        /// W34：该保真真身是否可在「武器/装备强制虚拟化」开启后迁到虚拟通道。
        /// 只做判定，不入账；迁移专用入账在 SDS_Ledger（绕过容量 / 筛选 / 可用性门）。
        /// </summary>
        /// <remarks>入：thing = 保真真身。出：true = 可迁移（开关开启 + 武器衣物 + 可销毁可搬运 + 开开关后不再需保真）。
        /// 区别：与 RequiresPreservedStorage 互补——本方法要求「迁移后无需保真」，否则拒绝。</remarks>
        public static bool CanMigratePreservedGearToVirtual(Thing thing)
        {
            if (!SDS_Config.ForceWeaponApparelVirtual || thing == null || thing.Destroyed
                || thing.stackCount <= 0 || thing.def == null)
            {
                return false;
            }
            if (!IsGearDef(thing.def) || !thing.def.destroyable || !thing.def.EverHaulable)
            {
                return false;
            }
            // 开关开启后仍需保真的（身份 / 经济 / 战斗状态）一律不动。
            return !RequiresPreservedStorage(thing);
        }

        /// <summary>旧口径不可重建黑名单：尸体 / 缩装 / 未完成 / 基因载体；纯读。</summary>
        /// <remarks>入：def = 目标 def（null 视为命中）。出：true = 命中黑名单。
        /// 区别：是 CanRebuildDef 的第一条否决项；不包含逐件跟踪与 def 级非键状态。</remarks>
        public static bool IsLegacyUnrebuildableDef(ThingDef def)
        {
            if (def == null)
            {
                return true;
            }
            Type cls = def.thingClass;
            return cls != null
                && (typeof(Corpse).IsAssignableFrom(cls)
                    || typeof(MinifiedThing).IsAssignableFrom(cls)
                    || typeof(UnfinishedThing).IsAssignableFrom(cls)
                    || typeof(GeneSetHolderBase).IsAssignableFrom(cls));
        }

        /// <summary>是否默认虚拟品类（Manufactured / ResourcesRaw / Foods / Chunks 子树）；纯读。</summary>
        /// <remarks>入：def = 目标 def，可 null。出：true = 属于默认虚拟品类。
        /// 区别：命中者可跳过 def 级非键状态检查（见 CanRebuildDef），但仍受黑名单 / 逐件跟踪约束。</remarks>
        public static bool IsVirtualDefaultCategory(ThingDef def)
        {
            return InAnyCategoryTree(def, virtualDefaultCategoryDefNames);
        }

        /// <summary>
        /// 在 def 的品类树（含父级）中查找给定 defName 列表；纯读。
        /// 入：def = 目标 def；categoryDefNames = 目标品类名数组。
        /// 出：true = 任一祖先命中。区别：只做树遍历匹配，不判 def 自身状态。
        /// </summary>
        private static bool InAnyCategoryTree(ThingDef def, string[] categoryDefNames)
        {
            if (def == null || def.thingCategories == null || def.thingCategories.Count == 0)
            {
                return false;
            }
            for (int c = 0; c < def.thingCategories.Count; c++)
            {
                ThingCategoryDef cat = def.thingCategories[c];
                while (cat != null)
                {
                    for (int i = 0; i < categoryDefNames.Length; i++)
                    {
                        if (cat.defName == categoryDefNames[i])
                        {
                            return true;
                        }
                    }
                    cat = cat.parent;
                }
            }
            return false;
        }

        /// <summary>
        /// def 能否走虚拟通道的完整判定：不在黑名单 + 非逐件跟踪 + （默认虚拟品类 或 无 def 级非键状态）；
        /// 结果按 ignoreArt 分两个字典缓存（Def 为静态数据，缓存无需清理）。
        /// 入：def = 目标 def；ignoreArt = true 时忽略 CompArt 这一条（武器衣物强制虚拟化专用）。
        /// 出：true = 可重建。
        /// 区别：是本文件唯一的「可虚拟化」真源；RequiresPreservedStorage(def) 即它的取反。
        /// </summary>
        private static bool CanRebuildDef(ThingDef def, bool ignoreArt)
        {
            if (def == null)
            {
                return false;
            }
            Dictionary<ThingDef, bool> cache = ignoreArt ? gearRebuildCache : rebuildCache;
            if (cache.TryGetValue(def, out bool cached))
            {
                return cached;
            }
            bool result = !IsLegacyUnrebuildableDef(def)
                && !IsTrackedPerInstance(def)
                && (IsVirtualDefaultCategory(def) || !HasDefLevelNonKeyState(def, ignoreArt));
            cache[def] = result;
            return result;
        }

        /// <summary>
        /// def 自身携带、无法由键还原的状态：艺术 / 书籍 / 单件武器（bladelink / generatedNames / unique）。
        /// 入：def = 目标 def；ignoreArt = true 时跳过 CompArt。
        /// 出：true = 存在此类状态，必须保真。
        /// 区别：与 HasInstanceNonKeyState 的区别是只看 def 组件，不看实例取值（实例状态可能为空）。
        /// </summary>
        private static bool HasDefLevelNonKeyState(ThingDef def, bool ignoreArt)
        {
            return (!ignoreArt && def.HasComp<CompArt>())
                || def.HasComp<CompBook>()
                || def.HasComp<CompBladelinkWeapon>()
                || def.HasComp<CompGeneratedNames>()
                || def.HasComp<CompUniqueWeapon>();
        }

        /// <summary>
        /// 实例上无法由键还原的状态：染色 / 风格 / 充能 / 内容物 / 燃料 / 武器特性 / 生物编码 / 尸体来源衣物。
        /// 入：thing = 实例；ignoreGearState = true 时跳过「武器衣物专属」的三项（染色 / 风格 / 充能）。
        /// 出：true = 存在实例状态，必须保真。
        /// 区别：默认虚拟品类与黑名单 def 直接返回 false（这些走 def 级规则，不再看实例）。
        /// </summary>
        private static bool HasInstanceNonKeyState(Thing thing, bool ignoreGearState)
        {
            ThingDef def = thing != null ? thing.def : null;
            if (def == null || IsVirtualDefaultCategory(def) || IsLegacyUnrebuildableDef(def))
            {
                return false;
            }
            if (!ignoreGearState)
            {
                CompColorable color = thing.TryGetComp<CompColorable>();
                if (color != null && color.Active)
                {
                    return true;
                }
                CompStyleable style = thing.TryGetComp<CompStyleable>();
                if (style != null && style.styleDef != null)
                {
                    return true;
                }
                CompApparelVerbOwner_Charged charged = thing.TryGetComp<CompApparelVerbOwner_Charged>();
                if (charged != null && charged.RemainingCharges > 0)
                {
                    return true;
                }
            }
            CompIngredients ingredients = thing.TryGetComp<CompIngredients>();
            if (ingredients != null && ingredients.ingredients != null && ingredients.ingredients.Count > 0)
            {
                return true;
            }
            CompRefuelable fuel = thing.TryGetComp<CompRefuelable>();
            if (fuel != null && fuel.Fuel > 0f)
            {
                return true;
            }
            CompUniqueWeapon unique = thing.TryGetComp<CompUniqueWeapon>();
            if (unique != null && unique.TraitsListForReading != null && unique.TraitsListForReading.Count > 0)
            {
                return true;
            }
            CompBiocodable biocode = thing.TryGetComp<CompBiocodable>();
            if (biocode != null && biocode.Biocoded)
            {
                return true;
            }
            if (thing is Apparel apparel && apparel.WornByCorpse)
            {
                return true;
            }
            // 第三方弹药 / 充能状态由兼容层提供；Core 不直接引用 Compat。
            return false;
        }

        /// <summary>
        /// 「身份型」受保护实例：带 quest 标签 / 命名 / 单件武器 / 艺术 / 圣物——这些一销毁就不可重建。
        /// 入：thing = 实例；ignoreArt = true 时跳过 CompArt（与强制虚拟化开关一致）。
        /// 出：true = 必须保真。
        /// 区别：与 HasInstanceNonKeyState 的区别是判「不可替代身份」而非「可变状态」；两者都可能触发保真。
        /// </summary>
        private static bool IsProtectedThing(Thing thing, bool ignoreArt)
        {
            if (thing == null || thing.def == null || thing.def.category != ThingCategory.Item)
            {
                return false;
            }
            if (thing.questTags != null && thing.questTags.Count > 0)
            {
                return true;
            }
            if (thing.TryGetComp<CompGeneratedNames>() != null
                || thing.TryGetComp<CompBladelinkWeapon>() != null)
            {
                return true;
            }
            if (!ignoreArt)
            {
                CompArt art = thing.TryGetComp<CompArt>();
                if (art != null && art.Active)
                {
                    return true;
                }
            }
            return IsRelicSafe(thing);
        }

        /// <summary>
        /// 圣物判定依赖 Ideology 与 ModsConfig；宿主未就绪（单测 / 早期加载）时按"不是圣物"处理。
        /// 只对异常做 ErrorOnce 去重，不做进程级一次性闩锁：否则一次早期失败会让整个进程
        /// 永久失去圣物保真判定（后续存档的圣物会被当普通物虚拟化）。
        /// </summary>
        /// <remarks>入：thing = 待判定实例。出：true = 圣物。区别：异常安全包装，失败即按非圣物降级。</remarks>
        private static bool IsRelicSafe(Thing thing)
        {
            try
            {
                return ReliquaryUtility.IsRelic(thing);
            }
            catch (Exception)
            {
                SDS_Diag.ErrorOnce("圣物探测失败（按非圣物处理）", null);
                return false;
            }
        }

        // ---------- 自检（W35） ----------

        /// <summary>
        /// W35 自检：遍历全部可研究 def，确认它们都走保真通道；失败行写入诊断。
        /// 入：lines = 输出目标，null 直接返回。
        /// 出：无；前 20 条失败写入 lines（诊断失败数由调用方另行汇总）。
        /// 区别：只校验「逐件跟踪 ⇒ 保真」这一条不变式，不检查账本一致性（那是 SDS_CoreDiagnostics）。
        /// </summary>
        internal static void SelfCheck(List<string> lines)
        {
            if (lines == null)
            {
                return;
            }
            int failures = 0;
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (def == null || def.category != ThingCategory.Item || !IsTrackedPerInstance(def))
                {
                    continue;
                }
                if (!RequiresPreservedStorage(def))
                {
                    failures++;
                    if (failures <= 20)
                    {
                        lines.Add("[失败] [W35] 可研究物品未走保真：" + def.defName);
                    }
                }
            }
        }
    }
}
