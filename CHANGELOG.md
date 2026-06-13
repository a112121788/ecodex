# Changelog

ECode 的用户可读变更记录。维护规则参见 `spec/06-roadmap.md` §3.2 与 `spec/07-implementation-backlog.md`。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)；roadmap 规划版本线从 `0.1.0` 起步（详见 `spec/06-roadmap.md` §3.1）。

> 下一节“未发布”条目将在每次 PR 合并后由维护者补全；建议结合 `git-cliff` 或 GitHub 自动 release-drafter 生成 release notes（见 backlog `M6-B-06`）。

## [Unreleased]

### Added

- 新增 `scripts/ci.ps1` 本地 CI 入口，串联 restore/build/test，并对 smoke/publish 提供显式开关与 dry-run gate。
- GitHub Actions 改为复用本地 CI 入口，减少本地验证与远端 CI 的命令漂移。
- 新增 daemon IPC DTO roundtrip 测试，覆盖 request、response、session info 与 event 序列化兼容性。
- 新增命令日志脱敏测试，覆盖环境变量、命令行 flag、URI credential 与独立密钥输入。
- 新增 VT parser 回归测试，覆盖 OSC ST 终止、UTF-8 跨包、无效 UTF-8 恢复与 CAN 取消 CSI。
- 新增 SplitNode 布局测试，覆盖嵌套移除、焦点循环、预设布局、等分、resize clamp 与 pane swap。
- 新增 `ecode reload-config` 与 `Ctrl+Shift+,`，可热重载 `ecode.json` 并刷新已打开的命令面板。
- 标准化 daemon debug log 字段，统一输出 `ts/component/event/paneId`，便于 grep 串联 attach 与请求流程。
- 当前 active surface tab 的关闭按钮改为常显，非 active tab 仍保持 hover 显示。
- 新增 `ResumeBinding` / `ResumeBindingFile` DTO，为 M2 会话恢复增强的 `resume.json` 打基础。
- 新增 `ResumeBindingService`，支持 `resume.json` 的加载、保存、增删、按 Surface 查询与信任前缀更新。
- `ResumeBindingService` 保存 `resume.json` 前会剔除 TOKEN、PASSWORD、SECRET、API_KEY 等敏感环境变量。

### Changed

- 统一 CLI `ecode version` 与 IPC `STATUS.version` 的版本读取逻辑，均使用程序集 informational version，并去掉 source revision 后缀。

## [0.2.0] - 2026-06-12

### Removed

- 移除项目右键菜单中的“设置项目图标”功能。
- 移除项目右键菜单中的“设置项目主题色”功能。

## [0.1.0] - 2026-06-12

### Breaking changes

- 项目品牌与代码标识统一为 **ECode**（旧称 `cmux-windows`），覆盖以下外部契约：
  - 主程序：`cmuxw.exe` → `ecode-app.exe`
  - CLI：`cmux.exe` → `ecode.exe`
  - 守护进程：`cmux-daemon.exe` → `ecode-daemon.exe`
  - 解决方案：`Cmux.sln` → `ECode.sln`
  - C# 根命名空间：`Cmux.*` → `ECode.*`；类名 `CmuxSettings` → `ECodeSettings`
  - MCP / Agent 工具名：`cmux_status` / `cmux_pane_*` / `cmux_workspace_*` / `cmux_surface_*` / `cmux_split_*` / `cmux_notify` / `cmux_scaffold_agents_files` → `ecode_*`
- 命名管道与互斥体：
  - `\\.\pipe\cmux`、`\\.\pipe\cmux-{tag}` → `\\.\pipe\ecode`、`\\.\pipe\ecode-{tag}`
  - `\\.\pipe\cmux-daemon` → `\\.\pipe\ecode-daemon`
  - `Global\CmuxDaemon` → `Global\ECodeDaemon`
- 数据目录与配置文件：
  - `%LOCALAPPDATA%\cmux\` / `%LOCALAPPDATA%\ecode\` → `%USERPROFILE%\.ecode\`
  - `.cmux/cmux.json` / `%USERPROFILE%\.config\cmux\cmux.json` → `.ecode/ecode.json` / `%USERPROFILE%\.config\ecode\ecode.json`
  - CI artifact 名称：`cmux-windows-x64` → `ecode-windows-x64`、`cmux-cli-windows-x64` → `ecode-cli-windows-x64`
- 守护进程日志前缀：`[cmux-daemon]` → `[ecode-daemon]`。
- 主程序集版本起点：`0.2.0` → `0.1.0`（按 roadmap 重启版本线）。

### Compatibility

- 本期保留对旧接口 / 旧配置的 **读取兼容**：
  - 命名管道客户端若旧 `cmux` / `cmux-daemon` 管道存在，仍可正常通信。
  - 命令面板解析仍支持 `.cmux/cmux.json`（`M1-C` 阶段可关闭兼容；详见 spec/06-roadmap.md §6.3）。
- 运行时数据目录不做旧路径兼容：新版只读写 `%USERPROFILE%\.ecode\`，不会自动读取或迁移 `%LOCALAPPDATA%\ecode\` / `%LOCALAPPDATA%\cmux\`。
- 旧 `cmux_*` 工具名在 CLI 顶层命令里保留为薄封装 1 个小版本周期，之后下线。

### Migration

1. 升级到 `0.1.0` 之前，请停止旧版 `cmuxw.exe` / `ecode-app.exe`。
2. 卸载旧安装器；删除 `C:\Program Files\ECode` 旧安装目录（若已存在）。
3. 安装新版 `0.1.x`（`ecode-setup.exe` 或 `ecode-cli`）后，运行时数据只写入 `%USERPROFILE%\.ecode\`；旧 `%LOCALAPPDATA%\ecode\` / `%LOCALAPPDATA%\cmux\` 不再自动读取或迁移。
4. 自动化脚本中的 `cmux status` 等命令在 CLI 顶层继续可运行，但建议改写为 `ecode ...`。
5. Agent 集成方需更新 MCP 工具调用名为 `ecode_*`。

### Known issues

- 资产（`assets/screenshots/*.jpg`）的 alt 文本可能仍含“cmux”水印（计划在 `M7-A` 文档站同步刷新）。
- 旧注册表键（`HKCU\Software\Cmux\...`，计划 M6 hooks setup 时落地）将在 M6 阶段统一。

---

<!-- 之后逐版本补全（roadmap 版本线）：0.1.x、0.2.x、0.3.x、0.4.x、0.5.x、0.6.x、0.7.x、1.0.0 -->
