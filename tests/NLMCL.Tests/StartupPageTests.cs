using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class StartupPageTests
    {
        [Fact]
        public async Task Starts_on_configured_page()
        {
            TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings
            {
                Current = new AppSettings { DefaultPage = "download" }
            };

            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"),
                appSettings: settings);

            await vm.InitializeAsync();

            Assert.True(vm.IsDownloadSection);
            Assert.False(vm.IsVersionsSection);
        }

        [Fact]
        public async Task Last_page_is_restored_when_configured()
        {
            TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings
            {
                Current = new AppSettings
                {
                    DefaultPage = "last",
                    LastPage = "multiplayer"
                }
            };

            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"),
                appSettings: settings);

            await vm.InitializeAsync();

            Assert.True(vm.IsMultiplayerSection);
            Assert.False(vm.IsVersionsSection);
        }
    }
}
