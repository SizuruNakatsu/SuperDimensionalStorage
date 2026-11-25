using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 测试宿主不是游戏进程：RimWorld / Unity 程序集不在输出目录，
    /// 这里用程序集元数据里记录的 GameDir 做一次按名解析，避免复制整包运行时。
    /// </summary>
    internal static class TestAssemblyHooks
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            string gameDir = null;
            foreach (AssemblyMetadataAttribute attribute
                in typeof(TestAssemblyHooks).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (attribute.Key == "SdsGameDir")
                {
                    gameDir = attribute.Value;
                    break;
                }
            }
            if (!string.IsNullOrEmpty(gameDir) && Directory.Exists(gameDir))
            {
                AssemblyLoadContext.Default.Resolving += (context, name) =>
                {
                    string path = Path.Combine(gameDir, name.Name + ".dll");
                    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
                };
            }

            // 让 Verse.GenFilePaths / ModsConfig 在无 Unity 宿主时仍能初始化：
            // 直接填 saveDataPath，绕过 UnityData / Application。
            try
            {
                string testData = Path.Combine(Path.GetTempPath(), "sds-test-data");
                Directory.CreateDirectory(testData);
                FieldInfo saveDataPath = typeof(Verse.GenFilePaths).GetField("saveDataPath",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (saveDataPath != null)
                {
                    saveDataPath.SetValue(null, testData);
                }
            }
            catch (Exception)
            {
                // 失败时依赖 ModsConfig 的判定会抛异常，由测试自身暴露。
            }
        }
    }
}
