using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public async Task Importing_an_existing_library_skin_does_not_duplicate_it()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-localskin-" + Guid.NewGuid().ToString("N"));
            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");

            try
            {
                AppPaths paths = new AppPaths(root);
                SkinService service = new SkinService(new NoopDownload(), paths);
                Account alpha = new Account("Alpha", "uuid-alpha", AccountType.Offline);

                string first = await service.ImportLocalAsync(alpha, source, CancellationToken.None);
                int countAfterFirst = Directory.GetFiles(paths.SkinDirectory, "*.png").Length;

                string second = await service.ImportLocalAsync(alpha, first, CancellationToken.None);

                Assert.Equal(first, second);
                Assert.Equal(countAfterFirst, Directory.GetFiles(paths.SkinDirectory, "*.png").Length);
                Assert.Equal(first, await service.GetLocalSkinPathAsync(alpha, CancellationToken.None));
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
        public async Task Microsoft_skin_uses_sessionserver_before_the_token_api()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-session-skin-" + Guid.NewGuid().ToString("N"));
            SessionSkinDownload download = new SessionSkinDownload(Base64Textures("https://cdn/skin.png", "default"));
            SkinService service = new SkinService(download, new AppPaths(root));
            Account account = new Account(
                "UniverseYYZH", "0ae3d697-22ae-4fe0-8eef-80acdcc18fd6",
                AccountType.Microsoft, "expired-token");

            try
            {
                SkinInfo? info = await service.GetAsync(account, CancellationToken.None);

                Assert.NotNull(info);
                Assert.Equal("https://cdn/skin.png", info!.SkinUrl);
                Assert.Equal("Mojang 会话服务器", info.Source);
                Assert.False(download.TokenApiTried);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public async Task Avatar_cache_is_stable_and_lives_under_the_skin_cache()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-avatar-cache-" + Guid.NewGuid().ToString("N"));
            string steve = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");
            SessionSkinDownload download = new SessionSkinDownload(
                Base64Textures("https://cdn/skin.png", "default"))
            {
                SkinPath = steve
            };
            SkinService service = new SkinService(download, new AppPaths(root));
            Account account = new Account(
                "UniverseYYZH", "0ae3d697-22ae-4fe0-8eef-80acdcc18fd6",
                AccountType.Microsoft, "token");

            try
            {
                string? first = await service.GetAvatarAsync(account, CancellationToken.None);
                string? second = await service.GetAvatarAsync(account, CancellationToken.None);

                Assert.NotNull(first);
                Assert.Equal(first, second);
                Assert.StartsWith(
                    Path.Combine(new AppPaths(root).SkinDirectory, "cache"),
                    first,
                    StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(first!));
                Assert.Equal(first, SkinService.GetCachedAvatarPath(new AppPaths(root), account));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public async Task Avatar_png_renders_a_local_skin_without_writing_a_file()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-avatar-png-" + Guid.NewGuid().ToString("N"));
            string skins = Path.Combine(root, "skins");
            Directory.CreateDirectory(skins);
            string local = Path.Combine(skins, "UniverseYYZH-local-test.png");
            File.Copy(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png"),
                local);

            try
            {
                SkinService service = new SkinService(
                    new SessionSkinDownload(Base64Textures("https://cdn/skin.png", "default"))
                    {
                        SkinPath = local
                    },
                    new AppPaths(root));
                Account account = new Account(
                    "UniverseYYZH", "0ae3d697-22ae-4fe0-8eef-80acdcc18fd6",
                    AccountType.Microsoft, "token");

                byte[]? png = await service.GetAvatarPngAsync(account, CancellationToken.None);

                Assert.NotNull(png);
                Assert.True(png!.Length > 100);
                Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
                Assert.False(Directory.Exists(Path.Combine(skins, "cache")));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public async Task Skin_service_failure_still_leaves_a_default_preview()
        {
            FakeSkinService skins = new FakeSkinService
            {
                GetHandler = _ => throw new InvalidOperationException("Minecraft Services 被阻断")
            };

            SkinViewModel vm = new SkinViewModel(skins);
            vm.SetAccount(new Account("UniverseYYZH", "uuid", AccountType.Microsoft));

            await Task.Delay(350);

            Assert.NotNull(vm.PreviewPng);
            Assert.NotEmpty(vm.PreviewPng!);
            Assert.Contains("默认皮肤", vm.StatusText);
        }

        [Fact]
        public async Task Cache_write_failure_still_previews_online_skin_from_temp()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-skinfallback-");
            AppPaths paths = new AppPaths(root);
            string source = Path.Combine(AppContext.BaseDirectory, "assets", "steve.png");

            FakeSkinService skins = new FakeSkinService
            {
                GetHandler = _ => Task.FromResult<SkinInfo?>(
                    new SkinInfo("Alpha", "https://example.com/custom.png", "default", "src")),
                SavePreviewHandler = (_, target) =>
                {
                    if (target.IndexOf("cache", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        throw new UnauthorizedAccessException("cache 不可写");
                    }

                    File.Copy(source, target, true);
                    return Task.CompletedTask;
                }
            };

            SkinViewModel vm = new SkinViewModel(skins, paths: paths);
            vm.SetAccount(new Account("Alpha", "uuid-alpha", AccountType.Microsoft));

            await Task.Delay(500);

            Assert.NotNull(vm.PreviewPng);
            Assert.NotEmpty(vm.PreviewPng!);
            Assert.Contains("临时预览", vm.StatusText);
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

        [Fact]
        public async Task Local_skin_library_scans_skin_directory_and_uses_a_card()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-skinlib-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(root);
            Directory.CreateDirectory(paths.SkinDirectory);

            try
            {
                string file = Path.Combine(paths.SkinDirectory, "alpha-local-20260101.png");
                File.WriteAllBytes(file, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

                SkinViewModel vm = new SkinViewModel(new FakeSkinService(), paths: paths);

                Assert.True(vm.HasLocalSkins);
                Assert.Single(vm.LocalSkins);
                Assert.Equal("alpha-local-20260101", vm.LocalSkins[0].Name);

                await vm.UseLocalSkinCommand.ExecuteAsync(vm.LocalSkins[0]);

                Assert.Equal("本地皮肤", vm.ModelText);
                Assert.Contains("alpha-local-20260101", vm.StatusText);
                Assert.True(vm.LocalSkins[0].IsCurrent);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }
        [Fact]
        public async Task Local_sort_keeps_current_first_and_applies_search_and_usage_filters()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-localsort-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(root);
            Directory.CreateDirectory(paths.SkinDirectory);

            try
            {
                foreach (string name in new[] { "zeta", "alpha", "middle" })
                {
                    string file = Path.Combine(paths.SkinDirectory, name + ".png");
                    File.WriteAllBytes(file, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                    File.SetCreationTimeUtc(file, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddDays(name == "alpha" ? 1 : name == "middle" ? 2 : 3));
                }

                SkinViewModel vm = new SkinViewModel(new FakeSkinService(), paths: paths);
                vm.LocalSortOption = "名称";
                vm.LocalSortDescending = false;

                Assert.Equal("alpha", vm.LocalSkins[0].Name);

                await vm.UseLocalSkinCommand.ExecuteAsync(
                    vm.LocalSkins.First(item => item.Name == "zeta"));

                vm.LocalSearchText = "alpha";

                Assert.Equal("zeta", vm.LocalSkins[0].Name);
                Assert.Equal(2, vm.LocalSkins.Count);
                Assert.Contains(vm.LocalSkins, item => item.Name == "alpha");

                vm.LocalSearchText = string.Empty;
                vm.LocalUsageFilter = "从未使用";

                Assert.Equal("zeta", vm.LocalSkins[0].Name);
                Assert.Contains(vm.LocalSkins, item => item.Name == "middle");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public async Task Current_local_skin_starts_usage_tracking_at_startup()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-usage-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(root);
            Directory.CreateDirectory(paths.SkinDirectory);
            string skin = Path.Combine(paths.SkinDirectory, "current.png");
            File.WriteAllBytes(skin, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            try
            {
                FakeSkinService skins = new FakeSkinService
                {
                    LocalSkinHandler = _ => Task.FromResult<string?>(skin)
                };
                SkinViewModel vm = new SkinViewModel(skins, paths: paths);
                vm.SetAccount(new Account("Alpha", "uuid-alpha", AccountType.Offline));

                for (int i = 0; i < 50 && !vm.LocalSkins.Any(item => item.IsCurrent); i++)
                {
                    await Task.Delay(40);
                }

                LocalSkinItem current = Assert.Single(vm.LocalSkins, item => item.IsCurrent);
                Assert.True(current.LastUsedAt.HasValue);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        private sealed class FakeSkinService : ISkinService
        {
            public Func<Account, Task<SkinInfo?>>? GetHandler { get; set; }

            public Func<string, string, Task>? SavePreviewHandler { get; set; }

            public Func<string, Task<SkinInfo?>>? LookupHandler { get; set; }

            public Func<Account, Task<string?>>? LocalSkinHandler { get; set; }

            public Task<SkinInfo?> GetAsync(Account account, CancellationToken cancellationToken)
                => GetHandler == null
                    ? Task.FromResult<SkinInfo?>(null)
                    : GetHandler(account);

            public Task<string> SaveAsync(SkinInfo skin, CancellationToken cancellationToken)
                => Task.FromResult("saved.png");

            public Task SavePreviewAsync(string url, string targetPath, CancellationToken cancellationToken)
                => SavePreviewHandler == null
                    ? Task.CompletedTask
                    : SavePreviewHandler(url, targetPath);

            public Task<string> ImportLocalAsync(
                string sourcePath, string playerName, CancellationToken cancellationToken)
                => Task.FromResult(sourcePath);

            public Task<string> ImportLocalAsync(
                Account account, string sourcePath, CancellationToken cancellationToken)
                => Task.FromResult(sourcePath);

            public Task<string?> GetLocalSkinPathAsync(
                Account account, CancellationToken cancellationToken)
                => LocalSkinHandler == null
                    ? Task.FromResult<string?>(null)
                    : LocalSkinHandler(account);

            public Task<byte[]> Create3DPreviewAsync(
                string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
                CancellationToken cancellationToken)
                => Task.FromResult(TestSupport.OnePixelPng);

            public Task<string?> GetAvatarAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<string?>(null);

            public Task<byte[]?> GetAvatarPngAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<byte[]?>(null);

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
                => LookupHandler == null
                    ? System.Threading.Tasks.Task.FromResult<SkinInfo?>(null)
                    : LookupHandler(playerName);

            public System.Threading.Tasks.Task<string> SavePresetAsync(
                string presetId, string playerName, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult(string.Empty);
        }

        [Fact]
        public async Task Player_id_search_uses_named_lookup_and_shows_a_result()
        {
            FakeSkinService skins = new FakeSkinService
            {
                LookupHandler = name => Task.FromResult<SkinInfo?>(
                    new SkinInfo(name, "https://example.com/" + name + ".png", "slim", "Mojang"))
            };

            SkinViewModel vm = new SkinViewModel(skins);
            vm.SetAccount(new Account("UniverseYYZH", "uuid", AccountType.Microsoft));
            vm.MuaSearchMode = "正版玩家";
            vm.MuaSearchText = "Notch";

            await vm.SearchLibraryCommand.ExecuteAsync(null);

            Assert.True(vm.HasPlayerSkinResult);
            Assert.Equal("Notch", vm.PlayerSkinName);
            Assert.Contains("Mojang", vm.PlayerSkinStatus);
            Assert.Contains("Notch.png", vm.PlayerSkinTextureUrl);
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
        public void Renderer_returns_png_bytes_without_a_file()
        {
            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");

            byte[] png = NLMCL.Core.Skins.SkinRender3D.RenderPng(source, 320, 28, 8, slim: false);

            Assert.True(png.Length > 100);
            Assert.Equal((byte)0x89, png[0]);
            Assert.Equal((byte)0x50, png[1]);
            Assert.Equal((byte)0x4E, png[2]);
            Assert.Equal((byte)0x47, png[3]);
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

            public Task<byte[]> Create3DPreviewAsync(
                string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
                CancellationToken cancellationToken)
                => Task.FromResult(TestSupport.OnePixelPng);

            public Task<string?> GetAvatarAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<string?>(@"C:\skins\avatar.png");

            public Task<byte[]?> GetAvatarPngAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<byte[]?>(TestSupport.OnePixelPng);

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
            Assert.NotEmpty(vm.PreviewPng!);
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

        private sealed class SessionSkinDownload : DownloadServiceStub
        {
            private readonly string _textures;

            public SessionSkinDownload(string textures)
            {
                _textures = textures;
            }

            public bool TokenApiTried { get; private set; }

            public string? SkinPath { get; set; }

            public override Task DownloadFileAsync(
                string url, string localPath, CancellationToken cancellationToken)
            {
                if (SkinPath == null)
                {
                    throw new HttpRequestException("no skin fixture");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                File.Copy(SkinPath, localPath, overwrite: true);
                return Task.CompletedTask;
            }

            public override Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                if (url.Contains("sessionserver.mojang.com"))
                {
                    string profile = "{\"id\":\"0ae3d69722ae4fe08eef80acdcc18fd6\","
                        + "\"name\":\"UniverseYYZH\",\"properties\":[{\"name\":\"textures\",\"value\":\""
                        + _textures + "\"}]}";
                    return Task.FromResult(profile);
                }

                throw new HttpRequestException("unexpected request: " + url);
            }

            public override Task<string> GetStringAsync(string url,
                IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
            {
                TokenApiTried = true;
                throw new HttpRequestException("token API blocked");
            }
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
