# ECodeX 架构设计文档

> 本文档基于源码（`src/ECodeX`、`src/ECodeX.Core`、`src/ECodeX.Cli`、`src/ECodeX.Daemon`）与 `README.md` 实际行为撰写。
> 项目代号：ECodeX（Windows 版），主程序集名 `ecodex-app`，CLI `ecodex`，守护进程 `ecodex-daemon`。

## 1. 项目概述

ecodex 是面向 Windows 的深色、键盘优先的终端复用器，灵感来自 tmux/ecodex 工作流，底层使用 **WPF + ConPTY** 原生构建。

**核心价值**：
- 项目（Workspace）+ 标签页（Surface）+ 二叉树分屏（SplitNode），实现多项目/多任务上下文隔离
- 终端持久化（守护进程 + 缓冲区快照双模式），崩溃/重启后可恢复
- AI Agent 友好：OSC 通知（9/99/777）、命令日志、脚本捕获、Agent 会话记录
- 键盘优先：命令面板 + 快捷键 + 命令片段（Snippet）
- 自动化集成：`ecodex` CLI + 命名管道 IPC，可被 agent/脚本调用

---

## 2. 技术栈

| 层级 | 选型 | 说明 |
|---|---|---|
| **UI 框架** | WPF (.NET 10, `net10.0-windows10.0.17763.0`) | 桌面应用，XAML + MVVM |
| **MVVM** | CommunityToolkit.Mvvm 8.3.2 | `[ObservableProperty]` / `[RelayCommand]` |
| **终端引擎** | ConPTY（`kernel32!CreatePseudoConsole`） | Windows 原生伪终端 |
| **IPC — 主↔守护** | Named Pipe（`\\.\pipe\ecodex-daemon`，JSON over line） | 双工 + 事件订阅 |
| **IPC — 主↔CLI** | Named Pipe（`\\.\pipe\ecodex` 或 `\\.\pipe\ecodex-{tag}`） | 单请求单响应 |
| **持久化** | JSON 文件 | 原子写（先 `.tmp` 再 `File.Move`） |
| **进程信息** | System.Management 9.0.3（WMI） | git 探测、端口扫描、Agent 探测 |
| **加密** | System.Security.Cryptography.ProtectedData 10.0.0（DPAPI） | API 密钥加密 |
| **浏览器集成** | Microsoft.Web.WebView2 1.0.2651.64 | Session Vault 浏览器视图 |
| **Toast** | Microsoft.Toolkit.Uwp.Notifications 7.1.3 | Windows Toast 通知 |
| **测试** | xUnit 2.9.3 + FluentAssertions 7.2.0 | CoreTests 单元测试 + ECodeX.Smoke ConPTY 烟雾测试 |

> 全局编译开关（`Directory.Build.props`）：`LangVersion=14`、`Nullable=enable`、`ImplicitUsings=enable`、`TreatWarningsAsErrors=true`、`WarningLevel=7`。

---

## 3. 解决方案结构

```text
ECodeX.sln
├── src/
│   ├── ECodeX/                # WPF 主程序（ecodex-app.exe），UI / 控件 / 视图模型
│   ├── ECodeX.Core/           # 跨进程复用库：终端引擎、模型、服务、IPC、配置
│   ├── ECodeX.Cli/            # CLI 客户端（ecodex.exe）
│   └── ECodeX.Daemon/         # 守护进程（ecodex-daemon.exe）
└── tests/
    ├── ECodeX.Tests/          # xUnit 单元测试（针对 ECodeX.Core）
    └── ECodeX.Smoke/          # ConPTY 集成烟雾测试
```

依赖关系（项目引用）：

```text
ECodeX  ──▶  ECodeX.Core
ECodeX.Cli ──▶  ECodeX.Core
ECodeX.Daemon ──▶  ECodeX.Core
ECodeX.Tests ──▶  ECodeX.Core
ECodeX.Smoke ──▶  ECodeX.Core
```

`ECodeX.Core.csproj` 额外声明 `AllowUnsafeBlocks=true`（Interop 用）。

---

## 4. 整体架构

### 4.1 分层视图

```text
┌───────────────────────────────────────────────────────────────┐
│                ECodeX (WPF · ecodex-app.exe)                         │
│ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐    │
│ │ Views (XAML) │ │ Controls     │ │ Themes / Converters  │    │
│ │ MainWindow … │ │ TerminalCtl  │ │                      │    │
│ └──────────────┘ │ SplitPaneCtl │ └──────────────────────┘    │
│                  │ CmdPalette … │                            │
│                  └──────────────┘                            │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ ViewModels (CommunityToolkit.Mvvm)                       │ │
│ │  MainViewModel · WorkspaceViewModel · SurfaceViewModel   │ │
│ └──────────────────────────────────────────────────────────┘ │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ UI 服务：ToastNotificationHelper · AgentRuntimeService   │ │
│ └──────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────┘
                              │ 项目引用
                              ▼
┌───────────────────────────────────────────────────────────────┐
│              ECodeX.Core (类库)                                 │
│ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌───────────────┐    │
│ │ Terminal │ │   IPC    │ │ Services │ │    Config     │    │
│ │ 引擎     │ │          │ │          │ │               │    │
│ └──────────┘ └──────────┘ └──────────┘ └───────────────┘    │
│ ┌──────────┐ ┌──────────┐                                    │
│ │  Models  │ │ Themes   │                                    │
│ └──────────┘ └──────────┘                                    │
└───────────────────────────────────────────────────────────────┘
          ▲                            ▲
          │                            │
┌─────────────────────┐      ┌─────────────────────┐
│   ECodeX.Daemon       │      │     ECodeX.Cli        │
│  (ecodex-daemon.exe)  │      │     (ecodex.exe)      │
│  会话托管 / 事件分发 │      │  通过命名管道调用    │
└─────────────────────┘      └─────────────────────┘
```

### 4.2 进程角色

| 进程 | 可执行文件 | 生命周期 | 主要职责 |
|---|---|---|---|
| **主应用** | `ecodex-app.exe` | 用户启动 / 关闭 | WPF UI、用户交互、终端渲染、IPC 服务端（CLI 通道） |
| **守护进程** | `ecodex-daemon.exe` | 按需启动、空闲 24h 退出 | 终端会话托管、IPC 事件分发（OUTPUT/EXITED/CWD/TITLE/BELL） |
| **CLI** | `ecodex.exe` | 单次执行 | 解析 argv、连接命名管道并请求 |
| **Shell** | `pwsh.exe` / `powershell.exe` / `cmd.exe` / `wsl.exe` / Git Bash | 终端生命周期 | 用户实际操作的目标进程（嵌入 ConPTY） |

> 守护进程单实例：`Global\ECodeXDaemon` 命名互斥体；日志统一写入 `%USERPROFILE%/.ecodex/daemon-debug.log`。

### 4.3 启动序列

```text
用户启动 ecodex-app.exe
  ├─ App.OnStartup
  │   ├─ 启动命名管道服务器 \\.\pipe\ecodex（CLI 通道）
  │   └─ 异步任务：先 300ms TryConnect() 守护进程，失败则 StartDaemonAndConnect()
  │
  ├─ MainViewModel 构造
  │   ├─ 接管 PipeServer.OnCommand
  │   └─ SessionPersistenceService.Load()
  │        ├─ 有 → RestoreSession()
  │        └─ 无 → CreateNewWorkspace()
  │
  ├─ WorkspaceViewModel → SurfaceViewModel → Surface 叶子节点
  │   └─ 每个叶子 StartSession(paneId, …)
  │        ├─ 等 App.DaemonConnectTask（≤5s）
  │        ├─ 守护进程可达 → StartDaemonSession（异步 attach，必要时回退本地）
  │        └─ 守护进程不可用 → StartLocalSession（直接 ConPTY）
  │
  └─ MainWindow 渲染 UI、刷新 Session/UI 状态
```

---

## 5. 数据流

### 5.1 本地终端会话（无守护进程）

```text
键盘输入
   │  KeyDown / TextInput (IME)
   ▼
TerminalControl → TerminalSession.Write(bytes)
   │  (未配置 DaemonWrite 委托时)
   ▼
FileStream.Write → ConPTY WritePipe
   │  WriteFile
   ▼
Shell 进程 (pwsh / cmd / …)
   │  ReadFile / ConPTY 转换
   ▼
FileStream.Read (4096B 循环，专用后台线程)
   ▼
VtParser.Feed(bytes) ── OSC ──▶ OscHandler (Title / Cwd / OSC 9/99/777 / OSC 133)
   │  CSI / ESC / 打印字符
   ▼
TerminalBuffer 更新 (光标、字符、属性、回滚)
   ▼
TerminalControl 渲染 (DrawingVisual，DispatcherTimer 刷新 / Redraw 事件)
```

### 5.2 守护进程会话

```text
WPF TerminalSession.Write(bytes)
   │  DaemonWrite 已注入
   ▼
DaemonClient.WriteAsync(paneId, bytes)
   │  JSON: { type:"SESSION_WRITE", paneId, data:Base64 }
   ▼
\\.\pipe\ecodex-daemon (字节流 + "\n")
   │
   ▼
DaemonPipeServer.HandleConnection
   │  串行 channel + 写线程
   ▼
DaemonSessionManager.WriteToSession → 本地 TerminalSession.Write
   │  → ConPTY 写入
   ▼
Shell 进程
   │
   ▼ ConPTY 读出
DaemonSessionManager.RawOutput 事件
   │  DaemonPipeServer.BroadcastEvent
   │  JSON: { type:"OUTPUT", paneId, data:Base64 }
   ▼
DaemonClient.ListenLoop → RawOutputReceived(paneId, bytes)
   ▼
SurfaceViewModel.OnDaemonRawOutput → TerminalSession.FeedOutput(bytes)
   ▼
VtParser → TerminalBuffer → 渲染
```

### 5.3 CLI → 主应用

```text
$ ecodex notify --title "Claude Code" --body "等待输入"
   │
   ▼ ECodeX.Cli 解析 argv
NamedPipeClient.SendCommand("NOTIFY", { title, body })
   │
   ▼ \\.\pipe\ecodex  →  "NOTIFY title=\"...\" body=\"...\"\n"
ECodeX.WPF NamedPipeServer.HandleConnection
   ▼ OnCommand(command, args)
MainViewModel.HandlePipeCommand (在 UI Dispatcher 上 Invoke)
   │  NOTIFY → NotificationService.AddNotification(...)
   ▼
JSON 响应返回 CLI → 控制台打印
```

### 5.4 CLI ↔ 守护进程（暂未提供）

当前 CLI 直接与主应用管道通信；与守护进程之间的交互由 WPF 端透传或通过 `STATUS` / `PANE.LIST` 等命令间接体现。

---

## 6. 关键设计决策

### 6.1 为什么需要守护进程？

**问题**：WPF 应用崩溃 / 重启 / 切换用户时，本地 ConPTY 会话随之终止，无法"真正"持久化终端。

**方案**：将 ConPTY 会话托管到独立的 `ecodex-daemon.exe`；WPF 通过命名管道订阅 `OUTPUT` / `EXITED` / `CWD_CHANGED` / `TITLE_CHANGED` / `BELL` 事件，并在重连时通过 `SESSION_SNAPSHOT` 拉回缓冲区。

**权衡**：
- ✅ 真正的会话持久化、进程隔离（Daemon 独立于 WPF 生命周期）
- ✅ WPF 启动时自动 `StartDaemonAndConnect()`；进程死亡时 `Disconnected` 事件触发 UI 回退
- ❌ 增加架构复杂度与 IPC 开销；因此保留纯本地模式（守护进程不可用时自动回退）

### 6.2 分屏布局：二叉树

```csharp
public class SplitNode {
    public string Id;          // 节点稳定 ID
    public bool IsLeaf;
    public SplitDirection Direction;  // Horizontal / Vertical
    public SplitNode? First;
    public SplitNode? Second;
    public double SplitRatio;  // 0.1 .. 0.9
    public string? PaneId;     // 叶子节点 = 终端面板 ID
}
```

- 天然支持递归分屏（左右/上下任意嵌套）
- 叶子删除：兄弟节点内容替换父节点 → 自动填充空间
- 序列化简单（JSON 树结构，对应 `SplitNodeState`）
- 工具方法：`Split / FindNode / FindParent / GetLeaves / Remove / GetNextLeaf / GetPreviousLeaf / ResizePane / SwapPanes / Equalize`
- 工厂方法：`CreateColumns / CreateRows / CreateGrid / CreateMainStack`

### 6.3 VT 解析：状态机

`VtParser` 基于 Paul Flo Williams 的 VT 解析器状态机实现（`vt100.net/emu/dec_ansi_parser`），状态枚举：

```text
Ground · Escape · EscapeIntermediate
CsiEntry · CsiParam · CsiIntermediate · CsiIgnore
OscString
DcsEntry · DcsParam · DcsIntermediate · DcsPassthrough · DcsIgnore
SosPmApc
```

要点：
- **UTF-8 续字节追踪**：`_utf8Remaining` / `_utf8Codepoint`，跨包边界不丢字符
- **CSI 参数分隔**：`;` 与 `:` 均视作合法分隔符；私有修饰符（`? > ! =`）用 `_collectChar` 保存
- **OSC 终止符**：BEL（0x07）、ST 8 位（0x9C）、ESC \\（0x1B 触发状态机回到 Escape）
- **回调式分派**：`OnPrint / OnExecute / OnCsiDispatch / OnEscDispatch / OnOscDispatch`，由 `TerminalSession.WireParser()` 绑定到 `TerminalBuffer` 和 `OscHandler`

### 6.4 会话持久化：快照 + 守护进程

| 方案 | 行为 | 优点 | 缺点 |
|---|---|---|---|
| 命令回放 | 记录输入并重新执行 | 真实环境 | 慢、副作用、不可重现 |
| 缓冲区快照 | 序列化 `TerminalBuffer`（行 + 屏幕 + 光标） | 快速、无副作用 | 不保留实时进程 |
| 守护进程托管 | 进程一直在跑 | 真正恢复 | 资源占用 |

实现策略：
- **默认**：缓冲区快照（启动时 `SessionPersistenceService.Load()` + `RestoreSession()` + `RestoreBufferSnapshot()`）
- **可选**：守护进程存在时优先 attach，attach 失败则回退到本地 ConPTY

### 6.5 命名管道的同步 I/O 与防死锁

`\\.\pipe\ecodex-daemon` 与 `\\.\pipe\ecodex` 均使用 `PipeOptions.Asynchronous`（重叠 I/O），原因：若使用同步 I/O，Windows 会在同一句柄上串行化所有操作，监听循环的阻塞 `Read` 会阻塞 `Write`，导致死锁。`DaemonPipeServer` 还为每个客户端分配独立写线程 + `Channel<string>`，保证事件与响应在管道上的写入严格串行。

### 6.6 命令日志脱敏

`CommandLogService` 在写入磁盘前会调用 `SanitizeCommandForStorage`，匹配三类规则：
- 形如 `KEY=VALUE` 的环境变量赋值（KEY 含 PASSWORD/TOKEN/SECRET/API_KEY/ACCESS_KEY）
- 形如 `--password / --token / --secret / --api-key / --api_key / --access-key / --access_key` 的命令行参数
- `scheme://user:pass@host` URL 中的凭据
- 形如密码提示输入的孤立 token（启发式 `LooksLikeSecretInput`）

启动时还会对历史 `*.jsonl` 与 `vault/terminal/*.log` 做一次 `ScrubSensitiveData…` 批量改写。

### 6.7 MVVM 与属性变更广播

- 所有 ViewModel 继承 `ObservableObject`（CommunityToolkit.Mvvm），通过 `[ObservableProperty]` / `[RelayCommand]` 生成样板代码
- `partial void OnXxxChanged(value)` 用于在属性变化时同步到内部 Model 对象（如 `WorkspaceViewModel.OnNameChanged` → `workspace.Name = value`）
- `TerminalSession.RawOutputReceived / OutputReceived / Redraw` 等事件驱动 `TerminalControl` 重绘
- `NotificationService.UnreadCountChanged` 事件回写到各 WorkspaceViewModel

---

## 7. 数据持久化

### 7.1 存储根目录

所有用户态数据位于 `%USERPROFILE%/.ecodex/`：

| 文件 / 目录 | 作用 | 写入策略 |
|---|---|---|
| `session.json` | 会话状态（窗口 + 项目 + Surface + 分屏 + 面板快照） | 原子写 |
| `settings.json` | `ECodeXSettings`（含 `AgentSettings`） | 原子写 |
| `snippets.json` | 代码片段 | 原子写；首次启动播种 10 条默认 |
| `secrets.json` | DPAPI 加密的 API Key 字典 | 原子写 |
| `logs/YYYY-MM-DD.jsonl` | 命令日志（按日 JSONL） | `File.AppendAllText` |
| `logs/terminal/YYYY-MM-DD/HHmmss_<reason>_<ws>_<surface>_<pane>.log` | 终端脚本捕获 | 按需写入 |
| `agent/threads.json` + `agent/threads/<id>.jsonl` | Agent 会话线程索引与消息 | 索引原子写，消息追加 |
| `daemon-debug.log` | 守护进程 + 客户端诊断日志 | 共享追加（`FileShare.ReadWrite`） |

> **原子写**：`File.WriteAllText(tmp); File.Move(tmp, dest, overwrite: true);`，避免半截文件。

### 7.2 `SessionState` 形状（与源码一致）

```jsonc
{
  "version": 1,
  "selectedWorkspaceIndex": 0,
  "window": { "x": 100, "y": 80, "width": 1280, "height": 800,
              "isMaximized": false, "sidebarWidth": 280,
              "sidebarVisible": true, "compactSidebar": false },
  "workspaces": [
    {
      "id": "…", "name": "My Project", "iconGlyph": "\uE8A5",
      "accentColor": "#FF818CF8", "workingDirectory": "C:\\…",
      "selectedSurfaceIndex": 0,
      "surfaces": [
        {
          "id": "…", "name": "Terminal 1",
          "focusedPaneId": "pane-…",
          "paneCustomNames": { "pane-…": "Build" },
          "paneSnapshots": {
            "pane-…": {
              "capturedAt": "2026-…",
              "workingDirectory": "C:\\…",
              "shell": "pwsh.exe",
              "commandHistory": ["git status", "…"],
              "bufferSnapshot": {
                "cols": 120, "rows": 30,
                "cursorRow": 12, "cursorCol": 0,
                "scrollbackLines": ["…"],
                "screenLines": ["…"]
              }
            }
          },
          "rootNode": { "isLeaf": false, "direction": "Vertical",
                        "splitRatio": 0.5,
                        "first": { … }, "second": { … } }
        }
      ]
    }
  ]
}
```

### 7.3 主题

- 内置主题：`Default Dark / Dracula / Nord / Solarized Dark / One Dark / Monokai / Tokyo Night / Catppuccin Mocha`
- 兼容 Ghostty：`GhosttyConfigReader` 解析 `%USERPROFILE%\.config\ghostty\config` 与 `%APPDATA%\ghostty\config`
- `TerminalThemes.GetEffective(settings)` 在用户开启 `UseCustomTerminalColors` 时叠加自定义 BG/FG/Cursor/Selection

---

## 8. 快捷键（来自 `MainWindow.xaml.cs` 与 README）

| 类别 | 快捷键 | 动作 |
|---|---|---|
| 项目 | `Ctrl+N` | 新建项目 |
|  | `Ctrl+1..8` | 跳转到第 1..8 个项目 |
|  | `Ctrl+9` | 跳转到最后一个 |
|  | `Ctrl+Shift+W` | 关闭项目 |
|  | `Ctrl+Shift+R` | 重命名项目 |
|  | `Ctrl+B` | 切换侧边栏 |
| Surface | `Ctrl+T` | 新建 Surface（标签页） |
|  | `Ctrl+W` | 关闭 Surface |
|  | `Ctrl+Shift+]` / `[` | 下一个 / 上一个 |
|  | `Ctrl+Tab` / `Ctrl+Shift+Tab` | 循环切换 |
| Pane | `Ctrl+D` / `Ctrl+Shift+D` | 向右 / 向下分屏 |
|  | `Ctrl+Alt+↑↓←→` | 方向聚焦面板 |
|  | `Ctrl+Shift+Z` | 缩放 / 还原 |
| 效率 | `Ctrl+Shift+P` | 命令面板 |
|  | `Ctrl+Shift+F` | 搜索浮层 |
|  | `Ctrl+Shift+L` | 命令日志窗口 |
|  | `Ctrl+Shift+V` | Session Vault |
|  | `Ctrl+Alt+H` | 命令历史选择器 |
|  | `Ctrl+I` | 切换通知面板 |
|  | `Ctrl+Shift+U` | 跳转到最新未读通知 |
|  | `Ctrl+,` | 设置 |

---

## 9. 进程与资源约束

| 项 | 默认 / 行为 |
|---|---|
| 主程序默认 Shell | pwsh → powershell → cmd |
| 守护进程单实例 | `Global\ECodeXDaemon` Mutex |
| 守护进程空闲退出 | 24 小时无客户端 / 无活动会话 |
| 守护进程连接超时 | 初始 `TryConnect` 300ms；`StartDaemonAndConnect` 最多 20 次，每次连接超时 1000ms，尝试间隔 500ms |
| 守护进程请求超时 | 3 秒（`SendRequestAsync` 内置 `CancellationTokenSource`） |
| 命令日志条数上限 | 内存中 5000；磁盘按日 JSONL |
| 通知条数上限 | 内存中 500 |
| 回滚行数 | `ECodeXSettings.ScrollbackLines`（默认 10000） |
| 快照回滚 | 保存 3000 行 |
| 缓冲区快照范围 | 恢复时 20000 行可导出 |
| `PANE.READ` 单次返回 | `lines ∈ [1, 5000]`、`maxChars ∈ [512, 200000]` |

---

## 10. 性能要点

- **终端渲染**：WPF `DrawingVisual` + 自管理 `Typeface`/画刷缓存；单元格脏标记驱动局部重绘
- **光标闪烁**：`DispatcherTimer` 周期 530ms（`ECodeXSettings.CursorBlinkMs`）
- **回滚缓冲**：`ScrollbackBuffer<T>` 循环数组（O(1) `Add`，避免 `List.RemoveAt(0)` 的 O(n)）
- **IPC**：单客户端一个 `Channel<string>` + 写线程，避免多个事件并发写管道
- **诊断日志**：`%USERPROFILE%/.ecodex/daemon-debug.log` 共享追加（`FileShare.ReadWrite`）

---

## 11. 安全考虑

- **DPAPI 加密**：API Key 通过 `ProtectedData.Protect(..., CurrentUser)` 存于 `secrets.json`
- **命名管道**：使用当前用户上下文；`ecodex` 与 `ecodex-daemon` 两套管道名（默认均无 `tag`）
- **命令白名单**：`MainViewModel.HandlePipeCommand` 仅分派已知命令（`NOTIFY / WORKSPACE.* / SURFACE.* / SPLIT.* / PANE.* / STATUS`）；未知命令返回 JSON `{ error }`
- **命令日志脱敏**：见 §6.6
- **崩溃处理**：`App.OnStartup` 注册 `DispatcherUnhandledException` 与 `AppDomain.UnhandledException`，仅显示错误并继续

---

## 12. 部署形态

| 模式 | 命令 | 大小 | 运行时依赖 |
|---|---|---|---|
| Framework-dependent | `dotnet publish … --self-contained false` | 最小 | 需要 .NET 10 Desktop Runtime |
| Self-contained | `dotnet publish … --self-contained true` | 较大 | 无（自带运行时） |
| Single-file | （README 提及，publish.ps1 注释指 WPF + ConPTY 与单文件配合不佳，故脚本未生成） | 较大 | 无 |
| CLI | `dotnet publish src/ECodeX.Cli … --self-contained true -o publish/ecodex-cli` | 较大 | 无；可放入 `PATH` |

`scripts/publish.ps1` 一键产出 Framework / Self-contained / CLI 三种产物到 `<repo>/publish/`。

---

## 13. 已规划但暂未实现 / 已知技术债

- 单文件发布与 WPF 资源 / ConPTY 互操作不完全兼容，README 提及但 `publish.ps1` 已规避
- 单元测试集中在 `ECodeX.Core`（`tests/ECodeX.Tests`），UI 层暂无单元测试
- ConPTY 烟雾测试 `tests/ECodeX.Smoke` 仅做集成级最小验证（环境注入）
- `AgentRuntimeService` 体积大（110KB+），承担 Agent / 工具调用 / 流式响应等逻辑，详见 `02-modules.md`

---

## 14. 参考

- `src/ECodeX.Core/Terminal/ConPtyInterop.cs` — ConPTY P/Invoke 绑定
- `src/ECodeX.Core/Terminal/VtParser.cs` — VT 解析器实现
- `src/ECodeX.Core/Models/SplitNode.cs` — 二叉树分屏模型
- `src/ECodeX.Daemon/DaemonPipeServer.cs` — 守护进程命名管道服务器
- `src/ECodeX.Core/IPC/DaemonMessages.cs` — IPC 协议定义
- `README.md` / `README.en.md` — 用户视角说明
