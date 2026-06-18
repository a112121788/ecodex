# 自定义命令

ECodex 使用 `ecodex.json` 定义项目命令、动作和启动布局。它适合把常用构建、测试、服务启动、集成浏览器与命令面板入口固化到仓库中。

## 加载位置

按优先级读取：

1. 用户配置：`%USERPROFILE%\.config\ecodex\ecodex.json`
2. 当前项目：`.ecodex/ecodex.json`
3. 当前项目根目录：`ecodex.json`

后读取的项目配置会覆盖同名全局命令或 action。旧 `.cmux/cmux.json` 不在当前公开加载路径中；迁移时请复制到 `.ecodex/ecodex.json`。

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

## 本仓 dogfood 示例

维护者可参考仓库内置的 `.ecodex/ecodex.example.json`，它预置了本仓常用的 build、unit test、docs build、status 和 health 命令；其中会触发构建、测试或 npm 脚本的命令都设置了 `confirm: true`。

如需在本仓启用该示例，请复制为真实项目配置后重载：

```powershell
Copy-Item .ecodex/ecodex.example.json .ecodex/ecodex.json
ecodex config reload
```

示例文件不会自动覆盖你的 `.ecodex/ecodex.json`，复制前请先检查已有本地配置。

## `commands`

`commands` 是命令面板中的普通命令列表。

| 字段 | 说明 |
|---|---|
| `name` | 命令面板展示名称。 |
| `command` | 要发送或执行的命令。 |
| `target` | 执行目标，例如 `currentTerminal`、`newTabInCurrentPane`。 |
| `confirm` | 为 `true` 时执行前要求用户确认。 |

常用 target：

- `currentTerminal`：写入当前 Pane。
- `newTabInCurrentPane`：在当前 Pane 所在 Surface 新建标签后执行。

## `actions`

`actions` 适合定义可复用的命令面板动作。当前公开接线的 action 类型是 `command`；浏览器入口请优先使用 `workspace.surfaces` 或 `ecodex browser open/new`。

```json
{
  "actions": {
    "dev-server": {
      "type": "command",
      "title": "Dev Server",
      "subtitle": "npm run dev",
      "command": "npm run dev",
      "target": "newTabInCurrentPane",
      "confirm": true
    }
  }
}
```

| 字段 | 说明 |
|---|---|
| `type` | 当前支持 `command`；其他类型会作为诊断警告显示，UI 不执行。 |
| `title` | UI 展示标题。 |
| `subtitle` | 可选副标题。 |
| `command` | `type=command` 时执行的命令。 |
| `target` | 执行目标，例如 `currentTerminal`、`newTabInCurrentPane`。 |
| `palette` | 是否显示到命令面板，默认 `true`。 |
| `confirm` | 高风险命令建议设为 `true`。 |

## Surface Tab 按钮预留字段

配置模型中预留了 `ui.surfaceTabBar.buttons`，但当前 UI 尚未把它作为稳定公开入口接线。不要依赖它触发 action；需要按钮或快捷入口时，请先使用命令面板里的 `commands` / `actions`。

```json
{
  "ui": {
    "surfaceTabBar": {
      "buttons": [
        { "action": "dev-server", "title": "Dev Server" }
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
        "name": "Dev",
        "type": "terminal"
      },
      {
        "name": "Preview",
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
- 浏览器 Surface 配置只保存 URL，不保存 cookie 或 localStorage。
- 如果配置来自旧 `.cmux/cmux.json`，建议迁移到 `.ecodex/ecodex.json`。
