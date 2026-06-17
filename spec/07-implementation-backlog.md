# ECodex 敏捷实施 Backlog

> 本文是 AI Agent 的可执行队列。路线图只讲方向；本文件必须能直接指导下一轮开发。
>
> 执行 loop 见 `00-agile-ai-delivery.md`，优先级与 Outcome 见 `06-roadmap.md`。

---

## 0. 状态与选择规则

### 0.1 状态

| 状态 | 含义 |
|---|---|
| `draft` | 想法存在，但 Outcome / Scope / Acceptance 不完整 |
| `ready` | 可以被 AI Agent 自动领取 |
| `doing` | 正在处理；默认全仓同一时间最多 1 个 |
| `blocked` | 缺信息、缺环境、缺权限或连续失败，需要人工处理 |
| `done` | 已完成并通过对应验证 |
| `icebox` | 暂不做；保留上下文 |

### 0.2 自动选择规则

AI Agent 启动后按以下顺序选择任务：

1. `P0` / 安全 / 数据丢失 / 静默执行风险。
2. `Now` 区域中最靠上的 `ready` 项。
3. 能在当前环境验证的项优先于 Windows-only 项。
4. 文档 / 测试 / Core 小切片优先于大 UI / 发布任务。
5. 没有 `ready` 项时，只做 backlog refinement，不写代码。

---

## 1. 当前冲刺：S2 - 常驻通知与开箱体验

目标：让下一版本的 ECodex 默认成为常驻后台工作台：关闭/最小化后进入系统托盘，通知能基于命令生命周期提醒用户，预制 skills 可安全种子安装，并让 `Ctrl+Enter` 在终端里稳定输入换行。

| ID | 状态 | Outcome | Scope | Acceptance |
|---|---|---|---|---|
| `TTY-01` | done | 所有 ECodex Terminal 中 `Ctrl+Enter` 默认输入换行，不立即执行；普通 `Enter` 仍提交 | 调整 `src/ECodex/Controls/TerminalControl.cs` 的按键映射与 IME 代理转发；同步 `docs/keyboard-shortcuts.md` 与 `CHANGELOG.md`；不新增设置项，不做 Codex 进程专属判断 | Source test 覆盖 `Ctrl+Enter -> LF`、`Enter -> CR` 且不触发 `SubmitBufferedCommand`；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~TerminalControlSourceTests" --no-restore` 通过；Windows PowerShell / cmd / Codex CLI 手测待人工确认；`git diff --check` 通过 |
| `SKL-01` | done | App 首次启动时把仓库预制 skills 安全复制到 `%USERPROFILE%\.agents\skills`，用户已有同名 skill 不被覆盖 | 约定模板源目录 `assets/default-skills/`，发布包内目录 `default-skills`；新增启动时种子安装服务，按第一层目录复制到用户目录；同名目录跳过，不合并、不删除；安装器只负责带上模板目录，不绑定具体 skill 清单 | `DefaultSkillSeedServiceTests` 覆盖复制第一层目录、递归复制子文件、跳过同名目录、忽略根文件和缺源 no-op；`DefaultSkillsPackagingTests` 覆盖 App 启动调用与 publish 内容声明；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~DefaultSkillSeedServiceTests|FullyQualifiedName~DefaultSkillsPackagingTests" --no-restore` 通过；docs 说明目录约定与不覆盖策略 |
| `TRAY-01A` | done | 关闭按钮与最小化都隐藏到系统托盘，后台终端和通知继续运行 | WPF 主窗口生命周期、系统托盘图标、双击/菜单“打开 ECodex”；保持单实例策略，第二次启动仍聚焦/恢复已有窗口；不改变 daemon 终端保留设置语义 | `TrayResidencySourceTests` 与 `AppSingleWindowSourceTests` 覆盖关闭/最小化隐藏、托盘恢复、显式退出和单实例恢复；`.dotnet\dotnet.exe build src\ECodex\ECodex.csproj -c Debug --no-restore` 通过；Windows GUI 手测待人工确认 |
| `TRAY-01B` | done | 托盘菜单提供“退出并保留终端”和“退出并终止终端”，退出语义与现有设置一致 | 复用现有 daemon session termination；菜单包含打开、退出并保留终端、退出并终止终端；同步 session restore / troubleshooting 文档 | `TrayResidencySourceTests` 覆盖托盘菜单文案、保留退出、终止退出、失败日志与文档；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~TrayResidencySourceTests" --no-restore` 通过；Windows GUI 手测待人工确认 |
| `NOT-02A` | done | PowerShell shell integration hook 默认随 App 首次启动安装，可回传命令开始、结束和退出码 | 扩展现有 setup/profile 机制：ECodex 专属标记块、写入前备份到 `%USERPROFILE%\.ecodex\backups\`、`setup status` 可检查、`setup uninstall --write true` 可移除；默认仅 PowerShell，cmd/Git Bash 后续再做；冲突时跳过并提示，不静默覆盖 | `PowerShellHookSetupServiceTests` 覆盖缺失安装、幂等、冲突跳过、写前备份与 App/CLI 接入；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~PowerShellHookSetupServiceTests" --no-restore` 通过；真实 profile 写入手测待人工确认 |
| `NOT-02B` | draft | 后台/非激活时，基于命令生命周期发送完成、失败、等待输入通知，并进入未读中心 | 依赖 `NOT-02A` 的 hook 事件；定义 pane/session/command id 数据流、退出码映射、通知去重与节流；没有 hook 时降级到 OSC / `ecodex notify` / 关键输出规则 | 后台执行命令成功结束只产生完成通知；非 0 退出码产生失败通知；前台活跃时不刷 Toast；通知能定位 workspace/surface/pane；普通流式输出不逐条通知 |
| `NOT-02C` | done | 点击 Windows Toast 精准打开 ECodex 并跳转到对应 workspace / surface / pane | 明确 Toast activation/AppUserModelID/非打包应用策略；复用通知 ID、workspaceId、surfaceId、paneId；窗口隐藏到托盘时也能恢复并跳转 | Toast 点击后窗口显示并聚焦对应 pane；通知标记为已读；目标 pane 不存在时给出可见 fallback；Windows Toast 不可用时不影响应用主流程；Windows-only live smoke/checklist 已覆盖诊断与人工证据 |
| `NOT-02D` | done | Codex 等待输入、关键确认、错误决策等交互状态能触发低噪声提醒 | 在生命周期通知之外补充状态识别；优先识别 Codex 常见等待输入/确认语义，再扩展可配置规则；仅窗口隐藏/非激活且匹配关键状态时提醒 | Codex 等待用户输入时后台收到 `AgentAttention` 通知；普通输出负控不通知；同一状态有去重/冷却；Windows-only smoke/checklist 已覆盖证据模板 |

### 1.1 上一冲刺归档：S1 - 会话恢复与 AI loop 稳定化

目标：优先交付 `SES-01`，让 ECodex 在用户正常关闭主窗口后保留后台终端进程，并在重开时自动接回，同时补齐状态可见性、终止入口和安全回退。

| ID | 状态 | Outcome | Scope | Acceptance |
|---|---|---|---|---|
| `SES-01` | done | 用户关闭 ECodex 窗口后，在同一 Windows 登录会话内重新打开，原 Codex / PowerShell 等终端进程仍由 daemon 托管，终端自动 attach 到原会话并可继续输入输出 | 首个切片覆盖“正常关闭主窗口 -> daemon 继续托管终端 -> 重开自动 attach”；涉及 `src/ECodex` 关闭/启动流程、`src/ECodex.Core` daemon session mapping、`session.json` pane/session id 持久化、状态可见性；不覆盖 Windows 重启/关机后的进程存活，不做命令回放 | Windows 手测：在 pane 启动 `pwsh` / Codex，关闭 ECodex，确认后台会话未退出；重开 ECodex 后恢复 workspace/surface/pane 布局并 attach 到同一进程，`pane.write/read` 可继续交互；无重复 shell；daemon 不可达时展示过期/已断开并回退到快照，不静默执行命令 |
| `SES-01A` | done | 正常关闭 ECodex 时只断开客户端，不把 daemon 托管会话误回退成本地 ConPTY | 区分 `DaemonClient.Dispose()` 主动关闭与 daemon 意外断线；主动关闭不广播 `Disconnected`，运行中意外断线仍保留本地 fallback；同步 session restore 与 IPC spec | `DaemonClientLifecycleSourceTests` 先失败后通过；`dotnet test --filter DaemonClientLifecycleSourceTests` 通过；关闭窗口默认由 `PreserveDaemonSessionsOnClose=true` 保留后台终端 |
| `SES-02` | done | 主程序崩溃后重开时，已持久化 pane 能继续自动 attach daemon 中仍存活的终端 | 保守恢复：结构变化后实时写 `session.json` checkpoint，但不生成 `session-close` transcript；重开时仅挂载 checkpoint 中已有 paneId，不自动创建 daemon 孤儿 pane | `CrashRecoveryCheckpointSourceTests` 先失败后通过；新建/关闭 Surface、分屏/关闭 pane、移动/调整分屏触发 `SessionCheckpointRequested`；`SaveSession(..., captureTranscripts:false)` 只更新布局与 pane snapshot |
| `SES-03` | done | 用户可持久化“关闭窗口时保留终端”设置，并通过内部 IPC 退出 ECodex 同时终止 daemon 终端 | 新增 `PreserveDaemonSessionsOnClose`（默认 true）与设置页开关；移除 daemon `SESSION_CLOSE_ALL` 和右键菜单清理入口；新增主应用 `ecodex.v2` 方法 `app.exit {"terminateTerminals":true}`，内部用 `SESSION_LIST` + 逐个 `SESSION_CLOSE` | `DaemonSessionTerminationPolicyTests`、`AppLifecycleApiServiceTests`、`DaemonMessageRoundTripTests` 先失败后通过；`docs/` 与 spec 同步说明设置、内部 IPC 与移除旧协议 |
| `WIN-02` | done | ECodex 只允许一个主窗口，避免多窗口重复挂载终端 | 取消 S3 多窗口接管方向；主进程用 `Global\ECodexMainApp` Mutex 单实例化；第二次启动只聚焦已有窗口，不转发参数；`window.create` 保留兼容但只聚焦现有窗口 | `WindowCreate_FocusesExistingWindowInsteadOfCreatingSecondWindow` 与 `AppSingleWindowSourceTests` 先失败后通过；CLI、`docs/` 与 spec 说明 `window.create` 不再新建第二主窗口 |
| `AGL-01` | done | AI loop 修改文档后能快速发现坏链接或旧文件名，降低文档漂移 | 新增 `scripts/check-doc-links.ps1`；`scripts/ci.ps1` 调用独立脚本；同步 `spec/04-build-deploy.md`；顺手修复 `spec/README.md` 对缺失 `08-dotnet-csharp-handbook.md` 的坏链接引用 | `pwsh ./scripts/check-doc-links.ps1` 通过；临时坏链接用例返回失败；脚本语法检查通过 |
| `NAM-01` | done | 用户、维护者和发布产物看到的品牌统一为 `ECodex`，代码项目 / namespace / XAML 类型命名也同步使用 `ECodex` | 已统一 README、`docs/`、spec 与历史文档、安装器显示名、solution/project/folder 名、C# namespace、XAML `x:Class`、资源 key 与测试命名；保留全小写 `ecodex` 命令、配置、管道、数据路径和产物名 | 旧 Pascal 品牌拼写搜索无命中；临时归档副本执行 `.\.dotnet\dotnet.exe build ECodex.sln -c Debug` 通过；`.\.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --no-restore` 通过 284/284；`.\.dotnet\dotnet.exe build tests\ECodex.Smoke\ECodex.Smoke.csproj -c Debug` 通过；`pwsh ./scripts/check-doc-links.ps1` 与 `git diff --cached --check` 通过 |

### 1.2 上一冲刺归档：S0 - spec 敏捷化与 AI loop

目标：把 `spec/` 从静态规划文档重构为可指导 AI 自动化开发的敏捷交付系统。

| ID | 状态 | Outcome | Scope | Acceptance |
|---|---|---|---|---|
| `S0-01` | done | 新增 AI 自动 loop 入口文档 | `spec/00-agile-ai-delivery.md` | 文档包含 loop、DoR、DoD、停止规则、验证矩阵 |
| `S0-02` | done | 路线图改为 Now / Next / Later | `spec/06-roadmap.md` | 不再把已完成 M0-M7 当当前 backlog；保留 1.0 基线归档 |
| `S0-03` | done | backlog 改为敏捷队列 | `spec/07-implementation-backlog.md` | 有状态规则、任务模板、ready 队列和 refinement 规则 |
| `S0-04` | done | spec 索引同步新入口 | `spec/README.md` | 阅读顺序以 `00` 开始，状态表反映敏捷交付用途 |

---

## 2. Ready 队列（Now）

下个可领切片优先进入 `OBS-01-8` AgentConversation Core DTO 与存储契约；`OBS-01-7` 已完成 AgentConversation planned 边界复核。

### `NOT-02B-R` - 拆分命令生命周期通知契约

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | 维护者获得可执行的 `NOT-02B` 子队列，明确 hook 事件如何定位 workspace / surface / pane，以及完成、失败、前台活跃、Toast、去重与节流的边界 |
| Scope | 只做 backlog / spec refinement；阅读 `TerminalEnvironmentVariables`、`PowerShellHookSetupService`、`MainViewModel.HandleHookCommand`、`NotificationService`、`ToastNotificationHelper`；可更新 `spec/03-data-and-ipc.md` 与本文；不改运行时代码、不写真实 PowerShell profile、不做 Toast live 验证 |
| 关联 | `06-roadmap.md` Now 的命令生命周期通知；`03-data-and-ipc.md` 的 `HOOK.COMMAND`、命令日志、通知契约；`NOT-02A` 已完成的 PowerShell hook |
| 验收 | `NOT-02B-1`、`NOT-02B-2`、`NOT-02B-3`、`NOT-02C-R` 的 Ready 字段补齐；明确是否新增 `ECODEX_SURFACE_ID` / `ECODEX_PANE_ID` 环境变量；明确前台活跃时是“不进通知中心”还是“进通知中心但不弹 Toast”；`pwsh ./scripts/check-doc-links.ps1` 与 `git diff --check` 通过 |
| 风险 | 如果前台行为或 pane 定位语义未确认，后续实现容易误报或跳错 pane；若 hook contract 设计过宽，会增加 profile 兼容风险 |
| 回滚 | 仅回退本文与相关 spec 的 refinement diff，不影响已完成的 `NOT-02A` hook 安装能力 |

### `NOT-02B-1` - 为 hook 生命周期事件补 pane 定位

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | PowerShell hook 生命周期事件能带上 ECodex 启动终端的 workspace / surface / pane 上下文，后续通知可以定位到触发命令的 pane，外部 PowerShell 不会被误当作 ECodex pane |
| Scope | 扩展 `TerminalEnvironmentVariables` 与终端启动调用，新增 `ECODEX_SURFACE_ID`、`ECODEX_PANE_ID`；更新 `PowerShellHookSetupService` 的 hook block 读取环境变量并透传到 `ecodex hook event`；更新 CLI hook 参数解析和 `MainViewModel.HandleHookCommand` 日志字段；同步 `spec/03-data-and-ipc.md`；不生成通知、不写真实 profile |
| 关联 | `src/ECodex.Core/Terminal/TerminalEnvironmentVariables.cs`、`src/ECodex/ViewModels/SurfaceViewModel.cs`、`src/ECodex.Daemon/DaemonSessionManager.cs`、`src/ECodex.Core/Services/PowerShellHookSetupService.cs`、`src/ECodex.Cli/Program.cs`、`src/ECodex/ViewModels/MainViewModel.cs`、`03-data-and-ipc.md` §5.4 |
| 验收 | `TerminalEnvironmentVariablesTests` 覆盖 workspace / surface / pane 环境变量和无效名过滤；`PowerShellHookSetupServiceTests` 覆盖 hook block 发送 `--workspace-id`、`--surface-id`、`--pane-id`；CLI source test 覆盖 `hook event` 参数透传；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~TerminalEnvironmentVariablesTests|FullyQualifiedName~PowerShellHookSetupServiceTests" --no-restore` 与 `git diff --check` 通过 |
| 风险 | daemon session 创建路径当前主要按 workspace 注入环境，补 surface / pane 需要避免打破现有 attach / restore；全局 profile hook 必须在缺少 ECodex 环境变量时保持 no-op 通知 |
| 回滚 | 移除新增环境变量和 hook 参数，恢复只记录 `phase / command / exitCode / cwd` 的 `NOT-02A` 行为 |

### `NOT-02B-2` - 生成低噪声完成 / 失败通知

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | 当 ECodex 隐藏到托盘或处于非激活状态时，命令结束会按退出码进入未读中心：`0` 为完成通知，非 `0` 为失败通知；前台活跃时不创建未读通知、不弹 Toast |
| Scope | 在 hook handler 周边或新增小服务维护 pane 级 active command lifecycle；只处理带 `workspaceId` 的 ECodex 终端事件；调用 `NotificationService.AddNotification` 生成 workspace / surface / pane 定位通知；同步 `03-data-and-ipc.md` 与必要 docs；不实现 Toast 点击激活、不识别 Codex 等待输入 |
| 关联 | `src/ECodex/ViewModels/MainViewModel.cs`、`src/ECodex.Core/Services/NotificationService.cs`、`src/ECodex.Core/Models/TerminalNotification.cs`、`src/ECodex/App.xaml.cs`、`03-data-and-ipc.md` §5.4/§6 |
| 验收 | 测试覆盖：外部 shell 缺 `workspaceId` 只写日志不通知；后台 / 非激活 `exitCode=0` 生成完成通知；非 `0` 生成失败通知；前台活跃不创建未读通知；缺 `paneId` 时只生成 workspace / surface 级通知且不跳错 pane；focused tests 与 `git diff --check` 通过 |
| 风险 | WPF 窗口活跃 / 隐藏状态在纯单测中只能通过抽象或 source test 验证；真实 Toast 噪声仍需 Windows 手测 |
| 回滚 | 禁用 hook lifecycle 到 `NotificationService` 的桥接，保留 hook 日志和命令日志 |

### `NOT-02B-3` - 生命周期通知去重与节流

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | watch / dev server / 重复 hook 不会短时间刷屏，同时不同 pane 的完成或失败事件仍能独立提醒 |
| Scope | 为 `NOT-02B-2` 的通知生成层增加保守去重 / 冷却；建议默认同 pane + 同命令 + 同退出状态 30 秒内只保留 1 条，失败通知可更新最近一条摘要但不重复 Toast；同步 spec；不新增用户设置项 |
| 关联 | `NotificationService`、`MainViewModel.HandleHookCommand` 或新增 lifecycle notification 服务、`03-data-and-ipc.md` §5.4 |
| 验收 | 测试覆盖同 pane 同命令同退出状态重复 end event 被节流；不同 pane 不互相吞；成功与失败状态变化不互相吞；冷却窗口后可再次通知；focused tests 与 `git diff --check` 通过 |
| 风险 | 冷却过强会漏掉真实重复失败；默认值需保守，并在后续用户反馈后再决定是否配置化 |
| 回滚 | 移除节流层，恢复每个合格 hook end event 生成一条通知 |

### `NOT-02C-R` - 拆分 Toast 点击跳转契约

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P2 |
| Outcome | Windows Toast 点击后恢复托盘窗口并跳转到目标 workspace / surface / pane 的实现边界清晰，后续可分为可单测的激活参数解析和 Windows-only live smoke |
| Scope | 只做 spec/backlog refinement；阅读 `ToastNotificationHelper`、`App.xaml.cs`、`NotificationApiService`、`MainViewModel.JumpToLatestUnread` 相关入口；明确 AppUserModelID、未打包 WPF activation、目标 pane 丢失 fallback；不实现 live activation |
| 关联 | `src/ECodex/Services/ToastNotificationHelper.cs`、`src/ECodex/App.xaml.cs`、`src/ECodex/Services/NotificationApiService.cs`、`src/ECodex/ViewModels/MainViewModel.cs`、`WIN-01` Windows GUI 验证限制 |
| 验收 | 后续 `NOT-02C-*` 子项补齐 Ready 字段；明确 Toast argument 必须包含 `notificationId/workspaceId/surfaceId/paneId`；目标 pane 不存在时恢复窗口并显示可见 fallback；`pwsh ./scripts/check-doc-links.ps1` 与 `git diff --check` 通过 |
| 风险 | 非打包 WPF 的 Toast activation 与 AppUserModelID 受 Windows 环境影响，不能用纯单测证明 live 点击链路 |
| 回滚 | 仅回退 spec/backlog refinement；现有 Toast 展示不受影响 |

### `NOT-02C-1` - Toast payload 与激活参数解析

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P2 |
| Outcome | 每条 Windows Toast 都带完整通知定位参数，激活 payload 可被稳定解析为 `notificationId/workspaceId/surfaceId/paneId`，为后续跳转处理提供可单测输入 |
| Scope | 更新 `ToastNotificationHelper.ShowToast` 的 arguments，补 `paneId`（无 pane 时传空字符串）；新增小型 parser / request DTO（可放 WPF 服务层或 Core 纯模型，按最小依赖选择）；补 source/unit tests；同步 `spec/03-data-and-ipc.md` 如有偏差；不注册 Windows activation handler、不做 live Toast 点击 |
| 关联 | `src/ECodex/Services/ToastNotificationHelper.cs`、`src/ECodex.Core/Models/TerminalNotification.cs`、`tests/ECodex.Tests/CoreTests.cs`、`spec/03-data-and-ipc.md` §6.1 |
| 验收 | 测试覆盖 Toast source 包含 `action=jumpToNotification`、`notificationId`、`workspaceId`、`surfaceId`、`paneId`；parser 覆盖缺字段 / 空 `paneId` / 非 jump action；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~Toast" --no-restore` 与 `git diff --check` 通过 |
| 风险 | `ToastContentBuilder` fluent API 不易直接断言运行时 XML，可用 source test 或将参数构建抽成纯函数降低脆弱性 |
| 回滚 | 移除新增 parser 和 `paneId` argument，恢复只带 notification/workspace/surface 的 Toast payload |

### `NOT-02C-2` - Toast 激活恢复窗口并跳转

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P2 |
| Outcome | 用户点击 Toast 后，隐藏到托盘或非激活的 ECodex 会恢复窗口，并按通知定位跳到目标 workspace / surface / pane；缺目标时给出可见 fallback，不跳错 pane |
| Scope | 注册 `ToastNotificationManagerCompat.OnActivated` 或等价 Windows activation 入口；在 UI Dispatcher 中恢复 `MainWindow.RestoreFromTray`，按 `notificationId` 找通知并复用 / 扩展 `MainViewModel.JumpToNotification`；缺 workspace / surface / pane 时打开通知面板并显示 fallback；成功跳转后标记已读；不处理 Codex 等待输入、不改变通知生成策略 |
| 关联 | `src/ECodex/App.xaml.cs`、`src/ECodex/Views/MainWindow.xaml.cs`、`src/ECodex/ViewModels/MainViewModel.cs`、`src/ECodex/Services/ToastNotificationHelper.cs`、`src/ECodex.Core/Services/NotificationService.cs` |
| 验收 | source/unit tests 覆盖 activation handler 注册、Dispatcher 派发、托盘恢复调用、成功跳转标记已读、缺 pane 不调用其他 pane 且打开 fallback；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~Toast|FullyQualifiedName~Notification" --no-restore` 与 Debug build 通过 |
| 风险 | WPF UI 与 Toast activation 回调线程边界易引入竞态；非打包应用的 AUMID / shortcut 注册失败时必须降级为 in-app 通知可用 |
| 回滚 | 取消 activation 注册与 handler，保留 Toast 展示和 in-app 通知中心 |

### `NOT-02C-3` - Windows Toast live smoke 与安装策略校验

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P2 |
| Outcome | 真实 Windows 通知中心点击链路被验证：Toast 出现、点击后恢复 ECodex、定位目标 pane、失败时 fallback 可见；不可用环境有清晰诊断 |
| Scope | 增加 `scripts/smoke-toast-activation.ps1` Windows-only smoke/checklist，覆盖 unpackaged WPF AppUserModelID、开始菜单快捷方式 / 安装器注册、Toast 展示和点击激活；同步 `docs/installation.md`、`docs/troubleshooting.md` 与 `spec/04-build-deploy.md`；不扩大到 MSIX/Velopack 发布重构 |
| 关联 | `installer/ecodex.iss`、`docs/installation.md`、`docs/troubleshooting.md`、`scripts/smoke-toast-activation.ps1`、`scripts/smoke-ecodex-v2.ps1`、`spec/04-build-deploy.md` Windows Toast requirement |
| 验收 | Windows 手测记录包含 Toast payload、点击恢复、pane 聚焦、缺 pane fallback；脚本 / checklist 能在无 Toast 权限或缺快捷方式时给出可读跳过 / 失败原因；`ToastActivationSmokeScript_CoversWindowsPrereqsManualEvidenceAndSkipsClearly` 固化脚本诊断字段；`pwsh ./scripts/check-doc-links.ps1` 与 `git diff --check` 通过 |
| 风险 | 系统通知权限、专注助手、安装方式和 AUMID 注册都会影响 live smoke；CI 无法稳定证明系统 Toast 点击 |
| 回滚 | 移除新增 smoke/checklist 文档或脚本，不影响运行时 Toast 展示能力 |

### `NOT-02D-R` - 拆分 Codex 等待输入提醒契约

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | Codex 等待输入、权限确认、错误决策提醒被拆成可独立实现的低风险子切片，明确数据来源、前台门控、去重和脱敏边界 |
| Scope | 只做 spec/backlog refinement；阅读 `TerminalSession`、`SurfaceViewModel`、`CommandLifecycleNotificationService`、`NotificationService` 和 `03-data-and-ipc.md` 通知契约；不扫描真实终端输出、不写通知运行时代码 |
| 关联 | `src/ECodex.Core/Terminal/TerminalSession.cs`、`src/ECodex/ViewModels/SurfaceViewModel.cs`、`src/ECodex.Core/Services/CommandLifecycleNotificationService.cs`、`src/ECodex.Core/Services/NotificationService.cs`、`03-data-and-ipc.md` §6.2 |
| 验收 | `NOT-02D-1/2/3` Ready 字段补齐；`03-data-and-ipc.md` 明确等待输入信号来源、Codex 优先范围、前台 no-op、去重冷却、脱敏摘要和 live smoke 边界；`Not02DRefinement_DefinesCodexAttentionContractAndReadySlices` 通过；`pwsh ./scripts/check-doc-links.ps1` 与 `git diff --check` 通过 |
| 风险 | 如果直接扫描 raw bytes 或泛化关键词，会误报普通流式输出；如果绕过前台门控，会造成高噪声 Toast |
| 回滚 | 仅回退 spec/backlog refinement，不影响已有命令生命周期通知 |

### `NOT-02D-1` - Codex 等待输入信号纯检测器

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | Core 层有可单测的 Codex attention 信号检测器，能从短文本尾部识别等待输入、确认授权和错误决策语义，但不产生通知 |
| Scope | 新增纯 Core 检测器 `AgentAttentionSignalDetector` + 小 DTO，输入为已脱敏 / 可脱敏的 pane text tail、最近命令和可选 agent hint；首版只覆盖 Codex 常见短语与结构化提示；不接 UI、不读 raw bytes、不显示 Toast、不做配置化规则 |
| 关联 | `src/ECodex.Core/Services/AgentAttentionSignalDetector.cs`、`src/ECodex.Core/Terminal/TerminalSession.cs` 输出 buffer、`CommandLogService.SanitizeCommandForStorage` / transcript 脱敏思路、`03-data-and-ipc.md` §6.2 |
| 验收 | `AgentAttentionSignalDetectorTests` 覆盖 Codex 等待用户输入 / approval / confirm / error decision 命中；普通日志、build 输出、流式回答不命中；长文本只返回脱敏短摘要；空输入 no-op；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~AgentAttention" --no-restore` 与 `git diff --check` 通过 |
| 风险 | Codex 文案版本会变；检测规则必须保守，避免因为 “error” / “input” 单词出现在普通日志里就提醒 |
| 回滚 | 删除检测器与测试；后续通知接入不受影响 |

### `NOT-02D-2` - 等待输入信号接入低噪声通知

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | 当 ECodex 隐藏到托盘或非激活时，Codex pane 出现等待输入 / 确认 / 错误决策信号会生成一次未读通知并可点击跳回 pane |
| Scope | 在 `SurfaceViewModel` 的 `TerminalSession.OutputReceived` 后读取 buffer tail 并调用 `NOT-02D-1` 检测器；新增小型去重 / 冷却状态（同 pane + signal + summary）；复用 `NotificationService.AddNotification` 和 Toast payload；前台活跃 no-op；不新增设置项、不支持非 Codex agent |
| 关联 | `src/ECodex/ViewModels/SurfaceViewModel.cs`、`src/ECodex/App.xaml.cs` 前台判断、`src/ECodex.Core/Services/AgentAttentionNotificationService.cs`、`src/ECodex.Core/Services/NotificationService.cs`、`ToastActivationParser` |
| 验收 | 已覆盖：后台 / 非激活命中信号创建 `NotificationSource.AgentAttention` 通知；前台活跃 no-op；同 pane 同摘要冷却；不同 pane / 不同摘要不互相吞；通知带 workspace / surface / pane 且复用 `NOT-02C` Toast payload 跳转路径；focused tests 与 Debug build 通过 |
| 风险 | `OutputReceived` 频繁触发，必须避免每个 chunk 扫描大缓冲或重复通知；读取 UI buffer 要保持线程边界安全 |
| 回滚 | 移除接线与去重状态，保留纯检测器 |

### `NOT-02D-3` - Codex 等待输入 live smoke 与文档

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P2 |
| Outcome | 维护者可用手测脚本 / checklist 证明真实 Codex 等待输入场景会低噪声提醒，且普通输出不会误报 |
| Scope | 扩展 Windows-only `scripts/smoke-toast-activation.ps1 -Scenario CodexAttention`，覆盖窗口非激活、Codex-like 等待输入触发、`AgentAttention` 通知、普通输出负控与手测证据模板；同步 troubleshooting / installation / release readiness；不做可配置规则 UI |
| 关联 | `scripts/smoke-toast-activation.ps1`、`docs/troubleshooting.md`、`docs/release-readiness.md`、`03-data-and-ipc.md` §6.2 |
| 验收 | `CodexAttentionSmokeScript_CoversAgentAttentionScenarioAndNegativeControl` 覆盖 `-Scenario CodexAttention`、`agentAttentionPayload`、普通输出负控和手测字段；`CodexAttentionSmokeDocs_CoverInstallTroubleshootingReleaseAndBacklog` 覆盖 installation / troubleshooting / release readiness；`pwsh ./scripts/check-doc-links.ps1` 与 `git diff --check` 通过 |
| 风险 | 真实 Codex CLI 文案和 approval 模式会随版本变化；脚本应能记录环境与原始触发样例，避免把 CI 静态测试当 live 证据 |
| 回滚 | 删除 smoke/checklist 文档，不影响检测器与通知接线 |

### `OBS-01-R` - 拆分失败 loop 证据包契约

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | 维护者明确 Session Vault / 命令日志 / terminal transcript / daemon log 如何组成失败 loop 证据包，并确认 AgentConversation 相关类型当前只是 planned，不作为现有事实源 |
| Scope | 只做 spec/backlog refinement；阅读 `SessionVaultWindow`、`CommandLogService`、`TerminalTranscriptEntry`、`02-modules.md` 与 `03-data-and-ipc.md`；不读取真实日志内容、不实现 UI、不新增存储 |
| 关联 | `spec/03-data-and-ipc.md` §8.1、`spec/02-modules.md`、`src/ECodex/Views/SessionVaultWindow.xaml.cs`、`src/ECodex.Core/Services/CommandLogService.cs` |
| 验收 | `Obs01RefinementTests` 覆盖 `FailureLoopEvidencePackage` 契约、当前源码缺少 `AgentConversationStoreService` 的显式边界、`OBS-01-1` ready 字段；`pwsh ./scripts/check-doc-links.ps1` 与 `git diff --check` 通过 |
| 风险 | 如果把 planned Agent 存储当作已存在，会导致后续实现直接失败；如果从 UI 扫日志，会绕过脱敏和时间窗约束 |
| 回滚 | 仅回退 OBS-01 契约和 backlog 拆分，不影响现有 Session Vault |

### `OBS-01-1` - 失败 loop 证据包 Core DTO 与装配器

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | Core 层能用 fixture 数据装配一个 `FailureLoopEvidencePackage`，把失败命令、相关 transcript 摘要和 daemon log 片段按 workspace / surface / pane / time window 串起来 |
| Scope | 新增纯 Core DTO 与装配器；输入为已脱敏的 `CommandLogEntry`、`TerminalTranscriptEntry` 元数据 / transcript 摘要、daemon log 行集合；Agent message 来源首版为空集合；不读取真实用户日志、不做 WPF UI、不写磁盘、不接 Session Vault 窗口 |
| 关联 | `CommandLogService.GetForDate(...)`、`CommandLogService.LoadTerminalTranscriptContent(...)`、`TerminalTranscriptEntry`、`03-data-and-ipc.md` §8.1 |
| 验收 | 单测覆盖非零 exit code 选为失败 loop、时间窗关联 transcript、paneId 过滤 daemon log、transcript 摘要截断 / 脱敏输入保持、AgentMessages 为空集合；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~FailureLoopEvidence" --no-restore` 与 `git diff --check` 通过 |
| 风险 | 证据包过大或包含敏感内容；首版必须只处理调用方传入的已脱敏内容并限制摘要长度 |
| 回滚 | 删除 DTO / 装配器和测试；保留 OBS-01 契约文档 |

### `OBS-01-2` - 失败 loop 证据源加载适配器

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | Core 层能通过可替换 provider 从命令日志、transcript 元数据 / 内容和 daemon log 行集合生成 `FailureLoopEvidencePackage`，为后续 Session Vault UI 提供单一入口 |
| Scope | 新增纯 Core provider / adapter seam，调用 `FailureLoopEvidenceAssembler`；测试用 fixture provider，不读取真实 `%USERPROFILE%` 日志、不接 WPF UI、不写磁盘、不接 planned Agent 存储 |
| 关联 | `FailureLoopEvidenceAssembler`、`CommandLogService.GetForDate(...)`、`CommandLogService.GetTerminalTranscripts(...)`、`CommandLogService.LoadTerminalTranscriptContent(...)`、`03-data-and-ipc.md` §8.1 |
| 验收 | 单测覆盖 provider 按日期加载命令、只为时间窗内 transcript 调用内容加载、daemon log provider 输出被 paneId / time window 过滤、空 Agent provider 保持空集合；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~FailureLoopEvidence" --no-restore` 与 `git diff --check` 通过 |
| 风险 | 如果 adapter 直接 new `CommandLogService` 或读取真实 profile，会触发保留策略 / 脱敏扫描副作用；首版必须依赖注入 provider，真实接线留到后续 UI/应用层切片 |
| 回滚 | 删除 adapter / provider seam 与对应测试；保留 DTO / 装配器 |

### `OBS-01-3` - daemon log 行解析与有限 tail provider

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | Core 层能把 `DaemonClient.FormatDaemonLogLine(...)` 兼容的 daemon-debug 行解析为 `FailureLoopDaemonLogInput`，并从调用方显式提供的日志路径读取有限 tail，供 `OBS-01-2` provider seam 使用 |
| Scope | 新增纯 Core daemon log parser / bounded tail helper；测试使用 fixture 文本或临时文件；不默认读取 `%USERPROFILE%\.ecodex\daemon-debug.log`、不接 UI、不读取 secrets、不做真实日志扫描 |
| 关联 | `src/ECodex.Core/IPC/DaemonClient.cs` 的 `FormatDaemonLogLine(...)`、`FailureLoopEvidenceCollector`、`03-data-and-ipc.md` §8.1 |
| 验收 | 单测覆盖安全 key=value、带引号 / 转义 message、paneId 提取、时间窗过滤、malformed line 跳过、最大 tail 行数限制；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~FailureLoopEvidence" --no-restore` 与 `git diff --check` 通过 |
| 风险 | daemon log 格式若漂移会导致证据缺失；解析器必须和 `FormatDaemonLogLine` 的转义规则保持一致，且不能因为 malformed 行抛异常中断证据包 |
| 回滚 | 删除 daemon log parser / tail helper 与测试；保留 provider seam |

### `OBS-01-4` - 失败 loop 证据包预览格式化器

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | Core 层能把 `FailureLoopEvidencePackage` 格式化成紧凑、可复制的复盘预览文本，后续 Session Vault UI 只负责展示和复制，不重新拼接证据 |
| Scope | 新增纯 Core formatter；覆盖命令、transcript 摘要、daemon log、空 AgentMessages 的提示和 package 元数据；不接 WPF UI、不读写磁盘、不新增导出文件 |
| 关联 | `FailureLoopEvidencePackage`、`FailureLoopEvidenceCollector`、`03-data-and-ipc.md` §8.1 |
| 验收 | 单测覆盖失败命令区块、transcript 摘要截断标记、daemon log 区块、Agent 会话 planned/empty 提示、无证据包时 no-op 文案；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~FailureLoopEvidence" --no-restore` 与 `git diff --check` 通过 |
| 风险 | 预览文本如果过长会把 UI/通知撑爆；formatter 必须保守限制每类条目数量或复用已截断摘要，真实导出留到后续切片 |
| 回滚 | 删除 formatter 与测试；保留 evidence package / provider seam |

### `OBS-01-5` - Session Vault 失败 loop 预览入口

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | Session Vault 能提供一个受控的“生成失败 loop 预览”入口，复用 Core collector / formatter 输出只读预览文本，维护者不用在 UI 层重新拼证据 |
| Scope | 先做最小 WPF/source-level 接线与测试：从选中的 transcript / pane 上下文构造 evidence request，调用注入的 Core provider/formatter，展示或保存到现有只读预览区域；不做真实 Toast、不新增导出文件、不接 planned Agent 存储、不读取 secrets |
| 关联 | `src/ECodex/Views/SessionVaultWindow.xaml.cs`、`FailureLoopEvidenceCollector`、`FailureLoopEvidencePreviewFormatter`、`03-data-and-ipc.md` §8.1 |
| 验收 | source/unit test 覆盖 Session Vault 入口只调用 Core formatter、不直接扫描 daemon log/profile、不在 UI 层读取 transcript 内容；失败或无证据时显示 no-op 文案；focused tests、`pwsh ./scripts/check-doc-links.ps1` 与 `git diff --check` 通过 |
| 风险 | WPF live 行为需要 Windows GUI 手测；首版只能用 source tests 保证接线边界，真实点击/布局验收留到 `WIN-01` |
| 回滚 | 移除 Session Vault 入口接线，保留 Core evidence package 能力 |

### `OBS-01-6` - Session Vault 失败 loop GUI smoke checklist

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | 维护者能用明确 checklist 在 Windows GUI 中验证 Session Vault “生成失败 loop 预览”按钮、复制行为、无证据 no-op 和不扫描真实 daemon log 的边界 |
| Scope | 新增/扩展手动 smoke checklist 或 docs；可用 source test 覆盖 checklist 文案；不做自动点击 WPF、不读取真实用户日志、不新增安装/发布步骤 |
| 关联 | `docs/session-restore.md`、`scripts/smoke-ecodex-v2.ps1` 或独立 smoke 文档、`WIN-01` |
| 验收 | 文档/checklist 包含准备失败命令、捕获 transcript、打开 Session Vault、生成预览、复制结果、无证据负控、不能作为 AgentConversation live 证据的限制；focused docs/source tests、`pwsh ./scripts/check-doc-links.ps1` 与 `git diff --check` 通过 |
| 风险 | 自动化 WPF 点击成本高且易受环境影响；首版只交付可执行手测 checklist，真实截图证据由 Windows GUI 手测补充 |
| 回滚 | 删除新增 checklist，不影响 Session Vault 预览入口 |

### `OBS-01-7` - AgentConversation planned 存储接入前 refinement

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | 在接入 Agent 会话证据前，维护者明确当前源码仍没有 `AgentConversationStoreService` / `AgentConversationThread`，并拆出不破坏现有 Session Vault 的最小存储契约或继续保持 planned |
| Scope | 只做 spec/backlog refinement 与 source tests；复核 `src/ECodex.Core/Models`、`src/ECodex.Core/Services`、`src/ECodex/Services` 是否出现 AgentConversation 相关类型；不实现 Agent runtime、不写真实 agent 日志、不读取 secrets |
| 关联 | `spec/02-modules.md` planned AgentConversation 行、`spec/03-data-and-ipc.md` §8.1、`OBS-01` draft 行 |
| 验收 | 测试覆盖 AgentConversation 类型仍为 planned / absent，新增下一可执行子切片 ready 字段，明确 Session Vault preview 继续以空 AgentMessages 运行；focused tests、`pwsh ./scripts/check-doc-links.ps1` 与 `git diff --check` 通过 |
| 风险 | 直接实现 Agent 存储会扩大到 runtime / LLM 工具调用 / token 统计；本切片只做边界收敛，避免把 planned 类型当现有依赖 |
| 回滚 | 回退 refinement 文档，不影响已完成 failure-loop Core 和 Session Vault 预览 |

### `OBS-01-8` - AgentConversation Core DTO 与存储契约

| 字段 | 内容 |
|---|---|
| 状态 | ready |
| 优先级 | P1 |
| Outcome | Core 层拥有可单测的 AgentConversation DTO 与存储服务最小契约，为后续 failure-loop 证据包接入 AgentMessages 提供真实事实源 |
| Scope | 新增 `AgentConversationThread` / `AgentConversationMessage` DTO 和纯 Core `AgentConversationStoreService`；只读写调用方传入的测试目录或明确配置根目录；不接 Agent runtime、不调用 LLM、不接 Session Vault UI、不读取 secrets |
| 关联 | `spec/03-data-and-ipc.md` §8 planned 契约、`FailureLoopEvidencePackage.AgentMessages`、`OBS-01-7` refinement |
| 验收 | 单测覆盖 create/search/thread index、append message、token 聚合、last preview、多值 JSON/BOM/JSONL 兼容读取、测试目录隔离；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~AgentConversation" --no-restore` 与 `git diff --check` 通过 |
| 风险 | 存储实现可能误读真实 `%USERPROFILE%` agent 数据或扩大到 runtime；首版必须依赖显式根目录，真实 profile 接线留到后续切片 |
| 回滚 | 删除 AgentConversation DTO / store 和测试；failure-loop evidence 继续保持空 AgentMessages |

### `PKG-02` - Inno 安装与卸载向导中文化

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | 使用 Inno Setup 安装包的用户，在安装、升级覆盖、创建快捷方式、完成页、卸载确认与卸载进度等流程中看到一致的简体中文界面 |
| Scope | 调整 `installer/ecodex.iss` 的语言配置、安装任务描述、运行后提示和必要的自定义消息；同步 `docs/installation.md` 的构建/验收说明；不改变安装目录、卸载数据保留策略、Velopack/MSIX 行为或发布产物命名 |
| 关联 | `installer/ecodex.iss`、`docs/installation.md`、`04-build-deploy.md` §Installer / Update |
| 验收 | `InnoSetupScriptTests` 覆盖固定简体中文语言包、隐藏语言选择、setup logging、桌面快捷方式 / 完成页自定义中文文案、卸载只清理 `{app}` 和 installation 文档验收清单；`.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter "FullyQualifiedName~InnoSetupScriptTests" --no-restore` 通过；Inno Setup Compiler 实机编译待人工确认 |
| 风险 | 构建机缺少 `compiler:Languages\ChineseSimplified.isl` 导致编译失败；自定义英文文案遗漏；第三方系统按钮或 Windows 控件文案不能被 Inno 脚本完全覆盖 |
| 回滚 | 移除新增自定义消息与强制语言配置，恢复 Inno 默认语言行为；保留现有 `ChineseSimplified.isl` 引用不影响安装功能 |

### `AGL-02` - 将 handoff note 接入 PR 流程

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P1 |
| Outcome | Agent 中断或交接时，PR 描述能直接承载固定 handoff 信息 |
| Scope | 可选更新 `.github/PULL_REQUEST_TEMPLATE.md` 或新增 docs 指引；不改运行时代码 |
| 关联 | `00-agile-ai-delivery.md` §3、§8，本文 §7 |
| 验收 | PR 模板新增可选 Handoff 区块，包含目标、已改文件、已验证、未跑验证 / 原因、风险、下一步、回滚点；`CommunityTemplateTests.PullRequestTemplate_CoversTestingDocsRiskAndCurrentPaths` 通过 |
| 风险 | 模板过重导致普通 PR 填写成本上升 |
| 回滚 | 从 PR 模板移除该块，保留本文 §7 作为内部手册 |

### `DOG-01` - 新增 ECodex 自举 dogfood 配置样例

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P2 |
| Outcome | 维护者能用 ECodex 命令面板一键执行本仓常用 build/test/docs 命令 |
| Scope | 新增示例 `.ecodex/ecodex.example.json` 或 `docs/configuration.md` 示例；不写入用户真实本地配置 |
| 关联 | `05-cli-commands.md`、`docs/custom-commands.md` |
| 验收 | 示例包含 build、unit test、docs build、status/health；所有高风险命令 `confirm:true` |
| 风险 | 示例路径在 macOS / Windows 不一致 |
| 回滚 | 删除示例文件，不影响源码 |

### `DOG-02` - 设计 ecodex.v2 本地 smoke 脚本

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P2 |
| Outcome | 用 ECodex 自身自动化 API 验证 workspace / pane / browser 的最小闭环 |
| Scope | 先写 spec 或脚本草案；涉及 live app 的执行标记 Windows-only；不要求当前环境跑通 WPF |
| 关联 | `03-data-and-ipc.md`、`05-cli-commands.md` |
| 验收 | `ECodexV2SmokeScript_CoversManualLiveApiLoopAndSkipsClearly` 覆盖 `scripts/smoke-ecodex-v2.ps1` 的 status -> workspace.create -> pane.write/read -> browser.open -> browser.snapshot 步骤、缺环境 skip 与不接 setup install；`CliDocs_CoverGlobalFlagsV1V2AndOperationalCommands` 覆盖 CLI 文档入口；真实 live smoke 手动运行 |
| 风险 | 依赖正在运行的 ECodex 主应用 |
| 回滚 | 脚本不接 CI，仅作为手动 smoke |

### `REL-01` - 发布前证据清单自动化

| 字段 | 内容 |
|---|---|
| 状态 | done |
| 优先级 | P2 |
| Outcome | Release 前能快速汇总测试、docs、perf、doctor 的证据路径 |
| Scope | `docs/release-readiness.md` 或脚本；不改变 release workflow |
| 关联 | `04-build-deploy.md`、`docs/release-readiness.md` |
| 验收 | `ReleaseEvidenceScriptTests` 覆盖 `scripts/release-evidence.ps1` 的 build、unit_tests、docs_build、perf_report、doctor、release_workflow、Windows-only 标记和 release artifact 名称；`ReleaseReadinessDocs_CoverP0P1GateAndValidation` 覆盖 docs 入口；`pwsh ./scripts/release-evidence.ps1` stdout smoke 通过 |
| 风险 | 与现有 GitHub artifacts 命名漂移 |
| 回滚 | 保留人工 release checklist |

---

## 3. Draft / Refinement 队列

这些项需要先补 Ready 信息，不能自动开工。

| ID | 状态 | Outcome | 缺口 | 下一步 |
|---|---|---|---|---|
| `OBS-01` | draft | Agent 会话、命令日志、terminal transcript 可串成一次失败 loop 的复盘视图 | 已完成 `OBS-01-1` Core DTO / 装配器、`OBS-01-2` provider seam、`OBS-01-3` daemon log parser、`OBS-01-4` preview formatter、`OBS-01-5` Session Vault 入口、`OBS-01-6` GUI smoke checklist 与 `OBS-01-7` planned 边界复核；AgentConversation 相关源码当前未落地 | 先完成 `OBS-01-8` AgentConversation Core DTO / store，再接入 failure-loop AgentMessages |
| `BRS-01` | draft | Browser scripting API 增加更多真实页面回归样例 | 需要本地测试页和 WebView2 环境策略 | 先列 P0 API 现有覆盖矩阵 |
| `PKG-01` | draft | 安装 / 更新 / 卸载 rollback 证据更清晰 | 需要 Windows 测试环境和 artifact 命名 | 先整理 release workflow 产物清单 |
| `DX-01` | draft | 新贡献者按 `spec/` 能 30 分钟跑通第一个小 PR | 需要观测真实 onboarding 缺口 | 先用一次 fresh clone 记录摩擦点 |

---

## 4. Blocked 队列

| ID | 状态 | 阻塞原因 | 解除条件 |
|---|---|---|---|
| `WIN-01` | blocked | WPF / ConPTY / WebView2 live 验证需要 Windows 图形环境 | 在 Windows 机器上运行对应 smoke 并回填证据 |
| `NET-01` | blocked | 需要联网或外部服务的检查不能默认自动执行 | 人工批准网络 / 凭据 / 发布操作后单独执行 |

---

## 5. Done 归档

### 5.1 1.0 基线归档

旧 M0-M7 backlog 已完成，详细用户可见变化见 `CHANGELOG.md` 的 `1.0.0` 节，公开路线见 `docs/roadmap.md`。后续不再在本文件维护历史 M0-M7 明细，避免当前队列被已完成任务淹没。

| 范围 | 状态 | 归档位置 |
|---|---|---|
| M0 工程基线 | done | `CHANGELOG.md`、`docs/roadmap.md` |
| M1 UI/UX 与 `ecodex.json` | done | `CHANGELOG.md`、`docs/roadmap.md` |
| M2 会话恢复 | done | `CHANGELOG.md`、`docs/session-restore.md` |
| M3 Browser Pane | done | `CHANGELOG.md`、`docs/getting-started.md` |
| M4 Browser scripting | done | `CHANGELOG.md`、`docs/browser-api.md` |
| M5 v2 协议 | done | `CHANGELOG.md`、`docs/cli.md` |
| M6 安装更新 | done | `CHANGELOG.md`、`docs/installation.md` |
| M7 文档与社区 | done | `CHANGELOG.md`、`CONTRIBUTING.md`、`SECURITY.md` |

### 5.2 1.0 发布前专项归档

| ID | 状态 | 说明 |
|---|---|---|
| `M7-A-03` | done | 文档站统一为简体中文单语，不再维护同页中英双语内容 |
| `X-03` | [x] 风险登记刷新 | 2026-06-15 发布前同步 P0/P1 门槛、CI Unicode smoke 与 release perf artifact 风险 |

---

## 6. Backlog 条目模板

新增条目时复制此模板。只有字段完整才能进入 `ready`。

```markdown
### `ID` - 标题

| 字段 | 内容 |
|---|---|
| 状态 | draft / ready / doing / blocked / done / icebox |
| 优先级 | P0 / P1 / P2 / P3 |
| Outcome | 完成后用户或维护者得到什么 |
| Scope | 涉及文件 / 模块；明确非目标 |
| 关联 | spec / docs / issue / 代码入口 |
| 验收 | 可执行命令、手测脚本或明确截图要求 |
| 风险 | 安全、兼容、性能、发布或验证风险 |
| 回滚 | 如何关闭、撤销或降级 |
```

---

## 7. Handoff Note 模板

### Handoff - SES-01

- 目标：ECodex 重开后自动接回 daemon 托管的后台终端，并提供设置化保留策略与退出终止入口。
- 已完成：启动 S1；将 `SES-01` 标记为 `doing`；完成首个子切片后经人工确认转为 `done`；修正 daemon 终端自然退出后 active sessions 不移除的问题；S4 新增 `PreserveDaemonSessionsOnClose` 持久化设置与 `app.exit {"terminateTerminals":true}` 内部 IPC；移除 daemon `SESSION_CLOSE_ALL` 协议与右键“终止全部保留会话”入口；同步公开路线图、session restore 文档与 daemon IPC spec。
- 已改文件：`docs/roadmap.md`、`docs/session-restore.md`、`spec/03-data-and-ipc.md`、`spec/05-cli-commands.md`、`spec/07-implementation-backlog.md`、`src/ECodex.Core/IPC/DaemonMessages.cs`、`src/ECodex.Core/IPC/DaemonClient.cs`、`src/ECodex.Daemon/DaemonSessionManager.cs`、`src/ECodex.Daemon/DaemonPipeServer.cs`、`src/ECodex/Views/MainWindow.xaml`、`src/ECodex/Views/MainWindow.xaml.cs`、`tests/ECodex.Tests/CoreTests.cs`。
- 已验证：S1 阶段 `git diff --check`、daemon 消息测试、Debug build 与 live GUI attach 已通过；S4 阶段新增 focused tests 覆盖 `DaemonMessageRoundTripTests`、`DaemonSessionTerminationPolicyTests`、`AppLifecycleApiServiceTests`。
- 未验证 / 原因：无；Windows GUI / ConPTY live attach 验收已由人工确认完成。
- 当前阻塞：无。
- 下一步建议：按 ready 队列继续推进下一项。
- 根因审计：公开路线图当前重点停留在 M7 的内容来自 `0d7cdf64 docs: localize docs site to simplified chinese`，S0 spec 敏捷化后未同步 `docs/roadmap.md`；daemon 自然退出未移除 active session 的原始逻辑来自 `7e9dc296`（旧 `src/Cmux.Daemon/DaemonSessionManager.cs`）。
- 回滚点：恢复 daemon `SESSION_CLOSE_ALL` 协议与 UI 菜单；移除 `PreserveDaemonSessionsOnClose`、`DaemonSessionTerminator`、`app.exit` 终止逻辑；保留自然退出移除 active session 的修正可单独评估。

每轮结束，如果任务没有完全 done，必须留下 handoff：

```markdown
### Handoff - ID

- 目标：
- 已完成：
- 已改文件：
- 已验证：
- 未验证 / 原因：
- 当前阻塞：
- 下一步建议：
- 回滚点：
```

---

## 8. 维护规则

- 每次 loop 开始：确认选中条目为 `ready`，再把状态改为 `doing`。
- 每次 loop 结束：只能改成 `done`、`blocked`、`ready`（拆小后）或保留 `doing` 并写 handoff。
- 每周 review：清理超过 2 周未动的 `ready`，补齐验收或降回 `draft`。
- 每个 `ready` 项必须能由一个 Agent 在单轮上下文内读完相关资料。
- 不允许把“继续完善”“优化体验”这类无法验收的句子作为 backlog 标题。
