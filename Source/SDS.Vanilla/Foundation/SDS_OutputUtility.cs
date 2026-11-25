using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 展示物 / 快照状态的唯一工厂（§6.5）：按品类键造展示物、写回品质与堆平均状态。
    /// 只读账本快照，不扣账、不建事务。
    /// </summary>
    internal static class SDS_OutputUtility
    {
        /// <summary>按 StockVersion 缓存的账本快照索引：展示物 / 镜像写状态共用。</summary>
        private static int cachedStockVersion = int.MinValue;
        private static Dictionary<SDS_StockKey, SDS_StockEntry> cachedEntries =
            new Dictionary<SDS_StockKey, SDS_StockEntry>();

        private static readonly Dictionary<SDS_StockKey, Thing> displayCache =
            new Dictionary<SDS_StockKey, Thing>();
        private static readonly Dictionary<Thing, SDS_StockKey> displayKeys =
            new Dictionary<Thing, SDS_StockKey>();

        /// <summary>按品类键取账本条目（含堆平均 HP / 腐烂进度）；找不到返回 false。</summary>
        internal static bool TryGetEntry(SDS_StockKey key, out SDS_StockEntry entry)
        {
            entry = null;
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || key == null)
            {
                return false;
            }
            int version = ledger.StockVersion;
            if (cachedStockVersion != version)
            {
                cachedStockVersion = version;
                cachedEntries.Clear();
                List<SDS_StockEntry> snapshot = ledger.Snapshot(0);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    SDS_StockEntry item = snapshot[i];
                    if (item != null && item.key != null)
                    {
                        cachedEntries[item.key] = item;
                    }
                }
            }
            return cachedEntries.TryGetValue(key, out entry) && entry != null;
        }

        /// <summary>把品类键里的品质 / 血量 / 腐烂进度写到实物上（展示物 / 代理 / 部分取出共用）。</summary>
        internal static void ApplyKeyState(SDS_StockKey key, Thing thing)
        {
            if (key == null || thing == null || thing.def == null)
            {
                return;
            }
            SDS_Core.ApplyQuality(thing, key.qualityInt);
            if (TryGetEntry(key, out SDS_StockEntry entry))
            {
                SDS_Core.ApplyVitals(thing, entry.avgHitPoints, entry.avgRotProgress);
            }
        }

        /// <summary>按品类键造一个展示用实物（不动物理存量）；保真品类返回 null。</summary>
        internal static Thing MakeDisplayThing(SDS_StockKey key)
        {
            if (key == null || key.thingDef == null || key.preserved)
            {
                return null;
            }
            Thing thing = ThingMaker.MakeThing(key.thingDef, key.stuff);
            if (thing == null)
            {
                return null;
            }
            ApplyKeyState(key, thing);
            displayKeys[thing] = key;
            return thing;
        }

        /// <summary>取虚拟品类展示物：按品类键缓存复用、永不销毁。</summary>
        internal static Thing CachedDisplayThing(SDS_StockKey key)
        {
            if (key == null || key.thingDef == null || key.preserved)
            {
                return null;
            }
            if (displayCache.TryGetValue(key, out Thing cached) && cached != null && !cached.Destroyed)
            {
                if (TryGetEntry(key, out SDS_StockEntry entry))
                {
                    SDS_Core.ApplyVitals(cached, entry.avgHitPoints, entry.avgRotProgress);
                }
                return cached;
            }
            Thing made = MakeDisplayThing(key);
            if (made != null)
            {
                displayCache[key] = made;
            }
            return made;
        }

        /// <summary>查询展示物创建时封存的品类键（身份唯一来源，不从展示物反推通道）。</summary>
        internal static bool TryGetDisplayKey(Thing display, out SDS_StockKey key)
        {
            key = null;
            return display != null && displayKeys.TryGetValue(display, out key) && key != null;
        }

        /// <summary>展示物身份的唯一判据：只认创建时封存的键（读档补封存见 BindDisplayKey）。</summary>
        internal static bool IsDisplay(Thing thing)
        {
            return thing != null && !thing.Destroyed && TryGetDisplayKey(thing, out SDS_StockKey _);
        }

        /// <summary>读档补封存：只允许 FinalizeInit 归一化对容器行内的持久化展示物调用。</summary>
        internal static void BindDisplayKey(Thing display, SDS_StockKey key)
        {
            if (display != null && key != null)
            {
                displayKeys[display] = key;
            }
        }

        /// <summary>实例 → 品类键的唯一公开口径（Core 门面）。</summary>
        internal static SDS_StockKey KeyOfThing(Thing thing)
        {
            return SDS_Rules.TryGetStockKey(thing, out SDS_StockKey key) ? key : null;
        }

        /// <summary>
        /// 展示物身份的唯一解析入口：保真桶展示物必须由桶代表真身生成键；
        /// 其余取创建时封存的键；两者都没有时按虚拟投影兜底。
        /// </summary>
        internal static SDS_StockKey KeyOfDisplay(Thing display, List<Thing> preservedBucket)
        {
            if (preservedBucket != null && preservedBucket.Count > 0 && preservedBucket[0] != null)
            {
                return SDS_Rules.TryGetStockKey(preservedBucket[0], out SDS_StockKey preservedKey)
                    ? preservedKey
                    : null;
            }
            if (display == null)
            {
                return null;
            }
            if (displayKeys.TryGetValue(display, out SDS_StockKey stored) && stored != null)
            {
                return stored;
            }
            return SDS_Rules.TryGetStockKey(display, out SDS_StockKey fallback) ? fallback : null;
        }

        // ---------- 交易用：按「原版分组桶」造展示物 ----------

        private static readonly System.Reflection.FieldInfo geneSetField =
            AccessTools.Field(typeof(GeneSetHolderBase), "geneSet");
        private static readonly System.Reflection.FieldInfo bladeTraitsField =
            AccessTools.Field(typeof(CompBladelinkWeapon), "traits");
        private static readonly System.Reflection.FieldInfo generatedNameField =
            AccessTools.Field(typeof(CompGeneratedNames), "name");
        private static readonly System.Reflection.FieldInfo stylePreceptField =
            AccessTools.Field(typeof(CompStyleable), "sourcePrecept");

        /// <summary>按桶代表真身缓存展示物（一个桶一个，永不销毁；代表消失后只解除引用）。</summary>
        private static readonly Dictionary<Thing, Thing> bucketDisplayCache = new Dictionary<Thing, Thing>();

        internal static Thing CachedPreservedBucketDisplay(Thing source)
        {
            if (source == null || source.Destroyed)
            {
                return null;
            }
            if (bucketDisplayCache.TryGetValue(source, out Thing cached) && cached != null && !cached.Destroyed)
            {
                return cached;
            }
            Thing made = MakePreservedDisplayThingFrom(source);
            if (made != null)
            {
                bucketDisplayCache[source] = made;
            }
            return made;
        }

        /// <summary>
        /// 是否具备可安全参与原版 TransferAsOne 的身份：基因包 / 异种胚必须带 GeneSet。
        /// 缺少 GeneSet 的物品会让原版 CanStackTogether 空引用，必须在展示 / 分组前剔除。
        /// </summary>
        internal static bool HasCompleteTransferIdentity(Thing thing)
        {
            if (thing == null)
            {
                return false;
            }
            Thing inner = thing.GetInnerIfMinified();
            if (inner == null)
            {
                return false;
            }
            // 无内层小人的尸体：原版 TransferAsOne 的尸体系分支会直接空引用（判据口径与 Core 一致）。
            if (inner is Corpse corpse && corpse.InnerPawn == null)
            {
                return false;
            }
            GeneSetHolderBase holder = inner as GeneSetHolderBase;
            return holder == null || holder.GeneSet != null;
        }

        /// <summary>按「原版分组桶」的代表真身造展示物（仅交易 / 编成 / 装载清单用）。</summary>
        internal static Thing MakePreservedDisplayThingFrom(Thing source)
        {
            if (source == null || source.Destroyed || source.def == null
                || !HasCompleteTransferIdentity(source))
            {
                return null;
            }
            if (typeof(Corpse).IsAssignableFrom(source.def.thingClass))
            {
                return null;
            }
            if (source is MinifiedThing sourceMinified)
            {
                if (sourceMinified.InnerThing == null)
                {
                    return null;
                }
                Thing minifiedDisplay = MakeMinifiedDisplay(sourceMinified);
                RegisterPreservedDisplay(minifiedDisplay, source);
                return minifiedDisplay;
            }
            Thing made = ThingMaker.MakeThing(source.def, source.Stuff);
            if (made == null)
            {
                return null;
            }
            CompQuality quality = source.TryGetComp<CompQuality>();
            SDS_Core.ApplyQuality(made, quality == null ? -1 : (int)quality.Quality);
            if (source.def.useHitPoints && made.def != null && made.def.useHitPoints)
            {
                made.HitPoints = source.HitPoints;
            }
            CopyPreservedIdentity(source, made);
            RegisterPreservedDisplay(made, source);
            return made;
        }

        private static void RegisterPreservedDisplay(Thing display, Thing source)
        {
            if (display == null || source == null)
            {
                return;
            }
            if (SDS_Rules.TryGetStockKey(source, out SDS_StockKey key) && key != null && key.thingDef != null)
            {
                displayKeys[display] = key;
            }
        }

        private static Thing MakeMinifiedDisplay(MinifiedThing source)
        {
            Thing shell = ThingMaker.MakeThing(source.def);
            if (!(shell is MinifiedThing outer))
            {
                shell?.Destroy();
                return null;
            }
            Thing innerSource = source.InnerThing;
            Thing inner = ThingMaker.MakeThing(innerSource.def, innerSource.Stuff);
            if (inner == null)
            {
                outer.Destroy();
                return null;
            }
            CompQuality quality = innerSource.TryGetComp<CompQuality>();
            SDS_Core.ApplyQuality(inner, quality == null ? -1 : (int)quality.Quality);
            if (innerSource.def != null && innerSource.def.useHitPoints
                && inner.def != null && inner.def.useHitPoints)
            {
                inner.HitPoints = innerSource.HitPoints;
            }
            CopyPreservedIdentity(innerSource, inner);
            outer.InnerThing = inner;
            if (outer.InnerThing == null)
            {
                outer.Destroy();
                inner.Destroy();
                return null;
            }
            return outer;
        }

        /// <summary>复制会影响原版价格 / 名称 / 分组的身份状态。</summary>
        private static void CopyPreservedIdentity(Thing source, Thing display)
        {
            if (source == null || display == null)
            {
                return;
            }
            if (source is Apparel sourceApparel && display is Apparel displayApparel)
            {
                displayApparel.WornByCorpse = sourceApparel.WornByCorpse;
            }
            CompIngredients sourceIngredients = source.TryGetComp<CompIngredients>();
            CompIngredients displayIngredients = display.TryGetComp<CompIngredients>();
            if (sourceIngredients != null && displayIngredients != null
                && sourceIngredients.ingredients != null && sourceIngredients.ingredients.Count > 0)
            {
                displayIngredients.ingredients = new List<ThingDef>(sourceIngredients.ingredients);
            }
            if (source is GeneSetHolderBase sourceGenes && display is GeneSetHolderBase displayGenes
                && sourceGenes.GeneSet != null && geneSetField != null)
            {
                geneSetField.SetValue(displayGenes, sourceGenes.GeneSet.Copy());
            }
            CompBladelinkWeapon sourceBlade = source.TryGetComp<CompBladelinkWeapon>();
            CompBladelinkWeapon displayBlade = display.TryGetComp<CompBladelinkWeapon>();
            if (sourceBlade != null && displayBlade != null && bladeTraitsField != null)
            {
                List<WeaponTraitDef> traits = sourceBlade.TraitsListForReading;
                bladeTraitsField.SetValue(displayBlade,
                    traits == null ? null : new List<WeaponTraitDef>(traits));
            }
            CompGeneratedNames sourceName = source.TryGetComp<CompGeneratedNames>();
            CompGeneratedNames displayName = display.TryGetComp<CompGeneratedNames>();
            if (sourceName != null && displayName != null && generatedNameField != null)
            {
                generatedNameField.SetValue(displayName, generatedNameField.GetValue(sourceName));
            }
            CompStyleable sourceStyle = source.TryGetComp<CompStyleable>();
            CompStyleable displayStyle = display.TryGetComp<CompStyleable>();
            if (sourceStyle != null && displayStyle != null && stylePreceptField != null)
            {
                stylePreceptField.SetValue(displayStyle, stylePreceptField.GetValue(sourceStyle));
            }
        }

        /// <summary>清掉代表真身已消失的缓存条目（只解除引用，绝不销毁展示物）。</summary>
        internal static void PruneBucketDisplays()
        {
            if (bucketDisplayCache.Count == 0)
            {
                return;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            List<Thing> dead = null;
            foreach (KeyValuePair<Thing, Thing> pair in bucketDisplayCache)
            {
                Thing source = pair.Key;
                if (source == null || source.Destroyed || (ledger != null && !ledger.IsPreserved(source)))
                {
                    (dead ?? (dead = new List<Thing>())).Add(source);
                }
            }
            if (dead == null)
            {
                return;
            }
            for (int i = 0; i < dead.Count; i++)
            {
                if (dead[i] != null)
                {
                    bucketDisplayCache.Remove(dead[i]);
                }
            }
        }

        /// <summary>E1：只解除引用，绝不销毁展示物（原版窗口可能仍持有引用）。</summary>
        internal static void ResetDisplayCaches()
        {
            displayCache.Clear();
            displayKeys.Clear();
            bucketDisplayCache.Clear();
            cachedEntries.Clear();
            cachedStockVersion = int.MinValue;
        }
    }

    /// <summary>
    /// 展示物 → 保真桶的运行期登记表。
    /// 车运与装载各自持有一个实例：复用同一实现，但保持流程生命周期与跨容器隔离。
    /// </summary>
    internal sealed class SDS_DisplayBucketRegistry
    {
        private readonly Dictionary<Thing, List<Thing>> buckets =
            new Dictionary<Thing, List<Thing>>();

        internal void Register(Thing display, List<Thing> bucket)
        {
            if (display != null && bucket != null)
            {
                buckets[display] = bucket;
            }
        }

        internal bool TryGet(Thing display, out List<Thing> bucket)
        {
            bucket = null;
            return display != null && buckets.TryGetValue(display, out bucket) && bucket != null;
        }

        internal IEnumerable<KeyValuePair<Thing, List<Thing>>> Entries
        {
            get { return buckets; }
        }

        internal void Remove(Thing display)
        {
            if (display != null)
            {
                buckets.Remove(display);
            }
        }

        internal void Clear()
        {
            buckets.Clear();
        }
    }
}
