using System;
using System.Collections.Generic;
using System.Reflection;

namespace SuperDimensionalStorage
{
    /// <summary>一次适配器扫描的计数结果（只用于启动日志，不参与行为）。</summary>
    internal struct SDS_CompatScanResult
    {
        public int assemblies;
        public int found;
        public int applied;
        public int notLoaded;
        public int duplicates;
        public int failed;
    }

    /// <summary>
    /// 适配器扫描（§10.2 第 4 步 / §9.1）：
    /// 反射枚举已加载程序集里的 ISDSCompatAdapter 实现；目标未装不调用 Apply。
    /// 2026-11-12 收编为单程序集 SDS.ThirdModCompat：同一程序集允许多个实现，
    /// 适配器身份的唯一防线是名称（Harmony ID）——名称重复记 Error 并跳过后注册者。
    /// 幂等：已处理过的适配器不会重复 Apply；可在 ExecuteWhenFinished 复扫一次。
    /// 不引用第三方适配器程序集；异常只走 ErrorOnce / 报告中心（Compat 层不调用 Fail）。
    /// </summary>
    internal static class SDS_CompatLoader
    {
        private static readonly Dictionary<string, Type> ownerByName =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<string> attempted = new HashSet<string>(StringComparer.Ordinal);
        private static int appliedCount;

        /// <summary>本进程已成功 Apply 的适配器数。</summary>
        internal static int AppliedCount
        {
            get { return appliedCount; }
        }

        /// <summary>本进程已识别（含目标未装）的适配器数；复扫后会累积。</summary>
        internal static int KnownCount
        {
            get { return ownerByName.Count; }
        }

        internal static SDS_CompatScanResult Scan()
        {
            SDS_CompatScanResult result = new SDS_CompatScanResult();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            result.assemblies = assemblies.Length;

            for (int a = 0; a < assemblies.Length; a++)
            {
                Assembly assembly = assemblies[a];
                if (assembly == null || assembly.IsDynamic)
                {
                    continue;
                }
                Type[] types = GetLoadableTypes(assembly);
                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (type == null || type.IsAbstract || type.IsInterface
                        || !typeof(ISDSCompatAdapter).IsAssignableFrom(type))
                    {
                        continue;
                    }
                    Process(type, ref result);
                }
            }
            return result;
        }

        private static void Process(Type type, ref SDS_CompatScanResult result)
        {
            ISDSCompatAdapter adapter;
            try
            {
                adapter = (ISDSCompatAdapter)Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                result.failed++;
                SDS_Diag.ErrorOnce("适配器实例化失败（已跳过）：" + type.FullName, e);
                return;
            }
            if (adapter == null)
            {
                return;
            }

            string name;
            string packageId = null;
            try
            {
                name = adapter.Name;
                packageId = adapter.TargetPackageId;
            }
            catch (Exception e)
            {
                result.failed++;
                SDS_Diag.ErrorOnce("适配器身份读取失败（已跳过）：" + type.FullName, e);
                return;
            }
            if (string.IsNullOrEmpty(name))
            {
                name = type.FullName;
            }

            Type owner;
            if (ownerByName.TryGetValue(name, out owner))
            {
                if (owner != type)
                {
                    result.duplicates++;
                    SDS_Diag.ErrorOnce("适配器名称（Harmony ID）重复，后注册者已跳过：" + name, null);
                    return;
                }
            }
            else
            {
                ownerByName[name] = type;
                result.found++;
            }

            bool targetLoaded;
            try
            {
                targetLoaded = adapter.IsTargetLoaded;
            }
            catch (Exception e)
            {
                result.failed++;
                attempted.Add(name);
                SDS_Diag.ErrorOnce("适配器目标检测失败（已跳过）：" + name, e);
                RegisterBrokenReport(name, packageId, "目标检测异常 " + e.GetType().Name);
                return;
            }
            if (!targetLoaded)
            {
                result.notLoaded++;
                return;
            }
            if (attempted.Contains(name))
            {
                return;
            }
            // 先登记再执行：Apply 抛异常也不在复扫里重试，避免第二条写路径。
            attempted.Add(name);
            try
            {
                adapter.Apply();
                appliedCount++;
                result.applied++;
            }
            catch (Exception e)
            {
                result.failed++;
                SDS_Diag.ErrorOnce("第三方适配器执行失败（已跳过）：" + name, e);
                RegisterBrokenReport(name, packageId, "适配器异常 " + e.GetType().Name);
            }
        }

        /// <summary>适配器自己没来得及登记报告时补一条，保证启动报告能看到异常。</summary>
        private static void RegisterBrokenReport(string name, string packageId, string reason)
        {
            if (SDS_CompatReportHub.Has(name))
            {
                return;
            }
            SDS_CompatReportHub.Register(name, packageId, true, null, null, reason);
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types ?? Type.EmptyTypes;
            }
            catch (Exception)
            {
                return Type.EmptyTypes;
            }
        }
    }
}
