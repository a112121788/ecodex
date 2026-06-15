using System.Text.Json;
using ECodeX.Core.IPC.V2;
using ECodeX.Core.Models;
using ECodeX.Core.Services;

namespace ECodeX.Services;

public sealed class NotificationApiService
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        "notification.list",
        "notification.read",
        "notification.unread",
        "notification.jump-latest",
        "notification.clear",
    };

    private readonly NotificationService _notificationService;
    private readonly Func<TerminalNotification?, bool> _jumpToNotification;

    public NotificationApiService(
        NotificationService notificationService,
        Func<TerminalNotification?, bool>? jumpToNotification = null)
    {
        _notificationService = notificationService;
        _jumpToNotification = jumpToNotification ?? (_ => false);
    }

    public static bool CanHandle(string? method)
    {
        return method != null && SupportedMethods.Contains(method);
    }

    public V2Response HandleRequest(V2Request request)
    {
        return request.Method switch
        {
            "notification.list" => V2Response.FromResult(request.Id, CreateListResult(request.Params)),
            "notification.read" => HandleRead(request),
            "notification.unread" => HandleUnread(request),
            "notification.jump-latest" => HandleJumpLatest(request),
            "notification.clear" => HandleClear(request),
            _ => V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotSupported,
                $"ecodex.v2 notification method is not supported: {request.Method}"),
        };
    }

    private object CreateListResult(JsonElement? parameters)
    {
        var unreadOnly = GetBoolParam(parameters, "unreadOnly", "unread") ?? false;
        var includeRead = GetBoolParam(parameters, "includeRead") ?? !unreadOnly;
        var limit = Math.Clamp(GetIntParam(parameters, "limit") ?? 100, 1, 500);
        var notifications = FilterNotifications(parameters)
            .Where(notification => includeRead || !notification.IsRead)
            .Take(limit)
            .ToList();

        return new
        {
            notifications = notifications.Select(CreateNotificationResult).ToList(),
            total = notifications.Count,
            unread = _notificationService.UnreadCount,
        };
    }

    private V2Response HandleRead(V2Request request)
    {
        if (IsTruthy(request.Params, "all"))
        {
            var before = _notificationService.UnreadCount;
            _notificationService.MarkAllAsRead();
            return V2Response.FromResult(request.Id, new
            {
                marked = before,
                unread = _notificationService.UnreadCount,
            });
        }

        if (!TryResolveNotification(request.Params, out var notification, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        _notificationService.MarkAsRead(notification!.Id);
        return V2Response.FromResult(request.Id, new
        {
            read = true,
            notification = CreateNotificationResult(notification with { IsRead = true }),
            unread = _notificationService.UnreadCount,
        });
    }

    private V2Response HandleUnread(V2Request request)
    {
        if (!TryResolveNotification(request.Params, out var notification, out var error))
            return V2Response.FromStableError(request.Id, error!.Code, error.Message);

        _notificationService.MarkAsUnread(notification!.Id);
        return V2Response.FromResult(request.Id, new
        {
            unread = true,
            notification = CreateNotificationResult(notification with { IsRead = false }),
            unreadTotal = _notificationService.UnreadCount,
        });
    }

    private V2Response HandleJumpLatest(V2Request request)
    {
        var latest = _notificationService.GetLatestUnread();
        if (latest == null)
        {
            return V2Response.FromResult(request.Id, new
            {
                jumped = false,
                notification = (object?)null,
                unread = _notificationService.UnreadCount,
            });
        }

        var jumped = _jumpToNotification(latest);
        return V2Response.FromResult(request.Id, new
        {
            jumped,
            notification = CreateNotificationResult(latest with { IsRead = jumped || latest.IsRead }),
            unread = _notificationService.UnreadCount,
        });
    }

    private V2Response HandleClear(V2Request request)
    {
        var before = _notificationService.Notifications.Count;
        _notificationService.Clear();
        return V2Response.FromResult(request.Id, new
        {
            cleared = before,
            unread = _notificationService.UnreadCount,
        });
    }

    private bool TryResolveNotification(
        JsonElement? parameters,
        out TerminalNotification? notification,
        out V2Error? error)
    {
        notification = null;
        error = null;
        var target = GetStringParam(parameters, "target", "notification", "notificationId", "id");
        if (string.IsNullOrWhiteSpace(target))
        {
            error = new V2Error(V2ErrorCodes.InvalidRef, "Missing required notification target.");
            return false;
        }

        notification = _notificationService.Notifications.FirstOrDefault(item =>
            string.Equals(item.Id, target, StringComparison.Ordinal));
        if (notification == null)
        {
            error = new V2Error(V2ErrorCodes.NotFound, $"Notification not found: {target}");
            return false;
        }

        return true;
    }

    private IEnumerable<TerminalNotification> FilterNotifications(JsonElement? parameters)
    {
        var workspaceId = GetStringParam(parameters, "workspaceId", "workspace");
        var surfaceId = GetStringParam(parameters, "surfaceId", "surface");
        var paneId = GetStringParam(parameters, "paneId", "pane");
        return _notificationService.Notifications.Where(notification =>
            (string.IsNullOrWhiteSpace(workspaceId) || string.Equals(notification.WorkspaceId, workspaceId, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(surfaceId) || string.Equals(notification.SurfaceId, surfaceId, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(paneId) || string.Equals(notification.PaneId, paneId, StringComparison.Ordinal)));
    }

    private static Dictionary<string, object?> CreateNotificationResult(TerminalNotification notification)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = notification.Id,
            ["workspaceId"] = notification.WorkspaceId,
            ["surfaceId"] = notification.SurfaceId,
            ["paneId"] = notification.PaneId,
            ["isRead"] = notification.IsRead,
            ["title"] = notification.Title,
            ["subtitle"] = notification.Subtitle,
            ["body"] = notification.Body,
            ["timestamp"] = notification.Timestamp,
            ["source"] = notification.Source.ToString(),
        };
    }

    private static bool IsTruthy(JsonElement? parameters, string name)
    {
        return GetBoolParam(parameters, name) == true;
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
}
