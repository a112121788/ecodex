namespace ECodeX.Core.Terminal;

/// <summary>
/// 处理 OSC（操作系统命令）终端序列。
/// 检测常见终端通知序列（OSC 9、99、777），用于长任务和自动化脚本提醒。
/// </summary>
public class OscHandler
{
    public event Action<string>? TitleChanged;
    public event Action<string>? WorkingDirectoryChanged;
    public event Action<string, string?, string>? NotificationReceived; // 标题、副标题、正文
    public event Action<char, string?>? ShellPromptMarker;

    /// <summary>
    /// 处理 OSC 字符串（不含 ESC ] 前缀和 BEL/ST 终止符）。
    /// </summary>
    public void Handle(string oscString)
    {
        if (string.IsNullOrEmpty(oscString)) return;

        // 按第一个 ';' 分割以获取 OSC 代码
        int semicolonIndex = oscString.IndexOf(';');
        string codeStr;
        string payload;

        if (semicolonIndex >= 0)
        {
            codeStr = oscString[..semicolonIndex];
            payload = oscString[(semicolonIndex + 1)..];
        }
        else
        {
            codeStr = oscString;
            payload = "";
        }

        if (!int.TryParse(codeStr, out int code))
            return;

        switch (code)
        {
            case 0: // 设置图标名称和窗口标题
            case 2: // 设置窗口标题
                TitleChanged?.Invoke(payload);
                break;

            case 7: // 设置工作目录（file://host/path）
                HandleWorkingDirectory(payload);
                break;

            case 9: // 终端通知（正文文本）
                // OSC 9 ; <body> ST
                // 许多终端模拟器用它发送简单通知
                NotificationReceived?.Invoke("Terminal", null, payload);
                break;

            case 99: // 扩展通知（key=value 键值对）
                HandleOsc99(payload);
                break;

            case 777: // 自定义通知（notify;title;body）
                HandleOsc777(payload);
                break;

            case 133: // Shell 集成（提示符标记）
                if (payload.Length > 0)
                {
                    var marker = payload[0];
                    string? markerPayload = null;

                    if (payload.Length > 1)
                    {
                        markerPayload = payload[1] == ';'
                            ? payload[2..]
                            : payload[1..];
                    }

                    ShellPromptMarker?.Invoke(marker, string.IsNullOrWhiteSpace(markerPayload) ? null : markerPayload);
                }
                break;
        }
    }

    private void HandleWorkingDirectory(string payload)
    {
        // 格式：file://hostname/path 或纯路径
        if (payload.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(payload);
                var path = uri.LocalPath;
                if (!string.IsNullOrEmpty(path))
                    WorkingDirectoryChanged?.Invoke(path);
            }
            catch (UriFormatException)
            {
                // 尝试按纯路径处理
                var path = payload["file://".Length..];
                int slashIndex = path.IndexOf('/');
                if (slashIndex >= 0)
                {
                    path = path[slashIndex..];
                    WorkingDirectoryChanged?.Invoke(path);
                }
            }
        }
        else if (!string.IsNullOrEmpty(payload))
        {
            WorkingDirectoryChanged?.Invoke(payload);
        }
    }

    /// <summary>
    /// OSC 99：扩展通知格式。
    /// 格式：key=value;key=value
    /// 键：i（id）、d（图标）、t（标题）、b（正文）、p（进度）
    /// 部分实现使用：OSC 99 ; title ; body
    /// </summary>
    private void HandleOsc99(string payload)
    {
        // 先尝试 key=value 格式
        if (payload.Contains('='))
        {
            string? title = null;
            string? body = null;
            string? subtitle = null;

            foreach (var pair in payload.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq < 0) continue;
                var key = pair[..eq].Trim();
                var value = pair[(eq + 1)..].Trim();

                switch (key)
                {
                    case "t": title = value; break;
                    case "b": body = value; break;
                    case "s": subtitle = value; break;
                }
            }

            if (title != null || body != null)
            {
                NotificationReceived?.Invoke(
                    title ?? "Terminal",
                    subtitle,
                    body ?? title ?? "");
            }
        }
        else
        {
            // 更简单的格式：OSC 99 ; body
            NotificationReceived?.Invoke("Terminal", null, payload);
        }
    }

    /// <summary>
    /// OSC 777：notify;title;body 格式。
    /// 由 rxvt-unicode 使用，并被其他终端采纳。
    /// </summary>
    private void HandleOsc777(string payload)
    {
        var parts = payload.Split(';', 3);
        if (parts.Length >= 1 && parts[0] == "notify")
        {
            string title = parts.Length >= 2 ? parts[1] : "Terminal";
            string body = parts.Length >= 3 ? parts[2] : "";
            NotificationReceived?.Invoke(title, null, body);
        }
        else
        {
            NotificationReceived?.Invoke("Terminal", null, payload);
        }
    }
}
