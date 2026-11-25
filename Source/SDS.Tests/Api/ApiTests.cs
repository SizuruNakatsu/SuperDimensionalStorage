using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Api 测试宿主：Core 真实、Host 替身；只补测试进程缺的 Faction / Map / Pawn 壳。
    /// </summary>
    internal static class ApiTestKit
    {
        private static Faction playerFaction;

        public static RulesTestEnv Create()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            EnsurePlayerFaction();
            return env;
        }

        public static SDS_ApiCaller Caller
        {
            get
            {
                EnsurePlayerFaction();
                return SDS_ApiCaller.FromFaction(playerFaction);
            }
        }

        /// <summary>
        /// 只建壳、不进 Find.Maps：Destroy / ThingOwner 会遍历 Find.Maps，
        /// 壳 Map 缺少各 Manager，留在表里会污染后续所有 Core 测试。
        /// </summary>
        public static Map NewMap()
        {
            CoreTestKit.EnsureGameStub();
            return (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        }

        public static void RegisterMap(Map map)
        {
            CoreTestKit.EnsureGameStub();
            if (!Current.Game.Maps.Contains(map))
            {
                Current.Game.Maps.Add(map);
            }
        }

        public static void ReleaseMap(Map map)
        {
            if (Current.Game != null && Current.Game.Maps != null)
            {
                Current.Game.Maps.Remove(map);
            }
        }

        public static void Spawn(Thing thing, Map map)
        {
            RegisterMap(map);
            int index = Current.Game.Maps.IndexOf(map);
            FieldInfo state = typeof(Thing).GetField("mapIndexOrState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            state.SetValue(thing, Convert.ChangeType(index, state.FieldType));
            SetField(thing, "positionInt", IntVec3.Zero);
        }

        public static Pawn PlayerPawn(Map map)
        {
            EnsurePlayerFaction();
            Pawn pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
            Spawn(pawn, map);
            SetField(pawn, "factionInt", playerFaction);
            Pawn_HealthTracker health =
                (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
            SetField(health, "healthState", PawnHealthState.Mobile);
            SetField(health, "hediffSet", new HediffSet(pawn));
            SetField(pawn, "health", health);
            return pawn;
        }

        public static Faction ForeignFaction()
        {
            return (Faction)RuntimeHelpers.GetUninitializedObject(typeof(Faction));
        }

        private static void EnsurePlayerFaction()
        {
            if (playerFaction != null)
            {
                return;
            }
            CoreTestKit.EnsureGameStub();
            if (Current.Game == null)
            {
                throw new InvalidOperationException("ApiTestKit: Current.Game 为空");
            }
            FieldInfo worldField = typeof(Game).GetField("worldInt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (worldField == null)
            {
                throw new InvalidOperationException("ApiTestKit: Game.worldInt 字段不存在");
            }
            World world = worldField.GetValue(Current.Game) as World;
            if (world == null)
            {
                world = (World)RuntimeHelpers.GetUninitializedObject(typeof(World));
                worldField.SetValue(Current.Game, world);
            }
            FieldInfo managerField = typeof(World).GetField("factionManager",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (managerField == null)
            {
                throw new InvalidOperationException("ApiTestKit: World.factionManager 字段不存在");
            }
            FactionManager manager = managerField.GetValue(world) as FactionManager;
            if (manager == null)
            {
                manager = new FactionManager();
                managerField.SetValue(world, manager);
            }
            FieldInfo ofPlayerField = typeof(FactionManager).GetField("ofPlayer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (ofPlayerField == null)
            {
                throw new InvalidOperationException("ApiTestKit: FactionManager.ofPlayer 字段不存在");
            }
            playerFaction = (Faction)RuntimeHelpers.GetUninitializedObject(typeof(Faction));
            ofPlayerField.SetValue(manager, playerFaction);
        }

        private static void SetField(object target, string name, object value)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
                type = type.BaseType;
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }
    }

    public class ApiTests
    {
        [Fact]
        public void Version_IsFour()
        {
            Assert.Equal(4, SDS_API.Version);
        }

        [Fact]
        public void VisibleThingsOfDef_ForwardsToHostMirrorQuery()
        {
            RulesTestEnv env = ApiTestKit.Create();
            Map map = ApiTestKit.NewMap();
            ThingDef def = CoreTestKit.Def("VisibleA");
            Thing visible = CoreTestKit.Make(def);
            env.MirrorQuery.Visible.Add(visible);

            IReadOnlyList<Thing> result = SDS_API.VisibleThingsOfDef(map, def);

            Assert.Single(result);
            Assert.Same(visible, result[0]);
            Assert.Empty(SDS_API.VisibleThingsOfDef(null, def));
        }

        [Fact]
        public void ReadStock_ReportsTotalAvailableAndVersions()
        {
            RulesTestEnv env = ApiTestKit.Create();
            ThingDef def = CoreTestKit.Def("ReadA");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));

            SDS_ApiStockInfo info = SDS_API.ReadStock(key);

            Assert.Equal(SDS_ApiResultCode.Ok, info.result);
            Assert.True(info.resolved);
            Assert.Equal(3L, info.total);
            Assert.Equal(3L, info.available);
            Assert.Equal(SDS_SupplyFailure.None, info.failure);
            Assert.Equal(env.Ledger.StockVersion, info.versions.stock);
            Assert.Equal(env.Ledger.ReservationVersion, info.versions.reservation);
            Assert.Equal(env.Ledger.FilterVersion, info.versions.filter);
        }

        [Fact]
        public void ReadStock_GridUnavailable_ZeroesAvailableKeepsTotal()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("ReadOff");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));

            SDS_ApiStockInfo info = SDS_API.ReadStock(key);

            Assert.Equal(SDS_ApiResultCode.GridUnavailable, info.result);
            Assert.Equal(3L, info.total);
            Assert.Equal(0L, info.available);
            Assert.Equal(SDS_SupplyFailure.Insufficient, info.failure);

            // 未解析优先：key 为空时不借道 G 关闭分支，仍回答 UnresolvedDef。
            SDS_ApiStockInfo unresolved = SDS_API.ReadStock(null);
            Assert.Equal(SDS_ApiResultCode.GridUnavailable, unresolved.result);
            Assert.Equal(SDS_SupplyFailure.UnresolvedDef, unresolved.failure);
        }

        [Fact]
        public void ReadStockFor_RoutesByCoreChannel()
        {
            RulesTestEnv env = ApiTestKit.Create();
            ThingDef virtualDef = CoreTestKit.Def("ReadVirtual");
            ThingDef preservedDef = CoreTestKit.Def("ReadPreserved");
            SDS_PreserveHooks.Register(thing => thing.def != null && thing.def.defName == "ReadPreserved");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(virtualDef, 2), env.Port, out _));
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(preservedDef, 2), env.Port, out _));

            SDS_ApiStockInfo virtualInfo = SDS_API.ReadStockFor(CoreTestKit.Make(virtualDef));
            SDS_ApiStockInfo preservedInfo = SDS_API.ReadStockFor(CoreTestKit.Make(preservedDef));

            Assert.Equal(2L, virtualInfo.total);
            Assert.Equal(2L, virtualInfo.available);
            Assert.False(virtualInfo.key.preserved);
            Assert.Equal(2L, preservedInfo.total);
            Assert.Equal(2L, preservedInfo.available);
            Assert.Equal(SDS_SupplyFailure.None, preservedInfo.failure);
            Assert.True(preservedInfo.key.preserved);
        }

        [Fact]
        public void CaptureReadVersions_CarriesAllSixSources()
        {
            RulesTestEnv env = ApiTestKit.Create();
            env.MirrorQuery.Version = 7;
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(CoreTestKit.Def("VersA"), 1), env.Port, out _));

            SDS_ReadVersions versions = SDS_API.CaptureReadVersions();

            Assert.Equal(7, versions.mirror);
            Assert.Equal(SDS_Rules.EndpointVersion, versions.endpoint);
            Assert.Equal(env.Ledger.StockVersion, versions.stock);
            Assert.Equal(env.Ledger.ReservationVersion, versions.reservation);
            Assert.Equal(env.Ledger.FilterVersion, versions.filter);
            Assert.True(versions.tick >= 0);
        }

        [Fact]
        public void FindDepositEndpoint_ForwardsToCoreSelection()
        {
            RulesTestEnv env = ApiTestKit.Create();
            Map map = ApiTestKit.NewMap();
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("FindA"));

            Assert.True(SDS_API.TryFindDepositEndpoint(thing, map, IntVec3.Zero, ApiTestKit.Caller,
                out Thing endpoint, out SDS_ApiResultCode code));

            Assert.Equal(SDS_ApiResultCode.Ok, code);
            Assert.Same(env.Port.Thing, endpoint);

            env.Ports.Candidates.Clear();
            Assert.False(SDS_API.TryFindDepositEndpoint(thing, map, IntVec3.Zero, ApiTestKit.Caller,
                out _, out code));
            Assert.Equal(SDS_ApiResultCode.NoEndpoint, code);
        }

        [Fact]
        public void FindWithdrawPoint_ForwardsSelfPort()
        {
            RulesTestEnv env = ApiTestKit.Create();
            Map map = ApiTestKit.NewMap();
            ApiTestKit.RegisterMap(map);
            env.Port.IsSelf = true;
            try
            {
                Pawn pawn = ApiTestKit.PlayerPawn(map);
                Assert.True(SDS_API.TryFindWithdrawPoint(pawn, out Thing endpoint, out bool self,
                    out SDS_ApiResultCode code));

                Assert.Equal(SDS_ApiResultCode.Ok, code);
                Assert.Same(env.Port.Thing, endpoint);
                Assert.True(self);
            }
            finally
            {
                ApiTestKit.ReleaseMap(map);
            }
        }

        [Fact]
        public void DepositNow_EndToEnd_WritesLedger()
        {
            RulesTestEnv env = ApiTestKit.Create();
            Map map = ApiTestKit.NewMap();
            ThingDef def = CoreTestKit.Def("DepositA");
            Thing thing = CoreTestKit.Make(def, 4);

            Assert.True(SDS_API.TryDepositNow(thing, map, IntVec3.Zero, ApiTestKit.Caller,
                out int accepted, out SDS_ApiResultCode code));

            Assert.Equal(SDS_ApiResultCode.Ok, code);
            Assert.Equal(4, accepted);
            Assert.True(thing.Destroyed);
            Assert.Equal(4L, env.Ledger.GetCount(new SDS_StockKey(def, null, -1)));
        }

        [Fact]
        public void DepositAt_NonPlayerFaction_IsRejected()
        {
            RulesTestEnv env = ApiTestKit.Create();
            Map map = ApiTestKit.NewMap();
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("DepositB"));
            SDS_ApiCaller foreign = SDS_ApiCaller.FromFaction(ApiTestKit.ForeignFaction());

            Assert.False(SDS_API.TryDepositAt(thing, env.Port.Thing, foreign, out _, out SDS_ApiResultCode code));
            Assert.Equal(SDS_ApiResultCode.NotPlayerFaction, code);
        }

        [Fact]
        public void Withdraw_EndToEnd_MaterializesVirtualStock()
        {
            RulesTestEnv env = ApiTestKit.Create();
            ThingDef def = CoreTestKit.Def("WithdrawA");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 5), env.Port, out _));

            Assert.True(SDS_API.TryWithdraw(key, 3, SDS_WithdrawMode.Hand, ApiTestKit.Caller,
                out List<Thing> made, out SDS_ApiResultCode code));

            Assert.Equal(SDS_ApiResultCode.Ok, code);
            Assert.Single(made);
            Assert.Equal(3, made[0].stackCount);
            Assert.Equal(2L, env.Ledger.GetCount(key));
        }

        [Fact]
        public void Withdraw_PreservedKey_IsInvalidArgument()
        {
            RulesTestEnv env = ApiTestKit.Create();
            ThingDef def = CoreTestKit.Def("WithdrawB");
            SDS_StockKey preservedKey = new SDS_StockKey(def, null, -1, true);

            Assert.False(SDS_API.TryWithdraw(preservedKey, 1, SDS_WithdrawMode.Hand, ApiTestKit.Caller,
                out _, out SDS_ApiResultCode code));
            Assert.Equal(SDS_ApiResultCode.InvalidArgument, code);
        }

        [Fact]
        public void WithdrawFromMirror_ForwardsToCoreTakeMirror()
        {
            RulesTestEnv env = ApiTestKit.Create();
            ThingDef def = CoreTestKit.Def("MirrorA");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 4), env.Port, out _));
            Thing mirror = CoreTestKit.Make(def, 4);

            Assert.True(SDS_API.TryWithdrawFromMirror(mirror, 2, SDS_WithdrawMode.Container,
                ApiTestKit.Caller, out Thing taken, out SDS_ApiResultCode code));

            Assert.Equal(SDS_ApiResultCode.Ok, code);
            Assert.NotNull(taken);
            Assert.Equal(2, taken.stackCount);
            Assert.Equal(2L, env.Ledger.GetCount(key));
        }

        [Fact]
        public void WithdrawPreservedExact_EndToEnd()
        {
            RulesTestEnv env = ApiTestKit.Create();
            ThingDef def = CoreTestKit.Def("PreservedA");
            SDS_PreserveHooks.Register(thing => thing.def != null && thing.def.defName == "PreservedA");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 2), env.Port, out _));
            List<Thing> preserved = new List<Thing>();
            env.Ledger.AppendPreservedThings(preserved);
            Assert.Single(preserved);
            List<ThingCount> plan = new List<ThingCount> { new ThingCount(preserved[0], 2) };

            Assert.True(SDS_API.TryWithdrawPreservedExact(plan, SDS_WithdrawMode.Container,
                ApiTestKit.Caller, out List<Thing> made, out SDS_ApiResultCode code));

            Assert.Equal(SDS_ApiResultCode.Ok, code);
            Assert.Single(made);
            Assert.Equal(2, made[0].stackCount);
            Assert.Equal(0L, env.Ledger.GetPreservedCount(new SDS_StockKey(def, null, -1, true)));
        }

        [Fact]
        public void WithdrawPreservedExact_ForbiddenKey_ReturnsForbidden()
        {
            RulesTestEnv env = ApiTestKit.Create();
            ThingDef def = CoreTestKit.Def("PreservedForbidden");
            SDS_PreserveHooks.Register(thing => thing.def != null && thing.def.defName == "PreservedForbidden");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 1), env.Port, out _));
            SDS_StockKey key = new SDS_StockKey(def, null, -1, true);
            Assert.True(SDS_Core.SetForbidden(key, true));

            List<Thing> preserved = new List<Thing>();
            env.Ledger.AppendPreservedThings(preserved);
            Assert.Single(preserved);
            List<ThingCount> plan = new List<ThingCount> { new ThingCount(preserved[0], 1) };

            Assert.False(SDS_API.TryWithdrawPreservedExact(plan, SDS_WithdrawMode.Container,
                ApiTestKit.Caller, out _, out SDS_ApiResultCode code));
            Assert.Equal(SDS_ApiResultCode.Forbidden, code);
            Assert.Equal(1L, env.Ledger.GetPreservedCount(key));
        }

        [Fact]
        public void WithdrawPreservedByDef_HandlesStackedPreserved()
        {
            RulesTestEnv env = ApiTestKit.Create();
            ThingDef def = CoreTestKit.Def("PreservedStacked");
            SDS_PreserveHooks.Register(thing => thing.def != null && thing.def.defName == "PreservedStacked");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));
            SDS_StockKey key = new SDS_StockKey(def, null, -1, true);
            Assert.Equal(3L, env.Ledger.GetPreservedCount(key));

            Assert.True(SDS_API.TryWithdrawPreservedByDef(def, 2, null, ApiTestKit.Caller,
                out List<Thing> made, out SDS_ApiResultCode code));

            Assert.Equal(SDS_ApiResultCode.Ok, code);
            Assert.NotNull(made);
            Assert.Equal(2, made.Count == 1 ? made[0].stackCount : made.Count);
            Assert.Equal(1L, env.Ledger.GetPreservedCount(key));
        }
    }
}
