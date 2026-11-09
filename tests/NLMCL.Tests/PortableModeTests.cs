using System;
using System.IO;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class PortableModeTests
    {
        [Fact]
        public void Normal_mode_keeps_appdata_nlmcl_layout()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-portable-normal-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(root);

            Assert.False(paths.IsPortable);
            Assert.Equal(Path.Combine(root, "NLMCL"), paths.DataRoot);
            Assert.Equal(Path.Combine(root, "NLMCL", "settings.json"), paths.SettingsFile);
            Assert.Equal(Path.Combine(root, ".minecraft"), paths.DefaultMinecraftDirectory);
        }

        [Fact]
        public void Portable_mode_uses_data_directory()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-portable-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(root, portable: true);

            Assert.True(paths.IsPortable);
            Assert.Equal(Path.Combine(root, "data"), paths.DataRoot);
            Assert.Equal(Path.Combine(root, "data", "settings.json"), paths.SettingsFile);
            Assert.Equal(Path.Combine(root, ".minecraft"), paths.DefaultMinecraftDirectory);

            paths.EnsureDirectories();
            Assert.True(Directory.Exists(Path.Combine(root, "data", "instances")));
        }

        [Fact]
        public void Portable_flag_is_detected_next_to_executable_directory()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-portable-flag-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            Assert.False(AppPaths.HasPortableFlag(root));

            File.WriteAllText(Path.Combine(root, AppPaths.PortableFlagName), string.Empty);

            Assert.True(AppPaths.HasPortableFlag(root));
        }

        [Fact]
        public void Default_constructor_detects_portable_flag_next_to_base_directory()
        {
            string flag = Path.Combine(AppContext.BaseDirectory, AppPaths.PortableFlagName);

            try
            {
                File.WriteAllText(flag, string.Empty);
                AppPaths paths = new AppPaths();

                Assert.True(paths.IsPortable);
                Assert.Equal(Path.Combine(AppContext.BaseDirectory, "data"), paths.DataRoot);
            }
            finally
            {
                if (File.Exists(flag))
                {
                    File.Delete(flag);
                }
            }
        }
    }
}
