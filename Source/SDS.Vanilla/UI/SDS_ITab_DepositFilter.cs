using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 超维空间核心与锚点上的「存入筛选」页签。
    /// 存入筛选是全局唯一一套标准：页签直接画 State.DepositFilter，所有端点共用，改一处即全生效。
    /// 只有核心提供全局存储优先级；锚点与核心共用同一结果，不出现第二个优先级来源。
    /// </summary>
    internal class SDS_ITab_DepositFilter : ITab
    {
        private static readonly Vector2 WinSize = new Vector2(320f, 480f);
        private const float HintHeight = 80f;

        private readonly ThingFilterUI.UIState filterState = new ThingFilterUI.UIState();

        public SDS_ITab_DepositFilter()
        {
            size = WinSize;
            labelKey = "SDS.Tab.DepositFilter";
            tutorTag = null;
        }

        public override bool IsVisible
        {
            get
            {
                Thing thing = SelThing;
                if (thing == null || thing.Faction != Faction.OfPlayer)
                {
                    return false;
                }
                return thing is Building_SDS_Core || thing is Building_SDS_Port;
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();
            filterState.quickSearch.Reset();
        }

        public override void Notify_ClickOutsideWindow()
        {
            base.Notify_ClickOutsideWindow();
            filterState.quickSearch.Unfocus();
        }

        protected override void FillTab()
        {
            Rect rect = new Rect(0f, 0f, WinSize.x, WinSize.y).ContractedBy(10f);
            Widgets.BeginGroup(rect);
            try
            {
                SDS_GameComponent_Grid grid = SDS_GameComponent_Grid.Instance;
                if (grid == null || grid.State == null)
                {
                    Widgets.Label(new Rect(0f, 0f, rect.width, 30f),
                        "SDS.Window.NoGridHint".Translate().ToString());
                    return;
                }
                Rect filterRect = new Rect(0f, 0f, rect.width,
                    Mathf.Max(0f, rect.height - HintHeight - 4f));
                if (SelThing is Building_SDS_Core)
                {
                    const float TopAreaHeight = 35f;
                    Text.Font = GameFont.Small;
                    Rect priorityRect = new Rect(0f, 0f, 160f, TopAreaHeight - 6f);
                    StoragePriority current = SDS_VanillaState.CurrentStoragePriority;
                    if (Widgets.ButtonText(priorityRect,
                        "Priority".Translate() + ": " + current.Label().CapitalizeFirst()))
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        foreach (StoragePriority value in Enum.GetValues(typeof(StoragePriority)))
                        {
                            if (value == StoragePriority.Unstored)
                            {
                                continue;
                            }
                            StoragePriority local = value;
                            options.Add(new FloatMenuOption(local.Label().CapitalizeFirst(), delegate
                            {
                                grid.State.SetCoreStoragePriority(local);
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                    UIHighlighter.HighlightOpportunity(priorityRect, "StoragePriority");
                    string tip = "SDS.Tab.GlobalPriority".Translate().ToString()
                        + "\n" + "SDS.Tab.PriorityHint".Translate().ToString();
                    if (SDS_Config.IgnorePriorityMode)
                    {
                        tip += "\n" + "SDS.Tab.IgnorePriorityOn".Translate().ToString();
                    }
                    TooltipHandler.TipRegion(priorityRect, tip);
                    filterRect = new Rect(0f, TopAreaHeight, rect.width,
                        Mathf.Max(0f, rect.height - TopAreaHeight - HintHeight - 4f));
                }
                // parentFilter = null：本 Mod 没有"上级存储设置"（全局唯一一套标准）。
                ThingFilterUI.DoThingFilterConfigWindow(filterRect, filterState,
                    grid.State.DepositFilter, null, 1, null, null, false, false, false, null, null);
                DrawChannelHint(new Rect(0f, rect.height - HintHeight, rect.width, HintHeight));
            }
            finally
            {
                Widgets.EndGroup();
            }
        }

        /// <summary>只读通道说明：解释自动分流，不参与存入判定。</summary>
        private static void DrawChannelHint(Rect rect)
        {
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;
            Color prevColor = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = new Color(0.62f, 0.62f, 0.62f);
                Widgets.Label(rect, "SDS.Tab.ChannelHint".Translate().ToString());
            }
            finally
            {
                GUI.color = prevColor;
                Text.WordWrap = prevWrap;
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
            }
        }
    }
}
