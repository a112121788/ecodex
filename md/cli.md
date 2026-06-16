# 命令行

`ecodex` 是 ECodex 的控制入口：既能运行本地诊断与安装集成命令，也能连接主应用 pipe，驱动 Workspace、Surface、Pane、通知、集成浏览器和会话恢复。

## 先用这几条

| 场景 | 命令 |
| --- | --- |
| 检查本机环境、WebView2、PATH、daemon | `ecodex doctor` |
| 查看主应用状态 | `ecodex status` |
| 机器可读输出，适合脚本 | `ecodex --json status` |
| 管理项目命令配置 | `ecodex config reload` / `ecodex config diagnostics` |
| 打开集成浏览器 | `ecodex browser open https://example.com` |
| 安装 shell 集成 | `ecodex setup install --write true` |

## 命令模型

ECodex 命令行分成三类，排障时先确认你正在使用哪一种：

| 类型 | 是否需要主应用运行 | 代表命令 | 说明 |
| --- | --- | --- | --- |
| 本地命令 | 否 | `ecodex doctor`、`ecodex setup status`、`ecodex completion powershell` | 只读取本地环境或生成脚本。 |
| v1 兼容 | 是 | `ecodex status`、`ecodex split right`、`ecodex reload-config` | 发送旧式 pipe 命令，适合快捷操作。 |
| `ecodex.v2` | 是 | `ecodex workspace list`、`ecodex pane write`、`ecodex notification list` | 使用结构化 request/response，适合自动化。 |

> 看到 `Error: Could not connect to ecodex. Is it running?` 时，先启动 ECodex 主应用；本地命令不受影响。

## 全局参数

```powershell
ecodex --json status
ecodex --id-format refs status
ecodex --id-format uuids status
ecodex --id-format both status
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
ecodex version
ecodex help
ecodex doctor
ecodex --json doctor
ecodex setup status
ecodex profile import --dry-run
ecodex completion powershell
```

常见用途：

- `doctor`：诊断 ConPTY、WebView2 Runtime、PATH、daemon、配置目录和主应用健康状态。
- `setup status`：查看 PowerShell profile、cmd AutoRun、PATH 等集成是否已安装。
- `completion powershell`：输出 PowerShell 补全脚本。

## v1 兼容

兼容命令会发送旧式 pipe 命令，适合手动触发高频动作：

| 命令 | 说明 |
| --- | --- |
| `ecodex status` | 查看主应用状态。 |
| `ecodex notify --title T --body B` | 发送通知。 |
| `ecodex split right\|down` | 对当前 Pane 分屏。 |
| `ecodex reload-config` | 重载 `ecodex.json`。 |
| `ecodex restore-session` | 刷新恢复绑定并定位可恢复 Pane。 |

## `ecodex.v2` 命令组

### Window

```powershell
ecodex window list
ecodex window current
ecodex window focus window:1
ecodex window create "Scratch" # 兼容命令：单窗口模式下会聚焦现有窗口，不创建第二个主窗口
ecodex window close window:1
```

对应方法：`window.list`、`window.current`、`window.focus`、`window.create`、`window.close`。ECodex 主程序使用 `Global\ECodexMainApp` 保证单实例；再次启动应用只激活已有窗口，不转发启动参数。

### App Lifecycle（内部 IPC）

`app.exit` 是主应用 `ecodex.v2` 内部方法，用于自动化“退出 ECodex”。传入 `{"terminateTerminals":true}` 时，会先通过 daemon 的 `SESSION_LIST` + 逐个 `SESSION_CLOSE` 终止托管终端，再请求主应用退出；不再提供独立的 daemon `SESSION_CLOSE_ALL` 清理入口。

### Workspace

```powershell
ecodex workspace list
ecodex workspace create --name demo --cwd C:\repo\demo
ecodex workspace select workspace:1
ecodex workspace rename workspace:1 demo-app
ecodex workspace reorder "workspace:2,workspace:1"
ecodex workspace close workspace:1
```

对应方法：`workspace.list`、`workspace.create`、`workspace.select`、`workspace.close`、`workspace.rename`、`workspace.reorder`。

`workspace.create` 必须传 `--cwd`/`--workingDirectory` 指定项目文件夹；同一文件夹只能创建一个 Workspace。未传 `--name` 时默认使用文件夹名。

### Surface

```powershell
ecodex surface create
ecodex surface move surface:1 0
ecodex surface reorder "surface:2,surface:1"
ecodex surface next
ecodex surface previous
ecodex surface resume set --pane pane:1 --kind tmux --command "tmux attach -t demo"
ecodex surface resume show
ecodex surface resume clear --pane pane:1
```

对应方法：`surface.move`、`surface.reorder`，以及 `surface resume` 兼容命令。

### Pane

```powershell
ecodex pane list
ecodex pane focus pane:1
ecodex pane write pane:1 "npm test"
ecodex pane read pane:1
ecodex pane split right
ecodex pane resize pane:1 0.05
ecodex pane swap pane:1 pane:2
ecodex pane zoom pane:1
ecodex pane close pane:1
```

对应方法：`pane.list`、`pane.focus`、`pane.write`、`pane.read`、`pane.split`、`pane.close`、`pane.resize`、`pane.swap`、`pane.zoom`。

### Notification

```powershell
ecodex notification list
ecodex notification list --unread true
ecodex notification read notification:1
ecodex notification read --all true
ecodex notification unread notification:1
ecodex notification jump-latest
ecodex notification clear
```

对应方法：`notification.list`、`notification.read`、`notification.unread`、`notification.jump-latest`、`notification.clear`。

### Config / Status / Health

```powershell
ecodex config reload
ecodex config diagnostics
ecodex status
ecodex health
```

对应方法：`config.reload`、`config.diagnostics`、`status`、`health`。

## 浏览器命令

浏览器命令用于打开 WebView2 集成浏览器，并对页面执行 snapshot、点击、输入、键盘、脚本和截图操作。详情见 [浏览器 API](./browser-api.md)。

```powershell
# 打开或创建集成浏览器
ecodex browser open https://example.com
ecodex browser new https://example.com
ecodex browser open-split https://example.com --direction right

# 先拿到 surfaceRef，再执行页面动作
ecodex browser snapshot --surfaceRef surface:1
ecodex browser click --role button --name Submit --surfaceRef surface:1
ecodex browser fill --testid email --value user@example.com --surfaceRef surface:1
ecodex browser hover --text Help --surfaceRef surface:1
ecodex browser press --testid search --key Enter --surfaceRef surface:1
ecodex browser eval "document.title" --surfaceRef surface:1
ecodex browser screenshot --surfaceRef surface:1
```

## Setup 命令

```powershell
ecodex setup status
ecodex setup install
ecodex setup install --write true
ecodex setup uninstall --write true
```

`setup` 可规划并应用 PATH、PowerShell profile、cmd AutoRun 集成。默认 dry-run；传 `--write true` 才会写入。

## Update 命令

```powershell
ecodex update check --feed https://example.com/ecodex/
ecodex update install --feed https://example.com/ecodex/
ecodex update install --feed https://example.com/ecodex/ --download-only true
```

更新命令读取 Velopack `RELEASES` feed。`check` 只检查版本，`install` 会下载并启动安装器。

## Completion 命令

```powershell
ecodex completion powershell > $PROFILE.CurrentUserAllHosts
```

补全覆盖顶层命令、子命令、常用参数和短引用前缀。建议先备份 profile，或把输出追加到自己管理的 profile 片段。

## 脚本化示例

```powershell
# 读取 workspace 列表
$workspaces = ecodex --json workspace list | ConvertFrom-Json
$workspaces

# 创建项目、分屏、写入命令
ecodex workspace create --name demo --cwd C:\repo\demo
ecodex pane split right
ecodex pane write pane:1 "npm test"

# 发送长任务通知
ecodex notify --title "Build" --body "等待输入"
```

## 常见错误

| 错误/现象 | 处理方式 |
| --- | --- |
| `Could not connect to ecodex` | 启动 ECodex 主应用后重试需要 pipe 的命令。 |
| 找不到 `workspace:1` / `pane:1` | 先运行 `ecodex --json status` 或对应 `list` 命令获取最新 ref。 |
| 浏览器命令找不到元素 | 先运行 `ecodex browser snapshot --surfaceRef <ref>`，确认 `--testid`、`--text` 或 `--role` 是否匹配。 |
| setup 没有写入 | 默认是 dry-run，需要加 `--write true`。 |
