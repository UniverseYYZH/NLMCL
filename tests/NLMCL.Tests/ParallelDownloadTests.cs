using System;
using System.Collections.Generic;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>并行分块下载的分段计算（不依赖网络）。</summary>
    public class ParallelDownloadTests
    {
        [Fact]
        public void Splits_file_into_equal_chunks_covering_everything()
        {
            List<(long Start, long End)> chunks = HttpFileDownloader.BuildChunks(1000, 4);

            Assert.Equal(4, chunks.Count);
            Assert.Equal(0, chunks[0].Start);
            Assert.Equal(999, chunks[chunks.Count - 1].End);

            // 连续、无空洞、无重叠
            for (int i = 1; i < chunks.Count; i++)
            {
                Assert.Equal(chunks[i - 1].End + 1, chunks[i].Start);
            }
        }

        [Fact]
        public void Chunk_count_shrinks_for_tiny_files()
        {
            List<(long Start, long End)> chunks = HttpFileDownloader.BuildChunks(3, 8);

            Assert.Equal(3, chunks.Count);
            Assert.Equal((0L, 0L), chunks[0]);
        }

        [Fact]
        public void Zero_length_yields_no_chunks()
        {
            Assert.Empty(HttpFileDownloader.BuildChunks(0, 8));
        }

        [Fact]
        public void Last_chunk_ends_exactly_at_file_end()
        {
            // 1001 不能被 8 整除：确保最后一段不越界
            List<(long Start, long End)> chunks = HttpFileDownloader.BuildChunks(1001, 8);

            Assert.Equal(1000, chunks[chunks.Count - 1].End);
            Assert.Equal(8, chunks.Count);
        }
    }
}
