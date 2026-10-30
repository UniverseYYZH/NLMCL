using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeloongCore;
using NLCL.Abstractions;
using NLCL.Core;
using NLCL.Core.Accounts;
using Xunit;

namespace NLCL.Tests
{
    /// <summary>
    /// 登录链路的两个关键回归：
    ///  1. 4xx 的正文必须能被认证器读到（OAuth 用 4xx 表达 authorization_pending / XErr）；
    ///  2. DPAPI 解不开令牌时，不能把账号当"正版"载入。
    /// </summary>
    public class OAuthFlowTests
    {
        /// <summary>模拟 PCL ModNet：非 2xx 直接抛异常，正文挂在 Response 上。</summary>
        private sealed class ThrowingDownload : DownloadServiceStub
        {
            private int _tokenCalls;

            public override Task<string> PostFormAsync(string url,
                IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
            {
                if (url.IndexOf("devicecode", StringComparison.Ordinal) >= 0)
                {
                    return Task.FromResult(@"{
                        ""device_code"": ""dev"", ""user_code"": ""ABCD"",
                        ""verification_uri"": ""https://microsoft.com/link"",
                        ""interval"": 1, ""expires_in"": 900
                    }");
                }

                if (url.IndexOf("/token", StringComparison.Ordinal) >= 0)
                {
                    _tokenCalls++;

                    // 第一次：微软按 RFC 8628 返回 400 + authorization_pending（不是 200！）
                    if (_tokenCalls == 1)
                    {
                        throw new HttpRequestCodeException(
                            "错误码 BadRequest (400)",
                            HttpStatusCode.BadRequest,
                            @"{ ""error"": ""authorization_pending"" }");
                    }

                    return Task.FromResult(@"{ ""access_token"": ""oauth"", ""refresh_token"": ""refresh"" }");
                }

                throw new NotSupportedException(url);
            }

            public override Task<string> PostJsonAsync(string url, string jsonBody,
                IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
            {
                if (url.IndexOf("user.auth.xboxlive.com", StringComparison.Ordinal) >= 0)
                {
                    return Task.FromResult(@"{ ""Token"": ""xbl"", ""DisplayClaims"": { ""xui"": [ { ""uhs"": ""uhs"" } ] } }");
                }

                if (url.IndexOf("xsts.auth.xboxlive.com", StringComparison.Ordinal) >= 0)
                {
                    return Task.FromResult(@"{ ""Token"": ""xsts"" }");
                }

                if (url.IndexOf("login_with_xbox", StringComparison.Ordinal) >= 0)
                {
                    return Task.FromResult(@"{ ""access_token"": ""mc-token"" }");
                }

                throw new NotSupportedException(url);
            }

            public override Task<string> GetStringAsync(string url,
                IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
            {
                return Task.FromResult(@"{ ""id"": ""069a79f444e94726a5befca90e38aaf5"", ""name"": ""Steve"" }");
            }
        }

        [Fact]
        public async Task Reads_error_body_from_4xx_responses()
        {
            OAuthDownloadService service = new OAuthDownloadService(new ThrowingDownload());

            string body = await service.PostFormAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                new Dictionary<string, string>(),
                CancellationToken.None);

            Assert.Contains("authorization_pending", body);
        }

        [Fact]
        public async Task Login_survives_authorization_pending()
        {
            // 修复前：第一次轮询就抛 HttpRequestCodeException，PollForTokenAsync 直接失败。
            MicrosoftAuthenticator authenticator = new MicrosoftAuthenticator(
                new OAuthDownloadService(new ThrowingDownload()), "client-id");

            DeviceCodeChallenge challenge = await authenticator.StartAsync(CancellationToken.None);
            Account account = await authenticator.WaitForAccountAsync(challenge, CancellationToken.None);

            Assert.Equal("Steve", account.Name);
            Assert.Equal(AccountType.Microsoft, account.Type);
        }

        [Fact]
        public async Task Microsoft_account_with_undecryptable_token_is_not_loaded()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlcl-acc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DataRoot);
                File.WriteAllText(paths.AccountsFile, @"{
                    ""name"": ""Steve"",
                    ""uuid"": ""069a79f4-44e9-4726-a5be-fca90e38aaf5"",
                    ""type"": ""Microsoft"",
                    ""accessToken"": ""dpapi:AAAA"",
                    ""refreshToken"": ""dpapi:AAAA""
                }", Encoding.UTF8);

                Account? account = await new JsonAccountStore(paths).LoadAsync(CancellationToken.None);

                Assert.Null(account); // 解不开就不能装成正版
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Offline_account_still_loads_without_tokens()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlcl-off-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DataRoot);
                File.WriteAllText(paths.AccountsFile, @"{
                    ""name"": ""Alex"",
                    ""uuid"": ""a"",
                    ""type"": ""Offline""
                }", Encoding.UTF8);

                Account? account = await new JsonAccountStore(paths).LoadAsync(CancellationToken.None);

                Assert.NotNull(account);
                Assert.Equal(AccountType.Offline, account!.Type);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
