using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 「局域网扫描」页：监听 Minecraft 的 UDP 广播，列出房间并显示在线人数。
    ///
    /// 二维码分享暂未做（需要 QR 编码器，见 docs 待办）。
    /// </summary>
    public sealed partial class LanScanViewModel : ObservableObject
    {
        private readonly ILanScanner _scanner;
        private readonly IClipboard _clipboard;

        private CancellationTokenSource? _cts;

        public LanScanViewModel(ILanScanner scanner, IClipboard clipboard)
        {
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        }

        public ObservableCollection<LanRoom> Rooms { get; } = new ObservableCollection<LanRoom>();

        [ObservableProperty]
        private string _statusText = "点「开始扫描」发现局域网房间（对方游戏里要选「对局域网开放」）。";

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private string _scanDetailsText = string.Empty;

        public bool HasScanDetails => !string.IsNullOrEmpty(ScanDetailsText);

        partial void OnScanDetailsTextChanged(string value)
        {
            OnPropertyChanged(nameof(HasScanDetails));
        }

        private void SetScanDetails(IReadOnlyList<LanInterfaceStatus> interfaces)
        {
            if (interfaces.Count == 0)
            {
                ScanDetailsText = string.Empty;
                return;
            }

            ScanDetailsText = string.Join(
                Environment.NewLine,
                interfaces.Select(item =>
                    (item.Joined ? "已监听" : "未监听")
                    + "：" + item.Name
                    + (string.IsNullOrWhiteSpace(item.Address) ? string.Empty : " " + item.Address)
                    + (string.IsNullOrWhiteSpace(item.Error) ? string.Empty : "（" + item.Error + "）")));
        }

        public bool HasRooms => Rooms.Count > 0;

        [RelayCommand]
        public async Task ScanAsync()
        {
            if (IsScanning)
            {
                return;
            }

            IsScanning = true;
            Rooms.Clear();
            ScanDetailsText = string.Empty;
            OnPropertyChanged(nameof(HasRooms));
            StatusText = "正在监听局域网广播（最多 10 秒）…";

            _cts = new CancellationTokenSource();

            try
            {
                IReadOnlyList<LanRoom> rooms;

                if (_scanner is ILanScannerDiagnostics diagnostics)
                {
                    LanScanReport report = await diagnostics
                        .ScanWithReportAsync(TimeSpan.FromSeconds(10), null, _cts.Token)
                        .ConfigureAwait(true);

                    rooms = report.Rooms;
                    SetScanDetails(report.Interfaces);
                }
                else
                {
                    rooms = await _scanner
                        .ScanAsync(TimeSpan.FromSeconds(10), null, _cts.Token)
                        .ConfigureAwait(true);

                    SetScanDetails(Array.Empty<LanInterfaceStatus>());
                }

                foreach (LanRoom room in rooms)
                {
                    Rooms.Add(room);
                }

                OnPropertyChanged(nameof(HasRooms));

                StatusText = rooms.Count == 0
                    ? "没有发现房间。请确认对方已开放局域网；校园网可能隔离客户端，可尝试手机热点、虚拟局域网或手动地址。"
                    : "发现 " + rooms.Count + " 个房间。";
            }
            catch (OperationCanceledException)
            {
                StatusText = "扫描已取消。";
            }
            catch (Exception ex)
            {
                StatusText = "扫描失败：" + ex.Message;
            }
            finally
            {
                IsScanning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        [RelayCommand]
        public void Cancel()
        {
            _cts?.Cancel();
        }

        [RelayCommand]
        public void CopyAddress(LanRoom? room)
        {
            if (room == null)
            {
                return;
            }

            _clipboard.SetText(room.Endpoint);
            StatusText = "已复制地址 " + room.Endpoint;
        }
    }
}
