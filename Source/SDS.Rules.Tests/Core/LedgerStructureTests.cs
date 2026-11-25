using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    public class StructureReconcileTests
    {
        [Fact]
        public void Reconcile_AddsPrunesAndUpdatesFacts()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            Thing a = CoreTestKit.Make(CoreTestKit.Def("ReconA"));
            Thing b = CoreTestKit.Make(CoreTestKit.Def("ReconB"));
            List<SDS_StructureFact> live = new List<SDS_StructureFact>
            {
                Fact(a, SDS_StructureKind.Core, true)
            };

            Assert.True(SDS_Rules.ReconcileStructures(live));
            Assert.True(SDS_Rules.Available);

            // 同样的活体集不产生变化，也不推进版本。
            Assert.False(SDS_Rules.ReconcileStructures(live));

            // 断电但未走回调：对账按活体事实修正 Operational。
            live[0] = Fact(a, SDS_StructureKind.Core, false);
            Assert.True(SDS_Rules.ReconcileStructures(live));
            Assert.False(SDS_Rules.Available);

            // 换图 / 替换：同一趟补齐新核心并剔除旧核心。
            live.Clear();
            live.Add(Fact(b, SDS_StructureKind.Core, true));
            Assert.True(SDS_Rules.ReconcileStructures(live));
            Assert.True(SDS_Rules.Available);

            // 已销毁的旧项在下一轮对账中剔除。
            b.Destroy();
            Assert.True(SDS_Rules.ReconcileStructures(new List<SDS_StructureFact>()));
            Assert.False(SDS_Rules.Available);
        }

        private static SDS_StructureFact Fact(Thing thing, SDS_StructureKind kind, bool operational)
        {
            return new SDS_StructureFact
            {
                Thing = thing,
                Kind = kind,
                Operational = operational
            };
        }
    }

    public class LedgerStructureTests
    {
        [Fact]
        public void PreservedOwner_DoesNotExposeChildren()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("PreservedItem"));
            Assert.Equal(true, CoreTestKit.InvokeInternal(env.Ledger, "TryDepositPreserved", thing));

            FieldInfo field = env.Ledger.GetType().GetField("preservedOwner",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            ThingOwner<Thing> owner = (ThingOwner<Thing>)field.GetValue(env.Ledger);
            Assert.Single(owner);

            IThingHolder holder = owner.Owner;
            List<IThingHolder> children = new List<IThingHolder>();
            holder.GetChildHolders(children);
            Assert.Empty(children);
            Assert.Null(holder.ParentHolder);
            Assert.Same(owner, holder.GetDirectlyHeldThings());
            Assert.NotSame(env.Ledger, holder);
        }

        [Fact]
        public void ExposeData_UsesLockedNodesAndLookModes()
        {
            string root = SourceRoot();
            Assert.NotNull(root);
            string text = File.ReadAllText(Path.Combine(root, "SDS.Core", "SDS_Ledger.cs"));

            Assert.Contains("Scribe_Values.Look(ref dataVersion, \"dataVersion\", 1)", text);
            Assert.Contains("\"stockEntries\", LookMode.Deep", text);
            Assert.Contains("\"forbiddenKeys\", LookMode.Deep", text);
            Assert.Contains("\"reservations\", LookMode.Deep", text);
            Assert.Contains("\"preservedThings\", LookMode.Deep", text);
            // C8：sdsReclaim 与本账本节点同级，只由 Vanilla 存档壳读写；账本不得再读同节点，
            // 否则新旧档会被两个 ExposeData 各读一次，产生重复回收条目。
            Assert.DoesNotContain("\"sdsReclaim\"", text);
            string component = File.ReadAllText(
                Path.Combine(root, "SDS.Vanilla", "Foundation", "SDS_GameComponent_Grid.cs"));
            Assert.Contains("\"sdsReclaim\", LookMode.Deep", component);
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
    }
}
