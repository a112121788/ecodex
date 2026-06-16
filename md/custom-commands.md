# 自定义命令

ECodex 使用 `ecodex.json` 定义项目命令、动作和启动布局。它适合把常用构建、测试、服务启动、集成浏览器与命令面板入口固化到仓库中。

## 加载位置

按优先级读取：

1. 当前项目：`.ecodex/ecodex.json`
2. 用户配置：`%USERPROFILE%\.config\ecodex\ecodex.json`
3. 兼容读取：`.cmux/cmux.json`（仅兼容迁移）

排查命令：

```powershell
ecodex config diagnostics
ecodex config reload
```

## 最小示例

```json
{
  "commands": [
    {
      "name": "Run Tests",
      "command": "dotnet test",
      "target": "currentTerminal"
    }
  ],
  "actions": {
    "codex": {
      "type": "command",
      "title": "Codex",
      "command": "codex",
      "confirm": true
    }
  }
}
```

## `commands`

`commands` 是命令面板中的普通命令列表。

| 字段 | 说明 |
|---|---|
| `name` | 命令面板展示名称。 |
| `command` | 要发送或执行的命令。 |
| `target` | 执行目标，例如 `currentTerminal`、`newTabInCurrentPane`。 |
| `cwd` | 可选工作目录。 |
| `env` | 可选环境变量。 |
| `confirm` | 为 `true` 时执行前要求用户确认。 |

常用 target：

- `currentTerminal`：写入当前 Pane。
- `newTabInCurrentPane`：在当前 Pane 所在 Surface 新建标签后执行。
- `newWorkspace`：创建 Workspace 后执行。

## `actions`

`actions` 适合定义按钮、快捷动作或集成浏览器入口。

```json
{
  "actions": {
    "open-preview": {
      "type": "browser",
      "title": "Preview",
      "url": "http://localhost:5173",
      "target": "splitRight"
    }
  }
}
```

| 字段 | 说明 |
|---|---|
| `type` | `command` 或 `browser`。 |
| `title` | UI 展示标题。 |
| `command` | `type=command` 时执行的命令。 |
| `url` | `type=browser` 时打开的 URL。 |
| `target` | 打开位置，例如 `currentTerminal`、`splitRight`。 |
| `confirm` | 高风险命令建议设为 `true`。 |

## Surface Tab 按钮

可通过 `ui.surfaceTabBar.buttons` 把 action 放到 Surface tab bar：

```json
{
  "ui": {
    "surfaceTabBar": {
      "buttons": [
        { "action": "open-preview", "label": "Preview" },
        { "action": "codex", "label": "Codex" }
      ]
    }
  }
}
```

## Workspace 启动布局

`workspace.surfaces` 可在启动或 reload 时创建 / 复用 `terminal` 与 `browser`（集成浏览器）类型的 Surface：

```json
{
  "workspace": {
    "selectedSurfaceIndex": 0,
    "surfaces": [
      {
        "title": "Dev",
        "type": "terminal",
        "command": "pwsh"
      },
      {
        "title": "Preview",
        "type": "browser",
        "url": "http://localhost:5173"
      }
    ]
  }
}
```

`selectedSurfaceIndex` 指定 reload 后选中的 Surface。

## 热重载

```powershell
ecodex reload-config
ecodex config reload
ecodex config diagnostics
```

`Ctrl+Shift+,` 会触发配置重载。重载会刷新命令面板并尽量复用已存在的集成浏览器。

## 安全建议

- 不要把真实 API key / token 写入 `ecodex.json`。
- 对会执行外部命令、安装依赖、删除文件的 action 使用 `confirm: true`。
- 浏览器 action 只保存 URL，不保存 cookie 或 localStorage。
- 如果配置来自旧 `.cmux/cmux.json`，建议迁移到 `.ecodex/ecodex.json`。
