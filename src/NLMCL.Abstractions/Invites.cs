namespace NLMCL.Abstractions;

/// <summary>一条 <c>nlmcl://join</c> 邀请。地址已经过语法与长度校验，不代表服务器在线。</summary>
public sealed class InviteLink
{
    public InviteLink(string host, int port, string? name, bool requiresAutoModpack)
    {
        Host = host;
        Port = port;
        Name = name;
        RequiresAutoModpack = requiresAutoModpack;
    }

    public string Host { get; }

    public int Port { get; }

    public string? Name { get; }

    public bool RequiresAutoModpack { get; }

    public string Address => Port == 25565 ? Host : Host + ":" + Port;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Address : Name!;
}

/// <summary>邀请链接解析结果；失败时 <see cref="Error"/> 为可直接展示给玩家的原因。</summary>
public sealed class InviteLinkParseResult
{
    private InviteLinkParseResult(InviteLink? link, string? error)
    {
        Link = link;
        Error = error;
    }

    public InviteLink? Link { get; }

    public string? Error { get; }

    public bool Success => Link != null;

    public static InviteLinkParseResult Ok(InviteLink link) => new(link, null);

    public static InviteLinkParseResult Fail(string error) => new(null, error);
}
