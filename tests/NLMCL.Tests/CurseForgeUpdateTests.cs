using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Mods;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class CurseForgeUpdateTests
    {
        [Fact]
        public void Fingerprint_ignores_whitespace()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-fp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                string a = Path.Combine(root, "a.jar");
                string b = Path.Combine(root, "b.jar");
                File.WriteAllBytes(a, new byte[] { 1, 2, 3, 4, 5 });
                File.WriteAllBytes(b, new byte[] { 1, 9, 2, 10, 3, 13, 4, 32, 5 });

                Assert.Equal(
                    CurseForgeFingerprint.Compute(a),
                    CurseForgeFingerprint.Compute(b));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Streaming_fingerprint_matches_reference()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-fp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                byte[] data = new byte[] { 7, 11, 17, 19, 23, 29, 31, 37, 41, 43 };
                string file = Path.Combine(root, "a.jar");
                File.WriteAllBytes(file, data);

                Assert.Equal(CurseForgeFingerprint.Compute(data), CurseForgeFingerprint.Compute(file));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Finds_latest_curseforge_file_by_fingerprint()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-cf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                string file = Path.Combine(root, "example.jar");
                File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4 });

                ModEntry entry = new ModEntry(file, "example.jar", 4)
                {
                    Name = "Example",
                    Version = "1.0.0",
                    Loader = "fabric"
                };

                FakeDownload download = new FakeDownload
                {
                    PostResponse = @"{ ""data"": { ""exactMatches"": [ { ""id"": 123, ""file"": { ""id"": 10, ""fileFingerprint"": 1 } } ] } }",
                    GetResponse = @"{
                        ""data"": {
                            ""latestFilesIndexes"": [
                                { ""fileId"": 11, ""filename"": ""example-2.0.0.jar"", ""gameVersion"": ""1.20.1"", ""modLoader"": 4 }
                            ]
                        }
                    }"
                };

                CurseForgeUpdateChecker checker = new CurseForgeUpdateChecker(download);
                ModUpdateInfo? update = await checker.CheckAsync(entry, "1.20.1", "fabric", CancellationToken.None);

                Assert.NotNull(update);
                Assert.Equal("example-2.0.0.jar", update!.LatestVersion);
                Assert.Contains("curseforge.com/projects/123", update.ProjectUrl);

                entry.LatestVersion = update.LatestVersion;
                entry.ProjectUrl = update.ProjectUrl;
                Assert.True(entry.HasUpdate);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private sealed class FakeDownload : IDownloadService
        {
            public string PostResponse { get; set; } = "{}";
            public string GetResponse { get; set; } = "{}";

            public Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult(GetResponse);

            public Task<string> GetStringAsync(
                string url,
                IReadOnlyDictionary<string, string>? headers,
                CancellationToken cancellationToken)
                => Task.FromResult(GetResponse);

            public Task<string> PostJsonAsync(
                string url,
                string jsonBody,
                IReadOnlyDictionary<string, string>? headers,
                CancellationToken cancellationToken)
                => Task.FromResult(PostResponse);

            public Task<string> PostFormAsync(
                string url,
                IReadOnlyDictionary<string, string> fields,
                CancellationToken cancellationToken)
                => Task.FromResult("{}");
        }
    }
}
