using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;

namespace NLMCL.Tests
{
    /// <summary>
    /// 下载端口的测试基类：默认全部抛"未使用"，具体测试只覆写自己关心的那几个。
    /// 接口一长，每个假件都实现 5 个方法很快就淹没测试意图了。
    /// </summary>
    internal class DownloadServiceStub : IDownloadService
    {
        public virtual Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("本测试未预期下载文件：" + url);
        }

        public virtual Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("本测试未预期 GET：" + url);
        }

        public virtual Task<string> GetStringAsync(string url,
            IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("本测试未预期带头的 GET：" + url);
        }

        public virtual Task<string> PostJsonAsync(string url, string jsonBody,
            IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("本测试未预期 POST JSON：" + url);
        }

        public virtual Task<string> PostFormAsync(string url,
            IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("本测试未预期 POST 表单：" + url);
        }
    }

    /// <summary>同步执行的封送器：单测里没有消息泵，投递即执行。</summary>
    internal sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool IsOnUiThread => true;

        public void Post(Action action) => action();
    }

    /// <summary>
    /// 把投递排队的封送器：用来验证"后台线程的事件确实被推迟到 UI 线程执行"。
    /// </summary>
    internal sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _queue = new Queue<Action>();
        private readonly object _gate = new object();

        public bool IsOnUiThread => false;

        public int Pending
        {
            get
            {
                lock (_gate)
                {
                    return _queue.Count;
                }
            }
        }

        public void Post(Action action)
        {
            lock (_gate)
            {
                _queue.Enqueue(action);
            }
        }

        /// <summary>模拟 Dispatcher 跑完排队的消息。</summary>
        public void Drain()
        {
            while (true)
            {
                Action? next;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        return;
                    }

                    next = _queue.Dequeue();
                }

                next();
            }
        }
    }

    /// <summary>测试用的零件工厂：避免每个测试都重复实现依赖。</summary>
    internal static class TestSupport
    {
        /// <summary>1×1 透明 PNG，用于不需要真实皮肤的预览测试替身。</summary>
        internal static readonly byte[] OnePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        private static readonly List<string> TemporaryDirectories = new List<string>();
        private static readonly DateTime RunStartedUtc = DateTime.UtcNow;

        static TestSupport()
        {
            CleanupStaleTemporaryDirectories();
            AppDomain.CurrentDomain.ProcessExit += (_, __) => CleanupTemporaryDirectories();
        }

        /// <summary>清掉上次/崩溃测试留下的旧夹具，避免 %TEMP% 越积越多。</summary>
        private static void CleanupStaleTemporaryDirectories()
        {
            string temp = Path.GetTempPath();
            DateTime cutoff = DateTime.UtcNow.AddHours(-12);

            try
            {
                foreach (string path in Directory.EnumerateDirectories(temp, "nlmcl-*"))
                {
                    try
                    {
                        if (Directory.GetLastWriteTimeUtc(path) >= cutoff)
                        {
                            continue;
                        }

                        Directory.Delete(path, true);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        internal static string CreateTemporaryDirectory(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            TemporaryDirectories.Add(path);
            return path;
        }

        internal static void CleanupTemporaryDirectories()
        {
            foreach (string path in TemporaryDirectories.ToArray())
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            TemporaryDirectories.Clear();

            // 很多历史用例直接拼 %TEMP%\nlmcl-*，没有登记到上面的列表；
            // 退出时把本轮开始后新建的同类目录一并收掉。
            string temp = Path.GetTempPath();
            DateTime cutoff = RunStartedUtc.AddMinutes(-10);
            try
            {
                foreach (string path in Directory.EnumerateDirectories(temp, "nlmcl-*"))
                {
                    try
                    {
                        if (Directory.GetLastWriteTimeUtc(path) >= cutoff)
                        {
                            Directory.Delete(path, true);
                        }
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        internal sealed class EmptyCatalog : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<MinecraftVersion>>(Array.Empty<MinecraftVersion>());
            }
        }

        internal sealed class EmptyProvider : IVersionListProvider
        {
            public Task<IReadOnlyList<VanillaVersion>> GetVersionsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<VanillaVersion>>(Array.Empty<VanillaVersion>());
            }
        }

        internal sealed class NoopInstaller : IVersionInstaller
        {
            public Task InstallAsync(InstallRequest request, IProgress<InstallProgress> progress,
                CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        internal sealed class NoopLauncher : IGameLauncher
        {
            public Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new LaunchResult(0, true, "stub"));
            }
        }

        internal sealed class NoopJavaLocator : IJavaLocator
        {
            public Task<IReadOnlyList<JavaRuntime>> FindInstalledAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<JavaRuntime>>(Array.Empty<JavaRuntime>());
            }
        }

        /// <summary>不会真的起进程的会话启动器。</summary>
        internal sealed class NoopSessionLauncher : IGameSessionLauncher
        {
            public Task<IGameSession> StartAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                throw new NotSupportedException("测试不应真的启动会话");
            }
        }

        /// <summary>给只需要 MainViewModel 的测试用的最小下载页视图模型。</summary>
        internal static DownloadViewModel CreateDownload(IAppPaths paths, DownloadQueue? queue = null)
        {
            return new DownloadViewModel(new EmptyProvider(), new NoopInstaller(), paths, queue: queue);
        }

        internal static LaunchConsoleViewModel CreateConsole(IAppPaths paths, IUiDispatcher? dispatcher = null)
        {
            return new LaunchConsoleViewModel(
                new NoopSessionLauncher(), new NoopJavaLocator(), paths,
                dispatcher ?? new ImmediateDispatcher());
        }

        /// <summary>内存账号存储，避免测试碰真实文件。</summary>
        internal sealed class MemoryAccountStore : IAccountStore
        {
            public Account? Current { get; set; }

            public Task<Account?> LoadAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(Current);
            }

            public Task SaveAsync(Account account, CancellationToken cancellationToken)
            {
                Current = account;
                return Task.CompletedTask;
            }

            public Task SignOutAsync(CancellationToken cancellationToken)
            {
                Current = null;
                return Task.CompletedTask;
            }

            public Task ClearAsync(CancellationToken cancellationToken)
            {
                Current = null;
                return Task.CompletedTask;
            }

            public System.Collections.Generic.List<Account> All { get; } = new();

            public Task<System.Collections.Generic.IReadOnlyList<Account>> LoadAllAsync(
                CancellationToken cancellationToken)
            {
                var list = new System.Collections.Generic.List<Account>();

                if (Current != null)
                {
                    list.Add(Current);
                }

                list.AddRange(All.Where(item => !ReferenceEquals(item, Current)));

                return Task.FromResult<System.Collections.Generic.IReadOnlyList<Account>>(list);
            }

            /// <summary>模拟账号文件被只读目录/安全软件占用，切换无法持久化。</summary>
            public bool FailSwitch { get; set; }

            public Task SwitchAsync(Account account, CancellationToken cancellationToken)
            {
                if (FailSwitch)
                {
                    throw new UnauthorizedAccessException("accounts.json.tmp 写入被拒绝");
                }

                Current = account;
                return Task.CompletedTask;
            }

            public Task RemoveAsync(Account account, CancellationToken cancellationToken)
            {
                All.RemoveAll(item => string.Equals(item.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));

                if (Current != null && string.Equals(Current.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
                {
                    Current = All.FirstOrDefault();
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>不会真的联网的微软认证器。</summary>
        internal sealed class NoopAuthenticator : IMicrosoftAuthenticator
        {
            public string? ClientId { get; set; }

            public Task<DeviceCodeChallenge> StartAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException("测试不应真的发起微软登录");
            }

            public Task<Account> WaitForAccountAsync(DeviceCodeChallenge challenge,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException("测试不应真的发起微软登录");
            }

            public Task<Account> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
            {
                throw new NotSupportedException("测试不应真的续期");
            }
        }

        /// <summary>内存版实例设置存储，避免测试写真实文件。</summary>
        internal sealed class MemoryInstanceSettingsStore : IInstanceSettingsStore
        {
            public Dictionary<string, InstanceSettings> Items { get; } =
                new Dictionary<string, InstanceSettings>(StringComparer.OrdinalIgnoreCase);

            public Task<InstanceSettings> LoadAsync(string minecraftDirectory, string versionId,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(Items.TryGetValue(versionId, out InstanceSettings? value)
                    ? value
                    : new InstanceSettings());
            }

            public Task SaveAsync(string minecraftDirectory, string versionId, InstanceSettings settings,
                CancellationToken cancellationToken)
            {
                Items[versionId] = settings;
                return Task.CompletedTask;
            }

            public bool Exists(string minecraftDirectory, string versionId) => Items.ContainsKey(versionId);

        }

        /// <summary>假的网络信息：地址固定，端口可按需"开/关"。</summary>
        internal sealed class FakeNetwork : ILocalNetworkInfo
        {
            public string Address { get; set; } = "192.168.1.7";

            public bool PortOpen { get; set; } = true;

            public int ProbeCalls { get; private set; }

            public string? GetLocalIPv4() => string.IsNullOrEmpty(Address) ? null : Address;

            public Task<bool> IsPortOpenAsync(string host, int port, CancellationToken cancellationToken)
            {
                ProbeCalls++;
                return Task.FromResult(PortOpen);
            }
        }

        internal sealed class FakeClipboard : IClipboard
        {
            public string Text { get; private set; } = string.Empty;

            public void SetText(string text) => Text = text;
        }

        /// <summary>假文件选择器：返回预设路径，null 表示用户取消。</summary>
        internal sealed class FakeFolderPicker : IFolderPicker
        {
            public string? Next { get; set; }

            public string? PickFolder(string description, string? initialDirectory) => Next;
        }

        /// <summary>内存版应用设置。</summary>
        internal sealed class FakeAppSettings : IAppSettingsStore
        {
            public AppSettings Current { get; set; } = new AppSettings();

            public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
                => Task.FromResult(Current);

            public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
            {
                Current = settings;
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 造一个"真实存在"的 Java：建临时目录并放一个空的 bin\javaw.exe。
        /// 不能用假路径——JavaRequirements.Pick 会过滤掉可执行文件不存在的条目。
        /// </summary>
        internal static JavaRuntime CreateJava(int major)
        {
            string home = CreateTemporaryDirectory("nlmcl-java-");
            Directory.CreateDirectory(Path.Combine(home, "bin"));
            File.WriteAllText(Path.Combine(home, "bin", "javaw.exe"), "");
            return new JavaRuntime(home + Path.DirectorySeparatorChar, new Version(major, 0));
        }

        /// <summary>组装一个只依赖目录的主窗口视图模型。</summary>
        internal static MainViewModel CreateMain(
            IVersionCatalog catalog,
            IAppPaths paths,
            IJavaLocator? javaLocator = null,
            IGameSessionLauncher? sessionLauncher = null,
            ISnapshotService? snapshotService = null,
            IDialogService? dialogs = null,
            IAppearance? appearance = null,
            IAppSettingsStore? appSettings = null,
            ResourceViewModel? resource = null,
            IServerBookmarkStore? bookmarks = null,
            IServerProfileStore? profileStore = null,
            IAccountStore? accounts = null,
            ISkinService? skins = null,
            SkinViewModel? skin = null,
            IFolderPicker? folderPicker = null,
            IVersionDetailsService? versionDetails = null,
            IFilePicker? filePicker = null,
            LanScanViewModel? lanScan = null,
            DownloadViewModel? download = null,
            IGameLauncher? launcher = null,
            NLMCL.Core.Servers.ServerProfilePicker? profilePicker = null,
            IVersionInstaller? versionInstaller = null,
            IFileDownloader? fileDownloader = null,
            IResourceCatalog? resourceCatalog = null,
            IInstancePathResolver? instancePaths = null,
            ISafeModeService? safeMode = null,
            IVersionRepairer? repairer = null,
            IModpackUpdateService? modpackUpdates = null,
            IModpackInstaller? modpackInstaller = null)
        {
            return new MainViewModel(
                catalog,
                paths,
                download ?? CreateDownload(paths),
                CreateConsole(paths),
                launcher ?? new NoopLauncher(),
                javaLocator ?? new NoopJavaLocator(),
                accounts ?? new MemoryAccountStore(),
                new NoopAuthenticator(),
                new MemoryInstanceSettingsStore(),
                new FakeNetwork(),
                new FakeClipboard(),
                (folderPicker ?? new FakeFolderPicker()),
                (appSettings ?? new FakeAppSettings()),
                javaInstaller: null,
                sessionLauncher: sessionLauncher,
                snapshotService: snapshotService,
                dialogs: dialogs,
                appearance: appearance,
                resource: resource,
                bookmarks: bookmarks,
                profileStore: profileStore,
                skins: skins,
                skin: skin,
                versionDetails: versionDetails,
                filePicker: filePicker,
                lanScan: lanScan,
                profilePicker: profilePicker,
                versionInstaller: versionInstaller,
                fileDownloader: fileDownloader,
                resourceCatalog: resourceCatalog,
                instancePaths: instancePaths,
                safeMode: safeMode,
                repairer: repairer,
                modpackUpdates: modpackUpdates,
                modpackInstaller: modpackInstaller);
        }
        internal sealed class NoopResourceCatalog : IResourceCatalog
        {
            public Task<IReadOnlyList<ResourceSummary>> SearchAsync(
                ResourceQuery query, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceSummary>>(new[]
                {
                    new ResourceSummary("AANobbMI", "sodium", "Sodium", "Modern rendering engine",
                        "https://cdn.modrinth.com/sodium.png", "jellysquid3", 123456789, ResourceKind.Mod),
                    new ResourceSummary("YL57xq9U", "iris", "Iris Shaders", "Shaders",
                        null, "coderbot", 5000, ResourceKind.Mod)
                });

            public Task<IReadOnlyList<ResourceFile>> GetFilesAsync(
                string projectId, ResourceKind kind, string? gameVersion, string? loader,
                CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceFile>>(Array.Empty<ResourceFile>());
        }

        internal sealed class NoopResourceInstaller : IResourceInstaller
        {
            public Task<string> InstallAsync(
                ResourceFile file, ResourceKind kind, string minecraftDirectory, string versionFolder,
                CancellationToken cancellationToken)
                => Task.FromResult(Path.Combine(minecraftDirectory, "mods", file.Name));
        }

        internal sealed class NoopSkinService : ISkinService
        {
            public Func<Account, Task<string?>>? AvatarHandler { get; set; }

            public Func<Account, Task<byte[]?>>? AvatarPngHandler { get; set; }

            private static string DefaultSkinPath =>
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");

            public Task<SkinInfo?> GetAsync(Account account, CancellationToken cancellationToken)
                => Task.FromResult<SkinInfo?>(null);

            public Task<string> SaveAsync(SkinInfo skin, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);

            public Task SavePreviewAsync(string url, string targetPath, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task<string> ImportLocalAsync(
                string sourcePath, string playerName, CancellationToken cancellationToken)
                => Task.FromResult(sourcePath);

            public Task<string> ImportLocalAsync(
                Account account, string sourcePath, CancellationToken cancellationToken)
                => Task.FromResult(sourcePath);

            public Task<string?> GetLocalSkinPathAsync(
                Account account, CancellationToken cancellationToken)
                => Task.FromResult<string?>(null);

            public Task<byte[]> Create3DPreviewAsync(
                string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
                CancellationToken cancellationToken)
                => Task.FromResult(OnePixelPng);

            public Task<string?> GetAvatarAsync(Account account, CancellationToken cancellationToken)
                => AvatarHandler == null
                    ? Task.FromResult<string?>(null)
                    : AvatarHandler(account);

            public Task<byte[]?> GetAvatarPngAsync(Account account, CancellationToken cancellationToken)
                => AvatarPngHandler == null
                    ? Task.FromResult<byte[]?>(null)
                    : AvatarPngHandler(account);

            public string GetDefaultSkinPath() => DefaultSkinPath;

            public Task<string> GetDefaultSkinAsync(CancellationToken cancellationToken)
                => Task.FromResult(DefaultSkinPath);

            public Task<SkinInfo?> LookupByNameAsync(string playerName, CancellationToken cancellationToken)
                => Task.FromResult<SkinInfo?>(null);

            public IReadOnlyList<SkinPreset> GetPresets() => Array.Empty<SkinPreset>();

            public string GetPresetPath(string presetId) => string.Empty;

            public Task<string> SavePresetAsync(
                string presetId, string playerName, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
        }

    }
}
