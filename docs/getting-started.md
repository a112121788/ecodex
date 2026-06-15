# 快速上手

本页用 15 分钟带你完成 ECodeX 的第一次使用。ECodeX 是 Windows 原生 SuperTerminal，核心概念是 Workspace、Surface 和 Pane。

## 1. 启动前检查

安装后先运行：

```powershell
ecodex version
ecodex doctor
```

`doctor` 会检查 ConPTY、WebView2、PATH、daemon 与运行时配置目录。如果看到 PATH 或 WebView2 警告，请先按 [故障排查](./troubleshooting.md) 处理。

## 2. 创建 Workspace

Workspace 表示一个项目或工作上下文。你可以从 UI 创建，也可以用命令行：

```powershell
ecodex workspace create --name demo
ecodex workspace list
```

常见操作：

```powershell
ecodex workspace select workspace:1
ecodex workspace rename workspace:1 demo-app
ecodex workspace reorder workspace:2 0
```

## 3. 使用 Surface 与 Pane

Surface 是一个标签页；Pane 是标签页里的终端或浏览器分屏。创建分屏：

```powershell
ecodex pane split right
ecodex pane split down
ecodex pane list
```

也可以使用兼容命令：

```powershell
ecodex split right
ecodex split down
```

Pane 支持短引用，例如 `pane:1`、`surface:2`。human 输出默认展示短引用，JSON 输出默认同时带 refs 与 UUID。

## 4. 打开集成浏览器

集成浏览器使用 WebView2，可与终端并排：

```powershell
ecodex browser new https://example.com
ecodex browser open-split https://example.com --direction right
```

检查 浏览器 API：

```powershell
ecodex browser snapshot
ecodex browser eval "document.title"
```

更多命令见 [浏览器 API](./browser-api.md)。

## 5. 尝试通知与跳转

ECodeX 会记录终端通知并在 sidebar / tab 上显示未读状态。命令行示例：

```powershell
ecodex notify --title Build --body "Tests finished"
ecodex notification list
ecodex notification jump-latest
```

快捷键 `Ctrl+Shift+U` 会跳到最新未读通知。

## 6. 配置自定义命令

项目内创建 `.ecodex/ecodex.json`：

```json
{
  "commands": [
    { "name": "Run Tests", "command": "dotnet test" }
  ],
  "actions": {
    "codex": { "type": "command", "title": "Codex", "command": "codex" }
  }
}
```

重载配置：

```powershell
ecodex config reload
ecodex config diagnostics
```

详情见 [自定义命令](./custom-commands.md)。

## 7. 会话恢复

ECodeX 会保存布局、cwd、终端快照、浏览器 URL / history。手动恢复入口：

```powershell
ecodex restore-session
ecodex surface resume set --pane pane:1 --kind tmux --command "tmux attach -t demo" --trusted false
ecodex surface resume show
```

未信任的 resume binding 不会自动执行；用户确认后才会恢复。详见 [会话恢复](./session-restore.md)。

## 8. 下一步

- 阅读 [命令行](./cli.md) 熟悉 `ecodex.v2` 命令组。
- 阅读 [安装](./installation.md) 选择 zip、Velopack、Inno Setup 或 MSIX。
- 阅读 [故障排查](./troubleshooting.md) 处理启动、WebView2、PATH、daemon 或 update 问题。
