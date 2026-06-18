# 安装

ECodex 支持多种 Windows 分发方式：self-contained 目录、Velopack 更新源、Inno Setup 安装器、MSIX 企业包，以及仅命令行包。

## 系统要求

- Windows 10 1809 / build 17763 或更新版本，用于 ConPTY。
- 集成浏览器需要 WebView2 Runtime。
- 源码构建需要 .NET 10 SDK；文档站需要 Node.js 与 `npm install`。
- 运行时数据保存在 `%USERPROFILE%\.ecodex`，卸载与更新默认保留该目录。
- 默认 skills 会从安装目录的 `default-skills` 种子安装到 `%USERPROFILE%\.agents\skills`；同名目录跳过，不覆盖用户已有内容。

## 推荐安装路径

| 形态 | 适用场景 | 说明 |
|---|---|---|
| zip / self-contained 目录 | 大多数普通用户 | 下载包含 `ecodex-app.exe` 与 `ecodex-daemon.exe` 的 `ecodex-win-x64-sc` 后解压运行。 |
| Velopack 安装器与 feed | 需要自动更新的用户 | 安装后可用 `ecodex update check` / `ecodex update install`。 |
| Inno Setup 备用安装器 | 传统桌面安装 | 创建开始菜单 / 桌面快捷方式，卸载只清理安装目录。 |
| MSIX 企业包 | 企业分发 | 适合受管环境；需要企业签名或测试签名链。 |
| 命令行专用包 | 自动化脚本 / CI | 将 `ecodex.exe` 所在目录加入 PATH。 |

## zip / self-contained 目录

1. 下载 `ecodex-win-x64-sc` 产物并解压到固定目录，例如 `C:\Tools\ECodex`，确认目录中同时存在 `ecodex-app.exe` 和 `ecodex-daemon.exe`。
2. 双击 `ecodex-app.exe` 启动主程序。
3. 如需 命令行，全局 PATH 指向同一目录，或执行：

```powershell
ecodex setup install --install-dir C:\Tools\ECodex --write true
```

验证：

```powershell
ecodex version
ecodex doctor
```

## Velopack 安装与更新

发布产物包含 Velopack setup 与 `RELEASES` feed 时，可通过安装器完成首次安装。后续检查更新：

```powershell
ecodex update check --feed https://example.com/ecodex/
ecodex update install --feed https://example.com/ecodex/
```

构建 Velopack 产物：

```powershell
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor Velopack -VpkCommand vpk
```

## Inno Setup 备用安装器

Inno Setup 脚本位于 `installer/ecodex.iss`。它会安装 app 与 命令行，创建快捷方式，并在卸载时只清理安装目录，不删除 `%USERPROFILE%\.ecodex`。安装与卸载向导固定使用简体中文界面；脚本引用项目内置的 `installer/Languages/ChineseSimplified.isl`，构建机只需安装 Inno Setup Compiler，不依赖本机语言包。该语言文件来源为 `jrsoftware/issrc` 的 `Files/Languages/Unofficial/ChineseSimplified.isl`，更新时应保留原文件头部说明。

发布验收需要覆盖安装向导、覆盖安装向导、卸载确认、卸载进度、开始菜单/桌面快捷方式任务与完成页，确认用户可见文案均为简体中文。静默安装 / 卸载不受中文文案影响；卸载仍只清理安装目录，保留 `%USERPROFILE%\.ecodex` 和 `%USERPROFILE%\.agents\skills`。

发布前先生成 app 与 命令行：

```powershell
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor SelfContained
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor Cli
```

随后使用 Inno Setup Compiler 打包 `installer/ecodex.iss`。

## Windows Toast 点击激活验收

Windows Toast 点击和任务栏固定图标激活链路依赖系统通知权限、未开启专注助手 / 请勿打扰、稳定的开始菜单快捷方式，以及非打包 WPF 可识别的 AppUserModelID（AUMID）。Inno Setup 备用安装器会创建开始菜单快捷方式，并可选创建桌面快捷方式；这些快捷方式必须写入 `ECodex.App`，并指向同一安装目录下的 `ecodex-app.exe`。发布签收时必须确认 Toast 点击不会被系统策略拦截，且运行中点击固定任务栏图标会激活已有窗口而不是启动第二个长期存活的 `ecodex-app`。

维护者在已启动 ECodex 主应用后运行 Windows-only live smoke：

```powershell
pwsh ./scripts/smoke-toast-activation.ps1
pwsh ./scripts/smoke-toast-activation.ps1 -Scenario CodexAttention
pwsh ./scripts/smoke-toast-activation.ps1 -Interactive -Cleanup
```

脚本会检查 Windows、CLI、主应用 pipe、系统 Toast 权限、专注助手提示、开始菜单 / 桌面快捷方式和 AppUserModelID 线索，然后用真实 `workspace/surface/pane` 上下文生成一条生命周期通知。`-Scenario CodexAttention` 会写入一条 Codex-like 等待输入输出，验证 Codex 等待输入提醒生成 `AgentAttention` 通知，并用普通输出做负控。默认输出 JSON 证据和手测步骤；`-Interactive` 会等待人工确认 Toast 是否出现、点击后是否恢复窗口、是否聚焦目标 pane，以及目标缺失时是否打开通知面板 fallback。若缺少 Toast 权限、快捷方式或主应用未运行，脚本会给出可读的 `skipped` / `failed` 原因，不把 CI 静态检查误报成 live 通过。

## MSIX 企业包

MSIX 清单文件位于 `installer/AppXManifest.xml`。构建示例：

```powershell
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor MSIX -MakeAppxCommand makeappx.exe
Add-AppxPackage .\publish\msix\ECodex-win-x64-1.0.2.0.msix
```

MSIX 适合企业环境；普通用户优先选择 self-contained 或 Velopack。

## 命令行专用包

命令行包位于 `publish/ecodex-cli`。将该目录加入 PATH 后可运行：

```powershell
ecodex version
ecodex status
ecodex setup status
ecodex completion powershell
```

## 默认 PowerShell shell integration

ECodex App 首次启动会默认尝试安装 PowerShell shell integration 到当前用户 profile，标记块为 `# >>> ecodex shell integration >>>` / `# <<< ecodex shell integration <<<`。该 hook 回传命令开始、结束、退出码和 ECodex workspace / surface / pane 上下文；当窗口隐藏到托盘或处于非激活状态时，命令结束会按退出码进入未读中心：`0` 生成完成通知，非 `0` 生成失败通知。同 pane、同命令、同退出码的重复事件会做 30 秒冷却；Codex pane 出现等待输入 / 确认 / 错误决策信号时，也会按 pane 生成低噪声未读通知。

- 写入前会备份原 profile 到 `%USERPROFILE%\.ecodex\backups\`。
- 已安装时幂等跳过；内容漂移时只替换 ECodex 标记块。
- 发现标记块冲突时跳过并写入 `daemon-debug.log`，不静默覆盖用户内容。
- 可用 `ecodex setup status` 查看 `PowerShell hook` 状态，用 `ecodex setup uninstall --write true` 移除 ECodex 标记块。

## 默认 skills 种子安装

ECodex App 启动时会检查安装目录下的 `default-skills`，并把其中第一层 skill 目录复制到 `%USERPROFILE%\.agents\skills`。

- 仓库模板源目录是 `assets/default-skills/`。
- 发布包内目录是 `default-skills/`。
- 只复制第一层目录；`default-skills` 根目录下的文件不会作为 skill 安装。
- 如果 `%USERPROFILE%\.agents\skills\<skill-name>` 已存在，ECodex 会跳过该 skill，不覆盖、不合并、不删除。
- 当前机制只负责种子安装，后续用户自行维护的 skill 不会被卸载器清理。

## 卸载与数据保留

- 删除 self-contained 目录不会删除 `%USERPROFILE%\.ecodex`。
- Inno Setup 卸载只清理安装目录。
- 更新流程不会删除 `session.json`、`resume.json`、`settings.json`。
- 卸载不会删除 `%USERPROFILE%\.agents\skills` 下的用户 skills。
- 如需彻底清理数据，请手动备份后删除 `%USERPROFILE%\.ecodex`。

## 故障排查

- 命令行工具不在 PATH：运行 `ecodex setup status`，再执行 `ecodex setup install --write true`。
- 集成浏览器不可用：安装或修复 WebView2 Runtime。
- App / daemon 连接失败：查看 `%USERPROFILE%\.ecodex\daemon-debug.log`。
- 发布或 restore 遇到 NuGet 审计网络问题：本地验证可使用 `-p:NuGetAudit=false`。
