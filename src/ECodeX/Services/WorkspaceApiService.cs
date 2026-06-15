using System.Text.Json;
using ECodeX.Core.IPC.V2;
using ECodeX.Core.Models;
using ECodeX.Core.Services;

namespace ECodeX.Services;

public sealed record WorkspaceApiWorkspace<TWorkspace>(
    TWorkspace Workspace,
    string WorkspaceId,
    string WorkspaceName,
    ShortRef WorkspaceRef,
    bool IsCurrent,
    int SurfaceCount,
    string? WorkingDirectory);

public sealed class WorkspaceApiService<TWorkspace>
    where TWorkspace : class
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        "workspace.list",
        "workspace.create",
        "workspace.select",
        "workspace.close",
        "workspace.rename",
        "workspace.reorder",
    };

    private readonly Func<IEnumerable<WorkspaceApiWorkspace<TWorkspace>>> _workspaceProvider;
    private readonly Func<string?, TWorkspace> _createWorkspace;
    private readonly Action<TWorkspace> _selectWorkspace;
    private readonly Func<TWorkspace, bool> _closeWorkspace;
    private readonly Func<TWorkspace, string, bool> _renameWorkspace;
    private readonly Func<IReadOnlyList<string>, bool> _reorderWorkspaces;

    public WorkspaceApiService(
        Func<IEnumerable<WorkspaceApiWorkspace<TWorkspace>>> workspaceProvider,
        Func<string?, TWorkspace> createWorkspace,
        Action<TWorkspace> selectWorkspace,
        Func<TWorkspace, bool> closeWorkspace,
        Func<TWorkspace, string, bool> renameWorkspace,
        Func<IReadOnlyList<string>, bool> reorderWorkspaces)
    {
        _workspaceProvider = workspaceProvider;
        _createWorkspace = createWorkspace;
        _selectWorkspace = selectWorkspace;
        _closeWorkspace = closeWorkspace;
        _renameWorkspace = renameWorkspace;
        _reorderWorkspaces = reorderWorkspaces;
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
            "workspace.list" => V2Response.FromResult(request.Id, CreateListResult(idFormat)),
            "workspace.create" => HandleCreate(request, idFormat),
            "workspace.select" => HandleSelect(request, idFormat),
            "workspace.close" => HandleClose(request, idFormat),
            "workspace.rename" => HandleRename(request, idFormat),
            "workspace.reorder" => HandleReorder(request, idFormat),
            _ => V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotSupported,
                $"ecodex.v2 workspace method is not supported: {request.Method}"),
        };
    }

    private object CreateListResult(CliIdFormat idFormat)
    {
        var workspaces = GetWorkspaces();
        var current = workspaces.FirstOrDefault(workspace => workspace.IsCurrent);
        return new
        {
            workspaces = workspaces.Select(workspace => CreateWorkspaceResult(workspace, idFormat)).ToList(),
            current = current == null ? null : CreateWorkspaceResult(current, idFormat),
        };
    }

    private V2Response HandleCreate(V2Request request, CliIdFormat idFormat)
    {
        var name = GetStringParam(request.Params, "name", "title");
        var created = _createWorkspace(name);
        var workspace = FindWorkspaceByInstance(created)
            ?? GetWorkspaces().FirstOrDefault(item => item.IsCurrent);

        return V2Response.FromResult(request.Id, new
        {
            created = true,
            workspace = workspace == null ? null : CreateWorkspaceResult(workspace, idFormat),
        });
    }

    private V2Response HandleSelect(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveWorkspace(request.Params, requireTarget: true, out var workspace, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        _selectWorkspace(workspace!.Workspace);
        var selected = FindWorkspaceById(workspace.WorkspaceId) ?? workspace;
        return V2Response.FromResult(request.Id, new
        {
            selected = true,
            workspace = CreateWorkspaceResult(selected, idFormat),
        });
    }

    private V2Response HandleClose(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveWorkspace(request.Params, requireTarget: false, out var workspace, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!_closeWorkspace(workspace!.Workspace))
        {
            return V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotSupported,
                $"Workspace close was not applied: {workspace.WorkspaceId}");
        }

        var current = GetWorkspaces().FirstOrDefault(item => item.IsCurrent);
        return V2Response.FromResult(request.Id, new
        {
            closed = true,
            workspace = CreateWorkspaceResult(workspace, idFormat),
            current = current == null ? null : CreateWorkspaceResult(current, idFormat),
        });
    }

    private V2Response HandleRename(V2Request request, CliIdFormat idFormat)
    {
        if (!TryResolveWorkspace(request.Params, requireTarget: false, out var workspace, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        var name = GetStringParam(request.Params, "name", "title");
        if (string.IsNullOrWhiteSpace(name))
        {
            return V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.InvalidRef,
                "Missing required workspace name.");
        }

        if (!_renameWorkspace(workspace!.Workspace, name.Trim()))
        {
            return V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotSupported,
                $"Workspace rename was not applied: {workspace.WorkspaceId}");
        }

        var renamed = FindWorkspaceById(workspace.WorkspaceId) ?? workspace;
        return V2Response.FromResult(request.Id, new
        {
            renamed = true,
            workspace = CreateWorkspaceResult(renamed, idFormat),
        });
    }

    private V2Response HandleReorder(V2Request request, CliIdFormat idFormat)
    {
        if (!TryGetRequestedOrder(request.Params, out var workspaceIds, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        if (!_reorderWorkspaces(workspaceIds))
        {
            return V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotSupported,
                "Workspace reorder was not applied.");
        }

        var workspaces = GetWorkspaces();
        return V2Response.FromResult(request.Id, new
        {
            reordered = true,
            workspaces = workspaces.Select(workspace => CreateWorkspaceResult(workspace, idFormat)).ToList(),
        });
    }

    private bool TryResolveWorkspace(
        JsonElement? parameters,
        bool requireTarget,
        out WorkspaceApiWorkspace<TWorkspace>? workspace,
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

        var target = GetStringParam(parameters, "target", "workspace", "workspaceRef", "ref", "workspaceId", "id");
        if (string.Equals(target, "current", StringComparison.OrdinalIgnoreCase))
            target = workspaces.FirstOrDefault(item => item.IsCurrent)?.WorkspaceId;

        if (string.IsNullOrWhiteSpace(target))
        {
            if (requireTarget)
            {
                error = new V2Error(V2ErrorCodes.InvalidRef, "Missing required workspace target.");
                return false;
            }

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

    private bool TryGetRequestedOrder(JsonElement? parameters, out List<string> workspaceIds, out V2Error? error)
    {
        workspaceIds = [];
        error = null;
        var workspaces = GetWorkspaces();
        var tokens = GetOrderTokens(parameters);
        if (tokens.Count == 0)
        {
            error = new V2Error(V2ErrorCodes.InvalidRef, "Missing required order.");
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (!TryResolveWorkspaceToken(workspaces, token, out var workspace, out error))
                return false;

            if (!seen.Add(workspace!.WorkspaceId))
            {
                error = new V2Error(V2ErrorCodes.InvalidRef, $"Duplicate workspace in order: {token}");
                return false;
            }

            workspaceIds.Add(workspace.WorkspaceId);
        }

        if (workspaceIds.Count != workspaces.Count)
        {
            error = new V2Error(
                V2ErrorCodes.InvalidRef,
                $"Workspace order must include every workspace exactly once. Expected {workspaces.Count}, got {workspaceIds.Count}.");
            return false;
        }

        return true;
    }

    private static bool TryResolveWorkspaceToken(
        IReadOnlyList<WorkspaceApiWorkspace<TWorkspace>> workspaces,
        string token,
        out WorkspaceApiWorkspace<TWorkspace>? workspace,
        out V2Error? error)
    {
        workspace = null;
        error = null;
        if (ShortRef.TryParse(token, out var shortRef))
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
            workspace = workspaces.FirstOrDefault(item => string.Equals(item.WorkspaceId, token, StringComparison.Ordinal))
                ?? workspaces.FirstOrDefault(item => string.Equals(item.WorkspaceName, token, StringComparison.OrdinalIgnoreCase));
        }

        if (workspace == null)
        {
            error = new V2Error(V2ErrorCodes.NotFound, $"Workspace not found: {token}");
            return false;
        }

        return true;
    }

    private WorkspaceApiWorkspace<TWorkspace>? FindWorkspaceByInstance(TWorkspace workspace)
    {
        return GetWorkspaces().FirstOrDefault(item => ReferenceEquals(item.Workspace, workspace));
    }

    private WorkspaceApiWorkspace<TWorkspace>? FindWorkspaceById(string workspaceId)
    {
        return GetWorkspaces().FirstOrDefault(item => string.Equals(item.WorkspaceId, workspaceId, StringComparison.Ordinal));
    }

    private List<WorkspaceApiWorkspace<TWorkspace>> GetWorkspaces()
    {
        return _workspaceProvider().ToList();
    }

    private static Dictionary<string, object?> CreateWorkspaceResult(WorkspaceApiWorkspace<TWorkspace> workspace, CliIdFormat idFormat)
    {
        var result = new Dictionary<string, object?>
        {
            ["name"] = workspace.WorkspaceName,
            ["isCurrent"] = workspace.IsCurrent,
            ["surfaces"] = workspace.SurfaceCount,
        };

        if (!string.IsNullOrWhiteSpace(workspace.WorkingDirectory))
            result["workingDirectory"] = workspace.WorkingDirectory;

        if (idFormat is CliIdFormat.Refs or CliIdFormat.Both)
            result["ref"] = workspace.WorkspaceRef.ToString();
        if (idFormat is CliIdFormat.Uuids or CliIdFormat.Both)
            result["id"] = workspace.WorkspaceId;

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
}
