using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ECode.Core.Config;
using ECode.Core.Models;
using ECode.Core.Terminal;

namespace ECode.Controls;

/// <summary>
/// 用于渲染 TerminalBuffer 并处理键盘/鼠标输入的 WPF 控件。
/// 使用 DrawingVisual 高效渲染终端单元格网格。
/// 功能：回滚、URL 检测、搜索高亮、鼠标上报、可视响铃。
/// </summary>
public class TerminalControl : FrameworkElement
{
    private TerminalSession? _session;
    private readonly TerminalSelection _selection = new();
    private GhosttyTheme _theme;
    private DrawingVisual _visual;
    private Typeface _typeface;
    private double _cellWidth;
    private double _cellHeight;
    private double _fontSize;
    private int _cols;
    private int _rows;
    private bool _mouseDown;
    private int _scrollOffset; // 负值 = 已滚入历史，0 = 处于底部
    private bool _followOutput = true;
    private int _lastScrollbackCount;
    private int _renderQueued;
    private string _cursorStyle = "bar";
    private bool _cursorBlink = true;

    // 光标闪烁计时器
    private System.Windows.Threading.DispatcherTimer? _cursorTimer;
    private bool _cursorVisible = true;
    private bool _nativeCaretCreated;
    private IntPtr _nativeCaretHwnd;
    private HwndSource? _imeSource;
    private ImeMetrics? _lastImeMetrics;
    private IntPtr _lastImeMetricsHwnd;
    private int _lastCandidateMask;
    private int _imeRefreshQueued;
    private System.Windows.Threading.DispatcherTimer? _imeRefreshTimer;

    // TSF 输入法支持：使用不可见 TextBox 作为输入代理
    private TextBox? _imeProxy;

    // 可视响铃
    private DateTime _bellFlashUntil;
    private System.Windows.Threading.DispatcherTimer? _bellTimer;

    // 通知跳转后的短暂定位闪烁
    private DateTime _attentionFlashUntil;
    private System.Windows.Threading.DispatcherTimer? _attentionTimer;

    // URL 检测
    private (int row, int startCol, int endCol, string url)? _hoveredUrl;
    private int _lastUrlRow = -1;
    private List<(int startCol, int endCol, string url)>? _cachedRowUrls;

    // 搜索高亮
    private List<(int row, int col, int length)> _searchMatches = [];
    private int _currentSearchMatch = -1;
    private HashSet<(int row, int col)>? _searchMatchSetCache;
    private HashSet<(int row, int col)>? _currentMatchSetCache;
    private static readonly HashSet<(int row, int col)> EmptyMatchSet = [];
    private readonly StringBuilder _inputLineBuffer = new();
    private bool _suppressNextEnterToShell;

    // 渲染缓存以避免每帧分配
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = [];
    private Typeface? _typefaceBold;
    private Typeface? _typefaceItalic;
    private Typeface? _typefaceBoldItalic;
    private readonly StringBuilder _textRunBuffer = new();
    private readonly List<(char Character, int Width)> _textRunCells = [];
    private bool _suppressNextEnterTextInput;

    private static readonly string[] TerminalFontFallbacks =
    [
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "Courier New",
    ];

    private const byte DimTextAlpha = 90;

    private static readonly Dictionary<string, FontFamily> TerminalFontFamilyCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当面板请求焦点时触发。</summary>
    public event Action? FocusRequested;
    public event Action<string>? CommandSubmitted;
    public event Action? ClearRequested;
    public event Action<SplitDirection>? SplitRequested;
    public event Action? ZoomRequested;
    public event Action? ClosePaneRequested;
    public event Action? SearchRequested;

    /// <summary>清除所有事件处理程序（在重新附加到可视化树之前调用）。</summary>
    public void ClearEventHandlers()
    {
        FocusRequested = null;
        CommandSubmitted = null;
        ClearRequested = null;
        SplitRequested = null;
        ZoomRequested = null;
        ClosePaneRequested = null;
        SearchRequested = null;
    }

    /// <summary>此面板是否具有通知状态（蓝色光环）。</summary>
    public static readonly DependencyProperty HasNotificationProperty =
        DependencyProperty.Register(nameof(HasNotification), typeof(bool), typeof(TerminalControl),
            new PropertyMetadata(false, OnHasNotificationChanged));

    public bool HasNotification
    {
        get => (bool)GetValue(HasNotificationProperty);
        set => SetValue(HasNotificationProperty, value);
    }

    /// <summary>此面板是否处于聚焦状态。</summary>
    public static readonly DependencyProperty IsPaneFocusedProperty =
        DependencyProperty.Register(nameof(IsPaneFocused), typeof(bool), typeof(TerminalControl),
            new PropertyMetadata(false, OnIsPaneFocusedChanged));

    public bool IsPaneFocused
    {
        get => (bool)GetValue(IsPaneFocusedProperty);
        set => SetValue(IsPaneFocusedProperty, value);
    }

    /// <summary>父 Surface 当前是否处于缩放状态。</summary>
    public bool IsSurfaceZoomed { get; set; }

    public TerminalControl()
    {
        _theme = GhosttyConfigReader.ReadConfig();
        _visual = new DrawingVisual();
        AddVisualChild(_visual);
        AddLogicalChild(_visual);

        _fontSize = _theme.FontSize;
        _typeface = CreateTerminalTypeface(_theme.FontFamily, FontStyles.Normal, FontWeights.Normal);

        var settings = SettingsService.Current;
        _cursorStyle = settings.CursorStyle;
        _cursorBlink = settings.CursorBlink;

        CalculateCellSize();

        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
        AllowDrop = true;
        InputMethod.SetIsInputMethodEnabled(this, true);
        TextCompositionManager.AddTextInputStartHandler(this, OnImeTextInputPositionChanged);
        TextCompositionManager.AddTextInputUpdateHandler(this, OnImeTextInputPositionChanged);

        _imeProxy = new TextBox
        {
            Width = 1,
            Height = 1,
            Opacity = 0,
            IsHitTestVisible = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            AcceptsReturn = false,
            AcceptsTab = false
        };
        _imeProxy.TextChanged += OnImeProxyTextChanged;
        _imeProxy.PreviewTextInput += OnImeProxyPreviewTextInput;
        AddVisualChild(_imeProxy);
        AddLogicalChild(_imeProxy);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        _selection.SelectionChanged += () => RequestRender(System.Windows.Threading.DispatcherPriority.Render);

        // 光标闪烁
        _cursorTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(530),
        };
        _cursorTimer.Tick += (_, _) =>
        {
            bool wasVisible = _cursorVisible;
            if (!_cursorBlink)
                _cursorVisible = true;
            else
                _cursorVisible = !_cursorVisible;

            if (_cursorVisible != wasVisible)
                RequestRender();
        };
        _cursorTimer.Start();
    }

    public void AttachSession(TerminalSession session)
    {
        if (_session != null)
        {
            _session.Redraw -= OnRedraw;
            _session.BellReceived -= OnBell;
        }

        _session = session;
        _inputLineBuffer.Clear();
        _scrollOffset = 0;
        _followOutput = true;
        _lastScrollbackCount = _session.Buffer.ScrollbackCount;
        _session.Redraw += OnRedraw;
        _session.BellReceived += OnBell;
        CalculateTerminalSize();

        // 确保 IME 代理正确初始化（支持终端复用）
        if (_imeProxy != null)
        {
            _imeProxy.Clear();
            _imeProxy.IsEnabled = true;
            UpdateImeProxyPosition();
        }

        Render();
    }

    private void OnRedraw()
    {
        if (_session == null)
            return;

        var currentScrollback = _session.Buffer.ScrollbackCount;
        var scrollbackDelta = currentScrollback - _lastScrollbackCount;

        if (_followOutput || _scrollOffset == 0)
        {
            // 实时模式：始终紧贴底部。
            _scrollOffset = 0;
            _followOutput = true;
        }
        else if (_scrollOffset < 0 && scrollbackDelta > 0)
        {
            // 输出流式传输时冻结视口。
            _scrollOffset -= scrollbackDelta;
        }

        _scrollOffset = Math.Clamp(_scrollOffset, -currentScrollback, 0);
        if (_scrollOffset == 0)
            _followOutput = true;

        _lastScrollbackCount = currentScrollback;
        RequestRender();
        QueueImeCaretRefresh();
        UpdateImeProxyPosition();
    }

    private void OnBell()
    {
        _bellFlashUntil = DateTime.UtcNow.AddMilliseconds(150);
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);

        _bellTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(170),
        };
        // 重新启动计时器（处理快速连续的响铃序列）
        _bellTimer.Stop();
        _bellTimer.Tick -= OnBellTimerTick;
        _bellTimer.Tick += OnBellTimerTick;
        _bellTimer.Start();
    }

    private void OnBellTimerTick(object? sender, EventArgs e)
    {
        _bellTimer?.Stop();
        RequestRender();
    }

    public void FlashAttention()
    {
        _attentionFlashUntil = DateTime.UtcNow.AddMilliseconds(420);
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);

        _attentionTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(440),
        };
        _attentionTimer.Stop();
        _attentionTimer.Tick -= OnAttentionTimerTick;
        _attentionTimer.Tick += OnAttentionTimerTick;
        _attentionTimer.Start();
    }

    private void OnAttentionTimerTick(object? sender, EventArgs e)
    {
        _attentionTimer?.Stop();
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);
    }

    // --- 搜索支持 ---

    public void SetSearchHighlights(List<(int row, int col, int length)> matches, int currentIndex)
    {
        _searchMatches = matches;
        _currentSearchMatch = currentIndex;
        RebuildSearchMatchCache();
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);
    }

    public void ClearSearchHighlights()
    {
        _searchMatches = [];
        _currentSearchMatch = -1;
        _searchMatchSetCache = null;
        _currentMatchSetCache = null;
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);
    }

    private void RebuildSearchMatchCache()
    {
        var matchSet = new HashSet<(int row, int col)>();
        foreach (var (mRow, mCol, mLen) in _searchMatches)
        {
            for (int i = 0; i < mLen; i++)
                matchSet.Add((mRow, mCol + i));
        }
        _searchMatchSetCache = matchSet;

        if (_currentSearchMatch >= 0 && _currentSearchMatch < _searchMatches.Count)
        {
            var curSet = new HashSet<(int row, int col)>();
            var (cmRow, cmCol, cmLen) = _searchMatches[_currentSearchMatch];
            for (int i = 0; i < cmLen; i++)
                curSet.Add((cmRow, cmCol + i));
            _currentMatchSetCache = curSet;
        }
        else
        {
            _currentMatchSetCache = null;
        }
    }

    private void RequestRender(System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Background)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        if (Interlocked.Exchange(ref _renderQueued, 1) == 1)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _renderQueued, 0);
            Render();
        }, priority);
    }

    // --- 布局 ---

    private void CalculateCellSize()
    {
        var formattedText = new FormattedText(
            "M",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            _fontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        _cellWidth = formattedText.WidthIncludingTrailingWhitespace;
        _cellHeight = formattedText.Height;
    }

    private void CalculateTerminalSize()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || _cellWidth <= 0 || _cellHeight <= 0) return;

        int cols = Math.Max(1, (int)(ActualWidth / _cellWidth));
        int rows = Math.Max(1, (int)(ActualHeight / _cellHeight));

        if (cols != _cols || rows != _rows)
        {
            _cols = cols;
            _rows = rows;
            _session?.Resize(cols, rows);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        CalculateTerminalSize();
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);
        QueueImeCaretRefresh();
        UpdateImeProxyPosition();
    }

    // --- 渲染 ---

    private SolidColorBrush GetCachedBrush(Color color)
    {
        if (_brushCache.TryGetValue(color, out var brush))
            return brush;

        brush = new SolidColorBrush(color);
        brush.Freeze();
        _brushCache[color] = brush;
        return brush;
    }

    private void InvalidateRenderCaches()
    {
        _brushCache.Clear();
        _typefaceBold = null;
        _typefaceItalic = null;
        _typefaceBoldItalic = null;
    }

    private Typeface GetTypeface(bool bold, bool italic)
    {
        if (!bold && !italic) return _typeface;
        if (bold && !italic) return _typefaceBold ??= CreateTerminalTypeface(_theme.FontFamily, FontStyles.Normal, FontWeights.Bold);
        if (!bold && italic) return _typefaceItalic ??= CreateTerminalTypeface(_theme.FontFamily, FontStyles.Italic, FontWeights.Normal);
        return _typefaceBoldItalic ??= CreateTerminalTypeface(_theme.FontFamily, FontStyles.Italic, FontWeights.Bold);
    }

    private void Render()
    {
        if (_session == null) return;

        try
        {
            var buffer = _session.Buffer;
            using var dc = _visual.RenderOpen();
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // 背景
            var bgColor = ToWpfColor(_theme.Background);
            dc.DrawRectangle(GetCachedBrush(bgColor), null, new Rect(0, 0, ActualWidth, ActualHeight));

            // 可视响铃闪烁
            if (DateTime.UtcNow < _bellFlashUntil)
            {
                dc.DrawRectangle(GetCachedBrush(Color.FromArgb(25, 255, 255, 255)), null,
                    new Rect(0, 0, ActualWidth, ActualHeight));
            }

            // 通知跳转定位闪烁
            if (DateTime.UtcNow < _attentionFlashUntil)
            {
                dc.DrawRectangle(GetCachedBrush(Color.FromArgb(38, 0x1F, 0xA0, 0xFF)), null,
                    new Rect(0, 0, ActualWidth, ActualHeight));
                var attentionPen = new Pen(GetCachedBrush(Color.FromArgb(230, 0x1F, 0xA0, 0xFF)), 3);
                attentionPen.Freeze();
                dc.DrawRoundedRectangle(null, attentionPen, new Rect(2, 2, ActualWidth - 4, ActualHeight - 4), 6, 6);
            }

            // 计算回滚偏移
            int scrollbackCount = buffer.ScrollbackCount;
            bool isScrolledBack = _scrollOffset < 0;
            int viewStartLine = scrollbackCount + _scrollOffset;

            // 使用缓存的搜索匹配集合（在 SetSearchHighlights 中一次性构建）
            var searchMatchSet = _searchMatchSetCache ?? EmptyMatchSet;
            var currentMatchSet = _currentMatchSetCache ?? EmptyMatchSet;
            var searchMatchBrush = searchMatchSet.Count > 0 ? GetCachedBrush(Color.FromArgb(100, 0xFB, 0xBF, 0x24)) : null;
            var currentMatchBrush = currentMatchSet.Count > 0 ? GetCachedBrush(Color.FromArgb(180, 0xFB, 0x92, 0x3C)) : null;

            // 使用批处理文本渲染可见行
            for (int visRow = 0; visRow < _rows; visRow++)
            {
                int virtualLine = viewStartLine + visRow;
                bool isScrollback = virtualLine < scrollbackCount;
                int bufferRow = virtualLine - scrollbackCount;

                TerminalCell[]? scrollbackLine = null;
                if (isScrollback)
                    scrollbackLine = buffer.GetScrollbackLine(virtualLine);

                double y = visRow * _cellHeight;

                // 用于批处理的文本运行状态
                int runStartCol = -1;
                Color runFgColor = default;
                bool runBold = false, runItalic = false, runDim = false;
                bool runUnderline = false, runStrikethrough = false;
                _textRunBuffer.Clear();
                _textRunCells.Clear();

                for (int c = 0; c < _cols; c++)
                {
                    TerminalCell cell;
                    if (isScrollback)
                    {
                        cell = (scrollbackLine != null && c < scrollbackLine.Length)
                            ? scrollbackLine[c]
                            : TerminalCell.Empty;
                    }
                    else if (bufferRow >= 0 && bufferRow < buffer.Rows && c < buffer.Cols)
                    {
                        cell = buffer.CellAt(bufferRow, c);
                    }
                    else
                    {
                        cell = TerminalCell.Empty;
                    }

                    double x = c * _cellWidth;
                    var attr = cell.Attribute;
                    bool isSelected = _selection.IsSelected(visRow, c);
                    bool isInverse = attr.Flags.HasFlag(CellFlags.Inverse) != isSelected;

                    // 单元格颜色
                    TerminalColor cellBg, cellFg;
                    if (isInverse)
                    {
                        cellBg = attr.Foreground.IsDefault ? _theme.Foreground : attr.Foreground;
                        cellFg = attr.Background.IsDefault ? _theme.Background : attr.Background;
                    }
                    else
                    {
                        cellBg = attr.Background;
                        cellFg = attr.Foreground;
                    }

                    if (isSelected && _theme.SelectionBackground.HasValue)
                        cellBg = _theme.SelectionBackground.Value;

                    // 绘制单元格背景
                    if (!cellBg.IsDefault)
                    {
                        dc.DrawRectangle(GetCachedBrush(ToWpfColor(cellBg)), null,
                            new Rect(x, y, _cellWidth, _cellHeight));
                    }

                    // 搜索匹配高亮（位于文本下方）
                    bool isSearchMatch = searchMatchSet.Contains((visRow, c));
                    bool isCurrentMatch = currentMatchSet.Contains((visRow, c));
                    if (isCurrentMatch)
                        dc.DrawRectangle(currentMatchBrush, null, new Rect(x, y, _cellWidth, _cellHeight));
                    else if (isSearchMatch)
                        dc.DrawRectangle(searchMatchBrush, null, new Rect(x, y, _cellWidth, _cellHeight));

                    // URL 悬停高亮
                    if (_hoveredUrl is { } url && visRow == url.row && c >= url.startCol && c <= url.endCol)
                    {
                        var urlPen = new Pen(GetCachedBrush(Color.FromRgb(0x81, 0x8C, 0xF8)), 1);
                        urlPen.Freeze();
                        dc.DrawLine(urlPen, new Point(x, y + _cellHeight - 1), new Point(x + _cellWidth, y + _cellHeight - 1));
                    }

                    // 文本批渲染：把视觉样式相同的连续字符合并为同一次绘制
                    bool hasChar = cell.Character != '\0' && cell.Character != ' ';
                    if (hasChar)
                    {
                        var fgColor = cellFg.IsDefault ? ToWpfColor(_theme.Foreground) : ToWpfColor(cellFg);
                        bool bold = attr.Flags.HasFlag(CellFlags.Bold);
                        bool italic = attr.Flags.HasFlag(CellFlags.Italic);
                        bool dim = attr.Flags.HasFlag(CellFlags.Dim);
                        bool underline = attr.Flags.HasFlag(CellFlags.Underline);
                        bool strikethrough = attr.Flags.HasFlag(CellFlags.Strikethrough);

                        // 样式已更改？先刷新当前运行
                        if (runStartCol >= 0 && (fgColor != runFgColor || bold != runBold ||
                            italic != runItalic || dim != runDim ||
                            underline != runUnderline || strikethrough != runStrikethrough))
                        {
                            FlushTextRun(dc, dpi, y, runStartCol, runFgColor, runBold, runItalic, runDim, runUnderline, runStrikethrough);
                            runStartCol = -1;
                        }

                        // 开始新的运行或继续现有运行
                        if (runStartCol < 0)
                        {
                            runStartCol = c;
                            runFgColor = fgColor;
                            runBold = bold;
                            runItalic = italic;
                            runDim = dim;
                            runUnderline = underline;
                            runStrikethrough = strikethrough;
                            _textRunBuffer.Clear();
                            _textRunCells.Clear();
                        }

                        _textRunBuffer.Append(cell.Character);
                        _textRunCells.Add((cell.Character, Math.Max(1, cell.Width)));
                    }
                    else if (runStartCol >= 0)
                    {
                        // 空单元格 — 刷新当前运行
                        FlushTextRun(dc, dpi, y, runStartCol, runFgColor, runBold, runItalic, runDim, runUnderline, runStrikethrough);
                        runStartCol = -1;
                    }
                }

                // 刷新此行的最终运行
                if (runStartCol >= 0)
                    FlushTextRun(dc, dpi, y, runStartCol, runFgColor, runBold, runItalic, runDim, runUnderline, runStrikethrough);
            }

            // 光标（仅在查看实时缓冲区时）
            if (!isScrolledBack && buffer.CursorVisible && IsPaneFocused && (_cursorVisible || !_cursorBlink))
            {
                double cx = buffer.CursorCol * _cellWidth;
                double cy = buffer.CursorRow * _cellHeight;
                var cursorColor = _theme.CursorColor.HasValue
                    ? ToWpfColor(_theme.CursorColor.Value)
                    : ToWpfColor(_theme.Foreground);
                var cursorBrush = GetCachedBrush(Color.FromArgb(200, cursorColor.R, cursorColor.G, cursorColor.B));

                switch ((_cursorStyle ?? "bar").ToLowerInvariant())
                {
                    case "block":
                        dc.DrawRectangle(cursorBrush, null, new Rect(cx, cy, _cellWidth, _cellHeight));
                        break;
                    case "underline":
                        dc.DrawRectangle(cursorBrush, null, new Rect(cx, cy + _cellHeight - 2, _cellWidth, 2));
                        break;
                    default:
                        dc.DrawRectangle(cursorBrush, null, new Rect(cx, cy, 2, _cellHeight));
                        break;
                }
            }

            // 回滚指示器
            if (isScrolledBack)
            {
                int linesBack = -_scrollOffset;
                string indicator = $"[{linesBack}/{scrollbackCount}]";
                var indicatorText = new FormattedText(
                    indicator,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    _typeface,
                    10,
                    GetCachedBrush(Color.FromArgb(160, 0x81, 0x8C, 0xF8)),
                    dpi);
                double iw = indicatorText.WidthIncludingTrailingWhitespace + 12;
                double ih = indicatorText.Height + 4;
                double ix = ActualWidth - iw - 8;
                dc.DrawRoundedRectangle(
                    GetCachedBrush(Color.FromArgb(200, 0x14, 0x14, 0x14)), null,
                    new Rect(ix, 6, iw, ih), 4, 4);
                dc.DrawText(indicatorText, new Point(ix + 6, 8));
            }

            DrawPaneStateOverlay(dc);

            UpdateImeCaretPosition();

            // 每次渲染时同步更新 IME 代理位置
            if (_imeProxy != null && _session != null)
            {
                var buf = _session.Buffer;
                double x = buf.CursorCol * _cellWidth;
                double y = buf.CursorRow * _cellHeight;
                _imeProxy.Arrange(new Rect(x, y, _cellWidth, _cellHeight));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TerminalControl] Render failed: {ex}");
        }
    }

    private void DrawPaneStateOverlay(DrawingContext dc)
    {
        if (ActualWidth <= 2 || ActualHeight <= 2)
            return;

        // 放在终端内容绘制之后，避免被单元格背景覆盖。
        if (HasNotification)
        {
            var notificationColor = TryGetResourceColor("NotificationColor", Color.FromRgb(0x1F, 0xA0, 0xFF));
            var notifPen = new Pen(GetCachedBrush(Color.FromArgb(230, notificationColor.R, notificationColor.G, notificationColor.B)), 2);
            notifPen.Freeze();
            dc.DrawRoundedRectangle(null, notifPen, new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 5, 5);
        }

        if (IsPaneFocused)
        {
            var focusPen = new Pen(GetCachedBrush(Color.FromArgb(70, 0x81, 0x8C, 0xF8)), 1);
            focusPen.Freeze();
            dc.DrawRectangle(null, focusPen, new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1));
        }
    }

    private static Color TryGetResourceColor(string key, Color fallback)
    {
        if (Application.Current?.Resources[key] is Color color)
            return color;

        return fallback;
    }

    /// <summary>
    /// 绘制批处理的文本运行及其装饰（下划线/删除线）。
    /// </summary>
    private void FlushTextRun(DrawingContext dc, double dpi, double y, int startCol,
        Color fgColor, bool bold, bool italic, bool dim, bool underline, bool strikethrough)
    {
        if (_textRunBuffer.Length == 0) return;

        var brush = dim
            ? GetCachedBrush(Color.FromArgb(DimTextAlpha, fgColor.R, fgColor.G, fgColor.B))
            : GetCachedBrush(fgColor);
        var tf = GetTypeface(bold, italic);
        double x = startCol * _cellWidth;
        double runColumns = 0;
        foreach (var (_, width) in _textRunCells)
            runColumns += Math.Max(1, width);

        if (runColumns <= 0)
            runColumns = _textRunBuffer.Length;

        double runWidth = runColumns * _cellWidth;
        var text = new FormattedText(
            _textRunBuffer.ToString(),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            tf,
            _fontSize,
            brush,
            dpi);

        if (Math.Abs(text.WidthIncludingTrailingWhitespace - runWidth) <= 0.5)
        {
            dc.DrawText(text, new Point(x, y));
        }
        else
        {
            // 对于缺失/等宽字体回退到固定单元格位置。
            double colOffset = 0;
            foreach (var (character, width) in _textRunCells)
            {
                var cellText = new FormattedText(
                    character.ToString(),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    tf,
                    _fontSize,
                    brush,
                    dpi);

                dc.DrawText(cellText, new Point(x + colOffset * _cellWidth, y));
                colOffset += Math.Max(1, width);
            }
        }

        if (underline)
        {
            var pen = new Pen(brush, 1);
            pen.Freeze();
            dc.DrawLine(pen, new Point(x, y + _cellHeight - 1), new Point(x + runWidth, y + _cellHeight - 1));
        }

        if (strikethrough)
        {
            var pen = new Pen(brush, 1);
            pen.Freeze();
            dc.DrawLine(pen, new Point(x, y + _cellHeight / 2), new Point(x + runWidth, y + _cellHeight / 2));
        }
    }

    private static Color ToWpfColor(TerminalColor c) =>
        c.IsDefault ? Colors.Transparent : Color.FromRgb(c.R, c.G, c.B);

    private void OnImeTextInputPositionChanged(object sender, TextCompositionEventArgs e)
    {
        UpdateImeCaretPosition();
        QueueImeCaretRefresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 终端复用时重新初始化 IME 代理
        if (_imeProxy != null && _session != null)
        {
            RemoveVisualChild(_imeProxy);
            RemoveLogicalChild(_imeProxy);

            // 创建用于 TSF 输入法支持的 TextBox（必须可获得焦点）。
            _imeProxy = new TextBox
            {
                Width = 1,
                Height = 1,
                Opacity = 0,
                IsHitTestVisible = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Focusable = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                IsEnabled = true,
                IsReadOnly = false
            };
            InputMethod.SetIsInputMethodEnabled(_imeProxy, true);
            _imeProxy.PreviewKeyDown += OnImeProxyPreviewKeyDown;
            _imeProxy.TextChanged += OnImeProxyTextChanged;
            _imeProxy.PreviewTextInput += OnImeProxyPreviewTextInput;
            AddVisualChild(_imeProxy);
            AddLogicalChild(_imeProxy);
            UpdateImeProxyPosition();
        }

        if (IsPaneFocused)
            ActivateForInput();
        else
        {
            RegisterImeHook();
            UpdateImeCaretPosition();
            QueueImeCaretRefresh();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _imeRefreshTimer?.Stop();
        DestroyNativeCaret();
        UnregisterImeHook();
        _lastImeMetrics = null;
        _lastImeMetricsHwnd = IntPtr.Zero;
        _lastCandidateMask = 0;
    }

    private void RegisterImeHook()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
            return;

        if (ReferenceEquals(_imeSource, source))
            return;

        UnregisterImeHook();
        _imeSource = source;
        _imeSource.AddHook(OnImeWindowMessage);
    }

    public void ActivateForInput()
    {
        Focusable = true;

        if (!IsKeyboardFocusWithin)
        {
            Focus();
            Keyboard.Focus(this);

            var focusScope = FocusManager.GetFocusScope(this);
            if (focusScope != null)
                FocusManager.SetFocusedElement(focusScope, this);
        }

        RegisterImeHook();
        UpdateImeCaretPosition();
        QueueImeCaretRefresh();
    }

    private void UnregisterImeHook()
    {
        if (_imeSource == null)
            return;

        _imeSource.RemoveHook(OnImeWindowMessage);
        _imeSource = null;
    }

    private IntPtr OnImeWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!IsKeyboardFocusWithin)
            return IntPtr.Zero;

        switch (msg)
        {
            case WmImeStartComposition:
                _lastCandidateMask = 0;
                UpdateImeCaretPosition();
                QueueImeCaretRefresh();
                break;
            case WmImeComposition:
                UpdateImeCaretPosition();
                QueueImeCaretRefresh();
                break;
            case WmImeNotify:
                if (wParam.ToInt32() is ImnOpenCandidate or ImnChangeCandidate)
                {
                    _lastCandidateMask = unchecked((int)lParam.ToInt64());
                    UpdateImeCaretPosition();
                    QueueImeCaretRefresh();
                }
                else if (wParam.ToInt32() == ImnCloseCandidate)
                {
                    _lastCandidateMask = 0;
                }
                break;
            case WmImeRequest:
                return HandleImeRequest(hwnd, wParam, lParam, ref handled);
        }

        return IntPtr.Zero;
    }

    private IntPtr HandleImeRequest(IntPtr hwnd, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (lParam == IntPtr.Zero || !TryGetImeMetrics(hwnd, allowCached: true, out var metrics))
            return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case ImrCompositionWindow:
                Marshal.StructureToPtr(CreateCompositionForm(metrics), lParam, false);
                handled = true;
                return IntPtrOne;

            case ImrCandidateWindow:
                var candidateForm = CreateCandidateForm(metrics, 0);
                try
                {
                    var requestedForm = Marshal.PtrToStructure<CandidateForm>(lParam);
                    candidateForm.DwIndex = requestedForm.DwIndex;
                }
                catch
                {
                    candidateForm.DwIndex = 0;
                }

                Marshal.StructureToPtr(candidateForm, lParam, false);
                handled = true;
                return IntPtrOne;

            case ImrQueryCharPosition:
                Marshal.StructureToPtr(CreateImeCharPosition(metrics), lParam, false);
                handled = true;
                return IntPtrOne;
        }

        return IntPtr.Zero;
    }

    private void UpdateImeCaretPosition()
    {
        try
        {
            if (_session == null || !IsKeyboardFocusWithin || _cellWidth <= 0 || _cellHeight <= 0)
            {
                DestroyNativeCaret();
                return;
            }

            if (PresentationSource.FromVisual(this) is not HwndSource source ||
                source.Handle == IntPtr.Zero ||
                source.CompositionTarget == null)
            {
                DestroyNativeCaret();
                return;
            }

            var hwnd = source.Handle;
            if (!TryGetImeMetrics(hwnd, allowCached: true, out var metrics))
                return;

            // 创建/更新 Native Caret
            if (!_nativeCaretCreated || _nativeCaretHwnd != hwnd)
            {
                DestroyNativeCaret();
                _nativeCaretCreated = CreateCaret(hwnd, IntPtr.Zero, 1, metrics.CellHeight);
                _nativeCaretHwnd = _nativeCaretCreated ? hwnd : IntPtr.Zero;
                if (_nativeCaretCreated)
                    _ = ShowCaret(hwnd);
            }

            if (_nativeCaretCreated)
            {
                _ = SetCaretPos(metrics.CaretTopLeftClient.X, metrics.CaretTopLeftClient.Y);
                NotifyCaretLocationChanged(hwnd);
            }

            UpdateImeWindows(hwnd, metrics, _lastCandidateMask);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TerminalControl] IME caret positioning failed: {ex}");
        }
    }

    private void QueueImeCaretRefresh()
    {
        if (Interlocked.Exchange(ref _imeRefreshQueued, 1) == 1)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _imeRefreshQueued, 0);
            UpdateImeCaretPosition();
            StartDelayedImeCaretRefresh();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void StartDelayedImeCaretRefresh()
    {
        _imeRefreshTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(35),
        };

        _imeRefreshTimer.Stop();
        _imeRefreshTimer.Tick -= OnImeRefreshTimerTick;
        _imeRefreshTimer.Tick += OnImeRefreshTimerTick;
        _imeRefreshTimer.Start();
    }

    private void OnImeRefreshTimerTick(object? sender, EventArgs e)
    {
        _imeRefreshTimer?.Stop();
        UpdateImeCaretPosition();
    }

    private static void NotifyCaretLocationChanged(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            NotifyWinEvent(EventObjectLocationChange, hwnd, ObjidCaret, ChildidSelf);
    }

    private void DestroyNativeCaret()
    {
        if (!_nativeCaretCreated)
            return;

        _ = HideCaret(_nativeCaretHwnd);
        _ = DestroyCaret();
        _nativeCaretCreated = false;
        _nativeCaretHwnd = IntPtr.Zero;
    }

    private void UpdateImeWindows(IntPtr hwnd, ImeMetrics metrics, int candidateMask)
    {
        var himc = ImmGetContext(hwnd);
        if (himc == IntPtr.Zero)
            return;

        try
        {
            var compositionForm = CreateCompositionForm(metrics);
            _ = ImmSetCompositionWindow(himc, ref compositionForm);

            UpdateCandidateWindows(himc, metrics, candidateMask);
        }
        finally
        {
            _ = ImmReleaseContext(hwnd, himc);
        }
    }

    private void UpdateCandidateWindows(IntPtr himc, ImeMetrics metrics, int candidateMask)
    {
        var mask = unchecked((uint)candidateMask);
        if (mask == 0)
        {
            var candidateForm = CreateCandidateForm(metrics, 0);
            _ = ImmSetCandidateWindow(himc, ref candidateForm);
            return;
        }

        for (int index = 0; index < 32; index++)
        {
            if ((mask & (1u << index)) == 0)
                continue;

            var candidateForm = CreateCandidateForm(metrics, index);
            _ = ImmSetCandidateWindow(himc, ref candidateForm);
        }
    }

    private bool TryGetImeMetrics(IntPtr hwnd, bool allowCached, out ImeMetrics metrics)
    {
        metrics = default;

        if (!TryCalculateImeMetrics(hwnd, out metrics))
        {
            if (allowCached && _lastImeMetricsHwnd == hwnd && _lastImeMetrics.HasValue)
            {
                metrics = _lastImeMetrics.Value;
                return true;
            }

            return false;
        }

        _lastImeMetricsHwnd = hwnd;
        _lastImeMetrics = metrics;
        return true;
    }

    private bool TryCalculateImeMetrics(IntPtr hwnd, out ImeMetrics metrics)
    {
        metrics = default;

        if (_session == null || _cellWidth <= 0 || _cellHeight <= 0)
        {
            LogIme("TryCalculateImeMetrics: Failed - session or cell size invalid");
            return false;
        }

        if (PresentationSource.FromVisual(this) is not HwndSource source ||
            source.Handle != hwnd ||
            source.CompositionTarget == null)
        {
            LogIme("TryCalculateImeMetrics: Failed - invalid source");
            return false;
        }

        var buffer = _session.Buffer;
        int cursorCol = Math.Clamp(buffer.CursorCol, 0, Math.Max(0, buffer.Cols - 1));
        int cursorRow = Math.Clamp(buffer.CursorRow, 0, Math.Max(0, buffer.Rows - 1));

        var caretTopLeft = new Point(cursorCol * _cellWidth, cursorRow * _cellHeight);
        LogIme($"Cursor: col={cursorCol}, row={cursorRow}, localPoint=({caretTopLeft.X:F1}, {caretTopLeft.Y:F1})");

        if (!TryToHwndClientPixel(source, caretTopLeft, out var caretTopLeftClient) ||
            !TryToHwndClientPixel(source, new Point(0, 0), out var documentTopLeft) ||
            !TryToHwndClientPixel(source, new Point(ActualWidth, ActualHeight), out var documentBottomRight))
        {
            LogIme("TryCalculateImeMetrics: Coordinate conversion failed");
            return false;
        }

        LogIme($"ClientPixel: ({caretTopLeftClient.X}, {caretTopLeftClient.Y})");

        var deviceSize = source.CompositionTarget.TransformToDevice.Transform(new Vector(_cellWidth, _cellHeight));
        int cellWidth = Math.Max(1, (int)Math.Ceiling(Math.Abs(deviceSize.X)));
        int cellHeight = Math.Max(1, (int)Math.Ceiling(Math.Abs(deviceSize.Y)));

        var caretBottomLeftClient = new NativePoint(caretTopLeftClient.X, caretTopLeftClient.Y + cellHeight);
        var caretRectClient = new NativeRect(
            caretTopLeftClient.X,
            caretTopLeftClient.Y,
            caretTopLeftClient.X + cellWidth,
            caretTopLeftClient.Y + cellHeight);

        var documentRectClient = NativeRect.FromPoints(documentTopLeft, documentBottomRight);

        var charPositionScreen = caretTopLeftClient;
        var documentTopLeftScreen = documentTopLeft;
        var documentBottomRightScreen = documentBottomRight;
        if (!ClientToScreen(hwnd, ref charPositionScreen) ||
            !ClientToScreen(hwnd, ref documentTopLeftScreen) ||
            !ClientToScreen(hwnd, ref documentBottomRightScreen))
        {
            return false;
        }

        var documentRectScreen = NativeRect.FromPoints(documentTopLeftScreen, documentBottomRightScreen);
        metrics = new ImeMetrics(
            caretTopLeftClient,
            caretBottomLeftClient,
            caretRectClient,
            documentRectClient,
            charPositionScreen,
            documentRectScreen,
            cellHeight);
        return true;
    }

    private static CompositionForm CreateCompositionForm(ImeMetrics metrics) =>
        new()
        {
            DwStyle = CfsForcePosition,
            PtCurrentPos = metrics.CaretTopLeftClient,
            RcArea = metrics.DocumentRectClient,
        };

    private static CandidateForm CreateCandidateForm(ImeMetrics metrics, int index) =>
        new()
        {
            DwIndex = index,
            DwStyle = CfsCandidatePos | CfsForcePosition,
            PtCurrentPos = metrics.CaretBottomLeftClient,
            RcArea = metrics.DocumentRectClient,
        };

    private static ImeCharPosition CreateImeCharPosition(ImeMetrics metrics) =>
        new()
        {
            DwSize = Marshal.SizeOf<ImeCharPosition>(),
            DwCharPos = 0,
            Pt = metrics.CharPositionScreen,
            CLineHeight = metrics.CellHeight,
            RcDocument = metrics.DocumentRectScreen,
        };

    private bool TryToHwndClientPixel(HwndSource source, Point localPoint, out NativePoint point)
    {
        // 直接使用屏幕坐标转换，更可靠地处理嵌套布局
        return TryPointToScreenClient(source.Handle, localPoint, out point);
    }

    private bool TryPointToScreenClient(IntPtr hwnd, Point localPoint, out NativePoint point)
    {
        point = default;

        try
        {
            var screenPoint = PointToScreen(localPoint);
            point = new NativePoint((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y));
            bool success = ScreenToClient(hwnd, ref point);
            LogIme($"ScreenToClient: Local({localPoint.X:F1},{localPoint.Y:F1}) -> Screen({screenPoint.X:F1},{screenPoint.Y:F1}) -> Client({point.X},{point.Y}), Success={success}");
            return success;
        }
        catch (InvalidOperationException ex)
        {
            LogIme($"TryPointToScreenClient exception: {ex.Message}");
            return false;
        }
    }

    private static Typeface CreateTerminalTypeface(string? preferredFamily, FontStyle style, FontWeight weight) =>
        new(ResolveTerminalFontFamily(preferredFamily), style, weight, FontStretches.Normal);

    private static FontFamily ResolveTerminalFontFamily(string? preferredFamily)
    {
        var key = string.IsNullOrWhiteSpace(preferredFamily) ? "" : preferredFamily.Trim();

        lock (TerminalFontFamilyCache)
        {
            if (TerminalFontFamilyCache.TryGetValue(key, out var cached))
                return cached;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in GetTerminalFontCandidates(key))
        {
            if (!seen.Add(candidate))
                continue;

            if (IsInstalledFontFamily(candidate))
            {
                var resolved = new FontFamily(candidate);
                lock (TerminalFontFamilyCache)
                    TerminalFontFamilyCache[key] = resolved;
                return resolved;
            }
        }

        var fallback = new FontFamily("Consolas");
        lock (TerminalFontFamilyCache)
            TerminalFontFamilyCache[key] = fallback;
        return fallback;
    }

    private static IEnumerable<string> GetTerminalFontCandidates(string preferredFamily)
    {
        if (!string.IsNullOrWhiteSpace(preferredFamily))
        {
            foreach (var candidate in preferredFamily.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return candidate;
        }

        foreach (var fallback in TerminalFontFallbacks)
            yield return fallback;
    }

    private static bool IsInstalledFontFamily(string familyName) =>
        Fonts.SystemFontFamilies.Any(font =>
            string.Equals(font.Source, familyName, StringComparison.OrdinalIgnoreCase) ||
            font.FamilyNames.Values.Any(name => string.Equals(name, familyName, StringComparison.OrdinalIgnoreCase)));

    // --- 鼠标上报 ---

    private bool IsMouseTrackingActive =>
        _session?.Buffer.MouseEnabled == true;

    private void SendMouseReport(int button, int col, int row, bool press)
    {
        if (_session == null) return;
        var buf = _session.Buffer;
        if (!buf.MouseEnabled) return;

        col = Math.Clamp(col, 0, buf.Cols - 1);
        row = Math.Clamp(row, 0, buf.Rows - 1);

        if (buf.MouseSgrExtended)
        {
            char suffix = press ? 'M' : 'm';
            _session.Write($"\x1b[<{button};{col + 1};{row + 1}{suffix}");
        }
        else if (press)
        {
            char cb = (char)(button + 32);
            char cx = (char)(col + 33);
            char cy = (char)(row + 33);
            _session.Write($"\x1b[M{cb}{cx}{cy}");
        }
    }

    // --- Keyboard input ---

    private void EnsureLiveView()
    {
        if (_session == null)
            return;

        if (_scrollOffset == 0 && _followOutput)
            return;

        _scrollOffset = 0;
        _followOutput = true;
        _lastScrollbackCount = _session.Buffer.ScrollbackCount;
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);
    }

    private void TrackInputText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\b':
                    if (_inputLineBuffer.Length > 0)
                        _inputLineBuffer.Length--;
                    break;

                case '\r':
                case '\n':
                    SubmitBufferedCommand();
                    break;

                default:
                    if (!char.IsControl(ch))
                    {
                        _inputLineBuffer.Append(ch);

                        if (_inputLineBuffer.Length > 4096)
                            _inputLineBuffer.Remove(0, _inputLineBuffer.Length - 4096);
                    }
                    break;
            }
        }
    }

    private void SubmitBufferedCommand()
    {
        var rawCommand = _inputLineBuffer.ToString();
        var command = rawCommand.Trim();
        _inputLineBuffer.Clear();

        if (string.IsNullOrWhiteSpace(command))
            return;
        CommandSubmitted?.Invoke(command);
    }

    private bool CopySelectionToClipboard()
    {
        if (_session == null || !_selection.HasSelection)
            return false;

        var text = _selection.GetSelectedText(_session.Buffer, _scrollOffset);
        if (string.IsNullOrEmpty(text))
            return false;

        Clipboard.SetText(text);
        _selection.ClearSelection();
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_session == null) return;
        UpdateImeCaretPosition();

        var modifiers = Keyboard.Modifiers;
        bool ctrl = modifiers.HasFlag(ModifierKeys.Control);
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = modifiers.HasFlag(ModifierKeys.Alt);

        // 让应用级快捷键可以冒泡到 MainWindow。
        // Ctrl+Alt 组合（面板聚焦）、Ctrl+Tab（Surface 循环），
        // 以及 Ctrl+Shift 组合（分屏、缩放、搜索等）属于应用级快捷键。
        if (ctrl && alt) return;
        if (ctrl && shift) return;
        if (ctrl && e.Key == Key.Tab) return;

        // Ctrl+Backspace：删除上一个词（发送 Ctrl+W / unix-word-rubout）
        if (ctrl && e.Key == Key.Back)
        {
            _inputLineBuffer.Clear();
            EnsureLiveView();
            _session.Write("\x17");
            QueueImeCaretRefresh();
            e.Handled = true;
            return;
        }

        // 终端级快捷键
        if (ctrl && e.Key == Key.C)
        {
            if (!CopySelectionToClipboard())
            {
                // 没有选中内容时，把 Ctrl+C 转发给 Shell 作为中断信号。
                _inputLineBuffer.Clear();
                EnsureLiveView();
                _session.Write("\x03");
                QueueImeCaretRefresh();
            }

            e.Handled = true;
            return;
        }

        if ((ctrl && e.Key == Key.V) || (shift && e.Key == Key.Insert))
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Insert)
        {
            _ = CopySelectionToClipboard();
            e.Handled = true;
            return;
        }

        // 将 Ctrl+字母 作为控制字节转发给 Shell（例如 Ctrl+X => 0x18），以支持 nano 等 TUI 应用。
        if (ctrl && !modifiers.HasFlag(ModifierKeys.Alt) && TryGetCtrlLetterSequence(e.Key, out var ctrlSequence))
        {
            _inputLineBuffer.Clear();
            EnsureLiveView();
            _session.Write(ctrlSequence);
            QueueImeCaretRefresh();
            e.Handled = true;
            return;
        }

        bool appCursor = _session.Buffer.ApplicationCursorKeys;
        string? sequence = KeyToVtSequence(e.Key, modifiers, appCursor);
        if (sequence != null)
        {
            if (e.Key == Key.Back)
                TrackInputText("\b");
            else if (e.Key == Key.Enter)
            {
                SubmitBufferedCommand();
                if (_suppressNextEnterToShell)
                {
                    _suppressNextEnterToShell = false;
                    e.Handled = true;
                    return;
                }
            }

            EnsureLiveView();
            _session.Write(sequence);
            QueueImeCaretRefresh();
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (_session == null || string.IsNullOrEmpty(e.Text)) return;

        // 输入开始时强制更新 IME 位置
        UpdateImeCaretPosition();

        // KeyDown 已经处理了 Enter；当被截获的命令已经消费掉 Shell 提交时，
        // 抑制 TextInput 路径尾随产生的 CR/LF。
        if (_suppressNextEnterTextInput && (e.Text.Contains('\r') || e.Text.Contains('\n')))
        {
            _suppressNextEnterTextInput = false;
            e.Handled = true;
            return;
        }

        // 防止 TextInput 路径重复写入换行。
        if (e.Text.Contains('\r') || e.Text.Contains('\n'))
        {
            e.Handled = true;
            return;
        }

        // 处理 Ctrl+C（有选中内容时复制）
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Text == "\x03")
        {
            if (_selection.HasSelection)
            {
                var text = _selection.GetSelectedText(_session.Buffer, _scrollOffset);
                if (!string.IsNullOrEmpty(text))
                    Clipboard.SetText(text);
                _selection.ClearSelection();
                return;
            }
        }

        // 处理 Ctrl+V（粘贴）
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Text == "\x16")
        {
            PasteFromClipboard();
            return;
        }

        EnsureLiveView();
        TrackInputText(e.Text);
        _session.Write(e.Text);
        _selection.ClearSelection();
        QueueImeCaretRefresh();
    }

    private void PasteFromClipboard()
    {
        if (_session == null) return;
        if (!TryGetClipboardPasteText(out var text)) return;

        PasteText(text);
    }

    private void PasteText(string text)
    {
        if (_session == null || string.IsNullOrEmpty(text)) return;

        EnsureLiveView();
        TrackInputText(text);

        if (_session.Buffer.BracketedPasteMode)
            _session.Write("\x1b[200~" + text + "\x1b[201~");
        else
            _session.Write(text);
        QueueImeCaretRefresh();
    }

    private static bool HasClipboardPasteContent()
    {
        try
        {
            return Clipboard.ContainsText()
                || Clipboard.ContainsFileDropList()
                || Clipboard.ContainsImage();
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetClipboardPasteText(out string text)
    {
        text = string.Empty;

        try
        {
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();
                return !string.IsNullOrEmpty(text);
            }

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                var paths = files.Cast<string>()
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToArray();

                if (paths.Length > 0)
                {
                    text = string.Join(" ", paths.Select(QuotePathForShell));
                    return true;
                }
            }

            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    var tempPath = SaveBitmapSourceToTempFile(image);
                    if (!string.IsNullOrWhiteSpace(tempPath))
                    {
                        text = QuotePathForShell(tempPath);
                        return true;
                    }
                }
            }
        }
        catch
        {
            // 忽略剪贴板的竞争条件或格式异常，并视为剪贴板不可用。
        }

        return false;
    }

    private static string? SaveBitmapSourceToTempFile(BitmapSource image)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "ecode", "clipboard-images");
            Directory.CreateDirectory(dir);

            var fileName = $"ecode-clipboard-{DateTime.Now:yyyyMMdd-HHmmssfff}.png";
            var fullPath = Path.Combine(dir, fileName);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using var stream = File.Create(fullPath);
            encoder.Save(stream);

            return fullPath;
        }
        catch
        {
            return null;
        }
    }

    private static string QuotePathForShell(string path)
    {
        if (path.IndexOfAny([' ', '\t', '\n', '\r', '"']) < 0)
            return path;

        return "\"" + path.Replace("\"", "\\\"") + "\"";
    }

    private static bool HasDropContent(IDataObject? data)
    {
        if (data == null)
            return false;

        try
        {
            return data.GetDataPresent(DataFormats.FileDrop)
                || data.GetDataPresent(DataFormats.UnicodeText)
                || data.GetDataPresent(DataFormats.Text)
                || data.GetDataPresent(DataFormats.Bitmap)
                || data.GetDataPresent("PNG");
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDropPasteText(IDataObject? data, out string text)
    {
        text = string.Empty;
        if (data == null)
            return false;

        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) &&
                data.GetData(DataFormats.FileDrop) is string[] files &&
                files.Length > 0)
            {
                text = string.Join(" ", files
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(QuotePathForShell));
                return !string.IsNullOrWhiteSpace(text);
            }

            if (data.GetDataPresent(DataFormats.UnicodeText) &&
                data.GetData(DataFormats.UnicodeText) is string unicodeText &&
                !string.IsNullOrEmpty(unicodeText))
            {
                text = unicodeText;
                return true;
            }

            if (data.GetDataPresent(DataFormats.Text) &&
                data.GetData(DataFormats.Text) is string plainText &&
                !string.IsNullOrEmpty(plainText))
            {
                text = plainText;
                return true;
            }

            if (TryGetDropBitmapSource(data, out var bitmap))
            {
                var tempPath = SaveBitmapSourceToTempFile(bitmap);
                if (!string.IsNullOrWhiteSpace(tempPath))
                {
                    text = QuotePathForShell(tempPath);
                    return true;
                }
            }
        }
        catch
        {
            // 忽略拖拽数据转换失败。
        }

        return false;
    }

    private static bool TryGetDropBitmapSource(IDataObject data, out BitmapSource bitmap)
    {
        bitmap = null!;

        if (data.GetDataPresent(DataFormats.Bitmap))
        {
            var value = data.GetData(DataFormats.Bitmap);
            if (value is BitmapSource bitmapSource)
            {
                bitmap = bitmapSource;
                return true;
            }
        }

        if (data.GetDataPresent("PNG"))
        {
            var value = data.GetData("PNG");
            if (value is MemoryStream memoryStream)
            {
                memoryStream.Position = 0;
                var frame = BitmapFrame.Create(memoryStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame.Freeze();
                bitmap = frame;
                return true;
            }

            if (value is byte[] bytes && bytes.Length > 0)
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame.Freeze();
                bitmap = frame;
                return true;
            }
        }

        return false;
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effects = HasDropContent(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = HasDropContent(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        Focus();
        FocusRequested?.Invoke();

        if (_session != null && TryGetDropPasteText(e.Data, out var text))
        {
            PasteText(text);
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    // --- Mouse input ---

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        FocusRequested?.Invoke();

        if (_cols <= 0 || _rows <= 0) return;

        var pos = e.GetPosition(this);
        int col = Math.Clamp((int)(pos.X / _cellWidth), 0, _cols - 1);
        int row = Math.Clamp((int)(pos.Y / _cellHeight), 0, _rows - 1);

        // Ctrl+点击 打开 URL
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _hoveredUrl.HasValue)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_hoveredUrl.Value.url) { UseShellExecute = true });
            }
            catch { }
            e.Handled = true;
            return;
        }

        // 鼠标上报（应用请求鼠标事件时绕过选区）
        if (IsMouseTrackingActive)
        {
            SendMouseReport(0, col, row, true);
            _mouseDown = true;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2 && _session != null)
        {
            _selection.SelectWord(_session.Buffer, row, col, _scrollOffset);
        }
        else if (e.ClickCount == 3 && _session != null)
        {
            _selection.SelectLine(row, _session.Buffer.Cols);
        }
        else
        {
            _selection.StartSelection(row, col);
            _mouseDown = true;
            CaptureMouse();
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_cols <= 0 || _rows <= 0) return;

        var pos = e.GetPosition(this);
        int col = Math.Clamp((int)(pos.X / _cellWidth), 0, _cols - 1);
        int row = Math.Clamp((int)(pos.Y / _cellHeight), 0, _rows - 1);

        // URL 检测（按住 Ctrl）—— 按行缓存扫描到的 URL
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _session != null && row < _session.Buffer.Rows)
        {
            // 仅在行号发生变化时重新扫描
            if (row != _lastUrlRow)
            {
                _lastUrlRow = row;
                var lineText = UrlDetector.GetRowText(_session.Buffer, row);
                _cachedRowUrls = UrlDetector.FindUrls(lineText);
            }

            // 在缓存的 URL 中查找当前列是否有命中
            var oldHover = _hoveredUrl;
            _hoveredUrl = null;
            if (_cachedRowUrls != null)
            {
                foreach (var (startCol, endCol, url) in _cachedRowUrls)
                {
                    if (col >= startCol && col <= endCol)
                    {
                        _hoveredUrl = (row, startCol, endCol, url);
                        break;
                    }
                }
            }

            Cursor = _hoveredUrl.HasValue ? Cursors.Hand : Cursors.Arrow;
            if (_hoveredUrl != oldHover)
                RequestRender(System.Windows.Threading.DispatcherPriority.Render);
        }
        else if (_hoveredUrl.HasValue)
        {
            _hoveredUrl = null;
            _lastUrlRow = -1;
            _cachedRowUrls = null;
            Cursor = Cursors.Arrow;
            RequestRender(System.Windows.Threading.DispatcherPriority.Render);
        }

        // 鼠标上报（移动事件）
        if (IsMouseTrackingActive && _mouseDown)
        {
            var buf = _session!.Buffer;
            if (buf.MouseTrackingButton || buf.MouseTrackingAny)
            {
                SendMouseReport(32, col, row, true); // 32 = 移动事件标志位
            }
            return;
        }
        if (IsMouseTrackingActive && _session!.Buffer.MouseTrackingAny)
        {
            SendMouseReport(35, col, row, true); // 35 = 无按键移动
            return;
        }

        // 选区拖拽
        if (_mouseDown && !IsMouseTrackingActive)
        {
            _selection.ExtendSelection(row, col);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (IsMouseTrackingActive && _mouseDown && _cols > 0 && _rows > 0)
        {
            var pos = e.GetPosition(this);
            int col = Math.Clamp((int)(pos.X / _cellWidth), 0, _cols - 1);
            int row = Math.Clamp((int)(pos.Y / _cellHeight), 0, _rows - 1);
            SendMouseReport(0, col, row, false);
        }

        if (_mouseDown)
        {
            _mouseDown = false;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);

        if (IsMouseTrackingActive)
        {
            if (_cols <= 0 || _rows <= 0) return;

            var pos = e.GetPosition(this);
            int col = Math.Clamp((int)(pos.X / _cellWidth), 0, _cols - 1);
            int row = Math.Clamp((int)(pos.Y / _cellHeight), 0, _rows - 1);
            SendMouseReport(2, col, row, true);
            return;
        }

        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3C)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
        };

        var menuItemStyle = new Style(typeof(MenuItem));
        menuItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE9))));
        menuItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        menuItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        menuItemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));

        var separatorStyle = new Style(typeof(Separator));
        separatorStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3C))));
        separatorStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2)));

        menu.Resources.Add(typeof(MenuItem), menuItemStyle);
        menu.Resources.Add(typeof(Separator), separatorStyle);

        // 复制
        var copyItem = new MenuItem { Header = "复制", InputGestureText = "Ctrl+C" };
        copyItem.Icon = MakeIcon("\uE8C8");
        copyItem.IsEnabled = _selection.HasSelection;
        copyItem.Click += (_, _) =>
        {
            if (_session != null)
            {
                var text = _selection.GetSelectedText(_session.Buffer, _scrollOffset);
                if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
                _selection.ClearSelection();
            }
        };
        menu.Items.Add(copyItem);

        // 粘贴
        var pasteItem = new MenuItem { Header = "粘贴", InputGestureText = "Ctrl+V" };
        pasteItem.Icon = MakeIcon("\uE77F");
        pasteItem.IsEnabled = HasClipboardPasteContent();
        pasteItem.Click += (_, _) => PasteFromClipboard();
        menu.Items.Add(pasteItem);

        // 全选
        var selectAllItem = new MenuItem { Header = "全选" };
        selectAllItem.Icon = MakeIcon("\uE8B3");
        selectAllItem.Click += (_, _) =>
        {
            if (_session != null)
                _selection.SelectAll(_session.Buffer.Rows, _session.Buffer.Cols);
        };
        menu.Items.Add(selectAllItem);

        menu.Items.Add(new Separator());

        // 向右分屏
        var splitRight = new MenuItem { Header = "向右分屏", InputGestureText = "Ctrl+D" };
        splitRight.Icon = MakeIcon("\uE745");
        splitRight.Click += (_, _) => SplitRequested?.Invoke(SplitDirection.Vertical);
        menu.Items.Add(splitRight);

        // 向下分屏
        var splitDown = new MenuItem { Header = "向下分屏", InputGestureText = "Ctrl+Shift+D" };
        splitDown.Icon = MakeIcon("\uE74B");
        splitDown.Click += (_, _) => SplitRequested?.Invoke(SplitDirection.Horizontal);
        menu.Items.Add(splitDown);

        menu.Items.Add(new Separator());

        // 缩放
        var isZoomed = IsSurfaceZoomed;
        var zoom = new MenuItem
        {
            Header = isZoomed ? "取消缩放面板" : "缩放面板",
            InputGestureText = "Ctrl+Shift+Z",
            IsCheckable = true,
            IsChecked = isZoomed,
        };
        zoom.Icon = MakeIcon(isZoomed ? "\uE73F" : "\uE740");
        zoom.Click += (_, _) => ZoomRequested?.Invoke();
        menu.Items.Add(zoom);

        // 关闭面板
        var closePane = new MenuItem { Header = "关闭面板" };
        closePane.Icon = MakeIcon("\uE711");
        closePane.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        closePane.Click += (_, _) => ClosePaneRequested?.Invoke();
        menu.Items.Add(closePane);

        menu.Items.Add(new Separator());

        // 清屏
        var clear = new MenuItem { Header = "清屏" };
        clear.Icon = MakeIcon("\uE894");
        clear.Click += (_, _) =>
        {
            ClearRequested?.Invoke();
            ClearTerminalView();
        };
        menu.Items.Add(clear);

        // 搜索
        var search = new MenuItem { Header = "搜索", InputGestureText = "Ctrl+Shift+F" };
        search.Icon = MakeIcon("\uE721");
        search.Click += (_, _) => SearchRequested?.Invoke();
        menu.Items.Add(search);

        menu.IsOpen = true;
        e.Handled = true;
    }

    private static TextBlock MakeIcon(string glyph) =>
        new() { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12 };

    private void ClearTerminalView()
    {
        if (_session == null) return;

        _session.Buffer.EraseInDisplay(3);
        _session.Buffer.MoveCursorTo(0, 0);
        _scrollOffset = 0;
        _followOutput = true;
        _lastScrollbackCount = _session.Buffer.ScrollbackCount;
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);

        // 在支持的情况下请 Shell 重绘提示符。
        _session.Write("\x0c");
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (HandleMouseWheel(e.Delta, e.GetPosition(this)))
            e.Handled = true;
    }

    public bool HandleMouseWheel(int delta, Point position)
    {
        if (_session == null) return false;

        // 鼠标滚轮上报
        if (IsMouseTrackingActive)
        {
            if (_cols <= 0 || _rows <= 0) return false;

            int col = Math.Clamp((int)(position.X / _cellWidth), 0, _cols - 1);
            int row = Math.Clamp((int)(position.Y / _cellHeight), 0, _rows - 1);
            int button = delta > 0 ? 64 : 65; // 64 = 向上滚动，65 = 向下滚动
            SendMouseReport(button, col, row, true);
            return true;
        }

        // 滚动历史导航
        int lines = delta > 0 ? -3 : 3;
        _scrollOffset = Math.Clamp(_scrollOffset + lines, -_session.Buffer.ScrollbackCount, 0);
        _followOutput = _scrollOffset == 0;
        _lastScrollbackCount = _session.Buffer.ScrollbackCount;
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);
        return true;
    }

    // --- Visual tree ---

    protected override int VisualChildrenCount => 2;
    protected override Visual GetVisualChild(int index) => index == 0 ? _visual : _imeProxy!;

    private void OnImeProxyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ShouldForwardImeProxyKeyDown(e))
            return;

        // 只截获功能键和组合键；普通字符交给 TextInput，否则会被 PreviewKeyDown 吃掉。
        e.Handled = true;

        // 触发 TerminalControl 的 KeyDown 处理
        var args = new KeyEventArgs(e.KeyboardDevice, e.InputSource, e.Timestamp, e.Key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        RaiseEvent(args);
    }

    private static bool ShouldForwardImeProxyKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt))
            return true;

        if (key is Key.Back or Key.Enter or Key.Tab or Key.Escape or Key.Insert or Key.Delete)
            return true;

        if (key >= Key.Left && key <= Key.Down)
            return true;

        if (key >= Key.Home && key <= Key.PageDown)
            return true;

        if (key >= Key.F1 && key <= Key.F24)
            return true;

        return false;
    }

    private void OnImeProxyPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // 阻止 TextBox 的默认处理，让文本直接发送到终端
        e.Handled = true;
        if (_session != null && !string.IsNullOrEmpty(e.Text))
        {
            _session.Write(e.Text);
            TrackInputText(e.Text);
        }
    }

    private void OnImeProxyTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_imeProxy == null || _session == null) return;

        var text = _imeProxy.Text;
        if (!string.IsNullOrEmpty(text))
        {
            _session.Write(text);
            TrackInputText(text);
            _imeProxy.Clear();
        }
    }

    private void UpdateImeProxyPosition()
    {
        if (_imeProxy == null || _session == null) return;
        InvalidateArrange(); // 触发重新布局
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_imeProxy != null && _session != null)
        {
            var buffer = _session.Buffer;
            double x = buffer.CursorCol * _cellWidth;
            double y = buffer.CursorRow * _cellHeight;
            _imeProxy.Arrange(new Rect(x, y, _cellWidth, _cellHeight));
        }
        return base.ArrangeOverride(finalSize);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);

        // 立即将焦点转移到 _imeProxy
        if (_imeProxy != null && !_imeProxy.IsFocused)
        {
            e.Handled = true;
            _imeProxy.Focus();
            return;
        }

        RegisterImeHook();
        UpdateImeCaretPosition();
        QueueImeCaretRefresh();

        // 焦点切换时立即更新 IME 代理位置
        if (_imeProxy != null && _session != null)
        {
            var buffer = _session.Buffer;
            double x = buffer.CursorCol * _cellWidth;
            double y = buffer.CursorRow * _cellHeight;
            _imeProxy.Arrange(new Rect(x, y, _cellWidth, _cellHeight));
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        _imeRefreshTimer?.Stop();
        DestroyNativeCaret();
        base.OnLostKeyboardFocus(e);
    }

    private static bool TryGetCtrlLetterSequence(Key key, out string sequence)
    {
        sequence = "";
        if (key < Key.A || key > Key.Z)
            return false;

        var controlCode = (char)(key - Key.A + 1);
        sequence = controlCode.ToString();
        return true;
    }

    private static string? KeyToVtSequence(Key key, ModifierKeys modifiers, bool appCursor)
    {
        if (appCursor)
        {
            var appSeq = key switch
            {
                Key.Up => "\x1bOA",
                Key.Down => "\x1bOB",
                Key.Right => "\x1bOC",
                Key.Left => "\x1bOD",
                Key.Home => "\x1bOH",
                Key.End => "\x1bOF",
                _ => (string?)null,
            };
            if (appSeq != null) return appSeq;
        }

        return key switch
        {
            Key.Enter => "\r",
            Key.Escape => "\x1b",
            Key.Back => "\x7f",
            Key.Tab => modifiers.HasFlag(ModifierKeys.Shift) ? "\x1b[Z" : "\t",
            Key.Up => "\x1b[A",
            Key.Down => "\x1b[B",
            Key.Right => "\x1b[C",
            Key.Left => "\x1b[D",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            Key.Insert => "\x1b[2~",
            Key.Delete => "\x1b[3~",
            Key.PageUp => "\x1b[5~",
            Key.PageDown => "\x1b[6~",
            Key.F1 => "\x1bOP",
            Key.F2 => "\x1bOQ",
            Key.F3 => "\x1bOR",
            Key.F4 => "\x1bOS",
            Key.F5 => "\x1b[15~",
            Key.F6 => "\x1b[17~",
            Key.F7 => "\x1b[18~",
            Key.F8 => "\x1b[19~",
            Key.F9 => "\x1b[20~",
            Key.F10 => "\x1b[21~",
            Key.F11 => "\x1b[23~",
            Key.F12 => "\x1b[24~",
            _ => null,
        };
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateCaret(IntPtr hwnd, IntPtr hBitmap, int width, int height);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCaretPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowCaret(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HideCaret(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyCaret();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern void NotifyWinEvent(uint eventMin, IntPtr hwnd, int idObject, int idChild);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hwnd);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr himc);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetCompositionWindow(IntPtr himc, ref CompositionForm form);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetCandidateWindow(IntPtr himc, ref CandidateForm form);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmNotifyIME(IntPtr himc, int dwAction, int dwIndex, int dwValue);

    private const int WmImeStartComposition = 0x010D;
    private const int WmImeComposition = 0x010F;
    private const int WmImeNotify = 0x0282;
    private const int WmImeRequest = 0x0288;
    private const int ImnChangeCandidate = 0x0003;
    private const int ImnCloseCandidate = 0x0004;
    private const int ImnOpenCandidate = 0x0005;
    private const int ImrCompositionWindow = 0x0001;
    private const int ImrCandidateWindow = 0x0002;
    private const int ImrQueryCharPosition = 0x0006;
    private const int CfsForcePosition = 0x0020;
    private const int CfsCandidatePos = 0x0040;
    private const int NI_COMPOSITIONSTR = 0x0015;
    private const int CPS_CANCEL = 0x0004;
    private const uint EventObjectLocationChange = 0x800B;
    private const int ObjidCaret = -8;
    private const int ChildidSelf = 0;
    private static readonly IntPtr IntPtrOne = new(1);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect(int left, int top, int right, int bottom)
    {
        public int Left = left;
        public int Top = top;
        public int Right = right;
        public int Bottom = bottom;

        public static NativeRect FromPoints(NativePoint first, NativePoint second) =>
            new(
                Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y),
                Math.Max(first.X, second.X),
                Math.Max(first.Y, second.Y));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm
    {
        public int DwStyle;
        public NativePoint PtCurrentPos;
        public NativeRect RcArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CandidateForm
    {
        public int DwIndex;
        public int DwStyle;
        public NativePoint PtCurrentPos;
        public NativeRect RcArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ImeCharPosition
    {
        public int DwSize;
        public int DwCharPos;
        public NativePoint Pt;
        public int CLineHeight;
        public NativeRect RcDocument;
    }

    private readonly record struct ImeMetrics(
        NativePoint CaretTopLeftClient,
        NativePoint CaretBottomLeftClient,
        NativeRect CaretRectClient,
        NativeRect DocumentRectClient,
        NativePoint CharPositionScreen,
        NativeRect DocumentRectScreen,
        int CellHeight);

    private static void LogIme(string message)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ecode-ime-debug.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    public void UpdateTheme(GhosttyTheme theme)
    {
        _theme = theme;
        _typeface = CreateTerminalTypeface(theme.FontFamily, FontStyles.Normal, FontWeights.Normal);
        _fontSize = theme.FontSize;
        InvalidateRenderCaches();
        CalculateCellSize();
        CalculateTerminalSize();
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);
    }

    public void UpdateSettings(TerminalTheme theme, string fontFamily, int fontSize)
    {
        // 将 TerminalTheme 转换为 GhosttyTheme
        var ghosttyTheme = new GhosttyTheme
        {
            Background = theme.Background,
            Foreground = theme.Foreground,
            Palette = theme.Palette,
            SelectionBackground = theme.SelectionBg,
            CursorColor = theme.CursorColor,
            FontFamily = fontFamily,
            FontSize = fontSize
        };
        UpdateSettings(ghosttyTheme, fontFamily, fontSize);
    }

    public void UpdateSettings(GhosttyTheme theme, string fontFamily, int fontSize)
    {
        _theme = theme;
        _fontSize = fontSize;

        var settings = SettingsService.Current;
        _cursorStyle = settings.CursorStyle;
        _cursorBlink = settings.CursorBlink;

        _typeface = CreateTerminalTypeface(fontFamily, FontStyles.Normal, FontWeights.Normal);
        InvalidateRenderCaches();
        CalculateCellSize();
        CalculateTerminalSize();
        RequestRender(System.Windows.Threading.DispatcherPriority.Render);
    }

    private static void OnHasNotificationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TerminalControl)d).RequestRender(System.Windows.Threading.DispatcherPriority.Render);
    }

    private static void OnIsPaneFocusedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (TerminalControl)d;
        if ((bool)e.NewValue)
        {
            ctrl._cursorVisible = true;
            if (ctrl._cursorBlink)
                ctrl._cursorTimer?.Start();
        }
        ctrl.RequestRender(System.Windows.Threading.DispatcherPriority.Render);
    }
}
