# 架构概览

ECodex 是 Windows 原生 WPF 应用，面向高强度终端、多项目分屏、浏览器预览与脚本化控制。

## 主要组件

| 组件 | 说明 |
|---|---|
| `src/ECodex` | WPF 主应用，承载 Workspace、Surface、Pane、集成浏览器与设置窗口。 |
| `src/ECodex.Core` | Core 模型、终端缓冲区、ConPTY、IPC、配置、会话恢复与 浏览器脚本契约。 |
| `src/ECodex.Cli` | `ecodex` 命令行，负责本地命令、v1 pipe 兼容与 `ecodex.v2` 请求。 |
| `src/ECodex.Daemon` | daemon 托管终端会话、快照与 attach/reconnect。 |
| `src/ECodex.Updater` | Velopack feed 检查与静默安装辅助。 |
| `tests/` | xUnit 单元 / contract 测试与 Windows ConPTY smoke。 |

## 运行时数据流

1. WPF 主应用创建 Workspace / Surface / Pane。
2. Pane 使用本地 ConPTY 或 daemon 托管的 `TerminalSession`。
3. 命令行通过 `\\.\pipe\ecodex` 发送 v1 命令或 `ecodex.v2` JSON 请求。
4. 会话状态保存到 `%USERPROFILE%\.ecodex\session.json`。
5. resume binding 保存到 `%USERPROFILE%\.ecodex\resume.json`，敏感环境变量会在保存前剔除。

## 文档边界

本页是用户级架构概览。权威工程设计仍以 `spec/` 为准；当源码、spec 与 docs 不一致时，应先更新 spec，再同步 docs。
