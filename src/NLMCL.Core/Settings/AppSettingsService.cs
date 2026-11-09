using System;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Settings
{
    /// <summary>
    /// 应用设置的单入口。所有读写都过同一把锁，保证「读—改—写」不互相覆盖。
    /// </summary>
    public sealed class AppSettingsService : IAppSettingsService
    {
        private readonly IAppSettingsStore _store;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public AppSettingsService(IAppSettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken)
        {
            if (mutate == null)
            {
                throw new ArgumentNullException(nameof(mutate));
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                AppSettings settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                mutate(settings);
                await _store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await _store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
