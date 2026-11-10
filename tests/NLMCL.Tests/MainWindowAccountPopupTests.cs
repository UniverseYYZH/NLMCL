using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>账号弹层离屏渲染：确认多账号行带头像，不只是文字。</summary>
    public class MainWindowAccountPopupTests
    {
        [Fact]
        public void Account_popup_renders_rows_with_avatars()
        {
            Exception? failure = null;
            string? output = null;
            string? avatar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");

            Thread thread = new Thread(() =>
            {
                try
                {
                    string root = Path.Combine(
                        Path.GetTempPath(), "nlmcl-account-shot-" + Guid.NewGuid().ToString("N"));

                    try
                    {
                        TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore();
                        store.All.Add(new Account("Alpha", "uuid-alpha", AccountType.Offline));
                        store.All.Add(new Account("Beta", "uuid-beta", AccountType.Offline));

                        MainViewModel vm = TestSupport.CreateMain(
                            new TestSupport.EmptyCatalog(),
                            new AppPaths(root),
                            accounts: store,
                            skins: new AvatarSkin(avatar));

                        vm.SwitchAccountAsync(store.All[0]).GetAwaiter().GetResult();

                        for (int i = 0; i < 40 && vm.SavedAccounts.Count < 2; i++)
                        {
                            Thread.Sleep(50);
                        }

                        for (int i = 0; i < 40; i++)
                        {
                            bool ready = true;
                            foreach (AccountItemViewModel item in vm.SavedAccounts)
                            {
                                ready &= item.HasAvatar;
                            }

                            if (ready && vm.SavedAccounts.Count == 2)
                            {
                                break;
                            }

                            Thread.Sleep(50);
                        }

                        Assert.Equal(2, vm.SavedAccounts.Count);
                        Assert.All(vm.SavedAccounts, item => Assert.True(item.HasAvatar));

                        vm.IsAccountPopupOpen = true;

                        const int width = 1100;
                        const int height = 720;

                        NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm)
                        {
                            Width = width,
                            Height = height
                        };

                        FrameworkElement rootElement = (FrameworkElement)window.Content;
                        rootElement.Measure(new Size(width, height));
                        rootElement.Arrange(new Rect(0, 0, width, height));
                        rootElement.UpdateLayout();

                        RenderTargetBitmap bitmap = new RenderTargetBitmap(
                            width, height, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(rootElement);

                        PngBitmapEncoder encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));

                        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
                        Directory.CreateDirectory(dir);
                        output = Path.Combine(dir, "ui-account-popup.png");

                        using (FileStream stream = File.Create(output))
                        {
                            encoder.Save(stream);
                        }

                        window.Close();
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
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "渲染线程超时。");
            Assert.Null(failure);
            Assert.NotNull(output);
            Assert.True(new FileInfo(output!).Length > 10_000, "账号弹层渲染结果过小。");
        }

        private sealed class AvatarSkin : ISkinService
        {
            private readonly string? _avatar;

            public AvatarSkin(string? avatar)
            {
                _avatar = avatar;
            }

            public Task<SkinInfo?> GetAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<SkinInfo?>(null);

            public Task<string> SaveAsync(SkinInfo skin, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);

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

            public Task<byte[]> Create3DPreviewAsync(
                string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
                CancellationToken cancellationToken)
                => Task.FromResult(TestSupport.OnePixelPng);

            public Task<string?> GetAvatarAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult(_avatar);

            public Task<byte[]?> GetAvatarPngAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<byte[]?>(TestSupport.OnePixelPng);

            public string GetDefaultSkinPath() => _avatar ?? string.Empty;

            public Task<string> GetDefaultSkinAsync(CancellationToken cancellationToken)
                => Task.FromResult(_avatar ?? string.Empty);

            public IReadOnlyList<SkinPreset> GetPresets() => Array.Empty<SkinPreset>();

            public string GetPresetPath(string presetId) => string.Empty;

            public Task<SkinInfo?> LookupByNameAsync(string playerName, CancellationToken cancellationToken)
                => Task.FromResult<SkinInfo?>(null);

            public Task<string> SavePresetAsync(
                string presetId, string playerName, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
        }
    }
}
