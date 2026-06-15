using Microsoft.Toolkit.Uwp.Notifications;
using ECodeX.Core.Models;

namespace ECodeX.Services;

/// <summary>
/// 当 AI 编码代理需要关注时发送 Windows Toast 通知。
/// 通过 Microsoft.Toolkit.Uwp.Notifications 使用 Windows 10/11 通知系统。
/// </summary>
public static class ToastNotificationHelper
{
    /// <summary>
    /// 为终端通知显示 Windows Toast 通知。
    /// </summary>
    public static void ShowToast(TerminalNotification notification, string workspaceName)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(notification.Title)
                .AddText(notification.Body)
                .AddAttributionText($"项目：{workspaceName}")
                .AddArgument("action", "jumpToNotification")
                .AddArgument("notificationId", notification.Id)
                .AddArgument("workspaceId", notification.WorkspaceId)
                .AddArgument("surfaceId", notification.SurfaceId)
                .Show();
        }
        catch
        {
            // Toast 通知在某些环境下可能失败
            // （无 UWP 支持、沙盒等）。不关键。
        }
    }

    /// <summary>
    /// 从通知中心清除所有 ecodex Toast 通知。
    /// </summary>
    public static void ClearAll()
    {
        try
        {
            ToastNotificationManagerCompat.History.Clear();
        }
        catch
        {
            // 尽力而为
        }
    }
}
