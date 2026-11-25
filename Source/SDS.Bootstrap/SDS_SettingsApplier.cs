using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 设置 → 建筑 Def / 场上实例 / 财富口径的应用层（§10.2 第 6 步）。
    /// 只写原版 CompProperties_Power 与通知缓存；不定义任何存储规则。
    /// </summary>
    internal static class SDS_SettingsApplier
    {
        /// <summary>该 repack 中 basePowerConsumption 为私有字段，必须反射访问。</summary>
        private static readonly FieldInfo basePowerField = typeof(CompProperties_Power).GetField(
            "basePowerConsumption", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>showPowerNeededIfOff 是 public 字段；与 basePowerConsumption 共用同一写入路径。</summary>
        private static readonly FieldInfo showPowerNeededField = typeof(CompProperties_Power).GetField(
            "showPowerNeededIfOff", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly HashSet<string> sharedPowerPropsChecked = new HashSet<string>();

        /// <summary>把设置值写入建筑 Def 与端点优先级；返回实际写入耗电的建筑 Def 数。</summary>
        internal static int ApplyPowerAndPriority(SDS_Settings settings)
        {
            if (settings == null)
            {
                return 0;
            }
            settings.ClampAll();
            int written = 0;
            written += ApplyPower(SDS_Config.CoreDefName, settings.corePower);
            written += ApplyPower(SDS_Config.ArrayDefName, settings.arrayPower);
            written += ApplyPower(SDS_Config.PortDefName, settings.portPower);
            SDS_Rules.NotifyEndpointChanged();
            SDS_Vanilla.ApplyStoragePriorityToEndpoints();
            return written;
        }

        /// <summary>
        /// 设置变更后立即重算各图财富，让「是否计入超维空间」的切换马上反映到原版财富明细。
        /// 原版 WealthWatcher 有惰性缓存，不主动重算会读到切换前的旧值。
        /// </summary>
        internal static void RefreshWealth()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.Maps == null)
            {
                return;
            }
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map map = Find.Maps[i];
                if (map != null && map.wealthWatcher != null)
                {
                    map.wealthWatcher.ForceRecount();
                }
            }
        }

        /// <summary>
        /// 把耗电写到建筑 Def（Def 级共享，对所有实例生效）。watts == 0 表示不耗电：
        /// 同时关掉原版「需要电力」提示，否则免电建筑会一直亮着「没通电」。
        /// </summary>
        private static int ApplyPower(string defName, float watts)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return 0;
            }
            CompProperties_Power power = def.GetCompProperties<CompProperties_Power>();
            if (power == null || basePowerField == null)
            {
                return 0;
            }
            // 保险：万一该 CompProperties 实例被别的 Def 共用，写下去会波及其它建筑，坚决跳过。
            if (IsPowerPropsSharedWithOtherDefs(power, def))
            {
                Log.ErrorOnce("[超维存储] " + defName
                    + " 的 CompProperties_Power 与其它 Def 共用同一实例，已跳过电力写入以免影响其它建筑。",
                    defName.GetHashCode());
                return 0;
            }
            float clamped = Math.Max(0f, watts);
            basePowerField.SetValue(power, clamped);
            if (showPowerNeededField != null)
            {
                // 有耗电时保留原版默认（true）以便正常提示；0 耗电时关掉误导性提示。
                showPowerNeededField.SetValue(power, clamped > 0f);
            }
            RefreshSpawnedPowerComps(def);
            return 1;
        }

        /// <summary>
        /// 已建成的建筑不会自动读到新的 Def 耗电值：CompPowerTrader 把 PowerOutput 缓存在实例上，
        /// 只在 SetUpPowerVars() 里重算。这里对场上实例主动重算一次。
        /// </summary>
        private static void RefreshSpawnedPowerComps(ThingDef def)
        {
            if (Current.Game == null || Find.Maps == null)
            {
                return;
            }
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map map = Find.Maps[i];
                if (map == null || map.listerThings == null)
                {
                    continue;
                }
                List<Thing> things = map.listerThings.ThingsOfDef(def);
                for (int j = 0; j < things.Count; j++)
                {
                    CompPowerTrader trader = things[j].TryGetComp<CompPowerTrader>();
                    if (trader != null)
                    {
                        trader.SetUpPowerVars();
                    }
                }
            }
        }

        /// <summary>
        /// 检查该 CompProperties_Power 实例是否被其它 ThingDef 也引用。
        /// 结果按 defName 缓存，避免反复全表扫描。
        /// </summary>
        private static bool IsPowerPropsSharedWithOtherDefs(CompProperties_Power power, ThingDef owner)
        {
            if (sharedPowerPropsChecked.Contains(owner.defName))
            {
                return false;
            }
            sharedPowerPropsChecked.Add(owner.defName);
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef other = allDefs[i];
                if (other == null || other == owner || other.comps == null)
                {
                    continue;
                }
                for (int j = 0; j < other.comps.Count; j++)
                {
                    if (ReferenceEquals(other.comps[j], power))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
