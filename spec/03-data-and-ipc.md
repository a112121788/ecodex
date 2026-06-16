# 数据模型与 IPC 协议

> 描述 ecodex 在 IPC 与持久化层面的所有"线缆级"约定：消息类型、JSON 形状、字节边界、错误码。

---

## 1. 命名管道总览

| 管道 | 端点 | 消息形态 | 用途 |
|---|---|---|---|
| `\\.\pipe\ecodex`（或 `\\.\pipe\ecodex-{tag}`） | WPF 端 `NamedPipeServer` ↔ CLI 端 `NamedPipeClient` | 单行文本请求 + 单行 JSON 响应 | CLI 自动化与外部 hook |
| `\\.\pipe\ecodex-daemon` | WPF 端 `DaemonClient` ↔ 守护进程 `DaemonPipeServer` | 多行 JSON（请求 / 响应 / 事件共享同一字节流） | 终端会话托管与事件订阅 |

两份协议均要求 `PipeOptions.Asynchronous`（重叠 I/O），否则同一句柄上同步读写会死锁。

---

## 2. 主应用 ↔ CLI 协议（`\\.\pipe\ecodex`）

### 2.1 请求行格式

```
COMMAND [key1=value1] [key2=value2] ...
```

- 命令名大写（服务端 `ToUpperInvariant` 后分派）
- `ECodex.Cli` 默认发送 `COMMAND {json}`，用于稳定传输带空格 / 引号的命令参数
- 参数解析支持三种形态（`NamedPipeServer.ParseArgs`）：
  1. JSON 对象（首字符为 `{`）
  2. `key=value`，值带空格可用 `"..."` 或 `'...'`
  3. 位置参数（无 `=`），依次命名为 `_arg0, _arg1, ...`

### 2.2 响应

服务端返回一行 JSON（任意自定义形状）。CLI 收到后若能解析则 `JsonSerializer` 美化输出，否则原样打印。

CLI 5 秒超时（`NamedPipeClient.SendCommand` 默认 `timeoutMs=5000`）；超时抛 `TimeoutException`，CLI 打印 `"Error: Could not connect to ecodex. Is it running?"`。

### 2.3 命令清单

> 命令名严格匹配 `MainViewModel.HandlePipeCommand`。

| 命令 | 参数 | 行为 |
|---|---|---|
| `NOTIFY` | `title?` `body?` `subtitle?` | 向当前选中项目 / Surface 添加一条 `NotificationSource.Cli` 通知；返回 `{ok:true}` |
| `WORKSPACE.LIST` | — | 返回项目列表 `[ {id, name, selected, surfaces}, ... ]` |
| `WORKSPACE.CREATE` | `name?` | 新建项目，可选改名为 `name`；返回 `{id, name}` |
| `WORKSPACE.SELECT` | `index?` `id?` `name?` | 按 index（0/1-based）/ id / 名称匹配；`name` 支持精确与 `Contains`；返回 `{ok:true}` |
| `SURFACE.CREATE` | — | 新建 Surface；返回 `{ok:true}` |
| `SURFACE.SELECT` | `workspaceId?`/`workspaceName?`/`workspaceIndex?` + `surfaceId?`/`surfaceName?`/`surfaceIndex?` | 切换项目 + Surface；返回 `{ok, workspaceId, workspaceName, surfaceId, surfaceName}` |
| `SURFACE.RESUME.SHOW` | 同上 + `paneId?`/`paneName?`/`paneIndex?` 或 `all=true` | 读取 `%USERPROFILE%\.ecodex\resume.json`，返回 `{ok, workspace, surface, pane, bindings}`；默认只返回当前聚焦 pane |
| `SURFACE.RESUME.SET` | 同上 + pane 定位 + `shell` 或 `_arg*`、`kind?`、`checkpoint?`、`workingDirectory?`/`cwd?`、`trusted?`、`approvedPrefix?` | 写入 / 替换当前 pane 的恢复绑定；`kind ∈ {agent, tmux, custom}`，默认 `custom`；未传 cwd 时使用当前 session cwd |
| `SURFACE.RESUME.CLEAR` | `id?` 或同上 + pane 定位 | `id` 存在时按 binding ID 删除；否则删除当前 / 指定 pane 的所有绑定；返回 `{ok, removed, ...}` |
| `BROWSER.OPEN` | `url?`/`_arg0?` + `workspaceId?`/`workspaceName?`/`workspaceIndex?` + `surfaceId?`/`surfaceName?`/`surfaceIndex?` + `name?`/`title?` | 打开 URL；若目标 Surface 是 Browser 则复用，否则创建 Browser Surface；返回 `{ok, created, workspaceId, workspaceName, surfaceId, surfaceName, kind, url, title}` |
| `BROWSER.NEW` | `url?`/`_arg0?` + workspace 定位 + `name?`/`title?` | 始终创建并选中新 Browser Surface |
| `BROWSER.OPEN_SPLIT` | `url?`/`_arg0?` + workspace 定位 + `direction?` | v1 兼容入口；当前创建 Browser Surface 并返回 `fallbackMode:"new-surface"`，为后续 mixed pane split 保留 `direction` |
| `SPLIT.RIGHT` / `SPLIT.DOWN` | — | 对当前聚焦面板分屏 |
| `PANE.LIST` | `workspaceId?`/`workspaceName?`/`workspaceIndex?` + `surfaceId?`/`surfaceName?`/`surfaceIndex?` | 返回 `{workspace, surface, panes:[{index, id, name, customName, focused, workingDirectory}]}` |
| `PANE.FOCUS` | 同上 + `paneId?`/`paneName?`/`paneIndex?` | 切换面板焦点；返回 `{ok, workspaceId, workspaceName, surfaceId, surfaceName, paneId, paneIndex, paneName}` |
| `PANE.WRITE` | 同上 + `text` `submit` `submitKey` | 写入文本；`submit=true` 时自动追加 submit 序列；`submitKey ∈ {auto, enter, linefeed, crlf, none}`；同时调用 `RegisterCommandSubmission` |
| `PANE.READ` | 同上 + `lines ∈ [1,5000]` `maxChars ∈ [512,200000]` | 返回 `{ok, ..., lines, maxChars, text}`（`Buffer.ExportPlainText` + `TailLines`） |
| `STATUS` | — | 返回 `{version, workspaces, selectedWorkspace, unreadNotifications}`（`version` 当前为 `1.0.0` 程序集版本） |
| 未知命令 | — | 返回 `{error:"Unknown command: …"}` |

> 索引解析（`TryResolveCollectionIndex`）：正整数（1-based） 或 0 起（0-based）；越界返回错误。
> `WORKSPACE.SELECT` / `SURFACE.SELECT` / `PANE.*` 一律遵循优先级 `id > name > index`，与 `spec/05-cli-commands.md` 的“项目 / Surface / 面板定位约定”保持一致。

### 2.4 CLI 顶层命令

`ecodex.exe`（即 `ECodex.Cli`）的 argv 入口：

```text
ecodex notify       --title <text> --body <text> --subtitle <text>
ecodex workspace    list | create [--name <text>] | select [--index <n>|--id <id>|--name <text>]
                  | next | previous | prev
ecodex surface      create | next | previous | prev
ecodex surface      resume show [--all] [--paneIndex <n>|--paneId <id>|--paneName <name>]
ecodex surface      resume set --shell <cmd> [--kind agent|tmux|custom] [--checkpoint <id>] [--cwd <path>] [--trusted true]
ecodex surface      resume clear [--id <bindingId>|--paneIndex <n>|--paneId <id>|--paneName <name>]
ecodex browser      open <url> [--workspaceName <name>|--surfaceName <name>]
ecodex browser      new <url> [--workspaceName <name>] [--name <surface-name>]
ecodex browser      open-split <url> [--direction right|down]
ecodex split        right | vertical | v | down | horizontal | h
ecodex status
ecodex help | --help | -h
ecodex version | --version | -v
```

退出码：`0` 成功，`1` 失败（连接超时 / 参数错误 / 未知命令）。

---

## 3. 主应用 ↔ 守护进程协议（`\\.\pipe\ecodex-daemon`）

### 3.1 字节流

所有消息以 **`\n`（LF）结尾的 JSON 行** 形式在管道上传输；服务端手动按 `\n` 切行（绕开 `StreamReader`，原因见源码注释）。每客户端独占：

- 一个后台读取线程（手动 `pipe.Read`）
- 一个 `Channel<string>`（无界，`SingleReader=true, SingleWriter=false`）
- 一个后台写入线程（从 channel 取消息 → `pipe.Write(bytes)` → 写线程结束条件 = channel 完成）

> 写线程唯一：保证 `BroadcastEvent` 与请求响应在管道上的字节写入严格串行。

### 3.2 消息类型常量

```csharp
public static class DaemonMessageTypes {
    public const string SessionCreate    = "SESSION_CREATE";
    public const string SessionWrite     = "SESSION_WRITE";
    public const string SessionResize    = "SESSION_RESIZE";
    public const string SessionClose     = "SESSION_CLOSE";
    public const string SessionCloseAll  = "SESSION_CLOSE_ALL";
    public const string SessionList      = "SESSION_LIST";
    public const string SessionSnapshot  = "SESSION_SNAPSHOT";
    public const string Ping             = "PING";

    public const string EventOutput      = "OUTPUT";
    public const string EventExited      = "EXITED";
    public const string EventTitleChanged= "TITLE_CHANGED";
    public const string EventCwdChanged  = "CWD_CHANGED";
    public const string EventBell        = "BELL";
}
```

### 3.3 请求

```jsonc
{
  "type":   "SESSION_CREATE",      // 见上表
  "paneId": "pane-uuid",
  "cols":   120,                   // CREATE / RESIZE
  "rows":   30,
  "workspaceId": "workspace-uuid", // 可选，CREATE；注入为 ECODEX_WORKSPACE_ID
  "workingDirectory": "C:\\repo",  // 可选，CREATE
  "command": "pwsh.exe",           // 可选，CREATE（覆盖默认 shell）
  "data":   "SGVsbG8="             // Base64 字节；WRITE
}
```

### 3.4 响应

```jsonc
{
  "success": true,
  "error":   null,                  // 失败时填写
  "data":    "<string>"            // CREATE → 序列化的 DaemonSessionInfo；
                                   // LIST → 序列化的 List<DaemonSessionInfo>；
                                   // CLOSE_ALL → {"closed": <int>}；
                                   // SNAPSHOT → 序列化的 TerminalBufferSnapshot JSON；
                                   // PING → "pong"
}
```

```csharp
public class DaemonSessionInfo {
    public string PaneId;          // 必填
    public int Cols;
    public int Rows;
    public string WorkingDirectory;
    public string? Title;
    public bool IsRunning;
    public bool IsExisting;        // 重连/attach 时 = true
}
```

### 3.5 事件（服务端 → 客户端，无请求对应）

```jsonc
{ "type": "OUTPUT",         "paneId": "pane-…", "data": "<base64 VT bytes>" }
{ "type": "EXITED",         "paneId": "pane-…", "data": "0" }
{ "type": "TITLE_CHANGED",  "paneId": "pane-…", "data": "Build Server" }
{ "type": "CWD_CHANGED",    "paneId": "pane-…", "data": "C:\\repo\\src" }
{ "type": "BELL",           "paneId": "pane-…" }
```

事件由 `DaemonPipeServer.BroadcastEvent` 写入所有已连接客户端的 channel。

### 3.6 区分响应 vs 事件

`DaemonClient.ListenLoop` 优先尝试按 `DaemonResponse` 反序列化；若反序列化后含有 `"Success"` 字段则视作响应，否则按 `DaemonEvent` 处理。

### 3.7 超时与重试

| 端 | 默认超时 |
|---|---|
| `DaemonClient.SendRequestAsync` | 3 秒 |
| `DaemonClient.TryConnect`（启动探测） | 300ms |
| `DaemonClient.StartDaemonAndConnect` | 最多 20 次 × 1000ms 连接尝试，间隔 500ms 探测守护进程是否崩溃 |
| `NamedPipeClient.SendCommand`（CLI） | 5000ms |

### 3.8 守护进程生命周期

```text
启动
  ├─ 命名互斥体 Global\ECodexDaemon 单实例
  ├─ 创建 DaemonSessionManager + DaemonPipeServer
  ├─ 后台线程 PipeServer-Accept 持续 AcceptNewConnection
  └─ 主线程每 5 分钟轮询：
       if 客户端==0 && 会话==0 && 距 lastActivity > 24h → 优雅退出
      lastActivity 在 ClientConnected/Disconnected/SessionCreated 时刷新
```

`SESSION_CLOSE_ALL` 会终止 daemon 当前托管的全部终端会话，并返回已清理的会话数；主窗口右下角 daemon 状态入口提供同等操作，用于用户显式清理关闭窗口后继续保留的后台进程。

主应用主动退出时，`DaemonClient.Dispose()` 只关闭客户端管道，不广播 `Disconnected`；因此 `SurfaceViewModel.OnDaemonDisconnected()` 的本地 ConPTY 回退仅用于运行中 daemon 意外断开，不用于正常关闭窗口。

终端进程自然退出时，`DaemonSessionManager` 会从 active sessions 中移除对应 pane，再广播 `EXITED`；因此 daemon 空闲退出判断不会被已结束的终端进程阻塞。

---

## 4. 会话持久化（`%USERPROFILE%/.ecodex/session.json`）

### 4.1 Schema（`SessionState`）

```jsonc
{
  "version": 1,
  "selectedWorkspaceIndex": 0,
  "window": {
    "x": 100, "y": 80, "width": 1280, "height": 800,
    "isMaximized": false,
    "sidebarWidth": 280,
    "sidebarVisible": true,
    "compactSidebar": false
  },
  "workspaces": [
    {
      "id": "guid",
      "name": "My Project",
      "iconGlyph": "\uE8A5",
      "accentColor": "#FF818CF8",
      "workingDirectory": "C:\\repo",
      "selectedSurfaceIndex": 0,
      "surfaces": [
        {
          "id": "guid",
          "name": "Terminal 1",
          "kind": "Terminal",
          "browserUrl": null,
          "browserTitle": null,
          "browserHistory": [],
          "focusedPaneId": "pane-guid",
          "paneCustomNames": { "pane-guid": "Build" },
          "paneSnapshots": {
            "pane-guid": {
              "capturedAt": "2026-06-11T08:00:00Z",
              "workingDirectory": "C:\\repo",
              "shell": "pwsh.exe",
              "commandHistory": ["git status", "..."],
              "bufferSnapshot": {
                "cols": 120, "rows": 30,
                "cursorRow": 12, "cursorCol": 0,
                "scrollbackLines": ["...line n...", "..."],
                "screenLines":     ["...current row..."]
              }
            }
          },
          "rootNode": { /* 嵌套 SplitNodeState，见 §4.2 */ }
        }
      ]
    }
  ]
}
```

`SurfaceState.kind` 目前为 `"Terminal"` 或 `"Browser"`；旧 `session.json` 缺少该字段时默认恢复为 `Terminal`。Browser surface 使用 `browserUrl/browserTitle/browserHistory` 保存 URL 状态，terminal surface 继续使用 `rootNode/paneSnapshots`。

Browser surface 示例：

```jsonc
{
  "id": "browser-surface",
  "name": "Docs",
  "kind": "Browser",
  "browserUrl": "https://example.com/docs",
  "browserTitle": "Docs",
  "browserHistory": ["https://example.com", "https://example.com/docs"]
}
```

### 4.2 `SplitNodeState`

```jsonc
{
  "isLeaf":      false,
  "direction":   "Vertical",   // 或 "Horizontal"
  "splitRatio":  0.5,
  "paneId":      "pane-…",     // 仅叶子节点
  "workingDirectory": "C:\\…", // 叶子节点可携带覆盖 cwd
  "first":       { /* 递归 */ },
  "second":      { /* 递归 */ }
}
```

序列化：`SessionPersistenceService.SerializeSplitNode` 递归；反序列化：`DeserializeSplitNode`（`Direction` 解析失败回退 `Vertical`）。

### 4.3 写入策略

`Save` → `tmp = path + ".tmp"` → `File.WriteAllText(tmp)` → `File.Move(tmp, path, overwrite:true)`。

`BuildState` 在调用前需先触发：
- `MainViewModel.SaveSession` 调用所有 `Surface.CapturePaneSnapshotsForPersistence()`，把当前 `TerminalBuffer.CreateSnapshot(maxScrollbackLines:3000)` 写入 `Surface.PaneSnapshots`。
- 同时 `Workspace.CaptureAllSurfaceTranscripts("session-close")` → `CommandLogService.SaveTerminalTranscript` 落盘到 `logs/terminal/YYYY-MM-DD/...`。

---

## 5. 命令日志（`%USERPROFILE%/.ecodex/logs/`）

### 5.1 文件结构

```
logs/
├── 2026-06-10.jsonl     # 一日一行：{id, paneId, surfaceId, workspaceId,
│                          command, startedAt, completedAt?, exitCode?, workingDirectory}
└── terminal/
    └── 2026-06-11/
        └── 143205_session-close_abc12345_def67890_11223344.log
```

每条 JSONL 行 = 一条 `CommandLogEntry`。`StartedAt` 用 UTC，落盘时按本地日期分桶（`DateOnly.FromDateTime(entry.StartedAt.ToLocalTime())`）。

### 5.2 终端脚本文件（`.log`）

```text
# ecodex terminal transcript
# captured-at: 2026-06-11T14:32:05.0000000Z
# workspace-id: …
# surface-id: …
# pane-id: …
# reason: session-close
# working-directory: C:\repo

<正文（已脱敏）>
```

`CommandLogService.LoadTerminalTranscriptContent` 解析时跳过所有 `#` 开头的头部行与紧随的空白行后拼接正文。

### 5.3 OSC 133 协议（提示符标记）

Shell 写入 `\e]133;A` / `\e]133;B;<command>` / `\e]133;C` / `\e]133;D;<exitcode>` 即可被 `OscHandler` 捕获并转发到 `CommandLogService.HandlePromptMarker`：

| 标记 | 行为 |
|---|---|
| `A` (prompt start) | 强制完成当前活动命令（用于重置） |
| `B` (command start) | 新建 `CommandLogEntry`，把 `<command>` 写入 `Command`（脱敏后） |
| `C` (output start) | 通知类，不改变状态 |
| `D` (command done) | 完成当前活动命令，记录退出码（支持 `<code>` 或 `...;<code>` 形式） |

`LooksLikeSecretInput`（启发式）会在 `B` 时把单独看起来像密码的输入剔除。

### 5.4 保留策略

| 字段 | 含义 |
|---|---|
| `ECodexSettings.CommandLogRetentionDays` | 命令日志按日清理（默认 90，`0` = 永久保留） |
| `ECodexSettings.TranscriptRetentionDays` | 脚本日志按文件 `LastWriteTime` 清理（默认 90，`0` = 永久保留） |
| `ECodexSettings.CaptureTranscriptsOnClose` | Surface/Pane 关闭 / 清理时是否落盘脚本 |
| `ECodexSettings.CaptureTranscriptsOnClear` | 清屏时是否落盘脚本 |

应用启动 + `SettingsChanged` 时调用 `ApplyRetentionPolicy / ApplyTranscriptRetentionPolicy / ScrubSensitiveData…`。

---

## 6. 通知（内存 + 可选 Toast）

`TerminalNotification` 关键字段：

```csharp
public required string WorkspaceId;
public required string SurfaceId;
public string? PaneId;
public bool IsRead;
public required string Title;
public string? Subtitle;
public required string Body;
public DateTime Timestamp;        // UTC
public NotificationSource Source; // Osc9 / Osc99 / Osc777 / Cli
```

- 内存上限 500（`AddNotification` 满则丢弃最旧）
- 项目维度的 `UnreadCount` 在每次 `UnreadCountChanged` 事件时回写到 `WorkspaceViewModel`
- 当 `MainWindow` 未获焦时触发 Windows Toast（`ToastNotificationHelper.ShowToast`）
- `Ctrl+Shift+U` → `JumpToLatestUnread`：选中项目 + Surface + 聚焦面板 + 标记已读

---

## 7. 代码片段（`%USERPROFILE%/.ecodex/snippets.json`）

`List<Snippet>` JSON 数组；`Snippet.Content` 支持 `{{key}}` 占位符：

- `Snippet.GetPlaceholders()` 提取所有不同占位符
- `Snippet.Resolve(parameters)` 替换为参数值，未匹配的占位符保留
- `SnippetPicker` 在选择时弹窗询问每个占位符 → 拼接 → 通过 `session.Write` 注入
- `SnippetService.Search` 跨 `Name / Content / Category / Tags / Description` 子串匹配（OrdinalIgnoreCase）

---

## 8. Agent 会话（`%USERPROFILE%/.ecodex/agent/`）

```
agent/
├── threads.json                              // 索引（每次 PersistThreadsIndex 整体重写）
└── threads/
    └── <threadId>.jsonl                      // 追加写
```

- `AgentConversationStoreService`：
  - `CreateThread / GetThread / GetThreads / SearchThreads` — 受 lock 保护，clone 返回
  - `AppendMessage` 追加消息（角色归一化小写，ID/时间缺失时填默认值）
  - `PersistThreadsIndex` 整体重写索引（按 `UpdatedAtUtc` 倒序）
  - `ReadMessagesFromFile` 兼容 BOM + 多值 JSON + 退化的单行 JSONL
- 线程聚合统计：`MessageCount / TotalInputTokens / TotalOutputTokens / TotalTokens / CompactionCount / LastMessagePreview`（前 160 字符，多行折叠成空格）

---

## 9. 加密存储（`%USERPROFILE%/.ecodex/secrets.json`）

```jsonc
{
  "agent.openai.apiKey":   "<base64 of ProtectedData.Protect(plain, null, CurrentUser)>",
  "agent.anthropic.apiKey":"<base64 of ProtectedData.Protect(plain, null, CurrentUser)>"
}
```

`SecretStoreService`：
- `GetSecret(name)` → `Convert.FromBase64String` → `ProtectedData.Unprotect(..., CurrentUser)` → UTF-8
- `SetSecret(name, value)` 为空时删除键；非空时加密后写回
- 原子写（先 `.tmp` 再 `Move`）

> 仅当前用户能解密；同一用户在不同进程上下文（提升 / 模拟）下不可访问。

---

## 10. 守护进程诊断日志

`%USERPROFILE%/.ecodex/daemon-debug.log`：

- 由 `DaemonClient.LogDaemon` 写入（共享追加 `FileShare.ReadWrite`，避免客户端与守护进程互锁）
- 行格式：`ts=<ISO8601> component=<name> event=<name> paneId=<id-or-> message=<quoted>`
- 附加字段按 key 排序输出，例如 `requestType=SESSION_CREATE`、`clientId=...`、`activeSessions=...`
- 守护进程 + WPF 客户端均写入；用于排查连接 / attach / 重连问题

> 该日志**不做完整敏感信息脱敏**，仅供开发者本地诊断使用，避免写入密钥或令牌。
