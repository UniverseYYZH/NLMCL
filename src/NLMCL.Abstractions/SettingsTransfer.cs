using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>设置导入结果。</summary>
    public sealed class SettingsImportResult
    {
        public SettingsImportResult(
            IReadOnlyList<string> appliedFields,
            IReadOnlyList<string> warnings)
        {
            AppliedFields = appliedFields;
            Warnings = warnings;
        }

        public IReadOnlyList<string> AppliedFields { get; }

        public IReadOnlyList<string> Warnings { get; }
    }

    /// <summary>可移植设置的导入导出。</summary>
    public interface ISettingsTransferService
    {
        Task ExportAsync(string filePath, CancellationToken cancellationToken);

        Task<SettingsImportResult> ImportAsync(string filePath, CancellationToken cancellationToken);
    }

    /// <summary>选择保存文件（导出设置用）。</summary>
    public interface IFileSavePicker
    {
        string? PickFile(string title, string filter, string defaultFileName);
    }
}
