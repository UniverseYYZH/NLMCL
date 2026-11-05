using System.Collections.Generic;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class HttpDownloaderChunkTests
    {
        [Theory]
        [InlineData(0, 6)]
        [InlineData(10L * 1024 * 1024, 6)]          // 10 MB：按最小 6 块
        [InlineData(93L * 1024 * 1024, 16)]         // 93 MB：封顶 16 块
        [InlineData(24L * 1024 * 1024, 6)]          // 24 MB：24/4 = 6
        public void Chunk_count_scales_with_size(long size, int expected)
        {
            Assert.Equal(expected, HttpFileDownloader.ChunkCountFor(size));
        }

        [Fact]
        public void Chunks_cover_whole_file_without_gaps()
        {
            List<(long Start, long End)> chunks = HttpFileDownloader.BuildChunks(1000, 7);
            long next = 0;

            foreach ((long start, long end) in chunks)
            {
                Assert.Equal(next, start);
                Assert.True(end >= start);
                next = end + 1;
            }

            Assert.Equal(1000, next);
        }
    }
}