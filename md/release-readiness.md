# 发布就绪

本页记录 1.0 线的 M7-C-01 质量门槛。发布前维护者必须更新本页，并确认 GitHub Release 可安全发布。

## 缺陷门槛

- P0 缺陷数量必须为 0。
- P1 缺陷数量必须不超过 3。
- 每个打开的 P1 必须记录可执行的规避方案（workaround）。
- 崩溃、数据丢失、静默命令执行、不可恢复状态默认按 P0 处理。
- 核心功能不可用但已有规避方案的问题按 P1 处理。

## 当前快照

快照日期：2026-06-15

来源：本地 backlog/spec 审计、当前验证套件，以及 2026-06-15 对 GitHub 打开状态 `p0` / `p1` label 的检查。未来发布前需要重新检查公开 issue，并把阻塞项同步到本页。

| 严重度 | 打开数量 | 上限 | 状态 | 规避方案状态 |
|---|---:|---:|---|---|
| P0 | 0 | 0 | 通过 | N/A |
| P1 | 0 | 3 | 通过 | N/A；当前无仓库跟踪的 P1 阻塞项 |

## 阻塞项台账

| ID | 严重度 | 范围 | 状态 | 规避方案 |
|---|---|---|---|---|
| 无 | P0/P1 | terminal/layout/notification/session restore/ecodex.json/browser/v2 命令行/install/update | 当前无发布阻塞项 | N/A |

## 必跑验证

发布 tag 前运行：

```powershell
npm run docs:build
.\.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj -p:NuGetAudit=false
.\.dotnet\dotnet.exe build ECodex.sln -c Debug -p:NuGetAudit=false
```

发布产物 workflow 还会上传 `ecodex-perf-report`，本地可运行：

```powershell
.\scripts\perf\measure.ps1 -OutputDir artifacts\perf -Samples 1
```

发布候选版本额外运行：

```powershell
.\scripts\ci.ps1 -Config Release -IncludeSmoke
```

## 分诊流程

1. 关键 issue 同时标记 `bug` 与 `p0` / `p1`。
2. P0 必须分配负责人，并在修复和测试前阻塞发布。
3. 每个 P1 都要把规避方案写入 issue，并同步到阻塞项台账。
4. P1 数量超过 3 时，推迟发布；只有确认不阻塞核心工作流后才能降级。
5. 修复落地后更新 `CHANGELOG.md`、本页和 backlog 行。

## 发布前重查范围

- Terminal 创建、ConPTY attach、scrollback、输入。
- Workspace / Surface / Pane 布局持久化与通知跳转。
- `ecodex.json` 加载、合并、命令面板展示、reload diagnostics。
- Session restore、trusted resume bindings、敏感环境剔除。
- 集成浏览器创建与 `ecodex browser` snapshot/click/fill/eval。
- Installer、setup、doctor、updater、uninstall 数据保留策略。
