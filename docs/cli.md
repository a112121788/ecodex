# 命令行

`ecode` 是 ECode 的控制入口：既能运行本地诊断与安装集成命令，也能连接主应用 pipe，驱动 Workspace、Surface、Pane、通知、浏览器 Surface 和会话恢复。

## 先用这几条

| 场景 | 命令 |
| --- | --- |
| 检查本机环境、WebView2、PATH、daemon | `ecode doctor` |
| 查看主应用状态 | `ecode status` |
| 机器可读输出，适合脚本 | `ecode --json status` |
| 管理项目命令配置 | `ecode config reload` / `ecode config diagnostics` |
| 打开浏览器 Surface | `ecode browser open https://example.com` |
| 安装 shell 集成 | `ecode setup install --write true` |

## 命令模型

ECode 命令行分成三类，排障时先确认你正在使用哪一种：

| 类型 | 是否需要主应用运行 | 代表命令 | 说明 |
| --- | --- | --- | --- |
| 本地命令 | 否 | `ecode doctor`、`ecode setup status`、`ecode completion powershell` | 只读取本地环境或生成脚本。 |
| v1 兼容 | 是 | `ecode status`、`ecode split right`、`ecode reload-config` | 发送旧式 pipe 命令，适合快捷操作。 |
| `ecode.v2` | 是 | `ecode workspace list`、`ecode pane write`、`ecode notification list` | 使用结构化 request/response，适合自动化。 |

> 看到 `Error: Could not connect to ecode. Is it running?` 时，先启动 ECode 主应用；本地命令不受影响。

## 全局参数

```powershell
ecode --json status
ecode --id-format refs status
ecode --id-format uuids status
ecode --id-format both status
```

| 参数 | 说明 |
| --- | --- |
| `--json` | 原样输出 JSON，方便 `ConvertFrom-Json` 或 CI 脚本处理。 |
| `--id-format refs\|uuids\|both` | 控制输出中的短引用与 UUID。human 默认 `refs`，JSON 默认 `both`。 |
| `--help` / `-h` | 显示帮助。 |
| `--version` / `-v` | 显示版本。 |

## 本地命令

这些命令不需要主应用 pipe：

```powershell
ecode version
ecode help
ecode doctor
ecode --json doctor
ecode setup status
ecode profile import --dry-run
ecode completion powershell
```

常见用途：

- `doctor`：诊断 ConPTY、WebView2 Runtime、PATH、daemon、配置目录和主应用健康状态。
- `setup status`：查看 PowerShell profile、cmd AutoRun、PATH 等集成是否已安装。
- `completion powershell`：输出 PowerShell 补全脚本。

## v1 兼容

兼容命令会发送旧式 pipe 命令，适合手动触发高频动作：

| 命令 | 说明 |
| --- | --- |
| `ecode status` | 查看主应用状态。 |
| `ecode notify --title T --body B` | 发送通知。 |
| `ecode split right\|down` | 对当前 Pane 分屏。 |
| `ecode reload-config` | 重载 `ecode.json`。 |
| `ecode restore-session` | 刷新恢复绑定并定位可恢复 Pane。 |

## `ecode.v2` 命令组

### Window

```powershell
ecode window list
ecode window current
ecode window focus window:1
ecode window create "Scratch"
ecode window close window:1
```

对应方法：`window.list`、`window.current`、`window.focus`、`window.create`、`window.close`。

### Workspace

```powershell
ecode workspace list
ecode workspace create --name demo
ecode workspace select workspace:1
ecode workspace rename workspace:1 demo-app
ecode workspace reorder "workspace:2,workspace:1"
ecode workspace close workspace:1
```

对应方法：`workspace.list`、`workspace.create`、`workspace.select`、`workspace.close`、`workspace.rename`、`workspace.reorder`。

### Surface

```powershell
ecode surface create
ecode surface move surface:1 0
ecode surface reorder "surface:2,surface:1"
ecode surface next
ecode surface previous
ecode surface resume set --pane pane:1 --kind tmux --command "tmux attach -t demo"
ecode surface resume show
ecode surface resume clear --pane pane:1
```

对应方法：`surface.move`、`surface.reorder`，以及 `surface resume` 兼容命令。

### Pane

```powershell
ecode pane list
ecode pane focus pane:1
ecode pane write pane:1 "npm test"
ecode pane read pane:1
ecode pane split right
ecode pane resize pane:1 0.05
ecode pane swap pane:1 pane:2
ecode pane zoom pane:1
ecode pane close pane:1
```

对应方法：`pane.list`、`pane.focus`、`pane.write`、`pane.read`、`pane.split`、`pane.close`、`pane.resize`、`pane.swap`、`pane.zoom`。

### Notification

```powershell
ecode notification list
ecode notification list --unread true
ecode notification read notification:1
ecode notification read --all true
ecode notification unread notification:1
ecode notification jump-latest
ecode notification clear
```

对应方法：`notification.list`、`notification.read`、`notification.unread`、`notification.jump-latest`、`notification.clear`。

### Config / Status / Health

```powershell
ecode config reload
ecode config diagnostics
ecode status
ecode health
```

对应方法：`config.reload`、`config.diagnostics`、`status`、`health`。

## 浏览器命令

浏览器命令用于打开 WebView2 Surface，并对页面执行 snapshot、点击、输入、键盘、脚本和截图操作。详情见 [浏览器 API](./browser-api.md)。

```powershell
# 打开或创建浏览器 Surface
ecode browser open https://example.com
ecode browser new https://example.com
ecode browser open-split https://example.com --direction right

# 先拿到 surfaceRef，再执行页面动作
ecode browser snapshot --surfaceRef surface:1
ecode browser click --role button --name Submit --surfaceRef surface:1
ecode browser fill --testid email --value user@example.com --surfaceRef surface:1
ecode browser hover --text Help --surfaceRef surface:1
ecode browser press --testid search --key Enter --surfaceRef surface:1
ecode browser eval "document.title" --surfaceRef surface:1
ecode browser screenshot --surfaceRef surface:1
```

## Setup 命令

```powershell
ecode setup status
ecode setup install
ecode setup install --write true
ecode setup uninstall --write true
```

`setup` 可规划并应用 PATH、PowerShell profile、cmd AutoRun 集成。默认 dry-run；传 `--write true` 才会写入。

## Update 命令

```powershell
ecode update check --feed https://example.com/ecode/
ecode update install --feed https://example.com/ecode/
ecode update install --feed https://example.com/ecode/ --download-only true
```

更新命令读取 Velopack `RELEASES` feed。`check` 只检查版本，`install` 会下载并启动安装器。

## Completion 命令

```powershell
ecode completion powershell > $PROFILE.CurrentUserAllHosts
```

补全覆盖顶层命令、子命令、常用参数和短引用前缀。建议先备份 profile，或把输出追加到自己管理的 profile 片段。

## 脚本化示例

```powershell
# 读取 workspace 列表
$workspaces = ecode --json workspace list | ConvertFrom-Json
$workspaces

# 创建项目、分屏、写入命令
ecode workspace create --name demo
ecode pane split right
ecode pane write pane:1 "npm test"

# 发送长任务通知
ecode notify --title "Build" --body "等待输入"
```

## 常见错误

| 错误/现象 | 处理方式 |
| --- | --- |
| `Could not connect to ecode` | 启动 ECode 主应用后重试需要 pipe 的命令。 |
| 找不到 `workspace:1` / `pane:1` | 先运行 `ecode --json status` 或对应 `list` 命令获取最新 ref。 |
| 浏览器命令找不到元素 | 先运行 `ecode browser snapshot --surfaceRef <ref>`，确认 `--testid`、`--text` 或 `--role` 是否匹配。 |
| setup 没有写入 | 默认是 dry-run，需要加 `--write true`。 |
