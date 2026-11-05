using System;
using System.IO;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class ModsDirectoryTests
    {
        [Fact]
        public void Uses_the_resolver_answer_so_launcher_matches_the_game()
        {
            string minecraft = @"C:\mc";
            var resolver = new FixedResolver(@"C:\mc\versions\fabric-loader-1.21.1");

            string dir = MainViewModel.ResolveGameDirectory(
                resolver, minecraft, "fabric-loader-1.21.1");

            Assert.Equal(@"C:\mc\versions\fabric-loader-1.21.1", dir);
        }

        [Fact]
        public void Falls_back_to_version_folder_when_resolver_fails()
        {
            string dir = MainViewModel.ResolveGameDirectory(
                new ThrowingResolver(), @"C:\mc", "1.21.1");

            Assert.Equal(Path.Combine(@"C:\mc", "versions", "1.21.1"), dir);
        }

        private sealed class FixedResolver : IInstancePathResolver
        {
            private readonly string _path;

            public FixedResolver(string path)
            {
                _path = path;
            }

            public string GetGameDirectory(string minecraftDirectory, string versionId) => _path;
        }

        private sealed class ThrowingResolver : IInstancePathResolver
        {
            public string GetGameDirectory(string minecraftDirectory, string versionId)
                => throw new InvalidOperationException("boom");
        }
    }
}