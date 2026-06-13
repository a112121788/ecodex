using ECode.Core.Terminal;
namespace ECode.Core.Models;

/// <summary>
/// Ghostty 终端主题配置，包含颜色、字体及 ANSI 调色板定义。
/// </summary>
public class GhosttyTheme
{
    public TerminalColor Background { get; set; } = new(30, 30, 30);
    public TerminalColor Foreground { get; set; } = new(204, 204, 204);
    public TerminalColor[] Palette { get; set; } = CreateDefaultPalette();
    public TerminalColor? SelectionBackground { get; set; }
    public TerminalColor? SelectionForeground { get; set; }
    public TerminalColor? CursorColor { get; set; }
    public string FontFamily { get; set; } = "Cascadia Mono";
    public double FontSize { get; set; } = 13.0;

    /// <summary>
    /// 默认 16 色 ANSI 调色板（匹配典型的深色终端主题）。
    /// </summary>
    private static TerminalColor[] CreateDefaultPalette()
    {
        return
        [
            // 普通颜色
            new(0x1e, 0x1e, 0x1e), // 0  黑色
            new(0xf4, 0x47, 0x47), // 1  红色
            new(0x4e, 0xc9, 0xb0), // 2  绿色
            new(0xd7, 0xba, 0x7d), // 3  黄色
            new(0x56, 0x9c, 0xd6), // 4  蓝色
            new(0xc5, 0x86, 0xc0), // 5  品红
            new(0x4e, 0xc9, 0xb0), // 6  青色
            new(0xcc, 0xcc, 0xcc), // 7  白色
            // 亮色
            new(0x80, 0x80, 0x80), // 8  亮黑
            new(0xf4, 0x47, 0x47), // 9  亮红
            new(0x4e, 0xc9, 0xb0), // 10 亮绿
            new(0xd7, 0xba, 0x7d), // 11 亮黄
            new(0x56, 0x9c, 0xd6), // 12 亮蓝
            new(0xc5, 0x86, 0xc0), // 13 亮品红
            new(0x4e, 0xc9, 0xb0), // 14 亮青
            new(0xff, 0xff, 0xff), // 15 亮白
        ];
    }
}
