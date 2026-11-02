using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Accounts;
using Xunit;

namespace NLMCL.Tests
{
    public class MicrosoftAuthenticatorTests
    {
        private const string DeviceCodeJson = @"{
            ""device_code"": ""dev-code"",
            ""user_code"": ""ABCD-1234"",
            ""verification_uri"": ""https://microsoft.com/link"",
            ""interval"": 1,
            ""expires_in"": 900
        }";

        private const string TokenJson = @"{ ""access_token"": ""oauth-token"", ""refresh_token"": ""refresh"" }";

        private const string XboxJson = @"{
            ""Token"": ""xbl-token"",
            ""DisplayClaims"": { ""xui"": [ { ""uhs"": ""user-hash"" } ] }
        }";

        private const string XstsJson = @"{ ""Token"": ""xsts-token"" }";

        private const string MinecraftLoginJson = @"{ ""access_token"": ""mc-token"" }";

        private const string ProfileJson = @"{ ""id"": ""069a79f444e94726a5befca90e38aaf5"", ""name"": ""Steve"" }";

        private class RoutedDownload : DownloadServiceStub
        {
            private int _tokenCalls;

            public RoutedDownload(int pendingTokenPolls = 0)
            {
                PendingTokenPolls = pendingTokenPolls;
            }

            public int PendingTokenPolls { get; }

            public int TokenCalls => _tokenCalls;

            public int XstsCalls { get; private set; }

            public string XstsBody { get; private set; } = string.Empty;

            public string ProfileAuthHeader { get; private set; } = string.Empty;

            public override Task<string> PostFormAsync(string url,
                IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
            {
                if (url.IndexOf("devicecode", StringComparison.Ordinal) >= 0)
                {
                    return Task.FromResult(DeviceCodeJson);
                }

                if (url.IndexOf("/token", StringComparison.Ordinal) >= 0)
                {
                    _tokenCalls++;
                    if (_tokenCalls <= PendingTokenPolls)
                    {
                        return Task.FromResult(@"{ ""error"": ""authorization_pending"" }");
                    }

                    return Task.FromResult(TokenJson);
                }

                throw new NotSupportedException(url);
            }

            public override Task<string> PostJsonAsync(string url, string jsonBody,
                IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
            {
                if (url.IndexOf("user.auth.xboxlive.com", StringComparison.Ordinal) >= 0)
                {
                    return Task.FromResult(XboxJson);
                }

                if (url.IndexOf("xsts.auth.xboxlive.com", StringComparison.Ordinal) >= 0)
                {
                    XstsCalls++;
                    XstsBody = jsonBody;
                    return Task.FromResult(XstsJson);
                }

                if (url.IndexOf("login_with_xbox", StringComparison.Ordinal) >= 0)
                {
                    return Task.FromResult(MinecraftLoginJson);
                }

                throw new NotSupportedException(url);
            }

            public override Task<string> GetStringAsync(string url,
                IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
            {
                if (url.IndexOf("minecraft/profile", StringComparison.Ordinal) >= 0)
                {
                    ProfileAuthHeader = headers != null && headers.TryGetValue("Authorization", out string? value)
                        ? value
                        : string.Empty;
                    return Task.FromResult(ProfileJson);
                }

                throw new NotSupportedException(url);
            }
        }

        private static MicrosoftAuthenticator Create(RoutedDownload download)
        {
            return new MicrosoftAuthenticator(download, "test-client-id");
        }

        [Fact]
        public async Task Full_flow_returns_a_microsoft_account()
        {
            RoutedDownload download = new RoutedDownload();
            MicrosoftAuthenticator authenticator = Create(download);

            DeviceCodeChallenge challenge = await authenticator.StartAsync(CancellationToken.None);

            Assert.Equal("ABCD-1234", challenge.UserCode);
            Assert.Equal("https://microsoft.com/link", challenge.VerificationUri);
            Assert.True(challenge.ExpiresAt > DateTimeOffset.UtcNow);

            Account account = await authenticator.WaitForAccountAsync(challenge, CancellationToken.None);

            Assert.Equal("Steve", account.Name);
            Assert.Equal("069a79f4-44e9-4726-a5be-fca90e38aaf5", account.Uuid);
            Assert.Equal(AccountType.Microsoft, account.Type);
            Assert.Equal("mc-token", account.AccessToken);
            Assert.Equal("正版", account.TypeLabel);

            // 拿到 profile 时必须带上 Bearer 令牌，否则接口 401
            Assert.Equal("Bearer mc-token", download.ProfileAuthHeader);

            // XSTS 的 RelyingParty 必须指向 Minecraft Services
            Assert.Contains("rp://api.minecraftservices.com/", download.XstsBody);
        }

        [Fact]
        public async Task Polls_until_authorization_pending_clears()
        {
            RoutedDownload download = new RoutedDownload(pendingTokenPolls: 1);
            MicrosoftAuthenticator authenticator = Create(download);

            DeviceCodeChallenge challenge = await authenticator.StartAsync(CancellationToken.None);
            Account account = await authenticator.WaitForAccountAsync(challenge, CancellationToken.None);

            Assert.Equal("Steve", account.Name);
            Assert.Equal(2, download.TokenCalls); // 一次 pending，一次成功
        }

        [Fact]
        public async Task Missing_xbox_profile_gives_actionable_message()
        {
            RoutedDownload download = new RoutedDownload();
            MicrosoftAuthenticator authenticator = Create(download);

            DeviceCodeChallenge challenge = await authenticator.StartAsync(CancellationToken.None);

            // 换一个会返回 XErr 的假件
            XerrDownload xerr = new XerrDownload();
            MicrosoftAuthenticator failing = new MicrosoftAuthenticator(xerr, "test-client-id");

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => failing.WaitForAccountAsync(challenge, CancellationToken.None));

            Assert.Contains("Xbox 档案", ex.Message);
        }

        private sealed class XerrDownload : RoutedDownload
        {
            public override Task<string> PostJsonAsync(string url, string jsonBody,
                IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
            {
                if (url.IndexOf("xsts.auth.xboxlive.com", StringComparison.Ordinal) >= 0)
                {
                    return Task.FromResult(@"{ ""XErr"": 2148916233 }");
                }

                return base.PostJsonAsync(url, jsonBody, headers, cancellationToken);
            }
        }

        [Fact]
        public async Task Client_id_is_required_at_start()
        {
            MicrosoftAuthenticator authenticator = new MicrosoftAuthenticator(new RoutedDownload(), "  ");

            Assert.False(authenticator.IsConfigured);

            // 校验推迟到真正登录时：这样组合根可以无条件注册，不至于启动即崩
            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => authenticator.StartAsync(CancellationToken.None));

            Assert.Contains("NLMCL_MS_CLIENT_ID", ex.Message);
        }

        [Fact]
        public void Uuid_is_formatted_with_dashes()
        {
            Assert.Equal(
                "069a79f4-44e9-4726-a5be-fca90e38aaf5",
                MicrosoftAuthenticator.FormatUuid("069a79f444e94726a5befca90e38aaf5"));

            // 已经是带连字符的就原样返回
            Assert.Equal(
                "069a79f4-44e9-4726-a5be-fca90e38aaf5",
                MicrosoftAuthenticator.FormatUuid("069a79f4-44e9-4726-a5be-fca90e38aaf5"));
        }
    }
}
