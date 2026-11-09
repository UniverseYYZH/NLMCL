namespace NLMCL.Abstractions
{
    /// <summary>下载失败重试策略（当前下载栈的固定行为，仅用于展示与复用常量）。</summary>
    public static class DownloadRetryPolicy
    {
        public const int Attempts = 3;

        public const int FileTimeoutSeconds = 300;

        public static string Describe()
        {
            return "ModNet 每文件失败后最多重试 " + Attempts +
                   " 次；仍失败时用流式备用下载兜底，单文件超时 " +
                   FileTimeoutSeconds + " 秒。";
        }
    }
}
