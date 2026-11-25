using RimWorld;
using UnityEngine;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 建筑附加光效层的属性：在建筑基图之上再画一层透明贴图（Core_light / Stabilizer_light / Anchor_light）。
    ///
    /// 使用前提（缺一不可）：
    ///  1. ThingDef 必须显式写 &lt;drawerType&gt;MapMeshAndRealTime&lt;/drawerType&gt;
    ///     （默认 MapMeshOnly 走 Thing.Print → PostPrintOnto，PostDraw 不会被调用）；
    ///  2. graphicDataExtra 的 graphicClass 必须填，否则 GraphicData.Init 返回 null；
    ///  3. drawSize 必须与基图一致，否则叠加层与建筑错位。
    /// </summary>
    internal class CompProperties_SDS_DrawExtraOverlay : CompProperties
    {
        public GraphicData graphicDataExtra;

        public CompProperties_SDS_DrawExtraOverlay()
        {
            compClass = typeof(Comp_SDS_DrawExtraOverlay);
        }
    }

    /// <summary>
    /// 附加光效层：建筑通电时在基图之上再 Draw 一次透明贴图；无状态字段（不入存档），纯视觉。
    /// 耗电配置为 0 时跳过电力判定（不接电网的核心也必须常亮，口径与 SDS_BuildingUtility 一致）。
    /// </summary>
    internal class Comp_SDS_DrawExtraOverlay : ThingComp
    {
        private CompPowerTrader cachedPowerComp;
        private bool powerCompResolved;

        public CompProperties_SDS_DrawExtraOverlay Props
        {
            get { return (CompProperties_SDS_DrawExtraOverlay)props; }
        }

        private bool ShouldDraw
        {
            get
            {
                if (!powerCompResolved)
                {
                    cachedPowerComp = parent.GetComp<CompPowerTrader>();
                    powerCompResolved = true;
                }
                if (cachedPowerComp == null)
                {
                    return true;
                }
                if (cachedPowerComp.Props.PowerConsumption <= 0f)
                {
                    return true;
                }
                return cachedPowerComp.PowerOn;
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();
            GraphicData data = Props.graphicDataExtra;
            if (data == null || data.Graphic == null || !ShouldDraw)
            {
                return;
            }
            // +1f 抬高一个 altitude 单位，保证叠加层画在基图之上。
            data.Graphic.Draw(parent.DrawPos + new Vector3(0f, 1f, 0f), parent.Rotation, parent);
        }
    }
}
