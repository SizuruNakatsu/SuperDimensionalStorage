using System;
using System.Reflection;
using HarmonyLib;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 边缘米莉拉「送礼」：在 JobDriver_IdealReceiveGift 预约前置，把镜像目标换成端点访问格上的真身。
    /// 本类只做描述符 + 挂点；物化唯一实现在 SDS_CompatMirrorSeam（不改 W54 / Core）。
    /// </summary>
    internal static class SDS_ModCompat_MiliraGift
    {
        private const string HookName = "JobDriver_IdealReceiveGift.TryMakePreToilReservations（端点物化）";

        private static readonly string[] FieldNames =
        {
            "Crafted", "Prepared", "Sent", "Note", "Author", "CrafterName", "Year"
        };

        private static Type compType;
        private static FieldInfo[] fields;

        public static void TryApply(Harmony harmony)
        {
            bool detected = false;
            try
            {
                Type driver = AccessTools.TypeByName(SDS_CompatAdapter_MiliraGift.DriverTypeName);
                compType = AccessTools.TypeByName(SDS_CompatAdapter_MiliraGift.CompTypeName);
                detected = driver != null || compType != null;
                if (driver == null)
                {
                    SDS_CompatReportHub.Register(SDS_CompatAdapter_MiliraGift.NameText,
                        SDS_CompatAdapter_MiliraGift.PackageId, detected, null, null,
                        detected
                            ? "找不到类型 " + SDS_CompatAdapter_MiliraGift.DriverTypeName + "（版本 / 命名变化？）"
                            : null);
                    return;
                }
                fields = ResolveFields(compType);
                if (fields == null)
                {
                    SDS_CompatReportHub.Register(SDS_CompatAdapter_MiliraGift.NameText,
                        SDS_CompatAdapter_MiliraGift.PackageId, true, null, null,
                        "CompIdealGift 字段不完整（版本 / 命名变化？）");
                    return;
                }
                SDS_CompatPatchReport report = new SDS_CompatPatchReport(SDS_CompatAdapter_MiliraGift.NameText);
                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_MiliraGift), HookName,
                    AccessTools.Method(driver, "TryMakePreToilReservations", new[] { typeof(bool) }),
                    nameof(TryMakeReservationsPrefix), null,
                    "找不到方法 TryMakePreToilReservations(bool)");
                SDS_CompatReportHub.Register(SDS_CompatAdapter_MiliraGift.NameText,
                    SDS_CompatAdapter_MiliraGift.PackageId, true, report);
            }
            catch (Exception e)
            {
                SDS_CompatReportHub.Register(SDS_CompatAdapter_MiliraGift.NameText,
                    SDS_CompatAdapter_MiliraGift.PackageId, detected, null, null,
                    "应用异常 " + e.GetType().Name);
                SDS_Diag.ErrorOnce("边缘米莉拉养成兼容补丁应用失败（已跳过，原行为不受影响）", e);
            }
        }

        /// <summary>
        /// 薄 Prefix：物化是否成功都返回 true——接管时 target 已换真身，未接管时原版预约照常执行。
        /// </summary>
        public static bool TryMakeReservationsPrefix(JobDriver __instance)
        {
            SDS_CompatMirrorSeam.TryMaterializeForReservation(
                __instance, TargetIndex.A, 1, compType, fields,
                "边缘米莉拉养成兼容（送礼端点物化）");
            return true;
        }

        private static FieldInfo[] ResolveFields(Type type)
        {
            if (type == null)
            {
                return null;
            }
            FieldInfo[] result = new FieldInfo[FieldNames.Length];
            for (int i = 0; i < FieldNames.Length; i++)
            {
                result[i] = AccessTools.Field(type, FieldNames[i]);
                if (result[i] == null)
                {
                    return null;
                }
            }
            return result;
        }
    }
}
