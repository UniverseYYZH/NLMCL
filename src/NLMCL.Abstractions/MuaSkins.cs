using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>MUA / Blessing Skin 皮肤库的一条材质。</summary>
    public sealed class MuaSkin
    {
        public MuaSkin(
            int tid,
            string name,
            string model,
            string uploader,
            int likes,
            string downloadUrl,
            string previewUrl)
        {
            Tid = tid;
            Name = name;
            Model = model;
            Uploader = uploader;
            Likes = likes;
            DownloadUrl = downloadUrl;
            PreviewUrl = previewUrl;
        }

        public int Tid { get; }

        public string Name { get; }

        /// <summary>default（经典）或 slim（纤细）。</summary>
        public string Model { get; }

        public string Uploader { get; }

        public int Likes { get; }

        public string DownloadUrl { get; }

        public string PreviewUrl { get; }
    }

    /// <summary>皮肤库一页。</summary>
    public sealed class MuaSkinPage
    {
        public MuaSkinPage(IReadOnlyList<MuaSkin> items, int page, int lastPage, int total)
        {
            Items = items;
            Page = page;
            LastPage = lastPage;
            Total = total;
        }

        public IReadOnlyList<MuaSkin> Items { get; }

        public int Page { get; }

        public int LastPage { get; }

        public int Total { get; }
    }

    /// <summary>MUA / Blessing Skin 皮肤库（只读浏览与下载）。</summary>
    public interface IMuaSkinLibrary
    {
        Task<MuaSkinPage> ListAsync(string siteRoot, int page, CancellationToken cancellationToken);

        Task<string> DownloadAsync(MuaSkin skin, string targetPath, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 把一张 PNG 上传为当前角色的皮肤。
    /// 只用于外置登录（Blessing Skin / MUA Yggdrasil）的“一键装配”。
    /// </summary>
    public interface IProfileTextureUploader
    {
        Task UploadSkinAsync(
            Account account, string pngPath, string model, CancellationToken cancellationToken);
    }
}
