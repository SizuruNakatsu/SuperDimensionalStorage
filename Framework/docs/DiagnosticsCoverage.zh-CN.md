# 超维存储 · 诊断覆盖清单

> 状态：**P0 + P1 已实施，并按用户 2026-10-21 要求收敛日志面**：
> 只保留「异常 / 数据一致性（Error）」「一次性自愈（Warning）」与「一次性统计输出」；
> **正常工作（闸门拒绝、决议让出、没库存、不可达…）一律不写日志**。
> 本文是诊断口径的唯一权威。

---

## 零、判定标准

一次**真故障**要能定位，日志至少包含：

1. **模块 + reason 码**：例如 `镜像/落点已满`、`取货/回滚失败（单件，已尝试兜底落地）`；
2. **运行时上下文**：`map`、`pawn`、`thing`（id + def + 数量）、`job`（def + id）、`reservation`；
3. **结果语义**：放弃本次 / 已回滚 / 异常；
4. 异常额外带堆栈；同一 reason 按时间节流。

---

## 一、现状总览（源码实测）

| 指标 | 数值 | 说明 |
|---|---|---|
| Harmony 补丁类 | 112 | 覆盖原版 API 的特性补丁入口 |
| 注册表条目 | 29 | 守卫 19 + 投影 owner 10（`SDS_MirrorGuardRegistry` / `SDS_ProjectionOwnerBridge`） |
| 补丁点合计 | 141 | 112 特性类 + 29 注册表条目 |
| TargetMethod 文本键去重 | 138 | 由 `CheckPatchInventory.ps1 -Report` 输出；文本键，非反射级；与 141 的差额来自多类挂同一方法 |
| `catch (Exception)` | 307 | 补丁入口 + 取货 / 镜像 / 保真桥 / 输出安装执行路径护栏（含执行器异常回滚、保真取出异常整笔回滚、镜像物理转换、容量提示、实转入账后的销毁异常） + 落点格 WorkGiver 运行期挂点 + 整备取量 Provider 护栏 + 第三方自建装载清单注入 + 销毁回充 / 接管预留异常 + 兼容注入闸门 + VC载具框架车辆货舱装载 / 守门 + 使用命令 provider 枚举 + W58 / W59 / W61 新接缝 + W62 / W63 落点折叠与落格入账 + 读档收集 / 重映射 / 即时时序 / 装载行重建 / 展示物补封存护栏 + W60 镜像 Job 目标收口 + 无效镜像替换换新护栏 |
| `SDS_Diag.ErrorOnce` | 251 | 异常：消息 + 调用点 + 异常类型（一次性）；含 SDS.Tests / SDS.Core.Tests / SDS.Rules.Tests 的调用点 |
| `SDS_Diag.Fail`（Error） | 93 | 数据一致性 / 执行期异常：回滚失败、异常回滚失败、保真取出异常 / 回滚转移失败、虚拟入账通道不匹配 / 接管账本持有物、扣账 / 预留超上限、镜像转换器缺失 / 预留不足、读档保真入容器失败、镜像物理转换、建镜失败、投影扣账失败、装载读档绑定不足、装载途中读档保真计划重建失败、销毁扣账 / 接管预留失败、入账后销毁异常、所有权回挂失败、编成展示物身份不唯一 / 保真桶无法唯一解析等 |
| `SDS_Diag.Warn`（Warning） | 32 | 一次性自愈：巡检移除内容失效真身、扣账超出账面、预留消费不足、金库登记自愈、旧档字段补齐、读档输入缓冲残留清理、保真桥回库失败、读档清理失效保真实物（尸体无内层小人 / 缩装无内层物，原版判定会空引用）、读档清理保真重复副本（按 thingID 去重）、读档发现保真真身仍在穿戴 / 装备槽（只告警）、装载派活快照缺失（运输舱 / 传送门两处调用）、回收时实物已被消耗、镜像取货点不在端点范围内（W54）、镜像 Job 目标失效收口（运行期移除 / 读档键失效两处，先结束 / 出队）、入账回滚时账面已空 / 不足（外部消费抢先）、运行期重复入库 / 同 ID 回充冲突 / 读档待恢复重复副本、读档归一化腐坏进度孤儿值（复位 -1）等；每个 reason 全程只记一次。2026-11-16：保真 / 虚拟供给 offer 的落点与回库告警收敛到 `SDS_OfferBridge.RollbackOrWarn` 单点，调用点 −2 |
| 普通工作日志（Message） | **0** | 闸门拒绝 / 决议让出 / 没库存 / 不可达 / 背包满：**不输出** |
| 一次性统计输出 | 8 类 | 补丁数、第三方挂点、可用性、端点数量、保真清理条数、使用命令 provider 来源、落点格 WorkGiver 挂点数、强制虚拟化转换件数（见 §三） |

> 统计口径（2026-10-28）：`超维存储/Source/**/*.cs`，排除 `obj` / `bin`，只计非注释行；`SDS_Diag.*` 只计调用点，不计定义。由 `CheckPatchInventory.ps1` §一 计数对账校验。
>
> 与 `SDS.Diag/Interfaces.zh-CN.md` §二 的区别：本表含 `SDS.Tests` / `SDS.Core.Tests` / `SDS.Rules.Tests` 的调用点；接口 §二 的 92 / 34 / 139（Core + Vanilla 的 Fail / Warn / ErrorOnce）为生产代码、排除测试与 Diag 自身。两者口径不同，不构成冲突。

### 为什么取消普通工作日志

`Log.Message` 并不免费：仍要做字符串格式化、写日志文件、进入日志缓冲；即使 600 tick 节流，
闸门拒绝这类事件在图上有大量候选时也会持续产生日志与噪音，并淹没真正的诊断。
因此按用户要求：**正常回退不记录**。

### 性能设计

- 高频路径（`CommonGates`、`TryDecideCore`、`SplitOff` / `TryStartCarry` / `SpawnSetup` 镜像分支）
  **没有任何日志调用**，连布尔判定的额外开销都没有；
- `Fail` 只在真故障分支调用：一次字典查询 + 整数比较（600 tick 节流，第 10 / 100 / 1000 次补一条）；
- `Warn` 只做一次 `HashSet.Add`，全程最多一条；
- 上下文串 `SDS_Diag.Ctx(...)` 只在 `Fail/Warn` 真要输出的分支构造。

### 日志级别约定

| 级别 | 用途 | 例子 |
|---|---|---|
| `Log.Error`（Fail） | 数据一致性 / 执行期异常 | `取货/回滚失败（单件，已尝试兜底落地）`、`取货/回滚兜底落地失败`、`镜像/落点已满`、`取货/执行器异常` |
| `Log.Warning`（Warn） | 一次性自愈 / 旧档升级 / 一次性回收异常 | `金库/登记自愈`、`存档/旧档字段补齐`、`回收时实物已被消耗` |
| 不输出 | 正常工作与回退 | 闸门拒绝、决议让出、无库存、不可达、背包满、无端点地图 |

---

## 二、逐模块覆盖清单

图例：✅ 有诊断；⏸ 有意静默（正常工作）。

### A. 启动 / 补丁应用

| 入口 | 现状 | 说明 |
|---|---|---|
| `SDS_Mod.ApplyHarmonyPatches` | ✅ | 失败逐类 Error；一次性输出 `已应用/声明` 数量；**只有失败时**才列全量名单 |
| `SDS_CellOnlyWorkGiverPatches.Apply` | ✅ | Def 加载完成后运行期枚举全部 WorkGiver；一次性输出 `JobOnThing N / HasJobOnThing M`；同时挂 `Toils_Haul.PlaceHauledThingInCell` 落格入账钩子（W63）；单条挂点失败 `ErrorOnce` + 失败名单 Warning；枚举 / 应用异常 `ErrorOnce`；Def 未就绪时静默等待下一次回调；自检输出 `W63 改写 / 探针 / 退让 Job / 退让探针` 计数 |
| `SDS_ModCompat_PUAH.TryApply` | ✅ | 逐挂点登记（`SDS_CompatPatchReport`），一次性输出 `已生效 A/B 个挂点`；有失败 → Warning 列「挂点名（找不到类型 / 找不到方法 / 应用异常）」+ 已生效名单；未安装静默，装了但探测不到 → Warning。应用阶段异常另记 `第三方兼容挂点应用失败（已跳过）：挂点名`（ErrorOnce；2026-10-27 起挂点应用逻辑与其它适配器共用 `SDS_CompatTools.TryPatch`） |
| `SDS_ModCompat_CE.TryApply` | ✅ | 同上（同一套 `SDS_CompatPatchReport`）：15 个挂点逐个登记 A/B；`Resolve()` 关键类型不完整时输出 `已生效 0 个挂点` + 四个类型的有/无。2026-10-27 起应用逻辑与其它适配器共用 `SDS_CompatTools.TryPatch`（优先级统一 `Low`），应用异常 reason 统一为 `第三方兼容挂点应用失败（已跳过）：挂点名` |
| 牵引光束搬运器Manipulator Beam Emitter（`SDS_ModCompat_ManipulatorBeam.TryApply`） | ✅ | 同上：3 个挂点逐个登记 A/B（`BeamTransfer` 构造函数缺失时计入 B）；三个运行期翻译各带 `ErrorOnce`（`牵引光束搬运器Manipulator Beam Emitter 兼容（容器类型闸门 / 容器扫描表 / 端点选源）失败`），正常拒绝（不是本 Mod 端点）完全静默 |
| 天工模块化穿梭机 CeleTech Shuttle Extension（`SDS_ModCompat_CeleTechShuttle.TryApply`） | ✅ | 同上：1 个挂点登记 A/B；未安装静默；类型 / 方法缺失 Warning 注明「装载清单注入未生效」（穿梭机列表看不到超维库存）；注入异常 `天工模块化穿梭机 CeleTech Shuttle Extension 兼容（装载清单注入）失败`（ErrorOnce）；宿主未开始装载时 `TransportersInGroup` 为 null 属正常态（回退宿主自身，不记日志）；无端点 / 无库存 / 未解析属正常回退，静默 |
| 边缘米莉拉养成：与你相连之人（`SDS_ModCompat_MiliraGift.TryApply`） | ✅ | 1 个挂点登记 A/B；未安装静默；`JobDriver_IdealReceiveGift` 类型或 `CompIdealGift` 字段缺失 → 不挂并给原因；物化 / 复制 / 落点异常记 `边缘米莉拉养成兼容（送礼端点物化）失败（已忽略，原行为不受影响）`（ErrorOnce）；`CompIdealGift` 在镜像或真身上缺失记 `边缘米莉拉养成兼容（送礼端点物化）：目标 comp 缺失（已回账，不接管）`（ErrorOnce）；正常非镜像 / 无端点 / 无库存不接管均不写日志 |

### B. 可见性 / 镜像

| 入口 | 现状 | reason |
|---|---|---|
| `SyncAll` / `SyncIfDirty` | ✅ 异常 + 真故障 | `镜像/金库落点不可用`（Error） |
| `SyncDirtyKeys` | ✅ | `镜像/金库缺失`（Error） |
| `SpawnMirror` | ✅ | `镜像/落点已满`、`镜像/品类不可重建`、`镜像/堆上限非法`（Error） |
| `OnMirrorSpawned`（`Thing.SpawnSetup` 补丁） | ✅ 护栏只包镜像 | `镜像/落点初始化异常` |
| `EnsureOn` / `FindVaultCell` | ✅ | `金库/Def缺失`、`金库/全图无空格`、`金库/创建失败`、`金库/落子失败` |
| `HandleNoAnchor` / `RemoveOn`（W37） | ✅ | `金库/撤回失败`（ErrorOnce）；宽限期内正常保留不写日志 |
| `RepairEdificeRegistration` | ✅ 一次性 | `金库/登记自愈`、`金库/登记跳过`（Warn） |
| `TryRelocate` | ✅ | `金库/搬迁无空位`（Warn）、`金库/搬迁失败`（Error）、`金库/搬退回原格失败`（ErrorOnce） |
| 结构离场撤离（金库 `DeSpawn` / `MapDeiniter`） | ✅ | `镜像/结构撤离`（Warn 一次性；含 reason + count，仅 `count > 0` 记）、`镜像/撤离失败`（Error）、`镜像/结构撤离异常`（ErrorOnce，入口兜底且不阻断离场）；账本零写入，不是丢失 |
| 投影 owner（Take / TryDrop / TryTransfer） | ✅ | `投影/取出失败`、`投影/落地*`、`投影/转移*`、`投影/目标*` |
| 本图无端点 / 端点附近无落点 | ⏸ | 正常状态，不写日志；W37：有结构性端点建筑则保留金库，否则宽限 250 tick 后移除 |
| 房间 / 观察 / 发脾气 / 纵火过滤 | ✅ 原有 | `ErrorOnce`（纯判定型无日志需求） |
| 偷窃目标 / 偷窃价值过滤（S15） | ⏸ | 正常过滤：镜像 / 金库在 `StealAIUtility` 里读作不可偷（价值 0），不写日志 |
| 镜像寻路 / 入手（W54） | ✅ 一次性 | 非芯片小人不在访问点命中范围内时拒绝取货：`镜像取货点不在端点范围内（已拒绝取货）`（Warn，每个 reason 只记一次）；寻路解析失败按目标不可达结束 Job；PUAH 候选 / 派活与小人 owner 的 `ThingOwner` 入口对镜像静默拒绝（正常过滤，不写日志） |

### C. 读侧口径 / 计数 / 可及性

| 入口 | 现状 | 说明 |
|---|---|---|
| `ResourceCounter` / 财富 / 可及性补丁 | ✅ 原有异常日志 | 数值来源解释属可选调试开关，未实现 |
| `ExposeData` 旧档兜底 | ✅ 一次性 | `存档/旧档字段补齐`（列出补齐字段，Warn） |
| `EnsureStorageSettings` | ✅ 一次性 | `存档/金库筛选补齐`（Warn） |
| `MirrorExposureOnResourceCounter` 返回 0 | ⏸ | 正常路径量大 |

### D. 搜索 / 可达

| 入口 | 现状 | 说明 |
|---|---|---|
| 四个 `GenClosest` 适配器 / `Reachability` | ✅ 原有异常日志 | 候选被过滤的逐次原因不记录 |
| 区域 BFS 边界 | ⏸ | 文档已记，运行期不加 |
| `ListerHaulables.ShouldBeHaulable`（W52 / W55） | ⏸ | 镜像流出与最高优先级候选的放行 / 排除都是正常过滤，不写日志 |

### E. 取得到

| 入口 | 现状 | reason |
|---|---|---|
| `SDS_WithdrawTransaction.Begin*`（SDS.Rules） | ✅ 仅参数/内部错误 | `取货/参数无效`、`取货/事务参数无效`、`取货/计划无效`、`取货/接管*`（Error） |
| 库存不足 / 预留竞态 / 保真数量不足 | ⏸ | 正常回退，**不写日志** |
| `ExecuteVirtual` / `ExecutePreserved` / `ExecutePreservedExact` / `ExecuteMirror` | ✅ 仅内部通道错误 | `取货/通道不匹配`（Error） |
| `Deliver` / `Rollback` | ✅ | `取货/预留释放失败`、`取货/回滚预留释放失败`（**Error**） |
| `RollbackMaterialized`（W27 按通道逐件回滚） | ✅ | 单件失败记 `取货/回滚失败（单件，已尝试兜底落地）`（Error，带 `index` + `reservation`），兜底再失败记 `取货/回滚兜底落地失败`（Error）；其余件继续回滚 |
| `SDS_ReturnUtility`（SDS.Rules：回充 / 保真回滚 / 虚拟回滚） | ✅ 仅内部错误 | 回充失败落地属正常回退，不刷屏；账本直写失败由调用方记 `编成/虚拟回滚失败`、`编成/保真回滚失败` |
| `SDS_ReclaimRegistry`（回收 / 巡检 / 读档审计） | ✅ | `回收时实物已被消耗（不回加账本）`（Warn，一次性） |
| Job 清理 / 目标回充（`Patch_SDS_ReclaimJobTargets`） | ✅ 异常兜底 | `Job 清理失败（已忽略，原版行为不受影响）`、`Job 目标回充失败（已忽略，实物留在地图）`（ErrorOnce）；成功丢弃登记、正常回充不写日志 |
| `SDS_WithdrawExecutor.*`（SDS.Rules） | ✅ 全入口护栏 | `取货/执行器异常`（Error，异常不外泄给原版） |
| `Thing.SplitOff` 镜像分支 | ✅ 护栏 | `取货/SplitOff异常`（Error） |
| `TryStartCarry` 镜像分支 | ✅ 护栏 | `取货/入手异常`（Error） |
| `Thing.TryAbsorbStack` 镜像分支 | ✅ 护栏 | `取货/合并取出异常`（Error）；镜像吸收别人属正常拒绝，不写日志 |
| `Thing.Destroy` 镜像分支（W19） | ✅ fail-closed + 一次性 | `外部销毁镜像被拒绝（W19 fail-closed）`（`Log.WarningOnce`，按 thingID 去重）；内部清理先 `Unmark`，不写日志 |
| `Toils_Haul.ErrorCheckForCarry` 镜像分支（W58） | ✅ 异常兜底 | `取量意图记录失败（已交回原版 1 件兜底）`、`取量意图补齐失败（已保留原版 1 件兜底）`（ErrorOnce）；正常补量 / 无预留保持 1 不写日志 |
| 消费点物化（W59：Wear / Equip / UseItem / Ingest） | ✅ 异常兜底 | `镜像消费点物化失败（穿衣 / 装备 / 使用物品 / 进食，已交回原版失败条件）`（ErrorOnce）；正常物化、无端点 / 无库存不接管均不写日志 |
| 镜像预留放行（W61） | ✅ 异常兜底 | `镜像预留放行（CanReserve / CanReserveStack）失败（已交回原版判定）`（ErrorOnce）；G 关闭时拒绝属正常回退，不写日志 |
| 镜像 Job 目标重映射与失效收口（W60） | ✅ 静默恢复 + 一次性收口 | 正常重映射静默；仅处理仍指向旧镜像（读档清理后已 `Destroyed`）或已失效为 null 的槽，Job 已推进（槽已换成真身等存活物）时跳过、不覆盖、不结束；同 key 无效镜像替换先建新镜像并精确改指，成功静默；新建 / 落点失败或换新异常时回退关闭旧目标（异常记 `镜像 Job 目标换新失败（已回退关闭旧目标）`，ErrorOnce）；键失效时先结束当前 Job / 排队出队 / 移除队列槽，再丢弃目标：运行期记 `镜像 Job 目标失效收口（先结束/出队，再移除镜像）`，读档记 `镜像 Job 目标失效收口（读档键已无镜像，先结束/出队）`（Warn，固定 reason 去重，计数 / 槽位放上下文）；任何情况下不写 `LocalTargetInfo.Invalid`；Job 已不存在按正常完成跳过 |
| 读档装载行重建（W48） | ✅ 静默恢复 | 正常按品类键重建 / 空行复用不写日志；容器不存在、库存已空或超过 250 tick 时丢弃该行，不刷错误 |
| 取货 JobDriver（装载 / 清单保真） | ✅ | `取货/清单*`、`装载/取货物化失败`、`装载/交付目标失效` |
| 供给失败原因（A1） | ⏸ / ✅ | `UnresolvedDef` / `Forbidden` / `Insufficient` 为正常回退，不写日志；`RebuildFailed` 归入 `取货/执行器异常`（Error） |

### F. 存入

| 入口 | 现状 | reason |
|---|---|---|
| 三个派活出口补丁 | ✅ 异常 | 异常走 `ErrorOnce` |
| `CommonGates`（13 道） | ⏸ | 正常回退，**不写日志**（纯判定） |
| `TryDecideCore` / `TryParseVanillaDestination` | ⏸ | 正常让出，**不写日志** |
| `BuildJob` | ✅ 仅内部错误 | `存入/缺JobDef`（Error）；容量 / 多件端口限制为正常回退，不记录 |
| `JobDriver_SDS_DepositToGrid` | ✅ 仅一致性风险 | `存入/入包失败`、`存入/背包移除失败`、`存入/入账失败`、`存入/手上货物落地`（Error/Warn）；队列空 / 端点失效 / 不可达 / 背包满 / 端点拒收为正常回退，不记录 |
| 端点 `InputBuffer` | ✅ 原有 | `端点输入缓冲数字化失败` |
| 端点 `InputBuffer` 原版预检（`GetCountCanAccept` / `CanAcceptAnyOf`） | ⏸ | 正常拒绝不写日志；与 `TryAdd` 共用 `Accepts` 硬门（2026-10-26） |
| 原版与第三方「只消费落点格」的清空容器派发（W63） | ⏸ 正常折叠 / 退让；✅ 异常 | 运行期枚举全部 WorkGiver 实现（`SDS_CellOnlyWorkGiverPatches`），先按 W63 折叠落点；无法解析落点时按原版「无空余储存空间」退让，属正常回退、不写日志；判定 / 探针 / 挂点异常只记 `ErrorOnce`（异常不外泄，单次挂点失败不影响其余）；启动日志输出 `JobOnThing N / HasJobOnThing M` 与失败名单。原版 `EmptyWasteContainer` / `EmptyEggBox` 的类型特化挂点已删除，统一由运行期枚举覆盖；圣物由 `Pawn_JobTracker.TryTakeOrderedJob` 统一折叠。 |
| 优先级流出（W52） | ⏸ 正常判定静默 | 设置关闭 / 最高优先级开启 / 无严格更高优先级目的地 / 目的地不收 / 可用量为 0：不进入候选或跳过搬运，不写日志；仅异常走 `ErrorOnce` |

### G. 交易 / 编成 / 装载

| 入口 | 现状 | reason |
|---|---|---|
| 交易交付 | ✅ 原有 | 可用量竞态已有 `ErrorOnce`；UI 展示过滤不记日志 |
| 编成 / 运输舱 / 传送门注入 | ✅ | `编成/取货物化失败`、`编成/保真无可用计划`、`编成/保真物化失败`、`编成/虚拟回滚失败`、`编成/保真回滚失败` |
| 装载 JobDriver | ✅ | `装载/取货物化失败`、`装载/交付目标失效`、`装载/装舱身份绑定失败`（装舱归一抛异常，`SDS_Diag.ErrorOnce`，交回原版） |
| 装载派活选行（W38，`TrySelectLoadRow`） | ⏸ 有意静默 | 行不可服务（桶已取空 / 全被其它 Job 计划 / 该键可用量 0 / 无取物点）属于正常回退：跳过该行继续扫下一行，不写日志；禁止把"不可服务"判成 HasJob=true（否则原版报 `yielded no actual job`） |
| 装载派活快照（W42） | ✅ 一次性 | JobOn 没有同 tick 快照 → `装载/派活快照缺失（本 tick 不改派）`（`SDS_Diag.Warn`，全程一次），原样保留原版结果；下一次 HasJob 重新登记；正常命中不写日志 |
| 装载计划有效期（W43） | ⏸ 有意静默 | 陈旧计划在选行时回收属于正常清理；排队 Job（`jobQueue`）仍算有效占用，同样不写日志；`TryMakePreToilReservations` 失败释放计划不写日志（已有 `取货/取物点不可预留`）；落点格守卫探针期间不登记计划，也不写日志 |
| 对话框刷新范围（W44） | ⏸ 有意静默 | 只清展示物身份注册，不触碰派活状态；跨容器互不影响，属正常行为 |
| 保真桶身份（W45） | ⏸ 有意静默 | 同桶复用 / 跨容器新实例都是正常路径；判定只看真身重叠，不写日志 |
| 装载可用量 / 携带上限（W46） | ⏸ 有意静默 | 预留后可用量为 0、或携带上限 ≤ 0（本趟不派活）→ 该行自然跳过，属正常回退 |
| 装载途中读档身份重建（W47） | ✅ 一次性 | 预留键可用 → 重建计划成功属正常路径，不写日志；重建不出计划 → 交回既有 `装载/取货物化失败`（`SDS_Diag.Fail`），不另设 reason |
| 装载行读档恢复（W48） | ✅ 一次性 | 传送门纯超维行被原版丢弃后按欠量重建属正常恢复，不写日志；绑定不足仍走 `装载/读档绑定不足`（`SDS_Diag.Fail`，600 tick 节流） |
| 在途物化标记（W49） | ⏸ 有意静默 | 回充 / 落地时撤销标记属正常收尾；撤销后该实物回到自动入库 / 自动取用范围，不写日志。原版搬运 Job 自身失败后的落地由标记寿命兜底（已知边界） |
| 装载行读档重建（`transportLoadRequests`） | ✅ 一次性 | 找不到容器 / 行已被清空 → 跳过（原版清单已不存在，属正常）。**保真行绑不到任何桶** → `装载/读档绑定不足`（`SDS_Diag.Fail`，600 tick 节流；W39） |
| 展示物身份解析（`KeyOfDisplay`） | ⏸ 有意静默 | 有桶 → 桶键；无桶 → 工厂封存的键；两者都无 → 按虚拟投影 `FromThing` 兜底。兜底只在"绕过工厂自造展示物"时出现，不刷日志（避免每帧噪音）；若日后出现，应修工厂而不是加日志 |
| ProjectE 等价交换 瞬时转化（`SDS_ModCompat_EquivalentExchange`） | ✅ 护栏 + A/B 报告 | 展示物换真身 / 保真摘行属正常翻译，不写日志；`ProjectE 等价交换 兼容（瞬时转化前置）失败`、`（瞬时转化回充）失败`、`（区域转化过滤）失败`（ErrorOnce）；挂点缺失由 `SDS_CompatPatchReport` 走统一 Warning；挂点应用统一走 `SDS_CompatTools.TryPatch`（`TryAccept` prefix 显式 `Priority.High`，其余 `Low`），应用异常 reason 为 `第三方兼容挂点应用失败（已跳过）：挂点名` |
| 联合轻工家具拓展-UF Light Industries Furniture 全自动打印机（`SDS_ModCompat_UFRecipes`） | ✅ 护栏 + A/B 报告 | 只补**保真通道**（尸体 / 缩装 / 基因）到打印机容器；虚拟品类交给它自己的原版拉料。取不出来属正常回退，不写日志；异常记 `联合轻工家具拓展-UF Light Industries Furniture 兼容（全自动保真供料）失败`（ErrorOnce）；挂点缺失走统一 Warning |

### H. 生命周期 / 存档 / 设置

| 入口 | 现状 | 说明 |
|---|---|---|
| `ExposeData` 旧档字段补齐 | ✅ 一次性 | `存档/旧档字段补齐` |
| 读档端点缓冲残留清理 | ✅ 一次性 | `存档/输入缓冲残留已清理`（Warn，件数；只清重复副本 / 已销毁 / 零栈，不改真实库存） |
| 读档保真重复副本清理（Core） | ✅ 一次性 | `读档清理保真重复副本`（Warn，件数；账本按 thingID 去重，只丢重复引用，不 Split / 不 Destroy / 不改数量） |
| 读档保真真身仍在穿戴 / 装备槽 | ✅ 一次性 | `读档发现保真真身仍在穿戴/装备槽（未自动摘除）`（Warn；不自动摘除，避免改变原版“穿着中”的表现，需人工处理） |
| 读档未解析条目（A1） | ✅ 一次性汇总 | `存档/未解析 def（账目已保留，修复 def 后自动恢复）`（Warn；条目数 + 件数 + 明细，不逐条刷屏） |
| 未解析重试 | ⏸ | 正常流程（玩家点「重试解析」），结果只走 UI 提示 |
| `SDS_StaticState.ResetAll`（镜像 / 投影 / 端点 / 回收 / offer / 交易 / 运输 / 编成 / Combat Extended 与米莉拉扩展-未来科技Milira Expanded - Future Technology 短时缓存 / 实物标记 / 存入派活 / 每图仓库代理 / 资源条缓存 / 清单计数缓存 / 芯片缓存） | ⏸ | 正常流程；新档 / 读档的**唯一**清理入口，见 §二·M |
| 设置 clamp | ⏸ | 正常夹取 |
| 武器/装备强制虚拟化（W33/W34） | ✅ 一次性 | 成功输出 `[超维存储][强制虚拟化] 转换完成：N 件`（玩家主动操作，只一行）；异常 `强制虚拟化/转换失败`（`SDS_Diag.Fail`，节流）；交易 / 编成 / 装载窗口未关闭时拒绝（正常回退，不写日志）；预留 / 受保护跳过为正常回退，不逐条刷屏 |

### I. 使用命令

| 入口 | 现状 | 说明 |
|---|---|---|
| provider 枚举（纳入 / 排除） | ✅ 一次性 | `【使用命令】右键菜单项来源：纳入 N 个 provider / 排除表 M 类` |
| 门控 0（点建筑 / 地面 / 物品） | ✅ 一次性 | `Use.NotPawn`（不取出、不扣账） |
| 门控 0（机械体 / 动物 / 非玩家阵营） | ✅ 一次性 | `Use.NotEligible` |
| 门控 0（无芯片 / 芯片不可用） | ✅ 一次性 | `Use.NoPort` |
| 门控 1（类别不适用） | ✅ 一次性 | `Use.NotUsable` |
| 门控 2（原版灰项理由） | ✅ 一次性 | `Use.Blocked`，附原版灰项原文 |
| 非玩家取用门（S18，设置开启） | ⏸ 正常拒绝静默 | 已知非玩家 Pawn 在搜索补候选 / 可达性 / Job 物化 / 携带 / 装备衣物背包接收 / 端点取出入口直接跳过；未知 actor 保持原版口径 |
| ④ 任务类 JobDef | ✅ 一次性 | `Use.TaskJob`，附 JobDef 名，已取消 + 回充 |
| ④ 未知 JobDef | ✅ 一次性（`Log.WarningOnce`） | `Use.UnknownJob`，已按使用处理 |
| ④ 无 JobDef 的任务 | ✅ 一次性（`Log.WarningOnce`） | `创建了无 JobDef 的任务，已按未知使用类处理`，保留落地并撤销在途标记（W49） |
| 回充失败 | ✅ 一次性 | `Use.RefundFailed`，物品留在地面 |
| 放置失败 | ✅ 一次性 | `Use.NoPlace`（已回充） |

### J. 保真消费桥（方案 A）

| 入口 | 现状 | reason |
|---|---|---|
| `HealthAIUtility.FindBestMedicine` / `FoodUtility.TryFindBestFoodSourceFor` / `JobGiver_PickUpOpportunisticWeapon` / `JobGiver_OptimizeApparel` | ✅ 异常兜底 | `保真桥/药品提供失败`、`保真桥/食物提供失败`、`保真桥/武器提供失败`、`保真桥/衣物提供失败`（ErrorOnce，异常不外泄） |
| 落点 / 回库 | ✅ 自愈告警 | `保真桥/落点失败（实物已落地，未回库）`、`保真桥/虚拟落点失败（实物已落地，未回库）`、`保真桥/超时回库失败（实物已落地）`、`保真桥/失图回库失败（实物已落地）`（Warn，一次性） |
| 无候选 / 无端点 / 冷却中 / 品类有预留 | ⏸ | 正常回退，**不写日志** |
| 多槽配方缺口物化（W51，`Patch_SDS_PreservedBillIngredients`） | ⏸ 正常回退静默；✅ ErrorOnce | 逐槽物化到工作台交互格失败时整批回账本并交回原版，不额外写日志；重跑原版搜索仍失败同属正常回退；异常统一走既有 `保真原料补漏失败（已忽略，原版行为不受影响）` |

### K. 输出安装（缩装物 / 安装蓝图）

| 入口 | 现状 | reason |
|---|---|---|
| 已有安装蓝图 → 取消旧蓝图后重建 | ⏸ 有意静默 | 与原版 `Designator_Install` 同语义，不算异常；`questTags` 不做特判 |
| 安装放置器取消（右键 / ESC） | ⏸ 有意静默 | 不取货、不扣账（W31），属正常取消 |
| 安装落点非法 / 库存已被取走 | ⏸ | 幽灵标红并由原版提示原因（`CanPlaceInstallAt`）；不写日志 |
| 建蓝图抛异常 | ✅ ErrorOnce | `输出/安装失败（物资已回滚）`（含半生成蓝图清理 + 真身退回账本） |
| 半生成蓝图清理失败 | ✅ ErrorOnce | `输出/安装半生成蓝图清理失败（已跳过）` |

### L. 公共 API 门面（阶段 1–5 已实现）

| 入口 | 现状 | 说明 |
|---|---|---|
| `SDS_API` 看 / 读 | ⏸ 正常拒绝静默；✅ ErrorOnce | 只读，无阵营/地图门；异常记 `公共API/看库存异常`、`公共API/读*异常`、`公共API/可用*异常` |
| `SDS_API` 找 | ⏸ 正常拒绝静默；✅ ErrorOnce | 非玩家 / 无端点只返回 `SDS_ApiResultCode`；异常记 `公共API/存/找端点异常`、`公共API/取/找取物点异常` |
| `SDS_API` 存 | ⏸ 正常拒绝静默；✅ ErrorOnce | 所有权 / 阵营 / 禁止 / 筛选 / 容量 / 跨地图失败只返回 `SDS_ApiResultCode`；异常记 `公共API/存/*异常` 并返回 `InternalError` |
| `SDS_API` 取 | ⏸ 正常拒绝静默；✅ ErrorOnce | 供给失败走 `SDS_SupplyFailure` 映射；`VirtualHandoff` / 非玩家 / 跨图镜像返回 `InvalidArgument`；异常记 `公共API/取/*异常` 并返回 `InternalError` |
| `SDS_API` 补料规划（S16） | ⏸ 正常拒绝静默；✅ ErrorOnce | 槽位不接受 / 无可用 def 只返回 `false`；异常记 `公共API/补料槽位判定异常`、`公共API/补料规划异常`、`公共API/def列表补料规划异常`、`公共API/补料存入反馈异常` |
| 适配器翻译层 | ✅ 阶段 4 已迁移、阶段 5 牵引光束搬运器Manipulator Beam Emitter / 天工模块化穿梭机 CeleTech Shuttle Extension 已接入；⏸ 正常翻译静默 | Pick Up And Haul / Combat Extended / ProjectE 等价交换 / 联合轻工家具拓展-UF Light Industries Furniture / 米莉拉扩展-未来科技Milira Expanded - Future Technology 只调用 `SDS_API` 公共五轴 / 公共辅助 / `internal` 适配器桥，不再直接调用 `SDS_DepositService` / `SDS_WithdrawExecutor` / `SDS_GameComponent_Grid` 等内部服务；牵引光束搬运器Manipulator Beam Emitter 只判端点身份（`IsDepositEndpoint`）与端点接收（`IHaulDestination.Accepts`），投递走原版容器契约（端点 `InputBuffer`）；天工模块化穿梭机 CeleTech Shuttle Extension 只调 internal 清单注入桥（`InjectTransportLoadCandidates`）。API 内部异常走 `公共API/*异常`，适配器自身挂点异常仍走原有兼容日志 |

> 适配器门面（《公共API》§二·六）的只读 / 回滚异常也统一走 `公共API/*异常`；正常拒绝静默。

> 阶段 4 与阶段 5（牵引光束搬运器Manipulator Beam Emitter / 天工模块化穿梭机 CeleTech Shuttle Extension）已完成；阶段 6 实机验收后，把本节对应项并入 §二 的 F / G，并按实际源码统计更新 §一。

### M. 运行期静态状态 / 不变量自检（2026-10-27）

| 入口 | 现状 | 说明 |
|---|---|---|
| `SDS_StaticState.ResetAll` | ⏸ 正常流程 | **E1**：新档 / 读档的**唯一**清理入口（由 `SDS_GameComponent_Grid` 构造函数调用）。**规则**：任何「跨存档不应保留」的静态缓存必须在这里登记，禁止各入口零散清理；进程级常量（反射 `FieldInfo`、Provider 实例）不得放进来。开发者模式启动时输出一行清理项数（当前静态 17 项 = Core 0 + Rules 4 + Vanilla 13；已启用适配器另有动态 2 项：CE / Milira）。 |
| 登记清单（2026-10-27 补齐） | ⏸ | **按 ID / 绝对 tick 的短时缓存**（同进程换档时 ID 重叠 + `TicksGame` 回退 → 旧 `until` 可能远超新档当前 tick，造成长期误判）：`SDS_ModCompat_CE`（换弹冷却 / 优先级）、`SDS_ModCompat_MiliraExpanded`（设备补料节流）、`SDS_ProducedThingMarker`（刚物化实物的来源排除标记）、`SDS_DepositDispatchLedger`（在途 + 失败退避，键为 `pawnID<<32|thingID`）。**按 Map / 实物引用的缓存**（只解除引用，绝不销毁实物）：`SDS_MirrorHaulDestination`（每图仓库代理）、`SDS_Exposure`（资源条刷新缓存 + 复用缓冲）、`SDS_ReadOnlyUI`（清单计数缓存，含旧 Grid 与 `List<Thing>`）、`SDS_ImplantUtility.ResetChipCache`（芯片等级缓存，与 `NotifyChipStateChanged` 共用实现）。`Building_SDS_Vault` 的 `vaults` 与 W37 的 `noAnchorSince` 经 `SDS_MirrorService.Reset` → `ResetStatics` 清理，同属第 1 项。 |
| 有意不登记 | ⏸ | `SDS_ModCompat_MiliraExpanded.canStoreCache`：键是 `ThingDef`、跨存档稳定，不属于「不应保留」，所以不放进 `ResetAll`；但 W33 起其值依赖「武器/装备强制虚拟化」开关，开关变化时必须调用 `SDS_ModCompat_MiliraExpanded.ClearStoreCache()`（`SDS_Mod.ApplySettings` 与确认回调已接入）。W51 的 `Patch_SDS_PreservedBillIngredients.rerunning`（重入守卫，调用内 `try/finally` 必复位）与 `findBillIngredients`（进程级反射 `MethodInfo`）同属不放 `ResetAll` 的进程级常量 / 瞬时量。 |
| 展示物缓存（`displayCache` / `displayKeys` / `bucketDisplayCache`） | ⏸ | `SDS_OutputUtility.ResetDisplayCaches` 只清引用，**绝不销毁**展示物（与既有纪律一致）。 |
| `SDS_Invariants`（开发期断言） | ✅ 仅 DEBUG 构建 + 开发者模式 | **E2**：`Available ∈ [0, Total]`——**不**断言 `Available == Total − Reserved`：外部销毁后 `Reserved > Total` 合法，此时可用量夹 0；事务必须处于 `Reserved` 才能 Execute；`Delivered / RolledBack` 后不得再推进。只记 `Error`，不抛异常。 |
| `SDS_SelfCheck.RunLedgerChecks`（开发者菜单「超维存储 → 自检：账本 / 预留 / 镜像」；开发者模式读档后首个 tick 自动跑一次） | ✅ 手动 / 自动（DevMode） | 只读校验：虚拟 / 保真的可用量 = `max(0, total − reserved)`；登记过的镜像必须仍是本图存活实物（不校验数量——镜像同步有节流窗口）；W35 逐件跟踪型必须被判为保真；W37 金库必须存活且属于本图、金库格不得属于 Zone / 外来 SlotGroup、锚点不得引用已销毁 / 他图对象。输出 `自检 完成：检查 N 项，失败 M 项`。 |
| `CheckPatchInventory.ps1`（构建期，非运行时） | 见《ModuleRules.zh-CN.md》十 | 补丁清单 ↔ 源码双向对账：特性类 112 + 注册表 29 = 补丁点 141、动态行（含VC载具框架车辆货舱装载 / 投影 / 守门 3 个挂点）、基线 29 行（19+10，28 个不同 TargetKey）、特例 3；TargetMethod 文本键去重 138。脚本登记不在此重复。 |

---

## 三、一次性输出清单（保留）

1. `Harmony 补丁类应用完成：A/B = 已应用/声明 个`（有失败时追加失败名单与已生效名单）；
2. 第三方挂点：Pick Up And Haul / Combat Extended / 联合轻工家具拓展-UF Light Industries Furniture / 闪耀世界毁灭者5Glitterworld Destroyer 5 - Mechanoid Addon / 牵引光束搬运器Manipulator Beam Emitter / 米莉拉扩展-未来科技Milira Expanded - Future Technology / 天工模块化穿梭机 CeleTech Shuttle Extension / ProjectE 等价交换，每个**已检测到**的适配器一条 **A/B**（`已生效 已应用/应生效 个挂点`）；有失败时改为 Warning，逐条列「挂点名（原因）」与已生效名单；未安装静默（装了但类型/方法探测不到必须 Warning）；
3. `超维空间可用性：可用/不可用`；
4. `端点总计 / 建筑端点 / 殖民者端点 / 机械体端点`（总与可用数）；
5. 保真通道清理条数（有清理才输出）；
6. 「使用命令」右键菜单项来源：纳入 / 排除计数 + 排除表未命中名单（启动一次）；
7. 落点格 WorkGiver 守卫：`JobOnThing N / HasJobOnThing M（枚举 X 个 WorkGiverDef）` + `W63 改写 / 探针 / 退让 Job / 退让探针` 计数；挂点失败时追加失败名单（W63 统一版，启动一次）。
8. 武器/装备强制虚拟化：`[强制虚拟化] 转换完成：N 件`（玩家确认开关后一次，只输出一行）。

---

## 四、日志规范（实际格式）

```
[超维存储][镜像/落点已满] map=0 cell=(120, 0, 171) max=1 items=37 def=GravlitePanel（调用点：SuperDimensionalStorage.SDS_MirrorService.SpawnMirror）
```

- **Error**：数据一致性 / 执行异常；同一 reason 600 tick 一条，第 10 / 100 / 1000 次补次数；
- **Warning**：一次性自愈（每 reason 全程一条）；
- **上下文**：`map=<uniqueID> pawn=<LabelShort>#<id> thing=<defName>#<id>[x数量] job=<defName>#<loadID>`；
- **调用点**：类全名 + 方法名（只在输出时取栈）；
- 异常：`ErrorOnce` 保留「消息 + 调用点 + 异常类型」一次性去重。

---

## 五、有意保持静默

- 全部闸门拒绝与决议让出（正常工作）；
- 无库存 / 预留竞态 / 不可达 / 背包满 / 端点拒收（正常回退）；
- 无端点的地图（正常状态）；
- 医疗账单补料被原版医药等级拒绝（S17 正常回退：`IsUsableIngredient` 直接拒绝 + 兜底不补料，由原版给出 `NoMedicineMatchingCategory`）；
- 输出安装时取消旧蓝图（与原版 `Designator_Install` 同语义，正常替换）；
- 安装放置器右键 / ESC 取消（不取货、不扣账）；落点非法只是幽灵预览拒绝（W31）；
- 纯显示层 catch、CE 反射探测可选路径、资源统计只读短路径、读档清理旧镜像、设置 clamp。

---

## 六、验收场景

| # | 场景 | 期望 |
|---|---|---|
| 1 | 正常工作（搬运、存入、取货成功） | **无任何日志** |
| 2 | 工作台产物直接进超维空间 | 成功无日志 |
| 3 | 金库格被建筑占 / edifice 槽被清 | `镜像/落点已满`（Error）+ `金库/登记自愈`（一次性） |
| 4 | 回滚阶段异常 | `取货/回滚失败（单件，已尝试兜底落地）`（Error，带 index 与 reservation）；兜底也失败再记 `取货/回滚兜底落地失败`；其余件仍按通道回滚 |
| 5 | 取货执行器抛出异常 | `取货/执行器异常`（Error，异常不外泄） |
| 6 | 旧存档缺字段 | `存档/旧档字段补齐 字段=…`（一次性 Warning） |
| 7 | 启动 | `Harmony 补丁类应用完成：A/B = 112/112 个。` + `镜像守卫注册表：A/B = 19/19` + `投影 owner 桥：A/B = 10/10`（全量成功）+ 可用性 + 端点统计 |
| 8 | 输出安装且已有在册蓝图（含任务绑定物） | 旧蓝图被取消，只保留新蓝图；无日志 |
| 9 | 输出安装建蓝图抛异常 | `输出/安装失败（物资已回滚）`（ErrorOnce）+ 物品回库 + 无半生成蓝图 |
| 10 | 逆重飞船换图 / 整图卸载 | `镜像/结构撤离`（首条，reason + count）；账本数量不变；新图金库为 spawned、镜像按账本重建；**无** `already spawned` 红字 |
| 11 | 选中带「取消种植 / 取消施工 / 清除优先工作」gizmo 的对象，按 C | 只执行原版命令；超维侧栏不弹出（同键 gizmo 让位） |
| 12 | 建造模式（设计器选中 / Architect 页打开），按 C / Q / E | 原版取消设计器 / 左旋 / 右旋照常；侧栏、输出、使用都不触发 |
| 13 | `Targeter` 正在选目标、或文本框聚焦时按 C | 不打开侧栏，按键交给原版 |
| 14 | 无任何原版占用，按 C / Q / E | 侧栏开关 / 输出 / 使用按原逻辑执行 |
| 15 | 开发者模式启动 / 新档 / 读档 | `[超维存储][状态] 运行期静态状态已清理：E1 登记 18 项。`（静态 18；适配器动态项另计） |
| 16 | 同进程换档（存档 A → 更旧的存档 B），B 中同 `thingIDNumber` 的小人 | 不继承 A 的换弹冷却 / 补弹优先级 / 补料节流 / 派活退避；首次 tick 即按 B 的账本重新判定 |
| 17 | 可安装缩装点「输出」（行内同品质／材质） | 不弹数量框；直接进入原版安装放置器（幽灵预览、Q / E 旋转）；右键或 ESC 取消后库存数量不变、无日志 |
| 18 | 核心 mini / 搬到新位置 / 拆除（W37） | 旧格释放可建造；金库在新锚点附近；无结构性端点且无可用芯片 Pawn 超过 250 tick 后金库移除；账本 / 财富不变；旋转与 Pawn 行走不搬金库 |

---

## 七、可选调试能力（未实现）

若以后需要“为什么没接管”的逐条原因，应做成**默认关闭**的调试开关（不是常开日志）：
打开后按 reason 输出 `存入/闸门:*`、`存入/优先级让出`、`端点/自身端口离线` 等，
关闭时零开销。当前版本不提供。

---

## 八、手工验收矩阵（取货 / 交付）

> 矩阵轴：看 / 读 / 找 / 取 / 存 × 虚拟 / 保真 × Pawn / 非 Pawn × 存读档 × 多地图 × 并发。以下为取货 / 交付维度的展开。

| 维度 | 检查点 | 期望结果 |
|---|---|---|
| 手 | 搬运、账单原料等取到手上 | 真身进手；镜像仍作为原 Job 目标；不提前落地、不额外打来源标记 |
| 背包 | 存入类 Job 用背包携带 | 物品在背包内；Job 挂起或结束时物品不被吐到地上 |
| 背包 | 物化实物被第三人搬进其背包 | 视为已交付 / 放弃：不回滚、不按数量加回账本、不追回，Job 失败并记诊断 |
| 容器 | 原版容器投递（先判接收、再换真身） | 容器拒收时回滚且不扣账；成功时真身 == 物化的那一件，不按品类找替代 |
| 容器 | 端点当容器使用（写穿） | 投递返回值回填为实际接收量；后续释放与回调照常执行 |
| 装备 | 装备一件 | 单件 owner 只取 1 件真身，不得把整堆一次装走；走容器契约，不默认落地 |
| 衣物 | 穿戴 | 最终闸拒绝镜像进入已穿列表；只取 1 件真身，部位 / 生物编码 / 可穿判定保留 |
| 棺材 | 尸体 / 保真品入棺 | 真身原样交付；入棺拒收走保真回滚（不受筛选影响），不把真身丢在地上 |
| 运输舱 | 装舱 | 真身先到手上再交原版容器；按真身身份定位装载行，不靠同品类回退 |
| 运输舱 | 装载行读档恢复 | 展示物引用不跨档，按身份 + 欠量重建行；绑定不足只记一次诊断 |
| 交易 | 成交结算 | 以成交时刻可用量再校验后瞬时结算；同一 key 多行共享一份预算逐行递减；不足整笔失败，不少给一部分 |
| 编成 | 出发结算 | 虚拟桶成员收不下、保真件回退分别走对应回滚通道；差额留在账本 |
| 保真 | 保真取用 / 交付 | 按真身引用预留，原样交付，不重建、不镜像；只读可用性走统一查询 |
| CE | 弹药取用 / 补弹 | 请求先夹到可用量再以足额口径请求；执行期库存变化则整笔失败并交回 CE 原生分支 |
| 打断 | Job 挂起 / 终止 | 挂起保留预留；真正结束才释放；未交付部分精确回滚，不复制、不丢失 |
| 异常 | 执行器物化抛异常 | `Begin` 成功后执行器必须 `Rollback`；已物化实物按通道回充，预留不得留到下次读档 |
| 打断 | 携带途中被打断 | 在携带栏的物品按原版落地：不入账、不丢件；背包内物品仍按挂起规则 |
| 存档 | 读档恢复 | 只有预留未物化 → 释放预留；已物化且实物仍在 → 优先回充，失败落地；已销毁 → 不回加账本 |
| 存档 | 预留审计 | `id ≤ 0`（负数 = Instant 残留，0 = 非法归属）一律清理；正 id 以 Vanilla 传入的判活委托（组合 `ISDS_ReclaimWorld.JobExists` 与 `Pawn.Destroyed`）复核，返回 false 即释放；由 Vanilla 在 `FinalizeInit` 与 250 tick 周期触发，不留永久占用量 |
| 存档 | 失效引用 | 只有 key 与数量、没有实际实物的条目绝不凭空加回账本 |
| 容量不足 | 事务创建前判定 | 负重 / CE 容量装不下就拒绝创建或让玩家拆单，绝不硬塞背包 |
| 多 key 部分不足 | 预留阶段 | 全部 key 校验通过才统一写入；任一不足则已预留全部释放，不留部分持有 |
| 多 key 部分不足 | 扣账阶段 | 某 key 扣账失败时已扣部分回充，整笔失败，不出现“钢已取、零部件没拿到却继续” |
| 多趟 | 一个 Job 多趟取货 | 多趟 = 多事务：每趟独立预留 / 扣账 / 回滚，同一数量只扣一次账 |
