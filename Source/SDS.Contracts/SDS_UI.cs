using System.Collections.Generic;
using Verse;
using UnityEngine;

namespace SuperDimensionalStorage
{
    /// <summary>侧边栏折叠 / 滚动 / 选中行；搜索词不落盘。</summary>
    public class SDS_SidebarUIState : IExposable
    {
        public List<string> collapsed = new List<string>();
        public Vector2 scroll = Vector2.zero;
        public bool hasSelection;
        public SDS_StockKey selectedKey;
        public bool selectedPreserved;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref collapsed, "collapsed", LookMode.Value);
            Scribe_Values.Look(ref scroll, "scroll", Vector2.zero);
            Scribe_Values.Look(ref hasSelection, "hasSelection", false);
            Scribe_Deep.Look(ref selectedKey, "selectedKey");
            Scribe_Values.Look(ref selectedPreserved, "selectedPreserved", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize();
            }
        }

        /// <summary>读档兜底：空节点丢弃、负滚动归零、未解析的选中键按名字重解析。</summary>
        public void Normalize()
        {
            if (collapsed == null)
            {
                collapsed = new List<string>();
            }
            collapsed.RemoveAll(string.IsNullOrEmpty);
            if (scroll.x < 0f)
            {
                scroll.x = 0f;
            }
            if (scroll.y < 0f)
            {
                scroll.y = 0f;
            }
            if (selectedKey != null)
            {
                selectedKey.TryResolve();
            }
            if (selectedKey == null || !selectedKey.HasIdentity)
            {
                selectedKey = null;
                hasSelection = false;
                selectedPreserved = false;
            }
        }
    }
}
