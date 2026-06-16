# ECodex 设计文档（`spec/`）

本目录是 ECodex 的工程内部设计与敏捷交付控制台。它不同于面向最终用户的 `README.md` / `md/`，目标是：

- 让新贡献者在 30 分钟内理解项目结构、关键模块、数据契约与发布流程。
- 让 AI Agent 能自动读取路线图与 backlog，按小步 loop 持续开发。
- 为规划、能力对齐与技术决策提供单一可信来源。
- 让每次架构 / 协议 / 数据模型变更先在此处收敛，再落到代码。

> 范围：本仓库主线是 **Windows 原生版**（WPF + ConPTY + WebView2 + Named Pipe + .NET 10）。macOS 可用于部分 Core / CLI / Tests 验证，但不能替代 Windows GUI / ConPTY / WebView2 验证。
>
> 品牌：项目代号 **ECodex**（旧称 `cmux-windows`，参见 `CHANGELOG.md` 的 “Breaking changes” 段）。`cmux` 一词在 spec/ 中仅作为上游 macOS 原版（`manaflow-ai/cmux`）的引用、协议 v1 命令名（`WORKSPACE.* / PANE.*`）与配置文件兼容名（`cmux.json`）出现。

---

## 阅读顺序

### AI Agent 自动开发

1. [`00-agile-ai-delivery.md`](00-agile-ai-delivery.md) — 敏捷交付规则、自动 loop、Definition of Ready / Done、停止规则与验证矩阵。
2. [`06-roadmap.md`](06-roadmap.md) — Now / Next / Later 路线图、当前 Outcome、质量门槛与风险登记。
3. [`07-implementation-backlog.md`](07-implementation-backlog.md) — AI 可领取的 `ready` 队列、任务模板、handoff note 与归档。
4. 按 backlog 的 `关联` 字段读取 `01`-`05` 中的具体事实。

### 人类贡献者理解项目

1. [`01-architecture.md`](01-architecture.md) — 整体架构、技术栈、进程模型、数据流、关键设计决策、持久化、安全、部署。
2. [`02-modules.md`](02-modules.md) — 按模块 / 类梳理职责、关键方法、协作链路。
3. [`03-data-and-ipc.md`](03-data-and-ipc.md) — 数据模型、命名管道协议、JSON 形状、错误码。
4. [`04-build-deploy.md`](04-build-deploy.md) — 解决方案布局、构建脚本、发布形态、运行依赖、故障排查。
5. [`05-cli-commands.md`](05-cli-commands.md) — `ecodex.exe` CLI 与 IPC 命令参考。

---

## 文档维护规则

- 任何架构 / 协议 / 数据模型 / 命名管道 / CLI 命令的变更，必须同步更新本目录下的对应文档，并在 PR 描述里写明“spec 改动点”。
- 任何产品方向或优先级变化，必须先更新 `06-roadmap.md`；任何可执行任务变化，必须更新 `07-implementation-backlog.md`。
- AI Agent 每轮 loop 开始前读取 `00` + `06` + `07`；结束时更新 backlog 状态、验证结果或 handoff note。
- 当文档与源码出现冲突时：以源码为准，并在文档里加 `TODO(spec): align with src/...` 注明。修复冲突需在同一个 PR 内完成。
- 每周刷新 `06-roadmap.md` 的 Now 区域、风险登记与 `07-implementation-backlog.md` 的 ready 队列。

---

## 与其他目录的关系

| 目录 | 角色 | 受众 |
|---|---|---|
| `spec/` | 工程设计 + 敏捷交付控制台 | 贡献者 / 维护者 / AI Agent |
| `md/` | 用户与运维文档 | 最终用户 / 集成方 |
| `README.md` / `README.en.md` | 项目入口、特性与使用概览 | 所有人 |
| `src/` | 实际实现 | 贡献者 |
| `tests/` | 单元 / 烟雾测试 | 贡献者 |
| `scripts/` | 构建、验证与发布脚本 | 维护者 / 发布者 / AI Agent |

---

## 当前状态

| 文档 | 状态 | 最近一次大改 |
|---|---|---|
| `00-agile-ai-delivery.md` | 新增：AI 自动 loop 与敏捷交付规则 | 本次 spec 重构 |
| `01-architecture.md` | 架构事实源，跟随源码校准 | 1.0 基线 |
| `02-modules.md` | 模块事实源，跟随源码校准 | 1.0 基线 |
| `03-data-and-ipc.md` | 数据与 IPC 事实源，跟随源码校准 | 1.0 基线 |
| `04-build-deploy.md` | 构建 / 发布事实源，跟随脚本校准 | 1.0 基线 |
| `05-cli-commands.md` | CLI / IPC 命令事实源，跟随实现校准 | 1.0 基线 |
| `06-roadmap.md` | 重构为 Now / Next / Later 敏捷路线图 | 本次 spec 重构 |
| `07-implementation-backlog.md` | 重构为 AI 可执行敏捷 backlog | 本次 spec 重构 |
| `2026-06-11-ecodex-rename-plan.md` | 历史重命名计划归档 | 0.1.0 归档 |

> 验证方法：在每个 PR 合并前，至少跑一次 `wc -l spec/*.md` 与 `git diff --stat spec/` 复核改动量；涉及 docs 导航时再运行 `npm run docs:build`。
