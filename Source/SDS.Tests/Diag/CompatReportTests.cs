using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace SuperDimensionalStorage
{
    public class CompatReportTests
    {
        [Fact]
        public void ReportDtos_OnlyPrimitiveAndStringListMembers()
        {
            Type[] allowed = { typeof(string), typeof(int), typeof(bool), typeof(List<string>) };
            Type[] dtos = { typeof(SDS_CompatPatchReport), typeof(SDS_CompatAdapterReport) };
            foreach (Type dto in dtos)
            {
                foreach (FieldInfo field in dto.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    Assert.Contains(field.FieldType, allowed);
                }
                foreach (PropertyInfo property in dto.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    Assert.Contains(property.PropertyType, allowed);
                }
            }
        }

        [Fact]
        public void DiagAssembly_DoesNotReferenceHarmony()
        {
            AssemblyName[] refs = typeof(SDS_CompatReportHub).Assembly.GetReferencedAssemblies();
            Assert.DoesNotContain(refs, r => r.Name.IndexOf("Harmony", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Hub_SnapshotIsDeepCopy()
        {
            SDS_CompatReportHub.Reset();
            SDS_CompatPatchReport report = new SDS_CompatPatchReport("Test");
            report.Applied("A.B");
            report.Missing("C.D", "缺失");
            SDS_CompatReportHub.Register("Test", "pkg", true, report, null, null);

            SDS_CompatAdapterReport[] snapshot = SDS_CompatReportHub.Snapshot();
            Assert.Single(snapshot);
            Assert.Equal(1, snapshot[0].appliedCount);
            Assert.Equal(2, snapshot[0].declaredCount);
            Assert.True(snapshot[0].hasFailures);

            snapshot[0].failed.Add("X");
            SDS_CompatAdapterReport[] again = SDS_CompatReportHub.Snapshot();
            Assert.Single(again[0].failed);
            Assert.NotSame(snapshot, again);
        }
    }
}
