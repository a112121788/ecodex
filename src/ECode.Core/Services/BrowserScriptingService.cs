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
    private readonly Dictionary<string, BrowserScriptingRef> _surfaceRefs = new(StringComparer.Ordinal);

    public BrowserScriptingService(Func<IEnumerable<BrowserScriptingSurfaceDescriptor>> surfaceProvider)
    {
        _surfaceProvider = surfaceProvider ?? throw new ArgumentNullException(nameof(surfaceProvider));
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
