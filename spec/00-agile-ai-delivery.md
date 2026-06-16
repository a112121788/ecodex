# 敏捷 AI 交付循环

> 本文是 `spec/` 的入口控制文档。目标是让人类维护者与 AI Agent 用同一套规则，把需求拆成可验证的小切片，持续自动 loop，直到完成、阻塞或需要人工决策。
>
> 范围：本文只规定交付方法、文档协作和自动化开发循环；产品方向见 `06-roadmap.md`，可执行 backlog 见 `07-implementation-backlog.md`，架构与协议事实见 `01-architecture.md` 到 `05-cli-commands.md`。

---

## 1. 交付目标

ECodex 的研发方式调整为 **敏捷交付 + AI 自动化开发**：

1. 每次只交付一个可验证的用户或工程价值切片。
2. 由 `spec/06-roadmap.md` 提供方向，由 `spec/07-implementation-backlog.md` 提供下一步。
3. AI Agent 可以按固定 loop 自动选择、执行、验证并更新任务状态。
4. 任何不确定、不可验证或风险外溢的工作必须停下来，把问题写清楚，而不是继续猜。

### 1.1 成功标准

| 维度 | 标准 |
|---|---|
| 价值 | 每个切片能说明解决了哪个用户 / 维护者问题 |
| 尺寸 | 默认 0.5-1 天可完成；超过 2 天必须拆分 |
| 边界 | 明确涉及文件、非目标、依赖与回滚点 |
| 验证 | 进入开发前已有验收命令或手测脚本 |
| 可续接 | 任意 Agent 读取 `00` + `06` + `07` 后能继续下一步 |

---

## 2. 文档即看板

`spec/` 不再只是说明文档，而是轻量项目管理系统：

| 文档 | 角色 | 更新时机 |
|---|---|---|
| `00-agile-ai-delivery.md` | 交付规则、loop、DoR/DoD | 流程变化时 |
| `01-architecture.md` | 架构事实、技术边界、关键约束 | 架构 / 进程 / 持久化变化时 |
| `02-modules.md` | 模块地图、类职责、协作链路 | 新增 / 移动模块时 |
| `03-data-and-ipc.md` | 数据模型、IPC、协议契约 | JSON / pipe / CLI contract 变化时 |
| `04-build-deploy.md` | 构建、测试、发布、运行约束 | 工具链或 CI 变化时 |
| `05-cli-commands.md` | CLI 与 IPC 命令参考 | 命令 / 参数 / 错误码变化时 |
| `06-roadmap.md` | Now / Next / Later 与成果指标 | 每个迭代 planning / review 时 |
| `07-implementation-backlog.md` | 可执行任务队列与状态 | 每个 loop 开始和结束时 |

### 2.1 单一事实来源

- 产品方向、优先级、停线：以 `06-roadmap.md` 为准。
- 当前下一步、任务状态、验收：以 `07-implementation-backlog.md` 为准。
- 架构 / 协议 / CLI 事实：以 `01`-`05` 为准，源码优先于文档。
- 用户可读发布状态：以 `CHANGELOG.md` 与 `md/` 为准。

---

## 3. Agile Loop

每个任务必须沿以下循环推进：

```text
Intake -> Slice -> Plan -> Implement -> Verify -> Document -> Review -> Decide
   ^                                                                  |
   |------------------------------------------------------------------|
```

| 阶段 | AI Agent 要做什么 | 产物 |
|---|---|---|
| Intake | 阅读 `00`、`06`、`07`，确认目标 / 范围 / 验收；检查 `git status --porcelain` | 任务边界与假设 |
| Slice | 若任务跨层或过大，先拆成 Core / UI / CLI / docs / tests 子切片 | backlog 子项 |
| Plan | 列 2-5 步短计划；明确会读哪些文件、改哪些文件、跑哪些验证 | 执行计划 |
| Implement | 小步修改；同一轮避免同时改无关模块 | 可回滚 diff |
| Verify | 跑任务对应的最小验证；无法跑的命令要说明原因 | 命令结果 / 缺口 |
| Document | 更新 `spec/`、`md/`、`CHANGELOG.md` 中必要部分 | 文档 diff |
| Review | 自查风险、兼容、测试缺口、是否污染无关文件 | review note |
| Decide | 更新 backlog 状态：done / blocked / split / next | 下一轮入口 |

### 3.1 自动继续规则

AI Agent 可以自动进入下一轮，条件是：

1. `07-implementation-backlog.md` 中存在状态为 `ready` 的最高优先级任务。
2. 任务的涉及文件和验收命令明确。
3. 当前工作树没有未知的非本轮改动；如有，先报告并等待人工确认。
4. 上一轮验证通过，或失败原因已修复且没有连续两次同类失败。
5. 下一轮不需要联网、GUI、凭据、发布、破坏性命令或大范围重构。

### 3.2 必须停止规则

遇到以下情况停止 loop，并把阻塞写到 `07-implementation-backlog.md`：

- 同类错误连续出现两次，且本地上下文无法解释。
- 需求会跨多个里程碑、多个不可回滚模块或影响安全边界。
- 验收依赖 Windows / WebView2 / ConPTY / 发布证书等当前环境没有的能力。
- 需要安装依赖、访问网络、触碰用户数据、执行破坏性命令或使用凭据。
- 文档与源码冲突，但无法在同一小切片内对齐。

---

## 4. Definition of Ready

一个 backlog 条目进入 `ready` 前必须具备：

| 字段 | 要求 |
|---|---|
| Outcome | 说明用户或维护者获得什么结果 |
| Scope | 明确涉及文件 / 模块，列出非目标 |
| Acceptance | 至少 1 条可执行验收，最好是命令 |
| Evidence | 指向相关 spec / docs / issue / 代码入口 |
| Risk | 说明安全、兼容、性能或发布风险 |
| Rollback | 说明如何回退或关闭该能力 |

不满足 Ready 的条目只能处于 `draft` 或 `blocked`。

---

## 5. Definition of Done

一个任务完成必须同时满足：

1. 代码或文档改动与任务 Scope 一致，没有顺手改无关内容。
2. 必跑验证已执行；不能执行的验证已明确标注环境限制。
3. 涉及协议 / CLI / 数据模型时，`03` / `05` 已同步。
4. 涉及用户行为时，`md/` 或 `README.md` 已同步；只改内部流程则可只改 `spec/`。
5. 涉及发布用户可见行为时，`CHANGELOG.md` 已同步。
6. `07-implementation-backlog.md` 状态已更新，留下下一步或阻塞原因。

---

## 6. 验证矩阵

| 改动类型 | 最小验证 | 完整验证 |
|---|---|---|
| spec-only | `rg -n "新增链接|旧文件名" spec` + 人工读 diff | `npm run docs:build`（若 docs 导航引用 spec） |
| Core 逻辑 | `dotnet test tests/ECodex.Tests/ECodex.Tests.csproj` | `dotnet build ECodex.sln -c Debug` |
| CLI / IPC | contract tests + `ecodex ... --json` 手测 | v1 / v2 兼容 smoke |
| UI / WPF | build + 截图 / 录屏 | Windows 真机手测 |
| ConPTY | unit tests + smoke dry-run | `tests/ECodex.Smoke` Windows 环境 |
| Browser / WebView2 | Browser service tests | Windows-only WebView2 integration |
| Installer / Update | publish dry-run | release workflow + 手工安装 / 卸载 |

> 当前仓库可在 macOS 上验证 Core / CLI / Tests 的一部分；WPF GUI、ConPTY smoke、WebView2 live integration 仍以 Windows 为准。不要把 macOS 静态检查描述为完整通过。

---

## 7. 任务切片规则

优先按“用户看得见的闭环”切片，其次按技术层切片：

1. **Docs-first**：复杂功能先写 spec 行为、数据结构、验收。
2. **Core-first**：数据模型、纯服务、解析器先落 Core 和测试。
3. **API-second**：CLI / IPC / v2 contract 再接入。
4. **UI-last**：UI 只绑定已稳定的模型与服务。
5. **Hardening**：补测试、性能、日志、docs、回滚路径。

禁止一个 PR 同时完成“大模型式一口气重写”：架构、数据模型、UI、发布脚本、文档全改。遇到这种任务必须拆。

---

## 8. AI Agent 启动提示

当需要让 AI 自动接手开发时，可以给它以下指令：

```text
读取 spec/00-agile-ai-delivery.md、spec/06-roadmap.md、spec/07-implementation-backlog.md。
从 backlog 中选择优先级最高且状态为 ready 的一项。
先检查 git status，再阅读相关 spec 与源码。
按 Intake -> Slice -> Plan -> Implement -> Verify -> Document -> Review -> Decide 循环执行。
每轮只做一个小切片；完成后更新 backlog 状态与必要文档。
如果连续两次同类失败、缺少 Windows/WebView2 环境、需要凭据/网络/破坏性命令，停止并报告阻塞。
```

---

## 9. 迭代节奏

| 节奏 | 活动 | 输出 |
|---|---|---|
| 每个 loop | 取一个 `ready` 项，完成或阻塞 | diff + 验证结果 + backlog 更新 |
| 每日 | 清理 `doing` / `blocked`，补齐 Ready 信息 | 今日可自动执行队列 |
| 每周 | review Now 目标、风险、度量 | `06-roadmap.md` 更新 |
| 每两周 | 迭代复盘与发布候选判断 | changelog / docs / release readiness |

默认 WIP 限制：每个 Agent 同时只做 1 个 `doing`。多个 Agent 并行时必须按模块分配，避免同改一个文件。

---

## 10. 度量

每轮结束尽量留下这些事实，供 review 使用：

- Lead time：从 `ready` 到 `done` 用时。
- Verification：跑了哪些命令，哪些没法跑。
- Escape：是否有返工、遗漏文档、遗漏测试。
- Risk：新增 / 关闭 / 接受了哪些风险。
- Loop health：是否需要人工介入，为什么。
