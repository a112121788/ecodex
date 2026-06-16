# 故障排查

本页按常见症状列出检查路径。优先运行：

```powershell
ecodex doctor
ecodex --json doctor
```

## `ecodex doctor` 字段

| 检查项 | 状态 | 说明 |
|---|---|---|
| `conpty` | `ok` / `fail` | Windows 10 1809 / build 17763 或更新版本才支持 ConPTY。 |
| `webview2` | `ok` / `warn` | 集成浏览器需要 WebView2 Runtime。 |
| `path` | `ok` / `warn` | 命令行目录是否在 PATH 中。 |
| `daemon` | `ok` / `warn` | 主应用 pipe 是否可连接。 |
| `config` | `ok` / `warn` | `%USERPROFILE%\.ecodex` 是否存在。 |

## 日志位置

| 文件 | 用途 |
|---|---|
| `%USERPROFILE%\.ecodex\daemon-debug.log` | App/daemon 连接、session create/attach、pipe、fallback、shutdown。 |
| `%TEMP%\ecodex-smoke.log` | ConPTY smoke 测试日志。 |
| `artifacts/perf*/perf-report.md` | 性能预算报告。 |

查看日志：

```powershell
Get-Content "$env:USERPROFILE\.ecodex\daemon-debug.log" -Tail 120
```

## 安装后启动出现 `VerifyAccess` 严重错误

症状：Inno Setup 安装后启动主程序弹出 `调用线程无法访问此对象，因为另一个线程拥有该对象`，堆栈包含 `TerminalControl.OnRedraw`、`UpdateImeProxyPosition`、`TerminalSession.ReadLoop`。

处理：

1. 升级到包含 WPF Dispatcher 调度修复的版本。
2. 如果仍复现，确认是否使用旧安装目录中的 `ecodex-app.exe`，并重新安装最新包。
3. 附上弹窗堆栈、`ecodex version`、`ecodex doctor` 和 `daemon-debug.log` 尾部日志提交 issue。

## `daemon-debug.log` 字段

重点关注：

- `ts=`：时间。
- `component=`：组件，例如 app、daemon、pipe。
- `event=`：事件，例如 `session.create`、`pane.write`。
- `paneId=`：Pane UUID 或短引用上下文。

## 命令行连不上主应用

症状：`Error: Could not connect to ecodex. Is it running?`

处理：

1. 启动 `ecodex-app.exe`。
2. 运行 `ecodex status`。
3. 查看 `daemon-debug.log` 是否有 pipe 错误。
4. 如果只需要本地命令，可使用 `ecodex setup status`、`ecodex doctor`、`ecodex completion powershell`、`ecodex version`。

## 托盘退出没有按预期处理终端

症状：点击托盘菜单“退出并保留终端”或“退出并终止终端”后，后台 shell / Codex 进程状态与预期不一致。

处理：

1. 查看 `%USERPROFILE%\.ecodex\daemon-debug.log`，搜索 `[Tray] Exit and terminate`。
2. 若选择“退出并终止终端”但日志显示 terminated 小于 requested，说明 daemon 会话逐个关闭时有失败项。
3. 需要保留当前终端时，优先用“退出并保留终端”；关闭按钮和最小化只隐藏到托盘，不会触发终端终止。
4. 重开 ECodex 后，使用 `ecodex status` 或 `ecodex pane list` 确认可见窗口与 pane 状态。

## WebView2 不可用

症状：集成浏览器显示 Runtime 缺失。

处理：

1. 安装 Microsoft Edge WebView2 Runtime。
2. 重启 ECodex。
3. 运行 `ecodex doctor` 确认 `webview2` 状态。

## PATH / shell profile 问题

先看 dry-run：

```powershell
ecodex setup status
ecodex setup install --write false
```

确认 diff 后再写入：

```powershell
ecodex setup install --write true
```

撤销：

```powershell
ecodex setup uninstall --write true
```

如果 `setup status` 显示 `PowerShell hook: conflict`，说明 profile 中只有一半 ECodex shell integration 标记，或存在多个同名标记块。ECodex 会跳过默认写入，避免覆盖用户内容；请先手动备份 `$PROFILE`，保留一个完整的 `# >>> ecodex shell integration >>>` / `# <<< ecodex shell integration <<<` 区块后再重试。

## `ecodex.json` 不生效

```powershell
ecodex config diagnostics
ecodex config reload
```

检查：

- 路径是否为 `.ecodex/ecodex.json` 或 `%USERPROFILE%\.config\ecodex\ecodex.json`。
- JSON 是否有尾逗号或注释。
- `commands` / `actions` 字段是否符合 [自定义命令](./custom-commands.md)。
- 旧 `.cmux/cmux.json` 是否依赖兼容开关。

## 会话恢复异常

如果恢复的 shell 命令或工作目录已失效，ECodex 会在对应终端面板显示 `Failed to start terminal`，但不应阻断主窗口启动。

```powershell
ecodex surface resume show
ecodex restore-session
```

检查：

- `resume.json` 是否存在。
- binding 是否 `trusted: true`。
- `AutoResumeTrustedBindings` 是否启用。
- `settings.json` 中的默认 shell 或恢复快照里的 shell 路径是否仍存在。
- 恢复快照里的工作目录是否仍存在或可访问。
- `daemon-debug.log` 是否出现 `SESSION_CREATE`、`session.create`、`session.created`。

## 浏览器 API 找不到元素

1. 先运行 `ecodex browser snapshot --surfaceRef <ref>`。
2. 确认目标 role / text / testid 在 snapshot 中。
3. 再执行 `ecodex browser click`、`ecodex browser fill`、`ecodex browser eval`。
4. 严格 CSP 页面可能限制脚本注入。

## 更新失败

```powershell
ecodex update check --feed <feed-url>
ecodex update install --feed <feed-url>
```

检查：

- feed 根目录是否包含 `RELEASES`。
- nupkg / setup URL 是否可下载。
- 当前版本是否低于 feed 中最新版本。

## 本地构建失败

推荐命令：

```powershell
npm run docs:build
.\.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj -p:NuGetAudit=false
.\.dotnet\dotnet.exe build ECodex.sln -c Debug -p:NuGetAudit=false
```

如果 NuGet audit 因证书或网络失败，本地验证可临时加 `NuGetAudit=false`。CI / release 环境应保持网络与证书链正常。

## 提交 issue 前

请附：

```powershell
ecodex version
ecodex doctor
Get-Content "$env:USERPROFILE\.ecodex\daemon-debug.log" -Tail 200 > daemon-debug-tail.log
```

上传前请脱敏 token、API key、私有路径和项目名称。
