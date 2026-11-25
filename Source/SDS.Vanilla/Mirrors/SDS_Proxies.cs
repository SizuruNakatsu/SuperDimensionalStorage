using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 代理 Thing 契约（§6.5）：账本的只读视图，不是第二份真相。
    /// 取货只经 SDS_TakeService → SDS_Rules.TryTakeMirror → 全局账本扣数。
    /// </summary>
    internal interface ISDS_ProxyThing
    {
        SDS_StockKey Key { get; }
        bool Preserved { get; }
        Thing ResolveReal(int count);
    }

    /// <summary>代理通用逻辑：活跃判定与统一取货。</summary>
    internal static class SDS_ProxyUtility
    {
        /// <summary>只有仍登记为镜像的代理才是活跃代理；整堆取走后它就是普通实物。</summary>
        internal static bool IsActive(Thing thing)
        {
            return thing is ISDS_ProxyThing && !thing.Destroyed && SDS_MirrorRegistry.IsMirror(thing);
        }

        internal static Thing Resolve(Thing proxy, int count)
        {
            if (proxy == null || count <= 0 || !SDS_MirrorRegistry.IsMirror(proxy))
            {
                return null;
            }
            return SDS_TakeService.TryTake(proxy, count, out Thing taken) ? taken : null;
        }
    }

    /// <summary>
    /// 虚拟镜像代理工厂：按 def.thingClass 选真实继承分支。
    /// 自定义 thingClass 不强行套代理（会破坏第三方强转），退回原版实物类。
    /// </summary>
    internal static class SDS_ProxyFactory
    {
        internal static Type ProxyTypeFor(ThingDef def)
        {
            if (def == null || def.thingClass == null)
            {
                return null;
            }
            Type cls = def.thingClass;
            if (cls == typeof(Thing))
            {
                return typeof(SDS_Proxy_Thing);
            }
            if (cls == typeof(ThingWithComps))
            {
                return typeof(SDS_Proxy_ThingWithComps);
            }
            if (cls == typeof(Apparel))
            {
                return typeof(SDS_Proxy_Apparel);
            }
            return null;
        }

        internal static Thing CreateMirrorThing(SDS_StockKey key)
        {
            if (key == null || key.thingDef == null || key.preserved)
            {
                return null;
            }
            Thing thing = null;
            Type proxyType = ProxyTypeFor(key.thingDef);
            if (proxyType != null)
            {
                try
                {
                    thing = Instantiate(proxyType, key);
                }
                catch (Exception e)
                {
                    SDS_Diag.ErrorOnce("代理镜像创建失败（已回退为原版实物类）", e);
                    thing = null;
                }
            }
            if (thing == null)
            {
                thing = ThingMaker.MakeThing(key.thingDef, key.stuff);
            }
            if (thing != null)
            {
                SDS_OutputUtility.ApplyKeyState(key, thing);
            }
            return thing;
        }

        /// <summary>复刻 ThingMaker.MakeThing 的构造顺序，只把实例类型换成代理类。</summary>
        private static Thing Instantiate(Type proxyType, SDS_StockKey key)
        {
            ThingDef def = key.thingDef;
            ThingDef stuff = key.stuff;
            if (stuff != null && !stuff.IsStuff)
            {
                stuff = null;
            }
            if (def.MadeFromStuff && stuff == null)
            {
                stuff = GenStuff.DefaultStuffFor(def);
            }
            if (!def.MadeFromStuff && stuff != null)
            {
                stuff = null;
            }
            Thing thing = (Thing)Activator.CreateInstance(proxyType);
            thing.def = def;
            thing.SetStuffDirect(stuff);
            thing.PostMake();
            thing.PostPostMake();
            return thing;
        }
    }

    /// <summary>普通 Thing 代理（无 Comp 的简单物品）。</summary>
    public class SDS_Proxy_Thing : Thing, ISDS_ProxyThing
    {
        public SDS_StockKey Key
        {
            get { return SDS_OutputUtility.KeyOfThing(this); }
        }

        public bool Preserved
        {
            get { return false; }
        }

        public Thing ResolveReal(int count)
        {
            return SDS_ProxyUtility.Resolve(this, count);
        }

        public override Thing SplitOff(int count)
        {
            if (!SDS_ProxyUtility.IsActive(this))
            {
                return base.SplitOff(count);
            }
            return ResolveReal(count);
        }

        public override bool CanStackWith(Thing other)
        {
            if (SDS_ProxyUtility.IsActive(this) || SDS_ProxyUtility.IsActive(other))
            {
                return false;
            }
            return base.CanStackWith(other);
        }

        public override bool TryAbsorbStack(Thing other, bool respectStackLimit)
        {
            if (SDS_MirrorAbsorb.TryHandle(this, other, respectStackLimit, out bool result))
            {
                return result;
            }
            return base.TryAbsorbStack(other, respectStackLimit);
        }

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                absorbed = true;
                return;
            }
            base.PreApplyDamage(ref dinfo, out absorbed);
        }

        public override void Kill(DamageInfo? dinfo = null, Hediff exactCulprit = null)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.Kill(dinfo, exactCulprit);
        }

        public override void Print(SectionLayer layer)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.Print(layer);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.DrawAt(drawLoc, flip);
        }

        public override void DrawGUIOverlay()
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.DrawGUIOverlay();
        }

        public override string GetInspectString()
        {
            return SDS_ProxyUtility.IsActive(this) ? null : base.GetInspectString();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                yield break;
            }
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                yield break;
            }
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }
        }
    }

    /// <summary>带 Comp 的普通物品代理（资源 / 食物 / 药 / 零部件等大多数物品）。</summary>
    public class SDS_Proxy_ThingWithComps : ThingWithComps, ISDS_ProxyThing
    {
        public SDS_StockKey Key
        {
            get { return SDS_OutputUtility.KeyOfThing(this); }
        }

        public bool Preserved
        {
            get { return false; }
        }

        public Thing ResolveReal(int count)
        {
            return SDS_ProxyUtility.Resolve(this, count);
        }

        public override Thing SplitOff(int count)
        {
            if (!SDS_ProxyUtility.IsActive(this))
            {
                return base.SplitOff(count);
            }
            return ResolveReal(count);
        }

        public override bool CanStackWith(Thing other)
        {
            if (SDS_ProxyUtility.IsActive(this) || SDS_ProxyUtility.IsActive(other))
            {
                return false;
            }
            return base.CanStackWith(other);
        }

        public override bool TryAbsorbStack(Thing other, bool respectStackLimit)
        {
            if (SDS_MirrorAbsorb.TryHandle(this, other, respectStackLimit, out bool result))
            {
                return result;
            }
            return base.TryAbsorbStack(other, respectStackLimit);
        }

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                absorbed = true;
                return;
            }
            base.PreApplyDamage(ref dinfo, out absorbed);
        }

        public override void Kill(DamageInfo? dinfo = null, Hediff exactCulprit = null)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.Kill(dinfo, exactCulprit);
        }

        public override void Print(SectionLayer layer)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.Print(layer);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.DrawAt(drawLoc, flip);
        }

        public override void DrawGUIOverlay()
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.DrawGUIOverlay();
        }

        public override string GetInspectString()
        {
            return SDS_ProxyUtility.IsActive(this) ? null : base.GetInspectString();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                yield break;
            }
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                yield break;
            }
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }
        }
    }

    /// <summary>衣物代理（保证 `is Apparel`、保暖 / 穿衣判断仍成立）。</summary>
    public class SDS_Proxy_Apparel : Apparel, ISDS_ProxyThing
    {
        public SDS_StockKey Key
        {
            get { return SDS_OutputUtility.KeyOfThing(this); }
        }

        public bool Preserved
        {
            get { return false; }
        }

        public Thing ResolveReal(int count)
        {
            return SDS_ProxyUtility.Resolve(this, count);
        }

        public override Thing SplitOff(int count)
        {
            if (!SDS_ProxyUtility.IsActive(this))
            {
                return base.SplitOff(count);
            }
            return ResolveReal(count);
        }

        public override bool CanStackWith(Thing other)
        {
            if (SDS_ProxyUtility.IsActive(this) || SDS_ProxyUtility.IsActive(other))
            {
                return false;
            }
            return base.CanStackWith(other);
        }

        public override bool TryAbsorbStack(Thing other, bool respectStackLimit)
        {
            if (SDS_MirrorAbsorb.TryHandle(this, other, respectStackLimit, out bool result))
            {
                return result;
            }
            return base.TryAbsorbStack(other, respectStackLimit);
        }

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                absorbed = true;
                return;
            }
            base.PreApplyDamage(ref dinfo, out absorbed);
        }

        public override void Kill(DamageInfo? dinfo = null, Hediff exactCulprit = null)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.Kill(dinfo, exactCulprit);
        }

        public override void Print(SectionLayer layer)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.Print(layer);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.DrawAt(drawLoc, flip);
        }

        public override void DrawGUIOverlay()
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                return;
            }
            base.DrawGUIOverlay();
        }

        public override string GetInspectString()
        {
            return SDS_ProxyUtility.IsActive(this) ? null : base.GetInspectString();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                yield break;
            }
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            if (SDS_ProxyUtility.IsActive(this))
            {
                yield break;
            }
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }
        }
    }
}
