using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    // ============================================================================
    // 来源：天工模块化穿梭机 CeleTech Shuttle Extension
    // ============================================================================

    /// <summary>
    /// 与天工模块化穿梭机 CeleTech Shuttle Extension（packageId = LongRange.CeleTech.ShuttleExtension）的兼容。
    ///
    /// 为什么需要：它把「装载清单」私有化成了自己的读模型（自建装载对话框，不经过原版
    /// <c>Dialog_LoadTransporters</c>）；而本 Mod 的库存没有地图实物，可见性只由
    /// 「谁在清单构建时注入展示物」决定 → 它的装载界面天然看不到超维库存。
    ///
    /// 数据 / 作业契约仍然守规：<c>CompTransporter</c> + <c>leftToLoad</c>、
    /// <c>WorkGiver_LoadTransporters</c>、<c>LoadTransportersJobUtility</c>、
    /// <c>HaulToTransporter</c>、<c>Notify_ThingAdded</c> 全部是原版那条链，
    /// 因此本适配器**只补清单注入**：
    ///  - 在它的清单构建函数返回后，经 <c>SDS_API</c> 内部桥调用唯一注入实现；
    ///  - 取货 / 预留 / 扣账 / 入舱 / 存档仍由 <c>SDS_TransportVirtual</c> 与既有原版挂点负责。
    ///
    /// 规则：W50（《看得见》§6.3）——第三方自建清单只能经 internal 桥注入，不得另建写路径。
    ///
    /// 未安装时整块跳过；对方内部类型 / 方法改名时只记挂点缺失，不影响本体。
    /// </summary>
    internal static class SDS_ModCompat_CeleTechShuttle
    {
        private const string PackageId = "LongRange.CeleTech.ShuttleExtension";
        private const string AdapterName = "天工模块化穿梭机 CeleTech Shuttle Extension";

        /// <summary>对方装载清单的构建函数所在类型（internal，只按名字探测）。</summary>
        private const string FilterTypeName =
            "CeleTech.ShuttleExtension.ModularShuttle.Cargo.CargoTransferFilter";

        /// <summary>对方稳定存在的公开类型：用于区分「未安装」与「装了但内部结构改名」。</summary>
        private const string MarkerTypeName =
            "CeleTech.ShuttleExtension.ModularShuttle.Core.Building_ModularShuttle";

        public static void TryApply(Harmony harmony)
        {
            bool found = false;
            try
            {
                if (harmony == null)
                {
                    return;
                }

                found = AccessTools.TypeByName(MarkerTypeName) != null;
                if (!found)
                {
                    SDS_CompatReportHub.Register(AdapterName, PackageId, false, null);
                    return;
                }

                Type filterType = AccessTools.TypeByName(FilterTypeName);
                SDS_CompatPatchReport report = new SDS_CompatPatchReport(AdapterName);
                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_CeleTechShuttle),
                    "CargoTransferFilter.BuildCargoTransferables（装载清单注入）",
                    SDS_CompatTools.Method(filterType, "BuildCargoTransferables"),
                    null, nameof(BuildCargoTransferablesPostfix),
                    filterType == null
                        ? "找不到类型 " + FilterTypeName
                        : "找不到方法 BuildCargoTransferables");
                SDS_CompatReportHub.Register(AdapterName, PackageId, true, report);
            }
            catch (Exception e)
            {
                SDS_CompatReportHub.Register(AdapterName, PackageId, found, null,
                    null, "应用异常 " + e.GetType().Name);
                SDS_Diag.ErrorOnce(AdapterName + " 兼容补丁应用失败（已跳过，本体行为不受影响）", e);
            }
        }

        /// <summary>
        /// 清单构建后注入展示物。只读投影：不写 leftToLoad，不改对方准入 / 冷藏 / 装载逻辑。
        /// </summary>
        public static void BuildCargoTransferablesPostfix(ThingWithComps host,
            List<TransferableOneWay> __result)
        {
            try
            {
                SDS_API.InjectTransportLoadCandidates(host, __result);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("天工模块化穿梭机 CeleTech Shuttle Extension 兼容（装载清单注入）失败（已忽略，穿梭机原行为不受影响）", e);
            }
        }
    }
}
