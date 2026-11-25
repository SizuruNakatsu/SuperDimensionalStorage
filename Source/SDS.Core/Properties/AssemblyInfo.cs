using System.Runtime.CompilerServices;

// 规则模块读取账本写接口与派生索引（P3 起：流程层迁入 Rules，P4 迁入判定层）。
// 登记见 CheckAssemblyBoundaries.ps1 §5；Vanilla / Api 不获得此权限。
[assembly: InternalsVisibleTo("SDS.Rules")]
