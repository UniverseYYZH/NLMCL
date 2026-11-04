using System;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>实例（版本目录）的重命名 / 复制 / 删除。</summary>
    public interface IVersionManager
    {
        /// <summary>重命名实例目录与 JSON 里的 id。</summary>
        Task RenameAsync(
            string minecraftDirectory, string folder, string newName, CancellationToken cancellationToken);

        /// <summary>复制出一个新实例（新名字不能已存在），返回新目录名。</summary>
        Task<string> CopyAsync(
            string minecraftDirectory, string folder, string newName, CancellationToken cancellationToken);

        /// <summary>删除整个实例目录（不可恢复）。</summary>
        Task DeleteAsync(string minecraftDirectory, string folder, CancellationToken cancellationToken);

        /// <summary>名字是否可用：非空、无非法字符、不与现有实例冲突。</summary>
        bool IsNameAvailable(string minecraftDirectory, string name, string? ignoreFolder = null);
    }
}
