using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLCL.Abstractions;
using NLCL.Core.Accounts;
using NLCL.Core.Installing;
using NLCL.Core.Logging;

namespace NLCL.App.ViewModels
{
    /// <summary>控制台里的一行日志。</summary>
    public sealed class ConsoleLine
    {
        public ConsoleLine(string text, LogTag tag)
        {
            Text = text ?? string.Empty;
            Tag = tag;
            TagLabel = LogClassifier.LabelOf(tag);
            Color = LogClassifier.ColorOf(tag);
        }

        public string Text { get; }

        public LogTag Tag { get; }

        public string TagLabel { get; }

        /// <summary>界面直接用这个颜色（与 legacy 的配色一致）。</summary>
        public string Color { get; }

        /// <summary>只有非普通行才显示标签，否则整屏都是"正常信息"。</summary>
        public bool ShowTag => Tag != LogTag.Normal;
    }

    /// <summary>
    /// 启动控制台：启动带输出捕获的游戏会话、逐行分类、停止与清空。
    /// 不引用 WPF 类型，可单测。
    /// </summary>
    public sealed partial class LaunchConsoleViewModel : ObservableObject
    {
        private readonly IGameSessionLauncher _launcher;
        private readonly IJavaLocator _javaLocator;
        private readonly IAppPaths _paths;
        private readonly IUiDispatcher _dispatcher;

        /// <summary>没有合适的 Java 时按版本要求自动下载（单测里可为 null）。</summary>
        private readonly IJavaRuntimeInstaller? _javaInstaller;

        private IGameSession? _session;

        /// <summary>
        /// 控制台日志同时落盘：界面上限 2000 行，超了就丢最旧的，
        /// 而"游戏崩了但界面只看到最后几行"是最常见的排障场景。
        /// </summary>
        private StreamWriter? _logWriter;

        /// <summary>退出只处理一次（事件与"订阅前已退出"的补查可能同时到达）。</summary>
        private bool _exitHandled;

        public LaunchConsoleViewModel(
            IGameSessionLauncher launcher,
            IJavaLocator javaLocator,
            IAppPaths paths,
            IUiDispatcher dispatcher,
            IJavaRuntimeInstaller? javaInstaller = null)
        {
            _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _javaLocator = javaLocator ?? throw new ArgumentNullException(nameof(javaLocator));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _javaInstaller = javaInstaller;

            GameDirectory = paths.DefaultMinecraftDirectory;

            // 用集合变化驱动「是否有日志」，这样直接往 Lines 里加（测试、日志回放）也能正确切换占位提示。
            Lines.CollectionChanged += (_, __) => HasLines = Lines.Count > 0;
        }

        public ObservableCollection<ConsoleLine> Lines { get; } = new ObservableCollection<ConsoleLine>();

        /// <summary>本次会话的日志文件路径（为空表示还没写过）。</summary>
        public string LogFilePath { get; private set; } = string.Empty;



        /// <summary>当前账号（由主窗口同步过来）。</summary>


        public Account? Account { get; set; }

        /// <summary>是否有日志行（为空时界面显示占位提示）。</summary>
        [ObservableProperty]
        private bool _hasLines;

        [ObservableProperty]
        private string _gameDirectory = string.Empty;

        /// <summary>要启动的版本目录名。</summary>
        [ObservableProperty]
        private string _versionId = string.Empty;

        [ObservableProperty]
        private string _statusText = "尚未启动";

        [ObservableProperty]
        private bool _isRunning;

        /// <summary>超过这个行数就丢掉最旧的，避免长时间运行把内存吃满。</summary>
        public int MaxLines { get; set; } = 2000;

        /// <summary>按标签统计，界面上做角标。</summary>
        [ObservableProperty]
        private int _errorCount;

        /// <summary>启动游戏并接管输出。</summary>
        [RelayCommand]
        public async Task StartAsync()
        {
            if (IsRunning)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(VersionId))
            {
                StatusText = "请先选择要启动的版本";
                return;
            }

            StatusText = "正在查找 Java…";

            try
            {
                IReadOnlyList<JavaRuntime> javas = await _javaLocator
                    .FindInstalledAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                // 与主界面同一条规则：按版本 JSON 要求的 Java 主版本挑，
                // 而不是无脑取最高版本（1.20.1 用 Java 25 起不来）。
                int? required = JavaRequirements.ReadRequiredMajor(GameDirectory, VersionId);
                JavaRuntime? java = JavaRequirements.Pick(javas, required);

                if (java == null && _javaInstaller != null)
                {
                    string? component = JavaRequirements.ReadComponent(GameDirectory, VersionId);
                    if (!string.IsNullOrEmpty(component))
                    {
                        StatusText = "本机没有合适的 Java，正在下载 " + component + "…";

                        try
                        {
                            JavaRuntime installed = await _javaInstaller
                                .InstallAsync(component!, null, CancellationToken.None)
                                .ConfigureAwait(true);

                            java = JavaRequirements.Pick(new[] { installed }, required) ?? installed;
                        }
                        catch (Exception ex)
                        {
                            StatusText = "下载 Java 运行时失败：" + ex.Message;
                            return;
                        }
                    }
                }

                if (java == null)
                {
                    StatusText = required.HasValue
                        ? "该版本需要 Java " + required.Value
                            + "，本机只有 " + JavaRequirements.Describe(javas)
                            + "（不兼容）。请安装 Java " + required.Value + "。"
                        : "没有找到可用的 Java";
                    return;
                }

                string javaNote = required.HasValue && java.Major != required.Value
                    ? "，该版本要求 Java " + required.Value
                    : string.Empty;
                StatusText = "正在启动（Java " + java.Major + javaNote + "）…";

                _exitHandled = false;
                _session = await _launcher.StartAsync(
                    new LaunchRequest(GameDirectory, VersionId)
                    {
                        JavaExecutable = java.ExecutablePath,
                        JavaVersion = java.Version
                    }.WithAccount(Account ?? OfflineAccountFactory.Create("Player")),
                    CancellationToken.None).ConfigureAwait(true);

                _session.Output += OnOutput;
                _session.Exited += OnExited;

                IsRunning = true;
                StatusText = "运行中（PID " + _session.ProcessId + "）";

                // JVM 秒退时，Exited 可能在订阅之前就广播完了——补查一次终态，
                // 否则界面永远停在"运行中"，StartCommand 会一直拒绝再次启动。
                if (_session.ExitCode.HasValue)
                {
                    HandleExit(_session.ExitCode.Value);
                }
            }
            catch (Exception ex)
            {
                StatusText = "启动失败：" + ex.Message;
            }
        }

        /// <summary>结束进程。</summary>
        [RelayCommand]
        public void Stop()
        {
            if (_session == null)
            {
                return;
            }

            try
            {
                _session.Stop();
                StatusText = "已请求结束进程";
            }
            catch (Exception ex)
            {
                StatusText = "结束进程失败：" + ex.Message;
            }
        }

        /// <summary>清空日志（不影响进程）。</summary>
        [RelayCommand]
        public void Clear()
        {
            Lines.Clear();
            ErrorCount = 0;
            StatusText = IsRunning ? "已清空日志（进程仍在运行）" : "已清空日志";
        }

        private void OnOutput(object? sender, GameOutputLine line)
        {
            // 这条回调运行在进程输出泵线程上：直接改 Lines 会让 WPF 抛跨线程异常，
            // 而该异常在后台线程上无人捕获，会把整个启动器进程带走。统一封送到 UI 线程。
            _dispatcher.Post(() =>
            {
                try
                {
                    AppendLine(line);
                }
                catch (Exception)
                {
                    // 单行日志渲染失败不该终止启动器。
                }
            });
        }

        private void AppendLine(GameOutputLine line)
        {
            LogTag tag = LogClassifier.Classify(line.Text);

            Lines.Add(new ConsoleLine(line.Text, tag));
            AppendToLogFile(line.Text);

            if (tag != LogTag.Normal)
            {
                ErrorCount++;
            }

            while (Lines.Count > MaxLines)
            {
                Lines.RemoveAt(0);
            }
        }

        /// <summary>
        /// 日志落盘：%APPDATA%\NLCL\logs\console-*.log。
        /// 界面上限 2000 行，而"游戏崩了，界面上只剩最后几行"是最常见的排障场景。
        /// </summary>
        private void AppendToLogFile(string text)
        {
            try
            {
                if (_logWriter == null)
                {
                    string folder = Path.Combine(_paths.DataRoot, "logs");
                    Directory.CreateDirectory(folder);

                    string path = Path.Combine(
                        folder, "console-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");

                    _logWriter = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
                    LogFilePath = path;
                }

                _logWriter.WriteLine(text);
            }
            catch (Exception)
            {
                // 日志写不出去不能影响游戏。
            }
        }

        private void OnExited(object? sender, int exitCode)
        {
            _dispatcher.Post(() => HandleExit(exitCode));
        }

        private void HandleExit(int exitCode)
        {
            if (_exitHandled)
            {
                return;
            }

            _exitHandled = true;
            IsRunning = false;
            AppendToLogFile("=== 进程退出，退出码 " + exitCode);

            try
            {
                // 枚举前先复制：此时可能还有残留的输出在投递途中。
                IEnumerable<string> recent = Lines.Select(line => line.Text).ToList();
                CrashReport? report = CrashDetector.Detect(exitCode, recent);

                if (report == null)
                {
                    StatusText = "已退出（退出码 " + exitCode + "）";
                    return;
                }

                StatusText = report.Reason + (string.IsNullOrEmpty(report.Hint) ? "" : " · " + report.Hint);

                // 把结论也写进日志区，否则用户只看状态栏容易漏
                Lines.Add(new ConsoleLine("【崩溃】" + report.Reason, LogTag.ModConflict));

                if (!string.IsNullOrEmpty(report.Hint))
                {
                    Lines.Add(new ConsoleLine("【建议】" + report.Hint, LogTag.ResourceFailure));
                }
            }
            catch (Exception)
            {
                StatusText = "已退出（退出码 " + exitCode + "）";
            }
        }
    }
}
