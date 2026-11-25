using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>本 Mod 的 JobDef 引用；每次访问现查，避免 Def 加载完成前的静态构造异常。</summary>
    public static class SDS_JobDefOf
    {
        public const string WithdrawForTransportLoadName = "SDS_WithdrawForTransportLoad";
        public const string DepositToGridName = "SDS_DepositToGrid";
        public const string WithdrawPreservedForBillName = "SDS_WithdrawPreservedForBill";

        public static JobDef WithdrawForTransportLoad
        {
            get
            {
                JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(WithdrawForTransportLoadName);
                if (def == null)
                {
                    Log.ErrorOnce("[超维存储] 找不到 JobDef " + WithdrawForTransportLoadName
                        + "：运输舱 / 传送门装载的超维取货不会启用。请检查 Defs/SDS_JobDefs.xml。", 0x5D5F21);
                }
                return def;
            }
        }

        public static JobDef DepositToGrid
        {
            get
            {
                JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(DepositToGridName);
                if (def == null)
                {
                    Log.ErrorOnce("[超维存储] 找不到 JobDef " + DepositToGridName
                        + "：手动「存入超维空间」不会启用。请检查 Defs/SDS_JobDefs.xml。", 0x5D5F22);
                }
                return def;
            }
        }

        public static JobDef WithdrawPreservedForBill
        {
            get
            {
                JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(WithdrawPreservedForBillName);
                if (def == null)
                {
                    Log.ErrorOnce("[超维存储] 找不到 JobDef " + WithdrawPreservedForBillName
                        + "：保真原料（尸体等）无法从端点派活取用。请检查 Defs/SDS_JobDefs.xml。", 0x5D5F23);
                }
                return def;
            }
        }
    }

    /// <summary>
    /// 运输舱装载行的「超维身份 + 欠量」存档快照（一次性中转，不是账本）。
    /// 只保存 def / 材质 / 品质 / 通道 + 欠量，不保存任何 Thing 引用。
    /// </summary>
    public sealed class SDS_TransportLoadSave : IExposable
    {
        public int containerThingID;
        public int rowIndex = -1;
        public string defName;
        public string stuffName;
        public int qualityInt = -1;
        public bool preserved;
        public int units;

        public void ExposeData()
        {
            Scribe_Values.Look(ref containerThingID, "containerThingID", 0);
            Scribe_Values.Look(ref rowIndex, "rowIndex", -1);
            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref stuffName, "stuffName");
            Scribe_Values.Look(ref qualityInt, "qualityInt", -1);
            Scribe_Values.Look(ref preserved, "preserved", false);
            Scribe_Values.Look(ref units, "units", 0);
        }
    }
}
