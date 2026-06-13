# 模块与类职责

> 按命名空间 / 项目组织，列出每个核心类的职责、关键方法、依赖与事件。
> 所有类路径均相对仓库根 `ECode/`。

## 1. ECode.Core · Terminal

| 文件 | 类 | 职责 |
|---|---|---|
| `src/ECode.Core/Terminal/ConPtyInterop.cs` | `ConPtyInterop` (internal static) | `kernel32!CreatePseudoConsole / ResizePseudoConsole / ClosePseudoConsole / CreateProcess / CreatePipe` 等 P/Invoke 绑定；`STARTUPINFOEX / COORD / SECURITY_ATTRIBUTES / PROCESS_INFORMATION` 结构 |
| `src/ECode.Core/Terminal/PseudoConsole.cs` | `PseudoConsole` | 封装 HPCON 句柄 + 输入/输出 `SafeFileHandle`。`Create(cols, rows)` 创建立即返回两根调用方持有的管道；`Resize(cols, rows)`；`Dispose()` 关闭 HPCON 与管道 |
| `src/ECode.Core/Terminal/TerminalEnvironmentVariables.cs` | `TerminalEnvironmentVariables` | 构建 shell 启动环境；当前注入 `ECODE_WORKSPACE_ID`，并与父进程环境合并 |
| `src/ECode.Core/Terminal/TerminalProcess.cs` | `TerminalProcess` | 包装 Shell 进程：`CreateProcess` + ConPTY 线程属性 + 可选 Unicode 环境块；后台线程 `WaitForSingleObject` 触发 `Exited`；`DetectShell()` 选择 pwsh → powershell → cmd |
| `src/ECode.Core/Terminal/TerminalSession.cs` | `TerminalSession` | **核心**：VT 解析、缓冲区写入、C0/C1 控制、CSI 分派、OSC 路由；事件：`OutputReceived / ProcessExited / TitleChanged / WorkingDirectoryChanged / NotificationReceived / ShellPromptMarker / Redraw / BellReceived / RawOutputReceived`。提供 `Start / Write / Resize / FeedOutput / CreateBufferSnapshot / RestoreBufferSnapshot / Dispose`。`DaemonWrite/DaemonResize` 两个 Func 委托用于在守护进程模式下把写入/调整转发到 IPC |
| `src/ECode.Core/Terminal/VtParser.cs` | `VtParser` | 14 个状态的状态机（Ground / Escape / CsiEntry / CsiParam / CsiIgnore / OscString / Dcs* / SosPmApc），含 UTF-8 多字节续字符跨包边界处理；`Feed(ReadOnlySpan<byte>)` / `Feed(string)` |
| `src/ECode.Core/Terminal/OscHandler.cs` | `OscHandler` | 分派 OSC 0/2（标题）、7（cwd）、9（通知）、99（扩展通知 `key=value` 或 `title;body`）、777（`notify;title;body`）、133（Shell 提示符标记 A/B/C/D） |
| `src/ECode.Core/Terminal/TerminalBuffer.cs` | `TerminalBuffer` | 单元格网格（`TerminalCell[,]`）+ `ScrollbackBuffer<TerminalCell[]>`；实现宽字符（CJK）写入、回滚、滚动区域（DECSTBM）、备用屏幕（DECSET 1049）、插入/删除、擦除、快照/恢复、纯文本导出 |
| `src/ECode.Core/Terminal/TerminalAttribute.cs` | `CellFlags / TerminalColor / TerminalAttribute / TerminalCell` | 标志位 + 256 色（含 6×6×6 立方体 + 24 级灰度）+ 真彩色（`FromRgb`）+ 单元格结构 |
| `src/ECode.Core/Terminal/ScrollbackBuffer.cs` | `ScrollbackBuffer<T>` | 循环数组，O(1) `Add`（满则覆盖最旧） |
| `src/ECode.Core/Terminal/TerminalSelection.cs` | `TerminalSelection` | 选区（点击 / 拖动 / 双击选词 / 三击选行 / Ctrl+A 全选），支持回滚区，行尾自动 `TrimEnd` |
| `src/ECode.Core/Terminal/UrlDetector.cs` | `UrlDetector` | 正则匹配 `http(s):// ftp:// file://`；`GetRowText` 输出整行字符串 |

## 2. ECode.Core · Models

| 文件 | 类型 | 说明 |
|---|---|---|
| `Models/SplitNode.cs` | `SplitNode / SplitDirection` | 树形分屏；工厂方法 `CreateLeaf / CreateColumns / CreateRows / CreateGrid / CreateMainStack`；操作 `Split / FindNode / FindParent / GetLeaves / Remove / GetNextLeaf / GetPreviousLeaf / ResizePane / SwapPanes / Equalize` |
| `Models/Workspace.cs` | `Workspace` | UI 层 Model：`Id / Name / IconGlyph / AccentColor / Surfaces / SelectedSurface / GitBranch / WorkingDirectory / ListeningPorts / LatestNotificationText / UnreadNotificationCount` |
| `Models/Surface.cs` | `Surface / SurfaceKind` | 标签页 Model：`Kind(Terminal/Browser) / BrowserUrl / BrowserTitle / BrowserHistory / RootSplitNode / FocusedPaneId / PaneCustomNames / PaneSnapshots` |
| `Models/SessionState.cs` | `SessionState / WorkspaceState / SurfaceState / SplitNodeState / WindowState` | 持久化 DTO，`[JsonPropertyName]` 全部小写；缺少 `SurfaceState.kind` 的旧文件默认 Terminal |
| `Models/PaneStateSnapshot.cs` | `PaneStateSnapshot` | 单面板快照：cwd + shell + 命令历史 + `TerminalBufferSnapshot` |
| `Models/ResumeBinding.cs` | `ResumeBindingFile / ResumeBinding / ResumeBindingKinds` | `%USERPROFILE%/.ecode/resume.json` DTO；记录 workspace/surface/pane 与可恢复 shell 命令、cwd、安全环境、信任前缀和更新时间 |
| `Models/CommandLogEntry.cs` | `CommandLogEntry` | 命令日志项：起止时间、退出码、状态图标（`\uE916 / \uE73E / \uE711`）、时长格式化 |
| `Models/TerminalNotification.cs` | `TerminalNotification / AppNotification / NotificationSource` | OSC 通知或 CLI 通知，统一带 `IsRead`、`Source`（Osc9/Osc99/Osc777/Cli） |
| `Models/TerminalTranscriptEntry.cs` | `TerminalTranscriptEntry` | 脚本文件元数据：`FilePath / CapturedAt / Reason / SizeBytes` |
| `Models/Snippet.cs` | `Snippet` | 代码片段：`{{key}}` 占位符解析（`Resolve`）+ `GetPlaceholders` |
| `Models/EcodeJsonConfig.cs` | `EcodeJsonConfig / EcodeCommand / EcodeAction` | 项目级 `ecode.json` DTO；M1 支持 `commands` 与 `actions` 的 `command` 子集，目标为 `currentTerminal` / `newTabInCurrentPane` |
| `Models/AgentConversationThread.cs` | `AgentConversationThread` | Agent 会话线程索引：`MessageCount / TotalTokens / LastMessagePreview` |
| `Models/AgentConversationMessage.cs` | `AgentConversationMessage` | Agent 单条消息：role / content / tokens / `IsCompactionSummary` |
| `Models/GhosttyTheme.cs` | `GhosttyTheme` | Ghostty 风格主题：背景/前景/16 色调色板/光标/选区颜色/字体 |

## 3. ECode.Core · Services

| 文件 | 类 | 职责 |
|---|---|---|
| `Services/SessionPersistenceService.cs` | `SessionPersistenceService` | `Load/Save` 会话 JSON；`BuildState` 把内存模型序列化为 DTO；`SerializeSplitNode / DeserializeSplitNode` 桥接 `SplitNode ↔ SplitNodeState` |
| `Services/NotificationService.cs` | `NotificationService` | 内存通知集合（≤500），线程安全（lock），事件 `NotificationAdded / UnreadCountChanged`；`GetLatestUnread / GetLatestText / MarkAsRead / MarkWorkspaceAsRead / MarkAllAsRead` |
| `Services/CommandLogService.cs` | `CommandLogService` | **OSC 133 提示符标记处理**（A/B/C/D）、命令提交、手动注入；按日 JSONL 持久化；脱敏（`SanitizeCommandForStorage / SanitizeTranscriptText`）；终端脚本捕获 `SaveTerminalTranscript / GetTerminalTranscripts / LoadTerminalTranscriptContent`；保留策略 `CommandLogRetentionDays / TranscriptRetentionDays`（0 = 永久，默认 90） |
| `Services/SnippetService.cs` | `SnippetService` | 增删改查 + 搜索（按 name / content / category / tags / description，收藏优先）+ 首次启动播种 10 条默认 |
| `Services/EcodeJsonService.cs` | `EcodeJsonService` | 读取 `%USERPROFILE%\.config\ecode\ecode.json`、`<cwd>\.ecode\ecode.json`、`<cwd>\ecode.json`；支持 JSONC 注释 / 尾随逗号；全局与本地配置合并；输出可显示的诊断 |
| `Services/ResumeBindingService.cs` | `ResumeBindingService` | 读写 `%USERPROFILE%/.ecode/resume.json`；支持 `Load / Save / Add / Remove / FindForSurface / TrustPrefix`；保存前剔除 TOKEN / PASSWORD / SECRET / API_KEY 等敏感环境变量 |
| `Services/AgentConversationStoreService.cs` | `AgentConversationStoreService` | Agent 会话线程索引（`agent/threads.json`）+ 单线程消息追加文件（`agent/threads/<id>.jsonl`），容忍 BOM/多值 JSON/单行回退解析 |
| `Services/SecretStoreService.cs` | `SecretStoreService` (static) | DPAPI `ProtectedData.Protect/Unprotect` 存取 `secrets.json`；`GetSecret/SetSecret/RemoveSecret` |
| `Services/GitService.cs` | `GitService` (static) | 快速读 `.git/HEAD`；`git rev-parse --abbrev-ref HEAD` 回退；`GetRemoteUrl` |
| `Services/PortScanner.cs` | `PortScanner` (static) | `netstat -ano -p TCP` + WMI 进程树 → 监听端口列表 |
| `Services/ShellDetector.cs` | `ShellDetector` (static) + `ShellInfo` | 枚举系统 Shell：PowerShell 7（pwsh.exe）、Windows PowerShell、cmd、WSL、Git Bash |
| `Services/AgentDetector.cs` | `AgentDetector` (static) + `AgentType` | WMI 探测子进程名 → ClaudeCode / Codex / Aider / Copilot / Cursor / Cline / Windsurf；提供 `GetLabel / GetIcon`（Segoe MDL2 Assets 字形） |

## 4. ECode.Core · Config

| 文件 | 类型 | 说明 |
|---|---|---|
| `Config/ECodeSettings.cs` | `ECodeSettings` | 全局设置：字体、主题、Cursor 样式、Scrollback、保留策略、ShellProfiles、KeyBindings、RecentDirectories、嵌套 `AgentSettings` |
| `Config/AgentSettings.cs` | `AgentSettings / OpenAiCompatibleAgentSettings / AnthropicAgentSettings / ExaSearchSettings / AgentCustomToolConfig / AgentMcpServerConfig / AgentSubmitProfileConfig` | Agent 启用、Provider、模型、密钥名（DPAPI）、Bash/WebSearch 工具、提交策略、自动压缩 |
| `Config/SettingsService.cs` | `SettingsService` (static) | `Current` 懒加载；`Load/Save/Reset/NotifyChanged`；`SettingsChanged` 事件 |
| `Config/TerminalThemes.cs` | `TerminalTheme` + `TerminalThemes` (static) | 内置 8 套主题；`GetEffective(settings)` 叠加自定义色；`TryParseHexColor` 支持 `#RRGGBB` / `#RRGGBBAA` |
| `Config/GhosttyConfigReader.cs` | `GhosttyConfigReader` (static) | 解析 `%USERPROFILE%\.config\ghostty\config` 与 `%APPDATA%\ghostty\config`，支持 `#RGB` / `#RRGGBB` / `rgb(r,g,b)` / 命名色 |

## 5. ECode.Core · IPC

| 文件 | 类型 | 说明 |
|---|---|---|
| `IPC/DaemonMessages.cs` | `DaemonMessageTypes / DaemonRequest / DaemonResponse / DaemonSessionInfo / DaemonEvent` | 协议常量与 DTO（见 `03-data-and-ipc.md`） |
| `IPC/NamedPipeServer.cs` | `NamedPipeServer / NamedPipeClient` | **主应用 ↔ CLI 通道**（`\\.\pipe\ecode` 或 `\\.\pipe\ecode-{tag}`）。`OnCommand` 回调返回 JSON。支持 JSON 与 `k=v` 混合参数解析；`SendCommand` 5s 超时 |
| `IPC/DaemonClient.cs` | `DaemonClient` | **主应用 ↔ 守护进程客户端**。`TryConnect / StartDaemonAndConnect / CreateSessionAsync / WriteAsync / ResizeAsync / CloseSessionAsync / ListSessionsAsync / GetSnapshotAsync / PingAsync`。事件 `RawOutputReceived / SessionExited / TitleChanged / CwdChanged / BellReceived / Connected / Disconnected`。`SendRequestAsync` 用 `SemaphoreSlim(1,1)` + `TaskCompletionSource` + 3s 超时 |

## 6. ECode.Daemon

| 文件 | 类型 | 说明 |
|---|---|---|
| `src/ECode.Daemon/Program.cs` | `Main` | 单实例 Mutex `Global\ECodeDaemon`；启动 `DaemonSessionManager + DaemonPipeServer`；后台 Accept 线程；主线程 5 分钟轮询空闲（24h 无活动 + 0 客户端 + 0 会话则退出）；日志统一走 `DaemonClient.LogDaemon` |
| `src/ECode.Daemon/DaemonSessionManager.cs` | `DaemonSessionManager` | `ConcurrentDictionary<string, TerminalSession> _sessions`；`CreateSession` 若 `paneId` 已存在则返回 `IsExisting=true`（attach/重连语义）；事件 `SessionCreated / SessionExited / TitleChanged / CwdChanged / BellReceived / RawOutput`；`GetSnapshot` 序列化缓冲区（3000 行滚动） |
| `src/ECode.Daemon/DaemonPipeServer.cs` | `DaemonPipeServer` | 管道名 `ecode-daemon`；每客户端独立写线程 + `Channel<string>`；事件订阅自动 `BroadcastEvent`；支持请求类型 `SESSION_CREATE / WRITE / RESIZE / CLOSE / LIST / SNAPSHOT / PING`；手动按 `\n` 切行（绕开 StreamReader） |

## 7. ECode.Cli

| 文件 | 类型 | 说明 |
|---|---|---|
| `src/ECode.Cli/Program.cs` | `Main` + 命令处理器 | argv 解析（支持 `--key value` / `-k value` / 位置参数）；命令：`notify / workspace {list,create,select,next,previous} / surface {create,next,previous} / split {right,down} / status / help / version`；走 `NamedPipeClient.SendCommand`；返回 0/1；统一 JSON 美化输出 |

## 8. ECode (WPF)

### 8.1 启动与基础设施

| 文件 | 类型 | 说明 |
|---|---|---|
| `src/ECode/App.xaml.cs` | `App : Application` | 单例服务：`NotificationService / PipeServer / SnippetService / CommandLogService / AgentConversationStore / AgentRuntime / DaemonClient / DaemonConnectTask`；`OnStartup` 启管道 + 异步连守护进程；注册全局异常；非焦点时弹 Toast |
| `src/ECode/Services/ToastNotificationHelper.cs` | `ToastNotificationHelper` | 通过 `Microsoft.Toolkit.Uwp.Notifications` 显示 Windows Toast |
| `src/ECode/Services/AgentRuntimeService.cs` | `AgentRuntimeService` | 内置 Agent 运行时（OpenAI 兼容 / Anthropic），流式响应、工具调用（Bash / WebSearch / 自定义 / MCP）、上下文压缩、会话持久化（AgentConversationStoreService）、`TryHandlePaneCommand` 拦截 `/agent` 命令等 |
| `src/ECode/Converters/*` | (略) | XAML 值转换器 |
| `src/ECode/Themes/*` | (略) | 资源字典与样式 |
| `src/ECode/Assets/*` | `app-icon.ico` 等 | 应用图标 |

### 8.2 视图模型

| 文件 | 类型 | 说明 |
|---|---|---|
| `src/ECode/ViewModels/MainViewModel.cs` | `MainViewModel : ObservableObject` | 顶层：管理项目集合 + 侧边栏（Visible/Width/CompactSidebar）；命令 `CreateNewWorkspace / DuplicateWorkspace / CloseWorkspace / SelectWorkspace / NextWorkspace / PreviousWorkspace / ToggleSidebar / ToggleCompactSidebar / ToggleNotificationPanel / JumpToLatestUnread / MarkAllNotificationsRead`；`HandlePipeCommand` 集中分派 CLI 命令，含 `CONFIG.RELOAD` 事件桥接；`SaveSession / RestoreSession / CloneSplitNode` |
| `src/ECode/ViewModels/WorkspaceViewModel.cs` | `WorkspaceViewModel : ObservableObject, IDisposable` | `Workspace` 包装；定时器 5s 刷新 `GitBranch / DetectedAgent`（WMI）；`CreateNewSurface / CloseSurface / NextSurface / PreviousSurface / RefreshInfo`；图标字形自动判断字体（Segoe MDL2 Assets vs Segoe UI Emoji） |
| `src/ECode/ViewModels/SurfaceViewModel.cs` | `SurfaceViewModel : ObservableObject, IDisposable` | **关键**：`SplitNode` ↔ `TerminalSession` 双向绑定；`StartSession` → 守护进程优先（异步 attach + 拉快照 + 300ms 后 CR 触发重绘）+ 失败回退本地；`OnDaemonDisconnected` 自动回退；`SplitFocused / ClosePane / FocusPane / FocusNextPane / FocusPreviousPane / ToggleZoom / EqualizePanes / OpenPaneWithShell`；`CapturePaneTranscript / CaptureAllPaneTranscripts / CapturePaneSnapshotsForPersistence`；`RegisterCommandSubmission / TryHandlePaneCommand`（Agent 拦截） |

### 8.3 控件

| 文件 | 类型 | 说明 |
|---|---|---|
| `src/ECode/Controls/TerminalControl.cs` | `TerminalControl : FrameworkElement` | **核心渲染控件**：使用 `DrawingVisual` 渲染 `TerminalBuffer`（字符 / 属性 / 光标 / 选区 / URL 下划线 / 搜索高亮 / 可视响铃）；`AttachSession / DetachSession`；键盘输入（IME 兼容 / BracketedPaste / Ctrl+Alt+方向键 / 选区 / 双击 / 三击 / Ctrl+Insert 复制 / Shift+Insert 粘贴）；滚轮 + 触摸滚动；`Search`；事件：`FocusRequested / CommandSubmitted / CommandInterceptRequested / ClearRequested / SplitRequested / ZoomRequested / ClosePaneRequested / SearchRequested` |
| `src/ECode/Controls/SplitPaneContainer.cs` | `SplitPaneContainer : ContentControl` | 把 `SplitNode` 递归渲染成嵌套 `Grid` + `GridSplitter`；缩放模式只渲染聚焦叶子；每个面板头含标题 + 关闭按钮 + 重命名菜单 |
| `src/ECode/Controls/SurfaceTabBar.xaml(.cs)` | `SurfaceTabBar` | 标签页栏 + 内联搜索框（`Next/Previous`）；支持 Surface 拖拽重排、未读点、右键菜单；active tab 关闭按钮常显，非 active tab hover 显示 |
| `src/ECode/Controls/CommandPalette.xaml(.cs)` | `CommandPalette` | `Ctrl+Shift+P` 命令面板；支持额外 `SearchText`，用于 `ecode.json` keywords / action id 搜索；打开状态下可刷新 items 并保留搜索词 |
| `src/ECode/Controls/NotificationPanel.xaml(.cs)` | `NotificationPanel` | 通知列表 + 标记已读 |
| `src/ECode/Controls/SnippetPicker.xaml(.cs)` | `SnippetPicker` | 代码片段选择 + `{{key}}` 占位符填写 |
| `src/ECode/Controls/WorkspaceSidebarItem.xaml(.cs)` | `WorkspaceSidebarItem` | 项目项 UI |
| `src/ECode/Controls/BrowserControl.xaml(.cs)` | `BrowserControl` | WebView2 包装（Session Vault 浏览器视图） |

### 8.4 视图（顶级 Window / Dialog）

| 文件 | 类型 | 说明 |
|---|---|---|
| `src/ECode/Views/MainWindow.xaml(.cs)` | `MainWindow : Window` | 主窗口：侧边栏 + 主区；`OnLoaded` 恢复窗口几何；`OnClosing` 调 `ViewModel.SaveSession`；`OnSettingsChanged` 广播主题 / 字号到所有终端；`UpdateDaemonStatus` 用绿/灰点指示；大量 `OnKeyDown` 绑定应用级快捷键；命令面板打开时读取 `ecode.json` 并执行项目命令；`Ctrl+Shift+,` / CLI 可热重载配置 |
| `src/ECode/Views/SettingsWindow.xaml(.cs)` | `SettingsWindow` | 设置（外观 / 终端 / 行为 / 集合 / Agent 等标签页，93KB XAML） |
| `src/ECode/Views/SessionVaultWindow.xaml(.cs)` | `SessionVaultWindow` | 脚本回放浏览器（依赖 WebView2） |
| `src/ECode/Views/LogsWindow.xaml(.cs)` | `LogsWindow` | 命令日志查看（按日期 / 搜索） |
| `src/ECode/Views/HistoryWindow.xaml(.cs)` | `HistoryWindow` | 命令历史选择器 |
| `src/ECode/Views/ColorPickerWindow.xaml(.cs)` | `ColorPickerWindow` | 颜色选择 |
| `src/ECode/Views/TextPromptWindow.xaml(.cs)` | `TextPromptWindow` | 通用文本输入弹窗（重命名 / 占位符填写） |
| `src/ECode/Views/WindowAppearance.cs` | `WindowAppearance` | 圆角、阴影等外观样式 |

## 9. 测试

| 文件 | 类型 | 说明 |
|---|---|---|
| `tests/ECode.Tests/CoreTests.cs` | xUnit `VtParserTests` 等 | 解析器 / 缓冲区 / 分屏 / 通知 / 主题 / 持久化的纯逻辑测试 |
| `tests/ECode.Smoke/Program.cs` | `Main` | ConPTY 环境注入 + 直接读管道烟雾测试，输出到 `%TEMP%/ecode-smoke.log` |

## 10. 关键协作链路（序列图速记）

### 10.1 启动一个新 Surface

```text
MainViewModel.RestoreSession
   └─ WorkspaceViewModel(workspace)
        └─ SurfaceViewModel(surface, …)
             └─ 对每个叶子 StartSession(paneId, …)
                  ├─ 等 App.DaemonConnectTask (≤5s)
                  ├─ 守护进程可达 → StartDaemonSession
                  │     ├─ 异步：CreateSessionAsync → IsExisting 决定是否 GetSnapshot + 回车重绘
                  │     └─ 失败 → StartLocalSession
                  └─ StartLocalSession
                        ├─ PseudoConsole.Create(cols, rows)
                        ├─ TerminalProcess.CreateProcess(shell, cwd)
                        ├─ 后台 ReadLoop 线程 → VtParser → TerminalBuffer
                        └─ 终端 → WireSessionEvents 绑定 OSC 7/9/99/777/133
```

### 10.2 用户在终端里敲 Enter

```text
TerminalControl.OnPreviewKeyDown(Return)
   └─ session.Write("\r")               // 本地模式
   └─ 或 session.DaemonWrite("\r")      // 守护进程模式
        └─ DaemonClient.WriteAsync → 管道 → DaemonSessionManager.WriteToSession
```

### 10.3 Agent 在面板里发命令（CLI → 主应用）

```text
$ ecode pane write --text "npm start" --submit
   └─ NamedPipeClient.SendCommand("PANE.WRITE", { text, submit })
   └─ MainViewModel.HandlePaneWrite
        ├─ TryResolveWorkspace / TryResolveSurface / TryResolvePaneId
        ├─ session.Write(text)
        ├─ submit=true → ResolveSubmitSequence(submitKey) → "\r"
        └─ RegisterCommandSubmission → CommandLogService.RecordManualCommandSubmission
```
