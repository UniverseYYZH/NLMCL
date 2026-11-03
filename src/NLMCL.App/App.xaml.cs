using System;
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

namespace NLMCL.App
{
    /// <summary>
    /// 组合根（Composition Root）：唯一知道「哪个接口用哪个实现」的地方。
    /// 换后端只改这里，UI 与业务层不动。
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider? _services;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
                LegacyDataMigration.TryMigrate(paths.DataRoot);

                paths.EnsureDirectories();

                // 把"哪个构建 / 谁 / 能不能写"落盘：用于区分"跑的是旧 exe"和"新 exe 写盘失败"
                Services.StartupTrace.Write(null);

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
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Services.CrashReporter.ShowFatal("界面线程发生未处理异常", e.Exception, canContinue: true);
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
            services.AddSingleton<IAccountStore, JsonAccountStore>();
            services.AddSingleton<IInstanceSettingsStore, JsonInstanceSettingsStore>();
            services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
            services.AddSingleton<ILocalNetworkInfo, LocalNetworkInfo>();
            services.AddSingleton<IClipboard, Services.WpfClipboard>();
            services.AddSingleton<IFolderPicker, Services.WinFormsFolderPicker>();
            services.AddSingleton<IFilePicker, Services.WinFormsFilePicker>();
            // ViewModel 不引用 WPF，需要封送到 UI 线程时走这个端口
            services.AddSingleton<IUiDispatcher, Services.WpfUiDispatcher>();
            // 主题 / 缩放：同样只在 App 层碰 WPF
            services.AddSingleton<IAppearance, Services.AppearanceService>();
            // 微软登录：client_id 不内置（与 PCL 一致），从环境变量读。
            // 外面包一层 OAuthDownloadService：OAuth/Xbox 的错误是 4xx + 正文，
            // 不把正文放回来，认证器里的 authorization_pending / XErr 分支就是死代码。
            services.AddSingleton<IMicrosoftAuthenticator>(sp => new MicrosoftAuthenticator(
                new OAuthDownloadService(sp.GetRequiredService<IDownloadService>()),
                Environment.GetEnvironmentVariable("NLMCL_MS_CLIENT_ID")));

            // 外置登录（MUA 联合认证 / Blessing Skin 皮肤站）：Yggdrasil 协议。
            // 同样需要把 4xx 的正文放回来，否则"密码错误"只会显示成一串异常。
            services.AddSingleton<IExternalAuthenticator>(sp => new YggdrasilAuthenticator(
                new OAuthDownloadService(sp.GetRequiredService<IDownloadService>())));
            services.AddSingleton<IAuthlibInjector, AuthlibInjectorProvider>();
            services.AddSingleton<ISnapshotService, SnapshotService>();
            services.AddSingleton<IResourceCatalog, ModrinthCatalog>();
            services.AddSingleton<IResourceInstaller, ResourceInstaller>();
            // 中文查询兜底翻译（Modrinth 只有英文元数据）
            services.AddSingleton<ITranslator, MyMemoryTranslator>();
            services.AddSingleton<ISkinService, SkinService>();
            services.AddSingleton<IServerBookmarkStore, Core.Servers.ServerBookmarkStore>();
            services.AddSingleton<IModManager, Core.Mods.ModManager>();
            services.AddSingleton<IModUpdateChecker, Core.Mods.ModrinthUpdateChecker>();
            services.AddSingleton<IServerPinger, Core.Lan.ServerListPing>();
            services.AddSingleton<ILanScanner, Core.Lan.LanScanner>();
            services.AddSingleton<IDialogService, Services.MessageBoxDialogService>();

            // PCL 适配层（VB 程序集只在这里被引用）
            services.AddSingleton<IVersionCatalog, PclVersionCatalog>();
            services.AddSingleton<PclDownloadService>();

            // Mojang 域名优先走 BMCLAPI 镜像，失败自动回退官方：
            // 安装器/Java 下载/版本列表都只认 IDownloadService，因此这里包一层即可
            services.AddSingleton<IDownloadService>(sp =>
                new MirroredDownloadService(sp.GetRequiredService<PclDownloadService>()));

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
            services.AddSingleton<IGameLauncher, PclGameLauncher>();
            services.AddSingleton<IGameSessionLauncher, PclGameSessionLauncher>();

            // 展示层
            services.AddSingleton<DownloadViewModel>();
            services.AddSingleton<ResourceViewModel>();
            services.AddSingleton<SkinViewModel>();
            services.AddSingleton<LanScanViewModel>();
            services.AddSingleton<ModsViewModel>();
            services.AddSingleton<LaunchConsoleViewModel>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_services != null)
            {
                _services.Dispose();
            }

            base.OnExit(e);
        }
    }
}
