using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Core.Networking;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 真实网络栈上的集成测试。刻意不 mock：要验证的就是"在这台机器、这个网络下
    /// 能否给出正确地址、能否真的探通一个端口"。
    /// </summary>
    public class LanNetworkIntegrationTests
    {
        private static List<string> MachineAddresses()
        {
            List<string> result = new List<string>();

            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        result.Add(info.Address.ToString());
                    }
                }
            }

            return result;
        }

        [Fact]
        public void Returns_an_address_that_actually_exists_on_this_machine()
        {
            LocalNetworkInfo network = new LocalNetworkInfo();

            string? detected = network.GetLocalIPv4();

            if (detected == null)
            {
                return; // 完全没网络时不判失败
            }

            List<string> actual = MachineAddresses();

            // 给出去的地址必须是本机真有的，且不能是回环
            Assert.Contains(detected, actual);
            Assert.NotEqual("127.0.0.1", detected);
            Assert.NotEqual("0.0.0.0", detected);
        }

        [Fact]
        public async Task Probes_a_real_listening_port_as_open()
        {
            // 起一个真的监听 socket，让被测代码去连
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                LocalNetworkInfo network = new LocalNetworkInfo();

                bool open = await network.IsPortOpenAsync("127.0.0.1", port, CancellationToken.None);

                Assert.True(open, "监听中的端口应被判为可连：" + port);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task Probes_a_closed_port_as_closed()
        {
            // 先占一个端口再放掉，保证这个端口确定没人监听
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            LocalNetworkInfo network = new LocalNetworkInfo();

            bool open = await network.IsPortOpenAsync("127.0.0.1", port, CancellationToken.None);

            Assert.False(open, "没人监听的端口不该被判为可连：" + port);
        }

        [Fact]
        public async Task Unreachable_host_times_out_instead_of_hanging()
        {
            LocalNetworkInfo network = new LocalNetworkInfo();

            // 保留地址段（TEST-NET-1），在校园网里不会被路由
            DateTime started = DateTime.UtcNow;
            bool open = await network.IsPortOpenAsync("192.0.2.1", 25565, CancellationToken.None);
            TimeSpan elapsed = DateTime.UtcNow - started;

            Assert.False(open);
            Assert.True(elapsed < TimeSpan.FromSeconds(10), "探测应在超时内返回，实际用了 " + elapsed);
        }
    }
}
