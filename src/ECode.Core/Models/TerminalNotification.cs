using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ECode.Core.Models;

/// <summary>
/// 通知来源 - OSC 序列或 CLI 触发
/// </summary>
public enum NotificationSource
{
    Osc9,    // OSC 9 序列（简单通知）
    Osc99,   // OSC 99 序列（键值对通知）
    Osc777,  // OSC 777 序列（结构化通知）
    Cli,     // CLI 命令触发
}

/// <summary>
/// 终端通知 - 由终端 OSC 序列或 CLI 触发的通知消息，支持已读/未读状态
/// </summary>
public record TerminalNotification : INotifyPropertyChanged
{
    private bool _isRead;

    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required string WorkspaceId { get; init; }
    public required string SurfaceId { get; init; }
    public string? PaneId { get; init; }
    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (_isRead == value)
                return;

            _isRead = value;
            OnPropertyChanged();
        }
    }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string Body { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public NotificationSource Source { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public record AppNotification
{
    public required string WorkspaceName { get; init; }
    public required string SurfaceName { get; init; }
    public required TerminalNotification Notification { get; init; }
}
