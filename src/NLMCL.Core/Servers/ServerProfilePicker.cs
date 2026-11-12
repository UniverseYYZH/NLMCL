using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Servers
{
    /// <summary>挑出来的一个可用入口。</summary>
    public sealed class ProfileEndpoint
    {
        public ProfileEndpoint(string address, ServerEndpoint endpoint)
        {
            Address = address;
            Endpoint = endpoint;
        }

        /// <summary>档案里写的原始地址（用于界面展示）。</summary>
        public string Address { get; }

        /// <summary>解析后实际要连的端点。</summary>
        public ServerEndpoint Endpoint { get; }

        public bool Online { get; set; }

        public int Players { get; set; }

        public int MaxPlayers { get; set; }

        public int LatencyMs { get; set; }

        public string VersionText { get; set; } = string.Empty;

        public string? Error { get; set; }

        public override string ToString() => Address + " → " + Endpoint;
    }

    /// <summary>
    /// 从档案的多个地址里挑一个能连的。
    ///
    /// 宝可梦服用 mc2 / mc4 两个域名分流（同一台服务器，两个入口），
    /// 启动器应当：先解析 SRV，再 SLP 探测，选"人少、延迟低"的那个；
    /// 全都不可用时返回第一个并带上原因，让界面给出准确提示。
    /// </summary>
    public sealed class ServerProfilePicker
    {
        private readonly IServerAddressResolver _resolver;
        private readonly IServerPinger _pinger;
        private readonly TimeSpan _totalTimeout;

        public ServerProfilePicker(
            IServerAddressResolver resolver,
            IServerPinger pinger,
            TimeSpan? totalTimeout = null)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _pinger = pinger ?? throw new ArgumentNullException(nameof(pinger));
            _totalTimeout = totalTimeout ?? TimeSpan.FromSeconds(4);
        }

        public async Task<ProfileEndpoint> PickAsync(
            ServerProfile profile, CancellationToken cancellationToken)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            List<string> addresses = profile.Addresses.ToList();

            if (addresses.Count == 0)
            {
                throw new InvalidOperationException("服务器档案里没有地址。");
            }

            // 档案里的入口可能很多，必须并发探测；否则一个不响应的域名
            // 就会把后面的入口全部拖住。
            Task<ProfileEndpoint>[] probes = addresses
                .Select(address => ProbeAsync(address, cancellationToken))
                .ToArray();

            Task all = Task.WhenAll(probes);
            TimeSpan timeout = _totalTimeout <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(4)
                : _totalTimeout;

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource())
            {
                Task delay = Task.Delay(timeout, timeoutCts.Token);
                Task finished = await Task.WhenAny(all, delay).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (finished != all)
                {

                    // 到总 deadline 还没完成的入口留下明确原因，
                    // 已完成的入口仍然参与挑优。
                    for (int i = 0; i < probes.Length; i++)
                    {
                        if (!probes[i].IsCompleted)
                        {
                            _ = probes[i].ContinueWith(
                                task => _ = task.Exception,
                                CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted,
                                TaskScheduler.Default);
                        }
                    }
                }

                timeoutCts.Cancel();

                List<ProfileEndpoint> candidates = new List<ProfileEndpoint>(probes.Length);

                for (int i = 0; i < probes.Length; i++)
                {
                    if (probes[i].Status == TaskStatus.RanToCompletion)
                    {
                        candidates.Add(probes[i].Result);
                    }
                    else
                    {
                        candidates.Add(new ProfileEndpoint(
                            addresses[i],
                            new ServerEndpoint(addresses[i], 0, false))
                        {
                            Error = "探测超时（" + timeout.TotalSeconds.ToString("0.#") + " 秒）"
                        });
                    }
                }

                return Choose(candidates);
            }
        }

        private async Task<ProfileEndpoint> ProbeAsync(string address, CancellationToken cancellationToken)
        {
            if (!ServerBookmark.TryParse(address, out string host, out int port, out string? parseError))
            {
                return new ProfileEndpoint(address, new ServerEndpoint(address, 0, false))
                {
                    Error = parseError
                };
            }

            ProfileEndpoint candidate;

            try
            {
                ServerEndpoint endpoint = await _resolver
                    .ResolveAsync(host, port, cancellationToken).ConfigureAwait(false);

                candidate = new ProfileEndpoint(address, endpoint);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                return new ProfileEndpoint(address, new ServerEndpoint(host, port, false))
                {
                    Error = ex.Message
                };
            }

            try
            {
                LanRoom room = await _pinger
                    .PingAsync(candidate.Endpoint.Host, candidate.Endpoint.Port, cancellationToken)
                    .ConfigureAwait(false);

                candidate.Online = room.Pinged;
                candidate.Players = room.Online;
                candidate.MaxPlayers = room.Max;
                candidate.LatencyMs = room.LatencyMs;
                candidate.VersionText = room.VersionName ?? string.Empty;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                candidate.Error = ex.Message;
            }

            return candidate;
        }

        private static ProfileEndpoint Choose(List<ProfileEndpoint> candidates)
        {
            ProfileEndpoint? best = null;

            foreach (ProfileEndpoint candidate in candidates)
            {
                if (!candidate.Online)
                {
                    continue;
                }

                if (best == null
                    || candidate.LatencyMs < best.LatencyMs
                    || (candidate.LatencyMs == best.LatencyMs && candidate.Players < best.Players))
                {
                    best = candidate;
                }
            }

            return best ?? candidates[0];
        }
    }
}
