using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>本 Mod 两个植入体芯片的 HediffDef 引用；每次访问现查。</summary>
    public static class SDS_HediffDefOf
    {
        public const string ChipAName = "SDS_ImplantHediffA";
        public const string ChipBName = "SDS_ImplantHediffB";

        public static HediffDef ChipA
        {
            get
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(ChipAName);
                if (def == null)
                {
                    Log.ErrorOnce("[超维存储] 找不到植入体芯片 HediffDef " + ChipAName
                        + "：植入体端口不会启用（Game 可正常运行）。请检查 Defs/SDS_Implants.xml。", 0x5D5F24);
                }
                return def;
            }
        }

        public static HediffDef ChipB
        {
            get
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(ChipBName);
                if (def == null)
                {
                    Log.ErrorOnce("[超维存储] 找不到植入体芯片 HediffDef " + ChipBName
                        + "：植入体端口不会启用（Game 可正常运行）。请检查 Defs/SDS_Implants.xml。", 0x5D5F25);
                }
                return def;
            }
        }
    }
}
