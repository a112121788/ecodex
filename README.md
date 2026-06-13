# ECode

一款运行在 Windows 上的深色、键盘优先的终端复用器，灵感来自 cmux，但底层使用 WPF + ConPTY 原生构建。

CLI 主二进制为 `ecode-app.exe`，CLI 工具为 `ecode.exe`，守护进程为 `ecode-daemon.exe`。

---

## 缘起 / 谁用 / 是什么 / 怎么用

| 缘起（痛点）                              | 谁用（用户）                              | 是什么（能力）                          | 怎么用                                                                       |
| ----------------------------------------- | ----------------------------------------- | --------------------------------------- | ---------------------------------------------------------------------------- |
| 在不同项目与 shell 之间切换时会丢失上下文 | 同时维护多个仓库/任务的同学               | **项目 + 标签页（surfaces）**     | `Ctrl+N` 新建项目，`Ctrl+T` 新建标签页，`Ctrl+1..9` 切换               |
| 一个终端永远不够用                        | 命令行重度用户、agent 工作流              | **分屏（左右/上下）**             | `Ctrl+D` 向右分屏，`Ctrl+Shift+D` 向下分屏，`Ctrl+Alt+方向键` 聚焦面板 |
| 错过 agent 的关键输出                     | 使用 AI 辅助编码的同学（Claude/Codex 等） | **OSC 通知 + 未读追踪**           | `Ctrl+I` 打开通知，`Ctrl+Shift+U` 跳到最新未读                           |
| 需要可审计的执行命令记录                  | 注重安全/排障的工作流                     | **命令日志 + 历史选择器**         | `Ctrl+Shift+L` 查看日志，`Ctrl+Alt+H` 命令历史，可在界面里插入/执行      |
| 希望崩溃/重启后能完整恢复会话             | 长时间运行的会话                          | **会话持久化 + 脚本捕获**         | 启动时自动恢复，并打开**Session Vault**（`Ctrl+Shift+V`）            |
| 像 Termius vault 一样可搜索历史输出       | 需要复盘终端会话的同学                    | **Session Vault 浏览器**          | 打开 vault，过滤捕获记录，预览脚本，复制/打开文件                            |
| 希望深色主题统一且可定制                  | 在意交互/可读性的同学                     | **深色 UI + 终端主题定制**        | 设置（`Ctrl+,`）调整颜色/字体/光标 + 项目配色                              |
| 不想反复找鼠标触发操作                    | 键盘优先的进阶用户                        | **命令面板 + 快捷键**             | `Ctrl+Shift+P` 打开命令面板，菜单镜像主快捷键                              |
| 需要脚本/工具自动化                       | 集成方/agent hook                         | **命名管道 CLI API**（`ecode`） | `ecode notify`、`ecode workspace`、`ecode split`、`ecode status`     |

---

## 核心能力

- 原生 **ConPTY 终端模拟**（真正的 Windows 终端后端）
- 项目侧边栏，带元信息（git 分支、当前目录、通知）
- 多标签页与分屏布局管理
- 通知接入（OSC 9/99/777），适配编码 agent
- 命令日志/历史，支持过滤与一键回放
- 终端脚本捕获 + Session Vault 浏览
- 会话持久化（窗口 + 项目/标签页/面板状态）
- 深色桌面 UI，主打键盘优先的导航

---

## 截图

<details>
  <summary>展开截图</summary>

<p><strong>主项目视图</strong></p>
  <img src="assets/screenshots/1.jpg" alt="ECode 主项目" width="1000" />

<p><strong>代码片段面板</strong></p>
  <img src="assets/screenshots/2.jpg" alt="ECode 代码片段面板" width="700" />

<p><strong>命令日志窗口</strong></p>
  <img src="assets/screenshots/3.jpg" alt="ECode 命令日志" width="1000" />
</details>

---

## 构建与运行（Windows）

### 环境要求

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- 可选：Visual Studio 2022 / Build Tools

### 克隆

```powershell
git clone <repo-url> ECode
cd ECode
```

### 开发模式运行

```powershell
dotnet build ECode.sln -c Debug
dotnet run --project src/ECode/ECode.csproj -c Debug
```

---

## 在 Windows 上构建 `.exe`

### 1) 依赖框架的 `.exe`（体积最小）

```powershell
dotnet publish src/ECode/ECode.csproj -c Release -r win-x64 --self-contained false -o publish/ecode-win-x64
```

产物：

- `publish/ecode-win-x64/ecode-app.exe`

适用场景：目标机器上已经装好 .NET 运行时。

### 2) 自包含 `.exe`（无需安装运行时）

```powershell
dotnet publish src/ECode/ECode.csproj -c Release -r win-x64 --self-contained true -o publish/ecode-win-x64-sc
```

产物：

- `publish/ecode-win-x64-sc/ecode-app.exe`

### 3) 单文件自包含 `.exe`（便携产物）

```powershell
dotnet publish src/ECode/ECode.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false -o publish/ecode-win-x64-single
```

产物：

- `publish/ecode-win-x64-single/ecode-app.exe`

> 注意：依赖 WebView2 的功能可能需要目标系统已安装 WebView2 运行时，具体取决于系统状态。

### 构建 CLI 可执行文件

```powershell
dotnet publish src/ECode.Cli/ECode.Cli.csproj -c Release -r win-x64 --self-contained true -o publish/ecode-cli
```

把 `publish/ecode-cli` 加入 `PATH` 后即可全局使用 `ecode`。

---

## 前 5 分钟上手

1. 启动 `ecode-app.exe`
2. 用 `Ctrl+N` 为你的仓库创建一个项目
3. 用 `Ctrl+T` 新增更多标签页
4. 用 `Ctrl+D` / `Ctrl+Shift+D` 分屏
5. 用 `Ctrl+Shift+P` 打开命令面板快速操作
6. 用 `Ctrl+Shift+L` 打开日志
7. 用 `Ctrl+Shift+V` 打开 Session Vault
8. 用 `Ctrl+,` 打开设置，调终端主题/字体/光标

---

## 项目命令：`ecode.json`

ECode 会在当前项目目录读取项目级命令，并把它们显示到命令面板（`Ctrl+Shift+P`）里。

读取顺序：

1. `%USERPROFILE%\.config\ecode\ecode.json`
2. `<当前项目>\.ecode\ecode.json`
3. `<当前项目>\ecode.json`

本地项目配置会覆盖全局同名命令 / action。配置文件支持 JSONC 注释和尾随逗号；解析错误会以“ecode.json 配置错误/警告”的形式出现在命令面板中。

示例：

```jsonc
{
  "commands": [
    {
      "name": "Run Tests",
      "description": "Run project tests",
      "keywords": ["test", "check"],
      "command": "dotnet test",
      "confirm": true
    }
  ],
  "actions": {
    "codex": {
      "type": "command",
      "title": "Codex",
      "subtitle": "Start Codex in a new tab",
      "command": "codex",
      "target": "newTabInCurrentPane",
      "palette": true,
      "confirm": true
    }
  }
}
```

支持的 `target`：

| target | 行为 |
| ------ | ---- |
| `currentTerminal` | 在当前聚焦终端写入命令并回车 |
| `newTabInCurrentPane` | 新建标签页后写入命令并回车 |

> 安全提示：仓库里的 `ecode.json` 可能包含任意 shell 命令。建议对会修改文件、安装依赖或访问凭据的命令设置 `confirm: true`。

---

## 快捷键

### 项目

| 快捷键           | 动作                 |
| ---------------- | -------------------- |
| `Ctrl+N`       | 新建项目             |
| `Ctrl+1..8`    | 跳转到第 1..8 个项目 |
| `Ctrl+9`       | 跳转到最后一个项目   |
| `Ctrl+Shift+W` | 关闭项目             |
| `Ctrl+Shift+R` | 重命名项目           |
| `Ctrl+B`       | 显示/隐藏侧边栏      |

### 标签页（surfaces）

| 快捷键                            | 动作           |
| --------------------------------- | -------------- |
| `Ctrl+T`                        | 新建标签页     |
| `Ctrl+W`                        | 关闭标签页     |
| `Ctrl+Shift+]`                  | 下一个标签页   |
| `Ctrl+Shift+[`                  | 上一个标签页   |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | 循环切换标签页 |

### 面板

| 快捷键              | 动作            |
| ------------------- | --------------- |
| `Ctrl+D`          | 向右分屏        |
| `Ctrl+Shift+D`    | 向下分屏        |
| `Ctrl+Alt+方向键` | 聚焦相邻面板    |
| `Ctrl+Shift+Z`    | 最大化/还原面板 |

### 效率工具

| 快捷键           | 动作           |
| ---------------- | -------------- |
| `Ctrl+Shift+P` | 命令面板       |
| `Ctrl+Shift+F` | 搜索浮层       |
| `Ctrl+Shift+L` | 命令日志       |
| `Ctrl+Shift+V` | Session Vault  |
| `Ctrl+Alt+H`   | 命令历史选择器 |
| `Ctrl+,`       | 设置           |

---

## CLI 用法

```powershell
# 发送一条通知（例如，来自 agent hook）
ecode notify --title "Claude Code" --body "等待输入"

# 项目管理
ecode workspace list
ecode workspace create --name "My Project"
ecode workspace select --index 0

# 标签页/面板操作
ecode surface create
ecode split right
ecode split down

# 查看状态
ecode status
```

---

## 架构概览

```text
src/
  ECode/         WPF 桌面应用（视图、控件、主题）
  ECode.Core/    终端引擎、模型、服务、持久化、IPC
  ECode.Cli/     用于自动化的命令行客户端
tests/
  ECode.Tests/   单元测试
```

---

## 许可协议

MIT
