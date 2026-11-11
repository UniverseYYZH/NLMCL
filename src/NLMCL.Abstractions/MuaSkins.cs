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
            string previewUrl,
            int uploaderId = 0)
        {
            Tid = tid;
            Name = name;
            Model = model;
            Uploader = uploader;
            Likes = likes;
            DownloadUrl = downloadUrl;
            PreviewUrl = previewUrl;
            UploaderId = uploaderId;
        }

        public int Tid { get; }

        public string Name { get; }

        /// <summary>default（经典）或 slim（纤细）。</summary>
        public string Model { get; }

        public string Uploader { get; }

        public int Likes { get; }

        public string DownloadUrl { get; }

        public string PreviewUrl { get; }

        /// <summary>上传者数字 ID；皮肤站详情页或列表没有提供时为 0。</summary>
        public int UploaderId { get; }
    }

    public enum MuaSkinSort
    {
        Newest = 0,
        MostFavorited = 1,
        Name = 2
    }

    /// <summary>MUA 皮肤库查询条件。翻页时复用同一个实例即可。</summary>
    public sealed class MuaSkinQuery
    {
        public int Page { get; set; } = 1;

        /// <summary>名称关键词；为空表示不按名称过滤。</summary>
        public string? Keyword { get; set; }

        /// <summary>上传者数字 ID；为空表示不按作者过滤。</summary>
        public int? UploaderId { get; set; }

        /// <summary>指定皮肤 ID；大于 0 时直接取这一张。</summary>
        public int? Tid { get; set; }

        /// <summary>default / slim；空表示全部。</summary>
        public string? Model { get; set; }

        public MuaSkinSort Sort { get; set; } = MuaSkinSort.Newest;

        public bool Descending { get; set; } = true;

        /// <summary>上一页响应里的总页数，用于升序时反向取页。</summary>
        public int KnownLastPage { get; set; }
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

        /// <summary>按名称 / 作者 / 皮肤 ID / 类型 / 排序查询公开皮肤库。</summary>
        Task<MuaSkinPage> QueryAsync(
            string siteRoot, MuaSkinQuery query, CancellationToken cancellationToken);

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
