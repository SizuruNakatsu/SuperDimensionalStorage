using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 本 Mod 的三个快捷键定义；字段名必须与 Def 的 defName 完全一致。
    /// 放在 Game 分类：建造模式下由 SDS_Hotkeys.MustYieldHotkey 主动让位。
    /// </summary>
    [DefOf]
    internal static class SDS_KeyBindingDefOf
    {
        public static KeyBindingDef SDS_OpenSuperSpace;
        public static KeyBindingDef SDS_OutputFromSuperSpace;
        public static KeyBindingDef SDS_UseFromSuperSpace;
    }

    /// <summary>
    /// 侧边栏的输入路由与快捷键设置（只处理 UI / 交互）：
    ///  - C：非建造模式开关侧边栏；建造模式完全让给原版。
    ///  - Q：侧边栏存在且有选中行时优先输出。
    ///  - E：条件同 Q，进入「使用」流程。
    ///  - 让位口径统一由 MustYieldHotkey 决定；ESC 在建造模式不关窗。
    ///  - 快捷键本身存进原版 KeyPrefs，Mod 设置页只是同一份数据的编辑入口。
    /// </summary>
    internal static class SDS_Hotkeys
    {
        private static bool bindingsEnsured;
        private static int lastSettingsFrame = -1000;

        public static bool Capturing { get; private set; }
        public static KeyBindingDef CaptureDef { get; private set; }
        public static string CaptureNote { get; private set; }

        public static KeyBindingDef OpenDef
        {
            get { return SDS_KeyBindingDefOf.SDS_OpenSuperSpace; }
        }

        public static KeyBindingDef OutputDef
        {
            get { return SDS_KeyBindingDefOf.SDS_OutputFromSuperSpace; }
        }

        public static KeyBindingDef UseDef
        {
            get { return SDS_KeyBindingDefOf.SDS_UseFromSuperSpace; }
        }

        /// <summary>建造模式：Architect 菜单打开，或任意设计器处于选中状态。</summary>
        public static bool InBuildMode
        {
            get
            {
                DesignatorManager manager = Find.DesignatorManager;
                if (manager != null && manager.SelectedDesignator != null)
                {
                    return true;
                }
                MainButtonDef architect = MainButtonDefOf.Architect;
                return Find.MainTabsRoot != null && architect != null
                    && Find.MainTabsRoot.OpenTab == architect;
            }
        }

        /// <summary>
        /// 全局快捷键的公共前置：游戏中、有地图、非世界视图（Planet）、没在输入 / 模态窗口里。
        /// 判据用 WorldSelected 而不是 WorldRendered：太空图（renderWorld）在地图视图下是 Background，
        /// 仍属于「有地图」；用 WorldRendered 会把轨道地图上的 C / Q / E 一并挡掉。
        /// </summary>
        public static bool CanUseGlobalHotkeys()
        {
            EnsureBindings();
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
            {
                return false;
            }
            if (WorldRendererUtility.WorldSelected || Find.WindowStack == null)
            {
                return false;
            }
            if (KeyPrefs.KeyPrefsData == null)
            {
                return false;
            }
            if (Find.WindowStack.AnySearchWidgetFocused || Find.WindowStack.AnyWindowAbsorbingAllInput)
            {
                return false;
            }
            return true;
        }

        public static void ToggleSidebar()
        {
            if (Find.WindowStack == null)
            {
                return;
            }
            Window_SuperSpace open = Find.WindowStack.WindowOfType<Window_SuperSpace>();
            if (open != null)
            {
                open.Close();
                return;
            }
            Find.WindowStack.Add(new Window_SuperSpace());
        }

        /// <summary>
        /// 全局快捷键的统一让位口径（原版 / 第三方优先）：命中任意一条就绝不消费事件。
        /// 注意 GizmoGridDrawer.drawnHotKeys 在绘制开始时清空，这里读到的是上一帧集合（1 帧延迟足够）。
        /// </summary>
        public static bool MustYieldHotkey(KeyCode code)
        {
            if (InBuildMode)
            {
                return true;
            }
            if (code != KeyCode.None && GizmoGridDrawer.drawnHotKeys.Contains(code))
            {
                return true;
            }
            Targeter targeter = Find.Targeter;
            if (targeter != null && targeter.IsTargeting)
            {
                return true;
            }
            return !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());
        }

        /// <summary>Q 的输出条件：侧边栏存在、有选中行（让位口径由 MustYieldHotkey 统一判断）。</summary>
        public static bool TryOutputFromSidebar()
        {
            if (Find.WindowStack == null)
            {
                return false;
            }
            Window_SuperSpace win = Find.WindowStack.WindowOfType<Window_SuperSpace>();
            if (win == null || !win.HasSelectedRow)
            {
                return false;
            }
            if (!Find.WindowStack.GetsInput(win))
            {
                return false;
            }
            return win.TryOpenOutputFromHotkey();
        }

        public static bool TryUseFromSidebar()
        {
            if (Find.WindowStack == null)
            {
                return false;
            }
            Window_SuperSpace win = Find.WindowStack.WindowOfType<Window_SuperSpace>();
            if (win == null || !win.HasSelectedRow)
            {
                return false;
            }
            if (!Find.WindowStack.GetsInput(win))
            {
                return false;
            }
            return win.TryOpenUseFromHotkey();
        }

        public static void NotifySettingsPageDrawn()
        {
            lastSettingsFrame = Time.frameCount;
        }

        public static void BeginCapture(KeyBindingDef def)
        {
            if (def == null)
            {
                return;
            }
            EnsureBindings();
            Capturing = true;
            CaptureDef = def;
            CaptureNote = null;
            lastSettingsFrame = Time.frameCount;
        }

        public static void CancelCapture()
        {
            Capturing = false;
            CaptureDef = null;
            CaptureNote = null;
        }

        public static string GetKeyLabel(KeyBindingDef def)
        {
            if (def == null)
            {
                return "?";
            }
            EnsureBindings();
            if (KeyPrefs.KeyPrefsData == null)
            {
                return "?";
            }
            KeyCode code = def.MainKey;
            return code == KeyCode.None
                ? "SDS.Settings.HotkeyNone".Translate().ToString()
                : code.ToString();
        }

        public static string HotkeyHintText()
        {
            return "SDS.Window.HotkeyHint".Translate(GetKeyLabel(OpenDef), GetKeyLabel(OutputDef),
                GetKeyLabel(UseDef)).ToString();
        }

        public static void ClearBinding(KeyBindingDef def)
        {
            if (def == null)
            {
                return;
            }
            EnsureBindings();
            SetBindingRaw(def, KeyCode.None);
            SaveBindings();
        }

        public static void ResetDefaults()
        {
            EnsureBindings();
            ResetOne(OpenDef);
            ResetOne(OutputDef);
            ResetOne(UseDef);
            SaveBindings();
            CancelCapture();
        }

        private static void ResetOne(KeyBindingDef def)
        {
            if (def == null)
            {
                return;
            }
            SetBindingRaw(def, def.GetDefaultKeyCode(KeyPrefs.BindingSlot.A));
        }

        /// <summary>设置页的按键捕获。返回 true 表示本次事件已被消费。</summary>
        public static bool TryCaptureKey(Event current)
        {
            if (!Capturing || current == null || current.type != EventType.KeyDown)
            {
                return false;
            }
            KeyCode code = current.keyCode;
            if (code == KeyCode.None)
            {
                return false;
            }
            if (code == KeyCode.Escape)
            {
                ClearBinding(CaptureDef);
                CancelCapture();
                current.Use();
                return true;
            }
            if (code == KeyCode.Return || code == KeyCode.KeypadEnter)
            {
                CaptureNote = "SDS.Settings.HotkeyReserved".Translate().ToString();
                current.Use();
                return true;
            }
            if (IsModifierOnly(code))
            {
                CaptureNote = "SDS.Settings.HotkeyModifierOnly".Translate().ToString();
                current.Use();
                return true;
            }
            if (current.control || current.alt || current.shift || current.command)
            {
                CaptureNote = "SDS.Settings.HotkeyNoModifiers".Translate().ToString();
                current.Use();
                return true;
            }
            // 本 Mod 自己的三个键之间不允许重复：规则只在这一处。
            foreach (KeyBindingDef own in OwnDefs())
            {
                if (own != null && own != CaptureDef && KeyPrefs.KeyPrefsData != null
                    && own.MainKey == code)
                {
                    CaptureNote = "SDS.Settings.HotkeyDuplicate".Translate().ToString();
                    current.Use();
                    return true;
                }
            }

            KeyBindingDef captured = CaptureDef;
            SetKey(captured, code);
            string conflict = FindConflictLabel(code, captured);
            CancelCapture();
            current.Use();
            if (conflict != null && code != KeyCode.C && code != KeyCode.Q)
            {
                Messages.Message("SDS.Settings.HotkeyConflict".Translate(conflict),
                    MessageTypeDefOf.RejectInput, false);
            }
            return true;
        }

        private static void SetKey(KeyBindingDef def, KeyCode code)
        {
            if (def == null)
            {
                return;
            }
            SetBindingRaw(def, code);
            SaveBindings();
        }

        private static void SetBindingRaw(KeyBindingDef def, KeyCode code)
        {
            EnsureBindings();
            if (KeyPrefs.KeyPrefsData == null)
            {
                return;
            }
            if (!KeyPrefs.KeyPrefsData.SetBinding(def, KeyPrefs.BindingSlot.A, code))
            {
                KeyPrefs.KeyPrefsData.AddMissingDefaultBindings();
                KeyPrefs.KeyPrefsData.SetBinding(def, KeyPrefs.BindingSlot.A, code);
            }
            KeyPrefs.KeyPrefsData.SetBinding(def, KeyPrefs.BindingSlot.B, KeyCode.None);
        }

        private static void SaveBindings()
        {
            if (KeyPrefs.KeyPrefsData != null)
            {
                KeyPrefs.Save();
            }
        }

        private static void EnsureBindings()
        {
            if (bindingsEnsured || KeyPrefs.KeyPrefsData == null)
            {
                return;
            }
            KeyPrefs.KeyPrefsData.AddMissingDefaultBindings();
            bindingsEnsured = true;
        }

        private static bool IsModifierOnly(KeyCode code)
        {
            switch (code)
            {
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                case KeyCode.LeftCommand:
                case KeyCode.RightCommand:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>本 Mod 自己的三个快捷键：重复绑定校验与冲突提示共用这一份名单（准则三）。</summary>
        private static IEnumerable<KeyBindingDef> OwnDefs()
        {
            if (OpenDef != null)
            {
                yield return OpenDef;
            }
            if (OutputDef != null)
            {
                yield return OutputDef;
            }
            if (UseDef != null)
            {
                yield return UseDef;
            }
        }

        private static bool IsOwnHotkey(KeyBindingDef def)
        {
            foreach (KeyBindingDef own in OwnDefs())
            {
                if (own == def)
                {
                    return true;
                }
            }
            return false;
        }

        private static string FindConflictLabel(KeyCode code, KeyBindingDef except)
        {
            if (KeyPrefs.KeyPrefsData == null)
            {
                return null;
            }
            foreach (KeyBindingDef def in DefDatabase<KeyBindingDef>.AllDefs)
            {
                if (def == null || def == except || IsOwnHotkey(def))
                {
                    continue;
                }
                if (def.MainKey == code)
                {
                    return def.LabelCap.ToString();
                }
            }
            return null;
        }

        internal static bool CaptureIsStale()
        {
            if (!Capturing)
            {
                return false;
            }
            if (Time.frameCount - lastSettingsFrame > 2)
            {
                return true;
            }
            return Find.WindowStack == null
                || Find.WindowStack.WindowOfType<Dialog_ModSettings>() == null;
        }
    }

    /// <summary>
    /// 输入路由统一入口（WindowStack.HandleEventsHighPriority 之前）：
    /// 捕获模式最先处理；中键确认输出；C / Q / E 只在明确满足条件时消费事件。
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.HandleEventsHighPriority))]
    internal static class Patch_SDS_Hotkeys
    {
        private static bool Prefix()
        {
            try
            {
                if (SDS_Hotkeys.Capturing)
                {
                    if (SDS_Hotkeys.CaptureIsStale())
                    {
                        SDS_Hotkeys.CancelCapture();
                    }
                    else
                    {
                        return !SDS_Hotkeys.TryCaptureKey(Event.current);
                    }
                }

                // 输出数量框：鼠标中键确认。必须在 CanUseGlobalHotkeys 之前判断
                //（该对话框吸收输入，CanUseGlobalHotkeys 会直接返回 false）。
                if (Event.current != null && Event.current.type == EventType.MouseDown
                    && Event.current.button == 2 && Find.WindowStack != null)
                {
                    Window_SDS_OutputDialog dialog =
                        Find.WindowStack.WindowOfType<Window_SDS_OutputDialog>();
                    if (dialog != null && Find.WindowStack.GetsInput(dialog))
                    {
                        Event.current.Use();
                        dialog.ConfirmFromMiddleClick();
                        return false;
                    }
                }

                if (!SDS_Hotkeys.CanUseGlobalHotkeys())
                {
                    return true;
                }

                KeyBindingDef open = SDS_Hotkeys.OpenDef;
                if (open != null && open.KeyDownEvent
                    && !SDS_Hotkeys.MustYieldHotkey(open.MainKey))
                {
                    Event.current.Use();
                    SDS_Hotkeys.ToggleSidebar();
                    return false;
                }

                KeyBindingDef output = SDS_Hotkeys.OutputDef;
                if (output != null && output.KeyDownEvent
                    && !SDS_Hotkeys.MustYieldHotkey(output.MainKey)
                    && SDS_Hotkeys.TryOutputFromSidebar())
                {
                    Event.current.Use();
                    return false;
                }

                KeyBindingDef use = SDS_Hotkeys.UseDef;
                if (use != null && use.KeyDownEvent
                    && !SDS_Hotkeys.MustYieldHotkey(use.MainKey)
                    && SDS_Hotkeys.TryUseFromSidebar())
                {
                    Event.current.Use();
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("快捷键路由失败（已忽略，原版输入不受影响）", e);
                return true;
            }
        }
    }
}
