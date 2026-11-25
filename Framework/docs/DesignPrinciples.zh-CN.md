# 超维存储 · 设计原则

## 一、总则（原文保留）

准则一：建筑、人、机械体共用同一种端口。

准则二：该端口等效视为「有超大容量的仓库 / 容器」——也就是「地图上真的放了这么多东西」。

推论一：端口参与原版判定，本身不特殊。

推论二：不改原版表现出的工作逻辑。

推论三：三者的端口在实现上共用同一个句柄。



## 二、文档分工（2026-10-21 定稿，2026-10-24 收敛）

准则三：本mod只追求流程唯一、逻辑唯一，而不是具体函数唯一。逻辑唯一 = 规则定义一次 + 所有入口调用它。

**准则三同样适用于文档：同一规则只允许在一个文档定义，其余文档只引用，不得复述第二份规则。**

**本文只保留总则、文档分工与变更分级；具体规则、字段、流程一律以《ModuleRules.zh-CN.md》十二「文档地图」指向的文档为唯一权威。**

**跨文档口径冲突时，以《RuleIndex.zh-CN.md》的「权威文档」列为唯一仲裁源；规则归属以《ModuleRules.zh-CN.md》十二「文档地图」为准。** 历史五轴能力文档已按模块拆分并入 `SDS.*/Rules.zh-CN.md`（判据）与 `SDS.*/Interfaces.zh-CN.md`（接触面），不再单独维护；启动 / 生命周期相关的有效内容并入《ModuleRules.zh-CN.md》九、《DataFlow.zh-CN.md》与 `SDS.Bootstrap/Interfaces.zh-CN.md`。



## 三、变更分级与影响面（2026-10-27 定）

> 本节只定义「什么变更必须回到真人」，不定义任何玩法规则。规则 ID（W/R/S/E）或规范句任一变化都算 L2 变更。

**E4 变更分级（按客观触发条件，不按主观判断）**

| 级别 | 触发条件（命中任一即是） | 执行方式 |
|---|---|---|
| **L1** | 纯实现：重命名、拆文件、等价重构、注释措辞、补机器校验、可见性收缩或物理搬移；判据：**公开面清单 diff 为空**（`Source/Tools/DumpPublicSurface.ps1 -Check` 退出码 0）且日志与断言未变；不触及规则 ID / 规范句 / 存档字段 / 补丁挂点集合 | AI 自主执行，跑一次 `Source/Tools/CheckRuleIds.ps1` + `CheckPatchInventory.ps1` + `DumpPublicSurface.ps1 -Check` |
| **L2** | 新增或修改规则 ID / 规范句；**模块间公共 API 变更**（`*_API.cs` 成员增删或改签名、公开白名单变更，即清单 diff 非空）；新增运行期静态缓存；新增或删除补丁挂点；改变事务状态机或数量口径 | AI 执行，**先出影响面清单**，真人审阅 |
| **L3** | 修改本文三条准则 / 推论 / 文档分工 / 本节分级表；删除规则 ID；修改存档结构（`ExposeData` / `Scribe`）；修改对外 `SDS_API` 五轴契约或 `Version` | 真人决策，AI 只提供影响面与证据 |

**E10 判据工件（2026-11-12 增补）**

模块间公共 API 的增删与签名变化一律按 L2 处理；「对外」只指 `SDS_API` 五轴与 `Version`，那一层是 L3。判级只认工件，不认主观描述：`DumpPublicSurface.ps1 -Check` 的 diff 为空即 L1，非空即 L2。清单的覆盖范围以实现侧登记表为准（当前 `SDS.Core` / `SDS.Diag` / `SDS.Api`）；未登记模块用公开白名单（`CheckAssemblyBoundaries.ps1` §4）的变更等效判定。

**E3 影响面清单（L2 / L3 必填，先声明后动代码）**

1. 改动的规则 ID：新增 / 修改 / 废弃，以及**规范句逐字 diff**；
2. 需同步的文档段落（AI 自己列，真人抽查）；
3. 需更新的断言 / 自检；
4. 需登记 `SDS_StaticState.ResetAll` 的静态缓存；
5. 补丁挂点增删（同步 A/B 报告与登记表，见《ModuleRules.zh-CN.md》七）；
6. **验证证据**：编译结果 + 自检结果。

> 缺第 6 项的清单只是声明，不算完成；缺第 1 项的变更一律按 L2 处理（默认从严）。

**E11 构建范围与取证（2026-11-13 定）**

默认只构建改动模块的 csproj（`dotnet build Source/SDS.<模块>/SDS.<模块>.csproj -c Debug`）；引用态已确认未变时可加 `--no-dependencies`。只有改动 `SDS.Contracts`、公开面、`ProjectReference` 图、`Directory.Build.props`、SDK 或 NuGet 引用态时才构建整解。

每次构建必须经 `Source/Tools/BuildModule.ps1`：构建前后比对 `超维存储/Assemblies/*.dll` 的 SHA256；除目标模块与显式声明的直接依赖（`-AllowChanged`）外出现任何 DLL 变化即失败。首次 restore 使全部项目 `CoreCompileInputs.cache` 失效时，必须显式加 `-AcknowledgeFullRebuild`，并在汇报中写明「引用态变化导致一次性全量重编」，不得把该现象归因于本次改动范围。无构建前基线（首次构建）只记录本次变化，需再跑一次取得范围证据。
