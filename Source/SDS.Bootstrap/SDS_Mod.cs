using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Mod 入口（《模块规则》九）：读设置 → 宿主注入 → Harmony 安装 → 适配器扫描
    /// → 模块注册 → 延迟应用设置 → 启动报告。启动顺序与逐条日志在 SDS_Startup。
    /// 设置页只写 SDS_Settings 数据；容量 / 筛选 / 扣账规则全部留在 Core / Vanilla。
    /// </summary>
    public class SDS_Mod : Mod
    {
        public static SDS_Settings Settings;

        private static readonly string[] hotkeyActions = { "Open", "Output", "Use" };

        /// <summary>Mod 设置页的滚动位置（原版设置窗固定尺寸，本 Mod 条目较多）。</summary>
        private Vector2 settingsScroll = Vector2.zero;
        private const float SettingsContentHeight = 1200f;

        public SDS_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SDS_Settings>();
            if (Settings != null)
            {
                Settings.ClampAll();
            }
            // §10.2 第 1 步：运行期设置实例只在这里写入一次，Core / Vanilla 只读。
            SDS_Config.Current = Settings;
            SDS_Startup.Run(Settings);
        }

        public override string SettingsCategory()
        {
            return "SDS.Settings.Category".Translate().ToString();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            SDS_Vanilla.NotifySettingsPageDrawn();
            if (SDS_Vanilla.HotkeyCapturing)
            {
                DrawHotkeyCaptureOverlay(inRect);
                return;
            }
            if (Settings == null)
            {
                return;
            }

            Listing_Standard listing = new Listing_Standard();
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, inRect.width - 18f), SettingsContentHeight);
            Widgets.BeginScrollView(inRect, ref settingsScroll, viewRect);
            listing.Begin(viewRect);

            // 当前品类：放在初始容量上方，并在整行居中。
            if (SDS_Vanilla.HasGrid)
            {
                // 容量口径：上限只拒绝新品类；调低到低于已用数不会驱逐存量。
                TextAnchor anchorBefore = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                try
                {
                    listing.Label("SDS.Settings.CapacityUsage".Translate(
                        SDS_Core.UsedCapacity, SDS_Rules.TypeCapacity).ToString());
                }
                finally
                {
                    Text.Anchor = anchorBefore;
                }
            }

            listing.Label("SDS.Settings.GridCapacity".Translate(
                Settings.gridCapacity, SDS_Config.GridCapacityMin, SDS_Config.GridCapacityMax).ToString());
            Settings.gridCapacity = Mathf.RoundToInt(listing.Slider(
                Settings.gridCapacity, SDS_Config.GridCapacityMin, SDS_Config.GridCapacityMax));
            listing.Gap();

            listing.Label("SDS.Settings.CapacityPerArray".Translate(
                Settings.capacityPerArray, SDS_Config.CapacityPerArrayMin, SDS_Config.CapacityPerArrayMax).ToString());
            Settings.capacityPerArray = Mathf.RoundToInt(listing.Slider(
                Settings.capacityPerArray, SDS_Config.CapacityPerArrayMin, SDS_Config.CapacityPerArrayMax));
            listing.Gap();

            listing.Label("SDS.Settings.CorePower".Translate(
                Mathf.RoundToInt(Settings.corePower), (int)SDS_Config.CorePowerMin, (int)SDS_Config.CorePowerMax).ToString());
            Settings.corePower = listing.Slider(Settings.corePower, SDS_Config.CorePowerMin, SDS_Config.CorePowerMax);
            listing.Gap();

            listing.Label("SDS.Settings.ArrayPower".Translate(
                Mathf.RoundToInt(Settings.arrayPower), (int)SDS_Config.ArrayPowerMin, (int)SDS_Config.ArrayPowerMax).ToString());
            Settings.arrayPower = listing.Slider(Settings.arrayPower, SDS_Config.ArrayPowerMin, SDS_Config.ArrayPowerMax);
            listing.Gap();

            listing.Label("SDS.Settings.PortPower".Translate(
                Mathf.RoundToInt(Settings.portPower), (int)SDS_Config.PortPowerMin, (int)SDS_Config.PortPowerMax).ToString());
            Settings.portPower = listing.Slider(Settings.portPower, SDS_Config.PortPowerMin, SDS_Config.PortPowerMax);
            listing.Gap();

            listing.Label("SDS.Settings.ZeroMeansNoPower".Translate().ToString());
            listing.Gap();

            // 品类计数口径：默认都分开＝原有行为，只影响容量计数，不改变取出还原。
            bool separateQualityBefore = Settings.separateByQuality;
            bool separateStuffBefore = Settings.separateByStuff;
            listing.CheckboxLabeled("SDS.Settings.SeparateQuality".Translate().ToString(),
                ref Settings.separateByQuality);
            listing.CheckboxLabeled("SDS.Settings.SeparateStuff".Translate().ToString(),
                ref Settings.separateByStuff);
            if (separateQualityBefore != Settings.separateByQuality
                || separateStuffBefore != Settings.separateByStuff)
            {
                // 品类集合与键索引都依赖这两个开关，切换后立即失效，避免读到旧分组。
                SDS_Core.NotifyCapacitySettingsChanged();
            }
            listing.Label("SDS.Settings.SeparateDesc".Translate().ToString());
            listing.Gap();

            listing.CheckboxLabeled("SDS.Settings.ExcludeFromWealth".Translate().ToString(),
                ref Settings.excludeGridFromWealth);
            listing.Gap();

            bool playerOnlyBefore = Settings.playerFactionOnlyAccess;
            listing.CheckboxLabeled("SDS.Settings.PlayerFactionOnlyAccess".Translate().ToString(),
                ref Settings.playerFactionOnlyAccess);
            if (!playerOnlyBefore && Settings.playerFactionOnlyAccess)
            {
                // 首次勾选先退回 false，确认后才真正开启。
                Settings.playerFactionOnlyAccess = false;
                ConfirmPlayerFactionOnlyAccess();
            }
            listing.Gap();

            bool ignorePriorityBefore = Settings.highestStoragePriority;
            listing.CheckboxLabeled("SDS.Settings.HighestStoragePriority".Translate().ToString(),
                ref Settings.highestStoragePriority);
            if (!ignorePriorityBefore && Settings.highestStoragePriority)
            {
                // W55：首次勾选先退回 false，确认后才真正开启；确认时关闭流出（两种优先级模式互斥）。
                Settings.highestStoragePriority = false;
                ConfirmHighestStoragePriority();
            }
            listing.Gap();

            bool outflowBefore = Settings.allowPriorityOutflow;
            listing.CheckboxLabeled("SDS.Settings.AllowPriorityOutflow".Translate().ToString(),
                ref Settings.allowPriorityOutflow);
            if (!outflowBefore && Settings.allowPriorityOutflow)
            {
                // 首次勾选先退回 false，确认后才真正开启；确认时再处理与最高优先级的互斥。
                Settings.allowPriorityOutflow = false;
                ConfirmAllowPriorityOutflow();
            }
            listing.Gap();

            // W33：武器/装备强制虚拟化。默认关闭；打开时必须先弹窗确认。
            bool forceGearBefore = Settings.forceWeaponApparelVirtual;
            listing.CheckboxLabeled("SDS.Settings.ForceGearVirtual".Translate().ToString(),
                ref Settings.forceWeaponApparelVirtual);
            if (!forceGearBefore && Settings.forceWeaponApparelVirtual)
            {
                // 先退回 false，只有玩家在确认框点「确认」才真正开启。
                Settings.forceWeaponApparelVirtual = false;
                ConfirmForceGearVirtual();
            }
            listing.Label("SDS.Settings.ForceGearVirtualDesc".Translate().ToString());

            DrawHotkeySection(listing);

            listing.Gap(12f);
            if (listing.ButtonText("SDS.Settings.ResetDefaults".Translate().ToString()))
            {
                Settings.ResetToDefaults();
                // 立刻把新值应用到建筑 Def 与财富口径，不必等关窗。
                SDS_SettingsApplier.ApplyPowerAndPriority(Settings);
                SDS_SettingsApplier.RefreshWealth();
            }
            listing.End();
            Widgets.EndScrollView();

            Settings.ClampAll();
        }

        /// <summary>S18 开关打开前的确认；取消保持关闭。</summary>
        private static void ConfirmPlayerFactionOnlyAccess()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "SDS.Settings.PlayerFactionOnlyAccess.Confirm".Translate(),
                delegate
                {
                    Settings.playerFactionOnlyAccess = true;
                },
                delegate
                {
                    // 取消：保持关闭（此前的勾选已被立即退回）。
                },
                destructive: false,
                title: "SDS.Settings.PlayerFactionOnlyAccess.Title".Translate().ToString()));
        }

        /// <summary>W55 开关打开前的确认；取消保持关闭，确认时关闭流出（两种优先级模式互斥）。</summary>
        private static void ConfirmHighestStoragePriority()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "SDS.Settings.HighestStoragePriority.Confirm".Translate(),
                delegate
                {
                    Settings.highestStoragePriority = true;
                    Settings.allowPriorityOutflow = false;
                    SDS_Rules.NotifyEndpointChanged();
                    SDS_Vanilla.ApplyStoragePriorityToEndpoints();
                },
                delegate
                {
                    // 取消：保持关闭（此前的勾选已被立即退回）。
                },
                destructive: false,
                title: "SDS.Settings.HighestStoragePriority.Title".Translate().ToString()));
        }

        /// <summary>W52 开关打开前的确认；取消保持关闭，确认时关闭最高优先级。</summary>
        private static void ConfirmAllowPriorityOutflow()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "SDS.Settings.AllowPriorityOutflow.Confirm".Translate(),
                delegate
                {
                    Settings.allowPriorityOutflow = true;
                    bool ignoreWasOn = Settings.highestStoragePriority;
                    Settings.highestStoragePriority = false;
                    if (ignoreWasOn)
                    {
                        SDS_Rules.NotifyEndpointChanged();
                        SDS_Vanilla.ApplyStoragePriorityToEndpoints();
                    }
                },
                delegate
                {
                    // 取消：保持关闭（此前的勾选已被立即退回）。
                },
                destructive: false,
                title: "SDS.Settings.AllowPriorityOutflow.Title".Translate().ToString()));
        }

        /// <summary>
        /// W33 开关打开前的确认。确认后才真正置位；取消则保持关闭。
        /// 交易 / 编成 / 装载窗口打开时拒绝，避免玩家正持有保真真身引用。
        /// </summary>
        private static void ConfirmForceGearVirtual()
        {
            bool inGame = Current.ProgramState == ProgramState.Playing
                && SDS_Vanilla.HasGrid;
            if (inGame && HasBlockingStockWindow())
            {
                Messages.Message("SDS.Settings.ForceGearVirtual.Blocked".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            string textKey = inGame
                ? "SDS.Settings.ForceGearVirtual.Confirm"
                : "SDS.Settings.ForceGearVirtual.ConfirmNoGame";
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                textKey.Translate(),
                delegate
                {
                    Settings.forceWeaponApparelVirtual = true;
                    int converted = 0;
                    if (Current.ProgramState == ProgramState.Playing
                        && SDS_Vanilla.HasGrid)
                    {
                        converted = SDS_Rules.ConvertPreservedGearToVirtual();
                    }
                    if (Current.ProgramState == ProgramState.Playing)
                    {
                        TaggedString message = converted > 0
                            ? "SDS.Settings.ForceGearVirtual.Done".Translate(converted)
                            : "SDS.Settings.ForceGearVirtual.None".Translate();
                        Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
                    }
                },
                delegate
                {
                    // 取消：保持关闭（此前的勾选已被立即退回）。
                },
                destructive: true,
                title: "SDS.Settings.ForceGearVirtual.Title".Translate().ToString()));
        }

        /// <summary>
        /// W34：交易 / 编成 / 装载窗口打开时，玩家可能正持有保真真身引用；
        /// 此时禁止切换。用类型名判定，避免硬引用可选 DLC / 版本差异的类型。
        /// </summary>
        private static bool HasBlockingStockWindow()
        {
            if (Find.WindowStack == null || Find.WindowStack.Windows == null)
            {
                return false;
            }
            IList<Window> windows = Find.WindowStack.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (window == null)
                {
                    continue;
                }
                string name = window.GetType().Name;
                if (name == "Dialog_Trade" || name == "Dialog_LoadTransporters"
                    || name == "Dialog_FormCaravan" || name == "Dialog_LoadShips")
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 快捷键区：三个入口各一行，[设置] 进入按键捕获，[清除] 置为无，底部恢复默认。
        /// 数据仍在原版 KeyPrefs 里，这里只是同一份键位的编辑界面。
        /// </summary>
        private static void DrawHotkeySection(Listing_Standard listing)
        {
            listing.Gap(12f);
            listing.Label("SDS.Settings.Hotkeys".Translate().ToString());
            DrawHotkeyRow(listing, "SDS.Settings.HotkeyOpen", hotkeyActions[0]);
            listing.Gap(2f);
            DrawHotkeyRow(listing, "SDS.Settings.HotkeyOutput", hotkeyActions[1]);
            listing.Gap(2f);
            DrawHotkeyRow(listing, "SDS.Settings.HotkeyUse", hotkeyActions[2]);
            listing.Gap(4f);
            if (listing.ButtonText("SDS.Settings.HotkeyReset".Translate().ToString()))
            {
                SDS_Vanilla.ResetHotkeys();
            }
            listing.Label("SDS.Settings.HotkeyNote".Translate().ToString());
        }

        private static void DrawHotkeyRow(Listing_Standard listing, string labelKey, string action)
        {
            Rect row = listing.GetRect(30f);
            float labelWidth = Mathf.Max(120f, row.width * 0.36f);
            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            const float keyWidth = 84f;
            const float buttonWidth = 58f;
            Rect keyRect = new Rect(labelRect.xMax, row.y, keyWidth, row.height);
            Rect setRect = new Rect(keyRect.xMax + 6f, row.y, buttonWidth, row.height);
            Rect clearRect = new Rect(setRect.xMax + 6f, row.y, buttonWidth, row.height);

            Widgets.Label(labelRect, labelKey.Translate().ToString());
            Widgets.DrawBoxSolid(keyRect, new Color(0.15f, 0.15f, 0.15f));
            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(keyRect, SDS_Vanilla.GetHotkeyLabel(action));
            Text.Anchor = prevAnchor;

            if (Widgets.ButtonText(setRect, "SDS.Settings.HotkeySet".Translate().ToString()))
            {
                SDS_Vanilla.BeginHotkeyCapture(action);
            }
            if (Widgets.ButtonText(clearRect, "SDS.Settings.HotkeyClear".Translate().ToString()))
            {
                SDS_Vanilla.ClearHotkey(action);
            }
        }

        private static void DrawHotkeyCaptureOverlay(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, new Color(0f, 0f, 0f, 0.55f));
            Rect box = new Rect(inRect.center.x - 240f, inRect.center.y - 70f, 480f, 150f);
            Widgets.DrawBoxSolid(box, new Color(0.14f, 0.14f, 0.14f));

            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = true;

                string action = SDS_Vanilla.HotkeyCaptureAction;
                Widgets.Label(new Rect(box.x + 12f, box.y + 10f, box.width - 24f, 26f),
                    "SDS.Settings.HotkeyCaptureTitle".Translate(
                        HotkeyActionLabel(action).Translate()).ToString());
                Widgets.Label(new Rect(box.x + 12f, box.y + 40f, box.width - 24f, 42f),
                    "SDS.Settings.HotkeyCaptureHint".Translate().ToString());
                string note = SDS_Vanilla.HotkeyCaptureNote;
                if (!string.IsNullOrEmpty(note))
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(1f, 0.7f, 0.4f);
                    Widgets.Label(new Rect(box.x + 12f, box.y + 84f, box.width - 24f, 24f), note);
                    GUI.color = prev;
                }
                if (Widgets.ButtonText(new Rect(box.center.x - 60f, box.yMax - 38f, 120f, 28f),
                    "SDS.Settings.HotkeyCancel".Translate().ToString()))
                {
                    SDS_Vanilla.CancelHotkeyCapture();
                }
            }
            finally
            {
                Text.WordWrap = prevWrap;
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
            }
        }

        private static string HotkeyActionLabel(string action)
        {
            if (action == hotkeyActions[0])
            {
                return "SDS.Settings.HotkeyOpen";
            }
            return action == hotkeyActions[2]
                ? "SDS.Settings.HotkeyUse"
                : "SDS.Settings.HotkeyOutput";
        }

        public override void WriteSettings()
        {
            SDS_Vanilla.CancelHotkeyCapture();
            if (Settings != null)
            {
                Settings.ClampAll();
            }
            // 原版 Dialog_ModSettings 在窗口出栈前调用本方法；副作用必须自吞异常，
            // 否则设置窗口会留在窗口栈上，玩家只能杀进程。
            try
            {
                SDS_SettingsApplier.ApplyPowerAndPriority(Settings);
                SDS_SettingsApplier.RefreshWealth();
            }
            catch (Exception e)
            {
                Log.Error("[超维存储] 应用设置失败（设置仍会保存，且不影响关闭设置窗口）：" + e);
            }
            base.WriteSettings();
        }
    }
}
