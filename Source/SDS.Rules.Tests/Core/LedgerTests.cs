using System.Collections.Generic;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    public class LedgerTests
    {
        [Fact]
        public void Capacity_BlocksNewCategory_ButAllowsExisting()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 2, perArray: 1);
            env.AddStructure(SDS_StructureKind.Core);

            ThingDef a = CoreTestKit.Def("CapA");
            ThingDef b = CoreTestKit.Def("CapB");
            ThingDef c = CoreTestKit.Def("CapC");

            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a), env.Port, out int acceptedA));
            Assert.Equal(1, acceptedA);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(b), env.Port, out _));
            Assert.False(SDS_Rules.TryDeposit(CoreTestKit.Make(c), env.Port, out _));

            // 同类目继续堆叠不受容量限制
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 2), env.Port, out _));
            Assert.Equal(2, SDS_Core.UsedCapacity);

            // 稳固装置扩容后第三个品类可存入
            env.AddStructure(SDS_StructureKind.CapacityArray);
            Assert.Equal(3, SDS_Rules.TypeCapacity);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(c), env.Port, out _));
        }

        [Fact]
        public void Capacity_ReactsToSettingChangeWithoutRebuild()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 2, perArray: 1);
            env.AddStructure(SDS_StructureKind.Core);
            env.AddStructure(SDS_StructureKind.CapacityArray);
            Assert.Equal(3, SDS_Rules.TypeCapacity);

            SDS_Config.Current.capacityPerArray = 5;
            Assert.Equal(7, SDS_Rules.TypeCapacity);

            SDS_Config.Current.gridCapacity = 4;
            Assert.Equal(9, SDS_Rules.TypeCapacity);
        }

        [Fact]
        public void SameKey_MergesIntoOneEntry()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("MergeA");

            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 3), env.Port, out _));
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 4), env.Port, out _));

            List<SDS_StockEntry> snapshot = env.Ledger.Snapshot(0);
            Assert.Single(snapshot);
            Assert.Equal(7L, snapshot[0].count);
            Assert.Equal(1, SDS_Core.UsedCapacity);
        }

        [Fact]
        public void DepositFilter_RejectsAndBumpsFilterVersion()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef banned = CoreTestKit.Def("Banned");
            env.Filter.AllowsFn = thing => thing.def.defName != "Banned";

            Assert.False(SDS_Rules.TryDeposit(CoreTestKit.Make(banned), env.Port, out _));
            Assert.Equal(0L, env.Ledger.GetCount(new SDS_StockKey(banned, null, -1)));

            int before = env.Ledger.FilterVersion;
            SDS_Core.NotifyDepositFilterChanged();
            Assert.Equal(before + 1, env.Ledger.FilterVersion);
        }

        [Fact]
        public void PreserveHook_RoutesToPreservedChannel()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("HookA");
            SDS_PreserveHooks.Register(thing => thing.def != null && thing.def.defName == "HookA");

            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 1), env.Port, out _));

            Assert.Equal(0L, env.Ledger.GetCount(new SDS_StockKey(a, null, -1)));
            Assert.Equal(1L, env.Ledger.GetPreservedCount(new SDS_StockKey(a, null, -1, true)));
        }

        [Fact]
        public void PreservedDeposit_RejectsCorpseWithoutInnerPawn()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("BareCorpse");
            def.thingClass = typeof(Corpse);
            Corpse corpse = new Corpse();
            corpse.def = def;
            corpse.stackCount = 1;

            // 无内层小人的尸体不能进保真通道：原版尸体系筛选 / TransferAsOne 会直接空引用。
            Assert.False(SDS_Rules.TryDeposit(corpse, env.Port, out _));
            Assert.Equal(0L, env.Ledger.GetPreservedCount(new SDS_StockKey(def, null, -1, true)));
            List<Thing> preserved = new List<Thing>();
            env.Ledger.AppendPreservedThings(preserved);
            Assert.Empty(preserved);
        }

        [Fact]
        public void PreservedAvailability_SubtractsReservations()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("PreservedAvail");
            SDS_PreserveHooks.Register(thing => thing.def != null && thing.def.defName == "PreservedAvail");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));

            SDS_StockKey key = new SDS_StockKey(def, null, -1, true);
            Assert.Equal(3L, SDS_Availability.GetAvailableCount(key));
            Assert.Equal(SDS_SupplyFailure.None, SDS_Availability.GetFailure(key));

            List<Thing> preserved = new List<Thing>();
            env.Ledger.AppendPreservedThings(preserved);
            Assert.True(SDS_Rules.TryReservePreservedForJob(77,
                new List<ThingCount> { new ThingCount(preserved[0], 2) }, null));

            Assert.Equal(1L, SDS_Availability.GetAvailableCount(key));
            Assert.Equal(SDS_SupplyFailure.None, SDS_Availability.GetFailure(key));
            Assert.True(SDS_Rules.TryReservePreservedForJob(78,
                new List<ThingCount> { new ThingCount(preserved[0], 1) }, null));
            Assert.Equal(0L, SDS_Availability.GetAvailableCount(key));
            Assert.Equal(SDS_SupplyFailure.Insufficient, SDS_Availability.GetFailure(key));
        }

        [Fact]
        public void Forbidden_TogglesAvailabilityAndVersion()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("ForbidToggle");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));

            int before = env.Ledger.StockVersion;
            Assert.True(SDS_Core.SetForbidden(key, true));
            Assert.True(env.Ledger.StockVersion > before);
            Assert.True(env.Ledger.IsForbidden(key));
            Assert.Equal(0L, SDS_Availability.GetAvailableCount(key));
            Assert.Equal(SDS_SupplyFailure.Forbidden, SDS_Availability.GetFailure(key));

            Assert.True(SDS_Core.SetForbidden(key, false));
            Assert.Equal(3L, SDS_Availability.GetAvailableCount(key));
        }

        [Fact]
        public void DefAvailability_IncludesVirtualAndPreserved()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("DefBothChannels");
            Thing preserved = CoreTestKit.Make(def, 2);
            SDS_PreserveHooks.Register(thing => ReferenceEquals(thing, preserved));

            Assert.True(SDS_Rules.TryDeposit(preserved, env.Port, out _));
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 2), env.Port, out _));

            Assert.Equal(4L, SDS_Rules.GetAvailableCountForDef(def));
        }

        [Fact]
        public void ConsumeVirtual_ClampsToHaveAndAllowsZero()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("ConsumeClamp");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));

            object[] args = { key, 10, 0 };
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryConsumeVirtual", args));
            Assert.Equal(3, (int)args[2]);
            Assert.Equal(0L, env.Ledger.GetCount(key));

            object[] zeroArgs = { key, 10, 0 };
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryConsumeVirtual", zeroArgs));
            Assert.Equal(0, (int)zeroArgs[2]);
        }

        [Fact]
        public void TradableCount_ZeroWhenGridUnavailable()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("TradableNoGrid");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryDepositVirtual",
                key, 5L, new SDS_StockVitals()));

            Assert.False(SDS_Rules.Available);
            Assert.Equal(0L, SDS_Availability.GetTradableCount(key));
        }

        [Fact]
        public void TradableCount_SubtractsReservations()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("TradableA");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 5), env.Port, out _));

            Assert.Equal(5L, SDS_Availability.GetTradableCount(key));
            Assert.True(SDS_Rules.TryReserveForJob(901, def, 2, null, key, out _, out _));
            Assert.Equal(3L, SDS_Availability.GetTradableCount(key));
        }

        [Fact]
        public void DefSelection_TradableIgnoresForbidden_WithdrawableDoesNot()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("DefSelectForbidden");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));
            Assert.True(SDS_Core.SetForbidden(key, true));

            // 生产 / 取用：禁用归零，选不到键。
            Assert.False(SDS_Rules.TryFindWithdrawableKey(def, out _, out _));

            // 交易 / 捐献 / 发射：R15，不因禁用归零。
            Assert.True(SDS_Rules.TryFindTradableKey(def, out SDS_StockKey tradeKey, out int available));
            Assert.True(tradeKey.SameAs(key));
            Assert.Equal(3, available);
        }

        [Fact]
        public void DefSelection_TradableFallsBackToPreserved()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("DefSelectPreserved");
            Thing preserved = CoreTestKit.Make(def, 1);
            SDS_PreserveHooks.Register(thing => ReferenceEquals(thing, preserved));
            Assert.True(SDS_Rules.TryDeposit(preserved, env.Port, out _));

            Assert.True(SDS_Rules.TryFindTradableKey(def, out SDS_StockKey key, out int available));
            Assert.True(key.preserved);
            Assert.Equal(1, available);
        }

        [Fact]
        public void ProducedMarker_PublicFacade_RoundTrips()
        {
            RulesTestEnv.Create(capacity: 30);
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ProducedA"));
            SDS_Rules.MarkProduced(thing);
            Assert.True(SDS_Rules.IsProducedMarked(thing));
            SDS_Rules.UnmarkProduced(thing);
            Assert.False(SDS_Rules.IsProducedMarked(thing));
        }

        [Fact]
        public void ThreeVersions_AdvanceOnTheirOwnChanges()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("VersionA");
            SDS_StockKey key = new SDS_StockKey(a, null, -1);

            int stockBefore = env.Ledger.StockVersion;
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 5), env.Port, out _));
            Assert.True(env.Ledger.StockVersion > stockBefore);

            int reservationBefore = env.Ledger.ReservationVersion;
            Assert.True(SDS_Rules.TryReserveForJob(91, a, 2, null, key, out _, out _));
            Assert.True(env.Ledger.ReservationVersion > reservationBefore);
            Assert.Equal(2L, env.Ledger.GetReserved(key));

            int filterBefore = env.Ledger.FilterVersion;
            SDS_Core.NotifyDepositFilterChanged();
            Assert.True(env.Ledger.FilterVersion > filterBefore);
        }

        [Fact]
        public void PreservedDeposit_TakesOwnership_AndRejectsDuplicate()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("PreservedOwnership");
            Thing thing = CoreTestKit.Make(def, 1);
            SDS_PreserveHooks.Register(t => ReferenceEquals(t, thing));

            TestHolder source = new TestHolder();
            Assert.True(source.Owner.TryAdd(thing, false));

            Assert.True(SDS_Rules.TryDeposit(thing, env.Port, out int accepted));
            Assert.Equal(1, accepted);
            // 原容器必须被真正摘出（不是置空 holdingOwner 后留在旧列表里）。
            Assert.Empty(source.Owner);
            Assert.NotSame(source.Owner, thing.holdingOwner);
            Assert.True(env.Ledger.IsPreserved(thing));
            SDS_StockKey key = new SDS_StockKey(def, null, -1, true);
            Assert.Equal(1L, env.Ledger.GetPreservedCount(key));

            // 同一真身只允许入列一次。
            Assert.False(SDS_Rules.TryDeposit(thing, env.Port, out _));
            Assert.Equal(1L, env.Ledger.GetPreservedCount(key));
        }

        [Fact]
        public void Deposit_DestroyThrows_RollsBackLedgerAndKeepsThing()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("ThrowingDestroyDeposit");
            def.thingClass = typeof(ThrowingDestroyThing);
            Thing thing = CoreTestKit.Make(def, 3);

            // 入账成功但销毁抛异常：账本必须回到入账前，实物保持原状（数量守恒）。
            Assert.False(SDS_Rules.TryDeposit(thing, env.Port, out int accepted));
            Assert.Equal(0, accepted);
            Assert.Equal(0L, env.Ledger.GetCount(new SDS_StockKey(def, null, -1)));
            Assert.False(thing.Destroyed);
            Assert.Equal(3, thing.stackCount);
        }

        [Fact]
        public void Tick_RemovesPreservedThingWithoutLiveContent()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);

            // 缩装物没有 InnerThing → 内容失效（原版判定会空引用），不得计入数量，且巡检必须清掉。
            ThingDef def = CoreTestKit.Def("ZombieMinified");
            def.thingClass = typeof(RimWorld.MinifiedThing);
            Thing zombie = CoreTestKit.Make(def);

            System.Reflection.FieldInfo field = env.Ledger.GetType().GetField("preservedOwner",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            ThingOwner<Thing> owner = (ThingOwner<Thing>)field.GetValue(env.Ledger);
            Assert.True(owner.TryAdd(zombie));

            Assert.False(env.Ledger.IsPreservedThingId(zombie.thingIDNumber));
            Assert.Equal(0L, env.Ledger.GetPreservedCount(new SDS_StockKey(def, null, -1, true)));

            for (int i = 0; i < 250; i++)
            {
                env.Ledger.Tick();
            }
            Assert.Empty(owner);
        }

        [Fact]
        public void Reserve_Overflow_RejectedWithoutPartialWrite()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("OverflowItem");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);

            // 账面足够（50 亿），但单 key 预留总量超过 int 上限：必须整笔失败，不得静默截断。
            Assert.Equal(true, CoreTestKit.InvokeInternal(env.Ledger, "TryDepositVirtual",
                key, 5000000000L, null));
            object result = CoreTestKit.InvokeInternal(env.Ledger, "TryReserve", 4242,
                new List<SDS_StockKey> { key, key },
                new List<int> { int.MaxValue, int.MaxValue }, null);

            Assert.Equal(false, result);
            Assert.Equal(0L, env.Ledger.GetReserved(key));
        }

        private sealed class TestHolder : IThingHolder
        {
            public readonly ThingOwner<Thing> Owner;

            public TestHolder()
            {
                Owner = new ThingOwner<Thing>(this, false);
            }

            public IThingHolder ParentHolder => null;

            public void GetChildHolders(List<IThingHolder> outChildren)
            {
            }

            public ThingOwner GetDirectlyHeldThings()
            {
                return Owner;
            }
        }
    }
}
