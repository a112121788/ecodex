using System.Windows;
using System;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ECode.Controls;
using ECode.Core.Services;
using ECode.ViewModels;
using ECode.Services;
using EcodeActionTargets = ECode.Core.Models.EcodeActionTargets;
using EcodeJsonDiagnosticSeverity = ECode.Core.Models.EcodeJsonDiagnosticSeverity;

namespace ECode.Views;

/// <summary>应用主窗口，管理项目侧边栏、Surface 标签栏和终端面板容器</summary>
public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private readonly DispatcherTimer _uiRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _terminalFocusTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private ICollectionView? _workspaceView;

    public MainWindow()
    {
        InitializeComponent();
        WindowAppearance.Apply(this);
        SetupWorkspaceFilter();

        CommandPaletteControl.PaletteClosed += () => FocusTerminal();
        CommandPaletteControl.ItemExecuted += item => FocusTerminal();


        // 连接代码片段选择器的事件
        SnippetPickerControl.SnippetSelected += OnSnippetSelected;
        SnippetPickerControl.Closed += () => SnippetPickerControl.Visibility = Visibility.Collapsed;

        // 连接来自标签栏的内联搜索事件
        SurfaceTabBarControl.SearchTextChanged += OnSearchTextChanged;
        SurfaceTabBarControl.NextMatchRequested += OnSearchNext;
        SurfaceTabBarControl.PreviousMatchRequested += OnSearchPrevious;
        SurfaceTabBarControl.SurfaceOrderChanged += PersistCurrentSession;

        // 连接终端 Surface 的事件
        SplitPaneContainerControl.SearchRequested += () =>
        {
            SurfaceTabBarControl.FocusSearch();
        };

        // 定期刷新轻量级 UI 状态（面板数量、缩放图标）
        _uiRefreshTimer.Tick += (_, _) => RefreshSurfaceUiState();
        _uiRefreshTimer.Start();
        _terminalFocusTimer.Tick += (_, _) =>
        {
            _terminalFocusTimer.Stop();
            FocusTerminal();
        };
        WorkspaceList.SelectionChanged += (_, _) => QueueFocusTerminal();

        // 订阅设置变更事件
        ECode.Core.Config.SettingsService.SettingsChanged += OnSettingsChanged;
        OnSettingsChanged();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.WorkspaceOrderChanged += PersistCurrentSession;
    }

    private void OnSettingsChanged()
    {
        var settings = ECode.Core.Config.SettingsService.Current;
        var theme = ECode.Core.Config.TerminalThemes.GetEffective(settings);

        Opacity = Math.Clamp(settings.Opacity, 0.5, 1.0);

        // 更新所有可见的终端控件
        foreach (var workspace in ViewModel.Workspaces)
        {
            foreach (var surface in workspace.Surfaces)
            {
                // 找到该 Surface 对应的 SplitPaneContainer 并更新其中的终端
                var container = FindVisualChild<SplitPaneContainer>(ContentArea, null);
                if (container != null)
                {
                    container.UpdateAllTerminals(theme, settings.FontFamily, settings.FontSize);
                }
            }
        }

        RefreshSurfaceUiState();
    }

    private void SetupWorkspaceFilter()
    {
        _workspaceView = CollectionViewSource.GetDefaultView(ViewModel.Workspaces);
        if (_workspaceView != null)
        {
            _workspaceView.Filter = WorkspaceFilterPredicate;
            WorkspaceList.ItemsSource = _workspaceView;
        }
    }

    private bool WorkspaceFilterPredicate(object obj)
    {
        if (obj is not WorkspaceViewModel ws)
            return false;

        var query = WorkspaceFilterBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return (ws.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (ws.WorkingDirectory?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (ws.GitBranch?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void WorkspaceFilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _workspaceView?.Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 如果有可用的会话状态，则恢复窗口位置
        var session = ECode.Core.Services.SessionPersistenceService.Load();
        if (session?.Window != null)
        {
            var w = session.Window;
            if (w.Width > 0 && w.Height > 0)
            {
                Left = w.X;
                Top = w.Y;
                Width = w.Width;
                Height = w.Height;
                WindowState = w.IsMaximized ? WindowState.Maximized : WindowState.Normal;
            }
        }

        RefreshSurfaceUiState();
        UpdateSidebarLayout();
        UpdateDaemonStatus();
        UpdateWindowChrome();
        SizeChanged += (_, _) => UpdateWindowClip();
        StateChanged += (_, _) => UpdateWindowChrome();
        UpdateWindowClip();
        QueueFocusTerminal();

        // 监听守护进程连接状态变化
        App.DaemonClient.Connected += () => Dispatcher.BeginInvoke(UpdateDaemonStatus);
        App.DaemonClient.Disconnected += () => Dispatcher.BeginInvoke(UpdateDaemonStatus);
    }

    private void UpdateDaemonStatus()
    {
        var connected = App.DaemonClient.IsConnected;
        DaemonStatusDot.Fill = connected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99)) // 绿色
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80)); // 灰色
        DaemonStatusText.Text = connected ? "守护进程" : "本地";
        DaemonStatusBorder.ToolTip = connected
            ? "已连接到 ecode-daemon——会话将在重启后保留"
            : "本地运行——重启后会话不会保留";
    }

    private void UpdateWindowChrome()
    {
        bool maximized = WindowState == WindowState.Maximized;
        // 最大化时，使用 0 圆角且无边框
        WindowBorder.CornerRadius = maximized ? new CornerRadius(0) : (CornerRadius)FindResource("WindowCornerRadius");
        WindowBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        // 更新最大化/还原图标
        MaxRestoreIcon.Text = maximized ? "\uE923" : "\uE922";
        MaxRestoreButton.ToolTip = maximized ? "还原" : "最大化";
        UpdateWindowClip();
    }

    private void UpdateWindowClip()
    {
        double radius = WindowState == WindowState.Maximized ? 0 : 10;
        WindowClipGeometry.RadiusX = radius;
        WindowClipGeometry.RadiusY = radius;
        WindowClipGeometry.Rect = new System.Windows.Rect(0, 0, WindowBorder.ActualWidth, WindowBorder.ActualHeight);
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _uiRefreshTimer.Stop();
        _terminalFocusTimer.Stop();
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.WorkspaceOrderChanged -= PersistCurrentSession;
        SurfaceTabBarControl.SurfaceOrderChanged -= PersistCurrentSession;
        PersistCurrentSession();
    }

    private void PersistCurrentSession()
    {
        ViewModel.SaveSession(Left, Top, Width, Height, WindowState == WindowState.Maximized);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SidebarVisible) ||
            e.PropertyName == nameof(MainViewModel.SidebarWidth))
        {
            UpdateSidebarLayout();
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.SelectedWorkspace))
        {
            RefreshSurfaceUiState();
            QueueFocusTerminal();
            return;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        // === 始终可用的应用级快捷键（即使在终端聚焦时也生效） ===

        // Ctrl+Tab / Ctrl+Shift+Tab：循环切换 Surface
        if (ctrl && e.Key == Key.Tab)
        {
            if (shift)
                ViewModel.SelectedWorkspace?.PreviousSurface();
            else
                ViewModel.SelectedWorkspace?.NextSurface();
            e.Handled = true;
            return;
        }

        // Ctrl+Alt：面板聚焦 + 历史选择器
        if (ctrl && alt && !shift)
        {
            switch (e.Key)
            {
                case Key.Right:
                case Key.Down:
                    ViewModel.SelectedWorkspace?.SelectedSurface?.FocusNextPane();
                    e.Handled = true;
                    return;
                case Key.Left:
                case Key.Up:
                    ViewModel.SelectedWorkspace?.SelectedSurface?.FocusPreviousPane();
                    e.Handled = true;
                    return;
                case Key.H: // 打开命令历史选择器（Ctrl+Alt+H）
                    OpenCommandHistoryPicker();
                    e.Handled = true;
                    return;
            }
        }

        // Ctrl+Shift：应用级快捷键（分屏、缩放、搜索、Surface 等）
        if (ctrl && shift && !alt)
        {
            switch (e.Key)
            {
                case Key.W: // Close workspace
                    ViewModel.CloseWorkspace(ViewModel.SelectedWorkspace);
                    e.Handled = true;
                    return;
                case Key.R: // Rename workspace
                    ViewModel.SelectedWorkspace?.Rename();
                    e.Handled = true;
                    return;
                case Key.D: // Split down
                    ViewModel.SelectedWorkspace?.SelectedSurface?.SplitDown();
                    e.Handled = true;
                    return;
                case Key.U: // Jump to latest unread
                    ViewModel.JumpToLatestUnread();
                    e.Handled = true;
                    return;
                case Key.OemCloseBrackets: // Next surface (Ctrl+Shift+])
                    ViewModel.SelectedWorkspace?.NextSurface();
                    e.Handled = true;
                    return;
                case Key.OemOpenBrackets: // Previous surface (Ctrl+Shift+[)
                    ViewModel.SelectedWorkspace?.PreviousSurface();
                    e.Handled = true;
                    return;
                case Key.Z: // Zoom toggle (Ctrl+Shift+Z)
                    ViewModel.SelectedWorkspace?.SelectedSurface?.ToggleZoom();
                    e.Handled = true;
                    return;
                case Key.F: // Search (Ctrl+Shift+F)
                    ToggleSearch();
                    e.Handled = true;
                    return;
                case Key.P: // Command palette (Ctrl+Shift+P)
                    ToggleCommandPalette();
                    e.Handled = true;
                    return;
                case Key.L: // Logs (Ctrl+Shift+L)
                    OpenLogsWindow();
                    e.Handled = true;
                    return;
                case Key.V: // Session Vault (Ctrl+Shift+V)
                    OpenSessionVault();
                    e.Handled = true;
                    return;
                case Key.H: // History: insert last command (Ctrl+Shift+H)
                    InsertLastCommandFromHistory();
                    e.Handled = true;
                    return;
            }
        }

        // === 仅 Ctrl 修饰的快捷键（终端聚焦时跳过，让终端自行处理） ===
        if (ctrl && !alt && IsTerminalFocusActive())
            return;

        // 项目
        if (ctrl && !shift && !alt)
        {
            switch (e.Key)
            {
                case Key.N: // 新建项目
                    ViewModel.CreateNewWorkspace();
                    e.Handled = true;
                    return;
                case Key.B: // 切换侧边栏
                    ViewModel.ToggleSidebar();
                    e.Handled = true;
                    return;
                case Key.I: // 通知面板
                    ViewModel.ToggleNotificationPanel();
                    e.Handled = true;
                    return;
                case Key.T: // 新建 Surface
                    ViewModel.SelectedWorkspace?.CreateNewSurface();
                    e.Handled = true;
                    return;
                case Key.W: // 关闭 Surface
                    var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
                    if (surface != null)
                        ViewModel.SelectedWorkspace?.CloseSurface(surface);
                    e.Handled = true;
                    return;
                case Key.D: // 向右分屏
                    ViewModel.SelectedWorkspace?.SelectedSurface?.SplitRight();
                    e.Handled = true;
                    return;
                // 项目 1-8
                case Key.D1: ViewModel.SelectWorkspace(0); e.Handled = true; return;
                case Key.D2: ViewModel.SelectWorkspace(1); e.Handled = true; return;
                case Key.D3: ViewModel.SelectWorkspace(2); e.Handled = true; return;
                case Key.D4: ViewModel.SelectWorkspace(3); e.Handled = true; return;
                case Key.D5: ViewModel.SelectWorkspace(4); e.Handled = true; return;
                case Key.D6: ViewModel.SelectWorkspace(5); e.Handled = true; return;
                case Key.D7: ViewModel.SelectWorkspace(6); e.Handled = true; return;
                case Key.D8: ViewModel.SelectWorkspace(7); e.Handled = true; return;
                case Key.D9: // 最后一个项目
                    if (ViewModel.Workspaces.Count > 0)
                        ViewModel.SelectWorkspace(ViewModel.Workspaces.Count - 1);
                    e.Handled = true;
                    return;
                case Key.OemComma: // 设置（Ctrl+,）
                    OpenSettings();
                    e.Handled = true;
                    return;
            }
        }
    }

    // 标题栏事件处理
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (WindowState != WindowState.Normal)
            return;

        if (sender is not Thumb thumb || thumb.Tag is not string edge)
            return;

        const double minW = 600;
        const double minH = 400;

        double left = Left;
        double top = Top;
        double width = Width;
        double height = Height;

        void ResizeLeft(double dx)
        {
            var newWidth = Math.Max(minW, width - dx);
            var delta = width - newWidth;
            width = newWidth;
            left += delta;
        }

        void ResizeRight(double dx)
        {
            width = Math.Max(minW, width + dx);
        }

        void ResizeTop(double dy)
        {
            var newHeight = Math.Max(minH, height - dy);
            var delta = height - newHeight;
            height = newHeight;
            top += delta;
        }

        void ResizeBottom(double dy)
        {
            height = Math.Max(minH, height + dy);
        }

        switch (edge)
        {
            case "Left": ResizeLeft(e.HorizontalChange); break;
            case "Right": ResizeRight(e.HorizontalChange); break;
            case "Top": ResizeTop(e.VerticalChange); break;
            case "Bottom": ResizeBottom(e.VerticalChange); break;
            case "TopLeft":
                ResizeLeft(e.HorizontalChange);
                ResizeTop(e.VerticalChange);
                break;
            case "TopRight":
                ResizeRight(e.HorizontalChange);
                ResizeTop(e.VerticalChange);
                break;
            case "BottomLeft":
                ResizeLeft(e.HorizontalChange);
                ResizeBottom(e.VerticalChange);
                break;
            case "BottomRight":
                ResizeRight(e.HorizontalChange);
                ResizeBottom(e.VerticalChange);
                break;
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    // --- Workspace drag-and-drop reordering ---

    private Point _dragStartPoint;

    private void WorkspaceItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void WorkspaceItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var diff = _dragStartPoint - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is System.Windows.Controls.ListBoxItem item &&
            item.DataContext is ViewModels.WorkspaceViewModel workspace)
        {
            DragDrop.DoDragDrop(item, workspace, DragDropEffects.Move);
        }
    }

    private void WorkspaceItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBoxItem targetItem) return;
        if (targetItem.DataContext is not ViewModels.WorkspaceViewModel targetWorkspace) return;

        var sourceWorkspace = e.Data.GetData(typeof(ViewModels.WorkspaceViewModel)) as ViewModels.WorkspaceViewModel;
        if (sourceWorkspace == null || sourceWorkspace == targetWorkspace) return;

        int sourceIndex = ViewModel.Workspaces.IndexOf(sourceWorkspace);
        int targetIndex = ViewModel.Workspaces.IndexOf(targetWorkspace);

        if (sourceIndex >= 0 && targetIndex >= 0)
        {
            ViewModel.MoveWorkspace(sourceWorkspace, targetIndex);
        }
    }

    // 标题栏 + 菜单事件处理
    private void CommandPalette_Click(object sender, RoutedEventArgs e) => ToggleCommandPalette();
    private void Search_Click(object sender, RoutedEventArgs e) => ToggleSearch();
    private void Snippets_Click(object sender, RoutedEventArgs e) => ToggleSnippetPicker();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void MenuOpenLogs_Click(object sender, RoutedEventArgs e) => OpenLogsWindow();
    private void MenuOpenSessionVault_Click(object sender, RoutedEventArgs e) => OpenSessionVault();
    private void MenuOpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void MenuOpenKeyboardShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow("Keyboard") { Owner = this };
        settings.ShowDialog();
    }
    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "ECode\n面向现代工作流优化的终端复用器。",
            "关于 ECode",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // 工具栏事件处理
    private void ToolbarSplitRight_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedWorkspace?.SelectedSurface?.SplitRight();
    private void ToolbarSplitDown_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedWorkspace?.SelectedSurface?.SplitDown();
    private void ShellSelector_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        var shells = ShellDetector.DetectShells();
        var menu = new ContextMenu();

        foreach (var shell in shells)
        {
            var item = new MenuItem { Header = shell.Name, Tag = shell.Path };
            item.Click += (s, _) =>
            {
                if (s is MenuItem mi && mi.Tag is string path)
                    ViewModel.SelectedWorkspace?.SelectedSurface?.OpenPaneWithShell(path);
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
    private void ToolbarLayout2Col_Click(object sender, RoutedEventArgs e) => ApplyLayout(2, 1);
    private void ToolbarLayoutGrid_Click(object sender, RoutedEventArgs e) => ApplyLayout(2, 2);
    private void ToolbarLayoutMainStack_Click(object sender, RoutedEventArgs e) => ApplyMainStackLayout();
    private void ToolbarEqualize_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedWorkspace?.SelectedSurface?.EqualizePanes();
    private void ToolbarZoom_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedWorkspace?.SelectedSurface?.ToggleZoom();
        RefreshSurfaceUiState();
    }


    private void ToggleSearch()
    {
        SurfaceTabBarControl.FocusSearch();
    }

    private void ToggleSnippetPicker()
    {
        if (SnippetPickerControl.Visibility == Visibility.Visible)
        {
            SnippetPickerControl.Visibility = Visibility.Collapsed;
        }
        else
        {
            SnippetPickerControl.RefreshList();
            SnippetPickerControl.Visibility = Visibility.Visible;
            SnippetPickerControl.FocusSearch();
        }
    }

    private void OnSnippetSelected(ECode.Core.Models.Snippet snippet)
    {
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        if (surface?.FocusedPaneId is string paneId)
        {
            var session = surface.GetSession(paneId);
            var content = snippet.Resolve();
            session?.Write(content);
            App.SnippetService.IncrementUseCount(snippet.Id);
        }
        SnippetPickerControl.Visibility = Visibility.Collapsed;
    }

    private void ToggleCommandPalette()
    {
        if (CommandPaletteControl.Visibility == Visibility.Visible)
        {
            CommandPaletteControl.Hide();
        }
        else
        {
            var items = BuildPaletteItems();
            CommandPaletteControl.Show(items);
        }
    }

    private void UpdateSidebarLayout()
    {
        if (ViewModel.SidebarVisible)
        {
            var width = Math.Clamp(ViewModel.SidebarWidth, 200, 500);
            SidebarColumn.Width = new GridLength(width);
            SidebarColumn.MinWidth = 200;
            SidebarColumn.MaxWidth = 500;
            SidebarBorder.Visibility = Visibility.Visible;
            SidebarSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            SidebarColumn.Width = new GridLength(0);
            SidebarColumn.MinWidth = 0;
            SidebarColumn.MaxWidth = 0;
            SidebarBorder.Visibility = Visibility.Collapsed;
            SidebarSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private static bool IsTerminalFocusActive()
    {
        var current = Keyboard.FocusedElement as DependencyObject;
        while (current != null)
        {
            if (current is TerminalControl)
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private List<PaletteItem> BuildPaletteItems()
    {
        List<PaletteItem> items =
        [
            new() { Id = "new-workspace", Label = "新建项目", Icon = "\uE710", Shortcut = "Ctrl+N", Category = "项目", Execute = () => ViewModel.CreateNewWorkspace() },
            new() { Id = "new-surface", Label = "新建标签页", Icon = "\uE710", Shortcut = "Ctrl+T", Category = "标签页", Execute = () => ViewModel.SelectedWorkspace?.CreateNewSurface() },
            new() { Id = "close-surface", Label = "关闭标签页", Icon = "\uE711", Shortcut = "Ctrl+W", Category = "标签页", Execute = () => { var s = ViewModel.SelectedWorkspace?.SelectedSurface; if (s != null) ViewModel.SelectedWorkspace?.CloseSurface(s); } },
            new() { Id = "close-workspace", Label = "关闭项目", Icon = "\uE711", Shortcut = "Ctrl+Shift+W", Category = "项目", Execute = () => ViewModel.CloseWorkspace(ViewModel.SelectedWorkspace) },
            new() { Id = "split-right", Label = "向右分屏", Icon = "\uE26B", Shortcut = "Ctrl+D", Category = "面板", Execute = () => ViewModel.SelectedWorkspace?.SelectedSurface?.SplitRight() },
            new() { Id = "split-down", Label = "向下分屏", Icon = "\uE74B", Shortcut = "Ctrl+Shift+D", Category = "面板", Execute = () => ViewModel.SelectedWorkspace?.SelectedSurface?.SplitDown() },
            new() { Id = "toggle-sidebar", Label = "切换侧边栏", Icon = "\uE700", Shortcut = "Ctrl+B", Category = "视图", Execute = () => ViewModel.ToggleSidebar() },
            new() { Id = "notifications", Label = "通知", Icon = "\uEA8F", Shortcut = "Ctrl+I", Category = "视图", Execute = () => ViewModel.ToggleNotificationPanel() },
            new() { Id = "test-notification", Label = "测试通知", Icon = "\uE7F4", Category = "视图", Execute = ShowTestNotification },
            new() { Id = "open-logs", Label = "打开命令日志", Icon = "\uE7BA", Shortcut = "Ctrl+Shift+L", Category = "日志", Execute = OpenLogsWindow },
            new() { Id = "open-session-vault", Label = "打开会话存档", Icon = "\uE8D1", Shortcut = "Ctrl+Shift+V", Category = "日志", Execute = OpenSessionVault },
            new() { Id = "open-command-history", Label = "打开命令历史", Icon = "\uE81C", Shortcut = "Ctrl+Alt+H", Category = "历史", Execute = OpenCommandHistoryPicker },
            new() { Id = "insert-last-command", Label = "插入上一条命令", Icon = "\uE8A7", Shortcut = "Ctrl+Shift+H", Category = "历史", Execute = InsertLastCommandFromHistory },
            new() { Id = "search", Label = "搜索", Icon = "\uE721", Shortcut = "Ctrl+Shift+F", Category = "视图", Execute = () => ToggleSearch() },
            new() { Id = "zoom-pane", Label = "缩放面板", Icon = "\uE740", Shortcut = "Ctrl+Shift+Z", Category = "面板", Execute = () => ViewModel.SelectedWorkspace?.SelectedSurface?.ToggleZoom() },
            new() { Id = "focus-next", Label = "聚焦下一面板", Icon = "\uE76C", Shortcut = "Ctrl+Alt+Right", Category = "面板", Execute = () => ViewModel.SelectedWorkspace?.SelectedSurface?.FocusNextPane() },
            new() { Id = "focus-prev", Label = "聚焦上一面板", Icon = "\uE76B", Shortcut = "Ctrl+Alt+Left", Category = "面板", Execute = () => ViewModel.SelectedWorkspace?.SelectedSurface?.FocusPreviousPane() },
            new() { Id = "next-surface", Label = "下一标签页", Icon = "\uE76C", Shortcut = "Ctrl+Tab", Category = "标签页", Execute = () => ViewModel.SelectedWorkspace?.NextSurface() },
            new() { Id = "prev-surface", Label = "上一标签页", Icon = "\uE76B", Shortcut = "Ctrl+Shift+Tab", Category = "标签页", Execute = () => ViewModel.SelectedWorkspace?.PreviousSurface() },
            new() { Id = "settings", Label = "Settings", Icon = "\uE713", Shortcut = "Ctrl+,", Category = "应用", Execute = () => OpenSettings() },
            new() { Id = "equalize", Label = "等宽面板", Icon = "\uE9D5", Category = "面板", Execute = () => ViewModel.SelectedWorkspace?.SelectedSurface?.EqualizePanes() },
            new() { Id = "layout-2col", Label = "布局：两列", Icon = "\uE745", Category = "布局", Execute = () => ApplyLayout(2, 1) },
            new() { Id = "layout-3col", Label = "布局：三列", Icon = "\uE745", Category = "布局", Execute = () => ApplyLayout(3, 1) },
            new() { Id = "layout-grid", Label = "布局：2x2 网格", Icon = "\uF0E2", Category = "布局", Execute = () => ApplyLayout(2, 2) },
            new() { Id = "layout-main-stack", Label = "布局：主+副", Icon = "\uE745", Category = "布局", Execute = () => ApplyMainStackLayout() },
        ];

        AddEcodeJsonPaletteItems(items);
        return items;
    }

    private void AddEcodeJsonPaletteItems(List<PaletteItem> items)
    {
        var service = new EcodeJsonService();
        var result = service.Load(GetActiveWorkspaceDirectory());

        foreach (var diagnostic in result.Diagnostics)
        {
            items.Add(new PaletteItem
            {
                Id = $"ecode-json-diagnostic:{items.Count}",
                Label = diagnostic.Severity == EcodeJsonDiagnosticSeverity.Error
                    ? "ecode.json 配置错误"
                    : "ecode.json 配置警告",
                Description = diagnostic.Message,
                Icon = "\uE7BA",
                Category = "配置",
                SearchText = $"{diagnostic.Path} {diagnostic.Message}",
                Execute = () => MessageBox.Show(
                    this,
                    $"{diagnostic.Message}\n\n{diagnostic.Path}",
                    "ecode.json",
                    MessageBoxButton.OK,
                    diagnostic.Severity == EcodeJsonDiagnosticSeverity.Error
                        ? MessageBoxImage.Error
                        : MessageBoxImage.Warning),
            });
        }

        foreach (var command in result.Config.Commands.Where(c =>
                     !string.IsNullOrWhiteSpace(c.Name) &&
                     !string.IsNullOrWhiteSpace(c.Command)))
        {
            var commandText = command.Command;
            items.Add(new PaletteItem
            {
                Id = $"ecode-json-command:{command.Name}",
                Label = command.Name,
                Description = command.Description ?? commandText,
                Icon = "\uE756",
                Category = "项目命令",
                SearchText = string.Join(' ', command.Keywords),
                Execute = () => ExecuteEcodeJsonCommand(
                    command.Name,
                    commandText,
                    command.Target,
                    command.Confirm),
            });
        }

        foreach (var (id, action) in result.Config.Actions.Where(kvp =>
                     kvp.Value.Palette &&
                     string.Equals(kvp.Value.Type, "command", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(kvp.Value.Command)))
        {
            var commandText = action.Command!;
            var title = string.IsNullOrWhiteSpace(action.Title) ? id : action.Title;
            items.Add(new PaletteItem
            {
                Id = $"ecode-json-action:{id}",
                Label = title,
                Description = action.Subtitle ?? commandText,
                Icon = "\uE756",
                Category = "项目动作",
                SearchText = id,
                Execute = () => ExecuteEcodeJsonCommand(
                    title,
                    commandText,
                    action.Target,
                    action.Confirm),
            });
        }
    }

    private string? GetActiveWorkspaceDirectory()
    {
        var workspace = ViewModel.SelectedWorkspace;
        var surface = workspace?.SelectedSurface;
        var focusedPaneId = surface?.FocusedPaneId;

        if (!string.IsNullOrWhiteSpace(focusedPaneId))
        {
            var cwd = surface?.GetSession(focusedPaneId)?.WorkingDirectory;
            if (!string.IsNullOrWhiteSpace(cwd))
                return cwd;
        }

        return workspace?.WorkingDirectory;
    }

    private void ExecuteEcodeJsonCommand(string label, string command, string target, bool confirm)
    {
        if (confirm)
        {
            var result = MessageBox.Show(
                this,
                $"要执行项目命令吗？\n\n{command}",
                label,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        if (string.Equals(target, EcodeActionTargets.NewTabInCurrentPane, StringComparison.Ordinal))
            ViewModel.SelectedWorkspace?.CreateNewSurface();

        WriteCommandToFocusedTerminal(command);
    }

    private void WriteCommandToFocusedTerminal(string command)
    {
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        if (surface == null)
            return;

        var paneId = surface.FocusedPaneId
            ?? surface.RootNode.GetLeaves()
                .Select(l => l.PaneId)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        if (string.IsNullOrWhiteSpace(paneId))
            return;

        var session = surface.GetSession(paneId);
        if (session == null)
            return;

        var commandText = command.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(commandText))
            return;

        surface.RegisterCommandSubmission(paneId, commandText);
        session.Write(commandText + Environment.NewLine);
    }

    private void ApplyLayout(int cols, int rows)
    {
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        if (surface == null) return;

        NormalizeToSinglePane(surface);

        for (int c = 1; c < cols; c++)
            surface.SplitRight();

        if (rows > 1)
        {
            var columnPaneIds = surface.RootNode.GetLeaves()
                .Select(l => l.PaneId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToList();

            foreach (var paneId in columnPaneIds)
            {
                surface.FocusPane(paneId);
                for (int r = 1; r < rows; r++)
                    surface.SplitDown();
            }
        }

        surface.EqualizePanes();
    }

    private void ApplyMainStackLayout()
    {
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        if (surface == null) return;

        NormalizeToSinglePane(surface);

        // 左侧主面板，右侧上下两个面板
        surface.SplitRight();

        var rightPaneId = surface.RootNode.GetLeaves()
            .Skip(1)
            .Select(l => l.PaneId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        if (!string.IsNullOrWhiteSpace(rightPaneId))
        {
            surface.FocusPane(rightPaneId);
            surface.SplitDown();
            surface.EqualizePanes();
        }
    }

    private static void NormalizeToSinglePane(SurfaceViewModel surface)
    {
        var paneIds = surface.RootNode.GetLeaves()
            .Select(l => l.PaneId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();

        if (paneIds.Count <= 1) return;

        var focusedPaneId = surface.FocusedPaneId;
        string keepPaneId = !string.IsNullOrWhiteSpace(focusedPaneId) && paneIds.Contains(focusedPaneId)
            ? focusedPaneId
            : paneIds[0];

        surface.FocusPane(keepPaneId);

        foreach (var paneId in paneIds.Where(id => id != keepPaneId))
            surface.ClosePane(paneId);
    }

    private void FocusTerminal()
    {
        // 让焦点回到当前激活的终端面板
        if (!SplitPaneContainerControl.FocusCurrentPane())
            ContentArea.Focus();
    }

    private void QueueFocusTerminal()
    {
        Dispatcher.BeginInvoke(FocusTerminal, DispatcherPriority.ContextIdle);
        _terminalFocusTimer.Stop();
        _terminalFocusTimer.Start();
    }

    private void RefreshSurfaceUiState()
    {
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        if (surface == null)
        {
            PaneCountText.Text = "0 个面板";
            ToolbarZoomIcon.Text = "\uE740";
            ToolbarZoomButton.ToolTip = "缩放面板 (Ctrl+Shift+Z)";
            return;
        }

        var paneCount = surface.RootNode.GetLeaves().Count();
        PaneCountText.Text = surface.IsZoomed
            ? $"{paneCount} 个面板（1 个已缩放）"
            : paneCount == 1 ? "1 个面板" : $"{paneCount} 个面板";

        ToolbarZoomIcon.Text = surface.IsZoomed ? "\uE73F" : "\uE740";
        ToolbarZoomButton.ToolTip = surface.IsZoomed
            ? "取消缩放面板 (Ctrl+Shift+Z)"
            : "缩放面板 (Ctrl+Shift+Z)";
    }

    // --- Search handling ---
    private int _currentSearchMatch = 0;
    private List<(int row, int col, int length)> _searchMatches = [];

    private void OnSearchTextChanged(string query)
    {
        _searchMatches = [];
        _currentSearchMatch = 0;

        if (string.IsNullOrEmpty(query))
        {
            ClearSearchHighlights();
            SurfaceTabBarControl.UpdateMatchCount(0, 0);
            return;
        }

        // 在聚焦的终端中进行搜索
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        if (surface?.FocusedPaneId is string paneId)
        {
            var session = surface.GetSession(paneId);
            if (session != null)
            {
                _searchMatches = FindAllInBuffer(session.Buffer, query);
                _currentSearchMatch = 0;
                UpdateSearchHighlights();
            }
        }

        SurfaceTabBarControl.UpdateMatchCount(_currentSearchMatch, _searchMatches.Count);
    }

    private void OnSearchNext()
    {
        if (_searchMatches.Count == 0) return;
        _currentSearchMatch = (_currentSearchMatch + 1) % _searchMatches.Count;
        UpdateSearchHighlights();
        SurfaceTabBarControl.UpdateMatchCount(_currentSearchMatch, _searchMatches.Count);
    }

    private void OnSearchPrevious()
    {
        if (_searchMatches.Count == 0) return;
        _currentSearchMatch = (_currentSearchMatch - 1 + _searchMatches.Count) % _searchMatches.Count;
        UpdateSearchHighlights();
        SurfaceTabBarControl.UpdateMatchCount(_currentSearchMatch, _searchMatches.Count);
    }

    private void OnSearchClosed()
    {
        ClearSearchHighlights();
        _searchMatches = [];
    }

    private void UpdateSearchHighlights()
    {
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        if (surface?.FocusedPaneId is string paneId)
        {
            var terminal = FindTerminalForPane(paneId);
            terminal?.SetSearchHighlights(_searchMatches, _currentSearchMatch);
        }
    }

    private void ClearSearchHighlights()
    {
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        if (surface?.FocusedPaneId is string paneId)
        {
            var terminal = FindTerminalForPane(paneId);
            terminal?.ClearSearchHighlights();
        }
    }

    private TerminalControl? FindTerminalForPane(string paneId)
    {
        return FindVisualChild<TerminalControl>(ContentArea, null);
    }

    private static T? FindVisualChild<T>(DependencyObject parent, Func<T, bool>? predicate) where T : DependencyObject
    {
        if (parent == null) return null;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && (predicate == null || predicate(typed)))
                return typed;
            var result = FindVisualChild(child, predicate);
            if (result != null) return result;
        }
        return null;
    }

    private static List<(int row, int col, int length)> FindAllInBuffer(ECode.Core.Terminal.TerminalBuffer buffer, string query)
    {
        var matches = new List<(int, int, int)>();
        if (string.IsNullOrEmpty(query)) return matches;

        for (int row = 0; row < buffer.Rows; row++)
        {
            var lineText = GetRowText(buffer, row);
            int idx = 0;
            while ((idx = lineText.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                matches.Add((row, idx, query.Length));
                idx++;
            }
        }
        return matches;
    }

    private static string GetRowText(ECode.Core.Terminal.TerminalBuffer buffer, int row)
    {
        var sb = new System.Text.StringBuilder();
        for (int col = 0; col < buffer.Cols; col++)
        {
            var cell = buffer.CellAt(row, col);
            sb.Append(cell.Character == '\0' ? ' ' : cell.Character);
        }
        return sb.ToString();
    }

    private void ShowTestNotification()
    {
        var workspaceId = ViewModel.SelectedWorkspace?.Workspace.Id ?? string.Empty;
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        var surfaceId = surface?.Surface.Id ?? string.Empty;
        var paneId = surface?.FocusedPaneId;

        App.NotificationService.AddNotification(
            workspaceId,
            surfaceId,
            paneId,
            "ecode 测试",
            "通知测试",
            "如果你在面板或系统通知中看到这条消息，说明通知功能正常。",
            ECode.Core.Models.NotificationSource.Cli);
    }

    private void OpenLogsWindow()
    {
        var window = new LogsWindow { Owner = this };
        window.ShowDialog();
    }

    private void OpenSessionVault()
    {
        var window = new SessionVaultWindow { Owner = this };
        window.ShowDialog();
    }

    private void OpenCommandHistoryPicker()
    {
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        if (surface?.FocusedPaneId is not string paneId)
            return;

        var history = surface.GetCommandHistory(paneId);
        if (history.Count == 0)
        {
            MessageBox.Show("此面板还没有命令历史记录。", "History", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var paneLabel = paneId.Length <= 8 ? paneId : paneId[..8];
        var window = new HistoryWindow(
            history,
            insertAction: command => surface.GetSession(paneId)?.Write(command),
            runAction: command =>
            {
                surface.RegisterCommandSubmission(paneId, command);
                surface.GetSession(paneId)?.Write(command + Environment.NewLine);
            })
        {
            Owner = this,
            Title = $"命令历史 · 面板 {paneLabel}",
        };

        window.ShowDialog();
    }

    private void InsertLastCommandFromHistory()
    {
        var surface = ViewModel.SelectedWorkspace?.SelectedSurface;
        if (surface?.FocusedPaneId is not string paneId)
            return;

        var history = surface.GetCommandHistory(paneId);
        if (history.Count == 0)
        {
            MessageBox.Show("此面板还没有命令历史记录。", "History", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var last = history[^1];
        surface.GetSession(paneId)?.Write(last);
    }

    private void OpenSettings()
    {
        var settings = new SettingsWindow { Owner = this };
        settings.ShowDialog();
    }
}

