using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Resources
{
    /// <summary>
    /// 用 MyMemory 的免费接口做中→英翻译（无需 key）。
    ///
    /// 端点：<c>https://api.mymemory.translated.net/get?q=...&langpair=zh-CN|en</c>
    /// 实测 1.2 秒返回。翻译结果做内存缓存，同一个词不会重复请求。
    ///
    /// 为什么不内置更大的词表：中文模组名是开放集合（每天都有新模组），
    /// 词表永远覆盖不全；翻译 + 别名表组合才是可维护的做法。
    /// </summary>
    public sealed class MyMemoryTranslator : ITranslator
    {
        public const string Endpoint = "https://api.mymemory.translated.net/get";

        private readonly IDownloadService _download;
        private readonly Dictionary<string, string?> _cache =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new object();

        public MyMemoryTranslator(IDownloadService download)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
        }

        public async Task<string?> TranslateToEnglishAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string query = text.Trim();

            lock (_lock)
            {
                if (_cache.TryGetValue(query, out string? cached))
                {
                    return cached;
                }
            }

            string? translated = null;

            try
            {
                string url = Endpoint + "?q=" + Uri.EscapeDataString(query) + "&langpair=zh-CN%7Cen";
                string json = await _download.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

                translated = Parse(json, query);
            }
            catch (Exception)
            {
                // 翻译是锦上添花：网络不通就按原词搜
                translated = null;
            }

            lock (_lock)
            {
                _cache[query] = translated;
            }

            return translated;
        }

        /// <summary>解析响应的 translatedText；与原文相同或为空时视为无效。</summary>
        internal static string? Parse(string json, string original)
        {
            try
            {
                JObject root = JObject.Parse(json);
                string? text = root.SelectToken("responseData.translatedText")?.Value<string>();

                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                text = text!.Trim();

                // 有些情况下接口原样返回，或不翻译成英文（仍是中文），都算无效
                if (string.Equals(text, original, StringComparison.OrdinalIgnoreCase)
                    || ChineseAliases.ContainsChinese(text))
                {
                    return null;
                }

                return text;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
