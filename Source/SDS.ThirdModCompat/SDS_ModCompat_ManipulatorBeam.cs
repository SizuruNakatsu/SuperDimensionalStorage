using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SuperDimensionalStorage
{

    // ============================================================================
    // 来源：牵引光束搬运器Manipulator Beam Emitter
    // ============================================================================

    /// <summary>
    /// 与「牵引光束搬运器Manipulator Beam Emitter」（packageId = natsuki.manipulatorbeam）的兼容。
    ///
    /// 为什么需要：它的目的地只有两条通道——
    ///  1. `SlotGroup`（`AllGroupsListInPriorityOrder`，落格）；
    ///  2. 一张**封闭的特殊容器白名单**（发酵桶 / 燃料 / 炮弹 / CE 弹药 / 次核扫描仪 / 培育舱 /
    ///     生物塑形舱 / 基因库 / 书架 / 床）。
    /// 它既不遍历原版「非格位容器」（`AllHaulDestinationsListInPriorityOrder`），也不问通用
    /// `IHaulDestination`；而本 Mod 端点不是 `SlotGroup` → 对牵引光束完全不可见。
    ///
    /// 本适配器只补「目的地发现」这一处缺口（《设计原则》准则三：规则只写一处）：
    ///  - 让端点通过它的容器类型闸门，并进入它的特殊容器扫描表；
    ///  - 用它的 `BeamTransfer` 形状描述「某件地面物 → 端点」：候选集用原版 `ListerHaulables`，
    ///    收不收问端点的 `IHaulDestination.Accepts`，「该由哪个端点收」问 `SDS_API.TryFindDepositEndpoint`；
    ///  - **投递不接管**：它自己的通用分支（`TryGetInnerInteractableThingOwner` +
    ///    `IHaulDestination.Accepts` + `ThingOwner.TryAdd`）就是原版容器契约，
    ///    本 Mod 端点的 `InputBuffer` 正是为此实现，货物照常「缓冲 → 数字化入账」。
    ///
    /// 有意不做（与牵引光束自己的行为对齐）：不从超维库存「卸载」回地面——
    /// 它对书架 / 基因库等容器同样只做存入，卸载只存在于发酵桶 / 生物铁收割机这类产出型建筑。
    ///
    /// 关闭方式：跟随它自己的「特殊容器」总开关（任一子开关开启即生效）；没有另设 SDS 专属开关。
    ///
    /// 未安装时整块跳过；结构变化时只记挂点缺失，不影响本体。
    ///
    /// 2026-11-08 对齐 1.2.0：`TryFindSpecialContainerTransfer` 由 4 参变为
    /// 5 参（新增 `IBeamOperator op`、`HashSet&lt;IntVec3&gt; excludedDestinations`），
    /// 其余两个挂点（类型闸门 / 容器扫描表）签名不变。
    /// </summary>
    internal static class SDS_ModCompat_ManipulatorBeam
    {
        private const string PackageId = "natsuki.manipulatorbeam";
        private const string SettingsTypeName = "ManipulatorBeam.BeamFeatureSettings";
        private const string ScanCacheTypeName = "ManipulatorBeam.MB_BeamScanCacheMapComponent";
        private const string TransferUtilTypeName = "ManipulatorBeam.BeamSpecialTransferUtility";
        private const string TransferTypeName = "ManipulatorBeam.BeamTransfer";

        /// <summary>
        /// `out BeamTransfer transfer` 在原方法参数表中的下标。
        /// 1.2.0 的签名为 `(IBeamOperator op, Thing containerThing, Predicate&lt;Thing&gt; haulValidator,
        /// HashSet&lt;IntVec3&gt; excludedDestinations, out BeamTransfer transfer)`。
        /// </summary>
        private const int TransferArgumentIndex = 4;

        /// <summary>`BeamTransfer(Thing, IntVec3, Thing, int)`（目的容器重载）；探测到目标 Mod 后才填充。</summary>
        private static ConstructorInfo transferCtor;

        public static void TryApply(Harmony harmony)
        {
            bool found = false;
            try
            {
                if (harmony == null)
                {
                    return;
                }

                Type settings = AccessTools.TypeByName(SettingsTypeName);
                Type scanCache = AccessTools.TypeByName(ScanCacheTypeName);
                Type utility = AccessTools.TypeByName(TransferUtilTypeName);
                Type transfer = AccessTools.TypeByName(TransferTypeName);
                found = settings != null || scanCache != null || utility != null;
                if (!found)
                {
                    SDS_CompatReportHub.Register("牵引光束搬运器Manipulator Beam Emitter", PackageId, false, null);
                    return;
                }

                SDS_CompatPatchReport report = new SDS_CompatPatchReport("牵引光束搬运器Manipulator Beam Emitter");

                transferCtor = transfer != null
                    ? AccessTools.Constructor(transfer,
                        new[] { typeof(Thing), typeof(IntVec3), typeof(Thing), typeof(int) })
                    : null;
                if (transferCtor == null)
                {
                    report.Missing("BeamTransfer(Thing, IntVec3, Thing, int)", "构造函数改名或结构变化");
                }

                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_ManipulatorBeam),
                    "BeamFeatureSettings.IsSpecialContainerTypeEnabled（容器类型闸门）",
                    SDS_CompatTools.Method(settings, "IsSpecialContainerTypeEnabled", typeof(Thing)),
                    null, nameof(IsSpecialContainerTypeEnabledPostfix),
                    "找不到类型 " + SettingsTypeName + " 或方法 IsSpecialContainerTypeEnabled(Thing)");

                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_ManipulatorBeam),
                    "MB_BeamScanCacheMapComponent.SpecialContainers（容器扫描表）",
                    SDS_CompatTools.PropertyGetter(scanCache, "SpecialContainers"),
                    null, nameof(SpecialContainersPostfix),
                    "找不到类型 " + ScanCacheTypeName + " 或属性 SpecialContainers");

                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_ManipulatorBeam),
                    "BeamSpecialTransferUtility.TryFindSpecialContainerTransfer（目的地 → 货源）",
                    FindTransferMethod(utility),
                    nameof(TryFindSpecialContainerTransferPrefix), null,
                    "找不到类型 " + TransferUtilTypeName
                        + "，或方法 TryFindSpecialContainerTransfer 的签名不是"
                        + " (op, containerThing, haulValidator, excludedDestinations, out transfer)");

                SDS_CompatReportHub.Register("牵引光束搬运器Manipulator Beam Emitter", PackageId, true, report);
            }
            catch (Exception e)
            {
                SDS_CompatReportHub.Register("牵引光束搬运器Manipulator Beam Emitter", PackageId, found, null,
                    null, "应用异常 " + e.GetType().Name);
                SDS_Diag.ErrorOnce("牵引光束搬运器Manipulator Beam Emitter 兼容应用失败（已跳过，本体行为不受影响）", e);
            }
        }

        /// <summary>
        /// 容器类型闸门：牵引光束只认自己的封闭白名单，这里把「本 Mod 端点」补成它认识的一种容器类型。
        /// 只放宽类型判定，不改变它对具体开关 / 排他占用 / 入队的任何判定。
        /// </summary>
        private static void IsSpecialContainerTypeEnabledPostfix(Thing containerThing, ref bool __result)
        {
            try
            {
                if (!__result && IsEndpointThing(containerThing))
                {
                    __result = true;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("牵引光束搬运器Manipulator Beam Emitter 兼容（容器类型闸门）失败（已忽略）", e);
            }
        }

        /// <summary>
        /// 容器扫描表：把本图的端点追加进它的特殊容器列表。
        /// 幂等（已在表里就跳过）；列表本身由它每 30 tick 重建，追加项随重建自然失效后重新补上。
        /// </summary>
        private static void SpecialContainersPostfix(object __instance, List<Thing> __result)
        {
            try
            {
                if (__result == null || !(__instance is MapComponent component) || component.map == null)
                {
                    return;
                }

                List<Building> buildings = component.map.listerBuildings.allBuildingsColonist;
                for (int i = 0; i < buildings.Count; i++)
                {
                    Building building = buildings[i];
                    if (IsEndpointThing(building) && !__result.Contains(building))
                    {
                        __result.Add(building);
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("牵引光束搬运器Manipulator Beam Emitter 兼容（容器扫描表）失败（已忽略）", e);
            }
        }

        /// <summary>
        /// 结构校验：只接受 1.2.0 的 5 参形态，且第 5 参是 `out transfer`。
        /// 版本再次漂移时按「挂点缺失」上报，绝不带着错误的参数下标去写参数表。
        /// </summary>
        private static MethodInfo FindTransferMethod(Type utility)
        {
            MethodInfo method = SDS_CompatTools.Method(utility, "TryFindSpecialContainerTransfer");
            if (method == null)
            {
                return null;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length <= TransferArgumentIndex
                || parameters[1].ParameterType != typeof(Thing)
                || parameters[2].ParameterType != typeof(Predicate<Thing>)
                || parameters[TransferArgumentIndex].Name != "transfer"
                || !parameters[TransferArgumentIndex].ParameterType.IsByRef)
            {
                return null;
            }

            return method;
        }

        /// <summary>
        /// 目的地 → 货源：牵引光束的容器分支没有「通用容器」入口，本 Mod 端点在这里给出 transfer。
        ///
        /// 只做形状翻译：候选集合用它自己的口径（原版 `ListerHaulables` 的待搬运地面物），
        /// 「这个端点收不收这件实物」由端点自己的 `IHaulDestination.Accepts` 回答（原版契约），
        /// 「该由哪个端点收」问 SDS 的「找」门面——适配器不复制筛选 / 容量 / 优先级 / 排序规则。
        /// 不是本 Mod 端点时返回 true，原方法原样执行。
        ///
        /// 本 prefix 一律跳过原方法，因此**必须同时写 `__result` 与 `out transfer`**：
        /// 跳过原方法时返回值取 `__result` 的默认值（`false`），只写 transfer 会被调用方当成“没有活”。
        ///
        /// `out BeamTransfer` 在编译期不可见（本程序集不引用 ManipulatorBeam.dll），
        /// 而 Harmony 对 by-ref 实参按类型校验，`ref object` 会被拒绝应用；
        /// 因此按 Harmony 的 `__args` 约定写下标 <see cref="TransferArgumentIndex"/>，
        /// 由 Harmony 在 prefix 返回后写回真实 out 实参。
        /// </summary>
        private static bool TryFindSpecialContainerTransferPrefix(Thing containerThing,
            Predicate<Thing> haulValidator, object[] __args, ref bool __result)
        {
            if (!IsEndpointThing(containerThing))
            {
                return true;
            }

            __result = false;
            WriteTransferArgument(__args, null);
            try
            {
                if (transferCtor == null)
                {
                    return false;
                }

                Map map = containerThing.Map;
                if (map == null || containerThing.Destroyed || !containerThing.Spawned
                    || containerThing.Position.Fogged(map) || haulValidator == null
                    || !(containerThing is IHaulDestination destination))
                {
                    return false;
                }

                Thing source = FindDepositableThing(map, destination, haulValidator);
                if (source != null)
                {
                    object transfer = transferCtor.Invoke(new object[]
                    {
                        source, source.Position, containerThing, source.stackCount
                    });
                    WriteTransferArgument(__args, transfer);
                    __result = transfer != null;
                }
            }
            catch (Exception e)
            {
                WriteTransferArgument(__args, null);
                __result = false;
                SDS_Diag.ErrorOnce("牵引光束搬运器Manipulator Beam Emitter 兼容（端点选源）失败（已忽略）", e);
            }

            // 端点一律由本适配器决断，不再落到它的白名单分支。
            return false;
        }

        /// <summary>只读：该实物是否是本 Mod 的可用端点（只问 SDS_API）。</summary>
        private static bool IsEndpointThing(Thing thing)
        {
            return SDS_API.IsDepositEndpoint(thing);
        }

        /// <summary>把 transfer 写回原方法的第 5 个参数（Harmony 会在 prefix 返回后同步到真实 out 实参）。</summary>
        private static void WriteTransferArgument(object[] args, object transfer)
        {
            if (args != null && args.Length > TransferArgumentIndex)
            {
                args[TransferArgumentIndex] = transfer;
            }
        }

        /// <summary>
        /// 在「待搬运的地面物」里挑一件本端点愿意收的实物。
        ///  - 候选集合与牵引光束自己的口径一致（原版 `ListerHaulables`）；
        ///  - 收不收只问端点自己的 `IHaulDestination.Accepts`（原版契约，也是本 Mod 的存入硬门：
        ///    全局可用 / 端点可用 / 筛选 / 品类容量 / 来源排除标记都在里面）；
        ///  - **不**再问「SDS 全局会选哪个端点」：牵引光束已经决定了目的地容器，
        ///    再拿全局就近结果去比对本端点，只要附近有更近的芯片小人或别的端点，
        ///    所有建筑端点都会同时返回"没有活"（实测表现就是完全不识别端点）。
        ///    适配器仍不复制任何筛选 / 容量规则，也不改对方开关 / 占用 / 入队判定。
        /// </summary>
        private static Thing FindDepositableThing(Map map, IHaulDestination destination,
            Predicate<Thing> haulValidator)
        {
            ICollection<Thing> haulables = map.listerHaulables.ThingsPotentiallyNeedingHauling();
            if (haulables == null)
            {
                return null;
            }
            foreach (Thing thing in haulables)
            {
                if (thing == null || thing.Destroyed || !thing.Spawned || thing.stackCount <= 0)
                {
                    continue;
                }
                if (!haulValidator(thing) || !destination.Accepts(thing))
                {
                    continue;
                }
                return thing;
            }
            return null;
        }
    }

}
