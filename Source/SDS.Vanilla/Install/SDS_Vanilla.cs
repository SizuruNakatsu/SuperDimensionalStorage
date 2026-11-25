using System;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Vanilla 模块初始化入口的唯一实现（internal）：静态清理登记 + 宿主装配 + 设置页 / 热键。
    /// 公开面在 SDS.Vanilla_API.cs 的 SDS_Vanilla 门面；本文件不得出现 public 成员。
    /// </summary>
    internal static class SDS_VanillaImpl
    {
        internal const string HotkeyActionOpen = "Open";
        internal const string HotkeyActionOutput = "Output";
        internal const string HotkeyActionUse = "Use";

        private sealed class ResetAction : ISDSResettable
        {
            private readonly Action action;

            public ResetAction(Action action)
            {
                this.action = action;
            }

            public void ResetStatics()
            {
                if (action != null)
                {
                    action();
                }
            }
        }

        private static bool registered;

        internal static void RegisterAll()
        {
            if (registered)
            {
                return;
            }
            registered = true;
            SDS_StaticState.Register(new ResetAction(SDS_MirrorService.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_ReclaimRegistry.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_ProjectionService.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_OutputUtility.ResetDisplayCaches));
            SDS_StaticState.Register(new ResetAction(SDS_MirrorHaulDestination.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_Exposure.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_DepositDispatchLedger.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_Selection.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_CaravanVirtual.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_TransportVirtual.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_OfferBridge.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_PreservedUsage.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_TradeVirtual.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_ReadOnlyUI.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_CellOnlyDestinationFold.Reset));
            SDS_StaticState.Register(new ResetAction(SDS_CellOnlyWorkGiverPatches.ResetDiagnostics));
            // 静态清理登记：当前共 16 项（V4 的 12 项 + V5 的 SDS_ReadOnlyUI + W63 落点登记 + W63 诊断计数）。
            // §10.2 第 5 步：Vanilla 自检数据提供者（出口在 SDS.Diag）。
            SDS_SelfCheck.Register(new SDS_VanillaDiagnostics());
        }

        /// <summary>
        /// 启动第 2 步：装配 11 个宿主槽位。
        /// 构造顺序与早前 SDS_HostInstaller 一致；所有实现都不读存档，可在存档壳之前完成。
        /// </summary>
        internal static SDS_HostSlots CreateHostSlots()
        {
            SDS_EndpointProvider ports = new SDS_EndpointProvider();
            SDS_VanillaServices services = new SDS_VanillaServices();
            SDS_MirrorService mirrorQuery = new SDS_MirrorService();
            SDS_MirrorPhysicalOps mirrorPhysical = new SDS_MirrorPhysicalOps();
            SDS_MedicalCareAdapter medicalCare = new SDS_MedicalCareAdapter();
            SDS_ReclaimWorldAdapter reclaimWorld = new SDS_ReclaimWorldAdapter();
            SDS_DisplayAdapter display = new SDS_DisplayAdapter();
            SDS_DepositJobsAdapter depositJobs = new SDS_DepositJobsAdapter();
            SDS_TransportLoadAdapter transportLoad = new SDS_TransportLoadAdapter();

            return new SDS_HostSlots
            {
                ports = ports,
                depositFilter = services,
                priority = services,
                delivery = services,
                mirrorQuery = mirrorQuery,
                mirrorPhysical = mirrorPhysical,
                medicalCare = medicalCare,
                reclaimWorld = reclaimWorld,
                display = display,
                depositJobs = depositJobs,
                transportLoad = transportLoad
            };
        }

        internal static bool HasGrid
        {
            get { return SDS_GameComponent_Grid.Instance != null; }
        }

        // ---------- 设置页 / 热键实现 ----------

        internal static void NotifySettingsPageDrawn()
        {
            SDS_Hotkeys.NotifySettingsPageDrawn();
        }

        internal static bool HotkeyCapturing
        {
            get { return SDS_Hotkeys.Capturing; }
        }

        internal static string HotkeyCaptureNote
        {
            get { return SDS_Hotkeys.CaptureNote; }
        }

        internal static string HotkeyCaptureAction
        {
            get
            {
                KeyBindingDef def = SDS_Hotkeys.CaptureDef;
                if (def != null && def == SDS_KeyBindingDefOf.SDS_OpenSuperSpace)
                {
                    return HotkeyActionOpen;
                }
                if (def != null && def == SDS_KeyBindingDefOf.SDS_UseFromSuperSpace)
                {
                    return HotkeyActionUse;
                }
                return HotkeyActionOutput;
            }
        }

        internal static void BeginHotkeyCapture(string action)
        {
            KeyBindingDef def = ResolveHotkey(action);
            if (def != null)
            {
                SDS_Hotkeys.BeginCapture(def);
            }
        }

        internal static void CancelHotkeyCapture()
        {
            SDS_Hotkeys.CancelCapture();
        }

        internal static string GetHotkeyLabel(string action)
        {
            return SDS_Hotkeys.GetKeyLabel(ResolveHotkey(action));
        }

        internal static void ClearHotkey(string action)
        {
            SDS_Hotkeys.ClearBinding(ResolveHotkey(action));
        }

        internal static void ResetHotkeys()
        {
            SDS_Hotkeys.ResetDefaults();
        }

        /// <summary>把存档级优先级写进每座端点的原版 StorageSettings（设置页 / 启动共用）。</summary>
        internal static void ApplyStoragePriorityToEndpoints()
        {
            SDS_BuildingUtility.ApplyStoragePriorityToEndpoints();
        }

        private static KeyBindingDef ResolveHotkey(string action)
        {
            if (action == HotkeyActionOpen)
            {
                return SDS_KeyBindingDefOf.SDS_OpenSuperSpace;
            }
            if (action == HotkeyActionUse)
            {
                return SDS_KeyBindingDefOf.SDS_UseFromSuperSpace;
            }
            return SDS_KeyBindingDefOf.SDS_OutputFromSuperSpace;
        }
    }
}
