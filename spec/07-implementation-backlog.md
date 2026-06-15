# ECode 实施 Backlog

> 本文档是 `06-roadmap.md` 的“可执行切片”。每条 backlog 都可以作为一张 GitHub Issue 或一个 PR 单独跟踪。
>
> Backlog ID 格式：`M{里程碑}-{包}-{序号}`，例如 `M0-A-01`、`M1-B-02`、`M5-ALL-01`。
>
> 状态： `[ ]` 待办、`[~]` 进行中、`[x]` 已合并、`[!]` 被拆分或重新规划。

---

## 0. 通用约束

- 产品方向调整为 **SuperTerminal**：优先服务高强度终端、多项目分屏、浏览器预览、脚本化控制、会话恢复与 Windows 原生集成；不再规划专用 AI 运行时 / 外部工具适配器。
- 任何 backlog 落地前必须先在 `spec/` 中补齐协议 / 数据结构 / UI 行为描述。
- 复杂功能必须拆成 Core 层 PR、UI 层 PR、CLI/API 层 PR、测试/文档 PR。
- 同一个 backlog 不应同时跨 2 个里程碑；如发现跨里程碑，先拆。
- 合并前必须通过：
  - `dotnet build ECode.sln -c Debug` 零警告（`TreatWarningsAsErrors=true`）。
  - `dotnet test tests/ECode.Tests/ECode.Tests.csproj` 全绿。
  - 对 UI 类 backlog：截图或录屏。
  - 对协议类 backlog：至少 1 个 contract 测试。

---

## M0 - 工程基线

### 包 A：CI 与构建脚本

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M0-A-01` | 新增 `scripts/ci.ps1`（restore + build + test + smoke dry-run + publish dry-run） | `scripts/ci.ps1` | `pwsh scripts/ci.ps1` 本地通过 |
| `M0-A-02` | 新增 GitHub Actions CI（windows-latest） | `.github/workflows/ci.yml` | PR 自动校验；徽章可加 |
| `M0-A-03` | 统一版本源：`ECode.csproj` Version → `ecode version` → `STATUS` | `src/ECode.Cli/Program.cs`、`MainViewModel.cs` | `ecode version` 与 `STATUS.version` 一致 |
| `M0-A-04` | 发布产物校验：SHA256 + 文件存在 + 大小阈值 + exe 版本 | `scripts/publish.ps1` | 产物末尾输出校验表 |
| `M0-A-05` | `docs/` 与 `spec/` 互相引用检查脚本 | `scripts/ci.ps1` | 缺链时 CI 失败 |

### 包 B：测试补齐

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M0-B-01` | 增补 VtParser tests（UTF-8 跨包、OSC ST、CSI private modes、invalid sequence） | `tests/ECode.Tests/CoreTests.cs` | 新增 ≥20 个 parser 测试 |
| `M0-B-02` | 增补 TerminalBuffer tests（宽字符、alternate screen、scroll region、snapshot roundtrip） | 同上 | 新增 ≥20 个 buffer 测试 |
| `M0-B-03` | 增补 SplitNode tests（remove/swap/resize/equalize/factory layout） | 同上 | 覆盖所有 public method |
| `M0-B-04` | 增补 IPC DTO tests（`DaemonRequest/Response/Event` roundtrip） | 同上 | DTO 序列化兼容 |
| `M0-B-05` | 增补命令脱敏 tests（TOKEN / PASSWORD / API_KEY 规则） | 同上 | 含正反例 |

### 包 C：可观测性

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M0-C-01` | `[x]` 守护进程日志字段标准化：`component/event/paneId/ts` | `DaemonClient.cs`、`DaemonPipeServer.cs` | 单次 attach 可被 grep 串起来 |

### 包 D：文档

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M0-D-01` | 新增 `spec/README.md`（文档索引） | `spec/README.md` | 列出 01-07 文档职责 |
| `M0-D-02` | 修正 `04-build-deploy.md` “5 个项目” → “6 个项目” | `spec/04-build-deploy.md` | 已在当前 PR 处理 |
| `M0-D-03` | 统一守护进程重连描述（300ms / 1000ms / 500ms） | `01-architecture.md` §9、`03-data-and-ipc.md` §3.7 | 已在当前 PR 处理 |

---

## M1 - UI/UX 与 ecode.json 基础

### 包 A：通知视觉闭环

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M1-A-01` | `[x]` Pane 蓝环绘制（2px 蓝色光环） | `Controls/TerminalControl.cs` | `HasNotification=true` 时 Adorner 出现 |
| `M1-A-02` | `[x]` Surface tab 未读点 / glow | `Controls/SurfaceTabBar.xaml(.cs)` | 未读时显示蓝点 |
| `M1-A-03` | `[x]` Workspace sidebar 未读态 | `Controls/WorkspaceSidebarItem.xaml(.cs)` | 显示 badge + latest text |
| `M1-A-04` | `[x]` `Ctrl+Shift+U` 跳到最新未读 | `MainWindow.xaml.cs` | 跳转后目标 pane 闪烁 1 次 |
| `M1-A-05` | `[x]` 通知排序修正（最新未读优先） | `NotificationService.cs` | xUnit 测试通过 |
| `M1-A-06` | `[x]` `NotificationPanel` 右键菜单（标记已读 / 未读 / 复制内容） | `Controls/NotificationPanel.xaml.cs` | 全部菜单项可用 |

### 包 B：基础交互

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M1-B-01` | `[x]` Surface 拖拽重排 | `Controls/SurfaceTabBar.xaml.cs` | 重排后 session 不丢 |
| `M1-B-02` | `[x]` Workspace 拖拽重排 | `MainWindow.xaml.cs`、`WorkspaceSidebarItem.xaml.cs` | 拖动后顺序持久化 |
| `M1-B-03` | `[x]` 拖入文件 / 图片到终端 | `Controls/TerminalControl.cs` | 输出正确 quoted path |
| `M1-B-04` | `[x]` Workspace 右键菜单（重命名 / 关闭 / 复制 ID） | `MainWindow.xaml.cs` | 三项操作可用 |
| `M1-B-05` | `[x]` Close active tab 按钮常显 | `Controls/SurfaceTabBar.xaml` | 视觉对比 macOS 截图 |
| `M1-B-06` | `[x]` 设置面板按"外观 / 终端 / 行为 / 键盘 / 高级"重排 | `Views/SettingsWindow.xaml` | 新增“自定义命令”页 |

### 包 C：`ecode.json` 基础

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M1-C-01` | `[x]` `EcodeJsonConfig / EcodeCommand / EcodeAction` DTO | `ECode.Core/Models/EcodeJsonConfig.cs` | Core 单元测试通过 |
| `M1-C-02` | `[x]` `EcodeJsonService` 解析（路径搜索 / 全局本地合并 / schema 错误） | `ECode.Core/Services/EcodeJsonService.cs` | 含 `MergesLocalOverGlobal`、`InvalidSchema_ReturnsDiagnostic`、JSONC 测试 |
| `M1-C-03` | `[x]` CommandPalette 接入 custom commands | `Controls/CommandPalette.xaml.cs`、`Views/MainWindow.xaml.cs` | 命令出现在面板，keywords/action id 可搜索 |
| `M1-C-04` | `[x]` `currentTerminal` / `newTabInCurrentPane` 目标执行 | `Views/MainWindow.xaml.cs` | 含 `confirm` 弹窗路径；执行时记录命令日志 |
| `M1-C-05` | `[x]` CLI `ecode reload-config` + `Ctrl+Shift+,` | `ECode.Cli/Program.cs`、`MainWindow.xaml.cs` | 重载后命令面板刷新 |

---

## M2 - 会话恢复增强

### 包 A：数据与服务

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M2-A-01` | `[x]` `ResumeBinding` DTO + JSON | `ECode.Core/Models/ResumeBinding.cs` | roundtrip 测试 |
| `M2-A-02` | `[x]` `ResumeBindingService`（Load/Save/Add/SetForPane/Remove/RemoveForPane/FindForSurface/TrustPrefix） | `ECode.Core/Services/ResumeBindingService.cs` | 覆盖所有 public method |
| `M2-A-03` | `[x]` 敏感环境剔除（TOKEN / PASSWORD / SECRET / API_KEY 等） | 同上 | `DropsSensitiveEnv` 测试通过 |
| `M2-A-04` | `[x]` `ECODE_WORKSPACE_ID` 启动注入 | `TerminalProcess.cs` | 本地与 daemon shell 启动环境均注入 workspace id |

### 包 B：UI 与开关

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M2-B-01` | `[x]` 恢复确认 UI（未信任 binding 提示条） | `Controls/SplitPaneContainer.cs` 或 `TerminalControl.cs` | 红框 + “可恢复” 按钮 |
| `M2-B-02` | `[x]` 自动恢复设置项（全局开关 + 每条 binding 显式信任） | `ECodeSettings.cs`、`SettingsWindow.xaml` | 关闭后所有 resume binding 均不自动执行 |
| `M2-B-03` | `[x]` 进程检测（tasklist 解析 tmux / shell 子进程） | `ECode.Core/Services/ResumeProcessDetector.cs` | 单元测试：含/不含 tmux 与 shell 路径 |

### 包 C：CLI

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M2-C-01` | `[x]` CLI `surface resume {set,show,clear}` | `ECode.Cli/Program.cs`、`MainViewModel.cs` | build + ResumeBinding service 测试通过 |
| `M2-C-02` | `[x]` CLI `restore-session` / `Ctrl+Shift+O` 入口 | `MainWindow.xaml.cs` | UI 入口可用 |

## M3 - 浏览器面板基础

### 包 A：模型与持久化

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M3-A-01` | `[x]` `SurfaceKind { Terminal, Browser }` | `Models/Surface.cs` | 旧 `session.json` 默认 Terminal |
| `M3-A-02` | `[x]` `SessionState` 新增 `kind/browserUrl/browserTitle/browserHistory` | `Models/SessionState.cs`、`SessionPersistenceService.cs` | Browser metadata roundtrip 测试通过 |

### 包 B：UI

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M3-B-01` | `[x]` `BrowserPaneViewModel` | `src/ECode/ViewModels/BrowserPaneViewModel.cs` | URL / Title / Loading / CanGoBack 等属性变更广播 |
| `M3-B-02` | `[x]` `BrowserControl` 升级（地址栏、back/forward/reload/stop/devtools） | `Controls/BrowserControl.xaml(.cs)` | 工具栏状态与加载进度可用 |
| `M3-B-03` | `[x]` `SplitPaneContainer` 支持 browser leaf | `Controls/SplitPaneContainer.cs` | `BuildLeaf` 分支渲染 BrowserControl |
| `M3-B-04` | `[x]` WebView2 缺失时的友好提示 | `BrowserControl.xaml.cs` | 不崩溃，提示下载链接 |

### 包 C：CLI / ecode.json

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M3-C-01` | `[x]` `ecode browser open|open-split|new <url>` | `ECode.Cli/Program.cs`、`MainViewModel.cs` | build 通过；open-split v1 回退为 new-surface |
| `M3-C-02` | `[x]` `.ecode/ecode.json` workspace 中 `type:"browser"` surface 解析 | `EcodeJsonService.cs`、`MainWindow.xaml.cs` | 在 layout 中可创建 |
| `M3-C-03` | `[x]` v1 IPC `BROWSER.OPEN_SPLIT` 文本参数 | `MainViewModel.HandlePipeCommand` | v1 响应含 `fallbackMode:"new-surface"` |

---

## M4 - 浏览器脚本化 API

### 包 A：协议层

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M4-A-01` | `[x]` v2 协议层基础（先做 v2 框架；具体 method 在 M4 落地） | `ECode.Core/IPC/v2/*` | `protocol:ecode.v2` 请求可被解析 |
| `M4-A-02` | `[x]` 稳定错误码：`invalid_ref / not_found / stale_ref / not_supported / timeout / internal_error` | 同上 | contract 测试覆盖所有错误码 |

### 包 B：脚本化服务

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M4-B-01` | `[x]` `BrowserScriptingService` 框架（refs / diagnostics / surfaceRef 路由） | `ECode/Services/BrowserScriptingService.cs` | 单测：refs 失效后返回 `stale_ref` |
| `M4-B-02` | `[x]` `snapshot / find.role / find.text / find.testid / find.first / find.last / find.nth` | 同上 | P0 locator 测试 |
| `M4-B-03` | `[x]` `click / fill / hover / press / eval / screenshot` | 同上 | 含空字符串 fill 清空 input |
| `M4-B-04` | `[x]` `cookies.get/set/clear` 与 `storage.get/set/clear` | 同上 | 状态测试 |
| `M4-B-05` | `[x]` `console.list/clear`、`dialog.accept/dismiss`、`download.wait`、`highlight` | 同上 | 应做范围测试 |
| `M4-B-06` | `[x]` `addinitscript / addscript / addstyle` | 同上 | 可做范围 |
| `M4-B-07` | `[x]` NotSupported 矩阵（viewport / geolocation / offline / trace / network.route / screencast / input_mouse / input_keyboard / input_touch） | 同上 | 全部返回 `not_supported` |

### 包 C：CLI

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M4-C-01` | `[x]` `ecode browser snapshot/click/fill/eval/screenshot/...` | `ECode.Cli/Program.cs` | E2E smoke：打开 localhost → snapshot → click → fill → eval |

### 包 D：测试

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M4-D-01` | `[x]` 浏览器脚本化 xUnit（依赖 WebView2 + 本地测试页） | `tests/ECode.Tests/BrowserScriptingTests.cs` | P0 全绿；CI 标记 Windows-only integration |

---

## M5 - v2 协议、多窗口与短 ID

### 包 A：协议升级

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M5-A-01` | `[x]` 同一 `\\.\pipe\ecode` 上 v1/v2 协商（首行 JSON 判 v2） | `NamedPipeServer.cs`、`NamedPipeClient.cs` | v1 / v2 并存可运行 |
| `M5-A-02` | `[x]` 短 ID 引用（`window:N / workspace:N / surface:N / pane:N`） | `ECode.Core/Models/ShortRef.cs` | UUID↔ref 双向解析 |
| `M5-A-03` | `[x]` `--id-format refs|uuids|both` 全局参数 | `ECode.Cli/Program.cs` | 默认行为与 `06-roadmap.md` §5.3 一致 |

### 包 B：多窗口

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M5-B-01` | `[x]` `WindowManagerService` | `ECode/Services/WindowManagerService.cs` | 多窗口独立生命周期 |
| `M5-B-02` | `[x]` `window.list/current/focus/create/close` v2 API | `MainViewModel`、v2 协议层 | contract 测试 |
| `M5-B-03` | `[x]` `surface.{move,reorder}` | 同上 | contract 测试 |

### 包 C：API 覆盖

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M5-C-01` | `[x]` `workspace.list/create/select/close/rename/reorder` | v2 协议层 | contract 测试 |
| `M5-C-02` | `[x]` `pane.list/focus/write/read/split/close/resize/swap/zoom` | 同上 | contract 测试 |
| `M5-C-03` | `[x]` `notification.list/read/unread/jump-latest/clear` | 同上 | contract 测试 |
| `M5-C-04` | `[x]` `config.reload` / `config.diagnostics` | 同上 | contract 测试 |
| `M5-C-05` | `[x]` `status` / `health` | 同上 | contract 测试 |

---

## M6 - 系统集成、安装与更新

### 包 A：Shell / CLI 集成

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M6-A-01` | `[x]` PATH / shell profile setup（PowerShell、cmd） | `ECode.Cli/Commands/ShellSetup.cs` | install / uninstall 可逆 |
| `M6-A-02` | `[x]` PowerShell completion | `scripts/completions/ecode.ps1` | `ecode <Tab>` 可补全命令与 refs |
| `M6-A-03` | `[x]` Windows Terminal profile 导入 | `ECode.Cli/Commands/ProfileImport.cs` | 可导入配色 / 字体 / shell profile |
| `M6-A-04` | `[x]` `ecode doctor` 环境诊断 | `ECode.Cli/Program.cs` | 输出 ConPTY / WebView2 / PATH / daemon 状态 |
| `M6-A-05` | `[x]` `ecode setup status` / `ecode setup uninstall` | 同上 | diff 输出可读，卸载清理 PATH/profile 变更 |

### 包 B：安装与更新

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M6-B-01` | `[x]` Velopack 集成（installer + feed） | `src/ECode.Updater`（新增）、`scripts/publish.ps1` | 新版本自动检测 |
| `M6-B-02` | `[x]` `ecode update check/install` CLI | `ECode.Cli/Program.cs` | 可后台静默更新 |
| `M6-B-03` | `[x]` Inno Setup 安装器（fallback 路径） | `installer/ecode.iss` | 卸载干净 |
| `M6-B-04` | `[x]` MSIX 打包（可选 enterprise） | `installer/AppXManifest.xml` | `Add-AppxPackage` 成功 |
| `M6-B-05` | `[x]` 多 RID CI（`win-x64 / win-x86 / win-arm64`） | `.github/workflows/release.yml` | nightly 出 4 个产物 |
| `M6-B-06` | `[x]` `CHANGELOG.md` 自动生成（git-cliff 或 release-drafter） | `.github/release.yml` | release 触发自动更新 |

---

## M7 - 生态、文档与 1.0 收敛

### 包 A：文档站

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M7-A-01` | `[x]` `docs/` 目录（mkdocs material 或 vitepress） | `docs/*` | 站点可构建 |
| `M7-A-02` | `[x]` `installation.md` | `docs/installation.md` | 含 zip / Velopack / MSIX 三种安装方式 |
| `M7-A-03` | `[x]` `getting-started.md` | `docs/getting-started.md` | 中文 / 英文 |
| `M7-A-04` | `[x]` `custom-commands.md` | `docs/custom-commands.md` | 与 M5 后状态同步 |
| `M7-A-05` | `[x]` `browser-api.md` | `docs/browser-api.md` | 与 M4 协议同步 |
| `M7-A-06` | `[x]` `session-restore.md` | `docs/session-restore.md` | 与 M2 数据模型同步 |
| `M7-A-07` | `[x]` `cli.md` | `docs/cli.md` | v1+v2 |
| `M7-A-08` | `[x]` `troubleshooting.md` | `docs/troubleshooting.md` | 含 daemon-debug.log 解读 |

### 包 B：社区与治理

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M7-B-01` | `[x]` `CONTRIBUTING.md` | `CONTRIBUTING.md` | 含构建、测试、PR 流程 |
| `M7-B-02` | `[x]` `SECURITY.md` | `SECURITY.md` | 含漏洞报告方式 |
| `M7-B-03` | [x] Issue / PR 模板 | `.github/ISSUE_TEMPLATE/*`、`PULL_REQUEST_TEMPLATE.md` | bug / feature 模板可用 |
| `M7-B-04` | [x] Discord 同步公告 | `scripts/discord-notify.ps1` | release 时 webhook 触发 |

### 包 C：1.0 门槛

| ID | 标题 | 关联文件 | 验收 |
|---|---|---|---|
| `M7-C-01` | P0/P1 bug 收敛（详见 `06-roadmap.md` §8） | — | P0=0、P1<=3 且有 workaround |
| `M7-C-02` | 1.0 发布说明（GitHub Release） | `docs/release-notes/1.0.0.md` | 用户可读 |
| `M7-C-03` | 公开 Roadmap 页面 | `docs/roadmap.md` | 与 `06-roadmap.md` 一致 |

---

## 跨里程碑（横切）

| ID | 标题 | 关联里程碑 | 关联文件 | 验收 |
|---|---|---|---|---|
| `X-01` | Windows 路径 / 中文路径 smoke | M0 起 | `tests/ECode.Smoke` | CI 中含 `中文 目录/项目/` |
| `X-02` | 性能预算监控（冷启动 / status / snapshot / save session） | M1 起 | `scripts/perf/*` | 报告产出并入 release |
| `X-03` | 风险登记刷新 | 持续 | `06-roadmap.md` §7 | 每迭代更新 |
| `X-04` | 与上游 `manaflow-ai/ecode` 对齐检查 | M2 起 | `scripts/upstream-sync.md` | 月度报告 |

---

## 推荐首个冲刺（PR 顺序）

1. `M0-A-01` + `M0-A-02`（建立 CI 安全网）
2. `M0-A-03`（版本号统一）
3. `M0-B-01` ~ `M0-B-05`（核心测试补齐）
4. `M1-C-01` ~ `M1-C-05`（已完成：`ecode.json` Core + CommandPalette 接入 + reload config）
5. `M2-A-01`（已完成：`ResumeBinding` DTO + JSON）
6. `M1-A-01`（Pane 蓝环，macOS 核心体验首块拼图）
7. `M2-A-03`（已完成：ResumeBinding 敏感环境剔除）
8. `M2-C-01`（已完成：CLI `surface resume`）
9. `M2-A-04`（已完成：`ECODE_WORKSPACE_ID` 启动注入）
10. `M3-A-01` + `M3-A-02`（已完成：SurfaceKind + SessionState 扩展）
11. `M3-B-01`（已完成：BrowserPaneViewModel 状态层）
12. `M3-B-03` + `M3-B-04`（已完成：Browser leaf 渲染 + WebView2 缺失提示）
13. `M3-C-01` + `M3-C-03`（已完成：CLI/IPC browser open）
14. `M3-B-02`（已完成：BrowserControl 工具栏升级）
15. `M3-C-02`（ecode.json browser surface 解析）

> 进入 M4 / M5 前必须先冻结 v1 CLI 行为并完成 v1 contract 测试固化，避免 v2 协议破坏现有自动化脚本。
