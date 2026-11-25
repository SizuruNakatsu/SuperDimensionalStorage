using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SuperDimensionalStorage
{

    /// <summary>
    /// 超维空间侧边栏：查看库存、搜索、折叠类目、选中一行后输出 / 使用。
    /// 只做展示与入口；取货 / 扣账 / 落地一律走 SDS_Core 与 SDS_OutputCommand / SDS_UseCommand。
    /// </summary>
    internal class Window_SuperSpace : Window
    {
        private static readonly Color SectionColor = new Color(0.17f, 0.17f, 0.17f);
        private static readonly Color TextColor = new Color(0.55f, 0.75f, 1f);
        private static readonly Color SubTextColor = new Color(0.42f, 0.58f, 0.78f);
        private static readonly Color ForbiddenTextColor = new Color(0.42f, 0.42f, 0.42f);

        private const float HeaderTitleH = 30f;
        private const float StatusH = 20f;
        private const float SearchY = 52f;
        private const float SearchH = 24f;
        private const float ListY = 80f;
        private const float FooterH = 76f;
        private const float RowHeight = 28f;
        private const float GroupHeaderHeight = 24f;
        private const float RowIndent = 8f;

        private const float SidebarTopMargin = 150f;
        private const float SidebarBottomMargin = 40f;

        private readonly QuickSearchWidget quickSearch = new QuickSearchWidget();
        private Vector2 stockScroll = Vector2.zero;

        private readonly HashSet<string> collapsedNodes = new HashSet<string>();

        private SDS_StockKey selectedKey;
        private bool selectedPreserved;
        private List<SDS_StockEntry> selectedMembers;
        private string message;

        private readonly Dictionary<SDS_StockEntry, List<SDS_StockEntry>> aggMembers =
            new Dictionary<SDS_StockEntry, List<SDS_StockEntry>>();

        /// <summary>聚合展示行 → 展示键（仅 UI 投影，账本记录与真实键不受影响）。</summary>
        private readonly Dictionary<SDS_StockEntry, SDS_StockKey> displayKeys =
            new Dictionary<SDS_StockEntry, SDS_StockKey>();

        private List<SDS_StockEntry> cachedEntries;
        private List<SDS_StockRow> cachedRows;
        private int cachedStockVersion = int.MinValue;
        private string cachedQuery;
        private bool cachedSeparateQuality;
        private bool cachedSeparateStuff;
        private bool rowsDirty = true;

        private static float SidebarWidth
        {
            get { return Mathf.Clamp(Mathf.RoundToInt(UI.screenWidth * 0.28f), 320, 420); }
        }

        private static float SidebarHeight
        {
            get { return Mathf.Max(320f, UI.screenHeight - SidebarTopMargin - SidebarBottomMargin); }
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(SidebarWidth, SidebarHeight); }
        }

        protected override float Margin
        {
            get { return 8f; }
        }

        public Window_SuperSpace()
        {
            doCloseX = true;
            draggable = false;
            resizeable = false;
            absorbInputAroundWindow = false;
            closeOnCancel = true;
            closeOnAccept = false;
            closeOnClickedOutside = false;
            preventCameraMotion = false;
            layer = WindowLayer.GameUI;
            LoadUIState();
        }

        // ---------------- 存档级 UI 状态（折叠 / 滚动 / 选中行） ----------------

        private static SDS_SidebarUIState UIState
        {
            get
            {
                SDS_GameComponent_Grid grid = SDS_GameComponent_Grid.Instance;
                return grid != null && grid.State != null ? grid.State.sidebarUI : null;
            }
        }

        private void LoadUIState()
        {
            SDS_SidebarUIState state = UIState;
            if (state == null)
            {
                return;
            }
            state.Normalize();
            collapsedNodes.Clear();
            for (int i = 0; i < state.collapsed.Count; i++)
            {
                string key = state.collapsed[i];
                if (!string.IsNullOrEmpty(key))
                {
                    collapsedNodes.Add(key);
                }
            }
            stockScroll = state.scroll;
            selectedKey = state.hasSelection ? state.selectedKey : null;
            selectedPreserved = selectedKey != null && state.selectedPreserved;
        }

        private static void PersistCollapsed(HashSet<string> nodes)
        {
            SDS_SidebarUIState state = UIState;
            if (state == null)
            {
                return;
            }
            state.collapsed.Clear();
            foreach (string key in nodes)
            {
                state.collapsed.Add(key);
            }
        }

        private void PersistScroll()
        {
            SDS_SidebarUIState state = UIState;
            if (state != null)
            {
                state.scroll = stockScroll;
            }
        }

        private void PersistSelection()
        {
            SDS_SidebarUIState state = UIState;
            if (state == null)
            {
                return;
            }
            state.hasSelection = selectedKey != null;
            state.selectedKey = selectedKey;
            state.selectedPreserved = selectedKey != null && selectedPreserved;
        }

        protected override void SetInitialSizeAndPosition()
        {
            float width = SidebarWidth;
            float height = SidebarHeight;
            float top = Mathf.Max(0f, Mathf.Min(SidebarTopMargin, UI.screenHeight - height));
            windowRect = new Rect(UI.screenWidth - width, top, width, height).Rounded();
        }

        public override QuickSearchWidget CommonSearchWidget
        {
            get { return quickSearch; }
        }

        protected override Rect QuickSearchWidgetRect(Rect winRect, Rect inRect)
        {
            return new Rect(inRect.x, inRect.y + SearchY, inRect.width, SearchH);
        }

        public override void Notify_CommonSearchChanged()
        {
            rowsDirty = true;
            stockScroll = Vector2.zero;
            PersistScroll();
        }

        /// <summary>建造模式下 ESC 让原版处理（取消设计器 / 退建筑菜单）；非建造模式才关侧边栏。</summary>
        public override void OnCancelKeyPressed()
        {
            if (SDS_Hotkeys.InBuildMode)
            {
                return;
            }
            base.OnCancelKeyPressed();
        }

        internal bool HasSelectedRow
        {
            get { return selectedKey != null; }
        }

        internal bool TryOpenOutputFromHotkey()
        {
            if (selectedKey == null)
            {
                return false;
            }
            OpenOutputDialog();
            return true;
        }

        internal bool TryOpenUseFromHotkey()
        {
            if (selectedKey == null)
            {
                return false;
            }
            OpenUseDialog();
            return true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect titleRect = new Rect(inRect.x, inRect.y, Mathf.Max(0f, inRect.width - 30f),
                HeaderTitleH);
            Rect statusRect = new Rect(inRect.x, inRect.y + HeaderTitleH, inRect.width, StatusH);

            float listTop = inRect.y + ListY;
            float footerTop = inRect.yMax - FooterH;
            Rect listRect = new Rect(inRect.x, listTop, inRect.width,
                Mathf.Max(40f, footerTop - listTop - 4f));
            float actualFooterTop = Mathf.Max(listRect.yMax + 4f, footerTop);
            Rect footerRect = new Rect(inRect.x, actualFooterTop, inRect.width,
                Mathf.Max(0f, inRect.yMax - actualFooterTop));

            DrawHeader(titleRect, statusRect);
            DrawStockList(listRect);
            DrawFooter(footerRect);
        }

        private void DrawHeader(Rect titleRect, Rect statusRect)
        {
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                ColoredLabel(titleRect, "SDS.Window.BigTitle".Translate().ToString());

                Text.Font = GameFont.Small;
                string info = !SDS_Rules.Available
                    ? "SDS.Window.GridStatusOff".Translate().ToString()
                    : "SDS.Window.GridStatusOn".Translate(SDS_Core.UsedCapacity,
                        SDS_Rules.TypeCapacity).ToString();
                ColoredLabel(statusRect, info, SubTextColor);
            }
            finally
            {
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
            }
        }

        private void DrawFooter(Rect rect)
        {
            if (rect.height <= 0f)
            {
                return;
            }
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;

                string hint = !message.NullOrEmpty() ? message : SDS_Hotkeys.HotkeyHintText();
                ColoredLabel(new Rect(rect.x, rect.y, rect.width, 18f), hint, SubTextColor);

                float fraction = 0f;
                string capacity = "SDS.Window.GridStatusOff".Translate().ToString();
                if (SDS_Rules.TypeCapacity > 0)
                {
                    fraction = Mathf.Clamp01((float)SDS_Core.UsedCapacity / SDS_Rules.TypeCapacity);
                    capacity = "SDS.Window.CapacityBar".Translate(SDS_Core.UsedCapacity,
                        SDS_Rules.TypeCapacity).ToString();
                }
                Rect barRect = new Rect(rect.x, rect.y + 20f, rect.width, 18f);
                Widgets.FillableBar(barRect, fraction);
                Text.Anchor = TextAnchor.MiddleCenter;
                ColoredLabel(barRect, capacity, Color.white);

                float useWidth = Mathf.Clamp(rect.width * 0.28f, 64f, 110f);
                Rect outputRect = new Rect(rect.x, rect.y + 42f,
                    Mathf.Max(40f, rect.width - useWidth - 6f), 30f);
                Rect useRect = new Rect(outputRect.xMax + 6f, rect.y + 42f, useWidth, 30f);
                string label = selectedKey != null
                    ? "SDS.Window.OutputButtonSelected".Translate(
                        SDS_OutputCommand.KeyLabel(selectedKey)).ToString()
                    : "SDS.Window.OutputButton".Translate().ToString();
                if (Widgets.ButtonText(outputRect, label.Truncate(outputRect.width - 8f)))
                {
                    OpenOutputDialog();
                }

                bool canUse = selectedKey != null
                    && RowHasAvailable(selectedKey, selectedMembers);
                Color prevColor = GUI.color;
                if (!canUse)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.45f);
                }
                string useLabel = "SDS.Window.UseButton".Translate().ToString();
                if (Widgets.ButtonText(useRect, useLabel.Truncate(useRect.width - 8f)) && canUse)
                {
                    OpenUseDialog();
                }
                GUI.color = prevColor;
                if (!canUse)
                {
                    string tip;
                    if (selectedKey == null)
                    {
                        tip = "SDS.Window.OutputNoKey".Translate().ToString();
                    }
                    else if (IsRowForbidden(selectedKey, selectedMembers))
                    {
                        tip = "SDS.Forbidden.CannotUse".Translate().ToString();
                    }
                    else
                    {
                        tip = "SDS.Use.NoStock".Translate().ToString();
                    }
                    TooltipHandler.TipRegion(useRect, tip);
                }
            }
            finally
            {
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
            }
        }

        private void OpenOutputDialog()
        {
            message = null;
            if (selectedKey == null)
            {
                message = "SDS.Window.OutputNoKey".Translate().ToString();
                return;
            }
            if (SDS_Core.Ledger == null)
            {
                message = "SDS.Output.NoGrid".Translate().ToString();
                return;
            }
            // W31：可安装缩装固定 1 件；同品质 / 材质（无歧义）直接进入原版安装放置器。
            // 聚合行必须先按「可用量」选出真实成员键，不能拿展示代表键做通道 / 安装判定。
            SDS_StockEntry picked = PickDefaultMember(selectedMembers);
            SDS_StockKey effectiveKey = picked != null && picked.key != null ? picked.key : selectedKey;
            if (SDS_OutputCommand.IsInstallablePreservedKey(effectiveKey))
            {
                bool ambiguous = selectedMembers != null && selectedMembers.Count > 1;
                if (!ambiguous)
                {
                    SDS_OutputTargeting.BeginInstallPlacement(effectiveKey);
                    return;
                }
                if (Find.WindowStack.WindowOfType<Window_SDS_OutputDialog>() == null)
                {
                    Find.WindowStack.Add(new Window_SDS_OutputDialog(selectedKey, selectedPreserved,
                        selectedMembers, Window_SDS_OutputDialog.DialogMode.Install));
                }
                return;
            }
            if (Find.WindowStack.WindowOfType<Window_SDS_OutputDialog>() == null)
            {
                Find.WindowStack.Add(new Window_SDS_OutputDialog(selectedKey, selectedPreserved,
                    selectedMembers));
            }
        }

        private void OpenUseDialog()
        {
            message = null;
            if (selectedKey == null)
            {
                message = "SDS.Window.OutputNoKey".Translate().ToString();
                return;
            }
            if (SDS_Core.Ledger == null)
            {
                message = "SDS.Output.NoGrid".Translate().ToString();
                return;
            }
            if (selectedMembers != null && selectedMembers.Count > 1)
            {
                if (Find.WindowStack.WindowOfType<Window_SDS_OutputDialog>() == null)
                {
                    Find.WindowStack.Add(new Window_SDS_OutputDialog(selectedKey, selectedPreserved,
                        selectedMembers, Window_SDS_OutputDialog.DialogMode.Use));
                }
                return;
            }
            SDS_UseTargeting.Begin(selectedKey, selectedPreserved);
        }

        // ---------------- 数据存储列表 ----------------

        private void DrawStockList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, SectionColor);
            Rect inner = rect.ContractedBy(6f);

            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null)
            {
                DrawCentered(inner, "SDS.Window.NoGrid".Translate().ToString());
                return;
            }

            // 未解析条目不显示为可用、不可选中、不可取出，但永不删除（读档后自动重解析）。
            List<SDS_StockEntry> unresolved = ledger.UnresolvedSnapshot();
            if (unresolved.Count > 0)
            {
                float sectionHeight = 24f + Mathf.Min(unresolved.Count, 3) * 18f + 4f;
                sectionHeight = Mathf.Min(sectionHeight, inner.height * 0.45f);
                Rect sectionRect = new Rect(inner.x, inner.y, inner.width, sectionHeight);
                DrawUnresolvedSection(sectionRect, unresolved);
                float delta = sectionRect.height + 4f;
                inner.y += delta;
                inner.height -= delta;
                if (inner.height < 40f)
                {
                    return;
                }
            }

            string query = quickSearch.filter == null ? null : quickSearch.filter.Text;
            query = query == null ? null : query.Trim();
            bool needRebuild = rowsDirty || cachedRows == null
                || ledger.StockVersion != cachedStockVersion
                || !string.Equals(query, cachedQuery, StringComparison.Ordinal)
                || SDS_Config.SeparateByQuality != cachedSeparateQuality
                || SDS_Config.SeparateByStuff != cachedSeparateStuff;
            if (needRebuild)
            {
                RebuildRows(ledger, query);
            }

            if (cachedEntries == null || cachedEntries.Count == 0)
            {
                DrawCentered(inner, "SDS.Window.StockEmpty".Translate().ToString());
                return;
            }
            List<SDS_StockRow> rows = cachedRows;
            if (rows == null || rows.Count == 0)
            {
                DrawCentered(inner, "SDS.Window.SearchNoMatch".Translate().ToString());
                return;
            }

            float totalHeight = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                totalHeight += rows[i].height;
            }

            Rect viewRect = new Rect(0f, 0f, inner.width - 20f, Mathf.Max(totalHeight, inner.height));
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            bool scrollOpen = false;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.BeginScrollView(inner, ref stockScroll, viewRect);
                scrollOpen = true;

                float viewTop = Mathf.Max(0f, stockScroll.y);
                float viewBottom = viewTop + inner.height;
                float y = 0f;
                for (int i = 0; i < rows.Count; i++)
                {
                    SDS_StockRow row = rows[i];
                    float height = row.height;
                    if (y + height < viewTop)
                    {
                        y += height;
                        continue;
                    }
                    if (y > viewBottom)
                    {
                        break;
                    }
                    Rect rowRect = new Rect(0f, y, viewRect.width, height);
                    if (row.node != null)
                    {
                        DrawCategoryRow(rowRect, row);
                    }
                    else
                    {
                        DrawStockRow(rowRect, row.entry, row.depth, row.pathLabel);
                    }
                    y += height;
                }
            }
            finally
            {
                if (scrollOpen)
                {
                    Widgets.EndScrollView();
                }
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
            }
            PersistScroll();
        }

        private void RebuildRows(ISDS_LedgerRead ledger, string query)
        {
            List<SDS_StockEntry> entries = new List<SDS_StockEntry>();
            List<SDS_StockEntry> virtualEntries = ledger.Snapshot(0);
            for (int i = 0; i < virtualEntries.Count; i++)
            {
                SDS_StockEntry entry = virtualEntries[i];
                if (entry != null && entry.key != null && entry.key.Resolved)
                {
                    entries.Add(entry);
                }
            }
            entries.AddRange(ledger.PreservedSnapshot());
            entries = BuildDisplayEntries(entries);
            cachedEntries = entries;
            cachedRows = entries.Count == 0
                ? new List<SDS_StockRow>()
                : (query.NullOrEmpty() ? BuildTreeRows(entries) : BuildSearchRows(entries, query));
            cachedStockVersion = ledger.StockVersion;
            cachedQuery = query;
            cachedSeparateQuality = SDS_Config.SeparateByQuality;
            cachedSeparateStuff = SDS_Config.SeparateByStuff;
            rowsDirty = false;
            RebuildSelectedMembers();
        }

        private void RebuildSelectedMembers()
        {
            selectedMembers = null;
            if (selectedKey == null || cachedEntries == null)
            {
                return;
            }
            if (TryBindSelection(selectedKey))
            {
                return;
            }
            selectedKey = null;
            selectedPreserved = false;
            selectedMembers = null;
            PersistSelection();
        }

        /// <summary>按真实键绑定选中行的成员表；命中成员键时把选中键归一到行代表键。</summary>
        private bool TryBindSelection(SDS_StockKey key)
        {
            if (key == null)
            {
                return false;
            }
            for (int i = 0; i < cachedEntries.Count; i++)
            {
                SDS_StockEntry entry = cachedEntries[i];
                if (entry == null || entry.key == null || entry.key.preserved != key.preserved)
                {
                    continue;
                }
                aggMembers.TryGetValue(entry, out List<SDS_StockEntry> members);
                bool hit = entry.key.SameAs(key);
                if (!hit && members != null)
                {
                    for (int m = 0; m < members.Count; m++)
                    {
                        SDS_StockEntry member = members[m];
                        if (member != null && member.key != null && member.key.SameAs(key))
                        {
                            hit = true;
                            break;
                        }
                    }
                }
                if (!hit)
                {
                    continue;
                }
                selectedMembers = members;
                if (!entry.key.SameAs(key))
                {
                    selectedKey = entry.key;
                    PersistSelection();
                }
                return true;
            }
            return false;
        }

        /// <summary>未解析区（def 暂缺）：只展示、不可选中、不可取出；修复 def 后读档自动恢复。</summary>
        private static void DrawUnresolvedSection(Rect rect, List<SDS_StockEntry> entries)
        {
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            try
            {
                Widgets.DrawBoxSolid(rect, new Color(0.30f, 0.10f, 0.10f, 0.45f));
                Rect line = rect.ContractedBy(4f);
                line.height = 18f;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(line, "SDS.Window.UnresolvedHeader".Translate(entries.Count).ToString());
                line.y += 18f;
                int max = Mathf.Min(entries.Count, Mathf.FloorToInt((rect.height - 26f) / 18f));
                for (int i = 0; i < max; i++)
                {
                    SDS_StockEntry entry = entries[i];
                    string name = entry != null && entry.key != null
                        ? entry.key.EffectiveDefName ?? "?"
                        : "?";
                    Widgets.Label(line, name + " ×" + (entry != null ? entry.count : 0));
                    line.y += 18f;
                }
            }
            finally
            {
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
            }
        }

        private static void DrawCentered(Rect rect, string text)
        {
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                ColoredLabel(rect, text, SubTextColor);
            }
            finally
            {
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
            }
        }

        private void DrawCategoryRow(Rect rect, SDS_StockRow row)
        {
            SDS_CatNode node = row.node;
            bool collapsed = collapsedNodes.Contains(node.key);
            float indent = row.depth * RowIndent;
            Rect barRect = new Rect(rect.x + indent, rect.y, rect.width - indent, rect.height);
            Widgets.DrawBoxSolid(barRect, new Color(0.13f, 0.13f, 0.13f));
            Color prev = GUI.color;
            GUI.color = SubTextColor;
            Widgets.Label(new Rect(barRect.x + 6f, barRect.y, barRect.width - 12f, barRect.height),
                (collapsed ? "+ " : "- ") + node.label + "   x" + node.total);
            GUI.color = prev;
            if (Widgets.ButtonInvisible(barRect))
            {
                if (collapsed)
                {
                    collapsedNodes.Remove(node.key);
                }
                else
                {
                    collapsedNodes.Add(node.key);
                }
                rowsDirty = true;
                PersistCollapsed(collapsedNodes);
            }
        }

        private void DrawStockRow(Rect row, SDS_StockEntry entry, int depth, string pathLabel)
        {
            const float ButtonSize = 24f;
            const float MassWidth = 62f;
            const float IconSize = 24f;

            if (entry == null || entry.key == null)
            {
                return;
            }
            bool selected = selectedPreserved == entry.key.preserved
                && selectedKey != null && selectedKey.SameAs(entry.key);

            aggMembers.TryGetValue(entry, out List<SDS_StockEntry> rowMembers);
            bool forbidden = IsRowForbidden(entry.key, rowMembers);
            SDS_StockKey displayKey = DisplayKeyOf(entry);

            float right = row.xMax - 2f;
            float buttonY = row.y + (row.height - ButtonSize) / 2f;
            Rect infoRect = new Rect(right - ButtonSize, buttonY, ButtonSize, ButtonSize);
            right = infoRect.x - 2f;
            Rect toggleRect = new Rect(right - ButtonSize, buttonY, ButtonSize, ButtonSize);
            right = toggleRect.x - 2f;
            Rect massRect = new Rect(right - MassWidth, row.y, MassWidth, row.height);
            right = massRect.x - 2f;

            float indent = depth * RowIndent + RowIndent;
            Rect iconRect = new Rect(row.x + indent, row.y + (row.height - IconSize) / 2f,
                IconSize, IconSize);
            if (displayKey.thingDef != null)
            {
                Widgets.ThingIcon(iconRect, displayKey.thingDef, displayKey.stuff);
            }

            long available = RowAvailable(entry, rowMembers);
            string suffix = entry.key.preserved
                ? "   x" + entry.count + "  " + "SDS.Window.PreservedTag".Translate().ToString()
                : "   x" + entry.count;
            // 行数量仍是权威总量；可用量低于总量时补一行提示，避免「列表有、点输出说没有」的误判。
            if (available < entry.count)
            {
                suffix += "  " + "SDS.Window.AvailableSuffix".Translate(available);
            }
            string text = (selected ? "> " : "")
                + (pathLabel.NullOrEmpty() ? string.Empty : pathLabel)
                + SDS_OutputCommand.KeyLabel(displayKey) + suffix;
            string channelTip = entry.key.preserved
                ? "SDS.Channel.PreservedTip".Translate().ToString()
                : "SDS.Channel.VirtualTip".Translate().ToString();
            string tooltip = available <= 0L
                ? text + "\n" + channelTip + "\n" + UnavailableReason(entry.key, rowMembers)
                : text + "\n" + channelTip;

            Rect textRect = new Rect(iconRect.xMax + 4f, row.y,
                Mathf.Max(0f, right - iconRect.xMax - 4f), row.height);
            Color prev = GUI.color;
            GUI.color = selected ? Color.white : (forbidden ? ForbiddenTextColor : TextColor);
            bool prevWrap = Text.WordWrap;
            try
            {
                Text.WordWrap = false;
                Widgets.Label(textRect, text.Truncate(textRect.width));
            }
            finally
            {
                Text.WordWrap = prevWrap;
                GUI.color = prev;
            }

            if (displayKey.thingDef != null)
            {
                float mass = RowMass(entry, rowMembers);
                TextAnchor prevMassAnchor = Text.Anchor;
                bool prevMassWrap = Text.WordWrap;
                Color prevMassColor = GUI.color;
                try
                {
                    Text.Anchor = TextAnchor.MiddleRight;
                    Text.WordWrap = false;
                    GUI.color = selected ? Color.white : TransferableOneWayWidget.ItemMassColor;
                    Widgets.Label(massRect, mass.ToStringMass());
                }
                finally
                {
                    GUI.color = prevMassColor;
                    Text.WordWrap = prevMassWrap;
                    Text.Anchor = prevMassAnchor;
                }
            }

            if (!Mouse.IsOver(toggleRect) && !Mouse.IsOver(infoRect))
            {
                TooltipHandler.TipRegion(row, tooltip);
            }

            // 原版「可用 / 不可用」同款视觉：绿色 √ / 红色 ×；点击带原版音效。
            bool allowed = !forbidden;
            Widgets.Checkbox(toggleRect.x, toggleRect.y, ref allowed, ButtonSize);
            bool nowForbidden = !allowed;
            if (nowForbidden != forbidden)
            {
                SetRowForbidden(entry.key, rowMembers, nowForbidden);
                message = null;
            }
            TooltipHandler.TipRegion(toggleRect,
                (nowForbidden ? "CommandForbiddenDesc" : "CommandNotForbiddenDesc").Translate().ToString());

            if (entry.key.thingDef != null)
            {
                Widgets.InfoCardButton(infoRect.x, infoRect.y, entry.key.thingDef);
            }

            if (!Mouse.IsOver(toggleRect) && !Mouse.IsOver(infoRect) && Widgets.ButtonInvisible(row))
            {
                selectedKey = entry.key;
                selectedPreserved = entry.key.preserved;
                selectedMembers = rowMembers;
                message = null;
                PersistSelection();
            }
        }

        private float RowMass(SDS_StockEntry entry, List<SDS_StockEntry> members)
        {
            if (entry == null || entry.key == null || entry.key.thingDef == null)
            {
                return 0f;
            }
            if (members != null && members.Count > 0)
            {
                float sum = 0f;
                bool any = false;
                for (int i = 0; i < members.Count; i++)
                {
                    SDS_StockEntry member = members[i];
                    if (member == null || member.key == null || member.key.thingDef == null)
                    {
                        continue;
                    }
                    sum += member.key.thingDef.GetStatValueAbstract(StatDefOf.Mass, member.key.stuff)
                        * member.count;
                    any = true;
                }
                if (any)
                {
                    return sum;
                }
            }
            SDS_StockKey displayKey = DisplayKeyOf(entry);
            if (displayKey.thingDef == null)
            {
                return 0f;
            }
            return displayKey.thingDef.GetStatValueAbstract(StatDefOf.Mass, displayKey.stuff)
                * entry.count;
        }

        /// <summary>这一行是否有可用量（「使用」按钮的唯一判据，走 SDS_Availability）。</summary>
        private static bool RowHasAvailable(SDS_StockKey key, List<SDS_StockEntry> members)
        {
            if (members != null && members.Count > 0)
            {
                for (int i = 0; i < members.Count; i++)
                {
                    SDS_StockEntry member = members[i];
                    if (member != null && member.key != null
                        && SDS_Availability.GetAvailableCount(member.key) > 0L)
                    {
                        return true;
                    }
                }
                return false;
            }
            return key != null && SDS_Availability.GetAvailableCount(key) > 0L;
        }

        /// <summary>行可用量（只用于展示与默认成员选择）：聚合行按成员真实键逐个求和，通道不混算。</summary>
        private static long RowAvailable(SDS_StockEntry entry, List<SDS_StockEntry> members)
        {
            if (entry == null || entry.key == null)
            {
                return 0L;
            }
            if (members == null || members.Count == 0)
            {
                return SDS_Availability.GetAvailableCount(entry.key);
            }
            long sum = 0L;
            for (int i = 0; i < members.Count; i++)
            {
                SDS_StockEntry member = members[i];
                if (member != null && member.key != null)
                {
                    sum += SDS_Availability.GetAvailableCount(member.key);
                }
            }
            return sum;
        }

        /// <summary>可用量为 0 时的唯一原因链：空间不可用 → 整行禁用 → 已被预留。</summary>
        private static string UnavailableReason(SDS_StockKey key, List<SDS_StockEntry> members)
        {
            if (!SDS_Rules.Available)
            {
                return "SDS.Window.NoGridHint".Translate().ToString();
            }
            if (IsRowForbidden(key, members))
            {
                return "SDS.Forbidden.CannotUse".Translate().ToString();
            }
            return "SDS.Output.Insufficient".Translate().ToString();
        }

        /// <summary>
        /// 聚合行的默认取件成员：可用量最大者优先（平局按 CompareIdentity），
        /// 全部不可用时退回“数量最大者”，只用于显示真实原因，不改变按钮禁用口径。
        /// </summary>
        internal static SDS_StockEntry PickDefaultMember(List<SDS_StockEntry> members)
        {
            if (members == null || members.Count == 0)
            {
                return null;
            }
            SDS_StockEntry best = null;
            long bestAvailable = -1L;
            for (int i = 0; i < members.Count; i++)
            {
                SDS_StockEntry member = members[i];
                if (member == null || member.key == null)
                {
                    continue;
                }
                long available = SDS_Availability.GetAvailableCount(member.key);
                if (available <= 0L)
                {
                    continue;
                }
                if (best == null || available > bestAvailable
                    || (available == bestAvailable
                        && SDS_StockKey.CompareIdentity(member.key, best.key) < 0))
                {
                    best = member;
                    bestAvailable = available;
                }
            }
            if (best != null)
            {
                return best;
            }
            for (int i = 0; i < members.Count; i++)
            {
                SDS_StockEntry member = members[i];
                if (member == null || member.key == null)
                {
                    continue;
                }
                if (best == null || member.count > best.count
                    || (member.count == best.count
                        && SDS_StockKey.CompareIdentity(member.key, best.key) < 0))
                {
                    best = member;
                }
            }
            return best;
        }

        /// <summary>行禁用：非聚合行读该键；聚合行只有全部成员都禁用才算整行禁用。</summary>
        private static bool IsRowForbidden(SDS_StockKey key, List<SDS_StockEntry> members)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || key == null)
            {
                return false;
            }
            if (members == null || members.Count == 0)
            {
                return ledger.IsForbidden(key);
            }
            bool any = false;
            for (int i = 0; i < members.Count; i++)
            {
                SDS_StockEntry member = members[i];
                if (member == null || member.key == null)
                {
                    continue;
                }
                any = true;
                if (!ledger.IsForbidden(member.key))
                {
                    return false;
                }
            }
            return any;
        }

        /// <summary>整行统一切换可用性：聚合行逐条落到真实品类键上。</summary>
        private static void SetRowForbidden(SDS_StockKey key, List<SDS_StockEntry> members, bool forbidden)
        {
            if (members == null || members.Count == 0)
            {
                ApplyForbidden(key, forbidden);
                return;
            }
            for (int i = 0; i < members.Count; i++)
            {
                SDS_StockEntry member = members[i];
                if (member != null && member.key != null)
                {
                    ApplyForbidden(member.key, forbidden);
                }
            }
        }

        private static void ApplyForbidden(SDS_StockKey key, bool forbidden)
        {
            if (key == null || !SDS_Core.SetForbidden(key, forbidden))
            {
                return;
            }
            if (!key.preserved)
            {
                return;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null)
            {
                return;
            }
            List<Thing> preserved = new List<Thing>();
            ledger.AppendPreservedThings(preserved);
            for (int i = 0; i < preserved.Count; i++)
            {
                Thing thing = preserved[i];
                if (thing == null || thing.Destroyed
                    || !SDS_Rules.TryGetStockKey(thing, out SDS_StockKey memberKey)
                    || memberKey == null || !memberKey.SameAs(key))
                {
                    continue;
                }
                thing.SetForbidden(forbidden, false);
            }
        }

        /// <summary>
        /// 按 Mod 设置把条目折成「展示行」：关闭"按品质 / 材质分开"时同类型合并、数量相加。
        /// 行身份取成员里 CompareIdentity 最小的一条真实键（合成键在账本里不存在，不能当身份）。
        /// 保真条目与虚拟条目永不互相合并（件数 vs 份数语义不同）。
        /// </summary>
        private List<SDS_StockEntry> BuildDisplayEntries(List<SDS_StockEntry> entries)
        {
            aggMembers.Clear();
            displayKeys.Clear();
            if (entries == null || entries.Count == 0)
            {
                return entries;
            }
            if (SDS_Config.SeparateByQuality && SDS_Config.SeparateByStuff)
            {
                return entries;
            }
            Dictionary<string, SDS_StockEntry> groupsById =
                new Dictionary<string, SDS_StockEntry>();
            Dictionary<string, List<SDS_StockEntry>> membersById =
                new Dictionary<string, List<SDS_StockEntry>>();
            List<SDS_StockEntry> display = new List<SDS_StockEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                SDS_StockEntry entry = entries[i];
                if (entry == null || entry.key == null || entry.key.thingDef == null)
                {
                    continue;
                }
                SDS_StockKey groupKey = GroupKeyOf(entry.key);
                string id = (entry.key.preserved ? "P|" : "V|") + groupKey.thingDef.defName
                    + "|" + (groupKey.stuff != null ? groupKey.stuff.defName : "-")
                    + "|" + groupKey.qualityInt;
                if (!groupsById.TryGetValue(id, out SDS_StockEntry group))
                {
                    group = new SDS_StockEntry(entry.key, 0L);
                    groupsById[id] = group;
                    displayKeys[group] = groupKey;
                    membersById[id] = new List<SDS_StockEntry>();
                    display.Add(group);
                }
                else if (group.key != null && SDS_StockKey.CompareIdentity(entry.key, group.key) < 0)
                {
                    group.key = entry.key;
                }
                group.count += entry.count;
                membersById[id].Add(entry);
            }
            foreach (KeyValuePair<string, List<SDS_StockEntry>> pair in membersById)
            {
                aggMembers[groupsById[pair.Key]] = pair.Value;
            }
            return display;
        }

        /// <summary>展示聚合键：两个"是否分开计数"开关决定归零哪个维度（仅展示，不动账本记录）。</summary>
        private static SDS_StockKey GroupKeyOf(SDS_StockKey key)
        {
            return SDS_Rules.CategoryKeyOf(key);
        }

        /// <summary>行展示键：聚合行取封存的展示键，其余行就是真实键。</summary>
        private SDS_StockKey DisplayKeyOf(SDS_StockEntry entry)
        {
            if (entry == null)
            {
                return null;
            }
            return displayKeys.TryGetValue(entry, out SDS_StockKey key) && key != null
                ? key
                : entry.key;
        }

        // ---------------- 类目树（与原版 ThingCategoryDef 同源） ----------------

        private List<SDS_StockRow> BuildTreeRows(List<SDS_StockEntry> entries)
        {
            SDS_CatNode root = BuildCategoryTree(entries);
            List<SDS_StockRow> rows = new List<SDS_StockRow>();
            AppendNodeRows(root, 0, true, rows);
            return rows;
        }

        private void AppendNodeRows(SDS_CatNode node, int depth, bool isOutermostLevel,
            List<SDS_StockRow> rows)
        {
            bool childOutermost = isOutermostLevel;
            if (!node.key.NullOrEmpty())
            {
                if (isOutermostLevel && node.children.Count > 0)
                {
                    // 最外层分组壳（"Items"）不画，其子分类以 depth 0 出现在顶层。
                    childOutermost = false;
                }
                else
                {
                    rows.Add(new SDS_StockRow { node = node, depth = depth });
                    if (collapsedNodes.Contains(node.key))
                    {
                        return;
                    }
                    depth++;
                    childOutermost = false;
                }
            }
            for (int i = 0; i < node.children.Count; i++)
            {
                AppendNodeRows(node.children[i], depth, childOutermost, rows);
            }
            for (int i = 0; i < node.entries.Count; i++)
            {
                rows.Add(new SDS_StockRow { entry = node.entries[i], depth = depth });
            }
        }

        private static SDS_CatNode BuildCategoryTree(List<SDS_StockEntry> entries)
        {
            SDS_CatNode root = new SDS_CatNode { key = string.Empty, label = string.Empty };
            for (int i = 0; i < entries.Count; i++)
            {
                SDS_StockEntry entry = entries[i];
                if (entry == null || entry.key == null || entry.key.thingDef == null)
                {
                    continue;
                }
                List<ThingCategoryDef> path = CategoryPath(entry.key.thingDef);
                if (path.Count == 0)
                {
                    SDS_CatNode fallback = root.ChildFor("SDS.Window.Uncategorized",
                        "SDS.Window.Uncategorized".Translate().ToString());
                    fallback.entries.Add(entry);
                    fallback.total += entry.count;
                    continue;
                }
                SDS_CatNode node = root;
                string key = string.Empty;
                for (int p = 0; p < path.Count; p++)
                {
                    key = key.Length == 0 ? path[p].defName : key + "/" + path[p].defName;
                    node = node.ChildFor(key, path[p].LabelCap.ToString());
                    node.total += entry.count;
                }
                node.entries.Add(entry);
            }
            SortTree(root);
            return root;
        }

        private static List<ThingCategoryDef> CategoryPath(ThingDef def)
        {
            List<ThingCategoryDef> path = new List<ThingCategoryDef>();
            if (def == null || def.thingCategories.NullOrEmpty())
            {
                return path;
            }
            ThingCategoryDef leaf = def.thingCategories[0];
            while (leaf != null)
            {
                path.Insert(0, leaf);
                leaf = leaf.parent;
            }
            return path;
        }

        private static string CategoryPathLabel(ThingDef def)
        {
            List<ThingCategoryDef> path = CategoryPath(def);
            if (path.Count == 0)
            {
                return "SDS.Window.Uncategorized".Translate().ToString();
            }
            string text = string.Empty;
            for (int i = 0; i < path.Count; i++)
            {
                text = text.Length == 0
                    ? path[i].LabelCap.ToString()
                    : text + " > " + path[i].LabelCap.ToString();
            }
            return text;
        }

        private static void SortTree(SDS_CatNode node)
        {
            node.children.Sort((a, b) => string.CompareOrdinal(a.label, b.label));
            node.entries.Sort(delegate(SDS_StockEntry a, SDS_StockEntry b)
            {
                int c = string.CompareOrdinal(a.key.thingDef.defName, b.key.thingDef.defName);
                if (c != 0)
                {
                    return c;
                }
                string stuffA = a.key.stuff != null ? a.key.stuff.defName : string.Empty;
                string stuffB = b.key.stuff != null ? b.key.stuff.defName : string.Empty;
                c = string.CompareOrdinal(stuffA, stuffB);
                return c != 0 ? c : a.key.qualityInt.CompareTo(b.key.qualityInt);
            });
            for (int i = 0; i < node.children.Count; i++)
            {
                SortTree(node.children[i]);
            }
        }

        private static List<SDS_StockRow> BuildSearchRows(List<SDS_StockEntry> entries,
            string query)
        {
            List<SDS_StockRow> rows = new List<SDS_StockRow>();
            for (int i = 0; i < entries.Count; i++)
            {
                SDS_StockEntry entry = entries[i];
                if (entry == null || entry.key == null || entry.key.thingDef == null)
                {
                    continue;
                }
                string path = CategoryPathLabel(entry.key.thingDef);
                string label = SDS_OutputCommand.KeyLabel(entry.key);
                bool hit = label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit)
                {
                    continue;
                }
                rows.Add(new SDS_StockRow { entry = entry, pathLabel = path + "   >   " });
            }
            return rows;
        }

        private sealed class SDS_CatNode
        {
            public string key;
            public string label;
            public long total;
            public readonly List<SDS_CatNode> children = new List<SDS_CatNode>();
            public readonly List<SDS_StockEntry> entries = new List<SDS_StockEntry>();

            public SDS_CatNode ChildFor(string childKey, string childLabel)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    if (children[i].key == childKey)
                    {
                        return children[i];
                    }
                }
                SDS_CatNode node = new SDS_CatNode { key = childKey, label = childLabel };
                children.Add(node);
                return node;
            }
        }

        private sealed class SDS_StockRow
        {
            public SDS_CatNode node;
            public SDS_StockEntry entry;
            public int depth;
            public string pathLabel;

            public float height
            {
                get { return node != null ? GroupHeaderHeight : RowHeight; }
            }
        }

        private static void ColoredLabel(Rect rect, string text)
        {
            ColoredLabel(rect, text, TextColor);
        }

        private static void ColoredLabel(Rect rect, string text, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            Widgets.Label(rect, text);
            GUI.color = prev;
        }
    }
}
