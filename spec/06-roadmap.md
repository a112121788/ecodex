# ECode 详细开发规划

> 本文档是 `spec/01-architecture.md` 到 `spec/05-cli-commands.md` 的产品与工程落地规划。
>
> 信息来源：
> 1. 当前 Windows 仓库源码：`src/ECode`、`src/ECode.Core`、`src/ECode.Cli`、`src/ECode.Daemon`、`tests/`、`scripts/`
> 2. 上游 macOS 原版：`manaflow-ai/ecode` README / TODO / CLI 目录 / 中文文档站
> 3. 上游公开文档：`https://ecode.com/zh-CN`、`/docs/getting-started`、`/docs/custom-commands`
>
> **范围声明**：本仓库只做 **Windows 原生版**，技术栈固定为 WPF + ConPTY + WebView2 + Named Pipe + .NET 10。本文不会规划 macOS / Linux 端实现。

---

## 0. 一句话目标

把当前 ECode 从“可用的 Windows 终端复用器原型”推进到“与 macOS ecode 核心体验对齐、面向 AI coding agents 的 Windows 原生开发工作台”。

最终用户应能：

1. 在 Windows 上用原生 WPF + ConPTY 获得稳定终端、多项目、多标签页和分屏体验。
2. 同时运行多个 Claude Code / Codex / OpenCode / Gemini / Copilot CLI 会话，并通过蓝环、侧栏、通知面板快速定位需要人工输入的会话。
3. 使用 `ecode.json` 把一个项目的常用布局、Agent 启动命令、浏览器预览和快捷按钮固化下来。
4. 通过 CLI / Named Pipe / v2 API 自动化创建项目、分屏、发送输入、读取输出、控制浏览器。
5. 在崩溃 / 重启后恢复窗口、布局、工作目录、终端滚动历史、浏览器 URL，并对支持的 agent 会话执行可审计的 resume。

---

## 1. 产品定位与设计原则

### 1.1 产品定位

ECode 不是一个新的 IDE，也不是强流程的 Agent 编排器，而是 Windows 上的一组可组合开发原语：

- 终端原语：ConPTY、scrollback、VT/OSC、快捷键、命令历史。
- 布局原语：项目、Surface、Pane、分屏树、缩放、重排。
- 通知原语：OSC 9/99/777、CLI notify、未读状态、跳转最新未读。
- 自动化原语：CLI、Named Pipe、短 ID、v2 JSON 协议。
- 浏览器原语：WebView2 browser pane、DOM 快照、click/fill/eval/screenshot。
- 恢复原语：布局快照、终端快照、resume binding、可信命令前缀。

### 1.2 非目标

| 非目标 | 原因 |
|---|---|
| 替代 VS Code / JetBrains | ECode 只提供 terminal/browser/control primitives，不管理编辑器工作流 |
| 复制 macOS 源码 | 上游是 Swift/AppKit/libghostty/WKWebView，Windows 端应采用 WPF/ConPTY/WebView2 的原生实现 |
| 做云端 VM 平台 | 上游 Founder's Edition 的 Cloud VMs 不属于当前开源 Windows 版范围 |
| 强绑定某一个 Agent | 支持 Claude/Codex/OpenCode 等多 agent，不把核心流程绑死在任一 vendor |
| 引入 Electron/Tauri | 与“原生 Windows 版”的定位冲突 |
| 在近期实现完整 remote daemon | `ecode ssh` 的 proxy broker / remote daemon 成本高，先列为远期评估项 |

### 1.3 工程原则

| 原则 | 要求 |
|---|---|
| 先稳定核心，再扩展体验 | M0/M1 先把终端、布局、通知、测试、发布打牢，再进入浏览器和 v2 API |
| 所有用户态持久化可审计 | `session.json`、`settings.json`、`logs/*.jsonl`、`agent/*.jsonl`、`resume.json` 均应可人工读懂 |
| 可回退 | 守护进程失败回退本地 ConPTY；WebView2 不存在时禁用浏览器；v2 API 不破坏 v1 CLI |
| 不自动执行不可信命令 | resume / ecode.json command / hooks setup 均需要信任模型或显式确认 |
| 优先可测试 | 新服务应进 `ECode.Core`，UI 只做绑定；协议先写 schema，再写实现 |
| Windows 原生优先 | DPAPI、Windows Toast、MSIX/Velopack、OpenSSH、WMI、ETW 可被优先利用 |

---

## 2. 当前能力基线

### 2.1 已实现能力清单

| 领域 | 已有能力 | 主要文件 |
|---|---|---|
| 终端引擎 | ConPTY 封装、Shell 启动、VT/CSI/OSC 解析、缓冲区、回滚、备用屏幕、宽字符、选择、URL 识别 | `ECode.Core/Terminal/*` |
| 布局 | Workspace、Surface、Pane、二叉树分屏、关闭面板、焦点切换、缩放 | `Models/SplitNode.cs`、`ViewModels/*` |
| UI | 主窗口、侧边栏、标签栏、命令面板、通知面板、日志窗口、历史窗口、设置窗口、Session Vault | `src/ECode/Views/*`、`Controls/*` |
| 通知 | OSC 9/99/777、CLI notify、未读计数、Toast | `OscHandler.cs`、`NotificationService.cs`、`ToastNotificationHelper.cs` |
| 命令日志 | OSC 133 A/B/C/D、命令开始/完成、脱敏、按日 JSONL、脚本捕获 | `CommandLogService.cs` |
| 持久化 | `session.json` 布局与终端快照、窗口位置、侧边栏状态 | `SessionPersistenceService.cs` |
| 守护进程 | `ecode-daemon.exe` 托管 `TerminalSession`、attach、snapshot、事件广播 | `src/ECode.Daemon/*`、`DaemonClient.cs` |
| CLI | `notify / workspace / surface / split / status` | `src/ECode.Cli/Program.cs` |
| 设置 | 外观、终端、行为、Agent、ShellProfiles、KeyBindings | `ECodeSettings.cs`、`SettingsWindow.xaml.cs` |
| Agent | Agent 会话存储、OpenAI/Anthropic 兼容运行时、工具调用、上下文压缩 | `AgentRuntimeService.cs`、`AgentConversationStoreService.cs` |
| 安全 | DPAPI 密钥、命令/脚本脱敏 | `SecretStoreService.cs`、`CommandLogService.cs` |
| 测试 | xUnit core tests、ConPTY smoke | `tests/ECode.Tests`、`tests/ECode.Smoke` |
| 发布 | Framework-dependent / Self-contained / CLI | `scripts/publish.ps1` |

### 2.2 与 macOS 原版的能力差距

| macOS 原版能力 | 上游描述 | Windows 状态 | 优先级 | 处理策略 |
|---|---|---|---|---|
| Notification rings | Pane 蓝环 + sidebar/tab 高亮 | 属性存在但视觉不完整 | P0 | M1 实现视觉闭环 |
| Notification panel | 一处查看未读并跳转 | 已有基础 | P0 | M1 加右键、排序、键盘导航 |
| Vertical + horizontal tabs | 侧栏展示 branch/cwd/port/notification | 大部分已有 | P0 | M1 补 ports display、PR 状态占位 |
| In-app browser | 终端旁 split browser + agent-browser API | `BrowserControl` 仅辅助 Vault | P1 | M3/M4 分两阶段 |
| Browser import | 导入 Chrome/Firefox/Arc sessions | 缺失 | P2 | M4 后半或 M6 |
| Custom commands | `ecode.json` commands/actions/layout/buttons | M1 command 子集已接入命令面板 | P0 | M3 browser surface，M5 v2 |
| Scriptable socket API | 创建 workspace/pane、send keys、browser automation | v1 CLI 很窄 | P0 | M5 v2 协议 |
| SSH workspace | `ecode ssh` + remote browser route | 缺失 | P3 | 远期评估，不进主线 |
| Claude Code Teams | 一键 teammate 模式 | 缺失 | P2 | M6 hooks/integrations |
| Session restore | layout/history/browser/resume hooks | 仅布局 + terminal snapshot | P0 | M2 增强 resume |
| Hooks setup | Claude/Codex/OpenCode 等 | 缺失 | P1 | M6 |
| Short refs | `workspace:N` / `pane:N` | 仅 index/id/name | P1 | M5 |
| Multi-window API | `window.list/current/focus/create/close` | 缺失 | P2 | M5 |
| Auto update | Sparkle | 缺失 | P2 | M6 Velopack |

### 2.3 Windows 特色增强机会

| Windows 能力 | 使用方向 | 里程碑 |
|---|---|---|
| DPAPI | API key、browser import cookie 解密、resume approvals 保护 | M2/M4/M6 |
| Windows Toast | Toast button / Action Center deep link 到 workspace/pane | M1/M6 |
| WebView2 | Browser pane、automation API、cookie/storage、DevTools | M3/M4 |
| OpenSSH | 轻量 SSH profile，远期 remote workspace | M6+ |
| WMI / netstat | 更准确的 process tree、ports、agent detection | M1/M2 |
| MSIX / Velopack | 安装、更新、卸载 | M6 |
| ETW | 远期性能诊断 | M7+ |
| Windows Terminal settings | 主题 / profile 导入 | M1/M6 |

---

## 3. 版本节奏

### 3.1 版本线

| 版本 | 目标 | 对应里程碑 | 发布类型 |
|---|---|---|---|
| `0.1.x` | 工程稳定、测试、发布脚本、已知 crash 修复 | M0 | 内部 / nightly |
| `0.2.x` | UI/通知体验对齐、`ecode.json` 基础 | M1 | preview |
| `0.3.x` | 会话恢复增强、resume binding、环境变量注入 | M2 | preview |
| `0.4.x` | 浏览器面板基础 | M3 | preview |
| `0.5.x` | 浏览器脚本化 API | M4 | beta |
| `0.6.x` | v2 API、多窗口、短 ID | M5 | beta |
| `0.7.x` | hooks、自动更新、安装器 | M6 | release candidate |
| `1.0.0` | Windows 版稳定发布 | M7 + 缺陷收敛 | stable |

### 3.2 每个迭代的固定输出

每两周一个迭代，每个迭代必须产出：

1. `CHANGELOG.md` 条目：新增 / 修复 / 破坏性变更 / 已知问题。
2. `spec/06-roadmap.md` 勾选更新或新增备注。
3. 至少一个可运行验证脚本：`scripts/verify-<feature>.ps1` 或测试用例。
4. 一组截图或短录屏（UI 相关迭代）。
5. 风险登记更新：新增风险、关闭风险、已接受风险。

### 3.3 发布通道

| 通道 | 触发 | 内容 | 适用人群 |
|---|---|---|---|
| local dev | 手动 `dotnet run` | Debug build | 开发者 |
| nightly | main 每日构建 | Self-contained + CLI | 早期用户 |
| preview | 每个里程碑结束 | signed installer + zip | 内测用户 |
| stable | `1.0.0` 后按需 | installer + update feed | 普通用户 |

---

## 4. 依赖关系矩阵

| 能力 | 依赖 | 被依赖方 |
|---|---|---|
| M0 CI / tests | 现有源码 | 全部后续里程碑 |
| M1 ecode.json 基础 | `CommandPalette`、`SplitNode`、`TerminalSession` | M3 browser layout、M6 hooks |
| M1 notification rings | `NotificationService`、`TerminalControl` | M6 agent hooks |
| M2 resume binding | `SessionPersistenceService`、`TerminalProcess`、`SecretStoreService` | M6 hooks setup、M7 stable |
| M3 browser pane | WebView2、`SplitPaneContainer`、M1 ecode.json layout | M4 browser automation |
| M4 browser automation | M3 browser pane、v2 command contracts | M5 v2 API、agent browser skill docs |
| M5 v2 protocol | M0 tests、M1/M3 object model | M6 hooks、external integrations |
| M6 updater/installer | M0 publish scripts | stable release |
| M7 docs/ecosystem | M0-M6 outputs | 1.0 adoption |

---

## 5. 详细里程碑

## M0 - 工程基线与可靠性

### M0.1 目标

让项目能被稳定构建、测试、发布，并把“当前原型能否安全迭代”这个问题回答清楚。

M0 不追求新功能，重点是：

- 建立 CI 与本地一键验证。
- 增补 core 单元测试，先覆盖高风险底层逻辑。
- 统一版本号、日志、产物校验。
- 明确发布脚本与 smoke 测试。

### M0.2 详细任务

| ID | 任务 | 说明 | 涉及文件 | 验收 |
|---|---|---|---|---|
| M0-T01 | 新增 `scripts/ci.ps1` | 串联 restore/build/test/smoke/publish dry-run | `scripts/ci.ps1` | `pwsh scripts/ci.ps1` 本地通过 |
| M0-T02 | 新增 GitHub Actions CI | Windows runner，跑 build + tests | `.github/workflows/ci.yml` | PR 自动校验 |
| M0-T03 | 统一版本源 | 从 `ECode.csproj` Version 读取到 CLI 与 STATUS | `ECode.Cli/Program.cs`、`MainViewModel.cs` | `ecode version` 与 `STATUS.version` 一致 |
| M0-T04 | 守护进程可观测性 | 标准化 `daemon-debug.log` 字段：component/event/paneId（已实现） | `DaemonClient.cs`、`DaemonPipeServer.cs` | 日志可被 grep 追踪一次 attach |
| M0-T05 | 增补 VT parser tests | UTF-8 跨包、OSC ST、CSI private modes、invalid sequence | `tests/ECode.Tests/CoreTests.cs` | 新增 ≥20 个 parser 测试 |
| M0-T06 | 增补 TerminalBuffer tests | 宽字符、alternate screen、scroll region、snapshot roundtrip | 同上 | 新增 ≥20 个 buffer 测试 |
| M0-T07 | 增补 SplitNode tests | remove/swap/resize/equalize/factory layout | 同上 | 覆盖所有 public method |
| M0-T08 | 增补 IPC DTO tests | `DaemonRequest/Response/Event` 序列化兼容 | 同上 | DTO roundtrip 通过 |
| M0-T09 | 发布产物校验 | SHA256、文件存在、大小阈值、exe version | `scripts/publish.ps1` | 产物末尾输出校验表 |
| M0-T10 | 文档索引 | `spec/README.md` 指向 01-06 文档 | `spec/README.md` | 新贡献者可从 spec 进入 |

### M0.3 实现要点

版本统一建议：

```csharp
var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
```

`STATUS` 返回建议扩展为：

```jsonc
{
  "version": "0.2.0",
  "protocol": "v1",
  "workspaces": 3,
  "selectedWorkspace": "...",
  "daemonConnected": true,
  "unreadNotifications": 2
}
```

发布校验建议输出：

```text
Artifact                 Exists  SizeMB  SHA256
ecode-win-x64/ecode-app.exe   yes     5.4     ...
ecode-win-x64-sc/ecode-app.exe yes    82.1    ...
ecode-cli/ecode.exe        yes     72.8    ...
```

### M0.4 验收门槛

- `dotnet build ECode.sln -c Debug` 通过。
- `dotnet test tests/ECode.Tests/ECode.Tests.csproj` 通过。
- `dotnet run --project tests/ECode.Smoke/ECode.Smoke.csproj` 在 Windows 机器上通过。
- `scripts/publish.ps1 -Flavor All` 可生成 3 类产物。
- 新增测试不引入 flakiness；CI 失败必须阻塞合并。

### M0.5 风险

| 风险 | 缓解 |
|---|---|
| 目前会话环境是 macOS，无法直接跑 WPF/ConPTY | CI 使用 `windows-latest`，本地 macOS 只做文档与静态检查 |
| `.NET 10` runner 可用性不稳定 | CI 先明确 `actions/setup-dotnet` 安装 SDK 版本 |
| WebView2 测试在 headless CI 不稳定 | M0 不测 WebView2，只检测项目构建 |

---

## M1 - UI/UX 与 ecode.json 基础

### M1.1 目标

对齐 macOS ecode 的可见核心体验：

- Agent 需要关注时，用户能从 pane、surface tab、workspace sidebar 三层看到提示。
- 命令面板可以读取项目配置，提供项目级动作。
- 基础交互补齐：拖拽重排、文件拖入、右键菜单、设置入口。

### M1.2 功能包

#### M1-A 通知视觉闭环

| ID | 任务 | 说明 | 文件 |
|---|---|---|---|
| M1-A01 | Pane 蓝环绘制 | `TerminalControl.HasNotification=true` 时绘制高亮边框 | `TerminalControl.cs` |
| M1-A02 | Surface tab 未读点 | 标签标题旁显示 unread dot / glow | `SurfaceTabBar.xaml(.cs)` |
| M1-A03 | Workspace sidebar 未读态 | 侧栏 item 显示 unread count、latest notification text | `WorkspaceSidebarItem.xaml(.cs)` |
| M1-A04 | Jump-to-unread 动画 | `Ctrl+Shift+U` 跳转后闪烁目标 pane | `TerminalControl.cs`、`MainWindow.xaml.cs` |
| M1-A05 | 通知排序修正 | 标记未读时推到列表顶部或保持“最新未读”队列 | `NotificationService.cs` |

建议视觉规则：

| 状态 | Pane | Surface tab | Workspace sidebar |
|---|---|---|---|
| 无通知 | 无边框 | 普通 | 普通 |
| 有未读 | 2px 蓝色光环 + 轻微阴影 | 蓝点 / 蓝底 | badge + latest text |
| 当前聚焦且未读 | 蓝环 + 焦点边框叠加 | 高亮 | 高亮 |
| 已读 | 去除蓝色 | 去除蓝点 | 计数减少 |

#### M1-B 交互补齐

| ID | 任务 | 说明 | 文件 |
|---|---|---|---|
| M1-B01 | Surface 拖拽重排 | `DragDrop.DoDragDrop` + insertion indicator | `SurfaceTabBar.xaml.cs` |
| M1-B02 | Workspace 重排 | 侧栏拖拽排序 | `MainWindow.xaml.cs`、`WorkspaceSidebarItem.xaml.cs` |
| M1-B03 | 文件拖入终端 | drop 文件时写入 quoted path | `TerminalControl.cs` |
| M1-B04 | 图片拖入远期占位 | 本期仅写入路径，不做 scp upload | `TerminalControl.cs` |
| M1-B05 | Close active tab 按钮常显 | 当前 active surface close 按钮不只 hover 出现（已实现） | `SurfaceTabBar.xaml` |
| M1-B06 | 设置面板重排 | 按“外观 / 终端 / 行为 / 集合 / Agent / 高级”重排，并新增“自定义命令”页 | `SettingsWindow.xaml` |

#### M1-C `ecode.json` 基础

上游文档路径：

1. 项目本地：`./.ecode/ecode.json`
2. 本地回退：`./ecode.json`
3. 全局：`~/.config/ecode/ecode.json`

Windows 路径映射：

1. `<workspace cwd>\.ecode\ecode.json`
2. `<workspace cwd>\ecode.json`
3. `%USERPROFILE%\.config\ecode\ecode.json`

M1 只实现子集：

```jsonc
{
  "commands": [
    {
      "name": "Run Tests",
      "description": "Run project tests",
      "keywords": ["test", "check"],
      "command": "dotnet test",
      "confirm": true
    }
  ],
  "actions": {
    "codex": {
      "type": "command",
      "title": "Codex",
      "command": "codex",
      "target": "newTabInCurrentPane",
      "palette": true
    }
  }
}
```

当前实现状态（Windows 版）：

- 已支持 JSONC 注释与尾随逗号。
- 已支持读取 `%USERPROFILE%\.config\ecode\ecode.json`、`<workspace cwd>\.ecode\ecode.json`、`<workspace cwd>\ecode.json`。
- 已支持全局 / 本地合并；本地同名 command 或 action 覆盖全局定义。
- 已支持在 `Ctrl+Shift+P` 命令面板展示 `commands` 与 `actions`（`type:"command"` 且 `palette:true`）。
- 已支持 `confirm:true` 弹窗确认、`currentTerminal` / `newTabInCurrentPane` 两种目标执行。
- 已支持 CLI `ecode reload-config` 与 `Ctrl+Shift+,` 热重载；命令面板打开时会保留搜索词并刷新配置项。

| ID | 任务 | 说明 | 文件 |
|---|---|---|---|
| M1-C01 | 新增 DTO | `EcodeJsonConfig / EcodeCommand / EcodeAction`（已实现） | `ECode.Core/Models/EcodeJsonConfig.cs` |
| M1-C02 | 解析服务 | 路径搜索、JSON 解析、全局/本地 merge（已实现） | `ECode.Core/Services/EcodeJsonService.cs` |
| M1-C03 | schema error | 失败时返回可显示错误项（已实现：命令面板诊断项） | 同上 |
| M1-C04 | 命令面板接入 | `CommandPalette` 数据源追加 custom commands（已实现） | `CommandPalette.xaml.cs`、`MainWindow.xaml.cs` |
| M1-C05 | 执行动作 | `currentTerminal` 或 `newTabInCurrentPane`（已实现） | `MainWindow.xaml.cs` |
| M1-C06 | reload config | CLI `ecode reload-config` + `Ctrl+Shift+,`（已实现） | `Program.cs`、`MainWindow.xaml.cs` |

### M1.3 实现建议

新增模型：

```csharp
public sealed class EcodeJsonConfig
{
    public Dictionary<string, EcodeAction> Actions { get; set; } = [];
    public List<EcodeCommand> Commands { get; set; } = [];
    public EcodeUiConfig? Ui { get; set; }
}

public sealed class EcodeAction
{
    public string Type { get; set; } = "command";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? Command { get; set; }
    public string Target { get; set; } = "currentTerminal";
    public bool Palette { get; set; } = true;
    public bool Confirm { get; set; }
}
```

执行路径：

```text
CommandPalette item selected
  -> MainWindow executes action
  -> if confirm then MessageBox
  -> if target=currentTerminal then focused session.Write(command + CR)
  -> if target=newTabInCurrentPane then create surface then focused session.Write(command + CR)
```

### M1.4 验收

- OSC 通知进入后，pane/tab/workspace 三层都显示未读态。
- `Ctrl+Shift+U` 能跳转到目标 pane 并清除该通知未读。
- `.ecode/ecode.json` 简单命令出现在命令面板；schema 错误在命令面板可见。
- `ecode reload-config` / `Ctrl+Shift+,` 能重新读取配置，并刷新已打开的命令面板。
- 文件拖入终端会输入正确 quoted path。
- Surface 重排后 session 不丢失。

### M1.5 测试

| 测试 | 类型 |
|---|---|
| `NotificationService_MarkUnread_MovesLatestUnreadCorrectly` | xUnit |
| `EcodeJsonService_MergesLocalOverGlobal` | xUnit |
| `EcodeJsonService_InvalidSchema_ReturnsDiagnostic` | xUnit |
| `EcodeJsonService_SupportsJsonCommentsAndTrailingCommas` | xUnit |
| `SplitNode_SurfaceReorder_DoesNotChangePaneIds` | xUnit |
| 手测：OSC 777 notify → 蓝环 → jump unread | smoke script |

---

## M2 - 会话恢复增强

### M2.1 目标

上游文档强调：ecode 只恢复 app-owned state，不 checkpoint 任意 live process；对支持 agent 通过 hooks/native session id 恢复。Windows 版应采取同样原则：

- 始终恢复布局、cwd、scrollback。
- 只有可信 resume binding 才自动执行命令。
- 未可信命令只能显示为待恢复项，让用户手动确认。

### M2.2 新增数据模型

建议新增 `%USERPROFILE%/.ecode/resume.json`：

```jsonc
{
  "version": 1,
  "bindings": [
    {
      "id": "...",
      "workspaceId": "...",
      "surfaceId": "...",
      "paneId": "...",
      "kind": "agent|tmux|custom",
      "checkpoint": "work",
      "shell": "codex resume abc123",
      "workingDirectory": "C:\\repo",
      "environment": { "SAFE_KEY": "value" },
      "trusted": true,
      "trustReason": "user-approved-prefix",
      "approvedPrefix": "codex resume",
      "createdAtUtc": "...",
      "updatedAtUtc": "..."
    }
  ]
}
```

### M2.3 任务拆解

| ID | 任务 | 说明 | 文件 |
|---|---|---|---|
| M2-T01 | `ResumeBinding` DTO | 数据模型 + JSON serializer（已实现） | `ECode.Core/Models/ResumeBinding.cs` |
| M2-T02 | `ResumeBindingService` | Load/Save/Add/Remove/FindForSurface/TrustPrefix（已实现） | `ECode.Core/Services/ResumeBindingService.cs` |
| M2-T03 | 敏感环境剔除 | TOKEN/PASSWORD/SECRET/API_KEY 等丢弃（已实现） | 同上 |
| M2-T04 | `ECODE_WORKSPACE_ID` 注入 | 启动 shell 时附带 env | `TerminalProcess.cs` |
| M2-T05 | CLI surface resume | `ecode surface resume set/show/clear` | `ECode.Cli/Program.cs`、`MainViewModel.cs` |
| M2-T06 | 自动恢复开关 | `AutoResumeAgentSessions` | `ECodeSettings.cs`、`SettingsWindow.xaml` |
| M2-T07 | 恢复确认 UI | 未信任 binding 显示提示条 | `SplitPaneContainer.cs` 或 `TerminalControl` overlay |
| M2-T08 | Agent hook mapping | 为 Codex/Claude/OpenCode 预留 session id 存储 | `AgentConversationStoreService` 或新服务 |

### M2.4 信任模型

自动执行 resume 命令必须满足任一条件：

1. 来源是 ecode 内置可信检测（如当前 live process 检测到 tmux session，并生成 `tmux attach -t X`）。
2. 用户显式批准过该命令前缀，且 cwd 与环境摘要一致。
3. 命令来自已信任的 `.ecode/ecode.json` action fingerprint。

fingerprint 建议：

```text
SHA256(kind + shell + cwd + sortedSafeEnv + configPath + commandId)
```

### M2.5 验收

- 关闭并重开后，未审批 resume 不自动运行，只显示“可恢复”。
- 用户批准 prefix 后，再次重开自动运行。
- `ecode surface resume show --json` 可看到 binding。
- 敏感 env 不写入 `resume.json`。
- `AutoResumeAgentSessions=false` 时所有 agent resume 均不自动执行。

### M2.6 测试

| 测试 | 类型 |
|---|---|
| `ResumeBindingService_DropsSensitiveEnv` | xUnit |
| `ResumeBindingService_TrustPrefixRequiresCwdMatch` | xUnit |
| `ResumeBindingService_RoundTripJson` | xUnit |
| `SurfaceResume_SetShowClear_CliContract` | CLI contract test |
| 手测：创建 binding → 重开 → 确认 → 再重开自动运行 | smoke |

---

## M3 - 浏览器面板基础

### M3.1 目标

让浏览器成为与终端同级的 Surface 类型，而不是只服务 Session Vault 的窗口。

用户场景：

- 在终端右侧打开 `http://localhost:3000`。
- 在 `.ecode/ecode.json` 里定义“前端 dev + 浏览器预览 + shell”的三栏布局。
- 浏览器 URL / title / history 随 session restore 恢复。

### M3.2 数据模型调整

当前 `Surface` 默认代表 terminal surface。M3 需要抽象 Surface 类型。

保守方案：保留现有 `Surface`，新增字段：

```csharp
public enum SurfaceKind { Terminal, Browser }

public class Surface
{
    public SurfaceKind Kind { get; set; } = SurfaceKind.Terminal;
    public string? BrowserUrl { get; set; }
    public string? BrowserTitle { get; set; }
    public List<string> BrowserHistory { get; set; } = [];
    // existing terminal fields remain
}
```

优点：最少改动；SessionState 兼容。缺点：terminal/browser 字段混杂。

推荐 M3 使用保守方案，M5 以后若需要再抽接口。

### M3.3 任务拆解

| ID | 任务 | 说明 | 文件 |
|---|---|---|---|
| M3-T01 | `SurfaceKind` | Terminal/Browser enum | `Models/Surface.cs` |
| M3-T02 | SessionState 扩展 | `kind/browserUrl/browserTitle/browserHistory` | `Models/SessionState.cs`、`SessionPersistenceService.cs` |
| M3-T03 | `BrowserPaneViewModel` | URL、Title、CanGoBack、CanGoForward、Loading | `src/ECode/ViewModels/BrowserPaneViewModel.cs` |
| M3-T04 | `BrowserControl` 升级 | 地址栏、back/forward/reload/devtools | `Controls/BrowserControl.xaml(.cs)` |
| M3-T05 | SplitPaneContainer 支持 browser | `BuildLeaf` 分支 | `Controls/SplitPaneContainer.cs` |
| M3-T06 | CLI browser open | `ecode browser open|open-split|new <url>` | `ECode.Cli/Program.cs`、`MainViewModel.cs` |
| M3-T07 | ecode.json browser surface | `type:"browser"` parser | `EcodeJsonService.cs` |
| M3-T08 | 持久化恢复 | 重开后恢复 URL 与 history | `SessionPersistenceService.cs` |

### M3.4 CLI 草案

```powershell
ecode browser open https://example.com
ecode browser open-split http://localhost:3000 --direction right
ecode browser new http://localhost:5173 --workspace workspace:1
```

对应 IPC：

```jsonc
{
  "command": "BROWSER.OPEN_SPLIT",
  "args": {
    "url": "http://localhost:3000",
    "direction": "right",
    "workspaceRef": "workspace:1",
    "paneRef": "pane:1"
  }
}
```

M3 可先走 v1 文本参数，M5 再统一 v2。

### M3.5 验收

- `ecode browser open-split http://localhost:3000` 在当前 pane 右侧打开浏览器。
- 浏览器可后退、前进、刷新、打开 DevTools。
- `.ecode/ecode.json` workspace layout 包含 browser surface 可创建成功。
- 关闭重开后恢复浏览器 URL。
- WebView2 未安装时显示友好提示，不崩溃。

### M3.6 风险

| 风险 | 缓解 |
|---|---|
| WPF WebView2 与当前窗口样式/透明度冲突 | BrowserControl 不放透明层；必要时禁用窗口全局 opacity 对 WebView2 区域影响 |
| Browser pane 与 terminal keybindings 冲突 | Browser 获焦时应用级快捷键白名单化；终端专属快捷键不拦截 |
| Session Vault 与 Live Browser 混用 | 通过 `BrowserSurfaceMode.Live/Vault` 隔离 |

---

## M4 - 浏览器脚本化 API

### M4.1 目标

对齐上游“从 agent-browser 移植的脚本化 API”：Agent 可以读取页面可访问树、定位元素、点击、填表、执行 JS、截图、读 console、管理 cookies/storage。

### M4.2 API 分层

| 层 | 职责 |
|---|---|
| CLI | `ecode browser snapshot/click/fill/eval/screenshot/...` |
| IPC v2 | 请求/响应 schema，明确 `ok/error/data` |
| BrowserRegistry | 用 `surfaceRef` 找到 `BrowserPaneViewModel` / WebView2 |
| BrowserScriptingService | DOM 注入、refs 管理、动作执行、错误诊断 |
| WebView2 | `ExecuteScriptAsync`、DevTools protocol、CookieManager、DownloadStarting |

### M4.3 v2 响应约定

```jsonc
{
  "ok": true,
  "data": { "snapshot": "...", "refs": { "e1": "button[name=Submit]" } },
  "meta": { "surfaceRef": "surface:2", "durationMs": 43 }
}
```

错误：

```jsonc
{
  "ok": false,
  "error": {
    "code": "not_found",
    "message": "No element matched text 'Submit'",
    "hint": "Try browser.snapshot and use a ref",
    "sample": ["button: Save", "link: Cancel"],
    "snapshotExcerpt": "..."
  }
}
```

### M4.4 API 清单

| API | 说明 | M4 范围 |
|---|---|---|
| `browser.snapshot` | 返回可访问树文本 + refs | 必做 |
| `browser.find.role/text/label/placeholder/alt/title/testid` | 定位元素 | 必做 |
| `browser.find.first/last/nth` | refs 集合选择 | 必做 |
| `browser.click` | 点击元素 | 必做 |
| `browser.fill` | 输入或清空文本 | 必做 |
| `browser.hover` | hover 元素 | 必做 |
| `browser.press` | 键盘输入 | 必做 |
| `browser.eval` | 执行 JS | 必做 |
| `browser.screenshot` | PNG 截图保存 | 必做 |
| `browser.cookies.get/set/clear` | cookie 管理 | 必做 |
| `browser.storage.get/set/clear` | localStorage/sessionStorage | 必做 |
| `browser.console.list/clear` | console 消息 | 应做 |
| `browser.dialog.accept/dismiss` | JS dialog | 应做 |
| `browser.download.wait` | 下载等待 | 应做 |
| `browser.highlight` | 高亮元素 | 应做 |
| `browser.addinitscript/addscript/addstyle` | 注入脚本/样式 | 可做 |
| `browser.viewport/geolocation/offline/trace/network.route/screencast/input_mouse` | WebView2 平台限制或高成本 | 返回 `not_supported` |

### M4.5 refs 策略

- Snapshot 后生成短期 refs：`ref:1`, `ref:2` 或 `e1`, `e2`。
- refs 只在同一 browser surface 当前 document version 有效。
- 页面导航后 refs 失效，返回 `stale_ref`。
- refs 不写入磁盘，只存内存。

### M4.6 测试策略

| 测试组 | 内容 |
|---|---|
| P0 | snapshot、find text、click、fill、eval、screenshot |
| locator | role/text/label/placeholder/alt/title/testid/first/last/nth |
| diagnostics | not_found hint、sample、snapshotExcerpt |
| state | cookies/storage |
| unsupported | not_supported matrix |
| regression | `fill` 空字符串清空 input、snapshot-after、URL parsing |

### M4.7 验收

- 一个 Agent 可通过 CLI 完成：打开 localhost → snapshot → click 登录按钮 → fill 表单 → eval 状态。
- 错误诊断包含 hint 与 snapshot excerpt。
- 不支持的 API 明确 `not_supported`，不是 `method_not_found`。
- browser API 测试在 CI 可运行或被标记为 Windows-only integration test。

---

## M5 - v2 协议、多窗口与短 ID

### M5.1 目标

从“少量 CLI 命令”升级为稳定 automation surface，让 agent 和脚本能可靠控制 ecode，而不依赖当前焦点窗口。

### M5.2 v2 协议设计

建议使用同一 Named Pipe：`\\.\pipe\ecode`，通过首行 JSON 判断 v2。

v1：

```text
STATUS
PANE.WRITE text="..."
```

v2：

```jsonc
{
  "protocol": "ecode.v2",
  "id": "req-1",
  "method": "pane.write",
  "params": {
    "target": "pane:1",
    "text": "npm test",
    "submit": true
  }
}
```

响应：

```jsonc
{
  "id": "req-1",
  "ok": true,
  "result": { "pane": { "ref": "pane:1", "id": "..." } }
}
```

### M5.3 短 ID 与 ref 规则

| ref | 解析范围 | 示例 |
|---|---|---|
| `window:N` | 所有打开窗口 | `window:1` |
| `workspace:N` | 当前窗口或指定 window 下 | `workspace:2` |
| `surface:N` | 当前 workspace 下 | `surface:3` |
| `pane:N` | 当前 surface 下 leaves 顺序 | `pane:1` |

响应支持 `--id-format`：

- `refs`：只输出短 ref。
- `uuids`：只输出持久 UUID。
- `both`：两者都输出。

默认建议：CLI human 输出 `refs`，`--json` 输出 `both`。

### M5.4 API 清单

| family | methods |
|---|---|
| window | `window.list/current/focus/create/close` |
| workspace | `workspace.list/create/select/close/rename/reorder` |
| surface | `surface.list/create/select/close/rename/move/reorder/read/resume.*` |
| pane | `pane.list/focus/write/read/split/close/resize/swap/zoom` |
| browser | M4 全部 browser API |
| notification | `notification.list/read/unread/jump-latest/clear` |
| config | `config.reload/config.diagnostics` |
| status | `status`, `health` |

### M5.5 多窗口设计

新增 `WindowManagerService`：

```csharp
public sealed class WindowManagerService
{
    public IReadOnlyList<MainWindow> Windows { get; }
    public MainWindow? ActiveWindow { get; }
    public MainWindow CreateWindow(SessionState? state = null);
    public bool FocusWindow(string windowRefOrId);
    public bool CloseWindow(string windowRefOrId);
}
```

注意点：

- 守护进程应全局共享。
- CLI 管道只注册一次，由 `App` 分发到 `WindowManagerService`。
- `SelectedWorkspace` 不应默认等于用户正在看的窗口；v2 调用应显式指定 target 或由 `ECODE_WORKSPACE_ID` 推断。

### M5.6 验收

- `ecode --json window list` 返回所有窗口。
- `ecode pane write --workspace workspace:2 --pane pane:1 --text ...` 不影响当前用户焦点窗口。
- v1 命令仍可用。
- v2 返回稳定错误码：`invalid_ref / not_found / stale_ref / not_supported / timeout / internal_error`。

---

## M6 - Hooks、集成、安装与更新

### M6.1 目标

让普通 Windows 用户无需手工配置即可把 Claude/Codex/OpenCode 等 agent 接入 ecode 通知与恢复系统，并能通过安装器/自动更新稳定使用。

### M6.2 Hooks setup

命令草案：

```powershell
ecode hooks setup
ecode hooks setup claude
ecode hooks setup codex
ecode hooks setup --agent opencode
ecode hooks status
ecode hooks uninstall codex
```

支持矩阵：

| Agent | 检测方式 | Hook 写入方式 | 通知机制 | Resume 机制 |
|---|---|---|---|---|
| Claude Code | `where claude` | Claude config JSON / wrapper | `ecode notify` | native session id / wrapper |
| Codex | `where codex` | config / wrapper / PowerShell profile | `ecode notify` | `codex resume` if supported |
| OpenCode | `where opencode` | config | `ecode notify` | native resume if supported |
| Gemini | `where gemini` | wrapper | `ecode notify` | custom |
| Copilot CLI | `where gh` + extension | shell integration | `ecode notify` | custom |

M6 先支持 Claude / Codex / OpenCode 三个。

### M6.3 Windows 安装策略

| 方案 | 优点 | 缺点 | 建议 |
|---|---|---|---|
| zip + self-contained | 简单 | 无 PATH / 无卸载 / 无更新 | nightly 可用 |
| Inno Setup | 成熟、简单、可加 PATH | 更新需自建 | preview |
| MSIX | 系统集成好 | ConPTY / 文件权限需验证 | stable 可选 |
| Velopack | .NET 更新友好 | 需要 feed | stable 推荐 |

建议最终组合：

- nightly：zip + self-contained。
- preview/stable：Velopack installer。
- enterprise：MSIX 可选。

### M6.4 更新流程

1. GitHub Release 上传 `RELEASES` feed 与 nupkg / installer。
2. App 启动后后台检查版本。
3. 标题栏显示“有更新”。
4. 用户点击后下载并提示重启。
5. CLI `ecode update check/install` 可触发更新。

### M6.5 验收

- 新机器安装后 `ecode-app.exe` 可启动，`ecode.exe` 在 PATH 中可用。
- `ecode hooks setup codex` 能检测并写入 hook，失败时打印可操作错误。
- 卸载后 PATH 与 hook 可清理。
- 更新过程不丢 `%USERPROFILE%/.ecode` 数据。

---

## M7 - 生态、文档与 1.0 收敛

### M7.1 目标

让项目从“能用”变成“可维护、可贡献、可发布、可解释”。

### M7.2 文档结构

建议新增 `docs/`：

```text
docs/
├── index.md
├── installation.md
├── getting-started.md
├── keyboard-shortcuts.md
├── configuration.md
├── custom-commands.md
├── session-restore.md
├── browser-api.md
├── cli.md
├── troubleshooting.md
├── architecture.md
└── roadmap.md
```

`spec/` 保持工程内部设计，`docs/` 面向用户与贡献者。

### M7.3 贡献流程

| 文件 | 内容 |
|---|---|
| `CONTRIBUTING.md` | 环境、构建、测试、PR 流程 |
| `.github/PULL_REQUEST_TEMPLATE.md` | 修改范围、测试、截图、风险 |
| `.github/ISSUE_TEMPLATE/bug.yml` | OS、版本、日志、重现步骤 |
| `.github/ISSUE_TEMPLATE/feature.yml` | 场景、替代方案、兼容性 |
| `SECURITY.md` | 漏洞报告方式 |
| `CHANGELOG.md` | 用户可读变更记录 |

### M7.4 1.0 发布门槛

- P0 bug = 0，P1 bug <= 3 且有 workaround。
- `scripts/ci.ps1`、CI、发布脚本稳定。
- 核心能力：terminal/layout/notification/session restore/ecode.json/browser basic/v2 CLI 至少达到 beta 质量。
- docs 覆盖安装、配置、CLI、browser API、故障排查。
- 安装器 + 卸载 + 更新流程可用。

---

## 6. 横向设计专题

### 6.1 `ecode.json` 完整兼容路线

| 字段 | M1 | M3 | M5 | M6 |
|---|---|---|---|---|
| `commands[].command` | 支持 | 支持 | 支持 | 支持 |
| `commands[].workspace` | terminal layout 子集 | browser surface | refs + v2 | hooks templates |
| `actions` | command 子集 | browser builtin | shortcut/action registry | trusted fingerprint |
| `ui.surfaceTabBar.buttons` | 读取但不完全渲染 | 支持 browser button | 支持 refs | 支持 icon trust |
| `ui.newWorkspace.action` | 不做 | 可做 | 可做 | 可做 |
| `shortcut` | 不做或只显示 | 不做 | 可执行 | 完整 |
| `icon` | emoji/symbol 优先 | image path | trust locked image | 完整 |

### 6.2 安全模型

| 场景 | 风险 | 策略 |
|---|---|---|
| 执行 `ecode.json` command | 仓库恶意命令 | 首次执行按 action fingerprint 请求信任 |
| resume 自动执行 | 自动运行危险命令 | 只执行 trusted binding；敏感 env 丢弃 |
| browser automation | Agent 操作已登录页面 | API 调用只对当前用户显式打开的 browser surface 生效 |
| hooks setup | 修改用户配置 | 先 diff，用户确认；提供 uninstall |
| DPAPI secret | 密钥泄露 | CurrentUser 加密；UI 不回显完整 key |
| logs/transcripts | token 泄露 | 启动时 scrub + 写入前 scrub |

### 6.3 兼容策略

| 项 | 策略 |
|---|---|
| v1 CLI | 至少保留到 `1.x`，所有新增命令优先走 v2，但 v1 不破坏 |
| `session.json` | 增加 `version`，新字段默认值兼容旧文件 |
| `settings.json` | 新字段有默认值，旧设置不迁移也可启动 |
| `ecode.json` | 不支持字段给 schema warning，不直接崩溃 |
| WebView2 | 不可用时禁用 browser feature，核心 terminal 仍可用 |

### 6.4 性能预算

| 指标 | 目标 |
|---|---|
| 冷启动到可输入 | < 1.5s（无 restore），< 3s（restore 10 panes） |
| 单 pane 输出吞吐 | 不卡 UI，RawOutput 批量处理 |
| 内存 | 10 panes < 350MB（不含 WebView2），带 2 browser < 800MB |
| 命令面板打开 | < 100ms |
| `ecode status` | < 100ms |
| `browser.snapshot` | 普通页面 < 500ms |
| 保存 session | < 300ms（10 panes，每 pane 3000 snapshot lines） |

### 6.5 测试金字塔

| 层 | 工具 | 目标 |
|---|---|---|
| Unit | xUnit + FluentAssertions | Core model/service/parser/protocol |
| Contract | xUnit + fake MainViewModel/NamedPipe | CLI/IPC request-response |
| Smoke | `ECode.Smoke` | ConPTY 可用性 |
| UI smoke | WinAppDriver 或 FlaUI（后续） | 主窗口打开、快捷键、分屏 |
| Browser integration | WebView2 + local test page | browser API |
| Installer smoke | PowerShell | 安装/卸载/更新 |

---

## 7. 风险登记

| ID | 风险 | 严重度 | 概率 | 触发信号 | 缓解 |
|---|---|---|---|---|---|
| R01 | ConPTY 在 CI / 旧 Windows 上行为不一致 | 高 | 中 | Smoke test flaky | 限定 Windows 10 1809+；CI 用固定 runner；失败记录原始字节 |
| R02 | WebView2 引入内存膨胀 | 中 | 高 | 2 browser 后内存 > 1GB | browser lazy load；关闭 surface 立即 Dispose |
| R03 | v2 协议重构破坏 v1 CLI | 高 | 中 | 旧脚本失败 | v1 tests 固化；兼容垫片 |
| R04 | `ecode.json` 执行恶意命令 | 高 | 中 | 首次运行即执行未知命令 | fingerprint 信任 + confirm |
| R05 | MSIX 与 ConPTY 不兼容 | 中 | 中 | 安装版无法创建 PTY | 保留 Velopack/Inno fallback |
| R06 | Agent hooks 格式频繁变化 | 中 | 高 | setup 后通知失效 | hooks setup 输出 diff；维护 per-agent adapter |
| R07 | 多窗口与 daemon event 分发错位 | 高 | 中 | 输出进入错误 pane | event 中只使用 paneId UUID，不用 index；强测试 |
| R08 | 大量 scrollback 保存卡 UI | 中 | 中 | 关闭窗口卡顿 | 后台保存 + snapshot line limit |
| R09 | Browser automation 被页面 CSP 阻止 | 中 | 中 | ExecuteScriptAsync 失败 | WebView2 devtools protocol fallback |
| R10 | 中文路径/空格路径导致发布或 CLI 出错 | 中 | 高 | 用户路径失败 | 全部路径 quote；CI 加中文路径 smoke |

---

## 8. 成功指标

### 8.1 产品指标

| 指标 | 目标 |
|---|---|
| 首次启动成功率 | > 99% |
| `ecode notify` 到 UI 可见 | < 300ms |
| 10 个 agent session 并行时 UI 无明显卡顿 | 是 |
| 用户从未读通知跳转到目标 pane | 1 次快捷键 |
| `.ecode/ecode.json` 识别并运行项目命令 | 0 配置外步骤 |
| 重启后恢复布局/cwd/scrollback | 可靠 |

### 8.2 工程指标

| 指标 | M0 | M1 | M2 | M3 | M4 | M5 | M6 | M7 |
|---|---|---|---|---|---|---|---|---|
| Core 测试数 | 80 | 120 | 150 | 170 | 220 | 260 | 300 | 330 |
| Contract 测试数 | 5 | 15 | 25 | 35 | 60 | 100 | 120 | 140 |
| Browser 测试数 | 0 | 0 | 0 | 5 | 40 | 50 | 55 | 60 |
| CI 时间 | < 8m | < 10m | < 12m | < 15m | < 20m | < 25m | < 30m | < 30m |
| P0 bug | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| P1 bug | <=5 | <=5 | <=4 | <=4 | <=3 | <=3 | <=2 | <=2 |

---

## 9. 上游跟踪与差异管理

### 9.1 上游定期检查项

每月检查一次：

- `manaflow-ai/ecode` README 变化。
- `TODO.md` 中 Socket API / Browser / SSH / Hooks 的状态。
- `docs/custom-commands` schema 新增字段。
- `docs/session-restore` 支持的 agent resume matrix。
- CLI 目录中新增命令族。

### 9.2 Windows 适配规则

| 上游能力 | Windows 适配策略 |
|---|---|
| `Cmd` 快捷键 | 映射为 `Ctrl`，涉及窗口级能力时使用 `Ctrl+Shift` 或 `Ctrl+Alt` |
| WKWebView API | WebView2 对应 API；缺口返回 `not_supported` |
| Sparkle | Velopack |
| `~/Library/Application Support/ecode` | `%USERPROFILE%/.ecode` |
| Unix socket | Named Pipe |
| shell hooks | PowerShell / cmd / agent config / wrapper exe |
| `~/.config/ecode` | `%USERPROFILE%\.config\ecode` |

---

## 10. 推荐实施顺序（前 10 个 PR）

| PR | 内容 | 原因 |
|---|---|---|
| 1 | `scripts/ci.ps1` + GitHub Actions | 所有后续改动先有安全网 |
| 2 | 版本号统一 + `STATUS` 扩展 | 低风险、高收益 |
| 3 | TerminalBuffer / VtParser / SplitNode 测试补齐 | 锁住核心行为 |
| 4 | Pane 蓝环 + notification UI 测试 | 直接补齐 macOS 核心体验 |
| 5 | NotificationPanel 右键菜单 + 未读排序 | 小步 UX 改进 |
| 6 | `EcodeJsonService` DTO + parser tests | 先做纯 Core，不动 UI |
| 7 | CommandPalette 接入 ecode.json simple command | 打通可见价值 |
| 8 | Surface 拖拽重排 | 完善基础交互 |
| 9 | ResumeBinding DTO/service | M2 起步，仍是 Core 层 |
| 10 | CLI `surface resume show/set/clear` | 第一个恢复闭环 |

---

## 11. 附录：参考链接

- 上游仓库：<https://github.com/manaflow-ai/ecode>
- 上游官网：<https://ecode.com/zh-CN>
- 自定义命令文档：<https://ecode.com/zh-CN/docs/custom-commands>
- 会话恢复文档：<https://ecode.com/zh-CN/docs/session-restore>
- ConPTY API：<https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session>
- WebView2 文档：<https://learn.microsoft.com/en-us/microsoft-edge/webview2/>
- xterm 控制序列：<https://invisible-island.net/xterm/ctlseqs/ctlseqs.html>
- Velopack：<https://velopack.io/>
- agent-browser：<https://github.com/vercel-labs/agent-browser>

---

## 12. 维护约定

- 本文档每个里程碑结束后更新一次。
- 任意 roadmap 范围变更必须说明：动机、影响范围、替代方案、验收标准。
- 进入实现前，先在对应 spec 文档中补充协议 / 数据结构 / UI 行为，再写代码。
- 复杂功能必须拆成 Core 层 PR、UI 层 PR、CLI/API 层 PR、测试/文档 PR，避免一次性大改。
