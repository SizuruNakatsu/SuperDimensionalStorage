using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 账本测试环境（P5 起 Core 测试只依赖账本）：只创建并附加账本，不接宿主、不碰规则模块。
    /// 需要存入 / 可用量 / 结构 / 端口等判定层能力的测试在 SDS.Rules.Tests 的 RulesTestEnv。
    /// </summary>
    internal sealed class CoreTestEnv
    {
        public ISDS_LedgerHost Ledger;

        public static CoreTestEnv Create(int capacity = 30, int perArray = 30,
            bool separateQuality = true, bool separateStuff = true)
        {
            SDS_Config.Current = new SDS_Settings
            {
                gridCapacity = capacity,
                capacityPerArray = perArray,
                separateByQuality = separateQuality,
                separateByStuff = separateStuff
            };

            CoreTestEnv env = new CoreTestEnv();
            env.Ledger = SDS_Core.CreateLedger();
            SDS_Core.Attach(env.Ledger);
            return env;
        }
    }

    internal static class CoreTestKit
    {
        /// <summary>最近一次 RoundTrip 的 XML，供断言失败时定位 Save/Load 内容。</summary>
        public static string LastSavedXml;

        public static ThingDef Def(string name, int stackLimit = 75)
        {
            EnsureGameStub();
            ThingDef def = new ThingDef();
            def.defName = name;
            def.thingClass = typeof(Thing);
            def.category = ThingCategory.Item;
            def.destroyable = true;
            def.stackLimit = stackLimit;
            def.alwaysHaulable = true;
            def.designateHaulable = true;
            def.useHitPoints = false;
            def.thingCategories = new List<ThingCategoryDef>();
            def.statBases = new List<StatModifier>();
            def.comps = new List<CompProperties>();
            return def;
        }

        /// <summary>注册到 DefDatabase，供 Scribe 按 defName 解析；仅 Scribe 回合测试使用。</summary>
        public static ThingDef RegisteredDef(string name, int stackLimit = 75)
        {
            ThingDef def = Def(name, stackLimit);
            DefDatabase<ThingDef>.Add(def);
            return def;
        }

        public static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, name);
            }
            return field.GetValue(target);
        }

        public static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, name);
            }
            field.SetValue(target, value);
        }

        /// <summary>
        /// 真实 Scribe 往返：保存到临时 XML，再读入 target 并执行 PostLoadInit。
        /// 入：source / target —— 两个账本实例（internal 类型，按 ExposeData 反射调用）。
        /// 出：无；异常直接抛出，临时文件在 finally 删除。
        /// </summary>
        public static void RoundTrip(object source, object target, Pawn pawnReference = null)
        {
            EnsureGameStub();
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "sds-ledger-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Scribe.mode = LoadSaveMode.Inactive;
                Scribe.saver = new ScribeSaver();
                Scribe.saver.InitSaving(path, "savegame");
                Scribe.saver.EnterNode("ledger");
                InvokeInternal(source, "ExposeData");
                // FinalizeSaving 自己 ExitNode；不要提前退出，否则节点层级会多退一层。
                Scribe.saver.FinalizeSaving();
                LastSavedXml = System.IO.File.ReadAllText(path);

                Scribe.mode = LoadSaveMode.Inactive;
                Scribe.loader = new ScribeLoader();
                Scribe.loader.InitLoading(path);
                Scribe.loader.EnterNode("ledger");
                InvokeInternal(target, "ExposeData");
                // 预置的 Pawn 不在任何地图/容器里，手工放进 LoadedObjectDirectory；
                // 随后交给原版 ResolveAllCrossReferences 回填，保证路径键（curParent +
                // curPathRelToParent）与 LoadingVars 阶段登记时完全一致，不手写 "/pawn" 之类的拼接。
                object crossRefs = null;
                if (pawnReference != null)
                {
                    crossRefs = GetField(Scribe.loader, "crossRefs");
                    object directory = GetField(crossRefs, "loadedObjectDirectory");
                    MethodInfo registerLoaded = directory.GetType().GetMethod("RegisterLoaded",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    registerLoaded.Invoke(directory, new object[] { pawnReference });
                }
                // 不调用 ScribeLoader.FinalizeLoading：它会走 DeepProfiler → Prefs.LogVerbose，
                // 而测试进程没有 Unity/Prefs 初始化。账本自身没有跨引用，手工退节点后
                // 直接进入 PostLoadInit 即可；Thing 的基本字段在 LoadingVars 已读完。
                Scribe.loader.ExitNode();
                if (crossRefs != null)
                {
                    MethodInfo resolveAll = crossRefs.GetType().GetMethod("ResolveAllCrossReferences",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    resolveAll.Invoke(crossRefs, null);
                }
                Scribe.mode = LoadSaveMode.PostLoadInit;
                InvokeInternal(target, "ExposeData");
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                Scribe.saver = null;
                Scribe.loader = null;
                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }
                }
                catch (System.IO.IOException)
                {
                }
            }
        }

        public static Thing Make(ThingDef def, int count = 1)
        {
            EnsureGameStub();
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            return thing;
        }

        private static bool gameStubbed;

        /// <summary>
        /// 非游戏进程里 ThingMaker 只缺 UniqueIDsManager；用一个未初始化的 Game 壳补上，
        /// 避免调用 Game 构造函数触发完整的组件装载。
        /// </summary>
        public static void EnsureGameStub()
        {
            if (gameStubbed)
            {
                return;
            }
            gameStubbed = true;
            if (Current.Game != null)
            {
                return;
            }
            Game game = (Game)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(Game));
            FieldInfo ids = typeof(Game).GetField("uniqueIDsManager",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (ids != null)
            {
                ids.SetValue(game, new UniqueIDsManager());
            }
            FieldInfo maps = typeof(Game).GetField("maps",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (maps != null)
            {
                maps.SetValue(game, new List<Map>());
            }
            Current.Game = game;
        }

        public static object InvokeInternal(object target, string method, params object[] args)
        {
            MethodInfo info = target.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null)
            {
                throw new MissingMethodException(target.GetType().FullName, method);
            }
            return info.Invoke(target, args);
        }

        public static object StaticInternal(Type type, string method, params object[] args)
        {
            MethodInfo info = type.GetMethod(method,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null)
            {
                throw new MissingMethodException(type.FullName, method);
            }
            return info.Invoke(null, args);
        }

        public static void DebitVirtual(ISDS_LedgerHost ledger, SDS_StockKey key, int count)
        {
            InvokeInternal(ledger, "TryDebitVirtual", key, count);
        }
    }
}
