using System.Text.Json;
using ECodeX.Core.IPC.V2;
using ECodeX.Core.Models;
using ECodeX.Core.Services;

namespace ECodeX.Services;

public sealed record PaneApiWorkspace<TWorkspace, TSurface, TPane>(
    TWorkspace Workspace,
    string WorkspaceId,
    string WorkspaceName,
    ShortRef WorkspaceRef,
    bool IsCurrent,
    IReadOnlyList<PaneApiSurface<TSurface, TPane>> Surfaces);

public sealed record PaneApiSurface<TSurface, TPane>(
    TSurface Surface,
    string SurfaceId,
    string SurfaceName,
    ShortRef SurfaceRef,
    bool IsCurrent,
    string Kind,
    bool IsZoomed,
    IReadOnlyList<PaneApiPane<TPane>> Panes);

public sealed record PaneApiPane<TPane>(
    TPane Pane,
    string PaneId,
    ShortRef PaneRef,
    string PaneName,
    bool IsFocused,
    string WorkingDirectory);

public sealed record PaneApiReadResult(string Text, int Lines, int MaxChars);

public sealed class PaneApiService<TWorkspace, TSurface, TPane>
    where TWorkspace : class
    where TSurface : class
    where TPane : class
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        "pane.list",
        "pane.focus",
        "pane.write",
        "pane.read",
        "pane.split",
        "pane.close",
        "pane.resize",
        "pane.swap",
        "pane.zoom",
    };

    private readonly Func<IEnumerable<PaneApiWorkspace<TWorkspace, TSurface, TPane>>> _workspaceProvider;
    private readonly Action<TWorkspace, TSurface> _selectSurface;
    private readonly Func<TSurface, string, bool> _focusPane;
    private readonly Func<TSurface, string, string, bool, string, bool> _writePane;
    private readonly Func<TSurface, string, int, int, PaneApiReadResult?> _readPane;
    private readonly Func<TSurface, string, SplitDirection, string?, bool> _splitPane;
    private readonly Func<TSurface, string, bool> _closePane;
    private readonly Func<TSurface, string, double, bool> _resizePane;
    private readonly Func<TSurface, string, string, bool> _swapPanes;
    private readonly Func<TSurface, bool?, bool> _zoomSurface;

    public PaneApiService(
        Func<IEnumerable<PaneApiWorkspace<TWorkspace, TSurface, TPane>>> workspaceProvider,
        Action<TWorkspace, TSurface> selectSurface,
        Func<TSurface, string, bool> focusPane,
        Func<TSurface, string, string, bool, string, bool> writePane,
        Func<TSurface, string, int, int, PaneApiReadResult?> readPane,
        Func<TSurface, string, SplitDirection, string?, bool> splitPane,
        Func<TSurface, string, bool> closePane,
        Func<TSurface, string, double, bool> resizePane,
        Func<TSurface, string, string, bool> swapPanes,
        Func<TSurface, bool?, bool> zoomSurface)
    {
        _workspaceProvider = workspaceProvider;
        _selectSurface = selectSurface;
        _focusPane = focusPane;
        _writePane = writePane;
        _readPane = readPane;
        _splitPane = splitPane;
        _closePane = closePane;
        _resizePane = resizePane;
        _swapPanes = swapPanes;
        _zoomSurface = zoomSurface;
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
            "pane.list" => HandleList(request, idFormat),
            "pane.focus" => HandleFocus(request, idFormat),
            "pane.write" => HandleWrite(request, idFormat),
            "pane.read" => HandleRead(request, idFormat),
            "pane.split" => HandleSplit(request, idFormat),
            "pane.close" => HandleClose(request, idFormat),
            "pane.resize" => HandleResize(request, idFormat),
            "pane.swap" => HandleSwap(request, idFormat),
            "pane.zoom" => HandleZoom(request, idFormat),
            _ => V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotSupported,
                $"ecodex.v2 pane method is not supported: {request.Method}"),
        };
    }

    private V2Response HandleList(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveContext(request.Params, requirePane: false, out var context, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        return V2Response.FromResult(request.Id, new
        {
            workspace = CreateWorkspaceResult(context.Workspace, idFormat),
            surface = CreateSurfaceResult(context.Surface, idFormat),
            panes = context.Surface.Panes.Select(pane => CreatePaneResult(pane, idFormat)).ToList(),
        });
    }

    private V2Response HandleFocus(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveContext(request.Params, requirePane: true, out var context, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        _selectSurface(context.Workspace.Workspace, context.Surface.Surface);
        if (!_focusPane(context.Surface.Surface, context.Pane!.PaneId))
            return V2Response.FromStableError(request.Id, V2ErrorCodes.NotFound, $"Pane not found: {context.Pane.PaneId}");

        var updated = FindPaneById(context.Workspace.WorkspaceId, context.Surface.SurfaceId, context.Pane.PaneId) ?? context.Pane;
        return V2Response.FromResult(request.Id, new
        {
            focused = true,
            pane = CreatePaneResult(updated, idFormat),
        });
    }

    private V2Response HandleWrite(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveContext(request.Params, requirePane: true, out var context, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        var text = GetStringParam(request.Params, "text", "value") ?? "";
        var submit = GetBoolParam(request.Params, "submit") ?? false;
        var submitKey = GetStringParam(request.Params, "submitKey") ?? "auto";
        if (!submit && string.IsNullOrWhiteSpace(text))
            return V2Response.FromStableError(request.Id, V2ErrorCodes.InvalidRef, "Missing required pane text.");

        _selectSurface(context.Workspace.Workspace, context.Surface.Surface);
        if (!_writePane(context.Surface.Surface, context.Pane!.PaneId, text, submit, submitKey))
            return V2Response.FromStableError(request.Id, V2ErrorCodes.NotFound, $"Pane session not found: {context.Pane.PaneId}");

        return V2Response.FromResult(request.Id, new
        {
            written = true,
            pane = CreatePaneResult(context.Pane, idFormat),
            bytes = text.Length,
            submit,
            submitKey,
        });
    }

    private V2Response HandleRead(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveContext(request.Params, requirePane: true, out var context, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        var lines = Math.Clamp(GetIntParam(request.Params, "lines") ?? 80, 1, 5000);
        var maxChars = Math.Clamp(GetIntParam(request.Params, "maxChars") ?? 20000, 512, 200000);
        var read = _readPane(context.Surface.Surface, context.Pane!.PaneId, lines, maxChars);
        if (read == null)
            return V2Response.FromStableError(request.Id, V2ErrorCodes.NotFound, $"Pane session not found: {context.Pane.PaneId}");

        return V2Response.FromResult(request.Id, new
        {
            pane = CreatePaneResult(context.Pane, idFormat),
            lines = read.Lines,
            maxChars = read.MaxChars,
            text = read.Text,
        });
    }

    private V2Response HandleSplit(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveContext(request.Params, requirePane: true, out var context, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!TryGetDirection(request.Params, out var direction, out error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        var shell = GetStringParam(request.Params, "shell");
        _selectSurface(context.Workspace.Workspace, context.Surface.Surface);
        if (!_splitPane(context.Surface.Surface, context.Pane!.PaneId, direction, shell))
            return V2Response.FromStableError(request.Id, V2ErrorCodes.NotSupported, $"Pane split was not applied: {context.Pane.PaneId}");

        var surface = FindSurfaceById(context.Workspace.WorkspaceId, context.Surface.SurfaceId) ?? context.Surface;
        var focused = surface.Panes.FirstOrDefault(pane => pane.IsFocused);
        return V2Response.FromResult(request.Id, new
        {
            split = true,
            direction = direction.ToString(),
            pane = focused == null ? null : CreatePaneResult(focused, idFormat),
            panes = surface.Panes.Select(pane => CreatePaneResult(pane, idFormat)).ToList(),
        });
    }

    private V2Response HandleClose(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveContext(request.Params, requirePane: true, out var context, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        _selectSurface(context.Workspace.Workspace, context.Surface.Surface);
        if (!_closePane(context.Surface.Surface, context.Pane!.PaneId))
            return V2Response.FromStableError(request.Id, V2ErrorCodes.NotSupported, $"Pane close was not applied: {context.Pane.PaneId}");

        var surface = FindSurfaceById(context.Workspace.WorkspaceId, context.Surface.SurfaceId) ?? context.Surface;
        var focused = surface.Panes.FirstOrDefault(pane => pane.IsFocused);
        return V2Response.FromResult(request.Id, new
        {
            closed = true,
            pane = CreatePaneResult(context.Pane, idFormat),
            current = focused == null ? null : CreatePaneResult(focused, idFormat),
            panes = surface.Panes.Select(pane => CreatePaneResult(pane, idFormat)).ToList(),
        });
    }

    private V2Response HandleResize(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveContext(request.Params, requirePane: true, out var context, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        var delta = GetDoubleParam(request.Params, "delta");
        if (delta == null)
            return V2Response.FromStableError(request.Id, V2ErrorCodes.InvalidRef, "Missing required pane resize delta.");

        if (!_resizePane(context.Surface.Surface, context.Pane!.PaneId, delta.Value))
            return V2Response.FromStableError(request.Id, V2ErrorCodes.NotFound, $"Pane resize target not found: {context.Pane.PaneId}");

        return V2Response.FromResult(request.Id, new
        {
            resized = true,
            pane = CreatePaneResult(context.Pane, idFormat),
            delta = delta.Value,
        });
    }

    private V2Response HandleSwap(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveContext(request.Params, requirePane: true, out var context, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        var otherTarget = GetStringParam(request.Params, "other", "with", "otherPane", "otherPaneRef");
        if (string.IsNullOrWhiteSpace(otherTarget))
            return V2Response.FromStableError(request.Id, V2ErrorCodes.InvalidRef, "Missing required pane swap target.");

        if (!TryResolvePane(context.Surface, otherTarget, out var otherPane, out error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!_swapPanes(context.Surface.Surface, context.Pane!.PaneId, otherPane!.PaneId))
            return V2Response.FromStableError(request.Id, V2ErrorCodes.NotFound, "Pane swap target not found.");

        var surface = FindSurfaceById(context.Workspace.WorkspaceId, context.Surface.SurfaceId) ?? context.Surface;
        return V2Response.FromResult(request.Id, new
        {
            swapped = true,
            pane = CreatePaneResult(context.Pane, idFormat),
            other = CreatePaneResult(otherPane, idFormat),
            panes = surface.Panes.Select(pane => CreatePaneResult(pane, idFormat)).ToList(),
        });
    }

    private V2Response HandleZoom(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveContext(request.Params, requirePane: false, out var context, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        var zoomed = GetBoolParam(request.Params, "zoomed", "value");
        if (!_zoomSurface(context.Surface.Surface, zoomed))
            return V2Response.FromStableError(request.Id, V2ErrorCodes.NotSupported, "Pane zoom was not applied.");

        var surface = FindSurfaceById(context.Workspace.WorkspaceId, context.Surface.SurfaceId) ?? context.Surface;
        return V2Response.FromResult(request.Id, new
        {
            zoomed = surface.IsZoomed,
            surface = CreateSurfaceResult(surface, idFormat),
        });
    }

    private bool TryResolveContext(
        JsonElement? parameters,
        bool requirePane,
        out PaneApiContext<TWorkspace, TSurface, TPane> context,
        out V2Error? error)
    {
        context = default;
        error = null;
        if (!TryResolveWorkspace(parameters, out var workspace, out error))
            return false;

        if (!TryResolveSurface(workspace!, parameters, out var surface, out error))
            return false;

        PaneApiPane<TPane>? pane = null;
        var target = GetStringParam(parameters, "target", "pane", "paneRef", "ref", "paneId", "id");
        if (string.Equals(target, "focused", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, "current", StringComparison.OrdinalIgnoreCase))
        {
            target = null;
        }

        if (!string.IsNullOrWhiteSpace(target) || requirePane)
        {
            if (!TryResolvePane(surface!, target, out pane, out error))
                return false;
        }

        context = new PaneApiContext<TWorkspace, TSurface, TPane>(workspace!, surface!, pane);
        return true;
    }

    private bool TryResolveWorkspace(
        JsonElement? parameters,
        out PaneApiWorkspace<TWorkspace, TSurface, TPane>? workspace,
        out V2Error? error)
    {
        workspace = null;
        error = null;
        var workspaces = GetWorkspaces();
        if (workspaces.Count == 0)
        {
            error = new V2Error(V2ErrorCodes.NotFound, "No workspace available.");
            return false;
        }

        var target = GetStringParam(parameters, "workspace", "workspaceRef", "workspaceId");
        if (string.IsNullOrWhiteSpace(target))
        {
            workspace = workspaces.FirstOrDefault(item => item.IsCurrent) ?? workspaces[0];
            return true;
        }

        if (ShortRef.TryParse(target, out var shortRef))
        {
            if (shortRef.Kind != ShortRefKind.Workspace)
            {
                error = new V2Error(V2ErrorCodes.InvalidRef, $"Expected workspace ref, got {shortRef}.");
                return false;
            }

            workspace = workspaces.FirstOrDefault(item => item.WorkspaceRef.Equals(shortRef));
        }
        else
        {
            workspace = workspaces.FirstOrDefault(item => string.Equals(item.WorkspaceId, target, StringComparison.Ordinal))
                ?? workspaces.FirstOrDefault(item => string.Equals(item.WorkspaceName, target, StringComparison.OrdinalIgnoreCase));
        }

        if (workspace == null)
        {
            error = new V2Error(V2ErrorCodes.NotFound, $"Workspace not found: {target}");
            return false;
        }

        return true;
    }

    private static bool TryResolveSurface(
        PaneApiWorkspace<TWorkspace, TSurface, TPane> workspace,
        JsonElement? parameters,
        out PaneApiSurface<TSurface, TPane>? surface,
        out V2Error? error)
    {
        surface = null;
        error = null;
        if (workspace.Surfaces.Count == 0)
        {
            error = new V2Error(V2ErrorCodes.NotFound, "No surface available in workspace.");
            return false;
        }

        var target = GetStringParam(parameters, "surface", "surfaceRef", "surfaceId");
        if (string.IsNullOrWhiteSpace(target))
        {
            surface = workspace.Surfaces.FirstOrDefault(item => item.IsCurrent) ?? workspace.Surfaces[0];
            return true;
        }

        if (ShortRef.TryParse(target, out var shortRef))
        {
            if (shortRef.Kind != ShortRefKind.Surface)
            {
                error = new V2Error(V2ErrorCodes.InvalidRef, $"Expected surface ref, got {shortRef}.");
                return false;
            }

            surface = workspace.Surfaces.FirstOrDefault(item => item.SurfaceRef.Equals(shortRef));
        }
        else
        {
            surface = workspace.Surfaces.FirstOrDefault(item => string.Equals(item.SurfaceId, target, StringComparison.Ordinal))
                ?? workspace.Surfaces.FirstOrDefault(item => string.Equals(item.SurfaceName, target, StringComparison.OrdinalIgnoreCase));
        }

        if (surface == null)
        {
            error = new V2Error(V2ErrorCodes.NotFound, $"Surface not found: {target}");
            return false;
        }

        return true;
    }

    private static bool TryResolvePane(
        PaneApiSurface<TSurface, TPane> surface,
        string? target,
        out PaneApiPane<TPane>? pane,
        out V2Error? error)
    {
        pane = null;
        error = null;
        if (surface.Panes.Count == 0)
        {
            error = new V2Error(V2ErrorCodes.NotFound, "No panes available in surface.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            pane = surface.Panes.FirstOrDefault(item => item.IsFocused) ?? surface.Panes[0];
            return true;
        }

        if (ShortRef.TryParse(target, out var shortRef))
        {
            if (shortRef.Kind != ShortRefKind.Pane)
            {
                error = new V2Error(V2ErrorCodes.InvalidRef, $"Expected pane ref, got {shortRef}.");
                return false;
            }

            pane = surface.Panes.FirstOrDefault(item => item.PaneRef.Equals(shortRef));
        }
        else
        {
            pane = surface.Panes.FirstOrDefault(item => string.Equals(item.PaneId, target, StringComparison.Ordinal))
                ?? surface.Panes.FirstOrDefault(item => string.Equals(item.PaneName, target, StringComparison.OrdinalIgnoreCase));
        }

        if (pane == null)
        {
            error = new V2Error(V2ErrorCodes.NotFound, $"Pane not found: {target}");
            return false;
        }

        return true;
    }

    private bool TryGetDirection(JsonElement? parameters, out SplitDirection direction, out V2Error? error)
    {
        error = null;
        var raw = GetStringParam(parameters, "direction") ?? "right";
        direction = raw.Trim().ToLowerInvariant() switch
        {
            "right" or "vertical" or "v" => SplitDirection.Vertical,
            "down" or "horizontal" or "h" => SplitDirection.Horizontal,
            _ => default,
        };

        if (raw.Trim().ToLowerInvariant() is "right" or "vertical" or "v" or "down" or "horizontal" or "h")
            return true;

        error = new V2Error(V2ErrorCodes.InvalidRef, $"Unsupported split direction: {raw}");
        return false;
    }

    private PaneApiSurface<TSurface, TPane>? FindSurfaceById(string workspaceId, string surfaceId)
    {
        return GetWorkspaces()
            .FirstOrDefault(workspace => workspace.WorkspaceId == workspaceId)?
            .Surfaces.FirstOrDefault(surface => surface.SurfaceId == surfaceId);
    }

    private PaneApiPane<TPane>? FindPaneById(string workspaceId, string surfaceId, string paneId)
    {
        return FindSurfaceById(workspaceId, surfaceId)?
            .Panes.FirstOrDefault(pane => pane.PaneId == paneId);
    }

    private List<PaneApiWorkspace<TWorkspace, TSurface, TPane>> GetWorkspaces()
    {
        return _workspaceProvider().ToList();
    }

    private static Dictionary<string, object?> CreateWorkspaceResult(
        PaneApiWorkspace<TWorkspace, TSurface, TPane> workspace,
        CliIdFormat idFormat)
    {
        var result = new Dictionary<string, object?>
        {
            ["name"] = workspace.WorkspaceName,
            ["isCurrent"] = workspace.IsCurrent,
        };
        if (idFormat is CliIdFormat.Refs or CliIdFormat.Both)
            result["ref"] = workspace.WorkspaceRef.ToString();
        if (idFormat is CliIdFormat.Uuids or CliIdFormat.Both)
            result["id"] = workspace.WorkspaceId;
        return result;
    }

    private static Dictionary<string, object?> CreateSurfaceResult(PaneApiSurface<TSurface, TPane> surface, CliIdFormat idFormat)
    {
        var result = new Dictionary<string, object?>
        {
            ["name"] = surface.SurfaceName,
            ["kind"] = surface.Kind,
            ["isCurrent"] = surface.IsCurrent,
            ["isZoomed"] = surface.IsZoomed,
        };
        if (idFormat is CliIdFormat.Refs or CliIdFormat.Both)
            result["ref"] = surface.SurfaceRef.ToString();
        if (idFormat is CliIdFormat.Uuids or CliIdFormat.Both)
            result["id"] = surface.SurfaceId;
        return result;
    }

    private static Dictionary<string, object?> CreatePaneResult(PaneApiPane<TPane> pane, CliIdFormat idFormat)
    {
        var result = new Dictionary<string, object?>
        {
            ["name"] = pane.PaneName,
            ["isFocused"] = pane.IsFocused,
            ["workingDirectory"] = pane.WorkingDirectory,
        };
        if (idFormat is CliIdFormat.Refs or CliIdFormat.Both)
            result["ref"] = pane.PaneRef.ToString();
        if (idFormat is CliIdFormat.Uuids or CliIdFormat.Both)
            result["id"] = pane.PaneId;
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

    private static bool? GetBoolParam(JsonElement? parameters, params string[] names)
    {
        var raw = GetStringParam(parameters, names);
        if (raw == null)
            return null;
        if (bool.TryParse(raw, out var parsed))
            return parsed;
        return raw == "1" ? true : raw == "0" ? false : null;
    }

    private static int? GetIntParam(JsonElement? parameters, string name)
    {
        var raw = GetStringParam(parameters, name);
        return int.TryParse(raw, out var value) ? value : null;
    }

    private static double? GetDoubleParam(JsonElement? parameters, string name)
    {
        var raw = GetStringParam(parameters, name);
        return double.TryParse(raw, out var value) ? value : null;
    }
}

public readonly record struct PaneApiContext<TWorkspace, TSurface, TPane>(
    PaneApiWorkspace<TWorkspace, TSurface, TPane> Workspace,
    PaneApiSurface<TSurface, TPane> Surface,
    PaneApiPane<TPane>? Pane)
    where TWorkspace : class
    where TSurface : class
    where TPane : class;
