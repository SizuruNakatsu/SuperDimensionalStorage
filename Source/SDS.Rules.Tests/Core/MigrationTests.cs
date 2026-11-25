using System.Collections.Generic;
using RimWorld;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 回归（2026-11-12，W34 合同变更）：保真武器 / 衣物在玩家确认开关后的一次性迁移。
    /// 只覆盖 Core 判定与账本入账；设置页确认与"只转一次"的 UI 流程属游戏内验证。
    /// </summary>
    public class MigrationTests
    {
        private static ThingDef GearDef(string name)
        {
            ThingDef def = CoreTestKit.Def(name);
            // CompArt 让它在开关关闭时按 def 级状态走保真；开关开启后 W33 忽略它。
            def.comps.Add(new CompProperties_Art());
            // tools 非空 → ThingDef.IsWeapon 为真（verbs 在本 repack 是私有字段）。
            def.tools = new List<Tool> { new Tool() };
            return def;
        }

        private static Thing DepositPreserved(RulesTestEnv env, ThingDef def, int count = 1)
        {
            Thing thing = CoreTestKit.Make(def, count);
            Assert.Equal(true, CoreTestKit.InvokeInternal(env.Ledger, "TryDepositPreserved", thing));
            return thing;
        }

        [Fact]
        public void Convert_RequiresSwitch_ThenMovesGearToVirtual()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            ThingDef def = GearDef("MigrationGear");
            Thing thing = DepositPreserved(env, def);
            SDS_StockKey preservedKey = new SDS_StockKey(def, null, -1, true);
            SDS_StockKey virtualKey = new SDS_StockKey(def, null, -1);

            Assert.Equal(1L, env.Ledger.GetPreservedCount(preservedKey));
            Assert.Equal(0L, env.Ledger.GetCount(virtualKey));

            // 开关关闭：不转换（W34 只在玩家确认后执行）。
            Assert.Equal(0, SDS_Rules.ConvertPreservedGearToVirtual());
            Assert.Equal(1L, env.Ledger.GetPreservedCount(preservedKey));
            Assert.Equal(0L, env.Ledger.GetCount(virtualKey));

            SDS_Config.Current.forceWeaponApparelVirtual = true;
            Assert.Equal(1, SDS_Rules.ConvertPreservedGearToVirtual());
            Assert.Equal(0L, env.Ledger.GetPreservedCount(preservedKey));
            Assert.Equal(1L, env.Ledger.GetCount(virtualKey));
            Assert.True(thing.Destroyed);
        }

        [Fact]
        public void Convert_SkipsGroupWithActiveReservation()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            ThingDef def = GearDef("MigrationReserved");
            DepositPreserved(env, def);
            SDS_Config.Current.forceWeaponApparelVirtual = true;

            List<Thing> stored = new List<Thing>();
            env.Ledger.AppendPreservedThings(stored);
            List<ThingCount> plan = new List<ThingCount> { new ThingCount(stored[0], 1) };
            Assert.True(SDS_Rules.TryReservePreservedForJob(101, plan, null));

            Assert.Equal(0, SDS_Rules.ConvertPreservedGearToVirtual());
            Assert.Equal(1L, env.Ledger.GetPreservedCount(new SDS_StockKey(def, null, -1, true)));

            SDS_Rules.ReleaseJobReservation(101);
            Assert.Equal(1, SDS_Rules.ConvertPreservedGearToVirtual());
            Assert.Equal(0L, env.Ledger.GetPreservedCount(new SDS_StockKey(def, null, -1, true)));
        }

        [Fact]
        public void Convert_SkipsGearThatStillRequiresPreservedStorage()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            ThingDef def = GearDef("MigrationProtected");
            Thing thing = DepositPreserved(env, def);
            thing.questTags = new List<string> { "TestQuest" };
            SDS_Config.Current.forceWeaponApparelVirtual = true;

            Assert.Equal(0, SDS_Rules.ConvertPreservedGearToVirtual());
            Assert.Equal(1L, env.Ledger.GetPreservedCount(new SDS_StockKey(def, null, -1, true)));
        }

        [Fact]
        public void Convert_SkipsNonGear()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            ThingDef def = CoreTestKit.Def("MigrationPlain");
            SDS_PreserveHooks.Register(other => other.def != null && other.def.defName == "MigrationPlain");
            DepositPreserved(env, def);
            SDS_Config.Current.forceWeaponApparelVirtual = true;

            Assert.Equal(0, SDS_Rules.ConvertPreservedGearToVirtual());
            Assert.Equal(1L, env.Ledger.GetPreservedCount(new SDS_StockKey(def, null, -1, true)));
        }
    }
}
