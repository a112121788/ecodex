# 故障排查

本页按常见症状列出检查路径。优先运行：

```powershell
ecode doctor
ecode --json doctor
```

## `ecode doctor` 字段

| 检查项 | 状态 | 说明 |
|---|---|---|
| `conpty` | `ok` / `fail` | Windows 10 1809 / build 17763 或更新版本才支持 ConPTY。 |
| `webview2` | `ok` / `warn` | 浏览器 Surface 需要 WebView2 Runtime。 |
| `path` | `ok` / `warn` | 命令行目录是否在 PATH 中。 |
| `daemon` | `ok` / `warn` | 主应用 pipe 是否可连接。 |
| `config` | `ok` / `warn` | `%USERPROFILE%\.ecode` 是否存在。 |

## 日志位置

| 文件 | 用途 |
|---|---|
| `%USERPROFILE%\.ecode\daemon-debug.log` | App/daemon 连接、session create/attach、pipe、fallback、shutdown。 |
| `%TEMP%\ecode-smoke.log` | ConPTY smoke 测试日志。 |
| `artifacts/perf*/perf-report.md` | 性能预算报告。 |

查看日志：

```powershell
Get-Content "$env:USERPROFILE\.ecode\daemon-debug.log" -Tail 120
```

## `daemon-debug.log` 字段

重点关注：

- `ts=`：时间。
- `component=`：组件，例如 app、daemon、pipe。
- `event=`：事件，例如 `session.create`、`pane.write`。
- `paneId=`：Pane UUID 或短引用上下文。

## 命令行连不上主应用

症状：`Error: Could not connect to ecode. Is it running?`

处理：

1. 启动 `ecode-app.exe`。
2. 运行 `ecode status`。
3. 查看 `daemon-debug.log` 是否有 pipe 错误。
4. 如果只需要本地命令，可使用 `ecode setup status`、`ecode doctor`、`ecode completion powershell`、`ecode version`。

## WebView2 不可用

症状：浏览器 Surface 显示 Runtime 缺失。

处理：

1. 安装 Microsoft Edge WebView2 Runtime。
2. 重启 ECode。
3. 运行 `ecode doctor` 确认 `webview2` 状态。

## PATH / shell profile 问题

先看 dry-run：

```powershell
ecode setup status
ecode setup install --write false
```

确认 diff 后再写入：

```powershell
ecode setup install --write true
```

撤销：

```powershell
ecode setup uninstall --write true
```

## `ecode.json` 不生效

```powershell
ecode config diagnostics
ecode config reload
```

检查：

- 路径是否为 `.ecode/ecode.json` 或 `%USERPROFILE%\.config\ecode\ecode.json`。
- JSON 是否有尾逗号或注释。
- `commands` / `actions` 字段是否符合 [自定义命令](./custom-commands.md)。
- 旧 `.cmux/cmux.json` 是否依赖兼容开关。

## 会话恢复异常

```powershell
ecode surface resume show
ecode restore-session
```

检查：

- `resume.json` 是否存在。
- binding 是否 `trusted: true`。
- `AutoResumeTrustedBindings` 是否启用。
- `daemon-debug.log` 是否出现 `SESSION_CREATE`、`session.create`、`session.created`。

## 浏览器 API 找不到元素

1. 先运行 `ecode browser snapshot --surfaceRef <ref>`。
2. 确认目标 role / text / testid 在 snapshot 中。
3. 再执行 `ecode browser click`、`ecode browser fill`、`ecode browser eval`。
4. 严格 CSP 页面可能限制脚本注入。

## 更新失败

```powershell
ecode update check --feed <feed-url>
ecode update install --feed <feed-url>
```

检查：

- feed 根目录是否包含 `RELEASES`。
- nupkg / setup URL 是否可下载。
- 当前版本是否低于 feed 中最新版本。

## 本地构建失败

推荐命令：

```powershell
npm run docs:build
.\.dotnet\dotnet.exe test tests\ECode.Tests\ECode.Tests.csproj -p:NuGetAudit=false
.\.dotnet\dotnet.exe build ECode.sln -c Debug -p:NuGetAudit=false
```

如果 NuGet audit 因证书或网络失败，本地验证可临时加 `NuGetAudit=false`。CI / release 环境应保持网络与证书链正常。

## 提交 issue 前

请附：

```powershell
ecode version
ecode doctor
Get-Content "$env:USERPROFILE\.ecode\daemon-debug.log" -Tail 200 > daemon-debug-tail.log
```

上传前请脱敏 token、API key、私有路径和项目名称。
