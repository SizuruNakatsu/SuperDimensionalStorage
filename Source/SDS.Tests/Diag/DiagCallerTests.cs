using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace SuperDimensionalStorage
{
    public class DiagCallerTests
    {
        private static string SourceRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "SDS.Contracts")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "Tools")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }

        [Fact]
        public void Fail_OnlyCalledFromImplementationModules()
        {
            string root = SourceRoot();
            Assert.NotNull(root);
            List<string> offenders = new List<string>();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.IndexOf("\\obj\\", StringComparison.Ordinal) >= 0) { continue; }
                if (file.IndexOf("\\bin\\", StringComparison.Ordinal) >= 0) { continue; }
                if (!File.ReadAllText(file).Contains("SDS_Diag.Fail")) { continue; }
                string rel = file.Substring(root.Length).TrimStart('\\', '/');
                string module = rel.Split('\\')[0];
                if (module == "SDS.Tests") { continue; }
                if (module != "SDS.Core" && module != "SDS.Rules" && module != "SDS.Vanilla")
                {
                    offenders.Add(rel);
                }
            }
            Assert.Empty(offenders);
        }

        [Fact]
        public void ErrorOnce_KeyUsesRealCallSite()
        {
            Type impl = typeof(SDS_Diag).Assembly.GetType("SuperDimensionalStorage.SDS_DiagImpl");
            Assert.NotNull(impl);
            FieldInfo seenField = impl.GetField("seen", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(seenField);
            IDictionary seen = (IDictionary)seenField.GetValue(null);
            Assert.NotNull(seen);
            seen.Clear();
            try
            {
                TriggerCallerProbe();
                string key = null;
                foreach (DictionaryEntry entry in seen)
                {
                    string candidate = entry.Key as string;
                    if (candidate != null && candidate.Contains("caller-probe"))
                    {
                        key = candidate;
                        break;
                    }
                }
                Assert.NotNull(key);
                Assert.Contains(nameof(DiagCallerTests) + "." + nameof(TriggerCallerProbe), key);
                Assert.DoesNotContain("SDS_Diag" + ".ErrorOnce", key);
            }
            finally
            {
                seen.Clear();
            }
        }

        private static void TriggerCallerProbe()
        {
            SDS_Diag.ErrorOnce("caller-probe", null);
        }

        [Fact]
        public void Diag_HasNoMessageFacade()
        {
            string root = SourceRoot();
            Assert.NotNull(root);
            string diagFile = Path.Combine(root, "SDS.Diag", "SDS_Diag.cs");
            Assert.True(File.Exists(diagFile));
            Assert.DoesNotContain("public static void Message", File.ReadAllText(diagFile));
        }
    }
}
