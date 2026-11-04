using System;
using System.IO;
using System.Linq;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>启动"卡住"提示：60 秒无输出时应该追加一条警告并改状态。</summary>
    public class WatchdogTests
    {
        [Fact]
        public void Sixty_seconds_of_silence_produces_a_hint()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-wd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var vm = TestSupport.CreateConsole(new AppPaths(root));
                vm.IsRunning = true;

                vm.BackdateSilence(TimeSpan.FromSeconds(61));
                vm.CheckSilence();

                Assert.Contains(vm.Lines, line => line.Text.Contains("没有任何输出"));
                Assert.Contains("可能卡在启动阶段", vm.StatusText);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public void Recent_output_suppresses_the_hint()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-wd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var vm = TestSupport.CreateConsole(new AppPaths(root));
                vm.IsRunning = true;

                vm.BackdateSilence(TimeSpan.FromSeconds(10));
                vm.CheckSilence();

                Assert.DoesNotContain(vm.Lines, line => line.Text.Contains("没有任何输出"));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
