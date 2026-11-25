using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 规则模块唯一对外门面（P2：端点决议 + 账单补料；P3：流程层；P4：判定层）。
    /// 目标：唯一定义本 Mod 的玩法 / 口径判定；账本与数量算术仍在 SDS.Core。
    /// 全部成员只转发本程序集的规则实现；不得在本文件写控制流（E9）。
    /// </summary>
    public static class SDS_Rules
    {
        /// <summary>端点决议；纯读，不预留、不派活。选择顺序与门槛在 SDS_EndpointResolver。</summary>
        /// <remarks>入：query = 地图 / 优先级 / 距离等查询条件（in 传引用，避免拷贝）。出：决议结果（含端点与可用性）。
        /// 区别：只回答「用哪个端点」，不扣账、不生成 Job；缓存随 `SDS_Rules.EndpointVersion` 失效。</remarks>
        public static SDS_EndpointDecision SelectEndpoint(in SDS_EndpointQuery query)
            => SDS_EndpointResolver.Select(query);

        /// <summary>配方槽位是否接受某 def / 实物；纯读，不预留。外部筛选（医药等）由调用方以委托注入。</summary>
        /// <remarks>入：slot = 配方槽；thing / def = 待判定对象（可只给其一）；bill / recipe = 上下文，可空；
        /// allowedIngredients / corpseFilter = 调用方附加的允许集与尸体筛选，可空；extraAllows = 外部附加筛选，可空。
        /// 出：true = 该槽接受。区别：只做单槽判定，不计算缺口；缺口见 TryPlanSlot。</remarks>
        public static bool AllowsDef(IngredientCount slot, Thing thing, ThingDef def, Bill bill,
            RecipeDef recipe, List<ThingDef> allowedIngredients, ThingFilter corpseFilter,
            Func<ThingDef, bool> extraAllows = null)
            => SDS_BillSupplyPlanner.AllowsDef(slot, thing, def, bill, recipe,
                allowedIngredients, corpseFilter, extraAllows);

        /// <summary>补料规划（单槽）：只产出计划，不改账本、不取货。</summary>
        /// <remarks>入：slot / bill / recipe / allowedIngredients / corpseFilter = 槽位上下文；need = 需求件数；
        /// ownCount / externalCount = 自有 / 外部供给查法；extraAllows = 附加判据；reserveProvider + reserveApplied
        /// = 预留夹取与去重集合；pool = 本次规划已分配的 def → 件数。
        /// 出：true = 得到一条补料方案（def / required / fromOwn / shortfall）；false = 无可用 def，out 全默认值。
        /// 区别：与 TryPlanAllowedDefSupply 的区别是入口为「单槽」还是「def 列表」；两者共用同一分配策略与 pool。</remarks>
        public static bool TryPlanSlot(IngredientCount slot, Bill bill, RecipeDef recipe,
            List<ThingDef> allowedIngredients, ThingFilter corpseFilter, int need,
            Func<ThingDef, int> ownCount, Func<ThingDef, long> externalCount,
            Func<ThingDef, bool> extraAllows,
            Func<ThingDef, int> reserveProvider, HashSet<ThingDef> reserveApplied,
            Dictionary<ThingDef, int> pool,
            out ThingDef def, out int required, out int fromOwn, out int shortfall)
            => SDS_BillSupplyPlanner.TryPlanSlot(slot, bill, recipe, allowedIngredients,
                corpseFilter, need, ownCount, externalCount, extraAllows, reserveProvider,
                reserveApplied, pool, out def, out required, out fromOwn, out shortfall);

        /// <summary>补料规划（def 列表）：只产出计划，不改账本、不取货。</summary>
        /// <remarks>入：allowedDefs = 候选 def；corpseFilter = 尸体筛选，可空；needForDef = 每个 def 的需求量；
        /// 其余参数语义同 TryPlanSlot（自有 / 外部 / 附加判据 / 预留 / pool）。
        /// 出：true = 选出方案；false = 候选全不可用，out 全默认值。
        /// 区别：与 TryPlanSlot 的区别是不绑定具体槽位，用于「任意允许 ing 列表」场景。</remarks>
        public static bool TryPlanAllowedDefSupply(
            List<ThingDef> allowedDefs, ThingFilter corpseFilter,
            Func<ThingDef, int> needForDef,
            Func<ThingDef, int> ownCount, Func<ThingDef, long> externalCount,
            Func<ThingDef, bool> extraAllows,
            Func<ThingDef, int> reserveProvider, HashSet<ThingDef> reserveApplied,
            Dictionary<ThingDef, int> pool,
            out ThingDef def, out int required, out int fromOwn, out int shortfall)
            => SDS_BillSupplyPlanner.TryPlanAllowedDefSupply(allowedDefs, corpseFilter, needForDef,
                ownCount, externalCount, extraAllows, reserveProvider, reserveApplied, pool,
                out def, out required, out fromOwn, out shortfall);

        /// <summary>登记一次补料存入：只更新调用方传入的 pool 计数，不写账本。</summary>
        /// <remarks>入：pool = 本次规划池，可空；def = 已存入 def；amount = 件数。出：无。
        /// 区别：与 `SDS_Rules.TryDeposit` 的区别是纯记账内存，不落账本、不销毁实物。</remarks>
        public static void NotifyDeposited(Dictionary<ThingDef, int> pool, ThingDef def, int amount)
            => SDS_BillSupplyPlanner.NotifyDeposited(pool, def, amount);

        // ---------- P3：存入 / 取出流程 ----------

        /// <summary>该实物能否存入（通道 + 容量 + 宿主筛选）；纯读，不占用容量。</summary>
        /// <remarks>入：thing = 待判定实物。出：true = 当前可收下。
        /// 区别：比 SDS_StockRules.CanDeposit 多「身份门 / 容量门」；不等价于 TryDeposit 成功（后者还可能被端口状态挡）。</remarks>
        public static bool CanDeposit(Thing thing) => SDS_RulesImpl.CanDeposit(thing);

        /// <summary>
        /// 向指定端口入账。
        /// 成功：accepted = 实际收下件数；虚拟通道会销毁原实物，保真通道把实物转入账本。
        /// 失败：accepted = 0，实物保持原状（不销毁、不转移）。
        /// </summary>
        /// <remarks>入：thing = 源实物；port = 目标端口（须 Operational）。出：true = 已入账，accepted = stackCount。
        /// 区别：与「无端口直接转虚」不同，本方法要求端口可用；与 CanDeposit 的区别是它真正落账并销毁 / 转移实物。</remarks>
        public static bool TryDeposit(Thing thing, ISDS_Port port, out int accepted)
            => SDS_RulesImpl.TryDeposit(thing, port, out accepted);

        /// <summary>
        /// 事务级取出虚拟库存。
        /// 成功：made 为已物化实物，交付由调用方完成；账本已扣、预留已消费。
        /// 失败：made = null，账本与实物整笔回滚，不留下半截。
        /// </summary>
        /// <remarks>入：plan = 单计划（键 / 件数 / 模式），必须是虚拟通道；actor = 触发者，可空。出：true = made 非空。
        /// 区别：与 TryWithdrawPreservedExact 的通道不同；本方法强制**足额**（不足整笔失败），
        ///       而 Job 预留路径允许部分成功。</remarks>
        public static bool TryWithdraw(SDS_WithdrawPlan plan, Pawn actor, out List<Thing> made)
            => SDS_RulesImpl.TryWithdraw(plan, actor, out made);

        /// <summary>
        /// 按精确真身计划取出保真库存。
        /// 成功：made 为对应真身；失败：made = null，整笔回滚（W24：不得跨桶顶替）。
        /// </summary>
        /// <remarks>入：plan = 点名真身 + 件数（必须全在保真口径）；actor = 触发者，可空。出：true = made 非空。
        /// 区别：与 TryWithdraw 的「按键凑数」不同，本方法锁定实例；与 Adopted 版不同，本方法自建并自行释放预留。</remarks>
        public static bool TryWithdrawPreservedExact(List<ThingCount> plan, Pawn actor,
            out List<Thing> made)
            => SDS_RulesImpl.TryWithdrawPreservedExact(plan, actor, out made);

        /// <summary>
        /// 接管既有 Job 预留后的精确保真取出；失败不主动释放预留，由 Job FinishAction 统一清理。
        /// </summary>
        /// <remarks>入：reservationId = Job 预留 id（0 失败）；plan = 点名真身；mode = 物化模式。出：true = made 非空。
        /// 区别：与 TryWithdrawPreservedExact 的区别只在预留来源（接管 vs 自建）；失败同样不释放预留。</remarks>
        public static bool TryWithdrawPreservedExactAdopted(int reservationId, List<ThingCount> plan,
            out List<Thing> made, SDS_WithdrawMode mode = SDS_WithdrawMode.Hand)
            => SDS_RulesImpl.TryWithdrawPreservedExactAdopted(reservationId, plan, out made, mode);

        /// <summary>消费既有 Job 预留并取出；无预留或不足即失败，失败不改账。</summary>
        /// <remarks>入：reservationId = Job 预留 id。出：true = 已物化，made 非空且**未交付**（调用方负责放置）；
        /// false = made = null，预留仍留给 Job 清理。
        /// 区别：与 TryWithdraw 的自建预留不同，本方法只消费他人预留，且允许部分成功、不主动 Deliver。</remarks>
        public static bool TryWithdrawReservedForJob(int reservationId, out List<Thing> made)
            => SDS_RulesImpl.TryWithdrawReservedForJob(reservationId, out made);

        /// <summary>为 Job 建预留（按 def 选键，受 maxStackSpace 夹取）。已有同 job 的虚拟同 def 预留时回填 key / count 并返回 true；已有其他通道 / 归属不匹配的预留时返回 false，不覆盖既有预留。</summary>
        /// <remarks>入：jobLoadID = Job 归属 id；def = 目标 def；maxStackSpace = 单次上限（≤0 直接失败）；
        /// pawn = 位置参考，可空；preferredKey = 优先键，可 null。
        /// 出：true = 已预留（或已有可复用的虚拟同 def 预留），key / count 为实际键与件数；false = key = null、count = 0。
        /// 区别：与 TryReservePreservedForJob 的区别是「按 def 选键 + 夹取」，本方法不走保真精确实例。</remarks>
        public static bool TryReserveForJob(int jobLoadID, ThingDef def, int maxStackSpace,
            Pawn pawn, SDS_StockKey preferredKey, out SDS_StockKey key, out int count)
            => SDS_RulesImpl.TryReserveForJob(jobLoadID, def, maxStackSpace, pawn, preferredKey,
                out key, out count);

        /// <summary>按精确真身计划建保真预留；参数有效（plan 非空）且已有该 jobLoadID 预留时直接返回 true。</summary>
        /// <remarks>入：jobLoadID = Job 归属 id；plan = 点名真身 + 件数；pawn = 触发者，可空。
        /// 出：true = 已预留（或参数有效且已有预留）；false = 参数无效或预留不足（不留半截）。
        /// 区别：与 TryReserveForJob 的区别是按实例而非 def；与 TryWithdrawPreservedExact 的区别是只预留不取出。</remarks>
        public static bool TryReservePreservedForJob(int jobLoadID, List<ThingCount> plan, Pawn pawn)
            => SDS_RulesImpl.TryReservePreservedForJob(jobLoadID, plan, pawn);

        /// <summary>读取某个 Job 的唯一预留键与合计件数；只读，不修改预留。</summary>
        /// <remarks>入：jobLoadID = Job 归属 id。出：key / total；该 Job 无预留、预留为 0 或存在多个不同键时返回 false。
        /// 用途：装载途中读档后按预留键重建保真计划（W47）；调用方不得据此自行扣账或释放。</remarks>
        public static bool TryGetJobReservation(int jobLoadID, out SDS_StockKey key, out int total)
            => SDS_RulesImpl.TryGetJobReservation(jobLoadID, out key, out total);

        /// <summary>释放该 Job 的全部预留；幂等，0 与未知 id 忽略。</summary>
        /// <remarks>入：jobLoadID = Job 归属 id。出：无；只清预留，不动物理实物与账面数量。
        /// 区别：与 TryWithdrawReservedForJob 的「消费」相反，本方法不取出任何东西。</remarks>
        public static void ReleaseJobReservation(int jobLoadID)
            => SDS_RulesImpl.ReleaseJobReservation(jobLoadID);

        /// <summary>从镜像取出真身；成功时 taken 为真身，失败时 taken = null。</summary>
        /// <remarks>入：mirror = 地图镜像；count = 取出件数；physical = 调用方注入的物理转换器。出：true = taken 非空且已扣账。
        /// 失败时账本按物理层实际件数对齐并回充；物理层遵守契约（失败不改镜像）时镜像不变，违反契约时以物理层实际状态为准。
        /// 区别：与 TryWithdraw 的「凭空物化」不同，本方法先把镜像就地转成实物；失败会退回刚扣的账。</remarks>
        public static bool TryTakeMirror(Thing mirror, int count, ISDS_MirrorPhysical physical,
            out Thing taken)
            => SDS_RulesImpl.TryTakeMirror(mirror, count, physical, out taken);

        /// <summary>
        /// 外部销毁确认（W22）：只扣账、不预留；不足按现有量扣，0 也放行销毁。
        /// 返回 false 仅表示参数 / 通道 / 扣账本身异常，调用方应拒绝销毁。
        /// </summary>
        /// <remarks>入：mirror = 将被外部销毁的镜像；out consumed = 实际扣减件数。出：true = 允许销毁。
        /// 区别：与 TryTakeMirror 的区别是不生成实物、不做物理转换，只同步账面；
        ///       与 TryWithdraw 的区别是不预留、不做足额校验，允许 0 件也放行。</remarks>
        public static bool TryConsumeMirror(Thing mirror, out int consumed)
            => SDS_RulesImpl.TryConsumeMirror(mirror, out consumed);

        /// <summary>
        /// W34 一次性存量迁移：只迁移设置生效后已不需要保真的武器 / 衣物。
        /// 返回转换件数；无账本返回 0。只在玩家确认后调用，不得在启动 / 读档时静默调用。
        /// </summary>
        /// <remarks>入：无。出：成功迁移的件数（0 = 无可迁移或失败）。
        /// 区别：与 TryDeposit 不同，本方法绕过容量 / 筛选 / 可用性门（迁移专用），且只处理武器 / 衣物。</remarks>
        public static int ConvertPreservedGearToVirtual()
            => SDS_RulesImpl.ConvertPreservedGearToVirtual();

        // ---------- P3：回充 ----------

        /// <summary>回充单件：先回账本；回账失败时按 fallback 交给宿主（退路成功也不改变返回值）；不抛异常。</summary>
        /// <remarks>入：thing = 待回充实物；channel = 虚拟 / 保真通道；map / pawn = 落点上下文，可空；
        /// fallback = 回账失败时的退路。出：true = 已回到账本；落到退路（地面 / 背包 / 手持）或无处可去返回 false。
        /// 区别：与 ReturnThing 带 cell 的重载相比，落点由实现自行推导；与 ReturnVirtualCount 的区别是处理实物。</remarks>
        public static bool ReturnThing(Thing thing, SDS_StockChannel channel,
            Map map, Pawn pawn, SDS_ReturnFallback fallback)
            => SDS_RulesImpl.ReturnThing(thing, channel, map, pawn, fallback);

        /// <summary>
        /// 回充单件（带调用方落点）：cell 只作用于 Ground 退路；无效 / 越界时回退到
        /// pawn 脚下 / 地图中心。
        /// </summary>
        /// <remarks>入：参数同上，另加 cell = 期望落点。出：true = 已回到账本；退路交付返回 false。
        /// 区别：与无 cell 重载的唯一差别是显式落点；两者回账口径完全一致。</remarks>
        public static bool ReturnThing(Thing thing, SDS_StockChannel channel,
            Map map, Pawn pawn, SDS_ReturnFallback fallback, IntVec3 cell)
            => SDS_RulesImpl.ReturnThing(thing, channel, map, pawn, fallback, cell);

        /// <summary>销毁失败时的数量回充：只补虚拟账本，不涉及实物。</summary>
        /// <remarks>入：key = 虚拟键；count = 补回件数（≤0 忽略）。出：true = 已补账。
        /// 区别：与 ReturnThing 的区别是没有实物、不做物化；仅用于「账已扣但销毁失败」的补偿。</remarks>
        public static bool ReturnVirtualCount(SDS_StockKey key, int count)
            => SDS_RulesImpl.ReturnVirtualCount(key, count);

        /// <summary>逐件调用 <see cref="ReturnThing"/>；单件失败不影响其余。</summary>
        /// <remarks>入：things = 待回充列表，可空；其余同 ReturnThing。出：无；不汇总失败数（失败由诊断通道记录）。
        /// 区别：与 ReturnThing 的区别是批量语义且忽略单件结果；与 ReturnThings 的带 cell 版本不存在，落点需逐件指定。</remarks>
        public static void ReturnThings(List<Thing> things, SDS_StockChannel channel,
            Map map, Pawn pawn, SDS_ReturnFallback fallback)
            => SDS_RulesImpl.ReturnThings(things, channel, map, pawn, fallback);

        // ---------- P3：在途物化标记 ----------

        /// <summary>该实物是否带「刚物化、待原版消费」标记；纯读。</summary>
        /// <remarks>入：thing = 待查实物，null 返回 false。出：true = 仍在标记集中。纯读。
        /// 区别：与 IsPreserved 无关；标记只表示「这批实物由我们物化」，不代表所有权。</remarks>
        public static bool IsProducedMarked(Thing thing) => SDS_RulesImpl.IsProducedMarked(thing);

        /// <summary>刚物化、待原版消费的来源标记：Vanilla 物化后登记；W49 只在交付期间有效。</summary>
        /// <remarks>入：thing = 刚生成实物，可空。出：无；加入进程级强引用表（1 天寿命 + 8192 上限，到期 / 销毁 / 落地时清理）。
        /// 区别：与 RegisterProtectedOutput 的区别是「短命标记」，不参与回收与回充。</remarks>
        public static void MarkProduced(Thing thing) => SDS_RulesImpl.MarkProduced(thing);

        /// <summary>解除来源标记（回充 / 交付完成后）；不存在时忽略。</summary>
        /// <remarks>入：thing = 目标实物，可空。出：无；幂等。
        /// 区别：与 MarkProduced 成对；不触碰账本与回收账本。</remarks>
        public static void UnmarkProduced(Thing thing) => SDS_RulesImpl.UnmarkProduced(thing);

        /// <summary>模块初始化时调用一次：登记 4 个静态清理项与 Rules 自检提供者（E1）；幂等。</summary>
        /// <remarks>入：无。出：无。区别：只登记清理动作与诊断提供者，不执行清理、不注册账本。</remarks>
        public static void RegisterAll() => SDS_RulesImpl.RegisterAll();

        // ---------- P4：判定层（通道 / 容量 / 可用量 / 结构 / 芯片） ----------

        /// <summary>全局超维空间是否可用（世界存在可用核心）；纯读，不看本图是否暴露端点。</summary>
        /// <remarks>入：无。出：true = 至少一个 `Kind == Core` 且 `Operational` 的结构事实存在。区别：与端点判定无关，端点找不到时本值仍可为 true。</remarks>
        public static bool Available => SDS_RulesImpl.Available;

        /// <summary>容量口径：品类容量；纯读。</summary>
        /// <remarks>入：无。出：品类容量上限。区别：与 SDS_Core.UsedCapacity 配对；不看单图端点，全局口径。</remarks>
        public static long TypeCapacity => SDS_RulesImpl.TypeCapacity;

        /// <summary>端点契约版本：地图 / 优先级 / 端点集合变化时推进，用于端点相关缓存失效。</summary>
        /// <remarks>入：无。出：版本号。区别：与 StockVersion 等账本版本相互独立，只管端点契约。</remarks>
        public static int EndpointVersion => SDS_RulesImpl.EndpointVersion;

        /// <summary>
        /// 实例 → 通道 / 键的唯一规则；纯读。
        /// 已在保真账本中的实例按保真口径，其余按实例分流规则；失败时 key = null。
        /// </summary>
        /// <remarks>
        /// 入：thing = 待判定实例；out key = 结果键。出：true = key 非空；false = thing 为 null 或无法构键。
        /// 区别：与 RequiresPreservedStorage(ThingDef) 的区别是「按实例状态分流」，会看保真持有 / 实例非键状态。
        /// </remarks>
        public static bool TryGetStockKey(Thing thing, out SDS_StockKey key)
            => SDS_RulesImpl.TryGetStockKey(thing, out key);

        /// <summary>def 级通道判定（是否必须走保真）；纯读。</summary>
        /// <remarks>入：def = 目标 def。出：true = 该 def 必须保真（不可重建 / 逐件跟踪 / 有 def 级非键状态）。
        /// 区别：与 TryGetStockKey 的「实例分流」不同，本方法只看 def，不含染色 / 充能等实例状态。</remarks>
        public static bool RequiresPreservedStorage(ThingDef def)
            => SDS_RulesImpl.RequiresPreservedStorage(def);

        /// <summary>旧口径不可重建黑名单（尸体 / 缩装 / 未完成 / 基因载体）；纯读。</summary>
        /// <remarks>入：def = 目标 def（null 视为不可重建）。出：true = 命中黑名单。
        /// 区别：是 RequiresPreservedStorage 的子集判据之一，不能替代它（还含逐件跟踪 / 非键状态）。</remarks>
        public static bool IsLegacyUnrebuildableDef(ThingDef def)
            => SDS_RulesImpl.IsLegacyUnrebuildableDef(def);

        /// <summary>品类归属键：按设置归零品质 / 材质维度；纯读，记录本身仍保留精确值。</summary>
        /// <remarks>入：key = 精确键，null 返回 null。出：归属键（可能等于入参）。纯读。
        /// 区别：与键本身不同，本方法只用于「分组 / 容量 / 展示」；扣账、预留仍用精确键。</remarks>
        public static SDS_StockKey CategoryKeyOf(SDS_StockKey key)
            => SDS_RulesImpl.CategoryKeyOf(key);

        /// <summary>
        /// 按 def 找一条可取出键（唯一选择规则）：精确键优先 → 可用量最大 → 保真同口径；纯读。
        /// 失败时 key = null、available = 0。
        /// </summary>
        /// <remarks>
        /// 入：def = 目标 def；out key = 选中键；out available = 可用件数（夹到 int 上限）。
        /// 出：true = 有可用量且已选键；false = def 为空 / 全局不可用 / 全部禁用或为 0。
        /// 区别：与 TryFindTradableKey 共用选键实现，但本方法**因品类禁用而归零**；不预留，取用前须另行预留。
        /// </remarks>
        public static bool TryFindWithdrawableKey(ThingDef def, out SDS_StockKey key, out int available)
            => SDS_RulesImpl.TryFindWithdrawableKey(def, out key, out available);

        /// <summary>
        /// 交易 / 捐献 / 发射口径（R15）：与 <see cref="TryFindWithdrawableKey"/> 共用同一选键实现，
        /// 但不因品类禁用归零；全局不可用时返回 false。
        /// </summary>
        /// <remarks>入：def = 目标 def；out key / available 同上。出：true = 有可交易量。
        /// 区别：唯一差别是忽略品类禁用（禁用只限制「取用」，不限制「卖出」）。</remarks>
        public static bool TryFindTradableKey(ThingDef def, out SDS_StockKey key, out int available)
            => SDS_RulesImpl.TryFindTradableKey(def, out key, out available);

        /// <summary>def 级总可用量：全部虚拟键 + 全部保真键（同 def、非禁用）；纯读。</summary>
        /// <remarks>入：def = 目标 def（null / 无账本 / 全局不可用返回 0）。出：可用件数合计。
        /// 区别：与 TryFindWithdrawableKey 的「只给一条最佳键」不同，本方法求和；不预留，结果可能随后变化。</remarks>
        public static long GetAvailableCountForDef(ThingDef def)
            => SDS_RulesImpl.GetAvailableCountForDef(def);

        /// <summary>
        /// 注入外部保真分类输入（组合根调用一次；传 null 清除）。
        /// 入：provider = 「该实例是否额外要求保真」；出：无。
        /// 区别：Rules 只持有委托，不引用任何模块；只允许把物品加严为保真。
        /// </summary>
        public static void SetExternalPreserveProvider(Func<Thing, bool> provider)
            => SDS_RulesImpl.SetExternalPreserveProvider(provider);

        /// <summary>登记结构事实并推进端点版本。</summary>
        /// <remarks>入：fact = 结构事实（位置 / 类型 / 可用性等，in 传引用）。出：无；同实例重复登记以最新为准。
        /// 区别：与 ReconcileStructures 的「批量对账」不同，本方法是增量单条登记。</remarks>
        public static void RegisterStructure(in SDS_StructureFact fact)
            => SDS_RulesImpl.RegisterStructure(fact);

        /// <summary>移除结构事实并推进端点版本。</summary>
        /// <remarks>入：thing = 被移除的结构，可空。出：无；不存在时静默忽略。
        /// 区别：与 ReconcileStructures 的批量剔除互补。</remarks>
        public static void UnregisterStructure(Thing thing)
            => SDS_RulesImpl.UnregisterStructure(thing);

        /// <summary>
        /// 以活体事实集为准对账结构注册表：补齐漏登记、剔除陈旧项、修正未走回调的变化；
        /// 有变化才推进端点版本。返回是否发生变化。
        /// </summary>
        /// <remarks>入：live = 当前实际存活的结构事实列表。出：true = 注册表被改动。
        /// 区别：与 RegisterStructure / UnregisterStructure 的单条增量不同，本方法做整体一致化，供巡检调用。</remarks>
        public static bool ReconcileStructures(List<SDS_StructureFact> live)
            => SDS_RulesImpl.ReconcileStructures(live);

        /// <summary>通知端点集合 / 优先级来源变化：推进端点契约版本，端点缓存失效。</summary>
        /// <remarks>入：无。出：无；只推版本，不重算端点。
        /// 区别：与 NotifyDepositFilterChanged 等账本版本通知相互独立。</remarks>
        public static void NotifyEndpointChanged() => SDS_RulesImpl.NotifyEndpointChanged();
    }

    /// <summary>
    /// 可用量 / 供给失败的唯一口径：total − reserved，夹到 ≥ 0。
    /// 取用口径在超维空间不可用或品类禁用时归零；交易口径只受超维空间总开关约束（品类禁用不影响卖出）；账本数据仍保留。全部成员纯读。
    /// </summary>
    public static class SDS_Availability
    {
        /// <summary>
        /// 取用 / 生产口径：total − reserved，夹到 ≥ 0；品类禁用或全局不可用时归零（R17）。
        /// 未知键返回 0。
        /// </summary>
        /// <remarks>入：key = 品类键（虚拟或保真）。出：可用件数（≥0）。纯读。
        /// 区别：与 GetTradableCount 的唯一差别是**受品类禁用影响**；与 ISDS_LedgerRead.GetCount 的区别是已扣预留。</remarks>
        public static long GetAvailableCount(SDS_StockKey key)
            => SDS_AvailabilityImpl.GetAvailableCount(key);

        /// <summary>
        /// 交易 / 捐献 / 发射口径：total − reserved，不做品类禁用过滤，只受全局可用性约束（R15 / R17）。
        /// </summary>
        /// <remarks>入：key = 品类键。出：可交易件数（≥0）。纯读。
        /// 区别：与 GetAvailableCount 的唯一差别是忽略禁用；两者共用同一夹取与预留扣除。</remarks>
        public static long GetTradableCount(SDS_StockKey key)
            => SDS_AvailabilityImpl.GetTradableCount(key);

        /// <summary>供给失败分类：UnresolvedDef / Forbidden / Insufficient / None。</summary>
        /// <remarks>入：key = 品类键，可 null。出：首个失败原因；None = 无失败。
        /// 区别：与两个 Get*Count 的区别是给出「为什么为 0」，用于 UI / 诊断。</remarks>
        public static SDS_SupplyFailure GetFailure(SDS_StockKey key)
            => SDS_AvailabilityImpl.GetFailure(key);

        /// <summary>
        /// 取用夹取的唯一实现（W56）：min(需求, 可用量)；任一非正即 0（本次不取）。
        /// 可用量由调用方从 <see cref="GetAvailableCount"/> / <see cref="GetTradableCount"/> 取得。
        /// </summary>
        /// <remarks>入：wanted = 期望件数；available = 可用量（调用方已选口径）。出：实际可取件数。
        /// 区别：只做夹取，不查账本；调用方必须先取对应口径的可用量，否则会绕过禁用 / 交易规则。</remarks>
        public static int ClampTakeToAvailable(int wanted, long available)
            => SDS_AvailabilityImpl.ClampTakeToAvailable(wanted, available);
    }

    /// <summary>
    /// 植入体端口：芯片识别、机械体继承、可用性判定与等级缓存（实现在 SDS_ImplantUtilityImpl）。
    /// 全部成员纯读（<see cref="NotifyChipStateChanged"/> 只清缓存）。
    /// </summary>
    public static class SDS_ImplantUtility
    {
        /// <summary>芯片状态变化后清缓存（安装 / 更换 / 进档）；不清账本。</summary>
        /// <remarks>入：无。出：无；只清缓存，下一次查询重建。
        /// 区别：与 NotifyEndpointChanged 等版本通知不同，本方法无版本号，纯缓存失效。</remarks>
        public static void NotifyChipStateChanged()
            => SDS_ImplantUtilityImpl.NotifyChipStateChanged();

        /// <summary>取该 Pawn 的脑部零件；无芯片可绑位置或身体缺失时返回 null。</summary>
        /// <remarks>入：pawn = 目标小人，可空。出：脑部 BodyPartRecord 或 null。纯读。
        /// 区别：只解析身体部位，不判断芯片是否可用；可用性见 IsPortOperational。</remarks>
        public static BodyPartRecord ResolveBrainPart(Pawn pawn)
            => SDS_ImplantUtilityImpl.ResolveBrainPart(pawn);

        /// <summary>当前生效芯片 hediff；B 优先于 A，均无则 null。</summary>
        /// <remarks>入：pawn = 目标小人。出：身上 hediff 列表中的芯片 def（B 优先于 A）或 null。纯读。
        /// 区别：与 EffectiveChipDef 的区别是不含机械体继承与缓存。</remarks>
        public static HediffDef CurrentChipHediffDef(Pawn pawn)
            => SDS_ImplantUtilityImpl.CurrentChipHediffDef(pawn);

        /// <summary>有效芯片（机械体可继承监工）；带缓存，芯片状态变化后需 NotifyChipStateChanged。</summary>
        /// <remarks>入：pawn = 目标小人。出：生效芯片 def 或 null。纯读（读缓存）。
        /// 区别：与 CurrentChipHediffDef 的区别是多一层「机械体继承」，且结果有缓存。</remarks>
        public static HediffDef EffectiveChipDef(Pawn pawn)
            => SDS_ImplantUtilityImpl.EffectiveChipDef(pawn);

        /// <summary>该 Pawn 的端口是否可用 = 有效芯片非空且 IsChipOperational。</summary>
        /// <remarks>入：pawn = 目标小人。出：true = 端口可用。纯读。
        /// 区别：与 EffectiveChipDef 的区别是额外要求芯片处于可用状态。</remarks>
        public static bool IsPortOperational(Pawn pawn)
            => SDS_ImplantUtilityImpl.IsPortOperational(pawn);
    }
}
