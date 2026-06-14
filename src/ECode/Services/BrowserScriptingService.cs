using System;
using System.Collections.Generic;
using System.Linq;
using ECode.Core.Models;
using ECode.Core.Services;
using ECode.ViewModels;
using CoreBrowserScriptingService = ECode.Core.Services.BrowserScriptingService;

namespace ECode.Services;

public sealed class BrowserScriptingService
{
    private readonly Func<IEnumerable<WorkspaceViewModel>> _workspaceProvider;
    private readonly CoreBrowserScriptingService _core;

    public BrowserScriptingService(Func<IEnumerable<WorkspaceViewModel>> workspaceProvider)
    {
        _workspaceProvider = workspaceProvider ?? throw new ArgumentNullException(nameof(workspaceProvider));
        _core = new CoreBrowserScriptingService(GetSurfaceDescriptors);
    }

    public BrowserScriptingResolveResult ResolveSurfaceRef(string? surfaceRef)
    {
        return _core.ResolveSurfaceRef(surfaceRef);
    }

    public BrowserScriptingDiagnostics GetDiagnostics()
    {
        return _core.GetDiagnostics();
    }

    public string TrackSurface(WorkspaceViewModel workspace, SurfaceViewModel surface)
    {
        return _core.TrackSurface(ToDescriptor(workspace, surface));
    }

    private IEnumerable<BrowserScriptingSurfaceDescriptor> GetSurfaceDescriptors()
    {
        return _workspaceProvider()
            .SelectMany(workspace => workspace.Surfaces.Select(surface => ToDescriptor(workspace, surface)));
    }

    private static BrowserScriptingSurfaceDescriptor ToDescriptor(WorkspaceViewModel workspace, SurfaceViewModel surface)
    {
        return new BrowserScriptingSurfaceDescriptor(
            WorkspaceId: workspace.Workspace.Id,
            WorkspaceName: workspace.Name,
            SurfaceId: surface.Surface.Id,
            SurfaceName: surface.Name,
            Kind: surface.Surface.Kind,
            Url: surface.Surface.BrowserUrl,
            Title: surface.Surface.BrowserTitle);
    }
}
