using ECodeX.Core.Models;
using ECodeX.Core.Terminal;

namespace ECodeX.Core.Config;

/// <summary>
/// 读取 Ghostty 配置文件中的主题、字体和颜色信息。
/// Ghostty 配置格式：key = value，每行一项，# 为注释。
/// 路径：%USERPROFILE%\.config\ghostty\config（主路径），
/// %APPDATA%\ghostty\config（备用路径）
/// </summary>
public class GhosttyConfigReader
{
    private static readonly string[] ConfigPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "ghostty", "config"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ghostty", "config"),
    ];

    /// <summary>
    /// 读取 Ghostty 配置并返回主题。
    /// 若找不到配置文件则回退到默认值。
    /// </summary>
    public static GhosttyTheme ReadConfig()
    {
        var theme = new GhosttyTheme();

        foreach (var path in ConfigPaths)
        {
            if (!File.Exists(path)) continue;

            try
            {
                ParseConfigFile(path, theme);
                return theme;
            }
            catch
            {
                // 继续尝试下一个路径
            }
        }

        return theme;
    }

    private static void ParseConfigFile(string path, GhosttyTheme theme)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex < 0) continue;

            var key = line[..equalsIndex].Trim();
            var value = line[(equalsIndex + 1)..].Trim();

            ApplyConfigValue(key, value, theme);
        }
    }

    private static void ApplyConfigValue(string key, string value, GhosttyTheme theme)
    {
        switch (key)
        {
            case "background":
                if (TryParseColor(value, out var bg))
                    theme.Background = bg;
                break;

            case "foreground":
                if (TryParseColor(value, out var fg))
                    theme.Foreground = fg;
                break;

            case "selection-background":
                if (TryParseColor(value, out var selBg))
                    theme.SelectionBackground = selBg;
                break;

            case "selection-foreground":
                if (TryParseColor(value, out var selFg))
                    theme.SelectionForeground = selFg;
                break;

            case "cursor-color":
                if (TryParseColor(value, out var cursor))
                    theme.CursorColor = cursor;
                break;

            case "font-family":
                if (!string.IsNullOrWhiteSpace(value))
                    theme.FontFamily = value.Trim('"', '\'');
                break;

            case "font-size":
                if (double.TryParse(value, out double fontSize) && fontSize > 0)
                    theme.FontSize = fontSize;
                break;

            default:
                // 处理调色板颜色：palette = N=#RRGGBB 或 palette = N=name
                if (key == "palette")
                {
                    var parts = value.Split('=', 2);
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out int index) &&
                        index >= 0 && index < 16 &&
                        TryParseColor(parts[1], out var paletteColor))
                    {
                        theme.Palette[index] = paletteColor;
                    }
                }
                break;
        }
    }

    /// <summary>
    /// 解析颜色字符串。支持：
    /// - #RGB、#RRGGBB
    /// - rgb(r, g, b)
    /// - 命名颜色（基础集合）
    /// </summary>
    private static bool TryParseColor(string value, out TerminalColor color)
    {
        color = TerminalColor.Default;
        value = value.Trim();

        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 3)
            {
                byte r = Convert.ToByte(new string(hex[0], 2), 16);
                byte g = Convert.ToByte(new string(hex[1], 2), 16);
                byte b = Convert.ToByte(new string(hex[2], 2), 16);
                color = new TerminalColor(r, g, b);
                return true;
            }
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex[..2], 16);
                byte g = Convert.ToByte(hex[2..4], 16);
                byte b = Convert.ToByte(hex[4..6], 16);
                color = new TerminalColor(r, g, b);
                return true;
            }
        }

        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
        {
            var inner = value[4..^1];
            var parts = inner.Split(',');
            if (parts.Length == 3 &&
                byte.TryParse(parts[0].Trim(), out byte r) &&
                byte.TryParse(parts[1].Trim(), out byte g) &&
                byte.TryParse(parts[2].Trim(), out byte b))
            {
                color = new TerminalColor(r, g, b);
                return true;
            }
        }

        // 命名颜色
        return value.ToLowerInvariant() switch
        {
            "black" => SetColor(out color, 0x00, 0x00, 0x00),
            "red" => SetColor(out color, 0xCC, 0x00, 0x00),
            "green" => SetColor(out color, 0x00, 0xCC, 0x00),
            "yellow" => SetColor(out color, 0xCC, 0xCC, 0x00),
            "blue" => SetColor(out color, 0x00, 0x00, 0xCC),
            "magenta" => SetColor(out color, 0xCC, 0x00, 0xCC),
            "cyan" => SetColor(out color, 0x00, 0xCC, 0xCC),
            "white" => SetColor(out color, 0xCC, 0xCC, 0xCC),
            _ => false,
        };
    }

    private static bool SetColor(out TerminalColor color, byte r, byte g, byte b)
    {
        color = new TerminalColor(r, g, b);
        return true;
    }
}
