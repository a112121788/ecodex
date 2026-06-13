using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ECode.Core.Models;
using ECode.ViewModels;

namespace ECode.Controls;

/// <summary>通知面板控件，显示终端通知列表并支持跳转到对应面板</summary>
public partial class NotificationPanel : UserControl
{
    public NotificationPanel()
    {
        InitializeComponent();
    }

    private void MarkRead_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetNotification(sender, out var notification))
            App.NotificationService.MarkAsRead(notification.Id);
    }

    private void MarkUnread_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetNotification(sender, out var notification))
            App.NotificationService.MarkAsUnread(notification.Id);
    }

    private void CopyBody_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetNotification(sender, out var notification))
            return;

        var text = new StringBuilder(notification.Title);
        if (!string.IsNullOrWhiteSpace(notification.Subtitle))
            text.AppendLine().Append(notification.Subtitle);
        if (!string.IsNullOrWhiteSpace(notification.Body))
            text.AppendLine().Append(notification.Body);

        Clipboard.SetText(text.ToString());
    }

    private void JumpToNotification_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetNotification(sender, out var notification))
            JumpToNotification(notification);
    }

    private void NotificationsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryGetSelectedNotification(out var notification))
        {
            JumpToNotification(notification);
            e.Handled = true;
        }
    }

    private void NotificationsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (TryGetSelectedNotification(out var notification))
        {
            JumpToNotification(notification);
            e.Handled = true;
        }
    }

    private void JumpToNotification(TerminalNotification notification)
    {
        if (DataContext is MainViewModel vm)
            vm.JumpToNotification(notification);
    }

    private bool TryGetSelectedNotification(out TerminalNotification notification)
    {
        if (NotificationsList.SelectedItem is TerminalNotification selected)
        {
            notification = selected;
            return true;
        }

        notification = null!;
        return false;
    }

    private static bool TryGetNotification(object sender, out TerminalNotification notification)
    {
        if (sender is FrameworkElement { DataContext: TerminalNotification direct })
        {
            notification = direct;
            return true;
        }

        if (sender is MenuItem { Parent: ContextMenu contextMenu }
            && contextMenu.PlacementTarget is FrameworkElement { DataContext: TerminalNotification placed })
        {
            notification = placed;
            return true;
        }

        notification = null!;
        return false;
    }
}
