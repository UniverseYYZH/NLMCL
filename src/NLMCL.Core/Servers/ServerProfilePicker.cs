using System;
using System.Collections.Generic;
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

        public ServerProfilePicker(IServerAddressResolver resolver, IServerPinger pinger)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _pinger = pinger ?? throw new ArgumentNullException(nameof(pinger));
        }

        public async Task<ProfileEndpoint> PickAsync(
            ServerProfile profile, CancellationToken cancellationToken)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            List<ProfileEndpoint> candidates = new List<ProfileEndpoint>();

            foreach (string address in profile.Addresses)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!ServerBookmark.TryParse(address, out string host, out int port, out string? parseError))
                {
                    candidates.Add(new ProfileEndpoint(address, new ServerEndpoint(address, 0, false))
                    {
                        Error = parseError
                    });

                    continue;
                }

                ProfileEndpoint candidate;

                try
                {
                    ServerEndpoint endpoint = await _resolver
                        .ResolveAsync(host, port, cancellationToken).ConfigureAwait(false);

                    candidate = new ProfileEndpoint(address, endpoint);
                }
                catch (Exception ex)
                {
                    candidates.Add(new ProfileEndpoint(address, new ServerEndpoint(host, port, false))
                    {
                        Error = ex.Message
                    });

                    continue;
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
                catch (Exception ex)
                {
                    candidate.Error = ex.Message;
                }

                candidates.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("服务器档案里没有地址。");
            }

            ProfileEndpoint? best = null;

            foreach (ProfileEndpoint candidate in candidates)
            {
                if (!candidate.Online)
                {
                    continue;
                }

                if (best == null
                    || candidate.Players < best.Players
                    || (candidate.Players == best.Players && candidate.LatencyMs < best.LatencyMs))
                {
                    best = candidate;
                }
            }

            return best ?? candidates[0];
        }
    }
}
