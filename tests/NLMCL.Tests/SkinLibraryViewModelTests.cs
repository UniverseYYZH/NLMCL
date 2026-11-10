using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>皮肤库与皮肤页的连接：列表、下载、非外置账号只预览。</summary>
    public sealed class SkinLibraryViewModelTests
    {
        [Fact]
        public async Task Loads_library_and_downloads_skin_for_preview()
        {
            FakeLibrary library = new FakeLibrary();
            SkinViewModel vm = new SkinViewModel(
                new TestSupport.NoopSkinService(), null, library);

            vm.SetAccount(new Account("Alpha", "uuid-alpha", AccountType.Offline));
            await Task.Delay(250);

            await vm.LoadLibraryAsync();

            Assert.Single(vm.LibrarySkins);
            Assert.Equal("测试皮肤", vm.LibrarySkins[0].Name);

            await vm.ApplyLibrarySkinAsync(vm.LibrarySkins[0]);

            Assert.NotNull(vm.PreviewPng);
            Assert.Contains("已下载", vm.LibraryStatusText);
        }

        [Fact]
        public async Task Preview_library_skin_updates_3d_preview_without_upload()
        {
            FakeLibrary library = new FakeLibrary();
            SkinViewModel vm = new SkinViewModel(
                new TestSupport.NoopSkinService(), null, library);

            vm.SetAccount(new Account("Alpha", "uuid-alpha", AccountType.Offline));
            await Task.Delay(250);
            await vm.LoadLibraryAsync();

            await vm.PreviewLibrarySkinAsync(vm.LibrarySkins[0]);

            Assert.NotNull(vm.PreviewPng);
            Assert.Contains("3D 预览", vm.LibraryStatusText);
            Assert.Contains("才会写入账号", vm.StatusText);
        }

        private sealed class FakeLibrary : IMuaSkinLibrary
        {
            public Task<MuaSkinPage> ListAsync(
                string siteRoot, int page, CancellationToken cancellationToken)
            {
                MuaSkin skin = new MuaSkin(
                    42,
                    "测试皮肤",
                    "default",
                    "tester",
                    3,
                    "https://example.com/raw/42",
                    "https://example.com/avatar/42");

                return Task.FromResult(
                    new MuaSkinPage(new[] { skin }, 1, 1, 1));
            }

            public Task<string> DownloadAsync(
                MuaSkin skin, string targetPath, CancellationToken cancellationToken)
            {
                string source = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");

                File.Copy(source, targetPath, overwrite: true);
                return Task.FromResult(targetPath);
            }
        }
    }
}
