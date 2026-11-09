using System;
using System.Collections.Generic;
using System.IO;

namespace NLMCL.Core.Mods
{
    /// <summary>
    /// CurseForge 文件指纹（MurmurHash2，过滤 TAB/LF/CR/空格）。
    /// 只用于更新检查；不写回 PCL Vendor。
    /// </summary>
    public static class CurseForgeFingerprint
    {
        private const uint M = 0x5bd1e995;
        private const int BufferSize = 64 * 1024;

        public static uint Compute(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("文件路径不能为空。", nameof(path));
            }

            using (FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            {
                // 两遍读取：第一遍只统计过滤后的长度，第二遍才算 hash。
                // 这样大 jar 不会像旧实现那样一次性 ReadAllBytes + List 复制，峰值内存减半。
                int length = CountFiltered(stream);
                stream.Position = 0;

                uint h = 1u ^ (uint)length;
                byte[] buffer = new byte[BufferSize];
                byte[] block = new byte[4];
                int filled = 0;
                int read;

                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                    {
                        byte value = buffer[i];

                        if (value == 9 || value == 10 || value == 13 || value == 32)
                        {
                            continue;
                        }

                        block[filled++] = value;

                        if (filled == 4)
                        {
                            MixBlock(ref h, block);
                            filled = 0;
                        }
                    }
                }

                unchecked
                {
                    switch (filled)
                    {
                        case 3:
                            h ^= (uint)(block[0] | block[1] << 8 | block[2] << 16);
                            h *= M;
                            break;
                        case 2:
                            h ^= (uint)(block[0] | block[1] << 8);
                            h *= M;
                            break;
                        case 1:
                            h ^= block[0];
                            h *= M;
                            break;
                    }

                    h ^= h >> 13;
                    h *= M;
                    h ^= h >> 15;
                }

                return h;
            }
        }

        internal static uint Compute(IReadOnlyList<byte> data)
        {
            int length = data.Count;
            uint h = 1u ^ (uint)length;
            int i = 0;

            unchecked
            {
                while (length - i >= 4)
                {
                    uint k = (uint)(data[i]
                        | data[i + 1] << 8
                        | data[i + 2] << 16
                        | data[i + 3] << 24);

                    Mix(ref h, k);
                    i += 4;
                }

                switch (length - i)
                {
                    case 3:
                        h ^= (uint)(data[i] | data[i + 1] << 8 | data[i + 2] << 16);
                        h *= M;
                        break;
                    case 2:
                        h ^= (uint)(data[i] | data[i + 1] << 8);
                        h *= M;
                        break;
                    case 1:
                        h ^= data[i];
                        h *= M;
                        break;
                }

                h ^= h >> 13;
                h *= M;
                h ^= h >> 15;
            }

            return h;
        }

        private static int CountFiltered(FileStream stream)
        {
            byte[] buffer = new byte[BufferSize];
            int count = 0;
            int read;

            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    byte value = buffer[i];

                    if (value != 9 && value != 10 && value != 13 && value != 32)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void MixBlock(ref uint h, byte[] block)
        {
            uint k = (uint)(block[0] | block[1] << 8 | block[2] << 16 | block[3] << 24);
            Mix(ref h, k);
        }

        private static void Mix(ref uint h, uint k)
        {
            unchecked
            {
                k *= M;
                k ^= k >> 24;
                k *= M;
                h *= M;
                h ^= k;
            }
        }
    }
}
