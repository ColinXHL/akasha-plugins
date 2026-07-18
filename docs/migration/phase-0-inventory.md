# Phase 0 盘点与迁移冻结

## 官方仓库

| 角色 | 地址 | 权威内容 |
| --- | --- | --- |
| 主程序 | `https://github.com/ColinXHL/AkashaNavigator` | 宿主、客户端、安装器 |
| 插件聚合仓库 | `https://github.com/ColinXHL/akasha-plugins` | 插件源码、catalog、Release |
| 插件 CNB 镜像 | `https://cnb.cool/AkashaNavigator/akasha-plugins` | 相同 catalog 与 Release 镜像 |
| 旧自动化仓库 | `https://github.com/ColinXHL/akasha-automation` | 迁移输入，Phase 6 后归档 |

## 插件 ID 与版本

插件 ID 在迁移后保持不变。不得使用目录显示名称或旧 Registry 推断新 ID。
机器可读盘点位于
[`phase-0-plugin-inventory.json`](phase-0-plugin-inventory.json)。

| 插件 ID | 当前源码 | 迁移基准版本 | 目标分发 |
| --- | --- | --- | --- |
| `bilibili-page-list` | `AkashaNavigator/repo/plugins/bilibili-page-list` | `1.2.1` | `repository` |
| `genshin-direction-marker` | `AkashaNavigator/repo/plugins/genshin-direction-marker` | `1.1.0` | `repository` |
| `smart-cursor-detection` | `AkashaNavigator/repo/plugins/smart-cursor-detection` | `1.0.0` | `repository` |
| `akasha-genshin-automation` | `akasha-automation/plugin/akasha-genshin-automation` | `0.4.3` | `release` |

### 已知冲突

`AkashaNavigator/repo/plugins/registry.json` 将 `genshin-direction-marker` 标记为
`2.0.0`，但实际随包发布的 `plugin.json` 为 `1.1.0`。迁移以插件自身清单
`1.1.0` 为准；`2.0.0` 视为旧硬编码 Registry 的错误数据，不得写入 catalog。

## 旧清单与目录映射

普通插件：

```text
AkashaNavigator/repo/plugins/<id>/
    -> AkashaPlugins/plugins/<id>/
```

自动化插件：

```text
akasha-automation/plugin/akasha-genshin-automation/
    -> AkashaPlugins/plugins/akasha-genshin-automation/frontend/

akasha-automation/src, tests, build, scripts
    -> AkashaPlugins/plugins/akasha-genshin-automation/backend/
```

自动化的 `bin`、`obj`、`artifacts`、`publish`、历史 ZIP、日志和本地模型缓存不迁移。
`LICENSE`、`DERIVATION.md` 和 `THIRD_PARTY_NOTICES.md` 必须随源码与 Release 包保留。

## 用户数据迁移

以下数据是迁移输入，任何仓库更新或订阅转换都不得删除：

| 当前数据 | 新职责 |
| --- | --- |
| `User/Data/InstalledPlugins/<id>/` | 保留为已安装插件目录 |
| `User/Data/associations.json` | 保留 Profile 与插件启用状态 |
| `User/Data/subscriptions.json` | 读取旧 Profile/插件订阅，转换后保留兼容备份 |
| `User/Data/Profiles/<profile>/plugins/<id>/` | 保留旧版 Profile 插件配置和资源 |
| `User/Data/PluginResources/<id>/` | 保留 Worker/插件运行时数据 |
| `User/Data/library.json` | 读取旧安装来源，转换为仓库订阅记录 |

新增 `plugin-subscriptions.json` 时，至少记录：

```json
{
  "repositoryId": "official",
  "pluginId": "genshin-direction-marker",
  "repositoryPath": "plugins/genshin-direction-marker",
  "installedVersion": "1.1.0",
  "installedCommit": "<local catalog HEAD>",
  "autoUpdate": false
}
```

默认迁移策略：

1. 通过稳定插件 ID 关联现有安装、Profile 和配置；
2. 旧内置插件被识别为 `official` 仓库订阅；
3. 自动化旧安装被识别为 `akasha-genshin-automation`；
4. 无法唯一识别的目录只提示，不自动删除或合并；
5. 首次迁移默认不擅自开启插件自动更新；
6. 迁移写入失败时保持旧文件和当前可用插件不变。

## 冻结结论

- Manifest 协议：v2；
- Repository index schema：v1；
- Companion protocol：v1；
- 最低目标宿主版本：`1.4.0`；
- 官方仓库 ID：`official`；
- GitHub 是源码发布权威源，CNB 是同一逻辑仓库的镜像；
- 插件版本是前端、Worker、模型和协议适配层的统一发布版本。
