using System.Collections.ObjectModel;
using ECode.Core.Models;

namespace ECode.Core.Services;

/// <summary>
/// 管理终端通知。跟踪未读状态，提供跳转到未读消息功能，并触发 Windows 吐司通知。
/// </summary>
public class NotificationService
{
    private readonly ObservableCollection<TerminalNotification> _notifications = [];
    private readonly object _lock = new();

    public ObservableCollection<TerminalNotification> Notifications => _notifications;
    public int UnreadCount => _notifications.Count(n => !n.IsRead);

    public event Action<TerminalNotification>? NotificationAdded;
    public event Action? UnreadCountChanged;

    /// <summary>
    /// 添加新通知。
    /// </summary>
    public void AddNotification(
        string workspaceId,
        string surfaceId,
        string? paneId,
        string title,
        string? subtitle,
        string body,
        NotificationSource source)
    {
        var notification = new TerminalNotification
        {
            WorkspaceId = workspaceId,
            SurfaceId = surfaceId,
            PaneId = paneId,
            Title = title,
            Subtitle = subtitle,
            Body = body,
            Source = source,
            IsRead = false,
        };

        lock (_lock)
        {
            _notifications.Insert(0, notification);

            // 最多保留 500 条通知
            while (_notifications.Count > 500)
                _notifications.RemoveAt(_notifications.Count - 1);
        }

        NotificationAdded?.Invoke(notification);
        UnreadCountChanged?.Invoke();
    }

    /// <summary>
    /// 将通知标记为已读。
    /// </summary>
    public void MarkAsRead(string notificationId)
    {
        lock (_lock)
        {
            var notification = _notifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification == null || notification.IsRead)
                return;

            notification.IsRead = true;
            SortNotifications();
        }

        UnreadCountChanged?.Invoke();
    }

    /// <summary>
    /// 将通知标记为未读。
    /// </summary>
    public void MarkAsUnread(string notificationId)
    {
        lock (_lock)
        {
            var notification = _notifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification == null || !notification.IsRead)
                return;

            notification.IsRead = false;
            SortNotifications();
        }

        UnreadCountChanged?.Invoke();
    }

    /// <summary>
    /// 将指定项目的所有通知标记为已读。
    /// </summary>
    public void MarkWorkspaceAsRead(string workspaceId)
    {
        lock (_lock)
        {
            foreach (var n in _notifications.Where(n => n.WorkspaceId == workspaceId && !n.IsRead))
                n.IsRead = true;

            SortNotifications();
        }
        UnreadCountChanged?.Invoke();
    }

    /// <summary>
    /// 将所有通知标记为已读。
    /// </summary>
    public void MarkAllAsRead()
    {
        lock (_lock)
        {
            foreach (var n in _notifications.Where(n => !n.IsRead))
                n.IsRead = true;

            SortNotifications();
        }
        UnreadCountChanged?.Invoke();
    }

    /// <summary>
    /// 获取最近一条未读通知。
    /// </summary>
    public TerminalNotification? GetLatestUnread()
    {
        lock (_lock)
        {
            return _notifications
                .Where(n => !n.IsRead)
                .OrderByDescending(n => n.Timestamp)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// 获取指定项目的未读数量。
    /// </summary>
    public int GetUnreadCount(string workspaceId)
    {
        lock (_lock)
        {
            return _notifications.Count(n => n.WorkspaceId == workspaceId && !n.IsRead);
        }
    }

    /// <summary>
    /// 获取指定标签页的未读数量。
    /// </summary>
    public int GetUnreadCount(string workspaceId, string surfaceId)
    {
        lock (_lock)
        {
            return _notifications.Count(n => n.WorkspaceId == workspaceId
                                             && n.SurfaceId == surfaceId
                                             && !n.IsRead);
        }
    }

    /// <summary>
    /// 获取指定面板的未读数量。
    /// </summary>
    public int GetUnreadCount(string workspaceId, string surfaceId, string paneId)
    {
        lock (_lock)
        {
            return _notifications.Count(n => n.WorkspaceId == workspaceId
                                             && n.SurfaceId == surfaceId
                                             && n.PaneId == paneId
                                             && !n.IsRead);
        }
    }

    /// <summary>
    /// 获取指定项目的最新通知文本（用于侧边栏显示）。
    /// </summary>
    public string? GetLatestText(string workspaceId)
    {
        lock (_lock)
        {
            var latest = _notifications
                .Where(n => n.WorkspaceId == workspaceId)
                .OrderByDescending(n => n.Timestamp)
                .FirstOrDefault();
            return latest?.Body;
        }
    }

    /// <summary>
    /// 清除所有通知。
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _notifications.Clear();
        }
        UnreadCountChanged?.Invoke();
    }

    private void SortNotifications()
    {
        var sorted = _notifications
            .Select((notification, index) => new { notification, index })
            .OrderBy(x => x.notification.IsRead)
            .ThenByDescending(x => x.notification.Timestamp)
            .ThenBy(x => x.index)
            .Select(x => x.notification)
            .ToList();

        for (var targetIndex = 0; targetIndex < sorted.Count; targetIndex++)
        {
            var notification = sorted[targetIndex];
            var currentIndex = _notifications.IndexOf(notification);
            if (currentIndex >= 0 && currentIndex != targetIndex)
                _notifications.Move(currentIndex, targetIndex);
        }
    }
}
