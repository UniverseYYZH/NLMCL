using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Servers
{
    /// <summary>
    /// 用 DNS 查 <c>_minecraft._tcp.&lt;域名&gt;</c> 的 SRV 记录，得到真实端口。
    ///
    /// 为什么自己拼 DNS 报文：.NET Framework 没有托管 SRV 查询 API。
    /// 直接 P/Invoke <c>DnsQuery_W</c> 要手工解 DNS_RECORD 链表，很容易出错；
    /// 而 SRV 查询只需要一次 UDP 往返，自己构造报文反而更可控。
    ///
    /// 失败一律回落到「原样返回」，绝不因为 DNS 挂了影响连接。
    /// </summary>
    public sealed class SrvResolver : IServerAddressResolver
    {
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);

        private readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new object();

        public async Task<ServerEndpoint> ResolveAsync(
            string host, int port, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(host) || IPAddress.TryParse(host, out _))
            {
                // IP 字面量没有 SRV 可查
                return new ServerEndpoint(host, port, fromSrv: false);
            }

            // 用户显式写了端口：尊重用户，不再用 SRV 覆盖
            if (port != 25565)
            {
                return new ServerEndpoint(host, port, fromSrv: false);
            }

            string cacheKey = host.Trim().TrimEnd('.');

            lock (_gate)
            {
                if (_cache.TryGetValue(cacheKey, out CacheEntry cached)
                    && cached.Expires > DateTime.UtcNow)
                {
                    return cached.Endpoint;
                }
            }

            string query = "_minecraft._tcp." + cacheKey;

            IReadOnlyList<SrvRecord> records = await QueryAsync(query, cancellationToken)
                .ConfigureAwait(false);

            ServerEndpoint endpoint = records.Count == 0
                ? new ServerEndpoint(cacheKey, port, fromSrv: false)
                : new ServerEndpoint(records[0].Target, records[0].Port, fromSrv: true);

            lock (_gate)
            {
                _cache[cacheKey] = new CacheEntry(endpoint, DateTime.UtcNow + CacheLifetime);
            }

            return endpoint;
        }

        private sealed class CacheEntry
        {
            public CacheEntry(ServerEndpoint endpoint, DateTime expires)
            {
                Endpoint = endpoint;
                Expires = expires;
            }

            public ServerEndpoint Endpoint { get; }

            public DateTime Expires { get; }
        }

        /// <summary>一条 SRV 记录。</summary>
        internal sealed class SrvRecord
        {
            public SrvRecord(int priority, int weight, int port, string target)
            {
                Priority = priority;
                Weight = weight;
                Port = port;
                Target = target;
            }

            public int Priority { get; }

            public int Weight { get; }

            public int Port { get; }

            public string Target { get; }
        }

        private async Task<IReadOnlyList<SrvRecord>> QueryAsync(
            string query, CancellationToken cancellationToken)
        {
            IPAddress? resolver = FindDnsServer();

            if (resolver == null)
            {
                return Array.Empty<SrvRecord>();
            }

            try
            {
                byte[] request = BuildQuery(query, new Random().Next(1, ushort.MaxValue));

                using (UdpClient client = new UdpClient(resolver.AddressFamily))
                {
                    client.Client.ReceiveTimeout = (int)QueryTimeout.TotalMilliseconds;
                    client.Connect(resolver, 53);

                    await client.SendAsync(request, request.Length).ConfigureAwait(false);

                    Task<UdpReceiveResult> receive = client.ReceiveAsync();

                    Task finished = await Task.WhenAny(
                        receive, Task.Delay(QueryTimeout, cancellationToken)).ConfigureAwait(false);

                    if (finished != receive)
                    {
                        return Array.Empty<SrvRecord>();
                    }

                    UdpReceiveResult result = await receive.ConfigureAwait(false);

                    return ParseResponse(result.Buffer);
                }
            }
            catch (SocketException)
            {
                return Array.Empty<SrvRecord>();
            }
            catch (ObjectDisposedException)
            {
                return Array.Empty<SrvRecord>();
            }
            catch (ArgumentException)
            {
                return Array.Empty<SrvRecord>();
            }
        }

        /// <summary>取第一个可用的 IPv4 DNS 服务器。</summary>
        private static IPAddress? FindDnsServer()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(nic => nic.GetIPProperties().DnsAddresses)
                    .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            }
            catch (NetworkInformationException)
            {
                return null;
            }
        }

        // ---------- DNS 报文 ----------

        internal static byte[] BuildQuery(string name, int id)
        {
            List<byte> packet = new List<byte>(64);

            packet.Add((byte)(id >> 8));
            packet.Add((byte)(id & 0xFF));
            packet.Add(0x01); // RD（递归）
            packet.Add(0x00);
            packet.Add(0x00); // QDCOUNT = 1
            packet.Add(0x01);
            packet.Add(0x00); // ANCOUNT = 0
            packet.Add(0x00);
            packet.Add(0x00); // NSCOUNT
            packet.Add(0x00);
            packet.Add(0x00); // ARCOUNT
            packet.Add(0x00);

            foreach (string label in name.TrimEnd('.').Split('.'))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(label);
                packet.Add((byte)bytes.Length);
                packet.AddRange(bytes);
            }

            packet.Add(0x00); // 名字结束
            packet.Add(0x00); // QTYPE = SRV (33)
            packet.Add(0x21);
            packet.Add(0x00); // QCLASS = IN
            packet.Add(0x01);

            return packet.ToArray();
        }

        /// <summary>解析响应，只取 SRV（33）记录；解析不了的行直接跳过。</summary>
        internal static IReadOnlyList<SrvRecord> ParseResponse(byte[] buffer)
        {
            List<SrvRecord> records = new List<SrvRecord>();

            if (buffer == null || buffer.Length < 12)
            {
                return records;
            }

            int questions = (buffer[4] << 8) | buffer[5];
            int answers = (buffer[6] << 8) | buffer[7];

            int offset = 12;

            for (int i = 0; i < questions; i++)
            {
                // 跳过 QNAME + QTYPE + QCLASS
                if (!SkipName(buffer, ref offset))
                {
                    return records;
                }

                offset += 4;

                if (offset > buffer.Length)
                {
                    return records;
                }
            }

            for (int i = 0; i < answers; i++)
            {
                if (!SkipName(buffer, ref offset) || offset + 10 > buffer.Length)
                {
                    break;
                }

                int type = (buffer[offset] << 8) | buffer[offset + 1];
                int dataLength = (buffer[offset + 8] << 8) | buffer[offset + 9];

                offset += 10;

                if (offset + dataLength > buffer.Length)
                {
                    break;
                }

                if (type == 33 && dataLength >= 7)
                {
                    int priority = (buffer[offset] << 8) | buffer[offset + 1];
                    int weight = (buffer[offset + 2] << 8) | buffer[offset + 3];
                    int port = (buffer[offset + 4] << 8) | buffer[offset + 5];

                    int targetOffset = offset + 6;
                    string target = ReadName(buffer, ref targetOffset);

                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        records.Add(new SrvRecord(priority, weight, port, target));
                    }
                }

                offset += dataLength;
            }

            // 先按优先级升序，再按权重降序
            return records
                .OrderBy(record => record.Priority)
                .ThenByDescending(record => record.Weight)
                .ToList();
        }

        /// <summary>跳过一个（可能带压缩指针的）域名。</summary>
        private static bool SkipName(byte[] buffer, ref int offset)
        {
            int guard = 0;

            while (offset < buffer.Length)
            {
                byte length = buffer[offset];

                if (length == 0)
                {
                    offset += 1;
                    return true;
                }

                if ((length & 0xC0) == 0xC0)
                {
                    offset += 2;
                    return offset <= buffer.Length;
                }

                offset += 1 + length;

                if (++guard > 128)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>读一个域名（支持压缩指针）。</summary>
        private static string ReadName(byte[] buffer, ref int offset)
        {
            StringBuilder builder = new StringBuilder();
            HashSet<int> visited = new HashSet<int>();
            int guard = 0;
            int jumps = 0;

            while (offset < buffer.Length)
            {
                byte length = buffer[offset];

                if (length == 0)
                {
                    offset += 1;
                    break;
                }

                if ((length & 0xC0) == 0xC0)
                {
                    if (offset + 1 >= buffer.Length)
                    {
                        break;
                    }

                    int pointer = ((length & 0x3F) << 8) | buffer[offset + 1];

                    // 防环：畸形/恶意响应里的指针可以指回自己，必须记录已访问位置。
                    if (!visited.Add(pointer) || ++jumps > 64)
                    {
                        break;
                    }

                    offset = pointer;
                    continue;
                }

                offset += 1;

                if (offset + length > buffer.Length)
                {
                    break;
                }

                if (builder.Length > 0)
                {
                    builder.Append('.');
                }

                builder.Append(Encoding.ASCII.GetString(buffer, offset, length));
                offset += length;

                if (++guard > 128)
                {
                    break;
                }
            }

            return builder.ToString();
        }
    }
}
