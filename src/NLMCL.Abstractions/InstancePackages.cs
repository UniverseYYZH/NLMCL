using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 把完整实例目录打包为可分享的 zip，或从 zip 还原成一个新实例。
    /// 只复制实例目录本身，不涉及 libraries/assets 等共享内容。
    /// </summary>
    public interface IInstancePackageService
    {
        Task<string> ExportAsync(string instancePath, string destinationZip, CancellationToken cancellationToken = default);

        /// <summary>返回新实例目录的绝对路径。</summary>
        Task<string> ImportAsync(string packageZip, string versionsDirectory, CancellationToken cancellationToken = default);

        /// <summary>
        /// 把实例包内容原子替换回现有实例目录。失败时原实例保持不动。
        /// 仅用于本机备份回滚，不导入成新实例。
        /// </summary>
        Task RestoreAsync(string packageZip, string instanceDirectory, CancellationToken cancellationToken = default);
    }
}
