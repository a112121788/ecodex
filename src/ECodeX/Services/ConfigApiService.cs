using System.Text.Json;
using ECodeX.Core.IPC.V2;

namespace ECodeX.Services;

public sealed class ConfigApiService
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        "config.reload",
        "config.diagnostics",
    };

    private readonly Func<string>? _reloadConfig;
    private JsonElement? _lastReloadResult;

    public ConfigApiService(Func<string>? reloadConfig = null)
    {
        _reloadConfig = reloadConfig;
    }

    public static bool CanHandle(string? method)
    {
        return method != null && SupportedMethods.Contains(method);
    }

    public V2Response HandleRequest(V2Request request)
    {
        return request.Method switch
        {
            "config.reload" => HandleReload(request),
            "config.diagnostics" => HandleDiagnostics(request),
            _ => V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotSupported,
                $"ecodex.v2 config method is not supported: {request.Method}"),
        };
    }

    private V2Response HandleReload(V2Request request)
    {
        var result = ReloadConfig();
        return V2Response.FromResult(request.Id, result);
    }

    private V2Response HandleDiagnostics(V2Request request)
    {
        var result = _lastReloadResult ?? ReloadConfig();
        var diagnostics = result.TryGetProperty("diagnostics", out var diagnosticsElement)
            ? diagnosticsElement.Clone()
            : JsonSerializer.SerializeToElement(Array.Empty<object>());
        var loadedPaths = result.TryGetProperty("loadedPaths", out var loadedPathsElement)
            ? loadedPathsElement.Clone()
            : JsonSerializer.SerializeToElement(Array.Empty<string>());
        var ok = result.TryGetProperty("ok", out var okElement) &&
                 okElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? okElement.GetBoolean()
            : true;

        return V2Response.FromResult(request.Id, new
        {
            ok,
            loadedPaths,
            diagnostics,
        });
    }

    private JsonElement ReloadConfig()
    {
        var json = _reloadConfig?.Invoke() ?? JsonSerializer.Serialize(new
        {
            ok = true,
            loadedPaths = Array.Empty<string>(),
            diagnostics = Array.Empty<object>(),
            note = "No config reload handler registered.",
        });

        using var document = JsonDocument.Parse(json);
        _lastReloadResult = document.RootElement.Clone();
        return _lastReloadResult.Value;
    }
}
