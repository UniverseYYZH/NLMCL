using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Invites;
using Xunit;

namespace NLMCL.Tests;

public sealed class InviteLinkTests
{
    [Fact]
    public void Parses_a_minimal_join_link()
    {
        InviteLinkParseResult result = InviteLinkParser.Parse("nlmcl://join?host=example.com");

        Assert.True(result.Success);
        Assert.Equal("example.com", result.Link!.Host);
        Assert.Equal(25565, result.Link.Port);
        Assert.Equal("example.com", result.Link.Address);
        Assert.False(result.Link.RequiresAutoModpack);
    }

    [Fact]
    public void Parses_name_port_and_automodpack_flag()
    {
        InviteLinkParseResult result = InviteLinkParser.Parse(
            "nlmcl://join?host=mc.example.com&port=25566&name=%E6%96%B9%E5%9D%97%E5%AE%81%E7%90%86&modded=1");

        Assert.True(result.Success);
        Assert.Equal("mc.example.com:25566", result.Link!.Address);
        Assert.Equal("方块宁理", result.Link.DisplayName);
        Assert.True(result.Link.RequiresAutoModpack);
    }

    [Theory]
    [InlineData("https://join?host=example.com")]
    [InlineData("nlmcl://other?host=example.com")]
    [InlineData("nlmcl://join?port=25565")]
    [InlineData("nlmcl://join?host=example.com&port=0")]
    [InlineData("nlmcl://join?host=example.com&port=70000")]
    [InlineData("nlmcl://join?host=bad..host")]
    [InlineData("nlmcl://join?host=example.com/path")]
    [InlineData("nlmcl://user@join?host=example.com")]
    public void Rejects_invalid_links(string value)
    {
        Assert.False(InviteLinkParser.Parse(value).Success);
    }

    [Fact]
    public void Rejects_overlong_values()
    {
        string host = new string('a', 300);
        Assert.False(InviteLinkParser.Parse("nlmcl://join?host=" + host).Success);
        Assert.False(InviteLinkParser.Parse("nlmcl://join?host=example.com&name=" + new string('x', 80)).Success);
    }

    [Fact]
    public void Invite_prompt_does_not_launch_before_confirmation()
    {
        string root = Path.Combine(Path.GetTempPath(), "nlmcl-invite-" + Guid.NewGuid().ToString("N"));
        try
        {
            var launcher = new RecordingLauncher();
            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), new AppPaths(root), launcher: launcher);

            vm.ShowInvite(new InviteLink("example.com", 25565, "朋友的服务器", true));

            Assert.True(vm.IsInvitePromptOpen);
            Assert.Equal("朋友的服务器", vm.InviteServerName);
            Assert.Equal("example.com", vm.InviteServerAddress);
            Assert.True(vm.InviteRequiresAutoModpack);
            Assert.Equal(0, launcher.Calls);

            vm.CancelInvite();
            Assert.False(vm.IsInvitePromptOpen);
            Assert.Equal(0, launcher.Calls);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Pipe_relays_one_message_to_the_running_instance()
    {
        string? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task server = Task.Run(() => InvitePipe.ListenAsync(
            message =>
            {
                received = message;
                return Task.CompletedTask;
            },
            cts.Token));

        // ListenAsync 没有单独的“已监听”事件；给服务端一点时间建立管道后再发。
        await Task.Delay(250);
        bool sent = await InvitePipe.SendAsync("nlmcl://join?host=example.com", cts.Token);

        Assert.True(sent);
        for (int i = 0; i < 20 && received == null; i++)
        {
            await Task.Delay(50);
        }

        cts.Cancel();
        Assert.Equal("nlmcl://join?host=example.com", received);
        await Task.WhenAny(server, Task.Delay(1000));
    }

    private sealed class RecordingLauncher : IGameLauncher
    {
        public int Calls { get; private set; }

        public Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new LaunchResult(0, true, "not started in this test"));
        }
    }
}
