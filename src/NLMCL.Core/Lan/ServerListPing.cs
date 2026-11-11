using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Lan
{
    /// <summary>
    /// Server List Ping（协议 1.7+）：
    /// 握手（0x00, protocol, host, port, nextState=1）→ 请求（0x00）→ 读状态 JSON。
    ///
    /// 协议号用 -1 也行（服务端一般不校验），这里用 767（1.21）并允许失败后重试一次。
    /// </summary>
    public sealed class ServerListPing : IServerPinger
    {
        private const int DefaultProtocol = 767;

        public async Task<LanRoom> PingAsync(string host, int port, CancellationToken cancellationToken)
        {
            LanRoom room = new LanRoom(host, port);

            Stopwatch watch = Stopwatch.StartNew();

            using (TcpClient client = new TcpClient())
            {
                Task connect = client.ConnectAsync(host, port);

                Task completed = await Task.WhenAny(
                    connect, Task.Delay(TimeSpan.FromSeconds(3), cancellationToken)).ConfigureAwait(false);

                if (completed != connect)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("连接超时：" + host + ":" + port);
                }

                await connect.ConfigureAwait(false);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 3000;
                    stream.WriteTimeout = 3000;

                    byte[] handshake = BuildHandshake(host, port, DefaultProtocol);
                    await stream.WriteAsync(handshake, 0, handshake.Length, cancellationToken).ConfigureAwait(false);

                    byte[] request = BuildStatusRequest();
                    await stream.WriteAsync(request, 0, request.Length, cancellationToken).ConfigureAwait(false);

                    string json;
                    using (CancellationTokenSource readTimeout =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        readTimeout.CancelAfter(TimeSpan.FromSeconds(3));

                        try
                        {
                            json = await ReadStatusAsync(stream, readTimeout.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            throw new TimeoutException(
                                "服务器接受了连接，但 3 秒内没有返回状态：" + host + ":" + port);
                        }
                    }

                    watch.Stop();

                    ApplyStatus(room, json);
                    room.LatencyMs = (int)watch.ElapsedMilliseconds;
                    room.Pinged = true;
                }
            }

            return room;
        }

        /// <summary>把状态 JSON 里的版本/人数/MOTD 填进房间对象。</summary>
        internal static void ApplyStatus(LanRoom room, string json)
        {
            JObject root = JObject.Parse(json);

            room.VersionName = root.SelectToken("version.name")?.Value<string>() ?? string.Empty;
            room.Online = root.SelectToken("players.online")?.Value<int>() ?? 0;
            room.Max = root.SelectToken("players.max")?.Value<int>() ?? 0;

            string motd = FlattenDescription(root["description"]);

            if (!string.IsNullOrWhiteSpace(motd))
            {
                room.Motd = motd;
            }
        }

        /// <summary>description 可能是字符串、{text}、或 {extra:[{text}...]}。</summary>
        internal static string FlattenDescription(JToken? token)
        {
            if (token == null)
            {
                return string.Empty;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>() ?? string.Empty;
            }

            if (token is JObject obj)
            {
                StringBuilder builder = new StringBuilder();
                builder.Append(obj.Value<string>("text") ?? string.Empty);

                if (obj["extra"] is JArray extra)
                {
                    foreach (JToken part in extra)
                    {
                        builder.Append(FlattenDescription(part));
                    }
                }

                return builder.ToString();
            }

            if (token is JArray array)
            {
                StringBuilder builder = new StringBuilder();

                foreach (JToken part in array)
                {
                    builder.Append(FlattenDescription(part));
                }

                return builder.ToString();
            }

            return token.ToString();
        }

        internal static byte[] BuildHandshake(string host, int port, int protocol)
        {
            using (MemoryStream payload = new MemoryStream())
            {
                WriteVarInt(payload, 0x00);
                WriteVarInt(payload, protocol);
                WriteString(payload, host);
                payload.WriteByte((byte)((port >> 8) & 0xFF));
                payload.WriteByte((byte)(port & 0xFF));
                WriteVarInt(payload, 1);

                return Frame(payload.ToArray());
            }
        }

        internal static byte[] BuildStatusRequest()
        {
            using (MemoryStream payload = new MemoryStream())
            {
                WriteVarInt(payload, 0x00);
                return Frame(payload.ToArray());
            }
        }

        private static byte[] Frame(byte[] payload)
        {
            using (MemoryStream packet = new MemoryStream())
            {
                WriteVarInt(packet, payload.Length);
                packet.Write(payload, 0, payload.Length);
                return packet.ToArray();
            }
        }

        private static async Task<string> ReadStatusAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            int length = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);

            if (length <= 0 || length > 1 << 21)
            {
                throw new InvalidDataException("状态包长度异常：" + length);
            }

            byte[] buffer = new byte[length];
            int read = 0;

            while (read < length)
            {
                int chunk = await stream.ReadAsync(buffer, read, length - read, cancellationToken).ConfigureAwait(false);

                if (chunk <= 0)
                {
                    throw new EndOfStreamException("服务端提前关闭连接。");
                }

                read += chunk;
            }

            using (MemoryStream payload = new MemoryStream(buffer))
            {
                int packetId = ReadVarInt(payload);
                _ = packetId;

                return ReadString(payload);
            }
        }

        private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
        {
            int value = 0;
            int shift = 0;

            for (int i = 0; i < 5; i++)
            {
                byte[] single = new byte[1];
                int read = await stream.ReadAsync(single, 0, 1, cancellationToken).ConfigureAwait(false);

                if (read <= 0)
                {
                    throw new EndOfStreamException("读取 VarInt 时连接关闭。");
                }

                value |= (single[0] & 0x7F) << shift;

                if ((single[0] & 0x80) == 0)
                {
                    return value;
                }

                shift += 7;
            }

            throw new InvalidDataException("VarInt 过长。");
        }

        internal static void WriteVarInt(Stream stream, int value)
        {
            unchecked
            {
                int remaining = value;

                do
                {
                    byte current = (byte)(remaining & 0x7F);
                    remaining = (int)((uint)remaining >> 7);

                    if (remaining != 0)
                    {
                        current |= 0x80;
                    }

                    stream.WriteByte(current);
                }
                while (remaining != 0);
            }
        }

        internal static int ReadVarInt(Stream stream)
        {
            int value = 0;
            int shift = 0;

            for (int i = 0; i < 5; i++)
            {
                int current = stream.ReadByte();

                if (current < 0)
                {
                    throw new EndOfStreamException("VarInt 未结束。");
                }

                value |= (current & 0x7F) << shift;

                if ((current & 0x80) == 0)
                {
                    return value;
                }

                shift += 7;
            }

            throw new InvalidDataException("VarInt 过长。");
        }

        internal static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        internal static string ReadString(Stream stream)
        {
            int length = ReadVarInt(stream);

            if (length < 0 || length > 1 << 17)
            {
                throw new InvalidDataException("字符串长度异常：" + length);
            }

            byte[] bytes = new byte[length];
            int read = 0;

            while (read < length)
            {
                int chunk = stream.Read(bytes, read, length - read);

                if (chunk <= 0)
                {
                    throw new EndOfStreamException("读取字符串时连接关闭。");
                }

                read += chunk;
            }

            return Encoding.UTF8.GetString(bytes);
        }
    }
}
