using System.Text.Json;
using ECodeX.Core.IPC.V2;
using ECodeX.Core.Models;
using ECodeX.Core.Services;

namespace ECodeX.Services;

public sealed class WindowApiService<TWindow>
    where TWindow : class
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        "window.list",
        "window.current",
        "window.focus",
        "window.create",
        "window.close",
    };

    private readonly WindowManagerService<TWindow> _windowManager;
    private readonly Func<string?, TWindow> _windowFactory;
    private readonly Action<TWindow> _showWindow;
    private readonly Action<TWindow> _focusWindow;
    private readonly Action<TWindow> _closeWindow;

    public WindowApiService(
        WindowManagerService<TWindow> windowManager,
        Func<string?, TWindow>? windowFactory = null,
        Action<TWindow>? showWindow = null,
        Action<TWindow>? focusWindow = null,
        Action<TWindow>? closeWindow = null)
    {
        _windowManager = windowManager;
        _windowFactory = windowFactory ?? (_ => throw new InvalidOperationException("Window creation is not configured."));
        _showWindow = showWindow ?? (_ => { });
        _focusWindow = focusWindow ?? (_ => { });
        _closeWindow = closeWindow ?? (_ => { });
    }

    public static bool CanHandle(string? method)
    {
        return method != null && SupportedMethods.Contains(method);
    }

    public V2Response HandleRequest(V2Request request)
    {
        var idFormat = GetIdFormat(request.Params);
        return request.Method switch
        {
            "window.list" => V2Response.FromResult(request.Id, CreateListResult(idFormat)),
            "window.current" => V2Response.FromResult(request.Id, CreateCurrentResult(idFormat)),
            "window.focus" => HandleFocus(request, idFormat),
            "window.create" => HandleCreate(request, idFormat),
            "window.close" => HandleClose(request, idFormat),
            _ => V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotSupported,
                $"ecodex.v2 window method is not supported: {request.Method}"),
        };
    }

    private object CreateListResult(CliIdFormat idFormat)
    {
        var windows = _windowManager.ListWindows();
        var current = windows.FirstOrDefault(window => window.IsCurrent);
        return new
        {
            windows = windows.Select(window => CreateWindowResult(window, idFormat)).ToList(),
            current = current == null ? null : CreateWindowResult(current, idFormat),
        };
    }

    private object CreateCurrentResult(CliIdFormat idFormat)
    {
        var current = _windowManager.ListWindows().FirstOrDefault(window => window.IsCurrent);
        return new
        {
            window = current == null ? null : CreateWindowResult(current, idFormat),
        };
    }

    private V2Response HandleFocus(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveWindow(request.Params, out var window, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!_windowManager.FocusWindow(window!.Id, _focusWindow))
        {
            return V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotFound,
                $"Window not found: {window.Id}");
        }

        var focused = _windowManager.ListWindows().First(item => item.Id == window.Id);
        return V2Response.FromResult(request.Id, new
        {
            focused = true,
            window = CreateWindowResult(focused, idFormat),
        });
    }

    private V2Response HandleCreate(V2Request request, CliIdFormat idFormat)
    {
        var title = GetStringParam(request.Params, "title", "name");
        var created = _windowManager.CreateWindow(
            () => _windowFactory(title),
            _showWindow,
            title);

        return V2Response.FromResult(request.Id, new
        {
            created = true,
            window = CreateWindowResult(created, idFormat),
        });
    }

    private V2Response HandleClose(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveWindow(request.Params, out var window, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!_windowManager.CloseWindow(window!.Id, _closeWindow))
        {
            return V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotFound,
                $"Window not found: {window.Id}");
        }

        var current = _windowManager.ListWindows().FirstOrDefault(item => item.IsCurrent);
        return V2Response.FromResult(request.Id, new
        {
            closed = true,
            window = CreateWindowResult(window, idFormat),
            current = current == null ? null : CreateWindowResult(current, idFormat),
        });
    }

    private bool TryResolveWindow(JsonElement? parameters, out ManagedWindowInfo? window, out V2Error? error)
    {
        window = null;
        error = null;

        var target = GetStringParam(parameters, "target", "window", "windowRef", "ref", "windowId", "id");
        if (string.Equals(target, "current", StringComparison.OrdinalIgnoreCase))
            target = _windowManager.CurrentWindowId;

        if (string.IsNullOrWhiteSpace(target))
        {
            error = new V2Error(V2ErrorCodes.InvalidRef, "Missing required window target.");
            return false;
        }

        var windows = _windowManager.ListWindows();
        if (ShortRef.TryParse(target, out var shortRef))
        {
            if (shortRef.Kind != ShortRefKind.Window)
            {
                error = new V2Error(V2ErrorCodes.InvalidRef, $"Expected window ref, got {shortRef}.");
                return false;
            }

            window = windows.FirstOrDefault(item => item.Ref.Equals(shortRef));
            if (window == null)
            {
                error = new V2Error(V2ErrorCodes.NotFound, $"Window not found: {target}");
                return false;
            }

            return true;
        }

        window = windows.FirstOrDefault(item => string.Equals(item.Id, target, StringComparison.Ordinal));
        if (window == null)
        {
            error = new V2Error(V2ErrorCodes.NotFound, $"Window not found: {target}");
            return false;
        }

        return true;
    }

    private static Dictionary<string, object?> CreateWindowResult(ManagedWindowInfo window, CliIdFormat idFormat)
    {
        var result = new Dictionary<string, object?>
        {
            ["title"] = window.Title,
            ["isCurrent"] = window.IsCurrent,
            ["createdAtUtc"] = window.CreatedAtUtc,
        };

        if (idFormat is CliIdFormat.Refs or CliIdFormat.Both)
            result["ref"] = window.Ref.ToString();
        if (idFormat is CliIdFormat.Uuids or CliIdFormat.Both)
            result["id"] = window.Id;

        return result;
    }

    private static CliIdFormat GetIdFormat(JsonElement? parameters)
    {
        var raw = GetStringParam(parameters, "idFormat", "id_format");
        return CliGlobalOptions.TryParseIdFormat(raw, out var idFormat)
            ? idFormat
            : CliIdFormat.Both;
    }

    private static string? GetStringParam(JsonElement? parameters, params string[] names)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } root)
            return null;

        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();

            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.GetRawText();
        }

        return null;
    }
}
