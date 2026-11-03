using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 把中文查询翻成英文。
    ///
    /// Modrinth 只有英文元数据，中文名直接搜是 0 结果。除了内置别名表，
    /// 再用一个翻译接口兜底（离线/被墙时返回 null，不影响原搜索）。
    /// </summary>
    public interface ITranslator
    {
        /// <summary>翻译成英文；失败或没有变化时返回 null。</summary>
        Task<string?> TranslateToEnglishAsync(string text, CancellationToken cancellationToken);
    }
}
