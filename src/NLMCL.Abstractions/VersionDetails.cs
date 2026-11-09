using System;

namespace NLMCL.Abstractions
{
    /// <summary>版本详情：只读数据，供本地版本页的详情浮层展示。</summary>
    public sealed class VersionDetailsData
    {
        public string VersionJsonPath { get; set; } = string.Empty;

        public bool VersionJsonExists { get; set; }

        public string ClientJarPath { get; set; } = string.Empty;

        public bool ClientJarExists { get; set; }

        public long ClientJarSize { get; set; }

        public string? VersionId { get; set; }

        public string? MainClass { get; set; }

        public string? Assets { get; set; }

        public string? InheritsFrom { get; set; }

        public int? JavaMajor { get; set; }

        public int LibraryCount { get; set; }

        public int ModCount { get; set; }

        public string? Error { get; set; }
    }

    /// <summary>读取某个本地版本的详情数据。</summary>
    public interface IVersionDetailsService
    {
        VersionDetailsData Read(string gameDirectory, string folderName);
    }
}
