using System;
using System.Collections.Generic;

namespace NLMCL.App.ViewModels
{
    /// <summary>顶层页面的显示名、持久化 key 与 ShellSection 之间的唯一映射。</summary>
    internal static class PageNavigation
    {
        public static IReadOnlyList<string> DefaultOptions { get; }
            = new[] { "上次页面", "世界库", "世界", "下载中心", "联机", "方块宁理", "快照", "皮肤", "日志", "设置" };

        public static string ToKey(string? text)
        {
            switch (text)
            {
                case "上次页面":
                    return "last";
                case "世界":
                    return "world";
                case "下载中心":
                    return "download";
                case "联机":
                case "联机房间":
                    return "multiplayer";
                case "方块宁理":
                    return "ningli";
                case "快照":
                    return "snapshot";
                case "皮肤":
                    return "skin";
                case "日志":
                    return "console";
                case "设置":
                    return "settings";
                default:
                    return "versions";
            }
        }

        public static string ToText(string? key)
        {
            switch (key)
            {
                case "last":
                    return "上次页面";
                case "world":
                    return "世界";
                case "download":
                    return "下载中心";
                case "multiplayer":
                    return "联机";
                case "ningli":
                    return "方块宁理";
                case "snapshot":
                    return "快照";
                case "skin":
                    return "皮肤";
                case "console":
                    return "日志";
                case "settings":
                    return "设置";
                default:
                    return "世界库";
            }
        }

        public static bool TryGetKey(ShellSection section, out string key)
        {
            switch (section)
            {
                case ShellSection.Versions:
                    key = "versions";
                    return true;
                case ShellSection.World:
                    key = "world";
                    return true;
                case ShellSection.Download:
                    key = "download";
                    return true;
                case ShellSection.Multiplayer:
                    key = "multiplayer";
                    return true;
                case ShellSection.Ningli:
                    key = "ningli";
                    return true;
                case ShellSection.Snapshot:
                    key = "snapshot";
                    return true;
                case ShellSection.Skin:
                    key = "skin";
                    return true;
                case ShellSection.Launch:
                    key = "console";
                    return true;
                case ShellSection.Settings:
                    key = "settings";
                    return true;
                default:
                    key = string.Empty;
                    return false;
            }
        }

        public static ShellSection ToSection(string? key)
        {
            switch (key)
            {
                case "world":
                    return ShellSection.World;
                case "download":
                case "import":
                case "mods":
                    return ShellSection.Download;
                case "multiplayer":
                case "lanscan":
                    return ShellSection.Multiplayer;
                case "ningli":
                    return ShellSection.Ningli;
                case "snapshot":
                    return ShellSection.Snapshot;
                case "skin":
                    return ShellSection.Skin;
                case "console":
                    return ShellSection.Launch;
                case "settings":
                    return ShellSection.Settings;
                default:
                    return ShellSection.Versions;
            }
        }
    }
}
