# 描述

<!-- 简述本次 PR 的目的、对应 backlog ID、影响的 spec 章节。 -->

- Backlog ID：`M?-?-??`（参见 `spec/07-implementation-backlog.md`）
- 关联 spec：<!-- 形如 spec/03-data-and-ipc.md §2.3 -->
- 关联 issue：<!-- 形如 #123；无则填 N/A -->
- 一句话目标：<!-- 用一句话写清本次合并后用户能做什么 / 系统会怎样变化 -->

## 改动类型

- [ ] 新功能（feature）
- [ ] 缺陷修复（bugfix）
- [ ] 重构（refactor，无行为变化）
- [ ] 性能优化
- [ ] 文档（spec / md / README）
- [ ] 测试
- [ ] 发布 / CI / 脚本
- [ ] 破坏性变更（breaking change）

## 改动摘要

- 关键文件与新增 / 修改的 public 表面（类、方法、IPC 命令、CLI 命令、ecodex.json 字段、SQL/JSON schema）。
- 数据 / 协议影响：`\\.\pipe\ecodex`、`\\.\pipe\ecodex-daemon`、`%USERPROFILE%\.ecodex\*.json`、ecodex.json。
- 持久化兼容：旧 `session.json` / `settings.json` / `resume.json` 能否继续工作？是否涉及兼容开关或迁移说明？

## 测试

- [ ] `.\.dotnet\dotnet.exe build ECodex.sln -c Debug -p:NuGetAudit=false` 零警告。
- [ ] `.\.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj -p:NuGetAudit=false` 全绿。
- [ ] `npm run docs:build`（涉及 md / README / spec 链接时）。
- [ ] 涉及 UI：附截图或短录屏。
- [ ] 涉及 IPC / CLI / ecodex.json：附 contract 测试或手测脚本。
- [ ] 涉及 ConPTY：附 `tests/ECodex.Smoke` 输出。
- [ ] 涉及 WebView2：标注为 Windows-only integration 或已在 CI 跳过。

## spec / 文档同步

- [ ] 已更新 `spec/` 至少一篇文档。
- [ ] 已更新 `CHANGELOG.md`（用户可读条目）。
- [ ] 如为新功能：已补 backlog 行（ID 与本 PR 一致）。
- [ ] 如破坏 v1 CLI / IPC：已写明迁移路径与替代命令。

## 风险与回滚

- 触发场景与缓解（例如：M2 binding 反序列化失败 → 新增默认字段）。
- 回滚步骤（`git revert <sha>`、开关、设置项、feature flag）。
- 是否需要新版 README / 安装器 / 数据库 schema 同步。

## 评审关注点

- 列出希望评审者重点关注的位置（敏感字段、并发、IPC 死锁、性能、locale、路径处理）。

---

### Checklist（自检）

- [ ] 我已阅读 `spec/README.md` 并更新了对应章节
- [ ] 我已确认本 PR 不跨超过 1 个里程碑（否则先拆）
- [ ] 我已避免一次性大改（Core / UI / CLI / 测试分 PR）
- [ ] 我已脱敏所有日志、示例与截图中的密钥
