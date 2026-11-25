using RimWorld;
using System;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 品类键：def + 材质 + 品质 + 保真标记的唯一组合，并带名字兜底。
    /// qualityInt 为 -1 表示无品质；stuff 为 null 表示无材质。
    /// defName / stuffName 无条件保留：def 解析失败时条目仍以 Unresolved 身份存在。
    /// 作为 Dictionary 键使用，必须按值相等。
    /// </summary>
    public class SDS_StockKey : IExposable, IEquatable<SDS_StockKey>
    {
        public ThingDef thingDef;
        public ThingDef stuff;
        public int qualityInt = -1;
        public string defName;
        public string stuffName;
        public bool preserved;

        public SDS_StockKey()
        {
        }

        public SDS_StockKey(ThingDef thingDef, ThingDef stuff, int qualityInt)
            : this(thingDef, stuff, qualityInt, false)
        {
        }

        public SDS_StockKey(ThingDef thingDef, ThingDef stuff, int qualityInt, bool preserved)
        {
            this.thingDef = thingDef;
            this.stuff = stuff;
            this.qualityInt = qualityInt;
            this.preserved = preserved;
            defName = thingDef != null ? thingDef.defName : null;
            stuffName = stuff != null ? stuff.defName : null;
        }

        /// <summary>落盘口径的 def 名：已解析取 def.defName，未解析取存档里的原名。</summary>
        public string EffectiveDefName
        {
            get { return thingDef != null ? thingDef.defName : defName; }
        }

        public string EffectiveStuffName
        {
            get { return stuff != null ? stuff.defName : stuffName; }
        }

        public bool Resolved
        {
            get { return thingDef != null; }
        }

        public bool HasIdentity
        {
            get
            {
                string name = EffectiveDefName;
                return !string.IsNullOrEmpty(name) && name != "null";
            }
        }

        /// <summary>把当前已解析的 def / 材质名写回持久化字段。</summary>
        public void EnsureNames()
        {
            if (thingDef != null)
            {
                defName = thingDef.defName;
            }
            if (stuff != null)
            {
                stuffName = stuff.defName;
            }
            if (string.IsNullOrEmpty(defName) || defName == "null")
            {
                defName = null;
            }
            if (string.IsNullOrEmpty(stuffName) || stuffName == "null")
            {
                stuffName = null;
            }
        }

        /// <summary>把名字解析成 Def（唯一解析入口，读档 / 手动重试共用）。</summary>
        public bool TryResolve()
        {
            bool changed = false;
            if (thingDef == null && HasIdentity)
            {
                ThingDef resolved = ResolveThingDef(defName);
                if (resolved != null)
                {
                    thingDef = resolved;
                    defName = resolved.defName;
                    changed = true;
                }
            }
            if (stuff == null && !string.IsNullOrEmpty(stuffName) && stuffName != "null")
            {
                ThingDef resolvedStuff = ResolveThingDef(stuffName);
                if (resolvedStuff != null && resolvedStuff.IsStuff)
                {
                    stuff = resolvedStuff;
                    stuffName = resolvedStuff.defName;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>Def 名字解析的唯一入口；解析失败是"未解析"这一合法状态，不刷屏。</summary>
        private static ThingDef ResolveThingDef(string defName)
        {
            if (string.IsNullOrEmpty(defName) || defName == "null")
            {
                return null;
            }
            string name = defName;
            try
            {
                name = BackCompatibility.BackCompatibleDefName(typeof(ThingDef), defName, false, null) ?? defName;
            }
            catch (Exception)
            {
                // 兼容链出错不影响按原名解析。
            }
            return DefDatabase<ThingDef>.GetNamedSilentFail(name);
        }

        public bool SameAs(SDS_StockKey other)
        {
            if (other == null || other.qualityInt != qualityInt || other.preserved != preserved)
            {
                return false;
            }
            // 两侧材质都已解析且非空：按 Def 引用比较（与 GetHashCode 的 defName 口径一致）。
            if (thingDef != null && other.thingDef != null && stuff != null && other.stuff != null)
            {
                return other.thingDef == thingDef && other.stuff == stuff;
            }
            // 任一侧材质未解析 / 为空：退回名字比较。必须与 GetHashCode 同口径，
            // 否则会出现「SameAs 相等但哈希不同」——同一条品类在字典里查不到。
            return string.Equals(EffectiveDefName, other.EffectiveDefName, StringComparison.Ordinal)
                && string.Equals(EffectiveStuffName, other.EffectiveStuffName, StringComparison.Ordinal);
        }

        public bool Equals(SDS_StockKey other)
        {
            return SameAs(other);
        }

        public override bool Equals(object obj)
        {
            return obj is SDS_StockKey other && SameAs(other);
        }

        public override int GetHashCode()
        {
            int hash = GenText.StableStringHash(EffectiveDefName ?? string.Empty);
            string stuffKey = EffectiveStuffName;
            if (!string.IsNullOrEmpty(stuffKey) && stuffKey != "null")
            {
                hash = hash * 397 ^ GenText.StableStringHash(stuffKey);
            }
            return (hash * 397 ^ qualityInt) * 397 ^ (preserved ? 1 : 0);
        }

        /// <summary>品类键的身份排序：defName → stuffName → quality → 通道。</summary>
        public static int CompareIdentity(SDS_StockKey a, SDS_StockKey b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }
            if (a == null)
            {
                return -1;
            }
            if (b == null)
            {
                return 1;
            }
            int c = string.CompareOrdinal(a.EffectiveDefName ?? string.Empty, b.EffectiveDefName ?? string.Empty);
            if (c != 0)
            {
                return c;
            }
            c = string.CompareOrdinal(a.EffectiveStuffName ?? string.Empty, b.EffectiveStuffName ?? string.Empty);
            if (c != 0)
            {
                return c;
            }
            c = a.qualityInt.CompareTo(b.qualityInt);
            if (c != 0)
            {
                return c;
            }
            return (a.preserved ? 1 : 0).CompareTo(b.preserved ? 1 : 0);
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                EnsureNames();
                Scribe_Values.Look(ref defName, "defName");
                Scribe_Values.Look(ref stuffName, "stuffName");
                // 2026-11-16 起只写新字段；旧节点 thingDef / stuff 仅保留读取（W26 只承诺旧档 → 新版）。
                Scribe_Values.Look(ref qualityInt, "qualityInt", -1);
                Scribe_Values.Look(ref preserved, "preserved", false);
                return;
            }

            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref stuffName, "stuffName");
            // 旧节点只读：2026-11-13 之前写出的档没有 defName / stuffName，靠这里兜底。
            string legacyDefName = null;
            string legacyStuffName = null;
            Scribe_Values.Look(ref legacyDefName, "thingDef");
            Scribe_Values.Look(ref legacyStuffName, "stuff");
            Scribe_Values.Look(ref qualityInt, "qualityInt", -1);
            Scribe_Values.Look(ref preserved, "preserved", false);

            if (string.IsNullOrEmpty(defName) || defName == "null")
            {
                defName = legacyDefName == "null" ? null : legacyDefName;
            }
            if (string.IsNullOrEmpty(stuffName) || stuffName == "null")
            {
                stuffName = legacyStuffName == "null" ? null : legacyStuffName;
            }
            defName = defName?.Trim();
            stuffName = stuffName?.Trim();
            TryResolve();
        }
    }

    /// <summary>账本条目：一个品类键 + 数量 + 虚拟通道堆平均状态。</summary>
    public class SDS_StockEntry : IExposable
    {
        public SDS_StockKey key;
        public long count;

        /// <summary>虚拟通道：该品类所有件的平均生命值（-1 = 不使用 HP / 旧档没有记录）。</summary>
        public float avgHitPoints = -1f;

        /// <summary>虚拟通道：该品类所有件的平均腐烂进度（-1 = 无腐烂状态 / 旧档没有记录）。</summary>
        public float avgRotProgress = -1f;

        public SDS_StockEntry()
        {
        }

        public SDS_StockEntry(SDS_StockKey key, long count)
        {
            this.key = key;
            this.count = count;
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref key, "key");
            Scribe_Values.Look(ref count, "count", 0L);
            Scribe_Values.Look(ref avgHitPoints, "avgHitPoints", -1f);
            Scribe_Values.Look(ref avgRotProgress, "avgRotProgress", -1f);
        }
    }

    /// <summary>
    /// 运行期堆平均状态（不落盘，只做 DTO）；加权平均由 Core 的 SDS_StockRules 计算。
    /// </summary>
    public sealed class SDS_StockVitals
    {
        /// <summary>该品类所有件的平均 HP；-1 = 该类型不使用 HP。</summary>
        public float hitPoints = -1f;

        /// <summary>该品类所有件的平均腐烂进度；-1 = 没有腐烂状态。</summary>
        public float rotProgress = -1f;
    }

    /// <summary>取出预留：某 Job 已把"哪个品类、多少数量"算给了谁。</summary>
    public class SDS_Reservation : IExposable
    {
        public int jobLoadID;
        public SDS_StockKey key;
        public int count;
        public Pawn pawn;

        public void ExposeData()
        {
            Scribe_Values.Look(ref jobLoadID, "jobLoadID", 0);
            Scribe_Deep.Look(ref key, "key");
            Scribe_Values.Look(ref count, "count", 0);
            Scribe_References.Look(ref pawn, "pawn");
        }
    }

    /// <summary>库存通道：虚拟（键 + 数量）或保真（真身实体）。</summary>
    public enum SDS_StockChannel
    {
        Virtual,
        Preserved
    }
}
