using System;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// XAML 冒烟测试：在 STA 线程里真正构造一次主窗口。
    /// 任何 XAML 语法错误、StaticResource 缺失、绑定目标不存在都会在这里炸出来，
    /// 因此不需要人肉开界面也能进 CI。
    /// </summary>
    public class MainWindowSmokeTests
    {
        private sealed class EmptyCatalog : IVersionCatalog
        {
            public Task<System.Collections.Generic.IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory,
                CancellationToken cancellationToken)
            {
                return Task.FromResult<System.Collections.Generic.IReadOnlyList<MinecraftVersion>>(
                    new MinecraftVersion[0]);
            }
        }

        [Fact]
        public void MainWindow_parses_xaml_and_binds_view_model()
        {
            Exception? failure = null;

            Thread thread = new Thread(() =>
            {
                try
                {
                    MainViewModel vm = TestSupport.CreateMain(new EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"));
                    NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm);
                    Assert.Same(vm, window.DataContext);
                    Assert.NotNull(window.Content);
                    window.Close();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA 线程超时未结束。");

            Assert.Null(failure);
        }
    }
}
