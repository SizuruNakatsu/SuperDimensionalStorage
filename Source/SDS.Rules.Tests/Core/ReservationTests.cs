using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>物化构造必抛：用于验证执行器异常路径必须回滚预留。</summary>
    public class ThrowingThing : Thing
    {
        public ThrowingThing()
        {
            throw new InvalidOperationException("test: ThrowingThing ctor");
        }
    }

    /// <summary>销毁必抛：用于验证「入账 → 销毁」原子块必须按实际销毁状态回退。</summary>
    public class ThrowingDestroyThing : Thing
    {
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            throw new InvalidOperationException("test: ThrowingDestroyThing.Destroy");
        }
    }

    public class ReservationTests
    {
        [Fact]
        public void ReserveByKey_IsIdempotent_AndReleasable()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("ReserveA");
            SDS_StockKey key = new SDS_StockKey(a, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 5), env.Port, out _));

            Assert.True(SDS_Rules.TryReserveForJob(101, a, 3, null, key, out _, out int reserved));
            Assert.Equal(3, reserved);
            Assert.Equal(3L, env.Ledger.GetReserved(key));

            // 读档重跑 toil：重复调用视为已预留
            Assert.True(SDS_Rules.TryReserveForJob(101, a, 3, null, key, out _, out int againReserved));
            Assert.Equal(3, againReserved);
            Assert.Equal(3L, env.Ledger.GetReserved(key));

            SDS_Rules.ReleaseJobReservation(101);
            Assert.Equal(0L, env.Ledger.GetReserved(key));
        }

        [Fact]
        public void ReserveByDef_PicksKeyAndCapsByStackSpace()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("ReserveB");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 5), env.Port, out _));

            Assert.True(SDS_Rules.TryReserveForJob(102, a, 3, null, null,
                out SDS_StockKey key, out int count));
            Assert.NotNull(key);
            Assert.Same(a, key.thingDef);
            Assert.Equal(3, count);
            Assert.Equal(3L, env.Ledger.GetReserved(key));

            // 同一 Job 再调：返回同一预留，不叠加
            Assert.True(SDS_Rules.TryReserveForJob(102, a, 3, null, null,
                out SDS_StockKey again, out int againCount));
            Assert.True(again.SameAs(key));
            Assert.Equal(3, againCount);
            Assert.Equal(3L, env.Ledger.GetReserved(key));
        }

        [Fact]
        public void AuditReservations_DropsNegativeIds()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("ReserveC");
            SDS_StockKey key = new SDS_StockKey(a, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 4), env.Port, out _));

            Assert.True(SDS_Rules.TryReserveForJob(-7, a, 2, null, key, out _, out _));
            Assert.Equal(2L, env.Ledger.GetReserved(key));

            SDS_Core.AuditReservations(null);
            Assert.Equal(0L, env.Ledger.GetReserved(key));
        }

        [Fact]
        public void AuditReservations_DropsJobReservation_WhenJobNoLongerExists()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("ReserveD");
            SDS_StockKey key = new SDS_StockKey(a, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 4), env.Port, out _));

            Assert.True(SDS_Rules.TryReserveForJob(555, a, 2, null, key, out _, out _));
            Assert.Equal(2L, env.Ledger.GetReserved(key));

            env.ReclaimWorld.JobExistsFn = id => id != 555;
            SDS_Core.AuditReservations(env.ReclaimWorld.JobExists);

            Assert.Equal(0L, env.Ledger.GetReserved(key));
            Assert.Equal(4L, SDS_Availability.GetAvailableCount(key));
        }

        [Fact]
        public void AuditReservations_KeepsJobReservation_WhenJobStillExists()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("ReserveE");
            SDS_StockKey key = new SDS_StockKey(a, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 4), env.Port, out _));

            Assert.True(SDS_Rules.TryReserveForJob(556, a, 2, null, key, out _, out _));
            env.ReclaimWorld.JobExistsFn = id => id == 556;
            SDS_Core.AuditReservations(env.ReclaimWorld.JobExists);

            Assert.Equal(2L, env.Ledger.GetReserved(key));
            Assert.Equal(2L, SDS_Availability.GetAvailableCount(key));
        }

        [Fact]
        public void WithdrawException_RollsBackReservation()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("ReserveThrow");
            def.thingClass = typeof(ThrowingThing);
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            CoreTestKit.InvokeInternal(env.Ledger, "TryDepositVirtual", key, 4L, null);
            Assert.Equal(4L, SDS_Availability.GetAvailableCount(key));

            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery { key = key, quantity = 3 },
                endpoint = null,
                mode = SDS_WithdrawMode.Hand
            };

            Assert.False(SDS_Rules.TryWithdraw(plan, null, out List<Thing> made));
            Assert.Null(made);
            // 同步事务用的是负 id 预留：异常后必须当场回滚，不能占用到下次读档。
            Assert.Equal(0L, env.Ledger.GetReserved(key));
            Assert.Equal(4L, SDS_Availability.GetAvailableCount(key));
        }

        [Fact]
        public void AuditReservations_DropsZeroIdOwner()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("ReserveZero");
            SDS_StockKey key = new SDS_StockKey(a, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 4), env.Port, out _));

            // 0 是非法归属（旧档 / 第三方写坏）：门面拒绝建，内部路径也必须被审计清掉。
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryReserve", 0,
                new List<SDS_StockKey> { key }, new List<int> { 2 }, null));
            Assert.Equal(2L, env.Ledger.GetReserved(key));

            env.ReclaimWorld.JobExistsFn = _ => true;
            SDS_Core.AuditReservations(env.ReclaimWorld.JobExists);

            Assert.Equal(0L, env.Ledger.GetReserved(key));
            Assert.Equal(4L, SDS_Availability.GetAvailableCount(key));
        }

        [Fact]
        public void ReservePreservedPlan_RejectsPlanCountAboveStack()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("ReserveOverStack");
            SDS_PreserveHooks.Register(t => t.def != null && t.def.defName == "ReserveOverStack");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 3), env.Port, out _));
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 2), env.Port, out _));

            List<Thing> preserved = new List<Thing>();
            env.Ledger.AppendPreservedThings(preserved);
            Assert.Equal(2, preserved.Count);
            SDS_StockKey key = new SDS_StockKey(a, null, -1, true);

            // ThingCount 构造时会按当时件数夹取；先建计划、再把真身缩小，模拟「计划过期」。
            Thing first = preserved[0];
            List<ThingCount> plan = new List<ThingCount> { new ThingCount(first, first.stackCount) };
            first.stackCount = 1;

            // 总量够但单件已不足：建预留必须失败，不能留下一个取不出来的预留。
            Assert.False(SDS_Rules.TryReservePreservedForJob(88, plan, null));
            Assert.Equal(0L, env.Ledger.GetReserved(key));
        }

        [Fact]
        public void ReservePreservedPlan_MergesSameOwnerAndKey()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("ReserveMerge");
            SDS_PreserveHooks.Register(t => t.def != null && t.def.defName == "ReserveMerge");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 2), env.Port, out _));
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 2), env.Port, out _));

            List<Thing> preserved = new List<Thing>();
            env.Ledger.AppendPreservedThings(preserved);
            Assert.Equal(2, preserved.Count);
            SDS_StockKey key = new SDS_StockKey(a, null, -1, true);

            // 绕过门面的 HasJobReservation 守卫，直接验证账本只有一条写入口、同 id 同 key 必须合并。
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryReservePreservedPlan",
                31, null, new List<ThingCount> { new ThingCount(preserved[0], 2) }));
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryReservePreservedPlan",
                31, null, new List<ThingCount> { new ThingCount(preserved[1], 2) }));

            Assert.Equal(4L, env.Ledger.GetReserved(key));
            FieldInfo field = env.Ledger.GetType().GetField("reservations",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            List<SDS_Reservation> entries = (List<SDS_Reservation>)field.GetValue(env.Ledger);
            Assert.Single(entries);
            Assert.Equal(4, entries[0].count);
        }
    }
}
