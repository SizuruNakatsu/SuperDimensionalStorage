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
    /// 2026-11-15 Core 加固回归：镜像件数对账、事务状态门、保真计划累计校验、
    /// 禁用失败分类、结构注册表清理。只经公开门面与既有测试反射工具。
    /// </summary>
    public class CoreHardeningTests
    {
        // ---------- 镜像：扣账件数必须等于实际交付件数 ----------

        [Fact]
        public void Mirror_RequestAboveStack_DebitsOnlyDeliveredUnits()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("MirrorQty");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 30), env.Port, out _));

            Thing mirror = CoreTestKit.Make(def, 10);
            Assert.True(SDS_Rules.TryTakeMirror(mirror, 30, env.MirrorPhysical, out Thing taken));

            Assert.NotNull(taken);
            Assert.Equal(10, taken.stackCount);
            Assert.Equal(20L, env.Ledger.GetCount(key));
            Assert.Equal(0L, env.Ledger.GetReserved(key));
        }

        [Fact]
        public void Mirror_SecondExecute_IsRejected()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("MirrorTwice");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 30), env.Port, out _));
            Thing mirror = CoreTestKit.Make(def, 10);

            Type txType = RulesTestKit.TransactionType;
            Assert.NotNull(txType);
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery { key = key, quantity = 10, preserved = false },
                endpoint = null,
                mode = SDS_WithdrawMode.Hand,
                physical = env.MirrorPhysical
            };
            object tx = txType.GetMethod("Begin", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { new List<SDS_WithdrawPlan> { plan }, null });
            Assert.NotNull(tx);

            MethodInfo execute = txType.GetMethod("ExecuteMirror");
            object[] first = { mirror, 5, null };
            Assert.True((bool)execute.Invoke(tx, first));
            Assert.Equal(5, ((Thing)first[2]).stackCount);
            Assert.Equal(25L, env.Ledger.GetCount(key));
            Assert.Equal(5L, env.Ledger.GetReserved(key));
            Assert.Equal(SDS_WithdrawEndState.Materialized, RulesTestKit.TransactionState(tx));

            // 状态门：第二次执行必须失败且零副作用。
            object[] second = { mirror, 5, null };
            Assert.False((bool)execute.Invoke(tx, second));
            Assert.Equal(25L, env.Ledger.GetCount(key));
            Assert.Equal(5L, env.Ledger.GetReserved(key));

            txType.GetMethod("Rollback").Invoke(tx, null);
            Assert.Equal(30L, env.Ledger.GetCount(key));
            Assert.Equal(0L, env.Ledger.GetReserved(key));
        }

        // ---------- 保真计划：同一真身按累计件数校验 ----------

        [Fact]
        public void Preserved_DuplicatePlanAboveStack_IsRejected()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("DupPreserved");
            SDS_PreserveHooks.Register(t => t.def != null && t.def.defName == "DupPreserved");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 5), env.Port, out _));

            List<Thing> stored = new List<Thing>();
            env.Ledger.AppendPreservedThings(stored);
            Assert.Single(stored);
            Thing thing = stored[0];
            SDS_StockKey preservedKey = new SDS_StockKey(def, null, -1, true);
            Assert.Equal(5L, env.Ledger.GetPreservedCount(preservedKey));

            // 3 + 3 > 5：逐条都合法，合计超了；建预留阶段就应拒绝。
            List<ThingCount> over = new List<ThingCount>
            {
                new ThingCount(thing, 3),
                new ThingCount(thing, 3)
            };
            Assert.False(SDS_Rules.TryReservePreservedForJob(900, over, null));
            Assert.False(SDS_Rules.TryWithdrawPreservedExact(over, null, out List<Thing> made));
            Assert.Null(made);
            Assert.Equal(5L, env.Ledger.GetPreservedCount(preservedKey));
            Assert.Equal(0L, env.Ledger.GetReserved(preservedKey));

            // 2 + 3 = 5：同一真身的合法累计计划仍可正常预留与取出。
            List<ThingCount> fitting = new List<ThingCount>
            {
                new ThingCount(thing, 2),
                new ThingCount(thing, 3)
            };
            Assert.True(SDS_Rules.TryReservePreservedForJob(901, fitting, null));
            Assert.Equal(5L, env.Ledger.GetReserved(preservedKey));
            SDS_Rules.ReleaseJobReservation(901);

            Assert.True(SDS_Rules.TryWithdrawPreservedExact(fitting, null, out List<Thing> taken));
            Assert.Equal(5, Units(taken));
            Assert.Equal(0L, env.Ledger.GetPreservedCount(preservedKey));
            Assert.Equal(0L, env.Ledger.GetReserved(preservedKey));
        }

        [Fact]
        public void Preserved_SecondExecuteExact_IsRejected()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("DoublePreserved");
            SDS_PreserveHooks.Register(t => t.def != null && t.def.defName == "DoublePreserved");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));

            List<Thing> stored = new List<Thing>();
            env.Ledger.AppendPreservedThings(stored);
            Thing thing = stored[0];
            SDS_StockKey preservedKey = new SDS_StockKey(def, null, -1, true);

            Type txType = RulesTestKit.TransactionType;
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery { key = preservedKey, quantity = 2, preserved = true },
                endpoint = null,
                mode = SDS_WithdrawMode.Hand
            };
            object tx = txType.GetMethod("Begin", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { new List<SDS_WithdrawPlan> { plan }, null });
            Assert.NotNull(tx);

            MethodInfo execute = txType.GetMethod("ExecutePreservedExact");
            object[] first = { new List<ThingCount> { new ThingCount(thing, 1) }, null };
            Assert.True((bool)execute.Invoke(tx, first));
            Assert.Equal(2L, env.Ledger.GetPreservedCount(preservedKey));
            Assert.Equal(SDS_WithdrawEndState.Materialized, RulesTestKit.TransactionState(tx));

            object[] second = { new List<ThingCount> { new ThingCount(thing, 1) }, null };
            Assert.False((bool)execute.Invoke(tx, second));
            Assert.Equal(2L, env.Ledger.GetPreservedCount(preservedKey));

            txType.GetMethod("Rollback").Invoke(tx, null);
            Assert.Equal(3L, env.Ledger.GetPreservedCount(preservedKey));
            Assert.Equal(0L, env.Ledger.GetReserved(preservedKey));
        }

        // ---------- 读侧口径与静态状态 ----------

        [Fact]
        public void GetFailure_ForbiddenOutranksUnavailable()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("ForbiddenOff");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Core.SetForbidden(key, true));

            Assert.False(SDS_Rules.Available);
            Assert.Equal(0L, SDS_Availability.GetAvailableCount(key));
            Assert.Equal(SDS_SupplyFailure.Forbidden, SDS_Availability.GetFailure(key));
        }

        [Fact]
        public void Structures_ResetAll_ClearsAvailabilityAndCapacity()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30, perArray: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.AddStructure(SDS_StructureKind.CapacityArray);
            Assert.True(SDS_Rules.Available);
            Assert.Equal(60L, SDS_Rules.TypeCapacity);

            SDS_Core.RegisterAll();
            SDS_Rules.RegisterAll();
            SDS_StaticState.ResetAll();

            Assert.False(SDS_Rules.Available);
            Assert.Equal(30L, SDS_Rules.TypeCapacity);
        }

        [Fact]
        public void WithdrawVirtual_PreservedKey_IsRejected()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("VirtualChannelGuard");
            SDS_StockKey preservedKey = new SDS_StockKey(def, null, -1, true);

            object result = CoreTestKit.InvokeInternal(env.Ledger, "TryWithdrawVirtual",
                preservedKey, 1, null);
            Assert.False((bool)result);
        }

        // ---------- 存档 / 诊断 / 圣物探针 ----------

        [Fact]
        public void CollectPreservedForSave_IncludesOrphanPending_ButNotDestroyed()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("PendingSaveDef");
            SDS_PreserveHooks.Register(t => t.def != null && t.def.defName == "PendingSaveDef");

            Thing ownerThing = CoreTestKit.Make(def);
            Assert.True(SDS_Rules.TryDeposit(ownerThing, env.Port, out _));

            FieldInfo pendingField = env.Ledger.GetType().GetField("pendingPreservedRestore",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(pendingField);
            List<Thing> pending = (List<Thing>)pendingField.GetValue(env.Ledger);
            Assert.NotNull(pending);

            Thing orphan = CoreTestKit.Make(def);
            Thing destroyed = CoreTestKit.Make(def);
            SetDestroyed(destroyed);
            pending.Add(orphan);
            pending.Add(destroyed);

            List<Thing> toSave = new List<Thing>();
            CoreTestKit.InvokeInternal(env.Ledger, "CollectPreservedForSave", toSave);

            Assert.Contains(ownerThing, toSave);
            Assert.Contains(orphan, toSave);
            Assert.DoesNotContain(destroyed, toSave);
        }

        [Fact]
        public void Diagnostics_DestroyedPreserved_IsReported()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("DiagPreserved");
            SDS_PreserveHooks.Register(t => t.def != null && t.def.defName == "DiagPreserved");
            Thing thing = CoreTestKit.Make(def);
            Assert.True(SDS_Rules.TryDeposit(thing, env.Port, out _));
            SetDestroyed(thing);

            // 数量口径只认存活真身；诊断口径必须还能看到它。
            List<Thing> live = new List<Thing>();
            env.Ledger.AppendPreservedThings(live);
            Assert.DoesNotContain(thing, live);

            List<Thing> all = new List<Thing>();
            CoreTestKit.InvokeInternal(env.Ledger, "AppendAllPreservedOwnerThings", all);
            Assert.Contains(thing, all);

            List<string> invariant = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(env.Ledger,
                "AppendPreservedInvariantReport", invariant);
            Assert.True(bad > 0);
            Assert.Contains(invariant, line => line.Contains("保真真身已销毁"));

            Type diagType = typeof(SDS_Core).Assembly.GetType("SuperDimensionalStorage.SDS_CoreDiagnostics");
            Assert.NotNull(diagType);
            object diagnostics = Activator.CreateInstance(diagType, true);
            List<string> report = new List<string>();
            diagType.GetMethod("AppendReport").Invoke(diagnostics, new object[] { report });
            Assert.Contains(report, line => line.Contains("保真真身失效 1 件"));
        }

        [Fact]
        public void RelicProbe_TransientFailure_DoesNotPermanentlyDisable()
        {
            Type reliquary = typeof(Thing).Assembly.GetType("RimWorld.ReliquaryUtility");
            Assert.NotNull(reliquary);
            MethodInfo target = reliquary.GetMethod("IsRelic",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Thing) }, null);
            Assert.NotNull(target);

            Type rules = typeof(SDS_Rules).Assembly.GetType("SuperDimensionalStorage.SDS_StockRules");
            Assert.NotNull(rules);
            MethodInfo requires = rules.GetMethod("RequiresPreservedStorage",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(Thing) }, null);
            Assert.NotNull(requires);
            // 一次性闩锁已删除：字段不存在即视为结构性回归通过。
            Assert.Null(rules.GetField("ideologyProbeFailed",
                BindingFlags.NonPublic | BindingFlags.Static));

            Harmony harmony = new Harmony("sds.test.relic.probe");
            RelicProbePatch.Calls = 0;
            try
            {
                MethodInfo prefix = typeof(RelicProbePatch).GetMethod("Prefix",
                    BindingFlags.Public | BindingFlags.Static);
                Assert.NotNull(prefix);
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));

                ThingDef def = CoreTestKit.Def("RelicProbeItem");
                Thing thing = CoreTestKit.Make(def);
                Assert.False((bool)requires.Invoke(null, new object[] { thing }));
                Assert.Equal(1, RelicProbePatch.Calls);

                // 旧实现第二次会直接返回 false，不再调用探针；新实现必须继续尝试。
                Assert.False((bool)requires.Invoke(null, new object[] { thing }));
                Assert.Equal(2, RelicProbePatch.Calls);
            }
            finally
            {
                harmony.UnpatchAll("sds.test.relic.probe");
            }
        }

        // ---------- 账本完整不变量：损坏必须被报告 ----------

        [Fact]
        public void LedgerInvariantReport_FlagsPreservedKeyInStock()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("InvariantPreservedKey_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1, true);
            Dictionary<SDS_StockKey, long> stock =
                (Dictionary<SDS_StockKey, long>)CoreTestKit.GetField(env.Ledger, "stock");
            stock[key] = 1L;

            List<string> lines = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(env.Ledger,
                "AppendLedgerInvariantReport", lines);

            Assert.True(bad > 0);
            Assert.Contains(lines, line => line.Contains("stock 混入保真键"));
        }

        [Fact]
        public void LedgerInvariantReport_FlagsNonPositiveReservation()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("InvariantReservation_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            List<SDS_Reservation> reservations =
                (List<SDS_Reservation>)CoreTestKit.GetField(env.Ledger, "reservations");
            reservations.Add(new SDS_Reservation
            {
                jobLoadID = 4242,
                key = key,
                count = 0
            });

            List<string> lines = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(env.Ledger,
                "AppendLedgerInvariantReport", lines);

            Assert.True(bad > 0);
            Assert.Contains(lines, line => line.Contains("预留非正数量"));
        }

        [Fact]
        public void LedgerInvariantReport_FlagsDerivedIndexDrift()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("InvariantIndex_" + Guid.NewGuid().ToString("N"));
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 2), env.Port, out _));
            Dictionary<ThingDef, List<SDS_StockKey>> keysByDef =
                (Dictionary<ThingDef, List<SDS_StockKey>>)CoreTestKit.GetField(env.Ledger, "keysByDef");
            keysByDef.Clear();
            // 让版本号看起来是最新的：EnsureKeysByDef 不会重建，报告必须靠独立扫描发现漂移。
            CoreTestKit.SetField(env.Ledger, "keysByDefVersion", env.Ledger.StockVersion);

            List<string> lines = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(env.Ledger,
                "AppendLedgerInvariantReport", lines);

            Assert.True(bad > 0);
            Assert.Contains(lines, line => line.Contains("keysByDef"));
        }

        // ---------- 自检 vitals 判据：量纲唯一在 SDS_StockRules（tick / 绝对HP / -1 哨兵） ----------

        [Fact]
        public void LedgerInvariantReport_AcceptsRawTickRotProgress()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("InvariantVitalsRot_" + Guid.NewGuid().ToString("N"));
            def.comps.Add(new CompProperties_Rottable());
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger,
                "TryDepositVirtual", key, 1L,
                new SDS_StockVitals { hitPoints = -1f, rotProgress = 6189.946f }));

            List<string> lines = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(env.Ledger,
                "AppendLedgerInvariantReport", lines);

            // 带 CompRottable 的 def：原始 tick 值（哪怕远超 1）是合法量纲，不得报错。
            Assert.Equal(0, bad);
        }

        [Fact]
        public void LedgerInvariantReport_FlagsRotProgressWithoutRotComp()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("InvariantVitalsNoRot_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger,
                "TryDepositVirtual", key, 1L,
                new SDS_StockVitals { hitPoints = -1f, rotProgress = 6189.946f }));

            List<string> lines = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(env.Ledger,
                "AppendLedgerInvariantReport", lines);

            // 结构不变量：无 CompRottable 的 def 只允许 -1 哨兵，带进度即非法（历史孤儿值）。
            Assert.True(bad > 0);
            Assert.Contains(lines, line => line.Contains("vitals.rotProgress 非法"));
        }

        [Fact]
        public void LedgerInvariantReport_FlagsVitalsSentinelViolations()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("InvariantVitalsSentinel_" + Guid.NewGuid().ToString("N"));
            def.comps.Add(new CompProperties_Rottable());
            def.useHitPoints = true;
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger,
                "TryDepositVirtual", key, 1L,
                new SDS_StockVitals { hitPoints = -1f, rotProgress = -1f }));
            // 入账合并会按哨兵语义吞掉负值，故仿既有用例先入账、再直改堆状态注入越界值。
            Dictionary<SDS_StockKey, SDS_StockVitals> vitalsMap =
                (Dictionary<SDS_StockKey, SDS_StockVitals>)CoreTestKit.GetField(
                    env.Ledger, "stockVitals");
            vitalsMap[key].hitPoints = -2f;
            vitalsMap[key].rotProgress = -2f;

            List<string> lines = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(env.Ledger,
                "AppendLedgerInvariantReport", lines);

            // 越过 -1 哨兵（含 NaN / 无穷同分支）：两个字段都必须报「非法」。
            Assert.True(bad > 0);
            Assert.Contains(lines, line => line.Contains("vitals.hitPoints 非法"));
            Assert.Contains(lines, line => line.Contains("vitals.rotProgress 非法"));
        }

        [Fact]
        public void LedgerInvariantReport_FlagsHitPointsAboveDefBound()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("InvariantVitalsHp_" + Guid.NewGuid().ToString("N"));
            def.useHitPoints = true;
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            // BaseMaxHitPoints / 上限判定都走 stat 查询，必须在入账前注入（生产环境由游戏加载）。
            StatDefOf.MaxHitPoints = new StatDef { defaultBaseValue = 300f };
            try
            {
                Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger,
                    "TryDepositVirtual", key, 1L,
                    new SDS_StockVitals { hitPoints = 9999f, rotProgress = -1f }));

                List<string> lines = new List<string>();
                int bad = (int)CoreTestKit.InvokeInternal(env.Ledger,
                    "AppendLedgerInvariantReport", lines);

                // 上限 = 300 × 1.8(品质上限) = 540；9999 必须报「超过 def 上限」。
                Assert.True(bad > 0);
                Assert.Contains(lines, line => line.Contains("vitals.hitPoints 超过 def 上限"));
            }
            finally
            {
                StatDefOf.MaxHitPoints = null;
            }
        }

        [Fact]
        public void LedgerInvariantReport_FlagsFutureDataVersion()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            CoreTestKit.SetField(env.Ledger, "dataVersion", 2);

            List<string> lines = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(env.Ledger,
                "AppendLedgerInvariantReport", lines);

            Assert.True(bad > 0);
            Assert.Contains(lines, line => line.Contains("存档版本"));
        }

        // ---------- 异常路径：已物化实物必须可回收，接管路径不释放预留 ----------

        [Fact]
        public void ExecuteVirtualReserved_ExceptionAfterFirstPlan_ReclaimsWithoutReleasing()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef good = CoreTestKit.Def("ReclaimGood");
            ThingDef bad = CoreTestKit.Def("ReclaimBad");
            bad.thingClass = typeof(ThrowingThing);
            SDS_StockKey goodKey = new SDS_StockKey(good, null, -1);
            SDS_StockKey badKey = new SDS_StockKey(bad, null, -1);
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryDepositVirtual", goodKey, 1L, null));
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryDepositVirtual", badKey, 1L, null));
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryReserve", 900,
                new List<SDS_StockKey> { goodKey, badKey }, new List<int> { 1, 1 }, null));

            Type txType = RulesTestKit.TransactionType;
            SDS_WithdrawPlan first = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery { key = goodKey, quantity = 1, preserved = false },
                endpoint = null,
                mode = SDS_WithdrawMode.Hand
            };
            SDS_WithdrawPlan second = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery { key = badKey, quantity = 1, preserved = false },
                endpoint = null,
                mode = SDS_WithdrawMode.Hand
            };
            object tx = txType.GetMethod("BeginJobAdopt", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[]
                {
                    // 刻意用多计划：当前生产入口只有单计划，但事务层必须自己守住异常路径。
                    900, new List<SDS_WithdrawPlan> { first, second }, null
                });
            Assert.NotNull(tx);

            object[] args = { null };
            Assert.Throws<TargetInvocationException>(
                () => txType.GetMethod("ExecuteVirtualReserved").Invoke(tx, args));

            // 第一个计划已扣账并登记；第二个计划在 MakeThing 阶段抛异常（尚未扣账）。
            Assert.Equal(0L, env.Ledger.GetCount(goodKey));
            Assert.Equal(1L, env.Ledger.GetCount(badKey));
            FieldInfo materializedField = txType.GetField("materialized",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(materializedField);
            List<Thing> materialized = (List<Thing>)materializedField.GetValue(tx);
            Assert.Single(materialized);

            txType.GetMethod("ReclaimMaterializedWithoutRelease",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(tx, null);

            Assert.Equal(1L, env.Ledger.GetCount(goodKey));
            Assert.Equal(1L, env.Ledger.GetCount(badKey));
            // 已消费的预留不回收；未执行的第二个计划预留原样留给 Job FinishAction。
            Assert.Equal(0L, env.Ledger.GetReserved(goodKey));
            Assert.Equal(1L, env.Ledger.GetReserved(badKey));
            Assert.Empty((List<Thing>)materializedField.GetValue(tx));
            SDS_Rules.ReleaseJobReservation(900);
        }

        [Fact]
        public void ExecutePreservedExact_PlanAboveReservation_IsRejected()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("ReservationSentinel");
            SDS_PreserveHooks.Register(t => t.def != null && t.def.defName == "ReservationSentinel");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));

            List<Thing> stored = new List<Thing>();
            env.Ledger.AppendPreservedThings(stored);
            Thing thing = stored[0];
            SDS_StockKey preservedKey = new SDS_StockKey(def, null, -1, true);
            Assert.True(SDS_Rules.TryReservePreservedForJob(902,
                new List<ThingCount> { new ThingCount(thing, 1) }, null));
            Assert.Equal(1L, env.Ledger.GetReserved(preservedKey));

            Type txType = RulesTestKit.TransactionType;
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery { key = preservedKey, quantity = 1, preserved = true },
                endpoint = null,
                mode = SDS_WithdrawMode.Hand
            };
            object tx = txType.GetMethod("BeginJobAdopt", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { 902, new List<SDS_WithdrawPlan> { plan }, null });
            Assert.NotNull(tx);

            // 预留只有 1，执行计划却要 2：必须在摘出真身之前拒绝。
            object[] args = { new List<ThingCount> { new ThingCount(thing, 2) }, null };
            Assert.False((bool)txType.GetMethod("ExecutePreservedExact").Invoke(tx, args));
            Assert.Equal(3L, env.Ledger.GetPreservedCount(preservedKey));
            Assert.Equal(1L, env.Ledger.GetReserved(preservedKey));
            SDS_Rules.ReleaseJobReservation(902);
        }

        private static void SetDestroyed(Thing thing)
        {
            // Thing.Destroyed 是 mapIndexOrState == -3 的派生属性；直接调 Destroy()
            // 会先把实物从 holder 里摘掉，测不到「已销毁但仍在账本容器里」的巡检分支。
            FieldInfo state = typeof(Thing).GetField("mapIndexOrState",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(state);
            state.SetValue(thing, Convert.ChangeType(-3, state.FieldType));
            Assert.True(thing.Destroyed);
        }

        private static int Units(List<Thing> things)
        {
            int units = 0;
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] != null && !things[i].Destroyed)
                {
                    units += things[i].stackCount;
                }
            }
            return units;
        }
    }

    /// <summary>
    /// 圣物探针的测试替身：每次调用都抛异常，用于验证「瞬时异常不形成永久闩锁」。
    /// 只由 CoreHardeningTests 在 Harmony 作用域内挂载，finally 里卸载。
    /// </summary>
    internal static class RelicProbePatch
    {
        public static int Calls;

        public static bool Prefix()
        {
            Calls++;
            throw new InvalidOperationException("forced relic probe failure");
        }
    }
}
