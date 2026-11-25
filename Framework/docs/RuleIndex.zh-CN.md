# 规则索引（派生文档，不定义规则）

> **本文件不定义任何规则**，只登记「规则 ID ↔ 规范句 ↔ 权威文档 ↔ 代码锚点 ↔ 机器校验」。
> 规则正文以「权威文档」为准，全部位于本框架（`Framework/docs/`）内；旧框架自 2026-11-12 起为纯历史存档，不再被任何脚本或文档引用。
>
> 约定：
>
> - **领域不变量**沿用 **R / W / S** 系列；**工程约束**用 **E** 系列。
> - 每条规则必须有**一行逐字稳定的规范句**；**ID 与规范句任一变化都算 L2**（见《DesignPrinciples.zh-CN.md》§三）。
> - 新增规则顺序：先在权威文档写规范句并标注 ID → 在本表登记 → 再写代码锚点 → 再补机器校验。
> - `机器校验` 为 `—` 表示当前只能靠人工审阅；不得把「没有校验」写成「已校验」。
> - 不新造并行前缀：S / W / R 只用于原五轴领域，E 只承接工程约束，不参与 S / W / R 映射。
> - 口径冲突时以本表「权威文档」列为唯一仲裁；规则归属以《ModuleRules.zh-CN.md》十二「文档地图」为准。
> - W / R / S 的历史渊源与取舍见各模块 `DesignDecisions.zh-CN.md`；本表只服务现行维护。

| ID | 规范句（逐字稳定） | 权威文档 | 代码锚点 | 机器校验 |
|---|---|---|---|---|
| R15 | 交易 / 捐献 / 发射读「物理存量 − 事务预留」，不因品类禁用归零；生产 / 取用仍禁用归零。 | SDS.Rules/规则 §一 | `SDS_Availability.cs` | 自检：可用量区间 |
| R16 | 读路径（信标 / 只读枚举）不得写交易登记表或保真桶映射；交易登记只由 BuildTradePreservedDisplays 做。 | SDS.Vanilla/规则 §十 | SDS_Trade.cs | — |
| R17 | G 关闭（世界上没有可用核心）时所有读侧可用量归零；账本数据保留，不属于品类禁用。 | SDS.Rules/规则 §一 | `SDS_Availability.cs` | — |
| S15 | 镜像与金库不进入 NPC 的偷窃目标与偷窃价值计算：`ThingRequestGroup.HaulableEverOrMinifiable` 不补镜像候选；`StealAIUtility.GetValue` 对镜像 / 金库读作 0；真实物品的偷窃、财富、交易、枚举不变。 | SDS.Vanilla/规则 §十 | SDS_Search.cs | `CheckPatchInventory.ps1`：挂点存在 |
| S16 | 第三方补料（配方 / 网络订单）：第三方自有库存优先，SDS 只补缺口；原料 def 选择、required / fromOwn / shortfall 计算与预留一次只在 `SDS_BillSupplyPlanner` 实现，适配器只提供事实并执行取货 / 存入。 | SDS.Rules/规则 §一 | `SDS_BillSupplyPlanner.cs` | `CheckCompatSurface.ps1`：调用图 / 入口 |
| S17 | 本 Mod 为账单提供的医疗候选必须通过原版 `WorkGiver_DoBill.GetMedicalCareCategory(billGiver)` 的 `AllowsMedicine`；该判据覆盖原版全部选料分支（含 `IHaulSource` 递归）与本 Mod 兜底，`IsUsableIngredient` 为唯一校验点；被拒绝时不得补料，并保留原版缺料列表以给出 `NoMedicineMatchingCategory`。 | SDS.Vanilla/规则 §十 | SDS_BillSupplyPlanner.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| S18 | 设置「仅玩家派系可取用超维库存」开启时，非玩家派系（含野生动物、袭击者、访客、囚犯、任务寄居者）不得从超维库存取用；镜像补候选、镜像可达性、Job 目标物化、携带、装备 / 衣物 / 背包接收与端点取出统一经 `SDS_AccessRules.BlocksNonPlayer` 拒绝；玩家派系与设置关闭时行为不变。 | SDS.Vanilla/规则 §十 | SDS_AccessRules.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W21 | 瞬时事务的执行期足额口径由入口固定：足额则不足整笔回滚，尽量则按实得交付。 | SDS.Rules/规则 §一 | `SDS_WithdrawTransaction.cs` | 断言：事务阶段 |
| W2 | 投影 owner 的取用 / 物化统一走取货主干（`SDS_TakeService` / 执行器），不得自行扣账。 | SDS.Vanilla/规则 §十 | `SDS_EndpointHandle.cs` | `CheckPatchInventory.ps1`：挂点存在 |
| W5 | 镜像作为 Job 目标 / 携带 / 拆分目标时，取真身发生在消费接缝（W58 数量补齐、W59 消费点物化）或取货原语（SplitOff / CarryTracker / ThingOwner），不在 `StartJob` 派活期物化。 | SDS.Vanilla/规则 §十 | — | `CheckPatchInventory.ps1`：挂点存在 |
| W18 | 未 `Unmark` 的镜像拒绝 `DeSpawn`；地图卸载窗口临时放行。 | SDS.Vanilla/规则 §十 | — | `CheckPatchInventory.ps1`：挂点存在 |
| W19 | 镜像被外部 `Destroy` 时 fail-closed：拒绝销毁并一次性告警；不得按镜像当前数量做整品类扣账；内部清理必须先 `Unmark`。 | SDS.Vanilla/规则 §十 | — | `CheckPatchInventory.ps1`：挂点存在 |
| W20 | 镜像不参与合并语义；吸收先取货。 | SDS.Vanilla/规则 §十 | — | `CheckPatchInventory.ps1`：挂点存在 |
| W22 | 外部消费只扣账、不回改预留；可用量 = max(0, 物理 − 预留)，预留可暂时大于物理。镜像的 `Destroy` 不走本口径（W19 fail-closed）。 | SDS.Rules/规则 §一 | `SDS_Availability.cs` | 自检：可用量区间 |
| W23 | 交易成交前按行校验：虚拟按 key 共享预算，保真按自身分组桶，本地 / 远程 / 聚落 / 商队同一套。 | SDS.Vanilla/规则 §十 | SDS_Trade.cs | — |
| W24 | 保真交付只从同一分组桶摘真身；桶内不足整笔拒绝，禁止跨桶顶替。 | SDS.Vanilla/规则 §十 | SDS_Trade.cs | — |
| W25 | 编成差额与装载差额是两个不同口径：编成（出发校验）= `CountToTransfer − 行内未被任何 holder 持有的实物量`（不含展示物）；装载（派活）= `CountToTransfer − 该容器原版在途 Job 已认领量`（`HaulToTransporter` / `HaulToPortal` 的 `initialCount`，不含本 Mod 待派活）。两者不得互换：装载不以地图实物量扣减，因为原版只在找不到可装实物时才让超维空间接管；编成阶段没有该容器的原版在途 Job，故第二项为 0。 | SDS.Vanilla/规则 §十 | SDS_CaravanTransport.cs | — |
| W26 | 旧档 → 新版必须能读；数据不丢优先于格式不变；无法表达的内容先回收 / 落地并记日志；不承诺新版 → 旧版。 | SDS.Vanilla/规则 §十 | `SDS_Ledger.cs` | — |
| W27 | 回滚必须按事务通道走专用回退（保真 ReturnPreserved、虚拟 ReturnVirtual），禁止通用 Return；逐件失败记日志并兜底落地。 | SDS.Rules/规则 §一 | `SDS_WithdrawTransaction.cs` | — |
| W28 | 编成 / 装载的超维差额校验只处理「物品 + 带本 Mod 展示物」的行；pawn 行与纯原版行交原版 CheckForErrors。 | SDS.Vanilla/规则 §十 | SDS_CaravanTransport.cs | — |
| W30 | 直接消费 Job 的镜像目标不在派活期物化；默认交付形态 = Hand。唯一例外：显式登记为 `SpawnedTarget` 的逃生门 Job（当前仅 `BottleFeedBaby`）按缺口 `WillIngestStackCountOf` 物化到端点、不夹携带量；逃生门内 `job.count ≤ 0` 按 1 件；未登记 Job 的数量缺失由 W58 处理，不得回落成 1；显式 `count = 1` 由调用方决定，不得改写。 | SDS.Vanilla/规则 §十 | SDS_JobTargets.cs | `CheckRuleIds.ps1`：ID/锚点存在；`TripCountTests`：夹取 / 0 语义 |
| W31 | 可安装缩装的「输出」固定 1 件：行内记录唯一时跳过对话框直接进入安装放置，存在不同材质／品质记录时才弹记录选择；放置取消不取货。 | SDS.Vanilla/规则 §十 | SDS_UI.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W32 | 安装蓝图只由 `SDS_InstallBlueprintGuard` 创建并使用放置器当前朝向；放置器一次只处理单格，禁止拖拽为同一真身重复建蓝图。 | SDS.Vanilla/规则 §十 | SDS_PreservedUsage.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W33 | 设置「武器/装备强制虚拟化」开启时，`IsWeapon` / `IsApparel` 的物品不再因 `CompArt` 的 def 级存在与已生成实例、`CompColorable.Active`、`CompStyleable.styleDef`、`CompApparelVerbOwner_Charged` 保真；任务、遗物、人格、生成名、独特特性、生物锁、尸衣、燃料、装填仍保真。 | SDS.Rules/规则 §一 | `SDS_StockRules.cs` | `CheckRuleIds.ps1`：ID/锚点存在 |
| W34 | 该设置只在玩家确认后转换一次存量，只转换转换后不再需要保真的武器/衣物；关闭不回溯，已虚拟条目需取出后重新存入才按 B1 重新分流；不得在读档/启动时静默转换。 | SDS.Core/规则 §十一 | `SDS_Ledger.cs` | `CheckRuleIds.ps1`：ID/锚点存在 |
| W35 | 凡 `ThingDef.IsStudiable` 的物品一律走保真通道，且先于四类默认虚拟判定；该判据只问原版能力，不列举 mod / defName / 组件；保真真身不参与同键合并。 | SDS.Rules/规则 §一 | `SDS_StockRules.cs` | `CheckRuleIds.ps1`：ID/锚点存在 |
| W36 | 代理镜像不得登记进 `Find.StudyManager` 的可研究缓存；真实物品路径不变。 | SDS.Vanilla/规则 §十 | Patch_Mirrors.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W37 | 金库必须锚定本图可用端点（建筑优先，其次带芯片 Pawn）；只在锚点身份 / 建筑位置变化或金库格被外来 SlotGroup 占用时重锚，旋转与 Pawn 行走不触发；无可用端点时先撤镜像，仍有结构性端点建筑则保留金库，否则宽限 250 tick 后移除金库；搬迁失败保留旧格不重试；锚点不写存档，读档后首次同步对齐一次。 | SDS.Vanilla/规则 §十 | SDS_Buildings.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W38 | 装载行只从该行展示物所属的原版分组桶内取真身（已登记桶直接用；不登记桶的 autoload 行按「展示物 + `TransferAsOne(PodsOrCaravanPacking)`」解析出它自己的那一个桶，不得按键聚合多个桶）；HasJob 与 JobOn 使用同一「桶内可计划单位 > 0 且该键可用量 > 0 且能决议出取物点」判定，不可服务就跳过该行，不得据此放行整台运输舱。 | SDS.Vanilla/规则 §十 | SDS_TransportVirtual.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W39 | 读档恢复装载行时，欠量取该行 CountToTransfer − 未装实物；展示物绑定同键且未被其它行占用的分组桶，不得按键取第一个桶，也不得多行共享同一展示物。 | SDS.Vanilla/规则 §十 | SDS_CaravanTransport.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W40 | 装舱记账必须按「真身 → 原版装载行」的身份定位行：优先用该行展示物登记桶包含该真身，其次用封存品类键等于真身键；命中后把真身放回该行 things，扣数、移除行与装载完毕消息仍由原版执行。运输舱 / 传送门 / 穿梭机共用同一身份规则，不得依赖 TransferAsOne 的同 def 回退定位行。 | SDS.Vanilla/规则 §十 | SDS_TransportVirtual.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W42 | 装载派活必须使用同一 tick 的判定快照：HasJob 生成并登记选择结果，JobOn 只消费该快照；不得在 JobOn 重新求值可变运行期状态（含 `FindThingToLoad`）。 | SDS.Vanilla/规则 §十 | SDS_TransportVirtual.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W43 | 装载计划只在「本 tick 新建」或「仍是某个小人的当前 Job（含其待执行 Job 队列）」时视为占用；否则在选行时回收；`TryMakePreToilReservations` 失败必须显式释放计划；**落点格守卫的 `JobOnThing` 探针期间不得登记计划**（`SDS_CellOnlyWorkGiverPatches.IsProbing` 闸门）。 | SDS.Vanilla/规则 §十 | SDS_TransportVirtual.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W44 | 装载对话框刷新只清理展示物身份注册，不得清空其它容器的派活计划、同 tick 快照或保真桶绑定；全局清理只允许由 SDS_StaticState.ResetAll 触发。 | SDS.Vanilla/规则 §十 | SDS_TransportVirtual.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W45 | 保真桶身份按「桶内任一真身重叠即同桶」判定；对话框注入去重、装载中重开复用、跨容器展示物隔离共用这一判定，不得用桶首元素代表桶身份。 | SDS.Vanilla/规则 §十 | SDS_TransportVirtual.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W46 | 装载选件的数量口径统一为「预留后可用量」（SDS_Availability：Total − Reserved，禁用归零）；选行门槛、单趟夹取、对话框注入、穿梭机 autoload 共用同一入口，不得另算物理存量。单趟夹取 = `min(欠量, 携带上限 MaxStackSpaceEver) ∩ 可用量（唯一实现 W56）`：携带部分唯一实现 `ClampCarry`（`ClampLoadTake` 与派活驱动器共用），可用量只经 `SDS_Availability`（`LoadAvailable`）；不得内联同式。装载是空手规划口径，故用携带上限而非剩余空间（区别于 W30 的 `AvailableStackSpace`）。 | SDS.Vanilla/规则 §十 | SDS_TransportVirtual.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W47 | 装载途中读档后，派活身份以预留账本为唯一权威：驱动器必须在物化前按 `job.loadID` 的预留键、从容器内该键所属的那一个原版分组桶重建保真计划；运行期计划 / 待用键不跨存档，不得退化成按 def 任取一件，也不得按键聚合多个桶。同键对应多个不同桶、找不到桶或桶内可计划单位不足额时必须 fail-closed（库存与预留不动）。 | SDS.Vanilla/规则 §十 | SDS_TransportVirtual.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W48 | 装载行的读档恢复对运输舱与传送门同一口径：展示物引用不跨存档，按身份 + 欠量重建；读档会丢弃纯超维行的容器必须按品类定位或按欠量重建行，不得按行号硬套。 | SDS.Vanilla/规则 §十 | SDS_VanillaState.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W49 | 在途物化标记只在交付期间有效；本 Mod 发起的任何回充 / 落地 / 失败退路都必须撤销标记，不得让实物以「在途」身份滞留。 | SDS.Rules/规则 §一 | `SDS_ProducedThingMarker.cs` | `CheckRuleIds.ps1`：ID/锚点存在 |
| W50 | 第三方自建装载清单的库存可见性只能由清单构建处的展示物注入提供；适配器必须经 `SDS_API` 内部桥调用唯一注入实现，不得在适配器内新增取货、预留、扣账或数量口径规则。 | SDS.Vanilla/规则 §十 | SDS_ModCompat_CeleTechShuttle.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W51 | 多槽配方的保真缺口只在原版搜索失败后处理：按原版 `missingIngredients` 逐槽从保真通道取真身、物化到工作台交互格（**不打来源排除标记**：同一 tick 由原版 Job 接手，取消清单后允许端点自动吸回），同一 tick 重跑原版搜索，之后由原版搬运与入容器；禁止直接写容器、禁止另算件数、禁止恢复批量预留 / 批量物化。 | SDS.Vanilla/接口 §4.9 | SDS_PreservedBill.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W52 | 设置「允许优先级流出」开启且「最高优先级」关闭时，镜像进入原版搬运候选；只有存在严格更高优先级且允许进入的储存目的地时才流出，账本只消耗 total − reserved，目标格 / 容器与镜像 Job 预留遵循原版；设置关闭或「最高优先级」开启时不流出。 | SDS.Vanilla/规则 §十 | SDS_Mirrors.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W53 | 镜像代表账本本身，不是可再入库的实物：`SDS_EndpointUtility.IsDepositable` 是「可存入实物」的唯一判据，端点接收（`IHaulDestination.Accepts`）、存入竞争与手动存入一律拒绝镜像；镜像的存储事实由每图仓库代理唯一提供（`CurrentHaulDestinationOf` 返回代理，`CurrentStoragePriorityOf` 取存档级优先级，`IsInAnyStorage` / `IsInValidStorage` 为真），W52 的「严格更高优先级才流出」据此成立。 | SDS.Vanilla/规则 §十 | SDS_EndpointUtility.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| E1 | 运行期静态缓存必须且只能在 SDS_StaticState.ResetAll 登记清理。 | 诊断覆盖清单 §二·M | `SDS_Core.cs` | 启动日志：清理项数 |
| E2 | 开发期断言只记 Error、不抛异常；仅 DEBUG 构建且开发者模式生效。 | 诊断覆盖清单 §二·M | SDS_Invariants.cs | 启动日志：自检摘要 |
| E3 | L2 及以上变更必须先给出影响面清单，并回填验证证据（编译结果 + 自检结果）。 | 设计原则 §三 | — | 人工 |
| E4 | 变更分级按客观工件：L1 公开面清单 diff 为空、L2 模块间公共 API 或规则语义变化、L3 准则 / 存档 / 对外五轴契约 / 分级表本身。 | 设计原则 §三 | — | 人工 |
| E5 | Version 变更即兼容性声明；无外部采用可直接 break；有采用需保留薄转发至少一个大版本。 | 公共API §3.8 | SDS_API.cs | — |
| E6 | 兼容层只允许引用 `SDS_API`、`SDS_Compat*`、`SDS_Diag` 与 `SDS.Contracts` 的公共类型；不得引用本 Mod 内部实现类。 | 公共API §4.5 | — | `CheckPatchInventory.ps1`：`[兼容直调]` allowlist |
| E7 | 每个第三方入口只有一个写接管路径；只读投影可与适配器翻译共存，但不得有第二条扣账 / 派活 / 回滚路径。 | 公共API §4.3 | — | `CheckCompatSurface.ps1`：入口唯一 + 人工双触发审计 |
| S2 | 同一候选 + 同一投影 = 所有搜索适配器得到相同距离 / 可达 / 仓储状态。 | SDS.Vanilla/规则 §九 | — | 代码审阅 |
| S4 | 不允许任何搜索适配器重复实现距离公式；访问点折算只经唯一实现。 | SDS.Vanilla/规则 §九 | — | 代码审阅 |
| S9 | 区域优先只约束能控制阶段与候选集的适配器（Prefix 型）；`ClosestThingReachable` 与 `ClosestThing_Regionwise_ReachablePrioritized` 是 Postfix 型，无法区分 Region / Global 阶段，统一按 Global 语义与原版 best 二选一（比较键 = `priorityGetter → effectiveDistance → tieBreaker`），允许在镜像严格更优时替换原版非空区域结果；这是记录在案的偏差。若将来需要严格区域优先，只允许新增 Prefix 型适配器，不得在 Postfix 内复制区域 BFS。 | SDS.Vanilla/规则 §九 | — | 代码审阅 |
| S10 | Source / Destination 的 EffectiveCell 分开；source 的镜像 / 保真依赖 root。 | SDS.Vanilla/规则 §九 | — | 代码审阅 |
| S12a | 无 `priorityGetter`（`ClosestThingReachable`）：Postfix 型适配器只做「补候选 vs 原版 best」的二选一比较，不重排区域候选、不跨 Region / Global。 | SDS.Vanilla/规则 §九 | — | 代码审阅 |
| S12b | 有 `priorityGetter`（`ClosestThing_Regionwise_ReachablePrioritized`）：同样只做二选一比较；比较键 = `priorityGetter → effectiveDistance → tieBreaker`。 | SDS.Vanilla/规则 §九 | — | 代码审阅 |
| W17 | 适配器只做 Intent → Query 转译和只读委托查询；`SDS_WithdrawPlan` 由 `Decide(query)` 生成；不得自行选端点、决定可用数量、直读 `preservedOwner`、创建事务或预留。 | SDS.ThirdModCompat/规则 | — | `CheckCompatSurface.ps1`：调用链 / 入口 |
| E8 | 登记在案的模块，其顶层 `public` 类型只允许声明在该模块登记的公开面文件里；登记表以实现侧脚本为唯一权威，未登记即泄漏。 | 模块规则 §五 | `CheckAssemblyBoundaries.ps1` | 脚本 §4.5 |
| E9 | 公开面文件只做契约声明与转发，不承载领域决策；控制流只允许出现在对外准入门 `SDS_API`。 | 模块规则 §五 | `CheckAssemblyBoundaries.ps1` | 脚本 §4.8 |
| E10 | 模块间公共 API 变更（公开面清单 diff 非空）一律 L2；清单 diff 为空为 L1；对外五轴契约与 `Version` 为 L3。 | 设计原则 §三 | — | `DumpPublicSurface.ps1 -Check` |
| W54 | 镜像真身位置（隐藏金库）永远不是取货点：镜像寻路、携带与存入执行器在任何情况下都不得按真身坐标走位；无可用端点时按目标不可达结束 Job（fail-closed）；不在端口范围内的取货一律拒绝；镜像不得经 `ThingOwner` 进入小人所属 owner（背包 / 装备 / 衣物），第三方搬运（含 PUAH）的候选与派活一律排除镜像，卸货清单中的镜像引用按品类键映射回背包真身。 | SDS.Vanilla/规则 §十 | SDS_JobTargets.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W55 | 设置「最高优先级」开启时，超维空间在所有可存实物的搬运判定中视为最高优先级储存：`ListerHaulables.ShouldBeHaulable` 对「已 spawn、非镜像、未禁止、无在途产出标记、可存入且本图有可用端点」的实物返回 true；`SDS_DepositService` 的存入竞争同时忽略「已在最优储存」拒绝；`SDS_DestinationResolver.TryDecideCore` 的存入决议不再与原版目的地比较优先级，直接采用超维端点。功能性目的地与 owner 型容器内容物不在覆盖内；镜像仍按 W52/W53/W54 处理，设置关闭时零行为变化。 | SDS.Vanilla/规则 §十 | Patch_Mirrors.cs | `CheckRuleIds.ps1`：ID/锚点存在 |
| W56 | 取用夹取（= min(需求, 可用量)）只有一处实现：`SDS_Availability.ClampTakeToAvailable(int wanted, long available)`；任一非正即 0（表示本次不取）。所有取用入口（Job 物化 / 装载单趟 / 编成差额 / 手动输出 / 交易与发射 / 兼容适配器）必须调用它；可用量本身只来自 `GetAvailableCount`（W22）或 `GetTradableCount`（R15），不得自算、不得内联同式。 | SDS.Rules/规则 §一 | SDS_Availability.cs | `CheckRuleIds.ps1`：ID/锚点存在；`TripCountTests`：夹取语义 |
| W57 | 所有玩家发起的手动存入入口（右键菜单、存入背包、第三方玩家 UI 的强制派活）必须共用同一手动决议 `SDS_DepositService.TryResolveManualDeposit`：手动 = forced，允许通过在途物化标记闸（`SDS_EndpointUtility.AcceptsForManual`）；自动搬运 / 回充 / 交付路径只允许 `AcceptsForAuto`，新增手动入口不得另写判定。 | SDS.Vanilla/规则 §十 | SDS_Destinations.cs | `CheckRuleIds.ps1`：ID/锚点存在；`CheckRuleSingleSource.ps1`：唯一标记 |
| W58 | 数量缺失的唯一补齐点 = `Toils_Haul.ErrorCheckForCarry`：仅目标为镜像且 `job.count ≤ 0` 时，按该 Job 对镜像的 `Reservation.StackCount`（-1 = 整堆）恢复取量；无预留保持 1；只读、不写账、不预留；恢复后仍由原版 `min(count, 携带量, 镜像量)` 与 W56 夹取。 | SDS.Vanilla/规则 §十 | SDS_HaulCountIntent.cs | `CheckRuleIds.ps1`：ID/锚点存在；`CheckRuleSingleSource.ps1`：唯一标记 |
| W59 | 消费点物化的唯一实现 = 直接消费型 driver 的 `Notify_Starting` 前置：Wear / Equip / UseItem 取 1 件；非工具使用者 Ingest 取 `max(1, job.count)`；落点 = 最近可达端点访问点；无端点 / 物化失败 → 不接管并交回原版失败条件，不得把镜像交给消费入口。 | SDS.Vanilla/规则 §十 | SDS_MirrorConsumeSeams.cs | `CheckRuleIds.ps1`：ID/锚点存在；`CheckRuleSingleSource.ps1`：唯一标记 |
| W60 | 镜像 Job 目标重映射与失效收口（保存 / 读档 / 运行期移除共用）：保存时按「Job loadID + 槽位 + 品类键」落盘；镜像重建完成后按同一身份换到新镜像；**同 key 无效镜像替换先建新镜像、按精确引用改指、再移除旧镜像（新建 / 落点 / 换新失败时回退关闭旧目标并移除）**；**仅处理仍指向旧镜像（读档清理后已 `Destroyed`）或已失效为 null 的槽，Job 已推进（槽已换成真身等存活物）时跳过、不覆盖、不结束**；键已无镜像（含运行期移除）时，当前 Job 的核心槽先结束该 Job、队列槽只移除该条、排队 Job 出队，再丢弃目标（队列同步移除 countQueue 对应项）；任何情况下不得写入 `LocalTargetInfo.Invalid`；旧档无节点视为空。 | SDS.Vanilla/规则 §十 | SDS_VanillaState.cs | `CheckRuleIds.ps1`：ID/锚点存在；`CheckRuleSingleSource.ps1`：唯一标记 |
| W61 | 镜像预留不排他：`ReservationManager.CanReserve` 对镜像目标一律放行（忽略 `maxPawns` / 其它预留），原版 `Reserve` 随后按正常路径登记；`CanReserveStack` 返回镜像当前堆量；**G 关闭（`SDS_Rules.Available` 为假）时两处一律拒绝 / 返回 0**，避免“派活成功、取货才失败”的 Job 死循环；互斥由账本事务（W22 / W56）保证；非镜像路径零行为变化。 | SDS.Vanilla/规则 §十 | SDS_MirrorReservations.cs | `CheckRuleIds.ps1`：ID/锚点存在；`CheckRuleSingleSource.ps1`：唯一标记 |
| E11 | 构建默认只编译改动模块的 csproj，且必须经 `BuildModule.ps1` 校验构建范围：构建前后比对 `超维存储/Assemblies/*.dll` 的 SHA256，除目标模块与显式声明的直接依赖外不得有任何 DLL 变化；只有改动 Contracts、公开面、ProjectReference 图、`Directory.Build.props`、SDK 或 NuGet 引用态时才构建整解；首次 restore 引发的全量重编必须显式确认并写入汇报。 | 设计原则 §三 | BuildModule.ps1 | `BuildModule.ps1` 退出码 |
| W62 | 任何「存入超维空间」的决议，若执行者是可用端口且可接收该实物，目的地必须折叠为执行者自身端口；物品到手后立刻经唯一存入入口入账；显式指定端口与取货 / 内部回充不适用。 | SDS.Vanilla/规则 §十 | SDS_SelfDepositFold.cs | `CheckRuleIds.ps1`：ID/锚点存在；`CheckRuleSingleSource.ps1`：唯一标记 |
| W63 | cell-only Job 的最佳目的地是超维建筑端点且执行者自身端口不可用时，落点必须改写为该端点 `AccessCellOf`（基础站位口径，不可站时兜底建筑格，不保证可走），由唯一落格入口入账；落点不可达、无法解析或入账失败时按原版「无空余储存空间」退让或落地；最佳目的地基线 = `StoragePriority.Unstored`（与 cell-only WorkGiver 同口径）。 | SDS.Vanilla/规则 §十 | SDS_CellOnlyDestinationFold.cs | `CheckRuleIds.ps1`：ID/锚点存在；`CheckRuleSingleSource.ps1`：唯一标记 |

## 唯一实现登记（机器校验）

> 规则的唯一实现地址（口径、判定、可见性等任何「有唯一实现符号」的规则）。`CheckRuleSingleSource.ps1` 校验：每条登记在源码里必须**恰有一处** `// RULE:<ID>` 标记且落在下表声明的文件；同时扫描重复取用算式与分散型规则的调用图白名单。新增或修改此类规则必须在此登记；流程类（E/S/R 过程项）不登记。

| 规则 | 唯一实现文件 | 说明 |
|---|---|---|
| R15 | SDS.Rules/SDS_Availability.cs | 交易 / 捐献 / 发射可用量 |
| R17 | SDS.Rules/SDS_Availability.cs | G 关闭时读侧归零 |
| S18 | SDS.Vanilla/Foundation/SDS_AccessRules.cs | 非玩家派系取用门 |
| W21 | SDS.Rules/SDS_WithdrawTransaction.cs | 执行期足额 / 尽量口径 |
| W22 | SDS.Rules/SDS_Availability.cs | 可用量 = total − reserved |
| W23 | SDS.Vanilla/TradeTransport/SDS_Trade.cs | 交易成交按行校验（预算唯一来源） |
| W24 | SDS.Vanilla/TradeTransport/SDS_Trade.cs | 保真只从同一分组桶摘真身 |
| W25 | SDS.Vanilla/TradeTransport/SDS_CaravanTransport.cs | 编成差额（装载差额见 W38–W48 装载链） |
| W62 | SDS.Vanilla/Endpoints/SDS_SelfDepositFold.cs | 通用自存折叠判定 |
| W63 | SDS.Vanilla/Jobs/SDS_CellOnlyDestinationFold.cs | cell-only 落点改写与登记 |
| W30 | SDS.Vanilla/Jobs/SDS_JobTargets.cs | 直接消费 Job 的物化取量（逃生门 + 瓶喂缺口） |
| W58 | SDS.Vanilla/Jobs/SDS_HaulCountIntent.cs | 数量缺失按预留意图补齐 |
| W59 | SDS.Vanilla/Jobs/SDS_MirrorConsumeSeams.cs | 消费点物化 |
| W60 | SDS.Vanilla/Foundation/SDS_VanillaState.cs | 镜像 Job 目标重映射与失效收口 |
| W61 | SDS.Vanilla/Mirrors/SDS_MirrorReservations.cs | 镜像预留不排他 |
| W34 | SDS.Core/SDS_Ledger.cs | 武器 / 衣物一次性迁移（不静默） |
| W35 | SDS.Rules/SDS_StockRules.cs | IsStudiable 一律保真 |
| W36 | SDS.Vanilla/Mirrors/Patch_Mirrors.cs | 镜像不登记可研究缓存 |
| W46 | SDS.Vanilla/TradeTransport/SDS_TransportVirtual.cs | 装载单趟夹取 |
| W51 | SDS.Vanilla/Preserve/SDS_PreservedBill.cs | 多槽配方的保真缺口 |
| W53 | SDS.Vanilla/Endpoints/SDS_EndpointUtility.cs | 可存入实物唯一判据 |
| S16 | SDS.Rules/SDS_BillSupplyPlanner.cs | 第三方补料规划 |
| W56 | SDS.Rules/SDS_Availability.cs | 取用夹取 |
| W57 | SDS.Vanilla/Jobs/SDS_Destinations.cs | 手动存入唯一决议（手动 = forced） |

## 维护脚本

脚本清单、职责与运行时机见《ModuleRules.zh-CN.md》十；本节不重复。
