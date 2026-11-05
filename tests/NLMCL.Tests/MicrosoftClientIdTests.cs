using NLMCL.Core.Accounts;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class MicrosoftClientIdTests
    {
        [Fact]
        public void Default_client_id_is_a_guid_so_login_works_out_of_the_box()
        {
            // 内置公共 id：不填任何东西也应该能发起设备码登录
            Assert.True(System.Guid.TryParse(MicrosoftAuthenticator.DefaultClientId, out _));
        }

        [Fact]
        public void Authenticator_uses_the_client_id_given_to_it()
        {
            MicrosoftAuthenticator authenticator = new MicrosoftAuthenticator(
                new StubDownloads(), MicrosoftAuthenticator.DefaultClientId);

            Assert.True(authenticator.IsConfigured);
            Assert.Equal(MicrosoftAuthenticator.DefaultClientId, authenticator.ClientId);
        }

        [Fact]
        public void Authenticator_is_unconfigured_without_client_id()
        {
            MicrosoftAuthenticator authenticator = new MicrosoftAuthenticator(new StubDownloads(), null);

            Assert.False(authenticator.IsConfigured);
        }

        private sealed class StubDownloads : NLMCL.Abstractions.IDownloadService
        {
            public System.Threading.Tasks.Task<string> GetStringAsync(
                string url, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult("{}");

            public System.Threading.Tasks.Task<string> GetStringAsync(
                string url, System.Collections.Generic.IReadOnlyDictionary<string, string> headers,
                System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult("{}");

            public System.Threading.Tasks.Task DownloadFileAsync(
                string url, string localPath, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.CompletedTask;

            public System.Threading.Tasks.Task<string> PostJsonAsync(
                string url, string json, System.Collections.Generic.IReadOnlyDictionary<string, string>? headers,
                System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult(string.Empty);

            public System.Threading.Tasks.Task<string> PostFormAsync(
                string url, System.Collections.Generic.IReadOnlyDictionary<string, string> form,
                System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult(string.Empty);
        }
    }
}