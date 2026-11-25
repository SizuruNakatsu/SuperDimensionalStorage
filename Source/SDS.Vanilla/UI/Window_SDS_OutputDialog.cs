using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SuperDimensionalStorage
{

    /// <summary>
    /// 「输出」小框：为选中的那一行选数量与记录，然后进入地图点选。
    /// 数量区按定稿拆两栏：第一栏「左箭头 + 数字框 + 右箭头 + 全部」，第二栏「-100 -10 +10 +100」。
    /// </summary>
    internal class Window_SDS_OutputDialog : Window
    {
        private const int DefaultAmount = 1000;

        private const float TitleH = 26f;
        private const float RecordsLabelH = 20f;
        private const float RowH = 26f;
        private const float RowGap = 6f;
        private const float ElementGap = 4f;
        private const float ConfirmH = 30f;
        private const float ArrowW = 26f;
        private const float FieldW = 84f;
        private const float MinInnerWidth = 360f;
        private const float MaxInnerWidth = 620f;
        private const float WindowMargin = 36f;

        private static readonly string[] StepTexts = { "-100", "-10", "+10", "+100" };
        private static readonly long[] StepDeltas = { -100L, -10L, 10L, 100L };

        internal enum DialogMode
        {
            Output,
            Use,
            Install
        }

        private SDS_StockKey key;
        private bool preserved;
        private List<SDS_StockEntry> members;

        private sealed class MemberButton
        {
            public SDS_StockEntry entry;
            public float width;
        }

        private List<List<MemberButton>> memberRows;
        private float innerWidth = MinInnerWidth;
        private float contentHeight = TitleH + RowGap + RowH + RowGap + ConfirmH;
        private float recordsLabelWidth;
        private float amountLabelWidth;
        private float allButtonWidth;
        private float confirmWidth;
        private float[] stepWidths;
        private string recordsLabelText;
        private string amountLabelText;
        private string confirmText;

        private string amountBuffer = DefaultAmount.ToString();
        private bool amountInitialized;
        private GUIStyle centeredFieldStyle;

        private readonly DialogMode dialogMode;

        public override Vector2 InitialSize
        {
            get { return new Vector2(innerWidth + WindowMargin, contentHeight + WindowMargin); }
        }

        public Window_SDS_OutputDialog(SDS_StockKey key)
            : this(key, false)
        {
        }

        public Window_SDS_OutputDialog(SDS_StockKey key, bool preserved)
            : this(key, preserved, null)
        {
        }

        public Window_SDS_OutputDialog(SDS_StockKey key, bool preserved, List<SDS_StockEntry> members)
            : this(key, preserved, members, DialogMode.Output)
        {
        }

        public Window_SDS_OutputDialog(SDS_StockKey key, bool preserved,
            List<SDS_StockEntry> members, DialogMode mode)
        {
            dialogMode = mode;
            this.key = key;
            this.preserved = preserved;
            // 展示行的 key 可能是聚合代表键；取用一律落到成员的真实键。
            // 默认成员按「可用量最大」选（不是数量最大），避免默认命中被禁用/预留的那条。
            if (members != null && members.Count > 0)
            {
                SDS_StockEntry best = Window_SuperSpace.PickDefaultMember(members);
                if (best != null && best.key != null)
                {
                    this.key = best.key;
                    this.preserved = best.key.preserved;
                }
                if (members.Count > 1)
                {
                    this.members = members;
                }
            }
            BuildContentSize();
            doCloseX = true;
            draggable = true;
            resizeable = false;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnClickedOutside = false;
            // 数量确认框打开期间禁止镜头移动：中键同时是原版拖镜头键。
            preventCameraMotion = true;
        }

        private void BuildContentSize()
        {
            bool useMode = dialogMode == DialogMode.Use;
            bool installMode = dialogMode == DialogMode.Install;
            bool noAmount = useMode || installMode;
            recordsLabelText = (useMode
                ? "SDS.Use.Records"
                : (installMode ? "SDS.Install.Records" : "SDS.Window.OutputRecords"))
                .Translate().ToString();
            amountLabelText = "SDS.Window.OutputAmount".Translate().ToString();
            confirmText = (useMode
                ? "SDS.Use.Confirm"
                : (installMode ? "SDS.Install.Confirm" : "SDS.Window.OutputConfirm"))
                .Translate().ToString();

            recordsLabelWidth = MeasureSmall(recordsLabelText).x;
            amountLabelWidth = MeasureSmall(amountLabelText).x + 2f;
            confirmWidth = Mathf.Max(120f, MeasureSmall(confirmText).x + 24f);

            stepWidths = new float[StepTexts.Length];
            float stepsRowWidth = 0f;
            for (int i = 0; i < StepTexts.Length; i++)
            {
                stepWidths[i] = ButtonWidthFor(StepTexts[i]);
                stepsRowWidth += stepWidths[i] + (i < StepTexts.Length - 1 ? ElementGap : 0f);
            }
            allButtonWidth = ButtonWidthFor("SDS.Window.OutputAll".Translate().ToString());

            float amountRowWidth = noAmount ? 0f : Mathf.Max(
                amountLabelWidth + ElementGap + ArrowW + ElementGap + FieldW + ElementGap + ArrowW
                    + RowGap + allButtonWidth,
                stepsRowWidth);

            long maxForMeasure = key != null ? SDS_Availability.GetAvailableCount(key) : 0L;
            string titleText = useMode
                ? "SDS.Use.Title".Translate(LabelOf(key)).ToString()
                : (installMode
                    ? "SDS.Install.Title".Translate(LabelOf(key)).ToString()
                    : "SDS.Window.OutputSelected".Translate(LabelOf(key), maxForMeasure).ToString());
            float titleWidth = MeasureSmall(titleText).x;

            innerWidth = Mathf.Clamp(
                Mathf.Max(amountRowWidth, Mathf.Max(titleWidth,
                    Mathf.Max(recordsLabelWidth, confirmWidth))),
                MinInnerWidth, MaxInnerWidth);

            BuildMemberRows();

            float height = TitleH;
            if (memberRows != null)
            {
                height += RowGap + RecordsLabelH + RowGap
                    + memberRows.Count * RowH + (memberRows.Count - 1) * RowGap;
            }
            height += noAmount
                ? RowGap + ConfirmH
                : RowGap + RowH + RowGap + RowH + RowGap + ConfirmH;
            contentHeight = height;
        }

        private static string LabelOf(SDS_StockKey stockKey)
        {
            return stockKey != null ? SDS_OutputCommand.KeyLabel(stockKey) : "?";
        }

        private void BuildMemberRows()
        {
            memberRows = null;
            if (members == null || members.Count == 0)
            {
                return;
            }
            List<List<MemberButton>> rows = new List<List<MemberButton>>();
            List<MemberButton> row = new List<MemberButton>();
            float used = 0f;
            for (int i = 0; i < members.Count; i++)
            {
                SDS_StockEntry member = members[i];
                if (member == null || member.key == null)
                {
                    continue;
                }
                MemberButton button = new MemberButton
                {
                    entry = member,
                    width = Mathf.Clamp(MeasureSmall(MemberLabelText(member, true)).x + 16f,
                        110f, innerWidth)
                };
                if (row.Count > 0 && used + ElementGap + button.width > innerWidth)
                {
                    rows.Add(row);
                    row = new List<MemberButton>();
                    used = 0f;
                }
                if (row.Count > 0)
                {
                    used += ElementGap;
                }
                row.Add(button);
                used += button.width;
            }
            if (row.Count > 0)
            {
                rows.Add(row);
            }
            if (rows.Count > 0)
            {
                memberRows = rows;
            }
        }

        private static string MemberLabelText(SDS_StockEntry member, bool selected)
        {
            string text = (selected ? "> " : string.Empty) + LabelOf(member.key) + " x" + member.count;
            if (member != null && member.key != null)
            {
                long available = SDS_Availability.GetAvailableCount(member.key);
                if (available < member.count)
                {
                    text += "  " + "SDS.Window.AvailableSuffix".Translate(available);
                }
            }
            return text;
        }

        /// <summary>只读通道说明：描述该通道保留与不保留的信息，不参与取货判定。</summary>
        private static string ChannelTipOf(bool isPreserved)
        {
            return isPreserved
                ? "SDS.Channel.PreservedTip".Translate().ToString()
                : "SDS.Channel.VirtualTip".Translate().ToString();
        }

        private static Vector2 MeasureSmall(string text)
        {
            GameFont prev = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 size = Text.CalcSize(text.NullOrEmpty() ? string.Empty : text);
            Text.Font = prev;
            return size;
        }

        private static float ButtonWidthFor(string text)
        {
            return Mathf.Max(34f, MeasureSmall(text).x + 16f);
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;
            try
            {
                DrawDialogContents(inRect);
            }
            finally
            {
                Text.WordWrap = prevWrap;
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
            }
        }

        private void DrawDialogContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;

            long max = key != null ? SDS_Availability.GetAvailableCount(key) : 0L;
            float centerX = inRect.x + inRect.width * 0.5f;
            float y = inRect.y;

            string titleText;
            if (dialogMode == DialogMode.Use)
            {
                titleText = "SDS.Use.Title".Translate(LabelOf(key)).ToString();
            }
            else if (dialogMode == DialogMode.Install)
            {
                titleText = "SDS.Install.Title".Translate(LabelOf(key)).ToString();
            }
            else
            {
                titleText = !SDS_Rules.Available
                    ? "SDS.Window.NoGrid".Translate().ToString()
                    : "SDS.Window.OutputSelected".Translate(LabelOf(key), max).ToString();
            }
            Widgets.Label(new Rect(inRect.x, y, inRect.width, TitleH), titleText);
            y += TitleH;

            if (memberRows != null)
            {
                y += RowGap;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, RecordsLabelH), recordsLabelText);
                y += RecordsLabelH + RowGap;
                for (int r = 0; r < memberRows.Count; r++)
                {
                    List<MemberButton> rowButtons = memberRows[r];
                    float rowWidth = 0f;
                    for (int i = 0; i < rowButtons.Count; i++)
                    {
                        rowWidth += rowButtons[i].width + (i > 0 ? ElementGap : 0f);
                    }
                    float buttonX = centerX - rowWidth * 0.5f;
                    for (int i = 0; i < rowButtons.Count; i++)
                    {
                        MemberButton button = rowButtons[i];
                        SDS_StockEntry member = button.entry;
                        bool current = member.key.preserved == preserved
                            && member.key.SameAs(key);
                        Rect buttonRect = new Rect(buttonX, y, button.width, RowH);
                        if (Widgets.ButtonText(buttonRect, MemberLabelText(member, current)))
                        {
                            key = member.key;
                            preserved = member.key.preserved;
                            amountInitialized = false;
                            amountBuffer = string.Empty;
                        }
                        TooltipHandler.TipRegion(buttonRect, ChannelTipOf(member.key.preserved));
                        buttonX += button.width + ElementGap;
                    }
                    y += RowH;
                    if (r < memberRows.Count - 1)
                    {
                        y += RowGap;
                    }
                }
                if (key != null)
                {
                    max = SDS_Availability.GetAvailableCount(key);
                }
            }

            if (dialogMode != DialogMode.Output)
            {
                y += RowGap;
                Rect noAmountConfirm = new Rect(centerX - confirmWidth * 0.5f, y,
                    confirmWidth, ConfirmH);
                if (Widgets.ButtonText(noAmountConfirm, confirmText))
                {
                    ConfirmOutput();
                }
                TooltipHandler.TipRegion(noAmountConfirm, (dialogMode == DialogMode.Use
                    ? "SDS.Use.ConfirmTip".Translate().ToString()
                    : "SDS.Install.ConfirmTip".Translate().ToString())
                    + "\n" + ChannelTipOf(preserved));
                return;
            }

            // 数量框默认值：min(1000, 实际可输出量)；填的数超过最大量就按最大量算。
            if (!amountInitialized)
            {
                amountInitialized = true;
                amountBuffer = Math.Min((long)DefaultAmount, max).ToString();
            }
            if (amountBuffer.NullOrEmpty())
            {
                amountBuffer = Math.Min((long)DefaultAmount, max).ToString();
            }

            // 第一栏：[数量] ◀ [数字框] ▶ [全部]
            y += RowGap;
            float row1W = amountLabelWidth + ElementGap + ArrowW + ElementGap + FieldW
                + ElementGap + ArrowW + RowGap + allButtonWidth;
            float x = centerX - row1W * 0.5f;

            Widgets.Label(new Rect(x, y, amountLabelWidth, RowH), amountLabelText);
            x += amountLabelWidth + ElementGap;
            if (Widgets.ButtonText(new Rect(x, y, ArrowW, RowH), "◀"))
            {
                amountBuffer = ClampAmount(ParseAmount(amountBuffer, max) - 1L, max).ToString();
            }
            x += ArrowW + ElementGap;
            Rect fieldRect = new Rect(x, y, FieldW, RowH);
            x += FieldW + ElementGap;
            if (centeredFieldStyle == null)
            {
                centeredFieldStyle = new GUIStyle(Text.CurTextFieldStyle)
                {
                    alignment = TextAnchor.MiddleCenter
                };
            }
            amountBuffer = GUI.TextField(fieldRect, amountBuffer.NullOrEmpty()
                ? string.Empty
                : amountBuffer, centeredFieldStyle);
            if (Widgets.ButtonText(new Rect(x, y, ArrowW, RowH), "▶"))
            {
                amountBuffer = ClampAmount(ParseAmount(amountBuffer, max) + 1L, max).ToString();
            }
            x += ArrowW + RowGap;
            if (Widgets.ButtonText(new Rect(x, y, allButtonWidth, RowH),
                "SDS.Window.OutputAll".Translate().ToString()))
            {
                amountBuffer = max.ToString();
            }
            y += RowH + RowGap;

            // 第二栏：四个步进按钮（整行居中）。
            float row2W = 0f;
            for (int i = 0; i < stepWidths.Length; i++)
            {
                if (i > 0)
                {
                    row2W += ElementGap;
                }
                row2W += stepWidths[i];
            }
            float stepX = centerX - row2W * 0.5f;
            for (int i = 0; i < stepWidths.Length; i++)
            {
                if (i > 0)
                {
                    stepX += ElementGap;
                }
                if (Widgets.ButtonText(new Rect(stepX, y, stepWidths[i], RowH), StepTexts[i]))
                {
                    amountBuffer = ClampAmount(ParseAmount(amountBuffer, max) + StepDeltas[i],
                        max).ToString();
                }
                stepX += stepWidths[i];
            }
            y += RowH;

            // 确定（居中）：关掉面板后进入地图瞄准。
            y += RowGap;
            Rect confirmRect = new Rect(centerX - confirmWidth * 0.5f, y, confirmWidth, ConfirmH);
            if (Widgets.ButtonText(confirmRect, confirmText))
            {
                ConfirmOutput();
            }
            TooltipHandler.TipRegion(confirmRect,
                "SDS.Window.OutputConfirmTip".Translate().ToString()
                + "\n" + ChannelTipOf(preserved));
        }

        public override void OnAcceptKeyPressed()
        {
            ConfirmOutput();
            if (Event.current != null)
            {
                Event.current.Use();
            }
        }

        private void ConfirmOutput()
        {
            if (key == null)
            {
                return;
            }
            // 空间不可用与“数量不足”必须分开提示：前者是全局开关/供电问题，重开窗口也无用。
            if (!SDS_Rules.Available)
            {
                Messages.Message("SDS.Output.NoGrid".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (dialogMode == DialogMode.Use)
            {
                SDS_UseTargeting.Begin(key, preserved);
                return;
            }
            if (dialogMode == DialogMode.Install)
            {
                SDS_OutputTargeting.BeginPreserved(key, 1);
                return;
            }
            long max = SDS_Availability.GetAvailableCount(key);
            long entered = ParseAmount(amountBuffer, max);
            long take = Math.Min(entered, max);
            int amount = (int)Math.Min(take, int.MaxValue);
            if (amount <= 0)
            {
                Messages.Message("SDS.Output.Insufficient".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (preserved)
            {
                SDS_OutputTargeting.BeginPreserved(key, amount);
            }
            else
            {
                SDS_OutputTargeting.Begin(key, amount);
            }
        }

        internal void ConfirmFromMiddleClick()
        {
            ConfirmOutput();
        }

        private static long ParseAmount(string buffer, long max)
        {
            if (long.TryParse(buffer, out long parsed) && parsed > 0L)
            {
                return parsed;
            }
            return max;
        }

        private static long ClampAmount(long value, long max)
        {
            if (max <= 0L)
            {
                return 0L;
            }
            if (value < 1L)
            {
                return 1L;
            }
            return value > max ? max : value;
        }
    }
}
