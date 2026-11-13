using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Invites;

/// <summary>
/// 解析 <c>nlmcl://join?host=...&amp;port=...&amp;name=...&amp;modded=1</c>。
/// 只做纯文本解析，不接受命令行、shell 片段或任意 URI scheme。
/// </summary>
public static class InviteLinkParser
{
    public const string Scheme = "nlmcl";
    private const int MaxUriLength = 2048;
    private const int MaxHostLength = 253;
    private const int MaxNameLength = 64;

    public static InviteLinkParseResult Parse(string? uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText))
        {
            return InviteLinkParseResult.Fail("邀请链接为空。");
        }

        string text = uriText.Trim();
        if (text.Length > MaxUriLength)
        {
            return InviteLinkParseResult.Fail("邀请链接过长。");
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return InviteLinkParseResult.Fail("不是有效的 nlmcl 邀请链接。");
        }

        if (!string.Equals(uri.Host, "join", StringComparison.OrdinalIgnoreCase))
        {
            return InviteLinkParseResult.Fail("邀请链接只支持 nlmcl://join。");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return InviteLinkParseResult.Fail("邀请链接不能包含账号信息。");
        }

        Dictionary<string, string> query = ParseQuery(uri.Query);
        if (!query.TryGetValue("host", out string? hostValue) || string.IsNullOrWhiteSpace(hostValue))
        {
            return InviteLinkParseResult.Fail("邀请链接缺少 host。");
        }

        string host = hostValue.Trim();
        if (host.Length == 0 || host.Length > MaxHostLength)
        {
            return InviteLinkParseResult.Fail("服务器地址长度不合法。");
        }

        if (host.IndexOf("..", StringComparison.Ordinal) >= 0 ||
            host.Any(char.IsWhiteSpace) ||
            host.IndexOfAny(new[] { '/', '\\', '?', '#', '@' }) >= 0)
        {
            return InviteLinkParseResult.Fail("服务器地址包含非法字符。");
        }

        UriHostNameType hostType = Uri.CheckHostName(host);
        if (hostType != UriHostNameType.Dns && hostType != UriHostNameType.IPv4)
        {
            return InviteLinkParseResult.Fail("服务器地址只支持域名或 IPv4。");
        }

        int port = 25565;
        if (query.TryGetValue("port", out string? portValue) && !string.IsNullOrWhiteSpace(portValue))
        {
            if (!int.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
                port < 1 || port > 65535)
            {
                return InviteLinkParseResult.Fail("服务器端口必须在 1–65535 之间。");
            }
        }

        string? name = null;
        if (query.TryGetValue("name", out string? nameValue))
        {
            name = CleanName(nameValue);
            if (name.Length > MaxNameLength)
            {
                return InviteLinkParseResult.Fail("服务器名称过长。");
            }
        }

        bool modded = query.TryGetValue("modded", out string? moddedValue) &&
                      (string.Equals(moddedValue, "1", StringComparison.Ordinal) ||
                       string.Equals(moddedValue, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(moddedValue, "yes", StringComparison.OrdinalIgnoreCase));

        return InviteLinkParseResult.Ok(new InviteLink(host, port, name, modded));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        string trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return values;
        }

        foreach (string pair in trimmed.Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

            int separator = pair.IndexOf('=');
            string key = separator < 0 ? pair : pair.Substring(0, separator);
            string value = separator < 0 ? string.Empty : pair.Substring(separator + 1);
            key = Decode(key);
            value = Decode(value);
            if (key.Length > 0)
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static string CleanName(string value)
    {
        string decoded = Decode(value).Trim();
        var buffer = new char[decoded.Length];
        int length = 0;
        foreach (char ch in decoded)
        {
            if (!char.IsControl(ch))
            {
                buffer[length++] = ch;
            }
        }

        return new string(buffer, 0, length).Trim();
    }
}
