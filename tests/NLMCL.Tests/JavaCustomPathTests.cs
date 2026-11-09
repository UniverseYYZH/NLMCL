using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class JavaCustomPathTests
    {
        [Fact]
        public async Task Add_java_uses_picker_and_persists_path()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-java-custom-" + Guid.NewGuid().ToString("N"));
            string java = Path.Combine(root, "jdk-17", "bin", "javaw.exe");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(java)!);
                File.WriteAllBytes(java, Array.Empty<byte>());

                FixedPicker picker = new FixedPicker { Next = java };
                TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings();

                MainViewModel vm = TestSupport.CreateMain(
                    new TestSupport.EmptyCatalog(),
                    new AppPaths(root),
                    appSettings: settings,
                    filePicker: picker);

                await vm.AddJavaAsync();

                Assert.Equal(java, vm.DefaultJavaPath);
                Assert.Contains(vm.JavaOptions, option => option.Path == java);
                Assert.Equal(java, settings.Current.DefaultJavaPath);

                // 重新探测后仍保留手动添加的 Java。
                await vm.RefreshJavaAsync();

                Assert.Contains(vm.JavaOptions, option => option.Path == java);
                Assert.Equal(java, vm.SelectedJava!.Path);
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
        public async Task Add_java_rejects_missing_file()
        {
            FixedPicker picker = new FixedPicker
            {
                Next = Path.Combine(Path.GetTempPath(), "missing-java-" + Guid.NewGuid().ToString("N"), "javaw.exe")
            };

            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new AppPaths(Path.GetTempPath()),
                filePicker: picker);

            await vm.AddJavaAsync();

            Assert.Contains("不存在", vm.JavaStatus);
            Assert.DoesNotContain(vm.JavaOptions, option =>
                option.Path != null && option.Path.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase)
                && option.Path.Contains("missing-java-"));
        }

        private sealed class FixedPicker : IFilePicker
        {
            public string? Next { get; set; }

            public string? PickFile(string title, string filter) => Next;
        }
    }
}
