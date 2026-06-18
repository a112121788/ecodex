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

连接到 `ecodex-daemon` 时，终端进程由 daemon 托管。关闭按钮会退出 `ecodex-app` 主进程，并按 `PreserveDaemonSessionsOnClose` 设置决定是否保留 daemon 托管终端；默认保留后台终端。最小化仍只把 ECodex 隐藏到系统托盘，主进程、后台终端和通知继续运行；双击托盘图标或托盘菜单“打开 ECodex”可恢复窗口。再次启动 ECodex 也会聚焦并恢复已有实例，不创建第二个长期存活的主进程。

安装版开始菜单 / 桌面快捷方式与主进程使用同一个 AppUserModelID（`ECodex.App`）。因此窗口在后台时，点击任务栏按钮或固定任务栏图标应与点击托盘图标一样激活已有窗口；若从未绑定 AUMID 的裸 exe 路径手动重复启动，操作系统仍会先创建启动进程，随后由 `Global\ECodexMainApp` 互斥体立即退出并通过 `window.focus` 兜底激活已有窗口。

显式退出 ECodex 时，只断开主程序与 daemon 的客户端连接，不触发“daemon 断线后回退到本地 ConPTY”的运行时保护逻辑。只有应用仍在运行且 daemon 意外不可达时，才会把当前 pane 回退为本地终端，并保留已捕获的 snapshot 作为诊断上下文。

`settings.json` 中的 `PreserveDaemonSessionsOnClose` 默认是 `true`，设置窗口“行为 → 保留终端会话”可持久化修改该开关。该开关作用于关闭按钮、托盘退出和 `app.exit` 等主应用退出路径；若设为 `false`，退出前会通过 daemon 的 `SESSION_LIST` + 逐个 `SESSION_CLOSE` 终止当前托管会话。

托盘菜单提供两条显式退出路径：“退出并保留终端”会强制保留 daemon 托管会话后退出主 UI；“退出并终止终端”会先通过 `SESSION_LIST` + 逐个 `SESSION_CLOSE` 终止当前 daemon 会话，再退出主 UI，并在 `daemon-debug.log` 记录终止数量或失败原因。

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

## ECodex 终端上下文环境变量

启动 shell 时，ECodex 会注入以下环境变量。本地 ConPTY 与 daemon 托管会话都可读取这些变量，用于脚本判断当前终端所属的 Workspace / Surface / Pane，也用于 PowerShell shell integration 回传命令生命周期上下文。

| 变量 | 含义 |
|---|---|
| `ECODEX_WORKSPACE_ID` | 当前 Workspace ID |
| `ECODEX_SURFACE_ID` | 当前 Surface ID |
| `ECODEX_PANE_ID` | 当前 Pane ID |

## 排查

- 恢复提示未出现：检查 `%USERPROFILE%\.ecodex\resume.json` 与 `daemon-debug.log`。
- 自动恢复未执行：确认 binding 是 `trusted: true`，且 `AutoResumeTrustedBindings` 已启用。
- 恢复命令不正确：先用 `trusted: false` 手动确认，再切换可信。
- 数据异常：备份后删除 `resume.json` 可重建绑定。

## Session Vault 失败 loop 预览

`Ctrl+Shift+V` 打开 Session Vault 后，选择一条 terminal transcript，再点击“生成失败 loop 预览”可把同一 Workspace / Surface / Pane 附近的失败命令、相关 transcript 摘要和已接入的诊断片段格式化为可复制文本。

该入口复用 Core 层 `FailureLoopEvidenceCollector` 和 `FailureLoopEvidencePreviewFormatter`，UI 层只展示结果，不重新拼接证据，也不直接扫描 `%USERPROFILE%` 下的 `daemon-debug.log`。如果没有找到匹配的失败命令或证据，会显示 `No failure loop evidence available.`。

AgentConversation Core 存储、runtime recorder seam 与 failure-loop `AgentMessages` provider 已经落地，Session Vault 通过 App 层显式 provider 读取 app-owned agent root；预览不会自行实例化 `AgentConversationStoreService`，也不会默认扫描其他 `%USERPROFILE%` profile。

Root 语义：Session Vault 不自行 new `AgentConversationStoreService`；App 层显式传入 `AgentMessages` provider。默认产品根目录只能是 `CompatibilityOptions.GetAppDataDir()` 下的 `agent` 子目录；目录不存在或 runtime 尚未写入时，Agent 消息区保持空集合 / 无匹配消息。

### 手动 smoke checklist

在 Windows GUI 环境验证 Session Vault 失败 loop 预览时，按以下步骤记录证据：

1. 准备失败命令：在 ECodex terminal pane 中运行一个确定会失败的命令，例如 `dotnet test --filter NoSuchTest`，确认退出码非 0。
2. 捕获 transcript：关闭对应 pane、清屏或触发现有会话存档捕获路径，确保 Session Vault 中出现同一 Workspace / Surface / Pane 的 terminal transcript。
3. 打开 Session Vault：按 `Ctrl+Shift+V`，选中刚捕获的 transcript，点击“生成失败 loop 预览”。
4. 生成预览：确认只读文本区出现 `Failure Loop Evidence`、失败命令、`Transcripts` 区块；点击“全部复制”后粘贴到临时文本编辑器核对内容可复制。
5. 无证据负控：选择一个没有附近失败命令的 transcript，点击“生成失败 loop 预览”，应显示 `No failure loop evidence available.`。
6. 边界记录：虽然 Core 已有 `AgentConversationRecorder` seam，但在 Agent runtime 接线写入真实消息前，当前结果不能作为 AgentConversation live 证据；Agent 消息区可能为空集合 / 无匹配消息。

检查过程中不要手动打开或复制 `%USERPROFILE%\.ecodex\secrets.json`、`.env*`、`config/credentials*` 或 `secrets/**`；如果需要 daemon 诊断证据，只记录按钮生成的预览文本和手测截图，不直接粘贴完整真实日志。
