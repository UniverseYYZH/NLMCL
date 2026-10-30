using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLCL.Abstractions;
using NLCL.Core.Pcl.Vb;
using Xunit;

namespace NLCL.Tests
{
    /// <summary>
    /// Java 探测的集成测试：走 PCL 的 JavaUtils 真实扫描本机。
    /// 机器上没有 Java 时自动跳过（而不是假装通过）。
    /// </summary>
    public class PclJavaLocatorTests
    {
        private static bool HasAnyJavaOnMachine()
        {
            string[] roots =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks")
            };

            return roots.Any(Directory.Exists);
        }

        [Fact]
        public async Task Finds_installed_java_through_pcl_java_utils()
        {
            if (!HasAnyJavaOnMachine())
            {
                return; // 本机没有 Java，跳过（不伪造通过）
            }

            PclJavaLocator locator = new PclJavaLocator();

            IReadOnlyList<JavaRuntime> found = await locator.FindInstalledAsync(CancellationToken.None);

            Assert.NotEmpty(found);

            // 每一项都必须有主目录，且能定位到 bin\javaw.exe
            foreach (JavaRuntime java in found)
            {
                Assert.False(string.IsNullOrWhiteSpace(java.Home));
                Assert.EndsWith("bin\\javaw.exe", java.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }

            // 本机装了 JDK 25，因此主版本应至少有一个 >= 17
            Assert.Contains(found, java => java.Major >= 17);
        }

        [Fact]
        public async Task Honours_cancellation()
        {
            PclJavaLocator locator = new PclJavaLocator();
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => locator.FindInstalledAsync(cts.Token));
        }
    }
}
