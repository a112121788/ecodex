# CLI 与 IPC 命令参考

> 这份清单汇总 `ecode`（CLI）、`\\.\pipe\ecode`（主应用通道）、`\\.\pipe\ecode-daemon`（守护进程通道）三处对外暴露的命令与消息。字段名、默认值、约束均来自源码（`src/ECode.Cli/Program.cs`、`src/ECode/ViewModels/MainViewModel.cs`、`src/ECode.Daemon/DaemonPipeServer.cs`、`src/ECode.Core/IPC/*`）。

---

## 1. `ecode.exe`（ECode.Cli）

### 1.1 全局参数约定

- 顶层第一个参数为命令名（大小写不敏感）
- 子参数支持：
  - 长选项 `--key value` / `--key`（后者等价于 `--key true`）
  - 短选项 `-k value`
  - 位置参数 `_arg0 / _arg1 / …`
- CLI 通过 JSON 参数体发送到主应用管道，带空格 / 引号的 shell 命令可稳定传输

### 1.2 命令

#### `notify`

| 选项 | 默认 | 含义 |
|---|---|---|
| `--title` | `"Terminal"` | 通知标题 |
| `--body` | `""` | 通知正文 |
| `--subtitle` | （无） | 可选副标题 |

底层 IPC：`NOTIFY title=… body=… subtitle=…`；返回 `{ok:true}`。

#### `workspace`

| 子命令 | 选项 | 行为 |
|---|---|---|
| `list` / `ls` | — | 列出项目 → `WORKSPACE.LIST` |
| `create` / `new` | `--name <text>` | 新建项目 → `WORKSPACE.CREATE` |
| `select` | `--index <n>` / `--id <id>` / `--name <name>` | 切换 → `WORKSPACE.SELECT` |
| `next` | — | `WORKSPACE.NEXT` |
| `previous` / `prev` | — | `WORKSPACE.PREVIOUS` |

#### `surface`

| 子命令 | 选项 | 行为 |
|---|---|---|
| `create` / `new` | — | `SURFACE.CREATE` |
| `next` | — | `SURFACE.NEXT` |
| `previous` / `prev` | — | `SURFACE.PREVIOUS` |
| `resume show` / `ls` / `list` | `--all` + 项目 / Surface / pane 定位参数 | `SURFACE.RESUME.SHOW`，默认显示当前聚焦 pane 的恢复绑定 |
| `resume set` | `--shell <cmd>` 或位置参数、`--kind <tmux|custom>`、`--checkpoint <id>`、`--cwd <path>`、`--trusted <bool>`、`--approvedPrefix <prefix>` | `SURFACE.RESUME.SET`，为当前 / 指定 pane 保存一条可恢复命令 |
| `resume clear` / `rm` / `remove` | `--id <bindingId>` 或项目 / Surface / pane 定位参数 | `SURFACE.RESUME.CLEAR`，按 ID 或当前 / 指定 pane 清理绑定 |

#### `split`

| 子命令 | 行为 |
|---|---|
| `right` / `vertical` / `v` | `SPLIT.RIGHT` |
| `down` / `horizontal` / `h` | `SPLIT.DOWN` |

#### `browser`

| 子命令 | 选项 | 行为 |
|---|---|---|
| `open <url>` | `--workspaceId/Name/Index`、`--surfaceId/Name/Index`、`--name <text>` | `BROWSER.OPEN`；若当前/指定 Surface 是 Browser 则复用，否则创建 Browser Surface |
| `new <url>` | `--workspaceId/Name/Index`、`--name <text>` | `BROWSER.NEW`；始终创建并选中新 Browser Surface |
| `open-split <url>` / `split <url>` | `--direction <right|down>`、`--workspaceId/Name/Index`、`--name <text>` | `BROWSER.OPEN_SPLIT`；v1 兼容入口，当前以 `fallbackMode:"new-surface"` 创建 Browser Surface |

#### `ecode.json` workspace layout

`ecode reload-config` 与应用启动会读取当前项目的 `.ecode/ecode.json`；`workspace.surfaces` 可声明 Terminal / Browser Surface，Browser Surface 会按 `name` 或 `url` 复用，避免重复创建。

```jsonc
{
  "workspace": {
    "selectedSurfaceIndex": 1,
    "surfaces": [
      { "type": "terminal", "name": "Shell" },
      { "type": "browser", "name": "Preview", "url": "http://localhost:5173" }
    ]
  }
}
```

#### `status`

- 无参数，调用 `STATUS`
- 返回 `{version, workspaces, selectedWorkspace, unreadNotifications}`

#### `help` / `--help` / `-h`

打印完整帮助（包含全部命令 + 快捷键 + 用法示例）。

#### `version` / `--version` / `-v`

打印 `ecode 1.0.0 (Windows)`。

### 1.3 退出码

| 退出码 | 含义 |
|---|---|
| `0` | 成功 |
| `1` | 解析错误 / 连接超时 / 服务端返回 `error` / 未知命令 |

## 2. `\\.\pipe\ecode` 主应用通道

### 2.1 请求行

```
COMMAND [k=v [k=v ...]]
```

- `COMMAND` 自动 `ToUpperInvariant`
- 参数解析：`NamedPipeServer.ParseArgs` 同时支持 JSON 对象 / `k=v`（引号包裹）/ 位置参数
- 响应：`OnCommand` 回调返回任意 JSON 字符串，单行返回

### 2.2 命令清单

| 命令 | 关键参数 | 默认 | 说明 |
|---|---|---|---|
| `NOTIFY` | `title` `body` `subtitle` | `title="Terminal"` | 写入当前项目 / Surface 的通知 |
| `WORKSPACE.LIST` | — | — | 返回 `[ {id,name,selected,surfaces} ]` |
| `WORKSPACE.CREATE` | `name` | — | 新建项目，返回 `{id,name}` |
| `WORKSPACE.SELECT` | `index`(0/1-based) / `id` / `name` | — | 切换项目（`name` 支持包含匹配） |
| `WORKSPACE.NEXT` | — | — | 下一个项目 |
| `WORKSPACE.PREVIOUS` | — | — | 上一个项目 |
| `SURFACE.CREATE` | — | — | 当前项目新增 Surface |
| `SURFACE.SELECT` | `workspaceId/Name/Index` + `surfaceId/Name/Index` | 当前项目 / Surface | 切换 Surface（`index` 支持 0/1-based，越界返回错误） |
| `SURFACE.NEXT` / `SURFACE.PREVIOUS` | — | — | 同 Surface 内切换 |
| `SURFACE.RESUME.SHOW` | 项目 + Surface + `paneId/Name/Index` 或 `all=true` | 当前聚焦 pane | 返回 `{ok, workspace, surface, pane, bindings}` |
| `SURFACE.RESUME.SET` | 项目 + Surface + pane + `shell` / `_arg*` + `kind` + `checkpoint` + `workingDirectory/cwd` + `trusted` + `approvedPrefix` | 当前聚焦 pane / `kind=custom` / session cwd | 写入 `resume.json`，同 pane 旧绑定会被替换 |
| `SURFACE.RESUME.CLEAR` | `id` 或项目 + Surface + pane | 当前聚焦 pane | 按 binding ID 删除，或删除当前 / 指定 pane 的绑定 |
| `BROWSER.OPEN` | `url` / `_arg0` + 项目 + 可选 Surface 定位 + `name/title` | 当前项目 / Surface | 打开 URL；复用 Browser Surface 或创建新 Browser Surface |
| `BROWSER.NEW` | `url` / `_arg0` + 项目 + `name/title` | 当前项目 | 创建并选中新 Browser Surface |
| `BROWSER.OPEN_SPLIT` | `url` / `_arg0` + 项目 + `direction` | 当前项目 / `direction=right` | v1 兼容入口；当前返回 `fallbackMode:"new-surface"` 并创建 Browser Surface |
| `SPLIT.RIGHT` / `SPLIT.DOWN` | — | — | 对当前聚焦面板分屏 |
| `PANE.LIST` | 项目 + Surface 定位参数 | 当前选中 | 返回 `{workspace, surface, panes:[{index,id,name,customName,focused,workingDirectory}]}` |
| `PANE.FOCUS` | 项目 + Surface + `paneId/Name/Index` | 当前聚焦 | 切换面板焦点 |
| `PANE.WRITE` | 项目 + Surface + `paneId/Name/Index` + `text` + `submit` + `submitKey` | 当前聚焦 | 写入文本；`submit=true` 自动追加 submit 序列；`submitKey ∈ {auto,enter,linefeed,crlf,none}` |
| `PANE.READ` | 项目 + Surface + `paneId/Name/Index` + `lines` (1..5000) + `maxChars` (512..200000) | 当前聚焦 / 80 行 / 20000 字符 | 返回 `{ok, ..., lines, maxChars, text}` |
| `STATUS` | — | — | `{version, workspaces, selectedWorkspace, unreadNotifications}`（`version` 当前为 `1.0.0` 程序集版本） |

#### 项目 / Surface / 面板定位约定

- 优先级：`id > name > index`
- 名称匹配：先精确（OrdinalIgnoreCase）后 `Contains`（OrdinalIgnoreCase）
- 索引：1-based 或 0-based 均可，越界返回 `{error:"... out of range"}`
- `PANE.WRITE` / `PANE.READ` / `PANE.FOCUS` 在未传 `paneId/Name/Index` 时，默认使用 `Surface.FocusedPaneId`，否则取 `RootNode.GetLeaves()[0]`

### 2.3 响应错误约定

未知命令 / 未找到目标 / 缺少必填参数 → `{error:"…"}`；成功 → `{ok:true, ...}` 或直接返回对象数组。

## 3. `\\.\pipe\ecode-daemon` 守护进程通道

### 3.1 消息格式

所有消息是**单行 JSON + `\n`**，请求 / 响应 / 事件共享同一字节流。

### 3.2 请求 / 响应

```jsonc
// 请求
{ "type": "SESSION_CREATE", "paneId": "pane-uuid", "cols": 120, "rows": 30,
  "workspaceId": "workspace-uuid", "workingDirectory": "C:\\repo", "command": "pwsh.exe" }
{ "type": "SESSION_WRITE",  "paneId": "pane-uuid", "data": "SGVsbG8=" }
{ "type": "SESSION_RESIZE", "paneId": "pane-uuid", "cols": 132, "rows": 40 }
{ "type": "SESSION_CLOSE",  "paneId": "pane-uuid" }
{ "type": "SESSION_LIST" }
{ "type": "SESSION_SNAPSHOT","paneId": "pane-uuid" }
{ "type": "PING" }

// 响应
{ "success": true, "error": null, "data": "<stringified payload>" }
{ "success": false, "error": "PaneId required", "data": null }
```

`data` 内容（按 `type` 不同）：

| `type` | `data` 解码 |
|---|---|
| `SESSION_CREATE` | `JsonSerializer.Deserialize<DaemonSessionInfo>(data)` |
| `SESSION_LIST`   | `List<DaemonSessionInfo>` |
| `SESSION_SNAPSHOT`| `TerminalBufferSnapshot`（`Cols/Rows/CursorRow/CursorCol/ScrollbackLines/ScreenLines`） |
| `PING`           | `"pong"` |
| 其他 | `null` |

### 3.3 事件（服务端主动推送）

```jsonc
{ "type": "OUTPUT",         "paneId": "pane-…", "data": "<base64 VT bytes>" }
{ "type": "EXITED",         "paneId": "pane-…", "data": "<exit code as string>" }
{ "type": "TITLE_CHANGED",  "paneId": "pane-…", "data": "<title>" }
{ "type": "CWD_CHANGED",    "paneId": "pane-…", "data": "<cwd>" }
{ "type": "BELL",           "paneId": "pane-…" }
```

事件由 `DaemonSessionManager` 的同名事件桥接到 `DaemonPipeServer.BroadcastEvent`，写入所有已连接客户端的 `Channel<string>`，再由 `DaemonClient.ListenLoop` 解析后分派为 `RawOutputReceived / SessionExited / TitleChanged / CwdChanged / BellReceived`。

### 3.4 区分响应与事件

`DaemonClient.ListenLoop` 优先按 `DaemonResponse` 反序列化；含 `"Success"` 字段则视为响应；否则按 `DaemonEvent` 处理（要求 `type` 非空）。

### 3.5 超时

| 端点 | 默认 |
|---|---|
| `SendRequestAsync` | 3 秒 |
| `TryConnect`（启动探测） | 300 ms |
| `StartDaemonAndConnect` | 最多 20 次 × 1000 ms 连接尝试，间隔 500 ms 探测守护进程崩溃 |
| `NamedPipeClient.SendCommand`（CLI） | 5000 ms |

### 3.6 守护进程守护逻辑

```text
启动
 ├─ 单实例互斥体 Global\ECodeDaemon
 ├─ 后台 Accept 线程（PipeServer-Accept）
 └─ 主线程每 5 分钟轮询：
     if 客户端==0 && 会话==0 && 距 lastActivity > 24h → 优雅退出
```

## 4. 用法示例

### 4.1 自动化脚本触发通知

```powershell
ecode notify --title "Build" --body "等待输入"
```

### 4.2 保存 / 查看恢复绑定

```powershell
# 保存当前 pane 的 tmux resume 命令
ecode surface resume set --kind tmux --shell "tmux attach -t work" --checkpoint "sprint-1" --trusted true --approvedPrefix "tmux attach"

# 查看当前 pane 绑定
ecode surface resume show

# 查看当前 Surface 下全部绑定
ecode surface resume show --all

# 清理当前 pane 绑定，或用 --id 精确删除
ecode surface resume clear
ecode surface resume clear --id <binding-id>
```

### 4.3 自动化建会话并写入命令

```powershell
# 创建项目
ecode workspace create --name "My Project"

# 新建 Surface
ecode surface create

# 在当前聚焦面板写入 + 提交（当前 CLI 顶层仅支持 status/notify/workspace/surface/split；pane 写入请直接通过 IPC 或 M5 v2 协议）
ecode status
# 等价于 PANE.WRITE / PANE.READ（用 PowerShell 调 NamedPipe 客户端；详见 spec/03-data-and-ipc.md §2）：
[script:pane-write] ...

# 读取最近 50 行输出
ecode pane read --lines 50
```

### 4.4 编程方式直接调 IPC（PowerShell）

```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'ecode', 'InOut')
$pipe.Connect(3000)
$writer = New-Object System.IO.StreamWriter($pipe, [System.Text.Encoding]::UTF8)
$reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
$writer.WriteLine('STATUS'); $writer.Flush()
$reader.ReadLine() | ConvertFrom-Json
$pipe.Close()
```

### 4.5 让脚本触发回车

`PANE.WRITE` 的 `submitKey` 关键字：

| 值 | 等价字节序列 |
|---|---|
| `auto`（默认） / 空 | `\r` |
| `enter` / `cr` / `ctrl+m` | `\r` |
| `linefeed` / `lf` / `ctrl+j` | `\n` |
| `crlf` | `\r\n` |
| `none` | （不追加） |

`submit=true` 时会自动 `TrimEnd('\r','\n')` 再追加序列；同步调用 `RegisterCommandSubmission` 把命令写入命令日志。

## 5. 错误码与边界

| 来源 | 触发 | 结果 |
|---|---|---|
| `ecode.exe` | 连接超时 | stderr `Error: Could not connect to ecode. Is it running?` 退出码 1 |
| `ecode.exe` | 命令未知 | stderr `Error: Unknown command: …` 退出码 1 |
| `NamedPipeServer` | 未注册 `OnCommand` | 响应 `{"error":"No handler registered"}` |
| `MainViewModel.HandlePipeCommand` | 未知命令 / 未找到目标 | 响应 `{"error":"…"}` |
| `DaemonPipeServer.ProcessRequest` | 异常 | 响应 `{success:false, error:<ex.Message>}` |
| `DaemonSessionManager` | 重连已存在会话 | `IsExisting=true` 响应 |
| `DaemonClient.TryConnect` | 超时 | 返回 `false`，由 `StartDaemonAndConnect` 兜底拉起守护进程 |
| `DaemonClient.ListenLoop` | 连接断开 | 设置 `_connected=false`、`Disconnected` 事件，UI 自动回退 |

## 6. 一致性约束

- 同一管道上的所有响应 / 事件均由对应客户端的**单一写线程**串行写入，避免交错字节
- 请求与响应通过 `SemaphoreSlim(1,1)` 串行化（同一 `DaemonClient` 同时只允许一个未决请求）
- 监听循环优先按响应解析（启发式："包含 `Success` 字段"），避免误把事件当成响应
- 所有 JSON 行以 `\n` 终止；服务端在 `ProcessRequest` 中按 `\n` 切行（绕开 `StreamReader`）
- `disconnected` 事件触发后，`SurfaceViewModel.OnDaemonDisconnected` 会清空 `_daemonPanes`，并将所有守护进程会话切换回本地 ConPTY（沿用 `paneShells + cwd`）
