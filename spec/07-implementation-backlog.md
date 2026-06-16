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
| `NOT-02A` | ready | PowerShell shell integration hook 默认随 App 首次启动安装，可回传命令开始、结束和退出码 | 扩展现有 setup/profile 机制：ECodex 专属标记块、写入前备份到 `%USERPROFILE%\.ecodex\backups\`、`setup status` 可检查、`setup uninstall --write true` 可移除；默认仅 PowerShell，cmd/Git Bash 后续再做；冲突时跳过并提示，不静默覆盖 | 首次启动缺 hook 时写入标记块并备份；再次启动幂等；用户已有冲突块时跳过；status 能显示 installed/missing/drifted；uninstall 只移除 ECodex 标记块；不读取 `.env*` / secrets |
| `NOT-02B` | draft | 后台/非激活时，基于命令生命周期发送完成、失败、等待输入通知，并进入未读中心 | 依赖 `NOT-02A` 的 hook 事件；定义 pane/session/command id 数据流、退出码映射、通知去重与节流；没有 hook 时降级到 OSC / `ecodex notify` / 关键输出规则 | 后台执行命令成功结束只产生完成通知；非 0 退出码产生失败通知；前台活跃时不刷 Toast；通知能定位 workspace/surface/pane；普通流式输出不逐条通知 |
| `NOT-02C` | draft | 点击 Windows Toast 精准打开 ECodex 并跳转到对应 workspace / surface / pane | 明确 Toast activation/AppUserModelID/非打包应用策略；复用通知 ID、workspaceId、surfaceId、paneId；窗口隐藏到托盘时也能恢复并跳转 | Toast 点击后窗口显示并聚焦对应 pane；通知标记为已读；目标 pane 不存在时给出可见 fallback；Windows Toast 不可用时不影响应用主流程 |
| `NOT-02D` | draft | Codex 等待输入、关键确认、错误决策等交互状态能触发低噪声提醒 | 在生命周期通知之外补充状态识别；优先识别 Codex 常见等待输入/确认语义，再扩展可配置规则；仅窗口隐藏/非激活且匹配关键状态时提醒 | Codex 等待用户输入时后台收到通知；普通日志不通知；同一状态有去重/冷却；规则可在后续版本扩展 |

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

### `PKG-02` - Inno 安装与卸载向导中文化

| 字段 | 内容 |
|---|---|
| 状态 | ready |
| 优先级 | P1 |
| Outcome | 使用 Inno Setup 安装包的用户，在安装、升级覆盖、创建快捷方式、完成页、卸载确认与卸载进度等流程中看到一致的简体中文界面 |
| Scope | 调整 `installer/ecodex.iss` 的语言配置、安装任务描述、运行后提示和必要的自定义消息；同步 `docs/installation.md` 的构建/验收说明；不改变安装目录、卸载数据保留策略、Velopack/MSIX 行为或发布产物命名 |
| 关联 | `installer/ecodex.iss`、`docs/installation.md`、`04-build-deploy.md` §Installer / Update |
| 验收 | Windows 环境使用 Inno Setup Compiler 编译通过；安装向导、覆盖安装向导、卸载向导、开始菜单/桌面快捷方式任务与完成页用户可见文案均为简体中文；静默安装/卸载不受影响；卸载仍只清理 `{app}`，保留 `%USERPROFILE%\.ecodex` |
| 风险 | 构建机缺少 `compiler:Languages\ChineseSimplified.isl` 导致编译失败；自定义英文文案遗漏；第三方系统按钮或 Windows 控件文案不能被 Inno 脚本完全覆盖 |
| 回滚 | 移除新增自定义消息与强制语言配置，恢复 Inno 默认语言行为；保留现有 `ChineseSimplified.isl` 引用不影响安装功能 |

### `AGL-02` - 将 handoff note 接入 PR 流程

| 字段 | 内容 |
|---|---|
| 状态 | ready |
| 优先级 | P1 |
| Outcome | Agent 中断或交接时，PR 描述能直接承载固定 handoff 信息 |
| Scope | 可选更新 `.github/PULL_REQUEST_TEMPLATE.md` 或新增 docs 指引；不改运行时代码 |
| 关联 | `00-agile-ai-delivery.md` §3、§8，本文 §7 |
| 验收 | PR 模板或说明中出现目标、已改文件、验证、未跑验证、风险、下一步、回滚点 |
| 风险 | 模板过重导致普通 PR 填写成本上升 |
| 回滚 | 从 PR 模板移除该块，保留本文 §7 作为内部手册 |

### `DOG-01` - 新增 ECodex 自举 dogfood 配置样例

| 字段 | 内容 |
|---|---|
| 状态 | ready |
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
| 状态 | ready |
| 优先级 | P2 |
| Outcome | 用 ECodex 自身自动化 API 验证 workspace / pane / browser 的最小闭环 |
| Scope | 先写 spec 或脚本草案；涉及 live app 的执行标记 Windows-only；不要求当前环境跑通 WPF |
| 关联 | `03-data-and-ipc.md`、`05-cli-commands.md` |
| 验收 | 脚本步骤覆盖 status -> workspace.create -> pane.write/read -> browser.open -> browser.snapshot；缺环境时输出清晰 skip |
| 风险 | 依赖正在运行的 ECodex 主应用 |
| 回滚 | 脚本不接 CI，仅作为手动 smoke |

### `REL-01` - 发布前证据清单自动化

| 字段 | 内容 |
|---|---|
| 状态 | ready |
| 优先级 | P2 |
| Outcome | Release 前能快速汇总测试、docs、perf、doctor 的证据路径 |
| Scope | `docs/release-readiness.md` 或脚本；不改变 release workflow |
| 关联 | `04-build-deploy.md`、`docs/release-readiness.md` |
| 验收 | 清单覆盖 build/test/docs/perf/release workflow；明确哪些是 Windows-only |
| 风险 | 与现有 GitHub artifacts 命名漂移 |
| 回滚 | 保留人工 release checklist |

---

## 3. Draft / Refinement 队列

这些项需要先补 Ready 信息，不能自动开工。

| ID | 状态 | Outcome | 缺口 | 下一步 |
|---|---|---|---|---|
| `OBS-01` | draft | Agent 会话、命令日志、terminal transcript 可串成一次失败 loop 的复盘视图 | 需要确认 UI 入口与用户故事 | 先读 `02-modules.md` 中 Agent / Session Vault 模块，写行为 spec |
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
