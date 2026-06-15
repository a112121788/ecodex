using ECodeX.Controls;
using ECodeX.Core.IPC.V2;
using ECodeX.Core.Services;

namespace ECodeX.Services;

public static class BrowserScriptingRuntime
{
    private sealed record Registration(string SurfaceId, string PaneId, BrowserControl Control);

    private static readonly Dictionary<string, Registration> Registrations = new(StringComparer.Ordinal);

    public static void Register(string surfaceId, string paneId, BrowserControl control)
    {
        if (string.IsNullOrWhiteSpace(surfaceId) || string.IsNullOrWhiteSpace(paneId))
            return;

        Registrations[Key(surfaceId, paneId)] = new Registration(surfaceId, paneId, control);
    }

    public static void UnregisterSurface(string surfaceId)
    {
        foreach (var key in Registrations
            .Where(item => string.Equals(item.Value.SurfaceId, surfaceId, StringComparison.Ordinal))
            .Select(item => item.Key)
            .ToList())
        {
            Registrations.Remove(key);
        }
    }

    public static async Task<BrowserScriptingSnapshot?> GetSnapshotAsync(string surfaceId)
    {
        var registration = ResolveRegistration(surfaceId);
        return registration == null
            ? null
            : await registration.Control.GetScriptingSnapshotAsync();
    }

    public static async Task<BrowserScriptingActionOutcome> ExecuteActionAsync(BrowserScriptingActionRequest request)
    {
        var registration = ResolveRegistration(request.SurfaceId);
        if (registration == null)
        {
            return BrowserScriptingActionOutcome.FromError(
                V2ErrorCodes.NotFound,
                $"Browser control not available: {request.SurfaceId}");
        }

        return await registration.Control.ExecuteScriptingActionAsync(request);
    }

    private static Registration? ResolveRegistration(string surfaceId)
    {
        return Registrations.Values.FirstOrDefault(item =>
            string.Equals(item.SurfaceId, surfaceId, StringComparison.Ordinal));
    }

    private static string Key(string surfaceId, string paneId) => $"{surfaceId}:{paneId}";
}
