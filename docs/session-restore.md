# 会话恢复

ECode 会保存窗口、Workspace、Surface、Pane 布局，以及终端与集成浏览器的关键状态。目标是在重启后尽量恢复工作上下文，同时避免自动执行不可信命令。

## 运行时文件

| 文件 | 说明 |
|---|---|
| `%USERPROFILE%\.ecode\session.json` | Window / Workspace / Surface 布局、terminal pane snapshots、集成浏览器元数据。 |
| `%USERPROFILE%\.ecode\resume.json` | tmux、agent、shell 等恢复绑定。 |
| `%USERPROFILE%\.ecode\daemon-debug.log` | 恢复、attach、daemon 与 IPC 诊断日志。 |

## `session.json`

`session.json` 保存：

- Workspace 名称、顺序、选中项。
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

未信任 binding 必须由用户确认；ECode 不会静默执行未知命令。

## 命令行操作

```powershell
ecode surface resume set --pane pane:1 --kind tmux --command "tmux attach -t demo" --trusted false
ecode surface resume show
ecode surface resume clear --pane pane:1
ecode restore-session
```

`ecode restore-session` 会刷新恢复绑定并定位第一个可恢复 Pane。快捷键：`Ctrl+Shift+O`。

## `ECODE_WORKSPACE_ID`

启动 shell 时，ECode 会注入 `ECODE_WORKSPACE_ID`。本地 ConPTY 与 daemon 托管会话都可读取该变量，用于脚本判断当前 Workspace。

## 排查

- 恢复提示未出现：检查 `%USERPROFILE%\.ecode\resume.json` 与 `daemon-debug.log`。
- 自动恢复未执行：确认 binding 是 `trusted: true`，且 `AutoResumeTrustedBindings` 已启用。
- 恢复命令不正确：先用 `trusted: false` 手动确认，再切换可信。
- 数据异常：备份后删除 `resume.json` 可重建绑定。
