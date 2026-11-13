using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Core.Invites;

/// <summary>
/// 同一登录会话内的邀请链接转发通道。只传一行 UTF-8 文本，不执行任何内容。
/// </summary>
public static class InvitePipe
{
    public const string PipeName = "NLMCL.Invite.v1";
    private const int MaxMessageBytes = 4096;

    /// <summary>把邀请链接发给已运行的实例；找不到服务端时返回 false。</summary>
    public static async Task<bool> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message) || Encoding.UTF8.GetByteCount(message) > MaxMessageBytes)
        {
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(1500, timeout.Token).ConfigureAwait(false);
            byte[] bytes = Encoding.UTF8.GetBytes(message + "\n");
            await client.WriteAsync(bytes, 0, bytes.Length, timeout.Token).ConfigureAwait(false);
            await client.FlushAsync(timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>在拥有单实例锁的进程中循环监听；取消时退出。</summary>
    public static async Task ListenAsync(Func<string, Task> onMessage, CancellationToken cancellationToken)
    {
        if (onMessage == null)
        {
            throw new ArgumentNullException(nameof(onMessage));
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                string? message = await ReadLineAsync(server, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    await onMessage(message).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxMessageBytes + 1];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer, length, 1, cancellationToken).ConfigureAwait(false);
            if (read == 0 || buffer[length] == (byte)'\n')
            {
                break;
            }

            length += read;
        }

        if (length == 0 || length > MaxMessageBytes)
        {
            return null;
        }

        return Encoding.UTF8.GetString(buffer, 0, length).TrimEnd('\r');
    }
}
