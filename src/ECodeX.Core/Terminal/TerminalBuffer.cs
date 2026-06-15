using System.Globalization;

namespace ECodeX.Core.Terminal;

/// <summary>
/// 管理终端单元格网格、光标状态、回滚缓冲区和滚动区域。
/// 这是 VT 解析器操作的核心数据结构，渲染器从中读取数据。
/// </summary>
public class TerminalBuffer
{
    private TerminalCell[,] _cells;
    private readonly ScrollbackBuffer<TerminalCell[]> _scrollback;
    private readonly int _maxScrollback;

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int CursorRow { get; set; }
    public int CursorCol { get; set; }
    public bool CursorVisible { get; set; } = true;

    // 滚动区域（包含边界，从 0 开始）
    public int ScrollTop { get; private set; }
    public int ScrollBottom { get; private set; }

    // 已保存的光标状态
    private int _savedCursorRow;
    private int _savedCursorCol;
    private TerminalAttribute _savedAttribute;

    // 当前写入属性
    public TerminalAttribute CurrentAttribute { get; set; } = TerminalAttribute.Default;

    // 模式标志
    public bool OriginMode { get; set; }
    public bool AutoWrapMode { get; set; } = true;
    public bool InsertMode { get; set; }
    public bool ApplicationCursorKeys { get; set; }
    public bool BracketedPasteMode { get; set; }
    public bool IsAlternateScreen { get; private set; }

    // 鼠标追踪模式
    public bool MouseTrackingNormal { get; set; }    // 模式 1000：按键事件
    public bool MouseTrackingButton { get; set; }    // 模式 1002：按键 + 按下时的移动
    public bool MouseTrackingAny { get; set; }       // 模式 1003：所有移动
    public bool MouseSgrExtended { get; set; }       // 模式 1006：SGR 扩展坐标
    public bool MouseEnabled => MouseTrackingNormal || MouseTrackingButton || MouseTrackingAny;

    private bool _wrapPending;

    // 备用屏幕缓冲区状态
    private TerminalCell[,]? _savedMainCells;
    private List<TerminalCell[]>? _savedMainScrollbackList;
    private int _savedMainCursorRow;
    private int _savedMainCursorCol;
    private TerminalAttribute _savedMainAttribute;

    public int ScrollbackCount => _scrollback.Count;
    public int TotalLines => Rows + _scrollback.Count;

    public event Action? ContentChanged;

    public TerminalBuffer(int cols, int rows, int maxScrollback = 10_000)
    {
        Cols = Math.Max(1, cols);
        Rows = Math.Max(1, rows);
        _maxScrollback = maxScrollback;
        _scrollback = new ScrollbackBuffer<TerminalCell[]>(maxScrollback);
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
        _cells = new TerminalCell[Rows, Cols];
        Clear();
    }

    public void Clear()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                _cells[r, c] = TerminalCell.Empty;
    }

    public ref TerminalCell CellAt(int row, int col)
    {
        int maxRow = _cells.GetLength(0) - 1;
        int maxCol = _cells.GetLength(1) - 1;
        row = Math.Clamp(row, 0, maxRow);
        col = Math.Clamp(col, 0, maxCol);
        return ref _cells[row, col];
    }

    public TerminalCell[] GetLine(int row)
    {
        int cols = _cells.GetLength(1);
        int safeRow = Math.Clamp(row, 0, _cells.GetLength(0) - 1);
        var line = new TerminalCell[cols];
        for (int c = 0; c < cols; c++)
            line[c] = _cells[safeRow, c];
        return line;
    }

    public TerminalCell[]? GetScrollbackLine(int index)
    {
        if (index < 0 || index >= _scrollback.Count) return null;
        return _scrollback[index];
    }

    public void SetChar(int row, int col, char ch, TerminalAttribute attr)
    {
        if (row < 0 || row >= Rows || col < 0 || col >= Cols) return;
        _cells[row, col] = new TerminalCell
        {
            Character = ch,
            Attribute = attr,
            IsDirty = true,
            Width = 1,
        };
    }

    /// <summary>
    /// 在当前光标位置写入字符并推进光标。
    /// 处理自动换行和插入模式。
    /// </summary>
    public void WriteChar(char c)
    {
        if (!ClampCursorToBounds())
            return;

        int width = IsWideChar(c) ? 2 : 1;

        if (_wrapPending && AutoWrapMode)
        {
            CarriageReturn();
            LineFeed();
            _wrapPending = false;
        }

        if (CursorCol + width > Cols && AutoWrapMode)
        {
            CarriageReturn();
            LineFeed();
        }

        if (InsertMode)
        {
            for (int col = Cols - 1; col >= CursorCol + width; col--)
                _cells[CursorRow, col] = _cells[CursorRow, col - width];
        }

        if (CursorRow >= 0 && CursorRow < Rows && CursorCol >= 0 && CursorCol < Cols)
        {
            _cells[CursorRow, CursorCol] = new TerminalCell
            {
                Character = c,
                Attribute = CurrentAttribute,
                IsDirty = true,
                Width = (byte)width,
            };

            if (width == 2 && CursorCol + 1 < Cols)
            {
                _cells[CursorRow, CursorCol + 1] = new TerminalCell
                {
                    Character = '\0',
                    Attribute = CurrentAttribute,
                    IsDirty = true,
                    Width = 0,
                };
            }
        }

        if (CursorCol + width >= Cols)
        {
            _wrapPending = true;
        }
        else
        {
            CursorCol += width;
        }
    }

    public void WriteString(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            if (element.Length > 0)
                WriteChar(element[0]);
        }
    }
    private static bool IsWideChar(char c)
    {
        int code = c;
        if (code < 0x1100) return false;

        if (code >= 0x2E80 && code <= 0x2FFB) return true;
        if (code >= 0x3000 && code <= 0x303E) return true;
        if (code >= 0x3041 && code <= 0x33FF) return true;
        if (code >= 0x3400 && code <= 0x4DBF) return true;
        if (code >= 0x4E00 && code <= 0x9FFF) return true;
        if (code >= 0xA000 && code <= 0xA4CF) return true;
        if (code >= 0xAC00 && code <= 0xD7A3) return true;
        if (code >= 0xF900 && code <= 0xFAFF) return true;
        if (code >= 0xFE30 && code <= 0xFE4F) return true;
        if (code >= 0xFF00 && code <= 0xFF60) return true;
        if (code >= 0xFFE0 && code <= 0xFFE6) return true;

        return false;
    }

    public void CarriageReturn()
    {
        CursorCol = 0;
        _wrapPending = false;
    }

    public void LineFeed()
    {
        _wrapPending = false;
        if (CursorRow == ScrollBottom)
        {
            ScrollUp(1);
        }
        else if (CursorRow < Rows - 1)
        {
            CursorRow++;
        }
    }

    public void ReverseLineFeed()
    {
        if (CursorRow == ScrollTop)
        {
            ScrollDown(1);
        }
        else if (CursorRow > 0)
        {
            CursorRow--;
        }
    }

    public void NewLine()
    {
        CarriageReturn();
        LineFeed();
    }

    /// <summary>
    /// 将滚动区域向上滚动指定行数。
    /// 滚出顶部的行在滚动区域为全屏时进入回滚缓冲区。
    /// </summary>
    public void ScrollUp(int lines = 1)
    {
        for (int n = 0; n < lines; n++)
        {
            // 若滚动区域从第 0 行开始，推入回滚缓冲区
            if (ScrollTop == 0)
            {
                var scrolledLine = new TerminalCell[Cols];
                for (int c = 0; c < Cols; c++)
                    scrolledLine[c] = _cells[0, c];

                _scrollback.Add(scrolledLine);
            }

            // 在滚动区域内将行上移
            for (int r = ScrollTop; r < ScrollBottom; r++)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r + 1, c];

            // 清除底部行
            for (int c = 0; c < Cols; c++)
                _cells[ScrollBottom, c] = TerminalCell.Empty;
        }

        RaiseContentChanged();
    }

    /// <summary>
    /// 将滚动区域向下滚动指定行数。
    /// </summary>
    public void ScrollDown(int lines = 1)
    {
        for (int n = 0; n < lines; n++)
        {
            for (int r = ScrollBottom; r > ScrollTop; r--)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r - 1, c];

            for (int c = 0; c < Cols; c++)
                _cells[ScrollTop, c] = TerminalCell.Empty;
        }

        RaiseContentChanged();
    }

    /// <summary>
    /// 擦除部分显示内容。
    /// 0 = 光标到末尾，1 = 起始到光标，2 = 全部，3 = 全部 + 回滚缓冲区
    /// </summary>
    public void EraseInDisplay(int mode)
    {
        if (!ClampCursorToBounds())
            return;

        switch (mode)
        {
            case 0: // 光标到末尾
                for (int c = CursorCol; c < Cols; c++)
                    _cells[CursorRow, c] = TerminalCell.Empty;
                for (int r = CursorRow + 1; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                        _cells[r, c] = TerminalCell.Empty;
                break;
            case 1: // 起始到光标
                for (int r = 0; r < CursorRow; r++)
                    for (int c = 0; c < Cols; c++)
                        _cells[r, c] = TerminalCell.Empty;
                for (int c = 0; c <= CursorCol; c++)
                    _cells[CursorRow, c] = TerminalCell.Empty;
                break;
            case 2: // 全部
                Clear();
                break;
            case 3: // 全部 + 回滚缓冲区
                Clear();
                _scrollback.Clear();
                break;
        }

        RaiseContentChanged();
    }

    /// <summary>
    /// 擦除当前行的部分内容。
    /// 0 = 光标到末尾，1 = 起始到光标，2 = 整行
    /// </summary>
    public void EraseInLine(int mode)
    {
        if (!ClampCursorToBounds())
            return;

        switch (mode)
        {
            case 0:
                for (int c = CursorCol; c < Cols; c++)
                    _cells[CursorRow, c] = TerminalCell.Empty;
                break;
            case 1:
                for (int c = 0; c <= CursorCol; c++)
                    _cells[CursorRow, c] = TerminalCell.Empty;
                break;
            case 2:
                for (int c = 0; c < Cols; c++)
                    _cells[CursorRow, c] = TerminalCell.Empty;
                break;
        }

        RaiseContentChanged();
    }

    public void EraseChars(int count)
    {
        if (!ClampCursorToBounds())
            return;

        count = Math.Max(0, count);
        for (int i = 0; i < count && CursorCol + i < Cols; i++)
            _cells[CursorRow, CursorCol + i] = TerminalCell.Empty;
        RaiseContentChanged();
    }

    public void InsertLines(int count)
    {
        if (!ClampCursorToBounds())
            return;

        count = Math.Max(0, count);
        int savedBottom = ScrollBottom;
        ScrollBottom = Rows - 1;
        for (int n = 0; n < count; n++)
        {
            for (int r = ScrollBottom; r > CursorRow; r--)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r - 1, c];
            for (int c = 0; c < Cols; c++)
                _cells[CursorRow, c] = TerminalCell.Empty;
        }
        ScrollBottom = savedBottom;
        RaiseContentChanged();
    }

    public void DeleteLines(int count)
    {
        if (!ClampCursorToBounds())
            return;

        count = Math.Max(0, count);
        for (int n = 0; n < count; n++)
        {
            for (int r = CursorRow; r < ScrollBottom; r++)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r + 1, c];
            for (int c = 0; c < Cols; c++)
                _cells[ScrollBottom, c] = TerminalCell.Empty;
        }
        RaiseContentChanged();
    }

    public void InsertChars(int count)
    {
        if (!ClampCursorToBounds())
            return;

        count = Math.Max(0, count);
        for (int n = 0; n < count; n++)
        {
            for (int c = Cols - 1; c > CursorCol; c--)
                _cells[CursorRow, c] = _cells[CursorRow, c - 1];
            _cells[CursorRow, CursorCol] = TerminalCell.Empty;
        }
        RaiseContentChanged();
    }

    public void DeleteChars(int count)
    {
        if (!ClampCursorToBounds())
            return;

        count = Math.Max(0, count);
        for (int n = 0; n < count; n++)
        {
            for (int c = CursorCol; c < Cols - 1; c++)
                _cells[CursorRow, c] = _cells[CursorRow, c + 1];
            _cells[CursorRow, Cols - 1] = TerminalCell.Empty;
        }
        RaiseContentChanged();
    }

    public void SetScrollRegion(int top, int bottom)
    {
        ScrollTop = Math.Max(0, Math.Min(top, Rows - 1));
        ScrollBottom = Math.Max(0, Math.Min(bottom, Rows - 1));
        if (ScrollTop > ScrollBottom)
            (ScrollTop, ScrollBottom) = (ScrollBottom, ScrollTop);
    }

    public void ResetScrollRegion()
    {
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
    }

    public void SaveCursor()
    {
        _savedCursorRow = CursorRow;
        _savedCursorCol = CursorCol;
        _savedAttribute = CurrentAttribute;
    }

    public void RestoreCursor()
    {
        CursorRow = _savedCursorRow;
        CursorCol = _savedCursorCol;
        CurrentAttribute = _savedAttribute;
    }

    /// <summary>
    /// 切换到备用屏幕缓冲区（DECSET 1049）。
    /// 保存主屏幕单元格、回滚缓冲区、光标和属性。
    /// </summary>
    public void SwitchToAlternateScreen()
    {
        if (IsAlternateScreen) return;

        // 保存主屏幕状态
        _savedMainCells = _cells;
        _savedMainScrollbackList = _scrollback.ToList();
        _savedMainCursorRow = CursorRow;
        _savedMainCursorCol = CursorCol;
        _savedMainAttribute = CurrentAttribute;

        // 创建全新屏幕
        _cells = new TerminalCell[Rows, Cols];
        Clear();
        _scrollback.Clear();

        CursorRow = 0;
        CursorCol = 0;
        CurrentAttribute = TerminalAttribute.Default;
        SetScrollRegion(0, Rows - 1);
        IsAlternateScreen = true;
    }

    /// <summary>
    /// 切换回主屏幕缓冲区（DECRST 1049）。
    /// 恢复已保存的主屏幕状态。
    /// </summary>
    public void SwitchToMainScreen()
    {
        if (!IsAlternateScreen) return;

        // 恢复主屏幕状态
        if (_savedMainCells != null)
        {
            _cells = _savedMainCells;
            _savedMainCells = null;
        }

        _scrollback.Clear();
        if (_savedMainScrollbackList != null)
        {
            _scrollback.AddRange(_savedMainScrollbackList);
            _savedMainScrollbackList = null;
        }

        CursorRow = _savedMainCursorRow;
        CursorCol = _savedMainCursorCol;
        CurrentAttribute = _savedMainAttribute;
        SetScrollRegion(0, Rows - 1);
        IsAlternateScreen = false;

        RaiseContentChanged();
    }

    public void MoveCursorTo(int row, int col)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorCol = Math.Clamp(col, 0, Cols - 1);
        _wrapPending = false;
    }

    public void MoveCursorUp(int count = 1)
    {
        CursorRow = Math.Max(ScrollTop, CursorRow - count);
        _wrapPending = false;
    }

    public void MoveCursorDown(int count = 1)
    {
        CursorRow = Math.Min(ScrollBottom, CursorRow + count);
        _wrapPending = false;
    }

    public void MoveCursorForward(int count = 1)
    {
        CursorCol = Math.Min(Cols - 1, CursorCol + count);
        _wrapPending = false;
    }

    public void MoveCursorBackward(int count = 1)
    {
        CursorCol = Math.Max(0, CursorCol - count);
        _wrapPending = false;
    }

    public void Tab()
    {
        int nextTab = ((CursorCol / 8) + 1) * 8;
        CursorCol = Math.Min(nextTab, Cols - 1);
    }

    public void Backspace()
    {
        if (CursorCol > 0)
            CursorCol--;
        _wrapPending = false;
    }

    /// <summary>
    /// 调整缓冲区大小，尽可能保留内容。
    /// </summary>
    public void Resize(int newCols, int newRows)
    {
        newCols = Math.Max(1, newCols);
        newRows = Math.Max(1, newRows);

        var newCells = new TerminalCell[newRows, newCols];
        for (int r = 0; r < newRows; r++)
            for (int c = 0; c < newCols; c++)
                newCells[r, c] = TerminalCell.Empty;

        int copyRows = Math.Min(Rows, newRows);
        int copyCols = Math.Min(Cols, newCols);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCols; c++)
                newCells[r, c] = _cells[r, c];

        _cells = newCells;
        Cols = newCols;
        Rows = newRows;
        ScrollTop = 0;
        ScrollBottom = newRows - 1;
        CursorRow = Math.Min(CursorRow, newRows - 1);
        CursorCol = Math.Min(CursorCol, newCols - 1);

        RaiseContentChanged();
    }

    public string ExportPlainText(int maxScrollbackLines = 20000)
    {
        var lines = new List<string>();

        int scrollbackStart = Math.Max(0, _scrollback.Count - Math.Max(0, maxScrollbackLines));
        for (int i = scrollbackStart; i < _scrollback.Count; i++)
            lines.Add(LineToText(_scrollback[i], Cols));

        for (int row = 0; row < Rows; row++)
            lines.Add(LineToText(GetLine(row), Cols));

        int lastNonEmpty = lines.FindLastIndex(line => !string.IsNullOrWhiteSpace(line));
        if (lastNonEmpty < 0)
            return string.Empty;

        return string.Join(Environment.NewLine, lines.Take(lastNonEmpty + 1));
    }

    /// <summary>
    /// 创建回滚和可见行的纯文本快照。
    /// 用于在应用重启时恢复终端上下文。
    /// </summary>
    public TerminalBufferSnapshot CreateSnapshot(int maxScrollbackLines = 3000)
    {
        var snapshot = new TerminalBufferSnapshot
        {
            Cols = Cols,
            Rows = Rows,
            CursorRow = CursorRow,
            CursorCol = CursorCol,
        };

        int scrollbackStart = Math.Max(0, _scrollback.Count - Math.Max(0, maxScrollbackLines));
        for (int i = scrollbackStart; i < _scrollback.Count; i++)
            snapshot.ScrollbackLines.Add(LineToText(_scrollback[i], Cols));

        for (int row = 0; row < Rows; row++)
            snapshot.ScreenLines.Add(LineToText(GetLine(row), Cols));

        return snapshot;
    }

    /// <summary>
    /// 恢复之前捕获的纯文本快照。
    /// </summary>
    public void RestoreSnapshot(TerminalBufferSnapshot snapshot)
    {
        if (snapshot == null) return;

        _scrollback.Clear();
        foreach (var line in snapshot.ScrollbackLines)
            _scrollback.Add(TextToLine(line, Cols));

        Clear();

        int rowCount = Math.Min(Rows, snapshot.ScreenLines.Count);
        for (int row = 0; row < rowCount; row++)
        {
            var text = snapshot.ScreenLines[row];
            int colCount = Math.Min(Cols, text.Length);
            for (int col = 0; col < colCount; col++)
            {
                _cells[row, col] = new TerminalCell
                {
                    Character = text[col],
                    Attribute = TerminalAttribute.Default,
                    IsDirty = true,
                    Width = 1,
                };
            }
        }

        CursorRow = Math.Clamp(snapshot.CursorRow, 0, Rows - 1);
        CursorCol = Math.Clamp(snapshot.CursorCol, 0, Cols - 1);
        ResetScrollRegion();
        MarkAllDirty();
        RaiseContentChanged();
    }

    private bool ClampCursorToBounds()
    {
        if (Rows <= 0 || Cols <= 0)
            return false;

        CursorRow = Math.Clamp(CursorRow, 0, Rows - 1);
        CursorCol = Math.Clamp(CursorCol, 0, Cols - 1);
        return true;
    }

    private static string LineToText(TerminalCell[] line, int cols)
    {
        var chars = new char[cols];
        for (int i = 0; i < cols; i++)
        {
            var ch = i < line.Length ? line[i].Character : ' ';
            chars[i] = ch == '\0' ? ' ' : ch;
        }

        return new string(chars).TrimEnd();
    }

    private static TerminalCell[] TextToLine(string? text, int cols)
    {
        var line = new TerminalCell[cols];
        for (int i = 0; i < cols; i++)
            line[i] = TerminalCell.Empty;

        if (string.IsNullOrEmpty(text)) return line;

        int len = Math.Min(cols, text.Length);
        for (int i = 0; i < len; i++)
        {
            line[i] = new TerminalCell
            {
                Character = text[i],
                Attribute = TerminalAttribute.Default,
                IsDirty = true,
                Width = 1,
            };
        }

        return line;
    }

    /// <summary>
    /// 将所有单元格标记为脏（用于全量重绘）。
    /// </summary>
    public void MarkAllDirty()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                _cells[r, c].IsDirty = true;
    }

    /// <summary>
    /// 清除所有单元格的脏标记。
    /// </summary>
    public void ClearDirty()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                _cells[r, c].IsDirty = false;
    }

    private void RaiseContentChanged() => ContentChanged?.Invoke();
}

public class TerminalBufferSnapshot
{
    public int Cols { get; set; }
    public int Rows { get; set; }
    public int CursorRow { get; set; }
    public int CursorCol { get; set; }
    public List<string> ScrollbackLines { get; set; } = [];
    public List<string> ScreenLines { get; set; } = [];
}
