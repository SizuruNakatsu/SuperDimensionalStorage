using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Vanilla 侧自检数据提供者（§10.2 第 5 步）：只产出检查结果，不直接落日志。
    /// 报告出口统一在 SDS.Diag；本类只负责 W63 / 镜像 / 金库与进入存档的 B 类报数。
    /// 行约定："[失败] ..." 失败；"[统计] ..." 统计；"[载入] ..." 进入存档报数。
    /// </summary>
    internal sealed class SDS_VanillaDiagnostics : ISDS_DiagnosticProvider
    {
        private const int MaxReported = 20;

        /// <summary>由 SDS_GameComponent_Grid 在进入存档时置位；只输出一次。</summary>
        internal static bool LoadReportRequested;

        public string Name
        {
            get { return "Vanilla"; }
        }

        public void AppendReport(List<string> lines)
        {
            if (lines == null)
            {
                return;
            }
            int checkedCount = 0;
            int failures = 0;
            int reported = 0;

            // W63 不依赖当前地图：必须在 grid 判空前跑，启动期也能暴露挂点缺失。
            AppendCellOnlyCheck(lines, ref checkedCount, ref failures, ref reported);

            SDS_GameComponent_Grid grid = SDS_GameComponent_Grid.Instance;
            if (grid == null)
            {
                lines.Add("[统计] Vanilla：检查 " + checkedCount + " 项，失败 " + failures + " 项。");
                return;
            }
            if (LoadReportRequested)
            {
                LoadReportRequested = false;
                AppendLoadReport(lines);
            }
            AppendChecks(lines, ref checkedCount, ref failures, ref reported);
        }

        private static void AppendCellOnlyCheck(List<string> lines,
            ref int checkedCount, ref int failures, ref int reported)
        {
            // 1) W63：Def 加载后的运行期枚举与落格挂点必须成功。
            checkedCount++;
            if (!SDS_CellOnlyWorkGiverPatches.Applied
                || !SDS_CellOnlyWorkGiverPatches.PlacementHookApplied
                || (SDS_CellOnlyWorkGiverPatches.JobOnThingCount <= 0
                    && SDS_CellOnlyWorkGiverPatches.HasJobOnThingCount <= 0))
            {
                failures++;
                if (reported++ < MaxReported)
                {
                    lines.Add("[失败] W63 落点守卫未挂载：Applied="
                        + SDS_CellOnlyWorkGiverPatches.Applied
                        + " PlacementHook=" + SDS_CellOnlyWorkGiverPatches.PlacementHookApplied
                        + " JobOnThing=" + SDS_CellOnlyWorkGiverPatches.JobOnThingCount
                        + " HasJobOnThing=" + SDS_CellOnlyWorkGiverPatches.HasJobOnThingCount);
                }
            }
            lines.Add("[统计] W63 落点：改写 " + SDS_CellOnlyWorkGiverPatches.W63RewriteCount
                + " / 探针 " + SDS_CellOnlyWorkGiverPatches.W63ProbeCount
                + " / 退让 Job " + SDS_CellOnlyWorkGiverPatches.W63FallbackJobCount
                + " / 探针 " + SDS_CellOnlyWorkGiverPatches.W63FallbackProbeCount);
        }

        private static void AppendChecks(List<string> lines,
            ref int checkedCount, ref int failures, ref int reported)
        {
            int jobMirrorChecks = 0;
            int jobMirrorFailures = 0;

            // 2) 镜像结构：登记过的镜像必须仍是存活、已 spawn、属于有效地图的实物。
            //    不校验数量：镜像同步有 30 tick 节流窗口。
            List<Thing> mirrors = new List<Thing>();
            SDS_MirrorRegistry.CollectAll(mirrors);
            for (int i = 0; i < mirrors.Count; i++)
            {
                Thing mirror = mirrors[i];
                checkedCount++;
                if (!SDS_MirrorService.IsLiveOn(mirror, mirror != null ? mirror.Map : null))
                {
                    failures++;
                    if (reported++ < MaxReported)
                    {
                        lines.Add("[失败] 镜像失效：def="
                            + (mirror != null && mirror.def != null ? mirror.def.defName : "?")
                            + " destroyed=" + (mirror != null && mirror.Destroyed)
                            + " spawned=" + (mirror != null && mirror.Spawned));
                    }
                }
            }

            // 3) 隐藏金库结构（W37）：每图至多 1 个；金库格不得属于 Zone / 外来 SlotGroup；锚点必须仍在本图。
            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];
                    if (map == null || map.Disposed)
                    {
                        continue;
                    }
                    AppendJobMirrorCheck(map, lines, ref jobMirrorChecks, ref jobMirrorFailures,
                        ref reported);
                    Building_SDS_Vault vault = Building_SDS_Vault.GetOn(map);
                    if (vault == null)
                    {
                        continue;
                    }
                    checkedCount++;
                    if (vault.Destroyed || !vault.Spawned || vault.Map != map)
                    {
                        failures++;
                        if (reported++ < MaxReported)
                        {
                            lines.Add("[失败] 金库失效：map=" + map.uniqueID);
                        }
                        continue;
                    }
                    SlotGroup own = vault.GetSlotGroup();
                    bool foreignGroup = own != null && map.haulDestinationManager != null
                        && map.haulDestinationManager.SlotGroupAt(vault.Position) != own;
                    if (vault.Position.GetZone(map) != null || foreignGroup)
                    {
                        failures++;
                        if (reported++ < MaxReported)
                        {
                            lines.Add("[失败] 金库格冲突：map=" + map.uniqueID
                                + " cell=" + vault.Position);
                        }
                    }
                    Thing anchor = vault.AnchorThing;
                    if (anchor != null && (anchor.Destroyed || anchor.Map != map))
                    {
                        failures++;
                        if (reported++ < MaxReported)
                        {
                            lines.Add("[失败] 金库锚点失效：map=" + map.uniqueID);
                        }
                    }
                }
            }

            checkedCount += jobMirrorChecks;
            failures += jobMirrorFailures;
            lines.Add("[统计] W60 镜像 Job 目标：检查 " + jobMirrorChecks + " 项，失败 "
                + jobMirrorFailures + " 项。");
            lines.Add("[统计] Vanilla：检查 " + checkedCount + " 项，失败 " + failures + " 项。");
        }

        /// <summary>W60：被 Job 引用的已登记镜像必须存活并已 spawn，否则说明收口漏了一次。</summary>
        private static void AppendJobMirrorCheck(Map map, List<string> lines,
            ref int jobMirrorChecks, ref int jobMirrorFailures, ref int reported)
        {
            if (map == null || map.mapPawns == null)
            {
                return;
            }
            List<Thing> refs = new List<Thing>();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                Pawn_JobTracker tracker = pawn != null ? pawn.jobs : null;
                if (tracker == null)
                {
                    continue;
                }
                CheckJobMirror(tracker.curJob, map, refs, lines, ref jobMirrorChecks,
                    ref jobMirrorFailures, ref reported);
                JobQueue queue = tracker.jobQueue;
                if (queue == null)
                {
                    continue;
                }
                for (int q = 0; q < queue.Count; q++)
                {
                    QueuedJob queued = queue[q];
                    CheckJobMirror(queued != null ? queued.job : null, map, refs, lines,
                        ref jobMirrorChecks, ref jobMirrorFailures, ref reported);
                }
            }
        }

        private static void CheckJobMirror(Job job, Map map, List<Thing> refs, List<string> lines,
            ref int jobMirrorChecks, ref int jobMirrorFailures, ref int reported)
        {
            if (job == null)
            {
                return;
            }
            refs.Clear();
            SDS_MirrorJobTargets.CollectReferencedMirrors(job, refs);
            for (int i = 0; i < refs.Count; i++)
            {
                Thing mirror = refs[i];
                jobMirrorChecks++;
                if (SDS_MirrorService.IsLiveOn(mirror, map))
                {
                    continue;
                }
                jobMirrorFailures++;
                if (reported++ < MaxReported)
                {
                    lines.Add("[失败] W60 镜像 Job 目标失效：def="
                        + (mirror.def != null ? mirror.def.defName : "?")
                        + " loadID=" + job.loadID
                        + " destroyed=" + mirror.Destroyed
                        + " spawned=" + mirror.Spawned);
                }
            }
        }

        private static void AppendLoadReport(List<string> lines)
        {
            List<Map> scanMaps = new List<Map>();
            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    Map map = Find.Maps[i];
                    if (map != null && (map.IsPlayerHome || HasPlayerSpawnedPawn(map)))
                    {
                        scanMaps.Add(map);
                    }
                }
            }

            int buildingTotal = 0;
            int buildingOk = 0;
            int colonistTotal = 0;
            int colonistOk = 0;
            int mechTotal = 0;
            int mechOk = 0;
            int mapsWithCore = 0;
            SDS_GameComponent_Grid grid = SDS_GameComponent_Grid.Instance;

            for (int m = 0; m < scanMaps.Count; m++)
            {
                Map map = scanMaps[m];
                if (map.listerThings != null)
                {
                    List<Thing> all = map.listerThings.AllThings;
                    for (int i = 0; i < all.Count; i++)
                    {
                        Thing thing = all[i];
                        if (thing == null || thing.Destroyed)
                        {
                            continue;
                        }
                        Building_SDS_Core core = thing as Building_SDS_Core;
                        if (core != null)
                        {
                            buildingTotal++;
                            if (core.Operational)
                            {
                                buildingOk++;
                            }
                            continue;
                        }
                        Building_SDS_Port port = thing as Building_SDS_Port;
                        if (port != null)
                        {
                            buildingTotal++;
                            if (port.Operational)
                            {
                                buildingOk++;
                            }
                            continue;
                        }
                        Building_SDS_CapacityArray array = thing as Building_SDS_CapacityArray;
                        if (array != null)
                        {
                            buildingTotal++;
                            if (array.Operational)
                            {
                                buildingOk++;
                            }
                        }
                    }
                }

                if (map.mapPawns != null)
                {
                    IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        Pawn pawn = pawns[i];
                        if (pawn == null || !pawn.Spawned || pawn.Faction != Faction.OfPlayer
                            || pawn.RaceProps == null)
                        {
                            continue;
                        }
                        bool ok = SDS_ImplantUtility.IsPortOperational(pawn);
                        if (pawn.RaceProps.IsMechanoid)
                        {
                            mechTotal++;
                            if (ok)
                            {
                                mechOk++;
                            }
                        }
                        else if (pawn.RaceProps.Humanlike)
                        {
                            colonistTotal++;
                            if (ok)
                            {
                                colonistOk++;
                            }
                        }
                    }
                }

                if (grid != null && grid.State != null && grid.State.HasOperationalCore(map))
                {
                    mapsWithCore++;
                }
            }

            int totalAll = buildingTotal + colonistTotal + mechTotal;
            int okAll = buildingOk + colonistOk + mechOk;
            lines.Add("[载入] 超维空间可用性：" + (SDS_Rules.Available ? "可用" : "不可用")
                + "（扫描地图 " + scanMaps.Count + " 张，可用核心 " + mapsWithCore + " 张图有核心）");
            lines.Add("[载入] 端点总计：总 " + totalAll + "，可用 " + okAll
                + "，不可用 " + (totalAll - okAll) + "（建筑 + 殖民者 + 机械体）");
            lines.Add("[载入] 建筑端点：总 " + buildingTotal + "，可用 " + buildingOk
                + "，不可用 " + (buildingTotal - buildingOk));
            lines.Add("[载入] 殖民者端点：总 " + colonistTotal + "，可用 " + colonistOk
                + "，不可用 " + (colonistTotal - colonistOk));
            lines.Add("[载入] 机械体端点：总 " + mechTotal + "，可用 " + mechOk
                + "，不可用 " + (mechTotal - mechOk));
        }

        private static bool HasPlayerSpawnedPawn(Map map)
        {
            if (map == null || map.mapPawns == null)
            {
                return false;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && pawn.Spawned && pawn.Faction == Faction.OfPlayer)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
