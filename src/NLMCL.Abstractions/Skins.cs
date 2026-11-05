using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 内置预设皮肤（经典 Steve / Alex 等），离线玩家可以直接挑一个换外观。
    /// 只影响本地预览与保存的文件，不会改变服务器上的皮肤。
    /// </summary>
    public sealed class SkinPreset
    {
        public SkinPreset(string id, string name, string model, int width)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? string.Empty;
            Model = model ?? "default";
            Width = width;
        }

        public string Id { get; }

        public string Name { get; }

        /// <summary>default（经典）或 slim（纤细）。</summary>
        public string Model { get; }

        /// <summary>原图宽度（64 为现代格式，32 为旧版格式）。</summary>
        public int Width { get; }

        public string Label => Width >= 64 ? Name : Name + "（旧版格式）";
    }

    /// <summary>当前账号的皮肤信息。</summary>
    public sealed class SkinInfo
    {
        public SkinInfo(string playerName, string? skinUrl, string model, string source)
        {
            PlayerName = playerName ?? string.Empty;
            SkinUrl = skinUrl;
            Model = model ?? "default";
            Source = source ?? string.Empty;
        }

        public string PlayerName { get; }

        /// <summary>皮肤 PNG 的直链；没有皮肤时为 null。</summary>
        public string? SkinUrl { get; }

        /// <summary>default（经典）或 slim（纤细）。</summary>
        public string Model { get; }

        /// <summary>皮肤来源（认证服务器地址或 Minecraft Services）。</summary>
        public string Source { get; }

        public string ModelLabel => string.Equals(Model, "slim", StringComparison.OrdinalIgnoreCase)
            ? "纤细（Alex）"
            : "经典（Steve）";

        public string Summary => PlayerName + " · " + ModelLabel;
    }

    /// <summary>
    /// 皮肤读取：向账号所属的认证服务器要 profile，解析 textures 属性里的皮肤地址。
    ///
    /// 注意：皮肤**不是启动器能改的**——它由认证服务器（或 Mojang）按账号下发，
    /// 游戏客户端通过 authlib-injector / 会话服务器拿到。启动器只做预览与保存。
    /// </summary>
    public interface ISkinService
    {
        Task<SkinInfo?> GetAsync(Account account, CancellationToken cancellationToken);

        /// <summary>把皮肤 PNG 存到本地皮肤目录，返回文件路径。</summary>
        Task<string> SaveAsync(SkinInfo skin, CancellationToken cancellationToken);

        /// <summary>把皮肤 PNG 下载到指定路径（预览用）。</summary>
        Task SavePreviewAsync(string url, string targetPath, CancellationToken cancellationToken);

        /// <summary>
        /// 导入本地皮肤文件：校验 PNG 头 → 复制到皮肤目录 → 返回副本路径（供预览）。
        /// 离线账号没有服务器皮肤，用它自娱自乐或做素材检查。
        /// </summary>
        Task<string> ImportLocalAsync(string sourcePath, string playerName, CancellationToken cancellationToken);

        /// <summary>把皮肤 PNG 渲染成纸娃娃立绘（供界面预览），返回渲染后的文件路径。</summary>
        Task<string> CreatePreviewAsync(string skinPngPath, CancellationToken cancellationToken);

        /// <summary>3D 预览：把皮肤渲染成带朝向的立体立绘，返回渲染后的文件路径。</summary>
        Task<string> Create3DPreviewAsync(
            string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
            CancellationToken cancellationToken);

        /// <summary>账号头像（头部正面 + 帽子层），没有皮肤时返回 null。</summary>
        Task<string?> GetAvatarAsync(Account account, CancellationToken cancellationToken);

        /// <summary>内置默认皮肤 PNG 的本地路径（账号没有自定义皮肤时兜底预览）。</summary>
        string GetDefaultSkinPath();

        /// <summary>内置的预设皮肤列表（Steve / Alex 等）。</summary>
        IReadOnlyList<SkinPreset> GetPresets();

        /// <summary>把某个预设皮肤解出到临时目录，返回 PNG 路径（供预览）。</summary>
        string GetPresetPath(string presetId);

        /// <summary>把预设皮肤复制进本地皮肤目录，返回保存后的路径。</summary>
        Task<string> SavePresetAsync(string presetId, string playerName, CancellationToken cancellationToken);
    }
}
