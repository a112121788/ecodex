# 安装

ECodex 支持多种 Windows 分发方式：self-contained 目录、Velopack 更新源、Inno Setup 安装器、MSIX 企业包，以及仅命令行包。

## 系统要求

- Windows 10 1809 / build 17763 或更新版本，用于 ConPTY。
- 集成浏览器需要 WebView2 Runtime。
- 源码构建需要 .NET 10 SDK；文档站需要 Node.js 与 `npm install`。
- 运行时数据保存在 `%USERPROFILE%\.ecodex`，卸载与更新默认保留该目录。

## 推荐安装路径

| 形态 | 适用场景 | 说明 |
|---|---|---|
| zip / self-contained 目录 | 大多数普通用户 | 下载 `ecodex-win-x64-sc` 后解压运行 `ecodex-app.exe`。 |
| Velopack 安装器与 feed | 需要自动更新的用户 | 安装后可用 `ecodex update check` / `ecodex update install`。 |
| Inno Setup 备用安装器 | 传统桌面安装 | 创建开始菜单 / 桌面快捷方式，卸载只清理安装目录。 |
| MSIX 企业包 | 企业分发 | 适合受管环境；需要企业签名或测试签名链。 |
| 命令行专用包 | 自动化脚本 / CI | 将 `ecodex.exe` 所在目录加入 PATH。 |

## zip / self-contained 目录

1. 下载 `ecodex-win-x64-sc` 产物并解压到固定目录，例如 `C:\Tools\ECodex`。
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

发布前先生成 app 与 命令行：

```powershell
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor SelfContained
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor Cli
```

随后使用 Inno Setup Compiler 打包 `installer/ecodex.iss`。

## MSIX 企业包

MSIX 清单文件位于 `installer/AppXManifest.xml`。构建示例：

```powershell
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor MSIX -MakeAppxCommand makeappx.exe
Add-AppxPackage .\publish\msix\ECodex-win-x64-1.0.0.0.msix
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

## 卸载与数据保留

- 删除 self-contained 目录不会删除 `%USERPROFILE%\.ecodex`。
- Inno Setup 卸载只清理安装目录。
- 更新流程不会删除 `session.json`、`resume.json`、`settings.json`。
- 如需彻底清理数据，请手动备份后删除 `%USERPROFILE%\.ecodex`。

## 故障排查

- 命令行工具不在 PATH：运行 `ecodex setup status`，再执行 `ecodex setup install --write true`。
- 集成浏览器不可用：安装或修复 WebView2 Runtime。
- App / daemon 连接失败：查看 `%USERPROFILE%\.ecodex\daemon-debug.log`。
- 发布或 restore 遇到 NuGet 审计网络问题：本地验证可使用 `-p:NuGetAudit=false`。
