using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Abstractions
{
    /// <summary>版本修复结果。</summary>
    public sealed class RepairReport
    {
        public RepairReport(int missing, int repaired, int failed, IReadOnlyList<string> details)
        {
            Missing = missing;
            Repaired = repaired;
            Failed = failed;
            Details = details ?? new List<string>();
        }

        public int Missing { get; }

        public int Repaired { get; }

        public int Failed { get; }

        public IReadOnlyList<string> Details { get; }

        public string Summary => Missing == 0
            ? "文件完整，无需修复。"
            : "缺失 " + Missing + " 个文件，已修复 " + Repaired + " 个"
              + (Failed > 0 ? "，失败 " + Failed + " 个" : "。");
    }

    /// <summary>
    /// 版本修复：沿 <c>inheritsFrom</c> 链收集库与客户端 JAR，缺哪个补哪个。
    ///
    /// 不处理资源对象（assets/objects）——缺资源只影响声音/语言，不影响进游戏，
    /// 且动辄上万个文件，放进"修复"会让人以为卡死。
    /// </summary>
    public interface IVersionRepairer
    {
        Task<RepairReport> InspectAsync(
            string minecraftDirectory, string instanceFolder, CancellationToken cancellationToken);

        Task<RepairReport> RepairAsync(
            string minecraftDirectory, string instanceFolder,
            IProgress<InstallProgress>? progress, CancellationToken cancellationToken);
    }
}
