using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 医疗等级门控（§6.3）：复刻原版 WorkGiver_DoBill.GetMedicalCareCategory + AllowsMedicine。
    /// 非药品、非 Pawn billGiver（原版口径 = Best）恒允许。
    /// </summary>
    internal sealed partial class SDS_MedicalCareAdapter : ISDS_MedicalCarePolicy
    {
        public bool Allows(ThingDef medicine, Thing billGiver)
        {
            if (medicine == null || !medicine.IsMedicine)
            {
                return true;
            }
            return WorkGiver_DoBill.GetMedicalCareCategory(billGiver).AllowsMedicine(medicine);
        }
    }
}

