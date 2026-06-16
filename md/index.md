---
layout: home

hero:
  name: ECodex
  text: Windows 原生终端工作台
  tagline: 为多仓库、多标签页、分屏终端、集成浏览器、自动化 API 与会话恢复设计的键盘优先 SuperTerminal。
  image:
    src: /app-icon.png
    alt: ECodex 图标
  actions:
    - theme: brand
      text: 开始使用
      link: /getting-started
    - theme: alt
      text: 安装
      link: /installation
    - theme: alt
      text: GitHub
      link: https://github.com/a112121788/ecodex

features:
  - title: 多项目不丢上下文
    details: Workspace、Surface 与 Pane 把仓库、标签页、终端状态和通知组织在一个工作台里。
  - title: 长任务更容易复盘
    details: OSC 通知、命令日志、历史选择器与 Session Vault 让关键输出可追踪、可恢复。
  - title: 终端 + 浏览器协同
    details: 集成浏览器内置 WebView2，并提供 snapshot、click、fill、eval、screenshot 等自动化能力。
  - title: 项目命令可复用
    details: 通过 ecodex.json 管理项目级命令，支持确认执行、命令面板入口和热重载。
  - title: Windows 原生集成
    details: 基于 WPF + ConPTY，支持 PATH/profile setup、Windows Terminal profile、doctor 检查。
  - title: 面向发布交付
    details: 覆盖 zip/self-contained、Velopack、Inno Setup、MSIX、release notes 与发布清单。
---

## 适合谁

ECodex 适合同时维护多个仓库、经常跑长任务、需要把终端和浏览器预览放在同一工作流里的 Windows 用户。它不是只做一个更漂亮的 shell 外壳，而是把项目、面板、日志、通知和自动化接口一起管理。

## 高频入口

| 你想做什么 | 推荐入口 |
| --- | --- |
| 第一次安装并启动 | [安装](./installation.md) + [快速上手](./getting-started.md) |
| 学会 Workspace、Surface、Pane | [快速上手](./getting-started.md) |
| 用脚本控制 ECodex | [命令行](./cli.md) |
| 自动化浏览器面板 | [浏览器 API](./browser-api.md) |
| 管理项目级命令 | [自定义命令](./custom-commands.md) |
| 排查环境、PATH、WebView2、daemon | [故障排查](./troubleshooting.md) |

## 文档地图

- [安装](./installation.md)：zip/self-contained、Velopack、Inno Setup、MSIX 与卸载策略。
- [快速上手](./getting-started.md)：首次启动、Workspace、Surface、Pane、集成浏览器、通知与恢复绑定。
- [命令行](./cli.md)：v1/v2 命令、全局参数、setup/update/doctor/completion。
- [浏览器 API](./browser-api.md)：WebView2 集成浏览器的脚本化操作能力。
- [自定义命令](./custom-commands.md)：`ecodex.json` 的 commands/actions、确认执行与热重载。
- [故障排查](./troubleshooting.md)：`ecodex doctor`、`daemon-debug.log`、WebView2、PATH、恢复与更新问题。
- [发布就绪](./release-readiness.md)：1.0 的 P0/P1 门槛与发布前验证命令。
- [1.0.0 发布说明](./release-notes/1.0.0.md)：可复制到 GitHub Release 的用户说明。
