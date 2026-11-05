using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 控制台里的一个游戏会话（多开时会有多个）。
    ///
    /// 每个会话各自缓存日志行；界面只显示"当前选中"的那个，
    /// 切回来时能从缓存重建，不会混在一起。
    /// </summary>
    public sealed partial class GameSessionItem : ObservableObject
    {
        /// <summary>缓存上限：和界面一致，避免多开时内存翻倍失控。</summary>
        public const int MaxBufferedLines = 2000;

        private readonly List<ConsoleLine> _buffer = new List<ConsoleLine>();

        public GameSessionItem(int processId, string versionLabel)
        {
            ProcessId = processId;
            VersionLabel = string.IsNullOrWhiteSpace(versionLabel) ? "未知版本" : versionLabel;
            StartedAt = DateTime.Now;
        }

        public int ProcessId { get; }

        public string VersionLabel { get; }

        public DateTime StartedAt { get; }

        /// <summary>是否还在运行（退出后保留条目，方便翻日志）。</summary>
        [ObservableProperty]
        private bool _isRunning = true;

        /// <summary>会话标题，例如「1.21.1 · PID 1234」。</summary>
        public string Title => VersionLabel + " · PID " + ProcessId;

        /// <summary>副标题：状态 + 启停时间。</summary>
        public string Subtitle => IsRunning
            ? "运行中 · " + StartedAt.ToString("HH:mm:ss") + " 启动"
            : "已退出 · " + StartedAt.ToString("HH:mm:ss") + " 启动";

        /// <summary>缓存的日志行（只在 UI 线程读写）。</summary>
        internal List<ConsoleLine> Buffer => _buffer;

        internal void Append(ConsoleLine line)
        {
            _buffer.Add(line);

            while (_buffer.Count > MaxBufferedLines)
            {
                _buffer.RemoveAt(0);
            }
        }

        internal void Clear()
        {
            _buffer.Clear();
        }

        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(Subtitle));
        }
    }
}
