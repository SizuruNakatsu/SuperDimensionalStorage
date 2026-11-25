using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RimWorld;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Contracts 专属测试：只覆盖跨模块高风险不变量，不逐成员复述实现。
    ///  - SDS_StockKey 的值相等 / 未解析身份 / 名字兜底（账本字典键与旧档读写都靠它）；
    ///  - 存档锁定节点名与 LookMode（《模块接口.md》8.3，契约级快照）；
    ///  - 以名字落盘的枚举成员名（Scribe_Values 写 ToString，改名即破档）；
    ///  - SDS_Host 槽位数量 / 注入复制 / 空注入容忍；
    ///  - SDS_Config 钳制与设置默认值。
    /// 测试工程仍只有 SDS.Tests 一个（逻辑独立即可），本文件只依赖 Contracts 与测试桩。
    /// </summary>
    public class ContractTests
    {
        // ---------- SDS_StockKey：值相等 / 身份 ----------

        [Fact]
        public void StockKey_ValueEquality_MatchesAllIdentityParts()
        {
            SDS_StockKey a = new SDS_StockKey { defName = "Steel", stuffName = "Wood", qualityInt = 2 };
            SDS_StockKey b = new SDS_StockKey { defName = "Steel", stuffName = "Wood", qualityInt = 2 };

            Assert.True(a.Equals(b));
            Assert.True(a.SameAs(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());

            Dictionary<SDS_StockKey, int> map = new Dictionary<SDS_StockKey, int> { { a, 1 } };
            map[b] = 2;
            Assert.Single(map);
            Assert.Equal(2, map[a]);
        }

        [Fact]
        public void StockKey_ChannelQualityStuff_AllParticipateInIdentity()
        {
            SDS_StockKey baseline = new SDS_StockKey { defName = "Steel", stuffName = "Wood", qualityInt = 2 };

            Assert.False(baseline.Equals(new SDS_StockKey { defName = "Steel", stuffName = "Wood", qualityInt = 3 }));
            Assert.False(baseline.Equals(new SDS_StockKey { defName = "Steel", stuffName = "Steel", qualityInt = 2 }));
            Assert.False(baseline.Equals(new SDS_StockKey { defName = "Plasteel", stuffName = "Wood", qualityInt = 2 }));
            // 虚拟与保真是两个通道，同 def 也必须不同键。
            Assert.False(baseline.Equals(new SDS_StockKey { defName = "Steel", stuffName = "Wood", qualityInt = 2, preserved = true }));
        }

        [Fact]
        public void StockKey_UnresolvedKeys_CompareByStoredNames()
        {
            SDS_StockKey a = new SDS_StockKey { defName = "MissingDef", stuffName = "MissingStuff", qualityInt = 1 };
            SDS_StockKey b = new SDS_StockKey { defName = "MissingDef", stuffName = "MissingStuff", qualityInt = 1 };

            Assert.False(a.Resolved);
            Assert.True(a.HasIdentity);
            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.Equal("MissingDef", a.EffectiveDefName);
            Assert.Equal("MissingStuff", a.EffectiveStuffName);
        }

        [Fact]
        public void StockKey_MixedResolvedStuff_MatchesByName_AndHashesConsistently()
        {
            ThingDef def = CoreTestKit.Def("ContractMixedDef");
            ThingDef stuff = CoreTestKit.Def("ContractMixedStuff");
            SDS_StockKey resolved = new SDS_StockKey(def, stuff, 2);
            SDS_StockKey unresolved = new SDS_StockKey
            {
                thingDef = def,
                defName = "ContractMixedDef",
                stuffName = "ContractMixedStuff",
                qualityInt = 2
            };

            Assert.Null(unresolved.stuff);
            Assert.True(resolved.SameAs(unresolved));
            Assert.True(unresolved.SameAs(resolved));
            Assert.Equal(resolved.GetHashCode(), unresolved.GetHashCode());

            Dictionary<SDS_StockKey, int> map = new Dictionary<SDS_StockKey, int> { { resolved, 1 } };
            map[unresolved] = 2;
            Assert.Single(map);
            Assert.Equal(2, map[resolved]);

            SDS_StockKey otherStuff = new SDS_StockKey
            {
                thingDef = def,
                defName = "ContractMixedDef",
                stuffName = "ContractOtherStuff",
                qualityInt = 2
            };
            Assert.False(resolved.SameAs(otherStuff));
        }

        [Fact]
        public void StockKey_HasIdentity_RejectsEmptyAndNullLiteral()
        {
            Assert.False(new SDS_StockKey().HasIdentity);
            Assert.False(new SDS_StockKey { defName = "" }.HasIdentity);
            Assert.False(new SDS_StockKey { defName = "null" }.HasIdentity);
            Assert.True(new SDS_StockKey { defName = "Steel" }.HasIdentity);
        }

        [Fact]
        public void StockKey_EnsureNames_NormalizesNullLiteralsAndPrefersLiveDef()
        {
            ThingDef live = CoreTestKit.Def("ContractLiveDef");
            SDS_StockKey key = new SDS_StockKey
            {
                thingDef = live,
                defName = "null",
                stuffName = "null"
            };

            key.EnsureNames();

            Assert.Equal("ContractLiveDef", key.defName);
            Assert.Null(key.stuffName);
            Assert.Equal("ContractLiveDef", key.EffectiveDefName);
        }

        [Fact]
        public void StockKey_TryResolve_UnknownNameStaysUnresolvedWithoutThrowing()
        {
            SDS_StockKey key = new SDS_StockKey { defName = "SDS_ContractTest_NoSuchDef_9F31" };

            Assert.False(key.TryResolve());
            Assert.False(key.Resolved);
            Assert.True(key.HasIdentity);
        }

        // ---------- 存档锁定节点 / LookMode ----------

        [Fact]
        public void StockKeyAndEntry_UseLockedSaveNodes()
        {
            string text = ReadContract("SDS_Stock.cs");

            Assert.Contains("Scribe_Values.Look(ref defName, \"defName\")", text);
            Assert.Contains("Scribe_Values.Look(ref stuffName, \"stuffName\")", text);
            // 2026-11-16 起停写旧节点；旧节点仍保留读取，保证 11.13 之前的档可读。
            Assert.DoesNotContain("\"thingDef\", \"null\"", text);
            Assert.DoesNotContain("\"stuff\", \"null\"", text);
            Assert.Contains("Scribe_Values.Look(ref legacyDefName, \"thingDef\")", text);
            Assert.Contains("Scribe_Values.Look(ref legacyStuffName, \"stuff\")", text);
            Assert.Contains("Scribe_Values.Look(ref qualityInt, \"qualityInt\", -1)", text);
            Assert.Contains("Scribe_Values.Look(ref preserved, \"preserved\", false)", text);

            Assert.Contains("Scribe_Deep.Look(ref key, \"key\")", text);
            Assert.Contains("Scribe_Values.Look(ref count, \"count\", 0L)", text);
            Assert.Contains("Scribe_Values.Look(ref avgHitPoints, \"avgHitPoints\", -1f)", text);
            Assert.Contains("Scribe_Values.Look(ref avgRotProgress, \"avgRotProgress\", -1f)", text);
        }

        [Fact]
        public void Reservation_UseLockedSaveNodes()
        {
            string text = ReadContract("SDS_Stock.cs");

            Assert.Contains("Scribe_Values.Look(ref jobLoadID, \"jobLoadID\", 0)", text);
            Assert.Contains("Scribe_Deep.Look(ref key, \"key\")", text);
            Assert.Contains("Scribe_Values.Look(ref count, \"count\", 0)", text);
            Assert.Contains("Scribe_References.Look(ref pawn, \"pawn\")", text);
        }

        [Fact]
        public void ReclaimSave_UseLockedSaveNodesAndLookModes()
        {
            string text = ReadContract("SDS_Withdraw.cs");

            Assert.Contains("Scribe_Values.Look(ref policy, \"policy\", SDS_ReclaimPolicy.JobTarget)", text);
            Assert.Contains("Scribe_Values.Look(ref jobLoadID, \"jobLoadID\", 0)", text);
            Assert.Contains("Scribe_Values.Look(ref ownerLoadID, \"ownerLoadID\", 0)", text);
            Assert.Contains("Scribe_Values.Look(ref tick, \"tick\", 0)", text);
            Assert.Contains("Scribe_Values.Look(ref hand, \"hand\", false)", text);
            Assert.Contains("Scribe_Collections.Look(ref things, \"things\", LookMode.Reference)", text);
            Assert.Contains("Scribe_Collections.Look(ref counts, \"counts\", LookMode.Value)", text);
        }

        [Fact]
        public void TransportLoadSave_UseLockedSaveNodes()
        {
            string text = ReadContract("SDS_CaravanTransport.cs");

            Assert.Contains("Scribe_Values.Look(ref containerThingID, \"containerThingID\", 0)", text);
            Assert.Contains("Scribe_Values.Look(ref rowIndex, \"rowIndex\", -1)", text);
            Assert.Contains("Scribe_Values.Look(ref defName, \"defName\")", text);
            Assert.Contains("Scribe_Values.Look(ref stuffName, \"stuffName\")", text);
            Assert.Contains("Scribe_Values.Look(ref qualityInt, \"qualityInt\", -1)", text);
            Assert.Contains("Scribe_Values.Look(ref preserved, \"preserved\", false)", text);
            Assert.Contains("Scribe_Values.Look(ref units, \"units\", 0)", text);
        }

        [Fact]
        public void SidebarUIState_UseLockedSaveNodesAndLookModes()
        {
            string text = ReadContract("SDS_UI.cs");

            Assert.Contains("Scribe_Collections.Look(ref collapsed, \"collapsed\", LookMode.Value)", text);
            Assert.Contains("Scribe_Values.Look(ref scroll, \"scroll\", Vector2.zero)", text);
            Assert.Contains("Scribe_Values.Look(ref hasSelection, \"hasSelection\", false)", text);
            Assert.Contains("Scribe_Deep.Look(ref selectedKey, \"selectedKey\")", text);
            Assert.Contains("Scribe_Values.Look(ref selectedPreserved, \"selectedPreserved\", false)", text);
        }

        [Fact]
        public void Settings_UseLockedSaveNodes()
        {
            string text = ReadContract("SDS_Mod.cs");

            Assert.Contains("Scribe_Values.Look(ref gridCapacity, \"gridCapacity\"", text);
            Assert.Contains("Scribe_Values.Look(ref capacityPerArray, \"capacityPerArray\"", text);
            Assert.Contains("Scribe_Values.Look(ref corePower, \"corePower\"", text);
            Assert.Contains("Scribe_Values.Look(ref arrayPower, \"arrayPower\"", text);
            Assert.Contains("Scribe_Values.Look(ref portPower, \"portPower\"", text);
            Assert.Contains("Scribe_Values.Look(ref excludeGridFromWealth, \"excludeGridFromWealth\"", text);
            Assert.Contains("Scribe_Values.Look(ref highestStoragePriority, \"highestStoragePriority\"", text);
            Assert.Contains("Scribe_Values.Look(ref forceWeaponApparelVirtual, \"forceWeaponApparelVirtual\"", text);
            Assert.Contains("Scribe_Values.Look(ref endpointStoragePriority, \"endpointStoragePriority\"", text);
            Assert.Contains("Scribe_Values.Look(ref separateByQuality, \"separateByQuality\"", text);
            Assert.Contains("Scribe_Values.Look(ref separateByStuff, \"separateByStuff\"", text);
        }

        [Fact]
        public void ReclaimPolicy_SaveNamesAreStable()
        {
            // Scribe_Values 对枚举写 ToString()：改名即破档，重排不受影响。
            Assert.Equal("JobTarget", SDS_ReclaimPolicy.JobTarget.ToString());
            Assert.Equal("ProtectedOutput", SDS_ReclaimPolicy.ProtectedOutput.ToString());
        }

        // ---------- 读档兜底（行为） ----------

        [Fact]
        public void SidebarUIState_Normalize_DropsBadValuesAndOrphanSelection()
        {
            SDS_SidebarUIState state = new SDS_SidebarUIState
            {
                collapsed = new List<string> { "a", null, "", "b" },
                scroll = new UnityEngine.Vector2(-1f, -2f),
                hasSelection = true,
                selectedKey = new SDS_StockKey(),
                selectedPreserved = true
            };

            state.Normalize();

            Assert.Equal(new[] { "a", "b" }, state.collapsed);
            Assert.Equal(0f, state.scroll.x);
            Assert.Equal(0f, state.scroll.y);
            Assert.False(state.hasSelection);
            Assert.Null(state.selectedKey);
            Assert.False(state.selectedPreserved);
        }

        // ---------- SDS_Host：槽位契约与空注入 ----------

        [Fact]
        public void HostSlots_DeclaresElevenSlots()
        {
            FieldInfo[] fields = typeof(SDS_HostSlots).GetFields(BindingFlags.Public | BindingFlags.Instance);

            Assert.Equal(11, fields.Length);
        }

        [Fact]
        public void HostInstall_CopiesEverySlot()
        {
            SDS_FakePortsProvider ports = new SDS_FakePortsProvider();
            SDS_FakeDepositFilter filter = new SDS_FakeDepositFilter();
            SDS_FakePrioritySource priority = new SDS_FakePrioritySource();
            SDS_FakeDeliverySink delivery = new SDS_FakeDeliverySink();
            SDS_FakeMirrorQuery mirrorQuery = new SDS_FakeMirrorQuery();
            SDS_FakeMirrorPhysical mirrorPhysical = new SDS_FakeMirrorPhysical();
            SDS_FakeMedicalCarePolicy medicalCare = new SDS_FakeMedicalCarePolicy();
            SDS_FakeReclaimWorld reclaimWorld = new SDS_FakeReclaimWorld();
            SDS_FakeDisplayQuery display = new SDS_FakeDisplayQuery();
            SDS_FakeDepositJobs depositJobs = new SDS_FakeDepositJobs();
            SDS_FakeTransportLoad transportLoad = new SDS_FakeTransportLoad();

            SDS_HostSlots slots = new SDS_HostSlots
            {
                ports = ports,
                depositFilter = filter,
                priority = priority,
                delivery = delivery,
                mirrorQuery = mirrorQuery,
                mirrorPhysical = mirrorPhysical,
                medicalCare = medicalCare,
                reclaimWorld = reclaimWorld,
                display = display,
                depositJobs = depositJobs,
                transportLoad = transportLoad
            };

            object[] backup = SaveHostSlots();
            try
            {
                SDS_Host.Install(slots);

                Assert.Same(ports, SDS_Host.Ports);
                Assert.Same(filter, SDS_Host.DepositFilter);
                Assert.Same(priority, SDS_Host.Priority);
                Assert.Same(delivery, SDS_Host.Delivery);
                Assert.Same(mirrorQuery, SDS_Host.MirrorQuery);
                Assert.Same(mirrorPhysical, SDS_Host.MirrorPhysical);
                Assert.Same(medicalCare, SDS_Host.MedicalCare);
                Assert.Same(reclaimWorld, SDS_Host.ReclaimWorld);
                Assert.Same(display, SDS_Host.Display);
                Assert.Same(depositJobs, SDS_Host.DepositJobs);
                Assert.Same(transportLoad, SDS_Host.TransportLoad);
            }
            finally
            {
                RestoreHostSlots(backup);
            }
        }

        [Fact]
        public void HostInstall_NullSlots_ClearsAllWithoutThrowing()
        {
            object[] backup = SaveHostSlots();
            try
            {
                SDS_Host.Install((SDS_HostSlots)null);

                Assert.Null(SDS_Host.Ports);
                Assert.Null(SDS_Host.DepositFilter);
                Assert.Null(SDS_Host.Priority);
                Assert.Null(SDS_Host.Delivery);
                Assert.Null(SDS_Host.MirrorQuery);
                Assert.Null(SDS_Host.MirrorPhysical);
                Assert.Null(SDS_Host.MedicalCare);
                Assert.Null(SDS_Host.ReclaimWorld);
                Assert.Null(SDS_Host.Display);
                Assert.Null(SDS_Host.DepositJobs);
                Assert.Null(SDS_Host.TransportLoad);
            }
            finally
            {
                RestoreHostSlots(backup);
            }
        }

        // ---------- SDS_Config / SDS_Settings ----------

        [Fact]
        public void Config_Clamp_RespectsBounds()
        {
            Assert.Equal(10, SDS_Config.ClampGridCapacity(-5));
            Assert.Equal(90, SDS_Config.ClampGridCapacity(999));
            Assert.Equal(30, SDS_Config.ClampGridCapacity(30));

            Assert.Equal(10, SDS_Config.ClampCapacityPerArray(0));
            Assert.Equal(90, SDS_Config.ClampCapacityPerArray(91));

            Assert.Equal(0f, SDS_Config.Clamp(-1f, 0f, 2000f));
            Assert.Equal(2000f, SDS_Config.Clamp(2500f, 0f, 2000f));
            Assert.Equal(0, SDS_Config.Clamp(-1, 0, 100));
            Assert.Equal(100, SDS_Config.Clamp(101, 0, 100));
        }

        [Fact]
        public void Config_NullCurrent_UsesDefaults()
        {
            SDS_Settings backup = SDS_Config.Current;
            SDS_Config.Current = null;
            try
            {
                Assert.Equal(SDS_Config.GridCapacityDefault, SDS_Config.GridCapacity);
                Assert.Equal(SDS_Config.CapacityPerArrayDefault, SDS_Config.CapacityPerArray);
                Assert.True(SDS_Config.SeparateByQuality);
                Assert.True(SDS_Config.SeparateByStuff);
                Assert.False(SDS_Config.IgnorePriorityMode);
                Assert.False(SDS_Config.ForceWeaponApparelVirtual);
                Assert.False(SDS_Config.ExcludeGridFromWealth);
                Assert.Equal(StoragePriority.Normal, SDS_Config.CoreStoragePriority);
            }
            finally
            {
                SDS_Config.Current = backup;
            }
        }

        [Fact]
        public void Settings_ClampAll_ClampsEveryRange()
        {
            SDS_Settings settings = new SDS_Settings
            {
                gridCapacity = 1,
                capacityPerArray = 999,
                corePower = -5f,
                arrayPower = 99999f,
                portPower = 5000f,
                endpointStoragePriority = 99
            };

            settings.ClampAll();

            Assert.Equal(SDS_Config.GridCapacityMin, settings.gridCapacity);
            Assert.Equal(SDS_Config.CapacityPerArrayMax, settings.capacityPerArray);
            Assert.Equal(SDS_Config.CorePowerMin, settings.corePower);
            Assert.Equal(SDS_Config.ArrayPowerMax, settings.arrayPower);
            Assert.Equal(SDS_Config.PortPowerMax, settings.portPower);
            Assert.Equal((int)StoragePriority.Critical, settings.endpointStoragePriority);
        }

        [Fact]
        public void Settings_ResetToDefaults_RestoresEveryField()
        {
            SDS_Settings settings = new SDS_Settings
            {
                gridCapacity = 1,
                capacityPerArray = 1,
                corePower = 1f,
                arrayPower = 1f,
                portPower = 1f,
                excludeGridFromWealth = true,
                highestStoragePriority = true,
                forceWeaponApparelVirtual = true,
                endpointStoragePriority = (int)StoragePriority.Critical,
                separateByQuality = false,
                separateByStuff = false
            };

            settings.ResetToDefaults();

            Assert.Equal(SDS_Config.GridCapacityDefault, settings.gridCapacity);
            Assert.Equal(SDS_Config.CapacityPerArrayDefault, settings.capacityPerArray);
            Assert.Equal(SDS_Config.CorePowerDefault, settings.corePower);
            Assert.Equal(SDS_Config.ArrayPowerDefault, settings.arrayPower);
            Assert.Equal(SDS_Config.PortPowerDefault, settings.portPower);
            Assert.False(settings.excludeGridFromWealth);
            Assert.False(settings.highestStoragePriority);
            Assert.False(settings.forceWeaponApparelVirtual);
            Assert.Equal((int)StoragePriority.Normal, settings.endpointStoragePriority);
            Assert.True(settings.separateByQuality);
            Assert.True(settings.separateByStuff);
        }

        // ---------- 基础设施 ----------

        private static string ReadContract(string fileName)
        {
            string root = SourceRoot();
            Assert.NotNull(root);
            return File.ReadAllText(Path.Combine(root, "SDS.Contracts", fileName));
        }

        private static string SourceRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "SDS.Contracts")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "Tools")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }

        private static object[] SaveHostSlots()
        {
            return new object[]
            {
                SDS_Host.Ports, SDS_Host.DepositFilter, SDS_Host.Priority, SDS_Host.Delivery,
                SDS_Host.MirrorQuery, SDS_Host.MirrorPhysical, SDS_Host.MedicalCare,
                SDS_Host.ReclaimWorld, SDS_Host.Display, SDS_Host.DepositJobs, SDS_Host.TransportLoad
            };
        }

        private static void RestoreHostSlots(object[] slots)
        {
            SDS_Host.Ports = (ISDS_PortProvider)slots[0];
            SDS_Host.DepositFilter = (ISDS_DepositFilter)slots[1];
            SDS_Host.Priority = (ISDS_PrioritySource)slots[2];
            SDS_Host.Delivery = (ISDS_DeliverySink)slots[3];
            SDS_Host.MirrorQuery = (ISDS_MirrorQuery)slots[4];
            SDS_Host.MirrorPhysical = (ISDS_MirrorPhysical)slots[5];
            SDS_Host.MedicalCare = (ISDS_MedicalCarePolicy)slots[6];
            SDS_Host.ReclaimWorld = (ISDS_ReclaimWorld)slots[7];
            SDS_Host.Display = (ISDS_DisplayQuery)slots[8];
            SDS_Host.DepositJobs = (ISDS_DepositJobs)slots[9];
            SDS_Host.TransportLoad = (ISDS_TransportLoadSink)slots[10];
        }
    }
}
