using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class DownloadJobStoreTests
    {
        [Fact]
        public async Task Saves_loads_and_clears_a_job()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-job-");
            try
            {
                JsonDownloadJobStore store = new JsonDownloadJobStore(new AppPaths(root));
                Assert.Null(await store.LoadAsync(CancellationToken.None));

                await store.SaveAsync(new DownloadJob
                {
                    VersionId = "1.21.1",
                    Loader = "Fabric",
                    LoaderVersion = "0.16.9",
                    TargetDirectory = @"D:\Games\.minecraft",
                    Parallelism = 6
                }, CancellationToken.None);

                DownloadJob? job = await store.LoadAsync(CancellationToken.None);
                Assert.NotNull(job);
                Assert.Equal("1.21.1", job!.VersionId);
                Assert.Equal("Fabric", job.Loader);
                Assert.Equal("0.16.9", job.LoaderVersion);
                Assert.Equal(6, job.Parallelism);

                await store.ClearAsync(CancellationToken.None);
                Assert.Null(await store.LoadAsync(CancellationToken.None));
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
    }
}
