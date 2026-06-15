# ECodeX

[![CI](https://github.com/a112121788/ecodex/actions/workflows/ci.yml/badge.svg)](https://github.com/a112121788/ecodex/actions/workflows/ci.yml)
[![Docs](https://github.com/a112121788/ecodex/actions/workflows/docs.yml/badge.svg)](https://github.com/a112121788/ecodex/actions/workflows/docs.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

ECodeX 是一款面向 Windows 的原生 SuperTerminal：用 WPF + ConPTY 构建，把多项目终端、标签页、分屏、浏览器 Surface、命令日志、会话恢复和脚本化自动化放在同一个键盘优先工作台里。

- 文档站点：https://a112121788.github.io/ecodex/
- 安装指南：https://a112121788.github.io/ecodex/installation
- 命令行：https://a112121788.github.io/ecodex/cli
- 浏览器 API：https://a112121788.github.io/ecodex/browser-api

> 主程序为 `ecodex-app.exe`，命令行工具为 `ecodex.exe`，后台守护进程为 `ecodex-daemon.exe`。

## 为什么做它

| 常见痛点 | ECodeX 的解法 | 高频入口 |
| --- | --- | --- |
| 多个仓库、多个 shell 来回切换容易丢上下文 | Workspace + Surface 把项目、标签页和终端状态组织起来 | `Ctrl+N`、`Ctrl+T`、`Ctrl+1..9` |
| 一个终端窗口不够跑测试、日志和 dev server | Pane 分屏支持左右/上下布局和键盘聚焦 | `Ctrl+D`、`Ctrl+Shift+D`、`Ctrl+Alt+方向键` |
| 长任务输出被刷走，失败原因难复盘 | OSC 通知、未读追踪、命令日志和历史回放 | `Ctrl+I`、`Ctrl+Shift+L`、`Ctrl+Alt+H` |
| 重启或崩溃后需要重新搭工作区 | 会话持久化 + Session Vault 记录终端脚本 | `Ctrl+Shift+V` |
| 需要让外部脚本驱动终端 UI | 命名管道命令行 API 支持通知、workspace、surface、split、status | `ecodex notify`、`ecodex split right` |

## 核心能力

- 原生 ConPTY 终端后端，服务 Windows 10/11 的日常 shell、构建和长任务工作流。
- Workspace 侧边栏展示目录、git 分支、通知等上下文，适合同时维护多个项目。
- 多 Surface + Pane 分屏，支持快速创建、切换、关闭和最大化终端面板。
- OSC 9/99/777 通知接入，配合未读追踪减少遗漏关键输出。
- 命令日志、命令历史和 `ecodex.json` 项目命令，让常用操作可审计、可回放、可复用。
- 浏览器 Surface 基于 WebView2，并暴露 snapshot、click、fill、eval、screenshot 等自动化能力。
- 会话恢复和 Session Vault 帮助恢复窗口、项目、标签页、面板与终端脚本记录。

## 截图

<details>
  <summary>展开截图</summary>

<p><strong>主项目视图</strong></p>
<img src="assets/screenshots/1.jpg" alt="ECodeX 主项目" width="1000" />

<p><strong>代码片段面板</strong></p>
<img src="assets/screenshots/2.jpg" alt="ECodeX 代码片段面板" width="700" />

<p><strong>命令日志窗口</strong></p>
<img src="assets/screenshots/3.jpg" alt="ECodeX 命令日志" width="1000" />
</details>

## 快速上手

1. 启动 `ecodex-app.exe`。
2. 使用 `Ctrl+N` 为仓库创建 Workspace。
3. 使用 `Ctrl+T` 新建 Surface，`Ctrl+1..9` 在 Workspace 间切换。
4. 使用 `Ctrl+D` / `Ctrl+Shift+D` 分屏，`Ctrl+Alt+方向键` 聚焦相邻 Pane。
5. 使用 `Ctrl+Shift+P` 打开命令面板，`Ctrl+Shift+L` 查看命令日志。
6. 使用 `Ctrl+Shift+V` 打开 Session Vault，复盘恢复后的终端脚本记录。
7. 使用 `Ctrl+,` 调整终端字体、主题、光标和 Workspace 配色。

更多教程见 [快速上手](https://a112121788.github.io/ecodex/getting-started)。

## 安装与构建

### 环境要求

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- 可选：Visual Studio 2022 / Build Tools

### 开发运行

```powershell
git clone https://github.com/a112121788/ecodex.git ECodeX
cd ECodeX
dotnet build ECodeX.sln -c Debug
dotnet run --project src/ECodeX/ECodeX.csproj -c Debug
```

### 发布 Windows 可执行文件

```powershell
# 依赖框架，体积最小，目标机器需要 .NET Runtime
dotnet publish src/ECodeX/ECodeX.csproj -c Release -r win-x64 --self-contained false -o publish/ecodex-win-x64

# 自包含，目标机器不需要单独安装 .NET Runtime
dotnet publish src/ECodeX/ECodeX.csproj -c Release -r win-x64 --self-contained true -o publish/ecodex-win-x64-sc

# 单文件自包含，适合便携分发
dotnet publish src/ECodeX/ECodeX.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false -o publish/ecodex-win-x64-single

# 命令行
dotnet publish src/ECodeX.Cli/ECodeX.Cli.csproj -c Release -r win-x64 --self-contained true -o publish/ecodex-cli
```

> 浏览器 Surface 依赖 WebView2。部分 Windows 环境可能需要先安装 WebView2 Runtime。

## 命令行示例

```powershell
# 发送通知，适合构建脚本或长任务提醒
ecodex notify --title "Build" --body "等待输入"

# Workspace 管理
ecodex workspace list
ecodex workspace create --name "My Project"
ecodex workspace select --index 0

# Surface / Pane 操作
ecodex surface create
ecodex split right
ecodex split down

# 状态与配置
ecodex status
ecodex reload-config
```

## 项目命令：`ecodex.json`

ECodeX 会读取全局或项目级 `ecodex.json`，并把命令显示到命令面板。读取顺序如下，后读取的本地配置会覆盖同名全局命令或 action。

1. `%USERPROFILE%\.config\ecodex\ecodex.json`
2. `<当前项目>\.ecodex\ecodex.json`
3. `<当前项目>\ecodex.json`

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
    "devServer": {
      "type": "command",
      "title": "Dev Server",
      "subtitle": "Start dev server in a new tab",
      "command": "npm run dev",
      "target": "newTabInCurrentPane",
      "palette": true,
      "confirm": true
    }
  }
}
```

支持的 `target`：

| target | 行为 |
| --- | --- |
| `currentTerminal` | 在当前聚焦终端写入命令并回车 |
| `newTabInCurrentPane` | 新建标签页后写入命令并回车 |

> 安全提示：仓库里的 `ecodex.json` 可能包含任意 shell 命令。建议对会修改文件、安装依赖或访问凭据的命令设置 `confirm: true`。

## 常用快捷键

| 区域 | 快捷键 | 动作 |
| --- | --- | --- |
| Workspace | `Ctrl+N` | 新建项目 |
| Workspace | `Ctrl+1..8` / `Ctrl+9` | 切换到指定项目 / 最后一个项目 |
| Workspace | `Ctrl+B` | 显示或隐藏侧边栏 |
| Surface | `Ctrl+T` / `Ctrl+W` | 新建 / 关闭标签页 |
| Surface | `Ctrl+Tab` / `Ctrl+Shift+Tab` | 循环切换标签页 |
| Pane | `Ctrl+D` / `Ctrl+Shift+D` | 向右 / 向下分屏 |
| Pane | `Ctrl+Alt+方向键` | 聚焦相邻面板 |
| 工具 | `Ctrl+Shift+P` | 命令面板 |
| 工具 | `Ctrl+Shift+L` | 命令日志 |
| 工具 | `Ctrl+Shift+V` | Session Vault |
| 设置 | `Ctrl+,` / `Ctrl+Shift+,` | 设置 / 重载 `ecodex.json` |

## 仓库结构

```text
src/
  ECodeX/          WPF 桌面应用（视图、控件、主题）
  ECodeX.Core/     终端引擎、模型、服务、持久化、IPC
  ECodeX.Cli/      命令行客户端
  ECodeX.Daemon/   后台守护进程
  ECodeX.Updater/  更新检查与安装
tests/
  ECodeX.Tests/    单元测试
  ECodeX.Smoke/    冒烟测试
docs/             VitePress 用户文档
spec/             架构、模块、协议和发布设计文档
```

## 开发者文档

- 用户文档：`docs/`，在线站点 https://a112121788.github.io/ecodex/
- 设计文档：`spec/README.md`
- 发布就绪清单：`docs/release-readiness.md`
- 本地文档预览：`npm run docs:dev`
- 文档构建校验：`npm run docs:build`

GitHub Pages 使用 `Deploy from a branch`，发布分支为 `docs`，目录为 `/ (root)`。`docs` 分支不手动维护；推送到 `main` 后，`.github/workflows/docs.yml` 会从 `docs/` 构建 VitePress，并把 `docs/.vitepress/dist` 的编译产物覆盖发布到 `docs` 分支。

## 许可协议

MIT。详见 [LICENSE](LICENSE)。
