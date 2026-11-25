using System;
using System.Reflection;
using RimWorld;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Vanilla 契约级自测：镜像表、暴露口径、访问点、代理工厂、两张动态注册表。
    /// 真实的枚举 / 财富 A/B 依赖地图与游戏内验证，不在这里覆盖。
    /// </summary>
    public class MirrorContractTests
    {
        private static Assembly Vanilla
        {
            get { return typeof(SDS_MirrorService).Assembly; }
        }

        private static Type VanillaType(string name)
        {
            Type type = Vanilla.GetType("SuperDimensionalStorage." + name);
            Assert.NotNull(type);
            return type;
        }

        private static MethodInfo StaticMethod(string typeName, string method, int paramCount = -1)
        {
            Type type = VanillaType(typeName);
            foreach (MethodInfo candidate in type.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (candidate.Name != method)
                {
                    continue;
                }
                if (paramCount < 0 || candidate.GetParameters().Length == paramCount)
                {
                    return candidate;
                }
            }
            Assert.Fail("找不到静态方法：" + typeName + "." + method + "/" + paramCount);
            return null;
        }

        private static object Call(string typeName, string method, int paramCount, params object[] args)
        {
            return StaticMethod(typeName, method, paramCount).Invoke(null, args);
        }

        [Fact]
        public void MirrorService_EmptyWorld_IsSafe()
        {
            SDS_MirrorService.Reset();
            SDS_MirrorService service = new SDS_MirrorService();
            Assert.Empty(service.VisibleThingsOfDef(null, null));
            Thing mirror;
            Assert.False(SDS_MirrorService.TryGetMirror(null, null, out mirror));
            Assert.Null(mirror);
            Assert.False(SDS_MirrorService.HasAnyMirrorOn(null));
            Assert.False(SDS_MirrorService.AnyMirrorInWorld);
            int before = ((ISDS_MirrorQuery)service).Version;
            SDS_MirrorService.Reset();
            Assert.True(((ISDS_MirrorQuery)service).Version > before);
        }

        [Fact]
        public void MirrorRegistry_MarkAndUnmark_ByReference()
        {
            SDS_MirrorService.Reset();
            ThingDef def = CoreTestKit.Def("MirrorMark");
            Thing mirror = CoreTestKit.Make(def);
            Thing other = CoreTestKit.Make(def);
            Call("SDS_MirrorRegistry", "Mark", 1, mirror);
            Assert.True((bool)Call("SDS_MirrorRegistry", "IsMirror", 1, mirror));
            Assert.False((bool)Call("SDS_MirrorRegistry", "IsMirror", 1, other));
            Call("SDS_MirrorRegistry", "Unmark", 1, mirror);
            Assert.False((bool)Call("SDS_MirrorRegistry", "IsMirror", 1, mirror));
        }

        [Fact]
        public void Exposure_NoWorld_ReturnsZero()
        {
            SDS_MirrorService.Reset();
            Assert.False((bool)Call("SDS_Exposure", "MapExposesGrid", 1, new object[] { null }));
            Assert.Equal(0L, (long)Call("SDS_Exposure", "CounterAdjust", 3, null, null, false));
            Assert.Equal(0f, (float)Call("SDS_Exposure", "WealthAdjust", 1, new object[] { null }));
            object[] anchorArgs = { null, null };
            Assert.False((bool)Call("SDS_Exposure", "TryGetExposureAnchor", 2, anchorArgs));
            Assert.Null(anchorArgs[1]);
        }

        [Fact]
        public void AccessPointResolver_NullInputs_AreSafe()
        {
            Call("SDS_AccessPointResolver", "Invalidate", 0);
            Assert.Null(Call("SDS_AccessPointResolver", "GetPoints", 1, new object[] { null }));
            object[] args = { IntVec3.Invalid, null, null };
            Assert.False((bool)Call("SDS_AccessPointResolver", "TryGetNearestAccess", 3, args));
            Assert.Null(args[2]);
            Call("SDS_AccessPointResolver", "Invalidate", 0);
        }

        [Fact]
        public void ProxyFactory_SelectsDeclaredBaseTypes()
        {
            ThingDef thing = CoreTestKit.Def("ProxyThing");
            Assert.Equal(typeof(SDS_Proxy_Thing), ProxyTypeFor(thing));

            ThingDef comps = CoreTestKit.Def("ProxyComps");
            comps.thingClass = typeof(ThingWithComps);
            Assert.Equal(typeof(SDS_Proxy_ThingWithComps), ProxyTypeFor(comps));

            ThingDef apparel = CoreTestKit.Def("ProxyApparel");
            apparel.thingClass = typeof(Apparel);
            Assert.Equal(typeof(SDS_Proxy_Apparel), ProxyTypeFor(apparel));

            ThingDef custom = CoreTestKit.Def("ProxyCustom");
            custom.thingClass = typeof(Building);
            Assert.Null(ProxyTypeFor(custom));
        }

        private static Type ProxyTypeFor(ThingDef def)
        {
            return (Type)Call("SDS_ProxyFactory", "ProxyTypeFor", 1, def);
        }

        [Fact]
        public void GuardRegistry_HasNineteenEntries()
        {
            Assert.Equal(19, EntryCount("SDS_MirrorGuardRegistry"));
        }

        [Fact]
        public void ProjectionOwnerBridge_HasTenEntries()
        {
            Assert.Equal(10, EntryCount("SDS_ProjectionOwnerBridge"));
        }

        private static int EntryCount(string typeName)
        {
            FieldInfo entries = VanillaType(typeName).GetField("Entries",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(entries);
            Array array = entries.GetValue(null) as Array;
            Assert.NotNull(array);
            return array.Length;
        }
    }
}
