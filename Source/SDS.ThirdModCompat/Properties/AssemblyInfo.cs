using System.Runtime.CompilerServices;

// 友元登记（唯一一项，见《模块规则.md》五白名单）：
// SDS.Tests 是唯一允许引用全部模块的测试程序集；本程序集公开面已收缩为 0，
// 测试经 InternalsVisibleTo 直接引用 internal 的适配器与 Sdk 工具类。
[assembly: InternalsVisibleTo("SDS.Tests")]
