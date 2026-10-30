using System;
using System.Threading;
using System.Threading.Tasks;
using NLCL.Abstractions;

namespace NLCL.Core.Installing
{
    /// <summary>
    /// 按 <see cref="InstallRequest.Loader"/> 把请求分派给具体安装器。
    /// 组合根只需注册这一个 <see cref="IVersionInstaller"/>。
    /// </summary>
    public sealed class CompositeVersionInstaller : IVersionInstaller
    {
        private readonly IVersionInstaller _vanilla;
        private readonly IVersionInstaller _fabric;

        public CompositeVersionInstaller(IVersionInstaller vanilla, IVersionInstaller fabric)
        {
            _vanilla = vanilla ?? throw new ArgumentNullException(nameof(vanilla));
            _fabric = fabric ?? throw new ArgumentNullException(nameof(fabric));
        }

        public Task InstallAsync(
            InstallRequest request,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrEmpty(request.Loader))
            {
                return _vanilla.InstallAsync(request, progress, cancellationToken);
            }

            if (string.Equals(request.Loader, "fabric", StringComparison.OrdinalIgnoreCase))
            {
                return _fabric.InstallAsync(request, progress, cancellationToken);
            }

            throw new NotSupportedException(
                "暂不支持加载器：" + request.Loader + "（目前只有原版与 fabric）。");
        }
    }
}
