using System;
using System.IO;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class AppPathsFallbackTests
    {
        [Fact]
        public void Falls_back_to_exe_directory_when_appdata_is_not_writable()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-paths-fallback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string preferred = Path.Combine(root, "appdata", "NLMCL");
            Directory.CreateDirectory(Path.GetDirectoryName(preferred)!);
            File.WriteAllText(preferred, "not a directory");

            string fallback = Path.Combine(root, "exe", "NLMCL-data");
            string local = Path.Combine(root, "local", "NLMCL");
            string temp = Path.Combine(root, "temp", "NLMCL");

            try
            {
                string selected = AppPaths.SelectDataRoot(
                    preferred,
                    fallback,
                    local,
                    temp,
                    out bool isFallback);

                Assert.Equal(fallback, selected);
                Assert.True(isFallback);
                Assert.True(File.Exists(Path.Combine(fallback, AppPaths.FallbackMarkerName)));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void Existing_fallback_marker_wins_over_writable_appdata()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-paths-marker-" + Guid.NewGuid().ToString("N"));
            string preferred = Path.Combine(root, "appdata", "NLMCL");
            string fallback = Path.Combine(root, "exe", "NLMCL-data");
            Directory.CreateDirectory(preferred);
            Directory.CreateDirectory(fallback);
            File.WriteAllText(Path.Combine(fallback, AppPaths.FallbackMarkerName), "marked");

            try
            {
                string selected = AppPaths.SelectDataRoot(
                    preferred,
                    fallback,
                    Path.Combine(root, "local", "NLMCL"),
                    Path.Combine(root, "temp", "NLMCL"),
                    out bool isFallback);

                Assert.Equal(fallback, selected);
                Assert.True(isFallback);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void Build_output_fallback_is_not_inside_bin()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-paths-build-" + Guid.NewGuid().ToString("N"));
            string output = Path.Combine(root, "src", "NLMCL.App", "bin", "Debug", "net48");
            Directory.CreateDirectory(output);

            try
            {
                string fallback = AppPaths.FindStableFallbackDirectory(output);

                Assert.Equal(
                    Path.Combine(root, "src", "NLMCL.App", "NLMCL-data"),
                    fallback);
                Assert.DoesNotContain(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, fallback);
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static void TryDelete(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
