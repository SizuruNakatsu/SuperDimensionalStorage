# SDS.Rules · 规则

> 定位：本模块「必须遵守什么」的可检查清单与规则正文。
> **当前状态（P5，迁移完成）**：端点决议、账单补料、判定层与流程层规则均已迁入；`SDS.Core/Rules.zh-CN.md` 只保留账本侧规则。迁移时**规范句逐字不变**，只改权威文档与代码锚点。

## 一、流程与补料规则（P2–P3）

### 1.1 S16 第三方补料

- 规范句：第三方补料（配方 / 网络订单）：第三方自有库存优先，SDS 只补缺口；原料 def 选择、`required` / `fromOwn` / `shortfall` 计算与预留一次只在 `SDS_BillSupplyPlanner` 实现，适配器只提供事实并执行取货 / 存入。
- 实现：`SDS_BillSupplyPlanner`（两个入口 `TryPlanSlot` / `TryPlanAllowedDefSupply` 共用 `TryPlanFromCandidates`；`NotifyDeposited` 只做池计数）。
- 输入边界：自有量 / 外部可用量 / 周期用量 / 预留只以委托进入；医药等级由调用方把 `SDS_MedicalGate.Allows` 并入 `extraAllows` 注入（S17），规划器不读 `SDS_Host.MedicalCare`。
- 检查方式：`CheckRuleSingleSource.ps1` 的唯一 `// RULE:S16` 标记；`SDS.Rules.Tests/BillMedicalGateTests`（注入拒绝 / 放行 / 无门放行 / 规划器拒料）。

### 1.2 端点候选与排序（原 Core 规则 §12.16 / §12.28）

- 候选 = 全部可用端点（核心 / 锚点 / 带芯片小人 / 机械体）+ 自身端口；原版储存格 / 仓储容器不进入本决议，由 `SDS_DestinationResolver` 在端点胜出后作为对手比较（§12.23）。
- 排序（端点之间，`SDS_EndpointResolver.IsBetter`）= 优先级 → 距离 → `thingIDNumber`；自身端口按真实距离参与，不再自身优先。
- 排序（端点 vs 原版目的地，`SDS_SearchRanker.PreferDestination`）= 优先级 → 距离 → 平局取端点 → `thingIDNumber`。
- 未知第三方容器放行，不因未知而拒绝。
- 判定顺序固定：静态门（运营 / 自身 / 可存 / 有货）→ 排序 → **只对当前胜出候选判可达**；不可达才取下一位。
- 禁止对全部候选逐个预判 `CanReach`；自身端口恒可达，`actor == null` 不做可达判定；可达结果不缓存。
- 实现：`SDS_EndpointResolver`；契约版本时钟在本程序集的 `SDS_EndpointContractCache`。

### 1.3 S17 医药门（实现落点）

- 规范句权威仍在 `SDS.Vanilla/Rules.zh-CN.md` §十；Rules 只负责「不读宿主、由调用方注入 `extraAllows`」这一实现约束。
- 规划器不得自行判定医药等级，也不得复制 `SDS_MedicalGate` 的取值逻辑。

### 1.4 W21 事务足额口径

- 规范句：瞬时事务的执行期足额口径由入口固定：足额则不足整笔回滚，尽量则按实得交付。
- 实现：`SDS_WithdrawTransaction.Begin` / `BeginSingle` 建立预留；执行器按入口传入的 `requireFull` 决定「不足即整笔回滚」或「按实得交付」。
- 检查方式：事务状态机断言；`SDS.Rules.Tests/TransactionTests` 覆盖足额失败与部分成功两条路径。

### 1.5 W27 回滚按通道

- 规范句：回滚必须按事务通道走专用回退（保真 ReturnPreserved、虚拟 ReturnVirtual），禁止通用 Return；逐件失败记日志并兜底落地。
- 实现：`SDS_WithdrawTransaction.Rollback` 按计划通道分派 `SDS_ReturnUtility.ReturnPreserved` / `ReturnVirtual`；不存在通用回退分支。
- 检查方式：回滚路径代码审阅；`SDS.Rules.Tests/ReturnTests` 覆盖两通道回充。

### 1.6 W49 在途物化标记生命周期

- 规范句：在途物化标记只在交付期间有效；本 Mod 发起的任何回充 / 落地 / 失败退路都必须撤销标记，不得让实物以「在途」身份滞留。
- 实现：`SDS_ProducedThingMarker`（`Mark` / `Unmark` / `IsMarked` / 1 天 `Reset` 兜底）；`SDS_ReturnUtility` 的两条回充路径都调用 `Unmark`；静态清理由 `SDS_Rules.RegisterAll` 登记。
- 检查方式：`CheckRuleIds.ps1` 锚点存在；回充路径代码审阅。

### 1.7 事务状态机约束

| 规则 | 具体约束 | 检查方式 |
|---|---|---|
| `Deliver` 只释放预留 | 不搬运、不调交付宿主；调用方承诺真身已交给原版后才能调；`Delivered` 后禁止 `Rollback` | 代码审阅 `Deliver`；E2 `AssertTransactionTerminal` |
| 部分成功不回滚 | `TryWithdrawReservedForJob` 只处理虚拟通道；物化成功即 true；Execute 时消费并释放预留，未交付件由 Vanilla 调 `ReturnThing` | `TransactionTests.ReservedJob_PartialSuccess_ReturnsUndeliveredBack` |
| `Rollback` 按通道 | 保真走 `ReturnPreserved`，虚拟走 `ReturnVirtual`；通道来自 Execute 路径，禁止按 def 反推；逐件 try/catch | `TransactionTests.Transaction_RolledBack_WhenExecuteFails`；审阅 `RollbackMaterialized` |
| 预留只经门面 | 建 / 释放只经 `SDS_Rules.TryReserveForJob` / `TryReservePreservedForJob` / `ReleaseJobReservation`；审计只在 `SDS_Core.AuditReservations`（账本只提供快照与按归属 id 释放，判活事实来自宿主） | `ReservationTests` 六个用例 |

### 1.8 回充 null 规则

1. 先回账本：`ReturnVirtual` → 账本唯一的「实转虚」原子实现 `TryConvertThingToVirtual`（摘出 → 入账 → 销毁；销毁未生效或抛异常则按实际状态回滚并回挂）；`ReturnPreserved` → `TryDepositPreserved`。
2. `map == null`：不落地、不调交付，记 `ErrorOnce`，实物保持原状。
3. `pawn == null` 且 fallback 为 `PawnInventory` / `PawnCarry`：记 `ErrorOnce`，按 `None` 处理。
4. `fallback == None`：直接返回；任何分支都不主动销毁地图实物，回充成功才消化临时实例。

检查方式：`ReturnTests.ReturnThing_MapNull_DoesNotFallback`、`ReturnThing_LedgerFirst_NoDelivery`。来源：《ModuleInterfaces.zh-CN.md》§3.4；`SDS_ReturnUtility.DeliverFallback` 注释。

### 1.9 镜像取用三步

只在 `SDS_WithdrawTransaction.ExecuteMirror` 执行，顺序固定：
① 实际扣账件数 = `min(quantity, mirror.stackCount)`（整堆转换时只能交付镜像堆量，绝不按请求量扣超出部分）；先确认预留足额（`GetReserved(owner, key) >= units`，与 12.33 同口径），再捕获 `vitals`、`ledger.TryDebitVirtual(key, units)`；
② `SDS_WithdrawPlan.physical.TryTakeWholePhysical(key, mirror, out piece)`（整堆）或 `TryTakePhysical(key, mirror, units, out piece)`（部分）；
③ 物理失败且未产出实物 → `ledger.TryDepositVirtual(key, units, vitals)` 回补；成功 → `ConsumeReservation(id, key, units)`。
失败收尾的对账分支：物理层报失败但已产出实物、或产出件数与扣账件数不一致时，先把账面差额对齐到实际件数（`AlignMirrorDebit`），再把实物登记进 `materialized` 交 `Rollback` 整件回充——账实必须回到调用前状态，不留「一账一物」。

附加约束：`key` 与 `physical` 都由事务计划传入，禁止从 mirror 反推、禁止 Rules 现读 `SDS_Host.MirrorPhysical`；物理层不碰账本、不得回调执行器或 `SDS_TakeService`。

检查方式：代码审阅 `ExecuteMirror`；`SDS.Rules.Tests/Core/ReviewRegressionTests.Mirror_PhysicalCountMismatch_RollsBackLedger` 覆盖物理件数不一致时的账面回充，完整镜像 A/B 仍由 Vanilla 覆盖。来源：《ModuleInterfaces.zh-CN.md》§3.4；《ModuleResponsibilities.zh-CN.md》五。

## 二、判定层规则（P4）

### 2.1 W22 外部消费只扣账

- 规范句：外部消费只扣账、不回改预留；可用量 = max(0, 物理 − 预留)，预留可暂时大于物理。镜像的 `Destroy` 不走本口径（W19 fail-closed）。
- 实现：`SDS_AvailabilityImpl.AvailableOf`（`RULE:W22`）；`TryConsumeMirror` 只扣账、不预留（当前无生产调用，镜像 Destroy 走 W19）；账本 `GetAvailable` 不裁剪他人预留。
- 检查方式：自检可用量区间；`LedgerTests.*`；E2 `AssertAvailableWithinTotal`。

### 2.2 W33 武器 / 衣物强制虚拟化

- 规范句：设置「武器/装备强制虚拟化」开启时，`IsWeapon` / `IsApparel` 的物品不再因 `CompArt` 的 def 级存在与已生成实例、`CompColorable.Active`、`CompStyleable.styleDef`、`CompApparelVerbOwner_Charged` 保真；任务、遗物、人格、生成名、独特特性、生物锁、尸衣、燃料、装填仍保真。
- 实现：`SDS_StockRules.RequiresPreservedStorage`、`CanRebuildDef`、`IsProtectedThing`、`HasInstanceNonKeyState`；开关只缩减免判据，不新增第二通道。
- 边界：`CompApparelVerbOwner_Charged` 是「充能」，开关开启时忽略；「装填」指 CE 已装弹药（外部 `SDS_PreserveHooks` 事实），Rules 不读宿主。
- 检查方式：`CheckRuleIds.ps1` 锚点存在；`MigrationTests`。

### 2.3 W35 逐件跟踪一律保真

- 规范句：凡 `ThingDef.IsStudiable` 的物品一律走保真通道，且先于四类默认虚拟判定；该判据只问原版能力，不列举 mod / defName / 组件；保真真身不参与同键合并。
- 实现：`SDS_StockRules.IsTrackedPerInstance`（`RULE:W35`）；`CanRebuildDef` 先否决逐件跟踪；`SelfCheck` 校验不变式。
- 检查方式：`CheckRuleIds.ps1`；`CheckRuleSingleSource.ps1` 唯一 `// RULE:W35`。

### 2.4 W56 取用夹取唯一实现

- 规范句：取用夹取（= min(需求, 可用量)）只有一处实现：`SDS_Availability.ClampTakeToAvailable(int wanted, long available)`；任一非正即 0（表示本次不取）。所有取用入口（Job 物化 / 装载单趟 / 编成差额 / 手动输出 / 交易与发射 / 兼容适配器）必须调用它；可用量本身只来自 `GetAvailableCount`（W22）或 `GetTradableCount`（R15），不得自算、不得内联同式。
- 实现：`SDS_AvailabilityImpl.ClampTakeToAvailable`（`RULE:W56`）。
- 检查方式：`CheckRuleSingleSource.ps1` 重复算式扫描；`TripCountTests`。

### 2.5 R15 / R17 读侧可用量

- R15 规范句：交易 / 捐献 / 发射读「物理存量 − 事务预留」，不因品类禁用归零；生产 / 取用仍禁用归零。
- R17 规范句：G 关闭（世界上没有可用核心）时所有读侧可用量归零；账本数据保留，不属于品类禁用。
- 实现：`SDS_AvailabilityImpl.GetTradableCount`（`RULE:R15`）/ `GetAvailableCount`（`RULE:R17`）；全局门读 `SDS_RulesImpl.Available`。
- 检查方式：自检可用量区间；`LedgerTests.*`。

## 三、迁移期约束

1. **只搬家、不复制**：任一时刻同一条规则只存在于一个实现文件；Core 删除与 Rules 新增必须在同一阶段完成。
2. **不设转发器**：Core 不引用 Rules，因此不保留旧门面的转发实现；消费方在同一阶段切到 `SDS_Rules`。
3. **规范句不变**：迁移只改《RuleIndex.zh-CN.md》的「权威文档 / 代码锚点 / 唯一实现登记」，不改规范句；`// RULE:<ID>` 标记随实现移动，且全仓仍恰有一处。
4. **Vanilla 接触约束不迁入**：补丁挂点、Job / UI 时序、原版 API 约束继续由 `SDS.Vanilla/Rules.zh-CN.md` 定义，Rules 不引用 Harmony / UI / Job。
5. **依赖顺延已结束**：P3 流程层经 Core 友元读账本；P4 判定层已在同一程序集内，Core 只保留账本与 `SDS_StockData` 数据原语，不再有跨阶段依赖。

## 四、检查方式

- 已迁入的 S16 / W21 / W22 / W27 / W33 / W35 / W49 / W56 / R15 / R17 由 `CheckRuleIds.ps1` / `CheckRuleSingleSource.ps1` 校验；端点规则无独立 ID，由 `SDS.Rules.Tests/EndpointTests` 覆盖。
- 账本侧规则（W26 / W34 / E1 / E2）仍按《RuleIndex.zh-CN.md》登记在 `SDS.Core/Rules.zh-CN.md` 校验。

## 五、补充口径（自 Core 规则 §十二 迁入）

> 以下编号保留原 Core 规则 §十二 的编号，便于历史引用；冲突时以本文件为准。

### 12.3 多趟 = 多事务
- 每一趟独立开事务，事务不跨趟挂起、不复用。
- 负重 / CE 容量必须在 `Begin` 前判定；装不下直接失败，不得先开事务再硬塞。


### 12.4 Job 挂起未终止时的地面物化
- 可达：继续使用该实物。
- 已销毁：不回加，不按 def 重建。
- 不可达：Job 失败，实物留在原地。
- 进入他人背包：放弃该件，不追回。
- 一律不因位移回滚事务。


### 12.5 三重预留互斥
同一件物不得同时持有「镜像预留 + 账本预留 + 真身预留」三份预留；已有其一即视为已占用。


### 12.6 物化标记范围
- 只打在地图 / 手 / 背包 / 地面四种落点。
- 交易、编成、装载不打标记。
- 以 1 天寿命兜底，到期自动失效。


### 12.7 全局开关关闭时的写侧
全局开关关闭时 `Begin` / `Execute` 一律拒绝，不只是读侧归零。
存入（`CanDeposit` / `TryDeposit`）不受全局开关约束：G 只影响读侧与取货事务，已存数据必须保留。


### 12.8 三层口径
- `Availability`（可用量）→ `Plan`（计划）→ `Execute`（执行），逐层收窄。
- key 级预留只是闸门，不是授权；授权来自 `Plan` 与 `Execute` 的复核。
- 按 def 选键只有一个实现（精确键 → 可用量最大 → 保真回退），读口径分两档：生产 / 取用走 `TryFindWithdrawableKey`（禁用归零），交易 / 捐献 / 发射走 `TryFindTradableKey`（不因禁用归零，R15）；两档共享同一选择顺序，不得各写一份。


### 12.9 `ConsumeReservation` 是原子三步
`ConsumeReservation` 三步原子完成，且顺序固定：先扣账、后消费预留；失败不得留下半消费状态。


### 12.11 `SplitOff` 后 `TryAdd` 失败的阶梯
- 七步阶梯：`TryAdd` → 吸收 → `TryPlace` → `Spawn` → 记 `partialRemaining` → 退避 → 绝不悬空。
- 任一步成功即止，不得跳步；镜像禁止 `SplitOff`。


### 12.12 集合级容量预检
构建清单后做一次集合级容量预检；它只降低失败概率，不承诺零失败，逐件写入仍须容忍失败。


### 12.13 平局表
端点与原版候选平局时取端点，这是**有意偏差**；原版只在严格大于时才取容器。


### 12.14 B1 完整判据
- 判据 = 四类默认虚拟（跳过 def 级与实例级非键状态）+ def 级 5 项 + 实例状态列举 + 身份型受保护实例（`IsProtectedThing`）；四类不是绝对虚拟，任务 / 圣物 / 人格 / 生成名 / 艺术等仍保真。
- 「版本」不是判据。
- 补名单优先用原版能力位，不列举 mod / defName。


### 12.15 判据不追溯
- 旧虚拟条目只按 `key.preserved` 判定，不用新判据回溯。
- 同一 def 的虚拟与保真双通道可并存。


### 12.17 读流水线（0–6）与只读边界
- 0 `ReadVersions` 快照（Map / Stock / Endpoint / Position / StorageSettings / Preserved）；1 `Enumerate(kind)` 薄适配器，不可统一；2 `PureProjection` 纯函数（Source：有效格 / 有效距离 / 候选类型；Destination：有效格 / `Accepts` / 优先级 / 容量）；3 `ReachabilityProjection`，键 = map + root + traverseParms + reachVersion，TTL 30；4 `Filter` 严格按原版顺序；5 `Rank` 条件化排序键；6 `Freeze` 只读结果 + 版本快照，不预留、不扣账、不物化。
- 字段分组：`Scope`（map / root / actor / faction）、`Query`（kind / def / ThingRequest / filter / group / quantity / preserved）、`Projection`（scope / peMode / traverseParms / maxDistance）、`Policy`（validator / priorityGetter / tieBreaker）；`priorityGetter` 只在 Rank 对每候选读一次并缓存，不在 Filter 调用。
- **R1 读只读**：不改库存、预留、实体持有关系，也不写交易登记表 / 保真桶映射；validator 写 `JobFailReason` / 诊断不算破坏只读。读会话止于 Freeze；执行复检是消费者契约，复检失败回「重新决议」，不得在执行器内改选来源或交付模式。


### 12.18 事务不变量（W1 / W3 / W4 / W6–W16，仍适用）
- W1 一次事务只有一个扣账点，同一数量只扣一次；一个 Job 可由多个事务组成，每个事务独立预留、扣账、回滚。
- W3 交付模式在 `Begin` 时固定，执行期不得切换；所有事务入口（`Begin` / `BeginJobAdopt` / `SDS_API` 取货）一律拒绝 `VirtualHandoff`，该枚举仅作历史兼容。
- W4 失败只回滚尚未交付的数量，绝不复制、不丢失。
- W6 搜索层只负责可见性，不物化、不扣账。
- W7 交易 / 编成 / 装载的数量结算必须走事务；不建跨对话框预留。即时结算走各自的直接入账路径，不使用 `VirtualHandoff` 事务（枚举仅历史兼容）。
- W8 全局开关关闭时 `Begin` / `Execute` 一律拒绝。
- W9 交付模式由原版契约决定，禁止所有 Job 一律进背包或一律落地。
- W10 多 key 全有或全无；事务内交付完成即关闭；多趟 = 多事务，不得跨趟长期持有；容量不足不得硬塞。
- W11 多 key 预留必须原子：先全部校验通过，再统一写入，不允许部分持有。
- W12 所有扣账走预留感知执行器，禁止零散 `TryWithdraw`；唯一例外是外部消费确认。
- W13 地面物化由事务持有精确 `Thing` 引用，回滚精确到件。
- W14 事务对象不落盘；落盘的是账本预留与账本内容。读档后由 Job 用 `jobLoadID` 重新派生事务；挂起预留保留，真正结束由 Job 的 `FinishAction` / `SDS_Core.AuditReservations` 释放。
- W15 作用域分 Instant / Job / Session，`Begin` 时固定；Session 已并入 Instant，现存两类。
- W16 存档只保存可验证的预留（归属 id / key / 件数 / pawn 引用）；保真真身由 Core 私有 owner 自身落盘；plan 由 Job 读档后重建，不单独存档。


### 12.19 多 key 原子预留（补充 W10/W11）
- 先对全部 key 做一次总量校验，全部通过才统一写入；不依赖遍历顺序、不会部分持有。
- 任一 key 预留失败即释放已预留的全部 key。
- `Execute` 中某 key 扣账失败则回充已扣 key、整笔事务失败。
- 禁止「钢预留成功、零部件失败，却继续拿钢」。


### 12.20 保真取用四原则与不重建
- ① 只在原版明确要取用的那一刻动手；② 先按点名计划汇总到 **key 级预留**（账本预留不区分 Thing），执行时按 key 校验 `Σplan ≤` 预留并逐件校验真身仍在 owner；③ 真身原样交付，不重建；④ 任一件不在 owner / 件数不足即整笔回滚。
- `TryWithdrawPreservedPlan` 从 `preservedOwner` 摘出精确真身 → **不重建 InnerPawn / InnerThing / GeneSet** → 失败回滚。
- `ExecutePreservedExact` 执行前必须按 key 汇总计划件数，逐 key 校验 `GetReserved(owner, key) >= Σplan`；不足即整笔失败并记 `Fail`，不得只依赖「同一份 plan 派生预留」的隐式闭合。


### 12.21 三种交付契约
- Hand：真身进 `Pawn_CarryTracker` / `Pawn_InventoryTracker`（DoBill 原料、运输装载、保真原料）。
- Container：真身经 `ThingOwner.TryAdd` 进目标容器（棺材、运输舱、基因库、商队背包）；装备走 `AddEquipment → TryAdd`，属 Container。
- SpawnedTarget：只在显式登记时启用（逃生门）。只有该形态允许在派活期把镜像物化到端点并改写 `targetA/B/C`；当前仅瓶喂登记。默认 Hand 由 W58（数量缺失）/ W59（消费点物化）/ 取货原语在消费点换真身。


### 12.22 读侧失败码优先级
`GetFailure(key)` 顺序固定：`key == null || !Resolved` → `UnresolvedDef`；`grid == null` → `Insufficient`；`IsForbidden` → `Forbidden`；`GetAvailableCount <= 0` → `Insufficient`；否则 `None`。即 `UnresolvedDef` 最高，`Forbidden` 高于 `Insufficient`；禁用时可用量返 0，但不影响容量 / 财富 / 交易。


### 12.23 存入候选池与自身端口
- 候选池 = 原版 `SlotGroup` 储存格 + 原版非 `SlotGroup` 中确认为仓储类的容器 + 全部可用端点（核心 / 锚点 / 带芯片小人 / 机械体）+ 自身端口。
- 自身端口是普通候选，按真实位置参与排序；端点决议胜出后 W62 把目的地折叠为自身端口（`SDS_SelfDepositFold`，不改变优先级比较），物品到手后立刻经唯一存入入口入账。


### 12.24 容量归属组粒度
- 虚拟与保真共用一个「品类数」池；容量上限只拒绝**新品类**，同一（已有）品类可无限堆叠。
- 容量被下调到 `UsedCapacity > TypeCapacity` 时不驱逐存量、也不拒收已有品类；只有新品类被拒绝。
- 容量按 `CategoryKeyOf` 品类键去重：def + 材质 + 品质 + 通道；同一品类多件 / 多真身只占一个名额。
- 「归属组」是历史术语，不作为独立容量维度；若未来要跨通道合并名额，另立 L2 口径并新增 `CapacityKeyOf`，不得改 `CategoryKeyOf`（它还服务展示分组）。


### 12.25 退避与落点顺序
- 先判收不收再摘物品；失败只能「留手 / 回充 / 落地」。
- Core 不执行落点行为：`GenPlace` / `GenSpawn` 兜底只允许在 Vanilla 的 `SDS_PlacementUtility.TryPlace`（`Exact` / `NearPawn` / `RandomNear`，硬前置 `!Spawned`）；金库 / 镜像落点例外。端点缓冲 `EjectToMap` 与取出侧 `ReturnToGridOrDrop` 同属 Vanilla。
- 保真回滚走 `ReturnPreserved`（先 `TryDepositPreserved`，不过全局筛选）；虚拟走 `ReturnVirtual` / `ReturnVirtualCount`。
- 失败落地不得打 `SDS_ProducedThingMarker`。


### 12.28 端点选择的判定顺序（实现已迁 SDS.Rules）
静态门、排序、只对胜出候选判可达的顺序已随 `SDS_EndpointResolver` 迁至 `SDS.Rules/Rules.zh-CN.md`；Core 不再执行端点选择。


### 12.32 异常与失败路径的数量守恒

- 保真取出（`TryWithdrawPreservedPlan`）整段 try/catch：异常必须整笔回滚已摘出真身，并记 `Fail`（def + thingID + index）。
- 保真回滚（`RollbackPreserved`）与拆出（`TakePreservedUnits`）必须检查所有权转移 / 移除结果；失败记 `Fail`，不得静默丢件。
- W34 迁移异常按 `thing.Destroyed` 实际状态结算：已销毁 = 迁移成立；未销毁 = 撤回账面并尽量回挂真身，回挂失败记 `Fail`。
- 读档时真身入容器失败不得丢弃：进入 `pendingPreservedRestore`，由每次巡检重试；未回账期间计入自检失败项。
- 事务物化必须在 `TryWithdrawVirtual` 成功后的第一行登记 `materialized`（`ExecuteVirtual` / `ExecuteVirtualReserved` 同口径）；接管既有预留的执行器 catch 只调 `ReclaimMaterializedWithoutRelease`（回充实物、不释放预留），预留仍由 Job 的 `FinishAction` 清理。


### 12.33 数量下限与预留消费

- `TryDepositVirtual` 只接受虚拟键；`key.preserved == true` 直接失败并记 `Fail`（防止保真键混入虚拟表后在其他口径「消失」）。
- `DebitVirtual` 不允许把账面扣成负值：超出部分按现有量截断并记 `Warn`。
- `ConsumeReservation` 返回实际消费件数；`ExecuteVirtual` / `ExecuteVirtualReserved` 必须比对「物化件数 == 实扣预留」，不等即整笔回滚。
- 预留条目是 int：单个 key 的请求总量超过 `int.MaxValue` 时整笔失败并记 `Fail`，禁止静默截断。
- 镜像取出（`ExecuteMirror`）在扣账前必须确认 `GetReserved(owner, key) >=` 实际扣账件数（`min(quantity, mirror.stackCount)`）。
- `TryReserveForJob` 的幂等分支回填既有预留数量，不按后续更小的 `maxStackSpace` 缩量；调用方在物化 / 交付前必须自行夹取，或释放后按新空间重建预留。

