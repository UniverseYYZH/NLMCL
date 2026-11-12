using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>实例目录里玩家可管理的三类内容。</summary>
    public enum InstanceContentKind
    {
        Saves = 0,
        ResourcePacks = 1,
        ShaderPacks = 2
    }

    /// <summary>
    /// 管理实例的 saves / resourcepacks / shaderpacks：
    /// 统计数量、导入存档压缩包或资源包文件。只操作当前 .minecraft，不碰共享资源。
    /// </summary>
    public interface IInstanceContentService
    {
        string GetDirectory(string gameDirectory, InstanceContentKind kind);

        int Count(string gameDirectory, InstanceContentKind kind);

        /// <summary>导入成功时返回落盘路径。</summary>
        Task<string> ImportAsync(
            string gameDirectory,
            InstanceContentKind kind,
            string sourcePath,
            CancellationToken cancellationToken = default);
    }
}
