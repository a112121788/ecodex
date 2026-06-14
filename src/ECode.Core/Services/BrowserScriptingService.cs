using System;
using System.Collections.Generic;
using System.Linq;
using ECode.Core.IPC.V2;
using ECode.Core.Models;

namespace ECode.Core.Services;

public sealed class BrowserScriptingService
{
    private const string SurfaceRefPrefix = "surface:";
    private readonly Func<IEnumerable<BrowserScriptingSurfaceDescriptor>> _surfaceProvider;
    private readonly Func<string, BrowserScriptingSnapshot?> _snapshotProvider;
    private readonly Func<BrowserScriptingActionRequest, BrowserScriptingActionOutcome>? _actionExecutor;
    private readonly Func<BrowserScriptingStateRequest, BrowserScriptingStateOutcome>? _stateExecutor;
    private readonly Func<BrowserScriptingControlRequest, BrowserScriptingControlOutcome>? _controlExecutor;
    private readonly Dictionary<string, BrowserScriptingRef> _surfaceRefs = new(StringComparer.Ordinal);

    public BrowserScriptingService(
        Func<IEnumerable<BrowserScriptingSurfaceDescriptor>> surfaceProvider,
        Func<string, BrowserScriptingSnapshot?>? snapshotProvider = null,
        Func<BrowserScriptingActionRequest, BrowserScriptingActionOutcome>? actionExecutor = null,
        Func<BrowserScriptingStateRequest, BrowserScriptingStateOutcome>? stateExecutor = null,
        Func<BrowserScriptingControlRequest, BrowserScriptingControlOutcome>? controlExecutor = null)
    {
        _surfaceProvider = surfaceProvider ?? throw new ArgumentNullException(nameof(surfaceProvider));
        _snapshotProvider = snapshotProvider ?? (_ => null);
        _actionExecutor = actionExecutor;
        _stateExecutor = stateExecutor;
        _controlExecutor = controlExecutor;
    }

    public string TrackSurface(BrowserScriptingSurfaceDescriptor surface)
    {
        var surfaceRef = CreateSurfaceRef(surface.SurfaceId);
        _surfaceRefs[surfaceRef] = new BrowserScriptingRef(
            surfaceRef,
            surface.WorkspaceId,
            surface.SurfaceId,
            DateTimeOffset.UtcNow);
        return surfaceRef;
    }

    public BrowserScriptingDiagnostics GetDiagnostics()
    {
        var surfaces = GetCurrentSurfaces();
        return CreateDiagnostics(surfaces, null, null);
    }

    public BrowserScriptingResolveResult ResolveSurfaceRef(string? surfaceRef)
    {
        var surfaces = GetCurrentSurfaces();
        if (!TryParseSurfaceRef(surfaceRef, out var surfaceId))
        {
            return Error(
                V2ErrorCodes.InvalidRef,
                "surfaceRef must use the format surface:<surfaceId>.",
                surfaces,
                surfaceRef,
                null);
        }

        var normalizedRef = CreateSurfaceRef(surfaceId);
        var wasTracked = _surfaceRefs.ContainsKey(normalizedRef);
        var surface = surfaces.FirstOrDefault(item => string.Equals(item.SurfaceId, surfaceId, StringComparison.Ordinal));
        if (surface == null)
        {
            return Error(
                wasTracked ? V2ErrorCodes.StaleRef : V2ErrorCodes.NotFound,
                wasTracked ? $"Surface reference is stale: {normalizedRef}" : $"Browser surface not found: {surfaceId}",
                surfaces,
                normalizedRef,
                surfaceId);
        }

        if (surface.Kind != SurfaceKind.Browser)
        {
            return Error(
                V2ErrorCodes.NotSupported,
                $"Surface is not a browser surface: {surfaceId}",
                surfaces,
                normalizedRef,
                surfaceId);
        }

        TrackSurface(surface);
        return new BrowserScriptingResolveResult(
            Success: true,
            Surface: surface,
            Error: null,
            Diagnostics: CreateDiagnostics(surfaces, normalizedRef, surfaceId));
    }

    public BrowserScriptingSnapshotResult GetSnapshot(string? surfaceRef)
    {
        var resolved = ResolveSurfaceRef(surfaceRef);
        if (!resolved.Success)
        {
            return new BrowserScriptingSnapshotResult(
                Success: false,
                Snapshot: null,
                Error: resolved.Error,
                Diagnostics: resolved.Diagnostics);
        }

        var snapshot = _snapshotProvider(resolved.Surface!.SurfaceId);
        if (snapshot == null)
        {
            return new BrowserScriptingSnapshotResult(
                Success: false,
                Snapshot: null,
                Error: new V2Error(V2ErrorCodes.NotFound, $"Browser snapshot not available: {resolved.Surface.SurfaceId}"),
                Diagnostics: resolved.Diagnostics);
        }

        return new BrowserScriptingSnapshotResult(
            Success: true,
            Snapshot: snapshot,
            Error: null,
            Diagnostics: resolved.Diagnostics);
    }

    public BrowserScriptingLocatorResult FindByRole(string? surfaceRef, string role, string? name = null)
    {
        return Find(surfaceRef, BrowserScriptingLocator.Role(role, name));
    }

    public BrowserScriptingLocatorResult FindByText(string? surfaceRef, string text)
    {
        return Find(surfaceRef, BrowserScriptingLocator.Text(text));
    }

    public BrowserScriptingLocatorResult FindByTestId(string? surfaceRef, string testId)
    {
        return Find(surfaceRef, BrowserScriptingLocator.TestId(testId));
    }

    public BrowserScriptingLocatorResult FindFirst(string? surfaceRef, BrowserScriptingLocator locator)
    {
        return Find(surfaceRef, BrowserScriptingLocator.First(locator));
    }

    public BrowserScriptingLocatorResult FindLast(string? surfaceRef, BrowserScriptingLocator locator)
    {
        return Find(surfaceRef, BrowserScriptingLocator.Last(locator));
    }

    public BrowserScriptingLocatorResult FindNth(string? surfaceRef, BrowserScriptingLocator locator, int index)
    {
        return Find(surfaceRef, BrowserScriptingLocator.Nth(locator, index));
    }

    public BrowserScriptingLocatorResult Find(string? surfaceRef, BrowserScriptingLocator locator)
    {
        var snapshotResult = GetSnapshot(surfaceRef);
        if (!snapshotResult.Success)
        {
            return new BrowserScriptingLocatorResult(
                Success: false,
                Nodes: [],
                Error: snapshotResult.Error,
                Diagnostics: snapshotResult.Diagnostics);
        }

        var nodes = EvaluateLocator(snapshotResult.Snapshot!, locator);
        if (locator.Kind is BrowserScriptingLocatorKind.First or BrowserScriptingLocatorKind.Last or BrowserScriptingLocatorKind.Nth &&
            nodes.Count == 0)
        {
            return new BrowserScriptingLocatorResult(
                Success: false,
                Nodes: [],
                Error: new V2Error(V2ErrorCodes.NotFound, "Locator did not match any node."),
                Diagnostics: snapshotResult.Diagnostics);
        }

        return new BrowserScriptingLocatorResult(
            Success: true,
            Nodes: nodes,
            Error: null,
            Diagnostics: snapshotResult.Diagnostics);
    }

    public BrowserScriptingActionResult Click(string? surfaceRef, BrowserScriptingLocator locator)
    {
        return ExecuteNodeAction(surfaceRef, locator, BrowserScriptingActionKind.Click);
    }

    public BrowserScriptingActionResult Fill(string? surfaceRef, BrowserScriptingLocator locator, string value)
    {
        return ExecuteNodeAction(surfaceRef, locator, BrowserScriptingActionKind.Fill, value: value);
    }

    public BrowserScriptingActionResult Hover(string? surfaceRef, BrowserScriptingLocator locator)
    {
        return ExecuteNodeAction(surfaceRef, locator, BrowserScriptingActionKind.Hover);
    }

    public BrowserScriptingActionResult Press(string? surfaceRef, BrowserScriptingLocator locator, string key)
    {
        return ExecuteNodeAction(surfaceRef, locator, BrowserScriptingActionKind.Press, key: key);
    }

    public BrowserScriptingActionResult Eval(string? surfaceRef, string script)
    {
        return ExecuteSurfaceAction(surfaceRef, BrowserScriptingActionKind.Eval, script: script);
    }

    public BrowserScriptingActionResult Screenshot(string? surfaceRef)
    {
        return ExecuteSurfaceAction(surfaceRef, BrowserScriptingActionKind.Screenshot);
    }

    public BrowserScriptingStateResult CookiesGet(string? surfaceRef, string? name = null)
    {
        return ExecuteState(surfaceRef, BrowserScriptingStateRequest.CookiesGet("", name));
    }

    public BrowserScriptingStateResult CookiesSet(string? surfaceRef, BrowserScriptingCookie cookie)
    {
        return ExecuteState(surfaceRef, BrowserScriptingStateRequest.CookiesSet("", cookie));
    }

    public BrowserScriptingStateResult CookiesClear(string? surfaceRef, string? name = null)
    {
        return ExecuteState(surfaceRef, BrowserScriptingStateRequest.CookiesClear("", name));
    }

    public BrowserScriptingStateResult StorageGet(
        string? surfaceRef,
        string? key = null,
        BrowserScriptingStorageArea area = BrowserScriptingStorageArea.Local)
    {
        return ExecuteState(surfaceRef, BrowserScriptingStateRequest.StorageGet("", area, key));
    }

    public BrowserScriptingStateResult StorageSet(
        string? surfaceRef,
        string key,
        string value,
        BrowserScriptingStorageArea area = BrowserScriptingStorageArea.Local)
    {
        return ExecuteState(surfaceRef, BrowserScriptingStateRequest.StorageSet("", area, key, value));
    }

    public BrowserScriptingStateResult StorageClear(
        string? surfaceRef,
        string? key = null,
        BrowserScriptingStorageArea area = BrowserScriptingStorageArea.Local)
    {
        return ExecuteState(surfaceRef, BrowserScriptingStateRequest.StorageClear("", area, key));
    }

    public BrowserScriptingControlResult ConsoleList(string? surfaceRef)
    {
        return ExecuteSurfaceControl(surfaceRef, BrowserScriptingControlKind.ConsoleList);
    }

    public BrowserScriptingControlResult ConsoleClear(string? surfaceRef)
    {
        return ExecuteSurfaceControl(surfaceRef, BrowserScriptingControlKind.ConsoleClear);
    }

    public BrowserScriptingControlResult DialogAccept(string? surfaceRef, string? promptText = null)
    {
        return ExecuteSurfaceControl(surfaceRef, BrowserScriptingControlKind.DialogAccept, text: promptText);
    }

    public BrowserScriptingControlResult DialogDismiss(string? surfaceRef)
    {
        return ExecuteSurfaceControl(surfaceRef, BrowserScriptingControlKind.DialogDismiss);
    }

    public BrowserScriptingControlResult DownloadWait(string? surfaceRef, string? fileName = null, int? timeoutMs = null)
    {
        return ExecuteSurfaceControl(surfaceRef, BrowserScriptingControlKind.DownloadWait, fileName: fileName, timeoutMs: timeoutMs);
    }

    public BrowserScriptingControlResult Highlight(string? surfaceRef, BrowserScriptingLocator locator)
    {
        return ExecuteNodeControl(surfaceRef, locator, BrowserScriptingControlKind.Highlight);
    }

    public BrowserScriptingControlResult AddInitScript(string? surfaceRef, string script)
    {
        return ExecuteSurfaceControl(surfaceRef, BrowserScriptingControlKind.AddInitScript, text: script);
    }

    public BrowserScriptingControlResult AddScript(string? surfaceRef, string script)
    {
        return ExecuteSurfaceControl(surfaceRef, BrowserScriptingControlKind.AddScript, text: script);
    }

    public BrowserScriptingControlResult AddStyle(string? surfaceRef, string css)
    {
        return ExecuteSurfaceControl(surfaceRef, BrowserScriptingControlKind.AddStyle, text: css);
    }

    public static string CreateSurfaceRef(string surfaceId)
    {
        return SurfaceRefPrefix + surfaceId;
    }

    private static bool TryParseSurfaceRef(string? surfaceRef, out string surfaceId)
    {
        surfaceId = "";
        if (string.IsNullOrWhiteSpace(surfaceRef) ||
            !surfaceRef.StartsWith(SurfaceRefPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        surfaceId = surfaceRef[SurfaceRefPrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(surfaceId);
    }

    private IReadOnlyList<BrowserScriptingSurfaceDescriptor> GetCurrentSurfaces()
    {
        return _surfaceProvider()
            .Where(surface => !string.IsNullOrWhiteSpace(surface.SurfaceId))
            .ToList();
    }

    private BrowserScriptingResolveResult Error(
        string code,
        string message,
        IReadOnlyList<BrowserScriptingSurfaceDescriptor> surfaces,
        string? surfaceRef,
        string? surfaceId)
    {
        return new BrowserScriptingResolveResult(
            Success: false,
            Surface: null,
            Error: new V2Error(code, message),
            Diagnostics: CreateDiagnostics(surfaces, surfaceRef, surfaceId));
    }

    private BrowserScriptingDiagnostics CreateDiagnostics(
        IReadOnlyList<BrowserScriptingSurfaceDescriptor> surfaces,
        string? surfaceRef,
        string? surfaceId)
    {
        return new BrowserScriptingDiagnostics(
            LiveSurfaceCount: surfaces.Count,
            LiveBrowserSurfaceCount: surfaces.Count(surface => surface.Kind == SurfaceKind.Browser),
            RegisteredRefCount: _surfaceRefs.Count,
            SurfaceRef: surfaceRef,
            SurfaceId: surfaceId);
    }

    private BrowserScriptingActionResult ExecuteNodeAction(
        string? surfaceRef,
        BrowserScriptingLocator locator,
        BrowserScriptingActionKind action,
        string? value = null,
        string? key = null)
    {
        var target = FindFirst(surfaceRef, locator);
        if (!target.Success)
        {
            return new BrowserScriptingActionResult(
                Success: false,
                Value: null,
                Error: target.Error,
                Diagnostics: target.Diagnostics);
        }

        var request = new BrowserScriptingActionRequest(
            SurfaceId: target.Diagnostics.SurfaceId ?? "",
            Action: action,
            Node: target.Nodes[0],
            Value: value,
            Key: key,
            Script: null);

        return ExecuteAction(request, target.Diagnostics);
    }

    private BrowserScriptingActionResult ExecuteSurfaceAction(
        string? surfaceRef,
        BrowserScriptingActionKind action,
        string? script = null)
    {
        var resolved = ResolveSurfaceRef(surfaceRef);
        if (!resolved.Success)
        {
            return new BrowserScriptingActionResult(
                Success: false,
                Value: null,
                Error: resolved.Error,
                Diagnostics: resolved.Diagnostics);
        }

        var request = new BrowserScriptingActionRequest(
            SurfaceId: resolved.Surface!.SurfaceId,
            Action: action,
            Node: null,
            Value: null,
            Key: null,
            Script: script);

        return ExecuteAction(request, resolved.Diagnostics);
    }

    private BrowserScriptingActionResult ExecuteAction(
        BrowserScriptingActionRequest request,
        BrowserScriptingDiagnostics diagnostics)
    {
        if (_actionExecutor == null)
        {
            return new BrowserScriptingActionResult(
                Success: false,
                Value: null,
                Error: new V2Error(V2ErrorCodes.NotSupported, $"Browser action is not wired: {request.Action}"),
                Diagnostics: diagnostics);
        }

        try
        {
            var outcome = _actionExecutor(request);
            return new BrowserScriptingActionResult(
                Success: outcome.Success,
                Value: outcome.Value,
                Error: outcome.Error,
                Diagnostics: diagnostics);
        }
        catch (TimeoutException ex)
        {
            return new BrowserScriptingActionResult(
                Success: false,
                Value: null,
                Error: new V2Error(V2ErrorCodes.Timeout, ex.Message),
                Diagnostics: diagnostics);
        }
        catch (Exception ex)
        {
            return new BrowserScriptingActionResult(
                Success: false,
                Value: null,
                Error: new V2Error(V2ErrorCodes.InternalError, ex.Message),
                Diagnostics: diagnostics);
        }
    }

    private BrowserScriptingStateResult ExecuteState(
        string? surfaceRef,
        BrowserScriptingStateRequest request)
    {
        var resolved = ResolveSurfaceRef(surfaceRef);
        if (!resolved.Success)
        {
            return new BrowserScriptingStateResult(
                Success: false,
                Value: null,
                Error: resolved.Error,
                Diagnostics: resolved.Diagnostics);
        }

        var routedRequest = request with { SurfaceId = resolved.Surface!.SurfaceId };
        if (_stateExecutor == null)
        {
            return new BrowserScriptingStateResult(
                Success: false,
                Value: null,
                Error: new V2Error(V2ErrorCodes.NotSupported, $"Browser state operation is not wired: {request.Kind}"),
                Diagnostics: resolved.Diagnostics);
        }

        try
        {
            var outcome = _stateExecutor(routedRequest);
            return new BrowserScriptingStateResult(
                Success: outcome.Success,
                Value: outcome.Value,
                Error: outcome.Error,
                Diagnostics: resolved.Diagnostics);
        }
        catch (TimeoutException ex)
        {
            return new BrowserScriptingStateResult(
                Success: false,
                Value: null,
                Error: new V2Error(V2ErrorCodes.Timeout, ex.Message),
                Diagnostics: resolved.Diagnostics);
        }
        catch (Exception ex)
        {
            return new BrowserScriptingStateResult(
                Success: false,
                Value: null,
                Error: new V2Error(V2ErrorCodes.InternalError, ex.Message),
                Diagnostics: resolved.Diagnostics);
        }
    }

    private BrowserScriptingControlResult ExecuteNodeControl(
        string? surfaceRef,
        BrowserScriptingLocator locator,
        BrowserScriptingControlKind kind)
    {
        var target = FindFirst(surfaceRef, locator);
        if (!target.Success)
        {
            return new BrowserScriptingControlResult(
                Success: false,
                Value: null,
                Error: target.Error,
                Diagnostics: target.Diagnostics);
        }

        var request = new BrowserScriptingControlRequest(
            SurfaceId: target.Diagnostics.SurfaceId ?? "",
            Kind: kind,
            Node: target.Nodes[0]);

        return ExecuteControl(request, target.Diagnostics);
    }

    private BrowserScriptingControlResult ExecuteSurfaceControl(
        string? surfaceRef,
        BrowserScriptingControlKind kind,
        string? text = null,
        string? fileName = null,
        int? timeoutMs = null)
    {
        var resolved = ResolveSurfaceRef(surfaceRef);
        if (!resolved.Success)
        {
            return new BrowserScriptingControlResult(
                Success: false,
                Value: null,
                Error: resolved.Error,
                Diagnostics: resolved.Diagnostics);
        }

        var request = new BrowserScriptingControlRequest(
            SurfaceId: resolved.Surface!.SurfaceId,
            Kind: kind,
            Node: null,
            Text: text,
            FileName: fileName,
            TimeoutMs: timeoutMs);

        return ExecuteControl(request, resolved.Diagnostics);
    }

    private BrowserScriptingControlResult ExecuteControl(
        BrowserScriptingControlRequest request,
        BrowserScriptingDiagnostics diagnostics)
    {
        if (_controlExecutor == null)
        {
            return new BrowserScriptingControlResult(
                Success: false,
                Value: null,
                Error: new V2Error(V2ErrorCodes.NotSupported, $"Browser control operation is not wired: {request.Kind}"),
                Diagnostics: diagnostics);
        }

        try
        {
            var outcome = _controlExecutor(request);
            return new BrowserScriptingControlResult(
                Success: outcome.Success,
                Value: outcome.Value,
                Error: outcome.Error,
                Diagnostics: diagnostics);
        }
        catch (TimeoutException ex)
        {
            return new BrowserScriptingControlResult(
                Success: false,
                Value: null,
                Error: new V2Error(V2ErrorCodes.Timeout, ex.Message),
                Diagnostics: diagnostics);
        }
        catch (Exception ex)
        {
            return new BrowserScriptingControlResult(
                Success: false,
                Value: null,
                Error: new V2Error(V2ErrorCodes.InternalError, ex.Message),
                Diagnostics: diagnostics);
        }
    }

    private static IReadOnlyList<BrowserScriptingNode> EvaluateLocator(
        BrowserScriptingSnapshot snapshot,
        BrowserScriptingLocator locator)
    {
        var nodes = Flatten(snapshot.Root)
            .Where(node => node.Visible)
            .ToList();

        return locator.Kind switch
        {
            BrowserScriptingLocatorKind.Role => nodes
                .Where(node => EqualsIgnoreCase(node.Role, locator.Value))
                .Where(node => string.IsNullOrWhiteSpace(locator.Name) || EqualsIgnoreCase(node.Name, locator.Name))
                .ToList(),
            BrowserScriptingLocatorKind.Text => nodes
                .Where(node => ContainsIgnoreCase(node.Text, locator.Value) || ContainsIgnoreCase(node.Name, locator.Value))
                .ToList(),
            BrowserScriptingLocatorKind.TestId => nodes
                .Where(node => string.Equals(node.TestId, locator.Value, StringComparison.Ordinal))
                .ToList(),
            BrowserScriptingLocatorKind.First => EvaluateNested(snapshot, locator).Take(1).ToList(),
            BrowserScriptingLocatorKind.Last => EvaluateNested(snapshot, locator).TakeLast(1).ToList(),
            BrowserScriptingLocatorKind.Nth => EvaluateNested(snapshot, locator).Skip(Math.Max(0, locator.Index)).Take(1).ToList(),
            _ => [],
        };
    }

    private static IReadOnlyList<BrowserScriptingNode> EvaluateNested(
        BrowserScriptingSnapshot snapshot,
        BrowserScriptingLocator locator)
    {
        return locator.Inner == null ? [] : EvaluateLocator(snapshot, locator.Inner);
    }

    private static IEnumerable<BrowserScriptingNode> Flatten(BrowserScriptingNode node)
    {
        yield return node;

        foreach (var child in node.Children)
        {
            foreach (var nested in Flatten(child))
                yield return nested;
        }
    }

    private static bool EqualsIgnoreCase(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsIgnoreCase(string? haystack, string? needle)
    {
        return !string.IsNullOrWhiteSpace(haystack) &&
               !string.IsNullOrWhiteSpace(needle) &&
               haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record BrowserScriptingSurfaceDescriptor(
    string WorkspaceId,
    string WorkspaceName,
    string SurfaceId,
    string SurfaceName,
    SurfaceKind Kind,
    string? Url,
    string? Title);

public sealed record BrowserScriptingRef(
    string Value,
    string WorkspaceId,
    string SurfaceId,
    DateTimeOffset CreatedAtUtc);

public sealed record BrowserScriptingDiagnostics(
    int LiveSurfaceCount,
    int LiveBrowserSurfaceCount,
    int RegisteredRefCount,
    string? SurfaceRef,
    string? SurfaceId);

public sealed record BrowserScriptingResolveResult(
    bool Success,
    BrowserScriptingSurfaceDescriptor? Surface,
    V2Error? Error,
    BrowserScriptingDiagnostics Diagnostics);

public sealed record BrowserScriptingSnapshot(BrowserScriptingNode Root);

public sealed record BrowserScriptingNode
{
    public string NodeId { get; init; } = Guid.NewGuid().ToString();
    public string Role { get; init; } = "";
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";
    public string? TestId { get; init; }
    public bool Visible { get; init; } = true;
    public IReadOnlyList<BrowserScriptingNode> Children { get; init; } = [];
}

public enum BrowserScriptingLocatorKind
{
    Role,
    Text,
    TestId,
    First,
    Last,
    Nth,
}

public sealed record BrowserScriptingLocator(
    BrowserScriptingLocatorKind Kind,
    string? Value = null,
    string? Name = null,
    BrowserScriptingLocator? Inner = null,
    int Index = 0)
{
    public static BrowserScriptingLocator Role(string role, string? name = null) =>
        new(BrowserScriptingLocatorKind.Role, role, name);

    public static BrowserScriptingLocator Text(string text) =>
        new(BrowserScriptingLocatorKind.Text, text);

    public static BrowserScriptingLocator TestId(string testId) =>
        new(BrowserScriptingLocatorKind.TestId, testId);

    public static BrowserScriptingLocator First(BrowserScriptingLocator inner) =>
        new(BrowserScriptingLocatorKind.First, Inner: inner);

    public static BrowserScriptingLocator Last(BrowserScriptingLocator inner) =>
        new(BrowserScriptingLocatorKind.Last, Inner: inner);

    public static BrowserScriptingLocator Nth(BrowserScriptingLocator inner, int index) =>
        new(BrowserScriptingLocatorKind.Nth, Inner: inner, Index: index);
}

public sealed record BrowserScriptingSnapshotResult(
    bool Success,
    BrowserScriptingSnapshot? Snapshot,
    V2Error? Error,
    BrowserScriptingDiagnostics Diagnostics);

public sealed record BrowserScriptingLocatorResult(
    bool Success,
    IReadOnlyList<BrowserScriptingNode> Nodes,
    V2Error? Error,
    BrowserScriptingDiagnostics Diagnostics);

public enum BrowserScriptingActionKind
{
    Click,
    Fill,
    Hover,
    Press,
    Eval,
    Screenshot,
}

public sealed record BrowserScriptingActionRequest(
    string SurfaceId,
    BrowserScriptingActionKind Action,
    BrowserScriptingNode? Node,
    string? Value,
    string? Key,
    string? Script);

public sealed record BrowserScriptingActionOutcome(
    bool Success,
    object? Value = null,
    V2Error? Error = null)
{
    public static BrowserScriptingActionOutcome FromValue(object? value = null) =>
        new(Success: true, Value: value, Error: null);

    public static BrowserScriptingActionOutcome FromError(string code, string message) =>
        new(Success: false, Value: null, Error: new V2Error(code, message));
}

public sealed record BrowserScriptingActionResult(
    bool Success,
    object? Value,
    V2Error? Error,
    BrowserScriptingDiagnostics Diagnostics);

public enum BrowserScriptingStateKind
{
    CookiesGet,
    CookiesSet,
    CookiesClear,
    StorageGet,
    StorageSet,
    StorageClear,
}

public enum BrowserScriptingStorageArea
{
    Local,
    Session,
}

public sealed record BrowserScriptingCookie(
    string Name,
    string Value,
    string? Domain = null,
    string Path = "/",
    bool HttpOnly = false,
    bool Secure = false,
    string? SameSite = null,
    DateTimeOffset? ExpiresUtc = null);

public sealed record BrowserScriptingStateRequest(
    string SurfaceId,
    BrowserScriptingStateKind Kind,
    string? Name = null,
    BrowserScriptingCookie? Cookie = null,
    BrowserScriptingStorageArea Area = BrowserScriptingStorageArea.Local,
    string? Key = null,
    string? Value = null)
{
    public static BrowserScriptingStateRequest CookiesGet(string surfaceId, string? name = null) =>
        new(surfaceId, BrowserScriptingStateKind.CookiesGet, Name: name);

    public static BrowserScriptingStateRequest CookiesSet(string surfaceId, BrowserScriptingCookie cookie) =>
        new(surfaceId, BrowserScriptingStateKind.CookiesSet, Cookie: cookie);

    public static BrowserScriptingStateRequest CookiesClear(string surfaceId, string? name = null) =>
        new(surfaceId, BrowserScriptingStateKind.CookiesClear, Name: name);

    public static BrowserScriptingStateRequest StorageGet(
        string surfaceId,
        BrowserScriptingStorageArea area = BrowserScriptingStorageArea.Local,
        string? key = null) =>
        new(surfaceId, BrowserScriptingStateKind.StorageGet, Area: area, Key: key);

    public static BrowserScriptingStateRequest StorageSet(
        string surfaceId,
        BrowserScriptingStorageArea area,
        string key,
        string value) =>
        new(surfaceId, BrowserScriptingStateKind.StorageSet, Area: area, Key: key, Value: value);

    public static BrowserScriptingStateRequest StorageClear(
        string surfaceId,
        BrowserScriptingStorageArea area = BrowserScriptingStorageArea.Local,
        string? key = null) =>
        new(surfaceId, BrowserScriptingStateKind.StorageClear, Area: area, Key: key);
}

public sealed record BrowserScriptingStateOutcome(
    bool Success,
    object? Value = null,
    V2Error? Error = null)
{
    public static BrowserScriptingStateOutcome FromValue(object? value = null) =>
        new(Success: true, Value: value, Error: null);

    public static BrowserScriptingStateOutcome FromError(string code, string message) =>
        new(Success: false, Value: null, Error: new V2Error(code, message));
}

public sealed record BrowserScriptingStateResult(
    bool Success,
    object? Value,
    V2Error? Error,
    BrowserScriptingDiagnostics Diagnostics);

public enum BrowserScriptingControlKind
{
    ConsoleList,
    ConsoleClear,
    DialogAccept,
    DialogDismiss,
    DownloadWait,
    Highlight,
    AddInitScript,
    AddScript,
    AddStyle,
}

public sealed record BrowserScriptingControlRequest(
    string SurfaceId,
    BrowserScriptingControlKind Kind,
    BrowserScriptingNode? Node = null,
    string? Text = null,
    string? FileName = null,
    int? TimeoutMs = null);

public sealed record BrowserScriptingControlOutcome(
    bool Success,
    object? Value = null,
    V2Error? Error = null)
{
    public static BrowserScriptingControlOutcome FromValue(object? value = null) =>
        new(Success: true, Value: value, Error: null);

    public static BrowserScriptingControlOutcome FromError(string code, string message) =>
        new(Success: false, Value: null, Error: new V2Error(code, message));
}

public sealed record BrowserScriptingControlResult(
    bool Success,
    object? Value,
    V2Error? Error,
    BrowserScriptingDiagnostics Diagnostics);
