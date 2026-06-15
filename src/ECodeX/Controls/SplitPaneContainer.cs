using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ECodeX.Core.Models;
using ECodeX.Core.Config;
using ECodeX.Core.Terminal;
using ECodeX.Services;
using ECodeX.ViewModels;
using ECodeX.Views;

namespace ECodeX.Controls;

/// <summary>
/// 将 SplitNode 树递归渲染为带 GridSplitter 可调分割条的嵌套 Grid 面板。
/// 叶子节点包含 TerminalControl 实例。
/// </summary>
public class SplitPaneContainer : ContentControl
{
    private SurfaceViewModel? _surface;
    private readonly Dictionary<string, TerminalControl> _terminalCache = [];
    private readonly Dictionary<string, BrowserControl> _browserCache = [];
    private int _lastAttentionVersion;

    public event Action? SearchRequested;

    private static SolidColorBrush GetThemeBrush(string key) =>
        Application.Current.Resources[key] as SolidColorBrush ?? Brushes.Transparent;

    private static Color GetThemeColor(string key) =>
        Application.Current.Resources[key] is Color c ? c : Colors.Transparent;

    public SplitPaneContainer()
    {
        Background = Brushes.Transparent;
        DataContextChanged += OnDataContextChanged;
        AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel), true);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SurfaceViewModel oldSurface)
        {
            oldSurface.PropertyChanged -= OnSurfacePropertyChanged;
            BrowserScriptingRuntime.UnregisterSurface(oldSurface.Surface.Id);
        }

        // 切换 Surface/项目时清除终端缓存
        // 防止重用来自其他项目的终端
        _terminalCache.Clear();
        _browserCache.Clear();

        _surface = e.NewValue as SurfaceViewModel;
        _lastAttentionVersion = 0;

        if (_surface != null)
        {
            _surface.PropertyChanged += OnSurfacePropertyChanged;
            Rebuild();
            ApplyAttentionPulse();
            QueueFocusCurrentPane();
        }
        else
        {
            Content = null;
        }
    }

    private void OnSurfacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SurfaceViewModel.RootNode)
            or nameof(SurfaceViewModel.IsZoomed))
        {
            Dispatcher.BeginInvoke(Rebuild);
        }
        else if (e.PropertyName is nameof(SurfaceViewModel.FocusedPaneId))
        {
            Dispatcher.BeginInvoke(UpdateFocusState);
        }
        else if (e.PropertyName is nameof(SurfaceViewModel.NotificationVersion))
        {
            Dispatcher.BeginInvoke(UpdateNotificationState);
        }
        else if (e.PropertyName is nameof(SurfaceViewModel.AttentionVersion))
        {
            Dispatcher.BeginInvoke(ApplyAttentionPulse);
        }
        else if (e.PropertyName is nameof(SurfaceViewModel.BrowserNavigationVersion))
        {
            Dispatcher.BeginInvoke(NavigateBrowserLeaves);
        }
        else if (e.PropertyName is nameof(SurfaceViewModel.ResumeBindingVersion))
        {
            Dispatcher.BeginInvoke(Rebuild);
        }
    }

    /// <summary>
    /// 仅更新缓存终端上与焦点相关的视觉状态，
    /// 而不重建整个 UI 树。
    /// </summary>
    private void UpdateFocusState()
    {
        if (_surface == null) return;

        // 在缩放模式下，如果缩放的面板发生变化，焦点变更可能需要重建"}

        if (_surface.IsZoomed)
        {
            Rebuild();
            QueueFocusCurrentPane();
            return;
        }

        foreach (var (paneId, terminal) in _terminalCache)
        {
            terminal.IsPaneFocused = paneId == _surface.FocusedPaneId;
        }

        QueueFocusCurrentPane();
    }

    private void UpdateNotificationState()
    {
        if (_surface == null) return;

        foreach (var (paneId, terminal) in _terminalCache)
        {
            terminal.HasNotification = _surface.HasUnreadNotification(paneId);
        }
    }

    private void Rebuild()
    {
        if (_surface == null) return;

        if (_surface.Surface.Kind == SurfaceKind.Browser)
            BrowserScriptingRuntime.UnregisterSurface(_surface.Surface.Id);

        // 缩放模式：仅全尺寸显示聚焦的面板
        if (_surface.IsZoomed && _surface.FocusedPaneId != null)
        {
            var focusedNode = _surface.RootNode.FindNode(_surface.FocusedPaneId);
            if (focusedNode != null)
            {
                Content = BuildLeaf(focusedNode);
                QueueFocusCurrentPane();
                return;
            }
        }

        Content = BuildNode(_surface.RootNode);
        QueueFocusCurrentPane();
    }

    private void ApplyAttentionPulse()
    {
        if (_surface == null || _surface.AttentionVersion == _lastAttentionVersion)
            return;

        _lastAttentionVersion = _surface.AttentionVersion;
        if (DateTime.UtcNow - _surface.AttentionRequestedAtUtc > TimeSpan.FromSeconds(2))
            return;

        var paneId = _surface.AttentionPaneId;
        if (!string.IsNullOrWhiteSpace(paneId) &&
            _terminalCache.TryGetValue(paneId, out var terminal))
        {
            terminal.FlashAttention();
        }
    }

    private UIElement BuildNode(SplitNode node)
    {
        if (node.IsLeaf)
        {
            return BuildLeaf(node);
        }

        return BuildSplit(node);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var terminal = FindTerminalUnderMouse(e.GetPosition(this));
        if (terminal == null)
            return;

        if (terminal.HandleMouseWheel(e.Delta, e.GetPosition(terminal)))
            e.Handled = true;
    }

    private TerminalControl? FindTerminalUnderMouse(Point point)
    {
        var hit = VisualTreeHelper.HitTest(this, point)?.VisualHit;
        return FindVisualAncestor<TerminalControl>(hit);
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void FocusTerminalPane(string paneId, TerminalControl terminal)
    {
        if (_surface == null)
            return;

        if (_surface.FocusedPaneId != paneId)
            _surface.FocusPane(paneId);

        terminal.IsPaneFocused = true;
        terminal.ActivateForInput();
    }

    public bool FocusCurrentPane()
    {
        if (_surface == null)
            return false;

        var paneId = _surface.FocusedPaneId;
        if (string.IsNullOrWhiteSpace(paneId) || _surface.RootNode.FindNode(paneId) == null)
        {
            paneId = _surface.RootNode.GetLeaves()
                .Select(leaf => leaf.PaneId)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

            if (!string.IsNullOrWhiteSpace(paneId))
                _surface.FocusPane(paneId);
        }

        if (string.IsNullOrWhiteSpace(paneId) ||
            !_terminalCache.TryGetValue(paneId, out var terminal))
        {
            if (!string.IsNullOrWhiteSpace(paneId) &&
                _browserCache.TryGetValue(paneId, out var browser))
            {
                browser.Focus();
                return true;
            }

            return false;
        }

        FocusTerminalPane(paneId, terminal);
        return true;
    }

    private void QueueFocusCurrentPane()
    {
        Dispatcher.BeginInvoke(() => FocusCurrentPane(), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private UIElement BuildLeaf(SplitNode node)
    {
        if (node.PaneId == null)
            return new Border { Background = Brushes.Transparent };

        var paneId = node.PaneId; // 为闭包捕获
        if (_surface?.Surface.Kind == SurfaceKind.Browser)
            return BuildBrowserLeaf(paneId);

        // 复用缓存中的终端（保留会话和滚动位置）
        if (!_terminalCache.TryGetValue(paneId, out var terminal))
        {
            terminal = new TerminalControl();
            _terminalCache[paneId] = terminal;
        }
        else
        {
            // 复用前从原父容器分离
            // 终端可能在 DockPanel（含头部）或 Border 中
            var oldParent = System.Windows.Media.VisualTreeHelper.GetParent(terminal) as FrameworkElement;
            
            if (oldParent is DockPanel dockPanel)
            {
                dockPanel.Children.Remove(terminal);
            }
            else if (oldParent is Border border)
            {
                border.Child = null;
            }
            
            // 清除旧的事件处理程序以防止内存泄漏和错误回调
            terminal.ClearEventHandlers();
        }

        // 用闭包连接事件处理程序，捕获当前面板 ID
        terminal.FocusRequested += () => FocusTerminalPane(paneId, terminal);
        terminal.CommandSubmitted += command => _surface?.RegisterCommandSubmission(paneId, command);
        terminal.ClearRequested += () => _surface?.CapturePaneTranscript(paneId, "clear-terminal");
        terminal.SplitRequested += dir =>
        {
            FocusTerminalPane(paneId, terminal);
            _surface?.SplitFocused(dir);
        };
        terminal.ZoomRequested += () => _surface?.ToggleZoom();
        terminal.ClosePaneRequested += () => _surface?.ClosePane(paneId);
        terminal.SearchRequested += () => SearchRequested?.Invoke();
        terminal.IsPaneFocused = paneId == _surface?.FocusedPaneId;
        terminal.IsSurfaceZoomed = _surface?.IsZoomed == true;
        terminal.HasNotification = _surface?.HasUnreadNotification(paneId) == true;
        var pendingResume = _surface?.GetPendingResumeBinding(paneId);

        // 附加终端会话
        var session = _surface?.GetSession(paneId);
        if (session != null)
            terminal.AttachSession(session);

        // 获取面板标题（自定义名称优先于 Shell 标题）
        var title = _surface?.GetPaneTitle(paneId, session?.Title) ?? "Terminal";

        // 创建带头部的面板
        var panel = new DockPanel { LastChildFill = true };
        panel.MouseEnter += (_, _) => FocusTerminalPane(paneId, terminal);
        panel.PreviewMouseDown += (_, _) => FocusTerminalPane(paneId, terminal);

        // 带标题和关闭按钮的头部栏
        var header = new Border
        {
            Background = GetThemeBrush("SidebarItemHoverBrush"),
            Height = 22,
            Padding = new Thickness(8, 2, 8, 2),
        };

        var headerMenu = new ContextMenu();
        var renamePane = new MenuItem { Header = "重命名面板" };
        renamePane.Click += (_, _) =>
        {
            var currentName = _surface?.GetPaneTitle(paneId, session?.Title) ?? "Terminal";
            var prompt = new TextPromptWindow(
                title: "重命名面板",
                message: "为此面板设置自定义名称。",
                defaultValue: currentName)
            {
                Owner = Window.GetWindow(this),
            };

            if (prompt.ShowDialog() == true && !string.IsNullOrWhiteSpace(prompt.ResponseText))
                _surface?.SetPaneCustomName(paneId, prompt.ResponseText);
        };
        headerMenu.Items.Add(renamePane);

        var resetPaneName = new MenuItem { Header = "重置面板名称" };
        resetPaneName.Click += (_, _) => _surface?.SetPaneCustomName(paneId, string.Empty);
        headerMenu.Items.Add(resetPaneName);

        header.ContextMenu = headerMenu;

        DockPanel.SetDock(header, Dock.Top);

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }); // 焦点指示器
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 标题
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }); // 关闭按钮

        // 焦点指示器（显示哪个面板被聚焦）
        var focusIndicator = new Border
        {
            Width = 3,
            Height = 12,
            CornerRadius = new CornerRadius(1.5),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = terminal.IsPaneFocused
                ? GetThemeBrush("AccentBrush")
                : GetThemeBrush("DividerBrush"),
        };
        Grid.SetColumn(focusIndicator, 0);

        // 标题文本
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 11,
            Foreground = GetThemeBrush("ForegroundBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(titleText, 1);

        // 关闭按钮
        var closeButton = new Button
        {
            Content = "\u2715",
            FontSize = 10,
            Width = 18,
            Height = 18,
            Background = Brushes.Transparent,
            Foreground = GetThemeBrush("ForegroundDimBrush"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "关闭面板",
        };
        closeButton.Click += (s, e) => _surface?.ClosePane(paneId);
        Grid.SetColumn(closeButton, 2);

        headerGrid.Children.Add(focusIndicator);
        headerGrid.Children.Add(titleText);
        headerGrid.Children.Add(closeButton);
        header.Child = headerGrid;

        panel.Children.Add(header);
        if (pendingResume != null)
        {
            var resumeBanner = CreateResumeBanner(paneId, pendingResume);
            DockPanel.SetDock(resumeBanner, Dock.Top);
            panel.Children.Add(resumeBanner);
        }
        panel.Children.Add(terminal);

        var focusedAccent = GetThemeColor("AccentColor");
        var errorBrush = GetThemeBrush("ErrorBrush");
        var container = new Border
        {
            Child = panel,
            BorderBrush = pendingResume != null
                ? errorBrush
                : terminal.IsPaneFocused
                ? new SolidColorBrush(Color.FromArgb(153, focusedAccent.R, focusedAccent.G, focusedAccent.B))
                : GetThemeBrush("BorderBrush"),
            BorderThickness = pendingResume != null ? new Thickness(2) : new Thickness(1),
        };
        container.MouseEnter += (_, _) => FocusTerminalPane(paneId, terminal);
        container.PreviewMouseDown += (_, _) => FocusTerminalPane(paneId, terminal);
        return container;
    }

    private UIElement CreateResumeBanner(string paneId, ResumeBinding binding)
    {
        var shellPreview = binding.Shell.Length > 96
            ? binding.Shell[..96] + "..."
            : binding.Shell;

        var banner = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(42, 239, 68, 68)),
            BorderBrush = GetThemeBrush("ErrorBrush"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(8, 4, 8, 4),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = $"可恢复：{shellPreview}",
            ToolTip = binding.Shell,
            Foreground = GetThemeBrush("ForegroundBrush"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(text, 0);

        var button = new Button
        {
            Content = "可恢复",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = GetThemeBrush("ErrorBrush"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            ToolTip = "确认后在此面板执行恢复命令",
        };
        button.Click += (_, e) =>
        {
            e.Handled = true;
            var result = MessageBox.Show(
                $"将在此面板执行恢复命令：\n\n{binding.Shell}",
                "恢复终端会话",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
                _surface?.RunPendingResumeBinding(paneId);
        };
        Grid.SetColumn(button, 1);

        var trustButton = new Button
        {
            Content = "信任并恢复",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = GetThemeBrush("SurfaceHighBrush"),
            Foreground = GetThemeBrush("ForegroundBrush"),
            BorderBrush = GetThemeBrush("ErrorBrush"),
            BorderThickness = new Thickness(1),
            ToolTip = "将此 binding 标记为可信并执行；启用自动恢复后可自动执行可信 binding",
        };
        trustButton.Click += (_, e) =>
        {
            e.Handled = true;
            var result = MessageBox.Show(
                $"将信任并执行此恢复命令：\n\n{binding.Shell}",
                "信任恢复命令",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
                _surface?.RunPendingResumeBinding(paneId, trustForFuture: true);
        };
        Grid.SetColumn(trustButton, 2);

        grid.Children.Add(text);
        grid.Children.Add(button);
        grid.Children.Add(trustButton);
        banner.Child = grid;
        return banner;
    }

    private UIElement BuildBrowserLeaf(string paneId)
    {
        if (_surface == null)
            return new Border { Background = Brushes.Transparent };

        if (!_browserCache.TryGetValue(paneId, out var browser))
        {
            browser = new BrowserControl
            {
                Focusable = true,
            };
            browser.CloseRequested += () => _surface?.ClosePane(paneId);
            browser.Browser.PropertyChanged += (_, _) => SyncBrowserMetadata(browser);
            _browserCache[paneId] = browser;
        }
        else
        {
            DetachFromParent(browser);
        }

        if (!string.IsNullOrWhiteSpace(_surface.Surface.BrowserUrl) &&
            !string.Equals(browser.Browser.Url, _surface.Surface.BrowserUrl, StringComparison.Ordinal))
        {
            browser.Navigate(_surface.Surface.BrowserUrl);
        }
        else if (string.IsNullOrWhiteSpace(browser.Browser.Url))
            browser.Navigate(string.IsNullOrWhiteSpace(_surface.Surface.BrowserUrl)
                ? "about:blank"
                : _surface.Surface.BrowserUrl);

        BrowserScriptingRuntime.Register(_surface.Surface.Id, paneId, browser);

        var container = new Border
        {
            Child = browser,
            BorderBrush = paneId == _surface.FocusedPaneId
                ? GetThemeBrush("AccentBrush")
                : GetThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };
        container.MouseEnter += (_, _) => _surface.FocusPane(paneId);
        container.PreviewMouseDown += (_, _) =>
        {
            _surface.FocusPane(paneId);
            browser.Focus();
        };

        return container;
    }

    private void SyncBrowserMetadata(BrowserControl browser)
    {
        if (_surface == null || _surface.Surface.Kind != SurfaceKind.Browser)
            return;

        _surface.Surface.BrowserUrl = browser.Browser.Url;
        _surface.Surface.BrowserTitle = browser.Browser.Title;
        _surface.Surface.BrowserHistory = browser.Browser.History.ToList();
    }

    private void NavigateBrowserLeaves()
    {
        if (_surface == null || _surface.Surface.Kind != SurfaceKind.Browser)
            return;

        foreach (var browser in _browserCache.Values)
        {
            if (!string.IsNullOrWhiteSpace(_surface.Surface.BrowserUrl) &&
                !string.Equals(browser.Browser.Url, _surface.Surface.BrowserUrl, StringComparison.Ordinal))
            {
                browser.Navigate(_surface.Surface.BrowserUrl);
            }
        }
    }

    private static void DetachFromParent(UIElement element)
    {
        var oldParent = VisualTreeHelper.GetParent(element) as FrameworkElement;
        if (oldParent is Border border)
            border.Child = null;
        else if (oldParent is Panel panel)
            panel.Children.Remove(element);
    }


    private UIElement BuildSplit(SplitNode node)
    {
        var grid = new Grid();

        if (node.Direction == SplitDirection.Vertical)
        {
            // 左 | 右
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(node.SplitRatio, GridUnitType.Star),
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(4, GridUnitType.Pixel),
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1 - node.SplitRatio, GridUnitType.Star),
            });

            if (node.First != null)
            {
                var first = BuildNode(node.First);
                Grid.SetColumn(first, 0);
                grid.Children.Add(first);
            }

            var splitter = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = FindResource("DividerBrush") as Brush ?? Brushes.Gray,
                Cursor = System.Windows.Input.Cursors.SizeWE,
            };
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);

            if (node.Second != null)
            {
                var second = BuildNode(node.Second);
                Grid.SetColumn(second, 2);
                grid.Children.Add(second);
            }
        }
        else
        {
            // 上 / 下
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(node.SplitRatio, GridUnitType.Star),
            });
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(4, GridUnitType.Pixel),
            });
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1 - node.SplitRatio, GridUnitType.Star),
            });

            if (node.First != null)
            {
                var first = BuildNode(node.First);
                Grid.SetRow(first, 0);
                grid.Children.Add(first);
            }

            var splitter = new GridSplitter
            {
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = FindResource("DividerBrush") as Brush ?? Brushes.Gray,
                Cursor = System.Windows.Input.Cursors.SizeNS,
            };
            Grid.SetRow(splitter, 1);
            grid.Children.Add(splitter);

            if (node.Second != null)
            {
                var second = BuildNode(node.Second);
                Grid.SetRow(second, 2);
                grid.Children.Add(second);
            }
        }

        return grid;
    }

    /// <summary>
    /// 更新所有缓存的终端控件的设置。
    /// </summary>
    public void UpdateAllTerminals(TerminalTheme theme, string fontFamily, int fontSize)
    {
        foreach (var terminal in _terminalCache.Values)
        {
            terminal.UpdateSettings(theme, fontFamily, fontSize);
        }
    }
}
