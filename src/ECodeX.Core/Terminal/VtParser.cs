using System.Text;

namespace ECodeX.Core.Terminal;

/// <summary>
/// 状态机式 VT100/xterm 解析器。处理字节流并为可打印字符、
/// C0 控制符、CSI 序列、ESC 序列和 OSC 字符串分派事件。
///
/// 基于 Paul Flo Williams 的 VT 解析器状态机：
/// https://vt100.net/emu/dec_ansi_parser
/// </summary>
public class VtParser
{
    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        CsiIgnore,
        OscString,
        OscStringEscape,
        DcsEntry,
        DcsParam,
        DcsIntermediate,
        DcsPassthrough,
        DcsIgnore,
        SosPmApc,
    }

    private State _state = State.Ground;
    private readonly StringBuilder _params = new();
    private readonly StringBuilder _intermediates = new();
    private readonly StringBuilder _oscString = new();
    private readonly List<int> _csiParams = [];
    private byte _collectChar;

    // UTF-8 解码器状态
    private int _utf8Remaining;
    private int _utf8Codepoint;

    // 回调
    public Action<char>? OnPrint { get; set; }
    public Action<byte>? OnExecute { get; set; }
    public Action<List<int>, char, string>? OnCsiDispatch { get; set; }
    public Action<byte>? OnEscDispatch { get; set; }
    public Action<string>? OnOscDispatch { get; set; }

    /// <summary>
    /// 将原始字节送入解析器。
    /// </summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            ProcessByte(b);
    }

    /// <summary>
    /// 将字符串送入解析器（UTF-16 文本的便捷方法）。
    /// </summary>
    public void Feed(string text)
    {
        Feed(Encoding.UTF8.GetBytes(text));
    }

    private void ProcessByte(byte b)
    {
        // 处理 UTF-8 续字节
        if (_utf8Remaining > 0)
        {
            if ((b & 0xC0) == 0x80)
            {
                _utf8Codepoint = (_utf8Codepoint << 6) | (b & 0x3F);
                _utf8Remaining--;
                if (_utf8Remaining == 0)
                {
                    char c = (char)_utf8Codepoint;
                    HandlePrint(c);
                }
                return;
            }
            // 无效续字节 — 重置并重新处理
            _utf8Remaining = 0;
        }

        // 多字节 UTF-8 起始
        if (_state == State.Ground && b >= 0xC0 && b <= 0xF7)
        {
            if (b < 0xE0)
            {
                _utf8Remaining = 1;
                _utf8Codepoint = b & 0x1F;
            }
            else if (b < 0xF0)
            {
                _utf8Remaining = 2;
                _utf8Codepoint = b & 0x0F;
            }
            else
            {
                _utf8Remaining = 3;
                _utf8Codepoint = b & 0x07;
            }
            return;
        }

        // 任意状态转换（在任意状态下生效）
        switch (b)
        {
            case 0x1B: // ESC（转义）
                if (_state == State.OscString)
                {
                    _state = State.OscStringEscape;
                    return;
                }

                _state = State.Escape;
                _intermediates.Clear();
                _params.Clear();
                _collectChar = 0;
                return;
            case 0x18 or 0x1A: // CAN、SUB — 取消当前序列
                _state = State.Ground;
                return;
        }

        switch (_state)
        {
            case State.Ground:
                ProcessGround(b);
                break;
            case State.Escape:
                ProcessEscape(b);
                break;
            case State.EscapeIntermediate:
                ProcessEscapeIntermediate(b);
                break;
            case State.CsiEntry:
                ProcessCsiEntry(b);
                break;
            case State.CsiParam:
                ProcessCsiParam(b);
                break;
            case State.CsiIntermediate:
                ProcessCsiIntermediate(b);
                break;
            case State.CsiIgnore:
                ProcessCsiIgnore(b);
                break;
            case State.OscString:
                ProcessOscString(b);
                break;
            case State.OscStringEscape:
                ProcessOscStringEscape(b);
                break;
            case State.DcsEntry:
            case State.DcsParam:
            case State.DcsIntermediate:
            case State.DcsPassthrough:
            case State.DcsIgnore:
                ProcessDcs(b);
                break;
            case State.SosPmApc:
                ProcessSosPmApc(b);
                break;
        }
    }

    private void ProcessGround(byte b)
    {
        if (b < 0x20)
        {
            OnExecute?.Invoke(b);
        }
        else if (b == 0x7F)
        {
            // DEL — 在 Ground 状态中忽略
        }
        else
        {
            HandlePrint((char)b);
        }
    }

    private void HandlePrint(char c)
    {
        if (_state == State.Ground || _state == State.Escape)
        {
            if (_state == State.Escape)
                _state = State.Ground;
            OnPrint?.Invoke(c);
        }
    }

    private void ProcessEscape(byte b)
    {
        if (b == (byte)'[')
        {
            _state = State.CsiEntry;
            _params.Clear();
            _intermediates.Clear();
            _csiParams.Clear();
            return;
        }

        if (b == (byte)']')
        {
            _state = State.OscString;
            _oscString.Clear();
            return;
        }

        if (b == (byte)'P')
        {
            _state = State.DcsEntry;
            return;
        }

        if (b is (byte)'X' or (byte)'^' or (byte)'_')
        {
            _state = State.SosPmApc;
            return;
        }

        if (b is >= 0x20 and <= 0x2F) // Intermediate
        {
            _intermediates.Append((char)b);
            _state = State.EscapeIntermediate;
            return;
        }

        if (b is >= 0x30 and <= 0x7E) // 终结字节
        {
            OnEscDispatch?.Invoke(b);
            _state = State.Ground;
            return;
        }

        // 忽略其他内容并返回 Ground 状态
        _state = State.Ground;
    }

    private void ProcessEscapeIntermediate(byte b)
    {
        if (b is >= 0x20 and <= 0x2F)
        {
            _intermediates.Append((char)b);
            return;
        }

        if (b is >= 0x30 and <= 0x7E)
        {
            OnEscDispatch?.Invoke(b);
            _state = State.Ground;
            return;
        }

        _state = State.Ground;
    }

    private void ProcessCsiEntry(byte b)
    {
        if (b is >= 0x30 and <= 0x39 or (byte)';') // 参数
        {
            _params.Append((char)b);
            _state = State.CsiParam;
            return;
        }

        if (b is (byte)'?' or (byte)'>' or (byte)'!' or (byte)'=') // 私有修饰符
        {
            _collectChar = b;
            _state = State.CsiParam;
            return;
        }

        if (b is >= 0x20 and <= 0x2F) // Intermediate
        {
            _intermediates.Append((char)b);
            _state = State.CsiIntermediate;
            return;
        }

        if (b is >= 0x40 and <= 0x7E) // 终结字节 — 立即分派
        {
            ParseCsiParams();
            DispatchCsi(b);
            return;
        }

        if (b < 0x20) // CSI 中的 C0 控制符
        {
            OnExecute?.Invoke(b);
            return;
        }

        _state = State.CsiIgnore;
    }

    private void ProcessCsiParam(byte b)
    {
        if (b is >= 0x30 and <= 0x39 or (byte)';' or (byte)':')
        {
            _params.Append((char)b);
            return;
        }

        if (b is >= 0x20 and <= 0x2F)
        {
            _intermediates.Append((char)b);
            _state = State.CsiIntermediate;
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            ParseCsiParams();
            DispatchCsi(b);
            return;
        }

        if (b < 0x20)
        {
            OnExecute?.Invoke(b);
            return;
        }

        _state = State.CsiIgnore;
    }

    private void ProcessCsiIntermediate(byte b)
    {
        if (b is >= 0x20 and <= 0x2F)
        {
            _intermediates.Append((char)b);
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            ParseCsiParams();
            DispatchCsi(b);
            return;
        }

        _state = State.CsiIgnore;
    }

    private void ProcessCsiIgnore(byte b)
    {
        if (b is >= 0x40 and <= 0x7E)
            _state = State.Ground;
    }

    private void ProcessOscString(byte b)
    {
        if (b == 0x07) // BEL 终止 OSC
        {
            OnOscDispatch?.Invoke(_oscString.ToString());
            _state = State.Ground;
            return;
        }

        if (b == 0x9C) // ST（8 位）
        {
            OnOscDispatch?.Invoke(_oscString.ToString());
            _state = State.Ground;
            return;
        }

        if (b == 0x1B) // 可能的 ST（ESC \）
        {
            _state = State.OscStringEscape;
            return;
        }

        if (b >= 0x20 || b == 0x09) // 可打印字符或制表符
        {
            _oscString.Append((char)b);
        }
    }

    private void ProcessOscStringEscape(byte b)
    {
        if (b == (byte)'\\')
        {
            OnOscDispatch?.Invoke(_oscString.ToString());
            _state = State.Ground;
            return;
        }

        _state = State.Escape;
        ProcessEscape(b);
    }

    private void ProcessDcs(byte b)
    {
        // 简化的 DCS 处理 — 直接消耗直到 ST
        if (b == 0x9C || b == 0x1B)
            _state = b == 0x1B ? State.Escape : State.Ground;
    }

    private void ProcessSosPmApc(byte b)
    {
        // 消耗直到 ST
        if (b == 0x9C || b == 0x1B)
            _state = b == 0x1B ? State.Escape : State.Ground;
    }

    private void ParseCsiParams()
    {
        _csiParams.Clear();
        if (_params.Length == 0) return;

        var paramStr = _params.ToString();
        foreach (var part in paramStr.Split(';'))
        {
            if (int.TryParse(part, out int val))
                _csiParams.Add(val);
            else
                _csiParams.Add(0);
        }
    }

    private void DispatchCsi(byte finalByte)
    {
        char prefix = _collectChar != 0 ? (char)_collectChar : '\0';
        string intermediates = _intermediates.ToString();

        // 为分派构建限定符字符串
        string qualifier = "";
        if (prefix != '\0') qualifier += prefix;
        qualifier += intermediates;

        OnCsiDispatch?.Invoke(_csiParams, (char)finalByte, qualifier);
        _state = State.Ground;
    }

    /// <summary>
    /// 将解析器重置为初始状态。
    /// </summary>
    public void Reset()
    {
        _state = State.Ground;
        _params.Clear();
        _intermediates.Clear();
        _oscString.Clear();
        _csiParams.Clear();
        _collectChar = 0;
        _utf8Remaining = 0;
        _utf8Codepoint = 0;
    }
}
