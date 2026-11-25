using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 账本数据原语：键构造、可存基础判据、堆平均状态与物化。
    /// 只做数据换算与实物写回，不做通道 / 保真判定（那在 SDS.Rules 的 SDS_StockRules）。
    /// 入 / 出：入 Thing / ThingDef / 键，出键、状态值或未 spawn 实物；不写账本、不派活。
    /// 区别：SDS_Ledger 与 SDS.Rules 共用这一份数据原语，避免账本为取键而反向依赖规则模块。
    /// </summary>
    internal static class SDS_StockData
    {
        // ---------- 键 ----------

        /// <summary>虚拟通道口径：按外壳 def 取键。</summary>
        /// <remarks>入：thing = 实例，null 返回 null。出：虚拟键（preserved = false，品质取外壳组件）。
        /// 区别：与 FromPreservedThing 的区别是缩装物取外壳而非内层；与 FromAnyThing 的区别是不做通道分流。</remarks>
        public static SDS_StockKey FromThing(Thing thing)
        {
            if (thing == null)
            {
                return null;
            }
            CompQuality quality = thing.TryGetComp<CompQuality>();
            return new SDS_StockKey(thing.def, thing.Stuff,
                quality == null ? -1 : (int)quality.Quality);
        }

        /// <summary>保真通道口径：按真身取键（缩装家具取内部物，其余取自身）。</summary>
        /// <remarks>入：thing = 实例，null 返回 null。出：保真键（preserved = true）。
        /// 区别：与 FromThing 的唯一差别是缩装物取 InnerThing；两者都只构键，不判通道。</remarks>
        public static SDS_StockKey FromPreservedThing(Thing thing)
        {
            if (thing == null)
            {
                return null;
            }
            if (thing is MinifiedThing minified && minified.InnerThing != null)
            {
                Thing inner = minified.InnerThing;
                CompQuality innerQuality = inner.TryGetComp<CompQuality>();
                return new SDS_StockKey(inner.def, inner.Stuff,
                    innerQuality == null ? -1 : (int)innerQuality.Quality, true);
            }
            CompQuality quality = thing.TryGetComp<CompQuality>();
            return new SDS_StockKey(thing.def, thing.Stuff,
                quality == null ? -1 : (int)quality.Quality, true);
        }

        /// <summary>品类归属键：按设置把品质 / 材质维度归零；记录本身仍保留精确值。</summary>
        /// <remarks>入：key = 精确键，null 返回 null。出：归属键（两个设置都开时原样返回）。
        /// 区别：只用于容量 / 分组 / 展示；扣账与预留必须用精确键，否则会把不同品质合并。</remarks>
        public static SDS_StockKey CategoryKeyOf(SDS_StockKey key)
        {
            if (key == null)
            {
                return null;
            }
            bool separateQuality = SDS_Config.SeparateByQuality;
            bool separateStuff = SDS_Config.SeparateByStuff;
            if (separateQuality && separateStuff)
            {
                return key;
            }
            return new SDS_StockKey(
                key.thingDef,
                separateStuff ? key.stuff : null,
                separateQuality ? key.qualityInt : -1,
                key.preserved);
        }

        // ---------- 可存基础判据 ----------

        /// <summary>def 级可存判定：物品类且非未完成物；纯读。</summary>
        /// <remarks>入：def = 目标 def，可 null。出：true = 属于可存类别。
        /// 区别：与 CanStore 的区别是不看实例状态 / 堆量；与 SDS_StockRules.CanDeposit 的区别是不看通道与宿主筛选。</remarks>
        public static bool CanStoreDef(ThingDef def)
        {
            return def != null && def.category == ThingCategory.Item && !IsUnfinishedDef(def);
        }

        /// <summary>实例级「是否可入账」的最基础判定：未销毁、件数 &gt; 0、def 可存；纯读。</summary>
        /// <remarks>入：thing = 实例，可 null。出：true = 具备入账资格。
        /// 区别：与 SDS_StockRules.CanDeposit 的区别是没有通道 / 容量 / 筛选门槛，供「一次校验」的场景复用。</remarks>
        public static bool CanStore(Thing thing)
        {
            return thing != null && !thing.Destroyed && thing.stackCount > 0 && CanStoreDef(thing.def);
        }

        /// <summary>
        /// 保真真身是否仍是一份「原版可安全使用」的实物：尸体必须带内层小人
        /// （无内层的 Corpse 会让原版尸体系筛选 / TransferAsOne 空引用）、缩装物必须带内层物。
        /// 这是保真通道唯一的"内容完整性"判据，账本出口与原版判定入口共用，不得各处另写。
        /// </summary>
        /// <remarks>入：thing = 保真真身，可 null。出：true = 内容完整（普通物恒 true）。
        /// 区别：只判「能否安全使用」，不判是否仍由账本持有（那看 ISDS_LedgerRead.IsPreserved）。</remarks>
        public static bool HasLiveContent(Thing thing)
        {
            if (thing == null || thing.Destroyed)
            {
                return false;
            }
            if (thing is Corpse corpse)
            {
                return corpse.InnerPawn != null;
            }
            if (thing is MinifiedThing minified)
            {
                return minified.InnerThing != null;
            }
            return true;
        }

        /// <summary>是否未完成物（def 标记或 thingClass 继承 UnfinishedThing）；纯读。</summary>
        /// <remarks>入：def = 目标 def，可 null。出：true = 未完成物，一律不可存。
        /// 区别：只按 def 判定；实例级状态不看。</remarks>
        public static bool IsUnfinishedDef(ThingDef def)
        {
            return def != null && (def.isUnfinishedThing || typeof(UnfinishedThing).IsAssignableFrom(def.thingClass));
        }

        // ---------- 堆平均状态 ----------

        /// <summary>
        /// 抓取单件的血量（用于并入堆平均状态）。
        /// 入：thing = 实例。出：HP；不使用血量的 def / null 返回 -1（表示无状态信息）。
        /// 区别：与 CaptureRotProgress 并列；与 DefaultHitPoints 的区别是取当前值而非满值。
        /// </summary>
        public static float CaptureHitPoints(Thing thing)
        {
            return thing != null && thing.def != null && thing.def.useHitPoints ? thing.HitPoints : -1f;
        }

        /// <summary>
        /// 抓取单件的腐烂进度（用于并入堆平均状态）。
        /// 入：thing = 实例。出：RotProgress；无 CompRottable / null 返回 -1。
        /// 区别：与 CaptureHitPoints 并列；-1 是「无状态」哨兵，不是有效进度。
        /// </summary>
        public static float CaptureRotProgress(Thing thing)
        {
            CompRottable rottable = thing != null ? thing.TryGetComp<CompRottable>() : null;
            return rottable != null ? rottable.RotProgress : -1f;
        }

        /// <summary>
        /// 该键对应的「未记录血量」时的默认值（def 满血）。
        /// 入：key = 品类键，可 null。出：BaseMaxHitPoints；key / def 为空或 stat 未载入时返回 -1。
        /// 区别：与 CaptureHitPoints 的区别是取理论满值，用于 MergeVitals 的缺省基准；
        ///       stat 未载入（测试宿主 / 极早启动）与 HitPointsAboveDefBound 同口径降级为无基准。
        /// </summary>
        public static float DefaultHitPoints(SDS_StockKey key)
        {
            if (key == null || key.thingDef == null || StatDefOf.MaxHitPoints == null)
            {
                return -1f;
            }
            return key.thingDef.BaseMaxHitPoints;
        }

        /// <summary>按件数加权平均并入堆平均状态（-1 表示本次没有状态信息）。</summary>
        /// <remarks>入：vitals = 目标堆状态（就地修改）；key = 品类键；oldCount = 并入前件数；addCount = 新增件数；
        /// addHitPoints / addRotProgress = 新增部分的取值，&lt;0 表示该项无信息。
        /// 出：无；vitals 被更新（缺省基准分别取满血 / 0）。
        /// 未解析 def（thingDef 为空）也参与合并：暂时缺 mod 时读档必须保留 HP / 腐烂（R13 / W26），
        /// 未知 def 不判定 useHitPoints，写回实物时再由 ApplyVitals 按真实 def 过滤。
        /// 区别：只做加权平均，不做夹取与写入实物（写入见 ApplyVitals）。
        /// </remarks>
        public static void MergeVitals(SDS_StockVitals vitals, SDS_StockKey key, long oldCount, int addCount,
            float addHitPoints, float addRotProgress)
        {
            if (vitals == null || key == null
                || (addHitPoints < 0f && addRotProgress < 0f))
            {
                return;
            }
            long total = oldCount + addCount;
            if (total <= 0)
            {
                return;
            }
            if (addHitPoints >= 0f && (key.thingDef == null || key.thingDef.useHitPoints))
            {
                float old = vitals.hitPoints;
                if (old < 0f && key.thingDef != null)
                {
                    old = DefaultHitPoints(key);
                }
                if (old < 0f)
                {
                    // 无旧值 / 未知 def / stat 未载入：按新值处理，避免把 -1 带进加权平均。
                    old = Math.Max(0f, addHitPoints);
                }
                float add = Math.Max(0f, addHitPoints);
                vitals.hitPoints = (float)((old * oldCount + add * addCount) / total);
            }
            if (addRotProgress >= 0f)
            {
                float oldRot = vitals.rotProgress >= 0f ? vitals.rotProgress : 0f;
                vitals.rotProgress = (float)((oldRot * oldCount + addRotProgress * addCount) / total);
            }
        }

        // ---------- vitals 合法性（自检与读档归一化共用的唯一判据） ----------

        /// <summary>腐烂进度是否非法（量纲 = RimWorld 原始 RotProgress tick；-1 = 无状态哨兵）。</summary>
        /// <remarks>
        /// 入：key = 品类键，可空；v = 待检值。
        /// 出：true = 非法（NaN / 无穷 / 越过哨兵 / 已解析 def 无 CompRottable 却带进度）。
        /// 未解析 def（key.thingDef 为空）视为「未知」：只做结构校验，保留存档值，等 def 恢复后再按真实 def 判定；
        /// 否则暂时缺 mod 时读档会把未解析条目的腐烂永久归零（R13 / W26）。
        /// 区别：与 CaptureRotProgress / MergeVitals / ApplyVitals 同一套量纲，**不做数值上限**——
        ///       端口等效「地图上真放了这么多东西」（总则推论二），未冷藏囤积任意 tick 都合法；
        ///       唯一的结构不变量是「已解析且无腐坏件的 def 只允许 -1」。
        /// </remarks>
        internal static bool RotProgressIllegal(SDS_StockKey key, float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < -1f)
            {
                return true;
            }
            if (key == null || key.thingDef == null)
            {
                return false;
            }
            bool hasRotComp = key.thingDef.GetCompProperties<CompProperties_Rottable>() != null;
            return !hasRotComp && v > -1f;
        }

        /// <summary>平均 HP 是否超过该键的上限（上限 = stat MaxHitPoints(含材质) × 品质上限 1.8）。</summary>
        /// <remarks>
        /// 入：key = 品类键，可空；hp = 待检值。出：true = 超上限。
        /// 区别：与 ApplyVitals 的实例钳制(thing.MaxHitPoints)同口径，比裸 def.BaseMaxHitPoints 宽——
        ///       vitals 存的是真实单件 HP，含材质 × 品质乘数；stat 未载入（测试宿主 / 极早启动）时不设界。
        /// </remarks>
        internal static bool HitPointsAboveDefBound(SDS_StockKey key, float hp)
        {
            if (hp < 0f || key == null || key.thingDef == null || !key.thingDef.useHitPoints
                || StatDefOf.MaxHitPoints == null)
            {
                return false;
            }
            float max = key.thingDef.GetStatValueAbstract(StatDefOf.MaxHitPoints, key.stuff) * 1.8f;
            return max > 0f && hp > max + 0.01f;
        }

        // ---------- 物化 ----------

        /// <summary>按品类键重建实物（不 spawn）；写回材质 / 品质 / 堆平均状态。</summary>
        /// <remarks>入：key = 品类键；count = 期望件数（夹到 stackLimit）；vitals = 堆状态，可 null。
        /// 出：新实物（未生成到地图）；key / def 无效或 count ≤0 返回 null。
        /// 区别：只负责「造出来」，扣账 / 预留 / 落位都不在这里；与 ThingMaker.MakeThing 的区别是附带品质与状态回写。</remarks>
        public static Thing MakeThing(SDS_StockKey key, int count, SDS_StockVitals vitals)
        {
            if (key == null || key.thingDef == null || count <= 0)
            {
                return null;
            }
            Thing thing = ThingMaker.MakeThing(key.thingDef, key.stuff);
            if (thing == null)
            {
                return null;
            }
            ApplyQuality(thing, key.qualityInt);
            thing.stackCount = Math.Min(count, thing.def.stackLimit);
            ApplyVitals(thing, vitals != null ? vitals.hitPoints : -1f,
                vitals != null ? vitals.rotProgress : -1f);
            return thing;
        }

        /// <summary>把品质写回实物；负值 = 无品质概念。展示物 / 代理 / 镜像共用。</summary>
        /// <remarks>入：thing = 目标实物；qualityInt = 品质等级，&lt;0 直接返回。出：无（无 CompQuality 时静默跳过）。
        /// 区别：与 ApplyVitals 并列；只写组件，不触碰账本。</remarks>
        public static void ApplyQuality(Thing thing, int qualityInt)
        {
            if (thing == null || qualityInt < 0)
            {
                return;
            }
            CompQuality quality = thing.TryGetComp<CompQuality>();
            if (quality != null)
            {
                quality.SetQuality((QualityCategory)qualityInt, null);
            }
        }

        /// <summary>把堆平均血量 / 腐烂进度写回实物（负值 = 不写）。展示物 / 代理 / 镜像共用。</summary>
        /// <remarks>入：thing = 目标实物；hitPoints / rotProgress = 目标值，&lt;0 跳过该项。出：无；HP 夹到 [1, MaxHitPoints]。
        /// 区别：与 MergeVitals 的区别是「写入实物」而非「算平均」。</remarks>
        public static void ApplyVitals(Thing thing, float hitPoints, float rotProgress)
        {
            if (thing == null || thing.def == null)
            {
                return;
            }
            if (thing.def.useHitPoints && hitPoints >= 0f)
            {
                int hp = (int)hitPoints;
                if (hp < 1)
                {
                    hp = 1;
                }
                if (hp > thing.MaxHitPoints)
                {
                    hp = thing.MaxHitPoints;
                }
                thing.HitPoints = hp;
            }
            if (rotProgress >= 0f)
            {
                CompRottable rottable = thing.TryGetComp<CompRottable>();
                if (rottable != null)
                {
                    rottable.RotProgress = rotProgress;
                }
            }
        }
    }
}
