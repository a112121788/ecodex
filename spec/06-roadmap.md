# ECodeX 敏捷交付路线图

> 本文回答“接下来为什么做、先做什么、做到什么程度”。
>
> 执行规则见 `00-agile-ai-delivery.md`；PR 级任务见 `07-implementation-backlog.md`；架构和协议事实见 `01-architecture.md` 到 `05-cli-commands.md`。

---

## 0. 北极星

ECodeX 的北极星是：**让 Windows 上的多项目终端、浏览器预览、脚本化控制和 AI 辅助开发形成稳定、可恢复、可审计的 SuperTerminal 工作台。**

核心用户包括：

1. 同时维护多个仓库和长任务的开发者。
2. 需要把终端、浏览器和自动化脚本串起来的集成方。
3. 让 AI Agent 在本地仓库中持续小步交付的维护者。

---

## 1. 产品原则

| 原则 | 含义 | 取舍 |
|---|---|---|
| SuperTerminal 优先 | 终端 / layout / browser / automation / restore 是核心原语 | 不做完整 IDE |
| AI 友好但不黑箱 | 为 AI 提供可观测、可脚本化、可恢复的工作台 | 不自动执行不可信命令 |
| Windows 原生 | WPF、ConPTY、WebView2、DPAPI、Toast、Velopack 优先 | 不用 Electron/Tauri 重写 |
| 可审计 | 会话、命令、配置、恢复绑定都能落盘和人工检查 | 不隐藏副作用 |
| 小步交付 | 每次改动必须可验证、可回滚、可解释 | 不做跨层大爆炸 PR |

---

## 2. 当前基线

截至 2026-06-15，ECodeX 已形成 `1.0.0` 稳定基线：

| 能力域 | 当前状态 | 事实来源 |
|---|---|---|
| Terminal / Layout | ConPTY、scrollback、分屏树、Workspace / Surface / Pane 已成型 | `01-architecture.md`、`02-modules.md` |
| Notification | OSC 通知、未读态、跳转、面板、Toast 基础可用 | `01-architecture.md`、`05-cli-commands.md` |
| Config | `.ecodex/ecodex.json` commands/actions、reload、diagnostics 已可用 | `03-data-and-ipc.md`、`05-cli-commands.md` |
| Restore | `session.json`、`resume.json`、trusted binding、安全过滤已可用；关闭窗口后自动接回仍在 `SES-01` 补齐 | `03-data-and-ipc.md`、`07-implementation-backlog.md` |
| Browser | Browser Surface、WebView2 友好提示、CLI open、脚本化 API 已成型 | `03-data-and-ipc.md`、`05-cli-commands.md` |
| v2 Automation | `ecodex.v2`、短引用、多窗口、pane/workspace/surface/notification/config/status API 已成型 | `03-data-and-ipc.md`、`05-cli-commands.md` |
| Delivery | CI、release workflow、docs、CONTRIBUTING、SECURITY、安装更新路径已具备 | `04-build-deploy.md`、`docs/` |

旧 M0-M7 里程碑已经沉淀为 1.0 基线；后续路线图改用 Now / Next / Later 管理连续交付。

### 2.1 1.0.0 发布前刷新：2026-06-15

| 检查项 | 结果 |
|---|---|
| 缺陷门槛 | P0=0、P1=0 |
| Unicode 验证 | CI 已加入 `中文 目录/项目/` smoke |
| 发布证据 | release 上传 `ecodex-perf-report` |

### 2.2 文档站语言要求

文档站统一采用简体中文单语内容，不再维护同页中英双语内容；英文术语只在命令、API、产品名或社区约定中保留。

---

## 3. Now / Next / Later

### Now：让 AI 自动交付循环稳定运行

目标：把当前仓库变成 AI Agent 可以持续接手的交付系统。

| Outcome | 说明 | 指标 |
|---|---|---|
| 可自动取下一项 | `07` 中 `ready` 队列足够清楚，Agent 不需要反复问下一步 | 每个 ready 项都有 Outcome / Scope / Acceptance |
| 可自动验证 | 常见改动有最小验证命令，环境缺口明确 | 每个 done 项记录验证命令或缺口 |
| 可自动停下 | 风险、凭据、Windows-only、连续失败有明确 stop rule | blocked 项都有原因和解除条件 |
| 可续接 | 任意 Agent 读 `00` + `06` + `07` 可继续 | `doing` 不超过 1 个，任务有 handoff note |

### Next：用 ECodeX 自身能力 dogfood AI 开发

目标：让 ECodeX 的 CLI / Browser / Restore / Notification 反过来支撑本仓库开发。

| Outcome | 说明 | 指标 |
|---|---|---|
| 本地 smoke 脚本化 | 用 `ecodex.v2` 创建 workspace/pane/browser，跑常用开发命令 | 至少 1 条可复用 dogfood 脚本 |
| 自动证据收集 | 测试、截图、日志、health 输出能汇总到 PR note | PR 模板引用证据路径 |
| 配置模板化 | `.ecodex/ecodex.json` 提供 build/test/docs/release 常用命令 | 新贡献者 5 分钟可打开命令面板执行 |
| Agent 会话可复盘 | Agent 线程、命令日志、terminal transcript 可用于排障 | 可从 session vault 找到一次失败 loop |

### Later：提升稳定性、生态与集成深度

目标：在不牺牲本地可控性的前提下扩展更多高价值场景。

| Outcome | 说明 | 进入条件 |
|---|---|---|
| Windows release hardening | 安装、更新、卸载、rollback 更稳 | Now / Next 的验证证据稳定 |
| Browser automation depth | 对更多 WebView2 能力提供支持或明确 not_supported | 现有 P0 API 无阻塞缺陷 |
| Remote / SSH 评估 | 只做轻量 profile 与安全评估，不默认进入主线 | 本地核心体验稳定且有明确用户需求 |
| 文档站与模板生态 | 把常见工作流沉淀成 docs / recipes / examples | 至少 3 个被复用的内部模板 |

---

## 4. 迭代节奏

| 周期 | 节奏 | 产物 |
|---|---|---|
| 每个 loop | 0.5-1 天，单任务 WIP | diff、验证结果、backlog 状态 |
| 每周 | 选 3-5 个 Now 任务，刷新风险 | `06` Now 状态、`07` ready 队列 |
| 每两周 | 小版本 review 或 release candidate 判断 | changelog、docs、release-readiness |
| 每个发布 | 稳定性门槛 + 用户可读发布说明 | tag、release notes、回滚说明 |

默认优先级排序：

1. P0 / 安全 / 数据丢失 / 静默执行风险。
2. 影响 AI loop 可持续运行的工程任务。
3. 高频用户工作流的阻塞问题。
4. 可提升 dogfood 与验证自动化的任务。
5. 新功能探索。

---

## 5. 成果树

```text
North Star: Windows SuperTerminal for auditable AI-assisted development
├─ Reliable core
│  ├─ terminal/layout/session stable
│  ├─ tests and CI trustworthy
│  └─ release rollback clear
├─ Scriptable workbench
│  ├─ CLI/v2 API predictable
│  ├─ Browser automation useful
│  └─ config recipes reusable
├─ AI delivery loop
│  ├─ backlog ready queue
│  ├─ verification matrix
│  └─ stop rules and handoff notes
└─ User adoption
   ├─ install/update smooth
   ├─ docs match product
   └─ troubleshooting fast
```

---

## 6. 质量门槛

### 6.1 发布门槛

| 严重度 | 标准 |
|---|---|
| P0 | 必须为 0；崩溃、数据丢失、静默不可信命令执行默认 P0 |
| P1 | 小版本发布前必须有 workaround；稳定版不超过 3 个 |
| P2 | 可进入下一迭代，但需要记录影响范围 |

### 6.2 Done 门槛

每个完成项必须能回答：

1. 这个切片解决了什么 Outcome？
2. 改了哪些文件？为什么这些文件属于同一切片？
3. 跑了什么验证？哪些验证因为环境限制没有跑？
4. 文档、协议、CLI、CHANGELOG 是否需要同步？如果不需要，原因是什么？
5. 回滚方式是什么？

---

## 7. 风险登记

| ID | 风险 | 严重度 | 触发信号 | 策略 |
|---|---|---|---|---|
| R-01 | AI loop 自动扩大范围 | High | 单轮修改跨 Core/UI/CLI/docs/release 多层 | WIP=1；超过 2 天必须拆 |
| R-02 | macOS 验证误报 Windows 通过 | High | 只跑静态检查却声称 WPF/ConPTY/WebView2 通过 | 明确 Windows-only 缺口；必要时 blocked |
| R-03 | 文档与源码漂移 | Medium | `07` ready 项指向不存在文件或旧命令 | 每轮 Intake 先 `rg` 真实文件 |
| R-04 | 不可信命令被自动执行 | High | `ecodex.json` / resume / setup 触发未确认命令 | 默认 confirm；敏感操作停 loop |
| R-05 | Backlog 只剩大块史诗 | Medium | ready 队列为空或每项超过 2 天 | planning 时先拆到 0.5-1 天 |
| R-06 | 后台终端保活造成资源残留 | High | 用户关闭 ECodeX 后仍有 Codex / shell / dev server 在跑 | `SES-01` 必须提供状态可见性、全部终止入口和可回滚设置 |
| R11 | Release webhook / token 缺失 | Medium | release 通知失败或跳过 Discord webhook | release workflow 保持非阻塞，发布前确认 secret 配置 |
| R12 | 性能预算随功能增长漂移 | Medium | CLI status、启动或渲染时间超过预算 | 保留 perf 脚本与 `ecodex-perf-report` artifact，定期刷新预算 |

---

## 8. 里程碑归档

旧版 M0-M7 已完成并作为 1.0 基线保留在 `CHANGELOG.md` 与 `docs/roadmap.md`：

| 里程碑 | 主题 | 当前归属 |
|---|---|---|
| M0 | 工程基线、CI、测试、发布脚本 | 1.0 基线 |
| M1 | UI/UX、通知、`ecodex.json` | 1.0 基线 |
| M2 | 会话恢复、resume binding | 1.0 基线 |
| M3 | Browser Pane 基础 | 1.0 基线 |
| M4 | Browser scripting API | 1.0 基线 |
| M5 | v2 协议、多窗口、短引用 | 1.0 基线 |
| M6 | Shell / CLI 集成、安装与更新 | 1.0 基线 |
| M7 | 文档、社区、1.0 发布就绪 | 1.0 基线 |

后续新增工作不要继续扩写 M0-M7，而是进入 `07-implementation-backlog.md` 的敏捷队列。

---

## 9. 维护约定

- 每周刷新 Now 区域：保留最多 3 个 Outcome，避免路线图变成任务清单。
- 每两周复盘 Later：只有用户证据明确的主题才能进入 Next。
- `07` 中超过 2 周未推进的 `ready` 项必须重验 Ready 条件。
- 涉及架构 / 协议 / CLI 的任务，先更新对应 spec，再实现。
- 任何 Agent 完成任务后必须留下验证证据和下一步建议。
