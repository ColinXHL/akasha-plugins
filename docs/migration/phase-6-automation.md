# Phase 6 automation migration

`akasha-genshin-automation` 已从独立仓库迁入
`plugins/akasha-genshin-automation/`。

## Source snapshot

- 旧仓库：`https://github.com/ColinXHL/akasha-automation`
- 远端基线：`e67c82d`
- 迁移快照：`e5e7ca5`
- 一并保留的本地提交：
  - `ebb5c8f`：稳定游戏窗口帧采集；
  - `e5e7ca5`：发布 0.4.3 包元数据。

迁移快照中的前端版本仍为 `0.4.3`，companion protocol 仍为 v1。

## New ownership

- `frontend/`：JS 入口和设置界面；
- `backend/`：独立 solution、源码、测试、固定 BetterGI 资产和开发工具；
- `packaging/`：完整插件包构建与校验；
- `manifest.json`：客户端和 Release 流水线共同使用的 Manifest v2；
- `DERIVATION.md`、`THIRD_PARTY_NOTICES.md`、`backend/upstream/`：
  BetterGI 来源、哈希和许可边界。

Worker 不引用 AkashaNavigator。宿主与 Worker 只通过 companion protocol v1
通信。

## Excluded generated content

迁移不包含 `bin/`、`obj/`、`artifacts/`、`publish/`、旧 Release ZIP、
本地日志、缓存或临时实机数据。Worker 二进制只在 Release Workflow 中生成。

## Publication boundary

源码 manifest 不保存自引用的 ZIP SHA-256。发布流程完成测试、Worker publish、
ZIP 复验、GitHub/CNB Release 和公开资源回读后，才把真实大小与 SHA-256
作为 catalog staging 覆盖写入 `catalog` 分支。

普通 catalog 发布遇到尚未发布的新 Release 版本时，会继续保留上一个已验证
版本；首次尚未发布时则不暴露该插件。
