using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Accounts;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 外置登录（Yggdrasil / MUA 联合认证）的协议回归。
    /// 不联网：下载端口用固定 JSON 冒充认证服务器。
    /// </summary>
    public class YggdrasilAuthenticatorTests
    {
        private const string Root = "https://skin.example.cn/api/yggdrasil";

        private sealed class FakeServer : DownloadServiceStub
        {
            private readonly string _response;

            public FakeServer(string response)
            {
                _response = response;
            }

            public string LastUrl { get; private set; } = string.Empty;

            public string LastBody { get; private set; } = string.Empty;

            public override Task<string> PostJsonAsync(string url, string jsonBody,
                IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
            {
                LastUrl = url;
                LastBody = jsonBody;
                return Task.FromResult(_response);
            }
        }

        [Fact]
        public void Rejects_plain_http_api_root()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => YggdrasilAuthenticator.NormalizeRoot("http://skin.example.cn/api/yggdrasil"));

            Assert.Contains("https", error.Message);
        }

        [Fact]
        public async Task Logs_in_and_maps_profile()
        {
            FakeServer server = new FakeServer(@"{
                ""accessToken"": ""ygg-token"",
                ""clientToken"": ""client-token"",
                ""selectedProfile"": { ""id"": ""069a79f444e94726a5befca90e38aaf5"", ""name"": ""Steve"" }
            }");

            Account account = await new YggdrasilAuthenticator(server)
                .LoginAsync(Root, "steve@example.cn", "pw", CancellationToken.None);

            Assert.Equal("Steve", account.Name);
            Assert.Equal(AccountType.External, account.Type);
            Assert.Equal("069a79f4-44e9-4726-a5be-fca90e38aaf5", account.Uuid);
            Assert.Equal("ygg-token", account.AccessToken);
            Assert.Equal("client-token", account.RefreshToken);
            Assert.Equal(Root, account.AuthServer);
            Assert.Equal("外置", account.TypeLabel);

            Assert.EndsWith("/authserver/authenticate", server.LastUrl);
            Assert.Contains("\"agent\"", server.LastBody);
            Assert.Contains("steve@example.cn", server.LastBody);
        }

        [Fact]
        public async Task Wrong_password_gives_a_chinese_message()
        {
            FakeServer server = new FakeServer(@"{
                ""error"": ""ForbiddenOperationException"",
                ""errorMessage"": ""Invalid credentials. Invalid username or password.""
            }");

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new YggdrasilAuthenticator(server).LoginAsync(Root, "steve", "bad", CancellationToken.None));

            Assert.Contains("账号或密码错误", error.Message);
        }

        [Fact]
        public async Task Account_without_profile_is_reported_clearly()
        {
            FakeServer server = new FakeServer(@"{ ""accessToken"": ""t"" }");

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new YggdrasilAuthenticator(server).LoginAsync(Root, "steve", "pw", CancellationToken.None));

            Assert.Contains("角色", error.Message);
        }

        [Theory]
        [InlineData("skin.example.cn")]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Rejects_invalid_server_address(string address)
        {
            FakeServer server = new FakeServer("{}");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new YggdrasilAuthenticator(server).LoginAsync(address, "steve", "pw", CancellationToken.None));
        }

        [Fact]
        public async Task Refresh_reuses_profile_when_response_omits_it()
        {
            FakeServer server = new FakeServer(@"{ ""accessToken"": ""new-token"" }");
            Account old = new Account("Steve", "uuid-1", AccountType.External, "old-token", "client", Root);

            Account refreshed = await new YggdrasilAuthenticator(server)
                .RefreshAsync(old, CancellationToken.None);

            Assert.Equal("new-token", refreshed.AccessToken);
            Assert.Equal("Steve", refreshed.Name);
            Assert.Equal("uuid-1", refreshed.Uuid);
            Assert.Equal("client", refreshed.RefreshToken);
            Assert.EndsWith("/authserver/refresh", server.LastUrl);
        }

        [Fact]
        public void Java_agent_argument_quotes_paths_with_spaces()
        {
            string argument = ExternalLaunch.BuildJavaAgentArgument(
                @"C:\Users\zack\AppData\Roaming\NLMCL\authlib-injector\authlib-injector-1.2.8.jar",
                Root + "/");

            Assert.Equal(
                "-javaagent:\"C:\\Users\\zack\\AppData\\Roaming\\NLMCL\\authlib-injector\\authlib-injector-1.2.8.jar\"="
                + Root,
                argument);
        }

        [Fact]
        public async Task Offline_accounts_never_get_an_agent_argument()
        {
            Account offline = new Account("Alex", "uuid", AccountType.Offline);

            Assert.Null(await ExternalLaunch.BuildJvmArgumentsAsync(offline, null, CancellationToken.None));
        }
    }
}
