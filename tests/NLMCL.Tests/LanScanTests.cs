using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core.Lan;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>局域网扫描：广播报文解析、描述扁平化、VarInt、真实 SLP 回环、视图模型。</summary>
    public class LanScanTests
    {
        [Theory]
        [InlineData("[MOTD]Hello[/MOTD][AD]25565[/AD]", true, "Hello", 25565)]
        [InlineData("[MOTD]中文房名[/MOTD][AD]12345[/AD]", true, "中文房名", 12345)]
        [InlineData("[MOTD]no port[/MOTD]", false, "", 0)]
        [InlineData("[AD]70000[/AD]", false, "", 0)]
        [InlineData("", false, "", 0)]
        public void Parses_lan_broadcast(string message, bool ok, string motd, int port)
        {
            bool parsed = LanScanner.TryParse(message, out string actualMotd, out int actualPort);

            Assert.Equal(ok, parsed);

            if (ok)
            {
                Assert.Equal(motd, actualMotd);
                Assert.Equal(port, actualPort);
            }
        }

        [Fact]
        public void Flattens_description_forms()
        {
            Assert.Equal("Hello", ServerListPing.FlattenDescription(Newtonsoft.Json.Linq.JToken.Parse("\"Hello\"")));
            Assert.Equal("World", ServerListPing.FlattenDescription(Newtonsoft.Json.Linq.JToken.Parse("{\"text\":\"World\"}")));

            Assert.Equal("AB", ServerListPing.FlattenDescription(
                Newtonsoft.Json.Linq.JToken.Parse("{\"text\":\"A\",\"extra\":[{\"text\":\"B\"}]}")));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(25565)]
        [InlineData(2097151)]
        public void VarInt_round_trips(int value)
        {
            using MemoryStream stream = new MemoryStream();
            ServerListPing.WriteVarInt(stream, value);
            stream.Position = 0;

            Assert.Equal(value, ServerListPing.ReadVarInt(stream));
        }

        [Fact]
        public async Task Pings_a_local_server()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Task server = Task.Run(async () =>
            {
                using TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                using NetworkStream stream = client.GetStream();

                ReadPacket(stream); // 握手
                ReadPacket(stream); // 状态请求

                const string json = @"{
                    ""version"": { ""name"": ""1.21.1"" },
                    ""players"": { ""online"": 2, ""max"": 8 },
                    ""description"": { ""text"": ""测试房"" }
                }";

                using MemoryStream payload = new MemoryStream();
                ServerListPing.WriteVarInt(payload, 0x00);
                ServerListPing.WriteString(payload, json);

                byte[] body = payload.ToArray();

                using MemoryStream framed = new MemoryStream();
                ServerListPing.WriteVarInt(framed, body.Length);
                framed.Write(body, 0, body.Length);

                byte[] packet = framed.ToArray();
                await stream.WriteAsync(packet, 0, packet.Length).ConfigureAwait(false);
            });

            try
            {
                ServerListPing pinger = new ServerListPing();
                LanRoom room = await pinger.PingAsync("127.0.0.1", port, CancellationToken.None);

                Assert.True(room.Pinged);
                Assert.Equal(2, room.Online);
                Assert.Equal(8, room.Max);
                Assert.Equal("1.21.1", room.VersionName);
                Assert.Contains("测试房", room.Motd);
                Assert.True(room.LatencyMs >= 0);
            }
            finally
            {
                listener.Stop();

                try
                {
                    await server.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void ReadPacket(NetworkStream stream)
        {
            int length = ServerListPing.ReadVarInt(stream);

            byte[] buffer = new byte[length];
            int read = 0;

            while (read < length)
            {
                int chunk = stream.Read(buffer, read, length - read);

                if (chunk <= 0)
                {
                    throw new EndOfStreamException();
                }

                read += chunk;
            }
        }

        private sealed class FakeScanner : ILanScanner
        {
            private readonly IReadOnlyList<LanRoom> _rooms;

            public FakeScanner(IReadOnlyList<LanRoom> rooms)
            {
                _rooms = rooms;
            }

            public Task<IReadOnlyList<LanRoom>> ScanAsync(
                TimeSpan timeout, IProgress<LanRoom>? progress, CancellationToken cancellationToken)
                => Task.FromResult(_rooms);
        }

        private sealed class FakeClipboard : IClipboard
        {
            public string? Text { get; private set; }

            public void SetText(string text) => Text = text;

            public string? GetText() => Text;
        }

        [Fact]
        public async Task ViewModel_lists_rooms_and_copies_address()
        {
            LanRoom room = new LanRoom("192.168.1.5", 25565)
            {
                Motd = "朋友的房",
                VersionName = "1.20.1",
                Online = 1,
                Max = 4,
                Pinged = true
            };

            FakeClipboard clipboard = new FakeClipboard();
            LanScanViewModel vm = new LanScanViewModel(new FakeScanner(new[] { room }), clipboard);

            await vm.ScanAsync();

            Assert.Single(vm.Rooms);
            Assert.True(vm.HasRooms);
            Assert.False(vm.IsScanning);
            Assert.Contains("发现 1 个房间", vm.StatusText);

            vm.CopyAddress(room);

            Assert.Equal("192.168.1.5:25565", clipboard.Text);
        }
    }
}
