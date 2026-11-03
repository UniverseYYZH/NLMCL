using System;
using System.Collections.Generic;
using System.IO;
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

            public Task<string> ImportLocalAsync(
                string sourcePath, string playerName, CancellationToken cancellationToken)
                => Task.FromResult(sourcePath);

            public Task<string> CreatePreviewAsync(string skinPngPath, CancellationToken cancellationToken)
                => Task.FromResult(skinPngPath);

            public Task<string> Create3DPreviewAsync(
                string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
                CancellationToken cancellationToken)
                => Task.FromResult(skinPngPath);

            public Task<string?> GetAvatarAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<string?>(null);
        }

        [Fact]
        public void Renderer_builds_a_paper_doll()
        {
            string artifacts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
            Directory.CreateDirectory(artifacts);

            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "notch.png");

            Assert.True(File.Exists(source), "缺少测试皮肤素材：" + source);

            string output = Path.Combine(artifacts, "skin-doll.png");

            NLMCL.Core.Skins.SkinPreviewRenderer.RenderPaperDoll(source, output, 8);

            using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(output))
            {
                Assert.Equal(128, bitmap.Width);
                Assert.Equal(256, bitmap.Height);
            }
        }

        [Fact]
        public void Renderer_handles_modern_64x64_skin()
        {
            string artifacts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
            Directory.CreateDirectory(artifacts);

            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");
            string output = Path.Combine(artifacts, "skin-doll-modern.png");

            NLMCL.Core.Skins.SkinPreviewRenderer.RenderPaperDoll(source, output, 8);

            using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(output))
            {
                Assert.Equal(128, bitmap.Width);
                Assert.Equal(256, bitmap.Height);

                // 头部中央不该是全透明：说明确实画上了东西
                System.Drawing.Color center = bitmap.GetPixel(64, 32);
                Assert.True(center.A > 0, "头部区域没有绘制内容");
            }
        }

        [Fact]
        public void Avatar_renderer_crops_the_head()
        {
            string artifacts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
            Directory.CreateDirectory(artifacts);

            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");
            string output = Path.Combine(artifacts, "avatar.png");

            NLMCL.Core.Skins.SkinAvatarRenderer.RenderHead(source, output, 8);

            using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(output))
            using (System.Drawing.Bitmap skin = new System.Drawing.Bitmap(source))
            {
                Assert.Equal(64, bitmap.Width);
                Assert.Equal(64, bitmap.Height);

                // 左上角对应头部贴图的 (8,8)，颜色应一致
                System.Drawing.Color expected = skin.GetPixel(8, 8);
                System.Drawing.Color actual = bitmap.GetPixel(0, 0);

                Assert.Equal(expected.R, actual.R);
                Assert.Equal(expected.G, actual.G);
                Assert.Equal(expected.B, actual.B);
            }
        }

        [Fact]
        public void Renderer_builds_a_3d_view()
        {
            string artifacts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
            Directory.CreateDirectory(artifacts);

            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");
            string output = Path.Combine(artifacts, "skin-3d.png");

            NLMCL.Core.Skins.SkinRender3D.Render(source, output, 384, 28, 8, slim: false);

            using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(output))
            {
                Assert.Equal(384, bitmap.Width);
                Assert.Equal(384, bitmap.Height);

                // 画面中央应该有人物（非全透明）
                Assert.True(bitmap.GetPixel(192, 192).A > 0 || bitmap.GetPixel(192, 150).A > 0);
            }
        }

        [Fact]
        public void Renderer_fails_on_missing_file()
        {
            Assert.Throws<FileNotFoundException>(() =>
                NLMCL.Core.Skins.SkinPreviewRenderer.RenderPaperDoll(
                    Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".png"),
                    Path.Combine(Path.GetTempPath(), "out.png")));
        }
        [Fact]
        public void Png_header_is_required_for_local_skin()
        {
            string dir = Path.Combine(Path.GetTempPath(), "nlmcl-png-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                string good = Path.Combine(dir, "good.png");
                File.WriteAllBytes(good, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 });

                string bad = Path.Combine(dir, "bad.png");
                File.WriteAllBytes(bad, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 });

                Assert.True(SkinService.HasPngHeader(good));
                Assert.False(SkinService.HasPngHeader(bad));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private sealed class FakePicker : IFilePicker
        {
            private readonly string? _path;

            public FakePicker(string? path)
            {
                _path = path;
            }

            public string? PickFile(string title, string filter) => _path;
        }

        private sealed class CopyingSkinService : ISkinService
        {
            public Task<SkinInfo?> GetAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<SkinInfo?>(null);

            public Task<string> SaveAsync(SkinInfo skin, CancellationToken cancellationToken)
                => Task.FromResult("x.png");

            public Task SavePreviewAsync(string url, string targetPath, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task<string> ImportLocalAsync(
                string sourcePath, string playerName, CancellationToken cancellationToken)
                => Task.FromResult(@"C:\skins\copied.png");

            public Task<string> CreatePreviewAsync(string skinPngPath, CancellationToken cancellationToken)
                => Task.FromResult(@"C:\skins\doll.png");

            public Task<string> Create3DPreviewAsync(
                string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
                CancellationToken cancellationToken)
                => Task.FromResult(@"C:\skins\3d.png");

            public Task<string?> GetAvatarAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<string?>(@"C:\skins\avatar.png");
        }

        [Fact]
        public async Task Picking_local_skin_sets_preview()
        {
            SkinViewModel vm = new SkinViewModel(new CopyingSkinService(), new FakePicker(@"C:\in\skin.png"));

            await vm.PickLocalAsync();

            Assert.True(vm.HasPreview);
            Assert.Equal(@"C:\skins\3d.png", vm.PreviewPath);
            Assert.Equal("本地皮肤", vm.ModelText);
            Assert.Contains("本地皮肤", vm.StatusText);
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
