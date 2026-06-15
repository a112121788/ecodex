using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ECodeX.Core.Config;
using ECodeX.Core.IPC.V2;

namespace ECodeX.Core.IPC;

/// <summary>
/// 用于 ecodex CLI/API 通信的命名管道服务器。
/// 管道名：\\.\pipe\ecodex（或带标签实例 \\.\pipe\ecodex-{tag}）。
/// 兼容期还会额外监听旧名 \\.\pipe\cmux（/ \\.\pipe\cmux-{tag}），
/// 以便旧版 CLI 集成脚本继续可用；可通过
/// <see cref="ECodeXSettings.CompatListenLegacyMainPipe"/> 关闭。
/// </summary>
public sealed class NamedPipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly string? _legacyPipeName;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public string PipeName => _pipeName;

    /// <summary>兼容监听时使用的旧管道名，未启用时为 null。</summary>
    public string? LegacyPipeName => _legacyPipeName;

    /// <summary>
    /// 收到命令时调用。参数：(命令，参数字典)。
    /// 返回响应 JSON 字符串。
    /// </summary>
    public Func<string, Dictionary<string, string>, Task<string>>? OnCommand { get; set; }

    /// <summary>
    /// 收到 ecodex.v2 JSON 请求时调用。若未注册，服务器返回 not_supported。
    /// </summary>
    public Func<V2Request, Task<V2Response>>? OnV2Request { get; set; }

    public NamedPipeServer(string? tag = null)
    {
        _pipeName = string.IsNullOrEmpty(tag) ? "ecodex" : $"ecodex-{tag}";
        if (CompatibilityOptions.ShouldListenLegacyMainPipe(tag))
        {
            _legacyPipeName = string.IsNullOrEmpty(tag) ? "cmux" : $"cmux-{tag}";
        }
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);

                // 在独立任务上处理每个连接
                _ = Task.Run(() => HandleConnection(pipe, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // 管道错误，重试
                await Task.Delay(100, ct);
            }
        }
    }

    /// <summary>
    /// 兼容旧管道名的额外监听循环；通过 <c>CompatListenLegacyMainPipe</c> 关闭时
    /// 不会启动。
    /// </summary>
    public void StartLegacyListener()
    {
        if (_legacyPipeName == null) return;
        _ = Task.Run(() => LegacyListenLoop(_cts!.Token));
    }

    private async Task LegacyListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _legacyPipeName != null)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    _legacyPipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);

                _ = Task.Run(() => HandleConnection(pipe, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                try { await Task.Delay(100, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task HandleConnection(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            using (pipe)
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var requestLine = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(requestLine)) return;

                var incoming = NamedPipeProtocol.ParseFirstLine(requestLine);
                var response = incoming.Kind == NamedPipeProtocolKind.V2
                    ? await HandleV2Request(incoming)
                    : await HandleV1Request(incoming.V1!);

                await writer.WriteLineAsync(response);
            }
        }
        catch (IOException)
        {
            // 客户端断开连接
        }
        catch (OperationCanceledException)
        {
            // 服务器关闭
        }
    }

    private async Task<string> HandleV1Request(NamedPipeV1Request request)
    {
        if (OnCommand != null)
            return await OnCommand(request.Command, request.Args.ToDictionary());

        return JsonSerializer.Serialize(new { error = "No handler registered" });
    }

    private async Task<string> HandleV2Request(NamedPipeIncomingRequest incoming)
    {
        V2Response response;
        if (incoming.V2ErrorResponse != null)
        {
            response = incoming.V2ErrorResponse;
        }
        else if (OnV2Request != null)
        {
            response = await OnV2Request(incoming.V2!);
        }
        else
        {
            response = V2Response.FromStableError(
                incoming.V2!.Id,
                V2ErrorCodes.NotSupported,
                "No ecodex.v2 handler registered.");
        }

        return JsonSerializer.Serialize(response);
    }

    private static void ParseArgs(string argsString, Dictionary<string, string> args)
    {
        // 同时支持 key=value 和 JSON 格式
        var trimmed = argsString.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                var json = JsonSerializer.Deserialize<Dictionary<string, string>>(trimmed);
                if (json != null)
                {
                    foreach (var kvp in json)
                        args[kvp.Key] = kvp.Value;
                    return;
                }
            }
            catch
            {
                // 回退到 key=value 解析
            }
        }

        foreach (var part in SplitRespectingQuotes(trimmed))
        {
            int eq = part.IndexOf('=');
            if (eq > 0)
            {
                var key = part[..eq];
                var value = part[(eq + 1)..].Trim('"', '\'');
                args[key] = value;
            }
            else
            {
                // 位置参数
                args.TryAdd("_arg" + args.Count, part);
            }
        }
    }

    private static IEnumerable<string> SplitRespectingQuotes(string input)
    {
        var current = new StringBuilder();
        bool inQuote = false;
        char quoteChar = '\0';

        foreach (var c in input)
        {
            if (inQuote)
            {
                if (c == quoteChar)
                {
                    inQuote = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                inQuote = true;
                quoteChar = c;
            }
            else if (c == ' ')
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listenTask?.Wait(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

/// <summary>
/// 用于连接 ecodex 命名管道服务器的客户端（CLI 使用）。
/// 兼容期会先尝试新管道（`ecodex` / `ecodex-{tag}`），失败时再尝试旧管道
/// （`cmux` / `cmux-{tag}`）。可通过
/// <see cref="ECodeXSettings.CompatListenLegacyMainPipe"/> 关闭旧管道 fallback。
/// </summary>
public static class NamedPipeClient
{
    public static async Task<string> SendCommand(string command, Dictionary<string, string>? args = null, string? tag = null, int timeoutMs = 5000)
    {
        var newName = string.IsNullOrEmpty(tag) ? "ecodex" : $"ecodex-{tag}";
        var candidates = new List<string> { newName };
        if (CompatibilityOptions.ShouldListenLegacyMainPipe(tag))
        {
            candidates.Add(string.IsNullOrEmpty(tag) ? "cmux" : $"cmux-{tag}");
        }

        var sb = new StringBuilder(command);
        if (args is { Count: > 0 })
            sb.Append(' ').Append(JsonSerializer.Serialize(args));

        var requestLine = sb.ToString();

        Exception? last = null;
        foreach (var pipeName in candidates)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var cts = new CancellationTokenSource(timeoutMs);

                await pipe.ConnectAsync(cts.Token);

                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                await writer.WriteLineAsync(requestLine);

                var response = await reader.ReadLineAsync(cts.Token);
                return response ?? "";
            }
            catch (TimeoutException ex)
            {
                last = ex;
            }
            catch (IOException ex)
            {
                last = ex;
            }
        }

        if (last is TimeoutException) throw last;
        throw new TimeoutException($"Could not connect to any ecodex pipe (tried: {string.Join(", ", candidates)}).");
    }

    public static async Task<string> SendV2Request(V2Request request, string? tag = null, int timeoutMs = 5000)
    {
        var requestLine = JsonSerializer.Serialize(request);
        return await SendRawLine(requestLine, tag, timeoutMs);
    }

    private static async Task<string> SendRawLine(string requestLine, string? tag, int timeoutMs)
    {
        var newName = string.IsNullOrEmpty(tag) ? "ecodex" : $"ecodex-{tag}";
        var candidates = new List<string> { newName };
        if (CompatibilityOptions.ShouldListenLegacyMainPipe(tag))
        {
            candidates.Add(string.IsNullOrEmpty(tag) ? "cmux" : $"cmux-{tag}");
        }

        Exception? last = null;
        foreach (var pipeName in candidates)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var cts = new CancellationTokenSource(timeoutMs);

                await pipe.ConnectAsync(cts.Token);

                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                await writer.WriteLineAsync(requestLine);

                var response = await reader.ReadLineAsync(cts.Token);
                return response ?? "";
            }
            catch (TimeoutException ex)
            {
                last = ex;
            }
            catch (IOException ex)
            {
                last = ex;
            }
        }

        if (last is TimeoutException) throw last;
        throw new TimeoutException($"Could not connect to any ecodex pipe (tried: {string.Join(", ", candidates)}).");
    }
}
