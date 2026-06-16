# 会话恢复

ECodex 会保存窗口、Workspace、Surface、Pane 布局，以及终端与集成浏览器的关键状态。目标是在重启后尽量恢复工作上下文，同时避免自动执行不可信命令。

## 运行时文件

| 文件 | 说明 |
|---|---|
| `%USERPROFILE%\.ecodex\session.json` | Window / Workspace / Surface 布局、terminal pane snapshots、集成浏览器元数据。 |
| `%USERPROFILE%\.ecodex\resume.json` | tmux、agent、shell 等恢复绑定。 |
| `%USERPROFILE%\.ecodex\daemon-debug.log` | 恢复、attach、daemon 与 IPC 诊断日志。 |

## `session.json`

`session.json` 保存：

- Workspace 名称、顺序、选中项、项目文件夹。
- 恢复时同一项目文件夹只保留第一个 Workspace，后续重复项会被跳过。
- Surface 标题、类型、顺序、选中项。
- Pane 分屏树、大小比例、focused pane。
- `paneSnapshots`：cwd、shell、命令历史、终端 buffer snapshot。
- 集成浏览器：`kind`、`browserUrl`、`browserTitle`、`browserHistory`。

示意：

```json
{
  "version": 2,
  "workspaces": [
    {
      "id": "workspace:1",
      "selectedSurfaceIndex": 0,
      "surfaces": [
        {
          "kind": "terminal",
          "paneSnapshots": {
            "pane:1": {
              "workingDirectory": "C:\\repo",
              "shell": "pwsh",
              "bufferSnapshot": {
                "cols": 120,
                "rows": 30,
                "scrollbackLines": [],
                "screenLines": []
              }
            }
          }
        },
        {
          "kind": "browser",
          "browserUrl": "http://localhost:5173",
          "browserHistory": ["http://localhost:5173"]
        }
      ]
    }
  ]
}
```

终端 snapshot 用于上下文连续性与诊断，不代表原始进程在重启后仍然存活。

新建/关闭 Surface、分屏、关闭 pane、调整分屏比例等结构变化会实时写入 `session.json` checkpoint；这些 checkpoint 只更新布局与 pane snapshot，不生成 `session-close` transcript。若主程序崩溃，重开时仅自动挂载 `session.json` 中已有 paneId 对应的 daemon 会话，不自动创建未持久化的 daemon 孤儿 pane。

## daemon 托管会话

连接到 `ecodex-daemon` 时，终端进程由 daemon 托管。正常关闭 ECodex 主窗口后，daemon 会继续保留仍在运行的终端进程；重新打开 ECodex 时，相同 pane 会优先 attach 到原 daemon 会话并拉取最新 buffer snapshot。

主动关闭 ECodex 只断开主程序与 daemon 的客户端连接，不触发“daemon 断线后回退到本地 ConPTY”的运行时保护逻辑。只有应用仍在运行且 daemon 意外不可达时，才会把当前 pane 回退为本地终端，并保留已捕获的 snapshot 作为诊断上下文。

`settings.json` 中的 `PreserveDaemonSessionsOnClose` 默认是 `true`，设置窗口“行为 → 保留终端会话”可持久化修改该开关。若设为 `false`，主窗口关闭前会通过 daemon 的 `SESSION_LIST` + 逐个 `SESSION_CLOSE` 终止当前托管会话。

显式“退出并终止终端”不再使用 daemon 级 `SESSION_CLOSE_ALL` 协议；内部自动化应走主应用 `ecodex.v2` 方法 `app.exit`，并传入 `{"terminateTerminals":true}`。这样终止动作与 ECodex 退出绑定，避免保留单独清理后台终端的旧入口。

## `resume.json`

resume binding 记录如何恢复某个 Pane 的外部会话，例如 tmux：

```json
{
  "version": 1,
  "bindings": [
    {
      "paneId": "pane:1",
      "kind": "tmux",
      "command": "tmux attach -t demo",
      "workingDirectory": "C:\\repo",
      "trusted": false,
      "createdAtUtc": "2026-01-01T00:00:00Z",
      "updatedAtUtc": "2026-01-01T00:00:00Z"
    }
  ]
}
```

敏感环境变量会在保存前剔除，例如 `TOKEN`、`PASSWORD`、`SECRET`、`API_KEY`。

## 信任模型

- `trusted: false`：只显示“可恢复”提示，不自动执行命令。
- `trusted: true`：用户明确批准后可在开关允许时自动恢复。
- `AutoResumeTrustedBindings`：控制可信 binding 是否自动执行。
- “信任并恢复”会记录用户批准的 prefix。

未信任 binding 必须由用户确认；ECodex 不会静默执行未知命令。

## 命令行操作

```powershell
ecodex surface resume set --pane pane:1 --kind tmux --command "tmux attach -t demo" --trusted false
ecodex surface resume show
ecodex surface resume clear --pane pane:1
ecodex restore-session
```

`ecodex restore-session` 会刷新恢复绑定并定位第一个可恢复 Pane。快捷键：`Ctrl+Shift+O`。

## `ECODEX_WORKSPACE_ID`

启动 shell 时，ECodex 会注入 `ECODEX_WORKSPACE_ID`。本地 ConPTY 与 daemon 托管会话都可读取该变量，用于脚本判断当前 Workspace。

## 排查

- 恢复提示未出现：检查 `%USERPROFILE%\.ecodex\resume.json` 与 `daemon-debug.log`。
- 自动恢复未执行：确认 binding 是 `trusted: true`，且 `AutoResumeTrustedBindings` 已启用。
- 恢复命令不正确：先用 `trusted: false` 手动确认，再切换可信。
- 数据异常：备份后删除 `resume.json` 可重建绑定。
