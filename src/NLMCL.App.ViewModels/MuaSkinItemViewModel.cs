using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>MUA 皮肤库列表的一行。</summary>
    public sealed class MuaSkinItemViewModel
    {
        public MuaSkinItemViewModel(MuaSkin skin)
        {
            Skin = skin;
        }

        public MuaSkin Skin { get; }

        public string Name => Skin.Name;

        public string UploaderText => "by " + Skin.Uploader + " · " + ModelLabel;

        public string ModelLabel => Skin.Model == "slim" ? "纤细" : "经典";

        public string PreviewUrl => Skin.PreviewUrl;

        public string LikesText => "♥ " + Skin.Likes;
    }
}
