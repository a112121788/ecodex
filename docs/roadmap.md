# 路线图

ECodex 是 Windows 原生 SuperTerminal：把终端 Workspace、集成浏览器、脚本化控制、会话恢复和 Windows 安装更新整合到一个桌面工作流中。

本公开路线图同步 `spec/06-roadmap.md` 中稳定、面向用户的内容；具体 PR 级任务以 `spec/07-implementation-backlog.md` 为准。

## 当前重点

- `1.0.0` 已作为稳定基线归档，后续工作按 Now / Next / Later 连续交付。
- 当前冲刺聚焦 `S3 - 窗口关闭与单实例激活`：右上角 X 退出 `ecodex-app` 主进程、最小化继续托盘常驻、任务栏 / 固定图标激活已有窗口。
- 继续保持 P0 缺陷数量 = 0；涉及后台进程保活、静默执行、数据丢失的风险默认按 P0 处理。
- 默认 PowerShell shell integration 会写入用户 profile，因此必须具备标记块、备份、状态检查和卸载回滚。
- `docs/` 统一为简体中文单语；英文站点如需恢复，应拆独立 locale。

## 版本线

| 版本 | 重点 | 里程碑 | 通道 |
|---|---|---|---|
| `0.1.x` | 工程基线、测试、发布脚本、crash 修复 | M0 | nightly |
| `0.2.x` | UI / 通知体验与 `ecodex.json` 基础 | M1 | preview |
| `0.3.x` | 会话恢复、resume binding、Workspace 环境注入 | M2 | preview |
| `0.4.x` | 浏览器 Pane 基础 | M3 | preview |
| `0.5.x` | 浏览器脚本 API | M4 | beta |
| `0.6.x` | `ecodex.v2`、多窗口、短引用 | M5 | beta |
| `0.7.x` | Shell / 命令行集成、自动更新、安装器 | M6 | 候选发布 |
| `1.0.0` | Windows 稳定版发布 | M7 + 缺陷收敛 | stable |
| `1.1.x` | 窗口关闭与单实例激活、常驻托盘、通知完善、默认 skills、终端多行输入 | S3 | preview |

## 里程碑

### M0 - 工程基线

- CI 与本地验证入口。
- 高风险 Core 单元测试。
- App、命令行、STATUS 统一版本来源。
- 发布产物校验与 smoke workflow。

### M1 - UI/UX 与 `ecodex.json`

- Workspace、Surface、Pane 交互完善。
- 通知环、未读跳转和通知面板。
- `ecodex.json` commands/actions 基础与 reload。
- Tab 与 Workspace 重排持久化。

### M2 - 会话恢复

- `session.json` / `resume.json` 数据模型加固。
- trusted resume binding 与敏感环境剔除。
- 手动恢复与可信自动恢复。
- 终端进程注入 `ECODEX_WORKSPACE_ID` / `ECODEX_SURFACE_ID` / `ECODEX_PANE_ID`。

### M3 - 浏览器 Pane 基础

- 集成浏览器持久化与分屏渲染。
- WebView2 Runtime 缺失提示。
- 浏览器 toolbar、URL/title/history 状态与命令行 `open` 命令。
- `workspace.surfaces` 支持 浏览器标签页。

### M4 - 浏览器脚本 API

- `ecodex.v2` browser 请求解析与稳定错误码。
- snapshot、locator、click、fill、hover、press、eval、screenshot。
- cookies、storage、console、dialog、download、highlight。
- 明确 `not_supported` 矩阵。

### M5 - v2 协议、多窗口与短引用

- `window:N`、`workspace:N`、`surface:N`、`pane:N` 短引用。
- window / workspace / surface / pane / notification / config / status / health API。
- 命令行命令接入结构化 `ecodex.v2` 响应。
- 同一 pipe 保持 v1 文本命令兼容并协商 v2 JSON。

### M6 - 系统集成、安装与更新

- PATH、PowerShell profile、cmd AutoRun 与 completion setup。
- Windows Terminal profile import 计划器。
- `ecodex doctor`、setup status/install/uninstall、update 命令。
- self-contained、命令行、Velopack、Inno Setup、MSIX 打包路径。

### M7 - 文档、社区与 1.0

- 简体中文用户文档：安装、快速上手、命令行、浏览器 API、会话恢复、故障排查。
- 贡献指南、安全策略、Issue / PR 模板、Discord release 通知。
- P0/P1 发布门槛与用户可读 `1.0.0` 发布说明。

### S3 - 窗口关闭与单实例激活

- 右上角 X 保存会话并退出 `ecodex-app` 主进程；默认保留 daemon 托管终端，除非用户关闭 `PreserveDaemonSessionsOnClose`。
- 最小化仍隐藏到系统托盘，后台终端与通知继续运行。
- 任务栏按钮 / 固定任务栏图标与托盘“打开 ECodex”一致，激活已有窗口；安装器快捷方式和进程使用相同 AppUserModelID。
- PowerShell shell integration 默认启用，用于回传命令开始、结束和退出码。
- 通知只在窗口隐藏/非激活且命中完成、失败、等待输入等关键状态时弹出。
- 预制 skills 从 `assets/default-skills/` 安全种子到 `%USERPROFILE%\.agents\skills`，同名不覆盖。
- 所有终端默认 `Ctrl+Enter` 输入换行，普通 `Enter` 仍提交。

## 1.0 门槛

稳定版要求：

- P0 缺陷数量 = 0。
- P1 缺陷数量 <= 3，且每个都有记录在案的规避方案（workaround）。
- CI、`scripts/ci.ps1` 和发布脚本保持绿色。
- terminal / layout / notification / session restore / `ecodex.json` / browser / v2 命令行核心流程达到 beta 或 stable 质量。
- 安装、卸载、更新流程保留 `%USERPROFILE%\.ecodex` 数据。

当前门槛状态见 [发布就绪](./release-readiness.md)。
