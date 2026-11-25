using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 端点统一契约（§6.4）：核心 / 锚点原版容器入口。
    /// 端点绝不能实现 ISlotGroupParent —— 一旦实现，原版 haul 的容器分支永不成立。
    /// </summary>
    internal interface ISDS_StorageEndpoint : IHaulDestination, IStoreSettingsParent, IThingHolder, IHaulEnroute
    {
        /// <summary>端点当前是否可用（通电 / 未损坏 / 已激活）。</summary>
        bool Operational { get; }

        /// <summary>存入缓冲：搬运工送进来的实物先放这里，由 Tick 数字化进账本。</summary>
        ThingOwner InputBuffer { get; }
    }

    /// <summary>
    /// 建筑可用性 / 优先级镜像的唯一实现。
    /// 耗电设置读取 SDS_Config.Current（0 = 不耗电，不接电网也算可用）。
    /// </summary>
    internal static class SDS_BuildingUtility
    {
        internal static float CoreWatts
        {
            get
            {
                return SDS_Config.Current != null ? SDS_Config.Current.corePower : SDS_Config.CorePowerDefault;
            }
        }

        internal static float ArrayWatts
        {
            get
            {
                return SDS_Config.Current != null ? SDS_Config.Current.arrayPower : SDS_Config.ArrayPowerDefault;
            }
        }

        internal static float PortWatts
        {
            get
            {
                return SDS_Config.Current != null ? SDS_Config.Current.portPower : SDS_Config.PortPowerDefault;
            }
        }

        /// <summary>已 spawn、未损坏、属于玩家；耗电配置 &gt; 0 时必须已通电。</summary>
        internal static bool IsOperational(Building building, float requiredWatts,
            CompPowerTrader power, CompBreakdownable breakdown)
        {
            if (building == null || !building.Spawned || building.Destroyed || building.Map == null)
            {
                return false;
            }
            if (building.Faction != Faction.OfPlayer)
            {
                return false;
            }
            if (building.HitPoints <= 0)
            {
                return false;
            }
            if (breakdown != null && breakdown.BrokenDown)
            {
                return false;
            }
            if (requiredWatts > 0f)
            {
                if (power == null)
                {
                    power = building.GetComp<CompPowerTrader>();
                }
                if (power != null && !power.PowerOn)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 把存档级优先级写进每座端点的原版 StorageSettings（只作为原版搜索的镜像），
        /// 并让各图 HaulDestinationManager 重排一次。返回是否真的改写了任一建筑。
        /// </summary>
        internal static bool ApplyStoragePriorityToEndpoints()
        {
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return false;
            }
            bool changed = false;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null || map.Disposed || map.listerThings == null)
                {
                    continue;
                }
                List<Thing> all = map.listerThings.AllThings;
                for (int i = 0; i < all.Count; i++)
                {
                    Building_SDS_Core core = all[i] as Building_SDS_Core;
                    if (core != null)
                    {
                        changed |= core.SyncStoragePriority();
                        continue;
                    }
                    Building_SDS_Port port = all[i] as Building_SDS_Port;
                    if (port != null)
                    {
                        changed |= port.SyncStoragePriority();
                    }
                }
            }
            if (!changed)
            {
                return false;
            }
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map != null && map.haulDestinationManager != null)
                {
                    map.haulDestinationManager.Notify_HaulDestinationChangedPriority();
                }
            }
            return true;
        }
    }

    /// <summary>
    /// 超维空间核心：可用性开关 + 端点 + 唯一能制造植入体芯片的工作台。
    /// 基类必须是 Building_WorkTable（ITab_Bills 对其硬转型）。
    /// </summary>
    public class Building_SDS_Core : Building_WorkTable, ISDS_StorageEndpoint, IBillGiver
    {
        // 核心是全局可用性开关（G）：断电必须尽快反映到 SDS_Rules.Available，
        // 否则镜像仍可选、Job 会被派出去后取货失败（started 10 jobs 红字）。
        // 10 tick ≈ 0.17s（正常速度），足够快；核心数量通常为 1，Operational 只读
        // CompPowerTrader / CompBreakdownable，轮询代价可忽略。
        private const int OperationalPollIntervalTicks = 10;

        private int nextOperationalPollTick;
        private bool lastOperational;
        private CompPowerTrader cachedPowerComp;
        private CompBreakdownable cachedBreakdownComp;
        private SDS_EndpointBuffer inputBuffer;
        private StorageSettings settings;
        private SDS_EndpointHandle handle;
        private int syncedFilterVersion = -1;

        public Building_SDS_Core()
        {
            inputBuffer = new SDS_EndpointBuffer(this);
        }

        private SDS_EndpointHandle Handle
        {
            get { return handle ?? (handle = SDS_EndpointHandle.ForBuilding(this)); }
        }

        public bool Operational
        {
            get
            {
                return SDS_BuildingUtility.IsOperational(this, SDS_BuildingUtility.CoreWatts,
                    cachedPowerComp, cachedBreakdownComp);
            }
        }

        // ---------- IBillGiver：清单可用性跟本 Mod 的 Operational 同口径 ----------

        bool IBillGiver.CurrentlyUsableForBills()
        {
            return Operational;
        }

        bool IBillGiver.UsableForBillsAfterFueling()
        {
            return Operational;
        }

        // ---------- ISDS_StorageEndpoint ----------

        public ThingOwner InputBuffer
        {
            get { return inputBuffer; }
        }

        public bool HaulDestinationEnabled
        {
            get { return SDS_EndpointUtility.CanReceive(Handle); }
        }

        public bool Accepts(Thing thing)
        {
            return SDS_EndpointUtility.Accepts(Handle, thing, false);
        }

        public int SpaceRemainingFor(ThingDef stuff)
        {
            return SDS_EndpointUtility.SpaceRemainingFor(Handle);
        }

        // ---------- IStoreSettingsParent：只为让原版搬运 / 优先级搜索成立 ----------

        public bool StorageTabVisible
        {
            get { return false; }
        }

        public StorageSettings GetStoreSettings()
        {
            EnsureSettings();
            // 只读：这里绝不能写 settings.Priority（setter → 重排 → getter 会无限递归）。
            SyncGlobalFilter();
            return settings;
        }

        public StorageSettings GetParentStoreSettings()
        {
            return null;
        }

        public void Notify_SettingsChanged()
        {
        }

        /// <summary>把存档级优先级写进本建筑；返回 true 表示真的改写（调用方据此重排）。</summary>
        internal bool SyncStoragePriority()
        {
            EnsureSettings();
            StoragePriority target = SDS_VanillaState.CurrentStoragePriority;
            if (settings.Priority == target)
            {
                return false;
            }
            settings.Priority = target;
            return true;
        }

        private void EnsureSettings()
        {
            if (settings == null)
            {
                settings = new StorageSettings(this);
            }
        }

        /// <summary>把全局唯一一套存入筛选同步进本建筑的存储设置副本（存档安全）。</summary>
        private void SyncGlobalFilter()
        {
            SDS_GameComponent_Grid grid = SDS_GameComponent_Grid.Instance;
            SDS_VanillaState state = grid != null ? grid.State : null;
            if (state == null || settings == null || syncedFilterVersion == state.FilterVersion)
            {
                return;
            }
            syncedFilterVersion = state.FilterVersion;
            settings.filter.CopyAllowancesFrom(state.DepositFilter);
        }

        // ---------- IThingHolder：存入缓冲 ----------

        public ThingOwner GetDirectlyHeldThings()
        {
            return inputBuffer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            if (inputBuffer != null)
            {
                ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, inputBuffer);
            }
            // B-PA：只读投影，让"以本端点做子树查询"的调用方也能看到库存。
            if (Operational)
            {
                SDS_ProjectionHolder projection = SDS_ProjectionService.ProjectionFor(this);
                if (projection != null)
                {
                    outChildren.Add(projection);
                }
            }
        }

        // ---------- 生命周期 ----------

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            // 读档路径下 billStack 可能尚未建好，Building_WorkTable.SpawnSetup 会直接空引用。
            if (billStack == null)
            {
                billStack = new BillStack(this);
            }
            base.SpawnSetup(map, respawningAfterLoad);
            cachedPowerComp = GetComp<CompPowerTrader>();
            cachedBreakdownComp = GetComp<CompBreakdownable>();
            if (inputBuffer == null)
            {
                inputBuffer = new SDS_EndpointBuffer(this);
            }
            RegisterFact();
            SyncStoragePriority();
            lastOperational = Operational;
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            SDS_Rules.UnregisterStructure(this);
            base.DeSpawn(mode);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            SDS_Rules.UnregisterStructure(this);
            // 拆除不吞物品：缓冲里来不及数字化的实物先入账，仍剩的退回地图。
            if (inputBuffer != null && inputBuffer.Count > 0)
            {
                SDS_EndpointUtility.DrainInputBufferSafely(this, inputBuffer);
                inputBuffer.ClearAndDestroyContents();
            }
            base.Destroy(mode);
        }

        protected override void Tick()
        {
            base.Tick();
            // 插入时已即时数字化；这里只兜底读档内容与绕过 TryAdd 的路径。
            SDS_EndpointUtility.FlushInputBuffer(this);
            int now = GenTicks.TicksGame;
            if (now < nextOperationalPollTick)
            {
                return;
            }
            nextOperationalPollTick = now + OperationalPollIntervalTicks;
            bool operational = Operational;
            if (operational != lastOperational)
            {
                lastOperational = operational;
                RegisterFact();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref inputBuffer, "inputBuffer", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (inputBuffer == null)
                {
                    inputBuffer = new SDS_EndpointBuffer(this);
                }
                else
                {
                    SDS_EndpointUtility.PruneLoadedBuffer(inputBuffer);
                }
            }
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            string status = (Operational ? "SDS.Status.Online" : "SDS.Status.Offline").Translate().ToString();
            return baseString.NullOrEmpty() ? status : baseString + "\n" + status;
        }

        /// <summary>可用性变化时重登记结构事实（Core 的 G 只读这份事实）。</summary>
        private void RegisterFact()
        {
            SDS_Rules.RegisterStructure(BuildFact());
        }

        /// <summary>当前结构事实：周期性对账与 SpawnSetup 共用同一构造，避免两份事实口径。</summary>
        internal SDS_StructureFact BuildFact()
        {
            return new SDS_StructureFact
            {
                Thing = this,
                Map = Map,
                Kind = SDS_StructureKind.Core,
                Operational = Operational
            };
        }
    }

    /// <summary>
    /// 超维空间锚点：与核心共用同一套端点流程；要求本图有可用核心，断电不可用。
    /// </summary>
    public class Building_SDS_Port : Building, ISDS_StorageEndpoint
    {
        private CompPowerTrader cachedPowerComp;
        private CompBreakdownable cachedBreakdownComp;
        private SDS_EndpointBuffer inputBuffer;
        private StorageSettings settings;
        private SDS_EndpointHandle handle;
        private int syncedFilterVersion = -1;

        public Building_SDS_Port()
        {
            inputBuffer = new SDS_EndpointBuffer(this);
        }

        private SDS_EndpointHandle Handle
        {
            get { return handle ?? (handle = SDS_EndpointHandle.ForBuilding(this)); }
        }

        public bool Operational
        {
            get
            {
                if (!SDS_BuildingUtility.IsOperational(this, SDS_BuildingUtility.PortWatts,
                    cachedPowerComp, cachedBreakdownComp))
                {
                    return false;
                }
                SDS_GameComponent_Grid grid = SDS_GameComponent_Grid.Instance;
                SDS_VanillaState state = grid != null ? grid.State : null;
                return state != null && state.HasOperationalCore(Map);
            }
        }

        // ---------- ISDS_StorageEndpoint ----------

        public ThingOwner InputBuffer
        {
            get { return inputBuffer; }
        }

        public bool HaulDestinationEnabled
        {
            get { return SDS_EndpointUtility.CanReceive(Handle); }
        }

        public bool Accepts(Thing thing)
        {
            return SDS_EndpointUtility.Accepts(Handle, thing, false);
        }

        public int SpaceRemainingFor(ThingDef stuff)
        {
            return SDS_EndpointUtility.SpaceRemainingFor(Handle);
        }

        // ---------- IStoreSettingsParent ----------

        public bool StorageTabVisible
        {
            get { return false; }
        }

        public StorageSettings GetStoreSettings()
        {
            EnsureSettings();
            SyncGlobalFilter();
            return settings;
        }

        public StorageSettings GetParentStoreSettings()
        {
            return null;
        }

        public void Notify_SettingsChanged()
        {
        }

        internal bool SyncStoragePriority()
        {
            EnsureSettings();
            StoragePriority target = SDS_VanillaState.CurrentStoragePriority;
            if (settings.Priority == target)
            {
                return false;
            }
            settings.Priority = target;
            return true;
        }

        private void EnsureSettings()
        {
            if (settings == null)
            {
                settings = new StorageSettings(this);
            }
        }

        private void SyncGlobalFilter()
        {
            SDS_GameComponent_Grid grid = SDS_GameComponent_Grid.Instance;
            SDS_VanillaState state = grid != null ? grid.State : null;
            if (state == null || settings == null || syncedFilterVersion == state.FilterVersion)
            {
                return;
            }
            syncedFilterVersion = state.FilterVersion;
            settings.filter.CopyAllowancesFrom(state.DepositFilter);
        }

        // ---------- IThingHolder：存入缓冲 ----------

        public ThingOwner GetDirectlyHeldThings()
        {
            return inputBuffer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            if (inputBuffer != null)
            {
                ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, inputBuffer);
            }
            if (Operational)
            {
                SDS_ProjectionHolder projection = SDS_ProjectionService.ProjectionFor(this);
                if (projection != null)
                {
                    outChildren.Add(projection);
                }
            }
        }

        // ---------- 生命周期 ----------

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            cachedPowerComp = GetComp<CompPowerTrader>();
            cachedBreakdownComp = GetComp<CompBreakdownable>();
            if (inputBuffer == null)
            {
                inputBuffer = new SDS_EndpointBuffer(this);
            }
            RegisterFact();
            SyncStoragePriority();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            SDS_Rules.UnregisterStructure(this);
            base.DeSpawn(mode);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            SDS_Rules.UnregisterStructure(this);
            if (inputBuffer != null && inputBuffer.Count > 0)
            {
                SDS_EndpointUtility.DrainInputBufferSafely(this, inputBuffer);
                inputBuffer.ClearAndDestroyContents();
            }
            base.Destroy(mode);
        }

        protected override void Tick()
        {
            base.Tick();
            SDS_EndpointUtility.FlushInputBuffer(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref inputBuffer, "inputBuffer", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (inputBuffer == null)
                {
                    inputBuffer = new SDS_EndpointBuffer(this);
                }
                else
                {
                    SDS_EndpointUtility.PruneLoadedBuffer(inputBuffer);
                }
            }
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            string status = (Operational ? "SDS.Status.Online" : "SDS.Status.Offline").Translate().ToString();
            return baseString.NullOrEmpty() ? status : baseString + "\n" + status;
        }

        private void RegisterFact()
        {
            SDS_Rules.RegisterStructure(BuildFact());
        }

        /// <summary>当前结构事实：周期性对账与 SpawnSetup 共用同一构造。</summary>
        internal SDS_StructureFact BuildFact()
        {
            return new SDS_StructureFact
            {
                Thing = this,
                Map = Map,
                Kind = SDS_StructureKind.Anchor,
                Operational = Operational
            };
        }
    }

    /// <summary>
    /// 超维空间稳固装置：只提供容量加成；断电时加成失效。
    /// </summary>
    public class Building_SDS_CapacityArray : Building
    {
        private const int OperationalPollIntervalTicks = 30;

        private int nextOperationalPollTick;
        private bool lastOperational;
        private CompPowerTrader cachedPowerComp;
        private CompBreakdownable cachedBreakdownComp;

        public bool Operational
        {
            get
            {
                return SDS_BuildingUtility.IsOperational(this, SDS_BuildingUtility.ArrayWatts,
                    cachedPowerComp, cachedBreakdownComp);
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            cachedPowerComp = GetComp<CompPowerTrader>();
            cachedBreakdownComp = GetComp<CompBreakdownable>();
            RegisterFact();
            lastOperational = Operational;
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            SDS_Rules.UnregisterStructure(this);
            base.DeSpawn(mode);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            SDS_Rules.UnregisterStructure(this);
            base.Destroy(mode);
        }

        protected override void Tick()
        {
            base.Tick();
            int now = GenTicks.TicksGame;
            if (now < nextOperationalPollTick)
            {
                return;
            }
            nextOperationalPollTick = now + OperationalPollIntervalTicks;
            bool operational = Operational;
            if (operational != lastOperational)
            {
                lastOperational = operational;
                RegisterFact();
            }
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            string status = (Operational ? "SDS.Status.Online" : "SDS.Status.Offline").Translate().ToString();
            return baseString.NullOrEmpty() ? status : baseString + "\n" + status;
        }

        private void RegisterFact()
        {
            SDS_Rules.RegisterStructure(BuildFact());
        }

        /// <summary>当前结构事实：周期性对账与 SpawnSetup 共用同一构造。</summary>
        internal SDS_StructureFact BuildFact()
        {
            return new SDS_StructureFact
            {
                Thing = this,
                Map = Map,
                Kind = SDS_StructureKind.CapacityArray,
                Operational = Operational
            };
        }
    }

    /// <summary>
    /// 隐藏金库：给镜像提供一个只计数、不收货的储存组；同时负责金库落点 / 搬迁 / 锚点 / 自愈。
    /// 不可选 / 不可拆 / 不绘制 / 不产生财富（Defs 已锁定）。
    /// </summary>
    public class Building_SDS_Vault : Building_Storage, IHaulDestination, IStoreSettingsParent, IHaulSource
    {
        // 规则 W37：金库必须锚定本图可用端点（建筑优先，其次带芯片 Pawn）；
        // 只在锚点身份 / 建筑位置变化或被外来 SlotGroup 占用时重锚；
        // 无可用端点时先撤镜像，仍有结构性端点建筑则保留金库，否则宽限 250 tick 后移除。
        public const string DefName = "SDS_HiddenVault";

        private static readonly Dictionary<Map, Building_SDS_Vault> vaults =
            new Dictionary<Map, Building_SDS_Vault>();
        private static readonly Dictionary<Map, int> noAnchorSince = new Dictionary<Map, int>();
        private static ThingDef vaultDef;
        private static bool vaultDefWarned;
        private const int NoAnchorGraceTicks = 250;

        private ThingOwner<Thing> emptyContents;

        // 只记运行时锚点，不写存档（避免新增存档字段）。
        private Thing anchorThing;
        private IntVec3 anchorPos = IntVec3.Invalid;

        bool IHaulDestination.HaulDestinationEnabled
        {
            get { return false; }
        }

        bool IStoreSettingsParent.StorageTabVisible
        {
            get { return false; }
        }

        internal static bool IsVault(Thing thing)
        {
            return thing is Building_SDS_Vault;
        }

        internal static void ResetStatics()
        {
            vaults.Clear();
            noAnchorSince.Clear();
        }

        private static ThingDef VaultDef
        {
            get
            {
                if (vaultDef == null && !vaultDefWarned)
                {
                    vaultDef = DefDatabase<ThingDef>.GetNamedSilentFail(DefName);
                    if (vaultDef == null)
                    {
                        vaultDefWarned = true;
                        Log.Error("[超维存储] 找不到隐藏金库 ThingDef（" + DefName
                            + "）：镜像不会生成（Game 可正常运行）。请检查 Defs/SDS_Buildings.xml。");
                    }
                }
                return vaultDef;
            }
        }

        internal static Building_SDS_Vault GetOn(Map map)
        {
            if (map == null || map.Disposed)
            {
                return null;
            }
            if (vaults.TryGetValue(map, out Building_SDS_Vault vault) && vault != null
                && !vault.Destroyed && vault.Spawned && vault.Map == map)
            {
                return vault;
            }
            vaults.Remove(map);
            ThingDef def = VaultDef;
            if (def != null && map.listerThings != null)
            {
                List<Thing> list = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < list.Count; i++)
                {
                    Building_SDS_Vault found = list[i] as Building_SDS_Vault;
                    if (found != null && found.Spawned && !found.Destroyed && found.Map == map)
                    {
                        vaults[map] = found;
                        return found;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 确保该图存在隐藏金库，并返回镜像应使用的格子。
        /// 已有金库若被玩家储存区覆盖或锚点变化，搬到无区域格；搬不动则保留原格由计数补丁兜底。
        /// </summary>
        internal static Building_SDS_Vault EnsureOn(Map map, IntVec3 preferredCell, Thing anchorThing,
            out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (map == null || map.Disposed)
            {
                return null;
            }
            Building_SDS_Vault existing = GetOn(map);
            if (existing != null)
            {
                bool foreignGroup = CellOwnedByForeignGroup(map, existing.Position, existing.GetSlotGroup());
                bool anchorChanged = anchorThing != null
                    && (existing.anchorThing != anchorThing
                        || (anchorThing is Building && anchorThing.Position != existing.anchorPos));
                if (foreignGroup || anchorChanged)
                {
                    IntVec3 target = FindVaultCell(map, preferredCell);
                    if (target.IsValid && target == existing.Position)
                    {
                        existing.SetAnchor(anchorThing);
                        RepairEdificeRegistration(map, existing);
                        cell = existing.Position;
                        return existing;
                    }
                    if (TryRelocate(map, existing, preferredCell, out IntVec3 moved))
                    {
                        existing.SetAnchor(anchorThing);
                        SDS_MirrorService.NotifyVaultRelocated(map);
                        cell = moved;
                        return existing;
                    }
                    existing.SetAnchor(anchorThing);
                    if (foreignGroup)
                    {
                        return existing;
                    }
                }
                else
                {
                    RepairEdificeRegistration(map, existing);
                }
                cell = existing.Position;
                return existing;
            }
            ThingDef def = VaultDef;
            if (def == null)
            {
                SDS_Diag.Fail("金库/Def缺失", SDS_Diag.Ctx(map, null, null, null, "defName=" + DefName));
                return null;
            }
            IntVec3 spawnCell = FindVaultCell(map, preferredCell);
            if (!spawnCell.IsValid)
            {
                SDS_Diag.Fail("金库/全图无空格", SDS_Diag.Ctx(map, null, null, null,
                    "preferred=" + (preferredCell.IsValid ? preferredCell.ToString() : "无效")));
                return null;
            }
            Building_SDS_Vault vault = ThingMaker.MakeThing(def) as Building_SDS_Vault;
            if (vault == null)
            {
                SDS_Diag.Fail("金库/创建失败", SDS_Diag.Ctx(map, null, null, null, "defName=" + DefName));
                return null;
            }
            GenSpawn.Spawn(vault, spawnCell, map);
            if (vault.Destroyed || !vault.Spawned)
            {
                SDS_Diag.Fail("金库/落子失败", SDS_Diag.Ctx(map, null, vault, null,
                    "target=" + spawnCell));
                return null;
            }
            vault.SetAnchor(anchorThing);
            cell = vault.Position;
            return vault;
        }

        internal Thing AnchorThing
        {
            get { return anchorThing; }
        }

        internal static bool IsAnchorUsable(Thing anchor, Map map)
        {
            if (anchor == null || anchor.Destroyed || !anchor.Spawned || anchor.Map != map)
            {
                return false;
            }
            Pawn pawn = anchor as Pawn;
            if (pawn != null)
            {
                return SDS_ImplantUtility.IsPortOperational(pawn);
            }
            return anchor is ISDS_StorageEndpoint;
        }

        private void SetAnchor(Thing anchor)
        {
            anchorThing = anchor;
            anchorPos = anchor != null ? anchor.Position : IntVec3.Invalid;
        }

        /// <summary>本图没有任何可用端点：结构性端点还在就保留；全无则宽限 250 tick 后移除。</summary>
        internal static void HandleNoAnchor(Map map)
        {
            if (map == null || map.Disposed)
            {
                return;
            }
            if (HasStructuralEndpoint(map))
            {
                noAnchorSince.Remove(map);
                return;
            }
            int now = GenTicks.TicksGame;
            if (!noAnchorSince.TryGetValue(map, out int since))
            {
                noAnchorSince[map] = now;
                return;
            }
            if (now - since < NoAnchorGraceTicks)
            {
                return;
            }
            noAnchorSince.Remove(map);
            RemoveOn(map);
        }

        internal static void ClearNoAnchorTimer(Map map)
        {
            if (map != null)
            {
                noAnchorSince.Remove(map);
            }
        }

        private static bool HasStructuralEndpoint(Map map)
        {
            if (map == null || map.listerBuildings == null)
            {
                return false;
            }
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (building != null && !building.Destroyed && building is ISDS_StorageEndpoint)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>撤回金库（镜像已由 RemoveMap 清空；账本零写入）。</summary>
        internal static void RemoveOn(Map map)
        {
            if (map == null)
            {
                return;
            }
            Building_SDS_Vault vault = GetOn(map);
            if (vault == null || vault.Destroyed)
            {
                return;
            }
            try
            {
                vault.Destroy(DestroyMode.Vanish);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("金库/撤回失败（已忽略）", e);
            }
        }

        /// <summary>
        /// 自愈：EdificeGrid.DeRegister 不校验"解登记的是不是自己"，
        /// 金库格被别的建筑离场清空后会掉回单格上限 1；这里重新登记回去。
        /// </summary>
        private static void RepairEdificeRegistration(Map map, Building_SDS_Vault vault)
        {
            if (map == null || map.edificeGrid == null || vault == null || !vault.Spawned)
            {
                return;
            }
            IntVec3 cell = vault.Position;
            if (map.edificeGrid[cell] == vault)
            {
                return;
            }
            Building first = cell.GetFirstBuilding(map);
            if (first != null && first != vault)
            {
                SDS_Diag.Warn("金库/登记跳过", SDS_Diag.Ctx(map, null, vault, null,
                    "cell=" + cell));
                return;
            }
            map.edificeGrid.Register(vault);
            SDS_Diag.Warn("金库/登记自愈", SDS_Diag.Ctx(map, null, vault, null, "cell=" + cell));
        }

        private static bool CellOwnedByForeignGroup(Map map, IntVec3 cell, SlotGroup own)
        {
            HaulDestinationManager manager = map != null ? map.haulDestinationManager : null;
            if (manager == null)
            {
                return false;
            }
            List<SlotGroup> groups = manager.AllGroupsListForReading;
            for (int i = 0; i < groups.Count; i++)
            {
                SlotGroup group = groups[i];
                if (group == null || group == own)
                {
                    continue;
                }
                if (group.CellsList.Contains(cell))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryRelocate(Map map, Building_SDS_Vault vault, IntVec3 preferredCell,
            out IntVec3 newCell)
        {
            newCell = IntVec3.Invalid;
            IntVec3 target = FindVaultCell(map, preferredCell);
            if (!target.IsValid)
            {
                SDS_Diag.Warn("金库/搬迁无空位", SDS_Diag.Ctx(map, null, vault, null,
                    "cell=" + vault.Position));
                return false;
            }
            IntVec3 old = vault.Position;
            try
            {
                vault.DeSpawn(DestroyMode.Vanish);
                GenSpawn.Spawn(vault, target, map);
                if (vault.Spawned && vault.Position == target)
                {
                    newCell = target;
                    return true;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.Fail("金库/搬迁失败", SDS_Diag.Ctx(map, null, vault, null,
                    "from=" + old + " to=" + target), e);
            }
            try
            {
                if (!vault.Spawned && old.IsValid && old.InBounds(map))
                {
                    GenSpawn.Spawn(vault, old, map);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("金库/搬退回原格失败（已忽略）", e);
            }
            return false;
        }

        private static IntVec3 FindVaultCell(Map map, IntVec3 preferred)
        {
            IntVec3 baseCell = preferred.IsValid && preferred.InBounds(map) ? preferred : map.Center;
            if (IsFreeCell(map, baseCell))
            {
                return baseCell;
            }
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(baseCell, 12f, true))
            {
                if (IsFreeCell(map, cell))
                {
                    return cell;
                }
            }
            // 端点附近没有空格：全图找最近的无区域空格（只在失败路径执行）。
            IntVec3 best = IntVec3.Invalid;
            float bestDist = float.MaxValue;
            foreach (IntVec3 cell in map.AllCells)
            {
                if (!IsFreeCell(map, cell))
                {
                    continue;
                }
                float dist = (float)(cell - baseCell).LengthHorizontalSquared;
                if (dist < bestDist)
                {
                    best = cell;
                    bestDist = dist;
                }
            }
            return best;
        }

        private static bool IsFreeCell(Map map, IntVec3 cell)
        {
            // 金库自己是 SlotGroup：同一格不能同时属于两个组，否则 ResourceCounter 会数两遍。
            return cell.IsValid && cell.InBounds(map) && !cell.Fogged(map)
                && cell.Standable(map) && cell.GetFirstBuilding(map) == null
                && cell.GetZone(map) == null
                && (map.haulDestinationManager == null
                    || map.haulDestinationManager.SlotGroupAt(cell) == null);
        }

        private void EnsureStorageSettings()
        {
            if (settings == null)
            {
                settings = new StorageSettings(this);
                SDS_Diag.Warn("存档/金库筛选补齐", SDS_Diag.Ctx(Map, null, this, null,
                    "旧存档缺少 settings"));
            }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            if (emptyContents == null)
            {
                emptyContents = new ThingOwner<Thing>(this, false);
            }
            return emptyContents;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            // 只把镜像投影挂成子 holder：直接内容物保持为空（只读直接内容的路径看不到库存）。
            SDS_ProjectionHolder projection = SDS_ProjectionService.ProjectionFor(this);
            if (projection != null)
            {
                outChildren.Add(projection);
            }
        }

        public bool HaulSourceEnabled
        {
            get { return true; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureStorageSettings();
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            EnsureStorageSettings();
            base.SpawnSetup(map, respawningAfterLoad);
            if (map != null)
            {
                vaults[map] = this;
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            if (map != null && vaults.TryGetValue(map, out Building_SDS_Vault vault) && vault == this)
            {
                vaults.Remove(map);
            }
            // 必须在 base.DeSpawn 之前：原版搬迁循环会把镜像 SplitOff 当成取货扣账。
            if (map != null)
            {
                try
                {
                    SDS_MirrorService.EvacuateForStructureChange(map, "vault-despawn");
                }
                catch (Exception e)
                {
                    SDS_Diag.ErrorOnce("镜像/结构撤离异常（已忽略，继续移除金库）", e);
                }
            }
            base.DeSpawn(mode);
        }
    }

    /// <summary>芯片植入：端点集合 / 芯片缓存失效。</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
        new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    internal static class Patch_SDS_EndpointChipAdded
    {
        public static void Postfix(Hediff hediff)
        {
            try
            {
                if (hediff != null
                    && (hediff.def == SDS_HediffDefOf.ChipA || hediff.def == SDS_HediffDefOf.ChipB))
                {
                    SDS_ImplantUtility.NotifyChipStateChanged();
                    SDS_Rules.NotifyEndpointChanged();
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("端点：芯片植入事件处理失败（已忽略）", e);
            }
        }
    }

    /// <summary>带芯片小人 / 机械体进图：端点候选与可用性都可能变化。</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup), new[] { typeof(Map), typeof(bool) })]
    internal static class Patch_SDS_EndpointPawnSpawned
    {
        public static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                if (SDS_ImplantUtility.EffectiveChipDef(__instance) != null
                    || __instance.Faction == Faction.OfPlayer)
                {
                    SDS_Rules.NotifyEndpointChanged();
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("端点：小人进图事件处理失败（已忽略）", e);
            }
        }
    }

    /// <summary>玩家派系小人离图：本图可用性与后续暴露口径可能变化。</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn), new[] { typeof(DestroyMode) })]
    internal static class Patch_SDS_PlayerPresencePawnDespawned
    {
        public static void Prefix(Pawn __instance, out bool __state)
        {
            __state = __instance != null && __instance.Faction == Faction.OfPlayer;
        }

        public static void Postfix(bool __state)
        {
            if (!__state)
            {
                return;
            }
            try
            {
                SDS_Rules.NotifyEndpointChanged();
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("端点：小人离图事件处理失败（已忽略）", e);
            }
        }
    }

    /// <summary>阵营变为 / 不再是玩家：本图可用性与后续暴露口径可能变化。</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction), new[] { typeof(Faction), typeof(Pawn) })]
    internal static class Patch_SDS_PlayerPresenceFactionChanged
    {
        public static void Prefix(Pawn __instance, out bool __state)
        {
            __state = __instance != null && __instance.Faction == Faction.OfPlayer;
        }

        public static void Postfix(Pawn __instance, bool __state)
        {
            try
            {
                bool nowPlayer = __instance != null && __instance.Faction == Faction.OfPlayer;
                if (__state || nowPlayer)
                {
                    SDS_Rules.NotifyEndpointChanged();
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("端点：阵营变化事件处理失败（已忽略）", e);
            }
        }
    }
}
