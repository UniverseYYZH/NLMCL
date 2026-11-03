using System;
using System.Collections.Generic;
using System.Linq;

namespace NLMCL.Core.Resources
{
    /// <summary>
    /// 中文名 → 英文名的小映射表。
    ///
    /// Modrinth 只有英文元数据，直接搜「史蒂夫模型」是 0 结果。
    /// 这里覆盖国内玩家常用叫法，搜不到时给一次"按英文名再搜"的机会。
    /// 不追求全，只收录确定对应的；对不上就按原词搜。
    /// </summary>
    public static class ChineseAliases
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["是！史蒂夫模型"] = "Yes Steve Model",
            ["史蒂夫模型"] = "Yes Steve Model",
            ["yes steve model"] = "Yes Steve Model",

            ["钠"] = "Sodium",
            ["钠优化"] = "Sodium",
            ["锂"] = "Lithium",
            ["磷"] = "Phosphor",
            ["铱"] = "Iris Shaders",
            ["钠光影"] = "Iris Shaders",

            ["机械动力"] = "Create",
            ["农夫乐事"] = "Farmer's Delight",
            ["苹果皮"] = "AppleSkin",
            ["玉"] = "Jade",
            ["小地图"] = "Xaero's Minimap",
            ["世界地图"] = "Xaero's World Map",
            ["匠魂"] = "Tinkers' Construct",
            ["通用机械"] = "Mekanism",
            ["植物魔法"] = "Botania",
            ["星辉魔法"] = "Astral Sorcery",
            ["沉浸工程"] = "Immersive Engineering",
            ["更好的战斗"] = "Better Combat",
            ["传送石碑"] = "Waystones",
            ["女仆"] = "Touhou Little Maid",
            ["车万女仆"] = "Touhou Little Maid",
            ["动态光源"] = "Dynamic Lights",
            ["物品栏"] = "Inventory Sorter"
        };

        /// <summary>是否含中日韩字符（用于提示"Modrinth 只有英文"）。</summary>
        public static bool ContainsChinese(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Any(ch => ch >= 0x4E00 && ch <= 0x9FFF);
        }

        /// <summary>命中已知别名则返回英文名，否则返回 null。</summary>
        public static string? TryMap(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string query = text!.Trim();

            if (Map.TryGetValue(query, out string? exact))
            {
                return exact;
            }

            // 长词优先：避免「钠」抢先匹配掉「钠光影」
            foreach (KeyValuePair<string, string> pair in Map.OrderByDescending(item => item.Key.Length))
            {
                if (query.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return pair.Value;
                }
            }

            return null;
        }
    }
}
