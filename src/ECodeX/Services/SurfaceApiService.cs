using System.Text.Json;
using ECodeX.Core.IPC.V2;
using ECodeX.Core.Models;
using ECodeX.Core.Services;

namespace ECodeX.Services;

public sealed record SurfaceApiWorkspace<TWorkspace, TSurface>(
    TWorkspace Workspace,
    string WorkspaceId,
    string WorkspaceName,
    ShortRef WorkspaceRef,
    bool IsCurrent,
    IReadOnlyList<SurfaceApiSurface<TSurface>> Surfaces);

public sealed record SurfaceApiSurface<TSurface>(
    TSurface Surface,
    string SurfaceId,
    string SurfaceName,
    ShortRef SurfaceRef,
    bool IsCurrent,
    string Kind);

public sealed class SurfaceApiService<TWorkspace, TSurface>
    where TWorkspace : class
    where TSurface : class
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        "surface.move",
        "surface.reorder",
    };

    private readonly Func<IEnumerable<SurfaceApiWorkspace<TWorkspace, TSurface>>> _workspaceProvider;
    private readonly Func<TWorkspace, TSurface, int, bool> _moveSurface;
    private readonly Func<TWorkspace, IReadOnlyList<string>, bool> _reorderSurfaces;
    private readonly Action<TWorkspace, TSurface> _selectSurface;

    public SurfaceApiService(
        Func<IEnumerable<SurfaceApiWorkspace<TWorkspace, TSurface>>> workspaceProvider,
        Func<TWorkspace, TSurface, int, bool> moveSurface,
        Func<TWorkspace, IReadOnlyList<string>, bool> reorderSurfaces,
        Action<TWorkspace, TSurface>? selectSurface = null)
    {
        _workspaceProvider = workspaceProvider;
        _moveSurface = moveSurface;
        _reorderSurfaces = reorderSurfaces;
        _selectSurface = selectSurface ?? ((_, _) => { });
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
            "surface.move" => HandleMove(request, idFormat),
            "surface.reorder" => HandleReorder(request, idFormat),
            _ => V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotSupported,
                $"ecodex.v2 surface method is not supported: {request.Method}"),
        };
    }

    private V2Response HandleMove(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveWorkspace(request.Params, out var workspace, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!TryResolveSurface(workspace!, request.Params, out var surface, out error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!TryGetTargetIndex(request.Params, workspace!.Surfaces.Count, out var targetIndex, out error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!_moveSurface(workspace.Workspace, surface!.Surface, targetIndex))
        {
            return V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotFound,
                $"Surface move was not applied: {surface.SurfaceId}");
        }

        _selectSurface(workspace.Workspace, surface.Surface);
        var updatedWorkspace = FindWorkspaceById(workspace.WorkspaceId) ?? workspace;
        var updatedSurface = updatedWorkspace.Surfaces.FirstOrDefault(item => item.SurfaceId == surface.SurfaceId) ?? surface;
        return V2Response.FromResult(request.Id, new
        {
            moved = true,
            workspace = CreateWorkspaceResult(updatedWorkspace, idFormat),
            surface = CreateSurfaceResult(updatedSurface, idFormat),
            surfaces = CreateSurfaceOrderResult(updatedWorkspace, idFormat),
        });
    }

    private V2Response HandleReorder(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveWorkspace(request.Params, out var workspace, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!TryGetRequestedOrder(request.Params, workspace!, out var surfaceIds, out error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!_reorderSurfaces(workspace!.Workspace, surfaceIds))
        {
            return V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotFound,
                "Surface reorder was not applied.");
        }

        var updatedWorkspace = FindWorkspaceById(workspace.WorkspaceId) ?? workspace;
        return V2Response.FromResult(request.Id, new
        {
            reordered = true,
            workspace = CreateWorkspaceResult(updatedWorkspace, idFormat),
            surfaces = CreateSurfaceOrderResult(updatedWorkspace, idFormat),
        });
    }

    private bool TryResolveWorkspace(
        JsonElement? parameters,
        out SurfaceApiWorkspace<TWorkspace, TSurface>? workspace,
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
        SurfaceApiWorkspace<TWorkspace, TSurface> workspace,
        JsonElement? parameters,
        out SurfaceApiSurface<TSurface>? surface,
        out V2Error? error)
    {
        surface = null;
        error = null;
        if (workspace.Surfaces.Count == 0)
        {
            error = new V2Error(V2ErrorCodes.NotFound, "No surface available in workspace.");
            return false;
        }

        var target = GetStringParam(parameters, "target", "surface", "surfaceRef", "ref", "surfaceId", "id");
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

    private static bool TryGetTargetIndex(JsonElement? parameters, int count, out int targetIndex, out V2Error? error)
    {
        targetIndex = -1;
        error = null;
        var raw = GetStringParam(parameters, "targetIndex", "toIndex", "index");
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = new V2Error(V2ErrorCodes.InvalidRef, "Missing required targetIndex.");
            return false;
        }

        if (!int.TryParse(raw, out var requested) || !TryResolveCollectionIndex(requested, count, out targetIndex))
        {
            error = new V2Error(V2ErrorCodes.InvalidRef, $"Surface targetIndex out of range: {raw}");
            return false;
        }

        return true;
    }

    private static bool TryGetRequestedOrder(
        JsonElement? parameters,
        SurfaceApiWorkspace<TWorkspace, TSurface> workspace,
        out List<string> surfaceIds,
        out V2Error? error)
    {
        surfaceIds = [];
        error = null;
        var tokens = GetOrderTokens(parameters);
        if (tokens.Count == 0)
        {
            error = new V2Error(V2ErrorCodes.InvalidRef, "Missing required order.");
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (!TryResolveSurfaceToken(workspace, token, out var surface, out error))
                return false;

            if (!seen.Add(surface!.SurfaceId))
            {
                error = new V2Error(V2ErrorCodes.InvalidRef, $"Duplicate surface in order: {token}");
                return false;
            }

            surfaceIds.Add(surface.SurfaceId);
        }

        if (surfaceIds.Count != workspace.Surfaces.Count)
        {
            error = new V2Error(
                V2ErrorCodes.InvalidRef,
                $"Surface order must include every surface exactly once. Expected {workspace.Surfaces.Count}, got {surfaceIds.Count}.");
            return false;
        }

        return true;
    }

    private static bool TryResolveSurfaceToken(
        SurfaceApiWorkspace<TWorkspace, TSurface> workspace,
        string token,
        out SurfaceApiSurface<TSurface>? surface,
        out V2Error? error)
    {
        surface = null;
        error = null;
        if (ShortRef.TryParse(token, out var shortRef))
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
            surface = workspace.Surfaces.FirstOrDefault(item => string.Equals(item.SurfaceId, token, StringComparison.Ordinal))
                ?? workspace.Surfaces.FirstOrDefault(item => string.Equals(item.SurfaceName, token, StringComparison.OrdinalIgnoreCase));
        }

        if (surface == null)
        {
            error = new V2Error(V2ErrorCodes.NotFound, $"Surface not found: {token}");
            return false;
        }

        return true;
    }

    private SurfaceApiWorkspace<TWorkspace, TSurface>? FindWorkspaceById(string workspaceId)
    {
        return GetWorkspaces().FirstOrDefault(item => string.Equals(item.WorkspaceId, workspaceId, StringComparison.Ordinal));
    }

    private List<SurfaceApiWorkspace<TWorkspace, TSurface>> GetWorkspaces()
    {
        return _workspaceProvider().ToList();
    }

    private static List<object> CreateSurfaceOrderResult(SurfaceApiWorkspace<TWorkspace, TSurface> workspace, CliIdFormat idFormat)
    {
        return workspace.Surfaces
            .Select(surface => (object)CreateSurfaceResult(surface, idFormat))
            .ToList();
    }

    private static Dictionary<string, object?> CreateWorkspaceResult(SurfaceApiWorkspace<TWorkspace, TSurface> workspace, CliIdFormat idFormat)
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

    private static Dictionary<string, object?> CreateSurfaceResult(SurfaceApiSurface<TSurface> surface, CliIdFormat idFormat)
    {
        var result = new Dictionary<string, object?>
        {
            ["name"] = surface.SurfaceName,
            ["kind"] = surface.Kind,
            ["isCurrent"] = surface.IsCurrent,
        };

        if (idFormat is CliIdFormat.Refs or CliIdFormat.Both)
            result["ref"] = surface.SurfaceRef.ToString();
        if (idFormat is CliIdFormat.Uuids or CliIdFormat.Both)
            result["id"] = surface.SurfaceId;

        return result;
    }

    private static List<string> GetOrderTokens(JsonElement? parameters)
    {
        var tokens = new List<string>();
        if (parameters is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("order", out var order))
        {
            return tokens;
        }

        if (order.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in order.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    tokens.Add(item.GetString()!);
            }
        }
        else if (order.ValueKind == JsonValueKind.String)
        {
            tokens.AddRange(order.GetString()!
                .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return tokens;
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

    private static bool TryResolveCollectionIndex(int requested, int count, out int zeroBasedIndex)
    {
        zeroBasedIndex = -1;
        if (count <= 0)
            return false;

        if (requested >= 1 && requested <= count)
        {
            zeroBasedIndex = requested - 1;
            return true;
        }

        if (requested >= 0 && requested < count)
        {
            zeroBasedIndex = requested;
            return true;
        }

        return false;
    }
}
