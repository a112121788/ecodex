# ECode 敏捷实施 Backlog

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

## 1. 当前冲刺：S0 - spec 敏捷化与 AI loop

目标：把 `spec/` 从静态规划文档重构为可指导 AI 自动化开发的敏捷交付系统。

| ID | 状态 | Outcome | Scope | Acceptance |
|---|---|---|---|---|
| `S0-01` | done | 新增 AI 自动 loop 入口文档 | `spec/00-agile-ai-delivery.md` | 文档包含 loop、DoR、DoD、停止规则、验证矩阵 |
| `S0-02` | done | 路线图改为 Now / Next / Later | `spec/06-roadmap.md` | 不再把已完成 M0-M7 当当前 backlog；保留 1.0 基线归档 |
| `S0-03` | done | backlog 改为敏捷队列 | `spec/07-implementation-backlog.md` | 有状态规则、任务模板、ready 队列和 refinement 规则 |
| `S0-04` | done | spec 索引同步新入口 | `spec/README.md` | 阅读顺序以 `00` 开始，状态表反映敏捷交付用途 |

---

## 2. Ready 队列（Now）

### `SES-01` - ECode 重开自动接回后台终端进程

| 字段 | 内容 |
|---|---|
| 状态 | ready |
| 优先级 | P0 |
| Outcome | 用户关闭 ECode 窗口后，在同一 Windows 登录会话内重新打开，原 Codex / PowerShell 等终端进程仍由 daemon 托管，终端自动 attach 到原会话并可继续输入输出 |
| Scope | 首个切片覆盖“正常关闭主窗口 -> daemon 继续托管终端 -> 重开自动 attach”；涉及 `src/ECode` 关闭/启动流程、`src/ECode.Core` daemon session mapping、`session.json` pane/session id 持久化、状态可见性与“终止全部保留会话”入口；不覆盖 Windows 重启/关机后的进程存活，不做命令回放 |
| 关联 | `01-architecture.md` §6.1/§6.4、`03-data-and-ipc.md` §3.8/§4、`docs/session-restore.md` |
| 验收 | Windows 手测：在 pane 启动 `pwsh` / Codex，关闭 ECode，确认后台会话未退出；重开 ECode 后恢复 workspace/surface/pane 布局并 attach 到同一进程，`pane.write/read` 可继续交互；无重复 shell；提供可见状态和“终止全部保留会话”入口；daemon 不可达时展示过期/已断开并回退到快照，不静默执行命令 |
| 风险 | 后台进程残留、资源泄漏、用户误以为关闭等于退出、Agent 长任务继续产生副作用、pane id / session id 映射过期 |
| 回滚 | 设置项或命令禁用默认保活，关闭窗口恢复为终止本地会话 + 仅保存快照；必要时清理 daemon 会话并保留 `session.json` 快照恢复 |

### `PKG-02` - Inno 安装与卸载向导中文化

| 字段 | 内容 |
|---|---|
| 状态 | ready |
| 优先级 | P1 |
| Outcome | 使用 Inno Setup 安装包的用户，在安装、升级覆盖、创建快捷方式、完成页、卸载确认与卸载进度等流程中看到一致的简体中文界面 |
| Scope | 调整 `installer/ecode.iss` 的语言配置、安装任务描述、运行后提示和必要的自定义消息；同步 `docs/installation.md` 的构建/验收说明；不改变安装目录、卸载数据保留策略、Velopack/MSIX 行为或发布产物命名 |
| 关联 | `installer/ecode.iss`、`docs/installation.md`、`04-build-deploy.md` §Installer / Update |
| 验收 | Windows 环境使用 Inno Setup Compiler 编译通过；安装向导、覆盖安装向导、卸载向导、开始菜单/桌面快捷方式任务与完成页用户可见文案均为简体中文；静默安装/卸载不受影响；卸载仍只清理 `{app}`，保留 `%USERPROFILE%\.ecode` |
| 风险 | 构建机缺少 `compiler:Languages\ChineseSimplified.isl` 导致编译失败；自定义英文文案遗漏；第三方系统按钮或 Windows 控件文案不能被 Inno 脚本完全覆盖 |
| 回滚 | 移除新增自定义消息与强制语言配置，恢复 Inno 默认语言行为；保留现有 `ChineseSimplified.isl` 引用不影响安装功能 |

### `AGL-01` - 增加 spec/docs 链接检查脚本

| 字段 | 内容 |
|---|---|
| 状态 | ready |
| 优先级 | P1 |
| Outcome | AI loop 修改文档后能快速发现坏链接或旧文件名，降低文档漂移 |
| Scope | `scripts/` 新增轻量检查脚本；可选接入 `scripts/ci.ps1` 的 docs gate；不改用户文档内容 |
| 关联 | `00-agile-ai-delivery.md` §6、`04-build-deploy.md` §4.3 |
| 验收 | 本地运行脚本能扫描 `spec/*.md` 与 `docs/*.md` 中相对链接；故意填一个坏链接时返回非 0 |
| 风险 | Windows PowerShell 与 macOS shell 路径差异 |
| 回滚 | 从 CI 中移除 gate，保留脚本手动运行 |

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

### `DOG-01` - 新增 ECode 自举 dogfood 配置样例

| 字段 | 内容 |
|---|---|
| 状态 | ready |
| 优先级 | P2 |
| Outcome | 维护者能用 ECode 命令面板一键执行本仓常用 build/test/docs 命令 |
| Scope | 新增示例 `.ecode/ecode.example.json` 或 `docs/configuration.md` 示例；不写入用户真实本地配置 |
| 关联 | `05-cli-commands.md`、`docs/custom-commands.md` |
| 验收 | 示例包含 build、unit test、docs build、status/health；所有高风险命令 `confirm:true` |
| 风险 | 示例路径在 macOS / Windows 不一致 |
| 回滚 | 删除示例文件，不影响源码 |

### `DOG-02` - 设计 ecode.v2 本地 smoke 脚本

| 字段 | 内容 |
|---|---|
| 状态 | ready |
| 优先级 | P2 |
| Outcome | 用 ECode 自身自动化 API 验证 workspace / pane / browser 的最小闭环 |
| Scope | 先写 spec 或脚本草案；涉及 live app 的执行标记 Windows-only；不要求当前环境跑通 WPF |
| 关联 | `03-data-and-ipc.md`、`05-cli-commands.md` |
| 验收 | 脚本步骤覆盖 status -> workspace.create -> pane.write/read -> browser.open -> browser.snapshot；缺环境时输出清晰 skip |
| 风险 | 依赖正在运行的 ECode 主应用 |
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
| M1 UI/UX 与 `ecode.json` | done | `CHANGELOG.md`、`docs/roadmap.md` |
| M2 会话恢复 | done | `CHANGELOG.md`、`docs/session-restore.md` |
| M3 Browser Pane | done | `CHANGELOG.md`、`docs/getting-started.md` |
| M4 Browser scripting | done | `CHANGELOG.md`、`docs/browser-api.md` |
| M5 v2 协议 | done | `CHANGELOG.md`、`docs/cli.md` |
| M6 安装更新 | done | `CHANGELOG.md`、`docs/installation.md` |
| M7 文档与社区 | done | `CHANGELOG.md`、`CONTRIBUTING.md`、`SECURITY.md` |

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
