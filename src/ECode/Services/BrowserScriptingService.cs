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

    public BrowserScriptingService(
        Func<IEnumerable<WorkspaceViewModel>> workspaceProvider,
        Func<string, BrowserScriptingSnapshot?>? snapshotProvider = null,
        Func<BrowserScriptingActionRequest, BrowserScriptingActionOutcome>? actionExecutor = null,
        Func<BrowserScriptingStateRequest, BrowserScriptingStateOutcome>? stateExecutor = null,
        Func<BrowserScriptingControlRequest, BrowserScriptingControlOutcome>? controlExecutor = null)
    {
        _workspaceProvider = workspaceProvider ?? throw new ArgumentNullException(nameof(workspaceProvider));
        _core = new CoreBrowserScriptingService(GetSurfaceDescriptors, snapshotProvider, actionExecutor, stateExecutor, controlExecutor);
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

    public BrowserScriptingSnapshotResult GetSnapshot(string? surfaceRef)
    {
        return _core.GetSnapshot(surfaceRef);
    }

    public BrowserScriptingLocatorResult FindByRole(string? surfaceRef, string role, string? name = null)
    {
        return _core.FindByRole(surfaceRef, role, name);
    }

    public BrowserScriptingLocatorResult FindByText(string? surfaceRef, string text)
    {
        return _core.FindByText(surfaceRef, text);
    }

    public BrowserScriptingLocatorResult FindByTestId(string? surfaceRef, string testId)
    {
        return _core.FindByTestId(surfaceRef, testId);
    }

    public BrowserScriptingLocatorResult FindFirst(string? surfaceRef, BrowserScriptingLocator locator)
    {
        return _core.FindFirst(surfaceRef, locator);
    }

    public BrowserScriptingLocatorResult FindLast(string? surfaceRef, BrowserScriptingLocator locator)
    {
        return _core.FindLast(surfaceRef, locator);
    }

    public BrowserScriptingLocatorResult FindNth(string? surfaceRef, BrowserScriptingLocator locator, int index)
    {
        return _core.FindNth(surfaceRef, locator, index);
    }

    public BrowserScriptingActionResult Click(string? surfaceRef, BrowserScriptingLocator locator)
    {
        return _core.Click(surfaceRef, locator);
    }

    public BrowserScriptingActionResult Fill(string? surfaceRef, BrowserScriptingLocator locator, string value)
    {
        return _core.Fill(surfaceRef, locator, value);
    }

    public BrowserScriptingActionResult Hover(string? surfaceRef, BrowserScriptingLocator locator)
    {
        return _core.Hover(surfaceRef, locator);
    }

    public BrowserScriptingActionResult Press(string? surfaceRef, BrowserScriptingLocator locator, string key)
    {
        return _core.Press(surfaceRef, locator, key);
    }

    public BrowserScriptingActionResult Eval(string? surfaceRef, string script)
    {
        return _core.Eval(surfaceRef, script);
    }

    public BrowserScriptingActionResult Screenshot(string? surfaceRef)
    {
        return _core.Screenshot(surfaceRef);
    }

    public BrowserScriptingStateResult CookiesGet(string? surfaceRef, string? name = null)
    {
        return _core.CookiesGet(surfaceRef, name);
    }

    public BrowserScriptingStateResult CookiesSet(string? surfaceRef, BrowserScriptingCookie cookie)
    {
        return _core.CookiesSet(surfaceRef, cookie);
    }

    public BrowserScriptingStateResult CookiesClear(string? surfaceRef, string? name = null)
    {
        return _core.CookiesClear(surfaceRef, name);
    }

    public BrowserScriptingStateResult StorageGet(
        string? surfaceRef,
        string? key = null,
        BrowserScriptingStorageArea area = BrowserScriptingStorageArea.Local)
    {
        return _core.StorageGet(surfaceRef, key, area);
    }

    public BrowserScriptingStateResult StorageSet(
        string? surfaceRef,
        string key,
        string value,
        BrowserScriptingStorageArea area = BrowserScriptingStorageArea.Local)
    {
        return _core.StorageSet(surfaceRef, key, value, area);
    }

    public BrowserScriptingStateResult StorageClear(
        string? surfaceRef,
        string? key = null,
        BrowserScriptingStorageArea area = BrowserScriptingStorageArea.Local)
    {
        return _core.StorageClear(surfaceRef, key, area);
    }

    public BrowserScriptingControlResult ConsoleList(string? surfaceRef)
    {
        return _core.ConsoleList(surfaceRef);
    }

    public BrowserScriptingControlResult ConsoleClear(string? surfaceRef)
    {
        return _core.ConsoleClear(surfaceRef);
    }

    public BrowserScriptingControlResult DialogAccept(string? surfaceRef, string? promptText = null)
    {
        return _core.DialogAccept(surfaceRef, promptText);
    }

    public BrowserScriptingControlResult DialogDismiss(string? surfaceRef)
    {
        return _core.DialogDismiss(surfaceRef);
    }

    public BrowserScriptingControlResult DownloadWait(string? surfaceRef, string? fileName = null, int? timeoutMs = null)
    {
        return _core.DownloadWait(surfaceRef, fileName, timeoutMs);
    }

    public BrowserScriptingControlResult Highlight(string? surfaceRef, BrowserScriptingLocator locator)
    {
        return _core.Highlight(surfaceRef, locator);
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
