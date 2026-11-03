using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Networking;
using Xunit;

namespace NLMCL.Tests
{
    public class LanAddressTests
    {
        [Theory]
        [InlineData("10.21.3.9:54321", "10.21.3.9", 54321)]
        [InlineData("192.168.1.7:25565", "192.168.1.7", 25565)]
        [InlineData("tcp://10.0.0.2:1234", "10.0.0.2", 1234)]
        [InlineData("  host.local:25565  ", "host.local", 25565)]
        public void Parses_valid_addresses(string input, string expectedHost, int expectedPort)
        {
            Assert.True(LanAddress.TryParse(input, out string host, out int port, out string? error));
            Assert.Null(error);
            Assert.Equal(expectedHost, host);
            Assert.Equal(expectedPort, port);

        }
        [Theory]
        [InlineData("10.21.38.133.56643", "10.21.38.133", 56643)]
        [InlineData("192.168.1.7.25565", "192.168.1.7", 25565)]
        public void Accepts_dot_separated_port(string input, string expectedHost, int expectedPort)
        {
            Assert.True(LanAddress.TryParse(input, out string host, out int port, out _));
            Assert.Equal(expectedHost, host);
            Assert.Equal(expectedPort, port);
        }

        [Theory]
        [InlineData("", "请输入")]
        [InlineData("10.0.0.1", "缺少端口")]
        [InlineData("10.0.0.1:abc", "不是有效数字")]
        [InlineData("10.0.0.1:0", "1-65535")]
        [InlineData("10.0.0.1:70000", "1-65535")]
        [InlineData("http://10.0.0.1:25565", "格式应为")]
        [InlineData("../evil:25565", "不能包含")]
        public void Rejects_bad_addresses_with_actionable_messages(string input, string expectedFragment)
        {
            Assert.False(LanAddress.TryParse(input, out _, out _, out string? error));
            Assert.NotNull(error);
            Assert.Contains(expectedFragment, error!);
        }

        [Fact]
        public void Format_defaults_to_loopback()
        {
            Assert.Equal("127.0.0.1:25565", LanAddress.Format(null, 25565));
            Assert.Equal("10.0.0.2:1234", LanAddress.Format("10.0.0.2", 1234));
        }
    }

    public class LanFlowTests
    {
        private sealed class OneVersion : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<MinecraftVersion>>(
                    new[] { new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null) });
            }
        }

        private sealed class RecordingLauncher : IGameLauncher
        {
            public LaunchRequest? LastRequest { get; private set; }

            public int Calls { get; private set; }

            public Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                LastRequest = request;
                return Task.FromResult(new LaunchResult(1, true, "ok"));
            }
        }

        private sealed class AnyJava : IJavaLocator
        {
            public Task<IReadOnlyList<JavaRuntime>> FindInstalledAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<JavaRuntime>>(
                    new[] { TestSupport.CreateJava(21) });
            }
        }

        private static MainViewModel Create(
            RecordingLauncher launcher, TestSupport.FakeNetwork network, TestSupport.FakeClipboard clipboard)
        {
            AppPaths paths = new AppPaths(@"C:\Temp\roaming");
            return new MainViewModel(
                new OneVersion(), paths,
                TestSupport.CreateDownload(paths),
                TestSupport.CreateConsole(paths),
                launcher, new AnyJava(),
                new TestSupport.MemoryAccountStore(),
                new TestSupport.NoopAuthenticator(),
                new TestSupport.MemoryInstanceSettingsStore(),
                network, clipboard, new TestSupport.FakeFolderPicker(), new TestSupport.FakeAppSettings());
        }

        [Fact]
        public async Task Connect_probes_port_then_launches_with_server_address()
        {
            RecordingLauncher launcher = new RecordingLauncher();
            TestSupport.FakeNetwork network = new TestSupport.FakeNetwork { PortOpen = true };
            MainViewModel vm = Create(launcher, network, new TestSupport.FakeClipboard());

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            vm.ServerAddress = "10.21.3.9:54321";

            await vm.ConnectToLanAsync();

            Assert.Equal(1, launcher.Calls);
            Assert.Equal("10.21.3.9:54321", launcher.LastRequest!.ServerAddress);
        }

        [Fact]
        public async Task Closed_port_never_launches()
        {
            RecordingLauncher launcher = new RecordingLauncher();
            TestSupport.FakeNetwork network = new TestSupport.FakeNetwork { PortOpen = false };
            MainViewModel vm = Create(launcher, network, new TestSupport.FakeClipboard());

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            vm.ServerAddress = "10.21.3.9:54321";

            await vm.ConnectToLanAsync();

            // 探测失败就不该启动游戏——否则用户只能对着"连接中"干等
            Assert.Equal(0, launcher.Calls);
            Assert.Contains("连不上", vm.StatusText);
        }

        [Fact]
        public async Task Bad_address_reports_before_any_network_call()
        {
            RecordingLauncher launcher = new RecordingLauncher();
            MainViewModel vm = Create(launcher, new TestSupport.FakeNetwork(), new TestSupport.FakeClipboard());

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            vm.ServerAddress = "10.21.3.9";

            await vm.ConnectToLanAsync();

            Assert.Equal(0, launcher.Calls);
            Assert.Contains("端口", vm.StatusText);
        }

        [Fact]
        public void My_address_combines_local_ip_and_port_and_copies()
        {
            TestSupport.FakeClipboard clipboard = new TestSupport.FakeClipboard();
            MainViewModel vm = Create(new RecordingLauncher(), new TestSupport.FakeNetwork(), clipboard);

            vm.MyPort = "54321";
            vm.RefreshMyAddress();

            Assert.Equal("192.168.1.7:54321", vm.MyAddress);

            vm.CopyMyAddress();
            Assert.Equal("192.168.1.7:54321", clipboard.Text);
        }

        [Fact]
        public void Missing_local_ip_is_reported_not_silently_empty()
        {
            TestSupport.FakeNetwork network = new TestSupport.FakeNetwork { Address = string.Empty };
            MainViewModel vm = Create(new RecordingLauncher(), network, new TestSupport.FakeClipboard());

            vm.MyPort = "54321";
            vm.RefreshMyAddress();

            Assert.Contains("未找到", vm.MyAddress);
        }

        [Fact]
        public void Invalid_port_shows_a_placeholder_instead_of_a_broken_address()
        {
            MainViewModel vm = Create(new RecordingLauncher(), new TestSupport.FakeNetwork(),
                new TestSupport.FakeClipboard());

            vm.MyPort = "abc";
            vm.RefreshMyAddress();

            Assert.Equal("192.168.1.7:端口", vm.MyAddress);
            Assert.Contains("端口", vm.StatusText);
        }
    }
}
