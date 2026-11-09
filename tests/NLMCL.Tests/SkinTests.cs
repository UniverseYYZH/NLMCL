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
using System.Collections.Generic;
using System.Net.Http;
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
        public void Parses_profile_with_top_level_skins_array()
        {
            string profile = @"{
                ""id"": ""0ae3d69722ae4fe08eef80acdcc18fd6"",
                ""name"": ""UniverseYYZH"",
                ""skins"": [
                    { ""state"": ""INACTIVE"", ""url"": ""http://textures.minecraft.net/texture/old"", ""variant"": ""CLASSIC"" },
                    { ""state"": ""ACTIVE"", ""url"": ""http://textures.minecraft.net/texture/active"", ""variant"": ""SLIM"" }
                ]
            }";

            SkinInfo? info = SkinService.ParseProfile(profile, "fallback", "Minecraft Services");

            Assert.NotNull(info);
            Assert.Equal("UniverseYYZH", info!.PlayerName);
            Assert.Equal("https://textures.minecraft.net/texture/active", info.SkinUrl);
            Assert.Equal("slim", info.Model);
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

        [Fact]
        public async Task Offline_avatar_falls_back_to_default_skin()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-avatar-" + Guid.NewGuid().ToString("N"));

            try
            {
                SkinService service = new SkinService(new NoopDownload(), new AppPaths(root));
                Account offline = new Account("NoSkin", "uuid-noskin", AccountType.Offline);

                string? avatar = await service.GetAvatarAsync(offline, CancellationToken.None);

                Assert.NotNull(avatar);
                Assert.True(File.Exists(avatar!));
                Assert.True(new FileInfo(avatar!).Length > 0);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Local_skin_is_remembered_per_offline_account()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-localskin-" + Guid.NewGuid().ToString("N"));
            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");

            try
            {
                AppPaths paths = new AppPaths(root);
                SkinService service = new SkinService(new NoopDownload(), paths);

                Account alpha = new Account("Alpha", "uuid-alpha", AccountType.Offline);
                Account beta = new Account("Beta", "uuid-beta", AccountType.Offline);

                string saved = await service.ImportLocalAsync(alpha, source, CancellationToken.None);

                Assert.True(File.Exists(saved));
                Assert.Equal(saved, await service.GetLocalSkinPathAsync(alpha, CancellationToken.None));
                Assert.Null(await service.GetLocalSkinPathAsync(beta, CancellationToken.None));

                string? avatar = await service.GetAvatarAsync(alpha, CancellationToken.None);
                Assert.NotNull(avatar);
                Assert.True(File.Exists(avatar!));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Custom_skin_label_is_not_classic_steve()
        {
            FakeSkinService skins = new FakeSkinService
            {
                GetHandler = _ => Task.FromResult<SkinInfo?>(
                    new SkinInfo("Alpha", "https://example.com/custom.png", "default", "src"))
            };

            SkinViewModel vm = new SkinViewModel(skins);
            vm.SetAccount(new Account("Alpha", "uuid-alpha", AccountType.Microsoft));

            await Task.Delay(300);

            Assert.Equal("自定义皮肤 · 经典", vm.ModelText);
        }

        [Fact]
        public async Task Skin_refresh_ignores_stale_account_result()
        {
            FakeSkinService skins = new FakeSkinService
            {
                GetHandler = async account =>
                {
                    await Task.Delay(account.Name == "Alpha" ? 250 : 10);
                    return new SkinInfo(
                        account.Name,
                        "https://example.com/" + account.Name + ".png",
                        "default",
                        "src-" + account.Name);
                }
            };

            SkinViewModel vm = new SkinViewModel(skins);

            vm.SetAccount(new Account("Alpha", "uuid-alpha", AccountType.Microsoft));
            vm.SetAccount(new Account("Beta", "uuid-beta", AccountType.Microsoft));

            await Task.Delay(600);

            Assert.Contains("Beta", vm.AccountText);
            Assert.Contains("src-Beta", vm.StatusText);
        }

        private sealed class FakeSkinService : ISkinService
        {
            public Func<Account, Task<SkinInfo?>>? GetHandler { get; set; }

            public Task<SkinInfo?> GetAsync(Account account, CancellationToken cancellationToken)
                => GetHandler == null
                    ? Task.FromResult<SkinInfo?>(null)
                    : GetHandler(account);

            public Task<string> SaveAsync(SkinInfo skin, CancellationToken cancellationToken)
                => Task.FromResult("saved.png");

            public Task SavePreviewAsync(string url, string targetPath, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task<string> ImportLocalAsync(
                string sourcePath, string playerName, CancellationToken cancellationToken)
                => Task.FromResult(sourcePath);

            public Task<string> ImportLocalAsync(
                Account account, string sourcePath, CancellationToken cancellationToken)
                => Task.FromResult(sourcePath);

            public Task<string?> GetLocalSkinPathAsync(
                Account account, CancellationToken cancellationToken)
                => Task.FromResult<string?>(null);

            public Task<string> CreatePreviewAsync(string skinPngPath, CancellationToken cancellationToken)
                => Task.FromResult(skinPngPath);

            public Task<string> Create3DPreviewAsync(
                string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
                CancellationToken cancellationToken)
                => Task.FromResult(skinPngPath);

            public Task<string?> GetAvatarAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<string?>(null);

            public string GetDefaultSkinPath()
                => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nlmcl-default-skin.png");
            public System.Threading.Tasks.Task<string> GetDefaultSkinAsync(
                System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult(GetDefaultSkinPath());
            public System.Collections.Generic.IReadOnlyList<SkinPreset> GetPresets()
                => System.Array.Empty<SkinPreset>();

            public string GetPresetPath(string presetId) => string.Empty;

            public System.Threading.Tasks.Task<SkinInfo?> LookupByNameAsync(
                string playerName, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult<SkinInfo?>(null);

            public System.Threading.Tasks.Task<string> SavePresetAsync(
                string presetId, string playerName, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult(string.Empty);
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

            public Task<string> ImportLocalAsync(
                Account account, string sourcePath, CancellationToken cancellationToken)
                => Task.FromResult(@"C:\skins\copied.png");

            public Task<string?> GetLocalSkinPathAsync(
                Account account, CancellationToken cancellationToken)
                => Task.FromResult<string?>(null);

            public Task<string> CreatePreviewAsync(string skinPngPath, CancellationToken cancellationToken)
                => Task.FromResult(@"C:\skins\doll.png");

            public Task<string> Create3DPreviewAsync(
                string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
                CancellationToken cancellationToken)
                => Task.FromResult(@"C:\skins\3d.png");

            public Task<string?> GetAvatarAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<string?>(@"C:\skins\avatar.png");

            public string GetDefaultSkinPath()
                => @"C:\skins\default.png";
            public System.Threading.Tasks.Task<string> GetDefaultSkinAsync(
                System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult(GetDefaultSkinPath());
            public System.Collections.Generic.IReadOnlyList<SkinPreset> GetPresets()
                => System.Array.Empty<SkinPreset>();

            public string GetPresetPath(string presetId) => string.Empty;

            public System.Threading.Tasks.Task<SkinInfo?> LookupByNameAsync(
                string playerName, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult<SkinInfo?>(null);

            public System.Threading.Tasks.Task<string> SavePresetAsync(
                string presetId, string playerName, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult(string.Empty);
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
        public void Auto_rotation_advances_yaw_and_can_be_toggled()
        {
            SkinViewModel vm = new SkinViewModel(new FakeSkinService());

            double before = vm.Yaw;

            vm.AutoRotateStep();

            Assert.True(vm.Yaw > before || vm.Yaw < before, "自动旋转应改变角度");

            vm.ToggleAutoRotate();
            Assert.False(vm.IsAutoRotating);

            double stopped = vm.Yaw;
            vm.AutoRotateStep();

            Assert.Equal(stopped, vm.Yaw);
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

        [Fact]
        public async Task Account_without_skin_still_gets_a_preview()
        {
            // 真实服务：没有自定义皮肤时返回默认皮肤路径，界面不该留空白
            NLMCL.Core.Resources.SkinService service = new NLMCL.Core.Resources.SkinService(
                new NoopDownload(),
                new NLMCL.Core.AppPaths(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nlmcl-skin-default")));

            string path = service.GetDefaultSkinPath();

            Assert.True(System.IO.File.Exists(path));
            Assert.True(new System.IO.FileInfo(path).Length > 256);

            // 默认皮肤能被渲染器读出来（否则头像会崩）
            string rendered = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-default-head-" + System.Guid.NewGuid().ToString("N") + ".png");
            NLMCL.Core.Skins.SkinAvatarRenderer.RenderHead(path, rendered, 8);

            Assert.True(System.IO.File.Exists(rendered));
        }

        [Fact]
        public async Task Offline_name_matching_a_premium_account_gets_its_skin()
        {
            // Mozilla 名字查询返回 uuid，紧接着 sessionserver 返回 textures
            string textures = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                "{\"textures\":{\"SKIN\":{\"url\":\"https://textures.minecraft.net/texture/abc\"}}}"));
            string profile = "{\"name\":\"Notch\",\"properties\":[{\"name\":\"textures\",\"value\":\""
                + textures + "\"}]}";

            QueueDownload download = new QueueDownload(new[]
            {
                "{\"id\":\"069a79f444e94726a5befca90e38aaf5\",\"name\":\"Notch\"}",
                profile
            });

            SkinService service = new SkinService(download, new AppPaths(@"C:\Temp\roaming"));
            SkinInfo? skin = await service.LookupByNameAsync("Notch", CancellationToken.None);

            Assert.NotNull(skin);
            Assert.Contains("textures.minecraft.net", skin!.SkinUrl);
        }

        [Fact]
        public async Task Offline_name_without_premium_account_returns_nothing()
        {
            QueueDownload download = new QueueDownload(Array.Empty<string>());
            SkinService service = new SkinService(download, new AppPaths(@"C:\Temp\roaming"));

            Assert.Null(await service.LookupByNameAsync("not-a-real-player-xyz", CancellationToken.None));
        }

        private sealed class QueueDownload : IDownloadService
        {
            private readonly Queue<string> _payloads;

            public QueueDownload(IEnumerable<string> payloads)
            {
                _payloads = new Queue<string>(payloads);
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                return Task.FromResult(_payloads.Count > 0 ? _payloads.Dequeue() : throw new HttpRequestException("404"));
            }

            public Task<string> GetStringAsync(
                string url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
                => GetStringAsync(url, cancellationToken);

            public Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task<string> PostJsonAsync(
                string url, string json, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);

            public Task<string> PostFormAsync(
                string url, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
        }


        [Fact]
        public void Bundled_default_skin_is_a_real_steve_not_a_generated_placeholder()
        {
            SkinService service = new SkinService(new NoopDownload(), new AppPaths(@"C:\Temp\roaming"));
            string path = service.GetDefaultSkinPath();

            Assert.True(File.Exists(path));

            // 真正的 Steve 是 64×64、几 KB 的 PNG；程序生成的占位图只有几百字节
            Assert.True(new FileInfo(path).Length > 1024, "内置默认皮肤应是真实皮肤，不是生成的占位");

            using (System.Drawing.Image image = System.Drawing.Image.FromFile(path))
            {
                Assert.Equal(64, image.Width);
                Assert.Equal(64, image.Height);
            }
        }

        [Fact]
        public async Task Default_skin_download_falls_back_to_bundled_copy()
        {
            // 下载失败时不能抛：退回内置的真实皮肤副本
            SkinService service = new SkinService(new NoopDownload(), new AppPaths(@"C:\Temp\roaming"));

            string path = await service.GetDefaultSkinAsync(CancellationToken.None);

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 1024);
        }

        [Fact]
        public async Task Offline_account_does_not_auto_lookup_premium_skin()
        {
            // ZW 这类名字可能撞上正版账号，但不该自动冒用别人的皮肤
            QueueDownload download = new QueueDownload(new[]
            {
                "{\"id\":\"d32ca748724248ccb40e42884f1cc831\",\"name\":\"Zw\"}"
            });

            SkinService service = new SkinService(download, new AppPaths(@"C:\Temp\roaming"));
            Account offline = new Account("ZW", "uuid", AccountType.Offline);

            SkinInfo? skin = await service.GetAsync(offline, CancellationToken.None);

            Assert.Null(skin);
        }
    }
}
