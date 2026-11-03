using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Resources;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>皮肤：profile 解析、textures 解码、无账号时的行为。</summary>
    public class SkinTests
    {
        private static string Base64Textures(string url, string model)
        {
            string json = @"{ ""textures"": { ""SKIN"": { ""url"": """ + url + @""", "
                + @"""metadata"": { ""model"": """ + model + @""" } } } }";

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        [Fact]
        public void Parses_textures_property()
        {
            string value = Base64Textures("https://example.com/skin.png", "slim");

            SkinInfo info = SkinService.ParseTexturesProperty(value, "ZW_Official", "https://skin.example.cn");

            Assert.Equal("https://example.com/skin.png", info.SkinUrl);
            Assert.Equal("slim", info.Model);
            Assert.Equal("纤细（Alex）", info.ModelLabel);
        }

        [Fact]
        public void Missing_model_defaults_to_classic()
        {
            string json = @"{ ""textures"": { ""SKIN"": { ""url"": ""https://x/s.png"" } } }";
            string value = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            SkinInfo info = SkinService.ParseTexturesProperty(value, "Player", "src");

            Assert.Equal("default", info.Model);
            Assert.Equal("经典（Steve）", info.ModelLabel);
        }

        [Fact]
        public void Invalid_base64_yields_empty_skin()
        {
            SkinInfo info = SkinService.ParseTexturesProperty("not-base64!!", "Player", "src");

            Assert.Null(info.SkinUrl);
        }

        [Fact]
        public void Parses_profile_with_textures_property()
        {
            string profile = @"{
                ""id"": ""069a79f444e94726a5befca90e38aaf5"",
                ""name"": ""ZW_Official"",
                ""properties"": [
                    { ""name"": ""textures"", ""value"": """ + Base64Textures("https://cdn/skin.png", "default") + @""" }
                ]
            }";

            SkinInfo? info = SkinService.ParseProfile(profile, "fallback", "MUA");

            Assert.NotNull(info);
            Assert.Equal("ZW_Official", info!.PlayerName);
            Assert.Equal("https://cdn/skin.png", info.SkinUrl);
        }

        [Fact]
        public void Profile_without_textures_has_no_skin()
        {
            SkinInfo? info = SkinService.ParseProfile(@"{ ""name"": ""Solo"" }", "fallback", "src");

            Assert.NotNull(info);
            Assert.Null(info!.SkinUrl);
            Assert.Equal("Solo", info.PlayerName);
        }

        private sealed class NoopDownload : DownloadServiceStub
        {
        }

        [Fact]
        public async Task Offline_account_has_no_server_skin()
        {
            SkinService service = new SkinService(new NoopDownload(), new AppPaths(@"C:\Temp\roaming"));
            Account offline = new Account("Alex", "uuid", AccountType.Offline);

            Assert.Null(await service.GetAsync(offline, CancellationToken.None));
        }

        private sealed class FakeSkinService : ISkinService
        {
            public Task<SkinInfo?> GetAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<SkinInfo?>(null);

            public Task<string> SaveAsync(SkinInfo skin, CancellationToken cancellationToken)
                => Task.FromResult("saved.png");

            public Task SavePreviewAsync(string url, string targetPath, CancellationToken cancellationToken)
                => Task.CompletedTask;
        }

        [Fact]
        public void Without_account_the_page_says_so()
        {
            SkinViewModel vm = new SkinViewModel(new FakeSkinService());

            vm.SetAccount(null);

            Assert.Equal("未登录", vm.AccountText);
            Assert.Contains("未登录", vm.StatusText);
            Assert.False(vm.HasPreview);
        }
    }
}
