using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Servers;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>SRV 解析：报文构造与响应解析（不依赖真实 DNS）。</summary>
    public class SrvResolverTests
    {
        [Fact]
        public void Query_encodes_name_and_type_srv()
        {
            byte[] packet = SrvResolver.BuildQuery("_minecraft._tcp.mc2.nxist.top", 0x1234);

            Assert.Equal(0x12, packet[0]);
            Assert.Equal(0x34, packet[1]);
            Assert.Equal(0x00, packet[4]);  // QDCOUNT 高字节
            Assert.Equal(1, packet[5]);

            // 包尾应为 00 00 21 00 01（名字结束 + QTYPE=33 + QCLASS=IN）
            int tail = packet.Length - 5;
            Assert.Equal(0x00, packet[tail]);
            Assert.Equal(0x00, packet[tail + 1]);
            Assert.Equal(0x21, packet[tail + 2]);
            Assert.Equal(0x00, packet[tail + 3]);
            Assert.Equal(0x01, packet[tail + 4]);

            Assert.Contains("_minecraft", Encoding.ASCII.GetString(packet));
        }

        [Fact]
        public void Parses_srv_answer_with_pointer_name()
        {
            byte[] response = BuildResponse(
                "_minecraft._tcp.mc2.nxist.top",
                new[] { (priority: 0, weight: 5, port: 46718, target: "mc2.nxist.top") });

            IReadOnlyList<SrvResolver.SrvRecord> records = SrvResolver.ParseResponse(response);

            Assert.Single(records);
            Assert.Equal(46718, records[0].Port);
            Assert.Equal("mc2.nxist.top", records[0].Target);
            Assert.Equal(0, records[0].Priority);
        }

        [Fact]
        public void Sorts_by_priority_then_weight()
        {
            byte[] response = BuildResponse(
                "_minecraft._tcp.example.org",
                new[]
                {
                    (priority: 10, weight: 0, port: 3000, target: "backup.example.org"),
                    (priority: 0, weight: 1, port: 2000, target: "slow.example.org"),
                    (priority: 0, weight: 9, port: 1000, target: "fast.example.org")
                });

            IReadOnlyList<SrvResolver.SrvRecord> records = SrvResolver.ParseResponse(response);

            Assert.Equal(3, records.Count);
            Assert.Equal(1000, records[0].Port);   // 优先级 0、权重 9
            Assert.Equal(2000, records[1].Port);   // 优先级 0、权重 1
            Assert.Equal(3000, records[2].Port);   // 优先级 10
        }

        [Fact]
        public async Task Explicit_port_is_respected_without_dns()
        {
            SrvResolver resolver = new SrvResolver();

            ServerEndpoint endpoint = await resolver.ResolveAsync("mc2.nxist.top", 25566, CancellationToken.None);

            Assert.False(endpoint.FromSrv);
            Assert.Equal("mc2.nxist.top", endpoint.Host);
            Assert.Equal(25566, endpoint.Port);
        }

        [Fact]
        public async Task Ip_literal_is_returned_as_is()
        {
            SrvResolver resolver = new SrvResolver();

            ServerEndpoint endpoint = await resolver.ResolveAsync("10.21.38.133", 25565, CancellationToken.None);

            Assert.False(endpoint.FromSrv);
            Assert.Equal("10.21.38.133", endpoint.Host);
            Assert.Equal(25565, endpoint.Port);
        }

        /// <summary>构造一个带一条 SRV 应答的 DNS 响应（ANSWER 的 NAME 用压缩指针）。</summary>
        private static byte[] BuildResponse(string question, (int priority, int weight, int port, string target)[] answers)
        {
            List<byte> buffer = new List<byte>();

            // 头部
            buffer.AddRange(new byte[] { 0x12, 0x34, 0x81, 0x80, 0x00, 0x01, 0x00, (byte)answers.Length, 0x00, 0x00, 0x00, 0x00 });

            // 问题
            foreach (string label in question.Split('.'))
            {
                buffer.Add((byte)label.Length);
                buffer.AddRange(Encoding.ASCII.GetBytes(label));
            }

            buffer.Add(0x00);
            buffer.AddRange(new byte[] { 0x00, 0x21, 0x00, 0x01 });

            // 应答
            foreach (var answer in answers)
            {
                buffer.AddRange(new byte[] { 0xC0, 0x0C }); // 指向问题名
                buffer.AddRange(new byte[] { 0x00, 0x21, 0x00, 0x01 }); // SRV / IN
                buffer.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x3C }); // TTL 60

                List<byte> rdata = new List<byte>
                {
                    (byte)(answer.priority >> 8), (byte)(answer.priority & 0xFF),
                    (byte)(answer.weight >> 8), (byte)(answer.weight & 0xFF),
                    (byte)(answer.port >> 8), (byte)(answer.port & 0xFF)
                };

                foreach (string label in answer.target.Split('.'))
                {
                    rdata.Add((byte)label.Length);
                    rdata.AddRange(Encoding.ASCII.GetBytes(label));
                }

                rdata.Add(0x00);

                buffer.AddRange(new byte[] { (byte)(rdata.Count >> 8), (byte)(rdata.Count & 0xFF) });
                buffer.AddRange(rdata);
            }

            return buffer.ToArray();
        }
    }
}
