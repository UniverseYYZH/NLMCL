using System.Collections.Generic;
using System.Threading;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Pcl;
using NLMCL.Core.Pcl.Vb;
using NLMCL.Core.Accounts;
using NLMCL.Core.Installing;
using NLMCL.Core.Networking;
using NLMCL.Core.Settings;
using NLMCL.Core.Snapshots;
using NLMCL.Core.Resources;
using NLMCL.Core.Maintenance;
using NLMCL.Core.Diagnostics;

namespace NLMCL.App
{
    /// <summary>
    /// 组合根（Composition Root）：唯一知道「哪个接口用哪个实现」的地方。
    /// 换后端只改这里，UI 与业务层不动。
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider? _services;

        private Mutex? _singleInstance;
        private bool _ownsSingleInstance;

        /// <summary>页面代码后置需要从组合根取窗口/服务时使用。</summary>
        public IServiceProvider RootServices =>
            _services ?? throw new InvalidOperationException("应用尚未完成初始化。");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 两个实例同时写 accounts.json / 实例设置会互相覆盖；先拿单实例锁。
            _singleInstance = new Mutex(
                initiallyOwned: true,
                name: @"Local\NLMCL.SingleInstance",
                createdNew: out _ownsSingleInstance);

            if (!_ownsSingleInstance)
            {
                // 最小化/隐藏到托盘时，第二个实例必须把已有窗口叫回来，而不是无声退出。
                if (!TryActivateExistingInstance())
                {
                    MessageBox.Show(
                        "NLMCL 已经在运行，但没有找到可显示的窗口。请在任务管理器中结束 NLMCL 后重试。",
                        "NLMCL",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                Shutdown();
                return;
            }

            // 先挂兜底再干活：没有这几行，后台线程上的任何异常都会让进程无声消失。
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                // 必须在任何网络调用之前、且在当前（UI）线程上，把 PCL 的全局状态初始化掉。
                // 否则 ModBase 会在线程池线程上首次初始化，UiThreadId 记错线程，
                // 之后同线程发请求会被 PCL 误判成"在 UI 线程执行了网络请求"而抛错。
                PclBootstrap.InitializeOnUiThread();

                ServiceCollection collection = new ServiceCollection();
                ConfigureServices(collection);
                _services = collection.BuildServiceProvider();

                IAppPaths paths = _services.GetRequiredService<IAppPaths>();

                // 改名迁移：老数据在 %APPDATA%\NLCL，新目录不存在时先整体复制（只跑一次）
                // 便携模式的数据在 exe 目录，不应从 %APPDATA% 迁移。
                if (!paths.IsPortable)
                {
                    LegacyDataMigration.TryMigrate(paths.DataRoot);
                }

                paths.EnsureDirectories();
                Services.CrashReporter.LogDirectory =
                    System.IO.Path.Combine(paths.DataRoot, "logs");
                Services.StartupTrace.LogDirectory =
                    System.IO.Path.Combine(paths.DataRoot, "logs");

                // 把"哪个构建 / 谁 / 能不能写"落盘：用于区分"跑的是旧 exe"和"新 exe 写盘失败"
                Services.StartupTrace.Write(null);

                // 首次使用引导：先让用户选目录 / 看 Java / 登录，再进主界面
                try
                {
                    AppSettings existingSettings = _services.GetRequiredService<IAppSettingsStore>()
                        .LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

                    if (!existingSettings.WizardCompleted)
                    {
                        // 关键：此时主窗口还没创建，ShutdownMode=OnMainWindowClose 会退化成
                        // "最后一个窗口关闭即退出"——引导一关，进程就没了，主界面永远不出现。
                        ShutdownMode previousMode = ShutdownMode;
                        ShutdownMode = ShutdownMode.OnExplicitShutdown;

                        try
                        {
                            Views.WizardWindow wizard = _services.GetRequiredService<Views.WizardWindow>();
                            wizard.ShowDialog();
                        }
                        finally
                        {
                            ShutdownMode = previousMode;
                        }
                    }
                }
                catch (Exception wizardError)
                {
                    // 引导自身失败不能拦住启动
                    Services.StartupTrace.Write(wizardError);
                }


                MainWindow window = _services.GetRequiredService<MainWindow>();
                MainWindow = window;

                // 标题栏带构建时间：一眼就能确认在测哪个版本
                window.Title = "NLMCL — 构建 " + Services.StartupTrace.BuildStamp();

                window.Show();
            }
            catch (Exception ex)
            {
                Services.StartupTrace.Write(ex);

                // 启动阶段失败（例如 %APPDATA% 不可写）至少要告诉用户为什么，而不是闪退。
                Services.CrashReporter.ShowFatal("启动失败", ex, canContinue: false);
                Shutdown(1);
            }
        }

        /// <summary>UI 线程上的异常：记日志、告知用户，并拦下来不让进程退出。</summary>
        /// <summary>
        /// 同一个异常的重复弹窗要抑制：XAML 模板绑定失败会在每次重绘时再次抛，
        /// 曾经一次渲染失败就叠出十几层「NLMCL 遇到问题」。
        /// </summary>
        private static readonly HashSet<string> ReportedUiErrors = new HashSet<string>();

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string key = e.Exception.GetType().FullName + "|" + e.Exception.Message;

            Services.CrashReporter.Write("界面线程发生未处理异常", e.Exception);

            if (ReportedUiErrors.Add(key))
            {
                Services.CrashReporter.ShowFatal("界面线程发生未处理异常", e.Exception, canContinue: true);
            }

            e.Handled = true;
        }
        /// <summary>后台线程上的异常：.NET Framework 无法阻止退出，至少留下日志与提示。</summary>
        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Services.CrashReporter.ShowFatal(
                "后台线程发生未处理异常", e.ExceptionObject as Exception, canContinue: false);
        }

        /// <summary>没人观察的任务异常：只记日志，不打扰用户。</summary>
        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Services.CrashReporter.Write("未观察的异步任务异常", e.Exception);
            e.SetObserved();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // 自有核心
            services.AddSingleton<IAppPaths, AppPaths>();
            // 安全模式租约：本次启动把 mods 改成 .disabled，退出/崩溃后按 journal 恢复。
            services.AddSingleton<ISafeModeService>(sp =>
                new NLMCL.Core.SafeMode.SafeModeService(sp.GetRequiredService<IAppPaths>().DataRoot));
            services.AddSingleton<IAccountStore, JsonAccountStore>();
            services.AddSingleton<IInstanceSettingsStore, JsonInstanceSettingsStore>();
            services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
            services.AddSingleton<IAppSettingsService, AppSettingsService>();
            services.AddSingleton<ISettingsTransferService, SettingsTransferService>();
            services.AddSingleton<ICacheCleaner, CacheCleaner>();
            services.AddSingleton<IGameDirectoryScanner, GameDirectoryScanner>();
            services.AddSingleton<IVersionDetailsService, VersionDetailsService>();
            services.AddSingleton<IInstancePackageService, NLMCL.Core.Instances.InstancePackageService>();
            services.AddSingleton<IModpackUpdateService, NLMCL.Core.Installing.ModpackUpdateService>();
            services.AddSingleton<ILauncherUpdateService>(sp => new NLMCL.Core.Updates.LauncherUpdateService(
                sp.GetRequiredService<IDownloadService>(),
                typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"));
            services.AddSingleton<IInstanceContentService, NLMCL.Core.Instances.InstanceContentService>();
            services.AddSingleton<ILocalNetworkInfo, LocalNetworkInfo>();
            services.AddSingleton<IClipboard, Services.WpfClipboard>();
            services.AddSingleton<IFolderPicker, Services.WinFormsFolderPicker>();
            services.AddSingleton<IFilePicker, Services.WinFormsFilePicker>();
            services.AddSingleton<IFileSavePicker, Services.WinFormsSaveFilePicker>();
            // ViewModel 不引用 WPF，需要封送到 UI 线程时走这个端口
            services.AddSingleton<IUiDispatcher, Services.WpfUiDispatcher>();
            // 主题 / 缩放：同样只在 App 层碰 WPF
            services.AddSingleton<IAppearance, Services.AppearanceService>();
            // 微软登录：client id 优先级 设置 > 环境变量 > 内置公共 id（开箱可用）
            // 外面包一层 OAuthDownloadService：OAuth/Xbox 的错误是 4xx + 正文，
            // 不把正文放回来，认证器里的 authorization_pending / XErr 分支就是死代码。
            services.AddSingleton<IMicrosoftAuthenticator>(sp => new MicrosoftAuthenticator(
                new OAuthDownloadService(sp.GetRequiredService<IDownloadService>()),
                ResolveMicrosoftClientId(sp.GetRequiredService<IAppSettingsStore>())));

            // 外置登录（MUA 联合认证 / Blessing Skin 皮肤站）：Yggdrasil 协议。
            // 同样需要把 4xx 的正文放回来，否则"密码错误"只会显示成一串异常。
            services.AddSingleton<IExternalAuthenticator>(sp => new YggdrasilAuthenticator(
                new OAuthDownloadService(sp.GetRequiredService<IDownloadService>())));
            services.AddSingleton<IAuthlibInjector, AuthlibInjectorProvider>();
            services.AddSingleton<ISnapshotService, SnapshotService>();
            services.AddSingleton<IResourceCatalog, ModrinthCatalog>();
            services.AddSingleton<ICurseForgeInstaller, CurseForgeInstaller>();
            services.AddSingleton<CurseForgeCatalog>();
            services.AddSingleton<IResourceInstaller, ResourceInstaller>();
            // 中文查询兜底翻译（Modrinth 只有英文元数据）
            services.AddSingleton<ITranslator, MyMemoryTranslator>();
            services.AddSingleton<ISkinService, SkinService>();
            services.AddSingleton<IMuaSkinLibrary, MuaSkinLibraryService>();
            services.AddSingleton<IProfileTextureUploader, ProfileTextureUploader>();
            services.AddSingleton<IServerBookmarkStore, Core.Servers.ServerBookmarkStore>();
            services.AddSingleton<IModManager, Core.Mods.ModManager>();
        services.AddSingleton<IInstancePathResolver, PclInstancePathResolver>();
            services.AddSingleton<IVersionRepairer, Core.Repair.VersionRepairer>();
            services.AddSingleton<ICrashAnalyzer, Core.Crash.CrashAnalyzer>();
            services.AddSingleton<IVersionManager, Core.Versions.VersionManager>();
            services.AddSingleton<IModUpdateChecker>(sp => new Core.Mods.CompositeModUpdateChecker(
                new Core.Mods.ModrinthUpdateChecker(sp.GetRequiredService<IDownloadService>()),
                new Core.Mods.CurseForgeUpdateChecker(sp.GetRequiredService<IDownloadService>())));
            services.AddSingleton<IServerPinger, Core.Lan.ServerListPing>();
            services.AddSingleton<IServerAddressResolver, Core.Servers.SrvResolver>();
            services.AddSingleton<IServerProfileStore, Core.Servers.ServerProfileStore>();
            services.AddSingleton<Core.Servers.ServerProfilePicker>();
            services.AddSingleton<ILanScanner, Core.Lan.LanScanner>();
            services.AddSingleton<IDialogService, Services.MessageBoxDialogService>();

            // PCL 适配层（VB 程序集只在这里被引用）
            services.AddSingleton<IVersionCatalog, PclVersionCatalog>();
            services.AddSingleton<PclDownloadService>();

            // Mojang 域名优先走 BMCLAPI 镜像，失败自动回退官方：
            // 安装器/Java 下载/版本列表都只认 IDownloadService，因此这里包一层即可
            services.AddSingleton<MirroredDownloadService>(sp =>
                new MirroredDownloadService(sp.GetRequiredService<PclDownloadService>()));
            services.AddSingleton<IDownloadService>(sp => sp.GetRequiredService<MirroredDownloadService>());
            services.AddSingleton<IMirrorSwitch>(sp => sp.GetRequiredService<MirroredDownloadService>());
            // 安装编排（§2.1a 豁免项：只依赖端口，实现见 NLMCL.Core）
            services.AddSingleton(new InstallOptions());
            services.AddSingleton<VanillaVersionInstaller>();
            services.AddSingleton<FabricLoaderInstaller>();
            services.AddSingleton<IVersionInstaller>(sp => new CompositeVersionInstaller(
                sp.GetRequiredService<VanillaVersionInstaller>(),
                sp.GetRequiredService<FabricLoaderInstaller>()));
            services.AddSingleton<IVersionListProvider, MojangVersionListProvider>();
            services.AddSingleton<IFabricLoaderCatalog, FabricLoaderVersionCatalog>();
            services.AddSingleton<IForgeCatalog, ForgeCatalog>();
            services.AddSingleton<INeoForgeCatalog, NeoForgeCatalog>();
            services.AddSingleton<ILoaderInstaller, ForgeLoaderInstaller>();
            services.AddSingleton<IProcessRunner, Services.ProcessRunner>();
            services.AddSingleton<IFileDownloader, HttpFileDownloader>();
            services.AddSingleton<IModpackInstaller, ModpackInstaller>();
            services.AddSingleton<IDownloadTuning, PclDownloadTuning>();
            services.AddSingleton<IJavaLocator, PclJavaLocator>();
            services.AddSingleton<IJavaRuntimeInstaller, JavaRuntimeInstaller>();
            services.AddSingleton<IDiagnosticPackageService, DiagnosticPackageService>();
            services.AddSingleton<IGameLauncher>(provider => new PclGameLauncher
            {
                LogDirectory = System.IO.Path.Combine(
                    provider.GetRequiredService<IAppPaths>().DataRoot, "logs"),
            });
            services.AddSingleton<IGameSessionLauncher>(provider => new PclGameSessionLauncher
            {
                LogDirectory = System.IO.Path.Combine(
                    provider.GetRequiredService<IAppPaths>().DataRoot, "logs"),
            });

            // 展示层
            services.AddSingleton<IDownloadJobStore, JsonDownloadJobStore>();
            services.AddSingleton<DownloadQueue>();
            services.AddSingleton<DownloadViewModel>();
            services.AddSingleton<ResourceViewModel>();
            services.AddSingleton<SkinViewModel>();
            services.AddSingleton<LanScanViewModel>();
            services.AddSingleton<ModsViewModel>();
            services.AddTransient<WizardViewModel>();
            services.AddSingleton<LaunchConsoleViewModel>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();
            services.AddTransient<Views.WizardWindow>();
        }

        /// <summary>
        /// 微软 client id 的优先级：设置里填的 &gt; 环境变量 &gt; 内置公共 id。
        /// 这样新用户开箱可用，团队也能随时换成自己注册的应用。
        /// </summary>
        private static string ResolveMicrosoftClientId(IAppSettingsStore store)
        {
            AppSettings? settings = null;

            try
            {
                settings = store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // 读不到设置不影响登录：退回环境变量/内置 id
            }

            if (!string.IsNullOrWhiteSpace(settings?.MicrosoftClientId))
            {
                return settings!.MicrosoftClientId!.Trim();
            }

            string? fromEnvironment = Environment.GetEnvironmentVariable("NLMCL_MS_CLIENT_ID");

            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment.Trim();
            }

            return MicrosoftAuthenticator.DefaultClientId;
        }

        /// <summary>把已经在运行的 NLMCL 窗口恢复并置前；找不到窗口时返回 false。</summary>
        private static bool TryActivateExistingInstance()
        {
            try
            {
                Process current = Process.GetCurrentProcess();

                foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                {
                    if (process.Id == current.Id)
                    {
                        continue;
                    }

                    IntPtr handle = process.MainWindowHandle;

                    if (handle == IntPtr.Zero)
                    {
                        process.Refresh();
                        handle = process.MainWindowHandle;
                    }

                    if (handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    ShowWindow(handle, SwRestore);
                    SetForegroundWindow(handle);
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // 进程刚好退出：交给下面的提示。
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            return false;
        }

        private const int SwRestore = 9;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        protected override void OnExit(ExitEventArgs e)
        {
            if (_services != null)
            {
                try
                {
                    if (_services.GetService(typeof(SkinViewModel)) is SkinViewModel skin)
                    {
                        skin.FlushUsage();
                    }

                    if (_services.GetService(typeof(LaunchConsoleViewModel)) is LaunchConsoleViewModel console)
                    {
                        console.Dispose();
                    }
                }
                catch (Exception)
                {
                    // 使用时长只是辅助数据，退出时不能阻止进程关闭。
                }

                _services.Dispose();
            }

            if (_ownsSingleInstance)
            {
                try
                {
                    _singleInstance?.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 锁已由系统释放，忽略。
                }
            }

            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
