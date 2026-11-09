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
using NLMCL.Abstractions;
using NLMCL.Core.Accounts;
using NLMCL.Core.Installing;
using NLMCL.Core.Logging;

namespace NLMCL.App.ViewModels
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

        /// <summary>外置登录所需的 authlib-injector（单测里可为 null）。</summary>
        private readonly IAuthlibInjector? _authlibInjector;

        /// <summary>控制台里的会话列表（多开；退出的也保留，方便回看日志）。</summary>
        public ObservableCollection<GameSessionItem> Sessions { get; } = new ObservableCollection<GameSessionItem>();

        /// <summary>会话栏里实际显示的条目（默认只显示运行中的）。</summary>
        public ObservableCollection<GameSessionItem> BarSessions { get; } = new ObservableCollection<GameSessionItem>();

        /// <summary>是否在会话栏里也列出已退出的游戏。</summary>
        [ObservableProperty]
        private bool _showExitedSessions;

        /// <summary>已退出的会话数量（用于「显示已退出 (N)」）。</summary>
        [ObservableProperty]
        private string _exitedSessionsLabel = string.Empty;

        /// <summary>会话栏是否显示（没有会话时隐藏，省版面）。</summary>
        [ObservableProperty]
        private bool _hasSessions;

        /// <summary>会话 → 列表项，事件回调时用它定位是哪个游戏。</summary>
        private readonly Dictionary<IGameSession, GameSessionItem> _items =
            new Dictionary<IGameSession, GameSessionItem>();

        /// <summary>当前正在看哪个会话的日志。</summary>
        [ObservableProperty]
        private GameSessionItem? _selectedSession;

        /// <summary>所有仍在运行的会话（多开：允许同时跑多个游戏）。</summary>
        private readonly List<IGameSession> _sessions = new List<IGameSession>();

        /// <summary>最近一次启动的会话（「停止」按钮作用于它）。</summary>
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
            IJavaRuntimeInstaller? javaInstaller = null,
            IAuthlibInjector? authlibInjector = null)
        {
            _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _javaLocator = javaLocator ?? throw new ArgumentNullException(nameof(javaLocator));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _javaInstaller = javaInstaller;
            _authlibInjector = authlibInjector;

            GameDirectory = paths.DefaultMinecraftDirectory;

            // 用集合变化驱动「是否有日志」，这样直接往 Lines 里加（测试、日志回放）也能正确切换占位提示。
            Lines.CollectionChanged += (_, __) => HasLines = Lines.Count > 0;
            Sessions.CollectionChanged += (_, __) => SyncBarSessions();
        }

        public ObservableCollection<ConsoleLine> Lines { get; } = new ObservableCollection<ConsoleLine>();

        /// <summary>重建会话栏：默认只列运行中的，避免历史记录一直占版面。</summary>
        private void SyncBarSessions()
        {
            BarSessions.Clear();

            int exited = 0;

            foreach (GameSessionItem item in Sessions)
            {
                if (!item.IsRunning)
                {
                    exited++;
                }

                if (item.IsRunning || ShowExitedSessions)
                {
                    BarSessions.Add(item);
                }
            }

            ExitedSessionsLabel = exited == 0 ? string.Empty : "显示已退出 (" + exited + ")";
            HasSessions = BarSessions.Count > 0;
        }

        partial void OnShowExitedSessionsChanged(bool value) => SyncBarSessions();

        /// <summary>显示 / 隐藏已退出的会话。</summary>
        [RelayCommand]
        public void ToggleExitedSessions() => ShowExitedSessions = !ShowExitedSessions;

        /// <summary>清掉已退出的会话（释放日志缓冲）。</summary>
        [RelayCommand]
        public void ClearExitedSessions()
        {
            foreach (GameSessionItem item in Sessions.Where(s => !s.IsRunning).ToList())
            {
                Sessions.Remove(item);
            }

            // 映射里也要清掉这些会话，否则事件还会路由到已删除的条目
            foreach (IGameSession session in _items.Keys.ToList())
            {
                if (!Sessions.Contains(_items[session]))
                {
                    _items.Remove(session);
                }
            }

            SyncBarSessions();
            StatusText = "已清理退出的会话";
        }

        partial void OnSelectedSessionChanged(GameSessionItem? value)
        {
            Lines.Clear();

            if (value == null)
            {
                HasLines = false;
                return;
            }

            foreach (ConsoleLine line in value.Buffer)
            {
                Lines.Add(line);
            }

            HasLines = Lines.Count > 0;
            StatusText = value.IsRunning ? "查看中：" + value.Title : "查看中：" + value.Title + "（已退出）";
        }

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

                LaunchRequest request = new LaunchRequest(GameDirectory, VersionId)
                {
                    JavaExecutable = java.ExecutablePath,
                    JavaVersion = java.Version
                }.WithAccount(Account ?? OfflineAccountFactory.Create("Player"));

                // 外置登录：与主界面共用同一段逻辑，避免两个入口行为不一致
                request.ExtraJvmArguments = await ExternalLaunch
                    .BuildJvmArgumentsAsync(Account, _authlibInjector, CancellationToken.None)
                    .ConfigureAwait(true);

                IGameSession session = await _launcher
                    .StartAsync(request, CancellationToken.None)
                    .ConfigureAwait(true);

                Attach(session);
            }
            catch (Exception ex)
            {
                StatusText = "启动失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 接管一个已经在运行的会话。
        ///
        /// 主界面「启动游戏」与这里原本是两条独立路径：主界面走 IGameLauncher（发射后不管，
        /// 不捕获输出），所以从「世界库」启动的游戏在控制台里看不到任何日志。
        /// 统一成会话模式后，两个入口共用一个控制台，也都能用「停止」。
        /// </summary>
        public void Attach(IGameSession session)
        {
            if (session == null)
            {
                return;
            }

            _session = session;
            _exitHandled = false;

            _sessions.Add(session);

            GameSessionItem item = new GameSessionItem(session.ProcessId, VersionId);
            _items[session] = item;
            Sessions.Add(item);
            SelectedSession = item;

            session.Output += OnOutput;
            session.Exited += (sender, code) => OnExited(session, code);

            IsRunning = true;
            StatusText = "运行中（共 " + _sessions.Count + " 个，最新 PID " + session.ProcessId + "）";
            StartWatchdog();

            // JVM 秒退时，Exited 可能在订阅之前就广播完了——补查一次终态。
            if (session.ExitCode.HasValue)
            {
                HandleExit(session, session.ExitCode.Value);
            }
        }

        /// <summary>结束选中会话（没选中就停最新那个）。</summary>
        [RelayCommand]
        public void Stop()
        {
            IGameSession? target = _session;

            if (SelectedSession != null)
            {
                foreach (KeyValuePair<IGameSession, GameSessionItem> pair in _items)
                {
                    if (ReferenceEquals(pair.Value, SelectedSession))
                    {
                        target = pair.Key;
                        break;
                    }
                }
            }

            if (target == null)
            {
                return;
            }

            try
            {
                target.Stop();
                StatusText = "已请求结束 " + (SelectedSession?.Title ?? "当前游戏");
            }
            catch (Exception ex)
            {
                StatusText = "结束进程失败：" + ex.Message;
            }
        }

        /// <summary>停止所有正在运行的游戏。</summary>
        [RelayCommand]
        public void StopAll()
        {
            int stopped = 0;

            foreach (IGameSession session in new List<IGameSession>(_sessions))
            {
                try
                {
                    session.Stop();
                    stopped++;
                }
                catch (Exception)
                {
                    // 个别失败不影响其它
                }
            }

            StatusText = stopped == 0 ? "没有正在运行的游戏" : "已请求结束 " + stopped + " 个游戏";
        }

        /// <summary>切换当前查看的会话。</summary>
        [RelayCommand]
        public void SelectSession(GameSessionItem? item)
        {
            if (item != null)
            {
                SelectedSession = item;
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
            GameSessionItem? item = null;

            if (sender is IGameSession session)
            {
                _items.TryGetValue(session, out item);
            }

            // 这条回调运行在进程输出泵线程上：直接改 Lines 会让 WPF 抛跨线程异常，
            // 而该异常在后台线程上无人捕获，会把整个启动器进程带走。统一封送到 UI 线程。
            _dispatcher.Post(() =>
            {
                try
                {
                    AppendLine(line, item);
                }
                catch (Exception)
                {
                    // 单行日志渲染失败不该终止启动器。
                }
            });
        }

        private void AppendLine(GameOutputLine line, GameSessionItem? item)
        {
            MarkOutput();
            LogTag tag = LogClassifier.Classify(line.Text);
            ConsoleLine entry = new ConsoleLine(line.Text, tag);

            // 每个会话各自记账；界面只显示当前选中的那个
            item?.Append(entry);

            if (item == null || ReferenceEquals(item, SelectedSession))
            {
                Lines.Add(entry);

                while (Lines.Count > MaxLines)
                {
                    Lines.RemoveAt(0);
                }
            }

            AppendToLogFile((item == null ? string.Empty : "[PID " + item.ProcessId + "] ") + line.Text);

            if (tag != LogTag.Normal)
            {
                ErrorCount++;
            }
        }

        /// <summary>
        /// 日志落盘：%APPDATA%\NLMCL\logs\console-*.log。
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

        private void OnExited(IGameSession session, int exitCode)
        {
            _dispatcher.Post(() => HandleExit(session, exitCode));
        }

        private void HandleExit(IGameSession session, int exitCode)
        {
            _sessions.Remove(session);

            if (_items.TryGetValue(session, out GameSessionItem? finished))
            {
                finished.IsRunning = false;
            }

            SyncBarSessions();

            // 多开：还有别的游戏在跑，就不能把界面标记成"已退出"
            if (_sessions.Count > 0)
            {
                IsRunning = true;
                StatusText = "还有 " + _sessions.Count + " 个游戏在运行";
                return;
            }

            StopWatchdog();

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
