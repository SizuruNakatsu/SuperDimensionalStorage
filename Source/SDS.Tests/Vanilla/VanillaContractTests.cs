using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Vanilla 契约级自测：存档壳、宿主实现、补丁数量。
    /// 游戏内 A/B 不在这里覆盖。
    /// </summary>
    public class VanillaContractTests
    {
        private static void DetachGrid()
        {
            PropertyInfo instance = typeof(SDS_GameComponent_Grid).GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public);
            if (instance != null)
            {
                instance.SetValue(null, null);
            }
            SDS_Core.Attach(null);
        }

        [Fact]
        public void Grid_Shell_CreatesLedgerAndState_WithoutThingHolder()
        {
            try
            {
                SDS_GameComponent_Grid grid = new SDS_GameComponent_Grid(null);
                Assert.Same(grid, SDS_GameComponent_Grid.Instance);
                Assert.NotNull(grid.Ledger);
                Assert.Same(grid.Ledger, SDS_Core.Ledger);
                Assert.False(typeof(IThingHolder).IsAssignableFrom(typeof(SDS_GameComponent_Grid)));
            }
            finally
            {
                DetachGrid();
            }
        }

        [Fact]
        public void HostImplementations_ConstructWithoutGameState()
        {
            Assert.NotNull(new SDS_VanillaServices());
            Assert.NotNull(new SDS_EndpointProvider());
            Assert.NotNull(new SDS_MedicalCareAdapter());
            Assert.NotNull(new SDS_ReclaimWorldAdapter());
        }

        [Fact]
        public void VanillaServices_Defaults_WithoutSave()
        {
            try
            {
                DetachGrid();
                SDS_Config.Current = new SDS_Settings();
                SDS_VanillaServices services = new SDS_VanillaServices();
                Assert.True(services.Allows(CoreTestKit.Make(CoreTestKit.Def("VanillaServiceItem"))));
                Assert.Equal((int)SDS_Config.CoreStoragePriority, services.Priority);
                Assert.Equal(SDS_Config.IgnorePriorityMode, services.IgnorePriority);
            }
            finally
            {
                SDS_Config.Current = null;
            }
        }

        [Fact]
        public void EndpointProvider_EmptyWithoutSave()
        {
            DetachGrid();
            SDS_EndpointProvider provider = new SDS_EndpointProvider();
            Assert.False(provider.IsLocalUsable(null));
            ISDS_Port port;
            Assert.False(provider.TryGetPort(null, out port));
            Assert.Null(port);
            Assert.False(provider.TryGetSelfPort(null, out port));
            Assert.Null(port);
        }

        [Fact]
        public void MedicalCare_NonMedicineAndNonPawnBillGiver_Allow()
        {
            SDS_MedicalCareAdapter adapter = new SDS_MedicalCareAdapter();
            Assert.True(adapter.Allows(null, null));
            Assert.True(adapter.Allows(CoreTestKit.Def("NotMedicine"), null));
        }

        [Fact]
        public void Vanilla_RegisterAll_IsIdempotent()
        {
            SDS_Vanilla.RegisterAll();
            SDS_Vanilla.RegisterAll();
        }

        [Fact]
        public void Vanilla_DiagnosticsProvider_IsRegistered()
        {
            SDS_Vanilla.RegisterAll();
            Assert.True(SDS_SelfCheck.Count >= 1);
        }

        [Fact]
        public void Grid_HasLoadReportEntry()
        {
            Assert.NotNull(typeof(SDS_GameComponent_Grid).GetMethod("FinalizeInit",
                BindingFlags.Instance | BindingFlags.Public));
            Assert.NotNull(typeof(SDS_GameComponent_Grid).GetMethod("TryLogLoadDiagnostics",
                BindingFlags.Instance | BindingFlags.NonPublic));
        }

        [Fact]
        public void PatchInventory_V2ToV5_HasOneHundredTwelvePatchClasses()
        {
            Assembly assembly = typeof(SDS_VanillaServices).Assembly;
            int declared = 0;
            foreach (Type type in assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                {
                    declared++;
                }
            }
            Assert.Equal(112, declared);
        }

        [Fact]
        public void V4_PatchClasses_ArePresent()
        {
            Assembly assembly = typeof(SDS_VanillaServices).Assembly;
            string[] expected =
            {
                "Patch_SDS_ResourceCounterWithGrid", "Patch_SDS_ResourceCounterFastRefresh",
                "Patch_SDS_PotentiallyMissingIngredients", "Patch_SDS_MapChildHoldersExposure",
                "Patch_SDS_WealthNormalize", "Patch_SDS_HaulGeneralToGrid",
                "Patch_SDS_JobGiverHaul", "Patch_SDS_HaulToStorageJob",
                "Patch_SDS_CarriedProductToGrid", "Patch_SDS_HaulToContainerJob",
                "Patch_SDS_CellOnlyOrderedJob",
                "Patch_SDS_ClosestGlobal", "Patch_SDS_ClosestGlobalReachable",
                "Patch_SDS_ClosestReachable", "Patch_SDS_ClosestRegionwisePrioritized",
                "Patch_SDS_StealValueIgnoresHiddenGrid", "Patch_SDS_ReachabilityMirror",
                "Patch_SDS_Accessible", "Patch_SDS_PossiblyAccessible",
                "Patch_SDS_PlayerCanMake", "Patch_SDS_PlayerHasDef", "Patch_SDS_PlayerHasFilter",
                "Patch_SDS_TradeDealReset", "Patch_SDS_TradeDealValidate",
                "Patch_SDS_TradeableResolveTrade", "Patch_SDS_DialogTradeClose",
                "Patch_SDS_BeaconLaunchRead", "Patch_SDS_BeaconLaunchTake",
                "Patch_SDS_SettlementBuyList", "Patch_SDS_CaravanBuyList",
                "Patch_SDS_SettlementGiveBought", "Patch_SDS_CaravanGiveBought",
                "Patch_SDS_CaravanRecacheReset", "Patch_SDS_CaravanAddVirtual",
                "Patch_SDS_CaravanValidate", "Patch_SDS_CaravanBlockInstant",
                "Patch_SDS_CaravanPostCloseCleanup", "Patch_SDS_CaravanMaterialize",
                "Patch_SDS_ShuttleRequiredItemsFromGrid", "Patch_SDS_TransporterAddVirtual",
                "Patch_SDS_TransporterLoadHasJob", "Patch_SDS_TransporterLoadJob",
                "Patch_SDS_PortalAddVirtual", "Patch_SDS_PortalLoadHasJob",
                "Patch_SDS_PortalLoadJob", "Patch_SDS_TransporterDepositIdentity",
                "Patch_SDS_PortalDepositIdentity",
                "Patch_SDS_XenogermImplantAvailable", "Patch_SDS_SelectXenogermList",
                "Patch_SDS_XenogermMaterialize", "Patch_SDS_GeneDialogBegin",
                "Patch_SDS_GeneAssemblerGenepacks", "Patch_SDS_GrowthVatEmbryoGizmo",
                "Patch_SDS_GrowthVatSelectEmbryo", "Patch_SDS_CasketAcceptPreservedCorpse",
                "Patch_SDS_PlaceBlueprintForInstall", "Patch_SDS_PreservedMedicineOffer",
                "Patch_SDS_PreservedFoodOffer", "Patch_SDS_PreservedWeaponOffer",
                "Patch_SDS_PreservedApparelOffer", "Patch_SDS_PreservedOfferInStorage",
                "Patch_SDS_PreservedBillIngredients", "Patch_SDS_MedicalIngredientGate",
                "Patch_SDS_VirtualFixedIngredients", "Patch_SDS_PreservedBillJob"
            };
            Assert.Equal(65, expected.Length);
            foreach (string name in expected)
            {
                Type type = assembly.GetType("SuperDimensionalStorage." + name, false);
                Assert.NotNull(type);
                Assert.True(type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0, name);
            }
        }

        [Fact]
        public void V4_PublicJobDrivers_ArePresent()
        {
            Assert.NotNull(typeof(JobDriver_SDS_DepositToGrid));
            Assert.NotNull(typeof(JobDriver_SDS_WithdrawFromGrid));
            Assert.NotNull(typeof(JobDriver_SDS_WithdrawPreservedForBill));
        }

        [Fact]
        public void V5_PatchClasses_ArePresent()
        {
            Assembly assembly = typeof(SDS_VanillaServices).Assembly;
            string[] expected =
            {
                "Patch_SDS_JobMirrorTargets", "Patch_SDS_NoWearMirror",
                "Patch_SDS_MirrorTargetGoto", "Patch_SDS_ReclaimJobTargets",
                "Patch_SDS_OwnerTryAdd", "Patch_SDS_OwnerTryAddOrTransfer",
                "Patch_SDS_FloatMenuDeposit", "Patch_SDS_PawnDepositGizmo",
                "Patch_SDS_PlaySettingsButton", "Patch_SDS_RecipeCounterCountProducts",
                "Patch_SDS_AlertNeedWarmClothes", "Patch_SDS_JobReport",
                "Patch_SDS_JobReportHaulToContainer", "Patch_SDS_Hotkeys"
            };
            Assert.Equal(14, expected.Length);
            foreach (string name in expected)
            {
                Type type = assembly.GetType("SuperDimensionalStorage." + name, false);
                Assert.NotNull(type);
                Assert.True(type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0, name);
            }
        }

        [Fact]
        public void W51_MaterializePreservedGaps_EarlyOuts_AreSilent()
        {
            Assert.False(SDS_PreservedBillDispatch.TryMaterializePreservedGaps(null, null, null, null,
                out List<Thing> placed));
            Assert.Null(placed);
            RulesTestEnv env = RulesTestEnv.Create();
            try
            {
                ThingDef def = CoreTestKit.Def("W51GapSlot");
                // 非 Building 的 billGiver（医疗 / 尸体等 Pawn 类）一律不接管。
                Assert.False(SDS_PreservedBillDispatch.TryMaterializePreservedGaps(
                    null, null, CoreTestKit.Make(def), new List<IngredientCount>(), out placed));
                Assert.Null(placed);
            }
            finally
            {
                SDS_Core.Attach(null);
            }
        }

        [Fact]
        public void W62_SelfDepositFold_NullInputs_AreSafe()
        {
            Assert.False(SDS_SelfDepositFold.TryResolve(null, null, false, out ISDS_Port port));
            Assert.Null(port);
        }

        [Fact]
        public void W63_CellOnlyDestinationFold_NullInputs_AreSafe()
        {
            Assert.False(SDS_CellOnlyDestinationFold.TryRewrite(null, null));
            Assert.False(SDS_CellOnlyDestinationFold.CanRewrite(null, null, out _));
            Assert.False(SDS_CellOnlyDestinationFold.TryGetSelf(null, out ISDS_Port self));
            Assert.Null(self);
            Assert.False(SDS_CellOnlyDestinationFold.TryGetEndpoint(null, out ISDS_Port endpoint));
            Assert.Null(endpoint);
            SDS_CellOnlyDestinationFold.Reset();
        }

        [Fact]
        public void V5_XmlReferencedCompTypes_ArePresent()
        {
            Assembly assembly = typeof(SDS_VanillaServices).Assembly;
            foreach (string name in new[]
            {
                "SDS_CompUsable", "SDS_CompProperties_InstallChip", "SDS_CompInstallChip",
                "CompProperties_SDS_DrawExtraOverlay", "Comp_SDS_DrawExtraOverlay",
                "SDS_ITab_DepositFilter"
            })
            {
                Assert.NotNull(assembly.GetType("SuperDimensionalStorage." + name, false));
            }
        }

        [Fact]
        public void PatchInstaller_HarmonyId_IsStable()
        {
            Assert.Equal("sizuru.sds.vanilla", SDS_PatchInstallerImpl.HarmonyId);
        }

        /// <summary>
        /// 信标 / 捐献：保真展示物出现在清单后必须能被 ConsumeFromGrid 真正扣掉。
        /// 这是「按 def 选键」唯一实现的回归测试（此前私有副本漏了保真回退）。
        /// </summary>
        [Fact]
        public void BeaconConsume_PreservedOnly_WithdrawsFromLedger()
        {
            try
            {
                RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
                env.AddStructure(SDS_StructureKind.Core);
                ThingDef def = CoreTestKit.Def("BeaconPreservedOnly");
                Thing preserved = CoreTestKit.Make(def, 1);
                SDS_PreserveHooks.Register(thing => ReferenceEquals(thing, preserved));
                Assert.True(SDS_Rules.TryDeposit(preserved, env.Port, out _));

                SDS_StockKey key = new SDS_StockKey(def, null, -1, true);
                Assert.Equal(1L, env.Ledger.GetPreservedCount(key));

                Assert.Equal(1, SDS_BeaconDisplay.ConsumeFromGrid(def, 1));

                Assert.Equal(0L, env.Ledger.GetPreservedCount(key));
            }
            finally
            {
                SDS_PreserveHooks.Reset();
            }
        }

        /// <summary>W47：读档重建所需的 Job 预留只读门面，只回唯一键 + 合计件数。</summary>
        [Fact]
        public void TryGetJobReservation_ReturnsUniquePreservedKeyAndTotal()
        {
            try
            {
                RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
                env.AddStructure(SDS_StructureKind.Core);
                ThingDef def = CoreTestKit.Def("W47Reservation");
                SDS_PreserveHooks.Register(thing => thing.def != null && thing.def.defName == "W47Reservation");
                Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));

                List<Thing> preserved = new List<Thing>();
                env.Ledger.AppendPreservedThings(preserved);
                Assert.Single(preserved);
                Assert.True(SDS_Rules.TryReservePreservedForJob(4701,
                    new List<ThingCount> { new ThingCount(preserved[0], 2) }, null));

                Assert.True(SDS_Rules.TryGetJobReservation(4701, out SDS_StockKey key, out int total));
                Assert.NotNull(key);
                Assert.True(key.preserved);
                Assert.Equal(2, total);

                Assert.False(SDS_Rules.TryGetJobReservation(4702, out SDS_StockKey none, out int zero));
                Assert.Null(none);
                Assert.Equal(0, zero);

                SDS_Rules.ReleaseJobReservation(4701);
            }
            finally
            {
                SDS_PreserveHooks.Reset();
            }
        }
    }
}
