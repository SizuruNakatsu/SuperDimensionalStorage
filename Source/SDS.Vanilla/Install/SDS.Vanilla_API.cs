namespace SuperDimensionalStorage
{
    // ============================================================================
    // SDS.Vanilla · 公开面（唯一 public 文件）
    // 规则：本程序集的 public 顶层类型只允许出现在本文件或登记的 engine-visible 表里（E8）。
    // 本文件只做契约声明与一行转发，不承载领域决策（E9）；实现见 SDS_VanillaImpl /
    // SDS_PatchInstallerImpl 与各 internal adapter。
    // 引擎按名实例化 / 序列化的类型（Building_SDS_*、JobDriver_SDS_*、SDS_Comp*、SDS_Proxy_*、
    // MapComponent_*、SDS_GameComponent_Grid、SDS_EndpointBuffer）不在此列，见登记表。
    // ============================================================================
    public static class SDS_Vanilla
    {
        public const string HotkeyActionOpen = SDS_VanillaImpl.HotkeyActionOpen;
        public const string HotkeyActionOutput = SDS_VanillaImpl.HotkeyActionOutput;
        public const string HotkeyActionUse = SDS_VanillaImpl.HotkeyActionUse;

        /// <summary>启动第 2 步：装配 11 个宿主槽位；由 Bootstrap 交给宿主安装入口。</summary>
        public static SDS_HostSlots CreateHostSlots() => SDS_VanillaImpl.CreateHostSlots();

        /// <summary>启动第 3 步：安装本程序集的特性补丁类，返回成功数；重复调用幂等。</summary>
        public static int InstallPatches() => SDS_PatchInstallerImpl.Install();

        /// <summary>启动第 6 步：Def 加载后重跑 W63 落点守卫，返回生效挂点数。</summary>
        public static int ApplyRuntimeHooks() => SDS_PatchInstallerImpl.ApplyRuntimeHooks();

        /// <summary>启动报告用：实际安装成功的特性补丁类数。</summary>
        public static int PatchAppliedCount => SDS_PatchInstallerImpl.AppliedCount;

        /// <summary>启动报告用：本程序集声明的特性补丁类数。</summary>
        public static int PatchDeclaredCount => SDS_PatchInstallerImpl.DeclaredCount;

        /// <summary>启动第 5 步：登记静态清理项与自检 provider；重复调用忽略。</summary>
        public static void RegisterAll() => SDS_VanillaImpl.RegisterAll();

        /// <summary>存档壳是否已建立（世界已进入、存档可用）。纯读。</summary>
        public static bool HasGrid => SDS_VanillaImpl.HasGrid;

        /// <summary>启动第 7 步：输出一次「使用命令」右键来源与排除表命中情况；重复调用忽略。</summary>
        public static void ReportUseProvidersOnce() => SDS_UseCommand.ReportProvidersOnce();

        // ---------- 设置页 / 热键 ----------

        /// <summary>设置页每一帧调用：让按键捕获知道设置窗口仍在绘制。</summary>
        public static void NotifySettingsPageDrawn() => SDS_VanillaImpl.NotifySettingsPageDrawn();

        public static bool HotkeyCapturing => SDS_VanillaImpl.HotkeyCapturing;

        public static string HotkeyCaptureNote => SDS_VanillaImpl.HotkeyCaptureNote;

        /// <summary>正在捕获的动作键（Open / Output / Use）。</summary>
        public static string HotkeyCaptureAction => SDS_VanillaImpl.HotkeyCaptureAction;

        public static void BeginHotkeyCapture(string action) => SDS_VanillaImpl.BeginHotkeyCapture(action);

        public static void CancelHotkeyCapture() => SDS_VanillaImpl.CancelHotkeyCapture();

        public static string GetHotkeyLabel(string action) => SDS_VanillaImpl.GetHotkeyLabel(action);

        public static void ClearHotkey(string action) => SDS_VanillaImpl.ClearHotkey(action);

        public static void ResetHotkeys() => SDS_VanillaImpl.ResetHotkeys();

        /// <summary>把存档级优先级写进每座端点的原版 StorageSettings（设置页 / 启动共用）。</summary>
        public static void ApplyStoragePriorityToEndpoints()
            => SDS_VanillaImpl.ApplyStoragePriorityToEndpoints();
    }
}
