# 配置说明

ECodex 的用户配置分为三类：运行时设置、自定义命令配置和兼容开关。

## 运行时目录

默认运行时目录：`%USERPROFILE%\.ecodex`

常见文件：

| 文件 | 用途 |
|---|---|
| `settings.json` | UI、终端行为、兼容开关等设置。 |
| `session.json` | Workspace、Surface、Pane 布局与快照。 |
| `resume.json` | tmux / agent 等恢复绑定。 |
| `daemon-debug.log` | App / daemon / IPC 诊断日志。 |

## `ecodex.json` 加载顺序

自定义命令配置使用 `ecodex.json`。推荐路径：

1. 项目内：`.ecodex/ecodex.json`
2. 用户级：`%USERPROFILE%\.config\ecodex\ecodex.json`
3. 兼容读取：`.cmux/cmux.json`（由兼容开关控制）

执行诊断：

```powershell
ecodex config diagnostics
ecodex config reload
```

## 运行时行为设置

| 字段 | 默认 | 说明 |
|---|---|---|
| `PreserveDaemonSessionsOnClose` | `true` | 关闭 ECodex 主窗口时保留 daemon 托管终端；设为 `false` 时关闭前逐个终止当前 daemon 会话。 |

## 兼容开关

设置窗口的“高级”页包含兼容选项：

- 监听旧主应用管道（`cmux`）。
- 监听旧 daemon 管道 / Mutex（`cmux-daemon`）。
- 读取旧配置文件 `.cmux/cmux.json`。
- 接受旧命令行命令别名。

兼容能力只用于迁移；新配置应统一写入 `.ecodex/ecodex.json`。

## 安全建议

- 不要在 `ecodex.json` 中写入真实 token 或 API key。
- 对自动执行命令使用 `confirm: true`。
- 附 issue 时先脱敏 `settings.json`、`resume.json` 和 `daemon-debug.log`。
