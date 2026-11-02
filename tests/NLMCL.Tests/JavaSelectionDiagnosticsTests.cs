using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Pcl.Vb;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 诊断：列出 PCL 探测到的 Java 与被版本要求的 Java，写进 %TEMP%\nlmcl-javas.txt。
    /// 用来回答"为什么启动器选了 Java 25"。
    /// </summary>
    public class JavaSelectionDiagnosticsTests
    {
        [Fact]
        public async Task Dumps_detected_javas()
        {
            string? minecraft = Environment.GetEnvironmentVariable("NLMCL_TEST_MC_DIR");
            string? version = Environment.GetEnvironmentVariable("NLMCL_TEST_VERSION");

            PclJavaLocator locator = new PclJavaLocator();
            var javas = await locator.FindInstalledAsync(CancellationToken.None);

            StringBuilder report = new StringBuilder();
            report.AppendLine("探测到的 Java（共 " + javas.Count + " 个）：");
            foreach (JavaRuntime java in javas)
            {
                report.AppendLine("  Java " + java.Major + "  存在=" + java.Exists + "  " + java.Home);
            }

            if (!string.IsNullOrWhiteSpace(minecraft) && !string.IsNullOrWhiteSpace(version))
            {
                int? required = NLMCL.Core.Installing.JavaRequirements.ReadRequiredMajor(minecraft, version);
                JavaRuntime? picked = NLMCL.Core.Installing.JavaRequirements.Pick(javas, required);
                report.AppendLine();
                report.AppendLine("版本 " + version + " 要求 Java: " + (required.HasValue ? required.Value.ToString() : "(未声明)"));
                report.AppendLine("JavaRequirements.Pick 选中: "
                    + (picked == null ? "(无)" : "Java " + picked.Major + "  " + picked.Home));
            }

            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "nlmcl-javas.txt"), report.ToString(), Encoding.UTF8);

            Assert.True(File.Exists(Path.Combine(Path.GetTempPath(), "nlmcl-javas.txt")));
        }
    }
}
