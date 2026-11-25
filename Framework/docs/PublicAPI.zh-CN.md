# 超维存储 · 公共 API 契约

> 定位：本文件是**对外公共 API 契约**的唯一权威（五轴 + 适配器规范 + 派生登记表）。
> 本文件**不定义**看 / 读 / 找 / 取 / 存的具体规则；规则 ID 与规范句见《RuleIndex.zh-CN.md》，规则正文见其「权威文档」列。
>
> 关系：
> - **原版契约优先**：`IHaulDestination` / `IThingHolder` / `IHaulEnroute` / `ListerThings` / Region / `ThingOwner` 仍是主路径；
> - **公共 API 是门面**：只转发到现有唯一实现，不新增规则；
> - **适配器是翻译**：只做入向/出向转换，不决定目的地、不扣账、不改第三方工作逻辑；
> - **目标**：让第三方把本 Mod 库存视作原版的一员，而不是把本 Mod 当成一个特殊系统。
>
> **实现状态（2026-11-16）**：五轴门面与 9 个适配器全部落地；`SDS_API.Version = 4`。两个薄适配（牵引光束 / 天工穿梭机）的缺口与边界见 [SDS.ThirdModCompat/Interfaces.zh-CN.md](SDS.ThirdModCompat/Interfaces.zh-CN.md)。

---

## 一、总则

1. **门面，不是第二实现**  
   公共 API 的每个方法必须落到对应能力文档指向的唯一实现；API 自身不得另写排序、筛选、容量、优先级、事务或扣账逻辑。

2. **原版可识别**  
   可计数 / 可取走的库存表示必须是原版可识别的 `Thing` / 现有代理（`SDS_Proxy_*`）；只读标量快照（如 `SDS_ApiStockInfo`）与不透明句柄可以存在，但不得替代 `Thing` 参与原版枚举、预留或 `ThingOwner` 操作。

3. **一个表示**  
   同一件库存在同一上下文中只能有一个可见表示（镜像 / 投影 / 真实物化），门面不得制造第二份计数或第二个可被取走的对象。

4. **适配器只翻译**  
   适配器允许把第三方事件翻译成门面调用、把门面结果翻译回第三方期望的形状；不得复制门面内部的规则。

5. **原版契约优先**  
   能通过 `IHaulDestination` / `ListerThings` / Region / `ThingOwner` 解决的，不走公共 API；公共 API 只补“原版契约覆盖不到”的场景（非 Pawn、非 Job、非标准容器）。

6. **非目标**  
   公共 API 不是第二套库存系统，也不是第二套搬运框架；不提供“找货源 + 预留 + 携带 + 途中重选 + 批量事务”的完整调度。

---

## 二、五轴门面

| 轴 | 门面职责（已实现子集） | 规则权威 | 返回形态 |
|---|---|---|---|
| **看** | `listerThings` 单 def 视图（含镜像/代理）；holder 树 / ThingGrid / Region 仍走原版 API | 《RuleIndex.zh-CN.md》S 系 | 只读 `Thing` 列表 |
| **读** | 库存可用性快照（总数/可用/禁用/失败）+ 只读版本；候选投影/可达/过滤/排序仍由内部实现 | 《RuleIndex.zh-CN.md》R 系 | `SDS_ApiStockInfo` + `SDS_ReadVersions` |
| **找** | 非 Pawn 存入端点选择、Pawn 取物点选择；物品搜索仍走原版 `GenClosest` | 《RuleIndex.zh-CN.md》S 系 | `Thing` + 失败原因 |
| **取** | 事务级物化；返回真实 `Thing`，交付由调用方完成 | 《RuleIndex.zh-CN.md》W 系 | `List<Thing>` / `Thing` + 失败原因 |
| **存** | 单件原子入账；所有权/阵营/禁止/地图门在 API 边界 | 《RuleIndex.zh-CN.md》W 系 | 实际接收量 + 失败原因 |

> 门面只暴露“入口 + 结果”；具体过滤、排序、预留、失败策略以《RuleIndex.zh-CN.md》指向的权威文档为准，不在本文件复述。

### 二·五、五轴已实现方法

| 轴 | 方法 | 说明 |
|---|---|---|
| 版本 | `SDS_API.Version` | 当前为 `4`；第三方 soft-depend 探测 |
| 看 | `SDS_API.VisibleThingsOfDef(Map, ThingDef)` | 返回该图原版枚举可见的 `Thing`（含镜像/代理），只读 |
| 读 | `SDS_API.ReadStock(SDS_StockKey)` / `ReadStockFor(Thing)` | 总数 / 可用量 / 禁用 / 供给失败原因；只读；结果携带 `SDS_ReadVersions` |
| 读 | `SDS_API.CaptureReadVersions()` | 捕获只读版本快照，供调用方判定缓存失效 |
| 找 | `SDS_API.TryFindDepositEndpoint(...)` | 非 Pawn 存入端点选择；复用同一 Gate + Ranker |
| 找 | `SDS_API.TryFindWithdrawPoint(Pawn, ...)` | Pawn 取物点选择（自身端口 / 最近端点）；只读 |
| 存 | `SDS_API.TryDepositNow(...)` / `TryDepositAt(...)` | 单件原子入账；玩家阵营 + 同地图 + 所有权/禁止门 |
| 取 | `SDS_API.TryWithdraw(...)` | 事务级物化；返回真实 `Thing`，交付由调用方完成 |
| 取 | `SDS_API.TryWithdrawFromMirror(..., SDS_ApiCaller, ...)` | 从镜像取出真实物化结果；玩家阵营 + 同图 |
| 取 | `SDS_API.TryWithdrawPreservedExact(..., SDS_ApiCaller, ...)` | 保真通道精确真身取出；玩家阵营 |
| 类型 | `SDS_ApiCaller` / `SDS_ApiResultCode` / `SDS_ApiStockInfo` | 调用方上下文 / 结构化结果码 / 只读库存快照（含 `result`、`versions`） |

> `ReadStock` 在 G 关闭（世界上没有可用核心）时返回 `GridUnavailable`，`available = 0`、`failure = Insufficient`，`total` 仍按账本给出（R17）。

**版本演进（只记契约影响）**

- Version 2（2026-10-27）：删除无采用的 `HasAvailable` / `GetAvailableCount(ThingDef)` / `RegisterWarehouseContainer`；`ReadStock` 在 G 关闭时返回 `GridUnavailable`（R17）。
- Version 3（2026-10-28）：5 个公共辅助退出五轴契约（`GetAvailableCount` / `GetPreservedAvailableCount` / `IsMirror` / `TryGetDisplayKey` / `TryGetWithdrawCell`）。
- Version 4（2026-11-12）：适配器桥随 ThirdModCompat 单程序集化改为 public 方法挂在 `SDS_API` 上；桥面不属于五轴契约，登记见《ModuleInterfaces.zh-CN.md》五。

### 二·六、适配器桥

> `SDS_API_Adapters.cs` 是同一个 `SDS_API` 的 `partial` 文件，不是第二个 API。
> 桥面方法为 public（跨程序集没有 `InternalsVisibleTo`），但**不属于五轴契约**、不进入 `SDS_API.Version` 承诺；公开的 `TryWithdraw` 调用方重载位于 `SDS_API.cs`。

#### 二·六·1 适配器桥

> 桥面的唯一登记处是《ModuleInterfaces.zh-CN.md》五（`CheckCompatSurface.ps1` 从那里读取方法名）；本节不重复方法清单。

> `TryWithdrawVirtualByDef` / `TryWithdrawPreservedByDef` 只是对同一事务原语逐键组合，不新增优先级 / 筛选 / 扣账规则。

> **W33（2026-11-04）**：`RequiresPreservedStorage` / `ReadStockFor(Thing)` 的通道结果会随
> 「武器/装备强制虚拟化」开关变化（普通武器/衣物可返回虚拟键）；该查询仍只转发
> `SDS_DepositRules`，不新增第二套规则。属适配器桥，不外承诺，适配器必须按查询结果走对应通道。

> **W35（2026-11-07）**：`RequiresPreservedStorage(def)` 的**返回值集合扩大**（签名不变）——
> `ThingDef.IsStudiable` 的物品一律返回「保真」，且该判据只消费原版能力位，不列举 mod / defName / 组件。
> 该查询仍只转发 `SDS_DepositRules`，不新增第二套规则；调用方必须按结果走对应通道，不得按旧结果缓存跨版本复用。

#### 二·六·2 五轴外的公共辅助

| 方法 | 说明 |
|---|---|
| `TryWithdraw(key, count, mode, SDS_ApiCaller, ...)` | 非 Pawn / 适配器重载；玩家阵营 + 当前地图（无 Pawn 时）；位于 `SDS_API.cs` |

> 桥面方法（含原 5 个公共辅助）均只转发现有唯一实现；不新增库存表示、排序或扣账规则；第三方应使用 §二·五 的五轴入口。

### 二·七、公共面清单（派生）

> 唯一权威仍是 §二·五 / §二·六；本表只登记五轴契约的“public 成员 ↔ 类别 ↔ 调用方 ↔ 采用证据”，供 `CheckCompatSurface.ps1` 对账。
> 桥面方法（public 但非五轴契约）登记在《ModuleInterfaces.zh-CN.md》五，不在本表；公共类型（`SDS_ApiCaller` / `SDS_ApiResultCode` / `SDS_ApiStockInfo` / `SDS_ReadVersions` / `SDS_StockKey` / `SDS_WithdrawMode`）按 §二·五 人工审阅。

| 成员 | 类别 | 文档节 | 本 Mod 调用方 | 外部采用证据 |
|---|---|---|---|---|
| `SDS_API.Version` | 契约 | 二·五 | —（第三方探测） | 待补 |
| `SDS_API.VisibleThingsOfDef` | 契约 | 二·五 | 无 | 待补 |
| `SDS_API.ReadStock` | 契约 | 二·五 | 无 | 待补 |
| `SDS_API.ReadStockFor` | 契约 | 二·五 | 无 | 待补 |
| `SDS_API.CaptureReadVersions` | 契约 | 二·五 | 无 | 待补 |
| `SDS_API.TryFindDepositEndpoint` | 契约 | 二·五 | 无（对外契约保留；牵引光束已固定目的地，按端点 `Accepts` 判定） | 待补 |
| `SDS_API.TryFindWithdrawPoint` | 契约 | 二·五 | 无 | 待补 |
| `SDS_API.TryDepositNow` | 契约 | 二·五 | 无 | 待补 |
| `SDS_API.TryDepositAt` | 契约 | 二·五 | 无 | 待补 |
| `SDS_API.TryWithdraw` | 契约 | 二·五 / 二·六·2 | `SDS_ModCompat_CE`、`SDS_ModCompat_EquivalentExchange` | 待补（两个重载合并为一行） |
| `SDS_API.TryWithdrawFromMirror` | 契约 | 二·五 | 无 | 待补 |
| `SDS_API.TryWithdrawPreservedExact` | 契约 | 二·五 | `SDS_CompatServices` | 待补 |

---

## 三、契约规则

### 3.1 薄门面

- 每个 API 方法只能调用一个对应的唯一服务；不得在 API 层组合出第二套流程。
- 允许的组合仅限于“同一事务内的必要步骤”（例如选端点后立即入账），且必须复用同一套门控与排序。

### 3.2 原版可识别

- 对外返回的对象必须是第三方可以按原版方式使用的 `Thing` / 代理；不能返回“只有 SDS 认识”的库存条目。
- 若必须返回句柄，句柄只能用于再次调用 SDS API，不能替代 `Thing` 参与原版枚举、预留、`ThingOwner` 操作。

### 3.3 一个表示

- 同一件库存在同一上下文中只能有一个可见表示；门面返回的表示必须与镜像可见性口径一致（见 [SDS.Vanilla/Rules.zh-CN.md](SDS.Vanilla/Rules.zh-CN.md) §十一）。
- 不允许“镜像里一份 + API 结果里一份”同时可被第三方计数或取走。
- 未解析条目、品类禁用、不可见条目不得通过 API 出现在候选里。

### 3.4 调用方上下文

- 每个会改变状态或涉及可达性的 API 必须携带调用方上下文：`Pawn` / `Faction` / `claimOwner`（建筑或机械体）。
- 会改变状态或涉及预留的 API 只服务**玩家阵营**；只读查询不限。`TryFindWithdrawPoint` 也要求玩家阵营（它通常用于后续取货）。
- 没有 Pawn 的调用方必须显式声明“非 Pawn 模式”；该模式没有自身端口、AllowedArea、Pawn 预留；距离只按参考格计算。
- 存入要求**调用方、实物、端点在同一地图**；端点阵营必须为空或等于调用方阵营。
- 镜像取出：有 Pawn 时要求 Pawn 与镜像同图；无 Pawn 时要求镜像是当前地图（保证回滚地图正确）。
- 保真精确取出：有 Pawn 时用其地图回滚；无 Pawn 时使用当前地图。
- 所有权 / 阵营 / 禁止检查由门面补齐，不能只依赖底层 `grid.TryDeposit` / `TryWithdraw`。
  当前实现：`thing.Faction` 与根持有者阵营必须等于调用方；无主物品放行；禁止物品直接拒绝；尸体按实物阵营判定，不追查 `InnerPawn`。
  这是 API 边界规则，不复用 `HaulAIUtility.PawnCanAutomaticallyHaulFast`；两处口径变更时必须同步检查。

### 3.5 事务后置条件

- **取**：成功 → 物品已完成账本扣减并物化为真实 `Thing`，**交付由调用方完成**；失败 → 账本与可回滚实物回到调用前状态。
- **存**：成功 → 实物已被消费（虚拟通道销毁 / 保真通道转交）；失败 → 实物原样留在调用方，不 SplitOff 一半。
- 调用方持有的 `Thing` 引用在成功后可能失效；契约必须写清“引用失效”与“重新解析”的方式。

### 3.6 失败码

- 状态型门面方法返回 `SDS_ApiResultCode`；只读快照（`SDS_ApiStockInfo`）用 `result` 字段；列表型只读（`VisibleThingsOfDef`）异常时返回空集并记一次诊断。
- `SDS_ApiResultCode` 是 **API 边界码**，与内部判定结果（`SDS_SupplyFailure`、端点阶段结论）对应，但不直接暴露内部枚举：
  - `SDS_SupplyFailure` → `UnresolvedDef` / `Forbidden` / `NoStock` / `RebuildFailed`（`RebuildFailed` 为预留映射，当前无产出）；
  - 端点阶段 → `EndpointUnavailable` / `NotDepositable` / `DepositFailed`。
- 异常统一返回 `InternalError`（或空集 / default）并记一次 `公共API/*异常`（`SDS_Diag.ErrorOnce`），异常不外泄给第三方。
- 正常拒绝（不可达 / 无库存 / 筛选拒绝 / 容量满 / 端点离线 / 非玩家阵营 / 跨地图）不写日志。

### 3.7 非 Pawn 模式

- 非 Pawn 模式是同一解析器 `TryFindBestCore` 的**参数分支**（`actor == null`），不是第二套 resolver：
  - `actor == null`；
  - `requireReach == false`；
  - `allowSelf == false`；
  - 距离按 `reference` 格计算；
  - 优先级 / 筛选 / 容量仍走同一套门控与排序。
- 端点枚举使用同一份注册表（只取建筑端点，不取 Pawn 端口）。
- `SDS_API` 额外传入阵营过滤器；这属于 API 边界，不改变 Ranker 规则。

### 3.8 版本与依赖

- 提供 `SDS_API.Version` 或等价的能力探测；第三方应以 soft-depend（反射探测）方式接入。
- 新增/修改门面方法必须更新本文件，并标注兼容性影响。
- **E5（2026-10-27）**：**Version 变更即兼容性声明；无外部采用可直接 break；有采用需保留薄转发至少一个大版本。**
  - “外部采用”的判定：至少存在一条公开的第三方引用 / 兼容适配器 / 用户报告；没有证据即视为无采用。
  - 薄转发只做翻译，不得复制规则（准则三）；旧入口内部必须调用当前唯一实现。
  - 任何 Version 变更都必须在 §二·五 标注兼容性影响（参照下方的 Version 2 记录）。
- **Version 2（2026-10-27）兼容性影响**：删除了未被本 Mod 使用、且无第三方采用记录的三个旧入口；五轴门面签名不变；`ReadStock` 在 G 关闭（无可用核心）时改为返回 `GridUnavailable`、`available = 0`、`total` 仍按账本给出（R17）。
- **Version 3 / 4 兼容性影响**：原 5 个公共辅助退出五轴契约，并在 Version 4 作为适配器桥保持 public（只服务本 Mod 兼容层）；五轴 public 签名不变。

### 3.9 线程与生命周期

- 所有 API 只在 RimWorld 主线程、游戏内 tick 上下文中调用。
- 返回的临时视图 / 句柄默认“仅本次调用有效”，除非明确标注可跨 tick 持有。

---

## 四、适配器规范

### 4.1 入向 / 出向

- **入向**：第三方查询 / 动作 / 事件 → 门面调用。
- **出向**：门面结果 / 失败码 → 第三方期望的形状（`Thing`、数量、Job、回退原因）。

### 4.2 禁止事项

适配器不得：

1. 自己决定目的地优先级、筛选、容量、平局；
2. 自己维护库存缓存或影子数量；
3. 改写第三方的 Job / Toil 工作逻辑；
4. 绕过门面直接调用 `SDS_GameComponent_Grid` / `SDS_EndpointHandle` 的内部路径；
5. 同时走“门面”和“SDS 的 Harmony 补丁”两条路。

### 4.3 双触发

- 同一第三方入口只能有一条接管路径：要么它调用门面，要么兼容层翻译；不得两者同时生效。**E7**：只读投影可以与适配器翻译共存，但不得存在第二条扣账 / 派活 / 回滚写路径。
- 门面调用需具备幂等标记或调用方上下文，避免重复入账 / 重复派活。

### 4.4 不合作 Mod

- 对不调用门面的第三方，仍走“原版契约 + 已记录的边界”；不为单个 Mod 在核心新增目的地特例。
- 如需兼容，优先做独立、可关闭的薄适配器；适配器只翻译，不复制规则。
- **不做轮询对账**：不为第三方自造产物建立定时扫描 / 比对通道——靠轮询对齐会形成第二条扣账通道；只承认显式门面调用与已登记的挂点（§4.8）。

### 4.5 “全部走公共 API”的正确定义

- 指：**所有适配器的业务规则收敛到公共 API**；
- 不指：取消适配器、强制第三方改道、或让公共 API 取代原版契约。
- **分层（2026-10-28 步骤 4）**：外部入口（补丁 / UI / JobDriver / 第三方适配器）只经本文件的公共门面或《ModuleInterfaces.zh-CN.md》五的适配器桥；服务层（`SDS_Trade` / `SDS_CaravanTransport` / `SDS_Preserved*`）可直接调唯一执行器，但不得自行排序 / 扣账 / 选端点。
- **兼容层边界（2026-10-28）**：`SDS_ModCompat*` 只允许引用 `SDS_API`、`SDS.Contracts` 的公共类型（`SDS_StockKey` / `SDS_WithdrawMode` / `SDS_ApiCaller` / `SDS_ApiResultCode` / `SDS_ApiStockInfo` / `SDS_ReadVersions` / `SDS_EETransmuteState`）、`SDS_Compat*` 自身基础设施与 `SDS_Diag`；不得引用 `SDS_GameComponent_Grid` / `SDS_DepositService` / `SDS_WithdrawExecutor` / `SDS_MirrorRegistry` / `SDS_ChipManualDeposit` 等内部实现。校验：`CheckPatchInventory.ps1` 的 `[兼容直调]`（allowlist）+ `CheckCompatSurface.ps1`（§二·七 公共面 / §4.8 入口归属 / 调用图）。**E6**：本边界是工程约束，不是玩法规则。

### 4.6 使用命令的扩展登记（目标，未实现）

- provider 类型名 / def 谓词登记属于尚未实现的入口扩展；目前没有公开方法。
- 若实现，统一走本文件定义的版本化 `SDS_API` 入口；不得在适配器里私自定义排除表。

### 4.7 文件归属（强制）

- **所有第三方 Mod 适配器只放在 `超维存储/Source/SDS.ThirdModCompat/SDS_ModCompat_*.cs`**（单程序集、按来源分文件）：类名为 `SDS_CompatAdapter_*`；`SDS_CompatServices.cs` 放共用 Sdk 工具类（`SDS_CompatTools` / `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply`）；挂点 A/B 报告 DTO 与 Hub 属 `SDS.Diag`（`SDS.Diag_API.cs`，实现 `SDS_CompatReports.cs`）；其余按「来源」各一个文件（Pick Up And Haul / Combat Extended / 联合轻工家具拓展-UF Light Industries Furniture / 牵引光束搬运器Manipulator Beam Emitter / 米莉拉扩展-未来科技Milira Expanded - Future Technology / 天工模块化穿梭机 CeleTech Shuttle Extension / ProjectE 等价交换 / VC载具框架 VersatranZeroOne）。不得散落到其它目录。
- 单个第三方 Mod 的适配放在它自己的 `SDS_ModCompat_<名字>.cs`（没有就新建该文件），不得混进别人的文件、也不得新建其它前缀的适配文件。
- 本约束只管「适配器放哪里」，不改变 §四 的翻译职责边界。

### 4.8 兼容入口归属（派生）

> 唯一目的：让“每个第三方入口只有一个写接管路径”（§4.3）可审计。
> `CheckCompatSurface.ps1` 的机器校验粒度是「文件 + 符号」；真正的 Hook 级双触发仍需人工审阅。
> 入口键在同一张表内不得重复；每个 `SDS_ModCompat_*.cs` 至少一行。

| 第三方 | 入口键 | 归属 | 源码锚点 | 允许调用 |
|---|---|---|---|---|
| Pick Up And Haul | `PUAH.HasJobOnThing` | 适配器 | `SDS_ModCompat_PUAH.cs: HasJobOnThing` | 《模块接口》五 Job 桥 |
| Pick Up And Haul | `PUAH.JobOnThing` | 适配器 | `SDS_ModCompat_PUAH.cs: JobOnThing` | 《模块接口》五 Job 桥 |
| Pick Up And Haul | `PUAH.UnloadDispatch` | 适配器 | `SDS_ModCompat_PUAH.cs: UnloadDispatchPrefix` | 《模块接口》五 Job 桥 |
| Combat Extended | `CE.ReloadTurret` | 适配器 | `SDS_ModCompat_CE.cs: WorkGiver_ReloadTurret, ReloadTurretHasJobPostfix, ReloadTurretJobPrefix` | public `TryWithdraw` + 桥 |
| Combat Extended | `CE.ReloadAutoLoader` | 适配器 | `SDS_ModCompat_CE.cs: WorkGiver_ReloadAutoLoader, ReloadAutoLoaderHasJobPostfix, ReloadAutoLoaderJobPrefix` | public `TryWithdraw` + 桥 |
| Combat Extended | `CE.ManTurrets` | 适配器 | `SDS_ModCompat_CE.cs: JobGiver_ManTurretsNearSelfCE, FindAmmoForTurret` | public `TryWithdraw` + 桥 |
| Combat Extended | `CE.CheckReload` | 适配器 | `SDS_ModCompat_CE.cs: JobGiver_CheckReload, JobTrackerTick` | public `TryWithdraw` + 桥 |
| Combat Extended | `CE.DefenderReload` | 适配器 | `SDS_ModCompat_CE.cs: JobGiver_DefenderReloadTurret` | public `TryWithdraw` + 桥 |
| Combat Extended | `CE.PickupAmmo` | 适配器 | `SDS_ModCompat_CE.cs: TryPickupAmmo` | public `TryWithdraw` + 桥 |
| Combat Extended | `CE.PrepareShot` | 适配器 | `SDS_ModCompat_CE.cs: TryPrepareShot` | public `TryWithdraw` + 桥 |
| Combat Extended | `CE.FindBestAmmo` | 适配器（只读翻译，不与核心搜索补丁重复写） | `SDS_ModCompat_CE.cs: JobGiverUtils_Reload, FindBestAmmo` | `IsMirror` / 真实物筛选 |
| Combat Extended | `CE.ReloadDriverGuard` | 适配器（驱动兜底） | `SDS_ModCompat_CE.cs: JobDriver_ReloadTurret, JobDriver_ReloadAutoLoader` | `IsMirror` |
| 联合轻工家具拓展-UF Light Industries Furniture | `UF.PullIngredients` | 适配器 + 共享补料服务 | `SDS_ModCompat_UFRecipes.cs: TryPullIngredientsFromCandidates` | `SDS_CompatBillSupply` |
| 牵引光束搬运器Manipulator Beam Emitter | `Beam.SpecialContainerType` | 适配器 | `SDS_ModCompat_ManipulatorBeam.cs: IsSpecialContainerTypeEnabled` | public `IsDepositEndpoint` |
| 牵引光束搬运器Manipulator Beam Emitter | `Beam.ScanCache` | 适配器 | `SDS_ModCompat_ManipulatorBeam.cs: SpecialContainers` | public `IsDepositEndpoint` |
| 牵引光束搬运器Manipulator Beam Emitter | `Beam.Transfer` | 适配器 | `SDS_ModCompat_ManipulatorBeam.cs: TryFindSpecialContainerTransfer` | public `IsDepositEndpoint` + 端点 `IHaulDestination.Accepts`（原版契约；目的地已由对方固定，不拿「找」结果比对） |
| 米莉拉扩展-未来科技Milira Expanded - Future Technology | `Milira.AutoProduction` | 适配器 + 共享补料服务 | `SDS_ModCompat_MiliraExpanded.cs: CompVirtualProductionDevice, CompTick` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| 米莉拉扩展-未来科技Milira Expanded - Future Technology | `Milira.AutomatedRecipeCycle` | 适配器 + 共享补料服务 | `SDS_ModCompat_MiliraExpanded.cs: TryProcessAutomatedRecipeCycle` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| 米莉拉扩展-未来科技Milira Expanded - Future Technology | `Milira.ManualRecompose` | 适配器 + 共享补料服务 | `SDS_ModCompat_MiliraExpanded.cs: TryProcessManualRecomposeBill` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| 米莉拉扩展-未来科技Milira Expanded - Future Technology | `Milira.RecomposeOrder` | 适配器 + 共享补料服务 | `SDS_ModCompat_MiliraExpanded.cs: TryProcessRecomposeOrder` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| 米莉拉扩展-未来科技Milira Expanded - Future Technology | `Milira.DecomposeOrder` | 适配器 + 共享补料服务 | `SDS_ModCompat_MiliraExpanded.cs: TryProcessDecomposeOrder` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| 米莉拉扩展-未来科技Milira Expanded - Future Technology | `Milira.LifeEssenceOrder` | 适配器 + 共享补料服务 | `SDS_ModCompat_MiliraExpanded.cs: TryProcessLifeEssenceOrder` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| 天工模块化穿梭机 CeleTech Shuttle Extension | `CeleTechShuttle.BuildCargoTransferables` | 适配器（只读投影，清单注入） | `SDS_ModCompat_CeleTechShuttle.cs: TryApply, BuildCargoTransferablesPostfix` | 桥 `SDS_API.InjectTransportLoadCandidates`；不写队列 / 不派活 / 不扣账 |
| ProjectE 等价交换 | `EE.TransmuteAccept` | 适配器 | `SDS_ModCompat_EquivalentExchange.cs: TryAcceptPrefix, TryAcceptFinalizer` | `TryGetDisplayKey` / `TryWithdraw` + `TryReturnVirtual` |
| VC载具框架 VersatranZeroOne | `Versatran.CargoInstantLoad` | 适配器（写翻译） | `SDS_ModCompat_Versatran.cs: LoadPrefix, LoadPostfix` | `SDS_API.TryWithdraw` / `TryWithdrawPreservedExact` + `TryReturnVirtual` / `TryReturnPreserved`；入舱用车辆自己的 `TryAddOrTransferThingToCargoStrict` |
| VC载具框架 VersatranZeroOne | `Versatran.CargoRefresh` | 适配器（只读投影） | `SDS_ModCompat_Versatran.cs: RefreshPostfix` | `SDS_API.ReadStock` / `GetAvailableCount` / `AppendPreservedThings` / `IsLivePreservedThing` / `IsStockForbidden` |
| VC载具框架 VersatranZeroOne | `Versatran.MirrorGuard` | 适配器（只读守门） | `SDS_ModCompat_Versatran.cs: CanStorePrefix` | `SDS_API.IsMirror` / `IsLivePreservedThing` |
| 边缘米莉拉养成：与你相连之人 | `MiliraGift.ReceiveGift` | 适配器 | `SDS_ModCompat_MiliraGift.cs: TryMakeReservationsPrefix` | public `IsMirror` / `IsGridUsable` / `HasAnyEndpoint` / `TryGetWithdrawCell` / `TryWithdrawFromMirror` / `TryReturnVirtual`（经 `SDS_CompatMirrorSeam`） |

### 4.9 适配器规则分类（派生）

> 判定标准：适配器只允许“读第三方状态 / 翻译参数 / 调门面 / 翻译结果”；出现“选哪个、多少、够不够、先后顺序、回滚顺序”等判定即为规则，必须上收到唯一实现。
> 本表只记录分类结论；规则本体仍以对应能力文档为准。

| 适配器 / 基础设施 | 代码位置 | 判定 | 唯一规则 / 说明 |
|---|---|---|---|
| `SDS_BillSupplyPlanner` | `SDS.Rules/SDS_BillSupplyPlanner.cs`（适配器查询在 `SDS_BillIngredientQuery.cs`） | **规则实现** | S16：选 def / required / fromOwn / shortfall / 每个 def 只预留一次；配方槽位与 def 列表两个入口共用 `TryPlanFromCandidates` 核心 |
| `SDS_ModCompat_PUAH` | `SDS_ModCompat_PUAH.cs` | 纯翻译 | 只走 《模块接口》五 Job 桥 |
| `SDS_ModCompat_CE.TryTakeForCe` | `SDS_ModCompat_CE.cs` | 翻译 + 组合 | 可用量来自 `SDS_API.TryFindWithdrawableKey`；不重算容量 / 优先级 |
| `SDS_CompatBillSupply` | `SDS_CompatServices.cs` | 翻译 + 复用原版算法 | 只筛保真候选 + 调原版 `TryFindBestBillIngredientsInSet`；槽位判定问 `SDS_API.IsBillIngredientAllowed` |
| `SDS_ModCompat_ManipulatorBeam` | `SDS_ModCompat_ManipulatorBeam.cs` | 纯翻译 | 只用 public `IsDepositEndpoint` 判端点身份 + 端点 `IHaulDestination.Accepts` 判收不收（牵引光束已固定目的地） |
| `SDS_ModCompat_MiliraExpanded` | `SDS_ModCompat_MiliraExpanded.cs` | 翻译 + 执行 | 只提供自有量 / 外部可用量 / 可存入 / 一个周期用量（第三方公式翻译），并执行取货 / 存入；规划问 `SDS_API.TryPlanBillSupplySlot`（配方）/ `SDS_API.TryPlanAllowedDefSupply`（无配方订单） |
| `SDS_ModCompat_CeleTechShuttle` | `SDS_ModCompat_CeleTechShuttle.cs` | 纯翻译（只读注入） | 只经 桥 `SDS_API.InjectTransportLoadCandidates` 转发唯一注入实现；不选行、不取货、不扣账（W50） |
| `SDS_ModCompat_Versatran` | `SDS_ModCompat_Versatran.cs` | 翻译 + 执行 | 只读投影：镜像行上限 = `ReadStock.available`、保真真身行来自 `AppendPreservedThings`；虚拟走 `TryWithdraw`、保真走 `TryWithdrawPreservedExact`；入舱用车辆自己的方法，不自定义容量 / 优先级 / 通道 |
| `SDS_ModCompat_EquivalentExchange.ConvertRows` | `SDS_ModCompat_EquivalentExchange.cs` | 事务组合 | 只调 `SDS_API.GetAvailableCount` / `TryWithdraw` / `TryReturnVirtual`；不自定回滚顺序 |
| `SDS_CompatMirrorSeam` | `SDS_CompatServices.cs` | 翻译 + 执行（共享实现） | 第三方 Job 预约前的端点物化唯一实现：取端点格 / 取货 / 复制 comp 字段 / 落点 / 改写 target / 失败回账；不选端点、不算可用量、不定义规则 |
| `SDS_ModCompat_MiliraGift` | `SDS_ModCompat_MiliraGift.cs` | 纯描述符 + 挂点 | 只声明 `JobDriver_IdealReceiveGift` 的类型、字段与取 1 件；物化行为全在 `SDS_CompatMirrorSeam` |
| `SDS_CompatIngredientRule` | 已删除 | — | 规则上收到 S16，不再保留第二个规则出口 |

> 第三方自身公式（例如米莉拉“一个周期吃几件”）只产出 `need` 输入，属于翻译参数；目标量、选 def、缺口与预留仍由 `SDS_BillSupplyPlanner` 决定（S16）。

### 4.10 调用链（派生）

> 唯一目的：把「入口 → 适配器 → 门面方法 → 唯一实现」写死，防止适配器重新长出第二套实现。
> 机器校验：`CheckCompatSurface.ps1` 校验文件存在、门面方法在 `SDS_API` 里可分类、唯一实现类名在源码里存在。
> Version 4 起，下列调用中的 `GetAvailableCount` / `GetPreservedAvailableCount` / `IsMirror` / `TryGetDisplayKey` / `TryGetWithdrawCell` 属于《ModuleInterfaces.zh-CN.md》五的适配器桥（public，但非五轴契约）。

| 适配器 | 入口组 | 门面方法 | 唯一实现 |
|---|---|---|---|
| `SDS_ModCompat_PUAH.cs` | Pick Up And Haul Job / 卸载 | `SDS_API.TryTakeDepositCandidate`、`SDS_API.TryMakeDepositJob`、`SDS_API.TryMakeDepositJobFromInventory`、`SDS_API.IsDepositJob`、`SDS_API.IsPawnPortOperational`、`SDS_API.IsProducedPendingConsumption` | `SDS_DepositService`、`SDS_JobDefOf`、`SDS_ProducedThingMarker`、`SDS_ImplantUtility` |
| `SDS_ModCompat_CE.cs` | Combat Extended 换弹 / 炮塔 / 捡弹 | `SDS_API.IsGridAvailable`、`SDS_API.IsGridUsable`、`SDS_API.IsManualDepositAvailable`、`SDS_API.IsMirror`、`SDS_API.MarkProducedPendingConsumption`、`SDS_API.TryFindWithdrawableKey`、`SDS_API.TryGetWithdrawCell`、`SDS_API.TryReturnVirtual`（`landOnFailure: false`）、`SDS_API.TryWithdraw` | `SDS_WithdrawExecutor`、`SDS_Availability`、`SDS_Core`、`SDS_ProducedThingMarker`、`SDS_ReturnUtility`、`SDS_ChipManualDeposit` |
| `SDS_ModCompat_UFRecipes.cs` | 联合轻工家具拓展-UF Light Industries Furniture 全自动打印机 | `SDS_API.AppendPreservedThings`、`SDS_API.IsBillIngredientAllowed`、`SDS_API.IsGridAvailable`、`SDS_API.IsLivePreservedThing`、`SDS_API.IsStockForbidden`、`SDS_API.GetAvailableCount`、`SDS_API.TryWithdrawPreservedExact` | `SDS_BillSupplyPlanner`、`SDS_WithdrawExecutor`、`SDS_PreservedBuckets`、原版 `TryFindBestBillIngredientsInSet` |
| `SDS_ModCompat_ManipulatorBeam.cs` | 牵引光束搬运器Manipulator Beam Emitter 特殊容器 | `SDS_API.IsDepositEndpoint` | `SDS_EndpointHandle`、端点 `IHaulDestination.Accepts` |
| `SDS_ModCompat_MiliraExpanded.cs` | 米莉拉扩展-未来科技Milira Expanded - Future Technology 自动 / 手动 / 订单 / 分解 | `SDS_API.TryPlanBillSupplySlot`、`SDS_API.TryPlanAllowedDefSupply`、`SDS_API.NotifyBillSupplyDeposited`、`SDS_API.RequiresPreservedStorage`、`SDS_API.TryReturnPreserved`、`SDS_API.TryReturnVirtual`、`SDS_API.GetAvailableCountForDef`、`SDS_API.TryWithdrawVirtualByDef`、`SDS_API.TryWithdrawPreservedByDef` | `SDS_BillSupplyPlanner`、`SDS_WithdrawExecutor`、`SDS_ReturnUtility` |
| `SDS_ModCompat_CeleTechShuttle.cs` | 天工模块化穿梭机 CeleTech Shuttle Extension 装载清单注入 | `SDS_API.InjectTransportLoadCandidates` | `SDS_TransportVirtual` |
| `SDS_ModCompat_EquivalentExchange.cs` | ProjectE 等价交换 转化对话框 | `SDS_API.GetAvailableCount`、`SDS_API.IsDisplayThing`、`SDS_API.IsMirror`、`SDS_API.TryGetDisplayKey`、`SDS_API.TryReturnVirtual`、`SDS_API.TryWithdraw` | `SDS_WithdrawExecutor`、`SDS_ReturnUtility` |
| `SDS_ModCompat_Versatran.cs` | VC载具框架车辆货舱装载 | `SDS_API.IsGridUsable`、`SDS_API.IsMirror`、`SDS_API.TryGetStockKey`、`SDS_API.ReadStock`、`SDS_API.GetAvailableCount`、`SDS_API.IsStockForbidden`、`SDS_API.AppendPreservedThings`、`SDS_API.IsLivePreservedThing`、`SDS_API.TryWithdraw`、`SDS_API.TryWithdrawPreservedExact`、`SDS_API.TryReturnVirtual`、`SDS_API.TryReturnPreserved` | `SDS_WithdrawExecutor`、`SDS_Availability`、`SDS_ReturnUtility` |
| `SDS_ModCompat_MiliraGift.cs` | 边缘米莉拉养成：与你相连之人 送礼 | `SDS_API.IsMirror`、`SDS_API.IsGridUsable`、`SDS_API.HasAnyEndpoint`、`SDS_API.TryGetWithdrawCell`、`SDS_API.TryWithdrawFromMirror`、`SDS_API.TryReturnVirtual` | `SDS_CompatMirrorSeam`、`SDS_WithdrawExecutor`、`SDS_ReturnUtility` |

### 4.11 双触发审计（派生）

> 判定：同一入口最多一条“写”路径；只读投影 + 适配器翻译可以共存。逐项结论见下表。
> 机器校验：`CheckCompatSurface.ps1` 校验入口键都在 §4.8、每个适配器都在 §4.11；语义仍需人工复核。

| 入口 | 核心路径 | 适配器路径 | 结论 |
|---|---|---|---|
| `PUAH.HasJobOnThing` / `PUAH.JobOnThing` / `PUAH.UnloadDispatch` | `Patch_SDS_JobGiverHaul` / `Patch_SDS_HaulGeneralToGrid` 走原版 WorkGiver | `SDS_ModCompat_PUAH` 走 Pick Up And Haul 自己的 WorkGiver / 卸载入口 | 不同 work giver；同一 Pawn 每 tick 只取一个 Job；能就地存才接管，否则让出 |
| `CE.FindBestAmmo` | `Patch_SDS_Closest*` 只做镜像距离投影（只读） | `FindBestAmmoMirrorFilterPostfix` 把镜像换成真实最近弹药 | 允许：核心只读、适配器翻译；不存在第二条取货扣账 |
| `CE.ReloadTurret` / `CE.ReloadAutoLoader` / `CE.ManTurrets` / `CE.CheckReload` / `CE.DefenderReload` / `CE.PickupAmmo` / `CE.PrepareShot` / `CE.ReloadDriverGuard` | 无同名核心写补丁 | `SDS_ModCompat_CE` 成对 `HasJobOnThing + JobOnThing` 或 `GetPriority + TryGiveJob`；驱动兜底只拒绝镜像目标 | 每条链只有 Combat Extended 自己的写出口；Combat Extended 能找到实物时适配器不插手 |
| `UF.PullIngredients` | `Patch_SDS_PreservedBillIngredients` / `Patch_SDS_VirtualFixedIngredients` 只挂 `WorkGiver_DoBill` | `SDS_CompatBillSupply` 走联合轻工家具拓展-UF Light Industries Furniture 自己的拉料入口 | 入口不同；该入口不经过 `WorkGiver_DoBill` |
| `GD.DeckReinforceCount` | `Patch_SDS_JobMirrorTargets` 只在 StartJob 改写镜像目标（写路径仍是统一执行器） | 只注册取值器，不直接取货 / 扣账 / 写 Job | 允许：适配器只供数值；取量、物化、扣账仍只有一条路径 |
| `Beam.SpecialContainerType` / `Beam.ScanCache` / `Beam.Transfer` | 无核心写补丁 | 只调 `SDS_API.IsDepositEndpoint` + 端点 `IHaulDestination.Accepts`，投递走原版容器契约 | 不调用存入门面，避免第二条入账路径 |
| `Milira.AutoProduction` / `Milira.AutomatedRecipeCycle` / `Milira.ManualRecompose` / `Milira.RecomposeOrder` / `Milira.DecomposeOrder` / `Milira.LifeEssenceOrder` | 核心 DoBill 桥不接米莉拉扩展-未来科技Milira Expanded - Future Technology 网络 | 各入口共享 `SDS_BillSupplyPlanner` + 自有池 / 预留 | 各入口共享同一规划器；同一 tick 不重复补同一缺口 |
| `CeleTechShuttle.BuildCargoTransferables` | 无同名核心写补丁（核心只在原版对话框清单 / 派活 / 入舱层） | 只注入展示物（只读投影）；写路径仍是统一装载 Job | 允许：读投影 + 适配器翻译；不存在第二条取货 / 扣账 |
| `EE.TransmuteAccept` | `Patch_SDS_TransporterAddVirtual` 只注入展示物（UI 只读） | `TryAcceptPrefix / TryAcceptFinalizer` 展示物换真身、失败回充 | 核心不写账；写路径只有 ProjectE 等价交换 转化 + 适配器回充 |
| `Versatran.CargoInstantLoad` | 无同名核心写补丁（车辆自定义装载 UI） | `SDS_ModCompat_Versatran` 先把镜像 / 保真行摘出原版循环，虚拟走 `TryWithdraw`、保真走 `TryWithdrawPreservedExact` | 允许：超维行只有一条 API 写路径；原版循环看不到镜像 / 保真真身；真实地图物品仍走车辆原逻辑 |
| `Versatran.CargoRefresh` | 无核心写补丁（只读投影） | `RefreshPostfix` 改 `MaxCount = available`、追加保真真身行 | 允许：只读投影；不扣账 / 不派活 |
| `Versatran.MirrorGuard` | 无核心写补丁 | `CanStorePrefix` 拒绝镜像 / 存活保真真身进入车辆容器 | 允许：只读守门；不新增写路径 |
| `MiliraGift.ReceiveGift` | 无同名核心写补丁（W54 只做镜像目标的搜索 / 可达 / 携带翻译） | 适配器在第三方 driver 的预约前置物化 1 件并改写 targetA；取货只经 `TryWithdrawFromMirror` | 允许：同一入口只有一条写路径；适配器不碰账本、不做位置判定 |

---

## 五、薄适配的已知边界（非契约）

> 本节只记录两个「不合作 Mod」薄适配为什么这样接，不定义规则。适配器的挂点与调用清单见 [SDS.ThirdModCompat/Interfaces.zh-CN.md](SDS.ThirdModCompat/Interfaces.zh-CN.md)。

### 五·一、两个薄适配（已实现）

17. 牵引光束搬运器Manipulator Beam Emitter（`natsuki.manipulatorbeam`）：独立、可关闭的薄适配器，只把「找目的地 / 投递」翻译成 Find / Deposit；不改对方工作逻辑（2026-10-26 已落地）。
    - 缺口：它的目的地只有 `SlotGroup` + 一张封闭的特殊容器类型白名单，既不遍历原版非格位容器、也不问通用 `IHaulDestination` → 本 Mod 端点对它不可见。
    - 翻译：类型闸门放行 + 容器扫描表追加端点 + 用它的 `BeamTransfer` 形状描述「地面物 → 端点」；候选集用原版 `ListerHaulables`，端点身份问 `IsDepositEndpoint`，收不收只问端点 `IHaulDestination.Accepts`（本 Mod 的存入硬门）。牵引光束已固定目的地，不再拿「找」门面的全局就近结果去比对该端点——否则附近只要有更近的芯片小人或别的端点，所有建筑端点会同时判为「没有活」（2026-11-12 实机症状：完全不识别端点）。
    - **投递零接管**：它自己的通用分支（`TryGetInnerInteractableThingOwner` + `IHaulDestination.Accepts` + `ThingOwner.TryAdd`）就是原版容器契约，端点 `InputBuffer` 直接接住 →「缓冲 → 数字化入账」。
    - 适配器只判「端点身份 + 端点是否接收」，不调用「存」门面：投递已由原版容器契约覆盖（§一.5 原版契约优先），再走一次 `TryDepositAt` 会成为第二条入账路径。
    - 关闭方式：跟随牵引光束搬运器Manipulator Beam Emitter 自己的「特殊容器」总开关（任一子开关开启即生效）；不另设 SDS 专属开关。
17·补（2026-11-11）：天工模块化穿梭机 CeleTech Shuttle Extension（`LongRange.CeleTech.ShuttleExtension`）：独立薄适配器，只在它的**装载清单构建函数**返回后注入展示物；不改它的装载 / 冷藏 / 发射逻辑。
    - 缺口：它自建装载对话框与读模型，不经过原版 `Dialog_LoadTransporters`；本 Mod 库存没有地图实物，清单里没有注入点就完全不可见。
    - 翻译：一个 Postfix（`CargoTransferFilter.BuildCargoTransferables`）→ 桥 `SDS_API.InjectTransportLoadCandidates` → 唯一注入实现 `SDS_TransportVirtual.InjectIntoTransporters`；取货 / 派活 / 入舱 / 读档仍由既有原版挂点与唯一执行器负责（W50）。
    - 已知边界：展示物是「全库存聚合堆」，对方若有按整堆质量的装载前准入（冷藏准入），清单期预检会比原版更严；适配器不做数量夹取，最终以对方「开始装载」按已选数量重算为准。
18. 若无法在不改对方逻辑的前提下接入，维持已记录的边界，不在核心加特例。

---

## 六、验收清单

- [x] 门面方法是否只调用对应唯一实现，没有第二套排序 / 筛选 / 扣账？
- [x] 返回对象是否原版可识别（`Thing` / 代理 / 只读快照）？
- [x] 同一库存是否只有一个可见表示，且与《RuleIndex.zh-CN.md》S 系同口径？
- [x] 非 Pawn 模式是否复用同一解析器，而不是第二套 resolver？
- [x] 取 / 存是否只暴露事务级接口，并写清后置条件？
- [x] 所有权 / 阵营 / 禁止是否由门面补齐？
- [x] 是否只服务玩家阵营，并拒绝跨地图存入 / 跨阵营端点？
- [x] 公共 API 异常是否统一返回 `InternalError` / 空集 / `default`，不外泄给第三方？
- [x] 未采用的旧入口是否已删除并 bump `Version`（Version 2）？
- [x] 桥面是否只承载转发、不进入五轴契约（Version 4）？
- [x] 适配器是否只做翻译，没有复制规则、没有改第三方 Job/Toil？
- [x] 是否只有一个接管路径，没有门面 + Harmony 双触发？（静态检查：Pick Up And Haul 保留 `DepositToGrid` 早退；Combat Extended / ProjectE 等价交换 / 联合轻工家具拓展-UF Light Industries Furniture 直接走门面；实机核对项见《DiagnosticsCoverage.zh-CN.md》）
- [x] 是否所有正常拒绝都不写日志，异常统一 `InternalError` / 空集 / `default` + 一次 `SDS_Diag.ErrorOnce`？
- [x] 是否提供版本 / 能力探测，并以 soft-depend 方式接入？
