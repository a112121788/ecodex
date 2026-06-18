# 构建、发布与运行

> 描述 ecodex 的构建配置、解决方案布局、产物形态、脚本入口以及运行约束。

---

## 1. 工具链要求

| 项 | 版本 |
|---|---|
| 操作系统 | Windows 10 / 11（x64 / arm64） |
| .NET SDK | .NET 10（含 Desktop Runtime） |
| 目标框架 | `net10.0-windows`（WPF: `net10.0-windows10.0.17763.0`） |
| 可选 | Visual Studio 2022 / MSBuild / Build Tools |

> 当前 `Directory.Build.props` 强制 `TreatWarningsAsErrors=true` + `WarningLevel=7`，所有目标项目必须零警告通过。
> `global.json` 固定 SDK `10.0.301`；如果 PATH 上的 `dotnet` 报告找不到兼容 SDK，优先使用仓库/用户本地 `.dotnet` 目录中的 `dotnet.exe`，例如 `.\.dotnet\dotnet.exe --list-sdks` 或 `$HOME\.dotnet\dotnet.exe --list-sdks`。

## 2. 解决方案布局

```text
ECodex/
├── ECodex.sln                       # 6 个项目
├── Directory.Build.props          # 全局 C# 编译选项
├── README.md / README.en.md
├── assets/                        # 截图、图标
├── scripts/
│   ├── ci.ps1                     # 本地 CI 入口
│   ├── publish.ps1                # 一键发布脚本
│   └── append-wide-tests.ps1      # 追加广覆盖测试脚本
├── spec/                          # 设计文档（本仓库）
├── src/
│   ├── ECodex/                      # WPF 主程序（ecodex-app.exe）
│   ├── ECodex.Cli/                  # CLI（ecodex.exe）
│   ├── ECodex.Core/                 # 类库
│   └── ECodex.Daemon/               # 守护进程（ecodex-daemon.exe）
└── tests/
    ├── ECodex.Tests/                # xUnit 单元测试
    └── ECodex.Smoke/                # ConPTY 集成烟雾测试
```

项目引用：

```text
ECodex       ──▶  ECodex.Core
ECodex.Cli   ──▶  ECodex.Core
ECodex.Daemon──▶  ECodex.Core
ECodex.Tests ──▶  ECodex.Core
ECodex.Smoke ──▶  ECodex.Core
```

## 3. 项目配置速览

| 项目 | 输出类型 | 程序集名 | TargetFramework | 关键包 |
|---|---|---|---|---|
| `ECodex` | `WinExe` | `ecodex-app` | `net10.0-windows10.0.17763.0` | CommunityToolkit.Mvvm 8.3.2、Microsoft.Web.WebView2 1.0.2651.64、Microsoft.Toolkit.Uwp.Notifications 7.1.3 |
| `ECodex.Cli` | `Exe` | `ecodex` | `net10.0-windows` | — |
| `ECodex.Core` | Library | `ECodex.Core` | `net10.0-windows` | System.Management 9.0.3、System.Security.Cryptography.ProtectedData 10.0.0 |
| `ECodex.Daemon` | `WinExe` | `ecodex-daemon` | `net10.0-windows` | — |
| `ECodex.Tests` | Library | `ECodex.Tests` | `net10.0-windows` | xunit 2.9.3、FluentAssertions 7.2.0、Microsoft.NET.Test.Sdk 17.12.0 |
| `ECodex.Smoke` | `Exe` | — | `net10.0-windows` | — |

`ECodex.Core.csproj` 启用 `AllowUnsafeBlocks=true`（ConPty Interop 使用）。

版本号以 `Directory.Build.props` 的 `<Version>` 作为单一事实源；CLI `ecodex version` 与 IPC `STATUS.version` 均读取程序集 `AssemblyInformationalVersion`，并去掉 CI 可能附加的 source revision 后缀。

## 4. 本地开发运行

### 4.1 命令行

```powershell
# 还原 + 编译
dotnet build ECodex.sln -c Debug

# 启动 WPF 主程序
dotnet run --project src/ECodex/ECodex.csproj -c Debug

# 跑单元测试
dotnet test tests/ECodex.Tests/ECodex.Tests.csproj

# 跑 ConPTY 烟雾测试（输出到 %TEMP%/ecodex-smoke.log）
dotnet run --project tests/ECodex.Smoke/ECodex.Smoke.csproj
```

如果 PATH 上的 `dotnet` 不可用，但本地 `.dotnet` 已包含 `10.0.301`，使用显式路径执行同一命令：

```powershell
.\.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj --filter DaemonMessageRoundTripTests --no-restore
.\.dotnet\dotnet.exe build ECodex.sln -c Debug
```

### 4.2 守护进程查找

`DaemonClient.StartDaemonAndConnect` 会按以下顺序查找 `ecodex-daemon.exe`：

1. 当前可执行文件旁（部署/发布场景）
2. 向上查找 `src/` 父目录，再遍历 `src/ECodex.Daemon/bin/{Debug|Release}/<tfm>/ecodex-daemon.exe`（开发构建场景）


### 4.3 本地 CI 入口

`scripts/ci.ps1` 是本地与 PR 前的统一验证入口：

```powershell
pwsh ./scripts/ci.ps1                         # restore + build + unit tests + smoke/publish dry-run gate
pwsh ./scripts/ci.ps1 -Config Release         # Release 配置验证
pwsh ./scripts/ci.ps1 -IncludeSmoke           # Windows 上额外运行 ConPTY smoke test
pwsh ./scripts/ci.ps1 -IncludePublish -PublishFlavor Cli
pwsh ./scripts/check-doc-links.ps1            # 仅检查 README/spec/md Markdown 相对链接
```

默认不执行耗时或强 Windows 依赖的 smoke/publish 实际步骤，只验证脚本语法与门禁提示；发布前应在 Windows 上显式加入 `-IncludeSmoke` 和 `-IncludePublish`。
`scripts/ci.ps1` 会调用 `scripts/check-doc-links.ps1` 做文档链接 gate；需要单独排查文档漂移时可直接运行该脚本，也可传入 `-Path` 扫描指定文件或目录。

### 4.4 Windows Toast live smoke

真实 Windows Toast 点击激活不能由 CI 稳定证明，必须在 Windows 图形会话中手测。发布签收前运行：

```powershell
pwsh ./scripts/smoke-toast-activation.ps1
pwsh ./scripts/smoke-toast-activation.ps1 -Interactive -Cleanup
```

脚本负责检查 Windows-only 前置条件（系统 Toast 权限、专注助手人工提示、开始菜单 / 桌面快捷方式、AppUserModelID 线索、主应用 pipe、CLI 可用性），并用 `ecodex hook event` 生成带 `notificationId/workspaceId/surfaceId/paneId` 的真实通知。默认输出 `manual` JSON，等待维护者点击 Toast 并记录 `toastShown/clickRestoredWindow/paneFocused/fallbackVisible`；缺少权限、快捷方式或主应用时输出清晰 `skipped` / `failed` 原因。

## 5. 发布形态

| 模式 | 命令 | 产物 | 大小 | 依赖 |
|---|---|---|---|---|
| Framework-dependent | `pwsh ./scripts/publish.ps1 -Flavor Framework` | `ecodex-app.exe` + `ecodex-daemon.exe` + 若干 `.dll` | 最小 | 需要 .NET 10 Desktop Runtime |
| Self-contained | `pwsh ./scripts/publish.ps1 -Flavor SelfContained` | `ecodex-app.exe` + `ecodex-daemon.exe` + 自带运行时 | 较大 | 无 |
| Single-file | `… /p:PublishSingleFile=true /p:PublishTrimmed=false` | 单个 `ecodex-app.exe` | 较大 | 无（README 提及，但 `publish.ps1` 已规避 WPF + ConPTY 兼容问题） |
| CLI | `dotnet publish src/ECodex.Cli/ECodex.Cli.csproj -c Release -r win-x64 --self-contained true -o publish/ecodex-cli` | `ecodex.exe` + 自带运行时 | 较大 | 无；放入 `PATH` 即可全局使用 |
| Inno Setup | 先发布 Self-contained + CLI，再用 Inno Setup Compiler 编译 `installer/ecodex.iss` | `publish/inno/ecodex-setup-<version>.exe` | 较大 | 固定简体中文安装 / 卸载向导；卸载只清理 `{app}`，保留 `%USERPROFILE%\.ecodex` 与用户 skills |

### 5.1 一键发布脚本

`scripts/publish.ps1`：

```powershell
pwsh ./scripts/publish.ps1                                      # All / Release / win-x64
pwsh ./scripts/publish.ps1 -Flavor SelfContained                # 仅自包含
pwsh ./scripts/publish.ps1 -Flavor Cli -Rid win-arm64           # 仅 CLI / arm64
pwsh ./scripts/publish.ps1 -Config Debug -Flavor Framework      # Debug 框架依赖
```

支持的运行时：`win-x64 / win-x86 / win-arm64`；支持的产物：`All / Framework / SelfContained / Cli`。

> 脚本在开始前会清理 `src/ECodex{,/Core}/obj` 与 `bin` 目录，避免 WPF 临时 csproj 残留导致 XAML code-behind 字段缺失。

## 6. 部署目录结构

```text
publish/
├── ecodex-win-x64/                # Framework-dependent
│   ├── ecodex-app.exe
│   ├── ecodex-daemon.exe
│   └── *.dll
├── ecodex-win-x64-sc/             # Self-contained
│   ├── ecodex-app.exe
│   ├── ecodex-daemon.exe
│   └── *.dll (+ 运行时文件)
├── ecodex-cli/                    # CLI
│   └── ecodex.exe (+ 运行时文件)
└── inno/                          # Inno Setup fallback installer
    └── ecodex-setup-<version>.exe
```

`%USERPROFILE%/.ecodex/`（运行时生成）：

```text
%USERPROFILE%/.ecodex/
├── session.json                 # 会话状态
├── settings.json                # 全局设置（含 AgentSettings）
├── snippets.json                # 代码片段
├── secrets.json                 # DPAPI 加密密钥
├── daemon-debug.log             # 守护进程 / 客户端诊断日志（FileShare.ReadWrite 共享追加）
├── logs/
│   ├── YYYY-MM-DD.jsonl         # 命令日志（按日）
│   └── terminal/YYYY-MM-DD/*.log# 终端脚本捕获
└── agent/
    ├── threads.json             # Agent 会话线程索引
    └── threads/<id>.jsonl       # 消息 JSONL
```

## 7. 运行时依赖

| 项 | 说明 |
|---|---|
| ConPTY | Windows 10 1809+ 内置 |
| WebView2 | Session Vault 浏览器视图需要（仅在使用 Session Vault 时） |
| .NET 10 Desktop Runtime | Framework-dependent 模式必需 |
| Windows Toast | Windows 10+ 系统支持 |

## 8. 进程与权限

| 进程 | 单实例保护 | 备注 |
|---|---|---|
| `ecodex-daemon.exe` | `Global\ECodexDaemon` 命名互斥体 | 二次启动立即退出 |
| `ecodex-app.exe` | AppUserModelID `ECodex.App` + `Global\ECodexMainApp` 命名互斥体 | 安装器快捷方式 / 任务栏固定图标应激活已有窗口；裸 exe 重复启动由 mutex 兜底发送 `window.focus` 后退出 |
| `ecodex.exe` (CLI) | 无 | 一次性进程 |

权限要求：

- 所有进程以当前用户身份运行（命名管道默认 ACL 限于当前用户）
- DPAPI 使用 `DataProtectionScope.CurrentUser`，跨用户不可解密
- `netstat` / WMI 调用依赖本地系统能力（无需管理员）

## 9. 故障排查

| 现象 | 排查方向 |
|---|---|
| 启动后立即崩溃 | 检查 `%USERPROFILE%/.ecodex/daemon-debug.log`；`ecodex-app.exe`（`ECodex` 主程序）的全局异常提示 |
| CLI 连不上 | 确认 ecodex-app.exe 在运行；`\\.\pipe\ecodex` 是否被占用；看 daemon-debug.log |
| 守护进程频繁退出 | 看是否 24 小时空闲自动退出；或者 `Global\ECodexDaemon` 互斥体冲突 |
| 终端无输出 | ConPTY 兼容性问题；先看 `tests/ECodex.Smoke` 是否通过 |
| 会话无法恢复 | 检查 `session.json` 版本号（`version=1`）；损坏会回退到默认项目 |
| 字体乱码 | `ECodexSettings.FontFamily` 是否安装；查看 `TerminalThemes.GetEffective` 主题覆盖 |

## 10. 测试

### 10.1 单元测试

`tests/ECodex.Tests/CoreTests.cs`：

- VtParser（可打印字符 / C0 控制符 / CSI / OSC / UTF-8 多字节）
- TerminalBuffer（滚动 / 备用屏幕 / 擦除 / 快照）
- SplitNode（拆 / 删 / 找 / 等分）
- OscHandler / VtParser 集成通知检测
- TerminalThemes（自定义颜色叠加、hex 解析）
- SessionPersistenceService / SnippetService / CommandLogService / SecretStoreService / GitService / PortScanner / NotificationService 行为验证

### 10.2 烟雾测试

`tests/ECodex.Smoke/Program.cs`：

- FreeConsole 后启动 TerminalSession（120×30），捕获 ProcessId，3 秒后再确认存活
- 直接读 ConPTY ReadPipe 2 秒，确认能拿到原始字节
- 输出到 `%TEMP%/ecodex-smoke.log`，按 PASS/FAIL 计数

用于在 PR/发布前快速验证 ConPTY 在目标 Windows 上的兼容性。

## 11. 已知构建约束

- **WPF + ConPTY 与 PublishSingleFile 配合不佳**：`scripts/publish.ps1` 因此不生成单文件形态；README 中保留为可选第三条命令。
- **ECodex.Core 启用 unsafe 代码**：ConPty Interop 需要；下游引用须遵守。
- **TreatWarningsAsErrors=true**：所有项目必须零警告。引入新包或 IDE 警告会立刻阻塞构建。
